---
title: "Portal Seed → Ancient Portal (Black Forest, v2)"
status: proposed
purpose: "Design doc for the Portal Seed: a single 25 kg field-craftable item that, when planted with a Hammer (no workbench), grows over ~15 s into an Ancient Portal — a horizontal, slightly-overhead root-grown portal. Otherwise a regular vanilla portal (keeps the ore/metal teleport ban). Rethemed + fully specced by Daniel 2026-06-13 from the earlier 'Pocket Portal' idea placeholder."
---

# Portal Seed → Ancient Portal (Black Forest, v2)

> 🟡 **PROPOSED — mechanics locked by Daniel 2026-06-13, not yet architect-specced.** This
> supersedes the earlier thin "Pocket Portal (v2 IDEA)" placeholder. The concept is now fully
> formed (item, recipe, grow mechanic, geometry, break behavior, aesthetic). Next step is an
> architect buildable spec, then an impl card. The vanilla-anchor line numbers below come from
> `nomap.md` §6 and need re-confirming against `assembly_valheim.dll` metadata at spec time.

## What Daniel said (verbatim + attributed)
> "I think I want to retheme a bit to 'Portal Seed' as an item used to make an 'Ancient portal'.
> When planted, it takes 15 seconds to grow into the portal properly and activate. It's a bit
> more fragile than a regular portal, but ultimately is a single 25 kg inventory item that
> doesn't require a workbench to build. The portal itself is horizontal and above the player's
> height slightly, requiring them to jump up to activate it. The aesthetic is roots growing into
> a portal shape. Uses an ancient seed, 20 greydwarf eyes, and two surtling cores to make the
> portal seed. It is otherwise a regular portal. Just note that it weighs less and only requires
> the single item to craft in the field. It can be placed with a regular hammer, no table
> required. When broken it collapses back into a seed ready to be replanted."
> — Daniel, 2026-06-13

## The concept in one line
A **portable, field-deployable portal** you carry as a single seed and plant anywhere — trading
a regular portal's sturdiness + bench requirement for **convenience**: one light item, no
workbench, Hammer-placed, and it grows itself. Mechanically it is **otherwise a vanilla portal**
(same tag-pairing, same teleport rules, **same ore/metal ban**). The fantasy is *convenience*,
explicitly NOT an ore-ban breaker.

## 🟢 Tier — Black Forest (v2). DECIDED + grounded.
Daniel: pocket/portal-seed is Black Forest tier. Every ingredient confirms it:
- **Ancient seed** — drops from **Greydwarf brute** + **Greydwarf nest** (Black Forest). Verified
  `~/valheim/sbpr-corpus/wiki/fandom/Ancient_seed.md`. Canonically the seed used to **summon The
  Elder** (the Black Forest tree-boss) — so "an ancient seed that grows an ancient structure" is
  *reusing the game's own fiction* (ancient seeds grow ancient things), not an arbitrary retheme.
- **Greydwarf eye** — Black Forest drop.
- **Surtling core** — Burial Chambers / Surtlings (Black-Forest-adjacent; same gate as the vanilla
  portal's cores).
- 🔴 **Doc reconciliation:** `PARKED-2026-06-03.md` mis-filed the Pocket Portal under "v3 (Swamps)".
  `MILESTONES.md:13` and `:38` already place pocket portals in **Black Forest tier (v0.2.0+)**, and
  `pocket-portal` was always a v2 idea note. This doc settles it: **Black Forest / v2.** The PARKED
  note is historical/frozen (do not edit it); this doc is the current truth where they disagree.

## 🟢 Recipe — DECIDED (Daniel 2026-06-13)
The **Portal Seed** item is crafted from:
| Ingredient | Qty | vs vanilla portal |
| --- | --- | --- |
| Ancient seed | 1 | NEW catalyst (vanilla portal uses none) |
| Greydwarf eye | 20 | vanilla portal uses 10 → **doubled** |
| Surtling core | 2 | same as vanilla portal |
| ~~Finewood~~ | 0 | vanilla portal uses 20 → **dropped** (no lumber; it grows itself) |

- Vanilla portal recipe for reference (verified `~/valheim/sbpr-corpus/wiki/fandom/Portal.md`):
  **20 Finewood + 10 Greydwarf eye + 2 Surtling core**, crafted at a Workbench.
- The swap reads thematically: drop the carpentry (Finewood), double the "eye"/magic component,
  keep the cores, add the ancient seed as the growth catalyst. "Less building, more growing."
- 🔴 **OPEN — where is the SEED itself crafted?** Daniel specified the seed needs no bench *to
  plant in the field*, but didn't say where you craft the seed. Options: (a) craft the seed at a
  Workbench/Forge back home (you pre-make seeds, then carry them out — most likely), or (b) the
  seed is itself field-craftable from raw mats. Lean (a). Confirm with Daniel.

## 🟢 Item properties — DECIDED
- **Weight: 25 kg** as a carried inventory item. (Heavy enough to be a real pack commitment — you
  carry ONE, not a stack of ten — but it's a single slot vs. hauling 20 Finewood + 10 eyes + 2
  cores as separate stacks.) 🔴 OPEN sub-question: **stack size** — does the Seed stack at all, or
  is 25 kg each meant to keep you to one or two? (Earlier idea floated stack 5; at 25 kg that's
  125 kg, probably too heavy to be the point. Lean: low/no stack. Confirm.)
- **Field-craftable / Hammer-placed, NO workbench:** the Portal Seed is placed with a **regular
  Hammer** (registered in the Hammer's PieceTable, OR placed via a use-action on the item — see
  impl options), and **requires no crafting station in range** to place — unlike the vanilla
  portal, which needs a Workbench nearby to build (but not to operate). This is the headline
  convenience.

## 🟢 Plant → grow → activate — DECIDED
- On placement the seed is **not yet a working portal**. It takes **~15 seconds to "grow"** into
  the Ancient Portal, then activates (becomes a live, tag-pairing portal).
- During the 15 s grow window it is inert (cannot teleport, presumably shows a growth animation —
  roots climbing into the portal ring shape).
- After grow completes it behaves as a **normal vanilla portal** in every functional way.

## 🟢 Geometry — DECIDED (the distinctive bit)
- The Ancient Portal ring is **horizontal** (lying flat, facing up) and sits **slightly above the
  player's height**, so the player must **jump up** into it to activate/teleport. This is a
  deliberate, signature interaction — you leap into an overhead root-ring rather than walking
  through a vertical doorway.
- 🔴 Architect/impl: confirm the activation trigger still fires on a jump-through (the vanilla
  portal teleport is a proximity/trigger-volume on `TeleportWorld`; a horizontal overhead trigger
  volume needs the collider positioned + sized so a jump registers reliably but you don't trigger
  it by walking underneath). This is the main novel-geometry risk.

## 🟢 Fragility + break-to-seed — DECIDED
- **More fragile than a regular portal.** Vanilla portal durability is 400 (verified Portal.md).
  The Ancient Portal has **less** — exact value TBD (a deliberate downside balancing the
  convenience). 🔴 OPEN knob: how much less (e.g. ~50%? ~150 HP?). Also: does it take rain/weather
  decay? (Vanilla portal does NOT — "Damaged by Rain? No." A root structure arguably should, but
  that's a balance call — confirm.)
- **Break behavior: collapses back into a Portal Seed.** When destroyed, instead of dropping
  rubble/refund mats, it **drops a single replantable Portal Seed** — ready to replant elsewhere.
  This makes it genuinely *portable*: break it, pick up the seed, replant somewhere new. 🔴 Impl
  note: this is a custom `OnDestroyed`/drop behavior (vanilla portals drop their mats on destroy).
  Decide: does breaking ALWAYS return exactly one seed (even if killed by a creature/decay), or
  only on player deconstruct? Lean: always returns the seed (that's the portability fantasy).

## 🟢 Teleport rules — DECIDED: "otherwise a regular portal"
- **Keeps the ore/metal teleport ban.** Daniel: "It is otherwise a regular portal." So copper,
  tin, iron, etc. still CANNOT pass — this is the convenience portal, NOT vanilla's endgame Portal
  stone. This resolves the earlier open design fork (convenience vs ore-ban-breaker vs recall):
  **it's the convenience portal.**
- Tag-pairing identical to vanilla (`ZDOVars.s_tag`, 10-char case-sensitive tag, paired-in-order).
- Same 8 s teleport, same invulnerability-during-teleport, same everything functional.

## Aesthetic — DECIDED
- **Roots growing into a portal shape.** The visual is organic: roots/vines climbing and weaving
  into the portal ring as it grows (the 15 s animation is roots assembling the ring), settling into
  a horizontal overhead root-portal. Custom mesh + grow animation — genuine asset work.
- Reinforces the "ancient seed grows an ancient structure" fiction; visually distinct from both the
  vanilla stone-and-wood portal and the future Twisted Portal.

## Grounded vanilla hooks (clean-side — ADR-0001, vanilla internals fair to read)
- Vanilla `TeleportWorld` (decomp ~line 122902, 233 lines per `nomap.md` §6) is the blueprint to
  READ for the teleport/tag/pairing behavior. Connection logic is tag-matching via `ZDOVars.s_tag`.
- 🔴 **ADR-0006 (load-bearing) — build ADDITIVELY, do NOT clone.** `nomap.md` §6 says "clone
  portal_wood" — that predates ADR-0006 and is now FORBIDDEN. Build the Ancient Portal prefab from
  `new GameObject()` + `AddComponent` (TeleportWorld + the components you intend), reading
  `portal_wood` only as a blueprint (`vprefab inspect portal_wood` — fires no Awake). Reading ≠
  cloning. Subtractive cloning caused every major prefab bug to date (cairn-bonfire, ZDO-orphan).
- **Grow timer:** a custom `MonoBehaviour` on the placed piece that flips inert→active after ~15 s
  (InvokeRepeating/coroutine or a ZDO-stamped plant-time so it survives relog mid-grow). Decide
  whether grow progress persists across save/reload (lean: yes, ZDO-stamp the plant time).
- **Break→seed:** custom drop on `WearNTear.OnDestroyed` returning the Portal Seed ItemDrop.
- **Hammer placement, no station:** register the piece with `m_craftingStation = null` and no
  station requirement so it places bench-free; put it on the Hammer's PieceTable (or the Spade's —
  see design-pillars.md note below).

## 🔴 Spec grounding / cross-doc
- `requirements.md:548` — Pocket Portal listed NOT in v1. Net-new v2 design (not drift). When this
  ships, the recipe count + `SpecCheck.cs` manifest move with the code (AGENTS.md spec+code rule).
- `datasets/PIECES_AND_CRAFTABLES.md:317` lists "Pocket Portal / Twisted Portal (Trailborne v3+)"
  — UPDATE this line when speccing: Portal Seed is v2 Black Forest, retheme the name.
- `design-pillars.md:33` — "pocket portals (when they ship) — all live on the Spade." 🔴 CONFLICT
  with Daniel's 2026-06-13 "placed with a regular hammer." Daniel's latest word wins (Hammer, not
  Spade-only) — but flag it: is it Hammer-ONLY, or both? The pillar doc says trail tools live on
  the Spade; a field-portal arguably fits the Spade thematically. Confirm Hammer vs Spade vs both,
  and update design-pillars.md to match.
- `nomap.md` §6 + `:212` and `index.md:15` reference the old "Pocket Portal" name/idea — update the
  cross-references to "Portal Seed → Ancient Portal" when this is specced.

## Remaining OPEN knobs for Daniel (small — the core is decided)
1. **Seed craft location** — Workbench/Forge at home (lean) vs field-craftable from raw mats?
2. **Stack size** — low/no stack at 25 kg each (lean) vs stackable?
3. **Fragility value** — how much less than 400 HP? And does it take weather/rain decay (vanilla
   portal doesn't)?
4. **Break→seed scope** — always returns the seed (lean), or only on intentional deconstruct?
5. **Hammer vs Spade vs both** for placement (reconcile with design-pillars.md).

## Status / next step
PROPOSED, mechanics locked. → Architect buildable spec (named acceptance tests: plant→15s-grow→
activate, horizontal-overhead jump-to-activate, ore-ban intact, break→single-seed, 25 kg / no-bench
/ Hammer-place, fragility). → Impl card (engineer-systems; novel-geometry trigger is the main risk).
Resolve the 5 small open knobs with Daniel either at spec time or now.
