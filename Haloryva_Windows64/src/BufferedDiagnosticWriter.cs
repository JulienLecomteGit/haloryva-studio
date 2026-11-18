using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace CursorFlow
{
    // Immutable view: counts refer to accepted event records, not to the header
    // or reserved final record. A successful backend call confirms a whole batch.
    internal sealed class DiagnosticWriterSnapshot
    {
        public bool Started { get; internal set; }
        public bool Accepting { get; internal set; }
        public bool Completed { get; internal set; }
        public bool TimedOut { get; internal set; }
        public bool Faulted { get; internal set; }
        public bool FooterExpected { get; internal set; }
        public bool FooterWritten { get; internal set; }
        public bool JournalComplete { get; internal set; }
        public long Requested { get; internal set; }
        public long Accepted { get; internal set; }
        public long Written { get; internal set; }
        public long Dropped { get; internal set; }
        public long OverflowDropped { get; internal set; }
        public long ContentionDropped { get; internal set; }
        public long OversizedDropped { get; internal set; }
        public long RejectedAfterClose { get; internal set; }
        public long Failed { get; internal set; }
        public int WriteFailures { get; internal set; }
        public int Pending { get; internal set; }
        public int InFlight { get; internal set; }
        public int QueueCapacity { get; internal set; }
        public int BatchSize { get; internal set; }
        public int MaximumPending { get; internal set; }
        public int MaximumRecordCharacters { get; internal set; }
        public DateTime? CloseRequestedUtc { get; internal set; }
        public string ErrorCode { get; internal set; }
    }

    // Bounded, drop-newest queue. The producer never waits for the worker's lock
    // and never calls the I/O delegate. The worker never performs I/O under gate.
    // No UI object, mutable settings or screen contents are sent to this thread.
    internal sealed class BufferedDiagnosticWriter : IDisposable
    {
        internal const int RecordCharacterLimit = 4096;
        private readonly object gate = new object();
        private readonly Queue<string> pending = new Queue<string>();
        private readonly Action<string> append;
        private readonly Func<DiagnosticWriterSnapshot, string> finalRecord;
        private readonly int capacity, batchSize;
        private readonly Thread worker;
        private volatile bool accepting = true;
        private bool completed, timedOut, faulted, footerWritten;
        private int inFlight, maximumPending, writeFailures;
        private DateTime? closeRequestedUtc;
        private string errorCode;
        private long requested, accepted, written, dropped, overflowDropped;
        private long contentionDropped, oversizedDropped, rejectedAfterClose, failed;

        internal BufferedDiagnosticWriter(Action<string> write, int queueCapacity, int maximumBatch)
            : this(write, queueCapacity, maximumBatch, null, null) { }

        internal BufferedDiagnosticWriter(Action<string> write, int queueCapacity, int maximumBatch,
            Func<DiagnosticWriterSnapshot, string> closingRecord, string firstRecord)
        {
            if (write == null) throw new ArgumentNullException("write");
            if (queueCapacity < 1 || queueCapacity > 4096) throw new ArgumentOutOfRangeException("queueCapacity");
            if (maximumBatch < 1 || maximumBatch > queueCapacity) throw new ArgumentOutOfRangeException("maximumBatch");
            if (firstRecord != null && firstRecord.Length > RecordCharacterLimit)
                throw new ArgumentException("Initial diagnostic record exceeds the bound.", "firstRecord");
            append = write; capacity = queueCapacity; batchSize = maximumBatch; finalRecord = closingRecord;
            // Initial event is reserved before starting the worker; it cannot be
            // lost to a startup contention between the first event and the thread.
            if (firstRecord != null)
            {
                pending.Enqueue(firstRecord); requested = accepted = 1; maximumPending = 1;
            }
            worker = new Thread(Drain);
            worker.IsBackground = true;
            worker.Name = "Haloryva diagnostics";
            worker.Start();
        }

        internal bool Accepting { get { return accepting; } }

        internal static string FormatRecord(DateTime utc, string category, string detail)
        {
            return utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) + ";" +
                SingleField(category) + ";" + SingleField(detail) + "\r\n";
        }
        private static string SingleField(string value)
        { return (value ?? String.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace(';', ' '); }

        internal void RecordContentionDrop()
        {
            Interlocked.Increment(ref requested);
            Interlocked.Increment(ref dropped);
            Interlocked.Increment(ref contentionDropped);
        }

        internal bool TryEnqueue(string record)
        {
            if (record == null) throw new ArgumentNullException("record");
            Interlocked.Increment(ref requested);
            if (record.Length > RecordCharacterLimit)
            {
                Interlocked.Increment(ref dropped); Interlocked.Increment(ref oversizedDropped);
                return false;
            }
            if (!Monitor.TryEnter(gate))
            {
                Interlocked.Increment(ref dropped); Interlocked.Increment(ref contentionDropped);
                return false;
            }
            try
            {
                if (!accepting)
                {
                    Interlocked.Increment(ref dropped); Interlocked.Increment(ref rejectedAfterClose);
                    return false;
                }
                if (pending.Count >= capacity)
                {
                    Interlocked.Increment(ref dropped); Interlocked.Increment(ref overflowDropped);
                    return false;
                }
                pending.Enqueue(record); Interlocked.Increment(ref accepted);
                maximumPending = Math.Max(maximumPending, pending.Count);
                Monitor.Pulse(gate);
                return true;
            }
            finally { Monitor.Exit(gate); }
        }

        private void Drain()
        {
            try
            {
                while (true)
                {
                    string[] batch;
                    lock (gate)
                    {
                        while (pending.Count == 0 && accepting) Monitor.Wait(gate);
                        if (pending.Count == 0) break;
                        int count = Math.Min(batchSize, pending.Count);
                        batch = new string[count];
                        for (int i = 0; i < count; i++) batch[i] = pending.Dequeue();
                        inFlight = count;
                    }
                    append(String.Concat(batch));
                    lock (gate)
                    {
                        Interlocked.Add(ref written, batch.Length);
                        inFlight = 0;
                    }
                }
                if (finalRecord != null)
                {
                    string record = finalRecord(GetSnapshot());
                    if (record == null || record.Length > RecordCharacterLimit)
                        throw new InvalidOperationException("Invalid closing diagnostic record.");
                    append(record);
                    lock (gate) footerWritten = true;
                }
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    // A failed append can have partially written a batch. Never
                    // claim those events as confirmed and never retry/duplicate it.
                    Interlocked.Add(ref failed, inFlight + pending.Count);
                    pending.Clear(); inFlight = 0; accepting = false; faulted = true;
                    writeFailures++;
                    string type = ex.GetType().Name;
                    errorCode = type.Length <= 64 ? type : type.Substring(0, 64);
                }
            }
            finally
            {
                lock (gate)
                {
                    accepting = false; completed = true;
                    Monitor.PulseAll(gate);
                }
            }
        }

        private void RequestCompletion()
        {
            lock (gate)
            {
                if (!closeRequestedUtc.HasValue) closeRequestedUtc = DateTime.UtcNow;
                accepting = false;
                Monitor.PulseAll(gate);
            }
        }

        // Only explicit shutdown uses Join. A timeout never aborts the write,
        // disposes resources under it, or starts a competing synchronous writer.
        internal bool Complete(int timeoutMs)
        {
            if (timeoutMs < 0) throw new ArgumentOutOfRangeException("timeoutMs");
            RequestCompletion();
            if (Thread.CurrentThread == worker) return false;
            bool finished = worker.Join(timeoutMs);
            if (!finished) lock (gate) timedOut = true;
            return finished;
        }

        internal DiagnosticWriterSnapshot GetSnapshot()
        {
            lock (gate)
            {
                long lost = Interlocked.Read(ref dropped), failures = Interlocked.Read(ref failed);
                long requests = Interlocked.Read(ref requested), acceptedCount = Interlocked.Read(ref accepted);
                long writtenCount = Interlocked.Read(ref written);
                return new DiagnosticWriterSnapshot
                {
                    Started = true, Accepting = accepting, Completed = completed,
                    TimedOut = timedOut, Faulted = faulted, FooterExpected = finalRecord != null,
                    FooterWritten = footerWritten,
                    JournalComplete = completed && !timedOut && !faulted && lost == 0 && failures == 0 &&
                        pending.Count == 0 && inFlight == 0 && requests == acceptedCount && acceptedCount == writtenCount &&
                        (finalRecord == null || footerWritten),
                    Requested = requests, Accepted = acceptedCount, Written = writtenCount, Dropped = lost,
                    OverflowDropped = Interlocked.Read(ref overflowDropped),
                    ContentionDropped = Interlocked.Read(ref contentionDropped),
                    OversizedDropped = Interlocked.Read(ref oversizedDropped),
                    RejectedAfterClose = Interlocked.Read(ref rejectedAfterClose), Failed = failures,
                    WriteFailures = writeFailures, Pending = pending.Count, InFlight = inFlight,
                    QueueCapacity = capacity, BatchSize = batchSize, MaximumPending = maximumPending,
                    MaximumRecordCharacters = RecordCharacterLimit,
                    CloseRequestedUtc = closeRequestedUtc, ErrorCode = errorCode
                };
            }
        }

        public void Dispose() { RequestCompletion(); }
    }
}
