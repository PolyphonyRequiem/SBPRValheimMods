---
title: "Cairn banner waggles in place, never streams downwind — the missing mechanism is GlobalWind.m_alignToWindDirection (transform rotation), not Cloth tuning"
status: current
last_updated: 2026-06-10
investigator: Starbright (engineering), clean-side — read Valheim's OWN assembly + prefab payload (permitted, ADR-0001)
spec_anchor: "docs/v0.1.0/planning/requirements.md §A2.1b (cairn banner color identity + wind response)"
supersedes_attempts: "PR #56 (color), #61 (scale), #69 (Cloth A-prime), #71 (windsock redesign), #78 (stiffness '3rd-attempt')"
---

# Cairn banner: why it waggles in place and never streams downwind

## Trigger (Daniel, 2026-06-10 in-game playtest, v0.2.15, verbatim)
> "issue 3: banners are still just floating, hanging in the air, and waggling. They respond to wind magnitude by increasing waggle amplitude, but that's it. please research this scenario in more depth before attempting to fix it yet again."

This is the **FIFTH** time the banner has been worked (color → scale → Cloth → windsock redesign → stiffness). Every prior attempt tuned the **`UnityEngine.Cloth` solver** (pin geometry, maxDistance ramp, stiffness, damping, RandomFactor). **None of them added the mechanism that actually produces directional streaming.** Daniel explicitly asked for depth-first research before another fix — this doc is that research.

## TL;DR — the root cause (grounded in the decomp, NOT a guess)

**Vanilla cloth streams downwind because `GlobalWind` ROTATES THE WHOLE CLOTH TRANSFORM to face the wind, THEN lets the Cloth solver add ripple on top. Our `ClothWindDriver` copied only the Cloth-force branch and omitted the transform rotation entirely. So our banner gets force-jittered (waggle, amplitude ∝ intensity) but never ORIENTS — exactly Daniel's symptom.**

The directional behavior is a **transform rotation**, not a Cloth-physics effect. Four attempts tuning the Cloth solver could never have produced it, because the solver was never the thing that orients vanilla cloth to the wind.

## The evidence (ground-truthed against THIS build)

### 1. Vanilla `GlobalWind.UpdateWind()` — `assembly_valheim.decompiled.cs:30348-30392`
```csharp
private void UpdateWind()
{
    if (m_alignToWindDirection)                                   // ← THE MISSING MECHANISM
    {
        Vector3 windDir = EnvMan.instance.GetWindDir();
        base.transform.rotation = Quaternion.LookRotation(windDir, Vector3.up);   // rotate the cloth to face wind
    }
    // … particle branch …
    if ((bool)m_cloth)
    {
        Vector3 vector = EnvMan.instance.GetWindForce();
        m_cloth.externalAcceleration = vector * m_multiplier;                      // ← the ONLY branch we copied
        m_cloth.randomAcceleration   = vector * m_multiplier * m_clothRandomAccelerationFactor;
    }
}
```
The `m_alignToWindDirection` block runs FIRST and sets `transform.rotation` so the sheet physically points downwind. The Cloth force terms then add billow/ripple ON TOP of an already-aligned sheet. **Directional streaming = the rotation. Ripple/flutter = the Cloth forces.** Vanilla sails set `m_alignToWindDirection = true`; our banner has no equivalent.

### 2. Our `ClothWindDriver.UpdateWind()` — `src/SBPR.Trailborne/Runtime/ClothWindDriver.cs:70-83`
```csharp
private void UpdateWind()
{
    if (_cloth == null || EnvMan.instance == null) return;
    Vector3 wind = EnvMan.instance.GetWindForce();
    if (CheckPlayerShelter && _player != null && _player.InShelter()) wind = Vector3.zero;
    _cloth.externalAcceleration = wind * Multiplier;                  // copied vanilla's cloth branch …
    _cloth.randomAcceleration   = wind * Multiplier * RandomFactor;   // … and ONLY that branch.
}
```
**Verified absent (grep, this build):** no `GetWindDir`, no `LookRotation`, no `transform.rotation`, no `alignToWind` anywhere in `ClothWindDriver.cs` or `CairnTag.cs` (the only `Quaternion` in CairnTag is the stone placement at :287, unrelated). We read `GetWindForce` (a magnitude-bearing vector we feed to the solver) but never use `GetWindDir` to ORIENT the transform.

### 3. Vanilla sail confirms the pattern — `vprefab inspect sail_full`
```
● sail_full
    scripts:   GlobalWind          ← the same component, with m_alignToWindDirection authored on
    material:  ashlandshipsail_mat
    other:     Cloth
```
The ship sail (the canonical "streams to the wind" cloth) is `GlobalWind + Cloth` — the same pair we built. The difference is purely the authored field `m_alignToWindDirection = true`, which our additive build never set because we wrote our own driver and only ported the cloth branch.

### 4. Why every prior attempt failed (they all tuned the wrong subsystem)
- **#69 (Cloth A-prime):** added Cloth + force driver. Waggled — no alignment.
- **#71 (windsock redesign):** elevated mount, free-fall tail, steep maxDistance ramp, lower RandomFactor. Still waggled — pin geometry doesn't orient a sheet.
- **#78 ("3rd-attempt root cause: stiffness"):** lowered stretching/bending stiffness so force could deform the sheet. Made the waggle looser; still no directional streaming — a softer sheet still isn't aimed downwind.
- The #78 card's own diagnosis ("stiffness defaults too rigid") is **real but secondary**: it explains why the ripple is stiff, not why there's no streaming. The streaming was never going to appear from the Cloth solver alone.

## The actual symptom, decomposed (matches Daniel exactly)
- **"waggling / responds to wind magnitude by increasing waggle amplitude"** → the `externalAcceleration`/`randomAcceleration` force terms ARE working (force ∝ `GetWindForce()` magnitude ∝ intensity). That's the ripple-on-top, doing its job.
- **"floating, hanging in the air"** → the mount/pin geometry (a separate concern; see Open Questions).
- **"but that's it" / never streams downwind** → the MISSING `m_alignToWindDirection` rotation. No code orients the banner to `GetWindDir()`, so it can only jitter in place.

## Proposed direction (DESIGN — for the architect; do NOT hand-patch)
The fix is to add the alignment mechanism vanilla uses, not to tune the Cloth further. Candidate shapes (architect chooses + specs):

1. **Add an `AlignToWindDirection` option to `ClothWindDriver`** (it's already the reusable wind component): when set, each `UpdateWind` does `transform.rotation = Quaternion.LookRotation(EnvMan.instance.GetWindDir(), Vector3.up)` on the banner's pivot, BEFORE writing the cloth forces — a faithful port of vanilla's branch. The banner's mount must then be built so that rotating the pivot swings the free tail downwind (pivot at the mount/tether point, tail extending along the aligned axis). This is the minimal, vanilla-faithful change.
   - **Caution:** vanilla applies this on a sail whose pivot is the mast. Our banner pivot is the cloth TOP (`CairnTag` bakes pivot at cloth top). Rotating about the wrong pivot will spin the sheet oddly. The architect must reconcile the alignment pivot with the windsock mount geometry — likely a small mount transform that rotates, with the tail hanging/streaming off it.
2. **Reconsider whether Cloth is even the right tool for a small trail-marker ribbon.** Vanilla uses Cloth for big authored sails/capes. For a small downwind streamer, a rotated mesh whose wave is shader-driven (the cloth child carries zero scripts → the material waves for free) + the alignment rotation may give a cleaner "windsock" read than a hand-tuned Cloth solver, with far less fragility. Per ADR-0006 this is also more additive-clean. The architect should weigh "port vanilla's GlobalWind faithfully (Cloth + align)" vs "simpler aligned-mesh streamer."
3. **Whatever the choice, the AT must test DIRECTION, not just amplitude** — the prior attempts' ATs leaned on "does it move more in a storm" (which always passed because the force branch works) and under-tested "does it point downwind when the wind angle changes" (which always failed). Lead with the direction test.

## Acceptance tests (named — architect refines; LEAD with direction)
- **AT-BANNER-DIR-1 (the whole ballgame):** `wind 1 0` vs `wind 1 180` (forced) — the banner visibly SWINGS to point downwind; the free end trails AWAY from the wind source, reorienting when the angle changes. Not flutter-in-place.
- **AT-BANNER-DIR-2 (tether read):** one end clearly fixed (the mount), the other clearly streaming downwind — reads as a windsock/flag, not a vibrating sheet.
- **AT-BANNER-FORCE-3 (keep what works):** calm (`wind 0.1`) = tail hangs slack/limp; storm (`wind 1`) = tail lifts toward horizontal. (This already works — don't regress it.)
- **AT-BANNER-MOUNT-4 ("floating" fix):** at rest the banner mounts to the cairn crown without a visible gap/float — addresses "hanging in the air." (Mount geometry, may be a sub-task.)
- **AT-BANNER-COLOR-5 (regression):** the 4 color identities (red/blue/white/black donors) still render correctly post-change.
- Logs-green ≠ playable: Daniel verifies in a forced thunderstorm, changing wind angle, in-game.

## Spec / routing
- §A2.1b describes wind response; update it to specify DIRECTIONAL alignment (transform rotation to `GetWindDir`), not just force-driven motion, so code+spec agree (AGENTS.md). No SpecCheck manifest impact (banner is cosmetic, not a recipe).
- **Clean-side:** all evidence from Valheim's own assembly/prefabs (permitted). Construct additively (ADR-0006); do not clone the sail/banner prefab; do not commit decompiled GlobalWind.cs. Route to **architect** for the design decision (port-GlobalWind-faithfully vs aligned-mesh-streamer), implementation lane **engineer-systems**, worktree-isolated.
- **Reusability:** the alignment belongs in `ClothWindDriver` (already feature-agnostic) so future sails/tents/flags inherit it.
