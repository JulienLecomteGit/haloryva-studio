using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CursorFlow
{
    internal sealed class ProfileCandidate
    {
        internal string Path;
        internal byte[] Bytes;
        internal Config Options;
        internal string SHA256;
    }
    internal sealed class MigrationAsset
    {
        internal string Field;
        internal string FileName;
        internal byte[] Bytes;
    }
    internal sealed class MigrationPlan
    {
        internal ProfileCandidate Source;
        internal string Directory;
        internal string ExpectedDestinationHash;
        internal Config Options;
        internal List<MigrationAsset> Assets = new List<MigrationAsset>();
        internal List<string> UncopiedAssets = new List<string>();
    }
    internal static class ProfileMigration
    {
        private const int ProfileLimit = 1000000;
        private const int AssetLimit = 10 * 1024 * 1024;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(IntPtr handle, StringBuilder path, uint length, uint flags);
        internal static string Hash(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""); }
        private static string Canonical(string path)
        {
            path = Path.GetFullPath(path);
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) path = path.Substring(4);
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        private static void RejectLinks(string path)
        {
            string cursor = Path.GetFullPath(path);
            if (cursor.StartsWith(@"\\", StringComparison.Ordinal)) throw new IOException(L.T("Fichier local requis."));
            while (!String.IsNullOrEmpty(cursor))
            {
                if ((File.Exists(cursor) || Directory.Exists(cursor)) &&
                    (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException(L.T("Chemin de fichier indirect refusé."));
                cursor = Path.GetDirectoryName(cursor);
            }
        }
        private static byte[] ReadBytes(string path, int limit, bool verifyPhysicalPath)
        {
            RejectLinks(path);
            using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (verifyPhysicalPath)
                {
                    var name = new StringBuilder(32768);
                    uint n = GetFinalPathNameByHandle(f.SafeFileHandle.DangerousGetHandle(), name, (uint)name.Capacity, 0);
                    if (n == 0 || n >= name.Capacity || !String.Equals(Canonical(name.ToString()), Canonical(path), StringComparison.OrdinalIgnoreCase))
                        throw new IOException(L.T("Le fichier trouvé ne correspond pas au profil attendu."));
                }
                if (f.Length > limit) throw new InvalidDataException(L.T("Le fichier est trop volumineux."));
                using (var memory = new MemoryStream())
                {
                    byte[] buffer = new byte[8192];
                    int n;
                    while ((n = f.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (memory.Length + n > limit) throw new InvalidDataException(L.T("Le fichier est trop volumineux."));
                        memory.Write(buffer, 0, n);
                    }
                    return memory.ToArray();
                }
            }
        }
        internal static ProfileCandidate ReadCandidate(string path, bool physical)
        {
            byte[] bytes = ReadBytes(path, ProfileLimit, physical);
            string json;
            using (var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true)) json = reader.ReadToEnd();
            return new ProfileCandidate { Path = Path.GetFullPath(path), Bytes = bytes, Options = ConfigStore.Deserialize(json), SHA256 = Hash(bytes) };
        }
        internal static bool HasProfile(string directory)
        { return File.Exists(Path.Combine(directory, "settings.json")) || File.Exists(Path.Combine(directory, "settings.json.bak")); }
        internal static ProfileCandidate ReadBest(string directory, bool physical)
        {
            Exception last = null;
            foreach (string file in new[] { "settings.json", "settings.json.bak" })
            {
                string path = Path.Combine(directory, file);
                if (!File.Exists(path)) continue;
                try { return ReadCandidate(path, physical); }
                catch (Exception ex) { last = ex; }
            }
            if (last != null) throw new InvalidDataException(L.T("Aucun profil valide à reprendre."), last);
            return null;
        }
        internal static MigrationPlan Prepare(ProfileCandidate source, string directory, bool copyAssets, bool replace)
        {
            if (source == null) throw new ArgumentNullException("source");
            directory = Path.GetFullPath(directory);
            RejectLinks(directory);
            if (ProfilePaths.Overlaps(directory, source.Path)) throw new IOException(L.T("La source doit rester indépendante."));
            string target = Path.Combine(directory, "settings.json");
            if (!replace && HasProfile(directory)) throw new IOException(L.T("Des réglages existent déjà."));
            string serialized = new JavaScriptSerializer().Serialize(source.Options);
            var plan = new MigrationPlan { Source = source, Directory = directory,
                Options = ConfigStore.Deserialize(serialized),
                ExpectedDestinationHash = File.Exists(target) ? Hash(ReadBytes(target, ProfileLimit, false)) : null };
            if (copyAssets)
            {
                AddAsset(plan, "TrailLogoPath", plan.Options.TrailLogoPath, ".png");
                AddAsset(plan, "CustomSound", plan.Options.CustomSound, ".wav");
                if (plan.Assets.Count > 0)
                { plan.Options.ActivePreset = ""; plan.Options.ActivePresetSignature = ""; }
            }
            return plan;
        }
        private static void AddAsset(MigrationPlan plan, string field, string source, string extension)
        {
            if (String.IsNullOrWhiteSpace(source)) return;
            try
            {
                if (!String.Equals(Path.GetExtension(source), extension, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException();
                byte[] bytes = ReadBytes(source, AssetLimit, false);
                bool signature = extension == ".png" ?
                    bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 && bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10 :
                    bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE";
                if (!signature) throw new InvalidDataException();
                string name = Hash(bytes) + extension;
                plan.Assets.Add(new MigrationAsset { Field = field, FileName = name, Bytes = bytes });
                string target = Path.Combine(plan.Directory, "ImportedAssets", name);
                if (field == "TrailLogoPath") plan.Options.TrailLogoPath = target;
                else plan.Options.CustomSound = target;
            }
            catch { plan.UncopiedAssets.Add(field); }
        }
        internal static void Commit(MigrationPlan plan, bool replace)
        {
            if (plan == null) throw new ArgumentNullException("plan");
            RejectLinks(plan.Directory);
            string target = Path.Combine(plan.Directory, "settings.json");
            string current = File.Exists(target) ? Hash(ReadBytes(target, ProfileLimit, false)) : null;
            if (current != plan.ExpectedDestinationHash || (!replace && HasProfile(plan.Directory)))
                throw new IOException(L.T("Les réglages ont changé. Réessaie l'import."));
            Directory.CreateDirectory(plan.Directory);
            // Raw recovery copy is in the destination only; the source is never opened for writing.
            WriteContentAddressed(Path.Combine(plan.Directory, "migration-source-" + plan.Source.SHA256 + ".json"), plan.Source.Bytes);
            foreach (var asset in plan.Assets)
            {
                string folder = Path.Combine(plan.Directory, "ImportedAssets");
                RejectLinks(folder); Directory.CreateDirectory(folder);
                WriteContentAddressed(Path.Combine(folder, asset.FileName), asset.Bytes);
            }
            string temporary = Path.Combine(plan.Directory, "settings.import-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                string json = new JavaScriptSerializer().Serialize(plan.Options);
                using (var f = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] data = new UTF8Encoding(false).GetBytes(json);
                    f.Write(data, 0, data.Length); f.Flush(true);
                }
                ReadCandidate(temporary, false);
                current = File.Exists(target) ? Hash(ReadBytes(target, ProfileLimit, false)) : null;
                if (current != plan.ExpectedDestinationHash) throw new IOException(L.T("Les réglages ont changé. Réessaie l'import."));
                if (current == null) File.Move(temporary, target);
                else
                {
                    string backup = Path.Combine(plan.Directory, "settings.before-import-" + Guid.NewGuid().ToString("N") + ".json");
                    File.Replace(temporary, target, backup);
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static void WriteContentAddressed(string path, byte[] bytes)
        {
            RejectLinks(path);
            if (File.Exists(path))
            {
                if (Hash(File.ReadAllBytes(path)) != Hash(bytes)) throw new IOException("Migration recovery hash mismatch");
                return;
            }
            using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { f.Write(bytes, 0, bytes.Length); f.Flush(true); }
        }
        internal static void RecoverSamePackage()
        {
            if (!PackageContext.IsPackaged || !ProfilePaths.SystemChangesAllowed) return;
            string target = PackageContext.PackageDirectory;
            if (HasProfile(target)) return;
            ProfileCandidate old = ReadBest(PackageContext.OldPackageDirectory, false);
            if (old != null) { Commit(Prepare(old, target, false, false), false); MarkChoice("same-package"); }
        }
        private static string ChoiceFile { get { return Path.Combine(PackageContext.PackageDirectory, "legacy-import-choice.txt"); } }
        internal static void Bootstrap(bool silent)
        {
            if (!PackageContext.IsPackaged || !ProfilePaths.SystemChangesAllowed) return;
            if (HasProfile(PackageContext.PackageDirectory) && !File.Exists(ChoiceFile))
                MarkChoice("existing-package");
            RecoverSamePackage();
            if (!CanOfferLegacy()) return;
            if (silent) { MarkChoice("pending"); return; }
            OfferLegacy(null, false);
        }
        internal static bool CanOfferLegacy()
        {
            if (!PackageContext.IsPackaged || !ProfilePaths.SystemChangesAllowed) return false;
            if (File.Exists(ChoiceFile) && File.ReadAllText(ChoiceFile).Trim() != "pending") return false;
            return HasProfile(PackageContext.LegacyDirectory);
        }
        internal static void MarkChoice(string value)
        {
            Directory.CreateDirectory(PackageContext.PackageDirectory);
            File.WriteAllText(ChoiceFile, value, new UTF8Encoding(false));
        }
        internal static bool OfferLegacy(IWin32Window owner, bool explicitRequest)
        {
            if (!CanOfferLegacy() && !explicitRequest) return false;
            if (!PackageContext.IsPackaged || !ProfilePaths.SystemChangesAllowed) return false;
            string text = L.T("Copier les réglages 2.7.0 et les PNG/WAV personnalisés lisibles (10 Mio maximum par fichier) ? L'ancienne installation et son démarrage resteront inchangés.");
            if (HasProfile(PackageContext.PackageDirectory))
                text += "\n\n" + L.T("Les réglages actuels seront sauvegardés avant leur remplacement.");
            var answer = MessageBox.Show(owner, text, "Haloryva Studio", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) { MarkChoice("pending"); return false; }
            if (answer == DialogResult.No) { MarkChoice("defaults"); return false; }
            ProfilePaths.CheckLegacyMaintenance();
            var candidate = ReadBest(PackageContext.LegacyDirectory, true);
            if (candidate == null) throw new FileNotFoundException(L.T("Aucun profil valide à reprendre."));
            bool replace = HasProfile(PackageContext.PackageDirectory);
            var plan = Prepare(candidate, PackageContext.PackageDirectory, true, replace);
            if (plan.UncopiedAssets.Count > 0 && MessageBox.Show(owner,
                L.T("Certains fichiers personnalisés ne peuvent pas être copiés. Garder leurs références externes et continuer ?"),
                "Haloryva Studio", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return false;
            Commit(plan, replace);
            MarkChoice("imported");
            ConfigStore.SetNotice(L.T("Réglages copiés ; l'ancienne installation est conservée.") + " " + L.T("Le démarrage automatique se règle séparément."));
            return true;
        }
    }
}
