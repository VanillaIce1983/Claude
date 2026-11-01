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
using Spiegelwerk.Core.Sketch;
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
    ///     SketchManager calls (one StepRunner step each). No Mirror feature, no derived part,
    ///     no external reference – a mirror feature of the original becomes ordinary features
    ///     too (MirrorFeatureBuilder).
    ///  3. Verify – per feature, compare with the mirrored original and pick the right variant.
    /// </summary>
    internal sealed class MirrorJob
    {
        private readonly SldWorks _sw;
        private readonly SwMath _math;

        public MirrorJob(SldWorks sw)
        {
            _sw = sw;
            _math = new SwMath(sw);
        }

        public MirrorJobResult Run(MirrorRequest req)
        {
            // The progress bar goes again however the run ends.
            using (var progress = new RunProgress(_sw))
            {
                MirrorJobResult result = Run(req, progress);
                result.Report.StopRequested = progress.EscAtEnd;
                return result;
            }
        }

        private MirrorJobResult Run(MirrorRequest req, RunProgress progress)
        {
            var report = new MirrorReport();
            ModelDoc2 source = req.Source;

            // ---- 1. capture
            // In an open sketch the rollback for the measurement fails, and the first new sketch would close it instead of opening one.
            if (source.SketchManager.ActiveSketch != null)
                throw new InvalidOperationException("Es ist noch eine Skizze zur Bearbeitung geöffnet. Bitte die Skizze schließen und erneut spiegeln.");
            if (SwDoc.TopLevelFeatures(source).Any(f => f.IsRolledBack()))
                throw new InvalidOperationException("Die Rückrollleiste steht nicht am Ende des Feature-Baums. Bitte ans Ende setzen und erneut spiegeln.");

            // Body pointers do not survive the rebuild of folding, nor the rollbacks of capture and measurement; names do.
            List<string> bodyNames = req.Bodies.Select(b => b.Name).ToList();
            // Sheet metal: folded for the whole run, put back however it ends (A1 §3).
            SheetMetalSession? session = SheetMetalSession.For(source);
            if (session?.Fold() is string notFolded) throw new InvalidOperationException(notFolded);
            try
            {
                return Run(req, progress, report, session, bodyNames);
            }
            catch (Exception ex) when (session != null)
            {
                // A refused or failed run returns no report: a flat pattern the original did not get back must still be said.
                List<string> lost = session.Restore();
                if (lost.Count == 0) throw;
                throw new InvalidOperationException(ex.Message + " " + string.Join(" ", lost), ex);
            }
            finally
            {
                // Runs once: after the catch above nothing is left to say here.
                if (session != null) report.General.AddRange(session.Restore());
            }
        }

        /// <summary>
        /// The selected bodies again in the folded part: folding rebuilt it and reading the tree
        /// rolled it back. Found by name; a body whose name differs once folded (a body is named
        /// after the last feature that changed it) stops the run instead of quietly mirroring less.
        /// </summary>
        private static List<Body2> SelectedBodiesFolded(ModelDoc2 source, List<string> names)
        {
            if (names.Count == 0) return new List<Body2>();
            var byName = new Dictionary<string, Body2>(StringComparer.Ordinal);
            foreach (Body2 b in SwDoc.SolidBodies(source)) byName[b.Name] = b;
            List<string> missing = names.Where(n => !byName.ContainsKey(n)).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException($"Gewählte Körper ließen sich im gefalteten Blechteil nicht wiederfinden ({string.Join(", ", missing.Select(n => $"„{n}“"))}; " +
                                                    "abgewickelt heißen Körper womöglich anders). Bitte das Teil zurückfalten, die Körper neu wählen und erneut spiegeln. Es wurde nichts gespiegelt.");
            return names.Select(n => byName[n]).ToList();
        }

        /// <param name="bodyNames">Names of the selected bodies, taken before anything was rebuilt.</param>
        private MirrorJobResult Run(MirrorRequest req, RunProgress progress, MirrorReport report, SheetMetalSession? session, List<string> bodyNames)
        {
            ModelDoc2 source = req.Source;
            MirrorOptions opt = req.Options;
            var selectedBodyNames = new HashSet<string>(bodyNames, StringComparer.Ordinal);
            SheetMetalTree sheetMetal = session == null ? SheetMetalTree.Empty : SwSheetMetal.Tree(source);
            List<Body2> bodies = session == null ? req.Bodies : SelectedBodiesFolded(source, bodyNames);

            var skipped = new List<(Feature feature, string reason)>();
            // The coordinate space of sketch region edges, once known for certain, holds for source and target alike.
            var regionSpaces = new RegionSpaceMemory();
            List<Feature> features;
            List<FeatureRecord> records;
            bool seedBodies;
            BodyScope bodyScope;
            // Capture and measurement roll the original back and forth; tree and graphics need not follow every step.
            using (new UpdateFreeze(source))
            {
                features = FeatureCollector.Collect(source, _math, req.Features, bodies, skipped, out seedBodies, out bodyScope, sheetMetal);
                foreach (var (feature, reason) in skipped)
                    report.Entries.Add(new ReportEntry(feature.Name, feature.GetTypeName2(), ReportStatus.Skipped, reason));
                if (features.Count == 0)
                {
                    report.General.Add("Es wurden keine spiegelbaren Features gefunden.");
                    report.Aborted = true;
                    return new MirrorJobResult(report, null);
                }
                // Refusal step 1 (blech-plan §1.5): by type, a part of an accepted owner (the SheetMetal of
                // a collected base flange) exempt; until S1b also sheet metal in the same part. The
                // fallback switch comes with the fallback (S7a).
                string? refusal = UnsupportedFeatures.RefusalBeforeCapture(features.Select(f => (f.Name, f.GetTypeName2())).ToList(), sheetMetal.Owners,
                    opt.Target == MirrorTarget.SamePart, fallbackOn: false);
                if (refusal != null) throw new InvalidOperationException(refusal);
                progress.Start(features.Count, opt.VerifySteps);
                var capture = new FeatureCapture(source, _math, regionSpaces, sheetMetal: sheetMetal, wasFlattened: session == null ? null : (Func<string, bool>)session.WasFlattened);
                List<FeatureRecord>? captured = capture.CaptureAll(features, f => progress.Show(RunSteps.Capture, f.Name), progress.CancelAfterStep);
                if (captured == null) return CancelledBeforeBuild(report, RunSteps.Capture);
                records = captured;
                // A body mirror of a body an earlier one made: that one's seeds are not rebuilt twice.
                MirrorFeatureRules.FlattenSeeds(records);
                NoteUnrequestedSeedBodies(records, bodyScope, req.Features);
                MarkOtherBodies(records, bodyScope);
                for (int i = 0; i < records.Count; i++) CaptureDimensions(features[i], records[i]);
                // Refusal step 2 (blech-plan §1.5): a sheet-metal body whose carrier is not rebuilt – before anything is measured or made.
                if (session != null)
                {
                    var types = new Dictionary<string, string>(StringComparer.Ordinal);
                    List<BodyHistory> histories = SwDoc.BodyHistories(source, SwDoc.FeaturesInCreationOrder(source), types);
                    if (SheetMetalPlanner.Refusal(histories, records, types, sheetMetal) is string carrier) throw new InvalidOperationException(carrier);
                }
                // With selected bodies, a feature may also change a body that is gone at the end; only the rolled-back state shows it.
                MassCapture.ForeignRule? foreign = bodies.Count > 0 || seedBodies
                    ? new MassCapture.ForeignRule(SwDoc.FeaturesInCreationOrder(source).Select(f => f.Name), features.Concat(skipped.Select(s => s.feature)).Select(f => f.Name))
                    : null;
                if (opt.VerifySteps && !MassCapture.Measure(source, records, progress, foreign)) return CancelledBeforeBuild(report, RunSteps.Measure);
            }

            // Capture and measurement end with the bar at the end; an interrupted regeneration may have stopped it mid-tree.
            if (!SwDoc.RollToEnd(source))
            {
                report.Aborted = true;
                report.General.Add("Die Rückrollleiste des Originals ließ sich nach dem Erfassen nicht wieder ans Ende setzen (Neuaufbau unterbrochen?). " +
                                   "Es wurde nichts aufgebaut; bitte die Leiste ans Ende setzen und erneut spiegeln.");
                return new MirrorJobResult(report, null);
            }

            // ---- 2. target
            ModelDoc2 target = opt.Target == MirrorTarget.SamePart ? source : CreatePart(source, opt, report);
            report.TargetName = target.GetTitle();
            // Whole part, selected bodies, or seed bodies of a selected body pattern: a body of its own.
            bool separateBody = opt.Target == MirrorTarget.SamePart && (req.Features.Count == 0 || bodies.Count > 0 || seedBodies);
            var ctx = new BuildContext(_sw, target, _math, new Reflection(req.MirrorPlane), opt, report, separateBody) { RegionSpaces = regionSpaces };
            ctx.Shared.Progress = progress;
            if (session != null)
            {
                ctx.Shared.Session = session;
                ctx.Shared.Guard = new SheetMetalGuard(target);
                ctx.Shared.SingleSheetMetalBody = SheetMetalRules.StaysSingleBody(records);
            }
            foreach (PartOfFeatureRecord part in records.OfType<PartOfFeatureRecord>()) ctx.PartOwners[part.Name] = part.Owner;
            foreach (SketchPatternRecord pattern in records.OfType<SketchPatternRecord>()) ctx.PointSketches.Add(pattern.SketchName);
            report.General.Add($"Spiegelebene: {ReportText.Plane(req.MirrorPlane)}");
            // New part: only the part's material is copied. Same part: a body of its own gets the part's material.
            if (opt.Target == MirrorTarget.NewPart ? opt.CopyMaterial : separateBody) NoteBodyMaterials(source, selectedBodyNames, req.Features.Count > 0 ? features : null, report);

            try
            {
                using (new SwPreferences(_sw))
                using (new UpdateFreeze(target))
                {
                    for (int i = 0; i < records.Count; i++)
                    {
                        progress.Show(RunSteps.Build, records[i].Name);
                        ReportEntry entry = StepRunner.BuildOne(ctx, records[i]);
                        if (ctx.Shared.AbortReason != null)
                        {
                            report.Aborted = true;
                            report.General.Add($"Abbruch nach „{records[i].Name}“: {ctx.Shared.AbortReason}");
                            break;
                        }
                        if (entry.Status == ReportStatus.Failed && opt.StopOnError)
                        {
                            report.Aborted = true;
                            break;
                        }
                        // Esc: stops like an error with "stop on error", what is built stays. Already
                        // confirmed within the step (a mirror feature's second half): not asked again.
                        if (ctx.Shared.Cancelled || progress.CancelAfterStep())
                        {
                            report.Aborted = report.Cancelled = true;
                            report.General.Add(RunSteps.CancelledInBuild(records[i].Name, records.Count - i - 1));
                            // Parts already reported as built with an owner that now is not built.
                            foreach (string owner in RunSteps.OwnersNotBuiltAfter(records, i)) StepRunner.WithdrawParts(ctx, owner);
                            break;
                        }
                    }
                }
                progress.Show(RunSteps.Finish);
                // Own values of the first sheet-metal body that no second body brought (B15).
                if (session != null) BaseFlangeBuilder.FinishPending(ctx);
                // The copies' flat patterns get the state of their originals (flattened or folded).
                if (session != null)
                    report.General.AddRange(session.ApplyTo(target, records.OfType<FlatPatternRecord>()
                        .Where(fp => ctx.Created.ContainsKey(fp.Name)).Select(fp => (fp.Name, ctx.Created[fp.Name]))));
                if (regionSpaces.ReportLine() is string spaceLine) report.General.Add(spaceLine);
                target.EditRebuild3();
                bool atEnd = SwDoc.RollToEnd(target);
                if (!atEnd)
                {
                    // Not complete: in an assembly the part stays open and unsaved, the original is not deleted.
                    report.Aborted = true;
                    report.General.Add("Achtung: Die Rückrollleiste steht nach dem Aufbau nicht am Ende des Feature-Baums (Neuaufbau unterbrochen?). " +
                                       "Bitte ans Ende setzen und das Ergebnis prüfen.");
                }

                // ---- 3. finish
                // Equations never reach the second half of a mirror feature: said once where they drive copied dimensions.
                bool secondHalves = ctx.Groups.Count > 0;
                if (opt.Target == MirrorTarget.NewPart)
                {
                    if (opt.CopyEquations && CopyEquations(source, ctx, report) > 0 && secondHalves) report.General.Add(MirrorFeatureRules.EquationsNote);
                    target.ShowNamedView2("*Isometric", (int)swStandardViews_e.swIsometricView);
                    target.ViewZoomtofit2();
                }
                else
                {
                    if (NoteEquationDrivenDimensions(source, ctx, report) && secondHalves) report.General.Add(MirrorFeatureRules.EquationsNote);
                    if (opt.DeleteOriginal && !atEnd) report.General.Add("Original nicht gelöscht: Die Rückrollleiste steht nicht am Ende.");
                    // Esc with the last step: everything is built, but the user wanted to stop – deleting cannot be undone.
                    else if (opt.DeleteOriginal && progress.EscAtEnd) report.General.Add("Original nicht gelöscht: Der Lauf wurde mit Esc abgebrochen.");
                    else if (opt.DeleteOriginal) DeleteOriginals(source, features, bodyScope, report);
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

        /// <summary>
        /// A feature pulled in through a selected body that also changed other bodies: the copy
        /// acts on the mirrored bodies only, so what the original did is no reference for it.
        /// </summary>
        private static void MarkOtherBodies(List<FeatureRecord> records, BodyScope scope)
        {
            foreach (FeatureRecord r in records)
                if (scope.OtherBodiesChanged.TryGetValue(r.Name, out List<string> bodies)) MassCapture.NoteOtherBodies(r, bodies);
        }

        /// <summary>
        /// „Körper spiegeln“ reached only through a body it made: its seed bodies come along
        /// unselected. The record says so, judged by the body scope – the same judgement that
        /// keeps the original from being deleted (<see cref="DeleteOriginals"/> says so itself).
        /// </summary>
        private static void NoteUnrequestedSeedBodies(List<FeatureRecord> records, BodyScope scope, List<Feature> chosen)
        {
            var chosenNames = new HashSet<string>(chosen.Select(f => f.Name), StringComparer.Ordinal);
            foreach (MirrorFeatureRecord m in records.OfType<MirrorFeatureRecord>())
            {
                m.UnrequestedSeedBodies.Clear();
                m.UnrequestedSeedBodies.AddRange(MirrorFeatureRules.UnrequestedSeedBodies(m, scope.UnselectedBodies, chosenNames.Contains(m.Name)));
            }
        }

        private static string Quoted(IEnumerable<string> names) => string.Join(", ", names.Select(n => $"\"{n}\""));

        /// <summary>
        /// The feature's own dimensions of the original, for their names. Not those of sketches
        /// (SketchBuilder names theirs) nor of reference geometry: that is rebuilt on other
        /// references, so its dimensions measure something else. Not those of a mirror feature
        /// either: it has none of its own, its second half is a set of new features. Unreadable:
        /// none, and copied equations then leave this feature's dimensions alone.
        /// </summary>
        private static void CaptureDimensions(Feature feature, FeatureRecord record)
        {
            if (record is SketchRecord || record is PartOfFeatureRecord || record is RefPlaneRecord || record is RefAxisRecord ||
                record is CoordSysRecord || record is UnsupportedRecord || record is MirrorFeatureRecord)
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

        private ModelDoc2 CreatePart(ModelDoc2 source, MirrorOptions opt, MirrorReport report)
        {
            string template = _sw.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            if (string.IsNullOrEmpty(template))
                throw new InvalidOperationException("Keine Standardvorlage für Teile eingestellt (Optionen › Standardvorlagen).");
            var target = (ModelDoc2)(object)_sw.NewDocument(template, 0, 0, 0)
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
        /// which. The bodies that are mirrored: the selected ones (by name, read fresh), with
        /// selected features those the collected features change, else all. Only a note: never
        /// stops the run.
        /// </summary>
        private static void NoteBodyMaterials(ModelDoc2 source, HashSet<string> selectedBodies, List<Feature>? collected, MirrorReport report)
        {
            try
            {
                if (!(source is PartDoc part)) return;
                string config = source.GetActiveConfiguration() is Configuration active ? active.Name : "";
                string db = "";
                string partMaterial = part.GetMaterialPropertyName2(config, out db);
                List<Body2> bodies = SwDoc.SolidBodies(source);
                if (selectedBodies.Count > 0) bodies = bodies.Where(b => selectedBodies.Contains(b.Name)).ToList();
                else if (collected != null)
                {
                    var rebuilt = new HashSet<string>(collected.Where(f => !SwDoc.IsSketch(f)).Select(f => f.Name), StringComparer.Ordinal);
                    bodies = bodies.Where(b => b.GetFeatures() is object[] fs && fs.OfType<Feature>().Any(f => rebuilt.Contains(f.Name))).ToList();
                }
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
        /// disabled state; an equation suppressed in an older original (before SOLIDWORKS 2014)
        /// becomes disabled – the new part ignores Suppression. They name dimensions ("D1@Skizze1"): an equation is
        /// only copied if each dimension it names is known to be the same one in the copy, else
        /// it would silently drive another. In the original they give the values the copy
        /// already has, so the part must stay as it is – checked by volume and centroid.
        /// </summary>
        /// <returns>How many equations were copied.</returns>
        private static int CopyEquations(ModelDoc2 source, BuildContext ctx, MirrorReport report)
        {
            EquationMgr src = source.GetEquationMgr(), dst = ctx.Doc.GetEquationMgr();
            int n = src.GetCount(), ok = 0;
            if (n == 0) return 0;
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
                    // IEquationMgr.Suppression is ignored in documents created as of SOLIDWORKS 2014, the new part is one; Disabled is its equivalent there.
                    if (disabled || suppressed) dst.set_Disabled(index, true);
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
                if (before.Volume <= 0 && after.Volume <= 0)
                {
                    check = ", Volumen und Schwerpunkt nicht geprüft (die Kopie hat keinen Volumenkörper)";
                }
                else if (MassComparison.Compare(MassState.Empty, after - before, before).Passes)
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
            // As an entry with a notice, so the summary and the assembly report count it.
            if (deviation != null) report.Entries.Add(new ReportEntry("Gleichungen", "Gleichungen", ReportStatus.Warning, deviation));
            return ok;
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
        /// <returns>Whether equations drive mirrored dimensions.</returns>
        private static bool NoteEquationDrivenDimensions(ModelDoc2 doc, BuildContext ctx, MirrorReport report)
        {
            try
            {
                EquationMgr eq = doc.GetEquationMgr();
                var driven = new List<string>();
                for (int i = 0; i < eq.GetCount(); i++)
                    if (!eq.get_Disabled(i) && !eq.get_Suppression(i) && EquationText.Driven(eq.get_Equation(i)) is string name &&
                        EquationText.Dimension(name) is (string, string owner) && ctx.Created.ContainsKey(owner))
                        driven.Add(name);
                if (driven.Count == 0) return false;
                string more = driven.Count > 5 ? $" und {driven.Count - 5} weitere" : "";
                report.General.Add($"Gleichungen werden im selben Teil nicht übertragen: Im Original treiben sie {Quoted(driven.Take(5))}{more}, " +
                                   "in der Kopie sind diese Maße feste Werte.");
                return true;
            }
            catch (Exception)
            {
                // Equations not readable: no note.
                return false;
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
