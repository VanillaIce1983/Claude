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

        /// <summary>
        /// ISheetMetalFeatureData.FixedReference of legacy or converted parts: only ever a face
        /// or an edge – a straight edge stays an edge, it is no axis.
        /// </summary>
        public GeometryRef? FixedReference { get; set; }
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
}
