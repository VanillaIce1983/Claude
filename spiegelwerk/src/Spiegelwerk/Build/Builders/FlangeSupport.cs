using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// What the builders of flanges on edges share (A2 §5, blech-plan S2): edges resolved before
    /// anything is made and – in separate-body mode – on the job's own bodies only (A2 P12), the
    /// allowance in an edit of its own and read back after every edit (§1.4, A2 P7), the bends of
    /// the copy against the original's (A2 P5), and removal of what a failed attempt made.
    /// </summary>
    internal static class FlangeSupport
    {
        /// <summary>
        /// The mirrored edges in the target, in their order, made safe for the rebuilds to come;
        /// with their signatures in the target. Null and the reason if one is missing or – separate
        /// body – lies on the original: the flange would attach to it (no feature scope in sheet metal).
        /// </summary>
        public static List<(Edge edge, EdgeSignature signature)>? ResolveEdges(BuildContext ctx, IEnumerable<EdgeSignature> edges, string what, List<string> notes,
            out string? why)
        {
            why = null;
            var result = new List<(Edge, EdgeSignature)>();
            foreach (EdgeSignature sig in edges)
            {
                EdgeSignature mirrored = sig.Mirror(ctx.Mirror);
                Edge? e = ctx.Topology.FindEdge(mirrored, notes);
                if (e == null)
                {
                    why = $"Kante {what} im Ziel nicht gefunden ({mirrored}).";
                    return null;
                }
                if (ctx.SeparateBody && !OnOwnBody(ctx, e))
                {
                    why = $"Kante {what} liegt im Ziel nicht auf einem gespiegelten Körper; das Original bleibt unberührt.";
                    return null;
                }
                result.Add(((Edge)Safe(e)!, mirrored));
            }
            return result;
        }

        private static bool OnOwnBody(BuildContext ctx, Edge e) => BodyOf(e) is Body2 b && ctx.IsOwnBody(b);

        /// <summary>
        /// The body a face, edge or vertex lies on; null for anything without one (plane, axis, sketch
        /// point) or if it does not read.
        /// </summary>
        public static Body2? BodyOf(object? entity)
        {
            try
            {
                return entity switch
                {
                    Face2 face => (object)face.GetBody() as Body2,
                    Edge edge => (object)edge.GetBody() as Body2,
                    Vertex vertex => ((object)vertex.GetEdges() as object[])?.OfType<Edge>().Select(e => (object)e.GetBody() as Body2).FirstOrDefault(b => b != null),
                    _ => null,
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The feature's own allowance as it should be (§1.4): read back; if it is not what the
        /// original has, one allowance-only edit (<see cref="FeatureEdit.Allowance"/>), read back again.
        /// A kind the feature type refuses (<see cref="AllowanceRules.Settable"/>) is not attempted.
        /// What still differs becomes a note.
        /// </summary>
        /// <param name="readBack">The feature's own allowance now (null: the default); ok false if it did not read.</param>
        public static void SettleAllowance(BuildContext ctx, Feature f, string type, BendAllowanceData? want,
            Func<(bool ok, BendAllowanceData? own)> readBack, string label, List<string> notes)
        {
            if (want != null && !AllowanceRules.Settable(type, want.Kind))
            {
                notes.Add(AllowanceRules.NotSettable(want));
                want = null;
            }
            var (ok, got) = readBack();
            if (ok && SheetMetalCompare.Allowance(want, got).None) return;
            FeatureEdit.Allowance(ctx.Doc, f, want);
            ctx.Doc.EditRebuild3();
            ctx.Topology.Invalidate();
            (ok, got) = readBack();
            if (!ok)
            {
                notes.Add($"{label}: Biegezugabe ließ sich nicht zurücklesen.");
                return;
            }
            Deviations d = SheetMetalCompare.Allowance(want, got);
            notes.AddRange(d.Differences.Select(x => $"{label}: {x}"));
            notes.AddRange(d.Notes.Select(x => $"{label}: {x}"));
        }

        /// <summary>
        /// A variant for the check whose edit may reset the custom allowance (B27): after every toggle
        /// call the allowance is read back and set again if it moved – also after one that returns
        /// false, which can follow an edit that took (an edge flange's toggle-back whose profiles did
        /// not come back); without an edit it only reads. Toggling twice restores the feature. The
        /// toggle's result goes to the check unchanged.
        /// </summary>
        public static Func<bool> KeepingAllowance(BuildContext ctx, Feature f, string type, BendAllowanceData? want,
            Func<(bool ok, BendAllowanceData? own)> readBack, Func<bool> toggle) => () =>
        {
            bool toggled = toggle();
            SettleAllowance(ctx, f, type, want, readBack, "", new List<string>());
            return toggled;
        };

        /// <summary>
        /// The copy's bends against the original's (A2 P5): the number (by definition; another
        /// number is a note), own values the original's bends do not have (a note each), and – for a
        /// single bend on both sides – its place in the bend sequence (information only).
        /// </summary>
        public static void CheckBends(BuildContext ctx, Feature f, int originalCount, BendValues parent, List<BendValues> originals,
            List<BendOverride> originalOverrides, List<string> notes, List<string> info)
        {
            List<(Feature sub, BendValues? values)> read;
            try
            {
                read = SwSheetMetal.ReadBends(ctx.Doc, f);
            }
            catch (Exception ex)
            {
                notes.Add($"Biegungen der Kopie nicht lesbar ({ex.Message}).");
                return;
            }
            if (BendOverrides.CountNote(originalCount, read.Count) is string count) notes.Add(count);
            List<BendValues> copies = read.Where(b => b.values != null).Select(b => b.values!).ToList();
            notes.AddRange(BendOverrides.UnexpectedInCopy(originalOverrides, BendOverrides.Differences(parent, copies)));
            if (originals.Count == 1 && copies.Count == 1) info.AddRange(BendOverrides.OrderNotes(new[] { (originals[0], copies[0]) }));
        }

        /// <summary>Everything the step made since the snapshot (a failed attempt, a flange removed again); what stays is said.</summary>
        public static List<string> RemoveMade(BuildContext ctx, HashSet<string> before)
        {
            List<string> left = SwSheetMetal.RemoveNew(ctx.Doc, before);
            ctx.Topology.Invalidate();
            return left;
        }

        /// <summary>The note for features a failed attempt left behind.</summary>
        public static string LeftBehind(IEnumerable<string> names) =>
            $"Nicht wieder entfernt: {string.Join(", ", names.Select(n => $"„{n}“"))} – bitte prüfen und von Hand löschen.";

        /// <summary>The feature if it is still in the document (looked up by the name it had).</summary>
        public static Feature? Live(ModelDoc2 doc, Feature f)
        {
            try
            {
                return SwDoc.FeatureByName(doc, f.Name);
            }
            catch (Exception)
            {
                // Deleted with its owner.
                return null;
            }
        }
    }
}
