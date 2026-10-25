using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Shell. Wall thickness, inside/outside and the open faces are relative to the body and
    /// survive a mirror unchanged; only the faces have to be found again. The open faces are
    /// selected with mark 1 (0 as fallback) for IModelDoc2.InsertFeatureShell; the faces with
    /// a thickness of their own and the direction are set afterwards on the feature data,
    /// so nothing depends on the less documented InsertFeatureShellAddThickness.
    /// </summary>
    internal sealed class ShellBuilder : IFeatureBuilder
    {
        private static readonly int[] Marks = { 1, 0 };

        public bool Handles(FeatureRecord record) => record is ShellRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (ShellRecord)record;
            var notes = new List<string>();
            ModelDoc2 doc = ctx.Doc;

            // An open face that is missing would leave the copy closed – a different part.
            var removed = new List<object>();
            foreach (var face in r.RemovedFaces)
            {
                Face2? found = ctx.Topology.FindFace(face.Mirror(ctx.Mirror), notes);
                if (found == null) return Fail("Eine offene Fläche der Wandung wurde im Ziel nicht gefunden.", notes);
                removed.Add(found);
            }
            var multi = new List<(object face, double thickness)>();
            foreach (FaceThickness ft in r.MultiThickness)
            {
                Face2? found = ctx.Topology.FindFace(ft.Face.Mirror(ctx.Mirror), notes);
                if (found != null) multi.Add((Safe(found)!, ft.Thickness));
                else notes.Add($"Fläche mit eigener Wandstärke ({ft.Thickness * 1000:0.###} mm) nicht gefunden; dort gilt die Standardstärke.");
            }

            // A closed shell has nothing selected; in the same part it has to be pointed at the
            // mirrored body, or it might hollow out the original as well.
            List<object> targets = removed.Count > 0 ? removed
                : ctx.SeparateBody ? ctx.OwnBodies().Cast<object>().ToList()
                : new List<object>();
            Feature? f = Create(doc, targets, r);
            if (f == null && removed.Count == 0 && targets.Count == 0)
                f = Create(doc, SwDoc.SolidBodies(doc).Cast<object>().ToList(), r);
            if (f == null) return Fail("SOLIDWORKS hat die Wandung nicht erzeugt.", notes);

            bool needsEdit = multi.Count > 0 || Direction(doc, f) != r.Direction;
            if (needsEdit)
            {
                bool ok = FeatureEdit.Shell(doc, f, d =>
                {
                    if (d.Direction != r.Direction) d.Direction = r.Direction;
                    if (multi.Count > 0)
                    {
                        d.MultipleThicknessFaces = multi.Select(m => m.face).ToArray();
                        for (int i = 0; i < multi.Count; i++) d.SetMultipleThicknessAtIndex(i, multi[i].thickness);
                    }
                });
                if (!ok) notes.Add("Richtung oder Flächen mit eigener Wandstärke ließen sich nicht setzen.");
            }

            ctx.Register(r.Name, f);
            BuildResult result = Ok(f, $"Wandung {r.Thickness * 1000:0.###} mm, {r.RemovedFaces.Count} offene Fläche(n)" +
                                       (r.MultiThickness.Count > 0 ? $", {r.MultiThickness.Count} mit eigener Stärke" : ""), notes);
            if (r.Direction == 0 || r.Direction == 1)
                result.Alternatives.Add(new Alternative("Wandung nach außen/innen gewechselt",
                    () => FeatureEdit.Shell(doc, f, d => d.Direction = 1 - d.Direction)));
            return result;
        }

        /// <summary>
        /// InsertFeatureShell returns nothing: the new feature is the last one added, if that
        /// is a shell that was not there before.
        /// </summary>
        private static Feature? Create(ModelDoc2 doc, List<object> selection, ShellRecord r)
        {
            string before = doc.Extension.GetLastFeatureAdded()?.Name ?? "";
            foreach (int mark in Marks)
            {
                SwSelect.Clear(doc);
                bool selected = true;
                for (int i = 0; i < selection.Count; i++) selected &= SwSelect.Select(doc, selection[i], mark, i > 0);
                if (!selected) continue;
                doc.InsertFeatureShell(r.Thickness, r.Direction != 0);
                SwSelect.Clear(doc);
                Feature? last = doc.Extension.GetLastFeatureAdded();
                if (last != null && last.Name != before && last.GetTypeName2() == "Shell") return last;
                if (selection.Count == 0) break;
            }
            return null;
        }

        private static int Direction(ModelDoc2 doc, Feature f)
        {
            var d = (ShellFeatureData)f.GetDefinition();
            if (!d.AccessSelections(doc, null)) return int.MinValue;
            try
            {
                return d.Direction;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
        }
    }
}
