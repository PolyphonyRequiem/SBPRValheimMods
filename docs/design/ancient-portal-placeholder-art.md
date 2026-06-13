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
> 🔴 **OPEN — ORIENTATION NOT YET LOCKED (pending Daniel, 2026-06-13).** This doc currently
> assumes the **horizontal / overhead jump-up** ring from `pocket-portal.md`. A web reference
> sweep found that essentially ALL existing "root portal" art is a **vertical** doorway — the
> horizontal jump-up is genuinely unusual (a feature: distinctive; a cost: no off-the-shelf
> reference, and a harder trigger). Daniel is deciding **horizontal jump-up vs. vertical
> root-knot doorway**. The verified-parts inventory below is orientation-AGNOSTIC and holds
> either way; only step 1 (ring rotation) + step 2 (trigger placement) change. If Daniel picks
> vertical: skip the 90° flip, mount the ring vertical at ground level like a normal portal, and
> the trigger is the easy vanilla walk-through volume (drops the novel-geometry risk entirely).

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
nodes (mesh+material only, no donor scripts):

1. **Ring (horizontal):** the `small_portal` mesh+material, **rotated 90° to lie flat** (face up),
   parented at ~**2.2–2.5 m** height (just above player head). This is the glowing disc you jump into.
2. **Teleport trigger:** our `TeleportWorld` + a `BoxCollider trigger` sized like the donor's,
   placed horizontally under/at the ring so a jump-through registers (🔴 the main geometry risk —
   size it to catch a jump but not a walk-underneath; verify on a joined client).
3. **Roots:** 2–4 instances of the `Greydwarf_Root` `default` mesh, scaled/rotated to weave up the
   legs and around the ring rim — sells "roots grown into a portal."
4. **Legs:** 2–3 `stubbe` stumps, scaled thin/tall, as the pillars holding the ring overhead
   (matches the concept render's tripod-of-roots).
5. **Grow fake (placeholder):** the ~15 s "grow" is faked by **lerping the whole piece's scale**
   from ~0.1 → 1.0 over 15 s in the grow-timer `MonoBehaviour` (ZDO-stamp the plant time so it
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
