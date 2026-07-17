---
title: "IAP-001 — Gate 0: pilot transport-principal evidence"
status: proposed
purpose: Executable Gate-0 evidence for the Niflheim cooperative-pilot account identity — names the one provider backend, proves subject stability and rejection, identifies the protected provisioning input, and inventories upstream raw-subject exposure. No account creation ships.
---

# IAP-001 — Gate 0: pilot transport-principal evidence

**Spec:** [`account-identity-pilot-spec.md`](account-identity-pilot-spec.md) (AIP-FR-001, AIP-FR-006, AIP-FR-019)
**Plan:** [`account-identity-pilot-plan.md`](account-identity-pilot-plan.md) → "Gate 0 — Prove the exact pilot transport principal"
**Contracts:** [`account-identity-pilot-contracts.md`](account-identity-pilot-contracts.md) → "Provider adapter port", "Logging contract"

> **Scope:** This is the Gate-0 proof only. It names and proves the transport credential and the protected
> provisioning input, and inventories upstream exposure. It does **not** create accounts, mint an
> `AccountId`/`CharacterId`, compute an allowlist/credential HMAC, or write any durable record — that is
> Tracer 1 and later, under separate authorization.

## Exit summary

| Gate-0 exit condition (plan) | Result |
|---|---|
| Exactly one provider backend named/proven | **Steamworks** (see §1) |
| Safe operator provisioning input exists | protected no-echo stdin, owner-only path, allowlist-only verb (see §4) |
| Every upstream identity-bearing artifact has a disclosed bounded purge path | inventoried; enrollment fails closed otherwise (see §5) |

All six named acceptance IDs produce real, executed evidence in
`tests/NiflheimPilotProviderGate0Tests.cs` (link-compiles the shipped engine-free core; `dotnet test`
green, 0 warnings).

## 1. Provider backend decision — Steamworks (exactly one)

The one admitted pilot backend is **Steamworks**. Grounds:

- The shipped Homestead transport seam already reads the authenticated **Steam socket host id** off the
  server's own `ZNetPeer` (`m_socket.GetHostName()`) in
  `src/SBPR.Niflheim.HomesteadStones/Features/Progression/ZdoAuthenticatedSenderSource.cs`, and
  `VanillaAdminIdentity.DefaultPlatform` is `"Steam"`. The dedicated server proven by PR #317 authenticates
  Steam sockets.
- **PlayFab is NOT admitted** by this pilot configuration. A PlayFab-namespace subject is rejected as
  `ProviderUnsupported` (proven: `AT-AIP-PROVIDER-NAMESPACE`). Supporting or migrating to it is deferred
  (spec closed-pilot decision #3).

The provider is identified by a `ProviderKey = (namespace, backend/issuer)` so two configurations of the
same namespace never collide (proven: `AT_AIP_PROVIDER_NAMESPACE_SameSubjectDifferentBackend_DoesNotCollide`).

## 2. Server-observed principal, never payload

The gate accepts **one** input: `ServerObservedTransportSubject`, constructed only from a server-read
authenticated host id (Steam socket) plus the opaque per-peer transport handle. The type has no
constructor path that carries a client claim, so a hostile payload asserting another subject cannot become
authority (proven: `AT_AIP_UNAUTHENTICATED_ServerObservedFact_IsOnlyInput_PayloadCannotManufactureIt`).

The net48 edge (`Features/PilotIdentity/ZdoPilotProviderSubjectSource.cs`) reads the peer that actually
delivered the packet on its **direct per-peer `ZRpc`** (reproducing `ZNet.GetPeer(ZRpc)` over the public
connected-peers table by `m_rpc` reference identity) — the PR #317 anti-forgery seam, never the
client-serialized routed sender id. That file references Valheim types and is net48-only (not
link-compiled into the net8 test project), so the acceptance tests prove the engine-free decision the
adapter defers to.

## 3. Rejections (fail closed)

| Condition | Rejection | Evidence |
|---|---|---|
| Empty/absent authenticated host | `UnauthenticatedPeer` | `AT-AIP-UNAUTHENTICATED` |
| Anonymous/placeholder subject (`0`, `anonymous`, `unknown`) | `UnauthenticatedPeer` | `AT-AIP-UNAUTHENTICATED` |
| Subject in a non-selected namespace (PlayFab) | `ProviderUnsupported` | `AT-AIP-PROVIDER-NAMESPACE` |
| Empty/ambiguous/noncanonical subject | `ProviderSubjectInvalid` | `AT-AIP-PROVIDER-NAMESPACE` |
| Misconfigured gate (no namespace/backend) | `ProviderUnsupported` (fail closed) | `AT-AIP-PROVIDER-GATE0` |

## 4. Subject stability across reconnect / restart

The canonical subject is the namespace-stripped bare Steam id, which is durable across reconnect and
server restart (it is the authenticated Steam identity, not the per-session peer handle or the
reconnect-unstable character ZDOID). Two independently observed transport facts with **different**
per-session handles for the same identity resolve to the **same** canonical subject; a different identity
does not; mixed colon/underscore host-id forms of the same identity canonicalize equally (proven:
`AT-AIP-PROVIDER-RECONNECT`).

## 5. Protected operator provisioning input

The exact allowlist subject is obtained/provisioned only through a bounded, non-logging path
(`PilotProvisioningInputGate`):

- raw subject accepted **only** on protected no-echo stdin — never a command-line argument, chat/console
  command, or environment variable (proven: `AT-AIP-PROVIDER-PROVISION-INPUT`);
- the key/data path must be **owner-only** (`0600`-or-tighter); anything group/other-reachable fails
  closed;
- the local utility's verb scope is **allowlist provision/revoke only** — `inspect`/`export`/`disable`/
  `delete`/`reset`/`retention`/gameplay are all rejected as out-of-local-scope, so it can never widen into
  an account-admin API;
- output is redacted of the raw subject and restricted to internal ids/receipt/correlation/result-code
  fields.

This proves the input discipline only; it computes no HMAC and writes no record (Tracer 1).

## 6. Clean Niflheim logs + upstream exposure inventory

**Niflheim subsystem logs stay clean.** The auth log line type (`PilotAuthLogLine`) can only express
allowed fields (timestamp, provider **class**, result code, post-resolution internal ids, correlation id,
build version) — it has no way to attach a raw subject/HMAC/token. A mechanical scrub over seeded
forbidden values (raw subject, HMAC, ticket) finds none in emitted lines, and the same scrub catches a
deliberately leaked control line (proven: `AT-AIP-PROVIDER-LOG-SCRUB`).

**Upstream base-runtime exposure is inventoried, not pretended away** (spec decision #10, AIP-FR-006).
`UpstreamSubjectInventory` enumerates the known upstream artifacts that may carry a raw subject outside
Niflheim's control:

| Upstream artifact | Location hint | Treatment required |
|---|---|---|
| Valheim/BepInEx server log (connecting Steam id) | `BepInEx/LogOutput.log` | access-restricted + scheduled purge (default 14d) |
| Vanilla world-save `s_creator` / `s_playerID` | `worlds_local/<world>.db` | access-restricted; world-fixture lifecycle / whole-fixture reset (default 30d) |
| BepInEx diagnostic log | `BepInEx/LogOutput.log` (diagnostics) | access-restricted + scheduled purge (default 14d) |

Enrollment may open **only** when every inventoried artifact is access-restricted, has a bounded purge
path, and carries a positive bounded retention; a single non-clearing (or an empty/not-yet-inventoried)
inventory fails closed (proven:
`AT_AIP_PROVIDER_LOG_SCRUB_UpstreamInventory_*`). The precise per-world artifact catalog and its purge
execution are owned by Tracer 4 (`AT-AIP-UPSTREAM-WORLD-FACT-INVENTORY`, `AT-AIP-ARTIFACT-CATALOG`); Gate 0
proves the inventory model and the fail-closed rule.

## Source & evidence map

| Concern | Shipped source (engine-free unless noted) | Acceptance |
|---|---|---|
| Provider gate, namespace, canonicalization, rejections, reconnect stability | `Adapters/Identity/PilotProviderGate.cs` | `AT-AIP-PROVIDER-GATE0`, `AT-AIP-UNAUTHENTICATED`, `AT-AIP-PROVIDER-NAMESPACE`, `AT-AIP-PROVIDER-RECONNECT` |
| Protected provisioning input | `Adapters/Identity/PilotProvisioningInputGate.cs` | `AT-AIP-PROVIDER-PROVISION-INPUT` |
| Clean-log contract + upstream inventory | `Adapters/Identity/PilotAuthLoggingContract.cs` | `AT-AIP-PROVIDER-LOG-SCRUB` |
| net48 transport edge (not link-compiled) | `Features/PilotIdentity/ZdoPilotProviderSubjectSource.cs` | (composition; engine-free decision proven above) |
| Executed evidence | `tests/NiflheimPilotProviderGate0Tests.cs` | all six IDs |

## Honesty note (per AGENTS.md)

The engine-free provider decision, rejections, subject stability, provisioning-input discipline, and
clean-log/inventory rules are **executed** under `dotnet test` (798 tests green, 0 warnings). The net48
transport adapter is written against the real direct-per-peer `ZRpc` seam but is **not** exercised on a
live dedicated server in this Gate-0 pass (no Valheim SDK in this environment). Live joined-client proof of
the transport read is owned by the final dedicated-server gate (`AT-AIP-DEDICATED-*`); this document does
not claim it. "Logs green ≠ playable" — what is proven here is the credential decision and its boundaries,
not an in-game join.
