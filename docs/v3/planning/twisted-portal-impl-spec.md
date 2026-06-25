---
title: "Twisted Portal — long-range named portal network, food-as-fuel cost model (v3 impl spec)"
status: proposed
purpose: "Build-ready architect spec for the v3 Swamp-tier Twisted Portal: a distinct portal class that teleports even where vanilla portals are blocked (NoPortals), addressed by player-assigned RUNE NAMES, with travel range gated by FOOD-AS-FUEL — NO key trinket. RECONCILED 2026-06-24 to the locked cost model in docs/design/twisted-portal-food-charge.md (PR #270 + #271, merged): Portal Energy PE(slot)=remaining_minutes x tier(food) summed over active belly slots; a teleport spends PE as food-time scaled by distance (so distance both costs provisioning AND lands you depleted); Bukeperries (vanilla Pukeberries) are a burnable EMERGENCY RESERVE spent only on the shortfall (30 m/berry, 10 = the 300 m ceiling), and a berry-burning jump arrives food-empty AND Feeling Sick (vanilla SE_Puke). The superseded SBPR_TwistedKey durability-battery economy is REMOVED from the build surface entirely. Q2 (charge economy) is RESOLVED -> food-as-fuel; Q1 (coexist) and Q3 (Model A name-pairing + informational overlay) are LOCKED as the architect's bottom-line recommendations. The portal mechanism, NoPortals bypass, rune-name ZDO pairing, the through-terrain overlay, and the server-side 300 m multiplayer finding are UNAFFECTED by the cost-model change. Every decomp line re-verified this pass against assembly_valheim. The child impl cards (C1 portal core t_2b388cd5, C2 cost model t_6e992a30, C3 overlay t_e732bd8b) are already created with this card as parent and auto-promote when this reconciliation merges. Daniel gates the merge (docs-only PR)."
supersedes_partial:
  - "This spec's own former §5 (the SBPR_TwistedKey food-charged Trinket durability-battery economy) is REPLACED by food-as-fuel per docs/design/twisted-portal-food-charge.md. No SBPR_TwistedKey item, no EatFood/RemoveOneFood charge postfixes, no DrainEquipedItemDurability prefix. The portal class, rune-name pairing, NoPortals bypass, through-terrain overlay, and server-side 300 m pairing are carried forward unchanged."
---

# Twisted Portal — long-range named portal network, food-as-fuel cost model

The design is locked across two docs: the *what* of the portal mechanism
([`nomap.md` §7 "Twisted Portal (THE BIG ONE)"](../../design/nomap.md)), and the *what* of the
travel-cost model ([`twisted-portal-food-charge.md`](../../design/twisted-portal-food-charge.md),
**accepted** — Daniel locked food-as-fuel + the Bukeperry reserve 2026-06-24, PR #270 + #271).
The kanban card **t_f9cab392** is the original acceptance shape; this doc was **reconciled to
food-as-fuel** under card **t_c15411b2**. This is the buildable *how*: the multi-prefab
architecture, the vanilla hooks re-verified against the decomp, the named-directory teleport model
that replaces vanilla's 1:1 tag pairing, the **food-as-fuel Portal Energy engine** (no key), named
acceptance tests, the `Features/Portals/` placement, and the SpecCheck manifest impact.

> 🔄 **RECONCILED 2026-06-24 (card t_c15411b2): the cost model is now FOOD-AS-FUEL, not a key.**
> The earlier draft of this spec described an `SBPR_TwistedKey` Trinket whose durability was a
> charge meter (eat food → charge; teleport → spend; Pukeberries → dump-charge). Daniel locked a
> different model: **there is no key.** Teleport range is gated by the food in your belly (**Portal
> Energy** `PE = remaining-food-minutes × a stat-derived tier`), a jump spends PE as food-time
> scaled by distance, and **Bukeperries** are a burnable emergency reserve for the shortfall. §5
> below is the rewritten cost model; the authority for every number is the design doc. The portal
> mechanism (§3–§4, §6), the overlay (§7), and the multiplayer finding (§2) are **unchanged** by
> this — only the cost economy moved.

> **Clean-side note (ADR-0001 — `0001-clean-room-no-jotunn.md`):** every decomp line cited here is
> the base game (`assembly_valheim`), which is **fair game to read and adapt** (repo AGENTS.md + the
> 2026-06-09 clarification). Line numbers are from
> `~/valheim/worldgen-spike/decomp/assembly_valheim.decompiled.cs` (this box) and were **re-grepped
> live during the food-as-fuel reconciliation pass (2026-06-24)** — re-confirm against the build
> assembly if the decomp drifts. `PortalIndicator` / Rune-Magic mods are **reference-only**: their
> *behaviour* (through-terrain portal labels; rune-of-detection) is reproduced from vanilla
> primitives only — no third-party code is read or copied. The named-directory + the food-as-fuel
> Portal Energy mechanics are net-new SBPR fiction.

> **ADR-0006 (load-bearing):** every prefab here is built **additively** (`new GameObject()` +
> `AddComponent`), reading vanilla prefabs only as blueprints via `ZNetScene.GetPrefab` /
> `vprefab inspect`. We do **NOT** `Instantiate` `portal_wood` (it drags `ZNetView` + a
> `PlayerBase` EffectArea + `GuidePoint` + `portal_destruction` — the cairn-soft-lock /
> Explorer's-Bench-GuidePoint bug class). The portal piece reuses the Ancient Portal's proven
> additive shell + grafted-ring kitbash (`docs/v2/planning/ancient-portal-impl-spec.md`).

> 🟢🟢 **STATUS after reconciliation: the cost economy is DECIDED (food-as-fuel, design-locked).**
> The two pacing/UX calls that remained — **Q1** (coexist vs replace) and **Q3** (destination UX) —
> are LOCKED to the architect's bottom-line recommendations (**Q1 = coexist, Q3 = Model A**), which
> is what the already-created child cards build against. The old "🔴 BLOCKED ON DANIEL: three design
> decisions" gate is **retired**: Q2 is resolved by the merged design doc; Q1/Q3 are the
> low-risk, vanilla-congruent defaults this spec always recommended. The remaining secondary flags
> (NoBossPortals keep/lift, ore-ban keep/lift, the AT-NO-VANILLA-PAIR `s_tag` approach) are
> engineer-resolvable in-game and noted per-section, not gates. If an engineer finds a hard reason
> to reopen Q1 or Q3 mid-build, they BLOCK with the specific finding rather than guessing (§1).

---

## 0. SpecCheck manifest impact (read first — it moves with the code)

`Runtime/SpecCheck.cs` holds the recipe drift manifest. Under food-as-fuel this feature adds
**+1 entry** (down from the former +3 — **the `SBPR_TwistedKey` item row is REMOVED**, and the
Sunstone row was always pre-existing, not re-added):

| # | Manifest entry | Kind | Resources | Station |
|---|---|---|---|---|
| 1 | `piece_sbpr_twisted_portal` | build piece | **see Q1 / §4.7 — recipe shape depends on coexist-vs-replace** (proposed, Q1 = coexist: `FineWood ×20, GreydwarfEye ×10, SurtlingCore ×4, SBPR_Sunstone ×1`) | (Hammer-placed; `m_craftingStation = null`) |
| — | ~~`SBPR_TwistedKey`~~ | ~~item recipe~~ | **REMOVED — no key trinket under food-as-fuel.** The whole row, its icon-ship requirement, and its `Item`-shape gotcha are gone. | — |
| — | `SBPR_Sunstone` | *(EXISTING — shared v3 material, already a SpecCheck row from the Sunstone Lens; this feature CONSUMES it via the portal recipe, does not re-add it)* | — | — |

> 🔄 **Reconciliation note:** the former manifest listed **+3** rows (a `SBPR_TwistedKey` item row,
> the portal piece row, and a Sunstone caveat). Killing the key drops the item row entirely, so the
> net is **+1 new row** (the portal piece). The Bukeperry reserve adds **no recipe** — Pukeberries
> are a vanilla loot item the feature *reads*, not a craftable; it never touches SpecCheck.

**Resource prefab-name caveats (must match vanilla internal IDs / SBPR consts, or SpecCheck flags a
NULL `m_resItem`) — verified this pass against the wiki corpus `Internal ID` field
(`~/valheim/sbpr-corpus/wiki/fandom/`):**
- `SBPR_Sunstone` = the shared v3 material const `SunstoneLens.SunstoneName` — referenced via the
  const, **never a literal**. The Twisted Portal network is Sunstone's second consumer (after the
  Lens) — the "more than one use" design intent realized. Spec-wires Twisted to depend on the
  Sunstone feature.
- GreydwarfEye = vanilla **`GreydwarfEye`** — already `MarkerSigns.EyeResource`; reference that
  const (the Ancient Portal precedent, `Portals.cs:71`), don't hardcode.
- FineWood / SurtlingCore = vanilla **`FineWood`** / **`SurtlingCore`** (portal-family mats).
- *(`Iron` and `Bloodbag` are dropped from the manifest — they were the proposed KEY recipe
  resources; with no key there is no key recipe. If Daniel later wants the portal piece itself
  Iron-gated to hold true Swamp tier, that's a one-line recipe edit + this row, flagged not built.)*

**The SpecCheck shape (gotcha — same as the Ancient Portal §0):** row 1
(`piece_sbpr_twisted_portal`) is `Piece` only (no `Item`, no `Station`). A `RecipeSpec` with both
`Item` and `Piece` null, or both set, is silently skipped — match the shape exactly. The portal
**piece** uses `m_icon` for the Hammer menu (absent = no thumbnail, non-fatal). **There is no longer
an additively-constructed `Item` shell** (the former key), so the `SpecCheck.CheckIcon` (C1)
boot-ERROR-on-missing-PNG concern that applied to the key **no longer applies** to this feature.

The card that touches `SpecCheck.cs` cites **this doc** alongside the existing sources. Code +
spec + SpecCheck row move in the **same PR** (spec-first rule).

---

## 1. Design questions — Q2 RESOLVED, Q1 + Q3 LOCKED (the gate is retired)

The card body named three open questions. They are now settled:

### Q1 — Coexist with, or replace, vanilla/Ancient portals once unlocked? → **LOCKED: COEXIST**
*(card OpenQ 1; also `nomap.md` §7 open #3)*

**Locked to COEXIST (additive, disable nothing)** — the architect's standing recommendation, and
the answer the child portal-core card (t_2b388cd5) is written against. Twisted is a separate prefab
on the Hammer menu; building it does not touch vanilla `portal_wood` or
`piece_sbpr_ancient_portal`. Lowest-risk, most-reversible; keeps the Ancient Portal's "convenience
portal that keeps the ore ban" identity intact (the two are deliberately different tools —
`PIECES_AND_CRAFTABLES.md:442`); matches the Trailborne philosophy of *adding* options, not removing
vanilla ones. **What it changes in the build:** §4.7 is empty (zero gating patches). If an engineer
discovers a hard reason REPLACE is required, they **BLOCK with the specific finding** rather than
silently building a `PieceTable`-filter patch + global-key unlock (a meaningfully larger, more
invasive build that touches vanilla content the rest of Trailborne leaves alone).

### Q2 — Charge economy → **RESOLVED: FOOD-AS-FUEL (no key)**
*(card OpenQ 2 — superseded by the merged design doc)*

🟢 **RESOLVED by [`twisted-portal-food-charge.md`](../../design/twisted-portal-food-charge.md)
(accepted, PR #270 + #271).** The "key durability battery" framing of this question is **void** —
there is no key. The cost model is **food-as-fuel Portal Energy** (§5): each active belly food slot
contributes `PE = remaining_minutes × tier`, a teleport spends PE as food-time scaled by distance,
and Bukeperries are an emergency reserve for the shortfall. The *numbers* (the `/30` tier divisor,
the `[1,5]` clamp, the `28 m` feast cap, `30 m`/berry, the meters-per-PE conversion) are explicit
**tuning knobs** the design doc marks live (playtest may retune them; the architecture is fixed) —
exposed as BepInEx config per the Sunstone Lens precedent. **What it changes in the build:** the
entire §5 (now the PE engine, not a key), §8 (the patch surface **collapses** — food-as-fuel needs
**zero Harmony patches**, where the key needed three), and the SpecCheck manifest (§0, the key row
removed). This is the substance of the t_c15411b2 reconciliation.

### Q3 — Rune-name UX (naming + destination choice) → **LOCKED: Model A**
*(card OpenQ 3; `nomap.md` §7 — "on-step shows visible portal names")*

Two interactions, both locked:

- **(a) Naming — reuse the vanilla `TextInput` rename dialog** (the path vanilla portals use for
  their tag: `TeleportWorld.Interact` → `TextInput.instance.RequestText(this, …)`). Our class
  implements `TextReceiver` (`GetText`/`SetText`, decomp interface `:54810`); `[Use]` (E) opens the
  rename box, the typed string is written to the `sbpr_rune_name` ZDO slot (§6). Zero new UI;
  matches every other nameable SBPR piece (the Surveyor's Table rename, the Marker Signs).
- **(b) Destination choice — LOCKED to Model A ("named-tag pairing").** A Twisted Portal teleports
  to *the nearest other Twisted Portal that shares its rune name*, mirroring vanilla's tag-pair
  semantics but on our `sbpr_rune_name` slot instead of `s_tag`. The player "picks a destination" by
  **naming two portals the same rune** — no new selection UI. The 300 m on-step list (§7) is purely
  **informational**, not a picker. Smallest, most vanilla-congruent build; routes teleport
  resolution **server-side** (multiplayer-correct by construction, §2). **Model B** (a selectable
  many-to-many directory picker + cross-client RPC sync) is explicitly **out of v3.0 scope** — it
  is a later milestone only if Daniel later wants the directory-picker fantasy. If an engineer reads
  the feature as REQUIRING Model B's picker, that's a scope expansion — **BLOCK and flag it**, don't
  discover it mid-impl. **What it changes in the build:** §4.4a (name-match resolution), §7 (overlay
  is informational), and §2's directory sync stays optional/out-of-scope.

> 🟢 **Architect's bottom line (now locked):** the cheap, shippable, vanilla-congruent v3.0 is
> **Q1 = coexist, Q2 = food-as-fuel (design-locked), Q3 = Model A name-pairing with an
> informational overlay.** That combination is patch-light (the cost model is now entirely
> patch-free), multiplayer-safe, and reuses proven SBPR precedents. The richer readings (replace;
> directory picker) remain *real* later options, each a larger build, each requiring an explicit
> Daniel go-ahead before an engineer builds it.

---

## 2. 🔴 The multiplayer 300m-proximity fork (architect finding — UNAFFECTED by the cost-model change)

> 🔄 **Reconciliation note:** this finding is about the *portal proximity query*, not the cost
> model — so food-as-fuel leaves it **entirely intact**. It is preserved verbatim (citations
> re-verified this pass) because it is still the single highest-risk finding in the portal
> mechanism, and Model A (locked Q3) is what defuses it.

The design doc (`nomap.md` §7) says: *"query `ZDOMan` for all ZDOs of the SBPR prefab hash within
radius (cheap — there's already `ZDOMan.GetAllZDOsWithPrefab` available)."* **That works in
singleplayer and BREAKS on a dedicated server**, and the design doc missed it — exactly as the
Ancient Portal spec caught the `PortalPrefabHash` pairing gotcha that *its* design doc missed.

**Why it breaks (re-verified against the decomp this pass):**
- A client does **not** hold every ZDO in the world. The server sends a client only the ZDOs within
  its **active sector window**: `ZDOMan.CreateSyncList` (`:65244`) → `FindSectorObjects(zone,
  m_activeArea, m_activeDistantArea, …)` (`:65212`, called at `:65252`). The window is purely
  sector/distance-based.
- `ZoneSystem.m_zoneSize = 64f` (`:96263`), `m_activeArea = 1` (`:96258`), `m_activeDistantArea = 1`
  (`:96260`). That is a **±1 active zone (~128 m of full ZDOs) + ±1 distant ring**, and the distant
  ring only carries `m_distant`-flagged ZDOs and is only appended when the near sync list has room
  (`if (toSync.Count < 10)`, `:65261`). A build piece is **not** `m_distant` by default.
- Net: a multiplayer client reliably holds Twisted Portal ZDOs only within **~one to two zones
  (~64–128 m)** of itself — **never a guaranteed 300 m**. So `ZDOMan.GetAllZDOsWithPrefabIterative`
  (`:65497`, the real API — note it is the *iterative* variant; the design doc's
  `GetAllZDOsWithPrefab(int)` name does not exist as written) returns only the *locally-held* subset
  on a client. On a dedicated server the result is silently short.
- **Vanilla portals dodge this entirely:** they never do a client-side range query. Pairing is
  resolved **server-side** in `Game.ConnectPortals` (`:84570`) over the `m_portalObjects` list
  (`:64445`) the server maintains for every prefab whose hash is in `Game.instance.PortalPrefabHash`
  (the add path is `:64704`/`:64706`). Registering our hash there (§4.3) populates the **server's**
  list, not the client's view.

**The three resolution options (architect-ranked):**

1. ✅ **Model A (name-tag pairing) makes this a NON-issue for *travel* — LOCKED (Q3).** Under Model
   A, teleport resolution is a **name match resolved exactly like vanilla tag-pairing** —
   server-side over the `m_portalObjects` list (our hash is registered), using `sbpr_rune_name`
   instead of `s_tag`. The server has every Twisted ZDO; the 300 m client query is then needed
   **only for the cosmetic on-step overlay** (§7), where "I only see the portals my client currently
   holds (~64–128 m)" degrades gracefully to "the overlay shows nearby portals, maybe not the full
   300 m on a far-flung server." That cosmetic shortfall is acceptable for v3.0 and is explicitly
   flagged in AT-OVERLAY. **This is why Model A is the locked Q3 answer: it removes the hardest
   multiplayer risk from the critical path.**

2. ⚠️ **Custom RPC directory sync (REQUIRED only if Daniel later picks Model B).** To populate a
   true 300 m directory on a dedicated client, the owner/server must *push* the portal directory to
   the client. The repo has the precedent: `SurveyorTableTag` uses `nview.Register<ZPackage>(…)` to
   sync a ZDO blob owner→client (`Cartography/SurveyorTableTag.cs`). A Twisted "rune registry" node
   would have the server maintain the authoritative directory (name → position) over the
   `m_portalObjects` list filtered to our hash, and RPC-send the within-300 m slice to the stepping
   client. This is a **real, multi-day build** — the bulk of why Model B is "HIGH risk." Specced
   here so it's ready *if* commissioned; **out of v3.0 scope** under the locked answers.

3. ❌ **Force `m_distant = true` on the portal ZNetView.** Tempting (distant ZDOs sync at longer
   range), but rejected: the distant ring is gated behind `toSync.Count < 10` (`:65261`), is still
   only ±1 distant zone, carries no 300 m guarantee, and marking a teleport-bearing build piece
   `m_distant` has unknown interactions with the portal-hash sync path. Not a real fix; noted so a
   future worker doesn't rediscover it as a "clever" shortcut.

> 🟢 **Architect resolution (locked):** ship **Model A**, which routes teleport resolution through
> the server-side `m_portalObjects` path (multiplayer-correct by construction) and lets the 300 m
> overlay be a best-effort client cosmetic. The custom-RPC directory (option 2) is specced so it's
> ready *if* Daniel ever picks Model B, but it is explicitly **out of v3.0 scope**. This finding is
> the reason Q3 locks to Model A.

---

## 3. Architecture — two prefabs + one tag (the key prefab is GONE)

`Features/Portals/` already exists (the Ancient Portal). Twisted lands in the **same feature
folder**. Under food-as-fuel the build surface **drops the `SBPR_TwistedKey` ItemDrop entirely**:

| Prefab / type | Kind | Role |
|---|---|---|
| `piece_sbpr_twisted_portal` | build `Piece` (Hammer) | The portal. A **distinct class** `SBPR_TwistedPortal : MonoBehaviour, Hoverable, Interactable, TextReceiver` — does NOT inherit `TeleportWorld` (tag-collision avoidance, card AC#1). Carries our own teleport + rune-name + **the PE-debit call** (§4.4 / §5). |
| ~~`SBPR_TwistedKey`~~ | ~~`ItemDrop` (Trinket)~~ | **REMOVED.** Food-as-fuel has no key. Travel is gated by belly Portal Energy, read on demand at teleport time — there is no item to construct, equip, charge, or drain. |
| `SBPR_TwistedPortalTag` | `MonoBehaviour` | Per-instance ZDO discipline (rune-name slot, owner-write, ghost guard) — the `AncientPortalTag` / `CairnTag` precedent. (May be folded INTO `SBPR_TwistedPortal` since that's already a per-instance MonoBehaviour — see §4.1.) |
| `SBPR_Sunstone` | *(shared v3 material)* | Consumed by the portal recipe; already shipped by the Sunstone Lens. |

**Files (mirrors the Ancient Portal layout in `Features/Portals/`):**
- `Features/Portals/TwistedPortal.cs` — `RegisterPrefabs` + `DoObjectDBWiring` for the portal piece
  + the `SBPR_TwistedPortal` class (the distinct teleporter; owns the teleport entry point C2 hooks).
- `Features/Portals/TwistedPortalEnergy.cs` — **the food-as-fuel cost engine (NEW, replaces
  `TwistedKey.cs`).** A mostly **engine-free** PE calculator: `tier(food)`, `PE(player)`, the
  distance→food-time debit, the Bukeperry-shortfall solve, and the `SE_Puke` arrival application.
  Reads `Player.GetFoods()` on demand; **no Harmony patches** (see §5, §8). The pure-math core
  (tier curve, PE sum, berry-shortfall) lands as a link-compiled engine-free truth table (the
  `SunstoneHaloGeometry.cs` / `CompassNorthGate.cs` precedent) so it's CI-gated (AT-PE-MATH).
- `Features/Portals/TwistedPortalOverlay.cs` — the on-step proximity overlay (§7), client-only.
- Wire `TwistedPortal` into `Runtime/Registrar.cs` (after `Portals` / `SunstoneLens`, since Twisted
  consumes Sunstone and reuses Portal helpers). **No patch registration is needed for the cost
  model** — the only thing `Plugin.Awake` must register is whatever the overlay/proximity needs, if
  anything (the portal/teleport/trigger/ZDO/PE machinery is **patch-free by construction**).

**The key architectural distinction from the Ancient Portal (card AC#1):** the Ancient Portal *adds
a real vanilla `TeleportWorld`* (inheriting tag-pairing + ore-ban + the NoPortals check). Twisted
**cannot** — `TeleportWorld.Teleport` hard-enforces `GlobalKeys.NoPortals` (`:123008`), the exact
thing Twisted must bypass. So Twisted **reimplements the small slice of teleport it needs** in
`SBPR_TwistedPortal`, omitting the NoPortals gate, and **debits belly Portal Energy** instead of key
charge. It still registers its prefab hash in `Game.PortalPrefabHash` (§4.3) so the server tracks it
for name-pairing — but the teleport *method* is ours, not vanilla's.

---

## 4. The Twisted Portal piece (`piece_sbpr_twisted_portal`)

> 🔄 The portal **mechanism** in this section is unaffected by the cost-model change — it is carried
> forward intact. The ONE change is §4.4: the teleport now **debits Portal Energy** (food-as-fuel,
> §5) where the old draft "spent key charge."

### 4.1 Construction (ADR-0006 additive — reuse the Ancient Portal shell)
- Build with **`Assets.TryConstructPieceShell("piece_sbpr_twisted_portal", donor, out var go)`** (the
  Ancient Portal path, `Portals.cs:215` — note the current helper is the `TryX(out)` form after the
  null-remediation refactor, t_0234cc42 / PR #187). Use a wood/organic effect donor (`portal_wood`
  read as a blueprint) so hit/destroy/place sounds read as a portal.
- **Visual kitbash** — reuse the Ancient Portal's grafted ring/legs/roots envelope
  (`Portals.BuildLegs` `:245` / `Portals.BuildRoots` `:248` + the grafted `small_portal` ring) but
  **re-tinted / re-themed** to read as "twisted/swamp" rather than "ancient/forest" — a darker,
  sicklier emission. Exact retint is a flagged art-pass detail (AT-GEOMETRY); v3.0 ships the Ancient
  envelope with a swamp tint so it's visually distinguishable at a glance. All grafts are
  mesh-reference, ZNetView-free (ADR-0006).
- **The per-instance class IS the portal's brain.** Unlike the Ancient Portal (which bolts a real
  `TeleportWorld` + a separate `AncientPortalTag`), Twisted's `SBPR_TwistedPortal` MonoBehaviour *is*
  both the teleporter and the ZDO-discipline owner. It implements `Hoverable` (`GetHoverText`
  /`GetHoverName`, decomp interface `:111336`), `Interactable` (`Interact`/`UseItem`, `:111594`), and
  `TextReceiver` (`GetText`/`SetText`, `:54810`). One MonoBehaviour, no `TeleportWorld`.

### 4.2 Fragility / placement (reuse the Ancient Portal's LOCKED choices)
- `WearNTear.MaterialType.Wood`, `m_health` per the Ancient Portal's 300 default unless Daniel
  re-leans (flag — Twisted is endgame, arguably tankier; default to **300** to match, note as a
  tunable). `m_noRoofWear = true` (no rain decay — the Ancient Portal precedent).
- **Hammer-placed, no station** (`m_category = Misc`, `m_craftingStation = null`,
  `Assets.AddOrReplacePieceByName` onto `Assets.GetHammerPieceTable()`) — identical to the Ancient
  Portal and consistent with the design-pillars Hammer-exception already carved for portals
  (`design-pillars.md`).
- **Solid-earth only** (`m_groundOnly = true`, vanilla field `:116132`) — same as the Ancient Portal.
- Build cost: recipe row 1 (§0), Q1 = coexist shape. Rebuilt authoritatively in `DoObjectDBWiring`
  after the materials resolve (the Ancient Portal ordering).

### 4.3 Prefab-hash registration (the server-side pairing substrate — REQUIRED for Model A)
Register `"piece_sbpr_twisted_portal".GetStableHashCode()` into `Game.instance.PortalPrefabHash`
**exactly as the Ancient Portal does** (`Portals.EnsurePortalHashRegistered`, called at
`Portals.cs:155` — idempotent, null-Game-guarded, re-asserted in `DoObjectDBWiring`). This makes
`ZDOMan` track our portal ZDOs in `m_portalObjects` (add path `:64704`/`:64706`), which is what gives
the **server** an authoritative list of all Twisted Portals for name-pairing (§4.4) — the
multiplayer-correct substrate from §2.

> 🔴 **But do NOT let vanilla `Game.ConnectPortals` auto-connect our portals on `s_tag`.** Vanilla
> `ConnectPortals` (`:84570`) pairs `m_portalObjects` entries by their `s_tag` ZDO string. Our
> portals must pair on `sbpr_rune_name`, NOT `s_tag` — and our portals must never form a vanilla
> `ConnectionType.Portal` ZDO connection (that's vanilla's 1:1 channel; ours is a name-directory).
> **Mitigation:** never write `s_tag` on a Twisted Portal ZDO. **This is a real interaction to
> verify in-game (AT-NO-VANILLA-PAIR):** confirm two unnamed Twisted Portals do not get
> auto-connected by vanilla's `ConnectPortals` into a spurious `ConnectionType.Portal` link. If they
> do (empty-tag collision), the fix is either (a) seed a unique per-portal sentinel into `s_tag` so
> vanilla never pairs them, or (b) keep our hash OUT of `PortalPrefabHash` and resolve name-pairing
> entirely in our own code over `ZDOMan.GetAllZDOsWithPrefabIterative` (`:65497`) server-side.
> **Architect lean: option (b) is cleaner** — it fully decouples us from vanilla's portal-pairing
> machinery and avoids any `s_tag` games, at the cost of doing our own server-side directory walk
> instead of reusing the `m_portalObjects` list. The engineer picks (a) vs (b) after the in-game
> AT-NO-VANILLA-PAIR check; this is the #1 build-time decision inside the portal-core card.

### 4.4 Teleport — our reimplementation, NoPortals omitted, PE debited (card AC#2 + food-as-fuel)
The activation surface is the **overhead jump-through trigger** (the Ancient Portal's
`TeleportWorldTrigger` precedent) — BUT since we don't have a vanilla `TeleportWorld`, the trigger
calls **our** teleport. Reproduce the minimal slice of `TeleportWorld.Teleport` (`:123002`) we want,
deliberately **omitting the NoPortals check** (`:123008`), and **debiting belly Portal Energy**
(§5) instead of any key charge:

```
SBPR_TwistedPortal.Teleport(Player player):
   if player == null: return
   target = ResolveDestination()        # §4.4a — name-match, server-side (Model A)
   if target == null: message "$sbpr_twisted_no_destination"; return
   D = Vector3.Distance(player.pos, target.pos)        # the jump distance
   # NO GlobalKeys.NoPortals check — this is the whole point (AC#2)
   # NoBossPortals: KEEP the boss check (:123013) — flagged for Daniel, default KEEP
   # Ore-ban: KEEP — player.IsTeleportable() (:57606); block "$msg_noteleport" (flag, default KEEP)
   #
   # FOOD-AS-FUEL GATE + DEBIT (§5) — replaces the old key-charge gate:
   result = TwistedPortalEnergy.TrySpendForJump(player, D)   # drains belly PE, then berries
   if not result.ok: message result.reason; return           # e.g. "not enough fuel even with berries"
   pos = target.position + (target.rotation * Vector3.forward) * exitDistance + Vector3.up
   player.TeleportTo(pos, target.rotation, distantTeleport: true)      # :20771 — the clean primitive
   if result.burnedBerries > 0:
       player.GetSEMan().AddStatusEffect(SE_Puke.NameHash(), resetTime:true)  # §5.4 — arrive Feeling Sick
```

- **`Player.TeleportTo(pos, rot, distantTeleport)`** (`:20771`) is the clean public primitive —
  owner-RPC-safe, handles the 2 s fade + area-ready wait + floor-find. We do NOT hand-poke
  transforms. Same call vanilla `TeleportWorld.Teleport` ends in (`:123031`).
- **The PE debit + berry-shortfall + Feeling Sick is the cost model (§5).** The portal-core card
  (C1, t_2b388cd5) exposes this **teleport entry point + the computed `D`** as the seam; the
  cost-model card (C2, t_6e992a30) implements `TwistedPortalEnergy.TrySpendForJump`. C1 must not
  invent PE math; C2 must not re-derive the teleport. The seam is `D` (distance, meters) in,
  `{ok, reason, burnedBerries}` out.
- **Ore-ban (card "otherwise behaves"): KEEP by default** — call `player.IsTeleportable()`
  (`:57606`) and block with `$msg_noteleport` if carrying ore, exactly like vanilla (`:123018`).
  The card lifts the *NoPortals* restriction, not the *ore* restriction. **Flag for Daniel:** does
  the endgame portal also let ore through? Default **NO** (keep the ban); it's a one-line flip.

#### 4.4a `ResolveDestination()` — server-correct name pairing (Model A, unchanged)
- Walk the authoritative portal set **server-side**: the owner of this portal (or the server)
  enumerates Twisted Portal ZDOs (via the `m_portalObjects` list filtered to our hash, OR our own
  `GetAllZDOsWithPrefabIterative` `:65497` walk per §4.3 option b), filters to those whose
  `sbpr_rune_name` equals this portal's, excludes self, and picks the nearest (or the single paired
  one). Returns its position+rotation.
- Because this runs where the full ZDO set exists (server/owner), it is **not** subject to the
  client's 64–128 m window (§2) — the core reason Model A is multiplayer-correct.
- If resolution must happen client-side and the destination is outside the client's held set, route
  through the portal owner via a routed RPC (the `SurveyorTableTag` RPC precedent). For v3.0 the
  common case (host owns world ZDOs) resolves directly; the RPC fallback is the robustness path.

### 4.5 The overhead jump-through trigger (reuse the Ancient Portal, AC — jump to travel)
Identical mechanism to the Ancient Portal's `BuildOverheadTrigger` (`Portals.cs:317`): a child
`BoxCollider{isTrigger=true}` + a small trigger MonoBehaviour whose `OnTriggerEnter` calls
`SBPR_TwistedPortal.Teleport(player)`. **Size is desk-estimated, FLAGGED for in-game tuning**
(AT-JUMP-ACTIVATE) — the same ~2.6 m × ~0.9 m envelope the Ancient Portal uses, reused verbatim.
(Twisted has no grow timer — active on placement, §4.6 — so the trigger is enabled immediately.)

### 4.6 No grow timer (a deliberate difference from the Ancient Portal)
The Ancient Portal's 15 s grow is its planted-seed fantasy. Twisted is *built*, not *grown* — active
on placement. So `SBPR_TwistedPortal` skips the grow lifecycle; the trigger is live once placed
(gated only by the PE check at teleport time, §5). (If Daniel wants a "warm-up" flourish, it's an
easy add later — flag, don't pre-build.)

### 4.7 Vanilla-portal gating (ONLY if Q1 = REPLACE — under the locked Q1 = coexist, this is EMPTY)
Under the **locked Q1 = coexist**, this section is empty — Twisted disables nothing. If Daniel ever
reverses to REPLACE, this is where a `PieceTable`-filter patch hides the vanilla `portal_wood` (and
possibly Ancient Portal) recipe behind a Twisted unlock global-key, plus an AT. Specced as a stub so
the decision has a home; not built under the locked answer.

---

## 5. The cost model — FOOD-AS-FUEL Portal Energy (replaces the TwistedKey entirely)

> 🔄 **This entire section was rewritten in the t_c15411b2 reconciliation.** The former §5 specced
> an `SBPR_TwistedKey` Trinket with durability-as-charge, two `EatFood`/`RemoveOneFood` charge
> postfixes, a `DrainEquipedItemDurability` prefix, and stack-split logic. **All of that is deleted.**
> The authority for everything below is
> [`twisted-portal-food-charge.md`](../../design/twisted-portal-food-charge.md) (accepted). The
> cost-model card is **C2 (t_6e992a30)**.

**The whole model in one line:** how far you can teleport is set by the food in your belly. There is
**no key, no item, no equip slot.** A jump spends food-time scaled by distance, so a long haul both
*costs* provisioning and *lands you depleted*. Bukeberries are an emergency reserve for the overflow.

### 5.1 The decomp surface — Portal Energy is a pure on-demand READ (no patches, no accumulation)

This is the load-bearing reconciliation finding the engineer must internalize: **food-as-fuel needs
zero Harmony patches.** The old key model patched `EatFood`/`RemoveOneFood` because it had to
*accumulate* charge into an item as food was eaten. Food-as-fuel accumulates nothing — it **reads the
live belly state at teleport time** and mutates it inline. The surfaces (re-verified this pass):

- **`Player.GetFoods()`** (`:17598`) — public; returns the live `List<Player.Food>` (`m_foods`,
  `:15604`; max **3 slots**, enforced at `:17423`/`:17498`). This is the PE read surface. No patch
  needed — it's a public getter on the local player.
- **`Player.Food`** (the slot struct, `:15321`) carries everything PE needs per slot:
  - `m_item` (`:15325`) — the `ItemDrop.ItemData`; its `m_shared.{m_food, m_foodStamina, m_foodEitr}`
    (`:57786`–`:57790`) are the food's **base** stat budget → the **tier** input.
  - `m_time` (`:15327`) — **remaining seconds** on this slot, counted down each second in `UpdateFood`
    (`:17534`, `food.m_time -= 1f`). `remaining_minutes = m_time / 60` → the **PE minutes** input.
  - `m_item.m_shared.m_foodBurnTime` (`:57792`) — the slot's max seconds (the full clock).
- 🔴 **Tier reads the BASE budget, not the decayed live stat.** `UpdateFood` decays the live
  `food.m_health/m_stamina/m_eitr` along a `Mathf.Pow(f, 0.3f)` curve as the slot empties
  (`:17535`–`:17539`). **Do NOT** feed those decayed values into `tier()` — that would double-count
  decay (PE already scales by `m_time`). `tier()` must read the **constant** base budget off
  `m_item.m_shared.m_food + m_foodStamina + m_foodEitr`. The remaining-time factor (`m_time`) is the
  *only* thing that should shrink as a slot depletes. This is the single most important correctness
  note in the cost model — flag it loud for C2.

So `TwistedPortalEnergy` is a plain helper that, on demand, walks `player.GetFoods()`, computes
`PE = Σ (slot.m_time/60 × tier(slot.m_item))`, and (on a jump) shortens `slot.m_time` to spend it.
**No `[HarmonyPatch]` anywhere in the cost model.**

### 5.2 Tier — derived from total stats (the design doc's formula, verbatim)

```
total_stats(food) = m_item.m_shared.m_food + m_foodStamina + m_foodEitr     # base budget
tier(food)        = round( clamp(total_stats / 30, 1.0, 5.0) × 2 ) / 2       # snapped to 0.5 rungs
```

- `/30` = ~30 stat-points per whole tier (slope); `clamp(…,1,5)` = floor/ceiling; `round(×2)/2` snaps
  to **0.5 rungs** so foods fall into legible travel classes. Range `[1.0, 5.0]`.
- **Stat-fallback IS the primary rule.** Every vanilla food's tier is computed; every
  modded/unaccounted food gets the identical treatment for **zero hand-authoring**. An **optional
  explicit override registry** (`foodID → tier`) may later pin mis-priced foods, but is not required
  for the system to function — `TwistedPortalEnergy` ships with the formula only. (Design doc §2; the
  full 60-food PE table is §3 there — do not duplicate it into code, derive it.)
- Eitr counts at full weight (mage foods rank highest for travel) — a locked **tuning knob**, not an
  architecture choice (design doc §6.1).

### 5.3 Portal Energy + the distance debit

```
PE(player) = Σ over active slots:  (slot.m_time / 60) × tier(slot.m_item)        # PE "minutes×tier"
belly_range = PE(player) × METERS_PER_PE                                         # PE → meters
```

A jump of distance `D`:
1. Drain belly PE → covers `belly_range` meters. The drain is applied as **food-time removed from
   slots** (shorten each `slot.m_time` proportionally to the PE spent, weighted by tier so the
   "minutes×tier" accounting stays exact). A `UpdateFood(0f, forceUpdate:true)` (`:17526`) style
   refresh — or letting the next tick recompute — re-derives Max HP/Stamina/Eitr from the shortened
   slots, so the player visibly weakens.
2. `D ≤ belly_range` → done. Arrive depleted (the coupling). **ZERO berries spent.**
3. `D > belly_range` → belly empties; the shortfall is paid by Bukeberries (§5.4).

> 🔴 **`METERS_PER_PE` is a tuning knob the design doc leaves to playtest.** The doc fixes the
> *architecture* (distance drains food-time; long jump → arrive depleted) and the *berry* conversion
> (30 m/berry), but does **not** pin the belly-PE→meters constant. **C2 must NOT invent it from
> nowhere** — derive a defensible baseline from the locked anchors (the 300 m portal ceiling, the
> PE-150 best personal food, the "a full belly is a meaningful but not unlimited range" intent) and
> **expose it as a BepInEx config knob**, OR, if no defensible baseline falls out of the design doc's
> numbers, **BLOCK for Daniel** rather than guess. This is the one genuinely under-specified constant
> in the cost model; the card body for C2 says so explicitly.

### 5.4 Bukeberries — the emergency reserve (vanilla `Pukeberries`)

> **Naming (corpus-verified):** the vanilla item is **`Pukeberries`** (internal id `Pukeberries`,
> `~/valheim/sbpr-corpus/wiki/fandom/Pukeberries.md`; "Bukeperries" is the wiki's spoonerism display
> name). Use the internal id `Pukeberries` in any reference. Drops from Greydwarf shaman (Black
> Forest) + Fuling shaman (Plains), stack 50 @ 0.1 weight.

Bukeberries are a **burnable bonus PE source, spent ONLY for the shortfall** when belly PE can't
cover the jump (design doc §5):

```
shortfall = D − belly_range                          # only reached when D > belly_range
berries_needed = ceil(shortfall / BUKE_METERS_PER_BERRY)     # 30 m/berry baseline
if inventory Pukeberries ≥ berries_needed:
    remove berries_needed Pukeberries; jump succeeds; arrive food-empty AND Feeling Sick
else:
    block the jump ("not enough fuel"); spend nothing
```

- **30 m per berry → 10 berries = 300 m.** Not arbitrary: 300 m is the Twisted Portal's own
  pairing/visible range (`nomap.md` §7). A from-empty, maximum-range jump costs **exactly 10
  berries**, and you can never need more than 10 on one jump (you can't jump farther than 300 m).
- **Berries extend reach, they do not refill you.** They fire only *after* the belly is empty, so the
  "long jump = arrive depleted" coupling is preserved by construction.
- **Decomp:** consume berries with `Inventory.RemoveItem("Pukeberries", n)` (`:56922`) /
  `CountItems("Pukeberries")` (`:56985`) on the player inventory. **No patch** — we read/mutate the
  inventory directly in our teleport code.

### 5.5 The thematic cost — arrive *Feeling Sick* (`SE_Puke`), applied directly (no patch)

When a jump burns Bukeberries, the player **arrives food-empty AND afflicted with vanilla *Feeling
Sick*** — the same `SE_Puke` the berry already carries on consumption (15 s of −100 % health regen,
−100 % stamina regen, −50 % move speed, 1 dmg / 2 s). Pushed past your provisions → land **wrecked**.

- **Decomp:** apply it in our teleport code with `player.GetSEMan().AddStatusEffect(SE_Puke.NameHash(),
  resetTime: true)` (the `SEMan.AddStatusEffect` overloads are `:24339` / `:24381`; `GetSEMan()` is
  `:10644`). `SE_Puke` is the class at `:25312`; while active it calls `Player.RemoveOneFood()`
  (`:17452`) every `m_removeInterval = 1f` second (`:25315`, driver at `:25331`) — i.e. the effect
  itself keeps the belly empty for its duration, reinforcing "arrive depleted." We do **not** patch
  `RemoveOneFood` (the old key model did, to charge the key faster — that hook is **deleted**); we
  just apply the standard effect and let vanilla run it.
- **Design tension, accepted (design doc §5.1):** the debuff lands exactly when a player is most
  likely to berry-jump (a panicked "get home NOW" escape) — arriving with −50 % move / no regen is
  genuinely punishing there. That is the **intended** weight: berry travel is a desperation lever,
  not a convenience one. (The forgiving alternative — food-empty, no debuff — was considered and
  rejected; it made berries too clean a substitute for cooking.)
- **Tuning knob:** the arrival debuff can be shortened/scaled vs the full 15 s vanilla `SE_Puke` if
  playtest finds it too lethal on escape jumps (design doc §6.6) — expose as config; baseline = full
  vanilla `SE_Puke`.

### 5.6 Feasts — separate normalized RANGE clock (slightly under personal meals)

Every vanilla feast is **50 m duration, flat** (encodes *convenience*, not earned power). Feeding raw
50 m into `minutes × tier` would hand feasts a fuel tank 67 % over the 30 m personal ceiling and
flatten progression. **Fix (design doc §4): two clocks that diverge only for feasts:**
- **Buff clock: 50 m, untouched.** Feasts stay excellent combat/sustain food.
- **Range clock: normalized to `FEAST_RANGE_CAP` (~28 m baseline)** — the value fed into the PE
  formula for a feast slot, regardless of its real `m_time`. Feast *range* then progresses through
  the stat-derived tier ladder, not the flat duration. The depletion-coupling (§5.3) still applies.
- **Result:** the best feast (Ashlands gourmet bowl, PE 140) stays **under** the best personal food
  (Marinated greens, PE 150) — the travel-optimal pick is always a personal meal.

> **Feast discriminator (grounded this pass):** vanilla feasts share the **`Feast*` prefab-name
> family** — `vprefab list` shows `FeastMeadows, FeastBlackforest, FeastSwamps, FeastMountains,
> FeastPlains, FeastMistlands, FeastAshlands, FeastOceans` (the 8 biome feasts). C2 detects a feast
> slot by matching `food.m_item.m_dropPrefab.name` against this `Feast`-prefixed set (or the
> equivalent shared-name check) and substitutes `FEAST_RANGE_CAP` for `m_time/60` in the PE term for
> that slot. `FEAST_RANGE_CAP` is a config knob (baseline 28 m; design doc §6.2).

### 5.7 Lore breadcrumb — the ONLY advertisement (localization, not a mechanic)

The Bukeberry-reserve feature is a **whispered feature**: no tutorial, no tooltip math, no UI
readout. The *only* in-game signpost is a **lore breadcrumb in item description text** — the Twisted
Portal (and/or the Pukeberry) hover/description hints that *Greydwarves are known to use these
berries in their own portal-magicks, though nobody really understands how it works.* It is a
**localization-string edit** (`$sbpr_*` keys), kept evocative and **non-explicit** (no numbers, no
"10 berries = 300 m"). Players discover the mechanic by experiment and rumor — that's what makes it
feel arcane (design doc §5.2). This is flavor text owned by C2, not a mechanic.

### 5.8 Open tuning knobs (architecture locked, numbers live) — all BepInEx config

Per the design doc §6, these are dials for playtest, **not** architecture reopenings — expose each as
BepInEx config (the Sunstone Lens precedent): `METERS_PER_PE` (§5.3, the one under-specified
constant), `BUKE_METERS_PER_BERRY` (30), `FEAST_RANGE_CAP` (28), the `/30` tier divisor + `[1,5]`
clamp, the eitr weighting, the `SE_Puke` arrival-debuff scale, and shaman drop rate (vanilla). Encode
the locked baselines as a **boot-time runtime assertion** (the SpecCheck/watchdog pattern) so a
silently-drifted config default screams on boot (AT-PE-MATH covers the pure-math core).

---

## 6. Rune-name storage (`sbpr_rune_name` ZDO slot — card AC, UNAFFECTED by the cost-model change)

> 🔄 Unchanged by food-as-fuel. Carried forward intact.

- **A dedicated ZDO string slot, SEPARATE from `s_tag`** — `m_zdo.Set("sbpr_rune_name", name)` /
  `GetString("sbpr_rune_name")`. The card names this slot explicitly. Keeping it off `s_tag` is what
  prevents vanilla's tag machinery (and vanilla portals) from ever connecting a Twisted Portal by tag
  collision (§4.3). **LOCK the key string `sbpr_rune_name`** — a rename orphans every named portal's
  ZDO (the `SBPR_PortalPlantTime` lock lesson).
- **Owner-write discipline** (the `CairnTag`/`AncientPortalTag` precedent): writes guard on a live
  ZDO (ghost = no-op), claim ownership before writing (`if (!nview.IsOwner()) nview.ClaimOwnership()`),
  then `Set`. Reads are free. The `SBPR_TwistedPortal` class owns this — fold the ZDO discipline in
  rather than a separate `…Tag` unless the class grows unwieldy (§3 noted both options).
- **Naming UX** = the vanilla `TextInput` dialog (Q3a): `Interact(human, hold, alt)` →
  `TextInput.instance.RequestText(this, "$sbpr_twisted_rune", 15)` (the portal's `[Use]` opens the
  rename box; `TextReceiver.SetText` writes the slot). 15-char cap (vanilla portal tags are 10; a
  rune name can be a touch longer — flag the exact cap, default 15).
- **Hover text** shows the rune name + paired destination: `GetHoverText()` returns the localized rune
  name + "[E] Set rune" + (if a paired destination exists) "→ <dest rune>". `GetHoverName()` returns
  "Twisted Portal". *(No charge readout — there is no key/charge to display. If anything, the hover
  could carry the §5.7 lore breadcrumb; keep it non-explicit.)*
- **CensorShittyWords / UGC filter:** vanilla portal tags run through `CensorShittyWords.FilterUGC`
  (`:123045`). Mirror it for rune names so a server's profanity policy applies (the Surveyor's Table
  naming did this) — flag as a nicety, not a blocker.

---

## 7. The on-step proximity overlay (card AC: "lists nearby names within 300m, visible through terrain")

> 🔄 Unaffected by the cost-model change in substance. **One reconciliation correction:** the child
> UI card (**C3, t_e732bd8b**) commissions the **through-terrain world-space** overlay (the design
> doc's literal "visible through terrain" reading) as the v3.0 deliverable — NOT the HUD-list route
> the earlier draft recommended as the safe default. This section is re-pointed to match the card the
> engineer-ui profile actually builds. Under the locked Model A (Q3), the overlay is **informational,
> not interactive** — it is not a destination picker.

### 7.1 What it shows
When the local player stands on (or near) a Twisted Portal, an overlay lists nearby Twisted Portals
by rune name, **rendered through terrain** so labels are visible behind hills.

- **Route B — world-space through-terrain labels (the v3.0 deliverable, C3 — BUILT).** A world-space
  `Canvas` per nearby portal with a `UI.Text` rune label (+ optional distance) rendered with a
  **ZTest Always** material so it shows *through* terrain (the `nomap.md` §7 "ZTest-Always shader,
  prefab-only" note). The most bespoke UI in the mod — which is why C3 is **intentionally LAST** in
  the build chain and is a **visual eyeball accept** that's Daniel's (build-green is the floor, "does
  it read right through terrain" is the in-game gate). Reference `BugattiBoys/PortalIndicator` for the
  *pattern* only (clean-room: read for API shape, copy zero gameplay code — ADR-0001).
  **Shipped files:** `Features/Portals/TwistedPortalOverlay.cs` (the render — a `Hud.Awake`-postfix
  pump mounted under `Hud.m_rootObject` that toggles a WORLD-SPACE billboarded label field, the
  `SunstoneLensHudOverlay`/`SunstoneWorldRing` #209 pump-stays-alive pattern; the ZTest-Always
  material is a non-destructive clone of the default UI material with `unity_GUIZTestMode = Always`)
  + `Features/Portals/TwistedPortalOverlayModel.cs` (the engine-free nearest-N / radius / unnamed-skip
  / distance-format policy, CI-gated in `tests/TwistedPortalOverlayModelTests.cs` — the
  `SunstoneHaloGeometry` link-compile precedent). Through-terrain is a **live BepInEx toggle**
  (`TwistedPortalOverlay.ThroughTerrain`) so Daniel A/B's see-through-hills vs honest-depth in-world.
- **Route A — HUD overlay (the fallback, NOT the commissioned route).** A list under
  `Hud.instance.m_rootObject` showing rune name + distance + bearing — NoMap-safe, client-only, the
  Sunstone Lens precedent. Documented here as the lower-risk alternative **if** the through-terrain
  labels prove too costly in playtest, but C3's scope is Route B. If the engineer-ui worker finds
  Route B unviable, they fall back to Route A and flag it — they do not silently swap.

### 7.2 Data source + the 300m multiplayer caveat (from §2)
- The overlay queries nearby Twisted Portals via `ZDOMan.GetAllZDOsWithPrefabIterative` (`:65497`) for
  our prefab hash, filtered to within `OverlayRadius` (300 m) of the player, reading each one's
  `sbpr_rune_name`.
- 🔴 **Per §2, on a dedicated server the client only holds portal ZDOs within ~64–128 m** — so the
  overlay shows the portals the client currently has, which may be fewer than the true 300 m set on a
  far-flung world. **Under Model A this is an acceptable cosmetic shortfall** (travel itself resolves
  server-side and is correct; only the *list display* is range-limited). **AT-OVERLAY explicitly tests
  + documents this** so it's a known, accepted limitation, not a bug. A *guaranteed* 300 m list on
  dedicated servers is the custom-RPC directory sync (§2 option 2 / Model B) — out of v3.0 scope.

### 7.3 Trigger
A proximity check (player within ~3 m of a Twisted Portal, the `Player.GetClosestPlayer` /
`m_activationRange` pattern vanilla portals use — `m_activationRange = 5f` `:122904`, used at
`:122980`) toggles the overlay visible, refreshed on a ~0.5 s throttle from the overlay's `Update`
(costs nothing on the server, which has no Hud — the Sunstone Lens cadence).

---

## 8. Registration + wiring order (Registrar, PatchCheck, server-gating) — the patch surface COLLAPSES

> 🔄 **The biggest structural win of food-as-fuel: the cost model is patch-free.** The old key model
> needed **three Harmony patches** (`Player.EatFood` postfix, `Player.RemoveOneFood` postfix, the
> `DrainEquipedItemDurability` prefix cloned from the Sunstone Lens). Food-as-fuel needs **zero** —
> PE is read on demand from `GetFoods()` and the debit/berry/`SE_Puke` all run inline in our own
> teleport method (§5.1). The feature is now **entirely component-wiring + one piece registration**,
> with no game-method patches at all.

New files in the existing `Features/Portals/` slice:
- **`TwistedPortal.RegisterPrefabs(zns)`** — build + register the portal piece (additive shell +
  grafted kitbash + `SBPR_TwistedPortal` MonoBehaviour); register our prefab hash into
  `Game.PortalPrefabHash` per §4.3 (idempotent, the Ancient Portal helper).
- **`TwistedPortal.DoObjectDBWiring(zns)`** — add the portal piece recipe (Q1 = coexist shape, §0),
  rebuild the portal piece cost, add the portal to the Hammer PieceTable (`AddOrReplacePieceByName`).
  **No item to register** (the key is gone).
- **`TwistedPortalEnergy`** (the §5 cost engine) — a plain helper class, **no registration, no
  patches**. Its pure-math core (tier, PE sum, berry-shortfall) is link-compiled into the test
  project as an engine-free truth table (the `SunstoneHaloGeometry`/`CompassNorthGate` precedent) so
  CI gates the math (AT-PE-MATH). The runtime call site is `SBPR_TwistedPortal.Teleport` (§4.4).
- **`TwistedPortalOverlay`** (§7) — client-only overlay. Its ONE Harmony patch is the **`Hud.Awake`
  postfix** (`TwistedPortalOverlay.HudBootstrap`) that mounts the overlay pump under
  `Hud.m_rootObject` — the Sunstone Lens / Iron Compass HUD-bootstrap doctrine. Past the mount it is
  patch-free (reads ZDOs + draws world-space Canvas labels). Registered in `Plugin.Awake` and
  PatchCheck-asserted (see below).
- **Wire into `Runtime/Registrar.cs`** — add the `RegisterPrefabs` call **after `Portals` and
  `SunstoneLens`** (Twisted reuses Portal helpers and consumes Sunstone), and `DoObjectDBWiring`
  **after `Trailhead`** (Explorer's Bench must exist if any recipe needs a station — though the
  portal piece is Hammer-placed/no-station) — mirror the Ancient Portal ordering.
- **`SpecCheck.cs`** — add the **one** new row (§0, the portal piece); extend the `LOCKED SOURCE`
  comment to cite this doc. (Sunstone's row already exists from the Lens — do not duplicate. **Do not
  add a key row** — there is no key.)
- **`Plugin.Awake` patch registration: ONE patch for this feature (C3's overlay mount).** The
  portal/teleport/trigger/ZDO/PE/berry/`SE_Puke` machinery (C1+C2) is **patch-free by construction**
  — the cost model contributes no patches (contrast the old draft, which had to register three). C3's
  overlay adds exactly one: the **`Hud.Awake` postfix** (`TwistedPortalOverlay.HudBootstrap`) that
  mounts the overlay pump under `Hud.m_rootObject` — registered via
  `harmony.PatchAll(typeof(TwistedPortalOverlay.HudBootstrap))` and PatchCheck-asserted (or it ships
  dead, the t_564f695a lesson). This is the same `Hud.Awake`-mount idiom the Sunstone Lens HUD + Iron
  Compass overlays use; it never fires on the dedicated server (no Hud).
- **Server-gating:** every registration is gated `if (!ServerContext.OnSBServer) return;` via the
  Registrar fan-out (SBPR doctrine). The overlay is client-relevant; the registration is server+client.

---

## 9. Decomposition — the impl cards (ALREADY CREATED; they auto-promote when this reconciliation merges)

> 🔄 **Reconciliation reality:** unlike the old draft (which said "the architect creates C1–C3 once
> Daniel unblocks"), the child cards **already exist** — they were created with **this card
> (t_c15411b2) as the gating parent** and **auto-promote from `todo`→`ready` the moment this
> reconciliation lands**. This architect card does **NOT** create them and does **NOT** implement
> anything; it reconciles the spec they build against. The fan-out as it actually exists on the board:

| # | Card (real id) | Assignee | Depends on | Scope | Primary ATs |
|---|---|---|---|---|---|
| C1 | **t_2b388cd5** — Twisted Portal piece: distinct class, NoPortals bypass, rune-name pairing | `engineer-systems` | this card (t_c15411b2) | §3, §4, §6. The portal class, hash registration + the AT-NO-VANILLA-PAIR `s_tag` decision, our `Teleport` (NoPortals omitted) **exposing the teleport entry point + distance `D` as the seam for C2**, `ResolveDestination` name-match (Model A), the rune `TextInput` rename, the overhead trigger (reuse Ancient). Recipe row 1. | AT-PORTAL-PLACE, AT-NOPORTALS-BYPASS, AT-RUNE-NAME, AT-NAME-PAIR, AT-NO-VANILLA-PAIR, AT-JUMP-ACTIVATE, AT-NO-KEY |
| C2 | **t_6e992a30** — Food-as-fuel Portal Energy + Bukeperry reserve + Feeling Sick | `engineer-systems` | **C1** (the teleport seam must exist to debit) | §5. The PE read off `GetFoods()`, the tier curve, the distance→food-time debit, the feast range-clock, the Bukeberry shortfall solve, the `SE_Puke` arrival apply, the lore breadcrumb. **Patch-free.** Hooks C1's teleport seam (`D` in, `{ok,reason,burnedBerries}` out). | AT-PE-DEBIT, AT-ARRIVE-DEPLETED, AT-BUKE-RESERVE, AT-BUKE-SICK, AT-FEAST-CLOCK, AT-PE-MATH |
| C3 | **t_e732bd8b** — Through-terrain portal-name overlay (informational, Model A) | `engineer-ui` | **C1** (portals must exist + be named) | §7. The world-space ZTest-Always rune labels, the proximity trigger, the 300 m client-window caveat documented. **Last in the chain; visual eyeball accept is Daniel's.** | AT-OVERLAY, AT-OVERLAY-THROUGHTERRAIN, AT-NOMAP-SAFE |

- **Build order:** C1 → C2 → C3 (the board edges already encode `parents`: C2 and C3 both depend on
  C1). Each is its own Daniel-gated PR (incremental delivery doctrine).
- **This (architect) card does NOT implement.** Its deliverable is the reconciled spec + the doc
  manifest updates; on merge, the board promotes C1, and C2/C3 follow as C1 completes.

---

## 10. Named acceptance tests (the single source of truth for "done")

Observable criteria. **logs-green ≠ playable** — every AT closes only on Daniel placing/using one
in-game on a joined client (repo honesty rule). The engineer reports per-AT status in each PR handoff;
the build PRs do NOT self-close these.

> 🔄 **The six key-charge ATs are DROPPED** (AT-KEY-CRAFT, AT-KEY-CHARGE-FOOD, AT-KEY-PUKE,
> AT-KEY-SPLIT, AT-KEY-SPEND, AT-KEY-ZERO-INERT) — there is no key. They are replaced by the
> food-as-fuel ATs below.

**Portal mechanism (C1):**
- **AT-PORTAL-PLACE** — `piece_sbpr_twisted_portal` is placeable with the **Hammer**, **no station in
  range**, solid-earth only (rejected on structures), costs the recipe-1 materials. Visually distinct
  from the Ancient Portal (swamp tint).
- **AT-NOPORTALS-BYPASS** (🔴 the headline, card AC#2) — with the `NoPortals` global key SET, a Twisted
  Portal **still teleports**. A vanilla portal next to it does NOT (proves we bypass the gate that
  still binds vanilla).
- **AT-RUNE-NAME** (card AC) — `[Use]` opens the rename dialog; the typed rune name persists across
  relog (stored in `sbpr_rune_name`, NOT `s_tag`). Hover shows the rune name.
- **AT-NAME-PAIR** (card AC#2, Model A) — two Twisted Portals with the **same rune name** teleport to
  each other; two with **different** names do not. Works with `NoPortals` set.
- **AT-NO-VANILLA-PAIR** (🔴 §4.3 risk) — two UNNAMED Twisted Portals are NOT auto-connected by vanilla
  `ConnectPortals` into a spurious `ConnectionType.Portal` link; a Twisted Portal never pairs with a
  vanilla `portal_wood`. Verified in-game (the `s_tag`-empty / hash-registration interaction).
- **AT-JUMP-ACTIVATE** (🔴 geometry, reuse Ancient tuning) — **jumping up** into the ring teleports;
  **walking underneath** does not. Trigger box tuned on a joined client.
- **AT-NO-KEY** (🔄 NEW, the reconciliation proof) — **no trinket item exists.** There is no
  `SBPR_TwistedKey` in the ObjectDB, no craftable key recipe, no Trinket to equip; SpecCheck has **no
  key row**. Travel is gated purely by belly food. (The negative AT that proves the supersession
  shipped.)

**Cost model — food-as-fuel (C2):**
- **AT-PE-DEBIT** (🔄 NEW, card AC) — a jump **drains belly food-time proportional to distance**: after
  a teleport, the player's active food slots show **less remaining time** than before, scaled by the
  jump distance; Max HP/Stamina/Eitr visibly drop. A longer jump drains more.
- **AT-ARRIVE-DEPLETED** (🔄 NEW, card AC) — a **long** jump lands the player **food-low** (slots near
  empty, buffs bottoming out); a short hop barely dents the belly. The distance↔depletion coupling is
  observable, not bolted on.
- **AT-BUKE-RESERVE** (🔄 NEW, card AC) — Bukeberries (`Pukeberries`) are burned **only on shortfall**:
  a jump the belly can cover spends **zero** berries; a jump past belly range burns
  `ceil(shortfall/30m)` berries; **10 berries = a from-empty 300 m max jump.** With no berries and an
  empty belly, the jump is blocked (not a crash).
- **AT-BUKE-SICK** (🔄 NEW, card AC) — a **berry-burning** jump applies the vanilla **Feeling Sick**
  (`SE_Puke`) effect on arrival (−50 % move / no regen) AND lands the player food-empty. A
  belly-covered jump (no berries) does NOT apply it.
- **AT-FEAST-CLOCK** (🔄 NEW) — a **feast** (a `Feast*` food) contributes travel range on the
  normalized ~28 m `FEAST_RANGE_CAP` clock, NOT its real 50 m buff timer; its 50 m buff duration is
  unchanged. The best feast out-ranges *no* personal meal (always slightly under).
- **AT-PE-MATH** (🔄 NEW, CI-gated) — the engine-free PE core (tier = `round(clamp(stats/30,1,5)×2)/2`,
  `PE = Σ minutes×tier`, berry-shortfall = `ceil((D−belly_range)/30)`) matches the design doc's
  worked numbers in a link-compiled unit table (the `SunstoneHaloGeometry` precedent). The one AT that
  closes headless.

**Overlay (C3) + cross-cutting:**
- **AT-OVERLAY** (card AC, Model A) — standing on a Twisted Portal shows the nearby Twisted Portal rune
  names. **Documented caveat:** on a dedicated server the list is limited to the client's held ZDO
  window (~64–128 m), not a guaranteed 300 m (§2/§7.2) — an accepted v3.0 limitation, verified +
  noted, not a bug. Informational only (not a picker).
- **AT-OVERLAY-THROUGHTERRAIN** (C3, the visual gate) — the rune labels render **through terrain**
  (ZTest Always), readable behind hills. Daniel's in-game eyeball accept.
- **AT-NOMAP-SAFE** — the overlay renders with the SB server's default NoMap (no minimap) — it does not
  depend on the map being on (the Sunstone Lens / Iron Compass HUD doctrine).
- **AT-VANILLA-ONLY** (card AC) — no third-party portal-mod code read or copied; all hooks are
  base-game primitives (the named-directory + food-as-fuel mechanics are net-new SBPR fiction).
- *(Model B, only if Daniel ever commissions it)* **AT-DIRECTORY-PICKER** — a selectable 300 m
  directory teleports to the chosen portal, correct on a dedicated server (requires the RPC sync).

---

## 11. Cross-doc updates (spec-first — move in the SAME PR as this spec) + decision log

### Cross-doc updates this spec PR carries (the docs half of spec-first)
- **`docs/v3/planning/index.md`** + **`docs/v3/planning/README.md`** — **rewrite the Twisted Portal
  rows** from the stale "food-charged TRINKET key (durability-as-charge)" blurb to **food-as-fuel
  (no key)**; flip the status framing from "BLOCKED on three Daniel decisions" to "cost model
  design-locked (food-as-fuel, PR #270/#271); Q1=coexist, Q3=Model A locked; reconciled card
  t_c15411b2." SpecCheck note: **+1 row** (was +2/+3; the key row is removed). *(Done in this PR.)*
- **`docs/datasets/PIECES_AND_CRAFTABLES.md:442`** — **rewrite the Twisted Portal line:** drop the
  `SBPR_TwistedKey` (Trinket charge-meter) half entirely; describe the single `piece_sbpr_twisted_portal`
  prefab + the food-as-fuel travel-cost model (belly Portal Energy, Bukeberry reserve). *(Done in this
  PR.)*
- **`docs/design/nomap.md` §7** — **NO further edit needed.** The food-as-fuel supersession banner is
  **already in place** (lines 135–159, landed by the merged design PR #270/#271), and the two factual
  drifts (`GetAllZDOsWithPrefab`→`…Iterative`, bukeberries→`Pukeberries`) were **already corrected**
  there by the prior spec-pass. This reconciliation does not re-touch nomap. *(Verified, no-op.)*

> **Folder-existence note (resolved):** the old draft flagged that `docs/v3/planning/` "does not yet
> exist on the v1 integration branch." That is **stale** — the folder exists on `main` (this spec, the
> Sunstone Lens spec, the Iron Compass spec, etc. all live there). The scaffolding concern is closed;
> this PR is cut from `main`.

### Decision log (reconciled state — what's locked vs. what an engineer may still resolve in-game)

**Locked by the design authority (Daniel, merged PRs #270/#271 — not reopenable without him):**
- **Cost model = food-as-fuel.** No key trinket. `PE = remaining_minutes × tier`, distance debits
  food-time, Bukeberries are the shortfall reserve, berry-jumps arrive `SE_Puke`. (Resolves the old
  Q2.) Numbers are tuning knobs; the architecture is fixed.

**Locked by the architect this pass (grounded in precedent + the locked design — the gate is retired):**
- **Q1 = COEXIST** — vanilla/Ancient portals stay craftable; §4.7 empty (the child portal card builds
  this).
- **Q3 = Model A** — name-tag pairing on `sbpr_rune_name`, server-side resolution, informational 300 m
  overlay; Model B (picker + RPC sync) explicitly out of v3.0 scope.
- Distinct class `SBPR_TwistedPortal`, NOT inheriting `TeleportWorld` (card AC#1; tag-collision).
- Reimplement teleport via `Player.TeleportTo` (`:20771`), omitting the `NoPortals` check (`:123008`,
  card AC#2); debit belly PE (§5) at the teleport seam.
- Additive construction reusing the Ancient Portal shell + kitbash (ADR-0006).
- Hammer-placed, no station, solid-earth-only (the Ancient Portal's locked placement choices).
- Rune name in the dedicated `sbpr_rune_name` ZDO slot, off `s_tag` (card AC); naming via the vanilla
  `TextInput` dialog through `TextReceiver` (Q3a).
- **The cost model is patch-free** — PE read on demand from `Player.GetFoods()` (`:17598`); no
  `EatFood`/`RemoveOneFood`/`DrainEquipedItemDurability` patches (those were the deleted key model).

**Engineer-resolvable IN-GAME (noted per-section; not gates, not Daniel decisions):**
- `METERS_PER_PE` baseline (§5.3) — derive from the design doc's anchors + expose as config; **BLOCK
  for Daniel only if no defensible baseline falls out of the locked numbers.**
- NoBossPortals keep/lift (§4.4, default KEEP); ore-ban keep/lift (§4.4, default KEEP).
- AT-NO-VANILLA-PAIR `s_tag` approach (a) sentinel vs (b) own-walk (§4.3, architect lean = b).
- Feast `FEAST_RANGE_CAP`, `BUKE_METERS_PER_BERRY`, tier `/30`+clamp, `SE_Puke` debuff scale — all
  config knobs, baselines from the design doc §6.
- The overlay route (§7): C3 builds Route B (through-terrain); falls back to Route A only if Route B
  proves unviable, with a flag.

**No remaining 🔴 BLOCK on this card.** The reconciliation is a docs-only PR; on Daniel's merge, the
board promotes C1 (t_2b388cd5), and C2/C3 follow as C1 lands. **No impl code is written by this card.**
