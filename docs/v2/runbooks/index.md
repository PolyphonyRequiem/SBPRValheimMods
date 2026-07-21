# index — docs/v2/runbooks

| file | purpose |
|------|---------|
| README.md | Human orientation for v2 runbooks |
| index.md | This manifest |
| account-identity-pilot-operator-runbook.md | IAP-009 operator-control runbook: local allowlist bootstrap, live-admin inspect, disable (idle + in-flight drain), deterministic session close, delete-drain with allowlist revocation, and failed-drain/process-death recovery. Uses only shipped operator commands + the OS-scoped bootstrap utility; no journal hand-editing. |
| account-identity-pilot-qa-bypass-runbook.md | T022 QA-only ephemeral account bypass runbook (isolated HomesteadT009L). TEST INFRASTRUCTURE, never production. Admits configured server-observed Steam peers into Homestead gameplay under EPHEMERAL opaque QA account/character identities without a PilotAllowlistEntry or any durable account/credential/character record. Default OFF; activates only on a conjunction of server-owned gates (enable flag + exact env tag `homestead-t009l` + exact world/data-root confinement + canonical wildcard-free SteamID allowlist + production hard-refuse). Grants the gameplay principal only, never Valheim admin (separate adminlist step). Rollback = disable the flag; no durable state. |
