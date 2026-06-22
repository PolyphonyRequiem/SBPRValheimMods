---
title: "Sunstone Lens solar ring → 3D pulsing sun-corona disc — impl-spec (buildable; engineer-ui)"
status: proposed
purpose: "Buildable spec graduating the /bug report on card t_2d500d45 (Discord ticket-diegetic-halo-render, Daniel 2026-06-22): the Sunstone Lens' 'solar ring' is a flat screen-space annulus (SunstoneLensHudOverlay._emptyRing :164, sprite RingSprite() :455, colour CSolarRing :112 = RGBA 0.98/0.78/0.36/0.18), never the 3D slowly-pulsing 'sun corona' disc Daniel pictured. This doc is the buildable HOW: REPLACE the flat _emptyRing with a WORLD-SPACE corona disc (a glowing solar disc co-located with the trophy halo — the substrate the fixed-distance trophy ring orbits, t_10bacccf/PR #248) that breathes on a slow alpha pulse. The gate dissolved (Daniel declined to pre-approve — 'why am I needed'): every visual knob is resolved by a REVERSIBLE live-config DEFAULT (orientation→GroundPlane, art→filled glow, pulse→~0.25 Hz, replace-not-stack), so no pre-build look-lock is required — Daniel's in-game eye on a GPU client is the acceptance test AFTER ship, not a gate before it. The pulse envelope is the engine-free, CI-gated SunstoneCoronaPulse (link-compiled into the test project, the SunstoneHaloGeometry precedent) so the locked envelope shape can't drift. Supersedes the flat-pulse no-minimap half of t_acaa0190 (docs/design/sunstone-lens-aura.md); its minimap-rim re-homing half is a SEPARATE parked follow-up (NOT built here, NOT blocked on). Every code line re-grounded against main @ 21f7a92 (#248). Render-only: SpecCheck +0, no new prefab (ADR-0006 additive), clean-side (ADR-0001). Daniel gates the impl-spec at doc review AND the in-game ATs; engineer-ui builds it."
owner: Daniel (design + merge authority); Starbright (architect — spec); engineer-ui (impl)
design_source: "docs/design/sunstone-lens-trophy-ring.md §1.6 (the empty-state affordance, world-space-resolved here) + card t_2d500d45 (the /bug redirect)"
supersedes_partial:
  - "docs/design/sunstone-lens-aura.md (t_acaa0190, PROPOSED) — the FLAT-PULSE no-minimap half ('EXTEND + ANIMATE the 2D _emptyRing') is superseded: the no-minimap pulsing art is now this 3D corona, not an animated flat ring. The doc's minimap-rim re-homing half (aura on the vanilla-minimap rim / carry-disc bezel when a minimap owns detection) SURVIVES as a parked follow-up — unbuilt, conditional on Daniel still wanting it on a minimap surface."
  - "docs/design/sunstone-lens-trophy-ring.md §1.6 (:453-468) + the §5 carried-over-locked line (:732-733) — the 'faint solar ring … either surface is acceptable … screen-space as the lower-risk choice' escape hatch is RESOLVED to world-space: the empty-state affordance is the 3D corona, the flat screen-space annulus is removed."
---

# Sunstone Lens solar ring → 3D pulsing sun-corona disc — impl-spec (buildable)

The report ([card `t_2d500d45`](#links), Daniel via `/bug`, ticket
`ticket-diegetic-halo-render`, 2026-06-22) is the locked *what*, verbatim:

> "the ring itself is just a screen space circle, not a 3d slowly pulsing 'sun
> corona' disc like we discussed."

This doc is the buildable *how*. It is the structural sibling of the world-space
**trophy halo** ([`sunstone-lens-trophy-ring.md`](../../design/sunstone-lens-trophy-ring.md),
PR #242 → fixed-distance #248) — the corona is the **world-space substrate that
the trophy halo orbits**, drawn into the **same scene root**, so the two read as
**one coherent world element** (a sun on the floor with creature trophies floating
around it) instead of a flat HUD outline plus a separate 3D ring.

> **Clean-side (ADR-0001):** every vanilla fact cited is base-game
> (`Character.GetEyePoint`, `Billboard`, `GameCamera`/`Utils.GetMainCamera`,
> `Player`) — fair to read and adapt. No third-party mod code is read or copied.
> SBPR lines are `main` @ `21f7a92` (#248), re-grounded this pass (the report's
> stale line refs are corrected in §1).
>
> **ADR-0006 (additive):** the corona is built additively (`new GameObject()` +
> `AddComponent` of a `MeshRenderer`/`MeshFilter` quad **or** a world-space
> `Canvas`+`Image`, engineer's call §2.3) into the existing world-content scene
> root. No vanilla prefab is `Instantiate`d-and-stripped; the gold sprite is
> generated procedurally (reading no asset).
>
> **SpecCheck/manifest impact: NONE.** Render-only; no recipe/piece/station/item
> change. The Sunstone Lens recipe row in `SpecCheck.cs` is untouched.

---

## 0. The decisions (build constraints) — RESOLVED BY REVERSIBLE DEFAULT, not gated

The design gate **dissolved**: Daniel declined to pre-approve the look (*"why am I
needed"*, 2026-06-22). The knobs below are therefore **architect defaults made
reversible via live config** — Daniel flips any of them on a joined GPU client
without a rebuild (the banner-windsock pattern this codebase already uses for
`LensHaloRadius`/`MinimapHandoffMode`). **His in-game eye is the acceptance test
AFTER ship (§3 ATs), not a gate before build.** Nothing here waits on Daniel.

| # | Knob | 🟢 Default (reversible) | Live config | Build consequence |
|---|------|------------------------|-------------|-------------------|
| 1 | **Orientation** | **GroundPlane** ("sun on the floor") | `CoronaOrientation` enum `{ GroundPlane, CameraFacing }` | GroundPlane → a flat disc in the **XZ plane** (`Quaternion.Euler(90,0,0)`), centred on the player, radius tracks the trophy ring. CameraFacing → an upright **Billboard** disc on the eye anchor (the trophy-slot idiom). The enum picks the render primitive at build; flipping it live re-orients without a rebuild. **Rationale for GroundPlane default:** #248 puts trophies on a horizontal fixed-distance ring at real bearings; a flat floor corona is the surface they orbit — camera-facing would visually divorce the corona from the trophies. |
| 2 | **Corona art** | **filled radial glow + radiant edge** (a sun, not a hoop) | `CoronaInnerFill` (0=thin hoop ↔ 1=filled sun-disc), `CoronaThickness` (radiant-edge falloff) | A procedural radial-falloff sprite (§2.4), gold = `CSolarRing` RGB **(0.98, 0.78, 0.36)** → `rgba(250,199,92)`. Replaces the thin 3 px annulus. |
| 3 | **Pulse** | **~0.25 Hz** (one breath / 4 s); α envelope **0.10 ↔ 0.28** around the existing 0.18 static baseline | `CoronaPulseHz`, `CoronaAlphaTrough`, `CoronaAlphaPeak` | One shared `Time.time` phase via the engine-free `SunstoneCoronaPulse` (§2.2). No drift across orientation flips or vs the trophy tint. |
| 4 | **Replace vs coexist** | **REPLACE** — the corona *is* the empty-state art; the flat screen-space `_emptyRing` is **removed** | (n/a — structural) | One coherent world-space element. The corona shares `SunstoneWorldRing`'s scene root so corona + trophies cull/dispose together. The screen-space circle is gone (the literal fix for the report). |
| 5 | **t_acaa0190 relationship** | **Supersede the flat-pulse no-minimap half**; **park** the minimap-rim half | (n/a — doc) | This corona is the no-minimap pulsing art. The aura-on-a-minimap-surface idea survives ONLY as a parked follow-up (§6), conditional on Daniel still wanting it — **not built here, not blocked on**. |

**Directional constants frozen from Starbright's look-lock mock** (carried as
engineer fine-tunes; the in-game AT on a GPU client is the final value):
`CoronaPulseHz = 0.25`, `CoronaAlphaTrough = 0.10`, `CoronaAlphaPeak = 0.28`
(around the 0.18 baseline), `CoronaThickness = 0.45`, `CoronaInnerFill = 0.35`.
Mock + frozen-constant provenance: ticket thread `1518684394846687495`, interactive
HTML `https://files.catbox.moe/iezmko.html` (sha256
`f0f6b6b360dd4ba213beac4a4d628c99289896eaa2651660ce667c49184cd7b9`).

---

## 1. Grounding — the flat ring that exists today, re-verified @ main `21f7a92`

The Sunstone Lens "solar ring" is a **flat screen-space annulus**, never world-space.
(The report's line refs are stale; corrected here — same catch the prior architect
pass made on `CSolarRing`.)

| What | Where (`main` @ `21f7a92`) | Report said |
|---|---|---|
| The ring `Image` (`_emptyRing`) | `SunstoneLensHudOverlay.cs:164`, parented under `_content` (the screen-space HUD visibility child, `:159`) | `:164` ✅ |
| Its colour `CSolarRing` | **`:112`** = `new Color(0.98f, 0.78f, 0.36f, 0.18f)` — warm amber/gold, α 0.18 | `:101` ❌ stale (RGB exact) |
| Its sprite | procedural thin annulus `RingSprite()` **`:455`** (256 px, **3 px-thick** white ring, tinted by `Image.color`) | `:579` ❌ stale |
| Fixed screen radius | `SolarRingRadiusPx = 140f` (`:71`) — a screen-space px size, NOT world metres | — |
| Shown when worn+charged | `RenderWorldHalo` `:399-404` (`_emptyRing.SetActive(showEmpty)`, colour re-stamped each frame to the **static** `CSolarRing`) | — |
| Half-alpha depleted variant | `:262-266` (`CSolarRing.a * 0.5f`), gated by `ShowDepletedHint` (default **OFF**) | — |
| **No pulse anywhere** | grep `Mathf.Sin(Time` / `PingPong` on a UI alpha → **zero hits**; the only `Mathf.Sin` calls are geometric | confirmed ✅ |
| Trophies DID go world-space | `SunstoneWorldRing.cs` (#242 → #248): a head-halo of billboarded trophies in a scene root `SBPR_SunstoneWorldHalo` (`:112`), owned by the overlay, NOT under `Hud.m_rootObject` | — |

**So the ring reads as a flat 2D circle on the HUD because it literally is one** —
only the trophies graduated to world space. This card graduates the ring too, and
upgrades it from a static thin hoop to a pulsing filled corona.

**🔵 No aura code to refactor away.** `t_acaa0190` (`sunstone-lens-aura.md`) shipped
a **design doc only** — `git grep -i 'LensAura|CoronaPulse|Pulse'` over `src/` is
**empty**. The supersession (§6) is doc-only; there is no half-built aura to unwind.

---

## 2. Render architecture — the world-space corona disc

### 2.1 Where it lives — the shared scene root (Knob #4: replace, one coherent element)

`SunstoneWorldRing` already owns a world-content scene root `SBPR_SunstoneWorldHalo`
(`SunstoneWorldRing.cs:112`) — a plain `GameObject` at world origin (identity/one),
toggled for visibility, never under `Hud.m_rootObject` (the #209 discipline: only
the visuals toggle; the host MonoBehaviour's `Update` pump stays alive). **The corona
renders into this SAME root** so corona + trophies are one coherent world element that
shows/hides/disposes together.

> **Engineer's-call sub-choice (not a blocker):** the corona may be (a) a `corona`
> child built inside `SunstoneWorldRing.EnsureBuilt()` (tightest coupling, literally
> "shares the root"), or (b) its own small `SunstoneCoronaDisc` class that parents
> under a root transform `SunstoneWorldRing` exposes (cleaner separation, still the
> shared root). Either satisfies the spec requirement: **shared scene root, single
> visibility/dispose lifecycle.** Pick whichever keeps `SunstoneWorldRing` legible.

The flat `_emptyRing` screen-space `Image` and its `RingSprite()` generator are
**removed** from `SunstoneLensHudOverlay` (Knob #4). `CSolarRing` (`:112`) is **kept**
— it's the corona's gold. `SolarRingRadiusPx` (`:71`) is removed (screen-space).

### 2.2 The pulse — engine-free, CI-gated `SunstoneCoronaPulse` (the load-bearing fence)

The slow alpha breath is computed by a **new engine-free** policy file
`SunstoneCoronaPulse.cs`, mirroring `SunstoneHaloGeometry` exactly: pure math, no
`UnityEngine`/Valheim refs, **link-compiled into the test project** so the locked
**envelope shape** is CI-gated headless and cannot silently regress (the render lives
in engine code; the POLICY lives here).

```csharp
namespace SBPR.Trailborne.Features.Sunstone
{
    /// <summary>
    /// Pure pulse-envelope math for the Sunstone Lens corona. Engine-free (no
    /// UnityEngine) so tests/SunstoneCoronaPulseTests.cs can gate the breathing
    /// envelope headless — the locked SHAPE (one shared phase, clamped to
    /// [trough,peak], periodic at Hz) can't drift. net48: System.Math only (no MathF).
    /// </summary>
    public static class SunstoneCoronaPulse
    {
        /// <summary>
        /// The breathing alpha at wall-clock <paramref name="time"/> seconds
        /// (pass Time.time — one shared phase so every consumer breathes in
        /// lockstep, no drift). <paramref name="hz"/> = breaths/sec (Knob #3 rate);
        /// the alpha swings between <paramref name="trough"/> and
        /// <paramref name="peak"/> (Knob #3 depth) on a sinusoid. trough/peak are
        /// clamped + ordered defensively so a fat-fingered .cfg (peak &lt; trough)
        /// degrades to a steady glow, never an inverted or NaN pulse.
        /// </summary>
        public static float AlphaAt(double time, float hz, float trough, float peak)
        {
            float lo = Clamp01(trough);
            float hi = Clamp01(peak);
            if (hi < lo) { float t = lo; lo = hi; hi = t; }   // order defensively
            // s ∈ [0,1]; 0.5*(1+sin) so a 0-phase start sits mid-breath rising.
            double s = 0.5 * (1.0 + System.Math.Sin(time * (2.0 * System.Math.PI * hz)));
            return (float)(lo + (hi - lo) * s);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
```

The render loop writes `corona.color = new Color(CSolarRing.r, CSolarRing.g,
CSolarRing.b, SunstoneCoronaPulse.AlphaAt(Time.time, hz, trough, peak))` each frame.
Because the phase is `Time.time` (not a per-object accumulator), every surface that
could show the corona — and a future re-homed minimap aura, if §6 is ever built —
breathes in lockstep by construction (**AT-CORONA-PULSE** no-drift).

### 2.3 The two orientations (Knob #1) — one primitive switch

The corona is a flat disc; orientation only changes how it's anchored + rotated:

- **GroundPlane (default)** — a horizontal disc in the **XZ plane**: rotate the quad
  flat (`Quaternion.Euler(90f, 0f, 0f)`), centre it on the player's **ground**
  position (the character-root transform, ≈ the feet) lifted by `CoronaPlaneOffsetY`,
  world radius = `CoronaRadius` (tracks the trophy `HaloRadius` by default, §2.5).
  This is the "sun on the floor" the trophies orbit. **No Billboard** — it stays
  flat regardless of camera.
- **CameraFacing** — an upright disc carrying the vanilla `Billboard` (`m_vertical =
  true`, the trophy-slot idiom, `SunstoneWorldRing.cs:252`), centred on the eye
  anchor (`player.GetEyePoint()`, grounded `:389`). Yaws to face the camera, stays
  upright. The trophy-halo billboard idiom, reused.

A single `if (orientation == GroundPlane)` branch picks the transform + whether the
`Billboard` component is enabled. Flipping the live enum re-orients next frame (the
disc is rebuilt/re-anchored on change) — no rebuild.

> **Anchor note (verify on the GPU client, not asserted from decomp):** GroundPlane
> centres on the character-root transform (feet); CameraFacing on `GetEyePoint()`
> (already grounded). The exact feet read is the engineer's anchor detail —
> `CoronaPlaneOffsetY` exists precisely so Daniel nudges the disc's height to taste
> in-game rather than us hard-committing a Y here.

### 2.4 The corona sprite — procedural radial glow (Knob #2)

A new procedural sprite generator (sibling of the removed `RingSprite()`), 256 px,
**filled radial falloff** instead of a thin annulus:

- **`CoronaInnerFill`** (0 → 1): the inner radius where the glow is fully opaque.
  `0` = a thin hoop (today's look); `1` = a fully filled sun-disc bright to the
  centre. Default `0.35` (between a hoop and a filled sun).
- **`CoronaThickness`** (0 → 1): the soft radiant-edge falloff width from the filled
  core out to fully transparent at the rim. Default `0.45` (a soft sun corona, not a
  hard edge).
- White texture, tinted at draw by `CSolarRing` gold; the per-frame **alpha** comes
  from the pulse (§2.2), so the texture itself is alpha-1 and `Image.color`/material
  colour multiplies the breathing alpha in. No shipped PNG (the procedural guarantee
  holds even if the modpack ships zero art — the existing `RingSprite()` rationale).

### 2.5 Sizing — track the trophy ring by default (the "substrate" relationship)

`CoronaRadius` (world m) defaults to the trophy `HaloRadius` value (~2.0 m, the #248
fixed-distance ring) so the corona's rim sits where the trophies orbit — the disc
**is** the substrate. It's an independent live knob (Daniel can grow the floor disc
without moving the trophies, or vice-versa); the *intent* is that they track, so the
default mirrors `SunstoneWorldRing.DefaultHaloRadius`. Engineer may bind it as a
multiplier on `LensHaloRadius` instead of a standalone metre value — engineer's call,
as long as the default visually couples them.

### 2.6 Wiring into the overlay (the gate is FREE — it rides the existing one)

`SunstoneLensHudOverlay.RenderWorldHalo` (`:378`) already runs only when the lens is
**worn AND charged** (the `Update` early-returns at `:233-269` handle not-worn /
depleted; `RenderWorldHalo` is the worn+charged body). The corona is driven from the
exact spot the flat `_emptyRing` is today:

- `RenderWorldHalo` (`:399-404`): replace the `_emptyRing.SetActive(showEmpty)` block
  with a `_corona.Render(anchor, orientation, radius, innerFill, thickness, Time.time,
  hz, trough, peak)` call, gated by the same `ShowEmptyRing` config (repurposed §4).
- Depleted-hint branch (`:259-266`, `ShowDepletedHint` default OFF): if Daniel ever
  turns the hint on, show the corona dimmer (e.g. peak α × 0.5) with no pulse, or a
  slower pulse — parity with today's half-alpha flat ring. Default OFF = corona off
  when depleted (unchanged behaviour).
- `SetVisible(false)` (`:205-227`) and `OnDestroy` (`:441-444`): the corona hides +
  disposes with the trophy halo (it's in the same root — one `Hide()`/`Dispose()`).
  **#209 invariant preserved**: visibility toggles the world-content child, never the
  host MonoBehaviour — the corona cannot freeze the `Update` pump.

---

## 3. Named acceptance tests — AT-CORONA-* (logs-green ≠ playable — Daniel's in-game eye is the accept)

- **AT-CORONA-3D** — the corona renders in **world space** (its own world-content
  scene root, NOT under `Hud.m_rootObject`, NOT a screen-space `Image`). The flat
  screen-space circle from the report is **gone**.
- **AT-CORONA-ORIENT** — `CoronaOrientation = GroundPlane` → a flat horizontal disc
  in the XZ plane on the player (sun on the floor); `= CameraFacing` → an upright
  camera-facing billboard disc on the eye anchor. **Live-flippable, no rebuild.**
- **AT-CORONA-PULSE** — the corona breathes: alpha swings `CoronaAlphaTrough ↔
  CoronaAlphaPeak` at `CoronaPulseHz`, on **one shared `Time.time` phase** (no drift
  vs the trophy tint, and no phase jump when the orientation enum is flipped mid-breath).
- **AT-CORONA-PULSE-MATH** (🔴 engine-free, CI-gated) — `SunstoneCoronaPulse.AlphaAt`:
  the returned alpha is always within `[trough, peak]`; it is periodic at `hz`
  (`AlphaAt(t) == AlphaAt(t + 1/hz)`); the breath midpoint and the trough/peak anchor
  points land where the sinusoid says; `peak < trough` is reordered (no inverted
  pulse, no NaN); `hz = 0` degrades to a steady mid-value. Mirrors
  `SunstoneHaloGeometryTests`; link-compiled, runs headless under net8.0.
- **AT-CORONA-ART** — the disc is a **filled radial glow with a soft radiant edge**
  (per `CoronaInnerFill` / `CoronaThickness`), gold = `CSolarRing` RGB **(250,199,92)**
  — not the old thin 3 px hoop. `CoronaInnerFill = 0` degrades to a hoop; `= 1` to a
  filled sun-disc.
- **AT-CORONA-GATED** — the corona shows **iff** the lens is worn AND `m_durability ≥
  MinChargeToDetect`. Unworn → none (`Update :241-246`). Depleted → none (or the dim
  hint per `ShowDepletedHint`, default OFF). **No corona on a dedicated server** (no
  `Hud` → the overlay never mounts).
- **AT-CORONA-SUBSTRATE** — the corona shares `SunstoneWorldRing`'s world-content
  scene root (one coherent element): hiding the lens overlay hides corona **and**
  trophies together; the corona's rim default-tracks the trophy `HaloRadius`.
- **AT-CORONA-PUMP** (🔴 #208/#209 guard) — hiding/showing the corona toggles the
  world-content child, **never** the host overlay GameObject; the `Update` detection
  pump never freezes (the same self-deactivating-host bug class the Iron Compass and
  the Lens HUD both already fixed).
- **AT-CORONA-CLEAN** (clean-room) — no third-party mod code; all hooks are base-game
  primitives (`Character.GetEyePoint`, `Billboard`, `Utils.GetMainCamera`, `Player`)
  + SBPR-owned types (`SunstoneLensHudOverlay`, `SunstoneWorldRing`,
  `SunstoneCoronaPulse`). Procedural sprite, **no new art**. ADR-0006: additive
  (`new GameObject()` + `AddComponent`), no prefab clone.
- **AT-CORONA-BUILD** — `dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c
  Release` → **0 errors / 0 warnings** (`TreatWarningsAsErrors` is ON); `dotnet test`
  green including the new `SunstoneCoronaPulseTests`.

---

## 4. Config + assets impact

**Config (Plugin `SunstoneLens` section, all live-tunable — the banner-windsock
pattern; defaults baked as `Default*` consts on the corona class, single source of
truth, range-clamped so a fat-finger in the `.cfg` can't blow the disc up):**

| Knob | Default | Range | Role |
|---|---|---|---|
| `CoronaOrientation` | **GroundPlane** | enum `{GroundPlane, CameraFacing}` | flat floor disc vs camera-facing billboard (Knob #1) — **live-flippable** |
| `CoronaPulseHz` | **0.25** | 0.05 – 2.0 | breaths/sec (one breath / 4 s); 0 = steady glow (Knob #3 rate) |
| `CoronaAlphaTrough` | **0.10** | 0.0 – 1.0 | alpha at the breath trough (Knob #3 depth) |
| `CoronaAlphaPeak` | **0.28** | 0.0 – 1.0 | alpha at the breath peak (around the 0.18 static baseline) |
| `CoronaInnerFill` | **0.35** | 0.0 – 1.0 | 0 = thin hoop ↔ 1 = filled sun-disc (Knob #2) |
| `CoronaThickness` | **0.45** | 0.0 – 1.0 | soft radiant-edge falloff width (Knob #2) |
| `CoronaRadius` | **~2.0** (tracks `HaloRadius`) | 0.5 – 8.0 | world-m rim radius — the substrate the trophy ring orbits (§2.5) |
| `CoronaPlaneOffsetY` | **0** | -2.0 – 2.0 | vertical lift of the disc off its anchor (drop to the feet for ground-plane) |
| `ShowEmptyRing` | **true** (repurposed) | bool | master on/off for the corona (was the flat-ring toggle; same key, now gates the corona — minimal `.cfg` churn) |

**Removed** with the flat ring: `SolarRingRadiusPx` (`:71`, screen-space). **Kept:**
`CSolarRing` (`:112`, the corona gold), `ShowDepletedHint` (default OFF), `RingMaxIcons`
+ all `Halo*`/detection knobs (untouched — trophy-halo concern). `ShowEmptyRing` is
**repurposed** from "show the flat ring" to "show the corona" (documented in its
`ConfigDescription`); engineer may rename to `ShowCorona` only if willing to eat the
`.cfg`-key migration — default is keep the key.

**Assets:** **NO new asset.** The corona sprite is procedural (§2.4); gold is the
existing `CSolarRing`. No shipped PNG/shader/material.

**Files the impl card touches:**
- **NEW** `src/SBPR.Trailborne/Features/Sunstone/SunstoneCoronaPulse.cs` — engine-free
  pulse envelope (§2.2).
- **NEW or folded** corona render (§2.1 engineer's-call: a `SunstoneCoronaDisc.cs`
  sibling, or a corona child inside `SunstoneWorldRing.cs`). Either way it renders into
  `SunstoneWorldRing`'s scene root and exposes `Render(...)` / `Hide()` / `Dispose()`.
- **EDIT** `SunstoneLensHudOverlay.cs` — remove `_emptyRing` + `RingSprite()` +
  `SolarRingRadiusPx`; drive the corona from `RenderWorldHalo` + the depleted branch;
  hide/dispose it with the world halo (§2.6). Update the file-header banner (it still
  describes "the faint SOLAR RING … kept screen-space").
- **EDIT** `Plugin.cs` — add the `Corona*` binds in the `SunstoneLens` section
  (mirror the `LensHalo*` + `LensMinimapHandoffMode` live-enum idiom); repurpose the
  `ShowEmptyRing` `ConfigDescription`.
- **EDIT** `tests/SBPR.Trailborne.Tests.csproj` — add `<Compile Include="../src/.../
  SunstoneCoronaPulse.cs" />` to the engine-free link-compile manifest.
- **NEW** `tests/SunstoneCoronaPulseTests.cs` — AT-CORONA-PULSE-MATH.
- **Docs (spec-and-code together, AGENTS.md):** this impl-spec;
  `docs/design/sunstone-lens-trophy-ring.md` §1.6 + the §5 carried-over line
  (flat ring → world-space corona); `docs/design/sunstone-lens-aura.md`
  (supersede the flat-pulse half, park the minimap half, §6);
  `docs/datasets/PIECES_AND_CRAFTABLES.md` (Lens Render/Visual/Config rows);
  `PLAYER_GUIDE.md` (the lens paragraph: "a pulsing sun-corona on the ground");
  `docs/v3/planning/index.md` (manifest row, two-file rule).

**SpecCheck manifest: no change** (render-only — no recipe/piece/station/mechanic).

---

## 5. Clean/dirty routing + ADR notes

**Clean/dirty: CLEAN-SIDE.** 🟢 All SBPR-authored (`Features/Sunstone/`). Reads vanilla
`Character.GetEyePoint`, `Billboard`, `Utils.GetMainCamera`, `Player`, `Time.time`
only — base-game, fair to read+adapt (ADR-0001). No third-party mod code. The
"diegetic glowing detector" feel is reproduced from vanilla primitives. **Route
`architect` (this doc) → `engineer-ui` (impl).** **SpecCheck +0** (render-only).

**ADR-0006 (additive):** the corona is `new GameObject()` + `AddComponent`
(`MeshRenderer`/`MeshFilter` quad **or** world-space `Canvas`+`Image`, §2.3),
carrying NO `ZNetView`/`Piece`/networked skeleton — purely cosmetic, client-local.
No vanilla prefab is cloned-and-stripped; the gold sprite is generated procedurally.

**Headless-render caveat (honesty rule):** this box cannot render the result —
Valheim shaders collapse on the headless server. **AT-CORONA-3D / ORIENT / PULSE /
ART / GATED / SUBSTRATE / PUMP are Daniel's in-game confirms on a GPU client**, not
claimable from here. AT-CORONA-PULSE-MATH and AT-CORONA-BUILD **are** headless-CI
confirmable (engine-free math + a clean compile).

---

## 6. t_acaa0190 reconciliation — supersede the flat-pulse half, park the minimap half

`docs/design/sunstone-lens-aura.md` (card `t_acaa0190`, **PROPOSED**, shipped as a
design doc only — no code) proposed two things:

1. **Pulse the flat `_emptyRing`** (the no-minimap home). → **SUPERSEDED.** The
   no-minimap pulsing art is now this **3D corona**, not an animated 2D ring. Daniel's
   `/bug` here is exactly the redirect: he wants the 3D corona, not a flat pulse.
2. **Re-home that pulsing aura onto the minimap rim** (vanilla corner map rim /
   carry-disc bezel) when a minimap owns detection, riding the shipped
   Sunstone→minimap handoff. → **PARKED follow-up.** This is a *different* surface
   (the minimap, not the no-minimap world) and a *different* concern (the §2 bezel
   colour collision with the Iron Compass tint). It is **not built here and not
   blocked on**, and per Daniel (card t_2d500d45 comment, 2026-06-22) it is
   *conditional on him still wanting the aura on a minimap surface*. If he confirms he
   wants it, cut a fresh `engineer-ui` card then — do not pre-emptively spawn it.

The aura doc keeps `status: proposed` (the minimap-rim half is still a live, unbuilt
proposal) but gains a supersession banner pointing here for the flat-pulse half. Its
`supersedes_partial` of trophy-ring §1.6 (the flat ring → optional pulse) is itself
superseded by this doc's stronger move (flat ring → 3D corona).

---

## 7. Sibling docs that move in this PR

- `docs/design/sunstone-lens-trophy-ring.md` — §1.6 (`:453-468`): the "either surface
  is acceptable … screen-space as the lower-risk choice" escape hatch is **resolved to
  world-space** (the empty-state affordance is the 3D corona); the §5 carried-over line
  (`:732-733`, "faint solar ring empty-state default ON") re-points to the corona. The
  trophy halo's own geometry (#248) is unchanged — the corona is its substrate.
- `docs/design/sunstone-lens-aura.md` — the §6 supersession banner (flat-pulse half
  superseded here; minimap-rim half parked).
- `docs/datasets/PIECES_AND_CRAFTABLES.md` — Sunstone Lens Render/Visual/Config rows
  (flat ring → pulsing corona; the new `Corona*` config keys).
- `PLAYER_GUIDE.md` — the Lens paragraph (the empty-state cue is "a slow-pulsing sun
  corona on the ground around you," not a faint HUD ring).
- `docs/v3/planning/index.md` — manifest row for this new spec (two-file rule).

## Links

- **Report (the /bug):** card `t_2d500d45`, Discord ticket `ticket-diegetic-halo-render`
  (`1518684394846687495`), Daniel 2026-06-22. Look-lock mock (informational, gate
  dissolved): `https://files.catbox.moe/iezmko.html`.
- **Substrate sibling (the trophy halo this corona underlies):**
  [`../../design/sunstone-lens-trophy-ring.md`](../../design/sunstone-lens-trophy-ring.md)
  (PR #242 → fixed-distance #248, card `t_10bacccf`);
  `Features/Sunstone/SunstoneWorldRing.cs`, `SunstoneHaloGeometry.cs` (the engine-free
  CI-gated precedent this corona's pulse math mirrors).
- **Superseded/parked design:** [`../../design/sunstone-lens-aura.md`](../../design/sunstone-lens-aura.md)
  (card `t_acaa0190`).
- **Render host + the flat ring being replaced:**
  `Features/Sunstone/SunstoneLensHudOverlay.cs` — `_emptyRing` `:164`, `CSolarRing`
  `:112`, `RingSprite()` `:455`, `RenderWorldHalo` `:378`, the #209 `SetVisible`
  discipline `:205`.
- **The shipped minimap handoff (read, not modified — the parked §6 half would ride
  it):** [`sunstone-minimap-handoff-impl-spec.md`](sunstone-minimap-handoff-impl-spec.md)
  (card `t_91e86951`); `Features/Sunstone/LensHandoffDecision.cs`.
