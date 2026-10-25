using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
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
    ///     are tried in turn, and a hole whose position sketch does not lie on the near-side
    ///     face (near and far side swapped) is removed again before the next.
    ///  2. Write the mirrored position sketch into the position sketch of the new hole
    ///     (SketchBuilder.BuildInto), as for a Hole Wizard hole.
    ///  3. Compare the elements with the original and correct what differs in one edit.
    ///  4. Count the holes.
    ///
    /// No direction flag: the elements run from their face into the material, and a
    /// reflection keeps outward normals outward. No feature scope either – the API has none
    /// for this feature; in "separate body" mode the job's guard removes a hole that would
    /// have reached the original.
    /// </summary>
    internal sealed class AdvancedHoleBuilder : IFeatureBuilder
    {
        /// <summary>Selection marks tried in turn: near-side face, far-side face.</summary>
        private static readonly (int near, int far)[] Marks = { (0, 1), (1, 2), (0, 0) };

        public bool Handles(FeatureRecord record) => record is AdvancedHoleRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (AdvancedHoleRecord)record;
            var notes = new List<string>();

            // A hole is a cut: in "separate body" mode it needs a mirrored body to act on.
            string? refused = PlanSeparateBody(ctx, true, false, out bool _, out bool _);
            if (refused != null) return Fail(refused, notes);

            if (!(Find(ctx, r.Face, notes) is Face2 face))
                return Fail("Nahseitige Fläche der erweiterten Bohrung im Ziel nicht gefunden.", notes);
            Face2? farFace = null;
            if (r.Far.Count > 0)
            {
                farFace = Find(ctx, r.FarFace, notes) as Face2;
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
            Feature? f = Create(ctx, r, face, farFace, centres, references, notes, out Feature? position);
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
                bool ok = FeatureEdit.AdvancedHole(ctx.Doc, f, d => Correct(ctx.Doc, d, r, references));
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

        /// <summary>
        /// The hole with one point and its position sketch, or null. Tries the selection marks
        /// and both kinds of array; a hole whose position sketch lies on the far-side face took
        /// the faces the wrong way round and is removed again.
        /// </summary>
        private static Feature? Create(BuildContext ctx, AdvancedHoleRecord r, Face2 face, Face2? farFace, List<Vec3> centres,
            Dictionary<AdvancedHoleElement, object> references, List<string> notes, out Feature? position)
        {
            position = null;
            ModelDoc2 doc = ctx.Doc;
            Plane3? farPlane = farFace == null ? null : TopologyCapture.PlaneOfFace(farFace);
            Vec3? first = centres.Count > 0 ? centres[0] : (Vec3?)null;
            bool swapped = false;
            var tried = new HashSet<(int, int)>();
            foreach (var (nearMark, farMark) in Marks)
            {
                if (!tried.Add((nearMark, farFace == null ? -1 : farMark))) continue;
                foreach (bool wrapped in new[] { false, true })
                {
                    object? near = Elements(doc, r.Near, references, r.CustomizeCallout, wrapped);
                    object? far = r.Far.Count == 0 ? null : Elements(doc, r.Far, references, r.CustomizeCallout, wrapped);
                    if (near == null || (r.Far.Count > 0 && far == null))
                    {
                        notes.Add("Ein Element der Bohrung lässt sich in dieser SOLIDWORKS-Version nicht anlegen.");
                        return null;
                    }

                    Feature? f = null;
                    try
                    {
                        if (!SelectFaceAt(ctx, face, first, nearMark)) continue;
                        if (farFace != null && !SwSelect.Select(doc, farFace, farMark, true)) continue;
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
                        continue;
                    }
                    position = sketch;
                    return f;
                }
            }
            if (swapped) notes.Add("Die Bohrung entstand nur mit vertauschter Nah- und Fernseite und wurde wieder entfernt.");
            return null;
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
        /// Within one edit: elements of the right number are corrected in place, a side whose
        /// number or order of types differs gets new elements.
        /// </summary>
        private static void Correct(ModelDoc2 doc, AdvancedHoleFeatureData d, AdvancedHoleRecord r, Dictionary<AdvancedHoleElement, object> references)
        {
            foreach (bool farSide in new[] { false, true })
            {
                List<AdvancedHoleElement> wanted = farSide ? r.Far : r.Near;
                object[] actual = SwAdvancedHole.Elements(d, farSide);
                if (wanted.Count == 0 && actual.Length == 0) continue;
                bool sameTypes = actual.Length == wanted.Count &&
                                 actual.Select((x, i) => ElementType(x) == wanted[i].ElementType).All(same => same);
                object? elements;
                if (sameTypes)
                {
                    for (int i = 0; i < actual.Length; i++)
                        SwAdvancedHole.Apply(actual[i], wanted[i], references.TryGetValue(wanted[i], out object reference) ? reference : null, r.CustomizeCallout);
                    elements = actual;
                }
                else
                {
                    elements = Elements(doc, wanted, references, r.CustomizeCallout, false);
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
