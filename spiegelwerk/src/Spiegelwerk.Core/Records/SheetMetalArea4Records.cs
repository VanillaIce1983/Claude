using System.Collections.Generic;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;

namespace Spiegelwerk.Core.Records
{
    // Area 4 of the sheet-metal plan (A4 (2) §3, blech-plan S8): tab and slot and the sheet-metal gusset.
    // A reflection keeps every value of both; it maps edges, faces and points onto their mirror images.
    // What it does not tell: from which end of a tab edge D1 counts when the copy's edge runs the other
    // way (TabSlotRules.Offsets), and from which end of a bend an offset counts when a gusset has no
    // reference point of its own – the help says „the end of the sheet metal bend“, and which end that is
    // depends on how the copy's bend runs (A4 P9). The copy gets an explicit reference point there, and
    // the place of the new gusset on its bend is always checked (GussetRules).

    /// <summary>Same numbers as swTabSlotFeatureHeightType_e.</summary>
    public static class TabSlotHeightTypes
    {
        public const int Blind = 0, UpToSurface = 1, OffsetFromSurface = 2;
    }

    /// <summary>Same numbers as swTabSlotFeatureSpacingType_e.</summary>
    public static class TabSlotSpacingTypes
    {
        public const int EqualSpacing = 0, SpacingLength = 1;
    }

    /// <summary>Same numbers as swTabEdgesType_e.</summary>
    public static class TabEdgesTypes
    {
        public const int SharpEdge = 0, FilletEdge = 1, ChamferEdge = 2;
    }

    /// <summary>Same numbers as swSheetMetalGussetProfileDimType_e (without its prefix).</summary>
    public static class GussetProfileDimTypes
    {
        public const int IndentDepth = 0, ProfileDimensions = 1;
    }

    /// <summary>Same numbers as swSheetMetalRibGussetType_e (without its prefix).</summary>
    public static class GussetTypes
    {
        public const int Rounded = 0, Flat = 1;
    }

    /// <summary>Same numbers as swSheetMetalGussetProfileType_e (without its prefix).</summary>
    public static class GussetProfileTypes
    {
        public const int Rib = 0, Custom = 1;
    }

    /// <summary>
    /// One group of a tab and slot (ITabAndSlotGroupData): the edge the tabs stand on, the face the slots are
    /// cut into, optionally the tab face and two reference points that limit the span, and the values.
    /// Read under the feature's AccessSelections – the state before it.
    /// </summary>
    public sealed class TabSlotGroup
    {
        public EdgeSignature TabEdge { get; set; } = null!;
        public FaceSignature SlotFace { get; set; } = null!;

        /// <summary>The planar face along the tab edge whose thickness the tabs take (help: shared with the edge); null if none was read.</summary>
        public FaceSignature? TabFace { get; set; }

        /// <summary>A vertex or sketch point where the tabs start; null: the start of the edge.</summary>
        public GeometryRef? StartReference { get; set; }

        /// <summary>A vertex or sketch point where they end; null: the end of the edge.</summary>
        public GeometryRef? EndReference { get; set; }

        /// <summary>D1 and D2 count: the span is shortened at both ends.</summary>
        public bool Offset { get; set; }

        /// <summary>Metres from the start (of the edge or of <see cref="StartReference"/>).</summary>
        public double D1OffsetFromStart { get; set; }

        /// <summary>Metres from the end.</summary>
        public double D2OffsetFromEnd { get; set; }

        /// <summary><see cref="TabSlotSpacingTypes"/>.</summary>
        public int SpacingType { get; set; }

        public int SpacingNumberOfInstances { get; set; }

        /// <summary>Metres, with <see cref="TabSlotSpacingTypes.SpacingLength"/>.</summary>
        public double Spacing { get; set; }

        public double TabLength { get; set; }
        public double TabThickness { get; set; }

        /// <summary><see cref="TabSlotHeightTypes"/>.</summary>
        public int TabHeightType { get; set; }

        public double TabHeightValue { get; set; }

        /// <summary>Metres from the slot face, with <see cref="TabSlotHeightTypes.OffsetFromSurface"/>.</summary>
        public double TabOffsetValue { get; set; }

        /// <summary>The offset from the slot face reversed (OffsetFromSurface only): measured from the face, whose outward normal stays outward – unchanged by the mirror.</summary>
        public bool TabReverseDirection { get; set; }

        /// <summary><see cref="TabEdgesTypes"/>.</summary>
        public int TabEdgesType { get; set; }

        public double TabFilletEdgeTreatmentValue { get; set; }
        public double TabChamferEdgeTreatmentValue { get; set; }
        public double SlotClearance { get; set; }

        /// <summary>A copy with D1 and D2 swapped or not (the values the copy's group should read back).</summary>
        public TabSlotGroup WithOffsets(double d1, double d2)
        {
            var g = (TabSlotGroup)MemberwiseClone();
            g.D1OffsetFromStart = d1;
            g.D2OffsetFromEnd = d2;
            return g;
        }
    }

    /// <summary>
    /// Tab and slot (ITabAndSlotFeatureData; the type name is not confirmed, B64 – captured by its data).
    /// SOLIDWORKS makes the slots as a feature of their own with it (help example: „Tab and Slot-Tab1“ and
    /// its slot); that one is a part of this record (<see cref="PartOwnerKind.TabAndSlot"/>), found in the
    /// tree as its child or by its name (<see cref="Planning.SheetMetalTree"/>), and the new tab and slot
    /// brings its own. A tab and slot changes two bodies: the tab body and the slot body (A4 P12).
    /// </summary>
    public sealed class TabAndSlotRecord : FeatureRecord
    {
        public TabAndSlotRecord(string name, string typeName) : base(name, typeName)
        {
        }

        public List<TabSlotGroup> Groups { get; } = new List<TabSlotGroup>();

        /// <summary>The slot feature that comes with it, after it in the tree; null if none was found.</summary>
        public string? SlotFeatureName { get; set; }

        /// <summary>Its slot comes into being with it.</summary>
        public override bool BringsParts => SlotFeatureName != null;

        /// <summary>The state „after it“ includes the slots.</summary>
        public override string MeasureUntil => SlotFeatureName ?? Name;
    }

    /// <summary>What the offset of a gusset without a reference point of its own counts from (A4 P9).</summary>
    public enum GussetAnchor
    {
        /// <summary>Not determinable: no bend line, no offset, or no candidate at the offset's distance.</summary>
        Unknown,

        /// <summary>The gusset has a reference point of its own.</summary>
        Own,

        /// <summary>One end of the bend (the help: „the end of the sheet metal bend“).</summary>
        BendEnd,

        /// <summary>An earlier gusset on the same bend (the help: „the edge of the gusset last inserted“).</summary>
        PreviousGusset,
    }

    /// <summary>The place of an earlier gusset on the same bend, as the capture measured it, with its width and thickness.</summary>
    public sealed class GussetSection
    {
        public GussetSection(string name, Vec3 point, double width, double thickness)
        {
            Name = name;
            Point = point;
            Width = width;
            Thickness = thickness;
        }

        public string Name { get; }

        /// <summary>Its section on the bend line (model coordinates, unmirrored).</summary>
        public Vec3 Point { get; }

        /// <summary>Its indent width (IndentWidth): its extent along the bend, symmetric to the section. Metres.</summary>
        public double Width { get; }

        /// <summary>Its wall thickness (GussetThickness). Metres.</summary>
        public double Thickness { get; }
    }

    /// <summary>
    /// Sheet-metal gusset (SMGusset, ISMGussetFeatureData). Every value 1:1; the legs, the reference line and the
    /// reference point are mirrored. Where it sits on its bend is captured as geometry (A4 P9): the bend line
    /// and a vertex at either end (state before the gusset), its section on the bend line (the middle of its
    /// faces' extent along the line, state right after it), and – for a gusset without a reference point of
    /// its own – what its offset counts from (<see cref="Anchor"/>).
    /// </summary>
    public sealed class SheetMetalGussetRecord : FeatureRecord
    {
        public SheetMetalGussetRecord(string name, string typeName) : base(name, typeName)
        {
        }

        /// <summary>The one or two faces it stands on (ISMGussetFeatureData.SupportingFaces), in their order.</summary>
        public List<FaceSignature> SupportingFaces { get; } = new List<FaceSignature>();

        /// <summary>A line perpendicular to its section plane; null: the bend line next to the legs.</summary>
        public GeometryRef? ReferenceLine { get; set; }

        /// <summary>Where it is placed; null: the offset counts from an assumed point (<see cref="Anchor"/>).</summary>
        public GeometryRef? ReferencePoint { get; set; }

        public bool UseOffset { get; set; }

        /// <summary>Metres.</summary>
        public double OffsetDistance { get; set; }

        /// <summary>The offset on the other side of the reference point.</summary>
        public bool FlipOffset { get; set; }

        /// <summary><see cref="GussetProfileDimTypes"/>.</summary>
        public int ProfileDimensionScheme { get; set; }

        public double IndentDepth { get; set; }

        /// <summary>The d1 leg (profile dimensions).</summary>
        public double ProfileLengthDim { get; set; }

        /// <summary>The d2 leg (profile dimensions).</summary>
        public double ProfileHeightDim { get; set; }

        /// <summary>Radians.</summary>
        public double ProfileAngleDim { get; set; }

        public bool UseAngleDimForProfile { get; set; }

        /// <summary>d1 and d2 swapped in the dimensioning scheme: tied to the order of the legs, which the copy keeps.</summary>
        public bool FlipDimSides { get; set; }

        public double IndentWidth { get; set; }
        public double GussetThickness { get; set; }
        public bool DraftSideFaces { get; set; }

        /// <summary>Radians.</summary>
        public double DraftAngle { get; set; }

        public bool FilletInnerCorners { get; set; }
        public double InnerCornerFilletRadius { get; set; }
        public bool FilletOuterCorners { get; set; }
        public double OuterCornerFilletRadius { get; set; }

        /// <summary><see cref="GussetTypes"/>.</summary>
        public int GussetType { get; set; }

        public bool FilletGussetEdges { get; set; }
        public double EdgeFilletRadius { get; set; }

        /// <summary><see cref="GussetProfileTypes"/>: a custom profile is not rebuilt (its meaning is open, B66).</summary>
        public int ProfileType { get; set; }

        public bool OverrideDocSettings { get; set; }
        public bool ShowProfile { get; set; }
        public bool ShowCenter { get; set; }

        // ---- where it sits on its bend (A4 P9)

        /// <summary>The bend's axis, in the state before the gusset; null if the bend was not found (toroidal bend, no bend face).</summary>
        public Line3? BendLine { get; set; }

        /// <summary>A vertex of the bend at the end with the smaller coordinate along <see cref="BendLine"/>.</summary>
        public Vec3? BendEndA { get; set; }

        /// <summary>A vertex of the bend at the other end.</summary>
        public Vec3? BendEndB { get; set; }

        /// <summary>Its section on <see cref="BendLine"/>, in the state right after it; null if not measured.</summary>
        public Vec3? SectionPoint { get; set; }

        /// <summary>Earlier gussets on the same bend, in creation order.</summary>
        public List<GussetSection> PreviousOnBend { get; } = new List<GussetSection>();

        /// <summary>What its offset counts from (<see cref="Planning.GussetRules.AnchorOf"/>).</summary>
        public GussetAnchor Anchor { get; set; }

        /// <summary>The bend-end vertex its offset counts from (<see cref="GussetAnchor.BendEnd"/>), unmirrored.</summary>
        public Vec3? AnchorPoint { get; set; }

        /// <summary>The earlier gusset its offset counts from (<see cref="GussetAnchor.PreviousGusset"/>).</summary>
        public string? AnchorGusset { get; set; }
    }
}
