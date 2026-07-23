---
status: current
---

# IAP-015 evidence — machine index

Machine-readable index of the IAP-015 dedicated joined-client account-identity pilot evidence.
Human companion: [README.md](README.md).

## Governing artifacts

- Preflight manifest & journey→acceptance matrix:
  [`../../runbooks/account-identity-pilot-dedicated-qa-manifest.md`](../../runbooks/account-identity-pilot-dedicated-qa-manifest.md)
- Operator runbook:
  [`../../runbooks/account-identity-pilot-operator-runbook.md`](../../runbooks/account-identity-pilot-operator-runbook.md)
- Accepted candidate: squash-merge `e1bec2d7d92f2361402ee85740e52ae6a5c5e9aa` (PR #416, `main`)
- Environment card (six-gate resolution source): `t_52f12248` (READY_FOR_EXECUTE)

## Accepted-candidate DLL pins (parity target)

| Assembly | SHA-256 |
|---|---|
| `SBPR.Trailborne.dll` | `a93cacc30d11f9f4fabf342c3c681cbc72915c94b733c9b6ff4da021157a75f5` |
| `SBPR.Trailborne.Core.dll` | `080733d27988286e7dd923ad7cf4e5e3ab71e62d328c1315d926dd8a0ad72abe` |
| `SBPR.Niflheim.HomesteadStones.dll` | `e6daaaf71265d2afbd63a28448be5fde92d5739de04f462a8da08ffabd9d3a3e` |

## Evidence documents

| File | Kind | Date | Status | Result |
|---|---|---|---|---|
| [`iap015-execute-attempt-20260723.md`](iap015-execute-attempt-20260723.md) | EXECUTE attempt 8 | 2026-07-23 | proposed | BLOCKED — crossplay/parity topology mismatch (resolved by manifest Option (a) / env card `t_52f12248`) |

## Acceptance coverage status

| Acceptance ID | Journey step | Status |
|---|---|---|
| `AT-AIP-DEDICATED-JOIN` | J1 first join + mint | pending (parity-staged fixture ready) |
| `AT-AIP-DEDICATED-RECONNECT` | J2 reconnect same ids | pending |
| `AT-AIP-DEDICATED-SECOND-PROFILE` | J3 `ForTheWort_QA2` distinct char | pending |
| `AT-AIP-DEDICATED-SECOND-SESSION-REJECT` | J4 split evidence | harness half PASS (18/18); live half pending |
| `AT-AIP-DEDICATED-RESTART` | J5 durable resolution | pending |
| `AT-AIP-DEDICATED-DISABLE` | J6 inspect/disable/reject | pending |
| `AT-AIP-OPERATOR-RUNBOOK` | J7 runbook via shipped commands | pending |
