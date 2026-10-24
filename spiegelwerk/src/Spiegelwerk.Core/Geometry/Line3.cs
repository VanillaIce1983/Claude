namespace Spiegelwerk.Core.Geometry
{
    /// <summary>Infinite line: a point and a unit direction. Used for revolve axes and reference axes.</summary>
    public readonly struct Line3
    {
        public readonly Vec3 Point;
        public readonly Vec3 Direction;

        public Line3(Vec3 point, Vec3 direction)
        {
            Point = point;
            Direction = direction.Normalized();
        }

        public double DistanceTo(Vec3 p)
        {
            Vec3 d = p - Point;
            return (d - Direction * d.Dot(Direction)).Length;
        }

        /// <summary>Same infinite line, either direction.</summary>
        public bool IsSameLine(Line3 o, double tol = Tol.Length) =>
            Direction.IsParallelTo(o.Direction, 1e-6) && DistanceTo(o.Point) <= tol;

        public override string ToString() => $"Gerade durch {Point}, Richtung {Direction}";
    }
}
