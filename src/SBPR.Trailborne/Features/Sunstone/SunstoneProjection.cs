// ============================================================================
//  Trailborne v3 (Swamp) — Sunstone detection PROJECTION (the one producer)
// ----------------------------------------------------------------------------
//  Design   : docs/design/sunstone-lens-minimap-handoff.md §2 (ACCEPTED)
//  Impl spec: docs/v3/planning/sunstone-minimap-handoff-impl-spec.md §2
//  Card     : t_54c989d3 (impl) ← t_3129842a (design)
//
//  THE ZERO-DRIFT SEAM (AT-LENS-DISC-NODRIFT). The detection MECHANIC is render-
//  agnostic and unchanged (SunstoneLens.GatherHostiles → List<Character>). What
//  USED to be PRIVATE to SunstoneLensHudOverlay — the per-hostile VISUAL derivation
//  (aggro tint, trophy sprite, star sprite, pip count, the trophy-less glyph) — is
//  LIFTED here so the camera-relative ring, the SBPR carry-disc, and the vanilla-
//  minimap overlay are THREE CONSUMERS OF ONE MAPPING. If each surface derived tint
//  or trophy independently, a future tweak to the aggro-colour rule would silently
//  desync them; with one producer, changing the rule here changes every surface
//  together. This mirrors the WorldPins.ResolveLabel anti-drift rule the minimap-vs-
//  viewer label sites already share.
//
//  The bodies below are MOVED verbatim from SunstoneLensHudOverlay (the ring's
//  byte-identical behaviour, task item 2a) — same trophy cache, same EnemyHud star
//  harvest, same BaseAI aggro reads, same procedural fallbacks — so lifting them is
//  a pure refactor with no behavioural change to the ring.
//
//  Clean-side (ADR-0001): reads base-game Character / CharacterDrop / ItemDrop /
//  BaseAI / EnemyHud only (fair to read + adapt). The procedural fallbacks are our
//  own art. No third-party mod code.
// ============================================================================

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SBPR.Trailborne.Runtime;
using SBPR.Trailborne.Features.Cartography;

namespace SBPR.Trailborne.Features.Sunstone
{
    /// <summary>
    /// The SINGLE per-hostile projection <c>Character → <see cref="ThreatBlip"/></c> every detection
    /// surface consumes (design §2). Also the <see cref="IThreatMarkerProvider"/> the SBPR carry-disc
    /// pulls from: <see cref="CollectThreatBlips"/> sweeps the live hostiles through <see cref="Project"/>,
    /// so the disc and the ring render byte-identical tint/trophy/pips. Static + caches — resolved once,
    /// reused; safe on the client (the only place a Hud / Minimap exists).
    /// </summary>
    public static class SunstoneProjection
    {
        // ── Aggro-state tint (the Rune-of-Awareness colour code, §1.8). Moved from the overlay
        //    verbatim so the ring's colours are byte-unchanged. ──
        private static readonly Color CYellow = new Color(0.95f, 0.82f, 0.29f, 1f);  // idle / alerted, no target
        private static readonly Color COrange = new Color(0.95f, 0.55f, 0.16f, 1f);  // aggroed on ANOTHER player
        private static readonly Color CRed    = new Color(0.90f, 0.25f, 0.17f, 1f);  // aggroed on YOU

        // ── Static caches (resolved once, reused) — moved from the overlay. ──
        private static readonly Dictionary<string, Sprite?> _trophyCache = new Dictionary<string, Sprite?>();
        private static Sprite? _starSprite;        // harvested from vanilla EnemyHud nameplate
        private static bool    _starHarvestDone;   // stop retrying once resolved (or proven absent)
        private static Sprite? _threatGlyph;       // generic glyph for trophy-less hostiles
        private static bool    _threatGlyphDone;   // resolved once (PNG, else procedural)
        private static Sprite? _proceduralThreat;
        // Loaded by BARE filename: pack-modpack.sh flattens assets/icons/items/*.png into the plugin
        // folder root, and Assets.LoadPngAsSprite resolves Path.Combine(PluginFolder, filename).
        private const string ThreatGlyphIcon = "threat_fallback_v0.1.png";

        // ───────────────────────────────────────────────
        // THE PROJECTION — Character → ThreatBlip (one producer; every surface consumes it)
        // ───────────────────────────────────────────────

        /// <summary>
        /// Map one detected hostile to its shared <see cref="ThreatBlip"/>: live world position, aggro
        /// tint, trophy sprite (null ⇒ trophy-less → consumer draws a glyph), and star-pip count. The
        /// ONE derivation all three surfaces consume — change the rule here and every surface changes
        /// together (AT-LENS-DISC-NODRIFT).
        /// </summary>
        public static ThreatBlip Project(Character c, Player localPlayer)
        {
            Color tint = AggroTint(c, localPlayer);
            Sprite? trophy = ResolveTrophySprite(c);
            int stars = Mathf.Max(0, c.GetLevel() - 1);
            return new ThreatBlip(c.transform.position, tint, trophy, stars);
        }

        /// <summary>
        /// Sweep the live hostiles within <paramref name="boundRadius"/> of <paramref name="boundCenter"/>
        /// and append each as a <see cref="ThreatBlip"/>. Single-sourced from the SAME detection mechanic
        /// the ring's pump drives: it prefers the overlay's published hostile list (the pump's output,
        /// single source) and falls back to a direct gather ONLY before the pump's first publish
        /// (defensive — never a blank disc on the first frame). GATED on the lens being active
        /// (<see cref="SunstoneLens.IsLensActive"/>): a disc must NEVER show threats without a worn +
        /// charged lens, regardless of pump state. Reuses a static scratch list to avoid per-rebuild
        /// allocation. Client-only by construction (no Player / Minimap server-side).
        /// </summary>
        private static readonly List<Character> _scratch = new List<Character>();
        public static void CollectThreatBlips(Vector3 boundCenter, float boundRadius, List<ThreatBlip> results)
        {
            var player = Player.m_localPlayer;
            if (player == null) return;
            // THE GATE: no active lens → no blips on the disc, full stop. Correct even if the ring
            // overlay's pump hasn't run yet (the disc is driven independently from MapSurface.RebuildOverlay).
            if (!SunstoneLens.IsLensActive(player)) return;

            // Prefer the pump's published list (single source — AT-LENS-DISC-NODRIFT / -PUMP). Fall back
            // to a direct gather only if the pump hasn't published this session yet (null), so the disc
            // is never blank on the first frame before the Hud overlay's Update has run once.
            var hostiles = SunstoneLensHudOverlay.LiveHostilesOrNull;
            if (hostiles == null)
            {
                float r = Plugin.LensDetectRadius?.Value ?? SunstoneLens.DefaultDetectRadius;
                SunstoneLens.GatherHostiles(player, r, _scratch);
                hostiles = _scratch;
            }

            float r2 = boundRadius > 0f ? boundRadius * boundRadius : float.MaxValue;
            foreach (var c in hostiles)
            {
                if (c == null || c.IsDead()) continue;
                if (boundRadius > 0f)
                {
                    Vector3 d = c.transform.position - boundCenter;
                    d.y = 0f;
                    if (d.sqrMagnitude > r2) continue;
                }
                results.Add(Project(c, player));
            }
        }

        // ───────────────────────────────────────────────
        // PER-HOSTILE DERIVATION (moved verbatim from SunstoneLensHudOverlay — clean-side)
        // ───────────────────────────────────────────────

        /// <summary>
        /// The trophy sprite for a creature: its <c>CharacterDrop</c> drop whose item is
        /// <c>ItemType.Trophy</c>, taking that item's <c>m_icons[0]</c>. Resolved once per creature
        /// prefab and cached (a null result is cached too, so a trophy-less hostile isn't re-scanned).
        /// </summary>
        public static Sprite? ResolveTrophySprite(Character c)
        {
            string key = StripCloneSuffix(c.name);
            if (_trophyCache.TryGetValue(key, out var cached)) return cached;

            Sprite? sprite = null;
            try
            {
                var cd = c.GetComponent<CharacterDrop>();
                if (cd != null && cd.m_drops != null)
                {
                    foreach (var d in cd.m_drops)
                    {
                        if (d == null || d.m_prefab == null) continue;
                        var id = d.m_prefab.GetComponent<ItemDrop>();
                        var shared = id != null ? id.m_itemData?.m_shared : null;
                        if (shared == null) continue;
                        if (shared.m_itemType != ItemDrop.ItemData.ItemType.Trophy) continue;
                        if (shared.m_icons != null && shared.m_icons.Length > 0 && shared.m_icons[0] != null)
                        {
                            sprite = shared.m_icons[0];
                            break;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Sunstone] trophy resolve failed for {key}: {e.Message}");
            }

            _trophyCache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// Harvest the real vanilla nameplate STAR sprite from <c>EnemyHud.m_baseHud</c>'s
        /// <c>level_2</c>/<c>level_3</c> children (the 1★/2★ decorations). Returns null until
        /// <c>EnemyHud.instance</c> + its base-hud template exist (then caches).
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

        /// <summary>
        /// Aggro-state tint (the Rune-of-Awareness colour code, §1.8) — reproduced from vanilla BaseAI:
        /// red = targeting the local player, orange = targeting another player, yellow = idle/alerted-
        /// without-a-player-target. Fails safe to yellow.
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
        /// The generic threat glyph for trophy-less hostiles (summoned minions, some boss adds). Loads
        /// the shipped PNG (near-white so the aggro tint reads); if it didn't ship, generates a
        /// procedural danger-triangle so a trophy-less hostile is NEVER invisible.
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
            var apex = new Vector2(s / 2f, s * 0.12f);
            var bl   = new Vector2(s * 0.14f, s * 0.84f);
            var br   = new Vector2(s * 0.86f, s * 0.84f);
            DrawThickLine(px, s, apex, bl, ink, 7f);
            DrawThickLine(px, s, bl, br, ink, 7f);
            DrawThickLine(px, s, br, apex, ink, 7f);
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

    /// <summary>
    /// Thin <see cref="IThreatMarkerProvider"/> adapter so the Cartography disc can pull Sunstone blips
    /// without Cartography referencing Sunstone (the coupling arrow stays Sunstone → Cartography). The
    /// SBPR carry-disc registers ONE of these (SunstoneLensHudOverlay.EnsureBuilt); it just forwards to
    /// the static <see cref="SunstoneProjection.CollectThreatBlips"/> (which gates on lens-active and is
    /// single-sourced from the live pump).
    /// </summary>
    public sealed class SunstoneThreatProvider : IThreatMarkerProvider
    {
        public void CollectThreatBlips(Vector3 boundCenter, float boundRadius, List<ThreatBlip> results)
            => SunstoneProjection.CollectThreatBlips(boundCenter, boundRadius, results);
    }
}
