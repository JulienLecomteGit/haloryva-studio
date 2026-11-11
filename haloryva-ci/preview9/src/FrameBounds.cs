using System;
using System.Drawing;
namespace CursorFlow
{
    internal static class FrameBounds
    {
        internal static Rectangle Calculate(Rectangle desktop, Rectangle desired, Rectangle mandatory)
        {
            Rectangle wanted = Rectangle.Intersect(desktop, desired);
            Rectangle required = Rectangle.Intersect(desktop, mandatory);
            if (wanted.Width < 1 || wanted.Height < 1) return Rectangle.Empty;
            int width = Math.Min(desktop.Width, Math.Max(384, Math.Min(Math.Max(1152, required.Width), Round(wanted.Width))));
            int height = Math.Min(desktop.Height, Math.Max(384, Math.Min(Math.Max(896, required.Height), Round(wanted.Height))));
            int cx = wanted.Width <= width ? wanted.Left + wanted.Width / 2 : required.Left + required.Width / 2;
            int cy = wanted.Height <= height ? wanted.Top + wanted.Height / 2 : required.Top + required.Height / 2;
            int x = (int)Math.Floor((cx - width / 2.0) / 64) * 64;
            int y = (int)Math.Floor((cy - height / 2.0) / 64) * 64;
            Rectangle anchor = wanted.Width <= width && wanted.Height <= height ? wanted : required;
            x = Math.Max(anchor.Right - width, Math.Min(anchor.Left, x));
            y = Math.Max(anchor.Bottom - height, Math.Min(anchor.Top, y));
            x = Math.Max(desktop.Left, Math.Min(desktop.Right - width, x));
            y = Math.Max(desktop.Top, Math.Min(desktop.Bottom - height, y));
            return new Rectangle(x, y, width, height);
        }
        private static int Round(int x) { return ((x + 127) / 128) * 128; }
    }
}
