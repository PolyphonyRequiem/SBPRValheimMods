---
status: current
---

# T029 remediation — Warrior T.W.I.G. Training runtime wiring

Author: `engineer-systems` (remediation implementer). Resolves the QA FAIL in
[`QA-joined-client-T029.md`](QA-joined-client-T029.md) (task `t_92e47866`, PR #366).
This is the node's own implementation evidence; the independent joined-client
capture and the Tracer-8 gate verdict remain QA / T032 (non-author).

## What QA found (correct) and the deeper root cause

QA's static analysis was right: the pure `LocalPlacementProvider` had **zero
runtime callers**, so on a real joined client a T.W.I.G. (`TrainingDummy`)
placement ran entirely through vanilla `Player.PlacePiece` with no SBPR gating —
the FR-016 effect-active / Settlement-policy / build-Permission AND never fired
in-world, and the required refusals could not occur. Prefab identity
(`TrainingDummy`) was already confirmed correct and is untouched.

Investigating the wiring surfaced a root cause **deeper than "wiring only"**: the
Warrior gate's admit decision is `LocalEffectActivationView.CanExercisePlacement`,
which `.Derive(...)` builds from the full **Stone-owned developed Local state**
(developed `TwigTraining@1`, committed Warrior Tree, Active Stone Level, single
Settlement Local policy) plus owner / authorized-Governor facts. At the time of
the first remediation attempt **no Stone-progression aggregate or command runtime
was composed server-side at all** — the `FacetCommandHandler` /
`DevelopmentCommandHandler` / `LocalPolicyCommandHandler` and any
`IStoneAggregateStore` existed only in the test suite.

## The authoritative dependency landed first (t_02c13405 / PR #368)

That missing substrate was built and merged independently as **PR #368**
(`fbea39c` on `origin/main`): `LocalProgressionServer` composes the accepted,
receipt-backed Facet / Development / Activity / LocalPolicy command handlers over
a real `IStoneAggregateStore` plus the shared character/authority stores, derives
per-occupant activation through `LocalActivationService` +
`LocalEffectActivationView`, and derives the owner / authorized-Governor
governance facts from **committed bond state** via `GovernorPresenceResolver`
(no dead flag, no parallel ledger).

T029 rebases onto that merge and **binds the Warrior placement gate to that one
authoritative progression truth**. The earlier attempt's
`WarriorProvisionalStoneStateSource` — a second, hardcoded developed-Local-state
source — is **removed**; there is no provisional Stone state anywhere in the
Warrior path.

## The fix (bound to the authoritative runtime)

1. **`Application/Runtime/WarriorLocalPlacementGate.cs`** — the engine-free
   server-validation core and the missing caller of the pure provider. It now
   reads the **authoritative `IStoneAggregateStore`** (the developed T.W.I.G.
   node, committed Warrior Tree, Active Stone Level, Settlement Local policy) and
   derives the owner / authorized-Governor governance facts from committed bond
   state via **`GovernorPresenceResolver`** — the exact projection
   `LocalActivationService.Derive` consumes. From server-owned facts only (the
   durable `player:<s_playerID>` peer key → bound internal principal; the placed
   piece's world position → Stone Area; the account–Stone authority reservation;
   the vanilla build-Permission result) it reconstructs
   `LocalEffectActivationView` and routes the exact placement through
   `LocalPlacementProvider.Admit`. A T.W.I.G. placement and a Local Effect
   snapshot for the same occupant therefore agree **by construction**. It fails
   closed on an unbound peer, a position inside no Stone Area, or a Stone with no
   authoritative aggregate. It makes no gating decision itself — every conjunct is
   the shared engine-free grammar.

2. **`Features/Progression/WarriorTwigPlacementObserver.cs`** (net48, listen-host)
   — a `Player.PlacePiece` postfix that recognizes the exact `TrainingDummy` prefab
   on a server-run placement, routes it through the gate, and on refusal **undoes**
   the placement (owner-claim → `ZNetView.Destroy`), matching
   `FoundationalPlacementObserver` in every authority/identity respect. ADR-0006:
   it reads and destroys a live world instance; it never clones a prefab.

3. **`Application/Runtime/WarriorTwigDedicatedIngress.cs` +
   `Features/Progression/WarriorTwigDedicatedIngressObserver.cs` +
   `Application/Runtime/WarriorTwigPendingUndoQueue.cs`** (dedicated-server path) —
   a joined dedicated-server client's build never runs `PlacePiece` on the server,
   so the client sends a direct per-peer notice carrying only an opaque ZDOID; the
   server authenticates the sender by the delivering `ZRpc`, captures it into a
   bounded pending queue, and on the `ZDOMan.Update` cadence re-derives every fact
   from its own ZDO store (prefab, creator, position via
   `ZdoServerPlacedInstanceSource`; build Permission via `PrivateArea.CheckAccess`),
   routes through the SAME gate, and destroys the piece server-side on refusal
   (the removal replicates to the client). Creator binding is enforced, so a client
   cannot force-undo a piece it did not place; a timed-out / unresolved notice is
   dropped with no action. This is the exact Foundational dedicated-ingress shape,
   specialized to gate/undo instead of credit.

4. **Composition (`FoundationalProgressionServer` + `FoundationalRuntimeBootstrap`).**
   `FoundationalProgressionServer.Create` no longer constructs the gate (it cannot
   — the authoritative Stone store is composed *after* it). It exposes
   `ArmWarriorTwig(stones, governorPresence)`; the engine-bound bootstrap composes
   the `LocalProgressionServer` and then immediately arms the Warrior gate against
   that runtime's **same** `IStoneAggregateStore` + `GovernorPresenceResolver`, so
   there is exactly one progression truth. Both observers are armed in
   `SBPR.Niflheim.HomesteadStones/Plugin.cs`; the gate + pending queue are null on
   a pure client and before arming, and the observers no-op until armed.

## No provisional state; one progression truth

The developed-node / committed-tree / Active-Stone-Level / Governor-present /
policy facts the gate consumes are now **exactly** the authoritative Stone
aggregate + committed-bond projection the shared Local Effect runtime owns. There
is no second, hardcoded Stone-state source. The exact-piece binding, the
effect-active / policy / build-Permission AND, the no-second-ledger dormancy
re-derivation, the owner/relationship membership, the fail-closed unbound-peer and
outside-area handling, the transport-authenticated identity, and the creator
binding are all the shipped engine-free grammar and the shipped authenticated
identity seams.

## Reaching a developed node for QA (accepted commands only)

QA reaches a developed T.W.I.G. node the legitimate way — through the shipped
`LocalNodeProvisioningDriver`, which issues only **accepted, receipt-backed
commands** (commit the Warrior Tree into the Martial Facet → credit BP →
`ApplyBPToNode` until developed → optional `SetSettlementLocalPolicy`) from a
bonded Governor subject. No hardcoded grant, no direct projection poke; any
handler rejection surfaces verbatim. This is the same path the shared-runtime
suite (`NiflheimSharedLocalEffectRuntimeTests`) uses, and the T029 runtime-gate
suite drives it end-to-end before querying the gate.

## Joined-client operator steps (for the qa-playtest capture)

1. Start the dedicated server; join a client and stand inside a Homestead Stone
   Area. With no developed T.W.I.G. node / no authorized Governor / outside policy,
   placing the T.W.I.G. is **refused + undone** → log
   `[warrior-twig] ... admission=EffectNotActive ... action=undone`.
2. Provision the node through accepted commands (bond a Governor, commit the
   Warrior Tree, credit BP, develop `TwigTraining@1`, set the Settlement Local
   policy) via the admin provisioning seam behind
   `[Progression] EnableAdminRelationshipProvisioning = true`.
3. Place the exact T.W.I.G. inside the Area **with** build access (no blocking
   ward) as a policy-eligible occupant → **stands**; log
   `[warrior-twig] ... admission=Admitted ... action=admitted`.
4. Place inside a ward you lack access to → **refused + undone**; log
   `admission=MissingBuildPermission ... action=undone`. Place any other piece
   (e.g. a wood floor) → untouched (`disposition=NotTwig`). Release the Governor
   bond → every T.W.I.G. placement goes dormant (`EffectNotActive`).

## Automated proof (this run)

- Rewritten tests: `tests/NiflheimWarriorTwigRuntimeGateTests.cs` — **18 tests**
  driving the gate, the dedicated ingress, and the pending-undo queue **against
  the composed `FoundationalProgressionServer` armed over the authoritative
  `LocalProgressionServer`** (real production wiring, developed via
  `LocalNodeProvisioningDriver` accepted commands — not a provisional grant).
  They cover: the gate is armed against the authoritative runtime (and is null
  before arming); a provisioned owner in-area + build-permitted **admits**; the
  same owner without build Permission **refused + undo** (`MissingBuildPermission`);
  an occupant outside a Private policy **refused + undo** (`EffectNotActive`);
  governance-dormancy after a Governor release **refuses even the owner**; an
  **undeveloped** node refused; an unbound peer **fail-closed**; outside every
  Stone Area refused; a non-T.W.I.G. prefab **declined, not undone**; and the
  dedicated path's admit / no-permission-undo / outside-policy-undo /
  non-twig-decline / creator-mismatch (no touch) / awaiting-replication / pump
  acts-once / deadline-drop / duplicate-converge.
- The pre-existing `tests/NiflheimWarriorTwigPlacementTests.cs` (pure provider) and
  the shared-runtime suite `tests/NiflheimSharedLocalEffectRuntimeTests.cs` are
  unchanged and still green.
- Full suite: **1243 / 1243** passing.
- Both net48 Release builds: **0 warnings / 0 errors**
  (`SBPR.Niflheim.HomesteadStones` and `SBPR.Trailborne`).
- `python3 scripts/docs-lint.py`: OK (185 docs).
- `git diff --check`: clean.
- `SpecCheck.cs` recipe manifest: **unchanged** — T029 registers no SBPR recipe or
  buildable (T.W.I.G. is the vanilla piece exposed under policy).

## Logs-green ≠ playable

This remediation makes the runtime path **reachable and correct under unit test of
the composed server, bound to the authoritative progression runtime**; it does not
itself capture the in-world joined-client artifact. Per the honesty rule, the
joined-client proof (a real client placing / being refused the T.W.I.G. per the
operator steps above) is the required T029 DoD-item-9 artifact and is captured by
`qa-playtest`, then independently re-run at T032. This card hands back to QA for
that capture.
