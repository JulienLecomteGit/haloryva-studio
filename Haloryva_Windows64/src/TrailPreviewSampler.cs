using System;
using System.Collections.Generic;
using System.Drawing;

namespace CursorFlow
{
    internal enum TrailPreviewMotion { Slow, Fast, Turns, Mouse }

    internal struct TrailPreviewSample
    {
        internal readonly PointF Point;
        internal readonly DateTime Time;
        internal readonly bool Corner;
        internal TrailPreviewSample(PointF point, DateTime time, bool corner)
        { Point = point; Time = time; Corner = corner; }
    }

    // Supplies the actual demonstration path between paints; it never extrapolates
    // physical mouse input. A delayed/hidden preview starts a separate portion.
    internal sealed class TrailPreviewSampler
    {
        internal const int MaximumSamples = 64;
        internal const double MaximumCatchUpMs = 100;
        internal const double StepSeconds = .002;
        internal readonly List<TrailPreviewSample> Samples = new List<TrailPreviewSample>(MaximumSamples);
        internal bool Discontinuous { get; private set; }

        internal static PointF PointAt(TrailPreviewMotion motion, double seconds,
            float width, float height, float topMinimum, float centerOffset)
        {
            seconds = Math.Max(0, seconds);
            if (motion == TrailPreviewMotion.Turns)
            {
                float left = width * .16f, right = width * .82f;
                float top = Math.Max(topMinimum, height * .24f), bottom = height * .78f;
                double phase = (seconds * .95) % 4;
                if (phase < 1) return new PointF(left + (right - left) * (float)phase, top);
                if (phase < 2) return new PointF(right, top + (bottom - top) * (float)(phase - 1));
                if (phase < 3) return new PointF(right - (right - left) * (float)(phase - 2), bottom);
                return new PointF(left, bottom - (bottom - top) * (float)(phase - 3));
            }
            double t = seconds * (motion == TrailPreviewMotion.Slow ? .62 : 3.4);
            return new PointF(width / 2f + (float)Math.Sin(t * .9) * width * .32f,
                height / 2f + centerOffset + (float)Math.Sin(t * 1.8) * height * .22f);
        }

        internal void Generate(TrailPreviewMotion motion, double fromSeconds, double toSeconds,
            DateTime fromTime, DateTime toTime, float width, float height, float topMinimum, float centerOffset)
        {
            Samples.Clear(); Discontinuous = false;
            if (Double.IsNaN(fromSeconds) || Double.IsInfinity(fromSeconds) ||
                Double.IsNaN(toSeconds) || Double.IsInfinity(toSeconds))
                throw new ArgumentOutOfRangeException("toSeconds");
            double elapsed = (toTime - fromTime).TotalMilliseconds;
            double duration = toSeconds - fromSeconds;
            if (motion == TrailPreviewMotion.Mouse) { Discontinuous = true; return; }
            if (duration < 0 || elapsed < 0 ||
                elapsed > MaximumCatchUpMs || duration * 1000 > MaximumCatchUpMs + .001)
            {
                Discontinuous = true;
                Add(motion, toSeconds, toTime, false, width, height, topMinimum, centerOffset);
                return;
            }
            Add(motion, fromSeconds, fromTime, IsCorner(motion, fromSeconds), width, height, topMinimum, centerOffset);
            if (duration <= 0 || toTime <= fromTime) return;
            double cursor = fromSeconds;
            while (cursor < toSeconds - .000000001)
            {
                double next = Math.Min(toSeconds, cursor + StepSeconds);
                bool corner = false;
                if (motion == TrailPreviewMotion.Turns)
                {
                    double nextCorner = (Math.Floor(cursor * .95 + .000000001) + 1) / .95;
                    if (nextCorner <= next + .000000001 && nextCorner <= toSeconds + .000000001)
                    { next = Math.Min(toSeconds, nextCorner); corner = true; }
                }
                if (Samples.Count >= MaximumSamples - 1 && next < toSeconds - .000000001)
                {
                    // Do not publish a truncated demonstration as a continuous path.
                    Samples.Clear(); Discontinuous = true;
                    Add(motion, toSeconds, toTime, false, width, height, topMinimum, centerOffset);
                    return;
                }
                double amount = Math.Max(0, Math.Min(1, (next - fromSeconds) / duration));
                DateTime time = next >= toSeconds - .000000001 ? toTime :
                    fromTime.AddTicks((long)Math.Round((toTime - fromTime).Ticks * amount));
                Add(motion, next, time, corner || IsCorner(motion, next), width, height, topMinimum, centerOffset);
                cursor = next;
            }
        }

        private static bool IsCorner(TrailPreviewMotion motion, double seconds)
        { return motion == TrailPreviewMotion.Turns && Math.Abs(seconds * .95 - Math.Round(seconds * .95)) < .000000001; }

        private void Add(TrailPreviewMotion motion, double seconds, DateTime time, bool corner,
            float width, float height, float topMinimum, float centerOffset)
        {
            PointF point = PointAt(motion, seconds, width, height, topMinimum, centerOffset);
            if (Samples.Count > 0 && time <= Samples[Samples.Count - 1].Time)
            {
                // Sub-tick rounding cannot produce two contradictory positions at one date.
                Samples[Samples.Count - 1] = new TrailPreviewSample(point, time, corner);
                return;
            }
            Samples.Add(new TrailPreviewSample(point, time, corner));
        }
    }
}
