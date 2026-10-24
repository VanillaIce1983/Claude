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
        ///
        /// Closed edges (full circles, closed ellipses and splines) have a seam whose position
        /// the kernel chooses freely – a rebuilt circle's seam is usually somewhere else than
        /// the mirrored seam of the original. They are compared by seam-independent data only.
        /// </summary>
        /// <param name="distanceToCandidate">
        /// Kernel distance from a point to the candidate curve (this signature's points are
        /// tested against <paramref name="o"/>). Needed for closed ellipses/splines and used to
        /// tell open splines apart; without it such edges match on endpoints and length only.
        /// </param>
        public double DistanceTo(EdgeSignature o, Func<Vec3, double>? distanceToCandidate = null, double tol = Tol.Length)
        {
            if (Kind != o.Kind || IsClosed != o.IsClosed) return double.PositiveInfinity;
            double lenTol = Math.Max(tol, Length * Tol.Relative);
            if (Math.Abs(Length - o.Length) > lenTol) return double.PositiveInfinity;

            if (Kind == CurveKind.Circle)
            {
                double centre = Center.DistanceTo(o.Center), radius = Math.Abs(Radius - o.Radius);
                if (centre > tol || radius > tol || !Axis.IsParallelTo(o.Axis, 1e-6)) return double.PositiveInfinity;
                if (IsClosed) return centre + radius;
            }

            if (IsClosed)
            {
                // Closed ellipse or spline: the mirrored points must lie on the candidate curve.
                if (distanceToCandidate == null) return double.PositiveInfinity;
                double onCurve = Math.Max(distanceToCandidate(Start), distanceToCandidate(Mid));
                return onCurve <= tol ? onCurve : double.PositiveInfinity;
            }

            double ends = Math.Min(
                Start.DistanceTo(o.Start) + End.DistanceTo(o.End),
                Start.DistanceTo(o.End) + End.DistanceTo(o.Start));
            if (ends > 2 * tol) return double.PositiveInfinity;

            // Two half circles share both endpoints; only the middle tells them apart. For lines
            // and circles the middle parameter is the geometric middle; for other curves the
            // parametrisation may run differently, so the middle is checked on the curve.
            if (Kind == CurveKind.Line || Kind == CurveKind.Circle)
            {
                double mid = Mid.DistanceTo(o.Mid);
                return mid > tol ? double.PositiveInfinity : ends + mid;
            }
            if (distanceToCandidate != null)
            {
                double onCurve = distanceToCandidate(Mid);
                return onCurve > tol ? double.PositiveInfinity : ends + onCurve;
            }
            return ends;
        }

        public override string ToString() => $"{Kind} {Start} → {End}, L={Length:0.######}";
    }
}
