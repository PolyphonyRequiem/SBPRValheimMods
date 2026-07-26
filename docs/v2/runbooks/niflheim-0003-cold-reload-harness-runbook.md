---
title: "Niflheim Wayfinder 0003 — cold-reload capture harness runbook"
status: proposed
purpose: >
  Executable runbook for the QA-only live cold-reload capture harness that supplies the primitive
  facts the Wayfinder 0003 reload identity/count gate (kanban t_1a1164f4) needs. Names the mechanical
  sequence — validate disposable fixture + exact UID, capture PRE from the production selector path,
  request + verify a REAL world save, terminate the entire graphical client, prove the old PID/session
  is gone, cold-launch the same disposable world, capture POST, compare fail-closed, cleanup. It is
  fail-closed on lease/rollback/fixture/production-target/bounded-wait and NEVER touches production
  Niflheim/Heistan. Building, compiling, testing, or registering this harness does NOT prove live
  reload, persistence, deployment, or playability — only a real OPERATE-provisioned live run does.
---

# Cold-reload capture harness runbook — Niflheim Wayfinder 0003

**Responsible operator:** the named QA/ops owner who provisions the disposable Astley window.
**Governing policy:** the Wayfinder 0003 ticket `niflheim/wayfinder/tickets/0003-location-placement-rules.md`
(in the sibling `niflheim` repo, outside this MIT repo); its reload identity/count gate (lines 113–119,
acceptance ledger lines 178–191) is the sole remaining 0003 gate and is automation-owned — no further
Daniel gate.

## Honesty preface (load-bearing)

- **Logs green ≠ playable, and a built harness ≠ a proven reload.** This harness only *captures* and
  *compares* primitive facts. Compiling it, registering it, and passing its deterministic tests proves
  the harness *logic and configuration reachability* are correct — the committed controller can now
  configure and arm the committed observer fail-closed (previously the observer's manifest/output-dir/
  provenance/save-receipt inputs had no committed writers, so it always refused). It does **not** prove
  that a joined client cold-reloaded a real saved world with a stable assignment set. That proof
  requires the live `--run` sequence below, executed in a real OPERATE-provisioned window.
- **Residual prerequisites before a live `--run` (BOTH still open, neither a human gate):**
  1. The disposable Astley `.db/.fwl` fixture at UID `2413287143` does not yet exist in the repo — an
     OPERATE environment prerequisite, not permission to fabricate a world.
  2. An OPERATE-provisioned graphical Valheim client window bound to that disposable world (the
     `VALHEIM_CLIENT_CMD` target), with a real lease, rollback bytes, and the build-provenance hashes
     the manifest now requires. No such live window is provisioned here; this IMPLEMENT is
     static/build-harness only and asserts no live verdict.
- **Forbidden evidence.** An in-process reload, copied state, engine-free selector rerun, warm scene
  transition, or same-process serialization round-trip is NOT reload proof. The PRE and POST captures
  MUST come from two different OS processes / sessions / boot generations, enforced structurally by the
  shipped `HomesteadReloadComparer`.

## What ships (committed, rebuildable)

| Component | Path | Role |
|---|---|---|
| Capture core | `src/SBPR.Niflheim.HomesteadStones/Domain/ReloadHarness/HomesteadReloadCapture.cs` | Runs the SHIPPED `HomesteadSelector.Select`, emits bounded primitive facts, scrubs secrets, fails closed |
| Fail-closed comparator | `src/…/Domain/ReloadHarness/HomesteadReloadComparer.cs` | PRE/POST identity + count comparison; rejects wrong UID, same process/session, missing save receipt, hash/count/host drift |
| Arming gate | `src/…/Domain/ReloadHarness/HomesteadReloadArmingGate.cs` | QA-only enablement + lease/rollback/fixture/production-guard/bounded-wait refusals |
| Configuration ingress | `src/…/Domain/ReloadHarness/HomesteadReloadConfigurationIngress.cs` | The ONE default-off writer that binds the controller's `NIFLHEIM_RELOAD_HARNESS_*` env contract into the observer's manifest/output-dir/provenance/save-receipt/phase, fail-closed, armed against the source-fixed fixture UID |
| Live capture observer (net48) | `src/…/Features/ReloadHarness/HomesteadReloadCaptureObserver.cs` | Harmony `ZoneSystem.Start` hook; binds config via the ingress then arms only when enabled + gate-approved; writes one boot's capture |
| Registration + conformance | `Plugin.cs` (`PatchAll` + `HomesteadReloadHarnessConformance.Verify`) | Welds the observer into the real client; LOUD boot error if registration is dropped |
| Controller / runbook | `tools/niflheim-homestead-reload-harness/controller.sh` | Mechanical PRE→save→exit→cold-reload→POST→compare sequence with `--dry-run` refusal |
| Manifest template | `tools/niflheim-homestead-reload-harness/manifest.example.env` | OPERATE-staged fixture/lease/rollback inputs |

## Source-fixed fixture values (from accepted material — do not re-open)

- Astley disposable world UID: `2413287143`
- Expected live-enumerated candidates / assigned: `285 / 114`
- Expected minimum pairwise distance: about `128.591 m`
- Compare exact per-host `(prefab, zoneX, zoneZ)` identity set and stale assignment-ZDO removal.

## Preconditions (all fail-closed)

The controller and the in-client arming gate BOTH refuse unless every item holds:

1. QA-only enablement flag `ReloadHarness.EnableColdReloadCaptureHarness` is `true` (default `false`).
2. An OPERATE-supplied QA manifest naming: lease id, rollback bytes, disposable `.db` + `.fwl`,
   `WORLD_UID == 2413287143`, a non-production target name/port.
3. Target world name contains neither `niflheim` nor `heistan`; port is not `2456/2457/2466/2467`.
4. Bounded, finite, positive waits; at most one readiness retry.
5. Complete build provenance (`PROV_SOURCE_HASH`/`PROV_PRODUCT_HASH`/`PROV_HARNESS_HASH`) and, on the
   POST boot only, a real save receipt (`SAVE_PRESENT=true` with a saved-db hash + timestamp).

The controller exports the full `NIFLHEIM_RELOAD_HARNESS_*` environment contract that the in-client
`HomesteadReloadConfigurationIngress` reads and validates fail-closed. Two residual OPERATE
prerequisites remain before a live `--run`: the disposable Astley `.db/.fwl` fixture does not yet exist
in the repo, and no OPERATE-provisioned graphical client window is available. Both are environment
prerequisites — report them, do not fabricate a world or a window.

## Dry-run (safe, launches nothing)

```
tools/niflheim-homestead-reload-harness/controller.sh --dry-run --manifest <manifest.env>
```

Validates every precondition and exits `0` on success or `3` with an exact refusal. It never launches
Valheim and never touches a world. Use this to prove the fixture/manifest is well-formed before a window.

## Live sequence (OPERATE only, `--run`)

```
tools/niflheim-homestead-reload-harness/controller.sh --run --manifest <manifest.env>
```

Phases, each with an explicit receipt and a fail-closed abort:

1. **PRE capture** — boot 1 launches the graphical client bound to the disposable world; the observer
   runs the production selector path and writes `homestead-reload-capture-pre.txt`.
2. **Save + verify** — request a REAL world save and wait (bounded) for its receipt; no receipt ⇒ abort.
3. **Full client exit** — terminate the entire graphical process and PROVE the old PID is gone.
4. **Cold reload** — boot 2 launches a FRESH process against the SAME disposable world; the observer
   writes `homestead-reload-capture-post.txt`.
5. **Teardown** — terminate boot 2, prove its PID is gone, release the lease.
6. **Compare** — hand both captures to `HomesteadReloadComparer` for the fail-closed identity/count
   verdict. The controller asserts no verdict itself.

## Cleanup / safety

- Production Niflheim/Heistan servers are never targeted or mutated.
- The lease is exclusive; rollback bytes restore the disposable world on any abort.
- Captures redact secrets and personal/provider identity by construction (the capture builder rejects
  any secret-bearing field).

## What this runbook explicitly does NOT claim

It does not claim the reload gate is met. A PASS from `HomesteadReloadComparer` over two genuinely
cold-separated captures, produced in a real OPERATE window against the real disposable Astley fixture,
is what a later EXECUTE card records as evidence for `t_1a1164f4`.
