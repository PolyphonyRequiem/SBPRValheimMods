---
spec_name: trailborne-v1
shaped_at: 2026-06-03
shaper: spec-shaper (Starbright, in-session with Daniel)
status: current
progress: SHIPPED — v0.1.0 milestones M0–M4 all delivered (see ../MILESTONES.md); this is the LOCKED spec anchor (referenced by AGENTS.md, CONTRIBUTING.md, SpecCheck.cs). M5–M7 are v0.2.0+ scope.
last_reviewed: 2026-06-17
correction_notes: |
  - Initial Round 2 questions posed mechanics as undefined; they weren't.
    design/PARKED-2026-06-03.md locked most v1 design on 2026-06-02.
  - Round 2 corrections committed in c73ba19.
  - Round 3 (this revision): Starbright failed AGAIN to read the
    existing PLAYER_GUIDE.md (20KB locked design) and design/nomap.md
    (20KB patch-surface cross-ref) before posing Round 3 questions.
    Re-baselined against ALL repo docs now. Most "Round 3 answers"
    were partially or fully already in PLAYER_GUIDE.md. Where Daniel's
    today-answers SUPERSEDE PLAYER_GUIDE.md text, the PLAYER_GUIDE
    needs a follow-up doc-PR (tracked at bottom of this file).
  - Process lesson: read EVERY *.md in repo before EVERY shaper round,
    not just stage 1. Skill patched (commit pending).
---

# Requirements: SBPR Trailborne v1

> **LOCKED SPEC (shipped).** This is the authoritative v0.1.0 requirements anchor
> — referenced by exact path in `AGENTS.md`, `CONTRIBUTING.md`, and `SpecCheck.cs`.
> Milestones M0–M4 are delivered (see [`../MILESTONES.md`](../MILESTONES.md)).
> The shaper rounds below are preserved as the record of how the spec was reached;
> the spec itself is locked. Changes to a locked recipe/piece move spec + code +
> SpecCheck together (ADR-0002).

## Source idea

See `planning/initialization.md` for the verbatim raw idea + carried doctrine + concept-seed inventory.

**Critical primary-source reference:** `design/PARKED-2026-06-03.md` in this repo
already contains substantial v1 design lock-in from 2026-06-02 evening session.
This requirements.md MUST stay consistent with that document. Where this
document diverges from the parked doc, the parked doc wins unless explicitly
overridden in a numbered round below.

---

## Q&A round-by-round

### Round 1 — Scope & Purpose ✅ COMPLETE

**Q1.1 — v1 piece roster:** Propose v1 ships exactly: Explorer's Bench, Cairns, Pigments, Painted Signs, Trailblazer's Spade (single tool item, hoe/hammer tier-equivalent), **Path Lamps**.

**A1.1:** ✅ Yes — INCLUDING Path Lamps. They're a philosophy-completing piece (night-time trail illumination), not scope creep. Without them, the trail-discipline loop is complete-by-day, broken-by-night, and players will misuse vanilla torches to fill the gap. Path Lamps belong in v1.

**Explicitly OUT of Trailborne entirely (different mod, different family):**
- **Guardian Stones (active OR inert)** — server worldbuilding artifact, separate mod (`SBPR.Wardens` or similar), gated on `valheim-regions` macro-boundary work. Stripped from Trailborne scope entirely.

---

**Q1.2 — Map-nerf scope for v1:** *CORRECTED from initial pass.* v1 DOES nerf the Cartography Table — existing in-world Cartography Tables lose functionality; new ones cannot be built.

**Map situation for v1 (locked):**
- `nomap=ON` (server setting) → **no map at all** (vanilla nomap mode)
- `nomap=OFF` (default) → **minimap ONLY**, freely rotating, **no north indicator**, **no M-key map**
- Cartography Table: disabled functionality if pre-existing; cannot be built

**A1.2:** ✅ Locked per parked doc.

---

**Q1.3 — Server-gated vs always-on:** Remove `SBPRContext.OnSBServer` gate for Trailborne. Always-on, configurable via BepInEx config.

**A1.3:** ✅ Agreed for now.

---

### 🆕 DOCTRINE REFINEMENT from Round 1 (added by Daniel, captured for spec-writer):

**"Leverage Unity indirectly, not directly."** This refines fact #112. What we CAN do at runtime:
- Compose vanilla Unity prefabs via Harmony + reflection
- Instantiate vanilla `ParticleSystem` instances for visual effects
- Reflect vanilla materials onto vanilla meshes with runtime tinting
- Reuse vanilla sprites for menu icons where available
- Load PNG icons via `File.ReadAllBytes` → `Texture2D.LoadImage` → `Sprite.Create`

What we will NOT do (in v1):
- Open the Unity Editor
- Bake `.unity3d` assetbundles
- Author custom meshes, materials, ParticleSystems, or animations in Unity

**v1 visual approach: kitbash prototype assets where possible for playtesting.** Composite existing vanilla prefabs/materials/particles into Trailborne pieces. Visual polish (custom materials, custom icons that aren't kitbashed) is a v1.1+ concern. Goal is *playtest-quality mechanics* in v1, not *ship-quality art*.

**Reserved exception for v∞:** when **Locations** become a thing, Daniel reserves the right to revisit this doctrine — Locations need baked scene hierarchies that can't be assembled at runtime. NOT a v1 problem.

---

### Round 2 — Mechanics ✅ COMPLETE (corrected against design/PARKED-2026-06-03.md)

#### A2.1 — Cairn mechanics (LOCKED)

| Aspect | Locked value |
|---|---|
| **Activation** | Always-on once built (no fuel) |
| **Comfort tier ladder** | 5-tier: stones can stack to give comfort floors of **3 / 4 / 5 / 6 / 7** |
| **Comfort interaction with vanilla** | `max()` clamp — cairn never *reduces* effective comfort, only raises floor. **Open-air comfort (BUG-FIX 2026-06-13, t_4c5b5b2d):** raising the comfort LEVEL is not sufficient to grant the **Rested** buff — vanilla `Player.UpdateEnvStatusEffects` gates the `Resting` status behind a SECOND near-fire condition (`flag = m_nearFireTimer < 0.25f`), which a heat-free cairn never satisfies in the open. The fix rides vanilla's own **`Resting` → `SE_Cozy` (10 s dwell) → `Rested`** pipeline for **campfire-parity timing** (Daniel's call, 2026-06-13, overriding the architect's "grant Rested immediately" default): a Harmony **postfix on `Player.UpdateEnvStatusEffects`** SEEDS the `Resting` status (without resetting its `SE_Cozy` timer) when a cairn is in range and the player otherwise qualifies — reading vanilla's own just-set exclusion statuses (Cold/Freezing/Burning/Wet+WarmCozy) — and a paired **prefix on `SEMan.RemoveStatusEffect`** swallows ONLY vanilla's per-tick `Resting` strip (the sole strip site, `UpdateEnvStatusEffects`) for the local player while qualifying, so `SE_Cozy.m_time` accumulates to the ~10 s `m_delay` and vanilla grants `Rested` itself. **Never touches `m_nearFireTimer` (so NO heat):** the player becomes eligible immediately on sitting near a cairn (Resting + `$se_resting_start` comfort message), then `Rested` appears after the ~10 s ramp, exactly like a campfire — but the cairn still emits no warmth. Rested TTL still scales from the (raised) comfort level via `SE_Rested.UpdateTTL`. See `docs/investigations/2026-06-13-cairn-no-open-air-comfort-near-fire-gate.md`. |
| **Implementation surface** | Patch `SE_Rested.CalculateComfortLevel` directly (cairn is NOT in vanilla `ComfortGroup` enum so it bypasses vanilla's same-group dedup) |
| **Decay** | ⚠️ **MANDATORY decay** — cairns ARE destructible. Downgrade @25% HP, collapse @0%. Cairns are *evidence of a trail still being walked* — abandonment = collapse. Re-correction: I (Starbright) proposed indestructible in error this morning; Daniel snapped me back. Decay is the design's *core thesis-in-a-piece*. |
| **Repair** | Flat **3 stone + 1 resin** regardless of damage level |
| **Pigment / banner persistence** | Persist across rebuilds — applied colors survive damage + repair cycles |
| **Downgrade re-ignite of resin** | OPEN — Daniel: "lean deliberate-only" (i.e. requires explicit player action, not auto-re-ignite on repair) |
| **Visual (LOCKED 2026-06-05, Daniel)** | Rich procedural stone pile — see **§A2.1b — Cairn visual** below. Per-tier haphazard stack of vertically-squashed / horizontally-flattened stones (count = the stone ladder), **deliberately constructed from the bare `Pickable_Stone` mesh+material — NOT runtime prefab clones (REVISED 2026-06-07)**, on a fire-neutralized `bonfire` structural base, with an HP-gated **wear-state ember** at the top that fizzles out below ~75% HP. |
| **Build cost** | Per-tier stone ladder (cumulative): T1=9 / T2=12 / T3=15 / T4=18 / T5=21 Stone, +1 Resin, +1 Cairn Marker at T1 (see §A3.1). |
| **Placement elevation gate (LOCKED 2026-06-08, Daniel)** | A cairn **cannot be placed below 2 m above sea level**, measured at the piece's placement origin (ground-contact point). Sea level is Valheim's global ocean plane read live from `ZoneSystem.instance.m_waterLevel` (default **30**; compile-time anchor `ZoneSystem.c_WaterLevel = 30f`) — NOT hardcoded. Valid iff `placementPoint.y ≥ seaLevel + 2` (≥ y32 at default sea level); below is rejected. Keeps cairns out of the waterline / shallows — they are trail markers, not buoys. **Cairn-only** — signs / Path Lamps / Spade path ops are unaffected (widening to other trail pieces is an OPEN question for Daniel, not assumed). Implemented as a Harmony **postfix on `Player.UpdatePlacementGhost`**: for a cairn ghost vanilla rated `Valid` but sitting too low, force `m_placementStatus = Invalid` (blocks the place + shows `$msg_invalidplacement`) and redden the preview via the public `Piece.SetInvalidPlacementHeightlight(true)`. Daniel, 2026-06-08 (v0.2.9 playtest): "cairns should not be able to be placed at elevations under 2 m from sea level." |

#### A2.1b — Cairn visual (LOCKED 2026-06-05, Daniel)

> Supersedes the earlier "procedural `rock_low` stack capped with a rune-glow particle" sketch AND extends the 2026-06-05 bonfire-neutralization fix (PR #23 / card t_9f8341c9). PR #23 stripped ALL fire off the cairn (correct — a cairn is not a campfire); this spec keeps that neutralized base and adds back a **small, deliberate, HP-gated** ember as a *wear indicator*, not a light source. The two do not conflict: neutralization runs first and unconditionally; the ember is a separate opt-in element layered on top only at high HP.

The cairn should read as a **real, hand-piled trail cairn** — not a tidy cone. Three parts:

| Aspect | Locked value |
|---|---|
| **Structural base** | Built **additively from scratch** (ADR-0006 — `Assets.ConstructPieceShell`), carrying only `WearNTear` / `Piece` / `ZNetView`; it is **NOT** cloned from `bonfire`, so there is no donor `Fireplace` / `EffectArea` / heat to strip. A small cosmetic flame is GRAFTED on top (`BuildCosmeticFire` / `ReconcileFire`) — flame VFX + crackle + a dim sub-torch Light, **no `EffectArea`, no heat, no fuel**. (Supersedes the earlier "clone bonfire then neutralize / PR #23" description — net behavior is identical, mechanism is additive.) |
| **Stone pile** | A **haphazard** pile of stones assembled at runtime by **deliberate construction (REVISED 2026-06-07, Daniel — was runtime prefab-cloning)**. Each stone is a **hand-built GameObject carrying ONLY `Transform` + `MeshFilter` + `MeshRenderer`** — the bare stone **mesh + material are read off the vanilla `Pickable_Stone` donor (mesh `Box02`, ~0.32×0.19×0.65 m) without ever instantiating that networked prefab** (so no `ZNetView`, no `Pickable`, no `StaticPhysics`, no ZDO, no collider rides along). **Why the pivot:** the old path `Instantiate`d `Pickable_Stone` (a ZNetView-bearing prefab) onto the active cairn inside vanilla's init-ZDO window and then `DestroyImmediate`'d the ZNetView, orphaning null-ZDO entries in `ZNetScene.m_instances` that vanilla `RemoveObjects` dereferenced every frame → client soft-lock (×21 "Double ZNetView" → repeating NRE). Constructing from bare mesh removes the crash mechanism by construction. **Count scales with tier and equals the stone ladder: T1=9, T2=12, T3=15, T4=18, T5=21 stones.** Each stone renders at **NATIVE mesh proportions — NO per-stone scaling or squash (REVISED 2026-06-08, Daniel — was vertically-squashed/horizontally-stretched)**. The squashed-disc look read as flat coins in-game; native `Box02` rocks read as real piled stones. Placement is **deterministically randomized** (seed = ZDO id, so it survives reload + is identical on every client): jittered position, random yaw, slight random tilt — a believable irregular pile, wider at the base, tapering up, built from POSITION + ROTATION variation alone. |
| **Color identity — BANNER (REVISED 2026-06-08, Daniel — replaces stone pigment tint; wind mechanism RE-REVISED 2026-06-09 → A-prime real Cloth, card t_e95949c2)** | The cairn's bound color is carried by a **wind-responsive banner**, NOT by tinting the stones (stones stay natural grey). One **cloth element** is read off a vanilla banner donor (cloth mesh + `Banner_Border_*_mat` material ONLY — never the `woodbeam` pole) and hand-built as an additive GameObject carrying a `SkinnedMeshRenderer` + `UnityEngine.Cloth` + reusable `ClothWindDriver` (additive, ADR-0006 — no `ZNetView`/`Piece`/pole). The banner's **wind response is delivered by that additive `UnityEngine.Cloth` simulation** on the cloth graft, driven by a small **reusable `ClothWindDriver` MonoBehaviour** that — mirroring vanilla `GlobalWind` (`decompiled.cs:30383-30392`, fair-game vanilla, lifted directly) — sets `cloth.externalAcceleration = EnvMan.instance.GetWindForce() * mult` and `cloth.randomAcceleration = GetWindForce() * mult * randomFactor` (randomFactor 0.5) on a **~2 s `InvokeRepeating` cadence** (NOT per-frame). `GetWindForce()` = wind **direction × intensity** (0.05–1.0), so the banner streams with both **DIRECTION and FORCE**. 🔴 **WINDSOCK re-root (card t_4a4a9706, 2026-06-09):** the cloth is mounted as a **windsock/wind-tail**, not a square drape — a **small mount band is hard-pinned and seated ELEVATED above the pile crown**, and a **longer, narrower tail hangs DOWN past the cairn**, **free-falls slack under gravity on build** (`cloth.useGravity = true`), then **streams downwind**. Freedom is **strongly asymmetric**: the mount band `maxDistance = 0`; every lower particle ramps `maxDistance = FreeDistance × (depthBelowMount/span)^RampExp` with a **large FreeDistance** and **RampExp > 1**, so the far tail genuinely **FLOPS/streams** while the mount stays fixed (replaces the A-prime near-linear ramp that made the whole sheet *vibrate in place* — "zigs and zags on the spot"). ⚠️ The bare-material vertex-shader waggle alone (first build) is **NOT** sufficient, and a stiff in-place vibration (second build) is **NOT** the windsock — directional streaming from one tethered end is the bar. Additive / ADR-0006-clean: `Cloth`/`SkinnedMeshRenderer`/driver are cosmetic Unity components, not a `ZNetView`; nothing networked is cloned. `ClothWindDriver` is **feature-agnostic and reusable** (cairn-banner is its first consumer; lives in `Runtime/`, not `Features/Cairns/`). Wind response is per-client physics off the synced global wind vector — it is NOT required to be ZDO-deterministic across clients (unlike the pile layout, which is), same as vanilla cloth. Color→donor (LOCKED 2026-06-08): **black→`piece_banner01`, blue→`piece_banner02`, red→`piece_banner04`, white→`piece_banner11`**. *Eyeball note:* the donor secondary tones (02 = blue/yellow, 11 = black/white-inverted) are carried as-is pending in-game review. Banner persists across rebuilds (re-applied from ZDO color on every `BuildKitbashArt`). |
| **Banner dimensions & seating (NEW 2026-06-09, card t_5756cd21; bake method RE-REVISED 2026-06-09 for A-prime Cloth, card t_e95949c2)** | The cloth is sized by **measure-and-normalize**, NOT a single scalar — the donor `default` cloth mesh is a flat Y-Z sheet (native `(X 0, Y 2.974, Z 1.1802)`, verified via vprefab on `piece_banner01/02/04/11`, all identical), so **Z carries the horizontal WIDTH and Y the vertical DROP; X is zero-thickness and is guarded/inert.** Each in-plane axis normalizes off the donor `bounds.size` to a target metre size: **width(Z) ≈ 0.236 m (`TargetBannerWidthZ`), drop(Y) ≈ 0.892 m (`TargetBannerDropY`)** (FLAGGED — starting eyeball derived from Daniel's 3×-wide/2×-long report; final metres bake here on in-game sign-off). The retired uniform `BannerScale = 0.6` could not correct an *anisotropic* error. 🔴 **A-prime bake method (t_e95949c2):** because `UnityEngine.Cloth`'s constraint solver **shears under a skewed `lossyScale`**, the normalization is applied to a **per-instance copy of the cloth mesh's VERTICES** (`Instantiate(donorMesh)` → scale verts by `sy = TargetBannerDropY/native.y`, `sz = TargetBannerWidthZ/native.z` → `RecalculateBounds`), so the rest-shape already carries the measured proportions and the Cloth simulates under a **UNIFORM** `transform.localScale = Vector3.one`. The two `TargetBanner*` constants remain the single source of truth for the proportions — they now feed the vertex bake instead of `transform.localScale`. The per-instance mesh is freed on every rebuild (a `DestroyMeshOnDestroy` janitor) since a runtime `Mesh` is not GC'd with its GameObject; the shared donor mesh is never mutated. 🔴 **WINDSOCK seating + LIVE-CONFIG re-revision (card t_4a4a9706, 2026-06-09):** the cloth pivot sits at its top (the mount end) and hangs down, so the mount is now seated **`BannerMountHeight` (default ≈ 0.20 m — lowered from 0.70 m for issue 12 / t_0e97ec16 so the banner TOP reads ~0.5 m lower; FLAGGED, final metre bakes on Daniel's in-game sign-off) ABOVE the pile crown** — ELEVATED, not a half-drop hug — so the tail free-falls DOWN past the cairn as a proper tail (replaces the retired `BannerSeatDropFrac` half-drop seat). Because this banner is a **pure client visual that cannot be verified headless/CI and shipped wrong in-world twice while CI was green**, **every look-shaping constant is now a LIVE BepInEx config entry** under the `CairnBanner` section (range-clamped): `SBPR_BannerTailLength` (drop, default **1.15 m** — longer than the retired 0.892 m square drape), `SBPR_BannerWidth` (default **0.18 m** — narrower than the retired 0.236 m so it reads as a tail/sock), `SBPR_BannerMountHeight`, `SBPR_BannerOffsetXZ`, `SBPR_BannerWindMult`, `SBPR_BannerWindRandomFactor` (default **0.25**, lowered from vanilla 0.5 so directional streaming dominates jitter), `SBPR_BannerClothDamping`, `SBPR_BannerTailFreedom` (default **3.0**), `SBPR_BannerFreedomRampExp` (default **2.0**), `SBPR_BannerMountPinBandFrac`, `SBPR_BannerUseGravity` (default **true**). The matching `CairnTag.Default*` consts are the single-source-of-truth FALLBACK (no-Plugin unit context) and the starting eyeball; the live `.cfg` value wins at runtime. **Daniel tunes the windsock feel in ONE joined session (BepInEx ConfigurationManager / edit-`.cfg`-and-reload), then we BAKE the chosen metres back into these defaults + this row.** The retired `TargetBanner*`/`BannerSeatDropFrac`/`BannerClothTopPinBandFrac` consts are gone. |
| **Wear-state ember (NEW)** | A **small** flame/ember at the **top** of the pile, present **only while HP ≥ ~75%** (pristine). It **fizzles out below ~75%** and stays out until repaired back to pristine. This is the ONLY fire on the cairn — small and decorative, NOT the donor bonfire's blaze, NOT a light/heat source (no `EffectArea`, no comfort contribution — comfort is the `SE_Rested` patch + open-air Rested-buff postfix). Implement as a **small dedicated particle/light element toggled by HP**, layered on the neutralized base — do NOT re-enable the donor `Fireplace`. |
| **Wear states (visual ladder)** | ≥75% HP = pristine (ember lit). <75% = fizzled (ember out; per §A2.1 this is also the repair-eligible threshold). <25% = downgrade one tier (rebuild the pile at the lower stone count). 0% = collapse (piece destroyed, leaves a small rubble remnant). The ember tracks the pristine/fizzled line; the stone count tracks the tier. |
| **Determinism** | Pile layout + ember presence must be a pure function of (ZDO id, tier, current HP bracket) so all clients and post-reload spawns agree. No per-frame RNG. |

⚠️ **Open technical questions for the engineer (investigate, don't guess):**
1. Best vanilla source for the **small ember** — a stripped-down particle from `fire_pit`/`bonfire` re-parented to the pile top, vs. a minimal custom `ParticleSystem`. Whichever, it must be cheap and not re-introduce heat/light/SFX.
2. The HP→visual hook — postfix `WearNTear.OnDamage` / a health-bracket check on the `CairnTag` to toggle the ember and rebuild the pile on tier change. Confirm the bracket fires on repair *up* as well as damage *down*.
3. Flatten ratios + jitter ranges that look "haphazard but stable" — tune against a joined client; bake the chosen constants into the spec on sign-off.

**Engineering resolutions (card t_f3761d28, build-verified 2026-06-05 — pending in-game sign-off):**
1. **Ember source → minimal custom `ParticleSystem`.** A hand-built GameObject carrying ONLY `Transform` + `ParticleSystem` + `ParticleSystemRenderer` (so by construction no `Light`/`EffectArea`/`AudioSource`/`SmokeSpawner` and it never touches the donor `Fireplace`). It borrows only the *material reference* off the vanilla `fire_pit` particle renderer so it renders without shader-guessing — the donor prefab is never instantiated, so no heat/light/SFX rides along. Re-parenting a real donor sub-particle was rejected: it risks dragging back the exact components PR #23 neutralized. Tiny budget (≤14 particles, rate 10/s). Parented under the kitbash root so the neutralization sweep (which excludes the kitbash) never strips it and a tier rebuild recycles it.
2. **HP→visual hook → 1 Hz health-bracket poll on `CairnTag` (`InvokeRepeating`), NOT a `WearNTear.OnDamage` postfix.** The poll is *path-independent*: it reconciles the ember on repair-UP, debug-damage-DOWN, out-of-zone backfill, and natural weather decay alike — sidestepping the open risk that a damage-only vanilla hook would miss the repair-up relight. The repair path additionally calls `RefreshEmber()` for an instant (no up-to-1 s wait) relight on the player's own action. The same poll performs the §A3.5 auto-downgrade (HP <25% & tier >1 → drop one tier + `Repair()` to 100% of the new tier; tier 1 falls through to 0% collapse). Owner-gated.
3. **Flatten/jitter constants (proposed — eyeball on a joined client, then bake here):** stones squashed to vertical/horizontal ratio **0.16–0.30**; overall size **0.55–0.95**; base disk radius **0.42 m (T1) + 0.06 m/tier**, pile height **0.34 m (T1) + 0.12 m/tier**; height exponent **1.6** (base-weighted → wider base, tapering up); top taper **0.78**; ±**12°** tilt; ±**0.06 m** lateral / ±**0.04 m** vertical jitter. Constants live in `CairnTag` as named `private const`s (single source of truth) until sign-off.

**Banner-wind engineering resolution (A-prime, card t_e95949c2 — re-grounded on `v1` after #61; build-verified 2026-06-09 0err/0warn Release; pending in-game sign-off, Daniel AC9):**
1. **Renderer → `SkinnedMeshRenderer` (NOT `MeshRenderer`).** `UnityEngine.Cloth` simulates a `SkinnedMeshRenderer`'s vertices, so the banner graft carries a `SkinnedMeshRenderer` with the **baked** cloth mesh (see #2) + the donor `sharedMaterial`. `updateWhenOffscreen=false` with an explicit roomy `localBounds` (sized off the baked drop, ≈ `1.5×`/`2×` the drop) so a streaming banner isn't screen-edge-culled while swinging beyond its rest AABB.
2. **Dimensions baked into a PER-INSTANCE mesh, Cloth under a UNIFORM transform (AC10 — the #61 ⊕ A-prime reconciliation).** `UnityEngine.Cloth`'s constraint solver **shears under a skewed `lossyScale`**, so the #61 anisotropic `transform.localScale` (which the wind feature must keep) cannot coexist with Cloth on the same GameObject. Resolution: `Instantiate` a private copy of the donor cloth mesh, scale its **vertices** by `sy = TargetBannerDropY/native.y` and `sz = TargetBannerWidthZ/native.z` (the same factors #61 computed), `RecalculateBounds`, assign to the `SkinnedMeshRenderer.sharedMesh` + `Cloth`, and leave `transform.localScale = Vector3.one`. The rest-shape carries the measured 0.236 m(Z) × 0.892 m(Y) proportions; the Cloth simulates clean (no skew). The `TargetBanner*` constants are STILL the source of truth — they feed the bake. The per-instance mesh is freed on every rebuild via a reusable `DestroyMeshOnDestroy` janitor (a runtime `Mesh` is not GC'd with its GameObject); the shared donor mesh is never mutated. `UnityEngine.Cloth` lives in **`UnityEngine.ClothModule`** (Unity 6 — `6000.0.61f1`), a separate assembly from `PhysicsModule`; that reference was added to `SBPR.Trailborne.csproj` (DLL ships in the Valheim Managed folder AND the CI dedicated-server payload, Steam app 896660 — verified present alongside `PhysicsModule.dll`).
3. **Mount-band pin + STEEP asymmetric tail ramp via `Cloth.coefficients`, mapped by a SPAN-FRACTION band against `Cloth.vertices` (WINDSOCK re-revision, card t_4a4a9706).** Built one `ClothSkinningCoefficient` per cloth particle: particles within `SBPR_BannerMountPinBandFrac` (default 0.04 × the baked Y-span) of `yMax` get `maxDistance=0` (a small hard-pinned MOUNT cluster — the donor cloth has a clean 8-vertex top row at `fromTop/span≈0`, next row at `≈0.059`, X-ray-confirmed); every lower particle gets `maxDistance = SBPR_BannerTailFreedom(default 3.0) × (depthBelowMount/span)^SBPR_BannerFreedomRampExp(default 2.0)`. The **`^RampExp>1` curve + large FreeDistance** is the load-bearing windsock change: it keeps the band just below the mount near-rigid and lets the FAR TAIL travel far, so the tail FLOPS/streams from one tethered end instead of the whole sheet vibrating in place (the A-prime linear `×(depth)` at FreeDistance 1.0 gave near-uniform low freedom → "zigs and zags on the spot"). `cloth.useGravity=true` (default) makes the tail free-fall slack on build, then wind streams it; `cloth.damping` (default 0.10) is the flop-vs-stiff knob — both live config. 🔴 **Two correctness traps the implementation still guards (both X-ray-grounded, both "compiles-clean-renders-wrong" if missed):** (a) the pin band is a **fraction of the Y-span, not absolute metres**, so the same mount row is caught regardless of the per-instance Y-bake scale (an absolute band would over-pin after the bake changes the span); (b) `Cloth` **welds coincident vertices** (the donor cloth is 78 mesh verts but only 71 unique positions — 7 coincident pairs), so `coefficients.Length` can be < `mesh.vertexCount` — coefficients are mapped against the authoritative `Cloth.vertices` array (== coefficient count), falling back to `mesh.vertices` only on a 1:1 length match, and logging loudly + leaving the cloth unpinned if neither matches (rather than mis-pinning a blow-away banner). If a future game patch ships the donor mesh non-readable, the bake/pin both detect it and skip the banner with an ERROR log (no wrong-sized blow-away banner).
4. **Driver = reusable `ClothWindDriver` in `Runtime/`** (feature-agnostic, AC8). Mirrors vanilla `GlobalWind` (`decompiled.cs:30383-30392`; cadence `:30344`): `InvokeRepeating("UpdateWind", Random(1.5–2.5), 2f)` + one immediate call; each tick `cloth.externalAcceleration = EnvMan.instance.GetWindForce() * Multiplier` and `cloth.randomAcceleration = … * RandomFactor`. **Chosen default knobs (card t_4a4a9706): `SBPR_BannerWindMult` = 1.0 (vanilla; intensity response already confirmed in-world), `SBPR_BannerWindRandomFactor` = 0.25 (LOWERED from vanilla 0.5 so the DIRECTIONAL term dominates the omnidirectional jitter that read as in-place zig-zag).** Both are live config. `CheckPlayerShelter=false` for cairns (open-air); the shelter-zeroing branch is wired through for reuse by sheltered cloth. No-op on headless (`BuildBanner` early-returns when there's no graphics device; the driver also null-guards a missing `Cloth`).
5. **ALL banner look-knobs are LIVE BepInEx config (`CairnBanner` section), not const-only (card t_4a4a9706).** This banner is a pure client visual that cannot be verified headless or in CI, and it shipped wrong in-world TWICE while building 0/0 (shader-only waggle, then stiff in-place vibration). Rather than burn a third recompile→PR→playtest cycle per guess, every shaping constant is a range-clamped `Config.Bind` entry (`SBPR_BannerTailLength/Width/MountHeight/OffsetXZ/WindMult/WindRandomFactor/ClothDamping/StretchStiffness/BendStiffness/TailFreedom/FreedomRampExp/MountPinBandFrac/UseGravity`). `CairnTag.Default*` consts are the single-source-of-truth fallback. Daniel converges the windsock feel in **one** joined session, then we bake the chosen values into the defaults + the §A2.1b rows above. **logs-green ≠ playable; this card closes only on Daniel's in-game windsock confirmation in a forced thunderstorm.**
6. **Cloth STIFFNESS set explicitly — the THIRD-attempt root cause (card t_a2fc3073, 2026-06-09).** The windsock still "waggled in place and would not stream downwind" through v0.2.11 because `BuildBanner` set `useGravity`, `damping`, and the pin coefficients but **never set `cloth.stretchingStiffness` / `cloth.bendingStiffness`** — so they sat at Unity's default **1.0 (maximally RIGID)**. A maximally-stiff cloth under the ~1 m/s² wind force from `ClothWindDriver` can only *jitter*; it cannot *displace*, which is exactly the in-place waggle that responds to wind *intensity* (more shake in a storm) but never to wind *direction*. **Fix:** set both stiffnesses LOW as live config — `SBPR_BannerStretchStiffness` and `SBPR_BannerBendStiffness`, **default 0.5 / 0.5**, range clamp `[0.1, 1.0]` (never 0 — zero stiffness makes the Unity Cloth solver unstable). 🔴 **Grounding correction (load-bearing):** the prior cards said to mirror "the vanilla *banner* donor's authored Cloth values," but an offline X-ray (UnityPy over the dedicated-server asset payload, bundle `c4210710`) confirmed **`piece_banner01` has NO `UnityEngine.Cloth` at all** — it is `woodbeam` (pole) + `default` (cloth child, plain `MeshRenderer` + `Banner_Border_*_mat`), and vanilla banners wave via a **wind shader on a static mesh** (this *is* the "shader-only waggle" recorded as the first failed attempt). The correct vanilla reference for a wind-*streaming* physics cloth is the **SAIL** (`sail_full`: `stretchingStiffness 0.5`, `bendingStiffness 0.5`, `damping 0.0`, `worldVelocity/AccelerationScale 0.5/0.5`, 6 identical instances), so the 0.5/0.5 defaults are grounded in IronGate's own tuning for a streaming sheet — not a guess. `worldAcceleration/VelocityScale` are deliberately **not** exposed: they scale the cloth's reaction to its *transform* moving through the world (a sail rides a moving ship), but the cairn banner's transform is static, so they are inert here and exposing them would ship dead config. The `externalAcceleration` wind path (driver) was always correct — stiffness was the missing half. **This card still closes ONLY on Daniel's in-game `wind 1 0` vs `wind 1 180` directional confirmation** (the test that failed twice); a clean build proves nothing for this visual.

7. **Mesh TESSELLATION + ROCK-DRAPE colliders — the FIFTH-attempt look fix (Daniel 2026-06-10, after the Step-1 diagnostic proved the solver RUNS).** The attempt-#6 `BannerDiagnostic` probe (card t_7de074f3) confirmed in-world that the Cloth solver is alive (`SOLVER=RUNNING`), hangs gravity-correct (`HANGS-DOWN`), and yaws to wind (alignment confirmed) — so the banner was never "broken." The residual complaint is that the tail reads **stiff / "like all four corners are pinned"** (Daniel) even with stiffness + freedom maxed live. Two grounded causes, both addressed additively (ADR-0006 — we mutate our own per-instance baked mesh + author our own colliders; nothing cloned):
   - **(a) The donor cloth is a COARSE ~78-vertex sheet.** Too few Cloth particles between the pinned mount and the tail tip to curve/drape — the sheet can only move as a near-rigid quad. **Fix:** subdivide the per-instance baked mesh by edge-midpoint 1→4 split, `SBPR_BannerSubdivisions` (default **1** = ~4× polys; range `[0,2]`, 2 = ~16×). Positions/normals/UVs/colors interpolate (the Option-B wind-shader wave mask survives); midpoints dedupe per shared edge so the sheet stays welded (no split seams the solver would tear). Runs once at build, never per frame; cloth-solve cost scales with particle count, hence the live cap.
   - **(b) The cloth had NO colliders, so it clips straight THROUGH the rock pile** — it can't "flap against the stones" (Daniel) because it has no knowledge they exist. **Fix:** `SBPR_BannerRockDrape` (default **true**) places a small vertical stack of `ClothSphereColliderPair` single-spheres down the pile centre axis (4 spheres, radius tapered to match `BuildPile`'s disk taper per tier), registered on `cloth.sphereColliders`. The cloth then drapes ON the pile. Cheap (4 pairs, well under Unity's 32-pair cap) vs. one-collider-per-stone (T1=9…T5=21). Only reads with subdivisions>0 — a coarse sheet has too few particles to drape. Both are LIVE config per resolution #5's discipline (client-only visual, unverifiable headless/CI). **NOTE:** the wind-force knob (`SBPR_BannerWindMult`) is the wrong lever for the stiffness symptom — Valheim wind intensity is `Clamp01`'d (max 1.0) so force maxes ~1 m/s² vs gravity's 9.8; the tail rigidity was particle-count + missing colliders, not under-force. Default mult stays 1.0 pending Daniel's in-world tune; he may raise it for a livelier calm-wind read, but that is independent of (a)/(b). 🔴 **ISSUE-10 FOLLOW-UP (2026-06-17, card t_293f2df5 — banner STILL won't stream in high wind):** this NOTE was right that force is the wrong lever — Daniel confirmed it empirically (live mult=20× → only ~10–15° lean; a *free* tail at 20× should reach ~64°). The remaining ~50° deficit points back at **(b)**: a geometry check (`scripts/banner-geom-check.py`) shows the rock-drape spheres (radius **0.46–0.58 m** at the base) exceed the tail's **0.30 m** lateral standoff (`SBPR_BannerOffsetXZ`), so the lower tail rest-**penetrates** them and is **caged in every wind direction** — the collider stack added to stop clipping may now be the constraint stopping the *streaming*. **Action:** a live-tune playbook (toggle `SBPR_BannerRockDrape` off + raise `SBPR_BannerTiltDegrees` toward horizontal, re-reading `bannerdiag` after each), then bake the result — and likely re-tune the rock-drape **geometry** (shrink radius / widen standoff / restrict spheres to the upper tail) so drape-on-stones and stream-in-wind can coexist. See `docs/investigations/2026-06-17-cairn-banner-issue10-force-vs-constraint.md` for the ordered playbook + ATs. This row updates WITH that code (engineer-systems follow-up), once Daniel's live result is in.

**Banner-wind A/B HARNESS (card t_1d7c0d19, 2026-06-10 — Daniel mid-flight: "B might be the visually better option … implement both for now? Make white use B and the rest use A"; build-verified 0err/0warn Release; pending in-game pick):**

This is a **temporary comparison harness, NOT a final ship state.** All four banner colors render, but the wind MECHANISM is routed by color so Daniel can stand in front of four cairns and compare side-by-side in one world, then a follow-up card collapses all colors onto the winner and deletes the loser. The collapse is a **one-line edit** (the `CairnTag.ShaderWaveColors` set), by design.

1. **Routing (LOCKED for the harness):** `white → Option B`; `black, blue, red → Option A`. Implemented as `private static readonly HashSet<string> ShaderWaveColors = { "white" }` in `CairnTag`; `BuildBanner` builds + seats the shared per-instance baked mesh (same width/drop/mount for both, so the comparison is size- and position-matched), then dispatches: in-set → `BuildShaderWaveBanner`, else → `BuildClothWindsock`. Each color still resolves its OWN donor (white=`piece_banner11`, black=`piece_banner01`, blue=`piece_banner02`, red=`piece_banner04`), so every cairn shows its color in BOTH modes; both persist across damage/rebuild (re-applied from ZDO color on every `BuildKitbashArt`).
2. **Option A — directional Cloth windsock (the alignment fix).** Finishes the windsock the prior FOUR attempts left half-built. All of resolutions #1–6 above still apply (SkinnedMeshRenderer + `UnityEngine.Cloth`, baked per-instance mesh, mount-pin + steep tail ramp, stiffness 0.5/0.5, `ClothWindDriver`). 🔴 **The added mechanism is the missing `m_alignToWindDirection` port:** vanilla cloth streams downwind because `GlobalWind.UpdateWind()` ROTATES the whole transform to the wind FIRST (`assembly_valheim.decompiled.cs:30348-30392`), THEN the Cloth solver ripples on top — our driver previously copied only the cloth-force branch, so the banner force-jittered but never ORIENTED (exactly the 4×-failed symptom). `ClothWindDriver` now carries `AlignToWindDirection` (default OFF on the reusable driver; the cairn windsock sets it ON) + an `AlignMode` axis enum. 🔴 **Pivot/axis trap:** our cloth mesh is a flat Y-Z sheet (Y=drop, Z=width, X=normal), so a blind vanilla `LookRotation(windDir, up)` would point the WIDTH EDGE into the wind and pitch the sheet. The default `AlignMode 0 = StreamYaw` is a **pure yaw about world-up** (the sheet plane contains the wind; the tail streams downwind in-plane; the pinned mount stays horizontal and the drop stays vertical). `AlignMode 1 = FaceYaw` (+90°, broad face to wind) and `2 = VanillaFull` (literal vanilla, pitches the sheet — reference only) are selectable so Daniel prototypes the right axis in-game rather than us shipping a third hardcoded guess. New live config: `SBPR_BannerAlignToWind` (bool, default true), `SBPR_BannerAlignMode` (int 0–2, default 0). Dead-calm guarded (skip rotation below a wind-intensity epsilon so a still banner doesn't snap to a heading).
3. **Option B — vanilla shader-wave flag (the clean alternative).** A SECOND construction path that reproduces a vanilla DECORATIVE banner's wave: a **static mesh** (`MeshFilter` + `MeshRenderer`) carrying the donor's cloth **material** (`Banner_Border_*_mat`), whose vertex shader already reads the global wind uniforms (`_GlobalWind1/2`, `_GlobalWindForce`, decomp :80463-80471) and waves the sheet for free — **NO `Cloth`, NO SkinnedMeshRenderer-for-cloth, NO pin coefficients, NO `ClothWindDriver`.** This is exactly how every vanilla banner moves (X-ray-confirmed: `piece_banner01`'s `default` child is a plain `MeshRenderer` + `Banner_Border_*_mat`, NO Cloth at all). It WAVES in place; it does NOT stream directionally — that is Option A's job. Zero per-instance physics cost, rock-solid (no Cloth solver to ship wrong). It reuses the same per-instance baked mesh (size-matched to A) via the shared `DestroyMeshOnDestroy` janitor. ⚠️ **Honest note:** Option B is essentially the FIRST banner build's "shader-only waggle" (failed attempt #1) — but it was rejected then because Daniel wanted a *windsock*, NOT because it looked bad; taste may now prefer the clean vanilla flag, which is the whole point of the side-by-side. **UNVERIFIED until in-game:** the X-ray says the material is the whole mechanism so the bare donor material on a static mesh should "just wave," but if the wave turns out to only animate under the vanilla banner's specific prefab setup, that's a handoff note — the motion is not faked.
4. **Collapse-to-winner (follow-up, after Daniel's pick):** flip the `ShaderWaveColors` set — `{ "white","black","blue","red" }` for all-B (then delete the dead Option-A path) or `{}` (empty) for all-A (then delete the dead Option-B path). One line until that pick. **This harness closes ONLY on Daniel's in-game thunderstorm comparison + his pick of A or B; logs-green ≠ playable. NEITHER look is verified (his flight = no in-game check yet).**

#### A2.2 — Trailblazer's Spade (LOCKED — single item, NOT options)

**Single tool item.** Hoe/hammer tier-equivalent. Its own slot in the player's inventory, its own keybind, its own selection wheel.

| Capability | Detail |
|---|---|
| **Path widths** | **1.5m / 3m / 5m** — three selectable widths (analogous to hoe's flatten radii) |
| **Path stamina** | **Flat 2 stamina per path/replant op, INDEPENDENT of width** (1.5m / 3m / 5m all cost 2). See A3.9. |
| **Replant Grass** | **Three grass-restore widths (1.5m / 3m / 5m)** that mirror the vanilla **Cultivator's replant ("Grass") mode** — restore/seed grass over the selected footprint, with **NO terrain raise/level/cultivate at ANY width**. The three widths mirror the path widths for a consistent three-width UX (Daniel, playtest 2026-06-05). Each width scales **only the grass/paint footprint** (`TerrainOp.m_settings.m_paintRadius`); `m_level`/`m_smooth`/`m_raise` are never set (left at the `Settings` default `false`) so no width can flatten or smooth terrain. ⚠️ **Correction (Daniel, playtest 2026-06-04) — still in force:** this is NOT the `cultivate` (soil-tiller) op. An earlier build cloned `cultivate` at a forced 5m radius — an "UBER level" that flattened/cultivated a huge area. PR #16 fixed it; this slice extends that fix to three grass-restore widths WITHOUT reintroducing terrain modification (the level/smooth/raise fields are never written). 🔴 **Mechanism (v0.2.17, card t_6fc9b3fa — supersedes the legacy-`replant`-clone design):** the op is built **additively** as a modern `TerrainOp` piece (mirroring vanilla `replant_v2`), NOT by cloning the legacy `replant` `TerrainModifier`. A `TerrainOp` is fire-and-forget (no ZNetView; bakes paint into the per-zone `TerrainComp` then self-destructs), so a grass op and a path op **coexist on one tile, last-applied-wins**, exactly like the vanilla Cultivator-grass ↔ Hoe-path relationship — fixing the "grass fights path" bug. See the impl-surface below + ADR-0006. |
| **ClearVegetation** | Removes existing vegetation along the laid path (small brush, grass, mushrooms — NOT trees) so the path is *visually a path*, not a stripe through bushes. **Deferred to v0.2.0** (see playtest limitations). |
| **Implementation surface** | Likely a new item class analogous to `Hoe`, with custom `m_operations` array entries for the three widths + cultivate-replant + clear-vegetation. May patch `Hoe` directly OR introduce a new `TrailblazerTool` MonoBehaviour. Decision for spec-writer. |

#### A2.3 — Explorer's Bench (LOCKED — kitbash for playtest)

**v1 approach:** kitbash the vanilla Workbench. Tier 1 reuse — vanilla Workbench mesh + Trailborne material tint + visual props (half-rolled hide-map + bone-needle-in-stone-disk per `design/nomap.md` §1 + antlers from Deer Trophy visually integrated into the bench mesh). Trailborne recipes register as new tabs on the Explorer's Bench (its own CraftingStation, NOT the vanilla Workbench). **Its CraftingStation must set `m_showBasicRecipies = false`** — the vanilla Workbench is the only station that ships this `true`, and it's what surfaces the stationless "basic" hand-craft recipes (Club, Torch, Stone Axe, Hammer, Hoe, …); a raw clone inherits `true` and wrongly offers all of them (bugfix 2026-06-04, card t_30f97042).

**v1.1+ path:** graduate to a visually-distinct mesh once mechanics validate. Retains thematic anchor (own recipe, own discovery moment).

**Recipe (LOCKED, Daniel 2026-06-03):** 10 Wood + 4 Stone + 1 Deer Trophy. No raspberries, no resin. See the dedicated Explorer's Bench section below for full detail.

#### A2.4 — Path Lamps (LOCKED, added in this round per Daniel)

**Recipe:** Wood + Resin (per parked doc; exact quantities TBD).

| Mechanic | Detail |
|---|---|
| **Light source** | Passive — like vanilla torch but slightly **dimmer** (trail-illumination, not base-illumination) |
| **Fuel duration** | **Longer** than vanilla torch (so a string of them doesn't become a refuel chore — *evidence-of-trail* shape rather than *maintenance burden*) |
| **Chain ignition** | Walking close to a lit Path Lamp with an unlit Path Lamp in proximity should light the unlit one (gives the satisfying "lighting the path home" moment without manual torch-by-torch interaction) — OPEN: Daniel to confirm |
| **Implementation surface** | Likely Tier 1 reuse — vanilla `Torch` prefab + custom light intensity + custom fuel rate + custom recipe. Chain-ignition would require a small `MonoBehaviour` on the lamp that polls for nearby lit lamps. |
| **Visual (playtest)** | Kitbash: vanilla torch model + dimmer light intensity reflection + (optional) pigment-tinted flame via runtime ParticleSystem property edit |

#### A2.5 — Pigments (LOCKED per parked doc)

| Aspect | Locked value |
|---|---|
| **Colors** | Red, White, Black, Blue (4 basic pigments) |
| **Display names** | `Red Pigment` / `White Pigment` / `Black Pigment` / `Blue Pigment` (canonical "Pigment" naming — unified 2026-06-07) |
| **Prefab names** | `SBPR_InkRed` / `SBPR_InkWhite` / `SBPR_InkBlue` / `SBPR_InkBlack` — **unchanged save/wire contract** (placed signs/cairns store these); only display + code identifiers say "Pigment" |
| **Output per craft** | 2 pigments per craft |
| **Stack size** | 20 |
| **Weight** | 0.1 |
| **Recipe inputs** | Red ← 1 Raspberry, White ← 1 BoneFragments, Blue ← 1 Blueberries, Black ← 1 Coal |
| **Craft station** | Explorer's Bench (v1 = vanilla Workbench kitbash) |

#### A2.6 — Painted Signs (LOCKED — single combined Paint+Text panel, two-tone, Daniel 2026-06-05)

> **SUPERSEDES the 2026-06-04 "single-color, apply-ink-item, no UI" lock**, which
> itself superseded the older "E=text color / Shift+E=accent / two-tone pin" model.
> Daniel re-locked this on 2026-06-05 from a UI mockup. Two deliberate reversals of
> the 6/04 lock: **(1) a real UI panel returns** (replaces apply-ink-item), and
> **(2) two-tone returns** — a sign now carries a **text color AND a border color**.
> Still ONE buildable sign piece (the four-variant sprawl stays dropped).

> **⚠️ THREE-SLOT model (Daniel 2026-06-21) — supersedes the 6/05 two-tone lock
> AND the interim 6/21 "border tints board+frame" re-wire.** A sign now carries
> THREE independent color slots, each tinting exactly one surface:
> **Text Color → the written letters; Board Color → the board plank; Border Color →
> the frame bars.** All three are independent — e.g. white board, red frame, blue
> letters. The panel grows from two swatch rows to three; cost is still ONE pigment
> per FILLED slot (so a full three-color sign = 3 pigments; same color in N slots =
> N of that pigment); every slot is individually optional; ≥1 required. ZDO gains a
> third field `SBPR_SignBoardColor`. Legacy single-color saves (`SBPR_SignColor`)
> migrate to the BOARD slot (that color tinted the plank). Table cells below are
> annotated `[6/21-3slot]` where this applies. **Design rationale:** a frame that
> shares the board's tone is invisible as a frame (no edge contrast) — independent
> slots let the frame differ from the board (ideally by lightness, not hue alone).

A single panel handles **both** painting and text, opened by interacting with a
placed sign (replaces the vanilla text dialog). Layout (from Daniel's mockup):

```
--- PAINTING ---
 Set Text Color:  [∅ None][Red][Blue][Black][White]   (letters — only DISCOVERED pigments render)
 Board Color:     [∅ None][Red][Blue][Black][White]   (board plank)
 Border Color:    [∅ None][Red][Blue][Black][White]   (frame bars — ∅ None = explicit clear)
 Cost:            <icon> Red Pigment    1/1
                  <icon> White Pigment  0/1   (red while short)
 { Paint this and consume }
--- TEXT ---
 [ text field ]   (enabled only once a paint color is chosen)
 { Update Text }   { Close }
```

| Aspect | Locked value |
|---|---|
| **Base** | ONE buildable piece (`piece_sbpr_sign`), variant of the vanilla wood sign. Placed **UNPAINTED** (plain wood). Build cost **2 Wood** (pigment is NOT a build ingredient) |
| **Panel** | Interacting with a placed sign opens the **combined Painted Sign panel** (custom uGUI built on Unity **layout groups**), NOT the vanilla text dialog. Two sections: PAINTING + TEXT. Rebuilt each open so swatch rows reflect current discovery |
| **Set Text Color** | Swatch row — an explicit **`∅ None`** tile (clears the slot) followed by one swatch per **DISCOVERED** pigment (discovery = *ever-discovered material OR known recipe OR owned*; primary signal `IsKnownMaterial`, persistent so swatches don't flicker on last-unit spend). Undiscovered pigments are **NOT rendered** (no dead/unclickable reserved boxes). **[6/21-3slot]** Sets the **text** tint — colours **ONLY the written letters** (`Sign.m_textWidget`). |
| **Board Color** | **[6/21-3slot]** Second swatch row, same `∅ None` + discovered-only swatches. Sets the **board plank** mesh tint, **independent** of letters and frame. `None` reverts the plank to plain wood. |
| **Border Color** | Third swatch row, same `∅ None` + discovered-only swatches. **[6/21-3slot]** Sets the **border frame** bars' tint, **independent** of board and letters. `None` reverts the frame to plain wood. |
| **Cost** | **Crafting-style requirement rows** (replicates `InventoryGui`'s recipe-requirement idiom): per pigment an **icon + pigment name + `have/need` count**, the count flashing **red while short**. **[6/21-3slot]** One pigment per FILLED color slot across all three (text Red + board White + border White → 1 Red + 2 White). Same color in N slots = **N of that pigment**. Every slot is **individually optional**; **at least one** color required |
| **`{ Paint this and consume }`** | Commits painting: removes exactly the displayed pigments from inventory, **[6/21-3slot]** tints **letters = text color** + **board plank = board color** + **border frame = border color** (three independent slots), writes all three to ZDO. **Disabled** unless the player holds the required pigments. **Re-painting later re-consumes** |
| **`{ Update Text }`** | Commits the text. **Free** (no pigment cost — Cost applies to PAINTING only). Text field is **locked until ≥1 paint color is chosen** |
| **Camera** | While the panel is open, **mouse-look is frozen** and the cursor is released, matching every vanilla full-screen GUI. Achieved by routing through vanilla's own suppression gate (`PlayerController.TakeInput` → false while open — the same gate the vanilla sign dialog used, which our panel bypasses by replacing that dialog), NOT by overriding `GameCamera.UpdateCamera` |
| **Color persistence** | Per-instance ZDO: `SBPR_SignTextColor`, `SBPR_SignBoardColor`, `SBPR_SignBorderColor` (`""` = unset) + vanilla text. Persists across reloads, syncs to clients, all three tints (text widget + board + border) re-applied on spawn (mirrors `CairnTag`). Legacy `SBPR_SignColor` (pre-three-slot single color) migrates one-way into `SBPR_SignBoardColor` on spawn |
| **Naming** | The items are **Pigments** — display names `Red/White/Blue/Black Pigment`, code identifiers `Pigments.Pigment*Name` / `Signs.PigmentForColor`. The prefab-name VALUES stay `SBPR_Ink*` (save/wire contract — renaming would orphan placed signs/cairns); only player- and code-facing strings say "Pigment" |
| **Pin (deferred)** | Minimap pin path stays **unregistered** for v0.1.0 (follow-up). If later registered, the pin reflects the **text** color when `nomap=OFF`; no-op if `nomap=ON` |
| **Implementation surface** | Custom uGUI panel (`SignPaintPanel`) replacing the vanilla `Sign` text dialog (`SignInteractPatch` intercepts `Sign.Interact`). Backend `SignPaintBackend` drives economy + commit; `SignTag` owns ZDO + re-tint on spawn; `Signs.TintBoard`/`TintText`/`TintBorder` (+ `RestoreBoard`/`RestoreBorder` for the None affordances) do the visuals. **Board/border tint = per-renderer `MaterialPropertyBlock` (MPB) `_Color` override** — the render-time layer vanilla itself paints build-piece colour through (`MaterialMan`/`WearNTear.Highlight`); the earlier `sharedMaterials.SetColor` + `SignTintBackup` mechanism wrote to a layer the piece's MPB sits in front of, so only the TMP text (a Canvas renderer outside `MaterialMan`) ever changed (fix t_f3310406 / diagnosis t_24ad2570). Per-renderer (not per-`GameObject` `MaterialMan.SetValue`) keeps board vs the child-of-board border independent for two-tone (§A2.6); `None` reverts a renderer to its material's own `_Color` (no clone to restore). The hammer support-overlay (`WearNTear.Highlight`→`ResetHighlight`) is the one thing that clobbers our `_Color` MPB, so `SignMeshRetintPatch` (postfix on `WearNTear.Highlight`, gated to `SignTag`) debounces a one-shot `SignTag.ReapplyMeshTint` re-assert ~0.3s after hover ends — the mesh-layer twin of `SignTextRetintPatch`. Two-tone border = kitbashed `SBPR_SignBorder` element (separate material). Owner-write via `ZNetView` (mirrors `CairnTag`). **CLIENT-SIDE surface — cannot be proven headless.** |

> **Rebuild note (2026-06-07, Daniel playtest with screenshots):** the original
> hand-built panel (raw `UnityEngine.UI` primitives + hand-computed `y -=` offsets)
> shipped with defects — 6 dead reserved swatches that couldn't be clicked, an
> invisible "remove border" affordance, no "remove text color", text color that
> never reached the letters (`TintBoard` only tinted the plank, never
> `Sign.m_textWidget`), a custom "icon ×N" cost row instead of the crafting idiom,
> the camera not locking while the panel was open, and inconsistent alignment. This
> revision rebuilds the panel on Unity **layout groups**, renders **only discovered
> pigments** plus an **explicit `None`** on both rows, drives the **TMP text widget**
> from the text color, replicates the **crafting-UI cost rows**, and **locks the
> camera** through vanilla's own input gate. "Pigment" naming is unified across the
> UI and code. ONE buildable sign piece, two-tone, unchanged.
>
> **RESOLVED (Daniel 2026-06-07) — "discovered" definition.** A pigment swatch renders
> only when the pigment is discovered, defined as: **ever-discovered material OR known
> recipe OR currently owned** — `Player.IsKnownMaterial(name) || Player.IsRecipeKnown(name)
> || CountPigment > 0`. The PRIMARY signal is `IsKnownMaterial` (vanilla's persistent
> material-discovery set, populated by `AddKnownItem` on first pickup and never cleared),
> so a swatch does NOT flicker away when the player spends their last unit. Recipe-known
> and owned are belt-and-braces fallbacks. Note SBPR pigments set
> `m_shared.m_name = displayName`, so the display name is the correct key for both
> vanilla knowledge sets. Code: `SignPaintBackend.IsPigmentDiscovered`.
>
> **§A2.6 — Border visual form: TRUE THIN FRAME (Option A, ratified Daniel 2026-06-09).**
> Reverses the original `KitbashBorderElement` mesh-reuse construction. The original built
> the rim by REUSING the board's full plank `sharedMesh` scaled ~1.10× in-plane / 0.85× on
> depth, kept concentric. **A solid plank mesh scaled near 1.0 is still a solid plank —
> never a frame** — so it rendered as a full second board poking ~5% past every edge (the
> "multiple board elements" seen in playtest). The ratified form is REAL frame geometry: a
> **rim WITH A HOLE**, not a scaled board copy.
>
> **Construction (engineer's choice of two; implemented as A1):**
> - **(A1, recommended — IMPLEMENTED)** FOUR thin bars (top / bottom / left / right) laid
>   around the board's face silhouette, all parented under one `SBPR_SignBorder` GameObject.
> - **(A2)** ONE procedural picture-frame mesh — outer rectangle MINUS inner rectangle (a
>   flat rectangular ring) — as a single `SBPR_SignBorder` renderer.
>
> Either way the geometry MUST be a rim-with-a-hole so it can **never regress to a second
> board**: the board shows through the central cutout; the frame occupies only the edge band.
>
> **Geometry rules (parametric — exact dims are v0.2+ polish, tunable in playtest):**
> - Built in the board's **LOCAL face plane, parented under the board transform** → inherits
>   board orientation automatically (orientation-robust; no front/back detection needed for
>   correctness).
> - Board face dimensions measured from **TRANSFORMED extents** (`Runtime/Assets.MeasureLocalExtent`
>   over all 3 axes; two largest = face plane, smallest = depth). **MUST NOT use raw
>   `sharedMesh.bounds`** — the plank is a scaled 1×1×1 unit cube, so raw bounds ≈ (1,1,1)
>   carry no real dimensions (this raw-bounds read is the original root cause — see below).
> - Inner cutout ≥ the board's text/face area, so the frame **RINGS the text and never covers
>   it**.
> - Frame rim width (visible band): default ~10% of the smaller face dimension (auto-scales to
>   the real board); clamp to a min so it's visible at readable distance but stays thin, and a
>   max half-fraction so a central hole always remains. Exact value tunable.
> - Frame depth: thin slab a multiple of the board thickness (`FrameDepthFactor`, v0.2.10 = 1.12×
>   after the playtest below), flush to / within the board's depth envelope;
>   **MUST NOT protrude forward past the text plane**.
> - Frame outer edge: pulled IN from the board silhouette by a small world inset (`FrameOuterInset`,
>   v0.2.10 = 4 mm) so the bar faces are NOT coplanar with the plank perimeter — a hair of bare-board
>   lip is fine and is what kills the outer-edge z-fight (below). A large overhang is the "second
>   board" failure and is forbidden; being strictly INSIDE the silhouette hardens that invariant.
>
> **Outer-edge z-fight fix (v0.2.10, t_153ca109 — Daniel playtest "borders z-fighting on the outer
> edges"):** two coplanarities caused the shimmer, both at the rim's outer band. (1) The bars' OUTER
> silhouette was flush with (coplanar to) the board's perimeter side faces — a depth-axis knob can
> NEVER separate two faces coplanar in the FACE plane, so this is fixed by `FrameOuterInset` pulling
> all four outer edges a few mm inside the silhouette. (2) The rim stood only ~2.7 mm proud per face
> at the old `FrameDepthFactor` 1.06× — too shallow to depth-sort the front/back at distance —
> fixed by raising it to 1.12× (~5.3 mm proud, still short of the text plane). Both are parametric,
> in-game-tunable knobs; exact values are v0.2+ polish. The board still shows through the central
> cutout (AC1) and the rim never rings inward over text (AC4).
>
> **Tint path (UNCHANGED — two-tone preserved):** the frame lives under a GameObject named
> `SBPR_SignBorder` (`Signs.BorderChildName`). `Signs.TintBorder` tints only renderers under
> that subtree (`IsUnderBorder` name-match); `Signs.TintBoard` skips it. Text=Red + border=White
> → red board + red letters + white frame. Unpainted frame = plain wood (own material instance
> copied from the board).
>
> **Clean-room:** procedural `new Mesh()` / `new GameObject()` + scaled primitive children only
> (ADR-0006 additive). Reading the vanilla `sign` plank as a blueprint (mesh/material/extents
> via `vprefab inspect` / `GetPrefab`) is permitted; copying IronGate source is not. (The
> implementation reuses the board's own unit-cube `sharedMesh` scaled into edge bands — no
> authored geometry, headless-safe, no collider.)
>
> **Root-cause note (why the original drifted):** the old border read its depth-axis from raw
> `sharedMesh.bounds` (≈ unit cube → meaningless), so the "depth shrink" landed on an arbitrary
> axis and the copy grew in a FACE direction → second board. The procedural frame eliminates this
> failure class **structurally**: a rim-with-a-hole has no solid center to become a second board
> even if axis detection were imperfect. (The sibling standoff feature already learned to use
> transformed extents; the frame builder reuses the same `MeasureLocalExtent` path.)
>
> **SpecCheck:** border geometry is NOT in the drift manifest (`SpecCheck.cs` tracks
> `piece_sbpr_sign` = Wood×2 only) → zero manifest impact.
>
> **Coupling with `sbpr-sign-repaint-color` (t_b7808fc1):** the oversized border is the suspected
> PERCEPTUAL cause of "repaint doesn't recolor" — the second board masks the real plank, so a new
> text/board color re-tints the (occluded) plank while the player keeps seeing the unchanged
> border. The tint code paths are independent (no shared defect); the coupling is visual masking.
> **Sequencing: land the frame fix FIRST; QA t_6e3cf19c observes whether the recolor perception
> resolves for free. Do NOT couple them in code.** Treated as likely-one-fix but closed empirically
> by QA.
>
> **Acceptance criteria (AC1–AC7) — reviewer-read invariants + in-game QA (no test project in repo):**
> - **AC1 — One board + one thin frame.** Exactly one plank + a thin distinct rim; no oversized/overlapping second-board surface.
> - **AC2 — Two-tone preserved.** Border independently tintable from the board; text=Red + border=White → red board + red letters + white frame.
> - **AC3 — Unpainted = plain wood.** Frame reads as plain wood before any paint (own material copied from board); no stray colored element pre-paint.
> - **AC4 — Never occludes board face or text.** Frame rings the text, never sits in front of the plank face or the TMP letters, at any sign orientation.
> - **AC5 — Visible as a frame at readable distance.** Not a hairline, not a second board.
> - **AC6 — Clean build.** `dotnet build -c Release` → 0 errors, **0 warnings** (`TreatWarningsAsErrors`, net48, Nullable=enable).
> - **AC7 — Logs-green ≠ playable.** Real close is Daniel seeing one-board-plus-thin-frame in-game (QA t_6e3cf19c).
>
> **Panel chrome — reuse vanilla UI sprites + font (2026-06-09, t_b47035e7 — Daniel playtest "the UI background and borders still don't look very valheimy, can we reuse the style assets from the game? ditto on font choices").** The panel's *layout/function* (above) is unchanged; this is a SKIN pass on its chrome. The combined Paint+Text panel no longer approximates the wood-panel look with hand-picked flat colours — it wears the **actual vanilla UI assets**, harvested at runtime from live vanilla GUI donors (the sign dialog we replace, `InventoryGui`, `StoreGui`) by `Features/Signs/VanillaUISkin.cs`:
> - **Background + frame:** the vanilla 9-sliced wood-panel sprite (carved frame baked into the panel sprite, as vanilla dialogs do — not a separately-layered frame).
> - **Buttons:** the vanilla carved-wood button sprite + its `SpriteState` (hover/pressed/disabled), so action buttons and the `∅ None` swatch tiles get native look + working hover states. Colour swatch tiles deliberately keep a flat pigment fill (the colour IS their content; a wood tint would muddy it).
> - **Text:** the legacy `Font` underlying vanilla's TMP display face (`TMP_FontAsset.sourceFontFile`), so the legacy `UnityEngine.UI.Text` widgets render in the game's Norse face instead of an Arial fallback.
> - **Clean-room:** reading/reusing vanilla UI sprite + font *references* at runtime is clean-side (ADR-0001 clarification 2026-06-09: the firewall is around other mods, never vanilla). No asset files are copied/exported/committed — same model as reusing vanilla meshes/materials for content.
> - **Graceful degradation:** if a donor isn't present the panel falls back to the prior flat-colour primitives; the skin is additive, never load-bearing for function. No recipe/piece change → SpecCheck untouched. Real close is Daniel's eyes in-game (AT-UI-PARITY).
> - **Interactive-text legibility — contrast tracks the chrome (t_f2fe06d4, Daniel playtest screenshot).** The harvested vanilla button + input-frame sprites are *light* carved wood, so the LOWER interactive panel (the typed sign text, its placeholder, and the `Update Text` / `Close` / `Paint this and consume` button labels) draws **dark text on the skinned light chrome**, and keeps the original **light text on the dark flat fallback fills** — never light-on-light or dark-on-dark, in both enabled and disabled button states, and with the placeholder always visibly dimmer than typed text. The colour is chosen from the *actual* skin result at build time (each `SkinButton`/`SkinPanel` return + sprite identity), not assumed. The UPPER panel (title, colour labels, pigment/cost rows) sits on the dark window backing and keeps its existing light parchment colours — do not restyle it. Contrast direction only; the sprite-harvesting mechanism is unchanged.

---

### Round 3 — Open mechanical questions ✅ CLOSED (Daniel's answers + repo + log re-check)

**Re-baseline note (corrected 2nd pass):** Two parallel sources of truth need cross-checking, NOT just the repo:
1. **Committed repo docs** (`PLAYER_GUIDE.md`, `design/*.md`, `README.md`) — but young, may lag behind chat decisions
2. **Recent chat decisions** (this Discord conversation, especially the prior session that established Trailborne naming, Explorer's Bench rename, and other refinements) — authoritative when they supersede repo docs, but only durable if captured to disk

The crafting station **was renamed from "Orienteering Table" → "Explorer's Bench"** in last night's Discord conversation (confirmed via session DB at id 37430 vicinity). The rename never propagated into PLAYER_GUIDE.md or design/nomap.md. When Starbright re-read those files this morning, she reverted to the older repo name. **Explorer's Bench is correct.** PLAYER_GUIDE.md + design/nomap.md need a doc-PR for the rename.

Skill lesson (already patched): cross-check repo docs AND recent chat decisions; capture chat-decisions to disk same-day or they rot.

#### A3.1 — Cairn build cost ✅ LOCKED (Daniel today)

Daniel: "the build cost for a cairn is 3 stone, 1 resin and one pre-made cairn marker. upgrade cost is always 3s + 1r"

**Cairn recipe (v1, locked — 2026-06-04 ladder update):**
- Initial build (Tier 1): **9 Stone + 1 Resin + 1 Cairn Marker (pre-crafted item)**
- Per-tier stone build cost (cumulative ladder, flat +3 per tier): **T1=9 / T2=12 / T3=15 / T4=18 / T5=21**
- Comfort floor per tier: **T1=3 / T2=4 / T3=5 / T4=6 / T5=7**
- Upgrade / repair gesture (combo E-press): **3 Stone + 1 Resin** flat per use, gated on HP <75%. Always repairs to max; if tier<5, simultaneously upgrades to tier+1. One-press, outcome state-dependent.
- **Damage immunity (LOCKED):** cairns are immune to player + monster damage (Harmony-prefix on `WearNTear.Damage(HitData)`, the combat path only). Combat cannot grief cairns; only abandonment can. Time decay does NOT flow through `Damage(HitData)` — it calls `WearNTear.ApplyDamage(float)` directly — so the immunity prefix and decay are cleanly independent.
- **Time decay (LOCKED — REVISED 2026-06-09, root-cause t_a185a3cd/t_22fdcca2):** decay is driven by TIME/abandonment, NOT weather. Two paths share ONE clock and ONE rate so they never double-count:
  - **Resident ticker** — the primary source. `CairnTag.HpBracketTick` (the existing 1 Hz owner poll) accrues elapsed in-game time and subtracts HP via vanilla `WearNTear.ApplyDamage(float)` while the cairn is loaded, so HP falls in-zone in **any** weather (clear or storm). NO 5% floor: a tier-1 cairn reaches 0% and collapses live through vanilla's own `ApplyDamage→Destroy` path (DropResources + fragments).
  - **Out-of-zone backfill** — `WearNTear.Awake` Harmony postfix backfills time missed while the chunk was unloaded, same rate, **keeps a 5% floor** as reload safety (never collapses a cairn sight-unseen on load).
  - **Shared clock:** ZDO-persisted `SBPR_LastWearTick`, measured in **in-game days** = `EnvMan.m_dayLengthSec` (vanilla 1200s/day) — *not* `/86400`, which made the prior backfill ~72× too slow.
  - **Rate:** `SBPR_CairnDecayHpPerDay` BepInEx config (default **10 HP/in-game-day** = a ~10-day life vs the 100 HP cairn). Set 0 for weather-only.
  - **Weather as accelerant:** vanilla wet `UpdateWear` already runs (`m_noRoofWear=true`) and stacks on top during genuinely wet weather ("rots faster in rain") — but it gates at `>50% HP`, so it can *never* be the primary decay source. (O3: weather-as-accelerant vs weather-only is a Daniel call tracked on parent t_a185a3cd; default ships as accelerant.)
  - Why the rewrite: a non-wet storm contributed exactly 0 decay and the mod added nothing of its own, so a resident cairn sat pinned at 100% HP ("100% HP in a storm"). The original card blamed the `Damage(HitData)` immunity prefix; that was DISPROVEN from vanilla source — weather decay never reaches `Damage(HitData)`. The real defect was the missing resident ticker.
- **Shift+E debug-damage (v0.1.0 only):** `SBPR_DebugCairnDamage` BepInEx config (default `true`). With a pristine cairn (≥75% HP), Shift+E drops it to ~70% so the combo gesture is exercisable without waiting on weather. Flip false or remove in v0.2.0 once natural decay is tuned.
- Repair: **3 Stone + 1 Resin** (flat, matches upgrade — confirmed from PARKED doc)
- **Visual identity (REVISED 2026-06-07 — the prior "non-burning" lock was wrong, Daniel):** a Cairn shows a **cosmetic fire** at pristine — **flame VFX + fire SFX + a small Light (intensity/range clearly BELOW a vanilla torch)** — but the fire grants **NO heat and consumes NO fuel**. It is cloned from the vanilla `bonfire` prefab; on the client the donor fire is CONFIGURED into a cosmetic fire by component type: `Fireplace` is KEPT but forced `m_infiniteFuel=true` with fuel knobs zeroed (eternal, fuel-less, no 'add fuel' hover); the flame `ParticleSystem`(s) + fire `AudioSource`/`ZSFX` are KEPT (the flame + crackle); ONE `Light` is kept and dimmed below a torch; `EffectArea` is DISABLED (no heat); `SmokeSpawner` is DISABLED. The donor mesh logs are hidden and a runtime stack of `Pickable_Stone` clones (T1=9 → T5=21 stones) shows instead. The cosmetic fire is HP-gated: **lit at ≥75% HP (pristine), OUT below** — so "fizzled" reads as the fire going out, and repair-to-pristine relights it. Comfort comes from the `SE_Rested` patch, NOT from fire. See `Features/Cairns/CairnTag.cs` (`ConfigureCosmeticFire` / `ReconcileFire`). **Open-air comfort without heat (BUG-FIX 2026-06-13, t_4c5b5b2d):** the "no `EffectArea` / not a heat source" lock STANDS — the cairn still emits no heat. Open-air **Rested** is granted by riding vanilla's own heat-free `Resting → SE_Cozy (10 s) → Rested` ramp (a postfix on `Player.UpdateEnvStatusEffects` seeds `Resting`; a paired prefix on `SEMan.RemoveStatusEffect` suppresses vanilla's per-tick `Resting` strip while qualifying so the ~10 s dwell elapses), NOT by re-introducing a Heat `EffectArea` and never resetting `m_nearFireTimer`. Campfire-parity *timing* (Daniel, 2026-06-13), campfire-parity *comfort*, but still not a fire. See `Features/Cairns/CairnComfortRestedPatch.cs`.

**New item introduced: Cairn Marker.** This is a pre-crafted consumable item (not a piece) used as the build ingredient for the base cairn. Recipe TBD — needs a Round 3.5 question. Likely crafted at Explorer's Bench. Thematic: the "marker" is what you carry out to plant a new cairn somewhere, after which you stack stones around it on-site (the cairn is built around a planted marker, not from raw stones alone).

#### A3.2 — Blue pigment Meadows-availability ✅ LOCKED

Daniel: "no, blueberries it is. V1"

**Pigment recipes (v1, locked):**
- Red: 1 raspberry → 2 red pigment
- White: 1 bone fragment → 2 white pigment
- Black: 1 coal → 2 black pigment
- Blue: 1 blueberry → 2 blue pigment

v1 effectively spans Meadows through early Black Forest for pigment ladder. Yellow (cloudberry, Plains) is v5+, not v1.

#### A3.3 — Path Lamp chain-ignition ✅ DROPPED

Daniel: "this isn't really a thing we discussed"

Starbright-hallucinated mechanic, removed. v1 Path Lamps: manual ignition, no chain effect.

#### A3.4 — Trailblazer's Spade recipe ✅ LOCKED

Daniel: "Leather Hides not scraps. Flint, not stone. So 5w/2f/2h"

**Trailblazer's Spade recipe (v1, locked):** 5 Wood + 2 Flint + 2 Leather Hides
**Crafted at:** Explorer's Bench

#### A3.5 — Cairn resin re-ignite on repair ✅ LOCKED

Daniel: "it reignites if the cairn is in the 'pristine' piece state rather than the lower tiers of wear and tear. 75% threshold as discussed to 'fizzle out'"

**Cairn resin glow mechanic (v1, locked):**
- **≥75% HP** = pristine, resin glows (visual)
- **<75% HP** = fizzled, no glow (visual maintenance signal)
- **<25% HP** = downgrade tier (per PARKED-2026-06-03.md)
- **0% HP** = collapse (per PARKED-2026-06-03.md)
- Re-ignite: AUTOMATIC when HP returns to ≥75% via repair. No player action required.
- Implementation: postfix `WearNTear.OnDamage`/`OnRepair` to toggle `ParticleSystem.emission.enabled` based on HP threshold.

#### A3.7 — Path Lamps wood material ✅ LOCKED

Daniel: "I think corewood still tracks"

**Path Lamps recipe (v1, locked — 2026-06-04 update):** **3 Wood + 2 Resin** (downshifted from Corewood to plain Wood per Daniel's morning playtest pass — Meadows-tier accessibility wins over the Black Forest gate; visual remains a slim post topped with a resin-fueled flame).
- Path Lamps are now squarely Meadows-tier — no Black Forest material gate. Pure trail discipline, available the moment a player has a workbench.
- Consistent with PLAYER_GUIDE.md line 110: "3m corewood torches, resin-fueled, long burn"

#### A3.8 — Ember Lamps in v1 ✅ DROPPED FROM v1

Daniel: "No"

**Decision:** Ember Lamps are NOT in v1. They move to v1.1 (or a later release). Keeps v1 scope tight on the Path Lamps tier; Ember Lamps + Beacons come together later.

#### A3.9 — Spade path stamina is flat 2, radius-independent ✅ LOCKED

Daniel (2026-06-04 playtest): "Pathing is supposed to only be 2 stamina with the spade regardless of size."

**Spade path/replant stamina (v1, locked):** **Flat 2 stamina per op for ALL widths** — 1.5m, 3m, and 5m each drain exactly 2. Stamina does NOT scale with radius.

- **Why the tool, not the op piece:** terrain-op / build stamina is driven by the *wielding tool*, not the placed op. `Player.GetBuildStamina()` returns the right-hand `ItemDrop`'s `m_shared.m_attack.m_attackStamina`. `Piece` / `TerrainModifier` carry no stamina field, so per-variant pinning is impossible; the only correct layer is the spade item itself. (Cross-ref `design/nomap.md` §2, which already recommended setting `m_attackStamina` on the tool.)
- **Implementation:** `Trailblazing.RegisterSpadeItemPrefab` sets `m_shared.m_attack.m_attackStamina = 2f` (and `m_secondaryAttack` to match) on the cloned spade. Because the spade's `SharedData`/`Attack` are `[Serializable]` and deep-copied by `Instantiate`, this does not mutate the vanilla Hoe.
- Radius-independence is structural: a single scalar on the tool cannot vary by op width.

---

### EXPLORER'S BENCH (LOCKED)

| Aspect | Value |
|---|---|
| Name | **Explorer's Bench** |
| Function | Crafting hub for all Trailborne pieces + Trailborne items (Trailblazer's Spade, Cairn Markers, Pigments, Painted Signs, Path Lamps) |
| Piece category | `PieceCategory.Crafting` |
| v1 implementation | Kitbash vanilla Workbench. Tier 1 reuse — vanilla Workbench mesh + Trailborne material tint + **antlers from the Deer Trophy visually integrated into the bench art itself** (NOT mounted on top as a trophy decoration — the antler shapes are part of the bench's structure: carved cups, leg supports, pen-holders, etc.; final composition deferred to visual-design stage) + half-rolled hide-map and bone needle stuck in a stone disk (per `design/nomap.md` §1 prop hint) |
| v1 recipe (LOCKED, Daniel 2026-06-03) | **10 Wood + 4 Stone + 1 Deer Trophy.** No raspberries. No resin. No bone fragments. No greydwarf eyes. No deer hide. (Earlier brainstorms in `design/nomap.md` §1 and prose in `PLAYER_GUIDE.md` lines 58-60 implied other ingredients; this recipe supersedes them and both docs have been updated to match.) |
| Patch surface | Pure prefab work. Clone `piece_workbench` → name `SBPR_ExplorersBench`. Add `CraftingStation` component with `m_name = "$sbpr_piece_explorers_bench"` and **`m_showBasicRecipies = false`** (the Workbench is the only vanilla station that ships this `true`; it's what surfaces the stationless basic hand-craft recipes — Club, Torch, Stone Axe, Hammer, Hoe — so a raw clone wrongly offers them; bugfix t_30f97042). Visual integration of antler shapes into the bench mesh is a kitbash / material composition task — NOT attaching the vanilla `TrophyDeer` prefab as a child. The antlers should *be part of the bench*, not sit *on* the bench. **After cloning, also strip the inherited `GuidePoint` component** — the vanilla Workbench prefab carries one (the proximity hook that makes Hugin pop the "you built a workbench" tutorial); the clone inherits it and Hugin wrongly greets the Explorer's Bench as a Workbench. The bench is its own station, so it must carry no Workbench tutorial hook (bugfix 2026-06-04, card t_53ab3232). |
| v1.1+ path | Graduate to visually-distinct mesh once mechanics validate. |

---

### CAIRN MARKER (LOCKED — pre-crafted item, gates Cairn construction)

| Aspect | Value |
|---|---|
| Name | Cairn Marker |
| Type | `ItemDrop` (consumable item, used as build-ingredient for Cairn pieces) |
| Recipe (Daniel today) | **2 Leather Scraps + 1 Finewood + 1 Pigment (player's color choice)** |
| Crafted at | Explorer's Bench |
| Function | Required ingredient for Cairn initial-build (1 Cairn Marker + 3 Stone + 1 Resin → Tier 1 Cairn). Consumed on placement. |
| Color-binding | The Pigment color used to craft the Marker IS the color the placed Cairn takes. The marker is what carries the cairn's color/banner identity from craft-time to plant-time. *Pigment+banner persist across rebuilds* (per PARKED-2026-06-03.md) implies the Cairn ZDO remembers its initial-marker color even after collapse/rebuild. |
| Thematic | The "marker" is the trail-claiming artifact you carry out into the wilderness. Stones-around-a-planted-marker is the cairn assembly mental model — you don't build a cairn from raw stones alone, you build it *around something you brought*. |
| Stack size | TBD — likely 10 (matches similar consumables like Surtling Core / Greydwarf Eye stacking shape) |
| Weight | TBD — likely 0.5 |
| Patch surface | None — pure ObjectDB registration. Recipe registers via standard `Recipe` ScriptableObject pattern. Cairn `Piece.m_resources` declares 1 `ItemDrop.ItemData` of type `Item_CairnMarker` as a required ingredient. |

---

### Round 4 — Reusability scan against decomp + wiki (NEXT)

Leveraging `design/nomap.md` line-references (Minimap, Hammer/Hoe, Sign, Fireplace, TeleportWorld, ZoneSystem, ObjectDB already mapped). Additional scans needed:
- `WearNTear` (cairn resin glow + decay)
- `SE_Rested.CalculateComfortLevel` (cairn comfort patch)
- `MapTable` (v1 disable mechanism)
- Wiki: Raspberries, Bone fragments, Coal, Resin, Blueberries (pigment input biome confirmation)
- Wiki: Banner (cairn comfort comparison)
- Wiki: Cartography Table (disable surface)
- Wiki: Torch (Path Lamp Tier 1 reuse pattern + fuel mechanics)

---

### Round 5 — Visual assets *(NOT YET ASKED)*

### Round 6 — Scope boundaries / out-of-scope *(NOT YET ASKED)*

---

### Round 3.5 — Single remaining open question

**Q3.9 — Cairn Marker recipe:** Daniel introduced "Cairn Marker" as a pre-crafted item required to build a cairn (3 stone + 1 resin + 1 cairn marker). What goes into a Cairn Marker? My instinct: thematic ingredients that make it feel like a "trail-claiming artifact" — maybe 1 Stone + 1 Resin + 1 Pigment (your-color choice), so the cairn's color is established at marker-craft time and the planted-marker is what carries the color into the cairn. But: this is your call, not a Starbright guess. What's the recipe?

---

### Round 4 — Reusability scan against decomp + wiki
*(NOT YET PERFORMED — will execute after Round 3 answers + with the grep-wiki-first discipline)*

Planned scans:
- `Hoe` class in decomp → for Trailblazer's Spade implementation pattern
- `Sign` class in decomp → for Painted Signs interaction extension
- `SE_Rested.CalculateComfortLevel` in decomp → for cairn comfort patch surface
- `Minimap.AddPin` + pin data structures in decomp → for two-tone pins
- `Torch` class in decomp → for Path Lamp Tier 1 reuse + chain-ignition surface
- `Piece.m_resources` shape in decomp → for recipe registration
- Wiki: `Cartography_Table.md` → for disable-mechanism surface
- Wiki: `Raspberries.md`, `Blueberries.md`, `Coal.md`, `Bone_fragments.md`, `Resin.md` → for pigment recipe-input availability per biome
- Wiki: `Banner.md` → for the +1 comfort radius/contribution Cairns are modeled after

---

### Round 5 — Visual assets
*(NOT YET ASKED — will ask in next round)*

---

### Round 6 — Scope boundaries / out-of-scope
*(NOT YET ASKED — will ask after Round 4)*

---

## Explicit features requested (running list)

1. **Explorer's Bench** (Meadows, v1 = kitbash vanilla Workbench with antlers from Deer Trophy integrated into bench art, recipe = **10 Wood + 4 Stone + 1 Deer Trophy**)
2. **Cairns** — 5-tier comfort floor 3/4/5/6/7, build cost **3 Stone + 1 Resin + 1 Cairn Marker**, upgrade cost flat **3 Stone + 1 Resin** per tier, repair cost flat **3 Stone + 1 Resin**, mandatory decay, ≥75% pristine (resin glows) / <75% fizzled / <25% downgrade / 0% collapse, pigment+banner persist, auto-re-ignite glow on repair-to-pristine
3. **Cairn Marker** (pre-crafted consumable, recipe = **2 Leather Scraps + 1 Finewood + 1 Pigment** of player's color, crafted at Explorer's Bench, pigment color binds cairn color at craft-time)
4. **Pigments** — R/W/B/Blue, 2/craft, stack 20, weight 0.1, recipes: R=raspberry, W=bone fragment, B=coal, Blue=blueberry (1:2 each)
5. **Painted Signs** — ONE buildable sign (`piece_sbpr_sign`, 2 Wood), placed via the **Trailblazer's Spade build menu** ('Trail' tab, NOT the Hammer; no station-proximity to place), UNPAINTED. Interacting with a placed sign opens a **custom combined Paint+Text uGUI panel** (replaces the vanilla text dialog): set a **text/board color** AND an optional **border color** (two-tone), pay one pigment per filled slot via `{Paint this and consume}` (border optional; same color in both slots = 2 of that pigment; ≥1 required; re-paint re-consumes), edit the label via `{Update Text}` (free, locked until a color is chosen). Both tones + text persist via ZDO (`SBPR_SignTextColor` + `SBPR_SignBorderColor`). **Free-standing on a kitbashed 2m wood pole** (`wood_pole2`), board at readable height (Daniel 2026-06-05); two-tone via a kitbashed `SBPR_SignBorder` element. Pin path (Shift+E) deferred/unregistered (combined Paint+Text panel, two-tone, Daniel 2026-06-05; supersedes the 6/04 apply-ink model)
6. **Trailblazer's Spade** — single tool item, hoe/hammer-tier, 1.5/3/5m path widths, **Replant Grass in 3 widths (1.5/3/5m)** mirroring the path widths (each restores grass over the stated footprint, still mirrors the Cultivator's "Grass" mode — NOT cultivate, NO terrain raise/level at any width; 3 widths per Daniel's 2026-06-05 playtest, scaling only the grass/paint radius), Clear Vegetation wide-radius (deferred to v0.2.0), recipe **5 Wood + 2 Flint + 2 Leather Hides**, crafted at Explorer's Bench
7. **Path Lamps** — **3 Wood + 2 Resin** (Meadows-tier, Daniel 2026-06-04), placed via the **Trailblazer's Spade build menu** ('Trail' tab, NOT the Hammer; no station-proximity to place), dimmer than torch, longer fuel, manual ignition (no chain ignition). **Scaled 3× vertically** (foot-anchored — base on the ground, flame at the new top; Daniel 2026-06-05)
8. **Map disable in v1** — Cartography Table disabled (no build, no functionality on existing); nomap=ON → no map; nomap=OFF → minimap only (no M-key, no north indicator)

**NOT in v1:** Ember Lamps, Beacons, Seer's Stone, Surveyor's Table, Pocket Portal, Twisted Portal, Iron Compass, Inert Guardian Stones, Yellow pigment (cloudberry).

## Constraints stated

- Standalone-by-default; solo install must be complete good experience
- "Leverage Unity indirectly" — runtime composition of vanilla prefabs/materials/ParticleSystems OK; Unity Editor + assetbundles NOT in v1
- v1 visual approach is "kitbash for playtest" — playtest-quality mechanics ≠ ship-quality art
- No server-gating for Trailborne (philosophy mod, not house-rules mod)
- All v1 pieces are Meadows tier (no biome-progression gate in v1)
- v1 DOES nerf vanilla map (Cartography Table disabled, no M-key map)
- v1 doctrine: corpus-first — grep `~/valheim/sbpr-corpus/wiki/fandom/` BEFORE claiming any vanilla-content fact

## Out of scope (user-confirmed)

- **Guardian Stones (active OR inert)** — entirely separate mod family, NOT Trailborne
- Local Maps + Surveyor's Table — v2 (Black Forest tier; SPECCED — see `docs/design/cartography-v2.md`)
- Real Tents (Bear hide) — v2 (Black Forest tier)
- Cartographer's Kit (a normal recipe; the 40-pigment cost is the gate — NOT a discovery unlock) — v2
- Iron Compass — v3 (Swamps tier, iron is Swamps metal)
- Twisted Portal, Beacons, Ember magic, Scrying Altar, Smokeless Cookfire — v3
- Seer's Stone (crystal-gated, Stone Golem drop) — v4 (Mountains tier, sole headline)
- Plains sailing tier, lighthouse-promote, Star Glass — v5
- Portable map magic — v6 (Mistlands)
- Custom Unity-authored assets — deferred to Locations work (v∞)
- Ship-quality custom art for v1 pieces — v1.1+ polish

## Reusability notes

*Round 4 — decomp + wiki + reference-mod scan, derived from `design/PARKED-2026-06-03.md` as source of truth (not re-imagined). Source-of-truth lines/files captured so the implementer doesn't re-derive.*

### Explorer's Bench (custom `CraftingStation`)

- **Vanilla anchor:** `piece_workbench` prefab. Reuse pattern proven by 3+ reference mods (`RandyKnapp/AdvancedPortals` sets `CraftingStation = "piece_workbench"`; `rolopogo/CraftyCarts` does `Prefab.Cache.GetPrefab<GameObject>("piece_workbench").GetComponent<CraftingStation>()`).
- **Class:** `CraftingStation` (`assembly_valheim.decompiled.cs` lines 56034–56416). Key fields: `m_name`, `m_icon`, `m_discoverRange`, `m_rangeBuild = 10f`, `m_craftRequireRoof = true`, `m_craftRequireFire = false` (vanilla Workbench doesn't need fire), `m_useDistance = 2f`, `m_craftingSkill = Skills.SkillType.Crafting`, `m_areaMarker`, `m_inUseObject`, `m_haveFireObject`.
- **Registration:** clone `piece_workbench` GameObject in `ZNetScene.Awake` postfix, swap `Piece.m_name = "$sbpr_piece_explorers_bench"`, set the SBPR recipe via `Piece.m_resources`, append the clone to the `_HammerPieceTable.m_pieces` list. **Pitfall:** `PieceTable.m_availablePieces` (`assembly_valheim.decompiled.cs` 59893–60202) caches lazily; register pieces in `ZNetScene.Awake` postfix BEFORE the first `Player.Awake` fires.
- **Art swap (Tier 1 — vanilla mesh + custom material):** runtime material swap on the workbench mesh. Antler integration deferred to visual round.

### Trailblazer's Spade (Hoe-class ground-modification tool — NOT a Hammer-class piece spawner)

- **Per PARKED v1 scope:** "1.5/3/5m paths, replant, ClearVegetation". This is a **Hoe variant**, not a build-piece spawner. Three terrain-paint modes (path widths) plus replant and clear actions.
- **🔴 NOT in scope:** **No Cultivate ability.** Cultivate is the vanilla Cultivator's job (turning ground into cultivated soil for crops). The Trailblazer's Spade stays in its own lane — exploration/trail-discipline, not farming. Earlier draft said "Cultivate replant"; that was a misread of PARKED's shorthand. Removed.
- **Vanilla anchors:** `TerrainComp` (`assembly_valheim.decompiled.cs` line 123154) owns ground modifications; `Heightmap` exposes `IsCleared` and the paint-mask enum (`PaintType.Path`, `PaintType.Reset` — line 123801 / 109565). The vanilla `Hoe` prefab + tool item is the closest peer (path-paint surface).
- **Class:** clone the `Hoe` ITEM prefab (a ZNetView-bearing ItemDrop — cloning a plain item is fine), replace its `m_buildPieces` PieceTable with a Trailborne-specific table that exposes three "path" entries (1.5m / 3m / 5m widths) plus **three replant entries** at the same 1.5m / 3m / 5m widths. 🔴 **The terrain ops themselves are built ADDITIVELY, NOT cloned (v0.2.17, card t_6fc9b3fa, ADR-0006):** each is a fresh `new GameObject()` + `AddComponent<Piece>()` + `AddComponent<TerrainOp>()` with `m_settings` set explicitly — mirroring the vanilla **modern** `path_v2`/`replant_v2` ops (read as blueprints via `vprefab inspect` / the tool PieceTable for icon + place-effect + `_GhostOnly` preview only). A `TerrainOp` has NO ZNetView and bakes paint into the per-zone `TerrainComp` then self-destructs, so two ops coexist on a tile (last-applied-wins) — fixing the precedence fight the legacy `TerrainModifier` clone caused. The ops are deliberately **not registered in ZNetScene** (vanilla path_v2/replant_v2 aren't either — they live only as PieceTable refs); they are held under the inactive PrefabHolder and added to the spade table by reference.
- **Replant mechanic (3 widths — Daniel playtest 2026-06-05; mechanism rebuilt v0.2.17, card t_6fc9b3fa):** build `piece_sbpr_replant_narrow/standard/wide` at **1.5m / 3m / 5m** as additive modern `TerrainOp` ops mirroring the vanilla Cultivator's **`replant_v2`** (Reset paint, paintR ≈ 2.2, `m_level`/`m_smooth`/`m_raise` all false). Set **ONLY `m_settings.m_paintRadius`** per width; never write level/smooth/raise (they stay at the `Settings` default `false`), so every width is a pure grass-restore brush — regrows grass on dirt with **no terrain raise/level/cultivate at any width** (PR #16 guard, by construction). `Piece.m_vegetationGroundOnly = true` (matching `replant_v2`); the natural vegetation mask a Dirt path leaves unchanged still permits grass-on-path placement (`Heightmap.PaintCleared` preserves the mask alpha for non-ClearVegetation paints). 🔴 **History (why this changed — read before touching):** the v0.1.0–v0.2.15 builds CLONED the **legacy `replant`** (`TerrainModifier`) donor, a persistent ZNetView-bearing networked piece. Stacked on a path op (also a legacy clone), the two fought via `TerrainModifier.RemoveOthers()` (which only evicts same-paint ops) — the "grass fights path" bug Daniel reported. Two prior re-attempts (#77, #83) tried to clone `replant_v2` instead and failed: it is not in ZNetScene at all (grass tool vanished), and cloning a registered TerrainOp orphaned existing ZDOs (client hang). The clone approach was the root cause both times (ADR-0006); additive construction dissolves it. ⚠️ The even-earlier M3 build cloned the **`cultivate`** soil-tiller at a forced 5m radius (the PR #16 "UBER level" bug); `cultivate` stays out of the spade's lane entirely.
- **Path mechanic (3 widths):** same additive `TerrainOp` construction mirroring vanilla **`path_v2`** (Dirt paint, paintR ≈ 2.0, level/smooth/raise all false). `path_v2` is **paint-only** — it does NOT level or smooth terrain — so our path op is paint-only too (a behavior change from the pre-0.2.17 legacy clone, which widened level/smooth radii to flatten the path tile). This matches AT-OP-4 ("flat ground stays flat at any width") and the trail-not-earthworks intent; flagged for Daniel's in-game confirmation. `Piece.m_vegetationGroundOnly = false` (matching `path_v2`).
- **Existing-world migration (AT-OP-3):** worlds saved by the pre-0.2.17 build hold persistent `piece_sbpr_path_*`/`piece_sbpr_replant_*` ZDOs written by the old donors. Because the new ops are not registered in ZNetScene, vanilla `ZNetScene.CreateObject` drops those orphans on first server load (no "not used when creating" warning possible). A server-only one-time sweep (`LegacyTerrainOpZdoCleanup`, postfix on the server-only `ZNet.LoadWorld`) ALSO destroys them eagerly via the same `SetOwner` + `ZDOMan.DestroyZDO` path vanilla uses, broadcasting the destroy RPC to clients, and logs an exact count.
- **ClearVegetation (deferred to v0.2.0):** would use a clear/`Reset` paint pass on a radius to remove bushes/grass/small rocks. NOT shipped in v0.1.0 — the spade ships only Path (×3) + Replant.

### Painted Signs (single buildable + combined Paint+Text panel, two-tone)

- **Model (LOCKED 2026-06-04, Daniel):** ONE buildable sign, placed UNPAINTED, painted afterward by applying a pigment/ink item. This SUPERSEDES the earlier "subclass `Sign` + custom multi-field edit dialog + E text-color / Shift+E accent-color / two-tone pin" design. No custom edit dialog, no accent color, no two-tone pin for v0.1.0.
  - **Build:** `piece_sbpr_sign` ("Painted Sign"), **Trailblazer's Spade build menu** ('Trail' tab — NOT the Hammer; design pillar: Explorer-placed pieces live on the Tools), **2 Wood**, **no station-proximity required to place** (`Piece.m_craftingStation` cleared). Clone of the vanilla wood `sign` prefab **kitbashed onto a vanilla 2m wood pole (`wood_pole2`)** so it stands free on the ground like a trail signpost (Daniel 2026-06-05), board raised so its TOP sits just under the measured pole crown (board centre ~1.65m, post foot flush at ground; board-top anchored to the measured crown at register time — pivot-robust, no magic height). The pole is a decorative child stripped of ZNetView/Piece/WearNTear and its OWN collider (no own ZDO, not separately destructible) — but the sign carries a separate thin **post-foot ground-contact collider** (below) so it seats flush; on the placed sign that foot collider is disabled, so the BOARD stays the sole interact/paint target (never intercepts the E raycast). Ships in plain wood (unpainted); ink is NOT a build ingredient.
    - **Board lateral standoff (v0.2.9):** the board mounts against the post's SIDE face — offset laterally along the board's facing normal by (½ post thickness + ½ board thickness, both measured from transformed bounds) so the board's back face just kisses the post side (no interpenetration, no gap); it is NOT embedded in the post centerline. The lateral axis and sign are derived from the donor board's facing normal at runtime, never hardcoded. Exact kiss tolerance is visual-polish (v0.2+).
    - **Board facing flip (v0.2.10 — fixes "board faces the wrong way", t_153ca109):** the standoff above only *translates* the board onto the post side and *trusts* the donor `faceT.forward` to equal the readable outward normal. In-game the donor's readable normal is the OPPOSITE of `faceT.forward`, so the text read straight INTO the post (a player at the natural front saw the back). Corrected by a 180° rotation of the board GROUP about its OWN vertical centroid (mirror across the board centre on both horizontal axes + `AngleAxis(180°, up)` on each child) — pivoting on the board's own centroid (NOT the post axis) flips the readable face to point AWAY from the post while keeping the board on the side the standoff already chose. A Y-axis rotation never touches Y (crown height preserved), and the board's normal-axis extent is symmetric about its centroid (the post-kiss face lands in the same place — standoff preserved). Placement yaw spins board+post rigidly, so the flip holds at every yaw. (NB: rotating about the *post* axis — the originally-proposed mechanism — would shuttle the board to the far side where the corrected normal still points at the post; the centroid pivot is the one that reads cleanly.)
    - **Post-foot ground-contact collider (v0.2.10 — fixes "post ~3/4 buried", t_4ad60d6f / parent t_1dc88742):** the sign carries a thin, **non-trigger** `BoxCollider` whose BOTTOM plane sits at the **measured planted-post foot** (root-local y≈0; derived through the planted pole's transform, no magic height). Valheim seats a placed piece by driving the AABB of its lowest enabled non-trigger collider to the ground; once `StripToDecorative` removed the pole's own collider, the board's interact collider — lifted ~1.5m to the crown — became the lowest one, so the seat buried the 2m post ~3/4. The foot collider returns the lowest-collider plane to the post foot, so the post seats flush. **Two-phase (load-bearing):** in the placement GHOST the collider is enabled and on the `piece` layer (inside the build placement ray-mask) so the seat counts it; on the **placed** instance `SignTag` (gated on a live ZDO, i.e. not the ghost) **disables** it so it can never steal the Sign's E-to-write / paint raycast — the BOARD remains the sole interact/paint target (regression guard). The post carries no WearNTear, so it is never separately destructible. Footprint = measured post thickness; thickness is a tiny shape constant. Clean-room: public UnityEngine API only.
  - **Paint:** with an ink in hand, apply it to the placed sign → the sign takes that color. Apply a different ink → repaint. One ink consumed per paint. An already-applied color is a no-op (no ink consumed).
  - **Text:** vanilla `E` text dialog, unchanged. Default label "Painted Sign".
- **Color state:** stored per-instance on the sign's ZDO as a string field `SBPR_SignColor` (one of `red`/`white`/`blue`/`black`, or `""` = unpainted). Owner-write via `ZNetView.ClaimOwnership()` + `ZDO.Set(string,string)` (mirrors the `CairnTag` tier pattern). Persists across reloads + syncs to clients; re-applied to the mesh on spawn via a `SignTag` component (`Renderer.sharedMaterials` `_Color` tint).
- **Paint seam (clean-room):** Harmony prefix on `Sign.UseItem(Humanoid, ItemDrop.ItemData)` — the public `Interactable.UseItem` contract for "apply a held item to this placed object," the same surface `ItemStand` uses. When the used item is one of our four inks AND the target carries a `SignTag`, we paint + consume one ink + return `true` (skip vanilla). Any other item / non-SBPR sign falls through to vanilla. Method/field signatures (`Sign : Hoverable, Interactable, TextReceiver`; `bool UseItem(Humanoid, ItemDrop.ItemData)`; `ItemData.m_dropPrefab`; `Inventory.RemoveItem(ItemData,int)`; `ZNetView.IsOwner/ClaimOwnership/GetZDO`; `ZDO.GetString/Set`) were confirmed against `assembly_valheim.dll` **public metadata** — not decompiled IronGate source.
- **Pin behavior (v1 deferred → WIRED in v2 Marker Signs, card t_0c7b782d):** the v1 Painted Sign's Shift+E pin gesture stayed deferred (the plain Painted Sign has no pin). The v2 **Marker Signs** feature (`docs/design/marker-signs-worldpin.md` / `docs/v2/planning/marker-signs-impl-spec.md`) introduces the WorldPin substrate and wires Shift+E on the four marker pieces: `alt==true` on a `MarkerSignTag` toggles the `SBPR_Pinned` ZDO bool and projects/removes a `save:false` WorldPin with the marker's custom icon (derive-by-scan reconcile, server-/host-authoritative ZDO scan). `SignInteractPatch` now recognises `MarkerSignTag` (alt==true → pin toggle; alt==false → the dedicated MarkerSignPanel) in addition to the Painted Sign's `SignTag` (alt==false → paint panel; alt==true → falls through to vanilla — a plain Painted Sign has no pin gesture). The plain Painted Sign's own single-color pin remains out of scope (Marker Signs are the SBPR pin vehicle).
- **UGC gate (decision LOCKED 2026-06-03):** v1 Painted Signs inherit vanilla `Sign`'s UGC gate as-is. Defer the bypass conversation to v2.

### Cairns (custom piece + comfort-level state machine via `SE_Rested.CalculateComfortLevel` patch)

- **Per PARKED v1 scope:** "3/4/5/6/7 comfort floor, max() clamp, patch `SE_Rested.CalculateComfortLevel` directly (not in vanilla `ComfortGroup` enum), repair flat 3 stone + 1 resin, pigment+banner persist, downgrade@25%, collapse@0%."
- **Vanilla anchors:** `SE_Rested` class at `assembly_valheim.decompiled.cs` line 25338. `SE_Rested.CalculateComfortLevel(Player)` at line 25397, overload `CalculateComfortLevel(bool inShelter, Vector3 position)` at line 25402. Vanilla `ComfortGroup` enum at line 116068; `Piece.m_comfortGroup` at line 116123 — confirmed that adding to the enum requires touching the assembly; the PARKED decision to patch `CalculateComfortLevel` directly bypasses this.
- **Why the `CalculateComfortLevel` patch (PARKED rationale):** vanilla comfort is computed by iterating nearby pieces grouped by `ComfortGroup` and picking the highest-comfort piece in each group. Cairns aren't in any vanilla group. Adding a new enum value would require IL-modifying the enum (fragile across game updates). Instead, postfix `SE_Rested.CalculateComfortLevel(bool, Vector3)`: scan nearby SBPR cairns within vanilla's comfort search radius, find the highest-tier (3-7), `result = Mathf.Max(vanillaResult, cairnTier)`. Clean and update-tolerant.
- **Lifecycle state machine:** Cairn has 5 tiers, comfort floor 3/4/5/6/7. Health tracked via vanilla `WearNTear.m_health` (line 128064) and `m_onDamaged` delegate (line 128029). Postfix `WearNTear.OnDamage` to check our thresholds: at `m_healthPercentage < 75%` lose pristine (visual indicator), at `< 25%` downgrade by one tier (reduces comfort floor by 1, resets health to 100% of new tier), at `0%` collapse (destroy piece, leave a pile-of-rocks remnant).
- **Repair:** flat 3 Stone + 1 Resin per tier-upgrade or per pristine-restore. Painted color + banner attachment persist through downgrade/upgrade — stored on cairn's ZDO, re-applied on tier swap.
- **Initial build:** 3 Stone + 1 Resin + 1 Cairn Marker. Marker carries the pigment color choice into the cairn ZDO at place-time.
- **Open thread (PARKED):** "Cairn downgrade re-ignite resin? lean: deliberate-only." Stays open.

### Pigments (custom `ItemDrop`, consumable craft input)

- **Per PARKED v1 scope:** "R/W/B/Blue, 2/craft, stack 20, weight 0.1". Four pigments in v1: red, white, black, blue. Each recipe yields 2 pigments per craft, max stack 20, item weight 0.1.
- **Vanilla anchor:** no vanilla pigment/dye/ink/paint item exists (full wiki grep returns only `Trinkets.md`). Pigments are novel; naming space is clean.
- **Pattern:** clone any simple consumable prefab (e.g. `Raspberry`) as a sprite-only stand-in, swap `m_shared.m_name`, `m_shared.m_icons`, `m_shared.m_maxStackSize = 20`, `m_shared.m_weight = 0.1f`. One `Recipe` per color, crafted at Explorer's Bench, yields 2 per craft.
- **v1 ingredient inputs:** red ← raspberries; blue ← blueberries; white ← (TBD — bone fragment? mushroom?); black ← (TBD — coal? greydwarf eye?). These are reasonable instincts only — needs Daniel confirmation in Round 5 alongside icons.

### Cairn Marker (custom `ItemDrop`, single-use consumable)

- **Recipe (LOCKED):** 2 Leather Scraps + 1 Finewood + 1 Pigment. Pigment color selected at craft-time → cairn color at build-time.
- **Pattern:** simple `ItemDrop` clone (any small consumable), recipe at Explorer's Bench. Consumed by the Cairn piece's `Piece.m_resources` requirement on initial build only (not on tier upgrades).

### Path Lamps (kitbash vanilla `piece_groundtorch_wood`)

- **Recipe (LOCKED, Daniel 2026-06-04 — see §A3.7):** **3 Wood + 2 Resin** (downshifted from the earlier 3 Corewood; Meadows-tier accessibility).
- **Build menu (LOCKED, Daniel 2026-06-05):** placed via the **Trailblazer's Spade build menu** ('Trail' tab — NOT the Hammer; design pillar: Explorer-placed pieces live on the Tools), **no station-proximity required to place** (`Piece.m_craftingStation` cleared).
- **Vanilla anchor:** `piece_groundtorch_wood` (Fireplace + Piece combo). Tune `Fireplace.m_fuelItem = Resin`, extend `m_secPerFuel` for "long burn" (vanilla torch ~600s/resin; ours ~1800s/resin so a 2-resin lamp = ~1hr burn), reduce child `Light.intensity` ~30% for "dimmer trail glow."
- **Visual (kitbash, Daniel 2026-06-05):** **scaled 3× vertically** so it reads as a tall standing path lamp. Foot-anchored: the base stays flush with the ground and the flame/light rides up to the new top (geometry children scale on Y; the flame/Light children keep their size and only translate up — not a bonfire-on-a-stick). Root collider intentionally NOT rescaled (flag QA if the collision box should match the taller visual).

### v1 Cartography Table (DISABLED)

- **Approach:** prefix `MapTable.OnRead` and `MapTable.OnWrite` (`assembly_valheim.decompiled.cs` 114014–114141) to return false. Show MessageHud text: "$sbpr_cartography_disabled_v1 — coming in v2."
- **Reference:** `shudnal/NomapPrinter` already patches the exact same `MapTable.OnRead`/`OnWrite` surface (`HarmonyPatch(typeof(MapTable), nameof(MapTable.OnRead))`). Clean precedent — different intent, same surface.

### Map situation (PARKED-locked, NOT a piece — global behavior)

- **Per PARKED:** "nomap ON = no map at all. nomap OFF = minimap ONLY, freely rotating, NO north indicator. No M-key map."
- **When nomap mode is OFF:** patch `Minimap.SetMapMode` to suppress `MapMode.Large` (full M-key map blocked); patch the minimap rotation/north-arrow logic in `Minimap.Awake`/`Minimap.UpdateMap` to disable the compass needle.
- **When nomap mode is ON:** patch `Minimap.IsOpen` to return false (compose with NomapPrinter's existing patch on this method — read NomapPrinter for the precise pattern, do not copy).
- **Pin behavior coupling:** when nomap is ON, `Minimap.AddPin` calls from Painted Signs early-return (PARKED: "no-op if nomap ON"). When nomap is OFF, pins land on the minimap as normal.

### Vanilla content corpus-verified this round

- ✅ `piece_workbench` exists; clone pattern proven by 3+ reference mods.
- ✅ `Sign` class signature current as of Bog Witch/Ashlands decomp; UGC gate at `Sign.Interact` confirmed.
- ✅ `WearNTear.m_health` / `m_onDamaged` / `OnDamage` / `Repair` all current.
- ✅ `SE_Rested.CalculateComfortLevel` exists at lines 25397/25402; both overloads present. `ComfortGroup` enum at 116068. PARKED rationale for patching `CalculateComfortLevel` directly (rather than extending the enum) is sound — confirmed cairns aren't in the vanilla enum.
- ✅ `TerrainComp` + `Heightmap.IsCultivated`/`IsCleared` exist (lines 123154, 109613). `PaintType.Cultivate`, `Path`, `Reset` enum values present (line 123801). Trailblazer's Spade wraps these.
- ✅ `Pickable.m_picked` + `m_itemPrefab` + `m_amount` + `m_respawnTimeMinutes` exist — usable for replant and v4 Seer's Stone area-pop.
- ✅ `Resin` is real, drops from all tree types + Greylings.
- ✅ Vanilla `Cairns` (`Waymarker01`/`Waymarker02`) are inert Mountain-biome POI, NOT buildable. Our buildable + tiered + comfort-emitting Cairn is original.
- ✅ `TextInput.RequestText` is the vanilla single-line input dialog (line 27163 — used by rename). For our multi-field sign edit dialog, we'll clone `InventoryGui` panel templates at runtime (no vanilla multi-field text-input dialog exists).
- ❌ No vanilla pigment/dye/ink/paint item. Pigments are fully novel.
- ❌ No vanilla "tiered building piece with comfort progression" — Cairn lifecycle is original work.

### What this round did NOT cover (deferred to later rounds or already locked elsewhere)

- **Pact** — out of scope for this mod entirely. Trailborne v1 ships standalone with no shared-library dependency.
- **Shared-infrastructure code organization** — also out of scope; each mod stands alone for now.
- **LOC estimates** — withdrawn. Not useful before tasks decomposition; was anchored to imaginary shared infrastructure.
- **Inert Guardian Stones** — PARKED as stretch goal contingent on `valheim-regions` macro boundaries finalizing first. NOT a v1 blocker.

## Visual assets

**v0.1 decision (LOCKED 2026-06-03):** ship with **placeholder art**. Focus on getting gameplay elements working first; visual polish iterates from a working playtest, not before. Asset doctrine (fact #112 — zero Unity assetbundles, Tier 0/1/2 reuse only) still applies — placeholders are runtime-loaded PNGs and vanilla material swaps, not custom Unity geometry.

### Placeholder art lanes for v0.1

- **Item icons** (Trailblazer's Spade, Cairn Marker, 4 Pigments, Path Lamp): runtime-loaded PNGs via `File.ReadAllBytes` → `Texture2D.LoadImage` → `Sprite.Create`. Generated as needed via FLUX local lane; quick + good-enough is the bar. Iterate freely in v0.1 → v0.2.
  - 🔴 **Equipable item icons MUST have a TRANSPARENT background** (bug t_b9a111ca, Daniel playtest 2026-06-19). The inventory slot draws the vanilla blue "equipped" highlight (`InventoryGrid` element child `equiped`, toggled by `m_equiped.SetActive(itemData.m_equipped)` in `assembly_valheim`) *behind* the icon Image — an opaque icon background occludes it, so every equipable (Utility / Trinket / Tool / hand / two-handed) silently loses its equipped indicator. Material-type icons (pigments, Cairn Marker, Portal Seed, raw Sunstone) never show the indicator, so opacity is harmless there. The procedural icon generators (`scripts/gen_*_icon*.py`) build on a transparent `Image.new("RGBA", …, (0,0,0,0))` canvas (NOT an opaque `warm_backdrop()`/`frame()`); icons composited from an uncommitted FLUX render are knocked out by `scripts/knockout_equipable_icon_bg.py`. The guard is `tests/EquipableIconTransparencyTests.cs` (CI-gating, engine-free) — it fails red if any equipable icon ships opaque.
- **Build icons** (Explorer's Bench, Painted Sign, Cairn × 5 tiers, Path Lamp build form): same runtime-PNG approach.
- **In-world meshes:**
  - Explorer's Bench → vanilla Workbench mesh + color-tinted material (so it visually reads as "not-quite-Workbench" in the world; the dialog hover-name + icon disambiguate it).
  - Cairn (5 tiers) → procedurally-stacked vanilla Stone prefabs at runtime, scaled per tier. Pigment overlay = material tint on the stack. Banner attachment = vanilla Banner prefab parented to the cairn root at place-time.
  - Painted Sign → vanilla Sign mesh; built unpainted (plain wood material), runtime per-instance tint once painted (reads `SBPR_SignTextColor` + `SBPR_SignBorderColor` from ZDO, applies the two tones — board + border — at spawn). ⚠️ Needs a separable border renderer/material on the mesh (open technical question).
  - Path Lamp → vanilla `piece_groundtorch_wood` mesh, no swap. Lower light intensity is the visual differentiation.
  - Trailblazer's Spade → vanilla Hoe item mesh; icon does the work of disambiguation in inventory.
- **Custom UI panel** — **REQUIRED for v0.1.0** (re-locked Daniel 2026-06-05). The Painted Sign uses a **custom combined Paint+Text uGUI panel** (text color + border color swatches, crafting-style pigment cost, `{Paint this and consume}` + `{Update Text}` buttons) that replaces the vanilla single-line text dialog. This **reverses** the 6/04 "no UI, apply-ink" note. Panel is built clean-room (no copied vanilla UI prefab); no new *mesh* prefabs required, but the sign mesh needs a separable border renderer for the two-tone tint.

### Asset generation as needed

Starbright generates placeholders on demand during implementation. FLUX local lane is the default (fast, free, run from RequiemSoul → Prime-W). Style target for placeholders: legibly Valheim-shaped, low-fi-okay, recognizable silhouette. Polish quality is explicitly NOT the bar — "you can tell what it is" is the bar.

### Deferred to v0.2+ (after gameplay works)

- Iconography polish pass (consistent line weight, palette, silhouette discipline across the set)
- Antler integration for Explorer's Bench mesh (per Q3.10 — deer trophy antlers visually integrated into the bench, not mounted on top)
- Custom mesh authoring (if/when v2 brings the Surveyor's Table, Iron Compass, Tents — those have genuine geometry needs)
- Visual differentiation of the 5 Cairn tiers beyond "scale + color" (e.g. moss progression, lichen, accumulated character)
- Pigment vegetation-stain visual on Cairns (color seeps slightly into the rock surface vs flat tint)

### Open for placeholder generation when implementation gets there

- 4 Pigment ingredient confirms: red ← raspberries ✓, blue ← blueberries ✓, white ← bone fragment OR mushroom (TBD), black ← coal OR greydwarf eye (TBD). Pick at icon-time alongside the visual.
- Painted Sign color palette: Round-1 captured "color is emergent player decision" (pillar 2). v0.1 ships with a fixed palette of 4 colors derived from the 4 pigments; the player chooses a sign's **text color and border color** (two-tone) via the combined Paint+Text panel after placement (two-tone re-locked 2026-06-05, reversing the 6/04 single-color drop). No per-color sign icons needed (one buildable, vanilla wood icon); the swatches live in the panel.

## Open questions / TBD

- **Q3.6: Cairn per-tier build cost** ✅ LOCKED — 3 Stone + 1 Resin + 1 Cairn Marker (initial); 3 Stone + 1 Resin (per upgrade)
- **Q3.7: Path Lamp wood material** ✅ LOCKED — corewood
- **Q3.8: Ember Lamps in v1** ✅ DROPPED FROM v1
- **Q3.9: Cairn Marker recipe** ✅ LOCKED — 2 Leather Scraps + 1 Finewood + 1 Pigment (player color choice)
- **Q3.10: Explorer's Bench exact quantities** ✅ LOCKED — 10 Wood + 4 Stone + 1 Deer Trophy. No raspberries. No resin. (Earlier I had inferred raspberries+resin from PLAYER_GUIDE.md narrative — Daniel corrected: the narrative's mention of those ingredients was describing what the bench is USED FOR, not what it's MADE OF.)
- **Q3.11: Path Lamp exact quantities** ✅ LOCKED — 3 Corewood + 2 Resin (3m light pole)
- Round 4 decomp/wiki scans pending (will leverage `design/nomap.md`'s existing line-references first)
- Round 5 visual assets pending
- Round 6 out-of-scope confirmation pending

## PLAYER_GUIDE.md / design/*.md doc-PR follow-up tracker

After spec finalization, the following doc updates are needed to keep repo consistent with this requirements.md (the authoritative v1 spec):

### ✅ Done this session
- **Rename Orienteering Table → Explorer's Bench** — propagated to `README.md` (module list line 28), `PLAYER_GUIDE.md` (lines 56-62, 87, 121, 229-230), and `design/nomap.md` (§1 heading, prefab name `SBPR_ExplorersBench`, localization key `$sbpr_piece_explorers_bench`, plus references in open-questions §2 and §5 and risk-ranking §5).
- **design/nomap.md §1 recipe** — corrected to `10 Wood + 4 Stone + 1 Deer Trophy` (was `20W + 4Stone + 4Bone fragment + 2Greydwarf eye + 2Deer hide`). Explanatory note added inline so future readers see why the change was made.
- **PLAYER_GUIDE.md bench-recipe prose** — line 58-62 rewritten. Now explicitly states `10 Wood + 4 Stone + 1 Deer Trophy` and clarifies that antlers are part of the bench art (not mounted-on-top). The misread-inducing phrase "raspberries (for red pigment), and resin (for ink fixative and lamp oil)" has been removed from the recipe paragraph (raspberries/resin are still mentioned later in §Meadows as pigment inputs, which is correct — they're what the bench is *used to process*, not ingredients in the bench itself).

### ⏳ Remaining doc-PR work
1. **Trailblazer's Spade recipe** — `PLAYER_GUIDE.md` line 67 says "wood, tin, flint". Today-locked: 5 Wood + 2 Flint + 2 Leather Hides. No tin.
2. **v1 Cartography Table behavior** — `PLAYER_GUIDE.md` §"Cartography Table (vanilla) — but rebalanced" describes the v2 cartography shape (now the Surveyor's Table). v1 is DISABLED, not "rebalanced." The v2 design is specced in `docs/design/cartography-v2.md` + `docs/v2/planning/`; the PLAYER_GUIDE section is annotated inline as v2-future.
3. **Painted Sign interaction model** — line 253 says "default keybind _TBD_" for the pin trigger. **Re-locked 2026-06-05 (Daniel, from UI mockup):** ONE buildable sign, placed UNPAINTED (2 Wood). Interacting with a placed sign opens a **custom combined Paint+Text panel** — set a **text color AND a border color** (two-tone), pay one pigment per filled slot via `{Paint this and consume}` (border optional, re-paint re-consumes), and edit the label via `{Update Text}` (free, locked until a color is chosen). This **supersedes** the 6/04 apply-ink/single-color/no-UI lock. Pin trigger (text color, no-op if nomap=ON) deferred + currently unregistered. PLAYER_GUIDE needs the build-unpainted-then-open-panel loop surfaced (and the "color baked at craft time" line corrected).
4. **Cairn lifecycle prose** — PLAYER_GUIDE references "the way Cairns are maintained" in Guardian Stones forward-pointer (lines 351-353). Cairn lifecycle now fully specified (3 Stone + 1 Resin + 1 Cairn Marker initial, flat 3+1 upgrade/repair, 5-tier comfort floor, 75% pristine threshold, 25% downgrade, 0% collapse). PLAYER_GUIDE should get a brief Cairn lifecycle section in §Meadows.
5. **Cairn Marker (new item)** — not yet in PLAYER_GUIDE. Add to crafted-at-Explorer's-Bench item list with recipe: 2 Leather Scraps + 1 Finewood + 1 Pigment.
6. **Remove Ember Lamps / Beacons from v1 scope language** — PLAYER_GUIDE includes them in the Black Forest section. They're not in v1. Either move them to a "Roadmap" section or clearly label them v1.1+.

## Vision context

Aligned with holographic facts:
- `#111` Trailborne naming lock
- `#112` Trailborne asset doctrine (Round 1 refined as "leverage Unity indirectly")
- `#93` Niflheim parked design
- `#94` Corpus-first rule (must grep wiki before claiming vanilla-content facts)
- `#110` Kanban Swarm execution handoff after spec lock

And primary-source design lock: `design/PARKED-2026-06-03.md` in this repo.

Bigger picture: Trailborne v1 is SBPR's first public Thunderstore release.
Its reception sets the brand for everything downstream (Guardian Stones as
separate mod family, Niflheim modpack, the eventual `niflheim.wiki`).
Standalone-install experience is non-negotiably good.
