using System.Collections.Generic;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>
    /// One ownership model for every feature that brings parts (blech-plan §1.3): which records
    /// own sketches, which bring parts at all, and which delete their absorbed features with them.
    /// The sheet-metal records do not exist yet; stubs stand in for them as the plan defines them.
    /// </summary>
    public class OwnershipTests
    {
        private sealed class FormToolStub : PositionedFeatureRecord
        {
            public FormToolStub() : base("Louver1", "FormToolInstance", "Skizze7", new SketchModel())
            {
            }
        }

        /// <summary>A base flange brings its SheetMetal and flat pattern, but owns no sketch: its profile is an ordinary, maybe shared sketch.</summary>
        private sealed class BaseFlangeStub : FeatureRecord
        {
            public BaseFlangeStub() : base("Basis-Lasche1", "SMBaseFlange")
            {
            }

            public override bool BringsParts => true;
        }

        private sealed class EdgeFlangeStub : FeatureRecord
        {
            public EdgeFlangeStub() : base("Kantenlasche1", "EdgeFlange")
            {
            }

            public override IReadOnlyList<string> OwnedSketches => new[] { "Skizze3", "Skizze4" };
        }

        [Fact]
        public void Owners_report_parts_and_owned_sketches()
        {
            var hole = new HoleRecord("Bohrung1", "HoleWzd", "Skizze2", new SketchModel());
            Assert.True(hole.BringsParts);
            Assert.Equal(new[] { "Skizze2" }, hole.OwnedSketches);
            Assert.True(hole.DeleteWithAbsorbed);

            var tool = new FormToolStub();
            Assert.True(tool.BringsParts);
            Assert.Equal(new[] { "Skizze7" }, tool.OwnedSketches);

            var flange = new EdgeFlangeStub();
            Assert.True(flange.BringsParts);
            Assert.True(flange.DeleteWithAbsorbed);

            var sketch = new SketchRecord("Skizze1", new SketchModel());
            Assert.False(sketch.BringsParts);
            Assert.Empty(sketch.OwnedSketches);
        }

        [Fact]
        public void Base_flange_brings_parts_but_keeps_its_profile_sketch_on_removal()
        {
            // K3: deleting "with absorbed" would take the profile sketch, a record of its own.
            var flange = new BaseFlangeStub();
            Assert.True(flange.BringsParts);
            Assert.Empty(flange.OwnedSketches);
            Assert.False(flange.DeleteWithAbsorbed);

            var extrude = new ExtrudeRecord("Linear austragen1", "Extrusion", "Skizze1");
            Assert.False(extrude.BringsParts);
            Assert.False(extrude.DeleteWithAbsorbed);
        }

        [Fact]
        public void Defaults_of_the_new_virtuals_change_nothing()
        {
            var extrude = new ExtrudeRecord("Linear austragen1", "Extrusion", "Skizze1");
            Assert.Equal(extrude.Name, extrude.MeasureUntil);
            Assert.False(extrude.TouchesSheetMetalDefaults);
            Assert.False(extrude.SampleSubFeatures);
            Assert.True(new UnsupportedRecord("Saum1", "Hem", "…", true).TriggersFallback);
        }

        [Fact]
        public void Esc_withdraws_every_owner_after_the_last_step_built_but_not_that_step()
        {
            var records = new FeatureRecord[]
            {
                new SketchRecord("Skizze1", new SketchModel()),
                new HoleRecord("Bohrung1", "HoleWzd", "Skizze2", new SketchModel()),
                new BaseFlangeStub(),
                new EdgeFlangeStub(),
                new ExtrudeRecord("Linear austragen1", "Extrusion", "Skizze1"),
            };
            Assert.Equal(new[] { "Bohrung1", "Basis-Lasche1", "Kantenlasche1" }, RunSteps.OwnersNotBuiltAfter(records, 0));
            // Esc right after the hole: it was built, its parts stay.
            Assert.Equal(new[] { "Basis-Lasche1", "Kantenlasche1" }, RunSteps.OwnersNotBuiltAfter(records, 1));
            Assert.Equal(new[] { "Kantenlasche1" }, RunSteps.OwnersNotBuiltAfter(records, 2));
            Assert.Empty(RunSteps.OwnersNotBuiltAfter(records, 3));
            Assert.Empty(RunSteps.OwnersNotBuiltAfter(records, records.Length - 1));
        }

        [Theory]
        [InlineData(PartOwnerKind.Hole, true, "Profilskizze der Bohrung „X“, die nicht gespiegelt wird – entfällt mit ihr.")]
        [InlineData(PartOwnerKind.FormTool, true, "Skizze des Umformwerkzeugs „X“, das nicht gespiegelt wird – entfällt mit ihm.")]
        [InlineData(PartOwnerKind.EdgeFlange, false, "Profilskizze der Kantenlasche „X“, die nicht gespiegelt wird – entfällt mit ihr.")]
        [InlineData(PartOwnerKind.Hem, false, "Skizze des Saums „X“, der nicht gespiegelt wird – entfällt mit ihm.")]
        [InlineData(PartOwnerKind.SheetMetal, false, "Entsteht nur mit der Basislasche „X“.")]
        [InlineData(PartOwnerKind.FlatPattern, false, "Gehört zur Abwicklung „X“.")]
        public void A_part_without_its_owner_is_captured_as_the_owner_kind_says(PartOwnerKind kind, bool position, string leftOut)
        {
            var part = new PartOfFeatureRecord("Skizze9", "ProfileFeature", "X", kind);
            Assert.False(part.ChangesSolid);
            Assert.Equal(position, part.OwnerHasPositionSketch);
            Assert.Equal(leftOut, part.LeftOut);
        }
    }
}
