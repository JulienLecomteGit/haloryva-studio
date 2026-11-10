using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CursorFlow.Setup
{
    internal enum SetupPage { Welcome, Conflict, Installing, Done, Error }

    // Contrôle dessiné (boutons, cases) : tout est peint par la fenêtre pour animer survol et appui.
    internal sealed class Widget
    {
        public Rectangle R; public string Text = ""; public int Kind; public bool Enabled = true, Checked;
        public float Hover, Press; public Action Click; public bool Default;
    }

    // Fenêtre d'installation : panneau de marque animé à gauche, contenu par pages à droite.
    internal sealed class SetupForm : Form
    {
        // palette (identique au Studio)
        private static readonly Color BgA = Color.FromArgb(7, 10, 22), BgB = Color.FromArgb(14, 20, 40), PanelA = Color.FromArgb(11, 16, 36), PanelB = Color.FromArgb(9, 12, 26);
        private static readonly Color Line = Color.FromArgb(31, 42, 76), Strong = Color.FromArgb(48, 63, 108);
        private static readonly Color Txt = Color.FromArgb(238, 242, 255), Subtle = Color.FromArgb(150, 163, 198), Faint = Color.FromArgb(99, 112, 150);
        private static readonly Color Cyan = Brand.Cyan, Purple = Brand.Purple, Green = Color.FromArgb(52, 211, 153), Red = Color.FromArgb(248, 113, 113), Amber = Color.FromArgb(251, 191, 36);

        private const int W = 860, H = 560, LeftW = 292;
        private float scale;
        private float fit = 1f;   // réduction pour tenir dans l'espace utile de l'écran (I08)
        private readonly Timer timer = new Timer();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        internal double TimeOverride = -1;
        private double pageStart;
        private SetupPage page;
        private readonly List<Widget> widgets = new List<Widget>();
        private Widget pressed;
        private readonly string root;
        private readonly bool demo; private readonly string demoFail;
        private InstallOptions opts = new InstallOptions();
        private InstallJob job;
        private double shown;           // progression affichée (lissée)
        private double finishedAt = -1;
        private bool launchAfter = true;
        private readonly bool isUpdate;
        private string errTitle = "", errText = "", errDetail = "", errKind = "";
        private bool corners;
        private readonly Font fTitle, fBig, fBody, fBodyB, fSmall, fSmallB, fBtn, fMono, fChip;
        // particules du panneau gauche
        private readonly float[] px = new float[18], py = new float[18], pv = new float[18], pr = new float[18], pp = new float[18];

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);

        private readonly bool uninstall;
        public SetupForm(string root, bool demo, string demoFail, bool uninstall)
        {
            this.root = root; this.demo = demo; this.demoFail = demoFail; this.uninstall = uninstall;
            using (Graphics g = CreateGraphics()) scale = Math.Max(1f, Math.Min(3f, g.DpiX / 96f));
            // Fenêtre adaptée à l'espace utile : même direction visuelle, boutons toujours accessibles.
            try
            {
                Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
                fit = (float)Math.Min(1.0, Math.Min((wa.Width - 32.0) / (W * scale), (wa.Height - 32.0) / (H * scale)));
                fit = Math.Max(.05f, fit); // fit is derived from the available screen, not an arbitrary .62 floor
                scale *= fit;
            }
            catch { }
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.CenterScreen; MaximizeBox = false;
            ClientSize = new Size(S(W), S(H)); Text = Brand.Product + (uninstall ? " — Désinstallation" : " — Installation");
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = BgA; KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            fTitle = new Font("Segoe UI", 22f * fit, FontStyle.Bold); fBig = new Font("Segoe UI", 15f * fit, FontStyle.Bold);
            fBody = new Font("Segoe UI", 10f * fit); fBodyB = new Font("Segoe UI", 10f * fit, FontStyle.Bold);
            fSmall = new Font("Segoe UI", 8.75f * fit); fSmallB = new Font("Segoe UI", 8.75f * fit, FontStyle.Bold);
            fBtn = new Font("Segoe UI", 10f * fit, FontStyle.Bold); fMono = new Font("Consolas", 8.5f * fit); fChip = new Font("Segoe UI", 8f * fit, FontStyle.Bold);
            Random rnd = new Random(11);
            for (int i = 0; i < px.Length; i++) { px[i] = (float)rnd.NextDouble(); py[i] = (float)rnd.NextDouble(); pv[i] = .015f + (float)rnd.NextDouble() * .03f; pr[i] = 1.4f + (float)rnd.NextDouble() * 2.2f; pp[i] = (float)rnd.NextDouble() * 6.28f; }
            opts.AutoStart = InstallJob.AutoStartWasOn(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haloryva"));
            if (MigrationPreview.LegacyAutoStartActive()) opts.AutoStart = false;
            isUpdate = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Haloryva\Haloryva.exe"));
            timer.Interval = 16; timer.Tick += delegate { Tick(); };
            Go(SetupPage.Welcome);
            timer.Start();
        }

        private int S(int v) { return (int)Math.Round(v * scale); }
        private double T { get { return TimeOverride >= 0 ? TimeOverride : clock.Elapsed.TotalSeconds; } }
        private static float Clamp01(double v) { return (float)Math.Max(0, Math.Min(1, v)); }
        private static float EaseOut(float t) { float u = 1f - t; return 1f - u * u * u; }

        // ------------------------------------------------------------------ fenêtre
        protected override CreateParams CreateParams { get { CreateParams cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int pref = 2; // DWMWCP_ROUND (Windows 11)
                corners = DwmSetWindowAttribute(Handle, 33, ref pref, 4) == 0;
            }
            catch { corners = false; }
            if (!corners) { try { Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, ClientSize.Width + 1, ClientSize.Height + 1, S(22), S(22))); } catch { } }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84) // WM_NCHITTEST : tout est déplaçable sauf les contrôles
            {
                int lp = m.LParam.ToInt32();
                Point p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                m.Result = new IntPtr(HitWidget(p) != null ? 1 : 2);
                return;
            }
            base.WndProc(ref m);
        }
        // Alt+F4 / fermeture système pendant le travail : refusé (la copie critique ne doit jamais être coupée).
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (page == SetupPage.Installing && job != null && !job.Finished && e.CloseReason != CloseReason.WindowsShutDown)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { if (page != SetupPage.Installing) Close(); return true; }
            if (keyData == Keys.Enter || keyData == Keys.Space)
            {
                foreach (Widget w in widgets) if (w.Default && w.Enabled && w.Click != null) { w.Click(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private Widget HitWidget(Point p)
        {
            for (int i = widgets.Count - 1; i >= 0; i--) if (widgets[i].Enabled && widgets[i].R.Contains(p)) return widgets[i];
            return null;
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = e.Button == MouseButtons.Left ? HitWidget(e.Location) : null; }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            Widget w = HitWidget(e.Location);
            if (pressed != null && w == pressed && pressed.Click != null) pressed.Click();
            pressed = null;
        }

        // ------------------------------------------------------------------ pages
        private void Go(SetupPage p)
        {
            page = p; pageStart = T; widgets.Clear();
            int right = S(W) - S(40), bottom = S(H) - S(34), bh = S(42);
            int x0 = S(LeftW) + S(44);
            AddWinButtons();
            if (p == SetupPage.Welcome && uninstall)
            {
                AddCheck(x0, S(330), "Supprimer aussi mes réglages et sauvegardes", opts.RemoveSettings, delegate(bool v) { opts.RemoveSettings = v; });
                AddButton(right - S(210), bottom - bh, S(210), bh, "Désinstaller", 0, OnInstallClicked, true);
                AddButton(right - S(210) - S(120), bottom - bh, S(110), bh, "Annuler", 2, delegate { Close(); }, false);
            }
            else if (p == SetupPage.Welcome)
            {
                if (!isUpdate && MigrationPreview.LegacyPreferencesExist())
                    AddCheck(x0, S(352), "Importer les anciens réglages (copie)", opts.ImportCursorFlowSettings, delegate(bool v) { opts.ImportCursorFlowSettings = v; });
                AddCheck(x0, S(388), "Créer un raccourci sur le Bureau", opts.DesktopShortcut, delegate(bool v) { opts.DesktopShortcut = v; });
                AddCheck(x0, S(422), "Démarrer Haloryva avec Windows", opts.AutoStart, delegate(bool v) { opts.AutoStart = v; });
                AddButton(right - S(210), bottom - bh, S(210), bh, isUpdate ? "Mettre à jour  →" : "Installer  →", 0, OnInstallClicked, true);
                AddButton(right - S(210) - S(120), bottom - bh, S(110), bh, "Annuler", 2, delegate { Close(); }, false);
            }
            else if (p == SetupPage.Conflict)
            {
                AddButton(right - S(300), bottom - bh, S(300), bh, "Fermer Haloryva et continuer", 0, OnForceClose, true);
                AddButton(right - S(300) - S(120), bottom - bh, S(110), bh, "Annuler", 2, delegate { Close(); }, false);
            }
            else if (p == SetupPage.Installing)
            {
                AddButton(right - S(190), bottom - bh, S(190), bh, uninstall ? "Désinstallation…" : "Installation…", 1, null, false).Enabled = false;
            }
            else if (p == SetupPage.Done)
            {
                if (!uninstall) AddCheck(x0, S(386), "Lancer Haloryva maintenant", launchAfter, delegate(bool v) { launchAfter = v; });
                AddButton(right - S(170), bottom - bh, S(170), bh, "Terminer", 0, OnFinish, true);
            }
            else
            {
                AddButton(right - S(130), bottom - bh, S(130), bh, "Fermer", 0, delegate { Close(); }, true);
                if (errDetail.Length > 0) AddButton(right - S(130) - S(190), bottom - bh, S(180), bh, "Copier le détail", 1, delegate
                {
                    try { Clipboard.SetText(Brand.VersionLine + "\r\n" + errTitle + "\r\n" + errText + "\r\n\r\n" + errDetail); } catch { }
                }, false);
            }
            Invalidate();
        }

        private void AddWinButtons()
        {
            int bw = S(40), bh = S(30), y = S(10);
            Widget c = new Widget(); c.Kind = 5; c.R = new Rectangle(S(W) - bw - S(10), y, bw, bh); c.Click = delegate { if (page != SetupPage.Installing) Close(); };
            Widget m = new Widget(); m.Kind = 4; m.R = new Rectangle(S(W) - bw * 2 - S(14), y, bw, bh); m.Click = delegate { WindowState = FormWindowState.Minimized; };
            widgets.Add(m); widgets.Add(c);
        }
        private Widget AddButton(int x, int y, int w, int h, string text, int kind, Action click, bool def)
        { Widget b = new Widget(); b.R = new Rectangle(x, y, w, h); b.Text = text; b.Kind = kind; b.Click = click; b.Default = def; widgets.Add(b); return b; }
        private void AddCheck(int x, int y, string text, bool value, Action<bool> set)
        {
            Widget c = new Widget(); c.Kind = 3; c.Text = text; c.Checked = value; c.R = new Rectangle(x - S(4), y - S(6), S(380), S(30));
            c.Click = delegate { c.Checked = !c.Checked; set(c.Checked); };
            widgets.Add(c);
        }

        private void OnInstallClicked()
        {
            bool running = false;
            if (!demo && !uninstall) { try { running = InstallJob.IsInstalledAppRunning(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Haloryva\Haloryva.exe")); } catch { } }
            if (running) Go(SetupPage.Conflict); else StartInstall();
        }
        private void OnForceClose()
        {
            // The worker requests a graceful exit and reports refusal without forcing a kill.
            StartInstall();
        }
        private void StartInstall()
        {
            job = new InstallJob(root, opts, demo, demoFail, uninstall); shown = 0; finishedAt = -1;
            job.Start(); Go(SetupPage.Installing);
        }
        internal bool LaunchRequested { get; private set; }
        private void OnFinish()
        {
            LaunchRequested = !uninstall && launchAfter && !demo;
            Close(); // SetupProgram launches only AFTER releasing both maintenance locks.
        }

        // ------------------------------------------------------------------ boucle d'animation
        private void Tick()
        {
            Point cp = PointToClient(Cursor.Position);
            bool active = Form.ActiveForm == this;
            foreach (Widget w in widgets)
            {
                bool over = active && w.Enabled && w.R.Contains(cp);
                w.Hover += ((over ? 1f : 0f) - w.Hover) * .28f;
                w.Press += (((over && pressed == w) ? 1f : 0f) - w.Press) * .4f;
            }
            Cursor = HitWidget(cp) != null ? Cursors.Hand : Cursors.Default;
            if (page == SetupPage.Installing && job != null)
            {
                double target = job.Progress();
                shown += (target - shown) * .10; if (job.Finished && !job.Failed) shown = Math.Max(shown, Math.Min(1.0, shown + .02));
                if (job.Finished)
                {
                    if (finishedAt < 0) finishedAt = T;
                    if (job.Failed && T - finishedAt > .25)
                    { errKind = job.FailKind; errTitle = job.FailTitle; errText = job.FailText; errDetail = job.FailDetail; Go(SetupPage.Error); }
                    else if (!job.Failed && shown > .985 && T - finishedAt > .55) Go(SetupPage.Done);
                }
            }
            Invalidate();
        }

        // ------------------------------------------------------------------ rendu
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            double t = T;
            Rectangle all = new Rectangle(0, 0, S(W), S(H));
            using (LinearGradientBrush bg = new LinearGradientBrush(all, BgB, BgA, 60f)) g.FillRectangle(bg, all);
            DrawLeft(g, t);

            // contenu avec entrée douce : glissement + fondu
            float k = EaseOut(Clamp01((t - pageStart) / .42));
            GraphicsState st = g.Save();
            g.TranslateTransform((1f - k) * S(26), 0);
            switch (page)
            {
                case SetupPage.Welcome: DrawWelcome(g, t); break;
                case SetupPage.Conflict: DrawConflict(g, t); break;
                case SetupPage.Installing: DrawInstalling(g, t); break;
                case SetupPage.Done: DrawDone(g, t); break;
                default: DrawError(g, t); break;
            }
            g.Restore(st);
            if (k < 1f)
            {
                using (SolidBrush veil = new SolidBrush(Color.FromArgb((int)((1f - k) * 235), 9, 13, 28)))
                    g.FillRectangle(veil, new Rectangle(S(LeftW), S(48), S(W) - S(LeftW), S(H) - S(48) - S(96)));
            }
            foreach (Widget w in widgets) DrawWidget(g, w);
            using (Pen border = new Pen(Color.FromArgb(corners ? 120 : 180, 48, 63, 108)))
            using (GraphicsPath p = Brand.RoundRect(new RectangleF(0, 0, S(W) - 1, S(H) - 1), corners ? S(8) : S(11))) g.DrawPath(border, p);
        }

        private void DrawLeft(Graphics g, double t)
        {
            Rectangle r = new Rectangle(0, 0, S(LeftW), S(H));
            using (LinearGradientBrush b = new LinearGradientBrush(r, PanelA, PanelB, 90f)) g.FillRectangle(b, r);
            using (Pen sep = new Pen(Line)) g.DrawLine(sep, S(LeftW), 0, S(LeftW), S(H));
            // particules : dérive lente vers le haut, très discrètes
            for (int i = 0; i < px.Length; i++)
            {
                float y = 1f - (float)((py[i] + t * pv[i]) % 1.0);
                float x = px[i] + (float)Math.Sin(t * .6 + pp[i]) * .03f;
                int a = (int)(18 + 46 * (.5 + .5 * Math.Sin(t * 1.3 + pp[i])));
                float rr = pr[i] * scale;
                using (SolidBrush b = new SolidBrush(Color.FromArgb(a, i % 2 == 0 ? Cyan : Purple)))
                    g.FillEllipse(b, x * S(LeftW) - rr, y * S(H) - rr, rr * 2, rr * 2);
            }
            // halo discret derrière le logo
            int d = S(118); float cx = S(LeftW) / 2f, cy = S(168) + (float)Math.Sin(t * 1.4) * S(4);
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(cx - d * .95f, cy - d * .95f, d * 1.9f, d * 1.9f);
                using (PathGradientBrush pg = new PathGradientBrush(gp))
                {
                    pg.CenterColor = Color.FromArgb(34, Purple); pg.SurroundColors = new Color[] { Color.FromArgb(0, Purple) };
                    g.FillPath(pg, gp);
                }
            }
            Brand.DrawLogo(g, new RectangleF(cx - d / 2f, cy - d / 2f, d, d), (float)(t * 70));
            StringFormat c = new StringFormat(); c.Alignment = StringAlignment.Center; c.LineAlignment = StringAlignment.Center;
            using (SolidBrush w = new SolidBrush(Txt)) g.DrawString("Haloryva", fBig, w, new RectangleF(0, S(250), S(LeftW), S(30)), c);
            using (SolidBrush cy2 = new SolidBrush(Cyan)) g.DrawString("Studio", fBig, cy2, new RectangleF(0, S(278), S(LeftW), S(30)), c);
            Rectangle chip = new Rectangle((S(LeftW) - S(70)) / 2, S(318), S(70), S(22));
            using (GraphicsPath p = Brand.RoundRect(chip, chip.Height / 2f))
            using (SolidBrush fb = new SolidBrush(Color.FromArgb(40, Purple))) using (Pen eb = new Pen(Color.FromArgb(120, Purple))) { g.FillPath(fb, p); g.DrawPath(eb, p); }
            using (SolidBrush vb = new SolidBrush(Color.FromArgb(205, 190, 255))) g.DrawString("v" + Brand.Version, fChip, vb, chip, c);
            using (SolidBrush sb = new SolidBrush(Subtle)) g.DrawString(Brand.Tagline, fSmall, sb, new RectangleF(S(24), S(358), S(LeftW) - S(48), S(44)), c);
            using (SolidBrush fb = new SolidBrush(Faint))
            {
                g.DrawString("par " + Brand.Author, fSmallB, fb, new RectangleF(0, S(H) - S(70), S(LeftW), S(20)), c);
                g.DrawString(Brand.Copyright, fSmall, fb, new RectangleF(0, S(H) - S(50), S(LeftW), S(20)), c);
            }
        }

        private void DrawText(Graphics g, string s, Font f, Color col, int x, int y, int w, int h, bool wrap, StringAlignment al)
        {
            StringFormat sf = new StringFormat(); sf.Alignment = al; sf.LineAlignment = StringAlignment.Near;
            if (!wrap) { sf.FormatFlags = StringFormatFlags.NoWrap; sf.Trimming = StringTrimming.EllipsisCharacter; }
            using (SolidBrush b = new SolidBrush(col)) g.DrawString(s, f, b, new RectangleF(x, y, w, h), sf);
        }

        private int X0 { get { return S(LeftW) + S(44); } }
        private int CW { get { return S(W) - S(LeftW) - S(88); } }

        private void Bubble(Graphics g, int x, int y, int d, int glyph, Color a, Color b)
        {
            Rectangle r = new Rectangle(x, y, d, d);
            using (GraphicsPath p = Brand.RoundRect(r, d * .3f))
            {
                using (SolidBrush f = new SolidBrush(Color.FromArgb(34, a))) g.FillPath(f, p);
                using (Pen e = new Pen(Color.FromArgb(110, a), 1.2f)) g.DrawPath(e, p);
            }
            using (Pen pen = new Pen(Color.FromArgb(235, 255, 255, 255), Math.Max(1.6f, d * .06f)))
            {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                float u = d / 40f, cx = x + d / 2f, cy = y + d / 2f;
                if (glyph == 0) { g.DrawLine(pen, cx, cy - 9 * u, cx, cy + 5 * u); g.DrawLines(pen, new PointF[] { new PointF(cx - 6 * u, cy - 1 * u), new PointF(cx, cy + 5 * u), new PointF(cx + 6 * u, cy - 1 * u) }); g.DrawLine(pen, cx - 9 * u, cy + 10 * u, cx + 9 * u, cy + 10 * u); }
                else if (glyph == 1) { for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++) using (GraphicsPath q = Brand.RoundRect(new RectangleF(cx - 9 * u + i * 10 * u, cy - 9 * u + j * 10 * u, 8 * u, 8 * u), 2 * u)) g.DrawPath(pen, q); }
                else { g.DrawArc(pen, cx - 9 * u, cy - 9 * u, 18 * u, 18 * u, -60, 280); g.DrawLines(pen, new PointF[] { new PointF(cx + 3 * u, cy - 10 * u), new PointF(cx + 9 * u, cy - 7 * u), new PointF(cx + 5 * u, cy - 1 * u) }); }
            }
        }

        private void DrawWelcome(Graphics g, double t)
        {
            int x = X0, w = CW;
            if (uninstall) { DrawUninstallWelcome(g); return; }
            DrawText(g, isUpdate ? "Mise à jour" : "Bienvenue", fTitle, Txt, x, S(70), w, S(44), false, StringAlignment.Near);
            DrawText(g, isUpdate
                ? "Une version de Haloryva est déjà installée. Elle sera sauvegardée, puis remplacée par la " + Brand.Version + ". Tes réglages sont conservés."
                : "Cette installation prépare Haloryva Studio " + Brand.Version + " pour ton compte Windows. Aucun droit administrateur n'est nécessaire.",
                fBody, Subtle, x, S(122), w, S(52), true, StringAlignment.Near);
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haloryva");
            string[][] rows = {
                new string[] { "Application", dir },
                new string[] { "Raccourcis", "Menu Démarrer, et Bureau si tu le souhaites" },
                new string[] { "Sécurité", "Réglages Haloryva et anciennes données préservés" } };
            Color[] cols = { Cyan, Purple, Green };
            for (int i = 0; i < 3; i++)
            {
                int y = S(178) + i * S(56);
                Bubble(g, x, y, S(42), i, cols[i], cols[i]);
                DrawText(g, rows[i][0], fBodyB, Txt, x + S(58), y, w - S(58), S(22), false, StringAlignment.Near);
                DrawText(g, rows[i][1], fSmall, Subtle, x + S(58), y + S(21), w - S(58), S(20), false, StringAlignment.Near);
            }
            using (Pen l = new Pen(Line)) g.DrawLine(l, x, S(339), x + w, S(339));
        }

        private void DrawUninstallWelcome(Graphics g)
        {
            int x = X0, w = CW;
            DrawText(g, "Désinstaller", fTitle, Txt, x, S(70), w, S(44), false, StringAlignment.Near);
            DrawText(g, Brand.Product + " " + Brand.Version + " va être retiré de ce PC. Si Haloryva est ouvert, il sera fermé automatiquement.", fBody, Subtle, x, S(122), w, S(52), true, StringAlignment.Near);
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haloryva");
            string[][] rows = {
                new string[] { "Sera supprimé", "L'application, ses raccourcis et le démarrage avec Windows" },
                new string[] { "Conservé par défaut", "Tes réglages et sauvegardes : " + dir } };
            Color[] cols = { Red, Green };
            for (int i = 0; i < 2; i++)
            {
                int y = S(196) + i * S(62);
                Bubble(g, x, y, S(42), i == 0 ? 1 : 2, cols[i], cols[i]);
                DrawText(g, rows[i][0], fBodyB, Txt, x + S(58), y, w - S(58), S(22), false, StringAlignment.Near);
                DrawText(g, rows[i][1], fSmall, Subtle, x + S(58), y + S(21), w - S(58), S(20), false, StringAlignment.Near);
            }
            using (Pen l = new Pen(Line)) g.DrawLine(l, x, S(310), x + w, S(310));
        }

        private void DrawConflict(Graphics g, double t)
        {
            int x = X0, w = CW;
            Bubble(g, x, S(74), S(56), 2, Amber, Amber);
            DrawText(g, "Haloryva est ouvert", fTitle, Txt, x, S(150), w, S(44), false, StringAlignment.Near);
            DrawText(g, "Pour installer la nouvelle version, l'application en cours doit être fermée. Elle sera arrêtée proprement, tes réglages et tes dessins de configuration sont conservés.", fBody, Subtle, x, S(204), w, S(90), true, StringAlignment.Near);
        }

        private void DrawInstalling(Graphics g, double t)
        {
            int x = X0, w = CW;
            int cur = job != null ? Math.Max(0, job.Current) : 0;
            DrawText(g, uninstall ? "Désinstallation en cours" : "Installation en cours", fTitle, Txt, x, S(70), w, S(44), false, StringAlignment.Near);
            string label = job != null && job.Finished && !job.Failed ? "Finalisation…" : job.StepNames[Math.Min(cur, job.StepNames.Length - 1)] + "…";
            DrawText(g, label, fBody, Subtle, x, S(122), w - S(80), S(24), false, StringAlignment.Near);

            // barre de progression
            int by = S(168), bh = S(12);
            Rectangle track = new Rectangle(x, by, w, bh);
            using (GraphicsPath p = Brand.RoundRect(track, bh / 2f)) using (SolidBrush tb = new SolidBrush(Color.FromArgb(70, 31, 42, 76))) g.FillPath(tb, p);
            int fw = Math.Max(bh, (int)(w * Math.Max(0, Math.Min(1, shown))));
            Rectangle fill = new Rectangle(x, by, fw, bh);
            using (GraphicsPath p = Brand.RoundRect(fill, bh / 2f))
            {
                using (LinearGradientBrush fb = new LinearGradientBrush(new Rectangle(x, by, Math.Max(2, fw), bh), Cyan, Purple, 0f)) g.FillPath(fb, p);
                // reflet qui glisse sur la partie remplie
                GraphicsState st = g.Save(); g.SetClip(p, CombineMode.Intersect);
                float sx = x + (float)((t * .55) % 1.4 - .2) * fw, sw = S(90);
                using (LinearGradientBrush sh = new LinearGradientBrush(new RectangleF(sx, by, sw, bh), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0f))
                {
                    ColorBlend cb = new ColorBlend(3); cb.Colors = new Color[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(120, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) }; cb.Positions = new float[] { 0, .5f, 1 };
                    sh.InterpolationColors = cb; g.FillRectangle(sh, sx, by, sw, bh);
                }
                g.Restore(st);
            }
            DrawText(g, ((int)Math.Round(shown * 100)) + " %", fSmallB, Txt, x + w - S(70), S(126), S(70), S(20), false, StringAlignment.Far);

            // étapes
            for (int i = 0; i < job.StepNames.Length; i++)
            {
                int y = S(214) + i * S(50);
                bool done = job != null && job.StepEnd[i] >= 0, active = job != null && job.StepStart[i] >= 0 && !done;
                
                int cx = x + S(14), cy = y + S(16), rr = S(11);
                if (done)
                {
                    float pop = EaseOut(Clamp01((job.NowMs - job.StepEnd[i]) / 320.0)); float q = rr * (.55f + .45f * pop);
                    using (SolidBrush gb = new SolidBrush(Color.FromArgb(40, Green))) g.FillEllipse(gb, cx - rr, cy - rr, rr * 2, rr * 2);
                    using (Pen gp = new Pen(Green, 1.6f)) g.DrawEllipse(gp, cx - q, cy - q, q * 2, q * 2);
                    using (Pen ck = new Pen(Green, 2.2f)) { ck.StartCap = LineCap.Round; ck.EndCap = LineCap.Round; ck.LineJoin = LineJoin.Round;
                        g.DrawLines(ck, new PointF[] { new PointF(cx - q * .42f, cy + q * .02f), new PointF(cx - q * .1f, cy + q * .36f), new PointF(cx + q * .46f, cy - q * .3f) }); }
                }
                else if (active)
                {
                    using (Pen ring = new Pen(Color.FromArgb(60, Cyan), 2f)) g.DrawEllipse(ring, cx - rr, cy - rr, rr * 2, rr * 2);
                    using (Pen arc = new Pen(Cyan, 2.4f)) { arc.StartCap = LineCap.Round; arc.EndCap = LineCap.Round; g.DrawArc(arc, cx - rr, cy - rr, rr * 2, rr * 2, (float)(t * 280), 100); }
                    using (Pen arc2 = new Pen(Purple, 2.4f)) { arc2.StartCap = LineCap.Round; arc2.EndCap = LineCap.Round; g.DrawArc(arc2, cx - rr, cy - rr, rr * 2, rr * 2, (float)(t * 280) + 180, 70); }
                }
                else using (Pen pend = new Pen(Color.FromArgb(120, Strong), 1.6f)) g.DrawEllipse(pend, cx - rr + 2, cy - rr + 2, rr * 2 - 4, rr * 2 - 4);
                Color tc = done ? Txt : active ? Txt : Faint;
                DrawText(g, job.StepNames[i], active ? fBodyB : fBody, tc, x + S(40), y + S(5), w - S(130), S(24), false, StringAlignment.Near);
                if (done) DrawText(g, ((job.StepEnd[i] - job.StepStart[i]) / 1000.0).ToString("0.0") + " s", fSmall, Faint, x + w - S(80), y + S(8), S(80), S(20), false, StringAlignment.Far);
                if (i < job.StepNames.Length - 1) using (Pen l = new Pen(Color.FromArgb(done ? 140 : 60, done ? Green : Strong))) g.DrawLine(l, cx, cy + rr + 2, cx, cy + S(50) - rr - 2);
            }
        }

        private void DrawDone(Graphics g, double t)
        {
            int x = X0, w = CW; double a = t - pageStart;
            DrawBadge(g, x + S(46), S(122), S(46), (float)a, Green, false);
            DrawText(g, uninstall ? "Désinstallation terminée" : Brand.Short + " est prêt", fTitle, Txt, x + S(112), S(84), w - S(112), S(44), false, StringAlignment.Near);
            DrawText(g, uninstall ? "Merci d'avoir utilisé Haloryva Studio" : "Version " + Brand.Version + (isUpdate ? " · mise à jour effectuée" : " · installation terminée"), fBody, Subtle, x + S(112), S(128), w - S(112), S(24), false, StringAlignment.Near);
            if (uninstall)
            {
                string[][] urows = {
                    new string[] { "Application", "Supprimée" }, new string[] { "Raccourcis", "Supprimés" },
                    new string[] { "Applications installées", "Entrée retirée" },
                    new string[] { "Réglages et sauvegardes", opts.RemoveSettings ? "Supprimés" : "Conservés dans " + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haloryva") } };
                for (int i = 0; i < urows.Length; i++)
                {
                    int y = S(212) + i * S(40);
                    DrawText(g, urows[i][0], fSmallB, Faint, x, y, S(190), S(20), false, StringAlignment.Near);
                    DrawText(g, urows[i][1], fSmall, Subtle, x + S(192), y, w - S(192), S(20), false, StringAlignment.Near);
                    using (Pen l = new Pen(Color.FromArgb(70, Line))) g.DrawLine(l, x, y + S(28), x + w, y + S(28));
                }
                return;
            }
            string dir = job != null ? job.TargetDir : "";
            string[][] rows = {
                new string[] { "Emplacement", dir },
                new string[] { "Raccourcis", opts.DesktopShortcut ? "Menu Démarrer et Bureau" : "Menu Démarrer" },
                new string[] { "Démarrage avec Windows", opts.AutoStart ? "Activé" : "Désactivé (modifiable dans Préférences)" },
                new string[] { "Sauvegardes", dir.Length > 0 ? Path.Combine(dir, "Backups") : "" } };
            for (int i = 0; i < rows.Length; i++)
            {
                int y = S(212) + i * S(40);
                DrawText(g, rows[i][0], fSmallB, Faint, x, y, S(170), S(20), false, StringAlignment.Near);
                DrawText(g, rows[i][1], fSmall, Subtle, x + S(172), y, w - S(172), S(20), false, StringAlignment.Near);
                using (Pen l = new Pen(Color.FromArgb(70, Line))) g.DrawLine(l, x, y + S(28), x + w, y + S(28));
            }
        }

        private void DrawError(Graphics g, double t)
        {
            int x = X0, w = CW; double a = t - pageStart;
            Color col = errKind == "launch-blocked" ? Amber : Red;
            DrawBadge(g, x + S(34), S(104), S(34), (float)a, col, true);
            DrawText(g, errTitle, fBig, Txt, x + S(88), S(76), w - S(88), S(54), true, StringAlignment.Near);
            DrawText(g, errText, fBody, Subtle, x, S(158), w, S(130), true, StringAlignment.Near);
            if (errDetail.Length > 0)
            {
                int lines = Math.Min(9, errDetail.Replace("\r", "").Split('\n').Length + errDetail.Length / 70);
                Rectangle box = new Rectangle(x, S(296), w, Math.Min(S(170), S(34) + lines * S(15)));
                using (GraphicsPath p = Brand.RoundRect(box, S(10))) { using (SolidBrush b = new SolidBrush(Color.FromArgb(150, 5, 8, 18))) g.FillPath(b, p); using (Pen e = new Pen(Line)) g.DrawPath(e, p); }
                DrawText(g, errDetail, fMono, Color.FromArgb(200, 210, 235), box.X + S(12), box.Y + S(10), box.Width - S(24), box.Height - S(18), true, StringAlignment.Near);
            }
        }

        // Pastille animée : anneau qui se referme, symbole tracé progressivement, étincelles discrètes.
        private void DrawBadge(Graphics g, float cx, float cy, float r, float t, Color col, bool error)
        {
            float ring = EaseOut(Clamp01(t / .6f));
            using (SolidBrush fb = new SolidBrush(Color.FromArgb((int)(34 * Clamp01((t - .35f) / .4f)), col))) g.FillEllipse(fb, cx - r, cy - r, r * 2, r * 2);
            using (Pen bg = new Pen(Color.FromArgb(40, col), 3f)) g.DrawEllipse(bg, cx - r, cy - r, r * 2, r * 2);
            using (Pen p = new Pen(col, 3.4f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; if (ring > 0) g.DrawArc(p, cx - r, cy - r, r * 2, r * 2, -90, 360f * ring); }
            float m = EaseOut(Clamp01((t - .45f) / .4f));
            using (Pen p = new Pen(Color.FromArgb(255, col), Math.Max(3f, r * .09f)))
            {
                p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                if (!error)
                {
                    PointF a = new PointF(cx - r * .36f, cy + r * .02f), b = new PointF(cx - r * .08f, cy + r * .3f), c = new PointF(cx + r * .4f, cy - r * .28f);
                    if (m <= .45f) { float u = m / .45f; g.DrawLine(p, a, new PointF(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u)); }
                    else { float u = (m - .45f) / .55f; g.DrawLine(p, a, b); g.DrawLine(p, b, new PointF(b.X + (c.X - b.X) * u, b.Y + (c.Y - b.Y) * u)); }
                }
                else
                {
                    float d = r * .3f;
                    g.DrawLine(p, cx - d, cy - d, cx - d + 2 * d * m, cy - d + 2 * d * m);
                    g.DrawLine(p, cx + d, cy - d, cx + d - 2 * d * m, cy - d + 2 * d * m);
                }
            }
            float u2 = Clamp01((t - .5f) / 1.1f);
            if (!error && u2 > 0 && u2 < 1)
                for (int i = 0; i < 14; i++)
                {
                    double ang = i * Math.PI * 2 / 14 + .3; float dist = r * (1.15f + .9f * EaseOut(u2)); float sz = Math.Max(1.5f, r * .06f) * (1f - u2 * .5f);
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb((int)(170 * (1f - u2)), i % 2 == 0 ? Cyan : Purple)))
                        g.FillEllipse(sb, cx + (float)Math.Cos(ang) * dist - sz, cy + (float)Math.Sin(ang) * dist - sz, sz * 2, sz * 2);
                }
        }

        private void DrawWidget(Graphics g, Widget w)
        {
            Rectangle r = w.R; float h = w.Hover;
            if (w.Kind == 4 || w.Kind == 5)
            {
                if (h > .02f) using (GraphicsPath p = Brand.RoundRect(r, S(8))) using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(h * (w.Kind == 5 ? 150 : 60)), w.Kind == 5 ? Red : Color.FromArgb(120, 140, 200)))) g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(230, w.Kind == 5 && h > .5f ? Color.White : Subtle), 1.6f))
                {
                    pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
                    float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, d = S(5);
                    if (w.Kind == 5) { g.DrawLine(pen, cx - d, cy - d, cx + d, cy + d); g.DrawLine(pen, cx + d, cy - d, cx - d, cy + d); }
                    else g.DrawLine(pen, cx - d, cy + 1, cx + d, cy + 1);
                }
                return;
            }
            if (w.Kind == 3)
            {
                int bs = S(20); Rectangle box = new Rectangle(r.X + S(4), r.Y + (r.Height - bs) / 2, bs, bs);
                using (GraphicsPath p = Brand.RoundRect(box, S(6)))
                {
                    if (w.Checked) using (LinearGradientBrush b = new LinearGradientBrush(box, Cyan, Purple, 45f)) g.FillPath(b, p);
                    else { using (SolidBrush b = new SolidBrush(Color.FromArgb(21, 30, 58))) g.FillPath(b, p); using (Pen e = new Pen(Color.FromArgb(120 + (int)(h * 100), Strong), 1.4f)) g.DrawPath(e, p); }
                }
                if (w.Checked) using (Pen ck = new Pen(Color.White, 2.2f)) { ck.StartCap = LineCap.Round; ck.EndCap = LineCap.Round; ck.LineJoin = LineJoin.Round;
                    g.DrawLines(ck, new PointF[] { new PointF(box.X + bs * .26f, box.Y + bs * .52f), new PointF(box.X + bs * .44f, box.Y + bs * .7f), new PointF(box.X + bs * .76f, box.Y + bs * .3f) }); }
                DrawText(g, w.Text, fBody, h > .3f ? Txt : Color.FromArgb(215, 222, 245), box.Right + S(12), r.Y + (r.Height - S(20)) / 2, r.Width - S(44), S(22), false, StringAlignment.Near);
                return;
            }
            int off = (int)Math.Round(w.Press * S(1));
            Rectangle rr = new Rectangle(r.X, r.Y + off, r.Width, r.Height);
            using (GraphicsPath p = Brand.RoundRect(rr, S(11)))
            {
                if (w.Kind == 0)
                {
                    Color a = Lerp(Cyan, Color.FromArgb(120, 235, 250), h * .35f), b = Lerp(Purple, Color.FromArgb(190, 165, 255), h * .35f);
                    int alpha = w.Enabled ? 255 : 90;
                    using (LinearGradientBrush br = new LinearGradientBrush(rr, Color.FromArgb(alpha, a), Color.FromArgb(alpha, b), 0f)) g.FillPath(br, p);
                }
                else if (w.Kind == 1)
                {
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(w.Enabled ? 255 : 120, Lerp(Color.FromArgb(21, 30, 58), Color.FromArgb(34, 46, 88), h)))) g.FillPath(br, p);
                    using (Pen e = new Pen(Color.FromArgb(w.Enabled ? 255 : 110, Lerp(Strong, Color.FromArgb(90, 110, 170), h)))) g.DrawPath(e, p);
                }
                else if (h > .02f) using (SolidBrush br = new SolidBrush(Color.FromArgb((int)(h * 50), 140, 160, 220))) g.FillPath(br, p);
            }
            Color tc = w.Kind == 0 ? Color.White : w.Enabled ? (w.Kind == 2 ? Lerp(Subtle, Txt, h) : Txt) : Faint;
            StringFormat c = new StringFormat(); c.Alignment = StringAlignment.Center; c.LineAlignment = StringAlignment.Center;
            using (SolidBrush tb = new SolidBrush(tc)) g.DrawString(w.Text, fBtn, tb, rr, c);
        }

        private static Color Lerp(Color a, Color b, float t)
        { t = Math.Max(0, Math.Min(1, t)); return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }

        // ------------------------------------------------------------------ outils de test
        internal void DebugShow(SetupPage p, double seconds, double progress, string kind)
        {
            TimeOverride = seconds; timer.Stop();
            if (p == SetupPage.Installing)
            {
                job = new InstallJob(root, opts, true, "", uninstall); shown = progress;
                int upto = (int)Math.Floor(progress * job.StepNames.Length);
                for (int i = 0; i < job.StepNames.Length; i++)
                {
                    if (i < upto) { job.StepStart[i] = 0; job.StepEnd[i] = 1800 + i * 300; }
                    else if (i == upto) { job.StepStart[i] = 0; job.Current = i; }
                }
            }
            if (p == SetupPage.Done) { job = new InstallJob(root, opts, true, "", uninstall); }
            if (p == SetupPage.Error)
            {
                errKind = kind;
                errTitle = kind == "blocked" ? "Windows a bloqué Haloryva" : "Compilation impossible";
                errText = kind == "blocked" ? "Smart App Control ou une stratégie de l'organisation refuse l'exécutable créé sur ce PC. Ne désactive pas la protection : lance outils\\DiagnosticBlocage.cmd (lecture seule, s'il est présent) ou demande une autorisation à ton administrateur."
                                            : "Le compilateur a signalé une erreur : l'installation est annulée, ta version actuelle n'a pas été touchée.";
                errDetail = kind == "blocked" ? "Une stratégie de contrôle d'application a bloqué ce fichier." : "Program.cs(12,5): error CS1002: ; expected\r\nController.cs(88,17): error CS0103: nom introuvable";
            }
            Go(p); pageStart = seconds - 5; Invalidate();
        }

        internal int OperationExitCode { get { return (job != null && job.Failed) || page == SetupPage.Error ? 1 : 0; } }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); fTitle.Dispose(); fBig.Dispose(); fBody.Dispose(); fBodyB.Dispose(); fSmall.Dispose(); fSmallB.Dispose(); fBtn.Dispose(); fMono.Dispose(); fChip.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal static class SetupProgram
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        // Écrit les ressources « payload/… » de l'exe dans un dossier ; renvoie le nombre de fichiers.
        internal static int ExtractPayload(string dir)
        {
            System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
            int count = 0;
            foreach (string name in asm.GetManifestResourceNames())
            {
                if (!name.StartsWith("payload/")) continue;
                string rel = name.Substring(8);
                if (rel != "Haloryva.exe" && rel != "Haloryva.sha256" && rel != "haloryva_demo.png")
                    throw new IOException("Ressource inattendue : " + rel);
                string dest = Path.Combine(dir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                FileTransaction.NoLinks(dest);
                using (Stream stream = asm.GetManifestResourceStream(name))
                using (FileStream output = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.CopyTo(output); output.Flush(true); }
                count++;
            }
            return count;
        }

        private static string WorkerRoot { get { return Path.Combine(Path.GetTempPath(), "HaloryvaMaintenance"); } }
        private static int Relocate(bool uninstall)
        {
            string dir = Path.Combine(WorkerRoot, "run-" + Guid.NewGuid().ToString("N"));
            FileTransaction.NoLinks(WorkerRoot); Directory.CreateDirectory(dir);
            string exe = Path.Combine(dir, "HaloryvaSetup.exe");
            FileTransaction.AtomicCopy(System.Reflection.Assembly.GetExecutingAssembly().Location, exe);
            string args = "--worker --parent=" + Process.GetCurrentProcess().Id + (uninstall ? " --uninstall" : "");
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = dir });
            return 0;
        }
        [STAThread]
        private static int Main(string[] args)
        {
            string extractOnly = null; bool demo = false, uninstall = false, worker = false, selftest = false;
            string demoFail = ""; int parent = 0;
            foreach (string a in args)
            {
                if (a == "--uninstall") uninstall = true;
                else if (a == "--worker") worker = true;
                else if (a == "--selftest") selftest = true;
                else if (a.StartsWith("--parent=")) Int32.TryParse(a.Substring(9), out parent);
                else if (a.StartsWith("--extract=")) extractOnly = a.Substring(10);
                else if (a == "--demo") demo = true;
                else if (a.StartsWith("--demo=")) { demo = true; demoFail = a.Substring(7); }
                else return 2; // do not accept a source directory as an installation payload
            }
            try
            {
                if (extractOnly != null) { return ExtractPayload(extractOnly) > 0 ? 0 : 2; }
                if (selftest)
                {
                    string tmpTest = Path.Combine(Path.GetTempPath(), "HaloryvaPayloadTest-" + Guid.NewGuid().ToString("N"));
                    try { ExtractPayload(tmpTest); InstallJob.VerifyPayload(tmpTest); return 0; }
                    finally { if (Directory.Exists(tmpTest)) Directory.Delete(tmpTest, true); }
                }
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                // A temporary copy, outside the installation folder, performs all real maintenance.
                // Thus the registered uninstaller can be deleted synchronously before success is shown.
                if (!worker && !demo) return Relocate(uninstall);
                if (worker)
                {
                    string directory = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
                    if (!String.Equals(Path.GetDirectoryName(directory), Path.GetFullPath(WorkerRoot), StringComparison.OrdinalIgnoreCase) ||
                        !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(directory), "^run-[a-f0-9]{32}$"))
                        throw new IOException("Emplacement de maintenance inattendu.");
                    if (parent > 0)
                    {
                        try { using (Process previous = Process.GetProcessById(parent)) if (!previous.WaitForExit(15000)) throw new IOException("L'ancien lanceur ne s'est pas fermé."); }
                        catch (ArgumentException) { /* parent already exited */ }
                    }
                }
                Directory.CreateDirectory(InstallJob.StateRoot); FileTransaction.NoLinks(InstallJob.StateRoot);
                // FileShare.None protects maintenance across sessions of the same user too.
                int outcome = 0; bool launch = false;
                bool owned;
                using (var oldCompatibleLock = new System.Threading.Mutex(true, @"Local\HaloryvaStudioSetup", out owned))
                {
                if (!owned) throw new IOException("Une installation ou désinstallation est déjà active.");
                using (var operationLock = new FileStream(Path.Combine(InstallJob.StateRoot, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "payload");
                    if (!demo && !uninstall) { ExtractPayload(root); InstallJob.VerifyPayload(root); }
                    using (var form = new SetupForm(root, demo, demoFail, uninstall))
                    { Application.Run(form); outcome = form.OperationExitCode; launch = form.LaunchRequested; }
                    if (!demo && Directory.Exists(root)) Directory.Delete(root, true);
                }
                }
                if (outcome == 0 && launch)
                {
                    string app = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haloryva", "Haloryva.exe");
                    try { Process.Start(new ProcessStartInfo(app) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(app) }); }
                    catch (Exception ex)
                    {
                        MessageBox.Show("L'installation est terminée mais Windows a refusé le lancement. Ne désactive pas ses protections.\n\n" + ex.Message,
                            "Haloryva Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return 4;
                    }
                }
                // This running EXE is a temporary maintenance copy, never a delayed task
                // aimed at the application directory. Windows may reclaim it as temporary data.
                return outcome;
            }
            catch (Exception ex)
            {
                if (!selftest && extractOnly == null)
                    MessageBox.Show("L'assistant n'a pas pu terminer cette opération. Une autre maintenance peut être active.\n\n" + ex.Message +
                        "\n\nAucune protection Windows n'a été désactivée.", "Haloryva Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 3;
            }
        }
    }
}
