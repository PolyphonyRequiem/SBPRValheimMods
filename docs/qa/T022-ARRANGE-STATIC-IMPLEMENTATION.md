# T022 ARRANGE — STATIC phase, as implemented

Implementation notes for the STATIC phase of the T022 arrange spec (issue #450).

> The parent specification, `docs/qa/T022-ARRANGE-SPEC.md`, is authored on the
> `m6-lanepw-solo` branch and is not yet on `main`. This document is deliberately a
> sibling rather than a section inside it, so the two can land independently without
> a merge conflict. When the spec lands, fold this in as its §4a. Section references
> below (§3 P1-P8, §4 STATIC, §2 I1-I11) are to that spec.

## Scope

STATIC only: the checks that can be made before anything expensive happens. No
process is started, no game is contacted, no file is written. The remaining phases —
SWEEP (#451), PROVISION (#452-#454), VERIFY (#455), LAUNCH (#456), and the runner
cutover (#457) — are separately owned and are NOT implemented here.

| Piece | Where |
|---|---|
| Manifest schema (data model, no I/O) | `qa/runner/runner_core/arrange_manifest.py` |
| Static checks | `qa/runner/runner_core/arrange_static.py` |
| Entrypoint (`--check`) | `qa/runner/sbpr-qa-arrange.py` |
| Worked example | `qa/runner/examples/arrange-manifest.example.json` |
| Tests | `qa/runner/tests/test_arrange_static.py`, `tests/test_arrange_cli.py` |

## Running it

```
python3 qa/runner/sbpr-qa-arrange.py --manifest <path> --check [--json]
```

Exit codes: `0` every precondition passed · `1` at least one failed (the report names
each) · `2` the manifest could not be read at all.

Measured runtime on the real two-client manifest: **64 ms**. P5's "cheap enough to run
every time" is therefore affordable by a wide margin — the comparison is against ~10
minutes to boot two GPU clients and discover the same fact (§2 I11).

## Preconditions enforced

Each has a stable id, reported verbatim; these are what an operator greps for and are
part of the contract.

| id | Precondition | Invariant |
|---|---|---|
| `S1-MANIFEST-WELL-FORMED` | schema shape; per-client identity/roots/ports declared | A1 |
| `S2-PRODUCTION-PORT-DENY` | 2456/2466 absent from the lane, every client port, and every join target | P8 |
| `S3-ARTIFACT-PINS` | sources present and matching their pin; no deployed copy drifted | I8 |
| `S4-LANE-PASSWORD-POLICY` | lane policy consistent with every client; each credential declares its consuming uid | I4, M6-LANEPW |
| `S5-PORTS-DISJOINT` | no port claimed by two clients, or by a client and the lane | I6 |
| `S6-ARTIFACT-CATALOGUE` | every required artifact exists in the catalogue | I1 |
| `S7-DEST-UNDER-CLIENT-ROOT` | a client stages only under its OWN game root | A1 |
| `S8-JOIN-TARGET` | target declared, is this run's lane, QA profile named | I5 |

Preserved guards, unchanged in intent: the B2 production deny (2456 Niflheim / 2466
Heistan hold real worlds and are never a legal target — here extended to cover client
port sets and join targets, not just the lane), the byte-equality artifact pin check
that correctly refused a stale deployed launcher, and the M6-LANEPW password-policy
consistency check.

## The two structural properties

**Nothing is assumed symmetric.** uid, unix user, Steam account, game root, binary
path, plugins dir, launcher kind + params, ports, credential paths, QA profile, and
join delivery are all per-client fields with no value inherited from a sibling. The
only cross-client comparisons anywhere in the implementation assert *disjointness*
(ports, credential paths, QA profiles) — never equality.

`S8` refuses a client with no join target, or one pointing somewhere other than this
run's lane.

It deliberately does **not** check whether a launcher can carry `+connect`. The first
revision did, refusing `connect_argv` under `steam_applaunch` on the theory that Steam
passes no arguments. Spike #449 disproved that with a live run: the Steam `%command%`
wrapper appends the fragment after `"$@"`, it survives `run_bepinex.sh`'s argv
rotation, and the resulting kernel argv was
`valheim.x86_64 +connect 127.0.0.1:2476` — with the lane server logging client_b's own
SteamID and an in-world spawn. The check was refusing the only configuration proven to
work, which is worse than no check: it costs a debugging cycle and teaches people to
disable the checker.

The real fragility is that `run_bepinex.sh` rotates argv, so **appended** args reach
the game and **prepended** args are swallowed by Steam's wrapper chain. That lives
inside a wrapper, invisible from a manifest, so it belongs to VERIFY (#455) reading the
launched process's actual argv.

Both clients therefore use `connect_argv`; the launcher difference is real, the
delivery difference was not.

**A third client is a data change.** Every check iterates `manifest.clients`; there is
no positional `client_a` / `client_b` anywhere in the schema or the checks. Launcher
variation is a `kind` plus parameters validated against the data-driven
`LAUNCHER_KINDS` table, so a new launch mechanism is a table entry plus manifest data
rather than a branch in a consumer. An unrecognised launcher parameter is **refused**,
not ignored — silently dropping a typoed field is exactly the class of failure this
phase exists to remove. `tests/test_arrange_static.py::TestThirdClientIsDataOnly`
adds a third client with a third launcher kind against unmodified code.

## Reporting contract (P3)

Every failure is a `StaticFailure` carrying `precondition`, `client`, `detail`,
`expected`, `actual`, and `remedy`. A check that cannot fill all of those in is not
specific enough to emit — the dominant failure mode of the current system is silence,
and a check that merely returns `False` reproduces it.

Checks deliberately **do not short-circuit**: one invocation reports every problem
found, because the cost being avoided is discovering them one boot cycle at a time.
`StaticReport.as_dict()` is the machine-readable form (P7).

## Deliberately NOT checked here

* **Whether a destination file exists yet.** Staging creates it; a stager that could
  only replace and never create was itself the I3 defect. An absent destination is
  normal pre-staging state, not a failure.
* **Whether a credential is readable in fact.** That is a VERIFY-phase test performed
  *as the consuming uid* (#455). STATIC checks only that the DECLARED consumer is the
  client's own uid — which is what catches the I4 shape (written 0600 by uid 1000,
  consumed by uid 1001) before anything is written.
* **Whether the join target actually arrives in the running client.** Also VERIFY.
  STATIC refuses only a delivery that is impossible by construction.

## Filesystem seam

The only environment contact is `StaticEnvironment.path_exists` / `hash_file`, both
read-only and injected. `real_static_environment()` wires the stdlib calls; the test
suite wires a dict. Importing or unit-testing either module touches nothing at all.
