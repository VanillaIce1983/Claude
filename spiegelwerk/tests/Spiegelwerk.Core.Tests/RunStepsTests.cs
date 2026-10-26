using Spiegelwerk.Core.Planning;
using Spiegelwerk.Core.Records;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>Progress bar steps, when Esc can still stop the run, and the report notes for it.</summary>
    public class RunStepsTests
    {
        [Theory]
        [InlineData(5, true, 15)]
        [InlineData(5, false, 10)]
        [InlineData(0, true, 0)]
        public void One_step_per_feature_and_phase(int features, bool measure, int total)
        {
            Assert.Equal(total, new RunSteps(features, measure).Total);
        }

        [Fact]
        public void Esc_after_the_last_step_stops_nothing()
        {
            var steps = new RunSteps(2, false);
            Assert.True(steps.Advance());
            Assert.True(steps.Advance());
            Assert.True(steps.Advance());
            Assert.False(steps.Advance());
            Assert.Equal(4, steps.Position);
        }

        [Fact]
        public void Advance_does_not_run_past_the_end()
        {
            var steps = new RunSteps(1, false);
            steps.Advance();
            steps.Advance();
            Assert.False(steps.Advance());
            Assert.Equal(2, steps.Position);
            Assert.False(new RunSteps(0, true).Advance());
        }

        [Fact]
        public void Title_names_phase_and_feature()
        {
            Assert.Equal("Spiegelwerk – Aufbauen: Austragung1", RunSteps.Title(RunSteps.Build, "Austragung1"));
            Assert.Equal("Spiegelwerk – Abschließen", RunSteps.Title(RunSteps.Finish, ""));
            Assert.Equal("Spiegelwerk", RunSteps.Title("", ""));
        }

        [Fact]
        public void Cancel_notes_say_what_was_not_built()
        {
            Assert.Equal("Mit Esc abgebrochen beim Vermessen des Originals; es wurde nichts aufgebaut.", RunSteps.CancelledBeforeBuild(RunSteps.Measure));
            Assert.Equal("Mit Esc abgebrochen nach „Verrundung1“; 3 weitere Features wurden nicht mehr aufgebaut.", RunSteps.CancelledInBuild("Verrundung1", 3));
            Assert.Equal("Mit Esc abgebrochen nach „Verrundung1“; 1 weiteres Feature wurde nicht mehr aufgebaut.", RunSteps.CancelledInBuild("Verrundung1", 1));
            Assert.Equal("Mit Esc abgebrochen nach „Verrundung1“.", RunSteps.CancelledInBuild("Verrundung1", 0));
        }

        [Fact]
        public void A_cancelled_component_cancels_the_assembly_report()
        {
            var asm = new MirrorReport();
            asm.Append(new MirrorReport { Aborted = true }, "Halter-1");
            Assert.True(asm.Aborted);
            Assert.False(asm.Cancelled);
            asm.Append(new MirrorReport { Aborted = true, Cancelled = true }, "Lasche-1");
            Assert.True(asm.Cancelled);
        }
    }
}
