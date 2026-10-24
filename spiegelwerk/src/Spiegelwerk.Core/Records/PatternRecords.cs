using System.Collections.Generic;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Verification;

namespace Spiegelwerk.Core.Records
{
    /// <summary>
    /// Linear and circular pattern. The seeds are features (by original name, the rebuilt
    /// counterparts are patterned), faces (by fingerprint) or bodies (by volume and first
    /// moment). The placements the original reports for its instances are kept: mirrored,
    /// they are what the rebuilt pattern has to report, which settles the direction flags
    /// without guessing.
    /// </summary>
    public abstract class PatternRecord : FeatureRecord
    {
        protected PatternRecord(string name, string typeName) : base(name, typeName)
        {
        }

        public List<string> SeedFeatures { get; } = new List<string>();
        public List<FaceSignature> SeedFaces { get; } = new List<FaceSignature>();
        public List<MassState> SeedBodies { get; } = new List<MassState>();
        public bool BodyPattern { get; set; }
        public bool GeometryPattern { get; set; }
        public bool VarySketch { get; set; }

        /// <summary>Instances left out, as SOLIDWORKS numbers them (SkippedItemArray).</summary>
        public List<int> SkippedItems { get; } = new List<int>();

        /// <summary>Seed → instance placement of every instance (unmirrored); empty if the original does not report them.</summary>
        public List<Affine> Instances { get; } = new List<Affine>();

        public int SeedCount => SeedFeatures.Count + SeedFaces.Count + SeedBodies.Count;
    }

    public sealed class LinearPatternRecord : PatternRecord
    {
        public LinearPatternRecord(string name, string typeName) : base(name, typeName)
        {
        }

        /// <summary>Linear edge, axis, sketch line, plane or planar face.</summary>
        public GeometryRef? Direction1 { get; set; }

        public GeometryRef? Direction2 { get; set; }
        public bool Reverse1 { get; set; }
        public bool Reverse2 { get; set; }
        public double Spacing1 { get; set; }
        public double Spacing2 { get; set; }
        public int Count1 { get; set; }
        public int Count2 { get; set; } = 1;
        public bool HasDirection2 { get; set; }

        /// <summary>Direction 2 patterns the seed only, not the instances of direction 1.</summary>
        public bool SeedOnly2 { get; set; }
    }

    public sealed class CircularPatternRecord : PatternRecord
    {
        public CircularPatternRecord(string name, string typeName) : base(name, typeName)
        {
        }

        /// <summary>Axis, linear or circular edge, sketch line or cylindrical face.</summary>
        public GeometryRef? Axis { get; set; }

        public bool Reverse { get; set; }

        /// <summary>Angle between instances, or the total angle with <see cref="EqualSpacing"/>.</summary>
        public double Spacing { get; set; }

        public int Count { get; set; }
        public bool EqualSpacing { get; set; }
        public bool Direction2 { get; set; }
        public bool Symmetric { get; set; }
        public double Spacing2 { get; set; }
        public int Count2 { get; set; }
        public bool EqualSpacing2 { get; set; }
    }
}
