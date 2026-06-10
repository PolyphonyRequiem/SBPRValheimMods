using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// Runtime harvester for VANILLA Valheim UI assets — the wood-panel background
    /// sprite, carved frame/border sprite, button sprite + hover/pressed states, and
    /// the game's display <see cref="Font"/> — so our custom panels can wear the
    /// native look instead of approximating it with flat colours (t_b47035e7).
    ///
    /// This READS live vanilla UI objects (their <c>Image.sprite</c> / <c>Selectable</c>
    /// state / TMP font) and reuses the <em>references</em>. It does NOT clone prefabs,
    /// export, or copy asset files — exactly the same way the content layer reuses
    /// vanilla meshes/materials by reference. Reading/adapting the base game we mod is
    /// clean-side (ADR-0001, clarified 2026-06-09): the clean-room firewall is around
    /// OTHER mods' code, never around vanilla Valheim itself.
    ///
    /// Everything degrades gracefully: if a donor isn't present yet (e.g. harvested too
    /// early, or a future game build moves things), each accessor returns null and the
    /// caller falls back to its old flat-colour primitive. The skin is additive polish,
    /// never load-bearing for function.
    ///
    /// Donor selection (all live, always present once the player is in-world and a sign
    /// is interacted with — the only path that opens our panel):
    ///   • Panel background  — the 9-sliced wood sprite off a real vanilla dialog
    ///     (<c>InventoryGui</c> / <c>StoreGui</c> / <c>TextInput</c> root chrome).
    ///   • Frame / border    — a darker/secondary sliced sprite from the same donors
    ///     (distinct from the background so the rim reads as a carved edge).
    ///   • Button            — a real vanilla <c>Button</c>'s <c>image.sprite</c> +
    ///     <c>spriteState</c> (highlighted/pressed/disabled), so our buttons inherit the
    ///     carved-wood look AND working hover states for free.
    ///   • Font              — the legacy <c>Font</c> underlying vanilla's TMP display
    ///     font (<c>TMP_FontAsset.sourceFontFile</c>), so legacy <c>UnityEngine.UI.Text</c>
    ///     renders in the Norse face the HUD uses. Several fallbacks below.
    /// </summary>
    internal static class VanillaUISkin
    {
        private static bool _harvested;

        private static Sprite? _panelSprite;     // 9-sliced wood background
        private static Sprite? _frameSprite;     // carved frame / darker rim
        private static Sprite? _buttonSprite;    // button base
        private static SpriteState _buttonState; // button hover/press/disabled
        private static bool _haveButtonState;
        private static Font? _font;              // legacy display font

        // ── Public accessors (each forces a one-time harvest, all null-safe) ──

        public static Sprite? PanelSprite { get { EnsureHarvested(); return _panelSprite; } }
        public static Sprite? FrameSprite { get { EnsureHarvested(); return _frameSprite ?? _panelSprite; } }
        public static Sprite? ButtonSprite { get { EnsureHarvested(); return _buttonSprite; } }
        public static Font? Font { get { EnsureHarvested(); return _font; } }

        /// <summary>
        /// True once a vanilla button donor was harvested — i.e. <see cref="SkinButton"/>
        /// will apply the LIGHT carved-wood sprite. Callers use this to pick legible
        /// (dark) label colors for the skinned light chrome vs. the original light colors
        /// for the flat dark fallback fill (t_f2fe06d4). Stable after the one-time harvest,
        /// so every button is skinned identically — querying it is equivalent to capturing
        /// each button's <see cref="SkinButton"/> return value.
        /// </summary>
        public static bool HasButtonSkin { get { EnsureHarvested(); return _buttonSprite != null; } }

        /// <summary>True only if we captured a usable button SpriteState (hover/press).</summary>
        public static bool TryGetButtonState(out SpriteState state)
        {
            EnsureHarvested();
            state = _buttonState;
            return _haveButtonState;
        }

        /// <summary>
        /// Apply the harvested button skin to a freshly-built <see cref="Button"/> +
        /// its target <see cref="Image"/>. No-op (leaves the caller's flat fill) if no
        /// donor was found. Switches the Selectable to SpriteSwap so the captured
        /// hover/pressed sprites actually drive the visual states.
        /// </summary>
        public static bool SkinButton(Button btn, Image img)
        {
            EnsureHarvested();
            if (btn == null || img == null || _buttonSprite == null) return false;

            img.sprite = _buttonSprite;
            img.type = Image.Type.Sliced;
            img.color = Color.white; // let the sprite show its own wood tone
            if (_haveButtonState)
            {
                btn.transition = Selectable.Transition.SpriteSwap;
                btn.spriteState = _buttonState;
            }
            return true;
        }

        /// <summary>
        /// Apply the harvested 9-sliced background sprite to an Image. No-op (caller's
        /// flat fill stays) if no donor sprite was found.
        /// </summary>
        public static bool SkinPanel(Image img, Sprite? sprite)
        {
            if (img == null || sprite == null) return false;
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = Color.white;
            return true;
        }

        // ── Harvest ──────────────────────────────────────────────────────────

        private static void EnsureHarvested()
        {
            if (_harvested) return;
            _harvested = true; // one attempt; never spin every open
            try { Harvest(); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/SignSkin] vanilla UI harvest failed; falling back to flat colours: {e.Message}");
            }
        }

        private static void Harvest()
        {
            HarvestFont();
            HarvestSpritesAndButton();

            Plugin.Log.LogInfo(
                "[Trailborne/SignSkin] harvested vanilla UI: " +
                $"panel={(_panelSprite != null ? _panelSprite.name : "<none>")}, " +
                $"frame={(_frameSprite != null ? _frameSprite.name : "<none>")}, " +
                $"button={(_buttonSprite != null ? _buttonSprite.name : "<none>")}, " +
                $"buttonState={_haveButtonState}, " +
                $"font={(_font != null ? _font.name : "<none>")}");
        }

        // ── Font: vanilla's TMP display font → its underlying legacy Font ──────

        private static void HarvestFont()
        {
            // 1) TMP global default — the face most vanilla TMP text uses.
            try
            {
                var def = TMPro.TMP_Settings.defaultFontAsset;
                if (def != null && def.sourceFontFile != null) { _font = def.sourceFontFile; return; }
            }
            catch { /* TMP not ready — fall through */ }

            // 2) The font off a live vanilla TMP_Text (e.g. the sign dialog topic, or
            //    any HUD label). sourceFontFile is the legacy TrueType backing it.
            try
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>())
                {
                    if (t == null || t.font == null) continue;
                    var src = t.font.sourceFontFile;
                    if (src != null) { _font = src; return; }
                }
            }
            catch { /* ignore */ }

            // 3) Last resort: any legacy Font already loaded (old behaviour), then Arial.
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                if (f != null) { _font = f; return; }
            try { _font = Font.CreateDynamicFontFromOSFont("Arial", 16); } catch { _font = null; }
        }

        // ── Sprites + button: read live vanilla GUI chrome ────────────────────

        private static void HarvestSpritesAndButton()
        {
            // Prefer a real vanilla Button for the button skin (gives base sprite AND
            // the highlight/pressed/disabled SpriteState in one shot). InventoryGui is
            // always present in-world and exposes several plain buttons.
            HarvestButtonFromInventoryGui();

            // Background sprite — STRUCTURAL first: read the actual panel-root Image of a
            // vanilla dialog (the dialog whose background we want to mimic), not a
            // frequency guess. The sign dialog we replace (TextInput.m_panel) is the most
            // contextually-correct donor; InventoryGui's root is the always-present
            // fallback. We take the largest sliced Image on/near that root — that's the
            // panel backing plate by construction.
            HarvestPanelStructural();

            // Frame + (background fallback): tally distinct sliced sprites across the
            // donor dialogs. Used to fill the frame slot (a secondary sliced sprite) and,
            // if the structural read found nothing, to supply the background too.
            var donors = CollectDonorRoots();
            var sliced = CollectSlicedSprites(donors);
            if (sliced.Count > 0)
            {
                var ranked = sliced
                    .OrderByDescending(s => s.Count)
                    .ThenByDescending(s => s.Sprite.rect.width * s.Sprite.rect.height)
                    .Select(s => s.Sprite)
                    .ToList();

                _panelSprite ??= ranked[0];
                _frameSprite = ranked.FirstOrDefault(s => s != _panelSprite);
            }

            // If we somehow got a button sprite but no panel sprite, reuse the button
            // sprite as a panel background (still native wood) rather than flat colour.
            if (_panelSprite == null && _buttonSprite != null)
                _panelSprite = _buttonSprite;
        }

        /// <summary>
        /// Read the panel-root backing sprite from a vanilla dialog structurally: the
        /// largest 9-sliced Image at/under the dialog's panel root is, by construction,
        /// the backing plate. Tries the sign dialog we replace first (most contextually
        /// correct), then InventoryGui / StoreGui roots. Sets <see cref="_panelSprite"/>
        /// only on success; never throws.
        /// </summary>
        private static void HarvestPanelStructural()
        {
            var roots = new List<Transform>();
            void Add(Transform? t) { if (t != null && !roots.Contains(t)) roots.Add(t); }

            // TextInput.m_panel is the exact dialog our panel replaces — the ideal donor.
            try { if (TextInput.instance != null && TextInput.instance.m_panel != null) Add(TextInput.instance.m_panel.transform); } catch { }
            try { if (InventoryGui.instance != null) Add(InventoryGui.instance.transform); } catch { }
            try { if (StoreGui.instance != null) Add(StoreGui.instance.transform); } catch { }

            foreach (var root in roots)
            {
                Sprite? best = null;
                float bestArea = 0f;
                Image[] imgs;
                try { imgs = root.GetComponentsInChildren<Image>(true); }
                catch { continue; }

                foreach (var img in imgs)
                {
                    if (img == null || img.sprite == null) continue;
                    if (img.type != Image.Type.Sliced) continue;
                    if (img.sprite.border == Vector4.zero) continue;
                    // Pixel area of the sprite source rect — the backing plate is the
                    // largest sliced chrome sprite in the dialog.
                    float area = img.sprite.rect.width * img.sprite.rect.height;
                    if (area > bestArea) { bestArea = area; best = img.sprite; }
                }

                if (best != null) { _panelSprite = best; return; }
            }
        }

        private static void HarvestButtonFromInventoryGui()
        {
            try
            {
                var inv = InventoryGui.instance;
                Transform? root = inv != null ? inv.transform : null;
                Button? donor = null;

                if (root != null)
                    donor = root.GetComponentsInChildren<Button>(true)
                                .FirstOrDefault(IsUsableButtonDonor);

                // Fallback: any live vanilla Button with a sliced sprite image.
                if (donor == null)
                    donor = Resources.FindObjectsOfTypeAll<Button>()
                                     .FirstOrDefault(IsUsableButtonDonor);

                if (donor == null) return;

                var img = donor.image != null ? donor.image
                        : donor.targetGraphic as Image;
                if (img != null && img.sprite != null)
                {
                    _buttonSprite = img.sprite;
                    _buttonState = donor.spriteState;
                    // Only trust the SpriteState if it carries at least a highlighted
                    // or pressed sprite (some buttons use ColorTint with no states).
                    _haveButtonState =
                        _buttonState.highlightedSprite != null ||
                        _buttonState.pressedSprite != null ||
                        _buttonState.selectedSprite != null;
                }
            }
            catch { /* ignore — leave button unskinned */ }
        }

        private static bool IsUsableButtonDonor(Button b)
        {
            if (b == null) return false;
            var img = b.image != null ? b.image : b.targetGraphic as Image;
            return img != null && img.sprite != null;
        }

        /// <summary>
        /// Live vanilla GUI roots to read chrome sprites from, in preference order. All
        /// reads are null-safe; absent donors are simply skipped.
        /// </summary>
        private static List<Transform> CollectDonorRoots()
        {
            var roots = new List<Transform>();
            void Add(Transform? t) { if (t != null && !roots.Contains(t)) roots.Add(t); }

            try { if (TextInput.instance != null) Add(TextInput.instance.transform); } catch { }
            try { if (InventoryGui.instance != null) Add(InventoryGui.instance.transform); } catch { }
            try { if (StoreGui.instance != null) Add(StoreGui.instance.transform); } catch { }
            return roots;
        }

        private readonly struct SpriteUse
        {
            public readonly Sprite Sprite;
            public readonly int Count;
            public SpriteUse(Sprite s, int c) { Sprite = s; Count = c; }
        }

        /// <summary>
        /// Walk the donor hierarchies and tally distinct 9-SLICED sprites (sliced =
        /// designed to scale as panel/button chrome; simple/filled icons are skipped).
        /// </summary>
        private static List<SpriteUse> CollectSlicedSprites(List<Transform> roots)
        {
            var counts = new Dictionary<Sprite, int>();
            foreach (var root in roots)
            {
                Image[] imgs;
                try { imgs = root.GetComponentsInChildren<Image>(true); }
                catch { continue; }

                foreach (var img in imgs)
                {
                    if (img == null || img.sprite == null) continue;
                    if (img.type != Image.Type.Sliced) continue; // only 9-slice chrome
                    var border = img.sprite.border;
                    if (border == Vector4.zero) continue; // sliced needs real borders
                    counts.TryGetValue(img.sprite, out var n);
                    counts[img.sprite] = n + 1;
                }
            }
            return counts.Select(kv => new SpriteUse(kv.Key, kv.Value)).ToList();
        }
    }
}
