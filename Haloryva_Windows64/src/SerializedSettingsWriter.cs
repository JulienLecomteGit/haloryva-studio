using System;
using System.Threading;

namespace CursorFlow
{
    internal delegate bool SettingsSnapshotSave(string snapshot, out string error);

    // Receives immutable JSON captured on the UI thread. One I/O operation and at
    // most one latest pending snapshot; no Control or shared Config reaches a worker.
    internal sealed class SerializedSettingsWriter : IDisposable
    {
        private readonly object sync = new object();
        private readonly ManualResetEvent idle = new ManualResetEvent(true);
        private readonly SettingsSnapshotSave save;
        private string pending, errorNotice;
        private bool running, paused, disposed;
        private long requested, completed, coalesced, failed;
        internal SerializedSettingsWriter(SettingsSnapshotSave write)
        { if (write == null) throw new ArgumentNullException("write"); save = write; }
        internal long Requested { get { lock (sync) return requested; } }
        internal long Completed { get { lock (sync) return completed; } }
        internal long Coalesced { get { lock (sync) return coalesced; } }
        internal long Failed { get { lock (sync) return failed; } }
        internal bool Busy { get { lock (sync) return running; } }

        internal bool Enqueue(string snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");
            lock (sync)
            {
                if (paused || disposed) return false;
                requested++;
                if (pending != null) coalesced++;
                pending = snapshot;
                if (running) return true;
                running = true; idle.Reset();
                try
                {
                    if (ThreadPool.QueueUserWorkItem(delegate { Drain(); })) return true;
                    errorNotice = "Le worker de sauvegarde n'a pas pu demarrer.";
                }
                catch (Exception ex) { errorNotice = ex.Message; }
                running = false; pending = null; failed++; idle.Set();
                return false;
            }
        }

        private void Drain()
        {
            while (true)
            {
                string snapshot;
                lock (sync)
                {
                    snapshot = pending; pending = null;
                    if (snapshot == null)
                    {
                        running = false; idle.Set();
                        if (disposed) idle.Dispose();
                        return;
                    }
                }
                bool ok; string error;
                try { ok = save(snapshot, out error); }
                catch (Exception ex) { ok = false; error = ex.Message; }
                lock (sync)
                {
                    completed++;
                    if (!ok)
                    {
                        failed++;
                        if (errorNotice == null) errorNotice = String.IsNullOrWhiteSpace(error) ? "Ecriture refusee." : error;
                    }
                }
            }
        }

        internal string TakeError()
        { lock (sync) { string result = errorNotice; errorNotice = null; return result; } }

        // Used only for explicit import/exit. A timeout cancels that operation;
        // it never aborts an I/O or permits a synchronous write to race it.
        internal bool TryPauseAndDrain(int timeoutMs)
        {
            lock (sync) { if (disposed) return false; paused = true; }
            bool done = idle.WaitOne(Math.Max(0, timeoutMs));
            if (!done) Resume();
            return done;
        }
        internal void Resume() { lock (sync) { if (!disposed) paused = false; } }
        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = paused = true;
                if (!running) idle.Dispose();
            }
        }
    }
}
