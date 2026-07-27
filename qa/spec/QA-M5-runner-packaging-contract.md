---
title: "QA-M5 — Deterministic T022 runner + QA overlay packaging contract (ADR-0009 §5.1/§6/§7/§8/§10)"
status: accepted
card: t_ded26114
supersedes: none
depends_on:
  - docs/decisions/0009-qa-harness-separate-fail-closed-mod.md   # ADR-0009 (spec anchor)
  - qa/runner/fsm/README.md                                      # adopted transport-neutral FSM core
  - qa/spec/QA-M4-action-evidence-contract.md                    # M4 evidence + adversarial core
---

# QA-M5 — Deterministic T022 runner + QA overlay packaging contract

**Scope.** The buildable implementation spec for ADR-0009 **M5** (packaging + drift
+ deploy pinning) *plus* the external deterministic T022 **runner** that composes
the adopted engine-free FSM into ADR-0009's SOLE-verdict-authority program. It lands
in ONE PR off current `main`.

**Maturity (load-bearing, both directions).** Everything here is **DRY-RUN /
SIMULATED**. Nothing is launched, deployed, or run in-world. The runner is exercised
ONLY against the deterministic in-process `FakeTransport`; the overlay packer writes
byte-reproducible artifacts but installs/deploys nothing. The four named T022
acceptance tests (ISSUE / UPGRADE / TRANSFER / TAMPER) have **NOT** been observed
in-world — that is the separate operator-authorized **M6** card. Any claim of live
coverage, playability, deployment, or observed ATs is false and a blocking review
finding.

## 1. Runner (`qa/runner/`)

### 1.1 Adopted FSM core (unchanged)

The transport-neutral, engine-free FSM at `qa/runner/fsm/` (8 phases
preflight→fixture→ISSUE→UPGRADE→TRANSFER→TAMPER→evidence→cleanup, adapter-driven
receipt/action schemas) is **adopted, not rewritten**. Its 32 pytest cases prove the
no-false-PASS invariant: `verdict == "PASS"` requires all four legs asserted from
correlated receipts AND cleanup confirmed; any missing / reordered / duplicate /
stale / tampered receipt, AT-assertion failure, timeout, crash, cleanup failure,
identity collision, artifact drift, or competing lease forces FAIL.

### 1.2 M5 orchestration envelope (`qa/runner/runner_core/`)

The runner wraps the FSM in the operational envelope ADR-0009 requires. **The runner
is the SOLE verdict authority (ADR-0009 §6)** — the helper emits only descriptive
primitive facts; only the runner composes PASS/FAIL.

| Component | Responsibility | ADR |
|---|---|---|
| `lease.LaneLease` | Exclusive disposable-lane lease: acquire-once, non-reentrant, idempotent release, competing-holder fail-closed. Its sentinel identity is threaded into the FSM `RunContext` so the FSM's own `CompetingLeaseError` fires when the lease is not held. | §5.3 |
| `manifest.ArtifactPinManifest` | Immutable 6-part pin set (product/helper/game/BepInEx/Harmony/scenario); rejects missing/extra parts + malformed sha256; drift detection; lowers into the FSM `Manifest.artifacts`. | §5.1, §8 |
| `timeouts.PhaseTimeoutTransport` | Per-phase (per-primitive) tick budgets layered on the FSM's single global deadline; a primitive over budget raises `PhaseTimeoutError` (an FSM `TransportError`, so it fails closed, never a soft PASS). | §3.2 |
| `evidence.EvidenceDocument` | The single correlated, byte-stable evidence artifact; carries the explicit DRY-RUN maturity banner. Descriptive — the orchestrator stamps the verdict. | §6 |
| `orchestrator.T022RunOrchestrator` | Composes lease → pins → FSM (under phase budgets) → evidence → **final verdict**, ALWAYS releasing the lease. | §6 |

**Verdict composition (§6).** A runner PASS requires **every** one of: FSM returned
PASS **and** the lease was held by us for the run **and** the artifact pins verified
(present + no drift) **and** the evidence correlated at least the four expected
receipts. Any missing precondition forces FAIL. Lease-acquire failure and pin drift
fail closed **before** the scenario is driven (nothing arms unless every precondition
holds, §5.1).

### 1.3 CLI (`qa/runner/sbpr-qa-t022.py`)

Engine-free entry point. `--dry-run --scenario <name>` replays a scripted scenario
through the real orchestrator against the fake transport; `--list-scenarios` lists
them; `--json` emits the byte-stable evidence document. Exit code encodes the
verdict (0 = PASS, 1 = FAIL); a FAIL is the **expected, correct** outcome for every
non-success scenario. Live execution is not implemented (returns exit 2 with an
M6-only notice).

### 1.4 Dry-run coverage (`qa/runner/runner_core/simulation.py`, tests)

Every path is scripted deterministically: `success` (the ONLY PASS), each leg
failure, missing / duplicate / tampered / stale / reordered receipt, peer crash,
per-phase timeout, whole-run global-deadline, cleanup-crash, pin-drift, and
competing-lease. `AT-QA-T022-COLD-30MIN` is realized here as a **timing model only**
(per-phase + global-deadline budgets) — NOT a real 30-minute cold run. The runner
pytest suite (`qa/runner/tests/`) asserts only `success` passes and that a held
lease + verified pins + correlated evidence are each independently necessary.

## 2. QA overlay packaging (`scripts/pack-qa-overlay.py`)

The QA harness is a **separate deterministic overlay** — helper DLL + engine-free
Python runner + a disposable-world BepInEx profile — shipped alongside testing and
**never** referenced by the product installer or release (ADR-0009 §7).

- **6-part SHA-256 manifest:** `helper | runner | contracts | profile | scenario |
  lane_sentinel`, folded into a single reproducible `overlay_digest`.
- **Drift rejection:** `verify` recomputes every part hash over the staged tree and
  fails closed on ANY divergence (`AT`-style negative proven in tests).
- **Explicit lane sentinel:** `lane_sentinel.json` declares `lane: disposable`,
  short retention, and the hard production deny list (Niflheim `2456`, Heistan
  `2466`) — production is denied even if an allowlist is misconfigured.
- **Rollback path:** each `build` snapshots the prior manifest as
  `qa-overlay-manifest.prev.json`; `rollback` restores it (revert a bad publish).
- **Deterministic:** fixed mtimes + sorted entries → reproducible zip + digest,
  version-independent for identical component bytes.
- **Helper honesty:** absent helper DLL → `helper_state: absent`, `publishable:
  false` (explicit, never a silent empty pin). Publishing requires the packed DLL.

### 2.1 Structural exclusion from the product modpack (ADR-0009 §7)

Two independent guarantees, both proven:

1. **Path.** The overlay's output dir is `qa/dist/` — a `qa/` subtree. The
   production-exclusion guard (`scripts/check-modpack-excludes-harness.py`) rejects
   any normalized path with a `qa` segment, so the overlay can never enter a product
   artifact. `qa/dist/` is git-ignored so build output is never committed.
2. **Content.** A packed helper DLL carries the QA assembly-name token + BepInPlugin
   GUID, so the guard's content signature catches it even renamed / case-folded /
   path-traversed. CI proves this against the **real freshly-built net48 helper
   DLL** (existing `qa-harness` job step), not just a synthetic fixture.

## 3. Gates (all green in this PR)

- Runner pytest deterministic: **84** cases (32 adopted FSM + 52 new M5).
- QA isolation + overlay packaging: **18** `unittest` cases (no pip).
- `qa/tests-core` unchanged (baseline 343/343 — no C# touched by this card).
- QA helper + `SBPR.Trailborne` net48 Release 0w/0e (unchanged — no C# touched;
  re-verified by CI).
- Dependency-boundary, modpack-exclude, M0 isolation guards, docs-lint, secret scan.

## 4. Non-goals (explicit)

No game launch, no deploy, no live qualification, no M6, no product release. The
runner-to-live-adapter integration does not exist on this card: the runner drives
the fake transport, and the real M1 loopback/ZRpc transport + M4 wire adapter drop
in behind the same FSM seam in a later, separately-authorized slice.
