using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace CursorFlow
{
    // Bounded local journal. The producer only queues an immutable event line;
    // all filesystem calls belong to the worker. No window identity, title,
    // input, screen pixel or coordinate is collected.
    internal sealed class OverlayRecoveryLog
    {
        internal const int MaximumBytes = 262144;
        private const int QueueCapacity = 128;
        private readonly string path;
        private readonly BufferedDiagnosticWriter writer;
        private readonly Action beforeAppend;
        private readonly string startupError;
        private volatile bool capReached, producerFailed, completionFailed;

        private sealed class RecoveryLogCapacityReachedException : IOException { }

        internal OverlayRecoveryLog(string directory) : this(directory, null) { }

        // The optional callback is a native self-test seam: it can hold the
        // actual worker before I/O without changing the format or disk backend.
        internal OverlayRecoveryLog(string directory, Action beforeAppend)
        {
            path = Path.Combine(directory, "recuperation-affichage.log");
            this.beforeAppend = beforeAppend;
            try
            {
                // Recovery events are infrequent. Single-record batches keep
                // the original exact cap boundary between complete event lines.
                writer = new BufferedDiagnosticWriter(AppendOnWorker, QueueCapacity, 1);
            }
            catch (Exception error)
            {
                // A journal thread that cannot start must not stop the effects.
                startupError = error.GetType().Name;
            }
        }

        internal bool CapReached { get { return capReached; } }

        internal void Write(RecoveryEvent entry)
        {
            if (entry == null || writer == null) return;
            try
            {
                // Timestamp captured on arrival, before queueing or disk delay.
                string text = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + ";" +
                    entry.Name + ";reason=" + entry.Reason.ToString() + ";win32=" +
                    entry.Error.ToString(CultureInfo.InvariantCulture) + ";attempts=" +
                    entry.Attempts.ToString(CultureInfo.InvariantCulture) + "\r\n";
                writer.TryEnqueue(text);
            }
            catch { producerFailed = true; }
        }

        private void AppendOnWorker(string text)
        {
            if (beforeAppend != null) beforeAppend();
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (FileStream file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
            {
                // Never truncate, rotate, delete or rewrite prior diagnostics.
                // An explicit worker fault distinguishes a capped journal from
                // a fully persisted one. The queued records remain accounted.
                if (file.Length + bytes.Length > MaximumBytes)
                {
                    capReached = true;
                    throw new RecoveryLogCapacityReachedException();
                }
                file.Position = file.Length;
                file.Write(bytes, 0, bytes.Length);
            }
        }

        // Called only after animation/hooks have stopped, never from Write.
        internal bool Close()
        {
            if (writer == null) return true;
            try { return writer.Complete(1500); }
            catch { completionFailed = true; return false; }
        }

        internal DiagnosticWriterSnapshot GetWriterSnapshot()
        {
            DiagnosticWriterSnapshot snapshot = writer == null ? new DiagnosticWriterSnapshot {
                Started = false, Accepting = false, Completed = true, Faulted = true,
                JournalComplete = false, ErrorCode = startupError ?? "WriterUnavailable"
            } : writer.GetSnapshot();
            if (producerFailed || completionFailed)
            {
                snapshot.Faulted = true;
                snapshot.JournalComplete = false;
                if (String.IsNullOrEmpty(snapshot.ErrorCode))
                    snapshot.ErrorCode = completionFailed ? "RecoveryLogCloseFailed" : "RecoveryLogEnqueueFailed";
            }
            return snapshot;
        }
    }
}
