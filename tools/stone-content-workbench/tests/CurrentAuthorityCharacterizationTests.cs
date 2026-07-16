using System.Linq;
using StoneContent.Workbench.Core.Model;
using Xunit;

namespace StoneContent.Workbench.Tests
{
    // Task 1 — characterize current authority. The adapter's normalized snapshot must contain the
    // exact current 20-node roster (13 executable + 7 unavailable; 12 L1 + sole executable L2 Swift
    // Preparation), the Foundational roster/exclusions, the Facet palette, and the current pins.
    public sealed class CurrentAuthorityCharacterizationTests
    {
        private static readonly StoneContentDocument Current = CurrentCatalogSnapshotAdapter.Build();

        [Fact]
        public void Roster_is_exactly_twenty_nodes_thirteen_executable_seven_unavailable()
        {
            Assert.Equal(20, Current.Nodes.Count);
            Assert.Equal(13, Current.Nodes.Count(n => n.FirstBuildStatus == "Executable"));
            Assert.Equal(7, Current.Nodes.Count(n => n.FirstBuildStatus == "Unavailable"));
        }

        [Fact]
        public void Executable_partition_is_twelve_level1_plus_sole_level2_swift_preparation()
        {
            var executable = Current.Nodes.Where(n => n.FirstBuildStatus == "Executable").ToList();
            Assert.Equal(12, executable.Count(n => n.TreeLevel == 1));
            var l2 = executable.Where(n => n.TreeLevel == 2).ToList();
            Assert.Single(l2);
            Assert.Equal("SwiftPreparation", l2[0].Id);
        }

        [Fact]
        public void Swift_preparation_requires_both_prior_offered_cooking_nodes()
        {
            var swift = Current.Nodes.Single(n => n.Id == "SwiftPreparation");
            Assert.Equal(new[] { "FieldPrep", "IronStomach" }, swift.Requirements.PriorOfferedNodeIds.ToArray());
        }

        [Fact]
        public void Foundational_roster_and_exclusions_match_current_build()
        {
            Assert.Equal("HomesteadFoundationalConstruction", Current.Foundational.Catalog.Id);
            Assert.Equal("v1", Current.Foundational.Catalog.VersionTag);
            Assert.Equal(8, Current.Foundational.Catalog.Members.Count);
            Assert.Contains("foundation_wood_floor", Current.Foundational.Catalog.Members);
            Assert.Equal(new[] { "foundation_workbench", "foundation_forge" },
                Current.Foundational.Catalog.Exclusions.ToArray());
        }

        [Fact]
        public void Facet_palette_is_one_profession_and_one_martial()
        {
            Assert.Equal(2, Current.Facets.Count);
            var prof = Current.Facets.Single(f => f.Id == "Profession");
            Assert.Equal("Profession", prof.Category);
            Assert.Equal(new[] { "Cooking", "Crafting" }, prof.CandidateTreeIds.ToArray());
            var martial = Current.Facets.Single(f => f.Id == "Martial");
            Assert.Equal(new[] { "Archer", "Warrior" }, martial.CandidateTreeIds.ToArray());
        }

        [Fact]
        public void Current_pins_are_all_v1()
        {
            Assert.Equal(1, Current.Versions.ContentRegistry);
            Assert.Equal(1, Current.Versions.FoundationalCatalog);
            Assert.Equal(1, Current.Versions.FacetPalette);
        }
    }
}
