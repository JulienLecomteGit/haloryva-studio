using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CursorFlow
{
    // Le worker ATTEND seulement. Tous les effets, contrôles et collections visuelles
    // restent sur le fil UI. Au plus un callback en attente/en cours, sans rattrapage.
    internal sealed class FramePump : IDisposable
    {
        private readonly Control target;
        private readonly Action tick, dispatch;
        private readonly FrameGate gate = new FrameGate();
        private readonly ManualResetEvent stop = new ManualResetEvent(false);
        private Thread worker;
        private volatile bool stopped;
        private int fps;
        private long coalesced;
        private int disposed;
        private volatile string mode = "INITIALISATION";
        internal string Mode { get { return mode; } }
        internal long Coalesced { get { return Interlocked.Read(ref coalesced); } }
        internal int FrameRate { get { return Interlocked.CompareExchange(ref fps, 0, 0); } set { Interlocked.Exchange(ref fps, Math.Max(15, Math.Min(60, value))); } }
        internal FramePump(Control owner, Action action, int rate)
        {
            target = owner; tick = action; FrameRate = rate;
            dispatch = delegate
            {
                try
                {
                    EffectDiagnostics.FrameDispatched();
                    if (!stopped) tick();
                }
                finally { gate.Leave(); }
            };
        }
        internal void Start()
        {
            if (worker != null) return;
            if (!target.IsHandleCreated) throw new InvalidOperationException("Calque non initialisé.");
            EffectDiagnostics.Event("PUMP_START", "UI_DISPATCH");
            worker = new Thread(Run); worker.IsBackground = true; worker.Name = "Haloryva-cadence"; worker.Start();
        }
        private void Run()
        {
            IntPtr timer = IntPtr.Zero;
            try
            {
                try
                {
                    // Windows10 1803+ : waitable timer haute résolution, sans timeBeginPeriod global.
                    timer = CreateWaitableTimerExW(IntPtr.Zero, null, 0x2, 0x100002);
                    mode = timer != IntPtr.Zero ? "WAITABLE_HAUTE_RESOLUTION" : "ATTENTE_STANDARD";
                }
                catch (EntryPointNotFoundException) { mode = "ATTENTE_STANDARD"; }
                catch (DllNotFoundException) { mode = "ATTENTE_STANDARD"; }
                Stopwatch clock = Stopwatch.StartNew(); FrameDeadline deadline = new FrameDeadline();
                while (!stopped)
                {
                    double delay = deadline.Delay(clock.Elapsed.TotalMilliseconds, FrameRate);
                    if (delay > 0)
                    {
                        if (timer != IntPtr.Zero)
                        {
                            long due = -Math.Max(1, (long)Math.Ceiling(delay * 10000.0));
                            if (!SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                            { EffectDiagnostics.Event("PUMP_TIMER_FALLBACK", "SET_WAITABLE_TIMER"); CloseHandle(timer); timer = IntPtr.Zero; mode = "ATTENTE_STANDARD"; continue; }
                            // L'arrêt est signalé séparément, pas par fermeture concurrente du timer.
                            IntPtr[] handles = { stop.SafeWaitHandle.DangerousGetHandle(), timer };
                            uint wait = WaitForMultipleObjects(2, handles, false, 1000);
                            if (wait == 0) break;
                            if (wait != 1) { EffectDiagnostics.Event("PUMP_TIMER_FALLBACK", "WAIT_RESULT_" + wait); CloseHandle(timer); timer = IntPtr.Zero; mode = "ATTENTE_STANDARD"; continue; }
                        }
                        else if (stop.WaitOne((int)Math.Ceiling(delay))) break;
                    }
                    if (stopped) break;
                    deadline.Advance(clock.Elapsed.TotalMilliseconds, FrameRate);
                    if (!gate.TryEnter()) { Interlocked.Increment(ref coalesced); EffectDiagnostics.FrameCoalesced(); continue; }
                    try { target.BeginInvoke(dispatch); EffectDiagnostics.FrameQueued(); }
                    catch (InvalidOperationException)
                    {
                        EffectDiagnostics.Event("PUMP_DISPATCH_FAILED", "INVALID_OPERATION");
                        gate.Leave(); break;
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorReporting.Record("FRAME_PUMP", ex);
                EffectDiagnostics.Event("PUMP_EXCEPTION", "UNEXPECTED_EXCEPTION");
                throw;
            }
            finally
            {
                EffectDiagnostics.Event("PUMP_EXIT", stopped ? "REQUESTED" : "UNEXPECTED");
                if (timer != IntPtr.Zero) CloseHandle(timer);
            }
        }
        internal void Stop() { stopped = true; stop.Set(); }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            Stop();
            // Pas de Join bloquant le worker sur Invoke synchrone (BeginInvoke uniquement).
            if (worker == null || worker.Join(1200)) stop.Dispose();
            // Dans le cas exceptionnel d'un worker bloqué, garder son handle vivant.
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr CreateWaitableTimerExW(IntPtr attrs, string name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWaitableTimer(IntPtr timer, ref long due, int period, IntPtr routine, IntPtr argument, bool resume);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForMultipleObjects(uint count, IntPtr[] handles, bool waitAll, uint milliseconds);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
