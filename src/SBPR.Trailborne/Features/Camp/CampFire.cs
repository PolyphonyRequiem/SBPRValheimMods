// ============================================================================
//  Trailborne — Covered Camp Fire (piece_sbpr_camp_fire)
// ----------------------------------------------------------------------------
//  The FIRE half of the Trailside Camp triad (impl-spec §3). A small additive
//  Fireplace whose Heat EffectArea satisfies the vanilla bed sleep gate 4
//  (Bed.CheckFire → EffectArea.IsPointInsideArea(pos, Heat)). Under the tent canopy
//  it stays lit in rain (underRoof beats Fireplace.CheckWet's rain clause); a high-wind
//  storm still douses it (canopy 0.47 < the 0.7 wind-cover threshold) — accepted as
//  ordinary honest behavior (Q4: Daniel's parent-task resolution — "ordinary rain
//  campfire behavior; no invisible mini-roof").
//
//  INVERTING THE CAIRN MACHINERY (impl-spec §3.1)
//  ----------------------------------------------
//  The Cairns feature builds a cosmetic flame that STRIPS the Heat EffectArea (a cairn
//  is a non-burning marker — Assets.GraftTorchFire defensively DestroyImmediates any
//  EffectArea that rides along). The camp fire is the OPPOSITE: it KEEPS a Heat area.
//  So we reuse GraftTorchFire for the flame VFX/light/sfx, then ADD our own Heat
//  EffectArea (the cairn never does) parented under the Fireplace's m_enabledObject so
//  it toggles with lit state — which is exactly what makes AT-BEDROLL-NOFIRE work: no
//  fuel → Fireplace.UpdateState deactivates m_enabledObject → the Heat collider goes
//  inactive → CheckFire fails with $msg_bednofire.
//
//  WHY ADDITIVE FIREPLACE, NOT A fire_pit CLONE (ADR-0006)
//  -------------------------------------------------------
//  We never Instantiate the networked fire_pit. We AddComponent<Fireplace>() and wire
//  the minimum field set the decompiled Fireplace.Awake/UpdateState/IsBurning read:
//  m_fuelItem (Wood), m_startFuel/m_maxFuel/m_secPerFuel, and m_enabledObject (the
//  child holding the Heat area + flame that UpdateState toggles active when burning).
//
//  Both Spade-placed (Q3), Misc category, "Trail" tab — matches the tent + bedroll.
//  All gated behind ServerContext.OnSBServer via Registrar.
// ============================================================================

using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Camp
{
    public static class CampFire
    {
        // LOCKED prefab name — save/wire contract; never rename.
        public const string CampFireName = "piece_sbpr_camp_fire";

        // Clean structural donor for the shell's effect-table reference-copy.
        private const string ShellEffectDonor = "wood_floor";

        // Fuel item — vanilla Wood, resolved in the ODB pass (not present at ZNetScene.Awake).
        private const string FuelItemName = "Wood";

        // Heat area radius (m). Tuned so a bedroll placed under the same canopy sits inside
        // it. The vanilla fire_pit Heat sphere is ~4 m; a camp fire is small, so 3.5 m keeps
        // it a "you must camp close to the fire" gate without being fiddly. Tunable polish.
        private const float HeatRadius = 3.5f;

        // Build cost — Black-Forest band (PROVISIONAL pending Daniel's recipe lock).
        public const int WoodCost  = 5;   // the fire itself
        public const int StoneCost = 3;   // the ring
        public const int CoalCost  = 2;   // kindling

        private const float CampFireHealth = 200f;

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (ZNetScene.Awake postfix, via Registrar)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            if (zns.GetPrefab(CampFireName) != null) return;

            if (!Assets.TryConstructPieceShell(CampFireName, ShellEffectDonor, out var go))
            {
                Plugin.Log.LogWarning($"[Trailborne/Camp] Could not construct piece shell for {CampFireName}; skipping.");
                return;
            }

            var wnt = go.GetComponent<WearNTear>();
            if (wnt != null)
            {
                wnt.m_health = CampFireHealth;
                wnt.m_materialType = WearNTear.MaterialType.Wood;
                // A camp fire is a fire — but the PIECE shouldn't self-ignite/burn away; the
                // flame is cosmetic + the Heat area is the gameplay. Keep it non-burnable so
                // it behaves like the vanilla fire_pit piece (which doesn't burn down).
                wnt.m_burnable = false;
            }

            var piece = go.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name = "Camp Fire";
                piece.m_description =
                    "A small trailside camp fire. Keeps a bedroll camp warm enough to sleep. " +
                    "Under a tent canopy it stays lit in the rain — but a howling storm will " +
                    "still put it out. Burns Wood. (Placeholder art.)";
                piece.m_category = Piece.PieceCategory.Misc;
                piece.m_craftingStation = null;
                piece.m_resources = BuildResources();
                piece.m_comfort = 0;
                piece.m_comfortGroup = Piece.ComfortGroup.None;
            }

            // ── The lit-state toggle root. Fireplace.UpdateState SetActive()s m_enabledObject
            //    true while burning, false when out. We parent BOTH the Heat EffectArea and the
            //    flame VFX under it, so "fire goes out" → Heat collider deactivates →
            //    Bed.CheckFire fails ($msg_bednofire). This is the inversion of the cairn (which
            //    keeps NO heat): here the heat is load-bearing. ──
            var enabledRoot = new GameObject("SBPR_CampFireEnabled");
            enabledRoot.transform.SetParent(go.transform, worldPositionStays: false);
            enabledRoot.transform.localPosition = new Vector3(0f, 0.2f, 0f);

            // Heat EffectArea: a trigger sphere on the "character_trigger" layer (the layer
            // EffectArea.IsPointInsideArea overlaps — decomp EffectArea.Awake :105158). m_type
            // Heat is what Bed.CheckFire keys on. Built additively — the cairn NEVER does this.
            var heat = new GameObject("SBPR_CampFireHeat");
            heat.transform.SetParent(enabledRoot.transform, worldPositionStays: false);
            heat.transform.localPosition = Vector3.zero;
            int charTrigger = LayerMask.NameToLayer("character_trigger");
            if (charTrigger >= 0) heat.layer = charTrigger;
            var sphere = heat.AddComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = HeatRadius;
            var ea = heat.AddComponent<EffectArea>();
            ea.m_type = EffectArea.Type.Heat;
            ea.m_statusEffect = "";   // heat gate only — no warm/burn status effect applied

            // Flame VFX/light/sfx: reuse the cairn's torch-fire graft, but KEEP it (the cairn
            // strips heat; we already added heat above). GraftTorchFire itself strips any
            // EffectArea from the flame subtree, which is fine — our Heat area is separate.
            var flame = Assets.GraftTorchFire(enabledRoot.transform, 0.1f, lightIntensity: 1.1f, lightRange: 6f);
            if (flame == null)
                Plugin.Log.LogWarning(
                    $"[Trailborne/Camp] {CampFireName}: flame VFX graft failed; the fire still provides heat " +
                    "and gates sleep, but shows no flame this load (check torch donor registration order).");

            // ── Fireplace: the fuel/lit state machine. Wire the minimum fields the decompiled
            //    Awake/UpdateState/IsBurning read. m_enabledObject is the toggle root above. ──
            var fire = go.AddComponent<Fireplace>();
            fire.m_name = "Camp Fire";
            fire.m_enabledObject = enabledRoot;
            fire.m_startFuel = 3f;
            fire.m_maxFuel = 10f;
            fire.m_secPerFuel = 60f;      // 1 Wood ≈ 1 minute; small camp fire, tune in playtest
            fire.m_infiniteFuel = false;
            fire.m_canRefill = true;
            fire.m_canTurnOff = false;
            fire.m_fuelItem = null;       // resolved in DoObjectDBWiring (Wood not in ODB yet here)
            // Leave m_fullObject/m_halfObject/m_emptyObject null — UpdateState guards each with
            // (bool)m_x, so a single-state camp fire is valid (no half/empty visual tiers).

            // Root collider: small camp-fire footprint.
            var box = go.GetComponent<BoxCollider>();
            if (box != null) { box.size = new Vector3(1.0f, 0.6f, 1.0f); box.center = new Vector3(0f, 0.3f, 0f); }

            Assets.RegisterPrefabInZNetScene(go);
            Plugin.Log.LogInfo($"[Trailborne/Camp] Registered Camp Fire piece: {CampFireName} (additive Fireplace + kept Heat EffectArea).");
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — resolve the Wood fuel item + rebuild resources
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            var p = zns?.GetPrefab(CampFireName);
            if (p == null) return;

            var piece = p.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_resources = BuildResources();
                piece.m_craftingStation = null;
            }

            // Resolve the Wood fuel ItemDrop now that ObjectDB is populated. Without this the
            // fire can't be refueled (Fireplace.Interact NREs on a null m_fuelItem when the
            // player E-presses to add fuel). Loud-warn on failure — a null fuel item is the
            // silent-bug class the skill warns about.
            var fire = p.GetComponent<Fireplace>();
            if (fire != null)
            {
                var woodPrefab = odb.GetItemPrefab(FuelItemName);
                var woodDrop = woodPrefab != null ? woodPrefab.GetComponent<ItemDrop>() : null;
                if (woodDrop != null) fire.m_fuelItem = woodDrop;
                else Plugin.Log.LogWarning(
                    $"[Trailborne/Camp] {CampFireName}: fuel item '{FuelItemName}' NOT FOUND in ObjectDB; " +
                    "the camp fire cannot be refueled (E-press to add fuel would fail).");
            }

            Plugin.Log.LogInfo("[Trailborne/Camp] Camp Fire ObjectDB wiring complete (Wood fuel resolved; placed via Spade menu).");
        }

        private static Piece.Requirement[] BuildResources()
        {
            return new[]
            {
                Assets.BuildReq("Wood",  WoodCost,  "Camp"),
                Assets.BuildReq("Stone", StoneCost, "Camp"),
                Assets.BuildReq("Coal",  CoalCost,  "Camp"),
            };
        }
    }
}
