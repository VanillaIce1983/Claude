using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Core.Sketch
{
    /// <summary>Which regions of the mirrored sketch stand for the selected regions of the original.</summary>
    public sealed class RegionMatch
    {
        /// <summary>A selection was found (exact, or with a note); otherwise <see cref="Reason"/> says why not.</summary>
        public bool Success { get; internal set; }

        /// <summary>Indices into the target regions, each once, in the order of the source regions.</summary>
        public List<int> Indices { get; } = new List<int>();

        /// <summary>
        /// One point inside each selected target region (same order as <see cref="Indices"/>, target
        /// sketch coordinates): what the created feature has to show it uses (read back after creation).
        /// </summary>
        public List<Vec3> Witnesses { get; } = new List<Vec3>();

        /// <summary>
        /// Report notes for a feature built with this selection: a region split or merged in the
        /// copy, a region that differs – each with what is selected instead.
        /// </summary>
        public List<string> Notes { get; } = new List<string>();

        /// <summary>
        /// Why the selection is not the original's (the regions behind <see cref="Inexact"/>), as
        /// sentences without the consequence: for a feature that is refused instead of built,
        /// where "gewählt ist …" would not hold.
        /// </summary>
        public List<string> Deviations { get; } = new List<string>();

        public string? Reason { get; internal set; }

        /// <summary>
        /// The selection is not geometrically the original's: a containing, larger region. The
        /// fallback policy decides whether that may be built (never in the same part without a
        /// body of its own). A cover or a merge is exact – the union is the original region.
        /// </summary>
        public bool Inexact { get; internal set; }
    }

    /// <summary>
    /// Matches the selected regions of the original, mirrored into the target sketch, to the
    /// regions of the mirrored sketch. Identity is decided geometrically, not by area: a
    /// region is the same if every vertex of either boundary lies within 2ε + 10·Tol.Length of
    /// the other (two-sided). Per source region:
    ///  1. hits: the target regions in which one of its witnesses lies (boundary witnesses
    ///     are ignored as long as one is inside); none: no match;
    ///  2. exact: one hit, and the same boundary;
    ///  3. merge: several sources hit the same target, which is exactly their union (the
    ///     separating element is missing in the copy) – selected once, with a note;
    ///  4. cover (not for thin features, where a split would add a wall): the source is
    ///     exactly the union of target regions lying inside it (split by geometry the
    ///     original lacks) – all selected, with a note;
    ///  5. otherwise a mismatch: one hit that contains the source is selected as a larger
    ///     region (inexact, both areas in the note) – containing means no hole, island or
    ///     notch of it lies inside the source, a slit may; anything else is no match.
    /// Area only backs up the union tests; a 60 % part of a sliver can never pass as "equal".
    /// </summary>
    public static class RegionMatcher
    {
        public static RegionMatch Match(IReadOnlyList<ProfileRegion> sources, IReadOnlyList<ProfileRegion> targets, bool allowCover)
        {
            var match = new RegionMatch();
            if (sources.Count == 0)
            {
                match.Reason = "Keine Skizzenbereiche zu übertragen";
                return match;
            }
            if (targets.Count == 0)
            {
                match.Reason = "Die gespiegelte Skizze hat keine lesbaren Bereiche";
                return match;
            }
            double eps = sources.Concat(targets).Max(r => r.ChordTolerance);
            double tolH = RegionGeometry.IdentityTolerance(eps);

            var hits = new List<List<int>>();
            for (int i = 0; i < sources.Count; i++)
            {
                ProfileRegion s = sources[i];
                List<int> h = Enumerable.Range(0, targets.Count).Where(t => InsideWitness(s, targets[t]).HasValue).ToList();
                if (h.Count == 0)
                {
                    match.Reason = $"{Label(i, sources.Count)} wurde in der gespiegelten Skizze nicht gefunden";
                    return match;
                }
                hits.Add(h);
            }

            // Selected target indices per source; null: not resolved yet.
            var chosen = new List<int>?[sources.Count];
            for (int i = 0; i < sources.Count; i++)
                if (hits[i].Count == 1 && Same(sources[i], targets[hits[i][0]], tolH))
                    chosen[i] = new List<int> { hits[i][0] };

            // Merge: several unresolved sources in one target that is exactly their union.
            foreach (var group in Enumerable.Range(0, sources.Count).Where(i => chosen[i] == null && hits[i].Count == 1)
                         .GroupBy(i => hits[i][0]).Where(g => g.Count() >= 2))
            {
                List<int> members = group.ToList();
                if (!IsUnion(targets[group.Key], members.Select(i => sources[i]).ToList(), tolH)) continue;
                foreach (int i in members) chosen[i] = new List<int> { group.Key };
                match.Notes.Add($"Die Skizzenbereiche {string.Join(", ", members.Select(i => i + 1))} sind in der Kopie ein einziger Bereich " +
                                "(das trennende Element fehlt dort); er wird einmal gewählt.");
            }

            for (int i = 0; i < sources.Count; i++)
            {
                if (chosen[i] != null) continue;
                ProfileRegion s = sources[i];
                string label = Label(i, sources.Count);
                if (allowCover)
                {
                    List<int> inside = Enumerable.Range(0, targets.Count)
                        .Where(t => targets[t].Witnesses.Count > 0 && targets[t].Witnesses.All(w => s.Classify(w.Point) == RegionPoint.Inside)).ToList();
                    if (inside.Count >= 2 && hits[i].All(inside.Contains) && IsUnion(s, inside.Select(t => targets[t]).ToList(), tolH))
                    {
                        chosen[i] = inside;
                        match.Notes.Add($"{label} ist in der Kopie durch zusätzliche Geometrie in {inside.Count} Bereiche geteilt; alle werden gewählt.");
                        continue;
                    }
                }
                if (hits[i].Count == 1 && Contains(targets[hits[i][0]], s, tolH))
                {
                    ProfileRegion t = targets[hits[i][0]];
                    chosen[i] = new List<int> { hits[i][0] };
                    match.Inexact = true;
                    string deviation = $"{label} weicht in der Kopie ab ({ReportText.SquareMillimetres(s.Area)} im Original, " +
                                       $"{ReportText.SquareMillimetres(t.Area)} in der Kopie)";
                    match.Deviations.Add(deviation + ".");
                    match.Notes.Add(deviation + "; gewählt ist der Bereich, in dem er liegt.");
                    continue;
                }
                match.Reason = hits[i].Count == 1
                    ? $"{label} ist in der Kopie kleiner oder anders geformt ({ReportText.SquareMillimetres(s.Area)} im Original, " +
                      $"{ReportText.SquareMillimetres(targets[hits[i][0]].Area)} in der Kopie)"
                    : $"{label} liegt in der Kopie in {hits[i].Count} Bereichen, die ihn nicht genau zusammensetzen" +
                      (allowCover ? "" : " (bei dünnwandigen Features wird ein geteilter Bereich nicht zusammengesetzt: das ergäbe eine zusätzliche Wand)");
                match.Notes.Clear();
                match.Deviations.Clear();
                match.Inexact = false;
                return match;
            }

            for (int i = 0; i < sources.Count; i++)
                foreach (int t in chosen[i]!)
                {
                    if (match.Indices.Contains(t)) continue;
                    match.Indices.Add(t);
                    // The target's own best witness; a sliver without one: the source witness inside it.
                    match.Witnesses.Add(targets[t].Witnesses.Count > 0 ? targets[t].Witnesses[0].Point : InsideWitness(sources[i], targets[t])!.Value);
                }
            match.Success = true;
            return match;
        }

        /// <summary>Two boundaries are the same: every vertex of each within <paramref name="tolH"/> of the other.</summary>
        public static bool Same(ProfileRegion a, ProfileRegion b, double tolH) =>
            RegionGeometry.MaxDistance(a.Vertices, b.Segments) <= tolH && RegionGeometry.MaxDistance(b.Vertices, a.Segments) <= tolH;

        /// <summary>
        /// <paramref name="whole"/> covers <paramref name="part"/>: every vertex of the part lies
        /// inside the whole or within <paramref name="tolH"/> of its boundary, and no boundary of
        /// the whole runs through the part's interior – except a slit, which has the whole on both
        /// sides. A hole, island or notch of the whole inside the part makes the whole smaller there.
        /// </summary>
        public static bool Contains(ProfileRegion whole, ProfileRegion part, double tolH) =>
            part.Vertices.All(v => whole.Classify(v) == RegionPoint.Inside || RegionGeometry.Distance(whole.Segments, v) <= tolH)
            && !whole.Segments.Any(s => Cuts(whole, part, s, tolH));

        /// <summary>
        /// A boundary segment of <paramref name="whole"/> inside <paramref name="part"/> (its
        /// midpoint more than 2·tolH from the part's boundary) without the whole on both sides:
        /// a hole, island or notch the part lacks. The sides are probed 2·tolH off the midpoint,
        /// beyond the classification margin; anything but inside on either side counts.
        /// </summary>
        private static bool Cuts(ProfileRegion whole, ProfileRegion part, RegionSegment s, double tolH)
        {
            double length = s.Length;
            if (length <= 0) return false;
            Vec3 mid = (s.A + s.B) * 0.5;
            if (part.Classify(mid) != RegionPoint.Inside || RegionGeometry.Distance(part.Segments, mid) <= 2 * tolH) return false;
            // Regions lie in sketch coordinates (Z = 0): the in-plane normal of the segment.
            Vec3 n = new Vec3(s.A.Y - s.B.Y, s.B.X - s.A.X, 0) * (2 * tolH / length);
            return whole.Classify(mid + n) != RegionPoint.Inside || whole.Classify(mid - n) != RegionPoint.Inside;
        }

        /// <summary>
        /// <paramref name="whole"/> is exactly the union of <paramref name="parts"/>: the parts'
        /// witnesses lie inside it, their vertices inside it or on its boundary, and the
        /// boundary of the union (the parts' segments without those shared by two parts) and
        /// the boundary of the whole lie within <paramref name="tolH"/> of each other; the
        /// areas back it up.
        /// </summary>
        public static bool IsUnion(ProfileRegion whole, IReadOnlyList<ProfileRegion> parts, double tolH)
        {
            if (parts.Count < 2) return parts.Count == 1 && Same(whole, parts[0], tolH);
            if (parts.Any(p => p.Witnesses.Count == 0 || p.Witnesses.Any(w => whole.Classify(w.Point) != RegionPoint.Inside))) return false;
            if (parts.Any(p => !Contains(whole, p, tolH))) return false;

            var boundary = new List<RegionSegment>();
            for (int j = 0; j < parts.Count; j++)
                foreach (RegionSegment s in parts[j].Segments)
                {
                    Vec3 mid = (s.A + s.B) * 0.5;
                    bool shared = false;
                    for (int k = 0; k < parts.Count && !shared; k++)
                        shared = k != j && RegionGeometry.Distance(parts[k].Segments, mid) <= tolH;
                    if (!shared) boundary.Add(s);
                }
            if (boundary.Count == 0) return false;
            if (RegionGeometry.MaxDistance(whole.Vertices, boundary) > tolH) return false;
            if (RegionGeometry.MaxDistance(RegionGeometry.Vertices(boundary), whole.Segments) > tolH) return false;

            bool kernel = whole.KernelArea.HasValue && parts.All(p => p.KernelArea.HasValue);
            double perimeters = whole.Perimeter + parts.Sum(p => p.Perimeter);
            double eps = Math.Max(whole.ChordTolerance, parts.Max(p => p.ChordTolerance));
            double tolA = Math.Max(1e-7 * whole.Area, 2 * (kernel ? Tol.Length : eps) * perimeters);
            double wholeArea = kernel ? whole.Area : whole.PolylineArea;
            double partsArea = kernel ? parts.Sum(p => p.Area) : parts.Sum(p => p.PolylineArea);
            return Math.Abs(partsArea - wholeArea) <= tolA;
        }

        /// <summary>
        /// Read-back of a created feature: did it take exactly the selected regions? Every
        /// expected witness (<see cref="RegionMatch.Witnesses"/>) must lie inside exactly one of
        /// the regions the feature reports, and every reported region must hold exactly one of
        /// them. A feature that silently ignored the pre-selection (whole sketch: no regions)
        /// or took other regions fails. Returns why not, or null.
        /// </summary>
        public static string? CheckUsed(IReadOnlyList<Vec3> expected, IReadOnlyList<ProfileRegion> used)
        {
            if (used.Count != expected.Count)
                return $"das Feature verwendet {used.Count} statt {expected.Count} Skizzenbereich{(expected.Count == 1 ? "" : "e")}";
            var holders = new int[used.Count];
            foreach (Vec3 w in expected)
            {
                List<int> inside = Enumerable.Range(0, used.Count).Where(u => used[u].Classify(w) == RegionPoint.Inside).ToList();
                if (inside.Count != 1) return inside.Count == 0 ? "ein gewählter Skizzenbereich fehlt im Feature" : "die Bereiche des Features überlappen sich";
                holders[inside[0]]++;
            }
            return holders.All(h => h == 1) ? null : "das Feature verwendet andere Skizzenbereiche als gewählt";
        }

        /// <summary>A witness of <paramref name="source"/> inside <paramref name="target"/>, if any.</summary>
        private static Vec3? InsideWitness(ProfileRegion source, ProfileRegion target)
        {
            foreach (RegionWitness w in source.Witnesses)
                if (target.Classify(w.Point) == RegionPoint.Inside)
                    return w.Point;
            return null;
        }

        private static string Label(int index, int count) => count == 1 ? "Der ausgewählte Skizzenbereich" : $"Skizzenbereich {index + 1}";
    }
}
