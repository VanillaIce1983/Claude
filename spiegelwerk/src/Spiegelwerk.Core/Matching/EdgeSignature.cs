using System;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Core.Matching
{
    public enum CurveKind
    {
        Line,
        Circle,
        Ellipse,
        Other,
    }

    /// <summary>
    /// Geometric fingerprint of a model edge, taken in the state the feature saw it.
    /// Edges have no stable identity across documents; the mirrored fingerprint is what
    /// finds the corresponding edge in the rebuilt body.
    /// </summary>
    public sealed class EdgeSignature
    {
        public EdgeSignature(CurveKind kind, Vec3 start, Vec3 end, Vec3 mid, double length)
        {
            Kind = kind;
            Start = start;
            End = end;
            Mid = mid;
            Length = length;
        }

        public CurveKind Kind { get; }
        public Vec3 Start { get; }
        public Vec3 End { get; }

        /// <summary>Curve point at the middle parameter. For lines and circles this is the geometric middle.</summary>
        public Vec3 Mid { get; }

        public double Length { get; }

        /// <summary>Circles only.</summary>
        public Vec3 Center { get; set; }

        /// <summary>Circles only.</summary>
        public double Radius { get; set; }

        /// <summary>Circle axis or line direction (unit vector; orientation is not significant).</summary>
        public Vec3 Axis { get; set; }

        public bool IsClosed => Start.AlmostEquals(End);

        public EdgeSignature Mirror(Reflection r) => new EdgeSignature(Kind, r.Apply(Start), r.Apply(End), r.Apply(Mid), Length)
        {
            Center = r.Apply(Center),
            Radius = Radius,
            Axis = r.ApplyVector(Axis),
        };

        /// <summary>
        /// 0 for a perfect match, larger is worse, <see cref="double.PositiveInfinity"/> if the
        /// edges cannot be the same. Endpoint order is ignored because the kernel may run a
        /// mirrored edge the other way round.
        /// </summary>
        public double DistanceTo(EdgeSignature o, double tol = Tol.Length)
        {
            if (Kind != o.Kind) return double.PositiveInfinity;
            double lenTol = Math.Max(tol, Length * Tol.Relative);
            if (Math.Abs(Length - o.Length) > lenTol) return double.PositiveInfinity;

            double ends = Math.Min(
                Start.DistanceTo(o.Start) + End.DistanceTo(o.End),
                Start.DistanceTo(o.End) + End.DistanceTo(o.Start));
            if (ends > 2 * tol) return double.PositiveInfinity;

            if (Kind == CurveKind.Circle)
            {
                if (Center.DistanceTo(o.Center) > tol || Math.Abs(Radius - o.Radius) > tol) return double.PositiveInfinity;
                if (!Axis.IsParallelTo(o.Axis, 1e-6)) return double.PositiveInfinity;
            }

            // Two half circles share both endpoints; only the middle tells them apart.
            double mid = Mid.DistanceTo(o.Mid);
            if ((Kind == CurveKind.Line || Kind == CurveKind.Circle) && mid > tol) return double.PositiveInfinity;
            return ends + mid;
        }

        public override string ToString() => $"{Kind} {Start} → {End}, L={Length:0.######}";
    }
}
