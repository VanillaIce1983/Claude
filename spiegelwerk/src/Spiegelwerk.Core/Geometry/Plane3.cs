using System;

namespace Spiegelwerk.Core.Geometry
{
    /// <summary>Oriented plane: a point on the plane and a unit normal.</summary>
    public readonly struct Plane3
    {
        public readonly Vec3 Origin;
        public readonly Vec3 Normal;

        public Plane3(Vec3 origin, Vec3 normal)
        {
            Origin = origin;
            Normal = normal.Normalized();
        }

        public static readonly Plane3 FrontXY = new Plane3(Vec3.Zero, Vec3.UnitZ);
        public static readonly Plane3 TopXZ = new Plane3(Vec3.Zero, Vec3.UnitY);
        public static readonly Plane3 RightYZ = new Plane3(Vec3.Zero, Vec3.UnitX);

        /// <summary>Signed distance of a point, positive on the normal side.</summary>
        public double SignedDistance(Vec3 p) => (p - Origin).Dot(Normal);

        public bool Contains(Vec3 p, double tol = Tol.Length) => Math.Abs(SignedDistance(p)) <= tol;

        public Vec3 Project(Vec3 p) => p - Normal * SignedDistance(p);

        /// <summary>Same geometric plane, regardless of normal orientation.</summary>
        public bool IsCoplanarWith(Plane3 o, double tol = Tol.Length) =>
            Normal.IsParallelTo(o.Normal) && Contains(o.Origin, tol);

        public bool IsParallelTo(Plane3 o) => Normal.IsParallelTo(o.Normal);

        public Plane3 Flipped() => new Plane3(Origin, -Normal);

        /// <summary>The line where two planes meet; null if they are parallel.</summary>
        public Line3? IntersectWith(Plane3 o)
        {
            Vec3 u = Normal.Cross(o.Normal);
            if (u.Length <= Tol.Angle) return null;
            // With d = n·p₀ for each plane: p = (d₁ (n₂ × u) + d₂ (u × n₁)) / |u|² lies on both.
            double d1 = Normal.Dot(Origin), d2 = o.Normal.Dot(o.Origin);
            Vec3 p = (o.Normal.Cross(u) * d1 + u.Cross(Normal) * d2) / u.Dot(u);
            return new Line3(p, u);
        }

        /// <summary>The point of the plane closest to the world origin; a canonical origin.</summary>
        public Vec3 ClosestPointToOrigin() => Project(Vec3.Zero);

        public override string ToString() => $"Ebene durch {Origin}, Normale {Normal}";
    }
}
