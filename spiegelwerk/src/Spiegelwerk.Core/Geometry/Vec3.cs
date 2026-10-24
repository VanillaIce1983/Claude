using System;
using System.Globalization;

namespace Spiegelwerk.Core.Geometry
{
    /// <summary>Point or vector in model space. SOLIDWORKS API units: metres.</summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Vec3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Vec3 Zero = new Vec3(0, 0, 0);
        public static readonly Vec3 UnitX = new Vec3(1, 0, 0);
        public static readonly Vec3 UnitY = new Vec3(0, 1, 0);
        public static readonly Vec3 UnitZ = new Vec3(0, 0, 1);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(double s, Vec3 a) => a * s;
        public static Vec3 operator /(Vec3 a, double s) => new Vec3(a.X / s, a.Y / s, a.Z / s);

        public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;

        public Vec3 Cross(Vec3 o) => new Vec3(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);

        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);

        public double DistanceTo(Vec3 o) => (this - o).Length;

        public Vec3 Normalized()
        {
            double len = Length;
            if (len < 1e-15) throw new InvalidOperationException("Nullvektor kann nicht normiert werden.");
            return this / len;
        }

        /// <summary>Same direction or opposite direction (unit vectors expected).</summary>
        public bool IsParallelTo(Vec3 o, double angleTol = Tol.Angle) => Math.Abs(Math.Abs(Dot(o)) - 1.0) < angleTol;

        public bool IsSameDirection(Vec3 o, double angleTol = Tol.Angle) => Dot(o) > 1.0 - angleTol;

        public bool AlmostEquals(Vec3 o, double tol = Tol.Length) => DistanceTo(o) <= tol;

        /// <summary>Any unit vector perpendicular to this one.</summary>
        public Vec3 AnyPerpendicular()
        {
            Vec3 helper = Math.Abs(X) < 0.9 ? UnitX : UnitY;
            return Cross(helper).Normalized();
        }

        public double[] ToArray() => new[] { X, Y, Z };

        public static Vec3 FromArray(double[] a, int offset = 0) => new Vec3(a[offset], a[offset + 1], a[offset + 2]);

        public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object obj) => obj is Vec3 v && Equals(v);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = X.GetHashCode();
                h = h * 397 ^ Y.GetHashCode();
                return h * 397 ^ Z.GetHashCode();
            }
        }

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.######}, {1:0.######}, {2:0.######})", X, Y, Z);
    }
}
