---
status: current
---

# T027 Fletcher's Habit — joined-client / in-world QA verdict @ 9b48670

- QA card: `t_275c5173` (qa-playtest).
- Implementation card: `t_e4767b36`. PR: [#380](https://github.com/PolyphonyRequiem/SBPRValheimMods/pull/380),
  branch `feat/hs-t027-fletchers-habit`, reviewed head
  `9b48670d4b127612edfd440343cef396b42937f6` (workspace worktree HEAD verified == this head).
- Safety gate: cleared by coordinator (prior GABS-owned QA client stopped; no user-owned
  `valheim.x86_64` play session seized). Production Niflheim/Heistan **never touched**
  (both headless dedicated servers left running, untouched). Isolated non-production QA
  server `homestead-t009l` (ports 2476-2478, non-public) used for the server side.
- Fresh net48 Release DLLs built this run @ 9b48670, deployed byte-identical to all three
  plugin locations (GUI client, t009l server config, t009l server data):
  - `SBPR.Niflheim.HomesteadStones.dll` sha256 `26a233a704ce28c50771d16192c30754847493c4eb33efc0ced919ab698e414c`
  - `SBPR.Trailborne.dll` sha256 `00789def835a071703ed95243b5cda39d96e26ab309aa9a683548fbf0231a8b5`

## Verdict: BLOCKED — required owner in-world proof is STRUCTURALLY UNREACHABLE at this head

The card's core deliverable is the human-pixel proof that an **owner** (a character that has
**developed + purchased** Fletcher's Habit) fires an eligible Wood Arrow, and on a recovery
pass picks up the *same exact* arrow instance (scenarios 1, 3, 4, and the owner half of 5).

That proof **cannot be produced** at reviewed head 9b48670, because **no runtime code path
exists — gameplay or QA seam — by which any character can come to OWN Fletcher's Habit on a
joined client.** This is a missing-ingress gap in the same class the T021 (PR #371) and T009L
joined-client reruns previously caught ("accepted handler wired, ZERO runtime callers"), and
it blocks merge of PR #380 until closed. It is **not** a defect in the reviewed recovery logic,
which is correct at the layer level; it is a missing **QA-provisioning seam** that the node's
own accepted per-node DoD (a decision-grade owner in-world proof) structurally depends on.

### Why ownership is unreachable (source-verified @ 9b48670)

`ProjectileRecoveryProvider.OwnsFletchersHabit(stone, character, authority)`
(`Adapters/Archer/ProjectileRecoveryProvider.cs:276-295`) returns true **only** when the
`DerivedActivationView` row for `FletchersHabit@1` is **`Developed && Purchased`** — i.e. the
Stone must carry a developed FletchersHabit `NodeDevelopmentRecord` **and** the acting character
must carry a `NodePurchaseRecord` for it. The pure-client gate path
(`ProjectileRecoveryGate.cs:195-199`) reads the same durable `IsOwned` (Purchased bit) from the
server-stamped `PersonalActivationSnapshot`; the host path (`:202-226`) reads the same aggregates.

The ONLY runtime caller that can create a personal purchase record is
`LocalProvisioningIngress.PurchaseNode(...)` (`Application/Runtime/LocalProvisioningIngress.cs:97`).
Grepping the whole `SBPR.Niflheim.HomesteadStones` tree, **that method has no runtime caller** —
no console command, no RPC handler, no gameplay Offering/purchase interaction invokes it. The
three shipped isolated-QA/playtest seams are:

| Console cmd       | RPC                                | What it provisions                         |
|-------------------|------------------------------------|--------------------------------------------|
| `sbpr_provision`  | `SBPR_Niflheim_ProvisionRelationship` | Bond / Attunement (relationship only)   |
| `sbpr_develop`    | `SBPR_Niflheim_ProvisionLocalNode`    | Develops a **Local** node (Refined Workshop only — `selector 1`) |
| `sbpr_savor`      | `SBPR_Niflheim_ProvisionSavor`        | Develops the Savor **Local** node + policy |

None of them purchases a **personal Permanent-Effect** node. `sbpr_develop` develops a
Stone-owned Local node and its selector hard-codes Refined Workshop; it neither reaches
FletchersHabit nor issues a `NodePurchaseRecord`. `sbpr_provision` only creates a relationship.
The server config on the running t009l box confirms the live gates: `EnableAdminRelationshipProvisioning = true`,
`EnableAdminLocalNodeProvisioning = false`, `EnableSavorPlaytestSeam = false` — and even with all
three ON, none can make a character own Fletcher's Habit.

The unit suite establishes ownership by **directly constructing** in-memory aggregates
(`tests/NiflheimFletchersHabitTests.cs:78-108`: a hand-built `StoneProgressionAggregate` with a
`NodeDevelopmentRecord` + a `CharacterProgressionAggregate` with a `NodePurchaseRecord`). There
is no analogous runtime seam, so that owned state is reachable in tests but not in a live joined
client. Sibling T026 (Field Fletching I, also a personal purchase) has the same gap; its accepted
R2 proof was therefore explicitly **REASONED, not observed**. This card forbids that substitution,
so it must block rather than accept a reasoned owner last-mile.

## What WAS verified in-world (reachable at this head)

A fresh GUI client (GABS `games_start valheim`, DISPLAY=:0, RTX 3090) was launched, connected via
the ValBridgeServer GABP bridge, and driven with the in-client `run_script` C# evaluator. On the
live client:

1. **T027 gate is loaded and Harmony-patched live.** `SBPR.Niflheim.HomesteadStones` assembly
   loaded; `Features.Archer.ProjectileRecoveryGate` present; **both** `Projectile.Setup` and
   `Projectile.OnHit` appear in `Harmony.GetAllPatchedMethods()`; `FletchersHabitContent.EligibleArrowItem == "ArrowWood"`.
   (state/live-provider-branch-probe.cs run output.)
2. **The live net48 provider resolves every decision branch correctly**, exercised against the
   assembly loaded in the running client (not the net8 test project), chance = 0.5:
   - owner + ArrowWood + SolidStructure + roll 0.1 → **Recovered, count=1, arrow=ArrowWood(q3, QA_PROV_TAG)** — exact provenance (quality + crafter) preserved.
   - owner + ArrowWood + Ground + roll 0.1 → Recovered, count=1, exact provenance preserved.
   - owner + ArrowWood + SolidStructure + roll 0.9 → RollFailed, count=0.
   - owner + ArrowWood + Water → NonRecoverableSurface, count=0 (no roll).
   - owner + ArrowWood + LostOrExpired (miss/TTL) → NonRecoverableSurface, count=0 (no roll).
   - owner + ArrowWood + ArcheryTarget + targetReturnWon=true → **SuppressedByTargetReturn**, count=0 (single deterministic return, no double).
   - **non-owner** + ArrowWood + SolidStructure + roll 0.1 → **NotOwned, count=0 (vanilla)**.
   - owner + ArrowIron + SolidStructure + roll 0.1 → IneligibleArrow, count=0.
3. **Fail-closed confirmed live.** On the client `LocalProgressionObserver.Server` is null and no
   owned `PersonalActivationSnapshot` is held, so the gate's `ResolveOwnedForShooter` takes the
   pure-client branch and returns false → the roll never runs → vanilla behaviour. A non-owner
   firing an ArrowWood gets no recovery.

This is decision-logic + fail-closed verification of the actually-loaded runtime, one layer above
"logs-green" — but it is **not** the human-pixel owner proof the card requires, and I do not claim
it as such. "Logs-green is never playability": the branches above prove the shipped `Resolve`
would recover the exact instance *if* a shooter owned the node — they do not prove a live owner
can fire and pick the arrow up, because no live owner can exist at this head.

## Environment honesty notes

- The GUI client, on launch, auto-loaded a leftover **local singleplayer** world ("T018IronQA",
  char DevTester) rather than joining the t009l dedicated server. The reachable verification above
  does not depend on which world loaded (it exercises the loaded assembly + the fail-closed gate),
  but the genuine non-host **joined-client** join was not completed, because the run pivoted to the
  blocking structural finding before the owner matrix could be attempted — attempting the join
  would not have unblocked ownership.
- No production world or the user's play session was touched. The QA client was launched by GABS
  and is left for teardown by the operator/next run.

## Required remediation (route, do not self-execute)

Ship a QA-only, config-gated, admin-authenticated **Fletcher's Habit ownership provisioning seam**
that drives `LocalProvisioningIngress.PurchaseNode(...)` (and the node development it depends on)
through the accepted, receipt-backed handlers — the exact sibling of `sbpr_develop` /
`RelationshipProvisioningAdmin`, but reaching a **personal Permanent-Effect purchase** instead of a
Local-node develop or a relationship. Suggested: a new server-owned flag (default OFF, e.g.
`Progression.EnableAdminPersonalNodeProvisioning`) + `sbpr_purchase fletcher` console command +
`SBPR_Niflheim_ProvisionPersonalNode` RPC. After that seam exists, re-run this card's 5-scenario
owner matrix on a genuine joined GUI client. The same seam unblocks the sibling T026 owner proof.

PR #380 stays **open, unmerged**, pending both the remediation and the subsequent owner in-world
proof.
