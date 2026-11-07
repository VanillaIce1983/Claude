using System.Collections.Generic;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Verification;

namespace Spiegelwerk.Core.Records
{
    // Shared sheet-metal value types (blech-plan §1.2). Area 1 owns them; flanges, bends,
    // corners, form tools and the fallback use these and define no copies of their own.
    // A mirror keeps thickness, radius and angle of every bend, so all of them are carried
    // over unchanged; only references (fixed face, edges) are mirrored, and those live in
    // the records that use these types.

    /// <summary>Same numbers as swBendAllowanceTypes_e.</summary>
    public enum BendAllowanceKind
    {
        BendTable = 1,
        KFactor = 2,

        /// <summary>A bend allowance value (length).</summary>
        Direct = 3,

        /// <summary>A bend deduction value (length).</summary>
        Deduction = 4,

        /// <summary>Bend calculation table (the file name is readable only late-bound).</summary>
        BendCalculationTable = 5,
    }

    /// <summary>Same numbers as swSheetMetalReliefTypes_e.</summary>
    public enum ReliefKind
    {
        Rectangular = 1,
        Tear = 2,
        Obround = 3,
        None = 4,
        TearBend = 5,
    }

    /// <summary>
    /// ICustomBendAllowance as data. On a feature (flange, bend) null stands for "uses the
    /// default"; the value applies only through <see cref="Kind"/>, the others are kept as read.
    /// </summary>
    public sealed class BendAllowanceData
    {
        public BendAllowanceKind Kind { get; set; } = BendAllowanceKind.KFactor;
        public double KFactor { get; set; } = 0.5;

        /// <summary>Bend allowance in metres (<see cref="BendAllowanceKind.Direct"/>).</summary>
        public double BendAllowance { get; set; }

        /// <summary>Bend deduction in metres (<see cref="BendAllowanceKind.Deduction"/>).</summary>
        public double BendDeduction { get; set; }

        public string BendTableFile { get; set; } = "";

        /// <summary>Newer API only (late-bound); empty when not readable.</summary>
        public string BendCalculationTableFile { get; set; } = "";
    }

    /// <summary>Automatic relief: kind, ratio to thickness or width/depth.</summary>
    public sealed class ReliefData
    {
        public bool UseAutoRelief { get; set; } = true;
        public ReliefKind Kind { get; set; } = ReliefKind.Rectangular;
        public bool UseRatio { get; set; } = true;
        public double Ratio { get; set; } = 0.5;

        /// <summary>Metres; only readable in 2022 and later (base flange getters).</summary>
        public double Width { get; set; }

        /// <summary>Metres; only readable in 2022 and later.</summary>
        public double Depth { get; set; }

        /// <summary>
        /// Whether a tear relief rips or extends (swReliefTearTypes_e: 1 rip, 2 extend); 0 where the
        /// data interface does not have it (of the flanges only the edge flange reads it).
        /// </summary>
        public int TearType { get; set; }
    }

    /// <summary>Gauge table of a base flange or a converted body.</summary>
    public sealed class GaugeData
    {
        public string TablePath { get; set; } = "";

        /// <summary>The gauge, e.g. "Gauge 3" (IBaseFlangeFeatureData.ThicknessTableName).</summary>
        public string ThicknessName { get; set; } = "";

        public double TableRadius { get; set; }
        public bool OverrideThickness { get; set; }
        public bool OverrideRadius { get; set; }
        public bool OverrideKFactor { get; set; }
        public double KFactor { get; set; }
    }

    /// <summary>
    /// ISheetMetalFeatureData: the part's defaults (TemplateSheetMetal, 2013 and later) or one
    /// body (SheetMetal) with its override flags. Builders use the defaults if there are any,
    /// otherwise the body (<c>Defaults ?? Body</c>).
    /// </summary>
    public sealed class SheetMetalParameters
    {
        public double Thickness { get; set; }
        public double BendRadius { get; set; }
        public BendAllowanceData BendAllowance { get; set; } = new BendAllowanceData();
        public ReliefData Relief { get; set; } = new ReliefData();
        public bool UseGaugeTable { get; set; }
        public string GaugeTableFile { get; set; } = "";

        /// <summary>Thickness, radius and K from the material (late-bound); null if not available.</summary>
        public bool? UseMaterialParameters { get; set; }

        /// <summary>Body level only: GetOverrideDefaultParameter2(0) – own bend parameters.</summary>
        public bool OverrideBendParameters { get; set; }

        /// <summary>Body level only: GetOverrideDefaultParameter2(1) – own bend allowance.</summary>
        public bool OverrideBendAllowance { get; set; }

        /// <summary>Body level only: GetOverrideDefaultParameter2(2) – own auto relief.</summary>
        public bool OverrideAutoRelief { get; set; }

        /// <summary>OverrideDefaultSheetMetalParameters of the flange (late-bound); null if not available.</summary>
        public bool? OverrideDefaults { get; set; }

        /// <summary>
        /// Return code of GetOverrideDefaultParameter2 (swSheetMetalModifierError_e); non-zero:
        /// the override flags do not apply (single-body or pre-2013 part). Null: not read.
        /// </summary>
        public int? OverrideReadCode { get; set; }

        private GeometryRef? _fixedReference;

        /// <summary>
        /// ISheetMetalFeatureData.FixedReference of legacy or converted parts: only ever a face
        /// or an edge – a straight edge stays an edge, it is no axis (a reference read the
        /// general way would turn it into one, and the copy would look for a reference axis).
        /// </summary>
        public GeometryRef? FixedReference
        {
            get => _fixedReference;
            set
            {
                if (value != null && !IsFixedReferenceKind(value.Kind))
                    throw new System.ArgumentException($"Die feste Referenz eines Blechkörpers ist eine Fläche oder Kante, keine Referenz der Art {value.Kind}.");
                _fixedReference = value;
            }
        }

        /// <summary>A fixed reference of a sheet-metal body is a face or an edge, nothing else.</summary>
        public static bool IsFixedReferenceKind(GeometryRefKind kind) => kind == GeometryRefKind.Face || kind == GeometryRefKind.Edge;
    }

    /// <summary>
    /// IOneBendFeatureData of one bend sub-feature (or the values its owner hands down): the
    /// copy cannot take a bend's own values – writes to a bend sub-feature are ignored –, so
    /// <see cref="Planning.BendOverrides"/> names the bends whose values differ.
    /// </summary>
    public sealed class BendValues
    {
        public string Name { get; set; } = "";
        public bool UseDefaultRadius { get; set; } = true;

        /// <summary>Metres.</summary>
        public double Radius { get; set; }

        public bool UseDefaultAllowance { get; set; } = true;

        /// <summary>The bend's own allowance; for an owner the allowance its bends inherit, null if not known.</summary>
        public BendAllowanceData? Allowance { get; set; }

        public bool UseDefaultRelief { get; set; } = true;

        /// <summary>The bend's own relief; for an owner the relief its bends inherit, null if not known.</summary>
        public ReliefData? Relief { get; set; }

        /// <summary>
        /// IOneBendFeatureData.BendOrder: the bend's place in the bend sequence (bend table of a
        /// drawing); null if not read. Not a value the owner hands down – compared only between
        /// a bend and its counterpart in the copy, as information (<see cref="Planning.BendOverrides.OrderNotes"/>).
        /// </summary>
        public int? BendOrder { get; set; }
    }

    /// <summary>A bend sub-feature with values of its own that the copy does not get (a note, never a write).</summary>
    public sealed class BendOverride
    {
        public BendOverride(string bend, string what)
        {
            Bend = bend;
            What = what;
        }

        /// <summary>Original name of the bend sub-feature.</summary>
        public string Bend { get; }

        /// <summary>What differs, e.g. „eine eigene Biegezugabe (K 0,38)“.</summary>
        public string What { get; }

        /// <summary>„Biegung „EdgeBend2“ hat eine eigene Biegezugabe (K 0,38); über die API nicht übertragbar.“</summary>
        public string Note() => $"Biegung „{Bend}“ hat {What}; über die API nicht übertragbar.";

        public override string ToString() => Note();
    }

    /// <summary>
    /// One direction of an open-profile base flange. <see cref="End"/> uses the numbers of
    /// swEndConditions_e, mapped from swFlangeOffsetTypes_e (<see cref="Planning.SheetMetalRules.FromFlangeOffset"/>).
    /// </summary>
    public sealed class FlangeSide
    {
        public EndCondition End { get; set; } = EndCondition.Blind;

        /// <summary>Metres.</summary>
        public double Distance { get; set; }

        /// <summary>Relative to the reference face, whose outward normal stays outward: carried over unchanged.</summary>
        public bool ReverseOffset { get; set; }

        /// <summary>Up-to or offset-from reference, if the end condition has one.</summary>
        public GeometryRef? Reference { get; set; }
    }

    /// <summary>
    /// Base flange or tab (SMBaseFlange). SOLIDWORKS creates the body's SheetMetal feature and
    /// its flat pattern with it: the SheetMetal is a part of this record (a
    /// <see cref="PartOfFeatureRecord"/> pointing here, its parameters in <see cref="Body"/>),
    /// the flat pattern has a record of its own that edits the one the copy brings along.
    /// Side rules: <see cref="Planning.SheetMetalRules.BaseFlangeFlags"/>.
    /// </summary>
    public sealed class BaseFlangeRecord : FeatureRecord
    {
        public BaseFlangeRecord(string name, string typeName, string sketchName) : base(name, typeName)
        {
            SketchName = sketchName;
        }

        /// <summary>The profile sketch; an ordinary sketch record of its own (it may be shared).</summary>
        public string SketchName { get; }

        /// <summary>Every contour of the sketch is closed: a plate (or tab); otherwise an open profile with depth.</summary>
        public bool ClosedProfile { get; set; }

        /// <summary>Metres.</summary>
        public double Thickness { get; set; }

        /// <summary>Open profile: the material side of the contour (in the sketch plane). No effect on a closed profile.</summary>
        public bool ReverseThickness { get; set; }

        /// <summary>Closed profile: the side of the plate along the sketch normal; open profile: the depth direction.</summary>
        public bool ReverseDirection { get; set; }

        /// <summary>Thickness on both sides of the profile (newer versions, late-bound): no side.</summary>
        public bool SymmetricThickness { get; set; }

        /// <summary>Metres.</summary>
        public double BendRadius { get; set; }

        /// <summary>1 or 2 directions of an open profile.</summary>
        public int OffsetDirections { get; set; } = 1;

        public FlangeSide Side1 { get; } = new FlangeSide();
        public FlangeSide Side2 { get; } = new FlangeSide();

        /// <summary>Gauge table of the flange; null without one.</summary>
        public GaugeData? Gauge { get; set; }

        /// <summary>2022 and later: the flange's own allowance when it does not use the default; null otherwise or not readable.</summary>
        public BendAllowanceData? OwnBendAllowance { get; set; }

        /// <summary>2022 and later: the flange's own relief when it does not use the default; null otherwise or not readable.</summary>
        public ReliefData? OwnRelief { get; set; }

        /// <summary>OverrideDefaultSheetMetalParameters of the flange (newer versions, late-bound); null if not readable.</summary>
        public bool? OverrideDefaults { get; set; }

        /// <summary>Thickness, radius and K come from the material: the copy takes them as fixed values (note).</summary>
        public bool UsesMaterialParameters { get; set; }

        /// <summary>
        /// A tab: joined an existing sheet-metal body instead of making one (the body count did not
        /// grow with it). The copy must merge again.
        /// </summary>
        public bool MergesIntoExisting { get; set; }

        /// <summary>For the report: the SheetMetal feature an earlier base flange of the same body owns, if the tab shares one.</summary>
        public string? TabOfSheetMetal { get; set; }

        /// <summary>Name of the body's SheetMetal feature this flange brings along; empty if there is none of its own.</summary>
        public string SheetMetalName { get; set; } = "";

        /// <summary>The SheetMetal feature's own dimensions (thickness, radius): its copy gets their names back, for equations.</summary>
        public List<NamedDimension> CompanionDimensions { get; } = new List<NamedDimension>();

        /// <summary>The body's SheetMetal parameters with its override flags; null without a SheetMetal of its own.</summary>
        public SheetMetalParameters? Body { get; set; }

        /// <summary>The part's defaults (TemplateSheetMetal, 2013 and later); null for older parts – builders use <c>Defaults ?? Body</c>.</summary>
        public SheetMetalParameters? Defaults { get; set; }

        /// <summary>The SheetMetal feature comes into being with it.</summary>
        public override bool BringsParts => true;

        /// <summary>Creating a base flange may write the part's shared sheet-metal defaults.</summary>
        public override bool TouchesSheetMetalDefaults => true;
    }

    /// <summary>
    /// Flat pattern of a sheet-metal body. The copy's flat pattern comes with its base flange;
    /// this record edits it: fixed face, options, excluded faces. It does not change the folded
    /// solid, so the usual check does not see it – <see cref="FlatChange"/> does (flattened minus
    /// folded, verifies fixed face, radius and allowance at once).
    /// </summary>
    public sealed class FlatPatternRecord : FeatureRecord
    {
        public FlatPatternRecord(string name, string typeName, string owner) : base(name, typeName)
        {
            Owner = owner;
        }

        /// <summary>The base flange that made the body.</summary>
        public string Owner { get; }

        /// <summary>The owner came from the tree, not from the body (<see cref="Planning.SheetMetalTree.OwnerFromTree"/>).</summary>
        public bool OwnerFromTree { get; set; }

        private GeometryRef? _fixedFace;

        /// <summary>FixedFace2: a face, or an edge for a cylindrical part – never an axis or a plane.</summary>
        public GeometryRef? FixedFace
        {
            get => _fixedFace;
            set
            {
                if (value != null && !SheetMetalParameters.IsFixedReferenceKind(value.Kind))
                    throw new System.ArgumentException($"Die feste Fläche einer Abwicklung ist eine Fläche oder Kante, keine Referenz der Art {value.Kind}.");
                _fixedFace = value;
            }
        }

        public bool MergeFaces { get; set; }
        public bool SimplifyBends { get; set; }
        public bool CornerTreatment { get; set; }
        public int CornerTrimReliefType { get; set; }
        public double CornerTrimReliefDistance { get; set; }
        public double CornerTrimRatioToThickness { get; set; }
        public bool UseRatioToThickness { get; set; }
        public int BreakCornerType { get; set; }
        public double BreakCornerRadius { get; set; }
        public bool ShowSlitInCornerRelief { get; set; }

        /// <summary>The flat pattern's own corner trim (GetAddCornerTrim); it is a part of this record.</summary>
        public bool AddCornerTrim { get; set; }

        /// <summary>The flat pattern's own break corners (GetBreakCorners); a part of this record.</summary>
        public bool BreakCorners { get; set; }

        public List<FaceSignature> ExcludedFaces { get; } = new List<FaceSignature>();

        /// <summary>Unsuppressed (flattened) in the active configuration of the original before the run folded it.</summary>
        public bool Flattened { get; set; }

        /// <summary>Flattened minus folded state of the original, unmirrored; null: not measured („nicht geprüft“).</summary>
        public MassState? FlatChange { get; set; }

        /// <summary>Points on the faces of the flattened original, unmirrored.</summary>
        public List<Vec3> FlatSamples { get; } = new List<Vec3>();

        /// <summary>Its sketches and own corner features come into being with it.</summary>
        public override bool BringsParts => true;

        /// <summary>The folded solid stays as it is.</summary>
        public override bool ChangesSolid => false;
    }
}
