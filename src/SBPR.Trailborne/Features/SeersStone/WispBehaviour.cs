// ============================================================================
//  Trailborne v4 (Mountains) — Seer's Stone WISP behaviour (engine-side MonoBehaviour)
// ----------------------------------------------------------------------------
//  Design : docs/design/seers-stone.md §5/§6. Drives ONE wisp's transform each frame
//           off the engine-free WispMotion helix (with the ground-aware-Y read), AND —
//           since the 2026-06-27 re-lock — IS the pin gesture: the wisp is a vanilla
//           Hoverable+Interactable, so walking up and pressing Use (E) drops the map pin
//           and dims the wisp. No camera raycast, no Player.Update patch — vanilla's own
//           FindHoverObject/Interact pipeline renders the [E] prompt and dispatches Use.
//
//  WHY THE WISP IS THE INTERACTABLE (architect decomp proof, Player.cs):
//    • FindHoverObject :3943 reads the Hoverable via collider.GetComponent (NOT InParent)
//      → the Hoverable (this) MUST sit on the SAME GO as the trigger collider (WispField
//      puts both on the wisp root `go`).
//    • FindHoverObject :3929 SKIPS any collider whose attachedRigidbody is the local
//      player → WispField re-parents the wisp OUT of the player hierarchy (world root).
//    • Interact :3966 resolves the Interactable via GetComponentInParent (GO-or-parent).
//
//  PER-WEARER VISIBILITY. Wisps are personal: they exist in the world FOR the local
//  player because they wear the lens. WispField only ever instantiates wisps on the
//  local client while the stone is worn, and destroys them all when it's removed —
//  so there is no networked wisp, no ZNetView, nothing another player can see. The pin
//  state (IsPinned) and the dim are likewise personal/client-only — no ZDO.
//
//  Clean-side (ADR-0001): SBPR-authored MonoBehaviour; implements the two vanilla
//  interfaces (precedent CairnInteractable, SBPR_TwistedPortal) and reads vanilla types
//  (Heightmap/Physics/Minimap) via public API only — no clone, no patch.
// ============================================================================

using UnityEngine;

namespace SBPR.Trailborne.Features.SeersStone
{
    /// <summary>
    /// One personal wisp orbiting one eligible object on the helix. Created by
    /// <see cref="WispField"/>; carries the source object's identity so Use (E) on the wisp can
    /// pin it with NO raycast. Implements vanilla <c>Hoverable</c>+<c>Interactable</c> so the
    /// engine renders the <c>[E]</c> prompt and dispatches Use; on a pin it dims its own glow.
    /// </summary>
    public sealed class WispBehaviour : MonoBehaviour, Hoverable, Interactable
    {
        private Vec3 _centroid;        // the cylinder axis (XZ from the source object, Y = base ground)
        private WispMotionParams _p;
        private float _t0;             // spawn time, so each wisp's helix has a stable origin
        private GameObject? _visual;   // the grafted glow child (for dim-on-pin); null if the graft failed

        /// <summary>The source object's prefab name (clone-stripped) — the eligibility re-check reads this.</summary>
        public string SourcePrefab { get; private set; } = "";
        /// <summary>The source object's friendly/hover name — becomes the pin label + the [E] prompt name.</summary>
        public string SourceFriendlyName { get; private set; } = "";
        /// <summary>Whether the source is a Pickable (abundance pin) or a Location (DiscoverLocation).</summary>
        public WispHitKind SourceKind { get; private set; }
        /// <summary>The source world position (for the pin + the merge check).</summary>
        public Vector3 SourcePos { get; private set; }

        /// <summary>True once this patch has been pinned via Use (E) — drives the dimmed hover + glow.</summary>
        public bool IsPinned { get; private set; }

        // Dim-on-pin factors (eyeball — Daniel verifies the final look on Prime). The INTENSITY drop
        // is the load-bearing "pinned" cue Daniel asked for ("the visual ... should become less intense").
        private const float DimIntensity = 0.40f;   // Light.intensity → 40%
        private const float DimRange     = 0.75f;   // Light.range → 75% (keep some reach so it's still visible)
        private const float DimAlpha     = 0.45f;   // particle start-colour alpha → 45%

        /// <summary>Configure the wisp at spawn. Centroid is the source's world position; visual is the
        /// grafted glow child (may be null) used for dim-on-pin.</summary>
        public void Init(Vector3 centroid, WispMotionParams p, string sourcePrefab,
                         string sourceFriendlyName, WispHitKind kind, GameObject? visual = null)
        {
            _centroid = new Vec3(centroid.x, centroid.y, centroid.z);
            _p = p;
            _t0 = Time.time;
            SourcePrefab = sourcePrefab ?? "";
            SourceFriendlyName = sourceFriendlyName ?? "";
            SourceKind = kind;
            SourcePos = centroid;
            _visual = visual;
            ApplyPosition(0f);
        }

        private void Update()
        {
            ApplyPosition(Time.time - _t0);
        }

        // ── Hoverable ────────────────────────────────────────────────────────────────────────
        // GetHoverName is the bold title; GetHoverText is the displayed crosshair tooltip. We emit
        // the vanilla $KEY_Use token and localize on the way out (the CairnInteractable idiom) so the
        // literal "$KEY_Use" never leaks to the player (that exact bug bit a 2026-06-05 playtest).

        public string GetHoverName() => SourceFriendlyName;

        public string GetHoverText()
        {
            string raw = IsPinned
                ? $"{SourceFriendlyName}  <color=#9aa0a6>\u2713 pinned</color>"      // muted, no Use line
                : $"[<color=yellow><b>$KEY_Use</b></color>] {SourceFriendlyName}";   // the native [E] <name> prompt
            return Localization.instance != null ? Localization.instance.Localize(raw) : raw;
        }

        // ── Interactable ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Use (E) on the wisp → pin the patch the wisp represents. NO raycast: the wisp already
        /// carries the source identity (<see cref="SourcePrefab"/>/<see cref="SourceFriendlyName"/>/
        /// <see cref="SourceKind"/>/<see cref="SourcePos"/>). Feeds the engine-free
        /// <see cref="SeersStonePinDecision.Decide"/> (eligibility re-check, no-count label,
        /// merge-radius dedup, private default), places via <see cref="SeersStonePinPlacement"/>, then
        /// dims. Returns true so vanilla plays the interact animation (Player.cs:3970-3972).
        /// </summary>
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (user != Player.m_localPlayer) return false;
            if (IsPinned) return true;   // already recorded — nothing to do (wisp is already dimmed)

            try
            {
                var existing = SeersStonePinPlacement.SnapshotPins();
                var plan = SeersStonePinDecision.Decide(
                    SourcePrefab, SourceFriendlyName, SourceKind,
                    new Vec3(SourcePos.x, SourcePos.y, SourcePos.z),
                    SeersStoneWhitelist.Eligibility, existing);

                if (!plan.ShouldPin)
                {
                    // "merged" ⇒ a same-name pin already sits within the merge radius: the patch IS on
                    // the map. Give the same pinned feedback (toast + dim) so an E-press never reads as
                    // a no-op (the very confusion Daniel reported). "ineligible" (defense-in-depth, should
                    // not happen — the wisp only spawns on eligible objects) stays silent.
                    if (plan.Reason == "merged")
                    {
                        MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "Already pinned");
                        SetPinned(true);
                    }
                    return true;
                }

                SeersStonePinPlacement.PlacePin(plan, SourcePos);
                SetPinned(true);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/SeersStone] Wisp Use-to-pin failed (ignored): {e.Message}");
            }
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        /// <summary>Flip the pinned state; on a true transition, dim the glow to confirm the pin.</summary>
        public void SetPinned(bool pinned)
        {
            if (IsPinned == pinned) return;
            IsPinned = pinned;
            if (pinned) ApplyDimmed();
        }

        /// <summary>
        /// Dim the grafted glow child to signal "pinned": drop Light intensity/range and particle
        /// start-colour alpha. Best-effort + null-safe (graft can fail → no visual to dim); a failure
        /// never un-pins the wisp. Personal/client-only, no ZDO.
        /// </summary>
        private void ApplyDimmed()
        {
            if (_visual == null) return;
            try
            {
                foreach (var light in _visual.GetComponentsInChildren<Light>(includeInactive: true))
                {
                    if (light == null) continue;
                    light.intensity *= DimIntensity;
                    light.range *= DimRange;
                }
                foreach (var ps in _visual.GetComponentsInChildren<ParticleSystem>(includeInactive: true))
                {
                    if (ps == null) continue;
                    var main = ps.main;
                    var c = main.startColor.color;   // StyleWisp set this grey at spawn; lower its alpha
                    c.a *= DimAlpha;
                    main.startColor = c;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/SeersStone] Wisp dim-on-pin failed (wisp still pinned): {e.Message}");
            }
        }

        // ── Motion (engine-coupled half of WispMotion) ─────────────────────────────────────────

        /// <summary>
        /// Place the wisp on its helix at elapsed time <paramref name="t"/>: horizontal offset from
        /// WispMotion (the cylinder wall), then ground height SAMPLED AT THE ORBIT POINT (the ground-
        /// aware-Y fix — on a slope the wisp tracks the terrain under its current orbit position so it
        /// never sinks into the uphill side), then the vertical sine on top. Writes ABSOLUTE world
        /// position each frame, so re-parenting the wisp to world root (the hover-pipeline fix) leaves
        /// motion unaffected.
        /// </summary>
        private void ApplyPosition(float t)
        {
            var h = WispMotion.HorizontalOffset(_p, t);
            float orbitX = _centroid.X + h.X;
            float orbitZ = _centroid.Z + h.Z;
            float groundY = SampleGround(orbitX, orbitZ, _centroid.Y);
            float height = WispMotion.VerticalHeight(_p, t);
            transform.position = new Vector3(orbitX, groundY + height, orbitZ);
        }

        /// <summary>
        /// Ground height at (x, z). Prefer the live Heightmap (cheap, no physics raycast); fall back
        /// to the centroid's Y if no heightmap is resolvable (open water / unloaded). This is the one
        /// step WispMotion can't do headless, isolated here so the geometry stays unit-tested.
        /// </summary>
        private static float SampleGround(float x, float z, float fallbackY)
        {
            if (ZoneSystem.instance != null)
            {
                // ZoneSystem.GetGroundHeight is the vanilla terrain-height query (decomp: used by
                // worldgen + placement). Returns the solid ground Y at the world XZ.
                float gh = ZoneSystem.instance.GetGroundHeight(new Vector3(x, fallbackY, z));
                if (!float.IsNaN(gh) && !float.IsInfinity(gh)) return gh;
            }
            return fallbackY;
        }
    }
}
