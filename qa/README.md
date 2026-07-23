# qa/ — SBPR QA test-harness subsystem (ADR-0009)

This tree holds the **QA-only** T022 fixture/action harness authorized by
[ADR-0009](../docs/decisions/0009-qa-harness-separate-fail-closed-mod.md). It is a
separate, fail-closed subsystem kept **outside** the product assemblies (`src/`)
and **excluded from the shipped modpack** — see the ADR for the full design and
trust boundary.

## Layout

| Path | What it is | Milestone |
|------|-----------|-----------|
| `SBPR.QaHarness.T022/` | Fail-closed BepInEx helper (net48). **M0: inert skeleton**; **M1: engine-free contract core added under `Contracts/` (verb catalog, capability parser, fail-closed arming + request-admission decision) — compiled in but not yet invoked at runtime (no channel until M2).** | M0 → M4 |
| `SBPR.QaHarness.T022/Contracts/` | **M1:** engine-free (System.* only) typed contracts + capability-manifest parser + fail-closed arming/admission decision. Link-compiled by `tests-core/` and consumed under net48 by the helper. | M1 → M4 |
| `SBPR.QaHarness.T022/ControlPlane/` | **Channels+dispatcher core (engine-free):** owner-local loopback frame parser + `127.0.0.1`/operator-token bind policy (`LoopbackFrameParser`), single-slot deadline-bounded cancellable dispatcher w/ bounded FIFO (`ControlDispatcher`), delivering-peer/connection-generation state (`DeliveringPeerState`), and game-binding seam interfaces + inert fakes written CLEAN-side from the PR #408 map (`GameBindingAdapters`). Decision logic only — the live TCP/ZRpc pump binding these seams to the game is a later slice; helper stays inert. | channels+dispatcher → M4 |
| `SBPR.QaHarness.T022/Fixtures/` | **M3 + M3R (vanilla fixtures + crash-safe owned cleanup):** engine-free owned-resource ledger core (`ResourceCategory`/`OwnedResourceId`/`FixtureBounds`/`ResourceAllowlist`/`FixturePlan`/`FixturePlanValidator`/`IFixtureWorld`/`OwnedResourceLedger`/`SnapshotCodec`) — product identity/AP/ownership/signature/verdict/journal/cache are **structurally unrepresentable** (closed enum + closed allowlist) — plus the canonical `VanillaFixtureManifest`, the `SeamFixtureWorld` bridge to the additive `IVanillaFixtureSeam` (additive-only, product-reject, drift-guard), and the execution-time `FixtureAuthority`/`FixtureProvisioner` recheck. **M3R adds the REAL adapter + crash-safe persistence** (all engine-free here; the game-touching seam/authority live in `Runtime/`): `LedgerSnapshotStore` (atomic temp+fsync+replace write, fail-closed read, observable `Delete`), `FixtureOwnership` (durable exact ownership markers — world uid + run nonce + fixture id + owned id — stamped on each spawned ZDO, with a **bounded, typed complete/refused** fail-closed marker recovery: recovery hands the seam a `FixtureWorldScope` — allowlisted prefabs + max radius + hard candidate cap — and the engine-bound seam answers with a pinned `ZoneSystem.GetZone` + `ZDOMan.FindSectorObjects` sector query, NOT a whole-world walk; any binding/enumeration/read fault or cap overflow refuses with zero candidates and zero world mutation), `FixtureRequestMapper` (verb+args → bounded vanilla plan, product-id refused pre-allowlist), `ServerFixtureExecutor` (durable-marker recovery + snapshot load + reconcile → authority gate → ensure/cleanup → atomic snapshot; owned-only across restart, fail-closed on marker integrity violations), and `FixtureVerbExecutorBridge` (admitted fixture verb → gated lifecycle, deterministic fixture id for crash recovery). **No craft/upgrade/transfer/tamper actions and no product state; no in-game execution verified (that is M6).** | M3 → M4 |
| `contracts/` | JSON Schema wire truth (request/receipt/envelope). **M0: disabled placeholders; M1: real schemas, kept in sync with `VerbCatalog`/`RejectReason` by `tests-core` guards.** | M0 → M2 |
| `SBPR.QaHarness.T022/Runtime/` | **M2R + M3R engine-bound adapters (net48, game-touching):** the thin Valheim/Unity implementations of the engine-free seams — `ControlPlaneComponent` pump, `QaServerRpcBridge` per-peer ZRpc, `ZNetServerAuthorityRecheck` (M2R), and **M3R** `ZNetVanillaFixtureSeam` (TRUE additive vanilla construction per ADR-0006 — `new GameObject` + intended `AddComponent`s from a read-only blueprint, no prefab clone — plus durable exact ownership-marker stamping + fail-closed marker discovery) + `ZNetServerAuthoritySource` (live server/world/admin recheck). Compiled only under net48 (references the game); the engine-free brains they plug into are headlessly tested. | M2R → M4 |
| `runner/` | Engine-free external Python runner — the sole scenario state machine + PASS/FAIL composer. **M0: skeleton (`--dry-run`).** | M0 → M5 |
| `tests/` | M0 isolation guard tests (`AT-QA-NO-PRODUCT-REF`, `AT-QA-MODPACK-EXCLUDES-HARNESS`). | M0+ |
| `tests-core/` | net8 xUnit suite link-compiling the engine-free `Contracts/*.cs`, `ControlPlane/*.cs` **and** `Fixtures/*.cs`. **M1** ATs: AT-QA-DISABLED-BY-DEFAULT, PROD-WORLD-REJECT, EXACT-WORLD-UID, BAD-NONCE-REJECT, OUT-OF-MANIFEST-REJECT, OUT-OF-BOUNDS-ARG-REJECT, REPLAY-REJECT. **Channels+dispatcher** ATs: AT-QA-LOOPBACK-ONLY, NO-SCRIPTTOOLS-LOCK, SERVER-NO-LISTENER, BUSY-TIMEOUT-CANCEL, REMOTE-FIXTURE-REJECT, PEER-SUBSTITUTION-REJECT. **M3 fixtures** ATs: AT-QA-FIXTURE-VANILLA-ONLY, AT-QA-CLEANUP-NO-LEAK (plus product-prefab rejection, non-additive-clone rejection, real-bounds overflow, delivering-peer/stale-generation/non-admin/non-server/world-not-loaded recheck with no world side effect, partial failure, crash reconcile, unrelated-object preservation). **M3R repair** ATs (durable exact ownership markers): crash-before-snapshot survivor adoption, corrupt-snapshot-never-treated-as-empty, duplicate/foreign-world/foreign-run/unexpected/malformed marker fail-closed refusal, unmarked same-prefab preservation, marker-write-failure as a create failure (no leak), observable snapshot-delete failure, and the additive-construction no-clone-path structural guarantee. Runs headless (no Valheim SDK). | M1+ |

## Firewall (load-bearing — do not undo without a new ADR)

- **No product dependency, either direction.** `qa/**` references no `src/SBPR.*`
  product project/DLL, and no product project references `qa/**`. Enforced
  structurally by `scripts/check-qa-dependency-boundary.py` (gate
  `AT-QA-NO-PRODUCT-REF`).
- **Never in the product modpack.** `scripts/pack-modpack.sh` is an explicit
  allowlist that never globs `qa/**`, and asserts post-stage that no
  `SBPR.QaHarness*` artifact is present; `scripts/check-modpack-excludes-harness.py`
  re-verifies the staged tree and built zip, resistant to case-fold / rename /
  nested-path / path-traversal evasion (gate `AT-QA-MODPACK-EXCLUDES-HARNESS`).
- **Fail-closed.** The helper is default-disabled. **M1** adds the engine-free
  fail-closed **arming gate** (`Contracts/ArmingGate.cs`) — AND-composed and
  fixed-order: exact world UID **and** name, hard production deny list (Niflheim
  `2456` / Heistan `2466`, refused even if the allowlist is misconfigured), explicit
  role/actor, immutable product/helper/game/BepInEx/Harmony/scenario hash manifest,
  nonce/expiry, and a capability manifest enumerating exactly which catalog verbs are
  permitted — plus the per-request **admission gate** (`Contracts/RequestAdmission.cs`)
  covering nonce/role/world/HMAC/out-of-manifest/out-of-bounds-arg/expiry/sequence/
  idempotency-replay. These are **pure decision logic, not yet driven at runtime**: no
  channel exists to deliver an arm manifest until M2, so the helper still cannot arm.
- **The harness never fabricates product state and never emits a product verdict.**
  Only the external runner declares PASS/FAIL, and only after all four T022 ATs
  plus cleanup are confirmed (ADR-0009 §4, §6).

## Running the M0 + M1 guards locally

```
python3 scripts/check-qa-dependency-boundary.py
python3 -m unittest discover -s qa/tests -p 'test_*.py'
python3 qa/runner/sbpr-qa-t022.py --dry-run
dotnet test qa/tests-core/SBPR.QaHarness.T022.Core.Tests.csproj -c Release
```
