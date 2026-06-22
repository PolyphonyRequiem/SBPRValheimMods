---
title: "Local Map provider binding persists across relog — buildable implementation spec"
status: proposed
purpose: "Build-ready architect spec for fixing the session-scoped provider binding: the SBPR minimap disc must survive a logout/login as long as the imprinted Local Map remains carried (AT-MAP-DURABLE), instead of vanishing until the player next equips a map. Reverses the one carve-out (map-provider-binding-impl-spec.md §3.1) that made the provider session-scoped, realigning it with the LOCKED requirement AT-MAP-DURABLE and with design map-provider-model.md §3.2 (both of which never sanctioned a relog exception). Specifies the fix as a one-shot cold-start carry re-derivation latch in LocalMapController (NO new persisted key, NO LocalMap.cs change, SpecCheck +0), names the rejected persist-identity alternatives, and carries the spec-reversal routing (the §3.1 + design §3.1/§3.2 content flip rides the engineer's CODE PR, same-PR-as-code per spec-first). Grounded against main @ e904b16 + assembly_valheim decomp. Authored by the architect for BUG card t_5fc02f00. Daniel gates the doc-review + the in-game ATs; an engineer-ui child builds it."
owner: Daniel (design authority); architect (spec capture + grounding)
supersedes_partial:
  - "map-provider-binding-impl-spec.md §3.1 (the 'client-only, session-scoped … null on relog' carve-out) — REVERSED to persist-while-carried; content flip lands with the code PR (§5)"
  - "design/map-provider-model.md §3.1 / §3.2 — the binding's relog behavior is now explicitly persist-while-carried (the design was silent on relog; this closes that gap)"
---

# Local Map provider binding persists across relog — buildable implementation spec

The SBPR minimap disc disappears after a logout/login even though the player still
carries the imprinted Local Map. It only returns when the player next *equips* a map.
This contradicts the **locked** requirement **AT-MAP-DURABLE**
([`requirements.md`](requirements.md) §6, `:281–282`): *"binding persists while the item
sits in inventory; reverts to no-map the instant it leaves inventory."* A relog does **not**
remove the item, so the binding must survive it.

This doc is the buildable *how*: the root cause (grounded), the chosen mechanism (a one-shot
cold-start carry re-derivation **latch** — no persisted key), the rejected persist-identity
alternatives, the named acceptance tests, and the spec-reversal routing. An `engineer-ui`
implementer should build the whole fix from this section without re-deriving anything.

> **Clean-side note (ADR-0001):** every vanilla decomp line cited here is the base game
> (`assembly_valheim`), **fair game to read + adapt** (repo AGENTS.md + the 2026-06-09
> clarification). Line numbers were grepped live against
> `~/valheim/worldgen-spike/decomp/assembly_valheim.decompiled.cs` — re-confirm against the
> build assembly if the dump drifts. No other mod's code is read or copied.

## 0. SpecCheck manifest impact (read first)

**None.** This is presentation/provider-state behaviour on an existing prefab
(`SBPR_LocalMap`). No recipe, no build piece, no icon. `Runtime/SpecCheck.cs` is **untouched**
(+0 rows). Spec-first still applies: the **code** PR carries the `LocalMapController.cs` change
**together with** the §3.1 + design §3.1/§3.2 reversal (§5) — that is the same-PR contract, not
this spec-pass PR.

---

## 1. What this reverses — drift-correction, NOT a fresh override (read carefully)

This is **not** Daniel overriding a locked behavior. It is realigning a buildable impl-spec
back to the **locked requirement it was supposed to implement.** Three docs, one of which drifted:

| Doc | Section | What it says about relog | Status |
|---|---|---|---|
| [`requirements.md`](requirements.md) **AT-MAP-DURABLE** | §6 `:281–282` (also §2 `:166–169`) | "binding persists while the item sits in inventory; reverts to no-map the instant it leaves inventory" | **LOCKED — governs.** A relog does not remove the item ⇒ binding must survive. |
| [`../../design/map-provider-model.md`](../../design/map-provider-model.md) **§3.2** | `:217–231` | provider stays until (a) another map is equipped OR (b) the map leaves inventory | **SILENT on relog.** Neither (a) nor (b) is a relog ⇒ never sanctioned dropping it. |
| [`map-provider-binding-impl-spec.md`](map-provider-binding-impl-spec.md) **§3.1** | `:144–148` | "client-only, session-scoped … does not survive a relog … null until the player next equips a map" | **DRIFTED.** This affirmative carve-out contradicts AT-MAP-DURABLE. **This is the line being reversed.** |

So the fix **removes a contradiction** the impl-spec introduced, restoring the locked
requirement. Daniel's 2026-06-22 in-game report (*"the state of the last equipped/still in
inventory local map is not persisted between sessions … the minimap disappears after logging
out/in"*) is the trigger that surfaced the drift, not a request to change locked behavior.

**Per AGENTS.md (spec and code change together; the spec wins where it speaks):** the §3.1
carve-out is the bug at the spec layer, the session-scoped `_provider` is the bug at the code
layer, and **both flip in the same PR** (§5).

---

## 2. Root cause (grounded — file + line, main @ e904b16)

The disc render is hard-gated on an **in-memory `_provider` reference** that is established
**only on an equip-edge transition**, with **no load-time re-derivation hook**:

- [`LocalMapController.cs:56`](../../../src/SBPR.Trailborne/Features/Cartography/LocalMapController.cs)
  — `private ItemDrop.ItemData? _provider;` (the binding). Disc gated on `_provider != null` at
  `:147–150` (`shouldBindDisc`).
- `:124–128` — the **only** establishing write:
  `if (mapEquipped && !ReferenceEquals(equippedMap, _provider)) _provider = equippedMap;` —
  binds on the **equip edge only**. The other two writes are *clears*: `:137` (left inventory,
  §3.4) and `:350` (`ClearBinding` teardown).
- `:65` — `Awake()` restores nothing. `LocalMapBootstrapPatch.cs:33–38` — the `Minimap.Start`
  postfix attaches a **fresh** controller each session; nothing re-binds from carry-state.
- `:54–55` — the code self-documents the defect: *"Client-only, session-scoped (null on relog
  until the player next equips a map — §3.1)."*
- `:366–370` — tombstone: the `GetCarriedLocalMap` "find any carried map" probe was **explicitly
  retired** and never replaced with a load hook. `:372–377` — the `IsLocalMap` probe survives and
  is reusable for re-derivation (`m_dropPrefab` is repopulated by vanilla on load — §3.4, decomp
  `ItemDrop.Awake :58698`).

**Persistence model as-is = in-memory only, lost on logout.** The *survey blob* IS persisted in
the item's `m_customData` ([`LocalMap.cs:228–231`](../../../src/SBPR.Trailborne/Features/Cartography/LocalMap.cs)) —
but that is the map's **data**, not the "this map is the active provider" **binding**. The survey
survives; the binding does not.

### 2.1 Why "still in inventory" is the precise failure mode (decomp-grounded)

Vanilla restores **equipped** state on load, so the two carry states diverge:

- **Equipped at logout → self-heals.** `Player.Load` calls `EquipInventoryItems()`
  (`assembly_valheim Player.Load :19915` → `:19919–19926`), which iterates
  `m_inventory.GetEquippedItems()` and `EquipItem(...)`s each. The `equipped` bool round-trips
  through save/load (`Inventory.Save :57328` writes it; `Inventory.Load :57357` reads it →
  `AddItem :57372`). So a map equipped at logout is re-equipped on load → the next 0.25 s poll's
  §3.2 bind re-derives `_provider`. **This case already mostly works.**
- **Carried-but-unequipped at logout → provably CANNOT heal.** Its `m_equipped` round-trips
  `false`, nothing re-equips it, and there is no carried-map re-derivation. This is Daniel's
  "still in inventory" case — the durable-carry **HOLD** state (§3.3 of the binding spec) — and
  it is the actual bug.

The fix targets the carried-unequipped case specifically, and incidentally hardens the
equipped case (§3.2 binds it on turn 1 rather than waiting for vanilla's re-equip + a poll).

---

## 3. The mechanism — 🟢 DECIDED: a one-shot cold-start carry re-derivation latch

The whole fix lives in `LocalMapController.cs`. It adds a **single** re-derivation that runs
**exactly once per controller lifetime** (= once per session), at the first poll where a local
player exists, *before* the existing equip-edge state machine takes over. After it fires, the
controller behaves exactly as today — so the §3.4 unbind semantics (including "re-pickup after
an in-session drop does NOT re-bind") are preserved untouched.

### 3.1 The latch — field + lifetime

Add one bool:

```csharp
// One-shot: have we run the cold-start carry re-derivation yet this controller life?
// The controller is attached fresh on every Minimap.Start (LocalMapBootstrapPatch), and
// Minimap.Start runs once per world-load / relog / character-switch — so the controller's
// lifetime IS the session, and this latch resets to false on every relog by construction.
private bool _coldStartResolved;
```

**Lifetime is the correctness basis.** `LocalMapBootstrapPatch.cs:33–38` adds a new
`LocalMapController` to the live `Minimap` on each `Minimap.Start`. Valheim has no "soft relog"
that keeps the `Minimap` alive across a session boundary — disconnect/reconnect, exit-to-menu,
and join-server all tear down and rebuild the scene, re-running `Minimap.Start` → a fresh
controller → `_coldStartResolved = false`. So the latch is naturally per-session; it needs no
explicit reset.

### 3.2 Cold-start resolution — run once, before the equip-edge machine

At the **top of the throttled poll block** (`LocalMapController.Update`, after the player-null
guard and the `_nextPoll` throttle, *before* the §3.2 bind at `:124`), insert:

```csharp
if (!_coldStartResolved)
{
    _coldStartResolved = true;            // one-shot regardless of outcome
    if (_provider == null)
        _provider = ResolveColdStartProvider(player);   // may stay null (nothing carried)
}
```

`ResolveColdStartProvider` re-derives the most plausible provider from **load-restored
inventory state**:

1. **Equipped local map, if any** (`GetEquippedLocalMap(player)`, `:357` — unchanged). Belt-and-
   suspenders with the vanilla self-heal (§2.1): binds the equipped map on turn 1 instead of
   waiting a poll.
2. **Else the first carried *imprinted* local map** in deterministic inventory-slot order
   (top-left → bottom-right, the order `Inventory.GetAllItems()` yields). Reuse the surviving
   `IsLocalMap` probe (`:372–377`) + `LocalMap.IsImprinted` (`LocalMap.cs:242`) — resurrect a
   scoped `GetCarriedLocalMap`-style scan (the one tombstoned at `:366–370`), now **imprinted-
   filtered**.
3. **Else null** — no carried imprinted map ⇒ no provider ⇒ no disc (correct; there is nothing
   to restore).

> **Why imprinted-filtered (a deliberate cold-start refinement):** mid-session, equipping a
> *blank* map makes it the provider (§3.5 of the binding spec) but renders no disc. At cold-start
> we are RE-DERIVING a binding whose only observable effect is restoring a disc, which requires an
> imprinted map. Binding a blank carried map would leave the disc absent and look like the bug
> isn't fixed; skipping blanks changes nothing observable (a blank provider showed no disc before
> the relog either). Do **not** "fix" this into binding blanks.

`m_customData` is restored on load **before** any of this runs (`Inventory.AddItem :57515`
assigns `m_customData = customData`), so `IsImprinted`/`ReadSurvey` work at cold-start. The
`m_dropPrefab` the probes need is repopulated in `ItemDrop.Awake :58698`.

### 3.3 Why this preserves §3.4 (the load-bearing invariant — do not break it)

The existing **§3.4 unbind** semantics MUST survive, including the explicit rule that *dropping
the provider then re-picking it up without equipping does NOT re-bind* (binding spec §3.4
`:190–194`). The latch guarantees this: the carry-scan runs **once, at cold-start only**. After
`_coldStartResolved` is true, an in-session drop → §3.4 clears `_provider` → re-pickup → the
poll does **not** re-scan (the latch is spent) → no disc until the player re-equips. **The latch
is precisely what distinguishes "carried across the session boundary" (re-bind) from "re-picked-
up mid-session" (stay unbound).** A naive "re-derive whenever `_provider == null`" would re-bind
on in-session re-pickup and violate §3.4 — **do not implement it that way.**

### 3.4 The equipped-at-logout self-heal (already-mostly-works, now turn-1)

As grounded in §2.1, an equipped map self-heals via vanilla `EquipInventoryItems`. Step 1 of
§3.2's resolver binds it immediately at cold-start, so the disc is present on the first poll
rather than after the re-equip lands. Either path satisfies the AT; the resolver just removes
the one-poll gap. No bespoke `EquipItem`/`Player.Load` hook is added (clean-side, no new patch).

### 3.5 The multi-carried-unequipped tie-break — the ONE place this differs from persist-identity

In the **rare** case where the player carries **two+ imprinted maps, all unequipped, across a
relog** (e.g. equip A → unequip → equip B → unequip → logout; both now carried unequipped), the
cold-start scan picks by **deterministic inventory-slot order**, which is **not** guaranteed to be
the most-recently-equipped map (AT-PROV-MOSTRECENT). This is the deliberate, documented tradeoff
of not persisting provider identity (§4):

- It is **deterministic** (same inventory → same pick), never an arbitrary/random map.
- It **self-corrects on the next equip**: the moment the player equips any map, §3.2's edge bind
  restores exact most-recent semantics for the rest of the session.
- Across-relog most-recent in the multi-carried-unequipped case is **not** required by any locked
  AT — AT-PROV-MOSTRECENT's body is entirely *same-session* equips, and AT-MAP-DURABLE only
  requires *a* binding to survive, not a specific tie-break winner.

If Daniel ever wants exact most-recent preserved across relog in this corner case, that is the
reversible upgrade to mechanism (a′) in §4 — but it is not worth its permanent cost (§4) for a
self-correcting cosmetic edge.

---

## 4. Rejected alternatives (and why) — read before "improving" the design

The investigation card recommended **(a) persist exact provider identity via a new
`m_customData` key.** The architect **rejected (a)/(a′) in favour of the §3 latch.** Reasoning,
so this is not silently re-litigated:

- **(a) Persist a per-item "I am the provider" flag (new locked `m_customData` key).** Rejected:
  1. **Permanent wire contract.** A new key alongside `MapBlobKey`/`BoundKey`/`NameKey`
     (`LocalMap.cs:72–74`) is a LOCK-forever save/wire contract (rename ⇒ orphans every imprinted
     map — same rule as the others). That is a heavy, irreversible surface for a cosmetic
     cold-start tie-break.
  2. **Cross-player stale-state leak.** The flag travels **with the item**. Trade/drop-and-other-
     player-grabs a flagged map → on **their** relog, **their** cold-start scan finds it flagged
     and binds it as **their** provider — a provider-identity leak across clients. Preventing it
     needs a clear-before-trade hook (no clean seam) or recipient-side "flagged-but-I-never-
     equipped-it" suppression — i.e. you end up needing session-local state **anyway**, on top of
     the persisted flag.
  3. **It is the repo's recurring bug class.** Stale persisted/latched flags surviving a session
     boundary are exactly what bit AT-LMAP-OPEN-RELIABLE (stale modal-open flag) and the WorldPin
     stale-projection-across-relog reconcile. Adding another persisted latch invites the same
     failure mode.
  4. **To even work, (a) must clear the *previous* provider's flag on every new bind** — an extra
     inventory scan per equip + a per-equip `m_customData` write (grows the save). More invasive
     than the card implies.
- **(a′) Persist a monotonic per-map "last-equipped" stamp; cold-start picks the max.** A more
  correct tie-break than (a)'s boolean (no clear-the-old-flag scan; you only write your own
  stamp). Still rejected for v1: it is **still** a permanent wire key, **still** writes
  `m_customData` on every equip, and **still** leaks across players (a traded-in map carrying a
  high stamp from its prior owner can win the recipient's cold-start). It buys only the rare
  multi-carried-unequipped tie-break — which the §3 latch already handles deterministically and
  which self-corrects on the next equip. **Documented here as the upgrade path** if Daniel ever
  insists on exact across-relog most-recent, but **not recommended.**

**Net:** the §3 latch is *smaller* than the card's recommendation (no `LocalMap.cs` change, no new
key, SpecCheck +0), *more robust* (no persisted flag to go stale or leak across players), and
satisfies AT-MAP-DURABLE exactly. The only thing it gives up is a tie-break the spec never
required and that self-heals on the next equip.

---

## 5. The spec reversal — what flips, where, and in which PR

Spec-first demands the locked-doc content flip and the code land **together** (AGENTS.md). Per
the established two-step (the binding spec itself shipped as a docs-only spec-pass PR #160 *then*
the code PR #162; and `map-provider-binding-impl-spec.md` §8 deliberately deferred the
behaviour-describing banners to the code PR so the spec would not "lie" while the code still only
logged), the split is:

**This spec-pass PR (docs-only) does:**
- Add **this** buildable spec.
- Register it in [`index.md`](index.md) + [`README.md`](README.md).
- Add a **forward-pointer breadcrumb** (a `> ⚠️ PENDING REVERSAL` note, *not* a content flip) on
  `map-provider-binding-impl-spec.md §3.1` and `design/map-provider-model.md §3.1/§3.2`, pointing
  here and stating the content flip rides the code card. (A breadcrumb describes a *plan*, not a
  false present-state, so it is safe in a code-less PR — unlike flipping the content, which would
  assert behaviour the code does not yet have.)

**The engineer-ui CODE PR (the child card) does — in ONE PR with the code:**
- Implement §3 in `LocalMapController.cs`.
- **Flip the content** of:
  - `map-provider-binding-impl-spec.md §3.1` — replace the "client-only, session-scoped … null on
    relog … until the player next equips a map" carve-out with the persist-while-carried rule
    (the provider is re-derived from carried imprinted maps at cold-start; it is *presentation*
    state but its *lifetime is the item's inventory residency*, surviving a relog, per
    AT-MAP-DURABLE). Keep the "client-only, does not replicate, zero server cost" facts — only the
    relog exception is removed.
  - `design/map-provider-model.md §3.1` (storage note) + `§3.2` (binding lifetime) — state
    explicitly that the binding survives a relog while the item is carried (closing the silence,
    not contradicting it).
- Add the cross-ref banner already owed by `map-provider-binding-impl-spec.md §8` if it has not
  landed (the carry path now *renders* a re-derived disc on load), co-located with the code.

This keeps spec and code consistent at every merge: this PR adds a *proposed* spec + breadcrumbs
(no behaviour claim); the code PR makes the locked docs and the code agree simultaneously.

---

## 6. Acceptance tests (named, observable — close only on Daniel's in-game check)

`logs-green ≠ playable` — every render/behaviour AT is a check on a GPU client; build 0/0 is
necessary, not sufficient. The headless build worker cannot verify the disc.

- **AT-PERSIST-CARRY (the bug)** — imprint a Local Map → **unequip** (keep it in inventory),
  confirm the disc shows → **log out → log back in** → the **minimap disc is present** without
  re-equipping, bound to that map's survey. (The carried-unequipped HOLD state survives the
  session boundary.)
- **AT-PERSIST-EQUIP-SELFHEAL** — map **equipped** at logout → relog → disc present within ~one
  0.25 s poll (cold-start binds it turn 1; confirms the equipped self-heal path holds).
- **AT-PERSIST-MULTI (the documented tie-break, §3.5)** — carry **two** imprinted maps, both
  unequipped, across a relog → the disc shows **one deterministic** map (inventory-slot order),
  never blank/none and never random. Note: this is **not** required to be the most-recently-
  equipped one across the relog — equipping any map restores exact most-recent for the session.
- **AT-PERSIST-UNBIND-INTACT (regression guard — must not break §3.4)** — after relog with a
  carried provider: **drop** the bound map → disc reverts to no-map within a poll; **re-pick it
  up without equipping** → **no disc** (re-pickup does not re-bind); **re-equip** → disc returns.
  (Proves the cold-start latch did not turn §3.4 into "re-bind on any carry.")
- **AT-PERSIST-BLANK** — carry only a **blank** (un-imprinted) map across a relog → **no disc, no
  error**; imprint it at a Table → disc appears on the next poll.
- **AT-PERSIST-NOMAPOFF** — on a `nomap=OFF` world, relog with a carried imprinted map → **no SBPR
  disc** (vanilla minimap owns the corner, binding spec §5); the map stays M-openable.
- **AT-PERSIST-SPEC** — `map-provider-binding-impl-spec.md §3.1` + `design/map-provider-model.md
  §3.1/§3.2` state persist-across-relog as the locked behavior, landed in the **same PR** as the
  code (§5).
- logs-green ≠ playable — Daniel confirms AT-PERSIST-* on a GPU client.

---

## 7. Files touched + clean-side

**Code (the engineer-ui CODE PR):**
- **`src/SBPR.Trailborne/Features/Cartography/LocalMapController.cs`** — the entire fix:
  - add `_coldStartResolved` (§3.1);
  - add the one-shot cold-start guard at the top of the throttled poll (§3.2);
  - add `ResolveColdStartProvider(player)` + a scoped, **imprinted-filtered** carried-map scan
    (resurrect the tombstoned `:366–370` probe, reusing `IsLocalMap :372–377` +
    `LocalMap.IsImprinted`).
  - **No** change to the §3.2 equip bind, §3.4 unbind, §4.x disc drive, or §5 nomap gate — they
    run unchanged after the latch.
- **No `LocalMap.cs` change. No new `m_customData` key. No new Harmony patch.** (The only vanilla
  hook in play — the `Minimap.Start` postfix in `LocalMapBootstrapPatch.cs` — already exists.)

**Docs (same code PR — §5):** `map-provider-binding-impl-spec.md §3.1` + `map-provider-model.md
§3.1/§3.2` content flip.

**Docs (this spec-pass PR):** this file + `index.md`/`README.md` registration + the breadcrumb
notes (§5).

**Clean-side (ADR-0001):** everything read is base-game — `Player.GetInventory`,
`Inventory.GetAllItems`/`GetEquippedItems`/`ContainsItem`, `ItemDrop.ItemData.m_customData`/
`m_dropPrefab`/`m_equipped`, `Player.Load`/`EquipInventoryItems`, `Game.m_noMap`. All fair to
read + adapt. No other mod's code touched. **No prefab cloned** (ADR-0006 N/A — no
ZNetView-bearing construction). **SpecCheck +0** (§0).

**Single-player vs dedicated-server:** identical. The defect is client-side per-client
presentation state in both topologies (the controller early-outs on the dedicated server —
`SystemInfo.graphicsDeviceType == Null`, `:80`). No ZDO-vs-local-save divergence.

---

## 8. Build routing

- **One `engineer-ui` worker** owns the whole change (code + the §3.1/design §3.1/§3.2 content
  flip move together — same-PR-as-code per spec-first). File it as a **child of card
  `t_5fc02f00`**.
- **No gating inputs.** R1 (disc centring) from the binding spec is orthogonal and already
  resolved upstream; the mechanism here is fully DECIDED (§3) and the tie-break (§3.5) is an
  architect call, not a Daniel gate. The worker can start immediately on unblock.
- **Build gate:** `dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release` →
  **0 warn / 0 err** (`TreatWarningsAsErrors` on). `docs-lint` green.
- **Daniel gates the merge; the worker NEVER self-merges** — it ends in
  `kanban_block(reason="review-required: …")` with the PR URL and a structured handoff comment.
- **Final accept is Daniel's eye on a GPU client** (AT-PERSIST-* are render/behaviour checks;
  logs-green ≠ playable).
