using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>What <see cref="BendSide.ChooseFixedPoint"/> decided about a fixed point read in two ways.</summary>
    public sealed class FixedPointChoice
    {
        public FixedPointChoice(Vec3? point, FixedPointSpace space, bool ambiguous)
        {
            Point = point;
            Space = space;
            Ambiguous = ambiguous;
        }

        /// <summary>The point in model coordinates; null if neither reading lies on the face.</summary>
        public Vec3? Point { get; }

        public FixedPointSpace Space { get; }

        /// <summary>Both readings lie on the face, but in different regions: the preferred one was taken.</summary>
        public bool Ambiguous { get; }
    }

    /// <summary>
    /// Which side of a bend stays (A3 P1, A2 P6, blech-plan K8/K9): the bend lines cut the fixed face
    /// into regions, and SOLIDWORKS holds the one the face was picked in. A reflection maps a point to
    /// a point and a region to a region, so the copy picks the mirrored face in the region of the
    /// mirrored point – at a point well away from every line, never on one (CADapter: a pick on the
    /// line turns half the sheet with no error). Whether a point lies left or right of a line is not
    /// invariant (the sign flips with the line's direction and the normal, A2 P14); only "the same
    /// region as" is, which is all this class compares.
    /// </summary>
    public static class BendSide
    {
        /// <summary>A fixed point lies on its face within this distance (10 µm, as the face samples).</summary>
        public const double OnFaceTolerance = 1e-5;

        /// <summary>Every candidate keeps at least this far from every line: twice the thickness, at least 1 mm.</summary>
        public static double Clearance(double thickness) => Math.Max(2 * thickness, 1e-3);

        /// <summary>
        /// The region of a point: for every line the side it lies on (+1/−1), 0 where it lies on the
        /// line (within <paramref name="tol"/>). The lines are taken in the plane with normal <paramref name="n"/>.
        /// </summary>
        public static int[] RegionOf(Vec3 p, IReadOnlyList<Line3> lines, Vec3 n, double tol = Tol.Length)
        {
            var region = new int[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                Vec3 across = n.Cross(lines[i].Direction);
                double len = across.Length;
                if (len < 1e-12)
                {
                    region[i] = 0;
                    continue;
                }
                double s = (p - lines[i].Point).Dot(across / len);
                region[i] = Math.Abs(s) <= tol ? 0 : Math.Sign(s);
            }
            return region;
        }

        /// <summary>Two points lie in the same region: on the same side of every line, and on none of them.</summary>
        public static bool SameRegion(int[] a, int[] b) => a.Length == b.Length && a.All(x => x != 0) && a.SequenceEqual(b);

        /// <summary>Distance in the plane from a point to the nearest line (infinity without lines).</summary>
        public static double DistanceToLines(Vec3 p, IReadOnlyList<Line3> lines, Vec3 n)
        {
            double best = double.PositiveInfinity;
            foreach (Line3 l in lines)
            {
                Vec3 across = n.Cross(l.Direction);
                double len = across.Length;
                if (len < 1e-12) continue;
                best = Math.Min(best, Math.Abs((p - l.Point).Dot(across / len)));
            }
            return best;
        }

        /// <summary>Grid steps per half axis of the search square.</summary>
        private const int GridSteps = 16;

        /// <summary>How many points of one region are offered to <c>onFace</c> before the region is given up.</summary>
        private const int TriesPerRegion = 40;

        /// <summary>
        /// One pick point per region of the face cut by all lines (A3 P1): a point at least
        /// <paramref name="clearance"/> from every line – of each region the one farthest from all
        /// lines, up to a quarter of <paramref name="reach"/> (then the one nearest the face point),
        /// that <paramref name="onFace"/> accepts (the caller checks it on the face by the kernel;
        /// without the callback the plane is taken as unbounded). A region narrower than twice
        /// the clearance gives none. Searched in the square of half size <paramref name="reach"/> about
        /// <paramref name="facePoint"/> in the plane through it with normal <paramref name="n"/> (the
        /// lines are projected into it), plus points beside each line's midpoint. Regions in the order
        /// of their best point's clearance.
        /// </summary>
        public static List<Vec3> SideCandidates(IReadOnlyList<Line3> lines, Vec3 n, Vec3 facePoint, double clearance, double reach,
            Func<Vec3, bool>? onFace = null)
        {
            n = n.Normalized();
            var plane = new Plane3(facePoint, n);
            List<Line3> inPlane = lines.Select(l => new Line3(plane.Project(l.Point), l.Direction - n * l.Direction.Dot(n))).ToList();
            Vec3 e1 = inPlane.Count > 0 && inPlane[0].Direction.Length > 1e-12 ? inPlane[0].Direction.Normalized() : n.AnyPerpendicular().Normalized();
            Vec3 e2 = n.Cross(e1).Normalized();

            var points = new List<Vec3>();
            for (int i = -GridSteps; i <= GridSteps; i++)
                for (int j = -GridSteps; j <= GridSteps; j++)
                    points.Add(facePoint + e1 * (reach * i / GridSteps) + e2 * (reach * j / GridSteps));
            foreach (Line3 l in inPlane)
            {
                Vec3 across = n.Cross(l.Direction);
                if (across.Length < 1e-12) continue;
                across = across.Normalized();
                for (int k = 0; k < 4; k++)
                {
                    double d = clearance + k * Math.Max(0, reach / 2 - clearance) / 3;
                    points.Add(l.Point + across * d);
                    points.Add(l.Point - across * d);
                }
            }

            var regions = new Dictionary<string, List<(Vec3 p, double clearance)>>(StringComparer.Ordinal);
            foreach (Vec3 p in points)
            {
                double c = DistanceToLines(p, inPlane, n);
                if (c < clearance) continue;
                int[] region = RegionOf(p, inPlane, n);
                if (region.Any(x => x == 0)) continue;
                string key = string.Join(",", region);
                if (!regions.TryGetValue(key, out var list)) regions[key] = list = new List<(Vec3, double)>();
                list.Add((p, c));
            }

            // Far from the lines, but no farther than a quarter of the face (A2's preference): beyond that the
            // point nearer the face point comes first – the corners of the square may be off a face of another shape.
            double enough = Math.Max(clearance, reach / 4);
            var chosen = new List<(Vec3 p, double clearance)>();
            foreach (var kv in regions.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                IEnumerable<(Vec3 p, double clearance)> ordered = kv.Value.OrderByDescending(x => Math.Min(x.clearance, enough)).ThenBy(x => x.p.DistanceTo(facePoint));
                foreach (var candidate in ordered.Take(onFace == null ? 1 : TriesPerRegion))
                    if (onFace == null || onFace(candidate.p))
                    {
                        chosen.Add(candidate);
                        break;
                    }
            }
            return chosen.OrderByDescending(x => x.clearance).Select(x => x.p).ToList();
        }

        /// <summary>
        /// The single-line case (the jog, A2 P6): the pick point on the side of <paramref name="line"/>
        /// that <paramref name="side"/> lies on – <see cref="SideCandidates"/> with one line, the
        /// candidate of that region. Null if the side point lies on the line or the region has none.
        /// </summary>
        public static Vec3? PointBeside(Line3 line, Vec3 n, Vec3 side, Vec3 facePoint, double clearance, double reach, Func<Vec3, bool>? onFace = null)
        {
            var lines = new[] { line };
            int[] wanted = RegionOf(side, lines, n);
            if (wanted[0] == 0) return null;
            foreach (Vec3 p in SideCandidates(lines, n, facePoint, clearance, reach, onFace))
                if (SameRegion(RegionOf(p, lines, n), wanted))
                    return p;
            return null;
        }

        /// <summary>
        /// The pick points in the order they are tried: first the region of <paramref name="fixedPoint"/>
        /// – the point itself if it keeps the clearance from every line, otherwise that region's candidate –,
        /// then every other region (<see cref="SideCandidates"/>). Without a fixed point (or one on a line)
        /// just the candidates.
        /// </summary>
        public static List<Vec3> PickOrder(Vec3? fixedPoint, IReadOnlyList<Line3> lines, Vec3 n, Vec3 facePoint, double clearance, double reach,
            Func<Vec3, bool>? onFace = null)
        {
            List<Vec3> candidates = SideCandidates(lines, n, facePoint, clearance, reach, onFace);
            if (!fixedPoint.HasValue) return candidates;
            Vec3 p = fixedPoint.Value;
            int[] region = RegionOf(p, lines, n);
            if (region.Any(x => x == 0)) return candidates;
            var order = new List<Vec3>();
            List<Vec3> own = candidates.Where(c => SameRegion(RegionOf(c, lines, n), region)).ToList();
            if (DistanceToLines(p, lines, n) >= clearance) order.Add(p);
            else order.AddRange(own.Take(1));
            order.AddRange(candidates.Where(c => !SameRegion(RegionOf(c, lines, n), region)));
            return order;
        }

        /// <summary>
        /// A fixed point read in two ways – as sketch coordinates mapped to the model, and as model
        /// coordinates – with each reading's distance to the fixed face (blech-plan K9). Only a reading
        /// on the face counts. If both do and lie in the same region (<paramref name="regionOf"/>; or
        /// at the same place), the <paramref name="preferred"/> one; in different regions the preferred
        /// one too, but ambiguous – the sketched bend takes it with a note, the jog treats its side as
        /// unknown. If neither lies on the face, unknown.
        /// </summary>
        /// <param name="preferred">The sketched bend: <see cref="FixedPointSpace.Sketch"/> (documented); the jog: <see cref="FixedPointSpace.Model"/> (A2's evidence).</param>
        public static FixedPointChoice ChooseFixedPoint(FixedPointSpace preferred, Vec3 asSketch, double dSketch, Vec3 asModel, double dModel, double tol,
            Func<Vec3, int[]>? regionOf = null)
        {
            bool onSketch = dSketch <= tol, onModel = dModel <= tol;
            if (!onSketch && !onModel) return new FixedPointChoice(null, FixedPointSpace.Unknown, false);
            if (onSketch && !onModel) return new FixedPointChoice(asSketch, FixedPointSpace.Sketch, false);
            if (onModel && !onSketch) return new FixedPointChoice(asModel, FixedPointSpace.Model, false);
            Vec3 chosen = preferred == FixedPointSpace.Model ? asModel : asSketch;
            FixedPointSpace space = preferred == FixedPointSpace.Model ? FixedPointSpace.Model : FixedPointSpace.Sketch;
            if (asSketch.AlmostEquals(asModel, tol)) return new FixedPointChoice(chosen, space, false);
            bool same = regionOf != null && SameRegion(regionOf(asSketch), regionOf(asModel));
            return new FixedPointChoice(chosen, space, !same);
        }
    }
}
