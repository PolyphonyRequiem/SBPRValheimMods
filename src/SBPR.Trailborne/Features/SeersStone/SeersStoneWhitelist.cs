// ============================================================================
//  Trailborne v4 (Mountains) — Seer's Stone whitelist (engine-side WRAPPER)
// ----------------------------------------------------------------------------
//  Design   : docs/design/seers-stone.md + docs/design/seers-stone-pickable-whitelist.md
//  Decisions: Daniel, 2026-06-25 — pre-packaged, OWNER-EDITABLE config under
//             BepInEx/config/<mod>/, seeded on first run; IGNORE-UNLISTED.
//
//  WHAT LIVES HERE (and what does NOT). This is the Unity/BepInEx-coupled half of the
//  Seer's Stone whitelist substrate. It:
//    1. Seeds the default whitelist into BepInEx/config/SBPR.Trailborne/ on first run
//       (the Assets.cs File.Exists idiom), shipping the full vanilla roster.
//    2. Reads the owner's file and hands the text to the engine-free WhitelistDocument
//       parser (a flat YAML-subset scan — NO serializer dependency; see that file's
//       header for why we deliberately ship no YamlDotNet/ILRepack).
//    3. Hands the parsed name set to the ENGINE-FREE SeersStoneEligibility core and
//       exposes IsEligible() for the (later) raycast/wisp code to call.
//  The DECISION logic (normalize, ignore-unlisted, match) lives in
//  SeersStoneEligibility (engine-free, unit-tested headless). The PARSE SHAPE
//  (section keys, comment handling) lives in WhitelistDocument (engine-free,
//  unit-tested). This wrapper is the thin I/O seam — only File/Directory calls, no
//  decision or parse logic — so the untestable part stays as small as possible.
//
//  Clean-side (ADR-0001): SBPR-authored; touches NO vanilla or third-party type —
//  the whole substrate is dependency-free (the file I/O is BCL System.IO only).
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using SBPR.Trailborne.Features.SeersStone;

namespace SBPR.Trailborne.Features.SeersStone
{
    /// <summary>
    /// Engine-side loader + lifecycle for the Seer's Stone wisp whitelist. Call
    /// <see cref="Load"/> once at boot (after <c>Plugin.PluginFolder</c> is set); thereafter
    /// <see cref="IsEligible"/> answers per raycast hit. All file I/O and YAML parsing is
    /// confined here; the decision logic is delegated to the engine-free
    /// <see cref="SeersStoneEligibility"/> so it can be gated headless in CI.
    /// </summary>
    public static class SeersStoneWhitelist
    {
        /// <summary>The owner-editable file name, seeded into BepInEx/config/&lt;mod&gt;/ on first run.</summary>
        public const string ConfigFileName = "seers_stone_whitelist.yaml";

        /// <summary>The shipped default (overlaid into the plugin folder by pack-modpack.sh).</summary>
        public const string DefaultFileName = "seers_stone_whitelist.default.yaml";

        private static SeersStoneEligibility _eligibility = new SeersStoneEligibility(null);
        private static bool _loaded;

        /// <summary>Count of eligible prefab names currently loaded (diagnostics / boot log).</summary>
        public static int Count => _eligibility.Count;

        /// <summary>
        /// The loaded eligibility object — exposed so the pin-by-look decision core (M4) can re-check
        /// eligibility at the pin site (defense in depth with the wisp spawn gate). Never null (the
        /// fail-safe is an empty set, not null).
        /// </summary>
        public static SeersStoneEligibility Eligibility => _eligibility;

        /// <summary>Whether <see cref="Load"/> has completed (success or fail-safe-empty).</summary>
        public static bool IsLoaded => _loaded;

        /// <summary>
        /// The core query the (later) wisp/raycast code calls: does this hit object earn a wisp?
        /// Delegates to the engine-free core. Fail-safe: if loading never ran or failed, the
        /// eligibility set is empty ⇒ returns false (reveals nothing rather than everything).
        /// </summary>
        public static bool IsEligible(string? prefabName) => _eligibility.IsEligible(prefabName);

        /// <summary>
        /// Load the whitelist: seed the default into the config dir on first run, then read +
        /// parse the owner's file. Idempotent-ish (re-running re-reads); call once at boot.
        /// </summary>
        /// <param name="configDir">BepInEx config dir (BepInEx.Paths.ConfigPath at the call site).</param>
        /// <param name="pluginFolder">Plugin.PluginFolder — where the shipped default lives.</param>
        /// <param name="log">Optional logger sink (path-agnostic so this stays test-reachable).</param>
        public static void Load(string configDir, string pluginFolder, Action<string>? log = null)
        {
            log ??= _ => { };
            try
            {
                var modConfigDir = Path.Combine(configDir, Plugin.ModName.Replace(" ", "."));
                var configPath = Path.Combine(modConfigDir, ConfigFileName);
                var defaultPath = Path.Combine(pluginFolder, DefaultFileName);

                // 1) Seed-on-first-run: if the owner has no file yet, copy the shipped default.
                if (!File.Exists(configPath))
                {
                    Directory.CreateDirectory(modConfigDir);
                    if (File.Exists(defaultPath))
                    {
                        File.Copy(defaultPath, configPath, overwrite: false);
                        log($"[SeersStone] Seeded default whitelist → {configPath}");
                    }
                    else
                    {
                        // Default missing from the pack (shouldn't happen post-build). Fail safe:
                        // empty eligibility, loud log — better than silently revealing nothing
                        // with no explanation, or worse, everything.
                        log($"[SeersStone] WARNING: shipped default not found at {defaultPath}; " +
                            "whitelist is EMPTY (no wisps). Reinstall the mod to restore it.");
                        _eligibility = new SeersStoneEligibility(null);
                        _loaded = true;
                        return;
                    }
                }

                // 2) Read + parse the owner's file.
                var yamlText = File.ReadAllText(configPath);
                var doc = WhitelistDocument.Parse(yamlText);
                _eligibility = new SeersStoneEligibility(doc.AllNames);
                _loaded = true;
                log($"[SeersStone] Whitelist loaded: {_eligibility.Count} eligible prefab(s) " +
                    $"({doc.Pickables.Count} pickable, {doc.OreSurface.Count} ore, {doc.Locations.Count} location).");
            }
            catch (Exception e)
            {
                // Never let a malformed config crash boot. Fail safe to empty (no wisps), log loud.
                _eligibility = new SeersStoneEligibility(null);
                _loaded = true;
                log($"[SeersStone] ERROR loading whitelist (wisps disabled until fixed): {e.Message}");
            }
        }

        /// <summary>Test/diagnostic seam: install a known eligibility set without touching disk.</summary>
        internal static void OverrideForTest(IEnumerable<string>? names)
        {
            _eligibility = new SeersStoneEligibility(names);
            _loaded = true;
        }
    }
}
