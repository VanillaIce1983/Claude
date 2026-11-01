using System;
using System.Collections.Generic;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// Hem (A2, blech-plan S2): which values a hem type uses, the read-back comparison, what its bends
    /// inherit, and the relief policy (A2 P9). Everything is unchanged by the reflection: the hem is
    /// defined by the sheet's faces at its edge, which R maps onto the copy's.
    /// </summary>
    public static class HemRules
    {
        /// <summary>
        /// The values a hem of this type uses (InsertSheetMetalHem2: length for open and closed,
        /// gap for open, angle and radius for tear-drop and rolled), and always the miter gap. A
        /// double hem is not described by the help; its length and gap are compared.
        /// </summary>
        public static List<(string name, double value, ScalarKind kind)> Values(HemRecord r)
        {
            var values = new List<(string, double, ScalarKind)>();
            bool lengthUsed = r.HemType == HemTypes.Open || r.HemType == HemTypes.Closed || r.HemType == HemTypes.Double;
            bool gapUsed = r.HemType == HemTypes.Open || r.HemType == HemTypes.Double;
            bool arcUsed = r.HemType == HemTypes.TearDrop || r.HemType == HemTypes.Rolled;
            if (lengthUsed) values.Add(("Saumlänge", r.Length, ScalarKind.Length));
            if (gapUsed) values.Add(("Spaltabstand", r.Gap, ScalarKind.Length));
            if (arcUsed)
            {
                // Not folded: unlike a bend angle, 2π − θ is another hem (a tear-drop hem of 200° is not one of 160°).
                values.Add(("Saumwinkel", r.Angle, ScalarKind.PlainAngle));
                values.Add(("Saumradius", r.Radius, ScalarKind.Length));
            }
            values.Add(("Gehrungsspalt", r.MiterGap, ScalarKind.Length));
            return values;
        }

        /// <summary>
        /// The hem as read back against what it should be: type, position, direction and the values
        /// its type uses. The allowance is compared on its own (<see cref="SheetMetalCompare.Allowance"/>):
        /// it is written in an edit of its own.
        /// </summary>
        public static List<string> Differences(HemRecord want, HemRecord got, IFormatProvider? culture = null)
        {
            var values = new List<(string, double, double, ScalarKind)>
            {
                ("Saumart", want.HemType, got.HemType, ScalarKind.Choice),
                ("Lage des Saums", want.Position, got.Position, ScalarKind.Choice),
                ("Saumrichtung umgekehrt", want.Reverse ? 1 : 0, got.Reverse ? 1 : 0, ScalarKind.Flag),
            };
            // Only if the type is right: the other type's values say nothing.
            if (want.HemType == got.HemType)
            {
                List<(string name, double value, ScalarKind kind)> w = Values(want), g = Values(got);
                for (int i = 0; i < w.Count; i++) values.Add((w[i].name, w[i].value, g[i].value, w[i].kind));
            }
            return SheetMetalCompare.Scalars(values, culture);
        }

        /// <summary>
        /// What the hem hands down to its bend: its radius (that of a tear-drop or rolled hem; for the
        /// other types as read) and its own allowance – null if it uses the default, then every own
        /// allowance of a bend counts. The hem's relief is not readable: every own relief of a bend counts.
        /// </summary>
        public static BendValues Parent(HemRecord r) => new BendValues
        {
            Name = r.Name,
            UseDefaultRadius = false,
            Radius = r.Radius,
            UseDefaultAllowance = r.OwnAllowance == null,
            Allowance = r.OwnAllowance,
            UseDefaultRelief = true,
            Relief = null,
        };

        /// <summary>
        /// Whether the step is checked against the original (A2 P9): with „Jeden Schritt prüfen“, a
        /// check that decides (not information only) and a measured reference. The relief is not
        /// readable, but a relief cut changes the folded body – a passing check confirms it.
        /// </summary>
        public static bool ReliefCheckable(bool verifySteps, bool checkDecides, bool hasReference) => verifySteps && checkDecides && hasReference;

        /// <summary>
        /// The relief note for a step that is not checked; null for a checked one (it gets
        /// <see cref="ReliefHint"/> for a failing check and <see cref="ReliefChecked"/> for a passing one).
        /// A note makes the step a warning – and blocks „Original löschen“ –, so it is given only where nothing confirms the relief.
        /// </summary>
        public static string? ReliefNote(bool checkable) => checkable ? null : ReliefUnchecked;

        /// <summary>The relief of a hem that is not checked.</summary>
        public const string ReliefUnchecked = "Entlastung des Saums nicht lesbar: Die Kopie hat die Standard-Entlastung (nicht geprüft).";

        /// <summary>Said only if the check finds a deviation.</summary>
        public const string ReliefHint = "Die Entlastung des Saums ist nicht lesbar; die Kopie hat die Standard-Entlastung – die Abweichung kann daher rühren.";

        /// <summary>Added to the verification text of a passing check.</summary>
        public const string ReliefChecked = "Entlastung: Standard (geprüft)";
    }
}
