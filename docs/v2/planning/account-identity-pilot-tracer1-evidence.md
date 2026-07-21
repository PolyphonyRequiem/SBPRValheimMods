---
title: "IAP-003 — Tracer 1: account + credential foundation evidence"
status: proposed
purpose: Executable Tracer-1 evidence for the Niflheim cooperative-pilot account identity — HMAC-only disclosure-aware allowlist, server-minted AccountId, atomic account+credential first bind, framed lifecycle journal, boot rehydration, indexed lookup, active/previous key handling with version census and safe retirement, and explicit no-name/no-auto-merge behavior. Engine-free CLEAN-side core.
---

# IAP-003 — Tracer 1: account + credential foundation evidence

**Spec:** [`account-identity-pilot-spec.md`](account-identity-pilot-spec.md) (AIP-FR-002..008, AIP-FR-016/017)
**Plan:** [`account-identity-pilot-plan.md`](account-identity-pilot-plan.md) → "Tracer 1 — Account + credential vertical slice"
**Data model:** [`account-identity-pilot-data-model.md`](account-identity-pilot-data-model.md)
**Contracts:** [`account-identity-pilot-contracts.md`](account-identity-pilot-contracts.md) → "ResolveOrCreatePilotAccount", "ProvisionPilotAllowlistEntry"

> **Scope:** This is the Tracer-1 account+credential vertical slice. It mints provider-independent
> `AccountId`/`CredentialBindingId` values, HMACs the transient provider subject, journals the account and
> credential atomically, rehydrates indexes at boot, and resolves on reconnect/restart. It does **not** mint
> characters (Tracer 2) or migrate gameplay receipts / remove `PlatformId` from durable digests (Tracer 3).
> The accepted Homestead identity-row supersession lands with Tracer 3, not here.

## Source (engine-free CLEAN-side; ships under net48, link-compiled under net8)

| File | Role |
|---|---|
| `Domain/Accounts/PilotAccountIdentifiers.cs` | Opaque `PilotAccountId`/`CredentialBindingId`/`AllowlistEntryId`/`AccountReceiptId`/`LookupKeyVersion` + `OpaqueIdMint` (128-bit CSPRNG) |
| `Domain/Accounts/PilotDisclosure.cs` | Privacy inventory (human-approved lawful-basis position), disclosure completeness, acknowledgement |
| `Adapters/Identity/LookupKeyRing.cs` | Versioned domain-separated HMAC-SHA-256; ≥256-bit keys; active/previous ring; fail-closed on missing version |
| `Persistence/Accounts/PilotAccountStore.cs` | Framed (len+CRC32+fsync) account journal; Intent→Committed atomic transaction; boot-rehydrated credential/allowlist indexes; torn-tail + Intent-only quarantine; version census |
| `Persistence/Accounts/PersistedPiiScanner.cs` | Mechanical raw-subject/token scan of the on-disk journal (raw bytes + deep-decoded fields) |
| `Application/Accounts/PilotAccountService.cs` | `ProvisionAllowlistEntry`, `ResolveOrCreateAccount` (atomic first bind, reconnect, first-bind race, previous-key + multi-hop lazy re-key, conflict/replay) |

Executable evidence: `tests/NiflheimPilotAccountFoundationTests.cs` (25 tests). `dotnet test` green
(823/823 total suite), net8 `TreatWarningsAsErrors` clean; the mod project builds under net48 with the
Valheim SDK, **0 warnings / 0 errors**.

## Acceptance coverage

| Acceptance ID | Where proven |
|---|---|
| `AT-AIP-FIRST-BIND` | one account + one binding minted atomically; no raw subject on disk |
| `AT-AIP-INTERNAL-ID-ENTROPY` | 128-bit (32-hex) opaque ids, 5000-sample uniqueness, non-provider-derived |
| `AT-AIP-ACCOUNT-RECONNECT` | reconnect (same process) and post-restart resolve the same account, no new mint |
| `AT-AIP-FIRST-BIND-RACE` | 16 concurrent first joins → exactly one account; losers resolve it |
| `AT-AIP-ACCOUNT-CREDENTIAL-ATOMIC` | crash after Intent leaves no partial account; reboot quarantines it; retry binds |
| `AT-AIP-HMAC-CANONICAL` | full 64-hex HMAC, domain separation, field-boundary non-collision, deterministic |
| `AT-AIP-ALLOWLIST-HMAC-ONLY` | provisioning stores HMAC not raw subject; refuses without complete basis |
| `AT-AIP-DISCLOSURE-COMPLETE` | all mandatory disclosure elements required; missing element fails |
| `AT-AIP-DATA-INVENTORY-BASIS` | human-approved lawful basis required per category; empty inventory never passes |
| `AT-AIP-FIRST-JOIN-AUTOCREATE` | first authenticated subject with no binding auto-mints exactly one opaque account+credential; no allowlist/disclosure record required or fabricated (supersedes retired `AT-AIP-NOT-ALLOWLISTED`) |
| `AT-AIP-UNKNOWN-CREDENTIAL-SEPARATE` | two subjects → two distinct accounts |
| `AT-AIP-NO-NAME-MERGE` | no auto-merge on resemblance; a distinct second subject mints its own distinct account |
| `AT-AIP-KEY-STRENGTH-SEPARATION` | <256-bit key rejected at construction; no raw-byte accessor exposed |
| `AT-AIP-KEY-MISSING-FAIL-CLOSED` | unknown key version throws `LookupKeyUnavailableException`; mandatory active key |
| `AT-AIP-PREVIOUS-KEY-REKEY` | previous-key match resolves and re-keys credential + linked allowlist in place, same `AccountId` |
| `AT-AIP-REKEY-MULTIHOP` | two sequential rotations keep `AccountId` stable; no live entries linger on retired versions |
| `AT-AIP-KEY-RETIREMENT-GATE` | census>0 blocks retirement; opens to zero after lazy re-key (`RunCensus`/`MayRetireKeyVersion`) |
| `AT-AIP-TORN-TAIL` | truncated frame quarantined (byte count reported); durable account still resolves |
| `AT-AIP-ACCOUNT-JOURNAL-RECOVERY` | 25 accounts rebuilt from journal alone on a fresh process; each reconnect resolves |
| `AT-AIP-OPERATION-CONFLICT` | same op id + different subject rejects; same op id + same subject replays idempotently |
| `AT-AIP-BOOT-BEFORE-ADMISSION` | credential index built in the store constructor; first post-boot lookup resolves |
| `AT-AIP-PERSISTED-PII-SCAN` | no forbidden subject/token/email on disk; scanner catches a seeded leak |
| `AT-AIP-INDEXED-10K` | 10,000 bindings; one boot replay; indexed post-boot resolution mints nothing |

> **Note on ID variants:** the IAP-003 card names `AT-AIP-KEY-RETIREMENT-GATE` and `AT-AIP-REKEY-MULTIHOP`;
> the spec coverage table lists the finer-grained `AT-AIP-KEY-VERSION-CENSUS` / `AT-AIP-KEY-RETIREMENT-BLOCKED`
> for AIP-FR-005. Both are realized here: the retirement-gate test asserts the census counts directly and the
> multi-hop test extends previous-key re-key across two rotations.

## What is deliberately NOT claimed

- No character minting, session admission, or creator bridge (Tracer 2).
- No gameplay-receipt migration; `PlatformId` still appears in the existing Homestead receipt path until
  Tracer 3, which is where the accepted Homestead data-model identity rows are reconciled and stamped.
  (Superseded: IAP-007 Tracer 3, t_c8c96581, has since removed `PlatformId` from the gameplay
  receipt/binding/log path — see account-identity-pilot-plan.md §"Tracer 3".)
- No live dedicated joined-client proof (final gate). This tracer is engine-free + net48-compile evidence;
  "logs green ≠ playable" still holds for the end-to-end journey.
