---
status: current
---

# T026 Field Fletching I — pure joined-client / delivery-layer proof (merged @ 33461d1)

- Owning QA card: `t_e9fffb41` (qa-playtest).
- Implementation PR: [#374](https://github.com/PolyphonyRequiem/SBPRValheimMods/pull/374)
  — **MERGED** to `origin/main`.
  - Reviewed head: `63a5292a40f460bd3c24067ac5fadd14ef86e7b1`.
  - Merge commit (this evidence's base): `33461d187d9d13be67750314c9aeb5bfbc4961f0`,
    verified the tip of `origin/main` (`git rev-parse HEAD == origin/main`).
- QA profile: qa-playtest. Production Niflheim/Heistan **untouched**; no
  user-owned Steam AppId 892970 / GUI `valheim.x86_64` client session was
  running, launched, stopped, or modified (only dedicated `valheim_server.x86_64`
  processes were present, none touched).
- Fresh net48 `SBPR.Niflheim.HomesteadStones` Release DLL md5
  `1423bd89a31a216adbbc0488ff7e0e2c` (built this run, 0w/0e).

## Verdict: PASS (delivery + data layer verified) — GUI last mile REASONED

The T026 review of PR #373 (`t_49e78f41`) correctly refused merge because Field
Fletching I was **host-occupant-only**: no authoritative Personal Character-Effect
server→client delivery channel existed, so a pure joined client always failed
closed and the node-owned craft artifact was PENDING. The remediation (PR #374,
now merged) shipped that channel. This card verifies the exact **merged**
implementation exposes the unchanged vanilla `ArrowWood` recipe station-free to a
pure joined client while Field Fletching I is active, and reverts on
dormancy/disconnect/stale-snapshot — proven at the delivery + data layer, with the
GUI-pixel last mile reasoned exactly as the merged sibling T025 R2 proof
(`t025-practice-range/R2-joined-client-proof.md`) established as the accepted
decision-grade shape under the same safety gate.

## What was VERIFIED (this run, at merged head 33461d1)

### Static gates — all green
- net48 `SBPR.Trailborne` Release: **0 warnings / 0 errors**.
- net48 `SBPR.Niflheim.HomesteadStones` Release: **0 warnings / 0 errors**.
- Full suite `tests/SBPR.Trailborne.Tests.csproj`: **1355 / 1355 passed** (net8
  link-compile = real execution of the engine-free projection + delivery
  substrate the net48 gate consumes).
- T026 subset (`FieldFletching` + `PersonalEffectDelivery`): **27 / 27 passed**.
- Stone-content workbench: **59 / 59 passed** (double-gen byte-identical + check
  clean).
- `scripts/docs-lint.py`: **OK — 202 docs checked**. `docs-freshness.py`: advisory
  pass (no blocking failures).

### Pure-client craft path (the artifact the reviewer found missing) — VERIFIED
`tests/NiflheimPersonalEffectDeliveryTests.cs` drives the **exact** server
authority + wire contract + client cache the net48 gate consumes on a pure joined
client, through the shipped `DerivedActivationView` (purchase record AND active
relationship, per character — no second active-effects ledger):

- **Active exposure** — `ServerSnapshot_ActiveCaller_DeliversFieldFletchingActive`:
  authenticated caller with purchase + active relationship → server stamps a
  snapshot with `IsActive(FieldFletchingI@1) == true`; the client cache
  (`ClientCache_AppliesNewerSnapshot...`) surfaces it via `IsActiveForStone`. This
  is the bit `FieldFletchingRecipeGate.ResolveExposedForLocalOccupant()` reads on a
  pure client (`FieldFletchingRecipeGate.cs:122-135`) to rescue
  `Player.RequiredCraftingStation` for the exact `ArrowWood` recipe to station-free.
- **Recipe identity / unchanged inputs-yield-authority** — the gate rescues ONLY
  the recipe whose output ItemDrop prefab is `ArrowWood`
  (`FieldFletchingRecipeGate.cs:106-112`, matched by prefab name, never a display
  string), and the provider ships `stationFree:true,
  preservesVanillaInputsYieldAuthority:true` (`BushcraftRecipeProvider.cs:82-87`):
  exposure only, no rewrite of inputs, yield, or authority. Vanilla PASS is never
  flipped to fail (`FieldFletchingRecipeGate.cs:89`).
- **Release / dormancy removes exposure** —
  `ServerSnapshot_PurchasedButNoRelationship_DeliversDormant` (relationship
  released → `IsActive == false` off the SAME durable purchase);
  `RelationshipLossThenRestore_FlipsActiveWithNoWrites_PureReDerivation`
  (active→dormant→active is pure re-derivation, no ledger poke).
- **Disconnect / cache expiry removes exposure** —
  `ClientCache_InvalidateAndClear_FailClosed`: `Invalidate` (relationship loss
  before refetch) and `Clear` (ZNet teardown / disconnect) both flip
  `IsActiveForStone` to false; `ClientCache_IsActiveForStone_FailsClosedWithoutSnapshot`
  (never-delivered → fail closed).
- **Stale / out-of-order snapshots cannot reactivate** —
  `ClientCache_AppliesNewerSnapshot_DropsStaleReorder`: a reordered late fetch with
  an older sequence is dropped; delivery cannot roll backward. `ShouldRefetch`
  converges only forward (ahead sequence or changed Stone/character/authority
  revision).
- **Ineligible recipes remain vanilla** — the gate early-returns for any recipe
  that is not `ArrowWood` (`FieldFletchingRecipeGate.cs:92`); no other recipe is
  ever touched. Practice Range / Practice Arrow content (T025, a separate Local
  node) is never exposed by this gate.
- **Hostile payload / identity cannot forge exposure** —
  `ClientCache_HostilePayload_CannotForgeActivationForLocalStone` and
  `DeniedSnapshot_WithActiveLookingRow_StillDeliversNothing`; and at the wire, the
  server binds the requesting peer's principal from the delivering `ZRpc`, never
  the payload (`PersonalActivationDeliveryObserver.cs:76-95`), so a client cannot
  forge whose effect it asks for or author an active row. An unbound peer gets a
  fail-closed Denied reply.

### Runtime wiring — VERIFIED present at merged head
- `Plugin.Awake` `PatchAll`s the delivery transport
  `PersonalActivationDeliveryObserver` (`Plugin.cs:134`) AND the craft gate
  `FieldFletchingRecipeGate` (`Plugin.cs:161`). The gate is wired, not an orphan
  provider. Transport registers the request handler server-side on
  `ZNet.OnNewConnection` and the snapshot receive handler client-side, and the gate
  opportunistically refetches on a bounded 2s interval keyed by the Stone the local
  player stands in (`FieldFletchingRecipeGate.cs:178-187`).

## What is REASONED, not observed (honest last mile — "logs-green ≠ playable")

The two authoritative craft-decision surfaces both require a live client `Player`
that a headless server cannot supply:

- The craft gate is a **postfix on `Player.RequiredCraftingStation`**, gated to
  `Player.m_localPlayer` (`FieldFletchingRecipeGate.cs:91`). A dedicated
  `-nographics` server has **no local `Player`**, so — exactly as the merged T025 R2
  proof documented for `Player.PlacePiece` — the gate's live firing and the
  resulting station-free craft in the crafting UI **cannot be observed headless**.
  A read-only QADiag data-layer probe cannot observe it either, for the same reason.

What is therefore proven vs reasoned:
- **Proven (verified):** (a) the delivery channel the reviewer found missing is
  shipped and wired; (b) the exact server→wire→client-cache path the pure-client
  gate reads returns the correct active/dormant/denied verdict across every
  activation, disconnect, stale-reorder, and hostile-identity vector via
  link-compiled real execution; (c) the gate identifies only `ArrowWood`, rescues
  only the station requirement, only while active, and never flips a vanilla PASS.
- **Reasoned (GUI last mile):** a human on a joined GUI client, actively Attuned
  with purchased Field Fletching I, opening the crafting menu away from any station
  and seeing Wood Arrows craftable with ordinary inputs/yield, then losing the
  craft when the relationship is released / they disconnect — is reasoned from
  (a)+(b)+(c) and left for an owner GUI smoke. The task safety gate forbids
  launching a user-owned `valheim.x86_64` client here, and production
  Niflheim/Heistan are never touched.

## Spec/docs concordance
No spec drift introduced. Field Fletching I authors NO new content and exposes the
**unchanged** vanilla `ArrowWood` recipe (spec line 160; contracts.md §Archer;
`BushcraftRecipeProvider.cs` constants). SpecCheck recipe count unchanged — T026
adds no recipe row, it exposes an existing vanilla one. This evidence adds only
documentation.
