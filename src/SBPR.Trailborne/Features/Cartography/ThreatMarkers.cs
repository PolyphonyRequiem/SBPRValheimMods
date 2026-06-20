// ============================================================================
//  Trailborne v3 (Swamp) — transient threat-marker SEAM (Cartography-owned)
// ----------------------------------------------------------------------------
//  Design   : docs/design/sunstone-lens-minimap-handoff.md §3.6 (ACCEPTED)
//  Impl spec: docs/v3/planning/sunstone-minimap-handoff-impl-spec.md §2 / §3
//  Card     : t_54c989d3 (impl) ← t_3129842a (design)
//
//  THE COUPLING DIRECTION (design §3.6, load-bearing). Sunstone → (registers into)
//  → THIS Cartography seam. Cartography OWNS the disc and ASKS a registered provider
//  "any threat blips in range?" each rebuild — it never reaches into Sunstone. So
//  every type here lives in the CARTOGRAPHY namespace and references NOTHING in
//  Sunstone; the arrow points one way (Sunstone depends on Cartography, never the
//  reverse), exactly as WorldPins (Cartography) is consumed by MarkerSigns without
//  Cartography depending on MarkerSigns. The disc (MapSurface.RebuildOverlay) pulls
//  from <see cref="ThreatMarkerRegistry"/>, mirroring its existing
//  WorldPins.CollectInDiscPins pull (MapSurface.cs:633).
//
//  ZERO-DRIFT (AT-LENS-DISC-NODRIFT). The SHARED <see cref="ThreatBlip"/> is the one
//  projection all three surfaces (camera-relative ring, SBPR disc, vanilla-minimap
//  overlay) consume. Sunstone derives it ONCE (SunstoneProjection); the tint / trophy
//  / pips are identical on every surface because there is one producer.
//
//  Clean-side (ADR-0001): all SBPR-authored; no game/third-party code involved.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// One per-hostile render datum — the SHARED projection the ring, the SBPR disc, and the
    /// vanilla-minimap overlay all consume (design §2). Carries the full world position (the disc
    /// + vanilla overlay project it to their own surface; the ring discards it and keeps only the
    /// bearing), the aggro tint, the optional trophy sprite (null ⇒ trophy-less hostile → a generic
    /// glyph), and the star-pip count. Deriving these once (SunstoneProjection) and handing the SAME
    /// struct to every surface is the zero-drift requirement (AT-LENS-DISC-NODRIFT) — a future tweak
    /// to the aggro-colour rule changes all surfaces together because there is one producer.
    /// </summary>
    public struct ThreatBlip
    {
        /// <summary>The hostile's live world position (<c>c.transform.position</c>).</summary>
        public Vector3 WorldPos;
        /// <summary>Aggro-state tint (the Rune-of-Awareness colour: yellow/orange/red).</summary>
        public Color Tint;
        /// <summary>Trophy sprite, or null for a trophy-less hostile (consumer falls back to a glyph).</summary>
        public Sprite? Trophy;
        /// <summary>Star-pip count (<c>GetLevel()-1</c>), 0 for a 0-star.</summary>
        public int Stars;

        public ThreatBlip(Vector3 worldPos, Color tint, Sprite? trophy, int stars)
        {
            WorldPos = worldPos;
            Tint = tint;
            Trophy = trophy;
            Stars = stars;
        }
    }

    /// <summary>
    /// The seam a feature registers to feed transient threat markers onto the cartography disc
    /// (design §3.6). Cartography owns the disc; it ASKS a registered provider for blips each
    /// rebuild. Sunstone implements this and registers into <see cref="ThreatMarkerRegistry"/>;
    /// Cartography sees only an <c>IThreatMarkerProvider</c>, never a Lens — so the dependency
    /// arrow stays Sunstone → Cartography. Mirrors the WorldPins.CollectInDiscPins shape.
    /// </summary>
    public interface IThreatMarkerProvider
    {
        /// <summary>
        /// Append the live threat blips within <paramref name="boundRadius"/> of
        /// <paramref name="boundCenter"/> to <paramref name="results"/> (caller-owned, reused). A
        /// provider that has nothing to show (lens off / handoff routed to the ring) appends nothing.
        /// Must not throw out — the registry guards, but a clean provider keeps its own try/catch.
        /// </summary>
        void CollectThreatBlips(Vector3 boundCenter, float boundRadius, List<ThreatBlip> results);
    }

    /// <summary>
    /// Registration point + safe pull for the transient threat-marker provider. The disc
    /// (MapSurface.RebuildOverlay) calls <see cref="Collect"/> each rebuild; a feature registers a
    /// provider via <see cref="Register"/>. Decoupled exactly like CartographyViewer's viewer
    /// registration: a missing provider is a graceful "no blips," never a NullReferenceException.
    /// </summary>
    public static class ThreatMarkerRegistry
    {
        private static IThreatMarkerProvider? _provider;

        /// <summary>
        /// Style hint for minimap consumers (the disc reads this; it must NOT reference Sunstone's
        /// <c>BlipStyle</c> enum — that would reverse the dependency arrow). The provider mirrors the
        /// live <c>BlipStyle</c> config here: true ⇒ draw the trophy sprite + pips, false (default) ⇒
        /// draw a tinted dot. Daniel A/Bs it in-game; the disc + vanilla overlay both honour it.
        /// </summary>
        public static bool PreferTrophyArt;

        /// <summary>Register the threat-marker provider (idempotent-ish; last registration wins, logged).</summary>
        public static void Register(IThreatMarkerProvider provider)
        {
            if (_provider != null && !ReferenceEquals(_provider, provider))
                Plugin.Log.LogInfo("[Trailborne/Cartography] ThreatMarkerRegistry: replacing the registered threat provider.");
            _provider = provider;
        }

        /// <summary>Drop the provider if it is the registered one (e.g. on teardown). Safe to call always.</summary>
        public static void Unregister(IThreatMarkerProvider provider)
        {
            if (ReferenceEquals(_provider, provider)) _provider = null;
        }

        /// <summary>True once a provider has registered (the disc can skip the pull entirely otherwise).</summary>
        public static bool HasProvider => _provider != null;

        /// <summary>
        /// Pull the live threat blips in range into <paramref name="results"/> (cleared first). No-ops
        /// to an empty list when no provider is registered or the provider has nothing to show. Never
        /// throws out — a provider hiccup can't break the disc rebuild.
        /// </summary>
        public static void Collect(Vector3 boundCenter, float boundRadius, List<ThreatBlip> results)
        {
            results.Clear();
            var p = _provider;
            if (p == null) return;
            try { p.CollectThreatBlips(boundCenter, boundRadius, results); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/Cartography] ThreatMarkerRegistry.Collect: provider threw ({e.Message}); no blips this rebuild.");
            }
        }
    }

    /// <summary>
    /// Shared procedural art for threat blips, so the disc (Cartography) and the vanilla-minimap
    /// overlay (Sunstone) draw an identical "dot" without either re-implementing it or reaching across
    /// the dependency arrow. A near-white filled disc on transparent; the consumer's <c>RawImage.color</c>
    /// multiplies the aggro tint onto it (same tint path the ring's trophy uses). Generated once.
    /// </summary>
    public static class ThreatBlipArt
    {
        private static Texture2D? _dot;

        /// <summary>A near-white anti-aliased filled disc (tinted by the consumer's RawImage.color). Cached.</summary>
        public static Texture2D DotTexture()
        {
            if (_dot != null) return _dot;
            const int N = 64;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, mipChain: false)
            {
                name = "SBPR_ThreatDot",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color32[N * N];
            var clear = new Color32(255, 255, 255, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;

            float cx = (N - 1) * 0.5f, cy = (N - 1) * 0.5f;
            float r = N * 0.42f;             // leave a transparent margin so the dot has a soft edge
            for (int y = 0; y < N; y++)
            {
                for (int x = 0; x < N; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // 1.5 px anti-aliased edge so the dot is round, not stair-stepped, at minimap scale.
                    float a = Mathf.Clamp01((r - d) / 1.5f);
                    if (a > 0f) px[y * N + x] = new Color32(245, 245, 245, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(updateMipmaps: false);
            _dot = tex;
            return _dot;
        }
    }
}
