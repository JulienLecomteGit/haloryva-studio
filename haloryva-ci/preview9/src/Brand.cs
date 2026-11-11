using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;

// Identité du produit : nom, version, auteur et logo dessiné en code.
// Ce fichier est volontairement autonome (System.Drawing seulement) : il est compilé
// à la fois dans Haloryva.exe et dans l'installateur graphique, pour que le logo,
// la version et les crédits restent identiques partout.
[assembly: AssemblyTitle("Haloryva Studio")]
[assembly: AssemblyDescription("Effets de curseur, annotations et présentation d'écran pour Windows")]
[assembly: AssemblyCompany("Julien Lecomte")]
[assembly: AssemblyProduct("Haloryva Studio")]
[assembly: AssemblyCopyright("© 2026 Julien Lecomte")]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Haloryva.Preview8.Targeted")]
[assembly: AssemblyVersion("2.7.1.0")]
[assembly: AssemblyFileVersion("2.7.1.9")]
[assembly: AssemblyInformationalVersion("2.7.1-preview.9")]

namespace CursorFlow
{
    internal static class Brand
    {
        public const string Product = "Haloryva Studio";
        public const string Short = "Haloryva";
        public const string Version = "2.7.1";
        public const string BuildDate = "11/11/2026";
        public const string Author = "Julien Lecomte";
        public const string Copyright = "© 2026 Julien Lecomte";
        public const string Tagline = "Effets, annotations et présentation du curseur";
        public const string Privacy = "Application locale · aucune donnée envoyée";

        // Historique court affiché dans « À propos » : version, date, résumé.
        public static readonly string[][] History = new string[][]
        {
            new string[] { Version, "11/11/2026", "Préversion : démarrage MSIX et reprise indépendante des réglages" },
            new string[] { "2.7.0", "10/11/2026", "269 ms, souplesse continue et orientation X1/X2 personnalisable" },
            new string[] { "2.6.6", "08/11/2026", "Fluidité : traînée persistante, surface stable, cadence et mesures" },
            new string[] { "2.6.5", "08/11/2026", "Routage des boutons, installation précompilée et maintenance vérifiée" },
            new string[] { "2.6.4", "08/11/2026", "Option : halo agrandi sur curseur texte" },
            new string[] { "2.6.3", "08/11/2026", "Fiabilité : boutons, réglages, installateur avec retour arrière" },
            new string[] { "2.6.2", "08/11/2026", "Exe d'installation autonome, signature et paquet Store préparés" },
            new string[] { "2.6.1", "08/11/2026", "Inscription dans Applications installées et désinstallateur graphique" },
            new string[] { "2.6.0", "08/11/2026", "Logo, crédits, splash et installateur graphique" },
            new string[] { "2.5.1", "08/11/2026", "Correctif : appui long réservé à la molette, X1 et X2" },
            new string[] { "2.5.0", "08/11/2026", "Appui long sur 5 boutons, roue de couleur, menus réorganisés" },
            new string[] { "2.4.1", "08/11/2026", "Affichage d'outil plus sobre" },
            new string[] { "2.4.0", "08/11/2026", "Cycle des outils de dessin avec annonce en haut à gauche" }
        };

        public static readonly Color Cyan = Color.FromArgb(34, 211, 238);
        public static readonly Color Purple = Color.FromArgb(150, 108, 255);

        public static string VersionLine { get { return Product + " " + Version + L.T(" · build ") + BuildDate + " · Windows x64"; } }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = Math.Max(0.5f, Math.Min(radius * 2f, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // Identité Haloryva : monogramme de halo et étoile, même rendu dans Studio et Setup.
        // Effets intégralement décoratifs : aucun changement au moteur de la souris.
        public static void DrawLogo(Graphics g, RectangleF r, float spin)
        {
            GraphicsState oldState = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(r.Width, r.Height);
            RectangleF box = new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s);
            using (GraphicsPath plate = RoundRect(box, s * .24f))
            using (LinearGradientBrush fill = new LinearGradientBrush(box, Cyan, Purple, 32f))
            {
                g.FillPath(fill, plate);
                RectangleF light = new RectangleF(box.X, box.Y, s, s * .52f);
                using (LinearGradientBrush shine = new LinearGradientBrush(light,
                    Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                {
                    g.SetClip(plate, CombineMode.Intersect);
                    g.FillRectangle(shine, light);
                    g.ResetClip();
                }
            }
            // Le halo donne son identité à Haloryva ; l'arc reste à l'intérieur du pictogramme.
            float cx = box.X + s * .50f, cy = box.Y + s * .53f;
            RectangleF orbit = new RectangleF(cx - s * .315f, cy - s * .315f, s * .63f, s * .63f);
            for (int i = 0; i < 4; i++)
            {
                float stroke = s * (.095f - i * .015f);
                int alpha = 28 + i * 44;
                using (Pen p = new Pen(Color.FromArgb(Math.Min(235, alpha), Color.White), stroke))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    g.DrawArc(p, orbit, 140 + spin, 253);
                }
            }
            // Étoile quatre branches, symbole de précision, distinct du pointeur Windows.
            float ex = box.X + s * .70f, ey = box.Y + s * .30f;
            PointF[] star = { new PointF(ex, ey - s * .135f), new PointF(ex + s * .043f, ey - s * .042f),
                new PointF(ex + s * .13f, ey), new PointF(ex + s * .043f, ey + s * .042f),
                new PointF(ex, ey + s * .135f), new PointF(ex - s * .043f, ey + s * .042f),
                new PointF(ex - s * .13f, ey), new PointF(ex - s * .043f, ey - s * .042f) };
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(68, Color.White)))
                g.FillEllipse(glow, ex - s * .15f, ey - s * .15f, s * .30f, s * .30f);
            using (SolidBrush white = new SolidBrush(Color.White)) g.FillPolygon(white, star);
            g.Restore(oldState);
        }

        public static Bitmap RenderLogo(int size)
        {
            Bitmap bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float pad = size >= 32 ? size * .04f : 0f;
                DrawLogo(g, new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad), 0f);
            }
            return bmp;
        }
    }
}
