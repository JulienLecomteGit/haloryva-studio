using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CursorFlow
{
    // Un parent qui sait peindre son propre fond derrière un enfant (coins arrondis
    // sans rectangles parasites, lueurs de l'espace de travail continues).
    internal interface IBackdrop { void PaintBackdrop(Graphics g, Control child); }

    internal abstract class StudioControl : Control
    {
        protected bool hover, pressed;
        protected StudioControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { pressed = true; if (CanSelect && TabStop) Focus(); Invalidate(); }
            base.OnMouseDown(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaintBackground(PaintEventArgs e) { PaintParent(this, e.Graphics); }
        internal static void PaintParent(Control c, Graphics g)
        {
            IBackdrop backdrop = c.Parent as IBackdrop;
            if (backdrop != null) { backdrop.PaintBackdrop(g, c); return; }
            g.Clear(PremiumPalette.BackOf(c));
        }
        protected bool KeyboardFocus { get { return Focused && ShowFocusCues; } }
        protected void FocusRing(Graphics g, Rectangle r, int radius)
        {
            if (!KeyboardFocus) return;
            using (GraphicsPath path = PremiumPalette.Round(r, radius))
            using (Pen pen = new Pen(PremiumPalette.Alpha(PremiumPalette.Cyan, 200), PremiumPalette.Sf(1.6f))) g.DrawPath(pen, path);
        }
        // Interrupteur partagé (switchs, tuiles du tableau de bord).
        internal static void DrawToggle(Graphics g, Rectangle track, float knob, bool hot, bool enabled)
        {
            using (GraphicsPath path = PremiumPalette.Round(track, track.Height / 2))
            {
                using (SolidBrush off = new SolidBrush(hot ? Color.FromArgb(46, 58, 98) : Color.FromArgb(36, 47, 84))) g.FillPath(off, path);
                if (knob > 0.01f)
                    using (LinearGradientBrush on = new LinearGradientBrush(track, PremiumPalette.Alpha(PremiumPalette.Cyan, (int)(255 * knob)),
                        PremiumPalette.Alpha(PremiumPalette.Purple, (int)(255 * knob)), 0f)) g.FillPath(on, path);
                using (Pen edge = new Pen(Color.FromArgb(hot ? 46 : 26, 255, 255, 255), 1f)) g.DrawPath(edge, path);
            }
            int pad = Math.Max(2, track.Height / 8);
            int d = track.Height - pad * 2;
            float x = track.X + pad + knob * (track.Width - d - pad * 2);
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 10))) g.FillEllipse(shadow, x, track.Y + pad + 1.5f, d, d);
            using (SolidBrush b = new SolidBrush(enabled ? Color.White : Color.FromArgb(150, 160, 185))) g.FillEllipse(b, x, track.Y + pad, d, d);
        }
    }

    internal enum ButtonKind { Secondary, Primary, Ghost, Danger, WindowClose }

    internal sealed class PremiumButton : StudioControl
    {
        private bool selected;
        private ButtonKind kind;
        private string icon;
        internal bool Selected { get { return selected; } set { if (selected != value) { selected = value; Invalidate(); } } }
        internal bool Primary { get { return kind == ButtonKind.Primary; } set { kind = value ? ButtonKind.Primary : ButtonKind.Secondary; Invalidate(); } }
        internal ButtonKind Kind { get { return kind; } set { kind = value; Invalidate(); } }
        internal string IconName { get { return icon; } set { icon = value; Invalidate(); } }
        public PremiumButton(string label, bool isPrimary) : this(label, isPrimary ? ButtonKind.Primary : ButtonKind.Secondary, null) { }
        public PremiumButton(string label, ButtonKind buttonKind, string iconName)
        {
            Text = label; kind = buttonKind; icon = iconName; Height = PremiumPalette.S(40);
            AccessibleRole = AccessibleRole.PushButton; AccessibleName = label;
            Cursor = Cursors.Hand; TabStop = true; Font = PremiumPalette.Font(9.75f, 1);
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
        }
        protected override void OnTextChanged(EventArgs e) { AccessibleName = Text; Invalidate(); base.OnTextChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            else base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            Rectangle r = new Rectangle(1, 1, Math.Max(2, Width - 3), Math.Max(2, Height - 3));
            int radius = kind == ButtonKind.WindowClose || kind == ButtonKind.Ghost ? PremiumPalette.S(9) : PremiumPalette.S(11);
            Color text = PremiumPalette.Text, glyph = PremiumPalette.Text;
            using (GraphicsPath path = PremiumPalette.Round(r, radius))
            {
                if (kind == ButtonKind.Primary)
                {
                    Color a = pressed ? Color.FromArgb(20, 160, 190) : hover ? Color.FromArgb(64, 224, 246) : PremiumPalette.Cyan;
                    Color b = pressed ? Color.FromArgb(112, 76, 214) : hover ? Color.FromArgb(170, 132, 255) : PremiumPalette.Purple;
                    if (hover && !pressed)
                        using (Pen halo = new Pen(PremiumPalette.Alpha(PremiumPalette.Cyan, 60), PremiumPalette.Sf(3))) g.DrawPath(halo, path);
                    using (LinearGradientBrush fill = new LinearGradientBrush(r, a, b, 0f)) g.FillPath(fill, path);
                    using (Pen sheen = new Pen(Color.FromArgb(70, 255, 255, 255), 1f))
                        g.DrawLine(sheen, r.X + radius, r.Y + 1, r.Right - radius, r.Y + 1);
                    text = glyph = Color.White;
                }
                else if (selected)
                {
                    using (SolidBrush fill = new SolidBrush(PremiumPalette.Mix(PremiumPalette.Field, PremiumPalette.Cyan, hover ? .2 : .14))) g.FillPath(fill, path);
                    using (LinearGradientBrush border = new LinearGradientBrush(r, PremiumPalette.Cyan, PremiumPalette.Purple, 0f))
                    using (Pen pen = new Pen(border, PremiumPalette.Sf(1.5f))) g.DrawPath(pen, path);
                    glyph = PremiumPalette.Cyan;
                }
                else if (kind == ButtonKind.Ghost || kind == ButtonKind.WindowClose)
                {
                    if (hover || pressed)
                    {
                        Color fill = kind == ButtonKind.WindowClose ? Color.FromArgb(pressed ? 190 : 225, 214, 54, 72) :
                            pressed ? PremiumPalette.Field : PremiumPalette.FieldHover;
                        using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                    }
                    text = glyph = hover ? Color.White : PremiumPalette.Subtle;
                }
                else
                {
                    bool danger = kind == ButtonKind.Danger;
                    Color fill = pressed ? PremiumPalette.Field : hover ? (danger ? PremiumPalette.Mix(PremiumPalette.Field, PremiumPalette.Red, .16) : PremiumPalette.FieldHover) : PremiumPalette.Field;
                    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                    using (Pen pen = new Pen(hover ? (danger ? PremiumPalette.Alpha(PremiumPalette.Red, 150) : PremiumPalette.BorderStrong) : PremiumPalette.Border, 1f)) g.DrawPath(pen, path);
                    if (danger) text = glyph = PremiumPalette.Red;
                    else glyph = hover ? PremiumPalette.Cyan : PremiumPalette.Subtle;
                }
            }
            if (!Enabled) text = glyph = PremiumPalette.Faint;
            int iconSize = PremiumPalette.S(Height >= PremiumPalette.S(40) ? 17 : 15);
            bool hasIcon = !String.IsNullOrEmpty(icon);
            bool hasText = !String.IsNullOrEmpty(Text);
            int textWidth = hasText ? Math.Min(Width - PremiumPalette.S(24), PremiumPalette.TextWidth(Text, Font)) : 0;
            int gap = hasIcon && hasText ? PremiumPalette.S(8) : 0;
            int total = (hasIcon ? iconSize : 0) + gap + textWidth;
            int x = Math.Max(PremiumPalette.S(10), (Width - total) / 2);
            if (hasIcon)
            {
                StudioIcons.Draw(g, icon, new RectangleF(x, (Height - iconSize) / 2f, iconSize, iconSize), glyph, 1.9f);
                x += iconSize + gap;
            }
            if (hasText)
                PremiumPalette.TextAt(g, Text, Font, text, new Rectangle(x, 0, Math.Max(4, Width - x - PremiumPalette.S(8)), Height), ContentAlignment.MiddleLeft);
            FocusRing(g, r, radius);
        }
    }

    // Navigation latérale : icône vectorielle + libellé, état actif lumineux,
    // pastille d'état du module (allumé / éteint) et mode replié.
    internal sealed class PremiumNavButton : StudioControl
    {
        private bool selected, collapsed, indicator;
        private readonly string icon;
        private readonly ToolTip tip = new ToolTip();
        internal bool Selected { get { return selected; } set { if (selected != value) { selected = value; Invalidate(); } } }
        internal bool Collapsed { get { return collapsed; } set { collapsed = value; tip.SetToolTip(this, value ? Text : ""); Invalidate(); } }
        internal bool Indicator { get { return indicator; } set { if (indicator != value) { indicator = value; Invalidate(); } } }
        internal Color IndicatorColor = PremiumPalette.Green;
        public PremiumNavButton(string label) : this(label, "dot") { }
        public PremiumNavButton(string label, string iconName)
        {
            Text = label; icon = iconName; Height = PremiumPalette.S(42); Cursor = Cursors.Hand; TabStop = true;
            AccessibleRole = AccessibleRole.PageTab; AccessibleName = label;
            Font = PremiumPalette.Font(10f, 1);
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
            Disposed += delegate { tip.Dispose(); };
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            else base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            Rectangle r = new Rectangle(0, 1, Width - 1, Height - 3);
            int radius = PremiumPalette.S(10);
            if (selected)
            {
                using (GraphicsPath path = PremiumPalette.Round(r, radius))
                using (LinearGradientBrush fill = new LinearGradientBrush(r, PremiumPalette.Alpha(PremiumPalette.Cyan, 40),
                    PremiumPalette.Alpha(PremiumPalette.Purple, 14), 0f))
                {
                    g.FillPath(fill, path);
                    using (Pen edge = new Pen(PremiumPalette.Alpha(PremiumPalette.Cyan, 46), 1f)) g.DrawPath(edge, path);
                }
                Rectangle bar = new Rectangle(0, r.Y + PremiumPalette.S(10), PremiumPalette.S(3), Math.Max(4, r.Height - PremiumPalette.S(20)));
                using (LinearGradientBrush accent = new LinearGradientBrush(new Rectangle(bar.X, bar.Y - 1, bar.Width, bar.Height + 2),
                    PremiumPalette.Cyan, PremiumPalette.Purple, 90f))
                using (GraphicsPath p = PremiumPalette.Round(bar, PremiumPalette.S(2))) g.FillPath(accent, p);
            }
            else if (hover)
            {
                using (GraphicsPath path = PremiumPalette.Round(r, radius))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(12, 255, 255, 255))) g.FillPath(fill, path);
            }
            int iconSize = PremiumPalette.S(19);
            Color ink = selected ? PremiumPalette.Text : hover ? PremiumPalette.Text : PremiumPalette.Subtle;
            Color glyph = selected ? PremiumPalette.Cyan : hover ? PremiumPalette.Text : PremiumPalette.Faint;
            int ix = collapsed ? (Width - iconSize) / 2 : PremiumPalette.S(16);
            if (selected)
                using (GraphicsPath gp = new GraphicsPath())
                {
                    float gr = iconSize * 1.25f, gx = ix + iconSize / 2f, gy = Height / 2f;
                    gp.AddEllipse(gx - gr, gy - gr, gr * 2, gr * 2);
                    using (PathGradientBrush glow = new PathGradientBrush(gp))
                    { glow.CenterColor = PremiumPalette.Alpha(PremiumPalette.Cyan, 70); glow.SurroundColors = new Color[] { PremiumPalette.Alpha(PremiumPalette.Cyan, 0) }; g.FillPath(glow, gp); }
                }
            StudioIcons.Draw(g, icon, new RectangleF(ix, (Height - iconSize) / 2f, iconSize, iconSize), glyph, 1.8f);
            if (!collapsed)
                PremiumPalette.TextAt(g, Text, Font, ink, new Rectangle(ix + iconSize + PremiumPalette.S(13), 0,
                    Math.Max(4, Width - ix - iconSize - PremiumPalette.S(40)), Height), ContentAlignment.MiddleLeft);
            if (indicator)
            {
                int d = PremiumPalette.S(7);
                float dx = collapsed ? ix + iconSize - d / 2f : Width - PremiumPalette.S(20);
                float dy = collapsed ? (Height - iconSize) / 2f - d / 3f : (Height - d) / 2f;
                using (SolidBrush glow = new SolidBrush(PremiumPalette.Alpha(IndicatorColor, 60))) g.FillEllipse(glow, dx - d / 2f, dy - d / 2f, d * 2, d * 2);
                using (SolidBrush dot = new SolidBrush(IndicatorColor)) g.FillEllipse(dot, dx, dy, d, d);
            }
            FocusRing(g, r, radius);
        }
    }

    internal sealed class PremiumSwitch : StudioControl
    {
        private bool checkedValue;
        private float knob;
        private readonly Timer animation = new Timer();
        public event EventHandler CheckedChanged;
        internal string Description { get; set; }
        internal string IconName { get; set; }
        // Compact : libellé à gauche de l'interrupteur, sans description (en-têtes de page).
        internal bool Compact { get; set; }
        internal bool Checked
        {
            get { return checkedValue; }
            set
            {
                if (checkedValue == value) return;
                checkedValue = value;
                if (IsHandleCreated && Visible) animation.Start(); else { knob = value ? 1f : 0f; Invalidate(); }
            }
        }
        public PremiumSwitch(string label, string description, bool initial)
        {
            Text = label; Description = description; checkedValue = initial; knob = initial ? 1f : 0f;
            Height = String.IsNullOrEmpty(description) ? PremiumPalette.S(40) : PremiumPalette.S(54);
            Font = PremiumPalette.Font(9.75f, 1); Cursor = Cursors.Hand; TabStop = true;
            AccessibleRole = AccessibleRole.CheckButton; AccessibleName = label;
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
            animation.Interval = 15;
            animation.Tick += delegate
            {
                float target = checkedValue ? 1f : 0f;
                knob += (target - knob) * .38f;
                if (Math.Abs(target - knob) < .02f) { knob = target; animation.Stop(); }
                Invalidate();
            };
            Disposed += delegate { animation.Dispose(); };
        }
        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return;
            Checked = !Checked; if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            base.OnClick(e);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            else base.OnKeyDown(e);
        }
        internal int PreferredCompactWidth
        {
            get { return PremiumPalette.TextWidth(Text, Font) + PremiumPalette.S(66); }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            int tw = PremiumPalette.S(42), th = PremiumPalette.S(24);
            Rectangle track = new Rectangle(Width - tw - PremiumPalette.S(2), (Height - th) / 2, tw, th);
            DrawToggle(g, track, knob, hover, Enabled);
            Color ink = Enabled ? PremiumPalette.Text : PremiumPalette.Faint;
            if (Compact)
            {
                PremiumPalette.TextAt(g, Text, Font, ink, new Rectangle(0, 0, track.X - PremiumPalette.S(12), Height), ContentAlignment.MiddleRight);
                FocusRing(g, new Rectangle(track.X - 3, track.Y - 3, track.Width + 5, track.Height + 5), track.Height / 2 + 3);
                return;
            }
            int left = 0;
            if (!String.IsNullOrEmpty(IconName))
            {
                int d = PremiumPalette.S(32);
                PremiumPalette.IconBubble(g, new Rectangle(0, (Height - d) / 2, d, d), IconName, PremiumPalette.Cyan, PremiumPalette.Purple, false);
                left = d + PremiumPalette.S(12);
            }
            int textRight = track.X - PremiumPalette.S(12) - left;
            if (String.IsNullOrEmpty(Description))
                PremiumPalette.TextAt(g, Text, Font, ink, new Rectangle(left, 0, textRight, Height), ContentAlignment.MiddleLeft);
            else
            {
                int lh = PremiumPalette.S(20);
                int top = (Height - lh * 2) / 2;
                PremiumPalette.TextAt(g, Text, Font, ink, new Rectangle(left, top, textRight, lh), ContentAlignment.MiddleLeft);
                PremiumPalette.TextAt(g, Description, PremiumPalette.Font(8.75f, 0), PremiumPalette.Subtle,
                    new Rectangle(left, top + lh, textRight, lh), ContentAlignment.MiddleLeft);
            }
            FocusRing(g, new Rectangle(0, 0, Width - 1, Height - 1), PremiumPalette.S(8));
        }
    }

    internal sealed class PremiumSliderTrack : StudioControl
    {
        private int min, max, current;
        private bool dragging;
        internal event Action<int> Changed;
        public int Minimum { get { return min; } set { min = value; Invalidate(); } }
        public int Maximum { get { return max; } set { max = value; Invalidate(); } }
        internal int Value { get { return current; } set { int v = Math.Max(min, Math.Min(max, value)); if (v != current) { current = v; Invalidate(); } } }
        public PremiumSliderTrack(int low, int high, int value)
        {
            min = low; max = high; current = Math.Max(low, Math.Min(high, value));
            TabStop = true; Height = PremiumPalette.S(26); Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.Slider;
            SetStyle(ControlStyles.Selectable, true);
        }
        private int Pad { get { return PremiumPalette.S(10); } }
        private void SetFromPosition(int x)
        {
            int usable = Math.Max(1, Width - Pad * 2);
            int next = min + (int)Math.Round((max - min) * Math.Max(0.0, Math.Min(1.0, (x - Pad) / (double)usable)));
            if (next == current) return;
            Value = next;
            if (Changed != null) Changed(Value);
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { dragging = true; Capture = true; SetFromPosition(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromPosition(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; base.OnMouseUp(e); }
        private void Step(int delta) { int before = current; Value = current + delta; if (current != before && Changed != null) Changed(current); }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = e.KeyCode == Keys.PageUp || e.KeyCode == Keys.PageDown ? Math.Max(1, (max - min) / 10) : 1;
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down || e.KeyCode == Keys.PageDown) { Step(-step); e.Handled = true; }
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up || e.KeyCode == Keys.PageUp) { Step(step); e.Handled = true; }
            else if (e.KeyCode == Keys.Home) { Step(min - current); e.Handled = true; }
            else if (e.KeyCode == Keys.End) { Step(max - current); e.Handled = true; }
            else base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            int h = PremiumPalette.S(4), cy = Height / 2;
            Rectangle rail = new Rectangle(Pad, cy - h / 2, Math.Max(1, Width - Pad * 2), h);
            using (GraphicsPath path = PremiumPalette.Round(rail, h / 2))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(36, 47, 84))) g.FillPath(b, path);
            double t = (current - min) / (double)Math.Max(1, max - min);
            int knobX = Pad + (int)Math.Round(rail.Width * t);
            Rectangle active = new Rectangle(Pad, rail.Y, Math.Max(h, knobX - Pad), h);
            using (GraphicsPath path = PremiumPalette.Round(active, h / 2))
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(rail.X - 1, rail.Y, rail.Width + 2, h), PremiumPalette.Cyan, PremiumPalette.Purple, 0f))
                g.FillPath(b, path);
            int d = PremiumPalette.S(16);
            if (hover || dragging || KeyboardFocus)
            {
                int gd = PremiumPalette.S(28);
                using (SolidBrush glow = new SolidBrush(PremiumPalette.Alpha(PremiumPalette.Cyan, KeyboardFocus ? 70 : 45)))
                    g.FillEllipse(glow, knobX - gd / 2f, cy - gd / 2f, gd, gd);
            }
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 10))) g.FillEllipse(shadow, knobX - d / 2f, cy - d / 2f + 1.5f, d, d);
            using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, knobX - d / 2f, cy - d / 2f, d, d);
            using (SolidBrush core = new SolidBrush(PremiumPalette.Mix(PremiumPalette.Cyan, PremiumPalette.Purple, t)))
                g.FillEllipse(core, knobX - d / 5f, cy - d / 5f, d * .4f, d * .4f);
        }
    }

    internal sealed class PremiumSlider : UserControl
    {
        private readonly PremiumSliderTrack track;
        private readonly TextBox editor;
        private readonly string title, unit;
        private int low, high;
        internal event Action<int> ValueChanged;
        internal int Value
        {
            get { return track.Value; }
            set { track.Value = value; if (!editor.Focused) editor.Text = track.Value.ToString(); }
        }
        public PremiumSlider(string name, int min, int max, int value, string unitText)
        {
            title = name; unit = (unitText ?? "").Trim(); low = min; high = max;
            Height = PremiumPalette.S(60); BackColor = PremiumPalette.Surface;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
            editor = new TextBox(); editor.BorderStyle = BorderStyle.None; editor.TextAlign = HorizontalAlignment.Right;
            editor.BackColor = PremiumPalette.Field; editor.ForeColor = PremiumPalette.Text; editor.Font = PremiumPalette.Font(9.5f, 1);
            editor.Text = value.ToString(); editor.AccessibleName = name;
            track = new PremiumSliderTrack(min, max, value); track.AccessibleName = name;
            track.Changed += delegate(int n) { editor.Text = n.ToString(); if (ValueChanged != null) ValueChanged(n); };
            editor.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { Commit(); e.Handled = true; e.SuppressKeyPress = true; } };
            editor.Leave += delegate { Commit(); };
            Controls.Add(track); Controls.Add(editor);
            Resize += delegate { PositionControls(); }; PositionControls();
        }
        private void Commit()
        {
            int result;
            if (!int.TryParse(editor.Text.Trim(), out result)) { editor.Text = track.Value.ToString(); return; }
            int v = Math.Max(low, Math.Min(high, result));
            editor.Text = v.ToString();
            if (v != track.Value) { track.Value = v; if (ValueChanged != null) ValueChanged(v); }
        }
        private Rectangle Pill
        {
            get
            {
                int w = PremiumPalette.S(unit.Length > 1 ? 76 : 64);
                return new Rectangle(Width - w - PremiumPalette.S(2), 0, w, PremiumPalette.S(26));
            }
        }
        private int UnitWidth { get { return unit.Length == 0 ? 0 : PremiumPalette.TextWidth(unit, PremiumPalette.Font(8.5f, 0)) + PremiumPalette.S(4); } }
        private void PositionControls()
        {
            Rectangle pill = Pill;
            int uw = UnitWidth;
            editor.Width = Math.Max(PremiumPalette.S(20), pill.Width - uw - PremiumPalette.S(14));
            editor.Location = new Point(pill.X + PremiumPalette.S(7), pill.Y + Math.Max(0, (pill.Height - editor.Height) / 2));
            track.SetBounds(0, PremiumPalette.S(30), Width, PremiumPalette.S(28));
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            Rectangle pill = Pill;
            PremiumPalette.TextAt(g, title, PremiumPalette.Font(9.75f, 1), Enabled ? PremiumPalette.Text : PremiumPalette.Faint,
                new Rectangle(PremiumPalette.S(2), 0, Math.Max(10, pill.X - PremiumPalette.S(10)), pill.Height), ContentAlignment.MiddleLeft);
            using (GraphicsPath path = PremiumPalette.Round(pill, PremiumPalette.S(8)))
            using (SolidBrush b = new SolidBrush(PremiumPalette.Field))
            using (Pen edge = new Pen(editor.Focused ? PremiumPalette.Alpha(PremiumPalette.Cyan, 170) : PremiumPalette.Border, 1f))
            { g.FillPath(b, path); g.DrawPath(edge, path); }
            if (unit.Length > 0)
                PremiumPalette.TextAt(g, unit, PremiumPalette.Font(8.5f, 0), PremiumPalette.Faint,
                    new Rectangle(pill.Right - UnitWidth - PremiumPalette.S(6), pill.Y, UnitWidth + PremiumPalette.S(2), pill.Height), ContentAlignment.MiddleLeft);
        }
    }

    // Liste peinte : pas de barres de défilement Windows ni de menu natif.
    internal sealed class PremiumChoicePopup : StudioControl
    {
        private readonly string[] choices;
        private readonly string selected;
        private readonly int visibleRows;
        private int offset, highlighted;
        internal event Action<string> Picked;
        internal event Action Canceled;
        private int RowHeight { get { return PremiumPalette.S(38); } }
        private int Pad { get { return PremiumPalette.S(6); } }
        public PremiumChoicePopup(string[] values, string active, int rows)
        {
            choices = values; selected = active; visibleRows = Math.Max(1, Math.Min(rows, values.Length));
            highlighted = Array.IndexOf(choices, active);
            offset = Math.Max(0, Math.Min(choices.Length - visibleRows, highlighted - visibleRows / 2));
            Height = Pad * 2 + visibleRows * RowHeight;
            Font = PremiumPalette.Font(9.75f, 0); Cursor = Cursors.Hand; TabStop = true;
            AccessibleRole = AccessibleRole.List;
            SetStyle(ControlStyles.Selectable, true);
        }
        private void ScrollBy(int n) { offset = Math.Max(0, Math.Min(choices.Length - visibleRows, offset + n)); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { ScrollBy(e.Delta > 0 ? -2 : 2); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int line = (e.Y - Pad) / RowHeight;
            int next = e.Y >= Pad && line >= 0 && line < visibleRows ? offset + line : -1;
            if (next != highlighted) { highlighted = next; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            int row = (e.Y - Pad) / RowHeight, index = offset + row;
            if (e.Y >= Pad && row < visibleRows && index >= 0 && index < choices.Length && Picked != null) Picked(choices[index]);
        }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            return k == Keys.Up || k == Keys.Down || k == Keys.Escape || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { if (Canceled != null) Canceled(); e.Handled = true; }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                highlighted = Math.Max(0, Math.Min(choices.Length - 1, highlighted + (e.KeyCode == Keys.Down ? 1 : -1)));
                if (highlighted < offset) ScrollBy(highlighted - offset);
                if (highlighted >= offset + visibleRows) ScrollBy(highlighted - offset - visibleRows + 1);
                Invalidate(); e.Handled = true;
            }
            else if ((e.KeyCode == Keys.Return || e.KeyCode == Keys.Space) && highlighted >= 0)
            { if (Picked != null) Picked(choices[highlighted]); e.Handled = true; }
            else base.OnKeyDown(e);
        }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); if (Canceled != null) Canceled(); }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(PremiumPalette.BackOf(this)); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            Rectangle back = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath shape = PremiumPalette.Round(back, PremiumPalette.S(12)))
            using (SolidBrush b = new SolidBrush(PremiumPalette.SurfaceHigh))
            using (Pen edge = new Pen(PremiumPalette.BorderStrong, 1f)) { g.FillPath(b, shape); g.DrawPath(edge, shape); }
            int rightSpace = choices.Length > visibleRows ? PremiumPalette.S(18) : Pad;
            for (int row = 0; row < visibleRows; row++)
            {
                int index = offset + row; if (index >= choices.Length) break;
                Rectangle r = new Rectangle(Pad, Pad + row * RowHeight, Math.Max(8, Width - rightSpace - Pad), RowHeight - PremiumPalette.S(2));
                bool chosen = choices[index] == selected, hot = index == highlighted;
                if (chosen || hot)
                    using (GraphicsPath p = PremiumPalette.Round(r, PremiumPalette.S(8)))
                    using (SolidBrush bg = new SolidBrush(chosen ? PremiumPalette.Mix(PremiumPalette.Field, PremiumPalette.Cyan, .16) : PremiumPalette.FieldHover)) g.FillPath(bg, p);
                PremiumPalette.TextAt(g, choices[index], chosen ? PremiumPalette.Font(9.75f, 1) : Font, chosen || hot ? PremiumPalette.Text : PremiumPalette.Subtle,
                    new Rectangle(r.Left + PremiumPalette.S(12), r.Top, r.Width - PremiumPalette.S(40), r.Height), ContentAlignment.MiddleLeft);
                if (chosen)
                {
                    int s = PremiumPalette.S(16);
                    StudioIcons.Draw(g, "check", new RectangleF(r.Right - s - PremiumPalette.S(10), r.Top + (r.Height - s) / 2f, s, s), PremiumPalette.Cyan, 2.2f);
                }
            }
            if (choices.Length > visibleRows)
            {
                Rectangle track = new Rectangle(Width - PremiumPalette.S(11), PremiumPalette.S(10), PremiumPalette.S(4), Height - PremiumPalette.S(20));
                using (GraphicsPath shape = PremiumPalette.Round(track, PremiumPalette.S(2)))
                using (SolidBrush bg = new SolidBrush(PremiumPalette.Border)) g.FillPath(bg, shape);
                int thumbH = Math.Max(PremiumPalette.S(20), (int)Math.Round(track.Height * visibleRows / (double)choices.Length));
                int y = track.Y + (int)Math.Round((track.Height - thumbH) * offset / (double)Math.Max(1, choices.Length - visibleRows));
                using (GraphicsPath thumb = PremiumPalette.Round(new Rectangle(track.X, y, track.Width, thumbH), PremiumPalette.S(2)))
                using (SolidBrush fg = new SolidBrush(PremiumPalette.Cyan)) g.FillPath(fg, thumb);
            }
        }
    }

    // Liste déroulante : popup enfant de la fenêtre (pas de ContextMenuStrip natif).
    internal sealed class PremiumSelect : StudioControl
    {
        private static PremiumSelect active;
        internal static void CloseActive() { if (active != null) active.CloseMenu(); }
        internal static bool IsOpen { get { return active != null && active.menu != null; } }
        private readonly string[] options;
        private string value;
        private PremiumChoicePopup menu;
        internal event Action<string> SelectedChanged;
        internal string Value { get { return value; } set { if (this.value != value) { this.value = value; Invalidate(); } } }
        public PremiumSelect(string[] choices, string initial)
        {
            options = choices; value = initial; Height = PremiumPalette.S(40); TabStop = true; Cursor = Cursors.Hand;
            Font = PremiumPalette.Font(9.75f, 0); AccessibleRole = AccessibleRole.ComboBox;
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
            Disposed += delegate { CloseMenu(); };
        }
        internal void CloseMenu()
        {
            if (menu != null) { PremiumChoicePopup p = menu; menu = null; if (!p.IsDisposed) { p.Hide(); p.Dispose(); } Invalidate(); }
            if (active == this) active = null;
        }
        private void Choose(string v)
        {
            Value = v; CloseMenu(); Focus();
            if (SelectedChanged != null) SelectedChanged(v);
        }
        protected override void OnClick(EventArgs e) { Toggle(); base.OnClick(e); }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            return k == Keys.Up || k == Keys.Down || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { Toggle(); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { CloseMenu(); e.Handled = true; }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                int i = Math.Max(0, Math.Min(options.Length - 1, Array.IndexOf(options, Value) + (e.KeyCode == Keys.Down ? 1 : -1)));
                if (options[i] != Value) Choose(options[i]);
                e.Handled = true;
            }
            else base.OnKeyDown(e);
        }
        private void Toggle()
        {
            if (menu != null) { CloseMenu(); return; }
            Form form = FindForm(); if (form == null || options.Length == 0) return;
            if (active != null && active != this) active.CloseMenu();
            active = this;
            menu = new PremiumChoicePopup(options, Value, Math.Min(7, options.Length));
            menu.Picked += delegate(string choice) { Choose(choice); };
            menu.Canceled += delegate { CloseMenu(); };
            menu.Width = Math.Max(PremiumPalette.S(220), Width);
            Point at = form.PointToClient(PointToScreen(new Point(0, Height + PremiumPalette.S(4))));
            if (at.Y + menu.Height > form.ClientSize.Height - 8)
                at.Y = form.PointToClient(PointToScreen(new Point(0, -menu.Height - PremiumPalette.S(4)))).Y;
            at.X = Math.Max(8, Math.Min(form.ClientSize.Width - menu.Width - 8, at.X));
            at.Y = Math.Max(8, Math.Min(form.ClientSize.Height - menu.Height - 8, at.Y));
            menu.Location = at;
            form.Controls.Add(menu); menu.BringToFront(); menu.Focus();
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            Rectangle r = new Rectangle(1, 1, Math.Max(3, Width - 3), Math.Max(3, Height - 3));
            bool open = menu != null;
            using (GraphicsPath p = PremiumPalette.Round(r, PremiumPalette.S(10)))
            using (SolidBrush b = new SolidBrush(hover || open ? PremiumPalette.FieldHover : PremiumPalette.Field))
            using (Pen edge = new Pen(open || Focused ? PremiumPalette.Alpha(PremiumPalette.Cyan, 190) : hover ? PremiumPalette.BorderStrong : PremiumPalette.Border, 1f))
            { g.FillPath(b, p); g.DrawPath(edge, p); }
            PremiumPalette.TextAt(g, Value, Font, PremiumPalette.Text, new Rectangle(PremiumPalette.S(14), 0, Math.Max(8, Width - PremiumPalette.S(52)), Height), ContentAlignment.MiddleLeft);
            int s = PremiumPalette.S(16);
            StudioIcons.Draw(g, open ? "chevron-down" : "chevron-down", new RectangleF(Width - s - PremiumPalette.S(14), (Height - s) / 2f, s, s),
                open || hover ? PremiumPalette.Cyan : PremiumPalette.Subtle, 2f);
        }
    }

    // Grille de choix visibles (outils, styles, modes) : remplace les menus cachés.
    internal sealed class PremiumChoiceGrid : StudioControl
    {
        private readonly string[] values, captions, icons;
        private readonly int maxColumns;
        private readonly bool tiles;
        private string value;
        private int hot = -1;
        internal event Action<string> ValueChanged;
        internal string Value { get { return value; } set { if (this.value != value) { this.value = value; Invalidate(); } } }
        internal string[] Swatches;
        public PremiumChoiceGrid(string[] choiceValues, string[] choiceCaptions, string[] choiceIcons, int columns, bool tileMode)
        {
            values = choiceValues; captions = choiceCaptions ?? choiceValues; icons = choiceIcons; maxColumns = Math.Max(1, columns); tiles = tileMode;
            Cursor = Cursors.Hand; TabStop = true; AccessibleRole = AccessibleRole.List;
            Font = PremiumPalette.Font(tiles ? 8.75f : 9.25f, 1);
            SetStyle(ControlStyles.Selectable, true);
            Height = MeasureHeight(PremiumPalette.S(320));
        }
        private int CellHeight { get { return tiles ? PremiumPalette.S(68) : PremiumPalette.S(38); } }
        private int Gap { get { return PremiumPalette.S(8); } }
        private int Columns(int width)
        {
            int min = tiles ? PremiumPalette.S(68) : PremiumPalette.S(92);
            return Math.Max(1, Math.Min(maxColumns, (width + Gap) / (min + Gap)));
        }
        private int MeasureHeight(int width)
        {
            int cols = Columns(width), rows = (values.Length + cols - 1) / cols;
            return rows * CellHeight + Math.Max(0, rows - 1) * Gap + 2;
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int h = MeasureHeight(Width);
            if (h != Height) Height = h;
        }
        private Rectangle Cell(int i)
        {
            int cols = Columns(Width);
            int w = (Width - Gap * (cols - 1) - 1) / cols;
            return new Rectangle((i % cols) * (w + Gap), (i / cols) * (CellHeight + Gap), w, CellHeight);
        }
        private int HitTest(Point p)
        {
            for (int i = 0; i < values.Length; i++) if (Cell(i).Contains(p)) return i;
            return -1;
        }
        protected override void OnMouseMove(MouseEventArgs e) { int h = HitTest(e.Location); if (h != hot) { hot = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            int i = HitTest(e.Location);
            if (i >= 0) Pick(i);
        }
        private void Pick(int i)
        {
            Value = values[i];
            if (ValueChanged != null) ValueChanged(values[i]);
        }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int i = Array.IndexOf(values, value), cols = Columns(Width);
            int next = i;
            if (e.KeyCode == Keys.Right) next = i + 1;
            else if (e.KeyCode == Keys.Left) next = i - 1;
            else if (e.KeyCode == Keys.Down) next = i + cols;
            else if (e.KeyCode == Keys.Up) next = i - cols;
            else { base.OnKeyDown(e); return; }
            next = Math.Max(0, Math.Min(values.Length - 1, next));
            if (next != i) Pick(next);
            e.Handled = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            int radius = PremiumPalette.S(10);
            for (int i = 0; i < values.Length; i++)
            {
                Rectangle r = Cell(i);
                bool on = values[i] == value, isHot = i == hot && Enabled;
                using (GraphicsPath path = PremiumPalette.Round(r, radius))
                {
                    Color fill = on ? PremiumPalette.Mix(PremiumPalette.Field, PremiumPalette.Cyan, isHot ? .2 : .14) : isHot ? PremiumPalette.FieldHover : PremiumPalette.Field;
                    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                    if (on)
                        using (LinearGradientBrush border = new LinearGradientBrush(new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), PremiumPalette.Cyan, PremiumPalette.Purple, 20f))
                        using (Pen pen = new Pen(border, PremiumPalette.Sf(1.5f))) g.DrawPath(pen, path);
                    else using (Pen pen = new Pen(isHot ? PremiumPalette.BorderStrong : PremiumPalette.Border, 1f)) g.DrawPath(pen, path);
                }
                Color ink = !Enabled ? PremiumPalette.Faint : on ? PremiumPalette.Text : isHot ? PremiumPalette.Text : PremiumPalette.Subtle;
                Color glyph = !Enabled ? PremiumPalette.Faint : on ? PremiumPalette.Cyan : isHot ? PremiumPalette.Text : PremiumPalette.Subtle;
                string icon = icons != null && i < icons.Length ? icons[i] : null;
                string swatch = Swatches != null && i < Swatches.Length ? Swatches[i] : null;
                if (tiles)
                {
                    int s = PremiumPalette.S(22);
                    RectangleF ir = new RectangleF(r.X + (r.Width - s) / 2f, r.Y + PremiumPalette.S(11), s, s);
                    if (swatch != null) DrawSwatch(g, swatch, ir);
                    else StudioIcons.Draw(g, icon, ir, glyph, 1.8f);
                    PremiumPalette.TextAt(g, captions[i], Font, ink, new Rectangle(r.X + PremiumPalette.S(4), r.Y + PremiumPalette.S(38), r.Width - PremiumPalette.S(8), PremiumPalette.S(22)), ContentAlignment.MiddleCenter);
                }
                else
                {
                    int s = PremiumPalette.S(16);
                    bool lead = !String.IsNullOrEmpty(icon) || swatch != null;
                    int fullWidth = PremiumPalette.TextWidth(captions[i], Font);
                    if (lead && fullWidth + s + PremiumPalette.S(7) > r.Width - PremiumPalette.S(14)) lead = false;
                    int tw = Math.Min(r.Width - PremiumPalette.S(16), fullWidth);
                    int total = (lead ? s + PremiumPalette.S(7) : 0) + tw;
                    int x = r.X + Math.Max(PremiumPalette.S(8), (r.Width - total) / 2);
                    if (lead)
                    {
                        RectangleF ir = new RectangleF(x, r.Y + (r.Height - s) / 2f, s, s);
                        if (swatch != null) DrawSwatch(g, swatch, ir); else StudioIcons.Draw(g, icon, ir, glyph, 1.9f);
                        x += s + PremiumPalette.S(7);
                    }
                    PremiumPalette.TextAt(g, captions[i], Font, ink, new Rectangle(x, r.Y, r.Right - x - PremiumPalette.S(4), r.Height), ContentAlignment.MiddleLeft);
                }
                if (on && KeyboardFocus) FocusRing(g, Rectangle.Inflate(r, 2, 2), radius + 2);
            }
        }
        private static void DrawSwatch(Graphics g, string spec, RectangleF r)
        {
            // « #A|#B » = dégradé, « rainbow » = arc-en-ciel.
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(r);
                if (spec == "rainbow")
                {
                    using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), Color.Red, Color.Blue, 45f))
                    {
                        ColorBlend blend = new ColorBlend(5);
                        blend.Colors = new Color[] { Color.FromArgb(255, 80, 120), Color.FromArgb(255, 200, 60), Color.FromArgb(70, 230, 140), Color.FromArgb(60, 170, 255), Color.FromArgb(170, 100, 255) };
                        blend.Positions = new float[] { 0f, .25f, .5f, .75f, 1f };
                        b.InterpolationColors = blend;
                        g.FillPath(b, p);
                    }
                }
                else
                {
                    string[] parts = spec.Split('|');
                    Color a = Visuals.ParseColor(parts[0], PremiumPalette.Cyan);
                    Color b2 = parts.Length > 1 ? Visuals.ParseColor(parts[1], a) : a;
                    using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), a, b2, 45f)) g.FillPath(b, p);
                }
                using (Pen edge = new Pen(Color.FromArgb(90, 255, 255, 255), 1f)) g.DrawPath(edge, p);
            }
        }
    }

    // Grille de contrôles (tuiles, sélecteurs) : colonnes adaptées à la largeur.
    internal sealed class FlowGrid : Panel
    {
        private readonly int maxColumns, minCell, gap;
        public FlowGrid(int columns, int minCellWidth)
        {
            maxColumns = Math.Max(1, columns); minCell = minCellWidth; gap = PremiumPalette.S(12);
            BackColor = PremiumPalette.Surface;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            ControlAdded += delegate { Arrange(); };
            Resize += delegate { Arrange(); };
        }
        internal void Arrange()
        {
            int cols = Math.Max(1, Math.Min(maxColumns, (Width + gap) / (minCell + gap)));
            int w = Math.Max(10, (Width - gap * (cols - 1)) / cols);
            int y = 0, rowH = 0, col = 0;
            foreach (Control c in Controls)
            {
                if (col == cols) { y += rowH + gap; rowH = 0; col = 0; }
                c.SetBounds(col * (w + gap), y, w, c.Height);
                rowH = Math.Max(rowH, c.Height); col++;
            }
            int h = Controls.Count == 0 ? 0 : y + rowH;
            if (Height != h) Height = h;
        }
    }

    // Rangée de boutons de même largeur.
    internal sealed class ButtonRow : Panel
    {
        public ButtonRow(params Control[] buttons)
        {
            BackColor = PremiumPalette.Surface; Height = PremiumPalette.S(40);
            foreach (Control b in buttons) Controls.Add(b);
            Resize += delegate { Arrange(); };
        }
        private void Arrange()
        {
            int n = Controls.Count; if (n == 0) return;
            int gap = PremiumPalette.S(10), w = (Width - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++) Controls[i].SetBounds(i * (w + gap), 0, i == n - 1 ? Width - i * (w + gap) : w, Height);
        }
    }

    internal sealed class PremiumCard : Panel, IBackdrop
    {
        private readonly string heading, subtitle;
        internal readonly FlowLayoutPanel Stack;
        public int Span = 1;
        internal string IconName;
        internal Color AccentA = PremiumPalette.Cyan, AccentB = PremiumPalette.Purple;
        private Control accessory;
        private int HeaderHeight { get { return PremiumPalette.S(80); } }
        public PremiumCard(string title, string detail, int columnSpan)
        {
            heading = title; subtitle = detail; Span = columnSpan;
            BackColor = PremiumPalette.Surface;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Stack = new FlowLayoutPanel(); Stack.BackColor = PremiumPalette.Surface;
            Stack.FlowDirection = FlowDirection.TopDown; Stack.WrapContents = false; Stack.AutoScroll = false;
            Stack.Margin = Padding.Empty; Stack.Padding = Padding.Empty;
            Controls.Add(Stack);
            Resize += delegate { PlaceStack(); };
        }
        internal Control Accessory
        {
            get { return accessory; }
            set { if (accessory != null) Controls.Remove(accessory); accessory = value; if (value != null) { Controls.Add(value); value.BringToFront(); } PlaceStack(); }
        }
        public void Add(Control control)
        {
            control.Margin = new Padding(0, 0, 0, PremiumPalette.S(12));
            Stack.Controls.Add(control);
            PlaceStack();
        }
        public int PreferredCardHeight
        {
            get
            {
                int h = HeaderHeight;
                // Correctif : ne pas tester Visible (toujours faux avant l'affichage de la fenêtre
                // sous Windows -> cartes écrasées au démarrage).
                foreach (Control c in Stack.Controls) h += c.Height + c.Margin.Vertical;
                return h + PremiumPalette.S(10);
            }
        }
        internal void PlaceStack()
        {
            int pad = PremiumPalette.S(20);
            Stack.SetBounds(pad, HeaderHeight, Math.Max(60, Width - pad * 2), Math.Max(30, Height - HeaderHeight - PremiumPalette.S(6)));
            foreach (Control c in Stack.Controls) c.Width = Math.Max(48, Stack.ClientSize.Width);
            if (accessory != null)
                accessory.Location = new Point(Width - pad - accessory.Width, PremiumPalette.S(22) + (PremiumPalette.S(38) - accessory.Height) / 2);
        }
        public void PaintBackdrop(Graphics g, Control child) { g.Clear(BackColor); }
        protected override void OnPaintBackground(PaintEventArgs e) { StudioControl.PaintParent(this, e.Graphics); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            Rectangle area = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = PremiumPalette.S(16);
            using (GraphicsPath p = PremiumPalette.Round(area, radius))
            {
                using (SolidBrush b = new SolidBrush(PremiumPalette.Surface)) g.FillPath(b, p);
                // Lueur douce derrière l'icône, limitée à l'en-tête (aucun contrôle enfant dessous).
                Region old = g.Clip;
                g.SetClip(p, CombineMode.Intersect);
                int gw = PremiumPalette.S(260), gh = PremiumPalette.S(150);
                using (GraphicsPath glowPath = new GraphicsPath())
                {
                    glowPath.AddEllipse(-gw / 3, -gh / 2, gw, gh);
                    using (PathGradientBrush glow = new PathGradientBrush(glowPath))
                    {
                        glow.CenterColor = PremiumPalette.Alpha(AccentA, 30);
                        glow.SurroundColors = new Color[] { PremiumPalette.Alpha(AccentA, 0) };
                        g.FillPath(glow, glowPath);
                    }
                }
                g.Clip = old;
                // Bord « verre » : plus clair en haut, fondu en bas.
                using (LinearGradientBrush edgeBrush = new LinearGradientBrush(new Rectangle(0, -1, Math.Max(2, Width), Height + 2),
                    Color.FromArgb(58, 120, 150, 230), PremiumPalette.Alpha(PremiumPalette.Border, 160), 90f))
                using (Pen edge = new Pen(edgeBrush, 1f)) g.DrawPath(edge, p);
                using (LinearGradientBrush sheen = new LinearGradientBrush(new Rectangle(radius, 0, Math.Max(2, Width - radius * 2), 2),
                    Color.FromArgb(0, 255, 255, 255), Color.FromArgb(34, 255, 255, 255), 0f))
                {
                    ColorBlend blend = new ColorBlend(3);
                    blend.Colors = new Color[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(40, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) };
                    blend.Positions = new float[] { 0f, .5f, 1f };
                    sheen.InterpolationColors = blend;
                    using (Pen line = new Pen(sheen, 1f)) g.DrawLine(line, radius, 1, Width - radius, 1);
                }
            }
            int x = PremiumPalette.S(20);
            if (!String.IsNullOrEmpty(IconName))
            {
                int d = PremiumPalette.S(38);
                PremiumPalette.IconBubble(g, new Rectangle(x, PremiumPalette.S(22), d, d), IconName, AccentA, AccentB, false);
                x += d + PremiumPalette.S(14);
            }
            int right = Width - PremiumPalette.S(20) - (accessory != null ? accessory.Width + PremiumPalette.S(10) : 0);
            PremiumPalette.TextAt(g, heading, PremiumPalette.Font(11.5f, 1), PremiumPalette.Text,
                new Rectangle(x, PremiumPalette.S(20), Math.Max(10, right - x), PremiumPalette.S(24)), ContentAlignment.MiddleLeft);
            PremiumPalette.TextAt(g, subtitle, PremiumPalette.Font(8.75f, 0), PremiumPalette.Subtle,
                new Rectangle(x, PremiumPalette.S(43), Math.Max(10, right - x), PremiumPalette.S(20)), ContentAlignment.MiddleLeft);
        }
    }

    // Couleur : pastille, valeur HEX éditable et palette rapide en un seul bloc.
    internal sealed class ColorField : Control
    {
        private readonly string title;
        private readonly TextBox editor;
        private string value;
        private int hot = -1;
        private ColorWheelPopup wheelPopup;
        internal event Action<string> ColorPicked;
        internal string Value
        {
            get { return value; }
            set { this.value = value; if (!editor.Focused) editor.Text = value; Invalidate(); }
        }
        public ColorField(string label, string initial)
        {
            title = label; value = initial; Height = PremiumPalette.S(76); BackColor = PremiumPalette.Surface;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Default;
            editor = new TextBox(); editor.BorderStyle = BorderStyle.None; editor.BackColor = PremiumPalette.Field;
            editor.ForeColor = PremiumPalette.Text; editor.Font = PremiumPalette.Font(9.5f, 1); editor.Text = initial;
            editor.CharacterCasing = CharacterCasing.Upper; editor.MaxLength = 7; editor.AccessibleName = label + " (hexadécimal)";
            editor.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { Apply(editor.Text); e.SuppressKeyPress = true; } };
            editor.Leave += delegate { Apply(editor.Text); };
            Controls.Add(editor);
            Resize += delegate { Place(); }; Place();
        }
        private Rectangle Pill { get { int w = PremiumPalette.S(104); return new Rectangle(Width - w - 1, 0, w, PremiumPalette.S(28)); } }
        private void Place()
        {
            Rectangle p = Pill;
            editor.Width = p.Width - PremiumPalette.S(38);
            editor.Location = new Point(p.X + PremiumPalette.S(32), p.Y + Math.Max(0, (p.Height - editor.Height) / 2));
        }
        private void Apply(string text)
        {
            string t = (text ?? "").Trim().ToUpperInvariant();
            if (t.Length == 6 && t[0] != '#') t = "#" + t;
            Color c = Visuals.ParseColor(t, Color.Empty);
            if (c.IsEmpty || t.Length != 7 || t[0] != '#') { editor.Text = value; return; }
            if (t == value) return;
            value = t; editor.Text = t; Invalidate();
            if (ColorPicked != null) ColorPicked(t);
        }
        private Rectangle Swatch(int i)
        {
            int d = PremiumPalette.S(24), gap = PremiumPalette.S(9);
            int count = PremiumPalette.QuickColors.Length + 1; // + bouton « roue chromatique »
            int step = Math.Min(d + gap, Math.Max(d + 2, (Width - d) / Math.Max(1, count - 1)));
            return new Rectangle(i * step, PremiumPalette.S(40), d, d);
        }
        private bool InSwatchPill(Point p)
        {
            Rectangle pill = Pill;
            return new Rectangle(pill.X, pill.Y, PremiumPalette.S(32), pill.Height).Contains(p);
        }
        private void ApplyFromWheel(string hex)
        {
            if (String.Equals(hex, value, StringComparison.OrdinalIgnoreCase)) return;
            value = hex.ToUpperInvariant();
            if (!editor.Focused) editor.Text = value;
            Invalidate();
            if (ColorPicked != null) ColorPicked(value);
        }
        private void ToggleWheel()
        {
            if (!Enabled) return;
            if (wheelPopup != null && !wheelPopup.IsDisposed) { wheelPopup.Dismiss(); return; }
            Form form = FindForm(); if (form == null) return;
            wheelPopup = new ColorWheelPopup(value, this);
            wheelPopup.Picked += delegate(string hex) { ApplyFromWheel(hex); };
            wheelPopup.Dismissed += delegate { wheelPopup = null; Invalidate(); };
            wheelPopup.ShowIn(form, RectangleToScreen(new Rectangle(0, 0, Width, Height)));
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1;
            for (int i = 0; i <= PremiumPalette.QuickColors.Length; i++) if (Rectangle.Inflate(Swatch(i), 3, 3).Contains(e.Location)) h = i;
            if (h < 0 && InSwatchPill(e.Location)) h = PremiumPalette.QuickColors.Length;
            if (h != hot) { hot = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || hot < 0 || !Enabled) return;
            if (hot == PremiumPalette.QuickColors.Length) ToggleWheel();
            else Apply(PremiumPalette.QuickColors[hot]);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && wheelPopup != null && !wheelPopup.IsDisposed) wheelPopup.Dismiss();
            base.Dispose(disposing);
        }
        protected override void OnEnabledChanged(EventArgs e) { editor.Enabled = Enabled; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            g.Clear(BackColor);
            Rectangle p = Pill;
            PremiumPalette.TextAt(g, title, PremiumPalette.Font(9.75f, 1), Enabled ? PremiumPalette.Text : PremiumPalette.Faint,
                new Rectangle(PremiumPalette.S(2), 0, Math.Max(10, p.X - PremiumPalette.S(10)), p.Height), ContentAlignment.MiddleLeft);
            using (GraphicsPath path = PremiumPalette.Round(p, PremiumPalette.S(8)))
            using (SolidBrush b = new SolidBrush(PremiumPalette.Field))
            using (Pen edge = new Pen(editor.Focused ? PremiumPalette.Alpha(PremiumPalette.Cyan, 170) : PremiumPalette.Border, 1f))
            { g.FillPath(b, path); g.DrawPath(edge, path); }
            Color current = Visuals.ParseColor(value, PremiumPalette.Cyan);
            int sd = PremiumPalette.S(16);
            using (SolidBrush sw = new SolidBrush(Enabled ? current : Color.FromArgb(90, current)))
                g.FillEllipse(sw, p.X + PremiumPalette.S(9), p.Y + (p.Height - sd) / 2f, sd, sd);
            using (Pen ring = new Pen(Color.FromArgb(80, 255, 255, 255), 1f)) g.DrawEllipse(ring, p.X + PremiumPalette.S(9), p.Y + (p.Height - sd) / 2f, sd, sd);
            for (int i = 0; i < PremiumPalette.QuickColors.Length; i++)
            {
                Rectangle r = Swatch(i);
                if (r.Right > Width) break;
                Color c = Visuals.ParseColor(PremiumPalette.QuickColors[i], Color.White);
                bool on = String.Equals(PremiumPalette.QuickColors[i], value, StringComparison.OrdinalIgnoreCase);
                if (on || i == hot)
                    using (Pen ring = new Pen(on ? Color.White : Color.FromArgb(120, 255, 255, 255), PremiumPalette.Sf(on ? 2f : 1.4f)))
                        g.DrawEllipse(ring, r.X - PremiumPalette.S(3), r.Y - PremiumPalette.S(3), r.Width + PremiumPalette.S(6), r.Height + PremiumPalette.S(6));
                using (SolidBrush b = new SolidBrush(Enabled ? c : Color.FromArgb(80, c))) g.FillEllipse(b, r);
            }
            // Dernier élément : disque de teintes = ouvre la roue chromatique.
            Rectangle wr = Swatch(PremiumPalette.QuickColors.Length);
            if (wr.Right <= Width)
            {
                bool open = wheelPopup != null && !wheelPopup.IsDisposed;
                if (open || hot == PremiumPalette.QuickColors.Length)
                    using (Pen ring = new Pen(open ? Color.White : Color.FromArgb(120, 255, 255, 255), PremiumPalette.Sf(open ? 2f : 1.4f)))
                        g.DrawEllipse(ring, wr.X - PremiumPalette.S(3), wr.Y - PremiumPalette.S(3), wr.Width + PremiumPalette.S(6), wr.Height + PremiumPalette.S(6));
                for (int a = 0; a < 360; a += 15)
                    using (SolidBrush hb = new SolidBrush(Enabled ? Visuals.Hsv(a, .85, 1) : Color.FromArgb(80, Visuals.Hsv(a, .85, 1))))
                        g.FillPie(hb, wr.X, wr.Y, wr.Width, wr.Height, a - 90, 16);
                int hole = wr.Width * 38 / 100;
                using (SolidBrush center = new SolidBrush(PremiumPalette.Surface))
                    g.FillEllipse(center, wr.X + (wr.Width - hole) / 2f, wr.Y + (wr.Height - hole) / 2f, hole, hole);
            }
        }
    }

    // Tuile de module (Accueil) : état + interrupteur + accès direct à la page.
    internal sealed class ModuleTile : StudioControl
    {
        private readonly string icon, title;
        private bool isOn;
        private bool hotToggle;
        internal string Status = "";
        internal Color AccentA = PremiumPalette.Cyan, AccentB = PremiumPalette.Purple;
        internal event Action<bool> Toggled;
        internal event Action Open;
        internal bool Checked { get { return isOn; } set { if (isOn != value) { isOn = value; Invalidate(); } } }
        public ModuleTile(string name, string iconName)
        {
            title = name; icon = iconName; Height = PremiumPalette.S(74); Cursor = Cursors.Hand; TabStop = true;
            AccessibleRole = AccessibleRole.PushButton; AccessibleName = name;
            SetStyle(ControlStyles.Selectable, true);
        }
        private Rectangle ToggleRect
        {
            get { int w = PremiumPalette.S(42), h = PremiumPalette.S(24); return new Rectangle(Width - w - PremiumPalette.S(16), (Height - h) / 2, w, h); }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool t = Rectangle.Inflate(ToggleRect, PremiumPalette.S(6), PremiumPalette.S(10)).Contains(e.Location);
            if (t != hotToggle) { hotToggle = t; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !ClientRectangle.Contains(e.Location)) return;
            if (hotToggle) { isOn = !isOn; Invalidate(); if (Toggled != null) Toggled(isOn); }
            else if (Open != null) Open();
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { isOn = !isOn; Invalidate(); if (Toggled != null) Toggled(isOn); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { if (Open != null) Open(); e.Handled = true; }
            else base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = PremiumPalette.Round(r, PremiumPalette.S(14)))
            {
                using (SolidBrush b = new SolidBrush(hover && !hotToggle ? PremiumPalette.FieldHover : PremiumPalette.SurfaceHigh)) g.FillPath(b, path);
                using (Pen edge = new Pen(isOn ? PremiumPalette.Alpha(AccentA, 90) : PremiumPalette.Border, 1f)) g.DrawPath(edge, path);
            }
            int d = PremiumPalette.S(42);
            PremiumPalette.IconBubble(g, new Rectangle(PremiumPalette.S(14), (Height - d) / 2, d, d), icon, AccentA, AccentB, isOn);
            int x = PremiumPalette.S(14) + d + PremiumPalette.S(14);
            int w = ToggleRect.X - x - PremiumPalette.S(10);
            PremiumPalette.TextAt(g, title, PremiumPalette.Font(10.5f, 1), PremiumPalette.Text, new Rectangle(x, Height / 2 - PremiumPalette.S(21), w, PremiumPalette.S(22)), ContentAlignment.MiddleLeft);
            Color statusColor = isOn ? PremiumPalette.Green : PremiumPalette.Faint;
            int dot = PremiumPalette.S(6);
            using (SolidBrush b = new SolidBrush(statusColor)) g.FillEllipse(b, x, Height / 2 + PremiumPalette.S(9) - dot / 2, dot, dot);
            PremiumPalette.TextAt(g, isOn ? Status : "Désactivé", PremiumPalette.Font(8.75f, 0), isOn ? PremiumPalette.Subtle : PremiumPalette.Faint,
                new Rectangle(x + dot + PremiumPalette.S(7), Height / 2, w - dot - PremiumPalette.S(7), PremiumPalette.S(19)), ContentAlignment.MiddleLeft);
            DrawToggle(g, ToggleRect, isOn ? 1f : 0f, hotToggle, true);
            FocusRing(g, r, PremiumPalette.S(14));
        }
    }

    // Style instantané (Accueil) : pastille de couleurs + sélection visible.
    internal sealed class PresetTile : StudioControl
    {
        private readonly string title, detail;
        private readonly Color[] colors;
        private bool selected;
        internal bool Selected { get { return selected; } set { if (selected != value) { selected = value; Invalidate(); } } }
        public PresetTile(string name, string description, params Color[] palette)
        {
            title = name; detail = description; colors = palette; Height = PremiumPalette.S(60); Cursor = Cursors.Hand; TabStop = true;
            AccessibleRole = AccessibleRole.RadioButton; AccessibleName = name;
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            else base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = PremiumPalette.Round(r, PremiumPalette.S(12)))
            {
                Color fill = selected ? PremiumPalette.Mix(PremiumPalette.SurfaceHigh, PremiumPalette.Cyan, .12) : hover ? PremiumPalette.FieldHover : PremiumPalette.SurfaceHigh;
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                if (selected)
                    using (LinearGradientBrush border = new LinearGradientBrush(new Rectangle(-1, -1, Width + 2, Height + 2), PremiumPalette.Cyan, PremiumPalette.Purple, 0f))
                    using (Pen pen = new Pen(border, PremiumPalette.Sf(1.5f))) g.DrawPath(pen, path);
                else using (Pen pen = new Pen(PremiumPalette.Border, 1f)) g.DrawPath(pen, path);
            }
            int d = PremiumPalette.S(34);
            Rectangle sw = new Rectangle(PremiumPalette.S(12), (Height - d) / 2, d, d);
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(sw);
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(sw.X - 1, sw.Y - 1, sw.Width + 2, sw.Height + 2), colors[0], colors[colors.Length - 1], 45f))
                {
                    if (colors.Length > 2)
                    {
                        ColorBlend blend = new ColorBlend(colors.Length);
                        blend.Colors = colors;
                        float[] pos = new float[colors.Length];
                        for (int i = 0; i < pos.Length; i++) pos[i] = i / (float)(pos.Length - 1);
                        blend.Positions = pos; b.InterpolationColors = blend;
                    }
                    g.FillPath(b, p);
                }
            }
            using (Pen inner = new Pen(Color.FromArgb(200, 10, 14, 30), PremiumPalette.Sf(3)))
                g.DrawEllipse(inner, sw.X + d * .3f, sw.Y + d * .3f, d * .4f, d * .4f);
            int x = sw.Right + PremiumPalette.S(12), w = Width - x - PremiumPalette.S(36);
            PremiumPalette.TextAt(g, title, PremiumPalette.Font(10f, 1), PremiumPalette.Text, new Rectangle(x, Height / 2 - PremiumPalette.S(19), w, PremiumPalette.S(20)), ContentAlignment.MiddleLeft);
            PremiumPalette.TextAt(g, detail, PremiumPalette.Font(8.5f, 0), PremiumPalette.Subtle, new Rectangle(x, Height / 2, w, PremiumPalette.S(18)), ContentAlignment.MiddleLeft);
            if (selected)
            {
                int s = PremiumPalette.S(18);
                StudioIcons.Draw(g, "check", new RectangleF(Width - s - PremiumPalette.S(14), (Height - s) / 2f, s, s), PremiumPalette.Cyan, 2.4f);
            }
            FocusRing(g, r, PremiumPalette.S(12));
        }
    }

    // Liste de raccourcis : libellé + touches dessinées comme des capsules.
    internal sealed class ShortcutList : Control
    {
        private readonly string[][] keys;
        private readonly string[] labels;
        public ShortcutList(string[] shortcutLabels, string[][] shortcutKeys)
        {
            labels = shortcutLabels; keys = shortcutKeys; BackColor = PremiumPalette.Surface;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = labels.Length * PremiumPalette.S(36);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g); g.Clear(PremiumPalette.BackOf(this));
            Font keyFont = PremiumPalette.Font(8.25f, 1);
            int rowH = PremiumPalette.S(36);
            for (int i = 0; i < labels.Length; i++)
            {
                int y = i * rowH;
                if (i > 0) using (Pen sep = new Pen(Color.FromArgb(20, 255, 255, 255))) g.DrawLine(sep, 0, y, Width, y);
                int x = Width;
                for (int k = keys[i].Length - 1; k >= 0; k--)
                {
                    int w = Math.Max(PremiumPalette.S(26), PremiumPalette.TextWidth(keys[i][k], keyFont) + PremiumPalette.S(14));
                    x -= w;
                    Rectangle chip = new Rectangle(x, y + (rowH - PremiumPalette.S(24)) / 2, w, PremiumPalette.S(24));
                    using (GraphicsPath p = PremiumPalette.Round(chip, PremiumPalette.S(6)))
                    using (SolidBrush b = new SolidBrush(PremiumPalette.Field))
                    using (Pen edge = new Pen(PremiumPalette.BorderStrong, 1f)) { g.FillPath(b, p); g.DrawPath(edge, p); }
                    using (Pen bottom = new Pen(Color.FromArgb(60, 0, 0, 0), 1f)) g.DrawLine(bottom, chip.X + 4, chip.Bottom, chip.Right - 4, chip.Bottom);
                    PremiumPalette.TextAt(g, keys[i][k], keyFont, PremiumPalette.Text, chip, ContentAlignment.MiddleCenter);
                    x -= PremiumPalette.S(5);
                }
                PremiumPalette.TextAt(g, labels[i], PremiumPalette.Font(9.25f, 0), PremiumPalette.Subtle, new Rectangle(0, y, Math.Max(10, x - PremiumPalette.S(8)), rowH), ContentAlignment.MiddleLeft);
            }
        }
    }

    // Note d'aide (icône + texte sur 2 lignes maximum).
    internal sealed class InfoNote : Control
    {
        private readonly Color tint;
        private readonly string icon;
        public InfoNote(string text) : this(text, "info", PremiumPalette.Cyan) { }
        public InfoNote(string text, string iconName, Color accent)
        {
            Text = text; icon = iconName; tint = accent; Height = PremiumPalette.S(54);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        // Hauteur ajustée au texte : plus de phrase coupée par des points de suspension.
        private void Fit()
        {
            int w = Math.Max(PremiumPalette.S(60), Width - PremiumPalette.S(50));
            Size need = TextRenderer.MeasureText(Text ?? "", PremiumPalette.Font(8.75f, 0), new Size(w, 2000),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            int h = Math.Max(PremiumPalette.S(40), need.Height + PremiumPalette.S(20));
            if (h != Height) Height = h;
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Fit(); }
        protected override void OnTextChanged(EventArgs e) { Fit(); Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g); g.Clear(PremiumPalette.BackOf(this));
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = PremiumPalette.Round(r, PremiumPalette.S(10)))
            using (SolidBrush b = new SolidBrush(PremiumPalette.Alpha(tint, 18)))
            using (Pen edge = new Pen(PremiumPalette.Alpha(tint, 50), 1f)) { g.FillPath(b, p); g.DrawPath(edge, p); }
            int s = PremiumPalette.S(17);
            StudioIcons.Draw(g, icon, new RectangleF(PremiumPalette.S(12), PremiumPalette.S(10), s, s), tint, 1.9f);
            PremiumPalette.WrapText(g, Text, PremiumPalette.Font(8.75f, 0), PremiumPalette.Subtle,
                new Rectangle(PremiumPalette.S(38), PremiumPalette.S(10), Width - PremiumPalette.S(50), Height - PremiumPalette.S(12)));
        }
    }

    // Segmented control pour les sous-sections d'une page.
    internal sealed class TabStrip : StudioControl
    {
        private readonly string[] tabs;
        private readonly string[] icons;
        private int index, hot = -1;
        internal event Action<int> Changed;
        internal int Index { get { return index; } set { index = value; Invalidate(); } }
        public TabStrip(string[] names, string[] iconNames)
        {
            tabs = names; icons = iconNames; Height = PremiumPalette.S(42); Cursor = Cursors.Hand; TabStop = true;
            Font = PremiumPalette.Font(9.5f, 1); AccessibleRole = AccessibleRole.PageTabList;
            SetStyle(ControlStyles.Selectable, true);
            Width = PreferredWidth;
        }
        internal int PreferredWidth
        {
            get
            {
                int w = PremiumPalette.S(8);
                for (int i = 0; i < tabs.Length; i++) w += SegmentWidth(i);
                return w;
            }
        }
        private int SegmentWidth(int i) { return PremiumPalette.TextWidth(tabs[i], Font) + PremiumPalette.S(icons != null ? 62 : 40); }
        private Rectangle Segment(int i)
        {
            int x = PremiumPalette.S(4);
            for (int k = 0; k < i; k++) x += SegmentWidth(k);
            return new Rectangle(x, PremiumPalette.S(4), SegmentWidth(i), Height - PremiumPalette.S(9));
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1; for (int i = 0; i < tabs.Length; i++) if (Segment(i).Contains(e.Location)) h = i;
            if (h != hot) { hot = h; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && hot >= 0 && hot != index) { index = hot; Invalidate(); if (Changed != null) Changed(index); }
        }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode; return k == Keys.Left || k == Keys.Right || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int next = e.KeyCode == Keys.Right ? index + 1 : e.KeyCode == Keys.Left ? index - 1 : index;
            next = Math.Max(0, Math.Min(tabs.Length - 1, next));
            if (next != index) { index = next; Invalidate(); if (Changed != null) Changed(index); e.Handled = true; }
            else base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = PremiumPalette.Round(r, PremiumPalette.S(12)))
            using (SolidBrush b = new SolidBrush(PremiumPalette.Surface))
            using (Pen edge = new Pen(PremiumPalette.Border, 1f)) { g.FillPath(b, p); g.DrawPath(edge, p); }
            for (int i = 0; i < tabs.Length; i++)
            {
                Rectangle s = Segment(i);
                bool on = i == index;
                if (on || i == hot)
                    using (GraphicsPath p = PremiumPalette.Round(s, PremiumPalette.S(9)))
                    {
                        if (on)
                            using (LinearGradientBrush fill = new LinearGradientBrush(s, PremiumPalette.Alpha(PremiumPalette.Cyan, 60), PremiumPalette.Alpha(PremiumPalette.Purple, 50), 0f))
                            using (Pen edge = new Pen(PremiumPalette.Alpha(PremiumPalette.Cyan, 90), 1f)) { g.FillPath(fill, p); g.DrawPath(edge, p); }
                        else using (SolidBrush fill = new SolidBrush(Color.FromArgb(12, 255, 255, 255))) g.FillPath(fill, p);
                    }
                int x = s.X + PremiumPalette.S(18);
                if (icons != null)
                {
                    int sz = PremiumPalette.S(16);
                    StudioIcons.Draw(g, icons[i], new RectangleF(x, s.Y + (s.Height - sz) / 2f, sz, sz), on ? PremiumPalette.Cyan : PremiumPalette.Faint, 1.9f);
                    x += sz + PremiumPalette.S(8);
                }
                PremiumPalette.TextAt(g, tabs[i], Font, on ? PremiumPalette.Text : PremiumPalette.Subtle, new Rectangle(x, s.Y, s.Right - x, s.Height), ContentAlignment.MiddleLeft);
                if (on && KeyboardFocus) FocusRing(g, s, PremiumPalette.S(9));
            }
        }
    }

    // Pastille d'état cliquable de l'en-tête (effets actifs / en pause).
    internal sealed class StatusPill : StudioControl
    {
        private bool active = true;
        internal bool Active { get { return active; } set { if (active != value) { active = value; Text = value ? "Effets actifs" : "Effets en pause"; Width = PreferredWidth; Invalidate(); } } }
        public StatusPill()
        {
            Text = "Effets actifs"; Height = PremiumPalette.S(34); Cursor = Cursors.Hand; TabStop = true;
            Font = PremiumPalette.Font(9.25f, 1); AccessibleRole = AccessibleRole.CheckButton;
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
            Width = PreferredWidth;
        }
        internal int PreferredWidth { get { return PremiumPalette.TextWidth(Text, Font) + PremiumPalette.S(44); } }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            else base.OnKeyDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            Color tone = active ? PremiumPalette.Green : PremiumPalette.Amber;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = PremiumPalette.Round(r, Height / 2))
            using (SolidBrush b = new SolidBrush(PremiumPalette.Alpha(tone, hover ? 40 : 24)))
            using (Pen edge = new Pen(PremiumPalette.Alpha(tone, hover ? 150 : 90), 1f)) { g.FillPath(b, p); g.DrawPath(edge, p); }
            int d = PremiumPalette.S(8);
            float cx = PremiumPalette.S(15), cy = Height / 2f;
            using (SolidBrush glow = new SolidBrush(PremiumPalette.Alpha(tone, 70))) g.FillEllipse(glow, cx - d, cy - d, d * 2, d * 2);
            using (SolidBrush dot = new SolidBrush(tone)) g.FillEllipse(dot, cx - d / 2f, cy - d / 2f, d, d);
            PremiumPalette.TextAt(g, Text, Font, PremiumPalette.Text, new Rectangle(PremiumPalette.S(28), 0, Width - PremiumPalette.S(34), Height), ContentAlignment.MiddleLeft);
            FocusRing(g, r, Height / 2);
        }
    }

    // Bannière d'accueil : statut global + actions principales + illustration.
    internal sealed class HeroBanner : Panel, IBackdrop
    {
        internal string Title = "", Status = "";
        internal bool Active = true;
        private readonly Timer pulse = new Timer();
        private double phase;
        public HeroBanner()
        {
            Height = PremiumPalette.S(176); BackColor = PremiumPalette.Surface;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            pulse.Interval = 50; pulse.Tick += delegate { phase += .06; Invalidate(new Rectangle(Width / 2, 0, Width / 2, Height)); };
            VisibleChanged += delegate { if (Visible) pulse.Start(); else pulse.Stop(); };
            Disposed += delegate { pulse.Dispose(); };
            pulse.Start();
        }
        private static Color HeroA { get { return PremiumPalette.Mix(Color.FromArgb(12, 18, 40), PremiumPalette.Cyan, .14); } }
        private static Color HeroB { get { return PremiumPalette.Mix(Color.FromArgb(16, 14, 40), PremiumPalette.Purple, .26); } }
        protected override void OnPaintBackground(PaintEventArgs e) { StudioControl.PaintParent(this, e.Graphics); }
        // Les boutons posés sur la bannière reprennent exactement son dégradé.
        public void PaintBackdrop(Graphics g, Control child)
        {
            GraphicsState st = g.Save();
            g.TranslateTransform(-child.Left, -child.Top);
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, Width - 1, Height - 1), HeroA, HeroB, 10f))
                g.FillRectangle(b, 0, 0, Width, Height);
            g.Restore(st);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = PremiumPalette.S(18);
            using (GraphicsPath p = PremiumPalette.Round(r, radius))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(r, HeroA, HeroB, 10f)) g.FillPath(b, p);
                Region old = g.Clip; g.SetClip(p, CombineMode.Intersect);
                // Anneaux décoratifs animés (rappel du halo).
                float cx = Width - PremiumPalette.S(150), cy = Height / 2f;
                for (int i = 0; i < 4; i++)
                {
                    float rad = PremiumPalette.Sf(34 + i * 30) + (float)Math.Sin(phase + i * .8) * PremiumPalette.Sf(3);
                    using (Pen ring = new Pen(PremiumPalette.Alpha(i % 2 == 0 ? PremiumPalette.Cyan : PremiumPalette.Purple, 150 - i * 32), PremiumPalette.Sf(i == 0 ? 3f : 1.6f)))
                        g.DrawEllipse(ring, cx - rad, cy - rad, rad * 2, rad * 2);
                }
                using (GraphicsPath glowPath = new GraphicsPath())
                {
                    float gr = PremiumPalette.Sf(70);
                    glowPath.AddEllipse(cx - gr, cy - gr, gr * 2, gr * 2);
                    using (PathGradientBrush glow = new PathGradientBrush(glowPath))
                    {
                        glow.CenterColor = PremiumPalette.Alpha(PremiumPalette.Cyan, 70);
                        glow.SurroundColors = new Color[] { PremiumPalette.Alpha(PremiumPalette.Cyan, 0) };
                        g.FillPath(glow, glowPath);
                    }
                }
                // Particules en orbite + comète autour du pointeur.
                for (int i = 0; i < 7; i++)
                {
                    double ang = phase * (i % 2 == 0 ? .55 : -.4) + i * 0.9;
                    float rad = PremiumPalette.Sf(34 + (i % 4) * 30);
                    float px = cx + (float)Math.Cos(ang) * rad, py = cy + (float)Math.Sin(ang) * rad;
                    float pd = PremiumPalette.Sf(i % 3 == 0 ? 5 : 3);
                    Color pc = i % 2 == 0 ? PremiumPalette.Cyan : PremiumPalette.Purple;
                    for (int t = 1; t <= 5; t++)
                    {
                        double ta = ang - (i % 2 == 0 ? .055 : -.04) * t * 1.6;
                        float tx = cx + (float)Math.Cos(ta) * rad, ty = cy + (float)Math.Sin(ta) * rad;
                        using (SolidBrush tb = new SolidBrush(PremiumPalette.Alpha(pc, 90 - t * 16))) g.FillEllipse(tb, tx - pd / 2, ty - pd / 2, pd, pd);
                    }
                    using (SolidBrush halo = new SolidBrush(PremiumPalette.Alpha(pc, 60))) g.FillEllipse(halo, px - pd * 1.5f, py - pd * 1.5f, pd * 3, pd * 3);
                    using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, px - pd / 2, py - pd / 2, pd, pd);
                }
                StudioIcons.Draw(g, "pointer", new RectangleF(cx - PremiumPalette.Sf(16), cy - PremiumPalette.Sf(18), PremiumPalette.Sf(38), PremiumPalette.Sf(38)), Color.White, 1.6f);
                g.Clip = old;
                using (Pen edge = new Pen(PremiumPalette.Alpha(PremiumPalette.Purple, 70), 1f)) g.DrawPath(edge, p);
            }
            int x = PremiumPalette.S(28), w = Width - PremiumPalette.S(330);
            PremiumPalette.TextAt(g, "TABLEAU DE BORD", PremiumPalette.Font(8f, 1), PremiumPalette.Cyan, new Rectangle(x, PremiumPalette.S(22), w, PremiumPalette.S(18)), ContentAlignment.MiddleLeft);
            PremiumPalette.GradientText(g, Title, PremiumPalette.Font(20f, 2), new Rectangle(x, PremiumPalette.S(40), Math.Max(50, Width - PremiumPalette.S(300)), PremiumPalette.S(40)),
                Color.White, PremiumPalette.Mix(PremiumPalette.Cyan, Color.White, .25));
            Color tone = Active ? PremiumPalette.Green : PremiumPalette.Amber;
            int d = PremiumPalette.S(8);
            using (SolidBrush dot = new SolidBrush(tone)) g.FillEllipse(dot, x + 1, PremiumPalette.S(88) - d / 2, d, d);
            PremiumPalette.TextAt(g, Status, PremiumPalette.Font(9.5f, 0), PremiumPalette.Subtle, new Rectangle(x + PremiumPalette.S(16), PremiumPalette.S(78), Math.Max(50, Width - PremiumPalette.S(320)), PremiumPalette.S(20)), ContentAlignment.MiddleLeft);
        }
    }

    // Schéma de la souris : boutons affectés en couleur, dernier bouton détecté en vert.
    // Tableau « bouton / appui court / appui long » : trois colonnes alignées, une ligne par bouton.
    internal sealed class ActionTable : Panel
    {
        private readonly string[] heads;
        private readonly System.Collections.Generic.List<Control[]> rows = new System.Collections.Generic.List<Control[]>();
        public ActionTable(params string[] columnTitles)
        {
            heads = columnTitles; BackColor = PremiumPalette.Surface;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Resize += delegate { Arrange(); };
            Height = HeadHeight;
        }
        private static int HeadHeight { get { return PremiumPalette.S(28); } }
        private static int RowHeight { get { return PremiumPalette.S(40); } }
        private static int RowGap { get { return PremiumPalette.S(10); } }
        private static int LabelWidth { get { return PremiumPalette.S(150); } }
        public void AddRow(Control label, Control first, Control second)
        {
            rows.Add(new Control[] { label, first, second });
            Controls.Add(label); Controls.Add(first); Controls.Add(second);
            Arrange();
        }
        private void Arrange()
        {
            int gap = PremiumPalette.S(12), lw = LabelWidth;
            int cw = Math.Max(PremiumPalette.S(60), (Width - lw - gap * 2) / 2);
            int y = HeadHeight;
            foreach (Control[] row in rows)
            {
                row[0].SetBounds(0, y, lw, RowHeight);
                row[1].SetBounds(lw + gap, y, cw, RowHeight);
                row[2].SetBounds(lw + gap * 2 + cw, y, cw, RowHeight);
                y += RowHeight + RowGap;
            }
            int h = rows.Count == 0 ? HeadHeight : y - RowGap;
            if (Height != h) Height = h;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(BackColor);
            int gap = PremiumPalette.S(12), lw = LabelWidth;
            int cw = Math.Max(PremiumPalette.S(60), (Width - lw - gap * 2) / 2);
            for (int i = 0; i < heads.Length && i < 3; i++)
            {
                int x = i == 0 ? 0 : i == 1 ? lw + gap : lw + gap * 2 + cw;
                PremiumPalette.TextAt(g, heads[i].ToUpperInvariant(), PremiumPalette.Font(7.75f, 2),
                    i == 2 ? PremiumPalette.Purple : PremiumPalette.Faint, new Rectangle(x + (i == 0 ? 0 : PremiumPalette.S(4)), 0, i == 0 ? lw : cw, HeadHeight - PremiumPalette.S(4)), ContentAlignment.MiddleLeft);
            }
        }
    }

    internal sealed class MouseDiagram : Control
    {
        internal Func<int, string> ActionOf;
        internal Func<int, string> LongActionOf;
        internal string Detected = "";
        private static readonly string[] Names = { "Gauche", "Droit", "Molette", "Latéral X1", "Latéral X2" };
        private static readonly string[] ButtonIds = { "Gauche", "Droit", "Milieu", "X1", "X2" };
        public MouseDiagram()
        {
            Height = PremiumPalette.S(200); BackColor = PremiumPalette.Surface;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        private Color StateColor(int i)
        {
            if (Detected == ButtonIds[i]) return PremiumPalette.Green;
            string a = ActionOf == null ? "Normal" : ActionOf(i);
            string l = LongActionOf == null ? "Normal" : LongActionOf(i);
            return a == "Normal" && l == "Normal" ? Color.Empty : PremiumPalette.Cyan;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g); g.Clear(BackColor);
            float s = PremiumPalette.Scale;
            float mw = 112 * s, mh = 176 * s, mx = 26 * s, my = (Height - mh) / 2f;
            RectangleF body = new RectangleF(mx, my, mw, mh);
            using (GraphicsPath shell = PremiumPalette.Round(body, mw / 2))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(body, PremiumPalette.SurfaceHigh, PremiumPalette.Field, 90f)) g.FillPath(b, shell);
                using (Pen edge = new Pen(PremiumPalette.BorderStrong, 1.4f * s)) g.DrawPath(edge, shell);
                Region old = g.Clip; g.SetClip(shell, CombineMode.Intersect);
                RectangleF left = new RectangleF(mx, my, mw / 2, mh * .42f), right = new RectangleF(mx + mw / 2, my, mw / 2, mh * .42f);
                FillZone(g, left, StateColor(0)); FillZone(g, right, StateColor(1));
                g.Clip = old;
                using (Pen split = new Pen(PremiumPalette.BorderStrong, 1.2f * s))
                {
                    g.DrawLine(split, mx + mw / 2, my, mx + mw / 2, my + mh * .42f);
                    g.DrawLine(split, mx, my + mh * .42f, mx + mw, my + mh * .42f);
                }
            }
            RectangleF wheel = new RectangleF(mx + mw / 2 - 7 * s, my + 24 * s, 14 * s, 30 * s);
            using (GraphicsPath w = PremiumPalette.Round(wheel, 7 * s))
            {
                Color c = StateColor(2);
                using (SolidBrush b = new SolidBrush(c.IsEmpty ? PremiumPalette.Background : PremiumPalette.Alpha(c, 200))) g.FillPath(b, w);
                using (Pen edge = new Pen(PremiumPalette.BorderStrong, 1.2f * s)) g.DrawPath(edge, w);
            }
            for (int i = 0; i < 2; i++)
            {
                RectangleF side = new RectangleF(mx - 6 * s, my + (i == 0 ? 104 : 76) * s, 9 * s, 22 * s);
                using (GraphicsPath p = PremiumPalette.Round(side, 4 * s))
                {
                    Color c = StateColor(3 + i);
                    using (SolidBrush b = new SolidBrush(c.IsEmpty ? PremiumPalette.Field : c)) g.FillPath(b, p);
                    using (Pen edge = new Pen(PremiumPalette.BorderStrong, 1.2f * s)) g.DrawPath(edge, p);
                }
            }
            float lx = mx + mw + 40 * s, row = (Height - 10 * s) / 5f;
            for (int i = 0; i < 5; i++)
            {
                float y = 5 * s + i * row;
                Color c = StateColor(i);
                string action = ActionOf == null ? "Normal" : ActionOf(i);
                using (SolidBrush dot = new SolidBrush(c.IsEmpty ? PremiumPalette.Faint : c)) g.FillEllipse(dot, lx, y + row / 2 - 4 * s, 8 * s, 8 * s);
                PremiumPalette.TextAt(g, Names[i], PremiumPalette.Font(9.5f, 1), PremiumPalette.Text,
                    new Rectangle((int)(lx + 18 * s), (int)y, (int)(110 * s), (int)row), ContentAlignment.MiddleLeft);
                string lng = LongActionOf == null ? "Normal" : LongActionOf(i);
                bool hasLong = lng != "Normal";
                int tx = (int)(lx + 130 * s), tw = Math.Max(20, Width - tx);
                PremiumPalette.TextAt(g, action == "Normal" ? "Comportement Windows" : action, PremiumPalette.Font(9f, 0),
                    action == "Normal" ? PremiumPalette.Faint : PremiumPalette.Cyan,
                    new Rectangle(tx, (int)(y - (hasLong ? row * .2f : 0)), tw, (int)row), ContentAlignment.MiddleLeft);
                if (hasLong)
                    PremiumPalette.TextAt(g, "Appui long : " + lng, PremiumPalette.Font(8.5f, 0), PremiumPalette.Purple,
                        new Rectangle(tx, (int)(y + row * .26f), tw, (int)row), ContentAlignment.MiddleLeft);
            }
        }
        private static void FillZone(Graphics g, RectangleF r, Color c)
        {
            if (c.IsEmpty) return;
            using (LinearGradientBrush b = new LinearGradientBrush(r, PremiumPalette.Alpha(c, 150), PremiumPalette.Alpha(c, 40), 90f)) g.FillRectangle(b, r);
        }
    }

    internal enum PreviewMode { Halo, Trail, Clicks, Cursor }

    // Aperçu animé : réutilise les VRAIS moteurs de rendu (halo, traînée, clics, viseur).
    internal sealed class PremiumPreview : Control
    {
        internal Config Config;
        internal bool TextMode = true;
        internal PreviewMode Mode = PreviewMode.Halo;
        private PointF point;
        private bool mouseInside;
        private readonly Timer timer = new Timer();
        private readonly List<TrailDot> dots = new List<TrailDot>();
        private readonly List<Ripple> ripples = new List<Ripple>();
        private DateTime started = DateTime.UtcNow, lastAutoClick = DateTime.MinValue;
        private PointF lastSample, textAnchor;
        private bool autoRight;
        private static Bitmap logo; private static string logoPath = "";
        public PremiumPreview(Config config) : this(config, PreviewMode.Halo) { }
        public PremiumPreview(Config config, PreviewMode mode)
        {
            Config = config; Mode = mode; Height = PremiumPalette.S(232); Cursor = Cursors.Cross;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            timer.Interval = 33;
            timer.Tick += delegate { Step(); Invalidate(); };
            VisibleChanged += delegate { if (Visible) timer.Start(); else timer.Stop(); };
            Disposed += delegate { timer.Dispose(); };
            timer.Start();
        }
        protected override void OnMouseMove(MouseEventArgs e) { point = e.Location; mouseInside = true; base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { mouseInside = false; base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Mode == PreviewMode.Clicks && Config.Clicks) ripples.Add(new Ripple(e.Location, DateTime.UtcNow, e.Button == MouseButtons.Right));
            base.OnMouseDown(e);
        }
        private PointF AutoPoint(double t)
        {
            float cx = Width / 2f, cy = Height / 2f + PremiumPalette.Sf(6);
            return new PointF(cx + (float)Math.Sin(t * .9) * Width * .32f, cy + (float)Math.Sin(t * 1.8) * Height * .22f);
        }
        private void Step()
        {
            DateTime now = DateTime.UtcNow;
            double t = (now - started).TotalSeconds;
            if (!mouseInside)
            {
                if (Mode != PreviewMode.Halo) point = AutoPoint(t);
                else if (TextMode) point = textAnchor.IsEmpty ? new PointF(Width * .5f, Height * .4f) : textAnchor;
                else point = AutoPoint(t * .55);
            }
            if (Mode == PreviewMode.Trail && Config.Trail)
            {
                double spacing = Math.Max(2, Config.TrailSpacing);
                double dist = Math.Sqrt(Math.Pow(point.X - lastSample.X, 2) + Math.Pow(point.Y - lastSample.Y, 2));
                if (dist >= spacing)
                {
                    int steps = Math.Max(1, Math.Min(10, (int)(dist / spacing)));
                    for (int s = 1; s <= steps; s++)
                    {
                        float k = s / (float)steps;
                        dots.Add(new TrailDot(Point.Round(new PointF(lastSample.X + (point.X - lastSample.X) * k, lastSample.Y + (point.Y - lastSample.Y) * k)), now, (float)Math.Min(4, dist / 33.0)));
                    }
                    lastSample = point;
                    if (dots.Count > 160) dots.RemoveRange(0, dots.Count - 160);
                }
                dots.RemoveAll(delegate(TrailDot d) { return (now - d.Time).TotalMilliseconds > Config.TrailLife; });
                if (Config.TrailStyle == "Logo" && logoPath != (Config.TrailLogoPath ?? "")) LoadLogo(Config.TrailLogoPath);
            }
            if (Mode == PreviewMode.Clicks)
            {
                if (Config.Clicks && !mouseInside && (now - lastAutoClick).TotalMilliseconds > Math.Max(900, Config.ClickDuration + 200))
                {
                    autoRight = !autoRight; lastAutoClick = now;
                    PointF a = AutoPoint(t * 1.7);
                    ripples.Add(new Ripple(Point.Round(a), now, autoRight));
                }
                ripples.RemoveAll(delegate(Ripple r) { return (now - r.Time).TotalMilliseconds > Config.ClickDuration + 150; });
            }
        }
        private static void LoadLogo(string path)
        {
            logoPath = path ?? "";
            if (logo != null) { logo.Dispose(); logo = null; }
            try
            {
                if (String.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
                using (Image src = Image.FromFile(path))
                {
                    double k = Math.Min(1.0, 128.0 / Math.Max(src.Width, src.Height));
                    logo = new Bitmap(src, Math.Max(1, (int)Math.Round(src.Width * k)), Math.Max(1, (int)Math.Round(src.Height * k)));
                }
            }
            catch { logo = null; }
        }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(PremiumPalette.BackOf(this)); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            DateTime now = DateTime.UtcNow;
            using (GraphicsPath p = PremiumPalette.Round(r, PremiumPalette.S(14)))
            {
                using (LinearGradientBrush b = new LinearGradientBrush(r, Color.FromArgb(9, 13, 29), Color.FromArgb(15, 17, 40), 35f)) g.FillPath(b, p);
                Region old = g.Clip; g.SetClip(p, CombineMode.Intersect);
                int step = PremiumPalette.S(22);
                using (SolidBrush dot = new SolidBrush(Color.FromArgb(16, 255, 255, 255)))
                    for (int y = step / 2; y < Height; y += step) for (int x = step / 2; x < Width; x += step) g.FillRectangle(dot, x, y, 1.6f, 1.6f);
                if (Mode == PreviewMode.Halo) PaintHalo(g, now);
                else if (Mode == PreviewMode.Trail) PaintTrail(g, now);
                else if (Mode == PreviewMode.Clicks) PaintClicks(g, now);
                g.Clip = old;
                using (Pen edge = new Pen(PremiumPalette.Border, 1f)) g.DrawPath(edge, p);
            }
            Rectangle chip = new Rectangle(PremiumPalette.S(14), PremiumPalette.S(12), PremiumPalette.S(96), PremiumPalette.S(24));
            using (GraphicsPath c = PremiumPalette.Round(chip, chip.Height / 2))
            using (SolidBrush b = new SolidBrush(Color.FromArgb(180, 12, 18, 38)))
            using (Pen edge = new Pen(PremiumPalette.Alpha(PremiumPalette.Green, 80), 1f)) { g.FillPath(b, c); g.DrawPath(edge, c); }
            using (SolidBrush dot = new SolidBrush(PremiumPalette.Green)) g.FillEllipse(dot, chip.X + PremiumPalette.S(10), chip.Y + chip.Height / 2 - PremiumPalette.S(3), PremiumPalette.S(6), PremiumPalette.S(6));
            PremiumPalette.TextAt(g, "En direct", PremiumPalette.Font(8.25f, 1), PremiumPalette.Text, new Rectangle(chip.X + PremiumPalette.S(22), chip.Y, chip.Width - PremiumPalette.S(24), chip.Height), ContentAlignment.MiddleLeft);
            string hint = Mode == PreviewMode.Clicks ? "Clic gauche / droit pour tester" : "Survole pour piloter l'aperçu";
            PremiumPalette.TextAt(g, hint, PremiumPalette.Font(8.25f, 0), PremiumPalette.Faint, new Rectangle(0, PremiumPalette.S(12), Width - PremiumPalette.S(16), PremiumPalette.S(24)), ContentAlignment.MiddleRight);
        }
        private void PaintOff(Graphics g, string text)
        {
            PremiumPalette.TextAt(g, text, PremiumPalette.Font(10f, 1), PremiumPalette.Faint, new Rectangle(0, 0, Width, Height), ContentAlignment.MiddleCenter);
        }
        private void PaintHalo(Graphics g, DateTime now)
        {
            {
                Font line = PremiumPalette.Font(14f, 1); // police partagée : ne jamais la libérer
                int x = PremiumPalette.S(30), y = PremiumPalette.S(58);
                string first = TextMode ? "Édite ton texte avec précision." : "Un curseur visible, partout.";
                TextRenderer.DrawText(g, first, line, new Point(x, y), PremiumPalette.Text, TextFormatFlags.NoPadding);
                textAnchor = new PointF(x + PremiumPalette.TextWidth(first, line) + PremiumPalette.Sf(6), y + PremiumPalette.Sf(13));
                Font sub = PremiumPalette.Font(10.5f, 0);
                TextRenderer.DrawText(g, "Le halo te suit en douceur et reste lisible", sub, new Point(x, y + PremiumPalette.S(40)), PremiumPalette.Subtle, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, "sur tous les fonds, clairs comme sombres.", sub, new Point(x, y + PremiumPalette.S(66)), PremiumPalette.Subtle, TextFormatFlags.NoPadding);
            }
            if (!Config.Ring) { PaintOff(g, ""); }
            else
            {
                PointF center = point;
                if (!(TextMode && Config.RingCenterOnText)) center = new PointF(point.X + Config.RingOffsetX / 2f, point.Y + Config.RingOffsetY / 2f);
                GraphicsState st = g.Save();
                OverlayForm.DrawRingCore(g, now, center, Config, false, 1f, TextMode, TextMode && Config.RingTextGrow ? 1f + Config.RingTextGrowPercent / 100f : 1f);
                g.Restore(st);
            }
            DrawPointer(g, point, TextMode);
        }
        private void PaintTrail(Graphics g, DateTime now)
        {
            if (!Config.Trail) { PaintOff(g, "Traînée désactivée"); return; }
            GraphicsState st = g.Save();
            OverlayForm.DrawTrailCore(g, now, Config, dots, logo);
            g.Restore(st);
            DrawPointer(g, point, false);
        }
        private void PaintClicks(Graphics g, DateTime now)
        {
            if (!Config.Clicks) { PaintOff(g, "Animations de clic désactivées"); return; }
            GraphicsState st = g.Save();
            OverlayForm.DrawRipplesCore(g, now, Config, ripples);
            g.Restore(st);
            if (mouseInside) DrawPointer(g, point, false);
        }
        private static void DrawPointer(Graphics g, PointF p, bool ibeam)
        {
            float s = PremiumPalette.Scale;
            if (ibeam)
            {
                using (Pen shadow = new Pen(Color.FromArgb(160, 0, 0, 0), 4f * s))
                using (Pen pen = new Pen(Color.White, 1.8f * s))
                {
                    foreach (Pen q in new Pen[] { shadow, pen })
                    {
                        g.DrawLine(q, p.X, p.Y - 11 * s, p.X, p.Y + 11 * s);
                        g.DrawLine(q, p.X - 4 * s, p.Y - 12 * s, p.X + 4 * s, p.Y - 12 * s);
                        g.DrawLine(q, p.X - 4 * s, p.Y + 12 * s, p.X + 4 * s, p.Y + 12 * s);
                    }
                }
                return;
            }
            PointF[] a = { new PointF(0, 0), new PointF(0, 17), new PointF(4.4f, 13), new PointF(7.4f, 19.6f), new PointF(10, 18.4f), new PointF(7.1f, 12), new PointF(12.6f, 11.8f) };
            for (int i = 0; i < a.Length; i++) a[i] = new PointF(p.X + a[i].X * s, p.Y + a[i].Y * s);
            using (SolidBrush fill = new SolidBrush(Color.White)) g.FillPolygon(fill, a);
            using (Pen edge = new Pen(Color.FromArgb(220, 10, 12, 24), 1.3f * s)) { edge.LineJoin = LineJoin.Round; g.DrawPolygon(edge, a); }
        }
    }
}
