using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Constant-radius fillet. Edges and faces of the original were fingerprinted in the state
    /// the fillet saw them; the mirrored fingerprints are looked up in the rebuilt part, which
    /// at this point is in exactly the mirrored state. Items are selected with mark 1, as
    /// IFeatureManager.FeatureFillet3 expects for the edges/faces of a simple fillet.
    /// </summary>
    internal sealed class FilletBuilder : IFeatureBuilder
    {
        private const int ItemMark = 1;

        public bool Handles(FeatureRecord record) => record is FilletRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (FilletRecord)record;
            var notes = new List<string>();
            var found = new List<(object entity, double radius)>();
            int missing = 0;
            foreach (FilletItem item in r.Items)
            {
                object? e = item.Edge != null
                    ? ctx.Topology.FindEdge(item.Edge.Mirror(ctx.Mirror), notes)
                    : (object?)ctx.Topology.FindFace(item.Face!.Mirror(ctx.Mirror), notes);
                if (e == null) missing++;
                else found.Add((e, item.Radius));
            }
            if (found.Count == 0)
            {
                var failed = BuildResult.Failed("Keine der verrundeten Kanten wurde im Ziel gefunden.");
                failed.Notes.AddRange(notes);
                return failed;
            }
            if (missing > 0) notes.Add($"{missing} von {r.Items.Count} Kanten/Flächen nicht gefunden; Verrundung nur an den übrigen.");

            ModelDoc2 doc = ctx.Doc;
            SwSelect.Clear(doc);
            for (int i = 0; i < found.Count; i++) SwSelect.Select(doc, found[i].entity, ItemMark, i > 0);

            int options = (int)swFeatureFilletOptions_e.swFeatureFilletAttachEdges;
            if (r.Propagate) options |= (int)swFeatureFilletOptions_e.swFeatureFilletPropagate;
            if (!r.MultipleRadius) options |= (int)swFeatureFilletOptions_e.swFeatureFilletUniformRadius;
            if (r.KeepFeatures) options |= (int)swFeatureFilletOptions_e.swFeatureFilletKeepFeatures;
            if (r.RoundCorners) options |= (int)swFeatureFilletOptions_e.swFeatureFilletCornerType;
            object? radii = r.MultipleRadius ? found.Select(x => x.radius).ToArray() : null;

            var f = (Feature)doc.FeatureManager.FeatureFillet3(options, r.Radius, 0, 0,
                (int)swFeatureFilletType_e.swFeatureFilletType_Simple, r.OverflowType,
                (int)swFeatureFilletProfileType_e.swFeatureFilletCircular,
                radii, null, null, null, null, null, null);
            SwSelect.Clear(doc);
            if (f == null)
            {
                var failed = BuildResult.Failed("SOLIDWORKS hat die Verrundung nicht erzeugt.");
                failed.Notes.AddRange(notes);
                return failed;
            }

            ctx.Register(r.Name, f);
            var result = BuildResult.Ok(f, $"{found.Count} Elemente, R = {r.Radius * 1000:0.###} mm" + (r.MultipleRadius ? " (mehrere Radien)" : ""));
            result.Notes.AddRange(notes);
            if (notes.Count > 0) result.Status = ReportStatus.Warning;
            return result;
        }
    }
}
