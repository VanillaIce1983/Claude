using System;
using System.Collections.Generic;

namespace Spiegelwerk.Core.Matching
{
    public readonly struct MatchResult
    {
        public MatchResult(int index, int candidates, double score)
        {
            Index = index;
            Candidates = candidates;
            Score = score;
        }

        /// <summary>Index into the candidate list, -1 if nothing matched.</summary>
        public int Index { get; }

        /// <summary>How many candidates passed; more than one means the best was picked from several.</summary>
        public int Candidates { get; }

        public double Score { get; }

        public bool Found => Index >= 0;
        public bool Ambiguous => Candidates > 1;
    }

    /// <summary>
    /// Finds the edge or face of the rebuilt body that corresponds to a mirrored fingerprint.
    /// Candidates are ranked by geometric distance; ties (e.g. a symmetric part where the
    /// original and the mirror both have an edge on the mirror plane) are resolved by the
    /// caller's preference, typically "belongs to a body this job created".
    /// </summary>
    public static class TopologyMatcher
    {
        public static MatchResult FindEdge(EdgeSignature wanted, IReadOnlyList<EdgeSignature> candidates, Func<int, bool>? preferred = null)
        {
            int best = -1, count = 0;
            double bestScore = double.PositiveInfinity;
            bool bestPreferred = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                double score = wanted.DistanceTo(candidates[i]);
                if (double.IsPositiveInfinity(score)) continue;
                count++;
                bool pref = preferred != null && preferred(i);
                if (best < 0 || (pref && !bestPreferred) || (pref == bestPreferred && score < bestScore))
                {
                    best = i;
                    bestScore = score;
                    bestPreferred = pref;
                }
            }
            return new MatchResult(best, count, bestScore);
        }

        /// <param name="distanceToCandidate">Distance from <see cref="FaceSignature.Sample"/> to candidate i (computed by the kernel).</param>
        public static MatchResult FindFace(
            FaceSignature wanted,
            IReadOnlyList<FaceSignature> candidates,
            Func<int, double> distanceToCandidate,
            double tol = Geometry.Tol.Length,
            Func<int, bool>? preferred = null)
        {
            int best = -1, count = 0;
            double bestScore = double.PositiveInfinity;
            bool bestPreferred = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!wanted.CouldBe(candidates[i])) continue;
                double d = distanceToCandidate(i);
                if (d > tol) continue;
                count++;
                double score = d + Math.Abs(wanted.Area - candidates[i].Area);
                bool pref = preferred != null && preferred(i);
                if (best < 0 || (pref && !bestPreferred) || (pref == bestPreferred && score < bestScore))
                {
                    best = i;
                    bestScore = score;
                    bestPreferred = pref;
                }
            }
            return new MatchResult(best, count, bestScore);
        }
    }
}
