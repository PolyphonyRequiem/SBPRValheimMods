---
status: current
---

# T021 remediation 2 — durable Local-node develop/purchase ingress

Task `t_79588427`. Closes the product defect the T021 joined-client rerun
(`T021-JOINED-CLIENT-RERUN-FAIL.md`, PR #371 head `f601d94`) caught: the accepted
progression command handlers wired into `LocalProgressionServer` +
`LocalNodeProvisioningDriver` + `PurchaseCommandHandler` had **zero runtime
callers**, so a Stone-cultivated Local node (Refined Workshop) could never reach
`Developed` at runtime and its Local Effect could never derive `Active`. The
positive effective-Level-3 path was structurally unreachable.

## What was missing (the FAIL's decisive chain)

Refined Workshop's `Active` is a pure derivation off the authoritative Stone
aggregate (`LocalEffectActivationView.Derive`): it requires the node **Developed**
and its owning Crafting **Tree committed**, plus an authorized Governor present,
the occupant inside the Stone Area, and Settlement Local policy eligibility. The
only code that develops/commits a Local node is `LocalNodeProvisioningDriver`
(itself only reachable through the accepted Facet/Activity/Development handlers),
and it had no caller outside the unit tests. There was also no runtime path that
seeded the authoritative Stone aggregate at all — the live server composed the
`LocalProgressionServer` over an **empty** Stone store that nothing ever wrote.

## The fix (focused; no gameplay redesign)

A single isolated-QA ingress, wired the exact way the accepted relationship seam
(`RelationshipProvisioningAdmin` → `RelationshipProvisioningIngress`) already
works:

- **`Application/Runtime/LocalProvisioningIngress.cs`** (engine-free, unit-tested)
  — routes a **server-derived** subject through the shipped, receipt-backed
  handlers:
  - `DevelopLocalNode` seeds only the **bare pre-progression Stone envelope** when
    the Stone aggregate is absent (the empty owner row the accepted commands need
    — never a node-state write, never overwriting an existing/rehydrated Stone),
    then drives `LocalNodeProvisioningDriver` (commit Tree → credit BP → develop
    node) to completion. Any handler rejection surfaces verbatim.
  - `PurchaseNode` routes a personal Offered-node purchase through the accepted
    `PurchaseCommandHandler` (its own durable `node-purchase.journal` alongside the
    four progression journals), so the purchase authority/revision/idempotency
    gates are a real reachable caller.
- **`LocalProgressionServer.CreateLocalProvisioningIngress()`** composes the
  purchase handler over the SAME shared Stone/character/authority stores + durable
  directory and returns the ingress.
- **`Features/Progression/LocalProgressionProvisioningAdmin.cs`** (net48) — the
  admin/isolated-QA seam: a **direct per-peer `ZRpc`** handler
  (`SBPR_Niflheim_ProvisionLocalNode`) + the `sbpr_develop refined` console
  command, gated behind the server-owned `Progression.EnableAdminLocalNodeProvisioning`
  BepInEx flag (default **OFF**) AND vanilla-normalized admin authority. Identity
  is the transport-authenticated peer's **bound-internal** principal (never a
  forgeable routed sender / client claim); the target Stone is resolved from the
  peer's server-owned character ZDO position. Outside that gate the handler is
  never registered (flag off) or rejects (non-admin) — **production fails closed**.

No provisional activation, no direct node-state write, no second ledger, no bypass
of Local policy/governance/dormancy, and Refined Workshop mechanics are unchanged.

## Verification

- **Red-first, then green:** `tests/NiflheimLocalProvisioningIngressTests.cs` — 10
  tests through the SAME shared runtime a live server composes:
  - Refined Workshop develops from an **empty** Stone store via accepted commands
    only; the developed node then **derives `Active`** for an eligible occupant and
    goes dormant on area exit / absent Governor (the positive precondition the FAIL
    found unreachable).
  - the seed never overwrites an existing Stone; a **restart** rehydrates the
    developed node from the **durable journals**, not the seed;
  - replay is idempotent (no double-develop, revision unchanged);
  - hostile/unauthenticated/non-Local-node reject with no developed node;
  - the personal-node purchase authority gate (`RelationshipRequired` for a
    bonded-but-unattuned buyer) is a real reachable caller.
- **Full suite:** 1281 / 1281 (net8), up from 1271 (+10).
- **Both net48 Release builds:** `SBPR.Niflheim.HomesteadStones` and
  `SBPR.Trailborne` — 0 warnings / 0 errors.
- **docs-lint:** OK.

## Safety / isolation

No Valheim client or server was launched, stopped, or overwritten. Production
Niflheim / Heistan untouched. The change is source + tests + docs only; the
joined-client effective-Level-3 artifact is re-run downstream by QA `t_8261a415`.
