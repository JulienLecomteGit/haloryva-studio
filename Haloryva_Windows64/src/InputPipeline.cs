using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace CursorFlow
{
    internal struct InputModifiers
    {
        internal bool Ctrl, Alt, Shift, Win, RightAlt;
    }
    internal enum InputKind { Move, Observe, Click, Action, Drawing, Key, Capture, CancelStroke }
    internal enum ModeChange { None, ToggleEnabled, ToggleDrawing, StopDrawing, SuspendDrawing }

    // Value-only UI -> hook publication. No Control, Config or mutable collection is shared.
    internal sealed class InputSnapshot
    {
        internal readonly long Revision, Acknowledged;
        internal readonly bool Enabled, Drawing, KeyDisplay;
        internal readonly int Session, PressEpoch, CaptureToken, Threshold;
        internal readonly DateTime CaptureUntil;
        internal readonly string DrawingTool, DrawingColor;
        internal readonly int DrawingWidth;
        private readonly string[] shortActions, longActions;
        internal readonly IntPtr Overlay, Annotations;
        internal InputSnapshot(long revision, long acknowledged, Config c, bool drawing, int session,
            int pressEpoch, int captureToken, DateTime captureUntil, IntPtr overlay, IntPtr annotations)
        {
            Revision = revision; Acknowledged = acknowledged; Enabled = c.Enabled; Drawing = drawing;
            Session = session; PressEpoch = pressEpoch; CaptureToken = captureToken; CaptureUntil = captureUntil;
            KeyDisplay = c.KeyDisplay; Threshold = c.LongPressMs; DrawingTool = c.AnnotationTool;
            DrawingColor = c.AnnotationColor; DrawingWidth = c.AnnotationThickness;
            shortActions = new[] { c.ButtonLeft, c.ButtonRight, c.ButtonMiddle, c.ButtonX1, c.ButtonX2 };
            longActions = new[] { c.ButtonLeftLong, c.ButtonRightLong, c.ButtonMiddleLong, c.ButtonX1Long, c.ButtonX2Long };
            Overlay = overlay; Annotations = annotations;
        }
        internal string Action(string button, bool isLong)
        {
            int i = button == "Gauche" ? 0 : button == "Droit" ? 1 : button == "Milieu" ? 2 : button == "X1" ? 3 : 4;
            return (isLong ? longActions : shortActions)[i];
        }
    }
    internal sealed class InputCommand
    {
        internal long Sequence;
        internal InputKind Kind;
        internal int X, Y, Code, Session, CaptureToken;
        internal string Button, Action, DrawingTool, DrawingColor;
        internal int DrawingWidth;
        internal bool Long;
        internal Keys Key;
        internal InputModifiers Modifiers;
    }
    internal interface IInputMailbox
    { bool TryPost(InputCommand command, bool terminal); }

    // Capacity is bounded. Space is reserved for endings of already-consumed gestures.
    internal sealed class InputMailbox : IInputMailbox
    {
        private readonly object sync = new object();
        private readonly LinkedList<InputCommand> queue = new LinkedList<InputCommand>();
        private readonly int capacity;
        internal InputMailbox(int capacity) { this.capacity = Math.Max(64, capacity); }
        internal int Count { get { lock (sync) return queue.Count; } }
        public bool TryPost(InputCommand command, bool terminal)
        {
            lock (sync)
            {
                if (command.Kind == InputKind.Move && queue.Last != null && queue.Last.Value.Kind == InputKind.Move)
                { queue.Last.Value = command; return true; }
                if (queue.Count >= capacity - (terminal ? 0 : 32)) return false;
                queue.AddLast(command); return true;
            }
        }
        internal InputCommand Take()
        {
            lock (sync)
            {
                if (queue.First == null) return null;
                InputCommand result = queue.First.Value; queue.RemoveFirst(); return result;
            }
        }
        internal void Clear() { lock (sync) queue.Clear(); }
    }

    // Owned exclusively by the hook thread. Tests inject the clock, mailbox and replay.
    internal sealed class InputRouter
    {
        private readonly IInputMailbox mailbox;
        private readonly Action<string> replay;
        private readonly PressBook presses;
        private readonly MouseRoutes routes = new MouseRoutes();
        private readonly HashSet<uint> pressedKeys = new HashSet<uint>(), blockedKeys = new HashSet<uint>();
        private sealed class Prediction { internal long Sequence; internal ModeChange Change; }
        private readonly List<Prediction> predictions = new List<Prediction>();
        private readonly List<long> predictedResets = new List<long>();
        private InputSnapshot snapshot;
        private bool enabled, drawing, textStroke;
        private int session, consumedCapture;
        private long sequence;
        internal bool Enabled { get { return enabled; } }
        internal bool Drawing { get { return drawing; } }
        internal int Session { get { return session; } }
        internal InputRouter(IInputMailbox mailbox, Action<string> replay, Func<long> clock)
        { this.mailbox = mailbox; this.replay = replay; presses = new PressBook(clock); }
        internal void Update(InputSnapshot next)
        {
            if (next == null || (snapshot != null && next.Revision <= snapshot.Revision)) return;
            bool acknowledgedReset = snapshot != null && predictedResets.Exists(delegate(long n) { return n > snapshot.Acknowledged && n <= next.Acknowledged; });
            if (snapshot != null && !acknowledgedReset && (next.PressEpoch != snapshot.PressEpoch || (snapshot.Enabled && !next.Enabled))) presses.Clear();
            predictedResets.RemoveAll(delegate(long n) { return n <= next.Acknowledged; });
            snapshot = next;
            enabled = next.Enabled; drawing = next.Drawing; session = next.Session;
            predictions.RemoveAll(delegate(Prediction p) { return p.Sequence <= next.Acknowledged; });
            foreach (Prediction p in predictions) Apply(p.Change);
        }
        private void Apply(ModeChange change)
        {
            if (change == ModeChange.ToggleEnabled)
            { enabled = !enabled; if (!enabled) { drawing = false; session++; } }
            else if (change == ModeChange.ToggleDrawing)
            { enabled = true; drawing = !drawing; session++; }
            else if (change == ModeChange.StopDrawing || change == ModeChange.SuspendDrawing)
            { drawing = false; session++; }
        }
        private static ModeChange ActionChange(string action)
        {
            return action == "Dessin : activer / arrêter" ? ModeChange.ToggleDrawing :
                action == "Effets : pause / reprise" ? ModeChange.ToggleEnabled : ModeChange.None;
        }
        private bool Post(InputCommand command, bool terminal, ModeChange change)
        {
            command.Sequence = ++sequence;
            if (!mailbox.TryPost(command, terminal)) return false;
            if (change != ModeChange.None)
            {
                predictions.Add(new Prediction { Sequence = command.Sequence, Change = change });
                Apply(change);
                if (change == ModeChange.ToggleEnabled && !enabled)
                { presses.Clear(); predictedResets.Add(command.Sequence); }
            }
            return true;
        }
        private bool CaptureActive(DateTime now)
        { return snapshot.CaptureToken != 0 && snapshot.CaptureToken != consumedCapture && now <= snapshot.CaptureUntil; }
        internal bool Mouse(int code, string button, int x, int y, bool ownWindow, bool ownInjection, DateTime now)
        {
            if (snapshot == null || ownInjection) return false;
            if (code == Native.WM_MOUSEMOVE)
            {
                bool trace = drawing && routes.IsDrawing("Gauche", session) && !ownWindow;
                var move = new InputCommand { Kind = trace ? InputKind.Drawing : InputKind.Move, Code = code, X = x, Y = y, Session = session };
                if (!Post(move, false, ModeChange.None) && trace)
                {
                    // Do not silently connect a stroke across a missing segment.
                    Post(new InputCommand { Kind = InputKind.CancelStroke, Session = session }, true, ModeChange.StopDrawing);
                }
                return false;
            }
            bool down = code == Native.WM_LBUTTONDOWN || code == Native.WM_RBUTTONDOWN || code == Native.WM_MBUTTONDOWN || code == Native.WM_XBUTTONDOWN;
            if (!down)
            {
                RoutedPress route = routes.Release(button);
                if (route.Route == MouseRoute.Pass) return false;
                if (route.Route == MouseRoute.Held) Execute(presses.Release(button), true);
                if (route.Route == MouseRoute.Drawing && drawing && route.DrawingSession == session)
                    Post(new InputCommand { Kind = InputKind.Drawing, Code = code, X = x, Y = y, Session = session }, true,
                        button == "Gauche" && textStroke ? ModeChange.SuspendDrawing : ModeChange.None);
                return true;
            }
            MouseRoute chosen = MouseRoute.Pass;
            int currentSession = session;
            if (CaptureActive(now))
            {
                if (Post(new InputCommand { Kind = InputKind.Capture, Button = button, CaptureToken = snapshot.CaptureToken }, false, ModeChange.None))
                { consumedCapture = snapshot.CaptureToken; chosen = MouseRoute.Block; }
            }
            else if (!ownWindow && enabled)
            {
                if (drawing && (button == "Gauche" || button == "Droit"))
                {
                    if (Post(new InputCommand { Kind = InputKind.Drawing, Code = code, X = x, Y = y, Session = session, Button = button,
                        DrawingTool = snapshot.DrawingTool, DrawingColor = snapshot.DrawingColor, DrawingWidth = snapshot.DrawingWidth }, false,
                        button == "Droit" ? ModeChange.StopDrawing : ModeChange.None))
                    { chosen = MouseRoute.Drawing; if (button == "Gauche") textStroke = snapshot.DrawingTool == "Texte"; }
                }
                else if ((button == "Milieu" || button == "X1" || button == "X2") &&
                    !String.IsNullOrEmpty(snapshot.Action(button, true)) && snapshot.Action(button, true) != "Normal")
                {
                    if (Post(new InputCommand { Kind = InputKind.Observe, Button = button }, false, ModeChange.None) &&
                        presses.Begin(button, snapshot.Action(button, false), snapshot.Action(button, true), snapshot.Threshold, x, y))
                        chosen = MouseRoute.Held;
                }
                else
                {
                    string action = snapshot.Action(button, false);
                    if (!String.IsNullOrEmpty(action) && action != "Normal")
                    {
                        if (Post(new InputCommand { Kind = InputKind.Action, Button = button, Action = action, X = x, Y = y }, false, ActionChange(action))) chosen = MouseRoute.Block;
                    }
                    else Post(new InputCommand { Kind = InputKind.Click, Button = button, X = x, Y = y }, false, ModeChange.None);
                }
            }
            else Post(new InputCommand { Kind = InputKind.Observe, Button = button }, false, ModeChange.None);
            routes.Begin(button, chosen, currentSession);
            return chosen != MouseRoute.Pass;
        }
        private void Execute(PressCommand command, bool terminal)
        {
            if (command == null) return;
            if (String.IsNullOrEmpty(command.Action) || command.Action == "Normal") replay(command.Button);
            else Post(new InputCommand { Kind = InputKind.Action, Button = command.Button, Action = command.Action,
                X = command.X, Y = command.Y, Long = command.Long }, terminal, ActionChange(command.Action));
        }
        internal void Poll() { presses.Poll(delegate(PressCommand c) { Execute(c, true); }); }
        internal bool IsPressed(Keys key) { return pressedKeys.Contains((uint)key); }
        internal bool Key(Keys key, bool down, InputModifiers mods, DateTime now)
        {
            if (snapshot == null) return false;
            uint code = (uint)key;
            if (!down) { pressedKeys.Remove(code); return blockedKeys.Remove(code); }
            if (!pressedKeys.Add(code)) return blockedKeys.Contains(code);
            mods.RightAlt |= key == Keys.RMenu || IsPressed(Keys.RMenu);
            if (KeyboardPolicy.IsTextEntry(key, mods.RightAlt) || KeyboardPolicy.IsModifier(key)) return false;
            bool block = KeyboardPolicy.IsShortcut(key, mods, drawing, CaptureActive(now));
            if (!block && !(enabled && snapshot.KeyDisplay && (mods.Ctrl || mods.Alt || mods.Win))) return false;
            ModeChange change = ModeChange.None;
            if (mods.Ctrl && mods.Alt && !mods.Win)
            {
                if (key == Keys.M) change = ModeChange.ToggleEnabled;
                if (key == Keys.D) change = ModeChange.ToggleDrawing;
            }
            if (key == Keys.Escape)
            {
                if (!CaptureActive(now) && drawing) change = ModeChange.ToggleDrawing;
            }
            bool accepted = Post(new InputCommand { Kind = InputKind.Key, Key = key, Modifiers = mods }, false, change);
            if (accepted && key == Keys.Escape && CaptureActive(now)) consumedCapture = snapshot.CaptureToken;
            if (accepted && key == Keys.R && mods.Ctrl && mods.Alt && !mods.Win)
            { presses.Clear(); predictedResets.Add(sequence); }
            if (block && accepted) blockedKeys.Add(code);
            return block && accepted;
        }
    }
}
