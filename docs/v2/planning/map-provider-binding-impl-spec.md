---
title: "Map provider binding + carry-state minimap disc — buildable implementation spec"
status: current
purpose: "Build-ready architect spec for the equipped-local-map PROVIDER BINDING state machine (design map-provider-model.md §3.2) and the carry-state circular minimap disc it feeds (§2/§7). Converts the merged design doc into one section an engineer-ui implementer picks up cold: the provider identity + most-recent-equipped tie-break, the unbind triggers (re-equip / leave-inventory / death), driving the controller's own §2H.1 circular viewer at minimap scale (NOT vanilla m_smallRoot), and named acceptance tests grounded against LocalMapController.cs + MapViewer.cs at v1 HEAD. Authored by the architect spec-pass (card t_1d1b505b). The design doc is the WHAT; this is the HOW-to-pick-it-up-cold. Build is the engineer-ui child of this card; Daniel gates the merge."
owner: Daniel (design authority); architect (spec capture + grounding)
supersedes_partial:
  - "card t_1d1b505b (issue 5, 'carry disc') — reframed as the §3.2 provider-binding state machine per map-provider-model.md §3.2"
---

# Map provider binding + carry-state minimap disc — buildable implementation spec

The merged design doc [`map-provider-model.md`](../../design/map-provider-model.md)
(PR #155, §3.2 + §2 + §7) is the locked *what*. This doc is the buildable *how*: the
provider-binding state machine, the carry-state minimap disc that reuses the §2H.1
circular viewer at minimap scale, observable acceptance criteria, the
`Features/Cartography/` placement, and the SpecCheck impact (none). An `engineer-ui`
implementer should build the whole feature from this section without re-deriving anything.

> **Clean-side note (ADR-0001):** every vanilla decomp line cited here is the base game
> (`assembly_valheim`), **fair game to read + adapt** (repo AGENTS.md + the 2026-06-09
> clarification). Line numbers are inherited from the cited code comments / the
> cartography-impl-spec, which were grepped live against
> `~/valheim/worldgen-spike/decomp/assembly_valheim.decompiled.cs` — re-confirm against the
> build assembly if the dump drifts. No other mod's code is read or copied.

> **Code coupling (load-bearing):** the carry disc MUST reuse the §2H.1 circular viewer
> (`MapViewer.cs`) as a **scaled instance**, not a parallel implementation, so it inherits
> the edge-bleed clip fix landing in **t_d44572f2 / PR #159** (engineer-ui, blocked-for-review
> on `v1`). **Sequence this build AFTER #159 merges.** A second bezel/clip implementation
> would re-ship the same disc-edge bleed at minimap size.

## 0. SpecCheck manifest impact (read first)

**None.** This feature adds no item recipe and no build piece — it is provider-state +
presentation behaviour on existing prefabs (`SBPR_LocalMap`, registered in `LocalMap.cs`).
`Runtime/SpecCheck.cs` is **untouched**. (Spec-first still applies: the **implementation**
PR carries the code + the `cartography-impl-spec.md` §2A.4/§2H.1 cross-ref banners together —
see §8 for why those banners are NOT in this spec-pass PR.)

---

## 1. What is DECIDED vs OPEN (read before touching anything)

The design doc marks each call. Carry these forward verbatim — do **not** reopen 🟢, do
**not** stamp 🟡.

**🟢 DECIDED (Daniel — build exactly this):**
- **Provider identity:** the equipped local map becomes the active *provider* (§3.2).
- **Persistence:** it stays the provider after unequip while it remains in inventory (§3.2).
- **Unbind triggers:** another local map is equipped (new provider), OR the provider map
  leaves inventory — dropped, traded, OR death (§3.2).
- **Tie-break:** most-recently-equipped-still-carried wins (§3.2, confirms issue-5 wording).
- **Minimap data:** when bound, the minimap renders the **local map's** data (the imprinted
  1000 m survey), circular, rotate-to-heading, reusing the §2H.1 viewer — NOT vanilla
  `m_smallRoot` (§2, §7).
- **No north reference** of any kind on the disc (§7 inherits §2H.1 mechanic 5).
- **Resolution:** 64 m/texel (§3.3) — already what the survey window carries; no new grid.

**🟡 OPEN (route to Daniel at review — do NOT stamp, do NOT attribute to Daniel):**
- **R1 — Disc centring (THE load-bearing render question).** See §6. The design doc is
  internally inconsistent here: §7 says "player-centered" while the same sentence says "all
  the §2H.1 mechanics stand," and §2H.1 mechanic 1 is "TABLE-centred, no pan." Both cannot
  hold. The player-centred full view was built (old §2H) and **rejected** by Daniel in the
  v0.2.22 playtest. This spec specifies the binding fully and flags centring as a single knob
  with a recommended default; Daniel resolves it at review.
- **R2 — Storage mechanism (§3.1 A vs B).** Starbright's reversible lean is (A) blob-in-
  `m_customData`; NOT Daniel-locked. **Orthogonal to this card** — the disc reads a
  `SurveyData` snapshot via `LocalMap.ReadSurvey(provider)` regardless of how the blob is
  stored/rehydrated. Spec the binding without locking storage; note the dependency only.

---

## 2. Where the code stands today (grounded against v1 HEAD)

Read these before coding — the carry path already exists as a *state machine* but renders
nothing, and the viewer already does circular rotate-to-heading for the full view. This card
wires the two together at minimap scale.

### 2.1 The controller already detects carry, but only logs

`Features/Cartography/LocalMapController.cs` (a client-only `MonoBehaviour` attached to the
live `Minimap`, polled every `PollSeconds = 0.25`):

- It tracks `_hadMapCarried` / `_hadMapEquipped` and fires on transitions (`:137`, `:147`).
- On the **carry** transition it **only logs** — `Plugin.Log.LogInfo("…Local Map bound (in
  inventory)…")` at `:140`. **It never instantiates a disc. That missing render is this card.**
- Provider selection today is wrong for the new model: `carriedMap = equippedMap ??
  GetCarriedLocalMap(player)` (`:129`), and `GetCarriedLocalMap` (`:330`) returns the **first**
  `SBPR_LocalMap` it finds in `inv.GetAllItems()` — **no provider identity, no most-recent
  tie-break.** Replacing that selection with the provider state machine (§3) is the core change.
- The full-view open path (`OpenFullView`, `:255`) and the equip→full-view flow (`:112-121`)
  are **correct and must stay intact** (design §1 keeps "equip opens the full view"; note the
  open *input* is M per design §1, an orthogonal card — do not touch it here).

### 2.2 The viewer already renders a circular rotate-to-heading disc

`Features/Cartography/MapViewer.cs` — the single forked viewer behind the `CartographyViewer`
seam — already builds the §2H.1 hierarchy this card reuses:

- `_frame` (`:134`) is the FIXED, screen-aligned circular clip+bezel that **never rotates**;
  `_mapContainer` (`:135`) is the rotating interior; `_bezel` (`:140`) is the ring;
  `EnsureBezelTexture` (`:336`) builds the clip. **After PR #159 this is a hard alpha clip** —
  the disc inherits it for free.
- `ApplyFieldOrientation` (`:766`) rotates the interior to heading about screen-centre and
  pins `_mapRect.anchoredPosition = Vector2.zero` (`:773`) — i.e. **TABLE-centred, no pan**
  (the §2H.1 lock). This is the exact line the centring question (R1, §6) turns on.
- It is a **single full-screen modal** today: `_canvas.sortingOrder = 5000` (`:986`), a dim
  full-screen backdrop (`:993-999`), an `[Esc] Close map` prompt, and a fixed
  `MaxFullViewPx = 900` square (`:71`). **A minimap disc is none of those things** — it must be
  small, corner-anchored, always-on (no backdrop, no Esc prompt, not modal). That delta is the
  viewer-side work (§4).

### 2.3 The seam is already the right boundary

`CartographyViewer` (`CartographyViewer.cs`) brokers `Open/Refresh/Close/IsViewerOpen/
CurrentMode` and degrades gracefully when no viewer is registered. **It is full-view-shaped
today** (one `Open(MapViewRequest)` → one modal). The disc needs a *concurrent, persistent*
surface that coexists with the full view, so this card **extends the seam** with a minimap
channel (§4.1) rather than overloading `Open`. `MapViewerMode` (`FieldReadOnly` / `TableEdit`)
gets a third member or a parallel minimap entry point — the implementer picks the lower-churn
shape; §4.1 gives the contract either way.

---

## 3. The provider state machine (§3.2 — fully DECIDED, build exactly this)

The provider is a single client-side identity tracked in `LocalMapController`. It is **the**
new state. All of §3 is 🟢 — no open questions here.

### 3.1 Provider identity — track the instance, not "is a map carried"

Add a provider field that survives unequip:

```
private ItemDrop.ItemData? _provider;   // the active provider local map instance, or null
```

**Identity = the `ItemDrop.ItemData` instance reference** (the same object `GetEquippedItems()`
/ `GetAllItems()` return). Do **not** key on prefab name or display name — every `SBPR_LocalMap`
shares those; only the instance distinguishes "map A" from "map B". The instance reference is
stable while the item lives in the inventory (vanilla never reallocates `ItemData` for a carried
item), which is exactly the provider's lifetime.

> **Why not a ZDO / persisted id:** the provider is **client-only, session-scoped** presentation
> state (which of *my* carried maps drives *my* minimap right now). It does not replicate and does
> not survive a relog — on relog nothing is equipped, so the provider is simply null until the
> player next equips a map (§3.4). This matches the controller's existing client-only design
> (`SystemInfo.graphicsDeviceType == Null` early-out, `:78`) and adds zero server cost.

### 3.2 Bind — equip sets the provider (most-recent wins)

On each throttled poll, read the currently-equipped local map (`GetEquippedLocalMap`, `:320` —
unchanged). Then:

- **If a local map is equipped this poll** and it differs from `_provider`: **set
  `_provider = equippedMap`.** This is the whole tie-break — the most-recently-*equipped* map
  becomes the provider, replacing any prior one. Equip A → provider A; later equip B → provider
  B. (Re-equipping the *current* provider is a no-op.)
- Equip detection is edge-free here: because we assign on "equipped ≠ provider", simply equipping
  is sufficient; no separate `EquipItem` hook is needed. (The existing `LocalMapEquipPatch` stays
  scoped to its torch job — do not extend it for provider tracking; the poll is the single source
  of truth.)

### 3.3 Hold — provider persists through unequip while still carried

When the poll sees **no** equipped local map but `_provider != null`:

- **Verify the provider is still in inventory** — `player.GetInventory().ContainsItem(_provider)`.
  If yes: **keep `_provider` unchanged.** This is the durable-while-carried behaviour — the disc
  stays bound to the unequipped-but-carried provider.
- This replaces the old `GetCarriedLocalMap` "first carried map" selection entirely. With several
  maps carried and none equipped, the provider is whichever was equipped most recently — **not**
  an arbitrary inventory-order pick.

### 3.4 Unbind — provider leaves inventory (drop / trade / death)

When `_provider != null` and **`ContainsItem(_provider)` is false** (the instance is no longer in
the inventory), **clear `_provider = null`** and tear down the disc (§4.3). This single check
covers all three design unbind paths, because every one of them removes the `ItemData` from the
inventory:

- **Drop / trade** — the item moves to a dropped-item ZDO / another inventory; `ContainsItem`
  goes false.
- **Death** — vanilla `Player.OnDeath` drops the whole inventory into the tombstone
  (`assembly_valheim` — clean-side, fair to rely on), so the provider instance is gone from the
  live inventory; `ContainsItem` goes false. **No bespoke death hook is required** — the
  `ContainsItem` poll catches it on the next 0.25 s tick. (If a frame-tight unbind on death is
  ever wanted, that is a future refinement, not a v1 requirement; the poll is correct and simple.)

> **Edge: re-acquire after drop.** If the player drops the provider then re-picks it up without
> equipping, it comes back as a plain carried map with **no** provider status (provider was
> cleared on the drop tick, and binding only happens on *equip* per §3.2). The player re-equips to
> re-bind. This is the literal reading of §3.2 ("becomes the provider when equipped") and is the
> correct, predictable behaviour — do not add a "re-bind on re-pickup" path.

### 3.5 Blank maps — a provider with no survey shows no disc (but stays the provider)

A `SBPR_LocalMap` is blank until imprinted (`LocalMap.IsImprinted`, `LocalMap.cs:242`). If the
provider is blank, `LocalMap.ReadSurvey` returns null. **Keep it as the provider** (it was
equipped; it satisfies §3.2) but **render no disc** — there is nothing to draw. The moment it is
imprinted at a Table, the next poll's `ReadSurvey` succeeds and the disc appears. Do not surface
an error for a blank provider (the existing full-view path already messages "blank — imprint at a
Table" on *equip*; the passive minimap stays silent).

---

## 4. The carry minimap disc (§2 / §7 — reuse the §2H.1 viewer at minimap scale)

The disc is a **scaled, persistent, non-modal instance** of the same circular rotate-to-heading
hierarchy the full view uses. The hard rule (design §2, §7; peer comments): **reuse the §2H.1
viewer; do not fork a second circular renderer.** It must render via the controller's own viewer
and stay visible while vanilla's small/large roots are suppressed under `nomap`/`MapMode.None`.

### 4.1 Seam extension — a minimap channel alongside the full view

`CartographyViewer.Open(MapViewRequest)` is full-view-shaped (one modal). The disc must coexist
with the full view (a player can have the disc up AND open the full view), so add a **parallel
minimap channel** to the seam rather than overloading `Open`:

```
// CartographyViewer — new minimap entry points (names illustrative)
static void BindMinimap(MapViewRequest request);   // show/refresh the persistent disc
static void UnbindMinimap();                        // hide + tear down the disc
static bool IsMinimapBound { get; }
```

- `BindMinimap` is **idempotent + refresh-capable**: called every poll while bound, it shows the
  disc on first call and updates the bound survey/heading on subsequent calls (same pattern as the
  full view's `Open`-then-`Refresh`). The controller drives it from the provider state (§4.4).
- Reuse the existing `MapViewRequest` struct — `Survey` (from `LocalMap.ReadSurvey(_provider)`),
  `BoundOrigin` (from `LocalMap.TryGetBoundOrigin(_provider)`), `RadiusMeters = SurveyRadiusMeters`
  (1000 m), `Title = ""` (a minimap shows no cartouche), `PinEditor = null` (read-only, structurally
  cannot edit). Add **`bool Minimap`** (or a third `MapViewerMode.MinimapDisc`) so the viewer knows
  to build the small surface, not the modal. The implementer picks the lower-churn shape; the
  contract is "one viewer, mode-flagged surfaces," never two forks (consistent with §4 of the
  cartography spec).
- The full-view seam (`Open/Refresh/Close`) is **unchanged**. Disc and full view are independent
  channels into one `MapViewer` instance.

### 4.2 Viewer-side — a second, small, always-on surface

`MapViewer` currently owns one `_root` (the modal). Give it a **second root** for the disc
(`_minimapRoot`), built once, that reuses the SAME §2H.1 machinery with minimap parameters:

- **Layout:** small (e.g. ~`MinimapDiscPx ≈ 180–220` px — a tunable const, NOT 900), **corner-
  anchored** (top-right is the vanilla minimap's home; match it so it reads as "the minimap"),
  **no dim backdrop**, **no `[Esc] Close map` prompt**, **no title cartouche**. `sortingOrder`
  **below** the full-view modal (the disc is HUD-tier, e.g. ~`3000`, so opening the full view
  cleanly covers it).
- **Same circular machinery:** the fixed `_frame` + `_bezel` (inheriting the **#159 hard alpha
  clip**) + the rotating `_mapContainer` interior (cartography + shroud mask + pins + player
  marker), driven by the SAME `ApplyFieldOrientation` rotate-to-heading and the SAME
  `EnsureBezelTexture` clip. Factor the build so both roots share the layer-tree builder at
  different sizes — do **not** copy-paste a second hierarchy (that is the "parallel implementation"
  the design forbids and the path that would re-ship edge-bleed at disc size).
- **Bezel scaling caveat (call out for the implementer):** `EnsureBezelTexture` and the #159 clip
  geometry are written in screen-px against `MaxFullViewPx`. Driving them at `MinimapDiscPx`
  means the inset/margin constants (`BezelDiscInsetPx`, ring width) must scale with the surface, or
  be recomputed for the smaller radius — otherwise a px-fixed inset that is invisible at 900 px is
  a large fraction of a 200 px disc. Parameterize the clip by **fraction of radius**, not absolute
  px, when generalizing it for two sizes. (This is the single highest-risk detail in the viewer
  work; flag it for in-game verification at disc scale.)
- **`nomap` independence (AT — design §2 / §7):** the disc is our own uGUI overlay on our own
  Canvas, exactly like the full view. It does **not** touch `Minimap.m_smallRoot` /
  `m_largeRoot`, which `nomap`/`MapMode.None` forces off (`Minimap.SetMapMode :975`,
  `if (Game.m_noMap) mode = None`). So the disc renders while vanilla's roots stay suppressed —
  the same standalone-overlay guarantee the full view already relies on. **Do NOT re-enable
  vanilla's roots** (that would fight `nomap` and is explicitly out of scope, design §6/out-of-scope).

### 4.3 Teardown — disc off on unbind

When the provider clears (§3.4) or goes blank (§3.5), call `UnbindMinimap()` → the viewer
`SetActive(false)`s `_minimapRoot` (mirror the full view's `Close` = deactivate-root idiom,
`MapViewer.cs:181-184`, so disc open-state is derived from the live root and cannot latch).

### 4.4 Controller wiring — drive the disc from provider state

Fold the §3 state machine into the existing throttled poll block (`LocalMapController.Update`,
`:123-167`). Per poll, after resolving `_provider`:

- **Provider bound + imprinted →** build the `MapViewRequest` (survey/origin/radius) and call
  `CartographyViewer.BindMinimap(req)` (shows on first call, refreshes thereafter — keeps the
  heading + player marker live at the 0.25 s cadence; rotation itself runs at frame rate inside
  the viewer's `Update`, unchanged).
- **No provider, or provider blank →** `CartographyViewer.UnbindMinimap()`.
- **Replace** the old carry-transition log-only branch (`:137-144`) with this bind/unbind drive.
  Keep a single info log on the **bind** and **unbind** *transitions* (not every poll) for
  diagnosability, matching the existing transition-logging style.
- **Full view + disc coexistence:** the disc is independent of the full-view open/close. Equipping
  still opens the full view on the M input (orthogonal card); the disc remains bound underneath.
  Do not close the disc when the full view opens — the full view's own higher `sortingOrder` covers
  it, and it reappears when the full view closes.

---

## 5. nomap-OFF interaction (design §6 — the disc must NOT take over the minimap)

Design §6 is explicit and 🟢: **in nomap-OFF the always-on minimap stays bound to the player's
GLOBAL map.** A local map equipped in nomap-off still survey-writes to both and is still M-openable
as its own artifact, but it **does not take over the minimap.** The local-map disc binding to the
minimap is a **nomap-ON-only** behaviour (in nomap-on there is no global minimap, so the local map
*is* the minimap, §2).

**Implication for this card:** the disc must only bind when `nomap` is **on**. Gate the
`BindMinimap` drive on the world's nomap state:

- The mod enforces `GlobalKeys.NoMap` server-side by default (`NoMapEnforcer.cs`), so in the normal
  Trailborne world nomap is ON and the disc binds as specified. But the gate must be **explicit**,
  not assumed — a server may run `SBPR_EnforceNoMap=false` (the escape hatch,
  `NoMapEnforcer.ShouldEnforceNoMap`), and design §6 requires the disc to stand down there.
- **Read the live nomap state, client-side:** `Game.m_noMap` is the per-client effective flag
  (`Game.UpdateNoMap :85133` sets it from `ZoneSystem.GetGlobalKey(NoMap) || the client pref`).
  The controller runs client-side, so **gate the bind on `Game.m_noMap` being true** (re-checked on
  the poll — it can change on a global-key broadcast). When `Game.m_noMap` is false (nomap-off),
  **never `BindMinimap`** regardless of provider state; the vanilla minimap owns that corner.
- This is purely additive to §4.4: the bind condition becomes "provider bound + imprinted **+
  `Game.m_noMap`**". Provider tracking (§3) still runs in both modes (the provider is also what the
  full view and the §5-cartography dual-write key on), only the **minimap render** is nomap-on-gated.

> **Why gate render, not provider:** the provider identity is also consumed by the equipped
> full-view path and (future) the Cartographer's-tools dual-write (design §5/§6), which are
> mode-independent. Only the *minimap disc* is nomap-on-only. Keep the provider state machine
> always-on and gate the single `BindMinimap` call.

---

## 6. 🟡 OPEN — R1: disc centring (THE question for Daniel at review)

**This is the one decision this spec deliberately does not make.** The design doc contradicts
itself, and the wrong pick re-ships a rejected behaviour.

**The contradiction, verbatim:**
- Design **§7**: "The viewer renders the bound LOCAL map's data (§2), grid-aligned to the global
  fog (§4.1), **player-centered**, rotate-to-heading, circular bezel. **All the §2H.1 mechanics
  stand.**"
- §2H.1 **mechanic 1** (cartography-impl-spec, Daniel-locked card t_05e702ee): "**TABLE-centred,
  fixed window — NO pan.** The 1000 m window is static… does not slide to follow the player."

"player-centered" and "all §2H.1 mechanics stand" are mutually exclusive. Worse: the player-centred
variant (old §2H) **was built in v0.2.22 and Daniel rejected it** (the §2H.1 re-lock supersedes it
precisely *because* he wanted table-centred).

**Why it actually matters at minimap scale (the part that makes this non-cosmetic):** a minimap is
conventionally **player-centred** — you, in the middle, world scrolling under you. The full-view
table-centred model exists because the full view is a *paper map nailed to the table* you read
deliberately. A **glanceable HUD disc** bound to a 1000 m survey has a real design fork:
- **(A) Table-centred (reuse §2H.1 as-is):** the disc shows the survey fixed on the table origin;
  the player marker **orbits** as they move/turn. Cheapest (the §2H.1 mechanics are literally
  unchanged — zero new centring code), and *internally consistent* with the full view. But a
  table-centred "minimap" where you are a moving dot near the edge is unusual and, far from the
  table, you fall off the disc into the edge-arrow state (§2H.1 mechanic 2) — a minimap that often
  shows just an arrow is a weak minimap.
- **(B) Player-centred (the §7 literal word):** the disc keeps you in the middle, the survey
  scrolls under you, still clipped to the 1000 m bound (beyond it = shroud). Reads like a real
  minimap. But it **breaks "all §2H.1 mechanics stand,"** needs the player-centring offset the
  §2H.1 re-lock deleted, and resurrects code Daniel removed — for the disc only, while the full view
  stays table-centred (two centring models in one viewer).

**Architect recommendation (reversible, NOT stamped):** **(A) table-centred for v1 of the disc.**
Rationale: it is the literal "reuse the §2H.1 viewer as a scaled instance" the design + peer
comments demand, it ships zero new centring code, and it keeps ONE centring model across both
surfaces (less risk, less drift). The "player-centered" word in §7 reads to me as loose phrasing
inside a sentence that simultaneously insists the §2H.1 mechanics stand — i.e. §7 is describing
"the §2H.1 circular viewer" and reached for "player-centered" imprecisely, not re-litigating the
table-vs-player lock he just set. **But this is Daniel's call, not mine** — he explicitly said the
disc feeds "the circular viewer at minimap size," and if he wants a *true* player-centred minimap
(B), that is a legitimate, larger build (new centring path, two models) and he should say so before
the engineer-ui worker starts. **Block routes this to him; the AT for centring (§7 AT-PROV-DISC-2)
is written mode-agnostic so it passes under whichever he picks.**

> Do not let the build start until R1 is resolved — it changes the viewer-side estimate materially
> (A ≈ reuse; B ≈ new centring apparatus). The child build card (§9) names R1 as a gating input.

---

## 7. Acceptance tests (named, observable — close only on Daniel's in-game check)

Each maps to one of the card's 7 acceptance criteria. `logs-green ≠ playable` — every AT below
is a **pixel/behaviour** check on a GPU client (the headless build worker cannot verify render;
build 0/0 is necessary, not sufficient).

- **AT-PROV-DISC-1 (card AC1+AC2)** — carrying (in inventory, **unequipped**) the most-recently-
  equipped, imprinted Local Map renders a **circular minimap disc** on screen, bound to **that
  map's 1000 m survey** (the disc's fog matches that table's imprint). Equip a map, unequip it
  (keep it) → the disc appears and shows that survey.
- **AT-PROV-DISC-2 (card AC3, mode-agnostic per R1)** — the disc is **circular** and
  **free-rotates to player heading**, with **no north indicator / compass / north-up mode** of any
  kind. (Centring — table vs player — is whichever R1 resolves; this AT does not assert centring.)
- **AT-PROV-MOSTRECENT (card AC4)** — carry **two** imprinted Local Maps. Equip A, unequip →
  disc shows A. Equip B, unequip → disc shows **B** (most-recently-equipped wins). Equip A again,
  unequip → disc shows **A**. The disc never shows an arbitrary "first in inventory" map.
- **AT-PROV-EQUIP-FULLVIEW (card AC5)** — equipping a Local Map still opens the **full-screen
  bounded viewer** (on the M input); **merely carrying** an unequipped map shows **only the disc**,
  never the full view. The two surfaces are distinct.
- **AT-PROV-NOMAP-INTACT (card AC6)** — the disc renders and stays visible **while vanilla's
  small/large map roots are suppressed** under `nomap`/`MapMode.None`. Vanilla's minimap circle does
  NOT appear; only our disc does. (Confirms the disc rides our own overlay, not `m_smallRoot`.)
- **AT-PROV-BEZEL-MATCH (card AC7)** — the disc's **bezel and rotate behaviour match the full
  view**: same circular bezel ring, same rotate-to-heading sense, **same clean edge** (inherits
  #159 — no parchment bleed past the bezel at disc scale). Turning the player rotates the disc
  interior; the frame/bezel does not spin.
- **AT-PROV-UNBIND** — **drop** the provider map → the disc disappears (provider unbound). **Trade**
  it away → disc disappears. **Die** (provider goes to tombstone) → disc disappears within a poll
  tick. Re-pick-up without equipping → no disc until re-equipped.
- **AT-PROV-NOMAPOFF (design §6)** — on a `nomap=OFF` world, carrying/equipping a Local Map does
  **NOT** bind the disc to the minimap; the vanilla global minimap stays in the corner. (The local
  map is still full-view-openable as its own artifact.)
- **AT-PROV-BLANK** — a **blank** (un-imprinted) carried/equipped map shows **no disc** (and no
  error); imprinting it at a Table makes the disc appear on the next poll.
- logs-green ≠ playable — Daniel confirms AT-PROV-* in-game.

---

## 8. Files touched + clean-side

**Lands in** (all under `src/SBPR.Trailborne/Features/Cartography/`):
- **`LocalMapController.cs`** — the provider state machine (§3): add `_provider`, replace the
  `GetCarriedLocalMap` first-carried selection + the log-only carry branch with bind/unbind drive
  (§3.2–3.4, §4.4), add the `Game.m_noMap` gate (§5). `GetCarriedLocalMap` (`:330`) is **retired**
  (its "first carried" semantics are wrong for the provider model); `GetEquippedLocalMap` (`:320`)
  stays.
- **`MapViewer.cs`** — the second `_minimapRoot` disc surface (§4.2): factor the §2H.1 layer-tree
  builder so both the modal and the disc share it at different sizes; parameterize the #159 clip by
  fraction-of-radius (§4.2 caveat). **Do NOT fork a second circular renderer.**
- **`CartographyViewer.cs`** — the minimap channel on the seam (§4.1): `BindMinimap` / `UnbindMinimap`
  / `IsMinimapBound`, degrading gracefully when no viewer is registered (mirror the existing
  `Open` graceful-fallback).
- **`MapViewRequest`** (in `CartographyViewer.cs`) — add the minimap flag (`bool Minimap` or a third
  `MapViewerMode.MinimapDisc`).

**Docs that move with the CODE in the implementation PR (spec-first — NOT this spec-pass PR):**
a cross-ref banner in `cartography-impl-spec.md` §2A.4 (the carry path now **renders** a disc,
not just logs) and §2H.1 (the viewer is now instanced at two scales). These describe behaviour
the build introduces, so they land in the **child build card's** PR alongside the code — adding
them now (while the code still only logs) would make the spec lie. **This spec-pass PR adds only
this new doc + the index/README registration.** **SpecCheck: untouched** (§0).

**Clean-side (ADR-0001):** everything read here is base-game — `Player`/`Humanoid` inventory
(`GetEquippedItems`/`GetAllItems`/`ContainsItem`), `Game.m_noMap`, `Minimap` roots, camera yaw — all
fair to read + adapt. The disc UI is our own uGUI (the SignPaintPanel / §2H.1 idiom this repo
already ships). No vanilla UI prefab cloned (ADR-0006 N/A — this is uGUI construction, no
ZNetView-bearing prefab). No other mod's code touched.

---

## 9. Build routing

- **One `engineer-ui` worker** owns the whole change (`LocalMapController` + `MapViewer` +
  `CartographyViewer` move together — they are one feature and would collide if split). File it as a
  **child of this card** (`t_1d1b505b`) AND **`t_d44572f2`** (PR #159), so the dependency graph
  forces sequencing.
- **Gating inputs (must be resolved before the worker starts):**
  1. **R1 (§6) — centring** resolved by Daniel. The viewer-side estimate depends on it (A ≈ reuse;
     B ≈ new centring apparatus).
  2. **PR #159 merged** — the disc reuses the post-#159 bezel clip. Building before #159 lands means
     reusing the about-to-be-replaced bezel and re-shipping edge-bleed at disc scale.
- **R2 (§1 / storage)** is **not** gating — the disc reads a `SurveyData` snapshot via
  `LocalMap.ReadSurvey` regardless of the storage pick. Note it as a soft dependency only.
- **Build gate:** `dotnet build -c Release` → **0 warn / 0 err** (`TreatWarningsAsErrors` on).
  **Daniel gates the merge; the worker NEVER self-merges** — it ends in
  `kanban_block(reason="review-required: …")` with the PR URL, structured handoff in a comment.
