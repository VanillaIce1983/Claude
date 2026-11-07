using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Sketch;

namespace Spiegelwerk.Core.Records
{
    // Flanges on edges (A2, blech-plan S2, S3): hem, miter flange and edge flange. A reflection
    // keeps every length, angle and radius and maps the edges, faces and profiles onto their
    // mirror images, so the scalars go over unchanged; only the edge fingerprints and the profile
    // sketches are mirrored – and the edge flange's sketch angle, which is a rotation about the
    // directed hinge (FlangeProfiles.AngleCandidates). The bend sub-features are read for notes
    // only – their own values cannot be written (B23).

    /// <summary>Same numbers as swHemTypes_e.</summary>
    public static class HemTypes
    {
        public const int Open = 0, Closed = 1, TearDrop = 2, Rolled = 3, Double = 4;
    }

    /// <summary>
    /// Same numbers as swFlangeOffsetTypes_e: how the length of an edge flange (and its position
    /// offset) ends. Up to edge and merge exists only from the 2024 interop on.
    /// </summary>
    public static class FlangeOffsetTypes
    {
        public const int Blind = 1, UpToVertex = 2, UpToSurface = 3, FromSurface = 4, MidPlane = 5, UptoEdgeAndMerge = 6;
    }

    /// <summary>Same numbers as swFlangeDimTypes_e: where the length of an edge flange is measured.</summary>
    public static class FlangeDimTypes
    {
        public const int OuterVirtualSharp = 1, InnerVirtualSharp = 2, BendTangentArc = 3;
    }

    /// <summary>Same numbers as swFlangePositionTypes_e: where the flange sits relative to its edge.</summary>
    public static class FlangePositionTypes
    {
        public const int MaterialInside = 1, MaterialOutside = 2, BendOutside = 3, BendCenterLine = 4, BendSharp = 5, BendTangent = 6;
    }

    /// <summary>Same bits as swInsertEdgeFlangeOptions_e (InsertSheetMetalEdgeFlange and …2).</summary>
    public static class EdgeFlangeOptions
    {
        public const int UseDefaultRadius = 1, FlipDir = 2, DoOffset = 4, ReverseOffsetDir = 8, TearClip = 16, TrimSideBend = 32,
            UseReliefRatio = 64, UseDefaultRelief = 128;
    }

    /// <summary>Same numbers as swReliefTearTypes_e.</summary>
    public static class ReliefTearTypes
    {
        public const int Rip = 1, Extend = 2;
    }

    /// <summary>
    /// The profile sketch of one edge of an edge flange: made by the flange (a sub-feature,
    /// „Laschenprofil bearbeiten“), or – the layout SOLIDWORKS sometimes shows – a sketch at the top
    /// level before it (A2 P13). It belongs to the flange alone: the new flange makes its own on the
    /// mirrored edge, and this one is written into it.
    /// </summary>
    public sealed class FlangeProfile
    {
        public FlangeProfile(string sketchName, SketchModel sketch, EdgeSignature hinge)
        {
            SketchName = sketchName;
            Sketch = sketch;
            Hinge = hinge;
        }

        /// <summary>Original name: the copy's profile gets it again.</summary>
        public string SketchName { get; }

        /// <summary>
        /// Frame and content as the finished flange shows them – read without rollback, so a position
        /// offset is included (A2 P2) –, with the relations and dimensions of the read in the state
        /// before the flange, where the edges and vertices they name exist (<see cref="Planning.FlangeProfiles.Merge"/>).
        /// </summary>
        public SketchModel Sketch { get; }

        /// <summary>The edge the profile stands on, read under AccessSelections (state before the flange).</summary>
        public EdgeSignature Hinge { get; }
    }

    /// <summary>
    /// Edge flange (IEdgeFlangeFeatureData, blech-plan S3). One profile per edge, in the order of
    /// its edges; one bend per edge (OneBend), which carries faces. The bend angle is one value for
    /// all edges. What the copy needs besides the values: the profiles and the edges they stand on.
    /// </summary>
    public sealed class EdgeFlangeRecord : FeatureRecord
    {
        public EdgeFlangeRecord(string name, string typeName) : base(name, typeName)
        {
        }

        /// <summary>One per edge, in the order of IEdgeFlangeFeatureData.Edges.</summary>
        public List<FlangeProfile> Profiles { get; } = new List<FlangeProfile>();

        /// <summary>Radians, as read: a rotation about the directed hinge (θ or 2π − θ for the same bend).</summary>
        public double BendAngle { get; set; }

        public bool UseDefaultRadius { get; set; } = true;

        /// <summary>Metres.</summary>
        public double BendRadius { get; set; }

        /// <summary>How the length ends (<see cref="FlangeOffsetTypes"/>).</summary>
        public int OffsetType { get; set; } = FlangeOffsetTypes.Blind;

        /// <summary>The flange length, metres (blind, mid plane, offset from surface).</summary>
        public double OffsetDistance { get; set; }

        /// <summary>Where the length is measured (<see cref="FlangeDimTypes"/>).</summary>
        public int OffsetDimType { get; set; } = FlangeDimTypes.OuterVirtualSharp;

        /// <summary>The flange direction: its reference is undocumented; the profile check decides it in the copy.</summary>
        public bool ReverseOffset { get; set; }

        /// <summary>The end of the length (vertex, face, plane; an edge for up to edge and merge).</summary>
        public GeometryRef? OffsetReference { get; set; }

        /// <summary><see cref="FlangePositionTypes"/>.</summary>
        public int PositionType { get; set; } = FlangePositionTypes.MaterialInside;

        public bool TrimSideBends { get; set; }

        /// <summary>The flange stands off its edge by a position offset (the profile is moved along its normal).</summary>
        public bool UsePositionOffset { get; set; }

        /// <summary><see cref="FlangeOffsetTypes"/> of the position offset.</summary>
        public int PositionOffsetType { get; set; } = FlangeOffsetTypes.Blind;

        /// <summary>Metres.</summary>
        public double PositionOffsetDistance { get; set; }

        /// <summary>Its direction: decided in the copy by the profile check, like <see cref="ReverseOffset"/>.</summary>
        public bool ReversePositionOffset { get; set; }

        public GeometryRef? PositionOffsetReference { get; set; }

        /// <summary>Gap between the flanges of neighbouring edges, metres.</summary>
        public double GapDistance { get; set; }

        /// <summary>The flange's own relief; null: the default.</summary>
        public ReliefData? OwnRelief { get; set; }

        /// <summary>The flange's own allowance; null: the default.</summary>
        public BendAllowanceData? OwnAllowance { get; set; }

        /// <summary>Newer versions only (late-bound); null where not readable.</summary>
        public bool? LockAngle { get; set; }

        /// <summary>Newer versions only (late-bound); null where not readable.</summary>
        public bool? NormalToFlangePlane { get; set; }

        /// <summary>Newer versions only (late-bound); null where not readable.</summary>
        public bool? PerpendicularToFace { get; set; }

        /// <summary>Newer versions only (late-bound): the face or edge the angle refers to; null without one.</summary>
        public GeometryRef? AngleReference { get; set; }

        /// <summary>Sub-features whose definition is IOneBendFeatureData.</summary>
        public int BendCount { get; set; }

        public List<BendValues> Bends { get; } = new List<BendValues>();
        public List<BendOverride> BendOverrides { get; } = new List<BendOverride>();

        public override IReadOnlyList<string> OwnedSketches => Profiles.Select(p => p.SketchName).ToList();

        /// <summary>The bends carry faces of their own.</summary>
        public override bool SampleSubFeatures => true;
    }

    /// <summary>One width sketch of a hem ("Edit Hem Width"): the sketch, the edge it belongs to (-1: not paired).</summary>
    public sealed class HemWidthSketch
    {
        public HemWidthSketch(string name, SketchModel sketch, int edgeIndex)
        {
            Name = name;
            Sketch = sketch;
            EdgeIndex = edgeIndex;
        }

        /// <summary>Original name: the copy's width sketch gets it again.</summary>
        public string Name { get; }

        /// <summary>Read in the state the hem saw (rolled back after the sketch), in source coordinates.</summary>
        public SketchModel Sketch { get; }

        /// <summary>Index into <see cref="HemRecord.Edges"/>; -1 while not paired.</summary>
        public int EdgeIndex { get; set; }
    }

    /// <summary>
    /// Hem (IHemFeatureData). It brings its width sketches along (sub-features, one per edge as far
    /// as known): they belong to it alone and are written into the ones the new hem makes. Its
    /// faces belong to its bend sub-feature (HemBend), not to the hem: face samples are taken there
    /// too. Its relief is not readable (the data interface has none): the copy gets the default.
    /// </summary>
    public sealed class HemRecord : FeatureRecord
    {
        public HemRecord(string name, string typeName) : base(name, typeName)
        {
        }

        /// <summary>The hem edges, in the order of IHemFeatureData.Edges (state before the hem).</summary>
        public List<EdgeSignature> Edges { get; } = new List<EdgeSignature>();

        /// <summary>swHemTypes_e (<see cref="HemTypes"/>).</summary>
        public int HemType { get; set; }

        /// <summary>swHemPositionTypes_e: 0 inside, 1 outside. Defined by the sheet's faces, which R maps onto the copy's: unchanged.</summary>
        public int Position { get; set; }

        /// <summary>ReverseDirection: probably relative to the face; carried over unchanged, the other way is an alternative.</summary>
        public bool Reverse { get; set; }

        /// <summary>Metres; used by open, closed and double hems.</summary>
        public double Length { get; set; }

        /// <summary>Metres; used by open hems.</summary>
        public double Gap { get; set; }

        /// <summary>Radians; used by tear-drop and rolled hems.</summary>
        public double Angle { get; set; }

        /// <summary>Metres; used by tear-drop and rolled hems.</summary>
        public double Radius { get; set; }

        /// <summary>Metres; between the hems of neighbouring edges.</summary>
        public double MiterGap { get; set; }

        /// <summary>The hem's own allowance; null: the default.</summary>
        public BendAllowanceData? OwnAllowance { get; set; }

        /// <summary>The width sketches, paired with <see cref="Edges"/> where that was possible.</summary>
        public List<HemWidthSketch> WidthSketches { get; } = new List<HemWidthSketch>();

        /// <summary>Every width sketch has its edge (<see cref="Planning.FlangeProfiles.Assign"/>); otherwise the copy keeps the ones SOLIDWORKS makes.</summary>
        public bool WidthSketchesPaired { get; set; } = true;

        /// <summary>Sub-features whose definition is IOneBendFeatureData (counted by definition, not by type name).</summary>
        public int BendCount { get; set; }

        /// <summary>The bend sub-features as read, in sub-feature order.</summary>
        public List<BendValues> Bends { get; } = new List<BendValues>();

        /// <summary>Bends with values of their own (notes; not transferable).</summary>
        public List<BendOverride> BendOverrides { get; } = new List<BendOverride>();

        public override IReadOnlyList<string> OwnedSketches => WidthSketches.Select(w => w.Name).ToList();

        /// <summary>The faces live on the hem's bend sub-feature (soheil, VERIFIED).</summary>
        public override bool SampleSubFeatures => true;
    }

    /// <summary>
    /// Miter flange (IMiterFlangeFeatureData). Its open profile is an ordinary user sketch (absorbed,
    /// but a record of its own, like an extrusion's): rebuilt first, then selected for the new flange.
    /// One bend sub-feature per edge (Miterbend&lt;n&gt;), which carries faces too.
    /// </summary>
    public sealed class MiterFlangeRecord : FeatureRecord
    {
        public MiterFlangeRecord(string name, string typeName, string sketchName) : base(name, typeName)
        {
            SketchName = sketchName;
        }

        /// <summary>The profile sketch (open, on a plane normal to the first edge at the chain's end).</summary>
        public string SketchName { get; }

        /// <summary>The chain, in order (state before the flange). The start of the chain carries the profile.</summary>
        public List<EdgeSignature> Edges { get; } = new List<EdgeSignature>();

        public bool UseDefaultRadius { get; set; } = true;

        /// <summary>Metres.</summary>
        public double BendRadius { get; set; }

        /// <summary>swFlangePositionTypes_e (1 material inside, 2 outside, 3 bend outside).</summary>
        public int PositionType { get; set; } = 1;

        public bool TrimSideBends { get; set; }

        /// <summary>Rip gap between the flanges of neighbouring edges, metres.</summary>
        public double GapDistance { get; set; }

        /// <summary>Partial flange: offset at the start of the chain, metres.</summary>
        public double StartOffset { get; set; }

        /// <summary>Partial flange: offset at the end of the chain, metres.</summary>
        public double EndOffset { get; set; }

        /// <summary>The flange's own relief; null: the default.</summary>
        public ReliefData? OwnRelief { get; set; }

        /// <summary>The flange's own allowance; null: the default.</summary>
        public BendAllowanceData? OwnAllowance { get; set; }

        public int BendCount { get; set; }
        public List<BendValues> Bends { get; } = new List<BendValues>();
        public List<BendOverride> BendOverrides { get; } = new List<BendOverride>();

        /// <summary>The bends carry faces of their own.</summary>
        public override bool SampleSubFeatures => true;
    }
}
