using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// Tab and slot (A4 (2) §6, P12, blech-plan S8). A reflection keeps lengths, spacing, height, clearance
    /// and edge treatment, and the reversed tab offset (measured from the slot face, whose outward normal stays
    /// outward). It maps edges, faces and reference points. D1 counts from the start of the tab edge (or of the
    /// start reference): without references, an edge the copy runs the other way puts D1 at the other end, so
    /// D1 and D2 are swapped there (<see cref="Offsets"/>). The slot feature is found by its name
    /// (<see cref="IsSlotName"/>) or as a child of the tab (<see cref="SheetMetalTree"/>).
    /// </summary>
    public static class TabSlotRules
    {
        /// <summary>swFeatureNameID_e.swFmTabAndSlot: the data object of CreateDefinition.</summary>
        public const int TabAndSlotId = 88;

        /// <summary>
        /// D1 and D2 for the copy's group: unchanged with a reference point (D1 stays at the mirrored start
        /// reference) or without offsets; otherwise swapped if the copy's edge <paramref name="actual"/> runs
        /// the other way than the mirrored original <paramref name="wanted"/> (<see cref="EdgeLocation.Map"/>).
        /// Null if that cannot be told (a closed edge, ends that do not match).
        /// </summary>
        public static (double d1, double d2, bool swapped)? Offsets(TabSlotGroup g, EdgeSignature wanted, EdgeSignature actual)
        {
            if (!g.Offset || g.StartReference != null || g.EndReference != null) return (g.D1OffsetFromStart, g.D2OffsetFromEnd, false);
            double? start = EdgeLocation.Map(wanted, actual, 0, 1);
            if (start == null) return null;
            return start.Value == 0 ? (g.D1OffsetFromStart, g.D2OffsetFromEnd, false) : (g.D2OffsetFromEnd, g.D1OffsetFromStart, true);
        }

        /// <summary>D1/D2 may come out the other way round: only with offsets and without references.</summary>
        public static bool OffsetsMaySwap(TabSlotGroup g) => g.Offset && g.StartReference == null && g.EndReference == null;

        /// <summary>The reversed tab offset counts only „offset from surface“.</summary>
        public static bool HasTabOffset(TabSlotGroup g) => g.TabHeightType == TabSlotHeightTypes.OffsetFromSurface;

        /// <summary>
        /// The slot feature of a tab, by name (help example: „Tab and Slot-Tab1“): the same text before the last
        /// hyphen, the same number at the end, another word between („…-Slot1“; in German „…-Schlitz1“). Language-
        /// independent: the tab's word is not looked at, only that the slot's is another.
        /// </summary>
        public static bool IsSlotName(string tab, string candidate)
        {
            if (!Split(tab, out string tabPrefix, out string tabWord, out string tabNumber)) return false;
            if (!Split(candidate, out string prefix, out string word, out string number)) return false;
            return prefix == tabPrefix && number == tabNumber && word.Length > 0 && !string.Equals(word, tabWord, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Split(string name, out string prefix, out string word, out string number)
        {
            prefix = word = number = "";
            int dash = name.LastIndexOf('-');
            if (dash <= 0 || dash == name.Length - 1) return false;
            prefix = name.Substring(0, dash);
            string rest = name.Substring(dash + 1);
            int digits = rest.Length;
            while (digits > 0 && char.IsDigit(rest[digits - 1])) digits--;
            if (digits == rest.Length) return false;
            word = rest.Substring(0, digits);
            number = rest.Substring(digits);
            return true;
        }

        /// <summary>
        /// A group as read back against the one the copy should have (offsets already as <see cref="Offsets"/>
        /// says): only the values its settings use.
        /// </summary>
        public static List<string> Differences(TabSlotGroup want, TabSlotGroup got, IFormatProvider? culture = null)
        {
            var values = new List<(string, double, double, ScalarKind)>
            {
                ("Versatz", Flag(want.Offset), Flag(got.Offset), ScalarKind.Flag),
                ("Abstandsart", want.SpacingType, got.SpacingType, ScalarKind.Choice),
                ("Laschenlänge", want.TabLength, got.TabLength, ScalarKind.Length),
                ("Laschendicke", want.TabThickness, got.TabThickness, ScalarKind.Length),
                ("Höhenart", want.TabHeightType, got.TabHeightType, ScalarKind.Choice),
                ("Kantenbehandlung", want.TabEdgesType, got.TabEdgesType, ScalarKind.Choice),
                ("Schlitzspiel", want.SlotClearance, got.SlotClearance, ScalarKind.Length),
            };
            if (want.Offset)
            {
                values.Add(("D1", want.D1OffsetFromStart, got.D1OffsetFromStart, ScalarKind.Length));
                values.Add(("D2", want.D2OffsetFromEnd, got.D2OffsetFromEnd, ScalarKind.Length));
            }
            if (want.SpacingType == TabSlotSpacingTypes.EqualSpacing) values.Add(("Anzahl", want.SpacingNumberOfInstances, got.SpacingNumberOfInstances, ScalarKind.Choice));
            else values.Add(("Abstand", want.Spacing, got.Spacing, ScalarKind.Length));
            if (want.TabHeightType == TabSlotHeightTypes.Blind) values.Add(("Laschenhöhe", want.TabHeightValue, got.TabHeightValue, ScalarKind.Length));
            if (HasTabOffset(want))
            {
                values.Add(("Laschenversatz", want.TabOffsetValue, got.TabOffsetValue, ScalarKind.Length));
                values.Add(("Laschenversatz umgekehrt", Flag(want.TabReverseDirection), Flag(got.TabReverseDirection), ScalarKind.Flag));
            }
            if (want.TabEdgesType == TabEdgesTypes.FilletEdge)
                values.Add(("Verrundung der Laschen", want.TabFilletEdgeTreatmentValue, got.TabFilletEdgeTreatmentValue, ScalarKind.Length));
            if (want.TabEdgesType == TabEdgesTypes.ChamferEdge)
                values.Add(("Fase der Laschen", want.TabChamferEdgeTreatmentValue, got.TabChamferEdgeTreatmentValue, ScalarKind.Length));
            return SheetMetalCompare.Scalars(values, culture);
        }

        /// <summary>Every group read back against the wanted ones, in their order, as notes („Gruppe 2: …“ only with several groups).</summary>
        public static List<string> Differences(IReadOnlyList<TabSlotGroup> want, IReadOnlyList<TabSlotGroup> got, IFormatProvider? culture = null)
        {
            var result = new List<string>();
            if (want.Count != got.Count)
            {
                result.Add($"Lasche und Schlitz: {Count(got.Count)} gelesen statt {Count(want.Count)}.");
                return result;
            }
            for (int i = 0; i < want.Count; i++)
            {
                string label = want.Count == 1 ? "Lasche und Schlitz" : $"Lasche und Schlitz, Gruppe {i + 1}";
                result.AddRange(Differences(want[i], got[i], culture).Select(d => $"{label}: {d}"));
            }
            return result;
        }

        private static double Flag(bool b) => b ? 1 : 0;

        private static string Count(int n) => n == 1 ? "1 Gruppe" : $"{n} Gruppen";

        public static string Describe(TabAndSlotRecord r)
        {
            string groups = Count(r.Groups.Count);
            int n = r.Groups.Count == 0 ? 0 : r.Groups[0].SpacingType == TabSlotSpacingTypes.EqualSpacing ? r.Groups[0].SpacingNumberOfInstances : 0;
            return n > 0 && r.Groups.Count == 1 ? $"Lasche und Schlitz, {n} Laschen" : $"Lasche und Schlitz, {groups}";
        }

        // ---- texts

        /// <summary>Made in the context of an assembly (A4 P12): its references point into another file.</summary>
        public const string InContext = "Lasche und Schlitz im Kontext einer Baugruppe angelegt (externe Bezüge) – wird nicht nachgebaut.";

        /// <summary>The slot's own options have no API in 2019 (only the clearance): the copy has the defaults, said where nothing checks it.</summary>
        public const string SlotOptionsUnchecked =
            "Schlitz-Optionen (Ecken, Längen- und Breitenversatz) sind über die API nicht lesbar: Die Kopie hat die Standardwerte (nicht geprüft).";

        /// <summary>Said only if the check finds a deviation.</summary>
        public const string SlotOptionsHint =
            "Die Schlitz-Optionen (Ecken, Längen- und Breitenversatz) sind über die API nicht lesbar; die Kopie hat die Standardwerte – die Abweichung kann daher rühren.";

        /// <summary>Added to the text of a passing check.</summary>
        public const string SlotOptionsChecked = "Schlitz-Optionen: Standard (geprüft)";

        /// <summary>Whether the check can confirm the slot options: „Jeden Schritt prüfen“, a check that decides, a reference of its own.</summary>
        public static bool SlotOptionsCheckable(bool verifySteps, bool checkDecides, bool hasReference) => verifySteps && checkDecides && hasReference;

        /// <summary>The note on the slot options: none where a check confirms or rejects them (like the hem's relief, A2 P9).</summary>
        public static string? SlotOptionsNote(bool checkable) => checkable ? null : SlotOptionsUnchecked;

        /// <summary>The variant „D1 and D2 the other way round“ offered to the check.</summary>
        public const string OffsetsSwapped = "D1/D2 getauscht";

        /// <summary>The variant „tab offset reversed“ (offset from surface only).</summary>
        public const string TabOffsetReversed = "Laschenversatz umgekehrt";

        /// <summary>A closed tab edge (or one whose ends do not match): which end D1 counts from is not determinable.</summary>
        public static string OffsetsUndetermined(int group, int groups) =>
            $"{(groups == 1 ? "Lasche und Schlitz" : $"Lasche und Schlitz, Gruppe {group}")}: An welchem Ende der Laschenkante D1 zählt, ist nicht bestimmbar; D1 und D2 wie im Original.";

        /// <summary>D1 and D2 swapped because the copy's edge runs the other way.</summary>
        public static string OffsetsSwappedInfo(int group, int groups) =>
            $"{(groups == 1 ? "Lasche und Schlitz" : $"Lasche und Schlitz, Gruppe {group}")}: Die Laschenkante läuft in der Kopie andersherum – D1 und D2 getauscht.";

        /// <summary>Reading back the copy found other entities than the mirrored ones.</summary>
        public static string OtherEntities(int group, int groups, string what) =>
            $"{(groups == 1 ? "Lasche und Schlitz" : $"Lasche und Schlitz, Gruppe {group}")}: SOLIDWORKS nennt eine andere {what} als die gespiegelte.";

        /// <summary>The copy has no single slot feature to take the original slot's name.</summary>
        public static string SlotNotFound(string slot, int candidates) =>
            $"Der Schlitz „{slot}“ hat in der Kopie kein eindeutiges eigenes Feature ({candidates} weitere neue Features); er entsteht mit der Lasche, sein Name wird nicht übernommen.";

        /// <summary>
        /// A later tab and slot <paramref name="name"/> with the same edges and faces as <paramref name="tab"/>, captured before:
        /// its slot feature, which the tree did not recognise (B64). If the tab has no slot yet, it becomes its slot and a part
        /// of it like one the tree found: the tab is measured up to it (MeasureUntil), the copy's slot takes its name, and it
        /// is reported with the tab's fate – built with it, or „nicht entstanden“ when the tab fails. If the tree already gave
        /// the tab another slot, it is not rebuilt (and triggers no fallback).
        /// </summary>
        public static FeatureRecord SlotBySameGroups(TabAndSlotRecord tab, string name, string type)
        {
            tab.SlotFeatureName ??= name;
            if (tab.SlotFeatureName == name)
                return new PartOfFeatureRecord(name, type, tab.Name, PartOwnerKind.TabAndSlot) { Notes = { SlotNotInTree(tab.Name) } };
            return new UnsupportedRecord(name, type, SameAsEarlier(tab.Name, tab.SlotFeatureName), false) { TriggersFallback = false };
        }

        /// <summary>The note on a slot that only the same edges and faces tell (B64): it is reported as a part of its tab.</summary>
        public static string SlotNotInTree(string tab) =>
            $"Vom Baum nicht als Schlitz erkannt: gleiche Kanten und Flächen wie Lasche und Schlitz „{tab}“, als deren Schlitz behandelt.";

        /// <summary>A later tab and slot with the same edges and faces as one captured before that already has its slot: not rebuilt.</summary>
        public static string SameAsEarlier(string earlier, string slot) =>
            $"Gleiche Kanten und Flächen wie Lasche und Schlitz „{earlier}“, deren Schlitz aber „{slot}“ ist – wird nicht für sich nachgebaut.";
    }
}
