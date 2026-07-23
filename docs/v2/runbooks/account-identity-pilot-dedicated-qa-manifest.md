---
title: "IAP-015 — Dedicated pilot preflight manifest & evidence matrix"
status: proposed
author: architect (t_7bc3b94a)
date: 2026-07-23
purpose: >
  Executable preflight manifest for the IAP-015 dedicated joined-client account-identity journey.
  Resolves the EXECUTE attempt-8 crossplay/parity topology block by specifying ONE fixture that is
  BOTH parity-staged to the accepted candidate AND join-capable by the direct-Steam modded GUI
  client. Does not change the standing server or consume its first-bind state. Every journey step
  is mapped to its required acceptance ID with exact commands, expected observable behavior,
  artifact locations, correlation identifiers, and failure/abort conditions. Unknown facts are
  marked OPEN, not guessed.
---

# IAP-015 — Dedicated pilot preflight manifest & evidence matrix

**Executing worker:** the assignee of `t_8a3a55c6` (`starbright-engineering` / qa-playtest lane).
**Preflight gates §0.4 are SETTLED** from the completed environment card `t_52f12248`
(READY_FOR_EXECUTE) — no owner gate remains open; see §0.4 for the recorded dispositions.

**Source of truth (read all before executing):**
- Spec: `../planning/account-identity-pilot-spec.md` (AIP-FR-028, AIP-SC-001..008)
- Contracts: `../planning/account-identity-pilot-contracts.md`
- Gate 0: `../planning/account-identity-pilot-gate0-evidence.md`
- IAP-015 evidence: `../planning/account-identity-pilot-operator-evidence.md` §IAP-015
- Operator runbook: `./account-identity-pilot-operator-runbook.md`
- Attempt-8 block (the problem this manifest solves): `../evidence/iap015/iap015-execute-attempt-20260723.md`

---

## 0. Gate 0 decision + preflight gates (record before anything runs)

### 0.1 Gate 0 backend decision — SETTLED
- **Selected provider backend: Steamworks (exactly one).** Grounded in
  `account-identity-pilot-gate0-evidence.md` §1: shipped transport seam reads the authenticated
  Steam socket host id; `VanillaAdminIdentity.DefaultPlatform = "Steam"`.
- **PlayFab is NOT admitted.** A PlayFab-namespace subject rejects `ProviderUnsupported`
  (`AT-AIP-PROVIDER-NAMESPACE`). Do not attempt a PlayFab join; it is out of scope by decision.
- Consequence for topology: `-crossplay` routes clients through the PlayFab/Steam **relay**; the
  reviewed live join path is **direct-Steam** (`ZdoAuthenticatedSenderSource.PeerForRpc` over the
  direct per-peer `ZRpc`). The two are incompatible on this box — see §0.3.

### 0.2 Accepted candidate pins (parity target — verify EXACT before join)
The QA fixture and the joining client MUST both carry the exact accepted-candidate binaries
(squash-merge `e1bec2d7` on `main`, PR #416):

| Assembly | SHA-256 |
|---|---|
| `SBPR.Trailborne.dll` | `a93cacc30d11f9f4fabf342c3c681cbc72915c94b733c9b6ff4da021157a75f5` |
| `SBPR.Trailborne.Core.dll` | `080733d27988286e7dd923ad7cf4e5e3ab71e62d328c1315d926dd8a0ad72abe` |
| `SBPR.Niflheim.HomesteadStones.dll` | `e6daaaf71265d2afbd63a28448be5fde92d5739de04f462a8da08ffabd9d3a3e` |

Standing `niflheim-server`'s in-container HomesteadStones DLL is currently `e6daaaf71265…`
(verified 2026-07-23). The QA lane MUST be staged from the identical binaries; parity is
`sha256sum`-checked in Step P3 and is a hard abort gate.

### 0.3 The topology block this manifest resolves (attempt 8)
Standing `niflheim-server` runs `SERVER_ARGS=-crossplay -preset hardcore` (verified via
`docker inspect`). The direct-Steam modded GUI client's `127.0.0.1:2456` dial times out `5003`
against the crossplay relay **before any SBPR admission code runs** (zero server-side admission
activity, no account minted). The full `sbpr_pilotop` cycle only works on an **isolated
non-crossplay lane** (proven by smoke `t_7c8bdea4`), which is a throwaway, not parity-staged.

**Resolution adopted by this manifest — Option (a):** stand up a **dedicated, non-crossplay,
parity-staged Niflheim-equivalent QA lane** as the real dedicated-server target for the whole
journey, then tear it down. This does NOT touch standing Niflheim and does NOT consume its
first-bind state.

### 0.4 Gate dispositions — SETTLED from the completed environment card `t_52f12248`
All six preflight gates are now closed by the authoritative, completed dedicated-environment card
`t_52f12248` (assignee `starbright-engineering`, run 1458, READY_FOR_EXECUTE) and its
`ENVIRONMENT-REPORT.md` (SHA-256 `9aee6e52a4b3028355b6bdf203fc61d51085f331651845493918f78228313315`).
These are not open owner questions; they record the environment that was actually built and
verified. No standing Niflheim state was consumed and no new owner gate is introduced.

- **OPEN-1 (topology ratification) — CLOSED: Option (a).** The prepared fixture is an isolated,
  dedicated, non-crossplay, parity-staged Niflheim clone (container `niflheim-iap015-t52-server`,
  join `127.0.0.1:2486`, status `127.0.0.1:9014`, `SERVER_ARGS=-preset hardcore`, no `-crossplay`).
  Standing Niflheim is **never** flipped off `-crossplay`; it was not modified or writable-shared
  (`ENVIRONMENT-REPORT.md` §"Safety decision"; card comment 1784800860). Option (b) is rejected.
- **OPEN-2 (joining Steam identity / adminlist) — CLOSED: validated valbot QA Steam identity.** The
  isolated admin/operator identity is the **distinct licensed `valbot` QA Steam account** (Steam
  logged on, subscribed to Valheim, self-owned, not family-shared), NOT the standing box's regular
  login. `adminlist.txt` on the QA lane contains exactly that validated valbot QA Steam identity
  and is mode 0600. The **raw SteamID is withheld** from this manifest and all published evidence
  (`ENVIRONMENT-REPORT.md` §"Client and identity confinement"). Wherever an admin id would appear
  in a command below, use the adminlist file by path — do not paste a raw Steam id.
- **OPEN-3 (second-Steam-identity for a live concurrent join) — CLOSED: approved Option-B split
  evidence, no second GUI identity.** A genuine two-GUI-client concurrent same-account login is not
  physically producible on this single-licensed-client box, and by design Steam kicks the first
  session (AIP-FR-028). `AT-AIP-DEDICATED-SECOND-SESSION-REJECT` is satisfied as split-evidence: the
  **harness half already PASSed** (attempt 7, 18/18, exact-binary direct-peer
  `qa-split-session-harness`) and the **live half** is the single-client transport→auth→admission→
  mint wiring covered by JOIN. No second Steam identity is created or fabricated.

---

## 1. Fixture identity, naming, and prohibitions

### 1.1 Fixed facts (verified this run)
| Fact | Value | Source |
|---|---|---|
| Provider backend | Steamworks | Gate 0 §1 |
| World name | `niflheim` | `niflheim.fwl` header |
| **Exact server seed** | `ForTheWort` | `od -c niflheim.fwl` → `n i f l h e i m \n F o r T h e W o r t` |
| World modifiers | `playerdamage 70`, `enemydamage 200`, `eventrate 60`, `deathskillsreset`, `deathdeleteitems`, `nomap`, `nobossportals`, `enemyspeedsize 120`, `enemylevelup­rate 140`, `preset hardcore` | `niflheim.fwl` strings |
| Standing container | `niflheim-server` (UDP 2456–2458, `-crossplay -preset hardcore`) | `docker inspect` |
| Standing admin Steam id | `76561197965627562` | `/config/adminlist.txt` |
| Candidate HomesteadStones DLL | `e6daaaf71265…` | in-container `sha256sum` |

### 1.2 Isolated QA identity naming — MANDATORY
- Local character/profile name: **`ForTheWort_QA`** (derived as `<exact server seed>_QA`).
- Second sequential profile (for `AT-AIP-DEDICATED-SECOND-PROFILE`): **`ForTheWort_QA2`**
  (`<seed>_QA` family; a distinct local character on the same account). **OPEN-4 — CLOSED:** the
  `_QA2` suffix is the ratified sibling local profile name (env card `t_52f12248`
  `ENVIRONMENT-REPORT.md` §"Client and identity confinement" reserves exactly `ForTheWort_QA2`
  for the second profile). Do not use `ForTheWort_QA_b` or any other name; use only
  `ForTheWort_QA*`, never Pololol or a regular character.
- The pilot **AccountId** is opaque and server-minted; it is NOT a chosen name.

### 1.3 Hard prohibitions
- **NEVER** join with `Pololol` or any pre-existing/regular Valheim character or profile.
- **NEVER** reuse a pre-existing pilot account; the journey must mint a fresh account on the QA
  lane's clean world fixture.
- **NEVER** run the journey against standing Niflheim's live world in a way that mints/mutates a
  pilot account on it. (Preflight/parity reads are fine; the actual bind/disable journey runs on
  the dedicated QA lane only.)
- Only `ForTheWort_QA*` local character profiles may exist client-side during the run; remove them
  on teardown.

---

## 2. Preflight — stand up the dedicated non-crossplay parity lane (Option a)

> **§2 provenance note — this fixture already exists (env card `t_52f12248`).** The dedicated
> non-crossplay lane described below was already stood up and verified by `t_52f12248`
> (`ENVIRONMENT-REPORT.md`); the steps here are its reproducible specification, not a fresh build to
> improvise. Reuse the prepared instance
> `/home/polyphonyrequiem/valheim/niflheim-iap015-t_52f12248`, container
> `niflheim-iap015-t52-server`, join `127.0.0.1:2486`, status `127.0.0.1:9014`. Standing
> `niflheim-server` is NOT touched and its first-bind state is NOT consumed.
>
> **OPEN-5 — CLOSED: use the UID-preserved isolated writable clone + pristine pilot store from
> `t_52f12248`.** The world fixture is the byte-identical isolated writable clone of the accepted
> `niflheim` world — world `niflheim`, seed `ForTheWort` (numeric `-756187396`), **World UID
> `196451874500733651` preserved** — NOT a regenerated-from-seed world and NOT the standing
> writable state. The pilot store is pristine: `pilot-account.journal` is absent and only the fresh
> owner-only mode-0600 lookup key exists, so the first authenticated join remains the first bind.

Correlation id for the whole run: set `RUN=iap015-$(date -u +%Y%m%dT%H%M%SZ)` and stamp every
artifact filename and log grep with it.

Artifact root: `~/repos/_kanban-artifacts/t_7bc3b94a-iap015-dedicated/$RUN/` (create it;
`_kanban-artifacts/` currently does not exist — `mkdir -p`).

### P0 — record environment baseline
```
mkdir -p ~/repos/_kanban-artifacts/t_7bc3b94a-iap015-dedicated/$RUN
docker ps --format '{{.Names}}\t{{.Ports}}' > .../$RUN/00-docker-baseline.txt
docker inspect niflheim-server --format '{{range .Config.Env}}{{println .}}{{end}}' \
  | grep -iE 'SERVER_ARGS|WORLD|NAME|PUBLIC' > .../$RUN/00-standing-env.txt
```
Expected: standing env shows `-crossplay`. This is the recorded proof the standing fixture is the
non-joinable one and is left untouched.

### P1 — create the QA lane container (non-crossplay)
Bring up a second `lloesche/valheim-server` container, **without `-crossplay`**:
- name: `niflheim-qa-$RUN` (a distinct name; never `niflheim-server`)
- `SERVER_NAME=NiflheimQA`, `WORLD_NAME=niflheim-qa`, `SERVER_PUBLIC=false`,
  `SERVER_ARGS=-preset hardcore` (**no `-crossplay`**), a QA-only `SERVER_PASS` (record it in the
  redacted run log per §4, not in plaintext committed files).
- host UDP port block distinct from standing (standing uses 2456–2458). Use **2496–2498/udp**
  (the block the attempt-8 smoke used) or the next free block; record the exact chosen port.
- mount a FRESH config dir (e.g. `~/valheim/niflheim-qa-$RUN/config`) so no standing state is
  shared.
- **OPEN-6 — CLOSED: exact invocation semantics come from `t_52f12248`'s `ENVIRONMENT-REPORT.md`.**
  The lane runs image `lloesche/valheim-server@sha256:20fde516ce311e6084f82f295c9eb6934af57b357c657937a04f62bdf5946149`
  as compose project `niflheim-iap015-t52`, container `niflheim-iap015-t52-server`, with
  `SERVER_NAME=Niflheim-IAP015-QA`, world `niflheim`, `SERVER_PUBLIC=false`,
  `SERVER_ARGS=-preset hardcore` (**no `-crossplay`**), game bind `127.0.0.1:2486-2488/udp`
  (join `127.0.0.1:2486`), status bind `127.0.0.1:9014/tcp`, `TZ=UTC`, restart policy `no`,
  auto-update/restart/backups disabled for deterministic evidence. The `SERVER_PASS` lives only in
  a mode-0600 `.env` and is **never printed or committed** — reference it as `<qa-pass>`. Join /
  restart / rollback / pre-run invocations are the exact commands in `ENVIRONMENT-REPORT.md`
  §"Baseline backup and tested rollback", §"Restart evidence instructions", and the
  `scripts/pre-run-guard.sh` gate — copy them verbatim; do not guess flags.

Expected observable: `docker logs niflheim-qa-$RUN` reaches `Game server connected` and, once the
mod loads, the operator-surface arm line (see P4).

### P2 — seed the world as `ForTheWort`
Ensure the QA world generates from **seed `ForTheWort`** (verify post-boot with
`od -c .../niflheim-qa.fwl` → the `ForTheWort` token must appear exactly as in §1.1). Abort if the
seed differs.
```
docker exec niflheim-qa-$RUN sh -c 'od -c /config/worlds_local/niflheim-qa.fwl' \
  > .../$RUN/02-qa-seed.txt
grep -q ForTheWort .../$RUN/02-qa-seed.txt || echo "ABORT: seed mismatch"
```

### P3 — parity-stage the accepted candidate binaries (HARD GATE)
Copy the three accepted-candidate DLLs (from standing `niflheim-server`'s verified plugin dir or
the merged-`main` build) into the QA lane's plugin dir, then verify SHA-256 EXACT against §0.2:
```
for d in SBPR.Trailborne.dll SBPR.Trailborne.Core.dll SBPR.Niflheim.HomesteadStones.dll; do
  docker exec niflheim-qa-$RUN sh -c "find /config/bepinex/plugins /opt/valheim/bepinex -name $d -exec sha256sum {} \;"
done > .../$RUN/03-qa-parity.txt
```
Compare every hash to §0.2. **ABORT** if any DLL differs — a non-parity fixture invalidates the
evidence. Also verify the joining client tree
`~/.local/share/Trailborne/Valheim-Modded/BepInEx/plugins` carries the same three hashes; record
in `03-client-parity.txt`.

### P4 — arm + confirm the operator surface, set adminlist
```
# adminlist: the validated valbot QA Steam identity (OPEN-2), one per line, mode 0600.
# Raw SteamID withheld — the QA lane's adminlist.txt already contains exactly this identity
# (env card t_52f12248). Reference it by path; do not paste a raw Steam id into artifacts.
docker restart niflheim-iap015-t52-server   # if adminlist changed
docker logs niflheim-iap015-t52-server 2>&1 | grep -iE 'Operator surface|provider=' \
  > .../$RUN/04-operator-arm.txt
```
Expected (must all be present):
```
Operator surface conformance: console=REGISTERED, server-request-handler=BOUND, client-reply-handler=BOUND
✓ Operator surface armed
provider=Steam
```
**ABORT** if any of the three surface bindings is missing or `provider` ≠ `Steam`.

### P5 — open the pilot for the QA world fixture
Join as admin (see §2 join procedure) then, from the client console:
```
sbpr_pilotop open-pilot
# expect reply: v1|<corr>|open-pilot|ok|PilotOpen|pilot=pilot-xxxxxxxx|admits=true
```
Record the reply + `pilot-…` id in `05-open-pilot.txt`. This catalogs the QA world save as the
fail-closed admission fixture. Idempotent.

---

## 3. Journey → acceptance evidence matrix

For EVERY step: capture (1) the exact command/action, (2) client `LogOutput.log` window
(`~/.local/share/Trailborne/Valheim-Modded/BepInEx/LogOutput.log`), (3) QA-lane server log window
(`docker logs niflheim-qa-$RUN`), (4) UTC timestamp range, (5) the `sbpr_pilotop` reply line with
its `<corr>` correlation id. Save each step's evidence as `NN-<step>.txt` under the `$RUN` dir and
list them in the checklist §5. Absence of server-side admission activity where a mint is expected
is an ABORT (that was the attempt-8 failure signature).

**Common join procedure (used by JOIN / RECONNECT / SECOND-PROFILE / RESTART):** launch the modded
GUI client via `boot-qa-client.sh` to the FejdStartup menu (GABS :8080; escape the first-scene
activation deadlock as in attempt 8 — retry until `MENU_REACHED scene=start`), set the local
profile to `ForTheWort_QA` (never Pololol), add server `127.0.0.1:<qa-port>`, supply the QA
password via `FejdStartup.ServerPassword`, and invoke join with `hasServerToJoin=True`.

| # | Journey step | Acceptance ID | Exact action | Expected observable (PASS) | Abort/fail condition |
|---|---|---|---|---|---|
| J1 | First authenticated Steam join → auto-create account + `ForTheWort_QA` character; do one progression op | **AT-AIP-DEDICATED-JOIN** | Join per common procedure as `ForTheWort_QA`; then perform one creator-bearing world op (place a piece) | Server log: `Got connection` → handshake → session-admission → opaque `AccountId` minted → `ForTheWort_QA` character mint + `CharacterCreated` receipt; client spawns in-world. `sbpr_pilotop inspect acct-…` → `status=Active|creds=1|classes=Steam|live=true` | `5003` transport timeout (crossplay leak — means lane misconfigured); zero server admission activity; any raw subject in logs |
| J2 | Logout, reconnect → same internal ids | **AT-AIP-DEDICATED-RECONNECT** | Disconnect cleanly; rejoin as `ForTheWort_QA` | Reconnect resolves the SAME `AccountId` + `CharacterId` (no new mint); `outcome=Resolved` path; `inspect` shows unchanged account/rev family | A second account minted on reconnect; character re-mint; id drift |
| J3 | Second sequential profile on the same account | **AT-AIP-DEDICATED-SECOND-PROFILE** | Disconnect; set local profile `ForTheWort_QA2`; rejoin | A DISTINCT `CharacterId` minted under the SAME `AccountId`; both characters listed in the account's membership; cross-account reuse impossible | Second profile mints a new account; character bound to wrong account |
| J4 | Concurrent same-account session rejection (SPLIT evidence) | **AT-AIP-DEDICATED-SECOND-SESSION-REJECT** | (a) Live half: confirm the single-client JOIN wiring (J1) drove real transport→auth→admission→mint. (b) Harness half: **re-run** exact-binary `qa-split-session-harness` against the candidate HomesteadStones DLL (`e6daaaf71265…`), attesting its SHA-256 before running | (a) J1 server-authoritative wiring green. (b) harness 18/18: two peers → one `AccountId`; second `TryReserve`/`Admit` rejects `AccountAlreadyConnected` BEFORE any character mint; first lease mints + releases on close; non-vacuity + attestation controls pass | Harness links a re-implemented/mocked/source-linked admission core (must link the SHIPPED candidate binary); SHA attestation mismatch; second reserve mints a character |
| J5 | Server restart → durable resolution | **AT-AIP-DEDICATED-RESTART** | `docker restart niflheim-qa-$RUN`; wait for boot-replay + operator arm; rejoin as `ForTheWort_QA` | Boot completes index/replay BEFORE admission opens; rejoin resolves the SAME `AccountId`/`CharacterId`; `open-pilot` re-catalogs the same fixture; no re-mint | Account not resolvable post-restart; admission opens before replay; fixture uncataloged → fail-closed unexpectedly |
| J6 | Live-admin inspect → disable → post-disable rejection | **AT-AIP-DEDICATED-DISABLE** | As admin, joined: `sbpr_pilotop inspect acct-…` then `sbpr_pilotop disable acct-…`; then attempt to reconnect as `ForTheWort_QA` | `inspect` → safe projection (ids/coarse status/provider CLASS only, no raw subject/HMAC). `disable` → `Disabled|outcome=Applied|sessionClosed=true|socketClosed=true`; REAL `ZNet.Disconnect` kicks the live session. Reconnect attempt rejects `AccountDisabled` with NO re-mint; status survives a further restart | Disable leaks a raw subject/HMAC; socket not closed; disabled account can still open a session or is silently re-minted |
| J7 | Operator runbook procedures execute end-to-end via shipped commands only | **AT-AIP-OPERATOR-RUNBOOK** | Walk the runbook (`account-identity-pilot-operator-runbook.md`) sections 2–7 on the QA lane: open-pilot, inspect, export, disable, delete + purge, retention-purge, close-pilot — using ONLY `sbpr_pilotop` (no journal hand-edits) | Each command returns its documented reply shape with a `<corr>` id; `export` catalogs a player-safe artifact (no secrets/subjects); `delete`+`purge` proves absence (compaction, not tombstone) and the wound-down barrier rejects same-subject re-join; `retention-purge` returns category counts only; `close-pilot` gates admission closed. Every reply captured with its correlation id | Any step requires editing `account-journal.bin`/`adminlist.txt`/HMAC key by hand; any `UnknownVerb` for an in-scope verb; a purge reported as a bare tombstone; any raw selector in a response |

> **Ordering note:** run J7's destructive tail (delete/purge/close-pilot) **last**, after J1–J6
> evidence is captured, because it winds the account/pilot down. J6's post-disable rejection must
> be captured before J7 deletes the account.

---

## 4. Redaction policy (reproducible without exposing credentials)

- **Never commit or print:** raw Steam subject/id in any pilot-log context, HMAC/digest values,
  the QA lane `SERVER_PASS`, tokens/tickets, or the contents of any `.fch`/credential file.
- **Do commit (reproducible):** opaque `acct-…`/`pilot-…`/`rcpt-…` ids, coarse statuses, result
  codes, `<corr>` correlation ids, safe counts, provider CLASS (`Steam`), DLL SHA-256s, seed
  token, world/container names, UTC timestamps, port numbers.
- The QA-lane admin Steam id `76561197965627562` appears in `adminlist.txt` by necessity; treat
  the adminlist file as operator-controlled and reference it by path in evidence, do not paste
  additional Steam ids into narrative artifacts.
- The QA password is recorded ONCE in an **uncommitted** local run note (mode `0600`, outside the
  repo, e.g. `~/valheim/niflheim-qa-$RUN/RUN-SECRETS.txt`) and referenced as `<qa-pass>` in
  committed artifacts. Redaction preserves reproducibility because every command uses the
  placeholder + a documented retrieval path, not the literal.
- Scrub each captured log window through the Gate-0 forbidden-value check before committing: grep
  for the raw Steam id and any `hmac`/`ticket`/`token` token; a hit is an ABORT-and-scrub, not a
  commit.

---

## 5. Evidence checklist (executing worker fills in)

Preflight:
- [ ] `00-docker-baseline.txt`, `00-standing-env.txt` — standing server untouched, shows `-crossplay`
- [ ] OPEN-1..OPEN-6 dispositions confirmed as SETTLED per §0.4 / §1.2 / §2 (env card `t_52f12248`)
- [ ] `02-qa-seed.txt` — QA world seed == `ForTheWort`
- [ ] `03-qa-parity.txt` + `03-client-parity.txt` — all three DLL SHA-256 == §0.2
- [ ] `04-operator-arm.txt` — console REGISTERED + both handlers BOUND + `provider=Steam`
- [ ] `05-open-pilot.txt` — `PilotOpen|admits=true`, `pilot-…` id recorded

Journey (each with command + client log + server log + UTC range + `<corr>`):
- [ ] J1 `AT-AIP-DEDICATED-JOIN` — mint proof, `inspect Active`
- [ ] J2 `AT-AIP-DEDICATED-RECONNECT` — same ids, no re-mint
- [ ] J3 `AT-AIP-DEDICATED-SECOND-PROFILE` — distinct char, same account
- [ ] J4 `AT-AIP-DEDICATED-SECOND-SESSION-REJECT` — live-half wiring + harness 18/18 with SHA attestation
- [ ] J5 `AT-AIP-DEDICATED-RESTART` — durable resolution post-restart
- [ ] J6 `AT-AIP-DEDICATED-DISABLE` — safe inspect, real kick, post-disable `AccountDisabled` no re-mint
- [ ] J7 `AT-AIP-OPERATOR-RUNBOOK` — full runbook via shipped commands, delete/purge/close last

Teardown (zero residue — mirror attempt-8 discipline):
- [ ] QA-lane container stopped + removed; QA config dir removed (or archived per owner)
- [ ] All `ForTheWort_QA*` local `.fch` profiles removed from `~/.config/unity3d/IronGate/Valheim/characters_local/`
- [ ] Standing `niflheim-server` verified untouched: still up, DLL parity `e6daaaf71265…`, no journey account minted on it
- [ ] Redaction scrub passed on every committed artifact (no raw subject/HMAC/pass)

---

## 6. Abort conditions (stop and escalate, do not improvise)

- `5003` transport timeout on join → the QA lane still has crossplay/relay leakage; fix the lane,
  do not fall back to standing Niflheim.
- Any DLL SHA-256 ≠ §0.2 → non-parity fixture; re-stage.
- Operator surface not fully armed / `provider` ≠ Steam → do not run the journey.
- A raw Steam subject or HMAC appears in any pilot log/artifact → scrub, treat as a defect, file it.
- Any runbook step that would require hand-editing `account-journal.bin`, HMAC key, or
  `adminlist.txt` outside the documented commands → escalate (runbook §9).
- OPEN-1..OPEN-6 are SETTLED by env card `t_52f12248` (§0.4/§1.2/§2). No owner gate remains; the
  topology and admin-identity decisions were recorded, not left open — do not reopen them or
  fabricate a second identity/topology.
