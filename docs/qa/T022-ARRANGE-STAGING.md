---
title: T022 ARRANGE — STAGE phase, unified artifact staging
status: current
last_updated: 2026-07-30
---

# T022 ARRANGE — STAGE phase, unified artifact staging

Implementation notes for issue #451 and invariants I1, I2, I3 in
[`T022-ARRANGE-SPEC.md`](T022-ARRANGE-SPEC.md).

## What this closes

`SBPR.QaHarness.T022` — the mod the entire test depends on — was absent from `client_b`
for the whole twelve-day effort. A `diff` of the two plugin directories returned exactly
one line. A client without the harness boots normally, loads every product mod, opens
its bridge port, and waits at a menu forever, emitting no error. Days were spent
debugging the launch path of a client that could never have armed.

The staging that existed was not merely buggy; it was structurally incapable of noticing:

| Defect | Shape | Invariant |
|---|---|---|
| Staged into one client only | The other client's tree was the *source*, so "both agree" was never a checked fact | I1 |
| Loop bounded by a literal count | A fourth manifest entry staged **nothing**, silently | I2 |
| Replace-only | Could not create a plugin directory, so a manifest could not introduce a new artifact at all | I3 |

The third defect is why the second mattered: adding the harness to the manifest could
never have worked, because its plugin directory did not yet exist on the second client.

## Scope and phase boundary

| Piece | Where |
|---|---|
| Staging phase | `qa/runner/runner_core/artifact_staging.py` |
| Entrypoint (`--stage`, `--stage --dry-run`) | `qa/runner/sbpr-qa-arrange.py` |
| Tests | `qa/runner/tests/test_artifact_staging.py`, `tests/test_arrange_cli.py` |

STAGE owns placing artifact bytes and proving they landed. It does **not** own:

- clearing prior-run residue — **SWEEP (#455)**, which runs *before* STAGE so staging
  never writes alongside state it is about to invalidate;
- the readiness report, credential readability, join-target presence, or port freedom —
  **VERIFY (#456)**, which consumes `assert_postconditions()` for its artifact half
  rather than reimplementing it;
- anything that starts a process. STAGE contacts no game and launches no client.

STATIC's existing artifact guards are unchanged. `S3-ARTIFACT-PINS` still refuses a
missing or drifted *source* and a *deployed* copy whose bytes differ from the pin, and
still treats a destination that does not exist yet as legal — creating it is this
phase's job.

## Running it

```
python3 qa/runner/sbpr-qa-arrange.py --manifest <path> --stage --dry-run   # reports, writes nothing
python3 qa/runner/sbpr-qa-arrange.py --manifest <path> --check --stage     # STATIC gates STAGE
python3 qa/runner/sbpr-qa-arrange.py --manifest <path> --stage --json      # machine-readable
```

Exit codes: `0` staged and postconditions passed · `1` a postcondition failed ·
`2` the manifest could not be read or no mode was selected · `3` staging failed and was
rolled back cleanly (the tree is as it was) · `4` staging failed **and** the rollback was
incomplete.

Exit `4` is deliberately distinct: every other failure leaves a known-good tree, while
`4` leaves a mixed one that a human must reconcile. It names every path it could not
restore.

Passing `--check --stage` runs STATIC first and refuses to stage if it fails. Staging on
top of a manifest whose preconditions failed would write bytes the run has already
declined to trust.

## Count-agnosticism is structural, not derived

`plan()` iterates `manifest.clients` and, within each, that client's own `artifacts`.
There is no artifact count, no index, and no bound anywhere in the module. A regression
test greps the source for reintroduced bounds.

This is a stronger property than the earlier in-place fix on the host, which derived a
count from a path list but still required the manifest's line **order** to match that
list. Iterating the parsed structure removes the concept of an artifact index entirely,
so there is no bound to get wrong and no order to get out of step.

Adding a third client is likewise data: `client_c` is another entry in `clients`, and a
test asserts it stages with no code change.

## Transaction model, and its honest boundary

Staging is planned in full before a single byte is written. Every source is resolved,
hashed, and checked against its catalogue pin first, so a source problem aborts with
nothing touched. That is the common failure and it is genuinely atomic.

Once writing begins, each entry goes through a same-directory temp file that is verified
for kind, owner, mode, and hash **before** an atomic rename — checking after the rename
would mean a bad file was, however briefly, the live artifact. Bytes displaced by a
replacement are preserved as a sibling `.sbpr-prev` file so a later failure can put them
back; the previous stager had no undo and simply returned mid-loop, leaving the tree
half-new. Preserved files are dropped only after postconditions pass.

**The boundary, stated plainly:** a rollback that must cross a uid boundary is
best-effort, not guaranteed. `client_b`'s tree is uid-1001-owned while the runner is uid
1000, so reverting it requires the same `sudo -n -u #<uid>` seam the credential
readability probes use, and that seam can itself fail. Within a single uid the rollback
is reliable. This phase does not claim atomicity it cannot deliver.

## Ownership and mode

| Object | Policy |
|---|---|
| Staged artifact | `0644`, owned by the consuming client's uid |
| Created plugin directory | `0755`, owned by the consuming client's uid |
| Parent left `0775`/`0770` by earlier tooling | Tightened to `0755` |
| Parent in any other unexpected mode | **Refused**, not repaired |

Writes are performed **as the identity that consumes them**. For `client_a` the runner is
already that uid and writes in-process; for `client_b` the write goes through
`sudo -n -u #1001`, so the file lands owned by uid 1001. The alternative — uid 1000
writing into `/home/valbot` and adjusting ownership afterwards — requires root and
dissolves the Steam identity isolation the dual-user rig depends on.

Tightening a parent is deliberately narrow. Silently "repairing" a mode nobody predicted
is how a permissions bug becomes permanent, so anything outside the allowlist produces a
diagnostic refusal instead.

## Creating a missing plugin directory (I3)

Creation is allowed in exactly one shape:

- the path must not exist at all — never adopt or "fix" something already present;
- its own parent must already exist as a real, correctly-owned directory (creation is
  one level only; it never materialises a whole tree);
- that parent must resolve **strictly under** this client's game root, never the root
  itself;
- the result is `0755`, owned by the staging identity, re-checked after creation.

A symlinked plugin directory is refused by name, because that is the shape that can
silently redirect writes outside the game root. `S7-DEST-UNDER-CLIENT-ROOT` compares
declared path strings and cannot see a symlinked intermediate; STAGE resolves realpaths
once the parent exists and refuses an escape.

## Postconditions: absence and drift are different failures

After staging, every client × every required artifact is re-read from disk. The record
of what `stage_all()` believed it did is not consulted — "the write appeared to succeed"
is precisely the belief this phase exists to stop relying on.

| id | Asserts | Why separate |
|---|---|---|
| `T1-ARTIFACT-STAGED` | The artifact is present at all | Absence means staging did not happen, or was undone |
| `T2-ARTIFACT-BYTES` | Bytes match the manifest pin | Drift means it happened and something else overwrote it |
| `T3-ARTIFACT-OWNERSHIP` | Regular non-symlink file, right uid, mode `0644` | The client may be unable to read its own artifact |

The two conditions produce the identical observable — a client at a menu — but need
different remedies, so conflating them would leave the operator to guess which. This
repeats the distinction the credential work had to unwind between "declared but absent"
and "written unreadable".

Failures are emitted as `StaticFailure` records, the same shape STATIC uses, so VERIFY
can fold them into its readiness report without a second format. Checks do not
short-circuit: one call reports every problem, because each discovery-by-boot cycle costs
about ten minutes.

## Idempotency

Re-running STAGE converges rather than churns: an artifact whose deployed bytes already
match its pin is reported `already-current` and not rewritten. Cross-run residue sweeping
belongs to #455; the temp-file conventions this phase uses are `.sbpr-stage.<pid>` and
`.sbpr-prev.<pid>`, named here so that sweep can target them.

## Verification performed

- `qa/runner` pytest suite: **455 passed** (411 pre-existing, 35 new staging cases, 9 new
  CLI staging cases).
- `docs-lint`: green, 237 docs checked.
- Real staging exercise against a throwaway tree under `/tmp`, covering: dry-run writing
  nothing; both clients receiving both artifacts at `0644` with created plugin
  directories; an idempotent re-run reporting `already-current`; the twelve-day failure
  reproduced (harness deleted from one client) and reported as `T1-ARTIFACT-STAGED`
  naming that client; drift reported as `T2-ARTIFACT-BYTES` on a *different* client in
  the same pass; and a missing source exiting `3` with nothing written.

**Verification boundary:** no graphical client was launched and no live T022 run was
performed. This proves staging, postconditions, and rollback on a real filesystem; it is
not evidence of an in-game join or a playable T022 run. Cross-uid staging is exercised
through a substituted filesystem seam rather than real `sudo`, because a test that shells
to `sudo` passes on the rig and skips in CI — which reads as green while proving nothing.
