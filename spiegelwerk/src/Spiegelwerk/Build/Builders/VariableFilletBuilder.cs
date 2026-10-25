using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Variable radius fillet. The edges are selected with mark 1 like a constant fillet and
    /// created with FeatureFillet3 (type "variable radius"); how that method orders its radii
    /// and control points is not documented, so everything that matters is set afterwards on
    /// the feature data, under selection access – the model is then rolled back to before the
    /// fillet, and the fillet's own edges and vertices exist:
    ///  - vertex radii by position: each vertex of the new fillet edges gets the radius of the
    ///    mirrored vertex of the original,
    ///  - control points per edge: same number as in the original (SOLIDWORKS starts with three
    ///    per edge), position along the edge carried over – counted from the other end if the
    ///    rebuilt edge runs the other way (EdgeLocation.Map),
    ///  - transition, overflow, propagation as read.
    /// Radii are not changed by a mirror.
    /// </summary>
    internal sealed class VariableFilletBuilder : IFeatureBuilder
    {
        private const int ItemMark = 1;

        public bool Handles(FeatureRecord record) => record is VariableFilletRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (VariableFilletRecord)record;
            var notes = new List<string>();
            List<EdgeSignature> wantedEdges = r.Edges.Select(e => e.Mirror(ctx.Mirror)).ToList();
            List<VertexRadius> wantedVertices = r.Vertices.Select(v => new VertexRadius(ctx.Mirror.Apply(v.Point), v.Radius)).ToList();

            var found = new List<Edge>();
            foreach (EdgeSignature wanted in wantedEdges)
            {
                Edge? e = ctx.Topology.FindEdge(wanted, notes);
                if (e != null) found.Add(e);
            }
            if (found.Count == 0) return Fail("Keine der verrundeten Kanten wurde im Ziel gefunden.", notes);
            if (found.Count < wantedEdges.Count)
                notes.Add($"{wantedEdges.Count - found.Count} von {wantedEdges.Count} Kanten nicht gefunden; Verrundung nur an den übrigen.");

            // Radii in the order of the edge ends for the creation call – a reasonable guess
            // only; the exact values follow below, vertex by vertex.
            var ends = new List<Vec3>();
            foreach (Edge e in found)
            {
                EdgeSignature sig = TopologyCapture.Edge(e);
                if (sig.IsClosed) continue;
                foreach (Vec3 p in new[] { sig.Start, sig.End })
                    if (!ends.Any(x => x.AlmostEquals(p))) ends.Add(p);
            }
            double RadiusAt(Vec3 p) => wantedVertices.FirstOrDefault(v => v.Point.AlmostEquals(p))?.Radius ?? r.DefaultRadius;

            int options = (int)swFeatureFilletOptions_e.swFeatureFilletAttachEdges;
            if (r.Propagate) options |= (int)swFeatureFilletOptions_e.swFeatureFilletPropagate;
            if (r.CurvatureContinuous) options |= (int)swFeatureFilletOptions_e.swFeatureFilletCurvatureContinuous;

            ModelDoc2 doc = ctx.Doc;
            Feature? f = Create(doc, found, options, r, ends.Count > 0 ? ends.Select(RadiusAt).ToArray() : null)
                         ?? Create(doc, found, options, r, null);
            if (f == null) return Fail("SOLIDWORKS hat die variable Verrundung nicht erzeugt.", notes);

            // ---- exact values
            int verticesSet = 0, pointsSet = 0;
            var editNotes = new List<string>();
            bool ok = FeatureEdit.VariableFillet(doc, f, d =>
            {
                Try(() => d.DefaultRadius = r.DefaultRadius);
                Try(() => d.TransitionType = r.TransitionType);
                Try(() => d.OverflowType = r.OverflowType);
                Try(() => d.PropagateToTangentFaces = r.Propagate);
                Try(() => d.CurvatureContinuous = r.CurvatureContinuous);

                var edges = new List<(Edge edge, EdgeSignature sig)>();
                for (int i = 0; i < d.FilletEdgeCount; i++)
                    if (d.GetFilletEdgeAtIndex(i) is Edge e) edges.Add((e, TopologyCapture.Edge(e)));

                verticesSet = SetVertexRadii(d, edges, wantedVertices, r.DefaultRadius);
                pointsSet = SetControlPoints(d, r, wantedEdges, editNotes);
            });
            notes.AddRange(editNotes);
            if (!ok)
                notes.Add("Eckradien und Kontrollpunkte ließen sich nicht setzen; es gelten die Werte beim Anlegen.");
            else
            {
                if (verticesSet < wantedVertices.Count)
                    notes.Add($"{wantedVertices.Count - verticesSet} von {wantedVertices.Count} Eckradien nicht übernommen.");
                if (pointsSet < r.ControlPoints.Count)
                    notes.Add($"{r.ControlPoints.Count - pointsSet} von {r.ControlPoints.Count} Kontrollpunkten nicht übernommen.");
            }

            ctx.Register(r.Name, f);
            return Ok(f, $"Variable Verrundung, {found.Count} Kanten, {r.Vertices.Count} Eckradien, {r.ControlPoints.Count} Kontrollpunkte", notes);
        }

        private static Feature? Create(ModelDoc2 doc, List<Edge> edges, int options, VariableFilletRecord r, double[]? radii)
        {
            SwSelect.Clear(doc);
            for (int i = 0; i < edges.Count; i++) SwSelect.Select(doc, edges[i], ItemMark, i > 0);
            var f = doc.FeatureManager.FeatureFillet3(options, r.DefaultRadius, 0, 0,
                (int)swFeatureFilletType_e.swFeatureFilletType_VariableRadius, r.OverflowType,
                (int)swFeatureFilletProfileType_e.swFeatureFilletCircular,
                radii, null, null, null, null, null, null) as Feature;
            SwSelect.Clear(doc);
            return f;
        }

        /// <summary>
        /// Every vertex of the new fillet edges: the radius of the mirrored original vertex, or
        /// the default where the original had none – the creation call may have put a radius
        /// meant for another vertex there. Returns how many of the original's radii were set.
        /// </summary>
        private static int SetVertexRadii(VariableFilletFeatureData2 d, List<(Edge edge, EdgeSignature sig)> edges, List<VertexRadius> wanted,
            double defaultRadius)
        {
            var done = new List<Vec3>();
            int matched = 0;
            foreach (var (edge, _) in edges)
                foreach (Vertex v in new[] { edge.GetStartVertex(), edge.GetEndVertex() }.OfType<Vertex>())
                {
                    Vec3 p = TopologyCapture.Vertex(v);
                    if (done.Any(x => x.AlmostEquals(p))) continue;
                    done.Add(p);
                    VertexRadius? w = wanted.FirstOrDefault(x => x.Point.AlmostEquals(p));
                    double radius = w?.Radius ?? defaultRadius;
                    if (Try(() => d.SetRadius(v, radius)) && w != null) matched++;
                }
            return matched;
        }

        /// <summary>
        /// Control points edge by edge: the new fillet's control points on the edge matching
        /// original edge k take position and radius of the original's points on k. Only if the
        /// numbers agree – an extra or missing point would leave a radius nobody asked for.
        /// </summary>
        private static int SetControlPoints(VariableFilletFeatureData2 d, VariableFilletRecord r, List<EdgeSignature> wantedEdges, List<string> notes)
        {
            if (r.ControlPoints.Count == 0) return 0;
            double full = r.LocationsInPercent ? 100 : 1;

            var actual = new List<(int index, int edge, EdgeSignature sig)>();
            for (int j = 0; j < d.GetControlPointsCount(); j++)
            {
                d.GetControlPointRadiusAtIndex(j, out double _, out Edge? edge);
                if (edge == null) continue;
                EdgeSignature sig = TopologyCapture.Edge(edge);
                int k = TopologyMatcher.FindEdge(sig, wantedEdges).Index;
                if (k >= 0) actual.Add((j, k, sig));
            }

            int set = 0;
            foreach (var group in r.ControlPoints.GroupBy(c => c.Edge))
            {
                var mine = actual.Where(a => a.edge == group.Key).ToList();
                var theirs = group.ToList();
                if (mine.Count != theirs.Count)
                {
                    notes.Add($"Kante {group.Key + 1}: {theirs.Count} Kontrollpunkte im Original, {mine.Count} in der Kopie – nicht übernommen.");
                    continue;
                }
                for (int i = 0; i < theirs.Count; i++)
                {
                    double? location = EdgeLocation.Map(wantedEdges[group.Key], mine[i].sig, theirs[i].Location, full);
                    if (location == null)
                    {
                        string why = wantedEdges[group.Key].IsClosed || mine[i].sig.IsClosed ? "geschlossene Kante" : "Kantenenden passen nicht";
                        notes.Add($"Kante {group.Key + 1}: Lage eines Kontrollpunkts nicht übertragbar ({why}).");
                        continue;
                    }
                    int index = mine[i].index;
                    double radius = theirs[i].Radius;
                    if (Try(() => d.SetControlPointRadiusAtIndex(index, location.Value, radius))) set++;
                }
            }
            return set;
        }

        /// <summary>A single refused value must not throw away the others.</summary>
        private static bool Try(Action set)
        {
            try
            {
                set();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
