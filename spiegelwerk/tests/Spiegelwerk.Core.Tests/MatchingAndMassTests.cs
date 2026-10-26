using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Verification;
using Xunit;
using static Spiegelwerk.Core.Tests.TestGeometry;

namespace Spiegelwerk.Core.Tests
{
    public class MatchingAndMassTests
    {
        private static EdgeSignature HalfCircle(Vec3 center, double r, bool upper)
        {
            Vec3 a = center + new Vec3(r, 0, 0), b = center - new Vec3(r, 0, 0);
            Vec3 mid = center + new Vec3(0, upper ? r : -r, 0);
            return new EdgeSignature(CurveKind.Circle, a, b, mid, Math.PI * r) { Center = center, Radius = r, Axis = Vec3.UnitZ };
        }

        [Fact]
        public void Two_half_circles_with_the_same_endpoints_are_told_apart()
        {
            var r = new Reflection(Plane3.RightYZ);
            var c = new Vec3(0.04, 0.01, 0.02);
            EdgeSignature upperOriginal = HalfCircle(c, 0.005, upper: true);

            // The rebuilt body: both halves of the mirrored hole, plus an unrelated line.
            var mc = r.Apply(c);
            var candidates = new List<EdgeSignature>
            {
                new EdgeSignature(CurveKind.Line, Vec3.Zero, Vec3.UnitX * 0.01, Vec3.UnitX * 0.005, 0.01) { Axis = Vec3.UnitX },
                HalfCircle(mc, 0.005, upper: false),
                HalfCircle(mc, 0.005, upper: true),
            };

            MatchResult m = TopologyMatcher.FindEdge(upperOriginal.Mirror(r), candidates);
            Assert.Equal(2, m.Index);
            Assert.False(m.Ambiguous);
        }

        [Fact]
        public void Reversed_edges_match_and_wrong_lengths_do_not()
        {
            var line = new EdgeSignature(CurveKind.Line, new Vec3(0, 0, 0), new Vec3(0.02, 0, 0), new Vec3(0.01, 0, 0), 0.02) { Axis = Vec3.UnitX };
            var reversed = new EdgeSignature(CurveKind.Line, new Vec3(0.02, 0, 0), new Vec3(0, 0, 0), new Vec3(0.01, 0, 0), 0.02) { Axis = -Vec3.UnitX };
            var longer = new EdgeSignature(CurveKind.Line, new Vec3(0, 0, 0), new Vec3(0.03, 0, 0), new Vec3(0.015, 0, 0), 0.03) { Axis = Vec3.UnitX };
            Assert.True(line.DistanceTo(reversed) < 1e-12);
            Assert.True(double.IsPositiveInfinity(line.DistanceTo(longer)));
        }

        [Fact]
        public void Open_splines_with_the_same_ends_are_told_apart_on_the_curve()
        {
            // Two arcs standing in for splines: same ends, same length, bulging to opposite sides.
            const double R = 0.0425;
            var upCentre = new Vec3(0.02, -0.0375, 0);
            var downCentre = new Vec3(0.02, 0.0375, 0);
            Vec3 OnUp(double x) => new Vec3(x, upCentre.Y + Math.Sqrt(R * R - (x - 0.02) * (x - 0.02)), 0);
            Vec3 OnDown(double x) => new Vec3(x, downCentre.Y - Math.Sqrt(R * R - (x - 0.02) * (x - 0.02)), 0);
            Vec3 a = Vec3.Zero, b = new Vec3(0.04, 0, 0);

            var wanted = new EdgeSignature(CurveKind.Other, a, b, OnUp(0.02), 0.0416);
            var candidates = new[]
            {
                new EdgeSignature(CurveKind.Other, a, b, OnDown(0.02), 0.0416),
                // Run the other way, and the kernel's middle parameter is not the geometric middle.
                new EdgeSignature(CurveKind.Other, b, a, OnUp(0.026), 0.0416),
            };
            Vec3[] centres = { downCentre, upCentre };

            // Without the kernel only ends and length count: both pass.
            Assert.Equal(2, TopologyMatcher.FindEdge(wanted, candidates).Candidates);

            // Stands in for the kernel: distance of a point to candidate curve i.
            double OnCurve(int i, Vec3 p) => Math.Abs(p.DistanceTo(centres[i]) - R);
            MatchResult m = TopologyMatcher.FindEdge(wanted, candidates, null, OnCurve);
            Assert.Equal(1, m.Index);
            Assert.False(m.Ambiguous);
        }

        [Fact]
        public void Preferred_candidates_win_ties()
        {
            var e = new EdgeSignature(CurveKind.Line, Vec3.Zero, Vec3.UnitY * 0.01, Vec3.UnitY * 0.005, 0.01);
            var list = new[] { e, e };
            Assert.Equal(0, TopologyMatcher.FindEdge(e, list).Index);
            Assert.Equal(1, TopologyMatcher.FindEdge(e, list, i => i == 1).Index);
            Assert.True(TopologyMatcher.FindEdge(e, list).Ambiguous);
        }

        [Fact]
        public void Face_match_uses_kind_area_normal_and_the_kernel_distance()
        {
            var r = new Reflection(Plane3.RightYZ);
            var face = new FaceSignature(SurfaceKind.Plane, 1e-4, new Vec3(0.02, 0.01, 0)) { Normal = Vec3.UnitX };
            FaceSignature wanted = face.Mirror(r);
            Near(-Vec3.UnitX, wanted.Normal);

            var candidates = new[]
            {
                new FaceSignature(SurfaceKind.Plane, 1e-4, new Vec3(-0.02, 0.01, 0)) { Normal = Vec3.UnitX },
                new FaceSignature(SurfaceKind.Plane, 1e-4, new Vec3(-0.02, 0.01, 0)) { Normal = -Vec3.UnitX },
                new FaceSignature(SurfaceKind.Cylinder, 1e-4, new Vec3(-0.02, 0.01, 0)),
            };
            MatchResult m = TopologyMatcher.FindFace(wanted, candidates, i => wanted.Sample.DistanceTo(candidates[i].Sample));
            Assert.Equal(1, m.Index);
        }

        [Fact]
        public void Mirrored_mass_state_equals_mirroring_every_body()
        {
            var rnd = new Random(23);
            for (int i = 0; i < 50; i++)
            {
                var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
                var bodies = Enumerable.Range(0, 4).Select(_ => (rnd.NextDouble() * 1e-5, RandomPoint(rnd))).ToList();
                MassState original = MassState.FromBodies(bodies);
                MassState mirrored = MassState.FromBodies(bodies.Select(b => (b.Item1, r.Apply(b.Item2))));

                MassComparison cmp = MassComparison.Compare(mirrored, original.Mirror(r));
                Assert.True(cmp.Passes, cmp.ToString());
            }
        }

        [Fact]
        public void Mass_comparison_detects_a_feature_built_the_wrong_way()
        {
            // A 10x10x10 mm block extruded up instead of down.
            var up = MassState.FromBodies(new[] { (1e-6, new Vec3(0, 0, 0.005)) });
            var down = MassState.FromBodies(new[] { (1e-6, new Vec3(0, 0, -0.005)) });
            MassComparison wrong = MassComparison.Compare(up, down);
            Assert.False(wrong.Passes);
            Near(0.01, wrong.CentroidError, 1e-12);
            Assert.True(MassComparison.Compare(up, up).Score < wrong.Score);
        }

        [Fact]
        public void Report_text_names_the_volume_and_centroid_deviation()
        {
            // 10 % too much volume, centroid 10 mm off.
            var expected = new MassState(1e-6, new Vec3(0, 0, 0.005) * 1e-6);
            var actual = new MassState(1.1e-6, new Vec3(0, 0, 0.015) * 1e-6);
            MassComparison cmp = MassComparison.Compare(expected, actual);
            Assert.False(cmp.Passes);
            Assert.False(cmp.NoNetVolume);

            CultureInfo saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                Assert.Equal("Abweichung: Volumen 10.000 %, Schwerpunkt 10 mm", cmp.ToString());
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
            Assert.Equal("Volumen und Schwerpunkt stimmen", MassComparison.Compare(expected, expected).ToString());
        }

        [Fact]
        public void Mass_changes_are_differences_of_states()
        {
            var before = MassState.FromBodies(new[] { (2e-6, new Vec3(0.01, 0, 0)) });
            var after = MassState.FromBodies(new[] { (2e-6, new Vec3(0.01, 0, 0)), (1e-6, new Vec3(0, 0.02, 0)) });
            MassState change = after - before;
            Near(1e-6, change.Volume, 1e-18);
            Near(new Vec3(0, 0.02e-6, 0), change.Moment, 1e-18);
        }
    }
}
