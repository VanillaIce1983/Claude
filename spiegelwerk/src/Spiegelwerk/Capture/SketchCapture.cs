using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Sketch;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Capture
{
    /// <summary>
    /// Reads a 2D sketch into the neutral <see cref="SketchModel"/>: points with their ids,
    /// segments referencing points, relations and dimensions referencing both, plus external
    /// references (origin, other sketches, model edges) as geometry.
    /// </summary>
    internal sealed class SketchCapture
    {
        private readonly SwMath _math;

        public SketchCapture(SwMath math)
        {
            _math = math;
        }

        public SketchModel Read(Feature feature)
        {
            var sketch = (Sketch)feature.GetSpecificFeature2();
            if (sketch.Is3D())
                throw new NotSupportedException("3D-Skizzen werden nicht gespiegelt.");

            var model = new SketchModel { Name = feature.Name, Frame = _math.SketchFrame(sketch) };

            var userPoints = new HashSet<string>(Objects<SketchPoint>(sketch.GetUserPoints2()).Select(SwDoc.IdOf));
            foreach (SketchPoint p in Objects<SketchPoint>(sketch.GetSketchPoints2()))
            {
                string id = SwDoc.IdOf(p);
                model.Points.Add(new SkPoint(id, p.X, p.Y) { Standalone = userPoints.Contains(id) });
            }

            foreach (SketchSegment seg in Objects<SketchSegment>(sketch.GetSketchSegments()))
            {
                SkSegment? s = ReadSegment(seg, model);
                if (s != null) model.Segments.Add(s);
            }

            ReadRelations(sketch, model);
            ReadDimensions(feature, model);
            return model;
        }

        private static SkSegment? ReadSegment(SketchSegment seg, SketchModel model)
        {
            string id = SwDoc.IdOf(seg);
            SkSegment s;
            switch ((swSketchSegments_e)seg.GetType())
            {
                case swSketchSegments_e.swSketchLINE:
                {
                    var l = (SketchLine)seg;
                    s = new SkSegment(id, SkSegmentKind.Line)
                    {
                        StartId = SwDoc.IdOf((SketchPoint)l.GetStartPoint2()),
                        EndId = SwDoc.IdOf((SketchPoint)l.GetEndPoint2()),
                    };
                    break;
                }
                case swSketchSegments_e.swSketchARC:
                {
                    var a = (SketchArc)seg;
                    string center = SwDoc.IdOf((SketchPoint)a.GetCenterPoint2());
                    if (a.IsCircle() == 1)
                    {
                        s = new SkSegment(id, SkSegmentKind.Circle) { CenterId = center, Radius = a.GetRadius() };
                    }
                    else
                    {
                        s = new SkSegment(id, SkSegmentKind.Arc)
                        {
                            CenterId = center,
                            StartId = SwDoc.IdOf((SketchPoint)a.GetStartPoint2()),
                            EndId = SwDoc.IdOf((SketchPoint)a.GetEndPoint2()),
                            Direction = a.GetRotationDir() >= 0 ? 1 : -1,
                        };
                    }
                    break;
                }
                case swSketchSegments_e.swSketchELLIPSE:
                {
                    var e = (SketchEllipse)seg;
                    var start = (SketchPoint)e.GetStartPoint2();
                    var end = (SketchPoint)e.GetEndPoint2();
                    bool full = start == null || end == null || SwDoc.IdOf(start) == SwDoc.IdOf(end);
                    s = new SkSegment(id, full ? SkSegmentKind.Ellipse : SkSegmentKind.EllipticalArc)
                    {
                        CenterId = SwDoc.IdOf((SketchPoint)e.GetCenterPoint2()),
                        MajorId = SwDoc.IdOf((SketchPoint)e.GetMajorPoint2()),
                        MinorId = SwDoc.IdOf((SketchPoint)e.GetMinorPoint2()),
                        StartId = full ? null : SwDoc.IdOf(start!),
                        EndId = full ? null : SwDoc.IdOf(end!),
                        Direction = e.GetRotationDir() >= 0 ? 1 : -1,
                    };
                    break;
                }
                case swSketchSegments_e.swSketchSPLINE:
                {
                    var sp = (SketchSpline)seg;
                    s = new SkSegment(id, SkSegmentKind.Spline);
                    foreach (SketchPoint fp in Objects<SketchPoint>(sp.GetPoints2()))
                        s.FitPointIds.Add(SwDoc.IdOf(fp));
                    if (sp.GetSplineHandleCount() > 0)
                        model.Warnings.Add($"Spline {id}: Tangenten-/Krümmungsgriffe werden nicht übernommen, nur die Stützpunkte.");
                    break;
                }
                case swSketchSegments_e.swSketchPARABOLA:
                {
                    var pa = (SketchParabola)seg;
                    s = new SkSegment(id, SkSegmentKind.Parabola)
                    {
                        CenterId = SwDoc.IdOf((SketchPoint)pa.GetFocalPoint2()),
                        MajorId = SwDoc.IdOf((SketchPoint)pa.GetApexPoint2()),
                        StartId = SwDoc.IdOf((SketchPoint)pa.GetStartPoint2()),
                        EndId = SwDoc.IdOf((SketchPoint)pa.GetEndPoint2()),
                    };
                    break;
                }
                case swSketchSegments_e.swSketchTEXT:
                    model.Warnings.Add($"Skizzentext {id} wird nicht übernommen (gespiegelter Text wäre unlesbar).");
                    return null;
                default:
                    model.Warnings.Add($"Skizzenelement {id} (Typ {seg.GetType()}) wird nicht unterstützt.");
                    return null;
            }
            s.Construction = seg.ConstructionGeometry;
            return s;
        }

        private void ReadRelations(Sketch sketch, SketchModel model)
        {
            SketchRelationManager rm = sketch.RelationManager;
            string self = model.Name;
            foreach (SketchRelation rel in Objects<SketchRelation>(rm.GetRelations((int)swSketchRelationFilterType_e.swAll)))
            {
                var type = (swConstraintType_e)rel.GetRelationType();
                RelationKind? kind = MapRelation(type);
                if (kind == null)
                {
                    if (!IsDimensionConstraint(type))
                        model.Warnings.Add($"Beziehung {type} wird nicht übernommen.");
                    continue;
                }
                var entities = Objects<object>(rel.GetEntities()).Select(e => ToRef(e, self)).ToList();
                if (entities.Count == 0) continue;
                model.Relations.Add(new SkRelation(kind.Value, entities));
            }
        }

        private void ReadDimensions(Feature feature, SketchModel model)
        {
            string self = model.Name;
            var dd = (DisplayDimension)feature.GetFirstDisplayDimension();
            while (dd != null)
            {
                Dimension dim = dd.GetDimension2(0);
                var ann = (Annotation)dd.GetAnnotation();
                DimensionKind? kind = MapDimension((swDimensionType_e)dd.Type2);
                if (kind == null)
                {
                    model.Warnings.Add($"Bemaßung {dim.Name} (Typ {(swDimensionType_e)dd.Type2}) wird nicht übernommen.");
                }
                else
                {
                    var refs = Objects<object>(ann.GetAttachedEntities3()).Select(e => ToRef(e, self)).ToList();
                    var pos = (double[])ann.GetPosition();
                    model.Dimensions.Add(new SkDimension(kind.Value, refs, dim.SystemValue)
                    {
                        Name = dim.Name,
                        Driven = dim.DrivenState == (int)swDimensionDrivenState_e.swDimensionDriven,
                        TextPosition = pos != null && pos.Length >= 3 ? Vec3.FromArray(pos) : (Vec3?)null,
                    });
                }
                dd = (DisplayDimension)feature.GetNextDisplayDimension(dd);
            }
        }

        /// <summary>Classifies what a relation/dimension refers to.</summary>
        private SkRef ToRef(object entity, string selfName)
        {
            switch (entity)
            {
                case SketchPoint p:
                {
                    var owner = (Feature)p.GetSketch();
                    if (owner.Name == selfName) return SkRef.LocalPoint(SwDoc.IdOf(p));
                    if (owner.GetTypeName2() == "OriginProfileFeature") return SkRef.Origin();
                    Frame3 f = _math.SketchFrame(p.GetSketch());
                    return SkRef.OtherSketchPoint(owner.Name, SwDoc.IdOf(p), f.ToWorld(p.X, p.Y));
                }
                case SketchSegment s:
                {
                    string owner = SwDoc.SketchNameOf(s.GetSketch());
                    return owner == selfName ? SkRef.LocalSegment(SwDoc.IdOf(s)) : SkRef.OtherSketchSegment(owner, SwDoc.IdOf(s));
                }
                case Edge e:
                    return SkRef.ModelEdge(TopologyCapture.Edge(e));
                case Vertex v:
                    return SkRef.ModelVertex(TopologyCapture.Vertex(v));
                case Face2 face:
                {
                    Plane3? plane = TopologyCapture.PlaneOfFace(face);
                    return plane.HasValue ? SkRef.Plane(plane.Value, "Fläche") : SkRef.Unknown("gekrümmte Fläche");
                }
                case Feature feat when feat.GetTypeName2() == "RefPlane":
                    return SkRef.Plane(_math.PlaneOf((RefPlane)feat.GetSpecificFeature2()), feat.Name);
                case RefPlane rp:
                    return SkRef.Plane(_math.PlaneOf(rp), "Ebene");
                default:
                    return SkRef.Unknown(entity?.GetType().Name ?? "leer");
            }
        }

        public static RelationKind? MapRelation(swConstraintType_e t) => t switch
        {
            swConstraintType_e.swConstraintType_HORIZONTAL => RelationKind.Horizontal,
            swConstraintType_e.swConstraintType_VERTICAL => RelationKind.Vertical,
            swConstraintType_e.swConstraintType_HORIZPOINTS => RelationKind.HorizontalPoints,
            swConstraintType_e.swConstraintType_VERTPOINTS => RelationKind.VerticalPoints,
            swConstraintType_e.swConstraintType_COINCIDENT => RelationKind.Coincident,
            swConstraintType_e.swConstraintType_MERGEPOINTS => RelationKind.MergePoints,
            swConstraintType_e.swConstraintType_TANGENT => RelationKind.Tangent,
            swConstraintType_e.swConstraintType_PARALLEL => RelationKind.Parallel,
            swConstraintType_e.swConstraintType_PERPENDICULAR => RelationKind.Perpendicular,
            swConstraintType_e.swConstraintType_SAMELENGTH => RelationKind.Equal,
            swConstraintType_e.swConstraintType_CONCENTRIC => RelationKind.Concentric,
            swConstraintType_e.swConstraintType_CORADIAL => RelationKind.Coradial,
            swConstraintType_e.swConstraintType_ATMIDDLE => RelationKind.Midpoint,
            swConstraintType_e.swConstraintType_COLINEAR => RelationKind.Collinear,
            swConstraintType_e.swConstraintType_SYMMETRIC => RelationKind.Symmetric,
            swConstraintType_e.swConstraintType_FIXED => RelationKind.Fixed,
            swConstraintType_e.swConstraintType_ATINTERSECT => RelationKind.AtIntersection,
            swConstraintType_e.swConstraintType_EQUALCURVATURE => RelationKind.EqualCurvature,
            swConstraintType_e.swConstraintType_ATPIERCE => RelationKind.Pierce,
            _ => null,
        };

        public static swConstraintType_e ToSw(RelationKind k) => k switch
        {
            RelationKind.Horizontal => swConstraintType_e.swConstraintType_HORIZONTAL,
            RelationKind.Vertical => swConstraintType_e.swConstraintType_VERTICAL,
            RelationKind.HorizontalPoints => swConstraintType_e.swConstraintType_HORIZPOINTS,
            RelationKind.VerticalPoints => swConstraintType_e.swConstraintType_VERTPOINTS,
            RelationKind.Coincident => swConstraintType_e.swConstraintType_COINCIDENT,
            RelationKind.MergePoints => swConstraintType_e.swConstraintType_MERGEPOINTS,
            RelationKind.Tangent => swConstraintType_e.swConstraintType_TANGENT,
            RelationKind.Parallel => swConstraintType_e.swConstraintType_PARALLEL,
            RelationKind.Perpendicular => swConstraintType_e.swConstraintType_PERPENDICULAR,
            RelationKind.Equal => swConstraintType_e.swConstraintType_SAMELENGTH,
            RelationKind.Concentric => swConstraintType_e.swConstraintType_CONCENTRIC,
            RelationKind.Coradial => swConstraintType_e.swConstraintType_CORADIAL,
            RelationKind.Midpoint => swConstraintType_e.swConstraintType_ATMIDDLE,
            RelationKind.Collinear => swConstraintType_e.swConstraintType_COLINEAR,
            RelationKind.Symmetric => swConstraintType_e.swConstraintType_SYMMETRIC,
            RelationKind.Fixed => swConstraintType_e.swConstraintType_FIXED,
            RelationKind.AtIntersection => swConstraintType_e.swConstraintType_ATINTERSECT,
            RelationKind.EqualCurvature => swConstraintType_e.swConstraintType_EQUALCURVATURE,
            RelationKind.Pierce => swConstraintType_e.swConstraintType_ATPIERCE,
            _ => throw new ArgumentOutOfRangeException(nameof(k)),
        };

        /// <summary>Selection-based fallback (IModelDoc2.SketchAddConstraints) for relations to external entities.</summary>
        public static string? ToConstraintString(RelationKind k) => k switch
        {
            RelationKind.Horizontal or RelationKind.HorizontalPoints => "sgHORIZONTAL2D",
            RelationKind.Vertical or RelationKind.VerticalPoints => "sgVERTICAL2D",
            RelationKind.Coincident or RelationKind.MergePoints => "sgCOINCIDENT",
            RelationKind.Tangent => "sgTANGENT",
            RelationKind.Parallel => "sgPARALLEL",
            RelationKind.Perpendicular => "sgPERPENDICULAR",
            RelationKind.Equal => "sgSAMELENGTH",
            RelationKind.Concentric => "sgCONCENTRIC",
            RelationKind.Coradial => "sgCORADIAL",
            RelationKind.Midpoint => "sgATMIDDLE",
            RelationKind.Collinear => "sgCOLINEAR",
            RelationKind.Symmetric => "sgSYMMETRIC",
            RelationKind.Fixed => "sgFIXED",
            RelationKind.AtIntersection => "sgATINTERSECT",
            RelationKind.Pierce => "sgATPIERCE",
            _ => null,
        };

        /// <summary>Relations that are only the solver's view of a dimension; dimensions are read separately.</summary>
        private static bool IsDimensionConstraint(swConstraintType_e t) =>
            t == swConstraintType_e.swConstraintType_DISTANCE || t == swConstraintType_e.swConstraintType_ANGLE ||
            t == swConstraintType_e.swConstraintType_RADIUS || t == swConstraintType_e.swConstraintType_DIAMETER ||
            t == swConstraintType_e.swConstraintType_ARCLENGTH || t == swConstraintType_e.swConstraintType_DOUBLEDISTANCE ||
            t == swConstraintType_e.swConstraintType_ANGLE3P ||
            (t >= swConstraintType_e.swConstraintType_ARCANG90 && t <= swConstraintType_e.swConstraintType_ARCANGRIGHT) ||
            (t >= swConstraintType_e.swConstraintType_ELLIPSEANG90 && t <= swConstraintType_e.swConstraintType_ELLIPSEANGRIGHT) ||
            t == swConstraintType_e.swConstraintType_FAKESLOTCONSTRAINT;

        public static DimensionKind? MapDimension(swDimensionType_e t) => t switch
        {
            swDimensionType_e.swLinearDimension => DimensionKind.Aligned,
            swDimensionType_e.swHorLinearDimension => DimensionKind.Horizontal,
            swDimensionType_e.swVertLinearDimension => DimensionKind.Vertical,
            swDimensionType_e.swAngularDimension => DimensionKind.Angular,
            swDimensionType_e.swRadialDimension => DimensionKind.Radial,
            swDimensionType_e.swDiameterDimension => DimensionKind.Diameter,
            _ => null,
        };

        private static IEnumerable<T> Objects<T>(object arr) where T : class =>
            arr is object[] a ? a.OfType<T>() : Enumerable.Empty<T>();
    }
}
