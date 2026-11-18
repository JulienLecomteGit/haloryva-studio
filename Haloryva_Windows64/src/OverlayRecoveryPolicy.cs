using System;
using System.Collections.Generic;

namespace CursorFlow
{
    // Pure decision logic: no HWND operation, input hook, settings write or UI.
    internal enum RecoveryState { Unknown, Deferred, Healthy, Obscured }
    internal enum RecoveryReason
    {
        None, LayerUnavailable, DesktopInactive, ForegroundUnavailable,
        OwnInterface, InteractionActive, FullScreen, CaptureActive,
        PriorityWindow, EnumerationChanged, ProbeBudget, NativeError,
        Ineligible, ClockReset
    }

    internal struct RecoverySample
    {
        internal readonly RecoveryState State;
        internal readonly long Layer, Blocker;
        internal readonly RecoveryReason Reason;
        internal RecoverySample(RecoveryState state, long layer, long blocker, RecoveryReason reason)
        { State = state; Layer = layer; Blocker = blocker; Reason = reason; }
        internal bool SameObstruction(RecoverySample other)
        {
            return State == RecoveryState.Obscured && other.State == RecoveryState.Obscured &&
                Layer != 0 && Blocker != 0 && Layer == other.Layer && Blocker == other.Blocker;
        }
    }

    internal struct RecoveryRepairResult
    {
        internal readonly bool Success;
        internal readonly int Error;
        internal readonly RecoveryReason Reason;
        internal RecoveryRepairResult(bool success, int error)
        { Success = success; Error = error; Reason = RecoveryReason.None; }
        internal RecoveryRepairResult(RecoveryReason reason)
        { Success = false; Error = 0; Reason = reason; }
    }

    internal sealed class RecoveryEvent
    {
        internal readonly string Name;
        internal readonly RecoveryReason Reason;
        internal readonly int Error, Attempts;
        internal RecoveryEvent(string name, RecoveryReason reason, int error, int attempts)
        { Name = name; Reason = reason; Error = error; Attempts = attempts; }
    }

    internal interface IOverlayRecoveryPlatform
    {
        RecoverySample Inspect();
        RecoveryRepairResult Repair(RecoverySample expected);
    }

    internal sealed class OverlayRecoveryPolicy
    {
        internal const long ProbeIntervalMs = 500;
        internal const long ConfirmationWindowMs = 1500;
        internal const long AttemptIntervalMs = 5000;
        internal const long AttemptWindowMs = 60000;
        internal const int MaxAttempts = 3;

        private readonly IOverlayRecoveryPlatform platform;
        private readonly Action<RecoveryEvent> log;
        private readonly Queue<long> attempts = new Queue<long>();
        private long lastNow = -1, nextProbe, nextAttempt, candidateAt, verificationLayer;
        private bool hasCandidate, verifying, throttleReported;
        private RecoverySample candidate;
        private RecoveryReason previousDeferred = RecoveryReason.None;

        internal OverlayRecoveryPolicy(IOverlayRecoveryPlatform platform, Action<RecoveryEvent> log)
        {
            if (platform == null) throw new ArgumentNullException("platform");
            this.platform = platform; this.log = log;
        }

        private void Record(string name, RecoveryReason reason, int error)
        {
            // A diagnostic sink must never break the render loop.
            try { if (log != null) log(new RecoveryEvent(name, reason, error, attempts.Count)); }
            catch { }
        }

        private void ClearCandidate() { hasCandidate = false; }

        internal void Tick(long nowMilliseconds, bool eligible)
        {
            long now = nowMilliseconds;
            if (now < 0 || (lastNow >= 0 && now < lastNow))
            {
                // Stopwatch is monotone in production. Fail closed for an invalid clock.
                ClearCandidate(); verifying = false;
                lastNow = Math.Max(0, now);
                nextProbe = lastNow + ProbeIntervalMs;
                nextAttempt = lastNow + AttemptWindowMs;
                attempts.Clear();
                Record("CLOCK_DEFERRED", RecoveryReason.ClockReset, 0);
                return;
            }
            lastNow = now;
            if (!eligible)
            {
                ClearCandidate(); verifying = false;
                previousDeferred = RecoveryReason.Ineligible;
                // Keep the rolling quota and backoff across disable/enable and Studio visits.
                return;
            }
            if (now < nextProbe) return;
            nextProbe = now + ProbeIntervalMs;
            if (!verifying && now < nextAttempt) return;
            while (attempts.Count > 0 && now - attempts.Peek() >= AttemptWindowMs)
                attempts.Dequeue();

            RecoverySample sample;
            try { sample = platform.Inspect(); }
            catch
            {
                ClearCandidate(); verifying = false; nextAttempt = now + AttemptIntervalMs;
                Record("NATIVE_ERROR", RecoveryReason.NativeError, 0); return;
            }

            if (verifying)
            {
                if (sample.State == RecoveryState.Healthy && sample.Layer == verificationLayer)
                    Record("ORDER_VERIFIED_CLEAR", RecoveryReason.None, 0);
                else if (sample.State == RecoveryState.Obscured && sample.Layer == verificationLayer)
                    Record("ORDER_STILL_OBSCURED", sample.Reason, 0);
                else Record("ORDER_VERIFICATION_DEFERRED", sample.Reason, 0);
                verifying = false;
            }

            if (sample.State != RecoveryState.Obscured || sample.Layer == 0 || sample.Blocker == 0)
            {
                ClearCandidate();
                if ((sample.State == RecoveryState.Deferred || sample.State == RecoveryState.Unknown) &&
                    previousDeferred != sample.Reason)
                    Record("RECOVERY_DEFERRED", sample.Reason, 0);
                previousDeferred = sample.State == RecoveryState.Healthy ? RecoveryReason.None : sample.Reason;
                return;
            }
            previousDeferred = RecoveryReason.None;
            if (now < nextAttempt) { ClearCandidate(); return; }
            if (attempts.Count >= MaxAttempts)
            {
                ClearCandidate(); nextAttempt = attempts.Peek() + AttemptWindowMs;
                if (!throttleReported) Record("RECOVERY_RATE_LIMIT", RecoveryReason.None, 0);
                throttleReported = true; return;
            }
            throttleReported = false;
            if (!hasCandidate || !sample.SameObstruction(candidate) || now - candidateAt > ConfirmationWindowMs)
            {
                candidate = sample; candidateAt = now; hasCandidate = true; return;
            }
            if (now - candidateAt < ProbeIntervalMs) return;

            // Check again after confirmation, immediately before the only possible mutation.
            RecoverySample fresh;
            try { fresh = platform.Inspect(); }
            catch
            {
                ClearCandidate(); nextAttempt = now + AttemptIntervalMs;
                Record("NATIVE_ERROR", RecoveryReason.NativeError, 0); return;
            }
            ClearCandidate();
            if (!fresh.SameObstruction(sample))
            {
                Record("RECOVERY_RECHECK_CANCELLED", fresh.Reason, 0); return;
            }

            // Reserve an attempt BEFORE the native call, including failures/exceptions.
            attempts.Enqueue(now); nextAttempt = now + AttemptIntervalMs;
            bool cancelled = false;
            try
            {
                RecoveryRepairResult result = platform.Repair(fresh);
                // No log I/O between the last inspection and the native mutation.
                Record("OBSTRUCTION_CONFIRMED", RecoveryReason.None, 0);
                cancelled = result.Reason != RecoveryReason.None;
                Record(cancelled ? "REPAIR_CANCELLED" : (result.Success ? "REPAIR_ACCEPTED" : "REPAIR_FAILED"),
                    result.Reason, result.Error);
            }
            catch { Record("NATIVE_ERROR", RecoveryReason.NativeError, 0); }
            verifying = !cancelled; verificationLayer = fresh.Layer;
        }
    }
}
