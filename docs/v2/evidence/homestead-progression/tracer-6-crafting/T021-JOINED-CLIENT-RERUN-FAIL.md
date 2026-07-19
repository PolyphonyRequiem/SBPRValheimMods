---
status: current
---

# T021 Refined Workshop — joined-client effective-Level-3 rerun

## Verdict: **FAIL** (product defect — deeper than the prior stale-head FAIL)

Task `t_8261a415`, rerun after parent remediation `t_2ac2ab59` merged PR #369.
Analyzed head: `223a50f20b393f1e26748efb09fd05d058f780bc` (checked out in the
worktree; identical merge-tree on `origin/main` as `fd10d2e`).

The runtime remediation genuinely fixed the *previous* FAIL (the pure
`EffectiveStationLevelProvider` had zero net48 callers). It now has real callers:
`RefinedWorkshopStationLevelPatch` postfixes `Player.RequiredCraftingStation`
(the craft/upgrade/repair gate) and `InventoryGui.SetupRequirementList` (the UI
seam), each routing through the provider. The bounded server→client activation
transport (`LocalActivationDeliveryObserver`) is registered, and the live
server composes the Local progression runtime at boot.

**But the +1 can never fire on any joined client, because the Refined Workshop
Local Effect can never reach `Active` at runtime.** No client walkthrough was
launched: the required precondition is structurally unreachable, so a live client
proof is impossible by construction, not by environment. This is verified at the
data/ingress layer.

## Why `Active` is unreachable (the decisive chain)

The gate applies the +1 only when the provider returns `BonusApplied=true`, which
requires `refinedWorkshopActive=true`
(`RefinedWorkshopStationLevelPatch.cs:98-102`, `EffectiveStationLevelProvider.cs:145-148`).

The client reads that bit ONLY from the replicated cache
(`ResolveActiveForLocalOccupant` → `LocalProgressionObserver.ClientCache.IsActiveForStone`,
`RefinedWorkshopStationLevelPatch.cs:199-212`), which the server fills from a
snapshot derived off `LocalProgressionObserver.Server`
(`LocalActivationDeliveryObserver.RPC_ActivationRequest:110-111`).

Server-side, a node's `Active` is a pure derivation:

    active = purchased && callerActive     (DerivedActivationView.cs:115)
    // and a node only becomes purchasable/deliverable after it is Developed:
    // state = purchased ? (active ? Active : Dormant) : (Developed ? Developed : ...)   (:118-120)

So `Active` requires the Refined Workshop node to be **Developed AND Purchased**
in the authoritative Stone aggregate. The only code that develops / commits /
purchases a Local node is:

- `LocalNodeProvisioningDriver` (calls `Facets.Handle(CommitTreeToFacet…)`,
  `Development.Handle(ApplyBPToNode…)`, `Activities.Handle(RecordAlignedActivity…)`,
  `LocalPolicy.Handle(SetSettlementLocalPolicy…)`), and
- `PurchaseCommandHandler.Handle(PurchaseNodeCommand)`.

**Neither has any runtime caller.** Exhaustive grep over `src/` (excluding tests):

- `LocalNodeProvisioningDriver` is referenced only by its own file and
  `tests/NiflheimSharedLocalEffectRuntimeTests.cs`. It is never constructed in any
  `Features/` observer, `Plugin.cs`, RPC handler, or console command.
- `Development.Handle` / `Facets.Handle` / `Activities.Handle` / `LocalPolicy.Handle`
  appear ONLY inside `LocalNodeProvisioningDriver.cs`.
- `PurchaseNodeCommand` is constructed nowhere at runtime.
- The only registered console command in the plugin is `sbpr_provision`
  (attune|bond) — it drives the RELATIONSHIP/Governor path only, not tree
  development or node purchase.
- The only registered plugin RPCs are `RpcProvision`, `RpcRequest`, `RpcSnapshot`,
  `RpcNotice` — none develop or purchase a node.
- `HomesteadProgressionPanel` is a hints-only *read* view (its own header:
  "command affordances are HINTS … not a client-authoritative can-commit flag");
  it issues no commands.

Live confirmation on the isolated server (`homestead-t009l-server`, world
`homesteadt009l` UID `-898655635`, the same dedicated topology the accepted
T009L2/T009L3 proofs used): the durable Homestead directory contains
`relationships.journal`, `pilot-account.journal`, `foundational-ap.journal` — and
**no `node-development.journal` and no `facet-commit.journal`** on any server on
the box. The boot line
`[Niflheim/HomesteadStones] Local progression runtime composed (server-authoritative)`
is present, so the runtime is wired — but with no ingress to develop/purchase, the
Refined Workshop node is permanently Undeveloped, `purchased=false`, `Active=false`.

Therefore, for every occupant on every joined client:
`ResolveActiveForLocalOccupant()` returns false → `EffectiveStationLevelProvider.Resolve`
returns `BonusApplied=false` → the gate postfix never flips `__result` → the UI
postfix never recolors. A real Level-2 station can never operate as effective
Level 3. The bonus is inert end-to-end.

## What the remediation DID correctly achieve (not inflating)

- Build gates green at the analyzed head:
  - net48 `SBPR.Niflheim.HomesteadStones` Release: 0 warnings / 0 errors.
  - net48 `SBPR.Trailborne` Release: 0 warnings / 0 errors.
  - Tests: **1242 / 1242 passed** (net8), duration ~2s.
- The provider now has real net48 consumers (the prior zero-caller FAIL is fixed).
- The gate postfix is correctly conservative: it only ever RESCUES a level-only
  vanilla failure on a present, type-matching station for an eligible portable
  recipe, and never turns a vanilla PASS into a fail
  (`RefinedWorkshopStationLevelPatch.cs:79-102`).
- Structure/build operations are never eligible operation kinds
  (`OperationFor` only emits PortableItem* kinds; provider rejects Structure/Build),
  so build/permission gates are structurally untouched — the "does not gain the
  bonus" half of the task is satisfied by construction. Absent station, ineligible
  item, area/policy/governor dormancy all fail closed
  (provider `eligible` conjunction + `ResolveActiveForLocalOccupant` fail-closed
  reads). These negative cases are correct precisely because the whole path
  resolves to no-bonus.

The problem is the POSITIVE case: there is no runtime path to the one state
(`Refined Workshop = Active`) that the task's primary acceptance criterion
("a real Level-2 station operates as effective Level 3 for an eligible portable
operation") requires. It exists only in unit tests via `LocalNodeProvisioningDriver`.

## Verified vs reasoned split

- **Verified (source + live data layer):** the caller wiring, the `Active` pure
  derivation, the total absence of any runtime develop/purchase ingress, and the
  absence of node-development/facet journals on the live isolated server.
- **Not attempted (and correctly so):** an in-world joined-client effective-Level-3
  frame. It is unreachable because the precondition is unreachable; launching a
  GPU client to demonstrate a permanently-false bit would produce no decision-grade
  positive evidence. `logs-green ≠ playable`, and here even the data layer cannot
  reach the active state.

## Safety / isolation

- Re-checked `valheim.x86_64` immediately before any deploy decision: **no client
  process running** (only server-side infra). No client was launched, stopped, or
  overwritten. No client files touched.
- Production Niflheim and Heistan containers untouched and still running. Only the
  isolated `homestead-t009l` surface and the repo worktree were read. No deploy was
  performed (the verdict did not require one). Deployed client/server DLLs are the
  pre-existing stale builds (client `df9b4fa…`, server `6890a68…`); the reviewed-head
  build is `8bc7c53…` — noted but irrelevant to the verdict, since the reviewed-head
  SOURCE itself contains no develop/purchase ingress.

## Routing

Focused remediation required (do NOT redesign the provider or the gate/UI patch —
they are correct): add a runtime ingress that develops + purchases the Refined
Workshop Local node on the authoritative server, so the Local Effect can reach
`Active` for an authorized occupant. Candidates the codebase already implies: wire
`LocalNodeProvisioningDriver` / `PurchaseCommandHandler` behind an admin console
command or RPC (mirroring `sbpr_provision`), or a legitimate in-game
development/purchase flow per spec. Then this joined-client gate can be re-run.

Per AGENTS.md: any behavior change here must move spec/docs in the same PR.
