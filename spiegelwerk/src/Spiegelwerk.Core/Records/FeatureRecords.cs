using System.Collections.Generic;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Sketch;
using Spiegelwerk.Core.Verification;

namespace Spiegelwerk.Core.Records
{
    /// <summary>
    /// Everything the rebuild needs to know about one feature of the original, captured once.
    /// After capture the original document is not touched again, which is what makes the
    /// result independent: nothing in the target can point back to the source.
    /// </summary>
    public abstract class FeatureRecord
    {
        protected FeatureRecord(string name, string typeName)
        {
            Name = name;
            TypeName = typeName;
        }

        public string Name { get; }

        /// <summary>IFeature.GetTypeName2 of the original.</summary>
        public string TypeName { get; }

        /// <summary>What the original feature did to the solid bodies (unmirrored), if measured.</summary>
        public MassState? ExpectedChange { get; set; }

        /// <summary>
        /// Points on the faces the original feature produced (unmirrored), taken in the state
        /// right after the feature. The rebuilt feature has to have a face through each mirrored
        /// point: this tells apart variants with the same volume and centroid (twist direction
        /// of a sweep, connectors of a loft).
        /// </summary>
        public List<Vec3> FaceSamples { get; } = new List<Vec3>();

        /// <summary>Does this feature add or remove material? Sketches and planes do not.</summary>
        public virtual bool ChangesSolid => true;

        /// <summary>Things worth telling the user that do not stop the rebuild.</summary>
        public List<string> Notes { get; } = new List<string>();

        public override string ToString() => $"{TypeName} \"{Name}\"";
    }

    public sealed class SketchRecord : FeatureRecord
    {
        public SketchRecord(string name, SketchModel sketch) : base(name, "ProfileFeature")
        {
            Sketch = sketch;
        }

        public SketchModel Sketch { get; }
        public override bool ChangesSolid => false;
    }

    public sealed class RefPlaneRecord : FeatureRecord
    {
        public RefPlaneRecord(string name, Plane3 plane) : base(name, "RefPlane")
        {
            Plane = plane;
        }

        public Plane3 Plane { get; }
        public override bool ChangesSolid => false;
    }

    public sealed class RefAxisRecord : FeatureRecord
    {
        public RefAxisRecord(string name, Line3 axis) : base(name, "RefAxis")
        {
            Axis = axis;
        }

        public Line3 Axis { get; }
        public override bool ChangesSolid => false;
    }

    /// <summary>
    /// Coordinate system. Its mirror image would be left-handed, which SOLIDWORKS does not
    /// have: the copy keeps X and Y mirrored and takes Z = X × Y, i.e. the mirrored Z reversed
    /// (<see cref="Geometry.Reflection.ApplyKeepingXY"/>). Points in its XY plane keep their
    /// coordinates; a coordinate along Z changes sign.
    /// </summary>
    public sealed class CoordSysRecord : FeatureRecord
    {
        public CoordSysRecord(string name, Frame3 frame) : base(name, "CoordSys")
        {
            Frame = frame;
        }

        /// <summary>Origin and axes in model coordinates (unmirrored).</summary>
        public Frame3 Frame { get; }

        public override bool ChangesSolid => false;
    }

    /// <summary>Same numbers as swEndConditions_e.</summary>
    public enum EndCondition
    {
        Blind = 0,
        ThroughAll = 1,
        ThroughNext = 2,
        UpToVertex = 3,
        UpToSurface = 4,
        OffsetFromSurface = 5,
        MidPlane = 6,
        UpToBody = 7,
        ThroughAllBoth = 9,
        UpToSelection = 10,
        UpToNext = 11,
    }

    /// <summary>Same numbers as swStartConditions_e.</summary>
    public enum StartCondition
    {
        SketchPlane = 0,
        Surface = 1,
        Vertex = 2,
        Offset = 3,
    }

    public sealed class ExtrudeSide
    {
        public EndCondition End { get; set; }
        public double Depth { get; set; }
        public bool Draft { get; set; }
        public bool DraftOutward { get; set; }
        public double DraftAngle { get; set; }
        public bool OffsetReverse { get; set; }
        public bool TranslateSurface { get; set; }
        public double WallThickness { get; set; }

        /// <summary>Up-to/offset-from reference, if the end condition has one.</summary>
        public GeometryRef? Reference { get; set; }

        public static bool NeedsReference(EndCondition c) =>
            c == EndCondition.UpToVertex || c == EndCondition.UpToSurface || c == EndCondition.OffsetFromSurface ||
            c == EndCondition.UpToBody || c == EndCondition.UpToSelection;
    }

    /// <summary>Boss-Extrude, Cut-Extrude, thin variants.</summary>
    public sealed class ExtrudeRecord : FeatureRecord
    {
        public ExtrudeRecord(string name, string typeName, string sketchName) : base(name, typeName)
        {
            SketchName = sketchName;
        }

        public string SketchName { get; }
        public bool IsCut { get; set; }
        public bool IsThin { get; set; }
        public bool BothDirections { get; set; }
        public bool Reverse { get; set; }
        public bool FlipSideToCut { get; set; }
        public ExtrudeSide Side1 { get; } = new ExtrudeSide();
        public ExtrudeSide Side2 { get; } = new ExtrudeSide();
        public bool Merge { get; set; } = true;
        public bool FeatureScope { get; set; }
        public bool AutoSelect { get; set; } = true;
        public int ThinType { get; set; }
        public bool CapEnds { get; set; }
        public double CapThickness { get; set; }
        public StartCondition Start { get; set; }
        public double StartOffset { get; set; }
        public bool StartOffsetReverse { get; set; }
        public GeometryRef? StartReference { get; set; }
        public bool NormalCut { get; set; }
        public bool OptimizeGeometry { get; set; }

        /// <summary>Thin feature from an open profile: round the corners automatically.</summary>
        public bool AutoFillet { get; set; }

        public double AutoFilletRadius { get; set; }

        /// <summary>Selected contours, each as the set of segment ids it consists of. Empty: whole sketch.</summary>
        public List<List<string>> Contours { get; } = new List<List<string>>();

        /// <summary>"Direction of extrusion" (instead of the sketch normal): a line, edge or plane; a second entity for two-point directions.</summary>
        public GeometryRef? DirectionReference1 { get; set; }

        public GeometryRef? DirectionReference2 { get; set; }
    }

    /// <summary>Revolve, Revolve-Cut, thin variants.</summary>
    public sealed class RevolveRecord : FeatureRecord
    {
        public RevolveRecord(string name, string typeName, string sketchName) : base(name, typeName)
        {
            SketchName = sketchName;
        }

        public string SketchName { get; }
        public bool IsCut { get; set; }
        public bool IsThin { get; set; }

        /// <summary>swRevolveType_e.</summary>
        public int RevolveType { get; set; }

        public double Angle1 { get; set; }
        public double Angle2 { get; set; }
        public bool Reverse { get; set; }
        public int ThinType { get; set; }
        public double Thickness1 { get; set; }
        public double Thickness2 { get; set; }
        public bool Merge { get; set; } = true;

        /// <summary>Sketch line, reference axis or linear edge.</summary>
        public GeometryRef? Axis { get; set; }

        /// <summary>Selected contours (segment id sets). Empty: whole sketch.</summary>
        public List<List<string>> Contours { get; } = new List<List<string>>();

        /// <summary>Only a one-direction revolve below 360° has a direction that matters.</summary>
        public bool DirectionMatters => (RevolveType == 0 && Angle1 < 2 * System.Math.PI - 1e-9) || RevolveType == 2;
    }

    public sealed class FilletItem
    {
        public FilletItem(EdgeSignature? edge, FaceSignature? face, double radius)
        {
            Edge = edge;
            Face = face;
            Radius = radius;
        }

        public EdgeSignature? Edge { get; }
        public FaceSignature? Face { get; }
        public double Radius { get; }
    }

    /// <summary>Constant-radius fillet on edges and faces (also with different radii per item).</summary>
    public sealed class FilletRecord : FeatureRecord
    {
        public FilletRecord(string name, string typeName) : base(name, typeName)
        {
        }

        public double Radius { get; set; }
        public bool MultipleRadius { get; set; }
        public bool Propagate { get; set; } = true;
        public int OverflowType { get; set; }
        public bool KeepFeatures { get; set; }
        public bool RoundCorners { get; set; }
        public List<FilletItem> Items { get; } = new List<FilletItem>();
    }

    public sealed class ChamferRecord : FeatureRecord
    {
        public ChamferRecord(string name, string typeName) : base(name, typeName)
        {
        }

        /// <summary>swChamferType_e.</summary>
        public int ChamferType { get; set; }

        public double Distance1 { get; set; }
        public double Distance2 { get; set; }
        public double Angle { get; set; }
        public bool Flip { get; set; }
        public bool TangentPropagation { get; set; }
        public bool KeepFeatures { get; set; }
        public List<EdgeSignature> Edges { get; } = new List<EdgeSignature>();
        public List<FaceSignature> Faces { get; } = new List<FaceSignature>();
    }

    /// <summary>A feature that is recognised but not rebuilt; listed in the report.</summary>
    public sealed class UnsupportedRecord : FeatureRecord
    {
        public UnsupportedRecord(string name, string typeName, string reason, bool changesSolid) : base(name, typeName)
        {
            Reason = reason;
            _changesSolid = changesSolid;
        }

        private readonly bool _changesSolid;

        public string Reason { get; }
        public override bool ChangesSolid => _changesSolid;
    }
}
