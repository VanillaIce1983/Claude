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
    /// active configuration is folded (suppressed), and put back when the run ends – whatever
    /// happens. The copies get the recorded state at the end of the build.
    /// </summary>
    internal sealed class SheetMetalSession
    {
        private readonly ModelDoc2 _doc;

        /// <summary>Flat pattern name → was flattened when the run began.</summary>
        private readonly Dictionary<string, bool> _flattened = new Dictionary<string, bool>(StringComparer.Ordinal);

        private bool _restored;

        private SheetMetalSession(ModelDoc2 doc)
        {
            _doc = doc;
        }

        /// <summary>A session for a part with sheet metal; null for any other part.</summary>
        public static SheetMetalSession? For(ModelDoc2 doc) => SwSheetMetal.HasSheetMetal(doc) ? new SheetMetalSession(doc) : null;

        /// <summary>
        /// Folds every flattened flat pattern. Returns why the run cannot go on (the state is put
        /// back first; what could not be put back is said too), or null.
        /// </summary>
        public string? Fold()
        {
            List<Feature> flatPatterns = SwSheetMetal.OfType(_doc, "FlatPattern");
            foreach (Feature fp in flatPatterns) _flattened[fp.Name] = !SwSheetMetal.IsSuppressed(fp);
            foreach (Feature fp in flatPatterns.Where(f => _flattened[f.Name]))
            {
                if (SwSheetMetal.SetSuppressed(_doc, fp, true)) continue;
                List<string> lost = Restore();
                lost.Insert(0, $"Die Abwicklung „{fp.Name}“ ließ sich für den Lauf nicht zurückfalten. Es wurde nichts gespiegelt.");
                return string.Join(" ", lost);
            }
            return null;
        }

        /// <summary>Was the flat pattern flattened when the run began?</summary>
        public bool WasFlattened(string flatPattern) => _flattened.TryGetValue(flatPattern, out bool flat) && flat;

        /// <summary>Flattens again what was flattened. Returns report lines for what could not be put back; runs once.</summary>
        public List<string> Restore()
        {
            var lines = new List<string>();
            if (_restored) return lines;
            _restored = true;
            foreach (var kv in _flattened.Where(kv => kv.Value))
            {
                Feature? fp = SwSheetMetal.OfType(_doc, "FlatPattern").FirstOrDefault(f => f.Name == kv.Key);
                if (fp == null || !SwSheetMetal.SetSuppressed(_doc, fp, false))
                    lines.Add($"Abwicklung „{kv.Key}“ ließ sich im Original nicht wieder abwickeln – bitte von Hand abwickeln.");
            }
            return lines;
        }

        /// <summary>
        /// The copies of the flat patterns get the state of their originals (flattened or folded).
        /// Returns report lines for what could not be set.
        /// </summary>
        public List<string> ApplyTo(ModelDoc2 target, IEnumerable<(string original, Feature copy)> copies)
        {
            var lines = new List<string>();
            foreach (var (original, copy) in copies)
            {
                if (!WasFlattened(original)) continue;
                if (!SwSheetMetal.SetSuppressed(target, copy, false))
                    lines.Add($"Abwicklung „{copy.Name}“ ließ sich in der Kopie nicht abwickeln wie im Original.");
            }
            return lines;
        }
    }
}
