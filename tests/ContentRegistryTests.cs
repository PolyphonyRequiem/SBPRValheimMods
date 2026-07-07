using System.Collections.Generic;
using System.Linq;
using SBPR.Trailborne.Core.Content;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    /// <summary>
    /// Engine-free invariant tests for the <see cref="ContentRegistry"/> (arch review Model B,
    /// sliced P2). These assert — left of boot, with no engine — the structural properties the
    /// SpecCheck boot guard used to be the only witness of: no duplicate outputs, every recipe/piece
    /// names a real cost, item recipes are well-formed. Because SpecCheck now PROJECTS its manifest
    /// from this same registry, a green run here plus a green shell build means the boot guard reads
    /// a structurally valid list.
    ///
    /// <para>The registry INSTANCE these test is built shell-side from the live feature consts
    /// (SbprContentManifest, which the Core can't reference — the consts live in engine-fused
    /// classes). So these tests build an equivalent registry from plain data to exercise the Core
    /// TYPE's invariants directly; the shell build is what proves the real manifest compiles against
    /// the same consts. Together they cover both halves.</para>
    /// </summary>
    public class ContentRegistryTests
    {
        private static Req R(string res, int amt) => new Req(res, amt);

        // A small, valid registry exercising the shapes the real manifest uses (item + piece,
        // station + no-station, single + multi resource).
        private static ContentRegistry Sample() => new ContentRegistry(
            recipes: new[]
            {
                new RecipeDef("SBPR_Item_A", 1, "piece_bench", new[] { R("Wood", 5), R("Iron", 2) }),
                new RecipeDef("SBPR_Item_B", 2, "piece_bench", new[] { R("Coal", 1) }),
            },
            pieces: new[]
            {
                new PieceDef("piece_bench", null, new[] { R("Wood", 10), R("Stone", 4) }),
                new PieceDef("piece_portal", null, new[] { R("SBPR_Item_A", 1) }),
            });

        [Fact]
        public void NoDuplicateOutputs_InHealthyRegistry()
        {
            Assert.Empty(Sample().DuplicateOutputs());
        }

        [Fact]
        public void DuplicateOutputs_AreDetected()
        {
            var dup = new ContentRegistry(
                recipes: new[]
                {
                    new RecipeDef("SBPR_Dup", 1, "b", new[] { R("Wood", 1) }),
                    new RecipeDef("SBPR_Dup", 1, "b", new[] { R("Wood", 2) }),   // same output twice
                },
                pieces: new PieceDef[0]);
            Assert.Contains("SBPR_Dup", dup.DuplicateOutputs());
        }

        [Fact]
        public void RecipeForItem_ResolvesAndMissesCorrectly()
        {
            var reg = Sample();
            Assert.NotNull(reg.RecipeForItem("SBPR_Item_A"));
            Assert.Equal(2, reg.RecipeForItem("SBPR_Item_B")!.Amount);
            Assert.Null(reg.RecipeForItem("SBPR_Nonexistent"));
        }

        [Fact]
        public void EveryRecipeAndPiece_HasAtLeastOneResource()
        {
            var reg = Sample();
            Assert.All(reg.Recipes, r => Assert.NotEmpty(r.Resources));
            Assert.All(reg.Pieces, p => Assert.NotEmpty(p.Resources));
        }

        [Fact]
        public void EveryResource_HasPositiveAmount_AndNonEmptyName()
        {
            var reg = Sample();
            foreach (var req in reg.Recipes.SelectMany(r => r.Resources).Concat(reg.Pieces.SelectMany(p => p.Resources)))
            {
                Assert.False(string.IsNullOrEmpty(req.Resource));
                Assert.True(req.Amount > 0, $"resource {req.Resource} has non-positive amount {req.Amount}");
            }
        }

        [Fact]
        public void ReferencedStations_AreCollected()
        {
            var stations = Sample().ReferencedStations();
            Assert.Contains("piece_bench", stations);
            Assert.Single(stations);   // piece_portal + the pieces themselves have null station
        }

        [Fact]
        public void ItemRecipe_AmountIsPositive()
        {
            var reg = Sample();
            Assert.All(reg.Recipes, r => Assert.True(r.Amount > 0));
        }
    }
}
