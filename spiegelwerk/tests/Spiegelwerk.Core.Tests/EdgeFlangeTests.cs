using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;
using Xunit;
using static Spiegelwerk.Core.Tests.TestGeometry;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>
    /// Edge flange (blech-plan S3, A2 §11 #1, #2, #4–#9, P1, P2, P13, P16): the sketch angle of the
    /// copy, the candidates and the one pair for all edges, the flange direction, the frame test, the
    /// profile check in three parts, profiles paired with their edges, the two reads of a profile,
    /// the read-back comparison and the ownership of profiles in the tree.
    /// </summary>
    public class EdgeFlangeTests
    {
        private static readonly NumberFormatInfo Comma = new NumberFormatInfo { NumberDecimalSeparator = ",", NegativeSign = "-" };

        private static EdgeSignature Line(Vec3 a, Vec3 b) => new EdgeSignature(CurveKind.Line, a, b, (a + b) / 2, a.DistanceTo(b)) { Axis = (b - a).Normalized() };

        /// <summary>Rodrigues: <paramref name="v"/> turned by <paramref name="angle"/> (right hand) about the unit axis <paramref name="k"/>.</summary>
        private static Vec3 Rot(Vec3 k, double angle, Vec3 v) =>
            v * Math.Cos(angle) + k.Cross(v) * Math.Sin(angle) + k * (k.Dot(v) * (1 - Math.Cos(angle)));

        private static Reflection RandomReflection(Random rnd) => new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));

        /// <summary>
        /// An edge-flange profile as SOLIDWORKS draws one: a w × h rectangle standing on the hinge
        /// from <paramref name="a"/> along <paramref name="x"/>, toward <paramref name="y"/>, in a frame
        /// with X along the hinge and Y along the flange, moved by <paramref name="offset"/> along its
        /// normal (a position offset). Optionally a circular cut-out.
        /// </summary>
        private static SketchModel Profile(Vec3 a, Vec3 x, Vec3 y, double w, double h, double offset = 0, string name = "Skizze3",
            (double cx, double cy, double r)? hole = null)
        {
            var at = new Frame3(a, x, y);
            var s = new SketchModel { Name = name, Frame = new Frame3(a + at.ZAxis * offset, x, y) };
            s.Points.Add(new SkPoint("P0", 0, 0));
            s.Points.Add(new SkPoint("P1", w, 0));
            s.Points.Add(new SkPoint("P2", w, h));
            s.Points.Add(new SkPoint("P3", 0, h));
            for (int i = 0; i < 4; i++)
                s.Segments.Add(new SkSegment($"L{i}", SkSegmentKind.Line) { StartId = $"P{i}", EndId = $"P{(i + 1) % 4}" });
            if (hole is (double cx, double cy, double r))
            {
                s.Points.Add(new SkPoint("C0", cx, cy));
                s.Segments.Add(new SkSegment("K0", SkSegmentKind.Circle) { CenterId = "C0", Radius = r });
            }
            return s;
        }

        /// <summary>The same sketch content in another frame.</summary>
        private static SketchModel WithFrame(SketchModel s, Frame3 frame)
        {
            var m = new SketchModel { Name = s.Name, Frame = frame };
            m.Points.AddRange(s.Points);
            m.Segments.AddRange(s.Segments);
            m.Relations.AddRange(s.Relations);
            m.Dimensions.AddRange(s.Dimensions);
            return m;
        }

        /// <summary>The copy's final state as a correct flange shows it: the mirrored sketch in the mirrored plane.</summary>
        private static SketchModel Mirrored(SketchModel s, Reflection r) => SketchMirror.Mirror(s, r, r.ApplyKeepingXY(s.Frame)).Sketch;

        // ------------------------------------------------------------ A2 #1, #2: the sketch angle

        [Fact]
        public void Sketch_angle_of_the_copy_is_the_conjugated_angle()
        {
            var rnd = new Random(11);
            for (int i = 0; i < 4000; i++)
            {
                Reflection r = RandomReflection(rnd);
                Vec3 t = RandomUnit(rnd), u = RandomUnit(rnd);
                double theta = 0.01 + rnd.NextDouble() * (Math.PI - 0.02);
                Vec3 expected = r.ApplyVector(Rot(t, theta, u));
                foreach (bool along in new[] { true, false })
                {
                    // The copy's edge runs along R(t) or against it: Rot(t′, a′)·R u = R·Rot(t, θ)·u.
                    Vec3 copyHinge = along ? r.ApplyVector(t) : -r.ApplyVector(t);
                    double a = FlangeProfiles.PredictedAngle(theta, along);
                    Near(expected, Rot(copyHinge, a, r.ApplyVector(u)), 1e-12);
                    Near(along ? 2 * Math.PI - theta : theta, a, 1e-12);
                    // That angle is tried first, with the original's FlipDir.
                    (double angle, bool flip) first = FlangeProfiles.AngleCandidates(theta, new[] { along }, reverseOffset: true)[0];
                    Near(a, first.angle, 1e-12);
                    Assert.True(first.flip);
                }
            }
        }

        [Fact]
        public void Candidates_cover_both_hinge_directions_without_relying_on_FlipDir()
        {
            foreach (double theta in new[] { 0.3, Math.PI / 2, 2.0, Math.PI - 1e-3 })
                foreach (bool along in new[] { true, false })
                    foreach (bool reverse in new[] { true, false })
                    {
                        List<(double angle, bool flip)> c = FlangeProfiles.AngleCandidates(theta, new[] { along }, reverse);
                        // Whatever FlipDir does: the angle for either edge direction comes with the original flag and with the other.
                        foreach (bool dir in new[] { true, false })
                            foreach (bool flip in new[] { reverse, !reverse })
                                Assert.Contains(c, x => Math.Abs(x.angle - FlangeProfiles.PredictedAngle(theta, dir)) < 1e-12 && x.flip == flip);
                        // θ, 2π − θ, π − θ, π + θ: each angle once, each with both flags, all in [0, 2π).
                        int distinct = c.Select(x => Math.Round(x.angle, 9)).Distinct().Count();
                        Assert.Equal(2 * distinct, c.Count);
                        Assert.All(c, x => Assert.InRange(x.angle, 0, 2 * Math.PI));
                    }
            // A right angle: θ = π − θ and 2π − θ = π + θ, so two angles, four candidates.
            Assert.Equal(4, FlangeProfiles.AngleCandidates(Math.PI / 2, new[] { true }, false).Count);
            Assert.Equal(8, FlangeProfiles.AngleCandidates(1.0, new[] { true }, false).Count);
            // Several edges: the predicted angle of each comes first, in edge order.
            List<(double angle, bool flip)> two = FlangeProfiles.AngleCandidates(1.0, new[] { false, true }, false);
            Near(1.0, two[0].angle, 1e-12);
            Near(2 * Math.PI - 1.0, two[2].angle, 1e-12);
        }

        // ------------------------------------------------------------ A2 #3: folded read-back

        [Fact]
        public void Differences_compare_folded_angles_and_only_what_the_flange_uses()
        {
            var want = new EdgeFlangeRecord("Kantenlasche1", "EdgeFlange") { BendAngle = 1.0, OffsetType = FlangeOffsetTypes.Blind, OffsetDistance = 0.02 };
            var got = new EdgeFlangeRecord("Kantenlasche1", "EdgeFlange") { BendAngle = 2 * Math.PI - 1.0, OffsetType = FlangeOffsetTypes.Blind, OffsetDistance = 0.02 };
            // The chosen angle 2π − θ is the same bend; the direction flags are the profile check's.
            got.ReverseOffset = true;
            got.ReversePositionOffset = true;
            Assert.True(EdgeFlangeRules.Differences(want, got).None);
            got.BendAngle = Math.PI - 1.0;
            Assert.Equal(new[] { "Biegewinkel: erwartet 57,2958°, gelesen 122,7042°" }, EdgeFlangeRules.Differences(want, got, Comma).Differences);
            got.BendAngle = 1.0;

            // Up to a vertex: the length is the reference's, whatever the getter says.
            want.OffsetType = got.OffsetType = FlangeOffsetTypes.UpToVertex;
            got.OffsetDistance = 0.5;
            got.OffsetDimType = FlangeDimTypes.BendTangentArc;
            Assert.True(EdgeFlangeRules.Differences(want, got).None);
            want.OffsetType = got.OffsetType = FlangeOffsetTypes.Blind;
            Assert.Equal(2, EdgeFlangeRules.Differences(want, got).Differences.Count);
            got.OffsetDistance = 0.02;
            got.OffsetDimType = want.OffsetDimType;

            // A position offset's type and distance only with a position offset; late-bound flags only where both read them.
            got.PositionOffsetDistance = 0.003;
            Assert.True(EdgeFlangeRules.Differences(want, got).None);
            want.UsePositionOffset = got.UsePositionOffset = true;
            Assert.Single(EdgeFlangeRules.Differences(want, got).Differences);
            want.PositionOffsetDistance = 0.003;
            want.PerpendicularToFace = true;
            Assert.True(EdgeFlangeRules.Differences(want, got).None);
            got.PerpendicularToFace = false;
            Assert.Single(EdgeFlangeRules.Differences(want, got).Differences);
            got.PerpendicularToFace = true;

            // Own relief with the tear type, compared where both know it.
            want.OwnRelief = new ReliefData { Kind = ReliefKind.Tear, TearType = ReliefTearTypes.Rip };
            got.OwnRelief = new ReliefData { Kind = ReliefKind.Tear, TearType = ReliefTearTypes.Extend };
            Assert.Equal(new[] { "Entlastung: erwartet ausreißen (aufreißen), gelesen ausreißen (strecken)" }, EdgeFlangeRules.Differences(want, got).Differences);
            got.OwnRelief.TearType = 0;
            Assert.True(EdgeFlangeRules.Differences(want, got).None);
        }

        // ------------------------------------------------------------ A2 #4: one pair for all edges

        [Fact]
        public void Common_candidate_for_multi_edge_flange_or_none()
        {
            bool[,] fits = { { true, false }, { false, true }, { true, true }, { true, true } };
            var asked = new List<(int, int)>();
            var abandoned = new List<int>();
            int? chosen = FlangeProfiles.CommonCandidate(4, 2, (c, e) =>
            {
                asked.Add((c, e));
                return fits[c, e];
            }, abandoned.Add);
            Assert.Equal(2, chosen);
            Assert.Equal(new[] { 0, 1 }, abandoned);
            // Not asked beyond the first edge that does not fit, never beyond the chosen candidate.
            Assert.Equal(new[] { (0, 0), (0, 1), (1, 0), (2, 0), (2, 1) }, asked);

            // Each edge fits another candidate only: no angle for the whole flange.
            bool[,] disjoint = { { true, false }, { false, true } };
            abandoned.Clear();
            Assert.Null(FlangeProfiles.CommonCandidate(2, 2, (c, e) => disjoint[c, e], abandoned.Add));
            Assert.Equal(new[] { 0, 1 }, abandoned);
        }

        // ------------------------------------------------------------ A2 #5: flange direction

        [Fact]
        public void Flange_direction_is_mirrored_with_the_profile()
        {
            var rnd = new Random(5);
            for (int i = 0; i < 300; i++)
            {
                Vec3 a = RandomPoint(rnd), x = RandomUnit(rnd);
                Vec3 y = x.Cross(RandomUnit(rnd)).Normalized();
                var hinge = Line(a, a + x * 0.05);
                SketchModel profile = Profile(a, x, y, 0.05, 0.02, offset: (rnd.NextDouble() - 0.5) * 0.01, hole: (0.025, 0.01, 0.004));
                Vec3 d = FlangeProfiles.Direction(profile, hinge)!.Value;
                Near(y, d, 1e-9);

                Reflection r = RandomReflection(rnd);
                SketchModel copy = Mirrored(profile, r);
                Near(r.ApplyVector(d), FlangeProfiles.Direction(copy, hinge.Mirror(r))!.Value, 1e-9);
                // The copy's edge may run the other way: the direction does not care.
                EdgeSignature back = Line(r.Apply(hinge.End), r.Apply(hinge.Start));
                Near(r.ApplyVector(d), FlangeProfiles.Direction(copy, back)!.Value, 1e-9);
            }
            // Points on the hinge line only: no direction.
            var flat = new SketchModel { Frame = Front };
            flat.Points.Add(new SkPoint("A", 0, 0));
            flat.Points.Add(new SkPoint("B", 0.05, 0));
            flat.Segments.Add(new SkSegment("L", SkSegmentKind.Line) { StartId = "A", EndId = "B" });
            Assert.Null(FlangeProfiles.Direction(flat, Line(Vec3.Zero, new Vec3(0.05, 0, 0))));
        }

        // ------------------------------------------------------------ A2 #6: the native frame

        [Fact]
        public void Frame_fits_whichever_way_the_copy_edge_runs()
        {
            var rnd = new Random(9);
            for (int i = 0; i < 200; i++)
            {
                Vec3 a = RandomPoint(rnd), x = RandomUnit(rnd);
                Vec3 y = x.Cross(RandomUnit(rnd)).Normalized();
                double w = 0.08, h = 0.03;
                var hinge = Line(a, a + x * w);
                // With and without a position offset: the frame test sees the profile rebased onto its edge.
                double offset = i % 2 == 0 ? 0 : 0.003;
                var source = new FlangeProfile("Skizze3", Profile(a, x, y, w, h, offset), hinge);
                Reflection r = RandomReflection(rnd);
                Vec3 o = r.Apply(a), rx = r.ApplyVector(x), ry = r.ApplyVector(y);

                Assert.True(FlangeProfiles.FitsFrame(source, r, new Frame3(o, rx, ry)));
                // X against the mirrored edge (the copy's edge runs backwards), origin at the other end.
                Assert.True(FlangeProfiles.FitsFrame(source, r, new Frame3(o + rx * w, -rx, ry)));
                // Y away from the flange: the profile would be drawn to the other side.
                Assert.False(FlangeProfiles.FitsFrame(source, r, new Frame3(o, rx, -ry)));
                // A plane tilted by 1e-4 rad about the hinge lifts the far points 3 µm off it.
                Vec3 n = rx.Cross(ry);
                Assert.False(FlangeProfiles.FitsFrame(source, r, new Frame3(o, rx, Rot(rx, 1e-4, ry))));
                // A plane beside the hinge (as the offset sketch of a finished flange would be).
                Assert.False(FlangeProfiles.FitsFrame(source, r, new Frame3(o + n * 0.0005, rx, ry)));
                // X turned in the plane: not the frame SOLIDWORKS makes on an edge.
                Assert.False(FlangeProfiles.FitsFrame(source, r, new Frame3(o, Rot(n, 1e-3, rx), Rot(n, 1e-3, ry))));
            }
        }

        // ------------------------------------------------------------ A2 #7: rebase and normal offset

        [Fact]
        public void Rebase_and_normal_offset_split_the_profile_check()
        {
            var rnd = new Random(3);
            for (int i = 0; i < 100; i++)
            {
                Vec3 a = RandomPoint(rnd), x = RandomUnit(rnd);
                Vec3 y = x.Cross(RandomUnit(rnd)).Normalized();
                var hinge = Line(a, a + x * 0.06);
                SketchModel final = Profile(a, x, y, 0.06, 0.025, offset: 0.003);
                Vec3 v = FlangeProfiles.NormalOffset(final.Frame, hinge);
                Near(0.003, v.Length, 1e-12);

                // Rebased: the plane holds the hinge again, the 2D content is the same.
                SketchModel rebased = FlangeProfiles.Rebase(final, hinge);
                Assert.True(rebased.Frame.Plane.Contains(hinge.Start, 1e-12) && rebased.Frame.Plane.Contains(hinge.End, 1e-12));
                Assert.Equal(final.Points, rebased.Points);
                Near(Vec3.Zero, FlangeProfiles.NormalOffset(rebased.Frame, hinge), 1e-12);

                Reflection r = RandomReflection(rnd);
                var source = new FlangeProfile("Skizze3", final, hinge);
                EdgeSignature copyHinge = hinge.Mirror(r);
                SketchModel copy = Mirrored(final, r);
                ProfileCheck right = FlangeProfiles.Check(source, r, copy, copyHinge);
                Assert.True(right.Passed);

                // The wrong ReversePositionOffset: the same profile, 3 mm to the other side of the edge.
                SketchModel wrong = WithFrame(copy, new Frame3(copy.Frame.Origin - r.ApplyVector(v) * 2, copy.Frame.XAxis, copy.Frame.YAxis));
                ProfileCheck check = FlangeProfiles.Check(source, r, wrong, copyHinge);
                Assert.False(check.Passed);
                Assert.True(check.PlaneParallel);
                Assert.True(check.InPlane <= 1e-9, $"in der Ebene {check.InPlane}");
                Near(0.006, check.Offset, 1e-9);
                Assert.Equal(new[] { "Profil „Skizze3“: Abstand der Profilebene zur Kante weicht um 6 mm vom gespiegelten ab" }, check.Describe(Comma));
            }
        }

        // ------------------------------------------------------------ A2 #8, P16: deviation

        [Fact]
        public void Profile_deviation_detects_a_flange_flipped_about_its_hinge_and_counts_arc_centres()
        {
            Vec3 a = new Vec3(0.01, 0.02, 0), x = Vec3.UnitX, y = Vec3.UnitZ;
            var hinge = Line(a, a + x * 0.1);
            var r = new Reflection(Plane3.RightYZ);
            SketchModel final = Profile(a, x, y, 0.1, 0.04, hole: (0.05, 0.02, 0.008));
            var source = new FlangeProfile("Skizze3", final, hinge);
            SketchModel copy = Mirrored(final, r);
            Assert.True(FlangeProfiles.Check(source, r, copy, hinge.Mirror(r)).Passed);

            // Flipped about its hinge: drawn to the other side in the same plane.
            SketchModel flipped = WithFrame(copy, new Frame3(copy.Frame.Origin, copy.Frame.XAxis, -copy.Frame.YAxis));
            ProfileCheck f = FlangeProfiles.Check(source, r, flipped, hinge.Mirror(r));
            Assert.False(f.Passed);
            Assert.True(f.PlaneParallel);
            Assert.True(f.InPlane >= 0.04 - 1e-9, $"{f.InPlane}");

            // The same outline with the hole 1 mm off, or 0.5 mm larger: only the circle's points tell.
            SketchModel moved = Mirrored(Profile(a, x, y, 0.1, 0.04, hole: (0.051, 0.02, 0.008)), r);
            Near(0.001, FlangeProfiles.Check(source, r, moved, hinge.Mirror(r)).InPlane, 1e-9);
            SketchModel larger = Mirrored(Profile(a, x, y, 0.1, 0.04, hole: (0.05, 0.02, 0.0085)), r);
            Near(0.0005, FlangeProfiles.Check(source, r, larger, hinge.Mirror(r)).InPlane, 1e-9);

            // An arc's middle counts: the same ends and centre, bulging the other way.
            SketchModel Arc(int direction)
            {
                SketchModel s = Profile(a, x, y, 0.1, 0.04);
                s.Segments.RemoveAt(2);
                s.Points.Add(new SkPoint("M", 0.05, 0.04));
                s.Segments.Add(new SkSegment("A2", SkSegmentKind.Arc) { StartId = "P2", EndId = "P3", CenterId = "M", Direction = direction });
                return s;
            }
            List<Vec3> up = FlangeProfiles.ProfilePoints(Arc(1)), down = FlangeProfiles.ProfilePoints(Arc(-1));
            // The middle 50 mm above the centre against one 50 mm below it: its nearest point is the centre.
            Near(0.05, FlangeProfiles.InPlaneDeviation(up, down), 1e-9);

            // A set: the order does not count.
            List<Vec3> points = FlangeProfiles.ProfilePoints(final);
            Assert.Equal(0, FlangeProfiles.InPlaneDeviation(points, Enumerable.Reverse(points).ToList()));
            Assert.Equal(double.PositiveInfinity, FlangeProfiles.InPlaneDeviation(points, new List<Vec3>()));
            // Construction geometry is no part of the profile.
            final.Segments.Add(new SkSegment("Z", SkSegmentKind.Line) { StartId = "P0", EndId = "P2", Construction = true });
            Assert.Equal(points.Count, FlangeProfiles.ProfilePoints(final).Count);
        }

        // ------------------------------------------------------------ A2 #9: profiles to edges

        [Fact]
        public void Assign_pairs_each_hinge_with_the_profile_on_it()
        {
            // Two edges of a plate meeting at a corner; the profiles listed the other way round.
            EdgeSignature e0 = Line(Vec3.Zero, new Vec3(0.1, 0, 0)), e1 = Line(new Vec3(0.1, 0, 0), new Vec3(0.1, 0.05, 0));
            SketchModel p0 = Profile(Vec3.Zero, Vec3.UnitX, Vec3.UnitZ, 0.1, 0.02, name: "Skizze3");
            SketchModel p1 = Profile(new Vec3(0.1, 0, 0), Vec3.UnitY, Vec3.UnitZ, 0.05, 0.02, name: "Skizze4");
            Assert.Equal(new[] { 1, 0 }, FlangeProfiles.Assign(new[] { e0, e1 }, new[] { new ProfileCandidate("Skizze4", p1), new ProfileCandidate("Skizze3", p0) }));
        }

        [Fact]
        public void Assign_refuses_ambiguous_or_missing_profiles()
        {
            // Two collinear edges (a plate with a notch) and a profile whose line covers both.
            EdgeSignature e0 = Line(Vec3.Zero, new Vec3(0.04, 0, 0)), e1 = Line(new Vec3(0.06, 0, 0), new Vec3(0.1, 0, 0));
            SketchModel both = Profile(Vec3.Zero, Vec3.UnitX, Vec3.UnitZ, 0.1, 0.02);
            SketchModel left = Profile(Vec3.Zero, Vec3.UnitX, Vec3.UnitZ, 0.04, 0.02, name: "Skizze4");
            Assert.Null(FlangeProfiles.Assign(new[] { e0, e1 }, new[] { new ProfileCandidate("Skizze3", both), new ProfileCandidate("Skizze3b", both) }));
            // One on e0 alone settles it; the other then has e1.
            Assert.Equal(new[] { 0, 1 }, FlangeProfiles.Assign(new[] { e0, e1 }, new[] { new ProfileCandidate("Skizze4", left), new ProfileCandidate("Skizze3", both) }));
            // A profile on neither edge.
            SketchModel elsewhere = Profile(new Vec3(0, 0.03, 0), Vec3.UnitX, Vec3.UnitZ, 0.04, 0.02);
            Assert.Null(FlangeProfiles.Assign(new[] { e0, e1 }, new[] { new ProfileCandidate("Skizze5", elsewhere) }));
            // By its frame it could be e0's, 30 mm off; equally far off two parallel edges it is nobody's.
            Assert.Equal(new[] { 0 }, FlangeProfiles.Assign(new[] { e0, e1 }, new[] { new ProfileCandidate("Skizze5", elsewhere) }, byFrame: true));
            EdgeSignature twin = Line(new Vec3(0, 0.06, 0), new Vec3(0.04, 0.06, 0));
            Assert.Null(FlangeProfiles.Assign(new[] { e0, twin }, new[] { new ProfileCandidate("Skizze5", elsewhere) }, byFrame: true));
        }

        [Fact]
        public void Assign_uses_frame_evidence_for_offset_profiles()
        {
            // Two parallel edges of a plate, each with a flange 3 mm off its edge: no line on either edge any more.
            EdgeSignature e0 = Line(Vec3.Zero, new Vec3(0.1, 0, 0)), e1 = Line(new Vec3(0.1, 0.05, 0), new Vec3(0, 0.05, 0));
            SketchModel p0 = Profile(Vec3.Zero, Vec3.UnitX, Vec3.UnitZ, 0.1, 0.02, offset: 0.003, name: "Skizze3");
            SketchModel p1 = Profile(new Vec3(0.1, 0.05, 0), -Vec3.UnitX, Vec3.UnitZ, 0.1, 0.02, offset: 0.003, name: "Skizze4");
            var candidates = new[] { new ProfileCandidate("Skizze4", p1), new ProfileCandidate("Skizze3", p0) };
            Assert.Null(FlangeProfiles.Assign(new[] { e0, e1 }, candidates));
            Assert.Equal(new[] { 1, 0 }, FlangeProfiles.Assign(new[] { e0, e1 }, candidates, byFrame: true));
            // The frame alone fits both parallel edges; the nearer one decides.
            Assert.True(FlangeProfiles.FrameFitsEdge(p0, e0));
            Assert.True(FlangeProfiles.FrameFitsEdge(p0, e1));
            Assert.False(FlangeProfiles.FrameFitsEdge(p0, Line(new Vec3(0.1, 0, 0), new Vec3(0.1, 0.05, 0))));
            // A frame whose X is not along the edge is no evidence.
            Assert.False(FlangeProfiles.FrameFitsEdge(Profile(Vec3.Zero, Vec3.UnitZ, Vec3.UnitX, 0.02, 0.1, offset: 0.003), e0));
        }

        // ------------------------------------------------------------ A2 P2: the two reads

        [Fact]
        public void Merge_takes_frame_and_content_from_the_final_read_and_relations_from_the_one_at_the_flange()
        {
            Vec3 a = Vec3.Zero;
            SketchModel final = Profile(a, Vec3.UnitX, Vec3.UnitZ, 0.1, 0.02, offset: 0.003);
            SketchModel atFlange = Profile(a, Vec3.UnitX, Vec3.UnitZ, 0.1, 0.02);
            var hinge = Line(a, new Vec3(0.1, 0, 0));
            atFlange.Relations.Add(new SkRelation(RelationKind.Collinear, new[] { SkRef.LocalSegment("L0"), SkRef.ModelEdge(hinge) }));
            final.Relations.Add(new SkRelation(RelationKind.Horizontal, new[] { SkRef.LocalSegment("L0") }));
            SketchModel merged = FlangeProfiles.Merge(final, atFlange, out string? why)!;
            Assert.Null(why);
            Assert.Equal(final.Frame.Origin, merged.Frame.Origin);
            Assert.Equal(RelationKind.Collinear, Assert.Single(merged.Relations).Kind);
            Assert.Equal(final.Points, merged.Points);

            // Another segment, or a point elsewhere in the sketch: not the same sketch.
            SketchModel other = Profile(a, Vec3.UnitX, Vec3.UnitZ, 0.1, 0.02, hole: (0.05, 0.01, 0.002));
            Assert.Null(FlangeProfiles.Merge(other, atFlange, out why));
            Assert.Equal("hat im fertigen Teil andere Skizzenelemente als vor der Lasche", why);
            SketchModel taller = Profile(a, Vec3.UnitX, Vec3.UnitZ, 0.1, 0.025);
            Assert.Null(FlangeProfiles.Merge(taller, atFlange, out why));
            Assert.Equal("liegt im fertigen Teil in ihrer Ebene anders als vor der Lasche", why);
        }

        // ------------------------------------------------------------ rules for the builder

        [Fact]
        public void Paths_options_and_direction_variants()
        {
            Assert.Equal(new[] { EdgeFlangePath.Definition, EdgeFlangePath.Insert2, EdgeFlangePath.Single }, EdgeFlangeRules.Paths(1));
            Assert.Equal(new[] { EdgeFlangePath.Definition, EdgeFlangePath.Insert2 }, EdgeFlangeRules.Paths(3));

            var r = new EdgeFlangeRecord("Kantenlasche1", "EdgeFlange") { UseDefaultRadius = true };
            Assert.Equal(EdgeFlangeOptions.UseDefaultRadius | EdgeFlangeOptions.UseReliefRatio | EdgeFlangeOptions.UseDefaultRelief, EdgeFlangeRules.CreationOptions(r, flip: false));
            r.UseDefaultRadius = false;
            r.UsePositionOffset = r.ReversePositionOffset = r.TrimSideBends = true;
            r.OwnRelief = new ReliefData { Kind = ReliefKind.Tear, TearType = ReliefTearTypes.Rip, UseRatio = false };
            Assert.Equal(EdgeFlangeOptions.FlipDir | EdgeFlangeOptions.DoOffset | EdgeFlangeOptions.ReverseOffsetDir | EdgeFlangeOptions.TearClip | EdgeFlangeOptions.TrimSideBend,
                EdgeFlangeRules.CreationOptions(r, flip: true));

            // Without a position offset only the flange direction is searched; with one, all three.
            Assert.Equal(new[] { (true, false) }, EdgeFlangeRules.DirectionVariants(false, false, usePositionOffset: false));
            Assert.Equal(new[] { (false, true), (true, false), (false, false) }, EdgeFlangeRules.DirectionVariants(true, true, usePositionOffset: true));

            Assert.True(EdgeFlangeRules.UsesReference(FlangeOffsetTypes.UptoEdgeAndMerge));
            Assert.False(EdgeFlangeRules.UsesReference(FlangeOffsetTypes.MidPlane));
            Assert.True(EdgeFlangeRules.UsesDistance(FlangeOffsetTypes.FromSurface) && EdgeFlangeRules.UsesReference(FlangeOffsetTypes.FromSurface));
            Assert.Equal("Profilebene mit Winkel 270° und FlipDir nein (Kandidat 1 von 4).", EdgeFlangeRules.DescribeCandidate(1.5 * Math.PI, false, 0, 4, Comma));
            r.Profiles.Add(new FlangeProfile("Skizze3", new SketchModel(), Line(Vec3.Zero, Vec3.UnitX)));
            r.BendAngle = 2 * Math.PI - Math.PI / 3;
            Assert.Equal("Kantenlasche, 1 Kante, 60°, mit Versatz", EdgeFlangeRules.Describe(r, Comma));
        }

        [Fact]
        public void Edge_flange_record_owns_its_profiles_and_samples_its_bends()
        {
            var r = new EdgeFlangeRecord("Kantenlasche1", "EdgeFlange");
            Assert.False(r.BringsParts);
            r.Profiles.Add(new FlangeProfile("Skizze3", new SketchModel(), Line(Vec3.Zero, Vec3.UnitX)));
            r.Profiles.Add(new FlangeProfile("Skizze4", new SketchModel(), Line(Vec3.UnitX, Vec3.UnitY)));
            Assert.Equal(new[] { "Skizze3", "Skizze4" }, r.OwnedSketches);
            Assert.True(r.BringsParts);
            Assert.True(r.DeleteWithAbsorbed);
            Assert.True(r.SampleSubFeatures);
            Assert.True(r.ChangesSolid);
            // Its bends inherit its radius, allowance and relief.
            r.UseDefaultRadius = false;
            r.BendRadius = 0.002;
            r.OwnAllowance = new BendAllowanceData { KFactor = 0.38 };
            BendValues parent = EdgeFlangeRules.Parent(r);
            Assert.False(parent.UseDefaultRadius);
            Assert.Equal(0.002, parent.Radius);
            Assert.Same(r.OwnAllowance, parent.Allowance);
            Assert.True(parent.UseDefaultRelief);
        }

        // ------------------------------------------------------------ A2 P13: ownership in the tree

        private static TreeFeature F(string name, string type, string[]? parents = null, params TreeFeature[] subs) => new TreeFeature(name, type, parents, subs);

        [Fact]
        public void Edge_flange_profiles_belong_to_the_flange_and_a_top_level_parent_only_under_the_P13_rule()
        {
            List<TreeFeature> Top(string[] otherParents) => new List<TreeFeature>
            {
                F("Sheet-Metal1", "SheetMetal"),
                F("Base-Flange1", "SMBaseFlange", new[] { "Sheet-Metal1", "Sketch1" }, F("Sketch1", "ProfileFeature")),
                F("Sketch5", "ProfileFeature"),
                F("Sketch6", "ProfileFeature"),
                F("Edge-Flange1", "EdgeFlange", new[] { "Base-Flange1", "Sketch2", "Sketch5", "Sketch6" },
                    F("Sketch2", "ProfileFeature"), F("EdgeBend1", "OneBend", new[] { "Sketch5" }), F("EdgeBend2", "OneBend")),
                F("Cut-Extrude1", "Cut", otherParents),
            };
            var facts = new Dictionary<string, EdgeFlangeFacts> { ["Edge-Flange1"] = new EdgeFlangeFacts(2, new[] { "Sketch5" }) };

            SheetMetalTree tree = SheetMetalTree.Of(Top(new[] { "Sketch6" }), edgeFlanges: facts);
            Assert.Equal(("Edge-Flange1", PartOwnerKind.EdgeFlange), tree.Owners["Sketch2"]);
            // On one of its edges, absorbed by nothing, no other feature's parent (its own bend does not count), one profile short.
            Assert.Equal(("Edge-Flange1", PartOwnerKind.EdgeFlange), tree.Owners["Sketch5"]);
            // Sketch6 is a parent too, but not on an edge (an up-to reference, say): a sketch of its own.
            Assert.False(tree.Owners.ContainsKey("Sketch6"));
            Assert.False(tree.Owners.ContainsKey("EdgeBend1"));
            Assert.True(SheetMetalTree.ListsSubFeature("EdgeFlange", "ProfileFeature"));

            // Shared with another feature: stays a sketch of its own.
            Assert.False(SheetMetalTree.Of(Top(new[] { "Sketch5" }), edgeFlanges: facts).Owners.ContainsKey("Sketch5"));
            // As many profile sub-sketches as edges: no profile is missing.
            var full = new Dictionary<string, EdgeFlangeFacts> { ["Edge-Flange1"] = new EdgeFlangeFacts(1, new[] { "Sketch5" }) };
            Assert.False(SheetMetalTree.Of(Top(new[] { "Sketch6" }), edgeFlanges: full).Owners.ContainsKey("Sketch5"));
            // Without facts: only the sub-sketches.
            SheetMetalTree plain = SheetMetalTree.Of(Top(new[] { "Sketch6" }));
            Assert.True(plain.Owners.ContainsKey("Sketch2"));
            Assert.False(plain.Owners.ContainsKey("Sketch5"));
        }
    }
}
