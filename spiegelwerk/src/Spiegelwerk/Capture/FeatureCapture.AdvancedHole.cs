using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>
    /// Advanced Hole: elements of both sides, hole centres, position sketch – and the near- and
    /// far-side faces, which the feature data does not report: they are found geometrically in
    /// the state before the hole.
    /// </summary>
    internal sealed partial class FeatureCapture
    {
        private AdvancedHoleRecord CaptureAdvancedHole(Feature f, string type, AdvancedHoleFeatureData d)
        {
            Feature position = SwAdvancedHole.PositionSketch(f)
                               ?? throw new NotSupportedException(SwDoc.SubFeatures(f).Any(x => x.GetTypeName2() == "3DProfileFeature")
                                   ? "Erweiterte Bohrungen mit 3D-Positionsskizze werden nicht unterstützt."
                                   : "Positionsskizze der erweiterten Bohrung nicht gefunden.");
            Frame3 frame = _math.SketchFrame((Sketch)position.GetSpecificFeature2());
            List<Vec3> points = SwAdvancedHole.Points(position).Select(p => frame.ToWorld(p.X, p.Y)).ToList();
            if (points.Count == 0) throw new NotSupportedException("Erweiterte Bohrung ohne Positionspunkte.");

            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            var near = new List<AdvancedHoleElement>();
            var far = new List<AdvancedHoleElement>();
            bool baseline, callout;
            try
            {
                ReadElements(d, false, near);
                ReadElements(d, true, far);
                baseline = d.UseBaselineDimensions;
                callout = d.CustomizeCallout;
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (near.Count == 0) throw new NotSupportedException("Erweiterte Bohrung ohne Elemente auf der Nahseite.");

            // Faces in the state before the hole; outside the selection access, which must not
            // be combined with moving the rollback bar.
            GeometryRef face;
            GeometryRef? farFace = null;
            FeatureManager fm = _doc.FeatureManager;
            if (!fm.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToBeforeFeature, f.Name))
                throw new NotSupportedException("Zustand vor der Bohrung nicht herstellbar (Rollback).");
            try
            {
                (Face2 nearFace, Vec3 centre) = NearFace(points, frame.ZAxis)
                                                ?? throw new NotSupportedException("Nahseitige Fläche der erweiterten Bohrung nicht gefunden (erwartet: ebene Fläche unter einem Positionspunkt).");
                face = ToGeometryRef(nearFace)!;
                if (far.Count > 0)
                {
                    Face2 exit = FarFace(nearFace, centre)
                                 ?? throw new NotSupportedException("Fernseitige Fläche der erweiterten Bohrung nicht bestimmbar (erwartet: ebene Fläche, durch die die Bohrungsachse den Körper verlässt).");
                    farFace = ToGeometryRef(exit);
                }
            }
            finally
            {
                fm.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
            }

            // Read outside the selection access: ReadSketch may move the rollback bar itself.
            var r = new AdvancedHoleRecord(f.Name, type, position.Name, ReadSketch(position))
            {
                Face = face,
                FarFace = farFace,
                UseBaselineDimensions = baseline,
                CustomizeCallout = callout,
            };
            r.Points.AddRange(points);
            r.Near.AddRange(near);
            r.Far.AddRange(far);
            if (far.Count > 0)
                r.Notes.Add("Die fernseitige Fläche ist über die API nicht lesbar; angenommen wird die erste Fläche, durch die die Bohrungsachse den Körper verlässt.");
            return r;
        }

        private void ReadElements(AdvancedHoleFeatureData d, bool farSide, List<AdvancedHoleElement> into)
        {
            object[] elements = SwAdvancedHole.Elements(d, farSide);
            string side = farSide ? "fernseitig" : "nahseitig";
            // A side that cannot be handed out must not pass as a side without elements.
            int? count = SwAdvancedHole.ElementCount(d, farSide);
            if (count.HasValue && count.Value != elements.Length)
                throw new NotSupportedException($"Elemente {side} nicht vollständig lesbar ({elements.Length} von {count.Value}).");
            for (int i = 0; i < elements.Length; i++)
            {
                AdvancedHoleElement e;
                try
                {
                    e = SwAdvancedHole.Read(elements[i], farSide, ToGeometryRef);
                }
                catch (Exception ex) when (!(ex is NotSupportedException))
                {
                    throw new NotSupportedException($"Element {i + 1} ({side}) nicht lesbar: {ex.Message}");
                }
                if (!AdvancedHoleTypes.IsKnown(e.ElementType))
                    throw new NotSupportedException($"Element {i + 1} ({side}) hat den unbekannten Typ {e.ElementType} (neuere SOLIDWORKS-Version?).");
                if (ExtrudeSide.NeedsReference(e.End) && (e.EndReference == null || e.EndReference.Kind == GeometryRefKind.Unknown))
                    throw new NotSupportedException($"Bezug der Endbedingung {e.End} von Element {i + 1} ({side}) ist im Original nicht lesbar.");
                into.Add(e);
            }
        }

        /// <summary>
        /// The planar face the holes start from: under a hole centre, parallel to the position
        /// sketch. Facing the sketch normal if two bodies touch there.
        /// </summary>
        private (Face2 face, Vec3 centre)? NearFace(List<Vec3> centres, Vec3 sketchNormal)
        {
            List<Face2> faces = SwDoc.SolidBodies(_doc).SelectMany(b => (b.GetFaces() as object[])?.OfType<Face2>() ?? Enumerable.Empty<Face2>()).ToList();
            foreach (Vec3 c in centres)
            {
                (Face2 face, bool facing)? best = null;
                foreach (Face2 face in faces)
                {
                    if (!(TopologyCapture.PlaneOfFace(face) is Plane3 plane) || !plane.Normal.IsParallelTo(sketchNormal, 1e-6)) continue;
                    if (TopologyCapture.DistanceToFace(face, c) > Tol.Length) continue;
                    bool facing = plane.Normal.Dot(sketchNormal) > 0;
                    if (best == null || (facing && !best.Value.facing)) best = (face, facing);
                }
                if (best != null) return (best.Value.face, c);
            }
            return null;
        }

        /// <summary>The planar face of the same body through which the axis at <paramref name="centre"/> first leaves it.</summary>
        private static Face2? FarFace(Face2 nearFace, Vec3 centre)
        {
            if (!(TopologyCapture.PlaneOfFace(nearFace) is Plane3 nearPlane) || !(nearFace.GetBody() is Body2 body)) return null;
            var faces = new List<Face2>();
            var planes = new List<Plane3>();
            foreach (Face2 face in (body.GetFaces() as object[])?.OfType<Face2>() ?? Enumerable.Empty<Face2>())
                if (TopologyCapture.PlaneOfFace(face) is Plane3 plane)
                {
                    faces.Add(face);
                    planes.Add(plane);
                }
            int i = HoleAxis.FirstExit(centre, -nearPlane.Normal, planes, (k, p) => TopologyCapture.DistanceToFace(faces[k], p) <= Tol.Length);
            return i >= 0 ? faces[i] : null;
        }
    }
}
