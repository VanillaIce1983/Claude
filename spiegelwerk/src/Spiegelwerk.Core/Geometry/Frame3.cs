using System;

namespace Spiegelwerk.Core.Geometry
{
    /// <summary>
    /// Right-handed orthonormal coordinate system given in model space. Used for sketch
    /// planes: X and Y span the sketch, Z is the sketch normal. ToWorld maps sketch
    /// coordinates to model coordinates (the inverse of ISketch.ModelToSketchTransform).
    /// </summary>
    public readonly struct Frame3
    {
        public readonly Vec3 Origin;
        public readonly Vec3 XAxis;
        public readonly Vec3 YAxis;
        public readonly Vec3 ZAxis;

        public Frame3(Vec3 origin, Vec3 xAxis, Vec3 yAxis)
        {
            Origin = origin;
            XAxis = xAxis.Normalized();
            // Re-orthogonalise Y against X so small numerical errors from the API do not accumulate.
            YAxis = (yAxis - XAxis * yAxis.Dot(XAxis)).Normalized();
            ZAxis = XAxis.Cross(YAxis);
        }

        public static readonly Frame3 World = new Frame3(Vec3.Zero, Vec3.UnitX, Vec3.UnitY);

        public Plane3 Plane => new Plane3(Origin, ZAxis);

        public Vec3 ToWorld(Vec3 local) => Origin + XAxis * local.X + YAxis * local.Y + ZAxis * local.Z;

        public Vec3 ToWorld(double x, double y) => Origin + XAxis * x + YAxis * y;

        public Vec3 VectorToWorld(Vec3 local) => XAxis * local.X + YAxis * local.Y + ZAxis * local.Z;

        public Vec3 ToLocal(Vec3 world)
        {
            Vec3 d = world - Origin;
            return new Vec3(d.Dot(XAxis), d.Dot(YAxis), d.Dot(ZAxis));
        }

        public Vec3 VectorToLocal(Vec3 world) => new Vec3(world.Dot(XAxis), world.Dot(YAxis), world.Dot(ZAxis));

        /// <summary>Builds a frame from three axis vectors as returned by the API, checking orthonormality.</summary>
        public static Frame3 FromAxes(Vec3 origin, Vec3 x, Vec3 y, Vec3 z)
        {
            var f = new Frame3(origin, x, y);
            if (!f.ZAxis.IsSameDirection(z.Normalized(), 1e-6))
                throw new ArgumentException("Achsen bilden kein rechtshändiges Koordinatensystem.");
            return f;
        }

        public override string ToString() => $"Ursprung {Origin}, X {XAxis}, Y {YAxis}, Z {ZAxis}";
    }
}
