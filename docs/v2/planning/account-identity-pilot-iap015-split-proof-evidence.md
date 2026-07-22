---
title: "IAP-015 — AT-AIP-DEDICATED-SECOND-SESSION-REJECT split-proof harness (executed harness-half evidence)"
status: proposed
author: engineer-systems (t_0d853e78)
purpose: >
  Executed evidence for the SHIPPED-BINARY half of AT-AIP-DEDICATED-SECOND-SESSION-REJECT
  under Option B (owner-approved on t_13db2c95). Records the qa-split-session-harness run
  against the exact compiled candidate admission assembly: SHA-256 attestation, the
  same-account two-peer concurrent-rejection assertions, the non-vacuity negative control,
  and the fail-closed attestation checks. This is the harness half only; the live-GUI
  transport half (a genuine joined modded client on the real dedicated Niflheim server) is
  executed separately in the QA window and is NOT covered here. Uses only the QA-only
  ForTheWort_QA identity; no raw provider subject enters the path.
---

# IAP-015 — Second-session-reject split proof: harness-half executed evidence

**Task:** t_0d853e78 (`engineer-systems`, IMPLEMENT phase) · decision `t_13db2c95` (Option B) · QA journey card `t_8a3a55c6`
**Spec/plan:** [`account-identity-pilot-spec.md`](account-identity-pilot-spec.md) → `AIP-FR-028` / `AIP-SC-008`; [`account-identity-pilot-plan.md`](account-identity-pilot-plan.md) → Final gate split-evidence rider
**Harness:** [`../../../qa-split-session-harness/`](../../../qa-split-session-harness/README.md) (`QaSplitSessionHarness`)
**Regression:** `tests/NiflheimSplitSessionHarnessRegressionTests.cs` (opt-in `SBPR_RUN_SPLIT_HARNESS=1`)

## Scope and honesty statement

This evidence covers **only** the shipped-binary direct-peer half of
`AT-AIP-DEDICATED-SECOND-SESSION-REJECT`. It proves the server-authoritative
one-account/one-session invariant (`AIP-FR-013`) at its real enforcement seam
(`AccountAdmissionIndex.TryReserve` / `LiveSessionAdmission.Admit`), exercised over the
exact compiled candidate admission binary.

It does **NOT** prove:

- Steam's transport layer independently rejecting a duplicate account login (Steam enforces
  that client-side by kicking the first session — which is why the server seam is
  unreachable by two concurrent Steam GUI clients, and why this harness is the only
  mechanism that can exercise same-account concurrency);
- the live transport→provider-auth→`AccountId`→admission→character-mint wiring — that is the
  separate **live-GUI transport half**, run in the QA window on the real dedicated server
  with a joined modded client.

Both halves together constitute `AT-AIP-DEDICATED-SECOND-SESSION-REJECT` PASS. This doc is
the harness half.

## Candidate binary under proof

| item | value |
|------|-------|
| Worktree base commit | `5921e5657c9638a6025c886abd8680d6db27c3a6` (current `origin/main`) |
| Candidate product assembly | `src/SBPR.Niflheim.HomesteadStones/bin/Release/SBPR.Niflheim.HomesteadStones.dll` |
| Candidate assembly SHA-256 | `2a4cc51b8ee5939743bd982954e9ff3e913b4e5f3109e4c4c567b20c78f78fc0` |
| QA-only identity | `ForTheWort_QA` (synthetic opaque subject `ForTheWort_QA-*`, never a live provider subject) |

> The live QA window MUST rebuild the candidate assembly from the exact
> implementation/review head being deployed and re-pin its SHA-256; the harness attests the
> hash it actually loads before running, so a drifted binary fails closed. (Note: the
> preflight `e9789c3` pin belongs to an unmerged T022 branch and is NOT an ancestor of
> current main — do not use it for IAP-015.)

## Executed commands

```
# Build the candidate product assembly (net48 Release, 0 warnings / 0 errors).
dotnet build src/SBPR.Niflheim.HomesteadStones/SBPR.Niflheim.HomesteadStones.csproj -c Release

DLL="$(pwd)/src/SBPR.Niflheim.HomesteadStones/bin/Release/SBPR.Niflheim.HomesteadStones.dll"
SHA="$(sha256sum "$DLL" | cut -d' ' -f1)"        # 2a4cc51b8ee5939743bd982954e9ff3e913b4e5f3109e4c4c567b20c78f78fc0

dotnet build qa-split-session-harness/QaSplitSessionHarness.csproj -c Release -p:CANDIDATE_DLL="$DLL"

# SHIPPED-GUARD proof → RESULT: PASS (exit 0)
dotnet run -c Release --no-build --project qa-split-session-harness/QaSplitSessionHarness.csproj \
    -p:CANDIDATE_DLL="$DLL" -- -e "$SHA"

# Non-vacuity negative control → RESULT: FAIL (exit 1)
dotnet run -c Release --no-build --project qa-split-session-harness/QaSplitSessionHarness.csproj \
    -p:CANDIDATE_DLL="$DLL" -- -e "$SHA" --bypass-guard

# Fail-closed attestation → exit 3
dotnet run -c Release --no-build --project qa-split-session-harness/QaSplitSessionHarness.csproj \
    -p:CANDIDATE_DLL="$DLL"                       # missing -e  → ATTESTATION FAILED
dotnet run -c Release --no-build --project qa-split-session-harness/QaSplitSessionHarness.csproj \
    -p:CANDIDATE_DLL="$DLL" -- -e 00deadbeef      # wrong hash  → SHA-256 mismatch
```

## Captured transcript — SHIPPED-GUARD proof (RESULT: PASS, exit 0)

```
[split-proof] candidate assembly SHA-256: 2a4cc51b8ee5939743bd982954e9ff3e913b4e5f3109e4c4c567b20c78f78fc0
[split-proof] SHA-256 attestation OK (matches expected candidate).
[split-proof] mode=SHIPPED-GUARD (invariant proof)
[split-proof] candidate assembly: .../qa-split-session-harness/bin/Release/net8.0/SBPR.Niflheim.HomesteadStones.dll
[split-proof] assertions:
  [PASS] first peer admission+mint succeeds (Admitted)
  [PASS] first peer resolves an internal AccountId
  [PASS] first peer mints an internal CharacterId
  [PASS] exactly one admission lease is held after first admit
  · internal AccountId (opaque, PII-free): acct-db9c4c5f8ee263073d2f5be26c5504fe
  [PASS] second concurrent same-account peer is REJECTED
  [PASS] second peer rejected at the Admission (lease) stage, before character mint (stage=Admission)
  [PASS] second peer rejection code is AccountAlreadyConnected (got AccountAlreadyConnected)
  [PASS] NO character was minted for the rejected second peer (before=1 after=1)
  [PASS] second peer never publishes a bound principal
  [PASS] first peer's lease remains valid after the second is rejected
  [PASS] first peer's bound principal is still live
  [PASS] still exactly one lease (the second reserved nothing)
  [PASS] closing the first peer releases its live bound principal
  [PASS] the admission lease is released after close
  [PASS] first peer's bound principal is gone after close
  [PASS] a later admission for the same account succeeds after the lease is released (Admitted)
  [PASS] the later admission resolves the SAME internal AccountId (one account, one identity)
  [PASS] exactly one lease again after the later admission
[split-proof] RESULT: PASS
```
(exit 0)

## Captured transcript — non-vacuity negative control (`--bypass-guard`, RESULT: FAIL, exit 1)

```
[split-proof] SHA-256 attestation OK (matches expected candidate).
[split-proof] mode=BYPASS-GUARD (non-vacuity negative control)
[split-proof] assertions:
  [PASS] first peer reserves a lease
  [FAIL] second concurrent same-account peer is REJECTED as AccountAlreadyConnected (got Reserved)
  [FAIL] still exactly one admission lease after the second peer (got 2)
  · bypass-guard mode reserves under two DISTINCT AccountIds, so the invariant assertions above are EXPECTED to fail here; the shipped-guard mode collapses both peers onto ONE AccountId and passes them.
[split-proof] RESULT: FAIL
```
(exit 1) — flipping the one-session guard turns the SAME green assertions red, proving the
positive proof is non-vacuous.

## Captured transcript — fail-closed attestation (exit 3)

```
# missing -e
[split-proof] ATTESTATION FAILED: no expected SHA-256 supplied (-e <sha256>). Refusing to run: an unattested binary cannot be split-proof evidence. ...
# wrong -e
[split-proof] ATTESTATION FAILED: candidate assembly SHA-256 mismatch. expected=00deadbeef actual=2a4cc51b... — the referenced binary is not the attested candidate.
```
(exit 3 in both cases) — an unattested or drifted binary cannot be presented as split-proof
evidence.

## Verification summary (this IMPLEMENT PR)

- Full test suite: `dotnet test tests/SBPR.Trailborne.Tests.csproj -c Release` → **1488 passed, 0 failed, 0 skipped** (harness-runner regression skips without the SDK opt-in).
- Split-harness regression with `SBPR_RUN_SPLIT_HARNESS=1`: **1 passed** (builds + runs the harness three times against fresh binaries; asserts PASS/exit 0, bypass FAIL/exit 1, attestation exit 3).
- Product assembly `SBPR.Niflheim.HomesteadStones` (net48 Release): **0 warnings / 0 errors**.
- Mod assembly `SBPR.Trailborne` (net48 Release): **0 warnings / 0 errors**.
- `docs-lint`, `git diff --check`, and clean-room scan: see the PR handoff.

## Live QA window — remaining half (NOT executed here)

The IMPLEMENT phase does not deploy, stage Niflheim, launch Valheim, or execute live ATs.
The QA window still owes the **live-GUI transport half**: a genuine joined modded Steam
client on the real dedicated `Niflheim` server (`ForTheWort_QA`, world `niflheim.fwl`, seed
`ForTheWort`) completing JOIN → RECONNECT → SECOND-PROFILE → RESTART → DISABLE →
post-disable-reject with captured evidence, run against the exact candidate assemblies whose
SHA-256 this harness attests. Both halves + the operator runbook constitute
`AT-AIP-DEDICATED-SECOND-SESSION-REJECT` PASS.
