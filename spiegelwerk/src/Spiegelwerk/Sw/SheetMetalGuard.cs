using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Verification;

namespace Spiegelwerk.Sw
{
    /// <summary>
    /// Keeps the part's shared sheet-metal values as they were (A1 P1, §3). Thickness, radius and
    /// allowance belong to the part's defaults; a later base flange, created or toggled the wrong
    /// way, can rewrite them – and with them every body that uses the defaults, also one whose
    /// step was already reported OK. Before such a step the guard takes the defaults and every
    /// SheetMetal of the target, afterwards it compares; a difference is written back, and if
    /// that does not restore them, the run stops.
    /// </summary>
    internal sealed class SheetMetalGuard
    {
        private readonly ModelDoc2 _doc;

        public SheetMetalGuard(ModelDoc2 doc)
        {
            _doc = doc;
        }

        /// <summary>The values before a step, and the masses of the bodies there were.</summary>
        internal sealed class State
        {
            public SheetMetalParameters? Template { get; set; }
            public Dictionary<string, SheetMetalParameters> SheetMetals { get; } = new Dictionary<string, SheetMetalParameters>(StringComparer.Ordinal);
            public Dictionary<string, MassState> Bodies { get; } = new Dictionary<string, MassState>(StringComparer.Ordinal);
        }

        /// <summary>
        /// The state before a step; null if there is nothing to protect yet (no defaults and no
        /// SheetMetal: the first base flange makes them) or it cannot be read.
        /// </summary>
        public State? Take()
        {
            try
            {
                var s = new State();
                if (SwSheetMetal.Template(_doc) is Feature t) s.Template = SwSheetMetal.ReadParameters(_doc, t, false);
                foreach (Feature sm in SwSheetMetal.OfType(_doc, "SheetMetal")) s.SheetMetals[sm.Name] = SwSheetMetal.ReadParameters(_doc, sm, true);
                if (s.Template == null && s.SheetMetals.Count == 0) return null;
                foreach (Body2 b in SwDoc.SolidBodies(_doc)) s.Bodies[b.Name] = SwDoc.MassOf(new[] { b });
                return s;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Compares with the state before the step. A difference is written back; <paramref name="notes"/>
        /// says so. Returns the reason to stop the run if the values or the earlier bodies are not
        /// as they were afterwards, else null.
        /// </summary>
        public string? Check(State before, List<string> notes)
        {
            List<string> changed = Differences(before);
            if (changed.Count == 0) return null;

            if (before.Template != null && SwSheetMetal.Template(_doc) is Feature t)
                SwSheetMetal.Apply(_doc, t, before.Template, false, Array.Empty<int>());
            foreach (var kv in before.SheetMetals)
                if (SwDoc.FeatureByName(_doc, kv.Key) is Feature sm)
                    SwSheetMetal.Apply(_doc, sm, kv.Value, true, SheetMetalRules.BodyOverrides(kv.Value));
            _doc.EditRebuild3();

            List<string> still = Differences(before);
            MassState total = SwDoc.MassOf(_doc);
            var moved = new List<string>();
            var now = SwDoc.SolidBodies(_doc).ToDictionary(b => b.Name, b => SwDoc.MassOf(new[] { b }), StringComparer.Ordinal);
            foreach (var kv in before.Bodies)
                if (!now.TryGetValue(kv.Key, out MassState mass) || !MassComparison.Compare(MassState.Empty, mass - kv.Value, total).Passes)
                    moved.Add(kv.Key);
            if (still.Count == 0 && moved.Count == 0)
            {
                notes.Add($"Die Blech-Standardwerte hatten sich mit diesem Schritt geändert und wurden wiederhergestellt ({string.Join("; ", changed)}).");
                return null;
            }
            string what = string.Join("; ", still.Count > 0 ? still : changed);
            string bodies = moved.Count > 0 ? $" Betroffen: {string.Join(", ", moved.Select(n => $"„{n}“"))}." : "";
            return $"Achtung: Die Blech-Standardwerte des Teils wurden verändert und ließen sich nicht wiederherstellen ({what}).{bodies} " +
                   "Die bisher gespiegelten Blechkörper können sich geändert haben – bitte prüfen.";
        }

        private List<string> Differences(State before)
        {
            var result = new List<string>();
            if (before.Template != null && SwSheetMetal.Template(_doc) is Feature t)
                result.AddRange(SwSheetMetal.Compare(_doc, t, before.Template, false));
            foreach (var kv in before.SheetMetals)
            {
                if (SwDoc.FeatureByName(_doc, kv.Key) is Feature sm) result.AddRange(SwSheetMetal.Compare(_doc, sm, kv.Value, true));
                else result.Add($"„{kv.Key}“ fehlt");
            }
            return result;
        }
    }
}
