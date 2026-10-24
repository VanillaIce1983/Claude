using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
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

        /// <summary>Removes a feature that could not be completed; its sketch stays for the next feature.</summary>
        public static void Delete(BuildContext ctx, Feature f)
        {
            SwSelect.Clear(ctx.Doc);
            SwSelect.Select(ctx.Doc, f, 0, false);
            ctx.Doc.Extension.DeleteSelection2(0);
            SwSelect.Clear(ctx.Doc);
            ctx.Topology.Invalidate();
        }

        /// <summary>
        /// Bodies a feature may act on in "separate body" mode. Called inside a FeatureEdit
        /// lambda, where the model is rolled back to just before the feature: exactly the bodies
        /// the feature will see – the mirrored body on its own, never one already fused with the original.
        /// </summary>
        public static object[] OwnBodiesBeforeFeature(BuildContext ctx) => ctx.OwnBodies().Cast<object>().ToArray();

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
    }
}
