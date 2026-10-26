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
        }

        [Fact]
        public void Type_constants_have_the_swconst_numbers()
        {
            SameConstants<swWzdGeneralHoleTypes_e>(typeof(HoleTypes), "swWzd");
            SameConstants<swAdvWzdGeneralHoleTypes_e>(typeof(AdvancedHoleTypes), "swAdvWzd");
            SameConstants<swRevolveType_e>(typeof(RevolveTypes), "swRevolveType");
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
