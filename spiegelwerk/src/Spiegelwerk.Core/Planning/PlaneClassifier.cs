using System.Collections.Generic;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Core.Planning
{
    public enum PlaneMatchKind
    {
        /// <summary>Coincides with an existing plane; sketch directly on it.</summary>
        Existing,

        /// <summary>Parallel to a standard plane; create an offset plane from it.</summary>
        OffsetFromStandard,

        /// <summary>Arbitrary orientation; create a plane through three fixed points.</summary>
        ThreePoints,
    }

    public sealed class PlaneMatch
    {
        public PlaneMatch(PlaneMatchKind kind, int index, double offset)
        {
            Kind = kind;
            Index = index;
            Offset = offset;
        }

        public PlaneMatchKind Kind { get; }

        /// <summary>Index of the existing plane (Existing) or of the standard plane (OffsetFromStandard).</summary>
        public int Index { get; }

        /// <summary>OffsetFromStandard: signed distance along the standard plane's normal.</summary>
        public double Offset { get; }

        public override string ToString() => $"{Kind} #{Index} {Offset:0.######}";
    }

    /// <summary>
    /// Decides on which plane a mirrored sketch is placed. The order of preference keeps the
    /// result independent of the original part: existing planes of the target first, then
    /// offsets of the standard planes (which only depend on the origin), and only for
    /// truly oblique planes a plane through three points of a fixed 3D sketch.
    /// </summary>
    public static class PlaneClassifier
    {
        /// <param name="existing">Planes of the target document (standard planes and planes created by this job).</param>
        /// <param name="standard">The three standard planes of the target (front, top, right) as planes through the origin.</param>
        public static PlaneMatch Classify(Plane3 wanted, IReadOnlyList<Plane3> existing, IReadOnlyList<Plane3> standard)
        {
            for (int i = 0; i < existing.Count; i++)
                if (existing[i].IsCoplanarWith(wanted))
                    return new PlaneMatch(PlaneMatchKind.Existing, i, 0);

            for (int i = 0; i < standard.Count; i++)
                if (standard[i].IsParallelTo(wanted))
                    return new PlaneMatch(PlaneMatchKind.OffsetFromStandard, i, standard[i].SignedDistance(wanted.Origin));

            return new PlaneMatch(PlaneMatchKind.ThreePoints, -1, 0);
        }

        /// <summary>Three non-collinear points spanning the plane, 10 mm apart, around the point closest to the origin.</summary>
        public static Vec3[] ThreePointsOn(Plane3 plane, double spacing = 0.01)
        {
            Vec3 o = plane.ClosestPointToOrigin();
            Vec3 u = plane.Normal.AnyPerpendicular();
            Vec3 v = plane.Normal.Cross(u);
            // Counter-clockwise about the normal, so a plane built from them has the same normal.
            return new[] { o, o + u * spacing, o + v * spacing };
        }
    }
}
