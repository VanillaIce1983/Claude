using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Sketch;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// A sketch that belongs to one edge of a flange or hem (an edge-flange profile, a hem width
    /// sketch) and what tells its edge: its lines and, where SOLIDWORKS names it, the edge it was
    /// made on (ISketch.GetReferenceEntity, the edge flange's evidence from S3 on).
    /// </summary>
    public sealed class ProfileCandidate
    {
        public ProfileCandidate(string name, SketchModel sketch, EdgeSignature? supportEdge = null)
        {
            Name = name;
            Sketch = sketch;
            SupportEdge = supportEdge;
        }

        public string Name { get; }
        public SketchModel Sketch { get; }
        public EdgeSignature? SupportEdge { get; }
    }

    /// <summary>
    /// Sketches that belong to the edges of a flange or hem (A2 §3): which sketch belongs to which
    /// edge, and whether a hem's width sketch is the default one (the whole edge). Pure geometry in
    /// model coordinates, so the same rules hold for the original and – mirrored – for the copy.
    /// </summary>
    public static class FlangeProfiles
    {
        /// <summary>The non-construction lines of a sketch, as model-space end points.</summary>
        public static List<(Vec3 a, Vec3 b)> Lines(SketchModel s) =>
            s.Segments.Where(g => g.Kind == SkSegmentKind.Line && !g.Construction && g.StartId != null && g.EndId != null)
                .Select(g => (s.ToModel(s.Point(g.StartId!)), s.ToModel(s.Point(g.EndId!))))
                .ToList();

        /// <summary>
        /// A sketch line lies on a straight edge: both its ends on the edge's line and the two
        /// overlap by more than <paramref name="tol"/> (touching at one end is no overlap).
        /// </summary>
        public static bool LiesOn((Vec3 a, Vec3 b) line, EdgeSignature edge, double tol = Tol.Length)
        {
            if (edge.Kind != CurveKind.Line || edge.Length <= tol) return false;
            Vec3 d = (edge.End - edge.Start).Normalized();
            var axis = new Line3(edge.Start, d);
            if (axis.DistanceTo(line.a) > tol || axis.DistanceTo(line.b) > tol) return false;
            double ta = (line.a - edge.Start).Dot(d), tb = (line.b - edge.Start).Dot(d);
            double from = Math.Max(Math.Min(ta, tb), 0), to = Math.Min(Math.Max(ta, tb), edge.Length);
            return to - from > tol;
        }

        /// <summary>
        /// The default width sketch of a hem: exactly one non-construction segment, a line from end
        /// to end of the (straight) edge, either way round. Construction geometry does not count.
        /// </summary>
        public static bool IsFullEdgeLine(SketchModel s, EdgeSignature edge, double tol = Tol.Length)
        {
            if (edge.Kind != CurveKind.Line) return false;
            List<SkSegment> drawn = s.Segments.Where(g => !g.Construction).ToList();
            if (drawn.Count != 1) return false;
            List<(Vec3 a, Vec3 b)> lines = Lines(s);
            if (lines.Count != 1) return false;
            (Vec3 a, Vec3 b) = lines[0];
            return (a.AlmostEquals(edge.Start, tol) && b.AlmostEquals(edge.End, tol)) ||
                   (a.AlmostEquals(edge.End, tol) && b.AlmostEquals(edge.Start, tol));
        }

        /// <summary>
        /// Pairs sketches with edges: for each candidate the index of its edge, or null when a
        /// candidate has none or the pairing is not unique. Evidence per candidate: the edge it was
        /// made on (if given and found among the edges), else every edge one of its lines lies on.
        /// Candidates with a single edge are settled first; that edge is then taken from the others.
        /// Each edge takes at most one candidate; edges may stay without one.
        /// </summary>
        public static int[]? Assign(IReadOnlyList<EdgeSignature> edges, IReadOnlyList<ProfileCandidate> profiles, double tol = Tol.Length)
        {
            var candidates = new List<HashSet<int>>();
            foreach (ProfileCandidate p in profiles)
            {
                var set = new HashSet<int>();
                if (p.SupportEdge != null)
                    for (int i = 0; i < edges.Count; i++)
                        if (!double.IsPositiveInfinity(edges[i].DistanceTo(p.SupportEdge, null, tol)))
                            set.Add(i);
                if (set.Count == 0)
                {
                    List<(Vec3 a, Vec3 b)> lines = Lines(p.Sketch);
                    for (int i = 0; i < edges.Count; i++)
                        if (lines.Any(l => LiesOn(l, edges[i], tol)))
                            set.Add(i);
                }
                if (set.Count == 0) return null;
                candidates.Add(set);
            }

            var result = Enumerable.Repeat(-1, profiles.Count).ToArray();
            for (int settled = 0; settled < profiles.Count; settled++)
            {
                int next = -1;
                for (int k = 0; k < profiles.Count && next < 0; k++)
                    if (result[k] < 0 && candidates[k].Count == 1)
                        next = k;
                // Every open candidate still has several edges: not unique.
                if (next < 0) return null;
                int edge = candidates[next].Single();
                result[next] = edge;
                for (int k = 0; k < profiles.Count; k++)
                {
                    if (result[k] >= 0) continue;
                    candidates[k].Remove(edge);
                    // Its only edge belongs to another one.
                    if (candidates[k].Count == 0) return null;
                }
            }
            return result;
        }
    }
}
