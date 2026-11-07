using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Records;
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
        /// made on (if given and found among the edges), else every edge one of its lines lies on,
        /// else – only with <paramref name="byFrame"/>, for edge-flange profiles that a position
        /// offset moved off their edge (A2 #8) – the edges its frame fits (<see cref="FrameFitsEdge"/>)
        /// that lie nearest to its plane: a frame cannot tell parallel edges apart that differ only along
        /// its normal, and a position offset is small next to the width of a sheet (live test B31).
        /// Candidates with a single edge are settled first; that edge is then taken from the others.
        /// Each edge takes at most one candidate; edges may stay without one.
        /// </summary>
        public static int[]? Assign(IReadOnlyList<EdgeSignature> edges, IReadOnlyList<ProfileCandidate> profiles, double tol = Tol.Length, bool byFrame = false)
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
                if (set.Count == 0 && byFrame)
                {
                    // Parallel edges in the direction of the plane's normal all fit a frame: the nearest ones count.
                    List<(int i, double off)> fits = Enumerable.Range(0, edges.Count).Where(i => FrameFitsEdge(p.Sketch, edges[i]))
                        .Select(i => (i, NormalOffset(p.Sketch.Frame, edges[i]).Length)).ToList();
                    if (fits.Count > 0)
                    {
                        double nearest = fits.Min(x => x.off);
                        foreach (var (i, off) in fits)
                            if (off <= nearest + tol)
                                set.Add(i);
                    }
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

        // ---------------------------------------------------------------- edge flange (S3, A2 P1, P2, P16)

        /// <summary>Directions within this of 1 (cosine) count as parallel: about 1.4e-4 rad.</summary>
        private const double AxisTolerance = 1e-8;

        /// <summary>Unit direction of a straight edge, start → end (the B-rep direction).</summary>
        public static Vec3 Along(EdgeSignature e) => (e.End - e.Start).Normalized();

        /// <summary>
        /// A profile drawn on <paramref name="hinge"/> as SOLIDWORKS makes one: its plane parallel to the
        /// edge, its X axis along it, and – rebased onto the edge (<see cref="Rebase"/>: a position offset
        /// moves the profile off it along its normal) – one of its lines on the edge. The third evidence
        /// of <see cref="Assign"/>, for profiles whose final state no longer touches their edge.
        /// </summary>
        public static bool FrameFitsEdge(SketchModel s, EdgeSignature hinge)
        {
            if (hinge.Kind != CurveKind.Line || hinge.Length <= Tol.Length) return false;
            Vec3 t = Along(hinge);
            if (Math.Abs(s.Frame.ZAxis.Dot(t)) > AxisTolerance || Math.Abs(s.Frame.XAxis.Dot(t)) < 1 - AxisTolerance) return false;
            return Lines(Rebase(s, hinge)).Any(l => LiesOn(l, hinge));
        }

        /// <summary>
        /// The points that describe a profile, in model coordinates (A2 P16): the defining points of
        /// every non-construction segment (ends, centres, axis and fit points), the middle of each arc
        /// and, for a circle, the four points at ±r along <paramref name="axis"/> and across it in the
        /// plane (default: the sketch's X axis) – so a circle's size counts, not only its centre. R maps
        /// the set onto the copy's as long as the axis maps onto ± the copy's; the hinge does. Compared
        /// as a set (<see cref="InPlaneDeviation"/>).
        /// </summary>
        public static List<Vec3> ProfilePoints(SketchModel s, Vec3? axis = null)
        {
            Frame3 f = s.Frame;
            Vec3 u = axis ?? f.XAxis;
            u = u - f.ZAxis * u.Dot(f.ZAxis);
            u = u.Length > Tol.Length ? u.Normalized() : f.XAxis;
            Vec3 v = f.ZAxis.Cross(u);
            var points = new List<Vec3>();
            foreach (SkSegment g in s.Segments.Where(g => !g.Construction))
            {
                foreach (string id in g.PointIds())
                    if (s.TryPoint(id) is SkPoint p)
                        points.Add(s.ToModel(p));
                if (g.Kind == SkSegmentKind.Arc && s.TryPoint(g.CenterId) is SkPoint c && s.TryPoint(g.StartId) is SkPoint a && s.TryPoint(g.EndId) is SkPoint b)
                    points.Add(f.ToWorld(ArcMiddle(c, a, b, g.Direction)));
                else if (g.Kind == SkSegmentKind.Circle && s.TryPoint(g.CenterId) is SkPoint centre)
                {
                    Vec3 m = s.ToModel(centre);
                    foreach (Vec3 d in new[] { u, -u, v, -v }) points.Add(m + d * g.Radius);
                }
            }
            return points;
        }

        /// <summary>The middle of an arc in sketch coordinates (direction +1 counter-clockwise about the sketch normal).</summary>
        private static Vec3 ArcMiddle(SkPoint c, SkPoint a, SkPoint b, int direction)
        {
            double a0 = Math.Atan2(a.Y - c.Y, a.X - c.X), a1 = Math.Atan2(b.Y - c.Y, b.X - c.X);
            int dir = direction >= 0 ? 1 : -1;
            double sweep = dir > 0 ? a1 - a0 : a0 - a1;
            while (sweep <= 0) sweep += 2 * Math.PI;
            double mid = a0 + dir * sweep / 2;
            double r = Math.Sqrt((a.X - c.X) * (a.X - c.X) + (a.Y - c.Y) * (a.Y - c.Y));
            return new Vec3(c.X + r * Math.Cos(mid), c.Y + r * Math.Sin(mid), 0);
        }

        /// <summary>
        /// The direction of the flange in its profile: the unit vector in the profile's plane, square to
        /// the hinge, from the hinge toward the centre of the profile points. A geometric vector – R maps
        /// it onto the copy's, no sign convention involved (A2 §6). Null without points or if their centre
        /// sits on the hinge line.
        /// </summary>
        public static Vec3? Direction(SketchModel profile, EdgeSignature hinge)
        {
            if (hinge.Kind != CurveKind.Line || hinge.Length <= Tol.Length) return null;
            List<Vec3> points = ProfilePoints(profile);
            if (points.Count == 0) return null;
            Vec3 centre = points.Aggregate(Vec3.Zero, (sum, p) => sum + p) / points.Count;
            Vec3 n = profile.Frame.ZAxis, t = Along(hinge);
            Vec3 d = centre - hinge.Start;
            d = d - t * d.Dot(t);
            d = d - n * d.Dot(n);
            return d.Length <= Tol.Length ? (Vec3?)null : d.Normalized();
        }

        /// <summary>
        /// How far a profile's plane stands off its hinge: the vector along the plane's normal from the
        /// hinge (its middle) to the plane. A position offset moves the profile so (tbcphutai); a profile
        /// on its edge gives zero. Free of the frame's normal sign: R maps the original's vector onto what
        /// the copy's must be (A2 P2, part c of the profile check).
        /// </summary>
        public static Vec3 NormalOffset(Frame3 frame, EdgeSignature hinge)
        {
            Vec3 n = frame.ZAxis;
            Vec3 middle = (hinge.Start + hinge.End) / 2;
            return n * (frame.Origin - middle).Dot(n);
        }

        /// <summary>
        /// The profile moved back onto its hinge (A2 P2): the frame shifted along its normal by
        /// −<see cref="NormalOffset"/>, so its plane contains the hinge again; the 2D content, the
        /// relations and the dimensions stay as they are. This is what the new flange's sketch takes –
        /// SOLIDWORKS moves it by the position offset itself.
        /// </summary>
        public static SketchModel Rebase(SketchModel s, EdgeSignature hinge)
        {
            Frame3 f = s.Frame;
            Vec3 shift = NormalOffset(f, hinge);
            var r = new SketchModel { Name = s.Name, Frame = new Frame3(f.Origin - shift, f.XAxis, f.YAxis), ConstrainedStatus = s.ConstrainedStatus };
            r.Points.AddRange(s.Points);
            r.Segments.AddRange(s.Segments);
            r.Relations.AddRange(s.Relations);
            r.Dimensions.AddRange(s.Dimensions);
            r.Warnings.AddRange(s.Warnings);
            return r;
        }

        /// <summary>
        /// One profile from its two reads (A2 P2): frame and content as the finished part shows them
        /// (<paramref name="final"/>, read without rollback – a position offset included), relations and
        /// dimensions as the state before the flange shows them (<paramref name="atFlange"/> – the edges
        /// and vertices they name exist there, and no longer in the finished part). Both reads must be the
        /// same sketch: the same segments, the same points at the same place in the sketch. Null with the
        /// reason otherwise.
        /// </summary>
        public static SketchModel? Merge(SketchModel final, SketchModel atFlange, out string? why)
        {
            why = null;
            if (!SameIds(final.Segments.Select(s => s.Id), atFlange.Segments.Select(s => s.Id)) ||
                !SameIds(final.Points.Select(p => p.Id), atFlange.Points.Select(p => p.Id)))
            {
                why = "hat im fertigen Teil andere Skizzenelemente als vor der Lasche";
                return null;
            }
            foreach (SkPoint p in final.Points)
            {
                SkPoint q = atFlange.Point(p.Id);
                if (p.Distance2D(q) > Tol.Length)
                {
                    why = "liegt im fertigen Teil in ihrer Ebene anders als vor der Lasche";
                    return null;
                }
            }
            var m = new SketchModel { Name = final.Name, Frame = final.Frame, ConstrainedStatus = atFlange.ConstrainedStatus };
            m.Points.AddRange(final.Points);
            m.Segments.AddRange(final.Segments);
            m.Relations.AddRange(atFlange.Relations);
            m.Dimensions.AddRange(atFlange.Dimensions);
            m.Warnings.AddRange(atFlange.Warnings.Concat(final.Warnings).Distinct(StringComparer.Ordinal));
            return m;
        }

        private static bool SameIds(IEnumerable<string> a, IEnumerable<string> b) =>
            a.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(b.OrderBy(x => x, StringComparer.Ordinal), StringComparer.Ordinal);

        /// <summary>
        /// Does a sketch SOLIDWORKS made on the copy's edge (InsertSketchForEdgeFlange) take the mirrored
        /// profile (A2 §5 step 2)? All in the copy's space: its X axis along the hinge – either way, the
        /// copy's edge may run backwards –, its Y axis along the flange direction (the side the profile is
        /// drawn to; tbcphutai measured Y along the flange), and its plane through the hinge and every
        /// point of the rebased, mirrored profile within <paramref name="tol"/> – a plane tilted by a
        /// hair would lift the far points of the profile off it.
        /// </summary>
        public static bool FitsFrame(Frame3 target, EdgeSignature hinge, Vec3 direction, IEnumerable<Vec3> points, double tol = Tol.Length)
        {
            if (hinge.Kind != CurveKind.Line || hinge.Length <= Tol.Length || direction.Length <= Tol.Length) return false;
            if (Math.Abs(target.XAxis.Dot(Along(hinge))) < 1 - AxisTolerance) return false;
            if (target.YAxis.Dot(direction.Normalized()) < 1 - AxisTolerance) return false;
            Plane3 plane = target.Plane;
            return plane.Contains(hinge.Start, tol) && plane.Contains(hinge.End, tol) && points.All(p => plane.Contains(p, tol));
        }

        /// <summary><see cref="FitsFrame(Frame3, EdgeSignature, Vec3, IEnumerable{Vec3}, double)"/> for an original's profile under R.</summary>
        public static bool FitsFrame(FlangeProfile source, Reflection r, Frame3 target, double tol = Tol.Length)
        {
            Vec3? d = Direction(source.Sketch, source.Hinge);
            if (d == null) return false;
            List<Vec3> points = ProfilePoints(Rebase(source.Sketch, source.Hinge)).Select(r.Apply).ToList();
            return FitsFrame(target, source.Hinge.Mirror(r), r.ApplyVector(d.Value), points, tol);
        }

        /// <summary>
        /// How far two point sets lie apart, order-free: the largest distance from a point of either set
        /// to the nearest point of the other. 0 for the same set in any order; infinity if exactly one is empty.
        /// </summary>
        public static double InPlaneDeviation(IReadOnlyList<Vec3> expected, IReadOnlyList<Vec3> actual)
        {
            if (expected.Count == 0 && actual.Count == 0) return 0;
            if (expected.Count == 0 || actual.Count == 0) return double.PositiveInfinity;
            static double OneWay(IReadOnlyList<Vec3> from, IReadOnlyList<Vec3> to) => from.Max(p => to.Min(q => p.DistanceTo(q)));
            return Math.Max(OneWay(expected, actual), OneWay(actual, expected));
        }

        /// <summary>An angle in [0, 2π).</summary>
        public static double Wrap(double a)
        {
            const double full = 2 * Math.PI;
            double m = a % full;
            if (m < 0) m += full;
            return m >= full ? 0 : m;
        }

        /// <summary>
        /// The angle InsertSketchForEdgeFlange needs on the copy's edge (A2 P1): the sketch angle is a
        /// rotation about the directed hinge, and R·Rot(t, θ)·R⁻¹ = Rot(R t, −θ). An edge of the copy that
        /// runs along R(t) needs −θ, one that runs against it θ again. In [0, 2π).
        /// </summary>
        public static double PredictedAngle(double theta, bool alongMirror) => Wrap(alongMirror ? -theta : theta);

        /// <summary>
        /// The profile planes to try, in order (A2 P1): first the predicted angle of each edge
        /// (<see cref="PredictedAngle"/>, in edge order), then θ, 2π − θ, π − θ and π + θ; each with the
        /// original's ReverseOffset as FlipDir first and then the other – what FlipDir does is not
        /// documented, so neither value is relied on. Angles equal mod 2π are tried once. One pair must
        /// fit every edge (<see cref="CommonCandidate"/>): the flange has one bend angle.
        /// </summary>
        public static List<(double angle, bool flip)> AngleCandidates(double theta, IEnumerable<bool> alongMirror, bool reverseOffset)
        {
            var angles = new List<double>();
            void Add(double a)
            {
                a = Wrap(a);
                if (!angles.Any(x => Math.Abs(Math.IEEERemainder(x - a, 2 * Math.PI)) <= SheetMetalCompare.AngleTolerance)) angles.Add(a);
            }
            foreach (bool along in alongMirror) Add(PredictedAngle(theta, along));
            foreach (double a in new[] { theta, 2 * Math.PI - theta, Math.PI - theta, Math.PI + theta }) Add(a);
            var result = new List<(double, bool)>();
            foreach (double a in angles)
            {
                result.Add((a, reverseOffset));
                result.Add((a, !reverseOffset));
            }
            return result;
        }

        /// <summary>
        /// The first candidate that fits every edge (A2 P1: one angle for the whole flange), or null.
        /// <paramref name="fits"/> is asked edge by edge and not beyond the first edge that does not fit;
        /// <paramref name="abandoned"/> then hears of the candidate – the builder removes the sketches it
        /// made for it before the next.
        /// </summary>
        public static int? CommonCandidate(int candidates, int edges, Func<int, int, bool> fits, Action<int>? abandoned = null)
        {
            for (int c = 0; c < candidates; c++)
            {
                bool all = true;
                for (int e = 0; e < edges && all; e++) all = fits(c, e);
                if (all) return c;
                abandoned?.Invoke(c);
            }
            return null;
        }

        /// <summary>
        /// The copy's profile against the mirrored original's (A2 P2), all three parts: (a) its plane
        /// parallel to the mirrored one, (b) the in-plane content – both rebased onto their hinge, so a
        /// position offset does not count here –, (c) the offset of the plane off the hinge against the
        /// mirrored offset, which is where a wrong position-offset direction shows. <paramref name="copyHinge"/>
        /// is the mirrored hinge: the copy's own edge is gone into its bend once the flange stands.
        /// </summary>
        public static ProfileCheck Check(FlangeProfile source, Reflection r, SketchModel copy, EdgeSignature copyHinge)
        {
            Vec3 n = r.ApplyVector(source.Sketch.Frame.ZAxis);
            List<Vec3> want = ProfilePoints(Rebase(source.Sketch, source.Hinge), Along(source.Hinge)).Select(r.Apply).ToList();
            List<Vec3> got = ProfilePoints(Rebase(copy, copyHinge), Along(copyHinge));
            return new ProfileCheck(source.SketchName,
                copy.Frame.ZAxis.Cross(n).Length <= Tol.Angle,
                InPlaneDeviation(want, got),
                NormalOffset(copy.Frame, copyHinge).DistanceTo(r.ApplyVector(NormalOffset(source.Sketch.Frame, source.Hinge))));
        }
    }

    /// <summary>The outcome of <see cref="FlangeProfiles.Check"/> for one profile.</summary>
    public sealed class ProfileCheck
    {
        public ProfileCheck(string sketch, bool planeParallel, double inPlane, double offset)
        {
            Sketch = sketch;
            PlaneParallel = planeParallel;
            InPlane = inPlane;
            Offset = offset;
        }

        /// <summary>The original's profile name.</summary>
        public string Sketch { get; }

        /// <summary>(a) The copy's plane is parallel to the mirrored one.</summary>
        public bool PlaneParallel { get; }

        /// <summary>(b) Largest in-plane distance between the rebased point sets, metres.</summary>
        public double InPlane { get; }

        /// <summary>(c) How far the copy's offset off the hinge is from the mirrored one, metres.</summary>
        public double Offset { get; }

        public bool Passed => PlaneParallel && InPlane <= Tol.Length && Offset <= Tol.Length;

        /// <summary>What does not fit, German; empty if it passed.</summary>
        public List<string> Describe(IFormatProvider? culture = null)
        {
            var result = new List<string>();
            if (!PlaneParallel) result.Add($"Profil „{Sketch}“: Ebene der Kopie gegen die gespiegelte gekippt");
            if (InPlane > Tol.Length)
                result.Add(double.IsPositiveInfinity(InPlane)
                    ? $"Profil „{Sketch}“: ohne vergleichbare Punkte"
                    : $"Profil „{Sketch}“: weicht in seiner Ebene um bis zu {SheetMetalCompare.Mm(InPlane, culture)} vom gespiegelten ab");
            if (Offset > Tol.Length) result.Add($"Profil „{Sketch}“: Abstand der Profilebene zur Kante weicht um {SheetMetalCompare.Mm(Offset, culture)} vom gespiegelten ab");
            return result;
        }
    }
}
