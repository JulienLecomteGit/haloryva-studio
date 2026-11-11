using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using CursorFlow;

internal sealed class FakeStartup : IStartupSettings
{
    internal int Reads, Changes;
    public Task<StartupSnapshot> ReadAsync() { Reads++; return Task.FromResult(StartupSnapshot.FromState(1)); }
    public Task<StartupSnapshot> ChangeFromUserAsync(bool desired) { Changes++; return Task.FromResult(StartupSnapshot.FromState(desired ? 2 : 0)); }
}
internal static class Targeted
{
    static List<object> results = new List<object>();
    static string output;
    static string root;
    static int failed;
    static void Check(bool condition, string id, string kind)
    {
        results.Add(new { Id = id, Kind = kind, Passed = condition });
        if (!condition) throw new Exception("Assertion failed: " + id);
    }
    static string Json(Config config) { return new JavaScriptSerializer().Serialize(config); }
    static string WriteProfile(string folder, Config cfg)
    { Directory.CreateDirectory(folder); string file = Path.Combine(folder, "settings.json"); File.WriteAllText(file, Json(cfg), new UTF8Encoding(false)); return file; }
    static void Throws(Action action, string id)
    {
        bool threw = false; try { action(); } catch { threw = true; }
        Check(threw, id, "isolated-files");
    }
    static void Unit()
    {
        ProfilePaths.Configure(new[] { "--trail-session" });
        L.Initialize("fr");
        string inputs = Path.Combine(root, "inputs");
        string source = WriteProfile(inputs, new Config { UiLanguage = "es", DrawingToolCycle = new[] { "Stylo", "Rectangle" }, LaunchAtLogin = true, AnnotationThickness = 7 });
        string original = ProfileMigration.Hash(File.ReadAllBytes(source));
        var c = ProfileMigration.ReadCandidate(source, true);
        string target = Path.Combine(root, "target");
        var plan = ProfileMigration.Prepare(c, target, false, false);
        ProfileMigration.Commit(plan, false);
        var copied = ProfileMigration.ReadCandidate(Path.Combine(target, "settings.json"), true);
        Check(copied.Options.UiLanguage == "es" && copied.Options.DrawingToolCycle.SequenceEqual(new[] { "Stylo", "Rectangle" }), "M1-language-tools", "isolated-files");
        Check(copied.Options.LaunchAtLogin && copied.Options.AnnotationThickness == 7, "M1-fields", "isolated-files");
        Check(ProfileMigration.Hash(File.ReadAllBytes(source)) == original, "M1-source-unchanged", "isolated-files");
        Throws(delegate { ProfileMigration.Prepare(c, target, false, false); }, "M4-refuse-overwrite");
        string corrupt = Path.Combine(root, "corrupt"); Directory.CreateDirectory(corrupt);
        File.WriteAllText(Path.Combine(corrupt, "settings.json"), "{invalid");
        File.Copy(source, Path.Combine(corrupt, "settings.json.bak"));
        var recovered = ProfileMigration.ReadBest(corrupt, true);
        Check(recovered.Path.EndsWith(".bak") && File.ReadAllText(Path.Combine(corrupt, "settings.json")) == "{invalid", "M2-backup-no-repair", "isolated-files");
        File.WriteAllText(Path.Combine(corrupt, "settings.json.bak"), "invalid");
        Throws(delegate { ProfileMigration.ReadBest(corrupt, true); }, "M2-invalid-refused");
        string large = Path.Combine(root, "large.json"); File.WriteAllBytes(large, new byte[1000001]);
        Throws(delegate { ProfileMigration.ReadCandidate(large, true); }, "M2-limit");
        string race = Path.Combine(root, "race");
        var raced = ProfileMigration.Prepare(c, race, false, false);
        string raceFile = WriteProfile(race, new Config { UiLanguage = "en" });
        string raceHash = ProfileMigration.Hash(File.ReadAllBytes(raceFile));
        Throws(delegate { ProfileMigration.Commit(raced, false); }, "M5-concurrent-destination");
        Check(ProfileMigration.Hash(File.ReadAllBytes(raceFile)) == raceHash && ProfileMigration.Hash(File.ReadAllBytes(source)) == original, "M5-rollback-source-target", "isolated-files");
        string old = Path.Combine(root, "270.json"); File.WriteAllText(old, "{\"AnnotationThickness\":8,\"LaunchAtLogin\":true}");
        var oldConfig = ProfileMigration.ReadCandidate(old, true);
        Check(oldConfig.Options.UiLanguage == "fr" && oldConfig.Options.DrawingToolCycle.Length == 10 && oldConfig.Options.AnnotationThickness == 8, "M1-270-defaults", "isolated-files");
        string png = Path.Combine(root, "logo.png"); using (var bitmap = new Bitmap(4,4)) bitmap.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        var assetCfg = new Config { TrailLogoPath = png, CustomSound = Path.Combine(root, "absent.wav"), ActivePreset = "custom", ActivePresetSignature = "old" };
        string assetFile = WriteProfile(Path.Combine(root, "asset-source"), assetCfg);
        var ap = ProfileMigration.Prepare(ProfileMigration.ReadCandidate(assetFile, true), Path.Combine(root, "asset-target"), true, false);
        Check(ap.Assets.Count == 1 && ap.UncopiedAssets.SequenceEqual(new[] { "CustomSound" }), "M6-asset-plan", "isolated-files");
        ProfileMigration.Commit(ap, false);
        Check(File.Exists(ap.Options.TrailLogoPath) && ProfileMigration.Hash(File.ReadAllBytes(png)) == ProfileMigration.Hash(File.ReadAllBytes(ap.Options.TrailLogoPath)), "M6-image-copy", "isolated-files");
        Check(ap.Options.ActivePreset == "" && ap.Options.CustomSound == assetCfg.CustomSound, "M6-preset-missing-reference", "isolated-files");
        foreach (int state in new[] {0,1,2,3,4,-1,99})
        {
            var snap = StartupSnapshot.FromState(state);
            bool enabled = state == 2 || state == 4;
            Check((state < 0 || state == 99) ? !snap.EffectiveEnabled.HasValue : snap.EffectiveEnabled == enabled,
                "S4-state-" + state, "simulated-state");
            Check(snap.CanChange == (state == 0 || state == 2), "S4-permission-" + state, "simulated-state");
        }
        Check(ProfilePaths.ProtectedRoots.All(p => ProfilePaths.Overlaps(p, Path.Combine(p, "diagnostic"))), "M4-protected-roots", "isolated-path-policy");
        string trial = Path.Combine(root, "trial");
        TrailTrial.Initialize(new[] { "--trail-session", trial });
        var engine = new CursorController();
        var fake = new FakeStartup(); engine.StartupProvider = fake;
        try
        {
            engine.Options.LaunchAtLogin = true; engine.Changed();
            engine.ReplaceOptions(new Config { UiLanguage = "en", LaunchAtLogin = false });
            engine.ImportProfile(source);
            Check(fake.Changes == 0 && engine.Options.LaunchAtLogin && engine.Options.UiLanguage == "es", "S5-import-no-startup-calls", "real-controller-fake-startup");
            Check(ProfileMigration.Hash(File.ReadAllBytes(source)) == original, "M1-controller-source-intact", "real-controller-isolated-files");
        }
        finally { engine.Exit(); }
    }
    static async Task Packaged(string mode)
    {
        ProfilePaths.Configure(new string[0]);
        Check(PackageContext.IsPackaged, "package-identity", "real-windows-msix");
        Check(ProfilePaths.NormalDirectory == PackageContext.PackageDirectory &&
            ProfilePaths.NormalDirectory.IndexOf("LocalState", StringComparison.OrdinalIgnoreCase) >= 0, "package-private-path", "real-windows-msix");
        var service = new PackagedStartupSettings();
        var before = await service.ReadAsync();
        results.Add(new { Diagnostic = "adapter-before", State = before.Availability.ToString(), Enabled = before.EffectiveEnabled, CanChange = before.CanChange, Message = before.MessageKey });
        try {
            var direct = await Windows.ApplicationModel.StartupTask.GetAsync(PackagedStartupSettings.TaskId);
            results.Add(new { Diagnostic = "direct-get", TaskId = direct.TaskId, State = direct.State.ToString() });
        } catch (Exception ex) { results.Add(new { Diagnostic = "direct-get-error", Error = ex.ToString(), HResult = ex.HResult }); }
        try {
            var tasks = await Windows.ApplicationModel.StartupTask.GetForCurrentPackageAsync();
            results.Add(new { Diagnostic = "package-task-list", Tasks = tasks.Select(t => new { Id = t.TaskId, State = t.State.ToString() }).ToArray() });
        } catch (Exception ex) { results.Add(new { Diagnostic = "package-task-list-error", Error = ex.ToString(), HResult = ex.HResult }); }
        if (mode == "package-enable")
        {
            Check(before.Availability == StartupAvailability.Disabled, "S1-default-disabled", "real-windows-msix");
            var on = await service.ChangeFromUserAsync(true);
            Check(on.Availability == StartupAvailability.Enabled && on.EffectiveEnabled == true, "S2-enable", "real-windows-msix");
            var off = await service.ChangeFromUserAsync(false);
            Check(off.Availability == StartupAvailability.Disabled, "S2-disable", "real-windows-msix");
            var again = await service.ChangeFromUserAsync(true);
            Check(again.Availability == StartupAvailability.Enabled, "S2-enable-for-update", "real-windows-msix");
        }
        else if (mode == "package-retained")
        {
            Check(before.Availability == StartupAvailability.Enabled, "U2-startup-retained", "real-windows-msix");
            var off = await service.ChangeFromUserAsync(false);
            Check(off.Availability == StartupAvailability.Disabled, "S2-final-disable", "real-windows-msix");
        }
        else throw new Exception("Unknown packaged mode");
    }
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3) throw new Exception("mode, output and token required");
            output = Path.GetFullPath(args[1]); root = Path.GetDirectoryName(output);
            var marker = new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root, "authorization.json")));
            if (!(bool)marker["GitHubHosted"] || (string)marker["Token"] != args[2]) throw new Exception("Not an authorized disposable recipe");
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var form = new Form { Width = 1, Height = 1, ShowInTaskbar = false, Opacity = 0 };
            form.Shown += async delegate
            {
                try { if (args[0] == "unit") Unit(); else await Packaged(args[0]); }
                catch (Exception ex) { failed = 1; results.Add(new { Error = ex.ToString() }); }
                finally
                {
                    File.WriteAllText(output, new JavaScriptSerializer().Serialize(new { Version="2.7.1-preview.8", Mode=args[0], Passed=failed==0, Checks=results, Historical195Executed=false }));
                    form.Close();
                }
            };
            Application.Run(form);
        }
        catch (Exception ex) { failed = 1; if (output != null) File.WriteAllText(output, new JavaScriptSerializer().Serialize(new {Passed=false,Error=ex.ToString()})); }
        return failed;
    }
}
