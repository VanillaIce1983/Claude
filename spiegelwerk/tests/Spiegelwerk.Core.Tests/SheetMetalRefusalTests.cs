using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>
    /// Refusal (blech-plan §1.5): before capture by type, after capture by position in the body.
    /// S0 was inert; S1a opens base flange, flat pattern and the template folders, decides the user
    /// corner trim as not rebuilt and refuses every other sheet-metal type as before; S1b opens the
    /// same types in the same part; S2 opens hem and miter flange, S3 the edge flange.
    /// </summary>
    public class SheetMetalRefusalTests
    {
        /// <summary>The set the run refused before the type table.</summary>
        private static readonly string[] OldSet =
        {
            "SheetMetal", "SMBaseFlange", "EdgeFlange", "SMMiteredFlange", "Hem", "Jog", "SketchBend", "SM3dBend", "OneBend",
            "ToroidalBend", "LoftedBend", "FlatPattern", "FlattenBends", "ProcessBends", "Fold", "UnFold", "BreakCorner",
            "CornerTrim", "CrossBreak", "SMGusset", "NormalCut", "Rip", "SolidToSheetMetal", "FormToolInstance",
            "TemplateSheetMetal", "TemplateFlatPattern",
        };

        /// <summary>What S1a, S2 and S3 decide.</summary>
        private static readonly string[] OpenedSoFar = { "SMBaseFlange", "FlatPattern", "CornerTrim", "TemplateSheetMetal", "TemplateFlatPattern", "Hem", "SMMiteredFlange", "EdgeFlange" };

        [Fact]
        public void Every_type_not_decided_by_S3_is_still_refused()
        {
            foreach (string type in OldSet)
            {
                Assert.True(UnsupportedFeatures.IsSheetMetal(type), type);
                bool open = OpenedSoFar.Contains(type);
                Assert.Equal(!open, UnsupportedFeatures.RefusesRun(type));
                // Not decided yet: the fallback switch does not help either.
                Assert.Equal(!open, UnsupportedFeatures.RefusesBeforeCapture(type, partOfAcceptedOwner: false, fallbackOn: true));
            }
        }

        [Fact]
        public void The_table_has_the_S3_states_and_lists_the_closed_corner_too()
        {
            foreach (string type in SheetMetalTypes.Names)
            {
                SheetMetalState expected = type switch
                {
                    "SMBaseFlange" or "FlatPattern" or "TemplateSheetMetal" or "TemplateFlatPattern" or "Hem" or "SMMiteredFlange" or "EdgeFlange" => SheetMetalState.Rebuilt,
                    "CornerTrim" => SheetMetalState.Unsupported,
                    _ => SheetMetalState.NotYet,
                };
                Assert.Equal(expected, SheetMetalTypes.Of(type)!.State);
            }
            Assert.Equal(OldSet.OrderBy(t => t).Concat(new[] { "CornerFeat" }).OrderBy(t => t), SheetMetalTypes.Names.OrderBy(t => t));
            Assert.Equal(SheetMetalRole.Leaf, SheetMetalTypes.Of("CornerFeat")!.Role);
        }

        [Fact]
        public void Unowned_SheetMetal_is_a_carrier()
        {
            Assert.Equal(SheetMetalRole.Carrier, SheetMetalTypes.Of("SheetMetal")!.Role);
            // Without an accepted owner (convert, lofted bend, swept flange …) it is refused, with the fallback off and – not yet decided – on.
            Assert.True(UnsupportedFeatures.RefusesBeforeCapture("SheetMetal", partOfAcceptedOwner: false, fallbackOn: false));
            var owners = new Dictionary<string, (string, PartOwnerKind)>();
            string? text = UnsupportedFeatures.RefusalBeforeCapture(new[] { ("Sheet-Metal1", "SheetMetal") }, owners, fallbackOn: false);
            Assert.Equal("Blech-Feature „Sheet-Metal1“ (SheetMetal) wird noch nicht nachgebaut. Es wurde nichts gespiegelt.", text);
        }

        [Fact]
        public void A_SheetMetal_of_a_collected_base_flange_is_accepted()
        {
            var owners = new Dictionary<string, (string, PartOwnerKind)> { ["Sheet-Metal1"] = ("Base-Flange1", PartOwnerKind.SheetMetal) };
            var collected = new List<(string, string)> { ("Sheet-Metal1", "SheetMetal"), ("Sketch1", "ProfileFeature"), ("Base-Flange1", "SMBaseFlange"), ("Flat-Pattern1", "FlatPattern") };
            Assert.Null(UnsupportedFeatures.RefusalBeforeCapture(collected, owners, fallbackOn: false));
            // Its base flange not collected: the SheetMetal stands alone and is refused.
            Assert.NotNull(UnsupportedFeatures.RefusalBeforeCapture(collected.Where(c => c.Item1 != "Base-Flange1").ToList(), owners, false));
            // An edge flange later in the part goes on since S3 (its profile a part of it); a jog still refuses the run.
            collected.Add(("Sketch2", "ProfileFeature"));
            collected.Add(("Edge-Flange1", "EdgeFlange"));
            owners["Sketch2"] = ("Edge-Flange1", PartOwnerKind.EdgeFlange);
            Assert.Null(UnsupportedFeatures.RefusalBeforeCapture(collected, owners, false));
            collected.Add(("Jog1", "Jog"));
            Assert.Equal("Blech-Feature „Jog1“ (Jog) wird noch nicht nachgebaut. Es wurde nichts gespiegelt.",
                UnsupportedFeatures.RefusalBeforeCapture(collected, owners, false));
        }

        [Fact]
        public void Same_part_follows_the_new_part_rules_since_S1b()
        {
            // S1b lifts the same-part refusal: base flange, SheetMetal and flat pattern go on, everything not decided still refuses.
            var owners = new Dictionary<string, (string, PartOwnerKind)> { ["Sheet-Metal1"] = ("Base-Flange1", PartOwnerKind.SheetMetal) };
            var collected = new List<(string, string)> { ("Sheet-Metal1", "SheetMetal"), ("Sketch1", "ProfileFeature"), ("Base-Flange1", "SMBaseFlange"), ("Flat-Pattern1", "FlatPattern") };
            Assert.Null(UnsupportedFeatures.RefusalBeforeCapture(collected, owners, fallbackOn: false));
            // S2: a hem and a miter flange go on as well, with the hem's width sketch as its part.
            collected.Add(("Sketch3", "ProfileFeature"));
            collected.Add(("Hem1", "Hem"));
            collected.Add(("Miter-Flange1", "SMMiteredFlange"));
            owners["Sketch3"] = ("Hem1", PartOwnerKind.Hem);
            Assert.Null(UnsupportedFeatures.RefusalBeforeCapture(collected, owners, fallbackOn: false));
            collected.Add(("Jog1", "Jog"));
            Assert.Equal("Blech-Feature „Jog1“ (Jog) wird noch nicht nachgebaut. Es wurde nichts gespiegelt.",
                UnsupportedFeatures.RefusalBeforeCapture(collected, owners, fallbackOn: false));
            // S1b opens the same types as S1a, now in the same part too.
            Assert.Equal(SheetMetalState.Rebuilt, SheetMetalTypes.Of("SMBaseFlange")!.State);
            Assert.Equal(SheetMetalState.NotYet, SheetMetalTypes.Of("SheetMetal")!.State);
        }

        [Fact]
        public void Unsupported_first_feature_of_a_sheet_metal_body_refuses_without_a_plan()
        {
            var tree = SheetMetalTree.Of(new[]
            {
                new TreeFeature("Sheet-Metal1", "SheetMetal"),
                new TreeFeature("Swept-Flange1", "SweptFlange", new[] { "Sheet-Metal1" }),
                new TreeFeature("Cut-Extrude1", "Cut"),
            });
            var types = new Dictionary<string, string> { ["Sheet-Metal1"] = "SheetMetal", ["Swept-Flange1"] = "SweptFlange", ["Cut-Extrude1"] = "Cut" };
            var body = new BodyHistory("Körper1", new[] { "Sheet-Metal1", "Swept-Flange1", "Cut-Extrude1" }, false, isSheetMetal: true);
            var records = new List<FeatureRecord>
            {
                new UnsupportedRecord("Swept-Flange1", "SweptFlange", "Feature-Typ SweptFlange wird nicht unterstützt.", true),
                new ExtrudeRecord("Cut-Extrude1", "Cut", "Sketch2") { IsCut = true },
            };
            Assert.Equal("„Swept-Flange1“ trägt den Blechkörper „Körper1“ und wird nicht nachgebaut (Feature-Typ SweptFlange wird nicht unterstützt). Es wurde nichts gespiegelt.",
                SheetMetalPlanner.Refusal(new[] { body }, records, types, tree));
            // Only the cut selected: the run does not rebuild the body – ordinary rules.
            Assert.Null(SheetMetalPlanner.Refusal(new[] { body }, records.Skip(1).ToList(), types, tree));
            // Not a sheet-metal body: not this rule's business.
            Assert.Null(SheetMetalPlanner.Refusal(new[] { new BodyHistory("Körper1", body.Features, false) }, records, types, tree));
        }

        [Fact]
        public void Body_without_a_rebuilt_base_flange_is_refused()
        {
            var tree = SheetMetalTree.Of(new[]
            {
                new TreeFeature("Sheet-Metal1", "SheetMetal"),
                new TreeFeature("Boss-Extrude1", "Extrusion"),
            });
            var types = new Dictionary<string, string> { ["Sheet-Metal1"] = "SheetMetal", ["Boss-Extrude1"] = "Extrusion" };
            var body = new BodyHistory("Körper1", new[] { "Sheet-Metal1", "Boss-Extrude1" }, false, isSheetMetal: true);
            var records = new List<FeatureRecord> { new ExtrudeRecord("Boss-Extrude1", "Extrusion", "Sketch1") };
            Assert.Equal("„Sheet-Metal1“ trägt den Blechkörper „Körper1“ und wird nicht nachgebaut (der Blechkörper hat keine eigene Basislasche). Es wurde nichts gespiegelt.",
                SheetMetalPlanner.Refusal(new[] { body }, records, types, tree));
        }

        [Fact]
        public void Base_flange_body_with_a_flat_pattern_is_not_refused()
        {
            var tree = SheetMetalTree.Of(new[]
            {
                new TreeFeature("Sheet-Metal1", "SheetMetal"),
                new TreeFeature("Base-Flange1", "SMBaseFlange", new[] { "Sheet-Metal1" }),
                new TreeFeature("Flat-Pattern1", "FlatPattern", new[] { "Base-Flange1" }),
            });
            var types = new Dictionary<string, string> { ["Sheet-Metal1"] = "SheetMetal", ["Base-Flange1"] = "SMBaseFlange", ["Flat-Pattern1"] = "FlatPattern" };
            var body = new BodyHistory("Körper1", new[] { "Sheet-Metal1", "Base-Flange1", "Flat-Pattern1" }, false, isSheetMetal: true);
            var records = new List<FeatureRecord>
            {
                new PartOfFeatureRecord("Sheet-Metal1", "SheetMetal", "Base-Flange1", PartOwnerKind.SheetMetal),
                new BaseFlangeRecord("Base-Flange1", "SMBaseFlange", "Sketch1"),
                new FlatPatternRecord("Flat-Pattern1", "FlatPattern", "Base-Flange1"),
            };
            Assert.Null(SheetMetalPlanner.Refusal(new[] { body }, records, types, tree));
            // A base flange that could not be read: CaptureAll turns its SheetMetal into an unsupported
            // record as well, which comes first in the body – the flange's own reason is what counts.
            records[0] = new UnsupportedRecord("Sheet-Metal1", "SheetMetal", ((PartOfFeatureRecord)records[0]).LeftOut, false);
            records[1] = new UnsupportedRecord("Base-Flange1", "SMBaseFlange", "Blech-Feature nicht lesbar (x).", true);
            Assert.Equal("„Base-Flange1“ trägt den Blechkörper „Körper1“ und wird nicht nachgebaut (Blech-Feature nicht lesbar (x)). Es wurde nichts gespiegelt.",
                SheetMetalPlanner.Refusal(new[] { body }, records, types, tree));
        }

        [Fact]
        public void Leaf_of_unknown_type_does_not_refuse()
        {
            var tree = SheetMetalTree.Of(new[]
            {
                new TreeFeature("Sheet-Metal1", "SheetMetal"),
                new TreeFeature("Base-Flange1", "SMBaseFlange", new[] { "Sheet-Metal1" }),
                new TreeFeature("Vent1", "Vent"),
            });
            var types = new Dictionary<string, string> { ["Sheet-Metal1"] = "SheetMetal", ["Base-Flange1"] = "SMBaseFlange", ["Vent1"] = "Vent" };
            var body = new BodyHistory("Körper1", new[] { "Sheet-Metal1", "Base-Flange1", "Vent1" }, false, isSheetMetal: true);
            var records = new List<FeatureRecord>
            {
                new PartOfFeatureRecord("Sheet-Metal1", "SheetMetal", "Base-Flange1", PartOwnerKind.SheetMetal),
                new BaseFlangeRecord("Base-Flange1", "SMBaseFlange", "Sketch1"),
                new UnsupportedRecord("Vent1", "Vent", "Feature-Typ Vent wird nicht unterstützt.", true),
            };
            Assert.Null(SheetMetalPlanner.Refusal(new[] { body }, records, types, tree));
            Assert.False(UnsupportedFeatures.RefusesRun("Vent"));
        }

        [Fact]
        public void Roles_follow_the_plan()
        {
            Assert.Equal(SheetMetalRole.System, SheetMetalTypes.Of("TemplateSheetMetal")!.Role);
            Assert.Equal(SheetMetalRole.Part, SheetMetalTypes.Of("OneBend")!.Role);
            foreach (string carrier in new[] { "SMBaseFlange", "SheetMetal", "FlattenBends", "ProcessBends", "SolidToSheetMetal", "LoftedBend", "ToroidalBend" })
                Assert.Equal(SheetMetalRole.Carrier, SheetMetalTypes.Of(carrier)!.Role);
            foreach (string leaf in new[] { "EdgeFlange", "Hem", "Jog", "FlatPattern", "CrossBreak", "FormToolInstance", "SMGusset" })
                Assert.Equal(SheetMetalRole.Leaf, SheetMetalTypes.Of(leaf)!.Role);
        }

        [Fact]
        public void MirrorStock_is_always_refused()
        {
            Assert.True(UnsupportedFeatures.RefusesBeforeCapture("MirrorStock", partOfAcceptedOwner: false, fallbackOn: false));
            Assert.True(UnsupportedFeatures.RefusesBeforeCapture("MirrorStock", partOfAcceptedOwner: true, fallbackOn: true));
            Assert.False(UnsupportedFeatures.IsSheetMetal("MirrorStock"));
        }

        [Fact]
        public void Part_of_an_accepted_owner_is_never_refused_on_its_own()
        {
            Assert.False(UnsupportedFeatures.RefusesBeforeCapture("SheetMetal", partOfAcceptedOwner: true, fallbackOn: false));
            Assert.False(UnsupportedFeatures.RefusesBeforeCapture("CornerTrim", partOfAcceptedOwner: true, fallbackOn: false));
        }

        [Fact]
        public void Unconfirmed_names_and_other_types_are_not_refused_by_name()
        {
            foreach (string type in new[] { "SweptFlange", "CornerRelief", "TabAndSlot", "Vent", "MirrorPattern", "MirrorSolid", "Extrusion", "Bending" })
            {
                Assert.False(UnsupportedFeatures.IsSheetMetal(type), type);
                Assert.False(UnsupportedFeatures.RefusesRun(type), type);
            }
        }

        [Fact]
        public void States_open_the_way_the_slices_need()
        {
            static bool Refuses(SheetMetalRole role, SheetMetalState state, bool fallbackOn) =>
                SheetMetalTypes.Refuses(new SheetMetalType(role, state), partOfAcceptedOwner: false, fallbackOn);

            foreach (SheetMetalRole role in new[] { SheetMetalRole.Carrier, SheetMetalRole.Leaf })
            {
                Assert.True(Refuses(role, SheetMetalState.NotYet, fallbackOn: true));
                Assert.False(Refuses(role, SheetMetalState.Rebuilt, fallbackOn: false));
            }
            // A decided leaf that is not rebuilt is an ordinary failed entry, not a refusal.
            Assert.False(Refuses(SheetMetalRole.Leaf, SheetMetalState.Unsupported, fallbackOn: false));
            // A carrier that is not rebuilt refuses unless the fallback may cover its body.
            Assert.True(Refuses(SheetMetalRole.Carrier, SheetMetalState.Unsupported, fallbackOn: false));
            Assert.False(Refuses(SheetMetalRole.Carrier, SheetMetalState.Unsupported, fallbackOn: true));
            Assert.False(SheetMetalTypes.Refuses((SheetMetalType?)null, partOfAcceptedOwner: false, fallbackOn: false));
        }

        [Fact]
        public void Cross_break_and_reference_geometry_do_not_change_the_solid()
        {
            Assert.False(UnsupportedFeatures.ChangesSolid("CrossBreak"));
            Assert.False(UnsupportedFeatures.ChangesSolid("ProfileFeature"));
            Assert.False(UnsupportedFeatures.ChangesSolid("RefPlane"));
            Assert.True(UnsupportedFeatures.ChangesSolid("EdgeFlange"));
            Assert.True(UnsupportedFeatures.ChangesSolid("MirrorPattern"));
        }

        [Fact]
        public void Mirror_feature_over_sheet_metal_has_its_own_reason()
        {
            Assert.Equal("Spiegel-Feature über Blech-Features wird noch nicht nachgebaut.", UnsupportedFeatures.MirrorOverSheetMetal);
        }

        [Fact]
        public void Unsupported_records_trigger_the_fallback_as_their_type_says()
        {
            // Every listed type triggers but the user corner trim (non-solid, A3 P3); a type the table does not know triggers too (K17).
            Assert.All(SheetMetalTypes.Names, type => Assert.Equal(type != "CornerTrim", SheetMetalTypes.TriggersFallback(type)));
            Assert.True(SheetMetalTypes.TriggersFallback("WeldedCornerUnknown"));
            Assert.False(new SheetMetalType(SheetMetalRole.Leaf, SheetMetalState.Unsupported, triggersFallback: false).TriggersFallback);
        }
    }
}
