---
title: "Cartography (v2 / Black Forest) — Map Station, Local Maps, Cartographer's Kit"
status: living
purpose: "DRAFT design spec for the v2 Black Forest cartography system. Grounded in the vanilla MapTable/Minimap surface; recipes and several mechanics are PROPOSED and gated on Daniel's open-question answers at the end. Not yet locked — promote sections to requirements.md as they ratify."
---

# Cartography (v2 / Black Forest) — Map Station, Local Maps, Cartographer's Kit

> 🚧 **DRAFT — pending Daniel's design calls.** This brick specs the three
> interlocking v2 cartography features from `PARKED-2026-06-03.md` §"v2 (Black
> Forest)". Mechanics that are GROUNDED in the vanilla decomp are marked
> ✅GROUNDED; design choices I made a recommendation on are 🟡PROPOSED; true forks
> I need Daniel to call are 🔴OPEN and collected in the final section. Nothing here
> is locked until Daniel ratifies it and it moves into a `requirements.md` section.

## 0. Where this sits

- **Tier:** Black Forest (v2). The tier *after* the current Meadows loop (v0.1.0 /
  the 0.2.x polish line) is locked. Not started; zero map code exists in `src/`
  today (greenfield).
- **The v1 map baseline this builds on (locked, `requirements.md` Q1.2 / A1.2):**
  - `nomap=ON` → no global map at all (only the minimap circle survives, see below).
  - `nomap=OFF` (default) → **minimap circle ONLY**, freely rotating, **no north
    indicator, no M-key full map**.
  - The vanilla **Cartography Table is nerfed**: existing ones lose function, new
    ones can't be built.
  - **⚠️ PREMISE CORRECTION (2026-06-11, card t_8c9abf6f):** the `nomap=ON` line above was
    written as a *server-config assumption* — and **nothing in the mod enforced it.** On a
    fresh/local world `GlobalKeys.NoMap` is NOT set unless a host runs `nomap` by hand, so the
    full vanilla global map was silently live and the whole "no map → earn bounded local maps"
    premise shipped false. **This is now FIXED: the mod sets `GlobalKeys.NoMap` server-side by
    DEFAULT** (a liftable gate for the future Mistlands re-enable). NoMap is no longer an
    assumed precondition — it is a mod-owned, enforced one. See
    [`../v2/planning/requirements.md §3.5`](../v2/planning/requirements.md) and
    [`../v2/planning/cartography-impl-spec.md §3.5`](../v2/planning/cartography-impl-spec.md).
- **Design pillars this must honor** (`design/design-pillars.md`): Pillar 1 — trail
  tools are peers, placed by the Spade, not the Hammer. Pillar 2 — **color
  semantics are emergent**; never hard-code "blue = water." Cartography UI/tooltips
  must not assign meanings to pigment colors.
- **The headline tension (read before designing):** v1 deliberately removed the
  full-screen map (and as of card t_8c9abf6f the mod *enforces* that removal by default — it
  no longer depends on a server config). A cartography system that "reveals the map" has
  nowhere vanilla to draw it. **How a Local Map is *viewed* is the central open question** (§6,
  Q-CART-1) — every other mechanic hangs off that answer.

## 1. The system in one paragraph

The **Map Station** is a placed Black-Forest-tier station that *retains* explored
terrain + pins in its own world-anchored memory (its ZDO). A **Local Map** is a
craftable **item** that is **blank leather until imprinted at a Station** — bind it
to a Station and it carries a snapshot of that Station's recorded knowledge, which
you can then carry, read, and hand to another player. The **Cartographer's Kit** is
the explorer's tool — gated behind discovering all four basic pigments — that
*creates and imprints* Local Maps (and is the only way to write fresh exploration
*into* a Station). Station = the well; Local Map = the bucket; Cartographer's Kit =
the rope. None works alone — that's the "paired, mandatory" lock.

## 2. Vanilla mechanism this is built on (✅GROUNDED — decomp-verified)

All three features sit on **one small, clean vanilla surface** — the same one
`shudnal/NomapPrinter` and `nbusseneau/BetterCartographyTable` patch (studied as
reference only; no code copied — ADR-0001):

- **`Minimap` singleton** owns fog-of-war as two `bool[]` arrays —
  `m_explored` (you) and `m_exploredOthers` (synced from others/tables) — sized
  `m_textureSize²` (default 2048²), plus a `Texture2D m_fogTexture`. Walking
  reveals via `m_exploreRadius = 100f`.
- **`Minimap.GetSharedMapData(byte[] oldMapData)`** (decomp line 48754) →
  serializes a **version-3 `ZPackage`**: the merged explored-bool array, then every
  `m_save` pin that isn't a Death pin (ownerID, name, world pos, PinType, checked,
  author). Merges any prior `oldMapData` in. **This is the exact "record fog + pins
  to a transferable blob" primitive the Station needs.**
- **`Minimap.AddSharedMapData(byte[])`** (line 48823) → the inverse: reads the
  blob, OR-merges explored pixels via `ExploreOthers(j,i)` into `m_exploredOthers`,
  calls `m_fogTexture.Apply()`, and merges pins with a **1 m dedup**
  (`HavePinInRange(pos,1f)`). Pins owned by others that aren't re-shared get
  tombstoned/removed — the vanilla "all-or-nothing per write" behavior our
  `pin-sharing.md` flags as the thing to improve on.
- **`MapTable`** (line 114014, a 128-line `MonoBehaviour`) is the reference piece:
  - Carries a `ZNetView`; stores its blob **compressed** in the ZDO byte array
    `ZDOVars.s_data` (`Utils.Compress`/`Decompress`).
  - **Write switch** → `GetMapData()` (= `GetSharedMapData` + merge with the ZDO's
    current blob) → store in ZDO + `InvokeRPC("MapData", …)` so the owner persists
    it. Emits `$msg_mapsaved`.
  - **Read switch** → `AddSharedMapData(decompressed ZDO blob)` into your Minimap.
    Emits `$msg_mapsynced` / `$msg_alreadysynced` / `$msg_mapnodata`.
  - **Ward-gated**: every op checks `PrivateArea.CheckAccess`.

**Engineering takeaway:** we do **not** need to invent fog serialization, pin
transfer, or persistence — vanilla already does all of it through public methods.
The Map Station is a re-skinned, re-gated, semantically-enriched re-implementation
of `MapTable`'s read/write loop. The Local Map item is a *second* container for the
same blob format, just stored on an item instead of a placed ZDO.

## 3. Feature — **Map Station** (the substrate)

### 3.1 What it is
A Black-Forest-tier placed station: a standing stone-and-timber survey post that
**holds map knowledge in the world**. It is the only thing that *retains* fog/pins
durably and the only place Local Maps are imprinted. Placed via the **Trailblazer's
Spade build menu** (Pillar 1 — never the Hammer), consistent with how signs, lamps,
and cairns already route through the Spade table.

### 3.2 Construction (✅GROUNDED doctrine — ADR-0006)
**Build additively — do NOT clone the vanilla `maptable` prefab.** `MapTable`
carries a `ZNetView`, and cloning ZNetView-bearing prefabs is exactly the
subtractive anti-pattern ADR-0006 retired (it caused the v0.2.7 ZDO-orphan
soft-lock). Instead:
- `new GameObject("SBPR_MapStation")` + `AddComponent` of only: `Piece`,
  `WearNTear`, `ZNetView`, two `Switch`es (read/write — or one Use + a radial), and
  a custom **`SBPR_MapStation : MonoBehaviour`** that reimplements the read/write
  logic against the **public** `Minimap.GetSharedMapData` / `AddSharedMapData`.
- Read the vanilla `maptable` only as a **blueprint** (`vprefab inspect maptable`)
  for mesh/material references; reference-copy, never instantiate.
- Persist the blob in `ZDOVars.s_data` exactly as vanilla does (compressed), so the
  format stays interoperable and we inherit vanilla's save/load for free.

### 3.3 Behavior (🟡PROPOSED, gated on Q-CART-1/2/4)
- **Write-to-Station** (requires Cartographer's Kit in inventory — see §5): merges
  your current Minimap exploration + your shareable pins into the Station's ZDO
  blob. The Station's knowledge is **cumulative** — every survey adds to it.
- **Read-from-Station**: merges the Station's recorded fog/pins into your Minimap
  (which, under v1's nerf, surfaces on the **minimap circle** — see Q-CART-1).
- **Imprint-a-Local-Map** (§4): copies the Station's current blob onto a blank
  Local Map item held by the user.
- **Ward-gated** like vanilla (`PrivateArea.CheckAccess`) — a Station inside a
  ward is read/write-locked to those with access. 🟡 Recommend keeping this; it's
  the natural "your basecamp survey post is yours" behavior.

### 3.4 Recipe (🟡PROPOSED — needs Daniel's lock, Q-CART-5)
Black-Forest-tier materials, evoking "a real surveyor's post":
- Fine Wood ×10 (frame), Bronze ×2 (instruments/fittings), Deer Hide ×4 (the
  map-skin surface), Bone Fragments ×8 (markers/stylus rack). Crafted/placed via the
  Spade menu; **may require an Explorer's Bench in range to place** (parallels how
  the Spade itself is bench-gated) — Q-CART-5.

### 3.5 Name (🔴OPEN — Q-CART-7; Daniel asked for a better one)
Working title "Map Station" is a placeholder. Candidates with meaning in §7.

## 4. Feature — **Local Maps** (the portable token)

### 4.1 What it is
A craftable **item** (`ItemDrop`), not a piece. **Blank leather when crafted** — it
carries no map data until imprinted at a Map Station (the parked lock: "Local Map
without Station = blank leather"). Once imprinted, it holds a **snapshot** of that
Station's fog+pins blob and can be carried, read in the field, and **handed to
another player** (the social-cartography payoff — you give a friend a map instead of
spamming a shared table).

### 4.2 Data storage (🟡PROPOSED — implementation surface to verify)
- Store the version-3 blob on the **item instance**, not a placed ZDO. Recent
  Valheim `ItemDrop.ItemData` carries `m_customData` (a `Dictionary<string,string>`)
  that persists per-instance and survives trade/drop — **verify this field exists
  and round-trips on our game version before relying on it** (don't assume; it's the
  load-bearing storage question). Fallback if unavailable: a crafted-item +
  ZDO-backed "map case" piece.
- Blob is the **same format** as the Station/vanilla `GetSharedMapData` output, so
  reading a Local Map = `Minimap.AddSharedMapData(blob)`. One format everywhere.

### 4.3 Behaviors (🔴 mostly OPEN — hang on Q-CART-1)
- **Imprint** (at a Station, via Cartographer's Kit): blank → snapshot. 🟡 Recommend
  a snapshot (frozen at imprint time), NOT a live link — a Local Map is "the map as
  it was when drawn," which is more diegetic and avoids live-sync complexity.
- **Read in the field** (the core UX fork): under v1's no-M-key-map nerf, *what does
  "reading" show?* Options in Q-CART-1.
- **Stacking / weight:** 🟡 non-stackable (each is a unique document), weight ~1.0,
  so carrying many maps is a real inventory cost.
- **Snapshot staleness:** a Local Map imprinted early shows only what the Station
  knew then. 🟡 Recommend this as a *feature* (maps age), not a bug to fix.

## 5. Feature — **Cartographer's Kit** (the gated tool)

### 5.1 What it is
The explorer's instrument that *makes cartography possible*: it is the tool you use
to **write exploration into a Station** and to **imprint Local Maps**. Without it, a
Station is an inert stone and Local Maps can't be created. **Gated on discovering
all four basic pigments** (red, white, blue, black) — the parked lock. This makes
cartography a *reward for having actually explored & experimented*, not a
turn-on-day-one convenience.

### 5.2 The pigment-discovery gate (🟡PROPOSED mechanism — Q-CART-3)
- "Discovered a pigment" = the player has crafted/obtained each of the 4 basic
  pigments at least once. Track via a per-player flag set when each pigment is first
  produced (a `PlayerProfile`/known-recipe-style boolean, or a custom known-set).
  **Recommend** gating the *recipe unlock* of the Cartographer's Kit on all 4 flags
  (the Kit appears as craftable once you've made R+W+B+K). Exact persistence surface
  → Q-CART-3.
- 🔴 Honor **Pillar 2**: the gate is "you found all four pigments," never "blue
  unlocks water-mapping." Colors carry no built-in meaning.

### 5.3 What it is, physically (🔴OPEN — Q-CART-6)
Is the Cartographer's Kit:
- (a) an **equippable tool item** (like the Spade) you wield to Use a Station, or
- (b) a **consumable/held inventory item** that merely needs to be *present* to
  enable Station writes + imprints, or
- (c) an **upgrade to the Spade** (a new mode/tab on the existing tool)?
Recommend **(b)** — simplest, no new tool-wielding UX, "you carry your kit." But
this is a real UX call → Q-CART-6.

### 5.4 Recipe (🟡PROPOSED — Q-CART-5)
Gated-by-pigments, so materials should feel "fieldcraft": Fine Wood ×2, Leather
Scraps ×3, Bronze ×1 (drawing instruments), + **1 of each basic pigment** (R/W/B/K)
to literally consume the four inks into the kit — reinforcing the gate diegetically.

## 6. How a map is *viewed* — the central fork (🔴OPEN — Q-CART-1)

Everything above is implementable on the grounded vanilla surface **except** the one
thing v1 deliberately deleted: the full-screen map. When a Station or Local Map
"reveals" terrain, that fog lands in `Minimap.m_exploredOthers` — but with the M-key
map gone, it only becomes visible on the **minimap circle**. Three coherent
directions (Daniel picks; this gates §3.3/§4.3):

- **(A) Minimap-only reveal.** Reading a map clears fog on the rotating minimap
  circle only. Zero new UI. Diegetically thin (you "know the area" but still have no
  big map). Cheapest, most consistent with the nomap pillar.
- **(B) Local Map item opens a bespoke viewer.** Using a Local Map opens a *framed,
  static, hand-drawn* full-screen image rendered from the blob — explicitly NOT the
  vanilla live map (à la `NomapPrinter`'s "screenshot" approach). This is the
  richest and most on-theme ("you unfurl the leather map"), but it's the most work
  (custom render of the explored region to a texture + a UI frame) and needs a
  scope/risk call.
- **(C) Station-proximity large map.** The M-key map works **only while standing at
  a Map Station** (patch `Minimap.SetMapMode`/input to allow Large mode when near a
  Station). "You can read the big map at the survey post, not in the wild." Middle
  ground; reuses vanilla's map renderer instead of a custom one.

🟡 My lean: **(C)** for the Station (reuses vanilla rendering, strong "go to the
post to study the map" loop) + **(A)** for a Local Map in the field (minimap reveal
only) — with **(B)** as a v2.1 stretch if the framed-leather viewer proves worth the
build. But this is the load-bearing aesthetic/scope decision and it's yours.

> **🟢 RESOLVED + CORRECTED (2026-06-11, issue 6, Daniel in-game).** Built as a
> standalone bounded fork (impl spec §2B) — the right call, because under v1's `nomap`
> vanilla forces the minimap roots OFF, so neither (C)'s station-Large-mode nor a reused
> root is reachable; the fork owns its own open path. **But the first render leaned too far
> toward (B)'s "bespoke, explicitly-NOT-the-vanilla-map" framing and painted a two-color
> fog mask** — Daniel: *"it should be almost identical in behavior to the regular map, just
> with the shroud at a fixed RADIUS, and the no-zoom alterations."* **Corrected design
> intent: the viewer is the (B) fork SHELL rendering (C)-grade vanilla cartography** — reuse
> a copy of vanilla's map material (the 4-texture biome/height/forest/fog shader composite)
> masked to our fixed-radius fog window, at fixed zoom. The two deliberate departures from
> vanilla are ONLY: fixed 1000 m shroud radius + no zoom. Authoritative build path:
> `cartography-impl-spec.md` §2E.

> **🟢 OPEN-INPUT CORRECTED (2026-06-11, issue 7, Daniel in-game).** Built as a standalone
> bounded fork (B-shell). The first build wired the fork's OPEN trigger to the vanilla
> **"Map" button** on the assumption that "Map is dead under nomap." That assumption is
> **false for this playtest's world.** Decomp truth (`Minimap.cs`): `Game.m_noMap==true`
> forces `SetMapMode→None`, killing **both** the M-map AND the minimap circle; so "Map" is
> only dead when there's no minimap either. Daniel sees a minimap circle AND M opening the
> global map → the world is **`nomap=OFF`**, where vanilla's M-map is fully alive. Binding
> our viewer to "Map" therefore stacked both maps. **Corrected: the equipped Local Map opens
> on the Use key (E)** — the same gesture the Surveyor's Table uses — off "Map" entirely, so
> it's correct regardless of the nomap config. Authoritative path: `cartography-impl-spec.md`
> §2F. **Separately surfaced (NOT this card):** the v1 "no M-key full map" baseline
> (`PARKED-2026-06-03.md:20`) was never actually implemented — no patch clamps vanilla's
> Large map — so under `nomap=OFF` players currently have the full vanilla map. Whether to
> ship that nerf (a `Minimap.SetMapMode` clamp) or relax the baseline is a separate Daniel
> call (impl-spec §2F.3).

## 7. Construction, gating & build-order summary

- **Build order (lowest→highest risk):** Map Station piece (clean — it's a
  re-gated `MapTable` on public APIs) → Cartographer's Kit + pigment gate → Local
  Map item storage → the *viewer* (risk concentrated entirely in whichever Q-CART-1
  option is chosen; (B) is the only high-risk item in the whole system).
- **All clean-room/ADR-0006:** additive construction, vanilla read as blueprint,
  reference mods studied not copied, no decompiled source committed.
- **Spec-first:** when each section ratifies, it moves into a proper
  `requirements.md` block (or a new `docs/v2/planning/requirements.md` if Daniel
  wants v2 to get its own semver planning dir — Q-CART-8), and `SpecCheck.cs` recipe
  counts update with the code.

## 8. Acceptance tests (named — refine as mechanics lock)
- [ ] **AT-STATION-1:** A placed Map Station persists its recorded fog+pins across a
  server restart (ZDO blob round-trips, compressed, like vanilla `MapTable`).
- [ ] **AT-STATION-2:** Writing to a Station merges the surveyor's current
  exploration in cumulatively; a second surveyor's write adds, doesn't overwrite.
- [ ] **AT-LOCALMAP-1:** A freshly-crafted Local Map reads as blank ("blank
  leather") until imprinted at a Station.
- [ ] **AT-LOCALMAP-2:** An imprinted Local Map handed to a second player, on read,
  reveals exactly the imprinting Station's fog/pins to that player (and persists on
  the item through the trade).
- [ ] **AT-KIT-1:** The Cartographer's Kit is uncraftable until all four basic
  pigments have been discovered; craftable immediately after the fourth.
- [ ] **AT-KIT-2:** Station writes and Local Map imprints are impossible without the
  Kit present; possible with it.
- [ ] **AT-VIEW-1:** (depends on Q-CART-1) the chosen viewing mode shows revealed
  terrain and hides unexplored terrain correctly under v1's map nerf.
- [ ] **AT-PILLAR-2:** No cartography UI, tooltip, or text assigns a meaning to any
  pigment color.
- [ ] logs-green ≠ playable: every AT closes only on Daniel's in-game check.

## 9. 🔴 OPEN QUESTIONS — Daniel's calls (these gate the build)

1. **Q-CART-1 — How is a map VIEWED?** ✅ **RESOLVED (2026-06-10 build; render
   CORRECTED 2026-06-11, issue 6).** Built as a standalone bounded fork (nomap forces the
   vanilla roots off, so (C)'s station-Large-mode is unreachable; the fork owns its own
   open path) that renders **(C)-grade vanilla cartography** inside a **(B)-style framed
   viewer** — reuse a copy of vanilla's 4-texture map material, masked to a fixed 1000 m
   shroud at fixed zoom. NOT the two-color mask the first build shipped. See §6 resolution
   note + `cartography-impl-spec.md` §2E.
2. **Q-CART-2 — Station knowledge: cumulative & shared, or per-surveyor?** Does a
   Station accumulate everyone's exploration into one shared regional record
   (recommend), or keep each player's separately?
3. **Q-CART-3 — Pigment-discovery gate persistence.** "Discovered all 4 pigments" —
   track per-player (your character earns it) or per-world? And is it
   first-time-crafted, or must you currently hold them? (Recommend per-player,
   first-crafted, permanent.)
4. **Q-CART-4 — Pin sharing model for maps.** Adopt the per-pin explicit-sharing
   model from `pin-sharing.md` (recommend), or accept vanilla's all-or-nothing
   per-write for v2 and refine later?
5. **Q-CART-5 — Recipes** for all three (Station / Local Map / Cartographer's Kit).
   I've proposed Black-Forest-tier eyeball recipes (§3.4/§4/§5.4); they need your
   lock. Also: does the Station require an Explorer's Bench in range to place?
6. **Q-CART-6 — What IS the Cartographer's Kit physically?** (a) wielded tool, (b)
   carried enabler item (recommend), or (c) a new mode on the Spade?
7. **Q-CART-7 — Name the station.** You asked for better than "Map Station." Grounded
   Old Norse / evocative candidates (each with meaning so the name isn't hollow):
   - **Varða** — an actual Old Norse word for a *wayfinding cairn/marker stone*.
     On-theme but risks confusion with our existing Cairn feature.
   - **Landvörðr** — "land-warden"; the post that keeps watch over known territory.
   - **Skygnir** — "the watcher/seer"; one who scans the land. (Note: Seer's Stone
     is a separate v4 feature — possible thematic overlap.)
   - **Spástaðr** — "prophecy/seeing place."
   - Fresh non-Norse options if you'd rather it read plainly: **Surveyor's Post**,
     **Waypost**, **The Reckoning Stone**, **Chart Stone**, **Mappy Stane** (Scots).
   My lean: **Landvörðr** (warden-of-the-known-land fits a knowledge-retention post)
   or plain **Surveyor's Post** if you want legibility over flavor. Your call.
8. **Q-CART-8 — Docs home for v2.** Keep ratified v2 specs appended to the existing
   `requirements.md`, or stand up a `docs/v2/planning/` semver dir for the Black
   Forest tier? (Recommend the latter once 2+ sections lock.)
9. **Q-CART-9 — Scope cut for first ship.** Is the v2 cartography MVP all three
   features at once, or a thin slice first (e.g. Station + minimap-reveal only,
   Local Maps + Kit in a follow-up)? Affects how I decompose the cards.

---

## 10. ✅ LOCKED DECISIONS (Daniel, 2026-06-10) — supersede the 🟡/🔴 above where they conflict

> These are ratified. Where a locked decision here contradicts an earlier PROPOSED
> section, **this section wins**; the prose above stays for design history but is
> superseded. Next promotion step: fold these into `requirements.md` proper.

### D1 — Q-CART-1 RESOLVED: the Local Map is an EQUIPPABLE item with a bounded fork of the vanilla map viewer
The viewing model is **(B′)** — a *fork of the vanilla map UI*, not the minimap-only
reveal (A) and not the vanilla big map (C). Concretely:

- **The Local Map equips like a weapon/shield/torch.** It's an `ItemDrop` with an
  equip slot (grounded: `ItemDrop.ItemData.ItemType` has `Shield=5`/`Torch=15`/
  `Utility=18`/`Tool=19` at decomp :57627 — pick the slot that reads right; Utility
  is the natural fit for a non-combat held tool). **Right-click (the item's
  secondary-use / `m_secondaryAttack` path) SETS it as your active minimap.**
- **Durable while equipped-then-unequipped, gone when removed from inventory.** While
  the map item is *in your inventory* its binding stays active (the minimap keeps
  showing that map even if you swap to a weapon); the moment the item *leaves your
  inventory* (dropped, traded, destroyed) the minimap reverts to nothing. So the map
  is a physical prerequisite you must keep carrying — lose the leather, lose the map.
- **Both the minimap AND the full view are a FORK of the current map viewer** with
  three hard constraints:
  1. **No pinning interface** on the Local Map view (read-only in the field — pin
     *editing* happens only at the Surveyor's Table, see D4).
  2. **Fixed zoom distance** — both the minimap circle AND the full-screen view are
     fixed-zoom (no scroll-to-zoom). One authored scale each.
  3. **Hard 1000 m radius**, centered on **the Surveyor's Table the map was bound
     to** (NOT the player). Everything beyond 1000 m of that bound origin is
     permanent shroud and never reveals.

### D2 — Q-CART-1b RESOLVED: the fog array is SIZED to 1000 m, not the whole world (Daniel's over-provisioning catch — confirmed by the math)
Vanilla's fog is a `256×256` `bool[]` (`m_textureSize=256`, `m_pixelSize=64 m/px`,
decomp :46692/:46694) covering the **entire ~16 km world** (`WorldToMapPoint`,
:47977). Reusing that whole array to store a 1000 m local map is exactly the
over-provisioning Daniel flagged.

**Locked: the Local Map / Station fog is a small array sized to its 1000 m radius,
at its own pixel resolution — NOT the vanilla 256² world array.** The numbers
(verified):
- A 2000 m diameter at vanilla's coarse 64 m/px = a `32×32` array (1,024 bools) —
  **1.6 %** of vanilla's 65,536. But 64 m/px is too coarse for a small local map to
  look good.
- 🟡 **Recommended resolution: 8 m/pixel** → a `~251×251` array (~63 k bools) — about
  the SAME storage as one vanilla world-map, but at **8× finer** spatial detail over
  the 1000 m disc (a crisp local map for the storage cost of the coarse world one).
  16 m/px (`126×126`, ~16 k bools) is the lighter-weight alternative if storage on
  the item/ZDO matters more than crispness. **Pick one before build (D2-RES, a sub-
  question — my lean is 8 m/px).**
- **Implementation note:** this means we do NOT just hand the blob to
  `Minimap.AddSharedMapData` (which expects the 256² world array). The Local Map
  carries its OWN compact fog array + the bound-origin world coord + a resolution
  tag; the forked viewer renders THAT array directly, clipped to the 1000 m disc.
  This is a bigger fork than "reuse vanilla's map texture," but it's the right call —
  it's what makes the 1000 m bound, the fixed storage, and the shroud-everything-else
  all fall out by construction.

### D3 — Player-outside-the-map: an EDGE ARROW, not a blank map
When the player is **outside the 1000 m disc** of the bound Surveyor's Table, the map
can't show their position (they're off it). Instead, render **a direction arrow at
the edge of the map view pointing toward the bound Station** — "your map is back
that way." Grounded precedent: vanilla already has `Minimap.ClampToScreenEdge`
(:34731) for clamping off-screen ping/chat markers to the screen edge — same pattern,
reused for the off-map player→station indicator. Pins likewise **only render within
the 1000 m radius**; anything beyond the disc is not drawn.

### D4 — The Surveyor's Table (Q-CART-7 RESOLVED: the name) pulls up the SAME view, but with pin EDITING
- **Name locked: "Surveyor's Table."** (Supersedes the Norse candidates in §9 Q-CART-7
  and the "Map Station" placeholder throughout.)
- **Using the Surveyor's Table opens the SAME forked viewer as a Local Map** — same
  fixed zoom, same 1000 m disc bound to that table — **except it operates on the
  Table's SHARED map data and ALLOWS pin removal** while open. So: field view (Local
  Map) = read-only; at-the-Table view = the shared record, with pin-removal editing
  enabled. (Pin *adding* model still gated on Q-CART-4; pin *removal at the table* is
  locked here.)

### D5 — Cartographer's Tools gate ALL auto-mapping (reaffirmed)
**Without the Cartographer's Tools, NO auto-mapping happens at all** — zero passive
fog reveal from walking. Exploration only writes to a map when the player has the
Tools. This is the hard gate that makes cartography an earned capability, not a
default. (Consistent with §5; reaffirmed as locked. Note: "Cartographer's Tools" /
"Cartographer's Kit" are the same thing — normalize the name in the requirements.md
promotion.)

---

## 11. ✅ CORRECTIONS & REFINEMENTS (Daniel, 2026-06-10, second pass) — these override §10 where they conflict

### C1 — D3 CORRECTED: the player marker clamps to the 1000 m SHROUD RADIUS, not the screen edge
My D3 said "clamp to screen edge" (the vanilla `ClampToScreenEdge` ping pattern).
**Wrong.** The clamp is to the **1000 m shroud boundary of the bound map**, not the
screen. When the player walks outside the 1000 m disc, their position marker (or a
direction arrow) **pins to the edge of the mapped disc at 1000 m** — i.e. at the
radius circle where shroud begins — pointing outward to where they actually are
relative to the bound Surveyor's Table. It's a map-space clamp (to the disc radius),
not a screen-space clamp. `ClampToScreenEdge` is the wrong precedent; this is a
"project the off-disc position onto the 1000 m circle" computation in map coordinates.

### C2 — D2 CORRECTED: fog resolution must MATCH the player's personal auto-map array — no custom 8 m/px
My "spend the storage saving on 8 m/px detail" idea is **rejected**, and for a real
reason: the Local Map is imprinted FROM the player's personal auto-map knowledge
(the fog the Cartographer's Tools accumulated as they walked). That personal
knowledge lives in vanilla's native fog resolution. **A custom-resolution local-map
array would not map cleanly onto the source** — we'd be resampling the player's
real explored fog into a different grid, introducing aliasing/registration error and
a conversion step every imprint. So the Local Map fog **uses the SAME pixel
resolution as the player's auto-map** (vanilla's `m_pixelSize`), just **windowed to
the 1000 m disc** — a sub-rectangle/sub-disc of the native grid, not a rescaled one.
- The over-provisioning fix STILL holds, just done correctly: store only the
  ~1000 m-radius window of cells at native resolution (≈ a `32×32` window at vanilla's
  64 m/px, or whatever the player auto-map's true `m_pixelSize` is), NOT the full
  256² world array. Same grid, fewer cells. Clean copy, no resample.
- **D2-RES is therefore CLOSED:** resolution = whatever the personal auto-map uses;
  we don't pick 8 vs 16. The only stored thing is the windowed cell range + the bound
  origin. (Confirm the auto-map's real `m_pixelSize` at build — the personal map the
  Tools write to may differ from the 64 m/px world minimap default.)

### C3 — D1 CORRECTED: the Local Map is a TWO-HANDED equippable; full view requires it equipped
- **The Local Map is equipped as a TWO-HANDED item** — it occupies BOTH hands,
  omitting weapon AND shield while held. You cannot fight with the map out; reading
  the map is a deliberate "put your weapon away and study it" act. (Grounded:
  `IsTwoHanded()` at decomp :13815 — a `TwoHandedWeapon`/`TwoHandedWeaponLeft`/`Bow`
  unconditionally unequips the left hand on equip, :13836-13840.)
- **🔴 It must truly UNEQUIP the shield, NOT hide it (Daniel, 2026-06-10 — block-weirdness fix).**
  Two vanilla mechanisms differ and only one is correct here:
  - `UnequipItem(m_leftItem)` → sets `m_leftItem = null`. `GetCurrentBlocker()`
    (decomp :13xxx) reads `m_leftItem` directly, so a null left hand → no shield
    block → clean.
  - `HideHandItems()` → stashes the shield in `m_hiddenLeftItem` to RESTORE later
    (the transient path used for eating, etc.). A lingering/auto-restored
    `m_hiddenLeftItem` repopulates `m_leftItem` → the shield's **block state comes
    back / never fully clears** = the "block weirdness" to avoid.
  So the map's equip path MUST do a full `UnequipItem(m_rightItem)` +
  `UnequipItem(m_leftItem)` and explicitly null `m_hiddenRightItem`/`m_hiddenLeftItem`
  — exactly what vanilla's `Tool` and two-handed-weapon equip branches already do
  (:13836-13840). Do NOT use the hide-and-restore mechanism for the map. Verify
  in-game that with the map equipped, RMB/block does nothing (no ghost shield block)
  and that unequipping the map cleanly returns weapon+shield.
- **Full-screen map view REQUIRES the map equipped** (not merely in inventory). The
  minimap binding is the durable-while-in-inventory part (D1); the FULL view is only
  available with the two-handed map actively equipped in hand. So: carry it → minimap
  persists; equip it (two hands, weapons away) → open the full map.
- **🟡 OPEN sub-question (C3-TORCH): can we allow map + TORCH?** Daniel would accept
  map-and-torch if feasible. Decomp finding: a *pure* two-handed item force-unequips
  the left hand including torch (:13836), and there is **no vanilla "two-handed but
  keep torch" flag**. BUT the torch-equip path (:13846) has special-casing, and the
  achievable route is a **custom equip rule via a Harmony patch on `Humanoid.EquipItem`**:
  treat the map as occupying both hands for weapon/shield purposes but explicitly
  permit a left-hand `Torch` to coexist (mirror the patch logic vanilla already uses
  to let a torch sit in the left hand beside a one-handed right weapon, :13846-13850).
  Feasible, modest patch surface. **Decision needed: pure-two-handed (simplest, no
  torch) vs custom map+torch rule (a Harmony patch, but you get a lit map at night).**
  My lean: ship pure-two-handed first, add the torch exception as a fast follow if it
  feels bad in the dark. **NOTE for C3-TORCH:** the torch exception must use the SAME
  true-unequip discipline for the shield — permit only a left-hand `Torch`, still
  hard-`UnequipItem` any shield/left-weapon (never hide), so the block-clear guarantee
  holds whether or not a torch is allowed.

### C4 — Q-CART-6 RESOLVED: the Cartographer's Kit is an ACCESSORY (Megingjord/Wishbone slot)
The Cartographer's Kit/Tools is an **equippable accessory in the Utility slot** — the
SAME slot as Megingjord (belt) and Wishbone. (Grounded: `ItemType.Utility = 18`,
decomp :57646; the player's dedicated `m_utilityItem` field :12874, fully separate
from both hands.) So the loadout model is clean and non-conflicting:
- **Kit (accessory / Utility slot):** worn passively; its presence is what enables
  auto-mapping (D5). Coexists with any weapon/shield/map — it's not a hand item.
- **Local Map (two-handed):** the artifact you imprint and read; equipping it for the
  full view costs you both hands.
- These never fight for a slot. You explore with the Kit worn (auto-mapping on) +
  weapons in hand; you stop and equip the Map (two-handed) to study the full view.
- **Note (Daniel, standing):** the Kit *may eventually fold into "holding a Local Map"*
  and disappear as a separate item — but for now it stays a distinct earned accessory.
  Design around it existing; keep the coupling loose so folding it in later is cheap.

### Net open list after this pass
- **C3-TORCH:** pure two-handed map vs custom map+torch Harmony rule (lean: pure first).
- **Q-CART-2** (cumulative/shared station knowledge — D4 implies yes; confirm).
- **Q-CART-3** (pigment-gate persistence), **Q-CART-4** (pin-ADD sharing model),
  **Q-CART-5** (recipes for Table / Local Map / Kit + Black-Forest material lock),
  **Q-CART-8** (docs home), **Q-CART-9** (MVP scope cut).

---

## 12. ✅ LOCKED (Daniel, 2026-06-10, third pass)

### C5 — Q-CART-2 RESOLVED: shared+cumulative, but ONLY within the 1000 m bound
A Surveyor's Table accumulates **everyone's** exploration into one shared regional
record — **but only the fog/pins falling inside its own 1000 m disc.** Writes from any
surveyor merge into the Table's shared blob; anything they explored beyond 1000 m of
THIS table is not stored here (it belongs to whatever other table bounds it, or
nowhere). So a Table is a *shared, cumulative, locally-bounded* survey of its 1000 m
neighborhood — not a global shared map. (Confirms D4's "shared map data," scoped to
the bound.)

### C6 — Q-CART-8 RESOLVED: stand up `docs/v2/` now
Ratified v2 cartography specs promote into a new **`docs/v2/planning/`** semver dir
(mirrors `docs/v0.1.0/planning/`), not appended to the v0.1.0 `requirements.md`. Clean
tier boundary. (Two-file rule applies: each new docs folder gets `README.md` +
`index.md`.)

### C7 — Q-CART-5 PARTIAL: Cartographer's Kit recipe (pigment-mount theme)
**Cartographer's Kit = 2× each basic pigment + 4 Fine Wood** (the wood mounts a map to
be drawn on; the inks are the drawing medium — and consuming all four pigments you
*discovered* into the kit reinforces the 4-pigment gate diegetically).
- **The 4-ingredient-cap worry is UNFOUNDED (grounded in decomp):** the crafting panel
  (`InventoryGui.SetupRequirementList`, :42389) does NOT cap recipes at 4 ingredients.
  `m_recipeRequirementList` is a fixed slot array, but when ingredients exceed the
  visible slots the panel **cycles through them on a timer** (:42417-42423) — the
  recipe still crafts. Vanilla ships 5+-ingredient recipes (padded armor, Eitr gear)
  that render fine, so the panel has ≥5–6 slots and the SBPR bench reuses it.
- **Therefore: KEEP all five** (2×Red + 2×White + 2×Blue + 2×Black + 4 Fine Wood) —
  🟡 RECOMMENDED (full-palette theme, panel handles it). Dropping White to hit "4
  distinct items" is unnecessary AND slightly off-theme (White is one of the four
  gate pigments). **Final lock pending Daniel's keep-5-vs-drop-white call.**
- Table + Local Map recipes still TBD (Q-CART-5 remainder).

---

## 13. ✅ LOCKED (Daniel, 2026-06-10, fourth pass) — AUTHORITATIVE current state (supersedes the stale open-lists in §10/§11)

### C8 — Q-CART-4 RESOLVED: per-pin explicit sharing model
Adopt the **per-pin explicit-sharing model** from `design/pin-sharing.md` (each pin
has an owner; sharing is opt-in per pin), NOT vanilla's all-or-nothing-per-write. This
is the clean-room reimplementation the investigation already scoped. Pin *removal* at
the Table is already locked (D4); this locks the *add/share* model as per-pin.

### C9 — Q-CART-9 RESOLVED: FULL SCOPE for the first ship
The v2 cartography MVP is **all three features at once** — Surveyor's Table + Local
Maps + Cartographer's Kit — not a thin slice. 🔴 **Build-risk note (honest):** the
bounded map-UI fork (C1/C2/D2 — own 1000 m-windowed fog array, fixed-zoom forked
viewer, edge-clamp-to-disc, no-pin field view) is the single biggest unknown in the
tier, and full-scope means we don't de-risk it in isolation first. Mitigation is in
the decomposition, not the scope: the UI fork becomes its OWN early card that a
spike/proof can validate before the item+gating cards layer on top. (See §14
decomposition note.)

### C10 — Q-CART-3 DISSOLVED: the Kit is just a normal recipe — NO discovery-gate system
Daniel: *"I think you're overcomplicating how the tools are discovered/made. It's just
a recipe like any other."* **Correct — remove the pigment-DISCOVERY gate entirely.**
There is NO "you have discovered all 4 pigments" unlock flag, no per-player/per-world
discovery persistence to track (Q-CART-3 is moot — resolved by deletion). The
Cartographer's Kit is a **normal craftable recipe** at the bench, surfaced the vanilla
way (`IsKnownMaterial` — it appears once you've encountered its ingredients). **The
recipe COST is the natural gate:** needing 40 pigments means you've necessarily
engaged with the pigment system, with zero special-case machinery. This also simplifies
D5 — the Kit (accessory) still gates *auto-mapping* by being worn, but acquiring the
Kit is just "craft it like anything else."

### C11 — Q-CART-5: Cartographer's Kit recipe LOCKED
**Cartographer's Kit = 10 Red + 10 White + 10 Blue + 10 Black pigment + 4 Fine Wood.**
(Keep all five ingredient types — the 4-slot worry was unfounded, §C7. The heavy
40-pigment cost is deliberate: it's the gate, per C10. Fine Wood mounts the map to be
drawn on.) Crafted at the Explorer's Bench.

### C12 — C3-TORCH RESOLVED: map + torch ships FROM THE GATE (not a fast-follow)
The two-handed Local Map MUST allow a left-hand **Torch** in the first shipped version
— Daniel: *"Torch out the gate. Not fast follow."* So the custom equip rule is
MVP-critical, not optional:
- Harmony-patch `Humanoid.EquipItem` so the map occupies both hands for **weapon and
  shield** purposes but **explicitly permits a left-hand `Torch`** to coexist (mirror
  vanilla's :13846-13850 torch-beside-one-handed logic).
- The shield/left-weapon eviction still uses TRUE `UnequipItem` (never hide — the C3
  block-weirdness rule holds): equipping the map hard-unequips any shield/weapon and
  nulls the hidden slots, then allows ONLY a `Torch` back into the left hand.
- In-game gate: map equipped → can hold a torch (lit map at night), CANNOT block or
  attack; unequip map → weapon+shield return clean, no ghost block.

### 📋 AUTHORITATIVE OPEN LIST — ✅ ALL CLOSED (architect spec-pass 2026-06-10, card t_4be278de)
> Promoted into `docs/v2/planning/requirements.md` §1/§2/§7 + the buildable impl spec
> `docs/v2/planning/cartography-impl-spec.md`. Kept here for decision history.
1. **Keep-5-vs-drop-white** on the Kit — **RESOLVED: keep 5** (C11, Daniel).
   *(no longer open)*
2. **Surveyor's Table recipe** — ✅ **LOCKED:** Fine Wood ×10, Bronze ×2, Deer Hide ×4,
   Bone Fragments ×8 (Black-Forest tier; Bronze = Copper+Tin gate; in-band with
   vanilla's own Cartography Table economy). **Bench-in-range to PLACE = NO**
   (`Piece.m_craftingStation = null`, matching every Spade-placed SBPR piece — Sign,
   Path Lamp). *This reverses the earlier "lean: yes" — placing the Spade tool is
   bench-gated, but placing pieces via the Spade menu is not, and a field survey post
   shouldn't need base proximity.*
3. **Local Map recipe** — ✅ **LOCKED:** Deer Hide ×1 + Fine Wood ×1. **Crafted at the
   Explorer's Bench, NOT the Surveyor's Table.** *This reverses the earlier "lean: at
   the Table" — a `CraftingStation`'s `Interact` opens the crafting GUI (:56135), which
   would collide with the Table's map-viewer-on-Use. The Table-coupling the design wants
   is the imprint step, not the craft.*
4. **Local Map equip slot / `ItemType`** — ✅ **LOCKED:** `ItemType.TwoHandedWeapon`
   (=14) with empty attack anims, NOT a custom enum value and NOT `Utility`. `EquipItem`
   (:13798–14011) is a closed if/else-if with no default branch, so a custom type never
   equips; the `TwoHandedWeapon` branch (:13921) already performs the exact C3
   block-clear (true-unequip both hands + null hidden slots) for free. The C12 torch
   exception remains its own `Humanoid.EquipItem` Harmony patch (the only patch the
   ItemType choice requires).

That whole remaining design surface is now closed — nothing in this tier is open.

> ⚠️ The "Still open after this pass" / "Net open list" blocks in §10 and §11 are
> STALE — this §13 list supersedes them.

### Still open after this pass (smaller forks)
- **D2-RES:** 8 m/px vs 16 m/px fog resolution for the local map (storage vs
  crispness). Lean 8.
- **Q-CART-2** (station knowledge cumulative/shared — though D4's "shared map data"
  implies cumulative+shared; likely now resolved, confirm).
- **Q-CART-3** (pigment-gate persistence), **Q-CART-4** (pin-add sharing model),
  **Q-CART-5** (recipes + the equip slot choice), **Q-CART-6** (now largely answered
  by D1 — the *Local Map* is the equippable; the *Tools* are the separate gating
  item — confirm the Tools' physical form), **Q-CART-8** (docs home), **Q-CART-9**
  (MVP scope cut).

### New acceptance tests from this pass
- [ ] **AT-MAP-EQUIP:** equipping the Local Map + right-click sets it as the active
  minimap; the minimap shows ONLY that map's 1000 m disc.
- [ ] **AT-MAP-DURABLE:** binding persists while the item sits unequipped in
  inventory; reverts to no-map the instant the item leaves inventory.
- [ ] **AT-MAP-BOUND:** nothing beyond 1000 m of the bound Surveyor's Table ever
  reveals (permanent shroud); pins beyond 1000 m don't render.
- [ ] **AT-MAP-FIXEDZOOM:** neither the minimap nor the full view zooms (fixed scale
  each); the full view has no pinning interface.
- [ ] **AT-MAP-EDGEARROW:** when the player is outside the 1000 m disc, an edge arrow
  points toward the bound Station and the player marker is off-map.
- [ ] **AT-MAP-STORAGE:** the Local Map's fog array is sized to the 1000 m radius at
  the chosen resolution — NOT a full 256² world array.
- [ ] **AT-TABLE-PINEDIT:** using the Surveyor's Table opens the same forked viewer
  on the shared data and permits pin removal; the field Local Map view does not.
- [ ] **AT-NOAUTOMAP:** with no Cartographer's Tools, walking reveals ZERO fog.
