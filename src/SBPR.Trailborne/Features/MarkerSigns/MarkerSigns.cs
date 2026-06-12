using System;
using System.Collections.Generic;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.MarkerSigns
{
    /// <summary>
    /// v2 Marker Signs — four buildable marker-sign pieces (POI, mining, shelter,
    /// portal) placed on the Trailblazer's Spade ('Trail' tab). Each reuses the Painted
    /// Sign interaction stack (it carries a <see cref="Sign"/> so the existing
    /// SignInteractPatch + panel apply) and, on Shift+E, pins/unpins itself on the map
    /// with a CUSTOM marker icon via the WorldPin substrate.
    ///
    /// This file owns:
    ///   • the data-driven type table (one source for registration + SpecCheck + panel),
    ///   • ADDITIVE prefab construction (ADR-0006 — new GameObject + AddComponent; NO
    ///     runtime clone of a ZNetView-bearing prefab — AT-PIN-ADR0006),
    ///   • ZNetScene registration + the ObjectDB-phase resource rebuild (Wood ×2 +
    ///     Greydwarf eye ×1 — a Black Forest availability gate),
    ///   • the ZDO-key + prefab-name string contracts (locked here, never renamed).
    ///
    /// The vanilla `sign` and `wood_pole2` prefabs are read as BLUEPRINTS only
    /// (ZNetScene.GetPrefab fires no Awake) for their mesh/material/effect references —
    /// reading the base game we mod is clean-side (ADR-0001). No third-party pin mod is
    /// read; the design is grounded entirely in the vanilla decomp.
    ///
    /// Design lock:  docs/design/marker-signs-worldpin.md
    /// Impl spec:    docs/v2/planning/marker-signs-impl-spec.md
    /// All registration gated behind ServerContext.OnSBServer (Registrar's fan-out).
    /// </summary>
    public static class MarkerSigns
    {
        // ── ZDO-key string contracts (save/wire — lock here, never rename) ──────────
        public const string ZdoMarkerType   = "SBPR_MarkerType";    // string: poi/mining/shelter/portal
        public const string ZdoPinned       = "SBPR_Pinned";        // bool:   pinned on the placer's map?
        public const string ZdoPinIconColor = "SBPR_PinIconColor";  // RESERVED (Q1 defers color)
        public const string ZdoPinTextColor = "SBPR_PinTextColor";  // RESERVED (Q1 defers color)

        // Build cost — Wood ×2 + Greydwarf eye ×1 (impl-spec §0). The +1 eye is a Black
        // Forest *availability gate*, not a cost tax: the eye is a Common BF drop, so
        // requiring one proves the player reached the Black Forest and killed a Greydwarf.
        // Markers stay cheap — they're just no longer craftable from turn-1 Meadows wood.
        // The map pin is still the value-add; the gate only sets the tier you unlock it at.
        public const int WoodCost = 2;

        // Greydwarf eye — vanilla Common BF drop (Greydwarf / Brute / Shaman). Token
        // confirmed against prefab_index.json + wiki Internal ID; do NOT retype from
        // memory (a wrong token silently drops the requirement — SpecCheck is the backstop).
        public const int    EyeCost     = 1;
        public const string EyeResource = "GreydwarfEye";

        // Vanilla blueprints (read-only, never cloned): the wood sign supplies the board
        // mesh/material + Sign field defaults + effect lists; the 2m pole is the post.
        private const string BlueprintSign = "sign";
        private const string BlueprintPole = "wood_pole2";

        /// <summary>
        /// One marker type. <c>VanillaPinType</c> is the valid, filter-toggleable base
        /// <see cref="Minimap.PinType"/> passed to AddPin; the CUSTOM sprite is overridden
        /// onto PinData.m_icon after AddPin (design §V1 — stable, no per-rebuild re-skin).
        /// Icon0 is a fine neutral base for all four (the player never sees the base sprite
        /// because we override it). Death/Boss/Player types are avoided (special culling).
        /// </summary>
        public sealed class MarkerType
        {
            public string Key = "";
            public string PrefabName = "";
            public string NiceName = "";
            public string PinLabel = "";
            public string IconFile = "";
            public Minimap.PinType VanillaPinType;
        }

        // The four marker types — the single data source (impl-spec §1.1).
        public static readonly MarkerType[] MarkerTypes =
        {
            new MarkerType {
                Key = "poi", PrefabName = "piece_sbpr_marker_poi",
                NiceName = "Marker: Point of Interest", PinLabel = "Point of Interest",
                IconFile = "marker_poi_v0.1.png", VanillaPinType = Minimap.PinType.Icon0,
            },
            new MarkerType {
                Key = "mining", PrefabName = "piece_sbpr_marker_mining",
                NiceName = "Marker: Mining", PinLabel = "Mining",
                IconFile = "marker_mining_v0.1.png", VanillaPinType = Minimap.PinType.Icon0,
            },
            new MarkerType {
                Key = "shelter", PrefabName = "piece_sbpr_marker_shelter",
                NiceName = "Marker: Shelter", PinLabel = "Shelter",
                IconFile = "marker_shelter_v0.1.png", VanillaPinType = Minimap.PinType.Icon0,
            },
            new MarkerType {
                Key = "portal", PrefabName = "piece_sbpr_marker_portal",
                NiceName = "Marker: Portal", PinLabel = "Portal",
                IconFile = "marker_portal_v0.1.png", VanillaPinType = Minimap.PinType.Icon0,
            },
        };

        public static MarkerType? ByKey(string key)
        {
            foreach (var m in MarkerTypes)
                if (m.Key == key) return m;
            return null;
        }

        public static MarkerType? ByPrefab(string prefabName)
        {
            foreach (var m in MarkerTypes)
                if (m.PrefabName == prefabName) return m;
            return null;
        }

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (ZNetScene.Awake postfix, via Registrar)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            foreach (var m in MarkerTypes)
                RegisterMarkerPiece(zns, m);
        }

        private static void RegisterMarkerPiece(ZNetScene zns, MarkerType m)
        {
            if (zns.GetPrefab(m.PrefabName) != null) return;

            var go = ConstructMarkerSign(zns, m);
            if (go == null)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/MarkerSigns] Could not construct marker piece '{m.PrefabName}' " +
                    $"({m.Key}); skipping this marker.");
                return;
            }

            Assets.RegisterPrefabInZNetScene(go);
            Plugin.Log.LogInfo($"[Trailborne/MarkerSigns] Registered marker piece: {m.PrefabName}");
        }

        /// <summary>
        /// ADDITIVELY build a marker-sign prefab (ADR-0006). Constructs a fresh
        /// GameObject under the inactive holder carrying exactly Piece + WearNTear +
        /// ZNetView(persistent) + Sign + MarkerSignTag + a root BoxCollider, plus a
        /// board+post visual grafted by READING the vanilla wood-sign / pole blueprint
        /// meshes (references, never an Instantiate of the ZNetView-bearing donor). This
        /// is the AT-PIN-ADR0006 path — no runtime clone of a networked prefab.
        /// </summary>
        private static GameObject? ConstructMarkerSign(ZNetScene zns, MarkerType m)
        {
            // Read the vanilla blueprints (no Awake — read-only). The sign supplies the
            // board mesh/material; the pole supplies the standing post mesh; the sign also
            // supplies WearNTear effect-list references + Sign field defaults. Missing
            // blueprints degrade to a bare (still functional) piece, logged once.
            var signBlueprint = zns.GetPrefab(BlueprintSign);
            var poleBlueprint = zns.GetPrefab(BlueprintPole);

            var go = Assets.NewHolderObject(m.PrefabName);

            // ── Root collider: a build piece needs one for placement raycasts + hits.
            //    Sized to the vanilla sign board AABB (1.0 x 0.55 x 0.089), lifted to the
            //    board height so the interact ray hits the plank. Exact seat/standoff is a
            //    v0.2+ polish call (design §1.2 — silhouette is not load-bearing for M1). ──
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(1.0f, 0.55f, 0.12f);
            box.center = new Vector3(0f, BoardLocalY, 0f);

            // ── ZNetView: the networked identity (persistent — the WorldPin durability
            //    rests on the sign's ZDO being persistent, design §3). ──
            var nview = go.AddComponent<ZNetView>();
            nview.m_persistent = true;
            nview.m_type = ZDO.ObjectType.Default;
            nview.m_distant = false;

            // ── Visual (additive mesh-reference graft, NOT a clone): a standing post and
            //    a board plank lifted to readable height. Both read shared mesh/material
            //    refs off the blueprints — permitted by ADR-0006. ──
            GraftPost(poleBlueprint, go);
            GameObject? board = GraftBoard(signBlueprint, go);

            // ── Sign: reuse the vanilla Sign component so SignInteractPatch + the paint
            //    panel + the text label all apply to the marker for free (impl-spec §1.2).
            //    A live m_textWidget is REQUIRED — Sign.Awake polls UpdateText() which
            //    writes m_textWidget.text and would NRE on a null widget — so we build a
            //    minimal additive world-space TMP widget and bind it. ──
            var sign = go.AddComponent<Sign>();
            var signBp = signBlueprint != null ? signBlueprint.GetComponent<Sign>() : null;
            sign.m_name           = m.NiceName;
            sign.m_defaultText    = m.PinLabel;
            sign.m_writtenBy      = signBp != null ? signBp.m_writtenBy : "Written by";
            sign.m_characterLimit = signBp != null ? signBp.m_characterLimit : 50;
            sign.m_textWidget     = BuildTextWidget(board ?? go, signBp);

            // ── WearNTear: wood material, vanilla wood-build norms; effect lists copied
            //    off the sign blueprint so place/hit/destroy look + sound vanilla. ──
            var wnt = go.AddComponent<WearNTear>();
            wnt.m_materialType = WearNTear.MaterialType.Wood;
            wnt.m_health = 100f;
            wnt.m_noRoofWear = true;
            wnt.m_noSupportWear = true;
            wnt.m_supports = false;
            wnt.m_burnable = false;          // a placed marker shouldn't burn away
            wnt.m_triggerPrivateArea = true;
            wnt.m_autoCreateFragments = true;
            CopyWearEffects(signBlueprint, wnt);

            // ── Piece: placement identity. Misc category = the single spade 'Trail' tab
            //    (every spade-placed SBPR piece is Misc; a non-Misc piece is added but its
            //    tab never renders — the cairn v0.2.2 vanish bug). No crafting station to
            //    place (Daniel: no bench requirement for signs/markers). ──
            var piece = go.AddComponent<Piece>();
            piece.m_name        = m.NiceName;
            piece.m_description  =
                $"A trail marker sign ({m.PinLabel}). Place it freely; press the use key to " +
                "edit its panel, and Shift+use to pin or unpin it on your map with its marker icon.";
            piece.m_category    = Piece.PieceCategory.Misc;
            piece.m_craftingStation = null;
            piece.m_groundPiece = false;
            piece.m_groundOnly  = false;
            piece.m_canBeRemoved = true;
            // First cut: piece build-icon art = the marker icon art (impl-spec §1.4;
            // Daniel: "for now, just make the piece art the icon art"). Sprite is null on
            // the headless server (no textures) — harmless, the menu icon is client-only.
            var icon = Assets.LoadPngAsSprite(m.IconFile);
            if (icon != null) piece.m_icon = icon;
            // Resources are (re)built authoritatively in the ObjectDB phase (Wood + the
            // Greydwarf eye are in ODB by then). Seed reqs now so the prefab is never
            // resource-less; warn=false because the ODB-phase rebuild is the authoritative pass.
            piece.m_resources = new[] { BuildReq("Wood", WoodCost, warn: false), BuildReq(EyeResource, EyeCost, warn: false) };

            // ── MarkerSignTag: per-instance ZDO state + the WearNTear destroy seam. ──
            var tag = go.AddComponent<MarkerSignTag>();
            tag.MarkerType = m.Key;
            tag.MarkerIcon = icon;

            return go;
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — authoritative resource rebuild (Wood in ODB by now).
        // The pieces are added to the SPADE PieceTable in Trailblazing.DoObjectDBWiring
        // (Registrar runs Trailblazing after this), NOT the Hammer.
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            foreach (var m in MarkerTypes)
            {
                var prefab = zns?.GetPrefab(m.PrefabName);
                var piece  = prefab != null ? prefab.GetComponent<Piece>() : null;
                if (piece != null)
                {
                    piece.m_resources = new[] { BuildReq("Wood", WoodCost), BuildReq(EyeResource, EyeCost) };
                    piece.m_craftingStation = null;
                }
            }

            Plugin.Log.LogInfo(
                "[Trailborne/MarkerSigns] ObjectDB wiring complete (4 marker pieces, Wood x2 + Greydwarf eye x1, " +
                "placed via the Spade 'Trail' tab).");
        }

        // ───────────────────────────────────────────────
        // Visual graft helpers (ADDITIVE mesh-reference grafts — clean-room safe,
        // NOT Instantiate-then-strip; ADR-0006 line 84-86)
        // ───────────────────────────────────────────────

        // Board centre height (sign-local metres). The vanilla sign board is a small
        // placard; on a free-standing trail marker we lift it to readable height near the
        // top of the 2m post. Exact value is v0.2+ visual polish (design §1.2 — the
        // silhouette is explicitly not load-bearing for M1); ~1.6m ≈ player eye height.
        private const float BoardLocalY = 1.6f;
        // Post extends from the ground up; the vanilla wood_pole2 is a centre-pivot 2m
        // cube mesh, so to plant its foot at y=0 we lift it by ~1m. Visual polish.
        private const float PostLocalY = 1.0f;

        /// <summary>
        /// Graft the board plank as an additive child: read the vanilla sign's board
        /// mesh/material references (NOT an Instantiate of the sign) and lift the plank to
        /// readable height. Returns the board child (so the text widget can parent to it),
        /// or null if the blueprint is missing.
        /// </summary>
        private static GameObject? GraftBoard(GameObject? signBlueprint, GameObject dst)
        {
            if (signBlueprint == null)
            {
                Plugin.Log.LogWarning(
                    "[Trailborne/MarkerSigns] Sign blueprint missing; marker piece will have no board " +
                    "visual this build (still placeable + pinnable, just visually bare).");
                return null;
            }
            // The sign's visible plank is a Cube_Cube_Material mesh under 'New/wood_pole (1)'.
            var board = Assets.GraftMeshFromBlueprint(signBlueprint, dst, "SBPR_MarkerBoard", "pole");
            if (board != null)
            {
                var lp = board.transform.localPosition;
                board.transform.localPosition = new Vector3(lp.x, BoardLocalY, lp.z);
            }
            return board;
        }

        /// <summary>Graft the 2m wood pole as an additive decorative post (mesh-reference,
        /// not a clone) and plant its foot at the ground.</summary>
        private static void GraftPost(GameObject? poleBlueprint, GameObject dst)
        {
            if (poleBlueprint == null)
            {
                Plugin.Log.LogWarning(
                    "[Trailborne/MarkerSigns] Pole blueprint missing; marker placed without a post " +
                    "(board visual only).");
                return;
            }
            var post = Assets.GraftMeshFromBlueprint(poleBlueprint, dst, "SBPR_MarkerPost", "New");
            if (post != null)
            {
                var lp = post.transform.localPosition;
                post.transform.localPosition = new Vector3(0f, PostLocalY, 0f);
            }
        }

        /// <summary>
        /// Build a minimal additive world-space Text widget for the Sign component to
        /// write into. <see cref="Sign"/>.Awake polls <c>UpdateText()</c>, which sets
        /// <c>m_textWidget.text</c>; a null widget would NRE on every poll, so even though
        /// the marker's primary interaction is intercepted by the paint panel, the Sign
        /// still needs a live <see cref="TMPro.TextMeshProUGUI"/>. Built from public uGUI
        /// + TMP primitives (no copied vanilla UI prefab) — clean-room safe.
        ///
        /// Inherits the FONT + face material REFERENCE off the vanilla sign blueprint's
        /// own text widget when available (a bare TMP with no font asset can fail to
        /// render); reading an asset reference off the base-game prefab is permitted
        /// (ADR-0001). Falls back to TMP's default font if the blueprint widget is absent.
        /// </summary>
        private static TMPro.TextMeshProUGUI BuildTextWidget(GameObject parent, Sign? signBp)
        {
            var canvasGo = new GameObject("SBPR_MarkerTextCanvas");
            canvasGo.transform.SetParent(parent.transform, worldPositionStays: false);
            canvasGo.transform.localPosition = new Vector3(0f, 0f, -0.07f); // just off the board face
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = canvasGo.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.sizeDelta = new Vector2(100f, 55f);
                rt.localScale = new Vector3(0.01f, 0.01f, 0.01f); // 100 uGUI units ≈ 1m board
            }

            var textGo = new GameObject("SBPR_MarkerText");
            textGo.transform.SetParent(canvasGo.transform, worldPositionStays: false);
            var tmp = textGo.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.text = "";
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 1f;
            tmp.fontSizeMax = 24f;
            // Inherit the vanilla sign's font asset/material by reference IF the blueprint's
            // widget happens to be bound. NOTE: on the prefab TEMPLATE (registration time,
            // under the inactive holder) the blueprint's Sign.Awake has not run, so
            // m_textWidget is null there (Signs.cs:432 documents the same) — the TMP default
            // font is the fallback in that case. Font polish is a v0.2+ concern; the text
            // surface is secondary to the panel for markers. Reading an asset reference off
            // the base-game prefab is permitted (ADR-0001).
            var bpWidget = signBp != null ? signBp.m_textWidget : null;
            if (bpWidget != null)
            {
                if (bpWidget.font != null) tmp.font = bpWidget.font;
                if (bpWidget.fontSharedMaterial != null) tmp.fontSharedMaterial = bpWidget.fontSharedMaterial;
            }
            var trt = textGo.GetComponent<RectTransform>();
            if (trt != null)
            {
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = Vector2.zero;
                trt.offsetMax = Vector2.zero;
            }
            return tmp;
        }

        /// <summary>Value-copy the sign blueprint's WearNTear place/hit/destroy effect
        /// lists onto our WearNTear so the marker sounds/looks like a wood build.</summary>
        private static void CopyWearEffects(GameObject? signBlueprint, WearNTear wnt)
        {
            if (signBlueprint == null) return;
            var dWnt = signBlueprint.GetComponent<WearNTear>();
            if (dWnt != null)
            {
                wnt.m_destroyedEffect = dWnt.m_destroyedEffect;
                wnt.m_hitEffect = dWnt.m_hitEffect;
                wnt.m_switchEffect = dWnt.m_switchEffect;
            }
        }

        private static Piece.Requirement BuildReq(string resourcePrefabName, int amount, bool warn = true)
        {
            return Assets.BuildReq(resourcePrefabName, amount, "MarkerSigns", warn);
        }
    }
}
