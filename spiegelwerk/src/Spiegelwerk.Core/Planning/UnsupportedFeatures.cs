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
        public static string RunRefusal(string feature, string type)
        {
            if (!IsSheetMetal(type)) return $"\"{feature}\": {Reason(type)} Es wurde nichts gespiegelt.";
            if (SheetMetalTypes.Of(type)!.State == SheetMetalState.NotYet)
                return $"Blech-Feature „{feature}“ ({type}) wird noch nicht nachgebaut. Es wurde nichts gespiegelt.";
            return $"Blech-Feature „{feature}“ ({type}): {Reason(type)} Es wurde nichts gespiegelt.";
        }

        /// <summary>Sheet metal in the same part (until blech-plan S1b).</summary>
        public static string SamePartRefusal(string feature) =>
            $"Blech im selben Teil wird noch nicht unterstützt („{feature}“). Bitte in ein neues Teil spiegeln. Es wurde nichts gespiegelt.";

        /// <summary>
        /// Step 1 for the collected features in creation order: the first refused one
        /// (<see cref="RefusesBeforeCapture"/>), then – until S1b – sheet metal in the same part.
        /// A part (a SheetMetal, a flat pattern's sketch or own corner) counts as part of an
        /// accepted owner if its owner is collected and not refused itself. Null: the run goes on.
        /// </summary>
        /// <param name="owners">Part → owner (<see cref="SheetMetalTree.Owners"/>).</param>
        public static string? RefusalBeforeCapture(IReadOnlyList<(string name, string type)> collected,
            IReadOnlyDictionary<string, (string owner, Records.PartOwnerKind kind)> owners, bool samePart, bool fallbackOn)
        {
            var types = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, type) in collected) types[name] = type;
            bool Accepted(string name) =>
                owners.TryGetValue(name, out var o) && types.TryGetValue(o.owner, out string ownerType) &&
                !RefusesBeforeCapture(ownerType, partOfAcceptedOwner: false, fallbackOn);
            foreach (var (name, type) in collected)
                if (RefusesBeforeCapture(type, Accepted(name), fallbackOn))
                    return RunRefusal(name, type);
            if (samePart)
                foreach (var (name, type) in collected)
                    if (IsSheetMetal(type))
                        return SamePartRefusal(name);
            return null;
        }

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
        /// that does not is only skipped. A cross break is cosmetic – lines on the flat pattern; a
        /// flat pattern and a corner trim under it act on the flattened state only.
        /// </summary>
        public static bool ChangesSolid(string type) => !NonSolidTypes.Contains(type);

        private static readonly HashSet<string> NonSolidTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "RefAxis", "RefPoint", "CoordSys", "3DProfileFeature", "ProfileFeature", "RefPlane", "Helix", "CompositeCurve",
            "ReferenceCurve", "CurveInFile", "SplitLine", "CosmeticThread", "Attribute", "CrossBreak",
            // Sheet metal that leaves the folded solid as it is: the flat pattern and what lives under it, the template folders.
            "FlatPattern", "CornerTrim", "TemplateSheetMetal", "TemplateFlatPattern",
        };
    }
}
