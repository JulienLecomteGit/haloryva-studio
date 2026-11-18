using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CursorFlow
{
    // Executes production geometry and rasterization on synthetic positions only.
    // No desktop window, hook, profile, screenshot or installed instance is used.
    internal static class TrailRenderingSelfTest
    {
        private sealed class Result
        {
            internal string Name, Detail;
            internal bool Passed;
        }
        private sealed class Measure
        {
            internal string Name;
            internal int Points, Triangles, Width, Height, Passes, TimedSamples;
            internal long Visits, TotalVisits, Budget;
            internal double Scale, MedianMs, P95Ms, MaximumMs;
            internal bool Degraded, FallbackPath;
            internal double GeometryError;
            internal int TessellationChecks, ResolutionChecks;
            internal bool GeometryLimited, ResolutionChanged;
        }
        private static readonly DateTime now = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
        private static readonly List<Result> results = new List<Result>();
        private static readonly List<Measure> measures = new List<Measure>();
        private static readonly List<string> pictures = new List<string>();
        private static string directory;

        internal static int Run()
        {
            ConfigureOutput();
            results.Clear(); measures.Clear(); pictures.Clear(); directory = null;
            try
            {
                directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "Preuves-trainee-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
                if (Directory.Exists(directory) || File.Exists(directory))
                    throw new IOException("Le dossier de preuves existe deja ; aucun ecrasement.");
                Directory.CreateDirectory(directory);
                Case("tete_exacte_sans_modifier_historique", ExactHead);
                Case("tete_inconnue_non_inventee", UnknownHead);
                Case("repos_ne_rajeunit_pas_la_trainee", IdleHead);
                Case("pause_produit_une_rupture", PauseBreak);
                Case("pause_preserve_l_extremite_non_echantillonnee", PauseEndPreserved);
                Case("expiration_et_desactivation", Expiration);
                Case("expiration_partielle_preserve_le_troncon_vivant", PartialExpiration);
                Case("identites_de_motifs_independantes_de_l_apercu", LocalIdentities);
                Case("tangentes_C1_partagees", SharedTangents);
                Case("extremites_exactes_et_bornes", GeometryBounds);
                Case("raccords_horizontaux_uniformes", UniformJoins);
                Case("lumiere_sans_bosses_aux_raccords", GlowJoins);
                Case("opacite_sans_empilement_aux_croisements", Crossing);
                Case("rupture_sans_pont_visible", GapPixels);
                Case("opacite_et_fondu_mesurables", Fade);
                Case("lissage_modifie_reellement_les_pixels", SmoothingPixels);
                Case("coordonnees_negatives_translatees", NegativeCoordinates);
                Case("transparence_hors_ruban_et_PArgb", TransparencyAndPArgb);
                Case("surface_reutilisee_sans_fantome", ReuseSurface);
                Case("etat_Graphics_restaure", GraphicsState);
                Case("budgets_qualite_et_performance", Budgets);
                Case("secours_grande_surface_sans_aplat", FallbackNoFlatFill);
                Case("mesures_cout_local_de_rendu", Timing);
                Case("exp3_extremites_sous_pixels", SubpixelEndpoints);
                Case("exp3_courbure_adaptative_et_normales", AdaptiveCurve);
                Case("exp3_demi_tours_sans_trou_ni_surbrillance", Reversals);
                Case("exp3_demi_tour_dates_differentes", DatedReversal);
                Case("exp3_queue_progressive_et_age_inchange", ProgressiveTail);
                Case("exp3_lueur_radiale_sans_bandes_plates", RadialGlow);
                Case("exp3_grille_stable_et_hysteresis", StableResolution);
                Case("exp4_composite_identique_reference_dense", SparseCompositeIdentity);
                Case("exp4_reutilisation_et_styles_sans_fantome", SparseCompositeReuse);
                Case("exp4_composite_moins_de_pixels_sans_changer_image", SparseCompositeWork);
                Case("exp4_mesures_etapes_opt_in", StageTimingOptIn);
                WriteReport();
                int failures = 0;
                foreach (Result result in results) if (!result.Passed) failures++;
                Console.WriteLine("TOTAL RENDU " + results.Count + " scenarios ; " +
                    (results.Count - failures) + " reussis ; " + failures + " echecs.");
                Console.WriteLine("PREUVES TRAINEE : " + directory);
                return failures == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ECHEC PREUVES TRAINEE : " + ex.GetType().Name + " : " + ex.Message);
                return 1;
            }
        }

        private static void ConfigureOutput()
        {
            try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }
            try
            {
                StreamWriter output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
                StreamWriter error = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false));
                output.AutoFlush = true; error.AutoFlush = true;
                Console.SetOut(output); Console.SetError(error);
            }
            catch { }
        }
        private static void Case(string name, Action test)
        {
            Result result = new Result { Name = name, Passed = false, Detail = "" };
            try { test(); result.Passed = true; Console.WriteLine("OK RENDU " + name); }
            catch (Exception ex)
            {
                result.Detail = ex.GetType().Name + ": " + ex.Message;
                Console.Error.WriteLine("ECHEC RENDU " + name + " : " + result.Detail);
            }
            results.Add(result);
        }
        private static void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
        private static double Distance(PointF a, PointF b)
        { double x = a.X - b.X, y = a.Y - b.Y; return Math.Sqrt(x * x + y * y); }
        private static TrailRibbonOptions Options(int smoothness, bool glow, bool performance)
        {
            return new TrailRibbonOptions { Width = 12, Life = 269, Opacity = 200,
                Smoothness = smoothness, SpeedResponsive = false, PerformanceMode = performance,
                Glow = glow, FirstColor = Color.Cyan, SecondColor = Color.MediumPurple, ColorMode = "Unie" };
        }
        private static List<TrailDot> Dots(params Point[] points)
        {
            List<TrailDot> dots = new List<TrailDot>();
            for (int i = 0; i < points.Length; i++)
                dots.Add(new TrailDot(points[i], now, 0) { BreakBefore = i == 0 });
            return dots;
        }
        private static List<TrailDot> Horizontal(int count)
        {
            List<TrailDot> dots = new List<TrailDot>();
            for (int i = 0; i < count; i++)
                dots.Add(new TrailDot(new Point(20 + (int)Math.Round(600.0 * i / (count - 1)), 120), now, 0)
                    { BreakBefore = i == 0 });
            return dots;
        }
        private static Bitmap Picture(TrailRibbon ribbon, IList<TrailDot> dots, TrailRibbonOptions options,
            DateTime time, int width, int height, float translateX, float translateY)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    if (translateX != 0 || translateY != 0) graphics.TranslateTransform(translateX, translateY);
                    ribbon.Draw(graphics, time, options, dots);
                }
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }
        private static Bitmap Picture(TrailRibbon ribbon, IList<TrailDot> dots, TrailRibbonOptions options)
        { return Picture(ribbon, dots, options, now, 660, 240, 0, 0); }
        private static void Save(Bitmap bitmap, string name)
        {
            string path = Path.Combine(directory, name + ".png");
            if (File.Exists(path)) throw new IOException("Preuve PNG deja presente.");
            bitmap.Save(path, ImageFormat.Png); pictures.Add(name + ".png");
        }

        private static void ExactHead()
        {
            TrailPipeline pipeline = new TrailPipeline();
            pipeline.Move(new Point(20, 80), now, 40, 269, true);
            pipeline.Move(new Point(33, 83), now.AddMilliseconds(4), 40, 269, true);
            int count = pipeline.History.Points.Count;
            long id = pipeline.History.Points[count - 1].Id;
            DateTime stamp = pipeline.History.Points[count - 1].Time;
            for (int i = 0; i < 20; i++)
            {
                pipeline.Prepare(new Point(33, 83), now.AddMilliseconds(4 + i), 269, true);
                Check(pipeline.RenderPoints.Count > 0, "tete absente");
                TrailDot head = pipeline.RenderPoints[pipeline.RenderPoints.Count - 1];
                Check(head.Point == new Point(33, 83) && head.Time == now.AddMilliseconds(4), "tete exacte ou date modifiee");
                Check(pipeline.History.Points.Count == count && pipeline.History.Points[count - 1].Id == id &&
                    pipeline.History.Points[count - 1].Time == stamp, "Prepare modifie l'historique");
            }
        }
        private static void UnknownHead()
        {
            TrailPipeline pipeline = new TrailPipeline();
            pipeline.Move(new Point(20, 80), now, 40, 269, true);
            pipeline.Move(new Point(33, 83), now.AddMilliseconds(4), 40, 269, true);
            pipeline.Prepare(new Point(600, 200), now.AddMilliseconds(5), 269, true);
            foreach (TrailDot dot in pipeline.RenderPoints)
                Check(dot.Point != new Point(600, 200), "un point non observe a ete ajoute");
        }
        private static void IdleHead()
        {
            foreach (int life in new int[] { 269, 2800 })
            {
                TrailPipeline pipeline = new TrailPipeline();
                pipeline.Move(new Point(20, 80), now, 40, life, true);
                pipeline.Move(new Point(33, 83), now.AddMilliseconds(4), 40, life, true);
                foreach (int delay in new int[] { 30, 100, 150, 180, life - 10 })
                {
                    pipeline.Move(new Point(33, 83), now.AddMilliseconds(delay), 40, life, true);
                    pipeline.Prepare(new Point(33, 83), now.AddMilliseconds(delay), life, true);
                    Check(pipeline.RenderPoints.Count > 0, "trainee immobile disparue avant sa duree");
                    TrailDot head = pipeline.RenderPoints[pipeline.RenderPoints.Count - 1];
                    Check(head.Point == new Point(33, 83) && head.Time == now.AddMilliseconds(4),
                        "tete immobile retiree ou rajeunie a " + delay + " ms / vie " + life);
                    foreach (TrailDot dot in pipeline.RenderPoints)
                        Check(dot.Time <= now.AddMilliseconds(4), "immobilite rajeunit un point");
                    if (life == 269 && delay == 150)
                    {
                        TrailRibbonOptions options = Options(65, false, false); options.Life = life;
                        using (TrailRibbon ribbon = new TrailRibbon())
                        using (Bitmap picture = Picture(ribbon, pipeline.RenderPoints, options, now.AddMilliseconds(delay), 660, 240, 0, 0))
                        {
                            Check(picture.GetPixel(33, 83).A > 0, "tete invisible a 150 ms / vie 269");
                            Save(picture, "Tete-immobile-150ms");
                        }
                    }
                }
                DateTime lastCap = now.AddMilliseconds(life + 2);
                pipeline.Prepare(new Point(33, 83), lastCap, life, true);
                Check(pipeline.History.Points.Count == 0 && pipeline.RenderPoints.Count == 1 &&
                    pipeline.RenderPoints[0].Point == new Point(33, 83) && pipeline.RenderPoints[0].Time == now.AddMilliseconds(4),
                    "dernier cap retire avec l'avant-dernier echantillon expire");
                Check(pipeline.HasLiveContent(lastCap, life), "cadence arretee alors que le dernier cap vit encore");
                TrailPipeline stopped = new TrailPipeline();
                stopped.Move(new Point(20, 80), now, 40, life, true);
                stopped.Move(new Point(33, 83), now.AddMilliseconds(4), 40, life, true);
                stopped.Prepare(new Point(33, 83), lastCap, life, true);
                stopped.ResetInput(); stopped.Prepare(new Point(33, 83), lastCap, life, true);
                Check(stopped.RenderPoints.Count == 1 && stopped.RenderPoints[0].Point == new Point(33, 83) &&
                    stopped.RenderPoints[0].Time == now.AddMilliseconds(4), "ResetInput retire le dernier cap encore vivant");
                pipeline.Prepare(new Point(33, 83), now.AddMilliseconds(life + 5), life, true);
                Check(pipeline.RenderPoints.Count == 0, "tete conservee au-dela de son expiration");
                Check(!pipeline.HasLiveContent(now.AddMilliseconds(life + 5), life) &&
                    !stopped.HasLiveContent(now.AddMilliseconds(life + 5), life), "contenu vivant fictif apres la derniere expiration");
                pipeline.Move(new Point(33, 83), now.AddMilliseconds(life + 20), 40, life, true);
                pipeline.Prepare(new Point(33, 83), now.AddMilliseconds(life + 20), life, true);
                Check(pipeline.History.Points.Count == 0 && pipeline.RenderPoints.Count == 0,
                    "un evenement immobile ressuscite la trainee expiree");
            }
        }
        private static void PauseBreak()
        {
            TrailPipeline pipeline = new TrailPipeline();
            pipeline.Move(new Point(20, 120), now, 6, 1000, true);
            pipeline.Move(new Point(100, 120), now.AddMilliseconds(20), 6, 1000, true);
            pipeline.Move(new Point(500, 120), now.AddMilliseconds(200), 6, 1000, true);
            pipeline.Move(new Point(600, 120), now.AddMilliseconds(220), 6, 1000, true);
            pipeline.Prepare(new Point(600, 120), now.AddMilliseconds(220), 1000, true);
            bool broken = false;
            foreach (TrailDot dot in pipeline.RenderPoints) if (dot.Point.X == 500 && dot.BreakBefore) broken = true;
            Check(broken, "absence de rupture apres pause longue");
            TrailRibbonOptions options = Options(65, false, false); options.Life = 1000;
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap picture = Picture(ribbon, pipeline.RenderPoints, options, now.AddMilliseconds(220), 660, 240, 0, 0))
            {
                Check(picture.GetPixel(300, 120).A == 0, "un pont a ete dessine a travers la pause");
                Save(picture, "Pause-apres-mouvement");
            }
        }
        private static void Expiration()
        {
            TrailPipeline pipeline = new TrailPipeline();
            pipeline.Move(new Point(20, 120), now, 6, 269, true);
            pipeline.Move(new Point(600, 120), now.AddMilliseconds(20), 6, 269, true);
            pipeline.Move(new Point(620, 120), now.AddMilliseconds(25), 6, 269, false);
            pipeline.Prepare(new Point(620, 120), now.AddMilliseconds(25), 269, true);
            foreach (TrailDot dot in pipeline.RenderPoints) Check(dot.Point.X != 620, "effet desactive ajoute une tete");
            pipeline.Prepare(new Point(620, 120), now.AddMilliseconds(400), 269, true);
            Check(pipeline.History.Points.Count == 0 && pipeline.RenderPoints.Count == 0, "points expires encore presents");
        }
        private static void PauseEndPreserved()
        {
            foreach (bool explicitReset in new bool[] { false, true })
            {
                TrailPipeline pipeline = new TrailPipeline();
                pipeline.Move(new Point(20, 80), now, 40, 700, true);
                pipeline.Move(new Point(33, 83), now.AddMilliseconds(4), 40, 700, true);
                Check(pipeline.History.Points.Count == 1, "fixture : extremite deja echantillonnee");
                if (explicitReset) pipeline.ResetInput();
                int resume = explicitReset ? 40 : 200;
                pipeline.Move(new Point(500, 120), now.AddMilliseconds(resume), 40, 700, true);
                pipeline.Move(new Point(600, 120), now.AddMilliseconds(resume + 20), 40, 700, true);
                pipeline.Prepare(new Point(600, 120), now.AddMilliseconds(resume + 20), 700, true);
                int endpoint = -1, restart = -1;
                for (int i = 0; i < pipeline.RenderPoints.Count; i++)
                {
                    TrailDot dot = pipeline.RenderPoints[i];
                    if (dot.Point == new Point(33, 83) && dot.Time == now.AddMilliseconds(4)) endpoint = i;
                    if (dot.Point == new Point(500, 120) && dot.BreakBefore) restart = i;
                }
                Check(endpoint >= 0 && restart > endpoint, "extremite avant pause perdue ou rupture absente");
                TrailRibbonOptions options = Options(65, false, false); options.Life = 700;
                using (TrailRibbon ribbon = new TrailRibbon())
                using (Bitmap picture = Picture(ribbon, pipeline.RenderPoints, options, now.AddMilliseconds(resume + 20), 660, 240, 0, 0))
                {
                    Check(picture.GetPixel(33, 83).A > 0, "extremite vivante disparue apres reprise");
                    Check(picture.GetPixel(260, 100).A == 0, "pont entre l'ancienne portion et la reprise");
                    Save(picture, explicitReset ? "Extremite-apres-reset" : "Extremite-apres-pause");
                }
            }
        }
        private static void PartialExpiration()
        {
            List<TrailDot> dots = Dots(new Point(20, 120), new Point(200, 120), new Point(400, 120), new Point(620, 120));
            dots[0].Time = now.AddMilliseconds(-500); dots[1].Time = now.AddMilliseconds(-300);
            dots[2].Time = now.AddMilliseconds(-100);
            TrailRibbonOptions options = Options(65, false, false); options.Life = 400;
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap picture = Picture(ribbon, dots, options))
            {
                Check(ribbon.Points.Count >= 2 && Distance(ribbon.Points[0].Point, dots[1].Point) < .001,
                    "expiration partielle supprime un point encore vivant");
                Check(Distance(ribbon.Points[ribbon.Points.Count - 1].Point, dots[3].Point) < .001,
                    "expiration partielle supprime la tete");
                Check(picture.GetPixel(80, 120).A == 0, "queue expiree encore visible");
                Check(picture.GetPixel(300, 120).A > 0 && picture.GetPixel(540, 120).A > 0,
                    "troncon vivant coupe lors de l'expiration");
                CheckPArgb(picture); Save(picture, "Expiration-partielle");
            }
        }
        private static void LocalIdentities()
        {
            TrailHistory desktop = new TrailHistory(), preview = new TrailHistory();
            desktop.Move(new Point(10, 10), now, 2, 700, true);
            long first = desktop.Points[0].Id;
            preview.Move(new Point(20, 30), now, 2, 700, true);
            for (int i = 1; i <= 50; i++) preview.Move(new Point(20 + i * 10, 30), now.AddMilliseconds(i * 2), 2, 700, true);
            desktop.Move(new Point(30, 10), now.AddMilliseconds(10), 2, 700, true);
            Check(first == 1 && desktop.Points.Count > 1, "ordinal local initial ou echantillonnage absent");
            for (int i = 1; i < desktop.Points.Count; i++)
                Check(desktop.Points[i].Id == desktop.Points[i - 1].Id + 1, "l'apercu consomme les identites du Bureau");
            long last = desktop.Points[desktop.Points.Count - 1].Id;
            desktop.Expire(now.AddMilliseconds(800), 700);
            desktop.Move(new Point(40, 10), now.AddMilliseconds(801), 2, 700, true);
            Check(desktop.Points[0].Id == last + 1, "expiration reinitialise la phase des motifs");
            desktop.Clear(); desktop.Move(new Point(10, 10), now.AddMilliseconds(900), 2, 700, true);
            Check(desktop.Points[0].Id == 1, "reset explicite ne reinitialise pas la phase");
        }
        private static void SharedTangents()
        {
            PointF p0 = new PointF(10, 20), p1 = new PointF(90, 160), p2 = new PointF(200, 60),
                p3 = new PointF(330, 140), p4 = new PointF(500, 30);
            foreach (int amount in new int[] { 0, 25, 65, 100 })
            {
                PointF a, b, c, d;
                float tension = TrailPath.SmoothingTension(amount);
                TrailPath.RibbonCurveControls(p0, p1, p2, p3, tension, out a, out b);
                TrailPath.RibbonCurveControls(p1, p2, p3, p4, tension, out c, out d);
                Check(Distance(new PointF(p2.X - b.X, p2.Y - b.Y), new PointF(c.X - p2.X, c.Y - p2.Y)) < .002,
                    "tangente discontinue a " + amount);
                if (amount == 0) Check(Distance(a, p1) < .001 && Distance(b, p2) < .001, "lissage zero non lineaire");
            }
        }
        private static void GeometryBounds()
        {
            List<TrailDot> dots = Dots(new Point(25, 120), new Point(120, 45), new Point(230, 190),
                new Point(370, 70), new Point(510, 170), new Point(620, 120));
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap picture = Picture(ribbon, dots, Options(100, true, false)))
            {
                Check(ribbon.Points.Count > 1, "geometrie vide");
                Check(Distance(ribbon.Points[0].Point, dots[0].Point) < .001 &&
                    Distance(ribbon.Points[ribbon.Points.Count - 1].Point, dots[dots.Count - 1].Point) < .001,
                    "extremite de geometrie decalee");
                foreach (TrailRibbonPoint point in ribbon.Points)
                {
                    Check(!Single.IsNaN(point.Point.X) && !Single.IsNaN(point.Point.Y) &&
                        !Single.IsInfinity(point.Point.X) && !Single.IsInfinity(point.Point.Y), "geometrie non finie");
                    Check(point.Point.X >= ribbon.LastBounds.Left - .01f && point.Point.X <= ribbon.LastBounds.Right + .01f &&
                        point.Point.Y >= ribbon.LastBounds.Top - .01f && point.Point.Y <= ribbon.LastBounds.Bottom + .01f,
                        "geometrie hors de la surface annoncee");
                }
                Save(picture, "Lumiere-courbe-100");
            }
        }
        private static void UniformJoins() { CheckUniform(false, "Ligne-raccords"); }
        private static void GlowJoins() { CheckUniform(true, "Lumiere-raccords"); }
        private static void CheckUniform(bool glow, string name)
        {
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap picture = Picture(ribbon, Horizontal(61), Options(65, glow, false)))
            {
                int min = 255, max = 0;
                for (int x = 40; x <= 600; x++) { int alpha = picture.GetPixel(x, 120).A; min = Math.Min(min, alpha); max = Math.Max(max, alpha); }
                Check(min > 20 && max - min <= 8, "bosses aux raccords : min=" + min + ", max=" + max);
                Save(picture, name);
            }
        }
        private static void Crossing()
        {
            TrailRibbonOptions options = Options(0, false, false); options.Opacity = 128; options.Width = 16;
            List<TrailDot> dots = Dots(new Point(40, 40), new Point(280, 200), new Point(280, 40), new Point(40, 200));
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap picture = Picture(ribbon, dots, options))
            {
                int alpha = picture.GetPixel(160, 120).A;
                Check(alpha >= 110 && alpha <= 132, "croisement troue ou empile : alpha=" + alpha);
                Save(picture, "Croisement-sans-surbrillance");
            }
        }
        private static void GapPixels()
        {
            List<TrailDot> dots = Dots(new Point(20, 120), new Point(180, 120), new Point(460, 120), new Point(620, 120));
            dots[2].BreakBefore = true;
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap picture = Picture(ribbon, dots, Options(100, true, false)))
            {
                for (int x = 250; x <= 390; x++) Check(picture.GetPixel(x, 120).A == 0, "pixel visible dans la rupture");
                Check(picture.GetPixel(100, 120).A > 0 && picture.GetPixel(540, 120).A > 0, "un troncon vivant a disparu");
                Save(picture, "Deux-troncons-separes");
            }
        }
        private static void Fade()
        {
            List<TrailDot> dots = Horizontal(31);
            TrailRibbonOptions options = Options(65, false, false); options.Life = 400; options.Opacity = 200;
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap fresh = Picture(ribbon, dots, options, now, 660, 240, 0, 0))
            using (Bitmap aged = Picture(ribbon, dots, options, now.AddMilliseconds(200), 660, 240, 0, 0))
            using (Bitmap expired = Picture(ribbon, dots, options, now.AddMilliseconds(401), 660, 240, 0, 0))
            {
                Check(fresh.GetPixel(320, 120).A > aged.GetPixel(320, 120).A && aged.GetPixel(320, 120).A > 0,
                    "le vieillissement ne reduit pas le rendu");
                Check(NonTransparent(expired) == 0, "pixels presents apres expiration totale");
                Save(aged, "Fondu-mi-vie");
            }
            using (TrailRibbon ribbon = new TrailRibbon())
            {
                options.Opacity = 80;
                using (Bitmap low = Picture(ribbon, dots, options))
                {
                    options.Opacity = 220;
                    using (Bitmap high = Picture(ribbon, dots, options))
                        Check(high.GetPixel(320, 120).A > low.GetPixel(320, 120).A + 80, "opacite peu ou pas appliquee");
                }
            }
        }
        private static void SmoothingPixels()
        {
            List<TrailDot> dots = Dots(new Point(20, 160), new Point(170, 35), new Point(320, 195), new Point(470, 35), new Point(620, 160));
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap straight = Picture(ribbon, dots, Options(0, false, false)))
            using (Bitmap smooth = Picture(ribbon, dots, Options(100, false, false)))
            {
                int different = 0;
                for (int y = 0; y < straight.Height; y++) for (int x = 0; x < straight.Width; x++)
                    if (Math.Abs(straight.GetPixel(x, y).A - smooth.GetPixel(x, y).A) > 20) different++;
                Check(different > 100, "le reglage de lissage n'a pas d'effet pixel mesurable");
                Save(straight, "Lissage-0"); Save(smooth, "Lissage-100");
            }
        }
        private static void NegativeCoordinates()
        {
            List<TrailDot> normal = Dots(new Point(30, 120), new Point(220, 45), new Point(410, 190), new Point(620, 120));
            List<TrailDot> negative = new List<TrailDot>();
            foreach (TrailDot dot in normal)
                negative.Add(new TrailDot(new Point(dot.Point.X - 1920, dot.Point.Y - 600), dot.Time, dot.Speed) { BreakBefore = dot.BreakBefore });
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap a = Picture(ribbon, normal, Options(65, false, false)))
            using (Bitmap b = Picture(ribbon, negative, Options(65, false, false), now, 660, 240, 1920, 600))
            {
                int different = 0;
                for (int y = 0; y < a.Height; y++) for (int x = 0; x < a.Width; x++)
                    if (Math.Abs(a.GetPixel(x, y).A - b.GetPixel(x, y).A) > 4) different++;
                Check(different <= 120, "la translation d'ecran change le ruban : " + different + " pixels");
                Save(b, "Coordonnees-negatives");
            }
        }
        private static int NonTransparent(Bitmap picture)
        {
            int count = 0;
            for (int y = 0; y < picture.Height; y++) for (int x = 0; x < picture.Width; x++)
                if (picture.GetPixel(x, y).A != 0) count++;
            return count;
        }
        private static void CheckPArgb(Bitmap picture)
        {
            BitmapData data = picture.LockBits(new Rectangle(0, 0, picture.Width, picture.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                byte[] row = new byte[picture.Width * 4];
                for (int y = 0; y < picture.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    for (int x = 0; x < picture.Width; x++)
                    {
                        int p = x * 4, alpha = row[p + 3];
                        if (row[p] > alpha || row[p + 1] > alpha || row[p + 2] > alpha)
                            throw new InvalidOperationException("RGB non premultiplie a " + x + "," + y);
                    }
                }
            }
            finally { picture.UnlockBits(data); }
        }
        private static void TransparencyAndPArgb()
        {
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap picture = Picture(ribbon, Horizontal(61), Options(65, true, false)))
            {
                Check(NonTransparent(picture) > 0, "ruban absent");
                RectangleF bounds = ribbon.LastBounds;
                for (int y = 0; y < picture.Height; y++) for (int x = 0; x < picture.Width; x++)
                    if (x < Math.Floor(bounds.Left) - 2 || x > Math.Ceiling(bounds.Right) + 2 ||
                        y < Math.Floor(bounds.Top) - 2 || y > Math.Ceiling(bounds.Bottom) + 2)
                        Check(picture.GetPixel(x, y).A == 0, "pixel opaque hors de la surface du ruban");
                CheckPArgb(picture); Save(picture, "Transparence-PArgb");
            }
        }
        private static void ReuseSurface()
        {
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap large = Picture(ribbon, Horizontal(61), Options(65, false, false)))
            using (Bitmap small = Picture(ribbon, Dots(new Point(280, 60), new Point(360, 60)), Options(65, false, false)))
            using (Bitmap empty = Picture(ribbon, new List<TrailDot>(), Options(65, false, false)))
            {
                Check(large.GetPixel(100, 120).A > 0, "premier trace absent");
                Check(small.GetPixel(100, 120).A == 0 && small.GetPixel(320, 60).A > 0, "fantome de la grande surface");
                Check(NonTransparent(empty) == 0, "surface vide restitue d'anciens pixels");
                Check(ribbon.Points.Count == 0, "geometrie precedente conservee apres Draw vide");
                CheckPArgb(small); CheckPArgb(empty); Save(small, "Surface-reutilisee-petite"); Save(empty, "Surface-reutilisee-vide");
            }
        }
        private static void GraphicsState()
        {
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap picture = new Bitmap(660, 240, PixelFormat.Format32bppPArgb))
            {
                using (Graphics graphics = Graphics.FromImage(picture))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.TranslateTransform(7, 9); graphics.SetClip(new Rectangle(40, 30, 500, 170));
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.SmoothingMode = SmoothingMode.HighSpeed;
                    graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                    graphics.PixelOffsetMode = PixelOffsetMode.Half;
                    CompositingMode compositing = graphics.CompositingMode;
                    SmoothingMode smoothing = graphics.SmoothingMode;
                    InterpolationMode interpolation = graphics.InterpolationMode;
                    PixelOffsetMode pixelOffset = graphics.PixelOffsetMode;
                    float[] before;
                    using (Matrix matrix = graphics.Transform) before = matrix.Elements;
                    RectangleF clip = graphics.ClipBounds;
                    ribbon.Draw(graphics, now, Options(65, true, false), Horizontal(61));
                    using (Matrix matrix = graphics.Transform)
                        for (int i = 0; i < before.Length; i++) Check(Math.Abs(before[i] - matrix.Elements[i]) < .0001, "transformation modifiee");
                    Check(graphics.ClipBounds == clip && graphics.CompositingMode == compositing &&
                        graphics.SmoothingMode == smoothing && graphics.InterpolationMode == interpolation &&
                        graphics.PixelOffsetMode == pixelOffset, "etat graphique non restaure");
                }
                Check(picture.GetPixel(20, 129).A == 0, "le clip du caller a ete ignore");
                Save(picture, "Etat-Graphics-et-clip");
            }
        }
        private static void RecordMeasure(TrailRibbon ribbon, string name, double[] times)
        {
            double[] sorted = (double[])times.Clone(); Array.Sort(sorted);
            Measure value = new Measure { Name = name, Points = ribbon.LastPointCount, Triangles = ribbon.LastTriangleCount,
                Width = ribbon.LastRasterWidth, Height = ribbon.LastRasterHeight, Visits = ribbon.LastPixelVisits,
                TotalVisits = ribbon.LastTotalPixelVisits, Passes = ribbon.LastRasterPasses, TimedSamples = sorted.Length,
                Budget = ribbon.LastPixelBudget, Scale = ribbon.LastScale, Degraded = ribbon.LastDegraded,
                FallbackPath = ribbon.LastFallbackPath, GeometryError = ribbon.LastGeometryError,
                GeometryLimited = ribbon.LastGeometryLimited, TessellationChecks = ribbon.LastTessellationChecks,
                ResolutionChecks = ribbon.LastPreflightChecks, ResolutionChanged = ribbon.LastResolutionChanged };
            if (sorted.Length > 0)
            {
                value.MedianMs = (sorted[(sorted.Length - 1) / 2] + sorted[sorted.Length / 2]) / 2;
                value.P95Ms = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1]; value.MaximumMs = sorted[sorted.Length - 1];
            }
            measures.Add(value);
        }
        private static void CheckBudgets(TrailRibbon ribbon, bool performance)
        {
            long pixels = (long)ribbon.LastRasterWidth * ribbon.LastRasterHeight;
            Check(ribbon.LastPointCount <= TrailRibbon.MaxPoints && ribbon.LastPointCount > 1, "budget de geometrie depasse ou vide");
            Check(pixels <= (performance ? TrailRibbon.PerformancePixelLimit : TrailRibbon.QualityPixelLimit), "surface raster trop grande");
            Check(ribbon.LastFallbackPath || pixels > 1, "raster minuscule etire sur le rectangle du trace");
            Check(ribbon.LastPixelVisits <= ribbon.LastPixelBudget && ribbon.LastPixelBudget <=
                (performance ? TrailRibbon.PerformanceWorkLimit : TrailRibbon.QualityWorkLimit), "budget de pixels visites depasse");
            Check(ribbon.LastRasterPasses >= (ribbon.LastFallbackPath ? 0 : 1) && ribbon.LastRasterPasses <= TrailRibbon.MaximumRasterPasses &&
                ribbon.LastTotalPixelVisits >= ribbon.LastPixelVisits &&
                ribbon.LastTotalPixelVisits <= ribbon.LastPixelBudget * TrailRibbon.MaximumRasterPasses,
                "budget cumule des passes raster depasse");
            Check(ribbon.LastScale >= 1 && ribbon.LastScale <= TrailRibbon.MaxRasterScale &&
                !Double.IsNaN(ribbon.LastScale) && !Double.IsInfinity(ribbon.LastScale), "echelle invalide ou excessive");
            Check(ribbon.LastTessellationChecks <= TrailRibbon.MaxTessellationChecks &&
                ribbon.LastPreflightChecks <= TrailRibbon.MaxResolutionChecks, "travail de preparation hors borne");
        }
        private static void Budgets()
        {
            List<TrailDot> dots = new List<TrailDot>();
            for (int i = 0; i < 1600; i++)
                dots.Add(new TrailDot(new Point(95 + (int)Math.Round(470.0 * i / 1599), i % 2 == 0 ? 80 : 160), now, 0)
                    { BreakBefore = i == 0 });
            foreach (bool performance in new bool[] { false, true })
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap picture = Picture(ribbon, dots, Options(65, true, performance)))
            {
                CheckBudgets(ribbon, performance);
                Check(Distance(ribbon.Points[0].Point, dots[0].Point) < .001 &&
                    Distance(ribbon.Points[ribbon.Points.Count - 1].Point, dots[dots.Count - 1].Point) < .001,
                    "budget supprime une extremite de la trajectoire");
                Check(NonTransparent(picture) > 0, "budget rend la surface entierement vide");
                Check(picture.GetPixel(0, 0).A == 0 && picture.GetPixel(659, 0).A == 0 &&
                    picture.GetPixel(0, 239).A == 0 && picture.GetPixel(659, 239).A == 0,
                    "budget produit un aplat lumineux loin du chemin");
                CheckPArgb(picture);
                RecordMeasure(ribbon, performance ? "budget_performance" : "budget_qualite", new double[0]);
                Save(picture, performance ? "Budget-performance" : "Budget-qualite");
            }
        }
        private static void FallbackNoFlatFill()
        {
            List<TrailDot> dots = Dots(new Point(150, 150), new Point(1000, 2150), new Point(2000, 150),
                new Point(3000, 2150), new Point(3946, 150));
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap picture = Picture(ribbon, dots, Options(100, true, false), now, 4096, 2304, 0, 0))
            {
                CheckBudgets(ribbon, false);
                Check(ribbon.LastFallbackPath, "fixture : le secours de grande surface n'a pas ete sollicite");
                Check(picture.GetPixel(150, 150).A > 0 && picture.GetPixel(3946, 150).A > 0,
                    "secours coupe la queue ou la tete du chemin");
                Check(picture.GetPixel(0, 0).A == 0 && picture.GetPixel(3000, 300).A == 0 &&
                    picture.GetPixel(4095, 2303).A == 0, "secours remplit le rectangle hors du chemin");
                RecordMeasure(ribbon, "secours_grande_surface", new double[0]);
                Save(picture, "Secours-grande-surface-sans-aplat");
            }
        }
        private static void Timing()
        {
            List<TrailDot> dots = new List<TrailDot>();
            for (int i = 0; i < 120; i++)
            {
                double t = i / 119.0;
                dots.Add(new TrailDot(new Point(25 + (int)Math.Round(t * 600), 120 + (int)Math.Round(65 * Math.Sin(t * Math.PI * 4))), now, 0)
                    { BreakBefore = i == 0 });
            }
            foreach (bool performance in new bool[] { false, true })
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap picture = new Bitmap(660, 240, PixelFormat.Format32bppPArgb))
            {
                TrailRibbonOptions options = Options(65, true, performance);
                double[] times = new double[12];
                using (Graphics graphics = Graphics.FromImage(picture))
                {
                    for (int i = -2; i < times.Length; i++)
                    {
                        graphics.Clear(Color.Transparent);
                        Stopwatch clock = Stopwatch.StartNew(); ribbon.Draw(graphics, now, options, dots); clock.Stop();
                        if (i >= 0) times[i] = clock.Elapsed.TotalMilliseconds;
                        CheckBudgets(ribbon, performance);
                    }
                }
                RecordMeasure(ribbon, performance ? "cout_performance" : "cout_qualite", times);
                Save(picture, performance ? "Mouvement-rapide-performance" : "Mouvement-rapide-qualite");
            }
        }
        private static void SubpixelEndpoints()
        {
            List<TrailDot> dots = new List<TrailDot> {
                new TrailDot(new PointF(20.25f, 110.75f), now, 0) { BreakBefore = true },
                new TrailDot(new PointF(210.625f, 40.125f), now, 0),
                new TrailDot(new PointF(620.375f, 170.625f), now, 0) };
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap picture = Picture(ribbon, dots, Options(100, false, false)))
            {
                Check(Distance(ribbon.Points[0].Point, dots[0].ExactPoint) < .0001 &&
                    Distance(ribbon.Points[ribbon.Points.Count - 1].Point, dots[2].ExactPoint) < .0001,
                    "extremite sous-pixel arrondie ou retardee");
                Check(NonTransparent(picture) > 0, "trace sous-pixel absent");
                CheckBudgets(ribbon, false); CheckPArgb(picture); Save(picture, "Exp3-sous-pixels");
            }
        }
        private static double PointToSegment(PointF p, PointF a, PointF b)
        {
            double dx = b.X - (double)a.X, dy = b.Y - (double)a.Y, d = dx * dx + dy * dy;
            double t = d < .0000001 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / d));
            double x = p.X - a.X - t * dx, y = p.Y - a.Y - t * dy;
            return Math.Sqrt(x * x + y * y);
        }
        private static void AdaptiveCurve()
        {
            List<TrailDot> dots = Dots(new Point(25, 120), new Point(160, 40), new Point(310, 190),
                new Point(460, 45), new Point(620, 120));
            TrailRibbonOptions options = Options(100, false, false); options.Width = 32;
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap picture = Picture(ribbon, dots, options))
            {
                Check(!ribbon.LastGeometryLimited && ribbon.LastGeometryError <= TrailRibbon.QualityGeometryTolerance * 1.001,
                    "tolerance geometrique non tenue sur courbe ordinaire");
                double worst = 0, worstEdge = 0;
                for (int i = 1; i < dots.Count; i++)
                {
                    PointF a = dots[i - 1].ExactPoint, b = dots[i].ExactPoint, c1, c2;
                    TrailPath.RibbonCurveControls(i < 2 ? a : dots[i - 2].ExactPoint, a, b,
                        i + 1 == dots.Count ? b : dots[i + 1].ExactPoint, TrailPath.SmoothingTension(100), out c1, out c2);
                    // Independent dense reference, not the adaptive split rule.
                    for (int j = 0; j <= 200; j++)
                    {
                        double t = j / 200.0, u = 1 - t;
                        PointF exact = new PointF((float)(u * u * u * a.X + 3 * u * u * t * c1.X + 3 * u * t * t * c2.X + t * t * t * b.X),
                            (float)(u * u * u * a.Y + 3 * u * u * t * c1.Y + 3 * u * t * t * c2.Y + t * t * t * b.Y));
                        double nearest = Double.MaxValue;
                        for (int k = 1; k < ribbon.Points.Count; k++)
                            nearest = Math.Min(nearest, PointToSegment(exact, ribbon.Points[k - 1].Point, ribbon.Points[k].Point));
                        worst = Math.Max(worst, nearest);
                        double vx = u * u * (c1.X - a.X) + 2 * u * t * (c2.X - c1.X) + t * t * (b.X - c2.X);
                        double vy = u * u * (c1.Y - a.Y) + 2 * u * t * (c2.Y - c1.Y) + t * t * (b.Y - c2.Y);
                        double magnitude = Math.Sqrt(vx * vx + vy * vy);
                        if (magnitude > .0001)
                            for (int side = -1; side <= 1; side += 2)
                            {
                                PointF edge = new PointF((float)(exact.X - vy / magnitude * 16 * side),
                                    (float)(exact.Y + vx / magnitude * 16 * side));
                                double nearestEdge = Double.MaxValue;
                                for (int k = 1; k < ribbon.Points.Count; k++)
                                    nearestEdge = Math.Min(nearestEdge, PointToSegment(edge,
                                        RibbonEdge(ribbon.Points[k - 1], side), RibbonEdge(ribbon.Points[k], side)));
                                worstEdge = Math.Max(worstEdge, nearestEdge);
                            }
                    }
                    if (i + 1 < dots.Count)
                    {
                        double length = Distance(b, c2);
                        if (length > .001)
                            foreach (TrailRibbonPoint point in ribbon.Points)
                                if (Distance(point.Point, b) < .0001)
                                    Check((point.Tangent.X * (b.X - c2.X) + point.Tangent.Y * (b.Y - c2.Y)) / length > .999,
                                        "normale non issue de la tangente analytique au raccord");
                    }
                }
                Check(worst <= .35, "ecart reel de la polyligne a la reference dense : " + worst);
                Check(worstEdge <= .4, "ecart du bord a la reference dense : " + worstEdge);
                CheckBudgets(ribbon, false); RecordMeasure(ribbon, "exp3_courbure_adaptative", new double[0]);
                Save(picture, "Exp3-courbure-adaptative");
                using (Bitmap straight = Picture(ribbon, Dots(new Point(20, 120), new Point(620, 120)), options))
                    Check(ribbon.Points.Count <= 4, "subdivision inutile d'une droite uniforme");
            }
        }
        private static PointF RibbonEdge(TrailRibbonPoint point, int side)
        { return new PointF(point.Point.X - point.Tangent.Y * point.HalfWidth * side,
            point.Point.Y + point.Tangent.X * point.HalfWidth * side); }
        private static void Reversals()
        {
            List<TrailDot> dots = Dots(new Point(50, 120), new Point(350, 120), new Point(50, 120), new Point(350, 121));
            foreach (int smooth in new int[] { 0, 100 })
            using (TrailRibbon ribbon = new TrailRibbon())
            {
                TrailRibbonOptions options = Options(smooth, false, false); options.Opacity = 128; options.Width = 14;
                using (Bitmap picture = Picture(ribbon, dots, options))
                {
                    Check(picture.GetPixel(350, 120).A > 0 && picture.GetPixel(50, 120).A > 0, "cap de demi-tour disparu");
                    for (int x = 65; x <= 335; x++)
                    {
                        int alpha = picture.GetPixel(x, 120).A;
                        Check(alpha >= 100 && alpha <= 130, "trou ou empilement au demi-tour : " + alpha);
                    }
                    Check(picture.GetPixel(200, 60).A == 0, "demi-tour deborde hors chemin");
                    CheckBudgets(ribbon, false); CheckPArgb(picture); Save(picture, "Exp3-demi-tour-" + smooth);
                }
            }
        }
        private static void DatedReversal()
        {
            List<TrailDot> dots = Dots(new Point(50, 120), new Point(350, 120), new Point(50, 120));
            dots[0].Time = now.AddMilliseconds(-100); dots[1].Time = now.AddMilliseconds(-50);
            TrailRibbonOptions options = Options(100, false, false); options.Width = 14;
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap picture = Picture(ribbon, dots, options))
            {
                // An old outward trace must not hide a pinched newer return.
                // At x=275 the returning portion is less than 40ms old: both
                // sides of its 14px width must retain the fresh return opacity.
                int above = picture.GetPixel(275, 114).A, below = picture.GetPixel(275, 125).A;
                Check(above >= 185 && below >= 185 && Math.Abs(above - below) <= 4,
                    "portion recente pincee au demi-tour : " + above + "/" + below);
                CheckBudgets(ribbon, false); CheckPArgb(picture); Save(picture, "Exp3-demi-tour-dates-differentes");
            }
        }
        private static void ProgressiveTail()
        {
            List<TrailDot> dots = new List<TrailDot>();
            for (int i = 0; i < 13; i++)
                dots.Add(new TrailDot(new Point(20 + i * 50, 120), now.AddMilliseconds(-950 + 950.0 * i / 12), 0)
                    { BreakBefore = i == 0 });
            TrailRibbonOptions options = Options(0, false, false); options.Life = 1000; options.Width = 20;
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap picture = Picture(ribbon, dots, options))
            {
                TrailRibbonPoint tail = ribbon.Points[0], head = ribbon.Points[ribbon.Points.Count - 1];
                Check(tail.Time == dots[0].Time && head.Time == dots[dots.Count - 1].Time, "profil esthetique change l'age");
                Check(tail.HalfWidth < head.HalfWidth * .15 && head.HalfWidth >= 9.9, "queue non effilee ou tete amincie");
                for (int i = 1; i < ribbon.Points.Count; i++)
                    Check(ribbon.Points[i].HalfWidth >= ribbon.Points[i - 1].HalfWidth - .001 &&
                        ribbon.Points[i].Alpha >= ribbon.Points[i - 1].Alpha - .001, "profil queue/tete non monotone");
                Check(picture.GetPixel(500, 120).A > picture.GetPixel(80, 120).A, "fondu longitudinal non visible");
                CheckBudgets(ribbon, false); CheckPArgb(picture); Save(picture, "Exp3-queue-progressive");
            }
        }
        private static void RadialGlow()
        {
            using (TrailRibbon ribbon = new TrailRibbon())
            using (Bitmap picture = Picture(ribbon, Horizontal(31), Options(65, true, false)))
            {
                int near = picture.GetPixel(320, 130).A, middle = picture.GetPixel(320, 140).A, far = picture.GetPixel(320, 150).A;
                Check(near > middle && middle > far && far > 0, "lueur radiale plate ou non decroissante");
                Check(picture.GetPixel(320, 166).A == 0, "lueur hors de son rayon borne");
                int changes = 0, previous = picture.GetPixel(320, 128).A;
                for (int y = 129; y <= 158; y++)
                {
                    int alpha = picture.GetPixel(320, y).A;
                    Check(alpha <= previous + 1, "rebond de luminosite dans la lueur");
                    if (alpha != previous) changes++; previous = alpha;
                }
                Check(changes >= 15, "lueur reduite a quelques bandes uniformes");
                CheckPArgb(picture); Save(picture, "Exp3-lueur-radiale");
            }
        }
        private static void StableResolution()
        {
            List<TrailDot> large = Dots(new Point(80, 80), new Point(1110, 720));
            List<TrailDot> smaller = Dots(new Point(80, 80), new Point(700, 420));
            TrailRibbonOptions options = Options(65, false, false); options.Life = 2800;
            using (TrailRibbon ribbon = new TrailRibbon())
            {
                double held; PointF origin;
                using (Bitmap first = Picture(ribbon, large, options, now, 1200, 800, 0, 0))
                {
                    Check(!ribbon.LastFallbackPath && ribbon.LastScale > 1, "fixture : pas de raster reduit");
                    held = ribbon.LastScale; origin = ribbon.LastRasterOrigin; CheckBudgets(ribbon, false);
                    Check(Math.Abs(origin.X / held - Math.Round(origin.X / held)) < .001 &&
                        Math.Abs(origin.Y / held - Math.Round(origin.Y / held)) < .001, "origine non alignee a la grille");
                    Save(first, "Exp3-grille-resolution-reduite");
                }
                foreach (int delay in new int[] { 20, 140 })
                    using (Bitmap waiting = Picture(ribbon, smaller, options, now.AddMilliseconds(delay), 1200, 800, 0, 0))
                    {
                        Check(ribbon.LastScale == held && !ribbon.LastResolutionChanged, "resolution change avant l'hysteresis");
                        Check(ribbon.LastRasterOrigin == origin, "phase de grille modifiee a resolution constante");
                        CheckBudgets(ribbon, false);
                    }
                using (Bitmap finer = Picture(ribbon, smaller, options, now.AddMilliseconds(230), 1200, 800, 0, 0))
                {
                    Check(ribbon.LastScale == 1 && ribbon.LastResolutionChanged, "resolution fine non retrouvee apres marge durable");
                    CheckBudgets(ribbon, false); CheckPArgb(finer); Save(finer, "Exp3-grille-retour-resolution-native");
                }
            }
        }
        private static string Quote(string value)
        {
            StringBuilder text = new StringBuilder("\"");
            foreach (char c in value ?? "")
            {
                if (c == '\\') text.Append("\\\\");
                else if (c == '"') text.Append("\\\"");
                else if (c < 32) text.Append("\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else text.Append(c);
            }
            return text.Append('"').ToString();
        }
        private static string Number(double value)
        { return value.ToString("0.######", CultureInfo.InvariantCulture); }
        private static void SamePixels(Bitmap expected, Bitmap actual, string context)
        {
            Check(expected.Size == actual.Size, "dimensions differentes : " + context);
            Rectangle area = new Rectangle(0, 0, expected.Width, expected.Height);
            BitmapData a = expected.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                BitmapData b = actual.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    byte[] rowA = new byte[expected.Width * 4], rowB = new byte[expected.Width * 4];
                    for (int y = 0; y < expected.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(a.Scan0, y * a.Stride), rowA, 0, rowA.Length);
                        Marshal.Copy(IntPtr.Add(b.Scan0, y * b.Stride), rowB, 0, rowB.Length);
                        for (int x = 0; x < rowA.Length; x++)
                            if (rowA[x] != rowB[x])
                                throw new InvalidOperationException("pixel different : " + context +
                                    " ligne=" + y + " octet=" + x);
                    }
                }
                finally { actual.UnlockBits(b); }
            }
            finally { expected.UnlockBits(a); }
        }

        private static void SparseCompositeIdentity()
        {
            List<TrailDot> curved = new List<TrailDot> {
                new TrailDot(new PointF(25.25f, 110.75f), now.AddMilliseconds(-190), .2f) { BreakBefore = true },
                new TrailDot(new PointF(170.625f, 40.125f), now.AddMilliseconds(-145), 3),
                new TrailDot(new PointF(310.375f, 190.625f), now.AddMilliseconds(-90), .4f),
                new TrailDot(new PointF(450.75f, 45.25f), now.AddMilliseconds(-40), 2.2f),
                new TrailDot(new PointF(625.5f, 120.75f), now, 1.4f) };
            List<TrailDot> gaps = Dots(new Point(30, 30), new Point(600, 200), new Point(60, 200),
                new Point(600, 35), new Point(310, 120), new Point(620, 120));
            gaps[4].BreakBefore = true;
            List<TrailDot> broad = Dots(new Point(30, 40), new Point(1180, 660),
                new Point(70, 620), new Point(1180, 70));
            List<TrailDot> reversal = new List<TrailDot> {
                new TrailDot(new PointF(50.25f, 120.75f), now.AddMilliseconds(-120), .3f) { BreakBefore = true },
                new TrailDot(new PointF(350.125f, 120.25f), now.AddMilliseconds(-80), 3f),
                new TrailDot(new PointF(50.375f, 120.625f), now.AddMilliseconds(-40), .5f),
                new TrailDot(new PointF(350.75f, 121.5f), now, 2f) };
            int compared = 0;
            foreach (bool glow in new bool[] { false, true })
            foreach (bool performance in new bool[] { false, true })
            foreach (string color in new string[] { "Unie", "Dégradé", "Arc-en-ciel" })
            {
                TrailRibbonOptions options = Options(100, glow, performance);
                options.ColorMode = color; options.SpeedResponsive = true;
                using (TrailRibbon reference = new TrailRibbon(true))
                using (TrailRibbon optimized = new TrailRibbon())
                {
                    foreach (List<TrailDot> dots in new List<TrailDot>[] { curved, gaps, broad, reversal })
                    {
                        int width = dots == broad ? 1220 : 660, height = dots == broad ? 720 : 240;
                        using (Bitmap a = Picture(reference, dots, options, now, width, height, 0, 0))
                        using (Bitmap b = Picture(optimized, dots, options, now, width, height, 0, 0))
                        {
                            SamePixels(a, b, "glow=" + glow + ",performance=" + performance + ",color=" + color);
                            Check(reference.LastScale == optimized.LastScale &&
                                reference.LastPointCount == optimized.LastPointCount &&
                                reference.LastPixelVisits == optimized.LastPixelVisits &&
                                reference.LastFallbackPath == optimized.LastFallbackPath,
                                "la composition a change la geometrie ou la resolution");
                            Check(optimized.LastCompositePixels <= reference.LastCompositePixels,
                                "la composition par intervalles visite plus que la reference dense");
                            compared++;
                        }
                    }
                }
            }
            Check(compared == 48, "matrice de comparaison incomplete");
        }

        private static void SparseCompositeReuse()
        {
            using (TrailRibbon reference = new TrailRibbon(true))
            using (TrailRibbon optimized = new TrailRibbon())
            {
                for (int i = 0; i < 10; i++)
                {
                    bool glow = i % 3 != 1, performance = i % 2 == 1;
                    TrailRibbonOptions options = Options(i % 2 == 0 ? 100 : 0, glow, performance);
                    options.Width = i % 3 == 0 ? 28 : 5;
                    List<TrailDot> dots = i == 9 ? new List<TrailDot>() : i % 2 == 0 ?
                        Dots(new Point(15, 20), new Point(620, 220), new Point(40, 200)) :
                        Dots(new Point(285, 75), new Point(350, 75));
                    DateTime time = now.AddMilliseconds(i * 13);
                    using (Bitmap a = Picture(reference, dots, options, time, 660, 240, 0, 0))
                    using (Bitmap b = Picture(optimized, dots, options, time, 660, 240, 0, 0))
                    {
                        SamePixels(a, b, "reutilisation " + i);
                        CheckPArgb(b);
                        if (i == 9) Check(NonTransparent(b) == 0, "ancien ruban apres image vide");
                    }
                }
                // Reuse the same private allocations through a full fade to
                // transparent, then through an explicit zero-opacity setting.
                TrailRibbonOptions fading = Options(100, true, false);
                fading.SpeedResponsive = true;
                List<TrailDot> tail = Dots(new Point(25, 110), new Point(310, 35), new Point(625, 190));
                tail[0].Time = now.AddMilliseconds(-130);
                tail[1].Time = now.AddMilliseconds(-70);
                foreach (int age in new int[] { 0, 60, 140, 240, 268, 400 })
                {
                    using (Bitmap a = Picture(reference, tail, fading, now.AddMilliseconds(age), 660, 240, 0, 0))
                    using (Bitmap b = Picture(optimized, tail, fading, now.AddMilliseconds(age), 660, 240, 0, 0))
                    {
                        SamePixels(a, b, "queue et extinction age=" + age);
                        if (age == 400) Check(NonTransparent(b) == 0, "queue expiree non transparente");
                    }
                }
                fading.Opacity = 0;
                using (Bitmap a = Picture(reference, Horizontal(61), fading))
                using (Bitmap b = Picture(optimized, Horizontal(61), fading))
                {
                    SamePixels(a, b, "opacite zero");
                    Check(NonTransparent(b) == 0, "opacite nulle restitue un ancien ruban");
                }
            }
        }

        private static void SparseCompositeWork()
        {
            List<TrailDot> dots = Dots(new Point(25, 25), new Point(625, 210));
            TrailRibbonOptions options = Options(100, false, true); options.Width = 6;
            using (TrailRibbon reference = new TrailRibbon(true))
            using (TrailRibbon optimized = new TrailRibbon())
            using (Bitmap a = Picture(reference, dots, options))
            using (Bitmap b = Picture(optimized, dots, options))
            {
                SamePixels(a, b, "diagonale fine");
                Check(reference.LastCompositePixels == (long)reference.LastRasterWidth * reference.LastRasterHeight,
                    "reference dense incomplete");
                Check(optimized.LastCompositePixels > 0 && optimized.LastCompositePixels * 2 < reference.LastCompositePixels,
                    "la diagonale fine ne reduit pas les pixels fusionnes");
                Check(optimized.LastBitmapCopiedPixels == reference.LastBitmapCopiedPixels &&
                    optimized.LastBitmapCopyCalls >= 1 && optimized.LastBitmapCopyCalls <= reference.LastBitmapCopyCalls,
                    "copie incomplete ou plus de transitions que la reference");
                Console.WriteLine("COMPOSITE EXP4 pixels=" + optimized.LastCompositePixels +
                    ",reference=" + reference.LastCompositePixels + ",copies=" + optimized.LastBitmapCopyCalls +
                    ",copies_reference=" + reference.LastBitmapCopyCalls);
                Save(b, "Exp4-composite-diagonale-identique");
            }
        }

        private static void StageTimingOptIn()
        {
            using (TrailRibbon ribbon = new TrailRibbon())
            {
                using (Bitmap ignored = Picture(ribbon, Horizontal(61), Options(65, true, true)))
                    Check(!ribbon.LastStageTiming.Active && ribbon.LastStageTiming.TotalMs == 0,
                        "mesures actives sans demande");
                ribbon.MeasureCosts = true;
                using (Bitmap ignored = Picture(ribbon, Horizontal(61), Options(65, true, true)))
                {
                    RibbonStageTiming t = ribbon.LastStageTiming;
                    Check(t.Active, "mesures demandees absentes");
                    double sum = 0;
                    foreach (double value in new double[] { t.GeometryMs, t.ResolutionMs, t.BuffersMs,
                        t.RasterMs, t.CompositeMs, t.UploadMs, t.BlitMs, t.FallbackMs })
                    {
                        Check(value >= 0 && !Double.IsNaN(value) && !Double.IsInfinity(value), "mesure invalide");
                        sum += value;
                    }
                    Check(t.TotalMs >= sum, "etapes de rendu chevauchantes");
                }
                using (Bitmap ignored = Picture(ribbon, new List<TrailDot>(), Options(65, true, true)))
                {
                    Check(ribbon.LastStageTiming.Active && ribbon.LastStageTiming.RasterMs == 0 &&
                        ribbon.LastStageTiming.UploadMs == 0 && ribbon.LastCompositePixels == 0 &&
                        ribbon.LastBitmapCopyCalls == 0, "metriques precedentes conservees sur image vide");
                }
                ribbon.MeasureCosts = false;
                using (Bitmap ignored = Picture(ribbon, Horizontal(61), Options(65, true, true)))
                    Check(!ribbon.LastStageTiming.Active && ribbon.LastStageTiming.TotalMs == 0,
                        "mesures non desactivees");
            }
        }

        private static void WriteReport()
        {
            int passed = 0; foreach (Result result in results) if (result.Passed) passed++;
            StringBuilder json = new StringBuilder();
            json.Append("{\n  \"Version\": \"2.7.2\",\n  \"Nature\": \"TESTS_CSHARP_GDI_SUR_POSITIONS_FICTIVES\",\n");
            json.Append("  \"ExecuteUTC\": ").Append(Quote(DateTime.UtcNow.ToString("o"))).Append(",\n");
            json.Append("  \"InterfaceOuverte\": false,\n  \"CaptureEcran\": false,\n  \"Total\": ").Append(results.Count)
                .Append(",\n  \"Reussis\": ").Append(passed).Append(",\n  \"Echecs\": ").Append(results.Count - passed).Append(",\n");
            json.Append("  \"Limites\": \"Les temps mesurent Draw sur bitmap local apres deux prechauffages. Ni FPS ecran, ni GPU, ni fluidite perceptuelle garantie.\",\n");
            json.Append("  \"Scenarios\": [\n");
            for (int i = 0; i < results.Count; i++)
            {
                Result result = results[i];
                json.Append("    {\"Nom\":").Append(Quote(result.Name)).Append(",\"Reussi\":").Append(result.Passed ? "true" : "false")
                    .Append(",\"Detail\":").Append(Quote(result.Detail)).Append('}').Append(i + 1 == results.Count ? "\n" : ",\n");
            }
            json.Append("  ],\n  \"Mesures\": [\n");
            for (int i = 0; i < measures.Count; i++)
            {
                Measure measure = measures[i];
                json.Append("    {\"Nom\":").Append(Quote(measure.Name)).Append(",\"Points\":").Append(measure.Points)
                    .Append(",\"Triangles\":").Append(measure.Triangles).Append(",\"LargeurRaster\":").Append(measure.Width)
                    .Append(",\"HauteurRaster\":").Append(measure.Height).Append(",\"PixelsVisites\":").Append(measure.Visits)
                    .Append(",\"VisitesTotales\":").Append(measure.TotalVisits).Append(",\"PassesRaster\":").Append(measure.Passes)
                    .Append(",\"BudgetVisitesParPasse\":").Append(measure.Budget).Append(",\"Echelle\":").Append(Number(measure.Scale))
                    .Append(",\"Degrade\":").Append(measure.Degraded ? "true" : "false")
                    .Append(",\"SecoursChemin\":").Append(measure.FallbackPath ? "true" : "false")
                    .Append(",\"ErreurGeometrieEstimeePx\":").Append(Number(measure.GeometryError))
                    .Append(",\"GeometrieLimitee\":").Append(measure.GeometryLimited ? "true" : "false")
                    .Append(",\"EvaluationsSubdivision\":").Append(measure.TessellationChecks)
                    .Append(",\"VerificationsResolution\":").Append(measure.ResolutionChecks)
                    .Append(",\"ResolutionChangee\":").Append(measure.ResolutionChanged ? "true" : "false")
                    .Append(",\"EchantillonsChronometres\":").Append(measure.TimedSamples)
                    .Append(",\"MedianeMs\":").Append(measure.TimedSamples > 0 ? Number(measure.MedianMs) : "null")
                    .Append(",\"P95Ms\":").Append(measure.TimedSamples > 0 ? Number(measure.P95Ms) : "null")
                    .Append(",\"MaximumMs\":").Append(measure.TimedSamples > 0 ? Number(measure.MaximumMs) : "null")
                    .Append('}').Append(i + 1 == measures.Count ? "\n" : ",\n");
            }
            json.Append("  ],\n  \"PNG\": [");
            for (int i = 0; i < pictures.Count; i++) { if (i > 0) json.Append(','); json.Append(Quote(pictures[i])); }
            json.Append("]\n}\n");
            File.WriteAllText(Path.Combine(directory, "Resultat-rendu-trainee.json"), json.ToString(), new UTF8Encoding(true));
        }
    }
}
