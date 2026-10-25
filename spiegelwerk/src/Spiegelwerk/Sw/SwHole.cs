using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace Spiegelwerk.Sw
{
    /// <summary>
    /// Sizes and options of a Hole Wizard hole, read from and written to
    /// IWizardHoleFeatureData2 by property name. Which of them apply depends on the hole type;
    /// one that does not apply may read as 0 or throw, so every access is guarded. Options come
    /// first: they decide which sizes apply (a near-side countersink needs to be switched on
    /// before its diameter means anything).
    /// </summary>
    internal static class SwHole
    {
        private sealed class Prop
        {
            public Prop(string name, Func<WizardHoleFeatureData2, double> get, Action<WizardHoleFeatureData2, double> set)
            {
                Name = name;
                Get = get;
                Set = set;
            }

            public string Name { get; }
            public Func<WizardHoleFeatureData2, double> Get { get; }
            public Action<WizardHoleFeatureData2, double> Set { get; }
        }

        private static Prop D(string name, Func<WizardHoleFeatureData2, double> get, Action<WizardHoleFeatureData2, double> set) => new Prop(name, get, set);

        private static Prop I(string name, Func<WizardHoleFeatureData2, int> get, Action<WizardHoleFeatureData2, int> set) =>
            new Prop(name, d => get(d), (d, v) => set(d, (int)Math.Round(v)));

        private static Prop B(string name, Func<WizardHoleFeatureData2, bool> get, Action<WizardHoleFeatureData2, bool> set) =>
            new Prop(name, d => get(d) ? 1 : 0, (d, v) => set(d, v > 0.5));

        private static readonly Prop[] Props =
        {
            B("NearSideCounterSink", d => d.NearSideCounterSink, (d, v) => d.NearSideCounterSink = v),
            B("FarSideCounterSink", d => d.FarSideCounterSink, (d, v) => d.FarSideCounterSink = v),
            I("HoleFit", d => d.HoleFit, (d, v) => d.HoleFit = v),
            I("HeadClearanceType", d => d.HeadClearanceType, (d, v) => d.HeadClearanceType = v),
            I("CosmeticThreadType", d => d.CosmeticThreadType, (d, v) => d.CosmeticThreadType = v),
            I("ThreadEndCondition", d => d.ThreadEndCondition, (d, v) => d.ThreadEndCondition = v),
            I("TapType", d => d.TapType, (d, v) => d.TapType = v),

            D("Diameter", d => d.Diameter, (d, v) => d.Diameter = v),
            D("HoleDiameter", d => d.HoleDiameter, (d, v) => d.HoleDiameter = v),
            D("ThruHoleDiameter", d => d.ThruHoleDiameter, (d, v) => d.ThruHoleDiameter = v),
            D("CounterBoreDiameter", d => d.CounterBoreDiameter, (d, v) => d.CounterBoreDiameter = v),
            D("CounterDrillDiameter", d => d.CounterDrillDiameter, (d, v) => d.CounterDrillDiameter = v),
            D("CounterSinkDiameter", d => d.CounterSinkDiameter, (d, v) => d.CounterSinkDiameter = v),
            D("MinorDiameter", d => d.MinorDiameter, (d, v) => d.MinorDiameter = v),
            D("MajorDiameter", d => d.MajorDiameter, (d, v) => d.MajorDiameter = v),
            D("TapDrillDiameter", d => d.TapDrillDiameter, (d, v) => d.TapDrillDiameter = v),
            D("ThruTapDrillDiameter", d => d.ThruTapDrillDiameter, (d, v) => d.ThruTapDrillDiameter = v),
            D("NearCounterSinkDiameter", d => d.NearCounterSinkDiameter, (d, v) => d.NearCounterSinkDiameter = v),
            D("MidCounterSinkDiameter", d => d.MidCounterSinkDiameter, (d, v) => d.MidCounterSinkDiameter = v),
            D("FarCounterSinkDiameter", d => d.FarCounterSinkDiameter, (d, v) => d.FarCounterSinkDiameter = v),
            D("ThreadDiameter", d => d.ThreadDiameter, (d, v) => d.ThreadDiameter = v),

            D("Depth", d => d.Depth, (d, v) => d.Depth = v),
            D("HoleDepth", d => d.HoleDepth, (d, v) => d.HoleDepth = v),
            D("ThruHoleDepth", d => d.ThruHoleDepth, (d, v) => d.ThruHoleDepth = v),
            D("CounterBoreDepth", d => d.CounterBoreDepth, (d, v) => d.CounterBoreDepth = v),
            D("CounterDrillDepth", d => d.CounterDrillDepth, (d, v) => d.CounterDrillDepth = v),
            D("TapDrillDepth", d => d.TapDrillDepth, (d, v) => d.TapDrillDepth = v),
            D("ThruTapDrillDepth", d => d.ThruTapDrillDepth, (d, v) => d.ThruTapDrillDepth = v),
            D("ThreadDepth", d => d.ThreadDepth, (d, v) => d.ThreadDepth = v),

            D("DrillAngle", d => d.DrillAngle, (d, v) => d.DrillAngle = v),
            D("CounterSinkAngle", d => d.CounterSinkAngle, (d, v) => d.CounterSinkAngle = v),
            D("CounterDrillAngle", d => d.CounterDrillAngle, (d, v) => d.CounterDrillAngle = v),
            D("NearCounterSinkAngle", d => d.NearCounterSinkAngle, (d, v) => d.NearCounterSinkAngle = v),
            D("MidCounterSinkAngle", d => d.MidCounterSinkAngle, (d, v) => d.MidCounterSinkAngle = v),
            D("FarCounterSinkAngle", d => d.FarCounterSinkAngle, (d, v) => d.FarCounterSinkAngle = v),
            D("ThreadAngle", d => d.ThreadAngle, (d, v) => d.ThreadAngle = v),

            D("HeadClearance", d => d.HeadClearance, (d, v) => d.HeadClearance = v),
            D("OffsetDistance", d => d.OffsetDistance, (d, v) => d.OffsetDistance = v),
            D("Length", d => d.Length, (d, v) => d.Length = v),
        };

        /// <summary>Every value the hole can report.</summary>
        public static Dictionary<string, double> Read(WizardHoleFeatureData2 d)
        {
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (Prop p in Props)
            {
                try
                {
                    double v = p.Get(d);
                    if (!double.IsNaN(v)) values[p.Name] = v;
                }
                catch (Exception)
                {
                    // Not applicable to this hole type.
                }
            }
            return values;
        }

        /// <summary>The wanted values the hole currently has differently (readable ones only).</summary>
        public static List<string> Differences(WizardHoleFeatureData2 d, IReadOnlyDictionary<string, double> wanted)
        {
            Dictionary<string, double> now = Read(d);
            return Props.Where(p => wanted.ContainsKey(p.Name) && now.ContainsKey(p.Name) && !Same(now[p.Name], wanted[p.Name]))
                .Select(p => p.Name).ToList();
        }

        /// <summary>Writes the wanted values that differ, in table order; a refused one is skipped.</summary>
        public static void Apply(WizardHoleFeatureData2 d, IReadOnlyDictionary<string, double> wanted)
        {
            foreach (Prop p in Props)
            {
                if (!wanted.TryGetValue(p.Name, out double v)) continue;
                try
                {
                    if (!Same(p.Get(d), v)) p.Set(d, v);
                }
                catch (Exception)
                {
                    // Not applicable to the new hole either – or refused; Differences reports it.
                }
            }
        }

        private static bool Same(double a, double b) => Math.Abs(a - b) <= Math.Max(1e-12, 1e-9 * Math.Max(Math.Abs(a), Math.Abs(b)));

        /// <summary>The position sketch of a hole: the sketch its hole points belong to.</summary>
        public static Feature? PositionSketch(ModelDoc2 doc, Feature hole)
        {
            var d = (WizardHoleFeatureData2)hole.GetDefinition();
            if (!d.AccessSelections(doc, null)) return null;
            try
            {
                return d.GetSketchPoints() is object[] points && points.Length > 0 && points[0] is SketchPoint p ? (Feature)p.GetSketch() : null;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }

        /// <summary>Number of holes (points of the position sketch).</summary>
        public static int PointCount(ModelDoc2 doc, Feature hole)
        {
            var d = (WizardHoleFeatureData2)hole.GetDefinition();
            if (!d.AccessSelections(doc, null)) return -1;
            try
            {
                return d.GetSketchPointCount();
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }
    }
}
