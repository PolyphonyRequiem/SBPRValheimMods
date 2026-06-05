using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// The combined Painted Sign Paint+Text panel (§A2.6, re-lock 2026-06-05). A
    /// custom, clean-room runtime uGUI panel built entirely from public UnityEngine.UI
    /// primitives (Canvas / Image / Button / InputField / Text) — NO copied vanilla UI
    /// prefab. Opened by interacting with a placed sign (replaces the vanilla text
    /// dialog). Layout mirrors Daniel's mockup exactly:
    ///
    ///   --- PAINTING ---
    ///    Set Text Color:  [Red][Blue][Black][White][·][·][·]   (extra slots disabled)
    ///    Border Color:    [Red][Blue][Black][White][·][·][·]   (extra slots disabled)
    ///    Cost:            (icons) 1 Red Pigment   1 White Pigment
    ///    { Paint this and consume }
    ///   --- TEXT ---
    ///    [ text field ]   (enabled only once a paint color is chosen)
    ///    { Update Text }
    ///
    /// State is local until committed: choosing swatches updates the cost line + button
    /// enablement; { Paint this and consume } drives <see cref="SignPaintBackend"/>;
    /// { Update Text } writes the label free (no pigment). The panel is a singleton —
    /// one instance, shown/hidden per interaction, rebuilt against the target sign.
    ///
    /// Client-only: never constructed on the headless server (no local Player → the
    /// open path early-returns). While open, <see cref="SignPanelInputBlock"/> blocks
    /// player input + releases the mouse cursor so the panel is usable.
    /// </summary>
    public class SignPaintPanel : MonoBehaviour
    {
        // ── Singleton ────────────────────────────────────────────────
        private static SignPaintPanel? _instance;
        public static SignPaintPanel? Instance => _instance;
        public static bool IsOpen => _instance != null && _instance._root != null && _instance._root.activeSelf;

        // Active-color swatch set (the four live pigments) + the reserved disabled slots.
        private static readonly string[] ActiveColors = { "red", "blue", "black", "white" };
        private const int DisabledSlots = 3; // reserved future-pigment placeholders

        // ── Target + selection state ─────────────────────────────────
        private Sign? _sign;
        private SignTag? _tag;
        private string _selText = "";   // chosen board/text color ("" = none)
        private string _selBorder = ""; // chosen border color ("" = none)

        // ── UI refs (built once) ─────────────────────────────────────
        private GameObject? _root;
        private Canvas? _canvas;
        private Text? _costText;
        private GameObject? _costIconRow;
        private Button? _paintBtn;
        private Text? _paintBtnLabel;
        private InputField? _textField;
        private Button? _updateTextBtn;
        private Text? _updateTextBtnLabel;
        private readonly List<Button> _textSwatches = new List<Button>();
        private readonly List<Button> _borderSwatches = new List<Button>();
        private Font? _font;

        // ── Public entry point ───────────────────────────────────────

        /// <summary>
        /// Show the panel for <paramref name="sign"/>. Lazily builds the singleton the
        /// first time. No-op without a local player (headless server) or a SignTag.
        /// </summary>
        public static void Open(Sign sign)
        {
            if (sign == null) return;
            var tag = sign.GetComponent<SignTag>();
            if (tag == null) return;
            if (Player.m_localPlayer == null) return; // client-only UI

            if (_instance == null)
            {
                var host = new GameObject("SBPR_SignPaintPanelHost");
                DontDestroyOnLoad(host);
                _instance = host.AddComponent<SignPaintPanel>();
                _instance.Build();
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
            // Escape closes the panel (mirrors vanilla dialog dismissal).
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Hide();
                return;
            }
            // If the sign was destroyed out from under us, close gracefully.
            if (_sign == null || _tag == null) { Hide(); return; }
        }

        private void ShowFor(Sign sign, SignTag tag)
        {
            _sign = sign;
            _tag = tag;
            // Seed selection from the sign's current ZDO state so re-opening shows what
            // it already is; an empty slot stays empty (unpainted).
            _selText = tag.ReadTextColor() ?? "";
            _selBorder = tag.ReadBorderColor() ?? "";

            if (_textField != null)
                _textField.text = sign.GetText() ?? "";

            if (_root != null) _root.SetActive(true);
            RefreshSwatchHighlights();
            RefreshDynamic();
        }

        private void Hide()
        {
            if (_root != null) _root.SetActive(false);
            _sign = null;
            _tag = null;
        }

        // ── UI construction (clean-room, from primitives) ────────────

        private void Build()
        {
            _font = ResolveFont();

            // Fullscreen overlay canvas. Reuses the scene's existing EventSystem for
            // click routing; we only add our own GraphicRaycaster.
            _root = new GameObject("SBPR_SignPanelRoot");
            _root.transform.SetParent(transform, false);
            _canvas = _root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5000; // above the HUD
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            // Dim backdrop (also catches clicks outside the window).
            var dim = MakePanel(_root.transform, "Dim", new Color(0f, 0f, 0f, 0.55f));
            Stretch(dim.GetComponent<RectTransform>());

            // Centered window.
            var window = MakePanel(_root.transform, "Window", new Color(0.12f, 0.11f, 0.09f, 0.96f));
            var wrt = window.GetComponent<RectTransform>();
            wrt.anchorMin = wrt.anchorMax = new Vector2(0.5f, 0.5f);
            wrt.pivot = new Vector2(0.5f, 0.5f);
            wrt.sizeDelta = new Vector2(640, 560);
            AddOutline(window, new Color(0.45f, 0.36f, 0.22f, 1f));

            // Vertical layout inside the window via a content column.
            float y = -24f; // running cursor from the window top, in local px

            MakeLabel(window.transform, "Title", "Painted Sign", 26, FontStyle.Bold,
                new Vector2(0, y), new Vector2(600, 34), TextAnchor.UpperCenter);
            y -= 44f;

            MakeLabel(window.transform, "PaintHeader", "— PAINTING —", 18, FontStyle.Bold,
                new Vector2(0, y), new Vector2(600, 26), TextAnchor.UpperCenter);
            y -= 36f;

            // Text-color swatch row
            MakeLabel(window.transform, "TextColorLabel", "Set Text Color:", 16, FontStyle.Normal,
                new Vector2(-300, y), new Vector2(220, 28), TextAnchor.UpperLeft);
            BuildSwatchRow(window.transform, new Vector2(-70, y), isBorder: false);
            y -= 44f;

            // Border-color swatch row
            MakeLabel(window.transform, "BorderColorLabel", "Border Color:", 16, FontStyle.Normal,
                new Vector2(-300, y), new Vector2(220, 28), TextAnchor.UpperLeft);
            BuildSwatchRow(window.transform, new Vector2(-70, y), isBorder: true);
            y -= 50f;

            // Cost line: label + icon row
            MakeLabel(window.transform, "CostLabel", "Cost:", 16, FontStyle.Normal,
                new Vector2(-300, y), new Vector2(120, 28), TextAnchor.UpperLeft);
            _costIconRow = new GameObject("CostIconRow");
            _costIconRow.transform.SetParent(window.transform, false);
            var cir = _costIconRow.AddComponent<RectTransform>();
            cir.anchorMin = cir.anchorMax = new Vector2(0.5f, 1f);
            cir.pivot = new Vector2(0f, 1f);
            cir.anchoredPosition = new Vector2(-210, y);
            cir.sizeDelta = new Vector2(500, 30);
            _costText = MakeLabel(window.transform, "CostText", "", 15, FontStyle.Normal,
                new Vector2(-210, y - 30), new Vector2(520, 26), TextAnchor.UpperLeft);
            y -= 64f;

            // Paint button
            _paintBtn = MakeButton(window.transform, "PaintBtn", "Paint this and consume",
                new Vector2(0, y), new Vector2(360, 40), OnPaintClicked, out _paintBtnLabel);
            y -= 56f;

            MakeLabel(window.transform, "TextHeader", "— TEXT —", 18, FontStyle.Bold,
                new Vector2(0, y), new Vector2(600, 26), TextAnchor.UpperCenter);
            y -= 36f;

            // Text input field
            _textField = MakeInputField(window.transform, "TextField",
                new Vector2(0, y), new Vector2(520, 38));
            y -= 50f;

            // Update Text button
            _updateTextBtn = MakeButton(window.transform, "UpdateTextBtn", "Update Text",
                new Vector2(-130, y), new Vector2(220, 40), OnUpdateTextClicked, out _updateTextBtnLabel);

            // Close button
            MakeButton(window.transform, "CloseBtn", "Close",
                new Vector2(130, y), new Vector2(160, 40), Hide, out _);

            _root.SetActive(false);
        }

        // ── Swatch row ───────────────────────────────────────────────

        private void BuildSwatchRow(Transform parent, Vector2 startPos, bool isBorder)
        {
            var list = isBorder ? _borderSwatches : _textSwatches;
            float x = startPos.x;
            const float cell = 44f, gap = 6f;

            foreach (var color in ActiveColors)
            {
                var capture = color;
                var btn = MakeSwatch(parent, $"{(isBorder ? "B" : "T")}_{color}",
                    Signs.ColorValues.TryGetValue(color, out var c) ? c : Color.gray,
                    new Vector2(x, startPos.y), new Vector2(cell, cell),
                    () => OnSwatchClicked(capture, isBorder), enabled: true);
                list.Add(btn);
                x += cell + gap;
            }
            // Disabled reserved slots (rendered, non-interactable).
            for (int i = 0; i < DisabledSlots; i++)
            {
                MakeSwatch(parent, $"{(isBorder ? "B" : "T")}_disabled{i}",
                    new Color(0.25f, 0.25f, 0.25f, 0.6f),
                    new Vector2(x, startPos.y), new Vector2(cell, cell), null, enabled: false);
                x += cell + gap;
            }

            // A small "none" toggle is implicit: clicking the already-selected swatch
            // clears that slot (handled in OnSwatchClicked), so border stays optional.
        }

        // ── Event handlers ───────────────────────────────────────────

        private void OnSwatchClicked(string color, bool isBorder)
        {
            if (isBorder)
                _selBorder = (_selBorder == color) ? "" : color; // toggle off = optional border
            else
                _selText = (_selText == color) ? "" : color;

            RefreshSwatchHighlights();
            RefreshDynamic();
        }

        private void OnPaintClicked()
        {
            if (_tag == null) { Hide(); return; }
            var player = Player.m_localPlayer;
            var result = SignPaintBackend.CommitPaint(_tag, player, _selText, _selBorder);
            switch (result)
            {
                case SignPaintBackend.PaintResult.Success:
                    var cost = SignPaintBackend.ComputeCost(_selText, _selBorder);
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                        $"Painted sign ({DescribeCost(cost)}).");
                    break;
                case SignPaintBackend.PaintResult.NoColorChosen:
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                        "Choose at least one color.");
                    break;
                case SignPaintBackend.PaintResult.InsufficientItems:
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                        "Not enough pigment.");
                    break;
                default:
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                        "Could not paint the sign.");
                    break;
            }
            RefreshDynamic();
        }

        private void OnUpdateTextClicked()
        {
            if (_sign == null) return;
            // Text edit is locked until ≥1 paint color is chosen (§A2.6).
            if (!AnyColorChosen()) return;
            string text = _textField != null ? _textField.text : "";
            // Free commit (no pigment). Use the vanilla setter so it persists + syncs
            // exactly like a vanilla sign label (owner-write under the hood).
            _sign.SetText(text);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Sign text updated.");
        }

        // ── Dynamic refresh ──────────────────────────────────────────

        private bool AnyColorChosen() => !string.IsNullOrEmpty(_selText) || !string.IsNullOrEmpty(_selBorder);

        private void RefreshDynamic()
        {
            var player = Player.m_localPlayer;
            var cost = SignPaintBackend.ComputeCost(_selText, _selBorder);

            // Cost text + icons
            BuildCostIcons(cost);
            if (_costText != null) _costText.text = cost.Count == 0 ? "(choose a color)" : DescribeCost(cost);

            // Paint button: enabled only with ≥1 color AND enough pigment held.
            bool canPaint = cost.Count > 0 && player != null && SignPaintBackend.HasPigments(player, cost);
            SetButtonEnabled(_paintBtn, _paintBtnLabel, canPaint);

            // Text field + Update Text locked until a color is chosen.
            bool textUnlocked = AnyColorChosen();
            if (_textField != null) _textField.interactable = textUnlocked;
            SetButtonEnabled(_updateTextBtn, _updateTextBtnLabel, textUnlocked);
        }

        private void RefreshSwatchHighlights()
        {
            void Mark(List<Button> row, string sel, bool isBorder)
            {
                for (int i = 0; i < ActiveColors.Length && i < row.Count; i++)
                {
                    var outline = row[i].GetComponent<Outline>();
                    bool selected = ActiveColors[i] == sel;
                    if (outline != null)
                    {
                        outline.effectColor = selected ? new Color(1f, 0.95f, 0.5f, 1f) : new Color(0f, 0f, 0f, 0.8f);
                        outline.effectDistance = selected ? new Vector2(3, 3) : new Vector2(1.5f, 1.5f);
                    }
                }
            }
            Mark(_textSwatches, _selText, false);
            Mark(_borderSwatches, _selBorder, true);
        }

        private void BuildCostIcons(Dictionary<string, int> cost)
        {
            if (_costIconRow == null) return;
            foreach (Transform c in _costIconRow.transform) Destroy(c.gameObject);

            float x = 0f;
            foreach (var kv in cost)
            {
                var sprite = PigmentSprite(kv.Key);
                var iconGo = new GameObject($"icon_{kv.Key}");
                iconGo.transform.SetParent(_costIconRow.transform, false);
                var rt = iconGo.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(x, 0);
                rt.sizeDelta = new Vector2(28, 28);
                var img = iconGo.AddComponent<Image>();
                if (sprite != null) { img.sprite = sprite; img.color = Color.white; }
                else if (Signs.ColorValues.TryGetValue(kv.Key, out var col)) img.color = col;
                x += 34f;

                var lbl = MakeLabel(_costIconRow.transform, $"icon_lbl_{kv.Key}",
                    $"x{kv.Value}", 14, FontStyle.Bold, Vector2.zero, new Vector2(40, 28), TextAnchor.MiddleLeft);
                var lrt = lbl.GetComponent<RectTransform>();
                lrt.anchorMin = lrt.anchorMax = new Vector2(0f, 0.5f);
                lrt.pivot = new Vector2(0f, 0.5f);
                lrt.anchoredPosition = new Vector2(x, 0);
                x += 56f;
            }
        }

        private static string DescribeCost(Dictionary<string, int> cost)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (var kv in cost)
            {
                if (!first) sb.Append(" + ");
                sb.Append(kv.Value).Append(' ').Append(Signs.PigmentLabel(kv.Key));
                first = false;
            }
            return sb.ToString();
        }

        private static Sprite? PigmentSprite(string color)
        {
            string? inkName = Signs.InkForColor(color);
            if (inkName == null) return null;
            var odb = ObjectDB.instance;
            var go = odb != null ? odb.GetItemPrefab(inkName) : null;
            var drop = go != null ? go.GetComponent<ItemDrop>() : null;
            var icons = drop?.m_itemData?.m_shared?.m_icons;
            return (icons != null && icons.Length > 0) ? icons[0] : null;
        }

        // ── Primitive builders ───────────────────────────────────────

        private Font? ResolveFont()
        {
            // Prefer a font already loaded by the game; fall back to an OS dynamic font.
            foreach (var f in Resources.FindObjectsOfTypeAll<Font>())
                if (f != null) return f;
            try { return Font.CreateDynamicFontFromOSFont("Arial", 16); }
            catch { return null; }
        }

        private static GameObject MakePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = color;
            return go;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void AddOutline(GameObject go, Color c)
        {
            var o = go.AddComponent<Outline>();
            o.effectColor = c;
            o.effectDistance = new Vector2(2, 2);
        }

        private Text MakeLabel(Transform parent, string name, string text, int size, FontStyle style,
            Vector2 anchoredPos, Vector2 sizeDelta, TextAnchor align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = align;
            t.color = new Color(0.93f, 0.90f, 0.82f, 1f);
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private Button MakeButton(Transform parent, string name, string label,
            Vector2 anchoredPos, Vector2 sizeDelta, Action? onClick, out Text labelText)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.30f, 0.26f, 0.18f, 1f);
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            AddOutline(go, new Color(0f, 0f, 0f, 0.7f));
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            labelText = MakeLabel(go.transform, "Label", label, 16, FontStyle.Bold,
                Vector2.zero, sizeDelta, TextAnchor.MiddleCenter);
            var lrt = labelText.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.anchoredPosition = Vector2.zero;
            return btn;
        }

        private Button MakeSwatch(Transform parent, string name, Color color,
            Vector2 anchoredPos, Vector2 sizeDelta, Action? onClick, bool enabled)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.interactable = enabled;
            AddOutline(go, new Color(0f, 0f, 0f, 0.8f));
            if (enabled && onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        private InputField MakeInputField(Transform parent, string name, Vector2 anchoredPos, Vector2 sizeDelta)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.06f, 0.05f, 1f);
            AddOutline(go, new Color(0.45f, 0.36f, 0.22f, 1f));

            var input = go.AddComponent<InputField>();

            // Text component (the visible typed text).
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10, 6); trt.offsetMax = new Vector2(-10, -6);
            var txt = textGo.AddComponent<Text>();
            txt.font = _font;
            txt.fontSize = 18;
            txt.color = new Color(0.95f, 0.93f, 0.86f, 1f);
            txt.supportRichText = false;
            txt.alignment = TextAnchor.MiddleLeft;

            // Placeholder.
            var phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(go.transform, false);
            var prt = phGo.AddComponent<RectTransform>();
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(10, 6); prt.offsetMax = new Vector2(-10, -6);
            var ph = phGo.AddComponent<Text>();
            ph.font = _font;
            ph.fontSize = 18;
            ph.fontStyle = FontStyle.Italic;
            ph.color = new Color(0.6f, 0.58f, 0.52f, 0.8f);
            ph.text = "Sign text…";
            ph.alignment = TextAnchor.MiddleLeft;

            input.textComponent = txt;
            input.placeholder = ph;
            input.lineType = InputField.LineType.MultiLineNewline;
            input.characterLimit = 200;
            return input;
        }

        private static void SetButtonEnabled(Button? btn, Text? label, bool enabled)
        {
            if (btn != null) btn.interactable = enabled;
            if (label != null)
                label.color = enabled ? new Color(0.97f, 0.95f, 0.88f, 1f) : new Color(0.55f, 0.53f, 0.48f, 1f);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
