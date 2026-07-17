---
title: "Niflheim account identity pilot — operator control runbook"
status: proposed
purpose: Executable operator runbook for the IAP-009 control foundation — local allowlist bootstrap, live-admin inspect/disable, deterministic session close, delete-drain with allowlist revocation, and recovery for failed drains and process death. No journal hand-editing.
---

# Operator control runbook — Niflheim account identity pilot (IAP-009)

**Responsible operator:** the named pilot ops owner (see the privacy inventory `OperatorContact`).
**Evidence:** [`../planning/account-identity-pilot-operator-evidence.md`](../planning/account-identity-pilot-operator-evidence.md)
**Contracts:** [`../planning/account-identity-pilot-contracts.md`](../planning/account-identity-pilot-contracts.md)

> **Golden rule:** every step below runs through a shipped operator command or the OS-scoped bootstrap
> utility. **Never hand-edit the account journal** (`account-journal.bin`) — it is a framed CRC journal;
> a manual edit corrupts the tail and quarantines records on the next boot. If a command will not do what
> you need, STOP and escalate; do not patch the journal.

> **Implementation status (IAP-009):** the operator DECISION LOGIC — admin authorization, inspect
> projection, disable/delete, the mutation fence + drain barrier, deterministic session close, allowlist
> provision/revoke, and the OS-ownership/no-echo/verb-scope boundary — is implemented and green
> (`OperatorAccountService`, `OperatorAdminGate`, `AccountMutationFence`, `PilotSessionRegistry`,
> `LocalAllowlistBootstrap`). The `pilot-op` / `niflheim-account-bootstrap` command names below are the
> operator SURFACE those cores back; the thin net48 host that binds real stdin/`stat`/console I/O to the
> cores is wired at the live-server integration in IAP-010. The commands are usable without hand-editing
> journals — every effect above is a core method, not a file edit.

## 0. Authority model (read first)

- **Account lifecycle** (inspect, disable, delete) requires the **live-server Valheim admin gate**: you must
  be connected as an authenticated admin whose host id is in the server's `adminlist.txt`. A gameplay
  payload can NEVER grant operator authority — there is no second admin path.
- **Allowlist provision/revoke** may ALSO be done by the **server-host service owner** through the local
  bootstrap utility, authenticated purely by OS ownership of the key/data path. That utility can do
  allowlist provision/revoke and NOTHING else (no inspect/disable/delete/export/reset/gameplay).

## 1. Bootstrap: add a tester to the allowlist (local, OS-scoped)

Preconditions: you are the server service account; the HMAC key/data path is owner-only (`0600` or
tighter). The utility fails closed otherwise.

```text
# On the server host, as the service account. The raw subject is typed into a
# PROTECTED NO-ECHO prompt — never passed as an argv/env value, never pasted into chat.
niflheim-account-bootstrap provision \
    --provider Steam \
    --backend <configured-backend-issuer> \
    --notice-version <current-notice-version>
# → prompts (no echo): "provider subject: "  ← type/paste the tester's Steam subject; it is not shown
# → prints: resultCode=Provisioned allowlistEntryId=allow-xxxxxxxx
```

The utility HMACs the subject immediately and discards it; the raw subject is never persisted or echoed.
Record only the returned `allowlistEntryId`. Rejections you may see (all fail closed, nothing written):

| resultCode | meaning | fix |
|---|---|---|
| `KeyPathTooPermissive` | the key/data path is group/other-reachable | `chmod 0600` the key path; re-run |
| `SubjectChannelForbidden` | the subject arrived off argv/env/chat | use the no-echo prompt only |
| `VerbOutOfLocalScope` | you tried a non-allowlist verb locally | use the live-admin path for lifecycle |
| `DisclosureIncomplete` | the disclosure/notice is not complete | complete the privacy inventory + notice |

Revoke an entry (allowlist-only, internal id — no subject needed):

```text
niflheim-account-bootstrap revoke --allowlist-entry allow-xxxxxxxx
# → resultCode=Revoked allowlistEntryId=allow-xxxxxxxx
```

## 2. Inspect an account (live admin)

```text
# Connected as an authenticated server admin:
pilot-op inspect --account acct-xxxxxxxx
# → accountId, status, revision, credentialCount, credentialClasses[], noticeVersion,
#   retentionPolicyVersion, hasLiveSession
```

Inspect is **safe by construction**: it returns internal ids, coarse status, and the provider CLASS only.
It never emits a raw subject, HMAC, secret, or token, and it cannot look an account up by raw subject.
A non-admin caller is rejected (`NotAdmin`) with no data returned.

## 3. Disable an account (closes admission + session)

Disable is the primary "stop this account now" control. It drains any in-flight mutation, atomically flips
`Active → Disabled`, then deterministically server-closes the live session.

```text
pilot-op disable --account acct-xxxxxxxx --op <unique-operation-id>
# → outcome=Applied resultCode=Disabled committedRevision=N sessionClosed=true
```

- After success, the account cannot open a new session (`AccountDisabled` on admission) and the durable
  status survives a server restart. A delayed network close from the old session cannot reopen authority —
  the account is already Disabled on disk before the socket close is issued.
- **Idempotent:** re-running the same `--op` returns `outcome=Replayed`. Re-running with a fresh op on an
  already-disabled account returns `resultCode=AlreadyDisabled` (NoOp) and re-closes any lingering session.

### Disabling during an in-flight mutation (drain)

You do not have to wait for a quiet moment. `disable` acquires the per-account mutation fence and waits for
any already-committing gameplay/account transaction to finish before it commits. If that drain does not
complete within the bounded timeout, the command aborts with `resultCode=DrainTimeout` and **changes
nothing** — the account stays Active and fully recoverable. Re-run when the stuck operation clears; if it
never clears, escalate (a genuinely wedged mutation is an incident, not a disable problem).

## 4. Delete an account (drain + allowlist revocation)

Delete is disable-plus: one terminal transaction commits `DeletionPending`, revokes every linked credential
AND its allowlist entry, and closes the session. The revoked allowlist means the same subject cannot
immediately re-enrol.

```text
pilot-op delete --account acct-xxxxxxxx --op <unique-operation-id>
# → outcome=Applied resultCode=DeletionPending committedRevision=N sessionClosed=true
```

Same drain semantics as disable: a failed drain aborts with `DrainTimeout` and no mutation. Same
idempotency: `Replayed` on the same op, `AlreadyClosing` (NoOp) on an already-pending account.

> Scheduled data/export/backup **purge** by the retention deadline, whole-fixture reset, and the pilot
> closure sweep are a later operator pass — this runbook covers the CONTROL surface (stop admission, fence,
> deterministically end the session, block recreation). Do not improvise a purge by editing files.

## 5. Recovery: failed drains and process death

- **Failed drain (`DrainTimeout`):** no state changed. Identify the stuck mutation (inspect + server logs),
  let it finish or treat a wedged mutation as an incident, then re-run the disable/delete. Never force it by
  editing the journal.
- **Process death mid-commit:** the account journal is Intent→Committed framed. A crash between the two
  writes leaves an Intent-only transaction that the next boot **quarantines** — the half-written lifecycle
  change never projects, so the account rehydrates to its last fully-committed state. Simply re-run the
  operator command with a fresh `--op` after restart; the store is already coherent.
- **Torn journal tail:** boot truncates and quarantines the torn bytes automatically (`QuarantinedTailBytes`
  is reported). No manual repair; re-run the command.
- **Session left dangling after a hard kill:** the session registry is process-local and cleared on restart,
  so a stale session cannot survive a reboot. On a live server, a repeat `disable`/`delete` re-closes any
  lingering session deterministically.

## 6. What to escalate (do NOT self-serve)

- A mutation that never drains (persistent `DrainTimeout`).
- Any need to remove data an account already committed (purge/reset) — later operator surface.
- Repeated `QuarantinedState` on boot — durable ambiguity needs an operator decision, not a journal edit.
- Anything that would require editing `account-journal.bin`, the HMAC key material, or `adminlist.txt` by
  hand outside the documented commands.
