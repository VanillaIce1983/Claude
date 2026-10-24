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

        public bool Success => Feature != null && Status != ReportStatus.Failed;

        public static BuildResult Ok(Feature f, string message) => new BuildResult(f, ReportStatus.Ok, message);
        public static BuildResult Failed(string message) => new BuildResult(null, ReportStatus.Failed, message);
        public static BuildResult Skipped(string message) => new BuildResult(null, ReportStatus.Skipped, message);
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
            if (!d.AccessSelections(doc, null)) return false;
            change(d);
            return Finish(f.ModifyDefinition(d, doc, null), d.ReleaseSelectionAccess);
        }

        public static bool Revolve(ModelDoc2 doc, Feature f, Action<RevolveFeatureData2> change)
        {
            var d = (RevolveFeatureData2)f.GetDefinition();
            if (!d.AccessSelections(doc, null)) return false;
            change(d);
            return Finish(f.ModifyDefinition(d, doc, null), d.ReleaseSelectionAccess);
        }

        public static bool Chamfer(ModelDoc2 doc, Feature f, Action<ChamferFeatureData2> change)
        {
            var d = (ChamferFeatureData2)f.GetDefinition();
            if (!d.AccessSelections(doc, null)) return false;
            change(d);
            return Finish(f.ModifyDefinition(d, doc, null), d.ReleaseSelectionAccess);
        }

        private static bool Finish(bool ok, Action release)
        {
            if (!ok) release();
            return ok;
        }
    }
}
