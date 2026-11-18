using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace CursorFlow
{
    // Runs only inside the explicit --trail-selftest path, before app startup.
    // Production DrawTrailCore paints synthetic in-memory bitmaps: no Form,
    // desktop capture, hook, profile, installed process or output file is used.
    internal static class TrailStylesSelfTest
    {
        private const int Side = 256;
        private static readonly DateTime Zero = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static int checks, failures;

        private struct Pixels
        {
            internal long Alpha;
            internal int Count, Peak;
            internal Rectangle Bounds;
        }

        internal static int Run()
        {
            checks = failures = 0;
            RunCase("quantite des motifs", MotifQuantity);
            RunCase("cadence des groupes Logo", LogoCadence);
            RunCase("proportions des PNG", LogoBounds);
            RunCase("fondu des motifs", MotifFade);
            Console.WriteLine("TOTAL " + checks + " controles motifs/logos ; echecs=" + failures + ".");
            return failures == 0 ? 0 : 1;
        }

        private static void RunCase(string label, Action test)
        {
            try { test(); }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine("ECHEC STYLES exception " + label + " : " + ex);
            }
        }

        private static void Check(bool ok, string label)
        {
            checks++;
            if (ok) Console.WriteLine("OK STYLES " + checks + " " + label);
            else { failures++; Console.Error.WriteLine("ECHEC STYLES " + checks + " " + label); }
        }

        private static Config Options(string style)
        {
            // Only a plain Config object is constructed. No ConfigStore call.
            return new Config
            {
                Trail = true, TrailStyle = style, TrailWidth = 6, TrailLife = 1000,
                TrailDensity = 1, TrailSpacing = 6, TrailSpread = 0, TrailOpacity = 255,
                TrailSmoothness = 0, TrailSpeedResponsive = false, TrailColorMode = "Unie",
                TrailColor = "#FFFFFF", TrailColor2 = "#FFFFFF", TrailLogoSize = 20,
                TrailLogoEvery = 1, TrailLogoRotate = false, PerformanceMode = false
            };
        }

        private static List<TrailDot> Single()
        {
            TrailDot dot = new TrailDot(new Point(Side / 2, Side / 2), Zero, 0, 1);
            dot.BreakBefore = true;
            return new List<TrailDot> { dot };
        }

        private static Bitmap Render(Config options, List<TrailDot> dots, DateTime now, Bitmap logo)
        {
            Bitmap result = new Bitmap(Side, Side, PixelFormat.Format32bppPArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(result))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.Clear(Color.Transparent);
                    graphics.CompositingMode = CompositingMode.SourceOver;
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    // The tested branch is the actual motif/Logo renderer. No
                    // alternative drawing, sampler or layout formula lives here.
                    OverlayForm.DrawTrailCore(graphics, now, options, dots, logo);
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        private static Pixels Read(Bitmap bitmap)
        {
            Pixels result = new Pixels();
            int left = bitmap.Width, top = bitmap.Height, right = -1, bottom = -1;
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                {
                    int alpha = bitmap.GetPixel(x, y).A;
                    result.Alpha += alpha;
                    result.Peak = Math.Max(result.Peak, alpha);
                    if (alpha == 0) continue;
                    result.Count++;
                    left = Math.Min(left, x); top = Math.Min(top, y);
                    right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                }
            result.Bounds = right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
            return result;
        }

        private static bool SamePixels(Bitmap first, Bitmap second)
        {
            if (first.Size != second.Size) return false;
            for (int y = 0; y < first.Height; y++)
                for (int x = 0; x < first.Width; x++)
                    if (first.GetPixel(x, y).ToArgb() != second.GetPixel(x, y).ToArgb()) return false;
            return true;
        }

        private static bool HasInkNear(Bitmap bitmap, Point center)
        {
            Rectangle area = Rectangle.Intersect(new Rectangle(center.X - 11, center.Y - 11, 23, 23),
                new Rectangle(Point.Empty, bitmap.Size));
            for (int y = area.Top; y < area.Bottom; y++)
                for (int x = area.Left; x < area.Right; x++)
                    if (bitmap.GetPixel(x, y).A > 0) return true;
            return false;
        }

        private static Bitmap PngLogo(int width, int height)
        {
            // Exercise PNG decoding without a path or a file. The detached bitmap
            // survives disposal of the stream and is owned by the caller.
            using (Bitmap source = new Bitmap(width, height, PixelFormat.Format32bppPArgb))
            using (MemoryStream stream = new MemoryStream())
            {
                using (Graphics graphics = Graphics.FromImage(source)) graphics.Clear(Color.FromArgb(255, 40, 220, 100));
                source.Save(stream, ImageFormat.Png);
                stream.Position = 0;
                using (Bitmap decoded = new Bitmap(stream)) return new Bitmap(decoded);
            }
        }

        private static void MotifQuantity()
        {
            // Independent observable requirements: one motif is visible, five
            // create substantially more coverage and spread across the path.
            // Tolerances allow antialiasing and deliberate overlap of neighbours.
            foreach (string style in new string[] { "Particules", "Points", "Étoiles", "Kawaii" })
                Quantity(style, null, style);
            using (Bitmap logo = PngLogo(24, 24)) Quantity("Logo", logo, "Logo PNG");
            Quantity("Logo", null, "Logo etoile de remplacement");
        }

        private static void Quantity(string style, Bitmap logo, string label)
        {
            Config options = Options(style);
            List<TrailDot> dots = Single();
            Pixels one, five;
            using (Bitmap frame = Render(options, dots, Zero, logo)) one = Read(frame);
            options.TrailDensity = 5;
            using (Bitmap frame = Render(options, dots, Zero, logo)) five = Read(frame);
            Check(one.Count > 0 && five.Count > 0, label + " : quantites 1 et 5 visibles");
            Check(five.Alpha > one.Alpha * 2 && five.Bounds.Height > one.Bounds.Height * 2,
                label + " : cinq motifs cote a cote augmentent la couverture (alpha=" + one.Alpha + "/" + five.Alpha +
                ", hauteur=" + one.Bounds.Height + "/" + five.Bounds.Height + ")");
        }

        private static void LogoCadence()
        {
            Config options = Options("Logo");
            options.TrailLogoEvery = 3; options.TrailLogoSize = 18;
            TrailHistory history = new TrailHistory();
            for (int i = 0; i < 8; i++)
                history.Points.Add(new TrailDot(new Point(24 + i * 28, 128), Zero.AddMilliseconds(i * 100), 0, i + 1));
            DateTime now = Zero.AddMilliseconds(1100);
            using (Bitmap logo = PngLogo(20, 20))
            using (Bitmap before = Render(options, history.Points, now, logo))
            {
                // At life=1000 ms, IDs 1 and 2 expire. The cadence must keep its
                // phase on surviving IDs 3..8, instead of restarting at index 0.
                history.Expire(now, options.TrailLife);
                Check(history.Points.Count == 6 && history.Points[0].Id == 3,
                    "cadence : expiration reelle des deux premiers points");
                using (Bitmap after = Render(options, history.Points, now, logo))
                {
                    bool correctGroups = HasInkNear(after, new Point(108, 128)) && HasInkNear(after, new Point(192, 128));
                    foreach (int x in new int[] { 24, 52, 80, 136, 164, 220 })
                        correctGroups &= !HasInkNear(after, new Point(x, 128));
                    Check(correctGroups, "LogoEvery=3 : seuls les groupes survivants 4 et 7 sont dessines");
                    Check(SamePixels(before, after), "retirer les points expires ne deplace ni ne decale la cadence des logos");
                }
            }
        }

        private static void LogoBounds()
        {
            LogoBounds(10, 200, true);
            LogoBounds(200, 10, false);
        }

        private static void LogoBounds(int width, int height, bool vertical)
        {
            Config options = Options("Logo"); options.TrailLogoSize = 80;
            string label = vertical ? "PNG vertical 1:20" : "PNG horizontal 20:1";
            using (Bitmap logo = PngLogo(width, height))
            using (Bitmap frame = Render(options, Single(), Zero, logo))
            {
                Pixels pixels = Read(frame);
                int longSide = vertical ? pixels.Bounds.Height : pixels.Bounds.Width;
                int shortSide = vertical ? pixels.Bounds.Width : pixels.Bounds.Height;
                Check(pixels.Count > 0 && longSide >= 78 && longSide <= 82,
                    label + " : cote maximal demande de 80 px, raster=" + longSide);
                Check(shortSide >= 2 && shortSide <= 6,
                    label + " : cote court de 4 px a deux pixels de tolerance, raster=" + shortSide);
                double centerX = pixels.Bounds.Left + pixels.Bounds.Width / 2.0;
                double centerY = pixels.Bounds.Top + pixels.Bounds.Height / 2.0;
                Check(pixels.Count > 0 && Math.Abs(centerX - 128) <= 2 && Math.Abs(centerY - 128) <= 2,
                    label + " : logo centre sans debordement de la zone de test");
            }
        }

        private static void MotifFade()
        {
            foreach (string style in new string[] { "Particules", "Points", "Étoiles", "Kawaii" })
                Fade(style, null, style);
            using (Bitmap logo = PngLogo(24, 24)) Fade("Logo", logo, "Logo PNG");
            Fade("Logo", null, "Logo etoile de remplacement");
        }

        private static void Fade(string style, Bitmap logo, string label)
        {
            Config options = Options(style); options.TrailLife = 269; options.TrailOpacity = 210;
            List<TrailDot> dots = Single();
            Pixels start, middle, late, end, after;
            using (Bitmap frame = Render(options, dots, Zero, logo)) start = Read(frame);
            using (Bitmap frame = Render(options, dots, Zero.AddMilliseconds(134), logo)) middle = Read(frame);
            using (Bitmap frame = Render(options, dots, Zero.AddMilliseconds(240), logo)) late = Read(frame);
            using (Bitmap frame = Render(options, dots, Zero.AddMilliseconds(269), logo)) end = Read(frame);
            using (Bitmap frame = Render(options, dots, Zero.AddMilliseconds(400), logo)) after = Read(frame);
            Check(start.Alpha > middle.Alpha && middle.Alpha > late.Alpha && late.Alpha > 0 &&
                start.Peak > middle.Peak && middle.Peak > late.Peak,
                label + " : le meme point perd progressivement son opacite sans nouvelle entree");
            Check(end.Count == 0 && after.Count == 0,
                label + " : aucun pixel a la fin de vie de 269 ms ni apres, meme avant purge de la liste");
        }
    }
}
