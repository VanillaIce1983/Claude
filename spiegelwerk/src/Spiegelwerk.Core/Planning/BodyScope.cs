using System;
using System.Collections.Generic;
using System.Linq;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>A solid body of the original and the features that made or changed it.</summary>
    public sealed class BodyHistory
    {
        public BodyHistory(string name, IEnumerable<string> features, bool selected)
        {
            Name = name;
            Features = features.ToList();
            Selected = selected;
        }

        public string Name { get; }

        /// <summary>Features that change solids (IBody2.GetFeatures without sketches and reference geometry), in creation order.</summary>
        public IReadOnlyList<string> Features { get; }

        /// <summary>Selected by the user, or a seed body of a selected body pattern.</summary>
        public bool Selected { get; }
    }

    /// <summary>
    /// Selected bodies (and the seed bodies of a selected body pattern) bring along every
    /// feature that made them – also one that changed other bodies as well: a cut through two
    /// bodies, a cut that split one block into two. Judged per body of the original:
    ///  - <see cref="OtherBodiesChanged"/>: a feature changed a body the copy does not have,
    ///    because a feature of that body before it is not rebuilt. What it did in the original
    ///    is no reference for the copy.
    ///  - <see cref="UnselectedBodies"/>: bodies outside the selection made of rebuilt
    ///    features. Deleting the original would change or remove them.
    /// A body touched by a feature the user selected counts as selected (the instances of a
    /// selected body pattern).
    /// </summary>
    public sealed class BodyScope
    {
        private BodyScope(Dictionary<string, List<string>> otherBodiesChanged, Dictionary<string, List<string>> unselectedBodies)
        {
            OtherBodiesChanged = otherBodiesChanged;
            UnselectedBodies = unselectedBodies;
        }

        /// <summary>Whole part or plain feature selection: nothing reaches beyond what is rebuilt.</summary>
        public static BodyScope None { get; } = Of(Enumerable.Empty<BodyHistory>(), Enumerable.Empty<string>(), Enumerable.Empty<string>());

        /// <summary>Rebuilt feature → bodies of the original it changed that the copy does not get.</summary>
        public IReadOnlyDictionary<string, List<string>> OtherBodiesChanged { get; }

        /// <summary>Body outside the selection → the rebuilt features it is made of, in creation order.</summary>
        public IReadOnlyDictionary<string, List<string>> UnselectedBodies { get; }

        /// <param name="rebuilt">Features collected for the rebuild.</param>
        /// <param name="chosen">Features the user selected.</param>
        public static BodyScope Of(IEnumerable<BodyHistory> bodies, IEnumerable<string> rebuilt, IEnumerable<string> chosen)
        {
            var rebuiltSet = new HashSet<string>(rebuilt, StringComparer.Ordinal);
            var chosenSet = new HashSet<string>(chosen, StringComparer.Ordinal);
            var other = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var unselected = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (BodyHistory b in bodies)
            {
                // From the first feature that is not rebuilt on, this body has no counterpart in the copy.
                bool missing = false;
                foreach (string f in b.Features)
                {
                    if (!rebuiltSet.Contains(f)) missing = true;
                    else if (missing) Add(other, f, b.Name);
                }

                if (b.Selected || b.Features.Any(chosenSet.Contains)) continue;
                List<string> made = b.Features.Where(rebuiltSet.Contains).ToList();
                if (made.Count > 0) unselected[b.Name] = made;
            }
            return new BodyScope(other, unselected);
        }

        private static void Add(Dictionary<string, List<string>> map, string key, string value)
        {
            if (!map.TryGetValue(key, out List<string> list)) map[key] = list = new List<string>();
            if (!list.Contains(value)) list.Add(value);
        }
    }
}
