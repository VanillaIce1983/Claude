using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>Legacy Insert Bends (blech-plan S6, A1 phase 1b): arguments, values, refusal by tree and position.</summary>
    public class LegacyBendsTests
    {
        private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

        private static TreeFeature F(string name, string type, string[]? parents = null, params TreeFeature[] subs) =>
            new TreeFeature(name, type, parents, subs);

        [Fact]
        public void Legacy_insert_bends_arguments_follow_the_allowance_kind()
        {
            InsertBendsArguments? k = SheetMetalRules.LegacyArguments(new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = 0.44 }, 0.5);
            Assert.NotNull(k);
            Assert.Equal(("", 0.44, -1.0, 0.5), (k!.BendTable, k.KFactor, k.BendAllowance, k.OffsetRatio));

            InsertBendsArguments? ba = SheetMetalRules.LegacyArguments(new BendAllowanceData { Kind = BendAllowanceKind.Direct, BendAllowance = 0.0021, KFactor = 0.3 }, 1);
            Assert.Equal(("", -1.0, 0.0021), (ba!.BendTable, ba.KFactor, ba.BendAllowance));

            InsertBendsArguments? table = SheetMetalRules.LegacyArguments(new BendAllowanceData { Kind = BendAllowanceKind.BendTable, BendTableFile = @"C:\t\stahl.btl" }, 0.5);
            Assert.Equal((@"C:\t\stahl.btl", -1.0, -1.0), (table!.BendTable, table.KFactor, table.BendAllowance));

            // What InsertBends2 cannot take: a bend deduction, a calculation table, a table without its file.
            Assert.Null(SheetMetalRules.LegacyArguments(new BendAllowanceData { Kind = BendAllowanceKind.Deduction, BendDeduction = 0.002 }, 0.5));
            Assert.Null(SheetMetalRules.LegacyArguments(new BendAllowanceData { Kind = BendAllowanceKind.BendCalculationTable }, 0.5));
            Assert.Null(SheetMetalRules.LegacyArguments(new BendAllowanceData { Kind = BendAllowanceKind.BendTable }, 0.5));
            // Then created with a K-factor, the allowance set afterwards: the one the object holds, else 0.5.
            Assert.Equal(0.38, SheetMetalRules.LegacyPlaceholder(new BendAllowanceData { Kind = BendAllowanceKind.Deduction, KFactor = 0.38 }, 0.5)!.KFactor);
            Assert.Equal(0.5, SheetMetalRules.LegacyPlaceholder(new BendAllowanceData { Kind = BendAllowanceKind.Deduction, KFactor = 0 }, 0.5)!.KFactor);
            Assert.Equal(-1.0, SheetMetalRules.LegacyPlaceholder(new BendAllowanceData(), 0.5)!.BendAllowance);
        }

        [Fact]
        public void Offset_ratio_outside_0_05_to_2_is_rejected()
        {
            var k = new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = 0.5 };
            Assert.NotNull(SheetMetalRules.LegacyArguments(k, 0.05));
            Assert.NotNull(SheetMetalRules.LegacyArguments(k, 2.0));
            Assert.Null(SheetMetalRules.LegacyArguments(k, 0.04));
            Assert.Null(SheetMetalRules.LegacyArguments(k, 2.01));
            Assert.Null(SheetMetalRules.LegacyArguments(k, 0));
            Assert.Null(SheetMetalRules.LegacyPlaceholder(k, 3));
            // Without auto relief the ratio means nothing: one the call takes; with it, the original's stays – and is rejected.
            Assert.Equal(SheetMetalRules.DefaultOffsetRatio, SheetMetalRules.LegacyOffsetRatio(new ReliefData { UseAutoRelief = false, Ratio = 0 }));
            Assert.Equal(0.7, SheetMetalRules.LegacyOffsetRatio(new ReliefData { UseAutoRelief = false, Ratio = 0.7 }));
            Assert.Equal(0, SheetMetalRules.LegacyOffsetRatio(new ReliefData { UseAutoRelief = true, Ratio = 0 }));
            Assert.False(SheetMetalRules.OffsetRatioValid(SheetMetalRules.LegacyOffsetRatio(new ReliefData { UseAutoRelief = true, Ratio = 0 })));
            Assert.Equal("Verhältnis der Entlastung 2,5 liegt außerhalb 0,05–2; „Biegungen einfügen“ nimmt es nicht.", SheetMetalRules.LegacyRatioInvalid(2.5, De));
        }

        [Fact]
        public void Creation_values_follow_the_base_flange_rule()
        {
            var body = new SheetMetalParameters { Thickness = 0.002, BendRadius = 0.003, OverrideReadCode = 1 };
            var defaults = new SheetMetalParameters { Thickness = 0.002, BendRadius = 0.001 };
            var template = new SheetMetalParameters { Thickness = 0.002, BendRadius = 0.0015 };
            // The first body makes the part's values (the original's defaults, or the body's own in a single-body copy).
            Assert.Same(defaults, SheetMetalRules.LegacyCreationValues(true, defaults, body, template, defaults));
            Assert.Same(body, SheetMetalRules.LegacyCreationValues(true, null, body, null, null));
            // Every later one is created with what the target has; its own values follow on its SheetMetal.
            Assert.Same(template, SheetMetalRules.LegacyCreationValues(false, defaults, body, template, defaults));
            // A target without defaults has nothing to protect: the body's own values (no readable flags: all of them).
            Assert.Same(body, SheetMetalRules.LegacyCreationValues(false, defaults, body, null, defaults));
            var overriding = new SheetMetalParameters { Thickness = 0.002, BendRadius = 0.003, OverrideReadCode = 0, OverrideBendParameters = true };
            SheetMetalParameters own = SheetMetalRules.LegacyCreationValues(false, defaults, overriding, null, new SheetMetalParameters { BendRadius = 0.001, Relief = new ReliefData { Ratio = 0.7 } });
            Assert.Equal((0.003, 0.7), (own.BendRadius, own.Relief.Ratio));
            // A single-body original (no readable flags): its values are its own – the group that differs from the target's defaults is overridden.
            Assert.Equal(new[] { 0 }, SheetMetalRules.LegacyOverrides(body, template, defaults));
            Assert.Empty(SheetMetalRules.LegacyOverrides(new SheetMetalParameters { Thickness = 0.002, BendRadius = 0.0015, OverrideReadCode = 1 }, template, defaults));
        }

        [Fact]
        public void A_legacy_body_counts_as_a_sheet_metal_body_of_its_own()
        {
            var flange = new BaseFlangeRecord("Base-Flange1", "SMBaseFlange", "Sketch1");
            var legacy = new LegacyBendsRecord("Sheet-Metal2", "SheetMetal");
            Assert.True(SheetMetalRules.StaysSingleBody(new FeatureRecord[] { legacy }));
            Assert.False(SheetMetalRules.StaysSingleBody(new FeatureRecord[] { flange, legacy }));
            // Deleting the original is refused after a run with it, as with every sheet-metal record.
            Assert.Equal(SheetMetalRules.DeleteRefused, SheetMetalRules.DeleteRefusal(new FeatureRecord[] { legacy }));
        }

        [Fact]
        public void The_record_measures_until_its_process_bends_and_brings_its_parts()
        {
            var r = new LegacyBendsRecord("Sheet-Metal1", "SheetMetal");
            Assert.Equal("Sheet-Metal1", r.MeasureUntil);
            r.FlattenBendsName = "Flatten-Bends1";
            Assert.Equal("Flatten-Bends1", r.MeasureUntil);
            r.ProcessBendsName = "Process-Bends1";
            Assert.Equal("Process-Bends1", r.MeasureUntil);
            Assert.True(r.BringsParts);
            Assert.True(r.TouchesSheetMetalDefaults);
            Assert.True(r.ChangesSolid);
            Assert.False(r.DeleteWithAbsorbed);
            // The fixed reference is a face or an edge, never an axis.
            r.FixedFace = GeometryRef.ToFace(new FaceSignature(SurfaceKind.Plane, 1e-3, Geometry.Vec3.Zero) { Normal = Geometry.Vec3.UnitZ });
            Assert.Throws<System.ArgumentException>(() => r.FixedFace = GeometryRef.ToAxis(new Geometry.Line3(Geometry.Vec3.Zero, Geometry.Vec3.UnitX), "Kante"));
        }

        [Fact]
        public void Own_values_of_the_bends_features_are_compared_where_they_have_them()
        {
            var want = new BendValues { UseDefaultRadius = false, Radius = 0.002, UseDefaultAllowance = false, Allowance = new BendAllowanceData { KFactor = 0.4 } };
            var same = new BendValues { UseDefaultRadius = false, Radius = 0.002, UseDefaultAllowance = false, Allowance = new BendAllowanceData { KFactor = 0.4 } };
            Assert.True(SheetMetalRules.HasOwnValues(want));
            Assert.Empty(SheetMetalRules.LegacyBendsDifferences(want, same, De));
            var defaults = new BendValues { Radius = 0.001 };
            Assert.False(SheetMetalRules.HasOwnValues(defaults));
            Assert.False(SheetMetalRules.HasOwnValues(null));
            List<string> d = SheetMetalRules.LegacyBendsDifferences(want, defaults, De);
            Assert.Contains(d, x => x.StartsWith("Standard-Biegeradius"));
            Assert.Contains(d, x => x.StartsWith("Biegeradius"));
            Assert.Contains(d, x => x.StartsWith("Standard-Biegezugabe"));
            // A bends feature that uses the SheetMetal's values: its radius as read does not count.
            Assert.Empty(SheetMetalRules.LegacyBendsDifferences(defaults, new BendValues { Radius = 0.005 }, De));
        }

        /// <summary>A shelled box with legacy Insert Bends and a cut after them.</summary>
        private static List<TreeFeature> LegacyPart(bool parents) => new List<TreeFeature>
        {
            F("Boss-Extrude1", "Extrusion", null, F("Sketch1", "ProfileFeature")),
            F("Shell1", "Shell", new[] { "Boss-Extrude1" }),
            F("Sheet-Metal1", "SheetMetal", parents ? new[] { "Shell1" } : null),
            F("Flatten-Bends1", "FlattenBends", parents ? new[] { "Sheet-Metal1" } : null),
            F("Process-Bends1", "ProcessBends", parents ? new[] { "Flatten-Bends1" } : null),
            F("Cut-Extrude1", "Cut", new[] { "Process-Bends1" }, F("Sketch2", "ProfileFeature")),
        };

        [Fact]
        public void A_legacy_sheet_metal_is_accepted_before_capture_with_its_bends()
        {
            SheetMetalTree tree = SheetMetalTree.Of(LegacyPart(true));
            List<(string, string)> collected = LegacyPart(true).Select(f => (f.Name, f.Type)).ToList();
            Assert.Null(UnsupportedFeatures.RefusalBeforeCapture(collected, tree.Owners, false, tree.LegacyAnchors));
            // Not known as an anchor: an unowned SheetMetal, not decided yet.
            Assert.Equal("Blech-Feature „Sheet-Metal1“ (SheetMetal) wird noch nicht nachgebaut. Es wurde nichts gespiegelt.",
                UnsupportedFeatures.RefusalBeforeCapture(collected, tree.Owners, false));
            // Its bends without it: they stand alone, as carriers that are rebuilt they are not refused by type – the
            // position decides after capture.
            Assert.Null(UnsupportedFeatures.RefusalBeforeCapture(collected.Where(c => c.Item1 != "Sheet-Metal1").ToList(), tree.Owners, false, tree.LegacyAnchors));
            Assert.Equal(SheetMetalState.Rebuilt, SheetMetalTypes.LegacyAnchor.State);
            Assert.Equal(SheetMetalRole.Carrier, SheetMetalTypes.LegacyAnchor.Role);
            Assert.Equal(SheetMetalState.NotYet, SheetMetalTypes.Of("SheetMetal")!.State);
        }

        [Fact]
        public void A_legacy_body_is_refused_only_without_its_record()
        {
            SheetMetalTree tree = SheetMetalTree.Of(LegacyPart(true));
            var types = LegacyPart(true).ToDictionary(f => f.Name, f => f.Type);
            var body = new BodyHistory("Körper1", new[] { "Boss-Extrude1", "Shell1", "Sheet-Metal1", "Flatten-Bends1", "Process-Bends1", "Cut-Extrude1" }, false, isSheetMetal: true);
            var records = new List<FeatureRecord>
            {
                new ExtrudeRecord("Boss-Extrude1", "Extrusion", "Sketch1"),
                new ShellRecord("Shell1", "Shell"),
                new LegacyBendsRecord("Sheet-Metal1", "SheetMetal") { FlattenBendsName = "Flatten-Bends1", ProcessBendsName = "Process-Bends1" },
                new PartOfFeatureRecord("Flatten-Bends1", "FlattenBends", "Sheet-Metal1", PartOwnerKind.LegacyBends),
                new PartOfFeatureRecord("Process-Bends1", "ProcessBends", "Sheet-Metal1", PartOwnerKind.LegacyBends),
                new ExtrudeRecord("Cut-Extrude1", "Cut", "Sketch2") { IsCut = true },
            };
            Assert.Null(SheetMetalPlanner.Refusal(new[] { body }, records, types, tree));

            // The solid rebuilt without its Insert Bends: the copy would not be sheet metal.
            List<FeatureRecord> solidOnly = records.Take(2).ToList();
            Assert.Equal("„Sheet-Metal1“ trägt den Blechkörper „Körper1“ und wird nicht nachgebaut („Biegungen einfügen“ wird nicht mitgespiegelt). Es wurde nichts gespiegelt.",
                SheetMetalPlanner.Refusal(new[] { body }, solidOnly, types, tree));

            // Not readable: its bends are captured on their own and come after it – its own reason counts.
            records[2] = new UnsupportedRecord("Sheet-Metal1", "SheetMetal", "Blech-Feature nicht lesbar (x).", true);
            records[3] = new UnsupportedRecord("Flatten-Bends1", "FlattenBends", ((PartOfFeatureRecord)records[3]).LeftOut, false);
            records[4] = new UnsupportedRecord("Process-Bends1", "ProcessBends", ((PartOfFeatureRecord)records[4]).LeftOut, false);
            Assert.Equal("„Sheet-Metal1“ trägt den Blechkörper „Körper1“ und wird nicht nachgebaut (Blech-Feature nicht lesbar (x)). Es wurde nichts gespiegelt.",
                SheetMetalPlanner.Refusal(new[] { body }, records, types, tree));
            // Only a bends feature in the body's history that the run captured on its own: the owner's reason, too.
            var bendsOnly = new BodyHistory("Körper1", new[] { "Boss-Extrude1", "Shell1", "Flatten-Bends1" }, false, isSheetMetal: true);
            Assert.Equal("„Sheet-Metal1“ trägt den Blechkörper „Körper1“ und wird nicht nachgebaut (Blech-Feature nicht lesbar (x)). Es wurde nichts gespiegelt.",
                SheetMetalPlanner.Refusal(new[] { bendsOnly }, records, types, tree));
        }

        [Fact]
        public void Bends_switched_off_with_their_flatten_bends_leave_the_copy_the_sheet_metal_alone()
        {
            var parts = ((string?)"Flatten-Bends1", (string?)"Process-Bends1");
            // Both active (the session bent a flattened one back): the copy makes all three.
            Assert.Equal(parts, SheetMetalRules.LegacyPartsInRun(parts, _ => false));
            // FlattenBends suppressed, ProcessBends with it – still so after folding: switched off, not flattened.
            var off = new HashSet<string> { "Flatten-Bends1", "Process-Bends1" };
            Assert.Equal(((string?)null, (string?)null), SheetMetalRules.LegacyPartsInRun(parts, off.Contains));
            Assert.Equal(((string?)null, (string?)null), SheetMetalRules.LegacyPartsInRun(parts, n => n == "Flatten-Bends1"));
            Assert.Equal(((string?)null, (string?)null), SheetMetalRules.LegacyPartsInRun(parts, n => n == "Process-Bends1"));
            Assert.Equal(((string?)null, (string?)null), SheetMetalRules.LegacyPartsInRun((null, null), _ => true));

            // Then InsertBends2 without flattening: measured after the SheetMetal, described without the bends.
            var (flatten, process) = SheetMetalRules.LegacyPartsInRun(parts, off.Contains);
            var r = new LegacyBendsRecord("Sheet-Metal1", "SheetMetal") { FlattenBendsName = flatten, ProcessBendsName = process };
            r.Body.BendRadius = 0.001;
            r.Body.BendAllowance = new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = 0.44 };
            Assert.Equal("Sheet-Metal1", r.MeasureUntil);
            Assert.Equal("Biegungen eingefügt, Radius 1 mm, K 0,44", SheetMetalRules.DescribeLegacy(r, De));
        }

        [Fact]
        public void Bending_back_with_dependents_brings_back_what_left_suppression()
        {
            var folds = new HashSet<string> { "Flat-Pattern1", "Process-Bends1" };
            List<string> back = SheetMetalRules.BroughtBack(
                new[] { "Sketch5", "Cut-Extrude1", "Flat-Pattern1", "Process-Bends1", "Fillet3", "Sketch9", "Cut-Extrude2" },
                new[] { "Fillet3" },
                folds);
            // In creation order; what stays suppressed (suppressed by the user on its own) and what the session folds itself left out.
            Assert.Equal(new[] { "Sketch5", "Cut-Extrude1", "Sketch9", "Cut-Extrude2" }, back);
            Assert.Empty(SheetMetalRules.BroughtBack(new[] { "Process-Bends1" }, new string[0], folds));
            Assert.Empty(SheetMetalRules.BroughtBack(new[] { "Cut-Extrude1" }, new[] { "Cut-Extrude1" }, folds));
        }

        [Fact]
        public void Texts()
        {
            var r = new LegacyBendsRecord("Sheet-Metal1", "SheetMetal") { FlattenBendsName = "F", ProcessBendsName = "P" };
            r.Body.BendRadius = 0.001;
            r.Body.BendAllowance = new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = 0.44 };
            Assert.Equal("Biegungen eingefügt (mit Abwickeln und Zurückbiegen), Radius 1 mm, K 0,44", SheetMetalRules.DescribeLegacy(r, De));
            Assert.Equal("Features zwischen „Biegungen einfügen“ und ihrem Zurückbiegen („Cut-Extrude1“) wirken auf den abgewickelten Zustand; " +
                         "die Kopie legt die Biegungen in einem Schritt an und kann nichts dazwischen setzen.", SheetMetalRules.LegacyBetween(new[] { "Cut-Extrude1" }));
            Assert.Equal("SOLIDWORKS hat „Biegungen einfügen“ nicht angelegt (Markierung 0: x; Markierung 1: y).", SheetMetalRules.LegacyNotCreated(new[] { "Markierung 0: x", "Markierung 1: y" }));
            Assert.StartsWith("Biegezugabe (Biegeabzug 2 mm) nimmt „Biegungen einfügen“ beim Anlegen nicht",
                SheetMetalRules.LegacyAllowanceLater(new BendAllowanceData { Kind = BendAllowanceKind.Deduction, BendDeduction = 0.002 }, De));
            // A later body: the target's defaults needed the placeholder, the body's own allowance went on afterwards – both named.
            Assert.Equal("Die Biegezugabe der Blech-Standardwerte im Ziel (Biegeabzug 2 mm) nimmt „Biegungen einfügen“ beim Anlegen nicht; " +
                         "angelegt mit einem Platzhalter, danach wurde am Blech-Feature die eigene (K 0,44) gesetzt.",
                SheetMetalRules.LegacyTargetAllowanceLater(new BendAllowanceData { Kind = BendAllowanceKind.Deduction, BendDeduction = 0.002 },
                    new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = 0.44 }, De));
        }
    }
}
