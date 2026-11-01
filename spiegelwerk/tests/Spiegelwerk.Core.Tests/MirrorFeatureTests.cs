using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Verification;
using Xunit;
using static Spiegelwerk.Core.Tests.TestGeometry;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>
    /// Mirror features of the original („Features/Flächen spiegeln“, „Körper spiegeln“): the
    /// second half is the first half reflected once more, at P′ = R_Π(P).
    /// </summary>
    public class MirrorFeatureTests
    {
        private static readonly Plane3 Pi = new Plane3(new Vec3(0.02, 0, 0), Vec3.UnitX);

        public static IEnumerable<object[]> Planes() => new[]
        {
            // P meets Π at an angle, through the origin and off it.
            new object[] { new Plane3(Vec3.Zero, new Vec3(1, 1, 0)), Pi },
            new object[] { new Plane3(new Vec3(0.01, 0.03, -0.02), new Vec3(0.2, -1, 0.4)), Pi },
            // P parallel to Π.
            new object[] { new Plane3(new Vec3(0.05, 0, 0), Vec3.UnitX), Pi },
            new object[] { new Plane3(new Vec3(-0.07, 0.1, 0), -Vec3.UnitX), new Plane3(Vec3.Zero, Vec3.UnitX) },
            // P equal to Π (either normal).
            new object[] { Pi, Pi },
            new object[] { new Plane3(Pi.Origin, -Pi.Normal), Pi },
            // Π not through the origin, P perpendicular to it.
            new object[] { Plane3.TopXZ, new Plane3(new Vec3(0, 0, 0.03), new Vec3(0, 0.3, 1)) },
        };

        // ------------------------------------------------------------ the mathematics

        [Theory]
        [MemberData(nameof(Planes))]
        public void Second_half_is_the_first_half_mirrored_at_the_mirrored_plane(Plane3 p, Plane3 pi)
        {
            var run = new Reflection(pi);
            Reflection secondHalf = MirrorFeatureRules.SecondHalf(run, p);
            var atP = new Reflection(p);
            var rnd = new Random(7);
            for (int i = 0; i < 200; i++)
            {
                Vec3 x = RandomPoint(rnd);
                // What the target needs (the original's second half, mirrored by the run) is the
                // first half in the target (R_Π(x)) mirrored at P′.
                Near(run.Apply(atP.Apply(x)), secondHalf.Apply(run.Apply(x)), 1e-12);
            }
        }

        [Theory]
        [MemberData(nameof(Planes))]
        public void Mirroring_the_mirrored_plane_back_gives_the_plane(Plane3 p, Plane3 pi)
        {
            var run = new Reflection(pi);
            Assert.True(run.Apply(run.Apply(p)).IsCoplanarWith(p));
        }

        [Fact]
        public void A_mirror_plane_equal_to_the_run_plane_stays_where_it_is()
        {
            var run = new Reflection(Pi);
            Plane3 second = MirrorFeatureRules.SecondHalf(run, Pi).Plane;
            Assert.True(second.IsCoplanarWith(Pi));
            // The second half then lies where the original seed is: R_Π(R_Π(x)) = x.
            var x = new Vec3(0.1, 0.02, -0.03);
            Near(x, run.Apply(new Reflection(Pi).Apply(x)), 1e-12);
        }

        [Fact]
        public void Two_reflections_rotate_by_twice_the_angle_about_their_common_line()
        {
            // P at 30° to Π, both through the z axis: rotation by 60° about z.
            double theta = 30 * Math.PI / 180;
            var pi = new Plane3(Vec3.Zero, Vec3.UnitX);
            var p = new Plane3(Vec3.Zero, new Vec3(Math.Cos(theta), Math.Sin(theta), 0));
            Affine t = new Reflection(pi).ToAffine().Compose(new Reflection(p).ToAffine());
            Assert.True(t.IsProperRigid());
            Near(1.0, t.Determinant, 1e-12);
            // The common line is fixed, a vector across it turns by 2θ.
            Near(new Vec3(0, 0, 0.3), t.Apply(new Vec3(0, 0, 0.3)), 1e-12);
            Vec3 turned = t.ApplyVector(Vec3.UnitX);
            Near(2 * theta, Math.Acos(Math.Max(-1, Math.Min(1, turned.Dot(Vec3.UnitX)))), 1e-12);
        }

        [Fact]
        public void Two_parallel_reflections_translate_by_twice_their_distance()
        {
            var pi = new Plane3(new Vec3(0.02, 0, 0), Vec3.UnitX);
            var p = new Plane3(new Vec3(0.05, 0, 0), -Vec3.UnitX);
            Affine t = new Reflection(pi).ToAffine().Compose(new Reflection(p).ToAffine());
            Assert.True(t.IsProperRigid());
            // x ↦ x + 2(Π − P) along the normal: 2 · (0.02 − 0.05) = −0.06.
            Near(new Vec3(-0.06, 0, 0), t.Translation, 1e-12);
            Near(Vec3.UnitX, t.Col0, 1e-12);
        }

        // ------------------------------------------------------------ transform → reflection

        [Theory]
        [MemberData(nameof(Planes))]
        public void A_reflection_is_recognised_from_its_transform(Plane3 p, Plane3 _)
        {
            Reflection? r = Reflection.TryFromAffine(new Reflection(p).ToAffine());
            Assert.NotNull(r);
            Assert.True(r!.Plane.IsCoplanarWith(p));
        }

        [Fact]
        public void Rotations_shears_glide_and_point_reflections_are_no_mirror_plane()
        {
            // Rotation by 90° about z (det +1).
            Assert.Null(Reflection.TryFromAffine(new Affine(Vec3.UnitY, -Vec3.UnitX, Vec3.UnitZ, Vec3.Zero)));
            // Shear with det −1.
            Assert.Null(Reflection.TryFromAffine(new Affine(-Vec3.UnitX, new Vec3(0.1, 1, 0), Vec3.UnitZ, Vec3.Zero)));
            // Reflection at z = 0.1 followed by a glide of 0.3 in x: same linear part.
            Affine mirror = new Reflection(new Plane3(new Vec3(0, 0, 0.1), Vec3.UnitZ)).ToAffine();
            Assert.NotNull(Reflection.TryFromAffine(mirror));
            Assert.Null(Reflection.TryFromAffine(new Affine(mirror.Col0, mirror.Col1, mirror.Col2, mirror.Translation + new Vec3(0.3, 0, 0))));
            // −I: det −1, symmetric and orthogonal, but a point reflection.
            Assert.Null(Reflection.TryFromAffine(new Affine(-Vec3.UnitX, -Vec3.UnitY, -Vec3.UnitZ, Vec3.Zero)));
            // Rotation by 180°: symmetric and orthogonal, det +1.
            Assert.Null(Reflection.TryFromAffine(new Affine(-Vec3.UnitX, -Vec3.UnitY, Vec3.UnitZ, Vec3.Zero)));
        }

        // ------------------------------------------------------------ references of the second half

        private static MassState Box(Vec3 centre, double volume) => MassState.FromBodies(new[] { (volume, centre) });

        [Fact]
        public void Seeds_away_from_the_mirror_plane_are_checked_step_by_step()
        {
            // Target coordinates: P′ is x = 0.05; two first-half bosses left of it.
            var secondHalf = new Reflection(new Plane3(new Vec3(0.05, 0, 0), Vec3.UnitX));
            MassState a = Box(new Vec3(0.01, 0, 0), 2e-6), b = Box(new Vec3(0.02, 0.03, 0), 1e-6);
            MassState whole = a.Mirror(secondHalf) + b.Mirror(secondHalf);
            MassState total = a + b + whole;
            Assert.True(MirrorFeatureRules.SeedsIndependent(whole, new MassState?[] { a, b }, secondHalf, total));
        }

        [Fact]
        public void A_seed_across_the_mirror_plane_is_not_checked_on_its_own()
        {
            // A boss from x = 0.03 to 0.06 (1 cm² section) reaches over P′ (x = 0.05): mirrored it
            // spans 0.04 to 0.07, and merged the mirror feature adds only 0.06 to 0.07.
            var secondHalf = new Reflection(new Plane3(new Vec3(0.05, 0, 0), Vec3.UnitX));
            MassState seed = Box(new Vec3(0.045, 0, 0), 3e-6);
            MassState whole = Box(new Vec3(0.065, 0, 0), 1e-6);
            Assert.False(MirrorFeatureRules.SeedsIndependent(whole, new MassState?[] { seed }, secondHalf, seed + whole));
        }

        [Fact]
        public void A_missing_reference_makes_the_seeds_not_independent()
        {
            var secondHalf = new Reflection(Pi);
            MassState a = Box(new Vec3(0.1, 0, 0), 1e-6);
            Assert.False(MirrorFeatureRules.SeedsIndependent(null, new MassState?[] { a }, secondHalf, a));
            Assert.False(MirrorFeatureRules.SeedsIndependent(a.Mirror(secondHalf), new MassState?[] { a, null }, secondHalf, a));
            Assert.False(MirrorFeatureRules.SeedsIndependent(a.Mirror(secondHalf), new MassState?[0], secondHalf, a));
        }

        [Fact]
        public void Face_samples_on_the_mirror_plane_are_left_out()
        {
            var plane = new Plane3(new Vec3(0.05, 0, 0), Vec3.UnitX);
            var on = new Vec3(0.05, 0.01, 0.02);
            var off = new Vec3(0.04, 0.01, 0.02);
            Assert.Equal(new[] { off }, MirrorFeatureRules.OffPlane(new[] { on, off }, plane));
        }

        [Fact]
        public void A_copy_is_patched_with_its_first_half()
        {
            var plane = new Plane3(new Vec3(0.05, 0, 0), Vec3.UnitX);
            var first = new BuiltFeature("Boss-Extrude1") { Expected = Box(new Vec3(0.01, 0, 0), 1e-6), Merge = true, ChangesSolid = true };
            first.Samples.Add(new Vec3(0.05, 0, 0));
            first.Samples.Add(new Vec3(0.01, 0.002, 0));
            // Built as a body of its own in the same part: created without merging.
            var copy = new ExtrudeRecord("Boss-Extrude1_gespiegelt", "Extrusion", "Skizze1_gespiegelt") { Merge = false };
            copy.FaceSamples.Add(new Vec3(9, 9, 9));

            MirrorFeatureRules.Patch(copy, first, plane);

            Assert.True(copy.Merge);
            Assert.Equal(first.Expected, copy.ExpectedChange);
            Assert.Equal(new[] { new Vec3(0.01, 0.002, 0) }, copy.FaceSamples);

            // Without a first half on record: no reference at all, never a stale one.
            MirrorFeatureRules.Patch(copy, null, plane);
            Assert.Null(copy.ExpectedChange);
            Assert.Empty(copy.FaceSamples);
        }

        [Fact]
        public void Only_boss_type_records_carry_the_merge_flag()
        {
            Assert.True(MirrorFeatureRules.SetMerge(new RevolveRecord("R", "Revolution", "S") { Merge = false }, true));
            Assert.True(MirrorFeatureRules.SetMerge(new SweepRecord("S", "Sweep"), false));
            Assert.True(MirrorFeatureRules.SetMerge(new LoftRecord("L", "Blend"), false));
            Assert.False(MirrorFeatureRules.SetMerge(new FilletRecord("F", "Fillet"), false));
            Assert.True(MirrorFeatureRules.MergeOf(new ExtrudeRecord("E", "Extrusion", "S") { Merge = false }) == false);
            Assert.Null(MirrorFeatureRules.MergeOf(new ChamferRecord("C", "Chamfer")));
        }

        // ------------------------------------------------------------ bodies

        [Fact]
        public void Body_mirror_expects_each_seed_body_at_both_reflections()
        {
            var p = new Plane3(new Vec3(0, 0.01, 0), Vec3.UnitY);
            var run = new Reflection(Pi);
            var seed = new MirrorSeedBody("Aufsatz1", Box(new Vec3(0.03, 0.02, 0.01), 3e-6), new[] { "Aufsatz1" });
            MassState expected = MirrorFeatureRules.ExpectedNewBodies(new[] { seed }, p, run).Single();
            // R_P′(R_Π(c)) = R_Π(R_P(c)).
            Vec3 c = MirrorFeatureRules.SecondHalf(run, p).Apply(run.Apply(new Vec3(0.03, 0.02, 0.01)));
            Near(c * 3e-6, expected.Moment, 1e-15);
            Near(3e-6, expected.Volume, 1e-18);
        }

        [Fact]
        public void The_second_half_must_make_exactly_one_body_per_seed()
        {
            MassState a = Box(new Vec3(0.1, 0, 0), 1e-6), b = Box(new Vec3(0.2, 0, 0), 1e-6);
            Assert.True(MirrorFeatureRules.NewBodiesMatch(new[] { a, b }, new[] { b, a }));
            // A multi-contour seed that made one body more.
            Assert.False(MirrorFeatureRules.NewBodiesMatch(new[] { a }, new[] { a, b }));
            // The same body twice does not stand for two.
            Assert.False(MirrorFeatureRules.NewBodiesMatch(new[] { a, b }, new[] { a, a }));
            Assert.False(MirrorFeatureRules.NewBodiesMatch(new[] { a }, new MassState[0]));
        }

        [Fact]
        public void A_body_mirror_of_a_mirrored_body_does_not_rebuild_its_seed_twice()
        {
            // Spiegeln1 mirrors the body of Aufsatz1 without merging; Spiegeln2 mirrors that new
            // body, whose history also lists Aufsatz1; Spiegeln3 mirrors the result of Spiegeln2.
            var m1 = Bodies("Spiegeln1", false, new MirrorSeedBody("Aufsatz1", Box(new Vec3(0.01, 0, 0), 1e-6), new[] { "Aufsatz1" }));
            var m2 = Bodies("Spiegeln2", false,
                new MirrorSeedBody("Spiegeln1[1]", Box(new Vec3(-0.01, 0, 0), 1e-6), new[] { "Aufsatz1", "Spiegeln1", "Verrundung1" }));
            var m3 = Bodies("Spiegeln3", false,
                new MirrorSeedBody("Spiegeln2[1]", Box(new Vec3(0, 0.01, 0), 1e-6), new[] { "Aufsatz1", "Spiegeln1", "Spiegeln2" }),
                new MirrorSeedBody("Aufsatz1", Box(new Vec3(0.01, 0, 0), 1e-6), new[] { "Aufsatz1" }));

            MirrorFeatureRules.FlattenSeeds(new FeatureRecord[] { m1, m2, m3 });

            Assert.Equal(new[] { "Aufsatz1" }, m1.SeedFeatures);
            // A fillet on the mirrored body came after Spiegeln1 and stays a seed.
            Assert.Equal(new[] { "Spiegeln1", "Verrundung1" }, m2.SeedFeatures);
            // Through two levels; the body of Aufsatz1 itself is still a seed of its own.
            Assert.Equal(new[] { "Spiegeln2", "Aufsatz1" }, m3.SeedFeatures);
        }

        [Fact]
        public void A_merged_body_mirror_keeps_the_whole_history_of_its_body()
        {
            // With merging the body is seed and copy at once: both are needed.
            var m1 = Bodies("Spiegeln1", true, new MirrorSeedBody("Aufsatz1", Box(new Vec3(0.01, 0, 0), 1e-6), new[] { "Aufsatz1" }));
            var m2 = Bodies("Spiegeln2", false,
                new MirrorSeedBody("Spiegeln1", Box(new Vec3(0, 0, 0), 2e-6), new[] { "Aufsatz1", "Spiegeln1" }));
            MirrorFeatureRules.FlattenSeeds(new FeatureRecord[] { m1, m2 });
            Assert.Equal(new[] { "Aufsatz1", "Spiegeln1" }, m2.SeedFeatures);
        }

        private static MirrorFeatureRecord Bodies(string name, bool merge, params MirrorSeedBody[] seeds)
        {
            var r = new MirrorFeatureRecord(name, "MirrorSolid", MirrorSeedKind.Bodies, Plane3.RightYZ) { Merge = merge };
            r.SeedBodies.AddRange(seeds);
            r.SeedFeaturesFromBodies();
            return r;
        }

        [Fact]
        public void Seed_bodies_of_a_chosen_body_mirror_count_as_selected()
        {
            // Spiegeln1 (chosen) mirrors the body of Aufsatz1; its seed body counts as selected, so
            // deleting the original is not held up by it. A body the mirror made is the user's choice too.
            BodyScope scope = BodyScope.Of(
                new[]
                {
                    new BodyHistory("Aufsatz1", new[] { "Aufsatz1" }, true),
                    new BodyHistory("Spiegeln1[1]", new[] { "Spiegeln1" }, false),
                },
                new[] { "Skizze1", "Aufsatz1", "Spiegeln1" }, new[] { "Spiegeln1" });
            Assert.Empty(scope.UnselectedBodies);
            Assert.Empty(scope.OtherBodiesChanged);

            // Only the mirrored body selected: its seed body comes along unselected and keeps the original.
            scope = BodyScope.Of(
                new[]
                {
                    new BodyHistory("Aufsatz1", new[] { "Aufsatz1" }, false),
                    new BodyHistory("Spiegeln1[1]", new[] { "Spiegeln1" }, true),
                },
                new[] { "Skizze1", "Aufsatz1", "Spiegeln1" }, new string[0]);
            Assert.Equal(new[] { "Aufsatz1" }, scope.UnselectedBodies.Keys);
        }

        // ------------------------------------------------------------ faces as seeds

        private static FaceSignature Face(double area, Vec3 sample, Vec3 normal) => new FaceSignature(SurfaceKind.Plane, area, sample) { Normal = normal };

        [Fact]
        public void Faces_are_rebuilt_only_as_the_whole_set_of_their_feature()
        {
            FaceSignature top = Face(1e-4, new Vec3(0, 0.01, 0), Vec3.UnitY);
            FaceSignature side = Face(2e-4, new Vec3(0.01, 0, 0), Vec3.UnitX);
            FaceSignature hole = new FaceSignature(SurfaceKind.Cylinder, 3e-5, new Vec3(0, 0, 0.004));
            Assert.True(MirrorFeatureRules.SameFaces(new[] { top, side, hole }, new[] { hole, top, side }));
            // One face of the three: rebuilt as the whole feature it would be more than was mirrored.
            Assert.False(MirrorFeatureRules.SameFaces(new[] { top, side, hole }, new[] { hole }));
            // A face of the same size elsewhere is not one of them.
            Assert.False(MirrorFeatureRules.SameFaces(new[] { top }, new[] { Face(1e-4, new Vec3(0, 0.02, 0), Vec3.UnitY) }));
        }

        // ------------------------------------------------------------ names and report

        [Fact]
        public void Second_half_names_nest_and_keep_the_suffix_last()
        {
            Assert.Equal("Boss-Extrude1 (Spiegeln1)", MirrorFeatureRules.SecondHalfName("Boss-Extrude1", "Spiegeln1"));
            Assert.Equal("Boss-Extrude1 (Spiegeln1) (Spiegeln2)",
                MirrorFeatureRules.SecondHalfName(MirrorFeatureRules.SecondHalfName("Boss-Extrude1", "Spiegeln1"), "Spiegeln2"));
            Assert.Equal("Boss-Extrude1 (Spiegeln1)_gespiegelt",
                FeatureNames.InTarget(MirrorFeatureRules.SecondHalfName("Boss-Extrude1", "Spiegeln1"), "_gespiegelt"));
            Assert.Equal("Boss-Extrude1", FeatureNames.InTarget("Boss-Extrude1", null));
        }

        [Fact]
        public void Second_half_entries_are_named_under_their_mirror_feature()
        {
            var ok = new ReportEntry("Boss-Extrude1_gespiegelt", "Extrusion", ReportStatus.Ok, "Aufsatz") { Verification = "Volumen und Schwerpunkt stimmen" };
            ok.Details.Add("eine Angabe");
            var failed = new ReportEntry("Cut-Extrude1_gespiegelt", "ICE", ReportStatus.Failed, "nicht erzeugt");
            var steps = new Dictionary<string, SecondHalfStep>
            {
                ["Boss-Extrude1_gespiegelt"] = new SecondHalfStep("Boss-Extrude1", "Boss-Extrude1 (Spiegeln1)_gespiegelt", ReportStatus.Ok),
                ["Cut-Extrude1_gespiegelt"] = new SecondHalfStep("Cut-Extrude1", null, ReportStatus.Ok),
            };

            List<ReportEntry> nested = MirrorReport.Nested("Spiegeln1", new[] { ok, failed }, n => steps.TryGetValue(n, out SecondHalfStep? s) ? s : null);

            Assert.Equal("Spiegeln1 › Boss-Extrude1", nested[0].Feature);
            Assert.Equal(ReportStatus.Ok, nested[0].Status);
            Assert.Equal("Volumen und Schwerpunkt stimmen", nested[0].Verification);
            Assert.Equal(new[] { "eine Angabe", "im Ziel: „Boss-Extrude1 (Spiegeln1)_gespiegelt“" }, nested[0].Details);
            Assert.Equal("Spiegeln1 › Cut-Extrude1", nested[1].Feature);
            Assert.Equal(ReportStatus.Failed, nested[1].Status);
            Assert.Empty(nested[1].Details);

            var report = new MirrorReport();
            report.Entries.Add(new ReportEntry("Spiegeln1", "MirrorPattern", ReportStatus.Ok, "zweite Hälfte"));
            report.Entries.AddRange(nested);
            Assert.False(report.AllMirrored);
            Assert.Equal(new[] { "Spiegeln1 › Cut-Extrude1" }, report.Blocking.Select(e => e.Feature));
        }

        [Fact]
        public void A_second_half_is_not_better_than_its_first_half()
        {
            var entry = new ReportEntry("Fillet1", "Fillet", ReportStatus.Ok, "Verrundung");
            List<ReportEntry> nested = MirrorReport.Nested("Spiegeln1", new[] { entry }, _ => new SecondHalfStep("Fillet1", "Fillet1 (Spiegeln1)", ReportStatus.Warning));
            Assert.Equal(ReportStatus.Warning, nested[0].Status);
            Assert.Contains(nested[0].Details, d => d.Contains("erste Hälfte"));

            Assert.Equal(ReportStatus.Ok, MirrorFeatureRules.WithFirstHalf(ReportStatus.Ok, ReportStatus.Ok));
            Assert.Equal(ReportStatus.Ok, MirrorFeatureRules.WithFirstHalf(ReportStatus.Ok, ReportStatus.Skipped));
            Assert.Equal(ReportStatus.Warning, MirrorFeatureRules.WithFirstHalf(ReportStatus.Warning, ReportStatus.Ok));
            Assert.Equal(ReportStatus.Failed, MirrorFeatureRules.WithFirstHalf(ReportStatus.Ok, ReportStatus.Failed));
            // Not built at all stays what it is.
            Assert.Equal(ReportStatus.Skipped, MirrorFeatureRules.WithFirstHalf(ReportStatus.Skipped, ReportStatus.Warning));
            Assert.Equal(ReportStatus.Failed, MirrorFeatureRules.WithFirstHalf(ReportStatus.Failed, ReportStatus.Warning));
        }

        private static (ReportEntry, bool) Step(ReportStatus status, bool solid, bool equivalent = false) =>
            (new ReportEntry("x", "t", status, "") { HasEquivalent = equivalent }, solid);

        [Fact]
        public void Mirror_feature_status_follows_its_second_half()
        {
            ReportStatus Aggregate(bool stop, bool problems, params (ReportEntry, bool)[] steps) => MirrorFeatureRules.Aggregate(steps, stop, problems);

            // All fine, a hole's sketch that comes with the hole included.
            Assert.Equal(ReportStatus.Ok, Aggregate(false, false, Step(ReportStatus.Ok, false), Step(ReportStatus.Skipped, false, true), Step(ReportStatus.Ok, true)));
            // No solid step with a counterpart: nothing was mirrored.
            Assert.Equal(ReportStatus.Failed, Aggregate(false, false, Step(ReportStatus.Ok, false), Step(ReportStatus.Failed, true)));
            Assert.Equal(ReportStatus.Failed, Aggregate(false, false));
            Assert.Equal(ReportStatus.Failed, Aggregate(false, false, Step(ReportStatus.Skipped, true)));
            // One of two failed: a warning – but with "stop on error" the run stops here.
            Assert.Equal(ReportStatus.Warning, Aggregate(false, false, Step(ReportStatus.Ok, true), Step(ReportStatus.Failed, true)));
            Assert.Equal(ReportStatus.Failed, Aggregate(true, false, Step(ReportStatus.Ok, true), Step(ReportStatus.Failed, true)));
            // A skipped seed, a step with a note, or another problem (body check, merging).
            Assert.Equal(ReportStatus.Warning, Aggregate(false, false, Step(ReportStatus.Ok, true), Step(ReportStatus.Skipped, true)));
            Assert.Equal(ReportStatus.Warning, Aggregate(false, false, Step(ReportStatus.Warning, true)));
            Assert.Equal(ReportStatus.Warning, Aggregate(true, true, Step(ReportStatus.Ok, true)));
        }

        [Fact]
        public void A_step_that_interacts_with_the_first_half_only_informs()
        {
            Assert.Equal("Volumen und Schwerpunkt stimmen (nur zur Information: die Hälften beeinflussen sich, maßgeblich ist die Prüfung von „Spiegeln1“)",
                MirrorFeatureRules.InformationOnly("Volumen und Schwerpunkt stimmen", "Spiegeln1"));
        }

        [Fact]
        public void Second_half_steps_are_checked_one_by_one_only_for_independent_seeds()
        {
            Assert.Equal(StepCheck.PassOnly, MirrorFeatureRules.ChooseCheck(true, true));
            Assert.Equal(StepCheck.ReportOnly, MirrorFeatureRules.ChooseCheck(true, false));
            Assert.Equal(StepCheck.Off, MirrorFeatureRules.ChooseCheck(false, true));
            Assert.Equal(StepCheck.Off, MirrorFeatureRules.ChooseCheck(false, false));
        }

        [Fact]
        public void Merging_a_body_mirror_needs_a_face_as_mirror_plane()
        {
            Assert.True(MirrorFeatureRules.MergeApplies(true, true));
            // A reference plane: the flag keeps its default, SOLIDWORKS leaves the bodies apart.
            Assert.False(MirrorFeatureRules.MergeApplies(true, false));
            Assert.False(MirrorFeatureRules.MergeApplies(false, true));
            Assert.False(MirrorFeatureRules.MergeApplies(false, false));
        }

        [Fact]
        public void Seed_bodies_nobody_selected_are_named()
        {
            MirrorFeatureRecord m = Bodies("Spiegeln1", false,
                new MirrorSeedBody("Aufsatz1", Box(new Vec3(0.01, 0, 0), 1e-6), new[] { "Aufsatz1" }),
                new MirrorSeedBody("Aufsatz2", Box(new Vec3(0.03, 0, 0), 1e-6), new[] { "Aufsatz2" }));
            BodyScope Scope(params (string name, string[] features, bool selected)[] bodies) => BodyScope.Of(
                bodies.Select(b => new BodyHistory(b.name, b.features, b.selected)),
                new[] { "Aufsatz1", "Aufsatz2", "Spiegeln1", "Verrundung1" }, new string[0]);

            // Only the mirrored body selected: both seed bodies come along unasked.
            BodyScope onlyMirrored = Scope(("Aufsatz1", new[] { "Aufsatz1" }, false), ("Aufsatz2", new[] { "Aufsatz2" }, false),
                ("Spiegeln1[1]", new[] { "Spiegeln1" }, true));
            Assert.Equal(new[] { "Aufsatz1", "Aufsatz2" }, MirrorFeatureRules.UnrequestedSeedBodies(m, onlyMirrored.UnselectedBodies, false));
            // One of them selected too.
            BodyScope oneMore = Scope(("Aufsatz1", new[] { "Aufsatz1" }, true), ("Aufsatz2", new[] { "Aufsatz2" }, false),
                ("Spiegeln1[1]", new[] { "Spiegeln1" }, true));
            Assert.Equal(new[] { "Aufsatz2" }, MirrorFeatureRules.UnrequestedSeedBodies(m, oneMore.UnselectedBodies, false));
            // The mirror feature itself chosen, the whole part, or features only (BodyScope.None): nothing unasked.
            Assert.Empty(MirrorFeatureRules.UnrequestedSeedBodies(m, onlyMirrored.UnselectedBodies, true));
            Assert.Empty(MirrorFeatureRules.UnrequestedSeedBodies(m, BodyScope.None.UnselectedBodies, false));
            // A feature mirror has no seed bodies.
            var features = new MirrorFeatureRecord("Spiegeln2", "MirrorPattern", MirrorSeedKind.Features, Plane3.RightYZ);
            Assert.Empty(MirrorFeatureRules.UnrequestedSeedBodies(features, onlyMirrored.UnselectedBodies, false));

            // A later fillet renamed the seed body: it is named as it is called at the end.
            BodyScope renamed = Scope(("Verrundung1", new[] { "Aufsatz1", "Verrundung1" }, false), ("Aufsatz2", new[] { "Aufsatz2" }, true),
                ("Spiegeln1[1]", new[] { "Spiegeln1" }, true));
            Assert.Equal(new[] { "Verrundung1" }, MirrorFeatureRules.UnrequestedSeedBodies(m, renamed.UnselectedBodies, false));
            // Selected under its new name: nothing unasked, although its name at the mirror feature was another.
            BodyScope renamedSelected = Scope(("Verrundung1", new[] { "Aufsatz1", "Verrundung1" }, true), ("Aufsatz2", new[] { "Aufsatz2" }, true),
                ("Spiegeln1[1]", new[] { "Spiegeln1" }, true));
            Assert.Empty(MirrorFeatureRules.UnrequestedSeedBodies(m, renamedSelected.UnselectedBodies, false));

            // „verschmelzen“: the seed body became the merged body, which takes the mirror feature's name; selected, nothing unasked.
            MirrorFeatureRecord merged = Bodies("Spiegeln1", true, new MirrorSeedBody("Aufsatz1", Box(new Vec3(0.01, 0, 0), 1e-6), new[] { "Aufsatz1" }));
            BodyScope mergedSelected = Scope(("Spiegeln1", new[] { "Aufsatz1", "Spiegeln1" }, true));
            Assert.Empty(MirrorFeatureRules.UnrequestedSeedBodies(merged, mergedSelected.UnselectedBodies, false));

            // A body outside the selection that a seed feature only cut as well is no seed body.
            MirrorFeatureRecord cutSeed = Bodies("Spiegeln1", false,
                new MirrorSeedBody("Schnitt1", Box(new Vec3(0.01, 0, 0), 1e-6), new[] { "Aufsatz1", "Schnitt1" }));
            BodyScope cutOther = BodyScope.Of(
                new[]
                {
                    new BodyHistory("Schnitt1", new[] { "Aufsatz1", "Schnitt1" }, true),
                    new BodyHistory("Aufsatz2", new[] { "Aufsatz2", "Schnitt1" }, false),
                    new BodyHistory("Spiegeln1[1]", new[] { "Spiegeln1" }, true),
                },
                new[] { "Aufsatz1", "Schnitt1", "Spiegeln1" }, new string[0]);
            Assert.Equal(new[] { "Aufsatz2" }, cutOther.UnselectedBodies.Keys);
            Assert.Empty(MirrorFeatureRules.UnrequestedSeedBodies(cutSeed, cutOther.UnselectedBodies, false));

            string note = MirrorFeatureRules.UnrequestedNote(new[] { "Aufsatz1" });
            Assert.Equal("Nicht gewählt, aber als Saat mitgespiegelt: „Aufsatz1“. Die Kopie bekommt auch diese Körper.", note);
        }

        [Fact]
        public void Mirror_feature_message_counts_its_second_half()
        {
            var features = new MirrorFeatureRecord("Spiegeln1", "MirrorPattern", MirrorSeedKind.Features, Plane3.RightYZ);
            Assert.Equal("Features spiegeln: zweite Hälfte als eigene Features nachgebaut, 3 von 4 Schritten", MirrorFeatureRules.Message(features, 3, 4));
            MirrorFeatureRecord bodies = Bodies("Spiegeln2", true, new MirrorSeedBody("Aufsatz1", Box(Vec3.Zero, 1e-6), new[] { "Aufsatz1" }));
            Assert.Equal("Körper spiegeln (1 Körper, verschmolzen): zweite Hälfte als eigene Features nachgebaut, 2 von 2 Schritten",
                MirrorFeatureRules.Message(bodies, 2, 2));
            // No solid step came of it: what was built is removed again, nothing is counted as built.
            Assert.Equal("Features spiegeln: zweite Hälfte nicht nachgebaut – kein Volumen-Feature entstanden.", MirrorFeatureRules.NotBuilt(features));
            Assert.Equal("Körper spiegeln (1 Körper, verschmolzen): zweite Hälfte nicht nachgebaut – kein Volumen-Feature entstanden.",
                MirrorFeatureRules.NotBuilt(bodies));
            Assert.Equal("Mit Esc abgebrochen nach „Skizze1“; 2 weitere Schritte der zweiten Hälfte wurden nicht mehr aufgebaut.",
                MirrorFeatureRules.CancelledIn("Skizze1", 2));
            Assert.Equal("Mit Esc abgebrochen nach „Skizze1“.", MirrorFeatureRules.CancelledIn("Skizze1", 0));
        }

        [Fact]
        public void Reflection_is_recognised_despite_rounding_of_the_transform()
        {
            // A transform as SOLIDWORKS reports it: entries rounded to 12 digits.
            var p = new Plane3(new Vec3(0.0123, -0.004, 0.02), new Vec3(0.3, 0.8, -0.52));
            Affine exact = new Reflection(p).ToAffine();
            static Vec3 Round(Vec3 v) => new Vec3(Math.Round(v.X, 12), Math.Round(v.Y, 12), Math.Round(v.Z, 12));
            var rounded = new Affine(Round(exact.Col0), Round(exact.Col1), Round(exact.Col2), Round(exact.Translation));
            Reflection? r = Reflection.TryFromAffine(rounded, 1e-7);
            Assert.NotNull(r);
            Assert.True(r!.Plane.IsCoplanarWith(p));
        }

        [Fact]
        public void Second_half_shows_in_the_progress_title()
        {
            Assert.Equal("Spiegeln1: erste Hälfte erfassen", RunSteps.SecondHalf("Spiegeln1"));
            Assert.Equal("Spiegeln1 › Boss-Extrude1 (2/3)", RunSteps.SecondHalf("Spiegeln1", "Boss-Extrude1", 2, 3));
            Assert.Equal("Spiegelwerk – Aufbauen: Spiegeln1 › Boss-Extrude1 (2/3)",
                RunSteps.Title(RunSteps.Build, RunSteps.SecondHalf("Spiegeln1", "Boss-Extrude1", 2, 3)));
        }

        [Fact]
        public void Mirror_feature_record_is_unmirrored_and_changes_the_solid()
        {
            var p = new Plane3(new Vec3(0.01, 0, 0), Vec3.UnitX);
            var r = new MirrorFeatureRecord("Spiegeln1", "MirrorPattern", MirrorSeedKind.Features, p);
            Assert.True(r.ChangesSolid);
            Assert.Equal(p.Origin, r.Plane.Origin);
            Assert.Equal(MirrorSeedKind.Features, r.Kind);
            Assert.False(r.Merge);
            Assert.Empty(r.SeedFeatures);
            // P′ in the target.
            var run = new Reflection(new Plane3(new Vec3(0.03, 0, 0), Vec3.UnitX));
            Assert.True(r.SecondHalfPlane(run).IsCoplanarWith(new Plane3(new Vec3(0.05, 0, 0), Vec3.UnitX)));
        }
    }
}
