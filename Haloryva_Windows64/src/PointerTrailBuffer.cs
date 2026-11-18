using System;

namespace CursorFlow
{
    internal static class PointerTrailFilter
    {
        internal static bool Accept(int sourceSession, int activeSession, DateTime time, DateTime acceptAfter)
        { return sourceSession == activeSession && time != DateTime.MinValue && time >= acceptAfter; }
    }

    internal struct PointerTrailSample
    {
        internal readonly int X, Y;
        internal readonly DateTime Time;
        internal PointerTrailSample(int x, int y, DateTime time)
        { X = x; Y = y; Time = time; }
    }

    // Value-only trail samples. Owned by the mailbox while queued, by the UI after Take.
    // It never changes the final command used by buttons, gestures or annotations.
    internal sealed class PointerTrailBatch
    {
        internal const int Capacity = 64;
        internal const long SampleIntervalTicks = 2 * TimeSpan.TicksPerMillisecond;
        private readonly PointerTrailSample[] samples = new PointerTrailSample[Capacity];
        private int start, count;
        private DateTime slotStarted;
        internal int Count { get { return count; } }
        internal long Dropped { get; private set; }
        internal bool BreakBefore { get; private set; }
        internal PointerTrailSample this[int index]
        {
            get
            {
                if (index < 0 || index >= count) throw new ArgumentOutOfRangeException("index");
                return samples[(start + index) % Capacity];
            }
        }
        internal void MarkDiscontinuity() { BreakBefore = true; }
        internal void Add(int x, int y, DateTime time)
        {
            if (time == DateTime.MinValue) return;
            if (count > 0)
            {
                PointerTrailSample last = this[count - 1];
                if (time < last.Time)
                {
                    Dropped += count; start = count = 0; BreakBefore = true;
                }
                else if (x == last.X && y == last.Y)
                {
                    // A stationary pointer must not continually renew the life of its trail.
                    return;
                }
                else if ((time - slotStarted).Ticks < SampleIntervalTicks)
                {
                    // Keep the newest position within a fixed slot. Do not move its start:
                    // otherwise a 1000 Hz mouse would replace the tail forever.
                    samples[(start + count - 1) % Capacity] = new PointerTrailSample(x, y, time);
                    return;
                }
            }
            if (count == Capacity)
            {
                start = (start + 1) % Capacity; count--; Dropped++; BreakBefore = true;
            }
            samples[(start + count) % Capacity] = new PointerTrailSample(x, y, time);
            count++; slotStarted = time;
        }
    }
}
