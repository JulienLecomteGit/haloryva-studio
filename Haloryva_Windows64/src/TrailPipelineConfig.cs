using System;
using System.Drawing;

namespace CursorFlow
{
    // Config adaptation is separate so the real sampling/rendering classes can be
    // tested without profiles, WinRT, hooks, forms or an installed application.
    internal sealed partial class TrailPipeline
    {
        internal void Move(Point point, DateTime time, Config options, bool active)
        { Move((PointF)point, time, options, active); }

        internal void Move(PointF point, DateTime time, Config options, bool active)
        {
            // Linear precision is automatic. A hidden particle-spacing setting
            // must not change the shape after switching from a motif to a line.
            bool linear = options.TrailStyle == "Ligne" || options.TrailStyle == "Lumière" ||
                options.TrailStyle == "Pointillés";
            Move(point, time, linear ? 2 : options.TrailSpacing, options.TrailLife, active);
        }

        internal void Prepare(Point head, DateTime time, Config options)
        { Prepare((PointF)head, time, options); }

        internal void Prepare(PointF head, DateTime time, Config options)
        {
            Prepare(head, time, options.TrailLife, options.TrailStyle == "Ligne" ||
                options.TrailStyle == "Lumière" || options.TrailStyle == "Pointillés");
            if (!options.Trail) RenderPoints.Clear();
        }
    }
}
