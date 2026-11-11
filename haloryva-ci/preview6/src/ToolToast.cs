using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CursorFlow
{
    // Liste unique des outils de dessin : utilisée par la page Dessin du Studio,
    // par le bouton de souris « Outil de dessin suivant » et par l'indicateur.
    internal static class DrawingTools
    {
        public static readonly string[] Names = { "Stylo", "Surligneur", "Ligne droite", "Rectangle", "Ellipse",
            "Flèche droite", "Flèche libre", "Forme libre", "Numéro", "Texte" };
        public static readonly string[] Captions = { "Stylo", "Surligneur", "Ligne", "Rectangle", "Ellipse",
            "Flèche", "Flèche libre", "Auto-formes", "Numéro", "Texte" };
        public static readonly string[] Icons = { "pen", "highlighter", "line", "rect", "ellipse",
            "arrow", "curve-arrow", "shapes", "number", "text" };
        public static int IndexOf(string tool)
        {
            int i = Array.IndexOf(Names, tool);
            return i < 0 ? 0 : i;
        }
    }

    // Indicateur flottant « outil actif » : verre sombre, halo aux couleurs du thème,
    // icône animée, rangée de points qui glisse d'un outil à l'autre, barre de temps.
    // Fenêtre calque transparente aux clics, jamais activée (ne vole pas le focus).
    internal sealed class ToolToast : Form
    {
        private const double InMs = 300, HoldMs = 2400, OutMs = 460, PopMs = 380, MoveMs = 340, ShineMs = 720;

        private readonly Timer timer = new Timer();
        private DateTime inStart, lastTrigger, popStart, moveStart;
        private float activeFrom, activeTo;
        private int index;
        private bool live;
        private float k = 1f;
        private Point origin;
        private Bitmap frame;

        public ToolToast()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Bounds = new Rectangle(-200, -200, 4, 4);
            BackColor = Color.Black;
            timer.Interval = 15;
            timer.Tick += delegate { Step(); };
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

        public bool IsLive { get { return live; } }

        // Remonte l'indicateur au-dessus des autres calques (sans activation).
        public void Raise()
        {
            if (!live || !IsHandleCreated || !Visible) return;
            const uint flags = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, flags);
        }

        // Affiche (ou met à jour sans clignoter) l'outil courant, en haut à gauche
        // de l'écran qui contient le pointeur.
        public void Announce(int toolIndex, Point cursor)
        {
            DateTime now = DateTime.UtcNow;
            toolIndex = Math.Max(0, Math.Min(DrawingTools.Names.Length - 1, toolIndex));
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) k = Math.Max(1f, Math.Min(3f, g.DpiX / 96f)); }
            catch { k = 1f; }
            Rectangle area = Screen.FromPoint(cursor).WorkingArea;
            origin = new Point(area.Left + (int)Math.Round(4 * k), area.Top + (int)Math.Round(4 * k));

            if (!live || Alpha(now) < .05f)
            {
                inStart = now;
                activeFrom = activeTo = toolIndex;
                moveStart = now.AddMilliseconds(-MoveMs * 2);
            }
            else
            {
                // Déjà visible : l'indicateur ne se rejoue pas, il glisse vers le nouvel outil.
                activeFrom = ActivePosition(now);
                activeTo = toolIndex;
                moveStart = now;
                inStart = now.AddMilliseconds(-InMs * 2);
            }
            index = toolIndex;
            lastTrigger = now;
            popStart = now;
            live = true;
            if (!Visible) Show();
            Raise();
            Step();
            timer.Start();
        }

        private float Alpha(DateTime now)
        {
            double inT = (now - inStart).TotalMilliseconds;
            double outT = (now - lastTrigger).TotalMilliseconds - HoldMs;
            float a = EaseOut(Clamp01(inT / InMs));
            if (outT > 0) a *= 1f - EaseIn(Clamp01(outT / OutMs));
            return a;
        }
        private float ActivePosition(DateTime now)
        {
            float t = EaseOut(Clamp01((now - moveStart).TotalMilliseconds / MoveMs));
            return activeFrom + (activeTo - activeFrom) * t;
        }

        private void Step()
        {
            DateTime now = DateTime.UtcNow;
            double outT = (now - lastTrigger).TotalMilliseconds - HoldMs;
            if (outT >= OutMs)
            {
                live = false;
                timer.Stop();
                Hide();
                return;
            }
            Render(now);
        }

        private void Render(DateTime now)
        {
            Size size = Measure(k);
            if (frame == null || frame.Width != size.Width || frame.Height != size.Height)
            {
                if (frame != null) frame.Dispose();
                frame = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            }
            double inT = (now - inStart).TotalMilliseconds;
            double outT = (now - lastTrigger).TotalMilliseconds - HoldMs;
            float inE = EaseOut(Clamp01(inT / InMs));
            float outE = outT > 0 ? EaseIn(Clamp01(outT / OutMs)) : 0f;
            float alpha = inE * (1f - outE);
            float slide = -(1f - inE) * 52f * k - outE * 34f * k;
            float pop = Clamp01((now - popStart).TotalMilliseconds / PopMs);
            float shine = Clamp01((now - popStart).TotalMilliseconds / ShineMs);
            float remaining = 1f - Clamp01((now - lastTrigger).TotalMilliseconds / HoldMs);

            using (Graphics g = Graphics.FromImage(frame))
                PaintCard(g, size, k, index, ActivePosition(now), pop, shine, remaining, PremiumPalette.Cyan, PremiumPalette.Purple);

            Publish(new Point(origin.X + (int)Math.Round(slide), origin.Y), size, (byte)Math.Max(0, Math.Min(255, (int)Math.Round(alpha * 255f))));
        }

        private void Publish(Point at, Size size, byte constantAlpha)
        {
            IntPtr bitmap = IntPtr.Zero, dc = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                bitmap = frame.GetHbitmap(Color.FromArgb(0));
                dc = Native.CreateCompatibleDC(IntPtr.Zero);
                if (dc == IntPtr.Zero || bitmap == IntPtr.Zero) return;
                old = Native.SelectObject(dc, bitmap);
                Native.POINT dst = new Native.POINT(at.X, at.Y);
                Native.SIZE sz = new Native.SIZE(size.Width, size.Height);
                Native.POINT src = new Native.POINT(0, 0);
                Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
                blend.BlendOp = Native.AC_SRC_OVER;
                blend.SourceConstantAlpha = constantAlpha;
                blend.AlphaFormat = Native.AC_SRC_ALPHA;
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
            if (disposing)
            {
                timer.Dispose();
                if (frame != null) frame.Dispose();
            }
            base.Dispose(disposing);
        }

        // ---------------------------------------------------------------- rendu

        private static float Clamp01(double v) { return (float)Math.Max(0.0, Math.Min(1.0, v)); }
        private static float EaseOut(float t) { float u = 1f - t; return 1f - u * u * u; }
        private static float EaseIn(float t) { return t * t * t; }

        // Taille de la fenêtre calque (carte + marge pour le halo).
        internal static Size Measure(float k)
        {
            return new Size((int)Math.Round(392 * k), (int)Math.Round(152 * k));
        }

        // Dessin pur (sans fenêtre) : réutilisable pour les captures de contrôle.
        //   activeF  : position fractionnaire de l'outil actif dans la rangée de points
        //   pop      : 0..1, rebond de l'icône à chaque changement
        //   shine    : 0..1, reflet qui traverse la carte
        //   remaining: 1..0, barre de temps restant
        internal static void PaintCard(Graphics g, Size total, float k, int tool, float activeF,
            float pop, float shine, float remaining, Color ca, Color cb)
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            float pad = 23f * k;
            RectangleF card = new RectangleF(pad, pad, total.Width - pad * 2, total.Height - pad * 2);
            float radius = 22f * k;
            Color mid = PremiumPalette.Mix(ca, cb, .5);

            // Ombre portée douce puis halo coloré (nombreux anneaux très fins : pas de paliers visibles).
            for (int i = 1; i <= 16; i++)
            {
                float grow = i * 1.05f * k;
                using (GraphicsPath sp = PremiumPalette.Round(new RectangleF(card.X - grow, card.Y - grow + 6f * k, card.Width + grow * 2, card.Height + grow * 2), radius + grow))
                using (Pen pen = new Pen(Color.FromArgb((int)Math.Round(17 * Math.Pow(1 - i / 17.0, 2)), 0, 0, 0), 1.7f * k))
                    g.DrawPath(pen, sp);
            }
            for (int i = 1; i <= 14; i++)
            {
                float grow = i * .95f * k;
                using (GraphicsPath gp = PremiumPalette.Round(new RectangleF(card.X - grow, card.Y - grow, card.Width + grow * 2, card.Height + grow * 2), radius + grow))
                using (Pen pen = new Pen(PremiumPalette.Alpha(mid, (int)Math.Round(9 * Math.Pow(1 - i / 15.0, 2.2))), 1.6f * k))
                    g.DrawPath(pen, gp);
            }

            using (GraphicsPath cp = PremiumPalette.Round(card, radius))
            {
                // Fond verre sombre.
                using (LinearGradientBrush bg = new LinearGradientBrush(card, Color.FromArgb(250, 16, 22, 44), Color.FromArgb(252, 9, 12, 27), 90f))
                    g.FillPath(bg, cp);

                // Lueur d'accent derrière l'icône et reflet en haut.
                float bubble = 64f * k;
                PointF icon = new PointF(card.X + 22f * k + bubble / 2f, card.Y + card.Height / 2f);
                g.SetClip(cp);
                using (GraphicsPath glowPath = new GraphicsPath())
                {
                    glowPath.AddEllipse(icon.X - 150f * k, icon.Y - 120f * k, 300f * k, 240f * k);
                    using (PathGradientBrush pg = new PathGradientBrush(glowPath))
                    {
                        pg.CenterPoint = icon;
                        pg.CenterColor = PremiumPalette.Alpha(ca, 26);
                        pg.SurroundColors = new Color[] { PremiumPalette.Alpha(cb, 0) };
                        g.FillPath(pg, glowPath);
                    }
                }
                RectangleF topLight = new RectangleF(card.X, card.Y, card.Width, card.Height * .5f);
                using (LinearGradientBrush tl = new LinearGradientBrush(topLight, Color.FromArgb(10, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillRectangle(tl, topLight);

                // Reflet diagonal à chaque changement d'outil.
                if (shine > 0f && shine < 1f)
                {
                    float sx = card.X - 90f * k + (card.Width + 180f * k) * shine;
                    PointF[] band = { new PointF(sx, card.Y), new PointF(sx + 70f * k, card.Y),
                        new PointF(sx + 30f * k, card.Bottom), new PointF(sx - 40f * k, card.Bottom) };
                    RectangleF bounds = new RectangleF(sx - 40f * k, card.Y, 110f * k, card.Height);
                    using (LinearGradientBrush sh = new LinearGradientBrush(bounds, Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0f))
                    {
                        ColorBlend blend = new ColorBlend(3);
                        blend.Colors = new Color[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(22, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) };
                        blend.Positions = new float[] { 0f, .5f, 1f };
                        sh.InterpolationColors = blend;
                        g.FillPolygon(sh, band);
                    }
                }
                g.ResetClip();

                // Liseré dégradé.
                using (LinearGradientBrush edge = new LinearGradientBrush(card, PremiumPalette.Alpha(ca, 150), PremiumPalette.Alpha(cb, 150), 20f))
                using (Pen border = new Pen(edge, 1.3f * k))
                    g.DrawPath(border, cp);
                using (GraphicsPath inner = PremiumPalette.Round(RectangleF.Inflate(card, -2f * k, -2f * k), radius - 2f * k))
                using (Pen hair = new Pen(Color.FromArgb(0, 255, 255, 255), 1f))
                    g.DrawPath(hair, inner);

                // Bulle d'icône (rebond à chaque changement).
                float scale = 1f + .16f * (float)Math.Sin(Math.PI * pop) * (1f - pop * .35f);
                float bw = bubble * scale;
                RectangleF bubbleRect = new RectangleF(icon.X - bw / 2f, icon.Y - bw / 2f, bw, bw);
                for (int i = 1; i <= 3; i++)
                {
                    float grow = i * 2.2f * k;
                    using (GraphicsPath hp = PremiumPalette.Round(RectangleF.Inflate(bubbleRect, grow, grow), 18f * k + grow))
                    using (Pen pen = new Pen(PremiumPalette.Alpha(ca, (int)(16 * Math.Pow(1 - i / 4.0, 2))), 2.6f * k))
                        g.DrawPath(pen, hp);
                }
                using (GraphicsPath bp = PremiumPalette.Round(bubbleRect, 19f * k))
                {
                    using (LinearGradientBrush bb = new LinearGradientBrush(bubbleRect, ca, cb, 45f))
                        g.FillPath(bb, bp);
                    using (LinearGradientBrush gloss = new LinearGradientBrush(bubbleRect, Color.FromArgb(28, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                        g.FillPath(gloss, bp);
                    using (Pen rim = new Pen(Color.FromArgb(60, 255, 255, 255), 1f * k))
                        g.DrawPath(rim, bp);
                }
                float inset = bw * .25f;
                StudioIcons.Draw(g, DrawingTools.Icons[tool], new RectangleF(bubbleRect.X + inset, bubbleRect.Y + inset, bw - inset * 2, bw - inset * 2), Color.White, 2.7f);

                // Textes.
                float tx = icon.X + bubble / 2f + 20f * k;
                float tw = card.Right - 22f * k - tx;
                Font eyebrow = PremiumPalette.Font(7.4f * k, 2);
                Font title = PremiumPalette.Font(19.5f * k, 2);
                Font counter = PremiumPalette.Font(8f * k, 1);
                using (StringFormat left = new StringFormat(StringFormat.GenericTypographic))
                using (StringFormat right = new StringFormat(StringFormat.GenericTypographic))
                {
                    left.FormatFlags |= StringFormatFlags.NoWrap; right.FormatFlags |= StringFormatFlags.NoWrap;
                    right.Alignment = StringAlignment.Far;
                    using (SolidBrush eb = new SolidBrush(PremiumPalette.Alpha(PremiumPalette.Mix(ca, Color.White, .55), 215)))
                        g.DrawString(L.T("OUTIL DE DESSIN"), eyebrow, eb, new RectangleF(tx, card.Y + 17f * k, tw, 16f * k), left);
                    using (SolidBrush cb2 = new SolidBrush(PremiumPalette.Alpha(PremiumPalette.Subtle, 230)))
                        g.DrawString((tool + 1) + " / " + DrawingTools.Names.Length, counter, cb2, new RectangleF(tx, card.Y + 16f * k, tw, 16f * k), right);
                }
                PremiumPalette.GradientText(g, DrawingTools.Captions[tool], title,
                    new Rectangle((int)Math.Round(tx), (int)Math.Round(card.Y + 31f * k), (int)Math.Round(tw), (int)Math.Round(34f * k)),
                    Color.White, PremiumPalette.Mix(ca, Color.White, .42));

                // Rangée de points : la pastille active glisse d'un outil à l'autre.
                int n = DrawingTools.Names.Length;
                float dot = 6f * k, gap = 7f * k, extra = 17f * k;
                float dy = card.Y + 77f * k;
                float x = tx;
                for (int i = 0; i < n; i++)
                {
                    float near = Math.Max(0f, 1f - Math.Abs(i - activeF));
                    float w = dot + extra * near;
                    RectangleF pill = new RectangleF(x, dy - dot / 2f, w, dot);
                    Color baseColor = PremiumPalette.Mix(ca, cb, n > 1 ? i / (double)(n - 1) : 0);
                    Color fill = PremiumPalette.Mix(PremiumPalette.Faint, baseColor, near);
                    if (near > .08f)
                        for (int h = 1; h <= 0; h++)
                            using (GraphicsPath hp = PremiumPalette.Round(RectangleF.Inflate(pill, h * 1.6f * k, h * 1.6f * k), dot / 2f + h * 1.6f * k))
                            using (SolidBrush hb = new SolidBrush(PremiumPalette.Alpha(baseColor, (int)(34 * near / h))))
                                g.FillPath(hb, hp);
                    using (GraphicsPath pp = PremiumPalette.Round(pill, dot / 2f))
                    using (SolidBrush pb = new SolidBrush(PremiumPalette.Alpha(fill, (int)(125 + 130 * near))))
                        g.FillPath(pb, pp);
                    x += w + gap;
                }

                // Barre de temps restant.
                float barX = tx, barW = tw, barY = card.Bottom - 15f * k, barH = 2.6f * k;
                using (GraphicsPath track = PremiumPalette.Round(new RectangleF(barX, barY, barW, barH), barH / 2f))
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(26, 255, 255, 255)))
                    g.FillPath(tb, track);
                if (remaining > .01f)
                {
                    RectangleF fillRect = new RectangleF(barX, barY, Math.Max(barH, barW * remaining), barH);
                    using (GraphicsPath lp = PremiumPalette.Round(fillRect, barH / 2f))
                    using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(barX, barY, barW, barH), PremiumPalette.Alpha(ca, 210), PremiumPalette.Alpha(cb, 210), 0f))
                        g.FillPath(lb, lp);
                }
            }
        }
    }
}
