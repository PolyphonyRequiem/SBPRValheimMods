# index — docs/v2/runbooks

| file | purpose |
|------|---------|
| README.md | Human orientation for v2 runbooks |
| index.md | This manifest |
| account-identity-pilot-operator-runbook.md | IAP-009 operator-control runbook: local allowlist bootstrap, live-admin inspect, disable (idle + in-flight drain), deterministic session close, delete-drain with allowlist revocation, and failed-drain/process-death recovery. Uses only shipped operator commands + the OS-scoped bootstrap utility; no journal hand-editing. |
| niflheim-0003-cold-reload-harness-runbook.md | Wayfinder 0003 QA-only live cold-reload capture harness: mechanical PRE → real save → full client exit → cold reload → POST → fail-closed compare sequence, with dry-run refusal, lease/rollback/fixture/production guards, and explicit non-claims about reload/persistence/playability. |
