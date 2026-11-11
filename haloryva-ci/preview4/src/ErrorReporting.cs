using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CursorFlow
{
    // No exception Message or Data: either may contain user text or file contents.
    internal sealed class RollingErrorLog
    {
        private readonly object sync = new object();
        private readonly string directory;
        private readonly int limit;
        internal RollingErrorLog(string directory, int limit)
        { this.directory = directory; this.limit = Math.Max(1024, limit); }
        internal bool Write(string category, Exception error)
        {
            try
            {
                string details = error == null ? "Unknown" : error.GetType().FullName + "\r\n" + new StackTrace(error, false);
                if (details.Length > 6000) details = details.Substring(0, 6000);
                // Categories are supplied by the application, never by an input event.
                string entry = DateTime.UtcNow.ToString("o") + " " + category + "\r\n" + details + "\r\n";
                byte[] bytes = new UTF8Encoding(false).GetBytes(entry);
                if (bytes.Length > limit) bytes = new UTF8Encoding(false).GetBytes(DateTime.UtcNow.ToString("o") + " " + category + " " + (error == null ? "Unknown" : error.GetType().FullName) + "\r\n");
                lock (sync)
                {
                    Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, "errors.log");
                    if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > limit)
                    {
                        if (File.Exists(path + ".2")) File.Delete(path + ".2");
                        if (File.Exists(path + ".1")) File.Move(path + ".1", path + ".2");
                        File.Move(path, path + ".1");
                    }
                    using (var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                        file.Write(bytes, 0, bytes.Length);
                }
                return true;
            }
            catch { return false; } // Reporting must never trigger another application failure.
        }
    }

    internal sealed class ErrorNoticeGate
    {
        private readonly Func<long> clock;
        private long last;
        private bool seen;
        internal ErrorNoticeGate(Func<long> clock) { this.clock = clock; }
        internal bool TryEnter()
        {
            lock (this)
            {
                long now = clock();
                if (seen && now - last < 30000) return false;
                last = now; seen = true; return true;
            }
        }
    }

    internal static class ErrorReporting
    {
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static readonly ErrorNoticeGate notices = new ErrorNoticeGate(delegate { return clock.ElapsedMilliseconds; });
        private static RollingErrorLog log;
        private static int showing;
        internal static void Initialize()
        { log = new RollingErrorLog(Path.Combine(ConfigStore.DirectoryPath, "Logs"), 1024 * 1024); }
        internal static void Record(string category, Exception error)
        { if (log != null) log.Write(category, error); }
        internal static void Notify(Exception error)
        {
            Record("UI", error);
            if (Interlocked.CompareExchange(ref showing, 1, 0) != 0) return;
            try
            {
                if (!notices.TryEnter()) return;
                MessageBox.Show("Une erreur est survenue dans Haloryva. Si elle persiste, ferme puis relance l’application.\n\nUn journal technique local est disponible dans le dossier Logs des réglages, si Windows autorise son écriture.",
                    "Haloryva Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { Interlocked.Exchange(ref showing, 0); }
        }
    }
}
