using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// Points of a table driven pattern, given in a coordinate system F. The mirrored part
    /// needs the instance at R(F(p)); the rebuilt coordinate system F' has X' = R(x),
    /// Y' = R(y) and – to stay right-handed – Z' = −R(z). So R(F(p)) = F'(p.X, p.Y, −p.Z):
    /// the table keeps its X and Y values, only a Z value changes sign.
    /// </summary>
    public static class TablePoints
    {
        public static Vec3 Mirror(Vec3 p) => new Vec3(p.X, p.Y, -p.Z);

        public static List<Vec3> Mirror(IEnumerable<Vec3> points) => points.Select(Mirror).ToList();

        /// <summary>Flat x/y/z array as PointArray takes it.</summary>
        public static double[] ToArray(IEnumerable<Vec3> points) => points.SelectMany(p => new[] { p.X, p.Y, p.Z }).ToArray();

        /// <summary>Points from a flat x/y/z array; a trailing incomplete triple is ignored.</summary>
        public static List<Vec3> FromArray(double[] xyz)
        {
            var result = new List<Vec3>();
            for (int i = 0; i + 2 < xyz.Length; i += 3) result.Add(new Vec3(xyz[i], xyz[i + 1], xyz[i + 2]));
            return result;
        }

        /// <summary>Same points in the same order, within the tolerance.</summary>
        public static bool Same(IReadOnlyList<Vec3> a, IReadOnlyList<Vec3> b, double tol = Tol.Length)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (!a[i].AlmostEquals(b[i], tol)) return false;
            return true;
        }
    }
}
