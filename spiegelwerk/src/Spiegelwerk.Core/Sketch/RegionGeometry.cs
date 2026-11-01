using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Core.Sketch
{
    /// <summary>A straight piece of a sampled region boundary, in sketch coordinates (Z = 0).</summary>
    public readonly struct RegionSegment
    {
        public readonly Vec3 A;
        public readonly Vec3 B;

        public RegionSegment(Vec3 a, Vec3 b)
        {
            A = a;
            B = b;
        }

        public double Length => A.DistanceTo(B);

        public RegionSegment Map(Func<Vec3, Vec3> f) => new RegionSegment(f(A), f(B));

        public override string ToString() => $"{A} – {B}";
    }

    /// <summary>A point inside a region and its distance to the region's boundary.</summary>
    public readonly struct RegionWitness
    {
        public readonly Vec3 Point;
        public readonly double Clearance;

        public RegionWitness(Vec3 point, double clearance)
        {
            Point = point;
            Clearance = clearance;
        }

        public RegionWitness Map(Func<Vec3, Vec3> f) => new RegionWitness(f(Point), Clearance);

        public override string ToString() => $"{Point} (Abstand {Clearance * 1000:0.####} mm)";
    }

    public enum RegionPoint
    {
        Inside,
        Outside,

        /// <summary>Closer to the boundary than the classification margin: the geometry may differ there.</summary>
        Boundary,
    }

    /// <summary>
    /// Geometry of sketch regions (ISketchRegion) as unordered sets of boundary segments. A
    /// region exposes no sketch segments, only B-rep loops whose edges are portions of
    /// segments; its identity in the mirrored sketch is therefore decided by points and
    /// shapes, not by ids:
    ///  - Sample: chord polylines of the edges (the add-in evaluates SOLIDWORKS' own curves),
    ///  - Weld: endpoints snapped together, every node of even degree (closed loops),
    ///  - FindInteriorPoints: witnesses well inside the region (even-odd rule, any number of holes),
    ///  - Classify, Distance, Area (band integration, independent of loop order and orientation).
    /// Everything works on unordered segments: outer loop and holes need not be told apart or
    /// chained, which is where pinch vertices would get them wrong.
    /// </summary>
    public static class RegionGeometry
    {
        /// <summary>Number of witnesses taken per region.</summary>
        public const int DefaultWitnesses = 8;

        /// <summary>Scan lines per direction through the widest gaps between vertex ordinates.</summary>
        private const int GapLevels = 64;

        /// <summary>Uniform scan lines per direction: ymin + h·k/16, k = 1..15.</summary>
        private const int UniformLevels = 16;

        /// <summary>
        /// Chord tolerance ε for a region from points of a coarse pass over its edges (a few
        /// evaluations per edge; B-rep vertices alone would not do – a full circle has none):
        /// 1e-4 of the bounding box diagonal, between 0.1 µm and 0.1 mm. A 100 mm sketch gets 10 µm.
        /// </summary>
        public static double ChordTolerance(IEnumerable<Vec3> coarsePoints)
        {
            double extent = Diagonal(coarsePoints);
            return Clamp(1e-4 * extent, 1e-7, 1e-4);
        }

        /// <summary>
        /// Smallest clearance a witness must have: the true boundary lies within ε of the
        /// polyline on the source and on the target side, the rebuilt sketch is accurate to
        /// Tol.Length; what remains to the target polyline (≥ 2ε + 9·Tol.Length) is more than the
        /// classification margin <see cref="Margin"/>, so a witness never counts as inside by accident.
        /// </summary>
        public static double MinClearance(double chordTolerance) => 4 * chordTolerance + 10 * Tol.Length;

        /// <summary>Distance below which a point counts as on the boundary.</summary>
        public static double Margin(double chordTolerance) => chordTolerance + Tol.Length;

        /// <summary>Largest distance at which two boundaries count as the same: 2ε + 10·Tol.Length.</summary>
        public static double IdentityTolerance(double chordTolerance) => 2 * chordTolerance + 10 * Tol.Length;

        /// <summary>Distance within which piece endpoints are welded together.</summary>
        public static double WeldTolerance(double chordTolerance) => 10 * chordTolerance;

        /// <summary>
        /// Chord polyline of a curve over [u0, u1], starting from <paramref name="minPieces"/>
        /// pieces. A piece is split while its midpoint or either quarter point deviates from its
        /// chord by more than <paramref name="chordTol"/> (the quarter points catch S-shaped
        /// pieces whose midpoint lies on the chord). Works with any curve SOLIDWORKS can
        /// evaluate: lines, arcs, circles, ellipses, splines, parabolas.
        /// </summary>
        public static List<Vec3> Sample(Func<double, Vec3> eval, double u0, double u1, double chordTol, int minPieces = 8, int maxDepth = 14)
        {
            if (minPieces < 1) minPieces = 1;
            var points = new List<Vec3>();
            Vec3 first = eval(u0);
            points.Add(first);
            Vec3 pa = first;
            for (int i = 0; i < minPieces; i++)
            {
                double a = u0 + (u1 - u0) * i / minPieces;
                double b = i + 1 == minPieces ? u1 : u0 + (u1 - u0) * (i + 1) / minPieces;
                Vec3 pb = eval(b);
                Refine(eval, a, pa, b, pb, chordTol, maxDepth, points);
                pa = pb;
            }
            return points;
        }

        private static void Refine(Func<double, Vec3> eval, double a, Vec3 pa, double b, Vec3 pb, double tol, int depth, List<Vec3> points)
        {
            double m = (a + b) / 2;
            Vec3 pm = eval(m);
            double dev = Math.Max(DistanceToSegment(pm, pa, pb),
                Math.Max(DistanceToSegment(eval(a + (b - a) / 4), pa, pb), DistanceToSegment(eval(a + 3 * (b - a) / 4), pa, pb)));
            if (dev > tol && depth > 0)
            {
                Refine(eval, a, pa, m, pm, tol, depth - 1, points);
                Refine(eval, m, pm, b, pb, tol, depth - 1, points);
            }
            else
            {
                points.Add(pb);
            }
        }

        /// <summary>
        /// Joins sampled pieces (one polyline per edge, in any order and direction) into a set
        /// of segments: piece endpoints within <paramref name="weldTol"/> become one node. Null
        /// if a node has odd degree – some loop is open – or nothing is left. The result is
        /// unordered; nothing needs coedge order or sense from the API.
        /// </summary>
        public static List<RegionSegment>? Weld(IEnumerable<IReadOnlyList<Vec3>> pieces, double weldTol)
        {
            var nodes = new List<Vec3>();
            var degree = new List<int>();
            var list = new List<(IReadOnlyList<Vec3> points, int start, int end)>();

            int NodeOf(Vec3 p)
            {
                int best = -1;
                double bestDistance = double.PositiveInfinity;
                for (int i = 0; i < nodes.Count; i++)
                {
                    double d = nodes[i].DistanceTo(p);
                    if (d <= weldTol && d < bestDistance)
                    {
                        best = i;
                        bestDistance = d;
                    }
                }
                if (best >= 0) return best;
                nodes.Add(p);
                degree.Add(0);
                return nodes.Count - 1;
            }

            foreach (IReadOnlyList<Vec3> piece in pieces)
            {
                if (piece == null || piece.Count < 2) return null;
                int s = NodeOf(piece[0]);
                int e = NodeOf(piece[piece.Count - 1]);
                degree[s]++;
                degree[e]++;
                list.Add((piece, s, e));
            }
            if (list.Count == 0 || degree.Any(d => (d & 1) == 1)) return null;

            var segments = new List<RegionSegment>();
            foreach (var (points, start, end) in list)
            {
                int last = points.Count - 1;
                for (int i = 0; i < last; i++)
                {
                    Vec3 a = i == 0 ? nodes[start] : points[i];
                    Vec3 b = i + 1 == last ? nodes[end] : points[i + 1];
                    if (a.DistanceTo(b) > 1e-12) segments.Add(new RegionSegment(Flat(a), Flat(b)));
                }
            }
            return segments.Count >= 2 ? segments : null;
        }

        /// <summary>Exact distance from a point to a segment (3D; region geometry has Z = 0).</summary>
        public static double DistanceToSegment(Vec3 p, Vec3 a, Vec3 b)
        {
            Vec3 d = b - a;
            double l2 = d.Dot(d);
            double t = l2 <= 0 ? 0 : Clamp((p - a).Dot(d) / l2, 0, 1);
            return (a + d * t - p).Length;
        }

        /// <summary>Distance from a point to the nearest boundary segment.</summary>
        public static double Distance(IReadOnlyList<RegionSegment> segments, Vec3 p)
        {
            double best = double.PositiveInfinity;
            foreach (RegionSegment s in segments)
            {
                double d = DistanceToSegment(p, s.A, s.B);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>
        /// Inside, outside or on the boundary (closer than <paramref name="margin"/>). Even-odd
        /// rule with a horizontal ray and the half-open crossing rule, so a ray through a
        /// vertex counts it once.
        /// </summary>
        public static RegionPoint Classify(IReadOnlyList<RegionSegment> segments, Vec3 p, double margin)
        {
            if (Distance(segments, p) < margin) return RegionPoint.Boundary;
            int crossings = 0;
            foreach (RegionSegment s in segments)
            {
                Vec3 a = s.A, b = s.B;
                if ((a.Y > p.Y) != (b.Y > p.Y) && a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y) > p.X) crossings++;
            }
            return (crossings & 1) == 1 ? RegionPoint.Inside : RegionPoint.Outside;
        }

        public static double Perimeter(IReadOnlyList<RegionSegment> segments) => segments.Sum(s => s.Length);

        /// <summary>
        /// Area enclosed by the segments (even-odd), by band integration: between consecutive
        /// distinct vertex ordinates the inside length is linear in y, so Δy·L(y_mid) is exact
        /// for the polyline – whatever the order, orientation and chaining of the segments.
        /// NaN if a band has an odd number of crossings (not closed).
        /// </summary>
        public static double Area(IReadOnlyList<RegionSegment> segments)
        {
            int n = segments.Count;
            if (n == 0) return 0;
            var ys = new List<double>(2 * n);
            foreach (RegionSegment s in segments)
            {
                ys.Add(s.A.Y);
                ys.Add(s.B.Y);
            }
            ys.Sort();
            int[] order = Enumerable.Range(0, n).OrderBy(i => Math.Min(segments[i].A.Y, segments[i].B.Y)).ToArray();
            var active = new List<int>();
            var xs = new List<double>();
            int next = 0;
            double area = 0;
            for (int k = 0; k + 1 < ys.Count; k++)
            {
                double y0 = ys[k], y1 = ys[k + 1];
                double ym = (y0 + y1) / 2;
                // A band too thin for a midpoint of its own adds nothing (ym would sit on a vertex).
                if (!(ym > y0 && ym < y1)) continue;
                // Active means crossing by the half-open rule, min ≤ ym < max – the same test as below.
                while (next < n && Math.Min(segments[order[next]].A.Y, segments[order[next]].B.Y) <= ym) active.Add(order[next++]);
                active.RemoveAll(i => Math.Max(segments[i].A.Y, segments[i].B.Y) <= ym);
                xs.Clear();
                foreach (int i in active)
                {
                    Vec3 a = segments[i].A, b = segments[i].B;
                    if ((a.Y > ym) != (b.Y > ym)) xs.Add(a.X + (ym - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                }
                if ((xs.Count & 1) == 1) return double.NaN;
                xs.Sort();
                double inside = 0;
                for (int j = 0; j + 1 < xs.Count; j += 2) inside += xs[j + 1] - xs[j];
                area += (y1 - y0) * inside;
            }
            return area;
        }

        /// <summary>
        /// Whether the kernel's area of a region agrees with its sampled polyline: the chords
        /// cut off at most ε per unit of boundary length. A disagreement means the edges were
        /// read wrongly (missing loop, wrong coordinates).
        /// </summary>
        public static bool AreaAgrees(double polylineArea, double kernelArea, double chordTolerance, double perimeter) =>
            Math.Abs(polylineArea - kernelArea) <= 2 * chordTolerance * perimeter + 1e-6 * Math.Abs(kernelArea) + 1e-12;

        /// <summary>Largest distance from any of the points to the nearest segment (one side of a Hausdorff test).</summary>
        public static double MaxDistance(IEnumerable<Vec3> points, IReadOnlyList<RegionSegment> segments)
        {
            double worst = 0;
            foreach (Vec3 p in points)
            {
                double d = Distance(segments, p);
                if (d > worst) worst = d;
            }
            return worst;
        }

        /// <summary>The segment endpoints.</summary>
        public static IEnumerable<Vec3> Vertices(IEnumerable<RegionSegment> segments)
        {
            foreach (RegionSegment s in segments)
            {
                yield return s.A;
                yield return s.B;
            }
        }

        /// <summary>
        /// Up to <paramref name="count"/> points inside the region with a clearance of at least
        /// <see cref="MinClearance"/>; empty if there is none (a sliver). Scan lines run
        /// horizontally and vertically – through the widest gaps between vertex ordinates (no
        /// vertex lies on them) and at 15 uniform levels; the midpoints of their inside
        /// intervals are the candidates, each with its exact clearance. The first witness is the
        /// candidate of largest clearance, moved further by alternating vertical and horizontal
        /// rescans; the others are spread over the region: each from a scan line of its own, at
        /// least 2·MinClearance from the others, with at least a quarter of the best clearance,
        /// the farthest from those taken first. Several witnesses survive a stand-in line that
        /// runs exactly through the middle of the region in the copy.
        /// </summary>
        public static List<RegionWitness> FindInteriorPoints(IReadOnlyList<RegionSegment> segments, double chordTolerance, int count = DefaultWitnesses)
        {
            var result = new List<RegionWitness>();
            if (count <= 0 || segments.Count < 2) return result;
            double minClearance = MinClearance(chordTolerance);
            double xmin = double.PositiveInfinity, xmax = double.NegativeInfinity, ymin = double.PositiveInfinity, ymax = double.NegativeInfinity;
            foreach (Vec3 v in Vertices(segments))
            {
                xmin = Math.Min(xmin, v.X);
                xmax = Math.Max(xmax, v.X);
                ymin = Math.Min(ymin, v.Y);
                ymax = Math.Max(ymax, v.Y);
            }
            if (ymax - ymin <= Tol.Length || xmax - xmin <= Tol.Length) return result;

            // Candidates per scan line ("band"): horizontal lines get ids 0.., vertical ones follow.
            var candidates = new List<(Vec3 point, double clearance, int band)>();
            int band = 0;
            foreach (bool horizontal in new[] { true, false })
            {
                foreach (double level in Levels(segments, horizontal))
                {
                    foreach (Vec3 c in Midpoints(segments, level, horizontal))
                        candidates.Add((c, Distance(segments, c), band));
                    band++;
                }
            }
            if (candidates.Count == 0) return result;

            var best = candidates.OrderByDescending(c => c.clearance).First();
            Vec3 bestPoint = best.point;
            double bestClearance = best.clearance;
            for (int it = 0; it < 4; it++)
            {
                bool horizontal = (it & 1) == 1;
                foreach (Vec3 c in Midpoints(segments, horizontal ? bestPoint.Y : bestPoint.X, horizontal))
                {
                    double d = Distance(segments, c);
                    if (d > bestClearance)
                    {
                        bestPoint = c;
                        bestClearance = d;
                    }
                }
            }
            if (bestClearance < minClearance) return result;
            result.Add(new RegionWitness(bestPoint, bestClearance));

            double floor = Math.Max(minClearance, bestClearance / 4);
            var pool = candidates.Where(c => c.clearance >= floor).OrderByDescending(c => c.clearance).ToList();
            var usedBands = new HashSet<int>();
            while (result.Count < count)
            {
                int pick = -1;
                double pickDistance = -1;
                for (int i = 0; i < pool.Count; i++)
                {
                    if (usedBands.Contains(pool[i].band)) continue;
                    double d = result.Min(w => w.Point.DistanceTo(pool[i].point));
                    // Ties keep the earlier one, i.e. the larger clearance.
                    if (d >= 2 * minClearance && d > pickDistance)
                    {
                        pick = i;
                        pickDistance = d;
                    }
                }
                if (pick < 0) break;
                result.Add(new RegionWitness(pool[pick].point, pool[pick].clearance));
                usedBands.Add(pool[pick].band);
            }
            return result;
        }

        /// <summary>
        /// Scan levels across the region: the midpoints of the widest gaps between consecutive
        /// distinct vertex ordinates (y for horizontal lines, x for vertical ones) and uniform levels.
        /// </summary>
        private static List<double> Levels(IReadOnlyList<RegionSegment> segments, bool horizontal)
        {
            List<double> ords = Vertices(segments).Select(v => horizontal ? v.Y : v.X).ToList();
            ords.Sort();
            double lo = ords[0], hi = ords[ords.Count - 1], h = hi - lo;
            var gaps = new List<(double level, double width)>();
            for (int i = 1; i < ords.Count; i++)
            {
                double w = ords[i] - ords[i - 1];
                if (w > 1e-12 * h) gaps.Add(((ords[i] + ords[i - 1]) / 2, w));
            }
            List<double> levels = gaps.OrderByDescending(g => g.width).Take(GapLevels).Select(g => g.level).ToList();
            for (int k = 1; k < UniformLevels; k++) levels.Add(lo + h * k / UniformLevels);
            return levels;
        }

        /// <summary>Midpoints of the inside intervals along y = v (horizontal) or x = v (vertical); none if the crossings are odd.</summary>
        private static IEnumerable<Vec3> Midpoints(IReadOnlyList<RegionSegment> segments, double v, bool horizontal)
        {
            var xs = new List<double>();
            foreach (RegionSegment s in segments)
            {
                double ax = horizontal ? s.A.X : s.A.Y, ay = horizontal ? s.A.Y : s.A.X;
                double bx = horizontal ? s.B.X : s.B.Y, by = horizontal ? s.B.Y : s.B.X;
                if ((ay > v) != (by > v)) xs.Add(ax + (v - ay) * (bx - ax) / (by - ay));
            }
            if ((xs.Count & 1) == 1) yield break;
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                double m = (xs[k] + xs[k + 1]) / 2;
                yield return horizontal ? new Vec3(m, v, 0) : new Vec3(v, m, 0);
            }
        }

        private static double Diagonal(IEnumerable<Vec3> points)
        {
            double xmin = double.PositiveInfinity, ymin = double.PositiveInfinity, zmin = double.PositiveInfinity;
            double xmax = double.NegativeInfinity, ymax = double.NegativeInfinity, zmax = double.NegativeInfinity;
            bool any = false;
            foreach (Vec3 p in points)
            {
                any = true;
                xmin = Math.Min(xmin, p.X);
                ymin = Math.Min(ymin, p.Y);
                zmin = Math.Min(zmin, p.Z);
                xmax = Math.Max(xmax, p.X);
                ymax = Math.Max(ymax, p.Y);
                zmax = Math.Max(zmax, p.Z);
            }
            return any ? new Vec3(xmax - xmin, ymax - ymin, zmax - zmin).Length : 0;
        }

        private static Vec3 Flat(Vec3 p) => new Vec3(p.X, p.Y, 0);

        // netstandard2.0 has no Math.Clamp.
        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
