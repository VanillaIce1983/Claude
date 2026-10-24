using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;

namespace Spiegelwerk.Core.Records
{
    public enum GeometryRefKind
    {
        Face,
        Plane,
        Edge,
        Vertex,
        SketchPoint,
        SketchSegment,
        Axis,

        /// <summary>A whole sketch (profile, path or guide curve of a sweep or loft).</summary>
        Sketch,
        Unknown,
    }

    /// <summary>
    /// A reference a feature makes to other geometry (up-to surface, revolve axis, start
    /// face ...), stored as geometry rather than as a SOLIDWORKS object so it can be mirrored
    /// and found again in the target document.
    /// </summary>
    public sealed class GeometryRef
    {
        private GeometryRef(GeometryRefKind kind) => Kind = kind;

        public GeometryRefKind Kind { get; private set; }
        public FaceSignature? Face { get; private set; }
        public EdgeSignature? Edge { get; private set; }
        public Plane3 Plane { get; private set; }
        public Vec3 Point { get; private set; }
        public Line3 Axis { get; private set; }
        public string SketchName { get; private set; } = "";
        public string EntityId { get; private set; } = "";
        public string Description { get; private set; } = "";

        public static GeometryRef ToFace(FaceSignature f) => new GeometryRef(GeometryRefKind.Face) { Face = f };
        public static GeometryRef ToPlane(Plane3 p, string name) => new GeometryRef(GeometryRefKind.Plane) { Plane = p, Description = name };
        public static GeometryRef ToEdge(EdgeSignature e) => new GeometryRef(GeometryRefKind.Edge) { Edge = e };
        public static GeometryRef ToVertex(Vec3 p) => new GeometryRef(GeometryRefKind.Vertex) { Point = p };
        public static GeometryRef ToSketchPoint(string sketch, string id, Vec3 p) =>
            new GeometryRef(GeometryRefKind.SketchPoint) { SketchName = sketch, EntityId = id, Point = p };
        public static GeometryRef ToSketchSegment(string sketch, string id) =>
            new GeometryRef(GeometryRefKind.SketchSegment) { SketchName = sketch, EntityId = id };
        public static GeometryRef ToAxis(Line3 l, string name) => new GeometryRef(GeometryRefKind.Axis) { Axis = l, Description = name };
        public static GeometryRef ToSketch(string sketch) => new GeometryRef(GeometryRefKind.Sketch) { SketchName = sketch };
        public static GeometryRef Unknown(string description) => new GeometryRef(GeometryRefKind.Unknown) { Description = description };

        public GeometryRef Mirror(Reflection r)
        {
            var m = (GeometryRef)MemberwiseClone();
            m.Face = Face?.Mirror(r);
            m.Edge = Edge?.Mirror(r);
            m.Plane = Kind == GeometryRefKind.Plane ? r.Apply(Plane) : Plane;
            m.Point = r.Apply(Point);
            m.Axis = Kind == GeometryRefKind.Axis ? r.Apply(Axis) : Axis;
            return m;
        }

        public override string ToString() => Kind switch
        {
            GeometryRefKind.SketchPoint or GeometryRefKind.SketchSegment => $"{Kind} {EntityId}@{SketchName}",
            GeometryRefKind.Sketch => $"Skizze \"{SketchName}\"",
            GeometryRefKind.Face => $"Fläche {Face}",
            GeometryRefKind.Edge => $"Kante {Edge}",
            _ => $"{Kind} {Description}",
        };
    }
}
