using System;
using System.Collections.Generic;
using System.Drawing;

namespace CursorFlow
{
    // Production geometry/input contracts. No hooks, windows, profiles or files.
    internal static class TrailPrecisionSelfTest
    {
        private static int checks;
        private static readonly DateTime Zero = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            checks++; Console.WriteLine("OK PRECISION " + checks + " " + label);
        }
        private static bool Near(double actual, double expected, double tolerance)
        { return Math.Abs(actual - expected) <= tolerance; }
        private static TrailDot Dot(float x, float y, int ms, float speed)
        { return new TrailDot(new PointF(x, y), Zero.AddMilliseconds(ms), speed); }

        internal static int Run()
        {
            checks = 0;
            try
            {
                Coordinates(); SamplingAndSpeed(); Compaction(); HeadAndPause(); Corners();
                Console.WriteLine("TOTAL PRECISION " + checks + " controles C# reussis.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("ECHEC PRECISION : " + ex); return 1; }
        }

        private static void Coordinates()
        {
            TrailDot p = new TrailDot(new Point(-7, 12), Zero, 1, 41);
            Check(p.Point == new Point(-7, 12) && p.ExactPoint == new PointF(-7, 12) && p.Id == 41,
                "integer constructor and motif identity preserved");
            p.ExactPoint = new PointF(-7.375f, 12.625f);
            Check(p.ExactPoint == new PointF(-7.375f, 12.625f) && p.Point == new Point(-7, 13),
                "fractions retained with a synchronized integer view");
            p.Point = new Point(51, -18);
            Check(p.ExactPoint == new PointF(51, -18), "integer head update synchronizes the exact coordinate");
            p = new TrailDot(new PointF(.125f, -.375f), Zero, 1, 77);
            Check(p.ExactPoint.X == .125f && p.ExactPoint.Y == -.375f && p.Id == 77,
                "fractional constructor does not round the ribbon");
            bool rejected = false;
            try { p.ExactPoint = new PointF(Single.NaN, 0); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && p.ExactPoint == new PointF(.125f, -.375f),
                "invalid coordinate rejected without corrupting the prior point");
        }

        private static void SamplingAndSpeed()
        {
            TrailHistory h = new TrailHistory();
            h.Move(new Point(0, 0), Zero, 2, 700, true);
            h.Move(new Point(7, 5), Zero.AddMilliseconds(3), 2, 700, true);
            Check(h.Points.Count == 2 && Near(h.Points[1].ExactPoint.X, 14.0 / 3, .00001) &&
                Near(h.Points[1].ExactPoint.Y, 10.0 / 3, .00001), "diagonal resampling retains subpixel locations");
            Check(Near((h.Points[1].Time - Zero).TotalMilliseconds, 2, .001) &&
                h.LastInputPoint == new Point(7, 5), "sampling date and latest physical endpoint preserved");
            Check(h.Points[1].Speed > 0 && h.Points[1].Speed < h.LastInputSpeed && h.LastInputSpeed < 3,
                "speed responds progressively at the true sample date");

            TrailHistory fast = new TrailHistory(), slow = new TrailHistory(), submillisecond = new TrailHistory();
            for (int ms = 0; ms <= 40; ms++)
                fast.Move(new Point(ms * 2, 0), Zero.AddMilliseconds(ms), 2, 700, true);
            for (int ms = 0; ms <= 40; ms += 2)
                slow.Move(new Point(ms * 2, 0), Zero.AddMilliseconds(ms), 2, 700, true);
            for (int halfMs = 0; halfMs <= 80; halfMs++)
                submillisecond.Move(new Point(halfMs, 0), Zero.AddTicks(halfMs * 5000L), 2, 700, true);
            Check(Near(fast.LastInputSpeed, slow.LastInputSpeed, .00001) &&
                Near(fast.LastInputSpeed, submillisecond.LastInputSpeed, .00001),
                "same velocity gives the same response at 500, 1000 and 2000 input Hz");
            Check(fast.LastInputSpeed > 1.9f && fast.LastInputSpeed < 2 &&
                fast.LastInputExactPoint == new PointF(80, 0), "speed converges without moving or delaying the head");
            bool bounded = fast.Points.Count <= 22;
            for (int i = 1; i < fast.Points.Count; i++)
                bounded &= (fast.Points[i].Time - fast.Points[i - 1].Time).TotalMilliseconds >= 1.9998;
            Check(bounded, "fractional history keeps its 500 emissions per second budget");
            float before = fast.LastInputSpeed;
            fast.Move(new PointF(80.2f, 0), Zero.AddMilliseconds(41), 2, 700, true);
            Check(fast.LastInputSpeed < before && fast.LastInputSpeed > .2f &&
                fast.LastInputExactPoint == new PointF(80.2f, 0), "sudden deceleration softens width only");
            fast.Move(new PointF(-20.25f, 7.5f), Zero.AddMilliseconds(250), 2, 700, true);
            Check(fast.LastInputSpeed == 0 && fast.Points[fast.Points.Count - 1].BreakBefore &&
                fast.LastInputExactPoint == new PointF(-20.25f, 7.5f), "pause resets width response and does not bridge gestures");
            fast.Move(new PointF(3.125f, 9.25f), Zero.AddMilliseconds(251), 2, 700, false);
            Check(!fast.HasInput, "deactivation remains explicit with fractional input");

            h = new TrailHistory();
            h.Move(new PointF(1.125f, 2.25f), Zero, 2, 100, true);
            h.Move(new PointF(1.13f, 2.25f), Zero.AddMilliseconds(1), 2, 100, true);
            Check(h.LastMotionTime == Zero.AddMilliseconds(1) && h.LastInputExactPoint.X == 1.13f,
                "small valid fractional motion receives its real age");
            h = new TrailHistory();
            h.Move(PointF.Empty, Zero, 2, 700, true);
            h.Move(new PointF(100, 0), Zero, 2, 700, true);
            Check(h.Points.Count == 1 && h.LastInputSpeed == 0 && h.LastInputExactPoint == new PointF(100, 0),
                "equal timestamps never divide by zero or emit an unbounded sample stream");
            h.Move(new PointF(200, 0), Zero.AddTicks(1), 2, 700, true);
            Check(h.LastInputSpeed > 0 && h.LastInputSpeed <= 4 && !Single.IsNaN(h.LastInputSpeed) &&
                !Single.IsInfinity(h.LastInputSpeed) && h.Points.Count == 1,
                "one-tick input stays finite with capped speed and unchanged history budget");
        }

        private static void Compaction()
        {
            List<TrailDot> output = new List<TrailDot>();
            List<TrailDot> input = new List<TrailDot> { Dot(0, 0, 0, 1), Dot(5, .4f, 2, 1), Dot(10, 0, 4, 1) };
            TrailPath.Compact(input, output);
            Check(output.Count == 3, "significant shallow turn is retained");
            input = new List<TrailDot> { Dot(0, 0, 0, 1), Dot(.2f, .3f, 2, 1), Dot(.4f, 0, 4, 1) };
            TrailPath.Compact(input, output);
            Check(output.Count == 3, "subpixel corner is preserved even when integer views coincide");
            input = new List<TrailDot> { Dot(0, 0, 0, 1), Dot(5, .2f, 2, 1), Dot(10, 0, 4, 1) };
            TrailPath.Compact(input, output);
            Check(output.Count == 2, "near-linear span still benefits from bounded compaction");
            input = new List<TrailDot> { Dot(0, 0, 0, 1), Dot(5, 0, 2, 2), Dot(10, 0, 4, 1) };
            TrailPath.Compact(input, output);
            Check(output.Count == 3, "width-driving speed peak survives collinear compaction");
            input = new List<TrailDot> { Dot(0, 0, 0, 1), Dot(1, 0, 6, 1), Dot(10, 0, 8, 1) };
            TrailPath.Compact(input, output);
            Check(output.Count == 3, "uneven motion timing survives collinear compaction");
            input = new List<TrailDot> { Dot(0, 0, 0, 1), Dot(5, 0, 2, 1), Dot(10, 0, 4, 1) };
            input[1].KeepVertex = true; TrailPath.Compact(input, output);
            Check(output.Count == 3, "explicit analytic corner remains a vertex");
            input[1].KeepVertex = false; input[1].BreakBefore = true; TrailPath.Compact(input, output);
            Check(output.Count == 3 && output[1].BreakBefore, "explicit discontinuity cannot be compacted away");
            input = new List<TrailDot> { Dot(0, 0, 0, 1), Dot(5, 0, 2, 1), Dot(0, 0, 4, 1) };
            TrailPath.Compact(input, output);
            Check(output.Count == 3, "complete reversal keeps its extreme point");
            input = new List<TrailDot>();
            for (int i = 0; i <= 100; i++) input.Add(Dot(i * 2, 0, i * 2, 1));
            TrailPath.Compact(input, output);
            bool valid = output[0] == input[0] && output[output.Count - 1] == input[input.Count - 1] && output.Count < input.Count / 4;
            for (int i = 1; i < output.Count; i++) valid &= (output[i].Time - output[i - 1].Time).TotalMilliseconds <= 16.001;
            Check(valid, "uniform long line reduces work while preserving endpoints and time span");
        }

        private static void HeadAndPause()
        {
            TrailPipeline p = new TrailPipeline();
            PointF end = new PointF(2.2f, 0);
            p.Move(PointF.Empty, Zero, 2, 100, true);
            p.Move(end, Zero.AddMilliseconds(3), 2, 100, true);
            int count = p.History.Points.Count;
            p.Prepare(end, Zero.AddMilliseconds(4), 100, true);
            TrailDot head = p.RenderPoints[p.RenderPoints.Count - 1];
            Check(head.ExactPoint == end && head.Time == Zero.AddMilliseconds(3) && p.History.Points.Count == count,
                "render-only fractional head remains exact without changing history");
            Check(p.History.Points[count - 1].Point == head.Point && p.History.Points[count - 1].ExactPoint != head.ExactPoint,
                "rounded equality cannot suppress the real head");
            p.Move(end, Zero.AddMilliseconds(10), 2, 100, true);
            p.Prepare(end, Zero.AddMilliseconds(10), 100, true);
            Check(p.RenderPoints[p.RenderPoints.Count - 1].Time == Zero.AddMilliseconds(3),
                "stationary fractional head is not rejuvenated");
            p.Prepare(new PointF(2.3f, 0), Zero.AddMilliseconds(11), 100, true);
            bool invented = false;
            foreach (TrailDot dot in p.RenderPoints) invented |= dot.ExactPoint == new PointF(2.3f, 0);
            Check(!invented, "unobserved fractional head is not invented");
            p.ResetInput();
            p.Move(new PointF(50.75f, 3), Zero.AddMilliseconds(12), 2, 100, true);
            p.Prepare(new PointF(50.75f, 3), Zero.AddMilliseconds(13), 100, true);
            bool oldEnd = false, separated = false;
            foreach (TrailDot dot in p.RenderPoints)
            {
                oldEnd |= dot.ExactPoint == end && dot.Time == Zero.AddMilliseconds(3);
                separated |= dot.ExactPoint == new PointF(50.75f, 3) && dot.BreakBefore;
            }
            Check(oldEnd && separated, "reset preserves the old fractional endpoint and starts a separate gesture");
            p.Expire(Zero.AddMilliseconds(120), 100);
            Check(!p.HasLiveContent(Zero.AddMilliseconds(120), 100), "all fractional endpoints expire normally");
        }

        private static void Corners()
        {
            TrailPipeline p = new TrailPipeline();
            Check(!p.PreserveInputCorner(), "missing input cannot create a corner");
            p.Move(PointF.Empty, Zero, 2, 700, true);
            p.Move(new PointF(10, 0), Zero.AddTicks(25000), 2, 700, true);
            int before = p.History.Points.Count;
            Check(p.PreserveInputCorner() && p.PreserveInputCorner() && p.History.Points.Count == before,
                "analytic corner is idempotent and emits no history sample");
            p.Move(new PointF(10, 10), Zero.AddMilliseconds(5), 2, 700, true);
            p.Prepare(new PointF(10, 10), Zero.AddMilliseconds(5), 700, true);
            int found = 0; bool ordered = true; DateTime previous = DateTime.MinValue;
            foreach (TrailDot dot in p.RenderPoints)
            {
                ordered &= dot.Time >= previous; previous = dot.Time;
                if (dot.ExactPoint == new PointF(10, 0) && dot.Time == Zero.AddTicks(25000))
                { found++; Check(dot.KeepVertex && !dot.BreakBefore, "inter-sample corner remains connected and protected"); }
            }
            Check(found == 1 && ordered, "corner occurs once in chronological order");
            p.Prepare(new PointF(10, 10), Zero.AddMilliseconds(5), 700, false);
            Check(p.RenderPoints.Count == p.History.Points.Count, "analytic corners do not change motif counts");

            p = new TrailPipeline();
            p.Move(PointF.Empty, Zero, 40, 700, true);
            p.Move(new PointF(5, 0), Zero.AddMilliseconds(1), 40, 700, true); p.PreserveInputCorner();
            p.Move(new PointF(5, 5), Zero.AddTicks(15000), 40, 700, true); p.PreserveInputCorner();
            p.Move(new PointF(10, 5), Zero.AddMilliseconds(3), 40, 700, true);
            p.Prepare(new PointF(10, 5), Zero.AddMilliseconds(3), 700, true);
            int breaks = 0;
            foreach (TrailDot dot in p.RenderPoints) if (dot.BreakBefore) breaks++;
            Check(p.RenderPoints.Count == 4 && breaks == 1, "two corners in one history interval remain connected");

            p = new TrailPipeline(); int rejected = 0;
            p.Move(PointF.Empty, Zero, 40, 700, true);
            PointF last = PointF.Empty;
            for (int i = 1; i <= 200; i++)
            {
                last = new PointF(i * .1f, i % 2 == 0 ? 0 : 3);
                p.Move(last, Zero.AddTicks(i * 1000), 40, 700, true);
                if (!p.PreserveInputCorner()) rejected++;
            }
            p.Prepare(last, Zero.AddMilliseconds(20), 700, true);
            Check(rejected > 0 && p.RenderPoints.Count <= p.History.Points.Count + TrailPipeline.MaximumRetainedCorners + 1,
                "analytic-corner storage remains bounded under adversarial input");
            p.Expire(Zero.AddMilliseconds(800), 700);
            Check(!p.HasLiveContent(Zero.AddMilliseconds(800), 700), "retained corners expire and release their budget");
        }
    }
}
