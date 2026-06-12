---
title: "Trailborne v2 (Black Forest) — Cartography requirements"
status: current
purpose: "Locked, build-ready requirements for the v2 cartography system (Surveyor's Table, Local Maps, Cartographer's Kit), distilled from the ratified decisions in docs/design/cartography-v2.md. ALL open items closed 2026-06-10 (architect spec-pass, card t_4be278de): both recipes + the Local Map ItemType are LOCKED. Per-feature buildable impl specs in cartography-impl-spec.md."
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

> **IMPL (2026-06-10, card t_2715661d):** built additively, build 0/0, SpecCheck row added
> — see `cartography-impl-spec.md` §1 IMPL STATUS for the two flagged build-card deviations
> (interactable-vs-Switch; viewer is the downstream card t_7b616020). `[hold]` PR; in-game
> verify pending. The bullets below remain the locked behavioural target.

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
- **Recipe — ✅ LOCKED:** Fine Wood ×10, Bronze ×2, Deer Hide ×4, Bone Fragments ×8.
  Black-Forest tier; Bronze (Copper + Tin → Smelter/Forge) is the correct tier gate.
  Grounded against vanilla's own Cartography Table economy (Finewood ×10 / Bronze ×2 /
  Bone Fragments ×10 / Leather Scraps ×5 / Raspberries ×4) — our recipe sits in the
  same band, themed for a surveyor's post (wood frame, bronze instruments, a hide
  map-surface, bone styli).
- **Bench-in-range to PLACE — ✅ LOCKED: NO.** `Piece.m_craftingStation = null`. The
  Table is placed via the Spade build menu, and **every existing Spade-placed SBPR
  piece sets `m_craftingStation = null`** (Painted Sign `Signs.cs:270`, Path Lamp
  `Trailhead.cs:186`). The earlier "lean: yes, parallels the Spade" conflated two
  things: crafting the Spade *tool* is Explorer's-Bench-gated, but pieces placed *via*
  the Spade menu are not station-gated to place. A survey post is built out in the
  field, away from base — gating its placement on bench-proximity breaks that loop for
  no design payoff. The Cartographer's Kit's 40-pigment cost is already the system's
  hard gate. *(This resolution reverses the proposed lean; flagged for review.)*

## 2. Local Map (two-handed equippable artifact)

> **IMPL (2026-06-10, card t_cb831069, engineer-ui):** built FULL (not MVP) — the item +
> equip/torch patch + binding controller + the forked bounded viewer (`MapViewer`) +
> Table-mode pin removal + the WorldPins-seam consumption. Build 0/0; SpecCheck row 2
> added; isolated dedicated-server boot is clean (SpecCheck "✓ All 22 recipes match",
> PatchCheck "✓ All 15 patch classes registered", 0 SBPR exceptions). See
> `cartography-impl-spec.md` §2 IMPL STATUS for the two flagged design clarifications
> (nomap forces a standalone overlay + the Map-button activate path) and the per-AT table
> in the PR handoff. `[hold]` PR off `integ/v2-cartography`; **logs-green ≠ playable** — the
> in-game pixel render + equip feel are Daniel's F9/Map-key/in-hand checks. The bullets
> below remain the locked behavioural target.
>
> **⚠️ ISSUE-7 CORRECTION (2026-06-11, Daniel playtest):** the "Map-button activate path"
> clarification above is WRONG — it assumed the playtest world is `nomap=ON`. It is
> `nomap=OFF`, where vanilla's M-key full map is alive, so binding our viewer to "Map"
> stacked both maps. **The equipped Local Map now opens on the Use key (E), not "Map"**
> (`cartography-impl-spec.md` §2F). AT-MAP-EQUIP below is amended accordingly.

### Acquisition + binding
- A craftable **item**, **blank when crafted** — carries no map data until **imprinted
  at a Surveyor's Table** (binds to that Table as its 1000 m origin).
- **Recipe — ✅ LOCKED:** Deer Hide ×1 + Fine Wood ×1 (cheap; a blank rolled leather
  on a dowel — you craft many). **Crafted at the Explorer's Bench**, NOT the Surveyor's
  Table. Rationale (reverses the earlier "craft at the Table" lean): a vanilla
  `CraftingStation` *is* an `Interactable` whose `Interact` opens the crafting GUI
  (`CraftingStation.Interact`, decomp :56135 → `InventoryGui.Show`). The Surveyor's
  Table's Use is reserved for opening the forked map viewer (§1) — making the Table
  *also* a crafting station would collide two behaviors on one Use and force a
  suppress-patch. Crafting blank Local Maps at the Explorer's Bench (the existing
  cartography crafting hub — Spade, pigments, Kit all craft there) keeps the Table's
  single responsibility = *imprint + view*. The Table-coupling the design wants is the
  **imprint** step (blank → bound snapshot, below), not the craft. *(Reverses the
  proposed lean; flagged for review.)*
- Imprint = a **snapshot** of the Table's current survey at imprint time (NOT a live
  link — a map "as it was when drawn"). Can be carried, read, and handed to another
  player.

### Equipping + viewing
- **`ItemType` — ✅ LOCKED: `ItemType.TwoHandedWeapon` (= 14)** with attack paths
  neutered, NOT a custom enum value. **This is the decisive build-time call** (closes
  the §13-C list item 4 and the requirements §7 open item). Grounded reasoning, decomp
  `Humanoid.EquipItem` :13798–14011:
  - `EquipItem` is a **closed `if / else-if` chain** keyed on `m_itemType`, ending at
    `Trinket` (:13992) then falling straight to `if (IsItemEquiped(item)) …` (:14001).
    **There is NO default/else branch that assigns a hand slot.** A brand-new custom
    `ItemType` value would match no branch, no `m_rightItem`/`m_leftItem` would be set,
    and the item would **never equip**. So "invent a two-handed-non-weapon type" is not
    viable without *also* Harmony-patching `EquipItem` to add a whole new branch —
    strictly more surface than option B for zero benefit.
  - The **`TwoHandedWeapon` branch (:13921–13932) already does the exact C3 block-clear
    discipline verbatim**: `UnequipItem(m_leftItem)` + `UnequipItem(m_rightItem)` +
    `m_hiddenRightItem = null` + `m_hiddenLeftItem = null`, then `m_rightItem = item`.
    That is precisely the "true-unequip, never-hide" behavior the map needs — for free,
    no patch.
  - `IsTwoHanded()` (:58050) returns true for `TwoHandedWeapon`, so all vanilla
    two-handed gating (no shield, no left-hand item) falls out by construction.
  - **Suppress combat:** leave `m_shared.m_attack.m_attackAnimation` and
    `m_secondaryAttack.m_attackAnimation` **empty** so `HavePrimaryAttack()` /
    `HaveSecondaryAttack()` (:58059/:58064) return false → RMB/LMB do nothing. The map's
    "activate as minimap" action is **not** the attack path; it's a custom equip-side
    hook (see the viewer impl card), so we don't repurpose `m_secondaryAttack`.
  - **One unavoidable patch — the torch exception (C12, ships from gate):** the bare
    `TwoHandedWeapon` branch force-unequips the left hand including a torch. To permit a
    left-hand `Torch`, Harmony-patch `Humanoid.EquipItem` so that when the equipping
    item is our Local Map it runs the TwoHandedWeapon eviction but **allows a `Torch`
    back into `m_leftItem`** (mirror the torch-beside-one-handed special-case at
    :13846–13850 / :13882). Shield/left-weapon are still hard-`UnequipItem`'d (never
    hidden). This is the *only* patch the ItemType decision requires.
- **Equipped as a two-handed item** — occupies BOTH hands; omits weapon and shield.
  Reading the map is a deliberate "put your weapon away" act.
- **🔴 Must truly UNEQUIP the shield/weapon, never HIDE them.** `GetCurrentBlocker()`
  reads `m_leftItem` directly (decomp :13263 → `return m_leftItem`), so a *hidden*
  (`m_hiddenLeftItem`) shield can leave a lingering/ghost block state. The map's equip
  path does a full `UnequipItem` on both hands and nulls
  `m_hiddenRightItem`/`m_hiddenLeftItem` — which is **exactly what the `TwoHandedWeapon`
  branch already does** (:13921–13932), so this is inherited, not hand-rolled.
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

## 3.5 NoMap precondition (the mod disables the global map by default) — ✅ LOCKED (Daniel, 2026-06-11, card t_8c9abf6f)

The entire tier above assumes there is **no vanilla global map** — that's the whole reason
the forked bounded viewer exists. v1's design treated `nomap=ON` as a *server-config
assumption* (see [`../../design/cartography-v2.md`](../../design/cartography-v2.md) §0). That
assumption was **never enforced by the mod**, so on a fresh/local world the full vanilla
global map (M key) is live and the cartography tier competes with a free full-world map. This
section closes that gap.

- **The mod disables the global map BY DEFAULT.** It sets `GlobalKeys.NoMap` server-side at
  world load (`Game.m_noMap` true → the vanilla map UI is forced off). The mod owns its own
  premise — it does not rely on a host having run `nomap` by hand. Daniel: *"Disable the
  global map by default. We will enable it again in the Mistlands tier advancement, but this
  mod should just disable it."*
- **Server-authoritative + auto-propagated.** Setting the key server-side pushes it to every
  joining client automatically (vanilla `SendGlobalKeys`), and it persists with the world. No
  client-side mod action, no per-client config.
- **LIFTABLE, not permanent.** The disable is built behind a gate so a future **Mistlands tier
  advancement** can re-enable the global map (a single `RemoveGlobalKey(GlobalKeys.NoMap)`).
  Build the gate now; the Mistlands *trigger* is future scope. Do NOT hardcode an
  unconditional permanent NoMap.
- **Honesty:** the mod logs a loud, greppable boot line stating it set/holds NoMap — the
  lesson of this bug is that a silent unenforced premise shipped false; never again.
- **Config:** default ON, enforced (per Daniel's directive). One optional config off-switch
  for debug / non-cartography servers; the boot-log fires either way.
- Buildable detail (hook, liftability seam, decomp grounding, ATs):
  [`cartography-impl-spec.md §3.5`](cartography-impl-spec.md). **SpecCheck impact: none**
  (global-key behaviour, not a recipe row).

## 4. Pins

- **Per-pin explicit sharing** (the model in [`../../design/pin-sharing.md`](../../design/pin-sharing.md)):
  each pin has an owner; sharing is opt-in per pin. NOT vanilla's all-or-nothing-per-write.
- **Pin removal** is allowed when viewing a Surveyor's Table (operates on shared data);
  the field Local-Map view is read-only.
- Pins render only within the 1000 m disc.
- **Marker-Sign WorldPins are namable** (ENHANCEMENT, card t_62af5802): the marker panel
  has a textbox; the typed name drives that pin's map label (empty → the type label).
  See [`../../design/marker-signs-worldpin.md §6.1`](../../design/marker-signs-worldpin.md)
  and [`marker-signs-impl-spec.md §7`](marker-signs-impl-spec.md). The name lives in the
  `SBPR_PinName` ZDO field on the marker sign (a wire contract, not a recipe row — no
  SpecCheck impact).

## 5. Scope + build order

**Full scope for the first v2 ship** — all three features at once (Table + Local Maps +
Kit). **Biggest build-risk: the bounded map-UI fork** (own 1000 m-windowed fog array,
fixed-zoom forked viewer, edge-clamp-to-disc, no-pin field view). Decomposition
mitigates this: the UI fork is its OWN early card that a spike validates before the
item/gating cards layer on top.

## 6. Acceptance tests

- **AT-MAP-EQUIP** — equip the Local Map, then press **Use (E)** → it opens the bounded
  full view showing ONLY its 1000 m disc. (Issue-7 correction: the open input is the Use
  key, NOT the "Map" button — binding to "Map" double-stacked vanilla's map under
  `nomap=OFF`. See `cartography-impl-spec.md` §2F / AT-LMAP-OPEN-*.)
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
- **AT-NOMAP-1** — on a world with the mod (fresh/local included), the vanilla global map (M)
  is disabled by default; no host has to run `nomap` by hand. (Full AT-NOMAP-2..6 +
  AT-NOMAP-BOOTLOG in [`cartography-impl-spec.md §3.5.5`](cartography-impl-spec.md).)
- **AT-NOMAP-LIFTABLE** — the disable is gated so a future Mistlands advancement can
  re-enable the global map (`RemoveGlobalKey(GlobalKeys.NoMap)`); verified now by toggling the
  gate condition / config. The Mistlands trigger is future scope; the liftability seam exists now.
- **AT-PIN-SHARE** — per-pin opt-in sharing works; a non-shared pin stays private.
- logs-green ≠ playable: every AT closes only on Daniel's in-game check.

## 7. Open items — ✅ ALL CLOSED (2026-06-10, architect spec-pass, card t_4be278de)

The three items that were open are now LOCKED in-place above:

1. **Surveyor's Table recipe + bench-in-range** → §1. Recipe locked at Fine Wood ×10 /
   Bronze ×2 / Deer Hide ×4 / Bone Fragments ×8. Bench-in-range-to-place = **NO**
   (`m_craftingStation = null`, matching every Spade-placed SBPR piece). *(Bench answer
   reverses the earlier lean — see §1.)*
2. **Local Map recipe + craft location** → §2. Recipe locked at Deer Hide ×1 + Fine
   Wood ×1, **crafted at the Explorer's Bench** (not the Table — avoids a
   `CraftingStation.Interact` ↔ map-viewer Use collision). *(Craft location reverses the
   earlier lean — see §2.)*
3. **Local Map `ItemType`** → §2 "Equipping + viewing". Locked at
   **`ItemType.TwoHandedWeapon` with empty attack anims + the one C12 torch-exception
   `EquipItem` patch** — NOT a custom enum value (a custom value matches no `EquipItem`
   branch and never equips; the `TwoHandedWeapon` branch already gives the exact
   block-clear discipline for free).

Everything else — viewing model, 1000 m bound, fog sizing, edge clamp, per-pin sharing,
full scope, the auto-mapping gate, map+torch, and naming — was already LOCKED.

> **Buildable per-feature implementation specs** (observable acceptance criteria, exact
> vanilla hooks, feature-folder placement, SpecCheck manifest impact) live in
> [`cartography-impl-spec.md`](cartography-impl-spec.md). **SpecCheck manifest delta =
> +3 recipes** — all three cartography recipes are new to `Runtime/SpecCheck.cs` (which
> today holds only the v0.1.0 manifest): +1 build piece (Surveyor's Table), +1 item
> recipe (Local Map), +1 item recipe (Cartographer's Kit). Each impl card adds its own
> entry as it lands, and the manifest's `LOCKED SOURCE` comment gains a cite to
> `docs/v2/planning/requirements.md`. See that doc's §0 for the per-recipe manifest rows.
