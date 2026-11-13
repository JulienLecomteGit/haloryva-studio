using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Storage;

namespace CursorFlow
{
    internal static class PackageContext
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFullName(ref uint length, StringBuilder name);
        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, IntPtr token, out IntPtr path);
        private static bool? packaged;
        internal static bool IsPackaged
        {
            get
            {
                if (packaged.HasValue) return packaged.Value;
                uint n = 0;
                int result = GetCurrentPackageFullName(ref n, null);
                if (result == 15700) { packaged = false; return false; }
                if (result != 122 || n == 0) throw new IOException("Package identity unavailable: " + result);
                var name = new StringBuilder((int)n);
                result = GetCurrentPackageFullName(ref n, name);
                if (result != 0) throw new IOException("Package identity unavailable: " + result);
                packaged = true; return true;
            }
        }
        internal static string RealLocalAppData
        {
            get
            {
                Guid id = new Guid("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
                IntPtr value;
                int hr = SHGetKnownFolderPath(ref id, 0x00010000, IntPtr.Zero, out value);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                try { return Marshal.PtrToStringUni(value); }
                finally { Marshal.FreeCoTaskMem(value); }
            }
        }
        internal static string LegacyDirectory { get { return Path.Combine(RealLocalAppData, "Haloryva"); } }
        internal static string PackageDirectory
        {
            get
            {
                if (!IsPackaged) throw new InvalidOperationException("Package required");
                return Path.Combine(ApplicationData.Current.LocalFolder.Path, "Haloryva");
            }
        }
        internal static string OldPackageDirectory
        {
            get
            {
                if (!IsPackaged) throw new InvalidOperationException("Package required");
                return Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "Local", "Haloryva");
            }
        }
    }
    internal static class ProfilePaths
    {
        internal static bool DiagnosticRequested { get; private set; }
        internal static void Configure(string[] args)
        {
            DiagnosticRequested = Array.IndexOf(args, "--trail-session") >= 0 ||
                Array.IndexOf(args, "--effect-diagnostics") >= 0;
            bool verified = PackageContext.IsPackaged;
        }
        internal static string NormalDirectory
        {
            get { return PackageContext.IsPackaged ? PackageContext.PackageDirectory : PackageContext.LegacyDirectory; }
        }
        internal static string CurrentDirectory
        {
            get { return TrailTrial.Active ? TrailTrial.DirectoryPath : NormalDirectory; }
        }
        internal static bool SystemChangesAllowed
        { get { return !DiagnosticRequested && !TrailTrial.Active && !EffectDiagnostics.Enabled; } }
        internal static string[] ProtectedRoots
        {
            get
            {
                var roots = new System.Collections.Generic.List<string>();
                roots.Add(PackageContext.LegacyDirectory);
                roots.Add(Path.Combine(PackageContext.RealLocalAppData, "CursorFlow"));
                if (PackageContext.IsPackaged)
                { roots.Add(PackageContext.PackageDirectory); roots.Add(PackageContext.OldPackageDirectory); }
                return roots.ToArray();
            }
        }
        internal static bool Overlaps(string a, string b)
        {
            a = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            b = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return a.Equals(b, StringComparison.OrdinalIgnoreCase) ||
                a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        internal static void CheckLegacyMaintenance()
        {
            string path = Path.Combine(PackageContext.RealLocalAppData, "Haloryva.Maintenance", "operation.lock");
            if (File.Exists(path))
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) { }
        }
    }
}
