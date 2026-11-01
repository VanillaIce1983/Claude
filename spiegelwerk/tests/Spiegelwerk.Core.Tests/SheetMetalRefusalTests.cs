using System.Linq;
using Spiegelwerk.Core.Planning;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>
    /// Refusal before capture (blech-plan §1.5). S0 is inert: every sheet-metal type is still
    /// refused, exactly as before the type table existed; the slices open their types one by one.
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

        [Fact]
        public void Every_type_of_the_old_set_is_still_refused()
        {
            foreach (string type in OldSet)
            {
                Assert.True(UnsupportedFeatures.IsSheetMetal(type), type);
                Assert.True(UnsupportedFeatures.RefusesRun(type), type);
                // Not decided yet: the fallback switch does not help either.
                Assert.True(UnsupportedFeatures.RefusesBeforeCapture(type, partOfAcceptedOwner: false, fallbackOn: true), type);
            }
        }

        [Fact]
        public void The_table_is_inert_and_lists_the_closed_corner_too()
        {
            Assert.All(SheetMetalTypes.Names, type => Assert.Equal(SheetMetalState.NotYet, SheetMetalTypes.Of(type)!.State));
            Assert.Equal(OldSet.OrderBy(t => t).Concat(new[] { "CornerFeat" }).OrderBy(t => t), SheetMetalTypes.Names.OrderBy(t => t));
            Assert.Equal(SheetMetalRole.Leaf, SheetMetalTypes.Of("CornerFeat")!.Role);
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
            // S0: every listed type triggers; a type the table does not know triggers too (K17).
            Assert.All(SheetMetalTypes.Names, type => Assert.True(SheetMetalTypes.TriggersFallback(type), type));
            Assert.True(SheetMetalTypes.TriggersFallback("WeldedCornerUnknown"));
            Assert.False(new SheetMetalType(SheetMetalRole.Leaf, SheetMetalState.Unsupported, triggersFallback: false).TriggersFallback);
        }
    }
}
