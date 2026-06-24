---
title: "Trailborne v2 (Black Forest) — Live-update cartography (field WRITE axis) buildable impl-spec"
status: current
purpose: "Buildable implementation spec for the locked live-update cartography model (map-provider-model.md §3.2a / §4.0a / §5 / §6, design PR #266, Daniel 2026-06-24). Turns the RENDER-vs-WRITE axis split into build-ready work: the field WRITE axis (Kit-worn reveal mutates every carried imprinted in-region Local Map's stored artifact, plus the vanilla global), the chosen perf model (direct-blob-mutation with a dirty-check — justified against the sub-KB blob), the Surveyor's Table local→ingest path, and named acceptance tests. Architect spec-pass, card t_d46b3398. SUPERSEDES the §2I render-overlay half of cartography-impl-spec.md (issue 5, PR #131): that approach was a render-time snapshot∪live overlay that explicitly NEVER mutated storage; #266 reverses it — the field write genuinely grows the held map's m_customData artifact. Implementers (engineer-ui) build from THIS doc; map-provider-model.md is the design lock."
owner: Daniel (design authority, locked #266); Starbright (impl-spec capture + grounding)
supersedes_partial:
  - "docs/v2/planning/cartography-impl-spec.md §2I (issue 5, 'Held map updates live while travelling with the Kit') — the render-time snapshot∪live overlay is replaced by genuine artifact mutation (this spec). The §2I render-overlay was specced but NEVER built (MapViewer.cs carries no merged-fog path); supersession is doc-level, no code removal."
---

# Live-update cartography — field WRITE axis (buildable impl-spec)

The design lock ([`../../design/map-provider-model.md`](../../design/map-provider-model.md)) is the
ratified *what*. This doc is the *buildable how* for the **WRITE axis** Daniel locked 2026-06-24
(PR #266, `main` @ 37fa8a8): one tight section per piece an `engineer-ui` worker picks up cold —
observable acceptance criteria, the exact vanilla + SBPR hooks (file:line), feature-folder
placement, the perf/architecture decision, and the SpecCheck manifest impact.

> **Clean-side note (ADR-0001):** every vanilla read cited here is the base game
> (`assembly_valheim`), fair game to read and adapt. Additive only (ADR-0006). net48,
> `<TreatWarningsAsErrors>` ON → **0 warnings**.

> **🔴 THIS SUPERSEDES the §2I render-overlay (cartography-impl-spec.md, issue 5 / PR #131).**
> The old §2I "Held map updates live while travelling" specced a **render-time** fog merge:
> `mergedFog[i] = snapshot.Fog[i] || liveWindow[i]` built into a throwaway copy at paint time,
> with the stored snapshot **explicitly never mutated** ("the imprinted snapshot stays static in
> storage … the live overlay is each reader's OWN `m_explored`, read at render time, never
> persisted", §2I.3). **Daniel's #266 model reverses that contract** (§3.2a): the field write
> **genuinely mutates the held map's artifact** — the `m_customData` blob grows — which is the
> only way *"carried-but-unequipped maps update silently; the new ground appears next time you
> equip or M-open them"* (§3.2a) can be true. A render-time overlay can't persist; a handed-off
> or relogged map would snap back to its imprint snapshot. So this spec replaces the overlay with
> **storage mutation**, generalizes it from the single equipped map to the **plural** carried
> write-set, and adds the **global** co-write. See §7 for the full reconciliation. **The §2I
> render-overlay was specced but never built** (verified: `MapViewer.cs` contains no merged-fog /
> live-window path), so this is a spec-level supersession with **no code to remove**.

---

## 0. SpecCheck manifest impact (read first) — **+0**

`Runtime/SpecCheck.cs` is a **recipe + icon** drift manifest (`RecipeSpec` rows: build pieces,
item recipes, resolved-icon checks — verified against `SpecCheck.cs`). This card is
**behavior-only**: it changes *where survey fog is written*, adds **no** new piece / item /
recipe / station / resource, and changes **no** recipe quantity. **SpecCheck delta = +0.** Do not
touch `SpecCheck.cs`; do not change its recipe count. (Confirms the task's expectation.)

No new wire-contract keys either: the write target is the **existing** `m_customData` blob key
`sbpr_map_blob` (`LocalMap.cs:77`), already a locked save/wire contract. The ingest match keys on
the **existing** `sbpr_map_bound` (`LocalMap.cs:78`). Nothing new is persisted.

---

## 1. The model in one paragraph (grounded)

When a Cartographer's Kit is worn, vanilla `Minimap.Explore` **already** writes the personal
global fog `m_explored` (the existing Kit-gate Prefix `UpdateExploreGate` returns `true`, letting
vanilla run — `CartographersKit.cs:287-309`). **The only new behavior this card adds** is: on the
same cadence, **stamp that same revealed ground into every carried, imprinted Local Map whose
1000 m region contains it**, by OR-merging the windowed global fog into each map's stored
`SurveyData` blob and rewriting `m_customData[sbpr_map_blob]`. The blob is the truth the viewer
already re-reads every 0.25 s (`LocalMapController.cs:173-194`), so a grown blob auto-reflects on
both the disc and the full view with **no render change**. The write-set is **plural** (all
in-region carried imprinted maps), keyed on **hold + Kit**, independent of the render-provider
(equip). A map whose disc doesn't contain the revealed cell is excluded **for free** by the disc
clip; a blank (unimprinted) map is excluded **for free** by `IsImprinted`.

**The two axes (design §3.2a), and which one this card touches:**

| Axis | Keyed on | Set size | This card |
|---|---|---|---|
| **RENDER** (full view + minimap disc) | **equip** (`LocalMapController._provider`) | exactly 1 | untouched |
| **WRITE** (receives field survey) | **hold + Kit** (carried imprinted in-region) | plural | **THIS CARD** |

---

## 2. The WRITE axis — stamp the revealed window into every in-region carried imprinted map

**Lands in:** a new `Features/Cartography/LiveFieldWrite.cs` (static), **driven from** the existing
`LocalMapController` poll. Rationale for a dedicated class: the write-set is *plural* and a
distinct concern from the controller's *singular* provider machine; isolating it keeps the
controller readable and the write path unit-testable. It is **client-only** by construction
(reads `Minimap.m_explored`, writes the local player's own carried items' `m_customData`, which
rides the local profile save — design §3.1 finding 1; no networked ZDO while carried).

### 2.1 The hot path (per throttled tick, Kit-on)

Driven from `LocalMapController.Update` (already client-only, already polls inventory at
`PollSeconds = 0.25f`, `LocalMapController.cs:45/87`). Add one call `LiveFieldWrite.Tick(player)`
inside the existing throttled block (after the provider machine, `cs:146-166`), **sub-throttled to
~2 s** (a private `_nextWrite` clock, matching vanilla's `m_exploreInterval` — new ground appears
at most that fast, so finer is wasted work). `Tick`:

1. **Kit gate (reuse verbatim).** `if (!CartographersKit.IsWearingKit(player)) return;`
   (`CartographersKit.cs:231`) — the exact public detector the gate Prefix uses. No Kit → no
   write-set (and, separately, vanilla wrote no new global fog this tick either, so nothing to
   stamp). *(See §6 for the nomap-OFF equip-keyed variant.)*
2. **Read global fog once.** `bool[] explored = MinimapFog.ReadExplored(mm);` — **extract** the
   reflected-field read currently private in `SurveyorTableTag.ReadExplored` (`cs:558-563`,
   `GetField("m_explored", Instance|NonPublic)`, cached `FieldInfo`) into a tiny shared
   `MinimapFog.ReadExplored(Minimap)` helper so the Table contribute and this writer read
   `m_explored` through **one** cached `FieldInfo` (no second reflection path). `mm =
   Minimap.instance`; bail if null (headless). `textureSize = mm.m_textureSize`, `pixelSize =
   mm.m_pixelSize` (both public, `cs:358-359`).
3. **Build the write-set.** Scan `player.GetInventory().GetAllItems()` (the same idiom as
   `LocalMapController.ResolveColdStartProvider`, `cs:407-421`) filtered to:
   - `IsLocalMap(it)` (prefab-tag check, `cs:432-437`) **AND**
   - `LocalMap.IsImprinted(it)` (`LocalMap.cs:320` — **unimprinted excluded for free**, design
     §4.0a / task item 6) **AND**
   - **in-region pre-filter:** `BoundedMapMath.InDisc(player.x, player.z, origin.x, origin.z,
     SurveyRadiusMeters + pixelSize)` where `origin = TryGetBoundOrigin(it)` (`LocalMap.cs:349`).
     The `+ pixelSize` margin avoids edge flicker at the rim. A map whose disc the player has
     **left** fails this → **not written** (design AT-LIVE-OUTREGION). *(Even without this filter
     the disc clip in step 4 would no-op it; the filter just skips the array build for far maps.)*
4. **Stamp each in-region map (idempotent OR).** For each map `M` in the set, window the **global**
   fog into **M's own** disc and OR-merge it into M's stored survey:
   ```csharp
   var cap = SurveyData.CaptureWindow(
       explored, textureSize, pixelSize,
       origin.x, origin.z, SurveyRadiusMeters,
       candidatePins: System.Array.Empty<SurveyPin>(),   // §2.2 — pins ride existing live union
       out int exploredInDisc, out _);
   var cur = LocalMap.ReadSurvey(M) ?? new SurveyData();  // sub-KB deserialize
   if (cur.MergeFrom(cap, out bool changed) && changed)   // §2.3 — OR fog; report flip
       LocalMap.WriteSurveyBlob(M, cur);                  // §2.4 — reserialize → m_customData
   ```
   - 🔵 **Why this is automatically §4.1 1:1-aligned:** `BuildWindowedFog` (inside `CaptureWindow`,
     `BoundedMapMath.cs:103-140`) reads `explored[srcY * textureSize + srcX]` — **the exact global
     array index** vanilla's own fog uses — at M's grid-snapped window (`ComputeWindow` →
     `WorldToCellX/Y`, `cs:54-58`). So a stamped local cell **IS** the global cell at the same
     world coordinate, by construction. The design's exploration-correctness invariant (§4.1) is
     preserved with zero extra code (AT-LIVE-ALIGN).
   - 🔵 **Idempotent:** re-stamping an already-lit cell is a no-op (`||`). No delta tracking, no
     before/after diff of `m_explored` needed — OR-merging the current windowed global fog each
     tick converges. Standing still or re-covering known ground writes nothing new (the dirty-check
     §2.3 then skips the reserialize).

### 2.2 Pins are NOT part of the hot-path write (grounded scope call)

The locked write is **fog** — design §5: *"field reveal writes … the same 64 m window"*; §3.2a:
*"the same revealed window is stamped."* Pins are handled on two separate, already-correct paths,
so the per-tick write passes `Array.Empty<SurveyPin>()`:

- **Live VISIBILITY of pins** is already done at render: the disc/full-view overlay unions the
  snapshot's pins with the **live** `WorldPins.CollectInDiscPins(origin, radius)` every rebuild
  (the existing pin overlay — cartography-impl-spec §2I.1/§2K). A player's freshly-dropped pin
  already shows on a bound map's disc **without** baking it into the blob. No change.
- **PERSISTENCE / home-flow of pins** is the Surveyor's Table **ingest** (§3): on Table-use the
  map's pins (and the player's in-disc pins) are folded into the Table survey, then re-imprinted.
  Pins reach durable storage there.

This keeps the hot path pure fog-OR (cheapest) and avoids per-tick pin dedup
(`AddOrUpdatePin` is O(pins²), `SurveyData.cs:190`). 🟡 **Reversible sub-decision:** if Daniel
wants a carried map to also *persist* pins dropped in its region without a Table visit, pass the
live `CollectShareablePins` set into `CaptureWindow` here (it already disc-clips + the merge
dedups) — a one-line change, at the cost of per-tick pin dedup. Left OUT by default; flag on
review if the home-flow-only pin persistence feels wrong in-game.

### 2.3 Dirty-check (the perf gate) — extend `MergeFrom` to report a flip

To avoid reserializing when nothing new lit, `MergeFrom` must report whether any fog cell flipped
`false→true`. Add a `bool` out-param to the existing OR loop (`SurveyData.cs:173-176`):
```csharp
public bool MergeFrom(SurveyData other, out bool changed) {
    changed = false;
    // … existing empty-adopt + grid-match guards (the adopt path sets changed = true) …
    for (int i = 0; i < n; i++)
        if (!Fog[i] && other.Fog[i]) { Fog[i] = true; changed = true; }
    foreach (var p in other.Pins) if (AddOrUpdatePin(p)) changed = true;
    return true;   // "merge ran" (grid matched); `changed` = "anything actually flipped"
}
```
Keep the existing `MergeFrom(other)` as a 1-line overload (`=> MergeFrom(other, out _)`) so the
Table contribute path (`SurveyorTableTag.cs:371`) is untouched. **Only reserialize when
`changed`** — this is the dirty-check that makes the steady state (exploring known ground, or no
Kit) cost zero writes.

### 2.4 The blob write — add `LocalMap.WriteSurveyBlob` (intent-named, not "Imprint")

`LocalMap.Imprint` (`LocalMap.cs:300`) already does the serialize→`m_customData` write but also
(re)writes `BoundKey` + `NameKey` and reads as a *birth* gesture. For a live field write we mutate
**only** the fog blob and must leave the region/name identity untouched. Add a thin sibling that
is literally `Imprint`'s blob line extracted:
```csharp
/// Live field-write: overwrite ONLY the survey blob (sbpr_map_blob) with an updated SurveyData,
/// leaving BoundKey + NameKey (region + name identity) intact. Used by LiveFieldWrite (§2) to
/// grow a carried map's artifact as the Kit-wearer explores. The blob is the §2C windowed format.
public static bool WriteSurveyBlob(ItemDrop.ItemData item, SurveyData survey) {
    if (item == null || survey == null) return false;
    try {
        item.m_customData[MapBlobKey] = Convert.ToBase64String(Utils.Compress(survey.Serialize()));
        return true;
    } catch (Exception e) {
        Plugin.Log.LogWarning($"[Trailborne/Cartography] WriteSurveyBlob failed: {e.Message}");
        return false;
    }
}
```
Identity is preserved: `BoundKey`/`NameKey` are never touched, so `TryGetBoundOrigin` /
`TryGetName` / `FormatDisplayName` keep returning what the imprint stamped (AT-LIVE-IDENTITY).

---

## 3. PERF MODEL — **direct-blob-mutation with a dirty-check** (LOCKED), in-RAM working-set documented as the deferred optimization

This is the real architecture question (task item 3). **Decision: direct-blob-mutation with a
dirty-check** (§2.3/§2.4) — deserialize the carried map's sub-KB blob, OR-merge the windowed
global fog, and **only when a cell actually flipped**, reserialize and rewrite `m_customData`. No
RAM cache. Justification, grounded:

1. **The blob is sub-KB (design §3.3, decomp-grounded).** A 1000 m disc at 64 m/texel is
   `Size = 2·⌈1000/64⌉+1 = 33` → `33×33 = 1089` cells ≈ 125 B raw fog, compressing "to near-
   nothing"; "well under 1 KB" even with a layer. So the per-write cost
   (`Deserialize`+`MergeFrom`+`Serialize`+`Compress`+Base64, `SurveyData.cs:259/294`) is a sub-KB
   `ZPackage` round-trip — microseconds.
2. **The real cadence is low.** New ground is revealed at most every `m_exploreInterval` (2 s),
   and **only** when the player moves into unexplored territory inside a map's disc. The §2.3
   dirty-check means: standing still, re-covering known ground, or Kit-off ⇒ **zero** reserializes.
3. **The multiplier is tiny.** The write-set is "carried imprinted **in-region** maps" — in
   practice 1 (the equipped map), occasionally 2–3. Bounded by inventory.
4. **🔑 It needs NO render change — the blob IS the source of truth.** The viewer re-reads
   `LocalMap.ReadSurvey()` (→ the blob) every 0.25 s on both surfaces
   (`LocalMapController.DriveMinimapDisc:204` / `RefreshOpenView:349`). A mutated blob is therefore
   reflected within 0.25 s with **zero** viewer code (§5). The in-RAM-working-set alternative would
   force the read path to be **rerouted through the cache** for liveness — strictly *more* code and
   a second source of truth to keep coherent.
5. **It avoids a cache lifecycle that is a bug surface.** A working-set keyed by item instance must
   be **evicted + flushed** precisely on drop / trade / death (the moment the map's instance leaves
   inventory and — design §3.1 finding 3 — its blob becomes a networked item-drop/tombstone/chest
   ZDO that **must** already carry the accumulated work). Direct mutation has this for free: the
   blob is always current, so the dropped item's ZDO serializes the up-to-date `m_customData` with
   no flush hook to forget.

**🟡 The in-RAM working-set is the documented deferred optimization — reach for it ONLY if:** a
future finer resolution (design §3.3's note: an 8 m grid over 2 km ≈ 62.5 k cells ≈ 1–3 KB
compressed) or pathological carried-map counts make per-tick serialization measurable in a
profile. Its shape, for the record: a `Dictionary<ItemData, SurveyData>` live cache, OR'd each
tick (bool ops only, no serialize), flushed to `m_customData` on (unequip | Table-use |
inventory-save/logout | a throttled cadence) and **evicted+flushed on leave-inventory**, with
`ReadSurvey`/the viewer rerouted to prefer the cache for liveness. This trades the sub-KB
per-write for a coherence-and-eviction burden that is **not justified at the locked 64 m
resolution** (Daniel's "build what you need, lean" doctrine). Lock direct-mutation now; leave a
one-paragraph breadcrumb in `LiveFieldWrite.cs` pointing here.

---

## 4. Write-path edge cases + ordering guarantees (the engineer's checklist)

The write axis touches shared state (the carried items' `m_customData`) on a poll, so a handful of
orderings must be nailed down. None require new mechanism — they fall out of the §2/§3 shape — but
state them so the engineer doesn't have to rediscover them:

- **Provider ≠ write-set (independence).** `LiveFieldWrite` reads the inventory **directly** each
  tick (§2.1 step 3); it does **not** consult `LocalMapController._provider`. So the equipped map
  being the render-provider has **no** bearing on which maps get written — an unequipped carried
  in-region map is written exactly like the equipped one (design §3.2a). Do **not** shortcut the
  scan to "just the provider."
- **Cold-start ordering.** The controller's one-shot cold-start provider re-derivation
  (`_coldStartResolved`, `LocalMapController.cs:134-139`) runs **before** the `LiveFieldWrite.Tick`
  call in the same poll. Irrelevant to the write-set (which re-scans inventory anyway), but place
  the `Tick` call **after** the provider machine (`cs:~166`) so a freshly-equipped map is already
  the provider when the write happens — keeps logs readable (provider-set then writes).
- **Drop / trade / death mid-accumulation.** The instant a map leaves inventory it falls out of the
  next tick's scan → no further writes (correct). Its blob is already current (direct-mutation, §3),
  so the now-in-world item-drop / tombstone / chest ZDO serializes the **accumulated** work (design
  §3.1 finding 3) with **no flush hook** — this is the concrete payoff of choosing direct-mutation
  over a working-set (which would need an eviction-flush exactly here, §3 point 5).
- **Two maps of the SAME region carried.** Both are in the write-set and both get the identical
  windowed stamp (AT-LIVE-MULTI). They converge to the same fog independently; no dedup needed (each
  owns its blob). A later Table-ingest (§5) folds both into the Table and they stay consistent
  through the shared Table truth (design §4 "overlapping local maps stay consistent").
- **Region boundary / disc rim.** A cell exactly on the 1000 m disc edge is decided by
  `BuildWindowedFog`'s world-space disc test (`BoundedMapMath.cs:128-134`), identical to how the
  imprint snapshot and the Table survey clip — so a live-written map and a fresh imprint of the same
  Table agree on the rim by construction. The `+ pixelSize` margin in the §2.1 in-region pre-filter
  is **only** an array-build skip, never a clip widening (the clip itself stays at exactly
  `SurveyRadiusMeters`).
- **No ownership / RPC.** Unlike the Table (which owner-routes its ZDO via `InvokeRPC`,
  `SurveyorTableTag.cs:525-554`), a carried item's `m_customData` is **local, single-owner** (the
  carrying player) and never networked while carried (design §3.1 finding 1) — so the write is a
  plain dictionary assignment, no `ClaimOwnership`, no RPC, no ward check. (Ward gating applies to
  the Table ingest path, §5, via `ContributeLocalSurvey`'s existing checks — not to the field
  write.)

## 5. Table local→ingest (design §4.0a) — pull bound carried maps' discoveries home on Use

**Lands in:** `SurveyorTableTag.Interact` (`SurveyorTableTag.cs:148-232`), one new step.

Design §4.0a item 2: *"Using a Surveyor's Table finds the carried Local Maps bound to THAT Table
and pulls their new field discoveries + pins back into the Table's stored survey (local → table),
then runs the existing §4 … sync."* Hook it in `Interact`, immediately **after**
`ContributeLocalSurvey(user)` (`cs:168`) and **before** the name-gate / viewer-open:

```csharp
ContributeLocalSurvey(user);     // existing: player global fog + pins → this Table (cs:168)
IngestBoundCarriedMaps(user);    // NEW (§5): carried maps bound to THIS table → this Table
```

`IngestBoundCarriedMaps(Humanoid user)`:
1. Compute **this Table's grid cell**: `tcx = WorldToCellX(transform.position.x, pixelSize,
   textureSize)`, `tcy = WorldToCellY(transform.position.z, …)` (`BoundedMapMath.cs:54-58`).
   `pixelSize`/`textureSize` from `Minimap.instance` (as `ContributeLocalSurvey` reads them,
   `cs:358-359`); if no Minimap (headless), return (ingest is a client act, like contribute).
2. `var merged = ReadSharedSurvey() ?? new SurveyData();` (`cs:498`).
3. For each carried item `it` in `user.GetInventory().GetAllItems()` with `IsLocalMap(it)` &&
   `LocalMap.IsImprinted(it)`:
   - `LocalMap.TryGetBoundOrigin(it, out var o)` (`LocalMap.cs:349`); compute `mcx =
     WorldToCellX(o.x,…)`, `mcy = WorldToCellY(o.z,…)`.
   - **Bound-to-THIS-table test:** `mcx == tcx && mcy == tcy`. 🔵 Grid-cell equality (not float
     equality) is what absorbs sub-cell drift and makes *"a Table rebuilt at the same spot
     re-adopts its old maps for free"* (design §4.0a, grounded — `BoundKey` snaps to the 64 m grid;
     `ComputeWindow` uses the same `WorldToCellX/Y`). A Table rebuilt at a **different** cell does
     NOT match → orphans those maps (🟡 accepted per design §4.0a; identity-keying would need a new
     persisted Table-ID — flag only if Daniel wants it).
   - `var s = LocalMap.ReadSurvey(it); if (s != null) merged.MergeFrom(s, out _);` — OR-merge the
     map's fog + union its pins into the Table survey (`SurveyData.cs`; the grid-match guard inside
     `MergeFrom` is satisfied because same-cell ⇒ same `ComputeWindow` ⇒ same `Size`/origin).
     🔧 **BUILD DELTA (card t_9c54d492):** the shipped `MergeFrom` guard previously refused on a raw
     **0.5 m** origin-proximity test (`Mathf.Abs(OriginX − other.OriginX) > 0.5f`), which would have
     **refused** a Table rebuilt a few metres off even though it lands in the SAME 64 m cell — the
     exact AT-INGEST-REBUILD case. The guard is now the grid-cell-equality test
     `BoundedMapMath.SameOriginCell` (`BoundedMapMath.cs`), making the "same-cell ⇒ mergeable"
     invariant this section relies on literally true. Behaviour-preserving for the contribute path
     (it always re-captures at the exact same Table transform ⇒ bit-identical origins). See §12.
4. If anything merged, `PersistSurvey(merged)` (owner-routed, `cs:525`) and a Center message
   (`user.Message(…, "Maps synced to table")`).

🔵 **What "then runs the existing §4 sync" means in CODE (honest grounding).** The design §4 frames
a bidirectional global↔local sync. The **shipped** code realizes it as two directed writes, not a
symmetric pass: (a) **player-global → Table** = `ContributeLocalSurvey` (already on Use,
`cs:340-380`); (b) **Table → map** = `LocalMap.Imprint` on the hotbar-imprint gesture
(`SurveyorTableTag.TryImprintSlot:403` → `LocalMap.Imprint:300`). There is **no** Table→global or
global→map-on-Use write in code, and **none is needed**: the player's `m_explored` already **is**
the global map and `ContributeLocalSurvey` already captured it, so the global has, by construction,
a superset of what the player walked. This card adds the **third** directed write — (c) **map →
Table** (`IngestBoundCarriedMaps`) — closing §4.0a. Do **not** add a phantom "global writeback";
the global is already whole. The distinct value of (c): a Table **rebuilt** at the same spot (empty
survey) re-absorbs its carried maps (AT-INGEST-REBUILD), and a map carrying fog the **current** user
never personally explored (traded in, or surveyed by another Kit-wearer) flows into the Table.

🟡 **Sub-decision — leave the imprint gesture as-is.** §4.0a is explicit that *"the hotbar-imprint
gesture is not made vestigial by live field-update"*: imprint still **births** a blank map's
region+name (`TryImprintSlot`), and Table-Use is now **also** the ingest point. Ingest does **not**
imprint and does **not** require the pressed-slot gesture — it runs on every named-Table Use over
**all** bound carried maps. No change to `TryImprintSlot`.

---

## 6. nomap-OFF — the degenerate write-set (design §6)

In nomap-OFF the Cartographer's Kit is non-functional (design §6 item 1: not craftable, no
function). So there is **no Kit-gated plural write-set**; the **only** writeable local map is the
**equipped** one (design §6 NOTE 2026-06-24: *"equipped → writes to both"* is the nomap-off special
case). `LiveFieldWrite.Tick` branches on the live `Game.m_noMap` flag (the same flag the controller
gates the disc on, `LocalMapController.cs:174`):

- **`Game.m_noMap == true` (nomap-ON, SBPR default via `NoMapEnforcer`):** the full §2 rule —
  write-set = all carried imprinted in-region maps, gated on `IsWearingKit`.
- **`Game.m_noMap == false` (nomap-OFF):** write-set = **just the equipped imprinted map** (if any),
  via `LocalMapController.Instance?._equippedMap` (or a local `GetEquippedLocalMap` scan), **not**
  Kit-gated. Still in-region-tested + `IsImprinted`-tested. Render axis is unchanged (the disc stays
  bound to the vanilla global in nomap-off — controller §5/§4.4).

✅ **RESOLVED — bypass TAKEN in this impl PR (card t_9c54d492).** The existing `UpdateExploreGate`
Prefix (`CartographersKit.cs`) did **not** check `Game.m_noMap`, so in a **manually** nomap-OFF world
it still gated the **global** `m_explored` write on wearing the Kit — contradicting design §6 item 3
(*"Map revealing always works WITHOUT the Cartographer's tools, writing to the global map"*). This
predated #266 and only bit a host who deliberately lifts the `NoMap` global key (SBPR servers are
nomap-ON by default via `NoMapEnforcer`, so global reveal is Kit-gated **by design** there). As §6
recommended, the **2-line bypass is folded into THIS PR**: `if (!Game.m_noMap) return true;` at the
top of the Prefix, immediately after the local-player guard. So in nomap-OFF the global reveals
without the Kit (AT-LIVE-NOMAPOFF / AT-LIVE-GLOBAL honest in nomap-off), while the nomap-ON Kit gate
(AT-KIT-*) is untouched (those worlds take the `IsWearingKit` path). Clean-side: reads the base-game
`Game.m_noMap` flag. See §9 (AT-LIVE-NOMAPOFF) and §12.

---

## 7. No UI render work expected — CONFIRMED from code

Task item 5, **confirmed**: the viewer re-reads the survey blob on a 0.25 s cadence on **both**
surfaces, so a mutated blob auto-reflects with **no render change**:

- **Minimap disc:** `LocalMapController.Update` (throttled 0.25 s) calls `DriveMinimapDisc(_provider)`
  every poll while bound (`cs:173-179`), which does `LocalMap.ReadSurvey(provider)` (`cs:206`) and
  re-binds the disc to the freshly-read survey (`CartographyViewer.BindMinimap`, `cs:218-228`).
- **Full view:** while a field map is equipped + the field viewer is open, the same poll calls
  `RefreshOpenView(equippedMap)` (`cs:192-194`) → `LocalMap.ReadSurvey(map)` (`cs:351`) →
  `CartographyViewer.Refresh` (`cs:355-363`).

Because both already pull `ReadSurvey` (→ `m_customData[sbpr_map_blob]`) each cycle, the §2 blob
mutation appears within ≤0.25 s on whichever surface is showing — the "equip the map, watch it fill
as you walk" of design §3.2a — with **zero** `MapViewer.cs`/`MapSurface.cs` change (AT-LIVE-RENDER).

> **🔴 This is also why §3 locks direct-blob-mutation over the working-set.** The "render
> auto-reflects" guarantee holds **because the blob is the single source of truth** the viewer
> already reads. An in-RAM working-set would break this guarantee (the cache, not the blob, would be
> current) and force a viewer/`ReadSurvey` reroute — so the working-set alternative is *both* more
> code *and* a render change, exactly what this confirmation lets us avoid.

### 7.1 Reconciliation with the superseded §2I render-overlay (full mapping)

| Concern | OLD §2I render-overlay (PR #131, never built) | NEW #266 write axis (this spec) |
|---|---|---|
| Storage | snapshot **never mutated**; live cells are render-time only | blob **genuinely grows** (`WriteSurveyBlob`, §2.4) |
| Persistence | none — relog/handoff snaps back to imprint | survives relog + travels with the item (rides profile save / item ZDO, design §3.1) |
| Set | single equipped map (the render-provider) | **plural** — all carried imprinted in-region maps |
| Global | n/a (overlay was each reader's own `m_explored`) | co-writes global (vanilla already does it; §1) |
| Where | `MapViewer.PaintShroudMask` merged-fog copy (render) | `LiveFieldWrite.Tick` blob mutation (storage) |
| Render change | yes (the merge lived in the paint path) | **none** (§7 — blob is truth, viewer unchanged) |

**Action:** mark the §2I render-overlay region in `cartography-impl-spec.md` superseded with a
banner pointing here (done in the same PR — see §9). No code removal (the overlay was never built;
`MapViewer.cs` has no merged-fog path). The user-visible effect §2I aimed at (held map fills as you
walk) is **delivered** here, correctly, by storage growth.

---

## 8. Files touched + clean/dirty

| File | Change | Clean? |
|---|---|---|
| **`Features/Cartography/LiveFieldWrite.cs`** (NEW) | the write-set tick (§2): Kit/nomap gate, in-region scan, per-map `CaptureWindow`→`MergeFrom`→`WriteSurveyBlob`, ~2 s sub-throttle | additive; reads base-game `Minimap.m_explored` + own math — ADR-0001 ✓ |
| **`Features/Cartography/LocalMapController.cs`** | one call `LiveFieldWrite.Tick(player)` in the existing throttled poll (`cs:~166`); expose `_equippedMap`/provider to the writer if needed (already fields) | base-game reads only ✓ |
| **`Features/Cartography/LocalMap.cs`** | add `WriteSurveyBlob` (§2.4) — `Imprint`'s blob line extracted, Bound/Name keys untouched | own storage helper ✓ |
| **`Features/Cartography/SurveyData.cs`** | add `MergeFrom(other, out bool changed)` (§2.3) + keep `MergeFrom(other)` 1-line overload | own type ✓ |
| **`Features/Cartography/MinimapFog.cs`** (NEW, tiny) **or** make `SurveyorTableTag.ReadExplored` reusable | single cached-`FieldInfo` `ReadExplored(Minimap)` shared by the Table + the writer (§2.1 step 2) | reflection on a stable base-game field — the established spike idiom ✓ |
| **`Features/Cartography/SurveyorTableTag.cs`** | `IngestBoundCarriedMaps` (§5) called in `Interact` after `ContributeLocalSurvey`; route its `m_explored` read through the shared helper | base-game reads + own ZDO ✓ |
| **`Features/Cartography/CartographersKit.cs`** (conditional, §6) | IF the nomap-off global bypass is taken: `if (!Game.m_noMap) return true;` at the Prefix top | base-game read ✓ |
| **`docs/v2/planning/cartography-impl-spec.md`** | supersede banner on the §2I render-overlay region (§9) | docs |
| **`docs/v2/planning/{index,README}.md`** | register this spec | docs |

**No** `SpecCheck.cs` change (§0). **No** new prefab / recipe / wire key. **No** `MapViewer.cs` /
`MapSurface.cs` / `CartographyViewer.cs` change (§7). net48, 0 warnings.

---

## 9. Named acceptance tests (observable; close only on Daniel's in-game check)

`logs-green ≠ playable` — every AT below is verified **in-game**, not from server logs.

- **AT-LIVE-WRITE-1** — Kit worn, an imprinted Local Map carried (equipped or not). Walk into
  unexplored ground inside the map's 1000 m region → the held map's stored survey **grows**
  (the new ground shows next time you open/equip it, AND persists). Distinguished from the old
  overlay: it is the **stored artifact** that grew (see AT-LIVE-SUPERSEDE).
- **AT-LIVE-NOKIT** — no Kit worn → walking reveals **nothing** new on any carried map (the map is
  a frozen snapshot; design "maps age" default state). The §2.1 Kit gate + §2.3 dirty-check ⇒ zero
  writes.
- **AT-LIVE-MULTI** — two carried imprinted maps whose regions **overlap** at the player's
  position → **both** grow (write-set is plural, not the single render-provider).
- **AT-LIVE-OUTREGION** — a carried imprinted map whose region you have **left** (player outside its
  1000 m disc) does **NOT** grow while you explore elsewhere (the in-region pre-filter + disc clip).
- **AT-LIVE-GLOBAL** — the personal **global** map also grows from the same walk (nomap-ON: vanilla
  `Explore` writes `m_explored` because the Kit gate passes). Verify via a later Table-use survey or
  the eventual Eye-of-Odin. *(nomap-OFF: depends on the §6 OPEN bypass — see AT-LIVE-NOMAPOFF.)*
- **AT-INGEST-1** — Use a named Surveyor's Table while carrying a Local Map **bound to that Table**
  that has field discoveries the Table lacks → the Table's shared survey **gains** those cells/pins
  (visible in the Table view immediately).
- **AT-INGEST-REBUILD** — destroy a Table, rebuild it at the **same spot** (empty survey), carry a
  map that was bound to the old Table, Use it → the rebuilt Table **re-adopts** the map's survey
  (grid-cell match). Rebuilt at a **different** cell → does NOT re-adopt (accepted orphan).
- **AT-LIVE-PERSIST** — walk with Kit + carried map (never opening it), **relog**, then open the
  map → the accumulated ground is **there** (the blob was mutated + rode the profile save), not the
  original imprint snapshot.
- **AT-LIVE-IDENTITY** — after live writes, the map's **name** (`Local map for <Table>`) and
  **region/bound-origin** are unchanged (only `sbpr_map_blob` was rewritten; Bound/Name keys
  untouched).
- **AT-LIVE-ALIGN** — a cell lit on a local map after a live write is lit on the **global** map at
  the **same world coordinate** (the §4.1 1:1 invariant; `BuildWindowedFog` reads the global index).
- **AT-LIVE-RENDER** — with a bound map's disc/full-view **open**, the new ground appears within
  ~0.25 s of being walked (no equip/reopen needed) — confirming the viewer's existing `ReadSurvey`
  poll auto-reflects the mutated blob, **no render code changed**.
- **AT-LIVE-SUPERSEDE** — the growth is **stored, not a render overlay**: hand the grown map to a
  **Kit-less** second player (or drop+pick-up) → they see the **accumulated** ground (the blob
  carries it), NOT a snap-back to the imprint snapshot. This is the behavior the old §2I overlay
  could **not** produce and is the point of #266.
- **AT-LIVE-UNIMPRINTED** — a **blank** (unimprinted) carried map is never written (excluded by
  `IsImprinted`); it stays blank until imprinted at a Table.
- **AT-LIVE-NOMAPOFF** — in a manually nomap-OFF world: only the **equipped** map receives the live
  write (no Kit-gated multi-set); the disc stays bound to the vanilla global. *(Global passive
  reveal in nomap-off is correct IFF the §6 gate bypass is taken; if deferred, document that
  global reveal is still Kit-gated in nomap-off and that's the known limitation.)*
- **AT-LIVE-CLEAN** — Release build is 0 warnings; SpecCheck recipe count unchanged (+0); no
  `MapViewer`/`SpecCheck`/recipe edits.

---

## 10. Routing + sequencing

- **Clean-side → `engineer-ui`** — this card lives entirely in the `Features/Cartography/` viewer +
  controller cluster (`LiveFieldWrite`, `LocalMapController`, `LocalMap`, `SurveyData`,
  `SurveyorTableTag`), the same worker that holds the cartography surface. No systems/prefab work.
- **No dependency on an unbuilt render** (unlike the old §2I, which sequenced behind §2E). The blob
  is the truth the **already-shipped** viewer reads, so this builds on `main` @ 37fa8a8 directly.
- **SpecCheck impact: +0** (behavior). Spec + code move together in the impl PR (AGENTS.md rule).
- **Engineer impl card** (child `t_9c54d492`, already created) auto-promotes when this spec card
  completes — it builds straight from §2–§9 here.

---

## 11. Provenance

- Design locked by **Daniel 2026-06-24**, merged **PR #266** (`main` @ 37fa8a8):
  [`../../design/map-provider-model.md`](../../design/map-provider-model.md) §3.2a (RENDER vs WRITE
  axes), §4.0a (imprint births region+name / Table-use ingests), §5 (Kit write-target widened), §6
  (nomap-off degenerate case), §9 (2026-06-24 closure block); reframed in
  [`../../design/cartography-v2.md`](../../design/cartography-v2.md) §4.3 (Kit-gated ageing).
- **Supersedes** the §2I render-overlay half of `cartography-impl-spec.md` (issue 5, card
  t_ed0f0066 / PR #131): a render-time `snapshot ∪ live m_explored` overlay that never mutated
  storage. #266 reverses it to genuine artifact mutation (§7.1).
- All API surfaces grounded against `main` @ 37fa8a8 (`CartographersKit.cs`, `LocalMap.cs`,
  `SurveyData.cs`, `BoundedMapMath.cs`, `SurveyorTableTag.cs`, `LocalMapController.cs`,
  `LocalMapBootstrapPatch.cs`, `SpecCheck.cs`) — file:line citations inline. Architect spec-pass,
  card **t_d46b3398**.

---

## 12. As-built notes (impl card t_9c54d492 — review-required)

Built straight from §2–§9 on a branch off `main` @ `00b389b` (PR #267 merge). Build floor met:
`dotnet build -c Release` → **0 warnings / 0 errors** (`<TreatWarningsAsErrors>` ON); test suite
**314 passing** (303 baseline + 11 new). SpecCheck untouched (**+0**, §0). No `MapViewer.cs` /
`MapSurface.cs` / `CartographyViewer.cs` change (§7 confirmed).

**Files as built** (matches §8, with the conditional §6 row taken):
- **NEW `Features/Cartography/LiveFieldWrite.cs`** — the write-set tick (§2): ~2 s sub-throttle,
  §6 nomap branch (nomap-ON ⇒ Kit-gated plural set via `IsWearingKit`; nomap-OFF ⇒ equipped-only,
  un-gated), in-region scan, per-map `CaptureWindow`→`MergeFrom(out changed)`→`WriteSurveyBlob`,
  pins passed empty (§2.2).
- **NEW `Features/Cartography/MinimapFog.cs`** — the shared cached-`FieldInfo` `ReadExplored(Minimap)`
  (§2.1 step 2). `SurveyorTableTag`'s private `ReadExplored` + its `_fiExplored` field were **removed**
  and re-pointed here, so there is exactly **one** reflection path for the personal fog.
- **`LocalMap.cs`** — `WriteSurveyBlob` (§2.4): `Imprint`'s blob line extracted; Bound/Name keys
  untouched (AT-LIVE-IDENTITY).
- **`SurveyData.cs`** — `MergeFrom(other, out bool changed)` (§2.3) + 1-line `MergeFrom(other)`
  overload; fog loop delegates to the new pure `BoundedMapMath.OrMergeFog` (so the shipped
  dirty-check IS the headless-tested one).
- **`BoundedMapMath.cs`** — three pure helpers added: `InRegionForLiveWrite` (§2.1 step 3),
  `SameOriginCell` (§5), `OrMergeFog` (§2.3). All engine-free → unit-tested in
  `tests/LiveFieldWriteMathTests.cs`.
- **`LocalMapController.cs`** — one call `LiveFieldWrite.Tick(player)` in the throttled poll, after
  the provider machine (§4 ordering).
- **`SurveyorTableTag.cs`** — `IngestBoundCarriedMaps` (§5) in `Interact` after `ContributeLocalSurvey`;
  contribute path re-pointed to `MinimapFog.ReadExplored`.
- **`CartographersKit.cs`** — the §6 `if (!Game.m_noMap) return true;` bypass folded in (§6 RESOLVED).

**Two build deltas from the spec's stated mechanism — both make a §-claim literally true (flagged
for review):**
1. **Merge guard → grid-cell equality (§5).** The shipped `SurveyData.MergeFrom` guard had refused on
   a raw **0.5 m** origin-proximity test, which would have **blocked** AT-INGEST-REBUILD (a Table
   rebuilt a few metres off but in the same 64 m cell). Replaced with `BoundedMapMath.SameOriginCell`,
   making the spec's "same-cell ⇒ mergeable" invariant true. **Behaviour-preserving for the existing
   contribute path** (it always re-captures at the exact same Table transform ⇒ bit-identical origins;
   the 303 baseline tests + the live contribute path are unaffected). This widens which Table-rebuilds
   re-adopt maps — a behaviour change confined to the ingest/rebuild case the spec explicitly wanted.
2. **Pure `OrMergeFog` extraction (§2.3).** The flip-detecting OR was extracted into `BoundedMapMath`
   (engine-free) so the §2.3 dirty-check is testable headless and the shipped path calls it — no
   second copy. Pure refactor of the loop the spec inlined.

**Pins-in-hot-path (§2.2) left OUT by default**, as specced — carried maps persist pins via the Table
ingest, not the per-tick fog write. The 🟡 reversible knob (pass live `CollectShareablePins` into the
per-tick `CaptureWindow`) is untaken; flag on in-game review if home-flow-only pin persistence feels
wrong.

**Verification honesty (AGENTS.md "logs-green ≠ playable"):** compiles clean + unit tests green +
the write/ingest/gate logic is grounded against the decomp and the shipped APIs. **NOT yet verified
in-game** — every AT-LIVE-* / AT-INGEST-* in §9 closes only on Daniel's in-game check. PR self-blocks
`review-required`; Starbright merges + build-verifies under delegated authority and holds the in-game
AT for Daniel.
