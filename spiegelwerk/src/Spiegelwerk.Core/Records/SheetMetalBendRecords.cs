using System.Collections.Generic;
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

        /// <summary>The features that made its body before it (no sketches, reference geometry, folders, SheetMetal).</summary>
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
