using System;
using System.Collections.Generic;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>
    /// The order in which the features of the original are rebuilt: every feature after the
    /// features it depends on. The feature tree alone does not give that – a loft absorbs its
    /// profile sketches and shows them under itself, while a guide-curve sketch drawn later
    /// (with relations to the profiles) stays at the top level above the loft. Walking the
    /// tree would rebuild the guide curve before the profiles it refers to.
    ///
    /// Depth-first: before a feature come its absorbed sub-features, before those the parents
    /// they depend on, wherever in the tree these sit. Tree order decides among independent
    /// features, so an untouched tree comes out unchanged.
    /// </summary>
    public static class CreationOrder
    {
        /// <param name="topLevel">Top-level features in tree order.</param>
        /// <param name="absorbed">Sub-features that belong to a feature and were created before it (absorbed sketches).</param>
        /// <param name="parents">Features a feature depends on.</param>
        /// <param name="key">Unique name of a feature.</param>
        public static List<T> Sort<T>(IEnumerable<T> topLevel, Func<T, IEnumerable<T>> absorbed, Func<T, IEnumerable<T>> parents, Func<T, string> key)
        {
            var result = new List<T>();
            var done = new HashSet<string>(StringComparer.Ordinal);
            var visiting = new HashSet<string>(StringComparer.Ordinal);

            void Visit(T f)
            {
                string k = key(f);
                // A cycle cannot occur in a valid history; the guard only keeps a damaged one from recursing forever.
                if (done.Contains(k) || !visiting.Add(k)) return;
                foreach (T p in parents(f)) Visit(p);
                foreach (T sub in absorbed(f)) Visit(sub);
                visiting.Remove(k);
                if (done.Add(k)) result.Add(f);
            }

            foreach (T f in topLevel) Visit(f);
            return result;
        }
    }
}
