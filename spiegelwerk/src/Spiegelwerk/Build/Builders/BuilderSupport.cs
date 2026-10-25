using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>Helpers shared by the builders.</summary>
    internal static class BuilderSupport
    {
        /// <summary>
        /// Faces, edges and vertices found before a feature is created are used after the model
        /// has been rebuilt with that feature. A safe entity stays valid across the rebuild.
        /// Planes and sketch entities pass through unchanged.
        /// </summary>
        public static object? Safe(object? o) =>
            (o is Face2 || o is Edge || o is Vertex) && o is Entity e ? e.GetSafeEntity() ?? (object)e : o;

        /// <summary>Selected contours of the original → contours of the new sketch with the same segments.</summary>
        public static List<object>? MapContours(List<List<string>> contours, SketchMap sketch, List<string> notes)
        {
            if (contours.Count == 0) return null;
            var available = ((object[])sketch.Sketch.GetSketchContours() ?? Array.Empty<object>()).OfType<SketchContour>()
                .Select(c => (contour: c, ids: new HashSet<string>(((object[])c.GetSketchSegments()).OfType<SketchSegment>().Select(SwDoc.IdOf))))
                .ToList();
            var result = new List<object>();
            foreach (List<string> wanted in contours)
            {
                var newIds = new HashSet<string>(wanted.Where(sketch.Segments.ContainsKey).Select(id => SwDoc.IdOf(sketch.Segments[id])));
                var match = available.FirstOrDefault(a => a.ids.SetEquals(newIds));
                if (match.contour == null)
                {
                    notes.Add("Eine ausgewählte Kontur wurde in der neuen Skizze nicht gefunden; es wird die ganze Skizze verwendet.");
                    return null;
                }
                result.Add(match.contour);
            }
            return result;
        }

        /// <summary>
        /// Removes a feature that could not be completed. Its sketch stays for the next feature,
        /// unless <paramref name="withAbsorbed"/> – a hole's sketches belong to the hole alone.
        /// </summary>
        public static void Delete(BuildContext ctx, Feature f, bool withAbsorbed = false)
        {
            SwSelect.Clear(ctx.Doc);
            SwSelect.Select(ctx.Doc, f, 0, false);
            ctx.Doc.Extension.DeleteSelection2(withAbsorbed ? (int)swDeleteSelectionOptions_e.swDelete_Absorbed : 0);
            SwSelect.Clear(ctx.Doc);
            ctx.Topology.Invalidate();
        }

        /// <summary>
        /// Bodies a feature may act on in "separate body" mode. Called inside a FeatureEdit
        /// lambda, where the model is rolled back to just before the feature: exactly the bodies
        /// the feature will see – the mirrored body on its own, never one already fused with the original.
        /// </summary>
        public static object[] OwnBodiesBeforeFeature(BuildContext ctx) => ctx.OwnBodies().Cast<object>().ToArray();

        /// <summary>The target object for a reference of the original, mirrored; null if the original had none or it is not found.</summary>
        public static object? Find(BuildContext ctx, GeometryRef? original, List<string> notes) =>
            original == null ? null : Safe(ctx.Topology.Resolve(original.Mirror(ctx.Mirror), notes));

        /// <summary>
        /// "Separate body": a boss is created without merging and limited to the bodies of
        /// this job afterwards (<paramref name="scope"/>); a cut needs such a body to act on.
        /// Returns why the feature must not be built, or null.
        /// </summary>
        public static string? PlanSeparateBody(BuildContext ctx, bool isCut, bool merge, out bool scope, out bool createMerge)
        {
            int ownBefore = ctx.SeparateBody ? ctx.OwnBodies().Count : 0;
            scope = ctx.SeparateBody && (isCut || (merge && ownBefore > 0));
            createMerge = !ctx.SeparateBody && merge;
            return ctx.SeparateBody && isCut && ownBefore == 0
                ? "Es gibt noch keinen gespiegelten Körper, auf den der Schnitt wirken könnte; das Original bleibt unberührt."
                : null;
        }

        public static BuildResult Fail(string message, List<string> notes)
        {
            var result = BuildResult.Failed(message);
            result.Notes.AddRange(notes);
            return result;
        }

        public static bool IsThinOneSided(int thinType) =>
            thinType == (int)swThinWallType_e.swThinWallOneDirection || thinType == (int)swThinWallType_e.swThinWallOppDirection;

        public static int OtherThinSide(int thinType) =>
            thinType == (int)swThinWallType_e.swThinWallOneDirection ? (int)swThinWallType_e.swThinWallOppDirection : (int)swThinWallType_e.swThinWallOneDirection;

        public static bool HasUnequalWalls(int thinType, double t1, double t2) =>
            thinType == (int)swThinWallType_e.swThinWallTwoDirection && Math.Abs(t1 - t2) > 1e-12;

        /// <summary>Ok, or Warning if there is anything to tell.</summary>
        public static BuildResult Ok(Feature f, string message, List<string> notes)
        {
            var result = BuildResult.Ok(f, message);
            result.Notes.AddRange(notes);
            if (notes.Count > 0) result.Status = ReportStatus.Warning;
            return result;
        }
    }
}
