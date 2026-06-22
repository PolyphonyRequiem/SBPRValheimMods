// ============================================================================
//  Trailborne v3 (Swamp) — Sunstone threat PROJECTION (Character → ThreatBlip)
// ----------------------------------------------------------------------------
//  Impl spec: docs/v3/planning/sunstone-minimap-handoff-impl-spec.md §2
//  Design   : docs/design/sunstone-lens-minimap-handoff.md §2 (ACCEPTED, PR #214)
//  Card     : t_91e86951
//
//  THE single per-hostile visual derivation, lifted out of SunstoneLensHudOverlay
//  so that all THREE render surfaces consume ONE mapping (zero-drift, AT-LENS-DISC-
//  NODRIFT):
//    • the camera-relative trophy RING (no minimap present — the fallback),
//    • the SBPR carry-disc (nomap-ON), and
//    • the vanilla corner minimap (nomap-OFF).
//
//  Each surface differs ONLY in the world→screen projection (the ring discards the
//  world position and keeps a camera-relative bearing; the disc uses
//  MapSurface.WorldToSurfacePx; the vanilla overlay uses vanilla's own
//  WorldToMapPoint math). Tint / trophy / star-pip count derive IDENTICALLY here,
//  so changing the aggro-colour rule (or the trophy resolution) changes every
//  surface together by construction — they cannot desync.
//
//  Clean-side (ADR-0001): reads base-game Character / CharacterDrop / BaseAI /
//  EnemyHud primitives only (the same surfaces the ring already read). The
//  Rune-of-Awareness colour behaviour is reproduced from vanilla BaseAI primitives;
//  no vanilla UI cloned, no third-party mod code referenced. ADR-0006: the
//  procedural threat-glyph + sprite reuse is asset reading, not prefab cloning.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Sunstone
{
    /// <summary>
    /// One detected hostile, render-agnostic. <see cref="WorldPos"/> is the transform position at
    /// sweep time — the ring discards it (it needs only the camera-relative bearing), the SBPR disc
    /// and the vanilla-minimap overlay project it directly. <see cref="Tint"/>/<see cref="Trophy"/>/
    /// <see cref="Stars"/> are the SINGLE derivation every surface shares (AT-LENS-DISC-NODRIFT).
    /// </summary>
    public readonly struct ThreatBlip
    {
        /// <summary>The live creature ref (kept so a consumer can re-read live state if it ever needs to).</summary>
        public readonly Character Character;
        /// <summary>The creature's transform.position at sweep time (world space).</summary>
        public readonly Vector3 WorldPos;
        /// <summary>Aggro-state tint — yellow (idle/alerted) / orange (aggro-other) / red (aggro-you).</summary>
        public readonly Color Tint;
        /// <summary>The creature's trophy sprite, or null when it has none (consumer falls back to the threat glyph).</summary>
        public readonly Sprite? Trophy;
        /// <summary>Star-pip count = max(0, level - 1).</summary>
        public readonly int Stars;

        public ThreatBlip(Character character, Vector3 worldPos, Color tint, Sprite? trophy, int stars)
        {
            Character = character;
            WorldPos = worldPos;
            Tint = tint;
            Trophy = trophy;
            Stars = stars;
        }
    }

    /// <summary>
    /// The single Character → ThreatBlip derivation, shared by the ring, the SBPR disc, and the
    /// vanilla-minimap overlay. The detection MECHANIC (<see cref="SunstoneLens.GatherHostiles"/>)
    /// is unchanged and stays in <see cref="SunstoneLens"/>; this is only the per-hostile VISUAL
    /// derivation lifted out of <see cref="SunstoneLensHudOverlay"/> so it has exactly one home.
    /// </summary>
    public static class SunstoneProjection
    {
        // ── Aggro-state tint (the Rune-of-Awareness colour code, design §1.8). Single source of
        //    truth for ALL three surfaces (was private to the ring overlay before the lift). ──
        internal static readonly Color CYellow = new Color(0.95f, 0.82f, 0.29f, 1f);  // idle / alerted, no target
        internal static readonly Color COrange = new Color(0.95f, 0.55f, 0.16f, 1f);  // aggroed on ANOTHER player
        internal static readonly Color CRed    = new Color(0.90f, 0.25f, 0.17f, 1f);  // aggroed on YOU

        // ── Variant→sibling TROPHY REMAP (design §Q1 Knob #3a, card t_d17d9b58). ──
        // Some hostiles carry NO Trophy-typed CharacterDrop (juveniles, ranged/summoned variants,
        // boss adds). Rather than fall straight to the generic glyph, a variant is first remapped
        // onto a sibling SPECIES that DOES drop a trophy, so e.g. a Greyling wears the Greydwarf
        // trophy (the canonical case — Greyling drops Resin only, no Trophy; wiki Greyling.md).
        //
        // KEYS + VALUES are vanilla PREFAB names (clone-suffix-stripped), grounded against the
        // ServersideQoL prefab catalog + the vanilla decomp (ADR-0001 fair-to-read) — NOT display
        // names. The map is FAIL-SAFE by construction: an unknown key simply isn't found (→ glyph),
        // and a value whose sibling itself has no trophy resolves to null (→ glyph) — neither
        // crashes nor silently omits a threat. Daniel grows this from the startup dump (Knob #3c).
        //
        // ⚠ Grounded prefab-name note: the design draft wrote "DraugrRanged"; the REAL vanilla prefab
        // is "Draugr_Ranged" (underscore) — verified 0 hits for "DraugrRanged" vs 14 for
        // "Draugr_Ranged" across the reference-mod corpus. Using the wrong key would have been a
        // silent no-op (the bears+Vegvísir class of bug). Every key/value below is corpus-verified.
        internal static readonly Dictionary<string, string> _trophyRemap = new Dictionary<string, string>
        {
            // Black Forest
            { "Greyling",            "Greydwarf" },   // Greyling drops Resin only, no Trophy → Greydwarf trophy (canonical)
            // Meadows / juveniles (Growup variants — young carry no trophy)
            { "Boar_piggy",          "Boar"      },
            { "Wolf_cub",            "Wolf"      },   // Mountain juvenile
            { "Lox_Calf",            "Lox"       },   // Plains juvenile
            { "Asksvin_hatchling",   "Asksvin"   },   // Ashlands juvenile
            // Swamp — the ranged Draugr variant shares the base Draugr trophy
            { "Draugr_Ranged",       "Draugr"    },
            // Plains — Fuling ranged/caster variants → base Fuling (Goblin) trophy
            { "GoblinArcher",        "Goblin"    },
            { "GoblinShaman",        "GoblinShaman" }, // GoblinShaman DOES have its own trophy; identity entry is harmless (self-resolves) — kept explicit so the dump shows it mapped
            { "GoblinShaman_Hildir", "GoblinShaman" },
            // Ashlands — Charred summoned/twitcher variants → base Charred melee
            { "Charred_Twitcher",          "Charred_Melee" },
            { "Charred_Twitcher_Summoned", "Charred_Melee" },
            { "Charred_Archer",            "Charred_Melee" },
            { "Charred_Mage",              "Charred_Melee" },
            // Mistlands — Dverger caster variants → base Dverger
            { "DvergerMage",         "Dverger"   },
            { "DvergerMageFire",     "Dverger"   },
            { "DvergerMageIce",      "Dverger"   },
            { "DvergerMageSupport",  "Dverger"   },
            // Mistlands — Seeker brood (no own trophy) → SeekerBrute/Seeker base
            { "SeekerBrood",         "Seeker"    },
            // Ashlands — non-sleeping Morgen shares the Morgen trophy
            { "Morgen_NonSleeping",  "Morgen"    },
        };

        // ── Startup unmapped-creature DUMP state (design §Q1 Knob #3c). Runs ONCE per session at
        //    ZNetScene-ready: enumerates every Character prefab, resolves each to (trophy | remap |
        //    none), and logs the "none" set as ONE reviewable block so Daniel can grow _trophyRemap. ──
        public const bool DefaultDumpUnmappedCreatures = true;   // Knob #3c — default ON (Daniel)
        private static bool _unmappedDumpDone;

        // ── Static caches (resolved once, reused) — shared across every surface. ──
        private static readonly Dictionary<string, Sprite?> _trophyCache = new Dictionary<string, Sprite?>();
        private static Sprite? _starSprite;        // harvested from the vanilla EnemyHud nameplate
        private static bool    _starHarvestDone;   // stop retrying once resolved (or proven absent)
        private static Sprite? _threatGlyph;       // generic glyph for trophy-less hostiles
        private static bool    _threatGlyphDone;   // resolved once (PNG, else procedural)
        private static Sprite? _proceduralThreat;  // code-generated danger triangle (no disk dependency)
        private static Sprite? _dotSprite;         // code-generated near-white filled disc (the minimap blip dot)

        // Loaded by BARE filename: pack-modpack.sh flattens assets/icons/items/*.png into the plugin
        // folder root, and Assets.LoadPngAsSprite resolves Path.Combine(PluginFolder, filename).
        private const string ThreatGlyphIcon = "threat_fallback_v0.1.png";

        /// <summary>
        /// Map a raw <see cref="SunstoneLens.GatherHostiles"/> result into render-ready blips. The ONLY
        /// place tint/trophy/stars are derived — every surface calls this, so they cannot drift
        /// (AT-LENS-DISC-NODRIFT). Output is written into the caller-owned <paramref name="outBlips"/>
        /// list (cleared first) to avoid per-sweep allocations.
        /// </summary>
        public static void Project(IReadOnlyList<Character> hostiles, Player local, List<ThreatBlip> outBlips)
        {
            outBlips.Clear();
            if (hostiles == null) return;
            for (int i = 0; i < hostiles.Count; i++)
            {
                var c = hostiles[i];
                if (c == null) continue;
                outBlips.Add(new ThreatBlip(
                    c,
                    c.transform.position,
                    AggroTint(c, local),
                    ResolveTrophySprite(c),                 // null → consumer uses ThreatGlyph()
                    Mathf.Max(0, c.GetLevel() - 1)));
            }
        }

        // ───────────────────────────────────────────────
        // THE LIFTED HELPERS (were private to SunstoneLensHudOverlay; now the one shared copy)
        // ───────────────────────────────────────────────

        /// <summary>
        /// Aggro-state tint (the Rune-of-Awareness colour code, design §1.8) — reproduced from vanilla
        /// BaseAI: <c>IsAlerted()</c> + <c>GetTargetCreature()</c>, the same surface vanilla's own
        /// EnemyHud reads. red = targeting the local player, orange = targeting another player, yellow =
        /// idle / alerted-without-a-player-target. Fails safe to yellow.
        /// </summary>
        public static Color AggroTint(Character c, Player localPlayer)
        {
            try
            {
                var ai = c.GetBaseAI();
                if (ai == null || !ai.IsAlerted()) return CYellow;
                var target = ai.GetTargetCreature();
                if (target == null) return CYellow;
                if (target == (Character)localPlayer) return CRed;
                if (target.IsPlayer()) return COrange;
                return CYellow;
            }
            catch
            {
                return CYellow;
            }
        }

        /// <summary>
        /// The trophy sprite for a creature: its <c>CharacterDrop</c> drop whose item is
        /// <c>ItemType.Trophy</c>, taking that item's <c>m_icons[0]</c>. Resolved once per creature
        /// prefab and cached (a null result is cached too, so a trophy-less hostile isn't re-scanned).
        ///
        /// HYBRID trophy-less policy (design §Q1 Knob #3, card t_d17d9b58): when the creature's own
        /// walk finds no Trophy drop, the <see cref="_trophyRemap"/> variant→sibling table is consulted
        /// BEFORE caching a null — a Greyling resolves the Greydwarf sibling's trophy and caches it
        /// under the ORIGINAL key ("Greyling"), so the variant wears a sensible sibling trophy. Only a
        /// creature that is neither a trophy-bearer NOR a remapped variant caches null (→ the consumer
        /// falls back to <see cref="ThreatGlyph"/>; a threat is never silently dropped).
        /// </summary>
        public static Sprite? ResolveTrophySprite(Character c)
        {
            string key = StripCloneSuffix(c.name);
            if (_trophyCache.TryGetValue(key, out var cached)) return cached;

            // 1. Direct walk: does THIS creature carry its own Trophy drop?
            Sprite? sprite = ResolveTrophyOnCharacter(c, key);

            // 2. Variant→sibling remap (Knob #3a): if the creature itself has no trophy, see whether
            //    it's a known variant of a sibling species that does. Resolve the sibling's sprite off
            //    its ZNetScene prefab blueprint (GetPrefab fires no Awake — reading, not cloning).
            if (sprite == null && _trophyRemap.TryGetValue(key, out var siblingName) && siblingName != key)
                sprite = ResolveTrophyOnPrefabName(siblingName);

            _trophyCache[key] = sprite;   // cache the result (possibly null → consumer uses ThreatGlyph)
            return sprite;
        }

        /// <summary>
        /// Walk a live <see cref="Character"/>'s <c>CharacterDrop</c> for its Trophy-typed drop and
        /// return that item's <c>m_icons[0]</c>, or null. The per-frame-safe inner walk shared by the
        /// direct resolution and (via the prefab variant) the remap path. <paramref name="logKey"/> is
        /// only used for the warning line.
        /// </summary>
        private static Sprite? ResolveTrophyOnCharacter(Character c, string logKey)
        {
            try
            {
                var cd = c.GetComponent<CharacterDrop>();
                return ResolveTrophyInDrops(cd);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Sunstone] trophy resolve failed for {logKey}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resolve the Trophy sprite for a creature identified by PREFAB NAME — read its ZNetScene
        /// prefab blueprint (<c>ZNetScene.GetPrefab</c> fires no Awake, so reading it is clean-side +
        /// ADR-0006-safe) and walk its <c>CharacterDrop</c>. Used by the variant→sibling remap to pull
        /// e.g. the Greydwarf trophy for a Greyling. Null-safe: a missing prefab / no trophy → null.
        /// </summary>
        private static Sprite? ResolveTrophyOnPrefabName(string prefabName)
        {
            try
            {
                var zns = ZNetScene.instance;
                var prefab = zns != null ? zns.GetPrefab(prefabName) : null;
                if (prefab == null) return null;
                var cd = prefab.GetComponent<CharacterDrop>();
                return ResolveTrophyInDrops(cd);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Sunstone] sibling trophy resolve failed for {prefabName}: {e.Message}");
                return null;
            }
        }

        /// <summary>The shared CharacterDrop→ItemType.Trophy→m_icons[0] walk (null-safe).</summary>
        private static Sprite? ResolveTrophyInDrops(CharacterDrop? cd)
        {
            if (cd == null || cd.m_drops == null) return null;
            foreach (var d in cd.m_drops)
            {
                if (d == null || d.m_prefab == null) continue;
                var id = d.m_prefab.GetComponent<ItemDrop>();
                var shared = id != null ? id.m_itemData?.m_shared : null;
                if (shared == null) continue;
                if (shared.m_itemType != ItemDrop.ItemData.ItemType.Trophy) continue;
                if (shared.m_icons != null && shared.m_icons.Length > 0 && shared.m_icons[0] != null)
                    return shared.m_icons[0];
            }
            return null;
        }

        /// <summary>
        /// Default-ON startup unmapped-creature DUMP (design §Q1 Knob #3c, card t_d17d9b58). Runs ONCE
        /// per session (idempotent via <see cref="_unmappedDumpDone"/>): enumerate every registered
        /// Character prefab (<c>ZNetScene.instance.m_prefabs</c>, public) carrying a <c>Character</c>
        /// component, resolve each to (own trophy | remap-sibling | none), and log the "none" set as
        /// ONE reviewable block so Daniel can grow <see cref="_trophyRemap"/> over time. This is a
        /// FULL-CATALOG scan (not a lazy log-on-first-sighting trickle) — the complete list in one place.
        ///
        /// Read-only + side-effect-free on game state: it only reads prefab blueprints (GetPrefab/
        /// GetComponent fire no Awake) and writes the per-prefab trophy cache (the same cache the live
        /// sweep populates lazily). Safe to call at ZNetScene-ready. No-op on the dedicated server path
        /// only in the sense that the dump is purely diagnostic; gate the CALL on the config flag.
        /// </summary>
        public static void DumpUnmappedCreatures()
        {
            if (_unmappedDumpDone) return;
            _unmappedDumpDone = true;   // set first: a mid-scan exception must not re-trigger the scan
            try
            {
                var zns = ZNetScene.instance;
                if (zns == null || zns.m_prefabs == null)
                {
                    Plugin.Log.LogWarning("[Trailborne/Sunstone] DumpUnmappedCreatures: ZNetScene not ready; skipping.");
                    return;
                }

                int characters = 0, withTrophy = 0, remapped = 0;
                var unmapped = new List<string>();
                var seen = new HashSet<string>();

                foreach (var prefab in zns.m_prefabs)
                {
                    if (prefab == null) continue;
                    if (prefab.GetComponent<Character>() == null) continue;   // creature prefabs only
                    string key = StripCloneSuffix(prefab.name);
                    if (!seen.Add(key)) continue;                            // de-dupe by prefab name
                    characters++;

                    var cd = prefab.GetComponent<CharacterDrop>();
                    if (ResolveTrophyInDrops(cd) != null) { withTrophy++; continue; }

                    // No own trophy — is it a known remap variant whose sibling has one?
                    if (_trophyRemap.TryGetValue(key, out var sib) && sib != key && ResolveTrophyOnPrefabName(sib) != null)
                    { remapped++; continue; }

                    unmapped.Add(key);   // genuinely unmapped → renders the generic threat glyph
                }

                unmapped.Sort(System.StringComparer.Ordinal);
                var sb = new System.Text.StringBuilder();
                sb.Append("[Trailborne/Sunstone] Unmapped-creature dump (DumpUnmappedCreatures): ")
                  .Append(characters).Append(" Character prefabs scanned — ")
                  .Append(withTrophy).Append(" with own trophy, ")
                  .Append(remapped).Append(" remapped to a sibling, ")
                  .Append(unmapped.Count).Append(" UNMAPPED (render the generic threat glyph).");
                if (unmapped.Count > 0)
                {
                    sb.Append("\n  Unmapped (no own Trophy drop, no remap entry) — add to SunstoneProjection._trophyRemap if a sibling fits:");
                    foreach (var name in unmapped) sb.Append("\n    - ").Append(name);
                }
                Plugin.Log.LogInfo(sb.ToString());
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Sunstone] DumpUnmappedCreatures failed (non-fatal): {e.Message}");
            }
        }

        /// <summary>
        /// Harvest the real vanilla nameplate STAR sprite from <c>EnemyHud.m_baseHud</c>'s
        /// <c>level_2</c>/<c>level_3</c> children. Returns null until <c>EnemyHud.instance</c> + its
        /// base-hud template exist (then caches whatever it found — possibly null). Daniel 2026-06-19:
        /// "use the Valheim stars used to decorate the monster nameplates."
        /// </summary>
        public static Sprite? StarSprite()
        {
            if (_starHarvestDone) return _starSprite;
            try
            {
                var eh = EnemyHud.instance;
                var baseHud = eh != null ? eh.m_baseHud : null;
                if (baseHud == null) return null;   // not ready yet — retry next render

                _starSprite = FindStarSprite(baseHud.transform, "level_2")
                              ?? FindStarSprite(baseHud.transform, "level_3");
                _starHarvestDone = true;            // template exists: accept whatever we found (may be null)
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Sunstone] star-sprite harvest failed: {e.Message}");
                _starHarvestDone = true;
            }
            return _starSprite;
        }

        private static Sprite? FindStarSprite(Transform root, string childName)
        {
            var t = root.Find(childName);
            if (t == null) return null;
            var img = t.GetComponent<Image>();
            if (img != null && img.sprite != null) return img.sprite;
            img = t.GetComponentInChildren<Image>(includeInactive: true);
            return img != null ? img.sprite : null;
        }

        // ────────────────────────────────────────────────────────────────────────
        //  SHARED MINIMAP DECORATION (card t_aab051ae) — star pips + off-edge rim clamp.
        //  Both minimap hosts (the SBPR carry-disc in MapSurface and the vanilla corner
        //  overlay in SunstoneMinimapThreatLayer) call these ONE copy, so star rendering
        //  and rim behaviour can't drift between the two surfaces (extends AT-LENS-DISC-
        //  NODRIFT to the decoration, not just the blip). The HUD ring keeps its own
        //  richer star row (RenderStars) — it has the vertical room; the minimap pips are
        //  a compact glanceable row sized for corner-map scale.
        // ────────────────────────────────────────────────────────────────────────

        /// <summary>On-minimap star-pip size (px). Smaller than the ring's 12px row — the minimap
        /// blip itself is ~14px, so the pips ride just above it without swamping the dot/trophy.</summary>
        public const float MinimapPipPx = 7f;

        /// <summary>
        /// Mount a compact row of star pips just above a minimap blip, parented under
        /// <paramref name="blipRt"/>. Used by BOTH minimap surfaces so a 2-star Greydwarf reads the
        /// same on the disc and the vanilla corner map. Uses the real vanilla nameplate star sprite
        /// (<see cref="StarSprite"/>); falls back to a Unicode ★ Text when the harvest hasn't resolved
        /// (e.g. very early, or a headless context — never blank). Pips are tinted by the aggro colour
        /// so the alert state reads on the stars too. A zero-star (level-1) hostile mounts nothing.
        /// </summary>
        public static void MountStarPips(RectTransform blipRt, int stars, float blipPx, Color tint)
        {
            if (blipRt == null || stars <= 0) return;
            stars = Mathf.Min(stars, 5);   // 2-star is the vanilla cap; clamp defensively for modded levels

            var rowGo = new GameObject("stars", typeof(RectTransform));
            rowGo.transform.SetParent(blipRt, worldPositionStays: false);
            var rowRt = rowGo.GetComponent<RectTransform>();
            rowRt.anchorMin = rowRt.anchorMax = rowRt.pivot = new Vector2(0.5f, 0.5f);
            // Sit the row just above the blip (blip is centre-pivoted at blipPx tall).
            rowRt.anchoredPosition = new Vector2(0f, blipPx * 0.5f + MinimapPipPx * 0.6f);
            rowRt.sizeDelta = Vector2.zero;

            Sprite? star = StarSprite();
            float startX = -(stars - 1) * 0.5f * MinimapPipPx;

            if (star != null)
            {
                for (int i = 0; i < stars; i++)
                {
                    var pgo = new GameObject($"pip_{i}", typeof(RectTransform));
                    pgo.transform.SetParent(rowRt, worldPositionStays: false);
                    var prt = pgo.GetComponent<RectTransform>();
                    prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0.5f);
                    prt.sizeDelta = new Vector2(MinimapPipPx, MinimapPipPx);
                    prt.anchoredPosition = new Vector2(startX + i * MinimapPipPx, 0f);
                    var img = pgo.AddComponent<Image>();
                    img.raycastTarget = false;
                    img.preserveAspect = true;
                    img.sprite = star;
                    img.color = tint;
                }
            }
            else
            {
                // Fallback so the star COUNT is never lost when the sprite hasn't harvested.
                var tgo = new GameObject("pip_text", typeof(RectTransform));
                tgo.transform.SetParent(rowRt, worldPositionStays: false);
                var trt = tgo.GetComponent<RectTransform>();
                trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
                trt.sizeDelta = new Vector2(40f, 10f);
                var txt = tgo.AddComponent<Text>();
                txt.font = SBPR.Trailborne.Features.Signs.VanillaUISkin.Font
                           ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
                txt.fontSize = 9;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.horizontalOverflow = HorizontalWrapMode.Overflow;
                txt.verticalOverflow = VerticalWrapMode.Overflow;
                txt.raycastTarget = false;
                txt.text = new string('\u2605', stars);
                txt.color = tint;
            }
        }

        /// <summary>
        /// The generic threat glyph for trophy-less hostiles (summoned minions, some boss adds). Loads
        /// the shipped near-white PNG (so the aggro tint reads); if it didn't ship, generates a
        /// procedural danger-triangle so a trophy-less hostile is NEVER invisible (degrades to "still
        /// shown," never a crash).
        /// </summary>
        public static Sprite ThreatGlyph()
        {
            if (_threatGlyphDone && _threatGlyph != null) return _threatGlyph;
            if (!_threatGlyphDone)
            {
                _threatGlyph = Assets.LoadPngAsSprite(ThreatGlyphIcon);
                _threatGlyphDone = true;
                if (_threatGlyph == null)
                    Plugin.Log.LogWarning(
                        $"[Trailborne/Sunstone] threat-fallback glyph '{ThreatGlyphIcon}' did not load; "
                        + "using procedural danger-triangle (trophy-less hostiles still render).");
            }
            return _threatGlyph ?? ProceduralThreatGlyph();
        }

        /// <summary>
        /// A code-generated near-white filled disc, the minimap blip DOT (BlipStyle.Dots). White so
        /// the aggro tint reads via Image.color; soft 1-px edge so it isn't aliased. No disk asset —
        /// the dot is guaranteed even if the modpack ships zero PNGs.
        /// </summary>
        public static Sprite DotSprite()
        {
            if (_dotSprite != null) return _dotSprite;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            float cx = size / 2f, cy = size / 2f;
            float r = size / 2f - 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // Solid inside r, 1-px feathered edge, transparent outside.
                    float a = Mathf.Clamp01(r - d);
                    px[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            _dotSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _dotSprite.name = "SBPR_ThreatDot";
            return _dotSprite;
        }

        /// <summary>A code-generated near-white danger triangle (no disk dependency).</summary>
        private static Sprite ProceduralThreatGlyph()
        {
            if (_proceduralThreat != null) return _proceduralThreat;
            const int s = 128;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var clear = new Color(1f, 1f, 1f, 0f);
            var px = new Color[s * s];
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            var ink = new Color(0.92f, 0.92f, 0.92f, 1f);
            // Triangle vertices (apex top, base bottom), drawn as a thick outline.
            var apex = new Vector2(s / 2f, s * 0.12f);
            var bl   = new Vector2(s * 0.14f, s * 0.84f);
            var br   = new Vector2(s * 0.86f, s * 0.84f);
            DrawThickLine(px, s, apex, bl, ink, 7f);
            DrawThickLine(px, s, bl, br, ink, 7f);
            DrawThickLine(px, s, br, apex, ink, 7f);
            // Exclamation bar + dot.
            for (int y = (int)(s * 0.34f); y < (int)(s * 0.62f); y++)
                for (int x = (int)(s / 2f - 4); x <= (int)(s / 2f + 4); x++)
                    if (x >= 0 && x < s && y >= 0 && y < s) px[y * s + x] = ink;
            FillDisc(px, s, new Vector2(s / 2f, s * 0.70f), 5.5f, ink);

            tex.SetPixels(px);
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            _proceduralThreat = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
            _proceduralThreat.name = "SBPR_ThreatGlyphProc";
            return _proceduralThreat;
        }

        private static void DrawThickLine(Color[] px, int s, Vector2 a, Vector2 b, Color c, float w)
        {
            float len = Vector2.Distance(a, b);
            int steps = Mathf.CeilToInt(len);
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, steps == 0 ? 0f : (float)i / steps);
                FillDisc(px, s, p, w * 0.5f, c);
            }
        }

        private static void FillDisc(Color[] px, int s, Vector2 c, float r, Color col)
        {
            int x0 = Mathf.Max(0, (int)(c.x - r)), x1 = Mathf.Min(s - 1, (int)(c.x + r));
            int y0 = Mathf.Max(0, (int)(c.y - r)), y1 = Mathf.Min(s - 1, (int)(c.y + r));
            float r2 = r * r;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - c.x, dy = y - c.y;
                    if (dx * dx + dy * dy <= r2) px[y * s + x] = col;
                }
        }

        // Mirror of vanilla ItemDrop.GetPrefabName clone-suffix strip: cut at the first '(' or ' '
        // so "Draugr(Clone)" matches the cache key "Draugr".
        private static string StripCloneSuffix(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int i = name.IndexOfAny(new[] { '(', ' ' });
            return i >= 0 ? name.Substring(0, i) : name;
        }
    }
}
