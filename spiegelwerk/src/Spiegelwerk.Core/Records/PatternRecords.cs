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

    /// <summary>
    /// Curve driven pattern: instances along a curve (sketch segment, whole sketch or model
    /// edge), optionally a second direction. Like the linear pattern, the reverse flags refer
    /// to the curve's own run, which in the mirrored part may be the other way – the
    /// placements decide.
    /// </summary>
    public sealed class CurvePatternRecord : PatternRecord
    {
        public CurvePatternRecord(string name, string typeName) : base(name, typeName)
        {
        }

        /// <summary>The curve of direction 1: sketch segment, sketch or edge (never an axis).</summary>
        public GeometryRef? Curve1 { get; set; }

        public bool Reverse1 { get; set; }
        public int Count1 { get; set; }
        public bool EqualSpacing1 { get; set; }

        /// <summary>Distance between instances, or the length covered with <see cref="EqualSpacing1"/>.</summary>
        public double Spacing1 { get; set; }

        /// <summary>swCurveDrivenPatternCurveMethod_e: transform curve 0, offset curve 1.</summary>
        public int CurveMethod { get; set; }

        /// <summary>swCurveDrivenPatternAlignment_e: tangent to curve 0, align to seed 1.</summary>
        public int Alignment { get; set; }

        /// <summary>Face whose normal orients the instances along a 3D curve (newer API versions only).</summary>
        public GeometryRef? FaceNormal { get; set; }

        public bool HasDirection2 { get; set; }
        public GeometryRef? Curve2 { get; set; }
        public bool Reverse2 { get; set; }
        public int Count2 { get; set; } = 1;
        public bool EqualSpacing2 { get; set; }
        public double Spacing2 { get; set; }

        /// <summary>Direction 2 patterns the seed only, not the instances of direction 1.</summary>
        public bool SeedOnly2 { get; set; }

        /// <summary>Instance indices to read the placements for (seed included).</summary>
        public int InstanceCount => Count1 * (HasDirection2 ? System.Math.Max(1, Count2) : 1);
    }

    /// <summary>
    /// Sketch driven pattern: one instance at each point of a reference sketch, placed by
    /// moving the reference point (the seed's centroid or a chosen point) there. Pure
    /// translations, so a mirror changes nothing but the places – the reference sketch and
    /// the reference point are rebuilt mirrored, and the pattern follows.
    /// </summary>
    public sealed class SketchPatternRecord : PatternRecord
    {
        public SketchPatternRecord(string name, string typeName, string sketchName) : base(name, typeName)
        {
            SketchName = sketchName;
        }

        /// <summary>The reference sketch, by original name.</summary>
        public string SketchName { get; }

        public bool UseCentroid { get; set; }

        /// <summary>Chosen reference point (sketch point or vertex) without <see cref="UseCentroid"/>.</summary>
        public GeometryRef? ReferencePoint { get; set; }

        /// <summary>Points of the reference sketch: an upper bound for the instance indices to read.</summary>
        public int SketchPointCount { get; set; }
    }
}
