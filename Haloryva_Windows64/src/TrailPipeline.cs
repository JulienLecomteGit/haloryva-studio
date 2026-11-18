using System;
using System.Collections.Generic;
using System.Drawing;

namespace CursorFlow
{
    // The desktop and the preview feed the same history with monotone timestamps.
    // The render-only head is never inserted into History or its sampling clock.
    internal sealed partial class TrailPipeline
    {
        internal const int MaximumRetainedCorners = 128;
        internal readonly TrailHistory History = new TrailHistory();
        internal readonly List<TrailDot> RenderPoints = new List<TrailDot>(1536);
        private readonly TrailDot headDot = new TrailDot(Point.Empty, DateTime.MinValue, 0);
        private readonly List<TrailDot> linearInput = new List<TrailDot>(1536);
        private readonly List<StoppedHead> stoppedHeads = new List<StoppedHead>();
        private sealed class StoppedHead
        {
            internal long AfterGroup;
            internal TrailDot Dot;
            internal bool IsCorner;
        }

        internal void Move(Point point, DateTime time, int spacing, int life, bool active)
        { Move((PointF)point, time, spacing, life, active); }

        internal void Move(PointF point, DateTime time, int spacing, int life, bool active)
        {
            Expire(time, life);
            if (History.HasInput && (!active || (point != History.LastInputExactPoint &&
                (History.Points.Count == 0 || (time - History.LastInputTime).TotalMilliseconds < 0 ||
                (time - History.LastInputTime).TotalMilliseconds > TrailHistory.MaxInputGapMs ||
                (time - History.LastMotionTime).TotalMilliseconds > TrailHistory.MaxInputGapMs))))
                StopHead();
            History.Move(point, time, spacing, life, active);
            ExpireHeads(time, life);
        }

        internal void Prepare(Point head, DateTime time, int life, bool linear)
        { Prepare((PointF)head, time, life, linear); }

        internal void Prepare(PointF head, DateTime time, int life, bool linear)
        {
            History.Expire(time, life);
            ExpireHeads(time, life);
            RenderPoints.Clear();
            if (linear)
            {
                linearInput.Clear();
                int end = 0;
                foreach (TrailDot dot in History.Points)
                {
                    while (end < stoppedHeads.Count && stoppedHeads[end].AfterGroup < dot.Id)
                        AppendStopped(stoppedHeads[end++]);
                    linearInput.Add(dot);
                    while (end < stoppedHeads.Count && stoppedHeads[end].AfterGroup == dot.Id)
                        AppendStopped(stoppedHeads[end++]);
                }
                while (end < stoppedHeads.Count) AppendStopped(stoppedHeads[end++]);
                TrailPath.Compact(linearInput, RenderPoints);
            }
            else RenderPoints.AddRange(History.Points);
            if (!linear || !History.HasInput ||
                head != History.LastInputExactPoint ||
                (time - History.LastMotionTime).TotalMilliseconds < 0 ||
                (time - History.LastMotionTime).TotalMilliseconds >= Math.Max(100, Math.Min(2800, life)))
                return;
            TrailDot last = RenderPoints.Count == 0 ? null : RenderPoints[RenderPoints.Count - 1];
            if (last != null && (last.ExactPoint == head || History.LastMotionTime < last.Time)) return;
            headDot.ExactPoint = head;
            // A repeated stationary input may advance the sampling clock; it
            // must never refresh the visible age of an unmoved head.
            headDot.Time = History.LastMotionTime;
            headDot.Speed = History.LastInputSpeed;
            headDot.BreakBefore = last == null;
            RenderPoints.Add(headDot);
        }

        private void StopHead()
        {
            if (!History.HasInput) return;
            TrailDot tail = History.Points.Count == 0 ? null : History.Points[History.Points.Count - 1];
            if (tail != null && (tail.ExactPoint == History.LastInputExactPoint || History.LastMotionTime < tail.Time)) return;
            if (LastRetainedMatchesInput()) return;
            // One final endpoint per stopped portion, never a particle emission.
            // Preserve the endpoint's true age while the input anchor is reset.
            stoppedHeads.Add(new StoppedHead { AfterGroup = History.LastGroupId,
                Dot = new TrailDot(History.LastInputExactPoint, History.LastMotionTime, History.LastInputSpeed, History.LastGroupId) });
        }

        // The analytic preview can name a real corner between two 2 ms samples.
        // Keep that dated vertex for continuous rendering without emitting a
        // motif or raising the history's 500/s budget. Never invent its position.
        internal bool PreserveInputCorner()
        {
            if (!History.HasInput) return false;
            TrailDot tail = History.Points.Count == 0 ? null : History.Points[History.Points.Count - 1];
            if (tail != null && tail.ExactPoint == History.LastInputExactPoint && tail.Time == History.LastMotionTime)
            { tail.KeepVertex = true; return true; }
            if (LastRetainedMatchesInput())
            { stoppedHeads[stoppedHeads.Count - 1].Dot.KeepVertex = true; return true; }
            int corners = 0;
            foreach (StoppedHead retained in stoppedHeads) if (retained.IsCorner) corners++;
            if (corners >= MaximumRetainedCorners) return false;
            TrailDot corner = new TrailDot(History.LastInputExactPoint, History.LastMotionTime,
                History.LastInputSpeed, History.LastGroupId);
            corner.KeepVertex = true;
            stoppedHeads.Add(new StoppedHead { AfterGroup = History.LastGroupId, Dot = corner, IsCorner = true });
            return true;
        }

        private bool LastRetainedMatchesInput()
        {
            if (stoppedHeads.Count == 0) return false;
            StoppedHead last = stoppedHeads[stoppedHeads.Count - 1];
            return last.AfterGroup == History.LastGroupId && last.Dot.ExactPoint == History.LastInputExactPoint &&
                last.Dot.Time == History.LastMotionTime;
        }

        private void AppendStopped(StoppedHead ended)
        {
            ended.Dot.BreakBefore = linearInput.Count == 0 ||
                linearInput[linearInput.Count - 1].Id != ended.AfterGroup;
            linearInput.Add(ended.Dot);
        }

        private void ExpireHeads(DateTime time, int life)
        {
            life = Math.Max(100, Math.Min(2800, life));
            for (int i = stoppedHeads.Count - 1; i >= 0; i--)
                if ((time - stoppedHeads[i].Dot.Time).TotalMilliseconds >= life) stoppedHeads.RemoveAt(i);
        }

        internal void Clear()
        { History.Clear(); RenderPoints.Clear(); linearInput.Clear(); stoppedHeads.Clear(); }
        internal void ResetInput() { StopHead(); History.ResetInput(); }
        internal void Expire(DateTime time, int life) { History.Expire(time, life); ExpireHeads(time, life); }
        internal bool HasLiveContent(DateTime time, int life)
        {
            Expire(time, life);
            double age = (time - History.LastMotionTime).TotalMilliseconds;
            return History.Points.Count > 0 || stoppedHeads.Count > 0 ||
                (History.HasInput && age >= 0 && age < Math.Max(100, Math.Min(2800, life)));
        }
    }

    internal static class TrailMotifLayout
    {
        internal static int Quantity(int value) { return Math.Max(1, Math.Min(5, value)); }
        internal static bool LogoGroup(long id, int every)
        { return (Math.Max(1, id) - 1) % Math.Max(1, Math.Min(12, every)) == 0; }
        internal static float LaneOffset(int lane, int quantity, float gap)
        { return (lane - (Quantity(quantity) - 1) / 2f) * gap; }
        internal static PointF Normal(IList<TrailDot> dots, int index)
        {
            Point center = dots[index].Point;
            Point previous = index > 0 && !dots[index].BreakBefore ? dots[index - 1].Point : center;
            Point next = index + 1 < dots.Count && !dots[index + 1].BreakBefore ? dots[index + 1].Point : center;
            double x = next.X - (double)previous.X, y = next.Y - (double)previous.Y;
            double length = Math.Sqrt(x * x + y * y);
            return length < .001 ? new PointF(0, 1) : new PointF((float)(-y / length), (float)(x / length));
        }
    }
}
