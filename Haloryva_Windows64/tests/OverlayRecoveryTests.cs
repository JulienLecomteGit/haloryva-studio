using System;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CursorFlow
{
    // Tests de la politique de production avec temps et plateforme simules.
    // Aucun formulaire, appel Win32, hook, profil ou processus Haloryva lance.
    // Ces tests ne prouvent ni la detection native ni le retour visuel des effets.
    internal static class OverlayRecoveryTests
    {
        private static int passed;
        private static int failed;
        private static string fixtureDirectory;

        private sealed class FakePlatform : IOverlayRecoveryPlatform
        {
            internal RecoverySample Current = Obscured(100, 200);
            internal readonly Queue<RecoverySample> Next = new Queue<RecoverySample>();
            internal readonly List<long> RepairTimes = new List<long>();
            internal RecoverySample LastRepair;
            internal int Inspections;
            internal int Repairs;
            internal long Now;
            internal bool ThrowOnInspect;
            internal bool ThrowOnRepair;
            internal bool RepairSuccess = true;
            internal int RepairError;
            internal RecoveryReason CancelRepairReason;

            public RecoverySample Inspect()
            {
                Inspections++;
                if (ThrowOnInspect) throw new InvalidOperationException("TEST_PROBE_EXCEPTION");
                return Next.Count > 0 ? Next.Dequeue() : Current;
            }

            public RecoveryRepairResult Repair(RecoverySample expected)
            {
                Repairs++;
                RepairTimes.Add(Now);
                LastRepair = expected;
                if (ThrowOnRepair) throw new InvalidOperationException("TEST_REPAIR_EXCEPTION");
                if (CancelRepairReason != RecoveryReason.None) return new RecoveryRepairResult(CancelRepairReason);
                return new RecoveryRepairResult(RepairSuccess, RepairError);
            }
        }

        private sealed class Scenario
        {
            internal readonly FakePlatform Platform = new FakePlatform();
            internal readonly List<RecoveryEvent> Events = new List<RecoveryEvent>();
            internal readonly OverlayRecoveryPolicy Policy;

            internal Scenario()
            {
                Policy = new OverlayRecoveryPolicy(Platform,
                    delegate(RecoveryEvent value) { Events.Add(value); });
            }

            internal void Tick(long now) { Tick(now, true); }
            internal void Tick(long now, bool eligible)
            {
                Platform.Now = now;
                Policy.Tick(now, eligible);
            }

            internal int EventCount(string name)
            {
                int count = 0;
                foreach (RecoveryEvent item in Events) if (item.Name == name) count++;
                return count;
            }
        }

        private static RecoverySample Obscured(long layer, long blocker)
        { return new RecoverySample(RecoveryState.Obscured, layer, blocker, RecoveryReason.None); }
        private static RecoverySample Healthy(long layer)
        { return new RecoverySample(RecoveryState.Healthy, layer, 0, RecoveryReason.None); }
        private static RecoverySample Deferred(RecoveryReason reason)
        { return new RecoverySample(RecoveryState.Deferred, 100, 0, reason); }
        private static RecoverySample Unknown()
        { return new RecoverySample(RecoveryState.Unknown, 100, 0, RecoveryReason.EnumerationChanged); }

        private static void Check(bool condition, string description)
        { if (!condition) throw new InvalidOperationException(description); }

        private static void Equal<T>(T expected, T actual, string description)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(description + " ; attendu=" + expected + ", obtenu=" + actual);
        }

        private static void Run(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("OK     " + name); }
            catch (Exception error)
            {
                failed++;
                Console.WriteLine("ECHEC  " + name + " : " + error.Message);
            }
        }

        private static void TickRange(Scenario test, long first, long last, long step)
        { for (long now = first; now <= last; now += step) test.Tick(now); }

        private static string NewLogDirectory(string caseName)
        {
            if (fixtureDirectory == null)
            {
                fixtureDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "tests-journal-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(fixtureDirectory);
                Console.WriteLine("FICHIERS_TEST : " + fixtureDirectory);
            }
            string directory = Path.Combine(fixtureDirectory, caseName);
            Directory.CreateDirectory(directory);
            return directory;
        }

        // Retry only a rejected nonblocking enqueue, never a disk operation.
        // GetSnapshot observes accepted records, not assumed synchronous writes.
        private static void EnqueueLog(OverlayRecoveryLog log, RecoveryEvent entry)
        {
            long accepted = log.GetWriterSnapshot().Accepted;
            for (int i = 0; i < 128; i++)
            {
                log.Write(entry);
                DiagnosticWriterSnapshot snapshot = log.GetWriterSnapshot();
                if (snapshot.Accepted > accepted) return;
                if (!snapshot.Accepting)
                    throw new InvalidOperationException("Journal ferme avant ingestion : " + snapshot.ErrorCode);
                Thread.Yield();
            }
            throw new InvalidOperationException("Le journal refuse l'ingestion apres 128 essais non bloquants.");
        }

        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
            Console.WriteLine("Haloryva : tests purs de recuperation d'ordre des fenetres");
            Console.WriteLine("Ne valide pas les API Win32, les pixels, la capture ou la fluidite.");

            Run("Deux constats espaces et relecture avant reparation", delegate
            {
                Scenario s = new Scenario();
                s.Tick(0);
                Equal(0, s.Platform.Repairs, "un seul constat ne suffit pas");
                s.Tick(499);
                Equal(1, s.Platform.Inspections, "pas de sonde avant 500 ms");
                s.Tick(500);
                Equal(1, s.Platform.Repairs, "deux constats coherents autorisent une tentative");
                Equal(3, s.Platform.Inspections, "la tentative exige une relecture fraiche");
                Equal(100L, s.Platform.LastRepair.Layer, "le meme calque est transmis");
                Equal(200L, s.Platform.LastRepair.Blocker, "la meme fenetre bloquante est transmise");
            });

            Run("Cadence bornee meme si Tick est appele a chaque image", delegate
            {
                Scenario s = new Scenario();
                s.Platform.Current = Healthy(100);
                TickRange(s, 0, 2000, 16);
                Check(s.Platform.Inspections <= 5, "au plus cinq sondes en deux secondes");
                Equal(0, s.Platform.Repairs, "un ordre sain ne provoque aucune tentative");
            });

            Run("Appels repetes au meme instant ne confirment pas une anomalie", delegate
            {
                Scenario s = new Scenario();
                for (int i = 0; i < 100; i++) s.Tick(0);
                Equal(1, s.Platform.Inspections, "une seule inspection au meme instant");
                Equal(0, s.Platform.Repairs, "pas de confirmation sans delai");
            });

            Run("Anomalie fugace suivie d'un ordre sain", delegate
            {
                Scenario s = new Scenario();
                s.Tick(0);
                s.Platform.Current = Healthy(100);
                TickRange(s, 500, 2500, 500);
                Equal(0, s.Platform.Repairs, "aucune reparation d'une anomalie disparue");
            });

            Run("Les effets ineligibles ne sont ni sondes ni remontes", delegate
            {
                Scenario s = new Scenario();
                for (long now = 0; now <= 10000; now += 16) s.Tick(now, false);
                Equal(0, s.Platform.Inspections, "aucune inspection si ineligible");
                Equal(0, s.Platform.Repairs, "aucune action si desactive, masque ou au repos");
            });

            Run("La desactivation annule une confirmation en cours", delegate
            {
                Scenario s = new Scenario();
                s.Tick(0);
                s.Tick(250, false);
                s.Tick(500);
                Equal(0, s.Platform.Repairs, "le constat precedent la desactivation ne compte plus");
                s.Tick(1000);
                Equal(1, s.Platform.Repairs, "deux nouveaux constats permettent la recuperation");
            });

            Run("Une enumeration inconnue annule la confirmation", delegate
            {
                Scenario s = new Scenario();
                s.Tick(0);
                s.Platform.Current = Unknown(); s.Tick(500);
                s.Platform.Current = Obscured(100, 200); s.Tick(1000);
                Equal(0, s.Platform.Repairs, "la rupture de preuve annule le premier constat");
                s.Tick(1500);
                Equal(1, s.Platform.Repairs, "confirmation reconstruite ensuite");
            });

            RecoveryReason[] protections = new RecoveryReason[] {
                RecoveryReason.DesktopInactive, RecoveryReason.ForegroundUnavailable,
                RecoveryReason.OwnInterface, RecoveryReason.InteractionActive,
                RecoveryReason.FullScreen, RecoveryReason.CaptureActive,
                RecoveryReason.PriorityWindow, RecoveryReason.ProbeBudget,
                RecoveryReason.LayerUnavailable
            };
            foreach (RecoveryReason protection in protections)
            {
                RecoveryReason current = protection;
                Run("Abstention : " + current, delegate
                {
                    Scenario s = new Scenario();
                    s.Platform.Current = Deferred(current);
                    TickRange(s, 0, 5000, 500);
                    Equal(0, s.Platform.Repairs, "le contexte protege interdit la recuperation");
                });
            }

            Run("Un contexte protege annule la confirmation", delegate
            {
                Scenario s = new Scenario();
                s.Tick(0);
                s.Platform.Current = Deferred(RecoveryReason.CaptureActive); s.Tick(500);
                s.Platform.Current = Obscured(100, 200); s.Tick(1000);
                Equal(0, s.Platform.Repairs, "pas de constat conserve a travers la capture");
                s.Tick(1500);
                Equal(1, s.Platform.Repairs, "recuperation possible apres deux constats admissibles");
            });

            Run("Changement de fenetre bloquante", delegate
            {
                Scenario s = new Scenario();
                s.Tick(0);
                s.Platform.Current = Obscured(100, 201); s.Tick(500);
                Equal(0, s.Platform.Repairs, "deux bloqueurs differents ne confirment rien");
                s.Tick(1000);
                Equal(1, s.Platform.Repairs, "le nouveau bloqueur est confirme");
                Equal(201L, s.Platform.LastRepair.Blocker, "reparation du nouveau constat");
            });

            Run("Changement d'identite du calque", delegate
            {
                Scenario s = new Scenario();
                s.Tick(0);
                s.Platform.Current = Obscured(101, 200); s.Tick(500);
                Equal(0, s.Platform.Repairs, "pas de confirmation entre deux calques");
                s.Tick(1000);
                Equal(1, s.Platform.Repairs, "nouvelle identite confirmee");
                Equal(101L, s.Platform.LastRepair.Layer, "la nouvelle identite est transmise");
            });

            Run("Un handle de calque nul ne peut etre repare", delegate
            {
                Scenario s = new Scenario();
                s.Platform.Current = Obscured(0, 200);
                TickRange(s, 0, 3000, 500);
                Equal(0, s.Platform.Repairs, "calque absent");
            });

            Run("Une anomalie sans bloqueur ne peut etre reparee", delegate
            {
                Scenario s = new Scenario();
                s.Platform.Current = Obscured(100, 0);
                TickRange(s, 0, 3000, 500);
                Equal(0, s.Platform.Repairs, "preuve de bloqueur absente");
            });

            Run("Un constat trop ancien ne sert pas de confirmation", delegate
            {
                Scenario s = new Scenario();
                s.Tick(0); s.Tick(2000);
                Equal(0, s.Platform.Repairs, "deux secondes entre constats depassent la fenetre");
                s.Tick(2500);
                Equal(1, s.Platform.Repairs, "la confirmation reprend avec un constat recent");
            });

            Run("La limite de confirmation de 1500 ms reste admissible", delegate
            {
                Scenario s = new Scenario();
                s.Tick(0); s.Tick(1500);
                Equal(1, s.Platform.Repairs, "borne haute incluse");
            });

            Run("L'ordre devenu sain a la relecture interdit la tentative", delegate
            {
                Scenario s = new Scenario();
                s.Platform.Next.Enqueue(Obscured(100, 200));
                s.Platform.Next.Enqueue(Obscured(100, 200));
                s.Platform.Next.Enqueue(Healthy(100));
                s.Tick(0); s.Tick(500);
                Equal(0, s.Platform.Repairs, "l'etat final prime sur les deux anciens constats");
            });

            Run("La capture apparue juste avant la tentative est respectee", delegate
            {
                Scenario s = new Scenario();
                s.Platform.Next.Enqueue(Obscured(100, 200));
                s.Platform.Next.Enqueue(Obscured(100, 200));
                s.Platform.Next.Enqueue(Deferred(RecoveryReason.CaptureActive));
                s.Tick(0); s.Tick(500);
                Equal(0, s.Platform.Repairs, "pas de course volontaire avec la capture");
            });

            Run("Un calque different a la relecture annule la tentative", delegate
            {
                Scenario s = new Scenario();
                s.Platform.Next.Enqueue(Obscured(100, 200));
                s.Platform.Next.Enqueue(Obscured(100, 200));
                s.Platform.Next.Enqueue(Obscured(101, 200));
                s.Tick(0); s.Tick(500);
                Equal(0, s.Platform.Repairs, "pas de reparation d'une nouvelle identite non confirmee");
            });

            Run("Un bloqueur different a la relecture annule la tentative", delegate
            {
                Scenario s = new Scenario();
                s.Platform.Next.Enqueue(Obscured(100, 200));
                s.Platform.Next.Enqueue(Obscured(100, 200));
                s.Platform.Next.Enqueue(Obscured(100, 201));
                s.Tick(0); s.Tick(500);
                Equal(0, s.Platform.Repairs, "pas de reparation sur une preuve devenue differente");
            });

            Run("Acceptation native et ordre retabli restent distincts", delegate
            {
                Scenario s = new Scenario();
                s.Tick(0); s.Tick(500);
                Equal(1, s.EventCount("REPAIR_ACCEPTED"), "l'API a accepte la demande");
                Equal(0, s.EventCount("ORDER_VERIFIED_CLEAR"), "pas de succes presume");
                s.Tick(1000);
                Equal(0, s.EventCount("ORDER_VERIFIED_CLEAR"), "l'ordre est encore mauvais");
                Equal(1, s.EventCount("ORDER_STILL_OBSCURED"), "echec de verification distingue");
            });

            Run("Ordre retabli verifie au releve suivant", delegate
            {
                Scenario s = new Scenario();
                s.Tick(0); s.Tick(500);
                s.Platform.Current = Healthy(100); s.Tick(1000);
                Equal(1, s.EventCount("ORDER_VERIFIED_CLEAR"), "verification differee du meme calque");
                s.Tick(1500);
                Equal(1, s.EventCount("ORDER_VERIFIED_CLEAR"), "pas de journalisation repetee du meme resultat");
            });

            Run("Un autre calque sain ne valide pas la reparation precedente", delegate
            {
                Scenario s = new Scenario();
                s.Tick(0); s.Tick(500);
                s.Platform.Current = Healthy(101); s.Tick(1000);
                Equal(0, s.EventCount("ORDER_VERIFIED_CLEAR"), "identite differente non assimilable a une verification");
            });

            Run("Capture pendant verification ne devient pas un succes", delegate
            {
                Scenario s = new Scenario();
                s.Tick(0); s.Tick(500);
                s.Platform.Current = Deferred(RecoveryReason.CaptureActive); s.Tick(1000);
                Equal(0, s.EventCount("ORDER_VERIFIED_CLEAR"), "verification indisponible");
                Equal(1, s.Platform.Repairs, "aucune nouvelle tentative pendant la capture");
            });

            Run("Code Win32 d'echec conserve", delegate
            {
                Scenario s = new Scenario();
                s.Platform.RepairSuccess = false; s.Platform.RepairError = 5;
                s.Tick(0); s.Tick(500);
                Equal(1, s.EventCount("REPAIR_FAILED"), "echec explicite");
                Equal(0, s.EventCount("REPAIR_ACCEPTED"), "aucune acceptation annoncee");
                bool errorFound = false;
                foreach (RecoveryEvent item in s.Events)
                    if (item.Name == "REPAIR_FAILED" && item.Error == 5) errorFound = true;
                Check(errorFound, "code d'erreur technique attendu");
            });

            Run("Au moins cinq secondes entre tentatives", delegate
            {
                Scenario s = new Scenario();
                TickRange(s, 0, 6500, 100);
                Check(s.Platform.Repairs >= 2, "une nouvelle tentative bornee reste possible");
                for (int i = 1; i < s.Platform.RepairTimes.Count; i++)
                    Check(s.Platform.RepairTimes[i] - s.Platform.RepairTimes[i - 1] >= 5000,
                        "l'intervalle minimal doit etre respecte");
            });

            Run("Trois tentatives au maximum pendant une minute d'echec", delegate
            {
                Scenario s = new Scenario();
                s.Platform.RepairSuccess = false; s.Platform.RepairError = 5;
                TickRange(s, 0, 60000, 500);
                Equal(3, s.Platform.Repairs, "le quota borne aussi les echecs");
            });

            Run("Les lectures saines ne reinitialisent pas le quota", delegate
            {
                Scenario s = new Scenario();
                for (long now = 0; now <= 59000; now += 500)
                {
                    int repairs = s.Platform.RepairTimes.Count;
                    bool verifying = repairs > 0 && now == s.Platform.RepairTimes[repairs - 1] + 500;
                    s.Platform.Current = verifying ? Healthy(100) : Obscured(100, 200);
                    s.Tick(now);
                }
                Equal(3, s.Platform.Repairs, "alternance sain/anormal sans lutte permanente");
            });

            Run("Les abstentions ne reinitialisent pas le quota", delegate
            {
                Scenario s = new Scenario();
                for (long now = 0; now <= 59000; now += 500)
                {
                    int repairs = s.Platform.RepairTimes.Count;
                    bool verifying = repairs > 0 && now == s.Platform.RepairTimes[repairs - 1] + 500;
                    s.Platform.Current = verifying
                        ? Deferred(RecoveryReason.PriorityWindow) : Obscured(100, 200);
                    s.Tick(now);
                }
                Equal(3, s.Platform.Repairs, "quota conserve a travers les contextes proteges");
            });

            Run("La desactivation ne reinitialise pas le quota", delegate
            {
                Scenario s = new Scenario();
                for (long now = 0; now <= 59000; now += 500)
                    s.Tick(now, now % 6000 != 1500);
                Equal(3, s.Platform.Repairs, "desactiver/reactiver ne contourne pas le quota");
            });

            Run("Le quota glissant libere une place apres soixante secondes", delegate
            {
                Scenario s = new Scenario();
                TickRange(s, 0, 60000, 500);
                Equal(3, s.Platform.Repairs, "quota encore plein avant expiration de la premiere tentative");
                TickRange(s, 60500, 62000, 500);
                Equal(4, s.Platform.Repairs, "une place liberee permet une tentative confirmee");
                Check(s.Platform.RepairTimes[3] - s.Platform.RepairTimes[0] >= 60000,
                    "la fenetre glissante ne doit pas expirer trop tot");
            });

            Run("Exception de sonde absorbee et signalee", delegate
            {
                Scenario s = new Scenario();
                s.Platform.ThrowOnInspect = true;
                s.Tick(0);
                Equal(0, s.Platform.Repairs, "aucune action apres une lecture invalide");
                Check(s.EventCount("NATIVE_ERROR") >= 1, "erreur technique signalee");
                s.Platform.ThrowOnInspect = false;
                TickRange(s, 500, 10000, 500);
                Check(s.Platform.Repairs >= 1, "une panne de sonde temporaire ne bloque pas definitivement");
            });

            Run("Exception de reparation compte dans les limites", delegate
            {
                Scenario s = new Scenario();
                s.Platform.ThrowOnRepair = true;
                TickRange(s, 0, 59000, 500);
                Equal(3, s.Platform.Repairs, "une exception ne contourne pas le quota");
                Check(s.EventCount("NATIVE_ERROR") >= 1, "exception technique signalee");
                Equal(0, s.EventCount("REPAIR_ACCEPTED"), "aucune acceptation inventee");
            });

            Run("Une panne du journal ne perturbe pas la politique", delegate
            {
                FakePlatform platform = new FakePlatform();
                OverlayRecoveryPolicy policy = new OverlayRecoveryPolicy(platform,
                    delegate(RecoveryEvent value) { throw new InvalidOperationException("TEST_LOG_EXCEPTION"); });
                platform.Now = 0; policy.Tick(0, true);
                platform.Now = 500; policy.Tick(500, true);
                Equal(1, platform.Repairs, "la recuperation reste bornee malgre l'indisponibilite du journal");
            });

            Run("Une regression d'horloge differe l'action", delegate
            {
                Scenario s = new Scenario();
                s.Tick(1000);
                s.Tick(900);
                Equal(0, s.Platform.Repairs, "pas de confirmation avec un temps negatif");
                s.Tick(1000);
                Equal(0, s.Platform.Repairs, "pas de tentative immediate apres regression");
            });

            Run("Temps negatif refuse sans action", delegate
            {
                Scenario s = new Scenario();
                s.Tick(-1000); s.Tick(-500);
                Equal(0, s.Platform.Repairs, "temps monotone invalide");
            });

            Run("Echantillonnage de 511 ms sous la cible de deux secondes", delegate
            {
                Scenario s = new Scenario();
                s.Tick(0); s.Tick(511);
                Equal(1, s.Platform.Repairs, "une anomalie admissible persistante declenche la tentative");
                Check(s.Platform.RepairTimes[0] <= 2000, "cible de la politique, hors delais du thread UI");
            });

            Run("Sequence 91 constats anormaux puis 85 sains", delegate
            {
                Scenario s = new Scenario();
                // Rejeu logique des phases observees, sans pretendre rejouer Win32 ni les pixels.
                for (int i = 0; i < 91; i++) s.Tick(i * 511L);
                Equal(3, s.Platform.Repairs, "la phase persistante reste bornee");
                int before = s.Platform.Repairs;
                s.Platform.Current = Healthy(100);
                for (int i = 91; i < 176; i++) s.Tick(i * 511L);
                Equal(before, s.Platform.Repairs, "aucune tentative dans la phase saine");
            });

            Run("Journal borne avec conservation des entrees precedentes", delegate
            {
                string directory = NewLogDirectory("limite");
                string path = Path.Combine(directory, "recuperation-affichage.log");
                const string marker = "ENTREE_DE_TEST_A_CONSERVER\r\n";
                string sample = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture) +
                    ";REPAIR_ACCEPTED;reason=None;win32=0;attempts=1\r\n";
                int entryBytes = Encoding.UTF8.GetByteCount(sample);
                int initialSize = OverlayRecoveryLog.MaximumBytes - entryBytes - 4;
                string initial = marker + new String('x', initialSize - marker.Length - 2) + "\r\n";
                File.WriteAllText(path, initial, new UTF8Encoding(false));
                OverlayRecoveryLog log = new OverlayRecoveryLog(directory);
                RecoveryEvent entry = new RecoveryEvent("REPAIR_ACCEPTED", RecoveryReason.None, 0, 1);
                try
                {
                    EnqueueLog(log, entry);
                    EnqueueLog(log, entry);
                }
                finally { Check(log.Close(), "fermeture bornee du journal au plafond"); }
                DiagnosticWriterSnapshot snapshot = log.GetWriterSnapshot();
                long lengthAtCap = new FileInfo(path).Length;
                Equal((long)initialSize + entryBytes, lengthAtCap, "une ligne complete ajoutee avant le plafond");
                Check(lengthAtCap <= OverlayRecoveryLog.MaximumBytes, "le plafond est respecte en octets");
                Check(File.ReadAllText(path, Encoding.UTF8).StartsWith(initial, StringComparison.Ordinal),
                    "les entrees anciennes ne sont pas effacees");
                Check(log.CapReached && snapshot.Faulted && !snapshot.JournalComplete && snapshot.Failed >= 1,
                    "journal plafonne explicitement incomplet");
                log.Write(entry);
                Equal(lengthAtCap, new FileInfo(path).Length, "aucune ecriture apres fermeture au plafond");
            });

            Run("Journal inaccessible sans exception ni modification du fichier obstacle", delegate
            {
                string directory = NewLogDirectory("indisponible");
                string obstacle = Path.Combine(directory, "obstacle.txt");
                const string initial = "FICHIER_FACTICE_A_CONSERVER";
                File.WriteAllText(obstacle, initial, new UTF8Encoding(false));
                OverlayRecoveryLog log = new OverlayRecoveryLog(obstacle);
                RecoveryEvent entry = new RecoveryEvent("REPAIR_FAILED", RecoveryReason.NativeError, 5, 1);
                try { EnqueueLog(log, entry); }
                finally { Check(log.Close(), "fermeture du worker apres erreur disque"); }
                DiagnosticWriterSnapshot snapshot = log.GetWriterSnapshot();
                Check(snapshot.Completed && snapshot.Faulted && !snapshot.JournalComplete && !log.CapReached,
                    "panne disque distinguee du plafond et d'un journal complet");
                Equal(initial, File.ReadAllText(obstacle, Encoding.UTF8), "aucun ecrasement du fichier obstacle");
            });

            Run("Journal sur worker sans bloquer les evenements suivants", delegate
            {
                string directory = NewLogDirectory("worker");
                int callerThread = Thread.CurrentThread.ManagedThreadId, workerThread = 0;
                int appendCalls = 0;
                using (ManualResetEvent entered = new ManualResetEvent(false))
                using (ManualResetEvent release = new ManualResetEvent(false))
                {
                    OverlayRecoveryLog log = new OverlayRecoveryLog(directory, delegate
                    {
                        if (Interlocked.Increment(ref appendCalls) == 1)
                        {
                            workerThread = Thread.CurrentThread.ManagedThreadId;
                            entered.Set();
                            if (!release.WaitOne(5000)) throw new TimeoutException("TEST_WORKER_NOT_RELEASED");
                        }
                    });
                    RecoveryEvent entry = new RecoveryEvent("REPAIR_ACCEPTED", RecoveryReason.None, 0, 1);
                    bool closed = false;
                    try
                    {
                        EnqueueLog(log, entry);
                        Check(entered.WaitOne(5000), "le backend est atteint sur le worker");
                        for (int i = 0; i < 10; i++) log.Write(entry);
                        DiagnosticWriterSnapshot held = log.GetWriterSnapshot();
                        Check(workerThread != callerThread && held.InFlight == 1 && held.Pending == 10,
                            "les nouveaux evenements s'enfilent pendant que le disque est bloque");
                        Check(!File.Exists(Path.Combine(directory, "recuperation-affichage.log")),
                            "aucune ecriture contourne le worker retenu");
                    }
                    finally
                    {
                        release.Set();
                        closed = log.Close();
                    }
                    Check(closed, "fermeture bornee apres liberation du worker");
                    DiagnosticWriterSnapshot done = log.GetWriterSnapshot();
                    Check(done.Completed && !done.Faulted && done.Written == 11 && done.Pending == 0,
                        "les onze evenements acceptes sont ecrits avant fermeture");
                    string[] lines = File.ReadAllLines(Path.Combine(directory, "recuperation-affichage.log"), Encoding.UTF8);
                    Equal(11, lines.Length, "aucune ligne perdue ou dupliquee apres liberation");
                    foreach (string line in lines)
                        Check(line.EndsWith(";REPAIR_ACCEPTED;reason=None;win32=0;attempts=1", StringComparison.Ordinal),
                            "format historique conserve");
                }
            });

            Run("Aucune ecriture du journal entre relecture et reparation", delegate
            {
                FakePlatform platform = new FakePlatform();
                bool premature = false;
                OverlayRecoveryPolicy policy = new OverlayRecoveryPolicy(platform, delegate(RecoveryEvent value)
                {
                    if (value.Name == "OBSTRUCTION_CONFIRMED" && platform.Repairs == 0) premature = true;
                });
                platform.Now = 0; policy.Tick(0, true);
                platform.Now = 500; policy.Tick(500, true);
                Equal(1, platform.Repairs, "une tentative est faite");
                Check(!premature, "le journal synchrone ne precede pas l'appel natif");
            });

            Run("Veto final distingue d'un echec Win32 et d'une verification", delegate
            {
                Scenario s = new Scenario();
                s.Platform.CancelRepairReason = RecoveryReason.DesktopInactive;
                s.Tick(0); s.Tick(500);
                Equal(1, s.EventCount("REPAIR_CANCELLED"), "le contexte final annule l'action");
                Equal(0, s.EventCount("REPAIR_FAILED"), "pas de faux code d'erreur Win32");
                s.Platform.Current = Healthy(100); s.Tick(1000);
                Equal(0, s.EventCount("ORDER_VERIFIED_CLEAR"), "pas de resultat attribue a un appel non effectue");
            });

            Console.WriteLine("RESULTAT : " + passed + " reussis, " + failed + " echecs.");
            return failed == 0 ? 0 : 1;
        }
    }
}
