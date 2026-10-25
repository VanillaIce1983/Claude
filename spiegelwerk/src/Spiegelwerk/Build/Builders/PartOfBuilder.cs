using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Sketches that belong to a feature which brings its own (the position and profile sketch
    /// of a Hole Wizard hole): nothing to build here, the owner takes care of them.
    /// </summary>
    internal sealed class PartOfBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is PartOfFeatureRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx) =>
            BuildResult.AlreadyThere($"Teil von „{((PartOfFeatureRecord)record).Owner}“; entsteht mit diesem.");
    }
}
