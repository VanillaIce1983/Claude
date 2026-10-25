using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
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

            Feature? f;
            if (removed.Count > 0)
            {
                f = Create(doc, removed, r);
            }
            else
            {
                // Closed shell: exactly the body it hollowed, never "whatever SOLIDWORKS takes".
                List<Body2> candidates = ctx.SeparateBody ? ctx.OwnBodies() : SwDoc.SolidBodies(doc);
                if (candidates.Count == 0)
                    return Fail("Es gibt noch keinen gespiegelten Körper, den die Wandung aushöhlen könnte; das Original bleibt unberührt.", notes);
                Body2? body = ShelledBody(ctx, r, candidates);
                if (body == null)
                    return Fail("Geschlossene Wandung: der ausgehöhlte Körper ist im Ziel nicht eindeutig.", notes);
                f = Create(doc, new List<object> { body }, r);
                // The only body there is: also without selection, as in the user interface.
                if (f == null && SwDoc.SolidBodies(doc).Count == 1) f = Create(doc, new List<object>(), r);
            }
            if (f == null) return Fail("SOLIDWORKS hat die Wandung nicht erzeugt.", notes);

            bool needsEdit = multi.Count > 0 || Direction(doc, f) != r.Direction;
            int appliedMulti = 0;
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
                appliedMulti = ok ? multi.Count : 0;
            }

            ctx.Register(r.Name, f);
            BuildResult result = Ok(f, $"Wandung {r.Thickness * 1000:0.###} mm, {r.RemovedFaces.Count} offene Fläche(n)" +
                                       (appliedMulti > 0 ? $", {appliedMulti} mit eigener Stärke" : ""), notes);
            if (r.Direction == 0 || r.Direction == 1)
                result.Alternatives.Add(new Alternative("Wandung nach außen/innen gewechselt",
                    () => FeatureEdit.Shell(doc, f, d => d.Direction = 1 - d.Direction)));
            return result;
        }

        /// <summary>
        /// The body a closed shell hollows: the candidate with a face through the mirrored
        /// sample point of the captured face; if two touch there (stacked blocks), the one whose
        /// face also matches type, area and normal. Without a captured face only a single
        /// candidate is taken. Null if it is not clear – then no shell rather than the wrong body.
        /// </summary>
        private static Body2? ShelledBody(BuildContext ctx, ShellRecord r, List<Body2> candidates)
        {
            if (r.BodyFace == null) return candidates.Count == 1 ? candidates[0] : null;
            FaceSignature wanted = r.BodyFace.Mirror(ctx.Mirror);
            var hits = candidates
                .Select(b => (body: b, faces: ((b.GetFaces() as object[]) ?? Array.Empty<object>()).OfType<Face2>()
                    .Where(face => TopologyCapture.DistanceToFace(face, wanted.Sample) <= Tol.Length).ToList()))
                .Where(h => h.faces.Count > 0)
                .ToList();
            if (hits.Count == 1) return hits[0].body;
            var exact = hits.Where(h => h.faces.Any(face => wanted.CouldBe(TopologyCapture.Face(face)))).ToList();
            return exact.Count == 1 ? exact[0].body : null;
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
