using System.Collections.Generic;
using Spiegelwerk.Core.Matching;

namespace Spiegelwerk.Core.Records
{
    /// <summary>Same numbers as swSimpleFilletType_e (and swFeatureFilletType_e).</summary>
    public enum FaceFilletKind
    {
        Face = 2,
        FullRound = 3,
    }

    /// <summary>
    /// Face fillet (between two face sets) and full round fillet (side set, centre set, side
    /// set). Everything refers to faces and their outward normals, which a mirror keeps
    /// outward – the "reverse face normal" flags carry over unchanged, the faces are found by
    /// their mirrored fingerprints.
    /// </summary>
    public sealed class FaceFilletRecord : FeatureRecord
    {
        public FaceFilletRecord(string name, string typeName, FaceFilletKind kind) : base(name, typeName)
        {
            Kind = kind;
        }

        public FaceFilletKind Kind { get; }

        /// <summary>Face fillet only; a full round fillet takes its size from the faces.</summary>
        public double Radius { get; set; }

        public List<FaceSignature> Set1 { get; } = new List<FaceSignature>();
        public List<FaceSignature> Set2 { get; } = new List<FaceSignature>();

        /// <summary>Full round fillet: the faces the fillet replaces.</summary>
        public List<FaceSignature> Center { get; } = new List<FaceSignature>();

        public bool ReverseNormal1 { get; set; }
        public bool ReverseNormal2 { get; set; }
        public bool ConstantWidth { get; set; }
        public bool CurvatureContinuous { get; set; }
        public bool Propagate { get; set; }
        public bool KeepFeatures { get; set; }
        public int OverflowType { get; set; }

        /// <summary>Edges that bound the fillet instead of a radius (face fillet).</summary>
        public List<EdgeSignature> HoldLines { get; } = new List<EdgeSignature>();
    }
}
