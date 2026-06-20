---
title: "Map Provider Model — local maps as data artifacts, global map, minimap binding (v2 revision)"
status: living
purpose: "Daniel's 2026-06-15 revision + clarification of the Trailborne cartography model: M-key behavior, the two map types (personal global map + local maps), the equipped-local-map 'provider' binding, how Cartographer's tools / Surveyor's Table write data, the bidirectional global<->local sync, and the nomap-on vs nomap-off behavior split. ARTIFACT model confirmed by Daniel. This doc is the living spec to iterate on as open questions close; it supersedes the M-key / viewer-open portions of cartography-impl-spec §2G and the issue-8 title work where they conflict. Grounded against assembly_valheim decomp (serialization, fog, minimap) — findings inline."
owner: Daniel (design authority); Starbright (capture + grounding)
supersedes_partial:
  - "docs/v2/planning/cartography-impl-spec.md §2G (Use-key open model) — M replaces E"
  - "card t_1f94d60a (M-key nerf I/II) — resolved as a THIRD answer (M owned by SBPR)"
---

# Map Provider Model (v2 revision — Daniel 2026-06-15)

> **STATUS: DRAFT for iteration.** Daniel stated this design 2026-06-15; ARTIFACT model
> confirmed. Sections marked 🟢 DECIDED are Daniel's locked calls; 🟡 OPEN are unresolved
> knobs awaiting his decision; 🔵 GROUNDED are decomp-verified facts Starbright pulled.
> Nothing here is an impl card yet — this is the design we iterate until the open
> questions close, THEN it becomes architect specs. Do not stamp OPEN items resolved.

---

## 0. The core reframe (Daniel's words, captured faithfully)

The map system has **two map types** and an **equipped-item "provider" binding** that routes
where survey data is written. M is the single map key; SBPR owns it.

1. **Personal global map** — the player's own explored/unexplored fog, exactly like vanilla.
   In Trailborne it only becomes *viewable on demand* much later (Mistlands tier — the
   **"Eye of Odin"**, working name). Until then it accumulates invisibly.
2. **Local maps** — craftable item artifacts, each bound to a region, carrying their own
   surveyed map **data** (the ARTIFACT model — see §3).

The **equipped local map is the active "provider."** Cartographer's tools write to whatever
local map is currently the provider. A Surveyor's Table does a bidirectional sync between the
provider local map and the personal global map (§4).

---

## 1. M-key behavior 🟢 DECIDED (Daniel)

| State | M does | Notes |
|---|---|---|
| Default (no local map bound, no Eye of Odin) | **Nothing** | Both nomap-on AND nomap-off. SBPR fully suppresses vanilla's Large-map toggle. |
| A local map is **bound** (currently/most-recently equipped, still in inventory) | **Opens the local map** | Works even after the map is UNEQUIPPED, as long as it's still bound (still in inventory). |
| Eye of Odin equipped (Mistlands, future) | **Opens the personal global map** | The on-demand world map. Not built yet. |

- 🟢 **Remove the E-to-open path entirely.** The equipped-map prompt changes from "[E] Open
  map" to **"[M] Open map"**. (Supersedes cartography-impl-spec §2G Use-key model.)
- 🔵 GROUNDED: SBPR must intercept M comprehensively. Vanilla wires M → `Minimap.SetMapMode`
  Large/Small toggle; `Game.UpdateNoMap` (decomp :85135) forces `MapMode.None` when
  `GlobalKeys.NoMap` is set, and `SetMapMode` (:47444) early-returns to None under `m_noMap`.
  SBPR owns the M input edge in all three rows above (no vanilla Large map ever).
- 🔴 Resolves card **t_1f94d60a** (M-key nerf I/II) as a THIRD answer: not "ship the clamp"
  nor "drop it" — **M does nothing by default and SBPR owns it.** Mark that card accordingly.
- 🔴 Obsoletes card **t_07f4f211** (issue 7: "E won't open looking down at terrain"). Do NOT
  fix the E predicate — E is being removed. Recommend archiving t_07f4f211 with a pointer here.

---

## 2. Minimap behavior 🟢 DECIDED (Daniel)

| Mode | Minimap at start | Binds to |
|---|---|---|
| **nomap ON** (Trailborne default) | **Does NOT appear initially** | The bound local map's data, once a local map is bound (§3). Appears + binds on bind. |
| **nomap OFF** | **Always available** | The player's **global map** (vanilla-style). |

- 🔵 GROUNDED: vanilla minimap visibility is the `m_smallRoot.SetActive` toggle inside
  `SetMapMode` (:47463 small-on / :47457 none-off). Under `m_noMap` it's forced off (:47457),
  which matches "doesn't appear at first" in nomap-on. SBPR re-enables + binds it when a local
  map becomes the provider.
- 🟢 **DECIDED (Daniel 2026-06-15): when bound to a local map, the minimap renders the LOCAL
  MAP's data** (Q1 → answer 1), as a **circular, rotating** minimap (Q2 → keep the §2H.1 circular
  rotate-to-heading viewer; do NOT retire it for a vanilla-fed surface). The custom circular
  viewer STAYS. → This keeps card **t_d44572f2** (issue 6, edge-bleed) LIVE — it's a real bug in a
  viewer we're keeping, not one that evaporates.

### 2.1 🟢 DECIDED (Daniel 2026-06-19 playtest) — the disc carries its NAME + a co-located `[M]` open-hint UNDERNEATH it
v0.2.27-playtest refinement: *"the [M] READ MAP display at the bottom doesn't really work for
me… maybe put the name of the map under the minimap and put the M key hint there to show it
will open the named local map?"* — Daniel, 2026-06-19, Niflheim #bugs.

The locked §2 model above specifies the disc renders the bound local map's data, but said
nothing about (a) showing the map's NAME or (b) where the open-hint lives. This addendum closes
both gaps:

- 🟢 The bound local map's **NAME** ("Local map for <Table>") renders as a caption **directly
  under the minimap disc** (top-right), NOT a cartouche on the disc face.
- 🟢 The **`[M]` open-hint** (rebind-correct `$KEY_Map`) is **co-located with the name**, under
  the disc — one visual unit reading *"[M] opens THIS named local map."* This **relocates** the
  old bottom-centre screen hint (it is moved, not duplicated; the floating bottom element is
  deleted).
- 🟢 Visibility tracks **disc-visibility** (provider bound + nomap-ON), NOT equip state — so the
  hint is present exactly when M actually opens the map (M opens a bound map even while
  unequipped-but-carried; the old equipped-only hint was narrower than M's real function).
- In **nomap-OFF** there is no SBPR disc (vanilla minimap owns the corner, §6) → no caption
  there; the map stays M-openable but the name+hint surface is the nomap-ON disc.
- 🔵 GROUNDED: the name is already stored (`LocalMap.TryGetName` / `FormatDisplayName`, used by
  the modal title today) — a one-call read, no new storage. Pure HUD presentation; SpecCheck +0.
- 🔴 Buildable spec: **`docs/v2/planning/local-map-disc-name-hint-impl-spec.md`** (architect
  card t_338f723b). Supersedes the bottom-centre PLACEMENT in
  `local-map-mkey-open-impl-spec.md` §5; the `$KEY_Map` token + rebind-correctness stand.

### 2.2 🟢 DECIDED (Daniel 2026-06-19 playtest) — both surfaces show the current-biome NAME (Path A)
v0.2.27-playtest enhancement: *"Minimap and local map need to have support for biome
indicators by the way."* — Daniel, 2026-06-19, Niflheim #bugs. On the A/B/C fork Daniel
chose **Path A** (the vanilla biome-NAME readout, NOT colour fills or a legend) and delegated
the layout to the architect: *"A and architect."*

The §2/§2.1 model specifies the disc renders the bound local map's data + carries its NAME +
`[M]` hint, but said nothing about a biome indicator. This addendum closes that:

- 🟢 BOTH SBPR cartography surfaces show the player's **current-biome NAME** as text (the
  literal vanilla map affordance the standalone overlay currently drops):
  - **Minimap disc:** the biome name is a line in the under-disc caption stack (§2.1) —
    the stack becomes **name / biome / `[M]` hint**. Updates on biome change (vanilla's
    change-driven minimap behavior).
  - **Local-map modal:** a **fixed** current-biome readout under the title cartouche. NOT a
    cursor-hover readout — the SBPR modal is a passive read-only view; vanilla-large-map-style
    cursor-hover is a **deferred follow-up** (needs modal input plumbing) if Daniel wants it
    after seeing the fixed readout in-game.
- 🟢 Both read the player's **current biome** (`Player.GetCurrentBiome()`), not a cursor/world
  lookup — one shared `MapSurface` helper feeds both surfaces (no divergent second path).
- 🔵 GROUNDED: `Player.GetCurrentBiome()` is already proven in-codebase (`SunstoneLens.cs:351`);
  the name uses vanilla `$biome_*` tokens (locale-correct, the same construction as
  `Minimap.UpdateBiome`), with a `Biome.None`/unlocalized guard so no `$biome_*` literal leaks
  (the 2026-06-05 sign-bug lesson). Computed live — **no `SurveyData` wire change** (Path A,
  not Path B-baked); pure HUD presentation; SpecCheck +0.
- 🔴 Buildable spec: **`docs/v2/planning/local-map-biome-indicator-impl-spec.md`** (architect
  card t_caf0f1cf). Extends the caption infrastructure landed in PR #205 (t_26bba85b).

---

## 3. Local maps = data ARTIFACTS 🟢 DECIDED (Daniel: artifact model)

A local map **carries its own surveyed map data** (fog, and whatever layers we survey). It is
a portable artifact — the data travels with the item. This is distinct from a "lens" (a thin
pointer into the global map); Daniel chose artifact so a local map is a real, self-contained,
potentially shareable map object.

### 3.1 Storage — NOT embedded in the live item ZDO; fetched on bind 🟡 OPEN (mechanism — Starbright leans m_customData, NOT Daniel-decided)
Daniel: *"the map data should probably not be embedded in a zdo on the local map for perf
reasons … Instead it is fetched when equipped or read from the surveyor's table."*

🔵 **GROUNDED — the perf question Daniel raised, ANSWERED (this is the load-bearing finding):**

**Does a carried local map's data blob get auto-serialized / broadcast to other players? NO,
not while carried alive.** Three distinct serialization surfaces, verified against decomp:

1. **Carried in inventory, player alive → LOCAL profile save ONLY, never a networked ZDO.**
   - `Player.Save(pkg)` (:19620) → `m_inventory.Save(pkg)` → the `ZPackage` goes to
     `PlayerProfile.SavePlayerData` → **disk file**, not the player's networked ZDO.
   - What OTHER players replicate about you is `VisEquipment` (:99292), which carries only
     **prefab-name hashes + variant ints** (`SetItem(slot, prefab.name, variant)`) — it does
     **NOT** carry `m_customData`. So a carried local map's blob is invisible to other clients.
   - **Cost while carried = (a) your local save file grows, (b) your client holds it in RAM.**
     Both local. **No network fanout, no per-player bloat.** ✅ Daniel's condition is satisfied:
     "if the blob isn't a big deal for other players while carried … then it's fine." It isn't.
2. **`m_customData` IS the per-item serialization channel** (string→string dict): written in
   inventory save (:57333), item-drop ZDO (:59197 `data_N`/`data__N`), and item clone (:58028).
   So storing the blob in `m_customData` is the *correct vanilla mechanism* — it's exactly how
   vanilla persists per-instance item data (the current Imprint already uses it, LocalMap.cs).
3. **The blob DOES become a networked ZDO when the map is DROPPED / in a TOMBSTONE (death) /
   in a CHEST** — then it's an item-drop or container ZDO that replicates to nearby clients.
   This is transient (only while the item is in-world, not carried) and bounded (one map's
   data, a few KB compressed — see §3.3). This is the only network-cost surface, and it's the
   same cost any dropped item with custom data incurs.

**So: the artifact model is clean for the carry case.** Daniel's "don't embed in the ZDO for
perf" instinct still has merit for a different reason — keeping the LIVE item lightweight and
fetching/rehydrating on bind avoids holding the blob hot when not needed — but it is NOT
required to avoid multiplayer broadcast bloat, because carried items don't broadcast customData.

🟡 **OPEN — the storage mechanism to pick (now a perf/ergonomics choice, not a correctness
one):**
- **(A) Blob in `m_customData`** (current Imprint approach) — simplest, rides vanilla
  per-item persistence, travels with the item automatically (carry/drop/trade/death all just
  work). Cost: the blob is always in the item record (local save + RAM); a finer-resolution
  map (§3.3) makes it a few KB. **Lean: this is probably fine** given the grounding above —
  "fetched on bind" can mean "deserialized into the active viewer on bind" rather than "stored
  elsewhere and pointered."
- **(B) Thin item + server-side keyed store, rehydrated on bind** — the item carries only an
  ID + region; the data lives in a separate store fetched on equip / from the table. Matches
  Daniel's "fetched when equipped" literally. Cost: novel serialization + a store keyed by map
  ID (the Traveller's-Cache-class complexity — real, and it complicates trade/death because the
  data no longer travels with the item automatically). 🔴 Tension: (B) makes the artifact
  *less* portable (the whole point of artifact) unless the store replicates with the item.
- **Starbright lean (mine, reversible): (A) blob-in-customData**, because the grounding shows
  the multiplayer-bloat fear doesn't apply to carried items, and (A) keeps the artifact truly
  self-contained (trade/death/drop all work for free). "Fetched on bind" = deserialize-on-bind,
  not a separate store. Revisit only if profile-save size or RAM measurably hurts.

### 3.2 The provider binding 🟢 DECIDED (Daniel)
- A local map becomes the **active provider** when **equipped**.
- It **stays** the provider until: (a) another local map is equipped (new provider), OR (b) the
  active local map **leaves the inventory** — dropped, traded, OR **death**.
- While bound, the **minimap binds + displays** (§2), and **M opens it** even after unequip (§1).
- 🔵 GROUNDED: equip detection already exists — `LocalMapEquipPatch` reads `Humanoid.m_leftItem`
  via reflection (AccessTools). The "leaves inventory incl. death" unbind needs an inventory-
  removal + death hook (death drops all items → the provider item is gone → unbind).
- 🟢 CONFIRMED (Daniel's design statement): "most recently equipped, still in inventory" (from
  issue 5) — if you equip map A,
  then unequip (still carried), A stays provider. Equip map B → B is provider. Unequip B (still
  carried) → B stays provider (most-recent). Confirm this is the intended precedence. (It
  matches Daniel's wording.)
- 🔴 Redefines card **t_1d1b505b** (issue 5, "carry disc") — it was a partial glimpse of THIS
  binding state machine. Fold it into this model; don't build it standalone.

### 3.3 Resolution 🟢 DECIDED (Daniel 2026-06-15) — vanilla-native 64 m, for now
🔵 GROUNDED: vanilla's whole map stack is **64 m per texel** (`m_pixelSize = 64f` :46694, over
a 256² texture = 16,384 m coverage; world is ~21 km diameter, edge wall at 10,500 m :81374 — so
the vanilla fog texture is actually *smaller than the world* and can't track the outer ~2 km
ring). 64 m/texel is coarse: a base fits in one texel; the "parchment smoothness" is shader
bilinear filtering over chunky data.

🟢 **DECIDED: local maps use vanilla-native 64 m resolution (the resolution we've discussed).**
Daniel: *"Never said local map is better map so it's fine for now."* Reversible "for now" — not
LOCKED-forever, but the current build target. Rationale: a local map's value is **what it covers
and that it's a portable artifact you survey/own**, NOT pixel detail beyond the global map. Equal
resolution to the global map is fine — the distinction is portability + the provider loop, not
fidelity.

**Downstream consequences of 64 m (all good):**
- **Blob is trivial.** A ~2 km-span local map ≈ 31×31 ≈ 1,000 cells, ~125 B raw fog, compresses
  to near-nothing. Even with a biome layer it's well under 1 KB.
- **Daniel's original perf worry largely dissolves.** At ~125 B–1 KB the blob is so small that
  the "don't bloat the item" concern essentially evaporates — which strengthens §3.1's lean
  toward storing it directly in `m_customData` (a sub-KB customData entry is negligible in the
  local profile save AND as a transient dropped-item ZDO).
- **local↔global table sync (§4) is loss-free** — both sides are 64 m, so no downsample/upsample
  on the bidirectional merge. (Resolves the §4 resolution-mismatch open question too.)
- No FINER survey grid to build — local maps read the same 64 m fog window the Kit already
  captures. Less net-new code.

🔵 If Daniel ever wants finer later, the artifact model supports it (just a bigger blob — a 8 m
grid over 2 km ≈ 62,500 cells ≈ 1–3 KB compressed, still customData-fine). Not now.

---

## 4. Surveyor's Table = bidirectional global<->local sync 🟢 DECIDED (Daniel)
When a Surveyor's Table is used:
1. The player's **personal global map** is updated with data for the area corresponding to the
   updated (provider) local map. (local → global)
2. **Global personal data is added to the local map** for its region. (global → local)

This bidirectional merge gives Daniel's three stated payoffs:
- **Overlapping local maps stay consistent** — both sync through the shared global truth.
- **A destroyed Surveyor's Table is rebuildable without data loss** — the data also lives in
  the personal global map, so a new table re-derives it.
- **Map data is present when the Eye of Odin (Mistlands global map) arrives** — because every
  table use has been feeding the global map all along.

🔵 GROUNDED: the personal global fog is `m_explored` (bool[256²], :46762), written by `Explore()`
(:48015) and **persisted for free** via `SaveMapData → PlayerProfile.SetMapData` (:48261) every
save. So "data present at Eye-of-Odin later" costs **nothing** — it rides vanilla's already-saved
global fog. The local→global write is a windowed `m_explored` stamp; global→local is a windowed
read of `m_explored` into the local map's artifact.
- 🟢 RESOLVED: resolution mismatch is N/A — both global and local are 64 m (§3.3), so the table
  sync is a straight 1:1 cell copy in both directions, no down/upsampling.

### 4.1 🟢 DECIDED (Daniel 2026-06-15) — local & global shrouds align 1:1 (exploration invariant)
**The local map shroud and the global map shroud MUST align 1:1.** A cell that is explored on the
local map is the SAME cell that is explored on the global map, and vice versa. This is an
**exploration-correctness invariant**, not a rendering nicety — it's what makes the bidirectional
table sync (§4) lossless and what guarantees that exploring through one surface reveals the same
ground on the other. Daniel: *"The local and global map shrouds should align 1:1 for exploration
reasons."*

- 🟢 **The accepted tradeoff (Daniel):** *"if that means the middle of the map doesn't perfectly
  align with the table, that's fine."* The grid wins; the table position floats. The local map's
  center cell is wherever the table's world position snaps to on the global grid — the table may
  sit anywhere inside its cell, and that sub-cell offset is acceptable.
- 🔵 GROUNDED — **the current code ALREADY does this** (this decision confirms an existing
  architecture choice, it is not a change):
  - `BoundedMapMath.ComputeWindow` snaps the bound origin to a global cell via `WorldToCellX/Y`
    (`= Round(world/64 + 128)`), then windows on that integer cell grid.
  - `BuildWindowedFog` reads `explored[srcY * textureSize + srcX]` — the **exact same array
    index** the global map uses. So a local-map fog cell IS a global-map fog cell. 1:1 by
    construction.
  - The table's sub-cell position is used ONLY to (a) clip the radius disc boundary in world space
    and (b) place the player marker continuously — neither offsets the fog grid. So "the middle
    doesn't perfectly align with the table" is precisely the behavior already shipped, and it's
    correct.
- **Implication for impl:** any future change to capture/render must PRESERVE this grid alignment.
  Do NOT re-center the fog grid on the exact table position (that would break 1:1 and desync
  local↔global exploration). Make this a hard acceptance test: a cell lit on the local map is lit
  on the global map at the same world coordinate, and table-sync is a pure index copy.

---

## 5. Cartographer's tools 🟢 DECIDED (Daniel)
- **Useless without a map provider.** With no bound local map, the tools do nothing.
- When a local map IS the provider, the tools **write survey data to that provider local map**.
- 🔵 GROUNDED: the Kit already patches `Minimap.UpdateExplore`/`Explore` (CartographersKit.cs:26,
  decomp :47056/:48005) BEFORE the nomap gate, so it can write fog while the map is hidden. The
  redirect is: instead of (or in addition to) writing `m_explored`, write the **provider local
  map's** artifact window.

---

## 6. nomap OFF — the behavior split 🟢 DECIDED (Daniel)
1. **Cartographer's tools are NOT craftable** and serve no function if present (e.g. settings
   changed mid-save). They're a nomap-ON-only mechanic.
2. **Minimap is always available**, bound to the **player's global map** (vanilla-style).
3. **Map revealing always works WITHOUT the Cartographer's tools**, writing to the **global map**.
   - If a local map is **also equipped**, revealing writes to **both** (global + the provider
     local map).
   - The local map is still viewable with **M**. The **global map is NOT showable with M** until
     the **Eye of Odin** is equipped later (same gate as nomap-on for the global view).
- 🔵 GROUNDED: nomap-off = `GlobalKeys.NoMap` NOT set → vanilla `Explore` writes `m_explored`
  normally (passive reveal "always works"). The only SBPR change in nomap-off is: suppress the
  vanilla M→Large-map (M still does nothing for the global map until Eye of Odin), keep the
  minimap, and dual-write to a provider local map when one is equipped.
- in nomap-off, the minimap binds to the GLOBAL map (row 2) — and if a local map is also
  equipped, does the minimap show global, local, or both-merged?
  🟢 **DECIDED (Daniel 2026-06-15): GLOBAL.** In nomap-off the minimap is always the global map.
  A local map equipped in nomap-off still survey-writes to both (per item 3 above) and is still
  M-openable as its own artifact, but the always-on minimap stays bound to global — the local map
  does NOT take over the minimap in nomap-off. (In nomap-ON, by contrast, the minimap binds to the
  local map per §2, because there's no global minimap available there.)

---

## 7. 🟢 DECIDED (Daniel 2026-06-15) — the custom §2H.1 circular viewer STAYS
Daniel: keep the **circular, rotating** minimap (the §2H.1 viewer). Do NOT retire it for a
vanilla-fed surface. Consequences:
- 🔴 Card **t_d44572f2** (issue 6, parchment edge-bleed) stays **LIVE** — a real bug in a viewer
  we're keeping; fix it, don't wait for it to evaporate.
- 🔴 Card **t_1d1b505b** (issue 5, carry disc) is the §3.2 provider binding feeding this circular
  viewer at minimap size — redefined, not retired.
- The viewer renders the bound LOCAL map's data (§2), grid-aligned to the global fog (§4.1),
  player-centered, rotate-to-heading, circular bezel. All the §2H.1 mechanics stand.

---

## 8. Impact on existing cards (confirm before any get built)
| Card | Effect of this design |
|---|---|
| `t_1f94d60a` (M-key nerf I/II) | **Resolved** — third answer: M does nothing by default, SBPR owns it (§1). |
| `t_07f4f211` (issue 7, E won't open looking down) | **Obsoleted** — E is removed for M (§1). Recommend archive. |
| `t_1d1b505b` (issue 5, carry disc) | **Redefined** as the §3.2 provider binding. |
| `t_d44572f2` (issue 6, edge bleed) | **Possibly evaporates** (§7) if custom viewer retired; else still real. |
| `t_2ea6b719` (issue 4, map title) | **Mostly unaffected** — title still wanted; Daniel's `Local map for <name>` format (no quotes) stands. |
| `t_2dd7c705` (NRE fix) | **Unrelated** — still good to run independently. |

---

## 9. Open questions to close before this becomes impl specs

**Closed 2026-06-15:**
- ~~**Resolution**~~ 🟢 vanilla-native **64 m** (§3.3). Cascade: makes the blob sub-KB.
- **Storage mechanism** → 🟡 STILL OPEN (Starbright's lean, NOT Daniel-decided): blob is sub-KB
  at 64 m, so storing it **in `m_customData`** (§3.1 option A) is the cheap default, "fetched on
  bind" = deserialize-on-bind. This is a reversible recommendation, not a locked call — Daniel's
  stated instinct ("don't embed in the ZDO") leans the other way; the grounding shows embedding is
  safe for the carry case, but the final pick is the architect's/Daniel's at spec time. One-line
  to swap.
- ~~**Resolution mismatch on table sync**~~ 🟢 N/A — both sides 64 m, sync is a 1:1 cell copy (§4).
- ~~**Minimap render when bound to a local map**~~ 🟢 renders the **local map's data** (§2).
- ~~**Custom viewer fate**~~ 🟢 **KEEP** the circular rotating §2H.1 viewer (§7). Edge-bleed card
  t_d44572f2 stays live.
- ~~**Local/global shroud alignment**~~ 🟢 **1:1 exploration invariant** (§4.1) — grid-aligned,
  table floats sub-cell; already how the code works.

**Still open:**
1. ~~**nomap-off minimap with a local map also equipped**~~ 🟢 **GLOBAL** (§6) — the always-on
   minimap stays bound to global in nomap-off; the local map still dual-writes + is M-openable but
   does not take over the minimap.
2. **Eye of Odin** (Mistlands global-map unlock): deferred to the Mistlands tier — out of scope
   for THIS build, specced when that tier is built. The global map's M-view is gated behind it in
   BOTH modes. Not a blocker for the v2 cartography provider model.

**All v2-blocking questions are now CLOSED.** The only remaining item (Eye of Odin) is a future
Mistlands-tier feature that this model is forward-compatible with (global fog accumulates all
along, §4). This design is ready to graduate to architect impl specs.

---

## 10. Provenance
- Design stated by Daniel 2026-06-15 (Discord, engineering lane), revising the cartography model.
- Artifact model + multiplayer-blob concern raised by Daniel; serialization grounding by Starbright
  against `assembly_valheim.decompiled.cs` (inventory/customData/fog/minimap paths cited inline).
- This doc is the iteration surface; close the §9 questions, then it graduates to architect specs.
