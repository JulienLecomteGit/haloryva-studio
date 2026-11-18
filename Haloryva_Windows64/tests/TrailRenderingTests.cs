using System;

namespace CursorFlow
{
    // Standalone Framework runner: production geometry/rasterizer, no desktop UI.
    internal static class TrailRenderingTests
    {
        [STAThread]
        private static int Main(string[] args)
        {
            return TrailRenderingSelfTest.Run();
        }
    }
}
