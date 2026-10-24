using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;

namespace Spiegelwerk.Core.Sketch
{
    /// <summary>
    /// A 2D sketch independent of SOLIDWORKS: points in sketch coordinates, segments that
    /// reference points by id, relations and dimensions that reference points/segments by id.
    /// The add-in reads an ISketch into this model, mirrors the model and writes it back.
    /// </summary>
    public sealed class SketchModel
    {
        public string Name { get; set; } = "";

        /// <summary>Sketch-to-model frame (X/Y in the sketch plane, Z = sketch normal).</summary>
        public Frame3 Frame { get; set; } = Frame3.World;

        public List<SkPoint> Points { get; } = new List<SkPoint>();
        public List<SkSegment> Segments { get; } = new List<SkSegment>();
        public List<SkRelation> Relations { get; } = new List<SkRelation>();
        public List<SkDimension> Dimensions { get; } = new List<SkDimension>();

        /// <summary>Things that were read but cannot be reproduced (text, blocks, pictures ...).</summary>
        public List<string> Warnings { get; } = new List<string>();

        /// <summary>How well the original sketch was defined (swConstrainedStatus_e), for comparison with the copy; 0 = unknown.</summary>
        public int ConstrainedStatus { get; set; }

        public SkPoint Point(string id) =>
            Points.FirstOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException($"Skizzenpunkt {id} fehlt.");

        public SkPoint? TryPoint(string? id) => id == null ? null : Points.FirstOrDefault(p => p.Id == id);

        public SkSegment Segment(string id) =>
            Segments.FirstOrDefault(s => s.Id == id) ?? throw new KeyNotFoundException($"Skizzensegment {id} fehlt.");

        /// <summary>Ids of all points that belong to a segment (end, centre, axis, fit points).</summary>
        public HashSet<string> SegmentPointIds()
        {
            var ids = new HashSet<string>();
            foreach (SkSegment s in Segments)
                foreach (string id in s.PointIds())
                    ids.Add(id);
            return ids;
        }

        /// <summary>
        /// Sketch points the user drew as points and no segment creates. Hidden helper points
        /// of segments are listed by SOLIDWORKS too but are not standalone and must not be
        /// recreated as extra points.
        /// </summary>
        public IEnumerable<SkPoint> FreePoints()
        {
            HashSet<string> used = SegmentPointIds();
            return Points.Where(p => p.Standalone && !used.Contains(p.Id));
        }

        public Vec3 ToModel(SkPoint p) => Frame.ToWorld(p.X, p.Y);
    }

    public sealed class SkPoint
    {
        public SkPoint(string id, double x, double y)
        {
            Id = id;
            X = x;
            Y = y;
        }

        public string Id { get; }
        public double X { get; }
        public double Y { get; }

        /// <summary>Drawn as a point of its own (ISketch.GetUserPoints2), not only part of a segment.</summary>
        public bool Standalone { get; set; }

        public override string ToString() => $"{Id} ({X:0.######}, {Y:0.######})";
    }

    public enum SkSegmentKind
    {
        Line,
        Arc,
        Circle,
        Ellipse,
        EllipticalArc,
        Spline,
        Parabola,
    }

    public sealed class SkSegment
    {
        public SkSegment(string id, SkSegmentKind kind)
        {
            Id = id;
            Kind = kind;
        }

        public string Id { get; }
        public SkSegmentKind Kind { get; }
        public bool Construction { get; set; }

        public string? StartId { get; set; }
        public string? EndId { get; set; }

        /// <summary>Arcs, circles, ellipses; parabola: focus.</summary>
        public string? CenterId { get; set; }

        /// <summary>Ellipse: point on the major axis; parabola: apex.</summary>
        public string? MajorId { get; set; }

        /// <summary>Ellipse: point on the minor axis.</summary>
        public string? MinorId { get; set; }

        /// <summary>Circle radius (a full circle has no start or end point).</summary>
        public double Radius { get; set; }

        /// <summary>Arcs and elliptical arcs: +1 counter-clockwise, -1 clockwise about the sketch normal.</summary>
        public int Direction { get; set; } = 1;

        /// <summary>Spline fit points, in order.</summary>
        public List<string> FitPointIds { get; } = new List<string>();

        public IEnumerable<string> PointIds()
        {
            foreach (string? id in new[] { StartId, EndId, CenterId, MajorId, MinorId })
                if (id != null) yield return id;
            foreach (string id in FitPointIds) yield return id;
        }

        public override string ToString() => $"{Kind} {Id}";
    }

    public enum RelationKind
    {
        Horizontal,
        Vertical,
        HorizontalPoints,
        VerticalPoints,
        Coincident,
        MergePoints,
        Tangent,
        Parallel,
        Perpendicular,
        Equal,
        Concentric,
        Coradial,
        Midpoint,
        Collinear,
        Symmetric,
        Fixed,
        AtIntersection,
        EqualCurvature,
        Pierce,

        /// <summary>
        /// "On edge" link of converted entities (swConstraintType USEEDGE). Not a relation
        /// SOLIDWORKS can add directly: the builder re-links the segment to the mirrored edge
        /// (collinear/coradial) or applies the fallback for missing references.
        /// </summary>
        ExternalLink,

        /// <summary>
        /// Offset link of offset entities (OFFSETEDGE to a model edge, SKETCHOFFSET within the
        /// sketch): the builder keeps the direction (parallel/concentric to the source) and lets
        /// the captured offset dimension set the distance – never collinear, that would pull
        /// the offset geometry onto its source.
        /// </summary>
        OffsetLink,
    }

    public sealed class SkRelation
    {
        public SkRelation(RelationKind kind, IEnumerable<SkRef> entities)
        {
            Kind = kind;
            Entities = entities.ToList();
        }

        public RelationKind Kind { get; }
        public List<SkRef> Entities { get; }

        public bool HasExternal => Entities.Any(e => !e.IsLocal);

        public override string ToString() => $"{Kind}({string.Join(", ", Entities)})";
    }

    public enum DimensionKind
    {
        /// <summary>True distance (aligned), point-line distance, line length.</summary>
        Aligned,
        Horizontal,
        Vertical,
        Angular,
        Radial,
        Diameter,
    }

    public sealed class SkDimension
    {
        public SkDimension(DimensionKind kind, IEnumerable<SkRef> refs, double value)
        {
            Kind = kind;
            Refs = refs.ToList();
            Value = value;
        }

        public DimensionKind Kind { get; }
        public List<SkRef> Refs { get; }

        /// <summary>System units: metres or radians.</summary>
        public double Value { get; }

        /// <summary>"D1"; the add-in tries to keep the name so equations keep working.</summary>
        public string Name { get; set; } = "";

        /// <summary>Reference (driven) dimension.</summary>
        public bool Driven { get; set; }

        /// <summary>Text position in model coordinates. It decides which angle an angular dimension measures.</summary>
        public Vec3? TextPosition { get; set; }

        public bool HasExternal => Refs.Any(r => !r.IsLocal);

        public override string ToString() => $"{Kind} {Name} = {Value} ({string.Join(", ", Refs)})";
    }

    public enum SkRefKind
    {
        /// <summary>A point of this sketch.</summary>
        Point,

        /// <summary>A segment of this sketch.</summary>
        Segment,

        /// <summary>The model origin as seen by the sketch.</summary>
        Origin,

        /// <summary>A point of another sketch (identified by sketch name and point id).</summary>
        OtherSketchPoint,

        /// <summary>A segment of another sketch.</summary>
        OtherSketchSegment,

        /// <summary>An edge of a solid body.</summary>
        ModelEdge,

        /// <summary>A vertex of a solid body.</summary>
        ModelVertex,

        /// <summary>A reference plane or planar face.</summary>
        Plane,

        /// <summary>Anything else; cannot be mapped.</summary>
        Unknown,
    }

    /// <summary>What a relation or dimension refers to.</summary>
    public sealed class SkRef
    {
        private SkRef(SkRefKind kind) => Kind = kind;

        public SkRefKind Kind { get; private set; }

        /// <summary>Local point/segment id, or the entity id inside <see cref="SketchName"/>.</summary>
        public string Id { get; private set; } = "";

        public string SketchName { get; private set; } = "";
        public EdgeSignature? Edge { get; private set; }
        public Vec3 Position { get; private set; }
        public Plane3 PlaneGeometry { get; private set; }

        /// <summary>Free text for reports (feature/entity name in the source model).</summary>
        public string Description { get; set; } = "";

        public bool IsLocal => Kind == SkRefKind.Point || Kind == SkRefKind.Segment;

        public static SkRef LocalPoint(string id) => new SkRef(SkRefKind.Point) { Id = id };
        public static SkRef LocalSegment(string id) => new SkRef(SkRefKind.Segment) { Id = id };
        public static SkRef Origin() => new SkRef(SkRefKind.Origin) { Position = Vec3.Zero, Description = "Ursprung" };
        public static SkRef OtherSketchPoint(string sketch, string id, Vec3 modelPosition) =>
            new SkRef(SkRefKind.OtherSketchPoint) { SketchName = sketch, Id = id, Position = modelPosition };
        public static SkRef OtherSketchSegment(string sketch, string id) =>
            new SkRef(SkRefKind.OtherSketchSegment) { SketchName = sketch, Id = id };
        public static SkRef ModelEdge(EdgeSignature e) => new SkRef(SkRefKind.ModelEdge) { Edge = e };
        public static SkRef ModelVertex(Vec3 p) => new SkRef(SkRefKind.ModelVertex) { Position = p };
        public static SkRef Plane(Plane3 p, string name) => new SkRef(SkRefKind.Plane) { PlaneGeometry = p, Description = name };
        public static SkRef Unknown(string description) => new SkRef(SkRefKind.Unknown) { Description = description };

        /// <summary>Mirrors the geometric payload of external references; local ids stay as they are.</summary>
        public SkRef Mirror(Reflection r)
        {
            var m = (SkRef)MemberwiseClone();
            switch (Kind)
            {
                case SkRefKind.Origin:
                case SkRefKind.ModelVertex:
                case SkRefKind.OtherSketchPoint:
                    m.Position = r.Apply(Position);
                    break;
                case SkRefKind.ModelEdge:
                    m.Edge = Edge?.Mirror(r);
                    break;
                case SkRefKind.Plane:
                    m.PlaneGeometry = r.Apply(PlaneGeometry);
                    break;
            }
            return m;
        }

        public override string ToString() => Kind switch
        {
            SkRefKind.Point => "P:" + Id,
            SkRefKind.Segment => "S:" + Id,
            SkRefKind.OtherSketchPoint => $"P:{Id}@{SketchName}",
            SkRefKind.OtherSketchSegment => $"S:{Id}@{SketchName}",
            _ => Kind + (Description.Length > 0 ? ":" + Description : ""),
        };
    }

    public static class SketchModelExtensions
    {
        public static double Distance2D(this SkPoint a, SkPoint b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }
}
