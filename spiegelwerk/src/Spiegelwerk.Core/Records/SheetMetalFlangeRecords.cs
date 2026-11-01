using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Sketch;

namespace Spiegelwerk.Core.Records
{
    // Flanges on edges (A2, blech-plan S2): hem and miter flange. A reflection keeps every
    // length, angle and radius and maps the edges, faces and profiles onto their mirror images,
    // so the scalars go over unchanged; only the edge fingerprints and the profile sketches are
    // mirrored. The bend sub-features are read for notes only – their own values cannot be
    // written (B23).

    /// <summary>Same numbers as swHemTypes_e.</summary>
    public static class HemTypes
    {
        public const int Open = 0, Closed = 1, TearDrop = 2, Rolled = 3, Double = 4;
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
