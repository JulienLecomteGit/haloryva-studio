using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CursorFlow
{
    // Laisse passer le redimensionnement de la fenêtre sans bordure à travers
    // les grands panneaux qui recouvrent ses bords.
    internal static class EdgeHit
    {
        internal static bool PassThrough(Control c, ref Message m)
        {
            if (m.Msg != 0x84) return false;
            Form f = c.FindForm();
            if (f == null || f.WindowState != FormWindowState.Normal) return false;
            Point p = f.PointToClient(Cursor.Position);
            int edge = PremiumPalette.S(6);
            if (p.X < edge || p.Y < edge || p.X >= f.ClientSize.Width - edge || p.Y >= f.ClientSize.Height - edge)
            { m.Result = new IntPtr(-1); return true; }
            return false;
        }
    }

    // Espace de travail : fond « aurora » ancré au contenu (défile avec les cartes).
    internal sealed class StudioWorkspace : Panel, IBackdrop
    {
        private Bitmap glow;
        public StudioWorkspace()
        {
            BackColor = PremiumPalette.Background; AutoScroll = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            Resize += delegate { if (glow == null || glow.Width != Math.Max(1, ClientSize.Width)) Rebuild(); };
        }
        internal void Rebuild()
        {
            if (glow != null) glow.Dispose();
            int w = Math.Max(1, ClientSize.Width), h = PremiumPalette.S(560);
            glow = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(glow))
            {
                g.Clear(PremiumPalette.Background);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Blob(g, new RectangleF(-w * .18f, -h * .55f, w * .62f, h * 1.15f), PremiumPalette.Cyan, 26);
                Blob(g, new RectangleF(w * .55f, -h * .6f, w * .62f, h * 1.2f), PremiumPalette.Purple, 30);
                using (LinearGradientBrush fade = new LinearGradientBrush(new Rectangle(0, h / 2, w, h / 2 + 1),
                    Color.FromArgb(0, PremiumPalette.Background), PremiumPalette.Background, 90f))
                    g.FillRectangle(fade, 0, h / 2, w, h / 2 + 1);
            }
            Invalidate(true);
        }
        private static void Blob(Graphics g, RectangleF r, Color c, int alpha)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(r);
                using (PathGradientBrush b = new PathGradientBrush(p))
                {
                    b.CenterColor = Color.FromArgb(alpha, c);
                    b.SurroundColors = new Color[] { Color.FromArgb(0, c) };
                    g.FillPath(b, p);
                }
            }
        }
        private void PaintGlow(Graphics g, int offsetX, int offsetY)
        {
            g.Clear(PremiumPalette.Background);
            if (glow == null) Rebuild();
            g.DrawImageUnscaled(glow, offsetX, offsetY + AutoScrollPosition.Y);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { PaintGlow(e.Graphics, 0, 0); }
        public void PaintBackdrop(Graphics g, Control child) { PaintGlow(g, -child.Left, -child.Top); }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { Native.ApplyDarkScrollbars(Handle); } catch { }
        }
        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); PremiumSelect.CloseActive(); ColorWheelPopup.CloseActive(); Invalidate(true); }
        protected override void OnMouseWheel(MouseEventArgs e) { PremiumSelect.CloseActive(); ColorWheelPopup.CloseActive(); base.OnMouseWheel(e); Invalidate(true); }
        protected override void WndProc(ref Message m) { if (!EdgeHit.PassThrough(this, ref m)) base.WndProc(ref m); }
        protected override void Dispose(bool disposing) { if (disposing && glow != null) glow.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class StudioSidebar : Panel
    {
        public StudioSidebar()
        {
            BackColor = PremiumPalette.Sidebar;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            using (Pen line = new Pen(PremiumPalette.Border)) e.Graphics.DrawLine(line, Width - 1, 0, Width - 1, Height);
        }
        protected override void WndProc(ref Message m) { if (!EdgeHit.PassThrough(this, ref m)) base.WndProc(ref m); }
    }

    internal sealed class StudioHeaderBar : Panel
    {
        private readonly Timer shimmer = new Timer();
        private float shimmerX = -0.3f;
        public StudioHeaderBar()
        {
            BackColor = PremiumPalette.Header;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            // Reflet lumineux qui parcourt le liseré supérieur (zone de 3 px seulement).
            shimmer.Interval = 40;
            shimmer.Tick += delegate { shimmerX += .012f; if (shimmerX > 1.6f) shimmerX = -0.4f; Invalidate(new Rectangle(0, 0, Width, PremiumPalette.S(3))); };
            shimmer.Start();
            Disposed += delegate { shimmer.Dispose(); };
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            using (Pen line = new Pen(PremiumPalette.Border)) g.DrawLine(line, 0, Height - 1, Width, Height - 1);
            using (LinearGradientBrush top = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(2, Width), 2), PremiumPalette.Cyan, PremiumPalette.Purple, 0f))
                g.FillRectangle(top, 0, 0, Width, PremiumPalette.S(2));
            int sw = Math.Max(40, Width / 5), sx = (int)(shimmerX * Width);
            using (LinearGradientBrush shine = new LinearGradientBrush(new Rectangle(sx - 1, 0, sw + 2, 3), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 0f))
            {
                ColorBlend cb = new ColorBlend(3);
                cb.Colors = new Color[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(220, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) };
                cb.Positions = new float[] { 0f, .5f, 1f };
                shine.InterpolationColors = cb;
                g.FillRectangle(shine, sx, 0, sw, PremiumPalette.S(2));
            }
            int d = PremiumPalette.S(36), x = PremiumPalette.S(18), y = (Height - d) / 2;
            Rectangle logo = new Rectangle(x, y, d, d);
            Brand.DrawLogo(g, logo, 0f);
            int tx = logo.Right + PremiumPalette.S(12);
            Font brand = PremiumPalette.Font(13f, 2);
            PremiumPalette.TextAt(g, "Haloryva", brand, PremiumPalette.Text, new Rectangle(tx, y - PremiumPalette.S(1), PremiumPalette.S(160), PremiumPalette.S(22)), ContentAlignment.MiddleLeft);
            int bw = PremiumPalette.TextWidth("Haloryva", brand);
            PremiumPalette.TextAt(g, "Studio", brand, PremiumPalette.Cyan, new Rectangle(tx + bw + PremiumPalette.S(5), y - PremiumPalette.S(1), PremiumPalette.S(90), PremiumPalette.S(22)), ContentAlignment.MiddleLeft);
            PremiumPalette.TextAt(g, L.T("Effets, annotations et présentation du curseur"), PremiumPalette.Font(8.25f, 0), PremiumPalette.Faint,
                new Rectangle(tx, y + PremiumPalette.S(20), PremiumPalette.S(330), PremiumPalette.S(17)), ContentAlignment.MiddleLeft);
            Rectangle chip = new Rectangle(tx + bw + PremiumPalette.S(66), y + PremiumPalette.S(1), PremiumPalette.S(42), PremiumPalette.S(19));
            using (GraphicsPath p = PremiumPalette.Round(chip, chip.Height / 2))
            using (SolidBrush b = new SolidBrush(PremiumPalette.Alpha(PremiumPalette.Purple, 40)))
            using (Pen edge = new Pen(PremiumPalette.Alpha(PremiumPalette.Purple, 110))) { g.FillPath(b, p); g.DrawPath(edge, p); }
            PremiumPalette.TextAt(g, Brand.Version, PremiumPalette.Font(7.75f, 1), Color.FromArgb(205, 190, 255), chip, ContentAlignment.MiddleCenter);
        }
        protected override void WndProc(ref Message m) { if (!EdgeHit.PassThrough(this, ref m)) base.WndProc(ref m); }
    }

    // En-tête de page : icône, titre, sous-titre et action principale de la page.
    internal sealed class PageHeading : Control, IBackdrop
    {
        internal string Title = "", Subtitle = "", IconName = "home";
        private Control accessory;
        public PageHeading()
        {
            Height = PremiumPalette.S(70);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Resize += delegate { PlaceAccessory(); };
        }
        internal Control Accessory
        {
            get { return accessory; }
            set
            {
                if (accessory != null) { Controls.Remove(accessory); accessory.Dispose(); }
                accessory = value;
                if (value != null) Controls.Add(value);
                PlaceAccessory(); Invalidate();
            }
        }
        private void PlaceAccessory()
        {
            if (accessory != null) accessory.Location = new Point(Width - accessory.Width, (Height - accessory.Height) / 2);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { StudioControl.PaintParent(this, e.Graphics); }
        // L'action de la page (interrupteur, bouton) reprend le fond « aurora ».
        public void PaintBackdrop(Graphics g, Control child)
        {
            IBackdrop parent = Parent as IBackdrop;
            if (parent == null) { g.Clear(PremiumPalette.Background); return; }
            GraphicsState st = g.Save();
            g.TranslateTransform(-child.Left, -child.Top);
            parent.PaintBackdrop(g, this);
            g.Restore(st);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            int d = PremiumPalette.S(52);
            Rectangle bubble = new Rectangle(0, (Height - d) / 2, d, d);
            using (SolidBrush glow = new SolidBrush(PremiumPalette.Alpha(PremiumPalette.Cyan, 26)))
                g.FillEllipse(glow, bubble.X - PremiumPalette.S(6), bubble.Y - PremiumPalette.S(6), d + PremiumPalette.S(12), d + PremiumPalette.S(12));
            PremiumPalette.IconBubble(g, bubble, IconName, PremiumPalette.Cyan, PremiumPalette.Purple, true);
            int x = d + PremiumPalette.S(18), right = Width - (accessory != null ? accessory.Width + PremiumPalette.S(20) : 0);
            PremiumPalette.GradientText(g, Title, PremiumPalette.Font(20f, 2), new Rectangle(x, PremiumPalette.S(4), Math.Max(10, right - x), PremiumPalette.S(40)),
                Color.White, PremiumPalette.Mix(PremiumPalette.Cyan, Color.White, .35));
            PremiumPalette.TextAt(g, Subtitle, PremiumPalette.Font(9.75f, 0), PremiumPalette.Subtle, new Rectangle(x, PremiumPalette.S(41), Math.Max(10, right - x), PremiumPalette.S(22)), ContentAlignment.MiddleLeft);
        }
    }

    // Pied de barre latérale : profil actif + bouton de repli.
    internal sealed class SidebarFooter : Control
    {
        internal string Profile = L.T("Personnalisé");
        internal bool Collapsed;
        public SidebarFooter()
        {
            Height = PremiumPalette.S(64); BackColor = PremiumPalette.Sidebar;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g); g.Clear(BackColor);
            if (Collapsed) return;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = PremiumPalette.Round(r, PremiumPalette.S(12)))
            using (LinearGradientBrush b = new LinearGradientBrush(r, PremiumPalette.Alpha(PremiumPalette.Cyan, 22), PremiumPalette.Alpha(PremiumPalette.Purple, 30), 0f))
            using (Pen edge = new Pen(PremiumPalette.Border)) { g.FillPath(b, p); g.DrawPath(edge, p); }
            int d = PremiumPalette.S(32);
            PremiumPalette.IconBubble(g, new Rectangle(PremiumPalette.S(12), (Height - d) / 2, d, d), "sparkles", PremiumPalette.Cyan, PremiumPalette.Purple, true);
            int x = PremiumPalette.S(54);
            PremiumPalette.TextAt(g, L.T("PROFIL ACTIF"), PremiumPalette.Font(7.5f, 1), PremiumPalette.Faint, new Rectangle(x, Height / 2 - PremiumPalette.S(18), Width - x - 8, PremiumPalette.S(16)), ContentAlignment.MiddleLeft);
            PremiumPalette.TextAt(g, Profile, PremiumPalette.Font(10f, 1), PremiumPalette.Text, new Rectangle(x, Height / 2 - PremiumPalette.S(2), Width - x - 8, PremiumPalette.S(20)), ContentAlignment.MiddleLeft);
        }
    }

    internal sealed class AboutBlock : Control
    {
        public AboutBlock()
        {
            Height = PremiumPalette.S(286); BackColor = PremiumPalette.Surface;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g); g.Clear(BackColor);
            int d = PremiumPalette.S(84);
            Brand.DrawLogo(g, new RectangleF(0, PremiumPalette.S(2), d, d), 0f);
            int x = d + PremiumPalette.S(20), w = Math.Max(10, Width - x);
            Font title = PremiumPalette.Font(14f, 2);
            PremiumPalette.TextAt(g, "Haloryva", title, PremiumPalette.Text, new Rectangle(x, PremiumPalette.S(2), w, PremiumPalette.S(28)), ContentAlignment.MiddleLeft);
            PremiumPalette.TextAt(g, "Studio", title, PremiumPalette.Cyan, new Rectangle(x + PremiumPalette.TextWidth("Haloryva", title) + PremiumPalette.S(6), PremiumPalette.S(2), w, PremiumPalette.S(28)), ContentAlignment.MiddleLeft);
            PremiumPalette.TextAt(g, L.T("Version ") + Brand.Version + L.T(" · build ") + Brand.BuildDate + " · Windows x64", PremiumPalette.Font(9f, 0), PremiumPalette.Subtle, new Rectangle(x, PremiumPalette.S(32), w, PremiumPalette.S(20)), ContentAlignment.MiddleLeft);
            PremiumPalette.TextAt(g, L.T("Création : ") + Brand.Author, PremiumPalette.Font(10f, 1), PremiumPalette.Text, new Rectangle(x, PremiumPalette.S(54), w, PremiumPalette.S(22)), ContentAlignment.MiddleLeft);
            PremiumPalette.TextAt(g, Brand.Copyright + L.T(" · Tous droits réservés"), PremiumPalette.Font(8.75f, 0), PremiumPalette.Faint, new Rectangle(x, PremiumPalette.S(76), w, PremiumPalette.S(18)), ContentAlignment.MiddleLeft);
            PremiumPalette.TextAt(g, Brand.Privacy, PremiumPalette.Font(8.75f, 0), PremiumPalette.Faint, new Rectangle(x, PremiumPalette.S(94), w, PremiumPalette.S(18)), ContentAlignment.MiddleLeft);

            int y = PremiumPalette.S(128);
            using (Pen line = new Pen(PremiumPalette.Border)) g.DrawLine(line, 0, y - PremiumPalette.S(8), Width, y - PremiumPalette.S(8));
            PremiumPalette.TextAt(g, L.T("HISTORIQUE DES VERSIONS"), PremiumPalette.Font(8f, 1), PremiumPalette.Faint, new Rectangle(0, y, w + x, PremiumPalette.S(18)), ContentAlignment.MiddleLeft);
            y += PremiumPalette.S(24);
            for (int i = 0; i < Brand.History.Length; i++)
            {
                string[] h = Brand.History[i];
                Rectangle chip = new Rectangle(0, y, PremiumPalette.S(50), PremiumPalette.S(20));
                Color c = i == 0 ? PremiumPalette.Cyan : PremiumPalette.Purple;
                using (GraphicsPath p = PremiumPalette.Round(chip, chip.Height / 2))
                using (SolidBrush b = new SolidBrush(PremiumPalette.Alpha(c, i == 0 ? 46 : 30)))
                using (Pen edge = new Pen(PremiumPalette.Alpha(c, i == 0 ? 130 : 80))) { g.FillPath(b, p); g.DrawPath(edge, p); }
                PremiumPalette.TextAt(g, h[0], PremiumPalette.Font(8f, 1), i == 0 ? PremiumPalette.Text : Color.FromArgb(205, 190, 255), chip, ContentAlignment.MiddleCenter);
                PremiumPalette.TextAt(g, h[2], PremiumPalette.Font(9f, 0), i == 0 ? PremiumPalette.Text : PremiumPalette.Subtle, new Rectangle(chip.Right + PremiumPalette.S(12), y, Math.Max(10, Width - chip.Right - PremiumPalette.S(100)), chip.Height), ContentAlignment.MiddleLeft);
                PremiumPalette.TextAt(g, h[1], PremiumPalette.Font(8.25f, 0), PremiumPalette.Faint, new Rectangle(Width - PremiumPalette.S(84), y, PremiumPalette.S(84), chip.Height), ContentAlignment.MiddleRight);
                y += PremiumPalette.S(24);
            }
        }
    }

    // Studio V2 : navigation groupée, tableau de bord, sous-onglets, cartes en
    // colonnes « masonry », aperçus animés et échelle DPI cohérente.
    internal sealed class PremiumStudioForm : Form
    {
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();

        private readonly IStudioEngine engine;
        private Config cfg;
        private readonly StudioHeaderBar header;
        private readonly StudioSidebar nav;
        private readonly StudioWorkspace workspace;
        private readonly PageHeading heading;
        private readonly StatusPill statusPill;
        private readonly PremiumButton quickDraw, collapseButton;
        private readonly SidebarFooter footer;
        private TabStrip sections;
        private Label detectedButton;
        private MouseDiagram mouseDiagram;
        private HeroBanner hero;
        private readonly List<PremiumNavButton> tabs = new List<PremiumNavButton>();
        private readonly List<Label> groupLabels = new List<Label>();
        private readonly List<PremiumCard> cards = new List<PremiumCard>();
        private readonly List<Control> extras = new List<Control>();
        private readonly List<Action> sync = new List<Action>();
        private PremiumPreview preview;
        private int page = 0, section = 0;
        private bool refreshing, committing, minimizedNav, userCollapsed, laying;
        private string currentDetected = "Aucun";
        private string lastObservedNativeButton = "Aucun";

        private static readonly string[] Pages = { L.T("Accueil"), L.T("Halo"), L.T("Traînées"), L.T("Clics"), L.T("Boutons souris"), L.T("Dessin"), "Présentation", L.T("Préférences") };
        private static readonly string[] Icons = { "home", "halo", "trail", "click", "mouse", "pen", "present", "settings" };
        private static readonly string[] Subtitles = {
            L.T("Tout ton studio en un coup d'œil : modules, styles et raccourcis."),
            L.T("Un halo précis et lumineux qui rend ton pointeur visible partout."),
            L.T("Une signature de mouvement fluide à chaque déplacement."),
            L.T("Des animations nettes pour rendre chaque clic lisible."),
            L.T("Chaque bouton physique peut déclencher une action Haloryva."),
            L.T("Annote ton écran librement — les formes se régularisent toutes seules."),
            L.T("Touches, loupe et focus pour présenter et expliquer."),
            L.T("Performance, profils et informations sur l'application.") };
        // Ordre d'affichage dans la barre latérale (null = intitulé de groupe).
        private static readonly object[] NavOrder = { 0, L.T("EFFETS"), 1, 2, 3, L.T("ATELIER"), 5, 6, L.T("SYSTÈME"), 4, 7 };

        private static int S(int v) { return PremiumPalette.S(v); }

        public PremiumStudioForm(IStudioEngine owner)
        {
            engine = owner; cfg = engine.Options;
            // InitScale can create the native handle. Choose manual placement before
            // that happens, then set the complete DPI-sized bounds below.
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            PremiumPalette.InitScale(this);
            PremiumPalette.ApplyAccent(cfg.StudioAccent);
            Text = "Haloryva Studio";
            BackColor = PremiumPalette.Background; ForeColor = PremiumPalette.Text;
            KeyPreview = true;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            MinimumSize = new Size(Math.Min(S(960), work.Width), Math.Min(S(620), work.Height));   // jamais plus grand que l'écran
            int initialWidth = Math.Max(Math.Min(S(960), work.Width), Math.Min(S(1440), work.Width - 40));
            int initialHeight = Math.Max(Math.Min(S(620), work.Height), Math.Min(S(900), work.Height - 40));
            // Center once when creating the Studio, within the monitor's working area
            // (including its origin). Showing an existing window keeps the user's position.
            Bounds = new Rectangle(work.Left + (work.Width - initialWidth) / 2,
                work.Top + (work.Height - initialHeight) / 2, initialWidth, initialHeight);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            header = new StudioHeaderBar(); Controls.Add(header);
            header.MouseDown += BeginWindowMove;
            header.DoubleClick += delegate { ToggleMaximize(); };
            statusPill = new StatusPill();
            statusPill.Click += delegate { engine.ToggleEnabled(); RefreshStates(); };
            header.Controls.Add(statusPill);
            quickDraw = new PremiumButton(L.T("Dessiner"), ButtonKind.Secondary, "pen"); quickDraw.Height = S(34); quickDraw.Width = S(118);
            quickDraw.Click += delegate { engine.DrawOnDesktop(); };
            header.Controls.Add(quickDraw);
            PremiumButton minimize = WindowButton("minus", ButtonKind.Ghost, L.T("Réduire"));
            PremiumButton maximize = WindowButton("square", ButtonKind.Ghost, L.T("Agrandir"));
            PremiumButton close = WindowButton("x", ButtonKind.WindowClose, L.T("Fermer (Haloryva reste actif près de l'horloge)"));
            minimize.Click += delegate { WindowState = FormWindowState.Minimized; };
            maximize.Click += delegate { ToggleMaximize(); };
            close.Click += delegate { Close(); };
            header.Controls.Add(minimize); header.Controls.Add(maximize); header.Controls.Add(close);
            header.Resize += delegate
            {
                int y = (header.Height - S(34)) / 2;
                close.SetBounds(header.Width - S(10) - S(44), y, S(44), S(34));
                maximize.SetBounds(close.Left - S(46), y, S(44), S(34));
                minimize.SetBounds(maximize.Left - S(46), y, S(44), S(34));
                quickDraw.SetBounds(minimize.Left - S(18) - quickDraw.Width, y, quickDraw.Width, S(34));
                statusPill.Location = new Point(quickDraw.Left - S(10) - statusPill.Width, y);
            };

            nav = new StudioSidebar(); Controls.Add(nav);
            foreach (object item in NavOrder)
            {
                if (item is string)
                {
                    Label group = new Label(); group.Text = (string)item; group.Font = PremiumPalette.Font(7.75f, 1);
                    group.ForeColor = PremiumPalette.Faint; group.BackColor = PremiumPalette.Sidebar; group.AutoSize = false;
                    group.TextAlign = ContentAlignment.BottomLeft; group.Height = S(30); group.Tag = item;
                    nav.Controls.Add(group); groupLabels.Add(group);
                    continue;
                }
                int pageId = (int)item;
                PremiumNavButton tab = new PremiumNavButton(Pages[pageId], Icons[pageId]);
                tab.Tag = pageId; tab.Click += delegate { Navigate(pageId, 0); };
                nav.Controls.Add(tab); tabs.Add(tab);
            }
            footer = new SidebarFooter(); nav.Controls.Add(footer);
            footer.Cursor = Cursors.Hand; footer.Click += delegate { Navigate(0, 0); };
            collapseButton = new PremiumButton(L.T("Replier"), ButtonKind.Ghost, "sidebar"); collapseButton.Height = S(36);
            collapseButton.Click += delegate { userCollapsed = !userCollapsed; ApplyNavWidth(); };
            nav.Controls.Add(collapseButton);
            nav.Resize += delegate { LayoutNav(); };

            workspace = new StudioWorkspace(); Controls.Add(workspace);
            heading = new PageHeading(); workspace.Controls.Add(heading);
            workspace.Resize += delegate { LayoutCards(); };
            Resize += delegate { ApplyNavWidth(); };
            KeyDown += OnStudioKey;
            engine.SettingsChanged += EngineChanged;
            engine.MouseButtonObserved += ButtonObserved;
            FormClosed += delegate
            {
                engine.SettingsChanged -= EngineChanged;
                engine.MouseButtonObserved -= ButtonObserved;
                engine.CancelButtonCapture();
            };
            // Mise en page recalculée une fois la fenêtre réellement affichée (tailles définitives).
            Shown += async delegate { LayoutCards(); workspace.Invalidate(true); engine.KeepEffectLayersVisible(); await engine.RefreshStartupStatusAsync(); };
            ResizeEnd += delegate { LayoutCards(); workspace.Invalidate(true); };
            Activated += async delegate { if (IsHandleCreated) BeginInvoke((Action)delegate { engine.KeepEffectLayersVisible(); }); await engine.RefreshStartupStatusAsync(); };
            ApplyNavWidth(); Navigate(0, 0);
        }

        private PremiumButton WindowButton(string icon, ButtonKind kind, string name)
        {
            PremiumButton b = new PremiumButton("", kind, icon); b.AccessibleName = name; b.TabStop = false;
            new ToolTip().SetToolTip(b, name);
            return b;
        }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;          // CS_DROPSHADOW : ombre de la fenêtre sans bordure
                return cp;
            }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { Native.ApplyRoundedCorners(Handle); } catch { }
        }

        internal bool IsPopupAt(int screenX, int screenY)
        {
            // Les listes sont de vrais contrôles enfants de la fenêtre, et non des HWND externes.
            return false;
        }
        private void OnStudioKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                if (ColorWheelPopup.Active != null) ColorWheelPopup.CloseActive();
                else if (PremiumSelect.IsOpen) PremiumSelect.CloseActive();
                else if (engine.IsDrawing) engine.ToggleDrawing();
                else Close();
                e.Handled = true; e.SuppressKeyPress = true;
            }
            else if (e.Control && !e.Alt && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D8)
            {
                Navigate(e.KeyCode - Keys.D1, 0); e.Handled = true; e.SuppressKeyPress = true;
            }
        }
        private void BeginWindowMove(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Clicks > 1) return;
            if (WindowState == FormWindowState.Maximized) return;
            try { ReleaseCapture(); Native.SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); } catch { }
        }
        private void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized) { WindowState = FormWindowState.Normal; return; }
            // Correctif : une fenêtre sans bordure maximisée recouvrait la barre des tâches.
            Screen screen = Screen.FromControl(this);
            Rectangle wa = screen.WorkingArea, b = screen.Bounds;
            MaximizedBounds = new Rectangle(wa.X - b.X, wa.Y - b.Y, wa.Width, wa.Height);
            WindowState = FormWindowState.Maximized;
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84 && WindowState == FormWindowState.Normal)
            {
                Point p = PointToClient(Cursor.Position);
                int edge = S(6); int x = p.X, y = p.Y;
                bool l = x < edge, r = x >= ClientSize.Width - edge, t = y < edge, b = y >= ClientSize.Height - edge;
                int hit = t && l ? 13 : t && r ? 14 : b && l ? 16 : b && r ? 17 : l ? 10 : r ? 11 : t ? 12 : b ? 15 : 0;
                if (hit != 0) { m.Result = new IntPtr(hit); return; }
            }
            base.WndProc(ref m);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen edge = new Pen(PremiumPalette.BorderStrong)) e.Graphics.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
        }

        // ---------- Coque : en-tête, navigation, espace de travail ----------
        private void ApplyNavWidth()
        {
            bool reduce = userCollapsed || Width < S(1120);
            minimizedNav = reduce;
            LayoutShell(); LayoutNav();
        }
        private void LayoutShell()
        {
            if (workspace == null || nav == null || header == null) return;
            int b = 1, top = S(64), navW = minimizedNav ? S(76) : S(236);
            header.SetBounds(b, b, ClientSize.Width - b * 2, top);
            nav.SetBounds(b, b + top, navW, Math.Max(0, ClientSize.Height - top - b * 2));
            workspace.SetBounds(nav.Right, b + top, Math.Max(0, ClientSize.Width - nav.Right - b), Math.Max(0, ClientSize.Height - top - b * 2));
        }
        private void LayoutNav()
        {
            int pad = S(12), y = S(14), w = Math.Max(S(40), nav.ClientSize.Width - pad * 2 - 1);
            foreach (Control c in nav.Controls)
            {
                if (c == footer || c == collapseButton) continue;
                Label group = c as Label;
                if (group != null)
                {
                    group.Text = minimizedNav ? "" : (string)group.Tag;
                    group.SetBounds(pad + S(14), y, w - S(14), minimizedNav ? S(14) : S(30));
                    y += group.Height + S(6);
                    continue;
                }
                PremiumNavButton tab = (PremiumNavButton)c;
                tab.Collapsed = minimizedNav;
                tab.SetBounds(pad, y, w, S(42));
                tab.Selected = (int)tab.Tag == page;
                y += S(46);
            }
            collapseButton.Text = minimizedNav ? "" : (userCollapsed ? L.T("Déplier") : L.T("Replier le menu"));
            collapseButton.SetBounds(pad, nav.ClientSize.Height - S(14) - S(36), w, S(36));
            footer.Collapsed = minimizedNav;
            footer.Visible = !minimizedNav && collapseButton.Top - S(80) > y;
            footer.SetBounds(pad, collapseButton.Top - S(10) - S(64), w, S(64));
        }

        private void Navigate(int index, int sectionIndex)
        {
            PremiumSelect.CloseActive(); ColorWheelPopup.CloseActive();
            if (index < 0 || index >= Pages.Length) return;
            page = index; section = sectionIndex;
            Redraw(false);
            workspace.SuspendLayout();
            try
            {
                workspace.AutoScrollPosition = new Point(0, 0);
                foreach (PremiumNavButton tab in tabs) tab.Selected = (int)tab.Tag == page;
                foreach (PremiumCard card in cards) { workspace.Controls.Remove(card); card.Dispose(); }
                foreach (Control c in extras) { workspace.Controls.Remove(c); c.Dispose(); }
                cards.Clear(); extras.Clear(); sync.Clear();
                preview = null; detectedButton = null; mouseDiagram = null; hero = null; sections = null;
                heading.Title = Pages[page]; heading.Subtitle = Subtitles[page]; heading.IconName = Icons[page];
                heading.Accessory = null;
                if (page == 0) PageHome();
                if (page == 1) PageHalo();
                if (page == 2) PageTrails();
                if (page == 3) PageClicks();
                if (page == 4) PageMouseButtons();
                if (page == 5) PageDrawing();
                if (page == 6) PageTools();
                if (page == 7) PagePreferences();
            }
            finally
            {
                workspace.ResumeLayout(false);
                LayoutCards(); RefreshStates();
                Redraw(true);
                workspace.Invalidate(true);
            }
        }
        // Gèle le dessin pendant la reconstruction d'une page (aucun clignotement).
        private void Redraw(bool on)
        {
            if (!workspace.IsHandleCreated) return;
            try { Native.SendMessage(workspace.Handle, Native.WM_SETREDRAW, on ? new IntPtr(1) : IntPtr.Zero, IntPtr.Zero); }
            catch { }
        }
        private void Sections(string[] names, string[] icons)
        {
            sections = new TabStrip(names, icons); sections.Index = section;
            sections.Changed += delegate(int i) { Navigate(page, i); };
            workspace.Controls.Add(sections); extras.Add(sections);
        }
        private PremiumCard Card(string name, string desc, int span, string icon)
        {
            PremiumCard c = new PremiumCard(name, desc, span); c.IconName = icon;
            cards.Add(c); workspace.Controls.Add(c); return c;
        }
        private PremiumCard Card(string name, string desc, int span) { return Card(name, desc, span, null); }

        // Cartes en colonnes « masonry » : chaque carte va dans la colonne la plus courte,
        // ce qui supprime les grands vides de l'ancienne grille par rangées.
        private void LayoutCards()
        {
            if (laying || workspace.IsDisposed || workspace.ClientSize.Width < 1) return;
            laying = true;
            try
            {
                int margin = S(32), gap = S(18);
                int available = Math.Max(S(300), workspace.ClientSize.Width - margin * 2);
                int cols = available >= S(1120) ? 3 : available >= S(700) ? 2 : 1;
                int cell = (available - gap * (cols - 1)) / cols;
                Point scroll = workspace.AutoScrollPosition;
                heading.SetBounds(margin + scroll.X, S(26) + scroll.Y, available, S(70));
                int top = heading.Bottom - scroll.Y + S(18);
                if (hero != null)
                {
                    hero.SetBounds(margin + scroll.X, top + scroll.Y, available, hero.Height);
                    top += hero.Height + gap;
                }
                if (sections != null)
                {
                    sections.SetBounds(margin + scroll.X, top + scroll.Y, Math.Min(available, sections.PreferredWidth), sections.Height);
                    top += sections.Height + S(18);
                }
                int[] heights = new int[cols];
                for (int i = 0; i < cols; i++) heights[i] = top;
                foreach (PremiumCard card in cards)
                {
                    int span = Math.Min(cols, Math.Max(1, card.Span));
                    int best = 0, bestY = int.MaxValue;
                    for (int c = 0; c + span <= cols; c++)
                    {
                        int y = 0; for (int k = c; k < c + span; k++) y = Math.Max(y, heights[k]);
                        if (y < bestY) { bestY = y; best = c; }
                    }
                    int width = cell * span + gap * (span - 1);
                    card.SetBounds(margin + best * (cell + gap) + scroll.X, bestY + scroll.Y, width, Math.Max(card.Height, S(120)));
                    card.PlaceStack();
                    int h = Math.Max(S(120), card.PreferredCardHeight);
                    if (card.Height != h) { card.Height = h; card.PlaceStack(); }
                    for (int k = best; k < best + span; k++) heights[k] = bestY + h + gap;
                }
                int bottom = top;
                foreach (int h in heights) bottom = Math.Max(bottom, h);
                workspace.AutoScrollMinSize = new Size(0, bottom + S(14));
            }
            finally { laying = false; }
        }

        // ---------- Synchronisation moteur <-> interface ----------
        private void CommitSettings()
        {
            cfg.Normalize();
            committing = true;
            try { engine.Changed(); } finally { committing = false; }
            RefreshStates();
        }
        private void EngineChanged()
        {
            if (IsDisposed || committing) return;
            if (InvokeRequired) { if (IsHandleCreated) BeginInvoke((Action)RefreshStates); }
            else RefreshStates();
        }
        private void ButtonObserved(string button)
        {
            lastObservedNativeButton = button;
            currentDetected = SideButtonMap.ToDisplay(button, engine.Options.SwapSideButtons);
            Action update = delegate
            {
                if (detectedButton != null && !detectedButton.IsDisposed) detectedButton.Text = L.T("Dernier bouton détecté : ") + L.T(currentDetected);
                if (mouseDiagram != null && !mouseDiagram.IsDisposed) { mouseDiagram.Detected = currentDetected; mouseDiagram.Invalidate(); }
            };
            if (InvokeRequired) { if (IsHandleCreated) BeginInvoke(update); }
            else update();
        }
        private void RefreshStates()
        {
            if (refreshing || IsDisposed) return;
            refreshing = true; cfg = engine.Options;
            try
            {
                statusPill.Active = cfg.Enabled;
                statusPill.Location = new Point(quickDraw.Left - S(10) - statusPill.Width, statusPill.Top);
                quickDraw.Selected = engine.IsDrawing;
                quickDraw.Text = engine.IsDrawing ? L.T("Dessin actif") : L.T("Dessiner");
                foreach (PremiumNavButton tab in tabs)
                {
                    int id = (int)tab.Tag;
                    tab.Indicator = id == 1 ? cfg.Enabled && cfg.Ring : id == 2 ? cfg.Enabled && cfg.Trail : id == 3 ? cfg.Enabled && cfg.Clicks :
                        id == 5 ? engine.IsDrawing : id == 6 ? cfg.Enabled && (cfg.KeyDisplay || cfg.Loupe || cfg.Focus) : false;
                    tab.IndicatorColor = id == 5 ? PremiumPalette.Pink : PremiumPalette.Green;
                }
                string profile = cfg.ActivePresetSignature == PresetFingerprint() && !String.IsNullOrEmpty(cfg.ActivePreset) ? cfg.ActivePreset : L.T("Personnalisé");
                if (footer.Profile != profile) { footer.Profile = profile; footer.Invalidate(); }
                foreach (Action action in sync) action();
                // Lors du changement d'orientation, le dernier bouton détecté suit
                // la nouvelle étiquette, sans échanger les actions enregistrées.
                currentDetected = SideButtonMap.ToDisplay(lastObservedNativeButton, cfg.SwapSideButtons);
                if (detectedButton != null && !detectedButton.IsDisposed)
                    detectedButton.Text = L.T("Dernier bouton détecté : ") + L.T(currentDetected);
                if (mouseDiagram != null && !mouseDiagram.IsDisposed)
                { mouseDiagram.Detected = currentDetected; mouseDiagram.Invalidate(); }
                if (preview != null) { preview.Config = cfg; preview.Invalidate(); }
            }
            finally { refreshing = false; }
        }

        // ---------- Fabriques de contrôles liés à la configuration ----------
        private PremiumButton Button(PremiumCard card, string name, Action action, ButtonKind kind, string icon)
        {
            PremiumButton b = new PremiumButton(name, kind, icon);
            b.Click += delegate { action(); RefreshStates(); };
            if (card != null) card.Add(b);
            return b;
        }
        private PremiumButton Button(PremiumCard card, string name, Action action, bool main)
        {
            return Button(card, name, action, main ? ButtonKind.Primary : ButtonKind.Secondary, null);
        }
        private PremiumButton StatefulButton(PremiumCard card, string name, Action action, Func<bool> active)
        {
            PremiumButton b = Button(card, name, action, false);
            sync.Add(delegate { b.Selected = active(); }); return b;
        }
        private PremiumSwitch Toggle(PremiumCard card, string name, string desc, Func<bool> getter, Action<bool> setter, string icon)
        {
            PremiumSwitch sw = new PremiumSwitch(name, desc, getter()); sw.IconName = icon;
            if (!String.IsNullOrEmpty(icon)) sw.Height = S(56);
            sw.CheckedChanged += delegate { if (!refreshing) { setter(sw.Checked); CommitSettings(); } };
            if (card != null) card.Add(sw);
            sync.Add(delegate { sw.Checked = getter(); });
            return sw;
        }
        private PremiumSwitch Toggle(PremiumCard card, string name, string desc, Func<bool> getter, Action<bool> setter)
        {
            return Toggle(card, name, desc, getter, setter, null);
        }
        private void MasterToggle(string name, Func<bool> getter, Action<bool> setter)
        {
            PremiumSwitch sw = Toggle(null, name, null, getter, setter, null);
            sw.Compact = true; sw.Height = S(40); sw.Width = sw.PreferredCompactWidth;
            heading.Accessory = sw;
        }
        private PremiumSlider Slider(PremiumCard card, string name, int min, int max, string unit, Func<int> getter, Action<int> setter)
        {
            PremiumSlider slider = new PremiumSlider(name, min, max, getter(), unit);
            slider.ValueChanged += delegate(int n) { if (!refreshing) { setter(n); CommitSettings(); } };
            card.Add(slider); sync.Add(delegate { slider.Value = getter(); });
            return slider;
        }
        private Panel Select(Control host, string title, string[] values, Func<string> getter, Action<string> setter)
        {
            Panel row = new Panel(); row.Height = S(70); row.BackColor = PremiumPalette.Surface;
            Label label = FieldLabel(title); label.SetBounds(0, 0, S(300), S(24)); row.Controls.Add(label);
            PremiumSelect select = new PremiumSelect(values, getter()); select.Location = new Point(0, S(28)); select.AccessibleName = L.T(title);
            row.Controls.Add(select);
            row.Resize += delegate { select.Width = Math.Max(S(50), row.ClientSize.Width); label.Width = row.Width; };
            select.SelectedChanged += delegate(string s) { if (!refreshing) { setter(s); CommitSettings(); } };
            PremiumCard card = host as PremiumCard;
            if (card != null) card.Add(row); else host.Controls.Add(row);
            sync.Add(delegate { select.Value = getter(); });
            return row;
        }
        private PremiumChoiceGrid Choices(PremiumCard card, string title, string[] values, string[] captions, string[] icons,
            int columns, bool tiles, Func<string> getter, Action<string> setter)
        {
            if (!String.IsNullOrEmpty(title)) card.Add(FieldLabel(title));
            PremiumChoiceGrid grid = new PremiumChoiceGrid(values, captions, icons, columns, tiles);
            grid.Value = getter(); grid.AccessibleName = L.T(title);
            grid.ValueChanged += delegate(string v) { if (!refreshing) { setter(v); CommitSettings(); } };
            card.Add(grid); sync.Add(delegate { grid.Value = getter(); });
            return grid;
        }
        private ColorField ColorPick(PremiumCard card, string title, Func<string> getter, Action<string> setter)
        {
            ColorField field = new ColorField(title, getter());
            field.ColorPicked += delegate(string hex) { if (!refreshing) { setter(hex); CommitSettings(); } };
            card.Add(field); sync.Add(delegate { field.Value = getter(); });
            return field;
        }
        private Label FieldLabel(string text)
        {
            Label l = new Label(); l.Text = L.T(text); l.Font = PremiumPalette.Font(9.75f, 1); l.ForeColor = PremiumPalette.Text;
            l.BackColor = PremiumPalette.Surface; l.AutoSize = false; l.Height = S(24); l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }
        private Label StatusLabel(PremiumCard card, string text, Color color)
        {
            Label l = FieldLabel(text); l.Font = PremiumPalette.Font(9.25f, 0); l.ForeColor = color; l.Height = S(26);
            card.Add(l); return l;
        }
        private void Note(PremiumCard card, string text) { card.Add(new InfoNote(text)); }
        private void FilePick(PremiumCard card, string name, string filter, string icon, Action<string> set)
        {
            Button(card, name, delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Filter = filter;
                    if (dialog.ShowDialog(this) == DialogResult.OK) { set(dialog.FileName); CommitSettings(); }
                }
            }, ButtonKind.Secondary, icon);
        }
        private static string ColorHex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        // ---------- Pages ----------
        private void PageHome()
        {
            hero = new HeroBanner();
            workspace.Controls.Add(hero); extras.Add(hero);
            PremiumButton draw = new PremiumButton(L.T("Dessiner sur le Bureau"), ButtonKind.Primary, "pen");
            PremiumButton pause = new PremiumButton(L.T("Mettre en pause"), ButtonKind.Secondary, "pause");
            draw.Click += delegate { engine.DrawOnDesktop(); };
            pause.Click += delegate { engine.ToggleEnabled(); RefreshStates(); };
            hero.Controls.Add(draw); hero.Controls.Add(pause);
            hero.Resize += delegate
            {
                draw.SetBounds(S(28), hero.Height - S(28) - S(42), S(220), S(42));
                pause.SetBounds(draw.Right + S(12), draw.Top, S(184), S(42));
            };
            sync.Add(delegate
            {
                int active = 0;
                if (cfg.Ring) active++; if (cfg.Trail) active++; if (cfg.Clicks) active++;
                if (cfg.KeyDisplay) active++; if (cfg.Loupe) active++; if (cfg.Focus) active++;
                hero.Active = cfg.Enabled;
                hero.Title = cfg.Enabled ? L.T("Ton curseur, version studio.") : L.T("Effets en pause.");
                hero.Status = cfg.Enabled
                    ? L.Count(active, " module actif", " modules actifs") + "  ·  " + L.Count(engine.StrokeCount, " annotation", " annotations") + L.T(" à l'écran") + (engine.IsDrawing ? L.T("  ·  dessin en cours") : "")
                    : L.T("Rien ne s'affiche à l'écran. Réactive les effets en un clic.");
                pause.Text = cfg.Enabled ? L.T("Mettre en pause") : L.T("Réactiver les effets");
                pause.IconName = cfg.Enabled ? "pause" : "play";
                draw.Text = engine.IsDrawing ? L.T("Reprendre le dessin") : L.T("Dessiner sur le Bureau");
                hero.Invalidate();
            });

            PremiumCard modules = Card(L.T("Modules"), L.T("Active, coupe ou ouvre un module en un clic."), 2, "grid");
            FlowGrid grid = new FlowGrid(2, S(250));
            Module(grid, L.T("Halo"), "halo", 1, () => cfg.Ring, b => cfg.Ring = b, () => L.T(cfg.RingShape) + " · " + (cfg.RingRadius * 2) + " px");
            Module(grid, L.T("Traînée"), "trail", 2, () => cfg.Trail, b => cfg.Trail = b, () => L.T(cfg.TrailStyle) + " · " + L.T(cfg.TrailColorMode));
            Module(grid, L.T("Clics animés"), "click", 3, () => cfg.Clicks, b => cfg.Clicks = b, () => L.T(cfg.ClickStyle) + " · " + cfg.ClickDuration + " ms");
            Module(grid, L.T("Touches à l'écran"), "keyboard", 6, () => cfg.KeyDisplay, b => cfg.KeyDisplay = b, () => L.T("Style ") + L.T(cfg.KeyStyle));
            Module(grid, L.T("Loupe"), "loupe", 6, () => cfg.Loupe, b => cfg.Loupe = b, () => L.T("Grossissement ×") + cfg.LoupeMagnification);
            Module(grid, L.T("Fenêtre active"), "focus", 6, () => cfg.Focus, b => cfg.Focus = b, () => L.T("Le reste du Bureau est assombri"));
            modules.Add(grid);

            PremiumCard looks = Card(L.T("Styles instantanés"), L.T("Un profil complet en un clic."), 1, "sparkles");
            Preset(looks, "Minimal", L.T("Halo net, sans traînée"), delegate { cfg.Ring = true; cfg.RingNeon = false; cfg.Trail = false; cfg.Clicks = true; cfg.KeyDisplay = false; },
                PremiumPalette.Cyan, Color.FromArgb(80, 140, 200));
            Preset(looks, "Néon", L.T("Halo lumineux + traînée lumière"), delegate { cfg.Ring = true; cfg.RingNeon = true; cfg.Trail = true; cfg.TrailStyle = "Lumière"; cfg.TrailColorMode = "Dégradé"; },
                PremiumPalette.Cyan, PremiumPalette.Purple, PremiumPalette.Pink);
            Preset(looks, "Présentation", L.T("Clics + raccourcis affichés"), delegate { cfg.Ring = true; cfg.RingNeon = false; cfg.Trail = false; cfg.Clicks = true; cfg.KeyDisplay = true; },
                PremiumPalette.Amber, PremiumPalette.Green);
            Preset(looks, "Créatif", L.T("Étoiles arc-en-ciel"), delegate { cfg.Ring = true; cfg.Trail = true; cfg.TrailStyle = "Étoiles"; cfg.TrailColorMode = "Arc-en-ciel"; },
                Color.FromArgb(255, 90, 120), PremiumPalette.Amber, PremiumPalette.Green, Color.FromArgb(60, 170, 255), PremiumPalette.Purple);

            PremiumCard keys = Card(L.T("Raccourcis essentiels"), L.T("Utilisables depuis n'importe quelle application."), 1, "keyboard");
            keys.Add(new ShortcutList(
                new string[] { "Dessin : activer / arrêter", "Effets : pause / reprise", "Annuler le dernier trait", "Effacer les dessins", L.T("Recentrer le halo"), L.T("Secours boutons souris") },
                new string[][] { new[] { "Ctrl", "Alt", "D" }, new[] { "Ctrl", "Alt", "M" }, new[] { "Ctrl", "Alt", L.T("Retour") },
                    new[] { "Ctrl", "Alt", "C" }, new[] { "Ctrl", "Alt", "H" }, new[] { "Ctrl", "Alt", "R" } }));
        }
        private void Module(FlowGrid grid, string name, string icon, int target, Func<bool> getter, Action<bool> setter, Func<string> status)
        {
            ModuleTile tile = new ModuleTile(name, icon);
            tile.Checked = getter(); tile.Status = status();
            tile.Toggled += delegate(bool on) { setter(on); CommitSettings(); };
            tile.Open += delegate { Navigate(target, 0); };
            grid.Controls.Add(tile);
            sync.Add(delegate { tile.Checked = getter(); tile.Status = status(); tile.Invalidate(); });
        }
        private void Preset(PremiumCard card, string name, string desc, Action apply, params Color[] colors)
        {
            PresetTile tile = new PresetTile(name, desc, colors);
            tile.Click += delegate { apply(); cfg.ActivePreset = name; cfg.ActivePresetSignature = PresetFingerprint(); CommitSettings(); };
            card.Add(tile);
            sync.Add(delegate { tile.Selected = cfg.ActivePreset == name && cfg.ActivePresetSignature == PresetFingerprint(); });
        }
        private string PresetFingerprint()
        {
            var fields = typeof(Config).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            Array.Sort(fields, delegate(System.Reflection.FieldInfo a, System.Reflection.FieldInfo b) { return String.CompareOrdinal(a.Name, b.Name); });
            var parts = new System.Collections.Generic.List<string>();
            foreach (var field in fields)
                if (field.Name.StartsWith("Ring", StringComparison.Ordinal) || field.Name.StartsWith("Trail", StringComparison.Ordinal) ||
                    field.Name.StartsWith("Click", StringComparison.Ordinal) || field.Name.StartsWith("Key", StringComparison.Ordinal) ||
                    field.Name == "LeftColor" || field.Name == "RightColor" || field.Name == "CustomSound")
                    parts.Add(field.Name + "=" + Convert.ToString(field.GetValue(cfg), System.Globalization.CultureInfo.InvariantCulture));
            return String.Join("|", parts.ToArray());
        }

        private void PageHalo()
        {
            MasterToggle(L.T("Halo activé"), () => cfg.Ring, b => cfg.Ring = b);
            Sections(new string[] { L.T("Apparence"), L.T("Comportement") }, new string[] { "palette", "wand" });
            if (section == 0)
            {
                PremiumCard live = Card(L.T("Aperçu en direct"), L.T("Le rendu réel du halo, sans déplacer celui de ton écran."), 2, "eye");
                preview = new PremiumPreview(cfg, PreviewMode.Halo); live.Add(preview);
                Choices(live, null, new string[] { "texte", "pointeur" }, new string[] { L.T("Mode texte (curseur I)"), L.T("Mode pointeur") },
                    new string[] { "ibeam", "pointer" }, 2, false,
                    () => preview != null && preview.TextMode ? "texte" : "pointeur",
                    v => { if (preview != null) preview.TextMode = v == "texte"; });

                PremiumCard style = Card(L.T("Forme et taille"), L.T("La silhouette du halo."), 1, "layers");
                Choices(style, L.T("Forme"), new string[] { "Cercle", "Carré arrondi", "Ellipse", "Réticule" }, new string[] { "Cercle", L.T("Carré"), "Ellipse", "Réticule" },
                    new string[] { "ring", "rect", "ellipse", "crosshair" }, 4, true, () => cfg.RingShape, s => cfg.RingShape = s);
                Slider(style, L.T("Diamètre"), 16, 310, "px", () => cfg.RingRadius * 2, n => cfg.RingRadius = n / 2);
                Slider(style, L.T("Épaisseur"), 1, 16, "px", () => cfg.RingThickness, n => cfg.RingThickness = n);
                Slider(style, L.T("Opacité"), 10, 100, "%", () => (int)Math.Round(cfg.RingOpacity * 100 / 255.0), n => cfg.RingOpacity = (int)Math.Round(n * 2.55));

                PremiumCard colors = Card(L.T("Couleur et ambiance"), L.T("Roue chromatique, palette rapide et HEX précis."), 1, "palette");
                PremiumChoiceGrid ringModes = Choices(colors, L.T("Mode de couleur"), new string[] { "Unie", "Dégradé", "Arc-en-ciel", "Dynamique" }, null,
                    null, 2, false, () => cfg.RingColorMode, s => cfg.RingColorMode = s);
                sync.Add(delegate { ringModes.Swatches = new string[] { cfg.RingColor, cfg.RingColor + "|" + cfg.RingColor2, "rainbow", "#22D3EE|#F472B6" }; ringModes.Invalidate(); });
                ColorPick(colors, L.T("Couleur principale"), () => cfg.RingColor, s => cfg.RingColor = s);
                ColorField second = ColorPick(colors, L.T("Couleur secondaire"), () => cfg.RingColor2, s => cfg.RingColor2 = s);
                sync.Add(delegate { second.Enabled = cfg.RingColorMode == "Dégradé"; });

                PremiumCard light = Card(L.T("Lumière et animation"), L.T("Glow, néon et pulsation."), 1, "sparkles");
                Slider(light, L.T("Flou lumineux"), 0, 40, "px", () => cfg.RingGlowBlur, n => cfg.RingGlowBlur = n);
                Slider(light, L.T("Intensité du glow"), 0, 100, "%", () => cfg.RingGlowIntensity, n => cfg.RingGlowIntensity = n);
                Toggle(light, L.T("Halo néon"), L.T("Éclairage secondaire diffus"), () => cfg.RingNeon, b => cfg.RingNeon = b, "sparkles");
                Toggle(light, L.T("Pulsation"), L.T("Respiration douce du contour"), () => cfg.RingPulse, b => cfg.RingPulse = b, "waves");
                Slider(light, L.T("Vitesse de pulsation"), 1, 15, "", () => cfg.RingPulseSpeed, n => cfg.RingPulseSpeed = n);
            }
            else
            {
                PremiumCard behavior = Card(L.T("Comportement intelligent"), L.T("Disparaît au repos, revient au mouvement."), 1, "timer");
                Toggle(behavior, L.T("Disparition automatique"), L.T("Fondu après inactivité de la souris"), () => cfg.RingAutoHide, b => cfg.RingAutoHide = b, "eye");
                Slider(behavior, L.T("Délai d'inactivité"), 1, 15, "s", () => cfg.RingIdleSeconds, v => cfg.RingIdleSeconds = v);
                Slider(behavior, L.T("Durée du fondu"), 100, 1200, "ms", () => cfg.RingFadeMilliseconds, v => cfg.RingFadeMilliseconds = v);

                PremiumCard precision = Card(L.T("Curseur texte"), L.T("Recentrage précis et discret pendant la saisie."), 1, "ibeam");
                Toggle(precision, L.T("Centrer sur le curseur texte"), L.T("Ignore le décalage dans les champs"), () => cfg.RingCenterOnText,
                    b => { cfg.RingCenterOnText = b; if (!b) cfg.RingTextPrecision = false; }, "crosshair");
                Toggle(precision, L.T("Accent texte"), L.T("Contour fin et lumineux sur le I"), () => cfg.RingTextPrecision,
                    b => { cfg.RingTextPrecision = b; if (b) cfg.RingCenterOnText = true; }, "sparkles");
                Toggle(precision, L.T("Agrandir le halo sur texte"), L.T("Dégage la lecture et la sélection"), () => cfg.RingTextGrow, b => cfg.RingTextGrow = b, "ibeam");
                Slider(precision, L.T("Agrandissement"), 10, 250, "%", () => cfg.RingTextGrowPercent, n => cfg.RingTextGrowPercent = n);
                Slider(precision, L.T("Intensité de l'accent"), 0, 100, "%", () => cfg.RingTextFocusIntensity, n => cfg.RingTextFocusIntensity = n);
                Slider(precision, L.T("Contour plus fin"), 0, 80, "%", () => cfg.RingTextFocusThinness, n => cfg.RingTextFocusThinness = n);

                PremiumCard position = Card(L.T("Position du halo"), L.T("Décalage appliqué hors curseur texte."), 1, "move");
                Slider(position, L.T("Décalage horizontal"), -250, 250, "px", () => cfg.RingOffsetX, n => cfg.RingOffsetX = n);
                Slider(position, L.T("Décalage vertical"), -250, 250, "px", () => cfg.RingOffsetY, n => cfg.RingOffsetY = n);
                Button(position, L.T("Recentrer le halo"), delegate { cfg.RingOffsetX = cfg.RingOffsetY = 0; CommitSettings(); }, ButtonKind.Secondary, "crosshair");
                Note(position, L.T("Halo décalé hors de l'écran ? Ctrl + Alt + H le recentre depuis n'importe où."));
            }
        }
        private void PageTrails()
        {
            MasterToggle(L.T("Traînée activée"), () => cfg.Trail, b => cfg.Trail = b);
            Sections(new string[] { L.T("Apparence"), L.T("Mouvement et logo") }, new string[] { "palette", "trail" });
            if (section == 0)
            {
            PremiumCard live = Card(L.T("Aperçu en direct"), L.T("Survole la zone pour dessiner ta propre traînée."), 2, "eye");
            preview = new PremiumPreview(cfg, PreviewMode.Trail); live.Add(preview);

            PremiumCard look = Card(L.T("Style de traînée"), L.T("Huit signatures visuelles."), 1, "sparkles");
            Choices(look, null, new string[] { "Particules", "Ligne", "Points", "Pointillés", "Lumière", "Étoiles", "Kawaii", "Logo" }, null,
                new string[] { "sparkles", "line", "dots", "dash", "glow", "star", "heart", "image" }, 4, true, () => cfg.TrailStyle, s => cfg.TrailStyle = s);
            Slider(look, L.T("Épaisseur"), 1, 40, "px", () => cfg.TrailWidth, n => cfg.TrailWidth = n);
            Slider(look, L.T("Persistance"), 100, 2800, "ms", () => cfg.TrailLife, n => cfg.TrailLife = n);
            Slider(look, L.T("Opacité"), 6, 100, "%", () => (int)Math.Round(cfg.TrailOpacity * 100 / 255.0), n => cfg.TrailOpacity = (int)Math.Round(n * 2.55));

            PremiumCard colors = Card(L.T("Couleurs"), L.T("Unie, dégradé ou arc-en-ciel animé."), 1, "palette");
            PremiumChoiceGrid trailModes = Choices(colors, null, new string[] { "Unie", "Dégradé", "Arc-en-ciel" }, null, null, 3, false, () => cfg.TrailColorMode, s => cfg.TrailColorMode = s);
            sync.Add(delegate { trailModes.Swatches = new string[] { cfg.TrailColor, cfg.TrailColor + "|" + cfg.TrailColor2, "rainbow" }; trailModes.Invalidate(); });
            ColorPick(colors, L.T("Première couleur"), () => cfg.TrailColor, s => cfg.TrailColor = s);
            ColorField second = ColorPick(colors, L.T("Seconde couleur"), () => cfg.TrailColor2, s => cfg.TrailColor2 = s);
            sync.Add(delegate { second.Enabled = cfg.TrailColorMode == "Dégradé"; });
            }
            else
            {
            PremiumCard motion = Card(L.T("Mouvement souple"), L.T("Souplesse : Ligne, Pointillés et Lumière."), 1, "trail");
            Slider(motion, L.T("Densité"), 1, 5, "/5", () => cfg.TrailDensity, n => cfg.TrailDensity = n);
            Slider(motion, L.T("Espacement"), 2, 40, "px", () => cfg.TrailSpacing, n => cfg.TrailSpacing = n);
            Slider(motion, L.T("Dispersion"), 0, 90, "px", () => cfg.TrailSpread, n => cfg.TrailSpread = n);
            Slider(motion, L.T("Souplesse"), 0, 100, "%", () => cfg.TrailSmoothness, n => cfg.TrailSmoothness = n);
            Toggle(motion, L.T("Épaisseur dynamique"), L.T("Réagit à la vitesse de la souris"), () => cfg.TrailSpeedResponsive, b => cfg.TrailSpeedResponsive = b);

            PremiumCard logo = Card(L.T("Logo personnalisé"), L.T("Un PNG transparent dans ta traînée."), 1, "image");
            Label file = StatusLabel(logo, "", PremiumPalette.Subtle);
            sync.Add(delegate { file.Text = String.IsNullOrEmpty(cfg.TrailLogoPath) ? L.T("Aucun logo — une étoile est utilisée.") : L.T("Logo : ") + Path.GetFileName(cfg.TrailLogoPath); });
            PremiumButton import = new PremiumButton(L.T("Importer un PNG"), ButtonKind.Secondary, "upload");
            import.Click += delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Filter = L.T("Images PNG (*.png)|*.png");
                    if (dialog.ShowDialog(this) == DialogResult.OK) { cfg.TrailLogoPath = dialog.FileName; cfg.TrailStyle = "Logo"; cfg.Trail = true; CommitSettings(); }
                }
            };
            PremiumButton remove = new PremiumButton(L.T("Retirer"), ButtonKind.Ghost, "trash");
            remove.Click += delegate { cfg.TrailLogoPath = ""; CommitSettings(); };
            logo.Add(new ButtonRow(import, remove));
            Slider(logo, L.T("Taille du logo"), 10, 125, "px", () => cfg.TrailLogoSize, n => cfg.TrailLogoSize = n);
            Slider(logo, L.T("Un logo tous les"), 1, 12, "pts", () => cfg.TrailLogoEvery, n => cfg.TrailLogoEvery = n);
            Toggle(logo, L.T("Rotation du logo"), L.T("Une signature plus vivante"), () => cfg.TrailLogoRotate, b => cfg.TrailLogoRotate = b);
            }
        }
        private void PageClicks()
        {
            MasterToggle(L.T("Clics animés"), () => cfg.Clicks, b => cfg.Clicks = b);
            PremiumCard live = Card(L.T("Aperçu en direct"), L.T("Clique (gauche ou droit) dans la zone pour tester."), 2, "eye");
            preview = new PremiumPreview(cfg, PreviewMode.Clicks); live.Add(preview);

            PremiumCard visual = Card(L.T("Animation"), L.T("Lisible, sans bloquer les clics Windows."), 1, "waves");
            Choices(visual, null, new string[] { "Ondes", "Anneau simple", "Flash", "Étincelles" }, new string[] { "Ondes", "Anneau", "Flash", "Étincelles" },
                new string[] { "waves", "ring", "flash", "sparkles" }, 4, true, () => cfg.ClickStyle, s => cfg.ClickStyle = s);
            Slider(visual, L.T("Amplitude"), 15, 180, "px", () => cfg.ClickSize, n => cfg.ClickSize = n);
            Slider(visual, L.T("Durée"), 200, 1800, "ms", () => cfg.ClickDuration, n => cfg.ClickDuration = n);
            Toggle(visual, L.T("Détection automatique"), L.T("Animation déclenchée à chaque clic réel"), () => cfg.ClickOnMouse, b => cfg.ClickOnMouse = b);

            PremiumCard palette = Card(L.T("Couleurs"), L.T("Distingue le clic gauche du clic droit."), 1, "palette");
            ColorPick(palette, L.T("Clic gauche"), () => cfg.LeftColor, s => cfg.LeftColor = s);
            ColorPick(palette, L.T("Clic droit"), () => cfg.RightColor, s => cfg.RightColor = s);

            PremiumCard sound = Card(L.T("Sons"), L.T("Un retour sonore, désactivable à tout moment."), 1, "sound");
            Toggle(sound, L.T("Sons de clic"), L.T("Son Windows ou fichier WAV personnel"), () => cfg.ClickSounds, b => cfg.ClickSounds = b, "sound");
            Label file = StatusLabel(sound, "", PremiumPalette.Subtle);
            sync.Add(delegate { file.Text = String.IsNullOrEmpty(cfg.CustomSound) ? L.T("Son : Windows par défaut") : L.T("Son : ") + Path.GetFileName(cfg.CustomSound); });
            PremiumButton import = new PremiumButton(L.T("Importer un WAV"), ButtonKind.Secondary, "upload");
            import.Click += delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Filter = L.T("Audio WAV (*.wav)|*.wav");
                    if (dialog.ShowDialog(this) == DialogResult.OK) { cfg.CustomSound = dialog.FileName; CommitSettings(); }
                }
            };
            PremiumButton reset = new PremiumButton(L.T("Par défaut"), ButtonKind.Ghost, "restore");
            reset.Click += delegate { cfg.CustomSound = ""; CommitSettings(); };
            sound.Add(new ButtonRow(import, reset));
        }
        private void PageMouseButtons()
        {
            PremiumButton restoreAll = new PremiumButton(L.T("Tout restaurer"), ButtonKind.Secondary, "restore");
            restoreAll.Width = S(170);
            restoreAll.Click += delegate { cfg.ResetButtons(); CommitSettings(); };
            heading.Accessory = restoreAll;

            PremiumCard mapping = Card(L.T("Affectations"), L.T("Appui court et appui long : une action pour chacun. Le comportement Windows reste normal par défaut."), 2, "mouse");
            mouseDiagram = new MouseDiagram(); mouseDiagram.ActionOf = ButtonValue; mouseDiagram.LongActionOf = ButtonLongValue; mouseDiagram.Detected = currentDetected;
            mapping.Add(mouseDiagram);
            sync.Add(delegate { if (mouseDiagram != null) mouseDiagram.Invalidate(); });
            Toggle(mapping, L.T("Inverser X1 et X2 sur ma souris"),
                L.T("Adapte les libellés à ta souris, sans changer les actions actuelles."),
                () => cfg.SwapSideButtons, value => cfg.SwapSideButtons = value, "mouse");
            Note(mapping, L.T("Si tu as déjà échangé les actions pour compenser : active cette option. Les actions physiques restent identiques, et les menus X1/X2 affichent désormais leur véritable rôle. Cyan = bouton configuré ; vert = dernier bouton détecté."));
            string[] names = { L.T("Bouton gauche"), L.T("Bouton droit"), L.T("Molette (clic)"), L.T("Latéral X1 (arrière)"), L.T("Latéral X2 (avant)") };
            ActionTable table = new ActionTable(L.T("Bouton"), L.T("Appui court"), L.T("Appui long"));
            for (int i = 0; i < 5; i++)
            {
                int id = i;
                Label name = new Label(); name.Text = names[i]; name.Font = PremiumPalette.Font(9.75f, 1); name.ForeColor = PremiumPalette.Text;
                name.BackColor = PremiumPalette.Surface; name.TextAlign = ContentAlignment.MiddleLeft; name.AutoEllipsis = true;
                PremiumSelect shortSelect = new PremiumSelect(Config.ButtonActions, ButtonValue(id)); shortSelect.AccessibleName = names[i] + L.T(", appui court");
                shortSelect.SelectedChanged += delegate(string v) { if (!refreshing) { AssignButton(id, v); CommitSettings(); } };
                sync.Add(delegate { shortSelect.Value = ButtonValue(id); });
                if (id < 2)
                {
                    // Clics principaux : pas d'appui long (le clic serait retardé, le glisser-déposer cassé).
                    Label na = new Label(); na.Text = L.T("Indisponible sur les clics principaux"); na.Font = PremiumPalette.Font(8.75f, 0);
                    na.ForeColor = PremiumPalette.Faint; na.BackColor = PremiumPalette.Surface; na.TextAlign = ContentAlignment.MiddleLeft; na.AutoEllipsis = true;
                    table.AddRow(name, shortSelect, na);
                    continue;
                }
                PremiumSelect longSelect = new PremiumSelect(LongActions, ButtonLongValue(id) == "Normal" ? "Aucun" : ButtonLongValue(id)); longSelect.AccessibleName = names[i] + L.T(", appui long");
                longSelect.SelectedChanged += delegate(string v) { if (!refreshing) { AssignLong(id, v); CommitSettings(); } };
                sync.Add(delegate { longSelect.Value = ButtonLongValue(id) == "Normal" ? "Aucun" : ButtonLongValue(id); });
                table.AddRow(name, shortSelect, longSelect);
            }
            mapping.Add(table);
            Slider(mapping, L.T("Durée de l'appui long"), 250, 1200, "ms", () => cfg.LongPressMs, n => cfg.LongPressMs = n);
            Note(mapping, L.T("Un bouton qui a un appui long n'envoie son clic Windows qu'au relâchement. L'appui long est réservé à la molette, X1 et X2 ; gauche et droit restent toujours immédiats."));

            PremiumCard restore = Card(L.T("Détection et secours"), L.T("Identifie un bouton puis affecte-lui une action."), 1, "crosshair");
            detectedButton = StatusLabel(restore, L.T("Dernier bouton détecté : ") + L.T(currentDetected), PremiumPalette.Cyan);
            Button(restore, L.T("Détecter mon bouton"), delegate
            { detectedButton.Text = L.T("Appuie maintenant sur un bouton de la souris…"); engine.ArmButtonCapture(); }, ButtonKind.Primary, "crosshair");
            restore.Add(FieldLabel(L.T("Préréglages")));
            Button(restore, L.T("X1 dessin · X2 annuler"), delegate { AssignButton(3, "Dessin : activer / arrêter"); AssignButton(4, "Annuler le dernier trait"); AssignLong(3, "Aucun"); AssignLong(4, "Aucun"); CommitSettings(); }, ButtonKind.Secondary, "pen");
            Button(restore, L.T("X1 dessin · X2 outil suivant"), delegate { AssignButton(3, "Dessin : activer / arrêter"); AssignButton(4, "Outil de dessin suivant"); AssignLong(3, "Aucun"); AssignLong(4, "Aucun"); CommitSettings(); }, ButtonKind.Secondary, "wand");
            Button(restore, L.T("X2 outil suivant · appui long : précédent"), delegate { AssignButton(4, "Outil de dessin suivant"); AssignLong(4, "Outil de dessin précédent"); CommitSettings(); }, ButtonKind.Secondary, "wand");
            Button(restore, L.T("Restaurer les 5 boutons"), delegate { cfg.ResetButtons(); CommitSettings(); }, ButtonKind.Danger, "restore");
            restore.Add(new InfoNote(L.T("Bouton bloqué ? Ctrl + Alt + R rend immédiatement leur comportement Windows aux 5 boutons."), "info", PremiumPalette.Amber));
            restore.Add(new InfoNote(L.T("Un bouton sans réaction (souvent un bouton DPI) est géré par la souris elle-même : Windows ne le voit pas, Haloryva ne peut pas l'utiliser."), "info", PremiumPalette.Cyan));
            restore.Add(new InfoNote(L.T("Changer d'outil de dessin : affecte « Outil de dessin suivant » à X1, X2 ou la molette — son nom s'affiche quelques secondes en haut à gauche de l'écran."), "info", PremiumPalette.Cyan));
            Button(restore, L.T("Choisir les outils à faire défiler"), delegate { Navigate(5, 2); }, ButtonKind.Secondary, "pen");
        }
        private static readonly string[] LongActions = MakeLongActions();
        private static string[] MakeLongActions()
        {
            string[] a = (string[])Config.ButtonActions.Clone();
            a[0] = "Aucun";
            return a;
        }
        private string ButtonLongValue(int id)
        {
            return SideButtonMap.ReadAction(cfg, id, true);
        }
        private void AssignLong(int id, string s)
        {
            string v = s == "Aucun" ? "Normal" : s;
            if (id < 2 && v != "Normal" && MessageBox.Show(this,
                L.T("Un appui long sur un clic principal retarde ce clic jusqu'au relâchement : le glisser-déposer et la sélection à la souris ne fonctionneront plus avec ce bouton. Ctrl + Alt + R rétablit les boutons. Continuer ?"),
                L.T("Confirmer l'appui long"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            SideButtonMap.WriteAction(cfg, id, v, true);
        }
        private string ButtonValue(int id)
        {
            return SideButtonMap.ReadAction(cfg, id, false);
        }
        private void AssignButton(int id, string s)
        {
            if (id < 2 && s != "Normal" && MessageBox.Show(this,
                L.T("Ce réglage remplace un clic principal. Ctrl + Alt + R rétablit les boutons. Continuer ?"),
                L.T("Confirmer l'affectation"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            SideButtonMap.WriteAction(cfg, id, s, false);
        }
        // Dessin : outils visibles et réels, sans menu déroulant qui masque la page.
        private void PageDrawing()
        {
            PremiumButton go = new PremiumButton(L.T("Dessiner sur le Bureau"), ButtonKind.Primary, "pen"); go.Width = S(236);
            go.Click += delegate { if (engine.IsDrawing) engine.ToggleDrawing(); else engine.DrawOnDesktop(); RefreshStates(); };
            sync.Add(delegate { go.Text = engine.IsDrawing ? L.T("Arrêter le dessin") : L.T("Dessiner sur le Bureau"); go.IconName = engine.IsDrawing ? "x" : "pen"; });
            heading.Accessory = go;
            Sections(new string[] { L.T("Dessiner"), L.T("Réglages"), L.T("Défilement") }, new string[] { "pen", "settings", "mouse" });

            string[] names = DrawingTools.Names, captions = DrawingTools.Captions, icons = DrawingTools.Icons;
            if (section == 0)
            {
            PremiumCard tools = Card(L.T("Outils"), L.T("Choisis un outil, puis « Dessiner sur le Bureau ». Clic droit ou Échap : arrêter."), 2, "pen");
            Choices(tools, null, names, captions, icons, 5, true, () => cfg.AnnotationTool,
                v => { cfg.AnnotationTool = v; if (v == "Forme libre") cfg.DrawSmoothMode = "Formes assistées"; });
            Note(tools, L.T("Auto-formes : trace un cercle, un rectangle, un triangle ou une ligne à main levée puis relâche — la forme est régularisée (le tracé d'origine reste restaurable). Changer d'outil à la souris : page Boutons souris."));

            PremiumCard style = Card(L.T("Trait"), L.T("Couleur et épaisseur, indépendantes du viseur."), 1, "palette");
            Slider(style, L.T("Épaisseur"), 1, 24, "px", () => cfg.AnnotationThickness, n => cfg.AnnotationThickness = n);
            ColorPick(style, L.T("Couleur du trait"), () => cfg.AnnotationColor, c => cfg.AnnotationColor = c);

            PremiumCard history = Card(L.T("Historique"), L.T("Annule, rétablis, efface ou exporte."), 2, "layers");
            Label count = StatusLabel(history, "", PremiumPalette.Subtle);
            sync.Add(delegate
            {
                count.Text = L.Count(engine.StrokeCount, " trait", " traits") + L.T(" à l'écran") +
                    (engine.RedoCount > 0 ? " · " + L.Format("{0} à rétablir", engine.RedoCount) : "");
            });
            PremiumButton undo = new PremiumButton(L.T("Annuler"), ButtonKind.Secondary, "undo");
            PremiumButton redo = new PremiumButton(L.T("Rétablir"), ButtonKind.Secondary, "redo");
            undo.Click += delegate { engine.UndoDrawing(); RefreshStates(); };
            redo.Click += delegate { engine.RedoDrawing(); RefreshStates(); };
            PremiumButton clear = new PremiumButton(L.T("Tout effacer"), ButtonKind.Danger, "trash");
            PremiumButton export = new PremiumButton(L.T("Exporter PNG"), ButtonKind.Primary, "download");
            clear.Click += delegate { engine.ClearDrawings(); RefreshStates(); };
            export.Click += delegate { engine.ExportDrawings(); };
            history.Add(new ButtonRow(undo, redo, clear, export));
            Button(history, L.T("Restaurer le trait original"), delegate { engine.RestoreLastOriginalStroke(); }, ButtonKind.Ghost, "restore");
            sync.Add(delegate { undo.Enabled = engine.StrokeCount > 0; redo.Enabled = engine.RedoCount > 0; clear.Enabled = engine.StrokeCount > 0; });

            }
            else if (section == 1)
            {
            PremiumCard assist = Card("Formes assistées", L.T("Cercles, rectangles, triangles et lignes."), 1, "wand");
            Choices(assist, null, new string[] { "Désactivé", "Lissage léger", "Formes assistées" }, new string[] { L.T("Brut"), L.T("Lissage"), L.T("Auto-formes") },
                new string[] { "minus", "trail", "shapes" }, 3, false, () => cfg.DrawSmoothMode,
                v => { cfg.DrawSmoothMode = v; if (v != "Formes assistées" && cfg.AnnotationTool == "Forme libre") cfg.AnnotationTool = "Stylo"; });
            Slider(assist, L.T("Intensité du lissage"), 0, 100, "%", () => cfg.DrawSmoothStrength, n => cfg.DrawSmoothStrength = n);
            Note(assist, L.T("50 % : lissage net conseillé · 100 % : très fort. Appliqué au relâchement du clic ; les formes reconnues gardent leurs angles."));

            PremiumCard cursor = Card(L.T("Viseur de dessin"), L.T("Le pointeur affiché pendant que tu dessines."), 1, "crosshair");
            Choices(cursor, null, new string[] { "Croix + anneau", "Croix", "Anneau", "Point central" }, new string[] { L.T("Mixte"), "Croix", "Anneau", L.T("Point") },
                new string[] { "crosshair", "plus", "ring", "dot" }, 4, true, () => cfg.DrawCursorStyle, s => cfg.DrawCursorStyle = s);
            Toggle(cursor, L.T("Lier au stylo"), L.T("Reprend la couleur du trait"), () => cfg.DrawCursorLinkedToPen, b => cfg.DrawCursorLinkedToPen = b);
            ColorField cursorColor = ColorPick(cursor, L.T("Couleur du viseur"), () => cfg.DrawCursorColor, c => cfg.DrawCursorColor = c);
            sync.Add(delegate { cursorColor.Enabled = !cfg.DrawCursorLinkedToPen; });
            Slider(cursor, L.T("Diamètre"), 12, 48, "px", () => cfg.DrawCursorSize, n => cfg.DrawCursorSize = n);
            Slider(cursor, L.T("Épaisseur"), 1, 5, "px", () => cfg.DrawCursorThickness, n => cfg.DrawCursorThickness = n);
            Slider(cursor, L.T("Opacité"), 20, 100, "%", () => cfg.DrawCursorOpacity, n => cfg.DrawCursorOpacity = n);
            Toggle(cursor, L.T("Masquer pendant le tracé"), L.T("Le viseur revient au relâchement"), () => cfg.DrawCursorHideWhileDrawing, b => cfg.DrawCursorHideWhileDrawing = b);
            }
            else PageDrawingCycle();
        }
        private void PageDrawingCycle()
        {
            PremiumCard first = Card(L.T("Outils à faire défiler"), L.T("Garde au moins un outil activé."), 1, "mouse");
            PremiumCard second = Card(L.T("Autres outils"), L.T("Même sélection pour suivant et précédent."), 1, "pen");
            for (int i = 0; i < DrawingTools.Names.Length; i++)
            {
                string tool = DrawingTools.Names[i];
                PremiumSwitch sw = Toggle(i < 5 ? first : second, DrawingTools.Captions[i], null,
                    () => Array.IndexOf(cfg.DrawingToolCycle, tool) >= 0,
                    enabled =>
                    {
                        var chosen = new List<string>(cfg.DrawingToolCycle);
                        if (enabled) { if (!chosen.Contains(tool)) chosen.Add(tool); }
                        else if (chosen.Count > 1) chosen.Remove(tool);
                        cfg.DrawingToolCycle = chosen.ToArray();
                    });
                sw.AccessibleName = L.T("Inclure ") + L.T(DrawingTools.Captions[i]) + L.T(" dans le défilement");
                sync.Add(delegate
                {
                    sw.Enabled = !sw.Checked || cfg.DrawingToolCycle.Length > 1;
                    sw.AccessibleDescription = sw.Enabled ? L.T("Outil parcouru par les boutons suivant et précédent.") :
                        L.T("Au moins un outil doit rester sélectionné.");
                });
            }
            PremiumCard help = Card(L.T("Défilement par boutons"), L.T("Choix enregistré automatiquement."), 2, "info");
            Label count = StatusLabel(help, "", PremiumPalette.Cyan);
            sync.Add(delegate { count.Text = cfg.DrawingToolCycle.Length + (cfg.DrawingToolCycle.Length > 1 ?
                L.T(" outils dans le défilement") : L.T(" outil dans le défilement")); });
            Note(help, L.T("Les boutons parcourent uniquement les outils activés, dans l'ordre affiché : colonne de gauche puis colonne de droite. Avec deux outils, ils alternent entre les deux."));
            Note(help, L.T("Tous les outils restent accessibles dans l'onglet Dessiner. Le trait en cours conserve son outil ; le changement s'applique au suivant."));
            Button(help, L.T("Inclure tous les outils"), delegate
            { cfg.DrawingToolCycle = (string[])DrawingTools.Names.Clone(); CommitSettings(); }, ButtonKind.Secondary, "restore");
            Button(help, L.T("Configurer les boutons souris"), delegate { Navigate(4, 0); }, ButtonKind.Ghost, "mouse");
        }
        private void PageTools()
        {
            PremiumCard keys = Card(L.T("Touches à l'écran"), L.T("Affiche tes raccourcis pendant une démo."), 1, "keyboard");
            Toggle(keys, L.T("Afficher les raccourcis"), L.T("Uniquement les combinaisons Ctrl / Alt / Win"), () => cfg.KeyDisplay, b => cfg.KeyDisplay = b, "keyboard");
            Choices(keys, L.T("Style des touches"), new string[] { "Classique", "Verre", "Touches", "Néon", "Dégradé", "Minimal" }, null, null, 3, false,
                () => cfg.KeyStyle, s => cfg.KeyStyle = s);
            keys.Add(new InfoNote(L.T("Confidentialité : le texte tapé n'est jamais affiché, seulement les raccourcis."), "info", PremiumPalette.Green));

            PremiumCard lens = Card(L.T("Loupe"), L.T("Une loupe flottante à côté du pointeur."), 1, "loupe");
            Toggle(lens, L.T("Loupe manuelle"), L.T("Toujours visible près du pointeur"), () => cfg.Loupe, b => cfg.Loupe = b, "loupe");
            Slider(lens, L.T("Taille"), 95, 380, "px", () => cfg.LoupeSize, n => cfg.LoupeSize = n);
            Slider(lens, L.T("Grossissement"), 2, 5, "×", () => cfg.LoupeMagnification, n => cfg.LoupeMagnification = n);
            Toggle(lens, L.T("Zoom ponctuel au clic"), L.T("La loupe apparaît un instant à chaque clic"), () => cfg.ZoomOnClick, b => cfg.ZoomOnClick = b);

            PremiumCard focus = Card(L.T("Fenêtre active"), L.T("Attire l'attention sur l'application en cours."), 1, "focus");
            Toggle(focus, L.T("Assombrir le reste du Bureau"), L.T("Seule la fenêtre au premier plan reste claire"), () => cfg.Focus, b => cfg.Focus = b, "focus");
            Toggle(focus, L.T("Secouer pour retrouver"), L.T("Agite la souris : le halo grossit un instant"), () => cfg.Shake, b => cfg.Shake = b, "sparkles");

            PremiumCard shortcuts = Card(L.T("Raccourcis de présentation"), L.T("Bascule chaque outil sans ouvrir le Studio."), 1, "bolt");
            shortcuts.Add(new ShortcutList(
                new string[] { L.T("Loupe"), L.T("Fenêtre active"), L.T("Touches à l'écran"), L.T("Zoom au clic"), L.T("Onde de clic manuelle") },
                new string[][] { new[] { "Ctrl", "Alt", "L" }, new[] { "Ctrl", "Alt", "F" }, new[] { "Ctrl", "Alt", "K" },
                    new[] { "Ctrl", "Alt", "Z" }, new[] { "Ctrl", "Alt", "X" } }));
        }
        // Applique le thème du Studio (palette, contrôles, sélection) après un changement direct, un import ou une réinitialisation.
        private void RefreshTheme()
        {
            PremiumPalette.ApplyAccent(cfg.StudioAccent);
            BeginInvoke((Action)delegate { workspace.Rebuild(); header.Invalidate(); nav.Invalidate(true); Navigate(page, 0); });
        }
        private void PagePreferences()
        {
            PremiumCard language = Card(L.T("Langue de l'application"), L.T("Français, anglais ou espagnol. Application au prochain lancement."), 2, "settings");
            PremiumChoiceGrid languageChoice = Choices(language, L.T("Langue"),
                new string[] { "fr", "en", "es" }, new string[] { "Français", "English", "Español" },
                null, 3, false, () => cfg.UiLanguage, value => cfg.UiLanguage = value);
            languageChoice.Name = "UiLanguage";
            language.Add(new InfoNote(L.T("La langue choisie sera utilisée au prochain lancement de Haloryva.")));

            PremiumCard look = Card(L.T("Apparence du Studio"), L.T("Choisis l'ambiance de couleur de l'interface."), 1, "palette");
            PremiumChoiceGrid accents = Choices(look, null, PremiumPalette.AccentNames, null, null, 3, true, () => cfg.StudioAccent, s =>
            {
                cfg.StudioAccent = s; RefreshTheme();
            });
            accents.Swatches = PremiumPalette.AccentSwatches;
            look.Add(new InfoNote(L.T("Le thème change uniquement l'interface du Studio, pas les couleurs du halo ni des traînées.")));

            PremiumCard behavior = Card(L.T("Performance et démarrage"), L.T("Réglages locaux, aucune connexion externe."), 1, "bolt");
            var startup = new PremiumSwitch(L.T("Démarrage avec Windows"), L.T("Vérification du démarrage…"), false);
            startup.IconName = "power"; startup.Height = S(56); behavior.Add(startup);
            sync.Add(delegate
            {
                startup.Checked = engine.StartupStatus.EffectiveEnabled == true;
                startup.Enabled = engine.StartupStatus.CanChange && !engine.StartupBusy;
                startup.Description = engine.StartupBusy ? L.T("Vérification du démarrage…") :
                    engine.StartupStatus.MessageKey != null ? L.T(engine.StartupStatus.MessageKey) : L.T("Lance Haloryva à la connexion");
                startup.Invalidate();
            });
            startup.CheckedChanged += async delegate
            {
                if (refreshing || engine.StartupBusy) return;
                bool desired = startup.Checked;
                if (desired && PackageContext.IsPackaged && StartupSettings.HasLegacyEntry() &&
                    MessageBox.Show(this, L.T("Une entrée de démarrage classique existe. Vérifie que l'ancien démarrage est désactivé avant d'activer celui du Store. Continuer ?"),
                    "Haloryva Studio", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                { RefreshStates(); return; }
                await engine.RequestStartupChangeAsync(desired);
                if (!IsDisposed) RefreshStates();
            };
            Toggle(behavior, L.T("Mode fluide"), L.T("Effets allégés pour les PC modestes"), () => cfg.PerformanceMode, b => cfg.PerformanceMode = b, "bolt");
            Slider(behavior, L.T("Rafraîchissement cible"), 15, 60, L.T("ips"), () => cfg.FrameRate, n => cfg.FrameRate = n);

            PremiumCard profile = Card(L.T("Profils et sauvegardes"), L.T("Compatible avec tes anciens réglages JSON."), 1, "user");
            Button(profile, L.T("Exporter mon profil"), delegate
            {
                using (SaveFileDialog d = new SaveFileDialog())
                {
                    d.Filter = L.T("Profil JSON|*.json"); d.FileName = "Haloryva_profil.json";
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        try { ConfigStore.Export(cfg, d.FileName); }
                        catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("Export impossible"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                    }
                }
            }, ButtonKind.Primary, "download");
            Button(profile, L.T("Importer un profil"), delegate
            {
                using (OpenFileDialog d = new OpenFileDialog())
                {
                    d.Filter = L.T("Profil JSON|*.json");
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        try
                        {
                            if (MessageBox.Show(this, L.T("Les réglages actuels seront sauvegardés avant leur remplacement."),
                                "Haloryva Studio", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                            engine.ImportProfile(d.FileName); cfg = engine.Options; RefreshTheme();
                        }
                        catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("Import impossible"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                    }
                }
            }, ButtonKind.Secondary, "upload");
            if (PackageContext.IsPackaged)
                Button(profile, L.T("Reprendre mes réglages 2.7.0"), delegate
                {
                    try
                    {
                        if (ProfileMigration.OfferLegacy(this, true))
                        { engine.ReplaceOptions(ConfigStore.Load()); cfg = engine.Options; RefreshTheme(); }
                    }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("Import impossible"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }, ButtonKind.Secondary, "upload");
            Button(profile, L.T("Ouvrir le dossier des réglages"), delegate
            {
                try { Directory.CreateDirectory(ConfigStore.DirectoryPath); System.Diagnostics.Process.Start("explorer.exe", "\"" + ConfigStore.DirectoryPath + "\""); }
                catch { }
            }, ButtonKind.Ghost, "squares");
            Button(profile, L.T("Réinitialiser la configuration"), delegate
            {
                if (MessageBox.Show(this, L.T("Réinitialiser tous les réglages et affectations ?"), L.T("Confirmer"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                { engine.ReplaceOptions(new Config()); cfg = engine.Options; RefreshTheme(); }
            }, ButtonKind.Danger, "restore");

            PremiumCard info = Card(L.T("À propos"), L.T("Haloryva Studio · Windows 64 bits"), 2, "info");
            info.Add(new AboutBlock());
            Button(info, L.T("Copier les informations de version"), delegate
            {
                try { Clipboard.SetText(Brand.VersionLine + L.T("\r\nSystème : ") + Environment.OSVersion + L.T("\r\n.NET : ") + Environment.Version); } catch { }
            }, ButtonKind.Secondary, "export");
            info.Add(new InfoNote(L.T("Ctrl + 1 à 8 : changer de page · Échap : fermer le Studio (Haloryva reste actif près de l'horloge).")));
        }
    }
}
