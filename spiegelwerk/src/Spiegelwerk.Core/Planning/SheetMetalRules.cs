using System.Collections.Generic;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>The ways a base flange can be created (blech-plan §1, A1 §8).</summary>
    public enum BaseFlangePath
    {
        /// <summary>A: CreateDefinition(swFmBaseFlange) → IBaseFlangeFeatureData → CreateFeature (Initialize late-bound only if needed).</summary>
        DataObject,

        /// <summary>B: InsertSheetMetalBaseFlange (16 parameters), no merge, no scope.</summary>
        V1,

        /// <summary>C: InsertSheetMetalBaseFlange2 (19 parameters) with merge and feature scope.</summary>
        V2,
    }

    /// <summary>What a base flange is created with: thickness, radius, the allowance object and the relief.</summary>
    public sealed class FlangeCreation
    {
        public FlangeCreation(double thickness, double bendRadius, BendAllowanceData? allowance, bool useDefaultRelief, ReliefData relief)
        {
            Thickness = thickness;
            BendRadius = bendRadius;
            Allowance = allowance;
            UseDefaultRelief = useDefaultRelief;
            Relief = relief;
        }

        /// <summary>Metres.</summary>
        public double Thickness { get; }

        /// <summary>Metres.</summary>
        public double BendRadius { get; }

        /// <summary>The allowance object passed at creation (PCBA); null: the default.</summary>
        public BendAllowanceData? Allowance { get; }

        public bool UseDefaultRelief { get; }
        public ReliefData Relief { get; }
    }

    /// <summary>
    /// Mirror rules of a base flange (A1 §5). A mirror keeps every length and angle, so
    /// thickness, radius, allowance, tables and reliefs go over unchanged; what changes are the
    /// sides: the plate side along the sketch normal (closed profile) and the depth of an open
    /// profile follow σ like an extrusion, the material side of an open contour lies in the
    /// sketch plane and follows the opposite rule.
    /// </summary>
    public static class SheetMetalRules
    {
        /// <summary>
        /// A side in the sketch plane, left or right of a contour (n × t): the copy keeps the start
        /// → end of every segment (t′ = R(t)) and has n′ = σ·R(n), so n′ × t′ = −σ·R(n × t). With σ =
        /// +1 left and right swap, with σ = −1 they stay. Assumes SOLIDWORKS measures the side
        /// against the sketch normal (model a, live test B4).
        /// </summary>
        public static bool InPlaneSideFlag(bool source, int sigma) => sigma > 0 ? !source : source;

        /// <summary>
        /// ReverseDirection and ReverseThickness of the copy. ReverseDirection – plate side of a
        /// closed profile, depth of an open one – follows σ as an extrusion does. ReverseThickness is
        /// the material side of an open contour (<see cref="InPlaneSideFlag"/>); on a closed profile
        /// it has no effect and goes over unchanged, as it does with symmetric thickness (no side).
        /// </summary>
        public static (bool reverseDirection, bool reverseThickness) BaseFlangeFlags(bool closedProfile, bool reverseDirection, bool reverseThickness,
            int sigma, bool symmetricThickness = false) =>
            (DirectionRules.ReverseFlag(reverseDirection, sigma),
             closedProfile || symmetricThickness ? reverseThickness : InPlaneSideFlag(reverseThickness, sigma));

        /// <summary>
        /// swFlangeOffsetTypes_e → the end condition (swEndConditions_e numbers). Null for any value
        /// it does not know – also 6, „bis Kante und zusammenführen“ (newer versions): such a flange
        /// is not rebuilt.
        /// </summary>
        public static EndCondition? FromFlangeOffset(int offsetType) => offsetType switch
        {
            1 => EndCondition.Blind,
            2 => EndCondition.UpToVertex,
            3 => EndCondition.UpToSurface,
            4 => EndCondition.OffsetFromSurface,
            5 => EndCondition.MidPlane,
            _ => (EndCondition?)null,
        };

        /// <summary>The end condition → swFlangeOffsetTypes_e; null for one a base flange does not have.</summary>
        public static int? ToFlangeOffset(EndCondition end) => end switch
        {
            EndCondition.Blind => 1,
            EndCondition.UpToVertex => 2,
            EndCondition.UpToSurface => 3,
            EndCondition.OffsetFromSurface => 4,
            EndCondition.MidPlane => 5,
            _ => (int?)null,
        };

        /// <summary>
        /// What a base flange of the copy is created with (A1 §8.3). The first body of a part makes
        /// the part's defaults: the original's defaults (its body's values before 2013) and their
        /// allowance, unless the flange has one of its own. Every later one is created with what the
        /// target already has – a tab with the body it joins, any other with the target's defaults –
        /// so creating it does not rewrite the defaults the earlier bodies use; body overrides
        /// follow on its own SheetMetal. The relief is the default unless the flange has its own.
        /// </summary>
        /// <param name="first">The target has neither defaults nor a SheetMetal yet.</param>
        /// <param name="tab">The flange joins an existing body.</param>
        /// <param name="defaults">The original's defaults (TemplateSheetMetal); null before 2013.</param>
        /// <param name="body">The original body's SheetMetal.</param>
        /// <param name="template">The target's defaults, read before the flange is created.</param>
        /// <param name="targetBody">The SheetMetal of the target body a tab joins.</param>
        /// <param name="thickness">The flange's own thickness, if nothing else is known.</param>
        /// <param name="radius">The flange's own radius, if nothing else is known.</param>
        public static FlangeCreation CreationValues(bool first, bool tab, SheetMetalParameters? defaults, SheetMetalParameters? body,
            SheetMetalParameters? template, SheetMetalParameters? targetBody, BendAllowanceData? ownAllowance, ReliefData? ownRelief,
            double thickness, double radius)
        {
            SheetMetalParameters? source = first ? defaults ?? body
                : tab ? targetBody ?? template ?? defaults ?? body
                : template ?? defaults ?? body;
            BendAllowanceData? allowance = first ? ownAllowance ?? source?.BendAllowance : ownAllowance;
            return new FlangeCreation(source?.Thickness ?? thickness, source?.BendRadius ?? radius, allowance,
                ownRelief == null, ownRelief ?? source?.Relief ?? new ReliefData());
        }

        /// <summary>Solid bodies a base flange adds: one, or none for a tab that joins its body.</summary>
        public static int ExpectedBodyDelta(bool tab) => tab ? 0 : 1;

        /// <summary>
        /// The order in which the creation paths are tried (A1 P5): the data object first – the API
        /// help's way, live-tested by third parties –, then v1, then v2. Only a tab in separate-body
        /// mode needs v2 first: it alone can merge with a scope limited to the job's own bodies.
        /// </summary>
        public static IReadOnlyList<BaseFlangePath> CreationPathOrder(bool tabInSeparateBodyMode) => tabInSeparateBodyMode
            ? new[] { BaseFlangePath.V2, BaseFlangePath.DataObject, BaseFlangePath.V1 }
            : new[] { BaseFlangePath.DataObject, BaseFlangePath.V1, BaseFlangePath.V2 };

        /// <summary>For the report: „Datenobjekt“, „InsertSheetMetalBaseFlange“, „InsertSheetMetalBaseFlange2“.</summary>
        public static string PathName(BaseFlangePath path) => path switch
        {
            BaseFlangePath.DataObject => "Datenobjekt",
            BaseFlangePath.V1 => "InsertSheetMetalBaseFlange",
            _ => "InsertSheetMetalBaseFlange2",
        };

        /// <summary>
        /// The override flags (swSheetMetalOverrideDefaultParameters_e 0/1/2) the copy's SheetMetal
        /// sets: those the original body set – only where they were readable (return code 0).
        /// </summary>
        public static IReadOnlyList<int> BodyOverrides(SheetMetalParameters? body)
        {
            var result = new List<int>();
            if (body == null || body.OverrideReadCode != 0) return result;
            if (body.OverrideBendParameters) result.Add(0);
            if (body.OverrideBendAllowance) result.Add(1);
            if (body.OverrideAutoRelief) result.Add(2);
            return result;
        }

        /// <summary>The copy gets fixed values where the original takes them from its material.</summary>
        public const string MaterialNote = "Blechparameter des Werkstoffs als feste Werte übernommen.";

        /// <summary>Grain direction: no API member reads or sets it.</summary>
        public const string GrainNote = "Eine Faserrichtung der Abwicklung ist über die API nicht lesbar und wird nicht übernommen.";

        /// <summary>The configuration SOLIDWORKS derives for a flat-pattern drawing view is not made.</summary>
        public const string FlatConfigurationNote = "Eine abgeleitete Konfiguration „…SM-FLAT-PATTERN“ wird nicht angelegt; SOLIDWORKS erzeugt sie bei Bedarf.";

        /// <summary>The flat check passed.</summary>
        public const string FlatChecked = "Abwicklung geprüft";

        /// <summary>The flat check failed: a warning, no variants – no flag can sensibly be flipped.</summary>
        public const string FlatDeviates = "Abwicklung weicht ab – K-Faktor/Biegezugabe/feste Fläche prüfen.";

        /// <summary>„Jeden Schritt prüfen“ on, but the original's flat state could not be measured.</summary>
        public const string FlatNotMeasured = "Abwicklung nicht geprüft: Der abgewickelte Zustand des Originals ließ sich nicht messen.";
    }
}
