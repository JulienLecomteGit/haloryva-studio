using System.Diagnostics;

namespace CursorFlow
{
    // Elapsed time of synchronous calls, not GPU time or displayed frame rate.
    // Values are meaningful only when Active is true. No position is captured.
    internal struct RenderStageTiming
    {
        internal bool Active;
        internal double PrepareMs, SurfaceMs, ClearMs, TrailMs, OtherEffectsMs;
        internal double FlushMs, PublishMs, TotalMs;
    }

    // Sub-stages of TrailMs; do not add them again to the overlay total.
    // Buffers/Raster/Resolution may accumulate several bounded retry passes.
    internal struct RibbonStageTiming
    {
        internal bool Active;
        internal double GeometryMs, ResolutionMs, BuffersMs, RasterMs;
        internal double CompositeMs, UploadMs, BlitMs, FallbackMs, TotalMs;
    }

    internal static class RenderStageClock
    {
        internal static long Start(bool active)
        { return active ? Stopwatch.GetTimestamp() : 0; }

        internal static double Elapsed(long start)
        {
            if (start == 0) return 0;
            long end = Stopwatch.GetTimestamp();
            return end < start ? -1 : (end - start) * (1000.0 / Stopwatch.Frequency);
        }
    }
}
