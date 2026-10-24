using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Revolve and Revolve-Cut (also thin). A mirror turns the sense of rotation around, and
    /// how SOLIDWORKS picks the default side of a revolve is not documented; the first guess
    /// follows the sketch normal like an extrude, the verifier corrects it if needed.
    /// </summary>
    internal sealed class RevolveBuilder : IFeatureBuilder
    {
        /// <summary>
        /// Selection mark for the axis: the reference documents 4, newer official examples use 16.
        /// Both are tried.
        /// </summary>
        private static readonly int[] AxisMarks = { 16, 4 };

        public bool Handles(FeatureRecord record) => record is RevolveRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (RevolveRecord)record;
            if (!ctx.Sketches.TryGetValue(r.SketchName, out SketchMap sketch))
                return BuildResult.Failed($"Skizze \"{r.SketchName}\" wurde nicht neu aufgebaut.");

            var notes = new List<string>();
            object? axis = r.Axis == null ? null : ctx.Topology.Resolve(r.Axis.Mirror(ctx.Mirror), notes);
            if (axis == null)
            {
                var failed = BuildResult.Failed("Drehachse im Ziel nicht gefunden (nur Skizzenlinien und gerade Kanten werden zugeordnet).");
                failed.Notes.AddRange(notes);
                return failed;
            }

            (bool singleDir, EndCondition dir1, double a1, EndCondition dir2, double a2) = Layout(r);
            bool reverse = DirectionRules.ReverseFlag(r.Reverse, sketch.Sigma);
            bool merge = r.Merge && !(ctx.SeparateBody && !r.IsCut && ctx.OwnBodies().Count == 0);

            ModelDoc2 doc = ctx.Doc;
            Feature? f = null;
            foreach (int mark in AxisMarks)
            {
                SwSelect.Clear(doc);
                SwSelect.Select(doc, sketch.Feature, 0, false);
                SwSelect.Select(doc, axis, mark, true);
                f = doc.FeatureManager.FeatureRevolve2(singleDir, true, r.IsThin, r.IsCut, reverse, false,
                    (int)dir1, (int)dir2, a1, a2, false, false, 0, 0,
                    r.ThinType, r.Thickness1, r.Thickness2, merge, false, true);
                SwSelect.Clear(doc);
                if (f != null) break;
            }
            if (f == null)
            {
                var failed = BuildResult.Failed("SOLIDWORKS hat das Feature nicht erzeugt.");
                failed.Notes.AddRange(notes);
                return failed;
            }

            if (ctx.SeparateBody && (r.IsCut || r.Merge))
            {
                var own = ctx.OwnBodies();
                if (own.Count > 0 && !FeatureEdit.Revolve(doc, f, d =>
                    {
                        d.FeatureScope = true;
                        d.AutoSelect = false;
                        d.FeatureScopeBodies = own.ConvertAll(b => (object)b).ToArray();
                    }))
                    notes.Add("Feature-Bereich konnte nicht auf die gespiegelten Körper beschränkt werden.");
            }

            ctx.Register(r.Name, f);
            var result = BuildResult.Ok(f, (r.IsCut ? "Rotationsschnitt" : "Rotation") + $", {a1 * 180 / Math.PI:0.##}°");
            result.Notes.AddRange(notes);
            if (notes.Count > 0) result.Status = ReportStatus.Warning;
            if (r.DirectionMatters)
                result.Alternatives.Add(new Alternative("Drehrichtung umgekehrt",
                    () => FeatureEdit.Revolve(doc, f, d => d.ReverseDirection = !d.ReverseDirection)));
            if (r.IsThin && (r.ThinType == (int)swThinWallType_e.swThinWallOneDirection || r.ThinType == (int)swThinWallType_e.swThinWallOppDirection))
                result.Alternatives.Add(new Alternative("Wandseite gewechselt",
                    () => FeatureEdit.Revolve(doc, f, d => d.ThinWallType = d.ThinWallType == (int)swThinWallType_e.swThinWallOneDirection
                        ? (int)swThinWallType_e.swThinWallOppDirection
                        : (int)swThinWallType_e.swThinWallOneDirection)));
            return result;
        }

        /// <summary>swRevolveType_e → parameters of FeatureRevolve2.</summary>
        private static (bool singleDir, EndCondition dir1, double a1, EndCondition dir2, double a2) Layout(RevolveRecord r)
        {
            const double full = 2 * Math.PI;
            switch ((swRevolveType_e)r.RevolveType)
            {
                case swRevolveType_e.swRevolveTypeMidPlane:
                    return (true, EndCondition.MidPlane, r.Angle1, EndCondition.Blind, 0);
                case swRevolveType_e.swRevolveTypeTwoDirection:
                case swRevolveType_e.swRevolveTypeTwoDirection360Degrees:
                    return (false, EndCondition.Blind, r.Angle1, EndCondition.Blind, r.Angle2);
                case swRevolveType_e.swRevolveTypeOneDirection360Degrees:
                    return (true, EndCondition.Blind, full, EndCondition.Blind, 0);
                case swRevolveType_e.swRevolveTypeMidPlane360Degrees:
                    return (true, EndCondition.MidPlane, full, EndCondition.Blind, 0);
                default:
                    return (true, EndCondition.Blind, r.Angle1, EndCondition.Blind, 0);
            }
        }
    }
}
