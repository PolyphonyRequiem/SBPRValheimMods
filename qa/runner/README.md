# qa/runner — external T022 QA runner (ADR-0009 §1, §6)

`sbpr-qa-t022.py` is the **engine-free** external runner for the T022 Masterwork
joined-client scenario. Per ADR-0009 it is the **sole scenario state machine and the
sole PASS/FAIL composer**: the BepInEx helper (`qa/SBPR.QaHarness.T022/`) emits dumb
primitive facts, and this runner correlates the server + both client receipts into
the single final evidence document. **Only the runner declares PASS/FAIL, and it
cannot PASS without all four named T022 acceptance tests asserted, cleanup confirmed,
the exclusive lane lease held, and the artifact pins verified.**

## Status: full runner + live-execution CAPABILITY (M6-EXEC), dry-run default

M5 wrapped the adopted transport-neutral FSM (`fsm/`) in the ADR operational envelope
(`runner_core/`). M6-EXEC adds the **live-execution wire + operator drivers** the M5
docstrings deferred to, WITHOUT changing the FSM `Transport` Protocol (so the 32-case
invariant suite still binds):

- `runner_core/live_transport.py` — the concrete `Transport` over the owner-local
  loopback TCP/JSON channel the merged C# `LoopbackControlServer` exposes: exact 4-byte
  framing, the `RequestHmac` canonical HMAC envelope, and per-endpoint
  `connectionGeneration` tracking (a pre-reconnect envelope is rejected server-side as
  StaleGeneration). Exercised end-to-end in `tests/test_live_transport.py` against a
  real in-process loopback socket **stub** — no Valheim, no game I/O.
- `runner_core/operator_drivers.py` — the fail-closed operator drivers: `LaneLauncher`
  (hard production-port deny, explicit readiness — no blind sleep), `DualClientLauncher`
  (two licensed identities, refuses any `valheim.x86_64` it did not launch, deterministic
  teardown), `EntitlementSeeder` (drives the product `sbpr_master` OFFER→BUY admin path
  with discriminators `CmdOffer=1`/`CmdBuy=2` — **never mints/signs/grants** entitlement),
  and `AdminlistGuard` (SHA-256 capture + byte-identical restore + loud mismatch).
- `runner_core/live_preflight.py` — the fail-closed `--live` gate: explicit opt-in **and**
  a valid disposable-lane sentinel (hard production deny list) **and** verified overlay
  pins. Any one missing/drifted refuses.

**Maturity — capability, NOT performed.** Merging this makes a live in-world run
*possible*; it does not perform one. `--dry-run` remains the **default** and is fully
working: it replays a scripted scenario through the real orchestrator against the
deterministic in-process `FakeTransport` with **no game I/O, no network I/O, and no file
mutation**. The four T022 acceptance tests remain **unobserved in-world** — actually
driving them is a separate operator-authorized step, never triggered by importing this
package or by the test suite.

```
python3 qa/runner/sbpr-qa-t022.py --dry-run                    # default: replay success (PASS)
python3 qa/runner/sbpr-qa-t022.py --dry-run --scenario crash   # replay a failure path (FAIL)
python3 qa/runner/sbpr-qa-t022.py --list-scenarios             # every scripted path
python3 qa/runner/sbpr-qa-t022.py --dry-run --json             # byte-stable evidence document
python3 qa/runner/sbpr-qa-t022.py --live \                     # fail-closed live preflight
    --lane-sentinel lane_sentinel.json --overlay-manifest manifest.json
```

Exit code encodes the verdict (0 = PASS, 1 = FAIL). A FAIL is the **expected,
correct** outcome for every non-`success` scenario — the no-false-PASS contract.

## Layout

| Path | What it is |
|------|-----------|
| `sbpr-qa-t022.py` | CLI entry point. Dry-run scenario replay; `--list-scenarios`, `--json`. |
| `fsm/` | **Adopted, unchanged.** Transport-neutral engine-free 8-phase state machine + no-false-PASS verdict core (32 pytest cases). Adapter-driven so the real M1/M4 wire contracts replace the fakes without an FSM rewrite. |
| `runner_core/` | **M5 orchestration envelope + M6-EXEC live wire.** `lease`, `manifest`, `timeouts`, `evidence`, `orchestrator` (the SOLE PASS authority, §6), `simulation` (deterministic dry-run scenarios), plus **M6-EXEC**: `live_transport` (concrete loopback TCP/JSON `Transport` to the merged C# control plane, connection-generation tracked), `operator_drivers` (lane/dual-client/entitlement-seed/adminlist guards), `live_preflight` (fail-closed `--live` gate). |
| `tests/` | pytest suite: 32 adopted FSM cases + 52 M5 runner cases + M6-EXEC live-transport / preflight / operator-driver coverage (127 total). |

## Verdict authority (ADR-0009 §6)

A runner **PASS** requires **every** one of:

- the FSM returned PASS (all four legs asserted from correlated receipts + cleanup
  confirmed — the no-false-PASS core), **and**
- the exclusive lane lease was actually held by us for the run, **and**
- the immutable 6-part artifact pins verified (present + no drift), **and**
- the evidence correlated at least the four expected receipts.

Any missing precondition forces **FAIL**. Lease-acquire failure and pin drift fail
closed **before** the scenario is driven (nothing arms unless every precondition
holds, §5.1). The helper/FSM alone can never mint a PASS — only the orchestrator can.

## Boundaries (never crossed)

- **Engine-free.** No Valheim/BepInEx/Unity dependency; runs on any box with Python 3.
- **Verdict authority.** Only this runner emits a product AT verdict; the helper
  never does (ADR-0009 §6).
- **No false PASS.** Missing any of the four T022 ATs, missing cleanup, an unheld
  lease, or drifted pins structurally blocks a PASS (enforced by `tests/`).
- **Live execution is a fail-closed CAPABILITY, not performed.** `--dry-run` is the
  default and drives nothing in-world. `--live` unlocks the live path ONLY under
  explicit opt-in + a disposable-lane sentinel + verified overlay pins; even then this
  card only *builds* the capability — launching a lane/clients, seeding entitlement, and
  observing the four T022 ATs in-world is a separate operator-authorized step (M6).
