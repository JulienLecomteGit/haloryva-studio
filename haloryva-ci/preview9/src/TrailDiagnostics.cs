using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CursorFlow
{
    // Une session d'essai copie les préférences dans un dossier neuf, jamais dans l'installation.
    // Pas de collecte de texte, touches, trajectoires, captures ni envoi réseau dans les mesures.
    internal static class TrailTrial
    {
        internal static string DirectoryPath { get; private set; }
        internal static bool Active { get { return DirectoryPath != null; } }
        internal static void Initialize(string[] args)
        {
            int index = Array.IndexOf(args, "--trail-session");
            if (index < 0) return;
            if (index + 1 >= args.Length || !Path.IsPathRooted(args[index + 1])) throw new ArgumentException("Dossier de session absolu attendu.");
            string path = Path.GetFullPath(args[index + 1]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string installed = ProfilePaths.NormalDirectory;
            foreach (string root in ProfilePaths.ProtectedRoots)
                if (ProfilePaths.Overlaps(path, root)) throw new ArgumentException("Dossier de session adjacent aux profils : refus.");
            if (path.Length < 10 || Directory.Exists(path) || File.Exists(path) ||
                path.StartsWith(installed, StringComparison.OrdinalIgnoreCase) ||
                installed.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choisis un dossier NEUF, distinct de l'installation, pour cet essai.");
            Directory.CreateDirectory(path);
            // Copie uniquement le JSON lisible, sans déplacer ni réparer la configuration réelle.
            string settings = Path.Combine(installed, "settings.json");
            if (File.Exists(settings))
            {
                string json = File.ReadAllText(settings);
                new JavaScriptSerializer().Deserialize<Config>(json); // parse seulement, pas de sauvegarde
                File.WriteAllText(Path.Combine(path, "settings.json"), json, Encoding.UTF8);
            }
            DirectoryPath = path;
        }
    }
    internal static class TrailMetrics
    {
        private sealed class Sample
        {
            internal double Time, Interval, RawInterval, Render;
            internal int Points, Width, Height;
            internal bool Moving, Studio, Published;
            internal string Style;
        }
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static readonly List<Sample> samples = new List<Sample>(18000);
        private static double previous = -1;
        private static bool previousMoving;
        private static long dropped;
        private static object initial;
        internal static void Configure(Config c)
        {
            if (!TrailTrial.Active) return;
            initial = new { c.Enabled, c.Trail, c.TrailStyle, c.TrailLife, c.TrailSpacing, c.TrailWidth,
                c.TrailDensity, c.TrailSpread, c.TrailSmoothness, c.TrailOpacity, c.TrailSpeedResponsive,
                c.TrailColorMode, c.TrailLogoSize, LogoPresent = !String.IsNullOrEmpty(c.TrailLogoPath),
                c.FrameRate, c.PerformanceMode, c.Ring, c.Loupe, c.Clicks, c.Focus, c.KeyDisplay };
        }
        internal static long BeginFrame() { return Stopwatch.GetTimestamp(); }
        internal static void Record(long start, int points, int width, int height, bool moving, bool studio, string style, bool published)
        {
            if (!TrailTrial.Active) return;
            double end = clock.Elapsed.TotalMilliseconds;
            double rawInterval = previous < 0 ? -1 : end - previous;
            double interval = previous < 0 || !moving || !previousMoving ? -1 : end - previous;
            previous = end; previousMoving = moving;
            if (samples.Count >= 120000) { dropped++; return; }
            samples.Add(new Sample { Time = end, Interval = interval, RawInterval = rawInterval,
                Render = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency,
                Points = points, Width = width, Height = height, Moving = moving, Studio = studio,
                Style = style, Published = published });
        }
        private static object Summary(IEnumerable<double> values)
        {
            double[] a = values.Where(x => x >= 0 && !double.IsNaN(x) && !double.IsInfinity(x)).OrderBy(x => x).ToArray();
            if (a.Length == 0) return new { Nombre = 0 };
            return new { Nombre = a.Length, MoyenneMs = Math.Round(a.Average(), 3),
                MedianeMs = Math.Round(a[(a.Length - 1) / 2], 3),
                P95Ms = Math.Round(a[(int)Math.Ceiling(.95 * a.Length) - 1], 3),
                P99Ms = Math.Round(a[(int)Math.Ceiling(.99 * a.Length) - 1], 3), MaximumMs = Math.Round(a[a.Length - 1], 3) };
        }
        internal static void Save(string timerMode, long coalesced, int surfaces, long fallbacks)
        {
            if (!TrailTrial.Active) return;
            string file = Path.Combine(TrailTrial.DirectoryPath, "MESURES_FLUIDITE.json");
            var groups = samples.GroupBy(s => (s.Studio ? "STUDIO_VISIBLE" : "STUDIO_MASQUE") + " / " + s.Style).Select(g => new
            { Groupe = g.Key, Images = g.Count(), IntervallesPendantMouvement = Summary(g.Where(s => s.Interval >= 0).Select(s => s.Interval)),
                TempsRendu = Summary(g.Select(s => s.Render)), PublicationsEchouees = g.Count(s => !s.Published) }).ToArray();
            var report = new
            {
                Nature = "MESURES_LOCALES_RENDUS_ET_PAS_FPS_ECRAN", Version = Brand.Version,
                ParametresInitiaux = initial, Timer = timerMode, CallbacksCoalesces = coalesced, SurfacesCreees = surfaces, ImagesRepliGDI = fallbacks,
                ImagesEnregistrees = samples.Count, ImagesNonEnregistreesLimite = dropped,
                IntervallesPendantMouvement = Summary(samples.Where(s => s.Interval >= 0).Select(s => s.Interval)),
                TempsRendu = Summary(samples.Select(s => s.Render)), IntervallesTousReposInclus = Summary(samples.Select(s => s.RawInterval)), Groupes = groups,
                PointsMax = samples.Count == 0 ? 0 : samples.Max(s => s.Points),
                PixelsMax = samples.Count == 0 ? 0L : samples.Max(s => (long)s.Width * s.Height),
                PublicationsEchouees = samples.Count(s => !s.Published),
                MemoireProcessusFinOctets = Process.GetCurrentProcess().WorkingSet64,
                Limites = "Pas une mesure de présentation écran ni du coût CPU isolé. Intervalles exclus après repos. Comparer mêmes effets et durée. État Studio = visible/non réduit, pas détection d'occlusion.",
                Confidentialite = "Pas de contenu écran, texte, frappe, coordonnées ni chemin de logo dans ces mesures.",
                DateUTC = DateTime.UtcNow.ToString("o")
            };
            File.WriteAllText(file, new JavaScriptSerializer().Serialize(report), Encoding.UTF8);
            using (StreamWriter w = new StreamWriter(Path.Combine(TrailTrial.DirectoryPath, "images.csv"), false, Encoding.UTF8))
            {
                w.WriteLine("temps_ms;intervalle_mouvement_ms;intervalle_tout_ms;rendu_ms;points;largeur;hauteur;mouvement;studio_visible;publication_ok;style");
                foreach (Sample s in samples) w.WriteLine(String.Join(";", new string[] { s.Time.ToString("F3", CultureInfo.InvariantCulture),
                    s.Interval.ToString("F3", CultureInfo.InvariantCulture), s.RawInterval.ToString("F3", CultureInfo.InvariantCulture), s.Render.ToString("F3", CultureInfo.InvariantCulture),
                    s.Points.ToString(), s.Width.ToString(), s.Height.ToString(), s.Moving ? "1" : "0", s.Studio ? "1" : "0", s.Published ? "1" : "0", s.Style }));
            }
        }
    }
}
