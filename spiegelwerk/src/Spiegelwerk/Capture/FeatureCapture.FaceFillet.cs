using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Capture
{
    /// <summary>Face fillet and full round fillet: the face sets, read in the state the fillet saw them.</summary>
    internal sealed partial class FeatureCapture
    {
        private static bool IsFaceFillet(SimpleFilletFeatureData2 d) =>
            d.Type == (int)swSimpleFilletType_e.swFaceFillet || d.Type == (int)swSimpleFilletType_e.swFullRoundFillet;

        private FaceFilletRecord CaptureFaceFillet(Feature f, string type, SimpleFilletFeatureData2 d)
        {
            var kind = (FaceFilletKind)d.Type;
            if (kind == FaceFilletKind.Face)
            {
                if (d.ConicTypeForCrossSectionProfile != (int)swFeatureFilletProfileType_e.swFeatureFilletCircular)
                    throw new NotSupportedException("Nur kreisförmige Verrundungsprofile werden unterstützt (kein Kegelschnitt).");
                if (d.AsymmetricFillet)
                    throw new NotSupportedException("Asymmetrische Verrundungen werden nicht unterstützt.");
            }

            var r = new FaceFilletRecord(f.Name, type, kind);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.Radius = d.DefaultRadius;
                r.Propagate = d.PropagateToTangentFaces;
                r.KeepFeatures = d.KeepFeatures;
                r.OverflowType = d.OverflowType;
                r.CurvatureContinuous = d.CurvatureContinuous;
                if (kind == FaceFilletKind.Face)
                {
                    r.Set1.AddRange(Faces(d, swSimpleFilletWhichFaces_e.swFaceFilletSet1));
                    r.Set2.AddRange(Faces(d, swSimpleFilletWhichFaces_e.swFaceFilletSet2));
                    r.ReverseNormal1 = d.get_ReverseFaceNormal((int)swSimpleFilletWhichFaces_e.swFaceFilletSet1);
                    r.ReverseNormal2 = d.get_ReverseFaceNormal((int)swSimpleFilletWhichFaces_e.swFaceFilletSet2);
                    r.ConstantWidth = d.ConstantWidth;
                    if (d.GetHoldLineCount() > 0 && d.HoldLines is object[] holds)
                    {
                        r.HoldLines.AddRange(holds.OfType<Edge>().Select(TopologyCapture.Edge));
                        if (r.HoldLines.Count < holds.Length)
                            r.Notes.Add("Haltelinien, die keine Kanten sind (z. B. Trennlinien-Kurven), werden nicht übernommen.");
                    }
                    if (d.HelpPoint != null)
                        r.Notes.Add("Der Hilfspunkt der Flächenverrundung wird nicht übernommen.");
                }
                else
                {
                    r.Set1.AddRange(Faces(d, swSimpleFilletWhichFaces_e.swFullRoundFilletSet1));
                    r.Center.AddRange(Faces(d, swSimpleFilletWhichFaces_e.swFullRoundFilletCenterSet));
                    r.Set2.AddRange(Faces(d, swSimpleFilletWhichFaces_e.swFullRoundFilletSet2));
                    if (r.Center.Count == 0) throw new NotSupportedException("Vollständige Verrundung ohne lesbare Mittelflächen.");
                }
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (r.Set1.Count == 0 || r.Set2.Count == 0) throw new NotSupportedException("Verrundung ohne lesbare Flächengruppen.");
            return r;
        }

        private static IEnumerable<FaceSignature> Faces(SimpleFilletFeatureData2 d, swSimpleFilletWhichFaces_e which) =>
            d.GetFaceCount((int)which) > 0 && d.GetFaces((int)which) is object[] faces
                ? faces.OfType<Face2>().Select(TopologyCapture.Face).ToList()
                : Enumerable.Empty<FaceSignature>();
    }
}
