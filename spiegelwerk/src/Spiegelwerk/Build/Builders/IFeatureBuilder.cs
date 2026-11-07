using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>Rebuilds one kind of feature in the target.</summary>
    internal interface IFeatureBuilder
    {
        bool Handles(FeatureRecord record);

        BuildResult Build(FeatureRecord record, BuildContext ctx);
    }

    /// <summary>
    /// A variant of a just-built feature: a toggle of a flag whose correct value cannot be
    /// derived with certainty (side of a thin wall, revolve start side ...). Applying it twice
    /// restores the feature. The verifier tries these when the mass check fails.
    /// </summary>
    internal sealed class Alternative
    {
        /// <param name="parts">
        /// The single changes this toggle makes, if it makes several that other alternatives
        /// also make on their own: the report names the changes left in the end, and two kept
        /// toggles of the same change cancel out. Default: the description.
        /// </param>
        public Alternative(string description, Func<bool> toggle, IReadOnlyList<string>? parts = null)
        {
            Description = description;
            Toggle = toggle;
            Parts = parts ?? new[] { description };
        }

        public string Description { get; }
        public Func<bool> Toggle { get; }
        public IReadOnlyList<string> Parts { get; }
    }

    /// <summary>
    /// A feature SOLIDWORKS made together with the step's feature (the SheetMetal of a base
    /// flange): it belongs to the step – rebuild errors, removal, own faces – and gets the name
    /// and dimension names of its original, but it is no group member.
    /// </summary>
    internal sealed class Companion
    {
        public Companion(Feature feature, string originalName, List<NamedDimension> dimensions)
        {
            Feature = feature;
            OriginalName = originalName;
            Dimensions = dimensions;
        }

        public Feature Feature { get; }
        public string OriginalName { get; }

        /// <summary>The original's own dimensions (thickness, radius) for copied equations.</summary>
        public List<NamedDimension> Dimensions { get; }
    }

    internal sealed class BuildResult
    {
        private BuildResult(Feature? feature, ReportStatus status, string message)
        {
            Feature = feature;
            Status = status;
            Message = message;
        }

        /// <summary>The new feature; for the second half of a mirror feature the last of its features.</summary>
        public Feature? Feature { get; }

        /// <summary>The features of a mirror feature's second half, in tree order; null for a single feature.</summary>
        public IReadOnlyList<Feature>? Group { get; private set; }

        /// <summary>Every feature this step made: the group, or the single feature with its companions (none if it failed).</summary>
        public IReadOnlyList<Feature> AllFeatures => Group ?? (Feature == null ? Array.Empty<Feature>() : new[] { Feature }.Concat(Companions.Select(c => c.Feature)).ToList());

        /// <summary>Features made together with <see cref="Feature"/> (K22).</summary>
        public List<Companion> Companions { get; } = new List<Companion>();

        /// <summary>Report details that are information only – they do not make the step a warning.</summary>
        public List<string> Info { get; } = new List<string>();

        /// <summary>The builder's own check, for a feature the verifier does not measure (the flat state of a flat pattern).</summary>
        public string? Verification { get; set; }

        /// <summary>Report entries of the steps of a second half, named under the mirror feature; they follow its own entry.</summary>
        public List<ReportEntry> ChildEntries { get; } = new List<ReportEntry>();

        public ReportStatus Status { get; set; }
        public string Message { get; set; }
        public List<string> Notes { get; } = new List<string>();
        public List<Alternative> Alternatives { get; } = new List<Alternative>();

        /// <summary>A possible reason the builder knows of, added to the report only if the check finds a deviation.</summary>
        public string? MismatchHint { get; set; }

        /// <summary>What a passing check confirms besides volume and shape (a hem's default relief), added to its text; nothing if it does not pass.</summary>
        public string? PassNote { get; set; }

        /// <summary>Removes helpers made for this feature alone – called if the job removes the feature again.</summary>
        public Action? RemoveHelpers { get; set; }

        public bool Success => Feature != null && Status != ReportStatus.Failed;

        public static BuildResult Ok(Feature f, string message) => new BuildResult(f, ReportStatus.Ok, message);
        public static BuildResult Failed(string message) => new BuildResult(null, ReportStatus.Failed, message);
        public static BuildResult Skipped(string message) => new BuildResult(null, ReportStatus.Skipped, message);

        /// <summary>The second half of a mirror feature: several features, the last one stands for them.</summary>
        public static BuildResult OfGroup(IReadOnlyList<Feature> group, ReportStatus status, string message) =>
            new BuildResult(group[group.Count - 1], status, message) { Group = group };

        /// <summary>Nothing to build because the target already has the equivalent.</summary>
        public static BuildResult AlreadyThere(string message) => new BuildResult(null, ReportStatus.Skipped, message) { HasEquivalent = true };

        public bool HasEquivalent { get; private set; }
    }

    /// <summary>
    /// Changing a feature after creation: GetDefinition → AccessSelections → change →
    /// ModifyDefinition. If ModifyDefinition fails the selection access has to be released
    /// explicitly, otherwise the model stays rolled back.
    /// </summary>
    internal static class FeatureEdit
    {
        public static bool Extrude(ModelDoc2 doc, Feature f, Action<ExtrudeFeatureData2> change)
        {
            var d = (ExtrudeFeatureData2)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool Revolve(ModelDoc2 doc, Feature f, Action<RevolveFeatureData2> change)
        {
            var d = (RevolveFeatureData2)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool Chamfer(ModelDoc2 doc, Feature f, Action<ChamferFeatureData2> change)
        {
            var d = (ChamferFeatureData2)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool Sweep(ModelDoc2 doc, Feature f, Action<SweepFeatureData> change)
        {
            var d = (SweepFeatureData)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool Loft(ModelDoc2 doc, Feature f, Action<LoftFeatureData> change)
        {
            var d = (LoftFeatureData)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool SimpleFillet(ModelDoc2 doc, Feature f, Action<SimpleFilletFeatureData2> change)
        {
            var d = (SimpleFilletFeatureData2)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool VariableFillet(ModelDoc2 doc, Feature f, Action<VariableFilletFeatureData2> change)
        {
            var d = (VariableFilletFeatureData2)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool Shell(ModelDoc2 doc, Feature f, Action<ShellFeatureData> change)
        {
            var d = (ShellFeatureData)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool Draft(ModelDoc2 doc, Feature f, Action<DraftFeatureData2> change)
        {
            var d = (DraftFeatureData2)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool Hole(ModelDoc2 doc, Feature f, Action<WizardHoleFeatureData2> change)
        {
            var d = (WizardHoleFeatureData2)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool AdvancedHole(ModelDoc2 doc, Feature f, Action<AdvancedHoleFeatureData> change)
        {
            var d = (AdvancedHoleFeatureData)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool TablePattern(ModelDoc2 doc, Feature f, Action<TablePatternFeatureData> change)
        {
            var d = (TablePatternFeatureData)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool CoordinateSystem(ModelDoc2 doc, Feature f, Action<ICoordinateSystemFeatureData> change)
        {
            var d = (ICoordinateSystemFeatureData)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool CurvePattern(ModelDoc2 doc, Feature f, Action<CurveDrivenPatternFeatureData> change)
        {
            var d = (CurveDrivenPatternFeatureData)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool SketchPattern(ModelDoc2 doc, Feature f, Action<SketchPatternFeatureData> change)
        {
            var d = (SketchPatternFeatureData)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool FillPattern(ModelDoc2 doc, Feature f, Action<FillPatternFeatureData> change)
        {
            var d = (FillPatternFeatureData)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool VariablePattern(ModelDoc2 doc, Feature f, Action<DimPatternFeatureData> change)
        {
            var d = (DimPatternFeatureData)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool LinearPattern(ModelDoc2 doc, Feature f, Action<LinearPatternFeatureData> change)
        {
            var d = (LinearPatternFeatureData)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool CircularPattern(ModelDoc2 doc, Feature f, Action<CircularPatternFeatureData> change)
        {
            var d = (CircularPatternFeatureData)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool BaseFlange(ModelDoc2 doc, Feature f, Action<BaseFlangeFeatureData> change)
        {
            var d = (BaseFlangeFeatureData)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool FlatPattern(ModelDoc2 doc, Feature f, Action<FlatPatternFeatureData> change)
        {
            var d = (FlatPatternFeatureData)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool Hem(ModelDoc2 doc, Feature f, Action<HemFeatureData> change)
        {
            var d = (HemFeatureData)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool MiterFlange(ModelDoc2 doc, Feature f, Action<MiterFlangeFeatureData> change)
        {
            var d = (MiterFlangeFeatureData)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool EdgeFlange(ModelDoc2 doc, Feature f, Action<EdgeFlangeFeatureData> change)
        {
            var d = (EdgeFlangeFeatureData)(object)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        /// <summary>
        /// A feature's own bend allowance in an edit of its own (blech-plan §1.4, A2 P7) – never
        /// together with the geometric values, so a refused allowance costs only itself. Order of
        /// <see cref="AllowanceRules.WriteOrder"/>: UseDefaultBendAllowance = false first (otherwise
        /// the values silently revert), the value of its kind, Type last, SetCustomBendAllowance;
        /// null: back to the default. Typed for hem, miter and edge flange; any other sheet-metal data
        /// late-bound (the same members, static on every bend data interface since 2019). Only the
        /// read-back tells whether it took.
        /// </summary>
        public static bool Allowance(ModelDoc2 doc, Feature f, BendAllowanceData? own)
        {
            object? def = (object)f.GetDefinition();
            switch (def)
            {
                case HemFeatureData h:
                    return Edit(doc, f, h, () => h.AccessSelections(doc, null),
                        () => WriteAllowance(own, v => h.UseDefaultBendAllowance = v, () => (object)h.GetCustomBendAllowance(), h.SetCustomBendAllowance),
                        h.ReleaseSelectionAccess);
                case MiterFlangeFeatureData m:
                    return Edit(doc, f, m, () => m.AccessSelections(doc, null),
                        () => WriteAllowance(own, v => m.UseDefaultBendAllowance = v, () => (object)m.GetCustomBendAllowance(), m.SetCustomBendAllowance),
                        m.ReleaseSelectionAccess);
                case EdgeFlangeFeatureData e:
                    return Edit(doc, f, e, () => e.AccessSelections(doc, null),
                        () => WriteAllowance(own, v => e.UseDefaultBendAllowance = v, () => (object)e.GetCustomBendAllowance(), e.SetCustomBendAllowance),
                        e.ReleaseSelectionAccess);
                case null:
                    return false;
                default:
                    // VBA's "Nothing" for the component: a null dispatch pointer.
                    var nothing = new System.Runtime.InteropServices.DispatchWrapper(null);
                    return Edit(doc, f, def, () => SwLate.Call(def, "AccessSelections", doc, nothing),
                        () => WriteAllowance(own, v =>
                            {
                                if (!SwLate.Set(def, "UseDefaultBendAllowance", v)) throw new InvalidOperationException("UseDefaultBendAllowance nicht setzbar.");
                            },
                            () => SwLate.Invoke(def, "GetCustomBendAllowance"),
                            c =>
                            {
                                if (!SwLate.Call(def, "SetCustomBendAllowance", c)) throw new InvalidOperationException("SetCustomBendAllowance nicht aufrufbar.");
                            }),
                        () => SwLate.Call(def, "ReleaseSelectionAccess"));
            }
        }

        /// <summary>
        /// The allowance into an edge flange's data object before CreateFeature (blech-plan §1.4: at
        /// creation where the call takes it) – the same writes as <see cref="Allowance"/>, without an edit.
        /// </summary>
        public static void AllowanceInto(EdgeFlangeFeatureData d, BendAllowanceData? own) =>
            WriteAllowance(own, v => d.UseDefaultBendAllowance = v, () => (object)d.GetCustomBendAllowance(), d.SetCustomBendAllowance);

        private static void WriteAllowance(BendAllowanceData? own, Action<bool> useDefault, Func<object?> get, Action<CustomBendAllowance> set)
        {
            if (own == null)
            {
                useDefault(true);
                return;
            }
            useDefault(false);
            if (!(get() is CustomBendAllowance cba)) throw new InvalidOperationException("Biegezugabe-Objekt nicht lesbar.");
            // The value of its kind, Type last.
            SwSheetMetal.Fill(cba, own);
            set(cba);
        }

        /// <summary>A setter that throws (stale entity, invalid value) must not leave the model rolled back either.</summary>
        private static bool Edit(ModelDoc2 doc, Feature f, object data, Func<bool> access, Action change, Action release)
        {
            if (!access()) return false;
            bool ok = false;
            try
            {
                change();
                ok = f.ModifyDefinition(data, doc, null);
                return ok;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (!ok) release();
            }
        }
    }
}
