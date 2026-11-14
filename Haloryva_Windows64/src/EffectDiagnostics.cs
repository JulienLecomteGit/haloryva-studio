using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace CursorFlow
{
    // C1: diagnostic opt-in uniquement. Aucun repositionnement, aucune relance de moteur.
    // Pas de collecte de positions souris, de frappes, de titres ou de captures d'écran.
    internal static class EffectDiagnostics
    {
        private static readonly object sync = new object();
        private static volatile bool enabled;
        private static string logFile;
        private static long lastSample;
        private static long queued, dispatched, coalesced, renderTicks, published, publishFailures;
        private static int lastPublication = -1;
        private static long lastFailureEvent;
        private static readonly Dictionary<string, long> recentEvents = new Dictionary<string, long>();

        internal static bool Enabled { get { return enabled; } }

        internal static void Initialize(string[] args)
        {
            int index = Array.IndexOf(args, "--effect-diagnostics");
            if (index < 0) return;
            if (index + 1 >= args.Length || !Path.IsPathRooted(args[index + 1]))
                throw new ArgumentException("Un dossier de diagnostic absolu et neuf est requis.");
            string path = Path.GetFullPath(args[index + 1]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string root in ProfilePaths.ProtectedRoots)
                if (ProfilePaths.Overlaps(path, root)) throw new ArgumentException("Dossier de diagnostic adjacent aux profils : refus.");
            string local = PackageContext.RealLocalAppData;
            string haloryva = Path.Combine(local, "Haloryva");
            string legacyProfile = Path.Combine(local, "CursorFlow");
            if (path.Length < 10 || Directory.Exists(path) || File.Exists(path) ||
                Overlaps(path, haloryva) || Overlaps(path, legacyProfile))
                throw new ArgumentException("Dossier de diagnostic existant ou adjacent aux profils installés : opération refusée.");
            lock (sync)
            {
                if (enabled) throw new InvalidOperationException("Une session de diagnostic est déjà active.");
                Directory.CreateDirectory(path);
                logFile = Path.Combine(path, "EFFETS_DIAGNOSTIC.csv");
                File.WriteAllText(logFile, "utc;evenement;details\r\n", new UTF8Encoding(false));
                lastSample = 0;
                queued = dispatched = coalesced = renderTicks = published = publishFailures = 0;
                lastPublication = -1;
                lastFailureEvent = 0;
                recentEvents.Clear();
                enabled = true;
                WriteUnsafe("SESSION_START", "OBSERVATION_SEULE");
            }
        }

        private static bool Overlaps(string path, string protectedPath)
        {
            string sep = Path.DirectorySeparatorChar.ToString();
            return path.Equals(protectedPath, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(protectedPath + sep, StringComparison.OrdinalIgnoreCase) ||
                protectedPath.StartsWith(path + sep, StringComparison.OrdinalIgnoreCase);
        }

        private static void WriteUnsafe(string category, string detail)
        {
            try
            {
                File.AppendAllText(logFile,
                    DateTime.UtcNow.ToString("o") + ";" + category + ";" + detail + "\r\n",
                    new UTF8Encoding(false));
            }
            catch { enabled = false; } // Le diagnostic ne doit jamais perturber l'application.
        }

        internal static void Event(string category, string detail)
        {
            if (!enabled) return;
            lock (sync)
            {
                if (!enabled) return;
                long now = Stopwatch.GetTimestamp(), previous;
                if ((category.EndsWith("_OK", StringComparison.Ordinal) ||
                    category == "PUMP_TIMER_FALLBACK") &&
                    recentEvents.TryGetValue(category, out previous) &&
                    now - previous < Stopwatch.Frequency * 2) return;
                recentEvents[category] = now;
                WriteUnsafe(category, detail);
            }
        }

        internal static void FrameQueued() { if (enabled) Interlocked.Increment(ref queued); }
        internal static void FrameDispatched() { if (enabled) Interlocked.Increment(ref dispatched); }
        internal static void FrameCoalesced() { if (enabled) Interlocked.Increment(ref coalesced); }
        internal static void RenderTick() { if (enabled) Interlocked.Increment(ref renderTicks); }

        internal static void Publication(bool success, int errorCode)
        {
            if (!enabled) return;
            if (success) Interlocked.Increment(ref published);
            else Interlocked.Increment(ref publishFailures);
            int next = success ? 1 : 0;
            lock (sync)
            {
                long now = Stopwatch.GetTimestamp();
                if (enabled && (lastPublication != next || (!success &&
                    now - lastFailureEvent >= Stopwatch.Frequency * 2)))
                {
                    WriteUnsafe(success ? "PUBLISH_OK" : "PUBLISH_FAILED", "win32=" + errorCode);
                    if (!success) lastFailureEvent = now;
                }
                lastPublication = next;
            }
        }

        // Appelee depuis l'UI au plus une fois toutes les 2 secondes.
        internal static bool ShouldSample()
        {
            if (!enabled) return false;
            long now = Stopwatch.GetTimestamp();
            long old = Interlocked.Read(ref lastSample);
            if (old != 0 && now - old < Stopwatch.Frequency * 2) return false;
            return Interlocked.CompareExchange(ref lastSample, now, old) == old;
        }

        internal static void Sample(bool effectsOn, bool handleExists, bool visible,
            bool windowsVisible, bool topmostStyle, int windowsAbove, bool lastPublished)
        {
            if (!enabled) return;
            Event("SNAPSHOT", "enabled=" + Bit(effectsOn) + ",handle=" + Bit(handleExists) +
                ",visible=" + Bit(visible) + ",winVisible=" + Bit(windowsVisible) +
                ",topmost=" + Bit(topmostStyle) + ",above=" + windowsAbove +
                ",lastPublished=" + Bit(lastPublished) +
                ",queued=" + Interlocked.Read(ref queued) +
                ",dispatched=" + Interlocked.Read(ref dispatched) +
                ",coalesced=" + Interlocked.Read(ref coalesced) +
                ",ticks=" + Interlocked.Read(ref renderTicks) +
                ",published=" + Interlocked.Read(ref published) +
                ",failures=" + Interlocked.Read(ref publishFailures));
        }

        private static string Bit(bool value) { return value ? "1" : "0"; }

        internal static void Close()
        {
            if (!enabled) return;
            lock (sync)
            {
                if (!enabled) return;
                WriteUnsafe("SESSION_END", "OBSERVATION_SEULE");
                enabled = false;
                logFile = null;
            }
        }
    }
}
