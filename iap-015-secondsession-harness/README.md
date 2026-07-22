# IAP-015 — `AT-AIP-DEDICATED-SECOND-SESSION-REJECT` exact-binary evidence half

Owner-approved **Option B** (Daniel, Discord `1529507269027434728`; architect DECIDE
`t_13db2c95`, comment 1886). This harness implements the second of two conjoined
obligations that together constitute `AT-AIP-DEDICATED-SECOND-SESSION-REJECT`:

1. **Transport/admission (live joined GUI — elsewhere):** one genuine joined modded
   Steam client on the real dedicated `Niflheim` server proves the production
   transport → auth → `AccountId` → admission → mint wiring is real.
2. **Same-account concurrent rejection (this harness):** a production-identical
   direct-peer harness that **reference-links the shipped admission binaries** and
   presents **two transport peers resolving to ONE authenticated `AccountId`**,
   asserting the second reserve rejects `AccountAlreadyConnected` **before** any
   character mint, the first lease mints normally, and the lease releases on close.

## Why a harness and not a second live GUI client

Steam enforces one live session per account **client-side** — logging one account into a
second client kicks the first. No supported Steam GUI path can ever deliver two
concurrent transport peers of one account to the server admission seam. A
production-identical direct-peer harness is therefore the **only** mechanism that can
exercise same-account concurrency against the real server-authoritative seam
(`AccountAdmissionIndex.TryReserve` + `LiveSessionAdmission.Admit`). This is recorded
verbatim in the `AIP-SC-008` rider in
[`../docs/v2/planning/account-identity-pilot-spec.md`](../docs/v2/planning/account-identity-pilot-spec.md).

## Exact-binary requirement (what makes this NOT a mock)

Unlike `tests/` and `qa-operator-harness/` (which link-compile shipped **source**), this
harness **reference-links the COMPILED shipped assemblies** and, at runtime,
SHA-256-hashes the on-disk assembly file that actually provided the admission types,
printing it as evidence. A re-implemented, mocked, or source-recompiled admission core
does not satisfy this AT — the emitted hash proves the assertion ran against the linked
shipped binary.

## Running

```bash
# Build the candidate mod from src/ and link it (requires VALHEIM_MANAGED + BEPINEX_CORE):
./run-second-session-harness.sh

# Or pin against an operator-staged shipped artifact and enforce its hash:
export IAP015_EXPECT_ADMISSION_SHA256=<expected sha256 of the staged HomesteadStones.dll>
./run-second-session-harness.sh /abs/path/SBPR.Niflheim.HomesteadStones.dll /abs/path/SBPR.Trailborne.Core.dll
```

Exit code `0` == PASS. The run prints per-assertion results, the linked binary SHA-256,
and a `BEGIN-EVIDENCE-JSON … END-EVIDENCE-JSON` block for the operator runbook / review.

## Scope guardrails

- Uses the disposable `ForTheWort_QA` identity only; no real provider subject enters the
  harness path (the admission seam carries only the internal opaque `AccountId`).
- Does not weaken the one-account/one-session invariant or the no-merge/no-link identity
  prohibition — it exercises the shipped invariant unchanged.
- The `AdmissionDll` / `TrailborneCoreDll` paths are supplied at build time so the harness
  always pins to whatever exact candidate/shipped build an operator staged.
