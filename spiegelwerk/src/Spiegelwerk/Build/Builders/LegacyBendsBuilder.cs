using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Legacy Insert Bends (A1 §8 phase 1b, blech-plan S6): SheetMetal, FlattenBends and ProcessBends in one
    /// InsertBends2 on the solid the run built before, with the mirrored fixed face (or edge) selected – mark 0,
    /// then 1 (B52) –; what is new since the snapshot tells whether it worked (<see cref="SwSheetMetal.InsertBends"/>,
    /// also fallback route B, S7a). Nothing has a side: radius, allowance, relief and offset ratio go over unchanged.
    ///
    /// Values as for a base flange (<see cref="BaseFlangeBuilder"/>): the first sheet-metal body of a part makes the
    /// part's values, every later one is created with what the target has and gets its own values through the
    /// overrides of its SheetMetal (<see cref="SheetMetalRules.LegacyOverrides"/>); the first body's own overrides
    /// wait for the second body. An allowance InsertBends2 does not take (deduction, calculation table, a bend
    /// table that is not there) is written onto the SheetMetal afterwards. What FlattenBends and ProcessBends
    /// have of their own goes on in an edit. It runs under the guard of the shared values; the bends features
    /// are companions (rebuild errors, dimension names), removed again with it.
    /// </summary>
    internal sealed class LegacyBendsBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is LegacyBendsRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (LegacyBendsRecord)record;
            ModelDoc2 doc = ctx.Doc;
            var notes = new List<string>();
            var info = new List<string>();

            // ---- the fixed face (or edge) on the solid the run built; separate body: on a mirrored one
            if (r.FixedFace == null) return Fail(SheetMetalRules.LegacyFixedFaceUnreadable, notes);
            object? found = r.FixedFace.Kind == GeometryRefKind.Face
                ? ctx.Topology.FindFace(r.FixedFace.Face!.Mirror(ctx.Mirror), notes)
                : (object?)ctx.Topology.FindEdge(r.FixedFace.Edge!.Mirror(ctx.Mirror), notes);
            if (found == null) return Fail(SheetMetalRules.LegacyFixedFaceMissing, notes);
            if (ctx.SeparateBody && !CornerSupport.OnOwnBody(ctx, found)) return Fail(SheetMetalRules.LegacyFixedFaceNotOwn, notes);
            object fixedEntity = Safe(found)!;

            // ---- values: the first body makes the part's, later ones are created with the target's
            SheetMetalSnapshot before = SwSheetMetal.Snapshot(doc);
            Feature? targetTemplate = SwSheetMetal.Template(doc);
            bool first = targetTemplate == null && SwSheetMetal.OfType(doc, "SheetMetal").Count == 0;
            SheetMetalParameters? template = targetTemplate == null ? null : TryRead(doc, targetTemplate, false);
            IReadOnlyList<int> overrides = first ? SheetMetalRules.BodyOverrides(r.Body) : SheetMetalRules.LegacyOverrides(r.Body, template, r.Defaults);
            BodyOverrideMode mode = SheetMetalRules.BodyOverrideModeOf(first, false, overrides.Count, ctx.Shared.SingleSheetMetalBody);
            SheetMetalParameters? partValues = mode == BodyOverrideMode.PartValues ? SheetMetalRules.OwnValues(r.Defaults, r.Body) : r.Defaults;
            SheetMetalParameters create = SheetMetalRules.LegacyCreationValues(first, partValues, r.Body, template, r.Defaults);
            if (r.Body.UseMaterialParameters == true) notes.Add(SheetMetalRules.MaterialNoteFor(ctx.SamePart));
            if (r.Body.UseGaugeTable)
                notes.Add($"Blechstärkentabelle „{r.Body.GaugeTableFile}“: „Biegungen einfügen“ nimmt keine; die Werte werden fest übernommen.");

            double ratio = SheetMetalRules.LegacyOffsetRatio(create.Relief);
            BendAllowanceData allowance = create.BendAllowance;
            InsertBendsArguments? Takes(BendAllowanceData a) =>
                a.Kind != BendAllowanceKind.BendTable || File.Exists(a.BendTableFile) ? SheetMetalRules.LegacyArguments(a, ratio) : null;
            InsertBendsArguments? args = Takes(allowance);
            bool allowanceLater = args == null;
            args ??= SheetMetalRules.LegacyPlaceholder(allowance, ratio);
            if (args == null) return Fail(SheetMetalRules.LegacyRatioInvalid(ratio), notes);
            bool flatten = r.FlattenBendsName != null || r.ProcessBendsName != null;

            // ---- create
            var tried = new List<string>();
            List<Feature> made;
            try
            {
                made = SwSheetMetal.InsertBends(doc, fixedEntity, create.BendRadius, args, create.Relief.UseAutoRelief, flatten, before.Names, tried);
            }
            finally
            {
                ctx.Topology.Invalidate();
            }
            if (made.Count == 0) return Fail(SheetMetalRules.LegacyNotCreated(tried), notes);
            if (tried.Count > 0) info.Add($"„Biegungen einfügen“ im zweiten Versuch angelegt ({string.Join("; ", tried)}).");
            Feature sm = made.First(f => f.GetTypeName2() == "SheetMetal");
            Feature? flattenBends = made.FirstOrDefault(f => f.GetTypeName2() == "FlattenBends");
            Feature? processBends = made.FirstOrDefault(f => f.GetTypeName2() == "ProcessBends");
            Feature? flatPattern = made.FirstOrDefault(f => f.GetTypeName2() == "FlatPattern");

            // ---- parameters on the SheetMetal (or the part's defaults)
            if (allowanceLater)
            {
                // What goes on afterwards: the created one, or – a later body created with the target's defaults – its own.
                BendAllowanceData written = template == null ? allowance : SheetMetalRules.BodyValues(r.Defaults, r.Body).BendAllowance;
                info.Add(template != null && Takes(written) != null
                    ? SheetMetalRules.LegacyTargetAllowanceLater(allowance, written)
                    : SheetMetalRules.LegacyAllowanceLater(written));
            }
            if (first)
            {
                notes.AddRange(BaseFlangeBuilder.WritePartValues(doc, sm, partValues ?? r.Body));
                if (mode == BodyOverrideMode.PartValues) info.Add(SheetMetalRules.OwnValuesAsPartValues);
                if (mode == BodyOverrideMode.Deferred)
                {
                    // Set by StepRunner after the step that makes the second body, outside its check and guard (B15).
                    ctx.Shared.PendingBodyOverrides.Add(new PendingBodyOverrides(r.Name, sm, r.Body, overrides, SheetMetalRules.OwnValues(r.Defaults, r.Body), false));
                    info.Add(SheetMetalRules.OwnValuesDeferred);
                }
            }
            else
            {
                // A placeholder allowance must not stay: the group goes on with the others.
                List<int> groups = overrides.ToList();
                if (allowanceLater && !groups.Contains(1)) groups.Add(1);
                groups.Sort();
                if (groups.Count > 0) notes.AddRange(SwSheetMetal.Apply(doc, sm, SheetMetalRules.BodyValues(r.Defaults, r.Body), true, groups));
            }
            doc.EditRebuild3();
            ctx.Topology.Invalidate();

            // ---- what FlattenBends and ProcessBends have of their own, and the fixed face as SOLIDWORKS took it
            OwnBendsValues(ctx, flattenBends, r.FlattenBendsValues, notes);
            OwnBendsValues(ctx, processBends, r.ProcessBendsValues, notes);
            if (flattenBends != null && r.FixedFace.Kind == GeometryRefKind.Face &&
                SwSheetMetal.SameLegacyFixedFace(doc, flattenBends, r.FixedFace.Face!.Mirror(ctx.Mirror)) == false)
                notes.Add("Feste Fläche von „Biegungen einfügen“: SOLIDWORKS hat eine andere Fläche genommen als die gespiegelte.");

            // ---- registration: the SheetMetal for the record, the bends features under their names, the body for a flat pattern
            ctx.Register(r.Name, sm);
            if (flattenBends != null && r.FlattenBendsName != null) ctx.Register(r.FlattenBendsName, flattenBends);
            if (processBends != null && r.ProcessBendsName != null) ctx.Register(r.ProcessBendsName, processBends);
            // No sketch: no σ of a base flange sketch (0).
            ctx.SheetMetalBodies[r.Name] = new SheetMetalBody(sm, flatPattern, 0);

            BuildResult result = Ok(sm, SheetMetalRules.DescribeLegacy(r), notes);
            result.Info.AddRange(info);
            if (flattenBends != null && r.FlattenBendsName != null) result.Companions.Add(new Companion(flattenBends, r.FlattenBendsName, r.FlattenBendsDimensions));
            if (processBends != null && r.ProcessBendsName != null) result.Companions.Add(new Companion(processBends, r.ProcessBendsName, r.ProcessBendsDimensions));
            // Removed again: the SheetMetal goes first (StepRunner), then whatever the step made since the snapshot – children first.
            HashSet<string> snapshot = before.Names;
            result.RemoveHelpers = () => FlangeSupport.RemoveMade(ctx, snapshot);
            return result;
        }

        /// <summary>
        /// FlattenBends or ProcessBends of the copy get what the original's have of their own (radius, allowance – not
        /// the SheetMetal's) in one edit, read back; what still differs becomes a note. Nothing to do if the original
        /// uses the SheetMetal's values.
        /// </summary>
        private static void OwnBendsValues(BuildContext ctx, Feature? copy, BendValues? want, List<string> notes)
        {
            if (copy == null || !SheetMetalRules.HasOwnValues(want)) return;
            ModelDoc2 doc = ctx.Doc;
            BendValues? got = SwSheetMetal.ReadLegacyBends(doc, copy, null, out _);
            if (got != null && SheetMetalRules.LegacyBendsDifferences(want!, got).Count == 0) return;
            bool edited = FeatureEdit.Bends(doc, copy, d =>
            {
                d.UseDefaultBendRadius = want!.UseDefaultRadius;
                if (!want.UseDefaultRadius) d.BendRadius = want.Radius;
                d.UseDefaultBendAllowance = want.UseDefaultAllowance;
                if (!want.UseDefaultAllowance && want.Allowance != null && (object)d.GetCustomBendAllowance() is CustomBendAllowance cba)
                {
                    SwSheetMetal.Fill(cba, want.Allowance);
                    d.SetCustomBendAllowance(cba);
                }
            });
            doc.EditRebuild3();
            ctx.Topology.Invalidate();
            got = SwSheetMetal.ReadLegacyBends(doc, copy, null, out _);
            string label = copy.Name;
            if (!edited) notes.Add($"Eigene Werte von „{label}“ ließen sich nicht setzen.");
            if (got == null) notes.Add($"„{label}“ ließ sich nicht zurücklesen.");
            else notes.AddRange(SheetMetalRules.LegacyBendsDifferences(want!, got).Select(x => $"„{label}“: {x}"));
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
    }
}
