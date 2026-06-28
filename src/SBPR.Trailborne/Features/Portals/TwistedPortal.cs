using System.Collections.Generic;
using UnityEngine;
using SBPR.Trailborne.Runtime;
using SBPR.Trailborne.Features.Sunstone;

namespace SBPR.Trailborne.Features.Portals
{
    // `MarkerSigns` is both a NAMESPACE and a TYPE; from inside this sibling Features.*
    // namespace the bare `MarkerSigns` binds to the namespace. Alias the TYPE so
    // `MarkerSigns.EyeResource` reads cleanly (the Portals.cs / Cairns pattern).
    using MarkerSigns = SBPR.Trailborne.Features.MarkerSigns.MarkerSigns;

    /// <summary>
    /// v3 Swamp content — the <b>Twisted Portal</b>: the endgame "no-restriction" portal
    /// that teleports even where vanilla portals are blocked (<c>GlobalKeys.NoPortals</c>),
    /// addressed by player-assigned RUNE NAMES, paired server-side on a dedicated
    /// <c>sbpr_rune_name</c> ZDO slot (NOT vanilla's <c>s_tag</c>).
    ///
    /// 🔄 This is card C1 (t_2b388cd5) — the portal MECHANISM. The travel-cost model is
    /// FOOD-AS-FUEL (no key trinket), built by the sibling cost-model card C2 (t_6e992a30).
    /// This file exposes the teleport seam (<see cref="SBPR_TwistedPortal.Teleport"/> →
    /// <see cref="TwistedPortalEnergy.TrySpendForJump"/>); it does NOT implement PE math.
    ///
    /// Impl spec:   docs/v3/planning/twisted-portal-impl-spec.md  (reconciled food-as-fuel)
    /// Design lock: docs/design/nomap.md §7 + docs/design/twisted-portal-food-charge.md
    ///
    /// ════════════════════════════════════════════════════════════════════════════════
    /// THE THREE THINGS THAT MAKE THIS DISTINCT FROM THE ANCIENT PORTAL (spec §3, §4):
    ///   1. A DISTINCT CLASS <see cref="SBPR_TwistedPortal"/> — it does NOT inherit
    ///      vanilla <see cref="TeleportWorld"/> (card AC#1: tag-collision avoidance). It
    ///      reimplements the small slice of teleport it needs, omitting the NoPortals gate.
    ///   2. NoPortals BYPASS (card AC#2) — our <see cref="SBPR_TwistedPortal.CommitTravel"/>
    ///      simply does NOT check <c>ZoneSystem.GetGlobalKey(GlobalKeys.NoPortals)</c>
    ///      (vanilla TeleportWorld.Teleport hard-blocks on it, decomp :123008). We are
    ///      independent code, so we just don't write that check.
    ///   3. LOOK-TO-AIM TRAVEL (Q3 superseded 2026-06-27, card t_f4d0d5e1 / L1) — the
    ///      destination is the portal the player AIMS the crosshair at (angular pick,
    ///      <see cref="AimPickMath"/>), committed on tap-[Use]/E (<see cref="TwistedPortalCommitInput"/>),
    ///      NOT a same-rune name match (Model A, RETIRED) and NOT vanilla's 1:1
    ///      ConnectionType.Portal channel. Rune names survive as human-readable AIM LABELS,
    ///      not the pairing key. We deliberately keep our hash OUT of <c>Game.PortalPrefabHash</c>
    ///      (the §4.3 option-(b) decision, see below) so vanilla <c>Game.ConnectPortals</c>
    ///      never touches our portals (AT-NO-VANILLA-PAIR passes by construction).
    ///
    /// 🔴 §4.3 DECISION — option (b), NOT (a). The spec flagged the #1 build-time choice:
    /// register our hash in <c>Game.PortalPrefabHash</c> and play <c>s_tag</c> games to
    /// stop vanilla auto-pairing (a), OR keep our hash out entirely and resolve the
    /// candidate set in our own ZDO walk (b). We chose (b): it fully decouples us from
    /// vanilla's portal-pairing machinery, never writes <c>s_tag</c>, and makes the
    /// spurious-vanilla-pair failure mode (AT-NO-VANILLA-PAIR) impossible rather than
    /// merely tested. The cost is doing our own <c>ZDOMan.GetAllZDOsWithPrefabIterative</c>
    /// walk (now in <see cref="TwistedPortalCandidates"/>) instead of reusing the engine's
    /// <c>m_portalObjects</c> list — cheap and explicit. (Architect lean in spec §4.3 was (b).)
    ///
    /// 🔴 MULTIPLAYER (spec §2 — look-to-aim makes server-authoritative reach REQUIRED): the
    /// candidate-set walk in <see cref="TwistedPortalCandidates.Gather"/> runs on the LOCAL
    /// CLIENT and currently sees only the ZDOs THIS PEER HOLDS — on a dedicated server, the
    /// ~64–128 m sector window, NOT a guaranteed 300 m. That is the L1 STAGING state (enough
    /// for the in-game aim/feel accept). L2 (card t_ccb454f8) MUST swap that walk for the
    /// owner-routed RPC candidate set (the <c>SurveyorTableTag</c> precedent) so travel reaches
    /// destinations past the client window (AT-PICK-LONGRANGE). The old "runs on the OWNER /
    /// not subject to the sector window" claim that used to sit here was FALSE — there is no
    /// RPC in the path yet; this comment states the real (client-window-limited) state.
    ///
    /// Q1 = COEXIST (locked): vanilla + Ancient portals stay craftable; we disable nothing.
    /// The cost model + travel are patch-free (component wiring + an on-demand ZDO read); the
    /// only Harmony patch in the look-to-aim surface is the client-only commit-input
    /// <c>Player.Update</c> postfix (<see cref="TwistedPortalCommitInput"/>). All registration
    /// gated behind ServerContext.OnSBServer via the Registrar fan-out.
    /// </summary>
    public static class TwistedPortal
    {
        // ── Prefab-name string contract (save/wire — LOCK here, never rename) ───────────
        public const string PortalPieceName = "piece_sbpr_twisted_portal";

        // The visual kitbash parent (a DIRECT child). No grow lifecycle (spec §4.6), so —
        // unlike the Ancient Portal — nothing scale-lerps this; it is just the art root.
        public const string VisualRootName = "SBPR_TwistedPortalVisual";

        // ── Portal piece recipe (LOCKED — SpecCheck row; spec §0 / §4.7 Q1 = coexist) ──
        //   FineWood ×20 + GreydwarfEye ×10 + SurtlingCore ×4 + SBPR_Sunstone ×1, Hammer-placed.
        //   GreydwarfEye via MarkerSigns.EyeResource and Sunstone via SunstoneLens.SunstoneName
        //   (referenced through the consts so a rename can't drift the recipe — SpecCheck is the
        //   drift backstop). FineWood / SurtlingCore are plain vanilla portal-family mats.
        private const string ResFineWood     = "FineWood";
        private const string ResGreydwarfEye = MarkerSigns.EyeResource;        // "GreydwarfEye"
        private const string ResSurtlingCore = "SurtlingCore";
        private static string ResSunstone => SunstoneLens.SunstoneName;        // "SBPR_Sunstone"
        public const int PortalFineWoodCost     = 20;
        public const int PortalGreydwarfEyeCost = 10;
        public const int PortalSurtlingCoreCost = 4;
        public const int PortalSunstoneCost     = 1;

        // ── Portal piece (LOCKED — spec §4.2: reuse the Ancient Portal's placement choices) ──
        public const float PortalHealth = 300f;   // match the Ancient Portal default (tunable, §4.2)

        // ── Visual envelope (DESK-ESTIMATED placeholder kitbash — flagged AT-GEOMETRY for
        //    in-game tuning, spec §4.1). Reuses the Ancient Portal's ~3 m envelope so the two
        //    read as the same class of object, re-tinted to "twisted/swamp" (§4.1 art-pass). ──
        private const float EnvelopeHeight = 3f;

        // Swamp re-tint (spec §4.1): a darker, sicklier emission so a Twisted Portal is
        // distinguishable from the Ancient Portal at a glance. Multiplies the grafted ring's
        // shared material color via a per-renderer MaterialPropertyBlock (never sharedMaterial —
        // the painted-sign lesson, t_f3310406). Placeholder tint, flagged for the art pass.
        private static readonly Color SwampTint = new Color(0.45f, 0.62f, 0.40f, 1f);

        // ── Ring orientation (SHARED so visual + trigger never drift). The ring lies FLAT
        //    (faces up) where the player jumps in — the Ancient Portal precedent. ───────────
        private static readonly Quaternion RingFlatRotation = Quaternion.Euler(90f, 0f, 0f);

        // ── Leg geometry (3 posts on a 1.2 m ring, 120° apart) — the Ancient Portal precedent,
        //    DESK-ESTIMATED, flagged AT-GEOMETRY. Each post gets a SOLID structural collider on
        //    the piece root so the structure is axe-hittable while the gaps let the player walk
        //    in / stand under / jump into the overhead ring. ──────────────────────────────────
        private const int   LegCount  = 3;
        private const float LegRadius = 1.2f;
        private static readonly Vector3 LegColliderSize = new Vector3(0.5f, EnvelopeHeight + 0.5f, 0.5f);

        // ── Donor blueprints (read-only, never cloned — ADR-0006) ───────────────────────────
        private const string DonorPortal = "portal_wood";    // ring (small_portal mesh) + effect donor
        private const string DonorRoot   = "Greydwarf_Root"; // root tendrils (default mesh)
        private const string DonorStump  = "stubbe";         // legs (cylinder stump mesh)

        // Build-menu thumbnail. There is no dedicated Twisted icon yet (the key is gone, so no
        // item icon ships with this feature); a piece m_icon is non-fatal when absent (no
        // thumbnail). Reuse the Ancient Portal seed icon as a placeholder so the Hammer entry
        // isn't blank — flagged for the art pass.
        private const string IconFile = "portal_seed_v0.1.png";

        // ════════════════════════════════════════════════════════════════════════════════
        // PREFAB REGISTRATION (ZNetScene.Awake postfix, via Registrar)
        // ════════════════════════════════════════════════════════════════════════════════

        public static void RegisterPrefabs(ZNetScene zns)
        {
            RegisterPortalPiece(zns);
            // NOTE (spec §4.3 option b): we deliberately do NOT register our hash into
            // Game.PortalPrefabHash. Pairing is resolved in our own server-side ZDO walk
            // (ResolveDestination), so vanilla Game.ConnectPortals never sees our portals and
            // can never form a spurious ConnectionType.Portal link (AT-NO-VANILLA-PAIR).
        }

        private static void RegisterPortalPiece(ZNetScene zns)
        {
            if (zns.GetPrefab(PortalPieceName) != null) return;

            // ADR-0006 additive piece shell: ZNetView + Piece + WearNTear + root BoxCollider,
            // hit/destroy/place effects reference-copied off the portal_wood blueprint (read via
            // GetPrefab — fires no Awake) so it sounds like a wooden/organic build. NO Instantiate
            // of portal_wood (the cairn-soft-lock / GuidePoint bug class).
            if (!Assets.TryConstructPieceShell(PortalPieceName, DonorPortal, out var go))
            {
                Plugin.Log.LogWarning("[Trailborne/TwistedPortal] Could not construct Twisted Portal piece shell; skipping.");
                return;
            }

            // ── Visual root (the art parent — a DIRECT child named VisualRootName). No grow
            //    lifecycle scales it (spec §4.6); it is just a grouping node for the kitbash. ──
            var visualRoot = new GameObject(VisualRootName);
            visualRoot.transform.SetParent(go.transform, worldPositionStays: false);

            // ── Grafted kitbash (all mesh-reference, ZNetView-free; placeholder transforms
            //    DESK-ESTIMATED for the ~3 m envelope, FLAGGED for AT-GEOMETRY tuning). ────────
            var portalBlueprint = zns.GetPrefab(DonorPortal);
            var rootBlueprint   = zns.GetPrefab(DonorRoot);
            var stumpBlueprint  = zns.GetPrefab(DonorStump);

            // Ring/glow on TOP (~3 m up), lying FLAT (face up) — the player jumps up into it,
            // re-tinted to swamp so it reads distinct from the Ancient Portal (spec §4.1).
            var ring = Assets.GraftMeshFromBlueprint(portalBlueprint, visualRoot, "SBPR_TwistedPortalRing", "small_portal");
            if (ring != null)
            {
                ring.transform.localPosition = new Vector3(0f, EnvelopeHeight, 0f);
                ring.transform.localRotation = RingFlatRotation;                 // lie flat (face up)
                ring.transform.localScale    = new Vector3(0.71f, 0.71f, 0.71f); // ~3 m wide
                ApplySwampTint(ring);
            }

            // Legs: 3 thin tall pillars holding the ring overhead, each with its own SOLID
            // structural collider on the piece root (built in BuildLegs) — the Ancient precedent.
            BuildLegs(stumpBlueprint, visualRoot, go);

            // Roots: a couple of tendrils weaving up toward the ring (placeholder; art pass).
            BuildRoots(rootBlueprint, visualRoot);

            // ── WearNTear: organic wood, HP 300, no rain decay (spec §4.2) ──────────────────
            var wnt = go.GetComponent<WearNTear>();
            if (wnt != null)
            {
                wnt.m_materialType = WearNTear.MaterialType.Wood;  // axe/fire, not the shell's Stone default
                wnt.m_health = PortalHealth;                        // 300 (match Ancient; tunable §4.2)
                wnt.m_noRoofWear = true;                            // no rain decay (Ancient precedent)
                wnt.m_burnable = false;                             // don't let it burn away on its own
                // m_canBeRemoved stays true (set by the shell) → deconstruct refunds the materials.
            }

            // ── Piece: Hammer-placed, no station, SOLID-EARTH only (spec §4.2) ──────────────
            var piece = go.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name = "Twisted Portal";
                piece.m_description =
                    "An endgame portal woven from twisted swamp-magic. Place it on solid earth with the " +
                    "Hammer, then stand on it and aim at another Twisted Portal in the world to travel — " +
                    "it works even where ordinary portals are sealed. " +
                    "Travel burns the food in your belly; a long jump leaves you depleted. " +
                    // Lore breadcrumb (spec §5.7 — the ONLY advertisement for the Bukeberry emergency reserve).
                    // Plain English, NOT a $sbpr_* token: this repo has no localization-registration layer, so a
                    // custom token would leak on-screen as a literal "[sbpr_...]" (the SBPR_TwistedPortal /
                    // SurveyorTableTag center-message precedent). Kept evocative + NON-EXPLICIT (no numbers, no
                    // "10 berries = 300 m") so the reserve stays a whispered, discover-by-experiment mechanic.
                    "The greydwarves are said to weave these same magicks with the foul berries their shamans " +
                    "hoard, though none can say quite how.";
                piece.m_category = Piece.PieceCategory.Misc;        // Hammer 'Misc' tab
                piece.m_craftingStation = null;                     // NO bench-in-range to place (§4.2)
                piece.m_groundOnly = true;                          // solid earth only, not on structures (§4.2)
                // Build cost is rebuilt authoritatively in DoObjectDBWiring (SBPR_Sunstone isn't
                // in ObjectDB yet at this prefab-build phase). Seed it now so the prefab is never
                // resource-less; warn=false because the ODB-phase rebuild is the authoritative pass.
                piece.m_resources = new[]
                {
                    BuildReq(ResFineWood, PortalFineWoodCost, warn: false),
                    BuildReq(ResGreydwarfEye, PortalGreydwarfEyeCost, warn: false),
                    BuildReq(ResSurtlingCore, PortalSurtlingCoreCost, warn: false),
                    BuildReq(ResSunstone, PortalSunstoneCost, warn: false),
                };
                var icon = Assets.LoadPngAsSprite(IconFile);
                if (icon != null) piece.m_icon = icon;
            }

            // ── The portal BRAIN: our distinct teleporter class (NO TeleportWorld). It owns the
            //    look-to-aim CommitTravel (NoPortals omitted), the rune-name ZDO discipline, and the
            //    Hoverable/Interactable/TextReceiver surfaces. Travel is the aim+tap-E commit owned by
            //    the client-only TwistedPortalCommitInput Player.Update postfix — there is NO overhead
            //    jump-through trigger under look-to-aim (the L1 supersession retired it, spec §4.5). ──
            go.AddComponent<SBPR_TwistedPortal>();

            // ── ROOT collider: collapse the shell's unit box to a THIN GROUND PAD so the player can
            //    walk onto / stand on the portal foundation (the Beat-1 proximity-active "stand on it"
            //    state). The 3 SOLID leg colliders carry the axe/deconstruct hit surface up the
            //    structure (Ancient precedent). No overhead trigger box — travel is aim+E, not jump.
            var rootBox = go.GetComponent<BoxCollider>();
            if (rootBox != null)
            {
                rootBox.size = new Vector3(2.0f, 0.3f, 2.0f);   // wide, ankle-low base pad
                rootBox.center = new Vector3(0f, 0.15f, 0f);     // sits on the ground, centre clear above
            }

            Assets.RegisterPrefabInZNetScene(go);
            Plugin.Log.LogInfo(
                $"[Trailborne/TwistedPortal] Registered piece: {PortalPieceName} (Hammer, solid-earth, HP {PortalHealth}, " +
                "distinct SBPR_TwistedPortal teleporter; NoPortals-bypass; look-to-aim travel (aim + tap-E commit)).");
        }

        /// <summary>Re-tint a grafted renderer toward the swamp emission via a per-renderer
        /// MaterialPropertyBlock (NOT sharedMaterial.SetColor — that bleeds into every portal_wood
        /// in the world; the painted-sign lesson, t_f3310406). Placeholder tint, flagged for the
        /// art pass (AT-GEOMETRY).</summary>
        private static void ApplySwampTint(GameObject graft)
        {
            var mr = graft.GetComponent<MeshRenderer>();
            if (mr == null) return;
            var mpb = new MaterialPropertyBlock();
            mr.GetPropertyBlock(mpb);
            // Vanilla portal materials key emission on _EmissionColor; tint both _Color and the
            // emission so the swamp read survives whichever the shader honours. Harmless if a
            // property is absent (MPB just carries an unused value).
            mpb.SetColor("_Color", SwampTint);
            mpb.SetColor("_EmissionColor", SwampTint);
            mr.SetPropertyBlock(mpb);
        }

        /// <summary>Build 3 thin tall legs (scaled stubbe stump) holding the ring overhead, each
        /// with a SOLID structural post collider on the PIECE ROOT (fixed geometry — axe-hittable /
        /// deconstructable) while the gaps stay open for the walk-up/stand-under path. The exact
        /// transforms are DESK-ESTIMATED, flagged AT-GEOMETRY. Mirrors Portals.BuildLegs.</summary>
        private static void BuildLegs(GameObject? stumpBlueprint, GameObject visualRoot, GameObject pieceRoot)
        {
            var legScale = new Vector3(0.06f, 0.8f, 0.06f);
            for (int i = 0; i < LegCount; i++)
            {
                float ang = (Mathf.PI * 2f / LegCount) * i;
                var legPos = new Vector3(Mathf.Cos(ang) * LegRadius, 0f, Mathf.Sin(ang) * LegRadius);

                // (a) Structural post collider on the PIECE ROOT (SOLID; inherits the piece's
                //     Default layer → in the build-remove mask + WearNTear hit mask + character
                //     collision, so no explicit layer assignment is needed).
                var legCollider = new GameObject("SBPR_TwistedPortalLegCollider" + i);
                legCollider.transform.SetParent(pieceRoot.transform, worldPositionStays: false);
                legCollider.transform.localPosition = legPos;
                var legBox = legCollider.AddComponent<BoxCollider>();
                legBox.size = LegColliderSize;
                legBox.center = new Vector3(0f, LegColliderSize.y * 0.5f, 0f);   // base on the ground

                // (b) Visual leg mesh on the visual root (cosmetic; placeholder).
                if (stumpBlueprint == null) continue;
                var leg = Assets.GraftMeshFromBlueprint(stumpBlueprint, visualRoot, "SBPR_TwistedPortalLeg" + i, "cylinder");
                if (leg == null) continue;
                leg.transform.localPosition = legPos;
                leg.transform.localScale = legScale;
                leg.transform.localRotation = Quaternion.identity;
                ApplySwampTint(leg);
            }
        }

        /// <summary>Build 2 root tendrils (Greydwarf_Root default mesh) weaving up near the legs
        /// toward the ring rim. Placeholder — flagged for the art pass. Mirrors Portals.BuildRoots.</summary>
        private static void BuildRoots(GameObject? rootBlueprint, GameObject visualRoot)
        {
            if (rootBlueprint == null) return;
            for (int i = 0; i < 2; i++)
            {
                var root = Assets.GraftMeshFromBlueprint(rootBlueprint, visualRoot, "SBPR_TwistedPortalRoot" + i, "default");
                if (root == null) continue;
                float side = (i == 0) ? 1f : -1f;
                root.transform.localPosition = new Vector3(side * 0.8f, 0.4f, 0f);
                root.transform.localScale = new Vector3(0.5f, 0.6f, 0.5f);
                root.transform.localRotation = Quaternion.Euler(0f, side > 0 ? 25f : -160f, side > 0 ? 12f : -12f);
                ApplySwampTint(root);
            }
        }

        // ════════════════════════════════════════════════════════════════════════════════
        // OBJECTDB WIRING — authoritative portal cost rebuild + Hammer menu add.
        // Runs AFTER SunstoneLens (so SBPR_Sunstone is in ObjectDB for the recipe BuildReq).
        // ════════════════════════════════════════════════════════════════════════════════

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            // Rebuild the portal piece's build cost (SBPR_Sunstone resolves now) + add it to the
            // HAMMER PieceTable (AddOrReplaceByName → no rejoin duplicate). No item recipe — the
            // Twisted Portal is a Hammer-placed piece with no key/material item of its own.
            var portalPrefab = zns?.GetPrefab(PortalPieceName);
            if (portalPrefab != null)
            {
                var piece = portalPrefab.GetComponent<Piece>();
                if (piece != null)
                {
                    piece.m_resources = new[]
                    {
                        BuildReq(ResFineWood, PortalFineWoodCost),
                        BuildReq(ResGreydwarfEye, PortalGreydwarfEyeCost),
                        BuildReq(ResSurtlingCore, PortalSurtlingCoreCost),
                        BuildReq(ResSunstone, PortalSunstoneCost),
                    };
                    piece.m_craftingStation = null;   // re-assert: no bench to place
                }
                if (Assets.TryGetHammerPieceTable(out var hammerTable)) Assets.AddOrReplacePieceByName(portalPrefab, hammerTable);
            }

            Plugin.Log.LogInfo(
                "[Trailborne/TwistedPortal] ObjectDB wiring complete (Twisted Portal cost = " +
                $"FineWood×{PortalFineWoodCost} + GreydwarfEye×{PortalGreydwarfEyeCost} + " +
                $"SurtlingCore×{PortalSurtlingCoreCost} + Sunstone×{PortalSunstoneCost} on the Hammer menu).");
        }

        private static Piece.Requirement BuildReq(string resourcePrefabName, int amount, bool warn = true)
        {
            return Assets.BuildReq(resourcePrefabName, amount, "TwistedPortal", warn);
        }
    }
}
