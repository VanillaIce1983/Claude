using System.Collections.Generic;
using Spiegelwerk.Core.Planning;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    /// <summary>Renaming new features around names that are taken.</summary>
    public class FeatureNamesTests
    {
        private static string Free(string wanted, string current, params string[] names)
        {
            var taken = new HashSet<string>(names) { current };
            return FeatureNames.Free(wanted, current, taken.Contains);
        }

        [Fact]
        public void A_free_name_is_taken_as_it_is()
        {
            Assert.Equal("Ebene1_gespiegelt", Free("Ebene1_gespiegelt", "Ebene4"));
        }

        [Fact]
        public void A_taken_name_gets_the_first_free_number()
        {
            Assert.Equal("Ebene1_gespiegelt (2)", Free("Ebene1_gespiegelt", "Ebene4", "Ebene1_gespiegelt"));
            Assert.Equal("Ebene1_gespiegelt (3)", Free("Ebene1_gespiegelt", "Ebene4", "Ebene1_gespiegelt", "Ebene1_gespiegelt (2)"));
        }

        [Fact]
        public void Renaming_a_second_time_keeps_the_number_of_the_first()
        {
            // PlaneResolver renames, BuildContext.Register renames again with the same wish.
            Assert.Equal("Ebene1_gespiegelt (2)", Free("Ebene1_gespiegelt", "Ebene1_gespiegelt (2)", "Ebene1_gespiegelt"));
        }

        [Fact]
        public void The_feature_already_called_so_keeps_its_name()
        {
            Assert.Equal("Achse1", Free("Achse1", "Achse1"));
        }
    }
}
