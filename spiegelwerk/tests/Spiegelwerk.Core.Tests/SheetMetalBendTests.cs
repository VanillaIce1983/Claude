using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>
    /// Sketched bend and jog (blech-plan S4; A3 #1–#3, #14, A2 P6/P7): creation codes, the first guess
    /// for the direction, bend lines, witnesses, how variants compare, read-back comparison.
    /// </summary>
    public class SheetMetalBendTests
    {
        private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

        [Fact]
        public void Bend_position_maps_to_the_creation_code()
        {
            // swFlangePositionTypes_e (4 = centre line) → BendPos of InsertSheetMetal3dBend (0 = centre line).
            Assert.Equal((short)0, SheetMetalBendRules.BendPosForCreation(4));
            Assert.Equal((short)1, SheetMetalBendRules.BendPosForCreation(1));
            Assert.Equal((short)2, SheetMetalBendRules.BendPosForCreation(2));
            Assert.Equal((short)3, SheetMetalBendRules.BendPosForCreation(3));
            foreach (int other in new[] { 0, 5, 6, 7 }) Assert.Null(SheetMetalBendRules.BendPosForCreation(other));
        }

        [Fact]
        public void First_reverse_guess_prefers_the_sheet_sigma()
        {
            foreach (bool reverse in new[] { false, true })
                foreach (int sketch in new[] { 1, -1 })
                {
                    // Without σ of the sheet (B39 open) the sketch's σ decides.
                    Assert.Equal(DirectionRules.ReverseFlag(reverse, sketch), SheetMetalBendRules.FirstReverseGuess(reverse, null, sketch));
                    foreach (int sheet in new[] { 1, -1 })
                        Assert.Equal(DirectionRules.ReverseFlag(reverse, sheet), SheetMetalBendRules.FirstReverseGuess(reverse, sheet, sketch));
                }
            // On the mirrored face σ = +1: the flag stays.
            Assert.True(SheetMetalBendRules.FirstReverseGuess(true, null, 1));
            Assert.True(SheetMetalBendRules.FirstReverseGuess(false, null, -1));
        }

        [Fact]
        public void Reverse_rule_mirrors_the_bent_direction_for_either_reference()
        {
            // A bent direction cos θ·u + s·sin θ·o against the reference o; the copy's reference is σ·R(o), whichever
            // normal SOLIDWORKS uses. With the flag from the rule the copy bends to R(direction) (A3 bendcheck).
            var rnd = new Random(5);
            for (int k = 0; k < 1000; k++)
            {
                var r = new Reflection(new Plane3(TestGeometry.RandomPoint(rnd), TestGeometry.RandomUnit(rnd)));
                Vec3 u = TestGeometry.RandomUnit(rnd);
                Vec3 o = u.Cross(TestGeometry.RandomUnit(rnd)).Normalized();
                double theta = rnd.NextDouble() * Math.PI;
                bool reverse = rnd.Next(2) == 0;
                int sigma = rnd.Next(2) == 0 ? 1 : -1;
                double s = reverse ? -1 : 1;
                Vec3 bent = u * Math.Cos(theta) + o * (s * Math.Sin(theta));
                Vec3 o2 = r.ApplyVector(o) * sigma;
                bool reverse2 = SheetMetalBendRules.FirstReverseGuess(reverse, null, sigma);
                Vec3 bent2 = r.ApplyVector(u) * Math.Cos(theta) + o2 * ((reverse2 ? -1 : 1) * Math.Sin(theta));
                TestGeometry.Near(r.ApplyVector(bent), bent2, 1e-12);
            }
        }

        [Fact]
        public void Variant_comparison()
        {
            // A feature in error never wins, even with fewer misses.
            Assert.True(SheetMetalBendRules.CompareVariants(5, false, 0, true) < 0);
            Assert.True(SheetMetalBendRules.CompareVariants(0, true, 5, false) > 0);
            // Otherwise fewer missed witnesses win.
            Assert.True(SheetMetalBendRules.CompareVariants(1, false, 3, false) < 0);
            Assert.True(SheetMetalBendRules.CompareVariants(3, false, 1, false) > 0);
            Assert.Equal(0, SheetMetalBendRules.CompareVariants(2, false, 2, false));
        }

        [Fact]
        public void Bend_lines_are_the_drawn_lines_in_model_coordinates()
        {
            var sketch = new SketchModel { Name = "Skizze2", Frame = TestGeometry.Top };
            sketch.Points.Add(new SkPoint("p1", 0.05, -0.01));
            sketch.Points.Add(new SkPoint("p2", 0.05, 0.06));
            sketch.Points.Add(new SkPoint("p3", 0, 0));
            sketch.Points.Add(new SkPoint("p4", 0.1, 0));
            sketch.Segments.Add(new SkSegment("l1", SkSegmentKind.Line) { StartId = "p1", EndId = "p2" });
            sketch.Segments.Add(new SkSegment("c1", SkSegmentKind.Line) { StartId = "p3", EndId = "p4", Construction = true });
            List<Line3> lines = SheetMetalBendRules.BendLines(sketch);
            Assert.Single(lines);
            // Top: sketch y is model −z.
            TestGeometry.Near(new Vec3(0.05, 0, -0.025), lines[0].Point);
            Assert.True(lines[0].Direction.IsParallelTo(Vec3.UnitZ, 1e-9));
        }

        [Fact]
        public void Witnesses_share_the_places_and_count_each_point_once()
        {
            var bends = Enumerable.Range(0, 20).Select(i => new Vec3(i * 0.001, 0, 0)).ToList();
            var moved = Enumerable.Range(0, 3).Select(i => new Vec3(0, i * 0.001 + 0.01, 0)).ToList();
            var turned = Enumerable.Range(0, 10).Select(i => new Vec3(0, 0, i * 0.001 + 0.01)).ToList();
            List<Vec3> w = SheetMetalBendRules.PickWitnesses(new IReadOnlyList<Vec3>[] { bends, moved, turned });
            Assert.Equal(SheetMetalBendRules.MaxWitnesses, w.Count);
            // Every group is represented; the short one entirely.
            Assert.Contains(w, p => p.Y == 0 && p.Z == 0);
            Assert.All(moved, m => Assert.Contains(m, w));
            Assert.Contains(w, p => p.Z > 0);
            // Duplicates count once; an empty group takes no share.
            var twice = new[] { Vec3.Zero, new Vec3(0, 0, 1e-9), Vec3.UnitX };
            Assert.Equal(2, SheetMetalBendRules.PickWitnesses(new IReadOnlyList<Vec3>[] { twice, Array.Empty<Vec3>() }).Count);
            Assert.Empty(SheetMetalBendRules.PickWitnesses(new IReadOnlyList<Vec3>[] { Array.Empty<Vec3>() }));
        }

        [Fact]
        public void The_fixed_point_witnesses_only_off_the_bend_zone()
        {
            var lines = new[] { new Line3(new Vec3(0.05, 0, 0), Vec3.UnitY) };
            Assert.True(SheetMetalBendRules.FixedPointWitnesses(new Vec3(0.02, 0, 0), lines, Vec3.UnitZ, 0.004));
            Assert.False(SheetMetalBendRules.FixedPointWitnesses(new Vec3(0.048, 0, 0), lines, Vec3.UnitZ, 0.004));
        }

        [Fact]
        public void Sketched_bend_read_back_compares_values_but_not_the_direction()
        {
            var want = new SketchedBendRecord("Biegung1", "SM3dBend", "Skizze2") { Angle = Math.PI / 2, UseDefaultRadius = false, Radius = 0.002, PositionType = 3, Reverse = true };
            var same = new SketchedBendRecord("Biegung1", "SM3dBend", "") { Angle = Math.PI / 2, UseDefaultRadius = false, Radius = 0.002, PositionType = 3, Reverse = false };
            Assert.True(SheetMetalBendRules.Differences(want, same, De).None);
            // A bend of 270° is not one of 90° (not folded like a flange angle).
            var other = new SketchedBendRecord("Biegung1", "SM3dBend", "") { Angle = 1.5 * Math.PI, UseDefaultRadius = false, Radius = 0.003, PositionType = 4 };
            Deviations d = SheetMetalBendRules.Differences(want, other, De);
            Assert.Equal(3, d.Differences.Count);
            Assert.Contains(d.Differences, x => x.StartsWith("Biegewinkel"));
            Assert.Contains(d.Differences, x => x.StartsWith("Biegeradius"));
            Assert.Contains(d.Differences, x => x.StartsWith("Biegeposition"));
        }

        [Fact]
        public void Sketched_bend_hands_its_radius_and_allowance_to_its_bends()
        {
            var r = new SketchedBendRecord("Biegung1", "SM3dBend", "Skizze2")
            {
                UseDefaultRadius = false,
                Radius = 0.002,
                OwnAllowance = new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = 0.3 },
            };
            BendValues parent = SheetMetalBendRules.Parent(r);
            var bends = new List<BendValues>
            {
                new BendValues { Name = "SketchBend1", UseDefaultRadius = false, Radius = 0.002, UseDefaultAllowance = false, Allowance = new BendAllowanceData { KFactor = 0.3 } },
                new BendValues { Name = "SketchBend2", UseDefaultRadius = false, Radius = 0.004 },
            };
            List<BendOverride> own = BendOverrides.Differences(parent, bends, De);
            Assert.Equal(new[] { "SketchBend2" }, own.Select(o => o.Bend));
            Assert.True(r.SampleSubFeatures);
            Assert.False(r.BringsParts);
        }

        [Fact]
        public void Jog_creation_codes()
        {
            // DimPos 1-based as read, BendPos 0-based (B41).
            Assert.Equal((short)1, JogRules.DimPos(JogDimensionPositions.InsideOffset));
            Assert.Equal((short)3, JogRules.DimPos(JogDimensionPositions.OverallPosition));
            Assert.Null(JogRules.DimPos(0));
            Assert.Null(JogRules.DimPos(4));
            Assert.Equal((short)0, JogRules.BendPos(JogPositions.BendCenterline));
            Assert.Equal((short)3, JogRules.BendPos(JogPositions.BendOutside));
            Assert.Null(JogRules.BendPos(0));
            Assert.Null(JogRules.BendPos(5));
            // The same 0-based encoding as InsertSheetMetal3dBend: centre line, inside, outside, bend outside.
            Assert.Equal(SheetMetalBendRules.BendPosForCreation(FlangePositionTypes.MaterialOutside), JogRules.BendPos(JogPositions.MaterialOutside));
            // Degrees first (B42), radians the retry.
            TestGeometry.Near(90, JogRules.CreationAngle(Math.PI / 2, inDegrees: true), 1e-12);
            TestGeometry.Near(Math.PI / 2, JogRules.CreationAngle(Math.PI / 2, inDegrees: false), 1e-12);
        }

        [Fact]
        public void Jog_offset_is_created_blind_and_edited_to_its_type()
        {
            var blind = new JogRecord("Kröpfung1", "Jog", "Skizze3") { OffsetType = JogOffsetTypes.Blind, OffsetDistance = 0.01 };
            TestGeometry.Near(0.01, JogRules.CreationOffset(blind));
            Assert.False(JogRules.NeedsOffsetEdit(blind));
            var upTo = new JogRecord("Kröpfung1", "Jog", "Skizze3") { OffsetType = JogOffsetTypes.UpToSurface, OffsetDistance = 0 };
            TestGeometry.Near(0.001, JogRules.CreationOffset(upTo));
            Assert.True(JogRules.NeedsOffsetEdit(upTo));
            Assert.True(JogRules.UsesReference(JogOffsetTypes.FromSurface));
            Assert.False(JogRules.UsesReference(JogOffsetTypes.MidPlane));
            Assert.True(JogRules.UsesDistance(JogOffsetTypes.MidPlane));
            Assert.False(JogRules.UsesDistance(JogOffsetTypes.UpToVertex));
        }

        [Fact]
        public void Jog_sketch_needs_exactly_one_line()
        {
            var s = new SketchModel();
            s.Points.Add(new SkPoint("a", 0, 0));
            s.Points.Add(new SkPoint("b", 0, 1));
            s.Segments.Add(new SkSegment("l", SkSegmentKind.Line) { StartId = "a", EndId = "b" });
            s.Segments.Add(new SkSegment("c", SkSegmentKind.Line) { StartId = "a", EndId = "b", Construction = true });
            Assert.Equal("l", JogRules.JogLine(s, out string? why));
            Assert.Null(why);
            s.Segments.Add(new SkSegment("l2", SkSegmentKind.Line) { StartId = "a", EndId = "b" });
            Assert.Null(JogRules.JogLine(s, out why));
            Assert.Contains("genau eine Linie", why);
            Assert.Null(JogRules.JogLine(new SketchModel(), out why));
            Assert.NotNull(why);
        }

        [Fact]
        public void Jog_read_back_compares_what_the_jog_uses()
        {
            var want = new JogRecord("Kröpfung1", "Jog", "Skizze3")
            {
                Angle = Math.PI / 2, OffsetType = JogOffsetTypes.Blind, OffsetDistance = 0.01, DimensionPosition = 2, JogPosition = 1,
                FixProjectedLength = true, ReverseDirection = true, ReverseOffset = true,
            };
            var got = new JogRecord("Kröpfung1", "Jog", "")
            {
                Angle = Math.PI / 2, OffsetType = JogOffsetTypes.Blind, OffsetDistance = 0.01, DimensionPosition = 2, JogPosition = 1,
                FixProjectedLength = true,
            };
            // The direction is the witnesses' business; ReverseOffset of a blind jog has no reference to refer to.
            Assert.True(JogRules.Differences(want, got, De).None);
            got.OffsetDistance = 0.012;
            got.JogPosition = 3;
            Deviations d = JogRules.Differences(want, got, De);
            Assert.Equal(2, d.Differences.Count);
            // With a reference the offset's direction is compared (relative to the reference: unchanged by R).
            want.OffsetType = got.OffsetType = JogOffsetTypes.FromSurface;
            got.OffsetDistance = 0.01;
            got.JogPosition = 1;
            Assert.Single(JogRules.Differences(want, got, De).Differences, x => x.StartsWith("Versatz umgekehrt"));
            Assert.Equal("Kröpfung 90°, Versatz 10 mm", JogRules.Describe(want, De));
        }
    }
}
