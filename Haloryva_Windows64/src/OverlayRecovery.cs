using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CursorFlow
{
    // Runs exclusively on the existing UI thread. Only Repair mutates a window,
    // and only the already-created effects HWND owned by this instance.
    internal sealed class OverlayRecovery : IOverlayRecoveryPlatform
    {
        private readonly Form layer;
        private readonly Func<bool> eligible;
        private readonly uint processId = Native.GetCurrentProcessId();
        private const int MaximumWindows = 1024;
        private const long ProbeBudgetMs = 12;
        private const int DwmCloaked = 14;
        private IntPtr inspectedForeground;
        private long inspectedAt;

        internal OverlayRecovery(Form layer, Func<bool> eligible)
        { this.layer = layer; this.eligible = eligible; }

        private RecoverySample Deferred(IntPtr handle, RecoveryReason reason)
        { return new RecoverySample(RecoveryState.Deferred, handle.ToInt64(), 0, reason); }
        private RecoverySample Unknown(IntPtr handle, RecoveryReason reason)
        { return new RecoverySample(RecoveryState.Unknown, handle.ToInt64(), 0, reason); }

        private bool TryLayer(out IntPtr handle, out Native.RECT bounds)
        {
            handle = IntPtr.Zero; bounds = new Native.RECT();
            // IsHandleCreated must precede Handle: no implicit handle recreation.
            if (layer == null || layer.IsDisposed || layer.Disposing || !layer.IsHandleCreated || !layer.Visible)
                return false;
            handle = layer.Handle;
            uint owner; int cloaked;
            return Native.GetWindowThreadProcessId(handle, out owner) != 0 && owner == processId &&
                Native.IsWindowVisible(handle) && !Native.IsIconic(handle) &&
                DwmGetWindowAttribute(handle, DwmCloaked, out cloaked, 4) >= 0 && cloaked == 0 &&
                Native.GetWindowRect(handle, out bounds) && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
        }

        public RecoverySample Inspect()
        {
            inspectedForeground = IntPtr.Zero;
            IntPtr handle; Native.RECT bounds;
            if (!eligible()) return Deferred(IntPtr.Zero, RecoveryReason.Ineligible);
            if (!TryLayer(out handle, out bounds)) return Unknown(handle, RecoveryReason.LayerUnavailable);
            Stopwatch budget = Stopwatch.StartNew();
            int receivesInput; uint required;
            IntPtr desktop = GetThreadDesktop(GetCurrentThreadId());
            if (desktop == IntPtr.Zero || !GetUserObjectInformationW(desktop, 6, out receivesInput, 4, out required) ||
                receivesInput == 0) return Deferred(handle, RecoveryReason.DesktopInactive);

            int notificationState;
            if (SHQueryUserNotificationState(out notificationState) < 0)
                return Unknown(handle, RecoveryReason.NativeError);
            if (notificationState == 1) return Deferred(handle, RecoveryReason.DesktopInactive);
            if (notificationState == 2 || notificationState == 3 || notificationState == 4)
                return Deferred(handle, RecoveryReason.FullScreen);

            IntPtr foreground = Native.GetForegroundWindow();
            uint foregroundPid;
            uint foregroundThread = Native.GetWindowThreadProcessId(foreground, out foregroundPid);
            if (foreground == IntPtr.Zero || foregroundThread == 0 || foregroundPid == 0)
                return Unknown(handle, RecoveryReason.ForegroundUnavailable);
            if (foregroundPid == processId) return Deferred(handle, RecoveryReason.OwnInterface);

            GuiThreadInfo info = new GuiThreadInfo(); info.Size = Marshal.SizeOf(typeof(GuiThreadInfo));
            if (!GetGUIThreadInfo(foregroundThread, ref info)) return Unknown(handle, RecoveryReason.NativeError);
            if ((info.Flags & 0x1e) != 0 || info.Capture != IntPtr.Zero || info.MoveSize != IntPtr.Zero ||
                MouseButtonDown()) return Deferred(handle, RecoveryReason.InteractionActive);

            string foregroundClass;
            if (!TryClass(foreground, out foregroundClass)) return Unknown(handle, RecoveryReason.EnumerationChanged);
            if (ProtectedClass(foregroundClass)) return Deferred(handle, RecoveryReason.PriorityWindow);
            string foregroundProcess;
            try
            {
                using (Process process = Process.GetProcessById(checked((int)foregroundPid)))
                    foregroundProcess = process.ProcessName;
            }
            catch { return Unknown(handle, RecoveryReason.ForegroundUnavailable); }
            if (CaptureProcess(foregroundProcess)) return Deferred(handle, RecoveryReason.CaptureActive);

            Native.RECT foregroundBounds;
            if (!Native.GetWindowRect(foreground, out foregroundBounds))
                return Unknown(handle, RecoveryReason.EnumerationChanged);
            Rectangle screen = Screen.FromHandle(foreground).Bounds;
            // Conservative borderless/fullscreen check. Maximized windows with an
            // auto-hidden taskbar can also defer recovery; documented, not hidden.
            if (foregroundClass != "Progman" && foregroundClass != "WorkerW" &&
                foregroundBounds.Left <= screen.Left + 2 && foregroundBounds.Top <= screen.Top + 2 &&
                foregroundBounds.Right >= screen.Right - 2 && foregroundBounds.Bottom >= screen.Bottom - 2)
                return Deferred(handle, RecoveryReason.FullScreen);

            IntPtr blocker = IntPtr.Zero;
            HashSet<IntPtr> seen = new HashSet<IntPtr>();
            IntPtr window = Native.GetTopWindow(IntPtr.Zero);
            for (int count = 0; window != IntPtr.Zero && count < MaximumWindows; count++)
            {
                if (budget.ElapsedMilliseconds > ProbeBudgetMs) return Unknown(handle, RecoveryReason.ProbeBudget);
                if (!seen.Add(window)) return Unknown(handle, RecoveryReason.EnumerationChanged);
                if (window == handle)
                {
                    Native.RECT currentBounds;
                    if (Native.GetForegroundWindow() != foreground || !Native.GetWindowRect(handle, out currentBounds) ||
                        !Equal(bounds, currentBounds)) return Unknown(handle, RecoveryReason.EnumerationChanged);
                    inspectedForeground = foreground;
                    inspectedAt = Stopwatch.GetTimestamp();
                    return new RecoverySample(blocker == IntPtr.Zero ? RecoveryState.Healthy : RecoveryState.Obscured,
                        handle.ToInt64(), blocker.ToInt64(), RecoveryReason.None);
                }
                if (Native.IsWindowVisible(window) && !Native.IsIconic(window))
                {
                    uint owner;
                    if (Native.GetWindowThreadProcessId(window, out owner) == 0)
                        return Unknown(handle, RecoveryReason.EnumerationChanged);
                    if (owner != processId)
                    {
                        Native.RECT rect;
                        if (!Native.GetWindowRect(window, out rect)) return Unknown(handle, RecoveryReason.EnumerationChanged);
                        if (Intersects(bounds, rect))
                        {
                            int cloaked;
                            if (DwmGetWindowAttribute(window, DwmCloaked, out cloaked, 4) < 0)
                                return Unknown(handle, RecoveryReason.NativeError);
                            if (cloaked == 0)
                            {
                                string className;
                                if (!TryClass(window, out className)) return Unknown(handle, RecoveryReason.EnumerationChanged);
                                int style = Native.GetWindowLong(window, Native.GWL_EXSTYLE);
                                if ((style & Native.WS_EX_TOPMOST) != 0 || ProtectedClass(className))
                                    return Deferred(handle, RecoveryReason.PriorityWindow);
                                // Desktop hosts are not an occluding ordinary application.
                                if (className != "Progman" && className != "WorkerW" && blocker == IntPtr.Zero)
                                    blocker = window;
                            }
                        }
                    }
                }
                window = Native.GetWindow(window, Native.GW_HWNDNEXT);
            }
            return Unknown(handle, RecoveryReason.EnumerationChanged);
        }

        public RecoveryRepairResult Repair(RecoverySample expected)
        {
            // Final independent guard on the UI thread. The policy already performs
            // a fresh full inspection; this refuses disposed/hidden/replaced handles.
            IntPtr handle; Native.RECT bounds;
            if (!eligible() || !TryLayer(out handle, out bounds) || handle.ToInt64() != expected.Layer)
                return new RecoveryRepairResult(RecoveryReason.LayerUnavailable);
            if (inspectedForeground == IntPtr.Zero || Native.GetForegroundWindow() != inspectedForeground ||
                (Stopwatch.GetTimestamp() - inspectedAt) * 1000.0 / Stopwatch.Frequency > 100 || MouseButtonDown())
                return new RecoveryRepairResult(RecoveryReason.EnumerationChanged);
            int receivesInput; uint required;
            if (!GetUserObjectInformationW(GetThreadDesktop(GetCurrentThreadId()), 6, out receivesInput, 4, out required) ||
                receivesInput == 0) return new RecoveryRepairResult(RecoveryReason.DesktopInactive);
            const uint flags = Native.SWP_NOMOVE | Native.SWP_NOSIZE |
                Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER;
            bool success = Native.SetWindowPos(handle, Native.HWND_TOPMOST, 0, 0, 0, 0, flags);
            return new RecoveryRepairResult(success, success ? 0 : Marshal.GetLastWin32Error());
        }

        private static bool MouseButtonDown()
        {
            return ((Native.GetAsyncKeyState(1) | Native.GetAsyncKeyState(2) | Native.GetAsyncKeyState(4) |
                Native.GetAsyncKeyState(5) | Native.GetAsyncKeyState(6)) & 0x8000) != 0;
        }
        private static bool TryClass(IntPtr hwnd, out string name)
        {
            StringBuilder value = new StringBuilder(256);
            bool found = GetClassNameW(hwnd, value, value.Capacity) > 0;
            name = value.ToString(); return found;
        }
        private static bool CaptureProcess(string name)
        {
            return String.Equals(name, "SnippingTool", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(name, "ScreenClippingHost", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(name, "SnipAndSketch", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(name, "CapturePicker", StringComparison.OrdinalIgnoreCase);
        }
        private static bool ProtectedClass(string name)
        {
            return name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd" || name == "#32768" ||
                name == "MultitaskingViewFrame" || name == "XamlExplorerHostIslandWindow" ||
                name.IndexOf("ScreenClipping", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private static bool Equal(Native.RECT a, Native.RECT b)
        { return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom; }
        private static bool Intersects(Native.RECT a, Native.RECT b)
        { return a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct GuiThreadInfo
        {
            internal int Size;
            internal uint Flags;
            internal IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
            internal Native.RECT CaretRect;
        }
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint threadId);
        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetUserObjectInformationW(IntPtr handle, int index, out int value, uint length, out uint needed);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern int GetClassNameW(IntPtr handle, StringBuilder value, int size);
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int size);
        [DllImport("shell32.dll", PreserveSig = true)]
        private static extern int SHQueryUserNotificationState(out int state);
    }
}
