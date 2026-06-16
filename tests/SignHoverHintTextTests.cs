// Headless self-test for the Marker Sign hover-hint decision logic.
// Card t_7816c0b0 / impl-spec §4A. Asserts AT-MARKER-HINT-1/2/3/5/6 + -WARD against
// the SHIPPED pure function SignHoverHintText.ComputeHintSuffix (linked, not copied).
//
// Idiom matches the repo's existing CI self-check (.github/workflows/ci.yml's inline
// `dotnet run`): print PASS/FAIL per assertion, exit non-zero on any failure.
//
// Run:  dotnet run -c Release --project tests/SBPR.Trailborne.Tests.csproj

using System;
using SBPR.Trailborne.Features.Signs;

namespace SBPR.Trailborne.Tests
{
    internal static class Program
    {
        private static int _fails;

        private static void Check(string label, bool ok)
        {
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {label}");
            if (!ok) _fails++;
        }

        private static int Main()
        {
            Console.WriteLine("== Marker Sign hover-hint decision self-test (§4A) ==");

            // Convenience: the three gate booleans are (isMarker, hasWardAccess, pinned).
            string Suffix(bool isMarker, bool ward, bool pinned)
                => SignHoverHintText.ComputeHintSuffix(isMarker, ward, pinned);

            // ── AT-MARKER-HINT-1: un-pinned marker (ward OK) → "Pin to map" ──
            string unpinned = Suffix(true, true, false);
            Check("AT-MARKER-HINT-1: un-pinned marker says 'Pin to map'",
                unpinned.Contains("Pin to map"));
            Check("AT-MARKER-HINT-1: un-pinned marker does NOT say 'Unpin'",
                !unpinned.Contains("Unpin"));

            // ── AT-MARKER-HINT-2: pinned marker (ward OK) → "Unpin from map" ──
            string pinned = Suffix(true, true, true);
            Check("AT-MARKER-HINT-2: pinned marker says 'Unpin from map'",
                pinned.Contains("Unpin from map"));

            // ── AT-MARKER-HINT-3: the verb FLIPS with the pinned bool (state-aware) ──
            Check("AT-MARKER-HINT-3: verb flips with pin state",
                unpinned != pinned
                && unpinned.Contains("Pin to map")
                && pinned.Contains("Unpin from map"));

            // ── AT-MARKER-HINT-5 (markers-only): a non-marker (plain Painted Sign) gets
            //    NO hint — no "Pin"/"Unpin" substring, regardless of the other gates. ──
            Check("AT-MARKER-HINT-5: non-marker, not pinned → empty suffix",
                Suffix(false, true, false).Length == 0);
            Check("AT-MARKER-HINT-5: non-marker, pinned → empty suffix",
                Suffix(false, true, true).Length == 0);
            Check("AT-MARKER-HINT-5: non-marker suffix contains no 'Pin'/'Unpin'",
                !Suffix(false, true, false).Contains("Pin")
                && !Suffix(false, true, true).Contains("Pin"));

            // ── AT-MARKER-HINT-WARD: marker WITHOUT ward access → empty suffix
            //    (no pin affordance on a sign the player can't toggle), both states. ──
            Check("AT-MARKER-HINT-WARD: ward-denied marker (un-pinned) → empty suffix",
                Suffix(true, false, false).Length == 0);
            Check("AT-MARKER-HINT-WARD: ward-denied marker (pinned) → empty suffix",
                Suffix(true, false, true).Length == 0);

            // ── AT-MARKER-HINT-6 (key tokens): the use key is the raw $KEY_Use token
            //    (localized downstream), behind a literal "Shift" modifier; NEVER a
            //    hardcoded "E", never a custom $piece_* token. ──
            Check("AT-MARKER-HINT-6: emits the $KEY_Use token (not a hardcoded key)",
                unpinned.Contains("$KEY_Use") && pinned.Contains("$KEY_Use"));
            Check("AT-MARKER-HINT-6: carries the literal 'Shift' modifier",
                unpinned.Contains("Shift+$KEY_Use"));
            Check("AT-MARKER-HINT-6: does NOT hardcode the literal key '+E]'",
                !unpinned.Contains("+E]") && !pinned.Contains("+E]"));
            Check("AT-MARKER-HINT-6: does NOT leak a custom $piece_* token",
                !unpinned.Contains("$piece_") && !pinned.Contains("$piece_"));

            // ── AT-MARKER-HINT-4 (append shape): the suffix is a NEW line (leading '\n'),
            //    so the postfix's `__result += suffix` never collides with the vanilla
            //    typed-text / [Use] line above it. ──
            Check("AT-MARKER-HINT-4: hint suffix starts on its own line",
                unpinned.StartsWith("\n") && pinned.StartsWith("\n"));

            Console.WriteLine(_fails == 0
                ? "RESULT: ALL HOVER-HINT ASSERTIONS PASS"
                : $"RESULT: {_fails} ASSERTION(S) FAILED");
            return _fails == 0 ? 0 : 1;
        }
    }
}
