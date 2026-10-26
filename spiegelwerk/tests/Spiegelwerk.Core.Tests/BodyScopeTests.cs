using System.Linq;
using Spiegelwerk.Core.Planning;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>Selected bodies: features that also made or changed bodies outside the selection.</summary>
    public class BodyScopeTests
    {
        private static BodyHistory Body(string name, bool selected, params string[] features) => new BodyHistory(name, features, selected);

        [Fact]
        public void Cut_that_split_the_block_keeps_the_other_half_from_being_deleted()
        {
            // Aufsatz1 builds the block, Schnitt1 splits it; only the first half is selected.
            BodyScope scope = BodyScope.Of(
                new[] { Body("Schnitt1[1]", true, "Aufsatz1", "Schnitt1"), Body("Schnitt1[2]", false, "Aufsatz1", "Schnitt1") },
                new[] { "Skizze1", "Aufsatz1", "Skizze2", "Schnitt1" }, new string[0]);

            Assert.Equal(new[] { "Schnitt1[2]" }, scope.UnselectedBodies.Keys);
            Assert.Equal(new[] { "Aufsatz1", "Schnitt1" }, scope.UnselectedBodies["Schnitt1[2]"]);
            // The copy splits its block the same way: both halves come along, the change is comparable.
            Assert.Empty(scope.OtherBodiesChanged);
        }

        [Fact]
        public void Cut_through_a_body_that_is_not_rebuilt_is_not_comparable()
        {
            // Schnitt3 cuts both bodies; the second one's boss is not rebuilt.
            BodyScope scope = BodyScope.Of(
                new[] { Body("Schnitt3", true, "Aufsatz1", "Schnitt3"), Body("Aufsatz2", false, "Aufsatz2", "Schnitt3") },
                new[] { "Aufsatz1", "Schnitt3" }, new string[0]);

            Assert.Equal(new[] { "Schnitt3" }, scope.OtherBodiesChanged.Keys);
            Assert.Equal(new[] { "Aufsatz2" }, scope.OtherBodiesChanged["Schnitt3"]);
            Assert.Equal(new[] { "Schnitt3" }, scope.UnselectedBodies["Aufsatz2"]);
        }

        [Fact]
        public void Every_other_body_is_named_once()
        {
            BodyScope scope = BodyScope.Of(
                new[]
                {
                    Body("A", true, "Aufsatz1", "Schnitt3", "Verrundung1"),
                    Body("B", false, "Aufsatz2", "Schnitt3", "Verrundung1", "Schnitt3"),
                    Body("C", false, "Aufsatz3", "Schnitt3"),
                },
                new[] { "Aufsatz1", "Schnitt3", "Verrundung1" }, new string[0]);

            Assert.Equal(new[] { "B", "C" }, scope.OtherBodiesChanged["Schnitt3"]);
            Assert.Equal(new[] { "B" }, scope.OtherBodiesChanged["Verrundung1"]);
            Assert.False(scope.OtherBodiesChanged.ContainsKey("Aufsatz1"));
        }

        [Fact]
        public void A_feature_not_rebuilt_after_the_rebuilt_ones_does_not_spoil_them()
        {
            // The other half got a fillet of its own later; at the time of the split it was still the copy's.
            BodyScope scope = BodyScope.Of(
                new[] { Body("Schnitt1[1]", true, "Aufsatz1", "Schnitt1"), Body("Verrundung9", false, "Aufsatz1", "Schnitt1", "Verrundung9") },
                new[] { "Aufsatz1", "Schnitt1" }, new string[0]);

            Assert.Empty(scope.OtherBodiesChanged);
            Assert.Equal(new[] { "Aufsatz1", "Schnitt1" }, scope.UnselectedBodies["Verrundung9"]);
        }

        [Fact]
        public void Instances_of_a_selected_body_pattern_belong_to_the_selection()
        {
            // The seed body is pulled in; the instances are made by the pattern the user selected.
            BodyScope scope = BodyScope.Of(
                new[]
                {
                    Body("Aufsatz1", true, "Aufsatz1"),
                    Body("LMuster1[1]", false, "LMuster1"),
                    Body("LMuster1[2]", false, "Aufsatz1", "LMuster1"),
                },
                new[] { "Skizze1", "Aufsatz1", "LMuster1" }, new[] { "LMuster1" });

            Assert.Empty(scope.UnselectedBodies);
            Assert.Empty(scope.OtherBodiesChanged);
        }

        [Fact]
        public void The_same_instances_reached_through_a_selected_instance_are_not()
        {
            // Only one instance selected: the pattern and its seed come along, the other instance does not belong to the selection.
            BodyScope scope = BodyScope.Of(
                new[]
                {
                    Body("Aufsatz1", true, "Aufsatz1"),
                    Body("LMuster1[1]", true, "LMuster1"),
                    Body("LMuster1[2]", false, "LMuster1"),
                },
                new[] { "Aufsatz1", "LMuster1" }, new string[0]);

            Assert.Equal(new[] { "LMuster1[2]" }, scope.UnselectedBodies.Keys);
        }

        [Fact]
        public void Bodies_without_rebuilt_features_are_left_alone()
        {
            BodyScope scope = BodyScope.Of(
                new[] { Body("Aufsatz1", true, "Aufsatz1"), Body("Aufsatz2", false, "Aufsatz2", "Verrundung2") },
                new[] { "Aufsatz1" }, new string[0]);

            Assert.Empty(scope.UnselectedBodies);
            Assert.Empty(scope.OtherBodiesChanged);
            Assert.Empty(BodyScope.None.UnselectedBodies.Concat(BodyScope.None.OtherBodiesChanged));
        }
    }
}
