---
title: "Ancient Portal — placeholder art proposal"
status: proposed
purpose: "Placeholder (programmer-art) construction plan for the Ancient Portal piece, grounded on real vanilla assets via vprefab so it's ADR-0006-clean (construct from mesh+material+collider, never clone). The cheap-but-legible silhouette to ship FIRST, before any bespoke mesh/animation work. Companion to pocket-portal.md (the feature design)."
---

# Ancient Portal — placeholder art proposal

> 🟡 **PROPOSED placeholder.** This is the FIRST-PASS programmer-art for the Ancient Portal
> (feature design in `pocket-portal.md`). Goal: a doctrine-clean kitbash that reads as "roots
> grown into a portal" using ONLY verified vanilla parts — NOT the final bespoke art. Every
> asset below was X-rayed with `vprefab inspect` (offline server payload). The target aesthetic
> (what the final/custom art aims at) is the concept render linked at the bottom; the placeholder
> is deliberately cruder.
>
> 🟢 **ORIENTATION CONFIRMED — horizontal jump-up (Daniel, 2026-06-13).** Daniel reviewed the
> concept render (horizontal glowing disc on root-pillars) and confirmed: *"basically what I'm
> hoping for, about 3 meters tall and 3 meters wide."* So the placeholder targets the **horizontal
> overhead ring** (NOT the vertical doorway alternative — that's dropped). **Size: ~3 m tall × ~3 m
> wide** — a compact root-arch (slightly smaller than the vanilla 4×4 portal). The verified-parts
> inventory below is unchanged; the build assembles them at the 3×3 m horizontal-overhead spec.
>
> ⏸️ **PARKED ART-DIRECTION VARIANT (Daniel, 2026-06-17) — see the addendum at the bottom of this
> file.** A later playtest raised a possible pivot: drop the wooden ring entirely, hold the portal
> on **two pillars** with the **energy manifested** between/above them. **Idea-stage — Daniel's
> call, NOT yet locked or building.** Captured under *"Parked art-direction variant"* at the end of
> this doc (issue 8, card `t_d6b08678`).

## Design constraints this has to satisfy (from pocket-portal.md)
1. **Horizontal ring, slightly above player height** — you jump UP into it to activate.
2. **Roots-growing-into-a-portal aesthetic** — organic, Black-Forest-native.
3. **~15 s grow** plant→activate (the placeholder can fake "grow" with a scale-up; see below).
4. **Otherwise a regular portal** — real `TeleportWorld` teleport behavior + tag pairing.
5. 🔴 **ADR-0006 — CONSTRUCT, don't clone.** Build from `new GameObject()` + the specific
   mesh/material/collider parts named below. Do NOT `Instantiate(portal_wood)` and strip it —
   that drags ZNetView/EffectArea/GuidePoint and is the exact cairn-soft-lock bug class.

## Verified vanilla parts (X-rayed via `vprefab inspect`, 2026-06-13)

### The portal ring + teleport surface — from `portal_wood`
`vprefab inspect portal_wood` tree (the parts we additively reuse):
- **`small_portal` child** — mesh `Cube.002` **(4.23 × 3.29 × 1.18 m)**, material `portal_small`
  (textures `portal_small_d/_n/_e`, with an **emission map** → it self-glows). 🟢 **Carries NO
  script** → a FREE additive steal per ADR-0006 (bare `MeshFilter`+`MeshRenderer`). This is the
  glowing portal ring/plane — the recognizable "this is a portal" read.
- **`TELEPORT` child** — a small `BoxCollider trigger` (0.46 × 1.03 × 0.14). This is the teleport
  volume. For the Ancient Portal we **reposition + reorient it horizontal and raised** (the
  jump-up trigger) on our own `TeleportWorld`.
- ❌ **OMIT** (do NOT carry these over — they're why you can't just clone): `PlayerBase`
  `EffectArea` (20 m "rested" sphere), `GuidePoint` (Hugin tutorial trigger — the same component
  that made the Explorer's Bench greet as a Workbench), `portal_destruction`, the LODGroup, the
  particle stack (optional — see below).
- Reference numbers (verified `Portal.md`): vanilla portal durability **400**, size 4×4, NOT
  rain-damaged. The Ancient Portal is MORE fragile (lower durability — value TBD in pocket-portal.md).

### The roots aesthetic — from `Greydwarf_Root` (THE thematic match)
`vprefab inspect Greydwarf_Root`:
- **`default` child** — mesh `default` **(1.75 × 3.87 × 4.07 m)**, material `GDSpawner_mat`,
  **980 tris**, 🟢 **NO script** → free additive steal. A gnarled mossy root-tangle.
- Why this one: the portal recipe is **Ancient seed + Greydwarf eyes** — Greydwarf roots are the
  *literal* "roots growing" asset from the same Black Forest creature family. Theme + tier line up
  with zero stretch. (`GreyDwarfExtraRoots` shares the same mesh/material if we want a second
  variant.)
- The root child also ships a `Cube` 1×1×1 `BoxCollider` we can reuse or ignore.

### The support pillars — from `stubbe` (tree stump) for the raised legs
`vprefab inspect stubbe`: mesh `cylinder1_cylinder1_auv` **(9.8 × 4.4 × 6.95 m)**, material
`cylinder1_..._auvMat`, **no script** → free additive steal. A stump/root-base mesh; scaled down
it gives the twisted legs that hold the ring overhead. (`stubbe_spawner` carries the same mesh
with material `stump` if we prefer that texture.)

## The placeholder construction (cheapest legible build)
Built additively on one `new GameObject("SBPR_AncientPortal")` carrying **only**: `Piece`,
`ZNetView`, `WearNTear`, `TeleportWorld`, our grow-timer `MonoBehaviour`, and these child render
nodes (mesh+material only, no donor scripts). **Target envelope: ~3 m tall × ~3 m wide** (Daniel,
2026-06-13) — scale every borrowed mesh to fit this; the donor meshes are all bigger and need
shrinking (see per-step scale notes).

1. **Ring (horizontal):** the `small_portal` mesh+material, **rotated 90° to lie flat** (face up),
   parented at the **top of the ~3 m height** (just above a ~1.8 m player → a reachable jump apex).
   🔧 **Scale:** the donor mesh is 4.23 × 3.29 m → scale to **~3 m wide** (≈ ×0.71) so the ring
   footprint matches the 3 m spec. This is the glowing disc you jump into.
2. **Teleport trigger:** our `TeleportWorld` + a `BoxCollider trigger`, placed horizontally at the
   ring (~3 m up) so a jump-through registers (🔴 the main geometry risk — size it to catch a jump
   apex but not a walk-underneath; with the ring at ~3 m and a ~1.8 m player +~1 m jump ≈ ~2.8–3 m
   reach, give the trigger vertical slack; verify on a joined client).
3. **Roots:** 2–4 instances of the `Greydwarf_Root` `default` mesh (donor 1.75 × 3.87 × 4.07 m →
   scale down to read as ~3 m-scale tendrils), scaled/rotated to weave up the legs and around the
   ring rim — sells "roots grown into a portal."
4. **Legs:** 2–3 `stubbe` stumps (donor is large, 9.8 m wide → scale down hard, thin/tall) as the
   pillars holding the ring at ~3 m overhead (matches the concept render's tripod-of-roots).
5. **Grow fake (placeholder):** the ~15 s "grow" is faked by **lerping the whole piece's scale**
   from ~0.1 → 1.0 (final = the 3×3 m envelope) over 15 s in the grow-timer `MonoBehaviour`
   (ZDO-stamp the plant time so it
   survives relog mid-grow), then enabling the `TeleportWorld`. No bespoke animation needed for
   placeholder — a real roots-assembling animation is a later art pass.
6. **Glow (free):** the `portal_small` material's **emission map** already self-glows — no light
   needed. OPTIONAL: carry ONE particle child (`blue flames`) for the portal shimmer, but it's
   skippable for placeholder (keep it cheap).

## What's deliberately deferred to "real art" (NOT placeholder)
- A bespoke **roots-weaving-into-a-ring** mesh + a real grow animation (vs the scale-lerp fake).
- Hanging rune-charms / runestone accents (the concept-render flourishes).
- Custom material blending the portal glow INTO the root mesh (placeholder just sits the disc in
  the root frame).

## Open / to verify at build time
- 🔴 Horizontal trigger reliability (jump-up activates, walk-under doesn't) — client playtest.
- Ring height: 2.2 m vs 2.5 m — tune so a jump reaches it but it's clearly overhead. Playtest.
- Whether the `small_portal` emission reads well lying flat (it's authored to face the player
  vertically) — if it looks wrong horizontal, fall back to a simple emissive-disc placeholder.
- Scale of `Greydwarf_Root`/`stubbe` meshes to portal ring — the root mesh (3.87 m tall) and stump
  (9.8 m wide) need scaling DOWN substantially; confirm proportions in-engine.

## Target aesthetic (concept render — NOT the placeholder)
The north-star look (final/custom art aims here): horizontal glowing disc on twisted root-pillars,
overhead, mossy Black-Forest roots, rune accents. Render:
`~/.hermes/profiles/starbright-engineering/cache/images/openai_gpt-image-2-medium_20260613_163653_b6076b32.png`

## Routing
Placeholder build → `engineer-systems` (additive prefab construction + grow-timer + horizontal
trigger) once the pocket-portal.md feature is promoted to an impl card. This doc is the art brief
for that card.

---

## Parked art-direction variant — "2 pillars + manifested energy" (issue 8, PARKED)

> ⏸️ **STATUS: PARKED IDEA — Daniel's call, do NOT build.** Daniel raised this during a v0.2.26-dev
> in-game playtest (2026-06-17). It is an *art-direction musing*, NOT a locked decision. Daniel's own
> words flag the uncertainty: *"I don't know that we even need…"*. This section CAPTURES the idea so
> it's not lost; it does not greenlight it. The current build (wooden `small_portal` ring on 3 `stubbe`
> legs) ships unchanged until Daniel says otherwise. Card: `t_d6b08678` (architect spec-pass).

### What Daniel said (verbatim + attributed)
> "I don't know that we even need the wooden portal geometry, just two pillar art pieces would help to
> show it being held up, and the energy just manifests."
> — Daniel, 2026-06-17, in-game playtest (v0.2.26-dev)

### The idea, restated
Drop the **wooden ring** (`small_portal` mesh, this doc's §1 / `Portals.cs:226-232`) as the load-bearing
"this is a portal" read. Instead:
- **Two pillar art pieces** (vs the current 2–3 `stubbe` legs) carry the silhouette — they visually
  *hold up* the portal.
- **The energy manifests** between/above the pillars as the portal surface — a glow/effect volume
  rather than a wooden frame around a hole.

This is a smaller delta than it first sounds: the placeholder already specs "**2–3 stubbe pillars**"
(§ construction step 4) and the impl already builds legs as a tunable `LegCount`. "Two pillars" is a
config nudge, not new geometry. The real change is **removing the ring as the portal-read** and
**promoting the energy effect from accent to the main visual**.

### What this couples to in the CURRENT code (grounded — so Daniel can weigh the cost)
The wooden ring is not just decoration; three live wirings hang off it today. If the ring is removed,
each must be re-homed onto the manifested-energy volume:

| Current ring duty | Where (grounded) | If ring is removed → |
|---|---|---|
| **`TeleportWorld.m_model`** (the emission-glow renderer the Update() lerp drives; NRE if null) | `Portals.cs:283-285` — `m_model = ring's MeshRenderer`, falls back to any grafted renderer | the energy volume's renderer becomes `m_model`. The existing fallback already covers "ring absent," so this degrades safely, but the energy volume should be a *real* `MeshRenderer`/emissive so the glow lerp has a surface. |
| **Proximity / "target-found" effect anchor** | `WireProximityEffect`, `Portals.cs:480-495` — grafted `_target_found_red`, positioned at ring height under the piece root | the effect's anchor + plane key off the energy volume, not "the ring." |
| **Issue-1 effect ORIENTATION fix** (align effect to the flat ring) | sibling cards `t_bf2bb402` (done) + `t_06b7b13c` (ready, engineer-systems) — sets the graft `localRotation` to the ring's flat-lay `Euler(90,0,0)` | ⚠️ **"align to the ring" becomes "align to the energy volume."** If the energy volume is also a flat overhead plane, the `Euler(90,0,0)` fix still holds and is NOT wasted. If the energy volume is oriented differently (e.g. a vertical column between pillars), the orientation target changes and this redesign **subsumes** the issue-1 fix. Do the cheap rotation fix now regardless (ring exists today); flag that a ring-removal re-homes the anchor. (per `starbright-engineering` coupling note on `t_bf2bb402`.) |

### Open questions for Daniel (answer these and it becomes a buildable art-spec)
1. **Remove the ring fully, or keep a faint one?** "I don't know that we even need…" reads as *lean toward
   removing*, but confirm — a thin emissive rim sometimes sells "portal" better than bare energy.
2. **Pillar art source?** Vanilla stone donor (e.g. `stubbe` retex, a standing-stone mesh), or does this
   wait for the bespoke art pass and bespoke pillar meshes? Placeholder-now vs art-pass-later changes who
   builds it.
3. **What IS the "manifested energy" visually?** Options, cheapest→richest: (a) the existing
   `_target_found_red` glow promoted to always-on between the pillars; (b) a vanilla portal/effect
   particle volume; (c) bespoke VFX (art-pass tier). This also decides what `m_model` points at.
4. **Energy volume orientation** — flat overhead plane (keeps the jump-up-into-it activation + the
   issue-1 `Euler(90,0,0)` fix) or a vertical curtain between the pillars (changes activation feel AND
   re-opens the effect-orientation target)? This is the one that determines whether issue-1 is subsumed.
5. **Does the activation TRIGGER move?** See the note below — likely **no**.

### What does NOT depend on this decision (de-coupling note for the architect/engineer)
The **activation trigger height** (issue 8 Part A — raise the overhead jump-through volume "a wee bit",
`BuildOverheadTrigger`, `Portals.cs:409-425`) is **art-independent**. The player jumps into an overhead
activation volume at ~`EnvelopeHeight` whether that volume is framed by a wooden ring or by manifested
energy between two pillars. The trigger keys off the **portal's activation plane height**, which both
art directions share (~3 m overhead). So the Part A tuning does **not** have to wait for this Part B
decision — it can ship standalone against the current build, and survives a future ring→pillars pivot
unchanged. (The card's "fold A into B if greenlit" caution applies only if Daniel also *moves the
activation height* as part of the redesign; a pure ring→energy swap at the same height leaves the
trigger tune valid.)

### Routing
If Daniel greenlights → this supersedes §1 (the ring step) of this doc and becomes an **architect
art-spec** for the Ancient Portal (2-pillar + energy-volume construction, with the three re-homings
above), then an `engineer-systems` impl card. Until then: **parked**, ring-build ships as-is.
