using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Sketch;

namespace Spiegelwerk.Capture
{
    /// <summary>
    /// Sketch regions (ISketchRegion) → <see cref="ProfileRegion"/>, on both sides: the selected
    /// regions of the original, all regions of the mirrored sketch, and the regions a created
    /// feature reports. A region has no sketch segments, only B-rep loops whose edges are
    /// portions of segments; they are sampled with SOLIDWORKS' own curves (Evaluate2), so
    /// splines and ellipses need no evaluator in Core. The coordinate space of these edges is
    /// not documented and is decided per region (<see cref="RegionSpace"/>), never assumed.
    /// Nothing here throws: a region that cannot be read gives a reason instead, and the
    /// caller falls back (one unreadable region must not end the run).
    /// </summary>
    internal static class RegionCapture
    {
        /// <summary>Evaluations per edge for the coarse pass that sizes ε.</summary>
        private const int CoarseSteps = 8;

        /// <summary>Upper bound on the loops of one region, should GetNext never return null.</summary>
        private const int MaxLoops = 10000;

        /// <summary>What SOLIDWORKS returns for one region, in a space still to be decided.</summary>
        private sealed class RawRegion
        {
            /// <summary>
            /// The edges, as the loops give them and – if that is a different set – as the region's
            /// own GetEdges does; the first set that forms a closed boundary agreeing with the
            /// kernel's area is used.
            /// </summary>
            public List<List<(Curve Curve, double U0, double U1)>> EdgeSets { get; } = new List<List<(Curve, double, double)>>();

            /// <summary>Loop vertices and the centres of closed circular edges (for the vote on the coordinate space).</summary>
            public List<Vec3> Vertices { get; } = new List<Vec3>();

            /// <summary>A few points per edge, for the extent.</summary>
            public List<Vec3> Coarse { get; } = new List<Vec3>();

            public double? KernelArea { get; set; }
        }

        /// <summary>
        /// The selected regions of the original: one ε for all of them (the smallest, from a
        /// coarse pass over each), every region with witnesses. Null if any of them cannot be
        /// read – the caller then keeps none (all or nothing).
        /// </summary>
        public static List<ProfileRegion>? ReadSelected(IReadOnlyList<SketchRegion> regions, Frame3 frame, IReadOnlyList<Vec3> sketchPoints,
            RegionSpaceMemory spaces, out double eps, out string? why)
        {
            eps = 0;
            var raws = new List<RawRegion>();
            foreach (SketchRegion region in regions)
            {
                RawRegion? raw = ReadRaw(region, out why);
                if (raw == null) return null;
                raws.Add(raw);
            }
            eps = raws.Min(r => RegionGeometry.ChordTolerance(r.Coarse));
            var result = new List<ProfileRegion>();
            for (int i = 0; i < raws.Count; i++)
            {
                ProfileRegion? shape = Shape(raws[i], frame, eps, sketchPoints, spaces, RegionGeometry.DefaultWitnesses, true, out why);
                if (shape == null)
                {
                    if (raws.Count > 1) why = $"Skizzenbereich {i + 1}: {why}";
                    return null;
                }
                result.Add(shape);
            }
            why = null;
            return result;
        }

        /// <summary>
        /// All regions of a sketch with the given ε. Regions that cannot be read are left out
        /// and counted: a witness that lies in one of them finds no region, which is a failed
        /// match, never a wrong one.
        /// </summary>
        public static List<(SketchRegion Region, ProfileRegion Shape)> ReadAll(Sketch sketch, Frame3 frame, double eps, RegionSpaceMemory spaces,
            out int unreadable, out string? firstWhy)
        {
            unreadable = 0;
            firstWhy = null;
            var result = new List<(SketchRegion, ProfileRegion)>();
            IReadOnlyList<Vec3> points = SketchPoints(sketch);
            object[] regions = (object)sketch.GetSketchRegions() as object[] ?? Array.Empty<object>();
            foreach (SketchRegion region in regions.OfType<SketchRegion>())
            {
                ProfileRegion? shape = Read(region, frame, eps, points, spaces, true, out string? why);
                if (shape != null)
                {
                    result.Add((region, shape));
                }
                else
                {
                    unreadable++;
                    firstWhy ??= why;
                }
            }
            return result;
        }

        /// <summary>
        /// One region with a given ε. Witnesses are searched if asked for but not required: a
        /// narrow region of the copy is still a region (only selected regions of the original
        /// need them); the read-back after creation needs only the boundary.
        /// </summary>
        public static ProfileRegion? Read(SketchRegion region, Frame3 frame, double eps, IReadOnlyList<Vec3> sketchPoints, RegionSpaceMemory spaces,
            bool witnesses, out string? why)
        {
            RawRegion? raw = ReadRaw(region, out why);
            return raw == null ? null : Shape(raw, frame, eps, sketchPoints, spaces, witnesses ? RegionGeometry.DefaultWitnesses : 0, false, out why);
        }

        /// <summary>Points of a 2D sketch in sketch coordinates (GetSketchPoints2: also end and centre points of segments).</summary>
        public static IReadOnlyList<Vec3> SketchPoints(Sketch sketch)
        {
            try
            {
                object[] points = (object)sketch.GetSketchPoints2() as object[] ?? Array.Empty<object>();
                return points.OfType<SketchPoint>().Select(p => new Vec3(p.X, p.Y, 0)).ToList();
            }
            catch (Exception)
            {
                // Only used for a vote; without points the vote stays undecided.
                return Array.Empty<Vec3>();
            }
        }

        /// <summary>
        /// Edges, vertices and the kernel's area of a region. Its loops (outer and holes) come from
        /// GetFirstLoop/GetNext, as in the API example "Get Sketch Regions"; the region's own
        /// GetEdges is the second edge set, should the loops not be exactly the region's. The
        /// loops' face gives the area the kernel computes.
        /// </summary>
        private static RawRegion? ReadRaw(SketchRegion region, out string? why)
        {
            why = null;
            try
            {
                var raw = new RawRegion();
                var loopEdges = new List<Edge>();
                Face2? face = null;
                Loop2? loop = region.GetFirstLoop();
                for (int n = 0; loop != null && n < MaxLoops; n++)
                {
                    face ??= (object)loop.GetFace() as Face2;
                    if ((object)loop.GetEdges() is object[] le) loopEdges.AddRange(le.OfType<Edge>());
                    if ((object)loop.GetVertices() is object[] lv) raw.Vertices.AddRange(lv.OfType<Vertex>().Select(TopologyCapture.Vertex));
                    loop = (object)loop.GetNext() as Loop2;
                }
                List<Edge> regionEdges = RegionEdges(region);
                if (loopEdges.Count > 0) raw.EdgeSets.Add(Params(loopEdges, raw));
                if (regionEdges.Count > 0 && regionEdges.Count != loopEdges.Count) raw.EdgeSets.Add(Params(regionEdges, raw));
                if (raw.EdgeSets.Count == 0)
                {
                    why = "der Bereich liefert keine Kanten";
                    return null;
                }
                if (face != null)
                {
                    double area = face.GetArea();
                    if (area > 0) raw.KernelArea = area;
                }
                return raw;
            }
            catch (Exception ex)
            {
                why = $"Kanten des Bereichs nicht lesbar ({ex.Message})";
                return null;
            }
        }

        private static List<Edge> RegionEdges(SketchRegion region)
        {
            try
            {
                return (object)region.GetEdges() is object[] edges ? edges.OfType<Edge>().ToList() : new List<Edge>();
            }
            catch (Exception)
            {
                // The loops may still do.
                return new List<Edge>();
            }
        }

        /// <summary>Curve and trimmed parameter range of each edge; the coarse pass and circle centres on the way (first set only).</summary>
        private static List<(Curve Curve, double U0, double U1)> Params(List<Edge> edges, RawRegion raw)
        {
            bool first = raw.EdgeSets.Count == 0;
            var result = new List<(Curve, double, double)>();
            foreach (Edge e in edges)
            {
                CurveParamData cp = e.GetCurveParams3();
                var curve = (Curve)(object)e.GetCurve();
                double u0 = cp.UMinValue, u1 = cp.UMaxValue;
                result.Add((curve, u0, u1));
                if (!first) continue;
                for (int k = 0; k <= CoarseSteps; k++) raw.Coarse.Add(Evaluate(curve, u0 + (u1 - u0) * k / CoarseSteps));
                // A full circle has no vertex; its centre stands in for one in the vote.
                if (curve.IsCircle() && Evaluate(curve, u0).DistanceTo(Evaluate(curve, u1)) <= Tol.Length)
                    raw.Vertices.Add(Vec3.FromArray((double[])(object)curve.CircleParams, 0));
            }
            return result;
        }

        /// <summary>The first edge set that gives a valid region; otherwise why the first did not.</summary>
        private static ProfileRegion? Shape(RawRegion raw, Frame3 frame, double eps, IReadOnlyList<Vec3> sketchPoints, RegionSpaceMemory spaces,
            int witnesses, bool requireWitnesses, out string? why)
        {
            why = null;
            foreach (var edges in raw.EdgeSets)
            {
                ProfileRegion? region = Shape(edges, raw, frame, eps, sketchPoints, spaces, witnesses, requireWitnesses, out string? w);
                if (region != null) return region;
                why ??= w;
            }
            return null;
        }

        /// <summary>
        /// Samples the edges with ε, decides their coordinate space, welds the pieces into a
        /// closed segment set and checks it against the kernel's area.
        /// </summary>
        private static ProfileRegion? Shape(List<(Curve Curve, double U0, double U1)> edges, RawRegion raw, Frame3 frame, double eps,
            IReadOnlyList<Vec3> sketchPoints, RegionSpaceMemory spaces, int witnesses, bool requireWitnesses, out string? why)
        {
            why = null;
            try
            {
                var pieces = edges.Select(e => RegionGeometry.Sample(u => Evaluate(e.Curve, u), e.U0, e.U1, eps)).ToList();
                List<Vec3> all = pieces.SelectMany(p => p).ToList();
                RegionSpaceDecision decision = RegionSpace.Decide(all, frame, raw.Vertices, sketchPoints.ToList(), spaces.Known, eps);
                spaces.Learn(decision);
                if (!decision.Space.HasValue)
                {
                    why = decision.Outcome == RegionSpaceOutcome.Error
                        ? "die Kanten des Bereichs liegen weder in Modell- noch in Skizzenkoordinaten in der Skizzenebene"
                        : "ob SOLIDWORKS die Kanten des Bereichs in Modell- oder in Skizzenkoordinaten liefert, lässt sich hier nicht entscheiden";
                    return null;
                }
                RawSpace space = decision.Space.Value;
                List<IReadOnlyList<Vec3>> flat = pieces.Select(p => (IReadOnlyList<Vec3>)p.Select(q => RegionSpace.ToSketch(q, space, frame)).ToList()).ToList();

                List<RegionSegment>? segments = RegionGeometry.Weld(flat, RegionGeometry.WeldTolerance(eps));
                if (segments == null)
                {
                    why = "der Rand des Bereichs ist nicht geschlossen";
                    return null;
                }
                double polyline = RegionGeometry.Area(segments);
                if (double.IsNaN(polyline) || polyline <= 0)
                {
                    why = "der Rand des Bereichs umschließt keine Fläche";
                    return null;
                }
                if (raw.KernelArea.HasValue && !RegionGeometry.AreaAgrees(polyline, raw.KernelArea.Value, eps, RegionGeometry.Perimeter(segments)))
                {
                    why = $"die Fläche des Bereichs ({raw.KernelArea.Value * 1e6:0.###} mm²) passt nicht zu seinen Kanten ({polyline * 1e6:0.###} mm²)";
                    return null;
                }
                ProfileRegion region = ProfileRegion.Create(segments, eps, raw.KernelArea, witnesses);
                if (requireWitnesses && region.Witnesses.Count == 0)
                {
                    why = "der Bereich ist zu schmal für einen sicheren Punkt in seinem Inneren";
                    return null;
                }
                return region;
            }
            catch (Exception ex)
            {
                why = $"Bereich nicht lesbar ({ex.Message})";
                return null;
            }
        }

        private static Vec3 Evaluate(Curve curve, double u) => Vec3.FromArray((double[])(object)curve.Evaluate2(u, 0));
    }
}
