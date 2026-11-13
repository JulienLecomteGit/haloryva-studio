using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace CursorFlow
{
    // Les annotations sont un calque independant des effets. Il n'est publie
    // que lorsque les traits changent (et non a chaque frame du pointeur).
    internal sealed class AnnotationOverlayForm : Form
    {
        private readonly CursorController controller;
        private Bitmap buffer;
        public AnnotationOverlayForm(CursorController owner)
        {
            controller = owner;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Bounds = new Rectangle(-100, -100, 2, 2);
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT |
                    Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCHITTEST)
            { m.Result = new IntPtr(Native.HTTRANSPARENT); return; }
            base.WndProc(ref m);
        }
        private static int FloorTo(int n, int block)
        {
            int rem = n % block;
            return rem == 0 ? n : n - (rem < 0 ? rem + block : rem);
        }
        private Rectangle DrawingBounds()
        {
            Rectangle desktop = SystemInformation.VirtualScreen;
            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            foreach (AnnotationStroke stroke in controller.Strokes)
            {
                foreach (Point p in stroke.Points)
                {
                    minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
                }
                if (stroke.Tool == "Texte" && stroke.Points.Count > 0)
                {
                    Point p = stroke.Points[0];
                    maxX = Math.Max(maxX, p.X + Math.Max(80, stroke.Text.Length * stroke.Width * 4));
                    maxY = Math.Max(maxY, p.Y + Math.Max(32, stroke.Width * 7));
                }
            }
            if (minX == int.MaxValue) return Rectangle.Empty;
            const int margin = 72, block = 128;
            int left = Math.Max(desktop.Left, FloorTo(minX - margin, block));
            int top = Math.Max(desktop.Top, FloorTo(minY - margin, block));
            int right = Math.Min(desktop.Right, FloorTo(maxX + margin + block - 1, block));
            int bottom = Math.Min(desktop.Bottom, FloorTo(maxY + margin + block - 1, block));
            if (right <= left || bottom <= top) return Rectangle.Empty;
            return Rectangle.FromLTRB(left, top, right, bottom);
        }
        public void Render()
        {
            Rectangle target = DrawingBounds();
            if (target.IsEmpty || controller.Strokes.Count == 0)
            {
                if (Visible) Hide();
                return;
            }
            if (!Visible) Show();
            if (Bounds != target) Bounds = target;
            if (buffer == null || buffer.Width != target.Width || buffer.Height != target.Height)
            {
                if (buffer != null) buffer.Dispose();
                buffer = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppPArgb);
            }
            using (Graphics g = Graphics.FromImage(buffer))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(-target.X, -target.Y);
                foreach (AnnotationStroke stroke in controller.Strokes)
                    OverlayForm.DrawStroke(g, stroke);
            }
            Publish(target);
        }
        private void Publish(Rectangle rect)
        {
            IntPtr bitmap = IntPtr.Zero, dc = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                bitmap = buffer.GetHbitmap(Color.FromArgb(0));
                dc = Native.CreateCompatibleDC(IntPtr.Zero);
                if (bitmap == IntPtr.Zero || dc == IntPtr.Zero) return;
                previous = Native.SelectObject(dc, bitmap);
                Native.POINT at = new Native.POINT(rect.X, rect.Y);
                Native.SIZE size = new Native.SIZE(rect.Width, rect.Height);
                Native.POINT from = new Native.POINT(0, 0);
                Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
                blend.BlendOp = Native.AC_SRC_OVER;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = Native.AC_SRC_ALPHA;
                Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref at, ref size, dc, ref from,
                    0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                if (previous != IntPtr.Zero && dc != IntPtr.Zero) Native.SelectObject(dc, previous);
                if (bitmap != IntPtr.Zero) Native.DeleteObject(bitmap);
                if (dc != IntPtr.Zero) Native.DeleteDC(dc);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && buffer != null) buffer.Dispose();
            base.Dispose(disposing);
        }
    }
}
