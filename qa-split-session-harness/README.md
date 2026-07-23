# qa-split-session-harness — IAP-015 Option-B same-account concurrent-session split proof

This is the **shipped-binary half** of `AT-AIP-DEDICATED-SECOND-SESSION-REJECT`
(spec `AIP-FR-028` / `AIP-SC-008` split-evidence rider; Option B owner-approved on
`t_13db2c95`, architect DECIDE comment 1886).

## Why this harness exists (and why it is not link-compile)

The server-authoritative one-account/one-session invariant (`AIP-FR-013` / spec rule
#5 / `AIP-SC-002`) is enforced at the shipped
`AccountAdmissionIndex.TryReserve` / `LiveSessionAdmission.Admit` seam, which sits
**upstream of and independent from Steam's transport layer**. Steam enforces one live
session per account **client-side** — a second login of the same account kicks the
first — so **no supported joined-GUI path can ever deliver two concurrent transport
peers of one account to the server admission seam**. The two licensed QA identities are
distinct Steam accounts (cross-account, not same-account). Therefore the server-side
same-account reject is unreachable via any supported joined-GUI mechanism, and demanding
live-GUI evidence for *this one AT* was a contract defect, not a harness gap.

Option B keeps the invariant and product semantics unchanged and corrects only the
*evidence method* for this AT: pair the live-GUI transport half with a
production-identical **direct-peer harness** that drives two peers resolving to ONE
internal `AccountId` and proves the second rejects `AccountAlreadyConnected` before
character mint.

## What makes this exact-binary evidence (not a source double)

Unlike `tests/` and `qa-operator-harness/` — which *link-compile* the shipped engine-free
source — this harness takes a **binary `<Reference>`** on the **compiled candidate
product assembly** (`SBPR.Niflheim.HomesteadStones.dll`) and calls the shipped
`LiveSessionAdmission.Admit` / `AccountAdmissionIndex.TryReserve` types with **typed
calls** (no reflection into internals, no source `<Compile Include>`). Before exercising
anything it computes the SHA-256 of the **loaded** assembly and requires it to match a
caller-supplied expected hash. If the hash is absent or mismatched it **refuses to run**
(exit 3). The proven behaviour is therefore the exact shipped, compiled behaviour of the
attested candidate binary built from this implementation/review head.

## What it proves

Against the attested candidate binary, in `SHIPPED-GUARD` mode:

- first peer's full admission + character mint succeeds and holds the sole lease;
- a second concurrent peer (different sibling profile / transport handle) resolving to the
  **same internal `AccountId`** is **rejected at the Admission (lease) stage**, with code
  `AccountAlreadyConnected`, **before any character mint** (character count unchanged, no
  bound principal published);
- the first lease remains valid and its bound principal stays live while the second is
  rejected;
- closing the first peer **releases** the lease and unbinds it;
- a **later** admission for the same account (after release) succeeds and resolves the
  **same internal `AccountId`** (one account, one identity — the fence is not sticky).

## What it does NOT prove (honest scope)

It does **not** prove Steam's transport layer independently rejects a duplicate account
login. Steam enforces that client-side by kicking the first session — which is precisely
why the server seam is unreachable by two concurrent Steam GUI clients and why this
direct-peer harness is the only mechanism that can exercise same-account concurrency. The
**live-GUI transport half** of the AT (a genuine joined modded client on the real
dedicated `Niflheim` server) carries that wiring proof.

## Non-vacuity (`--bypass-guard`)

`--bypass-guard` drives the SAME same-account concurrency scenario against the shipped
`AccountAdmissionIndex.TryReserve`, but deliberately reserves each peer's lease under a
**distinct** synthetic `AccountId` — the exact failure a broken guard would exhibit (two
transport handles not collapsed onto one account). It then asserts the **same** invariant
assertions (`AccountAlreadyConnected`; exactly one lease). Because the guard is bypassed
those identical assertions **fail** and the harness returns `RESULT: FAIL` / exit 1. Flip
the guard and the green goes red — proving the shipped-guard proof is non-vacuous.

## Privacy

The harness feeds only a **synthetic QA-only opaque subject** (`ForTheWort_QA-*`), never a
real provider subject. The admission seam already carries only the internal opaque
`AccountId`. Output is PII-free: result codes and internal ids only — no raw subject, no
HMAC.

## Run it

```
# 1) Build the candidate product assembly (needs the Valheim/BepInEx SDK; see scripts/setup.sh).
dotnet build src/SBPR.Niflheim.HomesteadStones/SBPR.Niflheim.HomesteadStones.csproj -c Release

# 2) Pin its exact SHA-256.
DLL="$(pwd)/src/SBPR.Niflheim.HomesteadStones/bin/Release/SBPR.Niflheim.HomesteadStones.dll"
SHA="$(sha256sum "$DLL" | cut -d' ' -f1)"

# 3) Build + run the harness against the attested candidate binary.
dotnet build qa-split-session-harness/QaSplitSessionHarness.csproj -c Release -p:CANDIDATE_DLL="$DLL"
dotnet run  -c Release --no-build --project qa-split-session-harness/QaSplitSessionHarness.csproj \
    -p:CANDIDATE_DLL="$DLL" -- -e "$SHA"                  # SHIPPED-GUARD proof → RESULT: PASS, exit 0

# Non-vacuity negative control (flip the guard → RESULT: FAIL, exit 1):
dotnet run  -c Release --no-build --project qa-split-session-harness/QaSplitSessionHarness.csproj \
    -p:CANDIDATE_DLL="$DLL" -- -e "$SHA" --bypass-guard

# Attestation is fail-closed: a missing or wrong -e hash refuses to run (exit 3).
```

The CI-gating regression that runs all of the above against freshly built binaries is
`tests/NiflheimSplitSessionHarnessRegressionTests.cs` (opt-in via
`SBPR_RUN_SPLIT_HARNESS=1`, since building the net48 product assembly needs the SDK that
SDK-less CI lacks).

## Exit codes

| exit | meaning |
|------|---------|
| 0 | proof passed (`SHIPPED-GUARD`) |
| 1 | invariant assertions failed (expected in `--bypass-guard`; a real failure otherwise) |
| 2 | bad CLI arguments |
| 3 | SHA-256 attestation failed (missing/mismatched expected hash) |
| 4 | harness error |
