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
    /// SheetMetal of the target, afterwards it compares; what differs is written back – with the
    /// material flag as it was, never as fixed values –, and if that does not restore them, the
    /// run stops. The body the step's own feature belongs to (the one a tab joins) changes by
    /// design and is left out of the body comparison.
    ///
    /// In the same part (S1b) the defaults and SheetMetals are the original's: the snapshot is
    /// taken from the first copied flange on, a step without one is not built
    /// (<see cref="SheetMetalRules.GuardExpectationOf"/>), and the earlier bodies – the original
    /// among them – are compared after every step, not only after a change of the values.
    /// </summary>
    internal sealed class SheetMetalGuard
    {
        private readonly ModelDoc2 _doc;
        private readonly bool _samePart;

        /// <param name="samePart">The copy is built into the original part.</param>
        public SheetMetalGuard(ModelDoc2 doc, bool samePart)
        {
            _doc = doc;
            _samePart = samePart;
        }

        /// <summary>The values before a step, and the masses and features of the bodies there were.</summary>
        internal sealed class State
        {
            public SheetMetalParameters? Template { get; set; }
            public Dictionary<string, SheetMetalParameters> SheetMetals { get; } = new Dictionary<string, SheetMetalParameters>(StringComparer.Ordinal);
            public Dictionary<string, MassState> Bodies { get; } = new Dictionary<string, MassState>(StringComparer.Ordinal);

            /// <summary>Body name → the names of its features (IBody2.GetFeatures); empty if not readable.</summary>
            public Dictionary<string, HashSet<string>> BodyFeatures { get; } = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
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
                foreach (Body2 b in SwDoc.SolidBodies(_doc))
                {
                    s.Bodies[b.Name] = SwDoc.MassOf(new[] { b });
                    s.BodyFeatures[b.Name] = FeatureNames(b);
                }
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
        /// as they were afterwards, else null. The bodies are compared after a change of the values,
        /// and in the same part always (<see cref="SheetMetalRules.GuardComparesBodies"/>).
        /// </summary>
        /// <param name="own">The step's feature if it stays (null after a failure or removal): the
        /// body it belongs to – the one a tab joined, maybe renamed after it – is not compared.</param>
        public string? Check(State before, List<string> notes, Feature? own)
        {
            List<string> changed = Differences(before);
            if (!SheetMetalRules.GuardComparesBodies(_samePart, changed.Count > 0)) return null;

            List<string> still = new List<string>();
            if (changed.Count > 0)
            {
                // Only what differs is written back, the material flag as it was (in the same part
                // the defaults and SheetMetals are the original's; SheetMetalRules.MaterialOnWrite).
                if (before.Template != null && SwSheetMetal.Template(_doc) is Feature t && SwSheetMetal.Compare(_doc, t, before.Template, false).Count > 0)
                    SwSheetMetal.Apply(_doc, t, before.Template, false, Array.Empty<int>(), restore: true);
                foreach (var kv in before.SheetMetals)
                    if (SwDoc.FeatureByName(_doc, kv.Key) is Feature sm && SwSheetMetal.Compare(_doc, sm, kv.Value, true).Count > 0)
                        SwSheetMetal.Apply(_doc, sm, kv.Value, true, SheetMetalRules.RestoreGroups(kv.Value, TryRead(sm)), restore: true);
                _doc.EditRebuild3();
                still = Differences(before);
            }

            List<string> moved = MovedBodies(before, own);
            if (still.Count == 0 && moved.Count == 0)
            {
                if (changed.Count > 0) notes.Add(SheetMetalRules.GuardRestored(changed));
                return null;
            }
            return SheetMetalRules.GuardAbort(_samePart, changed, still, moved);
        }

        /// <summary>
        /// The earlier bodies that are not as they were: missing by name, or with another mass. The
        /// bodies the step's feature belongs to are left out, by their features: a tab may rename
        /// the body it joins.
        /// </summary>
        private List<string> MovedBodies(State before, Feature? own)
        {
            MassState total = SwDoc.MassOf(_doc);
            var moved = new List<string>();
            List<Body2> all = SwDoc.SolidBodies(_doc);
            var now = all.ToDictionary(b => b.Name, b => SwDoc.MassOf(new[] { b }), StringComparer.Ordinal);
            string? ownName = own == null ? null : NameOf(own);
            List<HashSet<string>> ownBodies = ownName == null ? new List<HashSet<string>>()
                : all.Select(FeatureNames).Where(fs => fs.Contains(ownName)).ToList();
            foreach (var kv in before.Bodies)
            {
                bool joined = before.BodyFeatures.TryGetValue(kv.Key, out HashSet<string> had) && had.Count > 0 && ownBodies.Any(had.IsSubsetOf);
                if (joined) continue;
                if (!now.TryGetValue(kv.Key, out MassState mass) || !MassComparison.Compare(MassState.Empty, mass - kv.Value, total).Passes)
                    moved.Add(kv.Key);
            }
            return moved;
        }

        private SheetMetalParameters? TryRead(Feature sm)
        {
            try
            {
                return SwSheetMetal.ReadParameters(_doc, sm, true);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The names of a body's features; empty if not readable.</summary>
        private static HashSet<string> FeatureNames(Body2 b)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                if ((object)b.GetFeatures() is object[] fs)
                    foreach (Feature f in fs.OfType<Feature>()) names.Add(f.Name);
            }
            catch (Exception)
            {
                // Not readable: the body is compared like any other.
            }
            return names;
        }

        private static string? NameOf(Feature f)
        {
            try
            {
                return f.Name;
            }
            catch (Exception)
            {
                return null;
            }
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
