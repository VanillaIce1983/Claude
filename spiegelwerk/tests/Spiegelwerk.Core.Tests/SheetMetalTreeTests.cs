using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>Who comes into being with whom in a sheet-metal part (blech-plan S1a, A1 §2, A3 P3).</summary>
    public class SheetMetalTreeTests
    {
        private static TreeFeature F(string name, string type, string[]? parents = null, params TreeFeature[] subs) =>
            new TreeFeature(name, type, parents, subs);

        /// <summary>
        /// A single-body part: Sheet-Metal1 at the top, Base-Flange1 with its sketch, a cut, the flat
        /// pattern last with its sketches, its own corner trim and one the user inserted.
        /// </summary>
        private static SheetMetalTree SingleBody(out List<TreeFeature> top)
        {
            top = new List<TreeFeature>
            {
                F("Origin", "OriginProfileFeature"),
                F("Sheet-Metal1", "SheetMetal"),
                F("Base-Flange1", "SMBaseFlange", new[] { "Sheet-Metal1", "Sketch1" }, F("Sketch1", "ProfileFeature")),
                F("Cut-Extrude1", "Cut", new[] { "Base-Flange1" }, F("Sketch2", "ProfileFeature")),
                F("Flat-Pattern1", "FlatPattern", new[] { "Base-Flange1" },
                    F("Bend-Lines1", "ProfileFeature"), F("Bounding-Box1", "ProfileFeature"),
                    F("Corner-Trim1", "CornerTrim"), F("Corner-Trim2", "CornerTrim")),
                F("Cut-Extrude2", "Cut", new[] { "Flat-Pattern1" }),
            };
            var facts = new Dictionary<string, FlatPatternFacts>
            {
                ["Flat-Pattern1"] = new FlatPatternFacts("Corner-Trim1", null, new[] { "Flat-Pattern1", "Cut-Extrude1", "Base-Flange1", "Sheet-Metal1" }),
            };
            return SheetMetalTree.Of(top, facts);
        }

        [Fact]
        public void SheetMetal_belongs_to_its_base_flange()
        {
            SheetMetalTree tree = SingleBody(out _);
            Assert.Equal(("Base-Flange1", PartOwnerKind.SheetMetal), tree.Owners["Sheet-Metal1"]);
            Assert.False(tree.IsUnownedSheetMetal("Sheet-Metal1"));
        }

        [Fact]
        public void A_tab_does_not_take_the_sheet_metal_from_the_first_base_flange()
        {
            var top = new[]
            {
                F("Sheet-Metal1", "SheetMetal"),
                F("Base-Flange1", "SMBaseFlange", new[] { "Sheet-Metal1" }),
                F("Base-Flange2", "SMBaseFlange", new[] { "Sheet-Metal1" }),
            };
            Assert.Equal("Base-Flange1", SheetMetalTree.Of(top).Owners["Sheet-Metal1"].owner);
        }

        [Fact]
        public void Unowned_sheet_metal_is_recognised()
        {
            SheetMetalTree tree = SheetMetalTree.Of(new[] { F("Sheet-Metal1", "SheetMetal"), F("Lofted-Bend1", "LoftedBend", new[] { "Sheet-Metal1" }) });
            Assert.True(tree.IsUnownedSheetMetal("Sheet-Metal1"));
            Assert.False(tree.Owners.ContainsKey("Sheet-Metal1"));
        }

        [Fact]
        public void Flat_pattern_subsketches_belong_to_the_flat_pattern()
        {
            SheetMetalTree tree = SingleBody(out _);
            Assert.Equal(("Flat-Pattern1", PartOwnerKind.FlatPattern), tree.Owners["Bend-Lines1"]);
            Assert.Equal(("Flat-Pattern1", PartOwnerKind.FlatPattern), tree.Owners["Bounding-Box1"]);
            Assert.Equal("Base-Flange1", tree.FlatPatternOwners["Flat-Pattern1"]);
        }

        [Fact]
        public void Flat_pattern_owner_from_its_body_is_not_marked_as_from_the_tree()
        {
            SheetMetalTree tree = SingleBody(out _);
            Assert.False(tree.OwnerFromTree("Flat-Pattern1"));
        }

        [Fact]
        public void Flat_pattern_without_body_features_in_a_single_body_part_belongs_to_the_only_base_flange()
        {
            // B9: folded, the flat pattern may be missing from GetFeatures of its body; no parents known either.
            var top = new[]
            {
                F("Sheet-Metal1", "SheetMetal"),
                F("Base-Flange1", "SMBaseFlange", new[] { "Sheet-Metal1", "Sketch1" }, F("Sketch1", "ProfileFeature")),
                F("Base-Flange2", "SMBaseFlange", new[] { "Sheet-Metal1", "Sketch2" }, F("Sketch2", "ProfileFeature")),
                F("Flat-Pattern1", "FlatPattern", null, F("Bend-Lines1", "ProfileFeature")),
            };
            SheetMetalTree tree = SheetMetalTree.Of(top, new Dictionary<string, FlatPatternFacts> { ["Flat-Pattern1"] = new FlatPatternFacts() });
            Assert.Equal("Base-Flange1", tree.FlatPatternOwners["Flat-Pattern1"]);
            Assert.True(tree.OwnerFromTree("Flat-Pattern1"));
            Assert.Contains("Flat-Pattern1", tree.Closure(new[] { "Base-Flange1" }));
            Assert.StartsWith("Basislasche der Abwicklung aus dem Feature-Baum bestimmt", SheetMetalTree.OwnerFromTreeNote);
        }

        [Fact]
        public void Flat_pattern_without_body_features_follows_its_parents()
        {
            // Two bodies: the parents lead to the body – directly, over its SheetMetal, or over a tab to the SheetMetal it shares.
            var smFolder = F("Sheet-Metal", "TemplateSheetMetal", null, F("Sheet-Metal1", "SheetMetal"), F("Sheet-Metal2", "SheetMetal"));
            var fpFolder = F("Flat-Pattern", "TemplateFlatPattern", null,
                F("Flat-Pattern1", "FlatPattern", new[] { "Base-Flange3" }),
                F("Flat-Pattern2", "FlatPattern", new[] { "Sheet-Metal2" }));
            var top = new[]
            {
                smFolder,
                F("Base-Flange1", "SMBaseFlange", new[] { "Sheet-Metal1" }),
                F("Base-Flange2", "SMBaseFlange", new[] { "Sheet-Metal2" }),
                F("Base-Flange3", "SMBaseFlange", new[] { "Sheet-Metal1" }),
                fpFolder,
            };
            SheetMetalTree tree = SheetMetalTree.Of(top);
            Assert.Equal("Base-Flange1", tree.FlatPatternOwners["Flat-Pattern1"]);
            Assert.Equal("Base-Flange2", tree.FlatPatternOwners["Flat-Pattern2"]);
            Assert.True(tree.OwnerFromTree("Flat-Pattern2"));
            Assert.Contains("Flat-Pattern2", tree.Closure(new[] { "Base-Flange2" }));
            Assert.DoesNotContain("Flat-Pattern2", tree.Closure(new[] { "Base-Flange1" }));
        }

        [Fact]
        public void Flat_patterns_without_body_features_or_parents_get_no_guessed_owner()
        {
            var smFolder = F("Sheet-Metal", "TemplateSheetMetal", null, F("Sheet-Metal1", "SheetMetal"), F("Sheet-Metal2", "SheetMetal"));
            var fpFolder = F("Flat-Pattern", "TemplateFlatPattern", null, F("Flat-Pattern1", "FlatPattern"), F("Flat-Pattern2", "FlatPattern"));
            var top = new[]
            {
                smFolder,
                F("Base-Flange1", "SMBaseFlange", new[] { "Sheet-Metal1" }),
                F("Base-Flange2", "SMBaseFlange", new[] { "Sheet-Metal2" }),
                fpFolder,
            };
            SheetMetalTree tree = SheetMetalTree.Of(top);
            Assert.Empty(tree.FlatPatternOwners);
            Assert.False(tree.OwnerFromTree("Flat-Pattern1"));
            Assert.DoesNotContain("Flat-Pattern1", tree.Closure(new[] { "Base-Flange1" }));
        }

        [Fact]
        public void User_corner_trim_is_unsupported_and_flat_patterns_own_corner_trim_is_part_of_it()
        {
            SheetMetalTree tree = SingleBody(out _);
            Assert.Equal(("Flat-Pattern1", PartOwnerKind.FlatPattern), tree.Owners["Corner-Trim1"]);
            Assert.False(tree.IsUserCornerTrim("Corner-Trim1"));
            Assert.True(tree.IsUserCornerTrim("Corner-Trim2"));
            Assert.False(tree.Owners.ContainsKey("Corner-Trim2"));
            // As a record: unsupported, changes no solid, hands nothing to the fallback, has its own text.
            SheetMetalType trim = SheetMetalTypes.Of("CornerTrim")!;
            Assert.Equal(SheetMetalState.Unsupported, trim.State);
            Assert.False(trim.TriggersFallback);
            Assert.False(UnsupportedFeatures.ChangesSolid("CornerTrim"));
            Assert.StartsWith("Ecke trimmen (abgewickelt):", UnsupportedFeatures.Reason("CornerTrim"));
            Assert.False(UnsupportedFeatures.RefusesRun("CornerTrim"));
        }

        [Fact]
        public void Base_flange_brings_its_sheet_metal_and_flat_pattern()
        {
            SheetMetalTree tree = SingleBody(out _);
            IReadOnlyCollection<string> added = tree.Closure(new[] { "Base-Flange1" });
            Assert.Contains("Sheet-Metal1", added);
            Assert.Contains("Flat-Pattern1", added);
            // The flat pattern brings its own parts, not the corner trim the user inserted.
            Assert.Contains("Corner-Trim1", added);
            Assert.Contains("Bend-Lines1", added);
            Assert.DoesNotContain("Corner-Trim2", added);
            Assert.DoesNotContain("Cut-Extrude1", added);
            Assert.DoesNotContain("Base-Flange1", added);
        }

        [Fact]
        public void Flat_pattern_without_its_owner_is_reported()
        {
            SheetMetalTree tree = SingleBody(out _);
            // A selected flat pattern brings only its own parts, not its base flange.
            Assert.DoesNotContain("Base-Flange1", tree.Closure(new[] { "Flat-Pattern1" }));
            Assert.Equal("Abwicklung entsteht nur mit ihrer Basislasche „Base-Flange1“, die nicht gespiegelt wird.",
                SheetMetalTree.FlatPatternWithoutOwner("Base-Flange1"));
            Assert.Contains("ließ sich nicht bestimmen", SheetMetalTree.FlatPatternWithoutOwner(""));
        }

        [Fact]
        public void Template_folders_are_system_features()
        {
            Assert.True(SheetMetalTree.IsSystemType("TemplateSheetMetal"));
            Assert.True(SheetMetalTree.IsSystemType("TemplateFlatPattern"));
            Assert.False(SheetMetalTree.IsSystemType("FlatPattern"));
            Assert.False(SheetMetalTree.IsSystemType("Extrusion"));
            // Never collected: they change no solid and are not refused (S1a decides them).
            Assert.False(UnsupportedFeatures.ChangesSolid("TemplateSheetMetal"));
            Assert.False(UnsupportedFeatures.RefusesRun("TemplateFlatPattern"));
        }

        [Fact]
        public void Flat_pattern_inside_the_folder_is_listed_before_the_folder()
        {
            var folder = F("Flat-Pattern", "TemplateFlatPattern", null,
                F("Flat-Pattern1", "FlatPattern", null, F("Bend-Lines1", "ProfileFeature"), F("Corner-Trim1", "CornerTrim")),
                F("Flat-Pattern2", "FlatPattern"));
            var smFolder = F("Sheet-Metal", "TemplateSheetMetal", null, F("Sheet-Metal1", "SheetMetal"), F("Sheet-Metal2", "SheetMetal"));
            var top = new List<TreeFeature>
            {
                smFolder,
                F("Base-Flange1", "SMBaseFlange", null, F("Sketch1", "ProfileFeature")),
                F("Base-Flange2", "SMBaseFlange", null, F("Sketch2", "ProfileFeature")),
                folder,
            };
            List<TreeFeature> order = CreationOrder.Sort(top, f => f.SubFeatures.Where(s => SheetMetalTree.ListsSubFeature(f.Type, s.Type)),
                f => Enumerable.Empty<TreeFeature>(), f => f.Name);
            List<string> names = order.Select(f => f.Name).ToList();
            Assert.Equal(new[] { "Sheet-Metal", "Sketch1", "Base-Flange1", "Sketch2", "Base-Flange2", "Bend-Lines1", "Corner-Trim1", "Flat-Pattern1", "Flat-Pattern2", "Flat-Pattern" }, names);
            // The SheetMetal features inside their folder are not listed.
            Assert.DoesNotContain("Sheet-Metal1", names);
        }

        [Fact]
        public void Feature_after_a_flat_pattern_gets_its_own_skip_reason()
        {
            SheetMetalTree tree = SingleBody(out _);
            Assert.True(tree.IsAfterFlatPattern("Cut-Extrude2"));
            Assert.False(tree.IsAfterFlatPattern("Cut-Extrude1"));
            Assert.False(tree.IsAfterFlatPattern("Flat-Pattern1"));
            Assert.Equal("Nur in der Abwicklung vorhanden – nicht gespiegelt.", SheetMetalTree.OnlyFlattened);
            // The flat pattern and what lives under it are suppressed whenever the part is folded: no skip for that.
            Assert.True(tree.SuppressedWhenFolded("Flat-Pattern1"));
            Assert.True(tree.SuppressedWhenFolded("Bend-Lines1"));
            Assert.True(tree.SuppressedWhenFolded("Corner-Trim2"));
            Assert.False(tree.SuppressedWhenFolded("Cut-Extrude2"));
        }

        [Fact]
        public void A_part_without_sheet_metal_has_an_empty_tree()
        {
            SheetMetalTree tree = SheetMetalTree.Of(new[] { F("Boss-Extrude1", "Extrusion", null, F("Sketch1", "ProfileFeature")) });
            Assert.Empty(tree.Owners);
            Assert.Empty(tree.Closure(new[] { "Boss-Extrude1" }));
            Assert.Empty(SheetMetalTree.Empty.Owners);
        }
    }
}
