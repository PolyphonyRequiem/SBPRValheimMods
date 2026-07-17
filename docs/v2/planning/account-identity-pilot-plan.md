---
title: "Niflheim cooperative-pilot account identity — implementation plan"
status: proposed
purpose: Plan a three-week, dependency-ordered account/character identity pilot after specification approval; no implementation is authorized by this document.
---

# Implementation Plan: Niflheim cooperative-pilot account identity

**Spec:** [`account-identity-pilot-spec.md`](account-identity-pilot-spec.md)
**Research:** [`account-identity-pilot-research.md`](account-identity-pilot-research.md)
**Data model:** [`account-identity-pilot-data-model.md`](account-identity-pilot-data-model.md)
**Contracts:** [`account-identity-pilot-contracts.md`](account-identity-pilot-contracts.md)

> **Stop gate:** This package is docs-only and awaits independent verification and Daniel's approval. Do not create `tasks.md`, Kanban implementation cards, provider applications, secrets, databases, bot services, or runtime code without later separate authorization.

## Summary

Deepen the existing Homestead progression identity seam into one closed-pilot account foundation. The server will verify one configured transport provider, HMAC its transient subject into a durable credential binding, mint provider-independent accounts and characters, reuse Valheim's profile picker as selection, allow one pending admission or active session per account, migrate gameplay authority/receipts to internal IDs, and provide minimal operator/privacy operations.

The three-week boundary deliberately excludes Discord, OIDC, passkeys, email/passwords, public registration, automated recovery/merge, cross-server portability, and server-side character-select UI.

## Technical context

**Runtime:** C# / net48 BepInEx sibling plugin; engine-free domain/application seams link-tested under the existing net8 test project.
**Existing authority:** direct per-peer authenticated ZRpc → server `ZNetPeer` → platform subject + character ZDO `s_playerID`; payload claims never authority.
**Existing persistence:** framed append-only journals with CRC, fsync, terminal-record replay, and quarantine.
**New persistence:** small account journal plus boot-rehydrated credential/account/profile indexes; no new third-party runtime dependency selected.
**Primary platform:** Linux dedicated server with joined modded clients; listen host remains regression coverage.
**Scale:** closed allowlisted pilot; synthetic 10,000 credential/profile bindings prove lookup shape, not public scale.
**Performance:** join/operator event-driven only; zero provider network calls and zero account-journal scans on gameplay path.
**Privacy:** no raw provider/profile subject persisted; no provider tokens/profile claims; configurable positive retention with shipped 14-day security-log and 30-day closed-data defaults; increases require re-notice.

## Constitution check

| Article | Plan response | Status |
|---|---|---|
| Spec-first | Five docs precede runtime behavior; future implementation changes spec/code/tests together | PASS |
| Clean-room | Uses vanilla transport facts and SBPR code; external systems are behavioral references only | PASS |
| Corpus/source grounding | Existing authenticated identity and receipt seams read directly; Gate 0 re-proves live backend | PASS |
| Runtime conformance | Identity schema/provider/key versions require startup validation and test manifest | REQUIRED |
| Writer ≠ verifier | Fresh agent must compare all five docs against merged design/current code | REQUIRED GATE |
| Incremental delivery | Gate 0 plus four vertical tracers and final live proof | PASS |
| Daniel controls landing | Docs PR only; tasks and implementation require separate authorization | PASS |
| Semver docs | Artifacts live in `docs/v2/planning/`, status frontmatter, index entry | PASS |
| ADR-0005 | No `.specify/`, CLI, or `specs/NNN/` layout | PASS |

No constitutional exception is requested.

## Proposed source boundary

This is a planning boundary, not files created by this pass.

```text
src/SBPR.Niflheim.HomesteadStones/
├── Domain/
│   ├── Identity/                 # existing internal IDs/principal; provider-free
│   └── Accounts/                 # account, credential, character-binding transitions
├── Application/
│   ├── Accounts/                 # admission, session, operator, privacy handlers
│   ├── Commands/                 # existing gameplay command principal integration
│   └── Receipts/                 # remove PlatformId from gameplay receipt bindings
├── Persistence/
│   └── Accounts/                 # framed journal, replay, indexes, artifact/pilot lifecycle catalog
├── Adapters/
│   └── Identity/                 # provider/profile transient facts + HMAC boundary
└── Features/
    └── PilotIdentity/            # net48 transport composition/admin commands

tools/
└── niflheim-account-bootstrap/   # allowlist-only no-echo local utility; no account lifecycle reads

tests/
├── NiflheimPilotAccountDomainTests.cs
├── NiflheimPilotAccountContractTests.cs
├── NiflheimPilotAccountRecoveryTests.cs
├── NiflheimPilotAccountPrivacyTests.cs
└── NiflheimPilotAccountRuntimeTests.cs
```

Do not extract a shared Niflheim identity library until a second owning runtime proves the seam. Do not add provider-specific policy to gameplay handlers.

## Architecture decisions

### A1 — Internal identity, transient credentials

`AccountId`/`CharacterId` are server-minted. Platform and profile subjects exist only in adapters long enough to compute lookup HMACs and bind a session.

### A2 — One provider port, one pilot adapter

Core account code depends on `IPilotCredentialProvider`, but the pilot composes exactly one Gate-0-proven Valheim transport adapter. No plugin marketplace or dynamic issuer discovery.

### A3 — Profile picker as adapter-level selection

Valheim `s_playerID` remains necessary for server creator verification and profile selection, but is account-scoped/HMACed and resolves to minted `CharacterId` before domain use.

### A4 — Indexed journal, not scan-per-join

Reuse journal durability principles, not `InspectJournal` lookup behavior. Replay once at boot; serve credential/profile joins from in-memory unique indexes. Escalate to SQLite only after measured failure, not speculation.

### A5 — Provider-free gameplay receipts

The bound session supplies internal principal. Remove `PlatformId` from progression receipt digests and durable authority. Existing unreleased fixtures may reset.

### A6 — Manual operations are real product surface

Live-server admin-gated handlers/UI own account lifecycle operations. A narrow server-host-local bootstrap utility may provision/revoke allowlist entries through no-echo stdin under OS service-account/file ownership; it exposes no account read/lifecycle/gameplay capability. A future portal ships later, not now.

### A7 — Privacy behavior is executable

Retention, purge, backup purge, redaction, export allowlist, and incident-hold expiry require tests and operator proof. A prose notice alone does not satisfy the feature.

### A8 — Reconcile the accepted identity authority when behavior changes

This package proposes minted account/character IDs while the accepted Homestead S2 model and shipped code remain provider/profile-shaped. Approval of this docs package does not rewrite shipped truth. The later authorized implementation PR must update the accepted Homestead identity rows/contracts/research and add explicit supersession pointers in the same PR as code/tests and fixture reset.

### A9 — Whole-fixture purge is the three-week baseline

The estimate does not depend on safe per-account compaction of shared append-only gameplay journals. The pilot must catalog artifacts and implement verified whole-fixture reset/purge; account-scoped compaction is optional only if it independently proves journal invariants and complete removal.

## Three-week delivery shape

The effort model is 140–220 engineering hours. Parallel work begins only after Gate 0/schema agreement; final integration and live proof reconverge.

### Gate 0 — Prove the exact pilot transport principal

**Goal:** prove one configured Steamworks or PlayFab backend supplies a stable authenticated subject without payload trust or default logging.

Deliverables after implementation authorization: executable authenticated-peer spike, provider namespace decision, safe allowlist-provisioning input, reconnect/restart evidence, upstream log/world-fact inventory.

**Named acceptance:** `AT-AIP-PROVIDER-GATE0`, `AT-AIP-UNAUTHENTICATED`, `AT-AIP-PROVIDER-NAMESPACE`, `AT-AIP-PROVIDER-PROVISION-INPUT`, `AT-AIP-PROVIDER-RECONNECT`, `AT-AIP-PROVIDER-LOG-SCRUB`, `AT-AIP-UPSTREAM-WORLD-FACT-INVENTORY`.

**Exit:** exactly one provider backend is named/proven, a safe provisioning input exists, and every upstream identity-bearing log/world artifact has a disclosed bounded purge path; otherwise pilot enrollment remains closed.

### Tracer 1 — Account + credential vertical slice

**Goal:** HMAC one verified subject, mint account/binding, journal it recoverably, rehydrate indexes, and resolve on reconnect.

**Named acceptance:** `AT-AIP-FIRST-BIND`, `AT-AIP-INTERNAL-ID-ENTROPY`, `AT-AIP-ACCOUNT-RECONNECT`, `AT-AIP-FIRST-BIND-RACE`, `AT-AIP-HMAC-CANONICAL`, `AT-AIP-ALLOWLIST-HMAC-ONLY`, `AT-AIP-DISCLOSURE-COMPLETE`, `AT-AIP-DATA-INVENTORY-BASIS`, `AT-AIP-NOT-ALLOWLISTED`, `AT-AIP-UNKNOWN-CREDENTIAL-SEPARATE`, `AT-AIP-NO-NAME-MERGE`, `AT-AIP-KEY-STRENGTH-SEPARATION`, `AT-AIP-KEY-MISSING-FAIL-CLOSED`, `AT-AIP-PREVIOUS-KEY-REKEY`, `AT-AIP-KEY-VERSION-CENSUS`, `AT-AIP-KEY-RETIREMENT-BLOCKED`, `AT-AIP-TORN-TAIL`, `AT-AIP-ACCOUNT-JOURNAL-RECOVERY`, `AT-AIP-DURABLE-LIFECYCLE-REPLAY`, `AT-AIP-OPERATION-CONFLICT`, `AT-AIP-BOOT-BEFORE-ADMISSION`, `AT-AIP-PERSISTED-PII-SCAN`, `AT-AIP-INDEXED-10K`.

**Exit:** no raw subject in Niflheim journals/subsystem logs; upstream log/world facts are inventoried; restart returns one account; lookup performs no scan/network.

> **Implementation status (IAP-003, t_0f1a9160):** Tracer 1 is IMPLEMENTED and green. The engine-free
> CLEAN-side account foundation ships under `src/SBPR.Niflheim.HomesteadStones/`:
> `Domain/Accounts/PilotAccountIdentifiers.cs` (opaque 128-bit CSPRNG ids), `Domain/Accounts/PilotDisclosure.cs`
> (disclosure + human-approved privacy-inventory basis), `Adapters/Identity/LookupKeyRing.cs` (versioned
> domain-separated HMAC-SHA-256, ≥256-bit keys, active/previous ring, fail-closed), `Persistence/Accounts/PilotAccountStore.cs`
> (framed CRC journal + boot-rehydrated credential/allowlist indexes + version census + torn-tail quarantine),
> `Persistence/Accounts/PersistedPiiScanner.cs` (mechanical raw-subject scan), and `Application/Accounts/PilotAccountService.cs`
> (atomic account+credential first bind, reconnect, first-bind race, previous-key + multi-hop lazy re-key,
> retirement-gate census, operation conflict/replay). Evidence:
> [`account-identity-pilot-tracer1-evidence.md`](account-identity-pilot-tracer1-evidence.md). The Tracer-1
> acceptance IDs named in the IAP-003 card are green under `dotnet test` and the mod compiles clean (net48,
> 0 warnings). This tracer creates provider-independent account/credential state ONLY; it does NOT yet migrate
> gameplay receipts — the accepted Homestead `AccountId`/`CharacterId` supersession lands with Tracer 3
> (receipt scrub), which is where the accepted Homestead data-model identity rows are reconciled and stamped.
> `AT-AIP-KEY-VERSION-CENSUS`/`AT-AIP-KEY-RETIREMENT-BLOCKED` are realized as the census assertions inside the
> retirement-gate proof; `AT-AIP-REKEY-MULTIHOP` extends `AT-AIP-PREVIOUS-KEY-REKEY` across two sequential rotations.

### Tracer 2 — Character selection + one-session admission

**Goal:** map authenticated profile facts to minted characters, reserve one account session, and preserve creator validation.

**Named acceptance:** `AT-AIP-PROFILE-MINT`, `AT-AIP-PROFILE-RENAME`, `AT-AIP-NAME-NONAUTHORITY`, `AT-AIP-PROFILE-RECONNECT`, `AT-AIP-PROFILE-PREVIOUS-KEY-REKEY`, `AT-AIP-CROSS-ACCOUNT-BLOCK`, `AT-AIP-ADMISSION-LEASE-RACE`, `AT-AIP-ONE-SESSION`, `AT-AIP-STALE-DISCONNECT`, `AT-AIP-CREATOR-BRIDGE`.

**Exit:** two sequential sibling profiles work; concurrent sibling connection rejects; world creator evidence resolves to internal character.

### Tracer 3 — Gameplay principal and receipt migration

**Goal:** route existing progression through internal session principal and remove raw platform identity from all gameplay receipt bindings/logging.

**Named acceptance:** `AT-AIP-PRINCIPAL-SCRUB`, `AT-AIP-RECEIPT-REPLAY`, `AT-AIP-HOSTILE-PRINCIPAL`, `AT-AIP-NO-PROVIDER-HOTPATH`, `AT-AIP-DEFERRED-SURFACE-ABSENT`, plus the existing Foundational dedicated/listen/restart suite.

**Exit:** every current identity/recovery test stays green under minted IDs; mechanical fixture scan finds no provider subject/unkeyed provider digest.

### Tracer 4 — Operator/privacy lifecycle

**Goal:** inspect, disable, export, verifiably delete/purge, reset, retain/purge, and hold data using existing admin authority.

**Named acceptance:** `AT-AIP-ADMIN-INSPECT`, `AT-AIP-ADMIN-DISABLE`, `AT-AIP-LOCAL-BOOTSTRAP-SCOPE`, `AT-AIP-EXPORT-SAFE`, `AT-AIP-DELETE-PURGE`, `AT-AIP-DELETE-REVOKES-ALLOWLIST`, `AT-AIP-PURGE-FALLBACK-RESET`, `AT-AIP-FULL-RESET-ROTATES-KEY`, `AT-AIP-ARTIFACT-CATALOG`, `AT-AIP-PILOT-CLOSURE-DEADLINE`, `AT-AIP-BACKUP-PURGE`, `AT-AIP-RETENTION-PURGE`, `AT-AIP-RETENTION-CONFIG`, `AT-AIP-RETENTION-INCREASE-RENOTICE`, `AT-AIP-HOLD-EXPIRY`, `AT-AIP-RESET-EXPLICIT`, `AT-AIP-QUARANTINE`, `AT-AIP-NO-TIME-TRAVEL`, `AT-AIP-NONADMIN-REJECT`, `AT-AIP-BREACH-RUNBOOK`.

`AT-AIP-ARTIFACT-CATALOG` explicitly creates an uncataloged pilot world fixture and proves startup/admission fails closed, then catalogs that fixture and proves world-save, journal, export, backup, log, quarantine, and reset artifact generations all enter the purge inventory before use/success.

**Exit:** each operation has automated contract/recovery proof and an executable operator runbook; no file editing required.

### Final gate — Dedicated joined-client pilot proof

**Goal:** prove the whole journey in the real game and rehearse recovery/privacy operations.

**Journey:** first bind → profile character mint → progression operation → logout/reconnect → second profile sequentially → concurrent-session rejection → server restart → export → disable → post-disable join rejection → delete/purge dry-run or fixture execution.

**Named acceptance:** `AT-AIP-DEDICATED-JOIN`, `AT-AIP-DEDICATED-RECONNECT`, `AT-AIP-DEDICATED-SECOND-PROFILE`, `AT-AIP-DEDICATED-SECOND-SESSION-REJECT`, `AT-AIP-DEDICATED-RESTART`, `AT-AIP-DEDICATED-DISABLE`, `AT-AIP-OPERATOR-RUNBOOK`.

**Exit:** real joined-client evidence passes; logs-green alone is insufficient.

## Safe parallelism

After Gate 0 and the schema/contract types land:

- **Lane A — account persistence:** journal, HMAC boundary, boot indexes, recovery/performance tests.
- **Lane B — Valheim admission:** provider/profile adapters, session index, creator bridge, dedicated/listen integration.
- **Lane C — operations/privacy:** admin handlers, export allowlist, deletion/retention/backup purge, runbooks.

Tracer 3 integrates A+B. Final gate integrates all lanes. No lane may invent its own account/character identity or persistence format.

## Test strategy

### Highest seams

1. Pure account transitions and canonical HMAC input.
2. Application contracts over fake verified principals and temporary journals.
3. Existing direct-peer runtime binder tests.
4. Process-death/torn-tail replay harness.
5. Dedicated server + joined modded client.

### Required adversarial cases

- payload claims another account/character;
- empty/unsupported/non-allowlisted provider;
- first-bind race;
- credential/profile collision;
- missing/previous HMAC key, second rotation with inactive old-version entries/backups;
- second active connection;
- stale disconnect closing newer session;
- account disable/delete racing gameplay under the per-account mutation fence;
- deleted credential attempting immediate re-creation through a stale allowlist entry;
- uncataloged export/backup and purge-with-counts-but-no-evidence;
- pilot closure deadline with held and unheld artifacts;
- journal tail corruption and partial terminal;
- non-admin operator attempt;
- export/log fixture containing seeded forbidden tokens/IDs;
- provider unavailable after session bind.

### Mechanical verification

- `python3 scripts/docs-lint.py`;
- `git diff --check`;
- contiguous `AIP-FR-001..028` and exact acceptance-ID coverage;
- no account-identity pilot `tasks.md`;
- no `.cs`, config secret, provider app, or runtime dependency in the specification PR;
- source/test build and zero warnings only after implementation authorization.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Exact Steam/PlayFab identity semantics differ from assumption | Gate 0 blocks account creation and names one pilot backend |
| Raw provider identity leaks through old receipt/log code | Tracer 3 explicit removal + seeded mechanical fixture scans |
| HMAC key loss or rotation strands accounts | Separate backed-up secret, active+previous lazy re-key, version census blocks retirement/second rotation, explicit affected reset if census cannot reach zero |
| Journal lookup scales poorly | One replay, indexed reads, 10k structural test; SQLite only if measured |
| `s_playerID` remains accidental domain identity | Profile HMAC + minted CharacterId; creator fact resolved before domain mutation |
| Honest-pilot shortcut becomes public default | Allowlist and mandatory pilot-mode scope; no public registration path |
| Manual privacy operations rot | Commands + tests + runbook evidence are final-gate requirements |
| Append-only history makes individual purge dishonest | Catalog every artifact; prove compaction/removal or reset the disposable whole fixture; counts/tombstones do not pass |
| Three-week scope grows through federation | FR-027 and stop gates prohibit Discord/OIDC/passkeys/portal/recovery work |

## Explicit not-yet scope

- Discord bot/link/role service.
- OIDC/OAuth provider configuration.
- Passkeys/WebAuthn.
- Passwords, email, recovery factors, merges, credential replacement.
- Public registration or account portal.
- Full server character-select UI.
- Cross-server shared accounts/portability.
- Production migration, final legal/retention policy, or public security certification.
- Behavioral anti-cheat identity profiling.

## Package approval gate

Before this package is offered for Daniel approval:

1. all five artifacts exist with proposed status and index entry;
2. mechanical checks pass;
3. an independent verifier compares every requirement, contract, model, and plan against merged design/current code;
4. every blocker and worthwhile non-blocker is corrected;
5. a fresh independent pass returns PASS;
6. the PR states clearly that tasks and implementation are absent.

After Daniel approves the package, task authoring still requires separate authorization. Implementation requires a later, separate authorization after task review.