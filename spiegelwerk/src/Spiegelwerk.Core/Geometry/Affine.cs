using System;

namespace Spiegelwerk.Core.Geometry
{
    /// <summary>
    /// p' = M·p + T. The columns of M are the images of the unit axes, the same layout
    /// SOLIDWORKS uses in IMathTransform (elements 0-2, 3-5, 6-8 are the images of X, Y, Z,
    /// elements 9-11 the translation). Reflections are allowed (det M = -1).
    /// </summary>
    public readonly struct Affine
    {
        public readonly Vec3 Col0;
        public readonly Vec3 Col1;
        public readonly Vec3 Col2;
        public readonly Vec3 Translation;

        public Affine(Vec3 col0, Vec3 col1, Vec3 col2, Vec3 translation)
        {
            Col0 = col0;
            Col1 = col1;
            Col2 = col2;
            Translation = translation;
        }

        public static readonly Affine Identity = new Affine(Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ, Vec3.Zero);

        /// <summary>Local-to-world transform of a frame (a proper rigid motion).</summary>
        public static Affine FromFrame(Frame3 f) => new Affine(f.XAxis, f.YAxis, f.ZAxis, f.Origin);

        public Vec3 ApplyVector(Vec3 v) => Col0 * v.X + Col1 * v.Y + Col2 * v.Z;

        public Vec3 Apply(Vec3 p) => ApplyVector(p) + Translation;

        public double Determinant => Col0.Dot(Col1.Cross(Col2));

        /// <summary>this ∘ other: first other, then this.</summary>
        public Affine Compose(Affine other) =>
            new Affine(ApplyVector(other.Col0), ApplyVector(other.Col1), ApplyVector(other.Col2), Apply(other.Translation));

        public Affine Inverse()
        {
            double det = Determinant;
            if (Math.Abs(det) < 1e-12) throw new InvalidOperationException("Transformation ist nicht invertierbar.");
            // Rows of M⁻¹ are (Col1×Col2, Col2×Col0, Col0×Col1) / det.
            Vec3 r0 = Col1.Cross(Col2) / det;
            Vec3 r1 = Col2.Cross(Col0) / det;
            Vec3 r2 = Col0.Cross(Col1) / det;
            var c0 = new Vec3(r0.X, r1.X, r2.X);
            var c1 = new Vec3(r0.Y, r1.Y, r2.Y);
            var c2 = new Vec3(r0.Z, r1.Z, r2.Z);
            var inv = new Affine(c0, c1, c2, Vec3.Zero);
            return new Affine(c0, c1, c2, -inv.ApplyVector(Translation));
        }

        /// <summary>Orthonormal columns and det +1: a placement SOLIDWORKS accepts for a component.</summary>
        public bool IsProperRigid(double tol = 1e-9) =>
            Math.Abs(Col0.Length - 1) < tol && Math.Abs(Col1.Length - 1) < tol && Math.Abs(Col2.Length - 1) < tol &&
            Math.Abs(Col0.Dot(Col1)) < tol && Math.Abs(Col1.Dot(Col2)) < tol && Math.Abs(Col0.Dot(Col2)) < tol &&
            Determinant > 0;

        /// <summary>16-element array in IMathTransform.ArrayData layout (scale 1).</summary>
        public double[] ToSwArray() => new[]
        {
            Col0.X, Col0.Y, Col0.Z,
            Col1.X, Col1.Y, Col1.Z,
            Col2.X, Col2.Y, Col2.Z,
            Translation.X, Translation.Y, Translation.Z,
            1.0, 0, 0, 0,
        };

        public static Affine FromSwArray(double[] a)
        {
            if (a == null || a.Length < 13) throw new ArgumentException("Transformationsdaten unvollständig.");
            double s = a[12] == 0 ? 1.0 : a[12];
            return new Affine(Vec3.FromArray(a, 0) * s, Vec3.FromArray(a, 3) * s, Vec3.FromArray(a, 6) * s, Vec3.FromArray(a, 9));
        }
    }
}
