using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Capture
{
    /// <summary>Sweeps and lofts: profiles, path, guide curves and centreline are sketches that are rebuilt on their own.</summary>
    internal sealed partial class FeatureCapture
    {
        private SweepRecord CaptureSweep(Feature f, string type, SweepFeatureData d)
        {
            var r = new SweepRecord(f.Name, type);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.IsCut = !d.IsBossFeature();
                if (r.IsCut && d.GetCutSweepOption() == (int)swCutSweepOption_e.swSolidSweep)
                    throw new NotSupportedException("Austragung mit Werkzeugkörper (Volumenkörper-Schnitt) wird nicht unterstützt.");
                r.IsThin = d.IsThinFeature();
                if (r.IsThin)
                {
                    r.ThinType = d.ThinWallType;
                    r.Thickness1 = d.GetWallThickness(true);
                    r.Thickness2 = d.GetWallThickness(false);
                }

                r.CircularProfile = d.CircularProfile;
                if (r.CircularProfile) r.CircularDiameter = d.CircularProfileDiameter;
                else r.Profile = RequireCurve(d.Profile, "Profil");
                r.Path = RequireCurve(d.Path, "Pfad");
                r.Direction = d.Direction;
                if (d.GetGuideCurvesCount() > 0 && d.GuideCurves is object[] guides)
                    foreach (object g in guides) r.Guides.Add(RequireCurve(g, "Führungskurve"));

                r.Twist = (TwistControl)d.TwistControlType;
                r.TwistAngle = d.GetTwistAngle();
                r.TwistAngle2 = d.GetD2TwistAngle();
                r.ReverseTwist1 = d.D1ReverseTwistDir;
                r.ReverseTwist2 = d.D2ReverseTwistDir;
                r.PathAlignment = (Tangency)d.PathAlignmentType;
                if (r.PathAlignment == Tangency.DirectionVector)
                {
                    r.PathAlignmentDirection = ToGeometryRef(d.GetPathAlignmentDirectionVector(out int _));
                }

                r.MaintainTangency = d.MaintainTangency;
                r.AdvancedSmoothing = d.AdvancedSmoothing;
                r.AlignWithEndFaces = d.AlignWithEndFaces;
                r.TangentPropagation = d.TangentPropagation;
                r.MergeSmoothFaces = d.MergeSmoothFaces;
                r.StartTangency = (Tangency)d.StartTangencyType;
                r.EndTangency = (Tangency)d.EndTangencyType;
                if (r.StartTangency == Tangency.DirectionVector) r.StartDirection = ToGeometryRef(d.StartDirectionVector);
                if (r.EndTangency == Tangency.DirectionVector) r.EndDirection = ToGeometryRef(d.EndDirectionVector);
                r.Merge = d.Merge;
                if (d.FeatureScope && !d.AutoSelect)
                    r.Notes.Add("Der Feature-Bereich (ausgewählte Körper) wird nicht übernommen.");
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return r;
        }

        private LoftRecord CaptureLoft(Feature f, string type, LoftFeatureData d)
        {
            var r = new LoftRecord(f.Name, type);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.IsCut = !d.IsBossFeature();
                r.IsThin = d.IsThinFeature();
                if (r.IsThin)
                {
                    r.ThinType = d.ThinWallType;
                    r.Thickness1 = d.GetWallThickness(true);
                    r.Thickness2 = d.GetWallThickness(false);
                }

                if (d.Profiles is object[] profiles)
                    foreach (object p in profiles) r.Profiles.Add(RequireProfile(p));
                if (r.Profiles.Count < 2) throw new NotSupportedException("Ausformung mit weniger als zwei lesbaren Profilen.");
                if (d.GetGuideCurvesCount() > 0 && d.GuideCurves is object[] guides)
                    for (int i = 0; i < guides.Length; i++)
                    {
                        r.Guides.Add(RequireCurve(guides[i], "Führungskurve"));
                        r.GuideTangency.Add(d.GetGuideTangencyType((short)i));
                    }
                if (d.Centerline != null) r.Centerline = RequireCurve(d.Centerline, "Mittellinie");
                ReadPickPoints(d.PickPoints, r);

                r.Closed = d.Close;
                r.MaintainTangency = d.MaintainTangency;
                r.AdvancedSmoothing = d.AdvancedSmoothing;
                r.GuideCurveInfluence = d.GuideCurveInfluence;
                r.Merge = d.Merge;
                r.StartTangency = (Tangency)d.StartTangencyType;
                r.EndTangency = (Tangency)d.EndTangencyType;
                r.StartTangentLength = d.StartTangentLength;
                r.EndTangentLength = d.EndTangentLength;
                r.ReverseStartTangent = d.ReverseStartTangentDirection;
                r.ReverseEndTangent = d.ReverseEndTangentDirection;
                r.StartApplyToAll = d.StartConstraintApplyToAll;
                r.EndApplyToAll = d.EndConstraintApplyToAll;
                if (r.StartTangency == Tangency.DirectionVector)
                {
                    r.StartDirection = ToGeometryRef(d.StartDirectionVector);
                    r.StartDraftAngle = d.StartConstraintDraftAngle;
                    r.ReverseStartDraft = d.StartConstraintDraftAngleDirection;
                }
                if (r.EndTangency == Tangency.DirectionVector)
                {
                    r.EndDirection = ToGeometryRef(d.EndDirectionVector);
                    r.EndDraftAngle = d.EndConstraintDraftAngle;
                    r.ReverseEndDraft = d.EndConstraintDraftAngleDirection;
                }
                if (d.FeatureScope && !d.AutoSelect)
                    r.Notes.Add("Der Feature-Bereich (ausgewählte Körper) wird nicht übernommen.");
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return r;
        }

        /// <summary>
        /// Pick points: one array per profile chain, each holding math points (model space).
        /// They decide which vertices the loft connects; unreadable ones are reported, not fatal.
        /// </summary>
        private static void ReadPickPoints(object pickPoints, LoftRecord r)
        {
            if (!(pickPoints is object[] chains)) return;
            try
            {
                foreach (object chain in chains)
                {
                    var points = new List<Vec3>();
                    if (chain is object[] items)
                        foreach (object item in items)
                            if (item is MathPoint mp && mp.ArrayData is double[] xyz && xyz.Length >= 3)
                                points.Add(Vec3.FromArray(xyz));
                    r.PickPoints.Add(points);
                }
            }
            catch (Exception)
            {
                r.PickPoints.Clear();
            }
            if (r.PickPoints.Count == 0 || r.PickPoints.Any(c => c.Count == 0))
            {
                r.PickPoints.Clear();
                r.Notes.Add("Verbinder (Auswahlpunkte) der Profile nicht lesbar; die Zuordnung der Profile wird geprüft, nicht übertragen.");
            }
        }

        /// <summary>Profile, path, guide curve or centreline: a whole 2D sketch or one of its segments.</summary>
        private GeometryRef RequireCurve(object o, string what)
        {
            GeometryRef? g = ToGeometryRef(o);
            if (g != null && (g.Kind == GeometryRefKind.Sketch || g.Kind == GeometryRefKind.SketchSegment)) return g;
            throw new NotSupportedException($"{what}: nur 2D-Skizzen und ihre Elemente werden unterstützt, nicht {g?.ToString() ?? "unlesbarer Bezug"}.");
        }

        /// <summary>Loft profile: a 2D sketch, a sketch point or a planar face.</summary>
        private GeometryRef RequireProfile(object o)
        {
            GeometryRef? g = ToGeometryRef(o);
            if (g != null && (g.Kind == GeometryRefKind.Sketch || g.Kind == GeometryRefKind.SketchPoint || g.Kind == GeometryRefKind.Face)) return g;
            throw new NotSupportedException($"Profil: nur 2D-Skizzen, Skizzenpunkte und Flächen werden unterstützt, nicht {g?.ToString() ?? "unlesbarer Bezug"}.");
        }
    }
}
