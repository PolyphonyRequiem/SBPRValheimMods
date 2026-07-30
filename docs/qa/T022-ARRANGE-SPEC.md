---
title: T022 ARRANGE — specification
status: current
last_updated: 2026-07-29
---

# T022 ARRANGE — specification

**Scope:** everything that must be true *before* the first AT leg runs. Arrange only.
Act (the four legs) and Assert (evidence/verdict) are explicitly out of scope.

**Status:** current. This is the canonical arrange specification and the reference that
issues #451, #455, #456 and #457 cite.

**Provenance and reconciliation.** An earlier draft of this document was authored on
branch `m6-lanepw-solo` (`2165d49`) and never landed on `main`, so for a period the open
implementation issues cited a source no worker could read from `main` — contrary to the
repository's spec-first rule (see `AGENTS.md`). That draft has **not** been copied
verbatim: it predated several decisions that changed its content materially. This
revision reconciles it against what has since been decided and merged (#468). What
changed from the draft is recorded in [§7](#7-what-changed-from-the-branch-only-draft),
because a spec that silently rewrites its own history is not auditable.

---

## 0. Why this exists

Twelve days produced zero executed acceptance tests. Not one failure was in the test
logic; every one was in arrangement. The arrange phase was originally spread across
**four independent mechanisms that did not know about each other**:

| Concern | Owner at the time | Aware of the others? |
|---|---|---|
| client_a's mods | `scripts/pack-qa-overlay.py` | no |
| client_b's mods | `deploy/valbot-artifact.manifest` + isolation lib | no — did not know the QA harness existed |
| Credentials (bootstrap, lane password) | Python runner, at boot | no — wrote them 0600 as the wrong uid |
| Launch env / connect target | sidecar writer + two different launchers | partially |

The dominant failure mode is **silence**. Missing plugin, unreadable credential, missing
`+connect` — none raise an error. All three produce the *identical* observable: a client
sitting at a menu. One symptom, many causes, ~10 minutes per diagnosis cycle.

**Design consequence:** the arrange phase must make every precondition *explicit and
checked*, and must fail loudly and specifically. A silent partial arrangement is the
single most expensive thing this system can do.

Collapsing those four mechanisms into one is the job of #451 (staging) and #457
(cutover). Until #457 contracts, they still coexist.

---

## 1. Topology: the concurrent dual-user rig

**This is a settled decision, not an open question.** Issue #461 chose **option B** on
2026-07-29: T022 runs on the **dual-user rig** — two Unix users and two Steam accounts,
both clients **concurrent**, on the single Requiem Prime U box. There is one machine; the
earlier phrase "dual-client rig" was retired because it read as two machines.

The conjunction that option B rested on has since been **proven live** (#461, 14:29–14:35
PDT 2026-07-29): both clients concurrently in-world on the disposable lane with the QA
harness loaded on both, evidenced from the **server** log —

```
14:29:47  Got connection SteamID 76561197965627562   <- client_a, uid 1000
14:30:04  Got character ZDOID from sbpr_qa_join      <- client_a in-world
14:33:02  Got connection SteamID 76561198671522196   <- client_b, uid 1001
14:33:14  Got character ZDOID from sbpr_qa_join_b    <- client_b in-world
```

Both processes were live simultaneously. Both loaded `SBPR QA Harness T022 0.2.0` and
armed. The fallback trigger did not fire.

**What this decision does and does not claim.** Spike #448 established that TRANSFER's
verdict is a pure function of the item's signed bytes — `RPC_ValidationRequest` never
resolves the requesting peer — so a second Steam account is **not required by the
product**. That finding stands and is not reopened here. Option B is a choice about the
topology we run T022 *on*, chosen because it is the only topology that can also observe
account-scoped concurrent admission and ownership contention. If the rig is later
retired, T022 does not need re-proving.

**Consequence for this spec:** every invariant below is written for two *concurrent*
clients. Nothing here may be simplified on the assumption that only one client is live at
a time.

### 1.1 The asymmetry that causes almost everything

The two clients are **not** two instances of one thing. They differ along every axis:

| | client_a | client_b |
|---|---|---|
| uid | 1000 (`polyphonyrequiem`) | 1001 (`valbot`) |
| Steam account | 76561197965627562 | 76561198671522196 |
| Game root | `~/.local/share/Trailborne/Valheim-Modded` | `/home/valbot/.steam/.../common/Valheim` |
| Launched by | GABS `games_start` (DirectPath) | Steam `-applaunch 892970` via a controller chain |
| Join target delivery | `+connect` on the GABS wrapper's argv | `+connect` appended by the Steam `%command%` wrapper (#449) |
| Env delivery | inherited from GABS fork | `env -i` scrub, then sidecar re-injection |
| Mod provisioning | QA overlay packer | artifact manifest + isolation staging |
| GABS daemon | uid 1000, `:8080` | uid 1001, `:8081` |

Every layer was written for one client, then had a second bolted on. The uid split is not
a first-class concept anywhere — it was rediscovered painfully at each layer.

**Requirement A1.** Arrange MUST treat "a client" as a parameterised thing with a
per-client identity, game root, launch mechanism, port set, and credential paths. No step
may assume same-uid, same-path, or same-launcher.

**Requirement A2 (daemon topology).** One GABS daemon **per uid** is correct and
deliberate: a daemon must run as the identity whose game it launches, because the child
inherits that uid and its Steam session. Arrange must not attempt to drive both clients
from one daemon. Valbot's GABS is a thin `DirectPath` shim that ends in an AppID request
so that **Steam** performs the spawn — "launch as the other Steam identity" *is* the
AppID request, and GABS cannot replace it. See `AGENTS.md`.

---

## 2. Invariants (gathered, with evidence)

### I1 — The mod under test must be present on EVERY client
`diff` of the two plugin directories returned exactly one line: `SBPR.QaHarness.T022`,
present on client_a, absent on client_b, for the entire effort. A client without the
harness boots normally, loads every product mod, and waits at a menu forever.
**MERGED (#451).** *Evidence: plugin dir diff, 2026-07-29.* → **#451**

### I2 — Provisioning must be count-agnostic
The isolation library hardcoded `for index in 0 1 2` and `-eq 5` in five places. Adding a
fourth artifact staged **nothing** — the loops never reached it. Silent.
**MERGED (#451).** *Evidence: `run-trailborne-valbot-isolation-lib.sh` pre-fix.* → **#451**

### I3 — Staging must be able to CREATE, not only replace
The stager validated an existing parent directory and refused otherwise
(`parent kind: not a regular directory`). A manifest could therefore never introduce a
NEW artifact. **MERGED (#451).** *Evidence: staging refusal, 11:46:42.* → **#451**

### I4 — Credentials must be readable by the identity that consumes them
**MERGED (#452).** Written `0600` in a `0700` directory by uid 1000; consumed by uid
1001. Two independent locks, either one sufficient. Applies to the lane password AND the
bootstrap doc.

I4 does **not** dissolve under the option-B topology: writer and reader remain different
uids. The approved policy is per-run credentials at `0644` under a `0711` dedicated
credential directory — traversable without being listable — followed by an **actual read
performed as the declared consuming uid**. These are throwaway credentials on a
disposable loopback lane, minted fresh with a short TTL and swept on teardown, so local
readability is the correct trade; do not reach for a mechanism the threat model does not
justify.

**A dangling reference is the same defect as an unreadable one.** #461's live run found
`SBPR_QA_SERVER_PASSWORD_FILE` pointing at a non-existent path on **both** clients — not
just client_b. Existence and readability are ONE precondition, and neither is satisfied
by declaration alone. See `T022-ARRANGE-CREDENTIAL-PROVISIONING.md`.

### I5 — The join target must actually reach the game
**MERGED (#453); the original live blocker, resolved by #449.** client_a gets
`+connect 127.0.0.1:2476` from its GABS launcher. client_b is launched through Steam's
AppID path, which forwards no arguments, and `env -i` scrubs the environment — so
`m_queuedJoinServer` was never populated and it stopped at the server-list screen. The
harness hooks `ShowCharacterSelection`, i.e. it automates *character select only*, so its
trigger never fired.

**The fix was not a launcher swap.** #449 proved live that a Steam-launched client CAN
receive `+connect`, via the Steam `%command%` **wrapper seam**. The wrapper appends the
fragment after `"$@"`:

```
exec setsid --wait "$VSI_BEPINEX_RUNNER" "$@" "${SBPR_QA_CONNECT_ARGS[@]}"
```

`connect_argv` is therefore legal under **every** launcher kind. An earlier revision of
the static check refused it under `steam_applaunch` on the theory that Steam passes no
arguments; that theory was disproved by a live run, and the check was refusing the one
configuration actually proven to work. **A fail-closed guard that blocks the working path
is worse than no guard** — it costs a debugging cycle and teaches people to disable the
checker.

The real fragility, which IS worth encoding: `run_bepinex.sh` detects Steam's
`SteamLaunch` marker and **rotates argv**, so appended args survive into the game and
prepended args are swallowed. That is a property of the wrapper's internals, checkable by
reading the wrapper (STATIC, merged) but only *provable* by reading a launched process's
real argv (VERIFY, #456).

### I6 — Per-client resources must not collide
**Partly merged (#454, declarative half).** `UnityScriptHost: Failed to bind
127.0.0.1:48210: Address already in use` — a hardcoded single-instance port. Loopback
control ports (48610/48611) are correctly per-client; UnityScriptHost and ValBridgeServer
were not.

Under the option-B topology this is **confirmed under real concurrency, not inferred**:
#461's live run reproduced both bind failures on client_b while client_a held the ports.
It did not block the join — the harness rides its own path — but **client_b had no GABP
bridge while client_a ran**, so `games_connect` against `:8081` cannot drive it. T022
needs both clients drivable, so per-client ports are a **hard prerequisite**, not an
optimisation.

Prefer **deleting the problem to configuring it**: UnityScriptHost appears unused by T022
(the legs ride `LiveLoopbackTransport`), so it should be disabled on the second client
rather than given a second port. ValBridgeServer is emphatically used, so its port must
become per-client regardless. A component declared disabled must be **provably absent**,
not merely unconfigured — enumerating the client's whole plugin tree is the only proof of
absence, so that enumeration seam is a mandatory dependency of the check, never a
defaulted one (#467).

### I7 — Cross-uid process provenance cannot use `/proc/<pid>/environ`
Kernel restriction, no user-space workaround. The marker backs the B1 kill guard, so it
cannot simply be dropped. Current solution: an attested `{marker, pid}` receipt, with the
runner re-deriving binary and start-ticks from world-readable `/proc` entries. Under
option B the uid split persists, so this machinery stays live.

### I8 — Deployed artifacts must match their reviewed source
A stale deployed launcher predating M6-LAUNCHENV was correctly refused by a byte-equality
guard. This invariant is **working as intended** — keep it. Generalised across every
artifact and client by the S3 pin check.

### I9 — Timing budgets must track the payload
Cold boot to main menu measured at ~145s against a 150s budget. A fixed constant
benchmarked against an ever-growing mod stack is a latent failure with a countdown, and
it fails *flakily* before it fails consistently.
**Requirement:** derive or declare budgets; never freeze a magic number in source.

### I10 — Teardown must be guaranteed, not best-effort
Cleanup runs only on the runner's graceful exit paths. Every SIGKILL left **live
credentials on disk** (verified: expiry ~113 min in the future). The TTL bounds the
damage, but the guarantee is weaker than the design claims: cleanup-on-graceful-exit, not
cleanup-guaranteed.
**Requirement:** sweep on startup as well as teardown. → **#455**

### I11 — Preflight is cheap; booting is expensive
The STATIC phase runs in **64 ms** on the real two-client manifest, needs no game, no
GPU, no Steam — and caught two real defects. (Measured for STATIC alone; see
`T022-ARRANGE-STATIC-IMPLEMENTATION.md`. Compose is a separate path and is not covered
by that figure.) Booting two GPU clients is where
everything dies (~10 min/cycle).
**Requirement:** every precondition that CAN be checked statically MUST be, before any
client launches.

### I12 — Per-client identity extends to QA character profiles
**New, from #461's live run.** Both clients' sidecars declared the same
`SBPR_QA_PROFILE=sbpr_qa_join`. Harmless sequentially; under concurrency two clients
contend for one character save. Changing client_b to `sbpr_qa_join_b` resolved it and the
harness created the profile cleanly via the vanilla path.

QA profile disjointness therefore belongs in the manifest's disjointness checks alongside
ports — and the live sidecars violated it while nothing caught it, which is the exact
class of silence this phase exists to end. The **allowlist-of-one** join guard is
preserved independently: a QA join names its own profile and can never load a human
character.

---

## 3. Required properties of the arrange script

**P1 — Single authority.** One script owns arrangement end to end. No other mechanism
provisions, writes credentials, or stages artifacts. (Reached at #457's contract step;
until then the old mechanisms coexist by design.)

**P2 — Declarative, per-client.** One manifest describes each client: identity, roots,
launcher, ports, artifacts, credentials, QA profile. Adding a third client is a data
change.

**P3 — Fail loud and specific.** Every failure names the precondition, the client, and
expected-vs-actual. No silent no-ops. Checks do not short-circuit: one invocation reports
**every** problem, because each 10-minute boot cycle that discovers one more problem is
the cost this phase exists to avoid.

**P4 — Verify, don't assume.** Every arranged fact is read back and asserted after being
established. Assume nothing about symmetry between clients.

**P5 — Static-first ordering.** Cheap descriptor/filesystem checks run before any process
starts. Nothing expensive happens until everything cheap has passed.

**P6 — Idempotent + self-cleaning.** Safe to run repeatedly. Sweeps stale state from
prior runs (credentials, receipts, dead harness-owned clients) on entry.

**P7 — Observable.** Emits a machine-readable readiness report per client, so "is it
arranged?" is answered by reading a file, not by inferring from process tables.

**P8 — Preserve the real guards.** B2 production deny (2456/2466), B1 kill provenance,
artifact drift, pin verification, expiry checks — these are correct and have all
demonstrably prevented harm. Arrange must keep them, not route around them.

**P9 — Proof seams are mandatory, not defaulted.** Where a check's entire value is
proving a **negative** (a component is absent, a tree contains no such file), the
capability that establishes it must be a required dependency of the check. A defaulted
"cannot prove" seam is not a security hole when it fails closed, but it is a diagnostic
one: an omitted wiring is then reported as a fault in the *client's filesystem* rather
than as an incomplete caller, sending an operator to inspect a machine that is fine.
Make the incomplete caller impossible to construct instead. (#454, restored by #467.)

---

## 4. Phase ordering

Phase names below are the contract; the issue that owns each is named explicitly, because
an earlier revision of this table mapped phases to the wrong issues and the CLI copied
the error.

```
STATIC   (no processes; sub-second; fail here whenever possible)   [MERGED #450]
  ├── S1 descriptor well-formed; per-client identity/roots/ports declared
  ├── S2 production ports 2456/2466 absent from every target
  ├── S3 artifact source present; pins match deployed bytes  ....... [I8]
  ├── S4 lane password policy consistent; each credential names its
  │      consuming uid, and no path is shared across uids  ......... [I4]
  ├── S5 per-client port sets disjoint  ........................... [I6]
  ├── S6 every client's required artifacts exist in the catalogue  . [I1]
  ├── S7 per-client destinations live under that client's own root  [A1]
  ├── S8 join target declared, is this run's lane, QA profile named
  │      and disjoint, and the client's own wrapper can carry it  .. [I5][I12]
  └── S9 components declared disabled are provably absent  ......... [I6][P9]

SWEEP    (idempotent cleanup of prior-run residue)                  [#455]
  ├── stale credentials and provenance receipts  .................. [I10]
  ├── dead harness-owned clients — and ONLY harness-owned ones (B1)
  └── credentials cannot outlive the run that minted them, even on SIGKILL

STAGE    (filesystem; still no game processes)               [MERGED #451]
  ├── stage artifacts to EVERY client from ONE manifest  .......... [I1][I2][I3]
  ├── create a missing plugin directory, not only replace  ........ [I3]
  └── post-condition: every client has every artifact, hashes match

PROVISION (credentials + launch env)                     [MERGED #452, #453]
  ├── mint per-run credentials  ................................... [I4]
  ├── write them readable by each CONSUMING uid (0711/0644)  ...... [I4]
  └── write per-client launch env INCLUDING the join target  ...... [I5]

VERIFY   (read back everything just arranged)                       [#456]
  ├── every client has every required artifact, hashes asserted
  ├── every credential readable BY ITS CONSUMER, tested as that uid  [I4]
  ├── join target present in each client's ACTUAL launch path  .... [I5]
  ├── per-client port sets verified disjoint AND free  ............ [I6]
  └── emit a machine-readable readiness report per client  ........ [P7]

LAUNCH   (expensive; only reached when all the above passed)        [#457 cutover]
  ├── per-client boot with derived budgets  ....................... [I9]
  ├── provenance capture, cross-uid safe  ......................... [I7]
  └── readiness = explicit armed probe, never a sleep

READY    → per-client readiness report is the run's entry condition  [P7]
```

#457 owns LAUNCH as the *cutover*: it moves the runner onto this phase by
expand-contract (new arrange runs alongside the old path, then the runner is migrated,
then the unreferenced old mechanisms are deleted). The CLI calls that work CUTOVER;
this table names the phase it delivers.

SWEEP runs before STAGE: clearing prior-run residue first means STAGE never writes
alongside state it is about to invalidate, and never hashes bytes that are about to be
replaced. Both are idempotent, so the pair is safe to re-run; the ordering is about
avoiding wasted proof, not about correctness of either step alone.

---

## 5. Open questions

**Q1 — How should client_b receive its join target?** **ANSWERED (#449, merged #453).**
Through the Steam `%command%` wrapper seam, appended after `"$@"`. Proven live. See I5.

**Q2 — Is a second Steam account on a second Unix user actually required?**
**ANSWERED by the #448 spike, and separately DECIDED by #461.** (#448's finding is
written up and relied on by #461's decision, but the issue itself is still open at the
time of writing; treat the citation as the spike's recorded result rather than a closed
ticket.) Not *required* by the product: the
TRANSFER verdict never resolves the requesting peer. But T022 **runs on** the concurrent
dual-user rig by decision, because that topology is the only one that can observe
account-scoped concurrent admission and ZDO ownership contention. Not required ≠ not
valuable; the two answers are compatible and both stand.

**Q3 — Should arrange own launching, or stop at "ready to launch"?** Open. #457 decides
it in practice: arrange owns LAUNCH, because the readiness probe and the provenance
capture are both arrange concerns and splitting them across a boundary re-creates the
"four mechanisms that don't know about each other" problem this spec exists to end.

**Q4 — Is a partial run a legitimate artifact?** Open, and a **product call, not an
implementation detail**. The runner requires 4 receipts and the FSM is strictly
sequential, so a client_a-only run (ISSUE+UPGRADE) emits FAIL/IncompleteEvidence even if
both legs genuinely pass. **Not to be solved by lowering the threshold.**

**Q5 — Does UnityScriptHost need to run at all?** Effectively answered: no, it is unused
by T022 (the legs ride `LiveLoopbackTransport`), and its hardcoded port collides under
concurrency. Disable it on the second client rather than allocate it a port; S9 makes
"disabled" a provable claim. ValBridgeServer, by contrast, IS needed and must become
per-client.

---

## 6. Explicit non-goals

- Not redesigning the four AT legs or the FSM.
- Not touching the evidence/verdict layer.
- Not weakening any existing guard to make a run pass.
- Not adding retries as a substitute for a correct precondition.
- Not designing around GABS zombie liveness. **It is fixed upstream in our fork**
  (`PolyphonyRequiem/GABS` `b679943` reaps children on every terminate path; `79e1779`
  makes the finder skip processes in state `Z`), verified in production 2026-07-29 with
  10 defunct clients and a correct `stopped` report. Both daemons run the fixed binary.
  `_reset_gabs_state` in `qa/runner/runner_core/live_composition.py` is therefore
  **defence in depth, not load-bearing** — keep it (cheap, idempotent, correctly gated on
  zero live non-zombie clients so it can never touch Daniel's own Steam Valheim), but
  build no new workaround on the premise that GABS lies. If it lies again, that is a
  regression to fix in the fork, not a fact to route around here. See `AGENTS.md`.
- **Not #459 (sequential single-account two-character run).** #461 chose option B, so the
  sequential topology is **superseded and non-blocking** for T022. Its substantive
  content — `expected_conn_gen` seeded per actor across a disconnect (`fsm.py:221-226`) —
  becomes relevant only if the option-B fallback trigger ever fires. It does not gate any
  arrange work.
- **Not #460 (concurrent ZDO ownership contention).** Explicitly **non-blocking** for
  T022 and deliberately unscheduled. #448 established TRANSFER's assertion is invariant
  to concurrency: the validation handler never resolves the requesting peer, and the
  stamp's HMAC covers only immutable fields. Ownership contention is a real, genuinely
  untested question about a *different layer*, worth answering on its own terms — and
  worth noting the dual-user rig is what makes it answerable. It is not a T022
  precondition.
- Not running T022 itself. Arrange ends at READY.

---

## 7. What changed from the branch-only draft

Recorded so the reconciliation is auditable rather than silent (#468).

| Draft said | Now | Why |
|---|---|---|
| Status DRAFT FOR REVIEW, no implementation | `current`; STATIC, credentials and join delivery merged | #450, #452, #453, #454 landed |
| Q2 "nobody has re-examined" the second account — "highest-leverage question" | Answered by #448 and decided by #461 (option B) | The question was examined and closed |
| I5 the current live blocker; Q1 unanswered | Resolved: Steam `%command%` wrapper seam, proven live | #449 |
| I6 inferred from one bind error | Confirmed under real concurrency; a hard T022 prerequisite | #461 live run |
| I4 framed as a client_b problem | Both clients; existence and readability are one precondition | #461 live run |
| No QA-profile invariant | I12 added: profiles must be per-client and disjoint | #461 found both clients sharing one |
| Phases mapped SWEEP #451 / VERIFY #455 / LAUNCH #456 | SWEEP #455 / STAGE #451 / VERIFY #456 / CUTOVER #457 | The mapping was wrong and the CLI copied it |
| "zombie clients GABS never reaps" as a standing fact | Fixed upstream; local reset is defence in depth | `AGENTS.md`, GABS fork |
| No stance on defaulted proof seams | P9 added | #454's contract, regressed and restored by #467 |
| "dual-client rig" | "dual-user rig" | There is one machine; the old term implied two |

## Related

- `docs/qa/T022-ARRANGE-STATIC-IMPLEMENTATION.md` — the shipped STATIC phase in detail.
- `docs/qa/T022-ARRANGE-CREDENTIAL-PROVISIONING.md` — I4 as implemented.
- `docs/decisions/0009-qa-harness-separate-fail-closed-mod.md` — why the harness is a
  separate fail-closed mod with an engine-free external runner.
- `AGENTS.md` §"QA live-harness process discipline" — daemon topology, process reaping,
  worktree isolation.
