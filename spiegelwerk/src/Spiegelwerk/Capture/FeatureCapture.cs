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
    internal sealed partial class FeatureCapture
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
                if ((type == "ProfileFeature" || type == "3DProfileFeature") && HolePart(f, type) is PartOfFeatureRecord part)
                    return part;
                if (type == "ProfileFeature") return new SketchRecord(f.Name, ReadSketch(f));
                if (type == "3DProfileFeature") return new UnsupportedRecord(f.Name, type, "3D-Skizzen werden noch nicht gespiegelt.", false);
                if (type == "RefPlane") return new RefPlaneRecord(f.Name, _math.PlaneOf((RefPlane)f.GetSpecificFeature2()));
                if (type == "RefAxis") return new RefAxisRecord(f.Name, SwDoc.AxisLine((RefAxis)f.GetSpecificFeature2()));

                object def = f.GetDefinition();
                switch (def)
                {
                    case ExtrudeFeatureData2 ex:
                        return CaptureExtrude(f, type, ex);
                    case RevolveFeatureData2 rev:
                        return CaptureRevolve(f, type, rev);
                    // Before the simple fillet: a variable fillet's data might answer to both.
                    case VariableFilletFeatureData2 vf:
                        return CaptureVariableFillet(f, type, vf);
                    case SimpleFilletFeatureData2 fil when IsFaceFillet(fil):
                        return CaptureFaceFillet(f, type, fil);
                    case SimpleFilletFeatureData2 fil:
                        return CaptureFillet(f, type, fil);
                    case ChamferFeatureData2 ch:
                        return CaptureChamfer(f, type, ch);
                    case SweepFeatureData sw:
                        return CaptureSweep(f, type, sw);
                    case LoftFeatureData lo:
                        return CaptureLoft(f, type, lo);
                    case LinearPatternFeatureData lp:
                        return CaptureLinearPattern(f, type, lp);
                    case CircularPatternFeatureData cp:
                        return CaptureCircularPattern(f, type, cp);
                    case CurveDrivenPatternFeatureData cdp:
                        return CaptureCurvePattern(f, type, cdp);
                    case SketchPatternFeatureData sp:
                        return CaptureSketchPattern(f, type, sp);
                    case WizardHoleFeatureData2 wh:
                        return CaptureHole(f, type, wh);
                    case AdvancedHoleFeatureData ah:
                        return CaptureAdvancedHole(f, type, ah);
                    case ShellFeatureData sh:
                        return CaptureShell(f, type, sh);
                    case DraftFeatureData2 dr:
                        return CaptureDraft(f, type, dr);
                }
                return new UnsupportedRecord(f.Name, type, UnsupportedReason(type), ChangesSolid(type));
            }
            catch (NotSupportedException ex)
            {
                return new UnsupportedRecord(f.Name, type, ex.Message, ChangesSolid(type));
            }
        }

        /// <summary>
        /// A sketch that refers to model edges, vertices or faces is read again with the
        /// rollback bar right after it: later features (fillets, cuts) may have split or
        /// shortened those edges, and their fingerprints must describe the state the sketch saw.
        /// </summary>
        private Core.Sketch.SketchModel ReadSketch(Feature f)
        {
            Core.Sketch.SketchModel model = _sketches.Read(f);
            bool refersToModel = model.Relations.SelectMany(x => x.Entities).Concat(model.Dimensions.SelectMany(x => x.Refs))
                .Any(x => x.Kind == Core.Sketch.SkRefKind.ModelEdge || x.Kind == Core.Sketch.SkRefKind.ModelVertex || x.Kind == Core.Sketch.SkRefKind.Plane);
            if (!refersToModel) return model;

            FeatureManager fm = _doc.FeatureManager;
            if (!fm.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToAfterFeature, f.Name)) return model;
            try
            {
                return _sketches.Read(f);
            }
            finally
            {
                fm.EditRollback((int)swMoveRollbackBarTo_e.swMoveRollbackBarToEnd, "");
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
                ReadSide(d, true, r.Side1, r.IsThin, true);
                // A single-direction thin feature still needs the second wall thickness; its
                // direction-2 end condition is unused (and may hold stale settings).
                if (r.BothDirections || r.IsThin) ReadSide(d, false, r.Side2, r.IsThin, r.BothDirections);

                // "Direction of extrusion": extruded along a line/edge/plane normal instead of the sketch normal.
                d.GetDirectionReference(out object dir1, out int _, out object dir2, out int _);
                r.DirectionReference1 = ToGeometryRef(dir1);
                r.DirectionReference2 = ToGeometryRef(dir2);

                if (d.GetContoursCount() > 0)
                    ReadContours((object[])d.Contours, r.Contours, r.Notes);
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return r;
        }

        /// <summary>Selected contours as sets of segment ids; regions cannot be mapped.</summary>
        private static void ReadContours(object[] contours, List<List<string>> into, List<string> notes)
        {
            foreach (object c in contours)
            {
                if (c is SketchContour sc)
                    into.Add(((object[])sc.GetSketchSegments()).Cast<SketchSegment>().Select(SwDoc.IdOf).ToList());
                else
                    notes.Add("Ausgewählte Skizzenbereiche (Regionen) werden nicht übernommen; es wird die ganze Skizze verwendet.");
            }
        }

        private void ReadSide(ExtrudeFeatureData2 d, bool forward, ExtrudeSide side, bool thin, bool used)
        {
            side.End = (EndCondition)d.GetEndCondition(forward);
            side.Depth = d.GetDepth(forward);
            side.Draft = d.GetDraftWhileExtruding(forward);
            side.DraftOutward = d.GetDraftOutward(forward);
            side.DraftAngle = d.GetDraftAngle(forward);
            side.OffsetReverse = d.GetReverseOffset(forward);
            side.TranslateSurface = d.GetTranslateSurface(forward);
            if (thin) side.WallThickness = d.GetWallThickness(forward);
            if (used && ExtrudeSide.NeedsReference(side.End))
            {
                object reference = d.GetEndConditionReference(forward, out int _);
                side.Reference = ToGeometryRef(reference);
                if (side.Reference == null || side.Reference.Kind == GeometryRefKind.Unknown)
                    throw new NotSupportedException($"Bezug der Endbedingung {side.End} ist im Original nicht lesbar.");
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
                if (d.FeatureScope && !d.AutoSelect)
                    r.Notes.Add("Der Feature-Bereich (ausgewählte Körper) wird nicht übernommen.");
                if (d.GetContoursCount() > 0)
                    ReadContours((object[])d.Contours, r.Contours, r.Notes);
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
                throw new NotSupportedException($"Verrundungstyp {d.Type} wird nicht unterstützt.");
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

                // FilletItemsCount/GetFilletItemAtIndex only describe multiple-radius fillets
                // (-1 otherwise); a plain fillet lists its items as edges and faces.
                int n = r.MultipleRadius ? d.FilletItemsCount : 0;
                for (int i = 0; i < n; i++)
                {
                    object item = d.GetFilletItemAtIndex(i);
                    if (item != null) AddFilletItem(r, item, d.GetRadius(item));
                }
                if (r.Items.Count == 0)
                {
                    if (d.GetEdgeCount() > 0 && d.Edges is object[] edges)
                        foreach (object e in edges) AddFilletItem(r, e, r.MultipleRadius ? d.GetRadius(e) : r.Radius);
                    const int single = (int)swSimpleFilletWhichFaces_e.swSimpleFilletSingleRadius;
                    if (d.GetFaceCount(single) > 0 && d.GetFaces(single) is object[] faces)
                        foreach (object face in faces) AddFilletItem(r, face, r.MultipleRadius ? d.GetRadius(face) : r.Radius);
                }
                if (d.GetLoopCount() > 0 || d.GetFeatureCount() > 0)
                    r.Notes.Add("Schleifen oder Features als Verrundungselemente werden nicht übernommen.");
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            if (r.Items.Count == 0) throw new NotSupportedException("Verrundung ohne übertragbare Kanten oder Flächen.");
            return r;
        }

        private static void AddFilletItem(FilletRecord r, object item, double radius)
        {
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
                // A sketch object is also a feature (and the other way round): ask for it before Feature.
                case Sketch sk:
                    return SketchRef(sk);
                case RefPlane rp:
                    return GeometryRef.ToPlane(_math.PlaneOf(rp), "Ebene");
                case RefAxis ax:
                    return GeometryRef.ToAxis(SwDoc.AxisLine(ax), "Achse");
                case Feature feat:
                    // Only the specific objects handled above; anything else would recurse.
                    return feat.GetSpecificFeature2() switch
                    {
                        RefPlane rp => GeometryRef.ToPlane(_math.PlaneOf(rp), "Ebene"),
                        RefAxis ax => GeometryRef.ToAxis(SwDoc.AxisLine(ax), "Achse"),
                        Sketch sk => SketchRef(sk),
                        _ => GeometryRef.Unknown(feat.GetTypeName2()),
                    };
                default:
                    return GeometryRef.Unknown(o.GetType().Name);
            }
        }

        private static GeometryRef SketchRef(Sketch sk) =>
            sk.Is3D() ? GeometryRef.Unknown("3D-Skizze") : GeometryRef.ToSketch(SwDoc.SketchNameOf(sk));

        private static bool ChangesSolid(string type) => !NonSolidTypes.Contains(type);

        private static readonly HashSet<string> NonSolidTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "RefAxis", "RefPoint", "CoordSys", "3DProfileFeature", "ProfileFeature", "RefPlane", "Helix", "CompositeCurve",
            "ReferenceCurve", "CurveInFile", "SplitLine", "CosmeticThread", "Attribute",
        };

        private static string UnsupportedReason(string type) => type switch
        {
            // Reached only if the definition does not answer as IAdvancedHoleFeatureData.
            "AdvHoleWzd" or "AdvancedHole" => "Erweiterte Bohrung: Feature-Daten nicht lesbar.",
            "MirrorPattern" => "Spiegel-Feature im Original: wird bewusst nicht nachgebaut, weil Spiegelwerk die Spiegel-API nicht verwendet. " +
                               "Abhilfe: im Original durch die gespiegelten Einzel-Features ersetzen oder nach dem Lauf von Hand spiegeln.",
            // Reached only if the definition does not answer as the pattern's data interface.
            "CurvePattern" or "SketchPattern" => "Muster: Feature-Daten nicht lesbar.",
            "TablePattern" or "FillPattern" or "DimPattern" or "VarPattern" =>
                "Dieses Muster wird nicht unterstützt (nur lineare, kreisförmige, Kurven- und Skizzenmuster).",
            _ => $"Feature-Typ {type} wird nicht unterstützt.",
        };
    }
}
