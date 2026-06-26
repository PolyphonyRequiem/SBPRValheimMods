---
title: "Twisted Portal — distinct silhouette (colorblind-safe, deferred→required)"
status: proposed
purpose: "Architect art-brief + spec-delta for the v3 Swamp Twisted Portal's DISTINCT mesh. Promotes the deferred art-pass detail (twisted-portal-impl-spec §4.1, AT-GEOMETRY) to REQUIRED after Daniel's 2026-06-26 Niflheim playtest: the Twisted Portal currently ships the Ancient Portal's EXACT envelope re-tinted, and the tint differentiation rides entirely on a green hue — the one axis Daniel (colorblind) cannot read — so for him the two portals are fully identical. This doc grounds a distinct, colorblind-SAFE (shape/silhouette/form, NOT hue) silhouette from vprefab-verified vanilla parts (ADR-0006 additive), refines the acceptance tests, and carries the exact impl-spec §4.1 replacement text to apply alongside the code. Companion to ancient-portal-placeholder-art.md (the Ancient Portal precedent this supersedes for the Twisted prefab). Card: t_4ab58b42."
---

# Twisted Portal — distinct silhouette (colorblind-safe)

> 🔴 **PROPOSED — Daniel's art-direction pick gates the build.** This is the architect brief
> for card **t_4ab58b42** (BUG, deferred→required). It does NOT itself change `TwistedPortal.cs`
> — it grounds the distinct silhouette, refines the acceptance tests, and hands the exact
> impl-spec §4.1 replacement to an `engineer-systems` impl child card. The art direction below is
> the **architect lean**; §6 lists the alternatives Daniel can redirect to. **Until Daniel picks,
> no code moves** (the pick determines the impl card's content).

## 1. The bug, restated (verbatim + the load-bearing constraint)

> "we are using the same mesh as the ancient portal."
> — Daniel, 2026-06-26, Niflheim playtest (Swamp v3, Twisted Portal)

The Twisted Portal (`TwistedPortal.cs:113-116`) grafts from the **exact same donor blueprints**,
at the **exact same scales/counts/envelope**, as the Ancient Portal (`Portals.cs:130-132`,
`:233-248`). The ONLY differentiation is `ApplySwampTint` (`:256-268`): a per-renderer
`MaterialPropertyBlock` multiplying `_Color`/`_EmissionColor` by `SwampTint = (0.45, 0.62, 0.40)`.

🔴 **Colorblindness caveat (load-bearing).** Daniel is colorblind. The Twisted/Ancient
differentiation currently rides **entirely on a green-ish hue tint** — the exact perceptual axis
he cannot distinguish — so for him the two portals are not "same shape, different color," they are
**fully identical**. **A recolor does not fix this report.** The fix MUST differentiate by
**SHAPE / SILHOUETTE / FORM**, on axes legible in monochrome.

This is **deferred→required**, NOT drift: the engineer followed the spec, which deferred the
distinct mesh to the art pass (`twisted-portal-impl-spec.md §4.1`: *"v3.0 ships the Ancient
envelope with a swamp tint"*). The playtest promotes it. Spec §4.1 updates **in the same PR as the
code** (AGENTS.md spec+code rule; §7 carries the exact replacement text).

## 2. The architectural freedom this report unlocks (the key insight)

The Ancient Portal's wooden ring is **load-bearing geometry**: `TeleportWorld.m_model` points at
the ring's `MeshRenderer` (`Portals.cs:290-292`) and NREs 60×/s if it's null; the proximity /
"target-found" effect anchors to it (`WireProximityEffect`, `:465-499`). You cannot freely
restructure the Ancient Portal's ring without re-homing those wirings (that's the whole subject of
the PARKED "2-pillar" variant in `ancient-portal-placeholder-art.md`).

**The Twisted Portal carries NO `TeleportWorld`.** `SBPR_TwistedPortal` is both the teleporter and
the ZDO-discipline owner (`twisted-portal-impl-spec §4.1`, `TwistedPortal.cs:228`). The grafted
ring is created, positioned, tinted (`:164-170`) and then **never referenced again** — it drives
nothing. The overhead jump-through trigger is an independent child at `EnvelopeHeight`
(`BuildOverheadTrigger`, `:333-347`), wholly decoupled from the visual.

**Consequence: the Twisted Portal's entire visual is free to restructure.** We can drop or replace
the ring, change leg count/form, add masses — with **zero** functional re-homing, because no
component reads the visual. This is a far cheaper, lower-risk change than the same edit would be on
the Ancient Portal. It also means the silhouette break can be large.

> 🟢 **Out of scope (untouched):** the overhead jump-up activation (trigger height/size,
> `EnvelopeHeight = 3 m`), the rune-name pairing / travel model (sibling card **t_3d908685**,
> design-gated), and the floating-label overlay (sibling **t_f739451f**). The activation plane
> stays a ~horizontal overhead volume at ~3 m — so whatever we build keeps "jump up into it to
> travel." We change what the player SEES, not how they travel.

## 3. Colorblind-safe differentiation axes

The current diff uses exactly one axis, and it's the wrong one:

| Axis | Colorblind-safe? | Ancient Portal | Twisted today | Twisted (this design) |
|---|---|---|---|---|
| **Hue / tint** | ❌ NO (Daniel can't read it) | forest (untinted) | swamp green MPB | retained as *secondary* only |
| **Leg count** | ✅ yes | 3 | 3 (identical) | **5** (denser cluster) |
| **Leg form** | ✅ yes | straight `stubbe` cylinder | identical | **tapered spike** (`Stalagmite`) |
| **Symmetry** | ✅ yes | even 120° tripod | identical | **uneven heights, inward lean** |
| **Crown / added mass** | ✅ yes | flat wooden hoop | identical | **bulbous `GuckSack` mass** (self-emissive) |
| **Footprint radius** | ✅ yes | 1.2 m | identical | **0.9 m** (tighter) |

The fix must move **multiple shape axes at once** so the break is unmistakable even if any single
axis reads subtly in-engine — and so it survives Daniel's by-eye, colorblind validation.

## 4. Recommended design — "Guck-crowned twisted spire" (architect lean)

A jagged cluster of inward-leaning organic **spikes** crowned by a swollen, self-glowing **guck
mass** — reads at a glance as a swamp-grown, twisted thing, NOT the Ancient Portal's tidy
root-tripod-and-hoop. Every part is vprefab-verified vanilla geometry, grafted additively
(ADR-0006). All transforms below are **DESK-ESTIMATED, flagged AT-GEOMETRY** — the engineer tunes
on a joined client and Daniel eyeballs the accept.

### 4.1 Verified vanilla parts (X-rayed via `vprefab inspect`, 2026-06-26)

- **Spikes (legs) — `Stalagmite`** → child `default`, mesh `default` **(1.36 × 4.82 × 1.33 m,
  400 tris)**, material `Stalagtite_mat`. 🟢 **Zero scripts, zero colliders** → the cleanest
  possible free additive steal (bare `MeshFilter`+`MeshRenderer`). A tapered organic spike — the
  opposite silhouette of a straight cylindrical post. Graft hint: `"default"`.
  > ⚠️ Honest note: `Stalagmite` is a cave/mountain asset, not swamp-native. It is chosen for its
  > **tapered-spike silhouette**, not its biome. If Daniel wants strict swamp sourcing, §6 lists
  > swamp-native spike/leg alternatives (`HugeRoot1`, `SwampTree1`); the spike *form* is the point.
- **Crown (the "portal surface" mass) — `GuckSack`** → child `sack`, mesh `Cancer`
  **(2.41 × 3.92 × 2.80 m, 162 tris)**, material `gucksack` with an **`_EmissionMap` (`gucksack_e`)
  → it self-glows by SHAPE**, not by a flat tint (colorblind-safe glow). 🟢 The `GraftMeshFromBlueprint`
  helper attaches ONLY the `sack` mesh + material; the donor's separate `Point light` and
  `Particle System` children are NOT pulled, so no `Light`/`ParticleSystem`/`ZNetView` rides along.
  Graft hint: `"sack"`. (Guck is THE swamp-signature material — the recipe even uses GreydwarfEye +
  Sunstone; a guck bulb is on-theme with zero stretch.)
- **Roots (keep) — `Greydwarf_Root`** → child `default` (the existing tendrils, `:310`). Keep 2,
  or drop — minor. Listed so the engineer knows the existing graft is unaffected.

### 4.2 The build (additive — what changes vs the current code)

Replace the visual kitbash in `TwistedPortal.RegisterPortalPiece` (`:151-178`) and `BuildLegs`
(`:274-301`). The piece shell, `WearNTear`, `Piece`, `SBPR_TwistedPortal`, the overhead trigger,
and the root collider are **all unchanged** (functional layer untouched).

| Element | Ancient / Twisted-today | Twisted (this design) — desk-estimated, AT-GEOMETRY |
|---|---|---|
| **Donor (legs)** | `stubbe` cylinder | **`Stalagmite` `default`** (tapered spike) |
| **Leg count** (`LegCount`) | 3 | **5** |
| **Leg radius** (`LegRadius`) | 1.2 m | **0.9 m** (tighter cluster) |
| **Leg heights** | even (scale `0.8` Y) | **uneven** — alternate ~`0.75` / ~`0.62` Y so tips sit ~3.6 m / ~3.0 m |
| **Leg lean** | `Quaternion.identity` (vertical) | **lean inward** ~8–12° toward center (localRotation tilt) |
| **Overhead frame** | `small_portal` flat wooden hoop @ 3 m, scale 0.71 | **drop the hoop**; graft a **`GuckSack` `sack` mass** @ ~3 m, scale to ~1.6–1.8 m wide, sitting where the spikes converge |
| **Glow source** | hoop emission + green MPB | **guck's own `_EmissionMap`** (shape-bound) + retained MPB tint as *secondary* |
| **Envelope height** | 3 m | **3 m (UNCHANGED — jump-up activation height is out of scope)** |
| **Leg colliders** | 3 solid posts | **5 solid posts** at the new positions (axe-hittable; same `LegColliderSize` pattern) |

🔴 **Regression guard (t_f3310406 painted-sign lesson):** the swamp tint stays a **per-renderer
`MaterialPropertyBlock`**, never `sharedMaterial.SetColor`. Re-tinting the guck mass via
`ApplySwampTint` (unchanged) bleeds into NO other guck sack in the world. This is a named AT below.

🔴 **Why this is colorblind-safe:** the four moved axes (leg count 3→5, form cylinder→spike,
symmetry even→uneven-leaning, crown hoop→bulbous-mass) are all legible in **pure monochrome**. A
screenshot desaturated to greyscale still reads "spiky guck-crowned cluster" vs "tidy hoop
tripod." The hue tint is demoted to a secondary nicety, not the differentiator.

### 4.3 Sketch (silhouette intent, not to scale)

```
   ANCIENT (today + Twisted today)        TWISTED (this design)
        ___________                            (~) <- guck mass, self-glowing
       (  ring  )   flat hoop @3m            /  |  \   crown the convergence
        ‾‾|‾‾‾|‾‾                            /\  |  /\
          |   |     3 even straight        /  \ | /  \   5 uneven tapered spikes,
          |   |     posts, r=1.2m         |    \|/    |  leaning inward, r=0.9m
       ___|___|___                        |____/|\____|
        tidy tripod                        jagged spike-cluster
```

## 5. Refined acceptance tests

Supersedes the card's draft list:

- **AT-DISTINCT-SHAPE** — The Twisted Portal is distinguishable from the Ancient Portal by
  **shape/silhouette in monochrome** (verify on a desaturated screenshot, not just in color), at a
  glance, side-by-side. *Color contributes nothing to passing this test.* ← the load-bearing one.
- **AT-ADDITIVE** — Construction stays ADR-0006 additive: `new GameObject` + `AddComponent` +
  `GraftMeshFromBlueprint` mesh-reference grafts. **NO `Instantiate` of a ZNetView-bearing prefab**
  (no clone of `Stalagmite`/`GuckSack`/`portal_wood`). The grafted children carry only
  `MeshFilter`+`MeshRenderer`.
- **AT-NO-RING-WIRING-BREAK** — Dropping the `small_portal` hoop breaks nothing, because
  (verify in code) nothing reads the ring renderer on the Twisted Portal — there is no
  `TeleportWorld.m_model`. Confirm `SBPR_TwistedPortal` + the overhead trigger still resolve and
  teleport works after the visual swap.
- **AT-ACTIVATION-UNCHANGED** — The overhead jump-up trigger still activates at ~3 m (height/size
  untouched, out of scope). You still travel by jumping up into the portal.
- **AT-TINT-MPB** (regression, t_f3310406) — the swamp tint stays a per-renderer
  `MaterialPropertyBlock`, never `sharedMaterial` — no bleed into world guck sacks / portals.
- **AT-SPEC-SAME-PR** — `twisted-portal-impl-spec.md §4.1` is updated in the **same PR as the
  code** (no longer "ships the Ancient envelope"); §7 below is the verbatim replacement.
- **AT-PLAYABLE** — *"Logs green ≠ playable."* Server registration succeeding does NOT pass this
  card. **Daniel's in-game eyeball on a joined client is the accept.**
- **AT-BUILD-CLEAN** — `dotnet build -c Release` → 0 errors, **0 warnings** (`TreatWarningsAsErrors`).

## 6. Alternatives (Daniel redirects here) — the card's three options, grounded

The card asked: (a) re-kitbash from different vanilla blueprints, (b) author a new mesh, or
(c) restructure the existing kitbash. Mapped to grounded choices:

- **(a) Re-kitbash — ARCHITECT LEAN (§4).** Cheapest, ships now, ADR-0006-clean. Swap donors +
  counts. Donor menu for the **spike/leg** if Daniel dislikes `Stalagmite`'s cave origin:
  - `HugeRoot1` → child `root1`, mesh `root1` (gnarled root, scale down hard from 29 m) — chunky,
    organic, swamp-adjacent.
  - `SwampTree1` → child `swamptree1` (a gnarled swamp-tree trunk) — strictly swamp-native.
  And for the **crown**: `GuckSack_small` (same `Cancer` mesh) if the full sack reads too big, or
  keep a faint thin `small_portal` rim *under* the guck if "this is a portal" needs reinforcing.
- **(b) Author a new mesh.** A bespoke "twisted woven ring" / guck-portal mesh. Higher fidelity,
  but it's an art-pipeline task (Blender → asset bundle), not an additive kitbash — a separate,
  larger card and a later milestone. Not recommended for *this* bug (the report wants a distinct
  shape **now**, by-eye; a kitbash delivers that this PR).
- **(c) Restructure the existing kitbash (minimal).** Keep `stubbe`+`small_portal` donors but
  change **leg count → 4 or 2, radius, and uneven heights** only. Lowest-effort, but the silhouette
  break is weaker (still cylinders + a hoop) — riskier against a strict colorblind by-eye accept.
  Falls back to this if the donor swaps misbehave in-engine.

> 🟡 **Coupling flag for option (a)/(c) "two pillars":** the PARKED Ancient-Portal art variant
> (`ancient-portal-placeholder-art.md`, issue 8, `t_d6b08678`) also musters a **2-pillar** look.
> If *both* the Ancient (parked) and Twisted go 2-pillar, they'd re-converge. The §4 lean
> (5 uneven spikes + guck crown) deliberately diverges from any 2-pillar direction, keeping the two
> portals distinct regardless of how the parked Ancient variant lands.

## 7. Impl-spec §4.1 replacement (apply VERBATIM with the code, same PR — AT-SPEC-SAME-PR)

Replace the second bullet of `docs/v3/planning/twisted-portal-impl-spec.md` §4.1 ("Visual
kitbash", `:282-287`) — the one that currently ends *"v3.0 ships the Ancient envelope with a swamp
tint so it's visually distinguishable at a glance"* — with:

> - **Visual kitbash — a DISTINCT silhouette (colorblind-safe; deferred→required, card t_4ab58b42).**
>   The Twisted Portal must read as its own object by **shape**, not hue: Daniel is colorblind, so a
>   tint-only diff from the Ancient Portal is invisible to him. Per
>   [`twisted-portal-distinct-mesh.md`](../../design/twisted-portal-distinct-mesh.md), build a
>   distinct additive kitbash (ADR-0006, all mesh-reference grafts, ZNetView-free): a cluster of
>   **5 uneven inward-leaning tapered spikes** (`Stalagmite` `default` mesh) on a tighter ~0.9 m
>   radius, crowned by a **self-emissive `GuckSack` (`Cancer`) mass** in place of the
>   `small_portal` wooden hoop. Because the Twisted Portal carries **no `TeleportWorld`**, the ring
>   is purely cosmetic and free to drop — nothing reads its renderer. The swamp `MaterialPropertyBlock`
>   tint is retained as a *secondary* cue only (never `sharedMaterial`). Exact transforms are
>   desk-estimated and flagged **AT-GEOMETRY** for in-game tuning; the **AT-DISTINCT-SHAPE** accept
>   is Daniel's by-eye, **monochrome-legible** side-by-side check, not logs-green.

(If Daniel picks a §6 alternative, the engineer adjusts the donor/count nouns in this block to
match the chosen direction before applying — the *requirement* "distinct by shape, colorblind-safe,
additive" is invariant; the specific donors are the dial.)

## 7b. Concept render (the AT-DISTINCT-SHAPE proof — NOT the final art)

A side-by-side concept render, rendered deliberately in **near-greyscale** to demonstrate the
accept criterion directly: the two portals are distinguishable by **silhouette alone**, with zero
reliance on color. Left = the Ancient Portal (tidy symmetric tripod + flat overhead hoop); right =
the §4 lean (jagged inward-leaning spike cluster crowned by a self-glowing bulbous guck mass). In
pure monochrome the contrast is unmistakable — that *is* what AT-DISTINCT-SHAPE requires.

> 🟡 This is a **silhouette-intent concept**, not the kitbash output. The actual build is the
> additive vanilla-parts kitbash in §4 (`Stalagmite` spikes + `GuckSack` crown), which will read
> cruder than this render — the render shows the *target silhouette*, the kitbash approximates it
> from real parts (the `ancient-portal-placeholder-art.md` concept-vs-placeholder distinction).

Render (not committed into the MIT repo — binary, the Ancient Portal precedent references its
concept render by path too):
`~/.hermes/profiles/architect/cache/images/openai_gpt-image-2-medium_20260626_121922_67438146.png`

## 8. Routing

- **This doc** → architect deliverable; lands as a **docs-only PR** Daniel gates (the
  `ancient-portal-placeholder-art.md` precedent).
- **After Daniel picks the art direction** → create an `engineer-systems` impl child card
  (parent t_4ab58b42) to apply the `TwistedPortal.cs` kitbash swap **+ the §7 §4.1 spec edit in the
  same PR**, build clean, and hand to Daniel for the in-game AT-DISTINCT-SHAPE / AT-PLAYABLE accept.
  Not created yet — the pick determines the card's donor/count specifics.
- **Sibling cards (separate, not this one):** rune-name pairing/travel + dedicated-server bug
  (t_3d908685, design-gated, BLOCKED on Daniel); floating-label overlay (t_f739451f).

## Open questions for Daniel (answer these → it's a buildable impl card)

1. **Art direction:** take the §4 lean (5 spikes + guck crown), or redirect to a §6 alternative?
   Specifically — is the cave-origin `Stalagmite` spike OK, or do you want a strictly swamp-native
   leg (`HugeRoot1` gnarled root / `SwampTree1` trunk)?
2. **Keep a faint ring?** Drop the wooden hoop entirely (the lean), or keep a thin emissive rim
   under the guck for a stronger "this is a portal" read?
3. **Guck mass size:** one big `GuckSack` crown, or a ring of smaller `GuckSack_small` blobs around
   the convergence? (Both grounded; the latter is a richer "festering" read.)
