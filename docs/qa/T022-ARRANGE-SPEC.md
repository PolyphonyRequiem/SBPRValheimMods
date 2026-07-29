# T022 ARRANGE — specification

**Scope:** everything that must be true *before* the first AT leg runs. Arrange only.
Act (the four legs) and Assert (evidence/verdict) are explicitly out of scope.

**Status:** DRAFT FOR REVIEW. No implementation. Invariants below are gathered from the
running system on 2026-07-29, each with the evidence that established it.

---

## 0. Why this exists

Twelve days produced zero executed acceptance tests. Not one failure was in the test
logic; every one was in arrangement. The arrange phase is currently spread across **four
independent mechanisms that do not know about each other**:

| Concern | Owner today | Aware of the others? |
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

---

## 1. The asymmetry that causes almost everything

The two clients are **not** two instances of one thing. They differ along every axis:

| | client_a | client_b |
|---|---|---|
| uid | 1000 (`polyphonyrequiem`) | 1001 (`valbot`) |
| Steam account | 76561197965627562 | 76561198671522196 |
| Game root | `~/.local/share/Trailborne/Valheim-Modded` | `/home/valbot/.steam/.../common/Valheim` |
| Launched by | GABS `games_start` | Steam `-applaunch 892970` via `systemd-run` |
| Join target delivery | `+connect` on the command line | **NOTHING — this is the gap** |
| Env delivery | inherited from GABS fork | `env -i` scrub, then sidecar re-injection |
| Mod provisioning | QA overlay packer | artifact manifest + isolation staging |

Every layer was written for one client, then had a second client bolted on. The uid split
is not a first-class concept anywhere — it is rediscovered painfully at each layer.

**Requirement A1.** Arrange MUST treat "a client" as a parameterised thing with a
per-client identity, game root, launch mechanism, port set, and credential paths. No
step may assume same-uid, same-path, or same-launcher.

---

## 2. Invariants (gathered, with evidence)

### I1 — The mod under test must be present on EVERY client
`diff` of the two plugin directories returned exactly one line: `SBPR.QaHarness.T022`,
present on client_a, absent on client_b, for the entire effort. A client without the
harness boots normally, loads every product mod, and waits at a menu forever.
*Evidence: plugin dir diff, 2026-07-29.*

### I2 — Provisioning must be count-agnostic
The isolation library hardcoded `for index in 0 1 2` and `-eq 5` in five places. Adding a
fourth artifact staged **nothing** — the loops never reached it. Silent.
*Evidence: `run-trailborne-valbot-isolation-lib.sh` pre-fix.*

### I3 — Staging must be able to CREATE, not only replace
The stager validated an existing parent directory and refused otherwise
(`parent kind: not a regular directory`). A manifest could therefore never introduce a
NEW artifact. *Evidence: staging refusal, 11:46:42.*

### I4 — Credentials must be readable by the identity that consumes them
Written `0600` in a `0700` directory by uid 1000; consumed by uid 1001. Structurally
impossible. Applies to the lane password AND the bootstrap doc.
*Evidence: `SBPR_QA_SERVER_PASSWORD_FILE set but file unreadable`.*
**Note:** these are per-run throwaway credentials on a disposable loopback lane, minted
fresh with a short TTL and swept on teardown. Local readability is the correct trade.

### I5 — The join target must actually reach the game
**The current live blocker.** client_a gets `+connect 127.0.0.1:2476` on its command
line. client_b is launched as `steam -silent -applaunch 892970` with **no arguments**,
and `env -i` scrubs the environment. So `m_queuedJoinServer` is never populated.

The join sequence is: **world/server selection → character select → spawn.** The harness
patch hooks `ShowCharacterSelection` and drives `OnCharacterStart` — i.e. it automates
*character select only*. It relies on `+connect` having already queued the server.
Without it, client_b stops at the server-list screen and the patch's trigger never fires.
*Evidence: `run-trailborne-valbot-isolation-lib.sh:797`; client_b log ends at `bannerdiag`.*

### I6 — Per-client resources must not collide
`UnityScriptHost: Failed to bind 127.0.0.1:48210: Address already in use` — a hardcoded
single-instance port. Loopback control ports (48610/48611) are correctly per-client;
UnityScriptHost and ValBridgeServer are not.

### I7 — Cross-uid process provenance cannot use `/proc/<pid>/environ`
Kernel restriction, no user-space workaround. The marker backs the B1 kill guard, so it
cannot simply be dropped. Current solution: an attested `{marker, pid}` receipt, with the
runner re-deriving binary and start-ticks from world-readable `/proc` entries.

### I8 — Deployed artifacts must match their reviewed source
A stale deployed launcher predating M6-LAUNCHENV was correctly refused by a byte-equality
guard. This invariant is **working as intended** — keep it.

### I9 — Timing budgets must track the payload
Cold boot to main menu measured at ~145s against a 150s budget. A fixed constant
benchmarked against an ever-growing mod stack is a latent failure with a countdown, and
it fails *flakily* before it fails consistently.
**Requirement:** derive or declare budgets; never freeze a magic number in source.

### I10 — Teardown must be guaranteed, not best-effort
Cleanup runs only on the runner's graceful exit paths. Every SIGKILL left **live
credentials on disk** (verified: expiry ~113 min in the future).
**Requirement:** sweep on startup as well as teardown.

### I11 — Preflight is cheap; booting is expensive
Full preflight + compose runs in under a second, needs no game, no GPU, no Steam — and
caught two real defects. Booting two GPU clients is where everything dies (~10 min/cycle).
**Requirement:** every precondition that CAN be checked statically MUST be, before any
client launches.

---

## 3. Required properties of the arrange script

**P1 — Single authority.** One script owns arrangement end to end. No other mechanism
provisions, writes credentials, or stages artifacts.

**P2 — Declarative, per-client.** One manifest describes each client: identity, roots,
launcher, ports, artifacts, credentials. Adding a third client is a data change.

**P3 — Fail loud and specific.** Every failure names the precondition, the client, and
the expected-vs-actual. No silent no-ops. `arrange` either returns "ready" or an
actionable named failure.

**P4 — Verify, don't assume.** Every arranged fact is read back and asserted after being
established. Assume nothing about symmetry between clients.

**P5 — Static-first ordering.** Cheap descriptor/filesystem checks run before any process
starts. Nothing expensive happens until everything cheap has passed.

**P6 — Idempotent + self-cleaning.** Safe to run repeatedly. Sweeps stale state from prior
runs (credentials, receipts, zombies) on entry.

**P7 — Observable.** Emits a machine-readable readiness report per client, so "is it
arranged?" is answered by reading a file, not by inferring from process tables.

**P8 — Preserve the real guards.** B2 production deny (2456/2466), B1 kill provenance,
artifact drift, pin verification, expiry checks — these are correct and have all
demonstrably prevented harm. Arrange must keep them, not route around them.

---

## 4. Phase ordering

```
STATIC   (no processes; sub-second; fail here whenever possible)
  ├── descriptor well-formed; per-client identity/roots/ports declared
  ├── production ports 2456/2466 absent from every target
  ├── artifact source present; pins match deployed bytes
  ├── lane password policy consistent with client entries
  └── per-client port sets disjoint  ......................... [I6]

SWEEP    (idempotent cleanup of prior-run residue)
  ├── stale credentials, provenance receipts  ................ [I10]
  └── stale/zombie clients (harness-owned only)

PROVISION (filesystem; still no game processes)
  ├── stage artifacts to EVERY client from ONE manifest  ..... [I1][I2][I3]
  ├── mint per-run credentials
  ├── write credentials readable by each consuming uid  ...... [I4]
  └── write per-client launch env INCLUDING the join target .. [I5]

VERIFY   (read back everything just arranged)
  ├── every client has every required artifact, hashes match
  ├── every credential readable BY ITS CONSUMER (test as that uid)
  └── join target present in each client's actual launch path  [I5]

LAUNCH   (expensive; only reached when all the above passed)
  ├── per-client boot with derived budgets  .................. [I9]
  ├── provenance capture (cross-uid safe)  ................... [I7]
  └── readiness = explicit armed probe, never a sleep

READY    → emit per-client readiness report  .................. [P7]
```

---

## 5. Open questions for design

**Q1 — How should client_b receive its join target?** The blocking unknown. Options:
(a) pass launch args through `steam -applaunch <id> -- +connect <target>`;
(b) extend the harness to drive world/server selection, not just character select;
(c) launch client_b by the same mechanism as client_a and drop the Steam AppID path.
Needs a prototype; (a) is cheapest if Steam forwards args reliably.

**Q2 — Is a second Steam account on a second Unix user actually required?** Nearly every
wall descends from this one choice: cross-uid credentials, `/proc` provenance, X grants,
byte-pinned launchers, false Steam gates. If TRANSFER only needs a second *player
identity*, the entire uid split may be unnecessary. **Nobody has re-examined this.**
This is the highest-leverage question in the document.

**Q3 — Should arrange own launching, or stop at "ready to launch"?** Ordering argues for
including it; separation of concerns argues against.

**Q4 — Is a partial run a legitimate artifact?** The runner requires 4 receipts and the
FSM is strictly sequential. A client_a-only run (ISSUE+UPGRADE) emits FAIL/
IncompleteEvidence even if both legs genuinely pass. Product call, not an implementation
detail — and NOT to be solved by lowering the threshold.

**Q5 — Does UnityScriptHost need to run at all?** It is unused by T022 (the legs ride
LiveLoopbackTransport). Its hardcoded port collides. Disabling it on the second client
may be simpler than making it per-client.

---

## 6. Explicit non-goals

- Not redesigning the four AT legs or the FSM.
- Not touching the evidence/verdict layer.
- Not weakening any existing guard to make a run pass.
- Not adding retries as a substitute for a correct precondition.
