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
    internal enum TickWork { DiagnosticWindows, DiagnosticQueue, Preparation, Annotations, Recovery }

    internal static class TrailMetrics
    {
        private sealed class Sample
        {
            internal double Time, Interval, RawInterval, Render, InputAge;
            internal int Points, Width, Height;
            internal bool Moving, Studio, Published;
            internal bool Ribbon, RibbonFallback, RibbonDegraded;
            internal double RasterScale;
            internal int RibbonPoints, RasterWidth, RasterHeight, RasterPasses;
            internal long PixelVisits, TotalPixelVisits;
            internal double GeometryError;
            internal bool GeometryLimited, ResolutionChanged;
            internal int TessellationChecks, PreflightChecks;
            internal string Style;
            internal RenderStageTiming Stages;
            internal RibbonStageTiming RibbonStages;
            internal long CompositePixels, BitmapCopiedPixels;
            internal int BitmapCopyCalls;
            internal bool ConfigRecorded, Performance, SpeedResponsive, Ring, Focus, Loupe, Clicks;
            internal int FrameRate, TrailLife, TrailWidth, TrailSmoothness, TrailOpacity;
        }
        private sealed class TickSample
        {
            internal double Time, UiWait, Total, Drain, Render, InputAge;
            internal bool Rendered, Published;
            internal double DiagnosticWindows, DiagnosticQueue, Preparation, Annotations, Recovery, Unattributed;
            internal bool DiagnosticSample, AnnotationsRendered;
        }
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static readonly List<Sample> samples = new List<Sample>(18000);
        private static readonly List<TickSample> ticks = new List<TickSample>(18000);
        private static double previous = -1;
        private static bool previousMoving;
        private static long dropped;
        private static long inputBatches, inputSamples, inputDropped, inputBreaks;
        private static int inputBatchMaximum;
        private static object initial;
        private static double uiWait = -1, tickDrain, tickRender, tickInputAge = -1;
        private static bool tickRendered, tickPublished;
        private static double tickDiagnosticWindows, tickDiagnosticQueue, tickPreparation, tickAnnotations, tickRecovery;
        private static bool tickDiagnosticSample, tickAnnotationsRendered;
        private static bool framePumpStopped;
        private static DiagnosticWriterSnapshot recoveryJournal = new DiagnosticWriterSnapshot();
        private static bool recoveryLogCapReached;
        private static long ticksDropped, settingsRequested, settingsCompleted, settingsCoalesced, settingsFailed;
        private static DateTime latestInput = DateTime.MinValue, lastPublishedInput = DateTime.MinValue;
        internal static void Configure(Config c)
        {
            if (!TrailTrial.Active) return;
            initial = new { c.Enabled, c.Trail, c.TrailStyle, c.TrailLife, c.TrailSpacing, c.TrailWidth,
                c.TrailDensity, c.TrailSpread, c.TrailSmoothness, c.TrailOpacity, c.TrailSpeedResponsive,
                c.TrailColorMode, c.TrailLogoSize, LogoPresent = !String.IsNullOrEmpty(c.TrailLogoPath),
                c.FrameRate, c.PerformanceMode, c.Ring, c.Loupe, c.Clicks, c.Focus, c.KeyDisplay };
        }
        internal static long BeginFrame() { return Stopwatch.GetTimestamp(); }
        // Stage clocks and storage are opt-in; never write a file while animating.
        internal static long BeginStage() { return TrailTrial.Active ? Stopwatch.GetTimestamp() : 0; }
        internal static void Stage(TickWork work, long start)
        {
            if (!TrailTrial.Active) return;
            double elapsed = FrameTiming.Milliseconds(start, Stopwatch.GetTimestamp(), Stopwatch.Frequency);
            switch (work)
            {
                case TickWork.DiagnosticWindows: tickDiagnosticWindows = elapsed; tickDiagnosticSample = true; break;
                case TickWork.DiagnosticQueue: tickDiagnosticQueue = elapsed; break;
                case TickWork.Preparation: tickPreparation = elapsed; break;
                case TickWork.Annotations: tickAnnotations = elapsed; tickAnnotationsRendered = true; break;
                case TickWork.Recovery: tickRecovery = elapsed; break;
            }
        }
        internal static void Dispatch(long queued, long dispatched)
        { if (TrailTrial.Active) uiWait = FrameTiming.Milliseconds(queued, dispatched, Stopwatch.Frequency); }
        internal static long BeginTick()
        {
            tickDrain = tickRender = 0; tickInputAge = -1; tickRendered = tickPublished = false;
            tickDiagnosticWindows = tickDiagnosticQueue = tickPreparation = tickAnnotations = tickRecovery = 0;
            tickDiagnosticSample = tickAnnotationsRendered = false;
            return Stopwatch.GetTimestamp();
        }
        internal static void Drained(long start, long end)
        { if (TrailTrial.Active) tickDrain = FrameTiming.Milliseconds(start, end, Stopwatch.Frequency); }
        internal static void InputAccepted(DateTime time)
        { if (TrailTrial.Active && time > latestInput) latestInput = time; }
        internal static void InputReset() { latestInput = lastPublishedInput = DateTime.MinValue; }
        internal static void CompleteTick(long start, long end)
        {
            if (!TrailTrial.Active) return;
            if (ticks.Count >= 120000) { ticksDropped++; return; }
            double total = FrameTiming.Milliseconds(start, end, Stopwatch.Frequency);
            ticks.Add(new TickSample { Time = clock.Elapsed.TotalMilliseconds, UiWait = uiWait,
                Total = total, Drain = tickDrain,
                Render = tickRender, InputAge = tickInputAge, Rendered = tickRendered, Published = tickPublished,
                DiagnosticWindows = tickDiagnosticWindows, DiagnosticQueue = tickDiagnosticQueue,
                Preparation = tickPreparation, Annotations = tickAnnotations, Recovery = tickRecovery,
                DiagnosticSample = tickDiagnosticSample, AnnotationsRendered = tickAnnotationsRendered,
                Unattributed = total - tickDrain - tickRender - tickDiagnosticWindows - tickDiagnosticQueue -
                    tickPreparation - tickAnnotations - tickRecovery });
        }
        internal static void SettingsWrites(long requested, long completed, long coalesced, long failed)
        { settingsRequested = requested; settingsCompleted = completed; settingsCoalesced = coalesced; settingsFailed = failed; }
        internal static void PumpStopped(bool stopped) { framePumpStopped = stopped; }
        internal static void RecoveryJournal(DiagnosticWriterSnapshot state, bool capReached)
        { recoveryJournal = state; recoveryLogCapReached = capReached; }
        internal static void RecordInputBatch(int count, long lost, bool discontinuity)
        {
            if (!TrailTrial.Active) return;
            inputBatches++; inputSamples += count; inputDropped += lost;
            if (discontinuity) inputBreaks++;
            inputBatchMaximum = Math.Max(inputBatchMaximum, count);
        }
        internal static void Record(long start, int points, int width, int height, bool moving, bool studio, string style, bool published,
            TrailRibbon ribbon = null, RenderStageTiming stages = default(RenderStageTiming), Config config = null)
        {
            if (!TrailTrial.Active) return;
            double end = clock.Elapsed.TotalMilliseconds;
            double rawInterval = previous < 0 ? -1 : end - previous;
            double interval = previous < 0 || !moving || !previousMoving ? -1 : end - previous;
            previous = end; previousMoving = moving;
            tickRendered = true; tickPublished = published;
            tickRender = FrameTiming.Milliseconds(start, Stopwatch.GetTimestamp(), Stopwatch.Frequency);
            if (published && latestInput != DateTime.MinValue && latestInput != lastPublishedInput)
            {
                tickInputAge = FrameTiming.InputAge(latestInput, TrailClock.Now);
                lastPublishedInput = latestInput;
            }
            if (samples.Count >= 120000) { dropped++; return; }
            samples.Add(new Sample { Time = end, Interval = interval, RawInterval = rawInterval,
                Render = tickRender, InputAge = tickInputAge,
                Points = points, Width = width, Height = height, Moving = moving, Studio = studio,
                Style = style, Published = published,
                Ribbon = ribbon != null, RibbonFallback = ribbon != null && ribbon.LastFallbackPath,
                RibbonDegraded = ribbon != null && ribbon.LastDegraded,
                RasterScale = ribbon == null ? 0 : ribbon.LastScale,
                RibbonPoints = ribbon == null ? 0 : ribbon.LastPointCount,
                RasterWidth = ribbon == null ? 0 : ribbon.LastRasterWidth,
                RasterHeight = ribbon == null ? 0 : ribbon.LastRasterHeight,
                RasterPasses = ribbon == null ? 0 : ribbon.LastRasterPasses,
                PixelVisits = ribbon == null ? 0 : ribbon.LastPixelVisits,
                TotalPixelVisits = ribbon == null ? 0 : ribbon.LastTotalPixelVisits,
                GeometryError = ribbon == null ? 0 : ribbon.LastGeometryError,
                GeometryLimited = ribbon != null && ribbon.LastGeometryLimited,
                ResolutionChanged = ribbon != null && ribbon.LastResolutionChanged,
                TessellationChecks = ribbon == null ? 0 : ribbon.LastTessellationChecks,
                PreflightChecks = ribbon == null ? 0 : ribbon.LastPreflightChecks,
                Stages = stages, RibbonStages = ribbon == null ? default(RibbonStageTiming) : ribbon.LastStageTiming,
                CompositePixels = ribbon == null ? 0 : ribbon.LastCompositePixels,
                BitmapCopiedPixels = ribbon == null ? 0 : ribbon.LastBitmapCopiedPixels,
                BitmapCopyCalls = ribbon == null ? 0 : ribbon.LastBitmapCopyCalls,
                ConfigRecorded = config != null, Performance = config != null && config.PerformanceMode,
                SpeedResponsive = config != null && config.TrailSpeedResponsive,
                Ring = config != null && config.Ring, Focus = config != null && config.Focus,
                Loupe = config != null && config.Loupe, Clicks = config != null && config.Clicks,
                FrameRate = config == null ? 0 : config.FrameRate, TrailLife = config == null ? 0 : config.TrailLife,
                TrailWidth = config == null ? 0 : config.TrailWidth,
                TrailSmoothness = config == null ? 0 : config.TrailSmoothness,
                TrailOpacity = config == null ? 0 : config.TrailOpacity });
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
        private static string Ms(double value) { return value < 0 ? "" : value.ToString("F3", CultureInfo.InvariantCulture); }
        internal static void Save(string timerMode, long coalesced, int surfaces, long fallbacks)
        {
            if (!TrailTrial.Active) return;
            string file = Path.Combine(TrailTrial.DirectoryPath, "MESURES_FLUIDITE.json");
            var groups = samples.GroupBy(s => (s.Studio ? "STUDIO_VISIBLE" : "STUDIO_MASQUE") + " / " + s.Style).Select(g => new
            { Groupe = g.Key, Images = g.Count(), IntervallesPendantMouvement = Summary(g.Where(s => s.Interval >= 0).Select(s => s.Interval)),
                TempsRendu = Summary(g.Select(s => s.Render)), PublicationsEchouees = g.Count(s => !s.Published) }).ToArray();
            var ribbonFrames = samples.Where(s => s.Ribbon).ToArray();
            var rasterFrames = ribbonFrames.Where(s => !s.RibbonFallback && s.RasterWidth > 0).ToArray();
            var measuredFrames = samples.Where(s => s.Stages.Active).ToArray();
            var measuredRibbonFrames = ribbonFrames.Where(s => s.RibbonStages.Active).ToArray();
            DiagnosticWriterSnapshot diagnostic = EffectDiagnostics.GetWriterSnapshot();
            var report = new
            {
                SchemaVersion = 2, Nature = "MESURES_LOCALES_RENDUS_ET_PAS_FPS_ECRAN", Version = Brand.Version,
                ParametresInitiaux = initial, Timer = timerMode, CallbacksCoalesces = coalesced, SurfacesCreees = surfaces, ImagesRepliGDI = fallbacks,
                ImagesEnregistrees = samples.Count, ImagesNonEnregistreesLimite = dropped,
                EntreesTrainee = new { Lots = inputBatches, PositionsHorodatees = inputSamples,
                    TailleMaximumLot = inputBatchMaximum, PositionsEcarteesConnues = inputDropped,
                    LotsAvecRupture = inputBreaks, CapaciteLot = PointerTrailBatch.Capacity,
                    IntervalleCibleMs = PointerTrailBatch.SampleIntervalTicks / (double)TimeSpan.TicksPerMillisecond },
                RenduContinu = new { Images = ribbonFrames.Length,
                    ImagesSecoursChemin = ribbonFrames.Count(s => s.RibbonFallback),
                    ImagesRasterReduit = rasterFrames.Count(s => s.RasterScale > 1.0001),
                    EchelleRasterMaximum = rasterFrames.Length == 0 ? (double?)null : rasterFrames.Max(s => s.RasterScale),
                    PointsMaximum = ribbonFrames.Length == 0 ? (int?)null : ribbonFrames.Max(s => s.RibbonPoints),
                    VisitesPixelsGereesMaximum = ribbonFrames.Length == 0 ? (long?)null : ribbonFrames.Max(s => s.TotalPixelVisits),
                    PassesRasterMaximum = ribbonFrames.Length == 0 ? (int?)null : ribbonFrames.Max(s => s.RasterPasses),
                    ErreurGeometriqueEstimeeMaximumPx = ribbonFrames.Length == 0 ? (double?)null : ribbonFrames.Max(s => s.GeometryError),
                    ImagesToleranceGeometriqueLimitee = ribbonFrames.Count(s => s.GeometryLimited),
                    ChangementsResolution = ribbonFrames.Count(s => s.ResolutionChanged),
                    ControlesTessellationMaximum = ribbonFrames.Length == 0 ? (int?)null : ribbonFrames.Max(s => s.TessellationChecks),
                    ControlesResolutionMaximum = ribbonFrames.Length == 0 ? (int?)null : ribbonFrames.Max(s => s.PreflightChecks),
                    PixelsComposes = rasterFrames.Sum(s => s.CompositePixels),
                    PixelsCopiesVersBitmap = rasterFrames.Sum(s => s.BitmapCopiedPixels),
                    AppelsCopieBitmap = rasterFrames.Sum(s => (long)s.BitmapCopyCalls),
                    Limite = "Le secours simplifie les attributs du trait. Les visites de pixels ne mesurent pas le travail natif GDI+ ni les FPS ecran." },
                EtapesRendu = new { Images = measuredFrames.Length,
                    PreparationEtBornes = Summary(measuredFrames.Select(s => s.Stages.PrepareMs)),
                    Surface = Summary(measuredFrames.Select(s => s.Stages.SurfaceMs)),
                    EffacementEtContexte = Summary(measuredFrames.Select(s => s.Stages.ClearMs)),
                    Trainee = Summary(measuredFrames.Select(s => s.Stages.TrailMs)),
                    AutresEffets = Summary(measuredFrames.Select(s => s.Stages.OtherEffectsMs)),
                    SynchronisationGDI = Summary(measuredFrames.Select(s => s.Stages.FlushMs)),
                    PublicationCalque = Summary(measuredFrames.Select(s => s.Stages.PublishMs)),
                    TotalInterne = Summary(measuredFrames.Select(s => s.Stages.TotalMs)),
                    Limite = "Temps ecoules locaux, pas du CPU ni des FPS ecran. Les etapes internes du ruban sont incluses dans Trainee, pas a additionner une seconde fois." },
                EtapesRuban = new { Images = measuredRibbonFrames.Length,
                    Geometrie = Summary(measuredRibbonFrames.Select(s => s.RibbonStages.GeometryMs)),
                    Resolution = Summary(measuredRibbonFrames.Select(s => s.RibbonStages.ResolutionMs)),
                    Tampons = Summary(measuredRibbonFrames.Select(s => s.RibbonStages.BuffersMs)),
                    Rasterisation = Summary(measuredRibbonFrames.Select(s => s.RibbonStages.RasterMs)),
                    Composition = Summary(measuredRibbonFrames.Select(s => s.RibbonStages.CompositeMs)),
                    CopieBitmap = Summary(measuredRibbonFrames.Select(s => s.RibbonStages.UploadMs)),
                    DessinBitmap = Summary(measuredRibbonFrames.Select(s => s.RibbonStages.BlitMs)),
                    Secours = Summary(measuredRibbonFrames.Select(s => s.RibbonStages.FallbackMs)),
                    TotalInterne = Summary(measuredRibbonFrames.Select(s => s.RibbonStages.TotalMs)) },
                CadenceUI = new { Ticks = ticks.Count, TicksNonEnregistresLimite = ticksDropped,
                    AttenteAvantDispatchUI = Summary(ticks.Select(s => s.UiWait)),
                    TickComplet = Summary(ticks.Select(s => s.Total)),
                    VidangeEntrees = Summary(ticks.Select(s => s.Drain)),
                    InstantanesDiagnostic = ticks.Count(s => s.DiagnosticSample),
                    InspectionFenetresDiagnostic = Summary(ticks.Where(s => s.DiagnosticSample).Select(s => s.DiagnosticWindows)),
                    MiseEnFileDiagnostic = Summary(ticks.Where(s => s.DiagnosticSample).Select(s => s.DiagnosticQueue)),
                    PreparationAvantRendu = Summary(ticks.Select(s => s.Preparation)),
                    RenduAnnotations = Summary(ticks.Where(s => s.AnnotationsRendered).Select(s => s.Annotations)),
                    RecuperationCalque = Summary(ticks.Select(s => s.Recovery)),
                    HorsEtapesMesurees = Summary(ticks.Select(s => s.Unattributed)),
                    TicksSommeEtapesSuperieureTotal = ticks.Count(s => s.Unattributed < -.001),
                    AgeDerniereEntreeNouvelleAuRetourPublication = Summary(ticks.Select(s => s.InputAge)),
                    Limite = "Attente du callback et travail UI local, pas une mesure de presentation ecran. L'age utilise la date conservee du dernier point d'un lot nouvellement publie ; ce n'est pas la latence physique du capteur ni celle de tous les points." },
                DiagnosticArrierePlan = new { Actif = diagnostic.Started,
                    Complet = diagnostic.JournalComplete && recoveryJournal.JournalComplete && framePumpStopped,
                    Demandes = diagnostic.Requested, Acceptees = diagnostic.Accepted, Ecrites = diagnostic.Written,
                    Ecartees = diagnostic.Dropped, Echecs = diagnostic.Failed,
                    EcarteesFilePleine = diagnostic.OverflowDropped, EcarteesVerrouOccupe = diagnostic.ContentionDropped,
                    EcarteesLigneTropLongue = diagnostic.OversizedDropped, RejeteesApresFermeture = diagnostic.RejectedAfterClose,
                    ErreursEcriture = diagnostic.WriteFailures,
                    EnAttente = diagnostic.Pending, EnCours = diagnostic.InFlight,
                    Capacite = diagnostic.QueueCapacity, MaximumEnAttente = diagnostic.MaximumPending,
                    WorkerTermine = diagnostic.Completed, FinEcrite = diagnostic.FooterWritten,
                    ProducteurCadenceArrete = framePumpStopped,
                    DelaiFermetureDepasse = diagnostic.TimedOut, CodeErreur = diagnostic.ErrorCode,
                    Limite = "Complet exige les deux journaux termines sans perte ni erreur et l'arret du producteur de cadence. Compteurs de ce bloc = journal d'effets seul, hors pied technique ; recuperation detaillee dans JournalRecuperation." },
                JournalRecuperation = new { Actif = recoveryJournal.Started, Complet = recoveryJournal.JournalComplete,
                    Demandes = recoveryJournal.Requested, Acceptees = recoveryJournal.Accepted, Ecrites = recoveryJournal.Written,
                    Ecartees = recoveryJournal.Dropped, Echecs = recoveryJournal.Failed,
                    EcarteesFilePleine = recoveryJournal.OverflowDropped, EcarteesVerrouOccupe = recoveryJournal.ContentionDropped,
                    RejeteesApresFermeture = recoveryJournal.RejectedAfterClose,
                    ErreursEcriture = recoveryJournal.WriteFailures, EnAttente = recoveryJournal.Pending, EnCours = recoveryJournal.InFlight,
                    Capacite = recoveryJournal.QueueCapacity, MaximumEnAttente = recoveryJournal.MaximumPending,
                    WorkerTermine = recoveryJournal.Completed, DelaiFermetureDepasse = recoveryJournal.TimedOut,
                    PlafondAtteint = recoveryLogCapReached, PlafondOctets = OverlayRecoveryLog.MaximumBytes,
                    CodeErreur = recoveryJournal.ErrorCode },
                SauvegardesArrierePlan = new { Demandes = settingsRequested, EcrituresTerminees = settingsCompleted,
                    DemandesRegroupees = settingsCoalesced, Echecs = settingsFailed,
                    Limite = "Compteurs du worker de snapshots uniquement ; sauvegarde finale et imports explicites exclus." },
                IntervallesPendantMouvement = Summary(samples.Where(s => s.Interval >= 0).Select(s => s.Interval)),
                TempsRendu = Summary(samples.Select(s => s.Render)), IntervallesTousReposInclus = Summary(samples.Select(s => s.RawInterval)), Groupes = groups,
                PointsMax = samples.Count == 0 ? 0 : samples.Max(s => s.Points),
                PixelsMax = samples.Count == 0 ? 0L : samples.Max(s => (long)s.Width * s.Height),
                PublicationsEchouees = samples.Count(s => !s.Published),
                MemoireProcessusFinOctets = Process.GetCurrentProcess().WorkingSet64,
                Limites = "Pas une mesure de présentation écran ni du coût CPU isolé. Intervalles exclus après repos. Comparer mêmes effets, réglages et durée ; les réglages principaux sont aussi capturés par image dans images.csv. État Studio = visible/non réduit, pas détection d'occlusion. TickComplet inclut les étapes du callback, même sans dessin ; son dernier tick de fermeture peut être absent du fichier sauvegardé pendant cette fermeture. Les nouvelles horloges ajoutent un petit coût de mesure non soustrait.",
                Confidentialite = "Pas de contenu écran, texte, frappe, coordonnées ni chemin de logo dans ces mesures.",
                DateUTC = DateTime.UtcNow.ToString("o")
            };
            File.WriteAllText(file, new JavaScriptSerializer().Serialize(report), Encoding.UTF8);
            using (StreamWriter w = new StreamWriter(Path.Combine(TrailTrial.DirectoryPath, "images.csv"), false, Encoding.UTF8))
            {
                w.WriteLine("temps_ms;intervalle_mouvement_ms;intervalle_tout_ms;rendu_ms;points;largeur;hauteur;mouvement;studio_visible;publication_ok;style;ruban;secours_chemin;echelle_raster;points_ruban;visites_pixels;visites_pixels_total;passes_raster;age_entree_nouvelle_ms;erreur_geometrie_estimee_px;geometrie_limitee;changement_resolution;controles_geometrie;controles_resolution;preparation_bornes_ms;surface_ms;effacement_contexte_ms;trainee_ms;autres_effets_ms;synchronisation_gdi_ms;publication_calque_ms;rendu_interne_ms;ruban_geometrie_ms;ruban_resolution_ms;ruban_tampons_ms;ruban_raster_ms;ruban_composition_ms;ruban_copie_bitmap_ms;ruban_dessin_bitmap_ms;ruban_secours_ms;ruban_total_ms;pixels_composes;pixels_copies_bitmap;appels_copie_bitmap;cible_images_s;mode_performance;duree_trainee_ms;largeur_trainee;lissage_pct;opacite;epaisseur_reactive;halo;focus;loupe;clics");
                foreach (Sample s in samples) w.WriteLine(String.Join(";", new string[] { s.Time.ToString("F3", CultureInfo.InvariantCulture),
                    s.Interval.ToString("F3", CultureInfo.InvariantCulture), s.RawInterval.ToString("F3", CultureInfo.InvariantCulture), s.Render.ToString("F3", CultureInfo.InvariantCulture),
                    s.Points.ToString(), s.Width.ToString(), s.Height.ToString(), s.Moving ? "1" : "0", s.Studio ? "1" : "0", s.Published ? "1" : "0", s.Style,
                    s.Ribbon ? "1" : "0", s.Ribbon ? (s.RibbonFallback ? "1" : "0") : "",
                    s.Ribbon && !s.RibbonFallback && s.RasterWidth > 0 ? s.RasterScale.ToString("F3", CultureInfo.InvariantCulture) : "",
                    s.Ribbon ? s.RibbonPoints.ToString() : "", s.Ribbon ? s.PixelVisits.ToString() : "",
                    s.Ribbon ? s.TotalPixelVisits.ToString() : "", s.Ribbon ? s.RasterPasses.ToString() : "",
                    s.InputAge < 0 ? "" : s.InputAge.ToString("F3", CultureInfo.InvariantCulture),
                    s.Ribbon ? s.GeometryError.ToString("F4", CultureInfo.InvariantCulture) : "",
                    s.Ribbon ? (s.GeometryLimited ? "1" : "0") : "", s.Ribbon ? (s.ResolutionChanged ? "1" : "0") : "",
                    s.Ribbon ? s.TessellationChecks.ToString() : "", s.Ribbon ? s.PreflightChecks.ToString() : "",
                    s.Stages.Active ? Ms(s.Stages.PrepareMs) : "", s.Stages.Active ? Ms(s.Stages.SurfaceMs) : "",
                    s.Stages.Active ? Ms(s.Stages.ClearMs) : "", s.Stages.Active ? Ms(s.Stages.TrailMs) : "",
                    s.Stages.Active ? Ms(s.Stages.OtherEffectsMs) : "", s.Stages.Active ? Ms(s.Stages.FlushMs) : "",
                    s.Stages.Active ? Ms(s.Stages.PublishMs) : "", s.Stages.Active ? Ms(s.Stages.TotalMs) : "",
                    s.RibbonStages.Active ? Ms(s.RibbonStages.GeometryMs) : "", s.RibbonStages.Active ? Ms(s.RibbonStages.ResolutionMs) : "",
                    s.RibbonStages.Active ? Ms(s.RibbonStages.BuffersMs) : "", s.RibbonStages.Active ? Ms(s.RibbonStages.RasterMs) : "",
                    s.RibbonStages.Active ? Ms(s.RibbonStages.CompositeMs) : "", s.RibbonStages.Active ? Ms(s.RibbonStages.UploadMs) : "",
                    s.RibbonStages.Active ? Ms(s.RibbonStages.BlitMs) : "", s.RibbonStages.Active ? Ms(s.RibbonStages.FallbackMs) : "",
                    s.RibbonStages.Active ? Ms(s.RibbonStages.TotalMs) : "",
                    s.Ribbon ? s.CompositePixels.ToString() : "", s.Ribbon ? s.BitmapCopiedPixels.ToString() : "",
                    s.Ribbon ? s.BitmapCopyCalls.ToString() : "",
                    s.ConfigRecorded ? s.FrameRate.ToString() : "", s.ConfigRecorded ? (s.Performance ? "1" : "0") : "",
                    s.ConfigRecorded ? s.TrailLife.ToString() : "", s.ConfigRecorded ? s.TrailWidth.ToString() : "",
                    s.ConfigRecorded ? s.TrailSmoothness.ToString() : "", s.ConfigRecorded ? s.TrailOpacity.ToString() : "",
                    s.ConfigRecorded ? (s.SpeedResponsive ? "1" : "0") : "", s.ConfigRecorded ? (s.Ring ? "1" : "0") : "",
                    s.ConfigRecorded ? (s.Focus ? "1" : "0") : "", s.ConfigRecorded ? (s.Loupe ? "1" : "0") : "",
                    s.ConfigRecorded ? (s.Clicks ? "1" : "0") : "" }));
            }
            using (StreamWriter w = new StreamWriter(Path.Combine(TrailTrial.DirectoryPath, "ticks.csv"), false, Encoding.UTF8))
            {
                w.WriteLine("temps_ms;attente_ui_ms;tick_complet_ms;vidange_entrees_ms;rendu_ms;dessin_execute;publication_ok;age_entree_nouvelle_ms;instantane_diagnostic;inspection_fenetres_ms;mise_en_file_diagnostic_ms;preparation_ms;annotations_executees;annotations_ms;recuperation_ms;hors_etapes_ms");
                foreach (TickSample tick in ticks) w.WriteLine(String.Join(";", new string[] {
                    tick.Time.ToString("F3", CultureInfo.InvariantCulture),
                    tick.UiWait < 0 ? "" : tick.UiWait.ToString("F3", CultureInfo.InvariantCulture),
                    tick.Total < 0 ? "" : tick.Total.ToString("F3", CultureInfo.InvariantCulture),
                    tick.Drain < 0 ? "" : tick.Drain.ToString("F3", CultureInfo.InvariantCulture),
                    tick.Render.ToString("F3", CultureInfo.InvariantCulture), tick.Rendered ? "1" : "0",
                    tick.Published ? "1" : "0", tick.InputAge < 0 ? "" : tick.InputAge.ToString("F3", CultureInfo.InvariantCulture),
                    tick.DiagnosticSample ? "1" : "0", tick.DiagnosticSample ? Ms(tick.DiagnosticWindows) : "",
                    tick.DiagnosticSample ? Ms(tick.DiagnosticQueue) : "", Ms(tick.Preparation),
                    tick.AnnotationsRendered ? "1" : "0", tick.AnnotationsRendered ? Ms(tick.Annotations) : "",
                    Ms(tick.Recovery), tick.Unattributed.ToString("F3", CultureInfo.InvariantCulture) }));
            }
        }
    }
}
