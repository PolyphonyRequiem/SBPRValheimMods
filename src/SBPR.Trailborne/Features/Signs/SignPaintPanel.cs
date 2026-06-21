using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SBPR.Trailborne.Features.Signs
{
    /// <summary>
    /// The combined Painted Sign Paint+Text panel (§A2.6, rebuilt on native-idiom uGUI
    /// 2026-06-07 per Daniel's playtest). A clean-room runtime panel built from public
    /// UnityEngine.UI primitives (Canvas / Image / Button / InputField / Text) laid out
    /// with proper Unity LAYOUT GROUPS — NOT hand-computed y-offsets — so alignment and
    /// margins are consistent (Issue 7). No copied vanilla UI prefab.
    ///
    /// CHROME: the panel wears the ACTUAL vanilla UI assets — the 9-sliced wood-panel
    /// background + carved frame sprite, the carved-wood button sprite with vanilla
    /// hover/pressed states, and the game's Norse display font — all harvested at
    /// runtime by <see cref="VanillaUISkin"/> from live vanilla GUI donors (the sign
    /// dialog we replace, InventoryGui, StoreGui). Reading/reusing vanilla UI sprite +
    /// font REFERENCES is clean-side (ADR-0001 clarification 2026-06-09: the firewall is
    /// around other mods, never vanilla). If a donor isn't present the panel degrades to
    /// its previous flat-colour approximation — the skin is additive, never load-bearing.
    /// Colour swatch tiles intentionally keep a flat pigment fill (the colour is their
    /// content). Opened by interacting with a placed sign (replaces the vanilla dialog).
    ///
    ///   Painted Sign
    ///   — PAINTING —
    ///    Set Text Color:  [None][Red][Blue][Black][White]   (only DISCOVERED pigments)
    ///    Border Color:    [None][Red][Blue][Black][White]   (None = explicit clear)
    ///    Cost:  (crafting-style rows: icon + name + have/need, red when short)
    ///    { Paint this and consume }
    ///   — TEXT —
    ///    [ text field ]   (enabled only once a paint color is chosen)
    ///    { Update Text }   { Close }
    ///
    /// Fixes vs the old hand-built panel:
    ///   • Issue 4 — only DISCOVERED pigments render (no dead/unclickable reserved
    ///     boxes); an explicit "None" tile clears each row (text AND border).
    ///   • Issue 5 — cost reads like the vanilla crafting requirement list (icon +
    ///     name + have/need count, count pulses red when short).
    ///   • Issue 7 — layout groups replace the old y-=offset arithmetic.
    /// Text-color application to the sign letters (Issue 4b) lives in SignTag/Signs.
    ///
    /// Client-only: never constructed on the headless server (no local Player → the
    /// open path early-returns). While open, <see cref="SignPanelInputBlock"/> blocks
    /// player + camera input and releases the mouse cursor so the panel is usable.
    /// </summary>
    public class SignPaintPanel : MonoBehaviour
    {
        // ── Singleton ────────────────────────────────────────────────
        private static SignPaintPanel? _instance;
        public static SignPaintPanel? Instance => _instance;
        public static bool IsOpen => _instance != null && _instance._root != null && _instance._root.activeSelf;

        // The four live pigment colors, in the mockup's row order (None is prepended
        // as an explicit affordance, reserved future-pigment slots are simply not drawn).
        private static readonly string[] AllColors = { "red", "blue", "black", "white" };

        // ── Target + selection state ─────────────────────────────────
        private Sign? _sign;
        private SignTag? _tag;
        // Which of the three independent color slots a swatch row / click targets
        // (Daniel 2026-06-21 three-slot model). Text → letters, Board → plank, Border → frame.
        private enum Slot { Text, Board, Border }

        private string _selText = "";   // chosen letter color ("" = none)
        private string _selBoard = "";  // chosen board-plank color ("" = none)
        private string _selBorder = ""; // chosen border-frame color ("" = none)

        // ── UI refs (window rebuilt per-open so swatches reflect discovery) ──
        private GameObject? _root;
        private Canvas? _canvas;
        private GameObject? _window;          // the centered column; rebuilt each ShowFor
        private GameObject? _costList;        // crafting-style requirement rows live here
        private Button? _paintBtn;
        private Text? _paintBtnLabel;
        private InputField? _textField;
        private Button? _updateTextBtn;
        private Text? _updateTextBtnLabel;
        private readonly List<(string color, Button btn)> _textSwatches = new List<(string, Button)>();
        private readonly List<(string color, Button btn)> _boardSwatches = new List<(string, Button)>();
        private readonly List<(string color, Button btn)> _borderSwatches = new List<(string, Button)>();
        private Font? _font;

        // Palette (our own dark-Norse approximation — clean-room, no vanilla sprite copy).
        private static readonly Color CParchment = new Color(0.93f, 0.90f, 0.82f, 1f);
        private static readonly Color CParchmentDim = new Color(0.55f, 0.53f, 0.48f, 1f);
        private static readonly Color CWindow = new Color(0.12f, 0.11f, 0.09f, 0.97f);
        private static readonly Color CFrame = new Color(0.45f, 0.36f, 0.22f, 1f);
        private static readonly Color CButton = new Color(0.30f, 0.26f, 0.18f, 1f);
        private static readonly Color CSwatchNone = new Color(0.18f, 0.17f, 0.15f, 1f);
        private static readonly Color CSelected = new Color(1f, 0.95f, 0.5f, 1f);
        private static readonly Color CUnselected = new Color(0f, 0f, 0f, 0.8f);

        // ── Interactive-text colors that track the chrome under them ─────────
        // The harvested vanilla button + input-frame sprites are LIGHT carved wood,
        // so text drawn on the SKINNED path must be DARK to read; the flat fallback
        // fills are DARK, so text on them keeps the original LIGHT colors. Never
        // light-on-light or dark-on-dark (t_f2fe06d4 — Daniel's screenshot showed the
        // cream labels + near-white field text vanishing on the light skinned chrome).
        // Only the LOWER interactive panel (buttons + text field) uses these; the
        // upper panel labels sit on the dark window backing and are left untouched.
        private static readonly Color CBtnLabelOnSkin = new Color(0.12f, 0.09f, 0.05f, 1f);          // dark on light wood
        private static readonly Color CBtnLabelOnFlat = new Color(0.97f, 0.95f, 0.88f, 1f);          // orig cream on dark fill
        private static readonly Color CBtnLabelDisabledOnSkin = new Color(0.36f, 0.30f, 0.22f, 1f);  // dim-dark, still reads on light wood
        private static readonly Color CBtnLabelDisabledOnFlat = CParchmentDim;                        // orig dim cream on dark fill
        private static readonly Color CInputTextOnSkin = new Color(0.10f, 0.08f, 0.05f, 1f);          // dark typed text on light frame
        private static readonly Color CInputTextOnFlat = new Color(0.95f, 0.93f, 0.86f, 1f);          // orig light typed text on dark fill
        private static readonly Color CInputPlaceholderOnSkin = new Color(0.34f, 0.30f, 0.24f, 0.9f); // dimmer than typed, reads on light
        private static readonly Color CInputPlaceholderOnFlat = new Color(0.6f, 0.58f, 0.52f, 0.8f);  // orig dim placeholder on dark fill

        // ── Public entry point ───────────────────────────────────────

        /// <summary>
        /// Show the panel for <paramref name="sign"/>. Lazily builds the singleton host
        /// the first time; the window itself is rebuilt every open so the swatch rows
        /// reflect the current discovery state. No-op without a local player (headless
        /// server) or a SignTag.
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
            // Escape / controller-cancel closes the panel (mirrors vanilla dialog dismiss).
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.JoystickButton1))
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
            _selBoard = tag.ReadBoardColor() ?? "";
            _selBorder = tag.ReadBorderColor() ?? "";

            // Rebuild the window so swatch rows reflect the current discovery state.
            RebuildWindow();

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

        // ── Root / canvas (built once) ───────────────────────────────

        private void BuildRoot()
        {
            _font = ResolveFont();

            // Guarantee an EventSystem so the panel is clickable (vanilla normally
            // provides one; create a minimal owned fallback only if absent).
            EnsureEventSystem();

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
            var dim = MakeImage(_root.transform, "Dim", new Color(0f, 0f, 0f, 0.55f));
            Stretch(dim.GetComponent<RectTransform>());

            _root.SetActive(false);
        }

        // ── Window (rebuilt every open via layout groups) ────────────

        private void RebuildWindow()
        {
            if (_root == null) return;

            // Drop the previous window so a re-open reflects fresh discovery state.
            if (_window != null) Destroy(_window);
            _textSwatches.Clear();
            _boardSwatches.Clear();
            _borderSwatches.Clear();
            _costList = null;
            _paintBtn = null; _paintBtnLabel = null;
            _updateTextBtn = null; _updateTextBtnLabel = null;
            _textField = null;

            // Centered window with a vertical layout column. Width fixed; height fits
            // content via ContentSizeFitter so margins stay even regardless of how many
            // pigments are discovered (Issue 7).
            _window = MakeImage(_root.transform, "Window", CWindow);
            var wrt = _window.GetComponent<RectTransform>();
            wrt.anchorMin = wrt.anchorMax = new Vector2(0.5f, 0.5f);
            wrt.pivot = new Vector2(0.5f, 0.5f);
            wrt.sizeDelta = new Vector2(660, 100); // height grows to fit
            // Skin the window with the vanilla wood-panel sprite (9-sliced, carved frame
            // baked in — vanilla dialogs don't layer a separate frame sprite). If the
            // harvest found no donor, the flat CWindow fill + carved-colour Outline below
            // preserve the previous look (AT-UI-BG / AT-UI-FRAME, graceful fallback).
            if (!VanillaUISkin.SkinPanel(_window.GetComponent<Image>(), VanillaUISkin.PanelSprite))
                AddOutline(_window, CFrame);

            var col = _window.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(28, 28, 24, 24);
            col.spacing = 12f;
            col.childAlignment = TextAnchor.UpperCenter;
            col.childControlWidth = true;
            col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;
            var fitter = _window.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            var col_t = _window.transform;

            // Title
            MakeRowLabel(col_t, "Title", Loc("$sbpr_sign_title", "Painted Sign"), 26, FontStyle.Bold,
                TextAnchor.MiddleCenter, 36);

            // — PAINTING —
            MakeRowLabel(col_t, "PaintHeader", Loc("$sbpr_sign_painting", "— PAINTING —"), 18, FontStyle.Bold,
                TextAnchor.MiddleCenter, 26);

            // Three independent swatch rows (Daniel 2026-06-21): letters, board, frame.
            // Set Text Color → the written letters.
            BuildSwatchSection(col_t, Loc("$sbpr_sign_set_text_color", "Set Text Color:"), Slot.Text);

            // Board Color → the plank mesh.
            BuildSwatchSection(col_t, Loc("$sbpr_sign_board_color", "Board Color:"), Slot.Board);

            // Border Color → the frame bars.
            BuildSwatchSection(col_t, Loc("$sbpr_sign_border_color", "Border Color:"), Slot.Border);

            // Cost section (crafting-style requirement rows)
            MakeRowLabel(col_t, "CostLabel", Loc("$sbpr_sign_cost", "Cost:"), 16, FontStyle.Bold,
                TextAnchor.MiddleLeft, 24);
            var costHost = new GameObject("CostList");
            costHost.transform.SetParent(col_t, false);
            costHost.AddComponent<RectTransform>();
            var costCol = costHost.AddComponent<VerticalLayoutGroup>();
            costCol.spacing = 4f;
            costCol.childAlignment = TextAnchor.UpperLeft;
            costCol.childControlWidth = true;
            costCol.childControlHeight = true;
            costCol.childForceExpandWidth = true;
            costCol.childForceExpandHeight = false;
            AddLayoutElement(costHost, minHeight: 28);
            _costList = costHost;
            MakeRowLabel(costHost.transform, "CostEmpty",
                Loc("$sbpr_sign_cost_none", "(choose a color)"), 15, FontStyle.Italic,
                TextAnchor.MiddleLeft, 24);

            // Paint button
            _paintBtn = MakeButton(col_t, "PaintBtn", Loc("$sbpr_sign_paint", "Paint this and consume"),
                44, OnPaintClicked, out _paintBtnLabel);

            // — TEXT —
            MakeRowLabel(col_t, "TextHeader", Loc("$sbpr_sign_text", "— TEXT —"), 18, FontStyle.Bold,
                TextAnchor.MiddleCenter, 26);

            // Text input field
            _textField = MakeInputField(col_t, "TextField", 40);

            // Update Text + Close in a horizontal row
            var btnRow = new GameObject("ButtonRow");
            btnRow.transform.SetParent(col_t, false);
            btnRow.AddComponent<RectTransform>();
            var brow = btnRow.AddComponent<HorizontalLayoutGroup>();
            brow.spacing = 16f;
            brow.childAlignment = TextAnchor.MiddleCenter;
            brow.childControlWidth = true;
            brow.childControlHeight = true;
            brow.childForceExpandWidth = true;
            brow.childForceExpandHeight = false;
            AddLayoutElement(btnRow, minHeight: 44);
            _updateTextBtn = MakeButton(btnRow.transform, "UpdateTextBtn",
                Loc("$sbpr_sign_update_text", "Update Text"), 44, OnUpdateTextClicked, out _updateTextBtnLabel);
            MakeButton(btnRow.transform, "CloseBtn", Loc("$sbpr_sign_close", "Close"), 44, Hide, out _);
        }

        // ── Swatch section ───────────────────────────────────────────

        private void BuildSwatchSection(Transform parent, string label, Slot slot)
        {
            string rowName = slot == Slot.Border ? "BorderRow" : slot == Slot.Board ? "BoardRow" : "TextRow";
            string pfx     = slot == Slot.Border ? "Br" : slot == Slot.Board ? "Bd" : "T";
            var section = new GameObject(rowName);
            section.transform.SetParent(parent, false);
            section.AddComponent<RectTransform>();
            var row = section.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 8f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            AddLayoutElement(section, minHeight: 48);

            // Row label (fixed width so all rows align)
            var lbl = MakeFreeLabel(section.transform, "Label", label, 16, FontStyle.Normal, TextAnchor.MiddleLeft);
            AddLayoutElement(lbl.gameObject, preferredWidth: 170, minHeight: 44);

            var list = SwatchListFor(slot);
            var player = Player.m_localPlayer;

            // Explicit "None" affordance FIRST (Issue 4 — visible clear for every row).
            var none = MakeSwatch(section.transform, $"{pfx}_none", CSwatchNone, isNone: true,
                () => OnSwatchClicked("", slot));
            list.Add(("", none));

            // Then one swatch per DISCOVERED pigment (no dead reserved slots).
            foreach (var color in AllColors)
            {
                if (player != null && !SignPaintBackend.IsPigmentDiscovered(player, color))
                    continue; // undiscovered → not rendered at all
                var capture = color;
                var swatch = MakeSwatch(section.transform, $"{pfx}_{color}",
                    Signs.ColorValues.TryGetValue(color, out var c) ? c : Color.gray, isNone: false,
                    () => OnSwatchClicked(capture, slot));
                list.Add((color, swatch));
            }
        }

        // The swatch-button list backing a given slot's row.
        private List<(string color, Button btn)> SwatchListFor(Slot slot) =>
            slot == Slot.Border ? _borderSwatches : slot == Slot.Board ? _boardSwatches : _textSwatches;

        // ── Event handlers ───────────────────────────────────────────

        private void OnSwatchClicked(string color, Slot slot)
        {
            // Clicking "None" (color == "") clears; clicking a color sets it. Clicking
            // the already-selected color also clears (toggle), so either affordance works.
            switch (slot)
            {
                case Slot.Border: _selBorder = (_selBorder == color) ? "" : color; break;
                case Slot.Board:  _selBoard  = (_selBoard  == color) ? "" : color; break;
                default:          _selText   = (_selText   == color) ? "" : color; break;
            }

            RefreshSwatchHighlights();
            RefreshDynamic();
        }

        private void OnPaintClicked()
        {
            if (_tag == null) { Hide(); return; }
            var player = Player.m_localPlayer;
            var result = SignPaintBackend.CommitPaint(_tag, player, _selText, _selBoard, _selBorder);
            switch (result)
            {
                case SignPaintBackend.PaintResult.Success:
                    var cost = SignPaintBackend.ComputeCost(_selText, _selBoard, _selBorder);
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                        $"{Loc("$sbpr_sign_painted", "Painted sign")} ({DescribeCost(cost)}).");
                    break;
                case SignPaintBackend.PaintResult.NoColorChosen:
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                        Loc("$sbpr_sign_need_color", "Choose at least one color."));
                    break;
                case SignPaintBackend.PaintResult.InsufficientItems:
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                        Loc("$sbpr_sign_need_pigment", "Not enough pigment."));
                    break;
                default:
                    MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                        Loc("$sbpr_sign_paint_fail", "Could not paint the sign."));
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
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center,
                Loc("$sbpr_sign_text_updated", "Sign text updated."));
        }

        // ── Dynamic refresh ──────────────────────────────────────────

        private bool AnyColorChosen() => !string.IsNullOrEmpty(_selText) || !string.IsNullOrEmpty(_selBoard) || !string.IsNullOrEmpty(_selBorder);

        private void RefreshDynamic()
        {
            var player = Player.m_localPlayer;
            var cost = SignPaintBackend.ComputeCost(_selText, _selBoard, _selBorder);

            // Crafting-style cost rows (Issue 5).
            BuildCostRows(cost, player);

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
            void Mark(List<(string color, Button btn)> row, string sel)
            {
                foreach (var (color, btn) in row)
                {
                    if (btn == null) continue;
                    var outline = btn.GetComponent<Outline>();
                    if (outline == null) continue;
                    bool selected = color == sel;
                    outline.effectColor = selected ? CSelected : CUnselected;
                    outline.effectDistance = selected ? new Vector2(3, 3) : new Vector2(1.5f, 1.5f);
                }
            }
            Mark(_textSwatches, _selText);
            Mark(_boardSwatches, _selBoard);
            Mark(_borderSwatches, _selBorder);
        }

        /// <summary>
        /// Rebuild the cost section as vanilla-crafting-style requirement rows: each is
        /// an icon + pigment name + "have/need" count, count pulsing red while the player
        /// is short (mirrors InventoryGui.SetupRequirement's have&lt;need red flash, but
        /// rendered with our own primitives — clean-room). Empty cost → "(choose a color)".
        /// </summary>
        private void BuildCostRows(Dictionary<string, int> cost, Player? player)
        {
            if (_costList == null) return;
            // Clear previous rows (keep none — fully rebuilt each refresh).
            foreach (Transform c in _costList.transform) Destroy(c.gameObject);

            if (cost.Count == 0)
            {
                MakeRowLabel(_costList.transform, "CostEmpty",
                    Loc("$sbpr_sign_cost_none", "(choose a color)"), 15, FontStyle.Italic,
                    TextAnchor.MiddleLeft, 24);
                return;
            }

            foreach (var kv in cost)
            {
                int have = player != null ? SignPaintBackend.CountPigment(player, kv.Key) : 0;
                int need = kv.Value;
                BuildCostRow(kv.Key, have, need);
            }
        }

        private void BuildCostRow(string color, int have, int need)
        {
            if (_costList == null) return;
            var rowGo = new GameObject($"cost_{color}");
            rowGo.transform.SetParent(_costList.transform, false);
            rowGo.AddComponent<RectTransform>();
            var row = rowGo.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 10f;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = false;
            row.childControlHeight = false;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
            AddLayoutElement(rowGo, minHeight: 30);

            // Icon (pigment item sprite; falls back to a color swatch if no sprite).
            var iconGo = new GameObject("res_icon");
            iconGo.transform.SetParent(rowGo.transform, false);
            iconGo.AddComponent<RectTransform>();
            var img = iconGo.AddComponent<Image>();
            var sprite = SignPaintBackend.PigmentSprite(color);
            if (sprite != null) { img.sprite = sprite; img.color = Color.white; }
            else if (Signs.ColorValues.TryGetValue(color, out var col)) img.color = col;
            AddLayoutElement(iconGo, preferredWidth: 28, preferredHeight: 28, minHeight: 28);

            // Name
            var nameLbl = MakeFreeLabel(rowGo.transform, "res_name",
                SignPaintBackend.PigmentDisplayName(color), 15, FontStyle.Normal, TextAnchor.MiddleLeft);
            AddLayoutElement(nameLbl.gameObject, preferredWidth: 220, minHeight: 28);

            // have/need count — pulses red when short (crafting idiom).
            var amtLbl = MakeFreeLabel(rowGo.transform, "res_amount",
                $"{have}/{need}", 15, FontStyle.Bold, TextAnchor.MiddleLeft);
            bool short_ = have < need;
            amtLbl.color = short_ ? new Color(0.9f, 0.25f, 0.25f, 1f) : CParchment;
            AddLayoutElement(amtLbl.gameObject, preferredWidth: 70, minHeight: 28);
        }

        private static string DescribeCost(Dictionary<string, int> cost)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (var kv in cost)
            {
                if (!first) sb.Append(" + ");
                sb.Append(kv.Value).Append(' ').Append(SignPaintBackend.PigmentDisplayName(kv.Key));
                first = false;
            }
            return sb.ToString();
        }

        // ── Primitive builders ───────────────────────────────────────

        private Font? ResolveFont()
        {
            // Prefer the legacy Font underlying vanilla's TMP display face (Norse feel)
            // so our legacy UnityEngine.UI.Text matches the HUD/menu font (AT-UI-FONT).
            // VanillaUISkin handles all the fallbacks (TMP default → live TMP_Text →
            // any loaded Font → Arial) and never throws.
            return VanillaUISkin.Font;
        }

        private static GameObject MakeImage(Transform parent, string name, Color color)
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

        private static LayoutElement AddLayoutElement(GameObject go, float minHeight = -1, float preferredWidth = -1,
            float preferredHeight = -1)
        {
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (minHeight >= 0) le.minHeight = minHeight;
            if (preferredWidth >= 0) le.preferredWidth = preferredWidth;
            if (preferredHeight >= 0) le.preferredHeight = preferredHeight;
            return le;
        }

        /// <summary>A label that is itself a layout-group child (stretches to row width).</summary>
        private Text MakeRowLabel(Transform parent, string name, string text, int size, FontStyle style,
            TextAnchor align, float minHeight)
        {
            var t = MakeFreeLabel(parent, name, text, size, style, align);
            AddLayoutElement(t.gameObject, minHeight: minHeight);
            return t;
        }

        /// <summary>A bare label (caller controls its sizing via LayoutElement or rect).</summary>
        private Text MakeFreeLabel(Transform parent, string name, string text, int size, FontStyle style,
            TextAnchor align)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var t = go.AddComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = align;
            t.color = CParchment;
            t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        private Button MakeButton(Transform parent, string name, string label, float height,
            Action? onClick, out Text labelText)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = CButton;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            // Skin with the vanilla button sprite + its hover/pressed/disabled SpriteState
            // (AT-UI-BUTTONS). Falls back to the flat CButton fill + carved Outline when no
            // vanilla donor was harvested.
            bool skinned = VanillaUISkin.SkinButton(btn, img);
            if (!skinned)
                AddOutline(go, new Color(0f, 0f, 0f, 0.7f));
            AddLayoutElement(go, minHeight: height, preferredHeight: height);
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var lblGo = new GameObject("Label");
            lblGo.transform.SetParent(go.transform, false);
            var lrt = lblGo.AddComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            labelText = lblGo.AddComponent<Text>();
            labelText.font = _font;
            labelText.fontSize = 16;
            labelText.fontStyle = FontStyle.Bold;
            labelText.alignment = TextAnchor.MiddleCenter;
            // Dark label on the LIGHT skinned wood sprite; original cream on the dark flat
            // fallback fill (t_f2fe06d4 — never light-on-light). SetButtonEnabled tracks the
            // same split for the dynamically toggled Paint / Update Text buttons.
            labelText.color = skinned ? CBtnLabelOnSkin : CBtnLabelOnFlat;
            labelText.text = label;
            labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
            labelText.verticalOverflow = VerticalWrapMode.Overflow;
            return btn;
        }

        private Button MakeSwatch(Transform parent, string name, Color color, bool isNone, Action onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.interactable = true;
            // The "None" tile carries no pigment colour, so skin it with the vanilla
            // button sprite + hover states for the native carved-wood look. COLOUR
            // swatches keep their flat pigment fill — the colour IS the content, and a
            // wood-tinted sprite would muddy it (deliberate: a colour-picker tile reads
            // cleanly without competing with the pigment hue). The selection Outline
            // below is added for every swatch (RefreshSwatchHighlights drives it).
            if (isNone) VanillaUISkin.SkinButton(btn, img);
            AddOutline(go, CUnselected);
            AddLayoutElement(go, preferredWidth: 44, preferredHeight: 44, minHeight: 44);
            btn.onClick.AddListener(() => onClick());

            if (isNone)
            {
                // A diagonal "∅"-style hint: render an "X"/"None" glyph so the clear
                // affordance is legible even at swatch size.
                var glyph = new GameObject("NoneGlyph");
                glyph.transform.SetParent(go.transform, false);
                var grt = glyph.AddComponent<RectTransform>();
                grt.anchorMin = Vector2.zero; grt.anchorMax = Vector2.one;
                grt.offsetMin = Vector2.zero; grt.offsetMax = Vector2.zero;
                var gt = glyph.AddComponent<Text>();
                gt.font = _font;
                gt.fontSize = 18;
                gt.fontStyle = FontStyle.Bold;
                gt.alignment = TextAnchor.MiddleCenter;
                gt.color = CParchmentDim;
                gt.text = "∅";
                gt.horizontalOverflow = HorizontalWrapMode.Overflow;
                gt.verticalOverflow = VerticalWrapMode.Overflow;
            }
            return btn;
        }

        private InputField MakeInputField(Transform parent, string name, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.06f, 0.05f, 1f);
            // Skin the input background with the vanilla frame/inset sprite for parity
            // with the rest of the panel (AT-UI-PARITY). Falls back to the flat dark fill
            // + carved Outline when no donor was harvested.
            bool skinned = VanillaUISkin.SkinPanel(bg, VanillaUISkin.FrameSprite);
            if (!skinned)
                AddOutline(go, CFrame);
            AddLayoutElement(go, minHeight: height, preferredHeight: height);

            var input = go.AddComponent<InputField>();

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(10, 6); trt.offsetMax = new Vector2(-10, -6);
            var txt = textGo.AddComponent<Text>();
            txt.font = _font;
            txt.fontSize = 18;
            // Dark typed text on the LIGHT skinned frame; original light text on the dark
            // flat fallback fill (t_f2fe06d4 — never light-on-light). A *distinct* frame
            // sprite is the light carved inset (Daniel's screenshot); if FrameSprite
            // degrades to the dark PanelSprite backing (no secondary sliced sprite
            // harvested), that backing is dark — matching the window — so light text is
            // correct there. Identity check, not luminance: Valheim's atlas textures
            // aren't CPU-readable, and the contrast direction is fixed by spec.
            bool lightFrame = skinned && VanillaUISkin.FrameSprite != VanillaUISkin.PanelSprite;
            txt.color = lightFrame ? CInputTextOnSkin : CInputTextOnFlat;
            txt.supportRichText = false;
            txt.alignment = TextAnchor.MiddleLeft;

            var phGo = new GameObject("Placeholder");
            phGo.transform.SetParent(go.transform, false);
            var prt = phGo.AddComponent<RectTransform>();
            prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
            prt.offsetMin = new Vector2(10, 6); prt.offsetMax = new Vector2(-10, -6);
            var ph = phGo.AddComponent<Text>();
            ph.font = _font;
            ph.fontSize = 18;
            ph.fontStyle = FontStyle.Italic;
            // Placeholder tracks the same chrome but stays visibly DIMMER than the typed
            // text in both paths (a muted dark on the light frame, a muted light on the
            // dark fill) so it reads as a hint, not as content.
            ph.color = lightFrame ? CInputPlaceholderOnSkin : CInputPlaceholderOnFlat;
            ph.text = Loc("$sbpr_sign_text_placeholder", "Sign text…");
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
            {
                // Track the chrome: dark labels on the LIGHT skinned wood sprite, original
                // cream/dim-cream on the dark flat fallback fill (t_f2fe06d4). Both enabled
                // and disabled states stay legible — never light-on-light or dark-on-dark.
                bool skinned = VanillaUISkin.HasButtonSkin;
                label.color = enabled
                    ? (skinned ? CBtnLabelOnSkin : CBtnLabelOnFlat)
                    : (skinned ? CBtnLabelDisabledOnSkin : CBtnLabelDisabledOnFlat);
            }
        }

        /// <summary>
        /// Localize via vanilla Localization if a token is registered, else return the
        /// English fallback. Trailborne ships no locale file yet (v0.1.0 limitation), so
        /// the fallback is what renders today; the tokens are in place for the v0.2.0
        /// JSON locale pass so this panel localizes for free when that lands.
        /// </summary>
        private static string Loc(string token, string fallback)
        {
            try
            {
                var loc = Localization.instance;
                if (loc != null)
                {
                    var s = loc.Localize(token);
                    var bare = token.TrimStart('$');
                    // Vanilla Localize() returns an UNREGISTERED $token wrapped in brackets
                    // with the '$' stripped, e.g. Localize("$sbpr_sign_title") -> "[sbpr_sign_title]".
                    // Treat token / bare-token / "[bare-token]" all as "unresolved" and fall back.
                    // A genuine translation matches none of these and passes through unchanged.
                    if (!string.IsNullOrEmpty(s)
                        && s != token
                        && s != bare
                        && s != "[" + bare + "]")
                        return s;
                }
            }
            catch { /* fall through to fallback */ }
            return fallback;
        }

        private void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var existing = FindAnyObjectByType<EventSystem>();
            if (existing != null) return; // game has one — leave it alone

            var es = new GameObject("SBPR_SignPanelEventSystem");
            es.transform.SetParent(transform, false);
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
