// ============================================================================
//  Trailborne — Special Bedroll (piece_sbpr_bedroll)
// ----------------------------------------------------------------------------
//  The SLEEP half of the Trailside Camp triad (design: docs/design/trailside-camp.md,
//  impl-spec: docs/v2/planning/bear-hide-tent-triad-build-impl-spec.md §2). A
//  Spade-placed bedroll that — under the Bear Hide Tent canopy + a lit camp fire —
//  lets an Explorer skip the night out on the trail WITHOUT overwriting their home
//  respawn point.
//
//  ARCHITECTURE (verified against the decomp; full rationale in BedrollTag.cs)
//  --------------------------------------------------------------------------
//  Vanilla Bed.Interact (:99592) cannot skip the night without also calling
//  SetCustomSpawnPoint. So the E-press is owned by BedrollTag (an Interactable that
//  reimplements the 5-gate sleep chain and drives AttachStart(isBed:true) directly).
//  A co-located vanilla Bed component is kept as the spawn-anchor / structural bed
//  identity, but is NOT the interaction entrypoint. The prefab-gated
//  BedrollCheckExposurePatch relaxes Bed.CheckExposure's 0.8-cover clause for our
//  prefab only (belt-and-braces + regression-safe for vanilla beds).
//
//  Construction is ADDITIVE (ADR-0006): TryConstructPieceShell builds the
//  ZNetView+Piece+WearNTear+collider skeleton; the fur mesh is grafted as a
//  ZNetView-free cosmetic child read off the vanilla `bed` blueprint (mesh-reference,
//  never Instantiate-the-networked-prefab). Seat is MEASURED (MeasureLocalFootY), not
//  hand-guessed (the un-measured-seat defect the collider-fit sibling spec calls out).
//
//  Comfort: free vanilla SE_Rested rides the skip-wake (Q7 — Inspired deferred to the
//  beautification graduation; zero code here). All gated behind ServerContext.OnSBServer
//  via Registrar.
// ============================================================================

using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Camp
{
    public static class Bedroll
    {
        // LOCKED prefab name — a save/wire contract the moment a bedroll is placed; never
        // rename (renaming orphans every placed instance).
        public const string BedrollName = "piece_sbpr_bedroll";

        // Clean donor for the shell's effect-table reference-copy (place/hit/destroy SFX).
        // Wood build, matches a fur-on-frame bedroll. `wood_floor` is a clean structural
        // wood donor with no gameplay components to leak.
        private const string ShellEffectDonor = "wood_floor";

        // Visual blueprint: the vanilla `bed` prefab (a real ZNetScene build piece, present
        // in the serialized prefab list — confirmed via Jotunn prefab-list.md). Read for its
        // fur/frame mesh only; never instantiated as a networked object.
        private const string VisualBlueprint = "bed";

        // Build cost — Black-Forest band, mirrors the tent (PROVISIONAL pending Daniel's
        // recipe lock). A bedroll is hide + lashings: less frame than the tent.
        public const int BjornHideCost     = 2;   // the fur you lie on
        public const int LeatherScrapsCost = 3;   // ties/edging
        public const int WoodCost          = 4;   // the roll frame

        // Wood-tier field furniture HP — survives weather between visits, lighter than the
        // tent's 600 (it's a bedroll, not a structure). Tunable playtest polish.
        private const float BedrollHealth = 200f;

        // ───────────────────────────────────────────────
        // PREFAB REGISTRATION (ZNetScene.Awake postfix, via Registrar)
        // ───────────────────────────────────────────────

        public static void RegisterPrefabs(ZNetScene zns)
        {
            if (zns.GetPrefab(BedrollName) != null) return;

            if (!Assets.TryConstructPieceShell(BedrollName, ShellEffectDonor, out var go))
            {
                Plugin.Log.LogWarning($"[Trailborne/Camp] Could not construct piece shell for {BedrollName}; skipping.");
                return;
            }

            var wnt = go.GetComponent<WearNTear>();
            if (wnt != null)
            {
                wnt.m_health = BedrollHealth;
                wnt.m_materialType = WearNTear.MaterialType.Wood;
                wnt.m_burnable = false;   // a placed bedroll shouldn't burn away by the camp fire
            }

            var piece = go.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_name = "Bedroll";
                piece.m_description =
                    "A trailside bedroll of stretched hide. Under a tent canopy beside a lit " +
                    "camp fire you can skip the night here — a trail nap that does NOT change " +
                    "your home spawn. (Placeholder art.)";
                // MUST be Misc: the spade's from-scratch PieceTable declares only the single
                // Misc-backed 'Trail' tab; a non-Misc piece renders in no tab (the v0.2.2
                // cairn-vanish bug).
                piece.m_category = Piece.PieceCategory.Misc;
                // Spade-placed, no station-proximity gate (Pillar 1, Q3).
                piece.m_craftingStation = null;
                piece.m_resources = BuildResources();
                // Comfort is the wake event (vanilla SE_Rested), not a per-piece aura.
                piece.m_comfort = 0;
                piece.m_comfortGroup = Piece.ComfortGroup.None;
            }

            // ── Spawn-point anchor: the vanilla Bed reads m_spawnPoint.position for its
            //    exposure test + as the attach transform. Build a dedicated child at the
            //    resting height so AttachStart seats the player on the roll, and so the
            //    Cover.GetCoverForPoint sample is taken at the sleeper's position (under the
            //    canopy) rather than the piece origin. ──
            var spawnAnchor = new GameObject("SBPR_BedrollSpawnPoint");
            spawnAnchor.transform.SetParent(go.transform, worldPositionStays: false);
            spawnAnchor.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            spawnAnchor.transform.localRotation = Quaternion.identity;

            // ── BedrollTag: MUST be added BEFORE the Bed component so it is the
            //    FIRST Interactable on the GameObject. Unity's GetComponentInParent<Interactable>
            //    (Player.FindHoverObject :19276) returns components in ADD ORDER, so the E-press
            //    resolves to BedrollTag — which reimplements the sleep chain + drives the
            //    no-spawn night skip. If Bed were added first, vanilla Bed.Interact would win
            //    and always claim spawn (the bug this whole design exists to avoid). BedrollTag
            //    reads its sibling Bed via GetComponent in Awake, so the Bed added just below is
            //    still wired as its spawn anchor. ──
            go.AddComponent<BedrollTag>();

            // ── Vanilla Bed component: spawn-anchor + structural "this is a bed" identity for
            //    the all-asleep vote. NOT the interaction owner (BedrollTag, added above, is). ──
            var bed = go.AddComponent<Bed>();
            bed.m_spawnPoint = spawnAnchor.transform;

            // ── Cosmetic fur mesh: grafted off the vanilla bed blueprint, seated flush
            //    (foot at y=0) via a MEASURED offset — not a hand-guessed Y. Failure is
            //    non-fatal: the piece still registers/builds, just shows no fur this load. ──
            AttachBedrollVisual(zns, go);

            // Root collider: a low, bed-footprint box (the vanilla single bed is ~2.0 x 0.3
            // x 1.0). Big enough to receive placement raycasts + the E ray.
            var box = go.GetComponent<BoxCollider>();
            if (box != null) { box.size = new Vector3(2.0f, 0.4f, 1.0f); box.center = new Vector3(0f, 0.2f, 0f); }

            Assets.RegisterPrefabInZNetScene(go);
            Plugin.Log.LogInfo($"[Trailborne/Camp] Registered Bedroll piece: {BedrollName} (additive; BedrollTag owns the no-spawn night-skip).");
        }

        // ───────────────────────────────────────────────
        // OBJECTDB WIRING — rebuild resources (vanilla items resolve at this phase)
        // ───────────────────────────────────────────────

        public static void DoObjectDBWiring(ZNetScene zns)
        {
            var odb = ObjectDB.instance;
            if (odb == null) return;

            var p = zns?.GetPrefab(BedrollName);
            if (p == null) return;

            var piece = p.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_resources = BuildResources();
                piece.m_craftingStation = null;
            }
            // Added to the SPADE PieceTable in Trailblazing.DoObjectDBWiring (never Hammer).
            Plugin.Log.LogInfo("[Trailborne/Camp] Bedroll ObjectDB wiring complete (placed via Spade menu; no bench-in-range).");
        }

        // ───────────────────────────────────────────────
        // Visual: graft the vanilla bed fur mesh as a cosmetic child, seated flush
        // ───────────────────────────────────────────────

        private static void AttachBedrollVisual(ZNetScene zns, GameObject go)
        {
            var blueprint = zns.GetPrefab(VisualBlueprint);
            if (blueprint == null)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/Camp] {BedrollName}: visual blueprint '{VisualBlueprint}' not found; " +
                    "bedroll will register and build but show no fur this load.");
                return;
            }

            var visual = Assets.GraftMeshFromBlueprint(blueprint, go, "SBPR_BedrollVisual");
            if (visual == null)
            {
                Plugin.Log.LogWarning(
                    $"[Trailborne/Camp] {BedrollName}: fur mesh graft failed; the piece builds but shows no fur this load.");
                return;
            }

            // Seat flush: measure the grafted mesh's lowest point in root-local space and
            // lift the visual so its foot sits at y=0 (same discipline Signs/MarkerSigns use;
            // avoids the un-measured-seat defect the collider-fit spec flags on the tent).
            float footY = Assets.MeasureLocalFootY(go);
            if (Mathf.Abs(footY) > 0.001f)
            {
                var lp = visual.transform.localPosition;
                visual.transform.localPosition = new Vector3(lp.x, lp.y - footY, lp.z);
            }
        }

        private static Piece.Requirement[] BuildResources()
        {
            return new[]
            {
                Assets.BuildReq("BjornHide",     BjornHideCost,     "Camp"),
                Assets.BuildReq("LeatherScraps", LeatherScrapsCost, "Camp"),
                Assets.BuildReq("Wood",          WoodCost,          "Camp"),
            };
        }
    }
}
