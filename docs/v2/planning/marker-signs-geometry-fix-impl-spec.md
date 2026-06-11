# Marker Signs — geometry fix (board standoff + crown anchor + post-foot seat)

**Status:** SPEC (ratify → implement). Architect-authored 2026-06-11 from bug card
`t_69f3b4f8` (Daniel playtest, v0.2.19-playtest cartography first live test).
**Assignee for impl:** `engineer-systems` (owns `MarkerSigns.cs` + the `Signs.cs`
kitbash).
**Clean/dirty:** Clean-side. Reading the vanilla `sign` / `wood_pole2` blueprints is
ADR-0001 (base game is fair game) + ADR-0006 (read blueprints, don't clone). No
third-party mod code. No RE handoff needed.
**Manifest/SpecCheck impact:** NONE. This is transform/collider math, not recipe rows.
The recipe-cost change Daniel raised (issue 2) is a **separate** card and is out of
scope here.

---

## 0. Problem (verified from source)

All four marker pieces (`piece_sbpr_marker_poi/_mining/_shelter/_portal`) reproduce
the two geometry defects the Painted Sign already had fixed:

1. **Board inside the post.** `MarkerSigns.cs` places the board mesh child
   (`SBPR_MarkerBoard`) at X/Z = 0 (`GraftBoard`, `:290` keeps `lp.x/lp.z`, which for
   the grafted plank land on the post centreline) and the root interact `BoxCollider`
   at `center = (0, 1.6, 0)` (`:156`). The board therefore renders embedded in the
   post instead of standing off its near side face.

2. **Post foot sunk into the ground.** The root `BoxCollider`
   (`size.y = 0.55`, `center.y = 1.6` → bottom plane at root-local **y ≈ 1.325 m**) is
   the **only** collider on the prefab. Valheim's build placement seat drives the
   lowest enabled non-trigger collider's AABB to the ground, so it drops the whole
   prefab ~1.325 m — burying the post foot (prefab-local y ≈ 0) ~1.3 m underground.
   This is the **exact** root cause of the Painted Sign's #68 bury bug
   (lifted-collider-is-lowest → seat buries the post).

This is a **fix-inheritance gap**, not new design. The marker-sign worker explicitly
deferred seat/standoff as "v0.2+ polish" (`MarkerSigns.cs:151-153`,
`marker-signs-impl-spec.md:114-116`, `PIECES_AND_CRAFTABLES.md:290`). Daniel's
playtest lifts that deferral. The Painted Sign accreted four fixes the markers never
got: #46 crown anchor (`t_05bb5168`), #58 side-face standoff (`t_ac390fff`), #68
post-foot ground collider, #73 180° facing flip + outer-edge z-fight (`t_153ca109`).

The marker signs must end up with the **same clean silhouette** the Painted Sign has
in v0.2.19.

---

## 1. The architectural decision (extract math, not construction)

The card recommended "factor the Painted Sign's geometry helpers into a shared helper
both files call." That is directionally right but must be **refined**, because the two
files do **not** share a construction topology:

| | Painted Sign (`Signs.cs`) | Marker Sign (`MarkerSigns.cs`) |
|---|---|---|
| Root build | `Assets.ClonePrefab("sign")` — root **is** the vanilla sign | `Assets.NewHolderObject` — fresh empty root |
| Board mesh | an unnamed child of the clone (`"New"/"pole"` subtree) | a **named** child `SBPR_MarkerBoard` (`GraftMeshFromBlueprint`) |
| Post | cloned + `StripToDecorative`, planted last | a **named** child `SBPR_MarkerPost` (`GraftMeshFromBlueprint`) |
| Interact collider | a child collider inherited from the vanilla sign | a `BoxCollider` **on the root** (`:154-156`) |
| Crown-lift mechanism | "move **all** root children as a group" (`Signs.cs:495-499`) | move the one named board child + retarget the root collider |

`Signs.cs KitbashStandingPole` is built around *"the board + collider + text canvas are
sibling children of the root, and the pole is parented last, so I can move all root
children as one group."* That assumption is **false for the marker** (board is one named
child; the interact collider is on the root, not a child). A single
`KitbashStandingPole(GameObject root)` shared verbatim would either need leaky
per-topology conditionals (→ the same drift the card warns about) or force the marker
to be rebuilt into the sign's topology (→ large blast radius on a playtested feature,
breaks the named-child contract `WorldPins`/`MarkerSignPanel`/tint code relies on).

**Decision — extract the topology-INDEPENDENT pieces; keep the construction walk
per-file.** The things that actually drift between the two features are the **tuning
constants** and the **subtle foot-seat collider logic** — not the child-move loop. So:

### 1.1 New shared helper: `Runtime/SignGeometry.cs`

A `internal static class SignGeometry` in `namespace SBPR.Trailborne.Runtime` (Features
already depend on Runtime; this is the correct direction). It owns:

**(a) The shared tuning constants** (move out of `Signs.cs`, single source of truth):

```
public const float BoardTopInset             = 0.1f;   // was Signs.cs:66
public const float KissEpsilon               = 0.001f; // was Signs.cs:76
public const float PostFootColliderThickness  = 0.05f;  // was Signs.cs:85
public const float PostFootColliderMinFootprint = 0.1f; // was Signs.cs:91
```

`Signs.cs` references `SignGeometry.BoardTopInset` etc. in place of its local consts
(behavior-identical). `MarkerSigns.cs` references the same constants — so the kiss
gap, crown inset, and foot-pad thickness can **never** diverge between the two
features again. This is the single highest-value anti-drift move.

**(b) Pure scalar math** (formulas only, no GameObject walking — trivially auditable):

```
// Lift to put the board TOP just under the planted pole crown. Floored at 0 so we
// never push the board below where it already sits. (Signs.cs:396-398.)
public static float CrownAnchorLift(float boardTopY, float plantedPoleCrownY)
    => Mathf.Max((plantedPoleCrownY - BoardTopInset) - boardTopY, 0f);

// Lateral distance from the post centre to the board centre so the board's back
// face kisses the post's near side face + a sub-mm anti-z-fight gap. (Signs.cs:481.)
public static float LateralStandoff(float postThickness, float boardThickness)
    => 0.5f * postThickness + 0.5f * boardThickness + KissEpsilon;
```

**(c) The post-foot ground collider** — **move `AddPostFootGroundCollider` verbatim**
from `Signs.cs:621-687` into `SignGeometry`, generalised only by taking the
collider-child name as a parameter (default keeps the sign's `"SBPR_SignPostFoot"`;
marker passes `"SBPR_MarkerPostFoot"`). Body is otherwise **unchanged** — same measured
foot plane, same "piece"-layer non-trigger box, same `PostFootColliderTag` marker
component. This is ~60 lines of subtle two-phase seat logic (the #68 fix) the marker is
missing entirely; sharing it means one correct implementation.

**(d) The placed-only neutralize** — extract `SignTag.NeutralizePostFootColliderIfPlaced`
(`SignTag.cs:62-76`) into:

```
// On a PLACED instance (live ZDO) disable every collider under a PostFootColliderTag
// child so it can't steal the interact/paint raycast. NO-OP on the ghost (no ZDO) so
// the seat still works. Headless-safe.
public static void NeutralizeFootColliderIfPlaced(Component owner, ZNetView? nview);
```

Both `SignTag.Awake` and `MarkerSignTag.Awake` call it.

### 1.2 The marker foot-collider marker component

`Signs.SignPostFootCollider` (a pure marker `MonoBehaviour`) now serves both features.
**Move it to `Runtime/PostFootColliderTag.cs`** (`namespace SBPR.Trailborne.Runtime`)
and rename `SignPostFootCollider` → `PostFootColliderTag` so it is feature-neutral and
the shared helper doesn't create a Runtime→Features.Signs back-dependency. Update the
three references (`Signs.cs:679`, `SignTag.cs:68`, delete the old
`SignPostFootCollider.cs`). This is a mechanical, behavior-preserving rename; the type
carries no logic. *(If review judges even this rename too risky on the playtested sign,
the fallback is to keep `SignPostFootCollider` in `Features.Signs` and let
`SignGeometry` reference it — the dep direction is already breached by `SpecCheck`. The
rename is preferred but is the engineer's call.)*

### 1.3 What stays per-file (the construction walk)

- **`Signs.cs KitbashStandingPole`** keeps its existing measure → group-lift → standoff
  → 180° flip → plant-pole → `AddPostFootGroundCollider` sequence **unchanged in
  behavior**. The only edits: reference `SignGeometry.*` constants and call
  `SignGeometry.AddPostFootGroundCollider(...)` / `SignGeometry.LateralStandoff(...)` /
  `SignGeometry.CrownAnchorLift(...)` instead of the now-removed local copies. **Net
  behavioral change to the sign must be zero** (AT-MARKER-GEO-4).

- **`MarkerSigns.cs`** gets a **new** `SeatMarkerGeometry(...)` step in
  `ConstructMarkerSign`, written for the additive topology (see §2). It uses the shared
  constants + math + foot collider, but does its own (simpler) transform work because it
  has named children.

---

## 2. Marker-side implementation (the additive topology)

In `MarkerSigns.ConstructMarkerSign`, after both `GraftPost` and `GraftBoard` have run
and the `Sign` + text widget exist, replace the naive constant lifts with a measured
seat. Concretely:

**2.1 Delete the magic seat constants.** Remove `BoardLocalY = 1.6f` (`:265`) and
`PostLocalY = 1.0f` (`:268`) as *seat* drivers. `GraftBoard`/`GraftPost` should graft
the meshes at their blueprint-relative local positions (let `GraftMeshFromBlueprint` set
the TRS) and **not** overwrite Y with a guess. The seat is computed below. (A fallback
readable-height constant analogous to `Signs.BoardBottomHeight` may remain **only** for
the missing-pole error path.)

**2.2 Plant the post foot at root y = 0 (pivot-robust).** Measure the grafted post and
offset it so its measured foot lands at root-local 0 — identical to `Signs.cs:388/562`:

```
float poleFootY = Assets.MeasureLocalFootY(post);   // wood_pole2 is centre-pivot → ≈ -1
float poleTopY  = Assets.MeasureLocalTopY(post);
post.transform.localPosition = new Vector3(0f, -poleFootY, 0f);  // foot → y=0
float plantedPoleCrownY = -poleFootY + poleTopY;                  // ≈ 2.0 m for wood_pole2
```

**2.3 Crown-anchor the board.** Measure the grafted board's top in root space and lift
it so its top sits just under the crown:

```
float boardTopY = Assets.MeasureLocalTopY(board);
float lift = SignGeometry.CrownAnchorLift(boardTopY, plantedPoleCrownY);
```

**2.4 Lateral standoff onto the post's near side face.** Measure the **real grafted
board mesh** thickness and the **real post** thickness along the board's thinnest
horizontal axis — do **not** trust the root collider's `0.12` depth literal; measure the
mesh via `Assets.MeasureLocalExtent` exactly as `Signs.cs:420-481` does:

```
// normal axis = board's thinnest horizontal extent (X vs Z), measured on SBPR_MarkerBoard
// dir       = outward = the side the board's readable face points (see §2.6)
float standoff = SignGeometry.LateralStandoff(postThickness, boardThickness);
float targetBoardCenter_n = postCenter_n + dir * standoff;
float lateralDelta_n      = targetBoardCenter_n - boardCenter_n;
```

**2.5 Apply lift + standoff to the board child AND retarget the root collider.** Unlike
the sign (which moves a child group), the marker moves the **one** named board child
(`SBPR_MarkerBoard` — the text canvas is parented under it at `BuildTextWidget`, so it
follows automatically) **and** must move the root interact `BoxCollider.center` to track
the board, or E will raycast empty air where the board used to be:

```
board.transform.localPosition += new Vector3(lateralDelta on normalAxis, lift, 0 on other);
box.center = new Vector3(board-center.x, board-center.y, board-center.z); // track the visible plank
```

**2.6 Facing (the #73 lesson — highest pitfall risk).** The marker grafts the **same**
vanilla sign board mesh and builds its **own** TMP canvas at board-local `(0, 0, -0.07)`
(`MarkerSigns.cs:331`). The Painted Sign needed a 180° flip because the donor plank's
readable normal is the **opposite** of `faceT.forward`. The marker has two clean
options — pick one and verify in-game:
  - (a) Mirror the sign: derive `dir` from the TMP face forward and, if the readable
    normal points **into** the post, rotate the board child 180° about its own vertical
    centroid (`Signs.cs:540-551`). Reuses the proven path.
  - (b) Since the marker controls its own widget placement, choose `dir` deterministically
    so the text face points **away** from the post and place/orient the canvas to match.
    Simpler, but must be confirmed in-game on all four pieces.

**2.7 Foot-seat collider + neutralize.**

```
SignGeometry.AddPostFootGroundCollider(go.transform, post, "SBPR_MarkerPostFoot");
```

…so the placement ghost seats off the post foot (y≈0), not the crown-lifted root
collider. Then in `MarkerSignTag.Awake` add:

```
SignGeometry.NeutralizeFootColliderIfPlaced(this, nview);
```

…so on the **placed** instance the foot collider is disabled and the root interact
collider stays the sole E target. (On the ghost both colliders are enabled and the seat
correctly picks the lower foot collider.)

---

## 3. Open question resolved (card §"same constants or marker-specific tuning?")

**Use the same `BoardTopInset` / `KissEpsilon` / `PostFootCollider*` constants for both
features.** They are geometry-relative (crown-inset, kiss gap, foot-pad thickness), not
board-size-specific, so they transfer directly. The marker board's slightly different
AABB does **not** warrant separate tuning: the standoff/crown math **measures** the real
grafted plank + post at runtime (`Assets.MeasureLocalExtent` / `MeasureLocalFootY` /
`MeasureLocalTopY`), so it self-adapts to the marker's `1.0 × 0.55 × 0.089` plank
without any new constant. The root collider's `0.12` depth is just a padded interact box
— it must **not** be used as the board thickness in the standoff; measure the mesh.

---

## 4. Spec-and-code-together obligations (AGENTS.md rule)

The **engineer's impl PR** (not this spec PR) must, in the same commit:
- Update the in-code deferral admissions: `MarkerSigns.cs:151-153` and the
  `BoardLocalY`/`PostLocalY` comments `:261-268` (the deferral is lifted → describe the
  measured seat).
- Update `docs/datasets/PIECES_AND_CRAFTABLES.md:290` — replace "Board/post seat
  heights are v0.2+ visual polish (silhouette not load-bearing for M1)" with the
  shipped behavior (crown-anchored board + side-face standoff + post-foot seat, shared
  via `SignGeometry`).
- No `SpecCheck.cs` / manifest change (geometry only).

This spec PR updates only the planning docs (this file + the forward-pointer in
`marker-signs-impl-spec.md §1.2`).

---

## 5. Acceptance criteria (named, observable)

Logs-green ≠ playable. AT-1..7 close **only** on Daniel's in-game check; AT-8/9 are
mechanical.

- **AT-MARKER-GEO-1** — On all four marker pieces the board stands **off** the post's
  near side face (board fully visible, not embedded in the post).
- **AT-MARKER-GEO-2** — No z-fighting between the board back face and the post side
  face (no shimmer at the contact plane), at distance and grazing angles.
- **AT-MARKER-GEO-3** — The post foot seats **flush** on flat terrain (not sunk, not
  floating) on all four pieces, matching Painted Sign behavior.
- **AT-MARKER-GEO-4 (regression guard)** — The Painted Sign's own geometry is
  **unchanged** from v0.2.19: same crown anchor, same standoff, same flush foot. The
  sign-side edits are behavior-preserving moves only.
- **AT-MARKER-GEO-5 (ADR-0006 guard)** — Still additive construction: the board/post
  are grafted mesh references (`GraftMeshFromBlueprint`); no `Instantiate`-then-strip of
  the ZNetView-bearing `sign`/`wood_pole2` donors. `AT-PIN-ADR0006` still holds.
- **AT-MARKER-GEO-6 (facing)** — The board's readable face points **away** from the
  post (toward the player at the natural front) on all four pieces — no #73-style
  facing-into-the-post regression.
- **AT-MARKER-GEO-7 (interaction intact)** — Primary **E** on a placed marker still
  opens the `MarkerSignPanel`; **Shift+E** still pins/unpins. The retargeted root
  interact collider hits the relocated board, and the neutralized foot collider does not
  steal the ray.
- **AT-MARKER-GEO-8 (build)** — `dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj
  -c Release` → **0 errors, 0 warnings** (`<TreatWarningsAsErrors>` is ON).
- **AT-MARKER-GEO-9 (DRY guard)** — The seat tuning constants
  (`BoardTopInset`/`KissEpsilon`/`PostFootCollider*`) and the foot-collider +
  neutralize logic exist in **exactly one** place (`Runtime/SignGeometry.cs`); neither
  `Signs.cs` nor `MarkerSigns.cs` carries its own copy. `MarkerSigns.cs` no longer
  defines `BoardLocalY`/`PostLocalY` as seat drivers.

---

## 6. Scope

- **In:** board crown-anchor + side-face standoff + post-foot seat for all four marker
  pieces; extraction of shared constants + math + foot-collider + neutralize into
  `Runtime/SignGeometry.cs`; behavior-preserving refactor of `Signs.cs`/`SignTag.cs`
  onto the shared helper; the doc/comment updates in §4.
- **Out:** marker recipe cost / tier (issue 2 — separate card, touches the manifest);
  pin icon art/coloring; any `WorldPin` behavior change; any change to the marker
  interaction/panel logic beyond retargeting the interact collider.
