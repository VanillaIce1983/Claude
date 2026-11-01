using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>
    /// Sheet metal in the same part (blech-plan S1b, A1 §11): the guard against the snapshot, the
    /// copy's own values on its SheetMetal, the variants, tabs only on the job's own bodies, no
    /// „Original löschen“.
    /// </summary>
    public class SheetMetalSamePartTests
    {
        private static SheetMetalParameters Params(double t, double r, double k) =>
            new SheetMetalParameters { Thickness = t, BendRadius = r, BendAllowance = new BendAllowanceData { Kind = BendAllowanceKind.KFactor, KFactor = k } };

        [Fact]
        public void Guard_expects_the_snapshot_in_the_same_part_and_refuses_the_step_without_one()
        {
            Assert.Equal(GuardExpectation.Snapshot, SheetMetalRules.GuardExpectationOf(samePart: true, snapshotTaken: true));
            // The original's values would be unguarded: the step is not built.
            Assert.Equal(GuardExpectation.NotBuilt, SheetMetalRules.GuardExpectationOf(samePart: true, snapshotTaken: false));
            Assert.EndsWith("ob das Original unverändert bleibt.", SheetMetalRules.GuardNotReadable);
        }

        [Fact]
        public void In_a_new_part_the_guard_expects_the_snapshot_from_the_second_body_on()
        {
            // The first base flange makes the defaults – nothing to protect before it. Deviation from
            // the plan (S1a): later steps are compared with the snapshot, not with the original's defaults.
            Assert.Equal(GuardExpectation.None, SheetMetalRules.GuardExpectationOf(samePart: false, snapshotTaken: false));
            Assert.Equal(GuardExpectation.Snapshot, SheetMetalRules.GuardExpectationOf(samePart: false, snapshotTaken: true));
        }

        [Fact]
        public void Guard_compares_the_bodies_always_in_the_same_part()
        {
            Assert.True(SheetMetalRules.GuardComparesBodies(samePart: true, valuesChanged: false));
            Assert.True(SheetMetalRules.GuardComparesBodies(samePart: false, valuesChanged: true));
            Assert.False(SheetMetalRules.GuardComparesBodies(samePart: false, valuesChanged: false));
        }

        [Fact]
        public void Guard_abort_says_the_original_may_be_affected_in_the_same_part()
        {
            var none = Array.Empty<string>();
            var changed = new[] { "Blechdicke: erwartet 2 mm, gelesen 3 mm" };
            Assert.Null(SheetMetalRules.GuardAbort(true, changed, none, none));
            string? notRestored = SheetMetalRules.GuardAbort(true, changed, changed, new[] { "Base-Flange1" });
            Assert.Equal("Achtung: Die Blech-Standardwerte des Teils wurden verändert und ließen sich nicht wiederherstellen (Blechdicke: erwartet 2 mm, gelesen 3 mm). " +
                         "Betroffen: „Base-Flange1“. Das Original kann betroffen sein – bitte mit Rückgängig prüfen.", notRestored);
            // A body changed although the values stayed: only the same part compares that.
            Assert.Equal("Achtung: Mit diesem Schritt haben sich frühere Körper verändert, obwohl die Blech-Standardwerte gleich geblieben sind. " +
                         "Betroffen: „Base-Flange1“. Das Original kann betroffen sein – bitte mit Rückgängig prüfen.",
                SheetMetalRules.GuardAbort(true, none, none, new[] { "Base-Flange1" }));
            // New part: the text of S1a.
            Assert.Equal("Achtung: Die Blech-Standardwerte des Teils hatten sich mit diesem Schritt geändert (Blechdicke: erwartet 2 mm, gelesen 3 mm) und wurden wiederhergestellt, " +
                         "frühere Körper sind danach aber nicht wie vorher. Betroffen: „Base-Flange1“. Die bisher gespiegelten Blechkörper können sich geändert haben – bitte prüfen.",
                SheetMetalRules.GuardAbort(false, changed, none, new[] { "Base-Flange1" }));
            Assert.Equal("Die Blech-Standardwerte hatten sich mit diesem Schritt geändert und wurden wiederhergestellt (Blechdicke: erwartet 2 mm, gelesen 3 mm).",
                SheetMetalRules.GuardRestored(changed));
        }

        [Fact]
        public void Guard_restores_a_single_body_snapshot_through_the_groups_that_differ()
        {
            // Multibody before: the groups the body had overridden.
            SheetMetalParameters before = Params(0.002, 0.001, 0.44);
            before.OverrideReadCode = 0;
            before.OverrideBendAllowance = true;
            Assert.Equal(new[] { 1 }, SheetMetalRules.RestoreGroups(before, Params(0.003, 0.001, 0.44)));
            // Single-body before (flags not applicable), multibody now: every group whose values moved.
            before.OverrideReadCode = 2;
            SheetMetalParameters now = Params(0.003, 0.001, 0.44);
            now.OverrideReadCode = 0;
            Assert.Equal(new[] { 0 }, SheetMetalRules.RestoreGroups(before, now));
            now.BendAllowance.KFactor = 0.5;
            now.Relief = new ReliefData { Kind = ReliefKind.Obround };
            Assert.Equal(new[] { 0, 1, 2 }, SheetMetalRules.RestoreGroups(before, now));
            // Still single-body, or not readable: nothing can be overridden.
            now.OverrideReadCode = 2;
            Assert.Empty(SheetMetalRules.RestoreGroups(before, now));
            Assert.Empty(SheetMetalRules.RestoreGroups(before, null));
        }

        [Fact]
        public void Same_part_copy_overrides_what_the_part_defaults_do_not_give_it()
        {
            SheetMetalParameters defaults = Params(0.002, 0.001, 0.44);
            // A body that uses the defaults: nothing to override.
            SheetMetalParameters body = Params(0.002, 0.001, 0.44);
            body.OverrideReadCode = 0;
            Assert.Empty(SheetMetalRules.SamePartOverrides(body, defaults, defaults));
            // Its own thickness: the group it set.
            body.OverrideBendParameters = true;
            body.Thickness = 0.003;
            Assert.Equal(new[] { 0 }, SheetMetalRules.SamePartOverrides(body, defaults, defaults));
            // A single-body original (no flags) whose values are not the part's defaults: every group that differs.
            SheetMetalParameters single = Params(0.003, 0.001, 0.5);
            single.OverrideReadCode = 2;
            Assert.Equal(new[] { 0, 1 }, SheetMetalRules.SamePartOverrides(single, defaults, defaults));
            Assert.Equal(new[] { 0, 1 }, SheetMetalRules.SamePartOverrides(single, defaults, null));
            // There the SheetMetal's values are the body's own; with readable flags only the overridden groups.
            Assert.Equal(0.5, SheetMetalRules.BodyValues(defaults, single).BendAllowance.KFactor);
            Assert.Equal(0.44, SheetMetalRules.BodyValues(defaults, body).BendAllowance.KFactor);
            Assert.Equal(0.003, SheetMetalRules.BodyValues(defaults, body).Thickness);
            // Defaults not readable: only what the body set.
            Assert.Equal(new[] { 0 }, SheetMetalRules.SamePartOverrides(body, null, defaults));
            Assert.Empty(SheetMetalRules.SamePartOverrides(null, defaults, defaults));
        }

        [Fact]
        public void Same_part_offers_no_ReverseThickness_toggle_for_a_closed_profile()
        {
            // A1 P1: on a closed profile ReverseThickness has no effect and would write the shared values.
            Assert.Equal(new[] { BaseFlangeToggle.ReverseDirection },
                SheetMetalRules.BaseFlangeToggles(closedProfile: true, symmetricThickness: false, midPlane: false, samePart: true).Select(t => t.toggle));
            Assert.Equal(new[] { BaseFlangeToggle.ReverseDirection, BaseFlangeToggle.ReverseThickness },
                SheetMetalRules.BaseFlangeToggles(true, false, false, samePart: false).Select(t => t.toggle));
            Assert.Equal(new[] { "Blechdicke auf die andere Seite" }, SheetMetalRules.BaseFlangeToggles(true, true, false, false).Select(t => t.label));
            // An open contour keeps its material side toggle in the same part too – under the guard.
            Assert.Equal(new[] { "Richtung umgekehrt", "Materialseite der Kontur gewechselt" },
                SheetMetalRules.BaseFlangeToggles(false, false, false, samePart: true).Select(t => t.label));
            Assert.Equal(new[] { BaseFlangeToggle.ReverseThickness }, SheetMetalRules.BaseFlangeToggles(false, false, midPlane: true, samePart: true).Select(t => t.toggle));
            Assert.Empty(SheetMetalRules.BaseFlangeToggles(false, symmetricThickness: true, midPlane: true, samePart: false));
        }

        [Fact]
        public void Tab_in_separate_body_mode_needs_a_body_of_the_job()
        {
            Assert.Equal("Es gibt noch keinen gespiegelten Blechkörper, an den die Lasche anschließen könnte; das Original bleibt unberührt.",
                SheetMetalRules.TabRefusal(tab: true, separateBody: true, ownBodies: 0));
            Assert.Null(SheetMetalRules.TabRefusal(true, true, 1));
            // Selected features in the same part: a tab may join the original's body, like a boss.
            Assert.Null(SheetMetalRules.TabRefusal(true, separateBody: false, ownBodies: 0));
            // A base flange of its own never merges; the body count decides.
            Assert.Null(SheetMetalRules.TabRefusal(false, true, 0));
            Assert.Equal(new[] { BaseFlangePath.V2, BaseFlangePath.DataObject, BaseFlangePath.V1 }, SheetMetalRules.CreationPathOrder(tabInSeparateBodyMode: true));
            Assert.Equal(1, SheetMetalRules.ExpectedBodyDelta(tab: false));
        }

        [Fact]
        public void Delete_original_refused_when_sheet_metal_records_were_built()
        {
            var records = new List<FeatureRecord>
            {
                new ExtrudeRecord("Boss-Extrude1", "Extrusion", "Sketch0"),
                new BaseFlangeRecord("Base-Flange1", "SMBaseFlange", "Sketch1"),
                new FlatPatternRecord("Flat-Pattern1", "FlatPattern", "Base-Flange1"),
            };
            Assert.Equal("Original nicht gelöscht: Die Blech-Standardwerte des Teils hängen am ersten Blechkörper; die Kopie könnte sich mit dem Löschen ändern.",
                SheetMetalRules.DeleteRefusal(records));
            // A flat pattern alone counts too; a part without sheet metal is not this rule's business.
            Assert.NotNull(SheetMetalRules.DeleteRefusal(records.Skip(2)));
            Assert.Null(SheetMetalRules.DeleteRefusal(new FeatureRecord[] { records[0], new ExtrudeRecord("Cut-Extrude1", "Cut", "Sketch1") { IsCut = true } }));
        }

        [Fact]
        public void Same_part_says_that_original_and_copy_share_the_defaults()
        {
            Assert.Contains("teilen die Blech-Standardwerte", SheetMetalRules.SharedDefaultsNote);
            Assert.Equal("Gleichungen auf den Blech-Standardwerten („Thickness@Blech“) wirken im selben Teil auch auf die gespiegelten Blechkörper, " +
                         "soweit deren Blech-Features sie nicht überschreiben.",
                SheetMetalRules.SharedDefaultsEquations(new[] { "Thickness@Blech" }));
        }
    }
}
