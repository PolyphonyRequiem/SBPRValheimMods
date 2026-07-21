# niflheim-account-bootstrap (T022)

The thin operator host CLI that provisions **exactly one** real QA subject into the
**isolated HomesteadT009L** account store, over the SHIPPED IAP-009 cores. It owns no
provisioning policy — provisioning is delegated verbatim to the engine-free
`LiveStoreProvisioningGuard` → `LocalAllowlistBootstrap` → `PilotAccountService`, which
HMAC the subject and discard it. This host only does real OS I/O and the T022
confinement/quiescence boundary.

## Build

```sh
dotnet build tools/niflheim-account-bootstrap/NiflheimAccountBootstrap.csproj -c Release
```

Link-compiles the shipped engine-free core source (no game/BepInEx refs), so it builds
headless with no Valheim SDK. Warning-clean (`TreatWarningsAsErrors`).

## Commands

- `preflight` — subject-free proof of target identity, key permissions, store health,
  current notice/retention versions, and restart requirement. Reads NO subject, writes
  nothing.
- `provision` — presents the current privacy disclosure, requires explicit operator
  acknowledgement (`--i-acknowledge-current-disclosure`), reads the provider subject on a
  protected **no-echo TTY**, and provisions exactly one `Steam` /
  `niflheim-pilot-app-896660` allowlist entry.

There is intentionally **no `--subject` flag**: the raw subject can only arrive on the
interactive no-echo TTY (a redirected stdin is refused). Output and the on-disk journal
carry only the HMAC and opaque ids, never the raw subject.

The privacy disclosure notice requires a **routable operator contact**, supplied as
`--operator-contact <email|https-url>` or the `NIFLHEIM_T009L_OPERATOR_CONTACT` env var.
It is disclosure metadata (printed in the notice), **not a secret**. There is no silent
default: an absent, malformed, `.invalid`, or other documented-placeholder value fails
closed (`OperatorContactAbsent` / `OperatorContactMalformed` /
`OperatorContactNonRoutablePlaceholder`) before the subject is ever prompted.

## Fail-closed boundaries

Target confinement (must resolve under `--qa-root`; production `--forbid-root`s hard-refused;
symlink escapes refused), server quiescence (`--server-quiescent` only when the server is
down), store health (a quarantined store escalates), owner-only key permission, disclosure
acknowledgement, and subject-channel discipline — every one fails closed before any subject
is read or any byte is written. The store is never truncated, reset, or reinitialized.

## Operator runbook

See `docs/v2/runbooks/account-identity-pilot-operator-runbook.md` §7 for the full safe
stop → backup → provision → restart → verify → rollback procedure. Do not run this utility
against t009l without review and Daniel's explicit privacy/retention acknowledgement.
