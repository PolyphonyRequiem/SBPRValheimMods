// ============================================================================
//  Seer's Stone — whitelist PARSER tests (engine-free, net8.0/xUnit, CI-gated)
// ----------------------------------------------------------------------------
//  Gates the flat YAML-subset parse the runtime relies on: comment stripping
//  (whole-line + trailing), section routing, the version scalar, dedup across
//  sections, and graceful handling of malformed / empty input. Because we ship
//  NO serializer dependency (see WhitelistDocument header), this parser IS the
//  contract — these tests are what keep it honest.
// ============================================================================

using System.Linq;
using SBPR.Trailborne.Features.SeersStone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public class SeersStoneWhitelistParseTests
    {
        private const string Sample = @"
# A comment line
version: 1

pickables:
  - RaspberryBush        # trailing comment
  - BlueberryBush
  # an indented comment
  - Pickable_Thistle

ore_surface:
  - MineRock_Copper
  - Pickable_Tin

locations:
  - Crypt2
  - SunkenCrypt4
";

        [Fact]
        public void Parses_all_three_sections()
        {
            var doc = WhitelistDocument.Parse(Sample);
            Assert.Equal(new[] { "RaspberryBush", "BlueberryBush", "Pickable_Thistle" }, doc.Pickables);
            Assert.Equal(new[] { "MineRock_Copper", "Pickable_Tin" }, doc.OreSurface);
            Assert.Equal(new[] { "Crypt2", "SunkenCrypt4" }, doc.Locations);
        }

        [Fact]
        public void Version_scalar_is_read()
        {
            Assert.Equal(1, WhitelistDocument.Parse(Sample).Version);
            Assert.Equal(7, WhitelistDocument.Parse("version: 7\npickables:\n  - X").Version);
        }

        [Fact]
        public void Version_defaults_to_1_when_absent_or_unparseable()
        {
            Assert.Equal(1, WhitelistDocument.Parse("pickables:\n  - X").Version);
            Assert.Equal(1, WhitelistDocument.Parse("version: not-a-number\npickables:\n  - X").Version);
        }

        [Fact]
        public void Trailing_and_wholeline_comments_are_stripped()
        {
            var doc = WhitelistDocument.Parse("pickables:\n  - Foo  # this is Foo\n  # skip me\n  - Bar");
            Assert.Equal(new[] { "Foo", "Bar" }, doc.Pickables);
        }

        [Fact]
        public void AllNames_is_the_normalized_deduped_union()
        {
            var doc = WhitelistDocument.Parse(
                "pickables:\n  - RaspberryBush\n  - RaspberryBush(Clone)\nore_surface:\n  - raspberrybush\nlocations:\n  - Crypt2");
            // raspberry (3 spellings → 1) + crypt2 = 2
            Assert.Equal(2, doc.AllNames.Count);
            Assert.Contains("raspberrybush", doc.AllNames);
            Assert.Contains("crypt2", doc.AllNames);
        }

        [Fact]
        public void Items_before_any_section_are_dropped_not_crashed()
        {
            var doc = WhitelistDocument.Parse("- Orphan\n- AlsoOrphan\npickables:\n  - Real");
            Assert.Equal(new[] { "Real" }, doc.Pickables);
            Assert.Single(doc.AllNames);
        }

        [Fact]
        public void Unknown_section_key_parks_active_so_items_dont_misroute()
        {
            // An unknown future key must NOT absorb items into the previous section.
            var doc = WhitelistDocument.Parse(
                "pickables:\n  - Real\nfuture_section:\n  - NotARealEntry\nlocations:\n  - Crypt2");
            Assert.Equal(new[] { "Real" }, doc.Pickables);
            Assert.Equal(new[] { "Crypt2" }, doc.Locations);
            Assert.DoesNotContain("notarealentry", doc.AllNames);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("# only comments\n# nothing else")]
        [InlineData("version: 1")]
        public void Empty_or_contentless_input_yields_empty_document(string? text)
        {
            var doc = WhitelistDocument.Parse(text);
            Assert.Empty(doc.AllNames);
            Assert.Empty(doc.Pickables);
            Assert.Empty(doc.OreSurface);
            Assert.Empty(doc.Locations);
        }

        [Fact]
        public void Crlf_and_cr_line_endings_are_handled()
        {
            var crlf = "pickables:\r\n  - Foo\r\n  - Bar\r\n";
            var doc = WhitelistDocument.Parse(crlf);
            Assert.Equal(new[] { "Foo", "Bar" }, doc.Pickables);
        }

        [Fact]
        public void End_to_end_parsed_doc_feeds_eligibility()
        {
            var doc = WhitelistDocument.Parse(Sample);
            var e = new SeersStoneEligibility(doc.AllNames);
            Assert.True(e.IsEligible("RaspberryBush(Clone)"));  // runtime name
            Assert.True(e.IsEligible("MineRock_Copper"));
            Assert.True(e.IsEligible("SunkenCrypt4(Clone)"));
            Assert.False(e.IsEligible("Pickable_Stone"));        // unlisted litter
        }
    }
}
