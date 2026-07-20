---
status: current
---

# T022 Masterwork — R3 genuine four-AT run: BLOCKED on a missing server-side purchase ingress (fix-forward)

- QA card: `t_4f181af7` (qa-playtest), run 1082. Adversarial reviewer R3 `t_e025cb54` BLOCKED PR #392 @ `0ad2611`
  because the shipped evidence self-admits a "GUI last mile REASONED" verdict and the accepted per-node DoD
  requires a genuine joined-client artifact (HARD HONESTY GATE — no waiver on sibling precedent).
- Impl head under test: PR #388 `fix/hs-t022-masterwork-client-delivery` @ **8ccf6d30be0b0747d8a38bfa634b437208738090**
  (OPEN, unmerged). PR #392 is the docs-evidence PR on `docs/hs-t022-masterwork-joined-client-qa` @ `0ad2611`.
- Raw capture: `capture/t022-r3-purchase-ingress-gap-20260719.log`.

## Verdict: BLOCKED — the genuine dedicated-server four-AT run is not producible at this head, and it is
NOT an owner-setup gate. It is a code-level fix-forward gap in PR #388.

The prior two attempts blocked on "owner-gated GUI setup" (second Steam license + active :0 desktop +
progression seed). Those infrastructure gates are now CLEARED by the completed parents:

- **Second licensed client** (`t_35d19e57`, PASS): valbot lane provisioned + proven — Steam-initialized
  rendered menu, 36 GABP `valheim.*` tools on `127.0.0.1:8081`, clean scoped stop. Currently stopped/idle.
- **Live in-world instrument**: the primary GABS `127.0.0.1:8080` drives a live modded GUI client (pid 816135,
  `isServer=True`) whose `valheim.run_script` executes arbitrary C# on Unity's main thread against the live
  game assemblies — the exact mechanism the sibling T019 used for its accepted genuine verdict
  (`docs/v2/evidence/.../joined-client-t019-swift-preparation-PASS.md`).
- **Isolated dedicated server**: `homestead-t009l-server` UP on ports 2476/2477/2478 with the EXACT-head T022
  build deployed (HS `3cd86e94…`, TB `7c5d7d81…`), booted clean (SpecCheck ✓ 31 recipes, [stone-areas]
  registered=7, zero SBPR Harmony failures). Production `niflheim-server` (:2456) and `heistan-server` (:2466)
  UNTOUCHED.
- **Exact-head build reproduced**: net48 Release, both projects 0 warnings / 0 errors, DLL hashes
  **byte-identical** to the recorded evidence hashes (HS `3cd86e94c0a09d61e4843710fefd2408cd8c2e16470cae139025bac5816ee3b8`,
  TB `7c5d7d8188b94c7cea4f420674c10449e3a3b5af5deb2dd97ad4b8d114af241b`).

## Why the four-AT run still cannot be produced (the real, sharper blocker)

The four ATs split into two topologies:

| AT | Requires |
|----|----------|
| AT-MASTERWORK-ISSUE, AT-ITEM-UPGRADE-PRESERVE | a crafter for whom Masterwork is **ACTIVE** |
| AT-ITEM-TRANSFER, AT-ITEM-TAMPER-DEGRADE | a **pure joined client** hitting PR #388's `MasterworkDedicatedDeliveryObserver` (keyless read + server ZRpc validation) — the exact path R3 says was never exercised in-world |

Masterwork activation (`Adapters/Crafting/WorkmanshipIssuanceProvider.cs:137-151`, `IsMasterworkActive`) derives
from `(StoneProgressionAggregate, CharacterProgressionAggregate` **holding a Masterwork purchase record**`,
AccountStoneAuthorityIndex` **holding an active relationship**`)`. Both halves must exist on the authoritative
(dedicated-server) side for a joined principal.

The shipped runtime provisioning seams are:

- `sbpr_provision` (`RelationshipProvisioningAdmin`) → **Bond / Attunement only** (enabled on t009l).
- `sbpr_develop` (`LocalProgressionProvisioningAdmin`) → `DevelopLocalNode`, selector `refined`(=1) **only**.
- `sbpr_savor` (`SavorProvisioningAdmin`) → Savor Local node **only**.

**None purchases a personal Offered node.** The only method that routes a personal-Offered-node purchase through
the accepted `PurchaseCommandHandler` is `LocalProvisioningIngress.PurchaseNode(...)`
(`Application/Runtime/LocalProvisioningIngress.cs:97-123`) — and it has **zero runtime callers**:

- `grep '\.PurchaseNode('` across `src` → no RPC/console caller (only the pure domain transition
  `NodePurchases.PurchaseNode` at `PurchaseCommands.cs:226`).
- `CreateLocalProvisioningIngress()` has a single caller (`LocalProgressionProvisioningAdmin.cs:147`) which
  invokes **only** `DevelopLocalNode`, never `PurchaseNode`.
- The ingress file's own header (`:16`) states: *"PurchaseCommandHandler had ZERO runtime callers."*

So there is **no reachable runtime ingress to purchase the Masterwork personal Offered node** for a joined
principal on the dedicated server. The relationship half is provisionable (`sbpr_provision bond`); the purchase
half is not. `IsMasterworkActive` therefore returns false for every joined principal, and AT-MASTERWORK-ISSUE
(and the three downstream ATs) cannot fire in-world on the PR #388 dedicated topology R3 requires.

The T019 trick — reflect state into the composed server stores in-process — does **not** transfer: `run_script`
reaches only the GUI client, and on the dedicated topology that client is a PURE client whose composed server
stores are null. R3 explicitly rejects the listen-host path, and the headless dedicated server has no scripting
seam.

## Disposition

This is a **fix-forward gap in PR #388**, not an owner-setup gate: the feature ships the delivery + tooltip +
keyless-verdict seams but **no server-side Masterwork purchase-provisioning ingress** — the exact analog of
`sbpr_develop` / `sbpr_savor` that every other proven node has. The task scope forbids authoring runtime fixes
("Do NOT author runtime fixes"), so this QA slot cannot add the seam.

Recommended fix-forward (implementation `t_b78a05c4` / remediation `t_cdc76200`): add a gated, isolated-QA
Masterwork purchase provisioning ingress mirroring `SavorProvisioningAdmin` — a server-owned BepInEx flag +
admin-gated direct RPC + client console command that calls `LocalProvisioningIngress.PurchaseNode(...)` for the
Masterwork personal Offered node (Crafting tree). Note per AGENTS.md: this is a runtime/QA-seam addition only;
it introduces no new recipe and no spec drift (SpecCheck count stays 31). Once that ingress exists, the
genuine four-AT run becomes executable end-to-end on t009l (dedicated) + primary/valbot pure clients via the
`run_script` instrument and the ValBridgeServer `craft_item`/`drop_item`/tooltip tools.

## Safety / isolation (this run)

No production server touched (niflheim :2456 / heistan :2466 UNTOUCHED). No client binary launched, stopped, or
modified — primary client 816135 left exactly as found; valbot lane left stopped. t009l left as found. All
findings are read-only static analysis of the exact-head source plus read-only live probes.
