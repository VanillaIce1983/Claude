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
    /// Tab and slot (blech-plan S8, A4 §13 TabSlotRulesTests): D1/D2 swapped only without references and with a
    /// reversed edge, a closed edge undetermined, the slot feature by its name and in the tree, read-back
    /// comparison of only the values a group uses, the texts.
    /// </summary>
    public class TabSlotRulesTests
    {
        private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

        private static EdgeSignature Line(Vec3 a, Vec3 b) => new EdgeSignature(CurveKind.Line, a, b, (a + b) / 2, a.DistanceTo(b)) { Axis = (b - a).Normalized() };

        private static TabSlotGroup Group(bool offset = true, double d1 = 0.01, double d2 = 0.025) => new TabSlotGroup
        {
            TabEdge = Line(Vec3.Zero, new Vec3(0.1, 0, 0)),
            SlotFace = new FaceSignature(SurfaceKind.Plane, 0.002, new Vec3(0.05, 0.02, 0)) { Normal = Vec3.UnitY },
            Offset = offset,
            D1OffsetFromStart = d1,
            D2OffsetFromEnd = d2,
            SpacingType = TabSlotSpacingTypes.EqualSpacing,
            SpacingNumberOfInstances = 3,
            TabLength = 0.01,
            TabThickness = 0.002,
            TabHeightType = TabSlotHeightTypes.Blind,
            TabHeightValue = 0.002,
            SlotClearance = 0.0001,
        };

        [Fact]
        public void D1_and_D2_swap_only_without_references_and_with_a_reversed_edge()
        {
            var r = new Reflection(new Plane3(new Vec3(0.3, 0, 0), Vec3.UnitX));
            TabSlotGroup g = Group();
            EdgeSignature wanted = g.TabEdge.Mirror(r);
            // The copy's edge runs as the mirrored original: unchanged.
            Assert.Equal((0.01, 0.025, false), TabSlotRules.Offsets(g, wanted, Line(wanted.Start, wanted.End)));
            // The copy's edge runs the other way: D1 is at the other end.
            Assert.Equal((0.025, 0.01, true), TabSlotRules.Offsets(g, wanted, Line(wanted.End, wanted.Start)));
            Assert.True(TabSlotRules.OffsetsMaySwap(g));

            // A start reference fixes where D1 counts from: unchanged however the edge runs.
            TabSlotGroup withStart = Group();
            withStart.StartReference = GeometryRef.ToVertex(new Vec3(0.02, 0, 0));
            Assert.Equal((0.01, 0.025, false), TabSlotRules.Offsets(withStart, wanted, Line(wanted.End, wanted.Start)));
            Assert.False(TabSlotRules.OffsetsMaySwap(withStart));
            TabSlotGroup withEnd = Group();
            withEnd.EndReference = GeometryRef.ToVertex(new Vec3(0.08, 0, 0));
            Assert.Equal((0.01, 0.025, false), TabSlotRules.Offsets(withEnd, wanted, Line(wanted.End, wanted.Start)));

            // Without offsets there is nothing to swap.
            TabSlotGroup plain = Group(offset: false);
            Assert.Equal((0.01, 0.025, false), TabSlotRules.Offsets(plain, wanted, Line(wanted.End, wanted.Start)));
            Assert.False(TabSlotRules.OffsetsMaySwap(plain));
        }

        [Fact]
        public void A_closed_edge_or_ends_that_do_not_match_leave_it_undetermined()
        {
            TabSlotGroup g = Group();
            var circle = new EdgeSignature(CurveKind.Circle, new Vec3(0.01, 0, 0), new Vec3(0.01, 0, 0), new Vec3(-0.01, 0, 0), 0.0628);
            Assert.Null(TabSlotRules.Offsets(g, circle, circle));
            EdgeSignature wanted = g.TabEdge;
            Assert.Null(TabSlotRules.Offsets(g, wanted, Line(new Vec3(0, 0.5, 0), new Vec3(0.1, 0.5, 0))));
            Assert.Equal("Lasche und Schlitz: An welchem Ende der Laschenkante D1 zählt, ist nicht bestimmbar; D1 und D2 wie im Original.",
                TabSlotRules.OffsetsUndetermined(1, 1));
            Assert.StartsWith("Lasche und Schlitz, Gruppe 2:", TabSlotRules.OffsetsUndetermined(2, 3));
        }

        [Fact]
        public void The_mirror_keeps_the_reversed_tab_offset_and_every_value()
        {
            // Measured from the slot face, whose outward normal stays outward (A4 §9): the copy reads back the original's values.
            TabSlotGroup g = Group();
            g.TabHeightType = TabSlotHeightTypes.OffsetFromSurface;
            g.TabOffsetValue = 0.001;
            g.TabReverseDirection = true;
            TabSlotGroup copy = g.WithOffsets(g.D1OffsetFromStart, g.D2OffsetFromEnd);
            Assert.Empty(TabSlotRules.Differences(g, copy));
            Assert.True(TabSlotRules.HasTabOffset(g));
            Assert.False(TabSlotRules.HasTabOffset(Group()));
            // WithOffsets copies; the original group keeps its values.
            TabSlotGroup swapped = g.WithOffsets(g.D2OffsetFromEnd, g.D1OffsetFromStart);
            Assert.Equal(0.01, g.D1OffsetFromStart);
            Assert.Equal(0.025, swapped.D1OffsetFromStart);
            Assert.Same(g.TabEdge, swapped.TabEdge);
        }

        [Fact]
        public void Read_back_compares_only_what_the_group_uses()
        {
            TabSlotGroup want = Group(offset: false);
            TabSlotGroup got = Group(offset: false, d1: 0.5, d2: 0.5);
            // Offsets off: D1/D2 are not looked at, nor the spacing length of an equal spacing, nor an unused height or treatment.
            got.Spacing = 0.7;
            got.TabOffsetValue = 0.3;
            got.TabFilletEdgeTreatmentValue = 0.2;
            Assert.Empty(TabSlotRules.Differences(want, got));
            got.SpacingNumberOfInstances = 4;
            Assert.Equal(new[] { "Anzahl: erwartet 3, gelesen 4" }, TabSlotRules.Differences(want, got, De));

            TabSlotGroup offset = Group();
            List<string> d = TabSlotRules.Differences(offset, Group(d1: 0.025, d2: 0.01), De);
            Assert.Equal(new[] { "D1: erwartet 10 mm, gelesen 25 mm", "D2: erwartet 25 mm, gelesen 10 mm" }, d);

            TabSlotGroup fillet = Group();
            fillet.TabEdgesType = TabEdgesTypes.FilletEdge;
            fillet.TabFilletEdgeTreatmentValue = 0.0005;
            TabSlotGroup filletGot = fillet.WithOffsets(0.01, 0.025);
            filletGot.TabFilletEdgeTreatmentValue = 0.001;
            Assert.Equal(new[] { "Verrundung der Laschen: erwartet 0,5 mm, gelesen 1 mm" }, TabSlotRules.Differences(fillet, filletGot, De));
        }

        [Fact]
        public void Groups_are_compared_in_their_order_and_by_count()
        {
            var want = new List<TabSlotGroup> { Group(), Group() };
            Assert.Empty(TabSlotRules.Differences(want, new List<TabSlotGroup> { Group(), Group() }));
            Assert.Equal(new[] { "Lasche und Schlitz: 1 Gruppe gelesen statt 2 Gruppen." }, TabSlotRules.Differences(want, new List<TabSlotGroup> { Group() }));
            TabSlotGroup other = Group();
            other.SlotClearance = 0.0002;
            Assert.Equal(new[] { "Lasche und Schlitz, Gruppe 2: Schlitzspiel: erwartet 0,1 mm, gelesen 0,2 mm" },
                TabSlotRules.Differences(want, new List<TabSlotGroup> { Group(), other }, De));
        }

        [Theory]
        [InlineData("Tab and Slot-Tab1", "Tab and Slot-Slot1", true)]
        [InlineData("Lasche und Schlitz-Lasche3", "Lasche und Schlitz-Schlitz3", true)]
        [InlineData("Tab and Slot-Tab1", "Tab and Slot-Slot2", false)]
        [InlineData("Tab and Slot-Tab1", "Tab and Slot-Tab1", false)]
        [InlineData("Tab and Slot-Tab1", "Tab and Slot-tab1", false)]
        [InlineData("Tab and Slot-Tab1", "Other-Slot1", false)]
        [InlineData("Tab1", "Slot1", false)]
        [InlineData("Tab and Slot-Tab", "Tab and Slot-Slot", false)]
        public void The_slot_is_found_by_its_name(string tab, string candidate, bool slot)
        {
            Assert.Equal(slot, TabSlotRules.IsSlotName(tab, candidate));
        }

        [Fact]
        public void The_tree_makes_the_slot_a_part_of_its_tab()
        {
            static TreeFeature F(string name, string type, params string[] parents) => new TreeFeature(name, type, parents);
            var top = new[]
            {
                F("Sheet-Metal1", "SheetMetal"),
                F("Base-Flange1", "SMBaseFlange", "Sheet-Metal1"),
                F("Base-Flange2", "SMBaseFlange", "Sheet-Metal1"),
                // By its parent and the same data.
                F("Tab and Slot-Tab1", "TabAndSlot", "Base-Flange1", "Base-Flange2"),
                F("Tab and Slot-Slot1", "TabAndSlot", "Tab and Slot-Tab1"),
                // By its name only (no parent link, another type).
                F("Tab and Slot-Tab2", "TabAndSlot", "Base-Flange1"),
                F("Cut-Extrude1", "Cut", "Tab and Slot-Tab2"),
                F("Tab and Slot-Slot2", "SlotFeature"),
                // Without a slot of its own.
                F("Tab and Slot-Tab3", "TabAndSlot", "Base-Flange1"),
            };
            var answers = new[] { "Tab and Slot-Tab1", "Tab and Slot-Slot1", "Tab and Slot-Tab2", "Tab and Slot-Tab3" };
            SheetMetalTree tree = SheetMetalTree.Of(top, tabAndSlots: answers);
            Assert.Equal(("Tab and Slot-Tab1", PartOwnerKind.TabAndSlot), tree.Owners["Tab and Slot-Slot1"]);
            Assert.Equal("Tab and Slot-Slot1", tree.SlotOf("Tab and Slot-Tab1"));
            Assert.Equal("Tab and Slot-Slot2", tree.SlotOf("Tab and Slot-Tab2"));
            // A child that is no slot (a cut on it) is not taken.
            Assert.False(tree.Owners.ContainsKey("Cut-Extrude1"));
            Assert.Null(tree.SlotOf("Tab and Slot-Tab3"));
            // The slot is no tab of its own.
            Assert.Null(tree.SlotOf("Tab and Slot-Slot1"));
            // A selection brings the other half along, either way.
            Assert.Equal(new[] { "Tab and Slot-Slot1" }, tree.Closure(new[] { "Tab and Slot-Tab1" }).ToArray());
            Assert.Equal(new[] { "Tab and Slot-Tab2" }, tree.Closure(new[] { "Tab and Slot-Slot2" }).ToArray());
            // Without facts nothing is a tab.
            Assert.Null(SheetMetalTree.Of(top).SlotOf("Tab and Slot-Tab1"));
        }

        [Fact]
        public void The_record_brings_its_slot_and_is_measured_after_it()
        {
            var r = new TabAndSlotRecord("Tab and Slot-Tab1", "TabAndSlot");
            Assert.False(r.BringsParts);
            Assert.Equal(r.Name, r.MeasureUntil);
            r.SlotFeatureName = "Tab and Slot-Slot1";
            Assert.True(r.BringsParts);
            Assert.Equal("Tab and Slot-Slot1", r.MeasureUntil);
            // No sketches of its own: removing it takes nothing absorbed along.
            Assert.False(r.DeleteWithAbsorbed);
            var slot = new PartOfFeatureRecord("Tab and Slot-Slot1", "TabAndSlot", r.Name, PartOwnerKind.TabAndSlot);
            Assert.False(slot.ChangesSolid);
            Assert.False(slot.OwnerHasPositionSketch);
            Assert.Equal("Schlitz von Lasche und Schlitz „Tab and Slot-Tab1“, das nicht gespiegelt wird – entfällt mit ihm.", slot.LeftOut);
        }

        [Fact]
        public void Slot_options_are_noted_only_where_no_check_tells()
        {
            Assert.True(TabSlotRules.SlotOptionsCheckable(true, true, true));
            Assert.False(TabSlotRules.SlotOptionsCheckable(false, true, true));
            Assert.False(TabSlotRules.SlotOptionsCheckable(true, false, true));
            Assert.False(TabSlotRules.SlotOptionsCheckable(true, true, false));
            Assert.Null(TabSlotRules.SlotOptionsNote(true));
            Assert.Equal(TabSlotRules.SlotOptionsUnchecked, TabSlotRules.SlotOptionsNote(false));
            Assert.Contains("nicht geprüft", TabSlotRules.SlotOptionsUnchecked);
            Assert.Equal("Schlitz-Optionen: Standard (geprüft)", TabSlotRules.SlotOptionsChecked);
        }

        [Fact]
        public void Description_and_texts()
        {
            var r = new TabAndSlotRecord("T", "TabAndSlot");
            r.Groups.Add(Group());
            Assert.Equal("Lasche und Schlitz, 3 Laschen", TabSlotRules.Describe(r));
            r.Groups.Add(Group());
            Assert.Equal("Lasche und Schlitz, 2 Gruppen", TabSlotRules.Describe(r));
            Assert.EndsWith("wird nicht nachgebaut.", TabSlotRules.InContext);
            Assert.Equal("Lasche und Schlitz, Gruppe 2: SOLIDWORKS nennt eine andere Schlitzfläche als die gespiegelte.", TabSlotRules.OtherEntities(2, 2, "Schlitzfläche"));
            Assert.Contains("„T“", TabSlotRules.SameAsEarlier("T"));
        }
    }
}
