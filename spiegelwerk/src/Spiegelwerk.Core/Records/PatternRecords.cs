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

        /// <summary>
        /// The chosen reference point is the origin. It stays the origin only if the mirror
        /// plane passes through it – always so in the assembly context, where the part is
        /// mirrored at one of its standard planes; otherwise it depends on the chosen plane.
        /// </summary>
        public bool ReferenceIsOrigin { get; set; }

        /// <summary>
        /// Standalone points and arc/ellipse centres of the reference sketch – segment end
        /// points are no instances: an upper bound for the instance indices to read.
        /// </summary>
        public int SketchPointCount { get; set; }
    }

    /// <summary>
    /// Table driven pattern: instances at points given in the XY plane of a coordinate system,
    /// placed by moving the reference point (the seed's centroid or a chosen point) there.
    /// The coordinate system is rebuilt as X' = R(x), Y' = R(y), Z' = X' × Y'; in it the points
    /// keep X and Y and change the sign of Z (<see cref="Planning.TablePoints"/>).
    /// </summary>
    public sealed class TablePatternRecord : PatternRecord
    {
        public TablePatternRecord(string name, string typeName, string coordinateSystemName, Frame3 coordinateSystem) : base(name, typeName)
        {
            CoordinateSystemName = coordinateSystemName;
            CoordinateSystem = coordinateSystem;
        }

        /// <summary>The coordinate system, by original name.</summary>
        public string CoordinateSystemName { get; }

        /// <summary>Its origin and axes (unmirrored) – for a helper if it is not rebuilt itself.</summary>
        public Frame3 CoordinateSystem { get; }

        /// <summary>Points of the table in the coordinate system (PointArray, x/y/z per point).</summary>
        public List<Vec3> Points { get; } = new List<Vec3>();

        public bool UseCentroid { get; set; }

        /// <summary>Chosen reference point (sketch point or vertex) without <see cref="UseCentroid"/>.</summary>
        public GeometryRef? ReferencePoint { get; set; }

        /// <summary>The chosen reference point is the origin (see <see cref="SketchPatternRecord.ReferenceIsOrigin"/>).</summary>
        public bool ReferenceIsOrigin { get; set; }

        public bool PropagateVisualProperty { get; set; }
    }

    /// <summary>
    /// Fill pattern: instances laid out inside a boundary (faces or a sketch) – circular,
    /// square, polygon or staggered (perforation) –, of seed features or of a seed cut the
    /// pattern makes itself. The API reports no instance placements; the mass check decides.
    /// A reflection keeps spacings and margins; it turns a staggered lattice into the one with
    /// the supplementary stagger angle and reverses the rotation of a seed cut
    /// (<see cref="Planning.FillPatternRules"/>).
    /// </summary>
    public sealed class FillPatternRecord : PatternRecord
    {
        public FillPatternRecord(string name, string typeName) : base(name, typeName)
        {
        }

        /// <summary>Faces or sketches bounding the pattern.</summary>
        public List<GeometryRef> Boundary { get; } = new List<GeometryRef>();

        /// <summary>Edge, sketch line or axis giving the layout direction; null: SOLIDWORKS chooses.</summary>
        public GeometryRef? Direction { get; set; }

        /// <summary>swPatternLayoutType_e: circular 0, square 1, polygon 2, perforation 3.</summary>
        public int LayoutType { get; set; }

        /// <summary>swPatternLayoutSpacingType_e: target spacing 0, instances 1.</summary>
        public int SpacingType { get; set; }

        public double InstanceSpacing { get; set; }
        public double StaggerAngle { get; set; }
        public double Margins { get; set; }
        public double LoopSpacing { get; set; }
        public int InstanceCount { get; set; }
        public int PolygonSides { get; set; }

        /// <summary>swFeaturesToPatternType_e: selected features 0, create seed cut 1.</summary>
        public int FeaturesToPattern { get; set; }

        /// <summary>swCreateSeedCutType_e: none 0, circle 1, square 2, diamond 3, polygon 4.</summary>
        public int SeedCutType { get; set; }

        public double SeedCutDiameter { get; set; }
        public double SeedCutDimension { get; set; }
        public double SeedCutRotation { get; set; }
        public double SeedCutDiagonal { get; set; }
        public int SeedCutPolygonSides { get; set; }
        public double SeedCutOuterRadius { get; set; }
        public double SeedCutInnerRadius { get; set; }
        public bool SeedCutFlip { get; set; }

        /// <summary>Vertex or sketch point where the seed sits; null: the boundary's centre.</summary>
        public GeometryRef? SeedCenter { get; set; }

        public bool PropagateVisualProperty { get; set; }

        public bool IsSeedCut => FeaturesToPattern == 1;
    }

    /// <summary>
    /// Variable pattern: a table of instances, each a copy of the seed with its own values for
    /// chosen dimensions of the seed (sketch or feature dimensions). The seed is rebuilt with
    /// the same dimension values, so the table carries over unchanged – only the columns have
    /// to name the copy's dimensions.
    /// </summary>
    public sealed class VariablePatternRecord : PatternRecord
    {
        public VariablePatternRecord(string name, string typeName) : base(name, typeName)
        {
        }

        /// <summary>Columns of the table, in order.</summary>
        public List<ControllingDimension> Dimensions { get; } = new List<ControllingDimension>();

        /// <summary>Rows of the table, in order.</summary>
        public List<VariableInstance> Rows { get; } = new List<VariableInstance>();

        public bool PropagateVisualProperties { get; set; }
    }

    /// <summary>A dimension of the seed that the variable pattern varies: "Name@Owner".</summary>
    public sealed class ControllingDimension
    {
        public ControllingDimension(string name, string owner, bool ownerIsSketch, double value)
        {
            Name = name;
            Owner = owner;
            OwnerIsSketch = ownerIsSketch;
            Value = value;
        }

        /// <summary>Dimension name within its owner, e.g. "D1".</summary>
        public string Name { get; }

        /// <summary>Original name of the sketch or feature the dimension belongs to.</summary>
        public string Owner { get; }

        public bool OwnerIsSketch { get; }

        /// <summary>The seed's value (system units) – identifies the dimension if its name differs in the copy.</summary>
        public double Value { get; }

        public override string ToString() => $"{Name}@{Owner}";
    }

    /// <summary>One row of a variable pattern: its values per column (null: not readable) and whether it is suppressed.</summary>
    public sealed class VariableInstance
    {
        public VariableInstance(string name, bool suppressed)
        {
            Name = name;
            Suppressed = suppressed;
        }

        public string Name { get; }
        public bool Suppressed { get; }
        public List<double?> Values { get; } = new List<double?>();
    }
}
