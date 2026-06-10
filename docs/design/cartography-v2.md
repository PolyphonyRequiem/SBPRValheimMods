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
  - `nomap=ON` (server) → no map at all.
  - `nomap=OFF` (default) → **minimap circle ONLY**, freely rotating, **no north
    indicator, no M-key full map**.
  - The vanilla **Cartography Table is nerfed**: existing ones lose function, new
    ones can't be built.
- **Design pillars this must honor** (`design/design-pillars.md`): Pillar 1 — trail
  tools are peers, placed by the Spade, not the Hammer. Pillar 2 — **color
  semantics are emergent**; never hard-code "blue = water." Cartography UI/tooltips
  must not assign meanings to pigment colors.
- **The headline tension (read before designing):** v1 deliberately removed the
  full-screen map. A cartography system that "reveals the map" has nowhere vanilla
  to draw it. **How a Local Map is *viewed* is the central open question** (§6,
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

1. **Q-CART-1 — How is a map VIEWED?** The central fork (§6): (A) minimap-only
   reveal, (B) bespoke framed full-screen "leather map" viewer, or (C) the vanilla
   big map re-enabled only at a Station. My lean: C for Stations + A for field Local
   Maps, B as a stretch. **This decision gates almost everything else.**
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
