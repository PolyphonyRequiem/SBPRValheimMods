using System.Linq;
using StoneContent.Workbench.Core.Serialization;
using Xunit;

namespace StoneContent.Workbench.Tests
{
    // Task 2 — deterministic canonical JSON codec. Strict load (unknown/missing rejected), stable
    // property + array order, null prices preserved, and byte-identical re-serialization.
    public sealed class CanonicalJsonTests
    {
        [Fact]
        public void Loads_the_canonical_asset_with_all_four_sections_and_pins()
        {
            var doc = CanonicalJson.Load(TestAssets.AssetJson);

            Assert.Equal(1, doc.FormatVersion);
            Assert.Equal("niflheim.homestead-stone.progression", doc.AssetId);
            Assert.Equal("Settlement", doc.Family);
            Assert.Equal("Homestead", doc.Variant);

            Assert.Equal(1, doc.Versions.ContentRegistry);
            Assert.Equal(1, doc.Versions.FoundationalCatalog);
            Assert.Equal(1, doc.Versions.FacetPalette);
            Assert.Equal(1, doc.Versions.TreeTuning);

            Assert.Equal(20, doc.Nodes.Count);
            Assert.Equal(4, doc.Trees.Count);
            Assert.Equal(2, doc.Facets.Count);
            Assert.Equal(8, doc.Foundational.Catalog.Members.Count);
            Assert.Equal(2, doc.Foundational.Catalog.Exclusions.Count);
        }

        [Fact]
        public void Preserves_null_prices_on_unavailable_nodes()
        {
            var doc = CanonicalJson.Load(TestAssets.AssetJson);
            var watchful = doc.Nodes.Single(n => n.Id == "WatchfulCook");
            Assert.Null(watchful.Pricing.DevelopmentBp);
            Assert.Null(watchful.Pricing.PurchaseAp);

            var savor = doc.Nodes.Single(n => n.Id == "SavorTheHearth");
            Assert.Equal(1, savor.Pricing.DevelopmentBp);
            Assert.Null(savor.Pricing.PurchaseAp); // Local node: no AP price.

            var fieldPrep = doc.Nodes.Single(n => n.Id == "FieldPrep");
            Assert.Equal(1, fieldPrep.Pricing.DevelopmentBp);
            Assert.Equal(1, fieldPrep.Pricing.PurchaseAp);
        }

        [Fact]
        public void Preserves_stable_node_authored_order()
        {
            var doc = CanonicalJson.Load(TestAssets.AssetJson);
            Assert.Equal("SavorTheHearth", doc.Nodes[0].Id);
            Assert.Equal("SwiftPreparation", doc.Nodes[3].Id);
            Assert.Equal("HeavyHands", doc.Nodes[19].Id);
        }

        [Fact]
        public void Rejects_unknown_property()
        {
            const string json = @"{ ""formatVersion"": 1, ""surpriseField"": true }";
            var ex = Assert.Throws<CanonicalJson.JsonLoadException>(() => CanonicalJson.Load(json));
            Assert.Contains("surpriseField", ex.Message);
        }

        [Fact]
        public void Rejects_missing_required_property()
        {
            const string json = @"{ ""formatVersion"": 1 }";
            Assert.Throws<CanonicalJson.JsonLoadException>(() => CanonicalJson.Load(json));
        }

        [Fact]
        public void Second_serialization_is_byte_identical()
        {
            var doc = CanonicalJson.Load(TestAssets.AssetJson);
            var first = CanonicalJson.Serialize(doc);
            var reloaded = CanonicalJson.Load(first);
            var second = CanonicalJson.Serialize(reloaded);
            Assert.Equal(first, second);
        }

        [Fact]
        public void Checked_in_asset_matches_canonical_output_byte_for_byte()
        {
            var raw = TestAssets.AssetJson.Replace("\r\n", "\n");
            var doc = CanonicalJson.Load(raw);
            var canonical = CanonicalJson.Serialize(doc);
            Assert.Equal(raw, canonical);
        }
    }
}
