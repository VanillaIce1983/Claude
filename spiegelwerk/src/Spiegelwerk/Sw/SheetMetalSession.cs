using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace Spiegelwerk.Sw
{
    /// <summary>
    /// The folded state for the run (A1 §3, P3). Features added while a part is flattened land
    /// after the flat pattern and exist only flattened; the mass of a flattened part is not the
    /// folded one. So before anything is collected every flat pattern that is flattened in the
    /// active configuration is folded (suppressed) – and, legacy Insert Bends (S6), every
    /// suppressed ProcessBends unsuppressed –, and put back when the run ends – whatever
    /// happens. The copies get the recorded state at the end of the build; in the same part
    /// (S1b) both, the originals and their copies, end in the state the originals had: the
    /// originals are flattened after their copies, so the copies are checked again afterwards.
    /// </summary>
    internal sealed class SheetMetalSession
    {
        private readonly ModelDoc2 _doc;

        /// <summary>Flat pattern or ProcessBends name → was flattened when the run began.</summary>
        private readonly Dictionary<string, bool> _flattened = new Dictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>Same part: the copies <see cref="ApplyTo"/> flattened, by name – checked again once the originals are flattened.</summary>
        private readonly List<string> _flattenedCopies = new List<string>();

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

        /// <summary>„Abwicklung „X““ or, legacy, „„X“ (Biegungen einfügen)“.</summary>
        private static string Label(Feature f) => f.GetTypeName2() == "ProcessBends" ? $"„{f.Name}“ (Biegungen einfügen)" : $"Abwicklung „{f.Name}“";

        /// <summary>
        /// Folds every flattened flat pattern and legacy part. Returns why the run cannot go on (the state is put
        /// back first; what could not be put back is said too), or null.
        /// </summary>
        public string? Fold()
        {
            List<Feature> folds = FoldFeatures(_doc);
            foreach (Feature f in folds) _flattened[f.Name] = SwSheetMetal.IsFlattened(f);
            foreach (Feature f in folds.Where(f => _flattened[f.Name]))
            {
                if (SwSheetMetal.SetFlattened(_doc, f, false)) continue;
                List<string> lost = Restore();
                lost.Insert(0, f.GetTypeName2() == "ProcessBends"
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
                if (copy == null || !SwSheetMetal.IsFlattened(copy))
                    lines.Add($"{(copy == null ? $"„{name}“" : Label(copy))} ist in der Kopie nach dem Abwickeln des Originals nicht mehr abgewickelt – bitte von Hand abwickeln.");
            }
            return lines;
        }

        /// <summary>
        /// The copies of the flat patterns get the state of their originals (flattened or folded):
        /// at the end of the build, or – the build stopped on an unexpected error – where it stopped.
        /// In the same part the originals follow with <see cref="Restore"/>, so both end as they
        /// were; the copies flattened here are checked again there. Returns report lines for what
        /// could not be set; runs once.
        /// </summary>
        public List<string> ApplyTo(ModelDoc2 target, IEnumerable<(string original, Feature copy)> copies)
        {
            var lines = new List<string>();
            if (_applied) return lines;
            _applied = true;
            foreach (var (original, copy) in copies)
            {
                if (!WasFlattened(original)) continue;
                if (!SwSheetMetal.SetFlattened(target, copy, true))
                    lines.Add($"{Label(copy)} ließ sich in der Kopie nicht abwickeln wie im Original.");
                else if (ReferenceEquals(target, _doc))
                    _flattenedCopies.Add(copy.Name);
            }
            return lines;
        }
    }
}
