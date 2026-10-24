using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Build.Builders
{
    internal sealed class SketchFeatureBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is SketchRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var rec = (SketchRecord)record;
            var notes = new System.Collections.Generic.List<string>();
            SketchMap map = new SketchBuilder(ctx).Build(rec, notes);
            ctx.Register(rec.Name, map.Feature);

            int segments = map.Segments.Count, wanted = rec.Sketch.Segments.Count;
            var result = BuildResult.Ok(map.Feature,
                $"{segments} Elemente, {map.Mirrored.Sketch.Relations.Count} Beziehungen, {map.Mirrored.Sketch.Dimensions.Count} Bemaßungen" +
                (map.Sigma > 0 ? " (Skizze gespiegelt)" : " (Skizze gedreht, Normale umgekehrt)"));
            result.Notes.AddRange(notes);
            if (segments < wanted || notes.Count > 0) result.Status = ReportStatus.Warning;
            return result;
        }
    }
}
