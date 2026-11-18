using System;

namespace CursorFlow
{
    // Integrated mailbox checks, run only by --trail-selftest before application startup.
    internal static class InputTrailIntegrationSelfTest
    {
        private static int checks;
        private static void Check(bool ok, string name)
        { if (!ok) throw new InvalidOperationException(name); checks++; Console.WriteLine("OK MAILBOX " + checks + " " + name); }
        private static InputCommand Move(int x, int ms, long sequence)
        {
            return new InputCommand { Kind = InputKind.Move, X = x, Y = -x, Sequence = sequence,
                TrailTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms) };
        }
        internal static int Run()
        {
            checks = 0;
            try
            {
                InputMailbox box = new InputMailbox(64);
                for (int i = 0; i < 10; i++) Check(box.TryPost(Move(i, i * 4, i + 1), false), "post move " + i);
                Check(box.Count == 1, "moves still occupy one queue command");
                InputCommand latest = box.Take();
                Check(latest.X == 9 && latest.Sequence == 10 && latest.TrailSamples.Count == 10,
                    "latest command semantics with intermediate trail vertices");
                box.TryPost(Move(20, 44, 11), false);
                InputCommand later = box.Take();
                Check(!Object.ReferenceEquals(latest.TrailSamples, later.TrailSamples) && latest.TrailSamples.Count == 10,
                    "batch taken by UI is never mutated by future producer input");

                box = new InputMailbox(64);
                box.TryPost(Move(1, 0, 1), false);
                box.TryPost(new InputCommand { Kind = InputKind.Action, Action = "Halo : activer / arrêter", Sequence = 2 }, false);
                box.TryPost(Move(2, 4, 3), false);
                Check(box.Count == 3, "an action remains a barrier between movement batches");
                InputCommand a = box.Take(), action = box.Take(), b = box.Take();
                Check(a.Kind == InputKind.Move && action.Kind == InputKind.Action && b.Kind == InputKind.Move &&
                    a.Sequence == 1 && action.Sequence == 2 && b.Sequence == 3,
                    "ordering and acknowledgement sequences stay unchanged");
                Check(a.TrailSamples.Count == 1 && b.TrailSamples.Count == 1,
                    "samples do not merge across a command barrier");
                box = new InputMailbox(64);
                InputCommand oldSession = Move(1, 0, 1), newSession = Move(2, 4, 2);
                oldSession.Session = 7; newSession.Session = 8;
                box.TryPost(oldSession, false); box.TryPost(newSession, false);
                Check(box.Count == 2 && box.Take().Session == 7 && box.Take().Session == 8,
                    "consecutive moves from different sessions never share one batch");

                box = new InputMailbox(64);
                for (int i = 0; i < 32; i++) box.TryPost(new InputCommand { Kind = InputKind.Observe }, false);
                Check(!box.TryPost(Move(3, 0, 1), false), "reserved capacity still rejects a non-terminal move");
                Check(box.TryPost(new InputCommand { Kind = InputKind.CancelStroke }, true), "reserved capacity still accepts a terminal command");
                box.Take(); box.Take();
                Check(box.TryPost(Move(4, 4, 2), false), "movement accepted after capacity is available");
                InputCommand recovered = null, next;
                while ((next = box.Take()) != null) if (next.Kind == InputKind.Move) recovered = next;
                Check(recovered != null && recovered.TrailSamples.BreakBefore,
                    "lost input forces a trail break instead of a fabricated bridge");

                box.TryPost(Move(5, 8, 3), false); box.Clear(); box.TryPost(Move(6, 12, 4), false);
                Check(box.Take().TrailSamples.BreakBefore, "explicit queue reset breaks the next trail batch");
                Console.WriteLine("TOTAL " + checks + " controles integration mailbox C# reussis.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("ECHEC MAILBOX : " + ex); return 1; }
        }
    }
}
