using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Sw
{
    /// <summary>
    /// Elements of an Advanced Hole: read from, written to and compared with
    /// IAdvancedHoleElementData and the interface of the element type (ICounterboreElementData
    /// ...). The type-specific interfaces do not derive from the common one; the element object
    /// answers to both. Every access is guarded – a property the element does not support may
    /// throw.
    ///
    /// Order when writing: what selects a table entry (standard, fastener, size), then the
    /// switches that decide which values count (overrides, standard depth, fit, end shape),
    /// then the end condition, then the values. A value that only counts with its switch on is
    /// harmless otherwise: SOLIDWORKS takes the standard value.
    /// </summary>
    internal static class SwAdvancedHole
    {
        private sealed class Prop
        {
            public Prop(string name, int? type, Func<object, double> get, Action<object, double> set, Func<AdvancedHoleElement, bool>? applies)
            {
                Name = name;
                Type = type;
                Get = get;
                Set = set;
                Applies = applies;
            }

            public string Name { get; }

            /// <summary>Element type the property belongs to; null: all (common interface).</summary>
            public int? Type { get; }

            public Func<object, double> Get { get; }
            public Action<object, double> Set { get; }

            /// <summary>Whether the value means anything for this element (e.g. a depth only for "blind").</summary>
            public Func<AdvancedHoleElement, bool>? Applies { get; }

            public bool IsFor(AdvancedHoleElement e) => (Type == null || Type == e.ElementType) && (Applies == null || Applies(e));
        }

        private static Prop D<T>(string name, int? type, Func<T, double> get, Action<T, double> set, Func<AdvancedHoleElement, bool>? applies = null) =>
            new Prop(name, type, o => get((T)o), (o, v) => set((T)o, v), applies);

        private static Prop I<T>(string name, int? type, Func<T, int> get, Action<T, int> set, Func<AdvancedHoleElement, bool>? applies = null) =>
            new Prop(name, type, o => get((T)o), (o, v) => set((T)o, (int)Math.Round(v)), applies);

        private static Prop B<T>(string name, int? type, Func<T, bool> get, Action<T, bool> set, Func<AdvancedHoleElement, bool>? applies = null) =>
            new Prop(name, type, o => get((T)o) ? 1 : 0, (o, v) => set((T)o, v > 0.5), applies);

        private const int Cb = AdvancedHoleTypes.CounterBore, Cs = AdvancedHoleTypes.CounterSink, St = AdvancedHoleTypes.Straight,
            Tap = AdvancedHoleTypes.StraightTap, Taper = AdvancedHoleTypes.TaperTap;

        private static bool Blind(AdvancedHoleElement e) => e.End == EndCondition.Blind;
        private static bool Offset(AdvancedHoleElement e) => e.End == EndCondition.OffsetFromSurface;
        private static bool On(AdvancedHoleElement e, string name) => e.Values.TryGetValue(name, out double v) && v > 0.5;

        /// <summary>Switches: written before the end condition and the values.</summary>
        private static readonly Prop[] Switches =
        {
            B<CounterboreElementData>("EndConditionOverride", Cb, d => d.EndConditionOverride, (d, v) => d.EndConditionOverride = v),
            B<CounterboreElementData>("UseStandardDepth", Cb, d => d.UseStandardDepth, (d, v) => d.UseStandardDepth = v),

            B<CountersinkElementData>("EndConditionOverride", Cs, d => d.EndConditionOverride, (d, v) => d.EndConditionOverride = v),
            B<CountersinkElementData>("AngleOverride", Cs, d => d.AngleOverride, (d, v) => d.AngleOverride = v),
            B<CountersinkElementData>("UseStandardDepth", Cs, d => d.UseStandardDepth, (d, v) => d.UseStandardDepth = v),

            I<StraightElementData>("FitType", St, d => d.FitType, (d, v) => d.FitType = v),
            I<StraightElementData>("ClassificationType", St, d => d.ClassificationType, (d, v) => d.ClassificationType = v),
            I<StraightElementData>("Filter", St, d => d.Filter, (d, v) => d.Filter = v),
            I<StraightElementData>("EndShape", St, d => d.EndShape, (d, v) => d.EndShape = v),
            B<StraightElementData>("DrillPointAngleOverride", St, d => d.DrillPointAngleOverride, (d, v) => d.DrillPointAngleOverride = v),

            I<StraightTapElementData>("CustomSizing", Tap, d => d.CustomSizing, (d, v) => d.CustomSizing = v),
            B<StraightTapElementData>("ThreadClassOverride", Tap, d => d.ThreadClassOverride, (d, v) => d.ThreadClassOverride = v),
            I<StraightTapElementData>("EndShape", Tap, d => d.EndShape, (d, v) => d.EndShape = v),
            B<StraightTapElementData>("DrillPointAngleOverride", Tap, d => d.DrillPointAngleOverride, (d, v) => d.DrillPointAngleOverride = v),
            I<StraightTapElementData>("Equation", Tap, d => d.Equation, (d, v) => d.Equation = v),

            I<TaperedTapElementData>("CustomSizing", Taper, d => d.CustomSizing, (d, v) => d.CustomSizing = v),
            B<TaperedTapElementData>("EndConditionOverride", Taper, d => d.EndConditionOverride, (d, v) => d.EndConditionOverride = v),
            B<TaperedTapElementData>("ThreadClassOverride", Taper, d => d.ThreadClassOverride, (d, v) => d.ThreadClassOverride = v),
            B<TaperedTapElementData>("UseStandardDepth", Taper, d => d.UseStandardDepth, (d, v) => d.UseStandardDepth = v),

            B<AdvancedHoleElementData>("DiameterOverride", null, d => d.DiameterOverride, (d, v) => d.DiameterOverride = v),
        };

        /// <summary>Values: written after the end condition.</summary>
        private static readonly Prop[] Values =
        {
            D<AdvancedHoleElementData>("Diameter", null, d => d.Diameter, (d, v) => d.Diameter = v),
            D<AdvancedHoleElementData>("BlindDepth", null, d => d.BlindDepth, (d, v) => d.BlindDepth = v, Blind),
            D<AdvancedHoleElementData>("OffsetDistance", null, d => d.OffsetDistance, (d, v) => d.OffsetDistance = v, Offset),
            B<AdvancedHoleElementData>("OffsetReverseDirection", null, d => d.OffsetReverseDirection, (d, v) => d.OffsetReverseDirection = v, Offset),

            D<CountersinkElementData>("Angle", Cs, d => d.Angle, (d, v) => d.Angle = v),
            D<StraightElementData>("DrillPointAngle", St, d => d.DrillPointAngle, (d, v) => d.DrillPointAngle = v,
                e => On(e, "DrillPointAngleOverride")),
            I<StraightTapElementData>("ThreadClass", Tap, d => d.ThreadClass, (d, v) => d.ThreadClass = v, e => On(e, "ThreadClassOverride")),
            D<StraightTapElementData>("DrillPointAngle", Tap, d => d.DrillPointAngle, (d, v) => d.DrillPointAngle = v,
                e => On(e, "DrillPointAngleOverride")),
            I<TaperedTapElementData>("ThreadClass", Taper, d => d.ThreadClass, (d, v) => d.ThreadClass = v, e => On(e, "ThreadClassOverride")),
        };

        private static readonly string[] TextNames = { "HoleFit", "ShaftFit" };

        private static string? GetText(object element, string name)
        {
            if (!(element is StraightElementData s)) return null;
            return name switch
            {
                "HoleFit" => s.HoleFit,
                "ShaftFit" => s.ShaftFit,
                _ => null,
            };
        }

        private static void SetText(object element, string name, string value)
        {
            if (!(element is StraightElementData s)) return;
            if (name == "HoleFit") s.HoleFit = value;
            else if (name == "ShaftFit") s.ShaftFit = value;
        }

        /// <summary>
        /// One element as it is. <paramref name="toRef"/> turns the end-condition entity into
        /// a reference (called under selection access).
        /// </summary>
        public static AdvancedHoleElement Read(object element, bool farSide, Func<object, GeometryRef?> toRef)
        {
            var common = (AdvancedHoleElementData)element;
            var e = new AdvancedHoleElement(common.ElementType, farSide)
            {
                Standard = common.Standard,
                FastenerType = common.FastenerType,
                Size = common.Size ?? "",
                End = (EndCondition)common.EndCondition,
                Callout = Get(() => common.CalloutString) ?? "",
            };
            if (ExtrudeSide.NeedsReference(e.End) && Get(() => common.SelectionEntity) is object entity)
                e.EndReference = toRef(entity);
            // Switches first: the values of the second table depend on them.
            foreach (Prop p in Switches.Concat(Values))
            {
                if (!p.IsFor(e)) continue;
                double? v = GetValue(() => p.Get(element));
                if (v.HasValue && !double.IsNaN(v.Value)) e.Values[p.Name] = v.Value;
            }
            foreach (string name in TextNames)
                if (Get(() => GetText(element, name)) is string text) e.Texts[name] = text;
            return e;
        }

        /// <summary>
        /// A new element for <see cref="IFeatureManager.AdvancedHole"/>, or null if this
        /// version cannot create the type. <paramref name="endReference"/>: the target's
        /// up-to/offset-from entity.
        /// </summary>
        public static object? Create(ModelDocExtension ext, AdvancedHoleElement wanted, object? endReference, bool callout)
        {
            object? element = Get(() => ext.CreateAdvancedHoleElementData(wanted.ElementType));
            if (element == null) return null;
            Try(() => ((AdvancedHoleElementData)element).Orientation = wanted.FarSide ? 1 : 0);
            Apply(element, wanted, endReference, callout);
            return element;
        }

        /// <summary>Writes everything that differs, in the order the class comment gives; a refused value is skipped.</summary>
        public static void Apply(object element, AdvancedHoleElement wanted, object? endReference, bool callout)
        {
            var common = (AdvancedHoleElementData)element;
            if (GetValue(() => common.Standard) != wanted.Standard) Try(() => common.Standard = wanted.Standard);
            if (GetValue(() => common.FastenerType) != wanted.FastenerType) Try(() => common.FastenerType = wanted.FastenerType);
            if (Get(() => common.Size) != wanted.Size) Try(() => common.Size = wanted.Size);
            Write(element, wanted, Switches);
            if (GetValue(() => common.EndCondition) != (int)wanted.End) Try(() => common.EndCondition = (int)wanted.End);
            if (endReference != null) Try(() => common.SelectionEntity = endReference);
            Write(element, wanted, Values);
            foreach (var kv in wanted.Texts)
                if (Get(() => GetText(element, kv.Key)) != kv.Value) Try(() => SetText(element, kv.Key, kv.Value));
            if (callout && wanted.Callout.Length > 0) Try(() => common.CalloutString = wanted.Callout);
        }

        private static void Write(object element, AdvancedHoleElement wanted, Prop[] props)
        {
            foreach (Prop p in props)
            {
                if (!p.IsFor(wanted) || !wanted.Values.TryGetValue(p.Name, out double v)) continue;
                double? now = GetValue(() => p.Get(element));
                if (now.HasValue && Same(now.Value, v)) continue;
                Try(() => p.Set(element, v));
            }
        }

        /// <summary>What the element has differently from the wanted one (readable values only).</summary>
        public static List<string> Differences(object element, AdvancedHoleElement wanted)
        {
            var result = new List<string>();
            var common = (AdvancedHoleElementData)element;
            if (GetValue(() => common.ElementType) is int type && type != wanted.ElementType)
            {
                result.Add($"Elementtyp {type} statt {wanted.ElementType}");
                return result;
            }
            if (GetValue(() => common.Standard) is int standard && standard != wanted.Standard) result.Add("Standard");
            if (GetValue(() => common.FastenerType) is int fastener && fastener != wanted.FastenerType) result.Add("Befestigungsart");
            if (Get(() => common.Size) is string size && size != wanted.Size) result.Add("Größe");
            if (GetValue(() => common.EndCondition) is int end && end != (int)wanted.End) result.Add("Endbedingung");
            foreach (Prop p in Switches.Concat(Values))
            {
                if (!p.IsFor(wanted) || !wanted.Values.TryGetValue(p.Name, out double v)) continue;
                if (GetValue(() => p.Get(element)) is double now && !double.IsNaN(now) && !Same(now, v)) result.Add(p.Name);
            }
            foreach (var kv in wanted.Texts)
                if (Get(() => GetText(element, kv.Key)) is string now && now != kv.Value) result.Add(kv.Key);
            return result;
        }

        /// <summary>The elements of one side, as the feature data hands them out.</summary>
        public static object[] Elements(AdvancedHoleFeatureData d, bool farSide) =>
            (farSide ? Get(() => d.GetFarSideElements()) : Get(() => d.GetNearSideElements())) as object[] ?? Array.Empty<object>();

        /// <summary>Number of elements of one side as the feature data reports it; null if not readable.</summary>
        public static int? ElementCount(AdvancedHoleFeatureData d, bool farSide) =>
            farSide ? GetValue(() => d.FarSideElementsCount) : GetValue(() => d.NearSideElementsCount);

        /// <summary>
        /// The position sketch of an Advanced Hole: its 2D sub-sketch with the most sketch
        /// points (the hole centres). The data object has no getter for it.
        /// </summary>
        public static Feature? PositionSketch(Feature hole)
        {
            Feature? best = null;
            int most = 0;
            foreach (Feature sub in SwDoc.SubFeatures(hole).Where(x => x.GetTypeName2() == "ProfileFeature"))
            {
                int count = Points(sub).Count;
                if (count <= most) continue;
                best = sub;
                most = count;
            }
            return best;
        }

        /// <summary>The sketch points of a position sketch – one hole each.</summary>
        public static List<SketchPoint> Points(Feature positionSketch) =>
            (Get(() => ((Sketch)positionSketch.GetSpecificFeature2()).GetUserPoints2()) as object[])?.OfType<SketchPoint>().ToList()
            ?? new List<SketchPoint>();

        private static bool Same(double a, double b) => Math.Abs(a - b) <= Math.Max(1e-12, 1e-9 * Math.Max(Math.Abs(a), Math.Abs(b)));

        private static T? Get<T>(Func<T> get) where T : class
        {
            try
            {
                return get();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static T? GetValue<T>(Func<T> get) where T : struct
        {
            try
            {
                return get();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void Try(Action set)
        {
            try
            {
                set();
            }
            catch (Exception)
            {
                // Refused or not supported by this element; Differences reports it.
            }
        }
    }
}
