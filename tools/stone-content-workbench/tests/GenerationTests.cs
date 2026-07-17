using System.Linq;
using StoneContent.Workbench.Core.Generation;
using StoneContent.Workbench.Core.Serialization;
using Xunit;

namespace StoneContent.Workbench.Tests
{
    // Task 4 — deterministic C# generation. Four ordered artifacts, byte-stable across passes,
    // LF newlines, header pins, and Array.Empty<T>() for empty arrays.
    public sealed class GenerationTests
    {
        private static readonly CSharpCatalogGenerator Generator = new();
        private static GenerationResult Gen() => Generator.Generate(CanonicalJson.Load(TestAssets.AssetJson));

        [Fact]
        public void Emits_exactly_the_four_named_artifacts_in_order()
        {
            var names = Gen().Artifacts.Select(a => a.FileName).ToArray();
            Assert.Equal(new[]
            {
                "HomesteadProgressionCatalog.Data.g.cs",
                "FoundationalPieceCatalog.Data.g.cs",
                "StoneFacetPalette.Data.g.cs",
                "TreeTuningCatalog.Data.g.cs",
            }, names);
        }

        [Fact]
        public void Double_generation_is_byte_identical()
        {
            var a = Gen();
            var b = Gen();
            Assert.Equal(a.Artifacts.Count, b.Artifacts.Count);
            for (int i = 0; i < a.Artifacts.Count; i++)
            {
                Assert.Equal(a.Artifacts[i].FileName, b.Artifacts[i].FileName);
                Assert.Equal(a.Artifacts[i].Content, b.Artifacts[i].Content);
            }
        }

        [Fact]
        public void All_output_uses_lf_newlines_only()
        {
            foreach (var art in Gen().Artifacts)
                Assert.DoesNotContain("\r", art.Content);
        }

        [Fact]
        public void Header_carries_all_four_version_pins()
        {
            var nodes = Gen().Artifacts.First(a => a.FileName.StartsWith("HomesteadProgression"));
            Assert.Contains("contentRegistry=1", nodes.Content);
            Assert.Contains("foundationalCatalog=1", nodes.Content);
            Assert.Contains("facetPalette=1", nodes.Content);
            Assert.Contains("treeTuning=1", nodes.Content);
            Assert.Contains("source asset: niflheim.homestead-stone.progression", nodes.Content);
        }

        [Fact]
        public void Empty_prior_offered_set_emits_typed_empty_array_not_untyped()
        {
            var nodes = Gen().Artifacts.First(a => a.FileName.StartsWith("HomesteadProgression"));
            Assert.Contains("Array.Empty<string>()", nodes.Content);
            Assert.DoesNotContain("new[] {}", nodes.Content);
            Assert.DoesNotContain("new[] { }", nodes.Content);
        }

        [Fact]
        public void Populated_prior_offered_set_emits_the_swift_prerequisites()
        {
            var nodes = Gen().Artifacts.First(a => a.FileName.StartsWith("HomesteadProgression"));
            Assert.Contains("new string[] { \"FieldPrep\", \"IronStomach\" }", nodes.Content);
        }
    }
}
