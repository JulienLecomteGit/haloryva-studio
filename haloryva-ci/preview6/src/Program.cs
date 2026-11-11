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
            L.Initialize(ConfigStore.ReadLanguage());
            // A normal launch cannot race a maintenance operation. Self-tests above remain isolated.
            string maintenanceLock = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haloryva.Maintenance", "operation.lock");
            try
            {
                if (System.IO.File.Exists(maintenanceLock))
                    using (var probe = new System.IO.FileStream(maintenanceLock, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite)) { }
            }
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
                // Splash de lancement (pas au démarrage automatique de Windows : --silent).
                if (Array.IndexOf(args, "--silent") < 0) SplashScreen.Begin();
                Application.Run(new CursorController());
                EffectDiagnostics.Close();
            }
            return 0;
        }
    }
}
