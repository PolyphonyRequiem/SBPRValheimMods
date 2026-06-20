// ============================================================================
//  Cartography caption composition — xUnit structural tests
//  (biome-indicator-impl-spec §3.1/§3.5, card t_304076fa).
// ----------------------------------------------------------------------------
//  Tests the SHIPPED pure helper MapCaptionText (link-compiled from ../src, not
//  copied — see the .csproj). This is the right kind of thing to test: the
//  under-disc caption STACK assembly (name / biome / hint) and the $biome_*
//  literal-leak guard are STABLE CONTRACTS — string logic that ships, with no
//  UnityEngine / Valheim dependency. The Unity-bound adapter (MapSurface's
//  UpdateCaption / CurrentBiomeNameOrNull, which reads Player.GetCurrentBiome()
//  + Localization) is the volatile glue and is deliberately NOT tested here.
//
//  Pins the §5 acceptance behaviour that is unit-checkable headless:
//    • AT-CAPTION-NAME-HINT-INTACT — name, no biome ⇒ byte-identical 2-line stack
//    • AT-BIOME-NONE-OMIT          — no biome ⇒ middle line omitted (no empty row)
//    • AT-BIOME-CLEAN              — unresolved $biome_* token ⇒ treated as null
//  (the live "updates on border crossing" / GPU-legibility halves are Daniel's
//   in-game eyeball — logs-green ≠ playable.)
// ============================================================================

using SBPR.Trailborne.Features.Cartography;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class MapCaptionTextTests
    {
        // The shipped disc font sizes (MapSurface CaptionNameFontPx / CaptionBiomeFontPx /
        // CaptionHintFontPx). Distinct values so a mis-sized span is detectable in the output.
        private const int NameSz = 18;
        private const int BiomeSz = 16;
        private const int HintSz = 16;
        private const string Hint = "[M] Read map";

        private static string Compose(string? name, string? biome)
            => MapCaptionText.ComposeDiscCaption(name, biome, Hint, NameSz, BiomeSz, HintSz);

        // ── The full 3-line stack: name / biome / hint, in that order ────────────
        [Fact]
        public void NameAndBiome_compose_three_lines_in_order()
        {
            string s = Compose("Local map for Northern Outpost", "Meadows");
            string expected =
                "<size=18>Local map for Northern Outpost</size>\n" +
                "<size=16>Meadows</size>\n" +
                "<size=16>[M] Read map</size>";
            Assert.Equal(expected, s);
            // Exactly three rows → exactly two newlines.
            Assert.Equal(2, CountNewlines(s));
            // Order: name index < biome index < hint index.
            Assert.True(s.IndexOf("Northern Outpost") < s.IndexOf("Meadows"));
            Assert.True(s.IndexOf("Meadows") < s.IndexOf("Read map"));
        }

        // ── AT-CAPTION-NAME-HINT-INTACT: name, NO biome ⇒ the PR #205 2-line stack ──
        [Fact]
        public void Name_without_biome_is_the_two_line_name_hint_stack()
        {
            string s = Compose("Local map for Northern Outpost", null);
            string expected =
                "<size=18>Local map for Northern Outpost</size>\n" +
                "<size=16>[M] Read map</size>";
            Assert.Equal(expected, s);
            Assert.Equal(1, CountNewlines(s));   // two rows, one newline
            Assert.DoesNotContain("<size=16>Meadows", s);
        }

        // ── no name, biome present ⇒ biome / hint ────────────────────────────────
        [Fact]
        public void Biome_without_name_is_biome_hint()
        {
            string s = Compose(null, "Black Forest");
            string expected =
                "<size=16>Black Forest</size>\n" +
                "<size=16>[M] Read map</size>";
            Assert.Equal(expected, s);
            Assert.Equal(1, CountNewlines(s));
            Assert.DoesNotContain("Local map for", s);  // no empty "Local map for " tail
        }

        // ── AT-BIOME-NONE-OMIT (composition half): no name, no biome ⇒ hint only ──
        [Fact]
        public void Neither_name_nor_biome_is_hint_only()
        {
            string s = Compose(null, null);
            Assert.Equal("<size=16>[M] Read map</size>", s);
            Assert.Equal(0, CountNewlines(s));  // single row, no stray newline / empty row
        }

        // Empty strings degrade exactly like null (the IsNullOrEmpty contract).
        [Theory]
        [InlineData("", "")]
        [InlineData("", null)]
        [InlineData(null, "")]
        public void Empty_strings_omit_their_line_like_null(string? name, string? biome)
        {
            string s = Compose(name, biome);
            Assert.Equal("<size=16>[M] Read map</size>", s);
            Assert.Equal(0, CountNewlines(s));
        }

        // The hint line is ALWAYS present — the stack never collapses to nothing.
        [Theory]
        [InlineData("Map", "Meadows")]
        [InlineData("Map", null)]
        [InlineData(null, "Meadows")]
        [InlineData(null, null)]
        public void Hint_line_is_always_present(string? name, string? biome)
        {
            Assert.Contains("[M] Read map", Compose(name, biome));
        }

        // ── AT-BIOME-CLEAN: the $biome_* literal-leak guard ──────────────────────
        [Fact]
        public void Unresolved_biome_token_is_detected()
        {
            // Localization.Localize returns the input unchanged when no key matched.
            Assert.True(MapCaptionText.IsUnresolvedBiomeToken("$biome_none"));
            Assert.True(MapCaptionText.IsUnresolvedBiomeToken("$biome_somefuturebiome"));
            Assert.True(MapCaptionText.IsUnresolvedBiomeToken(""));
            Assert.True(MapCaptionText.IsUnresolvedBiomeToken(null));
        }

        [Fact]
        public void Resolved_biome_name_is_not_flagged()
        {
            // A real localized name never starts with the token prefix.
            Assert.False(MapCaptionText.IsUnresolvedBiomeToken("Meadows"));
            Assert.False(MapCaptionText.IsUnresolvedBiomeToken("Black Forest"));
            Assert.False(MapCaptionText.IsUnresolvedBiomeToken("Mistlands"));
        }

        private static int CountNewlines(string s)
        {
            int n = 0;
            foreach (char c in s) if (c == '\n') n++;
            return n;
        }
    }
}
