using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Build
{
    /// <summary>
    /// Finds edges, faces and vertices of the rebuilt part by mirrored fingerprint. The
    /// candidate list is cached and thrown away whenever a feature is added. Bodies created
    /// by this job are preferred, which matters in the same part where an edge on the mirror
    /// plane exists twice (once on the original, once on the copy).
    /// </summary>
    internal sealed class TopologyFinder
    {
        private readonly BuildContext _ctx;
        private List<(Edge edge, EdgeSignature sig, bool own)>? _edges;
        private List<(Face2 face, FaceSignature sig, bool own)>? _faces;

        public TopologyFinder(BuildContext ctx)
        {
            _ctx = ctx;
        }

        public void Invalidate()
        {
            _edges = null;
            _faces = null;
        }

        private void Load()
        {
            if (_edges != null) return;
            _edges = new List<(Edge, EdgeSignature, bool)>();
            _faces = new List<(Face2, FaceSignature, bool)>();
            foreach (Body2 body in Sw.SwDoc.SolidBodies(_ctx.Doc))
            {
                bool own = _ctx.IsOwnBody(body);
                if (body.GetEdges() is object[] edges)
                    foreach (Edge e in edges.OfType<Edge>()) _edges.Add((e, TopologyCapture.Edge(e), own));
                if (body.GetFaces() is object[] faces)
                    foreach (Face2 f in faces.OfType<Face2>()) _faces.Add((f, TopologyCapture.Face(f), own));
            }
        }

        /// <param name="mirrored">Signature already mirrored into the target.</param>
        public Edge? FindEdge(EdgeSignature mirrored, List<string> notes)
        {
            Load();
            List<(Edge edge, EdgeSignature sig, bool own)> edges = _edges!;
            MatchResult m = TopologyMatcher.FindEdge(mirrored, edges.Select(e => e.sig).ToList(), i => edges[i].own,
                (i, p) => DistanceToEdge(edges[i].edge, p));
            if (!m.Found) return null;
            if (m.Ambiguous) notes.Add($"Kante {mirrored}: {m.Candidates} Kandidaten, die passendste wurde gewählt.");
            return edges[m.Index].edge;
        }

        private static double DistanceToEdge(Edge e, Vec3 p)
        {
            var c = e.GetClosestPointOn(p.X, p.Y, p.Z) as double[];
            return c == null || c.Length < 3 ? double.PositiveInfinity : Vec3.FromArray(c).DistanceTo(p);
        }

        public Face2? FindFace(FaceSignature mirrored, List<string> notes)
        {
            Load();
            List<(Face2 face, FaceSignature sig, bool own)> faces = _faces!;
            MatchResult m = TopologyMatcher.FindFace(mirrored, faces.Select(f => f.sig).ToList(),
                i => TopologyCapture.DistanceToFace(faces[i].face, mirrored.Sample), Tol.Length, i => faces[i].own);
            if (!m.Found) return null;
            if (m.Ambiguous) notes.Add($"Fläche {mirrored}: {m.Candidates} Kandidaten, die passendste wurde gewählt.");
            return faces[m.Index].face;
        }

        /// <summary>A vertex at the given point; on a body this job created, if there is one there too.</summary>
        public Vertex? FindVertex(Vec3 mirrored)
        {
            Vertex? fallback = null;
            foreach (Body2 body in Sw.SwDoc.SolidBodies(_ctx.Doc))
            {
                bool own = _ctx.IsOwnBody(body);
                if (body.GetVertices() is object[] vs)
                    foreach (Vertex v in vs.OfType<Vertex>())
                        if (TopologyCapture.Vertex(v).AlmostEquals(mirrored))
                        {
                            if (own) return v;
                            fallback ??= v;
                        }
            }
            return fallback;
        }

        /// <summary>A linear edge of the target lying on the given line, preferring bodies this job created.</summary>
        public Edge? FindLinearEdgeOn(Line3 line, bool ownOnly = false)
        {
            Load();
            Edge? fallback = null;
            foreach (var (edge, sig, own) in _edges!)
                if (sig.Kind == CurveKind.Line && line.DistanceTo(sig.Start) <= Tol.Length && line.DistanceTo(sig.End) <= Tol.Length)
                {
                    if (own) return edge;
                    if (!ownOnly) fallback ??= edge;
                }
            return fallback;
        }

        /// <summary>
        /// Resolves a reference of the original (already mirrored) to an object of the target:
        /// a mapped sketch entity, a plane, or an edge/face/vertex found by fingerprint.
        /// </summary>
        public object? Resolve(GeometryRef mirrored, List<string> notes)
        {
            switch (mirrored.Kind)
            {
                case GeometryRefKind.Face:
                {
                    Face2? face = FindFace(mirrored.Face!, notes);
                    if (face != null) return face;
                    // A planar face can be replaced by a plane through it: independent and equivalent for up-to conditions.
                    if (mirrored.Face!.Kind == SurfaceKind.Plane)
                    {
                        notes.Add("Bezugsfläche nicht gefunden, es wird eine Ebene durch die gespiegelte Fläche verwendet.");
                        return _ctx.Planes.Resolve(new Plane3(mirrored.Face.Sample, mirrored.Face.Normal));
                    }
                    return null;
                }
                case GeometryRefKind.Plane:
                    return _ctx.Planes.Resolve(mirrored.Plane);
                case GeometryRefKind.Edge:
                    return FindEdge(mirrored.Edge!, notes);
                case GeometryRefKind.Vertex:
                    return FindVertex(mirrored.Point);
                case GeometryRefKind.SketchPoint:
                    return _ctx.Sketches.TryGetValue(mirrored.SketchName, out SketchMap sp) && sp.Points.TryGetValue(mirrored.EntityId, out SketchPoint p) ? p : null;
                case GeometryRefKind.SketchSegment:
                    return _ctx.Sketches.TryGetValue(mirrored.SketchName, out SketchMap ss) && ss.Segments.TryGetValue(mirrored.EntityId, out SketchSegment s) ? s : null;
                case GeometryRefKind.Axis:
                    return FindLineObject(mirrored.Axis) ?? HelperAxis(mirrored.Axis, notes);
                case GeometryRefKind.Sketch:
                    return _ctx.Sketches.TryGetValue(mirrored.SketchName, out SketchMap sk) ? sk.Feature : null;
                default:
                    return null;
            }
        }

        /// <summary>Nothing lies on the line: a helper axis, so the feature depends on nothing of the original.</summary>
        private object? HelperAxis(Line3 line, List<string> notes)
        {
            try
            {
                return _ctx.Planes.Axis(line);
            }
            catch (System.InvalidOperationException ex)
            {
                notes.Add(ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Something selectable that lies on a line: an axis of this job, a rebuilt sketch line,
        /// a model edge. In the same part only edges of the mirrored bodies count – an edge of the
        /// original would tie the copy to it again.
        /// </summary>
        public object? FindLineObject(Line3 line)
        {
            if (_ctx.Planes.FindAxis(line) is Feature axis) return axis;
            foreach (SketchMap map in _ctx.Sketches.Values)
                foreach (var seg in map.Mirrored.Sketch.Segments)
                {
                    if (seg.Kind != Core.Sketch.SkSegmentKind.Line || seg.StartId == null || seg.EndId == null) continue;
                    Vec3 a = map.TargetModelPoint(seg.StartId), b = map.TargetModelPoint(seg.EndId);
                    if (line.DistanceTo(a) <= Tol.Length && line.DistanceTo(b) <= Tol.Length && map.Segments.TryGetValue(seg.Id, out SketchSegment s))
                        return s;
                }
            return FindLinearEdgeOn(line, _ctx.SamePart);
        }
    }
}
