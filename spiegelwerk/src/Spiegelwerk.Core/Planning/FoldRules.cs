using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>One way of selecting what an unfold or fold acts on: the fixed face, the bends and – a fold – its unfold.</summary>
    public sealed class FoldSelection
    {
        public FoldSelection(int fixedMark, int bendMark, bool withUnfold, string description)
        {
            FixedMark = fixedMark;
            BendMark = bendMark;
            WithUnfold = withUnfold;
            Description = description;
        }

        public int FixedMark { get; }
        public int BendMark { get; }

        /// <summary>The copy of the preceding unfold is selected first, with mark 0 (official fold example).</summary>
        public bool WithUnfold { get; }

        public string Description { get; }
    }

    /// <summary>
    /// Unfold and fold (A3 (2) §5.6, P8, blech-plan S5, K25): nothing has a direction – the fixed face is
    /// mapped and the bends are found again. How: by the name map of the run first (a fold names the bends
    /// its unfold found), then by their mirrored faces (<see cref="Matching.BendMatcher"/>), the owner's single
    /// bend last – never by position among several, which a reflection may reverse. The selections are
    /// tried in order until the read-back names the wanted face and bends (B47).
    /// </summary>
    public static class FoldRules
    {
        /// <summary>
        /// The selections in order: fixed face 1 and bends 4 (official example); a fold also with its
        /// unfold on mark 0 before them (the official „Insert and Access Fold“ example); bends on 2 (CADapter).
        /// </summary>
        public static IReadOnlyList<FoldSelection> Selections(FoldKind kind, bool hasUnfold)
        {
            var result = new List<FoldSelection> { new FoldSelection(1, 4, false, "feste Fläche 1, Biegungen 4") };
            if (kind == FoldKind.Fold && hasUnfold) result.Add(new FoldSelection(1, 4, true, "Abwicklung 0, feste Fläche 1, Biegungen 4"));
            result.Add(new FoldSelection(1, 2, false, "feste Fläche 1, Biegungen 2"));
            return result;
        }

        /// <summary>
        /// The owner's single bend stands in for a bend found neither by name nor by its faces: only if the
        /// original owner had exactly this bend and the copy of the owner has exactly one (K25).
        /// </summary>
        public static bool UseOwnersOnlyBend(BendRef bend, int copyOwnerBends) => bend.OrdinalUsable && copyOwnerBends == 1;

        /// <summary>The bends the copy names, as a set of names, against the wanted ones.</summary>
        public static bool SameBends(IEnumerable<string> wanted, IEnumerable<string> got)
        {
            var w = new HashSet<string>(wanted, StringComparer.Ordinal);
            var g = new HashSet<string>(got, StringComparer.Ordinal);
            return w.SetEquals(g);
        }

        public static string KindName(FoldKind kind) => kind == FoldKind.Fold ? "Zurückbiegen" : "Abwickeln";

        public static string Describe(FoldRecord r) =>
            $"{KindName(r.Kind)}, {(r.Bends.Count == 1 ? "1 Biegung" : $"{r.Bends.Count} Biegungen")}";

        public static string FixedFaceMissing(FoldKind kind) => $"Feste Fläche von „{KindName(kind)}“ im Ziel nicht gefunden.";

        public static string FixedFaceNotOwn(FoldKind kind) =>
            $"Feste Fläche von „{KindName(kind)}“ liegt im Ziel nicht auf einem gespiegelten Körper; das Original bleibt unberührt.";

        public static string BendMissing(string bend) => $"Biegung „{bend}“ im Ziel nicht gefunden.";

        public static string BendNotOwn(string bend) =>
            $"Biegung „{bend}“ liegt im Ziel nur auf dem Originalkörper; das Original bleibt unberührt.";

        /// <summary>Information: a bend found as the single bend of its owner's copy.</summary>
        public static string ByOwner(string bend, string owner) => $"Biegung „{bend}“ als einzige Biegung von „{owner}“ wiedergefunden.";

        /// <summary>Several bends of the target had the mirrored faces of one.</summary>
        public static string Ambiguous(string bend, int candidates) => $"Biegung „{bend}“: {candidates} passende Biegungen im Ziel, die passendste wurde gewählt.";

        public const string NotAsWanted = "SOLIDWORKS hat die Auswahl in keiner Variante wie gewünscht übernommen (feste Fläche oder Biegungen weichen ab).";
    }
}
