using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// How a new body's own values – the override groups of its SheetMetal – reach the copy (B15:
    /// the override flags exist only in a multibody part).
    /// </summary>
    public enum BodyOverrideMode
    {
        /// <summary>Nothing to override, or a tab (it shares the SheetMetal of its body).</summary>
        None,

        /// <summary>The copy already has a sheet-metal body: the overrides go on the new SheetMetal at once.</summary>
        Apply,

        /// <summary>The first body of a copy that stays single-body: its own values are the part's values.</summary>
        PartValues,

        /// <summary>The first body of a copy that gets more: created with the defaults, its overrides follow with the second sheet-metal body.</summary>
        Deferred,
    }

    /// <summary>What the guard of the shared sheet-metal values compares a step with (blech-plan §1.4, S1b).</summary>
    public enum GuardExpectation
    {
        /// <summary>Nothing to protect yet: a new part before its first base flange, which makes the defaults.</summary>
        None,

        /// <summary>The state before the step: the defaults, every SheetMetal, the earlier bodies.</summary>
        Snapshot,

        /// <summary>The same part without a readable snapshot: the step is not built – the original's values would be unguarded.</summary>
        NotBuilt,
    }

    /// <summary>A variant of a base flange for the step check.</summary>
    public enum BaseFlangeToggle
    {
        /// <summary>ReverseDirection: the plate side of a closed profile, the depth of an open one.</summary>
        ReverseDirection,

        /// <summary>ReverseThickness: the material side of an open contour (no effect on a closed profile).</summary>
        ReverseThickness,
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

        /// <summary>
        /// How the body's own values reach the copy (<see cref="BodyOverrideMode"/>). The first body
        /// is created while the copy is single-body, where SetOverrideDefaultParameter2 does not
        /// apply: if the run builds no other sheet-metal body, its own values become the part's;
        /// otherwise they wait for the second body.
        /// </summary>
        /// <param name="first">The target has neither defaults nor a SheetMetal yet.</param>
        /// <param name="tab">The flange joins an existing body.</param>
        /// <param name="overrides">Number of override groups the original body set (<see cref="BodyOverrides"/>).</param>
        /// <param name="staysSingleBody">The run builds no other base flange with a body of its own.</param>
        public static BodyOverrideMode BodyOverrideModeOf(bool first, bool tab, int overrides, bool staysSingleBody) =>
            tab || overrides == 0 ? BodyOverrideMode.None
            : !first ? BodyOverrideMode.Apply
            : staysSingleBody ? BodyOverrideMode.PartValues
            : BodyOverrideMode.Deferred;

        /// <summary>
        /// Whether a flange's read-back expects the thickness and radius its body has now – the values
        /// it was created with – instead of the original's (B15): while the first body still waits for
        /// its own bend parameters (group 0), it and every tab on it have the defaults. A flange that
        /// takes them from its gauge table or carries its own over the defaults
        /// (OverrideDefaultSheetMetalParameters) keeps the original's.
        /// </summary>
        /// <param name="waitingGroups">The override groups its body still waits for; null if it waits for none.</param>
        /// <param name="flangeOverridesDefaults">The original flange's OverrideDefaultSheetMetalParameters.</param>
        /// <param name="gaugeTable">The copy's flange was given the gauge table.</param>
        public static bool ReadsBackCurrentBendValues(IReadOnlyList<int>? waitingGroups, bool? flangeOverridesDefaults, bool gaugeTable) =>
            waitingGroups != null && waitingGroups.Contains(0) && flangeOverridesDefaults != true && !gaugeTable;

        /// <summary>
        /// What a body actually has: the groups it overrides (<see cref="BodyOverrides"/>) from its
        /// own SheetMetal, everything else from the part's defaults (before 2013: all its own). In a
        /// copy that stays single-body these are the part's values.
        /// </summary>
        public static SheetMetalParameters OwnValues(SheetMetalParameters? defaults, SheetMetalParameters body)
        {
            IReadOnlyList<int> groups = BodyOverrides(body);
            SheetMetalParameters basis = defaults ?? body;
            return new SheetMetalParameters
            {
                Thickness = groups.Contains(0) ? body.Thickness : basis.Thickness,
                BendRadius = groups.Contains(0) ? body.BendRadius : basis.BendRadius,
                BendAllowance = groups.Contains(1) ? body.BendAllowance : basis.BendAllowance,
                Relief = groups.Contains(2) ? body.Relief : basis.Relief,
                UseGaugeTable = basis.UseGaugeTable,
                GaugeTableFile = basis.GaugeTableFile,
                UseMaterialParameters = basis.UseMaterialParameters,
            };
        }

        /// <summary>Base flanges of the run that make a body of their own: at most one means the copy stays single-body.</summary>
        public static bool StaysSingleBody(IEnumerable<FeatureRecord> records)
        {
            int bodies = 0;
            foreach (FeatureRecord r in records)
                if (r is BaseFlangeRecord b && !b.MergesIntoExisting)
                    bodies++;
            return bodies <= 1;
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

        // ---- same part (blech-plan S1b, A1 §11)

        /// <summary>
        /// What the guard compares a step that may write the shared values with. The state before
        /// the step whenever there is one – in the same part from the first copied flange on (the
        /// original's defaults and SheetMetals are there), in a new part from the second (the first
        /// makes the defaults; S1a: there too the snapshot, not the original's defaults, so a first
        /// body that did not take them fully does not stop every later step). In the same part a
        /// step without a readable snapshot is not built: the original's values would be unguarded.
        /// </summary>
        /// <param name="samePart">The copy is built into the original part.</param>
        /// <param name="snapshotTaken">The guard could read the defaults, the SheetMetals and the bodies before the step.</param>
        public static GuardExpectation GuardExpectationOf(bool samePart, bool snapshotTaken) =>
            snapshotTaken ? GuardExpectation.Snapshot
            : samePart ? GuardExpectation.NotBuilt
            : GuardExpectation.None;

        /// <summary>
        /// Whether the guard compares the earlier bodies even if the shared values are unchanged.
        /// In the same part they include the original: a step must leave it as it was whatever the
        /// cause (a flag of the shared values the guard does not read, a merge with it). In a new
        /// part only after a change of the values – nothing else of one step reaches another body.
        /// </summary>
        public static bool GuardComparesBodies(bool samePart, bool valuesChanged) => samePart || valuesChanged;

        /// <summary>The step's shared values had changed and were written back successfully.</summary>
        public static string GuardRestored(IEnumerable<string> changed) =>
            $"Die Blech-Standardwerte hatten sich mit diesem Schritt geändert und wurden wiederhergestellt ({string.Join("; ", changed)}).";

        /// <summary>
        /// Why the guard stops the run, or null: the shared values could not be restored
        /// (<paramref name="still"/>), or earlier bodies are not as they were (<paramref name="moved"/>).
        /// In the same part the original may be among them: undo is the way back.
        /// </summary>
        /// <param name="changed">What differed after the step, before writing back.</param>
        public static string? GuardAbort(bool samePart, IReadOnlyCollection<string> changed, IReadOnlyCollection<string> still, IReadOnlyCollection<string> moved)
        {
            if (still.Count == 0 && moved.Count == 0) return null;
            string bodies = moved.Count > 0 ? $" Betroffen: {string.Join(", ", moved.Select(n => $"„{n}“"))}." : "";
            string tail = samePart
                ? " Das Original kann betroffen sein – bitte mit Rückgängig prüfen."
                : " Die bisher gespiegelten Blechkörper können sich geändert haben – bitte prüfen.";
            if (still.Count > 0)
                return $"Achtung: Die Blech-Standardwerte des Teils wurden verändert und ließen sich nicht wiederherstellen ({string.Join("; ", still)}).{bodies}{tail}";
            if (changed.Count == 0)
                return $"Achtung: Mit diesem Schritt haben sich frühere Körper verändert, obwohl die Blech-Standardwerte gleich geblieben sind.{bodies}{tail}";
            return $"Achtung: Die Blech-Standardwerte des Teils hatten sich mit diesem Schritt geändert ({string.Join("; ", changed)}) und wurden wiederhergestellt, " +
                   $"frühere Körper sind danach aber nicht wie vorher.{bodies}{tail}";
        }

        /// <summary>The same part, a step that may write the shared values, no snapshot: not built.</summary>
        public const string GuardNotReadable = "Im selben Teil nicht angelegt: Die Blech-Standardwerte und Blech-Features des Teils ließen sich vorher nicht lesen; " +
                                               "ohne sie lässt sich nicht prüfen, ob das Original unverändert bleibt.";

        /// <summary>
        /// The override groups to write back on a SheetMetal the guard restores. Those the snapshot
        /// had set; if its flags were not readable then (single-body or pre-2013 part) but are now –
        /// the same part became multibody with the copy –, every group whose values differ: in a
        /// single-body part a body's values are its own, in a multibody part only the overridden ones.
        /// </summary>
        /// <param name="before">The SheetMetal before the step.</param>
        /// <param name="now">The same SheetMetal now; null if not readable.</param>
        public static IReadOnlyList<int> RestoreGroups(SheetMetalParameters before, SheetMetalParameters? now)
        {
            if (before.OverrideReadCode == 0) return BodyOverrides(before);
            if (now == null || now.OverrideReadCode != 0) return new List<int>();
            return DifferingGroups(before, now);
        }

        /// <summary>
        /// What the original body has: with readable override flags its overridden groups and the
        /// defaults for the rest (<see cref="OwnValues"/>); without them (a single-body or pre-2013
        /// part) its SheetMetal's values – there they are the body's own.
        /// </summary>
        public static SheetMetalParameters BodyValues(SheetMetalParameters? defaults, SheetMetalParameters body) =>
            body.OverrideReadCode == 0 ? OwnValues(defaults, body) : body;

        /// <summary>
        /// The override groups of the copy's SheetMetal in the same part, where the part's defaults
        /// are the original's and never written: those the original body set
        /// (<see cref="BodyOverrides"/>) and every other group in which the defaults the copy is
        /// created with differ from what the original body has (<see cref="BodyValues"/>) – a
        /// single-body original (no readable flags) whose values are not the part's defaults.
        /// </summary>
        /// <param name="body">The original body's SheetMetal.</param>
        /// <param name="defaultsNow">The part's defaults when the copy is built (the target's TemplateSheetMetal); null if none.</param>
        /// <param name="originalDefaults">The defaults captured with the original; null before 2013.</param>
        public static IReadOnlyList<int> SamePartOverrides(SheetMetalParameters? body, SheetMetalParameters? defaultsNow, SheetMetalParameters? originalDefaults)
        {
            var groups = new List<int>(BodyOverrides(body));
            if (body == null || defaultsNow == null) return groups;
            foreach (int g in DifferingGroups(BodyValues(originalDefaults, body), defaultsNow))
                if (!groups.Contains(g))
                    groups.Add(g);
            groups.Sort();
            return groups;
        }

        /// <summary>The override groups whose values differ: 0 thickness or radius, 1 allowance, 2 relief.</summary>
        private static List<int> DifferingGroups(SheetMetalParameters a, SheetMetalParameters b)
        {
            var groups = new List<int>();
            if (SheetMetalCompare.Scalars(new[]
                {
                    ("Blechdicke", a.Thickness, b.Thickness, ScalarKind.Length),
                    ("Biegeradius", a.BendRadius, b.BendRadius, ScalarKind.Length),
                }).Count > 0)
                groups.Add(0);
            if (SheetMetalCompare.Allowance(a.BendAllowance, b.BendAllowance).Differences.Count > 0) groups.Add(1);
            if (SheetMetalCompare.Relief(a.Relief, b.Relief).Differences.Count > 0) groups.Add(2);
            return groups;
        }

        /// <summary>
        /// The variants a base flange offers the step check. The plate side of a closed profile and
        /// the depth of an open one (not with mid-plane); the material side of an open contour
        /// (not with symmetric thickness, also in the same part – under the guard). The thickness
        /// direction of a closed profile has no effect and would write the shared values: never in
        /// the same part (A1 P1), elsewhere only without symmetric thickness.
        /// </summary>
        public static IReadOnlyList<(BaseFlangeToggle toggle, string label)> BaseFlangeToggles(bool closedProfile, bool symmetricThickness, bool midPlane, bool samePart)
        {
            var result = new List<(BaseFlangeToggle, string)>();
            if (closedProfile)
            {
                result.Add((BaseFlangeToggle.ReverseDirection, "Blechdicke auf die andere Seite"));
                if (!samePart && !symmetricThickness) result.Add((BaseFlangeToggle.ReverseThickness, "Dickenrichtung umgekehrt"));
                return result;
            }
            if (!midPlane) result.Add((BaseFlangeToggle.ReverseDirection, "Richtung umgekehrt"));
            if (!symmetricThickness) result.Add((BaseFlangeToggle.ReverseThickness, "Materialseite der Kontur gewechselt"));
            return result;
        }

        /// <summary>
        /// A tab in separate-body mode joins the job's own bodies only (scope, path C first); with
        /// none yet it would join the original – not built. Null: it may be built.
        /// </summary>
        public static string? TabRefusal(bool tab, bool separateBody, int ownBodies) =>
            tab && separateBody && ownBodies == 0
                ? "Es gibt noch keinen gespiegelten Blechkörper, an den die Lasche anschließen könnte; das Original bleibt unberührt."
                : null;

        /// <summary>
        /// „Original löschen“ after a run with sheet metal (A1 P8, K27): the part's shared values
        /// hang on its first sheet-metal body, the copy uses them – deleting the original could
        /// change it, and nothing measures that yet (blech-plan S10). Null: no sheet metal in the
        /// run. A record that failed keeps the original anyway; this reason is said first.
        /// </summary>
        public static string? DeleteRefusal(IEnumerable<FeatureRecord> records) =>
            records.Any(r => SheetMetalTypes.IsSheetMetal(r.TypeName)) ? DeleteRefused : null;

        /// <summary>The report line of <see cref="DeleteRefusal"/>.</summary>
        public const string DeleteRefused = "Original nicht gelöscht: Die Blech-Standardwerte des Teils hängen am ersten Blechkörper; die Kopie könnte sich mit dem Löschen ändern.";

        /// <summary>Same part with sheet metal: the copy is not wholly independent of the original.</summary>
        public const string SharedDefaultsNote = "Blech im selben Teil: Original und Kopie teilen die Blech-Standardwerte des Teils (Ordner „Blech“). " +
                                                 "Eine Änderung dort wirkt auf beide, soweit ein Körper sie nicht über sein Blech-Feature überschreibt.";

        /// <summary>Same part: equations on the shared values drive the copy too.</summary>
        public static string SharedDefaultsEquations(IReadOnlyCollection<string> driven) =>
            $"Gleichungen auf den Blech-Standardwerten ({string.Join(", ", driven.Select(d => $"„{d}“"))}) wirken im selben Teil auch auf die gespiegelten Blechkörper, " +
            "soweit deren Blech-Features sie nicht überschreiben.";

        /// <summary>The first body of a copy that stays single-body: its own values became the part's.</summary>
        public const string OwnValuesAsPartValues = "Eigene Blechparameter des Körpers als Werte des Teils übernommen: Die Kopie hat nur diesen Blechkörper.";

        /// <summary>The first body's overrides wait for the second sheet-metal body.</summary>
        public const string OwnValuesDeferred = "Eigene Blechparameter des Körpers folgen mit dem zweiten Blechkörper (im Einkörperteil nicht anwendbar); die Prüfung dieses Schritts sieht den Körper noch mit den Standardwerten.";

        /// <summary>A step checked while the first body's own values still wait: said only if its check finds a deviation.</summary>
        public const string OwnValuesPendingHint = "Der erste Blechkörper hatte bei dieser Prüfung noch die Standardwerte; seine eigenen Blechparameter werden erst mit dem zweiten Blechkörper (oder am Ende) gesetzt – die Abweichung kann daher rühren.";

        /// <summary>The first body's overrides, set after the step that made the second sheet-metal body.</summary>
        public static string OwnValuesSet(string sheetMetal, string flange) =>
            $"Eigene Blechparameter von „{sheetMetal}“ (Körper von „{flange}“) gesetzt, nun da das Teil einen zweiten Blechkörper hat.";

        /// <summary>The copy got no second sheet-metal body: the first body's own values became the part's at the end.</summary>
        public const string OwnValuesAtEnd = "Kein zweiter Blechkörper entstanden: Eigene Blechparameter des Körpers am Ende als Werte des Teils gesetzt.";

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
