# qa/ — SBPR QA test-harness subsystem (ADR-0009)

This tree holds the **QA-only** T022 fixture/action harness authorized by
[ADR-0009](../docs/decisions/0009-qa-harness-separate-fail-closed-mod.md). It is a
separate, fail-closed subsystem kept **outside** the product assemblies (`src/`)
and **excluded from the shipped modpack** — see the ADR for the full design and
trust boundary.

## Layout

| Path | What it is | Milestone |
|------|-----------|-----------|
| `SBPR.QaHarness.T022/` | Fail-closed BepInEx helper (net48). **M0: inert skeleton** — logs a disarmed banner and returns; no verbs/hooks/channels/mutation. | M0 → M4 |
| `contracts/` | JSON Schema wire truth (request/receipt/envelope). **M0: disabled placeholders.** | M0 → M2 |
| `runner/` | Engine-free external Python runner — the sole scenario state machine + PASS/FAIL composer. **M0: skeleton (`--dry-run`).** | M0 → M5 |
| `tests/` | M0 isolation guard tests (`AT-QA-NO-PRODUCT-REF`, `AT-QA-MODPACK-EXCLUDES-HARNESS`). | M0+ |

## Firewall (load-bearing — do not undo without a new ADR)

- **No product dependency, either direction.** `qa/**` references no `src/SBPR.*`
  product project/DLL, and no product project references `qa/**`. Enforced
  structurally by `scripts/check-qa-dependency-boundary.py` (gate
  `AT-QA-NO-PRODUCT-REF`).
- **Never in the product modpack.** `scripts/pack-modpack.sh` is an explicit
  allowlist that never globs `qa/**`, and asserts post-stage that no
  `SBPR.QaHarness*` artifact is present; `scripts/check-modpack-excludes-harness.py`
  re-verifies the staged tree and built zip, resistant to case-fold / rename /
  nested-path / path-traversal evasion (gate `AT-QA-MODPACK-EXCLUDES-HARNESS`).
- **Fail-closed.** The helper is default-disabled and, in M0, cannot arm at all.
  Arming (exact world UID **and** name, hard production deny list, nonce/HMAC/
  capability manifest) lands in M1.
- **The harness never fabricates product state and never emits a product verdict.**
  Only the external runner declares PASS/FAIL, and only after all four T022 ATs
  plus cleanup are confirmed (ADR-0009 §4, §6).

## Running the M0 guards locally

```
python3 scripts/check-qa-dependency-boundary.py
python3 -m unittest discover -s qa/tests -p 'test_*.py'
python3 qa/runner/sbpr-qa-t022.py --dry-run
```
