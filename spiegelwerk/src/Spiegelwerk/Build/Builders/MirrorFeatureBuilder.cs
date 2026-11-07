using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Verification;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// „Features/Flächen spiegeln“ (MirrorPattern) and „Körper spiegeln“ (MirrorSolid) of the
    /// original, rebuilt without the mirror API. In the target the first half – the copies of
    /// the seeds – is already built at R_Π. The second half the target needs is
    /// R_Π(R_P(x)) = R_P′(R_Π(x)) with P′ = R_Π(P): the first half reflected once more, at P′.
    /// So the copies are read back from the target like an original (FeatureCapture in copy
    /// mode) and built again in a child context whose reflection is R_P′. Every rule about
    /// orientation (σ, arc sense, pattern directions, sweep twist, fill angles …) carries over,
    /// because R_P′ is a reflection again; the result is a set of ordinary features.
    ///
    /// The references of the second half come from the original, not from the target: each
    /// step is checked against what its first half was checked against, mirrored once more
    /// (<see cref="BuildShared.Built"/>). Step by step only if the seeds do not interact; the
    /// mirror feature as a whole is always checked against the original mirror feature.
    /// „Körper spiegeln“ builds a body of its own, checked to be one per seed body at the
    /// right place, and with „verschmelzen“ combines it with its first half.
    /// </summary>
    internal sealed class MirrorFeatureBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is MirrorFeatureRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var m = (MirrorFeatureRecord)record;
            var notes = new List<string>();
            // The target holds no mirror feature: a second half never contains one (nested mirrors are flattened through groups).
            if (ctx.IsNested) return Fail("Ein Spiegel-Feature innerhalb der zweiten Hälfte eines anderen wird nicht nachgebaut.", notes);
            ModelDoc2 doc = ctx.Doc;
            HashSet<string> snapshot = StepRunner.TopLevelNames(doc);

            // ---- 1. the first half: the copies of the seeds in the target
            var seedCopies = new List<Feature>();
            var missing = new List<string>();
            foreach (string seed in m.SeedFeatures)
            {
                List<Feature> copies = ctx.Counterparts(seed, solidOnly: false);
                if (copies.Count == 0) missing.Add(seed);
                foreach (Feature c in copies)
                    if (seedCopies.All(x => x.Name != c.Name)) seedCopies.Add(c);
            }
            if (!seedCopies.Any(ctx.ChangesSolid))
                return Fail($"Keines der Saat-Features ({Quoted(m.SeedFeatures)}) wurde in der ersten Hälfte gespiegelt; die zweite Hälfte entsteht nicht.", notes);

            Reflection secondHalf = MirrorFeatureRules.SecondHalf(ctx.Mirror, m.Plane);
            notes.Add($"Spiegelebene der zweiten Hälfte: {ReportText.Plane(secondHalf.Plane)}" + (m.PlaneIsFace ? " (im Original eine Fläche)." : "."));
            if (m.SeedFaces.Count > 0) notes.Add(MirrorFeatureRules.FacesAsFeatures);
            if (m.UnrequestedSeedBodies.Count > 0) notes.Add(MirrorFeatureRules.UnrequestedNote(m.UnrequestedSeedBodies));

            // ---- 2. read it back
            ctx.Shared.Progress?.Show(RunSteps.Build, RunSteps.SecondHalf(m.Name));
            if (doc.SketchManager.ActiveSketch != null)
                throw new InvalidOperationException("Vor dem Einlesen der ersten Hälfte war noch eine Skizze geöffnet.");
            SwSelect.Clear(doc);
            var skipped = new List<(Feature feature, string reason)>();
            List<Feature> toRead = FeatureCollector.CollectCopies(doc, seedCopies, ctx.IsCounterpart, skipped);
            List<FeatureRecord> records = ReadFirstHalf(ctx, toRead, m.Name);
            if (records.OfType<MirrorFeatureRecord>().Any())
                throw new InvalidOperationException("In der ersten Hälfte steht ein Spiegel-Feature; Spiegelwerk legt keine an.");
            // Faces and edges do not survive the rollbacks of reading.
            ctx.Topology.Invalidate();

            // ---- 3. the child context: R_P′, its own registrations, the references of the first half
            bool bodies = m.Kind == MirrorSeedKind.Bodies;
            BuildContext child = BuildContext.Nested(ctx, secondHalf, bodies || ctx.SeparateBody,
                bodies ? new HashSet<string>(StringComparer.Ordinal) : ctx.OwnNames, m.Name);
            foreach (PartOfFeatureRecord part in records.OfType<PartOfFeatureRecord>()) child.PartOwners[part.Name] = part.Owner;
            foreach (SketchPatternRecord pattern in records.OfType<SketchPatternRecord>()) child.PointSketches.Add(pattern.SketchName);
            child.NoteSupportFaces(records);
            foreach (FeatureRecord r in records)
                MirrorFeatureRules.Patch(r, ctx.Shared.Built.TryGetValue(r.Name, out BuiltFeature first) ? first : null, secondHalf.Plane);
            child.Check = ChooseCheck(ctx, m, records, secondHalf);
            foreach (var (feature, reason) in skipped)
                child.Report.Entries.Add(new ReportEntry(feature.Name, feature.GetTypeName2(), ReportStatus.Skipped, reason));

            // ---- 4. the second half, step by step
            bool interrupted = BuildSteps(ctx, child, m, records, notes);

            var members = new List<(Feature feature, bool solid)>();
            foreach (FeatureRecord r in records)
                if (child.Created.TryGetValue(r.Name, out Feature f) && members.All(x => x.feature.Name != f.Name))
                    members.Add((f, r.ChangesSolid));

            // ---- 5. „Körper spiegeln“: one new body per seed body, at its place; merged if wanted
            bool problems = interrupted;
            if (bodies && members.Any(x => x.solid))
            {
                List<MassState> expected = MirrorFeatureRules.ExpectedNewBodies(m.SeedBodies, m.Plane, ctx.Mirror);
                List<MassState> made = child.OwnBodies().Select(b => SwDoc.MassOf(new[] { b })).ToList();
                if (!MirrorFeatureRules.NewBodiesMatch(expected, made))
                {
                    problems = true;
                    notes.Add($"Die zweite Hälfte ergibt {made.Count} Körper, erwartet {expected.Count} an der gespiegelten Stelle der Saat-Körper" +
                              (m.Merge ? "; nicht verschmolzen." : "."));
                }
                else if (m.Merge)
                {
                    problems |= !MergeBodies(ctx, child, m, expected, members, notes);
                }
            }

            // ---- 6. report: the mirror feature, then its steps under it
            var solid = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (FeatureRecord r in records) solid[r.Name] = r.ChangesSolid;
            foreach (var (feature, _) in skipped) solid[feature.Name] = ctx.ChangesSolid(feature);
            List<ReportEntry> nested = MirrorReport.Nested(m.Name, child.Report.Entries, name => Step(ctx, child, name));
            var steps = child.Report.Entries.Select((e, i) => (nested[i], solid.TryGetValue(e.Feature, out bool s) && s)).ToList();
            foreach (string seed in missing)
            {
                var entry = new ReportEntry($"{m.Name} › {seed}", TypeOf(ctx, seed), ReportStatus.Skipped, MirrorFeatureRules.MissingSeed);
                nested.Insert(0, entry);
                steps.Insert(0, (entry, true));
            }

            ReportStatus status = members.Count == 0 ? ReportStatus.Failed : MirrorFeatureRules.Aggregate(steps, ctx.Options.StopOnError, problems);

            if (status == ReportStatus.Failed && !members.Any(x => x.solid && IsMirrored(child, x.feature)))
            {
                // No solid step came of it: nothing of the second half stays, and none of its steps counts as built.
                var failed = BuildResult.Failed(MirrorFeatureRules.NotBuilt(m));
                failed.Notes.AddRange(notes);
                StepRunner.RemoveLeftovers(ctx, snapshot, failed);
                foreach (ReportEntry e in nested.Where(e => e.IsMirrored))
                {
                    e.Status = ReportStatus.Skipped;
                    e.HasEquivalent = false;
                    e.Details.Add($"Mit der zweiten Hälfte von „{m.Name}“ wieder entfernt.");
                }
                failed.ChildEntries.AddRange(nested);
                return failed;
            }

            string message = MirrorFeatureRules.Message(m, nested.Count(e => e.IsMirrored), nested.Count);
            BuildResult result = BuildResult.OfGroup(members.Select(x => x.feature).ToList(), status, message);
            result.Notes.AddRange(notes);
            result.ChildEntries.AddRange(nested);
            if (m.GeometryPattern)
                result.MismatchHint = "Im Original ist „Geometriemuster“ gesetzt: SOLIDWORKS kopiert dort nur die Geometrie, die Kopie löst Endbedingungen neu.";
            if (status != ReportStatus.Failed) ctx.RegisterGroup(m.Name, members);
            return result;
        }

        /// <summary>
        /// The copies of the first half as records, read like an original but without the
        /// notes on feature scope the job itself set. Reading may move the rollback bar (sketches
        /// with model references, closed shells, advanced holes); if that failed with the feature
        /// tree switched off, everything is read once more with the tree on. The bar must be at
        /// the end again, or every later feature would land mid-tree: then the run stops.
        /// </summary>
        private static List<FeatureRecord> ReadFirstHalf(BuildContext ctx, List<Feature> copies, string mirrorName)
        {
            ModelDoc2 doc = ctx.Doc;
            var capture = new FeatureCapture(doc, ctx.Math, ctx.RegionSpaces, ofOwnCopy: true);
            List<FeatureRecord> records = capture.CaptureAll(copies)!;
            if (capture.RollbackFailures > 0)
            {
                FeatureManager fm = doc.FeatureManager;
                bool tree = fm.EnableFeatureTree;
                try
                {
                    fm.EnableFeatureTree = true;
                    if (SwDoc.RollToEnd(doc)) records = new FeatureCapture(doc, ctx.Math, ctx.RegionSpaces, ofOwnCopy: true).CaptureAll(copies)!;
                }
                finally
                {
                    fm.EnableFeatureTree = tree;
                }
            }
            if (!SwDoc.RollToEnd(doc))
                throw new AbortRunException($"Die Rückrollleiste ließ sich nach dem Einlesen der ersten Hälfte von „{mirrorName}“ nicht wieder ans Ende setzen; " +
                                            "weitere Features landeten mitten im Feature-Baum. Bitte die Leiste ans Ende setzen und das Ergebnis prüfen.");
            return records;
        }

        /// <summary>
        /// Step by step only if the seeds do not interact: then the second-half steps add up to
        /// the mirror feature's change, and each step on its own is a valid reference.
        /// </summary>
        private static StepCheck ChooseCheck(BuildContext ctx, MirrorFeatureRecord m, List<FeatureRecord> records, Reflection secondHalf)
        {
            if (!ctx.Options.VerifySteps) return StepCheck.Off;
            if (m.ExpectedChange == null || m.OtherBodies.Count > 0) return StepCheck.ReportOnly;
            List<MassState?> seeds = records.Where(r => r.ChangesSolid).Select(r => r.ExpectedChange).ToList();
            bool independent = MirrorFeatureRules.SeedsIndependent(m.ExpectedChange.Value.Mirror(ctx.Mirror), seeds, secondHalf, SwDoc.MassOf(ctx.Doc));
            return MirrorFeatureRules.ChooseCheck(true, independent);
        }

        /// <returns>True if the second half stopped before its last step (stop on error, Esc, abort).</returns>
        private static bool BuildSteps(BuildContext ctx, BuildContext child, MirrorFeatureRecord m, List<FeatureRecord> records, List<string> notes)
        {
            RunProgress? progress = ctx.Shared.Progress;
            for (int i = 0; i < records.Count; i++)
            {
                FeatureRecord r = records[i];
                string logical = ctx.Logical(r.Name);
                progress?.Show(RunSteps.Build, RunSteps.SecondHalf(m.Name, logical, i + 1, records.Count));
                ReportEntry entry = StepRunner.BuildOne(child, r);
                bool last = i == records.Count - 1;
                if (ctx.Shared.AbortReason != null) return !last;
                if (entry.Status == ReportStatus.Failed && ctx.Options.StopOnError)
                {
                    if (!last) notes.Add($"Beim ersten Fehler abgebrochen: {records.Count - i - 1} weitere Schritte der zweiten Hälfte wurden nicht mehr aufgebaut.");
                    return !last;
                }
                if (!last && progress != null && progress.CancelWithinStep())
                {
                    ctx.Shared.Cancelled = true;
                    notes.Add(MirrorFeatureRules.CancelledIn(logical, records.Count - i - 1));
                    // Parts already reported as built with an owner that now is not built.
                    foreach (string owner in RunSteps.OwnersNotBuiltAfter(records, i)) StepRunner.WithdrawParts(child, owner);
                    return true;
                }
            }
            return false;
        }

        /// <summary>What the report knows of a second-half step, by the name of the first-half copy it was read from.</summary>
        private static SecondHalfStep? Step(BuildContext ctx, BuildContext child, string name)
        {
            if (!ctx.Shared.Built.TryGetValue(name, out BuiltFeature first)) return null;
            string? target = child.Created.TryGetValue(name, out Feature f) ? f.Name : null;
            return new SecondHalfStep(first.Logical, target, first.Status);
        }

        private static bool IsMirrored(BuildContext child, Feature f) =>
            child.Report.Entries.Any(e => e.IsMirrored && child.Created.TryGetValue(e.Feature, out Feature c) && c.Name == f.Name);

        /// <summary>Type of a seed of the original, as its own report entry names it.</summary>
        private static string TypeOf(BuildContext ctx, string seed) => ctx.Report.Entries.FirstOrDefault(e => e.Feature == seed)?.Type ?? "";

        // ---------------------------------------------------------------- merging („Körper spiegeln“)

        /// <summary>
        /// Combines each new body with the first-half copy of its seed body (Combine, add) – the
        /// way „verschmelzen“ joins them in the original. The new body is built apart first, so
        /// no step of the second half can reach into the first half; only the Combine joins them.
        /// </summary>
        /// <returns>False if a body could not be merged (it stays a body of its own, with a note).</returns>
        private static bool MergeBodies(BuildContext ctx, BuildContext child, MirrorFeatureRecord m, List<MassState> expected, List<(Feature feature, bool solid)> members, List<string> notes)
        {
            bool all = true;
            for (int i = 0; i < m.SeedBodies.Count; i++)
            {
                MassState firstMass = m.SeedBodies[i].Mass.Mirror(ctx.Mirror), secondMass = expected[i];
                // Bodies are found anew for every attempt: a rebuild may replace the objects.
                (Body2?, Body2?) Find() =>
                    (FindBody(ctx.Doc, firstMass, b => ctx.IsOwnBody(b) && !child.IsOwnBody(b), ctx.SamePart),
                     FindBody(ctx.Doc, secondMass, child.IsOwnBody, true));
                Feature? combine = Combine(ctx, Find);
                if (combine == null)
                {
                    notes.Add($"Körper „{m.SeedBodies[i].Name}“: Die Hälften ließen sich nicht verschmelzen (Kombinieren, hinzufügen) und bleiben getrennte Körper.");
                    all = false;
                    continue;
                }
                string name = SwDoc.Rename(ctx.Doc, combine, ctx.NameFor(m.Name));
                ctx.RegisterHelper(combine);
                child.OwnNames.Add(name);
                ctx.Shared.Built[name] = new BuiltFeature(m.Name) { ChangesSolid = true };
                members.Add((combine, true));
            }
            return all;
        }

        /// <summary>
        /// Combine „add“ of two bodies: InsertCombineFeature with the bodies as tools (main body
        /// null, as the API help says for adding), then with the bodies selected. Kept only if it
        /// is free of errors and leaves one body less.
        /// </summary>
        private static Feature? Combine(BuildContext ctx, Func<(Body2? first, Body2? second)> find)
        {
            ModelDoc2 doc = ctx.Doc;
            FeatureManager fm = doc.FeatureManager;
            const int add = (int)swBodyOperationType_e.SWBODYADD;
            var attempts = new Func<Body2, Body2, Feature?>[]
            {
                (a, b) => fm.InsertCombineFeature(add, null, new object[] { a, b }),
                (a, b) => Select(doc, a, 2, b, 2) ? fm.InsertCombineFeature(add, null, null) : null,
                (a, b) => Select(doc, a, 1, b, 2) ? fm.InsertCombineFeature(add, null, null) : null,
            };
            foreach (Func<Body2, Body2, Feature?> attempt in attempts)
            {
                var (first, second) = find();
                if (first == null || second == null) return null;
                int before = SwDoc.SolidBodies(doc).Count;
                Feature? f;
                try
                {
                    f = attempt(first, second);
                }
                catch (Exception)
                {
                    f = null;
                }
                SwSelect.Clear(doc);
                ctx.Topology.Invalidate();
                if (f == null) continue;
                bool isWarning = false;
                if (f.GetErrorCode2(out isWarning) == 0 || isWarning)
                    if (SwDoc.SolidBodies(doc).Count == before - 1) return f;
                Delete(ctx, f);
            }
            return null;
        }

        private static bool Select(ModelDoc2 doc, Body2 a, int markA, Body2 b, int markB)
        {
            SwSelect.Clear(doc);
            return SwSelect.Select(doc, a, markA, false) && SwSelect.Select(doc, b, markB, true);
        }

        /// <summary>The body with the given volume and centroid among those <paramref name="allowed"/> admits (with <paramref name="strict"/> only those).</summary>
        private static Body2? FindBody(ModelDoc2 doc, MassState wanted, Func<Body2, bool> allowed, bool strict)
        {
            Body2? fallback = null;
            foreach (Body2 b in SwDoc.SolidBodies(doc))
            {
                if (!MassComparison.Compare(wanted, SwDoc.MassOf(new[] { b })).Passes) continue;
                if (allowed(b)) return b;
                if (!strict) fallback ??= b;
            }
            return fallback;
        }

        private static string Quoted(IEnumerable<string> names) => string.Join(", ", names.Select(n => $"„{n}“"));
    }
}
