---
title: "Twisted Portal — long-range named portal network with a food-charged key (v3 impl spec)"
status: proposed
purpose: "Build-ready architect spec for the v3 Swamp-tier Twisted Portal: a distinct portal class that teleports even where vanilla portals are blocked (NoPortals), addressed by player-assigned RUNE NAMES, accessed via a food-charged TRINKET key that burns charge per teleport. Converts the locked design (docs/design/nomap.md §7) + card t_f9cab392 acceptance criteria into a buildable HOW — the multi-prefab architecture, the exact vanilla decomp hooks (all line-cited against assembly_valheim, grepped live this pass), the named-directory teleport model, the durability-as-charge key, and named acceptance tests. THREE design questions (Q1 coexist/replace, Q2 charge economy, Q3 rune-name + destination UX) plus ONE hard architecture fork (multiplayer 300m sync) are flagged for Daniel — this doc proposes defaults and BLOCKS on his ratification before the impl cards fan out. Authored by the architect spec-pass (card t_f9cab392); Daniel gates the merge."
---

# Twisted Portal — long-range named portal network with a food-charged key

The design ([`nomap.md` §7 "Twisted Portal (THE BIG ONE)"](../../design/nomap.md)) is the
locked *what*. The kanban card **t_f9cab392** is the locked *acceptance shape*. This doc is
the buildable *how*: the multi-prefab architecture, the vanilla hooks re-verified against the
decomp, the named-directory teleport model that replaces vanilla's 1:1 tag pairing, the
food-charged Trinket key, named acceptance tests, the `Features/` placement, and the SpecCheck
manifest impact.

> **Clean-side note (ADR-0001):** every decomp line cited here is the base game
> (`assembly_valheim`), which is **fair game to read and adapt** (repo AGENTS.md + the
> 2026-06-09 clarification). Line numbers are from
> `~/valheim/worldgen-spike/decomp/assembly_valheim.decompiled.cs` (this box) and were
> grepped live during this pass — re-confirm against the build assembly if the decomp drifts.
> `PortalIndicator` / Rune-Magic mods are **reference-only**: their *behaviour* (through-terrain
> portal labels; rune-of-detection) is reproduced from vanilla primitives only — no third-party
> code is read or copied. The named-directory + key-charge mechanics are net-new SBPR fiction.

> **ADR-0006 (load-bearing):** every prefab here is built **additively** (`new GameObject()` +
> `AddComponent`), reading vanilla prefabs only as blueprints via `ZNetScene.GetPrefab` /
> `vprefab inspect`. We do **NOT** `Instantiate` `portal_wood` (it drags `ZNetView` + a
> `PlayerBase` EffectArea + `GuidePoint` + `portal_destruction` — the cairn-soft-lock /
> Explorer's-Bench-GuidePoint bug class). The portal piece reuses the Ancient Portal's proven
> additive shell + grafted-ring kitbash (`docs/v2/planning/ancient-portal-impl-spec.md`).

> 🔴🔴 **STATUS: BLOCKED ON DANIEL.** This spec is build-ready in its mechanics but is gated on
> **three design decisions (Q1–Q3, §1)** and **one hard architecture ratification (§2, the
> multiplayer 300m sync fork)**. Each has a proposed default grounded in precedent; none can be
> silently chosen for Daniel because they are pacing/UX/scope calls. The impl cards do **not**
> fan out until Daniel answers. See §1, §2, and the §11 decision log.

---

## 0. SpecCheck manifest impact (read first — it moves with the code)

`Runtime/SpecCheck.cs` holds the recipe drift manifest. This feature adds **+3 entries**
(all new item/piece recipes):

| # | Manifest entry | Kind | Resources | Station |
|---|---|---|---|---|
| 1 | `SBPR_TwistedKey` | item recipe (amount 1) | **see Q2 / §5 — charge economy gates final costs** (proposed: `SBPR_Sunstone ×1, Bloodbag ×5, Iron ×1`) | `piece_sbpr_explorers_bench` |
| 2 | `piece_sbpr_twisted_portal` | build piece | **see Q1 / §4.7 — recipe shape depends on coexist-vs-replace** (proposed: `FineWood ×20, GreydwarfEye ×10, SurtlingCore ×4, SBPR_Sunstone ×1`) | (Hammer-placed; `m_craftingStation = null`) |
| 3 | `SBPR_Sunstone` | *(EXISTING — shared v3 material, already a SpecCheck row from the Sunstone Lens; this feature CONSUMES it, does not re-add it)* | — | — |

**Resource prefab-name caveats (must match vanilla internal IDs / SBPR consts, or SpecCheck
flags a NULL `m_resItem`) — verified this pass against the wiki corpus `Internal ID` field
(`~/valheim/sbpr-corpus/wiki/fandom/`):**
- `SBPR_Sunstone` = the shared v3 material const `SunstoneLens.SunstoneName` — referenced via
  the const, **never a literal**. This is the first cross-feature reuse of Sunstone the
  Lens design note anticipated ("expected to have more than one use") — the Twisted Portal
  network is its second consumer. Spec-wires Twisted to depend on the Sunstone feature.
- Iron = vanilla **`Iron`** (verified `Iron.md`) — the Swamp metal, the v3 tier gate.
- Bloodbag = vanilla **`Bloodbag`** (verified `Bloodbag.md`, leech drop) — Swamp-surface,
  on-thesis for a "charged" magical key; proposed, gated on Q2.
- GreydwarfEye = vanilla **`GreydwarfEye`** — already `MarkerSigns.EyeResource`; reference
  that const (the Ancient Portal precedent, `Portals.cs:71`), don't hardcode.
- FineWood / SurtlingCore = vanilla **`FineWood`** / **`SurtlingCore`** (portal-family mats).

**The two SpecCheck shapes (gotcha — same as the Ancient Portal §0):** row 1 (`SBPR_TwistedKey`)
is `Item` only (recipe, `Station` set, `Amount = 1`); row 2 (`piece_sbpr_twisted_portal`) is
`Piece` only (no `Item`, no `Station`). A `RecipeSpec` with both null or both set is silently
skipped — match the shape exactly. The Key is additively constructed (`Assets.ConstructItemShell`)
so `SpecCheck.CheckIcon` (C1) ERRORs at boot if the real PNG didn't ship — bundle a placeholder
key icon. The portal **piece** uses `m_icon` for the Hammer menu (absent = no thumbnail, non-fatal).

The card that touches `SpecCheck.cs` cites **this doc** alongside the existing sources. Code +
spec + SpecCheck row move in the **same PR** (spec-first rule).

---

## 1. The three design questions for Daniel (the card's gate — answer before impl)

The card body names three open questions "to resolve with Daniel before coding." As architect I
have a proposed default for each (grounded in precedent), but all three are pacing/UX/scope calls
that are genuinely Daniel's — so this doc **blocks** on them rather than silently picking. Each
answer changes a specific, isolated part of the build (named below), so a late answer is cheap to
fold in.

### Q1 — Coexist with, or replace, vanilla/Ancient portals once unlocked?
*(card OpenQ 1; also `nomap.md` §7 open #3)*

The Twisted Portal is the "no-restriction endgame portal" (`PIECES_AND_CRAFTABLES.md:421`). The
question is whether, once a player can build Twisted Portals, the **vanilla portal** (and our v2
**Ancient Portal**) remain craftable, or get disabled.

- **Architect-proposed default: COEXIST (additive, disable nothing).** Twisted is a separate
  prefab on the Hammer menu; building it does not touch vanilla `portal_wood` or
  `piece_sbpr_ancient_portal`. This is the lowest-risk, most-reversible choice, it keeps the
  Ancient Portal's "convenience portal that keeps the ore ban" identity intact (the two are
  deliberately different tools — `PIECES_AND_CRAFTABLES.md:421`), and it matches the Trailborne
  philosophy of *adding* options, not removing vanilla ones.
- **If Daniel wants REPLACE:** that means gating/hiding the vanilla portal recipe behind a
  global-key unlock — a `PieceTable`-filtering patch (the same surface the NoMap map-disable
  uses). That is a meaningfully larger, more invasive build (it touches vanilla content the rest
  of Trailborne leaves alone) and it interacts with multiplayer unlock-state sync. **It also
  reframes Q2:** if Twisted *replaces* vanilla portals, the key-charge economy becomes the only
  way to teleport at all, which makes a punishing economy a hard wall rather than a premium
  convenience. **Recommendation: COEXIST for v3.0; revisit REPLACE as a follow-up if playtest
  says the network feels too optional.**
- **What it changes in the build:** §4.7 (whether any vanilla-portal-gating patch exists at all).
  COEXIST = zero patches here. REPLACE = a new `PieceTable`-filter patch + a global-key unlock +
  its own AT. Default keeps the feature patch-light.

### Q2 — Key durability economy: teleports per full charge, and the food→charge ratio?
*(card OpenQ 2; `nomap.md` §7 — "charged accessory burns durability per teleport, food restores")*

The Twisted Key is a battery: eating food charges it, each teleport spends a chunk, Pukeberries
(see §5.3) dump-charge it fast. The numbers are the whole feel of the feature.

- **Architect-proposed default (mirrors the Sunstone Lens's exposed-config approach):**
  `m_maxDurability = 100` (the charge capacity); **`ChargePerTeleport = 20`** (→ 5 teleports on a
  full key); **`ChargePerFoodPoint = 0.5`** charge per point of `item.m_shared.m_food` eaten (a
  ~60-health meal → +30 charge → ~1.5 teleports per big meal); **Pukeberry purge multiplier ×8**
  per food slot evacuated (§5.3). These are **conservative starting values exposed as
  BepInEx config** (the Sunstone Lens precedent — `SunstoneLens` config knobs) so Daniel tunes the
  economy without a rebuild. A full key ≈ 5 jumps; a full belly ≈ 1–2 jumps; a panicked Pukeberry
  dump ≈ near-instant top-up at the cost of all your food buffs.
- **The pacing lean Daniel must set:** is a Twisted teleport a *premium* act (rare, expensive — a
  key is a few jumps and you ration them) or a *routine* one (cheap, the network is your daily
  commute)? The default leans **premium** (it's the endgame no-restriction portal — scarcity is
  the cost of breaking the ore ban). If Daniel wants routine, raise capacity / lower cost.
- **What it changes in the build:** the four constants in §5 and recipe row 1 (a more punishing
  economy pairs with a cheaper key recipe; a generous one with a costlier key). Isolated to
  `TwistedKey.cs` consts + the SpecCheck row.

### Q3 — Rune-name UX: how does a player name a portal and pick a destination from the 300m list?
*(card OpenQ 3; `nomap.md` §7 — "on-step shows visible portal names")*

Two distinct interactions: (a) **naming** this portal (assigning its rune name), and
(b) **choosing** a destination from the nearby-portals list when you travel.

- **(a) Naming — architect-proposed default: reuse the vanilla `TextInput` rename dialog**, the
  exact path vanilla portals use for their tag (`TeleportWorld.Interact` →
  `TextInput.instance.RequestText(this, …)`, decomp `:122967`). Our class implements
  `TextReceiver` (`GetText`/`SetText`, decomp interface `:54810`); `[Use]` (E) opens the rename
  box, the typed string is written to the `sbpr_rune_name` ZDO slot (§6). This is the
  zero-new-UI path and matches every other nameable SBPR piece (the Surveyor's Table rename, the
  Marker Signs). **Lock (a) to this.**
- **(b) Destination choice — the genuinely open UX, two viable models:**
  - **Model A — "named-tag pairing" (LOW build risk, recommended for v3.0).** A Twisted Portal
    teleports to *the nearest other Twisted Portal that shares its rune name*, exactly mirroring
    vanilla's tag-pair semantics but on our `sbpr_rune_name` slot instead of `s_tag`. The player
    "picks a destination" by **naming two portals the same rune** — no new selection UI at all.
    The 300m on-step list is then purely **informational** (it shows you what's nearby and what
    they're named), not a picker. This is the smallest, most vanilla-congruent build and it
    sidesteps the multiplayer directory-sync problem (§2) for *travel* (you still get the
    proximity list cosmetic, but teleport resolution is a simple name match the server already
    has the ZDOs for).
  - **Model B — "directory picker" (HIGH build risk).** Standing on a portal opens a selectable
    list of *all* named Twisted Portals within 300m (or globally), and you click one to travel
    there — a true many-to-many network. This is the richer fantasy but needs (1) a new
    world-space selectable UI and (2) the full cross-client directory sync of §2 to even populate
    the list correctly on a dedicated server. It is a multi-milestone build on its own.
  - **Architect recommendation: ship Model A for v3.0** (named-tag pairing + informational 300m
    overlay), and treat Model B as a *later milestone* if Daniel wants the directory-picker
    fantasy. Model A satisfies every card acceptance criterion except a literal "pick from a list
    to travel" reading — and the card's criteria say "lists nearby names" + "teleports correctly,"
    which Model A meets. **If Daniel reads the feature as REQUIRING Model B's picker, that's a
    scope expansion that changes §2 from "nice-to-have sync" to "required sync" and roughly
    doubles the build — flag it now, don't discover it mid-impl.**
- **What it changes in the build:** §4.4 (teleport resolution: name-match vs picker), §7 (the
  overlay is informational vs interactive), and whether §2's cross-client directory sync is
  optional (Model A) or mandatory (Model B).

> 🟢 **Architect's bottom line for Daniel:** the *cheap, shippable, vanilla-congruent* v3.0 is
> **Q1 = coexist, Q2 = the premium defaults above (exposed as config), Q3 = name-tag pairing
> (Model A) with an informational overlay.** That combination is patch-light, multiplayer-safe,
> and reuses three proven SBPR precedents. The richer readings (replace; routine economy;
> directory picker) are all *real* options but each is a larger build — I want your call on each
> before fanning out impl cards.

---

## 2. 🔴 The multiplayer 300m-proximity fork (architect finding — the Twisted equivalent of the PortalPrefabHash gotcha)

The design doc (`nomap.md` §7) says: *"query `ZDOMan` for all ZDOs of the SBPR prefab hash within
radius (cheap — there's already `ZDOMan.GetAllZDOsWithPrefab` available)."* **That works in
singleplayer and BREAKS on a dedicated server**, and the design doc missed it — exactly as the
Ancient Portal spec caught the `PortalPrefabHash` pairing gotcha that *its* design doc missed.
This is the single highest-risk finding in the feature. Flag it RED.

**Why it breaks (verified against the decomp, this pass):**
- A client does **not** hold every ZDO in the world. The server sends a client only the ZDOs
  within its **active sector window**: `ZDOMan.CreateSyncList` → `FindSectorObjects(zone,
  m_activeArea, m_activeDistantArea, …)` (`:65244`/`:65212`). The window is purely
  sector/distance-based.
- `ZoneSystem.m_zoneSize = 64f` (`:96263`), `m_activeArea = 1` (`:96258`), `m_activeDistantArea
  = 1` (`:96260`). That is a **±1 active zone (~128 m of full ZDOs) + ±1 distant ring**, and the
  distant ring only carries `m_distant`-flagged ZDOs and is only appended when the near sync list
  has room (`if (toSync.Count < 10)`, `:65261`). A build piece is **not** `m_distant` by default.
- Net: a multiplayer client reliably holds Twisted Portal ZDOs only within **~one to two zones
  (~64–128 m)** of itself — **never a guaranteed 300 m**. So `ZDOMan.GetAllZDOsWithPrefabIterative`
  (`:65497`, the real API — note it is the *iterative* variant; the design doc's
  `GetAllZDOsWithPrefab(int)` name does not exist as written) returns only the *locally-held*
  subset on a client. On a dedicated server the result is silently short.
- **Vanilla portals dodge this entirely:** they never do a client-side range query. Pairing is
  resolved **server-side** in `Game.ConnectPortals` (`:84570`) over `ZDOMan.GetPortals()` — the
  `m_portalObjects` list the server maintains for every prefab whose hash is in
  `Game.instance.PortalPrefabHash` (`:64704`). Registering our hash there (which we do for
  teleport — §4.3) populates the **server's** list, not the client's view.

**The three resolution options (architect-ranked):**

1. ✅ **Model A (name-tag pairing) makes this a NON-issue for *travel* — RECOMMENDED.** If Q3
   resolves to Model A, teleport resolution is a **name match resolved exactly like vanilla
   tag-pairing** — server-side over `ZDOMan.GetPortals()` (our hash is registered), using
   `sbpr_rune_name` instead of `s_tag`. The server has every Twisted ZDO; the 300 m client query
   is then needed **only for the cosmetic on-step overlay** (§7), where "I only see the portals my
   client currently holds (~64–128 m)" degrades gracefully to "the overlay shows nearby portals,
   maybe not the full 300 m on a far-flung server." That cosmetic shortfall is acceptable for
   v3.0 and is explicitly flagged in AT-OVERLAY. **This is why Model A is the recommended Q3
   answer: it removes the hardest multiplayer risk from the critical path.**

2. ⚠️ **Custom RPC directory sync (REQUIRED if Q3 = Model B picker).** To populate a true 300 m
   directory on a dedicated client, the owner/server must *push* the portal directory to the
   client. The repo already has the precedent: `SurveyorTableTag` uses
   `nview.Register<ZPackage>("…", RPC_…)` to sync a ZDO blob owner→client
   (`Cartography/SurveyorTableTag.cs:86`). A Twisted "rune registry" node would: the server
   maintains the authoritative directory (name → position) over `ZDOMan.GetPortals()` filtered to
   our hash; on the player stepping onto a portal, the portal's owner RPC-sends the within-300 m
   slice to the stepping client, which renders the picker. This is a **real, multi-day build** —
   it is the bulk of why Model B is "HIGH risk." Do not attempt it before Daniel commits to Model B.

3. ❌ **Force `m_distant = true` on the portal ZNetView.** Tempting (distant ZDOs sync at longer
   range), but rejected: the distant ring is gated behind `toSync.Count < 10` (`:65261`), is still
   only ±1 distant zone, carries no guarantee of 300 m, and marking a teleport-bearing build piece
   `m_distant` has unknown interactions with the portal-hash sync path. Not a real fix; noted so a
   future worker doesn't rediscover it as a "clever" shortcut.

> 🟢 **Architect resolution:** ship **Model A**, which routes teleport resolution through the
> server-side `GetPortals()` path (multiplayer-correct by construction) and lets the 300 m overlay
> be a best-effort client cosmetic. The custom-RPC directory (option 2) is specced here so it's
> ready *if* Daniel picks Model B, but it is explicitly **out of v3.0 scope** under the
> recommended answers. This finding is the reason §1 Q3 leans so hard toward Model A.

---

## 3. Architecture — three prefabs + one tag, the vertical slice

`Features/Portals/` already exists (the Ancient Portal). Twisted lands in the **same feature
folder** as a sibling set of files, reusing the Ancient Portal's additive shell helpers:

| Prefab / type | Kind | Role |
|---|---|---|
| `piece_sbpr_twisted_portal` | build `Piece` (Hammer) | The portal. A **distinct class** `SBPR_TwistedPortal : MonoBehaviour, Hoverable, Interactable, TextReceiver` — does NOT inherit `TeleportWorld` (tag-collision avoidance, card AC#1). Carries our own teleport + rune-name. |
| `SBPR_TwistedKey` | `ItemDrop` (Trinket) | The food-charged key. Durability = charge; burns per teleport; charges from food. Trinket slot (= 24). |
| `SBPR_TwistedPortalTag` | `MonoBehaviour` | Per-instance ZDO discipline (rune-name slot, owner-write, ghost guard) — the `AncientPortalTag` / `CairnTag` precedent. (May be folded INTO `SBPR_TwistedPortal` since that's already a per-instance MonoBehaviour — see §4.1.) |
| `SBPR_Sunstone` | *(shared v3 material)* | Consumed by the Key + portal recipes; already shipped by the Sunstone Lens. |

**Files (mirrors the Ancient Portal layout in `Features/Portals/`):**
- `Features/Portals/TwistedPortal.cs` — `RegisterPrefabs` + `DoObjectDBWiring` for the portal
  piece + the `SBPR_TwistedPortal` class (the distinct teleporter).
- `Features/Portals/TwistedKey.cs` — the Key item registration + its charge logic + the two
  Harmony patches (`Player.EatFood` / `Player.RemoveOneFood` postfixes).
- `Features/Portals/TwistedPortalOverlay.cs` — the on-step proximity overlay (HUD or world-space,
  §7), client-only (the Sunstone Lens HUD-overlay precedent).
- Wire all three into `Runtime/Registrar.cs` (after `Portals` / `SunstoneLens`, since Twisted
  consumes Sunstone and reuses Portal helpers) and register the two patches in `Plugin.Awake`
  (PatchCheck asserts they wove).

**The key architectural distinction from the Ancient Portal (card AC#1):** the Ancient Portal
*adds a real vanilla `TeleportWorld`* (inheriting tag-pairing + ore-ban + the NoPortals check).
Twisted **cannot** — `TeleportWorld.Teleport` hard-enforces `GlobalKeys.NoPortals` (`:123008`),
which is the exact thing Twisted must bypass. So Twisted **reimplements the small slice of
teleport it needs** in `SBPR_TwistedPortal`, omitting the NoPortals gate. It still registers its
prefab hash in `Game.PortalPrefabHash` (§4.3) so the server tracks it for name-pairing — but the
teleport *method* is ours, not vanilla's.

---

## 4. The Twisted Portal piece (`piece_sbpr_twisted_portal`)

### 4.1 Construction (ADR-0006 additive — reuse the Ancient Portal shell)
- Build with **`Assets.ConstructPieceShell("piece_sbpr_twisted_portal", donor)`** (the Ancient
  Portal path, `Portals.cs:195`). Use a wood/organic effect donor (`portal_wood` read as a
  blueprint) so hit/destroy/place sounds read as a portal.
- **Visual kitbash** — reuse the Ancient Portal's grafted ring/legs/roots envelope
  (`Portals.BuildLegs` / `BuildRoots` + the grafted `small_portal` ring) but **re-tinted /
  re-themed** to read as "twisted/swamp" rather than "ancient/forest" — a darker, sicklier
  emission. Exact retint is a flagged art-pass detail (AT-GEOMETRY); v3.0 ships the Ancient
  envelope with a swamp tint so it's visually distinguishable from the Ancient Portal at a glance.
  All grafts are mesh-reference, ZNetView-free (ADR-0006).
- **The per-instance class IS the portal's brain.** Unlike the Ancient Portal (which bolts a real
  `TeleportWorld` + a separate `AncientPortalTag`), Twisted's `SBPR_TwistedPortal` MonoBehaviour
  *is* both the teleporter and the ZDO-discipline owner. It implements `Hoverable` (`GetHoverText`
  /`GetHoverName`, decomp interface `:111336`), `Interactable` (`Interact`/`UseItem`, `:111594`),
  and `TextReceiver` (`GetText`/`SetText`, `:54810`). One MonoBehaviour, no `TeleportWorld`.

### 4.2 Fragility / placement (reuse the Ancient Portal's LOCKED choices)
- `WearNTear.MaterialType.Wood`, `m_health` per the Ancient Portal's 300 default unless Daniel
  re-leans (flag — Twisted is endgame, arguably tankier; default to **300** to match, note as a
  tunable). `m_noRoofWear = true` (no rain decay — the Ancient Portal precedent).
- **Hammer-placed, no station** (`m_category = Misc`, `m_craftingStation = null`,
  `Assets.AddOrReplacePieceByName` onto `Assets.GetHammerPieceTable()`) — identical to the Ancient
  Portal (`Portals.cs:545`) and consistent with the design-pillars Hammer-exception already carved
  for portals (`design-pillars.md:34`).
- **Solid-earth only** (`m_groundOnly = true`, `:18879`) — same as the Ancient Portal §3.4b.
- Build cost: recipe row 2 (§0), shape gated on Q1. Rebuilt authoritatively in `DoObjectDBWiring`
  after the materials resolve (the Ancient Portal ordering).

### 4.3 Prefab-hash registration (the server-side pairing substrate — REQUIRED even for Model A)
Register `"piece_sbpr_twisted_portal".GetStableHashCode()` into `Game.instance.PortalPrefabHash`
**exactly as the Ancient Portal does** (`Portals.EnsurePortalHashRegistered`, `:481` — idempotent,
null-Game-guarded, re-asserted in `DoObjectDBWiring`). This makes `ZDOMan` track our portal ZDOs
in `m_portalObjects` (`:64704`), which is what gives the **server** an authoritative list of all
Twisted Portals for name-pairing (§4.4) — the multiplayer-correct substrate from §2.

> 🔴 **But do NOT let vanilla `Game.ConnectPortals` auto-connect our portals on `s_tag`.** Vanilla
> `ConnectPortals` (`:84570`) pairs `m_portalObjects` entries by their `s_tag` ZDO string. Our
> portals must pair on `sbpr_rune_name`, NOT `s_tag` — and our portals must never form a vanilla
> `ConnectionType.Portal` ZDO connection (that's vanilla's 1:1 channel; ours is a name-directory).
> **Mitigation:** never write `s_tag` on a Twisted Portal ZDO (leave it empty). Vanilla
> `ConnectPortals` skips empty-tag portals from *pairing* (`FindRandomUnconnectedPortal` matches on
> tag equality, and two empty tags WOULD match — see the caveat below). **This is a real
> interaction to verify in-game (AT-NO-VANILLA-PAIR):** confirm two unnamed Twisted Portals do not
> get auto-connected by vanilla's `ConnectPortals` into a spurious `ConnectionType.Portal` link.
> If they do (empty-tag collision), the fix is to either (a) seed a unique per-portal sentinel into
> `s_tag` so vanilla never pairs them, or (b) keep our hash OUT of `PortalPrefabHash` and resolve
> name-pairing entirely in our own code over `ZDOMan.GetAllZDOsWithPrefabIterative` server-side.
> **Architect lean: option (b) is cleaner** — it fully decouples us from vanilla's portal-pairing
> machinery and avoids any `s_tag` games, at the cost of doing our own server-side directory walk
> instead of reusing `GetPortals()`. The engineer picks (a) vs (b) after the in-game AT-NO-VANILLA-PAIR
> check; this is flagged RED as the #1 build-time decision inside the portal card.

### 4.4 Teleport — our reimplementation, NoPortals omitted (card AC#2)
The activation surface is the **overhead jump-through trigger** (the Ancient Portal's
`TeleportWorldTrigger` precedent, §3.7 there) — BUT since we don't have a vanilla `TeleportWorld`,
the trigger calls **our** teleport. Reproduce the minimal slice of `TeleportWorld.Teleport`
(`:123002`) we want, deliberately **omitting the NoPortals check** (`:123008`):

```
SBPR_TwistedPortal.Teleport(Player player):
   if player == null or not key-charged (see §5): message + return     # key gates travel
   # NO GlobalKeys.NoPortals check — this is the whole point (AC#2)
   # NoBossPortals: KEEP the boss check (:123013) — flagged for Daniel, default KEEP
   #   (a no-restriction portal during a boss fight is likely NOT intended; confirm)
   target = ResolveDestination()        # §4.4a — name-match, server-side
   if target == null: message "$sbpr_twisted_no_destination"; return
   pos = target.position + (target.rotation * Vector3.forward) * exitDistance + Vector3.up
   player.TeleportTo(pos, target.rotation, distantTeleport: true)      # :20771 — the clean primitive
   SpendKeyCharge(player)               # §5 — burn ChargePerTeleport
```

- **`Player.TeleportTo(pos, rot, distantTeleport)`** (`:20771`) is the clean public primitive —
  owner-RPC-safe (`:20775`), handles the 2 s fade + area-ready wait + floor-find. We do NOT
  hand-poke transforms. This is the same call vanilla `TeleportWorld.Teleport` ends in (`:123031`).
- **Ore-ban (card "otherwise behaves"): KEEP by default** — call `player.IsTeleportable()`
  (`:57606`) and block with `$msg_noteleport` if carrying ore, exactly like vanilla (`:123018`).
  The card says Twisted lifts the *NoPortals* restriction, not the *ore* restriction; the ore ban
  is a separate axis. **Flag for Daniel:** does the endgame key also let ore through? Default
  **NO** (keep the ban); it's a one-line flip if he wants ore-portability as the key's payoff.

#### 4.4a `ResolveDestination()` — server-correct name pairing (Model A)
- Walk the authoritative portal set **server-side**: the owner of this portal (or the server)
  enumerates Twisted Portal ZDOs (via `ZDOMan.GetPortals()` filtered to our hash, OR our own
  `GetAllZDOsWithPrefabIterative` walk per §4.3 option b), filters to those whose
  `sbpr_rune_name` equals this portal's, excludes self, and picks the nearest (or the
  single paired one). Returns its position+rotation.
- Because this runs where the full ZDO set exists (server/owner), it is **not** subject to the
  client's 64–128 m window (§2) — this is the core reason Model A is multiplayer-correct.
- If the resolution must happen client-side (e.g. the stepping client owns the portal), and the
  destination is outside the client's held set, route the resolution through the portal owner via
  a routed RPC (the `SurveyorTableTag` RPC precedent). For v3.0 with Model A, the common case
  (host owns world ZDOs) resolves directly; the RPC fallback is the robustness path, flagged.

### 4.5 The overhead jump-through trigger (reuse the Ancient Portal, AC — jump to travel)
Identical mechanism to the Ancient Portal's `BuildOverheadTrigger` (`Portals.cs:365`): a child
`BoxCollider{isTrigger=true}` + a small trigger MonoBehaviour whose `OnTriggerEnter` calls
`SBPR_TwistedPortal.Teleport(player)`. **Size is desk-estimated, FLAGGED for in-game tuning**
(AT-JUMP-ACTIVATE) — the same ~2.6 m × ~0.9 m envelope the Ancient Portal uses, reused verbatim.
(Twisted has no grow timer — it's active on placement, see §4.6 — so the trigger is enabled
immediately, no grow-gate.)

### 4.6 No grow timer (a deliberate difference from the Ancient Portal)
The Ancient Portal's 15 s grow is its planted-seed fantasy. Twisted is *built*, not *grown* — it's
active on placement. So `SBPR_TwistedPortal` skips the grow lifecycle entirely; the trigger is live
once placed (gated only by the key-charge check at teleport time, §5). This keeps the class simpler
than `AncientPortalTag`. (If Daniel wants a "warm-up" flourish, it's an easy add later — flag, don't
pre-build.)

### 4.7 Vanilla-portal gating (ONLY if Q1 = REPLACE — default: nothing here)
Under the recommended **Q1 = coexist**, this section is empty — Twisted disables nothing. If Daniel
chooses REPLACE, this is where a `PieceTable`-filter patch hides the vanilla `portal_wood` (and
possibly Ancient Portal) recipe behind a Twisted unlock global-key, plus an AT for it. Specced as a
stub so the decision has a home; not built under the default.

---

## 5. The Twisted Key (`SBPR_TwistedKey`) — food-charged Trinket

The key is a Trinket-slot accessory whose **durability bar is a charge meter** — the same
durability-as-energy model the Sunstone Lens shipped (`SunstoneLens.cs`). It charges from food,
burns charge per teleport, and Pukeberries dump-charge it fast.

### 5.1 Item construction + slot (the Sunstone Lens precedent, verbatim pattern)
- Build via **`Assets.ConstructItemShell("SBPR_TwistedKey")`** (additive, ADR-0006 — fresh
  `ZNetView` + `ItemDrop` + `SharedData` + seeded `FallbackIcon` + the `m_attack` NRE-guard seed).
  The Sunstone Lens does exactly this.
- `m_shared.m_itemType = ItemType.Trinket` (= **24**, verified `:57652`). The Trinket slot is a
  fully-wired single equip slot (`Humanoid.m_trinketItem`, EquipItem Trinket branch `:13992`).
  **Shares the slot with the Sunstone Lens and the future Iron Compass** — a deliberate
  exploration-tool contention (you pick ONE trinket). Flag for Daniel; not pre-decided. (This is
  the same slot-contention note the Sunstone Lens raised.)
- `m_shared.m_useDurability = true` so the durability bar renders as the charge meter
  (`InventoryGrid` draws the bar only when `m_useDurability`). `m_maxDurability = 100` (Q2 capacity).
- `m_shared.m_maxStackSize` — **see §5.4** (stack-charge-split). The key MUST be stackable for the
  "charge splits across copies" criterion to mean anything → `m_maxStackSize = N > 1` (proposed 5).

### 5.2 Charge from food — `Player.EatFood` postfix (card AC: "eating food restores charge")
Harmony **postfix on `Player.EatFood(ItemDrop.ItemData item)`** (`:17462`). When `__result == true`
(the food was actually eaten):
```
foreach key in inventory matching SBPR_TwistedKey (count = n):
    increment = item.m_shared.m_food * ChargePerFoodPoint / n      # §5.4 split across n copies
    key.m_durability = Mathf.Min(key.m_durability + increment, key.GetMaxDurability())
```
- Gate to the local player + our item; pure pass-through otherwise. Registered in `Plugin.Awake`,
  PatchCheck-asserted (the Sunstone drain-patch precedent).
- Use `item.m_shared.m_food` (the food's health value, read at `:17469`) as the charge basis so
  bigger meals charge more — a natural economy. `ChargePerFoodPoint` is the Q2 config knob.

### 5.3 Pukeberry purge accelerator — `Player.RemoveOneFood` postfix (card AC: "bukeberries accelerate")
> **Naming correction (corpus-verified):** the design doc says "bukeberries/bukeperries." The
> vanilla item is **`Pukeberries`** (internal id `Pukeberries`, verified
> `~/valheim/sbpr-corpus/wiki/fandom/Pukeberries.md`; "Bukeperries" is the wiki's spoonerism
> display name). Use the internal id `Pukeberries` in any reference.

There is **no hardcoded Pukeberry path** in the decomp (verified — 0 hits for the literal). The
purge primitive is **`Player.RemoveOneFood()`** (`:17452`), which pops one random food slot. It is
driven by the **`SE_Puke` status effect** (`:25312`) that Pukeberries (and Rotten meat) apply via
`m_consumeStatusEffect`: `SE_Puke.UpdateStatusEffect` calls `RemoveOneFood()` every
`m_removeInterval` (1 s) for the effect's 15 s (`:25324`-`:25335`).

So: **postfix `Player.RemoveOneFood()`** (`:17452`); when `__result == true` (a food was actually
evacuated), apply a **larger** charge increment to keys in inventory than a normal `EatFood`
postfix would:
```
foreach key in inventory matching SBPR_TwistedKey (count = n):
    increment = PukePurgeCharge * PukePurgeMultiplier / n          # bigger than EatFood's per-tick
    key.m_durability = Mathf.Min(key.m_durability + increment, GetMaxDurability())
```
- This makes Pukeberries the "spend stomach for portal range" strategy: each of the ~15 evacuations
  over the puke duration dumps a big charge chunk into the key. `PukePurgeMultiplier` is the Q2 knob.
- **Subtlety:** `RemoveOneFood` is *also* called for non-Pukeberry reasons? Verified: the ONLY
  caller is `SE_Puke` (`:25331`) — so a `RemoveOneFood` postfix fires *only* during a puke effect.
  No need to detect "was it a Pukeberry"; any puke charges the key, which is exactly the intent
  (Rotten meat puke charges it too — a nice emergent parity the wiki notes at `Pukeberries.md:76`).

### 5.4 Charge split across stacked copies (card AC: "syncs/splits across multiple copies")
- Count `SBPR_TwistedKey` instances in inventory via `Inventory.CountItems("SBPR_TwistedKey")`
  (`:56985`) or by scanning `GetEquippedItems()`/all items; divide the per-event increment by the
  count and apply to **each** key. So one meal's charge is *split*, not duplicated — holding 3 keys
  doesn't triple your charge income.
- **Per-instance durability is per-ItemData**, so stacked-but-distinct keys each carry their own
  `m_durability`. Two keys at different charges do NOT auto-merge into one stack (vanilla blocks
  stacking items with differing durability) — which is correct: a half-charged and a full key stay
  separate slots. "Splits across copies" = the *income* divides; it does not mean the *meter* is
  shared. Confirm this reading with Daniel (it's the only sensible one given per-item durability,
  but the card phrase "syncs" is ambiguous — flag).
- **Which key gets spent on teleport?** `SpendKeyCharge` (§4.4) burns `ChargePerTeleport` from the
  **equipped** key (the one in `m_trinketItem`); if none equipped, the teleport is blocked
  ("equip a charged Twisted Key"). The inventory (non-equipped) copies charge but don't power
  travel — you must wear one. This makes the Trinket slot meaningful. Flag the "must be equipped to
  travel" rule for Daniel (alternative: any carried key works — but then the slot is pointless).

### 5.5 Not consumed at zero (the Sunstone Lens precedent)
At zero charge the key is **inert** (teleport blocked with a message) but **NOT consumed/broken** —
it works again after recharging. Vanilla `Humanoid.DrainEquipedItemDurability` (`:13227`)
auto-drains equipped trinkets every tick AND breaks+unequips+destroys at zero — which would both
(a) wrongly drain our key just for wearing it and (b) destroy it at zero. **Mirror the Sunstone
Lens fix exactly:** a Harmony **prefix on `DrainEquipedItemDurability`** that, for our key only,
**skips vanilla's drain+break entirely** (returns false) — our key only loses charge via
`SpendKeyCharge` on teleport, never from the passive wear tick. Every other item passes through.
This is the single most important reuse from the Sunstone Lens; the engineer should read
`SunstoneLens`'s drain-prefix and clone its shape.

---

## 6. Rune-name storage (`sbpr_rune_name` ZDO slot — card AC: "stored in the sbpr_rune_name ZDO slot")

- **A dedicated ZDO string slot, SEPARATE from `s_tag`** — `m_zdo.Set("sbpr_rune_name", name)` /
  `GetString("sbpr_rune_name")`. The card names this slot explicitly. Keeping it off `s_tag` is
  what prevents vanilla's tag machinery (and vanilla portals) from ever connecting to a Twisted
  Portal by tag collision (§4.3). **LOCK the key string `sbpr_rune_name`** — a rename orphans every
  named portal's ZDO (the `SBPR_PortalPlantTime` lock lesson).
- **Owner-write discipline** (the `CairnTag`/`AncientPortalTag` precedent): writes guard on a live
  ZDO (ghost = no-op), claim ownership before writing (`if (!nview.IsOwner()) nview.ClaimOwnership()`),
  then `Set`. Reads are free. The `SBPR_TwistedPortal` class owns this (it's the per-instance
  MonoBehaviour) — fold the ZDO discipline in rather than a separate `…Tag` unless the class grows
  unwieldy (§3 noted both options).
- **Naming UX** = the vanilla `TextInput` dialog (§1 Q3a): `Interact(human, hold, alt)` →
  `TextInput.instance.RequestText(this, "$sbpr_twisted_rune", 15)` (the portal's `[Use]` opens the
  rename box; `TextReceiver.SetText` writes the slot). 15-char cap (vanilla portal tags are 10; a
  rune name can be a touch longer — flag the exact cap, default 15).
- **Hover text** shows the rune name + charge hint: `GetHoverText()` returns the localized rune
  name + "[E] Set rune" + (if a paired destination exists) "→ <dest rune>". The `GetHoverName()`
  returns "Twisted Portal".
- **CensorShittyWords / UGC filter:** vanilla portal tags run through `CensorShittyWords.FilterUGC`
  (`:123045`). Mirror it for rune names so a server's profanity policy applies (the Surveyor's
  Table naming did this) — flag as a nicety, not a blocker.

---

## 7. The on-step proximity overlay (card AC: "lists nearby names within 300m, visible through terrain")

This is the "stranger UI" the design doc (`nomap.md` §7) and risk-rank #9 both flag as the biggest
novel-UI chunk. It is **deferred to the LAST milestone** (§9 ordering) and, under the recommended
Model A (§1 Q3), is **informational, not interactive**.

### 7.1 What it shows
When the local player stands on (or near) a Twisted Portal, an overlay lists nearby Twisted Portals
by rune name. Two render routes, mirroring the choices the rest of Trailborne has made:
- **Route A — HUD overlay (RECOMMENDED for v3.0, the Sunstone Lens / Iron Compass doctrine).** A
  list under `Hud.instance.m_rootObject` (`:38949`) showing rune name + distance + bearing for each
  nearby portal. **NoMap-safe** (the SB server runs NoMap by default — `NoMapEnforcer` — so minimap
  pins have no surface; the Sunstone Lens chose HUD for exactly this reason). Client-only by
  construction (`Hud.instance` only exists on a client). This is the lowest-risk route and reuses
  the `SunstoneLensHudOverlay` precedent almost verbatim.
- **Route B — world-space through-terrain labels (the design doc's literal "visible through
  terrain" reading; HIGHER risk, defer).** A world-space `Canvas` per nearby portal with a
  `UI.Text` rune label rendered with a **ZTest Always** material so it shows *through* terrain
  (the `nomap.md` §7 "ZTest-Always shader, prefab-only" note). This is pure prefab/shader work (no
  patches) but it's the most bespoke UI in the mod. **The card itself says "Defer the
  through-terrain standing name overlay … to a later milestone."** So v3.0 ships Route A (HUD list
  with bearings); Route B (the floating through-terrain rune labels) is a named later milestone.

### 7.2 Data source + the 300m multiplayer caveat (from §2)
- The overlay queries nearby Twisted Portals via `ZDOMan.GetAllZDOsWithPrefabIterative` for our
  prefab hash, filtered to within `OverlayRadius` (300 m) of the player, read each one's
  `sbpr_rune_name`.
- 🔴 **Per §2, on a dedicated server the client only holds portal ZDOs within ~64–128 m** — so the
  overlay shows the portals the client currently has, which may be fewer than the true 300 m set on
  a far-flung multiplayer world. **Under Model A this is an acceptable cosmetic shortfall** (travel
  itself resolves server-side and is correct; only the *list display* is range-limited).
  **AT-OVERLAY explicitly tests + documents this** so it's a known, accepted limitation, not a bug.
  If Daniel wants a *guaranteed* 300 m list on dedicated servers, that's the custom-RPC directory
  sync (§2 option 2 / Model B) — out of v3.0 scope under the recommended answers.

### 7.3 Trigger
A proximity check (player within ~3 m of a Twisted Portal, the same `GetClosestPlayer` /
`m_activationRange` pattern vanilla portals use, `:20324`) toggles the overlay visible, refreshed on
a ~0.5 s throttle from the overlay's `Update` (so it costs nothing on the server, which has no Hud —
the Sunstone Lens cadence).

---

## 8. Registration + wiring order (Registrar, PatchCheck, server-gating)

New files in the existing `Features/Portals/` slice:
- **`TwistedPortal.RegisterPrefabs(zns)`** — build + register the portal piece (additive shell +
  grafted kitbash + `SBPR_TwistedPortal` MonoBehaviour); register our prefab hash into
  `Game.PortalPrefabHash` per §4.3 (idempotent, the Ancient Portal helper).
- **`TwistedKey.RegisterPrefabs(zns)`** — build + register the Key item (additive shell).
- **`TwistedPortal.DoObjectDBWiring(zns)`** + **`TwistedKey.DoObjectDBWiring(zns)`** — register the
  item into ObjectDB, add the Key recipe + the portal piece recipe (Q1/Q2 shapes), rebuild the
  portal piece cost, add the portal to the Hammer PieceTable (`AddOrReplacePieceByName`).
- **Wire into `Runtime/Registrar.cs`** — add the `RegisterPrefabs` calls **after `Portals` and
  `SunstoneLens`** (Twisted reuses Portal helpers and consumes Sunstone), and the
  `DoObjectDBWiring` calls **after `Trailhead`** (Explorer's Bench must exist for the recipe
  station) — mirror the Ancient Portal ordering (`Registrar.cs:75`/`:146`).
- **`SpecCheck.cs`** — add the two new rows (§0); extend the `LOCKED SOURCE` comment to cite this
  doc. (Sunstone's row already exists from the Lens — do not duplicate it.)
- **Two Harmony patches** (`Player.EatFood` postfix, `Player.RemoveOneFood` postfix) + the
  **`DrainEquipedItemDurability` prefix** (§5.5, cloned from Sunstone) MUST be registered in
  `Plugin.Awake` via `harmony.PatchAll(typeof(...))` or **PatchCheck ERRORs at boot** (the
  "unregistered patch ships dead" lesson, t_564f695a). The portal/teleport/trigger/ZDO machinery is
  otherwise **patch-free by construction** (component wiring) — only the key-charge hooks need
  Harmony.
- **Server-gating:** every patch top + registration is gated `if (!ServerContext.OnSBServer) return;`
  via the Registrar fan-out (SBPR doctrine). The overlay + charge patches are client-relevant; the
  registration is server+client.

---

## 9. Decomposition — the impl cards this spec fans out into (AFTER Daniel unblocks)

This feature is 4+ subsystems; it must NOT be one impl card. Proposed child cards, in dependency
order (the architect creates these once Daniel answers Q1–Q3; assignee = the SBPR engineer-systems
profile, the same one that built the Ancient Portal + Sunstone Lens). Each is a separate PR Daniel
gates.

| # | Card | Depends on | Scope | Primary ATs |
|---|---|---|---|---|
| C1 | **Twisted Portal piece + reimplemented teleport (NoPortals bypass) + rune-name ZDO + jump trigger** | — | §3, §4, §6. The portal class, hash registration + the AT-NO-VANILLA-PAIR `s_tag` decision, our `Teleport` (NoPortals omitted), `ResolveDestination` name-match (Model A), the rune `TextInput` rename, the overhead trigger (reuse Ancient). Recipe row 2. | AT-PORTAL-PLACE, AT-NOPORTALS-BYPASS, AT-RUNE-NAME, AT-NAME-PAIR, AT-NO-VANILLA-PAIR, AT-JUMP-ACTIVATE |
| C2 | **Twisted Key item + food-charge + Pukeberry purge + stack-split + no-break-at-zero** | C1 (teleport must exist to spend charge) | §5. The Trinket key, the two charge postfixes, the drain-prefix (Sunstone clone), the SpendKeyCharge hook into C1's teleport. Recipe row 1. | AT-KEY-CRAFT, AT-KEY-CHARGE-FOOD, AT-KEY-PUKE, AT-KEY-SPLIT, AT-KEY-SPEND, AT-KEY-ZERO-INERT |
| C3 | **On-step proximity overlay (HUD list, Route A)** | C1 (portals must exist + be named) | §7. The HUD overlay listing nearby rune names + bearings, the proximity trigger, the 300m client-window caveat documented. | AT-OVERLAY |
| C4 *(later milestone, only if Daniel wants it)* | **Through-terrain world-space rune labels (Route B) and/or Model B directory picker + RPC sync** | C1, C3 | §2 option 2, §7 Route B. The ZTest-Always floating labels and/or the cross-client directory sync + selectable picker. | AT-OVERLAY-THROUGHTERRAIN, AT-DIRECTORY-PICKER |

- **Build order:** C1 → C2 → C3, each its own Daniel-gated PR (incremental delivery doctrine). C4
  is explicitly out of the v3.0 commitment under the recommended answers; it becomes real only if
  Daniel picks Model B (Q3) or wants the through-terrain labels promoted.
- **The architect (this card) creates C1–C3 via `kanban_create` once Daniel unblocks**, linking
  them `parents=[this card]` with the dependency edges above. This card does NOT implement anything.

---

## 10. Named acceptance tests (the single source of truth for "done")

Observable criteria. **logs-green ≠ playable** — every AT closes only on Daniel placing/using one
in-game on a joined client (repo honesty rule). The engineer reports per-AT status in each PR
handoff; the build PRs do NOT self-close these.

- **AT-PORTAL-PLACE** — `piece_sbpr_twisted_portal` is placeable with the **Hammer**, **no station
  in range**, solid-earth only (rejected on structures), costs the recipe-2 materials. Visually
  distinct from the Ancient Portal (swamp tint).
- **AT-NOPORTALS-BYPASS** (🔴 the headline, card AC#2) — with the `NoPortals` global key SET (the
  thing that blocks vanilla + Ancient portals), a Twisted Portal **still teleports**. A vanilla
  portal next to it does NOT (proves we bypass the gate that still binds vanilla).
- **AT-RUNE-NAME** (card AC) — `[Use]` on a Twisted Portal opens the rename dialog; the typed rune
  name persists across relog (stored in `sbpr_rune_name`, NOT `s_tag`). Hover shows the rune name.
- **AT-NAME-PAIR** (card AC#2, Model A) — two Twisted Portals given the **same rune name** teleport
  to each other; two with **different** names do not. Works with `NoPortals` set.
- **AT-NO-VANILLA-PAIR** (🔴 §4.3 risk) — two UNNAMED Twisted Portals are NOT auto-connected by
  vanilla `ConnectPortals` into a spurious `ConnectionType.Portal` link; a Twisted Portal never
  pairs with a vanilla `portal_wood` even if their tags/names coincide. Verified in-game (the
  `s_tag`-empty / hash-registration interaction).
- **AT-JUMP-ACTIVATE** (🔴 geometry, reuse Ancient tuning) — **jumping up** into the ring teleports;
  **walking underneath** does not. Trigger box tuned on a joined client.
- **AT-KEY-CRAFT** — `SBPR_TwistedKey` crafts at the Explorer's Bench from recipe-1 materials; it's
  a Trinket; SpecCheck row 1 green at boot (recipe + icon).
- **AT-KEY-CHARGE-FOOD** (card AC) — eating food raises the equipped key's durability/charge bar;
  a bigger meal charges more.
- **AT-KEY-PUKE** (card AC) — eating **Pukeberries** (or Rotten meat) charges the key markedly
  faster than normal food (the purge accelerator), at the cost of the player's food buffs.
- **AT-KEY-SPLIT** (card AC) — holding multiple keys splits one meal's charge income across them
  (3 keys ≠ 3× income); each key carries its own charge.
- **AT-KEY-SPEND** (card AC) — each Twisted teleport burns `ChargePerTeleport` from the **equipped**
  key; at zero charge, travel is blocked with a message (not a crash).
- **AT-KEY-ZERO-INERT** (Sunstone precedent) — draining the key to 0 leaves it **equipped + in
  inventory** (NOT consumed, NO `$msg_broke`); recharging restores travel. The key never drains
  from passive wear, only from teleports.
- **AT-OVERLAY** (card AC, Route A) — standing on a Twisted Portal shows a HUD list of nearby
  Twisted Portal rune names + bearings. **Documented caveat:** on a dedicated server the list is
  limited to the client's held ZDO window (~64–128 m), not a guaranteed 300 m (§2/§7.2) — this is
  an accepted v3.0 limitation, verified + noted, not a bug.
- **AT-NOMAP-SAFE** — the overlay renders with the SB server's default NoMap (no minimap) — it does
  not depend on the map being on (the Sunstone Lens / Iron Compass HUD doctrine).
- **AT-VANILLA-ONLY** (card AC) — no third-party portal-mod code read or copied; all hooks are
  base-game primitives (the named-directory + key-charge mechanics are net-new SBPR fiction).
- *(C4, only if commissioned)* **AT-OVERLAY-THROUGHTERRAIN** — floating rune labels render through
  terrain; **AT-DIRECTORY-PICKER** — a selectable 300m directory teleports to the chosen portal,
  correct on a dedicated server (requires the RPC sync).

---

## 11. Cross-doc updates (spec-first — move in the SAME PR as this spec) + decision log

### Cross-doc updates this spec PR carries (the docs half of spec-first)
- **`docs/v3/planning/index.md`** + **`docs/v3/planning/README.md`** — add this file's row/blurb
  (the v3 planning manifest, mirroring how the Sunstone Lens spec registered).
- **`docs/design/nomap.md` §7** — annotate (do not delete) the historical Twisted Portal note with
  a one-line "→ see `docs/v3/planning/twisted-portal-impl-spec.md` (named-directory, additive,
  multiplayer-corrected; bukeberry→Pukeberries; `GetAllZDOsWithPrefab`→`…Iterative`)" forward
  pointer, and correct the two factual drifts the impl pass found (the API name and the item name).
- **`docs/datasets/PIECES_AND_CRAFTABLES.md:421`** — split the "Twisted Portal (v3+)" future line
  into proper item+piece dataset rows (the Twisted Portal piece + the Twisted Key item), per the
  dataset's format, tagged v3 Swamp.
- *(NOTE: `docs/v3/planning/` does not yet exist on the `v1` integration branch — it lives only on
  the unmerged Sunstone Lens branch. This spec scaffolds the folder. If the Sunstone PR merges
  first, rebase; if this merges first, the Sunstone PR inherits the folder. Coordination flagged in
  the PR description.)*

### Decision log (what's locked vs. what waits on Daniel)
**Locked by the architect this pass (grounded in precedent — no Daniel input needed):**
- Distinct class `SBPR_TwistedPortal`, NOT inheriting `TeleportWorld` (card AC#1; tag-collision).
- Reimplement teleport via `Player.TeleportTo`, omitting the `NoPortals` check (card AC#2).
- Additive construction reusing the Ancient Portal shell + kitbash (ADR-0006).
- Hammer-placed, no station, solid-earth-only (the Ancient Portal's locked placement choices).
- Trinket key with durability-as-charge + the no-break-at-zero drain-prefix (the Sunstone Lens
  precedent, cloned).
- `EatFood` + `RemoveOneFood` postfixes for charge; Pukeberries→`SE_Puke`→`RemoveOneFood` linkage
  (decomp-verified; item name corrected to `Pukeberries`).
- Rune name in the dedicated `sbpr_rune_name` ZDO slot, off `s_tag` (card AC).
- Naming UX = vanilla `TextInput` rename dialog via `TextReceiver` (Q3a).

**🔴 BLOCKED — waiting on Daniel (the §1 / §2 gate):**
- **Q1** coexist vs replace vanilla/Ancient portals (default: coexist).
- **Q2** charge economy numbers + premium-vs-routine lean (defaults proposed, exposed as config).
- **Q3** destination UX — Model A name-pairing (recommended) vs Model B directory picker.
- **§2** ratify Model A (server-side name-pairing, 300m overlay as best-effort cosmetic) vs commit
  to the Model B custom-RPC directory sync.
- Secondary flags folded into the above: NoBossPortals keep/lift; ore-ban keep/lift; must-equip-key
  vs any-carried-key; the AT-NO-VANILLA-PAIR `s_tag` approach (a/b) — engineer-resolvable in-game
  but noted.

Once Daniel answers, the architect creates C1–C3 (§9) and this card completes. **No impl card is
created, and no code is written, until then.**
