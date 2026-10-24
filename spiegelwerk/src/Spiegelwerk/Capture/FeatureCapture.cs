using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>
    /// Turns features of the original into <see cref="FeatureRecord"/>s. References to
    /// geometry are read while IFeatureData.AccessSelections is active: during that time the
    /// model is rolled back to the feature, so faces and edges are read in the state the
    /// feature saw them, not in the final state (later fillets have not split them yet).
    /// </summary>
    internal sealed class FeatureCapture
    {
        private readonly ModelDoc2 _doc;
        private readonly SwMath _math;
        private readonly SketchCapture _sketches;

        public FeatureCapture(ModelDoc2 doc, SwMath math)
        {
            _doc = doc;
            _math = math;
            _sketches = new SketchCapture(math);
        }

        public FeatureRecord Capture(Feature f)
        {
            string type = f.GetTypeName2();
            try
            {
                if (type == "ProfileFeature") return new SketchRecord(f.Name, _sketches.Read(f));
                if (type == "3DProfileFeature") return new UnsupportedRecord(f.Name, type, "3D-Skizzen werden noch nicht gespiegelt.", false);
                if (type == "RefPlane") return new RefPlaneRecord(f.Name, _math.PlaneOf((RefPlane)f.GetSpecificFeature2()));

                object def = f.GetDefinition();
                switch (def)
                {
                    case ExtrudeFeatureData2 ex:
                        return CaptureExtrude(f, type, ex);
                    case RevolveFeatureData2 rev:
                        return CaptureRevolve(f, type, rev);
                    case SimpleFilletFeatureData2 fil:
                        return CaptureFillet(f, type, fil);
                    case ChamferFeatureData2 ch:
                        return CaptureChamfer(f, type, ch);
                }
                return new UnsupportedRecord(f.Name, type, UnsupportedReason(type), ChangesSolid(type));
            }
            catch (NotSupportedException ex)
            {
                return new UnsupportedRecord(f.Name, type, ex.Message, ChangesSolid(type));
            }
        }

        private ExtrudeRecord CaptureExtrude(Feature f, string type, ExtrudeFeatureData2 d)
        {
            Feature sketch = SketchOf(f);
            var r = new ExtrudeRecord(f.Name, type, sketch.Name);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.IsCut = !d.IsBossFeature();
                r.IsThin = d.IsThinFeature();
                r.BothDirections = d.BothDirections;
                r.Reverse = d.ReverseDirection;
                r.FlipSideToCut = d.FlipSideToCut;
                r.Merge = d.Merge;
                r.FeatureScope = d.FeatureScope;
                r.AutoSelect = d.AutoSelect;
                r.Start = (StartCondition)d.FromType;
                r.StartOffset = d.FromOffsetDistance;
                r.StartOffsetReverse = d.FromOffsetReverse;
                if (r.Start == StartCondition.Surface || r.Start == StartCondition.Vertex)
                {
                    d.GetFromEntity(out object fromEntity, out int _);
                    r.StartReference = ToGeometryRef(fromEntity);
                }
                if (r.IsCut)
                {
                    r.NormalCut = d.NormalCut;
                    r.OptimizeGeometry = d.OptimizeGeometry;
                }
                if (r.IsThin)
                {
                    r.ThinType = d.ThinWallType;
                    r.CapEnds = d.CapEnds;
                    r.CapThickness = d.CapThickness;
                    r.AutoFillet = d.GetAutoFilletCorners();
                    r.AutoFilletRadius = d.GetAutoFilletRadius();
                }
                if (r.FeatureScope && !r.AutoSelect)
                    r.Notes.Add("Der Feature-Bereich (ausgewählte Körper) wird nicht übernommen.");
                ReadSide(d, true, r.Side1, r.IsThin);
                if (r.BothDirections || r.IsThin) ReadSide(d, false, r.Side2, r.IsThin);

                if (d.GetContoursCount() > 0)
                {
                    foreach (object c in (object[])d.Contours)
                    {
                        if (c is SketchContour sc)
                            r.Contours.Add(((object[])sc.GetSketchSegments()).Cast<SketchSegment>().Select(SwDoc.IdOf).ToList());
                        else
                            r.Notes.Add("Ausgewählte Skizzenbereiche (Regionen) werden nicht übernommen; es wird die ganze Skizze verwendet.");
                    }
                }
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return r;
        }

        private void ReadSide(ExtrudeFeatureData2 d, bool forward, ExtrudeSide side, bool thin)
        {
            side.End = (EndCondition)d.GetEndCondition(forward);
            side.Depth = d.GetDepth(forward);
            side.Draft = d.GetDraftWhileExtruding(forward);
            side.DraftOutward = d.GetDraftOutward(forward);
            side.DraftAngle = d.GetDraftAngle(forward);
            side.OffsetReverse = d.GetReverseOffset(forward);
            side.TranslateSurface = d.GetTranslateSurface(forward);
            if (thin) side.WallThickness = d.GetWallThickness(forward);
            if (ExtrudeSide.NeedsReference(side.End))
            {
                object reference = d.GetEndConditionReference(forward, out int _);
                side.Reference = ToGeometryRef(reference);
            }
        }

        private RevolveRecord CaptureRevolve(Feature f, string type, RevolveFeatureData2 d)
        {
            Feature sketch = SketchOf(f);
            var r = new RevolveRecord(f.Name, type, sketch.Name);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.IsCut = !d.IsBossFeature();
                r.IsThin = d.IsThinFeature();
                r.RevolveType = d.Type;
                r.Angle1 = d.GetRevolutionAngle(true);
                r.Angle2 = d.GetRevolutionAngle(false);
                r.Reverse = d.ReverseDirection;
                r.Merge = d.Merge;
                if (r.IsThin)
                {
                    r.ThinType = d.ThinWallType;
                    r.Thickness1 = d.GetWallThickness(true);
                    r.Thickness2 = d.GetWallThickness(false);
                }
                r.Axis = ToGeometryRef(d.Axis);
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return r;
        }

        private FilletRecord CaptureFillet(Feature f, string type, SimpleFilletFeatureData2 d)
        {
            if (d.Type != (int)swSimpleFilletType_e.swConstRadiusFillet)
                throw new NotSupportedException("Nur Verrundungen mit konstantem Radius werden unterstützt (keine Flächen- oder vollständige Verrundung).");
            if (d.ConicTypeForCrossSectionProfile != (int)swFeatureFilletProfileType_e.swFeatureFilletCircular)
                throw new NotSupportedException("Nur kreisförmige Verrundungsprofile werden unterstützt (kein Kegelschnitt).");
            if (d.AsymmetricFillet)
                throw new NotSupportedException("Asymmetrische Verrundungen werden nicht unterstützt.");

            var r = new FilletRecord(f.Name, type);
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.Radius = d.DefaultRadius;
                r.MultipleRadius = d.IsMultipleRadius;
                r.Propagate = d.PropagateToTangentFaces;
                r.OverflowType = d.OverflowType;
                r.KeepFeatures = d.KeepFeatures;
                r.RoundCorners = d.RoundCorners;

                for (int i = 0; i < d.FilletItemsCount; i++)
                {
                    object item = d.GetFilletItemAtIndex(i);
                    double radius = r.MultipleRadius ? d.GetRadius(item) : r.Radius;
                    switch (item)
                    {
                        case Edge e:
                            r.Items.Add(new FilletItem(TopologyCapture.Edge(e), null, radius));
                            break;
                        case Face2 face:
                            r.Items.Add(new FilletItem(null, TopologyCapture.Face(face), radius));
                            break;
                        default:
                            r.Notes.Add("Ein Verrundungselement (Schleife oder Feature) wird nicht übernommen.");
                            break;
                    }
                }
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (r.Items.Count == 0) throw new NotSupportedException("Verrundung ohne übertragbare Kanten oder Flächen.");
            return r;
        }

        private ChamferRecord CaptureChamfer(Feature f, string type, ChamferFeatureData2 d)
        {
            var chType = (swChamferType_e)d.Type;
            if (chType == swChamferType_e.swChamferVertex)
                throw new NotSupportedException("Eckpunkt-Fasen werden nicht unterstützt.");

            var r = new ChamferRecord(f.Name, type) { ChamferType = d.Type };
            if (!d.AccessSelections(_doc, null))
                throw new NotSupportedException("Feature-Definition konnte nicht gelesen werden (AccessSelections).");
            try
            {
                r.Distance1 = d.GetEdgeChamferDistance(0);
                r.Distance2 = d.GetEdgeChamferDistance(1);
                r.Angle = d.EdgeChamferAngle;
                r.TangentPropagation = d.TangentPropagation;
                r.KeepFeatures = d.KeepFeatures;
                if (d.GetEdgeCount() > 0)
                {
                    var edges = ((object[])d.Edges).Cast<Edge>().ToList();
                    r.Flip = d.GetIsFlipped(edges[0]);
                    if (edges.Any(e => d.GetIsFlipped(e) != r.Flip))
                        r.Notes.Add("Fasenrichtung ist nicht bei allen Kanten gleich; es wird die Richtung der ersten Kante verwendet.");
                    r.Edges.AddRange(edges.Select(TopologyCapture.Edge));
                }
                if (d.GetFaceCount() > 0)
                    r.Faces.AddRange(((object[])d.Faces).Cast<Face2>().Select(TopologyCapture.Face));
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (r.Edges.Count == 0 && r.Faces.Count == 0) throw new NotSupportedException("Fase ohne übertragbare Kanten oder Flächen.");
            return r;
        }

        /// <summary>The sketch a feature is built from: first absorbed sketch, otherwise a sketch parent (shared sketch).</summary>
        private static Feature SketchOf(Feature f)
        {
            Feature? sketch = SwDoc.SubFeatures(f).FirstOrDefault(SwDoc.IsSketch);
            if (sketch == null && f.GetParents() is object[] parents)
                sketch = parents.OfType<Feature>().FirstOrDefault(SwDoc.IsSketch);
            if (sketch == null) throw new NotSupportedException("Skizze des Features nicht gefunden.");
            if (sketch.GetTypeName2() != "ProfileFeature") throw new NotSupportedException("Features aus 3D-Skizzen werden nicht unterstützt.");
            return sketch;
        }

        private GeometryRef? ToGeometryRef(object o)
        {
            switch (o)
            {
                case null:
                    return null;
                case Face2 face:
                    return GeometryRef.ToFace(TopologyCapture.Face(face));
                case Edge e:
                {
                    Line3? line = TopologyCapture.LineOfEdge(e);
                    return line.HasValue ? GeometryRef.ToAxis(line.Value, "Kante") : GeometryRef.ToEdge(TopologyCapture.Edge(e));
                }
                case Vertex v:
                    return GeometryRef.ToVertex(TopologyCapture.Vertex(v));
                case SketchPoint p:
                {
                    Frame3 frame = _math.SketchFrame(p.GetSketch());
                    return GeometryRef.ToSketchPoint(SwDoc.SketchNameOf(p.GetSketch()), SwDoc.IdOf(p), frame.ToWorld(p.X, p.Y));
                }
                case SketchSegment s:
                    return GeometryRef.ToSketchSegment(SwDoc.SketchNameOf(s.GetSketch()), SwDoc.IdOf(s));
                case RefPlane rp:
                    return GeometryRef.ToPlane(_math.PlaneOf(rp), "Ebene");
                case RefAxis ax:
                {
                    // start point x,y,z, end point x,y,z
                    var p = (double[])ax.GetRefAxisParams();
                    Vec3 a = Vec3.FromArray(p, 0), b = Vec3.FromArray(p, 3);
                    return GeometryRef.ToAxis(new Line3(a, b - a), "Achse");
                }
                case Feature feat:
                    return ToGeometryRef(feat.GetSpecificFeature2());
                default:
                    return GeometryRef.Unknown(o.GetType().Name);
            }
        }

        private static bool ChangesSolid(string type) => !NonSolidTypes.Contains(type);

        private static readonly HashSet<string> NonSolidTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "RefAxis", "RefPoint", "CoordSys", "3DProfileFeature", "ProfileFeature", "RefPlane", "Helix", "CompositeCurve",
            "ReferenceCurve", "CurveInFile", "SplitLine", "CosmeticThread", "Attribute",
        };

        private static string UnsupportedReason(string type) => type switch
        {
            "Sweep" or "SweepCut" => "Austragung: noch nicht umgesetzt (Weg + Profil spiegeln, Ausrichtung prüfen).",
            "Blend" or "BlendCut" => "Ausformung: noch nicht umgesetzt (Profile + Führungskurven spiegeln, Zuordnung prüfen).",
            "HoleWzd" => "Bohrungsassistent: noch nicht umgesetzt (Positionsskizze spiegeln, Bohrungsdaten übernehmen).",
            "LPattern" or "CirPattern" or "MirrorPattern" => "Muster: noch nicht umgesetzt (Saat-Features zuordnen, Richtung spiegeln).",
            "VarFillet" => "Variable Verrundung wird nicht unterstützt.",
            "Shell" => "Wandung: noch nicht umgesetzt.",
            "Draft" => "Formschräge: noch nicht umgesetzt.",
            "RefAxis" => "Achsen werden bei Bedarf über ihre Geometrie aufgelöst, nicht als eigenes Feature kopiert.",
            _ => $"Feature-Typ {type} wird nicht unterstützt.",
        };
    }
}
