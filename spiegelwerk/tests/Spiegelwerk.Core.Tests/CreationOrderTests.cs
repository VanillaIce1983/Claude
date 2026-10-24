using System.Collections.Generic;
using System.Linq;
using Spiegelwerk.Core.Planning;
using Xunit;

namespace Spiegelwerk.Core.Tests
{
    public class CreationOrderTests
    {
        private sealed class Node
        {
            public Node(string name, params Node[] parents)
            {
                Name = name;
                Parents.AddRange(parents);
            }

            public string Name { get; }
            public List<Node> Parents { get; } = new List<Node>();
            public List<Node> Absorbed { get; } = new List<Node>();
        }

        private static List<string> Order(IEnumerable<Node> tree) =>
            CreationOrder.Sort(tree, n => n.Absorbed, n => n.Parents, n => n.Name).Select(n => n.Name).ToList();

        [Fact]
        public void Guide_curve_comes_after_the_absorbed_profiles_it_refers_to()
        {
            var front = new Node("Ebene vorne");
            var plane = new Node("Ebene1", front);
            var profile1 = new Node("Skizze1", front);
            var profile2 = new Node("Skizze2", plane);
            var guide = new Node("Skizze3", profile1, profile2);
            var loft = new Node("Ausformung1", profile1, profile2, guide);
            loft.Absorbed.AddRange(new[] { profile1, profile2 });

            // Tree: the guide sketch stays above the loft, the profiles sit under it.
            Assert.Equal(new[] { "Ebene vorne", "Ebene1", "Skizze1", "Skizze2", "Skizze3", "Ausformung1" },
                Order(new[] { front, plane, guide, loft }));
        }

        [Fact]
        public void Untouched_tree_keeps_its_order()
        {
            var front = new Node("Ebene vorne");
            var s1 = new Node("Skizze1", front);
            var boss = new Node("Aufsatz1", s1);
            boss.Absorbed.Add(s1);
            var s2 = new Node("Skizze2", boss);
            var cut = new Node("Schnitt1", s2, boss);
            cut.Absorbed.Add(s2);
            var fillet = new Node("Verrundung1", cut);

            Assert.Equal(new[] { "Ebene vorne", "Skizze1", "Aufsatz1", "Skizze2", "Schnitt1", "Verrundung1" },
                Order(new[] { front, boss, cut, fillet }));
        }

        [Fact]
        public void Every_feature_appears_once_even_with_a_damaged_cycle()
        {
            var a = new Node("A");
            var b = new Node("B", a);
            a.Parents.Add(b);
            Assert.Equal(2, Order(new[] { a, b }).Distinct().Count());
        }
    }
}
