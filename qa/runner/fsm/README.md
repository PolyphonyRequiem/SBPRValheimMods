# qa/runner/fsm — transport-neutral T022 runner FSM core (PARALLEL PREBUILD)

Engine-free, deterministic state-machine core for the T022 Masterwork
joined-client QA scenario. Built as a **parallel prebuild** (Kanban `t_6827f187`)
so the canonical QA-M5 runner card (`t_6bb9c7d5`) can adopt a proven FSM +
PASS/FAIL composer instead of writing one under runtime pressure.

**This core performs NO game I/O, NO network I/O, and NO file mutation.** It has
no Valheim / BepInEx / socket dependency and imports only the Python stdlib. It
is the ADR-0009 §6 "runner is the brain, helper emits dumb primitives" contract,
realized against a fake transport.

## What's here

| File | Role |
|---|---|
| `fsm/fsm.py` | `T022Runner` — the phase machine + verdict composer. |
| `fsm/schema.py` | `ActionRequest`, `Receipt`, `Manifest`, `RunContext`, `ReceiptAdapter` (the adapter seam). |
| `fsm/transport.py` | `Transport` protocol + `FakeTransport` (deterministic, scriptable). |
| `fsm/result.py` | `RunResult` — compact, byte-stable JSON verdict object. |
| `fsm/errors.py` | Typed failure taxonomy (one class per fail-closed reason). |
| `tests/` | 32 pytest cases; the no-false-PASS proof. |

## Phases (ADR-0009 §Decision)

```
preflight → fixture → ISSUE → UPGRADE → TRANSFER → TAMPER → evidence → cleanup
```

- **One attempt, one deadline.** No internal retry. Crossing the manifest expiry
  at any checkpoint FAILs (timeout).
- **Cleanup always runs** (`finally`), even on crash/timeout. A failed run sets
  `evidence_preserved=True`.

## The no-false-PASS invariant

`verdict == "PASS"` requires **all four legs** (ISSUE/UPGRADE/TRANSFER/TAMPER)
asserted from correlated receipts **AND** cleanup confirmed. Every one of these
forces FAIL and is proven by a dedicated test:

missing · reordered · duplicate · stale (wrong connection generation) ·
tampered receipt body · forged/foreign integrity key · `reject`/`error` outcome ·
per-leg AT-assertion failure · timeout (before start and mid-run) · peer crash ·
cleanup failure · identity collision · missing artifact pin · artifact drift ·
competing lane lease.

The suite is organized as: one `golden` baseline that PASSes, then each test
perturbs exactly one thing and asserts the verdict flips to FAIL with the
expected `failure_kind`. A green suite therefore *is* the soundness proof.

## Receipt correlation

Receipts correlate on the four-part key `(run_nonce, request_id, actor,
conn_gen)` **plus** a strictly-monotonic per-run `seq` **plus** an HMAC integrity
tag that binds the key and the observed body. Any divergence →
`ReceiptCorrelationError`, never a soft pass.

## Run the tests

```bash
cd qa/runner
python3 -m pytest tests/ -q      # 32 passed
```

No third-party deps beyond `pytest`. `conftest.py` puts `qa/runner` on
`sys.path`; no install step.

## Integration notes for the canonical M5 card (`t_6bb9c7d5`)

This core is deliberately adapter-driven so the **final M1/M4 wire contracts drop
in behind the same FSM** with no state-machine rewrite:

1. **Transport.** Implement `fsm.Transport` (`now`/`send`/`cleanup`) over the real
   loopback-TCP/JSON client channel + per-peer ZRpc server channel. `send` returns
   the raw receipt payloads; `cleanup` raises on non-confirmation. Substitute it
   for `FakeTransport`; the FSM is unchanged.
2. **ReceiptAdapter.** Pass `ReceiptAdapter(parse=...)` that validates bytes
   against `qa/contracts/receipt.schema.json` and returns a `fsm.Receipt`. The
   default identity adapter is fake-only.
3. **Scenario.** The default four-leg scenario lives in `fsm.fsm._default_scenario`.
   For M5 replace it with steps loaded from `qa/scenarios/t022.json`, passing the
   step list as `T022Runner(..., scenario=[...])`. The observation validators are
   plain callables — point them at the real tooltip/field-key observations.
4. **Integrity key.** The fake uses a static key; wire the real per-run HMAC key
   from the arming manifest into `integrity_key=`.
5. **Verdict authority preserved.** `RunResult` is the only thing that emits
   PASS/FAIL, matching ADR-0009 §6. Feed `RunResult.to_json()` into the evidence
   doc; it is byte-stable (`sort_keys`) for hashing.

**Scope boundary (unchanged by this prebuild):** no live execution, no packaging,
no PR/merge. `AT-QA-T022-COLD-30MIN` remains a timing model; M6 live qualification
is a separate operator-authorized card.
