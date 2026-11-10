using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace CursorFlow.Setup
{
    internal sealed class InstallOptions
    {
        public bool DesktopShortcut = true;
        public bool AutoStart = false;
        public bool ImportCursorFlowSettings = false;  // import opt-in, never overwrite Haloryva
        public bool RemoveSettings = false;   // désinstallation : supprimer aussi réglages et sauvegardes
    }

    // Maintenance graphique du binaire déjà compilé, avec snapshots durables et reprise.
    // Aucun compilateur ni script de suppression différée sur le poste destinataire.
    internal sealed class InstallJob
    {
        public static readonly string[] InstallNames = { "Préparation", "Préparation du binaire", "Vérification", "Copie et sauvegardes", "Raccourcis et démarrage" };
        public static readonly int[] InstallWeights = { 10, 15, 25, 30, 20 };
        public static readonly string[] UninstallNames = { "Arrêt de Haloryva", "Raccourcis et démarrage", "Fichiers de l'application", "Inscription Windows" };
        public static readonly int[] UninstallWeights = { 20, 20, 40, 20 };
        public readonly string[] StepNames;
        public readonly int[] Weights;
        public readonly bool Uninstall;
        public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Haloryva";

        public readonly string Root;
        public readonly InstallOptions Options;
        public readonly bool Demo;
        public readonly string DemoFail;
        public readonly string TargetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haloryva");
        public string Target { get { return Path.Combine(TargetDir, "Haloryva.exe"); } }

        public volatile int Current = -1;
        public volatile bool Finished;
        public volatile bool Failed;
        public string FailKind = "", FailTitle = "", FailText = "", FailDetail = "";
        public readonly double[] StepStart;
        public readonly double[] StepEnd;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private Thread thread;

        public InstallJob(string root, InstallOptions options, bool demo, string demoFail, bool uninstall)
        {
            Root = root; Options = options; Demo = demo; DemoFail = demoFail; Uninstall = uninstall;
            StepNames = uninstall ? UninstallNames : InstallNames; Weights = uninstall ? UninstallWeights : InstallWeights;
            StepStart = new double[StepNames.Length]; StepEnd = new double[StepNames.Length];
            for (int i = 0; i < StepStart.Length; i++) { StepStart[i] = -1; StepEnd[i] = -1; }
        }

        public double NowMs { get { return clock.Elapsed.TotalMilliseconds; } }
        public void Start() { thread = new Thread(Run); thread.IsBackground = true; thread.Start(); }

        // Progression globale 0..1 ; l'étape en cours avance selon une courbe douce (durée réelle inconnue).
        public double Progress()
        {
            double total = 0, done = 0;
            for (int i = 0; i < Weights.Length; i++) total += Weights[i];
            for (int i = 0; i < Weights.Length; i++)
            {
                if (StepEnd[i] >= 0) done += Weights[i];
                else if (StepStart[i] >= 0)
                {
                    double t = (NowMs - StepStart[i]) / 1000.0;
                    double scale = i == 1 ? 7.0 : 1.4;
                    done += Weights[i] * Math.Min(.94, 1.0 - Math.Exp(-t / scale));
                }
            }
            return Finished && !Failed ? 1.0 : Math.Min(.995, done / total);
        }

        private void Begin(int i) { StepStart[i] = NowMs; Current = i; }
        private void End(int i) { StepEnd[i] = NowMs; }
        private sealed class StopInstall : Exception { }
        internal static string StateRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haloryva.Maintenance"); } }
        private FileTransaction transaction;
        private RegistryJournal registryBefore;
        private string MenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Haloryva.lnk"); } }
        private string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Haloryva Studio.lnk"); } }
        private bool Allowed(string path)
        {
            string prefix = Path.GetFullPath(TargetDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(path, MenuLink, StringComparison.OrdinalIgnoreCase) || String.Equals(path, DesktopLink, StringComparison.OrdinalIgnoreCase);
        }
        private void RecoverPending()
        {
            Directory.CreateDirectory(StateRoot); FileTransaction.NoLinks(StateRoot);
            foreach (string dir in Directory.GetDirectories(StateRoot, "transaction-*"))
            {
                if (!File.Exists(Path.Combine(dir, "files.xml"))) continue;
                var previous = new FileTransaction(dir, Allowed, true);
                if (previous.State != "active") continue;
                string reg = Path.Combine(dir, "registry.xml");
                RestoreTransaction(previous, File.Exists(reg) ? FileTransaction.Read<RegistryJournal>(reg) : null);
            }
        }
        private void StartTransaction()
        {
            string dir = Path.Combine(StateRoot, "transaction-" + Guid.NewGuid().ToString("N"));
            transaction = new FileTransaction(dir, Allowed, false);
            registryBefore = RegistryJournal.Capture();
            FileTransaction.Write(Path.Combine(dir, "registry.xml"), registryBefore);
        }
        private void Rollback()
        {
            if (transaction == null || transaction.State != "active") return;
            RestoreTransaction(transaction, registryBefore);
        }
        private static void RestoreTransaction(FileTransaction files, RegistryJournal registry)
        {
            var errors = new List<string>();
            // Attempt both, even when one side cannot be restored; do not close the journal early.
            try { files.Rollback(false); } catch (Exception ex) { errors.Add(ex.Message); }
            try { if (registry != null) registry.Restore(); } catch (Exception ex) { errors.Add(ex.Message); }
            if (errors.Count > 0) throw new IOException(String.Join(" ; ", errors.ToArray()));
            files.FinishRollback();
        }
        private void Fail(string kind, string title, string text, string detail)
        { FailKind = kind; FailTitle = title; FailText = text; FailDetail = detail ?? ""; throw new StopInstall(); }
        private void Run()
        {
            try
            {
                if (Demo) RunDemo(); else if (Uninstall) RunUninstall(); else RunReal();
            }
            catch (Exception ex)
            {
                Failed = true;
                if (!(ex is StopInstall))
                { FailKind = "other"; FailTitle = Uninstall ? "Désinstallation interrompue" : "Installation interrompue";
                  FailText = "L'opération n'a pas été terminée."; FailDetail = ex.GetType().Name + " : " + ex.Message; }
                bool active = transaction != null && transaction.State == "active";
                try
                {
                    Rollback();
                    if (active) FailText += " Les fichiers et l'inscription Windows précédents ont été restaurés.";
                }
                catch (Exception recovery)
                { FailText += " La restauration est incomplète. Ne supprime pas les sauvegardes de maintenance."; FailDetail += "\r\n" + recovery.Message; }
            }
            finally { Finished = true; }
        }

        private void RunDemo()
        {
            int[] ms = Uninstall ? new int[] { 700, 800, 1300, 900 } : new int[] { 700, 2600, 1300, 1100, 1200 };
            for (int i = 0; i < StepNames.Length; i++)
            {
                Begin(i); Thread.Sleep(ms[i]);
                if (DemoFail == "blocked" && i == 2) Fail("blocked", "Windows a bloqué Haloryva", "(démo)", "Démo : blocage simulé.");
                if (DemoFail == "compile" && i == 1) Fail("compile", "Compilation impossible", "(démo)", "Program.cs(12,5): error CS1002: ; expected");
                End(i);
            }
        }

        private static string Q(string s) { return "\"" + s + "\""; }
        internal static void VerifyPayload(string root)
        {
            string file = Path.Combine(root, "Haloryva.exe");
            string expected = File.ReadAllText(Path.Combine(root, "Haloryva.sha256")).Trim().ToLowerInvariant();
            if (expected.Length != 64 || FileTransaction.HashFile(file) != expected) throw new IOException("Empreinte du binaire incorrecte.");
            // Do not load the app to inspect its version.
            var version = AssemblyName.GetAssemblyName(file).Version;
            if (version.ToString(3) != Brand.Version) throw new IOException("Version du binaire et du setup différentes.");
        }
        private void RunReal()
        {
            Begin(0);
            if (!Environment.Is64BitOperatingSystem) Fail("platform", "Windows 64 bits requis", "Cette application est destinée à Windows x64.", "");
            VerifyPayload(Root);
            if (Options.ImportCursorFlowSettings) MigrationPreview.ValidateImport(TargetDir);
            if (MigrationPreview.LegacyAppRunning())
                Fail("busy", "Ferme l'ancienne application", "Une ancienne application d'effets est encore active. Quitte-la depuis l'icône près de l'horloge avant d'installer Haloryva, sans la désinstaller.", "Deux moteurs souris ne doivent pas fonctionner simultanément.");
            End(0);
            Begin(1);
            string output = Path.Combine(Root, "Haloryva.exe");
            // NO compiler, sources or external downloads on the destination PC.
            End(1);
            Begin(2);
            string log;
            try
            {
                int code = RunHidden(output, "--selftest", Root, 90000, out log);
                if (code != 0) Fail("selftest", "Vérification en échec", "Le binaire n'a pas réussi ses tests ; l'ancienne version n'est pas remplacée.", Trim(log));
            }
            catch (Win32Exception ex)
            { Fail("blocked", "Windows n'a pas autorisé le démarrage", "Ne désactive pas les protections Windows. Le binaire doit être autorisé ou signé selon la politique du PC.", ex.Message); }
            End(2);
            Begin(3);
            StopInstalledApp(Target);
            RecoverPending();
            StartTransaction();
            transaction.Replace(output, Target);
            if (Options.ImportCursorFlowSettings) MigrationPreview.CopyIfApproved(TargetDir, Root, transaction);
            string demo = Path.Combine(Root, "haloryva_demo.png");
            if (File.Exists(demo)) transaction.Replace(demo, Path.Combine(TargetDir, "Logos", "haloryva_demo.png"));
            End(3);
            Begin(4);
            RegisterUninstall(); VerifyRegistration();
            ApplyAutoStart(Options.AutoStart);
            transaction.Snapshot(MenuLink); Shortcut(MenuLink, Target);
            if (Options.DesktopShortcut) { transaction.Snapshot(DesktopLink); Shortcut(DesktopLink, Target); }
            else transaction.Delete(DesktopLink);
            if (FileTransaction.HashFile(Target) != FileTransaction.HashFile(output)) throw new IOException("Vérification finale du fichier installé en échec.");
            VerifyRegistration();
            transaction.Commit();
            End(4);
        }

        // « Applications installées » (Paramètres Windows) : clé par utilisateur, sans droits administrateur.
        private void RegisterUninstall()
        {
            string setupDir = Path.Combine(TargetDir, "Setup");
            string setupExe = Path.Combine(setupDir, "HaloryvaSetup.exe");
            string me = Assembly.GetExecutingAssembly().Location;
            transaction.Replace(me, setupExe);
            long size = 0; try { size = new FileInfo(Target).Length / 1024; } catch { }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", Brand.Product);
                k.SetValue("DisplayVersion", Brand.Version);
                k.SetValue("Publisher", Brand.Author);
                k.SetValue("DisplayIcon", Target);
                k.SetValue("InstallLocation", TargetDir);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("UninstallString", "\"" + setupExe + "\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)Math.Max(1, size), RegistryValueKind.DWord);
            }
        }

        private void VerifyRegistration()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstallKey))
            {
                if (k == null) throw new InvalidOperationException("Clé de désinstallation absente après écriture.");
                string us = k.GetValue("UninstallString") as string;
                string setupExe = Path.Combine(TargetDir, "Setup", "HaloryvaSetup.exe");
                if (String.IsNullOrEmpty(us) || us.IndexOf(setupExe, StringComparison.OrdinalIgnoreCase) < 0 || !File.Exists(setupExe))
                    throw new InvalidOperationException("Le désinstallateur n'est pas en place.");
                if (!String.Equals(k.GetValue("DisplayVersion") as string, Brand.Version)) throw new InvalidOperationException("Version inscrite incorrecte.");
            }
        }

        // Arrêt ciblé (I06) : uniquement le Haloryva.exe installé, dans la session de l'utilisateur ; fermeture propre d'abord.
        public static bool IsInstalledAppRunning(string targetExe)
        {
            int session = Process.GetCurrentProcess().SessionId;
            foreach (Process p in Process.GetProcessesByName("Haloryva"))
            {
                try
                {
                    if (p.SessionId != session) continue;
                    string path = p.MainModule.FileName;
                    if (String.Equals(Path.GetFullPath(path), Path.GetFullPath(targetExe), StringComparison.OrdinalIgnoreCase)) return true;
                }
                catch { }
            }
            return false;
        }
        public static void StopInstalledApp(string targetExe)
        {
            int session = Process.GetCurrentProcess().SessionId;
            foreach (Process p in Process.GetProcessesByName("Haloryva")) using (p)
            {
                string path;
                try { if (p.SessionId != session) continue; path = p.MainModule.FileName; }
                catch (Exception ex) { throw new IOException("Impossible d'identifier un processus Haloryva ; ferme-le depuis sa zone de notification.", ex); }
                if (!String.Equals(Path.GetFullPath(path), Path.GetFullPath(targetExe), StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using (var request = EventWaitHandle.OpenExisting("Local\\HaloryvaStudio.Exit." + p.Id)) request.Set();
                }
                catch (WaitHandleCannotBeOpenedException)
                { throw new IOException("Cette ancienne version ne gère pas la fermeture coordonnée. Clique sur son icône près de l'horloge > Quitter, puis relance l'assistant."); }
                if (!p.WaitForExit(6000)) throw new IOException("Haloryva ne s'est pas fermé. Aucun arrêt forcé n'a été effectué : quitte l'application puis réessaie.");
            }
        }
        private static List<string> SafeFiles(string directory)
        {
            var result = new List<string>();
            if (!Directory.Exists(directory)) return result;
            FileTransaction.NoLinks(directory);
            foreach (string file in Directory.GetFiles(directory)) { FileTransaction.NoLinks(file); result.Add(file); }
            foreach (string child in Directory.GetDirectories(directory)) result.AddRange(SafeFiles(child));
            return result;
        }
        private static void RemoveEmptyDirectories(string directory)
        {
            if (!Directory.Exists(directory)) return;
            FileTransaction.NoLinks(directory);
            foreach (string child in Directory.GetDirectories(directory)) RemoveEmptyDirectories(child);
            if (Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory);
        }
        private void RunUninstall()
        {
            Begin(0); StopInstalledApp(Target); RecoverPending(); StartTransaction(); End(0);
            Begin(1);
            transaction.Delete(MenuLink); transaction.Delete(DesktopLink);
            using (var run = Registry.CurrentUser.OpenSubKey(RegistryJournal.RunKey, true))
                if (run != null) run.DeleteValue("Haloryva", false);
            End(1);
            Begin(2);
            var files = Options.RemoveSettings ? SafeFiles(TargetDir) : new List<string> {
                Target, Target + ".config", Path.Combine(TargetDir, "Setup", "HaloryvaSetup.exe"),
                Path.Combine(TargetDir, "Setup", "HaloryvaSetup.exe.config"), Path.Combine(TargetDir, "Logos", "haloryva_demo.png") };
            // Back up the complete selected set before deleting any of it.
            foreach (string file in files) transaction.Snapshot(file);
            foreach (string file in files) transaction.Delete(file);
            foreach (string file in files) if (File.Exists(file)) throw new IOException("Fichier non supprimé : " + file);
            if (Options.RemoveSettings) RemoveEmptyDirectories(TargetDir);
            else
            {
                foreach (string dir in new string[] { Path.Combine(TargetDir, "Setup"), Path.Combine(TargetDir, "Logos"), TargetDir })
                    if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
            }
            End(2);
            Begin(3);
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
                if (k != null) throw new IOException("L'entrée Windows est encore présente.");
            transaction.Commit();
            // No cmd.exe, arbitrary delay or late cleanup targeting a future installation.
            // Clean historical maintenance backups only after verified uninstall, while lock is held.
            foreach (string dir in Directory.GetDirectories(StateRoot, "transaction-*"))
            { FileTransaction.NoLinks(dir); Directory.Delete(dir, true); }
            End(3);
        }

        private static string Trim(string log)
        {
            if (log == null) return "";
            string[] lines = log.Replace("\r", "").Split('\n');
            StringBuilder sb = new StringBuilder(); int n = 0;
            foreach (string l in lines) { if (l.Trim().Length == 0) continue; sb.AppendLine(l.Trim()); if (++n >= 40) break; }
            return sb.ToString();
        }

        private static int RunHidden(string file, string args, string workDir, int timeoutMs, out string output)
        {
            ProcessStartInfo psi = new ProcessStartInfo(file, args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.WorkingDirectory = workDir;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8; psi.StandardErrorEncoding = Encoding.UTF8;
            StringBuilder sb = new StringBuilder();
            using (Process p = Process.Start(psi))
            {
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } lock (sb) sb.AppendLine("Délai dépassé."); output = sb.ToString(); return -1; }
                p.WaitForExit();
                lock (sb) output = sb.ToString();
                return p.ExitCode;
            }
        }

        // Raccourci .lnk via WScript.Shell (aucune référence COM à compiler).
        private void Shortcut(string path, string target)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) throw new InvalidOperationException("WScript.Shell indisponible.");
            object shell = Activator.CreateInstance(t);
            object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            Type lt = lnk.GetType();
            lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
            lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(target) });
            lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Haloryva Studio - effets et annotations de souris" });
            lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { target });
            lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
        }

        // Démarrage avec Windows : clé Run + réglage de l'application, pour que le Studio affiche le même état.
        private void ApplyAutoStart(bool on)
        {
            if (on && MigrationPreview.LegacyAutoStartActive())
                throw new IOException("Une ancienne application d'effets démarre encore avec Windows. Pour éviter deux moteurs concurrents, désactive d'abord son démarrage automatique, puis relance l'assistant Haloryva.");
            string file = Path.Combine(TargetDir, "settings.json");
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            var data = File.Exists(file) ? serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(file, Encoding.UTF8)) : new Dictionary<string, object>();
            if (data == null) throw new IOException("Préférences invalides ; aucun remplacement silencieux.");
            data["LaunchAtLogin"] = on;
            string staged = Path.Combine(Root, "settings-install.json");
            File.WriteAllText(staged, serializer.Serialize(data), new UTF8Encoding(false));
            transaction.Replace(staged, file);
            using (var run = Registry.CurrentUser.CreateSubKey(RegistryJournal.RunKey))
            {
                if (on) run.SetValue("Haloryva", "\"" + Target + "\" --silent"); else run.DeleteValue("Haloryva", false);
                bool registered = run.GetValue("Haloryva") != null;
                if (registered != on) throw new IOException("Démarrage Windows non confirmé.");
            }
        }

        public static bool AutoStartWasOn(string targetDir)
        {
            try
            {
                string file = Path.Combine(targetDir, "settings.json");
                return File.Exists(file) && Regex.IsMatch(File.ReadAllText(file), "\"LaunchAtLogin\"\\s*:\\s*true");
            }
            catch { return false; }
        }
    }
}
