using System;
using System.Collections.Generic;
using System.Drawing;

namespace CursorFlow
{
    // Calculs purs sur les points : aucune dépendance au Bureau ni aux hooks Windows.
    // V2 : lissage réellement visible + reconnaissance par descripteurs géométriques
    // (enveloppe convexe, rectangle englobant minimal, plus grand triangle inscrit).
    internal static class DrawingGeometry
    {
        // ---------------------------------------------------------------- Lissage
        // Rééchantillonnage régulier (3 px) puis moyenne glissante symétrique.
        // La fenêtre se réduit près des extrémités : début et fin restent exacts.
        // Échelle V2.2 : 50 % = ancien maximum (±36 px, 3 passes), 100 % ≈ 2,3 × plus fort.
        internal static List<Point> Smooth(IList<Point> original, int intensity)
        {
            List<Point> copy = new List<Point>(original);
            if (original.Count < 3 || intensity <= 0) return copy;
            List<PointF> pts = ResampleBySpacing(original, 3.0);
            if (pts.Count < 5) return copy;
            int radius = Math.Max(1, (int)Math.Round(intensity * .24));
            int passes = intensity < 25 ? 2 : intensity < 70 ? 3 : 4;
            for (int pass = 0; pass < passes; pass++)
            {
                PointF[] next = new PointF[pts.Count];
                for (int i = 0; i < pts.Count; i++)
                {
                    int k = Math.Min(radius, Math.Min(i, pts.Count - 1 - i));
                    double x = 0, y = 0;
                    for (int j = i - k; j <= i + k; j++) { x += pts[j].X; y += pts[j].Y; }
                    next[i] = new PointF((float)(x / (2 * k + 1)), (float)(y / (2 * k + 1)));
                }
                pts = new List<PointF>(next);
            }
            List<Point> result = new List<Point>();
            result.Add(original[0]);
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Point p = Point.Round(pts[i]);
                if (p != result[result.Count - 1]) result.Add(p);
            }
            if (result[result.Count - 1] != original[original.Count - 1]) result.Add(original[original.Count - 1]);
            return result;
        }

        // ---------------------------------------------------------- Reconnaissance
        // Retourne true uniquement si le geste correspond nettement à une ligne,
        // un rectangle, une ellipse/un cercle ou un triangle. Les autres tracés
        // restent libres ; le contrôleur conserve OriginalPoints pour restauration.
        // Outils produits : "Ligne droite", "Rectangle", "Ellipse", "Triangle",
        // "Polygone" (rectangle ou ellipse inclinés).
        internal static bool TryAssist(AnnotationStroke stroke)
        {
            if ((stroke.Tool != "Stylo" && stroke.Tool != "Forme libre") || stroke.Points.Count < 6)
                return false;
            IList<Point> raw = stroke.Points;
            Point first = raw[0], last = raw[raw.Count - 1];
            double pathLength = 0;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int i = 0; i < raw.Count; i++)
            {
                minX = Math.Min(minX, raw[i].X); maxX = Math.Max(maxX, raw[i].X);
                minY = Math.Min(minY, raw[i].Y); maxY = Math.Max(maxY, raw[i].Y);
                if (i > 0) pathLength += Distance(raw[i - 1], raw[i]);
            }
            double diag = Math.Sqrt(Math.Pow(maxX - minX, 2) + Math.Pow(maxY - minY, 2));
            if (diag < 20 || pathLength < 25) return false;
            double chord = Distance(first, last);

            // 1. Segment presque droit : tolérance mesurée transversalement.
            if (chord >= 28 && pathLength <= chord * 1.40)
            {
                double sumSq = 0, maximum = 0;
                for (int i = 0; i < raw.Count; i++)
                {
                    double e = DistanceToLine(raw[i], first, last);
                    sumSq += e * e; maximum = Math.Max(maximum, e);
                }
                double rms = Math.Sqrt(sumSq / raw.Count);
                if (rms <= Math.Max(3.5, chord * .055) && maximum <= Math.Max(7, chord * .11))
                {
                    stroke.Tool = "Ligne droite";
                    stroke.Points = new List<Point> { first, last };
                    return true;
                }
            }

            // 2. Formes fermées : le tracé doit revenir près de son point de départ
            //    (un léger dépassement ou un petit écart sont tolérés).
            if (maxX - minX < 24 && maxY - minY < 24) return false;
            List<PointF> pts = Resample(raw, 96);
            double closeTolerance = Math.Max(18, diag * .22);
            bool closed = chord <= closeTolerance;
            if (!closed)
                for (int i = (int)(pts.Count * .6); i < pts.Count && !closed; i++)
                    if (Distance(pts[i], pts[0]) <= closeTolerance * .7) closed = true;
            if (!closed) return false;

            List<PointF> hull = ConvexHull(pts);
            if (hull.Count < 3) return false;
            double hullArea = Math.Abs(PolygonArea(hull));
            double hullPerimeter = Perimeter(hull, true);
            if (hullArea < 300 || hullPerimeter < 60) return false;
            // Gribouillage (plusieurs tours, va-et-vient) : jamais transformé.
            if (pathLength > hullPerimeter * 1.75) return false;

            double rectAngle, rectW, rectH; PointF rectCenter;
            MinAreaRectangle(hull, out rectCenter, out rectW, out rectH, out rectAngle);
            double rectArea = Math.Max(1, rectW * rectH);
            PointF ta, tb, tc;
            double triArea = LargestTriangle(hull, out ta, out tb, out tc);

            double fill = hullArea / rectArea;        // rectangle ≈ 1 · ellipse ≈ 0,785 · triangle ≈ 0,5
            double triRatio = triArea / hullArea;     // triangle ≈ 1 · rectangle ≈ 0,5 · ellipse ≈ 0,41
            double size = Math.Max(1, Math.Min(rectW, rectH));

            // Triangle : le plus grand triangle inscrit couvre presque tout le tracé.
            if (triRatio >= .68 && fill <= .72)
            {
                PointF[] tri = { ta, tb, tc };
                if (MeanDistanceToPolygon(pts, tri) / size > .10) return false;
                stroke.Tool = "Triangle";
                stroke.Points = new List<Point> { Point.Round(ta), Point.Round(tb), Point.Round(tc) };
                return true;
            }

            // Normalise l'orientation dans ]-45° ; 45°] en permutant largeur / hauteur.
            while (rectAngle > Math.PI / 4) { rectAngle -= Math.PI / 2; double t = rectW; rectW = rectH; rectH = t; }
            while (rectAngle <= -Math.PI / 4) { rectAngle += Math.PI / 2; double t = rectW; rectW = rectH; rectH = t; }
            double angleDeg = Math.Abs(rectAngle * 180 / Math.PI);
            bool axisAligned = angleDeg <= 10;

            // Rectangle : remplit son rectangle englobant minimal.
            if (fill >= .87)
            {
                FitSides(pts, ref rectCenter, ref rectW, ref rectH, rectAngle);
                PointF[] corners = RectCorners(rectCenter, rectW, rectH, axisAligned ? 0 : rectAngle);
                if (MeanDistanceToPolygon(pts, corners) / size > .10) return false;
                if (axisAligned)
                {
                    stroke.Tool = "Rectangle";
                    stroke.Points = new List<Point> {
                        Point.Round(new PointF(rectCenter.X - (float)rectW / 2, rectCenter.Y - (float)rectH / 2)),
                        Point.Round(new PointF(rectCenter.X + (float)rectW / 2, rectCenter.Y + (float)rectH / 2)) };
                }
                else
                {
                    stroke.Tool = "Polygone";
                    stroke.Points = new List<Point>();
                    foreach (PointF c in corners) stroke.Points.Add(Point.Round(c));
                }
                return true;
            }

            // Ellipse / cercle : ≈ π/4 du rectangle, contour régulier.
            if (fill >= .68 && triRatio < .62)
            {
                double a = rectW / 2, b = rectH / 2;
                // Rayon moyen du geste (et non ses points les plus extérieurs).
                double meanR = MeanNormalizedRadius(pts, rectCenter, a, b, rectAngle);
                a *= meanR; b *= meanR;
                // Presque rond → vrai cercle (c'est l'intention dans 9 cas sur 10).
                if (Math.Abs(a - b) / Math.Max(a, b) < .18) { a = b = (a + b) / 2; axisAligned = true; }
                if (MeanEllipseError(pts, rectCenter, a, b, axisAligned ? 0 : rectAngle) > .13) return false;
                if (axisAligned)
                {
                    stroke.Tool = "Ellipse";
                    stroke.Points = new List<Point> {
                        Point.Round(new PointF(rectCenter.X - (float)a, rectCenter.Y - (float)b)),
                        Point.Round(new PointF(rectCenter.X + (float)a, rectCenter.Y + (float)b)) };
                }
                else
                {
                    stroke.Tool = "Polygone";
                    stroke.Points = new List<Point>();
                    double cos = Math.Cos(rectAngle), sin = Math.Sin(rectAngle);
                    for (int i = 0; i < 72; i++)
                    {
                        double t = i * Math.PI * 2 / 72, x = a * Math.Cos(t), y = b * Math.Sin(t);
                        stroke.Points.Add(Point.Round(new PointF((float)(rectCenter.X + x * cos - y * sin), (float)(rectCenter.Y + x * sin + y * cos))));
                    }
                }
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- Outils
        private static List<PointF> Resample(IList<Point> src, int n)
        {
            double total = 0;
            for (int i = 1; i < src.Count; i++) total += Distance(src[i - 1], src[i]);
            return ResampleBySpacing(src, Math.Max(.5, total / (n - 1)));
        }
        private static List<PointF> ResampleBySpacing(IList<Point> src, double spacing)
        {
            List<PointF> result = new List<PointF>();
            PointF prev = src[0]; result.Add(prev);
            double carry = 0;
            for (int i = 1; i < src.Count; i++)
            {
                PointF cur = src[i];
                double seg = Distance(prev, cur);
                while (carry + seg >= spacing && seg > 0)
                {
                    double t = (spacing - carry) / seg;
                    PointF q = new PointF((float)(prev.X + (cur.X - prev.X) * t), (float)(prev.Y + (cur.Y - prev.Y) * t));
                    result.Add(q); prev = q; seg = Distance(prev, cur); carry = 0;
                }
                carry += seg; prev = cur;
            }
            PointF end = src[src.Count - 1];
            if (Distance(result[result.Count - 1], end) > .01) result.Add(end);
            return result;
        }
        private static List<PointF> ConvexHull(List<PointF> input)
        {
            List<PointF> p = new List<PointF>(input);
            p.Sort(delegate(PointF a, PointF b) { int c = a.X.CompareTo(b.X); return c != 0 ? c : a.Y.CompareTo(b.Y); });
            if (p.Count < 3) return p;
            PointF[] h = new PointF[p.Count * 2]; int k = 0;
            for (int i = 0; i < p.Count; i++)
            { while (k >= 2 && Cross(h[k - 2], h[k - 1], p[i]) <= 0) k--; h[k++] = p[i]; }
            for (int i = p.Count - 2, t = k + 1; i >= 0; i--)
            { while (k >= t && Cross(h[k - 2], h[k - 1], p[i]) <= 0) k--; h[k++] = p[i]; }
            List<PointF> hull = new List<PointF>();
            for (int i = 0; i < k - 1; i++) hull.Add(h[i]);
            return hull;
        }
        private static double Cross(PointF o, PointF a, PointF b)
        { return (a.X - (double)o.X) * (b.Y - (double)o.Y) - (a.Y - (double)o.Y) * (b.X - (double)o.X); }
        private static double PolygonArea(IList<PointF> p)
        {
            double s = 0;
            for (int i = 0; i < p.Count; i++) { PointF a = p[i], b = p[(i + 1) % p.Count]; s += a.X * (double)b.Y - b.X * (double)a.Y; }
            return s / 2;
        }
        private static double Perimeter(IList<PointF> p, bool closed)
        {
            double s = 0;
            for (int i = 1; i < p.Count; i++) s += Distance(p[i - 1], p[i]);
            if (closed && p.Count > 2) s += Distance(p[p.Count - 1], p[0]);
            return s;
        }
        // Rectangle d'aire minimale (calipers tournants sur les arêtes de l'enveloppe).
        private static void MinAreaRectangle(List<PointF> hull, out PointF center, out double width, out double height, out double angle)
        {
            double best = double.MaxValue; center = hull[0]; width = height = angle = 0;
            for (int i = 0; i < hull.Count; i++)
            {
                PointF a = hull[i], b = hull[(i + 1) % hull.Count];
                double ang = Math.Atan2(b.Y - a.Y, b.X - a.X), c = Math.Cos(ang), s = Math.Sin(ang);
                double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
                foreach (PointF p in hull)
                {
                    double u = p.X * c + p.Y * s, v = -p.X * s + p.Y * c;
                    minU = Math.Min(minU, u); maxU = Math.Max(maxU, u); minV = Math.Min(minV, v); maxV = Math.Max(maxV, v);
                }
                double area = (maxU - minU) * (maxV - minV);
                if (area < best)
                {
                    best = area; width = maxU - minU; height = maxV - minV; angle = ang;
                    double cu = (minU + maxU) / 2, cv = (minV + maxV) / 2;
                    center = new PointF((float)(cu * c - cv * s), (float)(cu * s + cv * c));
                }
            }
        }
        private static double LargestTriangle(List<PointF> hull, out PointF a, out PointF b, out PointF c)
        {
            List<PointF> h = hull;
            if (h.Count > 48) { h = new List<PointF>(); for (int i = 0; i < 48; i++) h.Add(hull[i * hull.Count / 48]); }
            double best = 0; a = b = c = h[0];
            for (int i = 0; i < h.Count; i++)
                for (int j = i + 1; j < h.Count; j++)
                    for (int k = j + 1; k < h.Count; k++)
                    {
                        double area = Math.Abs(Cross(h[i], h[j], h[k])) / 2;
                        if (area > best) { best = area; a = h[i]; b = h[j]; c = h[k]; }
                    }
            return best;
        }
        // Recale chaque côté sur la position moyenne des points qui le tracent.
        private static void FitSides(List<PointF> pts, ref PointF center, ref double w, ref double h, double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            double[] sum = new double[4]; int[] n = new int[4];
            foreach (PointF p in pts)
            {
                double dx = p.X - center.X, dy = p.Y - center.Y;
                double u = dx * c + dy * s, v = -dx * s + dy * c;
                double[] d = { Math.Abs(u + w / 2), Math.Abs(u - w / 2), Math.Abs(v + h / 2), Math.Abs(v - h / 2) };
                int k = 0; for (int i = 1; i < 4; i++) if (d[i] < d[k]) k = i;
                sum[k] += k < 2 ? u : v; n[k]++;
            }
            for (int i = 0; i < 4; i++) if (n[i] < 3) return;
            double left = sum[0] / n[0], right = sum[1] / n[1], top = sum[2] / n[2], bottom = sum[3] / n[3];
            if (right - left < 10 || bottom - top < 10) return;
            double cu = (left + right) / 2, cv = (top + bottom) / 2;
            center = new PointF((float)(center.X + cu * c - cv * s), (float)(center.Y + cu * s + cv * c));
            w = right - left; h = bottom - top;
        }
        private static double MeanNormalizedRadius(List<PointF> pts, PointF center, double a, double b, double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle), sum = 0;
            foreach (PointF p in pts)
            {
                double dx = p.X - center.X, dy = p.Y - center.Y;
                double u = (dx * c + dy * s) / Math.Max(1, a), v = (-dx * s + dy * c) / Math.Max(1, b);
                sum += Math.Sqrt(u * u + v * v);
            }
            return Math.Max(.7, Math.Min(1.05, sum / pts.Count));
        }
        private static PointF[] RectCorners(PointF center, double w, double h, double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            double[,] d = { { -w / 2, -h / 2 }, { w / 2, -h / 2 }, { w / 2, h / 2 }, { -w / 2, h / 2 } };
            PointF[] r = new PointF[4];
            for (int i = 0; i < 4; i++)
                r[i] = new PointF((float)(center.X + d[i, 0] * c - d[i, 1] * s), (float)(center.Y + d[i, 0] * s + d[i, 1] * c));
            return r;
        }
        private static double MeanDistanceToPolygon(List<PointF> pts, PointF[] poly)
        {
            double sum = 0;
            foreach (PointF p in pts)
            {
                double best = double.MaxValue;
                for (int i = 0; i < poly.Length; i++) best = Math.Min(best, DistanceToSegment(p, poly[i], poly[(i + 1) % poly.Length]));
                sum += best;
            }
            return sum / pts.Count;
        }
        private static double MeanEllipseError(List<PointF> pts, PointF center, double a, double b, double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle), sum = 0;
            foreach (PointF p in pts)
            {
                double dx = p.X - center.X, dy = p.Y - center.Y;
                double u = (dx * c + dy * s) / Math.Max(1, a), v = (-dx * s + dy * c) / Math.Max(1, b);
                sum += Math.Abs(Math.Sqrt(u * u + v * v) - 1);
            }
            return sum / pts.Count;
        }
        private static double Distance(PointF a, PointF b)
        { double dx = a.X - (double)b.X, dy = a.Y - (double)b.Y; return Math.Sqrt(dx * dx + dy * dy); }
        private static double DistanceToSegment(PointF p, PointF a, PointF b)
        {
            double dx = b.X - (double)a.X, dy = b.Y - (double)a.Y, len = dx * dx + dy * dy;
            if (len < 1e-9) return Distance(p, a);
            double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len));
            return Distance(p, new PointF((float)(a.X + t * dx), (float)(a.Y + t * dy)));
        }
        private static double DistanceToLine(Point p, Point a, Point b)
        {
            double dx = b.X - (double)a.X, dy = b.Y - (double)a.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            return len < .001 ? Distance(p, a) : Math.Abs(dy * p.X - dx * p.Y + b.X * (double)a.Y - b.Y * (double)a.X) / len;
        }
    }
}
