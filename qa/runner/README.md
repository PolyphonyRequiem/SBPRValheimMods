# qa/runner — external T022 QA runner (ADR-0009 §1, §6)

`sbpr-qa-t022.py` is the **engine-free** external runner for the T022 Masterwork
joined-client scenario. Per ADR-0009 it is, once complete, the **sole scenario
state machine and the sole PASS/FAIL composer**: the BepInEx helper
(`qa/SBPR.QaHarness.T022/`) emits dumb primitive facts, and this runner correlates
the server + both client receipts into the single final evidence document. **Only
the runner declares PASS/FAIL, and it cannot PASS without all four named T022
acceptance tests asserted and cleanup confirmed.**

## M0 status: skeleton only

In this milestone the runner is a **skeleton**: argparse + `--dry-run`, and nothing
else. It performs **no game I/O, no network I/O, and no file mutation**, mints no
nonce, signs no request, and drives nothing. It is deliberately fail-closed — it
never reports a live success in M0.

```
python3 qa/runner/sbpr-qa-t022.py --dry-run
```

The scenario state machine, capability-manifest minting, per-request HMAC, receipt
correlation, evidence composition, and the runner's pytest suite land in **M5** (the
runner card). A *live* two-client cold run is the separate, operator-authorized
**M6** card — never auto-run.

## Boundaries (never crossed)

- **Engine-free.** No Valheim/BepInEx/Unity dependency; runs on any box with
  Python 3.
- **Verdict authority.** Only this runner emits a product AT verdict; the helper
  never does (ADR-0009 §6).
- **No false PASS.** Missing any of the four T022 ATs, or missing cleanup
  confirmation, structurally blocks a PASS (enforced by tests in M5).
