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

> **Implementation status (IAP-015):** the operator DECISION LOGIC (admin authorization, inspect
> projection, disable/delete, the mutation fence + drain barrier, deterministic session close, allowlist
> provision/revoke, privacy open/export/purge/close) is implemented and green, AND the **live net48
> operator command surface is now shipped and wired**: a joined server admin drives it through the client
> console command **`sbpr_pilotop`**, which sends a bounded request over a DIRECT per-peer `ZRpc` handler
> (`OperatorCommandIngressObserver`). The server resolves the ACTUAL delivering `ZNetPeer`, derives its
> admin authority from the authenticated socket host against the live `adminlist.txt`
> (`OperatorAdminGate` over `ZNet.GetAdminList()`), and runs the request through the exact shared
> `PilotAccountStore` / `PilotSessionRegistry` / `AccountMutationFence` / privacy + lifecycle services the
> live admission path uses (`LiveOperatorServices`, composed at `ZNet.Awake` in
> `PilotSessionLifecycleObserver`). A disable/delete that closes a session performs the REAL server-side
> socket close via `ZNet.Disconnect`. A client payload NEVER grants authority, and a `ZRoutedRpc` sender
> id is never trusted. The `niflheim-account-bootstrap` allowlist utility remains the OS-owner-scoped
> local surface. The commands are usable without hand-editing journals — every effect below is a shipped
> command, not a file edit.
>
> **Option B is preserved:** the exact-binary `qa-split-session-harness` half already PASSed; the six
> joined-GUI acceptance tests remain live requirements (logs-green here does NOT prove the joined-client
> experience — that is the dedicated-server gate's job).

## 0. Authority model (read first)

- **Account lifecycle** (inspect, disable, delete) requires the **live-server Valheim admin gate**: you must
  be connected as an authenticated admin whose host id is in the server's `adminlist.txt`. A gameplay
  payload can NEVER grant operator authority — there is no second admin path.
- **Allowlist provision/revoke** may ALSO be done by the **server-host service owner** through the local
  bootstrap utility, authenticated purely by OS ownership of the key/data path. That utility can do
  allowlist provision/revoke and NOTHING else (no inspect/disable/delete/export/reset/gameplay).

## 1. Bootstrap: add a tester to the allowlist (local, OS-scoped) — DEPRECATED

> **Deprecated / not required for normal admission.** Testers no longer need to be pre-added to an
> allowlist: the first authenticated Steam join auto-creates the opaque account. This bootstrap surface
> is retained only for compatibility/audit of existing entries and is not part of the normal join path.
> You can normally skip this entire section — just have the tester join.

Preconditions (if you still choose to provision a legacy entry): you are the server service account; the HMAC key/data path is owner-only (`0600` or
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

## 2. Open (configure) the pilot for the current world fixture (live admin)

Before joins are gated against a specific pilot + world save, open the pilot. This catalogs the server's
current world-save fixture and binds the fail-closed admission gate to it, so a closed pilot or an
uncataloged/expired/purged fixture rejects live admission.

```text
# Connected as an authenticated server admin:
sbpr_pilotop open-pilot
# → reply: v1|<corr>|open-pilot|ok|PilotOpen|pilot=pilot-xxxxxxxx|admits=true
```

The world-save fixture locator is server-derived (world name + durable world UID) — never a client claim.
`open-pilot` is idempotent on its internal operation id; re-running it re-catalogs the same fixture.

## 3. Inspect an account (live admin)

```text
# Connected as an authenticated server admin:
sbpr_pilotop inspect acct-xxxxxxxx
# → reply: v1|<corr>|inspect|ok|Inspected|account=acct-...|status=Active|rev=N|creds=1|classes=Steam|live=true
```

Inspect is **safe by construction**: it returns internal ids, coarse status, and the provider CLASS only.
It never emits a raw subject, HMAC, secret, or token, and it cannot look an account up by raw subject — a
non-`acct-` selector is refused (`MalformedRequest`). A non-admin caller is rejected (`NotAdmin`) with no
data returned; an unauthenticated peer rejects `UnauthenticatedPeer`.

## 4. Export an account (player-safe)

```text
sbpr_pilotop export acct-xxxxxxxx
# → reply: v1|<corr>|export|ok|Exported|account=acct-...|astatus=Active|chars=N|classes=N|receipt=rcpt-...
```

The export carries only player-visible internal state (characters from the account's own membership,
credential CLASS + status, retention schedule). It is cataloged as an Export artifact with a
policy-derived expiry before the reply returns; the receipt id is a stable selector for the later purge.

## 5. Disable an account (closes admission + session, real server-side kick)

Disable is the primary "stop this account now" control. It drains any in-flight mutation, atomically flips
`Active → Disabled`, deterministically removes the live session from the shared registry, then performs the
REAL server-side socket close of that session's peer (`ZNet.Disconnect`).

```text
sbpr_pilotop disable acct-xxxxxxxx
# → reply: v1|<corr>|disable|ok|Disabled|outcome=Applied|rev=N|sessionClosed=true|socketClosed=true
```

- After success, the account cannot open a new session (`AccountDisabled` on admission) and the durable
  status survives a server restart. A delayed network close from the old session cannot reopen authority —
  the account is already Disabled on disk before the socket close is issued.
- **Idempotent:** re-running the same request replays (`outcome=Replayed`).
- **Drain:** if the per-account mutation fence cannot be drained in the bounded timeout, the command aborts
  with `DrainTimeout` and **changes nothing** (the account stays Active, fully recoverable).

## 6. Delete + account-scoped purge (drain + allowlist revocation + absence proof)

Delete is disable-plus: one terminal transaction commits `DeletionPending`, revokes every linked credential
AND its allowlist entry, and closes+kicks the session. The revoked credential means the same subject cannot
immediately re-enrol (wound-down re-admission barrier).

```text
sbpr_pilotop delete acct-xxxxxxxx
# → reply: v1|<corr>|delete|ok|DeletionPending|outcome=Applied|rev=N|sessionClosed=true|socketClosed=true

# Then complete the account-scoped purge (proves absence via compaction, not a tombstone):
sbpr_pilotop purge acct-xxxxxxxx
# → reply: v1|<corr>|purge|ok|Purged|account=acct-...|removedCreds=N|removedChars=N|purgedArtifacts=N|receipt=rcpt-...
```

## 7. Retention purge and pilot closure (as required by the runbook)

```text
sbpr_pilotop retention-purge
# → reply: v1|<corr>|retention-purge|ok|RetentionPurged|total=N|exports=N|backups=N|logs=N|held=N|evidence=N

sbpr_pilotop close-pilot pilot-xxxxxxxx
# → reply: v1|<corr>|close-pilot|ok|PilotClosing|pilot=pilot-...|admits=false
```

Retention purge processes only DUE, unheld artifacts and returns counts/evidence ids by CATEGORY — never a
player/provider selector. Closing the pilot records the derived purge deadline and gates further admission.

> **Not exposed by the live client surface:** whole-fixture reset, arbitrary scoped reset, quarantine, raw
> provider-subject lookup/input, and journal editing. Those remain host/operator-console-only and are never
> reachable from `sbpr_pilotop` (an unknown verb rejects `UnknownVerb`). Do not improvise a purge or reset
> by editing files.

## 8. Recovery: failed drains and process death

- **Failed drain (`DrainTimeout`):** no state changed. Identify the stuck mutation (inspect + server logs),
  let it finish or treat a wedged mutation as an incident, then re-run the disable/delete. Never force it by
  editing the journal.
- **Process death mid-commit:** the account journal is Intent→Committed framed. A crash between the two
  writes leaves an Intent-only transaction that the next boot **quarantines** — the half-written lifecycle
  change never projects, so the account rehydrates to its last fully-committed state. Simply re-run the
  operator command after restart; the store is already coherent.
- **Torn journal tail:** boot truncates and quarantines the torn bytes automatically (`QuarantinedTailBytes`
  is reported). No manual repair; re-run the command.
- **Session left dangling after a hard kill:** the session registry is process-local and cleared on restart,
  so a stale session cannot survive a reboot. On a live server, a repeat `disable`/`delete` re-closes any
  lingering session deterministically.

## 9. What to escalate (do NOT self-serve)

- A mutation that never drains (persistent `DrainTimeout`).
- Any need for a whole-fixture reset, scoped reset, or quarantine — those are host/operator-console-only and
  never reachable from `sbpr_pilotop`.
- Repeated `QuarantinedState` on boot — durable ambiguity needs an operator decision, not a journal edit.
- Anything that would require editing `account-journal.bin`, the HMAC key material, or `adminlist.txt` by
  hand outside the documented commands.
