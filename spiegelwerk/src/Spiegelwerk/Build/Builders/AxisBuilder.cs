using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// A reference axis of the original becomes an axis at the mirrored location on a fixed
    /// helper line – independent of the original and of the bodies. Later features that used
    /// the original axis (revolve, circular pattern, extrude direction) find it by geometry.
    /// </summary>
    internal sealed class AxisBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is RefAxisRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var rec = (RefAxisRecord)record;
            Feature f = ctx.Planes.CreateNamedAxis(ctx.Mirror.Apply(rec.Axis), ctx.NameFor(rec.Name));
            ctx.Register(rec.Name, f);
            return BuildResult.Ok(f, "Achse an gespiegelter Lage, auf fixierter Hilfslinie");
        }
    }
}
