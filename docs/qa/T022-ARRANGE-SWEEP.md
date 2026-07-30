---
title: T022 ARRANGE — SWEEP phase, entry sweep and idempotency
status: current
last_updated: 2026-07-30
---

# T022 ARRANGE — SWEEP phase, entry sweep and idempotency

Implementation notes for issue #455 and invariants I10, P6 in
[`T022-ARRANGE-SPEC.md`](T022-ARRANGE-SPEC.md).

## What this closes

Teardown ran only on the runner's graceful exit paths. Every time a run was killed to
stop it burning boot attempts — which was most of them — it left **live credentials on
disk**, observed with expiry ~113 minutes in the future. Launch-env sidecars accumulated
with nothing that would ever remove them. The guarantee the design claimed was
cleanup-guaranteed; what existed was cleanup-on-graceful-exit.

There was also no fact on disk that could distinguish *our residue* from *an operator's
file at the same path*. A sweeper without that fact can only guess, and a guessing
sweeper that deletes credentials is worse than no sweeper at all. Closing I10 therefore
required two things, not one: the sweep, and the ownership stamp that makes the sweep
decidable.

## What this phase honestly guarantees

The original acceptance criterion read *"credentials cannot outlive the run that minted
them, even when that run is SIGKILLed"*. That is not achievable in-process and claiming
it would be exactly the "logs green ≠ playable" dishonesty `AGENTS.md` prohibits: SIGKILL
runs no handler, no `atexit`, no `finally`. Three distinct mechanisms were being blurred
into one promise. Named separately:

| # | Mechanism | Status | What it bounds |
|---|---|---|---|
| 1 | Graceful teardown | Exists (`live_composition`) | The exit paths the runner actually reaches |
| 2 | **Next-entry sweep** | **This phase** | No credential from a prior run survives *into the next run* |
| 3 | TTL | Exists (`wire_mint`), enforced at the C# arm gate | A bootstrap doc is cryptographically inert past `expiry_unix_ms`, swept or not |

**Next-entry is the strongest guarantee #455 delivers.** It does not bound the residue
window *between* runs.

**The gap this phase does not close.** Lane-password files carry **no TTL at all**
(`lane_password_provision` writes a bare password; the C# hook reads and trims the whole
file). Their validity ends only with lane teardown. That is the real "~113 minutes"
exposure, and bounding it requires shortening the disposable lane's lifetime — **#457's
scope**, deliberately not smuggled in here. A supervising watchdog would bound it without
an intervening `arrange`, but that needs a supervision-topology decision.

Consequently SWEEP removes a lane password because *its run is over*, never because it
"expired", and the report says which. `CredentialProvenance.expiry_unix_ms` is `Optional`
for exactly this reason: recording `None` rather than inventing an expiry is what keeps
the phase honest.

The issue's other criterion, *"stale provenance receipts"*, named an artifact class that
did not exist on disk — grepping `provenance|receipt` across `qa/` returned only in-memory
C# RPC `Receipt` objects and comments describing the PID marker. It is redefined as the
ownership-provenance sidecars this phase introduces, so the criterion is checkable. Both
rewordings are recorded on issue #455.

## Convergent, not remembered

SWEEP is a pure function of the manifest: **reconcile every declared path to absent**. It
is deliberately *not* "delete what I remember writing" — in-process tracking is precisely
what fails when the process doing the remembering was killed.

Every action is *remove if present and provably ours, else record why not*. Nothing is
ever "remove, and error if missing". So a second run over an already-swept tree yields
`ok=True` with every action `already-absent` and an `as_dict()` identical to the first.
That is the idempotency criterion, and it is asserted byte-for-byte in
`tests/test_arrange_sweep.py` and end to end through the CLI in `tests/test_arrange_cli.py`
— not merely claimed.

SWEEP never **writes** a file. It unlinks and it signals; those are its only two mutations.

## Ownership provenance — the `.sbprqa` sidecar

Every credential the runner writes now gets a companion file at `<path>.sbprqa`, written
with the same atomic, symlink-refusing, mode-0644-in-a-0711-directory discipline as the
credential itself:

```json
{
  "kind": "sbpr-qa-credential-provenance",
  "version": 1,
  "run_id": "t022-<unix-ms>-<random>",
  "actor": "client_a",
  "credential_path": "/run/sbpr-qa/a/bootstrap.json",
  "minted_unix_ms": 1785187338221,
  "expiry_unix_ms": 1785190938221
}
```

It carries **no secret** — run id, actor, path, timestamps. A sidecar that leaked the
credential would double the exposure this ticket exists to reduce.

**Why a sidecar and not an embedded field.** For the lane password the answer is forced:
the C# hook reads and trims the *whole file* as the password, so any metadata inside it
would become part of the password. For the bootstrap doc a field would work —
`ArmBootstrapParser.Parse` reads named keys and ignores unknown members (verified by
reading `qa/SBPR.QaHarness.T022/ControlPlane/ArmBootstrapParser.cs`) — and the sidecar is
used there anyway, deliberately: one mechanism means the sweeper has one code path and one
failure mode, adding a third credential kind needs no new sweep logic, and a cleanup
feature never alters a format that crosses a language boundary into a fail-closed arming
gate.

Launch-env sidecars need no companion: `SBPR_QA_HARNESS_INSTANCE` is already written into
every one of them, and the marker *is* the provenance.

## The decision table (C2) — fail closed, always toward leaving things alone

Evaluated per declared credential path. Every ambiguity resolves to "do not touch it, and
say so".

| Observed at the declared path | Outcome | `ok` |
|---|---|---|
| Absent | `already-absent` | pass |
| Path is a symlink (`lstat`) | `refused` — never followed, never removed | **fail** |
| Owned by a uid that is neither the arranging uid nor the credential's declared `consumer_uid` | `left-alone` | **fail** |
| No / unreadable / unparseable provenance | `left-alone` | **fail** |
| Provenance names **this** run | `removed` (makes a repeated arrange converge) | pass |
| Provenance names a prior run, **expired** | `removed` | pass |
| Provenance names a different run, unexpired *or no TTL* | `left-alone` | **fail** |

Owner uid is checked **before** provenance on purpose: provenance is attacker-writable
content at a path we do not own, so it must not be able to talk the sweeper into a delete.

**Two owners are legitimate, not one.** A credential is written either by the arranging
runner in-process, or *into the consuming identity's tree as that identity* through #451's
`as_uid` staging — uid 1000 writing into `/home/valbot` and chowning the result would need
root and would dissolve the Steam identity isolation the dual-user rig depends on. So a
uid-1001-owned credential at a path the manifest declares with `consumer_uid: 1001` is
**ours**. Any third uid is genuinely foreign and still refused.

Correspondingly, `unlink(path, as_uid)` takes the identity **per call** and acts as the
file's own owner: uid 1000 cannot unlink from valbot's 0711 directory at all, because
unlink is governed by DIRECTORY write permission and a permissive file mode cannot rescue
it.

Both halves of that were originally wrong — the check admitted only the arranging uid and
the unlink seam was bound to one identity at construction — which made the cross-uid path
**unreachable in production** while the report still emitted a tidy `left-alone`. No stub
test caught it; it was found by running the phase against a real valbot-owned directory
with the production arranging uid. See the verification boundary below.

A concurrent run's live credential is arbitrated by the lane lease (`lease.py`), not by the
sweeper. `ok` answers "did the tree reach the state the manifest declares", so a refusal is
a phase failure even though it is the correct action.

The sidecar is removed with the credential it describes, kept when that credential was
left behind (deleting it would destroy the only evidence a later sweep could use), and
removed on its own when orphaned.

## B1 — marker-only process safety

**This is the highest-stakes rule in the phase, because SWEEP kills processes.** A client
with no harness marker is left strictly alone.

1. Read `/proc/<pid>/exe`; skip unless the basename is `valheim.x86_64`. Checked against
   the kernel's exe link, never argv, which is wrapper- and attacker-controlled.
2. Read `/proc/<pid>/environ` for `SBPR_QA_HARNESS_INSTANCE`. **`None` ⇒ `left-alone`,
   unconditionally.** This covers EACCES on a uid-1001 process: an unreadable environ is
   **not** proof of ownership. There is deliberately **no fallback heuristic** — no cmdline
   match, no cwd match, no `pkill -f`, no game-root prefix. Every one of those matches
   Daniel's own Steam Valheim, which runs the same binary from a similar root.
3. Marker present but naming a different run ⇒ `left-alone` (a concurrent harness run's
   client is not ours to kill). A pre-#455 marker with no run id is likewise unattributable
   and left alone.
4. Owned ⇒ SIGTERM → poll → SIGKILL, **re-verifying marker + start-ticks immediately
   before every signal**. `/proc/<pid>/stat` field 22 defeats PID reuse: a recycled PID has
   a different start time, and either mismatch aborts before the signal is sent.
5. An unenumerable process table degrades to a credential-only sweep and **says so** with
   `ok=False`. "I could not look" is never reported as "nothing was there".

**Zombies are report-only.** A `<defunct>` child exposes no readable exe link, cannot be
killed, and must be reaped by its parent. GABS's reaping was fixed upstream in our fork
(`b679943`, `79e1779`, verified in production — see `AGENTS.md`), so SWEEP issues **no GABS
call** and does not touch `_reset_gabs_state`, which stays where it is as defence in depth.
`SweepEnvironment` has no GABS seam at all, which is the structural form of that promise.

### The run-stamped marker

The marker was `<actor>:<random>` — interpretable only by the process that launched it. A
run killed with SIGKILL therefore left clients that **no later sweep could attribute**,
which would have made the process half of this phase decorative. It is now
`<run_id>:<actor>:<random>`, minted through `proc_provenance.mint_marker`, the single
authority on the format. `run_id` is required on `ClientSpec` for the real GABS launch path
(fail closed in `build_request`) and minted per-run in `build_live_run`, alongside the wire
envelope and for the same reason: a persisted run id would be reused, and every run would
then claim its predecessor's residue as its own.

## Manifest schema

`run_id` is a **required** top-level field and `MANIFEST_VERSION` moved 2 → 3. The bump is
what makes an older manifest a *named refusal* rather than one that parses and then sweeps
with an empty identity — matching nothing, removing nothing, and reporting a clean tree.

## Proof seams are mandatory (P9)

`SweepEnvironment` carries **no field with a default**, and `arrange_sweep` defaults
neither the environment nor the arranging uid. For a phase that removes files and signals
processes, the decision to touch *this* machine as *this* identity must be written down
where a human reviews it.

The contract is enforced structurally — a `dataclasses.fields` assertion plus an AST walk
over every construction site in the repository — not per-seam, so a future merge cannot
quietly re-default a seam that every current test happens to supply. That failure mode has
now recurred four times (#454 → #452/#453 → #467 → #473); the AST scan additionally asserts
it walked a real tree and found real construction sites, so a broken root or an over-eager
filter cannot make the guard pass by matching nothing.

## CLI

```bash
python3 qa/runner/sbpr-qa-arrange.py --manifest <path> --sweep [--json]
```

`--sweep` runs **before** `--check` and `--stage` when combined: stale residue can satisfy
a static check (a stale credential at a declared path *is* a present file), and staging
alongside state about to be invalidated wastes the proof. The entrypoint's docstring
previously claimed the program can never mutate a file; `--sweep` makes that false and it
was corrected in the same change.

Exit codes are unchanged: `0` pass, `1` something could not be reconciled, `2` the manifest
could not be read. An unparseable manifest exits `2` having touched nothing — the parse
happens before any environment is wired, so there is no window in which a malformed
manifest could remove a file or signal a process.

## Verification boundary

**Proved by real execution**, through the real seams:

- **The cross-uid unlink, both directions, against a real valbot-owned (uid 1001) 0711
  directory, with the production arranging uid (`os.geteuid()` = 1000).** Plain
  in-process unlink as uid 1000 fails with `PermissionError errno=13`, so the `as_uid`
  seam is demonstrably doing the work rather than the file merely being permissive; the
  same unlink through `real_staging_filesystem(as_uid=1001)` succeeds. A full sweep over
  that tree removed the owned credential and its sidecar while leaving a foreign-run
  credential and an unmarked file alone, with report and filesystem asserted to agree.
  The rig was deliberately **not** placed under `/tmp/pytest-of-*` (mode 0700), which a
  foreign uid cannot traverse — that rig defect silently broke an earlier probe.
- An expired prior-run credential and its sidecar removed; a second run converged; two
  `--json` runs a second apart were byte-identical.
- **B1 against three real live processes** named `valheim.x86_64` (a copy of `/bin/sleep`,
  never the game): the unmarked one and a foreign-run one both survived with named
  reasons; only the one carrying this run's marker was terminated. All reaped afterward.

**Proved by unit test against stubs:** the fault-injection suite in
`tests/test_arrange_sweep.py` — the decision table, TOCTOU/PID reuse, zombie handling,
partial failures, and multi-client manifests.

**A warning this phase earned the hard way.** The first cross-uid probe *passed* — because
it was invoked with `arranging_uid=1001`, which is not what the CLI does. Re-running with
the production value exposed a dead code path immediately. A probe that does not use
production values can confirm a dead path as working, and the resulting report is
indistinguishable from a healthy one. Anything claiming to verify this phase must use the
values production uses.

## Related

- [`T022-ARRANGE-SPEC.md`](T022-ARRANGE-SPEC.md) — the canonical spec; §2 I10, §3 P6, P9.
- [`T022-ARRANGE-STAGING.md`](T022-ARRANGE-STAGING.md) — STAGE, whose `StagingFilesystem`
  seam SWEEP reuses rather than duplicating.
- [`T022-ARRANGE-CREDENTIAL-PROVISIONING.md`](T022-ARRANGE-CREDENTIAL-PROVISIONING.md) —
  the 0711/0644 cross-uid policy the sidecars follow.
