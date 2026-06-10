---
title: "Trailborne v2 (Black Forest) — Cartography requirements"
status: current
purpose: "Locked, build-ready requirements for the v2 cartography system (Surveyor's Table, Local Maps, Cartographer's Kit), distilled from the ratified decisions in docs/design/cartography-v2.md. Two recipes remain PROPOSED (flagged inline)."
---

# Trailborne v2 (Black Forest) — Cartography requirements

The headline of the Black Forest tier: a **bounded, earned, player-carried map
system** that sits on top of v1's deliberate map nerf (no M-key map; minimap only,
no north). Three interlocking pieces:

- **Surveyor's Table** — the placed station that *retains* a shared, cumulative,
  locally-bounded survey of its 1000 m neighborhood.
- **Local Map** — the two-handed equippable artifact you imprint at a Table and carry
  to read in the field.
- **Cartographer's Kit** — the worn accessory whose presence enables auto-mapping at
  all. Without it, walking reveals nothing.

> Design rationale + decision history: [`../../design/cartography-v2.md`](../../design/cartography-v2.md).
> Where this doc and the design doc differ, **this doc is the build target**; the
> design doc is the *why*.

---

## 1. Surveyor's Table (placed station)

- A Black-Forest-tier piece, placed via the **Trailblazer's Spade build menu** (never
  the Hammer — design Pillar 1).
- **Built additively (ADR-0006)** — `new GameObject()` + `Piece`/`WearNTear`/`ZNetView`
  + a custom `SBPR_SurveyorTable` MonoBehaviour. Read the vanilla `maptable` only as a
  blueprint (`vprefab inspect maptable`); never instantiate/clone it.
- **Stores a shared, cumulative survey of its own 1000 m disc.** Any surveyor writing
  to the Table merges their exploration + pins that fall **inside 1000 m of THIS
  Table** into its record. Exploration beyond 1000 m of the Table is not stored here.
  Persisted compressed in the Table's ZDO (`ZDOVars.s_data`), like vanilla.
- **Using the Table opens the same forked map viewer as a Local Map** (§2) bound to
  this Table's 1000 m disc — **but operating on the Table's SHARED data and with pin
  REMOVAL enabled** (the field Local-Map view is read-only; the Table view can edit).
- Ward-gated (`PrivateArea.CheckAccess`) like vanilla — a Table in a ward is
  read/write-locked to those with access.
- **Recipe — 🟡 PROPOSED (needs final lock):** Fine Wood ×10, Bronze ×2, Deer Hide ×4,
  Bone Fragments ×8. Open: require an Explorer's Bench in range to place? (lean: yes.)

## 2. Local Map (two-handed equippable artifact)

### Acquisition + binding
- A craftable **item**, **blank when crafted** — carries no map data until **imprinted
  at a Surveyor's Table** (binds to that Table as its 1000 m origin).
- **Recipe — 🟡 PROPOSED (needs final lock):** Deer Hide ×1 + Fine Wood ×1 (cheap;
  crafted at the Surveyor's Table — the cartography hub).
- Imprint = a **snapshot** of the Table's current survey at imprint time (NOT a live
  link — a map "as it was when drawn"). Can be carried, read, and handed to another
  player.

### Equipping + viewing
- **Equipped as a two-handed item** — occupies BOTH hands; omits weapon and shield.
  Reading the map is a deliberate "put your weapon away" act.
- **🔴 Must truly UNEQUIP the shield/weapon, never HIDE them.** `GetCurrentBlocker()`
  reads `m_leftItem` directly, so a *hidden* (`m_hiddenLeftItem`) shield can leave a
  lingering/ghost block state. The map's equip path does a full `UnequipItem` on both
  hands and nulls `m_hiddenRightItem`/`m_hiddenLeftItem` — exactly like vanilla's
  `Tool` / two-handed-weapon branches.
- **Map + Torch ships from the gate** (not a fast-follow). A Harmony patch on
  `Humanoid.EquipItem` lets the map occupy both hands for weapon/shield purposes but
  **permits a left-hand `Torch`** to coexist (mirrors vanilla's torch-beside-one-handed
  logic). The shield/left-weapon is still hard-unequipped (never hidden); only a
  `Torch` is allowed back into the left hand. Result: lit map at night, no block, no
  attack.
- **Minimap binding is durable while the item sits in inventory; gone the instant it
  leaves inventory** (dropped/traded/destroyed → minimap reverts to nothing). The
  **full-screen view requires the map actively EQUIPPED** (two hands), not merely
  carried.
- **The viewer is a FORK of the vanilla map UI** with hard constraints:
  - **No pinning interface** in the field (Local Map view is read-only; pin editing is
    Table-only).
  - **Fixed zoom** on both the minimap circle AND the full view (no scroll-zoom).
  - **Hard 1000 m radius**, centered on the **bound Surveyor's Table** (not the
    player). Everything beyond 1000 m is permanent shroud and never reveals. Pins only
    render within the 1000 m disc.
  - **Player-outside-the-disc → edge indicator clamped to the 1000 m SHROUD RADIUS**
    (NOT the screen edge): project the off-disc player position onto the 1000 m circle
    and show a direction arrow toward the bound Table. (`ClampToScreenEdge` is the
    wrong precedent — this is a map-space clamp to the disc.)

### Fog storage (the over-provisioning fix)
- The Local Map / Table fog is a **small array windowed to the 1000 m disc at the
  player auto-map's NATIVE pixel resolution** — NOT the full vanilla 256² world array,
  and NOT a custom-resolution resample.
- **Why native resolution, not a prettier custom one:** the map imprints FROM the
  player's personal auto-map knowledge (the fog the Kit accumulated). A custom grid
  would force a lossy resample on every imprint. Same grid, windowed — clean copy.
- Stored = the windowed cell range + the bound-origin world coordinate. (Confirm the
  auto-map's real `m_pixelSize` at build — the personal map may differ from the 64 m/px
  world minimap default.)
- This means we do NOT just feed the blob to `Minimap.AddSharedMapData` (which expects
  the 256² world array); the forked viewer renders the windowed array directly.

## 3. Cartographer's Kit (worn accessory)

- An **equippable accessory in the Utility slot** — the SAME slot as Megingjord and
  Wishbone (`ItemType.Utility = 18`; the player's dedicated `m_utilityItem`). Coexists
  with any weapon/shield/map; never fights a hand slot.
- **Its presence enables auto-mapping.** With the Kit worn, walking reveals fog into
  the player's personal auto-map (which is what gets imprinted at a Table). **Without
  the Kit, NO auto-mapping happens at all** — zero passive fog reveal. This is the hard
  gate that makes cartography an earned capability.
- **It is a normal craftable recipe — there is NO "discovery" unlock system.** No
  per-player/per-world discovery flag, no "you found all 4 pigments" machinery. It's
  surfaced the vanilla way (`IsKnownMaterial`); the **recipe cost IS the gate**.
- **Recipe — LOCKED:** 10 Red + 10 White + 10 Blue + 10 Black pigment + 4 Fine Wood,
  crafted at the Explorer's Bench. (The heavy 40-pigment cost guarantees you've engaged
  the pigment system; the Fine Wood mounts the map to be drawn on.)
- **Standing note:** the Kit may eventually fold into "holding a Local Map" and vanish
  as a separate item — but for now it stays distinct. Keep the coupling loose so
  folding it in later is cheap.

## 4. Pins

- **Per-pin explicit sharing** (the model in [`../../design/pin-sharing.md`](../../design/pin-sharing.md)):
  each pin has an owner; sharing is opt-in per pin. NOT vanilla's all-or-nothing-per-write.
- **Pin removal** is allowed when viewing a Surveyor's Table (operates on shared data);
  the field Local-Map view is read-only.
- Pins render only within the 1000 m disc.

## 5. Scope + build order

**Full scope for the first v2 ship** — all three features at once (Table + Local Maps +
Kit). **Biggest build-risk: the bounded map-UI fork** (own 1000 m-windowed fog array,
fixed-zoom forked viewer, edge-clamp-to-disc, no-pin field view). Decomposition
mitigates this: the UI fork is its OWN early card that a spike validates before the
item/gating cards layer on top.

## 6. Acceptance tests

- **AT-MAP-EQUIP** — equip the Local Map + activate → it becomes the active minimap
  showing ONLY its 1000 m disc.
- **AT-MAP-DURABLE** — binding persists while the item sits in inventory; reverts to
  no-map the instant it leaves inventory.
- **AT-MAP-BOUND** — nothing beyond 1000 m of the bound Table ever reveals; pins beyond
  1000 m don't render.
- **AT-MAP-FIXEDZOOM** — neither minimap nor full view zooms; the field full view has
  no pinning interface.
- **AT-MAP-EDGEARROW** — player outside the disc → arrow clamped to the 1000 m circle
  pointing at the bound Table.
- **AT-MAP-STORAGE** — the fog array is windowed to 1000 m at native resolution, not a
  full 256² world array, not a resample.
- **AT-MAP-BLOCKCLEAR** — map equipped → RMB/block does nothing (no ghost shield
  block); unequip → weapon+shield return clean.
- **AT-MAP-TORCH** — map + left-hand torch coexist (lit map at night); still can't
  block or attack.
- **AT-TABLE-SHARED** — multiple surveyors writing to one Table build a combined record
  of its 1000 m disc; pins/fog beyond 1000 m aren't stored.
- **AT-TABLE-PINEDIT** — the Table view permits pin removal on shared data; the field
  Local-Map view does not.
- **AT-KIT-GATE** — with the Kit worn, walking reveals fog; without it, walking reveals
  ZERO fog.
- **AT-KIT-RECIPE** — the Kit crafts from 10×(R/W/B/K) + 4 Fine Wood at the Explorer's
  Bench, surfaced as a normal recipe (no discovery flag).
- **AT-PIN-SHARE** — per-pin opt-in sharing works; a non-shared pin stays private.
- logs-green ≠ playable: every AT closes only on Daniel's in-game check.

## 7. Still open (everything else is locked)

1. **Surveyor's Table recipe** final lock (proposed: 10 Fine Wood / 2 Bronze / 4 Deer
   Hide / 8 Bone Fragments) + bench-in-range-to-place question.
2. **Local Map recipe** final lock (proposed: 1 Deer Hide / 1 Fine Wood) + craft-at-Table
   vs craft-at-Bench.
3. **Local Map `ItemType`** — confirm the literal type at build (likely a custom
   two-handed type + the `EquipItem` patch, since vanilla has no "two-handed
   non-weapon").

Everything else — viewing model, 1000 m bound, fog sizing, edge clamp, per-pin sharing,
full scope, the auto-mapping gate, map+torch, and naming — is LOCKED.
