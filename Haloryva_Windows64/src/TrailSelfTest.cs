using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace CursorFlow
{
    // Tests du code de production, sans hook ni UI. CLI : --trail-selftest.
    internal static class TrailSelfTest
    {
        private static int count;
        private static void Check(bool ok, string label)
        { if (!ok) throw new InvalidOperationException(label); count++; Console.WriteLine("OK " + count + " " + label); }
        internal static int Run()
        {
            count = 0;
            try
            {
                Sampling(); Bounds(); Compaction(); Scheduling(); Smoothing();
                Console.WriteLine("TOTAL " + count + " contrôles traînée C# réussis."); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("ECHEC TRAINEE : " + ex); return 1; }
        }
        private static void Sampling()
        {
            DateTime zero = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            TrailHistory h = new TrailHistory();
            for (int ms = 0; ms <= 300; ms++) h.Move(new Point(ms * 4, 100), zero.AddMilliseconds(ms), 6, 700, true);
            Check(h.Points[0].Time == zero, "pas de suppression prématurée du point à 300 ms / vie700");
            Check(h.Points.Count <= 152, "émissions bornées 500/s");
            h.Expire(zero.AddMilliseconds(700), 700);
            Check(h.Points.Count > 0 && h.Points[0].Time > zero, "expiration au terme de la vie seulement");
            h.Expire(zero.AddMilliseconds(1001), 700);
            Check(h.Points.Count == 0, "tous les points finissent par disparaître");
            foreach (int life in new int[] { 100, 269, 700, 2800 })
            foreach (int spacing in new int[] { 2, 6, 40 })
            {
                h = new TrailHistory();
                for (int ms = 0; ms <= 10000; ms++) h.Move(new Point(ms * 12, ms % 200), zero.AddMilliseconds(ms), spacing, life, true);
                Check(h.PeakCount <= (int)Math.Ceiling(life / TrailHistory.MinIntervalMs) + 3, "budget temps sans coupe vie=" + life + " esp=" + spacing);
                for (int i = 1; i < h.Points.Count; i++)
                    if (h.Points[i].Time < h.Points[i - 1].Time) throw new Exception("âge non monotone");
                Check((zero.AddMilliseconds(10000) - h.Points[0].Time).TotalMilliseconds < life, "aucun point expiré dans la liste");
            }
            h = new TrailHistory();
            h.Move(new Point(1, 1), zero, 6, 700, true);
            h.Move(new Point(200, 30), zero.AddMilliseconds(40), 6, 700, true);
            int before = h.Points.Count; long id = h.Points[before - 1].Id;
            Point p = h.Points[before - 1].Point;
            h.Expire(zero.AddMilliseconds(710), 700);
            Check(h.Points.Exists(d => d.Id == id && d.Point == p), "identité et position conservées à l'expiration d'un voisin");
            h.Move(new Point(-500, 200), zero.AddMilliseconds(740), 6, 700, true);
            Check(h.Points[h.Points.Count - 1].BreakBefore, "pas de raccord après pause d'entrée");
            h.Move(new Point(0, 0), zero.AddMilliseconds(750), 6, 700, false);
            h.Move(new Point(50, 50), zero.AddMilliseconds(760), 6, 700, true);
            Check(h.Points[h.Points.Count - 1].BreakBefore, "reprise après dessin/arrêt non reliée à l'ancien tracé");
            h.Clear(); Check(h.Points.Count == 0, "reset explicite");
            h.Move(new Point(10, 10), zero, 2, 700, true);
            for (int i = 0; i < 10000; i++) h.Move(new Point(i, 20), zero, 2, 700, true);
            Check(h.Points.Count == 1, "horodatage identique : pas de flot illimité");
        }
        private static void Smoothing()
        {
            Check(new Config().TrailLife == 269, "persistance usine Haloryva C2 = 269 ms");
            Check(TrailPath.SmoothingTension(0) == 0f, "souplesse 0 % sans courbure");
            Check(TrailPath.SmoothingTension(-20) == 0f &&
                TrailPath.SmoothingTension(120) == TrailPath.SmoothingTension(100), "souplesse bornée 0–100 %");
            float previous = 0f;
            foreach (int value in new int[] { 25, 50, 75, 100 })
            {
                float tension = TrailPath.SmoothingTension(value);
                Check(tension > previous && tension <= .31f + .00001f, "souplesse progressive à " + value + " %");
                previous = tension;
            }
            Check(TrailPath.SmoothingTension(50) > .18f, "50 % plus perceptible que 100 % en C1");
            PointF[] input = { new PointF(0, 0), new PointF(40, 20), new PointF(80, 60), new PointF(120, 20), new PointF(125, 22) };
            foreach (int value in new int[] { 0, 25, 50, 75, 100 })
            {
                float tension = TrailPath.SmoothingTension(value);
                for (int i = 1; i < input.Length; i++)
                {
                    PointF a = input[i - 1], b = input[i];
                    PointF before = i < 2 ? a : input[i - 2];
                    PointF after = i + 1 == input.Length ? b : input[i + 1];
                    PointF c1, c2;
                    TrailPath.CurveControls(before, a, b, after, tension, out c1, out c2);
                    float lowX = Math.Min(a.X, b.X), highX = Math.Max(a.X, b.X);
                    float lowY = Math.Min(a.Y, b.Y), highY = Math.Max(a.Y, b.Y);
                    if (c1.X < lowX || c1.X > highX || c2.X < lowX || c2.X > highX ||
                        c1.Y < lowY || c1.Y > highY || c2.Y < lowY || c2.Y > highY)
                        throw new Exception("contrôle Bézier débordant : " + value + " %, segment " + i);
                    if (value == 0 && (c1.X != a.X || c1.Y != a.Y || c2.X != b.X || c2.Y != b.Y))
                        throw new Exception("tracé 0 % non rectiligne");
                }
                Check(true, "contrôles Bézier confinés, y compris virage serré à " + value + " %");
            }
        }
        private static void Bounds()
        {
            Rectangle screen = new Rectangle(0, 0, 1920, 1080);
            var s = new StableSurfaceBounds();
            Rectangle before = s.Calculate(screen, Rectangle.FromLTRB(127, 395, 1275, 685), true, 0);
            Rectangle after = s.Calculate(screen, Rectangle.FromLTRB(127, 395, 1281, 685), true, 17);
            Check(after.Contains(Rectangle.FromLTRB(127, 395, 1281, 685)), "scénario R8 : traînée entière conservée");
            Check(Math.Abs(after.Left - before.Left) <= 6, "aucun saut d'ancre de389px au seuil1152");
            s = new StableSurfaceBounds(); Size previous = Size.Empty; int changes = 0;
            for (int i = 0; i < 100; i++)
            {
                Rectangle r = s.Calculate(screen, new Rectangle(100, 100, i % 2 == 0 ? 510 : 514, 290), true, i * 17);
                if (r.Size != previous) changes++; previous = r.Size;
            }
            Check(changes == 2, "oscillation510/514 : deux tailles seulement");
            Rectangle big = s.Calculate(screen, new Rectangle(10, 10, 1800, 1000), true, 2000);
            Rectangle hold = s.Calculate(screen, new Rectangle(700, 400, 290, 290), false, 2001);
            Rectangle small = s.Calculate(screen, new Rectangle(700, 400, 290, 290), false, 2800);
            Check(hold.Size == big.Size && small.Width < hold.Width, "contraction après délai de repos");
            s = new StableSurfaceBounds();
            s.Calculate(screen, new Rectangle(10, 10, 1800, 1000), true, 0);
            s.Calculate(screen, new Rectangle(700, 400, 290, 290), false, 20);
            Rectangle resume = s.Calculate(screen, new Rectangle(700, 400, 300, 290), true, 1000);
            Check(resume.Width == 384, "contraction sur reprise après repos sans image intermédiaire");
            Random random = new Random(57);
            foreach (Rectangle desktop in new Rectangle[] { screen, new Rectangle(-3840, -600, 7680, 2760), new Rectangle(0, 0, 1366, 728) })
            {
                s = new StableSurfaceBounds();
                for (int i = 0; i < 1000; i++)
                {
                    Rectangle wanted = new Rectangle(desktop.Left - 100 + random.Next(desktop.Width), desktop.Top - 100 + random.Next(desktop.Height), 50 + random.Next(3000), 50 + random.Next(2000));
                    Rectangle clipped = Rectangle.Intersect(desktop, wanted);
                    Rectangle result = s.Calculate(desktop, wanted, i % 8 != 0, i * 17);
                    if (clipped.Width > 0 && clipped.Height > 0 && (!result.Contains(clipped) || !desktop.Contains(result))) throw new Exception("géométrie hors invariant " + i);
                }
                Check(true, "1000 rectangles complets : " + desktop);
            }
        }
        private static void Compaction()
        {
            DateTime t = new DateTime(2026, 1, 1); var list = new List<TrailDot>(); var output = new List<TrailDot>();
            for (int i = 0; i <= 350; i++) list.Add(new TrailDot(new Point(i * 8, 0), t.AddMilliseconds(i * 2), 4));
            TrailPath.Compact(list, output);
            Check(output.Count <= 46 && output[0] == list[0] && output[output.Count - 1] == list[list.Count - 1], "ligne351points : extrémités et <=46 sommets");
            Check(list.Count == 351, "compactage ne détruit aucun point source");
            list[150].BreakBefore = true; TrailPath.Compact(list, output);
            Check(output.Contains(list[149]) && output.Contains(list[150]), "rupture préservée");
            list = new List<TrailDot>();
            for (int i = 0; i < 100; i++) list.Add(new TrailDot(new Point(i, i < 50 ? 0 : (i - 49) * 10), t.AddMilliseconds(i * 2), 1));
            TrailPath.Compact(list, output);
            Check(output.Contains(list[49]), "angle net préservé");
        }
        private static void Scheduling()
        {
            FrameGate gate = new FrameGate();
            Check(gate.TryEnter() && !gate.TryEnter(), "un seul callback en attente");
            gate.Leave(); Check(gate.TryEnter(), "libération après rendu"); gate.Leave();
            int winners = 0; var threads = new List<Thread>();
            for (int i = 0; i < 20; i++) { Thread x = new Thread(delegate() { if (gate.TryEnter()) Interlocked.Increment(ref winners); }); threads.Add(x); x.Start(); }
            foreach (Thread x in threads) x.Join();
            Check(winners == 1, "20 concurrents : une réservation"); gate.Leave();
            FrameDeadline d = new FrameDeadline();
            Check(Math.Abs(d.Delay(0, 60) - 1000.0 / 60) < .001, "échéance60 fractionnaire sans arrondi17ms");
            d.Advance(200, 60);
            Check(d.Delay(200, 60) > 16, "retard200ms : aucun rattrapage en rafale");
        }
    }
}
