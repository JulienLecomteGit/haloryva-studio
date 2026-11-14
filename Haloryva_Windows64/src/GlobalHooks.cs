using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CursorFlow
{
    internal sealed class GlobalHooks : IDisposable
    {
        private readonly InputMailbox mailbox = new InputMailbox(8192);
        private readonly Action<InputCommand> consume;
        private readonly Thread worker;
        private readonly Thread replayWorker;
        private readonly AutoResetEvent replayReady = new AutoResetEvent(false);
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> replays = new System.Collections.Concurrent.ConcurrentQueue<string>();
        private readonly ManualResetEvent ready = new ManualResetEvent(false);
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly InputRouter router;
        private volatile InputSnapshot snapshot;
        private volatile bool stopped, installed;
        private uint threadId;
        private int disposed, draining;
        private IntPtr mouseHook, keyboardHook;
        private Native.HookProc mouseProc, keyboardProc;
        private Exception callbackError;
        private readonly bool observationOnly;
        private long polls;
        public bool Installed { get { return installed; } }
        internal int WorkerManagedThreadId { get { return worker.ManagedThreadId; } }
        internal int Pending { get { return mailbox.Count; } }
        internal long PollCount { get { return Interlocked.Read(ref polls); } }
        internal bool WorkerAlive { get { return worker.IsAlive; } }
        internal GlobalHooks(InputSnapshot initial, Action<InputCommand> consume, bool observationOnly = false)
        {
            snapshot = initial; this.consume = consume; this.observationOnly = observationOnly;
            router = new InputRouter(mailbox, Replay, delegate { return clock.ElapsedMilliseconds; });
            replayWorker = new Thread(RunReplays) { IsBackground = true, Name = "Haloryva-rejeu" };
            worker = new Thread(Run) { IsBackground = true, Name = "Haloryva-entrees" };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            if (!ready.WaitOne(3000)) Dispose();
        }
        internal void Publish(InputSnapshot state) { snapshot = state; }
        private void Run()
        {
            System.Windows.Forms.Timer poll = null;
            try
            {
                replayWorker.Start();
                threadId = GetCurrentThreadId();
                // Force creation of the thread message queue before publishing readiness.
                MSG msg; PeekMessage(out msg, IntPtr.Zero, 0, 0, 0);
                router.Update(snapshot);
                mouseProc = MouseCallback; keyboardProc = KeyboardCallback;
                IntPtr module = Native.GetModuleHandle(null);
                mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseProc, module, 0);
                keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyboardProc, module, 0);
                installed = mouseHook != IntPtr.Zero && keyboardHook != IntPtr.Zero;
                if (!installed) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Installation des hooks impossible");
                poll = new System.Windows.Forms.Timer { Interval = 10 };
                poll.Tick += delegate { router.Update(snapshot); router.Poll(); Interlocked.Increment(ref polls); };
                poll.Start(); ready.Set();
                if (!stopped) Application.Run();
            }
            catch (Exception ex) { Interlocked.Exchange(ref callbackError, ex); }
            finally
            {
                installed = false;
                stopped = true;
                replayReady.Set();
                if (poll != null) poll.Dispose();
                if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
                if (keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyboardHook);
                mouseHook = keyboardHook = IntPtr.Zero;
                ready.Set();
            }
        }
        // Invoked only on the UI thread, at the start of a render tick. Hooks never wait for it.
        internal void Drain()
        {
            if (Interlocked.Exchange(ref draining, 1) != 0) return;
            try
            {
                Exception error = Interlocked.Exchange(ref callbackError, null);
                if (error != null) ErrorReporting.Record("INPUT_WORKER", error);
                for (int i = 0; i < 256 && !stopped; i++)
                {
                    InputCommand command = mailbox.Take();
                    if (command == null) break;
                    consume(command);
                }
            }
            finally { Interlocked.Exchange(ref draining, 0); }
        }
        private void Replay(string button)
        {
            // Never call SendInput on the hook thread: even outside a callback it
            // can stall delivery of the next physical input while replay is in flight.
            if (stopped) return;
            replays.Enqueue(button);
            replayReady.Set();
        }
        private void RunReplays()
        {
            try
            {
                while (!stopped)
                {
                    replayReady.WaitOne();
                    string button;
                    while (!stopped && replays.TryDequeue(out button))
                    {
                        // Single consumer preserves release order. The native wrapper
                        // emits one tagged DOWN/UP pair; the hook remains free to return.
                        try
                        {
                            if (!Native.SendMouseClick(button))
                                Interlocked.Exchange(ref callbackError, new InvalidOperationException("Rejeu refusé par Windows"));
                        }
                        catch (Exception ex) { Interlocked.Exchange(ref callbackError, ex); }
                    }
                }
            }
            catch (Exception ex) { Interlocked.Exchange(ref callbackError, ex); }
            finally { string unused; while (replays.TryDequeue(out unused)) { } }
        }
        private static bool OwnWindow(int x, int y, InputSnapshot state)
        {
            // Native-only hit test: no Form.Handle, Config, UI invocation or foreground heuristic.
            IntPtr hwnd = Native.GetTopWindow(IntPtr.Zero);
            for (int i = 0; hwnd != IntPtr.Zero && i < 1024; i++, hwnd = Native.GetWindow(hwnd, Native.GW_HWNDNEXT))
            {
                if (hwnd == state.Overlay || hwnd == state.Annotations || !Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd)) continue;
                if ((Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) & Native.WS_EX_TRANSPARENT) != 0) continue;
                Native.RECT r;
                if (!Native.GetWindowRect(hwnd, out r) || x < r.Left || x >= r.Right || y < r.Top || y >= r.Bottom) continue;
                uint pid; Native.GetWindowThreadProcessId(hwnd, out pid);
                return pid == Native.GetCurrentProcessId();
            }
            return false;
        }
        private IntPtr MouseCallback(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && !stopped && !observationOnly)
            {
                try
                {
                    var m = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(data, typeof(Native.MSLLHOOKSTRUCT));
                    if (m.dwExtraInfo == Native.OwnInputTag) return Native.CallNextHookEx(mouseHook, code, message, data);
                    int kind = unchecked((int)message.ToInt64());
                    string button = kind == Native.WM_LBUTTONDOWN || kind == Native.WM_LBUTTONUP ? "Gauche" :
                        kind == Native.WM_RBUTTONDOWN || kind == Native.WM_RBUTTONUP ? "Droit" :
                        kind == Native.WM_MBUTTONDOWN || kind == Native.WM_MBUTTONUP ? "Milieu" :
                        kind == Native.WM_XBUTTONDOWN || kind == Native.WM_XBUTTONUP ? ((((uint)m.mouseData >> 16) & 0xffff) == 1 ? "X1" : "X2") : null;
                    if (kind == Native.WM_MOUSEMOVE || button != null)
                    {
                        InputSnapshot state = snapshot; router.Update(state);
                        bool hitTest = kind != Native.WM_MOUSEMOVE || router.Drawing;
                        bool own = hitTest && OwnWindow(m.pt.X, m.pt.Y, state);
                        if (router.Mouse(kind, button, m.pt.X, m.pt.Y, own, false, DateTime.UtcNow)) return new IntPtr(1);
                    }
                }
                catch (Exception ex) { Interlocked.Exchange(ref callbackError, ex); }
            }
            return Native.CallNextHookEx(mouseHook, code, message, data);
        }
        private bool Down(Keys key)
        { return router.IsPressed(key) || (Native.GetAsyncKeyState((int)key) & 0x8000) != 0; }
        private IntPtr KeyboardCallback(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && !stopped && !observationOnly)
            {
                try
                {
                    int kind = unchecked((int)message.ToInt64());
                    if (kind == Native.WM_KEYDOWN || kind == Native.WM_SYSKEYDOWN || kind == Native.WM_KEYUP || kind == Native.WM_SYSKEYUP)
                    {
                        var k = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(data, typeof(Native.KBDLLHOOKSTRUCT));
                        router.Update(snapshot);
                        var mods = new InputModifiers { Ctrl = Down(Keys.LControlKey) || Down(Keys.RControlKey), Alt = Down(Keys.LMenu) || Down(Keys.RMenu),
                            Shift = Down(Keys.LShiftKey) || Down(Keys.RShiftKey), Win = Down(Keys.LWin) || Down(Keys.RWin), RightAlt = Down(Keys.RMenu) };
                        if (router.Key((Keys)k.vkCode, kind == Native.WM_KEYDOWN || kind == Native.WM_SYSKEYDOWN, mods, DateTime.UtcNow)) return new IntPtr(1);
                    }
                }
                catch (Exception ex) { Interlocked.Exchange(ref callbackError, ex); }
            }
            return Native.CallNextHookEx(keyboardHook, code, message, data);
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            stopped = true;
            replayReady.Set();
            if (threadId != 0) PostThreadMessage(threadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
            bool hookEnded = Thread.CurrentThread != worker && worker.Join(2000);
            bool replayEnded = !replayWorker.IsAlive || (Thread.CurrentThread != replayWorker && replayWorker.Join(2000));
            if (hookEnded) ready.Dispose();
            if (hookEnded && replayEnded) replayReady.Dispose();
            mailbox.Clear();
        }
        [StructLayout(LayoutKind.Sequential)] private struct MSG
        { internal IntPtr hwnd; internal uint message; internal UIntPtr wParam; internal IntPtr lParam; internal uint time; internal Native.POINT pt; internal uint lPrivate; }
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
    }
}
