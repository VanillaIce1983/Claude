using System;
using System.Collections.Generic;
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
    /// Corners, folds and the cross break (blech-plan S5; A3 #7–#10, #12, #13, #15): overlap ↔ underlap,
    /// the selections of a closed corner, the lengths of the 2019 corner-relief call, bend references and
    /// the bend matcher, sets of faces, corners and break lines, read-back comparisons, the type table.
    /// </summary>
    public class SheetMetalCornerTests
    {
        private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

        private static FaceSignature Plane(double area, Vec3 sample, Vec3 normal) => new FaceSignature(SurfaceKind.Plane, area, sample) { Normal = normal };

        private static FaceSignature Cylinder(double area, Vec3 sample) => new FaceSignature(SurfaceKind.Cylinder, area, sample);

        /// <summary>A face as its sample point: the distance of a point to it is the distance to that point (enough to tell faces apart here).</summary>
        private static double Distance(FaceSignature face, Vec3 p) => face.Sample.DistanceTo(p);

        [Fact]
        public void Overlap_and_underlap_swap_butt_stays()
        {
            Assert.Equal(ClosedCornerTypes.Underlap, CornerRules.SwapOverlap(ClosedCornerTypes.Overlap));
            Assert.Equal(ClosedCornerTypes.Overlap, CornerRules.SwapOverlap(ClosedCornerTypes.Underlap));
            Assert.Equal(ClosedCornerTypes.Butt, CornerRules.SwapOverlap(ClosedCornerTypes.Butt));
            foreach (int t in new[] { 1, 2, 3 }) Assert.Equal(t, CornerRules.SwapOverlap(CornerRules.SwapOverlap(t)));
            Assert.False(CornerRules.HasOverlapRatio(ClosedCornerTypes.Butt));
            Assert.True(CornerRules.HasOverlapRatio(ClosedCornerTypes.Overlap));
        }

        [Fact]
        public void Closed_corner_is_tried_with_marks_1_then_alternating_then_0()
        {
            var s = CornerRules.ClosedCornerSelections(4);
            Assert.Equal(3, s.Count);
            Assert.Equal(new[] { 1, 1, 1, 1 }, s[0].marks);
            Assert.Equal(new[] { 1, CornerRules.MatchMark, 1, CornerRules.MatchMark }, s[1].marks);
            Assert.Equal(new[] { 0, 0, 0, 0 }, s[2].marks);
            Assert.Equal(0x40000000, CornerRules.MatchMark);
            // One face: nothing to alternate.
            Assert.Equal(2, CornerRules.ClosedCornerSelections(1).Count);
        }

        [Fact]
        public void Legacy_corner_relief_lengths()
        {
            var square = new CornerReliefItem { ReliefType = CornerReliefTypes.SquareRelief, SlotLength = 0.004, SlotWidth = 0.009, CornerFilletDiameter = 0.002 };
            Assert.Equal((0.004, 0.0), CornerRules.LegacyReliefLengths(square));
            square.AddFilletedCorners = true;
            // The fillet as a diameter in the data, as a radius in the call.
            Assert.Equal((0.004, 0.001), CornerRules.LegacyReliefLengths(square));
            var obround = new CornerReliefItem { ReliefType = CornerReliefTypes.ObroundRelief, SlotLength = 0.006, SlotWidth = 0.002 };
            Assert.Equal((0.006, 0.002), CornerRules.LegacyReliefLengths(obround));
            var circular = new CornerReliefItem { ReliefType = CornerReliefTypes.CircularRelief, SlotLength = 0.005 };
            Assert.Equal((0.0025, 0.0), CornerRules.LegacyReliefLengths(circular));
            foreach (int other in new[] { CornerReliefTypes.BendWaistRelief, CornerReliefTypes.TearRelief, CornerReliefTypes.ConstantWidthRelief, CornerReliefTypes.Mixed,
                         CornerReliefTypes.FullRoundRelief, CornerReliefTypes.SuitCaseRelief, CornerReliefTypes.RectangularRelief })
                Assert.Null(CornerRules.LegacyReliefLengths(new CornerReliefItem { ReliefType = other, SlotLength = 0.003 }));
        }

        [Fact]
        public void BendRef_mirror_keeps_names_and_mirrors_faces()
        {
            var r = new Reflection(new Plane3(new Vec3(0.05, 0, 0), Vec3.UnitX));
            var b = new BendRef("Edge-Flange1", "EdgeBend2", 1, 3);
            b.Faces.Add(Cylinder(1e-4, new Vec3(0.01, 0.02, 0.03)));
            b.Faces.Add(Plane(2e-4, new Vec3(0.02, 0.02, 0.03), Vec3.UnitX));
            BendRef m = b.Mirror(r);
            Assert.Equal("Edge-Flange1", m.Owner);
            Assert.Equal("EdgeBend2", m.Name);
            Assert.Equal(1, m.Ordinal);
            Assert.Equal(3, m.OwnerBendCount);
            Assert.Equal(2, m.Faces.Count);
            TestGeometry.Near(new Vec3(0.09, 0.02, 0.03), m.Faces[0].Sample);
            TestGeometry.Near(-Vec3.UnitX, m.Faces[1].Normal);
            Assert.Equal(2e-4, m.Faces[1].Area);
            // The original is untouched.
            TestGeometry.Near(new Vec3(0.01, 0.02, 0.03), b.Faces[0].Sample);
        }

        [Fact]
        public void Ordinal_is_usable_only_for_single_bend_owners()
        {
            Assert.True(new BendRef("Hem1", "HemBend1", 0, 1).OrdinalUsable);
            // Several bends: a reflection may reverse their order (A3 P8).
            Assert.False(new BendRef("Edge-Flange1", "EdgeBend1", 0, 2).OrdinalUsable);
            Assert.False(new BendRef("", "Bend1", 0, 1).OrdinalUsable);
            Assert.False(new BendRef("Hem1", "HemBend1", -1, 1).OrdinalUsable);
            Assert.True(FoldRules.UseOwnersOnlyBend(new BendRef("Hem1", "HemBend1", 0, 1), 1));
            // The copy of the owner has another number of bends: no stand-in.
            Assert.False(FoldRules.UseOwnersOnlyBend(new BendRef("Hem1", "HemBend1", 0, 1), 2));
        }

        [Fact]
        public void Bend_matcher_takes_full_matches_only_and_prefers_own_bodies()
        {
            FaceSignature inner = Cylinder(1e-4, new Vec3(0, 0, 0)), outer = Cylinder(2e-4, new Vec3(0, 0, 0.002));
            var wanted = new List<FaceSignature> { inner, outer };
            var full = new List<FaceSignature> { outer, inner };                                  // same faces, other order
            var partial = new List<FaceSignature> { inner, Cylinder(2e-4, new Vec3(0.5, 0, 0)) }; // one face elsewhere
            var extra = new List<FaceSignature> { inner, outer, Plane(1e-4, Vec3.UnitY, Vec3.UnitZ) };
            var candidates = new List<IReadOnlyList<FaceSignature>> { partial, full, extra };
            double Dist(int c, int k, Vec3 p) => Distance(candidates[c][k], p);

            MatchResult m = BendMatcher.FindBend(wanted, candidates, Dist);
            Assert.True(m.Found);
            Assert.Equal(1, m.Index);
            Assert.False(m.Ambiguous);

            // A partial match alone is no bend.
            Assert.False(BendMatcher.FindBend(wanted, new List<IReadOnlyList<FaceSignature>> { partial }, (c, k, p) => Distance(partial[k], p)).Found);
            // No faces wanted: nothing to find.
            Assert.False(BendMatcher.FindBend(new List<FaceSignature>(), candidates, Dist).Found);

            // The original's bend and the copy's at the same place (same part): ambiguous, the own one wins …
            var twins = new List<IReadOnlyList<FaceSignature>> { full, full };
            MatchResult both = BendMatcher.FindBend(wanted, twins, (c, k, p) => Distance(twins[c][k], p), preferred: c => c == 1);
            Assert.True(both.Ambiguous);
            Assert.Equal(1, both.Index);
            // … and with the original excluded (separate body) it is the only one.
            MatchResult own = BendMatcher.FindBend(wanted, twins, (c, k, p) => Distance(twins[c][k], p), allowed: c => c == 1);
            Assert.False(own.Ambiguous);
            Assert.Equal(1, own.Index);
            Assert.False(BendMatcher.FindBend(wanted, twins, (c, k, p) => Distance(twins[c][k], p), allowed: c => false).Found);
        }

        [Fact]
        public void Set_match_finds_a_pairing_where_greedy_would_not()
        {
            // Wanted 0 fits 0 and 1 (0 better), wanted 1 fits only 0: greedy would take 0 for 0 and fail.
            double[,] cost = { { 1, 2 }, { 1, double.PositiveInfinity } };
            Assert.Equal(new[] { 1, 0 }, SetMatch.Pair(2, 2, (i, j) => cost[i, j]));
            Assert.Null(SetMatch.Pair(2, 3, (i, j) => 0));
            Assert.Equal(new[] { 2 }, SetMatch.Pair(1, 3, (i, j) => j == 2 ? 0 : 1, sameCount: false));
            Assert.Equal(Array.Empty<int>(), SetMatch.Pair(0, 0, (i, j) => 0));
        }

        [Fact]
        public void Face_sets_compare_kind_area_normal_and_place()
        {
            FaceSignature a = Plane(1e-3, new Vec3(0, 0, 0), Vec3.UnitZ), b = Plane(2e-3, new Vec3(0.1, 0, 0), Vec3.UnitX);
            var got = new List<FaceSignature> { b, a };
            Assert.True(FaceSets.Same(new[] { a, b }, got, (k, p) => Distance(got[k], p)));
            // Another normal, area or place: not the same face.
            var turned = new List<FaceSignature> { Plane(1e-3, Vec3.Zero, -Vec3.UnitZ), b };
            Assert.False(FaceSets.Same(new[] { a, b }, turned, (k, p) => Distance(turned[k], p)));
            var bigger = new List<FaceSignature> { Plane(1.1e-3, Vec3.Zero, Vec3.UnitZ), b };
            Assert.False(FaceSets.Same(new[] { a, b }, bigger, (k, p) => Distance(bigger[k], p)));
            var moved = new List<FaceSignature> { Plane(1e-3, new Vec3(0, 0.01, 0), Vec3.UnitZ), b };
            Assert.False(FaceSets.Same(new[] { a, b }, moved, (k, p) => Distance(moved[k], p)));
            // A face too many or too few.
            Assert.False(FaceSets.Same(new[] { a }, got, (k, p) => Distance(got[k], p)));
        }

        [Fact]
        public void Corner_relief_item_mirror_keeps_face_roles()
        {
            var r = new Reflection(new Plane3(Vec3.Zero, Vec3.UnitY));
            var c = new CornerReliefItem
            {
                Face1 = Plane(1e-3, new Vec3(0, 0.01, 0), Vec3.UnitX),
                Face2 = Plane(2e-3, new Vec3(0, 0.02, 0), Vec3.UnitZ),
                Face3 = Plane(3e-3, new Vec3(0, 0.03, 0), Vec3.UnitY),
                ReliefType = CornerReliefTypes.ObroundRelief,
                SuitCaseType = 2,
                SlotLength = 0.006,
                SlotWidth = 0.002,
                CornerFilletDiameter = 0.001,
                AddFilletedCorners = true,
                CenterOnBendLines = true,
                RatioToThickness = true,
                TangentToBend = true,
            };
            CornerReliefItem m = c.Mirror(r);
            TestGeometry.Near(new Vec3(0, -0.01, 0), m.Face1.Sample);
            TestGeometry.Near(new Vec3(0, -0.02, 0), m.Face2.Sample);
            TestGeometry.Near(new Vec3(0, -0.03, 0), m.Face3!.Sample);
            TestGeometry.Near(-Vec3.UnitY, m.Face3.Normal);
            Assert.Equal(new[] { 1e-3, 2e-3, 3e-3 }, m.Faces().Select(f => f.Area));
            Assert.Empty(CornerRules.Differences(c, m, legacy: false));
            // Two-bend corner: no third face.
            Assert.Null(new CornerReliefItem { Face1 = c.Face1, Face2 = c.Face2 }.Mirror(r).Face3);
        }

        [Fact]
        public void Corners_pair_by_their_faces_in_any_order()
        {
            FaceSignature f1 = Plane(1e-3, new Vec3(0, 0, 0), Vec3.UnitX), f2 = Plane(2e-3, new Vec3(0, 0.1, 0), Vec3.UnitY);
            FaceSignature g1 = Plane(1e-3, new Vec3(0.5, 0, 0), Vec3.UnitX), g2 = Plane(2e-3, new Vec3(0.5, 0.1, 0), Vec3.UnitY);
            var want = new List<CornerReliefItem> { new CornerReliefItem { Face1 = f1, Face2 = f2 }, new CornerReliefItem { Face1 = g1, Face2 = g2 } };
            // The copy lists the corners the other way round, and the faces of one corner swapped.
            var got = new List<IReadOnlyList<FaceSignature>> { new[] { g1, g2 }, new[] { f2, f1 } };
            Assert.Equal(new[] { 1, 0 }, CornerRules.PairCorners(want, got, (j, k, p) => Distance(got[j][k], p)));
            var lost = new List<IReadOnlyList<FaceSignature>> { new[] { g1, g2 }, new[] { f1 } };
            Assert.Null(CornerRules.PairCorners(want, lost, (j, k, p) => Distance(lost[j][k], p)));
        }

        [Fact]
        public void Corner_relief_differences_compare_what_was_written()
        {
            var want = new CornerReliefItem { ReliefType = CornerReliefTypes.SquareRelief, SlotLength = 0.004, SuitCaseType = 1 };
            var got = new CornerReliefItem { ReliefType = CornerReliefTypes.SquareRelief, SlotLength = 0.005, SuitCaseType = 0 };
            List<string> d = CornerRules.Differences(want, got, legacy: false, De);
            Assert.Contains("Schlitzlänge: erwartet 4 mm, gelesen 5 mm", d);
            Assert.Contains("Kofferform: erwartet 1, gelesen 0", d);
            // The 2019 call sets no suitcase type: not compared there.
            List<string> legacy = CornerRules.Differences(want, got, legacy: true, De);
            Assert.DoesNotContain(legacy, x => x.StartsWith("Kofferform"));
            Assert.Contains("Länge: erwartet 4 mm, gelesen 5 mm", legacy);
            // A ratio is a factor, not a length.
            var ratio = new CornerReliefItem { ReliefType = CornerReliefTypes.ObroundRelief, RatioToThickness = true, SlotLength = 1.5 };
            Assert.Contains("Schlitzlänge: erwartet 1,5, gelesen 1,6", CornerRules.Differences(ratio, new CornerReliefItem
            {
                ReliefType = CornerReliefTypes.ObroundRelief,
                RatioToThickness = true,
                SlotLength = 1.6,
            }, false, De));
        }

        [Fact]
        public void Break_lines_compare_as_sets_either_way_round()
        {
            Vec3 a = new Vec3(0, 0, 0), b = new Vec3(0.1, 0.05, 0), c = new Vec3(0.1, 0, 0), d = new Vec3(0, 0.05, 0);
            var want = new List<(Vec3, Vec3)> { (a, b), (c, d) };
            Assert.True(CornerRules.SameLines(want, new List<(Vec3, Vec3)> { (d, c), (b, a) }));
            Assert.False(CornerRules.SameLines(want, new List<(Vec3, Vec3)> { (a, b) }));
            Assert.False(CornerRules.SameLines(want, new List<(Vec3, Vec3)> { (a, b), (c, d + new Vec3(0.001, 0, 0)) }));
            Assert.True(CornerRules.SameLines(new List<(Vec3, Vec3)>(), new List<(Vec3, Vec3)>()));
        }

        [Fact]
        public void Break_lines_are_the_drawn_lines_in_model_coordinates()
        {
            var sketch = new Sketch.SketchModel { Name = "Skizze7", Frame = TestGeometry.Top };
            sketch.Points.Add(new Sketch.SkPoint("p1", 0, 0));
            sketch.Points.Add(new Sketch.SkPoint("p2", 0.1, 0.05));
            sketch.Points.Add(new Sketch.SkPoint("p3", 0.1, 0));
            sketch.Segments.Add(new Sketch.SkSegment("l1", Sketch.SkSegmentKind.Line) { StartId = "p1", EndId = "p2" });
            sketch.Segments.Add(new Sketch.SkSegment("c1", Sketch.SkSegmentKind.Line) { StartId = "p1", EndId = "p3", Construction = true });
            List<(Vec3 a, Vec3 b)> lines = CornerRules.BreakLines(sketch);
            Assert.Single(lines);
            // Top: sketch y is model −z.
            TestGeometry.Near(Vec3.Zero, lines[0].a);
            TestGeometry.Near(new Vec3(0.1, 0, -0.05), lines[0].b);
            // Mirrored, the lines of the mirrored sketch are the mirrored lines.
            var r = new Reflection(new Plane3(new Vec3(0.05, 0, 0), Vec3.UnitX));
            Assert.True(CornerRules.SameLines(lines.Select(l => (r.Apply(l.a), r.Apply(l.b))).ToList(),
                new List<(Vec3, Vec3)> { (new Vec3(0, 0, -0.05), new Vec3(0.1, 0, 0)) }));
        }

        [Fact]
        public void Read_back_comparisons_name_what_differs()
        {
            var want = new ClosedCornerRecord("C", "CornerFeat") { CornerType = ClosedCornerTypes.Butt, Gap = 0.0001, OverlapRatio = 0.5 };
            var got = new ClosedCornerRecord("C", "CornerFeat") { CornerType = ClosedCornerTypes.Butt, Gap = 0.0001, OverlapRatio = 0.8 };
            // A butt corner has no overlap ratio.
            Assert.True(CornerRules.Differences(want, got).None);
            want.CornerType = got.CornerType = ClosedCornerTypes.Overlap;
            Assert.Equal(new[] { "Überlappungsverhältnis: erwartet 0,5, gelesen 0,8" }, CornerRules.Differences(want, got, De).Differences);

            var bw = new BreakCornerRecord("B", "BreakCorner") { BreakType = BreakCornerTypes.Fillet, Distance = 0.002 };
            var bg = new BreakCornerRecord("B", "BreakCorner") { BreakType = BreakCornerTypes.Fillet, Distance = 0.002, InternalCornersOnly = true };
            Assert.Equal(new[] { "Nur innere Ecken: erwartet nein, gelesen ja" }, CornerRules.Differences(bw, bg, De).Differences);

            var cw = new CrossBreakRecord("X", "CrossBreak") { Angle = Math.PI / 180, Radius = 0.001 };
            var cg = new CrossBreakRecord("X", "CrossBreak") { Angle = Math.PI / 180, Radius = 0.001, Reverse = true };
            Assert.Equal(new[] { "Richtung umgekehrt: erwartet nein, gelesen ja" }, CornerRules.Differences(cw, cg, De).Differences);
            cg.Reverse = false;
            Assert.True(CornerRules.Differences(cw, cg).None);
        }

        [Fact]
        public void Fold_selections_follow_the_examples()
        {
            IReadOnlyList<FoldSelection> unfold = FoldRules.Selections(FoldKind.Unfold, hasUnfold: true);
            Assert.Equal(new[] { (1, 4, false), (1, 2, false) }, unfold.Select(s => (s.FixedMark, s.BendMark, s.WithUnfold)));
            IReadOnlyList<FoldSelection> fold = FoldRules.Selections(FoldKind.Fold, hasUnfold: true);
            Assert.Equal(new[] { (1, 4, false), (1, 4, true), (1, 2, false) }, fold.Select(s => (s.FixedMark, s.BendMark, s.WithUnfold)));
            Assert.Equal(2, FoldRules.Selections(FoldKind.Fold, hasUnfold: false).Count);
            Assert.True(FoldRules.SameBends(new[] { "B1", "B2" }, new[] { "B2", "B1" }));
            Assert.False(FoldRules.SameBends(new[] { "B1", "B2" }, new[] { "B1" }));
        }

        [Fact]
        public void Fold_record_takes_its_kind_from_the_type_name()
        {
            Assert.Equal(FoldKind.Unfold, new FoldRecord("Unfold1", "UnFold").Kind);
            Assert.Equal(FoldKind.Fold, new FoldRecord("Fold1", "Fold").Kind);
            Assert.Equal("Abwickeln, 2 Biegungen", FoldRules.Describe(new FoldRecord("U", "UnFold") { Bends = { new BendRef("E", "B1", 0, 2), new BendRef("E", "B2", 1, 2) } }));
            Assert.Equal("Zurückbiegen, 1 Biegung", FoldRules.Describe(new FoldRecord("F", "Fold") { Bends = { new BendRef("E", "B1", 0, 1) } }));
        }

        [Fact]
        public void Cross_break_owns_its_line_sketch_and_changes_no_solid()
        {
            var r = new CrossBreakRecord("Cross-Break1", "CrossBreak");
            Assert.Empty(r.OwnedSketches);
            Assert.False(r.BringsParts);
            r.LineSketchName = "Sketch7";
            Assert.Equal(new[] { "Sketch7" }, r.OwnedSketches);
            Assert.True(r.BringsParts);
            Assert.True(r.DeleteWithAbsorbed);
            Assert.False(r.ChangesSolid);

            var tree = SheetMetalTree.Of(new[]
            {
                new TreeFeature("Base-Flange1", "SMBaseFlange"),
                new TreeFeature("Cross-Break1", "CrossBreak", null, new[] { new TreeFeature("Sketch7", "ProfileFeature") }),
            });
            Assert.Equal(("Cross-Break1", PartOwnerKind.CrossBreak), tree.Owners["Sketch7"]);
            Assert.Equal("Skizze der Bruchlinien des Querbruchs „Cross-Break1“, der nicht gespiegelt wird – entfällt mit ihm.",
                new PartOfFeatureRecord("Sketch7", "ProfileFeature", "Cross-Break1", PartOwnerKind.CrossBreak).LeftOut);
            Assert.False(new PartOfFeatureRecord("Sketch7", "ProfileFeature", "Cross-Break1", PartOwnerKind.CrossBreak).OwnerHasPositionSketch);
        }

        [Fact]
        public void Unsupported_features_of_area_3()
        {
            // S5 decides closed corner, break corner, unfold/fold and cross break: rebuilt, never refused.
            foreach (string type in new[] { "CornerFeat", "BreakCorner", "UnFold", "Fold", "CrossBreak" })
            {
                Assert.True(UnsupportedFeatures.IsSheetMetal(type), type);
                Assert.Equal(SheetMetalState.Rebuilt, SheetMetalTypes.Of(type)!.State);
                Assert.Equal(SheetMetalRole.Leaf, SheetMetalTypes.Of(type)!.Role);
                Assert.False(UnsupportedFeatures.RefusesRun(type), type);
                Assert.Equal("Blech-Feature: Feature-Daten nicht lesbar.", UnsupportedFeatures.Reason(type));
            }
            // The user corner trim: not rebuilt, non-solid, no fallback (A3 P3).
            Assert.StartsWith("Ecke trimmen (abgewickelt):", UnsupportedFeatures.Reason("CornerTrim"));
            Assert.False(SheetMetalTypes.TriggersFallback("CornerTrim"));
            Assert.False(UnsupportedFeatures.ChangesSolid("CrossBreak"));
            Assert.True(UnsupportedFeatures.ChangesSolid("CornerFeat"));
            // The corner relief's name is not confirmed (B44): not refused by name, only its text by that name.
            Assert.False(UnsupportedFeatures.IsSheetMetal("CornerRelief"));
            Assert.False(UnsupportedFeatures.RefusesRun("CornerRelief"));
            Assert.True(UnsupportedFeatures.ChangesSolid("CornerRelief"));
            Assert.Equal(CornerRules.CornerReliefTooOld, UnsupportedFeatures.Reason("CornerRelief"));
            // A lofted bend carries a body and is not decided yet: refused with the fallback off and on (S7a decides it).
            Assert.True(UnsupportedFeatures.RefusesBeforeCapture("LoftedBend", partOfAcceptedOwner: false, fallbackOn: false));
            Assert.True(UnsupportedFeatures.RefusesBeforeCapture("LoftedBend", partOfAcceptedOwner: false, fallbackOn: true));
            // The welded corner has no confirmed name: a generic record, which triggers the fallback until it is named (K17).
            Assert.True(SheetMetalTypes.TriggersFallback("WeldedCorner"));
            Assert.False(UnsupportedFeatures.IsSheetMetal("WeldedCorner"));
        }

        [Fact]
        public void Descriptions_name_the_corner()
        {
            Assert.Equal("Geschlossene Ecke überlappend, 2 Flächen", CornerRules.Describe(new ClosedCornerRecord("C", "CornerFeat")
            {
                CornerType = ClosedCornerTypes.Overlap,
                Faces = { Plane(1, Vec3.Zero, Vec3.UnitX), Plane(1, Vec3.UnitY, Vec3.UnitX) },
            }));
            Assert.Equal("Gebrochene Ecke Verrundung 2 mm, 4 Kanten", CornerRules.Describe(new BreakCornerRecord("B", "BreakCorner")
            {
                Distance = 0.002,
                Edges = { E(), E(), E(), E() },
            }, De));
            Assert.Equal("Querbruch 1°", CornerRules.Describe(new CrossBreakRecord("X", "CrossBreak") { Angle = Math.PI / 180 }, De));
            Assert.Equal("Eckentlastung, 4 Ecken", CornerRules.Describe(new CornerReliefRecord("R", "CornerRelief")
            {
                Corners = { new CornerReliefItem(), new CornerReliefItem(), new CornerReliefItem(), new CornerReliefItem() },
            }));
        }

        private static EdgeSignature E() => new EdgeSignature(CurveKind.Line, Vec3.Zero, Vec3.UnitX, Vec3.UnitX / 2, 1);
    }
}
