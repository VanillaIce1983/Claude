using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Build.Builders;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Verification;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Build
{
    internal sealed class MirrorRequest
    {
        public MirrorRequest(ModelDoc2 source, Plane3 mirrorPlane, MirrorOptions options)
        {
            Source = source;
            MirrorPlane = mirrorPlane;
            Options = options;
        }

        /// <summary>The part whose features are mirrored.</summary>
        public ModelDoc2 Source { get; }

        /// <summary>Mirror plane in the coordinates of <see cref="Source"/>.</summary>
        public Plane3 MirrorPlane { get; }

        public MirrorOptions Options { get; }
        public List<Feature> Features { get; } = new List<Feature>();
        public List<Body2> Bodies { get; } = new List<Body2>();
    }

    internal sealed class MirrorJobResult
    {
        public MirrorJobResult(MirrorReport report, ModelDoc2? target)
        {
            Report = report;
            Target = target;
        }

        public MirrorReport Report { get; }
        public ModelDoc2? Target { get; }
    }

    /// <summary>
    /// The whole run, in three strictly separated phases:
    ///  1. Capture – read the original into neutral records (and, for checking, what every
    ///     feature did to the volume). After this the original is not used any more.
    ///  2. Build – create every record mirrored in the target with plain FeatureManager /
    ///     SketchManager calls. No Mirror feature, no derived part, no external reference.
    ///  3. Verify – per feature, compare with the mirrored original and pick the right variant.
    /// </summary>
    internal sealed class MirrorJob
    {
        private readonly SldWorks _sw;
        private readonly SwMath _math;

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
        };

        public MirrorJob(SldWorks sw)
        {
            _sw = sw;
            _math = new SwMath(sw);
        }

        public MirrorJobResult Run(MirrorRequest req)
        {
            // The progress bar goes again however the run ends.
            using (var progress = new RunProgress(_sw))
                return Run(req, progress);
        }

        private MirrorJobResult Run(MirrorRequest req, RunProgress progress)
        {
            var report = new MirrorReport();
            ModelDoc2 source = req.Source;
            MirrorOptions opt = req.Options;

            // ---- 1. capture
            // In an open sketch the rollback for the measurement fails, and the first new sketch would close it instead of opening one.
            if (source.SketchManager.ActiveSketch != null)
                throw new InvalidOperationException("Es ist noch eine Skizze zur Bearbeitung geöffnet. Bitte die Skizze schließen und erneut spiegeln.");
            if (SwDoc.TopLevelFeatures(source).Any(f => f.IsRolledBack()))
                throw new InvalidOperationException("Die Rückrollleiste steht nicht am Ende des Feature-Baums. Bitte ans Ende setzen und erneut spiegeln.");

            var skipped = new List<(Feature feature, string reason)>();
            List<Feature> features;
            List<FeatureRecord> records;
            bool seedBodies;
            BodyScope bodyScope;
            // Capture and measurement roll the original back and forth; tree and graphics need not follow every step.
            using (new UpdateFreeze(source))
            {
                features = FeatureCollector.Collect(source, _math, req.Features, req.Bodies, skipped, out seedBodies, out bodyScope);
                foreach (var (feature, reason) in skipped)
                    report.Entries.Add(new ReportEntry(feature.Name, feature.GetTypeName2(), ReportStatus.Skipped, reason));
                if (features.Count == 0)
                {
                    report.General.Add("Es wurden keine spiegelbaren Features gefunden.");
                    report.Aborted = true;
                    return new MirrorJobResult(report, null);
                }
                progress.Start(features.Count, opt.VerifySteps);
                var capture = new FeatureCapture(source, _math);
                records = new List<FeatureRecord>();
                foreach (Feature f in features)
                {
                    progress.Show(RunSteps.Capture, f.Name);
                    records.Add(capture.Capture(f));
                    if (progress.CancelAfterStep()) return CancelledBeforeBuild(report, RunSteps.Capture);
                }
                // Sketches of a hole are rebuilt with the hole – only if the hole itself is rebuilt.
                var holes = new HashSet<string>(records.OfType<PositionedHoleRecord>().Select(h => h.Name), StringComparer.Ordinal);
                for (int i = 0; i < records.Count; i++)
                    if (records[i] is PartOfFeatureRecord part && !holes.Contains(part.Owner))
                        records[i] = capture.CaptureOnItsOwn(features[i], part);
                MarkOtherBodies(records, bodyScope);
                for (int i = 0; i < records.Count; i++) CaptureDimensions(features[i], records[i]);
                if (opt.VerifySteps && !MassCapture.Measure(source, records, progress)) return CancelledBeforeBuild(report, RunSteps.Measure);
            }

            // ---- 2. target
            ModelDoc2 target = opt.Target == MirrorTarget.SamePart ? source : CreatePart(source, opt, report);
            report.TargetName = target.GetTitle();
            // Whole part, selected bodies, or seed bodies of a selected body pattern: a body of its own.
            bool separateBody = opt.Target == MirrorTarget.SamePart && (req.Features.Count == 0 || req.Bodies.Count > 0 || seedBodies);
            var ctx = new BuildContext(_sw, target, _math, new Reflection(req.MirrorPlane), opt, report, separateBody);
            foreach (PartOfFeatureRecord part in records.OfType<PartOfFeatureRecord>()) ctx.PartOwners[part.Name] = part.Owner;
            foreach (SketchPatternRecord pattern in records.OfType<SketchPatternRecord>()) ctx.PointSketches.Add(pattern.SketchName);
            report.General.Add($"Spiegelebene: {ReportText.Plane(req.MirrorPlane)}");
            // New part: only the part's material is copied. Same part: a body of its own gets the part's material.
            if (opt.Target == MirrorTarget.NewPart ? opt.CopyMaterial : separateBody) NoteBodyMaterials(source, req.Bodies, report);

            try
            {
                using (new SwPreferences(_sw))
                using (new UpdateFreeze(target))
                {
                    for (int i = 0; i < records.Count; i++)
                    {
                        progress.Show(RunSteps.Build, records[i].Name);
                        ReportEntry entry = BuildOne(ctx, records[i]);
                        if (entry.Status == ReportStatus.Failed && opt.StopOnError)
                        {
                            report.Aborted = true;
                            break;
                        }
                        // Esc: stops like an error with "stop on error", what is built stays.
                        if (progress.CancelAfterStep())
                        {
                            report.Aborted = report.Cancelled = true;
                            report.General.Add(RunSteps.CancelledInBuild(records[i].Name, records.Count - i - 1));
                            break;
                        }
                    }
                }
                progress.Show(RunSteps.Finish);
                target.EditRebuild3();

                // ---- 3. finish
                if (opt.Target == MirrorTarget.NewPart)
                {
                    if (opt.CopyEquations) CopyEquations(source, ctx, report);
                    target.ShowNamedView2("*Isometric", (int)swStandardViews_e.swIsometricView);
                    target.ViewZoomtofit2();
                }
                else
                {
                    NoteEquationDrivenDimensions(source, ctx, report);
                    if (opt.DeleteOriginal) DeleteOriginals(source, features, bodyScope, report);
                }
            }
            catch (Exception ex)
            {
                // Unexpected API failure: keep what was built and the report, so the user sees where it stopped.
                report.Aborted = true;
                report.General.Add($"Abbruch wegen eines unerwarteten Fehlers: {ex.Message}");
            }
            return new MirrorJobResult(report, target);
        }

        /// <summary>Esc in capture or measurement: nothing is built, no new part is made.</summary>
        private static MirrorJobResult CancelledBeforeBuild(MirrorReport report, string phase)
        {
            report.Aborted = report.Cancelled = true;
            report.General.Add(RunSteps.CancelledBeforeBuild(phase));
            return new MirrorJobResult(report, null);
        }

        private ReportEntry BuildOne(BuildContext ctx, FeatureRecord record)
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
            var featuresBefore = new HashSet<string>(SwDoc.TopLevelFeatures(ctx.Doc).Select(f => f.Name), StringComparer.Ordinal);
            BuildResult result;
            string verification = "";
            ctx.Planes.BeginStep();
            try
            {
                result = builder.Build(record, ctx);
            }
            catch (Exception ex)
            {
                result = BuildResult.Failed(ex.Message);
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
            if (result.Feature == null) ctx.Planes.RemoveStepHelpers();

            try
            {
                verification = StepVerifier.Verify(ctx, record, result, before);
            }
            catch (Exception ex)
            {
                result.Status = ReportStatus.Warning;
                result.Notes.Add($"Prüfung nicht möglich: {ex.Message}");
                ctx.Topology.Invalidate();
            }
            // What capture already knew could not be carried over (feature scope, setbacks, regions
            // ...) makes the copy a warning, not "OK" – also for "delete original".
            if (result.Success && result.Status == ReportStatus.Ok && record.Notes.Count > 0) result.Status = ReportStatus.Warning;

            if (guardOriginal && result.Success && !OriginalUntouched(ctx, originalBefore))
            {
                result = RemoveAgain(ctx, record, result);
                verification = "";
            }
            // Only now: a variant tried above may have replaced dimensions.
            if (result.Success && record.OwnDimensions.Count > 0) RestoreDimensionNames(ctx, record, result);
            CheckRebuildError(result);

            ReportEntry entry = ctx.Report.Add(record, result.Status, result.Message);
            entry.Verification = verification;
            entry.HasEquivalent = result.HasEquivalent;
            entry.Details.AddRange(result.Notes);
            // Only if the hole is not in the target – one with a rebuild error still has its sketches.
            if (!entry.IsMirrored && !ctx.Created.ContainsKey(record.Name)) WithdrawParts(ctx, record.Name);
            return entry;
        }

        /// <summary>
        /// A feature pulled in through a selected body that also changed other bodies: the copy
        /// acts on the mirrored bodies only, so what the original did is no reference for it.
        /// </summary>
        private static void MarkOtherBodies(List<FeatureRecord> records, BodyScope scope)
        {
            foreach (FeatureRecord r in records)
                if (scope.OtherBodiesChanged.TryGetValue(r.Name, out List<string> bodies))
                {
                    r.OtherBodies.AddRange(bodies);
                    r.Notes.Add($"Das Original verändert auch Körper, die nicht mitgespiegelt werden ({Quoted(bodies)}). " +
                                "Die Kopie wirkt nur auf die gespiegelten Körper und wurde deshalb nicht mit dem Original verglichen.");
                }
        }

        private static string Quoted(IEnumerable<string> names) => string.Join(", ", names.Select(n => $"\"{n}\""));

        /// <summary>
        /// The feature's own dimensions of the original, for their names. Not those of sketches
        /// (SketchBuilder names theirs) nor of reference geometry: that is rebuilt on other
        /// references, so its dimensions measure something else. Unreadable: none, and copied
        /// equations then leave this feature's dimensions alone.
        /// </summary>
        private static void CaptureDimensions(Feature feature, FeatureRecord record)
        {
            if (record is SketchRecord || record is PartOfFeatureRecord || record is RefPlaneRecord || record is RefAxisRecord ||
                record is CoordSysRecord || record is UnsupportedRecord)
                return;
            try
            {
                record.OwnDimensions.AddRange(SwDoc.OwnDimensions(feature).Select(SwDoc.Named));
            }
            catch (Exception)
            {
                record.OwnDimensions.Clear();
            }
        }

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
        /// The sketches of a hole were reported as "built with the hole" before the hole was
        /// built. If the hole failed, they were not.
        /// </summary>
        private static void WithdrawParts(BuildContext ctx, string owner)
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
        /// at the top level before and stay, one made in this step comes up in the second round.
        /// </summary>
        private static void RemoveLeftovers(BuildContext ctx, HashSet<string> before, BuildResult result)
        {
            var removed = new List<string>();
            try
            {
                for (int round = 0; round < 2; round++)
                {
                    List<Feature> made = NewFeatures(ctx.Doc, before);
                    if (made.Count == 0) break;
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (Feature f in made)
                    {
                        names.Add(f.Name);
                        if (!removed.Contains(f.Name)) removed.Add(f.Name);
                        foreach (Feature sub in SwDoc.SubFeatures(f).Where(s => !before.Contains(s.Name))) names.Add(sub.Name);
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

        private static BuildResult RemoveAgain(BuildContext ctx, FeatureRecord record, BuildResult result)
        {
            // A hole's sketches belong to the hole alone and go with it.
            BuilderSupport.Delete(ctx, result.Feature!, record is PositionedHoleRecord);
            if (record is PositionedHoleRecord hole) ctx.ForgetSketch(hole.PositionSketchName);
            result.RemoveHelpers?.Invoke();
            ctx.Planes.RemoveStepHelpers();
            ctx.Unregister(record.Name);
            var failed = BuildResult.Failed("Das Feature hätte die Originalkörper verändert und wurde wieder entfernt.");
            failed.Notes.AddRange(result.Notes);
            return failed;
        }

        /// <summary>A feature that SOLIDWORKS created but shows with a rebuild error is not a success.</summary>
        private static void CheckRebuildError(BuildResult result)
        {
            if (result.Feature == null || !result.Success) return;
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

        private ModelDoc2 CreatePart(ModelDoc2 source, MirrorOptions opt, MirrorReport report)
        {
            string template = _sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            if (string.IsNullOrEmpty(template))
                throw new InvalidOperationException("Keine Standardvorlage für Teile eingestellt (Optionen › Standardvorlagen).");
            var target = (ModelDoc2)_sw.NewDocument(template, 0, 0, 0)
                ?? throw new InvalidOperationException($"Neues Teil aus Vorlage \"{template}\" konnte nicht angelegt werden.");

            // Same units as the original, so the copied dimensions read the same and copied
            // equations with plain numbers mean the same (custom unit systems included).
            foreach (swUserPreferenceIntegerValue_e pref in UnitPreferences)
            {
                int value = source.Extension.GetUserPreferenceInteger((int)pref, 0);
                target.Extension.SetUserPreferenceInteger((int)pref, 0, value);
            }

            if (opt.CopyMaterial && source is PartDoc sp && target is PartDoc tp)
            {
                string db = "";
                string material = sp.GetMaterialPropertyName2("", out db);
                if (!string.IsNullOrEmpty(material))
                {
                    tp.SetMaterialPropertyName2("", db, material);
                    report.General.Add($"Werkstoff übernommen: {material}");
                }
            }
            return target;
        }

        /// <summary>
        /// Materials of single bodies (multi-body parts) are not carried over; the report says
        /// which. Selected bodies, else all. Only a note: never stops the run.
        /// </summary>
        private static void NoteBodyMaterials(ModelDoc2 source, List<Body2> selected, MirrorReport report)
        {
            try
            {
                if (!(source is PartDoc part)) return;
                string config = source.GetActiveConfiguration() is Configuration active ? active.Name : "";
                string db = "";
                string partMaterial = part.GetMaterialPropertyName2(config, out db);
                List<Body2> bodies = selected.Count > 0 ? selected : SwDoc.SolidBodies(source);
                var materials = new List<(string, string)>();
                foreach (Body2 b in bodies) materials.Add((b.Name, b.GetMaterialPropertyName(config, out db)));
                if (BodyMaterials.Note(partMaterial, materials) is string note) report.General.Add(note);
            }
            catch (Exception)
            {
                // Materials not readable: no note.
            }
        }

        /// <summary>Unit system first: setting it resets the individual units to its defaults.</summary>
        private static readonly swUserPreferenceIntegerValue_e[] UnitPreferences =
        {
            swUserPreferenceIntegerValue_e.swUnitSystem,
            swUserPreferenceIntegerValue_e.swUnitsLinear,
            swUserPreferenceIntegerValue_e.swUnitsLinearDecimalDisplay,
            swUserPreferenceIntegerValue_e.swUnitsLinearDecimalPlaces,
            swUserPreferenceIntegerValue_e.swUnitsLinearFractionDenominator,
            swUserPreferenceIntegerValue_e.swUnitsAngular,
            swUserPreferenceIntegerValue_e.swUnitsAngularDecimalPlaces,
            swUserPreferenceIntegerValue_e.swUnitsMassPropLength,
            swUserPreferenceIntegerValue_e.swUnitsMassPropMass,
            swUserPreferenceIntegerValue_e.swUnitsMassPropVolume,
            swUserPreferenceIntegerValue_e.swUnitsMassPropDecimalPlaces,
        };

        /// <summary>
        /// Global variables and equations, copied verbatim, with angle unit, solve order and
        /// disabled or suppressed state. They name dimensions ("D1@Skizze1"): an equation is
        /// only copied if each dimension it names is known to be the same one in the copy, else
        /// it would silently drive another. In the original they give the values the copy
        /// already has, so the part must stay as it is – checked by volume and centroid.
        /// </summary>
        private static void CopyEquations(ModelDoc2 source, BuildContext ctx, MirrorReport report)
        {
            EquationMgr src = source.GetEquationMgr(), dst = ctx.Doc.GetEquationMgr();
            int n = src.GetCount(), ok = 0;
            if (n == 0) return;
            if (src.LinkToFile) report.General.Add($"Die Gleichungen des Originals sind mit „{src.FilePath}“ verknüpft; die Kopie übernimmt sie ohne diese Verknüpfung.");
            dst.AngularEquationUnits = src.AngularEquationUnits;
            dst.AutomaticSolveOrder = src.AutomaticSolveOrder;

            MassState before = SwDoc.MassOf(ctx.Doc);
            var failed = new List<string>();
            // Global variables whose equation was not copied: what uses them is not copied either.
            var dropped = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < n; i++)
            {
                string eq = src.get_Equation(i);
                bool disabled = src.get_Disabled(i), suppressed = src.get_Suppression(i);
                string? problem = EquationProblem(source, ctx, eq, dropped);
                // Disabled or suppressed: added unsolved, so it does not set its value first.
                int index = problem == null ? dst.Add2(-1, eq, !disabled && !suppressed) : -1;
                if (index >= 0)
                {
                    ok++;
                    if (disabled) dst.set_Disabled(index, true);
                    if (suppressed) dst.set_Suppression(index, true);
                    continue;
                }
                if (EquationText.Driven(eq) is string driven && EquationText.Dimension(driven) == null) dropped.Add(driven);
                failed.Add(problem == null ? eq : $"{eq} – {problem}");
            }

            string? deviation = null, check = "";
            if (ok > 0)
            {
                ctx.Doc.EditRebuild3();
                MassState after = SwDoc.MassOf(ctx.Doc);
                if (MassComparison.Compare(MassState.Empty, after - before, before).Passes)
                {
                    check = ", Volumen und Schwerpunkt bleiben damit unverändert";
                }
                else
                {
                    double volume = before.Volume > 0 ? (after.Volume - before.Volume) / before.Volume : 0;
                    double shift = before.Volume > 0 && after.Volume > 0 ? (after.Moment / after.Volume - before.Moment / before.Volume).Length : 0;
                    deviation = $"Achtung: Mit den Gleichungen ändert sich die Kopie (Volumen {volume:+0.###%;-0.###%;0%}, Schwerpunkt um {shift * 1000:0.###} mm verschoben). " +
                                "Im Original ergeben sie die Maße, die die Kopie schon hatte; eine von ihnen wirkt hier also anders. Bitte prüfen.";
                }
            }
            report.General.Add($"Gleichungen: {ok} von {n} übernommen{check}.");
            foreach (string eq in failed) report.General.Add($"  nicht übernommen: {eq}");
            if (deviation != null) report.General.Add(deviation);
        }

        /// <summary>Why an equation must not be copied, or null.</summary>
        private static string? EquationProblem(ModelDoc2 source, BuildContext ctx, string equation, HashSet<string> dropped)
        {
            foreach (string name in EquationText.Names(equation))
            {
                if (dropped.Contains(name)) return $"nutzt „{name}“, das nicht übernommen wurde";
                if (!(EquationText.Dimension(name) is (string dim, string owner))) continue;
                if (!ctx.Created.TryGetValue(owner, out Feature copy)) return $"„{owner}“ hat in der Kopie kein Gegenstück unter diesem Namen";
                if (copy.Name != owner) return $"„{owner}“ heißt in der Kopie „{copy.Name}“";
                string full = $"{dim}@{owner}";
                if (!ctx.KnownDimensions.Contains(full)) return $"„{full}“ hat in der Kopie kein gesichertes Gegenstück";
                if (!(source.Parameter(full) is Dimension wanted) || !(ctx.Doc.Parameter(full) is Dimension have) ||
                    !DimensionMatcher.SameValue(wanted.SystemValue, have.SystemValue))
                    return $"„{full}“ hat in der Kopie einen anderen Wert";
            }
            return null;
        }

        /// <summary>
        /// Same part: equations stay with the original. Where they drive a mirrored dimension,
        /// the copy has a fixed value – a later change of a global variable moves only the
        /// original half. Only a note: never stops the run.
        /// </summary>
        private static void NoteEquationDrivenDimensions(ModelDoc2 doc, BuildContext ctx, MirrorReport report)
        {
            try
            {
                EquationMgr eq = doc.GetEquationMgr();
                var driven = new List<string>();
                for (int i = 0; i < eq.GetCount(); i++)
                    if (!eq.get_Disabled(i) && !eq.get_Suppression(i) && EquationText.Driven(eq.get_Equation(i)) is string name &&
                        EquationText.Dimension(name) is (string, string owner) && ctx.Created.ContainsKey(owner))
                        driven.Add(name);
                if (driven.Count == 0) return;
                string more = driven.Count > 5 ? $" und {driven.Count - 5} weitere" : "";
                report.General.Add($"Gleichungen werden im selben Teil nicht übertragen: Im Original treiben sie {Quoted(driven.Take(5))}{more}, " +
                                   "in der Kopie sind diese Maße feste Werte.");
            }
            catch (Exception)
            {
                // Equations not readable: no note.
            }
        }

        /// <summary>
        /// Same part: removes the original features – only if every one of them has a
        /// counterpart (skipped axes, 3D sketches, coordinate systems … would be lost), no body
        /// outside the selection is made of them and nothing else depends on them. Never with
        /// several configurations: the copy carries the values of the active one in all of them.
        /// </summary>
        private static void DeleteOriginals(ModelDoc2 doc, List<Feature> originals, BodyScope scope, MirrorReport report)
        {
            int configurations = doc.GetConfigurationCount();
            if (configurations > 1)
            {
                string active = doc.GetActiveConfiguration() is Configuration config ? config.Name : "";
                report.General.Add($"Original nicht gelöscht: Das Teil hat {configurations} Konfigurationen. Die Kopie übernimmt Maße und Unterdrückung " +
                                   $"nur aus der aktiven (\"{active}\"); was die anderen abweichend festlegen, ginge mit dem Original verloren.");
                return;
            }
            if (!report.AllMirrored)
            {
                var blocking = report.Blocking.Select(e => $"{e.Feature} ({MirrorReport.StatusText(e.Status)})").ToList();
                report.General.Add(blocking.Count > 0
                    ? $"Original nicht gelöscht: ohne fehlerfreies Gegenstück sind {string.Join(", ", blocking)}."
                    : "Original nicht gelöscht, weil der Lauf abgebrochen wurde.");
                return;
            }
            var names = new HashSet<string>(originals.Select(f => f.Name), StringComparer.Ordinal);
            var unselected = scope.UnselectedBodies.Where(kv => kv.Value.Any(names.Contains)).ToList();
            if (unselected.Count > 0)
            {
                foreach (KeyValuePair<string, List<string>> body in unselected)
                    report.General.Add($"Original nicht gelöscht: Der nicht gewählte Körper \"{body.Key}\" entsteht auch aus {Quoted(body.Value.Where(names.Contains))}.");
                return;
            }
            foreach (Feature f in originals)
                if (f.GetChildren() is object[] children)
                    foreach (Feature c in children.OfType<Feature>())
                        if (!names.Contains(c.Name) && !SwDoc.IsSystemFeature(c))
                        {
                            report.General.Add($"Original nicht gelöscht: \"{c.Name}\" hängt von \"{f.Name}\" ab.");
                            return;
                        }

            SwSelect.Clear(doc);
            for (int i = originals.Count - 1; i >= 0; i--) SwSelect.Select(doc, originals[i], 0, true);
            bool deleted = doc.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
            SwSelect.Clear(doc);
            report.General.Add(deleted ? $"{originals.Count} Original-Features gelöscht." : "Original-Features konnten nicht gelöscht werden.");
        }
    }
}
