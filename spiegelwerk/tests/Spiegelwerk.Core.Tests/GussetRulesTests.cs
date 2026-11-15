using System;
using System.Globalization;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>
    /// The sheet-metal gusset (blech-plan S8, A4 §13 GussetRulesTests, P9): the section and the bend ends, what an
    /// offset counts from (a bend end, an earlier gusset), the variants – the mirrored end, the other end L − d
    /// (checks.py #4) –, FlipOffset by σ of a reference line, read-back comparison, texts.
    /// </summary>
    public class GussetRulesTests
    {
        private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

        /// <summary>A bend along X from x = 0 to x = 0.1 (L = 100 mm), a gusset 30 mm from the end at x = 0, 2 mm thick.</summary>
        private static SheetMetalGussetRecord OnBend(double section = 0.03, double offset = 0.03)
        {
            var r = new SheetMetalGussetRecord("Sheet Metal Gusset1", "SMGusset")
            {
                UseOffset = true,
                OffsetDistance = offset,
                GussetThickness = 0.002,
                BendLine = new Line3(new Vec3(0, 0.01, 0.01), Vec3.UnitX),
                BendEndA = new Vec3(0, 0.012, 0.01),
                BendEndB = new Vec3(0.1, 0.01, 0.012),
                SectionPoint = new Vec3(section, 0.01, 0.01),
            };
            r.SupportingFaces.Add(new FaceSignature(SurfaceKind.Plane, 0.005, new Vec3(0.05, 0.03, 0.01)) { Normal = Vec3.UnitZ });
            r.SupportingFaces.Add(new FaceSignature(SurfaceKind.Plane, 0.005, new Vec3(0.05, 0.01, 0.03)) { Normal = Vec3.UnitY });
            return r;
        }

        [Fact]
        public void The_section_is_the_middle_of_the_extent_along_the_bend()
        {
            var bend = new Line3(new Vec3(1, 2, 3), new Vec3(0, 0, -1));
            // Points of a gusset 2 mm thick around z = 2.95 (along the line: 0.05), at any distance from the line.
            var points = new[] { new Vec3(1.2, 2, 2.951), new Vec3(1, 2.3, 2.949), new Vec3(1.1, 2.1, 2.9505) };
            Vec3 s = GussetRules.Section(bend, points)!.Value;
            TestGeometry.Near(new Vec3(1, 2, 2.95), s);
            TestGeometry.Near(0.05, GussetRules.Along(bend, s));
            Assert.Null(GussetRules.Section(bend, Array.Empty<Vec3>()));

            var ends = GussetRules.Ends(bend, points)!.Value;
            TestGeometry.Near(new Vec3(1.2, 2, 2.951), ends.a);
            TestGeometry.Near(new Vec3(1, 2.3, 2.949), ends.b);
            Assert.Null(GussetRules.Ends(bend, Array.Empty<Vec3>()));
        }

        [Fact]
        public void The_implied_reference_is_the_bend_end_at_the_offset_distance()
        {
            SheetMetalGussetRecord r = OnBend();
            var (anchor, point, gusset) = GussetRules.AnchorOf(r);
            Assert.Equal(GussetAnchor.BendEnd, anchor);
            Assert.Equal(r.BendEndA, point);
            Assert.Null(gusset);
            // 70 mm from A is 30 mm from B.
            Assert.Equal(r.BendEndB, GussetRules.AnchorOf(OnBend(section: 0.07)).point);
            Assert.Equal(100e-3, GussetRules.BendLength(r)!.Value, 12);
            // At a distance from neither end, or without an offset: not determinable; with an own point: that one.
            Assert.Equal(GussetAnchor.Unknown, GussetRules.AnchorOf(OnBend(section: 0.05)).anchor);
            SheetMetalGussetRecord noOffset = OnBend();
            noOffset.UseOffset = false;
            Assert.Equal(GussetAnchor.Unknown, GussetRules.AnchorOf(noOffset).anchor);
            SheetMetalGussetRecord own = OnBend();
            own.ReferencePoint = GeometryRef.ToVertex(new Vec3(0, 0, 0));
            Assert.Equal(GussetAnchor.Own, GussetRules.AnchorOf(own).anchor);
            SheetMetalGussetRecord noBend = OnBend();
            noBend.BendLine = null;
            Assert.Equal(GussetAnchor.Unknown, GussetRules.AnchorOf(noBend).anchor);
        }

        [Fact]
        public void A_later_gusset_counts_from_the_one_inserted_before()
        {
            // The first at 30 mm, the second 20 mm on from its side face (30 + 1 + 20): the help's „edge of the gusset last inserted“.
            SheetMetalGussetRecord second = OnBend(section: 0.051, offset: 0.02);
            second.PreviousOnBend.Add(new GussetSection("Sheet Metal Gusset1", new Vec3(0.03, 0.01, 0.01), 0.002));
            var (anchor, point, gusset) = GussetRules.AnchorOf(second);
            Assert.Equal(GussetAnchor.PreviousGusset, anchor);
            Assert.Null(point);
            Assert.Equal("Sheet Metal Gusset1", gusset);
            second.Anchor = anchor;
            // From its section as well.
            SheetMetalGussetRecord fromSection = OnBend(section: 0.05, offset: 0.02);
            fromSection.PreviousOnBend.Add(new GussetSection("Sheet Metal Gusset1", new Vec3(0.03, 0.01, 0.01), 0.002));
            Assert.Equal(GussetAnchor.PreviousGusset, GussetRules.AnchorOf(fromSection).anchor);
            // The last inserted first: with two before, the later one is named.
            SheetMetalGussetRecord third = OnBend(section: 0.08, offset: 0.02);
            third.PreviousOnBend.Add(new GussetSection("A", new Vec3(0.1, 0.01, 0.01), 0.002));
            third.PreviousOnBend.Add(new GussetSection("B", new Vec3(0.06, 0.01, 0.01), 0.002));
            Assert.Equal("B", GussetRules.AnchorOf(third).gusset);
            Assert.Equal(GussetRules.FromPrevious, GussetRules.Variants(second)[0].Description);
            Assert.All(GussetRules.Variants(second), v => Assert.Equal(GussetPoint.None, v.Point));
        }

        [Fact]
        public void The_copys_bend_reversed_takes_the_mirrored_end_or_the_other_end()
        {
            // checks.py #4: a mirror across x = 0.2 reverses the bend; the copy's „end of the bend“ may be R(B).
            var mirror = new Reflection(new Plane3(new Vec3(0.2, 0, 0), Vec3.UnitX));
            SheetMetalGussetRecord r = OnBend();
            r.Anchor = GussetAnchor.BendEnd;
            r.AnchorPoint = r.BendEndA;
            Line3 bend = mirror.Apply(r.BendLine!.Value);
            Vec3 wanted = mirror.Apply(r.SectionPoint!.Value);
            Vec3 a = mirror.Apply(r.BendEndA!.Value), b = mirror.Apply(r.BendEndB!.Value);
            Vec3 Placed(Vec3 from, Vec3 toward, double offset) => GussetRules.At(bend, GussetRules.Along(bend, from) +
                                                                                         Math.Sign(GussetRules.Along(bend, toward) - GussetRules.Along(bend, from)) * offset);
            // From R(A), the end the original counted from: right.
            Assert.True(GussetRules.SectionMatches(bend, wanted, Placed(a, b, r.OffsetDistance)));
            // From R(B) with d: 40 mm off (L − 2d).
            Assert.False(GussetRules.SectionMatches(bend, wanted, Placed(b, a, r.OffsetDistance)));
            TestGeometry.Near(0.04, GussetRules.Deviation(bend, wanted, Placed(b, a, r.OffsetDistance)));
            // From R(B) with L − d: right again.
            var variants = GussetRules.Variants(r);
            GussetVariant other = variants.Single(v => v.Description == GussetRules.FromOtherEnd);
            TestGeometry.Near(0.07, other.Offset);
            Assert.True(GussetRules.SectionMatches(bend, wanted, Placed(b, a, other.Offset)));
            // The order: the mirrored end (both sides), as in the original, the other end, the other side.
            Assert.Equal(new[] { GussetPoint.Anchor, GussetPoint.Anchor, GussetPoint.None, GussetPoint.None, GussetPoint.None }, variants.Select(v => v.Point));
            Assert.Equal(new[] { false, true, false, false, true }, variants.Select(v => v.FlipToggled));
        }

        [Fact]
        public void The_other_end_is_offered_only_where_it_is_another_place()
        {
            // In the middle of the bend L − d = d: the same place.
            SheetMetalGussetRecord middle = OnBend(section: 0.05, offset: 0.05);
            middle.Anchor = GussetAnchor.BendEnd;
            Assert.DoesNotContain(GussetRules.Variants(middle), v => v.Description == GussetRules.FromOtherEnd);
            // Without the bend's ends: no length.
            SheetMetalGussetRecord noEnds = OnBend();
            noEnds.BendEndB = null;
            Assert.Null(GussetRules.BendLength(noEnds));
            Assert.DoesNotContain(GussetRules.Variants(noEnds), v => v.Description == GussetRules.FromOtherEnd);
            // Without an offset there is no other side.
            SheetMetalGussetRecord own = OnBend();
            own.UseOffset = false;
            own.ReferencePoint = GeometryRef.ToVertex(Vec3.Zero);
            own.Anchor = GussetAnchor.Own;
            Assert.Equal(new[] { GussetRules.AsOriginal }, GussetRules.Variants(own).Select(v => v.Description));
            own.UseOffset = true;
            Assert.Equal(new[] { GussetRules.AsOriginal, GussetRules.OtherSide }, GussetRules.Variants(own).Select(v => v.Description));
            Assert.All(GussetRules.Variants(own), v => Assert.Equal(GussetPoint.Own, v.Point));
            // Not determinable: no point, the other side, the other end.
            SheetMetalGussetRecord unknown = OnBend(section: 0.045);
            Assert.Equal(new[] { GussetRules.WithoutPoint, GussetRules.OtherSide, GussetRules.FromOtherEnd },
                GussetRules.Variants(unknown).Select(v => v.Description));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FlipOffset_follows_sigma_of_the_reference_line(bool flip)
        {
            // Like DraftDirectionTests: the mirrored direction against the copy's.
            var r = new Reflection(new Plane3(Vec3.Zero, Vec3.UnitX));
            Vec3 line = new Vec3(1, 0.2, 0).Normalized();
            Vec3 mirrored = r.ApplyVector(line);
            Assert.Equal(flip, GussetRules.FlipOffsetFor(flip, line, r, mirrored));
            Assert.Equal(!flip, GussetRules.FlipOffsetFor(flip, line, r, -mirrored));
            // Either direction unknown: unchanged (the section check decides).
            Assert.Equal(flip, GussetRules.FlipOffsetFor(flip, null, r, mirrored));
            Assert.Equal(flip, GussetRules.FlipOffsetFor(flip, line, r, null));
        }

        [Fact]
        public void Read_back_compares_the_values_of_the_scheme_and_not_the_place()
        {
            SheetMetalGussetRecord want = OnBend();
            want.IndentDepth = 0.01;
            want.IndentWidth = 0.008;
            SheetMetalGussetRecord got = OnBend(section: 0.07, offset: 0.07);
            got.IndentDepth = 0.01;
            got.IndentWidth = 0.008;
            // Offset and side are the section check's; unused values are not looked at.
            got.FlipOffset = true;
            got.ProfileLengthDim = 0.5;
            got.DraftAngle = 0.3;
            Assert.True(GussetRules.Differences(want, got).None);
            got.IndentDepth = 0.012;
            Assert.Equal(new[] { "Einzugstiefe: erwartet 10 mm, gelesen 12 mm" }, GussetRules.Differences(want, got, De).Differences);

            SheetMetalGussetRecord legs = OnBend();
            legs.ProfileDimensionScheme = GussetProfileDimTypes.ProfileDimensions;
            legs.ProfileLengthDim = 0.02;
            legs.ProfileHeightDim = 0.03;
            SheetMetalGussetRecord legsGot = OnBend();
            legsGot.ProfileDimensionScheme = GussetProfileDimTypes.ProfileDimensions;
            legsGot.ProfileLengthDim = 0.02;
            legsGot.ProfileHeightDim = 0.03;
            legsGot.IndentDepth = 0.4;
            Assert.True(GussetRules.Differences(legs, legsGot).None);
            legsGot.FlipDimSides = true;
            Assert.Equal(new[] { "d1 und d2 getauscht: erwartet nein, gelesen ja" }, GussetRules.Differences(legs, legsGot, De).Differences);
        }

        [Fact]
        public void Description_and_texts()
        {
            SheetMetalGussetRecord r = OnBend();
            r.IndentDepth = 0.01;
            Assert.Equal("Versteifungsrippe (gerundet), Einzug 10 mm, Dicke 2 mm", GussetRules.Describe(r, De));
            r.GussetType = GussetTypes.Flat;
            r.ProfileDimensionScheme = GussetProfileDimTypes.ProfileDimensions;
            r.ProfileLengthDim = 0.02;
            r.ProfileHeightDim = 0.03;
            Assert.Equal("Versteifungsrippe (flach), Schenkel 20 mm × 30 mm, Dicke 2 mm", GussetRules.Describe(r, De));
            Assert.Equal("Sheet: Lage weicht um 0,5 mm ab.", GussetRules.Tried("Sheet", 0.0005, De));
            Assert.Equal("Sheet: Lage nicht messbar.", GussetRules.Tried("Sheet", null, De));
            Assert.StartsWith("Lage der Versteifungsrippe weicht um 0,02 mm", GussetRules.PlaceOff(0.00002, "x", De));
            Assert.Equal(1e-5, GussetRules.SectionTolerance);
            Assert.Contains("nicht nachgebaut", GussetRules.CustomProfile);
            r.Anchor = GussetAnchor.PreviousGusset;
            r.AnchorGusset = "G1";
            Assert.Equal("Versatz zählt im Original von der Versteifungsrippe „G1“.", GussetRules.AnchorNote(r));
            r.Anchor = GussetAnchor.Own;
            Assert.Equal("", GussetRules.AnchorNote(r));
        }

        [Fact]
        public void The_gusset_is_rebuilt_and_never_refuses_a_run()
        {
            Assert.Equal(SheetMetalState.Rebuilt, SheetMetalTypes.Of("SMGusset")!.State);
            Assert.Equal(SheetMetalRole.Leaf, SheetMetalTypes.Of("SMGusset")!.Role);
            Assert.False(UnsupportedFeatures.RefusesRun("SMGusset"));
            Assert.Equal("Blech-Feature: Feature-Daten nicht lesbar.", UnsupportedFeatures.Reason("SMGusset"));
        }
    }
}
