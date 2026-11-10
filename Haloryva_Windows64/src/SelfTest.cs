using System;
using System.Collections.Generic;
using System.Drawing;

namespace CursorFlow
{
    // Auto-test intégré : « Haloryva.exe --selftest ». Aucun exécutable de test
    // séparé (bloqué par certaines stratégies Windows) ; même code que l'application.
    // Tests V2 : gestes « à la main » simulés (tremblement, contour non fermé,
    // dépassement, coins arrondis, inclinaison) sur 40 tirages par forme.
    internal static class GeometrySmoke
    {
        private static int passed = 0;
        private static Random rnd = new Random(42);

        private static string Recognize(List<Point> pts)
        {
            AnnotationStroke s = new AnnotationStroke(); s.Tool = "Stylo"; s.Points.AddRange(pts);
            return DrawingGeometry.TryAssist(s) ? s.Tool : "Libre";
        }
        // Suit un contour paramétrique avec tremblement, écart de fermeture et dépassement.
        private static List<Point> Hand(Func<double, PointF> shape, double jitter, double startT, double endT, double angle, PointF center)
        {
            List<Point> p = new List<Point>();
            double wobble = rnd.NextDouble() * 6;
            int n = 140;
            for (int i = 0; i <= n; i++)
            {
                double t = startT + (endT - startT) * i / n;
                PointF q = shape(t);
                double nx = q.X + Math.Sin(t * 9 + wobble) * jitter + (rnd.NextDouble() - .5) * jitter;
                double ny = q.Y + Math.Cos(t * 7 + wobble) * jitter + (rnd.NextDouble() - .5) * jitter;
                double c = Math.Cos(angle), s = Math.Sin(angle);
                Point r = Point.Round(new PointF((float)(center.X + nx * c - ny * s), (float)(center.Y + nx * s + ny * c)));
                if (p.Count == 0 || Math.Abs(r.X - p[p.Count - 1].X) + Math.Abs(r.Y - p[p.Count - 1].Y) >= 3) p.Add(r);
            }
            return p;
        }
        private static PointF OnPolygon(PointF[] v, double t, double round)
        {
            t = ((t % 1) + 1) % 1;
            double per = 0; double[] len = new double[v.Length];
            for (int i = 0; i < v.Length; i++) { PointF a = v[i], b = v[(i + 1) % v.Length]; len[i] = Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2)); per += len[i]; }
            double d = t * per;
            for (int i = 0; i < v.Length; i++)
            {
                if (d <= len[i])
                {
                    PointF a = v[i], b = v[(i + 1) % v.Length]; double k = d / len[i];
                    PointF q = new PointF((float)(a.X + (b.X - a.X) * k), (float)(a.Y + (b.Y - a.Y) * k));
                    // coins arrondis : on tire le point vers le centre près des sommets
                    double nearCorner = Math.Min(k, 1 - k) * len[i];
                    if (round > 0 && nearCorner < round) { double f = (round - nearCorner) / round * .25; q = new PointF((float)(q.X * (1 - f)), (float)(q.Y * (1 - f))); }
                    return q;
                }
                d -= len[i];
            }
            return v[0];
        }
        private static void Rate(string name, string expected, Func<List<Point>> make, int minimum)
        {
            int ok = 0; string lastWrong = "";
            for (int i = 0; i < 40; i++) { string r = Recognize(make()); if (r == expected) ok++; else lastWrong = r; }
            Console.WriteLine((ok >= minimum ? "OK : " : "KO : ") + name + " -> " + ok + "/40" + (ok < 40 ? " (sinon : " + lastWrong + ")" : ""));
            if (ok < minimum) throw new Exception(name + " : taux insuffisant " + ok + "/40");
            passed++;
        }
        internal static int Run()
        {
            // Sortie UTF-8 explicite : accents lisibles dans PowerShell (même sans console).
            try
            {
                System.IO.StreamWriter w = new System.IO.StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false));
                w.AutoFlush = true; Console.SetOut(w); Console.SetError(w);
            }
            catch { }
            try
            {
                PointF c = new PointF(400, 300);
                Rate("cercle main levée (écart / dépassement)", "Ellipse", delegate
                {
                    double r = 50 + rnd.NextDouble() * 90, gap = (rnd.NextDouble() - .35) * .18;
                    return Hand(t => new PointF((float)(r * Math.Cos(t * Math.PI * 2)), (float)(r * Math.Sin(t * Math.PI * 2))), r * .05, 0, 1 + gap, 0, c);
                }, 36);
                Rate("cercle très irrégulier (jamais carré)", "Ellipse", delegate
                {
                    double r = 60 + rnd.NextDouble() * 80, bump = rnd.NextDouble() * 6;
                    return Hand(t => new PointF((float)((r + Math.Sin(t * 12.6 + bump) * r * .06) * Math.Cos(t * Math.PI * 2)),
                        (float)((r * (.85 + rnd.NextDouble() * .02) + Math.Cos(t * 18.8) * r * .05) * Math.Sin(t * Math.PI * 2))), r * .07, rnd.NextDouble() * .1, 1.06, 0, c);
                }, 34);
                Rate("ellipse horizontale", "Ellipse", delegate
                {
                    double a = 120 + rnd.NextDouble() * 60, b = 50 + rnd.NextDouble() * 30;
                    return Hand(t => new PointF((float)(a * Math.Cos(t * Math.PI * 2)), (float)(b * Math.Sin(t * Math.PI * 2))), 3, .1, 1.05, 0, c);
                }, 36);
                Rate("rectangle main levée", "Rectangle", delegate
                {
                    double w = 80 + rnd.NextDouble() * 140, h = 60 + rnd.NextDouble() * 90;
                    PointF[] v = { new PointF((float)-w, (float)-h), new PointF((float)w, (float)-h), new PointF((float)w, (float)h), new PointF((float)-w, (float)h) };
                    return Hand(t => OnPolygon(v, t, 8), 3, 0, 1 + (rnd.NextDouble() - .3) * .08, (rnd.NextDouble() - .5) * .1, c);
                }, 36);
                Rate("carré aux coins arrondis", "Rectangle", delegate
                {
                    double w = 70 + rnd.NextDouble() * 60;
                    PointF[] v = { new PointF((float)-w, (float)-w), new PointF((float)w, (float)-w), new PointF((float)w, (float)w), new PointF((float)-w, (float)w) };
                    return Hand(t => OnPolygon(v, t, 22), 3, .05, 1.08, 0, c);
                }, 34);
                Rate("triangle main levée", "Triangle", delegate
                {
                    double s = 70 + rnd.NextDouble() * 80;
                    PointF[] v = { new PointF(0, (float)-s), new PointF((float)(s * .95), (float)(s * .7)), new PointF((float)(-s * 1.05), (float)(s * .65)) };
                    return Hand(t => OnPolygon(v, t, 10), 3, 0, 1 + (rnd.NextDouble() - .3) * .1, (rnd.NextDouble() - .5) * 1.2, c);
                }, 36);
                Rate("rectangle incliné 30°", "Polygone", delegate
                {
                    PointF[] v = { new PointF(-130, -60), new PointF(130, -60), new PointF(130, 60), new PointF(-130, 60) };
                    return Hand(t => OnPolygon(v, t, 6), 2.5, 0, 1.02, Math.PI / 6, c);
                }, 34);
                Rate("gribouillage préservé", "Libre", delegate
                {
                    List<Point> p = new List<Point>();
                    for (int i = 0; i <= 60; i++) p.Add(new Point(300 + rnd.Next(0, 160), 200 + rnd.Next(0, 120)));
                    return p;
                }, 38);
                Rate("courbe ouverte (U) préservée", "Libre", delegate
                {
                    return Hand(t => new PointF((float)(120 * Math.Cos(Math.PI * t)), (float)(120 * Math.Sin(Math.PI * t))), 2, 0, 1, 0, c);
                }, 38);
                Rate("ligne tremblante", "Ligne droite", delegate
                {
                    List<Point> p = new List<Point>();
                    for (int i = 0; i <= 40; i++) p.Add(new Point(20 + i * 6, 40 + rnd.Next(-3, 4)));
                    return p;
                }, 38);

                AnnotationStroke arrow = new AnnotationStroke(); arrow.Tool = "Flèche libre";
                for (int i = 0; i <= 35; i++) arrow.Points.Add(new Point(i * 5, 55 + (int)Math.Round(45 * Math.Sin(i / 35.0 * Math.PI))));
                if (DrawingGeometry.TryAssist(arrow)) throw new Exception("La flèche libre a été transformée.");
                passed++; Console.WriteLine("OK : flèche libre préservée");

                // Lissage : extrémités conservées ET tremblement nettement réduit.
                List<Point> line = new List<Point>();
                for (int i = 0; i <= 60; i++) line.Add(new Point(20 + i * 5, 100 + ((i % 2 == 0) ? 6 : -6)));
                foreach (int strength in new int[] { 35, 70, 100 })
                {
                    List<Point> sm = DrawingGeometry.Smooth(line, strength);
                    if (sm[0] != line[0] || sm[sm.Count - 1] != line[line.Count - 1]) throw new Exception("Le lissage a changé les extrémités.");
                    double before = 0, after = 0; int n = 0;
                    foreach (Point p in line) before += Math.Abs(p.Y - 100);
                    for (int i = sm.Count / 5; i < sm.Count * 4 / 5; i++) { after += Math.Abs(sm[i].Y - 100); n++; }
                    before /= line.Count; after /= Math.Max(1, n);
                    Console.WriteLine("OK : lissage " + strength + " % -> tremblement " + before.ToString("0.0") + " px => " + after.ToString("0.0") + " px");
                    if (after > before * .35) throw new Exception("Lissage " + strength + " % trop faible.");
                }
                passed++;

                // Bouton « Outil de dessin suivant » : la liste d'outils doit rester cohérente
                // avec la validation de la configuration, et l'action doit survivre à Normalize().
                if (DrawingTools.Names.Length != DrawingTools.Captions.Length || DrawingTools.Names.Length != DrawingTools.Icons.Length)
                    throw new Exception("Listes d'outils de dessin incohérentes.");
                string tool = "Stylo";
                for (int i = 0; i < DrawingTools.Names.Length * 2; i++)
                {
                    Config cfg = new Config();
                    cfg.AnnotationTool = tool; cfg.ButtonX2 = "Outil de dessin suivant"; cfg.Normalize();
                    if (cfg.AnnotationTool != tool) throw new Exception("Outil « " + tool + " » rejeté par la validation.");
                    if (cfg.ButtonX2 != "Outil de dessin suivant") throw new Exception("Action « Outil de dessin suivant » rejetée.");
                    tool = DrawingTools.Names[(DrawingTools.IndexOf(tool) + 1) % DrawingTools.Names.Length];
                }
                if (tool != "Stylo") throw new Exception("Le cycle d'outils ne boucle pas.");
                passed++; Console.WriteLine("OK : cycle des " + DrawingTools.Names.Length + " outils + action de souris validés");

                // Appui long : toutes les actions sont acceptées en court et en long ; valeurs invalides et durée bornées.
                foreach (string action in Config.ButtonActions)
                {
                    Config c2 = new Config();
                    c2.ButtonX1 = action; c2.ButtonX1Long = action; c2.ButtonX2Long = action; c2.ButtonMiddleLong = action; c2.Normalize();
                    if (c2.ButtonX1 != action || c2.ButtonX1Long != action || c2.ButtonX2Long != action || c2.ButtonMiddleLong != action)
                        throw new Exception("Action « " + action + " » rejetée par la validation.");
                }
                Config bad = new Config();
                bad.ButtonMiddleLong = "n'importe quoi"; bad.LongPressMs = 5; bad.Normalize();
                if (bad.ButtonMiddleLong != "Normal" || bad.LongPressMs != 250) throw new Exception("Validation de l'appui long incorrecte.");
                bad.LongPressMs = 99999; bad.ButtonX2 = "Effacer les dessins"; bad.ButtonX2Long = "Effacer les dessins"; bad.ResetButtons(); bad.Normalize();
                if (bad.LongPressMs != 1200 || bad.ButtonX2 != "Normal" || bad.ButtonX2Long != "Normal") throw new Exception("ResetButtons / bornes incorrects.");
                Config guard = new Config(); guard.ButtonLeftLong = "Effacer les dessins"; guard.ButtonRightLong = "Dessin : activer / arrêter"; guard.Normalize();
                if (guard.ButtonLeftLong != "Normal" || guard.ButtonRightLong != "Normal") throw new Exception("Appui long autorisé sur un clic principal.");
                passed++; Console.WriteLine("OK : appui court / long validés (" + Config.ButtonActions.Length + " actions)");
                // Identité : version cohérente, logo rendu (plaque opaque au centre, coins arrondis transparents).
                if (!System.Text.RegularExpressions.Regex.IsMatch(Brand.Version, @"^\d+\.\d+\.\d+$")) throw new Exception("Version mal formée.");
                if (Brand.History[0][0] != Brand.Version) throw new Exception("L'historique ne commence pas par la version courante.");
                if (Brand.Author.Length == 0) throw new Exception("Auteur manquant.");
                using (System.Drawing.Bitmap logo = Brand.RenderLogo(128))
                {
                    if (logo.GetPixel(64, 90).A != 255) throw new Exception("Logo : centre non opaque.");
                    if (logo.GetPixel(0, 0).A != 0) throw new Exception("Logo : coin non transparent.");
                }
                passed++; Console.WriteLine("OK : identité " + Brand.Product + " " + Brand.Version + " (logo, version, auteur)");
                // 2.6.3 : identité stable des particules, loupe dans l'écran, réglages illisibles refusés.
                TrailDot d1 = new TrailDot(new System.Drawing.Point(1, 1), DateTime.UtcNow, 1f), d2 = new TrailDot(new System.Drawing.Point(2, 2), DateTime.UtcNow, 1f);
                if (d2.Id <= d1.Id) throw new Exception("Identité de particule non croissante.");
                System.Drawing.Rectangle desk = new System.Drawing.Rectangle(0, 0, 1920, 1080);
                Config lens = new Config(); lens.LoupeSize = 300;
                foreach (System.Drawing.Point pt in new System.Drawing.Point[] { new System.Drawing.Point(5, 5), new System.Drawing.Point(1915, 1075), new System.Drawing.Point(960, 540) })
                    if (!desk.Contains(OverlayForm.LensRect(pt, lens, desk))) throw new Exception("Loupe hors écran en " + pt + ".");
                bool refused = false;
                try { ConfigStore.Deserialize("{\"Ring\": tr"); } catch { refused = true; }
                if (!refused) throw new Exception("JSON tronqué accepté.");
                passed++; Console.WriteLine("OK : particules stables, loupe dans l'écran, réglages illisibles refusés");
                Console.WriteLine("SUCCÈS : " + passed + " familles de tests géométriques.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ECHEC GEOMETRIE : " + ex.Message);
                return 1;
            }
        }
    }
}
