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
    /// Both built ADDITIVELY (ADR-0006: <see cref="Assets.TryConstructItemShell"/> /
    /// <see cref="Assets.TryConstructPieceShell"/> + mesh-reference grafts) — NEVER cloning
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
    ///      We ALSO wire m_proximityRoot + m_target_found (EffectFade) + m_connected together
    ///      so UpdatePortal (:122976-93) runs the vanilla proximity glow + "target found"
    ///      shimmer (issue 1, 2026-06-15) — see <see cref="WireProximityEffect"/>. All three
    ///      refs are set atomically (or none) so a missing donor degrades to no-effect, not NRE.
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

        // ── Vanilla TeleportWorld serialized values (read LIVE off portal_wood via
        //    `vprefab inspect portal_wood --json`, 2026-06-15 — NOT the class-initializer
        //    defaults, which differ). These drive "the same portal effects" Daniel asked for
        //    (issue 1): the emission glow lerps between these two HDR colors and the proximity
        //    shimmer reads within m_activationRange. Re-confirm after a game patch. ──────────
        //  • m_colorUnconnected = (0,0,0,1) — BLACK: no emission when the portal has no twin.
        //  • m_colorTargetfound = (5.0, 2.379, 0.0, 1) — HDR orange (>1 = bloom intensity):
        //    the lit-ring glow when connected. White→white (the class default) is INVISIBLE,
        //    which is the whole reason wiring m_model alone never showed a glow.
        //  • m_activationRange = 3 (the prefab overrides the 5f class default).
        //  • m_exitDistance = 1.
        private static readonly Color VanillaColorUnconnected = new Color(0f, 0f, 0f, 1f);
        private static readonly Color VanillaColorTargetFound = new Color(5.0f, 2.3793103f, 0f, 1f);
        private const float VanillaActivationRange = 3f;
        private const float VanillaExitDistance    = 1f;

        // Visual envelope target (~3 m tall × ~3 m wide; ring at the TOP). These are
        // DESK ESTIMATES for the placeholder kitbash — flagged for AT-GEOMETRY in-game
        // tuning (spec §3.2/§3.7). The grafted donor meshes are scaled into this envelope.
        private const float EnvelopeHeight = 3f;
        private const string IconFile = "portal_seed_v0.1.png";

        // ── Ring orientation (SHARED by the visual ring AND the grafted proximity/"target-
        //    found" effect so the two never drift — the effect must sit IN the ring's plane). ──
        //    The Ancient Portal ring lies FLAT (faces up) where the player jumps in, unlike
        //    portal_wood's UPRIGHT ring. The grafted donor "_target_found_red" subtree is at
        //    IDENTITY local rotation relative to its portal root (verified on the real prefab,
        //    t_bf2bb402), so a BARE OVERWRITE with this rotation — not a compose — drops the
        //    effect into the flat ring's plane (issue 1 follow-up).
        private static readonly Quaternion RingFlatRotation = Quaternion.Euler(90f, 0f, 0f); // lie flat (face up)

        // ── Leg geometry (SHARED by the visual legs AND their structural colliders so the
        //    two never drift — an axe swing must land where the leg is SEEN). 3 posts on a
        //    1.2 m-radius ring, 120° apart. DESK-ESTIMATED, flagged AT-GEOMETRY. ──────────
        private const int   LegCount  = 3;
        private const float LegRadius = 1.2f;
        // Per-leg SOLID structural post collider: ~0.5 m square footprint (≈ the visible
        // stubbe-leg cross-section) spanning ground → just past the ring (y ∈ [0, 3.5]).
        // Thin enough that the ~1.6 m gaps between the 3 posts let the player walk in and
        // stand under / jump into the overhead ring; tall+wide enough to be an easy axe
        // target. Replaces the old single centre slab that walled off ground-level approach.
        private static readonly Vector3 LegColliderSize = new Vector3(0.5f, EnvelopeHeight + 0.5f, 0.5f);

        // ── Donor blueprints (read-only, never cloned — ADR-0006) ───────────────────
        private const string DonorPortal = "portal_wood";       // ring (New/small_portal) + effect donor
        private const string DonorRoot   = "Greydwarf_Root";    // root tendrils (default mesh)
        private const string DonorStump  = "stubbe";            // legs (cylinder stump mesh)

        // ── portal_wood child-node names we read as blueprints (verified live via
        //    `vprefab inspect portal_wood`, 2026-06-15 — the proximity effect graft) ──────
        //  • "_target_found_red" — the EffectFade "target found" subtree (ParticleSystems +
        //    Light + AudioSource, NO ZNetView/collider) that TeleportWorld.m_target_found
        //    SetActive()-toggles when a teleportable player is near a CONNECTED portal.
        //  • "Proximity" — vanilla's empty m_proximityRoot anchor child (we build our own at
        //    the ring instead, but the name documents what we're reproducing).
        private const string DonorTargetFoundChild = "_target_found_red";

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
            if (!Assets.TryConstructItemShell(SeedItemName, out var go))
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
            if (!Assets.TryConstructPieceShell(PortalPieceName, DonorPortal, out var go))
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
                ring.transform.localRotation = RingFlatRotation;                 // lie flat (face up)
                ring.transform.localScale    = new Vector3(0.71f, 0.71f, 0.71f); // ~3 m wide
            }

            // Legs: 3 thin tall pillars (scaled-down stubbe stump) holding the ring overhead,
            // EACH with its own SOLID structural post collider on the piece root (built inside
            // BuildLegs) so the structure stays axe-hittable + deconstructable while the open
            // gaps between posts let the player walk in and stand under / jump into the ring.
            BuildLegs(stumpBlueprint, visualRoot, go);

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
            // Leave m_allowAllItems = false → the vanilla ore/metal ban holds with zero code (§3.5).

            // ── 🔴 #2b: wire the PROXIMITY / "target found" effect so the portal reads alive
            //    up close like vanilla portal_wood (issue 1, 2026-06-15). UpdatePortal (0.5 s,
            //    decomp :122976) early-returns while m_proximityRoot==null — which is exactly
            //    why the v1 deferral (both null) showed NO glow + NO shimmer. Once we set
            //    m_proximityRoot it dereferences TWO more refs every cycle:
            //      • m_target_found.SetActive(...)          (decomp :122992, every cycle)
            //      • m_connected.Create(...)                (decomp :122984, on connect-edge)
            //    BOTH are null on a fresh AddComponent<TeleportWorld>(), so wiring proximityRoot
            //    ALONE would just trade the old m_model NRE for a new one on first pairing. We
            //    wire all three atomically (or leave proximityRoot null and degrade cleanly). ──
            WireProximityEffect(go, teleport, portalBlueprint);

            // ── Overhead jump-through trigger (#3): a flat box collider at the ring, gated by
            //    the grow timer. Parented under the PIECE ROOT (sibling of the visual root) so
            //    its geometry is FIXED regardless of grow scale; TeleportWorldTrigger.Awake walks
            //    GetComponentInParent<TeleportWorld>() up to the piece root. The grow tag finds it
            //    via GetComponentInChildren<TeleportWorldTrigger> and toggles its collider. ──
            BuildOverheadTrigger(go);

            // ── ROOT collider: collapse the shell's unit box from a full-height CENTRE SLAB
            //    (the old 2.0 × 3.5 × 1.0 m wall that blocked ground-level approach / standing
            //    under the ring) down to a THIN GROUND PAD hugging the base. It still gives the
            //    base mass a hit/deconstruct target, but clears the central column from ~0.3 m
            //    upward so the player can walk straight in, stand directly under the overhead
            //    ring, and jump up into it. The 3 SOLID leg-post colliders (BuildLegs) carry the
            //    rest of the axe/deconstruct hit surface up the structure's height. SOLID
            //    (non-trigger) so it still reads as structure to WearNTear support + hits. ──────
            var rootBox = go.GetComponent<BoxCollider>();
            if (rootBox != null)
            {
                rootBox.size = new Vector3(2.0f, 0.3f, 2.0f);   // wide, ankle-low base pad
                rootBox.center = new Vector3(0f, 0.15f, 0f);     // sits on the ground, centre clear above
            }

            // ── The grow timer (plant → ~15 s scale-lerp → activate; relog-durable). ────
            go.AddComponent<AncientPortalTag>();

            Assets.RegisterPrefabInZNetScene(go);
            Plugin.Log.LogInfo(
                $"[Trailborne/Portals] Registered piece: {PortalPieceName} (Hammer, solid-earth, HP {PortalHealth}, " +
                "TeleportWorld + overhead trigger + grow timer).");
        }

        /// <summary>Build 3 thin tall legs from the scaled-down stubbe stump mesh, planted at
        /// the base around the center, holding the ring overhead, AND give each leg its own
        /// SOLID (non-trigger) structural post collider on the piece root so the structure is
        /// axe-hittable / deconstructable WITHOUT walling off the ground-level approach. The
        /// player walks between the ~1.6 m gaps to stand under / jump into the ring. Mesh
        /// transforms + collider placement DESK-ESTIMATED — flagged for AT-GEOMETRY in-game
        /// tuning.</summary>
        /// <param name="pieceRoot">The piece root (TeleportWorld/WearNTear host). Leg COLLIDERS
        /// parent here (fixed world geometry, like the overhead trigger), so an axe/deconstruct
        /// ray walking GetComponentInParent up from a post resolves the WearNTear/Piece. The
        /// collider must NOT live under the grow-scaled visual root or its size would lerp.</param>
        private static void BuildLegs(GameObject? stumpBlueprint, GameObject visualRoot, GameObject pieceRoot)
        {
            // stubbe mesh ≈ 9.8 (X) × 4.4 (Y) × 6.95 (Z). Thin it hard on X/Z and stretch Y to
            // make a ~3.5 m post ~0.6 m thick.
            var legScale = new Vector3(0.06f, 0.8f, 0.06f);
            for (int i = 0; i < LegCount; i++)
            {
                float ang = (Mathf.PI * 2f / LegCount) * i;
                var legPos = new Vector3(Mathf.Cos(ang) * LegRadius, 0f, Mathf.Sin(ang) * LegRadius);

                // (a) Structural post collider on the PIECE ROOT (fixed geometry — never
                //     grow-scaled). SOLID (isTrigger stays false) so it (1) is an axe/
                //     deconstruct hit target and (2) blocks walking THROUGH a post — while the
                //     gaps between posts stay open for the walk-up/stand-under path. Inherits
                //     the piece's "Default" layer (in the build remove-mask + WearNTear hit
                //     mask + character collision), so no explicit layer assignment is needed.
                var legCollider = new GameObject("SBPR_PortalLegCollider" + i);
                legCollider.transform.SetParent(pieceRoot.transform, worldPositionStays: false);
                legCollider.transform.localPosition = legPos;
                var legBox = legCollider.AddComponent<BoxCollider>();
                legBox.size = LegColliderSize;
                legBox.center = new Vector3(0f, LegColliderSize.y * 0.5f, 0f);   // base on the ground

                // (b) Visual leg mesh on the grow-scaled VISUAL root (cosmetic; placeholder).
                if (stumpBlueprint == null) continue;
                var leg = Assets.GraftMeshFromBlueprint(stumpBlueprint, visualRoot, "SBPR_PortalLeg" + i, "cylinder");
                if (leg == null) continue;
                leg.transform.localPosition = legPos;
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
        /// Wire the Ancient Portal's <see cref="TeleportWorld"/> to show the SAME proximity /
        /// "target found" effect vanilla <c>portal_wood</c> shows (issue 1, Daniel 2026-06-15).
        /// Promotes the v1-deferred cosmetic (spec §3.5) to required. ADDITIVE (ADR-0006): we
        /// graft the effect subtree off the <paramref name="portalBlueprint"/> read via
        /// GetPrefab (fires no Awake) — never clone portal_wood.
        ///
        /// 🔴 THE THREE REFS UpdatePortal DEREFERENCES once m_proximityRoot is non-null (decomp
        /// assembly_valheim:122976-93 — read clean-side per ADR-0001). They MUST be wired
        /// together or we trade the old NRE for a new one:
        ///   1. <c>m_proximityRoot</c> — the anchor whose position feeds
        ///      <c>Player.GetClosestPlayer(m_proximityRoot.position, m_activationRange)</c>
        ///      (:122980). A fresh child Transform at the ring height. Setting this is what
        ///      switches UpdatePortal ON (it early-returns while null, :122978).
        ///   2. <c>m_target_found</c> — the <see cref="EffectFade"/> SetActive()-toggled at
        ///      :122992 when a teleportable player is near a CONNECTED portal. Grafted from
        ///      portal_wood's <c>_target_found_red</c> subtree (ParticleSystems + Light +
        ///      AudioSource), which carries no ZNetView → ADR-0006-safe to Instantiate.
        ///   3. <c>m_connected</c> — the activation <see cref="EffectList"/> fired ONCE on the
        ///      unconnected→connected edge (:122984). Null on a fresh AddComponent, so we
        ///      VALUE-COPY it off the blueprint's own TeleportWorld (reference, not clone).
        /// Plus the two HDR colors + activation range so the emission lerp in
        /// <c>TeleportWorld.Update</c> (:122996, the m_model glow) actually has a visible
        /// gradient — vanilla's <c>m_colorUnconnected</c> is black and <c>m_colorTargetfound</c>
        /// is HDR orange; the class-default white→white is invisible.
        ///
        /// FAIL-SAFE: if the target-found graft fails (donor/child missing) we DO NOT set
        /// m_proximityRoot — leaving it null keeps UpdatePortal a guarded no-op (the old v1
        /// behaviour), so a missing donor degrades to "no proximity effect" rather than an NRE.
        /// m_model (the emission glow) is wired separately in the caller and is unaffected.
        /// </summary>
        private static void WireProximityEffect(GameObject pieceRoot, TeleportWorld teleport, GameObject? portalBlueprint)
        {
            // The two HDR colors + ranges drive the emission lerp + proximity test (read live
            // off portal_wood; see the Vanilla* constants). Safe to set unconditionally — they
            // only matter once UpdatePortal/Update run, and Update (m_model glow) already runs.
            teleport.m_colorUnconnected = VanillaColorUnconnected;
            teleport.m_colorTargetfound = VanillaColorTargetFound;
            teleport.m_activationRange  = VanillaActivationRange;
            teleport.m_exitDistance     = VanillaExitDistance;

            // (3) m_connected — value-copy the activation EffectList off the blueprint's own
            // TeleportWorld so the connect-edge Create() (:122984) has a real (possibly empty)
            // list to fire instead of NRE'ing. Reading an asset's field value is reference,
            // not inheritance (ADR-0006). Fall back to a fresh empty EffectList if the
            // blueprint or its TeleportWorld is somehow absent — Create() on an empty list is
            // a safe no-op, never null.
            var blueprintTw = portalBlueprint != null ? portalBlueprint.GetComponent<TeleportWorld>() : null;
            teleport.m_connected = blueprintTw?.m_connected ?? new EffectList();

            // (2) m_target_found — graft portal_wood's "_target_found_red" EffectFade subtree
            // onto the piece root (NOT the grow-scaled visual root, so the effect reads at a
            // FIXED world position like the trigger). Positioned at the ring height.
            if (Assets.TryGraftEffectSubtree(
                DonorPortal, DonorTargetFoundChild, pieceRoot, "SBPR_PortalTargetFound", out var targetFound))
            {
                // Anchor at the ring (~3 m up) so the shimmer reads at the overhead ring, where
                // a connected portal's effect belongs. DESK-ESTIMATED height — shares the
                // EnvelopeHeight the ring/trigger use; flagged for AT-GEOMETRY tuning alongside them.
                targetFound.transform.localPosition = new Vector3(0f, EnvelopeHeight, 0f);
                // Align the effect's plane to the FLAT ring (issue 1 follow-up). TryGraftEffectSubtree
                // copies the donor's local transform faithfully, and portal_wood's "_target_found_red"
                // child is at IDENTITY local rotation (verified, t_bf2bb402) — so without this it stays
                // in portal_wood's UPRIGHT plane and reads vertical, not in our flat ring. Donor being
                // identity means this is a bare OVERWRITE (same rotation the ring gets), not a compose.
                targetFound.transform.localRotation = RingFlatRotation;
                var fade = targetFound.GetComponent<EffectFade>();
                teleport.m_target_found = fade;

                // (1) m_proximityRoot — ONLY set when (2) succeeded, so UpdatePortal never
                // derefs a null m_target_found. A fresh anchor child at the ring height feeds
                // GetClosestPlayer; placed under the piece root (fixed, not grow-scaled).
                var prox = new GameObject("SBPR_PortalProximity");
                prox.transform.SetParent(pieceRoot.transform, worldPositionStays: false);
                prox.transform.localPosition = new Vector3(0f, EnvelopeHeight, 0f);
                teleport.m_proximityRoot = prox.transform;

                Plugin.Log.LogInfo(
                    "[Trailborne/Portals] Wired proximity effect (m_proximityRoot + m_target_found EffectFade + " +
                    "m_connected + HDR glow colors) — Ancient Portal now shows the vanilla portal_wood approach/" +
                    "connected effect (issue 1).");
            }
            else
            {
                // Graft failed → leave m_proximityRoot null so UpdatePortal stays a guarded
                // no-op (the v1 behaviour). The m_model emission glow set in the caller still
                // runs in Update(), but without proximityRoot there's no shimmer. Degraded,
                // not broken — and crucially NOT an NRE.
                Plugin.Log.LogWarning(
                    "[Trailborne/Portals] Could not graft portal_wood's '_target_found_red' effect subtree; " +
                    "leaving m_proximityRoot null (no proximity shimmer, no NRE). Check portal_wood availability/structure.");
            }
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
                if (Assets.TryGetHammerPieceTable(out var hammerTable)) Assets.AddOrReplacePieceByName(portalPrefab, hammerTable);
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
