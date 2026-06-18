---
title: "Cairn banner won't stream in high wind (issue 10) — the gap is NOT force magnitude. The force knob is already tunable to 100× and Daniel already tested 20× (only ~10–15° lean). A geometric CONSTRAINT eats the deflection: the rock-drape collider cage + the vertical-hang gravity geometry."
status: current
last_updated: 2026-06-17
investigator: architect (clean-side — reads only our own additive code + vanilla EnvMan/Cloth, ADR-0001)
card: t_293f2df5 (issue 10)
spec_anchor: "docs/v0.1.0/planning/requirements.md §A2.1b (cairn banner wind response) — engineering items 5/6/7"
supersedes_nothing: "This CORRECTS the issue-10 card's root-cause premise; it does not supersede the §A2.1b spec, which already documents the live-config knobs this playbook drives."
---

# Issue 10: cairn banner barely wind-responsive — the premise correction

> **TL;DR for Daniel.** The issue-10 card says the wind force is hardcoded too weak
> (`Multiplier = 1f`) and asks us to make it tunable and raise it. **That work already
> shipped a week ago** — `SBPR_BannerWindMult` is a live config entry, range **0.1–100×**,
> and the commit that added it (`076b43e`, 2026-06-10) records that **you already pushed it
> to 20× and the banner still only leaned ~10–15°.** A *free* tail at 20× should lean **~64°**
> off vertical (`atan(20/9.8)`). The ~50° you're missing is not absent force — **something is
> physically holding the lower tail.** The two grounded suspects are (1) the **rock-drape
> collider cage** (default ON) and (2) the **vertical-hang geometry** (tilt default 0° — gravity
> fights every degree of lift). Both are already live-config toggles. **This is a 10-minute
> live-tune session, not a sixth code rewrite.** The ordered playbook is at the foot of this doc.

## Why this card needed a premise correction before any fix

The card is **factually grounded on the right file/line** (`ClothWindDriver.cs:136`,
`externalAcceleration = wind * Multiplier`, `Multiplier` default `1f`) but draws a **stale
conclusion**: that the multiplier is *hardcoded* and that building a tunable knob + a
diagnostic command is the remaining work. Every piece of that infrastructure is already in
`main` (v0.2.26-dev). Verified against this build:

| Card claim | Ground truth (this build) | Evidence |
|---|---|---|
| "`Multiplier` is still hardcoded at `1f`" | `driver.Multiplier = CfgBannerWindMult`, a **live `Config.Bind` entry**, range **0.1–100×**, default 1.0 | `CairnTag.cs:827`, `:446`; `Plugin.cs:165-170` |
| "Make it Config.Bind-tunable (cairn live-tune pattern)" | **Already done** — `SBPR_BannerWindMult` under `[CairnBanner]`; plus 13 sibling live knobs | `Plugin.cs:143-264` |
| "The `bannerdiag` dev command verifies the force reaching the solver" | **Already shipped** (PR #90/#96) — reads live `extAccel` mag, hang angle, every cfg | `BannerDiagCommand.cs`, `BannerDiagnostic.SnapshotNow()` |
| "Rule out the dead-solver / non-uniform-scale kill FIRST" | **Already ruled out in-world** — verdict `SOLVER=RUNNING`, `HANGS-DOWN`, alignment confirmed | requirements §A2.1b **engineering item 7** (records the attempt-#6 diagnostic result) |
| "`CairnTag.cs:408` admits ~1 m/s² can only jitter → raise the force" | That comment is in the **STIFFNESS** block (card t_a2fc3073). Its conclusion was the **opposite**: lowering *stiffness* lets the *existing* force deform the cloth; the line right above says **"intensity response already confirmed at 1.0"** | `CairnTag.cs:403`, `:406-417` |

So the card asks us to re-build tools that exist and to re-pull a lever (multiplier) that was
already pulled to 20× without success. Per this repo's hard-won doctrine ("logs-green ≠
playable"; the banner shipped wrong **twice** while CI was green; the **entire point** of the
live-config rig is that Daniel converges the feel in one joined session, *then* we bake), the
right architect move is to **correct the premise and hand back an ordered live-tune playbook**,
not to write a sixth blind code fix.

## The decisive number (why "raise the multiplier" is the WEAKEST lever, not the fix)

A particle hanging from a fixed mount, under horizontal wind force `F` and gravity `g`, rests
at lean angle `θ` off vertical where `tan θ = F / g`. With the wind driver's force =
`intensity × Multiplier` and a full storm `intensity ≈ 1.0`:

| `SBPR_BannerWindMult` | Expected FREE-tail lean `atan(mult/9.8)` |
|---|---|
| 1 (default) | ~6° |
| 20 (Daniel tested) | **~64°** |
| 100 (cap) | ~84° |

Daniel observed **~10–15°** at mult=20 (commit `076b43e` body). The free-tail model predicts
**~64°**. A ~50° shortfall means the effective horizontal force reaching the lower tail is
~2.6 m/s², not the ~20 commanded — **~87% of the deflection is being absorbed by a
constraint.** Raising the multiplier higher fights that constraint instead of removing it: if
the tail is mechanically pinned/caged, no multiplier in range frees it. **Remove the
constraint first; the multiplier is the last knob to touch, not the first.**

## Suspect 1 (LEAD — geometry-grounded): the rock-drape collider CAGE

`AddPileClothColliders` (`CairnTag.cs:578`, default **ON** via `SBPR_BannerRockDrape`) attaches
4 `SphereCollider`s down the pile axis so the banner "drapes against the rocks." But the tail's
horizontal standoff from the pile axis is only `SBPR_BannerOffsetXZ = 0.30 m`, while the sphere
radii are **0.46–0.58 m at the base**. The tail therefore starts its rest pose **inside** the
lower spheres, so the solver shoves those particles out to the sphere surface and **holds them
there** — the lower ~half of the tail is boxed against a sphere it can't pass through, in *every*
wind direction (the spheres are radially symmetric about the pile). Grounded geometry (tier 1
and tier 3, banner-local frame, X=0 is the tail plane):

```
tier 1: tail standoff 0.30 m from pile axis
  sphere0  r=0.46  nearest-tail-distance 0.30  → TAIL INSIDE (caged)
  sphere1  r=0.35  nearest-tail-distance 0.30  → TAIL INSIDE (caged)
  sphere2  r=0.24  clear
  sphere3  r=0.16  clear
tier 3: tail standoff 0.30 m
  sphere0  r=0.58  → TAIL INSIDE (caged)
  sphere1  r=0.44  → TAIL INSIDE (caged)
  sphere2/3 clear
```
(derivation: `scripts/banner-geom-check.py` in this PR, from the pile constants `CairnTag.cs:52-58`,
the banner offsets `:401-402`, and the collider placement `:594-616`.)

This is a credible "compiles-clean / renders-wrong" defect: the rock-drape was added so the tail
**rests on** the stones (Daniel's "flap against the stones"), but with the current radius-vs-
standoff numbers it **cages the lower tail** instead of letting it drape and stream. It is also
the **newest unconverged variable** — it landed in the same PR (#96) as the freed pin ramp, the
tilt knob, and the 100× cap, and there is **no record in the repo of a clean post-rock-drape
`[BannerSnap]` reading**. The premature-sampling trap (every prior `extAccel≈0.06` was dead-calm
world-load, Daniel's catch) means the only trustworthy reading comes from the on-demand
`bannerdiag` after a forced storm — which is exactly step 1 below.

> **Test, don't assume.** I cannot render Cloth headless, so I am NOT flipping the default. The
> playbook's step 3a toggles `SBPR_BannerRockDrape = false` and re-snapshots; if the hang angle
> jumps, the cage is confirmed and we bake the geometry fix (shrink radius / widen standoff /
> restrict spheres to the upper tail) in the follow-up card.

## Suspect 2 (the gravity geometry the multiplier can't beat): vertical hang

`SBPR_BannerTiltDegrees` default is **0°** — the banner hangs straight down from a top pin. A
vertical sheet must lift its whole mass against the full 9.8 m/s² of gravity to stream, so wind
deflection saturates early (`tan θ = F/g` again). The flagpole fix already exists
(`076b43e`, Daniel's "rotate 90° so it falls into place"): a **horizontal mount** (`tilt 60–90°`)
makes gravity droop the sheet *across its length* instead of opposing the lift, so even modest
wind streams it sideways. This knob shipped but **defaults to 0**, i.e. the streaming-friendly
geometry is currently off. It's step 3b.

## What is NOT the cause (already eliminated — don't re-chase)

- **Dead Cloth solver / non-uniform lossyScale kill** — ruled out in-world (`SOLVER=RUNNING`,
  `HANGS-DOWN`; requirements §A2.1b item 7). The card asks to rule this out "first"; it's done.
- **Missing directional alignment** — fixed (card t_1d7c0d19); `AlignToWindDirection` ON,
  `ClothWindDriver.ApplyWindAlignment()` yaws the sheet downwind (`ClothWindDriver.cs:145-184`).
- **Over-stiff cloth** — `stretch/bend` default 0.5 (vanilla sail), live-tunable to 0.1.
- **Over-pinned ramp** — `FreeRampExp` already linearized 2.0→1.0 (frees the whole body).
- **Force not delivered** — code is a faithful vanilla `GlobalWind` port (`:136-137`); the
  on-demand snapshot will confirm `extAccel ≈ intensity × mult` (step 1).

## The fix is a LIVE-TUNE PLAYBOOK (ordered, grounded), then a bake — not new code

Run on a **joined client** in a forced storm. Each step is an existing `[CairnBanner]` cfg knob;
`bannerdiag` after each reload reads back the truth.

1. **Baseline (the missing measurement).** `wind 0 1` → wait ~3 s → `bannerdiag`. Record from the
   `[BannerSnap]` line: `extAccel mag`, `hangAngleOffVertical`, and the printed cfg. This is the
   clean post-storm reading the repo has never captured. *Expected at default mult=1:* `extAccel≈1`,
   small hang. Confirms force IS reaching the solver and quantifies the deficit.
2. **Push the force alone to re-confirm Daniel's 20× result.** Set `SBPR_BannerWindMult = 20`,
   reload, re-`bannerdiag`. If hang is still ~10–15° (not ~64°), the constraint is confirmed →
   continue; do **not** keep raising the multiplier.
3. **Remove the constraint, in this order, re-snapshotting after each:**
   - **3a (lead): `SBPR_BannerRockDrape = false`.** If the hang angle jumps toward the predicted
     lean, the collider cage was the culprit. ← most likely single fix.
   - **3b: `SBPR_BannerTiltDegrees = 60` then `90`.** Horizontal flagpole mount; gravity now aids
     streaming instead of opposing it.
   - **3c: `SBPR_BannerStretchStiffness` / `SBPR_BannerBendStiffness → 0.2`**, `SBPR_BannerClothDamping → 0.05`.
     Final softness if the sheet still reads stiff.
4. **Converge the feel** (Daniel's eyeball: full flag-in-a-storm vs calmer heraldic sway — the
   card's open question) and **record the winning combo**.
5. **Bake** the chosen values into the `CairnTag.Default*` consts + the §A2.1b rows, and — if 3a
   won — fix the rock-drape geometry so the feature can stay on without caging the tail. That is a
   **follow-up `engineer-systems` card** (code + spec in one PR), not this one.

## Acceptance tests (refining the card's — LEAD with the constraint, not the force)

- **AT-BANNER-SNAP-1 (baseline):** a clean forced-storm `[BannerSnap]` exists showing
  `extAccel ≈ intensity × mult` (force delivery proven) AND the measured `hangAngleOffVertical`.
- **AT-BANNER-CAGE-2 (lead hypothesis):** toggling `SBPR_BannerRockDrape` off materially increases
  the storm hang angle → the collider cage is confirmed/refuted on evidence, not assumption.
- **AT-BANNER-STREAM-3:** with the converged knobs, in high wind the banner clearly streams/billows
  downwind (not jitter) — flag-in-a-storm.
- **AT-BANNER-RESPONSIVE-4:** motion scales with intensity — calm hangs/gentle, storm streams; the
  calm→storm difference is obvious.
- **Regression-5:** the hard-pinned mount band still holds (banner doesn't blow off the pole); no
  return of the over-stiff/collapsed states (#78/#96 history).
- **Logs-green ≠ playable:** closes only on Daniel's joined-client eyeball in real wind.

## Routing / clean-room / spec

- **Clean-side, ADR-0006-clean.** Reads only our own additive banner code + vanilla `EnvMan`/`Cloth`.
  No third-party mod code, no prefab cloning, no decompiled source committed.
- **No code change in this PR** (docs + a read-only geometry script). The §A2.1b spec already
  documents the live-config knobs this playbook drives, so spec and code are already consistent —
  there is nothing to move in lockstep here. The **bake / rock-drape-geometry fix** is the
  code-touching follow-up, gated on Daniel's live result.
- **Honesty note:** the rock-drape cage is a *grounded hypothesis from headless geometry*, not an
  in-world confirmation. Step 3a is designed to confirm or kill it on evidence before any default
  flips.
