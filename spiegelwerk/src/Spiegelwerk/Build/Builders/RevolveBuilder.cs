using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Revolve and Revolve-Cut (also thin). A mirror turns the sense of rotation around, and
    /// how SOLIDWORKS picks the default side of a revolve is not documented; the first guess
    /// follows the sketch normal like an extrude, the verifier corrects it if needed.
    ///
    /// The axis is selected with mark 16 (newer official examples), then 4 (the reference);
    /// selected contours and sketch regions go the ways of ProfileRules.Ways like those of an
    /// extrude, never with mark 4 together with an axis at 4 (ProfileSupport).
    /// </summary>
    internal sealed class RevolveBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is RevolveRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (RevolveRecord)record;
            if (!ctx.Sketches.TryGetValue(r.SketchName, out SketchMap sketch))
                return BuildResult.Failed($"Skizze \"{r.SketchName}\" wurde nicht neu aufgebaut.");

            var notes = new List<string>();

            // Separate body: as for extrusions, never let the copy touch the original. Checked
            // first, so a refused revolve leaves no helper axis behind.
            string? refused = PlanSeparateBody(ctx, r.IsCut, r.Merge, out bool scope, out bool createMerge);
            if (refused != null) return Fail(refused, notes);
            refused = ProfileSupport.Plan(ctx, sketch, r, notes);
            if (refused != null) return Fail(refused, notes);

            object? axis = Find(ctx, r.Axis, notes);
            if (axis == null)
                return Fail("Drehachse im Ziel nicht gefunden.", notes);

            (bool singleDir, EndCondition dir1, double a1, EndCondition dir2, double a2) = r.Layout();
            bool reverse = DirectionRules.ReverseFlag(r.Reverse, sketch.Sigma);

            ModelDoc2 doc = ctx.Doc;
            bool wanted = ProfileSupport.Wanted(r);
            ProfileOutcome made = ProfileSupport.Create(ctx, sketch, r, true, wanted, (way, profile) =>
                Create(ctx, r, sketch, way, profile, axis, singleDir, reverse, dir1, a1, dir2, a2, createMerge));
            Feature? f = made.Feature;
            if (f == null) return Fail("SOLIDWORKS hat das Feature nicht erzeugt.", notes);
            string? profileRefused = ProfileSupport.Finish(ctx, made, wanted, notes);
            if (profileRefused != null)
            {
                Delete(ctx, f);
                return Fail(profileRefused, notes);
            }

            if (scope)
            {
                bool ok = FeatureEdit.Revolve(doc, f, d =>
                {
                    if (!r.IsCut) d.Merge = true;
                    d.FeatureScope = true;
                    d.AutoSelect = false;
                    d.FeatureScopeBodies = OwnBodiesBeforeFeature(ctx);
                });
                if (!ok)
                {
                    Delete(ctx, f);
                    return Fail("Der Feature-Bereich ließ sich nicht setzen; das Feature wurde wieder entfernt.", notes);
                }
            }

            ctx.Register(r.Name, f);
            var result = BuildResult.Ok(f, (r.IsCut ? "Rotationsschnitt" : "Rotation") + $", {a1 * 180 / Math.PI:0.##}°" + ProfileSupport.Describe(made));
            result.Notes.AddRange(notes);
            if (notes.Count > 0) result.Status = ReportStatus.Warning;
            if (r.DirectionMatters)
                result.Alternatives.Add(new Alternative("Drehrichtung umgekehrt",
                    () => FeatureEdit.Revolve(doc, f, d => d.ReverseDirection = !d.ReverseDirection)));
            if (r.IsThin && IsThinOneSided(r.ThinType))
                result.Alternatives.Add(new Alternative("Wandseite gewechselt",
                    () => FeatureEdit.Revolve(doc, f, d => d.ThinWallType = OtherThinSide(d.ThinWallType))));
            if (r.IsThin && r.ThinType == (int)swThinWallType_e.swThinWallTwoDirection && Math.Abs(r.Thickness1 - r.Thickness2) > 1e-12)
                result.Alternatives.Add(new Alternative("Wandstärken getauscht", () => FeatureEdit.Revolve(doc, f, d =>
                {
                    double a = d.GetWallThickness(true), b = d.GetWallThickness(false);
                    d.SetWallThickness(true, b);
                    d.SetWallThickness(false, a);
                })));
            return result;
        }

        /// <summary>
        /// Creates the revolve with the selection of one profile way (<see cref="ProfileSupport.Select"/>)
        /// and the axis with the way's mark. Null if a selection failed or SOLIDWORKS made nothing.
        /// </summary>
        private static Feature? Create(BuildContext ctx, RevolveRecord r, SketchMap sketch, ProfileWay way, Profile? profile, object axis,
            bool singleDir, bool reverse, EndCondition dir1, double a1, EndCondition dir2, double a2, bool merge)
        {
            ModelDoc2 doc = ctx.Doc;
            using (way.ContourSelection && profile != null ? new ContourSelectionScope(doc) : null)
            {
                Feature? f = null;
                if (ProfileSupport.Select(ctx, sketch, profile, way) && SwSelect.Select(doc, axis, way.AxisMark, true))
                    f = doc.FeatureManager.FeatureRevolve2(singleDir, true, r.IsThin, r.IsCut, reverse, false,
                        (int)dir1, (int)dir2, a1, a2, false, false, 0, 0,
                        r.ThinType, r.Thickness1, r.Thickness2, merge, false, true);
                SwSelect.Clear(doc);
                return f;
            }
        }
    }
}
