using System;
using System.Threading;
using System.Windows.Forms;

namespace CursorFlow
{
    internal static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [STAThread]
        private static int Main(string[] args)
        {
            if (Array.IndexOf(args, "--trail-selftest") >= 0) return TrailSelfTest.Run();
            if (Array.IndexOf(args, "--selftest") >= 0) return GeometrySmoke.Run();
            try { ProfilePaths.Configure(args); L.Initialize(ConfigStore.ReadLanguage()); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Haloryva Studio"); return 15; }
            // A normal launch cannot race a maintenance operation. Self-tests above remain isolated.
            try { ProfilePaths.CheckLegacyMaintenance(); }
            catch (System.IO.IOException)
            {
                MessageBox.Show(L.T("Une maintenance de Haloryva est en cours. Ferme son assistant avant de lancer l'application."), "Haloryva Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 12;
            }
            // Netteté à 125 / 150 % même si l'exécutable est compilé sans manifeste.
            try { SetProcessDPIAware(); } catch { }
            bool first;
            using (Mutex mutex = new Mutex(true, "Local\\CursorFlow_Windows64_Studio", out first))
            {
                if (!first)
                {
                    if (Array.IndexOf(args, "--silent") >= 0) return 0;
                    MessageBox.Show(L.T("Haloryva ou une ancienne application d'effets de curseur est déjà active. Ferme l'autre application avant de lancer Haloryva, depuis son icône près de l'horloge."),
                        "Haloryva Studio", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                try { TrailTrial.Initialize(args); EffectDiagnostics.Initialize(args); }
                catch (Exception ex) { MessageBox.Show(L.T("Session d'essai ou diagnostic refusé : ") + ex.Message, "Haloryva"); return 14; }
                L.Initialize(ConfigStore.ReadLanguage());
                ErrorReporting.Initialize();
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                { ErrorReporting.Record("UNHANDLED", e.ExceptionObject as Exception); };
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
                {
                    ErrorReporting.Notify(e.Exception);
                };
                try { ProfileMigration.Bootstrap(Array.IndexOf(args, "--silent") >= 0); L.Initialize(ConfigStore.ReadLanguage()); }
                catch (Exception ex) { MessageBox.Show(ex.Message, L.T("Import impossible"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return 16; }
                // Splash de lancement (pas au démarrage automatique de Windows : --silent).
                if (Array.IndexOf(args, "--silent") < 0) SplashScreen.Begin();
                Application.Run(new CursorController());
                EffectDiagnostics.Close();
            }
            return 0;
        }
    }
}
