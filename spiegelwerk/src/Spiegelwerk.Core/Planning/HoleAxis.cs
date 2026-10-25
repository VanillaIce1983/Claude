using System;
using System.Collections.Generic;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// Where a hole axis leaves the body. The far-side face of an Advanced Hole is not
    /// reported by the API; the face the axis first leaves the body through is the face the
    /// far-side elements start from in every ordinary case (a plate, a flange).
    /// </summary>
    public static class HoleAxis
    {
        /// <summary>
        /// Distance along the axis (from <paramref name="centre"/> in direction
        /// <paramref name="into"/>, unit length) at which it crosses <paramref name="plane"/>
        /// leaving the material – the outward normal points along the axis. Null if the axis
        /// enters there, runs parallel, or the crossing is not ahead of the centre.
        /// </summary>
        public static double? ExitDistance(Vec3 centre, Vec3 into, Plane3 plane)
        {
            double along = into.Dot(plane.Normal);
            if (along <= Tol.Angle) return null;
            double t = (plane.Origin - centre).Dot(plane.Normal) / along;
            return t > Tol.Length ? t : (double?)null;
        }

        /// <summary>
        /// Index of the first plane the axis leaves through at a point the face actually covers
        /// (<paramref name="covers"/>: face index, crossing point), or -1.
        /// </summary>
        public static int FirstExit(Vec3 centre, Vec3 into, IReadOnlyList<Plane3> planes, Func<int, Vec3, bool> covers)
        {
            int best = -1;
            double bestT = double.PositiveInfinity;
            for (int i = 0; i < planes.Count; i++)
            {
                double? t = ExitDistance(centre, into, planes[i]);
                if (t == null || t.Value >= bestT) continue;
                if (!covers(i, centre + into * t.Value)) continue;
                best = i;
                bestT = t.Value;
            }
            return best;
        }
    }
}
