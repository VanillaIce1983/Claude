using System;
using System.Collections.Generic;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>What a sheet-metal feature type is in the part.</summary>
    public enum SheetMetalRole
    {
        /// <summary>Template folders: never collected, never deleted.</summary>
        System,

        /// <summary>A sub-feature (a bend of a flange): never collected on its own.</summary>
        Part,

        /// <summary>Creates or re-bases a sheet-metal body: without it everything after it fails.</summary>
        Carrier,

        /// <summary>Acts on an existing sheet-metal body.</summary>
        Leaf,
    }

    /// <summary>How far Spiegelwerk is with a sheet-metal type. Each slice of blech-plan §3 moves its own types on.</summary>
    public enum SheetMetalState
    {
        /// <summary>No slice has decided it yet: a run that collects it is refused before capture.</summary>
        NotYet,

        /// <summary>Rebuilt by a builder.</summary>
        Rebuilt,

        /// <summary>Decided as not rebuilt: a leaf is an ordinary failed entry, a carrier refuses unless the fallback covers its body.</summary>
        Unsupported,
    }

    public sealed class SheetMetalType
    {
        public SheetMetalType(SheetMetalRole role, SheetMetalState state, string? reason = null, bool triggersFallback = true)
        {
            Role = role;
            State = state;
            Reason = reason;
            TriggersFallback = triggersFallback;
        }

        public SheetMetalRole Role { get; }
        public SheetMetalState State { get; }

        /// <summary>Report text of an <see cref="SheetMetalState.Unsupported"/> type; null: the general sheet-metal text.</summary>
        public string? Reason { get; }

        /// <summary>An unsupported record of this type hands its body to the fallback (from blech-plan S7a).</summary>
        public bool TriggersFallback { get; }
    }

    /// <summary>
    /// The sheet-metal feature types (IFeature.GetTypeName2) with their role and state
    /// (blech-plan §1.5). Only confirmed names are listed: a type whose name is still open (swept
    /// flange, tab and slot, vent, welded corner, corner relief) is not refused by name – a leaf
    /// of that kind is an ordinary failed record, a carrier is caught by its position in the
    /// body (from S1a). Its name joins the table once a live test confirms it.
    /// </summary>
    public static class SheetMetalTypes
    {
        private static SheetMetalType NotYet(SheetMetalRole role) => new SheetMetalType(role, SheetMetalState.NotYet);

        // S0: every entry NotYet – the run refuses exactly what it refused before.
        private static readonly Dictionary<string, SheetMetalType> Table = new Dictionary<string, SheetMetalType>(StringComparer.Ordinal)
        {
            ["TemplateSheetMetal"] = NotYet(SheetMetalRole.System),
            ["TemplateFlatPattern"] = NotYet(SheetMetalRole.System),

            // Bend sub-features; HemBend and Miterbend join once their names are confirmed (B23).
            ["OneBend"] = NotYet(SheetMetalRole.Part),
            ["SketchBend"] = NotYet(SheetMetalRole.Part),

            // A tab is an SMBaseFlange too, but a leaf; the SheetMetal feature is a carrier only without an accepted owner.
            ["SMBaseFlange"] = NotYet(SheetMetalRole.Carrier),
            ["SheetMetal"] = NotYet(SheetMetalRole.Carrier),
            ["FlattenBends"] = NotYet(SheetMetalRole.Carrier),
            ["ProcessBends"] = NotYet(SheetMetalRole.Carrier),
            ["SolidToSheetMetal"] = NotYet(SheetMetalRole.Carrier),
            ["LoftedBend"] = NotYet(SheetMetalRole.Carrier),
            // Meaning unknown: kept as a carrier, the cautious side.
            ["ToroidalBend"] = NotYet(SheetMetalRole.Carrier),

            ["EdgeFlange"] = NotYet(SheetMetalRole.Leaf),
            ["SMMiteredFlange"] = NotYet(SheetMetalRole.Leaf),
            ["Hem"] = NotYet(SheetMetalRole.Leaf),
            ["Jog"] = NotYet(SheetMetalRole.Leaf),
            ["SM3dBend"] = NotYet(SheetMetalRole.Leaf),
            ["FlatPattern"] = NotYet(SheetMetalRole.Leaf),
            ["UnFold"] = NotYet(SheetMetalRole.Leaf),
            ["Fold"] = NotYet(SheetMetalRole.Leaf),
            ["BreakCorner"] = NotYet(SheetMetalRole.Leaf),
            ["CornerTrim"] = NotYet(SheetMetalRole.Leaf),
            ["CrossBreak"] = NotYet(SheetMetalRole.Leaf),
            // Closed corner: the name comes from live third-party code, not from the API help.
            ["CornerFeat"] = NotYet(SheetMetalRole.Leaf),
            ["NormalCut"] = NotYet(SheetMetalRole.Leaf),
            ["Rip"] = NotYet(SheetMetalRole.Leaf),
            ["SMGusset"] = NotYet(SheetMetalRole.Leaf),
            ["FormToolInstance"] = NotYet(SheetMetalRole.Leaf),
        };

        public static bool IsSheetMetal(string type) => Table.ContainsKey(type);

        /// <summary>The entry of a sheet-metal type; null for any other type.</summary>
        public static SheetMetalType? Of(string type) => Table.TryGetValue(type, out SheetMetalType t) ? t : null;

        /// <summary>Every listed type name.</summary>
        public static IEnumerable<string> Names => Table.Keys;

        /// <summary>
        /// Does an unsupported record of this type hand its body to the fallback? As the table
        /// says; true for any type it does not list – a generic record changes the solid and
        /// triggers until its name is confirmed (blech-plan K17).
        /// </summary>
        public static bool TriggersFallback(string type) => Of(type)?.TriggersFallback ?? true;

        /// <summary>
        /// Refused before capture: a sheet-metal type that is not part of an accepted owner,
        /// if no slice has decided it yet – or if it carries a body, is not rebuilt and the
        /// fallback is off. A sub-feature is never refused on its own when its owner is accepted.
        /// </summary>
        public static bool Refuses(string type, bool partOfAcceptedOwner, bool fallbackOn) => Refuses(Of(type), partOfAcceptedOwner, fallbackOn);

        /// <summary>The rule of <see cref="Refuses(string, bool, bool)"/> for an entry (null: not sheet metal).</summary>
        public static bool Refuses(SheetMetalType? type, bool partOfAcceptedOwner, bool fallbackOn)
        {
            if (partOfAcceptedOwner || type == null) return false;
            return type.State == SheetMetalState.NotYet ||
                   (type.Role == SheetMetalRole.Carrier && type.State != SheetMetalState.Rebuilt && !fallbackOn);
        }
    }
}
