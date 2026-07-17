# qa-operator-harness — IAP-010 real-OS operator host

A throwaway-but-tracked QA host that binds the shipped **IAP-009** operator-control
CLEAN cores to **real operating-system resources**, so operator controls can be
proven on the dedicated Niflheim QA server — the "thin host that binds real
stdin / `stat` / console I/O to the cores" the IAP-009 operator runbook deferred
to IAP-010.

It **link-compiles the same shipped core source files** that
`tests/NiflheimOperatorControlTests.cs` compiles (`Application/Accounts/*`,
`Features/PilotIdentity/LocalAllowlistBootstrap.cs`, and their engine-free deps).
No copy or fork — the asserted behaviour IS the shipped behaviour. It references no
Valheim/BepInEx assemblies and runs headless (see `Directory.Build.props`, which
shields it from the repo-root Valheim-SDK gate exactly like `tests/`).

## Run

```bash
dotnet build -c Release
DATA=/some/throwaway/pilot-qa-run
DLL=bin/Release/net8.0/linux-x64/QaOperatorHarness.dll
dotnet $DLL --phase A --data $DATA   # bootstrap → discover → inspect → in-flight disable → failed-drain → delete
dotnet $DLL --phase B --data $DATA   # SEPARATE process: restart recovery + post-disable rejection
```

Phase B is intentionally a second process (new PID) that only reads the on-disk
journal, so restart durability + session-registry clearing are proven across a real
process boundary.

## What it proves / does not prove

Verified: protected OS-scoped allowlist bootstrap (real `stat` fail-closed, no-echo
stdin, allowlist-only verb scope), first-join account discovery, live-admin inspect
(subject-free), disable draining a real concurrent in-flight mutation, deterministic
session close, failed-drain recovery, delete-drain with allowlist revocation,
post-disable rejection, and process-restart recovery. The persisted journal is proven
raw-subject-free by recursive base64 decode.

Not proven (client last mile, Daniel / IAP-011): a Valheim graphical client joining
the headless server and a human visually seeing the kick.

Full executed evidence:
[`docs/v2/planning/account-identity-pilot-operator-qa-evidence.md`](../docs/v2/planning/account-identity-pilot-operator-qa-evidence.md).
