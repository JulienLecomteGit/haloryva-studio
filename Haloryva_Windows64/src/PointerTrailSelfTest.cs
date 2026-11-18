using System;

namespace CursorFlow
{
    // Production buffer tests. No hooks, forms, files, settings or Windows API calls.
    internal static class PointerTrailSelfTest
    {
        private static int checks;
        private static void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException(name);
            checks++; Console.WriteLine("OK INPUT " + checks + " " + name);
        }
        internal static int Run()
        {
            checks = 0;
            try
            {
                DateTime zero = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                PointerTrailBatch b = new PointerTrailBatch();
                Check(b.Count == 0 && !b.BreakBefore && b.Dropped == 0, "empty buffer");
                b.Add(1, 2, DateTime.MinValue);
                Check(b.Count == 0, "missing source timestamp is not fabricated");
                b.Add(1, 2, zero);
                Check(b.Count == 1 && b[0].X == 1 && b[0].Y == 2 && b[0].Time == zero, "first source sample");
                b.Add(9, 4, zero.AddTicks(5000));
                Check(b.Count == 1 && b[0].X == 9 && b[0].Time == zero.AddTicks(5000), "latest endpoint in a half-ms slot");
                b.Add(10, 5, zero.AddMilliseconds(2));
                Check(b.Count == 2 && b[1].X == 10, "slot origin does not move with replacements");
                DateTime motionTime = b[1].Time;
                b.Add(10, 5, zero.AddMilliseconds(500));
                Check(b.Count == 2 && b[1].Time == motionTime, "stationary pointer does not renew trail age");
                b.Add(20, 6, zero.AddMilliseconds(600));
                Check(b.Count == 3 && (b[2].Time - b[1].Time).TotalMilliseconds == 598,
                    "long pause remains visible to history gap detection");

                b = new PointerTrailBatch();
                for (int i = 0; i <= 40; i++) b.Add(i, i % 4, zero.AddMilliseconds(i));
                Check(b.Count == 21, "1000 Hz input creates 21 slots in 40 ms");
                Check(b[b.Count - 1].X == 40 && b[b.Count - 1].Time == zero.AddMilliseconds(40), "high-rate exact newest endpoint");
                Check(!b.BreakBefore && b.Dropped == 0, "ordinary frame does not discard samples");
                bool increasing = true;
                for (int i = 1; i < b.Count; i++) increasing &= b[i].Time > b[i - 1].Time;
                Check(increasing, "sample timestamps strictly increase across slots");

                b = new PointerTrailBatch();
                for (int i = 0; i < 100; i++) b.Add(i, -i, zero.AddMilliseconds(i * 2));
                Check(b.Count == PointerTrailBatch.Capacity, "capacity is fixed at 64 samples");
                Check(b.Dropped == 36 && b.BreakBefore, "overflow reports loss and requires a break");
                Check(b[0].X == 36 && b[63].X == 99, "overflow retains newest chronological window");
                bool ordered = true;
                for (int i = 1; i < b.Count; i++) ordered &= b[i].X == b[i - 1].X + 1 && b[i].Time > b[i - 1].Time;
                Check(ordered, "ring wrap preserves all retained turns in order");
                b.Add(200, 300, zero.AddMilliseconds(199));
                Check(b.Count == 64 && b[b.Count - 1].X == 200 && b.Dropped == 36, "tail replacement after wrap does not evict another sample");

                b = new PointerTrailBatch();
                for (int i = 0; i < 20; i++) b.Add(i * 10, i % 2 == 0 ? 0 : 40, zero.AddMilliseconds(i * 5));
                bool turns = b.Count == 20;
                for (int i = 0; i < b.Count; i++) turns &= b[i].Y == (i % 2 == 0 ? 0 : 40);
                Check(turns, "intermediate zigzag vertices survive batching");
                b.MarkDiscontinuity();
                Check(b.BreakBefore && b.Count == 20 && b.Dropped == 0, "explicit mailbox gap does not invent a lost sample count");
                b.Add(-8, -9, zero.AddMilliseconds(1));
                Check(b.Count == 1 && b.BreakBefore && b.Dropped == 20, "clock regression starts a separate batch");
                Check(b[0].X == -8 && b[0].Y == -9 && b[0].Time == zero.AddMilliseconds(1), "regression endpoint and timestamp preserved");

                b = new PointerTrailBatch();
                for (int i = 0; i < 10000; i++) b.Add(i, i % 7, zero.AddTicks(i * 1000L));
                Check(b.Count <= 64 && b.BreakBefore, "10000 Hz stress stays bounded");
                Check(b[b.Count - 1].X == 9999, "10000 Hz stress keeps the latest endpoint");
                bool lower = false, upper = false;
                try { PointerTrailSample ignored = b[-1]; } catch (ArgumentOutOfRangeException) { lower = true; }
                try { PointerTrailSample ignored = b[b.Count]; } catch (ArgumentOutOfRangeException) { upper = true; }
                Check(lower && upper, "index guards reject both invalid bounds");
                Check(!PointerTrailFilter.Accept(2, 3, zero, zero), "queued input from a previous session is rejected");
                Check(!PointerTrailFilter.Accept(3, 3, zero, zero.AddTicks(1)), "queued input before a UI restart is rejected");
                Check(PointerTrailFilter.Accept(3, 3, zero, zero), "exact restart timestamp is accepted");
                Check(PointerTrailFilter.Accept(3, 3, zero.AddMilliseconds(1), zero), "fresh input after restart is accepted");
                Check(!PointerTrailFilter.Accept(3, 3, DateTime.MinValue, DateTime.MinValue), "unknown timestamp cannot revive a trail");

                Console.WriteLine("TOTAL " + checks + " controles positions horodatees C# reussis.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("ECHEC POSITIONS : " + ex); return 1; }
        }
    }
}
