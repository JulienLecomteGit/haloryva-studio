using System;

namespace CursorFlow
{
    internal static class FrameTiming
    {
        // Unknown or regressing clocks remain unknown, never a fabricated zero.
        internal static double Milliseconds(long start, long end, long frequency)
        {
            if (start <= 0 || end < start || frequency <= 0) return -1;
            return (end - start) * (1000.0 / frequency);
        }
        internal static double InputAge(DateTime input, DateTime published)
        {
            if (input == DateTime.MinValue || published < input) return -1;
            return (published - input).TotalMilliseconds;
        }
    }
}
