// ============================================================================
//  Trailborne v4 (Mountains) — Seer's Stone wisp-eligibility (engine-free CORE)
// ----------------------------------------------------------------------------
//  Design   : docs/design/seers-stone.md (the consolidated spec)
//             docs/design/seers-stone-pickable-whitelist.md (the ratified roster)
//  Decisions: Daniel, 2026-06-25 (the #design "Seer's Stone" thread) —
//             • Tier = Mountains / crystal-gated (sole v4 headline).
//             • Scope = pickables + locations + SURFACE ore; NO creatures,
//               NO buried silver veins (preserves the Wishbone).
//             • Eligibility ships as a PRE-PACKAGED, OWNER-EDITABLE config
//               (BepInEx/config/<mod>/), NOT frozen in the DLL. Seed-on-first-run.
//             • IGNORE-UNLISTED: a prefab not named in the loaded whitelist gets
//               NO wisp (the owner's edit is the opt-in path — no derive-fallback).
//
//  WHY THIS IS A SEPARATE ENGINE-FREE FILE. The "does prefab X earn a wisp?"
//  decision is the load-bearing policy of the Seer's Stone M1 substrate, and it is
//  exactly the kind of pure logic the repo's test suite gates headlessly — the
//  CompassNorthGate / LensHandoffDecision / DiscRingGeometry link-compile precedent
//  (tests/SBPR.Trailborne.Tests.csproj). Keeping it free of UnityEngine / HarmonyLib /
//  Valheim / YamlDotNet types lets tests/SeersStoneEligibilityTests.cs assert the FULL
//  decision surface under net8.0, so a future edit that silently flips ignore-unlisted
//  into derive-unlisted, or breaks name normalization (the clone-suffix trap), fails CI
//  instead of shipping. The engine wrapper (SeersStoneWhitelist) does the YAML parse +
//  file I/O + live ZNetScene prefab lookups and then calls THIS for every decision.
//
//  Clean-side (ADR-0001): all SBPR-authored logic; references no vanilla or
//  third-party type. Normalization handles the one vanilla quirk that matters —
//  runtime instances are named "<Prefab>(Clone)" while the whitelist names the
//  base prefab — without importing a single Unity type.
// ============================================================================

using System;
using System.Collections.Generic;

namespace SBPR.Trailborne.Features.SeersStone
{
    /// <summary>
    /// Pure wisp-eligibility decision over a parsed whitelist set. Engine-free (no
    /// UnityEngine / Valheim / YamlDotNet refs) so tests/SeersStoneEligibilityTests.cs can
    /// gate the full decision surface headless in CI. The engine wrapper
    /// (<c>SeersStoneWhitelist</c>) owns the YAML parse + seed-on-first-run + the live
    /// prefab-name read; it hands the resolved <see cref="HashSet{String}"/> of eligible
    /// base-prefab names to this class and asks <see cref="IsEligible"/> per raycast hit.
    /// </summary>
    public sealed class SeersStoneEligibility
    {
        // The vanilla runtime appends "(Clone)" to instantiated prefabs (assembly_valheim:
        // every ZNetScene.Instantiate); whitelist authors write the BASE prefab name
        // ("RaspberryBush", not "RaspberryBush(Clone)"). Trailing " (Clone)" and "(Clone)"
        // are both observed across Unity versions, so we strip either spelling. Numeric
        // duplicate suffixes Unity adds in-editor (" (1)") are NOT stripped — they never
        // occur at runtime and stripping them would collapse distinct prefabs.
        private const string CloneSuffix = "(Clone)";

        private readonly HashSet<string> _eligible;

        /// <summary>
        /// Build from the resolved set of eligible BASE prefab names (already parsed out of
        /// the owner's YAML by the engine wrapper). Names are stored normalized so lookups
        /// are O(1) and case/whitespace-insensitive. A null set is treated as empty (the
        /// fail-safe: a missing/corrupt config reveals NOTHING rather than EVERYTHING — under
        /// ignore-unlisted, empty ⇒ no wisps, never a map full of them).
        /// </summary>
        public SeersStoneEligibility(IEnumerable<string>? eligibleBasePrefabNames)
        {
            _eligible = new HashSet<string>(StringComparer.Ordinal);
            if (eligibleBasePrefabNames == null) return;
            foreach (var raw in eligibleBasePrefabNames)
            {
                var n = Normalize(raw);
                if (n.Length != 0) _eligible.Add(n);
            }
        }

        /// <summary>Count of distinct eligible prefab names (for diagnostics / the boot log).</summary>
        public int Count => _eligible.Count;

        /// <summary>
        /// The core question: does this raycast-hit object earn a wisp? <paramref name="prefabName"/>
        /// is whatever the engine read off the live object (typically "<Base>(Clone)"); it is
        /// normalized (clone-suffix stripped, trimmed, lower-cased) and checked against the loaded
        /// set. IGNORE-UNLISTED (Daniel 2026-06-25): not in the set ⇒ false, every time. There is
        /// deliberately NO component-derive fallback here — adding one would change the shipped
        /// policy and is the thing this engine-free gate exists to prevent regressing silently.
        /// </summary>
        public bool IsEligible(string? prefabName)
        {
            var n = Normalize(prefabName);
            return n.Length != 0 && _eligible.Contains(n);
        }

        /// <summary>
        /// Normalize a prefab name for matching: trim, strip a trailing "(Clone)" (with or
        /// without a separating space), trim again, lower-case (ordinal). Public + static so the
        /// YAML-loading wrapper normalizes authored entries through the EXACT same path the
        /// runtime lookups use — one normalization, no drift between author-time and match-time.
        /// </summary>
        public static string Normalize(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var s = name!.Trim();

            // Strip one trailing clone suffix ("Foo(Clone)" or "Foo (Clone)").
            if (s.EndsWith(CloneSuffix, StringComparison.Ordinal))
            {
                s = s.Substring(0, s.Length - CloneSuffix.Length).Trim();
            }

            return s.ToLowerInvariant();
        }
    }
}
