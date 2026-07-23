---
title: "IAP-015 — Dedicated joined-client account-pilot journey: EXECUTE attempt 8 (crossplay/parity topology block)"
status: proposed
author: qa-playtest (t_8a3a55c6, run 1456)
date: 2026-07-23
purpose: >
  Second EXECUTE attempt, first after PR #416 (live operator command surface) merged to main
  (e1bec2d7) and staging t_c24ef3b4 returned READY_FOR_QA_WINDOW. The missing-operator-surface
  wall from attempt 7 is resolved. This attempt hit a NEW, empirically-confirmed wall: the
  parity-staged fixture (standing -crossplay Niflheim) is NOT joinable by the direct-Steam modded
  GUI client, and the only join-capable topology (an isolated NON-crossplay lane) is not the
  parity-staged artifact. This is a staging/topology mismatch that requires an OPERATE decision,
  not a QA action.
---

# IAP-015 EXECUTE attempt 8 — 2026-07-23 (run 1456)

## Preconditions verified this run (all green)
- **All gate dependencies cleared.** PR #416 (live IAP operator command surface) MERGED to main
  as squash commit `e1bec2d7d92f2361402ee85740e52ae6a5c5e9aa` (architect handoff, comment
  1784797888). Staging parity card `t_c24ef3b4` completed **READY_FOR_QA_WINDOW** pinned to
  `e1bec2d7`. Option B owner-approved. Privacy / provider-free / operator-control gates done.
- **Operator surface armed on standing Niflheim** (server boot 2026-07-23T02:15:50Z):
  `Operator surface conformance: console=REGISTERED, server-request-handler=BOUND,
  client-reply-handler=BOUND` / `✓ Operator surface armed`. `provider=Steam`. SpecCheck green.
- **Client parity holds exactly** against the e1bec2d7 candidate manifest (staging t_c24ef3b4):
  - `SBPR.Trailborne.dll` = `a93cacc30d11f9f4fabf342c3c681cbc72915c94b733c9b6ff4da021157a75f5`
  - `SBPR.Trailborne.Core.dll` = `080733d27988286e7dd923ad7cf4e5e3ab71e62d328c1315d926dd8a0ad72abe`
  - `SBPR.Niflheim.HomesteadStones.dll` = `e6daaaf71265d2afbd63a28448be5fde92d5739de04f462a8da08ffabd9d3a3e`
  Client tree `~/.local/share/Trailborne/Valheim-Modded/BepInEx/plugins` — all three verified.
  Standing Niflheim in-container DLL verified `e6daaaf71265…` (same candidate). Parity confirmed.
- **GUI environment is present and functional.** GABS (:8080) live; modded client launched to the
  FejdStartup menu via `boot-qa-client.sh` (escaped the first-scene activation deadlock on
  attempt 1, `MENU_REACHED scene=start|Y`); C# UnityScriptHost bridge (48210) responsive. The
  environment is NOT the blocker — a joined GUI client is mechanically launchable here.

## What was executed this run
Drove a real join attempt from the live menu client to the **parity-staged standing Niflheim**
(`127.0.0.1:2456`, world `niflheim`/seed `ForTheWort`, password `qwertyuiop`), using a QA-only
`ForTheWort_QA` profile (never Pololol/regular) and `FejdStartup.ServerPassword` for the
handshake. The join was invoked with `hasServerToJoin=True`.

### RESULT — BLOCKED: direct-Steam join to the -crossplay fixture times out pre-handshake
Client log (`~/.local/share/Trailborne/Valheim-Modded/BepInEx/LogOutput.log`), 02:31:23–02:31:35:
```
Determined backend of dedicated server to be Steamworks
Added server 127.0.0.1:2456 to server list
Connecting to server with Steam-backend 127.0.0.1:2456:2456
Starting to connect to 127.0.0.1:2456
Got status changed msg k_ESteamNetworkingConnectionState_Connecting
New connection
Got status changed msg k_ESteamNetworkingConnectionState_ProblemDetectedLocally
Got problem 5003:Timed out attempting to connect
Lost connection to server:ErrorConnectFailed
```
Server side: **zero** admission activity in the window — no `Got connection`, no handshake, no
`session-admission`, no `ForTheWort` character, **no account minted, no journal mutation.** The
connection died at the SteamNetworkingSockets transport layer BEFORE any SBPR admission code ran.

This is the **exact incompatibility the pre-merge smoke already documented** (worker comment,
t_7c8bdea4 lane creation, `1784789240`): *"standing Niflheim's crossplay relay is incompatible
with the reviewed direct-Steam join path."* Standing Niflheim runs `SERVER_ARGS=-crossplay
-preset hardcore` (`~/valheim/niflheim/.env`). `-crossplay` routes clients through the PlayFab/
Steam relay backend; the local modded client's direct-Steam `127.0.0.1:2456` dial cannot complete
against it and times out `5003`. The pre-merge runtime smoke (t_7c8bdea4) therefore PASSed its
join/inspect/disable/reconnect cycle only on a **purpose-built isolated NON-crossplay lane**
(container `niflheim-pr416-smoke-…`, host UDP 2496), never against standing Niflheim.

## Why this is a real block, not a self-resolvable QA step
The staging card `t_c24ef3b4` parity-staged the e1bec2d7 operator surface onto **standing
-crossplay Niflheim** and returned READY_FOR_QA_WINDOW — but that fixture is **not joinable** by
the modded GUI client this journey requires. The two facts are in tension:

1. **Parity-staged artifact = standing Niflheim (-crossplay)** → operator surface armed, DLL parity
   exact, BUT direct-Steam GUI join times out `5003`. Cannot run the six live-joined-GUI ATs here.
2. **Join-capable topology = isolated NON-crossplay lane** → the modded GUI client joins and the
   full `sbpr_pilotop` cycle works (proven by smoke t_7c8bdea4), BUT that lane is a throwaway
   container, NOT the parity-staged artifact, and it is torn down after each smoke.

Executing the IAP-015 journey requires ONE fixture that is BOTH parity-staged to `e1bec2d7` AND
join-capable by the direct-Steam modded GUI client. Neither existing option is both. Resolving
this requires an OPERATE decision on the QA-window topology — either:
  (a) stand up a **dedicated, non-crossplay, parity-staged** Niflheim-equivalent pilot server
      (seed `ForTheWort`, e1bec2d7 operator surface, adminlist for the joining Steam identity,
      QA-only `ForTheWort_QA`), persisted for the full journey (JOIN → RECONNECT → SECOND-PROFILE
      → SECOND-SESSION-REJECT live half → RESTART → DISABLE → OPERATOR-RUNBOOK), then teardown; OR
  (b) an owner decision to temporarily flip standing Niflheim off `-crossplay` for the QA window
      (affects the live fixture — an owner/product call, not a QA action), then restore.

Neither is a fixture I can allocate under this EXECUTE card's scope without an OPERATE/owner
decision on which topology the acceptance journey runs against and how the joining Steam identity
is admitted to that server's adminlist.

## Teardown / restoration (this run left zero residue)
- Client stopped via GABS `games_stop` (`stopped successfully`); no live valheim.x86_64 process
  remains (only a transient zombie awaiting reap); USH 48210 / GABP 491xx listeners GONE.
- Local `ForTheWort_QA.fch` profile file (created client-side by SetProfile, never joined) removed
  from `~/.config/unity3d/IronGate/Valheim/characters_local/`. No residual `ForTheWort_QA*` files.
- **Zero server-side effect:** connection died pre-handshake, so no account/character/journal
  mutation occurred on Niflheim. No purge needed.
- Standing services untouched and up: niflheim / heistan / homestead-t009l. Standing Niflheim
  DLL parity unchanged (`e6daaaf71265…`). GABS :8080 preserved. Steam preserved.

## Standing evidence status (unchanged, still valid)
- **Harness half of AT-AIP-DEDICATED-SECOND-SESSION-REJECT: PASS** (attempt 7, run 1426 —
  exact-binary direct-peer `qa-split-session-harness`, 18/18, non-vacuity + attestation controls).
- **Mechanism proof for the six live ATs: PASS in principle** on the isolated non-crossplay lane
  (smoke t_7c8bdea4: join as `ForTheWort_QA` → opaque AccountId mint → `sbpr_pilotop inspect` →
  `disable` durable-commit-before-socket-close → reconnect `AccountDisabled` zero re-mint → clean
  teardown). What remains unproven is the SAME journey against a PARITY-STAGED fixture as the
  formal acceptance evidence, which is blocked by the topology mismatch above.
