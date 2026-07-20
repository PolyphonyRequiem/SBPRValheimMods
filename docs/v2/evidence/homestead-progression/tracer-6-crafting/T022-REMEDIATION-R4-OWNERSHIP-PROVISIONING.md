---
status: current
---

# T022 remediation R4 — Masterwork ownership provisioning (make the four-AT run reachable)

**Task:** `t_4ce3873a` · **Branch:** `fix/hs-t022-masterwork-ownership-provisioning` ·
**Stacked onto:** PR #392 branch `fix/hs-t022-masterwork-client-delivery` @ head `0ad2611`.

## Problem (why QA `t_4f181af7` was structurally blocked)

The accepted T022 Masterwork node issues a Workmanship Property only while it is ACTIVE for the
crafter. `WorkmanshipIssuanceProvider.IsMasterworkActive` derives that (via the shipped T004
`DerivedActivationView`) from **a personal purchase record for `Masterwork@1` at the Stone AND an
active relationship** — no second ledger (`AT-NO-ACTIVE-LEDGER`).

At PR #392 head that active-purchased state was **structurally unreachable at runtime**:

- `LocalProvisioningIngress.PurchaseNode` had **zero runtime callers** — nothing ever drove a
  Masterwork personal purchase.
- `LocalNodeProvisioningDriver.Provision` only develops **Stone-cultivated Local** nodes
  (`NotALocalNode` for a personal Offered node), so nothing made Masterwork **Offered** on the Stone.

So no joined principal could acquire a Masterwork purchase record and `IsMasterworkActive` was always
false — the genuine dedicated-server four-AT run could never begin. QA had repeatedly confirmed the
infrastructure/owner gates were clear but could not reach an owned node.

## Fix (smallest QA-only, config-gated, admin-authenticated ownership seam — through accepted handlers)

Everything crosses the SAME accepted, receipt-backed handlers. No gameplay shortcut, no progression
redesign, production fails closed (default OFF). It never mints Attunement or AP.

| Layer | Change |
|-------|--------|
| `Application/Activation/LocalNodeProvisioningDriver.cs` | New `ProvisionOffered` develops a personal **Offered** node to completion via the identical commit Tree → credit BP → `ApplyBPToNode` chain the Local path uses (shared `ProvisionInternal`); ownership guard now rejects the wrong flavour as `NotAnOfferedNode` / `NotALocalNode`. |
| `Application/Runtime/LocalProvisioningIngress.cs` | `OfferMasterwork` (Bond authority) seeds the bare Stone when absent + offers Masterwork; `BuyMasterwork` (Attunement authority) purchases it via the accepted `PurchaseCommandHandler`; `OwnMasterwork` composes both for a two-subject QA subject. |
| `Features/Crafting/MasterworkOwnershipProvisioningAdmin.cs` (net48) | DIRECT per-peer `ZRpc` `SBPR_Niflheim_ProvisionMasterworkOwnership` + `sbpr_master offer\|buy` console command; registered only when `Crafting.EnableAdminMasterworkOwnershipProvisioning` is true AND the transport-authenticated sender is a normalized admin. Bound-internal principal + Stone resolved server-side. |
| `Plugin.cs` | Binds the server-owned config flag (default **false**) and patches the seam. |

### Why offer and buy are two separate commands

The accepted authority reservation model allows one character only ONE active relationship per Stone:
**develop/offer requires a Bond, purchase requires an Attunement** (`ReservationFor` returns the one
reservation; `FindActiveBond` and `HasActiveAttunement` each demand their kind). A single character
therefore cannot satisfy both. The genuine two-client QA matrix runs `sbpr_master offer` as the Governor
(Bond) and `sbpr_master buy` as the attuned buyer — matching the real gameplay separation.

## Operator steps (isolated-QA)

1. ENABLE `[Crafting] EnableAdminMasterworkOwnershipProvisioning = true` and
   `[Progression] EnableAdminRelationshipProvisioning = true`; restart the dedicated server.
2. On the Governor client (admin), stand in the Stone Area → `sbpr_provision bond` → `sbpr_master offer`.
3. On the buyer client, be attuned (`sbpr_provision attune`) and hold earned Personal AP → `sbpr_master buy`.
4. VERIFY the server log prints `[masterwork-ownership] buy outcome=Purchased ...`; an eligible craft
   now issues a validated Workmanship Property.
5. DISABLE the flag and restart.

## Tests (red-first) — `tests/NiflheimLocalProvisioningIngressTests.cs` (+7)

| Test | Proves |
|------|--------|
| `Ownership_offer_then_buy_reaches_active_purchased_masterwork_via_accepted_handlers` | A fresh authorized subject reaches an ACTIVE purchased Masterwork through the real handlers, asserted via the exact production `IsMasterworkActive` gate. |
| `Ownership_buy_before_offer_rejects_node_not_offered_no_purchase` | Purchase before offer rejects `NodeNotOffered`, no mutation. |
| `Ownership_buy_by_unattuned_subject_rejects_relationship_required` | Bond alone is not purchase authority (`RelationshipRequired`). |
| `Ownership_buy_by_unfunded_buyer_rejects_insufficient_personal_ap` | Unfunded buyer rejects `InsufficientPersonalAP`. |
| `Ownership_offer_of_wrong_ownership_local_node_rejects_not_an_offered_node` | `ProvisionOffered` refuses a Local node (`NotAnOfferedNode`). |
| `Ownership_buy_is_idempotent_on_replay_single_purchase_and_debit` | Replay returns `Replayed`; exactly one purchase record, one AP debit. |
| `Ownership_own_composite_two_subjects_reaches_active_purchased` | The `OwnMasterwork` composite reaches active-purchased. |

Red-first confirmed: before Stone-seeding was added to `OfferMasterwork` the develop path failed
`StoneNotFound`; the positive-path `IsMasterworkActive` assertion was mutated to `Assert.False` and
turned RED (`Expected: True, Actual: False`), then reverted GREEN.

## Verification

| Gate | Result |
|------|--------|
| Full test suite (`SBPR.Trailborne.Tests`) | **1454 / 1454** |
| Workbench (`StoneContent.Workbench.Tests`) | **59 / 59** |
| net48 Release `SBPR.Trailborne` | **0 warnings, 0 errors** |
| net48 Release `SBPR.Niflheim.HomesteadStones` | **0 warnings, 0 errors** |
| `docs-lint` | OK |
| `git diff --check` | clean |
| SpecCheck recipe manifest | unchanged (this seam registers no recipe) |

## Scope / honesty

Not live-QA. This makes the genuine two-client four-AT Masterwork matrix **reachable**; the actual
joined-client run is QA `t_4f181af7`. T022's exact dedicated-server entitlement and key-never-on-wire
issuance contracts are unchanged. No production Niflheim/Heistan or user-owned Valheim session touched.
