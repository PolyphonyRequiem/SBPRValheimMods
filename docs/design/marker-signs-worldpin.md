---
title: "Marker Signs + the SBPR WorldPin system (durable, ZDO-anchored map pins)"
status: current
purpose: "Design lock for the v2 Marker Signs feature and the WorldPin substrate it introduces — four buildable marker-sign pieces that pin/unpin themselves on the player's map via Shift+E, with custom marker icons, and a durable destroy-safe unpin that survives the sign being destroyed while its zone is unloaded or the placer is offline. Decomp-grounded; Daniel's 5 scope calls (Q1-Q5) are LOCKED 2026-06-10. The buildable how-to lives in docs/v2/planning/marker-signs-impl-spec.md."
---

# Marker Signs + the SBPR WorldPin system

> **Status: LOCKED** (architect spec-pass, card `t_5fb8703d`, 2026-06-10). Daniel's
> scope calls Q1–Q5 are ratified (see §1). This doc carries the *why*, the
> decomp-grounded viability findings, and the architecture decision. The
> build-ready *how* — per-feature acceptance criteria, exact vanilla hooks,
> feature-folder placement, SpecCheck rows — lives in
> [`../v2/planning/marker-signs-impl-spec.md`](../v2/planning/marker-signs-impl-spec.md).

## 0. Where this sits

- **Tier:** Black Forest (v2). Marker Signs are the **pin substrate the v2
  cartography tier consumes** — the Surveyor's Table edits WorldPins and the Local
  Map renders the WorldPins inside its 1000 m disc. They ship **with/before** the
  Local Map viewer so the two share **one** pin notion. Locked by Daniel (Q2).
- **What it is in one line:** four buildable **Marker Sign** pieces (POI, mining,
  shelter, portal) that reuse the existing Painted Sign UX, show the marker's icon
  in the panel for reference, and on **Shift+E** pin/unpin themselves on the
  player's map with a **custom marker icon** — and the pin **disappears when the
  sign is destroyed, durably**, including when the sign is destroyed while its zone
  is unloaded or the placer is offline.
- **Origin:** Daniel's v2 feature drop (Discord DM, 2026-06-10). Wires the deferred
  Shift+E map-pin gesture that `Features/Signs/SignInteractPatch.cs:22` and
  `docs/v0.1.0/planning/requirements.md:603` both flag as a tracked follow-up.
- **Design pillars this honors** (`design/design-pillars.md`):
  - **Pillar 1** — placed by the **Spade**, never the Hammer (like every SBPR piece).
  - **Pillar 2** — color stays **emergent**: the marker **TYPE** carries the meaning
    (magnifying glass = POI, pickaxe = mining), never a pigment color. The first cut
    ships fixed type-coded icons with **no** per-pin color (Q1), so there is no way
    to attach a meaning to a color here.

## 1. Daniel's scope calls — LOCKED (2026-06-10)

These supersede the open questions Q1–Q5 in the originating card. Build to these.

- **Q1 — COLOR: icons first.** Ship the **4 fixed, type-coded marker icons with NO
  per-pin color customization** in the first cut. Icon-tint + text-color are a
  documented fast-follow. The WorldPin persistence model **must leave room** for them
  (reserve the fields), but do **not** build the color UI now.
- **Q2 — TIER/TIMING: v2 (Black Forest).** Marker Signs ship as part of the v2
  cartography tier — they are the pin substrate the Local Map + Surveyor's Table
  consume, so they land with/before those, not as an earlier v1.x QoL.
- **Q3 — MARKER MODEL: 4 separate Spade build entries** (POI/magnifying-glass,
  mining/pickaxe, shelter/tent, portal). **NOT** one piece with a type selector. Four
  distinct pieces, four distinct build-menu silhouettes.
- **Q4 — DURABLE ID: YES, use the sign's `ZDOID`.** Verified durable in the decomp
  (§3). It is the durable key; the reconcile strategy (§4) is the rest of the answer.
- **Q5 — PORTAL ICON: simple circle for now.** The monochrome wood-portal silhouette
  is the eventual target but is not blocking; ship a circle. Art call, not a blocker.

### 1.1 The four marker types (locked)

| # | Marker type | Icon (first cut) | Prefab name | Pin label |
|---|---|---|---|---|
| 1 | Point of interest | magnifying glass | `piece_sbpr_marker_poi` | "Point of Interest" |
| 2 | Mining resource | pickaxe | `piece_sbpr_marker_mining` | "Mining" |
| 3 | Shelter | simple tent | `piece_sbpr_marker_shelter` | "Shelter" |
| 4 | Portal | circle (→ wood-portal later) | `piece_sbpr_marker_portal` | "Portal" |

Prefab-name strings are **save/wire contracts** the moment a piece is placed in a
live world — lock these four in the first impl PR that registers them and never
rename them after (renaming orphans every placed instance — the same reason
`Pigments` kept `SBPR_Ink*`). The four marker sprites ship as PNGs in
`assets/icons/items/` (the modpack packer copies every `*.png` there beside the DLL
— `scripts/pack-modpack.sh:99-104`) and load via `Runtime/Assets.cs:14
LoadPngAsSprite`, exactly as the cairn marker icon does (`Cairns.cs:175`). **No
AssetBundle.**

## 2. Viability findings (decomp-grounded — these drive the design)

All line refs: `~/valheim/worldgen-spike/decomp/assembly_valheim.decompiled.cs`
(re-verified by the architect during this spec-pass, 2026-06-10).

**V1 — Custom pin ICONS are VIABLE and STABLE; vanilla does NOT persist them.**
- `Minimap.PinData.m_icon` is a public `Sprite` field, separate from `m_type`
  (`PinData` class :46513, `m_type` :46517, `m_icon` :46519). A custom sprite set on
  `m_icon` after `AddPin` **renders correctly**.
- `AddPin` derives the icon from the enum once: `pinData.m_icon = GetSprite(type)`
  (:48481), and serialization writes **only `(int)pin.m_type`** (`GetSharedMapData`
  region :48754+, only `m_save && m_type != Death` pins are written). On load the
  icon is re-derived from the PinType. **⇒ a custom `m_icon` is RUNTIME-ONLY; lost on
  map save/reload and on sync to another client.**
- **🔴 CORRECTION to the originating card's "vanilla re-derives `m_icon` on every
  RebuildPinList" worry — it does NOT.** The render loop `UpdatePins` (:47786) reads
  `pin.m_icon` **only when it (re)creates the marker GameObject** (the
  `pin.m_uiElement == null` branch at :47812 → `pin.m_iconElement.sprite =
  pin.m_icon` :47816). It does **not** call `GetSprite` again. `GetSprite` has
  exactly **two** call sites total (:48440 a message-toast sprite, :48481 the
  AddPin derive) — neither is in the per-frame render. **So once we set a custom
  `m_icon` on a pin we created with `AddPin`, the icon is stable across rebuilds**;
  we do not need a per-rebuild re-skin postfix for the icon. (This simplifies the
  build: the fiddly per-frame re-apply only ever applied to the *color* override,
  which Q1 defers.)

**V2 — Custom icon COLOR + text color are VIABLE, same non-persistence caveat, and
DO need a per-frame re-apply.** `m_iconElement` is a uGUI `Image` → `.color` is
settable, but `UpdatePins` **forces `pin.m_iconElement.color` every frame**
(:47834, `color2 = (m_ownerID != 0) ? grey : Color.white`) and the label color
likewise. So a custom tint would be stomped each frame and require a re-apply pass.
**Q1 defers color**, so this re-apply pass is **out of scope for the first cut** —
but the WorldPin model reserves the color fields so the fast-follow is cheap.

**V3 — Vanilla pins live in the PLAYER PROFILE, client-side, not world state.**
`Minimap` saves/loads pins via `Game.instance.GetPlayerProfile().SetMapData /
GetMapData` partitioned by world UID (`SaveMapData`/`LoadMapData` region :48252+).
So a vanilla pin is **per-character, client-side** — another player never sees your
sign's pin, and **destroying the sign from another client / while offline cannot
reach the pin sitting in the placer's profile.** This is the crux of Daniel's
durability worry, and it is real.

**V4 — `Minimap.RemovePin` exists, two overloads.** `RemovePin(PinData)` (:48408)
and `RemovePin(Vector3 pos, float radius)` (:48366). Live unpin is easy *when you
can reach the pin*; the hard part is reaching it durably (V3).

**Net:** custom icons are fine and stable, but **vanilla's pin store is the wrong
source of truth** (non-persistent for our fields, per-profile, client-side).
Daniel's instinct — *"worldpinnable behaviors with worldpin durable ids"* — is the
correct architecture, not a nice-to-have.

## 3. The durable id — `ZDOID` is verified durable (Q4)

The WorldPin's durable key is **the marker sign's `ZDOID`**. Daniel's condition
("if ZDOID is durable") is verified in the decomp:

- `ZDOID` is a `struct` (`public struct ZDOID : IEquatable<ZDOID>` :64227 region);
  `ID` is a `uint` with a private setter (a stable per-user counter), `UserID` a
  `long`; `ZDOID.None == (0, 0u)`.
- **It is serialized verbatim to the world DB and read back on load.**
  `ZDOMan.Load(BinaryReader)` (:64657) reads every persistent ZDO and does
  `m_objectsByID.Add(item.m_uid, item)` (:64703). The `ZDOID` round-trips through
  the world save (`ZDOID(BinaryReader)` ctor :64278). ⇒ **a persistent ZDO's ZDOID
  is STABLE across save/reload.**
- On destroy the ZDO is released and removed from the table
  (`m_objectsByID.Remove` :65051); `ZDOMan.GetZDO(id)` (:64828) is a pure
  `m_objectsByID.TryGetValue` lookup that then returns null. ⇒ **destroyed sign →
  ZDOID stops resolving → pin reconciles away.**
- Buildable pieces are persistent (`ZNetView.m_persistent`), so this applies to our
  marker signs.

**The load-bearing fact that makes durable offline unpin tractable:** on a dedicated
server, `ZDOMan.Load` loads **ALL persistent ZDOs in the world** into `m_objectsByID`
(:64703), not just loaded zones — an unloaded zone's ZDOs sit inert in that dict.
So **server-side, `GetZDO(signZDOID) != null` is an O(1) authoritative
"does this sign still exist anywhere in the world?" check, regardless of zone-load
state.** This is the primitive the reconcile (§4) is built on, and it is why the
hard offline case is solvable at all.

## 4. The reconcile architecture — derive-by-scan, server-authoritative (the headline)

Daniel's note correctly separates two things: the **durable id is the foundation**,
the **reconcile strategy is the rest**. He surfaced the key simulation insight and
offered two shapes; the decomp lets me pick.

### 4.1 Daniel's load-bearing insight (validated)

> **Destruction only ever happens in a LOADED zone.** WearNTear decay, raids, and
> player demolition all require the sign to be instantiated, which requires its zone
> to be simulated, which means the SERVER sees the destroy. There is no "sign
> destroyed in a truly unloaded zone" — an unloaded ZDO just sits inert in the DB;
> nothing mutates it.

Validated against the decomp: `WearNTear.Destroy` (:129033) runs **owner-side**
(reached via `RPC_Remove` → `if (m_nview.IsOwner()) Destroy()` :129027, and the
decay/hit paths all gate on `m_nview.IsOwner()`), and a ZDO with no instantiated
GameObject has no `WearNTear` ticking. So the hard case reduces to: **the OWNING
client was offline/away when someone ELSE destroyed their sign.** The server caught
the destroy (the zone was loaded for the destroyer); the owner's client did not.

### 4.2 Decision: DERIVE-BY-SCAN, not a persisted ZDOID index

Daniel offered two shapes. **Locked choice: derive-by-scan.** Rationale, grounded:

- **There is no separately-persisted "WorldPin registry" to keep in sync.** The set
  of WorldPins **IS** the set of live marker-sign ZDOs. The server already holds all
  of them in `m_objectsByID`; the marker signs are discoverable by prefab via
  `ZDOMan.GetAllZDOsWithPrefabIterative(prefab, list, ref index)` (:65497), which
  walks `m_objectsBySector` (the complete persistent set server-side) in bounded
  batches (400 sectors/call — already used by vanilla at :37512).
- **This is stale-free by construction.** You only ever enumerate ZDOs that *exist*.
  A destroyed sign's ZDO is gone from the dict, so it cannot appear in a scan. There
  is **no dangling-reference failure mode** — which a stored ZDOID list *would* have
  (a persisted list of ids can outlive the things they point at, exactly the
  orphaned-reference class ADR-0006 fought in a different guise).
- **Each marker-sign ZDO carries its own pin metadata** (marker type, `pinned`
  bool, reserved color slots) in its ZDO fields, written owner-side via `ZNetView`,
  mirroring `SignTag`'s pattern (`Features/Signs/SignTag.cs`). So a scan reads
  `{position (ZDO.GetPosition :62297), prefab→marker-type, pinned}` directly off
  each live ZDO — no second data structure.

**The cost we accept (honest):** derive-by-scan gives **eventual** cross-player
unpin for the offline owner — the stale pin clears on the owner's **next reconcile
pass** (map open / login / zone-enter near the bound area), not instantly while
they are offline (they are not connected to receive an instant update anyway). For
the *online* cases (Shift+E, local destroy, a destroy while the owner is connected)
the unpin is prompt via the fast path (§4.3). This is the right trade: it buys
**zero stored-reference fragility** and **stale-free-by-construction** for the price
of "the pin is gone the next time you look at the map," which is exactly the
behavior Daniel described ("the next reconcile pass finds the ZDOID no longer
resolves and drops it").

> **Why not the persisted index?** A persisted server index keyed by ZDOID would
> give *instant* cross-player unpin but adds: a second source of truth to keep
> consistent with the ZDO set, add/remove hooks that must never miss, and a
> dangling-id cleanup pass for when they inevitably desync (crash mid-write, a ZDO
> removed by a path that didn't fire our hook). Derive-by-scan deletes all of that
> machinery. If a future feature needs instant offline cross-player unpin badly
> enough to justify the index, it can be layered on later — but the MVP should not
> pay that complexity, and the scan is already O(loaded-sector-count) cheap on the
> cadence we run it.

### 4.3 The pin lifecycle — projection, fast path, durable backstop

**The vanilla minimap is only a RENDER TARGET.** The source of truth is the world
(the live marker-sign ZDOs). The client projects pins from that truth:

- **Projection (render).** On map open and on a light periodic cadence, the client
  builds the set of WorldPins it should show, and for each one calls
  `AddPin(pos, type, name, save:FALSE, …)` — **`save=false` so vanilla NEVER
  persists OUR pins** (this is what kills the V1/V2 non-persistence trap *by
  construction*: nothing about our pins is written to the player profile, so there
  is no stale client-side pin that can survive a reconcile). Immediately after
  `AddPin` returns the `PinData` (:48466), set `pinData.m_icon = <custom marker
  sprite>`. Per V1 that icon is **stable** — no per-rebuild re-skin needed.
- **What set does the client project?** The WorldPins whose sign falls inside the
  area the client cares about. In the **field Local-Map / minimap** view that is the
  bound Surveyor's Table's **1000 m disc** (pins only render within the disc — the
  cartography lock C5/D4). The client gets the authoritative live set from the
  server-side scan (§4.2) for that disc; signs in loaded zones it can read directly,
  unloaded-zone signs come from the server's resident `m_objectsByID`.
- **Fast path (online, prompt).** Two triggers unpin immediately when the zone is
  loaded:
  1. **Shift+E** on a placed marker sign (the `alt:true` interact, §6) flips the
     sign's `pinned` ZDO bool off (owner-write via `ZNetView`) and calls
     `Minimap.RemovePin` on the locally-projected pin.
  2. **The sign's destroy hook.** Subscribe to `WearNTear.m_onDestroyed` (a public
     `Action` field, :128027, fired owner-side inside `Destroy()` at :129051) on the
     marker-sign instance. **No Harmony patch needed** — it is a public subscribe
     seam. On destroy, the owning client (and any client with the zone loaded)
     drops the projected pin promptly.
- **Durable backstop (the offline case).** Because every projected pin is a *live
  projection*, not a stored thing, a destroyed sign — even one destroyed while its
  zone was unloaded for the owner, or while the owner was offline — simply **is not
  in the next scan** (its ZDO is gone from `m_objectsByID`). The owner's next
  reconcile pass (map open / login) rebuilds the projection from the live set and
  the stale pin is **not re-added**. No client-side stale pin can survive, because
  nothing about our pins is persisted client-side. This is exactly the
  offline-durable behavior Daniel asked for.

### 4.4 The reconcile cadence (impl-card to confirm timings in-game)

- **On map open** — full reconcile of the visible disc (the load-bearing one; this
  is when a returning offline owner's stale pin clears).
- **On a light periodic tick while the map is open** — pick up Shift+E/destroy from
  other clients without reopening.
- **On zone-enter / login** — opportunistic refresh.

Exact intervals are playtest-tuned (v0.2+ polish); the load-bearing requirement is
"map-open does a full reconcile." Keep the scan server-authoritative and batched
(the iterative API is designed for exactly this).

## 5. Coupling — this IS the v2 cartography pin substrate (one pin model, do not fork)

The locked cartography decisions already depend on a pin model SBPR doesn't yet
have. **The WorldPin defined here is that model.** Concretely:

- **`cartography-v2.md` D4 / requirements.md §1, §4** — pin **removal/editing**
  happens at the **Surveyor's Table**. The Table's forked viewer operates on the
  shared data and enables pin removal. **Those removed pins are WorldPins** — the
  Table edits the same WorldPin set this doc defines.
- **C5** — the Surveyor's Table holds a **shared, cumulative survey within its
  1000 m bound**. WorldPins render only inside that disc (§4.3).
- **C8 / requirements.md §4** — pin **add/share** is the per-pin explicit-sharing
  model from `design/pin-sharing.md`. A **Marker-Sign WorldPin is inherently a public
  artifact** (the sign is visible in the world to anyone) — it maps onto
  pin-sharing.md's **Option C / Option D sign-driven path**: the pin is keyed to the
  sign's ZDOID (`m_sourceSignZDOID` in that doc's vocabulary), which is **exactly the
  durable id this doc locks**. So this WorldPin substrate and the pin-sharing model
  are the *same* design viewed from two angles — the sign's ZDOID is both the
  durable unpin key (here) and the pin's source-identity for sharing (there).

**Sequencing:** the WorldPin model (this doc) must land **before/with** the Local
Map viewer (`cartography-impl-spec.md §2B`, card `t_7b616020`) and the Surveyor's
Table (`§1`, card `t_38f9c77a`) so they consume it rather than forking a second pin
notion. The Marker Sign feature is the **natural first consumer** that proves the
substrate. Cross-references the cartography spec-pass card **t_4be278de** and the
locked design `docs/design/cartography-v2.md`. **Do NOT design a second,
incompatible pin notion inside the cartography cards.**

## 6. The Shift+E gesture — grounded, already half-wired

- **Shift+E = `Sign.Interact(user, hold:false, alt:true)`.** The `alt` interact
  param is the AltPlace (Shift) key — `Player` reads `ZInput.GetButton("AltPlace")`
  into `alt` (:16115) and feeds it through `Interact(go, hold, alt)` (:19270 →
  `componentInParent.Interact(this, hold, alt)` :19280). So "Shift+E" arrives at the
  existing `SignInteractPatch` prefix as `alt == true` — **no new input plumbing.**
- The existing prefix `Features/Signs/SignInteractPatch.cs` already intercepts
  `Sign.Interact` for SBPR signs (those carrying a `SignTag`) and currently opens
  the paint panel on the primary interact. Its comment (:22) explicitly records that
  the **Shift+E map-pin gesture is NOT wired** ("tracked follow-up; this patch
  intentionally does not add it"). **This feature wires it:** the prefix branches on
  `alt` — `alt:false` → paint panel (existing behavior, unchanged); `alt:true` →
  toggle the marker sign's `pinned` ZDO bool + project/remove the WorldPin. (Marker
  signs use a marker-specific tag — see the impl spec — so the branch only affects
  marker pieces, not the plain Painted Sign, which has no pin gesture.)

### 6.1 Discoverability — the gesture must be VISIBLE in-world (card t_7816c0b0)

The gesture shipped working but **undiscoverable**: a placed marker sign shows only the
vanilla `Sign` hover text, so a player has no way to learn Shift+E pins/unpins it, and no
indication of the current pin state. Daniel, v0.2.19-playtest: *"sign posts should have
hint text when looked at that state Shift + E to pin/unpin depending on the tracked
state."*

- **The surface:** the crosshair hover text, produced by `Sign.GetHoverText` — which is
  the `Hoverable` that wins the query on a marker (the vanilla `Sign` owns `Hoverable` and
  is added before `MarkerSignTag`; `Hud.UpdateCrosshair` reads a single
  `GetComponentInParent<Hoverable>()`). The fix is a Harmony **postfix** on
  `Sign.GetHoverText`, markers-only, that **appends** a state-aware line:
  - not pinned → `[Shift+E] Pin to map`
  - pinned → `[Shift+E] Unpin from map`
  reading the existing `MarkerSignTag.ReadPinned()`. The vanilla typed-text + primary
  `[Use]` line are preserved above it (append, never replace). Markers-only, so a plain
  Painted Sign is unaffected.
- This is **UX polish over an already-correct gesture** — no new state, no new ZDO field,
  no SpecCheck change. Full buildable detail (root cause, route (a) vs (b), patch shape,
  wording, key-token correctness, ATs) is in the impl spec **§4A**.

## 7. Clean/dirty routing & scope

- **Clean-side.** Reading vanilla `Minimap` / `PinData` / `ZDOMan` / `WearNTear`
  internals is allowed and required (ADR-0001: vanilla is fair game; we read the game
  we mod). Route to **architect** (this doc) then implementation to the engineers.
- **🔴 ADR-0001 firewall — do NOT read/crib third-party pin mods** (QoLPins,
  ColorfulSigns, BetterCartographyTable source, etc.) to copy behavior. The viability
  question is **fully answered from the vanilla decomp** above. Daniel's "some mods
  expand the pin system, we can learn from them" is satisfied by the vanilla-API
  design here — if a deep behavioral spec of another mod is ever wanted it goes
  through the `reviewer-cleanroom` wall, not a direct read.
- **ADR-0006 — the 4 marker pieces are built ADDITIVELY** (`new GameObject()` +
  `AddComponent`), NOT by cloning a ZNetView-bearing prefab. This is AT-PIN-ADR0006.
  (Note: the *existing* Painted Sign still clones `sign` — that predates ADR-0006 and
  is out of scope to refactor here; the NEW marker pieces are additive. See the impl
  spec for how to build a sign-equivalent additively.)

### In scope
The 4 marker types; the Shift+E pin/unpin gesture; the WorldPin durable-id
derive-by-scan reconcile; custom marker-icon rendering (`save:false` projection +
`m_icon` override); the panel-icon-for-reference; additive 4-piece construction.

### Out of scope (raise, don't silently fold)
- **Per-pin color** (icon tint + text color) — Q1 defers; reserve the ZDO fields,
  build no color UI. Fast-follow.
- **The "icon overlaid bottom-right on the piece build-icon art"** — Daniel: "for
  now, just make the piece art the icon art." v2.1 polish.
- **The monochrome wood-portal silhouette** — Q5 ships a circle first.
- **The Surveyor's-Table pin-editing UI** — that is the cartography cards
  (`t_38f9c77a` / `t_7b616020`); they consume this substrate.
- **A persisted server WorldPin index** — §4.2 rejects it for the MVP.

## 8. Acceptance tests (named — refined in the impl spec)

These are the originating card's ATs, ratified. The impl spec
([`../v2/planning/marker-signs-impl-spec.md`](../v2/planning/marker-signs-impl-spec.md))
restates them per-feature with exact observable criteria.

- **AT-MARK-1** — Four Marker Sign build entries exist on the Spade ('Trail' tab),
  each placeable like the existing sign; the panel shows the marker's icon for ref.
- **AT-MARK-2** — Shift+E on a placed marker sign adds a map pin using that marker's
  CUSTOM icon sprite (not a stock vanilla pin); Shift+E again removes it.
- **AT-PIN-PERSIST** — the custom icon survives map close/reopen AND a server restart
  AND a fresh client join — it does NOT revert to a stock vanilla pin (guards the
  V1/V2 non-persistence trap; passes because the pin is a `save:false` projection
  re-skinned on every reconcile, never relying on vanilla persistence).
- **AT-PIN-DESTROY-LOADED** — destroying a pinned marker sign in a loaded zone
  removes its pin promptly (fast path: `WearNTear.m_onDestroyed`).
- **AT-PIN-DESTROY-DURABLE** — a pinned marker sign destroyed while its zone is
  UNLOADED / the placer is OFFLINE (decay/raid) leaves **no stale pin** — gone on the
  owner's next map open (the load-bearing durability test; passes by construction via
  derive-by-scan).
- **AT-PIN-ADR0006** — the 4 marker pieces are built additively (no runtime clone of
  a ZNetView-bearing prefab).
- **AT-MARKER-HINT-1…6 + AT-MARKER-HINT-WARD** *(card t_7816c0b0 — hover hint)* — a
  placed marker sign's crosshair hover text carries a state-aware `[Shift+E] Pin to map`
  / `Unpin from map` line that flips with `ReadPinned()`, preserves the vanilla `[Use]`
  hint, shows only on markers (not plain Painted Signs), tokenizes the use key, and
  appends nothing when the player lacks ward access. Refined in impl spec §4A.5.
- **AT-PILLAR-2** — no UI/tooltip assigns an inherent meaning to a pigment color; the
  marker TYPE carries the meaning. (First cut has no per-pin color at all, so this
  holds trivially; the fast-follow color must preserve it.)
- **logs-green ≠ playable** — every AT closes only on Daniel's in-game check.

## 9. Provenance

- **Origin:** Daniel's v2 feature drop (Discord DM, 2026-06-10), card `t_5fb8703d`.
- **Scope calls Q1–Q5:** locked by Daniel 2026-06-10 (the card's second comment).
- **Architect spec-pass + decomp re-verification:** 2026-06-10 (this doc).
- **Supersedes:** the deferred Shift+E gesture in `SignInteractPatch.cs:22` /
  `requirements.md:603`.
- **Couples to:** `docs/design/cartography-v2.md`, `docs/design/pin-sharing.md`,
  `docs/v2/planning/cartography-impl-spec.md`, cards `t_4be278de` / `t_38f9c77a` /
  `t_7b616020`.
