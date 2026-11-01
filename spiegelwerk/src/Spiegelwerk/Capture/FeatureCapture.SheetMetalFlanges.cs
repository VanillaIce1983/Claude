using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Sketch;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>
    /// Flanges on edges (A2, blech-plan S2): hem and miter flange. Both go through the safe
    /// sheet-metal dispatch (<see cref="CaptureSheetMetal"/>), so a call that throws makes this
    /// feature unsupported, not the run fail. Edges and values are read under AccessSelections
    /// (the state before the feature), sketches outside it; the bend sub-features are read for
    /// notes (A2 P5).
    /// </summary>
    internal sealed partial class FeatureCapture
    {
        private HemRecord CaptureHem(Feature f, string type, HemFeatureData d)
        {
            var r = new HemRecord(f.Name, type);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition des Saums konnte nicht gelesen werden (AccessSelections).");
            try
            {
                if (SwSheetMetal.ReadHemScalars(d, r)) r.Notes.Add(OwnAllowanceUnreadable);
                r.Edges.AddRange(EdgesOf((object)d.Edges, "Saum"));
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (r.Edges.Count == 0) throw new NotSupportedException("Saum ohne lesbare Kanten.");

            // Width sketches: read outside the selection access, paired with the edges.
            ReadWidthSketches(f, r);
            ReadBendsInto(f, HemRules.Parent(r), r.Bends, r.BendOverrides, r.Notes, out int count);
            r.BendCount = count;
            return r;
        }

        private MiterFlangeRecord CaptureMiterFlange(Feature f, string type, MiterFlangeFeatureData d)
        {
            Feature sketch = SketchOf(f);
            var r = new MiterFlangeRecord(f.Name, type, sketch.Name);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition der Gehrungslasche konnte nicht gelesen werden (AccessSelections).");
            try
            {
                if (SwSheetMetal.ReadMiterScalars(d, r)) r.Notes.Add(OwnAllowanceUnreadable);
                // Chain order: the start of the chain carries the profile.
                r.Edges.AddRange(EdgesOf((object)d.Edges, "Gehrungslasche"));
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (r.Edges.Count == 0) throw new NotSupportedException("Gehrungslasche ohne lesbare Kanten.");
            ReadBendsInto(f, MiterRules.Parent(r), r.Bends, r.BendOverrides, r.Notes, out int count);
            r.BendCount = count;
            return r;
        }

        private const string OwnAllowanceUnreadable = "Eigene Biegezugabe nicht lesbar; die Kopie verwendet die Standard-Biegezugabe.";

        /// <summary>The edges of a flange in their order; anything else among them makes the feature unsupported (a face or loop cannot be found by an edge fingerprint).</summary>
        private static List<EdgeSignature> EdgesOf(object? edges, string what)
        {
            var result = new List<EdgeSignature>();
            if (!(edges is object[] items)) return result;
            foreach (object item in items)
            {
                if (!(item is Edge e)) throw new NotSupportedException($"{what}: ein gewähltes Element ist keine Kante.");
                result.Add(TopologyCapture.Edge(e));
            }
            return result;
        }

        /// <summary>
        /// The bend sub-features (by definition) with what differs from their owner: each bend with
        /// values of its own becomes a note (not transferable, A2 P5); one that does not read is
        /// said once. <paramref name="count"/>: every bend sub-feature, readable or not.
        /// </summary>
        private void ReadBendsInto(Feature f, BendValues parent, List<BendValues> bends, List<BendOverride> overrides, List<string> notes, out int count)
        {
            List<(Feature sub, BendValues? values)> read = SwSheetMetal.ReadBends(_doc, f);
            count = read.Count;
            bends.AddRange(read.Where(b => b.values != null).Select(b => b.values!));
            List<string> unreadable = read.Where(b => b.values == null).Select(b => b.sub.Name).ToList();
            if (unreadable.Count > 0)
                notes.Add($"Biegungen nicht lesbar ({string.Join(", ", unreadable.Select(n => $"„{n}“"))}); ob sie eigene Werte haben, ist nicht geprüft.");
            List<BendOverride> own = BendOverrides.Differences(parent, bends);
            overrides.AddRange(own);
            notes.AddRange(own.Select(o => o.Note()));
        }

        /// <summary>
        /// The width sketches of a hem (its sketch sub-features), read in the state the hem saw
        /// (rolled back after the sketch where it refers to the model) and once more as the finished
        /// part shows them; both must agree (same segments, same points). Paired with the hem's edges
        /// by the lines on them (<see cref="FlangeProfiles.Assign"/>). If any of that fails, the copy
        /// keeps the width sketches the new hem brings, with a note.
        /// </summary>
        private void ReadWidthSketches(Feature hem, HemRecord r)
        {
            List<Feature> sketches = SwDoc.SubFeatures(hem).Where(SwDoc.IsSketch).ToList();
            if (sketches.Count == 0) return;
            var candidates = new List<ProfileCandidate>();
            string? why = null;
            foreach (Feature s in sketches)
            {
                if (s.GetTypeName2() != "ProfileFeature")
                {
                    why = $"„{s.Name}“ ist eine 3D-Skizze";
                    break;
                }
                SketchModel atHem, final;
                try
                {
                    atHem = ReadSketch(s);
                    final = _sketches.Read(s);
                }
                catch (Exception ex)
                {
                    // A width sketch that does not read costs only the width, not the hem.
                    why = $"„{s.Name}“ nicht lesbar ({ex.Message})";
                    break;
                }
                if (!SameGeometry(atHem, final))
                {
                    why = $"„{s.Name}“ liegt im fertigen Teil anders als beim Saum";
                    break;
                }
                candidates.Add(new ProfileCandidate(s.Name, atHem));
            }
            int[]? pairs = why == null ? FlangeProfiles.Assign(r.Edges, candidates) : null;
            if (why == null && pairs == null) why = "sie lassen sich den Kanten nicht eindeutig zuordnen";
            if (why != null)
            {
                // Still owned: the new hem brings its own, these mean nothing without it.
                foreach (Feature s in sketches) r.WidthSketches.Add(new HemWidthSketch(s.Name, new SketchModel { Name = s.Name }, -1));
                r.WidthSketchesPaired = false;
                r.Notes.Add($"Breitenskizzen des Saums nicht übertragbar ({why}); die Kopie behält die Breitenskizzen, die SOLIDWORKS mit dem Saum anlegt.");
                return;
            }
            for (int i = 0; i < candidates.Count; i++) r.WidthSketches.Add(new HemWidthSketch(candidates[i].Name, candidates[i].Sketch, pairs![i]));
        }

        /// <summary>Same segment ids and every point at the same place (model coordinates).</summary>
        private static bool SameGeometry(SketchModel a, SketchModel b)
        {
            if (!a.Segments.Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(b.Segments.Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal)))
                return false;
            foreach (SkPoint p in a.Points)
            {
                SkPoint? q = b.TryPoint(p.Id);
                if (q == null || !a.ToModel(p).AlmostEquals(b.ToModel(q), Tol.Length)) return false;
            }
            return true;
        }
    }
}
