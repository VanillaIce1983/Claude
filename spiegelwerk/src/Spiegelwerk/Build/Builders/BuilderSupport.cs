using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
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

        /// <summary>The direction a reference of the original stands for: plane or planar-face normal, line direction.</summary>
        public static Vec3? ReferenceDirection(GeometryRef? g) => g?.Kind switch
        {
            GeometryRefKind.Plane => g.Plane.Normal,
            GeometryRefKind.Face when g.Face!.Kind == SurfaceKind.Plane => g.Face.Normal,
            GeometryRefKind.Axis => g.Axis.Direction,
            _ => (Vec3?)null,
        };

        /// <summary>The same for an object of the target, as SOLIDWORKS orients it; null if it has none.</summary>
        public static Vec3? ReferenceDirection(BuildContext ctx, object? o)
        {
            switch (o)
            {
                case Face2 face:
                {
                    FaceSignature sig = TopologyCapture.Face(face);
                    return sig.Kind == SurfaceKind.Plane ? sig.Normal : (Vec3?)null;
                }
                case Edge edge:
                    return TopologyCapture.LineOfEdge(edge)?.Direction;
                case Feature f when f.GetSpecificFeature2() is RefPlane plane:
                    return ctx.Math.PlaneOf(plane).Normal;
                case Feature f when f.GetSpecificFeature2() is RefAxis axis:
                    return SwDoc.AxisLine(axis).Direction;
                case SketchLine line:
                {
                    // Start → end in model coordinates; points of a 3D sketch are model coordinates already.
                    Sketch sketch = ((SketchSegment)line).GetSketch();
                    var a = (SketchPoint)line.GetStartPoint2();
                    var b = (SketchPoint)line.GetEndPoint2();
                    if (sketch.Is3D()) return new Vec3(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
                    Frame3 frame = ctx.Math.SketchFrame(sketch);
                    return frame.ToWorld(b.X, b.Y) - frame.ToWorld(a.X, a.Y);
                }
                default:
                    return null;
            }
        }

        /// <summary>
        /// A "reverse" flag relative to a reference: σ of the reference direction (as for a
        /// sketch normal). If either direction is unknown, the flag stays as it was.
        /// </summary>
        public static bool FlagForReference(BuildContext ctx, GeometryRef? original, object? target, bool flag)
        {
            Vec3? a = ReferenceDirection(original), b = ReferenceDirection(ctx, target);
            return a.HasValue && b.HasValue ? DirectionRules.ReverseFlag(flag, DirectionRules.Sigma(a.Value, ctx.Mirror, b.Value)) : flag;
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

        /// <summary>
        /// A placement face selected at a point on it – the first mirrored hole centre: a hole
        /// feature places its first hole where the face was picked. The pick must hit this very
        /// face (in the same part the original may have a face at the same spot); otherwise the
        /// face is selected without a point, and the position sketch moves the holes to their
        /// places anyway. Clears the selection first.
        /// </summary>
        public static bool SelectFaceAt(BuildContext ctx, object face, Vec3? at, int mark)
        {
            ModelDoc2 doc = ctx.Doc;
            SwSelect.Clear(doc);
            if (face is Face2 target && at.HasValue)
            {
                Vec3 p = at.Value;
                if (doc.Extension.SelectByID2("", "FACE", p.X, p.Y, p.Z, false, mark, null, 0) &&
                    ((SelectionMgr)doc.SelectionManager).GetSelectedObject6(1, -1) is Face2 picked && IsSameFace(ctx, picked, target))
                    return true;
                SwSelect.Clear(doc);
            }
            return SwSelect.Select(doc, face, mark, false);
        }

        private static bool IsSameFace(BuildContext ctx, Face2 picked, Face2 target)
        {
            FaceSignature a = TopologyCapture.Face(picked), b = TopologyCapture.Face(target);
            if (!b.CouldBe(a) || TopologyCapture.DistanceToFace(target, a.Sample) > Tol.Length) return false;
            // Separate body: the face of the original at the same spot would put the hole into the original.
            return !ctx.SeparateBody || (picked.GetBody() is Body2 body && ctx.IsOwnBody(body));
        }

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
