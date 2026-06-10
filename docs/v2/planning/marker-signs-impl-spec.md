---
title: "Marker Signs + WorldPin — buildable implementation spec"
status: current
purpose: "Per-feature, build-ready implementation spec for the v2 Marker Signs feature and the WorldPin substrate. Gives observable acceptance criteria, the exact vanilla decomp hooks, the feature-folder placement, and the SpecCheck manifest rows. Implementers (engineer-systems / engineer-ui) build from THIS doc; the design lock docs/design/marker-signs-worldpin.md is the why, this is the how-to-pick-it-up-cold. Authored by the architect spec-pass (card t_5fb8703d)."
---

# Marker Signs + WorldPin — buildable implementation spec

The design lock ([`../../design/marker-signs-worldpin.md`](../../design/marker-signs-worldpin.md))
is the *why* + the ratified architecture. This doc is the buildable *how*: one tight
section per concern an implementer can pick up cold, with the vanilla decomp hooks,
observable acceptance criteria, feature-folder placement, and SpecCheck impact.

> **Clean-side note (ADR-0001):** every vanilla decomp line cited here is the base
> game (`assembly_valheim`), which is fair game to read and adapt. Do **not** read or
> copy third-party pin mods — the design is fully grounded in vanilla primitives.
>
> **Decomp line refs:** `~/valheim/worldgen-spike/decomp/assembly_valheim.decompiled.cs`,
> re-verified by the architect 2026-06-10. They are stable anchors but **re-grep to
> confirm** before relying on an exact line (the AGENTS.md retrieve-before-claim rule).

## 0. SpecCheck manifest impact (read first — it moves with the code)

`Runtime/SpecCheck.cs` holds the recipe drift manifest. This feature adds **+4 build
piece entries** (all new, all build pieces — no item recipes):

| # | Manifest entry | Kind | Resources | Station |
|---|---|---|---|---|
| 1 | `piece_sbpr_marker_poi` | build piece | Wood ×2 | (Spade menu; `m_craftingStation = null`) |
| 2 | `piece_sbpr_marker_mining` | build piece | Wood ×2 | (Spade menu; `m_craftingStation = null`) |
| 3 | `piece_sbpr_marker_shelter` | build piece | Wood ×2 | (Spade menu; `m_craftingStation = null`) |
| 4 | `piece_sbpr_marker_portal` | build piece | Wood ×2 | (Spade menu; `m_craftingStation = null`) |

- **Build cost = Wood ×2**, matching the existing Painted Sign (`SpecCheck.cs:57-59`,
  `Signs.WoodCost`). Marker signs are the same fieldcraft tier as the plain sign;
  the *map pin* is the value-add, not a costlier recipe. (If Daniel wants them
  costlier, that is a one-line manifest + recipe change — flag it, don't assume.)
- **Resource prefab-name caveat:** `Wood` is the vanilla internal id. SpecCheck flags
  a NULL `m_resItem` if the name is wrong; reuse `Signs.WoodCost` / the existing
  `BuildReq("Wood", …)` call shape (`Assets.cs:365`).
- **Manifest shape gotcha:** `SpecCheck.Run()` iterates `Manifest.Where(s => s.Piece
  != null)` for build pieces. These four are `Piece` only (`Item = null`). Match that
  shape — a `RecipeSpec` with both null or both set won't be checked
  (`cartography-impl-spec.md §0` flagged the same trap).
- The card that touches `SpecCheck.cs` first should also extend the class's
  `LOCKED SOURCE` comment to cite `docs/design/marker-signs-worldpin.md` +
  `docs/v2/planning/marker-signs-impl-spec.md` alongside the v0.1.0 source.

> **Generation tip:** the four markers are 4× repetitive — mirror the existing cairn
> pattern in `SpecCheck.cs:140-181` (it generates the per-color cairn checks in a
> `foreach` rather than enumerating). A `foreach (var m in MarkerSigns.MarkerTypes)`
> keeps the manifest DRY.

## 1. The four Marker Sign pieces — additive construction

> **STATUS (M1 IMPLEMENTED, card t_0c7b782d):** the four additive pieces +
> `MarkerSignTag` + spade wiring + SpecCheck +4 rows are built and compile 0/0. The
> WearNTear destroy hook is subscribed (its WorldPin-unpin callback is an inert,
> logged stub until the §3 engine lands). The §1.4 panel-icon and the §4 Shift+E
> gesture are the gated follow-up milestones — see the card's review-required handoff.
> **logs-green ≠ playable**: AT-MARK-1 / AT-PIN-ADR0006 close only on Daniel's in-game check.

**Lands in:** `Features/MarkerSigns/MarkerSigns.cs` (registration + the type table) and
`Features/MarkerSigns/MarkerSignTag.cs` (the per-instance MonoBehaviour). New feature
folder `Features/MarkerSigns/` (mirror the vertical-slice layout of
`Features/Signs/`, `Features/Cairns/`).
**Card:** `t_<markersigns-pieces>` (engineer-systems).

### 1.1 The type table

Define the four marker types once, data-driven (so registration, SpecCheck, and the
panel all read one source — the cairn `Colors`/`MarkerName` pattern in `Cairns.cs`):

```
MarkerType { key, prefabName, niceName, pinLabel, iconFile, vanillaPinType }
  poi      → piece_sbpr_marker_poi      "Marker: Point of Interest" "Point of Interest" marker_poi_v0.1.png      Icon0
  mining   → piece_sbpr_marker_mining   "Marker: Mining"            "Mining"            marker_mining_v0.1.png   Icon0
  shelter  → piece_sbpr_marker_shelter  "Marker: Shelter"           "Shelter"          marker_shelter_v0.1.png  Icon0
  portal   → piece_sbpr_marker_portal   "Marker: Portal"            "Portal"           marker_portal_v0.1.png   Icon0
```

- `vanillaPinType` is the **base `Minimap.PinType` we pass to `AddPin`** so the pin is
  a valid, filter-toggleable vanilla pin; the **custom sprite is overridden onto
  `m_icon` after AddPin** (§3). `Icon0` is a fine neutral base for all four — the
  player never sees the base sprite because we override it every projection. (Do NOT
  reuse `Death`/`Boss`/`Player` types — those have special vanilla culling at
  `UpdatePins` :47808.)
- `iconFile` PNGs ship in `assets/icons/items/` (packer copies all `*.png` beside the
  DLL — `scripts/pack-modpack.sh:99-104`) and load via `Assets.LoadPngAsSprite`
  (`Assets.cs:14`). **First cut: these are placeholder art** (POI=magnifying glass,
  mining=pickaxe, shelter=tent, portal=circle per Q5). The art can be regenerated
  later without touching code (same filename).

### 1.2 Additive construction (ADR-0006 — hard constraint, AT-PIN-ADR0006)

**Do NOT `ClonePrefab("sign")` like the existing Painted Sign does** — that predates
ADR-0006. Build each marker piece additively:

- `new GameObject(prefabName)` parented under the inactive `PrefabHolder` (so no
  `Awake` fires during construction — `Assets.cs:42 GetHolder` pattern), then
  `AddComponent` of exactly: `Piece`, `WearNTear`, `ZNetView`, `Sign` (so the
  existing `SignInteractPatch` intercepts it and so the panel/label reuse works), and
  the custom `MarkerSignTag`.
- **Read the vanilla `sign` prefab as a BLUEPRINT only** (`ZNetScene.GetPrefab("sign")`
  fires no Awake — `Assets.cs` blueprint doctrine) for its mesh/material/`Sign`
  field values / `EffectList`s, and **reference-copy** those onto the constructed
  object. Use `vprefab inspect sign` to read the donor's real structure first.
- Set `ZNetView` fields explicitly: `m_persistent = true` (REQUIRED — the WorldPin
  durability rests on the sign's ZDO being persistent, design §3), `m_type` and
  `m_distant` per the vanilla sign blueprint. ADR-0006 confirms an `AddComponent`'d
  ZNetView with these fields set + a registered prefab name is a fully valid
  networked object (`docs/decisions/0006-additive-prefab-construction.md`).
- The visual: reuse the standing-pole + board kitbash the Painted Sign already builds
  (`Signs.cs KitbashStandingPole`), or a simpler board — the silhouette is a v0.2+
  polish call. The load-bearing requirement is a placeable, interactable sign body
  that carries a `Sign` (for the panel) + a `ZNetView` (for the ZDO).

> **Why keep a `Sign` component?** The existing `SignInteractPatch` keys on
> `Sign.Interact`, and the panel + text label reuse the vanilla `Sign` text widget.
> Putting `Sign` on the marker piece lets us reuse the whole Painted-Sign interaction
> stack (panel, two-tone tint, text) for free, and only ADD the Shift+E branch (§4).

### 1.3 Registration + Spade menu wiring

- Register the four prefabs in `MarkerSigns.RegisterPrefabs(zns)` (called from
  `Registrar.OnZNetSceneAwake` — add the dispatch line after `Signs.RegisterPrefabs`,
  `Registrar.cs:62`). Each is registered into ZNetScene by name via
  `Assets.RegisterPrefabInZNetScene` (`Assets.cs:264`).
- Add the four pieces to the **Spade** PieceTable, NOT the Hammer (Pillar 1). The
  spade table is built in `Trailblazing.DoObjectDBWiring` (`Trailblazing.cs:430-435`
  adds Sign / Path Lamp / Cairns via `AddSpadePieceByName`). Add four
  `AddSpadePieceByName(zns, table, MarkerSigns.PrefabName(type))` lines there, after
  the cairns. **Race-safety:** `Registrar` runs all `RegisterPrefabs` before any
  `DoObjectDBWiring` and dispatches Trailblazing AFTER the feature modules, so the
  marker prefabs resolve by name there — same guarantee the Sign/Cairn wiring relies
  on. Every piece must be `Piece.PieceCategory.Misc` to render in the single 'Trail'
  tab (`Trailblazing.cs:423` + the `EnsureCategory` self-heal guard).
- `piece.m_craftingStation = null` (no bench to place — every Spade-placed SBPR piece
  does this: `Signs.cs:270`, `Trailhead.cs:186`).
- `MarkerSigns.DoObjectDBWiring(zns)` rebuilds `piece.m_resources = { BuildReq("Wood",
  2) }` (the ODB-phase authoritative pass, mirroring `Signs.cs:303-328`), then add the
  `MarkerSigns.DoObjectDBWiring` dispatch line to `Registrar.cs` after
  `Signs.DoObjectDBWiring` (`Registrar.cs:116`).

### 1.4 The panel-icon-for-reference (AT-MARK-1)

Daniel: "add the icon into the UI for reference." The marker's icon sprite shows in
the paint/text panel when you interact (primary E) with a marker sign, so the player
sees which marker type this is. Reuse the existing `SignPaintPanel` (`Features/Signs/
SignPaintPanel.cs`) and add a single `Image` element bound to
`MarkerSignTag.MarkerIcon`. **First cut: piece build-icon art = the marker icon art**
(Daniel: "for now, just make the piece art the icon art") — set `piece.m_icon =
LoadPngAsSprite(iconFile)`. The "icon overlaid bottom-right on the piece art" is v2.1
polish, explicitly out of scope.

### 1.5 Acceptance criteria
- **AT-MARK-1** — four Marker Sign entries on the Spade 'Trail' tab; each places like
  the existing sign; interacting (primary E) shows the marker's icon in the panel.
- **AT-PIN-ADR0006** — the four pieces are constructed additively; a regression guard
  asserts no marker prefab is a runtime clone of a ZNetView-bearing prefab.
- SpecCheck rows 1–4 present; `[hold]` PR; logs-green ≠ playable.

## 2. `MarkerSignTag` — per-instance ZDO state (mirrors `SignTag`)

**Lands in:** `Features/MarkerSigns/MarkerSignTag.cs`. Mirrors the ZDO-backed
per-instance pattern of `Features/Signs/SignTag.cs` (owner-write via `ZNetView`,
re-apply on spawn).

### 2.1 ZDO fields

| ZDO key | Type | Meaning |
|---|---|---|
| `SBPR_MarkerType` | string | the marker type key (`poi`/`mining`/`shelter`/`portal`). Usually derivable from the prefab name, but stored so a scan can read it off the ZDO without a prefab→type map. |
| `SBPR_Pinned` | bool (int 0/1) | is this marker currently pinned on the placer's map? Toggled by Shift+E. |
| `SBPR_PinIconColor` | string | **RESERVED, unused in first cut** (Q1 defers color). Empty = default. Reserve the key now so the fast-follow doesn't need a ZDO migration. |
| `SBPR_PinTextColor` | string | **RESERVED, unused in first cut.** Same. |

- Owner-write pattern: `if (!nview.IsOwner()) nview.ClaimOwnership(); nview.GetZDO()
  .Set(key, value);` — copy `SignTag.WriteColors` (`SignTag.cs:97-105`) verbatim in
  shape. Guard every read/write on `nview != null && nview.GetZDO() != null` (ghost
  has no ZDO — `SignTag.cs:66` shows the ghost gate).
- `MarkerType` is set once at placement (from the prefab the player built). `Pinned`
  defaults false (a freshly built marker sign is NOT pinned until the player presses
  Shift+E — confirm with Daniel if he'd rather auto-pin on place; the card's wording
  "Shift+E should pin or unpin" implies explicit, so default false).

### 2.2 Lifecycle hooks on the tag

- **`Awake`** — cache `nview`, read `MarkerType`, subscribe the destroy hook (§4.2),
  and if `Pinned` is true on a client that owns/sees this zone, ensure the WorldPin is
  projected (the reconcile pass §3 is the authority; Awake just nudges it).
- **`OnDestroy` is NOT the hook** — Unity's `OnDestroy` fires on every zone unload too
  (not just true destruction), which would wrongly unpin a sign whose zone merely
  unloaded. Use `WearNTear.m_onDestroyed` instead (§4.2) — it fires only on **real**
  destruction (decay/raid/demolish), not on unload. **This distinction is
  load-bearing: getting it wrong reintroduces the exact stale/flicker bug the design
  avoids.**

## 3. The WorldPin projection + render (the substrate)

**Lands in:** `Features/MarkerSigns/WorldPins.cs` (the projection/reconcile engine) —
or, if the cartography cards land first, **this is shared code** that should live
where the cartography viewer can consume it (coordinate placement with cards
`t_38f9c77a` / `t_7b616020`; see design §5 "do not fork"). Default home:
`Features/MarkerSigns/WorldPins.cs` with a public surface the cartography viewer calls.

### 3.1 The projection primitive (render a WorldPin onto the vanilla minimap)

For each WorldPin to show:

```
var pin = Minimap.instance.AddPin(worldPos, basePinType, label, save: false, isChecked: false);
pin.m_icon = markerSprite;   // custom sprite override — STABLE per design §V1
```

- **`save: false` is mandatory** (`AddPin` signature :48466). It keeps vanilla from
  ever persisting our pin to the player profile — which is what makes AT-PIN-PERSIST
  and AT-PIN-DESTROY-DURABLE pass by construction (nothing of ours is stored
  client-side, so nothing stale can survive).
- **The `m_icon` override is stable** — `UpdatePins` (:47786) only reads `pin.m_icon`
  when it (re)creates the marker GameObject (`pin.m_uiElement == null` branch :47812 →
  :47816), and does NOT re-derive it from the type. So set it once after AddPin; no
  per-rebuild re-skin postfix needed for the icon (design §V1 — this is the
  correction to the originating card's framing).
- Keep a client-local map `{signZDOID → projected PinData}` so reconcile can diff
  (which pins to add, which to `RemovePin`). This map is **transient** (rebuilt from
  the live set each reconcile); it is NOT persisted.

### 3.2 The reconcile pass (derive-by-scan — design §4.2)

On each reconcile trigger (§3.3), rebuild the visible WorldPin set from the **live**
marker-sign ZDOs and diff against the client-local projected map:

- **Source of truth = live ZDOs.** Enumerate marker-sign ZDOs via
  `ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefabName, list, ref index)`
  (:65497) for each of the four marker prefabs — call it iteratively until it returns
  `true` (it batches 400 sectors/call; vanilla drives it the same way at :37512). On
  a **dedicated server** this enumerates the complete persistent set (design §3); a
  **client** sees its loaded zones, so for the field-disc projection the client should
  ask the **server** for the authoritative set inside the bound disc (a small routed
  RPC, or piggyback on the cartography Table's existing data channel — coordinate
  with `t_38f9c77a`). For a **local/singleplayer** host, client == server and the scan
  is authoritative directly.
- For each live ZDO read: `pos = zdo.GetPosition()` (:62297), `markerType` (from
  `SBPR_MarkerType` or the prefab), `pinned = zdo.GetBool(SBPR_Pinned)`. Project only
  the ones with `pinned == true` AND inside the bound 1000 m disc (design §4.3 / the
  cartography 1000 m bound).
- **Diff:** add pins newly present, `Minimap.RemovePin(PinData)` (:48408) pins whose
  signZDOID is no longer in the live set (the stale-pin drop — this is where a
  destroyed/offline sign's pin disappears, AT-PIN-DESTROY-DURABLE).
- **Stale-free by construction:** a destroyed sign's ZDO is gone from `m_objectsByID`
  (:65051) so it never appears in the scan — there is no dangling reference to clean
  up (design §4.2).

### 3.3 Reconcile triggers (design §4.4)

- **Map open** — full reconcile (load-bearing; the returning-offline-owner case).
  Hook the minimap mode change / map-open (the cartography viewer card owns the exact
  hook; for the standalone Marker-Sign MVP, a `Minimap.SetMapMode` postfix or the
  viewer's open event).
- **Light periodic tick while the map is open** — catch other clients' Shift+E /
  destroy without reopening (a few seconds; playtest-tuned).
- **Login / zone-enter** — opportunistic refresh.

### 3.4 Acceptance criteria
- **AT-MARK-2** — Shift+E adds a pin with the marker's CUSTOM sprite (not a stock
  vanilla pin); Shift+E again removes it; hover/legend reads the marker type.
- **AT-PIN-PERSIST** — the custom icon survives map close/reopen, server restart, and
  fresh client join (never reverts to a stock pin) — because each is a re-skinned
  `save:false` projection, never a persisted vanilla pin.
- `[hold]` PR; logs-green ≠ playable.

## 4. Shift+E gesture + destroy hook (the wiring)

### 4.1 Wire Shift+E into the existing `SignInteractPatch`

The existing prefix `Features/Signs/SignInteractPatch.cs` already intercepts
`Sign.Interact` for SBPR signs. Extend it (or add a sibling marker-specific patch —
implementer's call; extending is fewer moving parts) so:

- `hold == true` → fall through to vanilla (existing behavior, `SignInteractPatch.cs:31`).
- `alt == false` (primary E) → open the paint/text panel (existing behavior). For a
  marker sign the panel ALSO shows the marker icon (§1.4).
- **`alt == true` (Shift+E)** → **NEW**: if the sign carries a `MarkerSignTag`, toggle
  its `SBPR_Pinned` ZDO bool (owner-write) and immediately project/`RemovePin` the
  WorldPin locally (the fast path). Set `__result = true; return false;` to consume
  the interaction. If the sign is a plain Painted Sign (no `MarkerSignTag`), Shift+E
  falls through to vanilla (the plain sign has no pin gesture).
- **Grounding:** `alt` is the AltPlace/Shift key fed through `Player.Interact`
  (`ZInput.GetButton("AltPlace")` → `alt` :16115 → `Interact(go, hold, alt)` :19270 →
  `componentInParent.Interact(this, hold, alt)` :19280). No new input code.

> The current patch comment (`SignInteractPatch.cs:22`) says the Shift+E gesture is
> intentionally not wired — **update that comment** when wiring it, and update
> `requirements.md:603` (the spec-and-code-together rule: the deferred-gesture note
> becomes a done note).

### 4.2 The destroy hook — subscribe to `WearNTear.m_onDestroyed`

In `MarkerSignTag.Awake` (or a small dedicated component), subscribe to the sign's
`WearNTear.m_onDestroyed`:

```
var wnt = GetComponent<WearNTear>();
if (wnt != null) wnt.m_onDestroyed += OnSignDestroyed;
```

- `m_onDestroyed` is a **public `Action` field** on `WearNTear` (:128027), invoked
  **owner-side inside `WearNTear.Destroy()`** (:129051), which is reached only on real
  destruction (`RPC_Remove` → `if (m_nview.IsOwner()) Destroy()` :129025-129031, and
  the decay/hit paths). **It is a public subscribe seam — NO Harmony patch needed.**
- `OnSignDestroyed` clears the WorldPin for this signZDOID locally (fast path) and,
  for any client that has the zone loaded, the projected pin drops. The durable
  offline case is handled by the reconcile (§3.2) — this hook is just the prompt
  online path (AT-PIN-DESTROY-LOADED).
- **Do NOT use Unity `OnDestroy`** for unpin — it fires on zone unload too (§2.2),
  which would unpin a merely-unloaded sign. `m_onDestroyed` fires only on real
  destruction. This is the single most important wiring detail in the feature.
- Unsubscribe in `OnDestroy` to avoid a dangling delegate on the pooled component.

### 4.3 Acceptance criteria
- **AT-PIN-DESTROY-LOADED** — destroy a pinned marker sign in a loaded zone → its pin
  is gone promptly (the `m_onDestroyed` fast path).
- **AT-PIN-DESTROY-DURABLE** — destroy a pinned marker sign while its zone is unloaded
  for the owner / the owner is offline (decay/raid) → no stale pin; gone on the
  owner's next map open (the reconcile §3.2 backstop). **The load-bearing test.**

## 5. Build order, cross-feature seams, and the SpecCheck rule

- **Build order (lowest → highest risk):**
  1. The four pieces + `MarkerSignTag` + Spade wiring + SpecCheck rows (§1–§2) — a
     self-contained "buildable, interactable marker signs" milestone, testable
     before any pin code (AT-MARK-1, AT-PIN-ADR0006).
  2. The WorldPin projection + reconcile engine (§3) — the substrate.
  3. The Shift+E gesture + destroy hook (§4) — wires (1) and (2) together
     (AT-MARK-2, AT-PIN-DESTROY-*).
- **The WorldPin engine (§3) is the seam shared with the v2 cartography viewer**
  (`cartography-impl-spec.md §2B`, cards `t_38f9c77a` / `t_7b616020`). **One pin
  model, built once** (design §5). If a cartography card is in flight, coordinate the
  home of `WorldPins.cs` so the Local Map viewer and the Surveyor's Table consume the
  same projection/reconcile surface rather than forking a second pin notion. The
  Surveyor's Table's "pin removal" (cartography D4) operates on this same WorldPin
  set.
- **All clean-side / ADR-0006:** additive construction, vanilla read as blueprint, no
  third-party pin-mod source read, no decompiled IronGate source committed.
- **Spec-first:** each impl card moves its `SpecCheck.cs` manifest rows + its code +
  the relevant spec cross-check in the same PR. A card is done when code, spec, and
  the SpecCheck manifest agree — and (logs-green ≠ playable) only when Daniel verifies
  it in-game.

## 6. Naming reference (prefab / ZDO-key strings — agree before building)

| Thing | Name (lock in the first PR that registers it) | Type |
|---|---|---|
| POI marker | `piece_sbpr_marker_poi` | build piece |
| Mining marker | `piece_sbpr_marker_mining` | build piece |
| Shelter marker | `piece_sbpr_marker_shelter` | build piece |
| Portal marker | `piece_sbpr_marker_portal` | build piece |
| Marker-type ZDO field | `SBPR_MarkerType` | ZDO string |
| Pinned-state ZDO field | `SBPR_Pinned` | ZDO bool |
| Pin icon-color (reserved) | `SBPR_PinIconColor` | ZDO string (unused first cut) |
| Pin text-color (reserved) | `SBPR_PinTextColor` | ZDO string (unused first cut) |
| Marker sprites | `assets/icons/items/marker_{poi,mining,shelter,portal}_v0.1.png` | PNG |

> Prefab-name + ZDO-key strings are **save/wire contracts** the moment a piece is
> placed in a live world. Lock them in the first impl PR and never rename
> (renaming orphans every placed instance — the same reason `Pigments` kept
> `SBPR_Ink*`). The `_v0.1` suffix on sprite filenames matches the existing
> convention (`ink_red_v0.1.png`, `cairn_marker_v0.1.png`) so art can be revved
> without a code change at the same filename.
