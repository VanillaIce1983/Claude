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

        public override string ToString() => $"Spiegelung an {Plane}";
    }
}
