---
title: T022 ARRANGE — STATIC phase, as implemented
status: current
last_updated: 2026-07-29
---

# T022 ARRANGE — STATIC phase, as implemented

Implementation notes for the STATIC phase of the T022 arrange spec (issue #450).

> The parent specification is `docs/qa/T022-ARRANGE-SPEC.md`, now canonical on `main`
> (#468). This document remains a sibling rather than a section inside it: the spec
> states the contract and its evidence, while this states how the shipped STATIC phase
> implements it, and the two change on different cadences. Section references below
> (§3 P1-P9, §4 STATIC, §2 I1-I12) are to that spec.

## Scope

STATIC only: the checks that can be made before anything expensive happens. No
process is started, no game is contacted, no file is written. The remaining phases —
STAGE (#451), SWEEP (#455), VERIFY (#456), and the runner cutover (#457) — are
separately owned and are NOT implemented here. The credential and join-delivery
provisioning contracts STATIC enforces declaratively landed with #452, #453 and #454.

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
| `S8-JOIN-TARGET` | target declared, is this run's lane, QA profile named, and this client's own wrapper can actually carry it | I5 |
| `S9-DISABLED-COMPONENTS` | a listener declared `null`/disabled has no deployed plugin DLL | I6, Q5 |

Preserved guards, unchanged in intent: the B2 production deny (2456 Niflheim / 2466
Heistan hold real worlds and are never a legal target — here extended to cover client
port sets and join targets, not just the lane), the byte-equality artifact pin check
that correctly refused a stale deployed launcher, and the M6-LANEPW password-policy
consistency check.

## Per-client listener contract (issue #454)

Manifest schema v2 requires every client to declare all three known listener resources
under `ports`:

| Resource | Declaration | Runtime owner |
|---|---|---|
| `loopback_control` | required integer | `SBPR.QaHarness.T022`; already 48610/48611 |
| `valbridge_gabp` | required integer | ValBridgeServer/Lib.GAB, supplied by that client's GABS daemon as `GABP_SERVER_PORT` |
| `unity_script_host` | integer or `null` | UnityScriptHost; `null` means the plugin must be absent |

The real dual-user assignment is `valbridge_gabp=49152` for client_a and `49153`
for client_b. Each per-uid GABS daemon must use a singleton custom range matching its
client declaration (`portRanges.customRanges=[{"min":49152,"max":49152}]` for uid
1000; 49153 for uid 1001) and be restarted after configuration. This is not an
arbitrary convention: GABS's default allocator is a process-local counter starting at
49152. Two independent daemons therefore both choose 49152 for their first launch,
which is exactly the live collision observed on 2026-07-29. ValBridgeServer already
reads `GABP_SERVER_PORT` and Lib.GAB already binds the supplied value; neither contains
the single-instance assumption.

UnityScriptHost is intentionally asymmetric. ADR-0009 requires the four T022 AT legs
to ride `LiveLoopbackTransport` and `AT-QA-NO-SCRIPTTOOLS-LOCK` forbids re-entry through
the UnityScriptHost/ValBridge ScriptTools surface. client_a retains USH on its declared
48210 operator port; client_b declares `unity_script_host: null`. Because the deployed
USH build has no disable switch and falls back to hardcoded 48210 whenever its DLL is
loaded, S9 enumerates the complete client plugin tree (including directory and file
symlinks) and fails loudly if any `UnityScriptHost.dll` remains. The traversal tracks
directory identities to break symlink cycles; a missing or unreadable declared plugin
tree also fails closed. Disabling means removing the plugin, not assigning it 48211 and
preserving an unnecessary listener.

## The two structural properties

**Nothing is assumed symmetric.** uid, unix user, Steam account, game root, binary
path, plugins dir, launcher kind + params, ports, credential paths, QA profile, and
join delivery are all per-client fields with no value inherited from a sibling. The
only cross-client comparisons anywhere in the implementation assert *disjointness*
(ports, credential paths, QA profiles) — never equality.

`S8` refuses a client with no join target, or one pointing somewhere other than this
run's lane.

It deliberately does **not** infer whether a launcher kind can carry `+connect`. The
first revision did, refusing `connect_argv` under `steam_applaunch` on the theory that
Steam passes no arguments. Spike #449 disproved that with a live run: the Steam
`%command%` wrapper appends the fragment after `"$@"`, it survives `run_bepinex.sh`'s
argv rotation, and the resulting kernel argv was
`valheim.x86_64 +connect 127.0.0.1:2476` — with the lane server logging client_b's own
SteamID and an in-world spawn. The check was refusing the only configuration proven to
work, which is worse than no check: it costs a debugging cycle and teaches people to
disable the checker.

The real fragility is that `run_bepinex.sh` rotates argv, so **appended** args reach
the game and **prepended** args are swallowed by Steam's wrapper chain. A manifest
cannot prove that merely from `launcher.kind`; when `launcher.wrapper_path` declares
the controlled seam, however, STATIC can inspect the wrapper text and reject the
known broken ordering. VERIFY (#456) remains responsible for proving the launched
process's actual argv.

Both clients therefore use `connect_argv`; the launcher difference is real, the
delivery difference was not.

### Join delivery is checked at the wrapper, too (#453)

Declaring a join target is only half of delivery. GABS delivers neither per-launch env
nor per-launch argv to the forked child, so the target crosses the fork in two hops:
the runner writes `SBPR_QA_CONNECT=host:port` into a per-launch sidecar at the path
*that* client's wrapper reads, and the wrapper sources it and turns it into a
`+connect host:port` argv fragment just before `exec`.

Hop one is manifest data. Hop two is a shell script a human edits, and it is the half
that silently rots. So a client may name `launcher.wrapper_path`, and when it does the
preflight reads that script and asserts the seam is present: it sources the sidecar, it
builds the fragment, and — for a launcher that passes through Steam's `%command%` chain
— the fragment is **appended after `"$@"`**.

That last one is the argv-rotation trap above, and it is the specific regression #449
warned about. `run_bepinex.sh` rotates argv on the SteamLaunch marker; appended args
survive into the game, prepended args are swallowed by Steam's wrapper command. Moving
that fragment reintroduces the original symptom exactly — a client parked at the server
list with nothing logged. The rule is applied per launcher kind, because client_a's
GABS wrapper execs the game binary directly and has no rotation, so its fragment sits
before `"$@"` and that is correct.

`tests/test_join_delivery.py::TestRealWrappers` runs the checker against the two
**actually deployed** wrappers on the host, so this is re-asserted on every suite run
rather than being a claim in a spike comment. It is verified discriminating: moving the
real wrapper's fragment makes it fail with the remedy quoted above, both in the test and
through `arrange --check`.

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
  *as the consuming uid* (#456). STATIC checks only that the DECLARED consumer is the
  client's own uid — which catches identity drift before anything is written. The old
  0600/0700 policy was structurally unreadable cross-uid; PROVISION now uses the approved
  0644/0711 throwaway-credential policy and performs an immediate read-back under that
  uid after writing (#452); see
  `T022-ARRANGE-CREDENTIAL-PROVISIONING.md`. This does not move the fact into STATIC.
* **Whether the join target actually arrives in the running client.** Also VERIFY.
  STATIC refuses only a delivery that is impossible by construction.

## Filesystem seam

The only environment contact is `StaticEnvironment.path_exists` / `hash_file` /
`read_text` / `find_named_files`, all read-only and injected.
`real_static_environment()` wires the stdlib calls; the test suite wires stubs.
Importing or unit-testing either module touches nothing at all.

### Every seam is mandatory (#467)

None of the four fields carries a default. A caller that omits one cannot be
constructed — `StaticEnvironment(...)` raises `TypeError`.

This was accepted in PR #465's independent review for `find_named_files` and then
silently undone: overlapping merges (#452 / PR #466 and #453 / PR #464) restored
source-compatible defaults so older callers and focused test stubs kept working.
#467 restores the contract and extends it to `read_text`, which carries the #453
wrapper-delivery proof and has the identical failure mode.

The defaults failed *closed* — an omitted seam returned `None`, which every check
reads as "unverifiable" — so this was never a fail-open security bypass. The defect
was diagnostic. An omitted seam and a genuinely unreadable resource produced the
same report line, so "nobody wired the full-tree proof" was indistinguishable from
"the proof ran and the tree could not be read." Since the entire point of the STATIC
phase is to name each failure precisely enough to act on, an ambiguity of that shape
is a defect in its own right.

A caller that legitimately cannot supply a seam passes an explicit stub
(`read_text=lambda _p: None`) and thereby records that choice in its own source,
where a reviewer can see it.

`TestProofSeamsAreMandatory` in `qa/runner/tests/test_arrange_static.py` pins this
both by call shape and by reading `dataclasses.fields(StaticEnvironment)` directly,
so a fourth overlapping merge cannot re-default a seam that no explicit case happens
to omit.

On the dual-user rig, the uid-1001 plugin tree is intentionally not enumerable by uid
1000. Run the real two-client preflight through the existing privileged operator seam
(for example `sudo -n python3 qa/runner/sbpr-qa-arrange.py ... --check`); an ordinary
uid-1000 run fails S9 as unreadable rather than treating `EACCES` as an empty tree.
