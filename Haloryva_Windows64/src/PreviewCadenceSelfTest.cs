using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace CursorFlow
{
    internal static class PreviewCadenceSelfTest
    {
        private static int checks;
        private static readonly DateTime origin = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks++; Console.WriteLine("OK CADENCE " + checks + " " + name);
        }
        private static bool Near(PointF a, PointF b)
        { return Math.Abs(a.X - b.X) < .0002f && Math.Abs(a.Y - b.Y) < .0002f; }
        private static void Feed(TrailPipeline pipeline, TrailPreviewSampler sampler)
        {
            if (sampler.Discontinuous) pipeline.ResetInput();
            foreach (TrailPreviewSample sample in sampler.Samples)
            {
                pipeline.Move(sample.Point, sample.Time, 6, 269, true);
                if (sample.Corner && !pipeline.PreserveInputCorner()) throw new InvalidOperationException("preview corner budget");
            }
        }
        private static void PreviewSamples()
        {
            TrailPreviewSampler sampler = new TrailPreviewSampler();
            sampler.Generate(TrailPreviewMotion.Fast, .6, .696, origin, origin.AddMilliseconds(96), 800, 300, 62, 6);
            Check(!sampler.Discontinuous && sampler.Samples.Count > 20, "automatic path contains intermediate positions, not only one endpoint per paint");
            Check(Near(sampler.Samples[0].Point, TrailPreviewSampler.PointAt(TrailPreviewMotion.Fast, .6, 800, 300, 62, 6)), "first analytical point retained");
            TrailPreviewSample last = sampler.Samples[sampler.Samples.Count - 1];
            Check(last.Time == origin.AddMilliseconds(96) && Near(last.Point, TrailPreviewSampler.PointAt(TrailPreviewMotion.Fast, .696, 800, 300, 62, 6)), "last point and date are exact");
            bool increasing = true, fractional = false;
            for (int i = 0; i < sampler.Samples.Count; i++)
            {
                TrailPreviewSample sample = sampler.Samples[i];
                if (i > 0 && sample.Time <= sampler.Samples[i - 1].Time) increasing = false;
                if (Math.Abs(sample.Point.X - Math.Round(sample.Point.X)) > .001) fractional = true;
            }
            Check(increasing, "intermediate timestamps strictly increase");
            Check(fractional, "analytical positions retain fractional pixels");
            PointF first = sampler.Samples[0].Point;
            PointF middle = sampler.Samples[sampler.Samples.Count / 2].Point;
            PointF chord = new PointF((first.X + last.Point.X) / 2, (first.Y + last.Point.Y) / 2);
            Check(Math.Abs(middle.Y - chord.Y) > .2 || Math.Abs(middle.X - chord.X) > .2, "curved path is sampled from its formula rather than a straight chord");

            for (int corner = 1; corner <= 4; corner++)
            {
                double at = corner / .95;
                sampler.Generate(TrailPreviewMotion.Turns, at - .007, at + .011, origin, origin.AddMilliseconds(18), 800, 300, 62, 6);
                int found = 0; TrailPreviewSample marked = new TrailPreviewSample();
                foreach (TrailPreviewSample sample in sampler.Samples) if (sample.Corner) { found++; marked = sample; }
                Check(found == 1 && marked.Time == origin.AddMilliseconds(7) &&
                    Near(marked.Point, TrailPreviewSampler.PointAt(TrailPreviewMotion.Turns, at, 800, 300, 62, 6)), "turn " + corner + " keeps its exact corner between two paints");
            }
            double boundary = 1 / .95;
            sampler.Generate(TrailPreviewMotion.Turns, boundary - .008, boundary, origin, origin.AddMilliseconds(8), 800, 300, 62, 6);
            Check(sampler.Samples[sampler.Samples.Count - 1].Corner, "corner exactly at an image boundary is retained");
            sampler.Generate(TrailPreviewMotion.Fast, 0, .1, origin, origin.AddMilliseconds(100), 800, 300, 62, 6);
            Check(!sampler.Discontinuous && sampler.Samples.Count <= TrailPreviewSampler.MaximumSamples, "bounded catch-up at 100 ms");
            sampler.Generate(TrailPreviewMotion.Fast, 0, .101, origin, origin.AddMilliseconds(101), 800, 300, 62, 6);
            Check(sampler.Discontinuous && sampler.Samples.Count == 1 && sampler.Samples[0].Time == origin.AddMilliseconds(101), "late callback does not invent a long bridge or unbounded catch-up");
            sampler.Generate(TrailPreviewMotion.Fast, .1, .1, origin, origin, 800, 300, 62, 6);
            Check(!sampler.Discontinuous && sampler.Samples.Count == 1, "zero elapsed time does not fabricate movement");
            sampler.Generate(TrailPreviewMotion.Fast, .1, .11, origin, origin.AddTicks(-1), 800, 300, 62, 6);
            Check(sampler.Discontinuous && sampler.Samples.Count == 1, "clock regression starts a separate portion");
            sampler.Generate(TrailPreviewMotion.Mouse, 0, .016, origin, origin.AddMilliseconds(16), 800, 300, 62, 6);
            Check(sampler.Discontinuous && sampler.Samples.Count == 0, "mouse mode is never reconstructed as an automatic path");
        }
        private static void PreviewIntegration()
        {
            TrailPreviewSampler sampler = new TrailPreviewSampler();
            TrailPipeline pipeline = new TrailPipeline();
            double corner = 1 / .95;
            sampler.Generate(TrailPreviewMotion.Turns, corner - .007, corner + .011, origin, origin.AddMilliseconds(18), 800, 300, 62, 6);
            Feed(pipeline, sampler);
            PointF endpoint = sampler.Samples[sampler.Samples.Count - 1].Point;
            pipeline.Prepare(endpoint, origin.AddMilliseconds(18), 269, true);
            bool found = false;
            foreach (TrailDot point in pipeline.RenderPoints)
                if (point.Time == origin.AddMilliseconds(7) && Near(point.ExactPoint,
                    TrailPreviewSampler.PointAt(TrailPreviewMotion.Turns, corner, 800, 300, 62, 6))) found = true;
            Check(found, "analytical corner survives the production history and compaction");
            int oldCount = pipeline.History.Points.Count;
            pipeline.ResetInput();
            sampler.Generate(TrailPreviewMotion.Turns, corner + .011, corner + .027,
                origin.AddMilliseconds(60), origin.AddMilliseconds(76), 800, 300, 62, 6);
            Feed(pipeline, sampler);
            Check(pipeline.History.Points.Count > oldCount && pipeline.History.Points[oldCount].BreakBefore,
                "explicit pause and resume do not join the previous portion");
            oldCount = pipeline.History.Points.Count;
            sampler.Generate(TrailPreviewMotion.Turns, corner + .027, corner + .138,
                origin.AddMilliseconds(76), origin.AddMilliseconds(187), 800, 300, 62, 6);
            Feed(pipeline, sampler);
            Check(pipeline.History.Points.Count == oldCount + 1 && pipeline.History.Points[oldCount].BreakBefore,
                "preview delay beyond its budget becomes a real break in the production pipeline");
            pipeline.Expire(origin.AddMilliseconds(500), 269);
            Check(!pipeline.HasLiveContent(origin.AddMilliseconds(500), 269), "paused and resumed preview content expires normally");
        }
        private static void Timing()
        {
            Check(Math.Abs(FrameTiming.Milliseconds(1000, 2667, 100000) - 16.67) < .000001,
                "fractional frame duration is not rounded to an integer millisecond");
            Check(FrameTiming.Milliseconds(0, 10, 1000) < 0 && FrameTiming.Milliseconds(20, 10, 1000) < 0,
                "unknown or regressing performance timestamps remain unknown");
            Check(FrameTiming.InputAge(origin, origin.AddTicks(12500)) == 1.25,
                "input age retains sub-millisecond precision");
            Check(FrameTiming.InputAge(DateTime.MinValue, origin) < 0 && FrameTiming.InputAge(origin, origin.AddTicks(-1)) < 0,
                "unknown input age is not reported as zero latency");
        }
        private static void Writer()
        {
            using (ManualResetEvent entered = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            {
                List<string> writes = new List<string>(); int active = 0, peak = 0;
                SerializedSettingsWriter writer = new SerializedSettingsWriter(delegate(string json, out string error)
                {
                    int current = Interlocked.Increment(ref active); peak = Math.Max(peak, current);
                    try
                    {
                        if (json == "first")
                        { entered.Set(); if (!release.WaitOne(2000)) throw new TimeoutException("fake writer release"); }
                        lock (writes) writes.Add(json);
                        error = null; return true;
                    }
                    finally { Interlocked.Decrement(ref active); }
                });
                try
                {
                    Check(writer.Enqueue("first") && entered.WaitOne(2000), "save runs on the worker while caller can continue");
                    Check(writer.Enqueue("obsolete") && writer.Enqueue("latest"), "pending changes are accepted while a write is in progress");
                    release.Set();
                    Check(writer.TryPauseAndDrain(2000), "explicit operation can wait for completed writes with a bounded deadline");
                    lock (writes) Check(writes.Count == 2 && writes[0] == "first" && writes[1] == "latest", "only the latest pending immutable snapshot is written");
                    Check(peak == 1 && writer.Requested == 3 && writer.Completed == 2 && writer.Coalesced == 1 && writer.Failed == 0,
                        "single writer and coalescing counters are consistent");
                    Check(!writer.Enqueue("during import"), "paused writer cannot race an explicit import or final save");
                    writer.Resume();
                    Check(writer.Enqueue("after import") && writer.TryPauseAndDrain(2000), "writer resumes after an explicit operation");
                }
                finally { release.Set(); writer.TryPauseAndDrain(2000); writer.Dispose(); }
            }
            using (ManualResetEvent entered = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            {
                SerializedSettingsWriter writer = new SerializedSettingsWriter(delegate(string json, out string error)
                {
                    entered.Set(); if (!release.WaitOne(2000)) throw new TimeoutException("fake delayed writer");
                    error = null; return true;
                });
                try
                {
                    writer.Enqueue("blocked");
                    Check(entered.WaitOne(2000) && !writer.TryPauseAndDrain(0), "timeout cancels an import or closure instead of aborting the write");
                    Check(writer.Enqueue("newest after timeout"), "timeout releases the pause so later edits remain savable");
                    release.Set();
                    Check(writer.TryPauseAndDrain(2000) && writer.Completed == 2, "existing write and newest edit finish after timeout");
                }
                finally { release.Set(); writer.TryPauseAndDrain(2000); writer.Dispose(); }
            }
            using (SerializedSettingsWriter writer = new SerializedSettingsWriter(delegate(string json, out string error)
                { error = "fake failure"; return false; }))
            {
                writer.Enqueue("bad");
                Check(writer.TryPauseAndDrain(2000) && writer.Failed == 1, "failed background write remains an explicit failure");
                Check(writer.TakeError() == "fake failure" && writer.TakeError() == null, "failure is handed to the UI once, without worker UI calls");
            }
            using (SerializedSettingsWriter writer = new SerializedSettingsWriter(delegate(string json, out string error)
                { throw new InvalidOperationException("fake exception"); }))
            {
                writer.Enqueue("throws");
                Check(writer.TryPauseAndDrain(2000) && writer.Failed == 1 && writer.TakeError() == "fake exception",
                    "writer exception is contained and leaves the drain signalled");
            }
        }
        internal static int Run()
        {
            // Reuse the caller's writers; this suite also runs inside the redirected WinExe.
            checks = 0;
            try
            {
                PreviewSamples(); PreviewIntegration(); Timing(); Writer();
                Console.WriteLine("TOTAL CADENCE " + checks + " controles apercu, mesures et sauvegardes ; echecs=0.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("ECHEC CADENCE : " + ex); return 1; }
        }
    }
}
