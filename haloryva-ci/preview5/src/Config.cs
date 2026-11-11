using System;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace CursorFlow
{
    // Format JSON rétrocompatible : les anciens champs ne sont pas nécessaires.
    public sealed class Config
    {
        public bool Enabled = true;
        public string ActivePreset = "";
        public string ActivePresetSignature = "";
        public bool Ring = true;
        public int RingRadius = 30;
        public int RingThickness = 3;
        public bool RingPulse = true;
        public int RingPulseSpeed = 5;
        public bool RingNeon = true;
        public int RingGlowBlur = 24;
        public int RingGlowIntensity = 80;
        public int RingOpacity = 230;
        public string RingShape = "Cercle";
        public int RingOffsetX = 0;
        public int RingOffsetY = 0;
        public string RingColor = "#38D6FF";
        // Options désactivées par défaut pour préserver les profils existants.
        public bool RingAutoHide = false;
        public int RingIdleSeconds = 3;
        public int RingFadeMilliseconds = 350;
        public bool RingCenterOnText = false;
        public bool RingTextPrecision = false;
        // Curseur texte (I) : le halo s'agrandit pour dégager la lecture et la sélection ; désactivé par défaut.
        public bool RingTextGrow = false;
        public int RingTextGrowPercent = 80;
        public int RingTextFocusIntensity = 60;
        public int RingTextFocusThinness = 40;
        // Zoom local au sein du halo uniquement sur pointeur I standard.
        // Dépend du recentrage automatique pour éviter des captures déplacées.
        public bool RingTextZoom = false;
        public int RingTextZoomPercent = 180;
        public string RingColorMode = "Unie";
        public string RingColor2 = "#AC68FF";
        public bool Trail = true;
        public string TrailStyle = "Particules";
        public int TrailWidth = 7;
        // Valeur de référence validée pour Haloryva : 269 ms (réglable dans le Studio).
        public int TrailLife = 269;
        public int TrailDensity = 3;
        public int TrailSpacing = 6;
        public int TrailSpread = 9;
        public int TrailSmoothness = 65;
        public int TrailOpacity = 225;
        public bool TrailSpeedResponsive = true;
        public string TrailColorMode = "Dégradé";
        public string TrailColor = "#2BD9F4";
        public string TrailColor2 = "#A475FF";
        public string TrailLogoPath = "";
        public int TrailLogoSize = 34;
        public int TrailLogoEvery = 2;
        public bool TrailLogoRotate = true;
        public bool Clicks = true;
        public string ClickStyle = "Ondes";
        public bool ClickOnMouse = true;
        public string LeftColor = "#49E6B2";
        public string RightColor = "#FFB46D";
        public int ClickSize = 68;
        public int ClickDuration = 720;
        public bool ClickSounds = false;
        public string CustomSound = "";
        public bool Shake = true;
        public bool KeyDisplay = false;
        public string KeyStyle = "Verre";
        public bool Loupe = false;
        public int LoupeSize = 175;
        public int LoupeMagnification = 2;
        public bool ZoomOnClick = false;
        public bool Focus = false;
        public string AnnotationTool = "Stylo";
        // Missing in older profiles: preserve the complete tool cycle by default.
        public string[] DrawingToolCycle = (string[])DrawingTools.Names.Clone();
        public string DrawCursorColor = "#20DCE7";
        public string DrawCursorStyle = "Croix + anneau";
        public int DrawCursorSize = 24;
        public int DrawCursorThickness = 2;
        public int DrawCursorOpacity = 90;
        public bool DrawCursorLinkedToPen = false;
        public bool DrawCursorHideWhileDrawing = false;
        public string DrawSmoothMode = "Formes assistées";
        public int DrawSmoothStrength = 50;
        public string AnnotationColor = "#FF5DA2";
        public int AnnotationThickness = 4;
        // "Normal" laisse le comportement natif de Windows entièrement intact.
        public string ButtonLeft = "Normal";
        public string ButtonRight = "Normal";
        public string ButtonMiddle = "Normal";
        public string ButtonX1 = "Normal";
        public string ButtonX2 = "Normal";
        // Orientation des boutons physiques : n'altère jamais les événements Windows.
        // Les actions restent stockées sous leur identité native X1 / X2.
        public bool SwapSideButtons = false;
        // Appui long : seconde action par bouton (« Normal » = pas d'appui long).
        public string ButtonLeftLong = "Normal";
        public string ButtonRightLong = "Normal";
        public string ButtonMiddleLong = "Normal";
        public string ButtonX1Long = "Normal";
        public string ButtonX2Long = "Normal";
        public int LongPressMs = 400;
        public bool LaunchAtLogin = false;
        public string StudioAccent = "Aurora";
        public int FrameRate = 60;
        public bool PerformanceMode = true;

        // Actions possibles pour un bouton (appui court ou long) ; source unique pour l'interface.
        public static readonly string[] ButtonActions = { "Normal", "Dessin : activer / arrêter",
            "Outil de dessin suivant", "Outil de dessin précédent", "Annuler le dernier trait",
            "Rétablir un trait", "Effacer les dessins", "Traînée : activer / arrêter",
            "Halo : activer / arrêter", "Effets : pause / reprise", "Effet de clic",
            "Traînée suivante", "Ouvrir les réglages" };

        public void ResetButtons()
        {
            ButtonLeft = ButtonRight = ButtonMiddle = ButtonX1 = ButtonX2 = "Normal";
            ButtonLeftLong = ButtonRightLong = ButtonMiddleLong = ButtonX1Long = ButtonX2Long = "Normal";
        }

        private static string OneOf(string value, string fallback, params string[] allowed)
        {
            return Array.IndexOf(allowed, value) >= 0 ? value : fallback;
        }

        public void Normalize()
        {
            RingRadius = Math.Max(8, Math.Min(155, RingRadius));
            RingTextGrowPercent = Math.Max(10, Math.Min(250, RingTextGrowPercent));
            RingThickness = Math.Max(1, Math.Min(16, RingThickness));
            RingPulseSpeed = Math.Max(1, Math.Min(15, RingPulseSpeed));
            RingOpacity = Math.Max(25, Math.Min(255, RingOpacity));
            RingGlowBlur = Math.Max(0, Math.Min(40, RingGlowBlur));
            RingGlowIntensity = Math.Max(0, Math.Min(100, RingGlowIntensity));
            RingOffsetX = Math.Max(-250, Math.Min(250, RingOffsetX));
            RingOffsetY = Math.Max(-250, Math.Min(250, RingOffsetY));
            RingIdleSeconds = Math.Max(1, Math.Min(15, RingIdleSeconds));
            RingFadeMilliseconds = Math.Max(100, Math.Min(1200, RingFadeMilliseconds));
            RingTextFocusIntensity = Math.Max(0, Math.Min(100, RingTextFocusIntensity));
            RingTextFocusThinness = Math.Max(0, Math.Min(80, RingTextFocusThinness));
            RingTextZoomPercent = Math.Max(125, Math.Min(300, RingTextZoomPercent));
            if (RingColorMode != "Unie" && RingColorMode != "Dégradé" && RingColorMode != "Arc-en-ciel" && RingColorMode != "Dynamique") RingColorMode = "Unie";
            if (String.IsNullOrEmpty(RingColor2)) RingColor2 = "#AC68FF";
            TrailWidth = Math.Max(1, Math.Min(40, TrailWidth));
            TrailLife = Math.Max(100, Math.Min(2800, TrailLife));
            TrailDensity = Math.Max(1, Math.Min(5, TrailDensity));
            TrailSpacing = Math.Max(2, Math.Min(40, TrailSpacing));
            TrailSpread = Math.Max(0, Math.Min(90, TrailSpread));
            TrailSmoothness = Math.Max(0, Math.Min(100, TrailSmoothness));
            TrailOpacity = Math.Max(15, Math.Min(255, TrailOpacity));
            TrailLogoSize = Math.Max(10, Math.Min(125, TrailLogoSize));
            TrailLogoEvery = Math.Max(1, Math.Min(12, TrailLogoEvery));
            ClickSize = Math.Max(15, Math.Min(180, ClickSize));
            ClickDuration = Math.Max(200, Math.Min(1800, ClickDuration));
            AnnotationThickness = Math.Max(1, Math.Min(24, AnnotationThickness));
            DrawCursorSize = Math.Max(12, Math.Min(48, DrawCursorSize));
            DrawCursorThickness = Math.Max(1, Math.Min(5, DrawCursorThickness));
            DrawCursorOpacity = Math.Max(20, Math.Min(100, DrawCursorOpacity));
            DrawSmoothStrength = Math.Max(0, Math.Min(100, DrawSmoothStrength));
            if (DrawSmoothMode != "Désactivé" && DrawSmoothMode != "Lissage léger" && DrawSmoothMode != "Formes assistées") DrawSmoothMode = "Lissage léger";
            if (DrawCursorStyle != "Croix + anneau" && DrawCursorStyle != "Croix" && DrawCursorStyle != "Anneau" && DrawCursorStyle != "Point central") DrawCursorStyle = "Croix + anneau";
            if (String.IsNullOrEmpty(DrawCursorColor)) DrawCursorColor = "#20DCE7";
            FrameRate = Math.Max(15, Math.Min(60, FrameRate));
            // Correctif : ces valeurs n'étaient pas bornées (profils importés / anciens JSON).
            LoupeSize = Math.Max(95, Math.Min(380, LoupeSize));
            StudioAccent = OneOf(StudioAccent, "Aurora", "Aurora", "Sunset", "Émeraude", "Néon", "Glacier");
            LoupeMagnification = Math.Max(2, Math.Min(5, LoupeMagnification));
            RingShape = OneOf(RingShape, "Cercle", "Cercle", "Carré arrondi", "Ellipse", "Réticule");
            TrailStyle = OneOf(TrailStyle, "Particules", "Particules", "Ligne", "Points", "Pointillés", "Lumière", "Étoiles", "Kawaii", "Logo");
            TrailColorMode = OneOf(TrailColorMode, "Dégradé", "Unie", "Dégradé", "Arc-en-ciel");
            ClickStyle = OneOf(ClickStyle, "Ondes", "Ondes", "Anneau simple", "Flash", "Étincelles");
            KeyStyle = OneOf(KeyStyle, "Verre", "Classique", "Verre", "Touches", "Néon", "Dégradé", "Minimal");
            AnnotationTool = OneOf(AnnotationTool, "Stylo", "Stylo", "Surligneur", "Ligne droite", "Rectangle", "Ellipse",
                "Flèche droite", "Flèche libre", "Forme libre", "Numéro", "Texte");
            // Stable display order, no duplicate/unknown tools, never an empty cycle.
            var cycle = new List<string>();
            if (DrawingToolCycle != null)
                foreach (string tool in DrawingTools.Names)
                    if (Array.IndexOf(DrawingToolCycle, tool) >= 0) cycle.Add(tool);
            DrawingToolCycle = cycle.Count == 0 ? (string[])DrawingTools.Names.Clone() : cycle.ToArray();
            if (Visuals.ParseColor(LeftColor, System.Drawing.Color.Empty).IsEmpty) LeftColor = "#49E6B2";
            if (Visuals.ParseColor(RightColor, System.Drawing.Color.Empty).IsEmpty) RightColor = "#FFB46D";
            if (String.IsNullOrEmpty(RingColor)) RingColor = "#38D6FF";
            if (String.IsNullOrEmpty(TrailColor)) TrailColor = "#2BD9F4";
            if (String.IsNullOrEmpty(TrailColor2)) TrailColor2 = "#A475FF";
            if (String.IsNullOrEmpty(AnnotationColor)) AnnotationColor = "#FF5DA2";
            if (String.IsNullOrEmpty(RingShape)) RingShape = "Cercle";
            if (String.IsNullOrEmpty(TrailColorMode)) TrailColorMode = "Dégradé";
            if (String.IsNullOrEmpty(TrailStyle)) TrailStyle = "Particules";
            if (String.IsNullOrEmpty(ButtonLeft)) ButtonLeft = "Normal";
            if (String.IsNullOrEmpty(ButtonRight)) ButtonRight = "Normal";
            if (String.IsNullOrEmpty(ButtonMiddle)) ButtonMiddle = "Normal";
            if (String.IsNullOrEmpty(ButtonX1)) ButtonX1 = "Normal";
            if (String.IsNullOrEmpty(ButtonX2)) ButtonX2 = "Normal";
            if (Array.IndexOf(ButtonActions, ButtonLeft) < 0) ButtonLeft = "Normal";
            if (Array.IndexOf(ButtonActions, ButtonRight) < 0) ButtonRight = "Normal";
            if (Array.IndexOf(ButtonActions, ButtonMiddle) < 0) ButtonMiddle = "Normal";
            if (Array.IndexOf(ButtonActions, ButtonX1) < 0) ButtonX1 = "Normal";
            if (Array.IndexOf(ButtonActions, ButtonX2) < 0) ButtonX2 = "Normal";
            if (Array.IndexOf(ButtonActions, ButtonLeftLong) < 0) ButtonLeftLong = "Normal";
            if (Array.IndexOf(ButtonActions, ButtonRightLong) < 0) ButtonRightLong = "Normal";
            if (Array.IndexOf(ButtonActions, ButtonMiddleLong) < 0) ButtonMiddleLong = "Normal";
            if (Array.IndexOf(ButtonActions, ButtonX1Long) < 0) ButtonX1Long = "Normal";
            if (Array.IndexOf(ButtonActions, ButtonX2Long) < 0) ButtonX2Long = "Normal";
            LongPressMs = Math.Max(250, Math.Min(1200, LongPressMs));
            // Sécurité (2.5.1) : un appui long retient le clic puis le rejoue ; interdit sur les clics principaux
            // (glisser-déposer, sélection, double-clic). Corrige aussi une configuration 2.5.0 déjà enregistrée.
            ButtonLeftLong = "Normal";
            ButtonRightLong = "Normal";
        }
    }

    public static class ConfigStore
    {
        public static string DirectoryPath
        {
            get { return TrailTrial.Active ? TrailTrial.DirectoryPath : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Haloryva"); }
        }
        public static string FilePath { get { return Path.Combine(DirectoryPath, "settings.json"); } }

        // Message à montrer une seule fois à l'utilisateur (réglages restaurés, écriture impossible…).
        private static string notice;
        public static string TakeNotice() { string n = notice; notice = null; return n; }
        public static void SetNotice(string text) { if (notice == null) notice = text; }
        private static Config TryRead(string path)
        {
            try { if (File.Exists(path)) return Deserialize(File.ReadAllText(path)); } catch { }
            return null;
        }
        public static Config Load()
        {
            bool exists = File.Exists(FilePath);
            Config main = TryRead(FilePath);
            if (main != null) return main;
            if (!exists && !File.Exists(FilePath + ".bak")) return new Config();
            // Fichier invalide : le mettre de côté (jamais écrasé), puis reprendre la dernière bonne sauvegarde.
            try
            {
                if (exists)
                    File.Move(FilePath, Path.Combine(DirectoryPath, "settings.invalide-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"));
            }
            catch { }
            Config backup = TryRead(FilePath + ".bak");
            if (backup != null)
            {
                SetNotice("Réglages illisibles : la dernière sauvegarde valide a été restaurée.");
                return backup;
            }
            SetNotice("Réglages illisibles : valeurs par défaut utilisées. L'ancien fichier est conservé dans le dossier Haloryva.");
            return new Config();
        }
        public static Config Deserialize(string json)
        {
            Config config = new JavaScriptSerializer().Deserialize<Config>(json);
            if (config == null) throw new InvalidDataException("Configuration vide");
            config.Normalize();
            return config;
        }
        public static bool Save(Config config)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string path = FilePath + ".tmp";
                string json = new JavaScriptSerializer().Serialize(config);
                File.WriteAllText(path, json);
                Deserialize(File.ReadAllText(path));   // relecture : ne jamais remplacer par un fichier illisible
                // Le .bak ne reçoit que des réglages valides.
                if (File.Exists(FilePath))
                {
                    if (TryRead(FilePath) != null) File.Replace(path, FilePath, FilePath + ".bak");
                    else File.Replace(path, FilePath, Path.Combine(DirectoryPath, "settings.invalide-" + Guid.NewGuid().ToString("N") + ".json"));
                }
                else File.Move(path, FilePath);
                return true;
            }
            catch (Exception ex) { SetNotice("Réglages non enregistrés : " + ex.Message); return false; }
        }
        public static void Export(Config config, string path)
        {
            File.WriteAllText(path, new JavaScriptSerializer().Serialize(config));
        }
        public static Config Import(string path)
        {
            FileInfo fi = new FileInfo(path);
            if (fi.Length > 1000000) throw new InvalidDataException("Le fichier de réglages est trop volumineux.");
            return Deserialize(File.ReadAllText(path));
        }
        public static bool ApplyAutoStart(bool on)
        {
            if (TrailTrial.Active) return true; // Essai isolé : ne pas modifier le registre.
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    if (key == null) throw new IOException("Clé de démarrage Windows absente.");
                    if (on && key.GetValue("CursorFlow") != null)
                    {
                        SetNotice("Démarrage Haloryva refusé : une ancienne application d'effets démarre déjà avec Windows. Désactive d'abord son démarrage automatique.");
                        return false;
                    }
                    if (on) key.SetValue("Haloryva", "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --silent");
                    else key.DeleteValue("Haloryva", false);
                    if ((key.GetValue("Haloryva") != null) != on)
                        throw new IOException("Le réglage de démarrage Windows n'a pas été confirmé.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                SetNotice("Modification du démarrage Windows impossible : " + ex.Message);
                return false;
            }
        }
    }
}
