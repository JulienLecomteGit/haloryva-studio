using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CursorFlow
{
    // Sélecteur HSV : anneau de teinte + carré saturation / luminosité + aperçu.
    // V2 : mise à l'échelle DPI, centrage automatique et bitmap généré en une passe
    // (l'ancienne version appelait SetPixel 10 000 fois à chaque changement de teinte).
    internal sealed class StudioColorWheel : Control
    {
        private double hue = 185, saturation = .85, lightness = .98;
        private Bitmap satSquare;
        private int lastSquareHue = -1, lastSquareSize = -1;
        private bool dragRing, dragSquare;
        public event Action<Color> ColorChanged;
        public StudioColorWheel(Color initial)
        {
            Size = new Size(PremiumPalette.S(270), PremiumPalette.S(214)); Cursor = Cursors.Cross; TabStop = true;
            BackColor = PremiumPalette.Surface;
            AccessibleName = L.T("Roue chromatique. Flèches gauche droite : teinte. Haut bas : luminosité.");
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            SetColor(initial);
        }
        private float Outer { get { return Math.Min(Height - PremiumPalette.S(8), PremiumPalette.S(206)) / 2f; } }
        private float RingWidth { get { return PremiumPalette.Sf(15); } }
        private PointF Center { get { return new PointF(PremiumPalette.S(4) + Outer, Height / 2f); } }
        private RectangleF Square
        {
            get
            {
                float inner = Outer - RingWidth - PremiumPalette.Sf(8);
                float side = (float)Math.Floor(inner * 1.38f);
                return new RectangleF(Center.X - side / 2, Center.Y - side / 2, side, side);
            }
        }
        private void SetColor(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            lightness = max; saturation = max == 0 ? 0 : d / max;
            if (d >= .0001)
            {
                if (max == r) hue = 60 * (((g - b) / d) % 6);
                else if (max == g) hue = 60 * ((b - r) / d + 2);
                else hue = 60 * ((r - g) / d + 4);
                if (hue < 0) hue += 360;
            }
            Invalidate();
        }
        public void ChangeCurrentColor(Color c)
        {
            // Évite la dérive de teinte quand la couleur reçue est celle déjà affichée.
            Color mine = Visuals.Hsv(hue, saturation, lightness);
            if (Math.Abs(mine.R - c.R) + Math.Abs(mine.G - c.G) + Math.Abs(mine.B - c.B) <= 6) return;
            SetColor(c);
        }
        private void SendChange()
        {
            Invalidate();
            if (ColorChanged != null) ColorChanged(Visuals.Hsv(hue, saturation, lightness));
        }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) { hue = (hue + (e.KeyCode == Keys.Right ? 4 : -4) + 360) % 360; SendChange(); e.Handled = true; }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) { lightness = Math.Max(0, Math.Min(1, lightness + (e.KeyCode == Keys.Up ? .035 : -.035))); SendChange(); e.Handled = true; }
            else base.OnKeyDown(e);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            PointF c = Center;
            double distance = Math.Sqrt(Math.Pow(e.X - c.X, 2) + Math.Pow(e.Y - c.Y, 2));
            dragRing = distance > Outer - RingWidth - PremiumPalette.S(4) && distance < Outer + PremiumPalette.S(4);
            dragSquare = !dragRing && Square.Contains(e.Location);
            UsePoint(e.Location);
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e) { if (e.Button == MouseButtons.Left) UsePoint(e.Location); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { dragRing = dragSquare = false; base.OnMouseUp(e); }
        private void UsePoint(Point p)
        {
            PointF c = Center;
            if (dragRing) { hue = (Math.Atan2(p.Y - c.Y, p.X - c.X) * 180 / Math.PI + 450) % 360; SendChange(); }
            else if (dragSquare)
            {
                RectangleF sq = Square;
                saturation = Math.Max(0, Math.Min(1, (p.X - sq.X) / sq.Width));
                lightness = Math.Max(0, Math.Min(1, 1 - (p.Y - sq.Y) / sq.Height));
                SendChange();
            }
        }
        private void BuildSquare(int side)
        {
            int hi = (int)(hue / 2);
            if (satSquare != null && lastSquareHue == hi && lastSquareSize == side) return;
            if (satSquare != null) satSquare.Dispose();
            satSquare = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            BitmapData data = satSquare.LockBits(new Rectangle(0, 0, side, side), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            int[] row = new int[side];
            for (int y = 0; y < side; y++)
            {
                for (int x = 0; x < side; x++) row[x] = Visuals.Hsv(hue, x / (double)(side - 1), 1 - y / (double)(side - 1)).ToArgb();
                Marshal.Copy(row, 0, new IntPtr(data.Scan0.ToInt64() + (long)y * data.Stride), side);
            }
            satSquare.UnlockBits(data);
            lastSquareHue = hi; lastSquareSize = side;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(PremiumPalette.BackOf(this)); PremiumPalette.Quality(g);
            PointF c = Center; float outer = Outer, rw = RingWidth, mid = outer - rw / 2;
            for (int i = 0; i < 360; i += 2)
                using (Pen pen = new Pen(Visuals.Hsv(i, 1, 1), rw)) g.DrawArc(pen, c.X - mid, c.Y - mid, mid * 2, mid * 2, i - 90, 2.6f);
            RectangleF sq = Square;
            BuildSquare(Math.Max(8, (int)sq.Width));
            using (GraphicsPath clip = PremiumPalette.Round(sq, PremiumPalette.Sf(6)))
            {
                Region old = g.Clip; g.SetClip(clip, CombineMode.Intersect);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.DrawImage(satSquare, sq.X, sq.Y, sq.Width, sq.Height);
                g.Clip = old;
                using (Pen border = new Pen(Color.FromArgb(70, 255, 255, 255))) g.DrawPath(border, clip);
            }
            double angle = (hue - 90) * Math.PI / 180;
            float hx = c.X + (float)(mid * Math.Cos(angle)), hy = c.Y + (float)(mid * Math.Sin(angle));
            float k = PremiumPalette.Sf(9);
            using (SolidBrush cur = new SolidBrush(Visuals.Hsv(hue, 1, 1))) g.FillEllipse(cur, hx - k, hy - k, k * 2, k * 2);
            using (Pen pen = new Pen(Color.White, PremiumPalette.Sf(2.5f))) g.DrawEllipse(pen, hx - k, hy - k, k * 2, k * 2);
            float sx = sq.X + (float)(saturation * sq.Width), sy = sq.Y + (float)((1 - lightness) * sq.Height), sk = PremiumPalette.Sf(7);
            using (Pen pen = new Pen(Color.White, PremiumPalette.Sf(2.2f))) g.DrawEllipse(pen, sx - sk, sy - sk, sk * 2, sk * 2);
            using (Pen pen = new Pen(Color.FromArgb(120, 0, 0, 0), 1f)) g.DrawEllipse(pen, sx - sk - 1.5f, sy - sk - 1.5f, sk * 2 + 3, sk * 2 + 3);
            // Aperçu de la couleur courante à droite de la roue.
            float px = c.X + outer + PremiumPalette.Sf(22);
            if (px + PremiumPalette.Sf(40) < Width)
            {
                float d = PremiumPalette.Sf(44);
                Color current = Visuals.Hsv(hue, saturation, lightness);
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(60, current))) g.FillEllipse(glow, px - PremiumPalette.Sf(6), c.Y - d / 2 - PremiumPalette.Sf(6), d + PremiumPalette.Sf(12), d + PremiumPalette.Sf(12));
                using (SolidBrush swatch = new SolidBrush(current)) g.FillEllipse(swatch, px, c.Y - d / 2, d, d);
                using (Pen p = new Pen(Color.FromArgb(110, 255, 255, 255), 1.4f)) g.DrawEllipse(p, px, c.Y - d / 2, d, d);
                PremiumPalette.TextAt(g, L.T("Teinte ") + (int)Math.Round(hue) + "°", PremiumPalette.Font(8.25f, 0), PremiumPalette.Faint,
                    new Rectangle((int)px - PremiumPalette.S(14), (int)(c.Y + d / 2 + PremiumPalette.S(8)), (int)d + PremiumPalette.S(28), PremiumPalette.S(18)), ContentAlignment.MiddleCenter);
            }
            if (Focused && ShowFocusCues)
                using (Pen f = new Pen(PremiumPalette.Alpha(PremiumPalette.Cyan, 160), 1.2f)) g.DrawEllipse(f, c.X - outer - 2, c.Y - outer - 2, outer * 2 + 4, outer * 2 + 4);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && satSquare != null) satSquare.Dispose();
            base.Dispose(disposing);
        }
    }
}
