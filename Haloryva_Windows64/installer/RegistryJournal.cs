using System;
using System.Collections.Generic;
using Microsoft.Win32;
namespace CursorFlow.Setup
{
    public sealed class RegistryDatum
    {
        public string Name, Text;
        public string[] Strings;
        public byte[] Bytes;
        public long Number;
        public RegistryValueKind Kind;
        internal static RegistryDatum Capture(RegistryKey key, string name)
        {
            var d = new RegistryDatum { Name = name, Kind = key.GetValueKind(name) };
            object value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (d.Kind == RegistryValueKind.DWord || d.Kind == RegistryValueKind.QWord) d.Number = Convert.ToInt64(value);
            else if (d.Kind == RegistryValueKind.MultiString) d.Strings = (string[])value;
            else if (d.Kind == RegistryValueKind.Binary || d.Kind == RegistryValueKind.None) d.Bytes = (byte[])value;
            else d.Text = (string)value;
            return d;
        }
        internal void Restore(RegistryKey key)
        {
            object value = Text;
            if (Kind == RegistryValueKind.DWord) value = (int)Number;
            else if (Kind == RegistryValueKind.QWord) value = Number;
            else if (Kind == RegistryValueKind.MultiString) value = Strings;
            else if (Kind == RegistryValueKind.Binary || Kind == RegistryValueKind.None) value = Bytes;
            key.SetValue(Name, value, Kind);
        }
    }
    public sealed class RegistryJournal
    {
        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public bool UninstallExisted;
        public List<RegistryDatum> UninstallValues = new List<RegistryDatum>();
        public RegistryDatum AutoStart;
        internal static RegistryJournal Capture()
        {
            var snapshot = new RegistryJournal();
            using (var key = Registry.CurrentUser.OpenSubKey(InstallJob.UninstallKey))
                if (key != null)
                {
                    if (key.SubKeyCount != 0) throw new InvalidOperationException("Sous-clé de désinstallation inattendue ; arrêt sans changement.");
                    snapshot.UninstallExisted = true;
                    foreach (string name in key.GetValueNames()) snapshot.UninstallValues.Add(RegistryDatum.Capture(key, name));
                }
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                if (key != null && Array.IndexOf(key.GetValueNames(), "Haloryva") >= 0) snapshot.AutoStart = RegistryDatum.Capture(key, "Haloryva");
            return snapshot;
        }
        internal void Restore()
        {
            Registry.CurrentUser.DeleteSubKeyTree(InstallJob.UninstallKey, false);
            if (UninstallExisted) using (var key = Registry.CurrentUser.CreateSubKey(InstallJob.UninstallKey))
                foreach (var value in UninstallValues) value.Restore(key);
            using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
            { run.DeleteValue("Haloryva", false); if (AutoStart != null) AutoStart.Restore(run); }
        }
    }
}
