using System.Collections.Generic;
using Spiegelwerk.Core.Matching;

namespace Spiegelwerk.Core.Records
{
    public sealed class FaceThickness
    {
        public FaceThickness(FaceSignature face, double thickness)
        {
            Face = face;
            Thickness = thickness;
        }

        public FaceSignature Face { get; }
        public double Thickness { get; }
    }

    /// <summary>
    /// Shell. Everything about it is relative to the body – inside or outside, which faces are
    /// open, which walls are thicker – so a mirror changes nothing but the faces themselves.
    /// </summary>
    public sealed class ShellRecord : FeatureRecord
    {
        public ShellRecord(string name, string typeName) : base(name, typeName)
        {
        }

        public double Thickness { get; set; }

        /// <summary>IShellFeatureData.Direction as read; written back unchanged.</summary>
        public int Direction { get; set; }

        /// <summary>Faces removed (open sides). Empty: a closed, hollow body.</summary>
        public List<FaceSignature> RemovedFaces { get; } = new List<FaceSignature>();

        /// <summary>Faces with a wall thickness of their own.</summary>
        public List<FaceThickness> MultiThickness { get; } = new List<FaceThickness>();
    }

    /// <summary>Same numbers as swDraftType_e.</summary>
    public enum DraftKind
    {
        NeutralPlane = 0,
        PartingLine = 1,
        Step = 3,
    }

    /// <summary>
    /// Draft: neutral plane, parting line or step draft. The pull direction is a reference
    /// (plane, planar face, edge); its "reverse" flag depends on how that reference is
    /// oriented in the copy and follows σ of the reference direction.
    /// </summary>
    public sealed class DraftRecord : FeatureRecord
    {
        public DraftRecord(string name, string typeName) : base(name, typeName)
        {
        }

        public DraftKind Kind { get; set; }
        public double Angle { get; set; }
        public bool Reverse { get; set; }

        /// <summary>swDraftFacePropagationType_e.</summary>
        public int Propagation { get; set; }

        /// <summary>swDraftStepType_e, step drafts only.</summary>
        public int StepType { get; set; }

        public bool AllowReducedAngle { get; set; }

        /// <summary>Neutral plane (neutral plane draft) or direction of pull (parting line and step draft).</summary>
        public GeometryRef? Direction { get; set; }

        /// <summary>Faces to draft (neutral plane draft).</summary>
        public List<FaceSignature> Faces { get; } = new List<FaceSignature>();

        /// <summary>Parting lines (parting line and step draft).</summary>
        public List<EdgeSignature> PartingLines { get; } = new List<EdgeSignature>();

        /// <summary>Per parting line: the face on the other side is drafted.</summary>
        public List<bool> OtherFace { get; } = new List<bool>();
    }
}
