using System;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Capture
{
    /// <summary>Shell and draft: faces, edges and the pull direction, read in the state the feature saw them.</summary>
    internal sealed partial class FeatureCapture
    {
        private ShellRecord CaptureShell(Feature f, string type, ShellFeatureData d)
        {
            var r = new ShellRecord(f.Name, type);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.Thickness = d.Thickness;
                r.Direction = d.Direction;
                if (d.FacesRemovedCount > 0 && d.FacesRemoved is object[] removed)
                    r.RemovedFaces.AddRange(removed.OfType<Face2>().Select(TopologyCapture.Face));
                if (d.GetMultipleThicknessFacesCount() > 0 && d.MultipleThicknessFaces is object[] multi)
                    for (int i = 0; i < multi.Length; i++)
                        if (multi[i] is Face2 face)
                            r.MultiThickness.Add(new FaceThickness(TopologyCapture.Face(face), d.GetMultipleThicknessAtIndex(i)));
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return r;
        }

        private DraftRecord CaptureDraft(Feature f, string type, DraftFeatureData2 d)
        {
            var r = new DraftRecord(f.Name, type);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                int kind = d.Type;
                if (!Enum.IsDefined(typeof(DraftKind), kind))
                    throw new NotSupportedException($"Formschrägentyp {kind} wird nicht unterstützt.");
                r.Kind = (DraftKind)kind;
                r.Angle = d.Angle;
                r.Reverse = d.ReverseDirection;
                r.Propagation = d.FacePropagation;
                r.AllowReducedAngle = d.AllowReducedAngle;

                if (r.Kind == DraftKind.NeutralPlane)
                {
                    r.Direction = ToGeometryRef(d.NeutralPlane);
                    if (d.GetFacesToDraftCount() > 0 && d.FacesToDraft is object[] faces)
                        r.Faces.AddRange(faces.OfType<Face2>().Select(TopologyCapture.Face));
                    if (r.Faces.Count == 0) throw new NotSupportedException("Formschräge ohne lesbare Flächen.");
                }
                else
                {
                    r.Direction = ToGeometryRef(d.DirectionPull);
                    if (r.Kind == DraftKind.Step) r.StepType = d.StepType;
                    if (d.GetPartingLinesCount() > 0 && d.PartingLines is object[] edges)
                        for (int i = 0; i < edges.Length; i++)
                            if (edges[i] is Edge e)
                            {
                                r.PartingLines.Add(TopologyCapture.Edge(e));
                                r.OtherFace.Add(d.GetOtherFacesFlagAtIndex((short)i));
                            }
                    if (r.PartingLines.Count == 0) throw new NotSupportedException("Formschräge ohne lesbare Trennlinien.");
                }
                if (r.Direction == null || r.Direction.Kind == GeometryRefKind.Unknown)
                    throw new NotSupportedException(r.Kind == DraftKind.NeutralPlane
                        ? "Neutrale Ebene der Formschräge nicht lesbar."
                        : "Zugrichtung der Formschräge nicht lesbar.");
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return r;
        }
    }
}
