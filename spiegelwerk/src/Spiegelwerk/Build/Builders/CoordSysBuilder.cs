using SolidWorks.Interop.sldworks;
using Spiegelwerk.Core.Geometry;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// A coordinate system of the original becomes one at the mirrored location, on a fixed
    /// helper sketch – independent of the original. Its mirror image would be left-handed;
    /// the copy keeps X and Y mirrored and takes Z = X × Y (the mirrored Z reversed). Points
    /// in its XY plane – the table of a table driven pattern – keep their coordinates.
    /// </summary>
    internal sealed class CoordSysBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is CoordSysRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            var rec = (CoordSysRecord)record;
            Frame3 wanted = ctx.Mirror.ApplyKeepingXY(rec.Frame);
            Feature f = ctx.Planes.CreateNamedCoordinateSystem(wanted, ctx.NameFor(rec.Name));
            ctx.Register(rec.Name, f);
            return BuildResult.Ok(f, "Koordinatensystem an gespiegelter Lage (X und Y gespiegelt, Z = X × Y), auf fixierter Hilfsskizze");
        }
    }
}
