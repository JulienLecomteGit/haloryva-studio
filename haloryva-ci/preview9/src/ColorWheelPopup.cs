using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CursorFlow
{
    // Roue chromatique flottante (dans la fenêtre du Studio) ouverte depuis n'importe quel
    // sélecteur de couleur : anneau de teinte + carré saturation / luminosité, palette rapide
    // et saisie HEX. L'application est en direct ; un clic à l'extérieur ou Échap la referme.
    internal sealed class ColorWheelPopup : Control, IMessageFilter
    {
        private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207, WM_NCLBUTTONDOWN = 0x00A1;
        internal static ColorWheelPopup Active;
        internal static void CloseActive() { if (Active != null) Active.Dismiss(); }

        private readonly Control owner;
        private readonly StudioColorWheel wheel;
        private readonly TextBox hex;
        private string value;
        private int hot = -1, pillTop;
        private bool filtering, closed;
        internal event Action<string> Picked;
        internal event Action Dismissed;

        private static int Pad { get { return PremiumPalette.S(14); } }
        private static int Header { get { return PremiumPalette.S(34); } }

        public ColorWheelPopup(string initial, Control ownerField)
        {
            owner = ownerField;
            value = Visuals.ParseColor(initial, PremiumPalette.Cyan).IsEmpty ? "#22D3EE" : initial.ToUpperInvariant();
            BackColor = PremiumPalette.SurfaceHigh;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            AccessibleRole = AccessibleRole.Dialog; AccessibleName = L.T("Roue chromatique");

            wheel = new StudioColorWheel(Visuals.ParseColor(value, PremiumPalette.Cyan));
            wheel.BackColor = PremiumPalette.SurfaceHigh;
            wheel.Location = new Point(Pad, Header);
            wheel.ColorChanged += delegate(Color c)
            {
                value = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
                if (!hex.Focused) hex.Text = value;
                Invalidate();
                if (Picked != null) Picked(value);
            };
            Controls.Add(wheel);

            Width = wheel.Width + Pad * 2;
            int swatchY = wheel.Bottom + PremiumPalette.S(10);
            int hexY = swatchY + PremiumPalette.S(22) + PremiumPalette.S(14);
            int pillH = PremiumPalette.S(34);
            pillTop = hexY;
            Height = hexY + pillH + Pad;

            hex = new TextBox();
            hex.BorderStyle = BorderStyle.None; hex.BackColor = PremiumPalette.Field; hex.ForeColor = PremiumPalette.Text;
            hex.Font = PremiumPalette.Font(10f, 1); hex.CharacterCasing = CharacterCasing.Upper; hex.MaxLength = 7;
            hex.Text = value; hex.AccessibleName = L.T("Couleur (hexadécimal)");
            hex.Width = Width - Pad * 2 - PremiumPalette.S(84);
            hex.Location = new Point(Pad + PremiumPalette.S(70), hexY + Math.Max(0, (pillH - hex.Height) / 2));
            hex.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { ApplyText(hex.Text); e.SuppressKeyPress = true; } };
            hex.TextChanged += delegate { if (hex.Focused && hex.Text.Length == 7) ApplyText(hex.Text); };
            hex.Leave += delegate { hex.Text = value; };
            Controls.Add(hex);
        }

        private Rectangle CloseRect { get { int d = PremiumPalette.S(26); return new Rectangle(Width - d - PremiumPalette.S(8), PremiumPalette.S(6), d, d); } }
        private Rectangle HexPill { get { return new Rectangle(Pad, pillTop, Width - Pad * 2, PremiumPalette.S(34)); } }
        private Rectangle Swatch(int i)
        {
            int n = PremiumPalette.QuickColors.Length, d = PremiumPalette.S(22);
            int step = n > 1 ? (Width - Pad * 2 - d) / (n - 1) : 0;
            return new Rectangle(Pad + i * step, wheel.Bottom + PremiumPalette.S(10), d, d);
        }

        private void ApplyText(string text)
        {
            string t = (text ?? "").Trim().ToUpperInvariant();
            if (t.Length == 6 && t[0] != '#') t = "#" + t;
            Color c = Visuals.ParseColor(t, Color.Empty);
            if (c.IsEmpty || t.Length != 7 || t[0] != '#') return;
            value = t;
            wheel.ChangeCurrentColor(c);
            Invalidate();
            if (Picked != null) Picked(value);
        }

        // Ouvre la roue sous le sélecteur (ou au-dessus s'il manque de la place).
        internal void ShowIn(Form form, Rectangle ownerScreen)
        {
            if (Active != null) Active.Dismiss();
            Active = this;
            Point at = form.PointToClient(new Point(ownerScreen.Right - Width, ownerScreen.Bottom + PremiumPalette.S(6)));
            if (at.Y + Height > form.ClientSize.Height - 8)
                at.Y = form.PointToClient(new Point(0, ownerScreen.Top - Height - PremiumPalette.S(6))).Y;
            at.X = Math.Max(8, Math.Min(form.ClientSize.Width - Width - 8, at.X));
            at.Y = Math.Max(8, Math.Min(form.ClientSize.Height - Height - 8, at.Y));
            Location = at;
            form.Controls.Add(this); BringToFront();
            Application.AddMessageFilter(this); filtering = true;
            wheel.Focus();
        }

        internal void Dismiss()
        {
            if (closed) return;
            closed = true;
            if (filtering) { Application.RemoveMessageFilter(this); filtering = false; }
            if (Active == this) Active = null;
            if (Parent != null) Parent.Controls.Remove(this);
            Action done = Dismissed;
            Dispose();
            if (done != null) done();
        }

        // Clic n'importe où hors de la roue (et hors du sélecteur qui l'a ouverte) : fermeture.
        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_RBUTTONDOWN || m.Msg == WM_MBUTTONDOWN || m.Msg == WM_NCLBUTTONDOWN)
            {
                Control c = Control.FromHandle(m.HWnd);
                bool inside = false;
                for (Control p = c; p != null; p = p.Parent) if (p == this || p == owner) { inside = true; break; }
                if (!inside && IsHandleCreated) BeginInvoke((Action)Dismiss);
            }
            return false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1;
            for (int i = 0; i < PremiumPalette.QuickColors.Length; i++) if (Rectangle.Inflate(Swatch(i), 3, 3).Contains(e.Location)) h = i;
            if (CloseRect.Contains(e.Location)) h = 100;
            if (h != hot) { hot = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            if (hot == 100) { Dismiss(); return; }
            if (hot >= 0 && hot < PremiumPalette.QuickColors.Length) { hex.Text = PremiumPalette.QuickColors[hot]; ApplyText(PremiumPalette.QuickColors[hot]); }
        }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(PremiumPalette.BackOf(this)); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            Rectangle back = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath shape = PremiumPalette.Round(back, PremiumPalette.S(14)))
            {
                using (SolidBrush b = new SolidBrush(PremiumPalette.SurfaceHigh)) g.FillPath(b, shape);
                using (LinearGradientBrush edge = new LinearGradientBrush(back, PremiumPalette.Alpha(PremiumPalette.Cyan, 130), PremiumPalette.Alpha(PremiumPalette.Purple, 130), 35f))
                using (Pen pen = new Pen(edge, 1.2f)) g.DrawPath(pen, shape);
            }
            PremiumPalette.TextAt(g, L.T("Roue chromatique"), PremiumPalette.Font(10f, 1), PremiumPalette.Text,
                new Rectangle(Pad, PremiumPalette.S(6), Width - Pad * 2 - PremiumPalette.S(34), PremiumPalette.S(26)), ContentAlignment.MiddleLeft);
            Rectangle x = CloseRect;
            StudioIcons.Draw(g, "x", new RectangleF(x.X + x.Width * .25f, x.Y + x.Height * .25f, x.Width * .5f, x.Height * .5f),
                hot == 100 ? PremiumPalette.Text : PremiumPalette.Subtle, 2f);

            Color current = Visuals.ParseColor(value, PremiumPalette.Cyan);
            for (int i = 0; i < PremiumPalette.QuickColors.Length; i++)
            {
                Rectangle r = Swatch(i);
                Color c = Visuals.ParseColor(PremiumPalette.QuickColors[i], Color.White);
                bool on = String.Equals(PremiumPalette.QuickColors[i], value, StringComparison.OrdinalIgnoreCase);
                if (on || i == hot)
                    using (Pen ring = new Pen(on ? Color.White : Color.FromArgb(120, 255, 255, 255), PremiumPalette.Sf(on ? 2f : 1.3f)))
                        g.DrawEllipse(ring, r.X - PremiumPalette.S(3), r.Y - PremiumPalette.S(3), r.Width + PremiumPalette.S(6), r.Height + PremiumPalette.S(6));
                using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, r);
            }

            Rectangle pill = HexPill;
            using (GraphicsPath path = PremiumPalette.Round(pill, PremiumPalette.S(9)))
            using (SolidBrush b = new SolidBrush(PremiumPalette.Field))
            using (Pen edge = new Pen(hex.Focused ? PremiumPalette.Alpha(PremiumPalette.Cyan, 170) : PremiumPalette.Border, 1f))
            { g.FillPath(b, path); g.DrawPath(edge, path); }
            int sd = PremiumPalette.S(18);
            using (SolidBrush sw = new SolidBrush(current)) g.FillEllipse(sw, pill.X + PremiumPalette.S(10), pill.Y + (pill.Height - sd) / 2f, sd, sd);
            using (Pen ring = new Pen(Color.FromArgb(90, 255, 255, 255), 1f)) g.DrawEllipse(ring, pill.X + PremiumPalette.S(10), pill.Y + (pill.Height - sd) / 2f, sd, sd);
            PremiumPalette.TextAt(g, "HEX", PremiumPalette.Font(8.5f, 1), PremiumPalette.Faint,
                new Rectangle(pill.X + PremiumPalette.S(34), pill.Y, PremiumPalette.S(32), pill.Height), ContentAlignment.MiddleLeft);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && filtering) { Application.RemoveMessageFilter(this); filtering = false; }
            if (Active == this) Active = null;
            base.Dispose(disposing);
        }
    }
}
