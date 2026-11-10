using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using CursorFlow.Setup;

namespace CursorFlow.Tests
{
    internal static class ProtocolTests
    {
        private static int passed;
        private static void Assert(bool value, string label)
        { if (!value) throw new Exception(label); passed++; Console.WriteLine("OK " + passed + " : " + label); }
        private static void Throws(Action action, string label)
        { bool threw = false; try { action(); } catch { threw = true; } Assert(threw, label); }
        private static int Main()
        {
            try { SideButtons(); Inputs(); Bounds(); Files(); Migration(); EffectsDiagnostics(); Console.WriteLine("TOTAL " + passed + " assertions C# réussies."); return 0; }
            catch (Exception ex) { Console.Error.WriteLine("ECHEC : " + ex); return 1; }
        }
        private static void SideButtons()
        {
            Config cfg = new Config();
            Assert(!cfg.SwapSideButtons, "C3 : orientation normale par défaut");
            cfg.ButtonX1 = "Annuler le dernier trait";
            cfg.ButtonX2 = "Dessin : activer / arrêter";
            cfg.ButtonX1Long = "Effacer les dessins";
            cfg.ButtonX2Long = "Outil de dessin suivant";
            Assert(SideButtonMap.ReadAction(cfg, 3, false) == cfg.ButtonX1 &&
                   SideButtonMap.ReadAction(cfg, 4, false) == cfg.ButtonX2,
                "C3 : lecture native des deux actions courtes inchangée");
            Assert(SideButtonMap.ReadAction(cfg, 3, true) == cfg.ButtonX1Long &&
                   SideButtonMap.ReadAction(cfg, 4, true) == cfg.ButtonX2Long,
                "C3 : lecture native des deux actions longues inchangée");

            // Cas de Julien : actions déjà échangées à la main. Aucun clic ne doit changer.
            cfg.SwapSideButtons = true;
            Assert(SideButtonMap.ReadAction(cfg, 3, false) == "Dessin : activer / arrêter" &&
                   SideButtonMap.ReadAction(cfg, 4, false) == "Annuler le dernier trait",
                "C3 : menus arrière/avant reflètent les actions réellement utilisées");
            Assert(SideButtonMap.ReadAction(cfg, 3, true) == "Outil de dessin suivant" &&
                   SideButtonMap.ReadAction(cfg, 4, true) == "Effacer les dessins",
                "C3 : les deux appuis longs suivent les mêmes libellés");
            Assert(cfg.ButtonX1 == "Annuler le dernier trait" &&
                   cfg.ButtonX2 == "Dessin : activer / arrêter" &&
                   cfg.ButtonX1Long == "Effacer les dessins" &&
                   cfg.ButtonX2Long == "Outil de dessin suivant",
                "C3 : orientation ne modifie aucune affectation native");
            Assert(SideButtonMap.ToDisplay("X2", true) == "X1" &&
                   SideButtonMap.ToDisplay("X1", true) == "X2" &&
                   SideButtonMap.ToDisplay("Milieu", true) == "Milieu",
                "C3 : détection visuelle alignée, molette intacte");
            for (int i = 0; i < 5; i++)
                Assert(SideButtonMap.NativeIndex(SideButtonMap.NativeIndex(i, true), true) == i,
                    "C3 : permutation réversible du bouton " + i);
            SideButtonMap.WriteAction(cfg, 3, "Halo : activer / arrêter", false);
            SideButtonMap.WriteAction(cfg, 4, "Traînée : activer / arrêter", true);
            Assert(cfg.ButtonX2 == "Halo : activer / arrêter" && cfg.ButtonX1Long == "Traînée : activer / arrêter",
                "C3 : écritures X1 court et X2 long au bon endroit");
            string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(cfg);
            Config loaded = ConfigStore.Deserialize(json);
            Assert(loaded.SwapSideButtons &&
                   SideButtonMap.ReadAction(loaded, 3, false) == "Halo : activer / arrêter" &&
                   SideButtonMap.ReadAction(loaded, 4, true) == "Traînée : activer / arrêter",
                "C3 : orientation et affectations persistent dans settings.json");
            Config legacy = ConfigStore.Deserialize("{\"ButtonX1\":\"Normal\",\"ButtonX2\":\"Normal\"}");
            Assert(!legacy.SwapSideButtons, "C3 : anciens profils sans champ restent compatibles");
            cfg.ResetButtons();
            Assert(cfg.SwapSideButtons && cfg.ButtonX1 == "Normal" && cfg.ButtonX2 == "Normal",
                "C3 : restaurer les actions préserve l'orientation");
        }
        private static void Migration()
        {
            // Tous les essais sont fictifs et isolés dans le dossier temporaire du test.
            string root = Path.Combine(Path.GetTempPath(), "Haloryva-MigrationTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string old = Path.Combine(root, "legacy");
                string current = Path.Combine(root, "haloryva");
                Directory.CreateDirectory(old);
                Directory.CreateDirectory(current);
                string source = Path.Combine(old, "settings.json");
                string target = Path.Combine(current, "settings.json");
                string original = "{\"Trail\":true,\"TrailStyle\":\"Lumière\",\"TrailLife\":269}";
                File.WriteAllText(source, original);
                Predicate<string> allowed = delegate(string file) { return file.StartsWith(current + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); };
                var tx = new FileTransaction(Path.Combine(root, "transaction-1"), allowed, false);
                MigrationPreview.CopyForTest(source, target, tx);
                Assert(File.ReadAllText(target) == original, "import opt-in copie la configuration sans transformation");
                Assert(File.ReadAllText(source) == original, "configuration CursorFlow source intacte");
                Throws(delegate { MigrationPreview.CopyForTest(source, target, tx); }, "import refuse l'écrasement d'un profil Haloryva existant");
                tx.Rollback();
                Assert(!File.Exists(target) && File.Exists(source), "rollback retire la copie et préserve la source");
                File.WriteAllText(source, "contenu non JSON");
                Throws(delegate { MigrationPreview.ValidateFile(source, target); }, "préférences illisibles rejetées avant mutation");
                Assert(!File.Exists(target), "aucun profil créé après refus");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void EffectsDiagnostics()
        {
            // C1 ne doit jamais créer de journal sans activation explicite.
            Assert(!EffectDiagnostics.Enabled, "C1 désactivé par défaut");
            EffectDiagnostics.Event("TEST", "IGNORED_WHEN_DISABLED");
            string root = Path.Combine(Path.GetTempPath(), "Haloryva-C1Tests-" + Guid.NewGuid().ToString("N"));
            try
            {
                Throws(delegate { EffectDiagnostics.Initialize(new[] { "--effect-diagnostics" }); },
                    "C1 exige un dossier absolu après le drapeau");
                Throws(delegate { EffectDiagnostics.Initialize(new[] { "--effect-diagnostics", "captures" }); },
                    "C1 refuse les chemins relatifs");
                string protectedRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CursorFlow", "CapturesTestC1-" + Guid.NewGuid().ToString("N"));
                Throws(delegate { EffectDiagnostics.Initialize(new[] { "--effect-diagnostics", protectedRoot }); },
                    "C1 refuse les journaux dans le profil CursorFlow");
                Assert(!Directory.Exists(root), "C1 ne crée aucun dossier avant activation valide");
                EffectDiagnostics.Initialize(new[] { "--effect-diagnostics", root });
                Assert(EffectDiagnostics.Enabled, "C1 activé à la demande seulement");
                string second = Path.Combine(Path.GetTempPath(), "Haloryva-C1Tests-" + Guid.NewGuid().ToString("N"));
                Throws(delegate { EffectDiagnostics.Initialize(new[] { "--effect-diagnostics", second }); },
                    "C1 interdit deux sessions actives");
                Assert(!Directory.Exists(second), "C1 refuse une deuxième destination sans l'écrire");
                string file = Path.Combine(root, "EFFETS_DIAGNOSTIC.csv");
                Assert(File.Exists(file), "C1 crée un journal séparé du profil utilisateur");
                EffectDiagnostics.FrameQueued();
                EffectDiagnostics.FrameDispatched();
                EffectDiagnostics.FrameCoalesced();
                EffectDiagnostics.RenderTick();
                EffectDiagnostics.Publication(false, 123);
                EffectDiagnostics.Publication(true, 0);
                Assert(EffectDiagnostics.ShouldSample(), "C1 prélève le premier échantillon");
                Assert(!EffectDiagnostics.ShouldSample(), "C1 limite les échantillons rapprochés");
                EffectDiagnostics.Sample(true, true, true, true, true, 3, true);
                EffectDiagnostics.Event("ZORDER_EFFECTS_OK", "win32=0");
                EffectDiagnostics.Close();
                string log = File.ReadAllText(file);
                Assert(log.Contains("SESSION_START") && log.Contains("SESSION_END"),
                    "C1 délimite chaque session");
                Assert(log.Contains("PUBLISH_FAILED;win32=123") && log.Contains("PUBLISH_OK;win32=0"),
                    "C1 consigne états et codes Win32 des publications");
                Assert(log.Contains("queued=1") && log.Contains("dispatched=1") &&
                    log.Contains("coalesced=1") && log.Contains("ticks=1") &&
                    log.Contains("published=1") && log.Contains("failures=1") &&
                    log.Contains("above=3"), "C1 échantillonne cadence, visibilité et ordre Z");
                Assert(!log.Contains("IGNORED_WHEN_DISABLED") &&
                    !log.Contains("mouseX=") && !log.Contains("windowTitle="),
                    "C1 ne publie ni données de souris ni titre de fenêtre");
                Assert(!EffectDiagnostics.Enabled, "C1 ferme la session après observation");
                Throws(delegate { EffectDiagnostics.Initialize(new[] { "--effect-diagnostics", root }); },
                    "C1 refuse d'écraser un journal existant");
                Assert(File.ReadAllText(file) == log,
                    "C1 ne modifie pas le journal après refus de doublon");
            }
            finally
            {
                EffectDiagnostics.Close();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void Inputs()
        {
            foreach (int delay in new int[] { 399, 400, 401, 1300 })
            {
                long now = 100; var book = new PressBook(delegate { return now; });
                Assert(book.Begin("X1", "court", "long", 400, 15, 20), "début retenu " + delay);
                now += delay; var command = book.Release("X1");
                Assert(command.Action == (delay < 400 ? "court" : "long"), "seuil au relâchement " + delay);
                Assert(command.X == 15 && command.Y == 20, "point d'appui conservé");
                Assert(book.Release("X1") == null, "action unique au second UP");
            }
            long time = 0; var presses = new PressBook(delegate { return time; });
            Assert(!presses.Begin("Gauche", "court", "long", 400, 0, 0), "long gauche interdit");
            Assert(!presses.Begin("Droit", "court", "long", 400, 0, 0), "long droit interdit");
            Assert(!presses.Begin("X1", "court", "Normal", 400, 0, 0), "pas de rétention sans long");
            presses.Begin("X1", "court", "pause", 400, 0, 0);
            presses.Begin("X2", "court2", "effacer", 400, 0, 0);
            time = 405; int actions = 0;
            presses.Poll(delegate(PressCommand c) { actions++; presses.Clear(); });
            Assert(actions == 1, "pause réentrante avec deux boutons : pas d'accès à une clé effacée");
            Assert(presses.Release("X1") == null && presses.Release("X2") == null, "reset annule les actions en attente");
            time = 0; presses.Begin("X1", "ancien court", "ancien long", 400, 0, 0);
            presses.Begin("X1", "nouveau court", "nouveau long", 900, 99, 99);
            time = 410;
            Assert(presses.Release("X1").Action == "ancien long", "configuration figée jusqu'au UP");
            time = 0; presses.Begin("Milieu", "Normal", "dessin", 400, 0, 0);
            time = 100; Assert(presses.Release("Milieu").Action == "Normal", "court natif explicitement rejoué");
            time = 0; presses.Begin("X1", "court", "long", 400, 0, 0);
            time = 401; actions = 0; presses.Poll(delegate { actions++; }); presses.Poll(delegate { actions++; });
            Assert(actions == 1 && presses.Release("X1") == null, "poll + release : une seule action longue");
            var routes = new MouseRoutes();
            routes.Begin("Gauche", MouseRoute.Pass, 0);
            Assert(!routes.IsDrawing("Gauche", 1), "activation dessin pendant glisser natif : pas de dessin fantôme");
            Assert(routes.Release("Gauche").Route == MouseRoute.Pass, "relâchement natif conserve sa route");
            routes.Begin("X1", MouseRoute.Held, 0); presses.Clear();
            Assert(routes.Release("X1").Route == MouseRoute.Held, "UP retenu absorbé après reset");
            routes.Begin("Gauche", MouseRoute.Drawing, 3);
            Assert(routes.IsDrawing("Gauche", 3) && !routes.IsDrawing("Gauche", 4), "session dessin figée");
            Assert(routes.Release("Gauche").DrawingSession == 3, "UP garde la session originale");
            Assert(routes.Release("X2").Route == MouseRoute.Pass, "UP inconnu n'est jamais absorbé");
        }
        private static void Bounds()
        {
            foreach (Rectangle screen in new Rectangle[] { new Rectangle(0,0,1920,1080), new Rectangle(-1920,-200,3840,1280), new Rectangle(0,0,1366,728) })
            foreach (int size in new int[] { 60, 500, 980, 1500 })
            {
                int cx = screen.Left + screen.Width / 2, cy = screen.Top + screen.Height / 2;
                Rectangle required = new Rectangle(cx-size/2,cy-size/2,size,size);
                Rectangle desired = Rectangle.Union(required, new Rectangle(screen.Left,screen.Top,screen.Width,screen.Height));
                Rectangle result = FrameBounds.Calculate(screen,desired,required);
                Assert(result.Contains(Rectangle.Intersect(screen,required)), "halo prioritaire " + screen + " taille=" + size);
                Assert(screen.Contains(result), "surface bornée au Bureau");
            }
        }
        private static void Files()
        {
            string root = Path.Combine(Path.GetTempPath(), "Haloryva-ProtocolTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string targetDir = Path.Combine(root,"app"); Directory.CreateDirectory(targetDir);
                string target = Path.Combine(targetDir,"app.exe"), source = Path.Combine(root,"source.bin");
                File.WriteAllText(source,"nouveau");
                Predicate<string> allowed = delegate(string p) { return p.StartsWith(targetDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); };
                foreach (string point in new string[] { "before-replace", "after-replace" })
                {
                    File.WriteAllText(target,"ancien");
                    var tx = new FileTransaction(Path.Combine(root,Guid.NewGuid().ToString("N")),allowed,false);
                    tx.Fault = delegate(string phase) { if (phase == point) throw new IOException("faute injectée"); };
                    Throws(delegate { tx.Replace(source,target); }, "faute " + point);
                    tx.Rollback();
                    Assert(File.ReadAllText(target)=="ancien", "rollback conserve ancien EXE " + point);
                }
                string journal = Path.Combine(root,"recovery");
                var active = new FileTransaction(journal,allowed,false);
                active.Replace(source,target);
                var recovered = new FileTransaction(journal,allowed,true); recovered.Rollback();
                Assert(File.ReadAllText(target)=="ancien", "journal relu : reprise sans drapeau en mémoire");
                var fresh = new FileTransaction(Path.Combine(root,"fresh"),allowed,false);
                string nested = Path.Combine(targetDir,"Setup","installer.exe");
                fresh.Replace(source,nested); fresh.Rollback();
                Assert(!File.Exists(nested) && !Directory.Exists(Path.GetDirectoryName(nested)), "nouvelle installation annulée sans reliquat");
                var batch = new FileTransaction(Path.Combine(root,"batch"),allowed,false);
                string second = Path.Combine(targetDir,"setup.exe"); File.WriteAllText(second,"ancien setup");
                batch.Replace(source,target); batch.Replace(source,second); batch.Rollback();
                Assert(File.ReadAllText(target)=="ancien" && File.ReadAllText(second)=="ancien setup", "app + désinstallateur restaurés");
                var deletion = new FileTransaction(Path.Combine(root,"delete"),allowed,false);
                deletion.Fault = delegate(string point) { if (point=="after-delete") throw new IOException("suppression interrompue"); };
                Throws(delegate { deletion.Delete(target); }, "échec après suppression"); deletion.Rollback();
                Assert(File.ReadAllText(target)=="ancien", "suppression annulée");
                var committed = new FileTransaction(Path.Combine(root,"commit"),allowed,false);
                committed.Replace(source,target); committed.Commit(); committed.Rollback();
                Assert(File.ReadAllText(target)=="nouveau", "commit interdit rollback tardif");
                var unsafeTx = new FileTransaction(Path.Combine(root,"unsafe"),allowed,false);
                Throws(delegate { unsafeTx.Delete(source); }, "périmètre de suppression contrôlé");
                Assert(File.Exists(source), "fichier hors périmètre intact");
                var corrupt = new FileTransaction(Path.Combine(root,"corrupt"),allowed,false);
                corrupt.Replace(source,second);
                File.WriteAllText(Path.Combine(corrupt.DirectoryPath,"file-000000.bin"),"corrompu");
                Throws(delegate { corrupt.Rollback(); }, "sauvegarde corrompue : refus explicite");
                Assert(corrupt.State == "active", "journal conservé pour réparation");
            }
            finally { Directory.Delete(root,true); }
        }
    }
}
