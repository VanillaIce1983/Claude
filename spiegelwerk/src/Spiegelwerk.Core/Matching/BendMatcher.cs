using System;
using System.Collections.Generic;
using Spiegelwerk.Core.Geometry;

namespace Spiegelwerk.Core.Matching
{
    /// <summary>
    /// One-to-one pairing of two small sets by a cost (blech-plan S5): which read-back faces are the
    /// wanted ones, which bend of the target has the faces of a mirrored bend, which corner of a
    /// corner relief is which. A reflection may reverse the order in which SOLIDWORKS lists things, so
    /// the sets are compared as sets: every wanted item needs a distinct partner whose cost is finite.
    /// </summary>
    public static class SetMatch
    {
        /// <summary>
        /// A pairing wanted i → got <c>result[i]</c> with every cost finite and the smallest total, or
        /// null if there is none. With <paramref name="sameCount"/> both sets must be equally large (no
        /// extra item on either side). Exhaustive with pruning: the sets here are a handful of faces,
        /// edges or corners.
        /// </summary>
        public static int[]? Pair(int wanted, int got, Func<int, int, double> cost, bool sameCount = true)
        {
            if (wanted < 0 || got < 0) throw new ArgumentOutOfRangeException(nameof(wanted));
            if (sameCount && wanted != got) return null;
            if (wanted > got) return null;
            var c = new double[wanted, got];
            for (int i = 0; i < wanted; i++)
                for (int j = 0; j < got; j++)
                    c[i, j] = cost(i, j);
            int[]? best = null;
            double bestTotal = double.PositiveInfinity;
            var current = new int[wanted];
            var used = new bool[got];
            void Search(int i, double total)
            {
                if (total >= bestTotal) return;
                if (i == wanted)
                {
                    bestTotal = total;
                    best = (int[])current.Clone();
                    return;
                }
                for (int j = 0; j < got; j++)
                {
                    if (used[j] || double.IsPositiveInfinity(c[i, j]) || double.IsNaN(c[i, j])) continue;
                    used[j] = true;
                    current[i] = j;
                    Search(i + 1, total + c[i, j]);
                    used[j] = false;
                }
            }
            Search(0, 0);
            return best;
        }

        /// <summary>The total cost of a pairing (<see cref="Pair"/>).</summary>
        public static double Total(int[] pairs, Func<int, int, double> cost)
        {
            double total = 0;
            for (int i = 0; i < pairs.Length; i++) total += cost(i, pairs[i]);
            return total;
        }
    }

    /// <summary>Faces compared as sets (closed corner, fold, bend, corner relief).</summary>
    public static class FaceSets
    {
        /// <summary>
        /// The cost of taking candidate face <paramref name="got"/> for <paramref name="wanted"/>: the
        /// distance of the wanted sample to the candidate (by the caller, usually the kernel) plus the
        /// area difference, if the candidate could be it (<see cref="FaceSignature.CouldBe"/>) and the
        /// sample lies on it within <paramref name="tol"/>; infinite otherwise.
        /// </summary>
        public static double Cost(FaceSignature wanted, FaceSignature got, double distance, double tol = Tol.Length) =>
            wanted.CouldBe(got) && distance <= tol ? distance + Math.Abs(wanted.Area - got.Area) : double.PositiveInfinity;

        /// <summary>
        /// Pairs every wanted face with a distinct candidate (<see cref="Cost"/>); null if one has none or
        /// – with <paramref name="sameCount"/> – the counts differ. <paramref name="distance"/>(k, p): distance
        /// of point p to candidate k.
        /// </summary>
        public static int[]? Pair(IReadOnlyList<FaceSignature> wanted, IReadOnlyList<FaceSignature> candidates, Func<int, Vec3, double> distance,
            bool sameCount = true, double tol = Tol.Length) =>
            SetMatch.Pair(wanted.Count, candidates.Count, (i, k) => Cost(wanted[i], candidates[k], distance(k, wanted[i].Sample), tol), sameCount);

        /// <summary>The same faces, as sets: each wanted one has its own candidate and nothing is left over.</summary>
        public static bool Same(IReadOnlyList<FaceSignature> wanted, IReadOnlyList<FaceSignature> candidates, Func<int, Vec3, double> distance, double tol = Tol.Length) =>
            Pair(wanted, candidates, distance, true, tol) != null;
    }

    /// <summary>
    /// Finds the bend of the target that a mirrored bend reference names (A3 P8, blech-plan S5): a
    /// bend sub-feature whose faces are, as a set, the mirrored faces of the original's – a full match
    /// only, a partial one is no bend. <paramref name="allowed"/> excludes candidates outright (separate
    /// body: the original's bends); among the rest the preferred (own bodies) win, then the smaller
    /// total. More than one full match makes the result ambiguous.
    /// </summary>
    public static class BendMatcher
    {
        /// <param name="wanted">The bend's faces, mirrored into the target.</param>
        /// <param name="candidates">The faces of each bend sub-feature of the target.</param>
        /// <param name="distance">(candidate c, its face k, point p) → distance of p to that face.</param>
        public static MatchResult FindBend(IReadOnlyList<FaceSignature> wanted, IReadOnlyList<IReadOnlyList<FaceSignature>> candidates,
            Func<int, int, Vec3, double> distance, Func<int, bool>? preferred = null, Func<int, bool>? allowed = null, double tol = Tol.Length)
        {
            int best = -1, count = 0;
            double bestScore = double.PositiveInfinity;
            bool bestPreferred = false;
            if (wanted.Count == 0) return new MatchResult(-1, 0, bestScore);
            for (int c = 0; c < candidates.Count; c++)
            {
                if (allowed != null && !allowed(c)) continue;
                int index = c;
                Func<int, int, double> cost = (i, k) => FaceSets.Cost(wanted[i], candidates[index][k], distance(index, k, wanted[i].Sample), tol);
                int[]? pairs = SetMatch.Pair(wanted.Count, candidates[c].Count, cost);
                if (pairs == null) continue;
                count++;
                double score = SetMatch.Total(pairs, cost);
                bool pref = preferred != null && preferred(c);
                if (best < 0 || (pref && !bestPreferred) || (pref == bestPreferred && score < bestScore))
                {
                    best = c;
                    bestScore = score;
                    bestPreferred = pref;
                }
            }
            return new MatchResult(best, count, bestScore);
        }
    }
}
