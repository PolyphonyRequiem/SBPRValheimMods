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

Investigating the wiring surfaced a root cause **deeper than "wiring only"**:

- The Foundational placement path could be wired because its only gate input —
  an **active relationship** — is composed server-side (`RelationshipCommandHandler`
  + `IAccountStoneAuthorityStore`) and admin-provisionable at runtime via the
  shipped `sbpr_provision` seam.
- The Warrior gate's admit decision is `LocalEffectActivationView.CanExercisePlacement`,
  which `.Derive(...)` builds from the full **Stone-owned developed Local state**
  (developed `TwigTraining@1`, committed Warrior Tree, Active Stone Level, single
  Settlement Local policy) plus owner / authorized-Governor facts. Verified: **no
  Stone-progression aggregate or command runtime is composed server-side at all** —
  the `FacetCommandHandler` / `DevelopmentCommandHandler` / `LocalPolicyCommandHandler`
  and any `IStoneAggregateStore` exist only in the test suite. `Plugin.cs` composes
  relationship authority + AP receipts + Stone-area membership + bound sessions, and
  nothing else.

So the provider could not simply be "called" — the state it consumes did not
exist at runtime.

## The fix (mirrors the accepted Foundational provisional-proof pattern)

The Foundational runtime already ships provisional server-owned proof policies
(`ServerHomesteadFamilyResolver`, `ServerHomesteadBondPolicy` in
`FoundationalRuntimeBootstrap.cs`) so a joined client can exercise the real
pipeline before every upstream runtime exists. T029 does the same for the Warrior
Local slice, changing **no** pure grammar:

1. **`Application/Runtime/WarriorLocalPlacementGate.cs`** — the engine-free
   server-validation core and the missing caller of the pure provider. From
   server-owned facts only (the durable `player:<s_playerID>` peer key → bound
   internal principal; the placed piece's world position → Stone Area; the
   composed relationship authority; the vanilla build-Permission result) it
   reconstructs `LocalEffectActivationView` and routes the exact placement through
   `LocalPlacementProvider.Admit`. It **fails closed** on an unbound peer, a
   position inside no Stone Area, or a Stone with no developed state. It makes no
   gating decision itself — every conjunct is the shared engine-free grammar.

2. **`Application/Runtime/WarriorProvisionalStoneStateSource.cs`** — the
   provisional Stone-owned Local state (the direct analogue of the Foundational
   provisional resolvers): every resident Stone reports the one authored T.W.I.G.
   node **developed**, the Warrior Tree **committed**, Active Stone Level 2, an
   authorized Governor present, and an **Attuned** Settlement Local policy. Attuned
   is the load-bearing proof choice: under Attuned the effect is active for an
   occupant holding an active Bond/Attunement — relationship state that IS composed
   and IS admin-provisionable — so the joined-client proof needs **no un-wired
   input**. Policy mode and Governor presence are overridable so the outside-policy
   and governance-dormancy refusals are also demonstrable.

3. **`Features/Progression/WarriorTwigPlacementObserver.cs`** (net48, listen-host)
   — a `Player.PlacePiece` postfix that recognizes the exact `TrainingDummy` prefab
   on a server-run placement, routes it through the gate, and on refusal **undoes**
   the placement (owner-claim → `ZNetView.Destroy`), matching
   `FoundationalPlacementObserver` in every authority/identity respect. ADR-0006:
   it reads and destroys a live world instance; it never clones a prefab.

4. **`Application/Runtime/WarriorTwigDedicatedIngress.cs` +
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

5. Both observers armed in `SBPR.Niflheim.HomesteadStones/Plugin.cs`; the gate +
   pending queue are composed on `FoundationalProgressionServer.Create` and
   disarmed on a pure client.

## What is provisional, and what is not

Provisional (clearly labelled in-source, swappable when the real Stone-progression
command runtime is composed — only `WarriorProvisionalStoneStateSource` changes):
the developed-node / committed-tree / Active-Stone-Level / Governor-present /
Attuned-policy Stone snapshot.

**Not** provisional / not weakened: the exact-piece binding, the
effect-active / policy / build-Permission AND, the no-second-ledger dormancy
re-derivation, the owner/relationship membership, the fail-closed unbound-peer and
outside-area handling, the transport-authenticated identity, and the creator
binding are all the shipped engine-free grammar and the shipped authenticated
identity seams.

## Joined-client operator steps (for the qa-playtest capture)

1. Start the dedicated server; join a client and stand inside a Homestead Stone
   Area. Placing the T.W.I.G. now is **refused + undone** (outside the Attuned
   policy — no relationship) → log `[warrior-twig] ... admission=EffectNotActive ...
   action=undone`.
2. Enable `[Progression] EnableAdminRelationshipProvisioning = true`, restart, join
   as a server admin, stand in the Area, run `sbpr_provision attune`.
3. Place the exact T.W.I.G. inside the Area **with** build access (no blocking
   ward) → **stands**; log `[warrior-twig] ... admission=Admitted ... action=admitted`.
4. Place inside a ward you lack access to → **refused + undone**; log
   `admission=MissingBuildPermission ... action=undone`. Place any other piece
   (e.g. a wood floor) → untouched (`disposition=NotTwig`).

## Automated proof (this run)

- New tests: `tests/NiflheimWarriorTwigRuntimeGateTests.cs` — **18 tests** driving
  the gate, the dedicated ingress, and the pending-undo queue **against the
  composed `FoundationalProgressionServer`** (i.e. real production wiring, not just
  the pure value object). They cover: the gate is composed on the production
  server; attuned + bound + in-area + build-permitted **admit**; the same occupant
  without build Permission **refused + undo** (`MissingBuildPermission`); a bound
  but unattuned occupant **refused outside policy + undo** (`EffectNotActive`); an
  unbound peer **fail-closed**; outside every Stone Area refused; a non-T.W.I.G.
  prefab **declined, not undone**; governance-dormancy refusal; Everyone-policy
  admit; and the dedicated path's admit / no-permission-undo / outside-policy-undo /
  non-twig-decline / creator-mismatch (no touch) / awaiting-replication / pump
  acts-once / deadline-drop / duplicate-converge.
- The pre-existing `tests/NiflheimWarriorTwigPlacementTests.cs` (pure provider, 11)
  and `tests/NiflheimLocalPolicyDormancyTests.cs` (shared grammar) are unchanged and
  still green.
- Full suite: **1224 / 1224** passing (was 1206; +18 new gate wiring, plus the
  prior suite growth).
- Both net48 Release builds: **0 warnings / 0 errors**
  (`SBPR.Niflheim.HomesteadStones` and `SBPR.Trailborne`).
- `python3 scripts/docs-lint.py`: OK (184 docs).
- `git diff --check`: clean.
- `SpecCheck.cs` recipe manifest: **unchanged** — T029 registers no SBPR recipe or
  buildable (T.W.I.G. is the vanilla piece exposed under policy).

## Logs-green ≠ playable

This remediation makes the runtime path **reachable and correct under unit test of
the composed server**; it does not itself capture the in-world joined-client
artifact. Per the honesty rule, the joined-client proof (a real client placing /
being refused the T.W.I.G. per the operator steps above) is the required T029
DoD-item-9 artifact and is captured by `qa-playtest`, then independently re-run at
T032. This card hands back to QA for that capture.
