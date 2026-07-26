# Niflheim 0003 — cold-reload capture harness (tools)

QA-only controller + manifest for the Wayfinder 0003 live cold-reload capture harness.

- `controller.sh` — mechanically executable runbook: validate disposable fixture + exact UID →
  capture PRE from the production selector path → request + verify a REAL world save → terminate the
  entire graphical client → prove the old PID is gone → cold-launch the same disposable world →
  capture POST → hand both to the shipped fail-closed comparator → cleanup. Supports `--dry-run`
  (validate + refuse, launches nothing) and `--run` (OPERATE only).
- `manifest.example.env` — template for the OPERATE-staged fixture/lease/rollback inputs.

The engine-free brain lives in the shipping mod at
`src/SBPR.Niflheim.HomesteadStones/Domain/ReloadHarness/` (capture, comparator, arming gate, and the
`HomesteadReloadConfigurationIngress` that binds the controller's `NIFLHEIM_RELOAD_HARNESS_*` env
contract into the observer's inputs fail-closed) and the net48 in-client observer at
`src/SBPR.Niflheim.HomesteadStones/Features/ReloadHarness/`. The full runbook, honesty preface, and
source-fixed fixture values are in
`docs/v2/runbooks/niflheim-0003-cold-reload-harness-runbook.md`.

**This does not prove reload/persistence/playability.** Building, testing, registering, and dry-running
the harness proves the *logic and configuration reachability* are correct — the committed controller
can configure and arm the committed observer fail-closed. Only a real OPERATE-provisioned `--run`
against the disposable Astley `.db/.fwl` fixture (which does not yet exist in the repo) in a provisioned
graphical client window, compared fail-closed, produces reload identity/count evidence for
kanban `t_1a1164f4`. It never targets production Niflheim/Heistan.

## Quick check

```
./controller.sh --dry-run --manifest manifest.example.env   # exits 3 with a refusal (absent fixture)
```
