using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Serialization;

namespace CursorFlow.Setup
{
    public sealed class SavedFile
    {
        public string Path;
        public bool Existed;
        public int Index;
        public string Hash;
    }
    public sealed class FileJournal
    {
        public string State = "active";
        public List<SavedFile> Files = new List<SavedFile>();
        public List<string> CreatedDirectories = new List<string>();
    }
    // File snapshots are durable BEFORE mutation, never AFTER successful copying.
    // Independent of WinForms and registry so failure cases can be tested on real files.
    internal sealed class FileTransaction
    {
        internal readonly string DirectoryPath;
        private readonly Predicate<string> allowed;
        private FileJournal journal;
        internal Action<string> Fault { get; set; } // injected only by tests; never set by setup
        internal string State { get { return journal.State; } }
        internal FileTransaction(string directory, Predicate<string> isAllowed, bool recover)
        {
            DirectoryPath = Path.GetFullPath(directory); allowed = isAllowed; NoLinks(DirectoryPath);
            if (recover) journal = Read<FileJournal>(Path.Combine(DirectoryPath, "files.xml"));
            else { Directory.CreateDirectory(DirectoryPath); journal = new FileJournal(); Save(); }
        }
        internal static string HashFile(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        internal static T Read<T>(string file)
        {
            var settings = new XmlReaderSettings(); settings.DtdProcessing = DtdProcessing.Prohibit; settings.XmlResolver = null;
            using (var reader = XmlReader.Create(file, settings)) return (T)new XmlSerializer(typeof(T)).Deserialize(reader);
        }
        internal static void Write<T>(string file, T value)
        {
            string temp = file + ".new";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            { new XmlSerializer(typeof(T)).Serialize(stream, value); stream.Flush(true); }
            if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
        }
        private void Save() { Write(Path.Combine(DirectoryPath, "files.xml"), journal); }
        internal static void NoLinks(string path)
        {
            string p = Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(p))
            {
                if ((File.Exists(p) || Directory.Exists(p)) && (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Lien de dossier/fichier refusé : " + p);
                p = Path.GetDirectoryName(p);
            }
        }
        private string Safe(string path)
        {
            path = Path.GetFullPath(path);
            if (!allowed(path)) throw new IOException("Chemin hors périmètre : " + path);
            NoLinks(path); return path;
        }
        private string Backup(SavedFile f) { return Path.Combine(DirectoryPath, "file-" + f.Index.ToString("D6") + ".bin"); }
        internal void Snapshot(string path)
        {
            if (State != "active") throw new InvalidOperationException("Transaction terminée.");
            path = Safe(path);
            foreach (SavedFile old in journal.Files) if (String.Equals(old.Path, path, StringComparison.OrdinalIgnoreCase)) return;
            var item = new SavedFile { Path = path, Existed = File.Exists(path), Index = journal.Files.Count };
            if (item.Existed)
            {
                string copy = Backup(item);
                using (var input = File.OpenRead(path)) using (var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { input.CopyTo(output); output.Flush(true); }
                item.Hash = HashFile(copy);
            }
            journal.Files.Add(item); Save();
        }
        private void EnsureParent(string file)
        {
            string dir = Path.GetDirectoryName(file);
            var missing = new List<string>();
            while (!Directory.Exists(dir))
            { missing.Add(dir); dir = Path.GetDirectoryName(dir); if (String.IsNullOrEmpty(dir)) throw new IOException("Racine absente."); }
            for (int i = missing.Count - 1; i >= 0; i--)
            {
                // Recorded before creating: recovery can remove only newly created empty directories.
                if (!journal.CreatedDirectories.Contains(missing[i])) { journal.CreatedDirectories.Add(missing[i]); Save(); }
                Directory.CreateDirectory(missing[i]);
            }
        }
        internal static void AtomicCopy(string source, string target)
        {
            NoLinks(source); NoLinks(target);
            string staged = target + ".stage-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var input = File.OpenRead(source)) using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { input.CopyTo(output); output.Flush(true); }
                if (HashFile(staged) != HashFile(source)) throw new IOException("Copie incomplète : " + target);
                if (File.Exists(target)) File.Replace(staged, target, null); else File.Move(staged, target);
            }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }
        internal void Replace(string source, string target)
        {
            target = Safe(target); Snapshot(target); EnsureParent(target);
            if (Fault != null) Fault("before-replace");
            AtomicCopy(source, target);
            if (Fault != null) Fault("after-replace");
        }
        internal void Delete(string target)
        {
            target = Safe(target); Snapshot(target);
            if (Fault != null) Fault("before-delete");
            if (File.Exists(target)) File.Delete(target);
            if (File.Exists(target)) throw new IOException("Fichier encore présent : " + target);
            if (Fault != null) Fault("after-delete");
        }
        internal void Commit() { journal.State = "committed"; Save(); }
        internal void Rollback() { Rollback(true); }
        internal void FinishRollback() { journal.State = "rolledback"; Save(); }
        internal void Rollback(bool finish)
        {
            if (State != "active") return;
            var errors = new List<string>();
            for (int i = journal.Files.Count - 1; i >= 0; i--)
            {
                SavedFile item = journal.Files[i];
                try
                {
                    Safe(item.Path);
                    if (item.Index != i) throw new IOException("Journal incohérent.");
                    if (item.Existed)
                    {
                        if (!File.Exists(Backup(item)) || HashFile(Backup(item)) != item.Hash) throw new IOException("Sauvegarde incorrecte : " + item.Path);
                        Directory.CreateDirectory(Path.GetDirectoryName(item.Path)); AtomicCopy(Backup(item), item.Path);
                    }
                    else if (File.Exists(item.Path)) File.Delete(item.Path);
                }
                catch (Exception ex) { errors.Add(ex.Message); }
            }
            for (int i = journal.CreatedDirectories.Count - 1; i >= 0; i--)
            {
                string dir = journal.CreatedDirectories[i];
                try
                {
                    // Path validation uses a child file because allowed describes owned file roots.
                    Safe(Path.Combine(dir, "__directory_check__"));
                    if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
                }
                catch (Exception ex) { errors.Add(ex.Message); }
            }
            if (errors.Count > 0) throw new IOException("Restauration incomplète ; sauvegardes conservées. " + String.Join(" ; ", errors.ToArray()));
            if (finish) FinishRollback();
        }
    }
}
