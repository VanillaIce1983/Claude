using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// What the builders of corners, folds and the cross break share (A3 (2) §5, blech-plan S5): every
    /// reference resolved before anything is made and – separate body – on the job's own bodies (A3 P12:
    /// sheet metal has no feature scope, and a cross break changes no solid, so nothing after it would
    /// notice it on the original); the bends of an unfold or fold found again (A3 P8, K25).
    /// </summary>
    internal static class CornerSupport
    {
        /// <summary>
        /// The mirrored faces in the target, in their order, made safe for the rebuilds to come. Null and the
        /// reason if one is missing or – separate body – lies on the original.
        /// </summary>
        public static List<Face2>? ResolveFaces(BuildContext ctx, IEnumerable<FaceSignature> faces, string what, List<string> notes, out string? why)
        {
            why = null;
            var result = new List<Face2>();
            int i = 0;
            foreach (FaceSignature sig in faces)
            {
                i++;
                Face2? face = ctx.Topology.FindFace(sig.Mirror(ctx.Mirror), notes);
                if (face == null)
                {
                    why = $"Fläche {i} {what} im Ziel nicht gefunden.";
                    return null;
                }
                if (ctx.SeparateBody && !OnOwnBody(ctx, face))
                {
                    why = $"Fläche {i} {what} liegt im Ziel nicht auf einem gespiegelten Körper; das Original bleibt unberührt.";
                    return null;
                }
                result.Add((Face2)BuilderSupport.Safe(face)!);
            }
            return result;
        }

        public static bool OnOwnBody(BuildContext ctx, object entity) => FlangeSupport.BodyOf(entity) is Body2 b && ctx.IsOwnBody(b);

        /// <summary>The mirrored fingerprints, for the read-back.</summary>
        public static List<FaceSignature> Mirrored(BuildContext ctx, IEnumerable<FaceSignature> faces) => faces.Select(f => f.Mirror(ctx.Mirror)).ToList();

        /// <summary>Selects the entities with their marks (appending); false if one does not take.</summary>
        public static bool SelectAll(ModelDoc2 doc, IReadOnlyList<object> items, IReadOnlyList<int> marks, bool append = false)
        {
            if (!append) SwSelect.Clear(doc);
            for (int i = 0; i < items.Count; i++)
                if (!SwSelect.Select(doc, items[i], marks[i], append || i > 0))
                    return false;
            return true;
        }

        /// <summary>The faces of a feature; none for one the kernel does not list.</summary>
        public static List<Face2> FacesOf(Feature f)
        {
            try
            {
                return ((object)f.GetFaces() as object[])?.OfType<Face2>().ToList() ?? new List<Face2>();
            }
            catch (Exception)
            {
                return new List<Face2>();
            }
        }

        /// <summary>
        /// The bend of the target an original bend reference names (A3 P8, K25), in this order: the bend the
        /// run found for it before (an unfold before the fold) – separate body: only on a mirrored body; the bend
        /// sub-feature whose faces are, as a set, its mirrored faces (<see cref="BendMatcher"/>; separate body:
        /// only on mirrored bodies); the only bend of its owner's copy, if the owner had only this one. What is
        /// found is remembered (<see cref="BuildContext.RegisterBend"/>). Null and why otherwise.
        /// </summary>
        public static Feature? FindBend(BuildContext ctx, BendRef original, List<string> notes, List<string> info, out string? why)
        {
            why = null;
            if (ctx.BendFor(original.Name) is Feature known && (!ctx.SeparateBody || OwnBend(ctx, known))) return known;

            BendRef mirrored = original.Mirror(ctx.Mirror);
            List<(Feature bend, Feature owner)> all = SwSheetMetal.AllBends(ctx.Doc);
            List<List<Face2>> faces = all.Select(b => FacesOf(b.bend)).ToList();
            List<IReadOnlyList<FaceSignature>> sigs = faces.Select(l => (IReadOnlyList<FaceSignature>)l.Select(TopologyCapture.Face).ToList()).ToList();
            bool Own(int c) => faces[c].Any(face => OnOwnBody(ctx, face));
            double Distance(int c, int k, Core.Geometry.Vec3 p) => TopologyCapture.DistanceToFace(faces[c][k], p);
            MatchResult m = BendMatcher.FindBend(mirrored.Faces, sigs, Distance, Own, c => !ctx.SeparateBody || Own(c));
            if (m.Found)
            {
                if (m.Ambiguous) notes.Add(FoldRules.Ambiguous(original.Name, m.Candidates));
                Feature bend = all[m.Index].bend;
                ctx.RegisterBend(original.Name, original.Owner, bend);
                return bend;
            }

            if (original.OrdinalUsable)
            {
                List<Feature> copies = ctx.Counterparts(original.Owner, false).Where(f => !SwDoc.IsSketch(f)).ToList();
                if (copies.Count == 1)
                {
                    List<Feature> bends = SwSheetMetal.BendsOf(copies[0]);
                    if (FoldRules.UseOwnersOnlyBend(original, bends.Count))
                    {
                        info.Add(FoldRules.ByOwner(original.Name, original.Owner));
                        ctx.RegisterBend(original.Name, original.Owner, bends[0]);
                        return bends[0];
                    }
                }
            }
            bool onOriginal = ctx.SeparateBody && BendMatcher.FindBend(mirrored.Faces, sigs, Distance).Found;
            why = onOriginal ? FoldRules.BendNotOwn(original.Name) : FoldRules.BendMissing(original.Name);
            return null;
        }

        private static bool OwnBend(BuildContext ctx, Feature bend)
        {
            List<Face2> faces = FacesOf(bend);
            // Without faces of its own: its owner's copy is the run's (it was found as such).
            return faces.Count == 0 || faces.Any(f => OnOwnBody(ctx, f));
        }

        /// <summary>
        /// Removes everything the step made since the snapshot (a variant that did not take) and says what stays;
        /// true if nothing stayed.
        /// </summary>
        public static bool RemoveMade(BuildContext ctx, HashSet<string> before, List<string> tried)
        {
            List<string> left = FlangeSupport.RemoveMade(ctx, before);
            if (left.Count == 0) return true;
            tried.Add(FlangeSupport.LeftBehind(left));
            return false;
        }
    }
}
