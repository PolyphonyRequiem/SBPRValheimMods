using System;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Portals
{
    // Features.Trailhead is both a NAMESPACE and a TYPE name; from inside this sibling
    // Features.* namespace the bare `Trailhead` would bind to the sibling namespace.
    // Alias the TYPE so `Trailhead.ExplorersBenchName` reads cleanly (the Cairns pattern).
    using Trailhead = SBPR.Trailborne.Features.Trailhead.Trailhead;

    /// <summary>
    /// v2 Black-Forest content: Portal Seed → Ancient Portal — TWO prefabs on the cairn
    /// pattern (a marker ITEM whose recipe is checked + a PIECE whose build cost IS that
    /// item, so break→seed is free via vanilla <see cref="Piece.DropResources"/>):
    ///
    ///   1. <c>SBPR_PortalSeed</c> — a 25 kg <see cref="ItemDrop"/> crafted at the
    ///      Explorer's Bench (AncientSeed ×1 + GreydwarfEye ×20 + SurtlingCore ×2).
    ///   2. <c>piece_sbpr_ancient_portal</c> — a Hammer-placed build piece (Misc, no
    ///      station, SOLID-EARTH only) whose cost is one Portal Seed, carrying a real
    ///      vanilla <see cref="TeleportWorld"/> + an overhead jump-through trigger + a
    ///      ~15 s grow timer (<see cref="AncientPortalTag"/>).
    ///
    /// Both built ADDITIVELY (ADR-0006: <see cref="Assets.ConstructItemShell"/> /
    /// <see cref="Assets.ConstructPieceShell"/> + mesh-reference grafts) — NEVER cloning
    /// the ZNetView+EffectArea+GuidePoint-bearing <c>portal_wood</c> (the cairn-soft-lock
    /// bug class). Donors are read as blueprints only (ZNetScene.GetPrefab, no Awake).
    ///
    /// 🔴 THE THREE LOAD-BEARING HOOKS (spec §1, §3.5, §3.7):
    ///   1. <see cref="EnsurePortalHashRegistered"/> adds our prefab hash to
    ///      <c>Game.instance.PortalPrefabHash</c> — WITHOUT it the portal places, grows,
    ///      and locally-activates but NEVER tag-pairs (silent logs-green failure). The
    ///      #1 risk. Verified the list is build-once-and-only-Add'd in Game.Awake
    ///      (assembly_valheim:84083-89), so an idempotent re-assert never wipes it.
    ///   2. <c>TeleportWorld.m_model</c> is wired to a real ring MeshRenderer or its
    ///      <c>Update()</c> (assembly_valheim:122996-99) NREs 60×/s on m_model.material.
    ///      We leave m_proximityRoot/m_target_found BOTH null — UpdatePortal (:122976-93)
    ///      is m_proximityRoot==null-guarded so it's a safe no-op.
    ///   3. The overhead <see cref="TeleportWorldTrigger"/> child collider is the activation
    ///      surface (OnTriggerEnter→Teleport, :123144-51). Positioned flat at the ring; the
    ///      grow timer gates teleport by toggling THIS collider (not TeleportWorld.enabled,
    ///      which the trigger bypasses). The exact box size is desk-estimated and FLAGGED
    ///      for in-game tuning (AT-JUMP-ACTIVATE).
    ///
    /// Patch-free by construction (TeleportWorld + trigger + ZDO timer are all component
    /// wiring). All registration gated behind ServerContext.OnSBServer (Registrar's fan-out).
    ///
    /// Design lock:  docs/design/pocket-portal.md + docs/design/ancient-portal-placeholder-art.md
    /// Impl spec:    docs/v2/planning/ancient-portal-impl-spec.md
    /// </summary>
    public static class Portals
    {
        // ── Prefab-name string contracts (save/wire — LOCK here, never rename) ──────
        public const string SeedItemName  = "SBPR_PortalSeed";
        public const string PortalPieceName = "piece_sbpr_ancient_portal";

        // The grow timer scales a dedicated VISUAL child (not the ZNetView/collider root,
        // which must stay unit-scale for placement + networking). AncientPortalTag.ApplyGrowVisual
        // does transform.Find(VisualRootName), so this MUST be the name of a DIRECT child.
        public const string VisualRootName = "SBPR_AncientPortalVisual";

        // ── Seed recipe (LOCKED — SpecCheck row 1; spec §0/§2.3) ────────────────────
        // AncientSeed ×1 + GreydwarfEye ×20 + SurtlingCore ×2 @ Explorer's Bench, amount 1.
        // GreydwarfEye is referenced via MarkerSigns.EyeResource so the two stay in lockstep
        // (spec §0) — but to avoid a cross-feature compile coupling we keep the literal here
        // matched to that const and let SpecCheck be the drift backstop. Both resolve to the
        // same vanilla internal id.
        private const string ResAncientSeed = "AncientSeed";
        private const string ResGreydwarfEye = MarkerSigns.MarkerSigns.EyeResource; // "GreydwarfEye"
        private const string ResSurtlingCore = "SurtlingCore";
        public const int SeedAncientSeedCost  = 1;
        public const int SeedGreydwarfEyeCost = 20;
        public const int SeedSurtlingCoreCost = 2;
        // 🟡 Ectoplasm substitution for the eyes/cores is a playtest-contingent note only —
        // DO NOT implement (spec §2.3). First build ships the recipe above unchanged.

        // ── Item SharedData (LOCKED — spec §2.2) ────────────────────────────────────
        public const float SeedWeight = 25f;   // the locked 25 kg (Daniel)
        public const int   SeedMaxStack = 1;   // one-per-slot pack commitment (5×=125 kg is a trap)

        // ── Portal piece (LOCKED — spec §3.3/§3.4) ──────────────────────────────────
        public const float PortalHealth = 300f;   // DECIDED Daniel 2026-06-13 (75% of vanilla's 400)

        // Visual envelope target (~3 m tall × ~3 m wide; ring at the TOP). These are
        // DESK ESTIMATES for the placeholder kitbash — flagged for AT-GEOMETRY in-game
        // tuning (spec §3.2/§3.7). The grafted donor meshes are scaled into this envelope.
        private const float EnvelopeHeight = 3f;
        private const string IconFile = "portal_seed_v0.1.png";

        // ── Donor blueprints (read-only, never cloned — ADR-0006) ───────────────────
        private const string DonorPortal = "portal_wood";       // ring (New/small_portal) + effect donor
        private const string DonorRoot   = "Greydwarf_Root";    // root tendrils (default mesh)
        private const string DonorStump  = "stubbe";            // legs (cylinder stump mesh)

        // ════════════════════════════════════════════════════════════════════════════
        // PREFAB REGISTRATION (ZNetScene.Awake postfix, via Registrar)
        // ════════════════════════════════════════════════════════════════════════════

        public static void RegisterPrefabs(ZNetScene zns)
        {
            RegisterSeedItem(zns);
            RegisterPortalPiece(zns);

            // 🔴 #1 RISK — register our prefab hash so two Ancient Portals actually tag-pair.
            // Idempotent + null-Game-guarded; re-asserted in DoObjectDBWiring in case
            // Game.instance wasn't up yet here (Game.Awake may run before/after our hook).
            EnsurePortalHashRegistered();
        }

        private static void RegisterSeedItem(ZNetScene zns)
        {
            if (zns.GetPrefab(SeedItemName) != null) return;

            // ADR-0006 additive item shell: ZNetView + ZSyncTransform + Rigidbody +
            // item-layer BoxCollider + ItemDrop with a FRESH SharedData and the seeded
            // FallbackIcon. Do NOT clone a vanilla item.
            var go = Assets.ConstructItemShell(SeedItemName);
            if (go == null)
            {
                Plugin.Log.LogWarning("[Trailborne/Portals] Could not construct Portal Seed item shell; skipping seed.");
                return;
            }

            var drop = go.GetComponent<ItemDrop>();
            if (drop != null)
            {
                var sh = drop.m_itemData.m_shared;
                sh.m_name = "Portal Seed";
                sh.m_description =
                    "A gnarled seed swollen with portal-magic. Plant it with a Hammer on solid earth and " +
                    "it grows into an Ancient Portal over ~15 s — no workbench needed. Heavy: you carry ONE.";
                // Material = a carried/build-ingredient item (consumed as the portal piece's
                // m_resources cost), NOT a placement tool. It is NOT placed by being equipped;
                // the Hammer's PieceTable holds the portal whose cost is this Material (spec §2.2).
                sh.m_itemType = ItemDrop.ItemData.ItemType.Material;
                sh.m_weight = SeedWeight;             // 25 kg
                sh.m_maxStackSize = SeedMaxStack;     // 1
                // m_teleportable: leave the vanilla default true — the Seed ITEM may pass
                // through portals (it's not ore). The ore-ban is enforced portal-side (§3.5).

                // Real icon over the seeded magenta fallback (SpecCheck C1 screams if absent).
                var sprite = Assets.LoadPngAsSprite(IconFile);
                if (sprite != null) drop.m_itemData.m_shared.m_icons = new[] { sprite };
            }

            // Visual: a small scaled-down Greydwarf root knot reads as a "gnarled seed/root
            // bulb" (spec §2.1). Mesh-reference graft (not a clone); scaled tiny to a held-item
            // size. Placeholder — flagged for the art pass.
            var visual = Assets.GraftMeshFromBlueprint(zns.GetPrefab(DonorRoot), go, "SBPR_PortalSeedVisual", "default");
            if (visual != null)
            {
                // Donor root mesh is ~4 m across; shrink to a ~0.35 m hand-held bulb.
                visual.transform.localScale = new Vector3(0.09f, 0.09f, 0.09f);
                visual.transform.localPosition = Vector3.zero;
            }

            Assets.RegisterPrefabInZNetScene(go);
            Plugin.Log.LogInfo($"[Trailborne/Portals] Registered item: {SeedItemName} (25 kg, stack 1).");
        }

        private static void RegisterPortalPiece(ZNetScene zns)
        {
            if (zns.GetPrefab(PortalPieceName) != null) return;

            // ADR-0006 additive piece shell: ZNetView + Piece + WearNTear + root BoxCollider,
            // with hit/destroy/place effects reference-copied off the portal_wood blueprint
            // (read via GetPrefab — fires no Awake) so it sounds like a wooden/organic build.
            var go = Assets.ConstructPieceShell(PortalPieceName, DonorPortal);
            if (go == null)
            {
                Plugin.Log.LogWarning("[Trailborne/Portals] Could not construct Ancient Portal piece shell; skipping portal.");
                return;
            }

            // ── Visual root (the grow-scaled parent — a DIRECT child named VisualRootName) ──
            var visualRoot = new GameObject(VisualRootName);
            visualRoot.transform.SetParent(go.transform, worldPositionStays: false);

            // ── Grafted kitbash (all mesh-reference, ZNetView-free; placeholder transforms
            //    DESK-ESTIMATED for the ~3 m envelope, FLAGGED for AT-GEOMETRY in-game tuning). ──
            var portalBlueprint = zns.GetPrefab(DonorPortal);
            var rootBlueprint   = zns.GetPrefab(DonorRoot);
            var stumpBlueprint  = zns.GetPrefab(DonorStump);

            // Ring/glow on TOP (~3 m up), lying FLAT (face up) — the player jumps up into it.
            // portal_wood's small_portal mesh (Cube.002, 4.23×3.29 m) self-glows via emission.
            var ring = Assets.GraftMeshFromBlueprint(portalBlueprint, visualRoot, "SBPR_PortalRing", "small_portal");
            if (ring != null)
            {
                ring.transform.localPosition = new Vector3(0f, EnvelopeHeight, 0f);
                ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // lie flat (face up)
                ring.transform.localScale    = new Vector3(0.71f, 0.71f, 0.71f); // ~3 m wide
            }

            // Legs: 3 thin tall pillars (scaled-down stubbe stump) holding the ring overhead.
            BuildLegs(stumpBlueprint, visualRoot);

            // Roots: a couple of tendrils weaving up toward the ring (placeholder; art pass later).
            BuildRoots(rootBlueprint, visualRoot);

            // ── WearNTear: organic wood, HP 300, no rain decay (spec §3.3) ──────────────
            var wnt = go.GetComponent<WearNTear>();
            if (wnt != null)
            {
                wnt.m_materialType = WearNTear.MaterialType.Wood;  // axe/fire, not the shell's Stone default
                wnt.m_health = PortalHealth;                        // 300 (Daniel)
                wnt.m_noRoofWear = true;                            // no rain decay (Daniel confirmed)
                wnt.m_burnable = false;                             // don't let it burn away on its own
                // m_canBeRemoved stays true (set by the shell) → deconstruct returns the seed.
            }

            // ── Piece: Hammer-placed, no station, SOLID-EARTH only (spec §3.4/§3.4b) ────
            var piece = go.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name = "Ancient Portal";
                piece.m_description =
                    "A portal grown from a planted seed. Place it on solid earth with the Hammer; it grows " +
                    "over ~15 s, then jump up into the ring to travel. Otherwise a regular portal — tag-pair " +
                    "two of them, ore still can't pass. More fragile than a built portal.";
                piece.m_category = Piece.PieceCategory.Misc;        // Hammer 'Misc' tab
                piece.m_craftingStation = null;                     // NO bench-in-range to place (§3.4)
                piece.m_groundOnly = true;                          // solid earth only, not on structures (§3.4b)
                // Build cost is rebuilt authoritatively in DoObjectDBWiring (the Seed isn't in
                // ObjectDB yet at this prefab-build phase). Seed it now so the prefab is never
                // resource-less; warn=false because the ODB-phase rebuild is the authoritative pass.
                piece.m_resources = new[] { BuildReq(SeedItemName, 1, warn: false) };
                // Build-menu thumbnail: reuse the seed icon (pieces use m_icon; absent = no
                // thumbnail, non-fatal). Sprite is null on the headless server — harmless.
                var icon = Assets.LoadPngAsSprite(IconFile);
                if (icon != null) piece.m_icon = icon;
            }

            // ── TeleportWorld: the real vanilla teleporter (ore-ban inherited free) ─────
            var teleport = go.AddComponent<TeleportWorld>();
            // 🔴 NRE FIX (#2): m_model MUST be a real MeshRenderer or Update() NREs 60×/s on
            //   m_model.material. Point it at the ring's renderer. Fall back to ANY grafted
            //   renderer (legs/roots also carry portal/root materials) if the ring is somehow
            //   absent, so the frame-loop never derefs null. All donors are core prefabs that
            //   are always loaded, so in practice the ring resolves.
            var ringRenderer = ring != null ? ring.GetComponent<MeshRenderer>() : null;
            if (ringRenderer == null) ringRenderer = visualRoot.GetComponentInChildren<MeshRenderer>();
            teleport.m_model = ringRenderer;
            if (ringRenderer == null)
                Plugin.Log.LogError(
                    "[Trailborne/Portals] Ancient Portal has NO grafted MeshRenderer for TeleportWorld.m_model — " +
                    "TeleportWorld.Update would NRE. All visual donors (portal_wood/Greydwarf_Root/stubbe) failed " +
                    "to graft; the piece will be broken. Check donor prefab availability.");
            // Leave m_proximityRoot + m_target_found BOTH null (consistent): UpdatePortal is
            // m_proximityRoot==null-guarded (safe no-op), so neither is dereferenced. We lose
            // only the cosmetic "target found" glow pulse for v1 (spec §3.5).
            // Leave m_allowAllItems = false → the vanilla ore/metal ban holds with zero code (§3.5).

            // ── Overhead jump-through trigger (#3): a flat box collider at the ring, gated by
            //    the grow timer. Parented under the PIECE ROOT (sibling of the visual root) so
            //    its geometry is FIXED regardless of grow scale; TeleportWorldTrigger.Awake walks
            //    GetComponentInParent<TeleportWorld>() up to the piece root. The grow tag finds it
            //    via GetComponentInChildren<TeleportWorldTrigger> and toggles its collider. ──
            BuildOverheadTrigger(go);

            // ── Resize the root WearNTear/placement collider to cover the visible envelope so
            //    axe/deconstruct hits land on the structure (the shell defaults a unit box). ──
            var rootBox = go.GetComponent<BoxCollider>();
            if (rootBox != null)
            {
                rootBox.size = new Vector3(2.0f, EnvelopeHeight + 0.5f, 1.0f);
                rootBox.center = new Vector3(0f, (EnvelopeHeight + 0.5f) * 0.5f, 0f);
            }

            // ── The grow timer (plant → ~15 s scale-lerp → activate; relog-durable). ────
            go.AddComponent<AncientPortalTag>();

            Assets.RegisterPrefabInZNetScene(go);
            Plugin.Log.LogInfo(
                $"[Trailborne/Portals] Registered piece: {PortalPieceName} (Hammer, solid-earth, HP {PortalHealth}, " +
                "TeleportWorld + overhead trigger + grow timer).");
        }

        /// <summary>Build 3 thin tall legs from the scaled-down stubbe stump mesh, planted at
        /// the base around the center, holding the ring overhead. Placeholder transforms —
        /// flagged for AT-GEOMETRY in-game tuning.</summary>
        private static void BuildLegs(GameObject? stumpBlueprint, GameObject visualRoot)
        {
            if (stumpBlueprint == null) return;
            // stubbe mesh ≈ 9.8 (X) × 4.4 (Y) × 6.95 (Z). Thin it hard on X/Z and stretch Y to
            // make a ~3.5 m post ~0.6 m thick.
            var legScale = new Vector3(0.06f, 0.8f, 0.06f);
            const float radius = 1.2f;
            for (int i = 0; i < 3; i++)
            {
                var leg = Assets.GraftMeshFromBlueprint(stumpBlueprint, visualRoot, "SBPR_PortalLeg" + i, "cylinder");
                if (leg == null) continue;
                float ang = (Mathf.PI * 2f / 3f) * i;
                leg.transform.localPosition = new Vector3(Mathf.Cos(ang) * radius, 0f, Mathf.Sin(ang) * radius);
                leg.transform.localScale = legScale;
                leg.transform.localRotation = Quaternion.identity;
            }
        }

        /// <summary>Build 2 root tendrils from the Greydwarf_Root default mesh, weaving up near
        /// the legs toward the ring rim. Placeholder — flagged for the art pass.</summary>
        private static void BuildRoots(GameObject? rootBlueprint, GameObject visualRoot)
        {
            if (rootBlueprint == null) return;
            for (int i = 0; i < 2; i++)
            {
                var root = Assets.GraftMeshFromBlueprint(rootBlueprint, visualRoot, "SBPR_PortalRoot" + i, "default");
                if (root == null) continue;
                float side = (i == 0) ? 1f : -1f;
                root.transform.localPosition = new Vector3(side * 0.8f, 0.4f, 0f);
                root.transform.localScale = new Vector3(0.5f, 0.6f, 0.5f);
                root.transform.localRotation = Quaternion.Euler(0f, side > 0 ? 25f : -160f, side > 0 ? 12f : -12f);
            }
        }

        /// <summary>
        /// Build the overhead horizontal teleport trigger (spec §3.7 — the main novel-geometry
        /// risk). A child GameObject under the piece root with a flat trigger BoxCollider +
        /// <see cref="TeleportWorldTrigger"/>, positioned at the ring (~3 m up).
        ///
        /// 🔴 SIZE IS DESK-ESTIMATED, FLAGGED FOR IN-GAME TUNING (AT-JUMP-ACTIVATE): the box is
        /// ~2.6 m across (ring footprint) and ~0.9 m tall centered at the ring height, so a
        /// ~1 m jump apex (head reaching ~2.8–3 m) clips it while standing/walking under
        /// (head ~1.8 m) misses. These exact numbers depend on the player capsule + jump impulse
        /// and CANNOT be locked from the desk — the engineer tunes them on a joined client and
        /// Daniel verifies. If walk-under triggers, lower the box / raise its center; if a jump
        /// fails to register, grow the box vertically.
        /// </summary>
        private static void BuildOverheadTrigger(GameObject pieceRoot)
        {
            var trig = new GameObject("SBPR_PortalTrigger");
            trig.transform.SetParent(pieceRoot.transform, worldPositionStays: false);
            trig.transform.localPosition = new Vector3(0f, EnvelopeHeight, 0f);

            var box = trig.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(2.6f, 0.9f, 2.6f);   // wide ring footprint, ~0.9 m vertical slack
            box.center = Vector3.zero;                   // centered on the ring height (trig is at ~3 m)

            // TeleportWorldTrigger.Awake does GetComponentInParent<TeleportWorld>() — pieceRoot
            // holds the TeleportWorld, so this resolves. OnTriggerEnter → m_teleportWorld.Teleport.
            trig.AddComponent<TeleportWorldTrigger>();
            // NOTE: the grow timer (AncientPortalTag) starts this collider DISABLED and enables it
            // once at full grow, so a half-grown portal can't teleport (spec §3.6).
        }

        /// <summary>
        /// 🔴 #1 RISK (spec §1): add our portal prefab's stable hash to
        /// <c>Game.instance.PortalPrefabHash</c> so ZDOMan adds our portal ZDOs to its
        /// <c>m_portalObjects</c> tag-pairing set. Without it the portal places, grows, and
        /// locally-activates but NEVER connects to a same-tag twin — a SILENT logs-green failure.
        /// Idempotent (Contains-guarded) and null-Game-guarded; safe to call repeatedly. The
        /// vanilla list is built once and only ever Add'd to in Game.Awake (assembly_valheim:
        /// 84083-89), so our entry is never wiped.
        /// </summary>
        public static void EnsurePortalHashRegistered()
        {
            var game = Game.instance;
            if (game == null) return;   // Game not up yet — DoObjectDBWiring re-asserts later
            if (game.PortalPrefabHash == null) return;
            int hash = PortalPieceName.GetStableHashCode();
            if (!game.PortalPrefabHash.Contains(hash))
            {
                game.PortalPrefabHash.Add(hash);
                Plugin.Log.LogInfo(
                    $"[Trailborne/Portals] Registered '{PortalPieceName}' hash in Game.PortalPrefabHash " +
                    "(enables tag-pairing — the #1 silent-failure risk).");
            }
        }

        // ════════════════════════════════════════════════════════════════════════════
        // OBJECTDB WIRING — Seed recipe + authoritative portal cost rebuild + Hammer menu.
        // Runs AFTER Trailhead (the Explorer's Bench station must exist for the recipe).
        // ════════════════════════════════════════════════════════════════════════════

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            // Re-assert the portal hash in case Game.instance wasn't up at RegisterPrefabs time.
            EnsurePortalHashRegistered();

            // ── 1. Register the Seed item into ObjectDB, then its recipe. ───────────────
            var seedPrefab = zns?.GetPrefab(SeedItemName);
            if (seedPrefab != null) Assets.RegisterItemInObjectDB(seedPrefab);

            if (!RecipeHelpers.HasRecipe(SeedItemName))
            {
                var seedItem = odb.GetItemPrefab(SeedItemName);
                if (seedItem != null)
                {
                    var recipe = ScriptableObject.CreateInstance<Recipe>();
                    recipe.name = "Recipe_" + SeedItemName;
                    recipe.m_item = seedItem.GetComponent<ItemDrop>();
                    recipe.m_amount = 1;
                    recipe.m_minStationLevel = 1;
                    recipe.m_craftingStation = RecipeHelpers.FindStation(Trailhead.ExplorersBenchName);
                    recipe.m_resources = new[]
                    {
                        BuildReq(ResAncientSeed, SeedAncientSeedCost),
                        BuildReq(ResGreydwarfEye, SeedGreydwarfEyeCost),
                        BuildReq(ResSurtlingCore, SeedSurtlingCoreCost),
                    };
                    odb.m_recipes.Add(recipe);
                }
            }

            // ── 2. Rebuild the portal piece's build cost (the Seed resolves now) + add it
            //       to the HAMMER PieceTable (AddOrReplaceByName → no rejoin duplicate). ──
            var portalPrefab = zns?.GetPrefab(PortalPieceName);
            if (portalPrefab != null)
            {
                var piece = portalPrefab.GetComponent<Piece>();
                if (piece != null)
                {
                    piece.m_resources = new[] { BuildReq(SeedItemName, 1) };
                    piece.m_craftingStation = null;   // re-assert: no bench to place
                }
                var hammerTable = Assets.GetHammerPieceTable();
                if (hammerTable != null) Assets.AddOrReplacePieceByName(portalPrefab, hammerTable);
            }

            Plugin.Log.LogInfo(
                "[Trailborne/Portals] ObjectDB wiring complete (Portal Seed recipe @ Explorer's Bench " +
                "[AncientSeed×1 + GreydwarfEye×20 + SurtlingCore×2]; Ancient Portal cost=1 seed on the Hammer menu).");
        }

        private static Piece.Requirement BuildReq(string resourcePrefabName, int amount, bool warn = true)
        {
            return Assets.BuildReq(resourcePrefabName, amount, "Portals", warn);
        }
    }
}
