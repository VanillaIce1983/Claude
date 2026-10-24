using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// A reference plane of the original becomes a plane at the mirrored location that only
    /// depends on the standard planes (or on a fixed anchor sketch), never on the original.
    /// </summary>
    internal sealed class RefPlaneBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is RefPlaneRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var rec = (RefPlaneRecord)record;
            Feature? f = ctx.Planes.CreateNamed(ctx.Mirror.Apply(rec.Plane), ctx.NameFor(rec.Name));
            if (f == null) return BuildResult.AlreadyThere("Gespiegelte Lage ist eine Standardebene; keine eigene Ebene nötig.");
            ctx.Register(rec.Name, f);
            return BuildResult.Ok(f, "Ebene an gespiegelter Lage, ohne Bezug zum Original");
        }
    }
}
