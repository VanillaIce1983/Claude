using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Sketch;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>
    /// Region boundaries as the add-in reads them: one sampled piece per edge (lines, arcs,
    /// circles), welded into a segment set. Lengths in metres.
    /// </summary>
    internal static class RegionShapes
    {
        /// <summary>Chord tolerance of a 100 mm sketch.</summary>
        public const double Eps = 1e-5;

        public static IReadOnlyList<Vec3> Line(double x0, double y0, double x1, double y1, double eps = Eps) =>
            RegionGeometry.Sample(u => new Vec3(x0 + (x1 - x0) * u, y0 + (y1 - y0) * u, 0), 0, 1, eps);

        public static IReadOnlyList<Vec3> Arc(double cx, double cy, double r, double a0, double a1, double eps = Eps) =>
            RegionGeometry.Sample(u => new Vec3(cx + r * Math.Cos(u), cy + r * Math.Sin(u), 0), a0, a1, eps);

        public static IReadOnlyList<Vec3> Circle(double cx, double cy, double r, double eps = Eps) => Arc(cx, cy, r, 0, 2 * Math.PI, eps);

        /// <summary>A closed polygon, one piece per edge.</summary>
        public static List<IReadOnlyList<Vec3>> Polygon(double eps, params (double x, double y)[] corners)
        {
            var pieces = new List<IReadOnlyList<Vec3>>();
            for (int i = 0; i < corners.Length; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % corners.Length];
                pieces.Add(Line(a.x, a.y, b.x, b.y, eps));
            }
            return pieces;
        }

        public static List<IReadOnlyList<Vec3>> Polygon(params (double x, double y)[] corners) => Polygon(Eps, corners);

        public static List<IReadOnlyList<Vec3>> Rect(double x0, double y0, double x1, double y1, double eps = Eps) =>
            Polygon(eps, (x0, y0), (x1, y0), (x1, y1), (x0, y1));

        public static List<RegionSegment> Weld(IEnumerable<IReadOnlyList<Vec3>> pieces, double eps = Eps) =>
            RegionGeometry.Weld(pieces, RegionGeometry.WeldTolerance(eps)) ?? throw new InvalidOperationException("Schleife offen");

        public static ProfileRegion Region(double eps, params IEnumerable<IReadOnlyList<Vec3>>[] loops) =>
            ProfileRegion.Create(Weld(loops.SelectMany(l => l), eps), eps, null);

        public static ProfileRegion Region(params IEnumerable<IReadOnlyList<Vec3>>[] loops) => Region(Eps, loops);

        public static IEnumerable<IReadOnlyList<Vec3>> Pieces(params IReadOnlyList<Vec3>[] pieces) => pieces;

        /// <summary>The source region as the builder sees it: mirrored into the target sketch.</summary>
        public static ProfileRegion Mirrored(ProfileRegion source, Frame3 sourceFrame, Reflection r, Frame3 targetFrame) =>
            source.Map(p => SketchMirror.MapPoint(sourceFrame, r, targetFrame, p));

        public static double MinClearance => RegionGeometry.MinClearance(Eps);

        public static double Margin => RegionGeometry.Margin(Eps);

        /// <summary>Region 'where' (by witnesses) – the regions each witness lies in.</summary>
        public static List<int> Containing(Vec3 p, IReadOnlyList<ProfileRegion> regions) =>
            Enumerable.Range(0, regions.Count).Where(i => regions[i].Classify(p) == RegionPoint.Inside).ToList();
    }
}
