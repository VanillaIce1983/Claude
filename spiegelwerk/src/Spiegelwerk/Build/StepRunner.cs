using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Build.Builders;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Verification;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Build
{
    /// <summary>
    /// The job cannot go on (the rollback bar of the target stuck mid-tree): the step fails,
    /// what it made is removed, and the run stops after it (<see cref="BuildShared.AbortReason"/>).
    /// </summary>
    internal sealed class AbortRunException : Exception
    {
        public AbortRunException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// One step of the build: a record rebuilt by its builder, checked, guarded, named and
    /// reported – for the job itself and, in a child context, for each step of the second half
    /// of a mirror feature.
    /// </summary>
    internal static class StepRunner
    {
        private static readonly IFeatureBuilder[] Builders =
        {
            new SketchFeatureBuilder(),
            new PartOfBuilder(),
            new RefPlaneBuilder(),
            new AxisBuilder(),
            new CoordSysBuilder(),
            new ExtrudeBuilder(),
            new RevolveBuilder(),
            new SweepBuilder(),
            new LoftBuilder(),
            new FilletBuilder(),
            new VariableFilletBuilder(),
            new FaceFilletBuilder(),
            new ChamferBuilder(),
            new ShellBuilder(),
            new DraftBuilder(),
            new HoleBuilder(),
            new AdvancedHoleBuilder(),
            new PatternBuilder(),
            new CurvePatternBuilder(),
            new SketchPatternBuilder(),
            new TablePatternBuilder(),
            new FillPatternBuilder(),
            new VariablePatternBuilder(),
            new MirrorFeatureBuilder(),
        };

        /// <summary>
        /// Builds one record and adds its report entry – for a mirror feature followed by the
        /// entries of its second half.
        /// </summary>
        public static ReportEntry BuildOne(BuildContext ctx, FeatureRecord record)
        {
            if (record is UnsupportedRecord u)
            {
                var status = u.ChangesSolid ? ReportStatus.Failed : ReportStatus.Skipped;
                ReportEntry skipped = ctx.Report.Add(record, status, u.Reason);
                if (u.ChangesSolid) skipped.Details.Add("Folgende Features, die sich auf dieses beziehen, können ebenfalls scheitern.");
                return skipped;
            }

            IFeatureBuilder? builder = Builders.FirstOrDefault(b => b.Handles(record));
            if (builder == null) return ctx.Report.Add(record, ReportStatus.Skipped, "Kein Builder für diesen Typ.");

            MassState before = ctx.Options.VerifySteps && record.ChangesSolid ? SwDoc.MassOf(ctx.Doc) : MassState.Empty;
            bool guardOriginal = ctx.SeparateBody && record.ChangesSolid;
            MassState originalBefore = guardOriginal ? SwDoc.MassOf(ctx.OriginalBodies()) : MassState.Empty;
            HashSet<string> featuresBefore = TopLevelNames(ctx.Doc);
            BuildResult result;
            string verification = "";
            int mark = ctx.Planes.BeginStep(ctx.IsNested);
            try
            {
                result = builder.Build(record, ctx);
            }
            catch (Exception ex)
            {
                result = BuildResult.Failed(ex.Message);
                if (ex is AbortRunException) ctx.Shared.AbortReason = ex.Message;
                // Never leave a half-built sketch open.
                if (ctx.Doc.SketchManager.ActiveSketch != null) ctx.Doc.SketchManager.InsertSketch(true);
                SwSelect.Clear(ctx.Doc);
                RemoveLeftovers(ctx, featuresBefore, result);
            }
            finally
            {
                // Also after a failure: a feature may have been created (or removed) before it.
                ctx.Topology.Invalidate();
            }
            // No feature came of it (refused or failed after resolving its references): helper planes and axes made for it go again.
            if (result.Feature == null) ctx.Planes.RemoveStepHelpers(mark);

            try
            {
                verification = StepVerifier.Verify(ctx, record, result, before);
            }
            catch (Exception ex)
            {
                if (ctx.Check != StepCheck.ReportOnly)
                {
                    result.Status = ReportStatus.Warning;
                    result.Notes.Add($"Prüfung nicht möglich: {ex.Message}");
                }
                ctx.Topology.Invalidate();
            }
            // What capture already knew could not be carried over (feature scope, setbacks, regions
            // ...) makes the copy a warning, not "OK" – also for "delete original".
            if (result.Success && result.Status == ReportStatus.Ok && record.Notes.Count > 0) result.Status = ReportStatus.Warning;

            if (guardOriginal && result.Success && !OriginalUntouched(ctx, originalBefore))
            {
                result = RemoveAgain(ctx, record, result, featuresBefore, mark);
                verification = "";
            }
            // Only now: a variant tried above may have replaced dimensions.
            if (result.Success && record.OwnDimensions.Count > 0) RestoreDimensionNames(ctx, record, result);
            CheckRebuildError(result);

            ReportEntry entry = ctx.Report.Add(record, result.Status, result.Message);
            entry.Verification = verification;
            entry.HasEquivalent = result.HasEquivalent;
            entry.Details.AddRange(result.Notes);
            ctx.Report.Entries.AddRange(result.ChildEntries);
            // Only if the owner is not in the target – one with a rebuild error still has its parts.
            if (record.BringsParts && !entry.IsMirrored && !ctx.Created.ContainsKey(record.Name)) WithdrawParts(ctx, record.Name);
            Remember(ctx, record, result, entry);
            return entry;
        }

        /// <summary>
        /// What a mirror feature that reads this copy back as its first half needs to know: the
        /// reference this step was checked against, in target coordinates – the second half is
        /// checked against the same numbers, mirrored once more –, the merge flag of the
        /// original and the status.
        /// </summary>
        private static void Remember(BuildContext ctx, FeatureRecord record, BuildResult result, ReportEntry entry)
        {
            if (result.Group != null || result.Feature == null) return;
            if (!ctx.Shared.Built.TryGetValue(result.Feature.Name, out BuiltFeature built)) return;
            built.ChangesSolid = record.ChangesSolid;
            built.Expected = record.ExpectedChange?.Mirror(ctx.Mirror);
            built.Samples.Clear();
            built.Samples.AddRange(record.FaceSamples.Select(ctx.Mirror.Apply));
            built.Merge = MirrorFeatureRules.MergeOf(record);
            built.Status = entry.Status;
        }

        public static HashSet<string> TopLevelNames(ModelDoc2 doc) =>
            new HashSet<string>(SwDoc.TopLevelFeatures(doc).Select(f => f.Name), StringComparer.Ordinal);

        private static string Quoted(IEnumerable<string> names) => string.Join(", ", names.Select(n => $"\"{n}\""));

        /// <summary>
        /// The feature's own dimensions (depth, radius, spacing ...) get their names from the
        /// original (DimensionMatcher) – via temporary names, so two of them can swap theirs.
        /// Each that ends up with its original name counts as known for copied equations.
        /// </summary>
        private static void RestoreDimensionNames(BuildContext ctx, FeatureRecord record, BuildResult result)
        {
            try
            {
                List<DisplayDimension> own = SwDoc.OwnDimensions(result.Feature!);
                List<Dimension> dims = own.Select(dd => dd.GetDimension2(0)).ToList();
                List<string> before = dims.Select(d => d.Name).ToList();
                DimensionMatch match = DimensionMatcher.Match(record.OwnDimensions, own.Select(SwDoc.Named).ToList());
                for (int k = 0; k < match.Renames.Count; k++) SwDoc.TryRename(dims[match.Renames[k].copy], $"Spiegelwerk{k + 1}");
                foreach (var (copy, name) in match.Renames)
                    if (!SwDoc.TryRename(dims[copy], name)) SwDoc.TryRename(dims[copy], before[copy]);

                var unmatched = new List<string>();
                var unnamed = new List<string>();
                for (int o = 0; o < record.OwnDimensions.Count; o++)
                {
                    string name = record.OwnDimensions[o].Name;
                    int c = match.Counterparts[o];
                    if (c < 0) unmatched.Add(name);
                    else if (dims[c].Name != name) unnamed.Add(name);
                    else ctx.KnownDimensions.Add($"{name}@{record.Name}");
                }
                string equations = ctx.CopiesEquations ? "; Gleichungen darauf werden nicht übernommen" : "";
                if (unmatched.Count > 0)
                    result.Notes.Add($"Bemaßungen ohne eindeutiges Gegenstück in der Kopie, ihr Name bleibt nicht erhalten: {Quoted(unmatched)}{equations}.");
                if (unnamed.Count > 0)
                    result.Notes.Add($"Originalname ließ sich in der Kopie nicht setzen: {Quoted(unnamed)}{equations}.");
            }
            catch (Exception ex)
            {
                result.Notes.Add($"Bemaßungsnamen nicht wiederhergestellt ({ex.Message})" + (ctx.CopiesEquations ? "; Gleichungen auf dieses Feature werden nicht übernommen." : "."));
            }
        }

        /// <summary>
        /// The parts of a feature (the sketches of a hole) were reported as "built with it" before
        /// it was built. If it failed, they were not.
        /// </summary>
        public static void WithdrawParts(BuildContext ctx, string owner)
        {
            foreach (ReportEntry e in ctx.Report.Entries)
                if (ctx.PartOwners.TryGetValue(e.Feature, out string o) && o == owner && e.HasEquivalent)
                {
                    e.HasEquivalent = false;
                    e.Message = $"Teil von „{owner}“; nicht entstanden, weil „{owner}“ nicht gespiegelt wurde.";
                }
        }

        /// <summary>
        /// Same part, separate body: the original bodies must come out of every step unchanged.
        /// Patterns have no feature scope before newer API versions, and a cut acts on all bodies until its scope is set;
        /// a feature that changed the original anyway is removed again.
        /// </summary>
        private static bool OriginalUntouched(BuildContext ctx, MassState before)
        {
            MassState after = SwDoc.MassOf(ctx.OriginalBodies());
            return MassComparison.Compare(MassState.Empty, after - before, before).Passes;
        }

        /// <summary>
        /// A builder may throw after SOLIDWORKS created the feature or its helpers. Unregistered,
        /// such a feature would count neither as mirrored nor as the job's own body, and a cut or
        /// pattern without feature scope may already have changed the original: what the step
        /// added is deleted again. Deleting keeps absorbed sketches – those of earlier steps were
        /// at the top level before or sit under an earlier feature too (a shared sketch) and stay,
        /// one made in this step comes up in the second round. Also removes the second half of a
        /// mirror feature as a whole: everything since its step began.
        /// </summary>
        public static void RemoveLeftovers(BuildContext ctx, HashSet<string> before, BuildResult result)
        {
            var removed = new List<string>();
            try
            {
                // A shared sketch of an earlier step is also a sub-feature of the new feature; it
                // stays in the model under the earlier one and must stay registered.
                var shared = new HashSet<string>(SwDoc.TopLevelFeatures(ctx.Doc).Where(t => before.Contains(t.Name))
                    .SelectMany(SwDoc.SubFeatures).Select(s => s.Name), StringComparer.Ordinal);
                for (int round = 0; round < 2; round++)
                {
                    List<Feature> made = NewFeatures(ctx.Doc, before);
                    if (made.Count == 0) break;
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (Feature f in made)
                    {
                        names.Add(f.Name);
                        if (!removed.Contains(f.Name)) removed.Add(f.Name);
                        foreach (Feature sub in SwDoc.SubFeatures(f).Where(s => !before.Contains(s.Name) && !shared.Contains(s.Name))) names.Add(sub.Name);
                    }
                    ctx.Forget(names);
                    SwSelect.Clear(ctx.Doc);
                    for (int i = made.Count - 1; i >= 0; i--) SwSelect.Select(ctx.Doc, made[i], 0, true);
                    ctx.Doc.Extension.DeleteSelection2(0);
                    SwSelect.Clear(ctx.Doc);
                }
                List<string> left = NewFeatures(ctx.Doc, before).Select(f => f.Name).ToList();
                removed.RemoveAll(left.Contains);
                if (removed.Count > 0) result.Notes.Add($"Bis zum Fehler angelegt und wieder entfernt: {Quoted(removed)}.");
                if (left.Count > 0) result.Notes.Add($"Bis zum Fehler angelegt und nicht wieder entfernt: {Quoted(left)} – bitte prüfen und von Hand löschen.");
            }
            catch (Exception ex)
            {
                result.Notes.Add($"Aufräumen nach dem Fehler nicht möglich ({ex.Message}) – bitte das Modell prüfen.");
            }
        }

        /// <summary>Top-level features not there before the step (system folders SOLIDWORKS adds on its own aside).</summary>
        private static List<Feature> NewFeatures(ModelDoc2 doc, HashSet<string> before) =>
            SwDoc.TopLevelFeatures(doc).Where(f => !before.Contains(f.Name) && !SwDoc.IsSystemFeature(f)).ToList();

        private static BuildResult RemoveAgain(BuildContext ctx, FeatureRecord record, BuildResult result, HashSet<string> before, int mark)
        {
            var failed = BuildResult.Failed("Das Feature hätte die Originalkörper verändert und wurde wieder entfernt.");
            failed.Notes.AddRange(result.Notes);
            if (result.Group != null)
            {
                // The second half of a mirror feature: everything its step made goes, helpers of its steps included.
                RemoveLeftovers(ctx, before, failed);
                ctx.Unregister(record.Name);
                ctx.Planes.RemoveStepHelpers(mark);
                return failed;
            }
            // Sketches that belong to the feature alone (a hole's) go with it; a sketch it merely
            // uses is a record of its own, may be shared, and stays.
            BuilderSupport.Delete(ctx, result.Feature!, record.DeleteWithAbsorbed);
            foreach (string sketch in record.OwnedSketches) ctx.ForgetSketch(sketch);
            result.RemoveHelpers?.Invoke();
            ctx.Planes.RemoveStepHelpers(mark);
            ctx.Unregister(record.Name);
            return failed;
        }

        /// <summary>
        /// A feature that SOLIDWORKS created but shows with a rebuild error is not a success. In
        /// the second half of a mirror feature the step of the feature in error says so itself;
        /// the mirror feature gets a note.
        /// </summary>
        private static void CheckRebuildError(BuildResult result)
        {
            if (result.Feature == null || !result.Success) return;
            if (result.Group != null)
            {
                var inError = new List<string>();
                foreach (Feature f in result.Group.Where(g => !SwDoc.IsSketch(g)))
                {
                    bool warning = false;
                    if (f.GetErrorCode2(out warning) != 0 && !warning) inError.Add(f.Name);
                }
                if (inError.Count == 0) return;
                result.Notes.Add($"SOLIDWORKS meldet einen Neuaufbaufehler bei {Quoted(inError)}.");
                if (result.Status == ReportStatus.Ok) result.Status = ReportStatus.Warning;
                return;
            }
            bool isWarning = false;
            int code = result.Feature.GetErrorCode2(out isWarning);
            if (code == 0) return;
            if (isWarning)
            {
                result.Notes.Add($"SOLIDWORKS meldet eine Warnung beim Neuaufbau (Code {code}).");
                if (result.Status == ReportStatus.Ok) result.Status = ReportStatus.Warning;
            }
            else
            {
                result.Status = ReportStatus.Failed;
                result.Message += $" – Neuaufbaufehler (Code {code}).";
            }
        }
    }
}
