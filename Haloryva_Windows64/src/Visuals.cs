using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CursorFlow
{
    internal sealed class OverlayForm : Form
    {
        private readonly CursorController controller;
        private Bitmap buffer;
        private Graphics surfaceGraphics;
        private readonly List<TrailDot> renderTrail = new List<TrailDot>(1536);
        private readonly StableSurfaceBounds surfaceBounds = new StableSurfaceBounds();
        private readonly System.Diagnostics.Stopwatch surfaceClock = System.Diagnostics.Stopwatch.StartNew();
        internal int SurfaceCreations { get; private set; }
        internal long FallbackFrames { get; private set; }
        internal bool LastPublished { get; private set; }
        internal Size LastSurfaceSize { get; private set; }
        // Surface GDI réutilisable : évite la conversion en HBITMAP à chaque frame.
        private IntPtr surfaceDC = IntPtr.Zero;
        private IntPtr surfaceBitmap = IntPtr.Zero;
        private IntPtr previousBitmap = IntPtr.Zero;
        private bool persistentSurface;
        private bool dibDisabled;
        private Bitmap lensSnapshot;
        private Bitmap logoBitmap;
        private string loadedLogo = "";
        private DateTime snapshotAt = DateTime.MinValue;
        // Effets uniquement : les annotations sont sur un autre calque, indépendant.

        public OverlayForm(CursorController controller)
        {
            this.controller = controller;
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            Bounds = new Rectangle(-100, -100, 4, 4);
            BackColor = Color.Black;
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

        public void Render(DateTime now)
        {
            LastPublished = false;
            Rectangle desktop = SystemInformation.VirtualScreen;
            Point p = controller.CursorPoint;
            Config c = controller.Options;
            bool linearTrail = c.TrailStyle == "Ligne" || c.TrailStyle == "Lumière" || c.TrailStyle == "Pointillés";
            renderTrail.Clear();
            if (c.Trail)
            {
                if (linearTrail) TrailPath.Compact(controller.Trail, renderTrail);
                else renderTrail.AddRange(controller.Trail);
            }
            bool fullscreen = c.Focus;
            Rectangle target;
            if (fullscreen) { target = desktop; surfaceBounds.Reset(); }
            else
            {
                // Englobe le halo déplacé, les traînées encore vivantes et les clics.
                int minX = p.X - 145, minY = p.Y - 145, maxX = p.X + 145, maxY = p.Y + 145;
                if (c.Ring && controller.RingVisibility(now) > 0f)
                {
                    Point center = controller.RingCenter(p);
                    float radius = (c.RingRadius + (c.RingPulse ? 2.7f : 0f)) * controller.RingTextScale;
                    if (controller.ShakeUntil > now) radius *= 1.7f;
                    float border = c.RingThickness / 2f + (c.RingNeon ? c.RingGlowBlur / 2f : 0) + 4f;
                    int rx = (int)Math.Ceiling(radius * (c.RingShape == "Ellipse" ? 1.30f : 1f) + border + (c.RingShape == "Réticule" ? 11 : 0));
                    int ry = (int)Math.Ceiling(radius * (c.RingShape == "Ellipse" ? .78f : 1f) + border);
                    minX = Math.Min(minX, center.X - rx); maxX = Math.Max(maxX, center.X + rx);
                    minY = Math.Min(minY, center.Y - ry); maxY = Math.Max(maxY, center.Y + ry);
                }
                if (c.Trail && renderTrail.Count > 0)
                {
                    // Même géométrie que le rendu : inclure contrôles des Bézier et débordement du glow.
                    float presence = c.TrailWidth * (c.TrailSpeedResponsive ? 2.12f : 1f);
                    float padding;
                    if (linearTrail) padding = presence * (c.TrailStyle == "Lumière" ? (c.PerformanceMode ? 3.6f : 7f) : 1f) / 2f + 5;
                    else if (c.TrailStyle == "Logo") padding = c.TrailLogoSize * .72f + c.TrailSpread + 5;
                    else padding = c.TrailWidth * 1.6f * (c.TrailSpeedResponsive ? 2.08f : 1f) * 1.85f + c.TrailSpread + 5;
                    foreach (TrailDot d in renderTrail)
                        IncludePoint(d.Point.X, d.Point.Y, padding, ref minX, ref minY, ref maxX, ref maxY);
                    if (linearTrail)
                    {
                        float tension = TrailPath.SmoothingTension(c.TrailSmoothness);
                        for (int i = 1; i < renderTrail.Count; i++)
                        {
                            if (renderTrail[i].BreakBefore) continue;
                            PointF a = renderTrail[i - 1].Point, b = renderTrail[i].Point;
                            PointF prev = i < 2 || renderTrail[i - 1].BreakBefore ? a : renderTrail[i - 2].Point;
                            PointF next = i + 1 < renderTrail.Count && !renderTrail[i + 1].BreakBefore ? renderTrail[i + 1].Point : b;
                            PointF c1, c2;
                            TrailPath.CurveControls(prev, a, b, next, tension, out c1, out c2);
                            IncludePoint(c1.X, c1.Y, padding, ref minX, ref minY, ref maxX, ref maxY);
                            IncludePoint(c2.X, c2.Y, padding, ref minX, ref minY, ref maxX, ref maxY);
                        }
                        IncludePoint(p.X, p.Y, padding + 20, ref minX, ref minY, ref maxX, ref maxY);
                    }
                }
                foreach (Ripple r in controller.Ripples)
                {
                    minX = Math.Min(minX, r.Point.X - c.ClickSize - 18);
                    minY = Math.Min(minY, r.Point.Y - c.ClickSize - 18);
                    maxX = Math.Max(maxX, r.Point.X + c.ClickSize + 18);
                    maxY = Math.Max(maxY, r.Point.Y + c.ClickSize + 18);
                }
                // Loupe et texte des touches : toujours inclus dans le calque.
                if (c.Loupe || (c.ZoomOnClick && now < controller.ZoomUntil))
                {
                    Rectangle lr = LensRect(p, c, desktop);
                    minX = Math.Min(minX, lr.Left - 8); maxX = Math.Max(maxX, lr.Right + 8);
                    minY = Math.Min(minY, lr.Top - 8); maxY = Math.Max(maxY, lr.Bottom + 8);
                }
                if (c.KeyDisplay && now < controller.KeysUntil && !String.IsNullOrEmpty(controller.LastKeys))
                {
                    maxX = Math.Max(maxX, p.X + 40 + controller.LastKeys.Length * 17 + 60);
                    minY = Math.Min(minY, p.Y - 90);
                }
                target = surfaceBounds.Calculate(desktop, Rectangle.FromLTRB(minX, minY, maxX, maxY),
                    (c.Trail && controller.Trail.Count > 0) || controller.Ripples.Count > 0, surfaceClock.Elapsed.TotalMilliseconds);
            }
            if (target.Width < 1 || target.Height < 1) return;
            // UpdateLayeredWindow publie position + dimensions + pixels ensemble, sans SetBounds préalable.
            if (buffer == null || buffer.Width != target.Width || buffer.Height != target.Height)
                CreateSurface(target.Width, target.Height);
            if (surfaceGraphics == null) surfaceGraphics = Graphics.FromImage(buffer);
            Graphics g = surfaceGraphics;
            {
                g.ResetTransform(); g.ResetClip();
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceOver;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = c.PerformanceMode ? PixelOffsetMode.Half : PixelOffsetMode.HighQuality;
                g.TranslateTransform(-target.X, -target.Y);
                if (c.Focus) DrawFocus(g, desktop);
                if (c.Trail) DrawTrail(g, now, c);
                if (c.Ring && controller.RingVisibility(now) > 0f)
                {
                    Point ringAt = controller.RingCenter(p);
                    // Mode précision texte : aucun pixel de l'écran n'est capturé.
                    DrawRing(g, now, ringAt, c,
                        controller.ShakeUntil > now, controller.RingVisibility(now));
                }
                if (c.Clicks) DrawRipples(g, now, c);
                if (controller.IsDrawing && (!c.DrawCursorHideWhileDrawing || controller.CurrentStroke == null))
                    DrawDrawingIndicator(g, p, c);
                if (c.Loupe || (c.ZoomOnClick && now < controller.ZoomUntil)) DrawLens(g, now, p, c, desktop);
                if (c.KeyDisplay && now < controller.KeysUntil) DrawKeys(g, p, controller.LastKeys, now, controller.KeysUntil, c.KeyStyle);
            }
            // Le Graphics est désormais persistant : terminer ses écritures avant
            // que UpdateLayeredWindow lise les pixels partagés de la DIB.
            g.Flush(FlushIntention.Sync);
            LastSurfaceSize = target.Size;
            Publish(target);
        }

        // DIB de haut en bas, surface Format32bppPArgb partagée directement
        // avec UpdateLayeredWindow. Sur une machine où la DIB échoue, on conserve
        // le chemin historique à base de GetHbitmap comme solution de repli.
        private static void IncludePoint(float x, float y, float pad, ref int minX, ref int minY, ref int maxX, ref int maxY)
        {
            minX = Math.Min(minX, (int)Math.Floor(x - pad)); minY = Math.Min(minY, (int)Math.Floor(y - pad));
            maxX = Math.Max(maxX, (int)Math.Ceiling(x + pad)); maxY = Math.Max(maxY, (int)Math.Ceiling(y + pad));
        }

        private void CreateSurface(int width, int height)
        {
            SurfaceCreations++;
            ReleaseSurface();
            if (dibDisabled)
            {
                buffer = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                return;
            }
            try
            {
                surfaceDC = Native.CreateCompatibleDC(IntPtr.Zero);
                if (surfaceDC == IntPtr.Zero) throw new InvalidOperationException("CreateCompatibleDC");
                Native.BITMAPINFO info = new Native.BITMAPINFO();
                info.bmiHeader.biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
                info.bmiHeader.biWidth = width;
                info.bmiHeader.biHeight = -height; // top-down
                info.bmiHeader.biPlanes = 1;
                info.bmiHeader.biBitCount = 32;
                info.bmiHeader.biCompression = 0; // BI_RGB
                info.bmiHeader.biSizeImage = (uint)(width * height * 4);
                IntPtr pixels;
                surfaceBitmap = Native.CreateDIBSection(surfaceDC, ref info, 0, out pixels, IntPtr.Zero, 0);
                if (surfaceBitmap == IntPtr.Zero || pixels == IntPtr.Zero)
                    throw new InvalidOperationException("CreateDIBSection");
                previousBitmap = Native.SelectObject(surfaceDC, surfaceBitmap);
                if (previousBitmap == IntPtr.Zero || previousBitmap == new IntPtr(-1))
                    throw new InvalidOperationException("SelectObject");
                buffer = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, pixels);
                persistentSurface = true;
            }
            catch
            {
                dibDisabled = true;
                ReleaseSurface();
                buffer = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                persistentSurface = false;
            }
        }

        private void ReleaseSurface()
        {
            if (surfaceGraphics != null) { surfaceGraphics.Dispose(); surfaceGraphics = null; }
            if (buffer != null) { buffer.Dispose(); buffer = null; }
            if (surfaceDC != IntPtr.Zero)
            {
                if (previousBitmap != IntPtr.Zero && previousBitmap != new IntPtr(-1))
                    Native.SelectObject(surfaceDC, previousBitmap);
                previousBitmap = IntPtr.Zero;
            }
            if (surfaceBitmap != IntPtr.Zero) Native.DeleteObject(surfaceBitmap);
            surfaceBitmap = IntPtr.Zero;
            if (surfaceDC != IntPtr.Zero) Native.DeleteDC(surfaceDC);
            surfaceDC = IntPtr.Zero;
            persistentSurface = false;
        }

        private void Publish(Rectangle bounds)
        {
            if (persistentSurface)
            {
                Native.POINT destination = new Native.POINT(bounds.Left, bounds.Top);
                Native.SIZE size = new Native.SIZE(bounds.Width, bounds.Height);
                Native.POINT source = new Native.POINT(0, 0);
                Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
                blend.BlendOp = Native.AC_SRC_OVER;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = Native.AC_SRC_ALPHA;
                if (Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref destination, ref size,
                    surfaceDC, ref source, 0, ref blend, Native.ULW_ALPHA))
                {
                    LastPublished = true;
                    EffectDiagnostics.Publication(true, 0);
                    return;
                }
                int error = Marshal.GetLastWin32Error();
                EffectDiagnostics.Event("DIB_PUBLISH_FAILED", "win32=" + error);
                // Certains pilotes rejettent les DIB partagées. Ne jamais masquer
                // les effets : repasser au rendu classique et garder l'image courante.
                Bitmap snapshot = new Bitmap(buffer);
                dibDisabled = true;
                ReleaseSurface();
                buffer = snapshot;
            }
            FallbackFrames++;
            IntPtr bitmap = IntPtr.Zero, dc = IntPtr.Zero, oldBitmap = IntPtr.Zero;
            try
            {
                bitmap = buffer.GetHbitmap(Color.FromArgb(0));
                dc = Native.CreateCompatibleDC(IntPtr.Zero);
                if (dc == IntPtr.Zero || bitmap == IntPtr.Zero)
                {
                    EffectDiagnostics.Publication(false, 0);
                    return;
                }
                oldBitmap = Native.SelectObject(dc, bitmap);
                Native.POINT destination = new Native.POINT(bounds.Left, bounds.Top);
                Native.SIZE size = new Native.SIZE(bounds.Width, bounds.Height);
                Native.POINT source = new Native.POINT(0, 0);
                Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
                blend.BlendOp = Native.AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = Native.AC_SRC_ALPHA;
                LastPublished = Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref destination, ref size, dc, ref source, 0, ref blend, Native.ULW_ALPHA);
                int publishedError = LastPublished ? 0 : Marshal.GetLastWin32Error();
                EffectDiagnostics.Publication(LastPublished, publishedError);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero && dc != IntPtr.Zero) Native.SelectObject(dc, oldBitmap);
                if (bitmap != IntPtr.Zero) Native.DeleteObject(bitmap);
                if (dc != IntPtr.Zero) Native.DeleteDC(dc);
            }
        }

        private void DrawFocus(Graphics g, Rectangle desktop)
        {
            IntPtr hwnd = Native.GetForegroundWindow();
            Native.RECT r;
            if (hwnd == IntPtr.Zero || hwnd == Handle || !Native.GetWindowRect(hwnd, out r)) return;
            Rectangle focused = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            using (GraphicsPath path = new GraphicsPath(FillMode.Alternate))
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(146, 5, 10, 22)))
            {
                path.AddRectangle(desktop);
                if (focused.Width > 0 && focused.Height > 0) path.AddRectangle(focused);
                g.FillPath(dim, path);
            }
        }

        private void DrawRing(Graphics g, DateTime now, Point p, Config c, bool shake, float visibility)
        {
            DrawRingCore(g, now, p, c, shake, visibility, controller.IsTextPointer, controller.RingTextScale);
        }
        // Rendu partagé avec l'aperçu du Studio : l'aperçu montre exactement le vrai halo.
        internal static void DrawRingCore(Graphics g, DateTime now, PointF p, Config c, bool shake, float visibility, bool textPointer, float textScale = 1f)
        {
            if (!c.Ring || visibility <= 0f) return;
            Color color = Visuals.ParseColor(c.RingColor, Color.Cyan);
            bool precision = c.RingTextPrecision && c.RingCenterOnText && textPointer;
            float ringWidth = precision ? Math.Max(1f, c.RingThickness * (1f - c.RingTextFocusThinness * .0075f)) : c.RingThickness;
            int glowIntensity = precision ? Math.Min(100, c.RingGlowIntensity + c.RingTextFocusIntensity / 4) : c.RingGlowIntensity;
            if (c.RingColorMode == "Arc-en-ciel") color = Visuals.Hsv(now.TimeOfDay.TotalSeconds * 35, .75, 1);
            else if (c.RingColorMode == "Dynamique") color = Visuals.Hsv(now.TimeOfDay.TotalSeconds * 52, .90, .97);
            if (precision) color = Visuals.Lerp(color, Color.White, c.RingTextFocusIntensity / 420.0);
            int effectiveOpacity = Clamp((int)Math.Round(c.RingOpacity * visibility));
            float pulse = c.RingPulse && !precision ? (float)(Math.Sin(now.TimeOfDay.TotalSeconds * c.RingPulseSpeed) * 2.7) : 0;
            float radius = Math.Max(8, (c.RingRadius + pulse) * textScale);
            if (shake) radius *= (float)(1.5 + .2 * Math.Sin(now.TimeOfDay.TotalSeconds * 16));
            if (c.RingNeon && glowIntensity > 0 && c.RingGlowBlur > 0)
            {
                // Une seule passe en mode fluide, quatre sinon.
                int layers = c.PerformanceMode ? 1 : 4;
                for (int i = layers; i >= 1; i--)
                {
                    int alpha = Math.Max(1, Math.Min(180, (int)(glowIntensity * visibility * (c.PerformanceMode ? .16 : .07 + i * .025))));
                    float thick = ringWidth + c.RingGlowBlur * i / (float)layers;
                    using (Pen glow = new Pen(Color.FromArgb(alpha, color), thick))
                        DrawRingShape(g, glow, p, radius, c.RingShape);
                }
            }
            if (c.RingColorMode == "Dégradé")
            {
                RectangleF gradientBounds = new RectangleF(p.X - radius, p.Y - radius, radius * 2, radius * 2);
                using (LinearGradientBrush gradient = new LinearGradientBrush(gradientBounds,
                    Color.FromArgb(effectiveOpacity, color),
                    Color.FromArgb(effectiveOpacity, Visuals.ParseColor(c.RingColor2, Color.MediumPurple)), 35f))
                using (Pen pen = new Pen(gradient, Math.Max(1f, ringWidth)))
                    DrawRingShape(g, pen, p, radius, c.RingShape);
            }
            else using (Pen pen = new Pen(Color.FromArgb(effectiveOpacity, color), Math.Max(1f, ringWidth)))
                DrawRingShape(g, pen, p, radius, c.RingShape);
        }
        internal static void DrawRingShape(Graphics g, Pen pen, PointF p, float radius, string shape)
        {
            if (shape == "Réticule")
            {
                g.DrawArc(pen, p.X - radius, p.Y - radius, radius * 2, radius * 2, 25, 130);
                g.DrawArc(pen, p.X - radius, p.Y - radius, radius * 2, radius * 2, 205, 130);
                g.DrawLine(pen, p.X - radius - 11, p.Y, p.X - radius + 3, p.Y);
                g.DrawLine(pen, p.X + radius - 3, p.Y, p.X + radius + 11, p.Y);
            }
            else if (shape == "Carré arrondi")
            {
                Rectangle rect = Rectangle.Round(new RectangleF(p.X - radius, p.Y - radius, radius * 2, radius * 2));
                using (GraphicsPath path = Visuals.RoundRect(rect, Math.Max(4, Math.Min(rect.Width / 2 - 1, (int)(radius / 3))))) g.DrawPath(pen, path);
            }
            else if (shape == "Ellipse") g.DrawEllipse(pen, p.X - radius * 1.30f, p.Y - radius * .78f, radius * 2.60f, radius * 1.56f);
            else g.DrawEllipse(pen, p.X - radius, p.Y - radius, radius * 2, radius * 2);
        }

        private void DrawTrail(Graphics g, DateTime now, Config c)
        {
            if (c.TrailStyle == "Logo") EnsureLogo(c.TrailLogoPath);
            DrawTrailCore(g, TrailClock.Now, c, renderTrail, logoBitmap, controller.CursorPoint);
        }
        internal static void DrawTrailCore(Graphics g, DateTime now, Config c, List<TrailDot> dots, Bitmap logoBitmap, Point? head = null)
        {
            if (dots.Count < 1) return;
            string style = c.TrailStyle;
            double life = Math.Max(100, c.TrailLife);
            bool linear = style == "Ligne" || style == "Pointillés" || style == "Lumière";
            if (linear && dots.Count < 2) return;
            Color firstColor = Visuals.ParseColor(c.TrailColor, Color.Cyan);
            Color secondColor = Visuals.ParseColor(c.TrailColor2, Color.MediumPurple);
            if (linear)
            {
                using (Pen wide = new Pen(Color.Transparent, 1))
                using (Pen soft = new Pen(Color.Transparent, 1))
                using (Pen pen = new Pen(Color.Transparent, 1))
                using (Pen core = new Pen(Color.Transparent, 1))
                {
                    wide.StartCap = wide.EndCap = soft.StartCap = soft.EndCap = core.StartCap = core.EndCap = LineCap.Flat;
                    // Ligne/Lumière : des extrémités rondes sur CHAQUE segment se
                    // superposent et dessinent des « perles » lumineuses aux raccords.
                    // Les pointillés conservent leurs embouts ronds intentionnels.
                    pen.StartCap = pen.EndCap = style == "Pointillés" ? LineCap.Round : LineCap.Flat;
                    pen.LineJoin = soft.LineJoin = LineJoin.Round;
                    if (style == "Pointillés") pen.DashStyle = DashStyle.Dash;
                    bool freshHead = head.HasValue && (now - dots[dots.Count - 1].Time).TotalMilliseconds < 150 &&
                        head.Value != dots[dots.Count - 1].Point;
                    int count = dots.Count + (freshHead ? 1 : 0);
                    float tension = TrailPath.SmoothingTension(c.TrailSmoothness);
                    for (int i = 1; i < count; i++)
                    {
                        bool atHead = i == dots.Count;
                        if (!atHead && dots[i].BreakBefore) continue;
                        TrailDot dot = dots[Math.Min(i, dots.Count - 1)];
                        double rem = Math.Max(0, Math.Min(1, 1.0 - (now - dot.Time).TotalMilliseconds / life));
                        if (rem <= 0) continue;
                        Color col = TrailColorFast(c, firstColor, secondColor, i, count, now, -1);
                        int alpha = Clamp((int)(c.TrailOpacity * rem));
                        float speed = c.TrailSpeedResponsive ? (float)(1 + Math.Min(1.4, dot.Speed * .28)) : 1f;
                        float thickness = Math.Max(1, c.TrailWidth * speed * (float)(.55 + .45 * rem));
                        PointF a = dots[i - 1].Point;
                        PointF b = atHead ? head.Value : dot.Point;
                        PointF prev = atHead || i < 2 || dots[i - 1].BreakBefore ? a : dots[i - 2].Point;
                        PointF next = i + 1 < dots.Count && !dots[i + 1].BreakBefore ? dots[i + 1].Point : b;
                        PointF c1, c2;
                        TrailPath.CurveControls(prev, a, b, next, tension, out c1, out c2);
                        if (style == "Lumière")
                        {
                            if (!c.PerformanceMode)
                            { wide.Color = Color.FromArgb(Math.Max(1, alpha / 10), col); wide.Width = thickness * 7f; g.DrawBezier(wide, a, c1, c2, b); }
                            soft.Color = Color.FromArgb(Math.Max(1, alpha / 4), col); soft.Width = thickness * 3.6f;
                            g.DrawBezier(soft, a, c1, c2, b);
                        }
                        pen.Color = Color.FromArgb(alpha, col); pen.Width = thickness;
                        g.DrawBezier(pen, a, c1, c2, b);
                        if (style == "Lumière")
                        {
                            core.Color = Color.FromArgb(Clamp((int)(alpha * .85)), Visuals.Lerp(col, Color.White, .75));
                            core.Width = Math.Max(1f, thickness * .38f); g.DrawBezier(core, a, c1, c2, b);
                        }
                    }
                }
                return;
            }
            int every = Math.Max(1, 6 - c.TrailDensity);
            for (int i = 0; i < dots.Count; i++)
            {
                long id = dots[i].Id;
                if (id % every != 0) continue;   // sélection liée à l'identité : retirer le premier point ne décale pas les autres
                double age = (now - dots[i].Time).TotalMilliseconds;
                double remain = 1.0 - age / life;
                if (remain <= 0) continue;
                int alpha = Clamp((int)(c.TrailOpacity * remain));
                Color col = TrailColorFast(c, firstColor, secondColor, i, dots.Count, now, id);
                Point p = dots[i].Point;
                float spread = c.TrailSpread * (float)(.25 + (1 - remain) * .75);
                float dx = (float)(Math.Sin(id * 12.9898) * spread);
                float dy = (float)(Math.Cos(id * 9.4371) * spread);
                PointF location = new PointF(p.X + dx, p.Y + dy);
                float speed = c.TrailSpeedResponsive ? (float)(1.0 + Math.Min(1.6, dots[i].Speed * .27)) : 1f;
                float size = Math.Max(2f, c.TrailWidth * 1.6f * speed * (float)(.30 + .70 * remain));
                if (style == "Logo")
                {
                    if ((id / every) % Math.Max(1, c.TrailLogoEvery) != 0) continue;
                    float iconSize = c.TrailLogoSize * (float)(.45 + remain * .55);
                    DrawLogo(g, logoBitmap, location, iconSize, alpha, c.TrailLogoRotate ? (float)(id * 23 % 360) : 0, col);
                }
                else using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, col)))
                {
                    if (style == "Points") g.FillEllipse(brush, location.X - size, location.Y - size, size * 2, size * 2);
                    else if (style == "Étoiles") FillStar(g, brush, Point.Round(location), size * 1.6f);
                    else if (style == "Kawaii") FillHeart(g, brush, Point.Round(location), size * 1.7f);
                    else
                    {
                        g.FillEllipse(brush, location.X - size / 2, location.Y - size / 2, size, size);
                        using (Pen spark = new Pen(Color.FromArgb(alpha, col), Math.Max(1, size / 4)))
                        {
                            g.DrawLine(spark, location.X - size * 1.8f, location.Y, location.X + size * 1.8f, location.Y);
                            g.DrawLine(spark, location.X, location.Y - size * 1.8f, location.X, location.Y + size * 1.8f);
                        }
                    }
                }
            }
        }
        private static Color TrailColorFast(Config c, Color first, Color second, int i, int count, DateTime now, long id)
        {
            if (c.TrailColorMode == "Arc-en-ciel") return Visuals.Hsv(((id >= 0 ? id : i) * 17 + now.TimeOfDay.TotalSeconds * 45) % 360, .83, 1);
            return c.TrailColorMode == "Dégradé" ? Visuals.Lerp(first, second, count < 2 ? 0 : (double)i / (count - 1)) : first;
        }
        private void EnsureLogo(string path)
        {
            if (path == loadedLogo) return;
            loadedLogo = path;
            if (logoBitmap != null) { logoBitmap.Dispose(); logoBitmap = null; }
            if (String.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
            try
            {
                using (Image source = Image.FromFile(path))
                {
                    // Limite la mémoire même si le PNG d'origine est très grand.
                    // Même facteur sur les deux axes : le ratio du logo est conservé.
                    double k = Math.Min(1.0, 256.0 / Math.Max(source.Width, source.Height));
                    int w = Math.Max(1, (int)Math.Round(source.Width * k));
                    int h = Math.Max(1, (int)Math.Round(source.Height * k));
                    logoBitmap = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(logoBitmap))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(source, 0, 0, w, h);
                    }
                }
            }
            catch { if (logoBitmap != null) logoBitmap.Dispose(); logoBitmap = null; }
        }
        private static void DrawLogo(Graphics g, Bitmap logoBitmap, PointF at, float size, int alpha, float rotate, Color fallback)
        {
            if (logoBitmap == null)
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(alpha, fallback)))
                    FillStar(g, b, Point.Round(at), size * .46f);
                return;
            }
            GraphicsState state = g.Save();
            try
            {
                g.TranslateTransform(at.X, at.Y);
                if (rotate != 0) g.RotateTransform(rotate);
                float w = size, h = size * logoBitmap.Height / Math.Max(1f, logoBitmap.Width);
                using (ImageAttributes attrs = new ImageAttributes())
                {
                    ColorMatrix matrix = new ColorMatrix(); matrix.Matrix33 = alpha / 255f;
                    attrs.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                    g.DrawImage(logoBitmap, Rectangle.Round(new RectangleF(-w / 2, -h / 2, w, h)), 0, 0, logoBitmap.Width, logoBitmap.Height, GraphicsUnit.Pixel, attrs);
                }
            }
            finally { g.Restore(state); }
        }

        private static void FillStar(Graphics g, Brush b, Point p, float r)
        {
            PointF[] vertices = new PointF[10];
            for (int i = 0; i < vertices.Length; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / 5;
                double d = i % 2 == 0 ? r : r * .45;
                vertices[i] = new PointF(p.X + (float)(Math.Cos(a) * d), p.Y + (float)(Math.Sin(a) * d));
            }
            g.FillPolygon(b, vertices);
        }
        private static void FillHeart(Graphics g, Brush b, Point p, float r)
        {
            PointF[] vertices = new PointF[36];
            for (int i = 0; i < vertices.Length; i++)
            {
                double t = i * 2 * Math.PI / vertices.Length;
                double x = 16 * Math.Pow(Math.Sin(t), 3);
                double y = 13 * Math.Cos(t) - 5 * Math.Cos(2 * t) - 2 * Math.Cos(3 * t) - Math.Cos(4 * t);
                vertices[i] = new PointF(p.X + (float)(r * x / 18), p.Y - (float)(r * y / 18));
            }
            g.FillPolygon(b, vertices);
        }

        private void DrawRipples(Graphics g, DateTime now, Config c)
        {
            DrawRipplesCore(g, now, c, controller.Ripples);
        }
        internal static void DrawRipplesCore(Graphics g, DateTime now, Config c, List<Ripple> ripples)
        {
            foreach (Ripple ripple in ripples)
            {
                double progress = (now - ripple.Time).TotalMilliseconds / Math.Max(200, c.ClickDuration);
                if (progress < 0 || progress > 1) continue;
                Color col = Visuals.ParseColor(ripple.Right ? c.RightColor : c.LeftColor, Color.Green);
                int rings = c.ClickStyle == "Ondes" ? 3 : c.ClickStyle == "Étincelles" ? 2 : 1;
                for (int i = 0; i < rings; i++)
                {
                    double x = Math.Max(0, (progress - i * .15) / (1 - i * .15));
                    if (progress < i * .15 || x >= 1) continue;
                    float radius = (float)(8 + x * c.ClickSize);
                    using (Pen pen = new Pen(Color.FromArgb(Clamp((int)(235 * (1 - x))), col), c.ClickStyle == "Flash" ? 6 : 3))
                        g.DrawEllipse(pen, ripple.Point.X - radius, ripple.Point.Y - radius, radius * 2, radius * 2);
                }
                // Styles auparavant quasi identiques : Flash = disque lumineux, Étincelles = rayons.
                if (c.ClickStyle == "Flash" && progress < .55)
                {
                    double k = progress / .55;
                    float r = (float)(6 + k * c.ClickSize * .55);
                    using (SolidBrush flash = new SolidBrush(Color.FromArgb(Clamp((int)(120 * (1 - k))), col)))
                        g.FillEllipse(flash, ripple.Point.X - r, ripple.Point.Y - r, r * 2, r * 2);
                }
                else if (c.ClickStyle == "Étincelles")
                {
                    float inner = (float)(6 + progress * c.ClickSize * .55);
                    float outer = (float)(14 + progress * c.ClickSize * .95);
                    using (Pen spark = new Pen(Color.FromArgb(Clamp((int)(240 * (1 - progress))), col), 2.4f))
                    {
                        spark.StartCap = spark.EndCap = LineCap.Round;
                        for (int k = 0; k < 8; k++)
                        {
                            double a = k * Math.PI / 4 + (ripple.Right ? Math.PI / 8 : 0);
                            g.DrawLine(spark, ripple.Point.X + (float)Math.Cos(a) * inner, ripple.Point.Y + (float)Math.Sin(a) * inner,
                                ripple.Point.X + (float)Math.Cos(a) * outer, ripple.Point.Y + (float)Math.Sin(a) * outer);
                        }
                    }
                }
            }
        }

        private void DrawDrawingIndicator(Graphics g, Point p, Config c)
        {
            Color color = Visuals.ParseColor(c.DrawCursorLinkedToPen ? c.AnnotationColor : c.DrawCursorColor, Color.Cyan);
            int alpha = Math.Max(30, Math.Min(255, c.DrawCursorOpacity * 255 / 100));
            float radius = c.DrawCursorSize / 2f;
            using (Pen pen = new Pen(Color.FromArgb(alpha, color), c.DrawCursorThickness))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                if (c.DrawCursorStyle == "Croix + anneau" || c.DrawCursorStyle == "Anneau")
                    g.DrawEllipse(pen, p.X - radius, p.Y - radius, radius * 2, radius * 2);
                if (c.DrawCursorStyle == "Croix + anneau" || c.DrawCursorStyle == "Croix")
                {
                    g.DrawLine(pen, p.X - radius - 5, p.Y, p.X + radius + 5, p.Y);
                    g.DrawLine(pen, p.X, p.Y - radius - 5, p.X, p.Y + radius + 5);
                }
                if (c.DrawCursorStyle == "Point central")
                    using (Brush dot = new SolidBrush(Color.FromArgb(alpha, color)))
                        g.FillEllipse(dot, p.X - 4, p.Y - 4, 8, 8);
            }
        }

        internal static void DrawStroke(Graphics g, AnnotationStroke s)
        {
            if (s.Points.Count == 0) return;
            Point first = s.Points[0];
            Point last = s.Points[s.Points.Count - 1];
            if (s.Tool == "Texte")
            {
                if (!String.IsNullOrWhiteSpace(s.Text))
                {
                    using (Font font = new Font("Segoe UI", Math.Max(12, s.Width * 4), FontStyle.Bold))
                    using (Brush shadow = new SolidBrush(Color.FromArgb(170, 0, 0, 0)))
                    using (Brush textBrush = new SolidBrush(s.Color))
                    {
                        g.DrawString(s.Text, font, shadow, first.X + 2, first.Y + 2);
                        g.DrawString(s.Text, font, textBrush, first.X, first.Y);
                    }
                }
                return;
            }
            if (s.Tool == "Numéro")
            {
                using (SolidBrush b = new SolidBrush(s.Color)) g.FillEllipse(b, first.X - 16, first.Y - 16, 32, 32);
                using (Font f = new Font("Segoe UI", 12, FontStyle.Bold))
                using (StringFormat fmt = new StringFormat())
                {
                    fmt.Alignment = StringAlignment.Center; fmt.LineAlignment = StringAlignment.Center;
                    g.DrawString(s.Number.ToString(), f, Brushes.White, new RectangleF(first.X - 16, first.Y - 16, 32, 32), fmt);
                }
                return;
            }
            using (Pen pen = new Pen(Color.FromArgb(s.Tool == "Surligneur" ? 105 : 240, s.Color), s.Tool == "Surligneur" ? s.Width * 4 : s.Width))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                if ((s.Tool == "Triangle" || s.Tool == "Polygone") && s.Points.Count > 2)
                    g.DrawPolygon(pen, s.Points.ToArray());
                else if (s.Tool == "Rectangle" && s.Points.Count > 1)
                    g.DrawRectangle(pen, Math.Min(first.X, last.X), Math.Min(first.Y, last.Y), Math.Abs(last.X - first.X), Math.Abs(last.Y - first.Y));
                else if (s.Tool == "Ellipse" && s.Points.Count > 1)
                    g.DrawEllipse(pen, Math.Min(first.X, last.X), Math.Min(first.Y, last.Y), Math.Abs(last.X - first.X), Math.Abs(last.Y - first.Y));
                else if ((s.Tool == "Flèche" || s.Tool == "Flèche droite") && s.Points.Count > 1)
                {
                    pen.CustomEndCap = new AdjustableArrowCap(6, 6, true);
                    g.DrawLine(pen, first, last);
                }
                else if (s.Tool == "Flèche libre" && s.Points.Count > 1)
                {
                    using (AdjustableArrowCap arrow = new AdjustableArrowCap(5, 6, true))
                    {
                        pen.CustomEndCap = arrow;
                        if (s.Smoothed && s.Points.Count > 3)
                        {
                            using (GraphicsPath path = new GraphicsPath())
                            {
                                path.AddCurve(s.Points.ToArray(), .28f);
                                g.DrawPath(pen, path);
                            }
                        }
                        else g.DrawLines(pen, s.Points.ToArray());
                    }
                }
                else if (s.Smoothed && s.Points.Count > 3 && (s.Tool == "Forme libre" || s.Tool == "Stylo"))
                    g.DrawCurve(pen, s.Points.ToArray(), .28f);
                else if (s.Points.Count > 1) g.DrawLines(pen, s.Points.ToArray());
                else g.DrawEllipse(pen, first.X - 1, first.Y - 1, 2, 2);
            }
        }

        private void DrawKeys(Graphics g, Point p, string text, DateTime now, DateTime until, string style)
        {
            if (String.IsNullOrEmpty(text)) return;
            using (Font font = new Font("Segoe UI", 11, FontStyle.Bold))
            {
                SizeF size = g.MeasureString(text, font);
                float x = p.X + 40;
                float y = p.Y - 78;
                RectangleF area = new RectangleF(x, y, size.Width + 30, size.Height + 20);
                bool minimal = style == "Minimal";
                Color fill = style == "Dégradé" ? Color.FromArgb(235, 17, 94, 123) :
                    style == "Verre" ? Color.FromArgb(165, 79, 110, 136) :
                    style == "Touches" ? Color.FromArgb(235, 45, 53, 68) : Color.FromArgb(220, 10, 20, 36);
                Color stroke = style == "Néon" ? Color.FromArgb(255, 255, 89, 181) :
                    style == "Touches" ? Color.FromArgb(200, 205, 216, 226) : Color.FromArgb(220, 37, 204, 229);
                using (GraphicsPath path = Visuals.RoundRect(Rectangle.Round(area), style == "Touches" ? 5 : 12))
                using (Brush background = new SolidBrush(minimal ? Color.FromArgb(130, 9, 17, 32) : fill))
                using (Pen edge = new Pen(stroke, style == "Néon" ? 2.8f : 1.4f))
                {
                    g.FillPath(background, path);
                    if (!minimal) g.DrawPath(edge, path);
                    g.DrawString(text, font, Brushes.White, x + 15, y + 10);
                }
            }
        }

        // Focus texte : aucune capture du Bureau (loupe manuelle toujours disponible).

        internal static Rectangle LensRect(Point p, Config c, Rectangle desktop)
        {
            int d = Math.Max(90, c.LoupeSize);
            int x = p.X + 75;
            int y = p.Y + 50;
            if (x + d > desktop.Right) x = p.X - d - 65;
            if (y + d > desktop.Bottom) y = p.Y - d - 65;
            x = Math.Max(desktop.Left, Math.Min(desktop.Right - d, x));   // reste dans l'écran
            y = Math.Max(desktop.Top, Math.Min(desktop.Bottom - d, y));
            return new Rectangle(x, y, d, d);
        }
        private void DrawLens(Graphics g, DateTime now, Point p, Config c, Rectangle desktop)
        {
            if (lensSnapshot == null || (now - snapshotAt).TotalMilliseconds > 190)
            {
                Bitmap next = CaptureUnderPointer(p, c.LoupeSize, c.LoupeMagnification);
                if (next != null)
                {
                    if (lensSnapshot != null) lensSnapshot.Dispose();
                    lensSnapshot = next;
                    snapshotAt = now;
                }
            }
            if (lensSnapshot == null) return;
            Rectangle lensRect = LensRect(p, c, desktop);
            using (GraphicsPath circle = new GraphicsPath())
            {
                circle.AddEllipse(lensRect);
                GraphicsState state = g.Save();
                g.SetClip(circle);
                g.DrawImage(lensSnapshot, lensRect);
                g.Restore(state);
                using (Pen frame = new Pen(Color.FromArgb(240, 37, 204, 229), 4))
                    g.DrawEllipse(frame, lensRect);
            }
        }

        private static Bitmap CaptureUnderPointer(Point p, int size, int zoom)
        {
            return CaptureUnderPointer(p, size, (double)zoom);
        }
        private static Bitmap CaptureUnderPointer(Point p, int size, double zoom)
        {
            int d = Math.Max(24, size);
            int sourceSize = Math.Max(12, (int)(d / Math.Max(1.01, zoom)));
            Bitmap image = new Bitmap(sourceSize, sourceSize, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(image))
                {
                    g.Clear(Color.FromArgb(20, 27, 40));
                    g.CopyFromScreen(p.X - sourceSize / 2, p.Y - sourceSize / 2, 0, 0, new Size(sourceSize, sourceSize), CopyPixelOperation.SourceCopy);
                }
                return image;
            }
            catch { image.Dispose(); return null; }
        }

        public void ExportAnnotations(string path)
        {
            Rectangle screen = SystemInformation.VirtualScreen;
            using (Bitmap bitmap = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(-screen.X, -screen.Y);
                // Correctif : l'ancienne version enregistrait une image vide.
                foreach (AnnotationStroke stroke in controller.Strokes)
                    DrawStroke(g, stroke);
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        private static int Clamp(int a) { return Math.Max(0, Math.Min(255, a)); }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ReleaseSurface();
                if (lensSnapshot != null) lensSnapshot.Dispose();
                if (logoBitmap != null) logoBitmap.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class Visuals
    {
        public static Color ParseColor(string value, Color fallback)
        {
            try { return ColorTranslator.FromHtml(value); }
            catch { return fallback; }
        }
        public static Color Lerp(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
        public static Color Hsv(double h, double s, double v)
        {
            h = (h % 360 + 360) % 360;
            int hi = (int)(h / 60) % 6;
            double f = h / 60 - Math.Floor(h / 60);
            double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
            // Aucune allocation de tableau par particule / image.
            double red, green, blue;
            switch (hi)
            {
                case 0: red = v; green = t; blue = p; break;
                case 1: red = q; green = v; blue = p; break;
                case 2: red = p; green = v; blue = t; break;
                case 3: red = p; green = q; blue = v; break;
                case 4: red = t; green = p; blue = v; break;
                default: red = v; green = p; blue = q; break;
            }
            return Color.FromArgb((int)(red * 255), (int)(green * 255), (int)(blue * 255));
        }
        public static GraphicsPath RoundRect(Rectangle rect, int r)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Min(r * 2, Math.Min(rect.Width, rect.Height));
            if (d < 2) { p.AddRectangle(rect); return p; }
            p.AddArc(rect.X, rect.Y, d, d, 180, 90);
            p.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            p.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
