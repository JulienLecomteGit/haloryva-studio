using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace CursorFlow
{
    internal sealed class Ripple
    {
        public Point Point;
        public DateTime Time;
        public bool Right;
        public Ripple(Point p, DateTime t, bool right) { Point = p; Time = t; Right = right; }
    }
    internal sealed class AnnotationStroke
    {
        public string Tool;
        public Color Color;
        public int Width;
        public List<Point> Points = new List<Point>();
        public List<Point> OriginalPoints = new List<Point>();
        public string OriginalTool = "";
        public bool Smoothed = false;
        public int Number;
        public string Text = "";
    }

    // Contrat minimal entre l'interface Studio et le moteur : permet de tester
    // et de rendre l'interface sans hooks globaux ni calques Windows.
    internal interface IStudioEngine
    {
        Config Options { get; }
        bool IsDrawing { get; }
        int StrokeCount { get; }
        int RedoCount { get; }
        event Action SettingsChanged;
        event Action<string> MouseButtonObserved;
        void Changed();
        void ToggleEnabled();
        void ToggleDrawing();
        void DrawOnDesktop();
        void UndoDrawing();
        void RedoDrawing();
        void ClearDrawings();
        void ExportDrawings();
        void RestoreLastOriginalStroke();
        void ReplaceOptions(Config next);
        void ArmButtonCapture();
        void CancelButtonCapture();
        void KeepEffectLayersVisible();
    }

    internal sealed class CursorController : ApplicationContext, IStudioEngine
    {
        public int StrokeCount { get { return Strokes.Count; } }
        public int RedoCount { get { return RedoStrokes.Count; } }
        public Config Options { get; private set; }
        public Point CursorPoint { get; private set; }
        // Le délai d'inactivité concerne uniquement les déplacements réels du pointeur.
        private DateTime lastPointerMotion = DateTime.UtcNow;
        private int lastRingVisibilityStep = -1;
        public bool IsTextPointer { get; private set; }
        public float RingTextScale = 1f;
        private readonly Stopwatch growthClock = Stopwatch.StartNew();
        private long lastGrowth;
        public float RingVisibility(DateTime now)
        {
            if (!Options.Enabled || !Options.Ring) return 0f;
            if (!Options.RingAutoHide) return 1f;
            double elapsed = (now - lastPointerMotion).TotalMilliseconds;
            double fadeStart = Options.RingIdleSeconds * 1000.0;
            if (elapsed <= fadeStart) return 1f;
            double progress = (elapsed - fadeStart) / Math.Max(100, Options.RingFadeMilliseconds);
            return (float)Math.Max(0.0, Math.Min(1.0, 1.0 - progress));
        }
        public Point RingCenter(Point point)
        {
            if (Options.RingCenterOnText && IsTextPointer) return point;
            return new Point(point.X + Options.RingOffsetX, point.Y + Options.RingOffsetY);
        }
        public bool IsDrawing { get; private set; }
        private readonly TrailHistory trailHistory = new TrailHistory();
        public List<TrailDot> Trail { get { return trailHistory.Points; } }
        public List<Ripple> Ripples = new List<Ripple>();
        public List<AnnotationStroke> Strokes = new List<AnnotationStroke>();
        public List<AnnotationStroke> RedoStrokes = new List<AnnotationStroke>();
        public AnnotationStroke CurrentStroke;
        public DateTime ShakeUntil = DateTime.MinValue;
        public DateTime ZoomUntil = DateTime.MinValue;
        public DateTime KeysUntil = DateTime.MinValue;
        public string LastKeys = "";
        public event Action SettingsChanged;
        private DateTime previousMove = DateTime.MinValue, lastShake = DateTime.MinValue;
        private Point lastRenderedPoint;
        private bool forceRender = true;
        private bool annotationsDirty = true;
        private DateTime lastAnnotationDraw = DateTime.MinValue;
        private DateTime lastEffectDraw = DateTime.MinValue;
        public event Action<string> MouseButtonObserved;
        private string lastPhysicalButton = "Aucun";
        private bool capturePending;
        private int captureToken, pressEpoch;
        private long inputRevision, inputAcknowledged;
        private bool processingInput;
        private DateTime captureArmedAt = DateTime.MinValue;
        public void ArmButtonCapture() { capturePending = true; captureToken++; captureArmedAt = DateTime.UtcNow; PublishInputState(); }
        public bool CaptureButtonIfArmed(string button)
        {
            if (!capturePending) return false;
            // Armement périmé (6 s) : ne jamais avaler un clic sans rapport avec la détection.
            if ((DateTime.UtcNow - captureArmedAt).TotalSeconds > 6) { capturePending = false; return false; }
            capturePending = false;
            NotifyPhysicalButton(button);
            return true;
        }
        public void CancelButtonCapture() { capturePending = false; PublishInputState(); }
        public string LastPhysicalButton { get { return lastPhysicalButton; } }
        public void NotifyPhysicalButton(string button)
        {
            lastPhysicalButton = button;
            if (MouseButtonObserved != null) MouseButtonObserved(button);
        }
        // Toute fenêtre de Haloryva (réglages, saisie de texte, sélecteurs de fichier ou de couleur) est réservée à son interface.
        private bool IsOwnInteractiveWindowAt(int x, int y)
        {
            // Walk Z order, ignoring click-through effects, not the foreground PID.
            // An inactive owned dialog must still receive its first activation click.
            IntPtr hwnd = Native.GetTopWindow(IntPtr.Zero);
            for (int i = 0; hwnd != IntPtr.Zero && i < 1024; i++, hwnd = Native.GetWindow(hwnd, 2))
            {
                if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) continue;
                if (hwnd == overlay.Handle || hwnd == annotations.Handle) continue;
                if ((Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) & Native.WS_EX_TRANSPARENT) != 0) continue;
                Native.RECT r;
                if (!Native.GetWindowRect(hwnd, out r) || x < r.Left || x >= r.Right || y < r.Top || y >= r.Bottom) continue;
                uint pid; Native.GetWindowThreadProcessId(hwnd, out pid);
                return pid == Native.GetCurrentProcessId();
            }
            return false;
        }
        public bool IsPointInSettings(int x, int y) { return IsOwnInteractiveWindowAt(x, y); }
        public void MarkAnnotationsDirty() { annotationsDirty = true; }
        private Point previousPoint;
        private int lastDirection, directionChanges, annotationNumber;
        private readonly TrayIcon tray;
        private readonly OverlayForm overlay;
        private readonly AnnotationOverlayForm annotations;
        private readonly ToolToast toast;
        private readonly GlobalHooks hooks;
        private readonly FramePump timer;
        private PremiumStudioForm settings;
        private bool lastAutoStart;
        private bool savePending;
        private DateTime lastSettingChange = DateTime.MinValue;
        private SoundPlayer customPlayer;
        private string customPlayerPath = "";

        private System.Threading.EventWaitHandle exitRequested;
        private bool exiting;
        public CursorController()
        {
            try { exitRequested = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset,
                "Local\\HaloryvaStudio.Exit." + Process.GetCurrentProcess().Id); } catch { }
            Options = ConfigStore.Load();
            L.Initialize(Options.UiLanguage);
            TrailMetrics.Configure(Options);
            lastAutoStart = Options.LaunchAtLogin;
            CursorPoint = Cursor.Position;
            previousPoint = CursorPoint;
            lastRenderedPoint = new Point(int.MinValue, int.MinValue);
            lastPointerMotion = DateTime.UtcNow;
            tray = new TrayIcon(this);
            overlay = new OverlayForm(this);
            overlay.Show();
            annotations = new AnnotationOverlayForm(this);
            annotations.Show();
            toast = new ToolToast();
            hooks = new GlobalHooks(CreateInputSnapshot(), ProcessInput);
            if (!hooks.Installed) ConfigStore.SetNotice(L.T("Les raccourcis souris/clavier globaux n'ont pas pu démarrer : boutons et dessin indisponibles. Relance Haloryva."));
            timer = new FramePump(overlay, RenderTick, Options.FrameRate);
            timer.Start();
            if (!Array.Exists(Environment.GetCommandLineArgs(), delegate(string a) { return a == "--silent"; }))
                ShowSettings();
        }

        private InputSnapshot CreateInputSnapshot()
        {
            return new InputSnapshot(++inputRevision, inputAcknowledged, Options, IsDrawing, drawSession,
                pressEpoch, capturePending ? captureToken : 0, captureArmedAt.AddSeconds(6), overlay.Handle, annotations.Handle);
        }
        private void PublishInputState()
        {
            if (hooks != null && !processingInput && !exiting) hooks.Publish(CreateInputSnapshot());
        }
        private void ProcessInput(InputCommand command)
        {
            processingInput = true;
            try
            {
                if (command.Button != null && command.Kind != InputKind.Capture) NotifyPhysicalButton(command.Button);
                switch (command.Kind)
                {
                    case InputKind.Move: MouseMoved(command.X, command.Y); break;
                    case InputKind.Click:
                        if (command.Button == "Gauche" || command.Button == "Droit") MouseClicked(command.X, command.Y, command.Button == "Droit");
                        break;
                    case InputKind.Action:
                        ExecutePress(new PressCommand { Button = command.Button, Action = command.Action, X = command.X, Y = command.Y, Long = command.Long });
                        break;
                    case InputKind.Drawing:
                        MouseMoved(command.X, command.Y);
                        if (IsDrawing && command.Session == drawSession) DrawingInput(command.Code, command.X, command.Y, command);
                        break;
                    case InputKind.Key: KeyPressed(command.Key, command.Modifiers); break;
                    case InputKind.Capture:
                        if (capturePending && command.CaptureToken == captureToken)
                        { capturePending = false; NotifyPhysicalButton(command.Button); }
                        break;
                    case InputKind.CancelStroke:
                        if (command.Session == drawSession)
                        {
                            if (CurrentStroke != null) Strokes.Remove(CurrentStroke);
                            CurrentStroke = null;
                            if (IsDrawing) ToggleDrawing();
                            ConfigStore.SetNotice(L.T("Le dessin a été interrompu car l’application était occupée. Tu peux reprendre le tracé."));
                        }
                        break;
                }
            }
            finally
            {
                inputAcknowledged = command.Sequence;
                processingInput = false;
                PublishInputState();
            }
        }

        public void ShowSettings()
        {
            EffectDiagnostics.Event("SHOW_SETTINGS", "USER_INTERFACE");
            if (settings == null || settings.IsDisposed)
            {
                settings = new PremiumStudioForm(this);
                settings.FormClosed += delegate { settings = null; };
            }
            settings.Show();
            // Correctif : ne plus forcer la sortie du plein écran à chaque ouverture.
            if (settings.WindowState == FormWindowState.Minimized) settings.WindowState = FormWindowState.Normal;
            settings.Activate();
            // Le HWND du panneau de réglages vient d'être activé : remonter
            // ensuite les calques sans leur donner le focus.
            if (settings.IsHandleCreated)
                settings.BeginInvoke((Action)delegate { KeepEffectLayersVisible(); });
        }

        // Le rendu est une fenêtre topmost transparente indépendante.
        // L'activation du panneau WinForms peut modifier la pile des HWND.
        // On garantit l'ordre annotations -> effets -> curseur tout en gardant
        // l'interface cliquable (WS_EX_TRANSPARENT / HTTRANSPARENT).
        public void KeepEffectLayersVisible()
        {
            if (!Options.Enabled) return;
            const uint flags = Native.SWP_NOMOVE | Native.SWP_NOSIZE |
                Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER;
            if (annotations.IsHandleCreated && annotations.Visible)
            {
                bool success = Native.SetWindowPos(annotations.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, flags);
                int error = success ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                EffectDiagnostics.Event(success ? "ZORDER_ANNOTATION_OK" : "ZORDER_ANNOTATION_FAILED", "win32=" + error);
            }
            if (overlay.IsHandleCreated && overlay.Visible)
            {
                bool success = Native.SetWindowPos(overlay.Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, flags);
                int error = success ? 0 : System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                EffectDiagnostics.Event(success ? "ZORDER_EFFECTS_OK" : "ZORDER_EFFECTS_FAILED", "win32=" + error);
            }
            toast.Raise();
        }

        // Observation de l'ordre Z uniquement ; aucun changement de fenêtre.
        private int VisibleTopmostAboveOverlay()
        {
            if (!overlay.IsHandleCreated) return -1;
            int count = 0;
            IntPtr current = Native.GetTopWindow(IntPtr.Zero);
            for (int seen = 0; seen < 512 && current != IntPtr.Zero; seen++)
            {
                if (current == overlay.Handle) return count;
                if (Native.IsWindowVisible(current) &&
                    (Native.GetWindowLong(current, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0)
                    count++;
                current = Native.GetWindow(current, Native.GW_HWNDNEXT);
            }
            return -1; // Handle absent de la fenêtre d'observation, sans déduction.
        }

        public void Changed()
        {
            timer.FrameRate = Options.FrameRate;
            if (lastAutoStart != Options.LaunchAtLogin)
            {
                if (ConfigStore.ApplyAutoStart(Options.LaunchAtLogin))
                    lastAutoStart = Options.LaunchAtLogin;
                else
                    Options.LaunchAtLogin = lastAutoStart;
            }
            savePending = true;
            // Laisse l'anneau visible pendant la manipulation de ses réglages.
            lastPointerMotion = DateTime.UtcNow;
            lastRingVisibilityStep = -1;
            forceRender = true;
            lastSettingChange = DateTime.UtcNow;
            if (SettingsChanged != null) SettingsChanged();
            PublishInputState();
        }

        internal int DrawingSession { get { return drawSession; } }
        private int drawSession;   // change à chaque arrêt / pause : invalide les continuations en attente
        public void ToggleEnabled()
        {
            Options.Enabled = !Options.Enabled;
            if (!Options.Enabled)
            {
                drawSession++; ResetPressState();
                IsDrawing = false;
                CurrentStroke = null;
                trailHistory.Clear(); Ripples.Clear();
            }
            previousPoint = Cursor.Position; trailHistory.ResetInput(); previousMove = DateTime.UtcNow;
            annotationsDirty = true; forceRender = true;
            Changed();
            tray.Refresh();
        }

        public void ToggleDrawing()
        {
            if (!IsDrawing && !Options.Enabled) { Options.Enabled = true; Changed(); }
            IsDrawing = !IsDrawing;
            drawSession++;
            trailHistory.Clear(); Ripples.Clear();
            previousPoint = Cursor.Position; trailHistory.ResetInput();   // pas de segment entre deux sessions
            CurrentStroke = null;
            annotationsDirty = true; forceRender = true;
            tray.Refresh();
            if (!IsDrawing) RestoreSettingsAfterDrawing();
            if (SettingsChanged != null) SettingsChanged();
            PublishInputState();
        }

        // « Dessiner sur le Bureau » : masque le Studio puis le réaffiche
        // automatiquement quand le dessin s'arrête (Échap, clic droit, raccourci).
        private bool reopenSettingsAfterDrawing;
        public void DrawOnDesktop()
        {
            if (!IsDrawing) ToggleDrawing();
            if (settings != null && !settings.IsDisposed && settings.Visible)
            {
                reopenSettingsAfterDrawing = true;
                settings.Hide();
            }
        }
        private void RestoreSettingsAfterDrawing()
        {
            if (!reopenSettingsAfterDrawing) return;
            reopenSettingsAfterDrawing = false;
            // Jamais d'ouverture de fenêtre directement dans le hook souris/clavier.
            if (overlay.IsHandleCreated) overlay.BeginInvoke((Action)delegate { ShowSettings(); });
        }

        // Bouton de souris « Outil de dessin suivant » : fait défiler les outils en boucle
        // et affiche brièvement l'outil actif en haut à gauche de l'écran.
        public void StepDrawingTool(int delta)
        {
            if (delta == 0) return;
            string[] cycle = Options.DrawingToolCycle;
            if (cycle == null || cycle.Length == 0) cycle = DrawingTools.Names;
            int current = Array.IndexOf(cycle, Options.AnnotationTool);
            // A tool selected directly in Studio may be outside the button cycle.
            // Enter at the first selected tool going forward, or the last going back.
            int next = current < 0 ? (delta < 0 ? cycle.Length - 1 : 0) :
                (int)(((long)current + delta) % cycle.Length + cycle.Length) % cycle.Length;
            Options.AnnotationTool = cycle[next];
            int toastIndex = DrawingTools.IndexOf(Options.AnnotationTool);
            // Même règle que dans le Studio : « Auto-formes » exige les formes assistées.
            if (Options.AnnotationTool == "Forme libre" && Options.DrawSmoothMode != "Formes assistées")
                Options.DrawSmoothMode = "Formes assistées";
            Changed();
            // Jamais de création / affichage de fenêtre dans le hook souris (délai de 300 ms).
            Point where = CursorPoint;
            if (overlay.IsHandleCreated)
                overlay.BeginInvoke((Action)delegate { toast.Announce(toastIndex, where); });
        }

        public void UndoDrawing()
        {
            if (Strokes.Count == 0) return;
            int index = Strokes.Count - 1;
            RedoStrokes.Add(Strokes[index]);
            Strokes.RemoveAt(index);
            CurrentStroke = null;
            annotationsDirty = true;
            RefreshAnnotationNumber();
            if (SettingsChanged != null) SettingsChanged();
        }
        public void RedoDrawing()
        {
            if (RedoStrokes.Count == 0) return;
            int index = RedoStrokes.Count - 1;
            Strokes.Add(RedoStrokes[index]);
            RedoStrokes.RemoveAt(index);
            annotationsDirty = true;
            RefreshAnnotationNumber();
            if (SettingsChanged != null) SettingsChanged();
        }
        private void RefreshAnnotationNumber()
        {
            annotationNumber = 0;
            foreach (AnnotationStroke s in Strokes)
                if (s.Tool == "Numéro") annotationNumber = Math.Max(annotationNumber, s.Number);
        }
        public void RestoreLastOriginalStroke()
        {
            for (int i = Strokes.Count - 1; i >= 0; i--)
            {
                AnnotationStroke s = Strokes[i];
                if (s.OriginalPoints == null || s.OriginalPoints.Count == 0) continue;
                s.Points = new List<Point>(s.OriginalPoints);
                if (!String.IsNullOrEmpty(s.OriginalTool)) s.Tool = s.OriginalTool;
                s.OriginalPoints.Clear(); s.OriginalTool = ""; s.Smoothed = false;
                annotationsDirty = true;
                if (SettingsChanged != null) SettingsChanged();
                return;
            }
        }
        public void ClearDrawings()
        {
            Strokes.Clear();
            RedoStrokes.Clear();
            CurrentStroke = null;
            annotationNumber = 0;
            annotationsDirty = true;
            if (SettingsChanged != null) SettingsChanged();
        }
        public void ExportDrawings()
        {
            if (Strokes.Count == 0)
            {
                MessageBox.Show(L.T("Aucun dessin à exporter."), "Haloryva", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = L.T("Image PNG transparente|*.png");
                dialog.FileName = "Haloryva_annotations.png";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    try { overlay.ExportAnnotations(dialog.FileName); }
                    catch (Exception ex) { MessageBox.Show(L.T("Export impossible : ") + ex.Message, "Haloryva", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
            }
        }
        public void ReplaceOptions(Config next)
        {
            Options = next;
            Options.Normalize();
            Changed();
        }
        private string ButtonAction(string button)
        {
            if (button == "Gauche") return Options.ButtonLeft;
            if (button == "Droit") return Options.ButtonRight;
            if (button == "Milieu") return Options.ButtonMiddle;
            if (button == "X1") return Options.ButtonX1;
            return Options.ButtonX2;
        }
        public bool IsCursorFlowForeground()
        {
            IntPtr active = Native.GetForegroundWindow();
            if (active == IntPtr.Zero) return false;
            uint pid;
            Native.GetWindowThreadProcessId(active, out pid);
            return pid == Native.GetCurrentProcessId();
        }
        private string ButtonLongAction(string button)
        {
            if (button == "Gauche") return Options.ButtonLeftLong;
            if (button == "Droit") return Options.ButtonRightLong;
            if (button == "Milieu") return Options.ButtonMiddleLong;
            if (button == "X1") return Options.ButtonX1Long;
            return Options.ButtonX2Long;
        }

        // Appui court / long : actions et seuil figés à l'appui.
        public void ResetPressState() { pressEpoch++; }
        private void ExecutePress(PressCommand command)
        {
            if (command == null) return;
            if (command.Long) Ripples.Add(new Ripple(new Point(command.X, command.Y), DateTime.UtcNow, false));
            if (String.IsNullOrEmpty(command.Action) || command.Action == "Normal")
            {
                if (overlay.IsHandleCreated) overlay.BeginInvoke((Action)delegate {
                    if (!Native.SendMouseClick(command.Button))
                        ConfigStore.SetNotice(L.T("Windows a refusé le rejeu du clic. Aucune protection n'a été contournée."));
                });
            }
            else RunAction(command.Action, command.X, command.Y);
        }
        private void CheckLongPresses(DateTime now)
        {
            if (capturePending && (now - captureArmedAt).TotalSeconds > 6)
            { capturePending = false; NotifyPhysicalButton("Aucun"); }
            PublishInputState();
        }

        public bool HandleMouseButton(string button, bool pressed, int x, int y)
        {
            string action = ButtonAction(button);
            if (String.IsNullOrEmpty(action) || action == "Normal") return false;
            if (!pressed) return true;
            RunAction(action, x, y);
            return true; // Un bouton configuré n'agit plus dans l'application en dessous.
        }

        private void RunAction(string action, int x, int y)
        {
            switch (action)
            {
                case "Dessin : activer / arrêter": ToggleDrawing(); break;
                case "Outil de dessin suivant": StepDrawingTool(1); break;
                case "Outil de dessin précédent": StepDrawingTool(-1); break;
                case "Annuler le dernier trait": UndoDrawing(); break;
                case "Rétablir un trait": RedoDrawing(); break;
                case "Effacer les dessins": ClearDrawings(); break;
                case "Traînée : activer / arrêter": Options.Trail = !Options.Trail; Changed(); break;
                case "Halo : activer / arrêter": Options.Ring = !Options.Ring; Changed(); break;
                case "Effets : pause / reprise": ToggleEnabled(); break;
                case "Effet de clic": Ripples.Add(new Ripple(new Point(x, y), DateTime.UtcNow, false)); break;
                case "Traînée suivante":
                    string[] styles = { "Particules", "Ligne", "Points", "Pointillés", "Lumière", "Étoiles", "Kawaii", "Logo" };
                    int pos = Array.IndexOf(styles, Options.TrailStyle);
                    Options.TrailStyle = styles[(pos + 1) % styles.Length];
                    Options.Trail = true; Changed(); break;
                case "Ouvrir les réglages":
                    if (overlay.IsHandleCreated) overlay.BeginInvoke((Action)delegate { ShowSettings(); });
                    break;
            }
        }

        public void MouseMoved(int x, int y)
        {
            Point current = new Point(x, y);
            if (current != CursorPoint)
            {
                lastPointerMotion = DateTime.UtcNow;
                // Si le curseur réapparaît, reprendre immédiatement le rendu.
                forceRender = true;
            }
            CursorPoint = current;
            trailHistory.Move(current, TrailClock.Now, Options.TrailSpacing, Options.TrailLife,
                Options.Enabled && Options.Trail && !IsDrawing);
            if (!Options.Enabled) return;
            DateTime now = DateTime.UtcNow;
            double distance = Math.Sqrt(Math.Pow(x - previousPoint.X, 2) + Math.Pow(y - previousPoint.Y, 2));
            if (Options.Shake && distance > 14 && (now - previousMove).TotalMilliseconds < 220)
            {
                int direction = x > previousPoint.X ? 1 : -1;
                if (Math.Abs(x - previousPoint.X) > Math.Abs(y - previousPoint.Y) && direction != lastDirection && lastDirection != 0)
                    directionChanges++;
                lastDirection = direction;
                if (directionChanges >= 5 && (now - lastShake).TotalMilliseconds > 1400)
                {
                    ShakeUntil = now.AddMilliseconds(850);
                    lastShake = now;
                    directionChanges = 0;
                }
            }
            if ((now - previousMove).TotalMilliseconds > 700) directionChanges = 0;
            previousMove = now;
            previousPoint = current;
        }

        public void MouseClicked(int x, int y, bool right)
        {
            if (!Options.Enabled || IsDrawing) return;
            DateTime now = DateTime.UtcNow;
            if (Options.Clicks && Options.ClickOnMouse)
                Ripples.Add(new Ripple(new Point(x, y), now, right));
            if (Options.ZoomOnClick) ZoomUntil = now.AddMilliseconds(850);
            if (Options.ClickSounds)
            {
                try
                {
                    if (!String.IsNullOrEmpty(Options.CustomSound) && File.Exists(Options.CustomSound))
                    {
                        if (customPlayer == null || customPlayerPath != Options.CustomSound)
                        {
                            if (customPlayer != null) customPlayer.Dispose();
                            customPlayer = new SoundPlayer(Options.CustomSound);
                            customPlayerPath = Options.CustomSound;
                        }
                        customPlayer.Play();
                    }
                    else if (right) SystemSounds.Exclamation.Play();
                    else SystemSounds.Asterisk.Play();
                }
                catch { }
            }
        }

        public void DrawingInput(int code, int x, int y)
        { DrawingInput(code, x, y, null); }

        private void DrawingInput(int code, int x, int y, InputCommand input)
        {
            if (code == Native.WM_LBUTTONDOWN)
            {
                annotationsDirty = true;
                CurrentStroke = new AnnotationStroke();
                CurrentStroke.Tool = input == null ? Options.AnnotationTool : input.DrawingTool;
                CurrentStroke.Color = Visuals.ParseColor(input == null ? Options.AnnotationColor : input.DrawingColor, Color.DeepPink);
                CurrentStroke.Width = input == null ? Options.AnnotationThickness : input.DrawingWidth;
                CurrentStroke.Points.Add(new Point(x, y));
                if (CurrentStroke.Tool == "Numéro") CurrentStroke.Number = ++annotationNumber;
                Strokes.Add(CurrentStroke);
                RedoStrokes.Clear();
            }
            else if (code == Native.WM_MOUSEMOVE && CurrentStroke != null)
            {
                // Points reçus dans l'ordre depuis le hook ; rendu limité à la fréquence UI.
                annotationsDirty = true;
                if (CurrentStroke.Tool == "Stylo" || CurrentStroke.Tool == "Surligneur" ||
                    CurrentStroke.Tool == "Flèche libre" || CurrentStroke.Tool == "Forme libre")
                {
                    Point last = CurrentStroke.Points[CurrentStroke.Points.Count - 1];
                    // Evite des milliers de points par seconde sur les souris haute frequence.
                    if ((x - last.X) * (long)(x - last.X) + (y - last.Y) * (long)(y - last.Y) >= 9)
                        CurrentStroke.Points.Add(new Point(x, y));
                }
                else if (CurrentStroke.Points.Count == 1)
                    CurrentStroke.Points.Add(new Point(x, y));
                else
                    CurrentStroke.Points[1] = new Point(x, y);
            }
            else if (code == Native.WM_LBUTTONUP)
            {
                annotationsDirty = true;
                AnnotationStroke finished = CurrentStroke;
                CurrentStroke = null;
                if (finished != null && finished.Points.Count > 1 &&
                    (finished.Tool == "Stylo" || finished.Tool == "Surligneur" ||
                     finished.Tool == "Flèche libre" || finished.Tool == "Forme libre"))
                {
                    Point last = finished.Points[finished.Points.Count - 1];
                    if (Math.Abs(x - last.X) + Math.Abs(y - last.Y) >= 3)
                        finished.Points.Add(new Point(x, y));
                }
                if (finished != null && finished.Points.Count > 2 &&
                    (finished.Tool == "Stylo" || finished.Tool == "Surligneur" ||
                     finished.Tool == "Flèche libre" || finished.Tool == "Forme libre") &&
                    Options.DrawSmoothMode != "Désactivé")
                {
                    finished.OriginalTool = finished.Tool;
                    finished.OriginalPoints = new List<Point>(finished.Points);
                    // Correctif : la reconnaissance se fait sur le geste BRUT (le lissage
                    // préalable arrondissait les coins et faussait cercles / triangles).
                    bool shaped = Options.DrawSmoothMode == "Formes assistées" &&
                        (finished.Tool == "Forme libre" || finished.Tool == "Stylo") &&
                        DrawingGeometry.TryAssist(finished);
                    finished.Smoothed = !shaped && Options.DrawSmoothStrength > 0;
                    if (finished.Smoothed)
                        finished.Points = DrawingGeometry.Smooth(finished.Points, Options.DrawSmoothStrength);
                }
                if (finished != null && finished.Tool == "Texte")
                {
                    IsDrawing = false;
                    int session = ++drawSession;
                    overlay.BeginInvoke((Action)delegate
                    {
                        using (TextPrompt dialog = new TextPrompt())
                        {
                            DialogResult outcome = settings == null || settings.IsDisposed ? dialog.ShowDialog() : dialog.ShowDialog(settings);
                            if (outcome == DialogResult.OK && !String.IsNullOrWhiteSpace(dialog.Value))
                                finished.Text = dialog.Value;
                            else Strokes.Remove(finished);
                            annotationsDirty = true;
                        }
                        // Reprendre le dessin seulement si rien ne l'a arrêté ou mis en pause entre-temps.
                        if (session == drawSession && Options.Enabled) IsDrawing = true;
                        tray.Refresh();
                        PublishInputState();
                    });
                }
            }
            else if (code == Native.WM_RBUTTONDOWN)
            {
                annotationsDirty = true;
                if (CurrentStroke != null) Strokes.Remove(CurrentStroke);
                CurrentStroke = null;
                IsDrawing = false;
                drawSession++;
                previousPoint = Cursor.Position; trailHistory.ResetInput();
                tray.Refresh();
                RestoreSettingsAfterDrawing();
                if (SettingsChanged != null) SettingsChanged();
            }
        }

        // true = raccourci réservé à Haloryva : ne pas transmettre à l'application active.
        public bool KeyPressed(Keys key, bool rightAltDown)
        {
            if (KeyboardPolicy.IsTextEntry(key, rightAltDown)) return false;
            return KeyPressed(key, new InputModifiers {
                Ctrl = (Native.GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0,
                Alt = (Native.GetAsyncKeyState((int)Keys.Menu) & 0x8000) != 0,
                Shift = (Native.GetAsyncKeyState((int)Keys.ShiftKey) & 0x8000) != 0,
                Win = (Native.GetAsyncKeyState((int)Keys.LWin) & 0x8000) != 0 || (Native.GetAsyncKeyState((int)Keys.RWin) & 0x8000) != 0,
                RightAlt = rightAltDown });
        }
        private bool KeyPressed(Keys key, InputModifiers modifiers)
        {
            if (KeyboardPolicy.IsTextEntry(key, modifiers.RightAlt)) return false;
            DateTime now = DateTime.UtcNow;
            bool ctrl = modifiers.Ctrl, alt = modifiers.Alt, shift = modifiers.Shift, win = modifiers.Win;
            if (ctrl && alt && !win)
            {
                if (key == Keys.R) { Options.ResetButtons(); ResetPressState(); Changed(); return true; }
                if (key == Keys.M) { ToggleEnabled(); return true; }
                // Raccourci de secours si un ancien profil a décalé le halo.
                if (key == Keys.H) { Options.RingOffsetX = 0; Options.RingOffsetY = 0; Changed(); return true; }
                if (key == Keys.D) { ToggleDrawing(); return true; }
                if (key == Keys.C) { ClearDrawings(); return true; }
                if (key == Keys.Back) { if (shift) RedoDrawing(); else UndoDrawing(); return true; }
                if (key == Keys.L) { Options.Loupe = !Options.Loupe; Changed(); return true; }
                if (key == Keys.F) { Options.Focus = !Options.Focus; Changed(); return true; }
                if (key == Keys.K) { Options.KeyDisplay = !Options.KeyDisplay; Changed(); return true; }
                if (key == Keys.Z) { Options.ZoomOnClick = !Options.ZoomOnClick; Changed(); return true; }
                if (key == Keys.X) { Ripples.Add(new Ripple(CursorPoint, now, false)); return true; }
            }
            if (key == Keys.Escape && capturePending) { CancelButtonCapture(); return true; }
            if (key == Keys.Escape && IsDrawing) { ToggleDrawing(); return true; }
            if (ctrl && !alt && key == Keys.Z && IsDrawing) { UndoDrawing(); return true; }
            if (ctrl && !alt && key == Keys.Y && IsDrawing) { RedoDrawing(); return true; }
            if (!Options.KeyDisplay || !Options.Enabled) return false;
            // Confidentialité : seuls les raccourcis à modificateur sont affichés.
            if (ctrl || alt || win)
            {
                if (KeyboardPolicy.IsModifier(key)) return false;
                LastKeys = (ctrl ? "Ctrl + " : "") + (alt ? "Alt + " : "") + (shift ? L.T("Maj + ") : "") + (win ? "Win + " : "") + key.ToString();
                KeysUntil = now.AddMilliseconds(1100);
            }
            return false;
        }

        private bool wasAnimating;
        private void RenderTick()
        {
            hooks.Drain();
            EffectDiagnostics.RenderTick();
            if (EffectDiagnostics.ShouldSample())
            {
                bool hasHandle = overlay.IsHandleCreated;
                int style = hasHandle ? Native.GetWindowLong(overlay.Handle, Native.GWL_EXSTYLE) : 0;
                EffectDiagnostics.Sample(Options.Enabled, hasHandle, overlay.Visible,
                    hasHandle && Native.IsWindowVisible(overlay.Handle),
                    hasHandle && (style & Native.WS_EX_TOPMOST) != 0,
                    VisibleTopmostAboveOverlay(), overlay.LastPublished);
            }
            if (exitRequested != null && exitRequested.WaitOne(0)) { Exit(); return; }
            DateTime now = DateTime.UtcNow;
            CheckLongPresses(now);
            string notice = ConfigStore.TakeNotice();
            if (notice != null) tray.Notify(notice);
            if (savePending && (now - lastSettingChange).TotalMilliseconds > 380)
            {
                savePending = false;
                ConfigStore.Save(Options);
            }
            trailHistory.Expire(TrailClock.Now, Options.TrailLife);
            Ripples.RemoveAll(delegate(Ripple r) { return (now - r.Time).TotalMilliseconds > Options.ClickDuration + 150; });
            if (!Options.Enabled)
            {
                overlay.Visible = false;
                annotations.Visible = false;
                return;
            }
            if (!overlay.Visible)
            {
                overlay.Show();
                if (settings != null && settings.Visible) KeepEffectLayersVisible();
            }
            bool previousTextPointer = IsTextPointer;
            if (Options.Ring && (Options.RingCenterOnText || Options.RingTextGrow))
                IsTextPointer = Native.IsSystemTextCursor();
            else IsTextPointer = false;
            // Agrandissement progressif du halo sur curseur texte (transition douce, sans à-coup).
            float growTarget = Options.Ring && Options.RingTextGrow && IsTextPointer ? 1f + Options.RingTextGrowPercent / 100f : 1f;
            bool growing = Math.Abs(growTarget - RingTextScale) > .002f;
            long growNow = growthClock.ElapsedMilliseconds;
            double delta = Math.Max(0, Math.Min(250, growNow - lastGrowth)) / 1000.0; lastGrowth = growNow;
            RingTextScale = growing ? RingTextScale + (growTarget - RingTextScale) * (float)(1 - Math.Exp(-delta / .068)) : growTarget;
            float ringVisibility = RingVisibility(now);
            int visibilityStep = (int)Math.Round(ringVisibility * 100f);
            bool fading = visibilityStep != lastRingVisibilityStep;
            lastRingVisibilityStep = visibilityStep;
            bool changing = forceRender || CursorPoint != lastRenderedPoint ||
                previousTextPointer != IsTextPointer || fading || growing;
            bool animated = (Options.Trail && Trail.Count > 0) || Ripples.Count > 0 ||
                ShakeUntil > now || ZoomUntil > now || KeysUntil > now || Options.Focus;
            // Une pulsation immobile n'exige pas 30 rendus par seconde.
            bool slowPulse = Options.Ring && ringVisibility > 0f && Options.RingPulse &&
                (now - lastEffectDraw).TotalMilliseconds >= 80;
            // Le zoom texte est rafraîchi même si le pointeur reste immobile,
            // pour suivre la saisie/le caret, au maximum environ 8 fois/s.
            bool liveTextZoom = (Options.Loupe && (now - lastEffectDraw).TotalMilliseconds >= 190) ||
                (Options.Ring && ringVisibility > 0f && (Options.RingColorMode == "Dynamique" || Options.RingColorMode == "Arc-en-ciel") &&
                 (now - lastEffectDraw).TotalMilliseconds >= 33); // Zoom abandonné : aucune capture périodique.
            // Dernière image après l'expiration du dernier effet : le calque est effacé explicitement.
            bool finalClear = wasAnimating && !animated;
            wasAnimating = animated;
            if (changing || animated || slowPulse || liveTextZoom || finalClear)
            {
                long measuredAt = TrailMetrics.BeginFrame();
                overlay.Render(now);
                TrailMetrics.Record(measuredAt, Trail.Count, overlay.LastSurfaceSize.Width, overlay.LastSurfaceSize.Height,
                    (now - lastPointerMotion).TotalMilliseconds < 150,
                    settings != null && settings.Visible && settings.WindowState != FormWindowState.Minimized,
                    Options.Trail ? Options.TrailStyle : "INACTIVE", overlay.LastPublished);
                lastRenderedPoint = CursorPoint;
                lastEffectDraw = now;
                forceRender = false;
            }
            // Le calque de dessin ne suit PAS les mouvements du curseur : aucun
            // rendu plein ecran en boucle lorsque des annotations existent.
            if (annotationsDirty && (now - lastAnnotationDraw).TotalMilliseconds >= 45)
            {
                annotations.Render();
                // Un calque d'annotation rendu/affiché peut changer l'ordre Z.
                if (settings != null && settings.Visible) KeepEffectLayersVisible();
                annotationsDirty = false;
                lastAnnotationDraw = now;
            }
        }

        public void Exit()
        {
            if (exiting) return;
            if (!ConfigStore.Save(Options))
            { tray.Notify(ConfigStore.TakeNotice() ?? L.T("Fermeture annulée : les réglages ne sont pas sauvegardés.")); return; }
            exiting = true;
            drawSession++; IsDrawing = false; CurrentStroke = null; ResetPressState();
            timer.Stop();
            if (exitRequested != null) { exitRequested.Dispose(); exitRequested = null; }
            try { TrailMetrics.Save(timer.Mode, timer.Coalesced, overlay.SurfaceCreations, overlay.FallbackFrames); }
            catch (Exception ex) { MessageBox.Show(L.T("Écriture des mesures impossible : ") + ex.Message, "Haloryva"); }
            hooks.Dispose();
            if (customPlayer != null) customPlayer.Dispose();
            overlay.Close();
            annotations.Close();
            toast.Close();
            tray.Dispose();
            if (settings != null && !settings.IsDisposed) settings.Dispose();
            EffectDiagnostics.Close();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class TrayIcon : IDisposable
    {
        private NotifyIcon icon;
        private CursorController controller;
        private ToolStripMenuItem enabled;
        private ToolStripMenuItem drawing;
        public void Notify(string text) { try { icon.ShowBalloonTip(6000, "Haloryva", text, ToolTipIcon.Warning); } catch { } }
        public TrayIcon(CursorController c)
        {
            controller = c;
            icon = new NotifyIcon();
            try { icon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { icon.Icon = SystemIcons.Information; }
            icon.Text = L.T("Haloryva — effets du curseur");
            ContextMenuStrip menu = new ContextMenuStrip();
            StudioMenuRenderer.Apply(menu);
            enabled = new ToolStripMenuItem(L.T("Effets actifs"), null, delegate { controller.ToggleEnabled(); });
            drawing = new ToolStripMenuItem(L.T("Mode annotation (Ctrl+Alt+D)"), null, delegate { controller.ToggleDrawing(); });
            menu.Items.Add(enabled);
            menu.Items.Add(new ToolStripMenuItem(L.T("Réglages..."), null, delegate { controller.ShowSettings(); }));
            menu.Items.Add(new ToolStripMenuItem(L.T("Recentrer le halo (Ctrl+Alt+H)"), null, delegate
            { controller.Options.RingOffsetX = 0; controller.Options.RingOffsetY = 0; controller.Changed(); }));
            menu.Items.Add(drawing);
            menu.Items.Add(new ToolStripMenuItem(L.T("Annuler le dernier trait"), null, delegate { controller.UndoDrawing(); }));
            menu.Items.Add(new ToolStripMenuItem(L.T("Rétablir un trait"), null, delegate { controller.RedoDrawing(); }));
            menu.Items.Add(new ToolStripMenuItem(L.T("Effacer les annotations"), null, delegate { controller.ClearDrawings(); }));
            menu.Items.Add(new ToolStripMenuItem(L.T("Exporter les dessins en PNG..."), null, delegate { controller.ExportDrawings(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(L.T("Quitter Haloryva"), null, delegate { controller.Exit(); }));
            icon.ContextMenuStrip = menu;
            icon.DoubleClick += delegate { controller.ShowSettings(); };
            icon.Visible = true;
            Refresh();
        }
        public void Refresh() { enabled.Checked = controller.Options.Enabled; drawing.Checked = controller.IsDrawing; }
        public void Dispose() { icon.Visible = false; icon.Dispose(); }
    }
}
