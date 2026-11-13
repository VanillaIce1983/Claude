using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;

namespace Spiegelwerk.Core.Records
{
    // Bends from a sketch (A3, blech-plan S4): the sketched bend here, the jog next to the flanges
    // (SheetMetalFlangeRecords.cs). A reflection keeps angle, radius, position and allowance; it
    // maps the fixed face and the point that says which side stays onto their mirror images. What
    // it does not tell is the flag that turns the bend up or down: the reference SOLIDWORKS measures
    // it against is not documented (B39), so the copy decides it by witnesses – points on the
    // faces the original bend made, which must lie on the copy's faces once mirrored (A3 P2).

    /// <summary>In which space a fixed point of a bend or jog was read (B38).</summary>
    public enum FixedPointSpace
    {
        /// <summary>Not readable, or neither reading lies on the fixed face.</summary>
        Unknown,

        /// <summary>Sketch coordinates (z = 0), as the help documents for the sketched bend.</summary>
        Sketch,

        /// <summary>Model coordinates.</summary>
        Model,
    }

    /// <summary>
    /// Sketched bend (SM3dBend, ISketchedBendFeatureData). Its sketch is an ordinary sketch record of
    /// its own (absorbed, like an extrusion's), rebuilt first – on the mirrored face it was drawn on,
    /// where that face is found (<see cref="SketchFace"/>) – and then selected together with the fixed
    /// face, picked at a point on the side that stays. One bend sub-feature (SketchBend) per line.
    /// </summary>
    public sealed class SketchedBendRecord : FeatureRecord
    {
        public SketchedBendRecord(string name, string typeName, string sketchName) : base(name, typeName)
        {
            SketchName = sketchName;
        }

        /// <summary>The sketch with the bend lines.</summary>
        public string SketchName { get; }

        /// <summary>Radians, as read.</summary>
        public double Angle { get; set; }

        public bool UseDefaultRadius { get; set; } = true;

        /// <summary>Metres.</summary>
        public double Radius { get; set; }

        /// <summary>swFlangePositionTypes_e (1 material inside, 2 outside, 3 bend outside, 4 centre line).</summary>
        public int PositionType { get; set; } = 4;

        /// <summary>ReverseDirection of the original: its reference is not documented – the witnesses decide the copy's.</summary>
        public bool Reverse { get; set; }

        /// <summary>The bend's own allowance; null: the default.</summary>
        public BendAllowanceData? OwnAllowance { get; set; }

        /// <summary>The face that stays (GetFixedFace), in the state before the bend; null only in a read-back.</summary>
        public FaceSignature? FixedFace { get; set; }

        /// <summary>The point the fixed face was picked at, model coordinates; null if neither reading lies on the face.</summary>
        public Vec3? FixedPoint { get; set; }

        /// <summary>In which space the point was read (sketch space first, as the help says).</summary>
        public FixedPointSpace FixedPointSpace { get; set; }

        /// <summary>Both readings lie on the face, in different regions: the sketch reading was taken (a note).</summary>
        public bool FixedPointAmbiguous { get; set; }

        /// <summary>
        /// The face the bend sketch was drawn on, in the state right after the sketch (before the bend);
        /// null if it was drawn on a plane or the face did not read. The copy's sketch goes on its mirror image.
        /// </summary>
        public FaceSignature? SketchFace { get; set; }

        /// <summary>The bend lines (non-construction lines of the sketch), model coordinates, each through its midpoint.</summary>
        public List<Line3> BendLines { get; } = new List<Line3>();

        /// <summary>Sheet thickness of the body, metres; 0 if not read (the side clearance then is 1 mm).</summary>
        public double Thickness { get; set; }

        /// <summary>
        /// Points on its own faces and those of its bend sub-features, in the state right after it (model
        /// coordinates, unmirrored); empty if they could not be taken – then the direction is the rule's guess.
        /// </summary>
        public List<Vec3> Witnesses { get; } = new List<Vec3>();

        /// <summary>
        /// Points on the other faces of its body it made or moved (same state): they lie on the copy only
        /// if the copy has every feature of <see cref="BodyFeatures"/> – the build leaves them out otherwise.
        /// </summary>
        public List<Vec3> MovedWitnesses { get; } = new List<Vec3>();

        /// <summary>The features that made its body before it (no sketches, reference geometry, folders, SheetMetal); empty: not read.</summary>
        public List<string> BodyFeatures { get; } = new List<string>();

        /// <summary>
        /// <see cref="FixedPoint"/> still lies on a face of the original's body right after the bend
        /// (10 µm) – off the bend zone, on the side that stays: only then it is a witness (K10).
        /// </summary>
        public bool FixedPointStays { get; set; }

        /// <summary>Sub-features whose definition is IOneBendFeatureData (SketchBend).</summary>
        public int BendCount { get; set; }

        public List<BendValues> Bends { get; } = new List<BendValues>();
        public List<BendOverride> BendOverrides { get; } = new List<BendOverride>();

        /// <summary>The bend faces belong to the SketchBend sub-features (B24).</summary>
        public override bool SampleSubFeatures => true;
    }
}

namespace Spiegelwerk.Core.Records
{
    // Corners, folds and the cross break (A3 (2) §2, blech-plan S5). A reflection keeps every value
    // (types, gaps, distances, ratios, slot sizes, angles, radii) and maps the faces, edges and bends
    // they name onto their mirror images, in the same roles: nothing here has a side or a direction
    // that the mirror turns, except the cross break's, which is measured against the face's outward
    // normal – and a reflection keeps outward normals outward (B48).

    /// <summary>swClosedCornerTypes_e.</summary>
    public static class ClosedCornerTypes
    {
        public const int Butt = 1, Overlap = 2, Underlap = 3;
    }

    /// <summary>swBreakCornerTypes_e.</summary>
    public static class BreakCornerTypes
    {
        public const int Fillet = 0, Chamfer = 1;
    }

    /// <summary>swCornerReliefType_e (RectangularRelief only from the 2024 interop).</summary>
    public static class CornerReliefTypes
    {
        public const int CircularRelief = 0, SquareRelief = 1, BendWaistRelief = 2, TearRelief = 3, ConstantWidthRelief = 4, ObroundRelief = 5;
        public const int Mixed = 6, FullRoundRelief = 7, SuitCaseRelief = 8, RectangularRelief = 9;
    }

    /// <summary>swCornerReliefBendType_e (2022 and later).</summary>
    public static class CornerReliefBendTypes
    {
        public const int TwoBend = 0, ThreeBend = 1;
    }

    /// <summary>
    /// A bend sub-feature (OneBend, SketchBend, EdgeBend …) a fold or unfold names: its owner, its
    /// place among the owner's bends (counted by definition, IOneBendFeatureData) and its faces in the
    /// state the referring feature saw them. The ordinal is usable only for an owner with a single
    /// bend: a reflection may reverse the order of several (A3 P8, K25).
    /// </summary>
    public sealed class BendRef
    {
        public BendRef(string owner, string name, int ordinal, int ownerBendCount)
        {
            Owner = owner;
            Name = name;
            Ordinal = ordinal;
            OwnerBendCount = ownerBendCount;
        }

        /// <summary>The feature the bend belongs to (original name; empty if it did not read).</summary>
        public string Owner { get; }

        /// <summary>The bend's name in the original.</summary>
        public string Name { get; }

        /// <summary>Its place among the owner's bends, 0-based; −1 if unknown.</summary>
        public int Ordinal { get; }

        public int OwnerBendCount { get; }

        /// <summary>The faces of the bend, unmirrored, in the state of the feature that names it.</summary>
        public List<FaceSignature> Faces { get; } = new List<FaceSignature>();

        /// <summary>The owner has exactly this one bend: its counterpart's single bend is this one's.</summary>
        public bool OrdinalUsable => Owner.Length > 0 && OwnerBendCount == 1 && Ordinal == 0;

        /// <summary>Faces mirrored; names, owner and place kept.</summary>
        public BendRef Mirror(Reflection r)
        {
            var m = new BendRef(Owner, Name, Ordinal, OwnerBendCount);
            m.Faces.AddRange(Faces.Select(f => f.Mirror(r)));
            return m;
        }
    }

    /// <summary>
    /// Cross break (CrossBreak, ICrossBreakFeatureData): lines on a face for the drawing, the folded
    /// solid unchanged. Angle, radius and direction go along unchanged – the direction counts against
    /// the face's outward normal (B48), which a reflection keeps outward. Its break lines live in a
    /// sketch sub-feature, if it has one (B48): the sketch belongs to it (<see cref="OwnedSketches"/>);
    /// the new cross break brings its own, which the mirrored lines replace where they differ.
    /// </summary>
    public sealed class CrossBreakRecord : FeatureRecord
    {
        public CrossBreakRecord(string name, string typeName) : base(name, typeName)
        {
        }

        /// <summary>Radians.</summary>
        public double Angle { get; set; }

        /// <summary>Metres.</summary>
        public double Radius { get; set; }

        public bool Reverse { get; set; }

        /// <summary>The face it lies on, in the state before it; null only in a read-back.</summary>
        public FaceSignature? Face { get; set; }

        /// <summary>Its sketch sub-feature with the break lines; null if it has none.</summary>
        public string? LineSketchName { get; set; }

        /// <summary>That sketch as read (state before the cross break); null if it did not read – the copy keeps the lines SOLIDWORKS draws.</summary>
        public Sketch.SketchModel? LineSketch { get; set; }

        /// <summary>The break lines, model coordinates (unmirrored), each from one end to the other; empty if not read.</summary>
        public List<(Vec3 a, Vec3 b)> Lines { get; } = new List<(Vec3 a, Vec3 b)>();

        public override IReadOnlyList<string> OwnedSketches => LineSketchName == null ? System.Array.Empty<string>() : new[] { LineSketchName };

        /// <summary>Lines on the flat pattern: the folded solid stays as it is (CADapter).</summary>
        public override bool ChangesSolid => false;
    }

    /// <summary>
    /// Closed corner (CornerFeat, IClosedCornerFeatureData). The faces to extend, in their order; the
    /// type, gap, overlap ratio and open bend region unchanged – overlap and underlap name the roles of
    /// the faces, which the mapping keeps (A3 §6). The options the API does not expose (faces to match,
    /// coplanar faces, narrow corner, auto propagation) stay at the defaults of SOLIDWORKS.
    /// </summary>
    public sealed class ClosedCornerRecord : FeatureRecord
    {
        public ClosedCornerRecord(string name, string typeName) : base(name, typeName)
        {
        }

        /// <summary><see cref="ClosedCornerTypes"/>.</summary>
        public int CornerType { get; set; } = ClosedCornerTypes.Butt;

        /// <summary>Metres.</summary>
        public double Gap { get; set; }

        public double OverlapRatio { get; set; }
        public bool OpenBendRegion { get; set; }

        /// <summary>IClosedCornerFeatureData.Faces, in the state before the corner.</summary>
        public List<FaceSignature> Faces { get; } = new List<FaceSignature>();
    }

    /// <summary>
    /// Break corner in the folded state (BreakCorner, IBreakCornerFeatureData) – the same interface in
    /// the flattened state is a corner trim, which is not rebuilt (A3 P3). Edges and faces mapped,
    /// values unchanged.
    /// </summary>
    public sealed class BreakCornerRecord : FeatureRecord
    {
        public BreakCornerRecord(string name, string typeName) : base(name, typeName)
        {
        }

        /// <summary><see cref="BreakCornerTypes"/>.</summary>
        public int BreakType { get; set; }

        /// <summary>Metres: the radius of a fillet, the distance of a chamfer.</summary>
        public double Distance { get; set; }

        public bool InternalCornersOnly { get; set; }
        public bool CenteredOnBendLines { get; set; }
        public List<EdgeSignature> Edges { get; } = new List<EdgeSignature>();
        public List<FaceSignature> Faces { get; } = new List<FaceSignature>();
    }

    /// <summary>One corner of a corner relief (ISMCornerReliefData): its two or three faces in their roles, its relief.</summary>
    public sealed class CornerReliefItem
    {
        public FaceSignature Face1 { get; set; } = null!;
        public FaceSignature Face2 { get; set; } = null!;

        /// <summary>Only for a three-bend corner.</summary>
        public FaceSignature? Face3 { get; set; }

        /// <summary><see cref="CornerReliefTypes"/>.</summary>
        public int ReliefType { get; set; }

        public int SuitCaseType { get; set; }

        /// <summary>Metres, or a ratio to the thickness (<see cref="RatioToThickness"/>).</summary>
        public double SlotLength { get; set; }

        /// <summary>Metres, or a ratio to the thickness.</summary>
        public double SlotWidth { get; set; }

        /// <summary>Metres.</summary>
        public double CornerFilletDiameter { get; set; }

        public bool AddFilletedCorners { get; set; }
        public bool CenterOnBendLines { get; set; }
        public bool RatioToThickness { get; set; }
        public bool TangentToBend { get; set; }

        /// <summary>Its faces in their order (two or three).</summary>
        public List<FaceSignature> Faces()
        {
            var faces = new List<FaceSignature> { Face1, Face2 };
            if (Face3 != null) faces.Add(Face3);
            return faces;
        }

        /// <summary>Faces mirrored in their roles; values kept.</summary>
        public CornerReliefItem Mirror(Reflection r) => new CornerReliefItem
        {
            Face1 = Face1.Mirror(r),
            Face2 = Face2.Mirror(r),
            Face3 = Face3?.Mirror(r),
            ReliefType = ReliefType,
            SuitCaseType = SuitCaseType,
            SlotLength = SlotLength,
            SlotWidth = SlotWidth,
            CornerFilletDiameter = CornerFilletDiameter,
            AddFilletedCorners = AddFilletedCorners,
            CenterOnBendLines = CenterOnBendLines,
            RatioToThickness = RatioToThickness,
            TangentToBend = TangentToBend,
        };
    }

    /// <summary>
    /// Corner relief (ICornerReliefFeatureData, SOLIDWORKS 2022 and later; its type name is not
    /// confirmed, B44 – it is found by its data). The body it acts on, by one of its faces, the bend
    /// type of its corners and each corner.
    /// </summary>
    public sealed class CornerReliefRecord : FeatureRecord
    {
        public CornerReliefRecord(string name, string typeName) : base(name, typeName)
        {
        }

        /// <summary><see cref="CornerReliefBendTypes"/>.</summary>
        public int CornerType { get; set; }

        /// <summary>A face of the body it acts on (state before it), to find the mirrored body; null if the body scope did not read.</summary>
        public FaceSignature? BodyFace { get; set; }

        public List<CornerReliefItem> Corners { get; } = new List<CornerReliefItem>();
    }

    public enum FoldKind
    {
        Unfold,
        Fold,
    }

    /// <summary>
    /// Unfold (UnFold) or fold (Fold), IFoldsFeatureData: the fixed face and the bends, nothing with a
    /// direction (A3 §6). A fold names the bends its unfold flattened (official example): the copy finds
    /// them by name first (<see cref="BendRef.Name"/>), then by their mirrored faces.
    /// </summary>
    public sealed class FoldRecord : FeatureRecord
    {
        public FoldRecord(string name, string typeName) : base(name, typeName)
        {
            Kind = typeName == "Fold" ? FoldKind.Fold : FoldKind.Unfold;
        }

        public FoldKind Kind { get; }

        /// <summary>The face that stays, in the state before the feature; null only in a read-back.</summary>
        public FaceSignature? FixedFace { get; set; }

        public List<BendRef> Bends { get; } = new List<BendRef>();

        /// <summary>A fold: the last unfold before it (the official selection names it); null if there is none.</summary>
        public string? PrecedingUnfold { get; set; }
    }
}
