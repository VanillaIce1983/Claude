using System;

namespace Spiegelwerk.Core.Geometry
{
    /// <summary>
    /// Mirror about a plane: R(p) = p - 2((p - p0)·n)n. A reflection is its own inverse and
    /// reverses orientation, which is what makes arc directions, cross products and the
    /// handedness of sketch frames flip.
    /// </summary>
    public sealed class Reflection
    {
        public Plane3 Plane { get; }

        public Reflection(Plane3 plane)
        {
            Plane = plane;
        }

        public Vec3 Apply(Vec3 p) => p - Plane.Normal * (2.0 * Plane.SignedDistance(p));

        public Vec3 ApplyVector(Vec3 v) => v - Plane.Normal * (2.0 * v.Dot(Plane.Normal));

        public Plane3 Apply(Plane3 p) => new Plane3(Apply(p.Origin), ApplyVector(p.Normal));

        public Line3 Apply(Line3 l) => new Line3(Apply(l.Point), ApplyVector(l.Direction));

        public Affine ToAffine()
        {
            Vec3 t = Apply(Vec3.Zero);
            return new Affine(ApplyVector(Vec3.UnitX), ApplyVector(Vec3.UnitY), ApplyVector(Vec3.UnitZ), t);
        }

        /// <summary>
        /// A frame does not stay right-handed under reflection: the images of X and Y span the
        /// mirrored plane, but their cross product is -R(Z). This returns the right-handed frame
        /// with origin R(o), X = R(x), Y = R(y) and therefore Z = -R(z).
        /// </summary>
        public Frame3 ApplyKeepingXY(Frame3 f) => new Frame3(Apply(f.Origin), ApplyVector(f.XAxis), ApplyVector(f.YAxis));

        /// <summary>
        /// The reflection a transform is, or null. x ↦ Mx + t is one if M = I − 2nnᵀ (det −1,
        /// symmetric, orthogonal – and not −I, a point reflection) and t is parallel to n;
        /// otherwise it is a glide reflection, which has the same linear part. The plane goes
        /// through t/2.
        /// </summary>
        /// <param name="tol">For the matrix entries; the translation is checked against <see cref="Tol.Length"/>.</param>
        public static Reflection? TryFromAffine(Affine a, double tol = 1e-9)
        {
            Vec3[] cols = { a.Col0, a.Col1, a.Col2 };
            if (Math.Abs(a.Determinant + 1) > tol) return null;
            for (int i = 0; i < 3; i++)
            {
                if (Math.Abs(cols[i].Length - 1) > tol) return null;
                for (int j = i + 1; j < 3; j++)
                {
                    if (Math.Abs(cols[i].Dot(cols[j])) > tol) return null;
                    // Symmetric: the entry in row i, column j equals the one in row j, column i.
                    if (Math.Abs(Component(cols[j], i) - Component(cols[i], j)) > tol) return null;
                }
            }

            // I − M = 2nnᵀ: its column with the largest norm is a multiple of n.
            Vec3[] units = { Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ };
            Vec3 largest = Vec3.Zero;
            for (int i = 0; i < 3; i++)
            {
                Vec3 c = units[i] - cols[i];
                if (c.Length > largest.Length) largest = c;
            }
            if (largest.Length < 0.5) return null;
            Vec3 n = largest.Normalized();
            for (int i = 0; i < 3; i++)
                if ((units[i] - n * (2 * Component(n, i))).DistanceTo(cols[i]) > 10 * tol) return null;

            Vec3 t = a.Translation;
            if ((t - n * t.Dot(n)).Length > Tol.Length) return null;
            return new Reflection(new Plane3(t / 2, n));
        }

        private static double Component(Vec3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

        public override string ToString() => $"Spiegelung an {Plane}";
    }
}
