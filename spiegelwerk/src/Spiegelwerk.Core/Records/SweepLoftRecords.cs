using System.Collections.Generic;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Core.Records
{
    /// <summary>Same numbers as swTangencyType_e (start/end conditions of sweeps and lofts, path alignment).</summary>
    public enum Tangency
    {
        None = 0,
        NormalToProfile = 1,
        DirectionVector = 2,
        AllFaces = 3,
        MinimumTwist = 10,
    }

    /// <summary>Same numbers as swTwistControlType_e.</summary>
    public enum TwistControl
    {
        FollowPath = 0,
        KeepNormalConstant = 1,
        FollowPathFirstGuideCurve = 2,
        FollowFirstSecondGuideCurves = 3,
        ConstantTwistAlongPath = 8,
        NormalConstantTwistAlongPath = 9,
    }

    /// <summary>
    /// Sweep and swept cut, also thin and with a circular profile. Profile, path and guide
    /// curves are sketches of the original; they are rebuilt as sketches of their own before
    /// the sweep and selected by name.
    /// </summary>
    public sealed class SweepRecord : FeatureRecord
    {
        public SweepRecord(string name, string typeName) : base(name, typeName)
        {
        }

        public bool IsCut { get; set; }
        public bool IsThin { get; set; }

        /// <summary>swThinWallType_e.</summary>
        public int ThinType { get; set; }

        public double Thickness1 { get; set; }
        public double Thickness2 { get; set; }

        /// <summary>Profile sketch; null with a circular profile.</summary>
        public GeometryRef? Profile { get; set; }

        public GeometryRef? Path { get; set; }
        public List<GeometryRef> Guides { get; } = new List<GeometryRef>();

        public bool CircularProfile { get; set; }
        public double CircularDiameter { get; set; }

        /// <summary>swSweepDirection_e: one side, both sides, other side of the profile.</summary>
        public int Direction { get; set; }

        public TwistControl Twist { get; set; }
        public double TwistAngle { get; set; }
        public double TwistAngle2 { get; set; }
        public bool ReverseTwist1 { get; set; }
        public bool ReverseTwist2 { get; set; }

        /// <summary>Path alignment (swTangencyType_e: none, minimum twist, direction vector, all faces).</summary>
        public Tangency PathAlignment { get; set; }

        public GeometryRef? PathAlignmentDirection { get; set; }
        public bool MaintainTangency { get; set; }
        public bool AdvancedSmoothing { get; set; }
        public bool AlignWithEndFaces { get; set; }
        public bool TangentPropagation { get; set; }
        public bool MergeSmoothFaces { get; set; }
        public Tangency StartTangency { get; set; }
        public Tangency EndTangency { get; set; }
        public GeometryRef? StartDirection { get; set; }
        public GeometryRef? EndDirection { get; set; }
        public bool Merge { get; set; } = true;

        /// <summary>
        /// A twisted sweep turns the other way round in the mirror image; with a symmetric
        /// profile volume and centroid stay the same, only the face check sees it.
        /// </summary>
        public bool HasTwist =>
            (Twist == TwistControl.ConstantTwistAlongPath || Twist == TwistControl.NormalConstantTwistAlongPath) &&
            (System.Math.Abs(TwistAngle) > 1e-12 || System.Math.Abs(TwistAngle2) > 1e-12);
    }

    /// <summary>Loft and lofted cut, also thin. Profiles keep their order; the connectors are carried over as pick points.</summary>
    public sealed class LoftRecord : FeatureRecord
    {
        public LoftRecord(string name, string typeName) : base(name, typeName)
        {
        }

        public bool IsCut { get; set; }
        public bool IsThin { get; set; }
        public int ThinType { get; set; }
        public double Thickness1 { get; set; }
        public double Thickness2 { get; set; }

        /// <summary>Profiles in loft order: sketches (also a single point) or faces.</summary>
        public List<GeometryRef> Profiles { get; } = new List<GeometryRef>();

        public List<GeometryRef> Guides { get; } = new List<GeometryRef>();

        /// <summary>swTangencyType_e per guide curve, same order as <see cref="Guides"/>.</summary>
        public List<int> GuideTangency { get; } = new List<int>();

        public GeometryRef? Centerline { get; set; }

        /// <summary>
        /// Pick points per profile chain (model coordinates, unmirrored). SOLIDWORKS connects the
        /// profiles at these points; mirrored they give the same connectors. Empty if unreadable.
        /// </summary>
        public List<List<Vec3>> PickPoints { get; } = new List<List<Vec3>>();

        public bool Closed { get; set; }
        public bool MaintainTangency { get; set; }
        public bool AdvancedSmoothing { get; set; }
        public bool Merge { get; set; } = true;

        /// <summary>swGuideCurveInfluence_e.</summary>
        public int GuideCurveInfluence { get; set; }

        public Tangency StartTangency { get; set; }
        public Tangency EndTangency { get; set; }
        public double StartTangentLength { get; set; } = 1;
        public double EndTangentLength { get; set; } = 1;
        public bool ReverseStartTangent { get; set; }
        public bool ReverseEndTangent { get; set; }
        public GeometryRef? StartDirection { get; set; }
        public GeometryRef? EndDirection { get; set; }
        public bool StartApplyToAll { get; set; } = true;
        public bool EndApplyToAll { get; set; } = true;
        public double StartDraftAngle { get; set; }
        public double EndDraftAngle { get; set; }
        public bool ReverseStartDraft { get; set; }
        public bool ReverseEndDraft { get; set; }

        /// <summary>Normal-to-profile and direction-vector conditions point to one side; a mirror may need the other.</summary>
        public static bool HasSide(Tangency t) => t == Tangency.NormalToProfile || t == Tangency.DirectionVector;
    }
}
