using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Core.Verification;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Base flange and tab (A1 §8, blech-plan S1a). SOLIDWORKS makes the body's SheetMetal and
    /// flat pattern with it – and, in a new part, the part's defaults.
    ///
    /// Sides: the plate side of a closed profile and the depth of an open one follow σ like an
    /// extrusion; the material side of an open contour follows the in-plane rule
    /// (<see cref="SheetMetalRules.BaseFlangeFlags"/>). Values: the first body makes the defaults
    /// from the original's, every later one is created with what the target has and gets its own
    /// values through the overrides of its SheetMetal – so creating it does not rewrite the
    /// defaults the earlier bodies use (the guard checks that). The first body's own overrides
    /// need a multibody part: they follow with the second body, or become the part's values in a
    /// copy that stays single-body (<see cref="SheetMetalRules.BodyOverrideModeOf"/>).
    ///
    /// Creation: the data object, v1, v2 (<see cref="SheetMetalRules.CreationPathOrder"/>), each
    /// attempt checked – exactly one new base flange, the body count as expected (a tab joins its
    /// body) – and removed again before the next. References, symmetric thickness and the gauge
    /// table follow in one edit, everything is read back.
    /// </summary>
    internal sealed class BaseFlangeBuilder : IFeatureBuilder
    {
        private const double PlaceholderDepth = 0.01;

        public bool Handles(FeatureRecord record) => record is BaseFlangeRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (BaseFlangeRecord)record;
            if (!ctx.Sketches.TryGetValue(r.SketchName, out SketchMap sketch))
                return BuildResult.Failed($"Skizze \"{r.SketchName}\" wurde nicht neu aufgebaut.");
            ModelDoc2 doc = ctx.Doc;
            var notes = new List<string>();

            // Same part: a tab only on the job's own bodies (path C first, with scope), none yet: not built;
            // the first mirrored body never merges (body count and the original's mass after every attempt).
            string? refused = PlanSeparateBody(ctx, false, r.MergesIntoExisting, out bool scope, out bool _)
                              ?? SheetMetalRules.TabRefusal(r.MergesIntoExisting, ctx.SeparateBody, ctx.SeparateBody ? ctx.OwnBodies().Count : 0);
            if (refused != null) return Fail(refused, notes);

            var (revDir, revThk) = SheetMetalRules.BaseFlangeFlags(r.ClosedProfile, r.ReverseDirection, r.ReverseThickness, sketch.Sigma, r.SymmetricThickness);

            // ---- end references of an open profile, resolved before creation (blind equivalent as for extrusions)
            var sides = new List<SideTarget>();
            if (!r.ClosedProfile)
            {
                sides.Add(ResolveSide(ctx, sketch, r.Side1, 1, notes));
                if (r.OffsetDirections == 2) sides.Add(ResolveSide(ctx, sketch, r.Side2, 2, notes));
                if (sides.Any(s => ExtrudeSide.NeedsReference(s.End) && s.Reference == null))
                    return Fail("Bezug der Endbedingung im Ziel nicht gefunden.", notes);
            }

            // ---- creation values: the first body makes the defaults, later ones take what the target has
            SheetMetalSnapshot before = SwSheetMetal.Snapshot(doc);
            Feature? targetTemplate = SwSheetMetal.Template(doc);
            bool first = targetTemplate == null && SwSheetMetal.OfType(doc, "SheetMetal").Count == 0;
            SheetMetalParameters? template = first || targetTemplate == null ? null : TryRead(doc, targetTemplate, false);
            Feature? tabSheetMetal = r.MergesIntoExisting ? TabSheetMetal(ctx, r) : null;
            SheetMetalParameters? targetBody = tabSheetMetal == null ? null : TryRead(doc, tabSheetMetal, true);
            // Same part: the defaults are the original's and never written – the copy's SheetMetal also
            // overrides what they do not give it (a single-body original whose values are not the defaults).
            IReadOnlyList<int> overrides = ctx.SamePart && !r.MergesIntoExisting
                ? SheetMetalRules.SamePartOverrides(r.Body, template, r.Defaults)
                : SheetMetalRules.BodyOverrides(r.Body);
            BodyOverrideMode overrideMode = SheetMetalRules.BodyOverrideModeOf(first, r.MergesIntoExisting, overrides.Count, ctx.Shared.SingleSheetMetalBody);
            // What the first body makes the part's values: the original's defaults, or in a copy that stays single-body the body's own.
            SheetMetalParameters? partValues = overrideMode == BodyOverrideMode.PartValues ? SheetMetalRules.OwnValues(r.Defaults, r.Body!) : r.Defaults;
            FlangeCreation values = SheetMetalRules.CreationValues(first, r.MergesIntoExisting, partValues, r.Body, template, targetBody,
                r.OwnBendAllowance, r.OwnRelief, r.Thickness, r.BendRadius);
            // Same part: the defaults stay the original's, material-driven ones too – the copy shares them.
            if (r.UsesMaterialParameters) notes.Add(SheetMetalRules.MaterialNoteFor(ctx.SamePart));
            bool gauge = first && r.Gauge != null && File.Exists(r.Gauge.TablePath);
            if (r.Gauge != null && first && !gauge)
                notes.Add($"Blechstärkentabelle „{r.Gauge.TablePath}“ nicht gefunden; die Werte werden fest übernommen.");
            else if (r.Gauge != null && !first && !(template?.UseGaugeTable ?? false))
                notes.Add($"Blechstärkentabelle „{r.Gauge.TablePath}“: Ein Teil hat nur die des ersten Blechkörpers; die Werte werden fest übernommen.");

            // ---- create: each path checked and cleaned up before the next
            int expectedDelta = SheetMetalRules.ExpectedBodyDelta(r.MergesIntoExisting);
            // Separate body: an attempt that merged with the original (no Merge member on A and B) is no success.
            MassState originalBefore = ctx.SeparateBody ? SwDoc.MassOf(ctx.OriginalBodies()) : MassState.Empty;
            var tried = new List<string>();
            // Why attempts failed: said only if none succeeds.
            var failures = new List<string>();
            Feature? f = null;
            List<Feature> made = new List<Feature>();
            BaseFlangePath used = BaseFlangePath.DataObject;
            bool allowanceAtCreation = false;
            foreach (BaseFlangePath path in SheetMetalRules.CreationPathOrder(r.MergesIntoExisting && ctx.SeparateBody))
            {
                foreach (Attempt attempt in Attempts(path, r, sides))
                {
                    tried.Add(attempt.Name);
                    bool withAllowance = false;
                    try
                    {
                        withAllowance = Create(ctx, sketch, r, attempt, values, revDir, revThk, scope, sides);
                    }
                    catch (Exception ex)
                    {
                        // A call that throws is one more attempt that did not work.
                        failures.Add($"Weg {attempt.Name}: {ex.Message}");
                    }
                    finally
                    {
                        SwSelect.Clear(doc);
                        ctx.Topology.Invalidate();
                    }
                    List<Feature> now = SwSheetMetal.NewSince(doc, before.Names);
                    List<Feature> flanges = now.Where(x => x.GetTypeName2() == "SMBaseFlange").ToList();
                    bool counted = flanges.Count == 1 && SwDoc.SolidBodies(doc).Count - before.Bodies == expectedDelta;
                    if (counted && ctx.SeparateBody && !OriginalAsBefore(ctx, now, originalBefore))
                    {
                        counted = false;
                        failures.Add($"Weg {attempt.Name}: hätte das Original verändert (verschmolzen); wieder entfernt.");
                    }
                    if (counted)
                    {
                        f = flanges[0];
                        made = now;
                        used = path;
                        allowanceAtCreation = withAllowance;
                        break;
                    }
                    List<string> left = SwSheetMetal.RemoveNew(doc, before.Names);
                    ctx.Topology.Invalidate();
                    if (left.Count > 0)
                    {
                        notes.AddRange(failures);
                        notes.Add($"Nach dem Weg {attempt.Name} nicht wieder entfernt: {string.Join(", ", left.Select(n => $"„{n}“"))} – bitte prüfen.");
                        return Fail($"SOLIDWORKS hat die Basislasche nicht erzeugt (Wege: {string.Join(", ", tried)}).", notes);
                    }
                }
                if (f != null) break;
            }
            if (f == null)
            {
                notes.AddRange(failures);
                return Fail($"SOLIDWORKS hat die Basislasche nicht erzeugt (Wege: {string.Join(", ", tried)}).", notes);
            }

            Feature? sheetMetal = made.FirstOrDefault(x => x.GetTypeName2() == "SheetMetal");
            Feature? flatPattern = made.FirstOrDefault(x => x.GetTypeName2() == "FlatPattern");
            if (sheetMetal == null && r.SheetMetalName.Length > 0)
                notes.Add($"Das Blech-Feature der Kopie wurde nicht gefunden; „{r.SheetMetalName}“ hat in der Kopie kein Gegenstück (Gleichungen darauf werden nicht übernommen).");
            if ((r.OwnBendAllowance != null || r.OwnRelief != null) && !allowanceAtCreation)
                notes.Add($"Eigene Biegezugabe oder Entlastung der Basislasche über den Weg {SheetMetalRules.PathName(used)} nicht übertragbar; die Kopie verwendet die Standardwerte.");

            // ---- one edit: directions, end conditions with references, symmetric thickness, gauge table of the first body
            bool needsEdit = sides.Any(s => s.Reference != null || s.Temporary) || r.SymmetricThickness || gauge;
            if (needsEdit && !FeatureEdit.BaseFlange(doc, f, d => ApplyDefinition(d, r, sides, revDir, revThk, gauge)))
            {
                RemoveMade(ctx, before.Names, notes);
                return Fail("Endbedingungen, Bezüge, Richtungen oder Blechstärkentabelle ließen sich nicht setzen; die Basislasche wurde wieder entfernt.", notes);
            }

            // ---- parameters: the first body makes the defaults, a body's own values go on its SheetMetal
            SheetMetalParameters? defaults = partValues ?? r.Body;
            if (first && defaults != null && !gauge) notes.AddRange(WritePartValues(doc, sheetMetal, defaults));
            var info = new List<string>();
            if (overrideMode == BodyOverrideMode.PartValues) info.Add(SheetMetalRules.OwnValuesAsPartValues);
            // The body's own values: the overridden groups from it, the rest – in the same part a group the defaults do not give it – as it has them.
            if (sheetMetal != null && overrideMode == BodyOverrideMode.Apply)
                notes.AddRange(SwSheetMetal.Apply(doc, sheetMetal, SheetMetalRules.BodyValues(r.Defaults, r.Body!), true, overrides));
            if (sheetMetal != null && overrideMode == BodyOverrideMode.Deferred)
            {
                // Set by StepRunner after the step that makes the second body, outside its check and guard.
                ctx.Shared.PendingBodyOverrides.Add(new PendingBodyOverrides(r.Name, sheetMetal, r.Body!, overrides, SheetMetalRules.OwnValues(r.Defaults, r.Body!), gauge));
                info.Add(SheetMetalRules.OwnValuesDeferred);
            }
            // Newer versions: the flange's own thickness and radius over the defaults, only once the override took.
            if (!first && r.OverrideDefaults == true) notes.AddRange(OverrideOnFlange(doc, f, r));

            // The first body still waits for its own values (B15): it, and a tab on it, has the defaults until
            // then – read back against those, so no deviation is reported that the copy will not have.
            IReadOnlyList<int>? waiting = WaitingGroups(ctx, r.MergesIntoExisting ? tabSheetMetal : sheetMetal);
            if (r.MergesIntoExisting && waiting != null) info.Add(SheetMetalRules.OwnValuesDeferred);
            bool current = SheetMetalRules.ReadsBackCurrentBendValues(waiting, r.OverrideDefaults, gauge);
            notes.AddRange(ReadBack(doc, f, r, current ? values.Thickness : r.Thickness, current ? values.BendRadius : r.BendRadius, revDir, revThk, sides));

            // ---- registration: the flange, its SheetMetal (equations on the thickness), the body for the flat pattern
            ctx.Register(r.Name, f);
            if (sheetMetal != null && r.SheetMetalName.Length > 0) ctx.Register(r.SheetMetalName, sheetMetal);
            ctx.SheetMetalBodies[r.Name] = new SheetMetalBody(sheetMetal, flatPattern, sketch.Sigma);

            BuildResult result = Ok(f, Describe(r, revDir != r.ReverseDirection, revThk != r.ReverseThickness), notes);
            result.Info.Add($"Angelegt über {SheetMetalRules.PathName(used)}.");
            result.Info.AddRange(info);
            if (sheetMetal != null && r.SheetMetalName.Length > 0)
                result.Companions.Add(new Companion(sheetMetal, r.SheetMetalName, r.CompanionDimensions));
            result.RemoveHelpers = () => RemoveCompanions(ctx, flatPattern, sheetMetal);
            AddAlternatives(result, ctx, f, r, sides);
            return result;
        }

        // ---- end conditions

        /// <summary>One direction of an open profile in the target: its end condition and depth, its reference, whether it is created with a placeholder first.</summary>
        private sealed class SideTarget
        {
            public SideTarget(int which, FlangeSide source, EndCondition end, double distance, object? reference)
            {
                Which = which;
                Source = source;
                End = end;
                Distance = distance;
                Reference = reference;
            }

            public int Which { get; }
            public FlangeSide Source { get; }
            public EndCondition End { get; }
            public double Distance { get; }
            public object? Reference { get; }

            /// <summary>Created blind with a placeholder depth; the edit sets the real condition.</summary>
            public bool Temporary => ExtrudeSide.NeedsReference(End);

            /// <summary>The condition used while creating.</summary>
            public EndCondition CreateEnd => Temporary ? EndCondition.Blind : End;

            public double CreateDistance => Temporary || Distance <= 0 ? PlaceholderDepth : Distance;
        }

        /// <summary>
        /// The mirrored reference of an up-to or offset end; not found but a plane parallel to the
        /// sketch: the equivalent blind depth, as for extrusions (<see cref="ExtrudeSide.BlindEquivalent"/>).
        /// </summary>
        private static SideTarget ResolveSide(BuildContext ctx, SketchMap sketch, FlangeSide side, int which, List<string> notes)
        {
            if (!ExtrudeSide.NeedsReference(side.End) || side.Reference == null)
                return new SideTarget(which, side, side.End, side.Distance, null);
            object? target;
            try
            {
                target = Safe(ctx.Topology.Resolve(side.Reference.Mirror(ctx.Mirror), notes));
            }
            catch (InvalidOperationException ex)
            {
                notes.Add(ex.Message);
                ctx.Topology.Invalidate();
                target = null;
            }
            if (target != null) return new SideTarget(which, side, side.End, side.Distance, target);
            var equivalent = new ExtrudeSide { End = side.End, Depth = side.Distance, OffsetReverse = side.ReverseOffset, Reference = side.Reference };
            double? blind = equivalent.BlindEquivalent(sketch.Source.Frame.Plane);
            if (!blind.HasValue) return new SideTarget(which, side, side.End, side.Distance, null);
            notes.Add($"Endbedingung {side.End} (Richtung {which}) ohne Bezug im Ziel, ersetzt durch Blind {blind.Value * 1000:0.###} mm.");
            return new SideTarget(which, side, EndCondition.Blind, blind.Value, null);
        }

        /// <summary>Writes one direction: the newer members late-bound where the running version has them, else the 2019 ones.</summary>
        private static void ApplySide(BaseFlangeFeatureData d, SideTarget s)
        {
            int type = SheetMetalRules.ToFlangeOffset(s.End) ?? 1;
            bool one = s.Which == 1;
            if (!SwLate.Set(d, $"D{s.Which}EndConditionType", type))
            {
                if (one) d.D1OffsetType = type;
                else d.D2OffsetType = type;
            }
            if (s.End == EndCondition.Blind || s.End == EndCondition.MidPlane || s.End == EndCondition.OffsetFromSurface)
            {
                if (!SwLate.Set(d, $"D{s.Which}EndConditionDistance", s.Distance))
                {
                    if (one) d.D1OffsetDistance = s.Distance;
                    else d.D2OffsetDistance = s.Distance;
                }
            }
            if (one) d.D1ReverseOffset = s.Source.ReverseOffset;
            else d.D2ReverseOffset = s.Source.ReverseOffset;
            if (s.Reference != null && !SwLate.Set(d, $"D{s.Which}EndConditionReference", s.Reference))
            {
                if (one) d.D1OffsetReference = s.Reference;
                else d.D2OffsetReference = s.Reference;
            }
        }

        /// <summary>The definition after creation: directions, sides, symmetric thickness, the gauge table in the order of the API example.</summary>
        private static void ApplyDefinition(BaseFlangeFeatureData d, BaseFlangeRecord r, List<SideTarget> sides, bool revDir, bool revThk, bool gauge)
        {
            // Only what differs: ReverseThickness takes a write only with own sheet-metal values (help), and a refused write would cost the flange.
            if (d.ReverseDirection != revDir) d.ReverseDirection = revDir;
            if (d.ReverseThickness != revThk) d.ReverseThickness = revThk;
            if (!r.ClosedProfile && d.OffsetDirections != r.OffsetDirections) d.OffsetDirections = r.OffsetDirections;
            foreach (SideTarget s in sides) ApplySide(d, s);
            if (r.SymmetricThickness) SwLate.Set(d, "SymmetricThickness", true);
            if (gauge)
            {
                GaugeData g = r.Gauge!;
                d.UseGaugeTable = true;
                d.GaugeTablePath = g.TablePath;
                d.ThicknessTableName = g.ThicknessName;
                d.TableRadius = g.TableRadius;
                d.OverrideThickness = g.OverrideThickness;
                d.OverrideRadius = g.OverrideRadius;
                d.OverrideKFactor = g.OverrideKFactor;
                if (g.OverrideThickness) d.Thickness = r.Thickness;
                if (g.OverrideRadius) d.BendRadius = r.BendRadius;
                if (g.OverrideKFactor) d.KFactor = g.KFactor;
            }
        }

        // ---- creation paths

        /// <summary>One call of a path: v1 with each way of passing directions and end conditions.</summary>
        private sealed class Attempt
        {
            public Attempt(BaseFlangePath path, string name, int dirToUse = 1, bool flangeOffsetCodes = false)
            {
                Path = path;
                Name = name;
                DirToUse = dirToUse;
                FlangeOffsetCodes = flangeOffsetCodes;
            }

            public BaseFlangePath Path { get; }
            public string Name { get; }

            /// <summary>Most likely the number of directions (every working call passes 1); the help calls it an end condition.</summary>
            public int DirToUse { get; }

            /// <summary>End conditions as swFlangeOffsetTypes_e instead of swEndConditions_e.</summary>
            public bool FlangeOffsetCodes { get; }
        }

        /// <summary>
        /// The attempts of a path. v1 and v2: DirToUse as the number of directions, then 1; end
        /// conditions as swEndConditions_e (as third parties call it), then as swFlangeOffsetTypes_e.
        /// A closed profile has neither: one attempt.
        /// </summary>
        private static IEnumerable<Attempt> Attempts(BaseFlangePath path, BaseFlangeRecord r, List<SideTarget> sides)
        {
            string name = SheetMetalRules.PathName(path);
            if (path == BaseFlangePath.DataObject || r.ClosedProfile)
            {
                yield return new Attempt(path, name);
                yield break;
            }
            var dirs = new List<int> { r.OffsetDirections };
            if (!dirs.Contains(1)) dirs.Add(1);
            foreach (bool offsetCodes in new[] { false, true })
                foreach (int dir in dirs)
                    yield return new Attempt(path, $"{name} ({dir} Richtung{(dir == 1 ? "" : "en")}, {(offsetCodes ? "Laschenversatz" : "Endbedingung")})", dir, offsetCodes);
        }

        /// <summary>
        /// Creates by one attempt; true if the call took the allowance and relief at creation.
        /// The sides are the resolved ones: a blind equivalent is created with its real depth,
        /// a side with a reference blind with the placeholder (the edit sets it).
        /// </summary>
        private static bool Create(BuildContext ctx, SketchMap sketch, BaseFlangeRecord r, Attempt attempt, FlangeCreation v, bool revDir, bool revThk, bool scope,
            List<SideTarget> sides)
        {
            ModelDoc2 doc = ctx.Doc;
            FeatureManager fm = doc.FeatureManager;
            if (!Select(ctx, sketch, scope && attempt.Path == BaseFlangePath.V2)) return false;
            // Empty for a closed profile; side 2 only with two directions.
            SideTarget? s1 = sides.FirstOrDefault(s => s.Which == 1);
            SideTarget? s2 = sides.FirstOrDefault(s => s.Which == 2);
            int Code(SideTarget? s) => s == null ? 0
                : attempt.FlangeOffsetCodes ? SheetMetalRules.ToFlangeOffset(s.CreateEnd) ?? 1 : (int)s.CreateEnd;
            double d1 = s1?.CreateDistance ?? PlaceholderDepth, d2 = s2?.CreateDistance ?? 0;

            switch (attempt.Path)
            {
                case BaseFlangePath.V1:
                    fm.InsertSheetMetalBaseFlange(v.Thickness, revThk, v.BendRadius, d1, d2, revDir, Code(s1), Code(s2), attempt.DirToUse,
                        SwSheetMetal.NewAllowance(fm, v.Allowance), v.UseDefaultRelief, (int)v.Relief.Kind, v.Relief.Width, v.Relief.Depth,
                        v.Relief.Ratio, v.Relief.UseRatio);
                    return true;
                case BaseFlangePath.V2:
                    fm.InsertSheetMetalBaseFlange2(v.Thickness, revThk, v.BendRadius, d1, d2, revDir, Code(s1), Code(s2), attempt.DirToUse,
                        SwSheetMetal.NewAllowance(fm, v.Allowance), v.UseDefaultRelief, (int)v.Relief.Kind, v.Relief.Width, v.Relief.Depth,
                        v.Relief.Ratio, v.Relief.UseRatio, r.MergesIntoExisting, scope, !scope);
                    return true;
                default:
                {
                    var d = (BaseFlangeFeatureData)(object)fm.CreateDefinition((int)swFeatureNameID_e.swFmBaseFlange);
                    if (d == null) return false;
                    d.Thickness = v.Thickness;
                    d.BendRadius = v.BendRadius;
                    d.ReverseDirection = revDir;
                    d.ReverseThickness = revThk;
                    if (!r.ClosedProfile)
                    {
                        d.OffsetDirections = r.OffsetDirections;
                        foreach (SideTarget? s in new[] { s1, s2 })
                            if (s != null) ApplySide(d, new SideTarget(s.Which, s.Source, s.CreateEnd, s.CreateDistance, null));
                    }
                    // Without Initialize first (the 2015 example; third parties found it works so on 2026): no allowance at creation.
                    if (fm.CreateFeature(d) != null) return false;
                    // Newer versions want the data object initialised (the 2022 example); only then, so 2019 is not asked for a member it lacks.
                    if (!Select(ctx, sketch, false)) return false;
                    object? cba = SwSheetMetal.NewAllowance(fm, v.Allowance);
                    if (!SwLate.Call(d, "Initialize", false, cba != null, cba!, !v.UseDefaultRelief, (int)v.Relief.Kind, v.Relief.UseRatio,
                            v.Relief.Ratio, v.Relief.Width, v.Relief.Depth))
                        return false;
                    fm.CreateFeature(d);
                    return true;
                }
            }
        }

        /// <summary>The profile sketch on mark 0 (API examples), with a scope the job's own bodies on mark 8 (help of v2).</summary>
        private static bool Select(BuildContext ctx, SketchMap sketch, bool scope)
        {
            SwSelect.Clear(ctx.Doc);
            if (!SwSelect.Select(ctx.Doc, sketch.Feature, 0, false)) return false;
            if (!scope) return true;
            foreach (Body2 b in ctx.OwnBodies())
                if (!SwSelect.Select(ctx.Doc, b, 8, true))
                    return false;
            return true;
        }

        // ---- values

        /// <summary>The part's values, written by the first body: into the defaults, before 2013 into its SheetMetal.</summary>
        private static List<string> WritePartValues(ModelDoc2 doc, Feature? sheetMetal, SheetMetalParameters values)
        {
            // Without the gauge table (not found): fixed values, the table is not expected back.
            SheetMetalParameters want = WithoutGauge(values);
            if (SwSheetMetal.Template(doc) is Feature t) return SwSheetMetal.Apply(doc, t, want, false, Array.Empty<int>());
            return sheetMetal != null ? SwSheetMetal.Apply(doc, sheetMetal, want, false, Array.Empty<int>()) : new List<string>();
        }

        /// <summary>
        /// After a step that leaves the copy with a second sheet-metal body: the first body's own
        /// values (<see cref="BuildShared.PendingBodyOverrides"/>) on its SheetMetal, read back, under
        /// the guard (<see cref="WriteGuarded"/>). Runs after the step's check and guard – inside them
        /// the check would count body 1's change and the guard would undo it –, so the next guard
        /// snapshot holds them. If the guard cannot restore what the write changed, the run stops.
        /// </summary>
        public static void ApplyPending(BuildContext ctx, BuildResult result)
        {
            List<PendingBodyOverrides> pending = ctx.Shared.PendingBodyOverrides;
            if (pending.Count == 0 || SwSheetMetal.OfType(ctx.Doc, "SheetMetal").Count < 2) return;
            int had = result.Notes.Count;
            var guardNotes = new List<string>();
            List<(PendingBodyOverrides p, Feature sm, List<string> lines)> written = WriteGuarded(ctx, guardNotes, out string? abort);
            foreach (var (p, sm, lines) in written)
            {
                if (lines.Count == 0) result.Info.Add(SheetMetalRules.OwnValuesSet(sm.Name, p.Flange));
                result.Notes.AddRange(lines);
            }
            result.Notes.AddRange(guardNotes);
            if (abort != null)
            {
                result.Notes.Add(abort);
                ctx.Shared.AbortReason = abort;
            }
            if (result.Notes.Count > had && result.Status == ReportStatus.Ok) result.Status = ReportStatus.Warning;
            pending.Clear();
            ctx.Topology.Invalidate();
        }

        /// <summary>
        /// At the end of the build: own values still waiting (the copy got no second sheet-metal
        /// body) become the part's values – in a single-body part the body's values are the part's.
        /// Is the copy multibody after all (the step that made the second body stopped the run), they
        /// go on the SheetMetal under the guard as in <see cref="ApplyPending"/>. The lines go to the
        /// first body's report entry; a reason to stop also to the report's general lines.
        /// </summary>
        public static void FinishPending(BuildContext ctx)
        {
            List<PendingBodyOverrides> pending = ctx.Shared.PendingBodyOverrides;
            if (pending.Count == 0) return;
            bool multibody = SwSheetMetal.OfType(ctx.Doc, "SheetMetal").Count > 1;
            var guardNotes = new List<string>();
            string? abort = null;
            var written = new List<(PendingBodyOverrides p, Feature sm, List<string> lines)>();
            if (multibody) written = WriteGuarded(ctx, guardNotes, out abort);
            else
            {
                foreach (PendingBodyOverrides p in pending)
                {
                    if (!(Live(ctx.Doc, p.SheetMetal) is Feature sm)) continue;
                    written.Add((p, sm, !p.Gauge ? WritePartValues(ctx.Doc, sm, p.OwnValues)
                        : new List<string> { "Eigene Blechparameter des Körpers nicht gesetzt: Das Teil hat nur diesen Blechkörper, und seine Werte kommen aus der Blechstärkentabelle." }));
                }
                ctx.Doc.EditRebuild3();
            }
            ReportEntry? firstEntry = null;
            foreach (var (p, sm, lines) in written)
            {
                ReportEntry? entry = ctx.Report.Entries.LastOrDefault(e => e.Feature == p.Flange);
                if (entry == null) continue;
                firstEntry ??= entry;
                if (lines.Count == 0) entry.Details.Add(multibody ? SheetMetalRules.OwnValuesSet(sm.Name, p.Flange) : SheetMetalRules.OwnValuesAtEnd);
                entry.Details.AddRange(lines);
                if (lines.Count > 0 && entry.Status == ReportStatus.Ok) entry.Status = ReportStatus.Warning;
            }
            if (abort != null) guardNotes.Add(abort);
            if (firstEntry != null && guardNotes.Count > 0)
            {
                firstEntry.Details.AddRange(guardNotes);
                if (firstEntry.Status == ReportStatus.Ok) firstEntry.Status = ReportStatus.Warning;
            }
            else ctx.Report.General.AddRange(guardNotes);
            if (abort != null)
            {
                ctx.Report.Aborted = true;
                if (firstEntry != null) ctx.Report.General.Add(abort);
            }
            pending.Clear();
        }

        /// <summary>
        /// Writes the waiting overrides on their SheetMetals under the guard (A1 P1), as every write
        /// that may touch the defaults: the snapshot leaves out the SheetMetals being written and,
        /// through its flange, their body – they change by design –; the defaults, every other
        /// SheetMetal and the other bodies are compared and written back. If the guard wrote back,
        /// the written SheetMetals are read back once more. Returns each written one with its lines
        /// (empty: set and read back); the guard's notes go to <paramref name="guardNotes"/>,
        /// <paramref name="abort"/> is its reason to stop the run, or null.
        /// </summary>
        private static List<(PendingBodyOverrides p, Feature sm, List<string> lines)> WriteGuarded(BuildContext ctx, List<string> guardNotes, out string? abort)
        {
            abort = null;
            var live = new List<(PendingBodyOverrides p, Feature sm)>();
            foreach (PendingBodyOverrides p in ctx.Shared.PendingBodyOverrides)
                if (Live(ctx.Doc, p.SheetMetal) is Feature sm)
                    live.Add((p, sm));
            SheetMetalGuard.State? before = ctx.Shared.Guard?.Take();
            Feature? own = null;
            foreach (var (p, sm) in live)
            {
                before?.SheetMetals.Remove(sm.Name);
                if (own == null && ctx.Created.TryGetValue(p.Flange, out Feature flange)) own = flange;
            }
            var written = live.Select(x => (p: x.p, sm: x.sm, lines: SwSheetMetal.Apply(ctx.Doc, x.sm, x.p.Body, true, x.p.Groups))).ToList();
            ctx.Doc.EditRebuild3();
            if (before == null) return written;
            int had = guardNotes.Count;
            abort = ctx.Shared.Guard!.Check(before, guardNotes, own);
            // Written back: the defaults or another SheetMetal may have taken the new values with them.
            if (abort != null || guardNotes.Count > had)
                foreach (var (p, sm, lines) in written)
                    foreach (string line in SwSheetMetal.Compare(ctx.Doc, sm, p.Body, true, p.Groups.ToList()))
                        if (!lines.Contains(line)) lines.Add(line);
            return written;
        }

        /// <summary>The override groups the body of this SheetMetal still waits for (B15); null if it waits for none.</summary>
        private static IReadOnlyList<int>? WaitingGroups(BuildContext ctx, Feature? sm)
        {
            string? name = sm == null ? null : NameOf(sm);
            return name == null ? null : ctx.Shared.PendingBodyOverrides.FirstOrDefault(p => NameOf(p.SheetMetal) == name)?.Groups;
        }

        private static string? NameOf(Feature f)
        {
            try
            {
                return f.Name;
            }
            catch (Exception)
            {
                // Deleted with its flange.
                return null;
            }
        }

        /// <summary>The feature if it is still in the document.</summary>
        private static Feature? Live(ModelDoc2 doc, Feature f)
        {
            try
            {
                return SwDoc.FeatureByName(doc, f.Name);
            }
            catch (Exception)
            {
                // Deleted with its flange.
                return null;
            }
        }

        private static SheetMetalParameters? TryRead(ModelDoc2 doc, Feature f, bool bodyLevel)
        {
            try
            {
                return SwSheetMetal.ReadParameters(doc, f, bodyLevel);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static SheetMetalParameters WithoutGauge(SheetMetalParameters p) => new SheetMetalParameters
        {
            Thickness = p.Thickness,
            BendRadius = p.BendRadius,
            BendAllowance = p.BendAllowance,
            Relief = p.Relief,
            UseMaterialParameters = p.UseMaterialParameters == null ? (bool?)null : false,
        };

        /// <summary>
        /// The SheetMetal of the body a tab joins: the copy of the shared one; in the same part with
        /// selected features, where a tab may join the original's body, the original's itself; else
        /// the only one in the target.
        /// </summary>
        private static Feature? TabSheetMetal(BuildContext ctx, BaseFlangeRecord r)
        {
            Feature? sm = r.TabOfSheetMetal != null && ctx.Created.TryGetValue(r.TabOfSheetMetal, out Feature copy) ? copy : null;
            if (sm == null && ctx.SamePart && !ctx.SeparateBody && r.TabOfSheetMetal != null) sm = SwDoc.FeatureByName(ctx.Doc, r.TabOfSheetMetal);
            if (sm == null)
            {
                List<Feature> all = SwSheetMetal.OfType(ctx.Doc, "SheetMetal");
                if (all.Count == 1) sm = all[0];
            }
            return sm;
        }

        /// <summary>Newer versions: OverrideDefaultSheetMetalParameters on the flange, then its thickness and radius – only if the switch took.</summary>
        private static List<string> OverrideOnFlange(ModelDoc2 doc, Feature f, BaseFlangeRecord r)
        {
            var notes = new List<string>();
            bool took = false;
            bool ok = FeatureEdit.BaseFlange(doc, f, d =>
            {
                if (!SwLate.Set(d, "OverrideDefaultSheetMetalParameters", true) || !(SwLate.Get(d, "OverrideDefaultSheetMetalParameters") is bool on && on)) return;
                took = true;
                d.Thickness = r.Thickness;
                d.BendRadius = r.BendRadius;
            });
            if (!took || !ok) notes.Add("Eigene Blechdicke und eigener Biegeradius der Basislasche ließen sich nicht über die Standardwerte setzen.");
            return notes;
        }

        /// <summary>
        /// Reads the flange back under its own selection access (no capture, no rollback) and
        /// names what differs from the copy's intended values.
        /// </summary>
        /// <param name="thickness">The thickness the flange should have now: the original's, or its body's while that waits for its own (B15).</param>
        /// <param name="radius">The bend radius, likewise.</param>
        private static List<string> ReadBack(ModelDoc2 doc, Feature f, BaseFlangeRecord r, double thickness, double radius, bool revDir, bool revThk, List<SideTarget> sides)
        {
            var d = (BaseFlangeFeatureData)(object)f.GetDefinition();
            if (d == null || !d.AccessSelections(doc, null)) return new List<string> { "Basislasche ließ sich nicht zurücklesen." };
            var values = new List<(string, double, double, ScalarKind)>();
            try
            {
                values.Add(("Blechdicke", thickness, d.Thickness, ScalarKind.Length));
                values.Add(("Biegeradius", radius, d.BendRadius, ScalarKind.Length));
                values.Add(("Richtung umkehren", revDir ? 1 : 0, d.ReverseDirection ? 1 : 0, ScalarKind.Flag));
                if (!r.ClosedProfile)
                {
                    values.Add(("Materialseite umkehren", revThk ? 1 : 0, d.ReverseThickness ? 1 : 0, ScalarKind.Flag));
                    values.Add(("Richtungen", r.OffsetDirections, d.OffsetDirections, ScalarKind.Choice));
                    foreach (SideTarget s in sides)
                    {
                        int type = SwLate.Get(d, $"D{s.Which}EndConditionType") is int t ? t : s.Which == 1 ? d.D1OffsetType : d.D2OffsetType;
                        values.Add(($"Endbedingung Richtung {s.Which}", SheetMetalRules.ToFlangeOffset(s.End) ?? 0, type, ScalarKind.Choice));
                        if (s.End == EndCondition.Blind || s.End == EndCondition.MidPlane)
                        {
                            double dist = SwLate.Get(d, $"D{s.Which}EndConditionDistance") is double x ? x : s.Which == 1 ? d.D1OffsetDistance : d.D2OffsetDistance;
                            values.Add(($"Tiefe Richtung {s.Which}", s.Distance, dist, ScalarKind.Length));
                        }
                    }
                }
            }
            finally
            {
                d.ReleaseSelectionAccess();
            }
            return SheetMetalCompare.Scalars(values).Select(x => $"Basislasche: {x}").ToList();
        }

        /// <summary>
        /// Separate body: the original's bodies after an attempt – every body that is not the job's
        /// and holds none of the attempt's features – weigh what they weighed before. A flange that
        /// merged with the original makes its body one of the attempt's, and the rest lighter.
        /// </summary>
        private static bool OriginalAsBefore(BuildContext ctx, List<Feature> made, MassState before)
        {
            var names = new HashSet<string>(made.Select(m => m.Name), StringComparer.Ordinal);
            List<Body2> original = SwDoc.SolidBodies(ctx.Doc)
                .Where(b => !ctx.IsOwnBody(b) && !((object)b.GetFeatures() is object[] fs && fs.OfType<Feature>().Any(f => names.Contains(f.Name))))
                .ToList();
            MassState after = SwDoc.MassOf(original);
            return MassComparison.Compare(MassState.Empty, after - before, before).Passes;
        }

        // ---- removal

        /// <summary>Everything the step made since the snapshot; what stays is said.</summary>
        private static void RemoveMade(BuildContext ctx, HashSet<string> before, List<string> notes)
        {
            List<string> left = SwSheetMetal.RemoveNew(ctx.Doc, before);
            ctx.Topology.Invalidate();
            if (left.Count > 0) notes.Add($"Nicht wieder entfernt: {string.Join(", ", left.Select(n => $"„{n}“"))} – bitte prüfen und von Hand löschen.");
        }

        /// <summary>
        /// The companions of a flange removed again (the flange itself is deleted first): the flat
        /// pattern with its own sketches, then the SheetMetal. A template folder stays.
        /// </summary>
        private static void RemoveCompanions(BuildContext ctx, Feature? flatPattern, Feature? sheetMetal)
        {
            foreach (var (feature, absorbed) in new[] { (flatPattern, true), (sheetMetal, false) })
            {
                if (feature == null) continue;
                string name;
                try
                {
                    name = feature.Name;
                }
                catch (Exception)
                {
                    // Gone with the flange.
                    continue;
                }
                if (SwDoc.FeatureByName(ctx.Doc, name) is Feature live) Delete(ctx, live, absorbed);
            }
        }

        // ---- variants for the check

        private static void AddAlternatives(BuildResult result, BuildContext ctx, Feature f, BaseFlangeRecord r, List<SideTarget> sides)
        {
            ModelDoc2 doc = ctx.Doc;
            bool midPlane = sides.Any(s => s.End == EndCondition.MidPlane);
            // In the same part no thickness direction for a closed profile: no effect, and it would write the shared values (A1 P1).
            foreach (var (toggle, label) in SheetMetalRules.BaseFlangeToggles(r.ClosedProfile, r.SymmetricThickness, midPlane, ctx.SamePart))
                result.Alternatives.Add(new Alternative(label, toggle == BaseFlangeToggle.ReverseDirection
                    ? () => FeatureEdit.BaseFlange(doc, f, d => d.ReverseDirection = !d.ReverseDirection)
                    : (Func<bool>)(() => FeatureEdit.BaseFlange(doc, f, d => d.ReverseThickness = !d.ReverseThickness))));
        }

        private static string Describe(BaseFlangeRecord r, bool directionFlipped, bool sideFlipped)
        {
            string what = r.MergesIntoExisting ? "Lasche" : "Basislasche";
            string profile = r.ClosedProfile ? "geschlossenes Profil" : "offenes Profil";
            string text = $"{what}, {profile}, Dicke {(r.Thickness * 1000).ToString("0.###", System.Globalization.CultureInfo.CurrentCulture)} mm";
            if (directionFlipped) text += ", Richtungsflag umgekehrt (Skizzennormale entgegengesetzt)";
            if (sideFlipped) text += ", Materialseite der Kontur gewechselt (gespiegelt)";
            return text;
        }
    }
}
