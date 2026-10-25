using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Draft: neutral plane, parting line and step draft, created with
    /// IFeatureManager.InsertMultiFaceDraft after selecting – as documented – the neutral plane
    /// or direction of pull with mark 1, the faces to draft with 2 and the parting lines with 4.
    ///
    /// The angle tilts the faces relative to the pull direction; mirrored together, nothing
    /// changes there. The pull direction itself is "the reference, or reversed": the copy's
    /// reference (a plane taken with the opposite normal, an edge running the other way) may
    /// point against the mirrored one, so the flag follows σ of the reference direction. Which
    /// side of a parting line is drafted ("other face") depends on how SOLIDWORKS orients the
    /// edge – copied, with the opposite offered to the verifier.
    /// </summary>
    internal sealed class DraftBuilder : IFeatureBuilder
    {
        private const int DirectionMark = 1, FaceMark = 2, EdgeMark = 4;

        public bool Handles(FeatureRecord record) => record is DraftRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (DraftRecord)record;
            var notes = new List<string>();
            bool partingLine = r.Kind != DraftKind.NeutralPlane;

            // Nothing to draft: fail before the direction can add a helper plane or axis.
            var probe = new List<string>();
            if (!partingLine && !r.Faces.Any(fc => ctx.Topology.FindFace(fc.Mirror(ctx.Mirror), probe) != null))
                return Fail("Keine der Flächen der Formschräge wurde im Ziel gefunden.", notes);
            if (partingLine && r.PartingLines.Any(e => ctx.Topology.FindEdge(e.Mirror(ctx.Mirror), probe) == null))
                return Fail("Eine Kante der Trennlinie wurde im Ziel nicht gefunden.", notes);

            object? direction = r.Direction!.Kind == GeometryRefKind.Axis ? PullAxis(ctx, r.Direction.Mirror(ctx.Mirror).Axis, notes) : Find(ctx, r.Direction, notes);
            if (direction == null)
                return Fail(partingLine ? "Zugrichtung im Ziel nicht gefunden." : "Neutrale Ebene im Ziel nicht gefunden.", notes);

            var faces = new List<object>();
            var edges = new List<(object edge, bool otherFace)>();
            if (!partingLine)
            {
                foreach (var face in r.Faces)
                {
                    Face2? found = ctx.Topology.FindFace(face.Mirror(ctx.Mirror), notes);
                    if (found != null) faces.Add(Safe(found)!);
                }
                if (faces.Count == 0) return Fail("Keine der Flächen der Formschräge wurde im Ziel gefunden.", notes);
                if (faces.Count < r.Faces.Count) notes.Add($"{r.Faces.Count - faces.Count} von {r.Faces.Count} Flächen nicht gefunden; Formschräge nur an den übrigen.");
            }
            else
            {
                // A parting line with a gap is no parting line: all of it or nothing.
                for (int i = 0; i < r.PartingLines.Count; i++)
                {
                    Edge? found = ctx.Topology.FindEdge(r.PartingLines[i].Mirror(ctx.Mirror), notes);
                    if (found == null) return Fail("Eine Kante der Trennlinie wurde im Ziel nicht gefunden.", notes);
                    edges.Add((Safe(found)!, r.OtherFace[i]));
                }
            }

            bool flip = FlagForReference(ctx, r.Direction, direction, r.Reverse);

            ModelDoc2 doc = ctx.Doc;
            SwSelect.Clear(doc);
            bool selected = SwSelect.Select(doc, direction, DirectionMark, false);
            foreach (object face in faces) selected &= SwSelect.Select(doc, face, FaceMark, true);
            foreach (var (edge, _) in edges) selected &= SwSelect.Select(doc, edge, EdgeMark, true);
            Feature? f = selected
                ? doc.FeatureManager.InsertMultiFaceDraft(r.Angle, flip, partingLine, r.Propagation, r.Kind == DraftKind.Step, false)
                : null;
            SwSelect.Clear(doc);
            if (f == null) return Fail("SOLIDWORKS hat die Formschräge nicht erzeugt.", notes);

            bool needsEdit = r.AllowReducedAngle || (r.Kind == DraftKind.Step && r.StepType != 0) || edges.Any(e => e.otherFace);
            if (needsEdit)
            {
                bool ok = FeatureEdit.Draft(doc, f, d =>
                {
                    if (r.AllowReducedAngle) d.AllowReducedAngle = true;
                    if (r.Kind == DraftKind.Step && r.StepType != 0) d.StepType = (short)r.StepType;
                    for (int i = 0; i < edges.Count; i++)
                        if (d.GetOtherFacesFlagAtIndex((short)i) != edges[i].otherFace) d.SetOtherFacesFlagAtIndex((short)i, edges[i].otherFace);
                });
                if (!ok) notes.Add("Stufenart, reduzierter Winkel oder Seiten der Trennlinie ließen sich nicht setzen.");
            }

            ctx.Register(r.Name, f);
            BuildResult result = Ok(f, Describe(r, flip != r.Reverse), notes);
            result.Alternatives.Add(new Alternative("Zugrichtung umgekehrt",
                () => FeatureEdit.Draft(doc, f, d => d.ReverseDirection = !d.ReverseDirection)));
            if (partingLine)
                result.Alternatives.Add(new Alternative("Andere Seite der Trennlinie", () => FeatureEdit.Draft(doc, f, d =>
                {
                    for (short i = 0; i < d.GetPartingLinesCount(); i++) d.SetOtherFacesFlagAtIndex(i, !d.GetOtherFacesFlagAtIndex(i));
                })));
            return result;
        }

        /// <summary>
        /// A pull direction along a line: an axis of this job, a straight edge (in the same part
        /// only of the mirrored bodies) or a helper axis – not a sketch line, which SOLIDWORKS
        /// does not offer as a direction of pull. Faces found later come after any rebuild.
        /// </summary>
        private static object? PullAxis(BuildContext ctx, Line3 line, List<string> notes)
        {
            if (ctx.Planes.FindAxis(line) is Feature axis) return axis;
            if (ctx.Topology.FindLinearEdgeOn(line, ctx.SamePart) is Edge edge) return Safe(edge);
            try
            {
                return ctx.Planes.Axis(line);
            }
            catch (InvalidOperationException ex)
            {
                notes.Add(ex.Message);
                return null;
            }
        }

        private static string Describe(DraftRecord r, bool flipped)
        {
            string kind = r.Kind switch
            {
                DraftKind.NeutralPlane => $"neutrale Ebene, {r.Faces.Count} Fläche(n)",
                DraftKind.PartingLine => $"Trennlinie, {r.PartingLines.Count} Kante(n)",
                _ => $"Stufe, {r.PartingLines.Count} Kante(n)",
            };
            return $"Formschräge {r.Angle * 180 / Math.PI:0.##}°, {kind}" + (flipped ? ", Zugrichtung umgekehrt (Bezug entgegengesetzt)" : "");
        }
    }
}
