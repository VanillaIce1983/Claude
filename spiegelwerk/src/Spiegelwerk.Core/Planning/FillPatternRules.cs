using System;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// What a reflection does to a fill pattern. Spacings, margins and counts stay. Two things
    /// have a sense that the mirror reverses:
    ///
    ///  - The staggered (perforation) lattice with basis s·d and s·(cos θ·d + sin θ·n), n the
    ///    in-plane normal (face normal × d). Mirrored it is the lattice with the stagger angle
    ///    π − θ: the in-plane normal of the copy is −R(n) whichever way its direction edge runs,
    ///    as long as the boundary keeps its outward normal. 60°, 90° and 120° lattices are
    ///    symmetric and stay as they are.
    ///  - The rotation of a seed cut, measured about the face normal: it changes sign.
    ///
    /// Both depend on how SOLIDWORKS orients the plane of the copy (a sketch boundary may face
    /// the other way), so the opposite is offered to the verifier as well.
    /// </summary>
    public static class FillPatternRules
    {
        public static double MirrorStagger(double angle) => Math.PI - angle;

        public static double MirrorRotation(double angle) => -angle;

        /// <summary>The supplementary stagger gives the same lattice: nothing to choose.</summary>
        public static bool StaggerIsSymmetric(double angle) => Math.Abs(MirrorStagger(angle) - angle) < 1e-9 ||
                                                                 LatticeContains(angle, MirrorStagger(angle));

        /// <summary>Whether the lattice with stagger <paramref name="b"/> is the one with stagger <paramref name="a"/> (unit spacing).</summary>
        public static bool LatticeContains(double a, double b)
        {
            // Lattice(a) = { i·d + j·(cos a, sin a) }. Lattice(b) equals it iff its second basis
            // vector is an integer combination with j = ±1 (same cell area: sin a = sin b).
            if (Math.Abs(Math.Sin(a) - Math.Sin(b)) > 1e-9) return false;
            double i = Math.Cos(b) - Math.Cos(a);
            return Math.Abs(i - Math.Round(i)) < 1e-9;
        }

        /// <summary>Lattice point (i, j) with unit spacing in the frame (d, n).</summary>
        public static Vec3 LatticePoint(int i, int j, double stagger, Vec3 d, Vec3 n) =>
            d * (i + j * Math.Cos(stagger)) + n * (j * Math.Sin(stagger));
    }
}
