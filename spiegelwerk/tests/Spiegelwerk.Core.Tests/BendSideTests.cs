using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>
    /// Which side of a sketched bend or jog stays (blech-plan S4, K8/K9; A3 #4–#6, A2 #11/#12): regions
    /// of the fixed face, one pick point per region away from every line, the single-line case of the
    /// jog, and the space a fixed point was read in.
    /// </summary>
    public class BendSideTests
    {
        private static readonly Vec3 Up = Vec3.UnitZ;

        /// <summary>A line in the XY plane through (x, y) along (dx, dy).</summary>
        private static Line3 L(double x, double y, double dx, double dy) => new Line3(new Vec3(x, y, 0), new Vec3(dx, dy, 0));

        private static int Regions(IReadOnlyList<Vec3> points, IReadOnlyList<Line3> lines, Vec3 n) =>
            points.Select(p => string.Join(",", BendSide.RegionOf(p, lines, n))).Distinct().Count();

        [Fact]
        public void Side_candidates_give_one_interior_point_per_region()
        {
            const double clearance = 0.002, reach = 0.05;
            var cases = new (Line3[] lines, int regions)[]
            {
                (new[] { L(0.01, 0, 0, 1) }, 2),
                (new[] { L(-0.02, 0, 0, 1), L(0.02, 0, 0, 1) }, 3),
                (new[] { L(0, 0, 1, 0), L(0, 0, 0, 1) }, 4),
                // The strip between the two lines is 3 mm wide, narrower than twice the clearance: no point there.
                (new[] { L(0, 0, 0, 1), L(0.003, 0, 0, 1) }, 2),
            };
            foreach (var (lines, regions) in cases)
            {
                List<Vec3> points = BendSide.SideCandidates(lines, Up, Vec3.Zero, clearance, reach);
                Assert.Equal(regions, points.Count);
                Assert.Equal(regions, Regions(points, lines, Up));
                foreach (Vec3 p in points)
                {
                    Assert.True(BendSide.DistanceToLines(p, lines, Up) >= clearance - 1e-12, $"{p} zu nah an einer Linie");
                    Assert.True(BendSide.RegionOf(p, lines, Up).All(s => s != 0));
                    TestGeometry.Near(0, p.Z, 1e-12);
                }
            }
        }

        [Fact]
        public void Side_candidates_keep_to_the_face_the_caller_accepts()
        {
            // A plate 100 × 50 mm, a bend line across at x = 10 mm; the face test is the caller's (the kernel in the add-in).
            var lines = new[] { L(0.01, 0, 0, 1) };
            bool OnPlate(Vec3 p) => Math.Abs(p.X) <= 0.05 && Math.Abs(p.Y) <= 0.025;
            List<Vec3> points = BendSide.SideCandidates(lines, Up, Vec3.Zero, 0.002, 0.056, OnPlate);
            Assert.Equal(2, points.Count);
            Assert.All(points, p => Assert.True(OnPlate(p)));
            // The farthest from the line first: the wide side (x < 10 mm).
            Assert.True(points[0].X < 0.01);
            // A face that accepts nothing gives nothing.
            Assert.Empty(BendSide.SideCandidates(lines, Up, Vec3.Zero, 0.002, 0.056, _ => false));
        }

        [Fact]
        public void Side_candidates_commute_with_the_reflection()
        {
            var rnd = new Random(41);
            for (int k = 0; k < 50; k++)
            {
                var r = new Reflection(new Plane3(TestGeometry.RandomPoint(rnd), TestGeometry.RandomUnit(rnd)));
                Frame3 f = TestGeometry.RandomFrame(rnd);
                Vec3 n = f.ZAxis;
                var lines = new List<Line3> { new Line3(f.ToWorld(0.01, 0), f.XAxis * 0.3 + f.YAxis) };
                if (k % 2 == 0) lines.Add(new Line3(f.ToWorld(-0.02, 0.01), f.XAxis));
                List<Vec3> original = BendSide.SideCandidates(lines, n, f.Origin, 0.002, 0.05);
                List<Line3> mirroredLines = lines.Select(r.Apply).ToList();
                // The copy's normal may be R(n) or −R(n) (a face's outward normal, a sketch's z′): regions do not care.
                Vec3 n2 = k % 3 == 0 ? -r.ApplyVector(n) : r.ApplyVector(n);
                List<Vec3> mirrored = BendSide.SideCandidates(mirroredLines, n2, r.Apply(f.Origin), 0.002, 0.05);
                Assert.Equal(original.Count, mirrored.Count);
                foreach (Vec3 c in original)
                {
                    int[] region = BendSide.RegionOf(r.Apply(c), mirroredLines, n2);
                    Assert.Single(mirrored, m => BendSide.SameRegion(BendSide.RegionOf(m, mirroredLines, n2), region));
                }
            }
        }

        [Fact]
        public void Region_of_a_point_is_invariant_under_the_reflection()
        {
            var rnd = new Random(7);
            for (int k = 0; k < 500; k++)
            {
                var r = new Reflection(new Plane3(TestGeometry.RandomPoint(rnd), TestGeometry.RandomUnit(rnd)));
                Frame3 f = TestGeometry.RandomFrame(rnd);
                var lines = new[] { new Line3(f.ToWorld(0.01, 0.02), f.YAxis), new Line3(f.ToWorld(-0.03, 0), f.XAxis + f.YAxis * 0.5) };
                Vec3 p = f.ToWorld((rnd.NextDouble() - 0.5) * 0.2, (rnd.NextDouble() - 0.5) * 0.2);
                Vec3 q = f.ToWorld((rnd.NextDouble() - 0.5) * 0.2, (rnd.NextDouble() - 0.5) * 0.2);
                bool same = BendSide.SameRegion(BendSide.RegionOf(p, lines, f.ZAxis), BendSide.RegionOf(q, lines, f.ZAxis));
                Line3[] mirrored = lines.Select(r.Apply).ToArray();
                foreach (Vec3 n2 in new[] { r.ApplyVector(f.ZAxis), -r.ApplyVector(f.ZAxis) })
                    Assert.Equal(same, BendSide.SameRegion(BendSide.RegionOf(r.Apply(p), mirrored, n2), BendSide.RegionOf(r.Apply(q), mirrored, n2)));
            }
        }

        [Fact]
        public void Side_sign_is_not_invariant_but_the_mirrored_point_is_on_its_side()
        {
            // A2 P14: (R(p − a))·(R n × R t) = −(p − a)·(n × t) – the sign flips with the mirrored normal and
            // stays with the reversed one; a test of a fixed sign would pass for one convention only.
            var rnd = new Random(3);
            for (int k = 0; k < 200; k++)
            {
                var r = new Reflection(new Plane3(TestGeometry.RandomPoint(rnd), TestGeometry.RandomUnit(rnd)));
                Frame3 f = TestGeometry.RandomFrame(rnd);
                var line = new Line3(f.Origin, f.XAxis);
                Vec3 p = f.ToWorld(0.03, 0.02 + rnd.NextDouble() * 0.02);
                int s = BendSide.RegionOf(p, new[] { line }, f.ZAxis)[0];
                Line3 ml = r.Apply(line);
                Assert.Equal(-s, BendSide.RegionOf(r.Apply(p), new[] { ml }, r.ApplyVector(f.ZAxis))[0]);
                Assert.Equal(s, BendSide.RegionOf(r.Apply(p), new[] { ml }, -r.ApplyVector(f.ZAxis))[0]);
                // The copy's pick point lies on the side of R(line) that holds R(p), whatever the sign convention.
                Vec3 pick = BendSide.PointBeside(ml, r.ApplyVector(f.ZAxis), r.Apply(p), r.Apply(f.Origin), 0.002, 0.05)!.Value;
                Assert.True(BendSide.SameRegion(BendSide.RegionOf(pick, new[] { ml }, f.ZAxis), BendSide.RegionOf(r.Apply(p), new[] { ml }, f.ZAxis)));
            }
        }

        [Fact]
        public void PointBeside_is_the_single_line_case_of_SideCandidates()
        {
            var line = L(0.01, 0, 0, 1);
            List<Vec3> both = BendSide.SideCandidates(new[] { line }, Up, Vec3.Zero, 0.002, 0.05);
            Assert.Equal(2, both.Count);
            Vec3 left = BendSide.PointBeside(line, Up, new Vec3(-0.01, 0.003, 0), Vec3.Zero, 0.002, 0.05)!.Value;
            Vec3 right = BendSide.PointBeside(line, Up, new Vec3(0.04, -0.01, 0), Vec3.Zero, 0.002, 0.05)!.Value;
            Assert.Contains(left, both);
            Assert.Contains(right, both);
            Assert.True(left.X < 0.01 && right.X > 0.01);
            // At least the clearance from the line, far from it where the face allows (a quarter of the face is a preference, not a rule).
            Assert.True(Math.Abs(left.X - 0.01) >= 0.002 && Math.Abs(right.X - 0.01) >= 0.002);
            // A side point on the line names no side.
            Assert.Null(BendSide.PointBeside(line, Up, new Vec3(0.01, 0.02, 0), Vec3.Zero, 0.002, 0.05));
        }

        [Fact]
        public void Pick_order_starts_in_the_region_of_the_fixed_point()
        {
            var lines = new[] { L(0, 0, 0, 1), L(0.03, 0, 0, 1) };
            // Far enough from both lines: the point itself first.
            List<Vec3> order = BendSide.PickOrder(new Vec3(0.015, 0.01, 0), lines, Up, Vec3.Zero, 0.002, 0.05);
            Assert.Equal(new Vec3(0.015, 0.01, 0), order[0]);
            Assert.Equal(3, order.Count);
            // Close to a line (CADapter: a pick on the line turns half the sheet): that region's candidate instead, still first.
            order = BendSide.PickOrder(new Vec3(0.0005, 0.01, 0), lines, Up, Vec3.Zero, 0.002, 0.05);
            Assert.Equal(3, order.Count);
            Assert.True(BendSide.SameRegion(BendSide.RegionOf(order[0], lines, Up), BendSide.RegionOf(new Vec3(0.0005, 0.01, 0), lines, Up)));
            Assert.True(BendSide.DistanceToLines(order[0], lines, Up) >= 0.002);
            // Without a fixed point, just the candidates.
            Assert.Equal(3, BendSide.PickOrder(null, lines, Up, Vec3.Zero, 0.002, 0.05).Count);
        }

        [Fact]
        public void Fixed_point_prefers_the_documented_sketch_reading()
        {
            var lines = new[] { L(0.05, 0, 0, 1) };
            Func<Vec3, int[]> region = p => BendSide.RegionOf(p, lines, Up);
            Vec3 a = new Vec3(0.02, 0.01, 0), b = new Vec3(0.08, 0.01, 0), c = new Vec3(0.03, 0.02, 0);
            const double tol = BendSide.OnFaceTolerance;
            // Only the sketch reading lies on the face.
            FixedPointChoice x = BendSide.ChooseFixedPoint(FixedPointSpace.Sketch, a, 0, b, 0.002, tol, region);
            Assert.Equal((a, FixedPointSpace.Sketch, false), (x.Point!.Value, x.Space, x.Ambiguous));
            // Only the model reading.
            x = BendSide.ChooseFixedPoint(FixedPointSpace.Sketch, a, 0.002, b, 0, tol, region);
            Assert.Equal((b, FixedPointSpace.Model, false), (x.Point!.Value, x.Space, x.Ambiguous));
            // Neither.
            x = BendSide.ChooseFixedPoint(FixedPointSpace.Sketch, a, 0.002, b, 0.002, tol, region);
            Assert.Null(x.Point);
            Assert.Equal(FixedPointSpace.Unknown, x.Space);
            // Both, on the same side: the documented one, not ambiguous.
            x = BendSide.ChooseFixedPoint(FixedPointSpace.Sketch, a, 0, c, 0, tol, region);
            Assert.Equal((a, FixedPointSpace.Sketch, false), (x.Point!.Value, x.Space, x.Ambiguous));
            // Both, on different sides: the documented one, ambiguous.
            x = BendSide.ChooseFixedPoint(FixedPointSpace.Sketch, a, 0, b, 0, tol, region);
            Assert.Equal((a, FixedPointSpace.Sketch, true), (x.Point!.Value, x.Space, x.Ambiguous));
        }

        [Fact]
        public void Fixed_point_space_prefers_the_documented_reading_and_reports_ambiguity()
        {
            var lines = new[] { L(0.05, 0, 0, 1) };
            Func<Vec3, int[]> region = p => BendSide.RegionOf(p, lines, Up);
            Vec3 left = new Vec3(0.02, 0.01, 0), right = new Vec3(0.08, 0.01, 0);
            // The sketched bend prefers the sketch reading (help), the jog the model reading (A2's evidence).
            FixedPointChoice bend = BendSide.ChooseFixedPoint(FixedPointSpace.Sketch, left, 0, right, 0, BendSide.OnFaceTolerance, region);
            FixedPointChoice jog = BendSide.ChooseFixedPoint(FixedPointSpace.Model, left, 0, right, 0, BendSide.OnFaceTolerance, region);
            Assert.Equal(left, bend.Point!.Value);
            Assert.Equal(right, jog.Point!.Value);
            Assert.True(bend.Ambiguous && jog.Ambiguous);
            // The same point read both ways is no ambiguity, with or without regions.
            FixedPointChoice same = BendSide.ChooseFixedPoint(FixedPointSpace.Model, left, 0, left, 0, BendSide.OnFaceTolerance);
            Assert.False(same.Ambiguous);
            Assert.Equal(FixedPointSpace.Model, same.Space);
        }

        [Fact]
        public void Clearance_is_twice_the_thickness_but_at_least_a_millimetre()
        {
            TestGeometry.Near(0.001, BendSide.Clearance(0));
            TestGeometry.Near(0.001, BendSide.Clearance(0.0004));
            TestGeometry.Near(0.004, BendSide.Clearance(0.002));
        }
    }
}
