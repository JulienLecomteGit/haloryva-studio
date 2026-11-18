using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace CursorFlow
{
    internal static class DiagnosticWriterSelfTest
    {
        private static int checks;
        private static readonly DateTime origin = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc).AddTicks(1234);
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks++; Console.WriteLine("OK DIAGNOSTIC " + checks + " " + name);
        }
        private static void Rejects<T>(Action action, string name) where T : Exception
        {
            bool rejected = false;
            try { action(); } catch (T) { rejected = true; }
            Check(rejected, name);
        }
        private static void Wait(WaitHandle signal, string name)
        { if (!signal.WaitOne(5000)) throw new TimeoutException(name); }
        private static string End(DiagnosticWriterSnapshot state)
        { return "END accepted=" + state.Accepted + ",written=" + state.Written + ",dropped=" + state.Dropped + "\r\n"; }

        private static void ArgumentsAndFormat()
        {
            Rejects<ArgumentNullException>(delegate { new BufferedDiagnosticWriter(null, 1, 1); }, "null backend rejected before thread creation");
            Rejects<ArgumentOutOfRangeException>(delegate { new BufferedDiagnosticWriter(delegate { }, 0, 1); }, "zero queue capacity rejected");
            Rejects<ArgumentOutOfRangeException>(delegate { new BufferedDiagnosticWriter(delegate { }, 4097, 1); }, "unbounded queue request rejected");
            Rejects<ArgumentOutOfRangeException>(delegate { new BufferedDiagnosticWriter(delegate { }, 2, 0); }, "zero batch rejected");
            Rejects<ArgumentOutOfRangeException>(delegate { new BufferedDiagnosticWriter(delegate { }, 2, 3); }, "batch larger than queue rejected");
            Rejects<ArgumentException>(delegate
            { new BufferedDiagnosticWriter(delegate { }, 2, 1, null, new string('x', BufferedDiagnosticWriter.RecordCharacterLimit + 1)); },
                "oversized initial event rejected before thread creation");
            string text = BufferedDiagnosticWriter.FormatRecord(origin, "SNAPSHOT", "label=éclat");
            Check(text == "2026-11-18T12:00:00.0001234Z;SNAPSHOT;label=éclat\r\n", "event time retains its original fractional UTC value and UTF8 text");
            string safe = BufferedDiagnosticWriter.FormatRecord(origin, "EVENT;bad\r\n", "one;two\r\nthree");
            Check(safe.Split(';').Length == 3 && safe.IndexOf('\n') == safe.Length - 1 && safe.IndexOf('\r') == safe.Length - 2,
                "record cannot inject another row or CSV column");
            Check(BufferedDiagnosticWriter.FormatRecord(origin, null, null).EndsWith(";;\r\n", StringComparison.Ordinal),
                "empty optional fields remain well formed");
            using (BufferedDiagnosticWriter writer = new BufferedDiagnosticWriter(delegate { }, 1, 1))
            {
                Rejects<ArgumentNullException>(delegate { writer.TryEnqueue(null); }, "null record rejected");
                Rejects<ArgumentOutOfRangeException>(delegate { writer.Complete(-1); }, "negative closure deadline rejected");
                Check(writer.Complete(5000) && writer.GetSnapshot().JournalComplete, "empty writer closes without fabricated events");
            }
        }

        private static void SlowBackendAndBounds()
        {
            using (ManualResetEvent entered = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            {
                List<string> writes = new List<string>(); int caller = Thread.CurrentThread.ManagedThreadId;
                int ioThread = 0, active = 0, peak = 0;
                BufferedDiagnosticWriter writer = new BufferedDiagnosticWriter(delegate(string text)
                {
                    ioThread = Thread.CurrentThread.ManagedThreadId;
                    int current = Interlocked.Increment(ref active); peak = Math.Max(peak, current);
                    try
                    {
                        if (text == "A\r\n") { entered.Set(); Wait(release, "blocked fake disk release"); }
                        lock (writes) writes.Add(text);
                    }
                    finally { Interlocked.Decrement(ref active); }
                }, 3, 2, End, "A\r\n");
                try
                {
                    Wait(entered, "first async append");
                    Check(ioThread != caller, "backend runs on another thread while producer remains available");
                    Check(writer.TryEnqueue("B\r\n") && writer.TryEnqueue("C\r\n") && writer.TryEnqueue("D\r\n"),
                        "producer accepts pending events while disk remains blocked");
                    Check(!writer.TryEnqueue("overflow\r\n"), "full queue rejects newest event without waiting");
                    DiagnosticWriterSnapshot state = writer.GetSnapshot();
                    Check(state.Pending == 3 && state.InFlight == 1 && state.MaximumPending == 3 && state.QueueCapacity == 3,
                        "queue and in-flight batch have separate explicit bounds");
                    Check(state.Requested == 5 && state.Accepted == 4 && state.Dropped == 1 && state.OverflowDropped == 1,
                        "overflow is visible and accepted records are not replaced");
                    Check(!writer.Complete(0), "zero-deadline close returns while disk is still blocked");
                    Check(!writer.Accepting && !writer.TryEnqueue("too late\r\n"), "closing writer refuses later input");
                    state = writer.GetSnapshot();
                    Check(state.TimedOut && !state.Completed && !state.JournalComplete && state.RejectedAfterClose == 1,
                        "timeout and post-close rejection cannot appear as a complete journal");
                    release.Set();
                    Check(writer.Complete(5000), "same writer can finish after timeout without abort or concurrent retry");
                    state = writer.GetSnapshot();
                    Check(state.Completed && state.Written == 4 && state.Failed == 0 && state.Pending == 0 && state.InFlight == 0,
                        "accepted events finish exactly once after release");
                    Check(state.FooterWritten && state.TimedOut && !state.JournalComplete && state.Dropped == 2,
                        "reserved final record and previous incompleteness survive queue saturation");
                    lock (writes)
                    {
                        Check(writes.Count == 4 && writes[0] == "A\r\n" && writes[1] == "B\r\nC\r\n" &&
                            writes[2] == "D\r\n" && writes[3] == "END accepted=4,written=4,dropped=2\r\n",
                            "batch order and reserved final record are exact");
                    }
                    Check(peak == 1, "no two backend writes overlap");
                    Check(writer.Complete(0) && writer.GetSnapshot().Written == 4, "repeated close neither repeats records nor resets counters");
                }
                finally { release.Set(); writer.Complete(5000); writer.Dispose(); }
            }
        }

        private static void ExplicitLosses()
        {
            using (ManualResetEvent entered = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            {
                BufferedDiagnosticWriter writer = new BufferedDiagnosticWriter(delegate(string text)
                { if (text == "first") { entered.Set(); Wait(release, "bounded record test release"); } }, 4, 2, End, "first");
                try
                {
                    Wait(entered, "bounded record test first append");
                    Check(!writer.TryEnqueue(new string('x', BufferedDiagnosticWriter.RecordCharacterLimit + 1)),
                        "one oversized record cannot defeat the queue memory bound");
                    writer.RecordContentionDrop();
                    DiagnosticWriterSnapshot state = writer.GetSnapshot();
                    Check(state.Requested == 3 && state.Accepted == 1 && state.Dropped == 2 &&
                        state.OversizedDropped == 1 && state.ContentionDropped == 1,
                        "size and producer contention losses are counted independently");
                    release.Set();
                    Check(writer.Complete(5000) && !writer.GetSnapshot().JournalComplete,
                        "successful disk writes do not hide prior loss");
                }
                finally { release.Set(); writer.Complete(5000); writer.Dispose(); }
            }
        }

        private static void BackendFailures()
        {
            using (ManualResetEvent entered = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            {
                int attempts = 0, finals = 0;
                BufferedDiagnosticWriter writer = new BufferedDiagnosticWriter(delegate(string text)
                {
                    Interlocked.Increment(ref attempts); entered.Set(); Wait(release, "failing backend release");
                    throw new IOException("synthetic private path and message must not enter metadata");
                }, 4, 2, delegate(DiagnosticWriterSnapshot state) { finals++; return "END"; }, "A");
                try
                {
                    Wait(entered, "failing backend entered");
                    Check(writer.TryEnqueue("B") && writer.TryEnqueue("C"), "events can queue before an I/O failure is known");
                    release.Set();
                    Check(writer.Complete(5000), "failed worker still terminates and allows closure");
                    DiagnosticWriterSnapshot state = writer.GetSnapshot();
                    Check(state.Faulted && state.Completed && !state.JournalComplete && !state.FooterWritten,
                        "failure is distinct from successful completion");
                    Check(state.Accepted == 3 && state.Written == 0 && state.Failed == 3 && state.Pending == 0 && state.InFlight == 0,
                        "failed batch and abandoned pending records remain unconfirmed");
                    Check(state.ErrorCode == "IOException" && state.WriteFailures == 1,
                        "error metadata exposes a bounded type code, not path or exception message");
                    Check(writer.Complete(0) && attempts == 1 && finals == 0,
                        "failed append is never retried or followed by a false end-of-session marker");
                }
                finally { release.Set(); writer.Complete(5000); writer.Dispose(); }
            }
            using (BufferedDiagnosticWriter writer = new BufferedDiagnosticWriter(delegate(string text)
                { if (text == "END") throw new UnauthorizedAccessException("synthetic"); },
                2, 1, delegate { return "END"; }, "A"))
            {
                Check(writer.Complete(5000), "failure of final record still terminates worker");
                DiagnosticWriterSnapshot state = writer.GetSnapshot();
                Check(state.Written == 1 && state.Failed == 0 && state.Faulted && !state.FooterWritten &&
                    !state.JournalComplete && state.ErrorCode == "UnauthorizedAccessException",
                    "missing final marker cannot be reported as complete even when events were written");
            }
        }

        private static void DisposeDuringWrite()
        {
            using (ManualResetEvent entered = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            {
                int calls = 0;
                BufferedDiagnosticWriter writer = new BufferedDiagnosticWriter(delegate(string text)
                { Interlocked.Increment(ref calls); entered.Set(); Wait(release, "dispose release"); }, 2, 1, null, "A");
                try
                {
                    Wait(entered, "dispose writer entered");
                    writer.Dispose();
                    Check(!writer.Accepting && !writer.GetSnapshot().Completed, "dispose requests closure without waiting on an active disk call");
                    release.Set();
                    Check(writer.Complete(5000) && writer.GetSnapshot().JournalComplete && calls == 1,
                        "active worker finishes safely after dispose with no disposed wait handle race");
                    writer.Dispose();
                    Check(writer.Complete(0) && calls == 1, "repeated dispose and closure are harmless");
                }
                finally { release.Set(); writer.Complete(5000); writer.Dispose(); }
            }
        }

        private static void ConcurrentProducers()
        {
            using (ManualResetEvent entered = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            {
                List<string> writes = new List<string>(); HashSet<string> admitted = new HashSet<string>();
                Thread[] producers = new Thread[4];
                BufferedDiagnosticWriter writer = new BufferedDiagnosticWriter(delegate(string text)
                {
                    if (text == "START\r\n") { entered.Set(); Wait(release, "concurrent backend release"); }
                    lock (writes) writes.Add(text);
                }, 1024, 16, null, "START\r\n");
                try
                {
                    Wait(entered, "concurrent first append");
                    for (int p = 0; p < producers.Length; p++)
                    {
                        int number = p;
                        producers[p] = new Thread(delegate()
                        {
                            for (int i = 0; i < 128; i++)
                            {
                                string id = number + ":" + i;
                                if (writer.TryEnqueue(id + "\r\n")) lock (admitted) admitted.Add(id);
                            }
                        });
                        producers[p].IsBackground = true; producers[p].Start();
                    }
                    bool allJoined = true;
                    foreach (Thread producer in producers) if (!producer.Join(5000)) allJoined = false;
                    Check(allJoined, "concurrent producers finish while disk remains blocked");
                    DiagnosticWriterSnapshot state = writer.GetSnapshot();
                    Check(state.Requested == 513 && state.Requested == state.Accepted + state.Dropped &&
                        state.OverflowDropped == 0 && state.Dropped == state.ContentionDropped,
                        "concurrent admission or contention accounts for every attempt");
                    Check(state.Accepted == admitted.Count + 1 && state.Pending == admitted.Count && state.InFlight == 1 &&
                        state.MaximumPending <= state.QueueCapacity, "concurrent producers preserve the bound and exact admitted count");
                    release.Set();
                    Check(writer.Complete(5000), "all admitted concurrent events drain normally");
                    string all; lock (writes) all = String.Concat(writes.ToArray());
                    string[] lines = all.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                    HashSet<string> actual = new HashSet<string>(lines);
                    bool exact = lines.Length == admitted.Count + 1 && actual.Count == lines.Length && actual.Contains("START");
                    foreach (string id in admitted) if (!actual.Contains(id)) exact = false;
                    Check(exact, "every admitted event appears once, without duplicate or substitution");
                    int[] previous = new int[] { -1, -1, -1, -1 }; bool ordered = true;
                    foreach (string line in lines)
                    {
                        if (line == "START") continue;
                        string[] values = line.Split(':');
                        int p = Int32.Parse(values[0], CultureInfo.InvariantCulture), i = Int32.Parse(values[1], CultureInfo.InvariantCulture);
                        if (i <= previous[p]) ordered = false;
                        previous[p] = i;
                    }
                    Check(ordered, "accepted events retain each producer's order");
                }
                finally
                {
                    release.Set();
                    foreach (Thread producer in producers) if (producer != null) producer.Join(5000);
                    writer.Complete(5000); writer.Dispose();
                }
            }
        }

        private static void RealUtf8File()
        {
            string directory = Path.Combine(Environment.CurrentDirectory,
                "Preuves-diagnostic-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture));
            if (Directory.Exists(directory)) throw new IOException("Synthetic proof folder already exists.");
            Directory.CreateDirectory(directory);
            string file = Path.Combine(directory, "diagnostic-synthetique.csv");
            string initial = BufferedDiagnosticWriter.FormatRecord(origin, "SESSION_START", "label=éclat");
            string footer = null; int ioThread = 0;
            File.WriteAllText(file, "utc;evenement;details\r\n", new UTF8Encoding(false));
            using (BufferedDiagnosticWriter writer = new BufferedDiagnosticWriter(delegate(string text)
            { ioThread = Thread.CurrentThread.ManagedThreadId; File.AppendAllText(file, text, new UTF8Encoding(false)); },
                4, 2, delegate(DiagnosticWriterSnapshot state)
                {
                    footer = BufferedDiagnosticWriter.FormatRecord(state.CloseRequestedUtc.Value, "SESSION_END", "synthetic=1");
                    return footer;
                }, initial))
            {
                Check(writer.Complete(5000), "real synthetic CSV is drained before inspection");
                DiagnosticWriterSnapshot state = writer.GetSnapshot();
                Check(state.Started && state.Completed && state.JournalComplete && state.FooterWritten && !state.TimedOut &&
                    state.Requested == 1 && state.Accepted == 1 && state.Written == 1,
                    "clean session has a complete final snapshot");
                byte[] bytes = File.ReadAllBytes(file);
                Check(bytes.Length > 3 && !(bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191),
                    "CSV uses UTF8 without an unexpected BOM");
                Check(Encoding.UTF8.GetString(bytes) == "utc;evenement;details\r\n" + initial + footer,
                    "on-disk header, event UTC, accented text and final marker are exact");
                Check(ioThread != Thread.CurrentThread.ManagedThreadId, "real file appends use the worker thread");
            }
            using (BufferedDiagnosticWriter next = new BufferedDiagnosticWriter(delegate { }, 1, 1, null, "next"))
            {
                Check(next.Complete(5000) && next.GetSnapshot().JournalComplete && next.GetSnapshot().Written == 1,
                    "a new writer after closure has independent state and counters");
            }
        }

        internal static int Run()
        {
            // Reuse caller writers; the same suite runs inside redirected WinExe.
            checks = 0;
            try
            {
                ArgumentsAndFormat(); SlowBackendAndBounds(); ExplicitLosses(); BackendFailures();
                DisposeDuringWrite(); ConcurrentProducers(); RealUtf8File();
                Console.WriteLine("TOTAL DIAGNOSTIC " + checks + " controles ecriture asynchrone ; echecs=0.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("ECHEC DIAGNOSTIC : " + ex); return 1; }
        }
    }
}
