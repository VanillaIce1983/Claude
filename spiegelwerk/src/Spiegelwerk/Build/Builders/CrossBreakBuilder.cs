using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using Spiegelwerk.Capture;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Matching;
using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Spiegelwerk.Sw;
using static Spiegelwerk.Build.Builders.BuilderSupport;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Cross break (A3 (2) §5.2, P10, P12, blech-plan S5). The mirrored face – separate body: on a mirrored
    /// body, checked before anything is made, because a cross break changes no solid and nothing after it
    /// would notice it on the original –, picked at its mirrored sample (the side does not matter), then
    /// InsertCrossBreak with angle and radius; the direction in an edit where it differs (it counts against
    /// the face's outward normal, which a reflection keeps, B48), everything read back. Its break lines: the
    /// new cross break brings its own sketch; where its lines are not the mirrored ones of the original (the
    /// user edited them), the mirrored sketch is written into it (<see cref="SketchBuilder.ReplaceInto"/>).
    /// Nothing to measure: the direction follows the rule.
    /// </summary>
    internal sealed class CrossBreakBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is CrossBreakRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var r = (CrossBreakRecord)record;
            ModelDoc2 doc = ctx.Doc;
            var notes = new List<string>();
            var info = new List<string>();
            if (r.Face == null) return Fail("Fläche des Querbruchs nicht lesbar.", notes);
            List<Face2>? faces = CornerSupport.ResolveFaces(ctx, new[] { r.Face }, "des Querbruchs", notes, out string? why);
            if (faces == null) return Fail(why!, notes);
            FaceSignature wanted = r.Face.Mirror(ctx.Mirror);

            SheetMetalSnapshot before = SwSheetMetal.Snapshot(doc);
            Feature? cross = null;
            var tried = new List<string>();
            try
            {
                if (SelectFaceAt(ctx, faces[0], wanted.Sample, 0)) cross = doc.FeatureManager.InsertCrossBreak(r.Angle, r.Radius);
                else tried.Add("Die Fläche ließ sich nicht wählen.");
            }
            catch (Exception ex)
            {
                tried.Add($"InsertCrossBreak: {ex.Message}");
            }
            finally
            {
                SwSelect.Clear(doc);
                ctx.Topology.Invalidate();
            }
            cross ??= SwSheetMetal.NewOfType(doc, before.Names, "CrossBreak", d => d is CrossBreakFeatureData);
            if (cross == null)
            {
                notes.AddRange(tried);
                if (!CornerSupport.RemoveMade(ctx, before.Names, notes)) return Fail("SOLIDWORKS hat den Querbruch nicht passend erzeugt.", notes);
                return Fail("SOLIDWORKS hat den Querbruch nicht erzeugt.", notes);
            }

            CrossBreakRecord? got = SwSheetMetal.ReadBackCrossBreak(doc, cross, wanted, out _);
            if (got != null && !CornerRules.Differences(r, got).None)
            {
                if (!FeatureEdit.CrossBreak(doc, cross, d => WriteValues(d, r))) notes.Add("Werte des Querbruchs ließen sich nicht nachträglich setzen.");
                doc.EditRebuild3();
                ctx.Topology.Invalidate();
            }
            CrossBreakRecord? now = SwSheetMetal.ReadBackCrossBreak(doc, cross, wanted, out bool sameFace);
            if (now == null) notes.Add("Der Querbruch ließ sich nicht zurücklesen.");
            else
            {
                notes.AddRange(CornerRules.Differences(r, now).Differences.Select(x => $"Querbruch: {x}"));
                if (!sameFace) notes.Add("Querbruch: Er liegt nicht auf der gespiegelten Fläche.");
            }

            ctx.Register(r.Name, cross);
            WriteLines(ctx, r, cross, notes, info);
            BuildResult result = Ok(cross, CornerRules.Describe(r), notes);
            // The verifier measures nothing here (no solid change): said in the check column.
            result.Verification = "nicht messbar";
            result.Info.Add(CornerRules.CrossBreakByRule);
            result.Info.AddRange(info);
            return result;
        }

        private static void WriteValues(CrossBreakFeatureData d, CrossBreakRecord r)
        {
            d.BreakAngle = r.Angle;
            d.BreakRadius = r.Radius;
            if (d.ReverseDirection != r.Reverse) d.ReverseDirection = r.Reverse;
        }

        /// <summary>
        /// The break lines (P10): the new cross break's own sketch gets the original's name; where its lines are
        /// not the mirrored ones, the mirrored sketch is written into it and the lines are compared again. If the
        /// new cross break has no single sketch, or writing fails, the lines stay as SOLIDWORKS draws them (note).
        /// </summary>
        private static void WriteLines(BuildContext ctx, CrossBreakRecord r, Feature cross, List<string> notes, List<string> info)
        {
            if (r.LineSketchName == null) return;
            List<Feature> native = SwDoc.SubFeatures(cross).Where(SwDoc.IsSketch).ToList();
            if (native.Count != 1)
            {
                notes.Add($"Der neue Querbruch hat {(native.Count == 0 ? "keine" : $"{native.Count}")} Skizzen; die Bruchlinien des Originals werden nicht übertragen.");
                return;
            }
            Feature sketch = native[0];
            List<(Vec3 a, Vec3 b)> wanted = r.Lines.Select(l => (ctx.Mirror.Apply(l.a), ctx.Mirror.Apply(l.b))).ToList();
            List<(Vec3 a, Vec3 b)>? Lines()
            {
                try
                {
                    return CornerRules.BreakLines(new SketchCapture(ctx.Math).Read(SwDoc.FeatureByName(ctx.Doc, sketch.Name) ?? sketch));
                }
                catch (Exception)
                {
                    return null;
                }
            }
            List<(Vec3 a, Vec3 b)>? lines = Lines();
            if (r.LineSketch == null || r.Lines.Count == 0)
            {
                // Not read at capture (the record says so): the new one stays.
                ctx.Register(r.LineSketchName, sketch);
                return;
            }
            if (lines != null && CornerRules.SameLines(wanted, lines))
            {
                ctx.Register(r.LineSketchName, sketch);
                return;
            }
            try
            {
                new SketchBuilder(ctx).ReplaceInto(r.LineSketch, sketch, notes);
                ctx.Register(r.LineSketchName, sketch);
                ctx.Doc.EditRebuild3();
                ctx.Topology.Invalidate();
                lines = Lines();
                if (lines != null && CornerRules.SameLines(wanted, lines)) info.Add("Bruchlinien aus der gespiegelten Skizze übernommen.");
                else notes.Add("Bruchlinien in die Skizze des neuen Querbruchs geschrieben, aber danach nicht als die gespiegelten wiedergefunden.");
            }
            catch (SketchDamagedException ex)
            {
                ctx.Sketches.Remove(r.LineSketchName);
                notes.Add($"Skizze der Bruchlinien ist leer oder unvollständig: Sie ließ sich nicht vollständig leeren oder neu zeichnen ({ex.InnerException?.Message ?? ex.Message}). Querbruch prüfen.");
            }
            catch (Exception ex)
            {
                // As with a hem's width sketch: it stays as SOLIDWORKS made it, under its own name.
                ctx.Sketches.Remove(r.LineSketchName);
                notes.Add(CornerRules.LinesNotWritten(ex.Message));
            }
        }
    }
}
