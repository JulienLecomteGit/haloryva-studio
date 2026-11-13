using System.Windows.Forms;

namespace CursorFlow
{
    internal static class KeyboardPolicy
    {
        // Right Alt is reserved for text entry, including layouts where Windows
        // represents AltGr as a synthetic left Ctrl followed by right Alt.
        // Do not log or display these combinations. Left Ctrl + left Alt remains available.
        internal static bool IsTextEntry(Keys key, bool rightAltDown)
        { return rightAltDown || key == Keys.RMenu; }

        internal static bool IsModifier(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey ||
                key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu ||
                key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey ||
                key == Keys.LWin || key == Keys.RWin;
        }
        internal static bool IsShortcut(Keys key, InputModifiers m, bool drawing, bool capture)
        {
            if (IsTextEntry(key, m.RightAlt)) return false;
            if (m.Ctrl && m.Alt && !m.Win &&
                (key == Keys.R || key == Keys.M || key == Keys.H || key == Keys.D ||
                    key == Keys.C || key == Keys.Back || key == Keys.L || key == Keys.F ||
                    key == Keys.K || key == Keys.Z || key == Keys.X)) return true;
            return (key == Keys.Escape && (capture || drawing)) ||
                (m.Ctrl && !m.Alt && drawing && (key == Keys.Z || key == Keys.Y));
        }
    }
}
