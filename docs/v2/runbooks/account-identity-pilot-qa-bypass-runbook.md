---
title: "T022 QA-only ephemeral account bypass — isolated HomesteadT009L runbook"
status: proposed
purpose: >
  Executable runbook for the T022 QA-only ephemeral account bypass — the narrowest
  test-only admission adapter that admits configured server-observed Steam peers into
  Homestead gameplay under EPHEMERAL opaque QA account/character identities on the
  isolated HomesteadT009L fixture, so canonical T022 can run live WITHOUT provisioning
  the pilot account store. TEST INFRASTRUCTURE, never production. Default OFF; a
  conjunction of server-owned gates. Rollback = disable the flag; no durable state.
---

# T022 QA-only ephemeral account bypass — isolated HomesteadT009L

> **⚠️ TEST INFRASTRUCTURE, NEVER PRODUCTION ARCHITECTURE.** This bypass exists ONLY to
> run canonical T022 on the isolated HomesteadT009L fixture. It admits configured Steam
> peers under throw-away, process-local opaque identities and writes **nothing** durable.
> It is not, and must never become, the production account-admission path. Production
> admission remains the shipped [`PilotAccountService`](../planning/account-identity-pilot-contracts.md)
> first-bind + allowlist path.

**Scope decision (Daniel, supersedes live-store provisioning gate `t_63e803b9`):** rather than
provisioning the real valbot subject into the pilot account store, add a QA-only account bypass.
This runbook is that bypass. Do **not** provision the account journal and do **not** touch
production Niflheim / Heistan to run T022.

**Contracts / spec:** [`../planning/account-identity-pilot-qa-bypass-spec.md`](../planning/account-identity-pilot-qa-bypass-spec.md)
**Live-admission seam:** `src/SBPR.Niflheim.HomesteadStones/Features/PilotIdentity/PilotSessionLifecycleObserver.cs`
**Engine-free core (unit-tested):** `src/SBPR.Niflheim.HomesteadStones/Application/Runtime/QaAccountBypass.cs`

## What it does (and does not do)

When — and ONLY when — every gate below passes, the live-admission observer routes admission
through the QA bypass instead of the normal live path:

- A configured, authenticated, **server-observed** Steam peer is admitted into Homestead
  gameplay under a fresh **ephemeral opaque** `AccountId` + `CharacterId` (≥128-bit CSPRNG).
- Distinct Steam peers receive distinct opaque accounts; distinct profiles receive distinct
  opaque characters; the ids are stable for the live QA session and **do not survive restart**.
- The bypass grants the **Homestead gameplay principal only**. It does **not** grant Valheim
  admin — the t009l adminlist remains a separate exact-ID operator step.

It never: calls `PilotAccountService` first-bind, requires a `PilotAllowlistEntry`, fabricates a
disclosure acknowledgement, appends an account/credential/character/disclosure record, or emits a
raw Steam subject / HMAC. The only marker it logs is a subject-free
`[qa-account-bypass] admitted account=acct-… character=char-… session=sess-… result=Admitted`.

## The conjunction gate (all must hold — default OFF)

The bypass activates only when the engine-free `QaAccountBypassGate.Evaluate` returns `None`,
which requires **every** one of:

1. `[QaAccountBypass] EnableQaAccountBypass = true` (default `false`).
2. `[QaAccountBypass] EnvironmentTag` equals exactly `homestead-t009l`.
3. `[QaAccountBypass] ExpectedWorldName` equals the world the server actually loaded
   (server-observed off `ZNet.GetWorldName()`).
4. `[QaAccountBypass] ExpectedDataRoot` equals the durable directory the Foundational runtime
   actually composed (server-observed).
5. `[QaAccountBypass] AllowlistedSteamIds` is a non-empty, canonical (decimal), wildcard-free set
   of server-observed SteamID subjects. An empty set, a `*`, `0`, or any non-numeric entry refuses
   the whole set.
6. No production marker (`niflheim`, `heistan`, case-insensitive substring) appears in the
   environment tag or the configured/observed world name — any such marker is a **hard refuse**.

If any gate fails, the bypass stays OFF and normal admission (including `NotAllowlisted`) runs
**bit-for-bit unchanged**. The observer logs `[qa-account-bypass] inactive (gate=<reason>)`.

## Enable (isolated T009L only)

1. On the isolated T009L dedicated server, edit the BepInEx config
   `BepInEx/config/net.danielgreen.sbpr.niflheim.homesteadstones.cfg`:
   ```ini
   [QaAccountBypass]
   EnableQaAccountBypass = true
   EnvironmentTag = homestead-t009l
   ExpectedWorldName = HomesteadT009L        # the EXACT world the T009L server loads
   ExpectedDataRoot = /path/to/t009l/config/sbpr-niflheim-homestead/HomesteadT009L-<uid>
   AllowlistedSteamIds = <primary-steamid>, <valbot-steamid>
   ```
   `ExpectedDataRoot` is the world-scoped durable directory the server logs at boot as
   `Live session admission composed … durable='…'`. `AllowlistedSteamIds` are the exact
   server-observed Steam subject ids (decimal), separated by commas or spaces.
2. Restart the T009L dedicated server.
3. Confirm activation in the log:
   `[qa-account-bypass] ACTIVE — TEST INFRASTRUCTURE, NOT PRODUCTION. …`. If instead you see
   `inactive (gate=…)`, the named gate did not match — fix that config value and restart.

## Verify a QA admission

1. Join the T009L server as a configured allowlisted Steam peer and pick a profile.
2. The server log prints the subject-free marker:
   `[qa-account-bypass] admitted account=acct-… character=char-… session=sess-… result=Admitted`.
3. The gameplay path now resolves the ephemeral bound principal for that peer, so canonical T022
   commands run. Two distinct allowlisted peers produce two distinct `acct-…` values.

## Grant Valheim admin (SEPARATE step — not part of the bypass)

The bypass never grants Valheim admin. For T022 commands that require the Valheim adminlist
(e.g. server-admin-gated provisioning), add **only** the server-observed valbot SteamID to the
t009l `adminlist.txt` and restart — the exact-ID operator step called out in the task delivery.

## Rollback

Set `EnableQaAccountBypass = false` (or clear any gate) and restart. Normal `NotAllowlisted`
admission resumes immediately. There is **no durable state to clean** — every QA mapping is
in-memory and dropped on restart.
