using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Sketch;
using Xunit;
using static Spiegelwerk.Core.Tests.RegionShapes;
using static Spiegelwerk.Core.Tests.TestGeometry;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>
    /// Selected sketch regions found again in the mirrored sketch: witnesses, the two-sided
    /// identity test, cover, merge, the containing region – and the read-back of a created feature.
    /// Sketches of 80 × 20 mm on the front plane, mirrored at the right plane unless said otherwise.
    /// </summary>
    public class RegionMatcherTests
    {
        private static readonly Reflection AtRight = new Reflection(Plane3.RightYZ);

        /// <summary>Front plane seen from behind: same plane, normal −Z (σ = −1 for a front sketch mirrored at the right plane).</summary>
        private static readonly Frame3 FrontFlipped = new Frame3(Vec3.Zero, new Vec3(-1, 0, 0), Vec3.UnitY);

        // Rectangle −40…40 × 0…20 mm with a centerline at x = 0: two regions.
        private static ProfileRegion LeftHalf => Region(Rect(-0.04, 0, 0, 0.02));
        private static ProfileRegion RightHalf => Region(Rect(0, 0, 0.04, 0.02));

        private static ProfileRegion MirrorToFront(ProfileRegion source) => Mirrored(source, Front, AtRight, Front);

        private static RegionMatch Match(ProfileRegion source, bool allowCover, params ProfileRegion[] targets) =>
            RegionMatcher.Match(new[] { MirrorToFront(source) }, targets, allowCover);

        // ------------------------------------------------------------ exact

        [Fact]
        public void Left_half_of_a_rectangle_with_a_centerline_becomes_the_right_half_for_sigma_plus_one()
        {
            Assert.Equal(1, AxisMap.Compute(Front, AtRight, Front).Sigma);
            // The copy, read in its own sketch coordinates: the same two halves.
            RegionMatch m = Match(LeftHalf, true, LeftHalf, RightHalf);
            Assert.True(m.Success, m.Reason);
            Assert.Equal(new[] { 1 }, m.Indices);
            Assert.Empty(m.Notes);
            Assert.False(m.Inexact);
            Assert.True(m.Witnesses[0].X > 0);
        }

        [Fact]
        public void Left_half_of_a_rectangle_with_a_centerline_becomes_the_right_half_for_sigma_minus_one()
        {
            Assert.Equal(-1, AxisMap.Compute(Front, AtRight, FrontFlipped).Sigma);
            // Target sketch x runs along model −X: its region at local x < 0 is the model's right half.
            var targets = new[] { Region(Rect(-0.04, 0, 0, 0.02)), Region(Rect(0, 0, 0.04, 0.02)) };
            RegionMatch m = RegionMatcher.Match(new[] { Mirrored(LeftHalf, Front, AtRight, FrontFlipped) }, targets, true);
            Assert.True(m.Success, m.Reason);
            Assert.Equal(new[] { 0 }, m.Indices);
            Assert.Empty(m.Notes);
            Assert.True(FrontFlipped.ToWorld(m.Witnesses[0].X, m.Witnesses[0].Y).X > 0);
        }

        [Fact]
        public void Ring_and_disc_are_told_apart()
        {
            ProfileRegion ring = Region(Pieces(Circle(0, 0.03, 0.02), Circle(0, 0.03, 0.01)));
            ProfileRegion disc = Region(Pieces(Circle(0, 0.03, 0.01)));
            Assert.Equal(new[] { 1 }, Match(ring, true, disc, ring).Indices);
            Assert.Equal(new[] { 0 }, Match(disc, true, disc, ring).Indices);
            Assert.Empty(Match(ring, true, disc, ring).Notes);
        }

        [Fact]
        public void Adjacent_pair_selects_both_regions()
        {
            RegionMatch m = RegionMatcher.Match(new[] { MirrorToFront(LeftHalf), MirrorToFront(RightHalf) }, new[] { LeftHalf, RightHalf }, true);
            Assert.True(m.Success, m.Reason);
            Assert.Equal(new[] { 0, 1 }, m.Indices.OrderBy(i => i));
            Assert.Empty(m.Notes);
            Assert.Equal(2, m.Witnesses.Count);
        }

        [Fact]
        public void Random_regions_match_their_independently_sampled_copy_exactly_for_random_frames()
        {
            var rnd = new Random(23);
            var sigmas = new HashSet<int>();
            for (int i = 0; i < 60; i++)
            {
                Frame3 src = RandomFrame(rnd);
                var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
                Plane3 mirrored = r.Apply(src.Plane);
                Vec3 zt = rnd.Next(2) == 0 ? mirrored.Normal : -mirrored.Normal;
                Vec3 xt = zt.AnyPerpendicular();
                var target = new Frame3(mirrored.Project(RandomPoint(rnd)), xt, zt.Cross(xt));
                sigmas.Add(AxisMap.Compute(src, r, target).Sigma);

                // A star polygon with a circular hole, in source sketch coordinates.
                int n = rnd.Next(5, 20);
                var corners = new (double x, double y)[n];
                for (int k = 0; k < n; k++)
                {
                    double a = 2 * Math.PI * k / n, rr = 0.02 + 0.03 * rnd.NextDouble();
                    corners[k] = (rr * Math.Cos(a), rr * Math.Sin(a));
                }
                const double hole = 0.006;
                var sourcePieces = Polygon(corners);
                sourcePieces.Add(Circle(0, 0, hole));
                ProfileRegion source = Region(sourcePieces);

                // The copy as SOLIDWORKS would hand it over: corners mirrored into the target sketch,
                // starting at another corner, the hole's seam elsewhere.
                Vec3 Map(double x, double y) => SketchMirror.MapPoint(src, r, target, new Vec3(x, y, 0));
                int shift = rnd.Next(n);
                var copyCorners = Enumerable.Range(0, n).Select(k => Map(corners[(k + shift) % n].x, corners[(k + shift) % n].y))
                    .Select(p => (p.X, p.Y)).ToArray();
                Vec3 c = Map(0, 0);
                double seam = rnd.NextDouble() * 2 * Math.PI;
                var copyPieces = Polygon(copyCorners);
                copyPieces.Add(Arc(c.X, c.Y, hole, seam, seam + 2 * Math.PI));
                ProfileRegion copy = Region(copyPieces);
                ProfileRegion inHole = Region(Pieces(Arc(c.X, c.Y, hole, seam, seam + 2 * Math.PI)));
                ProfileRegion beside = Region(Rect(c.X + 0.06, c.Y, c.X + 0.08, c.Y + 0.02));

                RegionMatch m = RegionMatcher.Match(new[] { Mirrored(source, src, r, target) }, new[] { inHole, beside, copy }, true);
                Assert.True(m.Success, m.Reason);
                Assert.Equal(new[] { 2 }, m.Indices);
                Assert.Empty(m.Notes);
                Assert.Null(RegionMatcher.CheckUsed(m.Witnesses, new[] { copy }));
            }
            Assert.Equal(new[] { -1, 1 }, sigmas.OrderBy(s => s));
        }

        // ------------------------------------------------------------ cover, merge, mismatch

        [Fact]
        public void Region_split_by_an_extra_line_in_the_copy_is_covered_by_its_parts_with_a_note()
        {
            // The right half mirrors to −40…0; in the copy a line at x = −10 splits it.
            var targets = new[] { Region(Rect(-0.04, 0, -0.01, 0.02)), Region(Rect(-0.01, 0, 0, 0.02)), RightHalf };
            RegionMatch m = Match(RightHalf, true, targets);
            Assert.True(m.Success, m.Reason);
            Assert.Equal(new[] { 0, 1 }, m.Indices.OrderBy(i => i));
            Assert.Contains("geteilt", Assert.Single(m.Notes));
            Assert.Empty(m.Deviations);
            Assert.False(m.Inexact);

            // Thin: a split would add an inner wall – no cover.
            RegionMatch thin = Match(RightHalf, false, targets);
            Assert.False(thin.Success);
            Assert.Contains("dünnwandig", thin.Reason);
        }

        [Fact]
        public void Stand_in_exactly_through_the_centre_still_finds_the_halves_through_the_other_witnesses()
        {
            // The best witness is the centre (x = −20 mirrored), exactly where SketchBuilder puts a stand-in line.
            ProfileRegion source = MirrorToFront(RightHalf);
            Assert.Equal(-0.02, source.Witnesses[0].Point.X, 9);
            var targets = new[] { Region(Rect(-0.04, 0, -0.02, 0.02)), Region(Rect(-0.02, 0, 0, 0.02)) };
            Assert.Equal(RegionPoint.Boundary, targets[0].Classify(source.Witnesses[0].Point));
            RegionMatch m = RegionMatcher.Match(new[] { source }, targets, true);
            Assert.True(m.Success, m.Reason);
            Assert.Equal(new[] { 0, 1 }, m.Indices.OrderBy(i => i));
            Assert.Single(m.Notes);
        }

        [Fact]
        public void Sixty_percent_part_of_a_split_sliver_never_passes_as_the_same_region()
        {
            // 0.12 × 100 mm, ε = 10 µm; the copy splits it at 60 %. By area alone (2ε·(Ps + Pt)) the part passed as equal.
            ProfileRegion sliver = Region(Rect(0, 0, 0.1, 0.00012));
            Assert.NotEmpty(sliver.Witnesses);
            var targets = new[] { Region(Rect(-0.1, 0, -0.04, 0.00012)), Region(Rect(-0.04, 0, 0, 0.00012)) };
            foreach (bool cover in new[] { true, false })
            {
                RegionMatch m = Match(sliver, cover, targets);
                Assert.False(m.Success && m.Notes.Count == 0, "Teil des Streifens als derselbe Bereich angenommen");
                Assert.False(m.Success && m.Indices.Count == 1);
            }
        }

        [Fact]
        public void Two_selected_regions_merged_in_the_copy_select_the_merged_one_once_with_a_note()
        {
            // The copy lost the centerline: one region where the original had two, both selected.
            ProfileRegion whole = Region(Rect(-0.04, 0, 0.04, 0.02));
            RegionMatch m = RegionMatcher.Match(new[] { MirrorToFront(LeftHalf), MirrorToFront(RightHalf) }, new[] { whole }, true);
            Assert.True(m.Success, m.Reason);
            Assert.Equal(new[] { 0 }, m.Indices);
            Assert.Contains("einziger Bereich", Assert.Single(m.Notes));
            Assert.False(m.Inexact);
        }

        [Fact]
        public void A_larger_containing_region_is_selected_as_inexact_with_both_areas()
        {
            // Only the left half was selected, the copy lost the centerline.
            ProfileRegion whole = Region(Rect(-0.04, 0, 0.04, 0.02));
            RegionMatch m = Match(LeftHalf, true, whole);
            Assert.True(m.Success, m.Reason);
            Assert.True(m.Inexact);
            Assert.Equal(new[] { 0 }, m.Indices);
            string note = Assert.Single(m.Notes);
            Assert.Contains("800", note);
            Assert.Contains("1600", note);
            Assert.Contains("gewählt ist", note);
            // For a refused feature: the same reason without the selection that would not happen.
            string deviation = Assert.Single(m.Deviations);
            Assert.Contains("800", deviation);
            Assert.Contains("1600", deviation);
            Assert.DoesNotContain("gewählt ist", deviation);
            Assert.EndsWith(".", deviation);
        }

        [Fact]
        public void A_larger_region_with_an_island_beside_the_selected_one_still_contains_it()
        {
            // The copy lost the centerline and has a circle in the other half: still larger and containing.
            ProfileRegion whole = Region(Rect(-0.04, 0, 0.04, 0.02), Pieces(Circle(-0.02, 0.01, 0.004)));
            RegionMatch m = Match(LeftHalf, false, whole, Region(Pieces(Circle(-0.02, 0.01, 0.004))));
            Assert.True(m.Success, m.Reason);
            Assert.True(m.Inexact);
            Assert.Equal(new[] { 0 }, m.Indices);
            Assert.Single(m.Deviations);
        }

        [Fact]
        public void A_region_with_an_island_or_notch_inside_the_selected_one_is_smaller_and_no_match_for_a_thin_feature()
        {
            // An island the source's witnesses miss: the copy's region holds all of the source's
            // boundary, but lacks the island – smaller, never "the region it lies in".
            ProfileRegion disc = Region(Pieces(Circle(0, 0, 0.01)));
            ProfileRegion island = Region(Pieces(Circle(0.0087, 0, 0.0008)));
            ProfileRegion holed = Region(Pieces(Circle(0, 0, 0.01)), Pieces(Circle(0.0087, 0, 0.0008)));
            Assert.All(MirrorToFront(disc).Witnesses, w => Assert.Equal(RegionPoint.Outside, island.Classify(w.Point)));
            RegionMatch thin = Match(disc, false, holed, island);
            Assert.False(thin.Success);
            Assert.Contains("kleiner", thin.Reason);
            Assert.Empty(thin.Notes);
            Assert.Empty(thin.Deviations);
            Assert.Empty(thin.Indices);
            Assert.False(thin.Inexact);
            // Not thin: the island has witnesses of its own, the two pieces compose the disc exactly.
            RegionMatch cover = Match(disc, true, holed, island);
            Assert.True(cover.Success, cover.Reason);
            Assert.False(cover.Inexact);
            Assert.Equal(new[] { 0, 1 }, cover.Indices.OrderBy(i => i));

            // Larger elsewhere, with the island inside the source: not containing either.
            ProfileRegion wider = Region(Pieces(Circle(0, 0, 0.012)), Pieces(Circle(0.0087, 0, 0.0008)));
            RegionMatch both = Match(disc, false, wider, island);
            Assert.False(both.Success);
            Assert.Contains("kleiner", both.Reason);

            // A notch in the bottom edge of the right half (mirrored to −40…0).
            ProfileRegion notched = Region(Polygon((-0.04, 0), (-0.0235, 0), (-0.0235, 0.003), (-0.0215, 0.003), (-0.0215, 0),
                (0, 0), (0, 0.02), (-0.04, 0.02)));
            foreach (bool allowCover in new[] { false, true })
            {
                RegionMatch m = Match(RightHalf, allowCover, notched);
                Assert.False(m.Success);
                Assert.Contains("kleiner", m.Reason);
                Assert.Empty(m.Notes);
                Assert.Empty(m.Indices);
            }
        }

        [Fact]
        public void A_slit_in_the_copy_is_not_the_same_region_but_gives_a_note()
        {
            // The right half mirrored, in the copy with a dangling line from its left side (a slit: its edge twice in the loop).
            var pieces = new List<IReadOnlyList<Vec3>>
            {
                Line(-0.04, 0, 0, 0), Line(0, 0, 0, 0.02), Line(0, 0.02, -0.04, 0.02), Line(-0.04, 0.02, -0.04, 0.01), Line(-0.04, 0.01, -0.04, 0),
                Line(-0.04, 0.01, -0.03, 0.01), Line(-0.03, 0.01, -0.04, 0.01),
            };
            ProfileRegion slit = Region(pieces);
            Near(0.04 * 0.02, slit.PolylineArea, 1e-12);
            RegionMatch m = Match(RightHalf, true, slit);
            Assert.True(m.Success, m.Reason);
            Assert.True(m.Inexact);
            Assert.Single(m.Notes);
            Assert.DoesNotContain("gewählt ist", Assert.Single(m.Deviations));
        }

        [Fact]
        public void A_smaller_region_is_no_match_for_a_thin_feature()
        {
            // One witness only, inside the larger part: the copy's region does not contain the original's.
            List<RegionSegment> segments = Weld(Rect(0, 0, 0.04, 0.02));
            var source = new ProfileRegion(segments, new[] { new RegionWitness(new Vec3(0.03, 0.01, 0), 0.01) }, Eps, null);
            var targets = new[] { Region(Rect(-0.04, 0, -0.01, 0.02)), Region(Rect(-0.01, 0, 0, 0.02)) };
            RegionMatch m = Match(source, false, targets);
            Assert.False(m.Success);
            Assert.Contains("kleiner", m.Reason);
            Assert.Empty(m.Notes);
            Assert.Empty(m.Indices);
        }

        [Fact]
        public void Region_without_a_hit_is_not_found()
        {
            RegionMatch m = Match(LeftHalf, true, Region(Rect(0.1, 0.1, 0.2, 0.2)));
            Assert.False(m.Success);
            Assert.Contains("nicht gefunden", m.Reason);
            Assert.False(Match(LeftHalf, true).Success);
        }

        // ------------------------------------------------------------ read-back

        [Fact]
        public void Read_back_wants_each_expected_region_exactly_once()
        {
            ProfileRegion a = LeftHalf, b = RightHalf, other = Region(Rect(0.05, 0, 0.07, 0.02));
            var expected = new List<Vec3> { a.Witnesses[0].Point, b.Witnesses[0].Point };
            Assert.Null(RegionMatcher.CheckUsed(expected, new[] { b, a }));
            // Whole sketch: the feature reports no regions.
            Assert.NotNull(RegionMatcher.CheckUsed(expected, Array.Empty<ProfileRegion>()));
            Assert.NotNull(RegionMatcher.CheckUsed(expected, new[] { a }));
            Assert.NotNull(RegionMatcher.CheckUsed(expected, new[] { a, other }));
            Assert.NotNull(RegionMatcher.CheckUsed(expected, new[] { a, a }));
            // The union where two were selected.
            Assert.NotNull(RegionMatcher.CheckUsed(expected, new[] { Region(Rect(-0.04, 0, 0.04, 0.02)) }));
            Assert.NotNull(RegionMatcher.CheckUsed(new List<Vec3> { a.Witnesses[0].Point }, new[] { b }));
        }

        // ------------------------------------------------------------ MapPoint, ProfileRegion

        [Fact]
        public void MapPoint_equals_the_points_of_Mirror_for_random_frames_and_both_signs()
        {
            var rnd = new Random(11);
            var sigmas = new HashSet<int>();
            for (int i = 0; i < 200; i++)
            {
                Frame3 src = RandomFrame(rnd);
                var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
                Plane3 mirrored = r.Apply(src.Plane);
                Vec3 zt = rnd.Next(2) == 0 ? mirrored.Normal : -mirrored.Normal;
                Vec3 xt = zt.AnyPerpendicular();
                var target = new Frame3(mirrored.Project(RandomPoint(rnd)), xt, zt.Cross(xt));

                var sketch = new SketchModel { Name = "Skizze1", Frame = src };
                for (int k = 0; k < 5; k++) sketch.Points.Add(new SkPoint("p" + k, (rnd.NextDouble() - 0.5) * 0.2, (rnd.NextDouble() - 0.5) * 0.2));
                SketchMirrorResult res = SketchMirror.Mirror(sketch, r, target);
                sigmas.Add(res.Map.Sigma);
                foreach (SkPoint p in sketch.Points)
                {
                    Vec3 mapped = SketchMirror.MapPoint(src, r, target, new Vec3(p.X, p.Y, 0));
                    SkPoint expected = res.Sketch.Point(p.Id);
                    Near(new Vec3(expected.X, expected.Y, 0), mapped, 1e-12);
                }
            }
            Assert.Equal(new[] { -1, 1 }, sigmas.OrderBy(s => s));
        }

        [Fact]
        public void MapPoint_refuses_a_target_off_the_mirrored_plane()
        {
            var off = new Frame3(new Vec3(0, 0, 0.001), Vec3.UnitX, Vec3.UnitY);
            Assert.Throws<ArgumentException>(() => SketchMirror.MapPoint(Front, AtRight, off, new Vec3(0.01, 0.02, 0)));
        }

        [Fact]
        public void Mapped_region_keeps_area_and_clearance_and_its_witnesses_stay_inside()
        {
            var rnd = new Random(5);
            ProfileRegion l = Region(Polygon((0, 0), (0.05, 0), (0.05, 0.005), (0.005, 0.005), (0.005, 0.05), (0, 0.05)), Pieces(Circle(0.002, 0.04, 0.001)));
            for (int i = 0; i < 50; i++)
            {
                Frame3 src = RandomFrame(rnd);
                var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
                Plane3 mirrored = r.Apply(src.Plane);
                Vec3 zt = rnd.Next(2) == 0 ? mirrored.Normal : -mirrored.Normal;
                Vec3 xt = zt.AnyPerpendicular();
                var target = new Frame3(mirrored.Project(RandomPoint(rnd)), xt, zt.Cross(xt));

                ProfileRegion m = Mirrored(l, src, r, target);
                Near(l.Area, m.Area, 1e-12);
                Near(l.Perimeter, m.Perimeter, 1e-12);
                Near(l.Clearance, m.Clearance, 1e-15);
                Assert.Equal(l.Witnesses.Count, m.Witnesses.Count);
                foreach (RegionWitness w in m.Witnesses)
                {
                    Assert.Equal(RegionPoint.Inside, m.Classify(w.Point));
                    Near(RegionGeometry.Distance(m.Segments, w.Point), w.Clearance, 1e-12);
                }
            }
        }

        [Fact]
        public void Kernel_area_is_preferred_and_kept_by_the_map()
        {
            List<RegionSegment> segments = Weld(Rect(0, 0, 0.04, 0.02));
            ProfileRegion r = ProfileRegion.Create(segments, Eps, 8.0e-4 + 1e-9);
            Near(8.0e-4 + 1e-9, r.Area, 1e-15);
            Near(8.0e-4, r.PolylineArea, 1e-15);
            Assert.Equal(r.KernelArea, MirrorToFront(r).KernelArea);
            Assert.Empty(ProfileRegion.Create(segments, Eps, null, 0).Witnesses);
        }
    }
}
