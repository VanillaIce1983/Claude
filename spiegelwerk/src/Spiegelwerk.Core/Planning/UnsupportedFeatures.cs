using System;
using System.Collections.Generic;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>Report text for feature types (IFeature.GetTypeName2) that are not rebuilt.</summary>
    public static class UnsupportedFeatures
    {
        private const string MirrorApi = "wird bewusst nicht nachgebaut, weil Spiegelwerk die Spiegel-API nicht verwendet.";

        /// <summary>A sheet-metal feature or folder (<see cref="SheetMetalTypes"/>).</summary>
        public static bool IsSheetMetal(string type) => SheetMetalTypes.IsSheetMetal(type);

        /// <summary>
        /// Step 1 of the refusal (blech-plan §1.5), before anything is captured: „Teil spiegeln“
        /// always – the part is derived from another file –, and sheet metal as
        /// <see cref="SheetMetalTypes.Refuses"/> decides. Such features carry the base body and
        /// are not rebuilt: everything after them would fail too, and in an assembly an empty
        /// counterpart would be saved and inserted.
        /// </summary>
        /// <param name="partOfAcceptedOwner">The feature comes into being with an owner that is rebuilt (its SheetMetal, a flat pattern's corner trim).</param>
        /// <param name="fallbackOn">The fallback for sheet-metal bodies is switched on.</param>
        public static bool RefusesBeforeCapture(string type, bool partOfAcceptedOwner, bool fallbackOn) =>
            type == "MirrorStock" || SheetMetalTypes.Refuses(type, partOfAcceptedOwner, fallbackOn);

        /// <summary><see cref="RefusesBeforeCapture"/> for a feature without an accepted owner, with the fallback off.</summary>
        public static bool RefusesRun(string type) => RefusesBeforeCapture(type, partOfAcceptedOwner: false, fallbackOn: false);

        /// <summary>Why the run was refused (<see cref="RefusesBeforeCapture"/>).</summary>
        public static string RunRefusal(string feature, string type) => IsSheetMetal(type)
            ? $"Blechteile werden nicht unterstützt (\"{feature}\"). Es wurde nichts gespiegelt."
            : $"\"{feature}\": {Reason(type)} Es wurde nichts gespiegelt.";

        /// <summary>A mirror feature with a sheet-metal seed: its second half would have to be read back under the sheet-metal session.</summary>
        public const string MirrorOverSheetMetal = "Spiegel-Feature über Blech-Features wird noch nicht nachgebaut.";

        public static string Reason(string type) => type switch
        {
            // Reached only if the definition does not answer as IAdvancedHoleFeatureData.
            "AdvHoleWzd" or "AdvancedHole" => "Erweiterte Bohrung: Feature-Daten nicht lesbar.",
            // Reached only if the definition does not answer as IMirrorPatternFeatureData / IMirrorSolidFeatureData.
            "MirrorPattern" or "MirrorSolid" => "Spiegel-Feature: Feature-Daten nicht lesbar. " +
                                                "Abhilfe: die zweite Hälfte nach dem Lauf im Ziel von Hand nachbauen.",
            "MirrorStock" => $"Teil spiegeln im Original: Das Teil ist von einem Ausgangsteil gespiegelt abgeleitet und {MirrorApi} " +
                             "Abhilfe: Das Ausgangsteil ist bis auf die Lage das Gegenstück, aber nur im Stand von „Teil spiegeln“. " +
                             "Features, die danach in diesem Teil angelegt wurden, fehlen ihm; sie gehören gespiegelt in eine Kopie des " +
                             "Ausgangsteils, nicht in das Ausgangsteil selbst (von dort kämen sie über die Verknüpfung auch hierher).",
            // Reached only if the definition does not answer as the pattern's data interface.
            "CurvePattern" or "SketchPattern" or "TablePattern" or "FillPattern" or "DimPattern" or "VarPattern" => "Muster: Feature-Daten nicht lesbar.",
            _ when SheetMetalTypes.Of(type)?.Reason is string own => own,
            _ when IsSheetMetal(type) => "Blech wird nicht unterstützt: Blech-Features werden nicht nachgebaut.",
            _ => $"Feature-Typ {type} wird nicht unterstützt.",
        };

        /// <summary>
        /// Does a feature of this type change solid bodies? For records that are not rebuilt:
        /// one that does is a failure („Folgende Features … können ebenfalls scheitern“), one
        /// that does not is only skipped. A cross break is cosmetic – lines on the flat pattern.
        /// </summary>
        public static bool ChangesSolid(string type) => !NonSolidTypes.Contains(type);

        private static readonly HashSet<string> NonSolidTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "RefAxis", "RefPoint", "CoordSys", "3DProfileFeature", "ProfileFeature", "RefPlane", "Helix", "CompositeCurve",
            "ReferenceCurve", "CurveInFile", "SplitLine", "CosmeticThread", "Attribute", "CrossBreak",
        };
    }
}
