using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
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
    /// Advanced Hole, in four steps:
    ///  1. Create one hole: the near-side face selected at the first mirrored hole centre, the
    ///     far-side face after it if there are far-side elements, the elements made with
    ///     CreateAdvancedHoleElementData and filled in, IFeatureManager.AdvancedHole – the
    ///     documented way. Which selection marks the call expects is not documented, and
    ///     whether it takes an object array or wrapped dispatch pointers neither: the variants
    ///     are tried in turn. A hole whose position sketch lies on the far-side face took the
    ///     faces the wrong way round: it is removed again, and the same variant is tried with
    ///     the faces exchanged (marks and order).
    ///  2. Write the mirrored position sketch into the position sketch of the new hole
    ///     (SketchBuilder.BuildInto), as for a Hole Wizard hole.
    ///  3. Compare the elements with the original and correct what differs in one edit, with
    ///     the kind of array the creation call accepted.
    ///  4. Count the holes.
    ///
    /// No direction flag: the elements run from their face into the material, and a
    /// reflection keeps outward normals outward. No feature scope either – the API has none
    /// for this feature; in "separate body" mode the job's guard removes a hole that would
    /// have reached the original.
    /// </summary>
    internal sealed class AdvancedHoleBuilder : IFeatureBuilder
    {
        /// <summary>A way to select the faces: mark of each, and whether the far-side face goes first.</summary>
        private readonly struct Selection
        {
            public Selection(int near, int far, bool farFirst)
            {
                Near = near;
                Far = far;
                FarFirst = farFirst;
            }

            public int Near { get; }
            public int Far { get; }
            public bool FarFirst { get; }

            /// <summary>The faces the other way round – for a hole that came out with the sides swapped.</summary>
            public Selection Exchanged => new Selection(Far, Near, !FarFirst);
        }

        /// <summary>Selections tried in turn; without far-side elements only the near mark counts.</summary>
        private static readonly Selection[] Selections = { new Selection(0, 1, false), new Selection(1, 2, false), new Selection(0, 0, false) };

        public bool Handles(FeatureRecord record) => record is AdvancedHoleRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (AdvancedHoleRecord)record;
            var notes = new List<string>();

            // A hole is a cut: in "separate body" mode it needs a mirrored body to act on.
            string? refused = PlanSeparateBody(ctx, true, false, out bool _, out bool _);
            if (refused != null) return Fail(refused, notes);

            // Faces only – no plane through a face that is missing: the hole needs the face itself.
            if (!(FindFace(ctx, r.Face, notes) is Face2 face))
                return Fail("Nahseitige Fläche der erweiterten Bohrung im Ziel nicht gefunden.", notes);
            Face2? farFace = null;
            if (r.Far.Count > 0)
            {
                farFace = FindFace(ctx, r.FarFace, notes);
                if (farFace == null) return Fail("Fernseitige Fläche der erweiterten Bohrung im Ziel nicht gefunden.", notes);
            }
            var references = new Dictionary<AdvancedHoleElement, object>();
            foreach (AdvancedHoleElement e in r.Near.Concat(r.Far).Where(x => ExtrudeSide.NeedsReference(x.End)))
            {
                object? target = Find(ctx, e.EndReference, notes);
                if (target == null) return Fail($"Bezug der Endbedingung {e.End} ({e}) im Ziel nicht gefunden.", notes);
                references[e] = target;
            }
            List<Vec3> centres = r.Points.Select(ctx.Mirror.Apply).ToList();

            // ---- 1. one hole
            Feature? f = Create(ctx, r, face, farFace, centres, references, notes, out Feature? position, out bool wrapped);
            if (f == null || position == null) return Fail("SOLIDWORKS hat die erweiterte Bohrung nicht erzeugt.", notes);

            // ---- 2. the position sketch
            try
            {
                new SketchBuilder(ctx).BuildInto(r.Position, position, notes);
            }
            catch (Exception ex)
            {
                Delete(ctx, f, true);
                ctx.Sketches.Remove(r.PositionSketchName);
                return Fail($"Positionsskizze ließ sich nicht übertragen ({ex.Message}); die Bohrung wurde wieder entfernt.", notes);
            }
            ctx.Register(r.PositionSketchName, position);

            // ---- 3. elements
            List<string> differing = Differences(ctx.Doc, f, r);
            if (differing.Count > 0)
            {
                bool ok = FeatureEdit.AdvancedHole(ctx.Doc, f, d => Correct(ctx.Doc, d, r, references, wrapped));
                differing = Differences(ctx.Doc, f, r);
                if (!ok && differing.Count > 0) notes.Add("Die Elemente ließen sich nachträglich nicht korrigieren.");
            }
            if (differing.Count > 0)
                notes.Add($"Nicht übernommen: {string.Join("; ", differing)}.");

            // ---- 4. count
            int count = SwAdvancedHole.Points(position).Count;
            if (count != r.Points.Count)
                notes.Add($"Die Kopie hat {count} statt {r.Points.Count} Bohrungen.");

            ctx.Register(r.Name, f);
            return Ok(f, $"{r.Points.Count} × {Describe(r)}", notes);
        }

        /// <summary>A face of the target by the mirrored fingerprint, as a safe entity; null if not found.</summary>
        private static Face2? FindFace(BuildContext ctx, GeometryRef? original, List<string> notes) =>
            original?.Face is FaceSignature sig ? Safe(ctx.Topology.FindFace(sig.Mirror(ctx.Mirror), notes)) as Face2 : null;

        /// <summary>
        /// The hole with one point and its position sketch, or null. Tries the selections and
        /// both kinds of array (<paramref name="wrapped"/>: the one that worked); a hole whose
        /// position sketch lies on the far-side face took the faces the wrong way round, is
        /// removed again, and the exchanged selection is tried next.
        /// </summary>
        private static Feature? Create(BuildContext ctx, AdvancedHoleRecord r, Face2 face, Face2? farFace, List<Vec3> centres,
            Dictionary<AdvancedHoleElement, object> references, List<string> notes, out Feature? position, out bool wrapped)
        {
            position = null;
            wrapped = false;
            ModelDoc2 doc = ctx.Doc;
            Plane3? farPlane = farFace == null ? null : TopologyCapture.PlaneOfFace(farFace);
            Vec3? first = centres.Count > 0 ? centres[0] : (Vec3?)null;
            bool swapped = false;
            var queue = new List<Selection>(Selections);
            var tried = new HashSet<(int, int, bool)>();
            for (int q = 0; q < queue.Count; q++)
            {
                Selection sel = queue[q];
                if (!tried.Add(farFace == null ? (sel.Near, -1, false) : (sel.Near, sel.Far, sel.FarFirst))) continue;
                foreach (bool asWrapped in new[] { false, true })
                {
                    object? near = Elements(doc, r.Near, references, r.CustomizeCallout, asWrapped);
                    object? far = r.Far.Count == 0 ? null : Elements(doc, r.Far, references, r.CustomizeCallout, asWrapped);
                    if (near == null || (r.Far.Count > 0 && far == null))
                    {
                        notes.Add("Ein Element der Bohrung lässt sich in dieser SOLIDWORKS-Version nicht anlegen.");
                        return null;
                    }

                    Feature? f = null;
                    try
                    {
                        if (!Select(ctx, face, farFace, first, sel)) continue;
                        f = doc.FeatureManager.AdvancedHole(near, far, r.UseBaselineDimensions, r.CustomizeCallout, out object _);
                    }
                    catch (Exception)
                    {
                        // Refused in this form – the next one follows.
                    }
                    finally
                    {
                        SwSelect.Clear(doc);
                    }
                    if (f == null) continue;

                    ctx.Topology.Invalidate();
                    Feature? sketch = SwAdvancedHole.PositionSketch(f);
                    if (sketch == null)
                    {
                        Delete(ctx, f, true);
                        notes.Add("Positionsskizze der neuen Bohrung nicht gefunden; die Bohrung wurde wieder entfernt.");
                        return null;
                    }
                    if (farPlane != null && OnPlane(ctx, sketch, farPlane.Value))
                    {
                        swapped = true;
                        Delete(ctx, f, true);
                        ctx.Topology.Invalidate();
                        // Next: the same selection with the faces exchanged.
                        queue.Insert(q + 1, sel.Exchanged);
                        break;
                    }
                    position = sketch;
                    wrapped = asWrapped;
                    return f;
                }
            }
            if (swapped) notes.Add("Die Bohrung entstand nur mit vertauschter Nah- und Fernseite und wurde wieder entfernt.");
            return null;
        }

        /// <summary>
        /// Near-side face at the first hole centre (the first hole goes where the face was
        /// picked) and the far-side face, in the order and with the marks of the selection.
        /// Picked second, the near-side face is selected without a point; the position sketch
        /// moves the holes to their places anyway.
        /// </summary>
        private static bool Select(BuildContext ctx, Face2 face, Face2? farFace, Vec3? first, Selection sel)
        {
            ModelDoc2 doc = ctx.Doc;
            if (farFace == null) return SelectFaceAt(ctx, face, first, sel.Near);
            if (!sel.FarFirst) return SelectFaceAt(ctx, face, first, sel.Near) && SwSelect.Select(doc, farFace, sel.Far, true);
            SwSelect.Clear(doc);
            return SwSelect.Select(doc, farFace, sel.Far, false) && SwSelect.Select(doc, face, sel.Near, true);
        }

        private static bool OnPlane(BuildContext ctx, Feature sketch, Plane3 plane)
        {
            Plane3 sketchPlane = ctx.Math.SketchFrame((Sketch)sketch.GetSpecificFeature2()).Plane;
            return sketchPlane.IsParallelTo(plane) && plane.Contains(sketchPlane.Origin);
        }

        /// <summary>The elements of one side for the creation call, or null if one cannot be created.</summary>
        private static object? Elements(ModelDoc2 doc, List<AdvancedHoleElement> wanted, Dictionary<AdvancedHoleElement, object> references,
            bool callout, bool wrapped)
        {
            var result = new List<object>();
            foreach (AdvancedHoleElement e in wanted)
            {
                object? element = SwAdvancedHole.Create(doc.Extension, e, references.TryGetValue(e, out object reference) ? reference : null, callout);
                if (element == null) return null;
                result.Add(element);
            }
            return wrapped ? result.Select(x => new DispatchWrapper(x)).ToArray() : (object)result.ToArray();
        }

        /// <summary>
        /// Within one edit, side by side and only where something differs: elements of the
        /// right number and order of types are corrected in place, otherwise the side gets new
        /// elements – handed over as the kind of array the creation call accepted.
        /// </summary>
        private static void Correct(ModelDoc2 doc, AdvancedHoleFeatureData d, AdvancedHoleRecord r, Dictionary<AdvancedHoleElement, object> references,
            bool wrapped)
        {
            foreach (bool farSide in new[] { false, true })
            {
                List<AdvancedHoleElement> wanted = farSide ? r.Far : r.Near;
                object[] actual = SwAdvancedHole.Elements(d, farSide);
                var differing = new List<string>();
                Compare(actual, wanted, "", differing);
                if (differing.Count == 0) continue;
                bool sameTypes = actual.Length == wanted.Count &&
                                 actual.Select((x, i) => ElementType(x) == wanted[i].ElementType).All(same => same);
                object? elements;
                if (sameTypes)
                {
                    for (int i = 0; i < actual.Length; i++)
                        SwAdvancedHole.Apply(actual[i], wanted[i], references.TryGetValue(wanted[i], out object reference) ? reference : null, r.CustomizeCallout);
                    elements = wrapped ? actual.Select(x => new DispatchWrapper(x)).ToArray() : (object)actual;
                }
                else
                {
                    elements = Elements(doc, wanted, references, r.CustomizeCallout, wrapped);
                    if (elements == null) continue;
                }
                if (farSide) d.SetFarSideElements(elements);
                else d.SetNearSideElements(elements);
            }
            if (d.UseBaselineDimensions != r.UseBaselineDimensions) d.UseBaselineDimensions = r.UseBaselineDimensions;
            if (d.CustomizeCallout != r.CustomizeCallout) d.CustomizeCallout = r.CustomizeCallout;
        }

        private static int ElementType(object element)
        {
            try
            {
                return ((AdvancedHoleElementData)element).ElementType;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        /// <summary>What the new hole has differently from the original, side by side.</summary>
        private static List<string> Differences(ModelDoc2 doc, Feature f, AdvancedHoleRecord r)
        {
            var result = new List<string>();
            var d = (AdvancedHoleFeatureData)f.GetDefinition();
            if (!d.AccessSelections(doc, null)) return result;
            try
            {
                Compare(SwAdvancedHole.Elements(d, false), r.Near, "Nahseite", result);
                Compare(SwAdvancedHole.Elements(d, true), r.Far, "Fernseite", result);
                if (d.UseBaselineDimensions != r.UseBaselineDimensions) result.Add("Basismaße");
                if (d.CustomizeCallout != r.CustomizeCallout) result.Add("angepasste Bohrungsbeschriftung");
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return result;
        }

        private static void Compare(object[] actual, List<AdvancedHoleElement> wanted, string side, List<string> into)
        {
            if (actual.Length != wanted.Count)
            {
                into.Add($"{side}: {actual.Length} statt {wanted.Count} Elemente");
                return;
            }
            for (int i = 0; i < actual.Length; i++)
            {
                List<string> diff = SwAdvancedHole.Differences(actual[i], wanted[i]);
                if (diff.Count > 0) into.Add($"{side}, Element {i + 1} ({wanted[i]}): {string.Join(", ", diff)}");
            }
        }

        private static string Describe(AdvancedHoleRecord r)
        {
            string near = string.Join(" + ", r.Near.Select(e => e.ToString()));
            return r.Far.Count == 0 ? near : $"{near} | fernseitig {string.Join(" + ", r.Far.Select(e => e.ToString()))}";
        }
    }
}
