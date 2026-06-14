---
title: "Stone of Drought — water-repel feasibility (research)"
status: idea
purpose: "Feasibility/scoping research for a 'Stone of Drought' placeable that carves a curved divot in the water around it (repels water). Grounded in vanilla water internals (WaterVolume / GetWaterSurface) — NOT in the Rune Magic mod's code (clean-room wall, ADR-0001). Captures what it would actually take, the hard part (visual vs logical water are two systems), and a phased path. No build decision yet."
---

# Stone of Drought — water-repel feasibility (research)

> 🌱 **RESEARCH / FEASIBILITY — not a build order.** Daniel asked "what would it take" to build a
> Stone of Drought: placed in the world, it carves a **curved divot in the water around it**,
> repelling it. This doc scopes that against the REAL vanilla water system. It is grounded in
> Valheim's own internals; it does **NOT** read or reproduce the Rune Magic mod's implementation.

## 🔴 Clean-room wall (ADR-0001) — READ FIRST
Daniel noted "rune magic does something similar." **Rune Magic is a third-party mod. We do NOT
read, copy, or port its code.** We reproduce the *behavior* (a local water-repel divot) from
**vanilla Valheim internals only**, which we are free to read and adapt. If we ever want true
parity with Rune Magic's specific effect, that requires the formal clean-room RE wall (a
`reviewer-cleanroom` writes a behavioral description, a separate implementer reproduces it) — but
the analysis below shows we don't need Rune Magic at all; vanilla gives us the hooks.

## What Daniel asked for (verbatim + attributed)
> "Look into what it would take to build a 'stone of drought' item that when placed in the world
> creates a curved divot in the water, essentially repelling it. I know that rune magic does
> something similar." — Daniel, 2026-06-13

## 🟢 How vanilla water actually works (GROUNDED vs decomp — the load-bearing finding)
The single most important fact for scoping this: **Valheim has TWO water systems that must agree,
and they are driven differently.**

1. **LOGICAL water height (gameplay).** Everything that asks "is this point underwater / how deep"
   funnels through **`WaterVolume.GetWaterSurface(Vector3 point, float waveFactor)`** (decomp
   `assembly_valheim.decompiled.cs:127768`). The static aggregator
   `Floating.GetWaterLevel(p, ref WaterVolume)` (`:108425`) and `GetWaterLevel(p, type, waveFactor)`
   (`:108408`) find nearby `WaterVolume` colliders via `Physics.OverlapSphere` against the
   `"WaterVolume"` layer mask and take `Mathf.Max` of each volume's `GetWaterSurface`. This one
   method governs: player swim/wet depth (`Character.CalculateLiquidDepth` `:9875`,
   `InLiquidSwimDepth` `:9837`), boat buoyancy (`Floating` `:108156`, `IWaterInteractable`),
   and "am I underwater" (`Floating.IsUnderWater` `:108470`).
   - `GetWaterSurface` body (`:127768`): `surface = transform.position.y + wave + m_surfaceOffset`.
     The knobs that exist per-volume: **`m_surfaceOffset`** (`:127620`, flat height offset),
     **`m_forceDepth`** (`:127618`), and the wave calc (`CalcWave` `:127797`, depth-scaled).
   - 🟢 **This is the surgical hook.** A Harmony **postfix on `WaterVolume.GetWaterSurface`** that
     subtracts a smooth radial falloff for points within R of a Stone of Drought would lower the
     LOGICAL water surface in a curved bowl — exactly the "divot." Because *everything* reads this
     method, the player would actually walk on the carved seabed, boats would ground, etc. That's
     the real, gameplay-true repel — and it's clean vanilla, no Rune Magic.

2. **VISUAL water surface (rendering).** The water you SEE is a `MeshRenderer` (`m_waterSurface`,
   `:127614`) driven by a proprietary **`Custom/*` water shader** reading a `_depth` shader array
   (`s_shaderDepth` `:127630`) and per-vertex wave math on the GPU (`CalcWave`/`CreateWave`
   `:127790-127817`). The mesh is a flat grid; the shader displaces it. **A Harmony patch on the
   C# `GetWaterSurface` does NOT move the rendered mesh** — the GPU doesn't call our postfix.
   So logical-only carving gives you an *invisible* divot: you'd swim-walk in a bowl the water
   still visually covers. **Making the divot VISIBLE is the hard part**, and it's a shader/mesh
   problem, not a logic one.

## 🔴 The hard part, stated plainly (so we don't under-scope it like the cartography map)
Carving the LOGICAL water (gameplay) is **tractable** — one well-placed Harmony postfix + a
registry of active stones. Carving the VISUAL water (what the player sees) is **hard** because
Valheim's water surface is a GPU shader we don't own and can't drive headlessly. This is the SAME
class of trap as the cartography "render like the map" scope-erosion (the styled look was a GPU
shader the build box couldn't drive). State it up front:
- **Logical-only Stone of Drought** = the seabed is walkable in a bowl, but the water *visually*
  still fills it (or z-fights). Reads as broken/unfinished to a player. NOT shippable alone as
  "repels water" — it doesn't LOOK repelled.
- **Visually-correct divot** = needs one of: (a) locally displacing the water mesh vertices,
  (b) feeding a per-instance depth/mask into the water material so the shader carves the dip, or
  (c) a cosmetic "dry bowl" prop + hiding the water mesh in the radius. Each is real Unity
  asset/shader work, and (a)/(b) fight a proprietary shader. **Get a GPU/in-engine verification
  loop in the plan before committing** — the headless build box cannot judge this.

## 🟢 Depth limit — the effect FAILS past a certain depth (Daniel, 2026-06-13) — DECIDED + load-bearing
Daniel: the repel should be powerful enough to walk on dry seabed in shallow water, but it
**starts to fail after a certain depth** — a stone can't drain the deep ocean. This is grounded as
cheap to implement AND is the feature's natural balance + scope control, not just a limitation:

- **Implementation is nearly free.** The postfix already computes `carved = waterSurface −
  wellDepth(distanceFromStone)`. The depth limit is one clamp. Two grounded ways to express it:
  - **Seabed floor clamp:** never carve below the actual ground. `WaterVolume` already holds
    `m_heightmap` (`:127616`), and `Depth(point)` (`:127831`) already bilerps the local normalized
    depth from the volume's corners — so the seabed/local-depth is known to the system. Clamp
    `carved = max(carved, seabedHeight + ε)`. In shallow water the bowl reaches the seabed → you
    stand on dry ground; in deep water the same subtraction is clamped → only a surface dimple,
    can't dig a dry shaft to the ocean floor.
  - **Hard well cap:** subtract at most `D` meters (e.g. ~4–6 m). Past depth `D` the water remains
    below you — the stone "runs out of power."
- **Why it's the RIGHT design, not just a cap:**
  1. **Self-balancing power.** Dries fords / marsh edges / harbor shallows (walkable), but CANNOT
     drain the deep ocean — caps the exploit ceiling (no instant underwater base anywhere, no
     draining the sea around a boss/serpent).
  2. **It SHRINKS the hard visual problem.** A shallow-only dry bowl at a shoreline reads
     convincingly; a 30 m dry cylinder in open ocean would expose every seam in the proprietary
     water shader. The depth limit reduces the Phase-2 visual carve to the tractable size — it is
     the single most important scope-control lever for the expensive part.
  3. **Thematically honest.** "Drought" dries shallows and damp ground; it does not part the sea.
- 🔴 **Open knob — what "fails" looks like at the limit (aesthetic, Daniel's call):**
  - **Soft falloff (lean):** the bowl gets shallower as the water deepens, fading to nothing — no
    hard seam, reads as natural.
  - **Hard cap:** full carve to depth `D`, then a clamped dimple beyond — a legible "this is as deep
    as it goes" line.
  Starbright leans **soft falloff** (fewer visible seams, hides the shader problem better), but it's
  an eyeball call for Daniel.

## Phased feasibility (what it would actually take)
- **Phase 0 — SPIKE (throwaway, 1 session):** Harmony-postfix `WaterVolume.GetWaterSurface` to
  subtract a radial falloff near a hardcoded test point (WITH the seabed-floor clamp from day one —
  it's one line and proves the shallow-vs-deep behavior); join a client and observe. Answers the two
  unknowns nothing else can: (1) does lowering logical surface actually let you stand on seabed in a
  bowl, and (2) what does the UNTOUCHED visual water do over it (fill? z-fight? clip?), AND (3) how
  does the depth-limited bowl read shallow vs deep. This single spike tells us whether the
  visible-divot problem is "annoying" or "showstopper." Do this FIRST.
- **Phase 1 — logical repel + a cosmetic stand-in:** the placeable item/piece + a stone registry +
  the `GetWaterSurface` postfix (radial cosine falloff → curved bowl) + a placeholder visual (e.g.
  hide/scale the local water mesh, or a dry-bowl decal) good enough to read as "repelled."
- **Phase 2 — real visual carve:** proper water-mesh displacement or material-mask integration so
  the dip renders correctly with the vanilla shader, waves, and shoreline. The expensive, GPU-bound
  part — only after Phase 0 proves the approach.

## Grounded vanilla hooks (clean-side — all base-game, ADR-0001 fair to read)
- **Primary hook:** `WaterVolume.GetWaterSurface(Vector3, float)` postfix (`:127768`) — subtract a
  smooth radial well for points within R of any active stone. Affects ALL consumers (swim depth,
  boats, underwater check) → the gameplay-true repel.
- **Aggregators that call it (don't need patching, they inherit the postfix):**
  `Floating.GetWaterLevel` (`:108425`), `GetWaterLevel(...type, waveFactor)` (`:108408`),
  `Floating.IsUnderWater` (`:108470`). Confirm none cache a pre-postfix value across frames.
- **Per-volume knobs (reference, may not suffice for a localized bowl):** `m_surfaceOffset`
  (`:127620`), `m_forceDepth` (`:127618`) — these are whole-volume, so they shift an ENTIRE
  WaterVolume, not a local radius. A localized divot needs the per-POINT postfix, not these.
- **Player depth recompute:** `Character.CalculateLiquidDepth` (`:9875`) /
  `InvalidateCachedLiquidDepth` (`:9887`) — verify the player re-queries often enough that a moving
  into/out-of-divot updates promptly (it caches; may need an invalidation nudge).
- **Visual layer (the hard part):** `m_waterSurface` MeshRenderer (`:127614`), `_depth` shader
  array (`s_shaderDepth` `:127630`), `CalcWave`/`CreateWave` (`:127790`). Proprietary `Custom/*`
  shader — inspect with `vprefab`, but expect we cannot fully drive it.
- 🔴 **ADR-0006:** the Stone item/piece is built additively (`new GameObject` + components), never
  by cloning a water/ZNetView prefab and stripping it.

## 🔴 Open design questions for Daniel (this is an IDEA — none pre-decided)
1. ~~**Scope of the effect — gameplay or cosmetic?**~~ ✅ ANSWERED 2026-06-13 — **GAMEPLAY**: you can
   physically walk on dry seabed in the bowl (logical repel), **with a depth limit** so it fails in
   deep water (see "Depth limit" section above). This makes Phase 2 (visual carve) eventually
   mandatory for ship-quality, but the depth limit shrinks it to a tractable shallow-water problem.
2. **Visible-divot bar.** How polished must the water LOOK? "Convincing dry bowl" (Phase 2, hard) vs
   "good enough placeholder" (Phase 1)? Set this as a hard, eyeball-judged acceptance criterion so a
   convenient logical-only approximation can't silently pass as done.
3. **Radius + shape.** "Curved divot" → radial cosine falloff? How big (a few m, a pond, a harbor)?
   Steep walls or gentle bowl?
4. **Tier / theme / cost.** What unlocks it, what's it made of, where does it fit? ("Drought"
   suggests a dry/desert/ashlands flavor, but it's untethered — Daniel's call.) Does it persist
   (ZDO-anchored, survives relog/restart) like other placed SBPR pieces?
5. **Multiplayer.** Water height is queried client-side per player — does every client need the
   stone registry synced (ZDO) so they all see/feel the same divot? (Almost certainly yes.)
6. **Interactions.** What happens to fish/serpents/boats caught in the bowl? Leviathans? Does it
   drain tar pits (`LiquidVolume`/`m_depths` `:112862` is the SEPARATE savable-liquid system —
   different hook) or only ocean water? Edge cases to decide.
7. **Does it belong in Trailborne at all,** or a separate SBPR mod? It's not a no-map navigation
   feature; like the Forge Master's Trinket, its home is an open question.

## Honest bottom line
- **Logical water repel is very doable** — one Harmony postfix on the right vanilla method
  (`WaterVolume.GetWaterSurface`) + a synced stone registry. No Rune Magic code needed; vanilla
  exposes exactly the hook. A Phase-0 spike proves it in one session.
- **Making the carved water LOOK right is the real cost** — it fights a proprietary GPU water
  shader the headless box can't drive. That's the part to spike early and scope honestly, not the
  logic. Don't greenlight "ship a water-repel stone" without deciding the visual bar (Q2) and
  getting an in-engine GPU verification loop.
- Recommended next step: **a Phase-0 throwaway spike** to see what untouched visual water does over
  a logically-carved bowl. That single observation right-sizes the whole feature. Want me to file
  the spike as a card?

## Routing / status
RESEARCH/IDEA. Clean-side (vanilla water internals only; Rune Magic walled off). No impl card until
Daniel picks the scope (Q1/Q2). If he greenlights, start with the Phase-0 spike (own card,
`engineer-systems`, worktree), not a full build.
