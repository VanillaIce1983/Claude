using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;

namespace Spiegelwerk.Build.Builders
{
    /// <summary>
    /// Sketches that belong to a feature which brings its own (the position and profile sketch
    /// of a Hole Wizard hole, the sketches of an Advanced Hole): nothing to build here, the
    /// owner takes care of them. A part after its owner (the slot of a tab and slot, the bends of
    /// legacy Insert Bends) whose owner was not built is not reported as built with it: the owner's
    /// withdrawal (<see cref="StepRunner.WithdrawParts"/>) came before this entry existed.
    /// </summary>
    internal sealed class PartOfBuilder : IFeatureBuilder
    {
        public bool Handles(FeatureRecord record) => record is PartOfFeatureRecord;

        public BuildResult Build(FeatureRecord record, BuildContext ctx)
        {
            string owner = ((PartOfFeatureRecord)record).Owner;
            return ctx.OwnersNotBuilt.Contains(owner)
                ? BuildResult.Skipped(RunSteps.PartOf(owner, ownerNotBuilt: true))
                : BuildResult.AlreadyThere(RunSteps.PartOf(owner, ownerNotBuilt: false));
        }
    }
}
