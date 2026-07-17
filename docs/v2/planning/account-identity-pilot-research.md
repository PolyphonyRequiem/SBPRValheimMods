---
title: "Niflheim cooperative-pilot account identity — research"
status: proposed
purpose: Ground the pilot account specification in current SBPR code, accepted identity contracts, external precedent, privacy law principles, and mandatory proof gaps.
---

# Niflheim cooperative-pilot account identity — research

**Normative spec:** [`account-identity-pilot-spec.md`](account-identity-pilot-spec.md)
**Design provenance:** [`server-characters-accounts-discord.md`](server-characters-accounts-discord.md)

## Research boundary

This document answers one bounded question: what must change in the shipped Homestead progression identity substrate to support a three-week, closed, honest-playtester pilot with server-minted accounts/characters and minimal personal data?

It does not select or build Discord, OIDC, passkeys, passwords, a public account service, cross-server portability, or automated recovery.

## Current shipped substrate

### Already present and reusable

| Capability | Current seam | Pilot use |
|---|---|---|
| Transport-bound sender identity | `Application/Runtime/AuthenticatedSenderIdentity.cs` plus direct per-peer ZRpc resolution | Supplies a transient authenticated platform subject and server-observed `s_playerID`; payload identity remains untrusted |
| Account/character value objects | `Domain/Identity/ProgressionIdentity.cs` | Keep types and domain boundaries, change what values populate them |
| Candidate-E resolver seam | `PrincipalResolver(Func<string,string?>)` | Replace passthrough fallback in pilot composition with a required account store lookup |
| Reconnect-stable creator fact | `ServerCreatorIdentity.CharacterSubject(s_playerID)` | Retain only at the adapter boundary for creator evidence/profile selection; map to minted `CharacterId` before domain mutation |
| Hostile-principal/reconnect tests | `NiflheimTransportAreaRetryTests`, `NiflheimRuntimeCorrectionTests`, contract tests | Regression floor for the new mapping |
| Durable framed journal | `OperationReceiptStore` and relationship/facet journals | Reuse framing, CRC, fsync, terminal-record, replay, and quarantine principles |
| Account/character/authority aggregates | Homestead progression domain/persistence | Re-key under minted IDs; preserve gameplay ownership separation |
| Admin identity | `VanillaAdminIdentity` and provision ingress | Authority source for pilot account operations; no new admin identity path |

### Gaps that block the pilot

1. **Account remains provider-shaped in the default composition.** `PrincipalResolver` falls back to `AccountId = PlatformId`; runtime tests intentionally instantiate candidate A with `accountIdForPlatform: null`.
2. **Character remains profile-shaped.** The runtime currently renders `s_playerID` directly as `player:<id>` and treats that as durable `CharacterId`. It is reconnect-stable and correct for creator matching, but it is not server-minted account-owned identity.
3. **No account/credential/character-binding aggregate exists.** There is no durable unique credential index, account status, active-session gate, provider namespace, deletion/retention transition, or operator export.
4. **Raw provider identity influences durable receipt binding and current account fields.** `OperationReceiptStore.SubmitFoundationalAp` hashes `AccountId|CharacterId|PlatformId`; because platform subjects such as Steam IDs are enumerable and the digest is unkeyed/truncated, this is not an acceptable privacy boundary. Candidate-A fallback also sets `AccountId = PlatformId`, and the receipt journal persists that account value directly on every identity-bearing boundary. Minted IDs plus explicit fixture reset are therefore required; merely deleting `PlatformId` from one digest is insufficient.
5. **Receipt lookup currently scans the journal.** `InspectJournal(operationId)` walks all durable records. That proof mechanism is acceptable for the existing bounded slice but must not become the per-join account lookup. Pilot account/credential/profile indexes must rehydrate once and serve bounded in-memory lookups.
6. **The exact production transport credential remains a P0.** The merged design and Homestead research both leave the production provider unresolved. Current code can read a socket/platform subject, but the pilot must prove its stability, namespace, and authentication semantics under the actual configured Steamworks or PlayFab backend.
7. **No privacy operations exist.** No field inventory, retention purge, player-safe export, verified delete/purge command, backup purge proof, token redaction test, or incident-hold lifecycle exists.
8. **No Discord/OIDC service exists in the owning repositories.** Keeping those out of the three-week critical path is a schedule and privacy reduction, not merely feature deferral.

### Accepted-contract drift this package intentionally proposes

The accepted Homestead S2 data model still names `AccountId` as the authenticated provider subject and `CharacterId` as a server-bound subject, while current runtime composition concretely uses the platform subject and `player:<s_playerID>`. This package intentionally proposes a later refinement to minted IDs. Until Daniel approves it and an authorized behavior PR reconciles the older accepted package, the older documents/code remain the description of shipped behavior. The implementation gate must update both authorities together rather than leaving contradictory active definitions.

## External landscape conclusion

The merged design's primary-source survey controls:

- **Smoothbrain ServerCharacters** is cooperative server profile vaulting, not authoritative account/character state. Clients still upload complete profile/inventory bytes; identity is provider plus character name; current source is Steamworks-only; its emergency signature sends derived signing material to the client.
- **World of Valheim SSC** is deprecated and uses the same client-uploaded profile model.
- **AzuAntiCheat** is a compatibility/filter layer and cannot fix client-authoritative identity or state.
- **PlayFab** demonstrates the correct account/linked-credential separation but also the danger of force-linking/orphaning; the pilot therefore forbids automatic merge/rebind.
- **TrinityCore** demonstrates mature account/character separation and account-scoped moderation; only the domain boundary transfers.
- **Praetoris/DiscordSRV** demonstrate two-channel code ceremonies, but no Discord link is needed to prove the pilot account root.

No surveyed Valheim mod supplies the required internal account, minted character, minimal-PII binding, lifecycle, and receipt integration.

## Standards and privacy grounding

### Stable external identity

OpenID Connect defines `(issuer, subject)` as the stable relying-party identifier. The pilot does not implement OIDC, but it adopts the same provider-namespace discipline: a subject is never globally meaningful without its configured backend/issuer namespace.

- OpenID Connect Core: https://openid.net/specs/openid-connect-core-1_0-final.html
- Steam user authentication/session-ticket model: https://partner.steamgames.com/doc/features/auth

### Data minimization and pseudonymisation

Provider IDs, online identifiers, IP/log data, internal account links, and pseudonymous lookup values are treated as personal data while Niflheim can connect them to a player. HMAC reduces database-only breach impact; it does not make the store anonymous or remove data-subject obligations.

- GDPR consolidated text, especially Articles 5, 25, 32, and 33: https://eur-lex.europa.eu/eli/reg/2016/679/oj/eng
- EDPB pseudonymisation guidance: https://www.edpb.europa.eu/public-consultations/guidelines-012025-on-pseudonymisation_en
- EDPB data-protection-by-design guidance: https://www.edpb.europa.eu/documents/guideline/guidelines-42019-on-article-25-data-protection-by-design-and-by-default_en
- EDPB breach guidance: https://www.edpb.europa.eu/sme/assess-the-risks/data-breaches_en

The pilot minimizes risk by storing no email, password, token, Discord identity, avatar, guild list, or provider profile; by HMAC-keying the sole external credential; by keeping operator workflows manual but tested; and by separating provider data from gameplay receipts.

Recording a pilot notice version and acknowledgement supports transparency; it is not treated as automatic consent or as the selected lawful basis for core authentication. Final lawful-basis and jurisdiction-specific decisions require human legal review before public launch.

## Mandatory Gate 0 — authenticated provider subject proof

Before account creation code is allowed to ship, an executable spike must prove against the actual pilot backend:

1. the server—not payload—obtains the provider subject;
2. the provider namespace is explicit and stable;
3. reconnect and server restart reproduce the same subject;
4. two accounts cannot present the same authenticated subject simultaneously without the active-session gate observing one account;
5. unavailable/anonymous/empty subjects fail closed;
6. Niflheim subsystem logging writes no raw subject; any upstream Valheim/BepInEx transport logging is inventoried and covered by restricted access plus the pilot retention policy;
7. the proof identifies a bounded, non-logging way for the operator to obtain/provision the exact allowlist subject (including the case where a PlayFab subject is not player-visible);
8. the proof identifies whether Steamworks or PlayFab is the only supported pilot backend.

This gate may reuse the direct per-peer transport seam already proven by PR #317; it must not reopen routed-sender trust.

### First-enrollment bootstrap authority

The live Valheim admin gate remains the authority for account lifecycle operations, but it cannot bootstrap the first allowlist entry if account admission rejects everyone first. The pilot therefore adds one narrower server-host-local utility: OS service-account/file ownership authenticates the caller; no-echo stdin carries the transient subject; outputs are internal entry/receipt IDs; permitted verbs are allowlist provision/revoke only; broad key/data permissions fail closed. It is not a second account-admin API and cannot inspect/export/disable/delete/reset accounts or invoke gameplay.

## Storage feasibility

### Selected pilot shape

Use a small append-only account journal with the same framed-record discipline as the shipped receipt store:

- length + CRC framing;
- versioned record type;
- fsync only for accepted account/credential/character/disable/delete/reset mutations;
- terminal records as authority;
- replay/quarantine of torn/incomplete tails;
- boot rehydration into dictionaries keyed by `AccountId`, credential HMAC, and `(AccountId, profile-subject HMAC)`.

Do not use the current `InspectJournal` full scan per join. Do not add SQLite/native interop inside the three-week pilot unless a measured Gate-0/load test disproves the indexed journal shape.

### HMAC considerations

- Use full HMAC-SHA-256, not truncated SHA-256.
- Canonical input must be unambiguous (length-prefixed fields or an equivalent canonical encoder), not delimiter concatenation, and must domain-separate credential (`credential-v1`) from profile (`profile-v1`) lookups.
- Key material lives outside the journal and carries `LookupKeyVersion`.
- The HMAC key is generated from at least 256 bits of CSPRNG entropy and is backed up separately from ordinary account journals/backups so a database-only breach does not automatically include it.
- At most active + configured previous key versions may resolve. A successful previous-key login lazily writes a new binding under the active key in one recoverable transition.
- Losing every configured HMAC key makes bindings intentionally unrecoverable. Recovery is explicit pilot reset—not guessed matching.

## Performance model

Account operations are join-time or operator-time, never per-frame:

- Boot cost: one sequential journal replay.
- Join cost after boot: one credential dictionary lookup, one account lookup, one profile-binding lookup, one active-session lookup.
- Gameplay cost: no provider lookup, provider HTTP call, or account-journal scan; the bound internal principal is reused.
- Mutation cost: rare account operations each pay a durable fsync.

A synthetic 10,000-binding test is sufficient for the pilot to prove the cost shape. Its acceptance is structural—indexed lookup and zero network/journal scan per resolution—not an invented latency promise tied to one workstation.

## Privacy data inventory

| Category | Pilot value | Persisted? | Retention |
|---|---|---:|---|
| Raw provider subject inside Niflheim account subsystem | transient input from authenticated transport | no | request lifetime only |
| Upstream Valheim/BepInEx transport log subject/IP, if present | base-runtime operation outside Niflheim account persistence | possibly upstream | inventoried; restricted; scheduled purge required or pilot fails closed |
| Vanilla world-save `s_creator` / profile fact | base-game creator/ownership behavior; read by Niflheim creator bridge | yes, vanilla world data | world-save/fixture lifecycle; disclosed; whole-fixture reset fallback |
| Allowlist HMAC, key version, entry ID/status | closed-pilot enrollment | yes | active enrollment/account + configured closed-data period |
| Notice version/acknowledgement + retention-policy version | transparency/policy provenance | yes | same as allowlist/account |
| Internal operator attribution/operation receipt | accountable lifecycle operations | yes | minimal audit period / scoped hold |
| Credential lookup HMAC + key version | resolve account | yes | active account/pilot + configured closed-data period (default 30 days) |
| Opaque AccountId | authority/audit/gameplay ownership | yes | active account/pilot + configured closed-data period (default 30 days) |
| Opaque CharacterId | gameplay ownership | yes | active account/pilot + configured closed-data period (default 30 days) |
| Profile-subject HMAC | select character within account | yes | same as character |
| Character/display name | live presentation only | no in account store | session/log policy only |
| Gameplay progression/receipts | pilot gameplay and recovery | yes | active account/pilot + configured closed-data period (default 30 days) |
| Authentication/security log | operate/diagnose abuse and failure | minimized | configured ordinary period (default 14 days) |
| Incident hold record | bounded exception | yes | explicit expiry |
| Player-safe export artifact | answer a player/operator request | yes, when generated | cataloged; source-data deadline or earlier |
| Backup/journal/log artifact metadata | locate and prove purge of data-bearing artifacts | yes | until artifact purge proof + minimal audit expiry |
| Pilot lifecycle/closure timestamps and purge deadline | calculate and prove end-of-pilot retention | yes | until terminal purge proof + minimal audit expiry |
| Reset/quarantine record | recover safely and explain discarded state | yes | minimal audit period / scoped hold |
| HMAC secret/key-ring material | keyed lookup/recovery | yes, outside account store | separate protected lifecycle; retired only after zero version census |
| Provider/OAuth/Discord tokens and claims | absent feature | no | never |

The final public product may choose different lawful bases/retention after legal review; this pilot does not silently set launch policy.

## Rejected shortcuts

- Provider subject as `AccountId`.
- `s_playerID`, character ZDOID, or display name as server `CharacterId`.
- Plain hash of Steam/Discord/provider IDs.
- Raw provider ID in audit/receipts "for debugging."
- Account creation before allowlist and privacy disclosure.
- Name-based merge, recovery, or reassignment.
- Self-service UI before operator commands are correct.
- Discord/OIDC/passkeys as a three-week stretch hidden inside the core estimate.
- Per-join journal scanning.
- Assuming a successful unit test proves the dedicated transport hook.

## Research exit criteria

The package may become approval-ready when:

- the five artifacts agree on every identity, retention, command, and exclusion;
- Gate 0 is a blocking first tracer, not an assumed capability;
- every functional requirement has at least one named acceptance path;
- the current raw-platform receipt dependency is explicitly removed;
- no tasks or code changes exist;
- mechanical checks pass;
- a fresh writer≠verifier pass returns PASS after corrections.