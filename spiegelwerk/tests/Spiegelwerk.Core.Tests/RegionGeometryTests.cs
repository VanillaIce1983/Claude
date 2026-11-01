using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Sketch;
using Xunit;
using static Spiegelwerk.Core.Tests.RegionShapes;
using static Spiegelwerk.Core.Tests.TestGeometry;

namespace Spiegelwerk.Core.Tests
{
    public class RegionGeometryTests
    {
        private static List<RegionSegment> Polyline(IReadOnlyList<Vec3> points) =>
            Enumerable.Range(0, points.Count - 1).Select(i => new RegionSegment(points[i], points[i + 1])).ToList();

        /// <summary>Largest distance of the true curve (densely evaluated) from the sampled polyline.</summary>
        private static double Deviation(Func<double, Vec3> eval, double u0, double u1, IReadOnlyList<Vec3> sampled)
        {
            List<RegionSegment> poly = Polyline(sampled);
            return Enumerable.Range(0, 2001).Max(i => RegionGeometry.Distance(poly, eval(u0 + (u1 - u0) * i / 2000)));
        }

        [Fact]
        public void Sampled_arc_stays_within_the_chord_tolerance()
        {
            const double r = 0.05;
            Func<double, Vec3> arc = u => new Vec3(r * Math.Cos(u), r * Math.Sin(u), 0);
            List<Vec3> pts = RegionGeometry.Sample(arc, 0, 2, Eps);
            double worst = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                double a0 = Math.Atan2(pts[i - 1].Y, pts[i - 1].X), a1 = Math.Atan2(pts[i].Y, pts[i].X);
                worst = Math.Max(worst, r * (1 - Math.Cos((a1 - a0) / 2)));
            }
            Assert.True(worst <= Eps, $"Sehnenabweichung {worst:E2}");
            Assert.InRange(pts.Count, 9, 200);
            Near(arc(0), pts[0], 1e-15);
            Near(arc(2), pts[pts.Count - 1], 1e-15);
        }

        [Fact]
        public void Flat_ellipse_and_an_s_curve_with_its_midpoint_on_the_chord_are_refined()
        {
            Func<double, Vec3> ellipse = u => new Vec3(0.1 * Math.Cos(u), 0.002 * Math.Sin(u), 0);
            List<Vec3> e = RegionGeometry.Sample(ellipse, 0, 2 * Math.PI, Eps);
            Assert.True(Deviation(ellipse, 0, 2 * Math.PI, e) <= Eps * 1.01);

            // One piece only: its midpoint lies exactly on the chord, the quarter points do not.
            Func<double, Vec3> s = u => new Vec3(0.1 * u, 0.01 * Math.Sin(2 * Math.PI * u), 0);
            List<Vec3> sp = RegionGeometry.Sample(s, 0, 1, Eps, minPieces: 1);
            Assert.True(sp.Count > 3);
            Assert.True(Deviation(s, 0, 1, sp) <= Eps * 1.01);
        }

        [Fact]
        public void Full_circle_is_sampled_closed()
        {
            IReadOnlyList<Vec3> c = Circle(0.01, 0.02, 0.03);
            Near(c[0], c[c.Count - 1], 1e-15);
        }

        [Fact]
        public void Chord_tolerance_of_a_vertex_free_circle_comes_from_the_coarse_pass()
        {
            // A full circle has no B-rep vertex: from vertices alone the extent would be zero.
            Assert.Equal(1e-7, RegionGeometry.ChordTolerance(Array.Empty<Vec3>()));
            const double r = 0.1;
            IEnumerable<Vec3> coarse = Enumerable.Range(0, 9).Select(i => new Vec3(r * Math.Cos(i * Math.PI / 4), r * Math.Sin(i * Math.PI / 4), 0));
            double eps = RegionGeometry.ChordTolerance(coarse);
            Near(1e-4 * Math.Sqrt(2) * 2 * r, eps, 1e-12);
            Assert.InRange(Circle(0, 0, r, eps).Count, 100, 400);
            // Large sketches are capped at 0.1 mm, tiny ones at 0.1 µm.
            Assert.Equal(1e-4, RegionGeometry.ChordTolerance(new[] { Vec3.Zero, new Vec3(5, 0, 0) }));
        }

        [Fact]
        public void Weld_ignores_order_and_direction_of_the_pieces()
        {
            var pieces = Rect(0, 0, 0.05, 0.03).Concat(Pieces(Circle(0.02, 0.015, 0.008))).ToList();
            List<RegionSegment> ordered = Weld(pieces);
            double area = RegionGeometry.Area(ordered);
            var rnd = new Random(3);
            for (int k = 0; k < 20; k++)
            {
                List<IReadOnlyList<Vec3>> shuffled = pieces.OrderBy(_ => rnd.Next())
                    .Select(p => rnd.Next(2) == 0 ? p : (IReadOnlyList<Vec3>)p.Reverse().ToList()).ToList();
                List<RegionSegment> w = Weld(shuffled);
                Near(area, RegionGeometry.Area(w), 1e-15);
                Assert.Equal(RegionPoint.Outside, RegionGeometry.Classify(w, new Vec3(0.02, 0.015, 0), Margin));
                Assert.Equal(RegionPoint.Inside, RegionGeometry.Classify(w, new Vec3(0.04, 0.015, 0), Margin));
            }
        }

        [Fact]
        public void Weld_closes_a_gap_inside_the_tolerance_and_rejects_a_larger_one()
        {
            double tol = RegionGeometry.WeldTolerance(Eps);
            List<IReadOnlyList<Vec3>> Square(double gap) => new List<IReadOnlyList<Vec3>>
            {
                Line(0, 0, 0.02, 0), Line(0.02, 0, 0.02, 0.02), Line(0.02, 0.02, 0, 0.02), Line(0, 0.02, 0, gap),
            };
            Assert.NotNull(RegionGeometry.Weld(Square(0.5 * tol), tol));
            Assert.Null(RegionGeometry.Weld(Square(2 * tol), tol));
            // An open chain: odd degree at both ends.
            Assert.Null(RegionGeometry.Weld(Square(0).Take(3), tol));
        }

        [Fact]
        public void Pinch_vertex_area_does_not_depend_on_the_order_of_the_edges()
        {
            // Rectangle minus a triangle that touches the left side at one vertex.
            var edges = new List<IReadOnlyList<Vec3>>
            {
                Line(0, 0, 0.04, 0), Line(0.04, 0, 0.04, 0.02), Line(0.04, 0.02, 0, 0.02), Line(0, 0.02, 0, 0.01), Line(0, 0.01, 0, 0),
                Line(0, 0.01, 0.01, 0.015), Line(0.01, 0.015, 0.01, 0.005), Line(0.01, 0.005, 0, 0.01),
            };
            Near(7.5e-4, RegionGeometry.Area(Weld(edges)), 1e-15);
            // The triangle traversed the other way round, edges in another order.
            var other = new List<IReadOnlyList<Vec3>>
            {
                Line(0, 0.01, 0.01, 0.005), Line(0, 0.02, 0, 0.01), Line(0.01, 0.005, 0.01, 0.015), Line(0.04, 0, 0.04, 0.02),
                Line(0.01, 0.015, 0, 0.01), Line(0, 0, 0.04, 0), Line(0, 0.01, 0, 0), Line(0.04, 0.02, 0, 0.02),
            };
            Near(7.5e-4, RegionGeometry.Area(Weld(other)), 1e-15);
        }

        [Fact]
        public void Ring_area_is_exact_within_the_chord_error()
        {
            List<RegionSegment> ring = Weld(Pieces(Circle(0, 0, 0.02), Circle(0, 0, 0.01)));
            double exact = Math.PI * (0.02 * 0.02 - 0.01 * 0.01);
            Assert.True(Math.Abs(RegionGeometry.Area(ring) - exact) <= 2 * Eps * RegionGeometry.Perimeter(ring));
            Assert.True(RegionGeometry.AreaAgrees(RegionGeometry.Area(ring), exact, Eps, RegionGeometry.Perimeter(ring)));
            Assert.False(RegionGeometry.AreaAgrees(RegionGeometry.Area(ring), Math.PI * 0.02 * 0.02, Eps, RegionGeometry.Perimeter(ring)));
        }

        [Fact]
        public void Rectangle_and_l_shape_get_interior_points()
        {
            ProfileRegion rect = Region(Rect(0, 0, 0.04, 0.02));
            Assert.Equal(RegionGeometry.DefaultWitnesses, rect.Witnesses.Count);
            Near(0.01, rect.Clearance, 1e-9);

            // The bounding box centre of an L lies outside it.
            ProfileRegion l = Region(Polygon((0, 0), (0.05, 0), (0.05, 0.005), (0.005, 0.005), (0.005, 0.05), (0, 0.05)));
            Assert.Equal(RegionPoint.Outside, l.Classify(new Vec3(0.025, 0.025, 0)));
            Assert.NotEmpty(l.Witnesses);
            foreach (RegionWitness w in l.Witnesses)
            {
                Assert.Equal(RegionPoint.Inside, l.Classify(w.Point));
                Assert.True(w.Clearance >= MinClearance);
                Near(RegionGeometry.Distance(l.Segments, w.Point), w.Clearance, 1e-15);
            }
        }

        [Fact]
        public void Comb_with_twenty_teeth_gets_interior_points()
        {
            var corners = new List<(double, double)> { (0, 0), (0.1, 0), (0.1, 0.002) };
            for (int i = 19; i >= 0; i--)
            {
                double x0 = i * 0.005;
                corners.Add((x0 + 0.004, 0.002));
                corners.Add((x0 + 0.004, 0.03));
                corners.Add((x0 + 0.001, 0.03));
                corners.Add((x0 + 0.001, 0.002));
            }
            corners.Add((0, 0.002));
            ProfileRegion comb = Region(Polygon(corners.ToArray()));
            Assert.NotEmpty(comb.Witnesses);
            Assert.All(comb.Witnesses, w => Assert.Equal(RegionPoint.Inside, comb.Classify(w.Point)));
        }

        [Fact]
        public void Diamond_with_scan_lines_through_vertices()
        {
            ProfileRegion d = Region(Polygon((0, -0.01), (0.01, 0), (0, 0.01), (-0.01, 0)));
            Assert.NotEmpty(d.Witnesses);
            Assert.All(d.Witnesses, w => Assert.Equal(RegionPoint.Inside, d.Classify(w.Point)));
            Assert.Equal(RegionPoint.Outside, d.Classify(new Vec3(-0.02, 0, 0)));  // ray through two vertices
            Assert.Equal(RegionPoint.Inside, d.Classify(new Vec3(0.002, 0, 0)));   // ray through one vertex
            Assert.Equal(RegionPoint.Boundary, d.Classify(new Vec3(0.005, 0.005, 0)));
        }

        [Fact]
        public void Ring_witnesses_lie_in_the_ring_not_in_the_hole()
        {
            ProfileRegion ring = Region(Pieces(Circle(0, 0, 0.02), Circle(0, 0, 0.01)));
            ProfileRegion disc = Region(Pieces(Circle(0, 0, 0.01)));
            Assert.Equal(RegionPoint.Outside, ring.Classify(Vec3.Zero));
            Assert.NotEmpty(ring.Witnesses);
            Assert.True(ring.Clearance > 0.0049);
            foreach (RegionWitness w in ring.Witnesses)
            {
                Assert.Equal(RegionPoint.Inside, ring.Classify(w.Point));
                Assert.Equal(RegionPoint.Outside, disc.Classify(w.Point));
            }
        }

        [Fact]
        public void Three_nested_levels_form_a_partition()
        {
            var regions = new[]
            {
                Region(Pieces(Circle(0, 0, 0.03), Circle(0, 0, 0.02))),
                Region(Pieces(Circle(0, 0, 0.02), Circle(0, 0, 0.01))),
                Region(Pieces(Circle(0, 0, 0.01))),
            };
            for (int i = 0; i < regions.Length; i++)
                foreach (RegionWitness w in regions[i].Witnesses)
                    Assert.Equal(new List<int> { i }, Containing(w.Point, regions));
        }

        [Fact]
        public void Lens_and_crescents_bounded_by_the_same_two_circles_are_told_apart()
        {
            // Circles A at (-d/2, 0) and B at (d/2, 0), r = 20 mm, d = 15 mm, crossing at (0, ±h):
            // all three regions are bounded by portions of the same two circles.
            const double r = 0.02, d = 0.015;
            double h = Math.Sqrt(r * r - d * d / 4), tA = Math.Atan2(h, d / 2), tB = Math.PI - tA;
            IReadOnlyList<Vec3> aRight = Arc(-d / 2, 0, r, -tA, tA), aLeft = Arc(-d / 2, 0, r, tA, 2 * Math.PI - tA);
            IReadOnlyList<Vec3> bLeft = Arc(d / 2, 0, r, tB, 2 * Math.PI - tB), bRight = Arc(d / 2, 0, r, -tB, tB);
            var regions = new[] { Region(Pieces(aRight, bLeft)), Region(Pieces(aLeft, bLeft)), Region(Pieces(bRight, aRight)) };
            for (int i = 0; i < regions.Length; i++)
            {
                Assert.NotEmpty(regions[i].Witnesses);
                foreach (RegionWitness w in regions[i].Witnesses)
                    Assert.Equal(new List<int> { i }, Containing(w.Point, regions));
            }
            double lens = 2 * r * r * Math.Acos(d / (2 * r)) - d / 2 * Math.Sqrt(4 * r * r - d * d);
            Assert.True(Math.Abs(regions[0].Area - lens) <= 2 * Eps * regions[0].Perimeter);
        }

        [Fact]
        public void Slivers_are_accepted_at_two_and_a_half_and_rejected_at_one_and_a_half_min_clearances()
        {
            Assert.NotEmpty(Region(Rect(0, 0, 0.05, 2.5 * MinClearance)).Witnesses);
            Assert.Empty(Region(Rect(0, 0, 0.05, 1.5 * MinClearance)).Witnesses);
        }

        [Fact]
        public void Random_star_polygons_with_holes_get_witnesses_inside_whatever_their_orientation()
        {
            var rnd = new Random(7);
            for (int k = 0; k < 300; k++)
            {
                int n = rnd.Next(5, 40);
                var corners = new (double, double)[n];
                for (int i = 0; i < n; i++)
                {
                    double a = 2 * Math.PI * i / n, rr = 0.01 + 0.04 * rnd.NextDouble();
                    corners[i] = (rr * Math.Cos(a), rr * Math.Sin(a));
                }
                var pieces = Polygon(corners);
                if (rnd.Next(2) == 0) pieces.Add(Circle(0, 0, 0.004));
                ProfileRegion region = Region(pieces);
                Assert.NotEmpty(region.Witnesses);
                foreach (RegionWitness w in region.Witnesses)
                {
                    Assert.Equal(RegionPoint.Inside, region.Classify(w.Point));
                    Assert.True(w.Clearance >= MinClearance);
                }
                ProfileRegion reversed = Region(pieces.Select(p => (IReadOnlyList<Vec3>)p.Reverse().ToList()).Reverse());
                Assert.Equal(region.Witnesses.Count, reversed.Witnesses.Count);
                Near(region.Clearance, reversed.Clearance, 1e-12);
                Near(region.Area, reversed.Area, 1e-15);
            }
        }

        [Fact]
        public void Witnesses_are_spread_so_a_line_through_the_centre_leaves_some_inside_each_half()
        {
            ProfileRegion rect = Region(Rect(0, 0, 0.1, 0.01));
            IReadOnlyList<RegionWitness> ws = rect.Witnesses;
            Assert.Equal(RegionGeometry.DefaultWitnesses, ws.Count);
            for (int i = 0; i < ws.Count; i++)
                for (int j = i + 1; j < ws.Count; j++)
                    Assert.True(ws[i].Point.DistanceTo(ws[j].Point) >= 2 * MinClearance);
            Assert.True(ws.Max(w => w.Point.X) - ws.Min(w => w.Point.X) >= 0.05);

            // A stand-in line exactly through the centre, where the best witness is.
            var halves = new[] { Region(Rect(0, 0, 0.05, 0.01)), Region(Rect(0.05, 0, 0.1, 0.01)) };
            Assert.Empty(Containing(ws[0].Point, halves));
            Assert.Contains(ws, w => Containing(w.Point, halves).SequenceEqual(new[] { 0 }));
            Assert.Contains(ws, w => Containing(w.Point, halves).SequenceEqual(new[] { 1 }));
        }
    }
}
