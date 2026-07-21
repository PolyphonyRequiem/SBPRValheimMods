---
title: "IAP-010 — Operator controls proven on a dedicated QA server (executed evidence)"
status: proposed
author: qa-playtest (t_58ba450f)
purpose: >
  Executed operator-control evidence for the IAP-009 foundation, run on the dedicated
  Niflheim QA server host against REAL operating-system resources using only a QA-only
  identity (ForTheWort_QA). Proves protected allowlist bootstrap, first-join/account
  discovery, live-admin inspect, disable while idle and during a controlled in-flight
  mutation, deterministic session closure, failed-drain recovery, delete-drain with
  allowlist revocation, post-disable rejection, and a genuine process-restart recovery.
  Captures the exact commands, logs, and identities used without exposing any raw
  provider subject. This is focused operator evidence, not the final full-pilot journey.
---

# IAP-010 — Operator controls on a dedicated QA server (executed evidence)

**Task:** t_58ba450f (`qa-playtest`) · parent IAP-009 t_32cdc8ea (PR #334) · sibling verifier IAP-011 t_992dbd81 (`reviewer-adversarial`)
**Spec/plan/contracts:** [`account-identity-pilot-spec.md`](account-identity-pilot-spec.md), [`account-identity-pilot-plan.md`](account-identity-pilot-plan.md) → Tracer 4 control subset, [`account-identity-pilot-operator-evidence.md`](account-identity-pilot-operator-evidence.md)
**Runbook exercised:** [`../runbooks/account-identity-pilot-operator-runbook.md`](../runbooks/account-identity-pilot-operator-runbook.md)

## What this is (and the honest frame)

IAP-009 shipped the operator DECISION LOGIC as engine-free CLEAN cores (`OperatorAccountService`,
`OperatorAdminGate`, `AccountMutationFence`, `PilotSessionRegistry`, `LocalAllowlistBootstrap`,
`PilotAccountStore`) exercised by unit tests. Its runbook explicitly deferred **the thin host that
binds real stdin / `stat` / console I/O to those cores** to "the live-server integration in IAP-010."
That host did not exist: no Harmony patch, console command, or CLI bound the cores to real OS
resources on the server — they were reachable only from `tests/`.

This task built that host and ran it **on the dedicated Niflheim QA server box** against **real OS
resources**, then split cleanly what is verified from what remains the client last mile.

### Verified here (real OS host over the shipped cores)
The QA harness (`qa-operator-harness/`) **link-compiles the same shipped IAP-009 core source files**
that `tests/NiflheimOperatorControlTests.cs` compiles — same provenance, no copy or fork, so the
asserted behaviour IS the shipped behaviour — and binds them to real resources:

- **Real account journal file** on the server host (`account-journal.bin`), rehydrated across a
  genuine process boundary.
- **Real `stat(2)`-derived ownership** (`File.GetUnixFileMode`) so the OS-scope fail-closed boundary
  is measured on real inode permission bits, not simulated.
- **Real protected no-echo stdin channel** semantics; argv/env/chat channels proven refused.
- **A real concurrent in-flight mutation** on a second OS thread that the disable drains through the
  real fence before committing.
- **A genuine restart**: phase B is a *separate `dotnet` process* (new PID) that only reads the
  on-disk journal.

### NOT claimed here (client last mile — Daniel / IAP-011)
- A **Valheim graphical client** joining the `-nographics` headless dedicated server and a human
  visually observing the kick/disable. The Niflheim server cannot host a graphical client
  (established last-mile precedent, `docs/v1/research/QA-client-usability.md`). The join-and-see-kick
  step stays with Daniel's manual walk / the adversarial verifier.
- Independent adversarial re-verification (IAP-011, `reviewer-adversarial`).

## Environment & identities used

| Fact | Value |
|---|---|
| Host | `Requiem-Prime-U` (dedicated Niflheim server box), service account `polyphonyrequiem` |
| World / seed | `niflheim.fwl`, seed **`ForTheWort`** |
| QA character/account label | **`ForTheWort_QA`** (`<seed>_QA`) — NOT a regular character, NOT Pololol |
| Provider class | `Steam` (single admitted pilot backend `niflheim-pilot-app-896660`) |
| QA provider subject | a reserved **synthetic** QA-only Steam subject; HMAC'd by the core, **never persisted or echoed raw** |
| Admin host | mirrors `config/adminlist.txt` shape (`Steam_7656119800000000x`) |
| Data dir (throwaway) | `/home/polyphonyrequiem/valheim/niflheim/pilot-qa-run` (removed after capture; HMAC key `shred`-ed) |

The raw QA subject value is deliberately omitted from this report. The persisted journal was
recursively base64-decoded end-to-end and contains **no** raw subject (see privacy scan below).

## Exact commands

```bash
# Build the real OS host over the shipped IAP-009 cores (0 warnings, TreatWarningsAsErrors):
SRC=.../aip-t009-operator-control/wt/src/SBPR.Niflheim.HomesteadStones
dotnet build -c Release -p:SRCDIR=$SRC   # -> Build succeeded

DATA=/home/polyphonyrequiem/valheim/niflheim/pilot-qa-run
DLL=bin/Release/net8.0/linux-x64/QaOperatorHarness.dll

# Phase A — bootstrap → discover → inspect → in-flight disable → failed-drain → delete (one process):
dotnet $DLL --phase A --data $DATA     # -> PASS=18 FAIL=0, exit 0

# Phase B — SEPARATE process (new PID): restart recovery + post-disable rejection:
dotnet $DLL --phase B --data $DATA     # -> PASS=5  FAIL=0, exit 0
```

## Results — 23/23 operator checks PASS across two real OS processes

### Phase A (pid distinct from shell) — 18/18
| Check | Acceptance | Result |
|---|---|---|
| BOOTSTRAP-FAILCLOSED | AT-AIP-LOCAL-BOOTSTRAP-SCOPE | real `0640` key path → `KeyPathTooPermissive`, nothing written |
| BOOTSTRAP-OWNERONLY-BITS | AT-AIP-LOCAL-BOOTSTRAP-SCOPE | real `stat` of `0600` path reads owner-only |
| BOOTSTRAP-PROVISION | AT-AIP-LOCAL-BOOTSTRAP-SCOPE | no-echo stdin → `Provisioned` (`allow-…`) |
| BOOTSTRAP-NO-SUBJECT-ECHO | AT-AIP-LOCAL-BOOTSTRAP-SCOPE | output line carries no raw subject |
| BOOTSTRAP-CHANNEL-REFUSE | AT-AIP-LOCAL-BOOTSTRAP-SCOPE | argv/env/chat subject channels all `SubjectChannelForbidden` |
| BOOTSTRAP-NO-LIFECYCLE | AT-AIP-LOCAL-BOOTSTRAP-SCOPE | every account-lifecycle verb `VerbOutOfLocalScope` |
| FIRST-JOIN-BIND | (Tracer-1 admission) | first-bind minted `acct-…` over the allowlisted QA subject |
| INSPECT-ACCEPT / INSPECT-SAFE | AT-AIP-ADMIN-INSPECT | admin summary: `Active`, creds=1, class `[Steam]`, no raw subject |
| NONADMIN-REJECT | AT-AIP-NONADMIN-REJECT | non-admin inspect `Rejected`; disable `NotAdmin`; unauth `UnauthenticatedPeer` |
| NONADMIN-NO-MUTATION | AT-AIP-NONADMIN-REJECT | account still `Active` after rejected attempts |
| SESSION-OPEN | (setup) | QA live session active (handle 4242) |
| DISABLE-DRAINS | AT-AIP-MUTATION-FENCE | disable **blocks on the drain barrier** while a real in-flight mutation is held; account stays `Active` |
| DISABLE-APPLIED | AT-AIP-ADMIN-DISABLE | post-drain disable `Applied`; session closed (handle 4242) after durable commit |
| DISABLE-SESSION-GONE | AT-AIP-DISABLE-CLOSES-SESSION | live session deterministically removed |
| DISABLE-IDEMPOTENT | AT-AIP-ADMIN-DISABLE | same op replays → `Replayed` |
| DRAIN-TIMEOUT-RECOVER | AT-AIP-MUTATION-FENCE (neg) | stuck mutation + bounded timeout → `DrainTimeout`, account stays `Active` (recoverable) |
| DELETE-APPLIED | AT-AIP-DELETE-DRAIN-BARRIER | delete `Applied`; session closed |

### Phase B — fresh process, restart recovery — 5/5
| Check | Acceptance | Result |
|---|---|---|
| RESTART-REHYDRATE | AT-AIP-FR-016 recovery | account rehydrated from on-disk journal in a new PID |
| RESTART-DELETION-DURABLE | AT-AIP-ADMIN-DISABLE (durable) | `DeletionPending` survived the process restart |
| POST-DELETE-REJECT | AT-AIP-DELETE-DRAIN-BARRIER | same QA subject re-join rejected `AccountDeletionPending` — the still-present revoked credential trips the wound-down re-admission barrier, so auto-create does not recreate the account |
| RESTART-SESSION-CLEARED | AT-AIP-FR-016 | process-local session registry empty in the fresh process (no stale session survives restart) |
| DRAIN-RECOVERY-COMPLETES | AT-AIP-MUTATION-FENCE recovery | the account left `Active` by the failed drain disables cleanly after restart (`Disabled`) |

Recovery semantics for both **failed drains** (bounded `DrainTimeout` leaves a coherent Active state
that later completes cleanly) and **process death / restart** (journal rehydrate, durable status,
cleared session registry) are demonstrated with real artifacts.

## Privacy verification (load-bearing)

- Recursive base64 decode of the **entire** persisted journal → **no raw provider subject** present
  (only HMAC hex + opaque `acct-`/`allow-`/`cred-` ids + coarse status). See
  `artifacts/journal-privacy-scan.txt`.
- HMAC key file was `0600` (owner-only) for the whole run; `shred`-ed on teardown.
- Neither the evidence log nor the phase-A id handoff contains any raw subject.

## Artifacts

Under `t_58ba450f/artifacts/` (kanban run workspace; checksums in `artifact-checksums.txt`):

| Artifact | What |
|---|---|
| `operator-evidence.log` | full stdout of both phases (23/23 PASS, host/pid/utc banners) |
| `qa-account-journal.bin` | the real persisted account journal (proven subject-free) |
| `journal-privacy-scan.txt` | raw-subject scan + recursive-base64 decode result + key perms |
| `artifact-checksums.txt` | sha256 of the log + journal |

The harness source lives on this branch at `qa-operator-harness/` (link-compiles the shipped cores;
`dotnet build` 0 warnings).

## Verdict

**PASS at the operator-control layer on a real dedicated QA server**, against real OS resources,
using only the `ForTheWort_QA` QA-only identity. All 7 IAP-009 named acceptance IDs are exercised
end-to-end by a real host (not just unit tests), plus first-join discovery, in-flight-mutation
disable, failed-drain recovery, and a genuine process-restart recovery.

**Remaining last mile (not this task):** a Valheim graphical client joining the headless server and a
human seeing the kick — stays with Daniel's manual walk; independent adversarial re-verification is
IAP-011. Logs/tests green here does not prove the joined-client experience.
