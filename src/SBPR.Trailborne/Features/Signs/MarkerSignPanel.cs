using System;
using UnityEngine;
using UnityEngine.UI;
using SBPR.Trailborne.Features.MarkerSigns;
// Disambiguate the class from its same-named namespace (the namespace
// SBPR.Trailborne.Features.MarkerSigns shadows the MarkerSigns CLASS when both are in
// scope; this alias lets MarkerSigns.ByKey(...) resolve to the class — same fix
// Trailblazing.cs uses for the identical collision).
using MarkerSignsType = SBPR.Trailborne.Features.MarkerSigns.MarkerSigns;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// The Marker Sign reference panel (v2 Marker Signs, card t_0c7b782d, impl-spec §1.4 /
    /// AT-MARK-1). Opened on PRIMARY E on a placed marker sign; shows the marker's type-coded
    /// ICON, its name, the current pin state, and a Pin/Unpin button (same toggle as the
    /// Shift+E fast gesture, surfaced as a discoverable affordance), plus Close.
    ///
    /// Why a DEDICATED panel and not <see cref="SignPaintPanel"/>: that panel is the Painted
    /// Sign's PIGMENT + text editor — it hard-requires a <see cref="SignTag"/> and its whole
    /// surface (color swatches, pigment discovery, CommitPaint) is meaningless for a marker
    /// (Q1 defers per-pin color; a marker has NO paint colors). Markers carry a
    /// <see cref="MarkerSignTag"/>, not a SignTag, so routing them through SignPaintPanel
    /// would silently no-op. This small panel is the correct, self-contained surface and
    /// touches nothing in the shipping Painted Sign UI. It reuses the SHARED
    /// <see cref="VanillaUISkin"/> so it wears the native wood look for free, and degrades to
    /// flat-color primitives if the skin donor isn't present (same discipline as the paint
    /// panel).
    ///
    /// CLIENT-ONLY: <see cref="Open"/> early-returns without a local Player, so it never
    /// builds on the dedicated server. Input/cursor handling is shared with the paint panel
    /// via <see cref="SignPanelInputBlock"/> (which gates on <see cref="AnyOpen"/>).
    /// </summary>
    public class MarkerSignPanel : MonoBehaviour
    {
        private static MarkerSignPanel? _instance;

        public static bool IsOpen => _instance != null && _instance._root != null && _instance._root.activeSelf;

        // ── Target ───────────────────────────────────────────────────
        private Sign? _sign;
        private MarkerSignTag? _tag;

        // ── UI refs ──────────────────────────────────────────────────
        private GameObject? _root;
        private GameObject? _window;
        private Image? _iconImage;
        private Text? _titleLabel;
        private InputField? _nameInput;
        private Text? _stateLabel;
        private Button? _pinBtn;
        private Text? _pinBtnLabel;
        private Font? _font;

        // The custom pin name length cap (impl-spec §7.5). Enforced on the InputField AND
        // at commit so a programmatic/pasted over-long value can't bypass the field cap.
        private const int PinNameMaxLen = 32;

        // Palette (mirror SignPaintPanel's dark-Norse approximation so the two panels read
        // as one family on the flat-color fallback path).
        private static readonly Color CWindow = new Color(0.12f, 0.11f, 0.09f, 0.97f);
        private static readonly Color CFrame = new Color(0.45f, 0.36f, 0.22f, 1f);
        private static readonly Color CButton = new Color(0.30f, 0.26f, 0.18f, 1f);
        private static readonly Color CLabel = new Color(0.97f, 0.95f, 0.88f, 1f);
        private static readonly Color CLabelOnSkin = new Color(0.12f, 0.09f, 0.05f, 1f);

        // Input-field text colors. Dark typed text on the LIGHT skinned frame; light text on
        // the dark flat-fill fallback (never light-on-light / dark-on-dark — the same
        // discipline as SignPaintPanel.MakeInputField). Placeholder stays a dimmer variant of
        // whichever path is active so it reads as a hint, not as content.
        private static readonly Color CInputTextOnSkin = new Color(0.12f, 0.09f, 0.05f, 1f);
        private static readonly Color CInputTextOnFlat = new Color(0.97f, 0.95f, 0.88f, 1f);
        private static readonly Color CInputPlaceholderOnSkin = new Color(0.35f, 0.30f, 0.22f, 1f);
        private static readonly Color CInputPlaceholderOnFlat = new Color(0.70f, 0.67f, 0.58f, 1f);
        private static readonly Color CInputFill = new Color(0.06f, 0.06f, 0.05f, 1f);

        // ── Public entry point ───────────────────────────────────────

        /// <summary>
        /// Show the marker reference panel for <paramref name="sign"/>. No-op without a local
        /// player (headless server) or a <see cref="MarkerSignTag"/>. Lazily builds the
        /// singleton host the first time.
        /// </summary>
        public static void Open(Sign sign)
        {
            if (sign == null) return;
            var tag = sign.GetComponent<MarkerSignTag>();
            if (tag == null) return;
            if (Player.m_localPlayer == null) return; // client-only UI

            if (_instance == null)
            {
                var host = new GameObject("SBPR_MarkerSignPanelHost");
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<MarkerSignPanel>();
                _instance.BuildRoot();
            }
            _instance.ShowFor(sign, tag);
        }

        public static void CloseIfOpen()
        {
            if (_instance != null && IsOpen) _instance.Hide();
        }

        // ── Lifecycle ────────────────────────────────────────────────

        private void Update()
        {
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1))
            {
                Hide();
                return;
            }
            // If the sign was destroyed out from under us, close gracefully.
            if (_sign == null || _tag == null) { Hide(); return; }
        }

        private void ShowFor(Sign sign, MarkerSignTag tag)
        {
            _sign = sign;
            _tag = tag;
            RebuildWindow();
            if (_root != null) _root.SetActive(true);
            RefreshDynamic();
        }

        private void Hide()
        {
            // Commit any pending name edit BEFORE we drop the target (covers the Close button,
            // Escape, and the destroyed-sign path that routes through here). onEndEdit handles
            // Enter / focus-loss; this is the Close/Escape leg of impl-spec §7.4. Idempotent —
            // no-ops when the name is unchanged, so a double-fire with onEndEdit is harmless.
            CommitPinName();
            if (_root != null) _root.SetActive(false);
            _sign = null;
            _tag = null;
        }

        // ── Root / canvas (built once) ───────────────────────────────

        private void BuildRoot()
        {
            _font = VanillaUISkin.Font;

            _root = new GameObject("SBPR_MarkerPanelRoot");
            _root.transform.SetParent(transform, false);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000; // above the HUD
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            // Dim backdrop (also catches clicks outside the window).
            var dim = MakeImage(_root.transform, "Dim", new Color(0f, 0f, 0f, 0.55f));
            Stretch(dim.GetComponent<RectTransform>());

            _root.SetActive(false);
        }

        // ── Window (rebuilt every open) ──────────────────────────────

        private void RebuildWindow()
        {
            if (_root == null) return;
            if (_window != null) Destroy(_window);
            _iconImage = null; _titleLabel = null; _stateLabel = null;
            _nameInput = null;
            _pinBtn = null; _pinBtnLabel = null;

            _window = MakeImage(_root.transform, "Window", CWindow);
            var wrt = _window.GetComponent<RectTransform>();
            wrt.anchorMin = wrt.anchorMax = new Vector2(0.5f, 0.5f);
            wrt.pivot = new Vector2(0.5f, 0.5f);
            wrt.sizeDelta = new Vector2(440, 100); // height grows to fit
            if (!VanillaUISkin.SkinPanel(_window.GetComponent<Image>(), VanillaUISkin.PanelSprite))
                AddOutline(_window, CFrame);

            var col = _window.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(28, 28, 24, 24);
            col.spacing = 14f;
            col.childAlignment = TextAnchor.UpperCenter;
            col.childControlWidth = true;
            col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;
            var fitter = _window.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var t = _window.transform;

            // Title (the marker's nice name).
            _titleLabel = MakeLabel(t, "Title", "Marker", 24, FontStyle.Bold, TextAnchor.MiddleCenter, 34);

            // The type-coded icon, centered, as a square reference image (AT-MARK-1).
            var iconHost = new GameObject("IconHost");
            iconHost.transform.SetParent(t, false);
            iconHost.AddComponent<RectTransform>();
            AddLayoutElement(iconHost, minHeight: 88);
            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(iconHost.transform, false);
            _iconImage = iconGo.AddComponent<Image>();
            _iconImage.preserveAspect = true;
            var irt = iconGo.GetComponent<RectTransform>();
            irt.anchorMin = new Vector2(0.5f, 0.5f);
            irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.pivot = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(80, 80);

            // Custom pin-name field (impl-spec §7). Placeholder = the type's PinLabel so the
            // player sees what the default map label will be if they leave it blank; seeded
            // from the persisted name on open. Commits on Enter / focus-loss (onEndEdit) and
            // also on Close/Escape via the Hide() chokepoint.
            var def0 = MarkerSignsType.ByKey(_tag != null ? _tag.MarkerType : "");
            string placeholder = def0 != null ? def0.PinLabel : "Marker name";
            MakeLabel(t, "NameCaption", "Pin name", 14, FontStyle.Normal, TextAnchor.MiddleCenter, 18);
            _nameInput = MakeInputField(t, "NameInput", placeholder, 40);
            _nameInput.text = _tag != null ? _tag.ReadPinName() : "";
            _nameInput.onEndEdit.AddListener(_ => CommitPinName());

            // Pin-state line.
            _stateLabel = MakeLabel(t, "State", "", 16, FontStyle.Italic, TextAnchor.MiddleCenter, 24);

            // Pin / Unpin + Close in a horizontal row.
            var btnRow = new GameObject("ButtonRow");
            btnRow.transform.SetParent(t, false);
            btnRow.AddComponent<RectTransform>();
            var brow = btnRow.AddComponent<HorizontalLayoutGroup>();
            brow.spacing = 16f;
            brow.childAlignment = TextAnchor.MiddleCenter;
            brow.childControlWidth = true;
            brow.childControlHeight = true;
            brow.childForceExpandWidth = true;
            brow.childForceExpandHeight = false;
            AddLayoutElement(btnRow, minHeight: 44);
            _pinBtn = MakeButton(btnRow.transform, "PinBtn", "Pin", 44, OnPinClicked, out _pinBtnLabel);
            MakeButton(btnRow.transform, "CloseBtn", "Close", 44, Hide, out _);
        }

        // ── Event handlers ───────────────────────────────────────────

        private void OnPinClicked()
        {
            if (_tag == null) { Hide(); return; }
            try
            {
                // Persist any pending name edit first so a "type a name then click Pin" sequence
                // projects with the new label (don't rely on uGUI's click-deselect ordering to
                // have fired onEndEdit). Idempotent — no-ops when the field is unchanged.
                CommitPinName();

                bool nowPinned = !_tag.ReadPinned();
                if (!_tag.WritePinned(nowPinned))
                {
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                        "Marker not ready to pin yet.");
                    return;
                }
                if (nowPinned) WorldPins.ProjectPinnedNow(_tag);
                else           WorldPins.RemoveProjected(_tag.GetZdoId());

                MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                    nowPinned ? "Pinned on map." : "Unpinned.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Trailborne/MarkerSigns] Panel pin toggle failed: {e}");
            }
            RefreshDynamic();
        }

        /// <summary>
        /// Commit the custom pin name from the field (impl-spec §7.4/§7.5). Trims, caps to 32,
        /// no-ops when unchanged, then owner-writes SBPR_PinName. If the marker is currently
        /// pinned, re-projects NOW (RemoveProjected + ProjectPinnedNow) so the on-map label
        /// refreshes live for the editing client without a relog (AT-MARKER-NAME-4) — the
        /// reconcile pass never relabels an already-projected pin, so this explicit re-project
        /// is required, not optional. If NOT pinned, the name is just persisted and used when
        /// the player next pins it. Called from onEndEdit (Enter/focus-loss) and Hide()
        /// (Close/Escape); idempotent so the overlapping fire is harmless.
        /// </summary>
        private void CommitPinName()
        {
            if (_tag == null || _nameInput == null) return;
            try
            {
                string name = (_nameInput.text ?? "").Trim();
                if (name.Length > PinNameMaxLen) name = name.Substring(0, PinNameMaxLen);

                // Reflect the trimmed/capped value back into the field so what the player sees
                // matches what's stored (a programmatic .text set fires onValueChanged, NOT
                // onEndEdit, so this can't recurse into CommitPinName).
                if (_nameInput.text != name) _nameInput.text = name;

                if (name == _tag.ReadPinName()) return; // unchanged → no write, no re-project

                if (!_tag.WritePinName(name)) return; // ghost / ZDO not ready — nothing persisted

                // Dynamic relabel for the editing client: drop the stale-label pin and re-add
                // with the new label. Both calls are CLIENT-ONLY no-ops without a Minimap, and
                // only matter when this marker is actually pinned.
                if (_tag.ReadPinned())
                {
                    WorldPins.RemoveProjected(_tag.GetZdoId());
                    WorldPins.ProjectPinnedNow(_tag);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[Trailborne/MarkerSigns] Panel pin-name commit failed: {e}");
            }
        }

        // ── Dynamic refresh ──────────────────────────────────────────

        private void RefreshDynamic()
        {
            if (_tag == null) return;
            var def = MarkerSignsType.ByKey(_tag.MarkerType);

            if (_titleLabel != null)
                _titleLabel.text = def != null ? def.NiceName : "Marker";

            if (_iconImage != null)
            {
                var sprite = _tag.MarkerIcon
                             ?? (def != null ? Runtime.Assets.LoadPngAsSprite(def.IconFile) : null);
                _iconImage.sprite = sprite;
                _iconImage.enabled = sprite != null;
            }

            bool pinned = _tag.ReadPinned();
            if (_stateLabel != null)
                _stateLabel.text = pinned ? "Pinned on your map" : "Not pinned";
            if (_pinBtnLabel != null)
                _pinBtnLabel.text = pinned ? "Unpin" : "Pin";
        }

        // ── UI primitive helpers (small, local; the paint panel's are private to it) ──

        private static GameObject MakeImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        /// <summary>
        /// Single-line, 32-char-capped text field for the custom pin name (impl-spec §7.5).
        /// Adapts the proven SignPaintPanel.MakeInputField recipe (skinned frame + flat-fill
        /// fallback, light-on-skin / light-on-flat text discipline) into this panel's own
        /// local helper — deliberately NOT cross-referencing the paint panel's private method
        /// (impl-spec §7.1 / the panel's :265 "keep our own small UI primitives" note).
        /// </summary>
        private InputField MakeInputField(Transform parent, string name, string placeholder, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var bg = go.AddComponent<Image>();
            bg.color = CInputFill;
            bool skinned = VanillaUISkin.SkinPanel(bg, VanillaUISkin.FrameSprite);
            if (!skinned)
                AddOutline(go, CFrame);
            AddLayoutElement(go, minHeight: height);

            var input = go.AddComponent<InputField>();

            // Distinct light carved-inset frame ⇒ dark text; degraded dark backing (FrameSprite
            // fell back to the dark PanelSprite) or flat fill ⇒ light text. Identity check, not
            // luminance (the atlas textures aren't CPU-readable) — same call SignPaintPanel makes.
            bool lightFrame = skinned && VanillaUISkin.FrameSprite != VanillaUISkin.PanelSprite;

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10, 6); trt.offsetMax = new Vector2(-10, -6);
            var txt = textGo.AddComponent<Text>();
            txt.font = _font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.fontSize = 18;
            txt.color = lightFrame ? CInputTextOnSkin : CInputTextOnFlat;
            txt.supportRichText = false;
            txt.alignment = TextAnchor.MiddleLeft;

            var phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(go.transform, false);
            var prt = phGo.AddComponent<RectTransform>();
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(10, 6); prt.offsetMax = new Vector2(-10, -6);
            var ph = phGo.AddComponent<Text>();
            ph.font = _font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            ph.fontSize = 18;
            ph.fontStyle = FontStyle.Italic;
            ph.color = lightFrame ? CInputPlaceholderOnSkin : CInputPlaceholderOnFlat;
            ph.text = placeholder;
            ph.alignment = TextAnchor.MiddleLeft;
            ph.supportRichText = false;

            input.textComponent = txt;
            input.placeholder = ph;
            input.lineType = InputField.LineType.SingleLine; // a pin label is one line; Enter commits
            input.characterLimit = PinNameMaxLen;
            return input;
        }

        private Text MakeLabel(Transform parent, string name, string text, int size, FontStyle style,
            TextAnchor anchor, float minHeight)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = _font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = anchor;
            t.color = CLabel;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            AddLayoutElement(go, minHeight: minHeight);
            return t;
        }

        private Button MakeButton(Transform parent, string name, string label, float height,
            Action onClick, out Text labelText)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = CButton;
            var btn = go.AddComponent<Button>();
            bool skinned = VanillaUISkin.SkinButton(btn, img);
            if (!skinned) img.color = CButton;

            var txtGo = new GameObject("Label");
            txtGo.transform.SetParent(go.transform, false);
            labelText = txtGo.AddComponent<Text>();
            labelText.text = label;
            labelText.font = _font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            labelText.fontSize = 18;
            labelText.alignment = TextAnchor.MiddleCenter;
            labelText.color = skinned ? CLabelOnSkin : CLabel;
            Stretch(txtGo.GetComponent<RectTransform>());

            if (onClick != null) btn.onClick.AddListener(() => onClick());
            AddLayoutElement(go, minHeight: height);
            return btn;
        }

        private static void AddLayoutElement(GameObject go, float minHeight = 0f, float preferredWidth = 0f)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (minHeight > 0f) le.minHeight = minHeight;
            if (preferredWidth > 0f) le.preferredWidth = preferredWidth;
        }

        private static void AddOutline(GameObject go, Color color)
        {
            var outline = go.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(2f, -2f);
        }

        private static void Stretch(RectTransform? rt)
        {
            if (rt == null) return;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
