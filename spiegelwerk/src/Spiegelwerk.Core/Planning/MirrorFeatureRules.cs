using System;
using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Verification;

namespace Spiegelwerk.Core.Planning
{
    /// <summary>How the steps of a build are checked against the mirrored original.</summary>
    public enum StepCheck
    {
        /// <summary>Every step checked, the best variant kept (the run itself).</summary>
        Full,

        /// <summary>
        /// Second half whose seeds do not interact: checked step by step, but a variant is only
        /// kept if it passes – a better but still wrong one must not win.
        /// </summary>
        PassOnly,

        /// <summary>
        /// Second half whose seeds interact with the first half (or a reference is missing):
        /// the check is shown, but it neither tries variants nor changes a status; the mirror
        /// feature as a whole is checked.
        /// </summary>
        ReportOnly,

        /// <summary>„Jeden Schritt prüfen“ is off.</summary>
        Off,
    }

    /// <summary>
    /// Mirror features of the original („Features/Flächen spiegeln“, „Körper spiegeln“),
    /// rebuilt without the mirror API. For any isometry g, g ∘ R_P ∘ g⁻¹ = R_g(P); so the
    /// second half the target needs, R_Π(R_P(x)), is R_P′(R_Π(x)) with P′ = R_Π(P): the first
    /// half, already built in the target, reflected once more at P′. A reflection again, so
    /// every rule of the builders about orientation (σ, arc sense, pattern direction, sweep
    /// twist, fill angles …) carries over unchanged.
    /// </summary>
    public static class MirrorFeatureRules
    {
        /// <summary>The reflection that builds the second half in the target: R_P′ with P′ = R_Π(P).</summary>
        public static Reflection SecondHalf(Reflection run, Plane3 p) => new Reflection(run.Apply(p));

        /// <summary>Name of a second-half feature: „Boss-Extrude1 (Spiegeln1)“; nested mirrors give „X (M1) (M2)“.</summary>
        public static string SecondHalfName(string seedLogical, string mirrorName) => $"{seedLogical} ({mirrorName})";

        /// <summary>
        /// The seeds do not interact: the changes of the single second-half steps, each the
        /// first-half change mirrored at P′, add up to the change of the whole mirror feature.
        /// Only then is a step on its own a valid reference. Not so for a seed that straddles P
        /// (its halves merge), faces on P that merge, a cut through both halves … False if any
        /// value is missing.
        /// </summary>
        /// <param name="whole">The mirror feature's change, in target coordinates.</param>
        /// <param name="seeds">The first-half change of each solid seed, in target coordinates.</param>
        /// <param name="total">The whole target, for the noise floor.</param>
        public static bool SeedsIndependent(MassState? whole, IReadOnlyList<MassState?> seeds, Reflection secondHalf, MassState total)
        {
            if (whole == null || seeds.Count == 0 || seeds.Any(s => s == null)) return false;
            MassState sum = MassState.Empty;
            foreach (MassState? s in seeds) sum += s!.Value.Mirror(secondHalf);
            return MassComparison.Compare(whole.Value, sum, total).Passes;
        }

        /// <summary>
        /// Face samples of a first-half step that do not lie on P′. A face on P′ stays where it
        /// is when mirrored there, and the halves merge across it: the sample would find no face.
        /// </summary>
        public static List<Vec3> OffPlane(IEnumerable<Vec3> samples, Plane3 plane) => samples.Where(p => !plane.Contains(p)).ToList();

        /// <summary>„Körper spiegeln“: the bodies the second half has to make – each seed body at R_Π(R_P(·)).</summary>
        public static List<MassState> ExpectedNewBodies(IEnumerable<MirrorSeedBody> seeds, Plane3 p, Reflection run)
        {
            var atP = new Reflection(p);
            return seeds.Select(s => s.Mass.Mirror(atP).Mirror(run)).ToList();
        }

        /// <summary>
        /// The second half made exactly one body per seed body, each where it belongs: same
        /// number, and every expected body matched by a body of its own.
        /// </summary>
        public static bool NewBodiesMatch(IReadOnlyList<MassState> expected, IReadOnlyList<MassState> made)
        {
            if (expected.Count != made.Count) return false;
            var free = made.ToList();
            foreach (MassState e in expected)
            {
                int i = free.FindIndex(m => MassComparison.Compare(e, m).Passes);
                if (i < 0) return false;
                free.RemoveAt(i);
            }
            return true;
        }

        /// <summary>
        /// The selected faces of a feature are all its faces: only then is mirroring them the
        /// same as mirroring the feature. One face of a cut that made three holes, a face of the
        /// base extrude – rebuilt as the whole feature, that would be more than was mirrored.
        /// Both lists are read in the same state, so the same face gives the same fingerprint.
        /// </summary>
        public static bool SameFaces(IReadOnlyList<FaceSignature> ownerFaces, IReadOnlyList<FaceSignature> selected)
        {
            if (ownerFaces.Count != selected.Count) return false;
            var free = ownerFaces.ToList();
            foreach (FaceSignature s in selected)
            {
                int i = free.FindIndex(o => s.CouldBe(o) && o.Sample.AlmostEquals(s.Sample));
                if (i < 0) return false;
                free.RemoveAt(i);
            }
            return true;
        }

        /// <summary>
        /// „Körper spiegeln“ of a body that an earlier „Körper spiegeln“ without merging made: its
        /// body history may also list the features of that one's seed body. They are the
        /// earlier mirror's seeds, whose copy the earlier mirror's second half already is;
        /// rebuilding them again would make the body twice. Dropped from that body, also through
        /// several levels; the earlier mirror stays as a seed. Records in creation order.
        /// </summary>
        public static void FlattenSeeds(IEnumerable<FeatureRecord> records)
        {
            // Mirror feature without merging → every feature its result is a copy of.
            var covered = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (MirrorFeatureRecord m in records.OfType<MirrorFeatureRecord>())
            {
                if (m.Kind == MirrorSeedKind.Bodies)
                {
                    foreach (MirrorSeedBody body in m.SeedBodies)
                        foreach (string earlier in body.Features.Where(covered.ContainsKey).ToList())
                            body.Features.RemoveAll(f => covered[earlier].Contains(f));
                    m.SeedFeaturesFromBodies();
                }
                if (m.Kind != MirrorSeedKind.Bodies || m.Merge) continue;
                var set = new HashSet<string>(m.SeedFeatures, StringComparer.Ordinal);
                foreach (string seed in m.SeedFeatures)
                    if (covered.TryGetValue(seed, out HashSet<string> deeper)) set.UnionWith(deeper);
                covered[m.Name] = set;
            }
        }

        /// <summary>
        /// „verschmelzen“ of „Körper spiegeln“ exists only with a planar face as the mirror plane.
        /// With a reference plane the stored flag keeps its default (true) while SOLIDWORKS leaves
        /// the bodies apart: it counts only with a face.
        /// </summary>
        public static bool MergeApplies(bool merge, bool planeIsFace) => merge && planeIsFace;

        /// <summary>Merge flag of a boss-type record (extrude, revolve, sweep, loft); null for others.</summary>
        public static bool? MergeOf(FeatureRecord r) => r switch
        {
            ExtrudeRecord e => e.Merge,
            RevolveRecord v => v.Merge,
            SweepRecord s => s.Merge,
            LoftRecord l => l.Merge,
            _ => null,
        };

        /// <summary>
        /// A first-half copy built as a body of its own was created without merging; its second
        /// half has to merge as the original did. Only the four record types that have the flag.
        /// </summary>
        public static bool SetMerge(FeatureRecord r, bool merge)
        {
            switch (r)
            {
                case ExtrudeRecord e:
                    e.Merge = merge;
                    return true;
                case RevolveRecord v:
                    v.Merge = merge;
                    return true;
                case SweepRecord s:
                    s.Merge = merge;
                    return true;
                case LoftRecord l:
                    l.Merge = merge;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// A record read back from a first-half copy gets the reference its first half was
        /// checked against (target coordinates, mirrored once more by the second-half step),
        /// without the samples on P′, and the merge flag of the original.
        /// </summary>
        public static void Patch(FeatureRecord copy, BuiltFeature? first, Plane3 secondHalfPlane)
        {
            copy.ExpectedChange = first?.Expected;
            copy.FaceSamples.Clear();
            if (first == null) return;
            copy.FaceSamples.AddRange(OffPlane(first.Samples, secondHalfPlane));
            if (first.Merge is bool merge) SetMerge(copy, merge);
        }

        /// <summary>
        /// Status of a second-half step: its own, but not better than its first half – what had
        /// to be checked there was copied along. A first half without a counterpart of its own
        /// (a hole's sketch) counts as fine.
        /// </summary>
        public static ReportStatus WithFirstHalf(ReportStatus second, ReportStatus first)
        {
            if (second != ReportStatus.Ok && second != ReportStatus.Warning) return second;
            if (first == ReportStatus.Ok || first == ReportStatus.Skipped) return second;
            return Rank(first) > Rank(second) ? first : second;
        }

        private static int Rank(ReportStatus s) => s switch
        {
            ReportStatus.Ok => 0,
            ReportStatus.Warning => 1,
            _ => 2,
        };

        /// <summary>
        /// Status of the mirror feature from its second-half steps: Fehler if no solid step has a
        /// counterpart, or – with „beim ersten Fehler abbrechen“ – if a step failed; Hinweis if
        /// any step is not mirrored or not fine, or something else went wrong
        /// (<paramref name="problems"/>: body check, merging); otherwise OK.
        /// </summary>
        public static ReportStatus Aggregate(IEnumerable<(ReportEntry entry, bool changesSolid)> children, bool stopOnError, bool problems)
        {
            var list = children.ToList();
            bool solid = list.Any(c => c.changesSolid && (c.entry.Status == ReportStatus.Ok || c.entry.Status == ReportStatus.Warning));
            if (!solid) return ReportStatus.Failed;
            if (stopOnError && list.Any(c => c.entry.Status == ReportStatus.Failed)) return ReportStatus.Failed;
            bool allFine = list.All(c => c.entry.Status == ReportStatus.Ok || (c.entry.Status == ReportStatus.Skipped && c.entry.HasEquivalent));
            return problems || !allFine ? ReportStatus.Warning : ReportStatus.Ok;
        }

        /// <summary>
        /// The check of a second-half step whose seeds interact with the first half: shown, but it
        /// decides nothing – the whole mirror feature is checked as one.
        /// </summary>
        public static string InformationOnly(string outcome, string mirrorName) =>
            $"{outcome} (nur zur Information: die Hälften beeinflussen sich, maßgeblich ist die Prüfung von „{mirrorName}“)";

        /// <summary>
        /// How the steps of a second half are checked: step by step only if the seeds are
        /// independent (<see cref="SeedsIndependent"/>), else only for information.
        /// </summary>
        public static StepCheck ChooseCheck(bool verifySteps, bool independent) =>
            !verifySteps ? StepCheck.Off : independent ? StepCheck.PassOnly : StepCheck.ReportOnly;

        /// <summary>
        /// „Körper spiegeln“ reached only through a body it made (bodies selected, the mirror
        /// feature itself not): its seed bodies come along as copies of their own although the
        /// user did not select them. Judged as the body scope judges them, in the final state: a
        /// body outside the selection (<see cref="BodyScope.UnselectedBodies"/>) that is made of
        /// all the features of a seed body is what became of it. Names are not compared – the
        /// seed body's name is the one at the mirror feature, and a later feature renames a body
        /// (with „verschmelzen“ the seed body itself becomes the merged one). Final names, for
        /// the report.
        /// </summary>
        /// <param name="unselectedBodies">Bodies outside the selection → the rebuilt features they are made of (BodyScope).</param>
        /// <param name="chosen">The user selected the mirror feature itself: its seed bodies are wanted.</param>
        public static List<string> UnrequestedSeedBodies(MirrorFeatureRecord m, IReadOnlyDictionary<string, List<string>> unselectedBodies, bool chosen)
        {
            if (chosen || m.Kind != MirrorSeedKind.Bodies) return new List<string>();
            return unselectedBodies
                .Where(kv => m.SeedBodies.Any(s => s.Features.Count > 0 && s.Features.All(kv.Value.Contains)))
                .Select(kv => kv.Key).ToList();
        }

        /// <summary>
        /// Report note on seed bodies nobody selected (<see cref="UnrequestedSeedBodies"/>). Whether
        /// the original may be deleted is left to the body scope, which reports it itself.
        /// </summary>
        public static string UnrequestedNote(IEnumerable<string> bodies) =>
            $"Nicht gewählt, aber als Saat mitgespiegelt: {Quoted(bodies)}. Die Kopie bekommt auch diese Körper.";

        /// <summary>
        /// Message of the mirror feature's entry: what it repeats and how many steps of its
        /// second half have a counterpart; the steps themselves follow as entries of their own.
        /// </summary>
        /// <param name="built">Steps of the second half with a counterpart in the target.</param>
        /// <param name="steps">Steps of the second half, sketches included.</param>
        public static string Message(MirrorFeatureRecord m, int built, int steps) =>
            $"{What(m)}: zweite Hälfte als eigene Features nachgebaut, {built} von {steps} Schritten";

        /// <summary>
        /// Message of a mirror feature none of whose second-half steps changed the solid: what
        /// was built of it is removed again, so nothing of the second half is counted.
        /// </summary>
        public static string NotBuilt(MirrorFeatureRecord m) =>
            $"{What(m)}: zweite Hälfte nicht nachgebaut – kein Volumen-Feature entstanden.";

        private static string What(MirrorFeatureRecord m) => m.Kind == MirrorSeedKind.Bodies
            ? $"Körper spiegeln ({(m.SeedBodies.Count == 1 ? "1 Körper" : $"{m.SeedBodies.Count} Körper")}{(m.Merge ? ", verschmolzen" : "")})"
            : "Features spiegeln";

        /// <summary>A seed of the mirror feature has no counterpart in the target: its second half cannot be made.</summary>
        public const string MissingSeed = "In der ersten Hälfte nicht gespiegelt – die zweite Hälfte dazu entsteht nicht.";

        /// <summary>The faces to mirror are rebuilt as the features they belong to (only complete sets are accepted).</summary>
        public const string FacesAsFeatures = "Die zu spiegelnden Flächen wurden als die ganzen Features nachgebaut, zu denen sie gehören.";

        /// <summary>
        /// For the general part of the report: the second half is made of copies with fixed
        /// values, an equation on a seed does not reach it – in a new part it changes the first
        /// half only, in the same part neither.
        /// </summary>
        public const string EquationsNote =
            "Bei Spiegel-Features ist die zweite Hälfte eine eigene Kopie mit festen Werten: Gleichungen, die im Original die Saat treiben, ändern sie nicht mit.";

        /// <summary>Report note for a second half stopped with Esc after <paramref name="step"/>; <paramref name="left"/> steps were not built.</summary>
        public static string CancelledIn(string step, int left) =>
            left <= 0 ? $"Mit Esc abgebrochen nach „{step}“." : $"Mit Esc abgebrochen nach „{step}“; {left} weitere Schritte der zweiten Hälfte wurden nicht mehr aufgebaut.";

        private static string Quoted(IEnumerable<string> names) => string.Join(", ", names.Select(n => $"„{n}“"));
    }
}
