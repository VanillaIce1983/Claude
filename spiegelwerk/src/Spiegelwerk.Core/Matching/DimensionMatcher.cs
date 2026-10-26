using System;
using System.Collections.Generic;
using System.Linq;

namespace Spiegelwerk.Core.Matching
{
    /// <summary>A dimension of a feature as the matcher sees it: name, value and kind.</summary>
    public sealed class NamedDimension
    {
        public NamedDimension(string name, double value, int kind)
        {
            Name = name;
            Value = value;
            Kind = kind;
        }

        /// <summary>Name within its feature, e.g. "D1".</summary>
        public string Name { get; }

        /// <summary>System units: metres, radians, or a plain number (instance count).</summary>
        public double Value { get; }

        /// <summary>IDisplayDimension.Type2 (linear, angular ...): equal values of different kinds are different dimensions.</summary>
        public int Kind { get; }

        public override string ToString() => $"{Name} = {Value}";
    }

    /// <summary>Which dimension of the copy is which of the original, and the renames that give each its original name.</summary>
    public sealed class DimensionMatch
    {
        internal DimensionMatch(int[] counterparts, List<(int copy, string name)> renames)
        {
            Counterparts = counterparts;
            Renames = renames;
        }

        /// <summary>Per dimension of the original: index of its counterpart in the copy, -1 if there is none for certain.</summary>
        public IReadOnlyList<int> Counterparts { get; }

        /// <summary>
        /// Dimensions of the copy to rename (index, new name): each counterpart to its original's
        /// name, and one without original out of the way if it holds such a name.
        /// </summary>
        public IReadOnlyList<(int copy, string name)> Renames { get; }
    }

    /// <summary>
    /// A rebuilt feature has the dimensions of the original (depth, radius, spacing ...) with
    /// the same values, but SOLIDWORKS names them in creation order – and a builder that creates
    /// a feature in steps (an extrude with provisional end conditions, edited afterwards) gets
    /// other names. Copied equations name dimensions ("D1@Extrude1"), so the copy's get the
    /// original names back. The counterpart is the dimension with the same name, kind and value;
    /// otherwise the only one left with that kind and value, provided the original has only one
    /// left with them too. Equal values that cannot be told apart stay unmatched.
    /// </summary>
    public static class DimensionMatcher
    {
        /// <summary>Same value, as dimensions are compared elsewhere (system units).</summary>
        public static bool SameValue(double a, double b) => Math.Abs(a - b) <= Math.Max(1e-9, Math.Abs(a) * 1e-7);

        public static DimensionMatch Match(IReadOnlyList<NamedDimension> original, IReadOnlyList<NamedDimension> copy)
        {
            var counterparts = new int[original.Count];
            var taken = new bool[copy.Count];
            for (int o = 0; o < original.Count; o++)
            {
                counterparts[o] = -1;
                for (int c = 0; c < copy.Count; c++)
                    if (!taken[c] && copy[c].Name == original[o].Name && Same(copy[c], original[o]))
                    {
                        counterparts[o] = c;
                        taken[c] = true;
                        break;
                    }
            }
            for (int o = 0; o < original.Count; o++)
            {
                if (counterparts[o] >= 0) continue;
                NamedDimension wanted = original[o];
                bool unique = Enumerable.Range(0, original.Count).Count(x => counterparts[x] < 0 && Same(original[x], wanted)) == 1;
                List<int> candidates = Enumerable.Range(0, copy.Count).Where(c => !taken[c] && Same(copy[c], wanted)).ToList();
                if (!unique || candidates.Count != 1) continue;
                counterparts[o] = candidates[0];
                taken[candidates[0]] = true;
            }

            var renames = new List<(int copy, string name)>();
            var wantedNames = new HashSet<string>(StringComparer.Ordinal);
            for (int o = 0; o < original.Count; o++)
            {
                if (counterparts[o] < 0) continue;
                wantedNames.Add(original[o].Name);
                if (copy[counterparts[o]].Name != original[o].Name) renames.Add((counterparts[o], original[o].Name));
            }
            // One without original that holds a wanted name makes room, under a name no dimension of the original has.
            var used = new HashSet<string>(original.Select(d => d.Name).Concat(copy.Select(d => d.Name)), StringComparer.Ordinal);
            for (int c = 0; c < copy.Count; c++)
                if (!taken[c] && wantedNames.Contains(copy[c].Name))
                    renames.Add((c, FreeName(used)));
            return new DimensionMatch(counterparts, renames);
        }

        private static bool Same(NamedDimension a, NamedDimension b) => a.Kind == b.Kind && SameValue(a.Value, b.Value);

        /// <summary>"D1", "D2" ... as SOLIDWORKS names them: the first one not used yet.</summary>
        private static string FreeName(HashSet<string> used)
        {
            for (int i = 1; ; i++)
                if (used.Add($"D{i}")) return $"D{i}";
        }
    }
}
