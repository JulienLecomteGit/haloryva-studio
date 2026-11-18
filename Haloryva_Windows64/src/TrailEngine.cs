using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace CursorFlow
{
    // Pas d'horloge murale pour la persistance : une correction d'heure ne vieillit pas le tracé.
    internal static class TrailClock
    {
        private static readonly DateTime origin = DateTime.UtcNow;
        private static readonly Stopwatch watch = Stopwatch.StartNew();
        internal static DateTime Now { get { return origin.AddTicks((long)(watch.ElapsedTicks * (10000000.0 / Stopwatch.Frequency))); } }
    }

    internal sealed class TrailDot
    {
        private Point point;
        private PointF exactPoint;
        // Motifs keep their integer coordinate contract. The continuous ribbon
        // uses ExactPoint, including the fractions produced by resampling.
        public Point Point
        {
            get { return point; }
            set { point = value; exactPoint = value; }
        }
        internal PointF ExactPoint
        {
            get { return exactPoint; }
            set
            {
                if (Single.IsNaN(value.X) || Single.IsNaN(value.Y) ||
                    Single.IsInfinity(value.X) || Single.IsInfinity(value.Y))
                    throw new ArgumentOutOfRangeException("value", "Finite trail coordinates required.");
                exactPoint = value; point = System.Drawing.Point.Round(value);
            }
        }
        public DateTime Time;
        public float Speed;
        public bool BreakBefore;
        internal bool KeepVertex;
        private static long counter;
        public readonly long Id;
        public TrailDot(Point p, DateTime t, float speed) : this(p, t, speed, Interlocked.Increment(ref counter)) { }
        internal TrailDot(Point p, DateTime t, float speed, long groupId)
        { Point = p; Time = t; Speed = speed; Id = groupId; }
        public TrailDot(PointF p, DateTime t, float speed) : this(p, t, speed, Interlocked.Increment(ref counter)) { }
        internal TrailDot(PointF p, DateTime t, float speed, long groupId)
        { ExactPoint = p; Time = t; Speed = speed; Id = groupId; }
    }

    // Rééchantillonnage distance ET temps. 500 émissions/s maximum, vie 2800 ms maximum :
    // <= 1402 points, sans supprimer une particule qui n'a pas fini son fondu.
    // Usage exclusivement sur le fil UI ; le worker de cadence ne touche jamais à cette liste.
    internal sealed class TrailHistory
    {
        internal const double MinIntervalMs = 2.0;
        internal const double MaxInputGapMs = 125.0;
        internal const double SpeedResponseMs = 12.0;
        internal readonly List<TrailDot> Points = new List<TrailDot>(1536);
        private bool anchored;
        private PointF input;
        private DateTime inputTime, emittedTime, lastMotionTime;
        private float inputSpeed;
        private double remainder;
        private long emittedGroups;
        internal int GapResets { get; private set; }
        internal int PeakCount { get; private set; }
        internal bool HasInput { get { return anchored; } }
        internal Point LastInputPoint { get { return System.Drawing.Point.Round(input); } }
        internal PointF LastInputExactPoint { get { return input; } }
        internal DateTime LastInputTime { get { return inputTime; } }
        internal DateTime LastMotionTime { get { return lastMotionTime; } }
        internal float LastInputSpeed { get { return inputSpeed; } }
        internal long LastGroupId { get { return emittedGroups; } }

        internal void ResetInput() { anchored = false; remainder = 0; }
        internal void Clear() { Points.Clear(); ResetInput(); emittedGroups = 0; }
        internal void Expire(DateTime now, int life)
        {
            life = Math.Max(100, Math.Min(2800, life));
            int count = 0;
            while (count < Points.Count && (now - Points[count].Time).TotalMilliseconds >= life) count++;
            if (count > 0) Points.RemoveRange(0, count);
        }
        internal void Move(Point current, DateTime now, int spacing, int life, bool active)
        { Move((PointF)current, now, spacing, life, active); }

        internal void Move(PointF current, DateTime now, int spacing, int life, bool active)
        {
            if (Single.IsNaN(current.X) || Single.IsNaN(current.Y) ||
                Single.IsInfinity(current.X) || Single.IsInfinity(current.Y))
                throw new ArgumentOutOfRangeException("current", "Finite trail coordinates required.");
            Expire(now, life);
            if (!active) { ResetInput(); return; }
            spacing = Math.Max(2, Math.Min(40, spacing));
            // Expiration does not turn an unchanged cursor position into a new
            // gesture. Keep the sampling clock current without renewing its age.
            if (anchored && current == input) { inputTime = now; return; }
            if (!anchored || Points.Count == 0)
            {
                anchored = true; input = current; inputTime = emittedTime = lastMotionTime = now; remainder = 0; inputSpeed = 0;
                TrailDot first = new TrailDot(current, now, 0, ++emittedGroups); first.BreakBefore = true; Points.Add(first);
                PeakCount = Math.Max(PeakCount, Points.Count); return;
            }
            double dt = (now - inputTime).TotalMilliseconds;
            double dx = current.X - (double)input.X, dy = current.Y - (double)input.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < .000001) { input = current; inputTime = now; return; }
            if (dt < 0 || dt > MaxInputGapMs || (now - lastMotionTime).TotalMilliseconds > MaxInputGapMs)
            {
                // Reprise après une pause/retard : ne pas inventer un raccord à travers le Bureau.
                ResetInput(); GapResets++;
                // Laisser les anciens points finir leur vie. Le nouveau segment est discontinu.
                anchored = true; input = current; inputTime = emittedTime = lastMotionTime = now; remainder = 0; inputSpeed = 0;
                TrailDot first = new TrailDot(current, now, 0, ++emittedGroups); first.BreakBefore = true; Points.Add(first);
                PeakCount = Math.Max(PeakCount, Points.Count); return;
            }
            if (dt <= 0) { input = current; remainder = Math.Min(spacing, remainder + distance); return; }
            // Smooth only the width-driving speed, never the cursor position.
            // An exponential response is independent of event frequency for a
            // constant velocity; use the same response at each emitted date.
            float previousSpeed = inputSpeed;
            // dt is strictly positive here. A 1 ms divisor floor would make
            // an otherwise identical sub-millisecond input appear slower.
            // The existing 4 px/ms cap bounds unusually short/spiky intervals.
            float targetSpeed = (float)Math.Min(4.0, distance / dt);
            inputSpeed = ResponseSpeed(previousSpeed, targetSpeed, dt);
            lastMotionTime = now;
            double along = 0;
            int generated = 0;
            while (generated < 64)
            {
                double byDistance = along + Math.Max(0, spacing - remainder);
                double byTime = distance * Math.Max(0, (emittedTime.AddMilliseconds(MinIntervalMs) - inputTime).TotalMilliseconds) / dt;
                double next = Math.Max(byDistance, byTime);
                if (next > distance + .000001) break;
                double t = Math.Max(0, Math.Min(1, next / distance));
                DateTime stamp = inputTime.AddTicks((long)(dt * t * TimeSpan.TicksPerMillisecond));
                if ((stamp - emittedTime).TotalMilliseconds < MinIntervalMs - .0001) break;
                PointF point = new PointF((float)(input.X + dx * t), (float)(input.Y + dy * t));
                TrailDot dot = new TrailDot(point, stamp, ResponseSpeed(previousSpeed, targetSpeed, dt * t), ++emittedGroups);
                Points.Add(dot); emittedTime = stamp;
                along = next; remainder = 0; generated++;
                if (next >= distance) break;
            }
            remainder = Math.Min(spacing, remainder + Math.Max(0, distance - along));
            input = current; inputTime = now;
            PeakCount = Math.Max(PeakCount, Points.Count);
        }

        private static float ResponseSpeed(float previous, float target, double elapsedMs)
        { return (float)(previous + (target - previous) * (1 - Math.Exp(-Math.Max(0, elapsedMs) / SpeedResponseMs))); }
    }

    // Réduction du travail de dessin, pas de la vie des points. Extrémités et ruptures
    // conservées. Une corde ne remplace au plus que 16 ms, 0,25 px d'écart,
    // 1 ms de datation et 0,04 px/ms de vitesse interpolée.
    internal static class TrailPath
    {
        internal const double CompactPositionError = .25;
        internal const double CompactTimeErrorMs = 1.0;
        internal const double CompactSpeedError = .04;
        // Répartition perceptible de la souplesse : 0 % = segment droit,
        // 25 % = effet déjà visible, 100 % = lissage maximal borné.
        // Les mêmes contrôles sont utilisés pour le rendu ET ses limites de surface.
        internal static float SmoothingTension(int percent)
        {
            float amount = Math.Max(0, Math.Min(100, percent)) / 100f;
            return .31f * (float)Math.Sqrt(amount);
        }

        internal static void CurveControls(PointF prev, PointF a, PointF b, PointF next,
            float tension, out PointF c1, out PointF c2)
        {
            float minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X);
            float minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
            // Contrôles confinés au rectangle du segment : aucun débordement aux angles serrés.
            c1 = new PointF(
                Math.Max(minX, Math.Min(maxX, a.X + (b.X - prev.X) * tension)),
                Math.Max(minY, Math.Min(maxY, a.Y + (b.Y - prev.Y) * tension)));
            c2 = new PointF(
                Math.Max(minX, Math.Min(maxX, b.X - (next.X - a.X) * tension)),
                Math.Max(minY, Math.Min(maxY, b.Y - (next.Y - a.Y) * tension)));
        }

        // Continuous-ribbon controls use the SAME tangent on both sides of a node.
        // Unlike the historical dotted-line controls, no independent axis clamp
        // creates a corner at every segment. Tangent length is bounded by the
        // shorter adjacent chord; the surface includes these controls explicitly.
        internal static void RibbonCurveControls(PointF prev, PointF a, PointF b, PointF next,
            float tension, out PointF c1, out PointF c2)
        {
            PointF first = RibbonTangent(prev, a, b, tension);
            PointF second = RibbonTangent(a, b, next, tension);
            c1 = new PointF(a.X + first.X, a.Y + first.Y);
            c2 = new PointF(b.X - second.X, b.Y - second.Y);
        }

        private static PointF RibbonTangent(PointF previous, PointF current, PointF next, float tension)
        {
            double left = Math.Sqrt(Math.Pow(current.X - previous.X, 2) + Math.Pow(current.Y - previous.Y, 2));
            double right = Math.Sqrt(Math.Pow(next.X - current.X, 2) + Math.Pow(next.Y - current.Y, 2));
            if (left < .001 && right < .001) return PointF.Empty;
            double limit = .45 * (left < .001 ? right : (right < .001 ? left : Math.Min(left, right)));
            double x = (next.X - previous.X) * tension, y = (next.Y - previous.Y) * tension;
            double length = Math.Sqrt(x * x + y * y);
            if (length > limit && length > .001) { x *= limit / length; y *= limit / length; }
            return new PointF((float)x, (float)y);
        }

        internal static void Compact(IList<TrailDot> input, List<TrailDot> output)
        {
            output.Clear();
            if (input.Count == 0) return;
            output.Add(input[0]); int start = 0;
            while (start < input.Count - 1)
            {
                int end = start + 1;
                if (!input[end].BreakBefore)
                {
                    for (int candidate = end + 1; candidate < input.Count && candidate <= start + 32; candidate++)
                    {
                        if (input[candidate].BreakBefore || (input[candidate].Time - input[start].Time).TotalMilliseconds > 16.001) break;
                        bool ok = true;
                        for (int k = start + 1; k < candidate; k++)
                            if (!CanReplace(input[k], input[start], input[candidate])) { ok = false; break; }
                        if (!ok) break;
                        end = candidate;
                    }
                }
                output.Add(input[end]); start = end;
            }
        }
        private static bool CanReplace(TrailDot sample, TrailDot first, TrailDot last)
        {
            if (sample.KeepVertex) return false;
            PointF p = sample.ExactPoint, a = first.ExactPoint, b = last.ExactPoint;
            double dx = b.X - (double)a.X, dy = b.Y - (double)a.Y, den = dx * dx + dy * dy;
            double t = den < .00001 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / den));
            double x = p.X - a.X - t * dx, y = p.Y - a.Y - t * dy;
            if (x * x + y * y > CompactPositionError * CompactPositionError) return false;
            double duration = (last.Time - first.Time).TotalMilliseconds;
            if (duration <= 0) return false;
            double timeError = Math.Abs((sample.Time - first.Time).TotalMilliseconds - duration * t);
            if (timeError > CompactTimeErrorMs) return false;
            double predictedSpeed = first.Speed + (last.Speed - first.Speed) * t;
            return Math.Abs(sample.Speed - predictedSpeed) <= CompactSpeedError;
        }
    }

    // Pas de seuil 1152/896 tronquant une traînée vivante. Le rectangle reste contenu
    // dans le Bureau virtuel. Il grandit par paliers ; il ne rétrécit qu'après 750 ms au repos.
    internal sealed class StableSurfaceBounds
    {
        private Rectangle current = Rectangle.Empty, previousDesktop;
        private double quietSince = -1;
        private static int Round(int value) { return ((value + 127) / 128) * 128; }
        internal void Reset() { current = Rectangle.Empty; quietSince = -1; }
        internal Rectangle Calculate(Rectangle desktop, Rectangle desired, bool motionAlive, double nowMs)
        {
            if (desktop != previousDesktop) { Reset(); previousDesktop = desktop; }
            Rectangle wanted = Rectangle.Intersect(desktop, desired);
            if (wanted.Width <= 0 || wanted.Height <= 0) return Rectangle.Empty;
            // Contraction aussi au premier mouvement après un long repos :
            // aucune image périodique n'est imposée uniquement pour réduire la DIB.
            bool shrink = quietSince >= 0 && nowMs - quietSince >= 750;
            if (motionAlive) quietSince = -1;
            else if (quietSince < 0) quietSince = nowMs;
            int minW = Math.Min(desktop.Width, Math.Max(384, Round(wanted.Width)));
            int minH = Math.Min(desktop.Height, Math.Max(384, Round(wanted.Height)));
            int width = shrink ? minW : Math.Min(desktop.Width, Math.Max(minW, current.Width));
            int height = shrink ? minH : Math.Min(desktop.Height, Math.Max(minH, current.Height));
            int x = current.IsEmpty ? wanted.Left : current.Left;
            int y = current.IsEmpty ? wanted.Top : current.Top;
            x = Math.Max(wanted.Right - width, Math.Min(wanted.Left, x));
            y = Math.Max(wanted.Bottom - height, Math.Min(wanted.Top, y));
            x = Math.Max(desktop.Left, Math.Min(desktop.Right - width, x));
            y = Math.Max(desktop.Top, Math.Min(desktop.Bottom - height, y));
            current = new Rectangle(x, y, width, height);
            return current;
        }
    }

    // Échéance absolue : aucune rafale de rattrapage après un retard du fil UI.
    internal sealed class FrameDeadline
    {
        private double next;
        private bool started;
        internal double Delay(double now, int fps)
        {
            double interval = 1000.0 / Math.Max(15, Math.Min(60, fps));
            if (!started) { started = true; next = now + interval; }
            return Math.Max(0, next - now);
        }
        internal void Advance(double now, int fps)
        {
            double interval = 1000.0 / Math.Max(15, Math.Min(60, fps));
            next += interval;
            if (next <= now) next = now + interval;
        }
    }
    internal sealed class FrameGate
    {
        private int pending;
        internal bool TryEnter() { return Interlocked.CompareExchange(ref pending, 1, 0) == 0; }
        internal void Leave() { Interlocked.Exchange(ref pending, 0); }
    }
}
