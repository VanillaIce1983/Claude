using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Planning;

namespace Spiegelwerk.Sw
{
    /// <summary>
    /// The folded state for the run (A1 §3, P3). Features added while a part is flattened land
    /// after the flat pattern and exist only flattened; the mass of a flattened part is not the
    /// folded one. So before anything is collected every flat pattern that is flattened in the
    /// active configuration is folded (suppressed) – and, legacy Insert Bends (S6), every
    /// ProcessBends suppressed on its own unsuppressed with its dependents (what was sketched
    /// on the bent faces after it was suppressed with it) –, and put back when the run ends – whatever
    /// happens. The copies get the recorded state at the end of the build; in the same part
    /// (S1b) both, the originals and their copies, end in the state the originals had: the
    /// originals are flattened after their copies, so the copies are checked again afterwards.
    /// What a ProcessBends brought back is suppressed with it again in the original (its children);
    /// their copies sit on planes, not on the copy's bent faces, so the copy suppresses them itself.
    /// </summary>
    internal sealed class SheetMetalSession
    {
        private readonly ModelDoc2 _doc;

        /// <summary>Flat pattern or ProcessBends name → was flattened when the run began.</summary>
        private readonly Dictionary<string, bool> _flattened = new Dictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>Same part: the copies <see cref="ApplyTo"/> flattened, by name – checked again once the originals are flattened.</summary>
        private readonly List<string> _flattenedCopies = new List<string>();

        /// <summary>ProcessBends name → the features bending it back with its dependents brought back (<see cref="SheetMetalRules.BroughtBack"/>).</summary>
        private readonly Dictionary<string, List<string>> _broughtBack = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        private bool _restored;
        private bool _applied;

        private SheetMetalSession(ModelDoc2 doc)
        {
            _doc = doc;
        }

        /// <summary>A session for a part with sheet metal; null for any other part.</summary>
        public static SheetMetalSession? For(ModelDoc2 doc) => SwSheetMetal.HasSheetMetal(doc) ? new SheetMetalSession(doc) : null;

        /// <summary>The features that hold the flattened state: flat patterns (top level and folders) and the ProcessBends of legacy Insert Bends.</summary>
        private static List<Feature> FoldFeatures(ModelDoc2 doc) => SwSheetMetal.OfType(doc, "FlatPattern").Concat(SwSheetMetal.OfType(doc, "ProcessBends")).ToList();

        private static bool IsLegacy(Feature f) => f.GetTypeName2() == "ProcessBends";

        /// <summary>„Abwicklung „X““ or, legacy, „„X“ (Biegungen einfügen)“.</summary>
        private static string Label(Feature f) => IsLegacy(f) ? $"„{f.Name}“ (Biegungen einfügen)" : $"Abwicklung „{f.Name}“";

        /// <summary>The names of every suppressed feature, in creation order; null if the tree does not read (then nothing is recorded).</summary>
        private List<string>? SuppressedNames()
        {
            try
            {
                return SwDoc.FeaturesInCreationOrder(_doc).Where(SwSheetMetal.IsSuppressed).Select(f => f.Name).ToList();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Folds every flattened flat pattern and legacy part. Returns why the run cannot go on (the state is put
        /// back first; what could not be put back is said too), or null.
        /// </summary>
        public string? Fold()
        {
            List<Feature> folds = FoldFeatures(_doc);
            foreach (Feature f in folds) _flattened[f.Name] = SwSheetMetal.IsFlattened(_doc, f);
            var foldNames = new HashSet<string>(folds.Select(f => f.Name), StringComparer.Ordinal);
            // ProcessBends first: bent back with its dependents, it may bring back a folded flat pattern of its body (its child).
            // The flat patterns are judged after that, by their state then (lazily) – recorded as they were at the start, so
            // Restore leaves such a one to the ProcessBends that takes it along again.
            IEnumerable<Feature> order = folds.Where(f => IsLegacy(f) && _flattened[f.Name])
                .Concat(folds.Where(f => !IsLegacy(f) && SwSheetMetal.IsFlattened(_doc, f)));
            foreach (Feature f in order)
            {
                List<string>? before = IsLegacy(f) ? SuppressedNames() : null;
                if (SwSheetMetal.SetFlattened(_doc, f, false))
                {
                    if (before != null && SuppressedNames() is List<string> after) _broughtBack[f.Name] = SheetMetalRules.BroughtBack(before, after, foldNames);
                    continue;
                }
                List<string> lost = Restore();
                lost.Insert(0, IsLegacy(f)
                    ? $"„{f.Name}“ (Biegungen einfügen) ließ sich für den Lauf nicht zurückbiegen. Es wurde nichts gespiegelt."
                    : $"Die Abwicklung „{f.Name}“ ließ sich für den Lauf nicht zurückfalten. Es wurde nichts gespiegelt.");
                return string.Join(" ", lost);
            }
            return null;
        }

        /// <summary>Was the flat pattern (or ProcessBends) flattened when the run began?</summary>
        public bool WasFlattened(string flatPattern) => _flattened.TryGetValue(flatPattern, out bool flat) && flat;

        /// <summary>Flattens again what was flattened. Returns report lines for what could not be put back; runs once.</summary>
        public List<string> Restore()
        {
            var lines = new List<string>();
            if (_restored) return lines;
            _restored = true;
            List<Feature> folds = FoldFeatures(_doc);
            foreach (var kv in _flattened.Where(kv => kv.Value))
            {
                Feature? f = folds.FirstOrDefault(x => x.Name == kv.Key);
                if (f == null || !SwSheetMetal.SetFlattened(_doc, f, true))
                    lines.Add($"{(f == null ? $"„{kv.Key}“" : Label(f))} ließ sich im Original nicht wieder abwickeln – bitte von Hand abwickeln.");
            }
            // Same part: flattening an original may fold a copy again (one part, two flat patterns flattened at once).
            folds = FoldFeatures(_doc);
            foreach (string name in _flattenedCopies)
            {
                Feature? copy = folds.FirstOrDefault(f => f.Name == name);
                if (copy == null || !SwSheetMetal.IsFlattened(_doc, copy))
                    lines.Add($"{(copy == null ? $"„{name}“" : Label(copy))} ist in der Kopie nach dem Abwickeln des Originals nicht mehr abgewickelt – bitte von Hand abwickeln.");
            }
            return lines;
        }

        /// <summary>
        /// The copies of the flat patterns get the state of their originals (flattened or folded):
        /// at the end of the build, or – the build stopped on an unexpected error – where it stopped.
        /// A copy's ProcessBends flattened, the copies of what its original brought back
        /// (<paramref name="copiesOf"/>: original name → its copies) are suppressed too – in the copy
        /// they are no children of it. In the same part the originals follow with <see cref="Restore"/>,
        /// so both end as they were; the copies flattened here are checked again there. Returns
        /// report lines for what could not be set; runs once.
        /// </summary>
        public List<string> ApplyTo(ModelDoc2 target, IEnumerable<(string original, Feature copy)> copies, Func<string, List<Feature>> copiesOf)
        {
            var lines = new List<string>();
            if (_applied) return lines;
            _applied = true;
            foreach (var (original, copy) in copies)
            {
                if (!WasFlattened(original)) continue;
                if (!SwSheetMetal.SetFlattened(target, copy, true))
                {
                    lines.Add($"{Label(copy)} ließ sich in der Kopie nicht abwickeln wie im Original.");
                    continue;
                }
                if (ReferenceEquals(target, _doc)) _flattenedCopies.Add(copy.Name);
                if (!_broughtBack.TryGetValue(original, out List<string> back)) continue;
                // Parents first: a child suppressed with its parent is already in the state wanted (SetSuppressed returns at once).
                foreach (Feature dependent in back.SelectMany(copiesOf))
                    if (!SwSheetMetal.SetSuppressed(target, dependent, true))
                        lines.Add($"„{dependent.Name}“ ließ sich in der Kopie nicht mit {Label(copy)} unterdrücken wie im Original – bitte von Hand unterdrücken.");
            }
            return lines;
        }
    }
}
