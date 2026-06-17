---
title: "Traveller's Cache — public + per-player private trailside storage (design + architecture)"
status: proposed
purpose: "Design + buildable architecture for the Traveller's Cache: a placeable trailside chest holding ONE shared public inventory plus a per-player private compartment, so travellers can leave gifts for the next wanderer while keeping their own stash isolated. Grounds the persistence/destroy/keying model on the in-tree Surveyor's Table ZDO-blob precedent (no decomp on this box — vanilla symbols are cited against what the shipped code already compiles against, novel UI plumbing is flagged VERIFY-AGAINST-DECOMP). Carries the five OPEN design knobs (tier/gating, placement tool, public size, private size, destroy-warning hover) for Daniel before a version-scoped impl-spec is cut. Net-new design — NOT in requirements.md v1 scope. Card t_b6d133e1."
---

# Traveller's Cache — public + per-player private trailside storage

> **What this doc is.** The locked-as-far-as-it-can-go *what* and the buildable
> *how* for the Traveller's Cache, minus five design knobs only Daniel can set
> (§9). It is deliberately a `docs/design/` doc, not a `docs/v3/planning/`
> impl-spec, because **tier is one of the open knobs** — filing it under a
> version dir would presume the answer. Once Daniel resolves §9, this graduates
> to a version-scoped impl-spec (the `swamp-detection-item.md` →
> `v3/planning/sunstone-lens-impl-spec.md` path), and the chosen tier picks the
> dir. Until then everything here is buildable except the five flagged knobs.

> **Lineage.** This supersedes the `nomap.md` §4 sketch ("Traveler's Storage").
> Two corrections that sketch predates: (1) **build ADDITIVELY, do NOT wrap a
> vanilla `Container`/clone a chest** — ADR-0006 postdates the sketch and the
> "wrap two Container instances" idea it floats is the clone-then-fight pattern
> the ADR retires (§3); (2) the durable persistence path is the **in-tree
> Surveyor's Table ZDO-blob precedent** (`SurveyData` + `SurveyorTableTag`),
> which did not exist when the sketch was written. Read this, not `nomap.md` §4.

## 0. Grounding posture (read first — it changes how to read the citations)

The decomp of `assembly_valheim` and the wiki corpus are **NOT present on this
architect box** (`VALHEIM_MANAGED` is empty here; `vprefab` is non-functional).
This doc therefore does **not** invent decomp line numbers. Every vanilla symbol
named below is in one of two trust tiers, and the tier is stated inline:

- 🟢 **COMPILES-AGAINST** — the symbol is already used by *shipped* Trailborne
  code, which builds clean against the real game assemblies in CI. That is proof
  the symbol exists with that shape on our game version. These carry an in-tree
  `file:line` citation, not a decomp line.
- 🟡 **VERIFY-AGAINST-DECOMP** — the symbol is part of the novel surface (the
  dual-compartment `Container`/`InventoryGui` interaction) that no shipped
  Trailborne code touches yet. It is named from modding general knowledge and
  the `nomap.md` §4 sketch, and **must be confirmed against the decomp by an
  engineer who holds the corpus** before implementation. Every 🟡 is collected
  in §8 so the implementer has one checklist.

This split is the honest version of the repo's RULE 5 (retrieve before you
claim) given the corpus isn't on this machine. It is also why this is a
*design+architecture* doc handed to an engineer with decomp access, not a
turn-key impl-spec.

## 1. The fantasy (why it earns a place in Trailborne)

A Traveller's Cache is a small chest you plant beside a trail. It has two faces:

- **A public shelf** — anyone can take from it and leave to it. You drop a spare
  health mead, three cooked boar, a torch; the next wanderer who finds your
  cairn-marked trail takes what they need and leaves what they can. It is a
  *gift economy node* for a server that has embraced the Explorer role: the
  trail is not just navigation, it is a place people leave things for each other.
- **A private drawer** — keyed to *you*. The same physical cache, opened by a
  different player, shows a different private drawer. It is your personal stash
  at this waypoint: stuff you cached here and only you can retrieve.

This is squarely on-thesis (`trailborne-vision.md`): the trail becomes
*inhabited*. Painted Signs say "this way"; Cairns say "someone maintains this
route"; a Traveller's Cache says "someone was here, and left something for you."
The public/private split is the whole design — a plain shared chest already
exists in vanilla; the private-drawer-per-player is the novel, Trailborne-shaped
twist that makes it a *traveller's* cache and not just a roadside box.

**The destroy contract is part of the fantasy, not a footnote.** When the cache
is destroyed, the *public* shelf spills to the ground (vanilla chest behaviour —
the gifts were always going to be found by someone). The *private* drawers are
**gone, discarded into the void** — "traveller beware." This asymmetry is
deliberate: it keeps a private drawer from being a safe-deposit box (you cannot
trust a roadside cache with your irreplaceable things), and it makes the public
shelf the emotionally-correct thing that survives (gifts outlive the giver).

## 2. Data model — one public inventory + a player-keyed map of private ones

The cache's entire state is one serialisable record, persisted as a compressed
blob in the piece's own ZDO (§4). In-memory shape:

```
TravelerCacheData
  int                     WireVersion        // bump-and-branch on layout change
  Inventory               Public             // the shared shelf (one, for everyone)
  Dictionary<long, byte[]> Private           // playerID -> that player's serialised drawer
```

- **`Public`** is a single `Inventory` (🟡 the vanilla item-container type;
  VERIFY §8-A). Everyone who opens the cache sees and mutates the *same* `Public`.
- **`Private`** maps a **player ID** (`long`) to that player's drawer, stored as
  the drawer's own serialised bytes (lazily deserialised to an `Inventory` only
  when that player opens the cache). One entry per player who has *ever* opened
  the private side here. A player with no entry yet gets a fresh empty drawer
  allocated on first open (§5).
- **The key is `Player.GetPlayerID()` → `long`** — 🟢 COMPILES-AGAINST: shipped
  code reads exactly this for pin ownership
  (`Features/Cartography/SurveyorTableTag.cs:362`,
  `Player.m_localPlayer.GetPlayerID()`). It is the same stable `long` the card
  specifies and the same identity vanilla `Bed` ownership uses. Crucially it is
  **the persistent profile ID, not a session/ZDO id**, so a player's drawer
  survives their relog and reconnect with a new session — the AC's "persist
  across relog" requirement rides on choosing this key and no other.

> **Why store `Private` as `byte[]` per player, not `Inventory` per player.**
> Two reasons. (1) **Serialisation cost is pay-per-open:** we only need the live
> `Inventory` object for the *one* drawer being opened right now; the rest stay
> as opaque bytes, so a cache used by 8 players doesn't hold 8 live `Inventory`
> objects hot. (2) **The blob round-trips losslessly even for players who never
> log in again:** an absent player's drawer is bytes we re-emit verbatim on every
> save without ever needing their `Inventory` to deserialise cleanly on our
> client. This mirrors how `SurveyData` carries pins it doesn't need to inflate.

## 3. Prefab construction — ADDITIVE (ADR-0006), the Surveyor's Table pattern

The cache is a placed, networked, damageable build piece with a custom
MonoBehaviour and a ZDO blob. That is **exactly** the Surveyor's Table shape, and
it is built the same way — `Assets.ConstructPieceShell` + a grafted vanilla
visual + our Tag. We do **NOT** `Instantiate` a vanilla `piece_chest*` and bolt
extra inventories on; that is the clone-then-fight anti-pattern ADR-0006 retires,
and it would drag the vanilla `Container` MonoBehaviour (single-inventory, ZDO-
keyed) we explicitly do not want as our base.

```
Features/TravelerCache/
  TravelerCache.cs        — prefab registration + recipe wiring (the SurveyorsTable.cs analogue)
  TravelerCacheTag.cs     — the MonoBehaviour: Hoverable + Interactable, owns the two
                            compartments, ZDO persistence, destroy hook (the SurveyorTableTag analogue)
  TravelerCacheData.cs    — the serialisable record + versioned Serialize/Deserialize
                            (the SurveyData.cs analogue)
```

**Registration (`TravelerCache.RegisterPrefabs`)** — verbatim the Surveyor's
Table recipe (`Features/Cartography/SurveyorsTable.cs:67-139`), differing only in
names, visual donor, and the Tag attached:

1. 🟢 `Assets.ConstructPieceShell(CacheName, ShellEffectDonor)` builds the
   `ZNetView + Piece + WearNTear + BoxCollider` skeleton from scratch
   (`Runtime/Assets.cs:1061`). `ShellEffectDonor = "wood_floor"` or `"stone_floor"`
   per the chosen tier material (the Surveyor's Table uses `"stone_floor"`;
   `Assets.cs:1050-1058` reference-copies hit/destroy/place EffectLists off it).
2. 🟢 `Assets.GraftVisualSubtree(...)` grafts a vanilla chest's *visual mesh* as a
   ZNetView-free cosmetic child (`Assets.cs:923`) — reading the donor, never
   instantiating its networked root. 🟡 The donor prefab name + visual child name
   must be read with `vprefab inspect` on a box that has the corpus (VERIFY §8-B);
   the Surveyor's Table did exactly this for `piece_cartographytable`/child `"new"`.
3. Set `Piece.m_name`, `m_description`, `m_category = Misc` (if Spade-placed —
   §9-Q2), `m_craftingStation = null`, `m_resources = BuildResources()`.
4. 🟢 `go.AddComponent<TravelerCacheTag>()` — the survey-behaviour analogue.
5. 🟢 `Assets.RegisterPrefabInZNetScene(go)` (`Assets.cs:377`).

The Tag implements `Hoverable, Interactable` **directly** (the in-production
`CairnInteractable`/`SurveyorTableTag` pattern, `Features/Cairns/CairnInteractable.cs:14`)
rather than wiring a vanilla `Container`'s `Switch` — we own the open path so we
can branch public-vs-private on the modifier key (§5).

**Why not just attach a vanilla `Container`?** A vanilla `Container` is
single-inventory and owns its own open UI; it has no concept of a per-player
drawer. We would be fighting it (suppressing its open, shimming its inventory)
the moment we wanted the second compartment — the exact subtractive trap ADR-0006
exists to prevent. Building the Tag additively means the dual-compartment logic
is *ours*, with no donor behaviour to suppress.

## 4. Persistence — compressed blob in the piece ZDO, owner-authoritative write

This is the `SurveyData` + `SurveyorTableTag` persistence model applied verbatim;
it is the in-tree, server-restart-proven path (`SurveyorTableTag.cs:495-554`).

- **Storage slot:** 🟢 `ZDOVars.s_data` (byte[]) on the cache's own ZDO — the
  same slot the Surveyor's Table and vanilla `MapTable` use
  (`SurveyData.cs:10-13`, `SurveyorTableTag.cs:504`). Survives dedicated-server
  restart for free because it is a normal persistent ZDO field.
- **Read:** 🟢 `zdo.GetByteArray(ZDOVars.s_data)` → `Utils.Decompress` →
  `TravelerCacheData.Deserialize` (mirror `SurveyorTableTag.ReadSharedSurvey`,
  `:498-516`). A null/empty/old-version blob deserialises to a fresh empty cache,
  never a crash (mirror `SurveyData.Deserialize`, `SurveyData.cs:277-324`).
- **Write — exactly one owner write, via RPC if we aren't the owner:** 🟢 mirror
  `SurveyorTableTag.PersistSurvey` (`:525-540`): if `nview.IsOwner()` →
  `zdo.Set(ZDOVars.s_data, Utils.Compress(data.Serialize()))`; else
  `nview.InvokeRPC("SBPR_CacheData", new ZPackage(compressed))` and the owner-only
  `RPC_CacheData` (`:547-554` analogue) commits the single `zdo.Set`. This is what
  stops two players writing the cache at once from clobbering each other. For the
  **private drawer** the owner-write discipline matters doubly: a non-owner
  player's drawer edit must round-trip to the owner so the authoritative blob
  carries it — see §5 for the read-modify-write that keeps other players' drawers
  intact across that RPC.
- **Serialisation (`TravelerCacheData.Serialize`):** 🟢 `ZPackage` + the
  versioned-layout discipline of `SurveyData.Serialize` (`SurveyData.cs:242-270`):
  ```
  int    WireVersion
  <public inventory bytes>           // §8-C: vanilla Inventory.Save(ZPackage) if available, else item-tuple loop
  int    privateCount
  repeat: long playerID ; int len ; len bytes (that player's drawer blob)
  ```
  🟡 The public-inventory encode/decode is the one genuinely novel serialisation
  step (VERIFY §8-C) — everything around it is the proven `SurveyData` envelope.
- **Linear growth (documented, AC #7):** the blob grows by one drawer per
  *distinct* player who opens the private side. On a bounded private server
  (≤10 players, the target) this is a hard, small cap. See §7 for the public-
  server eviction requirement that gates any Thunderstore release.

## 5. The open/interaction model — branching public vs private

The Tag's `Interact(Humanoid user, bool hold, bool alt)` (🟢 the
`CairnInteractable.Interact` signature, `CairnInteractable.cs:68`) chooses which
compartment to open. **The mechanism for showing an `Inventory` in the vanilla
chest UI is the one big VERIFY (§8-D)** — vanilla `Container.Interact` calls into
`InventoryGui.Show(container)`, and we need to drive that UI with an `Inventory`
we own rather than a `Container` component. Two candidate routes, for the
engineer-with-decomp to choose:

- **Route A — borrow the vanilla `InventoryGui` container view.** If
  `InventoryGui.Show` (or its container-panel entry point) can be handed an
  object exposing the container `Inventory`, we present whichever compartment was
  selected. 🟡 VERIFY the exact signature and whether it requires a `Container`
  or just an `Inventory` (§8-D). Lowest-effort if the seam exists.
- **Route B — a thin throwaway `Container` shim per open.** Construct a transient
  `Container` whose `m_inventory` we point at our compartment, show it, and on
  close copy the (possibly mutated) `Inventory` back into our record and persist.
  🟡 VERIFY that a `Container` can be shown without its own ZDO/ZNetView write
  path firing (we must not let it persist into its own ZDO — *our* blob is the
  source of truth). This is more moving parts but sidesteps needing a private
  `InventoryGui` seam.

Either route, the **selection** is the design surface:

- **Default press (no modifier) → PUBLIC shelf.** The common case — leave/take a
  gift — is the zero-friction one.
- **Modifier press (the `alt` arg, i.e. the vanilla "alternate use"/crouch
  modifier the cache hover advertises) → PRIVATE drawer.** 🟢 the `alt` bool is
  already the second-action channel shipped code uses (`CairnInteractable.cs:75`
  branches on `alt` for its secondary action). On private open: look up
  `data.Private[user.GetPlayerID()]`; if absent, allocate a fresh empty drawer of
  the private size (§9-Q4) and insert it; deserialise to a live `Inventory`, show it.

> **The read-modify-write that protects other drawers (load-bearing).** When a
> non-owner edits their private drawer, the change must land in the *owner's*
> authoritative blob without erasing the drawers the owner holds for everyone
> else. So a drawer edit is never "send my whole TravelerCacheData" — it is
> "send (playerID, my new drawer bytes)" and the **owner** does
> `data = ReadBlob(); data.Private[playerID] = bytes; WriteBlob(data)`. The owner
> read-modify-writes its own up-to-date record, so concurrent edits to *different*
> drawers compose instead of clobbering. 🟡 This argues for a second, finer RPC
> (`SBPR_CachePrivatePut(long playerID, ZPackage drawerBytes)`) alongside the
> coarse whole-blob `SBPR_CacheData` — VERIFY the RPC registration shape against
> `nview.Register`/`InvokeRPC` usage (the Surveyor's Table registers one; we
> likely want two). The public shelf can use the coarse whole-blob path since it
> is a single shared inventory where last-writer-wins is acceptable (same as a
> vanilla chest two players poke at once).

**Hover text** (🟢 `GetHoverText`, `CairnInteractable.cs:33`) advertises both
actions and — pending §9-Q5 — the destroy warning, e.g.:

```
Traveller's Cache
[E] Open public cache
[Shift+E] Open your private drawer
⚠ If destroyed, public items drop — private drawers are lost forever.
```

(Tokens resolved through `Localization.instance.Localize` on the way out, the
shipped fix for the `$KEY_Use` leak — `CairnInteractable.cs:58-65`.)

## 6. Destroy contract — public drops, private discarded (AC #5)

🟢 `WearNTear.m_onDestroyed` is a public `Action` fired owner-side on REAL
destruction (decay/raid/demolish — *not* a zone unload), and shipped code already
subscribes/unsubscribes it correctly (`Features/MarkerSigns/MarkerSignTag.cs:88-111`,
with the explicit "Unity `OnDestroy` is NOT our signal, zone-unload fires it too"
discipline). The cache Tag subscribes the same way:

```
Awake:    wnt.m_onDestroyed += OnCacheDestroyed   (idempotent, MarkerSignTag.cs:93)
OnDestroy: wnt.m_onDestroyed -= OnCacheDestroyed   (detach only; NOT the drop signal)
```

`OnCacheDestroyed` (owner-side, real destruction):

1. **Public → ground.** Read the public `Inventory` from the blob and spill every
   item as a world drop at the cache position. 🟡 The vanilla primitive is the
   `Container.DropAllItems`-style loop — `ItemDrop.DropItem` per stack around the
   piece origin (VERIFY §8-E for the exact drop helper; the card names the
   "vanilla `Container.DropAllItems` pattern").
2. **Private → void.** Do nothing with `data.Private`. The blob dies with the ZDO;
   the drawers are gone. This is the intended "traveller beware" loss, not a leak.

Guard the whole callback in try/catch so a drop-side failure never escapes the
WearNTear destroy path (🟢 the shipped `MarkerSignTag.OnSignDestroyed` discipline,
`:120-133`).

## 7. ZDO-blob growth + the eviction requirement (AC #7, gates public release)

The private map grows **O(distinct players who opened the private side)**, each
entry being one serialised drawer (≤ private size × per-item record). The bound:

- **Target deployment (bounded private server, ≤10 players):** worst case 10
  drawers. At, say, a 2×4 private drawer that is trivially small — kilobytes,
  compressed. **No eviction needed; this is the shipped target and AC #7's
  "documented" bar is met by this section.**
- **Public/open server (OUT OF SCOPE to implement, IN SCOPE to document):** the
  map is **unbounded** — every visitor who ever opens the private side leaves a
  drawer forever, even after they never return. On a server with hundreds of
  transient visitors this blob grows without limit and is re-serialised on every
  write. **This is a hard blocker for any Thunderstore/public release** and MUST
  be resolved before one. Stated requirement for that future work:

  > **REQ-CACHE-EVICT (release-gating):** before the Traveller's Cache ships to a
  > public/open server, the private map must have a bounded cap with an eviction
  > policy (candidates: LRU by last-open timestamp; hard cap N drawers with
  > oldest-evicted; or TTL drop of drawers untouched for D days). Eviction means a
  > player *loses* that drawer's contents, so the policy interacts with the
  > destroy contract's "traveller beware" framing and needs its own design pass.

This is the explicit "stated cap/eviction requirement gating any public
Thunderstore release" the card's AC #7 asks for. The implementation of REQ-CACHE-
EVICT is a **separate future card**, not this one (§10).

## 8. VERIFY-AGAINST-DECOMP checklist (the 🟡 surface, one place)

An engineer with the decomp/corpus must confirm these before/during build. None
are exotic — they are the parts no shipped Trailborne code exercises yet, so this
architect box (no corpus) cannot self-verify them per RULE 5. Each names what to
confirm and the fallback if the assumed shape is wrong.

- **§8-A `Inventory` construction + capacity.** Confirm the `Inventory` ctor
  signature (name, sprite, width, height) and that we can `new Inventory(...)` a
  standalone instance not bound to a `Container`. Fallback: if `Inventory`
  demands a backing container, Route B's shim (§5) supplies one.
- **§8-B Visual donor.** `vprefab inspect` a vanilla chest (`piece_chest_wood`,
  `piece_chest`, `piece_chest_private`, `piece_chest_blackmetal`) to get the
  prefab name + visual-child name for `GraftVisualSubtree`, and pick the donor
  matching the chosen tier (§9-Q1). Fallback: any chest mesh; visual polish is
  Daniel-gated in playtest (the Surveyor's Table flagged the same).
- **§8-C Inventory (de)serialisation.** Confirm `Inventory.Save(ZPackage)` /
  `Inventory.Load(ZPackage)` (or the vanilla equivalent the chest uses to put its
  contents in `s_items`) and use it verbatim for both the public shelf and each
  private drawer's bytes. Fallback: hand-roll an item-tuple loop
  (prefab name, stack, quality, durability, custom data) — heavier, but the
  `SurveyData` serializer shows the envelope discipline.
- **§8-D Showing an owned `Inventory` in the chest UI.** The load-bearing one.
  Confirm `InventoryGui.Show(Container)` vs an entry point that accepts an
  `Inventory`, and decide Route A vs Route B (§5). This is the single biggest
  build risk and should be **spiked first** (a throwaway "open an empty owned
  inventory from a placed piece" proof) before the rest is wired.
- **§8-E Item drop on destroy.** Confirm the `Container.DropAllItems` internals /
  `ItemDrop.DropItem` (or `Inventory` → world-drop) helper for spilling the
  public shelf at the piece origin. Fallback: instantiate each item prefab at the
  position and set its stack — the vanilla drop helper just does this in a loop.
- **§8-F RPC registration shape.** Confirm `ZNetView.Register<...>(name, handler)`
  + `InvokeRPC` signatures for the coarse `SBPR_CacheData` and the finer
  `SBPR_CachePrivatePut` (§5). The Surveyor's Table registers one such RPC; mirror
  it. Fallback: a single coarse whole-blob RPC with the owner read-modify-write
  done owner-side from the live in-memory record.
- **§8-G `m_destroyBroken`-style surprises.** Confirm a placed build piece's
  WearNTear destroy path is the only destruction channel (no auto-consume like
  the Sunstone Lens durability trap). A build piece has no durability-drain path,
  so this is expected-clean, but confirm no chest-specific auto-empty fires.

Every 🟡 above has a working fallback, so none is a feasibility risk — they are
*verification* items, not unknowns that could sink the feature. The architecture
(additive piece + ZDO blob + owner-write + destroy hook) is fully grounded in
shipped code; only the chest-UI seam (§8-D) deserves a spike before committing.

## 9. OPEN design knobs — Daniel decides (this is what blocks lock)

Five aesthetic/pacing calls the architect cannot make for you. Each carries a
**lean** (reversible) and the reasoning, so you can thumbs-up or redirect fast.
Nothing below changes the architecture — they parameterise it.

**Q1 — Tier / gating (what unlocks the cache + its recipe materials).**
The cache is biome-neutral architecture; tier is pure pacing. Options: Meadows
(available early, the trail-network fantasy starts at game start), Black Forest
(pairs with the Surveyor's Table / cartography tier when trails first span real
distance), or Swamp (the current v3 working tier).
- **Lean: Black Forest.** The cache is a *trail-network* payoff, and trails only
  start spanning meaningful distance once you are pushing out of Meadows; it also
  slots beside the Surveyor's Table as "the BF tier is when the trail becomes
  shared infrastructure." Meadows feels too early (no one is leaving cross-country
  gifts in the starting meadow); Swamp feels too late for something this social.
  Recipe would then be FineWood/Bronze-tier, echoing the Surveyor's Table band.
- **This also picks the doc's eventual home dir** (`docs/v2/planning/` for BF).

**Q2 — Placement tool: Trailblazer's Spade or Hammer? (Pillar 1 tension — real).**
Design Pillar 1 says everything an Explorer places in the world goes on the
**Spade**, with a stated exception for the Ancient Portal (a "deployable
convenience structure," Hammer). The cache sits on the *line* between those: it
is trail infrastructure (Spade-shaped, like Signs/Cairns/Lamps) but also a
"deployable convenience structure you plant" (the Ancient Portal's Hammer
reasoning).
- **Lean: Trailblazer's Spade.** It is a *trailside* fixture whose whole point is
  marking and provisioning a route — that is the Spade's domain (paths, signs,
  cairns), not the settler's. The Ancient Portal earned its Hammer exception by
  being a *destination* you jump between, not a *wayfinding/route* mark; the cache
  is the latter. Putting it on the Spade keeps Pillar 1 clean and groups it with
  the other trail furniture in one build menu. If Daniel reads it as "campsite
  convenience" rather than "trail mark," it flips to Hammer — but the lean is
  Spade. (If Spade: `Piece.m_category = Misc`, the single Trail tab, per
  `SurveyorsTable.cs:92-96`.)

**Q3 — Public compartment size (nomap Open Q6, never answered).**
`nomap.md` §6 explicitly left this for Daniel: "4×4? Bigger? Smaller (forces
selection of what to leave)?" The size sets the gift economy's texture.
- **Lean: small — 2×3 (6 slots) or 3×3 (9).** A *small* public shelf is better
  design: it forces curation (you leave a few good things, not your junk), keeps
  the "gift" feeling intentional, and stops a cache becoming a bulk free-dump.
  Going visibly smaller than a normal storage chest signals "this is a shelf, not
  storage." Lean 2×3 if you want it pointed, 3×3 if you want a little breathing
  room. (Exact vanilla chest dimensions are a §8-A VERIFY item — not asserted here.)

**Q4 — Private compartment size.**
Independent of the public size. The private drawer is "what I cached here for
myself."
- **Lean: small and equal-or-smaller than public — 2×2 (4) or 2×3 (6).** Private
  drawers are the thing that grows the ZDO blob per player (§7), so smaller is
  cheaper at scale, and a private waypoint stash should be a few key items (a
  spare pickaxe, food, a portal scroll), not a second base chest. Keeping it ≤
  public also reinforces "the public shelf is the main event; the drawer is a
  personal convenience."

**Q5 — The "traveller beware" destroy-warning hover (Daniel leans yes).**
Should the hover text carry the explicit warning that destruction drops public
items and *loses* private drawers? The card notes Daniel leans yes.
- **Lean: yes, confirmed.** The private-loss-on-destroy is a genuinely surprising,
  punishing rule; surfacing it on hover is honest and prevents a "I lost my stuff
  and had no warning" feel-bad. It costs one localized line (§5). Ship the
  warning. Open sub-knob: exact wording/severity color — trivial, pick at
  implementation.

> **Until Q1–Q5 are answered this doc stays `status: proposed` and does NOT lock.**
> When Daniel answers, the architect (or a follow-up spec card) graduates it to a
> version-scoped impl-spec under the tier dir Q1 picks, adds the SpecCheck manifest
> row (a Piece-only row — the cache is a build piece, not an item recipe, so it
> takes the `Piece = "piece_sbpr_traveler_cache"` shape like the Surveyor's Table
> at `SpecCheck.cs:88-91`), and the dataset row (§10).

## 10. Scope boundaries + follow-ups

**In scope for the implementing card (once Q1–Q5 lock):** the additive prefab,
the dual-compartment Tag, the ZDO-blob persistence with owner-write, the
public-vs-private open branch, the destroy contract, the SpecCheck Piece row, the
`PIECES_AND_CRAFTABLES.md` dataset row, and the multiplayer ≥2-player verification
(AC #8 — "logs green ≠ playable": two real clients, one leaves a gift + a private
item, the other sees the gift but NOT the private drawer, both persist across a
server restart, destroy drops public + loses private).

**Out of scope (document only / separate cards):**
- **REQ-CACHE-EVICT** (§7) — the public-server private-map cap/eviction. Separate
  future card, gates public Thunderstore release; do not implement here.
- **requirements.md v1 scope** — the cache is net-new design and is NOT added to
  the locked v1 manifest (Explorer's Bench / Pigments / Signs / Spade / Cairns).
- **Sibling Exploration-Tools** (Pocket/Twisted Portal, Iron Compass, Beacons) —
  their own cards; this doc is the cache only.

**Spawn-when-locked:** an implementing card assigned to an engineer-with-decomp
(the §8 VERIFY surface requires the corpus), and — flagged for Daniel — whether to
spike §8-D (chest-UI seam) as its own tiny proof card first, given it is the one
real build risk.
