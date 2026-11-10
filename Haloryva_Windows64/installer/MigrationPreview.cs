using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CursorFlow.Setup
{
    // Migration par COPIE uniquement, jamais par déplacement ou modification de l'ancien dossier.
    // Nécessite une coche explicite ; les transactions du setup protègent le nouveau dossier.
    internal static class MigrationPreview
    {
        internal static string LegacyDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CursorFlow"); } }
        internal static string LegacyFile { get { return Path.Combine(LegacyDir, "settings.json"); } }
        internal static bool LegacyPreferencesExist() { return File.Exists(LegacyFile); }

        internal static bool LegacyAutoStartActive()
        {
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RegistryJournal.RunKey))
                return run != null && run.GetValue("CursorFlow") != null;
        }

        internal static bool LegacyAppRunning()
        {
            int session = Process.GetCurrentProcess().SessionId;
            foreach (Process process in Process.GetProcessesByName("CursorFlow"))
                using (process)
                {
                    try
                    {
                        if (process.SessionId != session) continue;
                        string path = process.MainModule.FileName;
                        if (String.Equals(Path.GetFullPath(path), Path.GetFullPath(Path.Combine(LegacyDir, "CursorFlow.exe")), StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    catch (Exception ex)
                    {
                        // Cannot verify old process identity: fail closed instead of risking dual mouse hooks.
                        throw new IOException("Impossible de vérifier l'ancienne application ; quitte-la avant d'installer Haloryva.", ex);
                    }
                }
            return false;
        }

        internal static void ValidateFile(string source, string destination)
        {
            if (!File.Exists(source)) throw new IOException("Aucune ancienne préférence à importer.");
            FileTransaction.NoLinks(source);
            FileTransaction.NoLinks(destination);
            if (File.Exists(destination) || File.Exists(destination + ".bak"))
                throw new IOException("Des préférences Haloryva existent déjà. Import refusé pour éviter tout écrasement.");
            var size = new FileInfo(source).Length;
            if (size <= 0 || size > 1000000) throw new IOException("L'ancien fichier de préférences est vide ou dépasse 1 Mo.");
            string json = File.ReadAllText(source, Encoding.UTF8);
            var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            if (data == null || data.Count == 0) throw new IOException("L'ancien fichier de préférences n'est pas un JSON valide.");
        }
        internal static void ValidateImport(string targetDir)
        {
            ValidateFile(LegacyFile, Path.Combine(targetDir, "settings.json"));
        }
        // Testable avec fichiers fictifs ; le source n'est jamais modifié.
        internal static void CopyForTest(string source, string destination, FileTransaction transaction)
        {
            ValidateFile(source, destination);
            string before = FileTransaction.HashFile(source);
            transaction.Replace(source, destination);
            if (before != FileTransaction.HashFile(source) || before != FileTransaction.HashFile(destination))
                throw new IOException("Copie de préférences non conforme ; restauration requise.");
        }
        internal static void CopyIfApproved(string targetDir, string stagingRoot, FileTransaction transaction)
        {
            // stagingRoot kept as part of the signature for future transaction isolation.
            if (String.IsNullOrEmpty(stagingRoot)) throw new IOException("Dossier de travail absent.");
            CopyForTest(LegacyFile, Path.Combine(targetDir, "settings.json"), transaction);
        }
    }
}
