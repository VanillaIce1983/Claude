using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Build.Builders;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
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
            new ExtrudeBuilder(),
            new RevolveBuilder(),
            new SweepBuilder(),
            new LoftBuilder(),
            new FilletBuilder(),
            new VariableFilletBuilder(),
            new ChamferBuilder(),
            new ShellBuilder(),
            new DraftBuilder(),
            new HoleBuilder(),
            new PatternBuilder(),
        };

        public MirrorJob(SldWorks sw)
        {
            _sw = sw;
            _math = new SwMath(sw);
        }

        public MirrorJobResult Run(MirrorRequest req)
        {
            var report = new MirrorReport();
            ModelDoc2 source = req.Source;
            MirrorOptions opt = req.Options;

            // ---- 1. capture
            if (SwDoc.TopLevelFeatures(source).Any(f => f.IsRolledBack()))
                throw new InvalidOperationException("Die Rückrollleiste steht nicht am Ende des Feature-Baums. Bitte ans Ende setzen und erneut spiegeln.");

            var skipped = new List<(Feature feature, string reason)>();
            List<Feature> features = FeatureCollector.Collect(source, _math, req.Features, req.Bodies, skipped, out bool seedBodies);
            foreach (var (feature, reason) in skipped)
                report.Entries.Add(new ReportEntry(feature.Name, feature.GetTypeName2(), ReportStatus.Skipped, reason));
            if (features.Count == 0)
            {
                report.General.Add("Es wurden keine spiegelbaren Features gefunden.");
                report.Aborted = true;
                return new MirrorJobResult(report, null);
            }
            var capture = new FeatureCapture(source, _math);
            List<FeatureRecord> records = features.Select(capture.Capture).ToList();
            // Sketches of a hole are rebuilt with the hole – only if the hole itself is rebuilt.
            var holes = new HashSet<string>(records.OfType<HoleRecord>().Select(h => h.Name), StringComparer.Ordinal);
            for (int i = 0; i < records.Count; i++)
                if (records[i] is PartOfFeatureRecord part && !holes.Contains(part.Owner))
                    records[i] = capture.CaptureOnItsOwn(features[i], part);
            if (opt.VerifySteps) MassCapture.Measure(source, records);

            // ---- 2. target
            ModelDoc2 target = opt.Target == MirrorTarget.SamePart ? source : CreatePart(source, opt, report);
            report.TargetName = target.GetTitle();
            // Whole part, selected bodies, or seed bodies of a selected body pattern: a body of its own.
            bool separateBody = opt.Target == MirrorTarget.SamePart && (req.Features.Count == 0 || req.Bodies.Count > 0 || seedBodies);
            var ctx = new BuildContext(_sw, target, _math, new Reflection(req.MirrorPlane), opt, report, separateBody);
            foreach (PartOfFeatureRecord part in records.OfType<PartOfFeatureRecord>()) ctx.PartOwners[part.Name] = part.Owner;
            report.General.Add($"Spiegelebene: {req.MirrorPlane}");

            try
            {
                using (new SwPreferences(_sw))
                using (new UpdateFreeze(target))
                {
                    foreach (FeatureRecord record in records)
                    {
                        ReportEntry entry = BuildOne(ctx, record);
                        if (entry.Status == ReportStatus.Failed && opt.StopOnError)
                        {
                            report.Aborted = true;
                            break;
                        }
                    }
                }
                target.EditRebuild3();

                // ---- 3. finish
                if (opt.Target == MirrorTarget.NewPart)
                {
                    if (opt.CopyEquations) CopyEquations(source, target, report);
                    target.ShowNamedView2("*Isometric", (int)swStandardViews_e.swIsometricView);
                    target.ViewZoomtofit2();
                }
                else if (opt.DeleteOriginal)
                {
                    DeleteOriginals(source, features, report);
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
            BuildResult result;
            string verification = "";
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
            }
            finally
            {
                // Also after a failure: a feature may have been created (or removed) before it.
                ctx.Topology.Invalidate();
            }

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

        private static BuildResult RemoveAgain(BuildContext ctx, FeatureRecord record, BuildResult result)
        {
            // A hole's sketches belong to the hole alone and go with it.
            BuilderSupport.Delete(ctx, result.Feature!, record is HoleRecord);
            if (record is HoleRecord hole) ctx.ForgetSketch(hole.PositionSketchName);
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
        /// Global variables and equations, copied verbatim. They refer to features and
        /// dimensions by name ("D1@Skizze1"), which is why the rebuild keeps the original names.
        /// </summary>
        private static void CopyEquations(ModelDoc2 source, ModelDoc2 target, MirrorReport report)
        {
            EquationMgr src = source.GetEquationMgr(), dst = target.GetEquationMgr();
            int n = src.GetCount(), ok = 0;
            var failed = new List<string>();
            for (int i = 0; i < n; i++)
            {
                string eq = src.get_Equation(i);
                if (dst.Add2(-1, eq, true) >= 0) ok++;
                else failed.Add(eq);
            }
            if (n > 0) report.General.Add($"Gleichungen: {ok} von {n} übernommen.");
            foreach (string eq in failed) report.General.Add($"  nicht übernommen: {eq}");
        }

        /// <summary>
        /// Same part: removes the original features – only if every one of them has a
        /// counterpart (skipped axes, 3D sketches, coordinate systems … would be lost) and
        /// nothing else depends on them.
        /// </summary>
        private static void DeleteOriginals(ModelDoc2 doc, List<Feature> originals, MirrorReport report)
        {
            if (!report.AllMirrored)
            {
                var blocking = report.Blocking.Select(e => $"{e.Feature} ({MirrorReport.StatusText(e.Status)})").ToList();
                report.General.Add(blocking.Count > 0
                    ? $"Original nicht gelöscht: ohne fehlerfreies Gegenstück sind {string.Join(", ", blocking)}."
                    : "Original nicht gelöscht, weil der Lauf abgebrochen wurde.");
                return;
            }
            var names = new HashSet<string>(originals.Select(f => f.Name), StringComparer.Ordinal);
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
