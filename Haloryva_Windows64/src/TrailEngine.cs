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
        public Point Point;
        public DateTime Time;
        public float Speed;
        public bool BreakBefore;
        private static long counter;
        public readonly long Id = Interlocked.Increment(ref counter);
        public TrailDot(Point p, DateTime t, float speed) { Point = p; Time = t; Speed = speed; }
    }

    // Rééchantillonnage distance ET temps. 500 émissions/s maximum, vie 2800 ms maximum :
    // <= 1402 points, sans supprimer une particule qui n'a pas fini son fondu.
    // Usage exclusivement sur le fil UI ; le worker de cadence ne touche jamais à cette liste.
    internal sealed class TrailHistory
    {
        internal const double MinIntervalMs = 2.0;
        internal const double MaxInputGapMs = 125.0;
        internal readonly List<TrailDot> Points = new List<TrailDot>(1536);
        private bool anchored;
        private Point input;
        private DateTime inputTime, emittedTime;
        private double remainder;
        internal int GapResets { get; private set; }
        internal int PeakCount { get; private set; }

        internal void ResetInput() { anchored = false; remainder = 0; }
        internal void Clear() { Points.Clear(); ResetInput(); }
        internal void Expire(DateTime now, int life)
        {
            life = Math.Max(100, Math.Min(2800, life));
            int count = 0;
            while (count < Points.Count && (now - Points[count].Time).TotalMilliseconds >= life) count++;
            if (count > 0) Points.RemoveRange(0, count);
        }
        internal void Move(Point current, DateTime now, int spacing, int life, bool active)
        {
            Expire(now, life);
            if (!active) { ResetInput(); return; }
            spacing = Math.Max(2, Math.Min(40, spacing));
            if (!anchored || Points.Count == 0)
            {
                anchored = true; input = current; inputTime = emittedTime = now; remainder = 0;
                TrailDot first = new TrailDot(current, now, 0); first.BreakBefore = true; Points.Add(first);
                PeakCount = Math.Max(PeakCount, Points.Count); return;
            }
            double dt = (now - inputTime).TotalMilliseconds;
            double dx = current.X - (double)input.X, dy = current.Y - (double)input.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < .01) { input = current; inputTime = now; return; }
            if (dt < 0 || dt > MaxInputGapMs)
            {
                // Reprise après une pause/retard : ne pas inventer un raccord à travers le Bureau.
                ResetInput(); GapResets++;
                // Laisser les anciens points finir leur vie. Le nouveau segment est discontinu.
                anchored = true; input = current; inputTime = emittedTime = now; remainder = 0;
                TrailDot first = new TrailDot(current, now, 0); first.BreakBefore = true; Points.Add(first);
                PeakCount = Math.Max(PeakCount, Points.Count); return;
            }
            if (dt <= 0) { input = current; remainder = Math.Min(spacing, remainder + distance); return; }
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
                Point point = new Point((int)Math.Round(input.X + dx * t), (int)Math.Round(input.Y + dy * t));
                TrailDot dot = new TrailDot(point, stamp, (float)Math.Min(4.0, distance / Math.Max(1, dt)));
                Points.Add(dot); emittedTime = stamp;
                along = next; remainder = 0; generated++;
                if (next >= distance) break;
            }
            remainder = Math.Min(spacing, remainder + Math.Max(0, distance - along));
            input = current; inputTime = now;
            PeakCount = Math.Max(PeakCount, Points.Count);
        }
    }

    // Réduction du travail de dessin, pas de la vie des points. Extrémités et ruptures
    // conservées. Une corde ne remplace au plus que 16 ms et 0,65 px d'écart du tracé.
    internal static class TrailPath
    {
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
                            if (Distance(input[k].Point, input[start].Point, input[candidate].Point) > .65) { ok = false; break; }
                        if (!ok) break;
                        end = candidate;
                    }
                }
                output.Add(input[end]); start = end;
            }
        }
        private static double Distance(Point p, Point a, Point b)
        {
            double dx = b.X - (double)a.X, dy = b.Y - (double)a.Y, den = dx * dx + dy * dy;
            double t = den < .00001 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / den));
            double x = p.X - a.X - t * dx, y = p.Y - a.Y - t * dy;
            return Math.Sqrt(x * x + y * y);
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
