using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CursorFlow
{
    // Saisie d'une annotation texte, au design du Studio.
    internal sealed class TextPrompt : Form
    {
        private readonly TextBox input;
        public string Value { get { return input.Text; } }
        public TextPrompt()
        {
            Text = L.T("Haloryva · Annotation texte");
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false; TopMost = true; KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(PremiumPalette.S(500), PremiumPalette.S(226));
            BackColor = PremiumPalette.Surface; ForeColor = PremiumPalette.Text;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

            Panel field = new Panel(); field.BackColor = PremiumPalette.Field;
            field.SetBounds(PremiumPalette.S(28), PremiumPalette.S(100), ClientSize.Width - PremiumPalette.S(56), PremiumPalette.S(42));
            input = new TextBox(); input.AccessibleName = L.T("Texte de l'annotation"); input.BorderStyle = BorderStyle.None; input.BackColor = PremiumPalette.Field;
            input.ForeColor = PremiumPalette.Text; input.Font = PremiumPalette.Font(11.5f, 0);
            input.SetBounds(PremiumPalette.S(12), 0, field.Width - PremiumPalette.S(24), PremiumPalette.S(30));
            input.Top = Math.Max(0, (field.Height - input.Height) / 2);
            field.Controls.Add(input); Controls.Add(field);

            PremiumButton ok = new PremiumButton(L.T("Ajouter au Bureau"), ButtonKind.Primary, "check");
            ok.SetBounds(ClientSize.Width - PremiumPalette.S(28) - PremiumPalette.S(190) - PremiumPalette.S(130), PremiumPalette.S(160), PremiumPalette.S(180), PremiumPalette.S(42));
            ok.Click += delegate { DialogResult = DialogResult.OK; Close(); }; Controls.Add(ok);
            PremiumButton cancel = new PremiumButton(L.Key("dialog.cancel"), ButtonKind.Secondary, null);
            cancel.SetBounds(ClientSize.Width - PremiumPalette.S(28) - PremiumPalette.S(130), PremiumPalette.S(160), PremiumPalette.S(130), PremiumPalette.S(42));
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); }; Controls.Add(cancel);
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); e.SuppressKeyPress = true; }
            };
            Shown += delegate { input.Focus(); };
        }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; } // CS_DROPSHADOW
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; PremiumPalette.Quality(g);
            using (Pen edge = new Pen(PremiumPalette.BorderStrong)) g.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
            using (LinearGradientBrush bar = new LinearGradientBrush(new Rectangle(0, 0, Width, 3), PremiumPalette.Cyan, PremiumPalette.Purple, 0f))
                g.FillRectangle(bar, 0, 0, Width, 3);
            int d = PremiumPalette.S(38);
            PremiumPalette.IconBubble(g, new Rectangle(PremiumPalette.S(28), PremiumPalette.S(28), d, d), "text", PremiumPalette.Cyan, PremiumPalette.Purple, true);
            PremiumPalette.TextAt(g, L.T("Ajouter une annotation"), PremiumPalette.Font(14f, 2), PremiumPalette.Text,
                new Rectangle(PremiumPalette.S(80), PremiumPalette.S(24), Width - PremiumPalette.S(100), PremiumPalette.S(26)), ContentAlignment.MiddleLeft);
            PremiumPalette.WrapText(g, L.T("Le texte s'affiche à l'endroit cliqué. Entrée pour valider, Échap pour annuler."), PremiumPalette.Font(8.75f, 0), PremiumPalette.Subtle,
                new Rectangle(PremiumPalette.S(80), PremiumPalette.S(50), Width - PremiumPalette.S(100), PremiumPalette.S(42)));
            Rectangle f = new Rectangle(PremiumPalette.S(27), PremiumPalette.S(99), Width - PremiumPalette.S(54), PremiumPalette.S(43));
            using (Pen p = new Pen(PremiumPalette.Alpha(PremiumPalette.Cyan, 150))) g.DrawRectangle(p, f);
        }
    }
}
