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
        private Text? _stateLabel;
        private Button? _pinBtn;
        private Text? _pinBtnLabel;
        private Font? _font;

        // Palette (mirror SignPaintPanel's dark-Norse approximation so the two panels read
        // as one family on the flat-color fallback path).
        private static readonly Color CWindow = new Color(0.12f, 0.11f, 0.09f, 0.97f);
        private static readonly Color CFrame = new Color(0.45f, 0.36f, 0.22f, 1f);
        private static readonly Color CButton = new Color(0.30f, 0.26f, 0.18f, 1f);
        private static readonly Color CLabel = new Color(0.97f, 0.95f, 0.88f, 1f);
        private static readonly Color CLabelOnSkin = new Color(0.12f, 0.09f, 0.05f, 1f);

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
