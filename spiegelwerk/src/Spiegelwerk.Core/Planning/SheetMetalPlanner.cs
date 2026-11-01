using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// Step 2 of the refusal (blech-plan §1.5), after capture and before the target is made:
    /// by position in the body, not by type. A body whose carrier is not rebuilt would come out
    /// empty or as something else – a swept-flange or lofted base, an unknown carrier, a feature
    /// that could not be read. Without a fallback plan (from S7a) nothing covers such a body.
    /// </summary>
    public static class SheetMetalPlanner
    {
        /// <summary>
        /// Why the run is refused, or null. Looked at: every sheet-metal body with a record of the
        /// run in its history. Refused when
        ///  - an unsupported record of the run on it is a carrier (<see cref="SheetMetalRole.Carrier"/>), or
        ///  - the run rebuilds the body from its first solid feature and that one is unsupported, or
        ///  - the run rebuilds the body and its SheetMetal has no base flange the run rebuilds.
        /// A body the run does not rebuild (only a later feature selected) is left to the ordinary rules.
        /// </summary>
        /// <param name="bodies">Every solid body of the original with its features in creation order (also in whole-part mode).</param>
        /// <param name="types">Feature name → GetTypeName2, for the features in the histories.</param>
        public static string? Refusal(IEnumerable<BodyHistory> bodies, IReadOnlyList<FeatureRecord> records, IReadOnlyDictionary<string, string> types,
            SheetMetalTree tree)
        {
            var byName = new Dictionary<string, FeatureRecord>(StringComparer.Ordinal);
            foreach (FeatureRecord r in records) byName[r.Name] = r;
            string TypeOf(string n) => types.TryGetValue(n, out string t) ? t : byName.TryGetValue(n, out FeatureRecord r) ? r.TypeName : "";

            foreach (BodyHistory body in bodies.Where(b => b.IsSheetMetal))
            {
                List<string> inRun = body.Features.Where(byName.ContainsKey).ToList();
                if (inRun.Count == 0) continue;
                foreach (string n in inRun)
                    if (byName[n] is UnsupportedRecord u && SheetMetalTypes.Of(u.TypeName)?.Role == SheetMetalRole.Carrier)
                        return Text(n, body.Name, u.Reason);

                string? first = body.Features.FirstOrDefault(n => MakesBody(TypeOf(n)));
                if (first == null || !byName.TryGetValue(first, out FeatureRecord firstRecord)) continue;
                if (firstRecord is UnsupportedRecord unsupported) return Text(first, body.Name, unsupported.Reason);
                foreach (string sm in body.Features.Where(n => TypeOf(n) == "SheetMetal"))
                {
                    if (!tree.Owners.TryGetValue(sm, out var owner))
                        return Text(sm, body.Name, "der Blechkörper hat keine eigene Basislasche");
                    if (!(byName.TryGetValue(owner.owner, out FeatureRecord o) && o is BaseFlangeRecord))
                        return Text(owner.owner, body.Name, "die Basislasche wird nicht neu aufgebaut");
                }
            }
            return null;
        }

        /// <summary>
        /// Could the feature be what made the body? Not the SheetMetal and flat pattern that come
        /// with a base flange, not folders, not features that change no solid.
        /// </summary>
        private static bool MakesBody(string type) =>
            type.Length > 0 && type != "SheetMetal" && type != "FlatPattern" && !SheetMetalTree.IsSystemType(type) && UnsupportedFeatures.ChangesSolid(type);

        /// <summary>„„Swept-Flange1“ trägt den Blechkörper „Körper1“ und wird nicht nachgebaut (…). Es wurde nichts gespiegelt.“</summary>
        public static string Text(string feature, string body, string reason) =>
            $"„{feature}“ trägt den Blechkörper „{body}“ und wird nicht nachgebaut ({reason.TrimEnd('.', ' ')}). Es wurde nichts gespiegelt.";
    }
}
