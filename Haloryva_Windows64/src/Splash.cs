using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CursorFlow
{
    // Écran de démarrage : carte sombre avec le logo animé, affichée ~1,6 s puis fondu.
    // Fenêtre en couches (UpdateLayeredWindow), insensible aux clics : elle ne gêne jamais le travail.
    internal sealed class SplashScreen : Form
    {
        private const int FadeIn = 260, Hold = 1250, FadeOut = 340;
        private readonly Timer timer = new Timer();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private Bitmap frame;
        private readonly Size total;
        private readonly int pad;
        private bool closing;

        public SplashScreen()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            PremiumPalette.InitScale(this);
            pad = PremiumPalette.S(22);
            total = new Size(PremiumPalette.S(470) + pad * 2, PremiumPalette.S(230) + pad * 2);
            Size = total;
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.X + (area.Width - total.Width) / 2, area.Y + (area.Height - total.Height) / 2 - area.Height / 12);
            timer.Interval = 16; timer.Tick += delegate { Step(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_NCHITTEST) { m.Result = new IntPtr(Native.HTTRANSPARENT); return; }
            base.WndProc(ref m);
        }

        // Affiche le splash puis rend la main : l'animation continue dans la boucle de messages de l'application.
        public static SplashScreen Begin()
        {
            try
            {
                SplashScreen s = new SplashScreen();
                s.Show();
                s.Step();
                s.timer.Start();
                return s;
            }
            catch { return null; }
        }

        private void Step()
        {
            if (IsDisposed || !IsHandleCreated) return;
            double ms = clock.ElapsedMilliseconds, alpha;
            if (ms < FadeIn) alpha = ms / FadeIn;
            else if (ms < FadeIn + Hold) alpha = 1.0;
            else if (ms < FadeIn + Hold + FadeOut) alpha = 1.0 - (ms - FadeIn - Hold) / FadeOut;
            else { if (!closing) { closing = true; timer.Stop(); Close(); } return; }
            alpha = Math.Max(0, Math.Min(1, alpha));
            float eased = (float)(1.0 - Math.Pow(1.0 - Math.Min(1.0, ms / (FadeIn + 200.0)), 3));
            try { Render(ms, eased); Publish((byte)(alpha * 255)); } catch { }
        }

        private void Render(double ms, float rise)
        {
            if (frame == null) frame = new Bitmap(total.Width, total.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(frame))
            {
                PremiumPalette.Quality(g); g.Clear(Color.Transparent);
                Rectangle card = new Rectangle(pad, pad, total.Width - pad * 2, total.Height - pad * 2);
                for (int i = 8; i >= 1; i--)
                {
                    Rectangle r = Rectangle.Inflate(card, i * PremiumPalette.S(2), i * PremiumPalette.S(2));
                    using (GraphicsPath p = PremiumPalette.Round(r, PremiumPalette.S(18) + i * PremiumPalette.S(2)))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(5, 0, 0, 0))) g.FillPath(b, p);
                }
                using (GraphicsPath p = PremiumPalette.Round(card, PremiumPalette.S(18)))
                {
                    using (LinearGradientBrush bg = new LinearGradientBrush(card, Color.FromArgb(14, 20, 40), Color.FromArgb(7, 10, 22), 60f)) g.FillPath(bg, p);
                    using (Pen edge = new Pen(Color.FromArgb(200, 48, 63, 108), 1f)) g.DrawPath(edge, p);
                }
                // Liseré supérieur dégradé : rappel de la barre du Studio.
                using (LinearGradientBrush top = new LinearGradientBrush(new Rectangle(card.X, card.Y, card.Width, 4), Brand.Cyan, Brand.Purple, 0f))
                {
                    GraphicsState st = g.Save();
                    using (GraphicsPath p = PremiumPalette.Round(card, PremiumPalette.S(18))) g.SetClip(p, CombineMode.Intersect);
                    g.FillRectangle(top, card.X, card.Y, card.Width, PremiumPalette.S(3));
                    g.Restore(st);
                }

                int d = PremiumPalette.S(92), lx = card.X + PremiumPalette.S(34), ly = card.Y + (card.Height - d) / 2 - PremiumPalette.S(8);
                float dy = (1f - rise) * PremiumPalette.S(10);
                Brand.DrawLogo(g, new RectangleF(lx, ly + dy, d, d), (float)(ms * .09));
                int tx = lx + d + PremiumPalette.S(28), tw = card.Right - tx - PremiumPalette.S(24);
                Font big = PremiumPalette.Font(21f, 2);
                PremiumPalette.TextAt(g, "Haloryva", big, PremiumPalette.Text, new Rectangle(tx, card.Y + PremiumPalette.S(54) + (int)dy, tw, PremiumPalette.S(36)), ContentAlignment.MiddleLeft);
                int bw = PremiumPalette.TextWidth("Haloryva", big);
                PremiumPalette.TextAt(g, "Studio", big, PremiumPalette.Cyan, new Rectangle(tx + bw + PremiumPalette.S(6), card.Y + PremiumPalette.S(54) + (int)dy, tw, PremiumPalette.S(36)), ContentAlignment.MiddleLeft);
                PremiumPalette.TextAt(g, Brand.Tagline, PremiumPalette.Font(9.25f, 0), PremiumPalette.Subtle, new Rectangle(tx, card.Y + PremiumPalette.S(92) + (int)dy, tw, PremiumPalette.S(22)), ContentAlignment.MiddleLeft);
                Rectangle chip = new Rectangle(tx, card.Y + PremiumPalette.S(124) + (int)dy, PremiumPalette.S(60), PremiumPalette.S(22));
                using (GraphicsPath p = PremiumPalette.Round(chip, chip.Height / 2))
                using (SolidBrush b = new SolidBrush(PremiumPalette.Alpha(PremiumPalette.Purple, 40)))
                using (Pen edge = new Pen(PremiumPalette.Alpha(PremiumPalette.Purple, 120))) { g.FillPath(b, p); g.DrawPath(edge, p); }
                PremiumPalette.TextAt(g, "v" + Brand.Version, PremiumPalette.Font(8.5f, 1), Color.FromArgb(205, 190, 255), chip, ContentAlignment.MiddleCenter);
                PremiumPalette.TextAt(g, L.T("par ") + Brand.Author, PremiumPalette.Font(9f, 0), PremiumPalette.Faint, new Rectangle(chip.Right + PremiumPalette.S(10), chip.Y, tw, chip.Height), ContentAlignment.MiddleLeft);

                // Barre de chargement : remplissage continu + reflet qui glisse.
                int by = card.Bottom - PremiumPalette.S(26), bx = card.X + PremiumPalette.S(34), bwid = card.Width - PremiumPalette.S(68), bh = PremiumPalette.S(4);
                using (GraphicsPath track = PremiumPalette.Round(new Rectangle(bx, by, bwid, bh), bh / 2))
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(40, 120, 140, 200))) g.FillPath(tb, track);
                float prog = (float)Math.Min(1.0, ms / (FadeIn + Hold));
                prog = 1f - (1f - prog) * (1f - prog);
                int fw = Math.Max(bh, (int)(bwid * prog));
                using (GraphicsPath fill = PremiumPalette.Round(new Rectangle(bx, by, fw, bh), bh / 2))
                using (LinearGradientBrush fb = new LinearGradientBrush(new Rectangle(bx, by, Math.Max(2, fw), bh), Brand.Cyan, Brand.Purple, 0f)) g.FillPath(fb, fill);
            }
        }

        private void Publish(byte constantAlpha)
        {
            IntPtr bitmap = IntPtr.Zero, dc = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                bitmap = frame.GetHbitmap(Color.FromArgb(0));
                dc = Native.CreateCompatibleDC(IntPtr.Zero);
                if (dc == IntPtr.Zero || bitmap == IntPtr.Zero) return;
                old = Native.SelectObject(dc, bitmap);
                Native.POINT dst = new Native.POINT(Left, Top);
                Native.SIZE sz = new Native.SIZE(total.Width, total.Height);
                Native.POINT src = new Native.POINT(0, 0);
                Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
                blend.BlendOp = Native.AC_SRC_OVER; blend.SourceConstantAlpha = constantAlpha; blend.AlphaFormat = Native.AC_SRC_ALPHA;
                Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref sz, dc, ref src, 0, ref blend, Native.ULW_ALPHA);
            }
            finally
            {
                if (old != IntPtr.Zero && dc != IntPtr.Zero) Native.SelectObject(dc, old);
                if (bitmap != IntPtr.Zero) Native.DeleteObject(bitmap);
                if (dc != IntPtr.Zero) Native.DeleteDC(dc);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); if (frame != null) frame.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
