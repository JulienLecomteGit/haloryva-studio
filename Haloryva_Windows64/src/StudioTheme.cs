using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CursorFlow
{
    // Design system V2 « Aurora » : jetons de couleur, échelle DPI, polices
    // mises en cache et icônes vectorielles (aucun glyphe Unicode fragile).
    internal static class PremiumPalette
    {
        internal static readonly Color Background = Color.FromArgb(7, 10, 22);
        internal static readonly Color Sidebar = Color.FromArgb(10, 14, 30);
        internal static readonly Color Header = Color.FromArgb(9, 13, 27);
        internal static readonly Color Surface = Color.FromArgb(14, 20, 40);
        internal static readonly Color SurfaceHigh = Color.FromArgb(19, 27, 53);
        internal static readonly Color Field = Color.FromArgb(21, 30, 58);
        internal static readonly Color FieldHover = Color.FromArgb(28, 40, 76);
        internal static readonly Color Border = Color.FromArgb(31, 42, 76);
        internal static readonly Color BorderStrong = Color.FromArgb(48, 63, 108);
        internal static readonly Color Text = Color.FromArgb(238, 242, 255);
        internal static readonly Color Subtle = Color.FromArgb(150, 163, 198);
        internal static readonly Color Faint = Color.FromArgb(99, 112, 150);
        // Couleurs d'accent : modifiables à chaud par le choix du thème (Préférences).
        internal static Color Cyan = Color.FromArgb(34, 211, 238);
        internal static Color Purple = Color.FromArgb(150, 108, 255);
        internal static readonly string[] AccentNames = { "Aurora", "Sunset", "Émeraude", "Néon", "Glacier" };
        internal static readonly string[] AccentSwatches = { "#22D3EE|#966CFF", "#FB923C|#EC4899", "#34D399|#38BDF8", "#F472B6|#A855F7", "#7DD3FC|#6366F1" };
        internal static string Accent = "Aurora";
        internal static void ApplyAccent(string name)
        {
            int i = Array.IndexOf(AccentNames, name); if (i < 0) i = 0;
            string[] parts = AccentSwatches[i].Split('|');
            Cyan = ColorTranslator.FromHtml(parts[0]); Purple = ColorTranslator.FromHtml(parts[1]);
            Accent = AccentNames[i];
        }
        internal static readonly Color Pink = Color.FromArgb(244, 114, 182);
        internal static readonly Color Green = Color.FromArgb(52, 211, 153);
        internal static readonly Color Amber = Color.FromArgb(251, 191, 36);
        internal static readonly Color Red = Color.FromArgb(248, 113, 113);
        // Alias historiques conservés pour la compatibilité des anciens appels.
        internal static readonly Color Card = Surface;
        internal static readonly Color CardBottom = Surface;
        internal static readonly Color Line = Border;

        internal static readonly string[] QuickColors = {
            "#22D3EE", "#38BDF8", "#966CFF", "#F472B6", "#FB7185", "#FBBF24", "#34D399", "#F8FAFC" };

        private static float scale = 1f;
        internal static float Scale { get { return scale; } }
        internal static float ForcedScale = 0f; // tests de rendu uniquement
        internal static void InitScale(Control reference)
        {
            if (ForcedScale > 0f) { scale = ForcedScale; return; }
            try { using (Graphics g = reference.CreateGraphics()) scale = Math.Max(1f, Math.Min(3f, g.DpiX / 96f)); }
            catch { scale = 1f; }
        }
        // Toutes les dimensions en pixels passent par S() : à 125 / 150 %, les
        // polices (en points) et les boîtes grandissent ensemble, sans chevauchement.
        internal static int S(int value) { return (int)Math.Round(value * scale); }
        internal static float Sf(float value) { return value * scale; }

        private static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        private static string semiboldFamily;
        internal static Font Font(float size, bool bold) { return Font(size, bold ? 2 : 0); }
        // Titre en dégradé (texte converti en tracé, lissé).
        internal static void GradientText(Graphics g, string text, Font font, Rectangle area, Color a, Color b)
        {
            text = L.T(text);
            if (String.IsNullOrEmpty(text) || area.Width < 4) return;
            using (GraphicsPath path = new GraphicsPath())
            using (StringFormat fmt = new StringFormat(StringFormat.GenericTypographic))
            {
                fmt.LineAlignment = StringAlignment.Center; fmt.Trimming = StringTrimming.EllipsisCharacter; fmt.FormatFlags |= StringFormatFlags.NoWrap;
                float em = font.SizeInPoints * g.DpiY / 72f;
                path.AddString(text, font.FontFamily, (int)font.Style, em, area, fmt);
                RectangleF bounds = path.GetBounds();
                if (bounds.Width < 1) return;
                SmoothingMode old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
                using (LinearGradientBrush brush = new LinearGradientBrush(new RectangleF(bounds.X - 1, bounds.Y, bounds.Width + 2, bounds.Height), a, b, 0f))
                    g.FillPath(brush, path);
                g.SmoothingMode = old;
            }
        }
        // weight : 0 normal, 1 semi-gras, 2 gras.
        internal static Font Font(float size, int weight)
        {
            string key = size.ToString("0.00") + "|" + weight;
            Font f;
            if (fonts.TryGetValue(key, out f)) return f;
            if (semiboldFamily == null)
            {
                semiboldFamily = "";
                foreach (FontFamily family in FontFamily.Families)
                    if (family.Name == "Segoe UI Semibold") { semiboldFamily = family.Name; break; }
            }
            if (weight == 1 && semiboldFamily.Length > 0) f = new Font(semiboldFamily, size, FontStyle.Regular, GraphicsUnit.Point);
            else f = new Font("Segoe UI", size, weight == 0 ? FontStyle.Regular : FontStyle.Bold, GraphicsUnit.Point);
            fonts[key] = f;
            return f;
        }

        internal static GraphicsPath Round(Rectangle rect, int radius) { return Round((RectangleF)rect, radius); }
        internal static GraphicsPath Round(RectangleF rect, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            if (d < 2) { p.AddRectangle(rect); return p; }
            p.AddArc(rect.X, rect.Y, d, d, 180, 90);
            p.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            p.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
        internal static void TextAt(Graphics g, string text, Font font, Color color, Rectangle area, ContentAlignment align)
        {
            text = L.T(text);
            TextFormatFlags flags = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            if (align == ContentAlignment.MiddleCenter || align == ContentAlignment.TopCenter) flags |= TextFormatFlags.HorizontalCenter;
            else if (align == ContentAlignment.MiddleRight) flags |= TextFormatFlags.Right;
            else flags |= TextFormatFlags.Left;
            flags |= align == ContentAlignment.TopCenter || align == ContentAlignment.TopLeft ? TextFormatFlags.Top : TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, text ?? "", font, area, color, flags);
        }
        internal static void WrapText(Graphics g, string text, Font font, Color color, Rectangle area)
        {
            text = L.T(text);
            TextRenderer.DrawText(g, text ?? "", font, area, color,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
        internal static int TextWidth(string text, Font font)
        {
            text = L.T(text);
            return TextRenderer.MeasureText(text ?? "", font, new Size(4000, 200), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
        }
        internal static Color Alpha(Color c, int alpha) { return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c); }
        internal static Color Mix(Color a, Color b, double t) { return Visuals.Lerp(a, b, t); }
        internal static void Quality(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }
        // Remplit le fond d'un contrôle avec la couleur réelle de l'ancêtre opaque.
        internal static Color BackOf(Control c)
        {
            Control p = c.Parent;
            while (p != null && p.BackColor.A < 255) p = p.Parent;
            return p == null ? Background : p.BackColor;
        }
        // Bulle d'icône à dégradé (cartes, en-têtes, tuiles).
        internal static void IconBubble(Graphics g, Rectangle r, string icon, Color a, Color b, bool strong)
        {
            using (GraphicsPath path = Round(r, Math.Max(4, r.Width * 30 / 100)))
            {
                using (LinearGradientBrush fill = new LinearGradientBrush(r, Alpha(a, strong ? 235 : 40), Alpha(b, strong ? 235 : 34), 45f))
                    g.FillPath(fill, path);
                using (Pen edge = new Pen(Alpha(a, strong ? 120 : 70), 1f)) g.DrawPath(edge, path);
            }
            int pad = r.Width * 26 / 100;
            StudioIcons.Draw(g, icon, new RectangleF(r.X + pad, r.Y + pad, r.Width - pad * 2, r.Height - pad * 2),
                strong ? Color.White : a, 2f);
        }
    }

    // Icônes au trait (grille 24 × 24), dessinées en GDI+ et nettes à tout DPI.
    internal static class StudioIcons
    {
        internal static void Draw(Graphics g, string name, RectangleF r, Color color, float stroke)
        {
            if (String.IsNullOrEmpty(name) || r.Width < 2 || r.Height < 2) return;
            GraphicsState state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float k = Math.Min(r.Width, r.Height) / 24f;
                g.TranslateTransform(r.X + (r.Width - 24 * k) / 2, r.Y + (r.Height - 24 * k) / 2);
                g.ScaleTransform(k, k);
                using (Pen p = new Pen(color, Math.Max(1.1f, stroke * Math.Max(1f, PremiumPalette.Scale) / Math.Max(.6f, k) * .85f)))
                using (SolidBrush b = new SolidBrush(color))
                {
                    p.StartCap = p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                    Paint(g, name, p, b);
                }
            }
            finally { g.Restore(state); }
        }
        private static void Poly(Graphics g, Pen p, params float[] xy)
        {
            PointF[] pts = new PointF[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[i * 2], xy[i * 2 + 1]);
            g.DrawLines(p, pts);
        }
        private static void RoundBox(Graphics g, Pen p, float x, float y, float w, float h, float r)
        {
            using (GraphicsPath path = PremiumPalette.Round(new RectangleF(x, y, w, h), r)) g.DrawPath(p, path);
        }
        private static void Pointer(Graphics g, Pen p, SolidBrush b, float x, float y, float s, bool fill)
        {
            PointF[] a = { new PointF(x, y), new PointF(x, y + 14 * s), new PointF(x + 3.6f * s, y + 10.6f * s),
                new PointF(x + 6.2f * s, y + 16 * s), new PointF(x + 8.4f * s, y + 15 * s), new PointF(x + 5.9f * s, y + 9.8f * s),
                new PointF(x + 10.4f * s, y + 9.6f * s) };
            if (fill) g.FillPolygon(b, a);
            g.DrawPolygon(p, a);
        }
        private static void Paint(Graphics g, string name, Pen p, SolidBrush b)
        {
            switch (name)
            {
                case "home":
                    Poly(g, p, 3.5f, 11, 12, 3.8f, 20.5f, 11);
                    Poly(g, p, 5.5f, 9.6f, 5.5f, 20, 18.5f, 20, 18.5f, 9.6f);
                    Poly(g, p, 10, 20, 10, 14.5f, 14, 14.5f, 14, 20); break;
                case "halo":
                    g.DrawEllipse(p, 3.5f, 3.5f, 17, 17);
                    g.FillEllipse(b, 9.6f, 9.6f, 4.8f, 4.8f);
                    g.DrawArc(p, 6.8f, 6.8f, 10.4f, 10.4f, 200, 80); break;
                case "trail":
                    g.DrawBezier(p, 3, 18, 7, 18, 9, 6, 14, 7);
                    g.FillEllipse(b, 16.2f, 4.6f, 4.2f, 4.2f);
                    g.FillEllipse(b, 2.2f, 16.6f, 2.6f, 2.6f);
                    g.FillEllipse(b, 18.8f, 13.6f, 2.4f, 2.4f); break;
                case "click":
                    Pointer(g, p, b, 9.5f, 8.5f, .9f, false);
                    Poly(g, p, 5.6f, 3.2f, 6.6f, 5.6f); Poly(g, p, 2.6f, 7.4f, 5, 8.2f);
                    Poly(g, p, 10.6f, 2.6f, 10.4f, 5.2f); Poly(g, p, 3.4f, 12.6f, 5.6f, 11.4f); break;
                case "mouse":
                    RoundBox(g, p, 6, 2.8f, 12, 18.4f, 6);
                    Poly(g, p, 12, 6.6f, 12, 10); Poly(g, p, 6.2f, 11.4f, 17.8f, 11.4f); break;
                case "pen":
                    Poly(g, p, 15.5f, 4.5f, 19.5f, 8.5f, 8.5f, 19.5f, 3.8f, 20.2f, 4.5f, 15.5f, 15.5f, 4.5f);
                    Poly(g, p, 13.2f, 6.8f, 17.2f, 10.8f); break;
                case "present":
                    RoundBox(g, p, 2.8f, 4, 18.4f, 12.4f, 2.2f);
                    Poly(g, p, 12, 16.4f, 12, 20.2f); Poly(g, p, 8, 20.2f, 16, 20.2f);
                    Poly(g, p, 7.4f, 12.6f, 10.4f, 9.4f, 12.8f, 11.4f, 16.6f, 7.6f); break;
                case "settings":
                    g.DrawEllipse(p, 8.6f, 8.6f, 6.8f, 6.8f);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        g.DrawLine(p, 12 + (float)Math.Cos(a) * 6.6f, 12 + (float)Math.Sin(a) * 6.6f,
                            12 + (float)Math.Cos(a) * 9.2f, 12 + (float)Math.Sin(a) * 9.2f);
                    }
                    g.DrawEllipse(p, 5.4f, 5.4f, 13.2f, 13.2f); break;
                case "power":
                    g.DrawArc(p, 4, 4.6f, 16, 16, -50, 280); Poly(g, p, 12, 2.6f, 12, 11.4f); break;
                case "play":
                    g.FillPolygon(b, new PointF[] { new PointF(8, 5), new PointF(19, 12), new PointF(8, 19) }); break;
                case "pause":
                    g.FillRectangle(b, 6.5f, 5, 3.8f, 14); g.FillRectangle(b, 13.7f, 5, 3.8f, 14); break;
                case "undo":
                    Poly(g, p, 9, 5, 4.6f, 9.4f, 9, 13.8f);
                    g.DrawBezier(p, 4.6f, 9.4f, 14, 9.4f, 19.6f, 10.6f, 19.6f, 19); break;
                case "redo":
                    Poly(g, p, 15, 5, 19.4f, 9.4f, 15, 13.8f);
                    g.DrawBezier(p, 19.4f, 9.4f, 10, 9.4f, 4.4f, 10.6f, 4.4f, 19); break;
                case "trash":
                    Poly(g, p, 4, 6.5f, 20, 6.5f); Poly(g, p, 9.5f, 6.5f, 9.5f, 3.8f, 14.5f, 3.8f, 14.5f, 6.5f);
                    Poly(g, p, 6, 6.5f, 7.1f, 20.2f, 16.9f, 20.2f, 18, 6.5f);
                    Poly(g, p, 10, 10.5f, 10, 16.4f); Poly(g, p, 14, 10.5f, 14, 16.4f); break;
                case "export":
                    Poly(g, p, 12, 3.6f, 12, 14.6f); Poly(g, p, 7.6f, 8, 12, 3.6f, 16.4f, 8);
                    Poly(g, p, 4, 14, 4, 20, 20, 20, 20, 14); break;
                case "upload":
                    Poly(g, p, 12, 15.4f, 12, 4.2f); Poly(g, p, 7.6f, 8.6f, 12, 4.2f, 16.4f, 8.6f);
                    Poly(g, p, 4, 15, 4, 20, 20, 20, 20, 15); break;
                case "download":
                    Poly(g, p, 12, 3.6f, 12, 14.6f); Poly(g, p, 7.6f, 10.2f, 12, 14.6f, 16.4f, 10.2f);
                    Poly(g, p, 4, 15, 4, 20, 20, 20, 20, 15); break;
                case "sparkles":
                    Spark(g, b, 10, 12, 7.2f); Spark(g, b, 18.2f, 5.6f, 3.4f); Spark(g, b, 18, 17.4f, 2.6f); break;
                case "check":
                    Poly(g, p, 5, 12.6f, 9.8f, 17.2f, 19.2f, 6.8f); break;
                case "chevron-down": Poly(g, p, 6, 9.2f, 12, 15.2f, 18, 9.2f); break;
                case "chevron-right": Poly(g, p, 9.4f, 5.6f, 15.6f, 12, 9.4f, 18.4f); break;
                case "chevron-left": Poly(g, p, 14.6f, 5.6f, 8.4f, 12, 14.6f, 18.4f); break;
                case "x": Poly(g, p, 6, 6, 18, 18); Poly(g, p, 18, 6, 6, 18); break;
                case "minus": Poly(g, p, 5.5f, 12, 18.5f, 12); break;
                case "square": RoundBox(g, p, 5.5f, 5.5f, 13, 13, 2); break;
                case "squares":
                    RoundBox(g, p, 4.5f, 8.5f, 11, 11, 2); Poly(g, p, 8.5f, 8.5f, 8.5f, 4.5f, 19.5f, 4.5f, 19.5f, 15.5f, 15.5f, 15.5f); break;
                case "keyboard":
                    RoundBox(g, p, 2.6f, 6, 18.8f, 12, 2.4f);
                    for (int i = 0; i < 4; i++) g.FillEllipse(b, 5.6f + i * 3.8f, 8.8f, 1.8f, 1.8f);
                    for (int i = 0; i < 4; i++) g.FillEllipse(b, 5.6f + i * 3.8f, 11.6f, 1.8f, 1.8f);
                    Poly(g, p, 8.4f, 15.2f, 15.6f, 15.2f); break;
                case "loupe":
                    g.DrawEllipse(p, 3.8f, 3.8f, 12.4f, 12.4f); Poly(g, p, 14.6f, 14.6f, 20.2f, 20.2f);
                    Poly(g, p, 10, 7.2f, 10, 12.8f); Poly(g, p, 7.2f, 10, 12.8f, 10); break;
                case "focus":
                    Poly(g, p, 3.6f, 8.4f, 3.6f, 3.6f, 8.4f, 3.6f); Poly(g, p, 15.6f, 3.6f, 20.4f, 3.6f, 20.4f, 8.4f);
                    Poly(g, p, 20.4f, 15.6f, 20.4f, 20.4f, 15.6f, 20.4f); Poly(g, p, 8.4f, 20.4f, 3.6f, 20.4f, 3.6f, 15.6f);
                    g.FillEllipse(b, 9.6f, 9.6f, 4.8f, 4.8f); break;
                case "highlighter":
                    Poly(g, p, 14.6f, 3.6f, 20.4f, 9.4f, 11.6f, 18.2f, 5.8f, 12.4f, 14.6f, 3.6f);
                    Poly(g, p, 5.8f, 12.4f, 3.6f, 18.6f, 5.4f, 20.4f, 11.6f, 18.2f); break;
                case "line": Poly(g, p, 4.5f, 19.5f, 19.5f, 4.5f); break;
                case "rect": RoundBox(g, p, 3.8f, 5.6f, 16.4f, 12.8f, 1.8f); break;
                case "ellipse": g.DrawEllipse(p, 2.8f, 5.4f, 18.4f, 13.2f); break;
                case "arrow":
                    Poly(g, p, 4.5f, 19.5f, 19, 5); Poly(g, p, 10.6f, 5, 19, 5, 19, 13.4f); break;
                case "curve-arrow":
                    g.DrawBezier(p, 3.6f, 18.4f, 6, 8, 12, 17, 18.6f, 6.2f);
                    Poly(g, p, 12.6f, 5.6f, 18.6f, 6.2f, 18.6f, 12.2f); break;
                case "shapes":
                    g.DrawEllipse(p, 2.8f, 2.8f, 9.6f, 9.6f); RoundBox(g, p, 12, 12, 9.2f, 9.2f, 1.6f);
                    Poly(g, p, 5.4f, 21, 9, 14.6f, 12.6f, 21, 5.4f, 21); break;
                case "number":
                    g.DrawEllipse(p, 3.2f, 3.2f, 17.6f, 17.6f); Poly(g, p, 10.2f, 9, 12.6f, 7.4f, 12.6f, 16.8f); break;
                case "text":
                    Poly(g, p, 5, 6.6f, 5, 4.4f, 19, 4.4f, 19, 6.6f); Poly(g, p, 12, 4.4f, 12, 19.6f); Poly(g, p, 9, 19.6f, 15, 19.6f); break;
                case "restore":
                    g.DrawArc(p, 4.4f, 4.4f, 15.2f, 15.2f, 160, 300);
                    Poly(g, p, 3.6f, 4.6f, 4.4f, 10.2f, 9.8f, 9.2f); break;
                case "palette":
                    g.DrawEllipse(p, 3.2f, 3.2f, 17.6f, 17.6f);
                    g.FillEllipse(b, 7.2f, 8.6f, 2.6f, 2.6f); g.FillEllipse(b, 11, 6.2f, 2.6f, 2.6f);
                    g.FillEllipse(b, 14.8f, 8.6f, 2.6f, 2.6f); g.FillEllipse(b, 13.6f, 13, 2.6f, 2.6f); break;
                case "sound":
                    Poly(g, p, 4, 9.4f, 7.6f, 9.4f, 12, 5.4f, 12, 18.6f, 7.6f, 14.6f, 4, 14.6f, 4, 9.4f);
                    g.DrawArc(p, 11, 8.4f, 7.2f, 7.2f, -55, 110); g.DrawArc(p, 10.2f, 5, 11.8f, 14, -55, 110); break;
                case "image":
                    RoundBox(g, p, 3.4f, 4.4f, 17.2f, 15.2f, 2.4f); g.FillEllipse(b, 7.4f, 7.6f, 3.2f, 3.2f);
                    Poly(g, p, 3.8f, 17.4f, 9.6f, 12.4f, 13, 15.4f, 15.6f, 13, 20.2f, 17.2f); break;
                case "crosshair":
                    g.DrawEllipse(p, 5, 5, 14, 14); Poly(g, p, 12, 2.4f, 12, 7.2f); Poly(g, p, 12, 16.8f, 12, 21.6f);
                    Poly(g, p, 2.4f, 12, 7.2f, 12); Poly(g, p, 16.8f, 12, 21.6f, 12); break;
                case "bolt":
                    Poly(g, p, 13.4f, 2.8f, 5.6f, 13.4f, 11.6f, 13.4f, 10.6f, 21.2f, 18.4f, 10.4f, 12.4f, 10.4f, 13.4f, 2.8f); break;
                case "info":
                    g.DrawEllipse(p, 3.2f, 3.2f, 17.6f, 17.6f); Poly(g, p, 12, 11, 12, 16.6f); g.FillEllipse(b, 10.9f, 6.8f, 2.2f, 2.2f); break;
                case "layers":
                    Poly(g, p, 12, 3.6f, 20.6f, 8.2f, 12, 12.8f, 3.4f, 8.2f, 12, 3.6f);
                    Poly(g, p, 3.4f, 12.2f, 12, 16.8f, 20.6f, 12.2f); Poly(g, p, 3.4f, 16, 12, 20.6f, 20.6f, 16); break;
                case "droplet":
                    using (GraphicsPath d = new GraphicsPath())
                    {
                        d.AddBezier(12, 3, 12, 3, 5, 10.6f, 5, 14.6f);
                        d.AddBezier(5, 14.6f, 5, 18.6f, 8.2f, 21, 12, 21);
                        d.AddBezier(12, 21, 15.8f, 21, 19, 18.6f, 19, 14.6f);
                        d.AddBezier(19, 14.6f, 19, 10.6f, 12, 3, 12, 3);
                        g.DrawPath(p, d);
                    }
                    break;
                case "move":
                    Poly(g, p, 12, 3, 12, 21); Poly(g, p, 3, 12, 21, 12);
                    Poly(g, p, 9.4f, 5.6f, 12, 3, 14.6f, 5.6f); Poly(g, p, 9.4f, 18.4f, 12, 21, 14.6f, 18.4f);
                    Poly(g, p, 5.6f, 9.4f, 3, 12, 5.6f, 14.6f); Poly(g, p, 18.4f, 9.4f, 21, 12, 18.4f, 14.6f); break;
                case "timer":
                    g.DrawEllipse(p, 4, 5, 16, 16); Poly(g, p, 12, 9, 12, 13, 14.6f, 14.8f); Poly(g, p, 9.6f, 2.6f, 14.4f, 2.6f); break;
                case "eye":
                    using (GraphicsPath e = new GraphicsPath())
                    {
                        e.AddBezier(2.6f, 12, 6, 5.6f, 18, 5.6f, 21.4f, 12);
                        e.AddBezier(21.4f, 12, 18, 18.4f, 6, 18.4f, 2.6f, 12);
                        g.DrawPath(p, e);
                    }
                    g.DrawEllipse(p, 9, 9, 6, 6); break;
                case "ibeam":
                    Poly(g, p, 9, 4, 12, 5.6f, 15, 4); Poly(g, p, 12, 5.6f, 12, 18.4f); Poly(g, p, 9, 20, 12, 18.4f, 15, 20); break;
                case "pointer": Pointer(g, p, b, 7, 3.4f, 1.05f, false); break;
                case "user":
                    g.DrawEllipse(p, 8, 3.6f, 8, 8); g.DrawArc(p, 4.4f, 13.4f, 15.2f, 14, 180, 180); break;
                case "grid":
                    RoundBox(g, p, 3.6f, 3.6f, 7, 7, 1.6f); RoundBox(g, p, 13.4f, 3.6f, 7, 7, 1.6f);
                    RoundBox(g, p, 3.6f, 13.4f, 7, 7, 1.6f); RoundBox(g, p, 13.4f, 13.4f, 7, 7, 1.6f); break;
                case "sidebar":
                    RoundBox(g, p, 3.2f, 4.2f, 17.6f, 15.6f, 2.4f); Poly(g, p, 9.4f, 4.2f, 9.4f, 19.8f); break;
                case "wand":
                    Poly(g, p, 4, 20, 15, 9); Spark(g, b, 17.6f, 6.4f, 3.6f); Spark(g, b, 9, 4.6f, 2.2f); break;
                case "dots":
                    g.FillEllipse(b, 3.4f, 13.4f, 3.6f, 3.6f); g.FillEllipse(b, 10.2f, 9.6f, 3.6f, 3.6f); g.FillEllipse(b, 17, 5.8f, 3.6f, 3.6f); break;
                case "dash":
                    Poly(g, p, 3.6f, 18.4f, 6.8f, 15.2f); Poly(g, p, 10.4f, 11.6f, 13.6f, 8.4f); Poly(g, p, 17.2f, 4.8f, 20.4f, 3.6f); break;
                case "glow":
                    using (Pen soft = new Pen(Color.FromArgb(70, p.Color), p.Width * 3.2f))
                    { soft.StartCap = soft.EndCap = LineCap.Round; g.DrawBezier(soft, 3.4f, 17, 8, 17, 11, 7, 20.6f, 7); }
                    g.DrawBezier(p, 3.4f, 17, 8, 17, 11, 7, 20.6f, 7); break;
                case "star":
                    {
                        PointF[] v = new PointF[10];
                        for (int i = 0; i < 10; i++)
                        {
                            double a = -Math.PI / 2 + i * Math.PI / 5; float d = i % 2 == 0 ? 9.2f : 3.9f;
                            v[i] = new PointF(12 + (float)Math.Cos(a) * d, 12.8f + (float)Math.Sin(a) * d);
                        }
                        g.DrawPolygon(p, v);
                    }
                    break;
                case "heart":
                    using (GraphicsPath h = new GraphicsPath())
                    {
                        h.AddBezier(12, 19.6f, 4, 14.4f, 2.6f, 9.6f, 5.2f, 6.6f);
                        h.AddBezier(5.2f, 6.6f, 7.6f, 3.8f, 11, 5, 12, 7.6f);
                        h.AddBezier(12, 7.6f, 13, 5, 16.4f, 3.8f, 18.8f, 6.6f);
                        h.AddBezier(18.8f, 6.6f, 21.4f, 9.6f, 20, 14.4f, 12, 19.6f);
                        g.DrawPath(p, h);
                    }
                    break;
                case "plus": Poly(g, p, 12, 4.5f, 12, 19.5f); Poly(g, p, 4.5f, 12, 19.5f, 12); break;
                case "ring": g.DrawEllipse(p, 4.5f, 4.5f, 15, 15); break;
                case "dot": g.FillEllipse(b, 8.5f, 8.5f, 7, 7); g.DrawEllipse(p, 4, 4, 16, 16); break;
                case "flash":
                    g.FillEllipse(b, 8, 8, 8, 8);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        g.DrawLine(p, 12 + (float)Math.Cos(a) * 6.8f, 12 + (float)Math.Sin(a) * 6.8f, 12 + (float)Math.Cos(a) * 9.6f, 12 + (float)Math.Sin(a) * 9.6f);
                    }
                    break;
                case "waves":
                    g.DrawEllipse(p, 9.5f, 9.5f, 5, 5); g.DrawEllipse(p, 6, 6, 12, 12);
                    using (Pen faint = new Pen(Color.FromArgb(110, p.Color), p.Width)) g.DrawEllipse(faint, 2.6f, 2.6f, 18.8f, 18.8f); break;
                default:
                    g.DrawEllipse(p, 5, 5, 14, 14); break;
            }
        }
        private static void Spark(Graphics g, SolidBrush b, float x, float y, float r)
        {
            PointF[] v = new PointF[8];
            for (int i = 0; i < 8; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / 4;
                float d = i % 2 == 0 ? r : r * .3f;
                v[i] = new PointF(x + (float)Math.Cos(a) * d, y + (float)Math.Sin(a) * d);
            }
            g.FillPolygon(b, v);
        }
    }

    // Menu de la zone de notification au thème sombre (cohérent avec le Studio).
    internal sealed class StudioMenuRenderer : ToolStripProfessionalRenderer
    {
        private StudioMenuRenderer() : base(new StudioMenuColors()) { RoundedEdges = true; }
        internal static void Apply(ToolStrip menu)
        {
            menu.Renderer = new StudioMenuRenderer();
            menu.BackColor = PremiumPalette.SurfaceHigh;
            menu.ForeColor = PremiumPalette.Text;
            menu.Font = PremiumPalette.Font(9.5f, 0);
            menu.Padding = new Padding(4);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? PremiumPalette.Text : PremiumPalette.Faint;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            Rectangle r = new Rectangle(e.ImageRectangle.X, e.ImageRectangle.Y, e.ImageRectangle.Width, e.ImageRectangle.Height);
            StudioIcons.Draw(g, "check", r, PremiumPalette.Cyan, 2.2f);
        }
        private sealed class StudioMenuColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return PremiumPalette.FieldHover; } }
            public override Color MenuItemSelectedGradientBegin { get { return PremiumPalette.FieldHover; } }
            public override Color MenuItemSelectedGradientEnd { get { return PremiumPalette.FieldHover; } }
            public override Color MenuItemBorder { get { return PremiumPalette.BorderStrong; } }
            public override Color MenuBorder { get { return PremiumPalette.BorderStrong; } }
            public override Color ToolStripDropDownBackground { get { return PremiumPalette.SurfaceHigh; } }
            public override Color ImageMarginGradientBegin { get { return PremiumPalette.SurfaceHigh; } }
            public override Color ImageMarginGradientMiddle { get { return PremiumPalette.SurfaceHigh; } }
            public override Color ImageMarginGradientEnd { get { return PremiumPalette.SurfaceHigh; } }
            public override Color SeparatorDark { get { return PremiumPalette.Border; } }
            public override Color SeparatorLight { get { return PremiumPalette.Border; } }
            public override Color CheckBackground { get { return PremiumPalette.Field; } }
            public override Color CheckSelectedBackground { get { return PremiumPalette.Field; } }
            public override Color CheckPressedBackground { get { return PremiumPalette.Field; } }
        }
    }
}
