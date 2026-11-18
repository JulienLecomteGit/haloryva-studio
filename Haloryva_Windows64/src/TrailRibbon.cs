using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CursorFlow
{
    internal sealed class TrailRibbonOptions
    {
        internal int Width = 8, Life = 269, Opacity = 210, Smoothness = 65;
        internal bool SpeedResponsive = true, PerformanceMode, Glow;
        internal Color FirstColor = Color.Cyan, SecondColor = Color.MediumPurple;
        internal string ColorMode = "Unie";
    }

    internal struct TrailRibbonPoint
    {
        internal PointF Point;
        internal PointF Tangent;
        internal DateTime Time;
        internal float HalfWidth, Alpha, Red, Green, Blue;
        internal bool BreakBefore;
    }

    // A connected strip is rasterized to a small, reusable PArgb surface. Each
    // layer keeps the maximum coverage at a pixel, never source-over per segment:
    // shared edges and crossings cannot accumulate bright beads. Only the final
    // complete layers are composited, then GDI+ copies the finished ribbon once.
    internal sealed class TrailRibbon : IDisposable
    {
        internal const int MaxPoints = 4096;
        internal const int QualityPixelLimit = 524288, PerformancePixelLimit = 262144;
        internal const long QualityWorkLimit = 3000000L, PerformanceWorkLimit = 1500000L;
        internal const int MaximumRasterPasses = 3;
        internal const double MaxRasterScale = 4.0;
        internal const int MaxSubdivisionDepth = 10;
        internal const int MaxTessellationChecks = MaxPoints * 2;
        internal const int MaxResolutionChecks = 7;
        internal const double QualityGeometryTolerance = .25, PerformanceGeometryTolerance = .5;
        private static readonly double[] ResolutionLevels = { 1, 1.25, 1.5, 2, 3, 4 };
        private const double ResolutionHoldMs = 180;
        private const int CapSteps = 12;

        private readonly List<TrailRibbonPoint> points = new List<TrailRibbonPoint>(2048);
        private readonly IList<TrailRibbonPoint> pointView;
        private readonly List<TrailDot> controls = new List<TrailDot>(1536);
        private readonly List<Vertex> vertices = new List<Vertex>(8192);
        private readonly List<Triangle> triangles = new List<Triangle>(8192);
        private readonly List<Vertex> projected = new List<Vertex>(8192);
        private int[] wide, soft, ink, core, output;
        private int[] rowFirst, rowLast;
        // The historical dense compositor is retained only as an independent
        // pixel reference for the native self-tests. Production uses spans.
        private readonly bool denseCompositeReference;
        private Bitmap bitmap;
        private int capacityWidth, capacityHeight, rasterWidth, rasterHeight;
        private double scale, originX, originY;
        private long visits, budget;
        private bool disposed;
        private readonly CurvePiece[] pieces = new CurvePiece[MaxTessellationChecks];
        private readonly int[] roots = new int[MaxPoints], heap = new int[MaxPoints];
        private int pieceCount, heapCount;
        private DateTime buildTime, finerSince = DateTime.MinValue, lastFrameTime = DateTime.MinValue;
        private TrailRibbonOptions buildOptions;
        private int buildLife, retainedResolution = -1, finerCandidate = -1;
        private bool previousPerformance;
        private double edgeFactor, tolerance;

        internal TrailRibbon() : this(false) { }
        internal TrailRibbon(bool denseCompositeReference)
        { this.denseCompositeReference = denseCompositeReference; pointView = points.AsReadOnly(); }
        internal bool MeasureCosts { get; set; }
        internal RibbonStageTiming LastStageTiming { get; private set; }
        internal long LastCompositePixels { get; private set; }
        internal long LastBitmapCopiedPixels { get; private set; }
        internal int LastBitmapCopyCalls { get; private set; }
        internal IList<TrailRibbonPoint> Points { get { return pointView; } }
        internal int LastPointCount { get { return points.Count; } }
        internal int LastTriangleCount { get { return triangles.Count; } }
        internal int LastRasterWidth { get; private set; }
        internal int LastRasterHeight { get; private set; }
        internal long LastPixelVisits { get; private set; }
        internal long LastTotalPixelVisits { get; private set; }
        internal long LastPixelBudget { get; private set; }
        internal int LastRasterPasses { get; private set; }
        internal double LastScale { get; private set; }
        internal bool LastDegraded { get; private set; }
        internal bool LastFallbackPath { get; private set; }
        internal RectangleF LastBounds { get; private set; }
        internal double LastGeometryError { get; private set; }
        internal bool LastGeometryLimited { get; private set; }
        internal int LastTessellationChecks { get; private set; }
        internal int LastPreflightChecks { get; private set; }
        internal bool LastResolutionChanged { get; private set; }
        internal PointF LastRasterOrigin { get; private set; }

        private struct CurvePiece
        {
            internal PointF A, C1, C2, B;
            internal double From, To, GeometryError, Score;
            internal int Source, Depth, Left, Right;
        }

        private struct Vertex
        {
            internal float X, Y, CenterX, CenterY, Radius, Alpha, Red, Green, Blue;
        }
        private struct Triangle
        {
            internal int A, B, C;
            internal Triangle(int a, int b, int c) { A = a; B = b; C = c; }
        }

        internal void Draw(Graphics graphics, DateTime now, TrailRibbonOptions options, IList<TrailDot> dots)
        {
            if (disposed) throw new ObjectDisposedException("TrailRibbon");
            if (graphics == null || options == null || dots == null) throw new ArgumentNullException();
            bool measured = MeasureCosts;
            long totalStarted = RenderStageClock.Start(measured);
            long stage = totalStarted;
            RibbonStageTiming timing = new RibbonStageTiming { Active = measured };
            try
            {
                LastCompositePixels = LastBitmapCopiedPixels = 0; LastBitmapCopyCalls = 0;
                LastRasterWidth = LastRasterHeight = LastRasterPasses = 0;
                LastPixelVisits = LastTotalPixelVisits = LastPixelBudget = 0; LastScale = 1;
                LastDegraded = LastFallbackPath = false; LastBounds = RectangleF.Empty;
                LastGeometryError = 0; LastGeometryLimited = LastResolutionChanged = false;
                LastTessellationChecks = LastPreflightChecks = 0; LastRasterOrigin = PointF.Empty;
                vertices.Clear(); triangles.Clear(); projected.Clear();
                BuildPoints(now, options, dots);
                timing.GeometryMs = RenderStageClock.Elapsed(stage);
                if (points.Count == 0)
                { retainedResolution = -1; finerCandidate = -1; finerSince = DateTime.MinValue; return; }

                stage = RenderStageClock.Start(measured);
                float factor = options.Glow ? (options.PerformanceMode ? 3.6f : 7f) : 1f;
                float left = Single.MaxValue, top = Single.MaxValue, right = Single.MinValue, bottom = Single.MinValue;
                foreach (TrailRibbonPoint point in points)
                {
                    // Miter length is capped at two radii. This includes every vertex,
                    // cap and the antialias fringe before clipping to the destination.
                    float pad = point.HalfWidth * factor * 2 + 16;
                    left = Math.Min(left, point.Point.X - pad); right = Math.Max(right, point.Point.X + pad);
                    top = Math.Min(top, point.Point.Y - pad); bottom = Math.Max(bottom, point.Point.Y + pad);
                }
                RectangleF bounds = RectangleF.Intersect(RectangleF.FromLTRB(left, top, right, bottom), graphics.VisibleClipBounds);
                if (bounds.Width <= 0 || bounds.Height <= 0)
                { timing.ResolutionMs = RenderStageClock.Elapsed(stage); return; }
                int pixelLimit = options.PerformanceMode ? PerformancePixelLimit : QualityPixelLimit;
                budget = options.PerformanceMode ? PerformanceWorkLimit : QualityWorkLimit;
                LastPixelBudget = budget;
                bool resolutionFits = PrepareResolution(bounds, factor, pixelLimit, now, options.PerformanceMode);
                timing.ResolutionMs = RenderStageClock.Elapsed(stage);
                if (!resolutionFits)
                {
                    stage = RenderStageClock.Start(measured);
                    DrawFallback(graphics, options, bounds);
                    timing.FallbackMs = RenderStageClock.Elapsed(stage);
                    return;
                }

                // A defensive rerasterization discards an incomplete PRIVATE buffer.
                // The destination only receives a complete image. Resolution never
                // drops below 1/4: if a complete raster does not fit, GDI+ draws the
                // full geometric path with simpler attributes, not a stretched pixel.
                for (int pass = 0; pass < MaximumRasterPasses; pass++)
                {
                    if (pass > 0)
                    {
                        if (retainedResolution + 1 >= ResolutionLevels.Length) break;
                        stage = RenderStageClock.Start(measured);
                        retainedResolution++; LastResolutionChanged = true; finerCandidate = -1;
                        SetRasterGrid(bounds, ResolutionLevels[retainedResolution]);
                        BuildMesh(factor, scale); ProjectAndEstimate();
                        timing.ResolutionMs += RenderStageClock.Elapsed(stage);
                    }
                    if ((long)rasterWidth * rasterHeight > pixelLimit) break;
                    stage = RenderStageClock.Start(measured);
                    EnsureSurface(rasterWidth, rasterHeight, pixelLimit);
                    int used = capacityWidth * rasterHeight;
                    Array.Clear(wide, 0, used); Array.Clear(soft, 0, used);
                    Array.Clear(ink, 0, used); Array.Clear(core, 0, used);
                    for (int y = 0; y < rasterHeight; y++)
                    { rowFirst[y] = rasterWidth; rowLast[y] = -1; }
                    timing.BuffersMs += RenderStageClock.Elapsed(stage);
                    stage = RenderStageClock.Start(measured);
                    visits = 0;
                    bool complete = true;
                    foreach (Triangle triangle in triangles)
                    {
                        if (!RasterTriangle(projected[triangle.A], projected[triangle.B], projected[triangle.C], options))
                        { complete = false; break; }
                    }
                    timing.RasterMs += RenderStageClock.Elapsed(stage);
                    LastRasterPasses++; LastTotalPixelVisits += visits;
                    if (!complete) continue;
                    LastPixelVisits = visits;
                    Composite(options, ref timing);
                    LastRasterWidth = rasterWidth; LastRasterHeight = rasterHeight;
                    LastScale = scale; LastDegraded = scale > 1.0001 || pass > 0;
                    LastRasterOrigin = new PointF((float)originX, (float)originY);
                    LastBounds = new RectangleF((float)originX, (float)originY,
                        (float)(rasterWidth * scale), (float)(rasterHeight * scale));
                    stage = RenderStageClock.Start(measured);
                    GraphicsState saved = graphics.Save();
                    try
                    {
                        graphics.CompositingMode = CompositingMode.SourceOver;
                        graphics.InterpolationMode = scale > 1.0001 ? InterpolationMode.HighQualityBilinear : InterpolationMode.NearestNeighbor;
                        graphics.PixelOffsetMode = PixelOffsetMode.Half;
                        graphics.DrawImage(bitmap, LastBounds, new RectangleF(0, 0, rasterWidth, rasterHeight), GraphicsUnit.Pixel);
                    }
                    finally { graphics.Restore(saved); }
                    timing.BlitMs = RenderStageClock.Elapsed(stage);
                    return;
                }
                stage = RenderStageClock.Start(measured);
                DrawFallback(graphics, options, bounds);
                timing.FallbackMs = RenderStageClock.Elapsed(stage);
            }
            finally
            {
                timing.TotalMs = RenderStageClock.Elapsed(totalStarted);
                LastStageTiming = timing;
            }
        }

        private void SetRasterGrid(RectangleF bounds, double level)
        {
            scale = level;
            // A fixed world-space phase avoids changing the sampling grid each
            // time a moving tail changes its bounding rectangle by one pixel.
            originX = Math.Floor(bounds.Left / scale) * scale;
            originY = Math.Floor(bounds.Top / scale) * scale;
            rasterWidth = Math.Max(1, (int)Math.Ceiling((bounds.Right - originX) / scale));
            rasterHeight = Math.Max(1, (int)Math.Ceiling((bounds.Bottom - originY) / scale));
        }

        private bool PrepareResolution(RectangleF bounds, float factor, int pixelLimit, DateTime now, bool performance)
        {
            if (lastFrameTime == DateTime.MinValue || now < lastFrameTime || performance != previousPerformance)
            { retainedResolution = -1; finerCandidate = -1; finerSince = DateTime.MinValue; }
            lastFrameTime = now; previousPerformance = performance;
            int best = -1;
            double bestWork = 0, bestPixels = 0;
            for (int i = 0; i < ResolutionLevels.Length; i++)
            {
                LastPreflightChecks++;
                SetRasterGrid(bounds, ResolutionLevels[i]);
                double pixels = (double)rasterWidth * rasterHeight;
                if (pixels > pixelLimit) continue;
                BuildMesh(factor, scale);
                double work = ProjectAndEstimate();
                if (work <= budget) { best = i; bestPixels = pixels; bestWork = work; break; }
            }
            if (best < 0) { finerCandidate = -1; return false; }
            int chosen = best;
            if (retainedResolution >= 0 && best < retainedResolution)
            {
                chosen = retainedResolution;
                // Coarsening is immediate to respect the hard budgets. Returning
                // to finer pixels requires a 22% margin for 180 monotone ms.
                if (bestPixels <= pixelLimit * .78 && bestWork <= budget * .78)
                {
                    if (finerCandidate != best) { finerCandidate = best; finerSince = now; }
                    if ((now - finerSince).TotalMilliseconds >= ResolutionHoldMs)
                    { chosen = best; finerCandidate = -1; }
                }
                else finerCandidate = -1;
            }
            else finerCandidate = -1;
            LastResolutionChanged = retainedResolution >= 0 && chosen != retainedResolution;
            retainedResolution = chosen;
            if (chosen != best)
            {
                LastPreflightChecks++;
                SetRasterGrid(bounds, ResolutionLevels[chosen]); BuildMesh(factor, scale);
                if ((long)rasterWidth * rasterHeight > pixelLimit || ProjectAndEstimate() > budget) return false;
            }
            return true;
        }

        private void DrawFallback(Graphics graphics, TrailRibbonOptions options, RectangleF bounds)
        {
            LastFallbackPath = LastDegraded = true; LastScale = 1;
            LastRasterWidth = LastRasterHeight = 0; LastPixelVisits = 0;
            LastBounds = bounds;
            vertices.Clear(); triangles.Clear(); projected.Clear();
            // At most 2*MaxPoints vertices and four whole-path calls. This escape
            // route preserves every portion and gap. Its global width, opacity
            // and color are deliberately simpler; GDI+ CPU time is not claimed
            // to obey the managed pixel-work budget.
            using (GraphicsPath path = new GraphicsPath())
            {
                int start = 0;
                while (start < points.Count)
                {
                    int end = start + 1;
                    while (end < points.Count && !points[end].BreakBefore) end++;
                    if (end - start >= 2)
                    {
                        PointF[] line = new PointF[end - start];
                        for (int i = start; i < end; i++) line[i - start] = points[i].Point;
                        path.StartFigure(); path.AddLines(line);
                    }
                    else
                    {
                        // A final endpoint can outlive its penultimate sample.
                        // The tiny round-capped segment retains that fading dot.
                        PointF point = points[start].Point;
                        path.StartFigure(); path.AddLine(point, new PointF(point.X + .01f, point.Y));
                    }
                    start = end;
                }
                if (path.PointCount == 0) return;
                double alpha = 0, width = 0, red = 0, green = 0, blue = 0;
                foreach (TrailRibbonPoint point in points)
                {
                    alpha += point.Alpha; width += point.HalfWidth * 2;
                    red += point.Red; green += point.Green; blue += point.Blue;
                }
                double divisor = Math.Max(1, points.Count);
                alpha /= divisor; width /= divisor;
                Color color = Color.FromArgb(Byte(red / divisor), Byte(green / divisor), Byte(blue / divisor));
                GraphicsState saved = graphics.Save();
                try
                {
                    graphics.CompositingMode = CompositingMode.SourceOver;
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    if (options.Glow)
                    {
                        if (!options.PerformanceMode) DrawFallbackLayer(graphics, path, color, alpha * .10, width * 7);
                        DrawFallbackLayer(graphics, path, color, alpha * .25, width * 3.6);
                    }
                    DrawFallbackLayer(graphics, path, color, alpha, width);
                    if (options.Glow)
                        DrawFallbackLayer(graphics, path, Mix(color, Color.White, .75), alpha * .85, Math.Max(1, width * .38));
                }
                finally { graphics.Restore(saved); }
            }
        }

        private static void DrawFallbackLayer(Graphics graphics, GraphicsPath path, Color color, double alpha, double width)
        {
            using (Pen pen = new Pen(Color.FromArgb(Byte(alpha), color), (float)Math.Max(1, width)))
            {
                pen.StartCap = pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                graphics.DrawPath(pen, path);
            }
        }

        private void BuildPoints(DateTime now, TrailRibbonOptions options, IList<TrailDot> dots)
        {
            points.Clear(); controls.Clear();
            // The sampler emits at most 500/s and life is at most 2800 ms.
            // Refuse an unsupported caller instead of silently truncating its tail.
            if (dots.Count > MaxPoints) throw new ArgumentException("Trop de points de contrôle pour le ruban.", "dots");
            int life = Math.Max(100, Math.Min(2800, options.Life));
            for (int i = 0; i < dots.Count; i++)
                if ((now - dots[i].Time).TotalMilliseconds < life) controls.Add(dots[i]);
            if (controls.Count == 0) return;
            buildTime = now; buildOptions = options; buildLife = life;
            tolerance = options.PerformanceMode ? PerformanceGeometryTolerance : QualityGeometryTolerance;
            edgeFactor = options.Glow ? (options.PerformanceMode ? 3.6 : 7) : 1;
            pieceCount = heapCount = 0;
            float tension = TrailPath.SmoothingTension(options.Smoothness);
            for (int i = 1; i < controls.Count; i++)
            {
                roots[i] = -1;
                if (controls[i].BreakBefore) continue;
                PointF c1, c2; Controls(i, tension, out c1, out c2);
                roots[i] = NewPiece(new CurvePiece { A = controls[i - 1].ExactPoint, C1 = c1, C2 = c2,
                    B = controls[i].ExactPoint, From = 0, To = 1, Source = i, Left = -1, Right = -1 });
            }
            // A max heap spends the remaining budget on the largest estimated
            // error anywhere in the path. No repeated O(N^2) selection, no tail
            // truncation, and each original endpoint is reserved in advance.
            int available = MaxPoints - controls.Count;
            while (available > 0 && heapCount > 0)
            {
                int index = PopPiece(); CurvePiece piece = pieces[index];
                PointF a = Mid(piece.A, piece.C1), b = Mid(piece.C1, piece.C2), c = Mid(piece.C2, piece.B);
                PointF d = Mid(a, b), e = Mid(b, c), middle = Mid(d, e);
                double stamp = (piece.From + piece.To) * .5;
                piece.Left = NewPiece(new CurvePiece { A = piece.A, C1 = a, C2 = d, B = middle,
                    From = piece.From, To = stamp, Source = piece.Source, Depth = piece.Depth + 1, Left = -1, Right = -1 });
                piece.Right = NewPiece(new CurvePiece { A = middle, C1 = e, C2 = c, B = piece.B,
                    From = stamp, To = piece.To, Source = piece.Source, Depth = piece.Depth + 1, Left = -1, Right = -1 });
                pieces[index] = piece; available--;
            }
            for (int i = 1; i < controls.Count; i++)
            {
                if (i == 1 || controls[i - 1].BreakBefore)
                {
                    if (points.Count == 0 || points[points.Count - 1].Point != controls[i - 1].ExactPoint ||
                        points[points.Count - 1].Time != controls[i - 1].Time)
                        AddControl(i - 1, roots[i] < 0 ? PointF.Empty : Derivative(pieces[roots[i]], 0));
                }
                if (roots[i] >= 0) EmitPiece(roots[i]);
                else AddControl(i, i + 1 < controls.Count && roots[i + 1] >= 0 ? Derivative(pieces[roots[i + 1]], 0) : PointF.Empty);
            }
            if (controls.Count == 1) AddControl(0, PointF.Empty);
            // Colors follow the ACTUAL sampled arc, not a gradient in screen X/Y
            // and not the (longer) polygon of Bezier control handles.
            Colorize(now, options);
        }

        private int NewPiece(CurvePiece piece)
        {
            ScorePiece(ref piece);
            int index = pieceCount++; pieces[index] = piece;
            if (piece.Score > 1 && piece.Depth < MaxSubdivisionDepth)
            {
                int pos = heapCount++;
                while (pos > 0)
                {
                    int parent = (pos - 1) / 2;
                    if (pieces[heap[parent]].Score >= piece.Score) break;
                    heap[pos] = heap[parent]; pos = parent;
                }
                heap[pos] = index;
            }
            return index;
        }
        private int PopPiece()
        {
            int result = heap[0], last = heap[--heapCount], pos = 0;
            while (pos * 2 + 1 < heapCount)
            {
                int child = pos * 2 + 1;
                if (child + 1 < heapCount && pieces[heap[child + 1]].Score > pieces[heap[child]].Score) child++;
                if (pieces[last].Score >= pieces[heap[child]].Score) break;
                heap[pos] = heap[child]; pos = child;
            }
            if (heapCount > 0) heap[pos] = last;
            return result;
        }
        private void ScorePiece(ref CurvePiece piece)
        {
            LastTessellationChecks++;
            double error = Math.Max(ChordDistance(piece.C1, piece.A, piece.B), ChordDistance(piece.C2, piece.A, piece.B));
            float radiusA, alphaA, radiusB, alphaB;
            PropertiesAt(piece.Source, piece.From, out radiusA, out alphaA);
            PropertiesAt(piece.Source, piece.To, out radiusB, out alphaB);
            PointF directionA = PieceDirection(piece, 0), directionB = PieceDirection(piece, 1);
            double alphaError = 0;
            for (int sample = 1; sample <= 3; sample++)
            {
                double t = sample * .25;
                PointF point = Evaluate(piece, t), direction = PieceDirection(piece, t);
                float radius, alpha; PropertiesAt(piece.Source, piece.From + (piece.To - piece.From) * t, out radius, out alpha);
                alphaError = Math.Max(alphaError, Math.Abs(alpha - (alphaA + (alphaB - alphaA) * t)));
                for (int side = -1; side <= 1; side += 2)
                {
                    PointF a = Offset(piece.A, directionA, radiusA * edgeFactor * side);
                    PointF b = Offset(piece.B, directionB, radiusB * edgeFactor * side);
                    error = Math.Max(error, ChordDistance(Offset(point, direction, radius * edgeFactor * side), a, b));
                }
            }
            piece.GeometryError = error;
            piece.Score = Math.Max(error / tolerance, alphaError / 3.0);
            // Rainbow color is not linear across a long straight line. Reserve
            // enough color samples even when its geometric error is exactly zero.
            if (buildOptions.ColorMode == "Arc-en-ciel")
                piece.Score = Math.Max(piece.Score, (Distance(piece.A, piece.C1) + Distance(piece.C1, piece.C2) + Distance(piece.C2, piece.B)) / 12.0);
        }
        private void PropertiesAt(int source, double t, out float radius, out float alpha)
        {
            TrailDot a = controls[source - 1], b = controls[source];
            DateTime stamp = a.Time.AddTicks((long)((b.Time - a.Time).Ticks * t));
            Properties(stamp, (float)(a.Speed + (b.Speed - a.Speed) * t), buildTime, buildOptions, buildLife, out radius, out alpha);
        }
        private void AddControl(int index, PointF derivative)
        {
            TrailDot dot = controls[index];
            AddPoint(dot.ExactPoint, dot.Time, dot.Speed, true, derivative);
        }
        private void EmitPiece(int index)
        {
            CurvePiece piece = pieces[index];
            if (piece.Left >= 0) { EmitPiece(piece.Left); EmitPiece(piece.Right); return; }
            LastGeometryError = Math.Max(LastGeometryError, piece.GeometryError);
            if (piece.Score > 1.00001) LastGeometryLimited = true;
            TrailDot a = controls[piece.Source - 1], b = controls[piece.Source];
            DateTime stamp = piece.To == 1 ? b.Time : a.Time.AddTicks((long)((b.Time - a.Time).Ticks * piece.To));
            AddPoint(piece.B, stamp, (float)(a.Speed + (b.Speed - a.Speed) * piece.To), false, Derivative(piece, 1));
        }
        private static PointF Mid(PointF a, PointF b) { return new PointF((a.X + b.X) * .5f, (a.Y + b.Y) * .5f); }
        private static PointF Evaluate(CurvePiece p, double t)
        {
            double u = 1 - t;
            return new PointF((float)(u * u * u * p.A.X + 3 * u * u * t * p.C1.X + 3 * u * t * t * p.C2.X + t * t * t * p.B.X),
                (float)(u * u * u * p.A.Y + 3 * u * u * t * p.C1.Y + 3 * u * t * t * p.C2.Y + t * t * t * p.B.Y));
        }
        private static PointF Derivative(CurvePiece p, double t)
        {
            double u = 1 - t;
            return Unit((float)(u * u * (p.C1.X - p.A.X) + 2 * u * t * (p.C2.X - p.C1.X) + t * t * (p.B.X - p.C2.X)),
                (float)(u * u * (p.C1.Y - p.A.Y) + 2 * u * t * (p.C2.Y - p.C1.Y) + t * t * (p.B.Y - p.C2.Y)));
        }
        private static PointF PieceDirection(CurvePiece p, double t)
        {
            PointF direction = Derivative(p, t);
            return direction.IsEmpty ? Unit(p.B.X - p.A.X, p.B.Y - p.A.Y) : direction;
        }
        private static PointF Offset(PointF p, PointF direction, double radius)
        { return new PointF((float)(p.X - direction.Y * radius), (float)(p.Y + direction.X * radius)); }
        private static double ChordDistance(PointF p, PointF a, PointF b)
        {
            double x = b.X - (double)a.X, y = b.Y - (double)a.Y, length2 = x * x + y * y;
            double t = length2 < .000001 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * x + (p.Y - a.Y) * y) / length2));
            double dx = p.X - a.X - t * x, dy = p.Y - a.Y - t * y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private void Colorize(DateTime now, TrailRibbonOptions options)
        {
            double length = 0;
            for (int i = 1; i < points.Count; i++)
                if (!points[i].BreakBefore) length += Distance(points[i - 1].Point, points[i].Point);
            double arc = 0;
            for (int i = 0; i < points.Count; i++)
            {
                TrailRibbonPoint point = points[i];
                if (i > 0 && !point.BreakBefore) arc += Distance(points[i - 1].Point, point.Point);
                double t = length < .001 ? 0 : Math.Min(1, arc / length);
                if (options.ColorMode == "Dégradé")
                {
                    point.Red = (float)(options.FirstColor.R + (options.SecondColor.R - options.FirstColor.R) * t);
                    point.Green = (float)(options.FirstColor.G + (options.SecondColor.G - options.FirstColor.G) * t);
                    point.Blue = (float)(options.FirstColor.B + (options.SecondColor.B - options.FirstColor.B) * t);
                }
                else if (options.ColorMode == "Arc-en-ciel")
                {
                    Color color = Hue((arc * 1.3 + now.TimeOfDay.TotalSeconds * 45) % 360);
                    point.Red = color.R; point.Green = color.G; point.Blue = color.B;
                }
                points[i] = point;
            }
        }

        private void Controls(int index, float tension, out PointF c1, out PointF c2)
        {
            PointF a = controls[index - 1].ExactPoint, b = controls[index].ExactPoint;
            PointF previous = index < 2 || controls[index - 1].BreakBefore ? a : controls[index - 2].ExactPoint;
            PointF next = index + 1 >= controls.Count || controls[index + 1].BreakBefore ? b : controls[index + 1].ExactPoint;
            TrailPath.RibbonCurveControls(previous, a, b, next, tension, out c1, out c2);
        }

        private static double Ease(double value)
        { value = Math.Max(0, Math.Min(1, value)); return value * value * (3 - 2 * value); }

        private static void Properties(DateTime time, float speed, DateTime now, TrailRibbonOptions options, int life,
            out float radius, out float alpha)
        {
            double remain = Math.Max(0, Math.Min(1, 1 - (now - time).TotalMilliseconds / life));
            float dynamic = options.SpeedResponsive ? 1 + Math.Min(1.4f, Math.Max(0, Math.Min(4, speed)) * .28f) : 1;
            // Preserve the fresh head's width. Only the last 45% of lifetime
            // tapers toward zero, with zero derivative at both ends of the taper.
            radius = Math.Max(.01f, Math.Max(1, Math.Min(40, options.Width)) * dynamic * (float)Ease(remain / .45) / 2);
            alpha = (float)(Math.Max(0, Math.Min(255, options.Opacity)) * Ease(remain));
        }

        private void AddPoint(PointF point, DateTime time, float speed, bool start, PointF tangent)
        {
            float radius, alpha; Properties(time, speed, buildTime, buildOptions, buildLife, out radius, out alpha);
            Color color = buildOptions.FirstColor;
            points.Add(new TrailRibbonPoint { Point = point, Tangent = tangent, Time = time, BreakBefore = start,
                HalfWidth = radius, Alpha = alpha,
                Red = color.R, Green = color.G, Blue = color.B });
        }

        private void BuildMesh(float factor, double pixelScale)
        {
            vertices.Clear(); triangles.Clear();
            int start = 0;
            while (start < points.Count)
            {
                int end = start + 1;
                while (end < points.Count && !points[end].BreakBefore) end++;
                if (end - start == 1) AddCap(points[start], factor, pixelScale);
                else if (end - start >= 2)
                {
                    int firstRail = vertices.Count;
                    PointF carriedNormal = PointF.Empty;
                    for (int i = start; i < end; i++)
                    {
                        PointF before = i == start ? points[i].Point : points[i - 1].Point;
                        PointF after = i == end - 1 ? points[i].Point : points[i + 1].Point;
                        PointF previousDirection = Unit(points[i].Point.X - before.X, points[i].Point.Y - before.Y);
                        PointF nextDirection = Unit(after.X - points[i].Point.X, after.Y - points[i].Point.Y);
                        if (previousDirection.IsEmpty) previousDirection = nextDirection;
                        if (nextDirection.IsEmpty) nextDirection = previousDirection;
                        if (i == start) previousDirection = nextDirection;
                        if (i == end - 1) nextDirection = previousDirection;
                        PointF tangent = points[i].Tangent;
                        PointF normal = tangent.IsEmpty ? Unit(-previousDirection.Y - nextDirection.Y, previousDirection.X + nextDirection.X) :
                            new PointF(-tangent.Y, tangent.X);
                        if (Math.Abs(normal.X) + Math.Abs(normal.Y) < .001f)
                            normal = new PointF(-previousDirection.Y, previousDirection.X);
                        if (normal.IsEmpty) normal = carriedNormal.IsEmpty ? new PointF(0, 1) : carriedNormal;
                        // A normal and its opposite describe the same symmetric
                        // section. Carry its sign through a cusp/reversal so the
                        // two rails cannot swap and pinch the returning portion.
                        if (!carriedNormal.IsEmpty && normal.X * carriedNormal.X + normal.Y * carriedNormal.Y < 0)
                            normal = new PointF(-normal.X, -normal.Y);
                        carriedNormal = normal;
                        float projection = Math.Abs(normal.X * -nextDirection.Y + normal.Y * nextDirection.X);
                        float miter = tangent.IsEmpty ? Math.Min(2f, 1f / Math.Max(.05f, projection)) : 1f;
                        float radius = (points[i].HalfWidth * factor + (float)pixelScale) * miter;
                        vertices.Add(MakeVertex(points[i], points[i].Point.X + normal.X * radius, points[i].Point.Y + normal.Y * radius));
                        vertices.Add(MakeVertex(points[i], points[i].Point.X - normal.X * radius, points[i].Point.Y - normal.Y * radius));
                    }
                    for (int i = 0; i < end - start - 1; i++)
                    {
                        int a = firstRail + i * 2, b = a + 2;
                        triangles.Add(new Triangle(a, a + 1, b));
                        triangles.Add(new Triangle(a + 1, b + 1, b));
                    }
                    AddCap(points[start], factor, pixelScale); AddCap(points[end - 1], factor, pixelScale);
                    for (int i = start + 1; i < end - 1; i++)
                    {
                        PointF a = Unit(points[i].Point.X - points[i - 1].Point.X, points[i].Point.Y - points[i - 1].Point.Y);
                        PointF b = Unit(points[i + 1].Point.X - points[i].Point.X, points[i + 1].Point.Y - points[i].Point.Y);
                        if (a.X * b.X + a.Y * b.Y < -.75f) AddCap(points[i], factor, pixelScale);
                    }
                }
                start = end;
            }
        }

        private static Vertex MakeVertex(TrailRibbonPoint point, float x, float y)
        {
            return new Vertex { X = x, Y = y, CenterX = point.Point.X, CenterY = point.Point.Y,
                Radius = point.HalfWidth, Alpha = point.Alpha, Red = point.Red, Green = point.Green, Blue = point.Blue };
        }
        private void AddCap(TrailRibbonPoint point, float factor, double pixelScale)
        {
            int center = vertices.Count; vertices.Add(MakeVertex(point, point.Point.X, point.Point.Y));
            float radius = (point.HalfWidth * factor + (float)pixelScale) * 1.04f;
            for (int i = 0; i < CapSteps; i++)
            {
                double angle = i * Math.PI * 2 / CapSteps;
                vertices.Add(MakeVertex(point, point.Point.X + (float)Math.Cos(angle) * radius,
                    point.Point.Y + (float)Math.Sin(angle) * radius));
            }
            for (int i = 0; i < CapSteps; i++)
                triangles.Add(new Triangle(center, center + 1 + i, center + 1 + ((i + 1) % CapSteps)));
        }

        private double ProjectAndEstimate()
        {
            projected.Clear();
            foreach (Vertex source in vertices)
            {
                Vertex value = source;
                value.X = (float)((value.X - originX) / scale); value.Y = (float)((value.Y - originY) / scale);
                value.CenterX = (float)((value.CenterX - originX) / scale); value.CenterY = (float)((value.CenterY - originY) / scale);
                value.Radius = (float)(value.Radius / scale); projected.Add(value);
            }
            double work = 0;
            foreach (Triangle triangle in triangles)
            {
                Vertex a = projected[triangle.A], b = projected[triangle.B], c = projected[triangle.C];
                float minX = Math.Min(a.X, Math.Min(b.X, c.X)), maxX = Math.Max(a.X, Math.Max(b.X, c.X));
                float minY = Math.Min(a.Y, Math.Min(b.Y, c.Y)), maxY = Math.Max(a.Y, Math.Max(b.Y, c.Y));
                if (maxX < 0 || maxY < 0 || minX >= rasterWidth || minY >= rasterHeight) continue;
                work += Math.Abs(Area(a, b, c)) / 2 + 2 * (maxX - minX + maxY - minY) + 8;
            }
            return work;
        }

        private bool RasterTriangle(Vertex a, Vertex b, Vertex c, TrailRibbonOptions options)
        {
            double area = Area(a, b, c);
            if (Math.Abs(area) < .000001) return true;
            int firstY = Math.Max(0, (int)Math.Ceiling(Math.Min(a.Y, Math.Min(b.Y, c.Y)) - .5));
            int lastY = Math.Min(rasterHeight - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y)) - .5) - 1);
            for (int y = firstY; y <= lastY; y++)
            {
                double yy = y + .5, low = Double.MaxValue, high = Double.MinValue;
                EdgeAt(a, b, yy, ref low, ref high); EdgeAt(b, c, yy, ref low, ref high); EdgeAt(c, a, yy, ref low, ref high);
                if (low > high) continue;
                int firstX = Math.Max(0, (int)Math.Ceiling(low - .5));
                int lastX = Math.Min(rasterWidth - 1, (int)Math.Ceiling(high - .5) - 1);
                if (!denseCompositeReference && firstX <= lastX)
                {
                    // Every nonzero layer pixel is written inside one of these
                    // scanline spans. Their union is conservative, never tighter.
                    rowFirst[y] = Math.Min(rowFirst[y], firstX);
                    rowLast[y] = Math.Max(rowLast[y], lastX);
                }
                for (int x = firstX; x <= lastX; x++)
                {
                    if (visits >= budget) return false;
                    visits++;
                    double xx = x + .5;
                    double wa = ((b.X - xx) * (c.Y - yy) - (b.Y - yy) * (c.X - xx)) / area;
                    double wb = ((c.X - xx) * (a.Y - yy) - (c.Y - yy) * (a.X - xx)) / area;
                    double wc = 1 - wa - wb;
                    double centerX = wa * a.CenterX + wb * b.CenterX + wc * c.CenterX;
                    double centerY = wa * a.CenterY + wb * b.CenterY + wc * c.CenterY;
                    double dx = xx - centerX, dy = yy - centerY;
                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    double radius = Math.Max(.00001, wa * a.Radius + wb * b.Radius + wc * c.Radius);
                    double alpha = Math.Max(0, wa * a.Alpha + wb * b.Alpha + wc * c.Alpha);
                    int red = Byte(wa * a.Red + wb * b.Red + wc * c.Red);
                    int green = Byte(wa * a.Green + wb * b.Green + wc * c.Green);
                    int blue = Byte(wa * a.Blue + wb * b.Blue + wc * c.Blue);
                    int index = y * capacityWidth + x;
                    if (options.Glow)
                    {
                        if (!options.PerformanceMode) Put(wide, index, alpha * .10 * GlowCoverage(distance, radius * 7), red, green, blue);
                        Put(soft, index, alpha * .25 * GlowCoverage(distance, radius * 3.6), red, green, blue);
                    }
                    Put(ink, index, alpha * Coverage(distance, radius), red, green, blue);
                    if (options.Glow)
                        Put(core, index, alpha * .85 * Coverage(distance, Math.Max(.00001, radius * .38)),
                            (red + 765) / 4, (green + 765) / 4, (blue + 765) / 4);
                }
            }
            return true;
        }

        private static void EdgeAt(Vertex a, Vertex b, double y, ref double low, ref double high)
        {
            if ((a.Y <= y && y < b.Y) || (b.Y <= y && y < a.Y))
            {
                double x = a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y);
                low = Math.Min(low, x); high = Math.Max(high, x);
            }
        }
        private static double Area(Vertex a, Vertex b, Vertex c)
        { return (b.X - a.X) * (double)(c.Y - a.Y) - (b.Y - a.Y) * (double)(c.X - a.X); }
        private static double Coverage(double distance, double radius)
        { return Math.Max(0, Math.Min(Math.Min(1, radius * 2), radius + .5 - distance)); }
        private static double GlowCoverage(double distance, double radius)
        {
            // Smooth compact radial profile: no flat rings, no per-pixel exp(),
            // and exactly zero outside the bounded glow footprint.
            double t = Math.Min(1, distance / Math.Max(.00001, radius));
            double value = 1 - t * t;
            return value * value * Coverage(distance, radius);
        }
        private static int Byte(double value) { return (int)Math.Max(0, Math.Min(255, Math.Round(value))); }
        private static void Put(int[] layer, int index, double alpha, int red, int green, int blue)
        {
            int a = Byte(alpha);
            if (a == 0 || a < (int)((uint)layer[index] >> 24)) return;
            layer[index] = (a << 24) | ((red * a + 127) / 255 << 16) |
                ((green * a + 127) / 255 << 8) | ((blue * a + 127) / 255);
        }
        private static int Over(int lower, int upper)
        {
            int a = (int)((uint)upper >> 24), inverse = 255 - a;
            int alpha = a + ((int)((uint)lower >> 24) * inverse + 127) / 255;
            int red = ((upper >> 16) & 255) + (((lower >> 16) & 255) * inverse + 127) / 255;
            int green = ((upper >> 8) & 255) + (((lower >> 8) & 255) * inverse + 127) / 255;
            int blue = (upper & 255) + ((lower & 255) * inverse + 127) / 255;
            return (alpha << 24) | (Math.Min(255, red) << 16) | (Math.Min(255, green) << 8) | Math.Min(255, blue);
        }

        private void EnsureSurface(int width, int height, int limit)
        {
            if (bitmap != null && width <= capacityWidth && height <= capacityHeight &&
                (long)capacityWidth * capacityHeight <= limit) return;
            int nextWidth = ((width + 31) / 32) * 32, nextHeight = ((height + 31) / 32) * 32;
            if ((long)nextWidth * nextHeight > limit) { nextWidth = width; nextHeight = height; }
            if ((long)nextWidth * nextHeight > limit) throw new InvalidOperationException("Surface de ruban hors budget.");
            if (bitmap != null) bitmap.Dispose();
            capacityWidth = nextWidth; capacityHeight = nextHeight;
            int count = capacityWidth * capacityHeight;
            wide = new int[count]; soft = new int[count]; ink = new int[count]; core = new int[count]; output = new int[count];
            rowFirst = new int[capacityHeight]; rowLast = new int[capacityHeight];
            bitmap = new Bitmap(capacityWidth, capacityHeight, PixelFormat.Format32bppPArgb);
        }

        private void Composite(TrailRibbonOptions options, ref RibbonStageTiming timing)
        {
            long stage = RenderStageClock.Start(timing.Active);
            // Clear the WHOLE output, including unused rows and columns. GDI+
            // interpolation can sample outside the current raster rectangle.
            // The sparse pass only skips pixels whose four layers remain zero.
            Array.Clear(output, 0, output.Length);
            for (int y = 0; y < rasterHeight; y++)
            {
                int first = denseCompositeReference ? 0 : rowFirst[y];
                int last = denseCompositeReference ? rasterWidth - 1 : rowLast[y];
                if (first <= last) LastCompositePixels += last - first + 1;
                for (int x = first; x <= last; x++)
                {
                    int index = y * capacityWidth + x;
                    int value = options.Glow ? Over(wide[index], soft[index]) : 0;
                    value = Over(value, ink[index]);
                    output[index] = options.Glow ? Over(value, core[index]) : value;
                }
            }
            timing.CompositeMs = RenderStageClock.Elapsed(stage);
            stage = RenderStageClock.Start(timing.Active);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, capacityWidth, capacityHeight),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                // Exact same bytes, with a single managed/native transition for
                // a contiguous top-down bitmap. Other strides keep the old path.
                if (!denseCompositeReference && data.Stride == capacityWidth * 4)
                {
                    Marshal.Copy(output, 0, data.Scan0, output.Length);
                    LastBitmapCopyCalls = 1;
                }
                else
                {
                    for (int y = 0; y < capacityHeight; y++)
                        Marshal.Copy(output, y * capacityWidth, IntPtr.Add(data.Scan0, y * data.Stride), capacityWidth);
                    LastBitmapCopyCalls = capacityHeight;
                }
                LastBitmapCopiedPixels = output.Length;
            }
            finally { bitmap.UnlockBits(data); }
            timing.UploadMs = RenderStageClock.Elapsed(stage);
        }

        private static double Distance(PointF a, PointF b)
        { double x = b.X - a.X, y = b.Y - a.Y; return Math.Sqrt(x * x + y * y); }
        private static PointF Unit(float x, float y)
        { double length = Math.Sqrt(x * (double)x + y * (double)y); return length < .0001 ? PointF.Empty : new PointF((float)(x / length), (float)(y / length)); }
        private static Color Mix(Color a, Color b, double t)
        { return Color.FromArgb(Byte(a.R + (b.R - a.R) * t), Byte(a.G + (b.G - a.G) * t), Byte(a.B + (b.B - a.B) * t)); }
        private static Color Hue(double hue)
        {
            hue = (hue % 360 + 360) % 360;
            double x = 1 - Math.Abs((hue / 60) % 2 - 1), m = .17;
            double r = 0, g = 0, b = 0;
            if (hue < 60) { r = 1; g = x; }
            else if (hue < 120) { r = x; g = 1; }
            else if (hue < 180) { g = 1; b = x; }
            else if (hue < 240) { g = x; b = 1; }
            else if (hue < 300) { r = x; b = 1; }
            else { r = 1; b = x; }
            return Color.FromArgb(Byte((r * .83 + m) * 255), Byte((g * .83 + m) * 255), Byte((b * .83 + m) * 255));
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (bitmap != null) { bitmap.Dispose(); bitmap = null; }
            wide = soft = ink = core = output = rowFirst = rowLast = null;
            points.Clear(); controls.Clear(); vertices.Clear(); triangles.Clear(); projected.Clear();
        }
    }
}
