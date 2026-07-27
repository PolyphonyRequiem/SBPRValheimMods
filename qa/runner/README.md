# qa/runner — external T022 QA runner (ADR-0009 §1, §6)

`sbpr-qa-t022.py` is the **engine-free** external runner for the T022 Masterwork
joined-client scenario. Per ADR-0009 it is the **sole scenario state machine and the
sole PASS/FAIL composer**: the BepInEx helper (`qa/SBPR.QaHarness.T022/`) emits dumb
primitive facts, and this runner correlates the server + both client receipts into
the single final evidence document. **Only the runner declares PASS/FAIL, and it
cannot PASS without all four named T022 acceptance tests asserted, cleanup confirmed,
the exclusive lane lease held, and the artifact pins verified.**

## M5 status: full runner, DRY-RUN only

M5 wraps the adopted transport-neutral FSM (`fsm/`) in the ADR operational envelope
(`runner_core/`). It is exercised ONLY against the deterministic in-process
`FakeTransport`: it performs **no game I/O, no network I/O, and no file mutation**,
mints no nonce against a live world, signs nothing on the wire, and drives no game.
A *live* two-client cold run is the separate, operator-authorized **M6** card and is
never run here.

```
python3 qa/runner/sbpr-qa-t022.py --dry-run                 # replay the success path (PASS)
python3 qa/runner/sbpr-qa-t022.py --dry-run --scenario crash  # replay a failure path (FAIL)
python3 qa/runner/sbpr-qa-t022.py --list-scenarios          # every scripted path
python3 qa/runner/sbpr-qa-t022.py --dry-run --json          # byte-stable evidence document
```

Exit code encodes the verdict (0 = PASS, 1 = FAIL). A FAIL is the **expected,
correct** outcome for every non-`success` scenario — the no-false-PASS contract.

## Layout

| Path | What it is |
|------|-----------|
| `sbpr-qa-t022.py` | CLI entry point. Dry-run scenario replay; `--list-scenarios`, `--json`. |
| `fsm/` | **Adopted, unchanged.** Transport-neutral engine-free 8-phase state machine + no-false-PASS verdict core (32 pytest cases). Adapter-driven so the real M1/M4 wire contracts replace the fakes without an FSM rewrite. |
| `runner_core/` | **M5 orchestration envelope.** `lease` (exclusive disposable-lane lease, §5.3), `manifest` (immutable 6-part artifact pins + drift, §5.1/§8), `timeouts` (per-phase budgets on the global deadline, §3.2), `evidence` (correlated byte-stable evidence document, §6), `orchestrator` (composes lease→pins→FSM→evidence→**final verdict**, the SOLE PASS authority, §6), `simulation` (deterministic dry-run scenarios). |
| `tests/` | pytest suite: 32 adopted FSM cases + 52 M5 runner cases (84 total). |

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
- **Dry-run only.** Nothing is launched, deployed, or run in-world (that is M6).
