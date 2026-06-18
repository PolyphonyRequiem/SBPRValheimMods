// ============================================================================
//  Marker Sign hover-hint decision logic — xUnit structural tests
//  (CLEANUP 3/3, card t_4364809c; originally card t_7816c0b0 / impl-spec §4A).
// ----------------------------------------------------------------------------
//  Tests the SHIPPED pure function SignHoverHintText.ComputeHintSuffix (link-
//  compiled from ../src, not copied — see the .csproj). This is a STABLE
//  CONTRACT: a locked decision table whose wording is pinned by impl-spec §4A.4.
//  It is the right kind of thing to test (low-volatility behaviour, not volatile
//  UI internals) — the hover PATCH that calls it is the volatile adapter and is
//  deliberately NOT tested here.
//
//  Migrated verbatim in intent from the prior `dotnet run` console self-test
//  (AT-MARKER-HINT-1/2/3/4/5/6 + -WARD) into xUnit facts/theories so the suite
//  gates CI via `dotnet test`.
// ============================================================================

using SBPR.Trailborne.Features.Signs;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class SignHoverHintTextTests
    {
        // Convenience: the three gate booleans are (isMarker, hasWardAccess, pinned).
        private static string Suffix(bool isMarker, bool ward, bool pinned)
            => SignHoverHintText.ComputeHintSuffix(isMarker, ward, pinned);

        // ── AT-MARKER-HINT-1: un-pinned marker (ward OK) → "Pin to map" ──────────
        [Fact]
        public void Unpinned_marker_says_Pin_to_map()
        {
            string s = Suffix(isMarker: true, ward: true, pinned: false);
            Assert.Contains("Pin to map", s);
            Assert.DoesNotContain("Unpin", s);
        }

        // ── AT-MARKER-HINT-2: pinned marker (ward OK) → "Unpin from map" ─────────
        [Fact]
        public void Pinned_marker_says_Unpin_from_map()
        {
            string s = Suffix(isMarker: true, ward: true, pinned: true);
            Assert.Contains("Unpin from map", s);
        }

        // ── AT-MARKER-HINT-3: the verb FLIPS with the pinned bool (state-aware) ──
        [Fact]
        public void Verb_flips_with_pin_state()
        {
            string unpinned = Suffix(true, true, false);
            string pinned = Suffix(true, true, true);
            Assert.NotEqual(unpinned, pinned);
            Assert.Contains("Pin to map", unpinned);
            Assert.Contains("Unpin from map", pinned);
        }

        // ── AT-MARKER-HINT-5 (markers-only): a non-marker (plain Painted Sign) gets
        //    NO hint — no "Pin"/"Unpin" substring, regardless of the other gates. ──
        [Theory]
        [InlineData(false)] // not pinned
        [InlineData(true)]  // pinned
        public void Non_marker_gets_empty_suffix(bool pinned)
        {
            string s = Suffix(isMarker: false, ward: true, pinned: pinned);
            Assert.Equal(0, s.Length);
            Assert.DoesNotContain("Pin", s); // also covers "Unpin"
        }

        // ── AT-MARKER-HINT-WARD: marker WITHOUT ward access → empty suffix
        //    (no pin affordance on a sign the player can't toggle), both states. ──
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Ward_denied_marker_gets_empty_suffix(bool pinned)
        {
            string s = Suffix(isMarker: true, ward: false, pinned: pinned);
            Assert.Equal(0, s.Length);
        }

        // ── AT-MARKER-HINT-6 (key tokens): the use key is the raw $KEY_Use token
        //    (localized downstream), behind a literal "Shift" modifier; NEVER a
        //    hardcoded "E", never a custom $piece_* token. ──
        [Fact]
        public void Emits_KEY_Use_token_behind_Shift_modifier_never_hardcoded_key()
        {
            string unpinned = Suffix(true, true, false);
            string pinned = Suffix(true, true, true);

            Assert.Contains("$KEY_Use", unpinned);
            Assert.Contains("$KEY_Use", pinned);
            Assert.Contains("Shift+$KEY_Use", unpinned);

            Assert.DoesNotContain("+E]", unpinned);
            Assert.DoesNotContain("+E]", pinned);
            Assert.DoesNotContain("$piece_", unpinned);
            Assert.DoesNotContain("$piece_", pinned);
        }

        // ── AT-MARKER-HINT-4 (append shape): the suffix is a NEW line (leading '\n'),
        //    so the postfix's `__result += suffix` never collides with the vanilla
        //    typed-text / [Use] line above it. ──
        [Fact]
        public void Hint_suffix_starts_on_its_own_line()
        {
            Assert.StartsWith("\n", Suffix(true, true, false));
            Assert.StartsWith("\n", Suffix(true, true, true));
        }
    }
}
