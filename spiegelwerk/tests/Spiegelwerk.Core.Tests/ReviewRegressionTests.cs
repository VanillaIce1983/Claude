using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;
using Spiegelwerk.Core.Verification;
using Xunit;
using static Spiegelwerk.Core.Tests.TestGeometry;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>Cases from the code review: closed edges, cancelling mass changes, angle tolerances, the delete gate.</summary>
    public class ReviewRegressionTests
    {
        // ------------------------------------------------------------ closed edges

        private static EdgeSignature FullCircle(Vec3 c, double r, Vec3 axis, double seamAngle)
        {
            Vec3 u = axis.AnyPerpendicular(), v = axis.Cross(u);
            Vec3 P(double a) => c + u * (r * Math.Cos(a)) + v * (r * Math.Sin(a));
            Vec3 seam = P(seamAngle);
            return new EdgeSignature(CurveKind.Circle, seam, seam, P(seamAngle + Math.PI), 2 * Math.PI * r) { Center = c, Radius = r, Axis = axis };
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(Math.PI / 2)]
        [InlineData(Math.PI)]
        public void Full_circle_matches_whatever_the_seam(double seamShift)
        {
            var r = new Reflection(Plane3.RightYZ);
            var c = new Vec3(0.03, 0.01, 0.02);
            EdgeSignature original = FullCircle(c, 0.004, Vec3.UnitZ, 0.3);
            EdgeSignature rebuilt = FullCircle(r.Apply(c), 0.004, Vec3.UnitZ, 1.1 + seamShift);

            Assert.True(TopologyMatcher.FindEdge(original.Mirror(r), new[] { rebuilt }).Found);
        }

        [Fact]
        public void Full_circle_does_not_match_another_radius_axis_or_a_half_circle()
        {
            var c = new Vec3(0.01, 0, 0);
            EdgeSignature wanted = FullCircle(c, 0.004, Vec3.UnitZ, 0);
            Assert.False(TopologyMatcher.FindEdge(wanted, new[] { FullCircle(c, 0.0041, Vec3.UnitZ, 0) }).Found);
            Assert.False(TopologyMatcher.FindEdge(wanted, new[] { FullCircle(c, 0.004, Vec3.UnitX, 0) }).Found);

            var half = new EdgeSignature(CurveKind.Circle, c + new Vec3(0.004, 0, 0), c - new Vec3(0.004, 0, 0), c + new Vec3(0, 0.004, 0), Math.PI * 0.004)
            {
                Center = c, Radius = 0.004, Axis = Vec3.UnitZ,
            };
            Assert.True(double.IsPositiveInfinity(wanted.DistanceTo(half)));
        }

        [Fact]
        public void Closed_spline_needs_the_kernel_to_decide()
        {
            var a = new EdgeSignature(CurveKind.Other, Vec3.Zero, Vec3.Zero, new Vec3(0.01, 0, 0), 0.05);
            var b = new EdgeSignature(CurveKind.Other, new Vec3(0, 0.01, 0), new Vec3(0, 0.01, 0), new Vec3(0.02, 0, 0), 0.05);
            Assert.False(TopologyMatcher.FindEdge(a, new[] { b }).Found);
            Assert.True(TopologyMatcher.FindEdge(a, new[] { b }, null, (i, p) => 0).Found);
            Assert.False(TopologyMatcher.FindEdge(a, new[] { b }, null, (i, p) => 1e-3).Found);
        }

        // ------------------------------------------------------------ mass comparison

        [Fact]
        public void Change_with_zero_net_volume_is_judged_by_its_moment()
        {
            // Fillet adding material at a concave edge (x = +0.02) and removing as much at a convex one (x = -0.02).
            double dv = 4e-8;
            MassState right = MassState.FromBodies(new[] { (dv, new Vec3(0.02, 0, 0)), (-dv, new Vec3(-0.02, 0, 0)) });
            MassState wrong = MassState.FromBodies(new[] { (dv, new Vec3(-0.02, 0, 0)), (-dv, new Vec3(0.02, 0, 0)) });
            var total = new MassState(1e-4, new Vec3(0, 0, 1e-4 * 0.01));

            Assert.True(MassComparison.Compare(right, right, total).Passes);
            Assert.False(MassComparison.Compare(right, wrong, total).Passes);
        }

        [Fact]
        public void Kernel_noise_below_the_part_level_floor_passes()
        {
            var total = new MassState(1e-4, new Vec3(0.005, 0, 0) * 1e-4);
            var expected = new MassState(3e-14, new Vec3(1e-11, 0, 0));
            var actual = new MassState(-2e-14, new Vec3(1e-11, 0, 0));
            Assert.True(MassComparison.Compare(expected, actual, total).Passes);
        }

        [Fact]
        public void Cuts_compare_like_bosses()
        {
            var cut = MassState.FromBodies(new[] { (-2e-6, new Vec3(0.01, 0.02, 0)) });
            var wrongSide = MassState.FromBodies(new[] { (-2e-6, new Vec3(0.01, -0.02, 0)) });
            Assert.True(MassComparison.Compare(cut, cut).Passes);
            MassComparison cmp = MassComparison.Compare(cut, wrongSide);
            Assert.False(cmp.Passes);
            Near(0.04, cmp.CentroidError, 1e-12);
        }

        // ------------------------------------------------------------ angle tolerances

        [Fact]
        public void Slightly_tilted_plane_is_not_taken_for_a_standard_plane()
        {
            double tilt = 0.02 * Math.PI / 180;
            var tilted = new Plane3(Vec3.Zero, new Vec3(0, Math.Sin(tilt), Math.Cos(tilt)));
            Plane3[] standard = { Plane3.FrontXY, Plane3.TopXZ, Plane3.RightYZ };
            Assert.Equal(PlaneMatchKind.ThreePoints, PlaneClassifier.Classify(tilted, standard, standard).Kind);
        }

        [Fact]
        public void Slightly_oblique_mirror_drops_horizontal_instead_of_distorting()
        {
            var s = new SketchModel { Frame = Front };
            s.Points.Add(new SkPoint("a", 0.01, 0));
            s.Points.Add(new SkPoint("b", 0.06, 0));
            s.Segments.Add(new SkSegment("L", SkSegmentKind.Line) { StartId = "a", EndId = "b" });
            s.Relations.Add(new SkRelation(RelationKind.Horizontal, new[] { SkRef.LocalSegment("L") }));

            SketchMirrorResult res = SketchMirror.Mirror(s, new Reflection(new Plane3(Vec3.Zero, new Vec3(1, 2e-4, 0))), Front);
            Assert.Equal(AxisImage.Oblique, res.Map.X);
            Assert.Empty(res.Sketch.Relations);
            Assert.Single(res.Warnings);
        }

        [Fact]
        public void Target_plane_tilted_by_a_hundredth_degree_is_rejected()
        {
            var s = new SketchModel { Frame = Front };
            s.Points.Add(new SkPoint("far", 0.1, 0));
            double tilt = 0.01 * Math.PI / 180;
            var target = new Frame3(Vec3.Zero, new Vec3(Math.Cos(tilt), 0, Math.Sin(tilt)), Vec3.UnitY);
            Assert.Throws<ArgumentException>(() => SketchMirror.Mirror(s, new Reflection(Plane3.RightYZ), target));
        }

        [Fact]
        public void Two_planes_meet_in_a_line_on_both()
        {
            var rnd = new Random(31);
            for (int i = 0; i < 50; i++)
            {
                var a = new Plane3(RandomPoint(rnd), RandomUnit(rnd));
                var b = new Plane3(RandomPoint(rnd), RandomUnit(rnd));
                Line3? l = a.IntersectWith(b);
                Assert.NotNull(l);
                foreach (double t in new[] { -0.3, 0, 0.7 })
                {
                    Vec3 q = l!.Value.Point + l.Value.Direction * t;
                    Assert.True(a.Contains(q, 1e-9) && b.Contains(q, 1e-9));
                }
            }
            Assert.Null(Plane3.FrontXY.IntersectWith(new Plane3(new Vec3(0, 0, 0.01), -Vec3.UnitZ)));
        }

        [Fact]
        public void Report_text_for_a_change_without_net_volume_shows_the_moment()
        {
            double dv = 4e-8;
            MassState right = MassState.FromBodies(new[] { (dv, new Vec3(0.02, 0, 0)), (-dv, new Vec3(-0.02, 0, 0)) });
            MassState wrong = MassState.FromBodies(new[] { (dv, new Vec3(-0.02, 0, 0)), (-dv, new Vec3(0.02, 0, 0)) });
            MassComparison cmp = MassComparison.Compare(right, wrong, new MassState(1e-4, Vec3.Zero));
            Assert.True(cmp.NoNetVolume);
            Assert.Contains("ohne Nettovolumen", cmp.ToString());
        }

        // ------------------------------------------------------------ report

        private static FeatureRecord Rec(string name) => new UnsupportedRecord(name, "X", "", false);

        [Fact]
        public void Skipped_features_without_equivalent_block_deleting_the_original()
        {
            var report = new MirrorReport();
            report.Add(Rec("Skizze1"), ReportStatus.Ok, "");
            report.Add(Rec("Achse1"), ReportStatus.Skipped, "");
            Assert.False(report.HasProblems);
            Assert.False(report.AllMirrored);
            Assert.Equal(new[] { "Achse1" }, report.NotMirrored.Select(e => e.Feature));

            report.Entries[1].HasEquivalent = true;
            Assert.True(report.AllMirrored);

            report.Add(Rec("Fase1"), ReportStatus.Warning, "");
            Assert.False(report.AllMirrored);
            // The explanation must name everything that blocks, warnings included.
            Assert.Equal(new[] { "Fase1" }, report.Blocking.Select(e => e.Feature));
        }
    }
}
