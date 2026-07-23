---
title: "Niflheim cooperative-pilot account identity — feature specification"
status: proposed
purpose: Normative product and acceptance contract for the three-week, closed-playtest account and server-character identity foundation.
---

# Feature Specification: Niflheim cooperative-pilot account identity

**Maturity:** S1 Design → proposed S2 Spec. This package awaits Daniel's approval.
**Input:** [`server-characters-accounts-discord.md`](server-characters-accounts-discord.md), the accepted Homestead progression identity/receipt substrate, and Daniel's in-thread three-week cooperative-pilot plus minimal-PII direction.

> **Scope guard:** This is a closed, allowlisted, honest-playtester pilot foundation. It does not build public registration, Discord linking, OIDC, passkeys, passwords, email recovery, a web portal, cross-server portability, a server-side character-select UI, or production migration. No task decomposition or implementation is authorized by this package.

## Problem statement

The Homestead progression runtime already derives a transport-bound platform subject and a reconnect-stable Valheim profile subject, but it still uses the raw platform subject as `AccountId` when no map is supplied and uses `player:<s_playerID>` directly as `CharacterId`. That is enough for the current proof but not a durable account foundation: provider identity is baked into authority, the client-facing profile subject remains domain identity, two connections from one account are not centrally excluded, and the current AP receipt binding digest includes the raw platform subject.

For a three-week cooperative pilot, Niflheim needs the smallest server-owned account and character layer that preserves the shipped hostile-principal protections, minimizes personal data, survives restart/retry, supports manual operator recovery, and leaves clean seams for later credentials without implementing those credentials now.

## Solution

An allowlisted tester joins through one Gate-0-proven Valheim transport provider. The server uses the transient provider subject only to compute a versioned, domain-separated HMAC; it resolves or creates an opaque server `AccountId`, reserves that account's sole pending admission, maps the selected Valheim profile to an opaque server `CharacterId`, and then promotes the admission to an active internal session. Gameplay sees only those internal IDs.

The account journal owns credential, character-binding, disable, deletion, reset, and retention transitions and rehydrates bounded lookup indexes at boot. Operators receive tested account inspect/disable/export/delete/reset/purge commands rather than editing files. Discord, OIDC, passkeys, email/passwords, public registration, automated recovery, and portability remain absent but can later implement the verified-provider port without renumbering accounts or characters.

## Closed pilot decisions

These decisions close the open forks from the merged design discussion for this bounded pilot only.

1. **Pilot mode is mandatory when this subsystem is installed.** There is no `progression = off` branch in this package. A non-Niflheim vanilla server simply does not mount this subsystem.
2. **The account root is server-minted.** `AccountId` is an opaque random server identifier. The authenticated platform subject is the first credential, never the account key.
3. **Only one transport provider is admitted per running pilot configuration.** Gate 0 proves the exact current Steamworks or PlayFab transport subject before it may create accounts. Supporting both or migrating between them is deferred.
4. **Valheim's profile picker remains the selector.** The server-observed `s_playerID` is a profile/creator fact used only to find or mint one server `CharacterId` inside the authenticated account. It is not the durable domain `CharacterId`.
5. **At most one pending admission or active connection exists per account.** A second connection rejects even when it presents a different sibling profile.
6. **Recovery is manual and operator-mediated.** There are no player recovery factors, account merges, provider changes, Discord hints, or self-service flows. Unknown credentials create separate accounts; operators never infer identity from names.
7. **Rollback is journal replay plus quarantine.** There is no point-in-time character restore promise. Disposable pilot state may be explicitly reset.
8. **No external identity association ships.** Discord, OIDC, passkeys, email, profile claims, avatars, guild lists, and provider access/refresh tokens are outside the pilot.
9. **Privacy operations may be operator-driven.** The pilot must support export, disable, verified deletion/purge, retention, and backup purge through tested commands/runbooks; no account portal is required.
10. **The Niflheim account subsystem does not persist provider subjects raw.** A versioned keyed HMAC is its lookup key. Niflheim-owned account/gameplay logs, receipts, projections, exports, and operator views use internal IDs. Existing upstream Valheim/BepInEx logs and vanilla `s_creator` world facts are separately inventoried, disclosed, access-restricted, and retained; this package does not falsely claim it can suppress base-game persistence.

## Proposed supersession boundary

If Daniel approves this package, it becomes the controlling pilot identity definition over the accepted Homestead S2 data-model rows that currently describe `AccountId` as the authenticated provider subject and `CharacterId` as the directly server-bound profile subject. The new split is:

- authenticated provider subject → transient credential input;
- server-observed `s_playerID` → transient profile selector/creator fact;
- server-minted `AccountId` → durable account authority/grouping/audit;
- server-minted `CharacterId` → durable gameplay progression owner inside the account.

This proposed docs PR does not claim shipped behavior changed. A later authorized implementation must reconcile the accepted Homestead spec/research/data-model/contracts and code/tests in the same behavior-changing PR, stamp the superseded identity wording with a pointer to the approved authority, and explicitly reset incompatible unreleased fixtures.

## User scenarios and testing

### User Story 1 — Join through one verified platform credential (Priority P1)

As an allowlisted playtester, I want my authenticated game connection to resolve to one stable Niflheim account so reconnects and restarts preserve my authority without storing my raw provider identifier.

**Independent test:** Join a fresh dedicated pilot server through the configured, proven transport backend. Verify one opaque `AccountId` and one HMAC credential binding are created atomically, no raw provider subject appears in Niflheim account/gameplay persistence or Niflheim subsystem logs, upstream server-log exposure is inventoried/retained, and reconnect resolves the same account.

**Acceptance scenarios:**

1. **Given** a supported authenticated and allowlisted provider subject with no binding, **when** the player joins, **then** the server mints one opaque `AccountId`, stores one versioned HMAC binding, and opens one session.
2. **Given** the same provider subject reconnects after logout or restart, **when** the lookup runs, **then** it resolves the original account without minting another.
3. **Given** no authenticated subject, an unsupported provider namespace, or a non-allowlisted subject, **when** join is attempted, **then** it rejects before account or character mutation.
4. **Given** a payload claims another account, character, provider, or profile subject, **when** it is handled, **then** server-observed transport/profile facts remain authoritative and the conflicting claim rejects.

### User Story 2 — Use Valheim profiles as selectors without making them identity (Priority P1)

As a playtester with more than one local Valheim profile, I want each selected profile to map to its own server character inside my account while names and client-selected identifiers cannot cross account boundaries.

**Independent test:** Under one account, join with two profiles sequentially and observe two opaque `CharacterId` records. Rename one profile and reconnect; it resolves the same character. Present the same `s_playerID` from another authenticated account; it cannot acquire the first account's character.

**Acceptance scenarios:**

1. **Given** an authenticated account and a server-observed nonzero `s_playerID` not yet bound within that account, **when** first join completes, **then** the server mints one opaque `CharacterId` and binds the profile subject to it.
2. **Given** the same account/profile reconnects under a different display name or session ZDOID, **when** selection resolves, **then** the same character is selected.
3. **Given** another account presents an equal client-controlled/profile-shaped value, **when** selection resolves, **then** it cannot load, mutate, or receive the first account's character.
4. **Given** one account has a pending admission or active connection, **when** a second profile from that account connects, **then** the second connection rejects as `AccountAlreadyConnected` without minting or changing either character.

### User Story 3 — Keep gameplay authority provider-independent (Priority P1)

As a progression system, I want every gameplay command and receipt to use internal account/character identities so future credential providers can be added without rewriting progression ownership or leaking provider subjects.

**Independent test:** Submit the existing Foundational placement flow after account/character resolution. Verify account/character authority, creator binding, retry, reconnect, and restart still pass while the durable receipt and every binding digest contain no raw provider subject or unkeyed provider-derived value.

**Acceptance scenarios:**

1. **Given** an authenticated session, **when** any progression command is admitted, **then** it receives only resolved `AccountId` and `CharacterId` plus operation evidence; provider subject is not a domain key.
2. **Given** an existing operation is retried, **when** its receipt binding is recomputed, **then** internal IDs produce the same result without consulting a provider.
3. **Given** a provider service is unavailable after session establishment, **when** ordinary gameplay runs, **then** no provider network call is attempted and gameplay authority remains local.
4. **Given** persisted files and ordinary logs are inspected, **then** no raw platform subject, access token, refresh token, email, Discord ID, or profile claim appears.

### User Story 4 — Operate a closed pilot safely (Priority P2)

As a pilot operator, I want bounded commands to inspect, disable, export, delete, and explicitly reset accounts so honest playtester support does not require editing files or inventing state.

**Independent test:** Create an account with two characters and progression, export its player-safe data, disable it, verify join rejection, delete it, restart, and verify credentials no longer resolve and expired backups can be purged.

**Acceptance scenarios:**

1. **Given** an internal account ID, **when** an authenticated server administrator inspects it, **then** the view shows status, character IDs, coarse timestamps, revisions, and receipt correlations but no raw provider subject.
2. **Given** an active account, **when** `DisablePilotAccount` commits, **then** future joins reject while existing durable state remains quarantinable/exportable.
3. **Given** a data export request, **when** the operator runs the export, **then** it contains the player's account, character, credential class/status, and gameplay records without server secrets, HMAC values, unrelated accounts, private operator notes, or raw provider identifiers.
4. **Given** a deletion request or pilot cleanup, **when** deletion commits, **then** credential lookup is revoked, account/character data follows the retention transition, and backup purge is verifiable.
5. **Given** incompatible unreleased pilot data, **when** an explicit reset is authorized, **then** the reset is receipted, scoped to named internal IDs, and never inferred from whichever file is newest.

### User Story 5 — Minimize and expire personal data (Priority P2)

As a playtester, I want Niflheim to retain only what the pilot needs and to explain and remove it predictably.

**Independent test:** Review the generated data inventory and privacy notice against every durable field and log event. Advance retention clocks in a test harness and verify the shipped 14-day security-log and 30-day closed-data defaults, shorter configured periods, re-notice enforcement before any increase, and explicit incident holds.

**Acceptance scenarios:**

1. **Given** first pilot enrollment (auto-created on first authenticated join), **when** the opaque account is created, **then** the pilot's published server-policy / out-of-band disclosure concisely covers stored identity/gameplay data, purpose, retention, operator contact, and reset/deletion boundaries. The disclosure is delivered as server policy, not recorded as a per-account admission acknowledgement.
2. **Given** normal authentication/security logs, **when** they exceed the configured period (shipped default 14 days), **then** they purge automatically or through a tested scheduled operator command.
3. **Given** an account or the pilot closes, **when** the configured closed-data period elapses (shipped default 30 days), **then** credential bindings and linked personal gameplay records are verifiably purged, including eligible backups. If account-scoped journal compaction cannot prove the purge, the disposable pilot fixture is reset in full.
4. **Given** an incident hold, **when** retention extends, **then** scope, reason, actor, and expiry are recorded; a hold cannot silently make all data permanent.

## Functional requirements

- **AIP-FR-001:** The subsystem SHALL admit only authenticated subjects from one explicitly configured and Gate-0-proven transport provider namespace; Gate 0 SHALL also prove a bounded, non-logging operator path to obtain/provision the exact subject used by the HMAC-only allowlist.
- **AIP-FR-002:** Normal pilot admission SHALL auto-create an opaque account on the first authenticated Steam join: there SHALL be no pre-join allowlist requirement and no fabricated per-account disclosure acknowledgement. The subsystem SHALL NOT persist raw provider subjects and no public registration exists. Disclosure is delivered as server policy / out-of-band notice and is NOT recorded as a per-account admission acknowledgement, and acknowledgement SHALL NOT be treated as the selected legal basis by itself. Existing HMAC-only allowlist records MAY remain readable for compatibility/audit but SHALL NOT be required for normal first bind; destructive migration of existing entries SHALL be avoided.
- **AIP-FR-003:** The server SHALL mint opaque cryptographically random `AccountId` and `CharacterId` values with at least 128 bits of entropy, independent of provider/profile identifiers.
- **AIP-FR-004:** Credential and profile lookup keys SHALL use full-length HMAC-SHA-256 over unambiguous canonical encodings with explicit domain separation; credentials bind `(credential-v1, provider namespace, issuer/backend identity, subject)` and profiles bind `(profile-v1, AccountId, s_playerID)`.
- **AIP-FR-005:** The HMAC key SHALL contain at least 256 cryptographically random bits, live outside the account data store and its ordinary backups, carry a key version, and never appear in logs, receipts, exports, or client payloads. Key retirement or a second rotation SHALL be blocked until a version census proves zero live entries/backups on the retiring version or an explicit affected-account/fixture reset completes.
- **AIP-FR-006:** Raw provider subjects inside the Niflheim account subsystem SHALL remain transient and SHALL NOT be persisted in Niflheim account records, gameplay records, receipt bindings, subsystem logs, or exports; Gate 0 SHALL inventory any upstream Valheim/BepInEx transport logging and vanilla world-save profile/creator facts, disclose them, and prove bounded access plus scheduled deletion/whole-fixture purge. If an upstream artifact cannot meet that boundary, pilot enrollment SHALL fail closed.
- **AIP-FR-007:** First account creation and credential binding SHALL commit atomically/recoverably and replay idempotently.
- **AIP-FR-008:** An unknown credential SHALL auto-create a separate opaque account on first authenticated join (no pre-join allowlist validation gate); the server SHALL NOT auto-merge on names or resemblance, so distinct subjects always mint distinct accounts. A wound-down account's still-present (revoked) credential SHALL block re-admission from silently recreating the account until its records are physically purged.
- **AIP-FR-009:** The server SHALL treat Valheim's profile picker as the pilot selector and SHALL derive profile selection only from the authenticated peer's server-observed nonzero `s_playerID`.
- **AIP-FR-010:** The server SHALL mint opaque `CharacterId` values and map each profile subject within one `AccountId`; `s_playerID`, character ZDOID, and display name SHALL NOT be domain `CharacterId`.
- **AIP-FR-011:** Creator/placement validation MAY compare server-observed `s_playerID` facts, but the result SHALL resolve through the profile binding to internal `CharacterId` before domain mutation.
- **AIP-FR-012:** Display names SHALL remain non-authoritative presentation data and SHALL NOT be required in the persistent account store.
- **AIP-FR-013:** At most one pending admission or active session SHALL exist per `AccountId`; reservation SHALL occur before character minting, and conflicting admission SHALL fail without character mutation.
- **AIP-FR-014:** Gameplay commands SHALL consume internal `AccountId` and `CharacterId` from the bound session and SHALL perform no provider lookup or network request.
- **AIP-FR-015:** Existing progression receipt/binding logic SHALL remove raw `PlatformId` and any brute-forceable unkeyed provider-derived digest from durable identity calculations.
- **AIP-FR-016:** Durable account, allowlist, credential, character-binding, disable, delete, purge, retention-hold, pilot-closure, and reset mutations SHALL be revisioned, idempotent, auditable, and recoverable. Pending/active admission leases are deliberately process-local and SHALL instead be race-safe, matching-session released, and cleared on restart.
- **AIP-FR-017:** Boot SHALL rehydrate and validate allowlist/account/credential/character indexes plus pilot-lifecycle/artifact state before join admission; steady-state lookup SHALL use bounded indexed reads rather than journal-wide scans.
- **AIP-FR-018:** The account resolver SHALL make no external provider call on the game thread or ordinary gameplay path.
- **AIP-FR-019:** Pilot account lifecycle operations SHALL require the existing authenticated live-server admin gate; payload identity never grants operator authority. Allowlist provision/revoke MAY also use one server-host-local bootstrap utility authenticated by OS service-account/file ownership, accepting no-echo stdin and exposing no account inspect/export/delete/reset capability.
- **AIP-FR-020:** The pilot SHALL provide inspect, disable, player-safe export, verified deletion/purge, explicit reset, retention purge, incident-hold operations, and a rehearsed breach-response runbook with a named responsible operator.
- **AIP-FR-021:** Exports SHALL exclude secrets, HMAC values, raw provider subjects, tokens, unrelated accounts, and private operator notes.
- **AIP-FR-022:** Access tokens, ID tokens, refresh tokens, passwords, emails, Discord identifiers, avatars, guild lists, and provider profile claims SHALL NOT be stored by this pilot.
- **AIP-FR-023:** Authentication/security logs SHALL be identifier-minimized and governed by `SecurityLogRetentionDays` (shipped pilot default 14); zero/unbounded retention SHALL be invalid and any increase SHALL follow the same new notice/policy version and acknowledgement gate as closed data.
- **AIP-FR-024:** Closed-account/pilot identity and linked gameplay data SHALL use `ClosedDataRetentionDays` (shipped pilot default 30) and SHALL be verifiably purged from account/credential/character/gameplay stores, exports, and eligible backups when due; if scoped journal compaction cannot prove removal, the disposable pilot fixture SHALL reset in full. Zero/unbounded retention SHALL be invalid, any configured increase SHALL require a new notice/policy version and acknowledgement before it applies to an existing account or new enrollment, and incident holds SHALL be explicit and expiring.
- **AIP-FR-025:** Before pilot enrollment, a responsible human SHALL document the pilot purpose/lawful-basis position per data category, and the player disclosure SHALL enumerate stored categories, purposes, retention, operator contact, export/deletion route, and the possibility of explicit unreleased-data reset; notice acknowledgement alone SHALL NOT be recorded as that basis.
- **AIP-FR-026:** Recovery SHALL use journal replay and quarantine; no point-in-time restore, silent repair, automatic merge, or name-based reassignment exists.
- **AIP-FR-027:** Discord linking, OIDC, OAuth portals, passkeys, email/password auth, recovery factors, cross-provider migration, cross-server portability, and a server character-select UI SHALL remain absent.
- **AIP-FR-028:** The accepted implementation SHALL prove dedicated-server joined-client behavior, not only engine-free tests or registration logs. For `AT-AIP-DEDICATED-SECOND-SESSION-REJECT` ONLY, this proof is SPLIT into two conjoined obligations, both required to pass (Option B, owner-approved on `t_13db2c95`): (a) a genuine joined modded Steam client on the real dedicated `Niflheim` server drives the live transport→provider-auth→`AccountId`→admission→character-mint wiring (unchanged live-GUI bar); and (b) a production-identical direct-peer harness invokes the SHIPPED `LiveSessionAdmission.Admit` / `AccountAdmissionIndex.TryReserve` code from the exact compiled candidate assembly (attesting its SHA-256 before running), presents two transport peers resolving to ONE authenticated `AccountId`, and proves the second reserves reject `AccountAlreadyConnected` BEFORE any character mint while the first lease still mints and releases correctly on close. The split proves the server-authoritative one-account/one-session invariant at its real enforcement seam. It does NOT prove Steam's transport layer independently rejects a duplicate account login — Steam enforces that client-side by kicking the first session, which is precisely why the server seam is unreachable by two concurrent Steam GUI clients and why a production-identical direct-peer harness is the only mechanism that can exercise same-account concurrency. The harness MUST link the shipped candidate admission binary; a re-implemented, source-linked, or mocked admission core does not satisfy this AT. The other six ATs (JOIN/RECONNECT/SECOND-PROFILE/RESTART/DISABLE/OPERATOR-RUNBOOK) keep the full live-joined-GUI bar unchanged.

## Success criteria

- **AIP-SC-001:** One new tester's first authenticated join auto-creates exactly one account/credential binding and one selected character; reconnect and restart resolve the same internal IDs.
- **AIP-SC-002:** Hostile payload substitution, unauthenticated peers, unsupported provider namespaces, wound-down (disabled/deleted/quarantined) subjects, and second simultaneous sessions reject without durable mutation.
- **AIP-SC-003:** Two sequential profiles on one account receive distinct internal characters; rename/reconnect preserves selection; cross-account reuse cannot cross ownership.
- **AIP-SC-004:** Existing Foundational placement authority/replay/restart acceptance remains green after provider identity is removed from domain receipts.
- **AIP-SC-005:** A mechanical scan of Niflheim durable fixtures, subsystem logs, exports, and receipts finds no raw provider subject or forbidden token/profile field; the Gate-0 evidence separately inventories upstream runtime logs and proves their bounded access/retention treatment.
- **AIP-SC-006:** A synthetic 10,000-binding test proves post-rehydration account and character resolution uses indexed lookup with no journal scan or network call per lookup.
- **AIP-SC-007:** Disable, export, verified account/fixture purge, reset, retention purge, backup purge, and incident-hold expiry each have a named automated test and operator runbook proof.
- **AIP-SC-008:** A real dedicated server and joined modded client complete first bind, reconnect, second-profile selection, second-session rejection, restart, and post-disable rejection. For second-session rejection ONLY, the evidence is split-proof (AIP-FR-028 Option B): the live joined client proves the transport/auth/admission wiring, and a production-identical direct-peer harness against the exact compiled candidate admission binary proves two peers resolving to one `AccountId` reject the second as `AccountAlreadyConnected` before character mint. The harness half does not, and does not claim to, prove Steam's client-side duplicate-login kick.

## Requirement-to-acceptance coverage

| Requirement | Named acceptance |
|---|---|
| AIP-FR-001 | `AT-AIP-PROVIDER-GATE0`, `AT-AIP-UNAUTHENTICATED`, `AT-AIP-PROVIDER-NAMESPACE`, `AT-AIP-PROVIDER-PROVISION-INPUT`, `AT-AIP-PROVIDER-RECONNECT` |
| AIP-FR-002 | `AT-AIP-FIRST-JOIN-AUTOCREATE`, `AT-AIP-ALLOWLIST-HMAC-ONLY`, `AT-AIP-DISCLOSURE-COMPLETE` |
| AIP-FR-003 | `AT-AIP-FIRST-BIND`, `AT-AIP-INTERNAL-ID-ENTROPY` |
| AIP-FR-004 | `AT-AIP-HMAC-CANONICAL` |
| AIP-FR-005 | `AT-AIP-KEY-STRENGTH-SEPARATION`, `AT-AIP-KEY-MISSING-FAIL-CLOSED`, `AT-AIP-PREVIOUS-KEY-REKEY`, `AT-AIP-PROFILE-PREVIOUS-KEY-REKEY`, `AT-AIP-KEY-VERSION-CENSUS`, `AT-AIP-KEY-RETIREMENT-BLOCKED`, `AT-AIP-FULL-RESET-ROTATES-KEY` |
| AIP-FR-006 | `AT-AIP-PRINCIPAL-SCRUB`, `AT-AIP-PERSISTED-PII-SCAN`, `AT-AIP-PROVIDER-LOG-SCRUB`, `AT-AIP-UPSTREAM-WORLD-FACT-INVENTORY` |
| AIP-FR-007 | `AT-AIP-FIRST-BIND-RACE`, `AT-AIP-ACCOUNT-RECONNECT`, `AT-AIP-ACCOUNT-JOURNAL-RECOVERY` |
| AIP-FR-008 | `AT-AIP-UNKNOWN-CREDENTIAL-SEPARATE`, `AT-AIP-NO-NAME-MERGE` |
| AIP-FR-009 | `AT-AIP-PROFILE-MINT` |
| AIP-FR-010 | `AT-AIP-PROFILE-MINT`, `AT-AIP-PROFILE-RECONNECT`, `AT-AIP-CROSS-ACCOUNT-BLOCK` |
| AIP-FR-011 | `AT-AIP-CREATOR-BRIDGE` |
| AIP-FR-012 | `AT-AIP-PROFILE-RENAME`, `AT-AIP-NAME-NONAUTHORITY` |
| AIP-FR-013 | `AT-AIP-ADMISSION-LEASE-RACE`, `AT-AIP-ONE-SESSION`, `AT-AIP-STALE-DISCONNECT` |
| AIP-FR-014 | `AT-AIP-NO-PROVIDER-HOTPATH` |
| AIP-FR-015 | `AT-AIP-PRINCIPAL-SCRUB`, `AT-AIP-HOSTILE-PRINCIPAL`, `AT-AIP-RECEIPT-REPLAY` |
| AIP-FR-016 | `AT-AIP-ACCOUNT-JOURNAL-RECOVERY`, `AT-AIP-TORN-TAIL`, `AT-AIP-OPERATION-CONFLICT`, `AT-AIP-DURABLE-LIFECYCLE-REPLAY`, `AT-AIP-ARTIFACT-CATALOG`, `AT-AIP-PILOT-CLOSURE-DEADLINE` |
| AIP-FR-017 | `AT-AIP-INDEXED-10K`, `AT-AIP-BOOT-BEFORE-ADMISSION` |
| AIP-FR-018 | `AT-AIP-NO-PROVIDER-HOTPATH` |
| AIP-FR-019 | `AT-AIP-NONADMIN-REJECT`, `AT-AIP-LOCAL-BOOTSTRAP-SCOPE` |
| AIP-FR-020 | `AT-AIP-ADMIN-INSPECT`, `AT-AIP-ADMIN-DISABLE`, `AT-AIP-EXPORT-SAFE`, `AT-AIP-DELETE-PURGE`, `AT-AIP-DELETE-REVOKES-ALLOWLIST`, `AT-AIP-RETENTION-PURGE`, `AT-AIP-RESET-EXPLICIT`, `AT-AIP-HOLD-EXPIRY`, `AT-AIP-BREACH-RUNBOOK`, `AT-AIP-OPERATOR-RUNBOOK` |
| AIP-FR-021 | `AT-AIP-EXPORT-SAFE` |
| AIP-FR-022 | `AT-AIP-PERSISTED-PII-SCAN` |
| AIP-FR-023 | `AT-AIP-RETENTION-CONFIG`, `AT-AIP-RETENTION-INCREASE-RENOTICE` |
| AIP-FR-024 | `AT-AIP-RETENTION-CONFIG`, `AT-AIP-RETENTION-INCREASE-RENOTICE`, `AT-AIP-DELETE-PURGE`, `AT-AIP-DELETE-REVOKES-ALLOWLIST`, `AT-AIP-PURGE-FALLBACK-RESET`, `AT-AIP-ARTIFACT-CATALOG`, `AT-AIP-PILOT-CLOSURE-DEADLINE`, `AT-AIP-BACKUP-PURGE`, `AT-AIP-HOLD-EXPIRY` |
| AIP-FR-025 | `AT-AIP-DISCLOSURE-COMPLETE`, `AT-AIP-DATA-INVENTORY-BASIS` |
| AIP-FR-026 | `AT-AIP-RECEIPT-REPLAY`, `AT-AIP-QUARANTINE`, `AT-AIP-NO-TIME-TRAVEL`, `AT-AIP-FULL-RESET-ROTATES-KEY` |
| AIP-FR-027 | `AT-AIP-DEFERRED-SURFACE-ABSENT` |
| AIP-FR-028 | `AT-AIP-DEDICATED-JOIN`, `AT-AIP-DEDICATED-RECONNECT`, `AT-AIP-DEDICATED-SECOND-PROFILE`, `AT-AIP-DEDICATED-SECOND-SESSION-REJECT` (split-evidence: live-GUI transport half + shipped-binary direct-peer harness half — see AIP-FR-028), `AT-AIP-DEDICATED-RESTART`, `AT-AIP-DEDICATED-DISABLE` |

## Edge cases

- The HMAC key is missing or unknown at boot: fail closed before account admission; never fall back to raw identifiers.
- A previous lookup-key version exists: permit configured previous-key lookup only; successful authentication may re-key the binding under the active version without changing `AccountId`.
- The lookup key is lost: bindings are unrecoverable by design; the pilot may explicitly reset after operator disclosure rather than guess ownership.
- Two first joins race for one credential: one account/binding commits; the loser replays/resolves that account.
- One account attempts two profiles concurrently: one session wins; the other rejects without minting a character.
- Account deletion races an active session: a per-account mutation fence waits for any already-committing mutation, then atomically commits `DeletionPending` plus credential/allowlist revocation before the server closes the session; later gameplay admission/commit rejects.
- A torn account journal tail appears: ignore/quarantine the incomplete tail and rebuild only terminal records.
- Provider backend changes between boots: existing bindings do not reinterpret under the new namespace; the server refuses startup/admission until the configured migration/reset decision is explicit.

## Testing decisions

- Test externally observable account/character/session outcomes, durable records, exports, rejections, and live joined-client behavior—not private dictionary or serializer implementation details.
- Keep one high seam for provider admission: fake verified provider/profile principals drive the same account application used by the net48 transport adapter.
- Reuse the existing hostile-principal, creator-binding, reconnect, process-death, torn-tail, revision, and dedicated-ingress test patterns.
- Run one synthetic 10,000-binding proof to assert lookup shape: one boot replay, then indexed/no-network/no-journal-scan resolution. Do not turn one machine's latency into a product promise.
- Seed forbidden raw IDs/tokens/claims into negative fixtures and mechanically verify they do not appear in journals, Niflheim subsystem logs, exports, or gameplay receipts.
- Treat live dedicated-server first bind, reconnect, second-profile selection, concurrent-session rejection, restart, disable, and operator runbook execution as the final acceptance gate. Registration logs or engine-free tests alone cannot pass it.
- Preserve writer ≠ verifier for this package and each later implementation tracer.

## Out of scope

- Public launch readiness or anonymous registration.
- Discord association, role synchronization, support routing, or recovery.
- OIDC, OAuth, passkeys/WebAuthn, passwords, email, SAML, or arbitrary provider plugins.
- Automated account recovery, merge, transfer, or credential replacement.
- Full server-side character creation/selection UI.
- More than one active account session.
- Cross-server/product portability or shared account service.
- Behavioral anti-cheat profiling or identity-linked analytics.
- Final production retention/legal policy, production migrations, or compatibility freeze.
- Task decomposition and runtime implementation.

## Further notes

This package intentionally chooses a smaller but deeper module: provider authentication proves a transient credential; Niflheim resolves it once into internal authority; gameplay never sees the provider. Future credential providers must implement the same verified-principal port and may not widen stored claims by default.