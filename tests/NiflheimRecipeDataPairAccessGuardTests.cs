// ============================================================================
//  Homestead progression — RecipeDataPair.Recipe ACCESS-PATH conformance guard.
// ----------------------------------------------------------------------------
//  RED-FIRST regression for the T019 Swift Preparation live-timer defect (and
//  its sibling in the Refined Workshop UI seam).
//
//  THE DEFECT (genuine live QA, task t_a5eef554): the net48 runtime seams that
//  read the player's currently-selected recipe do so through
//  Harmony `Traverse.Create(gui).Field("m_selectedRecipe").___("Recipe")`.
//  `InventoryGui.m_selectedRecipe` is an `InventoryGui.RecipeDataPair`, and its
//  `Recipe` member is a C# AUTO-PROPERTY (compiler backing field
//  `<Recipe>k__BackingField`), NOT a plain field. Harmony `Traverse.Field("Recipe")`
//  therefore resolves NOTHING and returns null, so `ScaleMenuCraftDuration` fails
//  closed at recipe resolution and the intended 1/3 menu-craft effect never fires.
//  The correct access is `Traverse.Property("Recipe")`.
//
//  WHY A SOURCE-CONFORMANCE GUARD (not a link-compiled execution test): this test
//  project deliberately references NO UnityEngine / HarmonyLib / Valheim assemblies
//  (see SBPR.Trailborne.Tests.csproj) so it runs headless in CI with no Valheim SDK
//  fetched. The net48-only seams (SwiftPreparationCraftTimer, RefinedWorkshop-
//  StationLevelPatch) cannot be link-compiled here, and the real
//  InventoryGui.RecipeDataPair type only exists in assembly_valheim at runtime on a
//  live client. So the strongest regression the base-game/clean-room build permits
//  is to assert the shipped ACCESS PATH against the known member shape: every
//  RecipeDataPair.Recipe read MUST go through `.Property("Recipe")`, and NONE may
//  use the broken `.Field("Recipe")`. This fails RED on the shipped
//  `.Field("Recipe")` behavior and turns GREEN only once both seams are corrected.
//  It is drift-proof: it reads the SHIPPED source files directly, so it tracks the
//  real seams, not a copy.
//
//  Named acceptance touched: guards AT-SWIFT-MENU-ONLY's live delivery precondition
//  (the seam must actually resolve the selected recipe before it can apply 1/3).
// ============================================================================

using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimRecipeDataPairAccessGuardTests
    {
        // The shipped net48 seams that read InventoryGui.m_selectedRecipe → RecipeDataPair.Recipe.
        public static readonly string[] SeamSourceFiles =
        {
            "src/SBPR.Niflheim.HomesteadStones/Features/Cooking/SwiftPreparationCraftTimer.cs",
            "src/SBPR.Niflheim.HomesteadStones/Features/Progression/RefinedWorkshopStationLevelPatch.cs",
        };

        public static System.Collections.Generic.IEnumerable<object[]> SeamCases()
        {
            foreach (var f in SeamSourceFiles) yield return new object[] { f };
        }

        // A `.Field("m_selectedRecipe")` step followed (through any Traverse chaining) by a
        // `.Field("Recipe")` step — the BROKEN access that returns null because Recipe is an
        // auto-property. Whitespace/newlines tolerated between the two calls.
        private static readonly Regex BrokenFieldRecipe = new Regex(
            @"Field\(\s*""m_selectedRecipe""\s*\)\s*\.\s*Field\(\s*""Recipe""\s*\)",
            RegexOptions.Compiled);

        // The CORRECT access: `.Field("m_selectedRecipe").Property("Recipe")`.
        private static readonly Regex CorrectPropertyRecipe = new Regex(
            @"Field\(\s*""m_selectedRecipe""\s*\)\s*\.\s*Property\(\s*""Recipe""\s*\)",
            RegexOptions.Compiled);

        [Theory]
        [MemberData(nameof(SeamCases))]
        public void SelectedRecipe_isReadViaProperty_notField(string relativePath)
        {
            var full = Path.Combine(RepoRoot(), relativePath);
            Assert.True(File.Exists(full), "shipped seam source not found: " + full);
            var src = File.ReadAllText(full);

            // RED on the shipped bug: `.Field("m_selectedRecipe").Field("Recipe")` returns null
            // (Recipe is an auto-property), so the effect silently never fires.
            Assert.False(
                BrokenFieldRecipe.IsMatch(src),
                relativePath + " reads RecipeDataPair.Recipe via Traverse.Field(\"Recipe\"), which returns null " +
                "because Recipe is a C# auto-property. Use Traverse.Property(\"Recipe\").");

            // GREEN requires the corrected access path to actually be present.
            Assert.True(
                CorrectPropertyRecipe.IsMatch(src),
                relativePath + " must read RecipeDataPair.Recipe via " +
                "Traverse.Field(\"m_selectedRecipe\").Property(\"Recipe\").");
        }

        private static string RepoRoot()
        {
            // Walk up from the test assembly until we find a repo marker (the src tree + this seam).
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName,
                        "src", "SBPR.Niflheim.HomesteadStones",
                        "Features", "Cooking", "SwiftPreparationCraftTimer.cs")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate repo root (src/.../SwiftPreparationCraftTimer.cs) from " + AppContext.BaseDirectory);
        }
    }
}
