using System.Collections.Generic;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;

namespace Spiegelwerk.Core.Records
{
    public sealed class VertexRadius
    {
        public VertexRadius(Vec3 point, double radius)
        {
            Point = point;
            Radius = radius;
        }

        public Vec3 Point { get; }
        public double Radius { get; }
    }

    /// <summary>An intermediate control point: radius at a position along one of the fillet edges.</summary>
    public sealed class ControlPoint
    {
        public ControlPoint(int edge, double location, double radius)
        {
            Edge = edge;
            Location = location;
            Radius = radius;
        }

        /// <summary>Index into <see cref="VariableFilletRecord.Edges"/>.</summary>
        public int Edge { get; }

        /// <summary>Position along the edge from its start, as SOLIDWORKS reports it (fraction or percent).</summary>
        public double Location { get; }

        public double Radius { get; }
    }

    /// <summary>
    /// Variable radius fillet: radii at the vertices of the fillet edges and at control points
    /// along them. Radii are mirror-invariant; a vertex is identified by its position, a
    /// control point by its edge and its position along it – which has to be counted from the
    /// other end if the mirrored edge runs the other way.
    /// </summary>
    public sealed class VariableFilletRecord : FeatureRecord
    {
        public VariableFilletRecord(string name, string typeName) : base(name, typeName)
        {
        }

        public double DefaultRadius { get; set; }

        /// <summary>IVariableFilletFeatureData2.TransitionType (smooth or straight), written back as read.</summary>
        public int TransitionType { get; set; }

        public int OverflowType { get; set; }
        public bool Propagate { get; set; }
        public bool CurvatureContinuous { get; set; }
        public List<EdgeSignature> Edges { get; } = new List<EdgeSignature>();
        public List<VertexRadius> Vertices { get; } = new List<VertexRadius>();
        public List<ControlPoint> ControlPoints { get; } = new List<ControlPoint>();

        /// <summary>Locations are given in percent (some reported value above 1), otherwise as a fraction.</summary>
        public bool LocationsInPercent { get; set; }
    }

    public static class EdgeLocation
    {
        /// <summary>
        /// A position along an edge, counted from its start, carried over to the rebuilt edge:
        /// unchanged if that edge starts where the mirrored original starts, counted from the
        /// other end if it runs the other way. Null for closed edges (the kernel places their
        /// seam freely) or edges whose ends do not match.
        /// </summary>
        /// <param name="wanted">The original edge, already mirrored.</param>
        /// <param name="actual">The rebuilt edge.</param>
        /// <param name="full">1 for fractions, 100 for percent.</param>
        /// <remarks>The orientation is decided like edge matching does (summed end distances), so an edge that matched is never rejected here.</remarks>
        public static double? Map(EdgeSignature wanted, EdgeSignature actual, double location, double full, double tol = Tol.Length)
        {
            if (wanted.IsClosed || actual.IsClosed) return null;
            double same = actual.Start.DistanceTo(wanted.Start) + actual.End.DistanceTo(wanted.End);
            double swapped = actual.Start.DistanceTo(wanted.End) + actual.End.DistanceTo(wanted.Start);
            if (System.Math.Min(same, swapped) > 2 * tol) return null;
            return same <= swapped ? location : full - location;
        }
    }
}
