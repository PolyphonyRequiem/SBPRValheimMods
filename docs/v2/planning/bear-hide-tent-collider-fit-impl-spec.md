---
title: "Bear Hide Tent — collider fit + walk-under shelter (the canopy collider must coincide with the canopy)"
status: current
purpose: "Buildable implementation spec for the Bear Hide Tent collider defect (card t_439f2351, defect 1): the placed tent's collision volume has 'no relationship to the tent mesh' (Daniel, 2026-06-26 in-game playtest). Architect spec-pass GROUNDED firsthand against the as-built code + the shipped AssetBundle mesh AABB + the donor TraderTent prefab. CORRECTS the card's proposed fix in one load-bearing way: the right fix is NOT 'measure a box to the mesh AABB' — it is graft the donor's OPEN-SIDED MeshCollider so the tent becomes a walk-under shelter, because the as-built defect is a wrong collider TYPE (solid box vs open canopy), not merely a mis-sized box. Implementer = engineer-systems."
---

# Bear Hide Tent — collider fit + walk-under shelter

**Status:** SPEC (ratify → implement). Architect-authored 2026-06-26 from bug card
`t_439f2351` (Daniel in-game playtest, #design thread "Black Forest — Bear Hide Tents").
**Assignee for impl:** `engineer-systems` (owns the additive-shell collider/geometry
domain — same profile that fixed the structurally-identical Ancient Portal walk-up
collider, card `t_ea0072ba`).
**Clean/dirty:** Clean-side (ADR-0001). Reading the vanilla `TraderTent` donor's mesh +
`MeshCollider`, and vanilla `Cover` / `Bed.CheckExposure` / `Fireplace.CheckWet`, is all
base-game internals. No third-party mod code. No RE handoff.
**Manifest/SpecCheck impact:** NONE. This is collider geometry on an existing prefab
(`piece_sbpr_bearhide_tent` is already a SpecCheck Piece row). The bedroll + camp-fire
pieces that finish the triad (card `t_439f2351` defects 2,3) are a **separate** spec
(`bear-hide-tent-triad-build-impl-spec.md`) and add the SpecCheck/dataset rows there.
**Base branch:** `main` (the Bear Hide Tent ships on `main` via PR #277 — NOT the
seers-stone branch; confirmed `git ls-tree origin/main`).

---

## 0. Problem (verified firsthand from source + the shipped bundle)

Daniel, 2026-06-26: *"the collision mesh has no relationship to the tent mesh."* This
is **literally true and quantifiable.** Two defects, one root cause (the canopy collider
was never made to coincide with the canopy mesh):

### 0.1 The visual renders ~4.7 m away from the collider (the "no relationship")

The shipped bundle mesh `SBPR_TraderTentMesh` has its pivot at Haldor's **camp scene
origin**, not the tent centroid (the donor is *location decoration*; build script
`scripts/build_bear_hide_tent_bundle.py:7-14`). Measured AABB of the actual shipped mesh
(`UnityPy` read of `assets/bundles/sbpr_tradertent.unity3d`, 2026-06-26):

```
mesh center = (4.492, 2.376, 1.459)   extent = (4.015, 2.432, 3.450)
  footY(min) = -0.055   topY(max) = 4.808   height = 4.863
  Xrange = [0.478, 8.507]   (width 8.029)
  Zrange = [-1.990, 4.909]  (depth 6.900)
```

The visual is attached at `localPosition = Vector3.zero` (`BearHideTent.cs:187-189`) with
**zero measurement**, so the canopy renders centred at root-local **(4.49, 2.38, 1.46)** —
~4.49 m off in X, ~1.46 m off in Z, foot ~0.055 m below ground. The collider box is
authored at `center = (0, 2.45, 0)` (`BearHideTent.cs:139-140`). So the canopy sits
**~4.7 m horizontally away** from the collision box. That is the "no relationship,"
exactly.

### 0.2 The collider is the WRONG TYPE — a solid block, not an open canopy (the deeper defect)

This is the part the card's *proposed fix* ("measure the box to the mesh AABB like the
siblings") gets wrong, and it is the load-bearing architect correction in this spec:

- **The donor TraderTent ships a concave open-sided `MeshCollider`.** `vprefab inspect
  TraderTent`: `collider: MeshCollider {'convex': False, 'mesh': 'default', mesh_size
  (8.029, 4.8634, 6.8998)}`. That collider IS a walk-under canopy — solid where the cloth
  and legs are, **open underneath and on the sides**. It is the correct shelter shape.
- **The as-built threw it away.** `Assets.TryConstructPieceShell` (`Assets.cs:1364-1365`)
  adds a **solid, non-trigger `BoxCollider`** (`isTrigger=false` by default), and
  `BearHideTent.cs:139-140` sizes it `8.0 × 4.9 × 6.9` from `y=0` to `4.9 m`. That is a
  **solid wall of collision from the ground to 4.9 m** filling the tent's whole footprint.
- **Therefore "measure the box to the AABB" does NOT fix the bug** — it relocates a solid
  block to sit on the visible canopy, which:
  - **Fails AT-WALK-UNDER** at any size. You cannot walk *into* a solid box. Daniel said
    *"I am not finding a spot where I can get shelter"* — a measured-but-solid box keeps
    that symptom: he'd collide with the canopy footprint instead of standing under it.
  - **Inverts the §2 design intent.** `trailside-camp.md §2` is built on the canopy being
    **open-sided** (that is *why* `coverPercentage ≈ 0.47`, below the 0.8 sleep gate — the
    whole triad mechanic depends on it). A solid box would block the 8 horizontal cover
    rays and could push cover *up*, breaking the deliberate sub-0.8 design.

> **Correction to the card body — sibling exemplar.** The card cites
> `SurveyorsTable.cs:121-125` as the "measure the mesh AABB and fit the collider" pattern
> the tent skipped. That is **inaccurate**: `SurveyorsTable.cs:125` *also* hand-guesses its
> box (`2.0 × 1.2 × 1.4`, comment "exact fit is visual polish (flagged)") and does **not**
> measure the mesh. The real measure-to-AABB exemplars are **`Signs.cs` (`KitbashStandingPole`)
> and `MarkerSigns.cs` (`SeatMarkerGeometry`)**, which call `Assets.MeasureLocalExtent` /
> `MeasureLocalFootY` / `MeasureLocalTopY` to seat geometry to the real grafted mesh. The
> engineer should copy *that* machinery (for the measured seat in §2.3), NOT the Surveyor's
> Table's hand-guessed box.

---

## 1. The architectural decision (graft the donor MeshCollider; don't re-guess a box)

The fix has two independent halves, both required, mirroring the Ancient Portal walk-up
fix philosophy (`ancient-portal-impl-spec.md §3.2b`: replace a walling box with
access-preserving geometry that is still a valid WearNTear hit target):

1. **Shelter collider = the donor's open-sided `MeshCollider`, seated to coincide with the
   visual.** Graft the TraderTent collision mesh (the same mesh, used as a `MeshCollider`)
   onto the shell as a child, at the **same TRS as the visual** so collider and canopy are
   one object. This makes the tent a genuine walk-under shelter (open underneath) on a
   cover-mask, non-leaky layer (preserves `underRoof`, §2 of the design).

2. **Seat the whole tent so the mesh foot lands at root `y = 0` and the canopy centres on
   X/Z** — measured, not guessed — so it plants flush and the collider/canopy coincide with
   the placement origin.

The current solid root `BoxCollider` is **demoted to a thin ground pad** (not deleted),
exactly as the Ancient Portal fix kept a base pad: it guarantees a non-trigger hit/seat
target even if the MeshCollider graft degrades, without walling the interior.

### 1.1 Why a runtime concave MeshCollider is safe here (grounded)

A concave (`convex=false`) `MeshCollider` is normally static-only in PhysX — fine, because
a placed build piece is static. **The proof it works is the donor itself:** vanilla ships
`TraderTent` with exactly this `MeshCollider{convex:False}` and it functions in-game. We
are reusing the donor's own collision approach, not inventing one. The mesh is already
resident (we load it for the visual), so the collider reuses `mf.sharedMesh` — no second
asset load.

### 1.2 New shared helper (mirror the Signs/MarkerSigns measure pattern)

The seat math is the same `Assets.Measure*` family already proven on Signs/MarkerSigns; no
new measurement primitive is needed. The only new code is a small **`SeatTentGeometry`**
step in `BearHideTent.cs` (additive-topology seat, analogous to
`MarkerSigns.SeatMarkerGeometry`), plus a `MeshCollider` graft. Keep it in
`Features/Camp/BearHideTent.cs` — it is one piece, no cross-feature drift surface to
extract.

---

## 2. Implementation (the buildable steps)

All steps land in `src/SBPR.Trailborne/Features/Camp/BearHideTent.cs`,
`RegisterPrefabs(...)`, replacing the hand-guessed box block at `:137-140` and re-seating
the visual attached at `:185-189`.

### 2.1 Attach the visual, capturing the mesh child (no change to material path)

Keep `TryAttachTentVisual` (`:177-196`) building the `SBPR_BearHideTentVisual` child with
the runtime hide material — but have it **return the child GameObject** (or re-`Find` it)
so the seat step can measure + match it. The mesh is `mf.sharedMesh` =
`SBPR_TraderTentMesh`.

### 2.2 Add the shelter MeshCollider as a child of the piece root (NOT the visual, NOT the shell box)

```
// Open-sided canopy collider: reuse the SAME tent mesh as a static concave MeshCollider,
// so the collision volume IS the canopy shape (walk-under), exactly like the vanilla
// TraderTent donor (which itself ships MeshCollider{convex:false}).
var colObj = new GameObject("SBPR_BearHideTentCollider");
colObj.transform.SetParent(go.transform, worldPositionStays: false);
var mc = colObj.AddComponent<MeshCollider>();
mc.sharedMesh = tentMesh;     // the loaded SBPR_TraderTentMesh
mc.convex     = false;        // open-sided; static piece → concave is legal (donor proves it)
mc.isTrigger  = false;        // a real shelter surface (Cover spherecast needs a solid hit)
// Layer: static_solid (in the vanilla Cover ray-mask, non-leaky) so underRoof stays TRUE
// (design §2). Match the donor: vprefab shows TraderTent collider on layer 15 = static_solid.
int staticSolid = LayerMask.NameToLayer("static_solid");
if (staticSolid >= 0) colObj.layer = staticSolid;
```

> ⚠ **The collider child and the visual child MUST share the same TRS** (§2.3 seats both
> identically) — they use the same mesh, so any pivot offset that moves one moves the
> other. Seat them as a unit.

### 2.3 Seat the tent: measure the mesh, plant the foot at y=0, centre the canopy on X/Z

Measure in **root space** (the same flatten caveat as MarkerSigns — measure the child mesh
in the ROOT frame, not its own, or a self-frame measure round-trips):

```
// Measure the grafted canopy mesh AABB in ROOT space.
Assets.MeasureLocalExtent(visual, go.transform, 0, out float minX, out float maxX);
Assets.MeasureLocalExtent(visual, go.transform, 1, out float minY, out float maxY);
Assets.MeasureLocalExtent(visual, go.transform, 2, out float minZ, out float maxZ);
float centreX = 0.5f * (minX + maxX);   // ≈ 4.49 m for the shipped mesh
float centreZ = 0.5f * (minZ + maxZ);   // ≈ 1.46 m
float footY   = minY;                    // ≈ -0.055 m

// Re-seat the MESH (visual + collider, same delta) so the foot lands at root y=0 and the
// canopy centres over the placement origin on X/Z.
Vector3 seat = new Vector3(-centreX, -footY, -centreZ);
visual.transform.localPosition += seat;
colObj.transform.localPosition  = visual.transform.localPosition;   // collider tracks visual
colObj.transform.localRotation  = visual.transform.localRotation;
colObj.transform.localScale     = visual.transform.localScale;
```

After this, the canopy crown sits at root-local y ≈ 4.86 m, the foot at 0, centred on the
placement point — collider and canopy coincide, plant flush.

### 2.4 Demote the shell box to a thin ground pad (keep a guaranteed hit/seat target)

```
// The shell's solid root BoxCollider was an 8×4.9×6.9 WALL filling the interior (the bug).
// Collapse it to a thin ground pad: a base-mass hit/deconstruct + placement-seat target,
// clearing the interior column from ~0.2 m up so the player can walk under the canopy.
// (Same move as the Ancient Portal walk-up fix: keep a pad, never DestroyImmediate the
//  only structural collider.) Centre it under the SEATED canopy footprint, not at origin.
var box = go.GetComponent<BoxCollider>();
if (box != null)
{
    box.size      = new Vector3(maxX - minX, 0.2f, maxZ - minZ); // footprint × thin
    box.center    = new Vector3(centreX + seat.x, 0.1f, centreZ + seat.z); // = (0,0.1,0) post-seat
    box.isTrigger = false;
}
```

> 🔴 **DESK-ESTIMATED, flagged AT-WALK-UNDER:** the pad height (0.2 m) and whether the leg
> footprint of the *MeshCollider* itself leaves enough ground gap to walk in depend on the
> player capsule + the actual leg geometry. Tune on a joined client; if the player snags on
> the pad, lower it; if the MeshCollider's legs feel too tight, that is real-art-pass scope
> (the placeholder donor's leg spacing is fixed). Daniel verifies in-game.

### 2.5 What does NOT change

- The AssetBundle, the runtime hide material (`BuildHideMaterial`), the recipe, HP,
  Spade-menu wiring, `Piece.PieceCategory.Misc`, `m_craftingStation = null` — all unchanged.
- No `Tag` MonoBehaviour is added (the tent stays visual-only this cut; the sleep mechanic
  is the bedroll's job in the triad spec). No Harmony patch.

---

## 3. Spec-and-code-together obligations (AGENTS.md rule)

The engineer's impl PR (not this spec PR) must, in the same commit:
- Update the in-code SHELTER NOTE (`BearHideTent.cs:40-45`) and the collider comment
  (`:137-140`): the canopy is now an open-sided **MeshCollider** seated to the measured mesh
  (walk-under), with the shell box demoted to a ground pad — not a hand-guessed solid box.
- Update `docs/datasets/PIECES_AND_CRAFTABLES.md` (Bear Hide Tent "Visual notes" / "Status"
  row, ~`:450-452`): replace the implied solid-box collider with the shipped behavior
  (measured-seat open MeshCollider walk-under shelter + ground pad).
- No `SpecCheck.cs` / manifest change (collider geometry only).

This spec PR updates only planning docs (this file + the `index.md` / `README.md` rows).

---

## 4. Acceptance criteria (named, observable)

Logs-green ≠ playable. AT-COLLIDER-1..4 close **only** on Daniel's in-game check; AT-5/6
are mechanical.

- **AT-COLLIDER-FIT** — The collision volume **coincides with the rendered canopy**. Stand
  where the tent is drawn: the collider is there, not 4–5 m to the side. (Defect 0.1 fixed.)
- **AT-WALK-UNDER** — The player can **walk under the canopy and stand inside it** — it is
  an open-sided shelter, not a solid block barring entry. (Defect 0.2 fixed; the core
  symptom "no spot where I can get shelter.")
- **AT-UNDERROOF** — Standing under the canopy in rain: the player **stays dry** (no Wet
  status) and a camp fire placed under it **stays lit** (the design §2 "free" `underRoof`
  benefits land where the player actually stands). Confirms the collider is on a
  cover-mask, non-leaky layer (`static_solid`) and registers as roof overhead.
- **AT-COLLIDER-PLANT** — The tent **plants flush** on flat terrain (foot at ground, not
  sunk, not floating); the placement ghost seats off the ground pad / mesh foot at y≈0.
- **AT-COLLIDER-HIT (regression)** — The placed tent is still a valid **axe-hit /
  deconstruct** target (WearNTear resolves the target by walking the parent chain up from
  any child collider — decomp-grounded, `Projectile.FindHitObject` →
  `GetComponentInParent<IDestructible>()`, same basis as the Ancient Portal per-leg fix).
- **AT-COLLIDER-BUILD** — `dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c
  Release` → **0 errors, 0 warnings** (`<TreatWarningsAsErrors>` is ON).

---

## 5. Scope

- **In:** replace the hand-guessed solid root box with (a) an open-sided **MeshCollider**
  reusing the tent mesh, seated to the **measured** mesh AABB so collider + canopy coincide
  and the tent is a walk-under shelter on `static_solid`; (b) the shell box demoted to a
  thin ground pad; (c) the §3 doc/comment updates.
- **Out:** the missing bedroll + camp-fire pieces (card `t_439f2351` defects 2,3 — owned by
  `bear-hide-tent-triad-build-impl-spec.md`); the gated `Bed.CheckExposure` relax; real
  bear-hide art; the tent's beautification aura (owned by `trailside-beautification.md`);
  the partial-sleep time-acceleration seed (design §8).
