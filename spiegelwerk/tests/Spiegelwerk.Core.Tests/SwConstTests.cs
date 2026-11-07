using System;
using System.Linq;
using System.Reflection;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Records;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>
    /// Core copies swconst numbers so it builds without SOLIDWORKS. Checked here against the
    /// interop by name (2024 SP1; the numbers are the same in the 2019 interop).
    /// </summary>
    public class SwConstTests
    {
        /// <summary>Every Core value has a swconst member of the mapped name with the same number; with <paramref name="complete"/> Core leaves none out.</summary>
        private static void SameNumbers<TCore, TSw>(Func<string, string> swName, bool complete = true)
            where TCore : struct, Enum
            where TSw : struct, Enum
        {
            foreach (TCore c in Enum.GetValues(typeof(TCore)))
            {
                string name = swName(c.ToString());
                Assert.True(Enum.IsDefined(typeof(TSw), name), $"{typeof(TSw).Name}.{name} fehlt.");
                Assert.Equal(Convert.ToInt32(Enum.Parse(typeof(TSw), name)), Convert.ToInt32(c));
            }
            if (complete) Assert.Equal(Enum.GetValues(typeof(TSw)).Length, Enum.GetValues(typeof(TCore)).Length);
        }

        /// <summary>The same for a class of int constants (<see cref="HoleTypes"/> and alike).</summary>
        private static void SameConstants<TSw>(Type core, string prefix) where TSw : struct, Enum
        {
            FieldInfo[] constants = core.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).ToArray();
            foreach (FieldInfo f in constants)
            {
                string name = prefix + f.Name;
                Assert.True(Enum.IsDefined(typeof(TSw), name), $"{typeof(TSw).Name}.{name} fehlt.");
                Assert.Equal(Convert.ToInt32(Enum.Parse(typeof(TSw), name)), (int)f.GetRawConstantValue()!);
            }
            Assert.Equal(Enum.GetValues(typeof(TSw)).Length, constants.Length);
        }

        [Fact]
        public void Enums_have_the_swconst_numbers()
        {
            SameNumbers<EndCondition, swEndConditions_e>(n => "swEndCond" + n);
            SameNumbers<StartCondition, swStartConditions_e>(n => "swStart" + n);
            SameNumbers<Tangency, swTangencyType_e>(n => n == nameof(Tangency.MinimumTwist) ? "swMinimumTwist" : "swTangency" + n);
            SameNumbers<TwistControl, swTwistControlType_e>(n => "swTwistControl" + n);
            SameNumbers<DraftKind, swDraftType_e>(n => "sw" + n + "Draft");
            SameNumbers<FaceFilletKind, swSimpleFilletType_e>(n => "sw" + n + "Fillet", complete: false);
            SameNumbers<FaceFilletKind, swFeatureFilletType_e>(n => "swFeatureFilletType_" + n, complete: false);
            SameNumbers<BendAllowanceKind, swBendAllowanceTypes_e>(n => "swBendAllowance" + n);
            SameNumbers<ReliefKind, swSheetMetalReliefTypes_e>(n => "swSheetMetalRelief" + n);
        }

        [Fact]
        public void Sheet_metal_override_parameters_are_numbered_as_the_flags_read_them()
        {
            // SheetMetalParameters reads GetOverrideDefaultParameter2(0/1/2) into its three flags.
            Assert.Equal(0, (int)swSheetMetalOverrideDefaultParameters_e.swSheetMetalOverrideDefaultParameters_BendParameters);
            Assert.Equal(1, (int)swSheetMetalOverrideDefaultParameters_e.swSheetMetalOverrideDefaultParameters_BendAllowance);
            Assert.Equal(2, (int)swSheetMetalOverrideDefaultParameters_e.swSheetMetalOverrideDefaultParameters_AutoRelief);
            Assert.Equal(0, (int)swSheetMetalModifierError_e.swSheetMetalModifierError_NoError);
        }

        [Fact]
        public void Sheet_metal_feature_and_flange_offset_numbers()
        {
            // CreateDefinition numbers the add-in uses or names (blech-plan S1a).
            Assert.Equal(30, (int)swFeatureNameID_e.swFmSheetMetal);
            Assert.Equal(31, (int)swFeatureNameID_e.swFmFlattenBends);
            Assert.Equal(32, (int)swFeatureNameID_e.swFmProcessBends);
            Assert.Equal(34, (int)swFeatureNameID_e.swFmBaseFlange);
            Assert.Equal(38, (int)swFeatureNameID_e.swFmFlatPattern);
            Assert.Equal(115, (int)swFeatureNameID_e.swFmNormalCut);
            // swFlangeOffsetTypes_e 1–5 map to end conditions; 6 (up to edge and merge) exists only from the 2024 interop and is not rebuilt.
            foreach (swFlangeOffsetTypes_e t in Enum.GetValues(typeof(swFlangeOffsetTypes_e)))
            {
                EndCondition? end = Spiegelwerk.Core.Planning.SheetMetalRules.FromFlangeOffset((int)t);
                Assert.Equal(t != swFlangeOffsetTypes_e.swFlangeOffsetUptoEdgeAndMerge, end.HasValue);
                if (end.HasValue) Assert.Equal((int)t, Spiegelwerk.Core.Planning.SheetMetalRules.ToFlangeOffset(end.Value));
            }
            Assert.Equal(6, (int)swFlangeOffsetTypes_e.swFlangeOffsetUptoEdgeAndMerge);
            Assert.Equal((int)EndCondition.MidPlane, (int)swEndConditions_e.swEndCondMidPlane);
            Assert.Equal(EndCondition.MidPlane, Spiegelwerk.Core.Planning.SheetMetalRules.FromFlangeOffset((int)swFlangeOffsetTypes_e.swFlangeOffsetMidPlane));
        }

        [Fact]
        public void Type_constants_have_the_swconst_numbers()
        {
            SameConstants<swWzdGeneralHoleTypes_e>(typeof(HoleTypes), "swWzd");
            SameConstants<swAdvWzdGeneralHoleTypes_e>(typeof(AdvancedHoleTypes), "swAdvWzd");
            SameConstants<swRevolveType_e>(typeof(RevolveTypes), "swRevolveType");
            SameConstants<swHemTypes_e>(typeof(HemTypes), "swHemType");
        }

        [Fact]
        public void Edge_flange_numbers()
        {
            // The record carries these numbers through unchanged; the builder passes them to SOLIDWORKS (blech-plan S3).
            SameConstants<swFlangeOffsetTypes_e>(typeof(FlangeOffsetTypes), "swFlangeOffset");
            SameConstants<swFlangeDimTypes_e>(typeof(FlangeDimTypes), "swFlangeDimType");
            SameConstants<swFlangePositionTypes_e>(typeof(FlangePositionTypes), "swFlangePositionType");
            SameConstants<swInsertEdgeFlangeOptions_e>(typeof(EdgeFlangeOptions), "swInsertEdgeFlange");
            SameConstants<swReliefTearTypes_e>(typeof(ReliefTearTypes), "swReliefTearType");
            Assert.Equal((int)swFeatureNameID_e.swFmEdgeFlange, Spiegelwerk.Core.Planning.EdgeFlangeRules.FeatureId);
            // Every error AddEdges can return has a text of its own.
            foreach (swEdgeFlangeError_e e in Enum.GetValues(typeof(swEdgeFlangeError_e)))
                Assert.DoesNotContain("Fehler " + (int)e, Spiegelwerk.Core.Planning.EdgeFlangeRules.AddEdgesError((int)e));
        }

        [Fact]
        public void Jog_and_sketched_bend_numbers()
        {
            // The jog record carries these through unchanged; JogRules turns two of them into the insert call's codes (blech-plan S4).
            SameConstants<swJogOffsetTypes_e>(typeof(JogOffsetTypes), "swJogOffset");
            SameConstants<swJogDimensionPositionType_e>(typeof(JogDimensionPositions), "swJogDimensionPosition");
            SameConstants<swJogPositionType_e>(typeof(JogPositions), "swJogPosition");
            Assert.Equal((int)swFeatureNameID_e.swFmSketchBend, Spiegelwerk.Core.Planning.SheetMetalBendRules.SketchBendId);
            Assert.Equal((int)swFeatureNameID_e.swFmSM3dBend, Spiegelwerk.Core.Planning.SheetMetalBendRules.SM3dBendId);
            // The sketched bend's PositionType is swFlangePositionTypes_e; only the creation code counts the centre line as 0.
            Assert.Equal((short)0, Spiegelwerk.Core.Planning.SheetMetalBendRules.BendPosForCreation((int)swFlangePositionTypes_e.swFlangePositionTypeBendCenterLine));
            Assert.Equal((short)3, Spiegelwerk.Core.Planning.SheetMetalBendRules.BendPosForCreation((int)swFlangePositionTypes_e.swFlangePositionTypeBendOutside));
            Assert.Equal(2, (int)swSelectType_e.swSelFACES);
        }

        [Fact]
        public void Hem_and_miter_flange_numbers()
        {
            // HemRecord.Position and MiterFlangeRecord.PositionType carry these numbers through unchanged (blech-plan S2).
            Assert.Equal(0, (int)swHemPositionTypes_e.swHemPositionTypeInside);
            Assert.Equal(1, (int)swHemPositionTypes_e.swHemPositionTypeOutside);
            Assert.Equal((int)swFlangePositionTypes_e.swFlangePositionTypeMaterialInside, new MiterFlangeRecord("M", "SMMiteredFlange", "S").PositionType);
            Assert.Equal(3, (int)swFlangePositionTypes_e.swFlangePositionTypeBendOutside);
        }

        /// <summary>The general type a detailed Hole Wizard type belongs to, read from its name.</summary>
        private static int GeneralTypeByName(string name)
        {
            string n = name.Substring(2);
            // The old types (swSimple, swCounterBored ...) name no end condition.
            if (!n.Contains("Blind") && !n.Contains("Thru")) return HoleTypes.Legacy;
            if (n.StartsWith("CounterBoreSlot")) return HoleTypes.CounterBoreSlot;
            if (n.StartsWith("CounterSinkSlot")) return HoleTypes.CounterSinkSlot;
            if (n.StartsWith("Slot")) return HoleTypes.HoleSlot;
            if (n.StartsWith("CounterBore")) return HoleTypes.CounterBore;
            if (n.StartsWith("CounterSink")) return HoleTypes.CounterSink;
            if (n.StartsWith("PipeTap")) return HoleTypes.PipeTap;
            if (n.StartsWith("Tap")) return HoleTypes.Tap;
            if (n.StartsWith("Hole")) return HoleTypes.Hole;
            throw new ArgumentException($"Bohrungstyp {name} nicht zugeordnet.");
        }

        [Fact]
        public void Every_detailed_hole_type_maps_to_the_general_type_its_name_says()
        {
            foreach (swWzdHoleTypes_e t in Enum.GetValues(typeof(swWzdHoleTypes_e)))
            {
                int? generic = HoleTypes.GenericOf((int)t);
                Assert.True(generic == GeneralTypeByName(t.ToString()), $"{t} ({(int)t}) → {generic}");
                Assert.Equal(t.ToString().Contains("Slot"), HoleTypes.IsSlot(generic!.Value));
            }
        }
    }
}
