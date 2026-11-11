using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace CursorFlow
{
    internal enum StartupAvailability
    { Disabled = 0, DisabledByUser = 1, Enabled = 2, DisabledByPolicy = 3, EnabledByPolicy = 4, Unknown = -1 }
    internal sealed class StartupSnapshot
    {
        internal StartupAvailability Availability;
        internal bool? EffectiveEnabled;
        internal bool CanChange;
        internal string MessageKey;
        internal static StartupSnapshot FromState(int state)
        {
            var result = new StartupSnapshot { Availability = (StartupAvailability)state };
            switch (result.Availability)
            {
                case StartupAvailability.Disabled: result.EffectiveEnabled = false; result.CanChange = true; break;
                case StartupAvailability.Enabled: result.EffectiveEnabled = true; result.CanChange = true; break;
                case StartupAvailability.DisabledByUser:
                    result.EffectiveEnabled = false; result.MessageKey = "Démarrage désactivé dans Windows."; break;
                case StartupAvailability.DisabledByPolicy:
                    result.EffectiveEnabled = false; result.MessageKey = "Ce réglage est géré par Windows."; break;
                case StartupAvailability.EnabledByPolicy:
                    result.EffectiveEnabled = true; result.MessageKey = "Ce réglage est géré par Windows."; break;
                default:
                    result.Availability = StartupAvailability.Unknown;
                    result.MessageKey = "État du démarrage indisponible."; break;
            }
            return result;
        }
    }
    internal interface IStartupSettings
    {
        Task<StartupSnapshot> ReadAsync();
        Task<StartupSnapshot> ChangeFromUserAsync(bool desired);
    }
    internal sealed class PackagedStartupSettings : IStartupSettings
    {
        internal const string TaskId = "HaloryvaStudioStartup";
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        public async Task<StartupSnapshot> ReadAsync()
        {
            try
            {
                if (!PackageContext.IsPackaged || !ProfilePaths.SystemChangesAllowed)
                    return StartupSnapshot.FromState(-1);
                var task = await StartupTask.GetAsync(TaskId);
                return StartupSnapshot.FromState((int)task.State);
            }
            catch (Exception ex) { ErrorReporting.Record("STARTUP_READ", ex); return StartupSnapshot.FromState(-1); }
        }
        public async Task<StartupSnapshot> ChangeFromUserAsync(bool desired)
        {
            await gate.WaitAsync();
            try
            {
                if (!PackageContext.IsPackaged || !ProfilePaths.SystemChangesAllowed)
                    return StartupSnapshot.FromState(-1);
                var task = await StartupTask.GetAsync(TaskId);
                if (task.State == StartupTaskState.Disabled && desired) await task.RequestEnableAsync();
                else if (task.State == StartupTaskState.Enabled && !desired) task.Disable();
                return StartupSnapshot.FromState((int)task.State);
            }
            catch (Exception ex) { ErrorReporting.Record("STARTUP_CHANGE", ex); return StartupSnapshot.FromState(-1); }
            finally { gate.Release(); }
        }
    }
    internal sealed class LegacyStartupSettings : IStartupSettings
    {
        public Task<StartupSnapshot> ReadAsync()
        {
            try
            {
                if (PackageContext.IsPackaged || !ProfilePaths.SystemChangesAllowed)
                    return Task.FromResult(StartupSnapshot.FromState(-1));
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    string actual = key == null ? null : key.GetValue("Haloryva") as string;
                    string expected = "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --silent";
                    return Task.FromResult(StartupSnapshot.FromState(String.Equals(actual, expected, StringComparison.OrdinalIgnoreCase) ? 2 : 0));
                }
            }
            catch (Exception ex) { ErrorReporting.Record("STARTUP_READ", ex); return Task.FromResult(StartupSnapshot.FromState(-1)); }
        }
        public async Task<StartupSnapshot> ChangeFromUserAsync(bool desired)
        {
            if (PackageContext.IsPackaged || !ProfilePaths.SystemChangesAllowed)
                return StartupSnapshot.FromState(-1);
            if (!ConfigStore.ApplyAutoStart(desired)) return StartupSnapshot.FromState(-1);
            return await ReadAsync();
        }
    }
    internal static class StartupSettings
    {
        internal static IStartupSettings Create()
        { return PackageContext.IsPackaged ? (IStartupSettings)new PackagedStartupSettings() : new LegacyStartupSettings(); }
        internal static bool HasLegacyEntry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    return key != null && (key.GetValue("Haloryva") != null || key.GetValue("CursorFlow") != null);
            }
            catch { return true; } // Unknown is reported as a caution, never changed automatically.
        }
    }
}
