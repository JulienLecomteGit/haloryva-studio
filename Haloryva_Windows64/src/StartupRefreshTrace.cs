using System;
using System.IO;
using System.Text;
namespace CursorFlow
{
    // Opt-in recipe diagnostics: no profile content, input events, network or normal-launch log.
    internal static class StartupRefreshTrace
    {
        private static readonly bool enabled = Array.IndexOf(Environment.GetCommandLineArgs(), "--startup-refresh-trace") >= 0;
        private static readonly object gate = new object();
        internal static void Record(string stage, int generation, int state)
        {
            if (!enabled) return;
            try
            {
                lock (gate)
                {
                    string folder = Path.Combine(ConfigStore.DirectoryPath, "Logs");
                    Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, "startup-refresh-recipe.tsv");
                    if (File.Exists(path) && new FileInfo(path).Length > 524288) return;
                    File.AppendAllText(path, DateTime.UtcNow.ToString("o") + "\t" + stage + "\t" +
                        generation + "\t" + state + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch { } // Diagnostics must never alter normal application behavior.
        }
    }
}
