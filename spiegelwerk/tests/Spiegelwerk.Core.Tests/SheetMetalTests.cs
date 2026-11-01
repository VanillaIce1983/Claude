using System;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Verification;
using Xunit;
using static Spiegelwerk.Core.Tests.TestGeometry;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>Base flange and flat pattern (blech-plan S1a, A1 §13): sides, end conditions, creation values, flat check.</summary>
    public class SheetMetalTests
    {
        private static Vec3 InPlaneUnit(Random rnd, Vec3 n)
        {
            Vec3 v = RandomUnit(rnd);
            return (v - n * v.Dot(n)).Normalized();
        }

        /// <summary>A random reflection, a sketch normal, a segment direction in the sketch, and the copy's sketch normal ±R(n).</summary>
        private static (Reflection r, Vec3 n, Vec3 t, Vec3 nCopy, int sigma) RandomCase(Random rnd)
        {
            var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
            Vec3 n = RandomUnit(rnd);
            Vec3 t = InPlaneUnit(rnd, n);
            // The plane the copy sketches on is R(S); SOLIDWORKS decides which way its normal points.
            Vec3 nCopy = rnd.Next(2) == 0 ? r.ApplyVector(n) : -r.ApplyVector(n);
            return (r, n, t, nCopy, DirectionRules.Sigma(n, r, nCopy));
        }

        [Fact]
        public void Closed_profile_plate_lies_on_the_mirrored_side()
        {
            var rnd = new Random(31);
            for (int i = 0; i < 500; i++)
            {
                var (r, n, _, nCopy, sigma) = RandomCase(rnd);
                bool reverse = rnd.Next(2) == 0;
                var (flag, _) = SheetMetalRules.BaseFlangeFlags(true, reverse, rnd.Next(2) == 0, sigma);
                // The plate grows along ±n like a blind boss.
                Near(r.ApplyVector(reverse ? -n : n), flag ? -nCopy : nCopy);
            }
        }

        [Fact]
        public void Open_profile_contour_side_is_mirrored_under_model_a()
        {
            var rnd = new Random(37);
            for (int i = 0; i < 500; i++)
            {
                var (r, n, t, nCopy, sigma) = RandomCase(rnd);
                bool reverse = rnd.Next(2) == 0;
                // Model a: the material side is ±n × t; the copy keeps start → end, t′ = R(t).
                Vec3 side = (reverse ? -1 : 1) * n.Cross(t);
                Vec3 tCopy = r.ApplyVector(t);
                bool flag = SheetMetalRules.InPlaneSideFlag(reverse, sigma);
                Near(r.ApplyVector(side), (flag ? -1 : 1) * nCopy.Cross(tCopy));
                Assert.Equal(flag, SheetMetalRules.BaseFlangeFlags(false, false, reverse, sigma).reverseThickness);
            }
        }

        [Fact]
        public void Model_b_needs_a_flip_only_where_the_rules_differ()
        {
            var rnd = new Random(41);
            for (int i = 0; i < 500; i++)
            {
                var (r, n, t, nCopy, sigma) = RandomCase(rnd);
                bool reverseDepth = rnd.Next(2) == 0, reverseSide = rnd.Next(2) == 0;
                // Model b: the side is ±d × t with d the actual extrusion direction.
                Vec3 d = reverseDepth ? -n : n;
                var (depthFlag, sideFlag) = SheetMetalRules.BaseFlangeFlags(false, reverseDepth, reverseSide, sigma);
                Vec3 dCopy = depthFlag ? -nCopy : nCopy;
                Near(r.ApplyVector(d), dCopy);
                Vec3 want = r.ApplyVector((reverseSide ? -1 : 1) * d.Cross(t));
                Vec3 got = (sideFlag ? -1 : 1) * dCopy.Cross(r.ApplyVector(t));
                // Model a's rule is right under model b exactly where σ = +1; "always flip" would be right everywhere.
                Assert.Equal(sigma > 0, want.DistanceTo(got) < 1e-9);
                Near(want, (!reverseSide ? -1 : 1) * dCopy.Cross(r.ApplyVector(t)));
            }
        }

        [Fact]
        public void Sketch_on_right_mirrored_at_right_flips_depth_keeps_side()
        {
            var r = new Reflection(Plane3.RightYZ);
            // The copy reuses the right plane: σ = −1.
            int sigma = DirectionRules.Sigma(Vec3.UnitX, r, Vec3.UnitX);
            Assert.Equal(-1, sigma);
            Assert.Equal((true, false), SheetMetalRules.BaseFlangeFlags(false, false, false, sigma));
        }

        [Fact]
        public void Sketch_on_front_mirrored_at_right_keeps_depth_flips_side()
        {
            var r = new Reflection(Plane3.RightYZ);
            int sigma = DirectionRules.Sigma(Vec3.UnitZ, r, Vec3.UnitZ);
            Assert.Equal(1, sigma);
            Assert.Equal((false, true), SheetMetalRules.BaseFlangeFlags(false, false, false, sigma));
        }

        [Fact]
        public void BaseFlangeFlags_closed_profile_never_touches_ReverseThickness()
        {
            foreach (int sigma in new[] { 1, -1 })
                foreach (bool rt in new[] { false, true })
                {
                    Assert.Equal(rt, SheetMetalRules.BaseFlangeFlags(true, false, rt, sigma).reverseThickness);
                    // Symmetric thickness has no side either.
                    Assert.Equal(rt, SheetMetalRules.BaseFlangeFlags(false, false, rt, sigma, symmetricThickness: true).reverseThickness);
                }
        }

        [Fact]
        public void Flange_offset_types_map_to_end_conditions_and_back()
        {
            var expected = new[] { EndCondition.Blind, EndCondition.UpToVertex, EndCondition.UpToSurface, EndCondition.OffsetFromSurface, EndCondition.MidPlane };
            for (int t = 1; t <= 5; t++)
            {
                Assert.Equal(expected[t - 1], SheetMetalRules.FromFlangeOffset(t));
                Assert.Equal(t, SheetMetalRules.ToFlangeOffset(expected[t - 1]));
            }
            // 6 is "up to edge and merge" (newer versions): not rebuilt.
            foreach (int unknown in new[] { 0, 6, 99 }) Assert.Null(SheetMetalRules.FromFlangeOffset(unknown));
            Assert.Null(SheetMetalRules.ToFlangeOffset(EndCondition.ThroughAll));
        }

        private static SheetMetalParameters Params(double t, double r, double k) =>
            new SheetMetalParameters { Thickness = t, BendRadius = r, BendAllowance = new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = k } };

        [Fact]
        public void Creation_values_use_defaults_for_the_first_body_and_the_template_otherwise()
        {
            SheetMetalParameters defaults = Params(0.002, 0.001, 0.44), body = Params(0.003, 0.002, 0.3), template = Params(0.0025, 0.0015, 0.5);
            FlangeCreation first = SheetMetalRules.CreationValues(true, false, defaults, body, null, null, null, null, 0.009, 0.009);
            Assert.Equal(0.002, first.Thickness);
            Assert.Equal(0.001, first.BendRadius);
            Assert.Equal(0.44, first.Allowance!.KFactor);
            Assert.True(first.UseDefaultRelief);

            // A later body: what the target has, so its defaults stay; no allowance object unless the flange has its own.
            FlangeCreation later = SheetMetalRules.CreationValues(false, false, defaults, body, template, null, null, null, 0.009, 0.009);
            Assert.Equal(0.0025, later.Thickness);
            Assert.Null(later.Allowance);

            var own = new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = 0.38 };
            var relief = new ReliefData { Kind = ReliefKind.Obround };
            FlangeCreation withOwn = SheetMetalRules.CreationValues(false, false, defaults, body, template, null, own, relief, 0.009, 0.009);
            Assert.Same(own, withOwn.Allowance);
            Assert.False(withOwn.UseDefaultRelief);
            Assert.Same(relief, withOwn.Relief);
        }

        [Fact]
        public void Tab_takes_the_target_body_values()
        {
            SheetMetalParameters defaults = Params(0.002, 0.001, 0.44), targetBody = Params(0.004, 0.003, 0.4);
            FlangeCreation tab = SheetMetalRules.CreationValues(false, true, defaults, null, Params(0.002, 0.001, 0.44), targetBody, null, null, 0.009, 0.009);
            Assert.Equal(0.004, tab.Thickness);
            Assert.Equal(0.003, tab.BendRadius);
            Assert.Null(tab.Allowance);
        }

        [Fact]
        public void Pre_2013_original_uses_body_as_defaults()
        {
            SheetMetalParameters body = Params(0.003, 0.002, 0.3);
            FlangeCreation first = SheetMetalRules.CreationValues(true, false, null, body, null, null, null, null, 0.009, 0.009);
            Assert.Equal(0.003, first.Thickness);
            Assert.Equal(0.3, first.Allowance!.KFactor);
            // Nothing read at all: the flange's own values.
            Assert.Equal(0.009, SheetMetalRules.CreationValues(true, false, null, null, null, null, null, null, 0.009, 0.008).Thickness);
        }

        [Fact]
        public void Expected_body_delta_is_one_for_a_new_body_and_zero_for_a_tab()
        {
            Assert.Equal(1, SheetMetalRules.ExpectedBodyDelta(false));
            Assert.Equal(0, SheetMetalRules.ExpectedBodyDelta(true));
        }

        [Fact]
        public void Creation_path_order_puts_C_first_only_for_a_tab_in_separate_body_mode()
        {
            Assert.Equal(new[] { BaseFlangePath.DataObject, BaseFlangePath.V1, BaseFlangePath.V2 }, SheetMetalRules.CreationPathOrder(false));
            Assert.Equal(new[] { BaseFlangePath.V2, BaseFlangePath.DataObject, BaseFlangePath.V1 }, SheetMetalRules.CreationPathOrder(true));
            Assert.Equal("InsertSheetMetalBaseFlange2", SheetMetalRules.PathName(BaseFlangePath.V2));
        }

        [Fact]
        public void Body_overrides_are_set_only_where_the_original_could_read_them()
        {
            var body = new SheetMetalParameters { OverrideBendParameters = true, OverrideAutoRelief = true, OverrideReadCode = 0 };
            Assert.Equal(new[] { 0, 2 }, SheetMetalRules.BodyOverrides(body));
            body.OverrideReadCode = 2;
            Assert.Empty(SheetMetalRules.BodyOverrides(body));
            Assert.Empty(SheetMetalRules.BodyOverrides(null));
        }

        [Fact]
        public void First_bodys_overrides_wait_for_a_second_body_or_become_the_part_values()
        {
            // B15: SetOverrideDefaultParameter2 does not apply while the copy is single-body.
            Assert.Equal(BodyOverrideMode.Deferred, SheetMetalRules.BodyOverrideModeOf(first: true, tab: false, overrides: 1, staysSingleBody: false));
            Assert.Equal(BodyOverrideMode.PartValues, SheetMetalRules.BodyOverrideModeOf(first: true, tab: false, overrides: 2, staysSingleBody: true));
            // Later bodies: the copy is multibody once they exist.
            Assert.Equal(BodyOverrideMode.Apply, SheetMetalRules.BodyOverrideModeOf(first: false, tab: false, overrides: 1, staysSingleBody: false));
            // Nothing of its own, or a tab sharing the SheetMetal of its body.
            Assert.Equal(BodyOverrideMode.None, SheetMetalRules.BodyOverrideModeOf(first: true, tab: false, overrides: 0, staysSingleBody: false));
            Assert.Equal(BodyOverrideMode.None, SheetMetalRules.BodyOverrideModeOf(first: false, tab: true, overrides: 1, staysSingleBody: false));
        }

        [Fact]
        public void Flange_on_a_body_waiting_for_its_thickness_is_read_back_against_what_the_body_has_now()
        {
            // B15: until the second body, the first body and its tabs have the defaults – no deviation the copy will not keep.
            Assert.True(SheetMetalRules.ReadsBackCurrentBendValues(new[] { 0 }, null, false));
            Assert.True(SheetMetalRules.ReadsBackCurrentBendValues(new[] { 1, 0 }, false, false));
            // Not waiting, or only for allowance and relief: thickness and radius are already its own.
            Assert.False(SheetMetalRules.ReadsBackCurrentBendValues(null, null, false));
            Assert.False(SheetMetalRules.ReadsBackCurrentBendValues(new[] { 1, 2 }, null, false));
            // Its own thickness over the defaults, or from the gauge table: the original's.
            Assert.False(SheetMetalRules.ReadsBackCurrentBendValues(new[] { 0 }, true, false));
            Assert.False(SheetMetalRules.ReadsBackCurrentBendValues(new[] { 0 }, null, true));
        }

        [Fact]
        public void Own_values_take_the_overridden_groups_from_the_body_and_the_rest_from_the_defaults()
        {
            var defaults = new SheetMetalParameters
            {
                Thickness = 0.002, BendRadius = 0.001,
                BendAllowance = new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = 0.5 },
                Relief = new ReliefData { Kind = ReliefKind.Rectangular, Ratio = 0.5 },
            };
            var body = new SheetMetalParameters
            {
                Thickness = 0.003, BendRadius = 0.004,
                BendAllowance = new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = 0.44 },
                Relief = new ReliefData { Kind = ReliefKind.Obround, Ratio = 0.7 },
                OverrideBendParameters = true, OverrideReadCode = 0,
            };
            SheetMetalParameters own = SheetMetalRules.OwnValues(defaults, body);
            Assert.Equal(0.003, own.Thickness);
            Assert.Equal(0.004, own.BendRadius);
            Assert.Equal(0.5, own.BendAllowance.KFactor);
            Assert.Equal(ReliefKind.Rectangular, own.Relief.Kind);
            body.OverrideBendAllowance = body.OverrideAutoRelief = true;
            own = SheetMetalRules.OwnValues(defaults, body);
            Assert.Equal(0.44, own.BendAllowance.KFactor);
            Assert.Equal(ReliefKind.Obround, own.Relief.Kind);
            // Before 2013 (no defaults): everything is the body's.
            Assert.Equal(0.003, SheetMetalRules.OwnValues(null, body).Thickness);
        }

        [Fact]
        public void Copy_stays_single_body_with_one_base_flange_of_its_own()
        {
            var one = new BaseFlangeRecord("Base-Flange1", "SMBaseFlange", "Sketch1");
            var tab = new BaseFlangeRecord("Base-Flange2", "SMBaseFlange", "Sketch2") { MergesIntoExisting = true };
            var two = new BaseFlangeRecord("Base-Flange3", "SMBaseFlange", "Sketch3");
            Assert.True(SheetMetalRules.StaysSingleBody(new FeatureRecord[] { one, tab, new ExtrudeRecord("Cut-Extrude1", "Cut", "Sketch4") { IsCut = true } }));
            Assert.False(SheetMetalRules.StaysSingleBody(new FeatureRecord[] { one, tab, two }));
            Assert.True(SheetMetalRules.StaysSingleBody(Array.Empty<FeatureRecord>()));
        }

        [Fact]
        public void Flat_pattern_fixed_face_is_a_face_or_an_edge()
        {
            var fp = new FlatPatternRecord("Flat-Pattern1", "FlatPattern", "Base-Flange1");
            fp.FixedFace = GeometryRef.ToEdge(new Matching.EdgeSignature(Matching.CurveKind.Line, Vec3.Zero, Vec3.UnitX, Vec3.UnitX * 0.5, 1));
            Assert.Throws<ArgumentException>(() => fp.FixedFace = GeometryRef.ToAxis(new Line3(Vec3.Zero, Vec3.UnitX), "Kante"));
            Assert.False(fp.ChangesSolid);
            Assert.True(fp.BringsParts);
        }

        // ---- flat check on an analytic L sheet (A1 P16): 60 + 25 mm, width 100, t = r = 2 mm

        private const double L1 = 0.060, L2 = 0.025, W = 0.100, T = 0.002, Rad = 0.002;

        /// <summary>A box in local coordinates of the sheet, mapped to the model by <paramref name="map"/>.</summary>
        private static MassState Box(Func<Vec3, Vec3> map, double x0, double x1, double y0, double y1, double z0, double z1)
        {
            double v = (x1 - x0) * (y1 - y0) * (z1 - z0);
            return new MassState(v, map(new Vec3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2)) * v);
        }

        /// <summary>
        /// Folded: leg A along +x on z ∈ [0, t], a 90° bend up about the axis through (L1, ·, t + r),
        /// leg B upwards. The centroid of an annular sector lies on its bisector at
        /// (2/3)·(r₂³ − r₁³)/(r₂² − r₁²)·sin(α/2)/(α/2).
        /// </summary>
        private static MassState Folded(Func<Vec3, Vec3> map)
        {
            double r1 = Rad, r2 = Rad + T, alpha = Math.PI / 2;
            double area = alpha / 2 * (r2 * r2 - r1 * r1);
            double d = 2.0 / 3 * (r2 * r2 * r2 - r1 * r1 * r1) / (r2 * r2 - r1 * r1) * Math.Sin(alpha / 2) / (alpha / 2);
            Vec3 c = new Vec3(L1, W / 2, T + Rad) + new Vec3(Math.Sqrt(0.5), 0, -Math.Sqrt(0.5)) * d;
            var bend = new MassState(area * W, map(c) * (area * W));
            return Box(map, 0, L1, 0, W, 0, T) + bend + Box(map, L1 + Rad, L1 + Rad + T, 0, W, T + Rad, T + Rad + L2);
        }

        private static double Allowance(double k) => Math.PI / 2 * (Rad + k * T);

        /// <summary>Flattened with leg A or leg B fixed; <paramref name="ba"/> is the bend allowance.</summary>
        private static MassState Flat(Func<Vec3, Vec3> map, bool fixedA, double ba) => fixedA
            ? Box(map, 0, L1 + ba + L2, 0, W, 0, T)
            : Box(map, L1 + Rad, L1 + Rad + T, 0, W, T + Rad - ba - L1, T + Rad + L2);

        /// <summary>The copy's flat change against the original's (fixed face A, K <paramref name="k"/>), mirrored – compared as the builder does.</summary>
        private static MassComparison Check(Reflection r, Frame3 frame, double k, bool copyFixedA, double copyBa)
        {
            Func<Vec3, Vec3> original = frame.ToWorld;
            Func<Vec3, Vec3> copy = p => r.Apply(frame.ToWorld(p));
            MassState expected = (Flat(original, true, Allowance(k)) - Folded(original)).Mirror(r);
            MassState after = Flat(copy, copyFixedA, copyBa);
            return MassComparison.Compare(expected, after - Folded(copy), after);
        }

        [Fact]
        public void Flat_change_of_the_copy_matches_the_mirrored_change()
        {
            var rnd = new Random(43);
            foreach (double k in new[] { 0.5, 0.44 })
                for (int i = 0; i < 50; i++)
                {
                    var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
                    Assert.True(Check(r, RandomFrame(rnd), k, true, Allowance(k)).Passes);
                }
            // With K = 0.5 flattening adds no net volume, but the moment still tells the cases apart.
            MassState change = Flat(p => p, true, Allowance(0.5)) - Folded(p => p);
            Assert.True(Math.Abs(change.Volume) < 1e-15);
            Assert.True(change.Moment.Length > 1e-9);
        }

        [Fact]
        public void Wrong_fixed_face_fails_on_the_moment()
        {
            var rnd = new Random(47);
            foreach (double k in new[] { 0.5, 0.44 })
                for (int i = 0; i < 50; i++)
                {
                    var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
                    MassComparison c = Check(r, RandomFrame(rnd), k, false, Allowance(k));
                    Assert.False(c.Passes);
                    Assert.True(c.MomentRatio > 1e3, $"Moment nur {c.MomentRatio}-fach über der Toleranz");
                }
        }

        [Fact]
        public void Wrong_k_factor_fails_on_the_volume()
        {
            var rnd = new Random(53);
            foreach (var (k, wrong) in new[] { (0.5, 0.44), (0.44, 0.5) })
                for (int i = 0; i < 50; i++)
                {
                    var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
                    MassComparison c = Check(r, RandomFrame(rnd), k, true, Allowance(wrong));
                    Assert.False(c.Passes);
                    Assert.True(c.VolumeRatio > 1e3, $"Volumen nur {c.VolumeRatio}-fach über der Toleranz");
                }
        }

        [Fact]
        public void Bend_allowance_off_by_0_1_mm_fails()
        {
            var rnd = new Random(59);
            foreach (double k in new[] { 0.5, 0.44 })
                foreach (double off in new[] { 1e-4, -1e-4 })
                {
                    var r = new Reflection(new Plane3(RandomPoint(rnd), RandomUnit(rnd)));
                    Assert.False(Check(r, RandomFrame(rnd), k, true, Allowance(k) + off).Passes);
                }
        }

        [Fact]
        public void Base_flange_and_flat_pattern_own_their_parts_but_only_the_flange_touches_the_defaults()
        {
            var bf = new BaseFlangeRecord("Base-Flange1", "SMBaseFlange", "Sketch1");
            Assert.True(bf.BringsParts);
            Assert.True(bf.TouchesSheetMetalDefaults);
            Assert.False(bf.DeleteWithAbsorbed);
            Assert.True(bf.ChangesSolid);
            var fp = new FlatPatternRecord("Flat-Pattern1", "FlatPattern", "Base-Flange1");
            Assert.False(fp.TouchesSheetMetalDefaults);
            Assert.False(fp.DeleteWithAbsorbed);
            Assert.Contains(SheetMetalRules.FlatDeviates, new[] { "Abwicklung weicht ab – K-Faktor/Biegezugabe/feste Fläche prüfen." });
            Assert.True(new[] { SheetMetalRules.MaterialNote, SheetMetalRules.MaterialSharedNote, SheetMetalRules.GrainNote }.All(s => s.EndsWith(".")));
        }
    }
}
