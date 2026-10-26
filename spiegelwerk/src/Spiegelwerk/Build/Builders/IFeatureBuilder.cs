using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Records;

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
        public Alternative(string description, Func<bool> toggle)
        {
            Description = description;
            Toggle = toggle;
        }

        public string Description { get; }
        public Func<bool> Toggle { get; }
    }

    internal sealed class BuildResult
    {
        private BuildResult(Feature? feature, ReportStatus status, string message)
        {
            Feature = feature;
            Status = status;
            Message = message;
        }

        public Feature? Feature { get; }
        public ReportStatus Status { get; set; }
        public string Message { get; set; }
        public List<string> Notes { get; } = new List<string>();
        public List<Alternative> Alternatives { get; } = new List<Alternative>();

        /// <summary>Removes helpers made for this feature alone – called if the job removes the feature again.</summary>
        public Action? RemoveHelpers { get; set; }

        public bool Success => Feature != null && Status != ReportStatus.Failed;

        public static BuildResult Ok(Feature f, string message) => new BuildResult(f, ReportStatus.Ok, message);
        public static BuildResult Failed(string message) => new BuildResult(null, ReportStatus.Failed, message);
        public static BuildResult Skipped(string message) => new BuildResult(null, ReportStatus.Skipped, message);

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
            var d = (ExtrudeFeatureData2)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool Revolve(ModelDoc2 doc, Feature f, Action<RevolveFeatureData2> change)
        {
            var d = (RevolveFeatureData2)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool Chamfer(ModelDoc2 doc, Feature f, Action<ChamferFeatureData2> change)
        {
            var d = (ChamferFeatureData2)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool Sweep(ModelDoc2 doc, Feature f, Action<SweepFeatureData> change)
        {
            var d = (SweepFeatureData)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool Loft(ModelDoc2 doc, Feature f, Action<LoftFeatureData> change)
        {
            var d = (LoftFeatureData)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool SimpleFillet(ModelDoc2 doc, Feature f, Action<SimpleFilletFeatureData2> change)
        {
            var d = (SimpleFilletFeatureData2)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool VariableFillet(ModelDoc2 doc, Feature f, Action<VariableFilletFeatureData2> change)
        {
            var d = (VariableFilletFeatureData2)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool Shell(ModelDoc2 doc, Feature f, Action<ShellFeatureData> change)
        {
            var d = (ShellFeatureData)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool Draft(ModelDoc2 doc, Feature f, Action<DraftFeatureData2> change)
        {
            var d = (DraftFeatureData2)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool Hole(ModelDoc2 doc, Feature f, Action<WizardHoleFeatureData2> change)
        {
            var d = (WizardHoleFeatureData2)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool AdvancedHole(ModelDoc2 doc, Feature f, Action<AdvancedHoleFeatureData> change)
        {
            var d = (AdvancedHoleFeatureData)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool TablePattern(ModelDoc2 doc, Feature f, Action<TablePatternFeatureData> change)
        {
            var d = (TablePatternFeatureData)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool CoordinateSystem(ModelDoc2 doc, Feature f, Action<ICoordinateSystemFeatureData> change)
        {
            var d = (ICoordinateSystemFeatureData)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool CurvePattern(ModelDoc2 doc, Feature f, Action<CurveDrivenPatternFeatureData> change)
        {
            var d = (CurveDrivenPatternFeatureData)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool SketchPattern(ModelDoc2 doc, Feature f, Action<SketchPatternFeatureData> change)
        {
            var d = (SketchPatternFeatureData)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool FillPattern(ModelDoc2 doc, Feature f, Action<FillPatternFeatureData> change)
        {
            var d = (FillPatternFeatureData)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool VariablePattern(ModelDoc2 doc, Feature f, Action<DimPatternFeatureData> change)
        {
            var d = (DimPatternFeatureData)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool LinearPattern(ModelDoc2 doc, Feature f, Action<LinearPatternFeatureData> change)
        {
            var d = (LinearPatternFeatureData)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
        }

        public static bool CircularPattern(ModelDoc2 doc, Feature f, Action<CircularPatternFeatureData> change)
        {
            var d = (CircularPatternFeatureData)f.GetDefinition();
            return Edit(doc, f, d, () => d.AccessSelections(doc, null), () => change(d), d.ReleaseSelectionAccess);
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
