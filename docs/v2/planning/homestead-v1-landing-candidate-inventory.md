---
title: "Homestead Stone v1 landing candidate — file isolation inventory"
status: proposed
---

# Homestead v1 landing candidate — isolation inventory

Isolation prep for the accepted Homestead Stone v1 landing. This branch stages ONLY the
already-authored v1 product slice (placement/identity runtime, assets, packaging, deterministic
selector, and the engine-free placement test) from the dirty `feat/niflheim-homestead-stone`
checkout, rebased onto clean `origin/main`. The dirty checkout was treated as read-only input.

Base: `origin/main` @ `eb8cba9`. Candidate branch: `wt/t_3cf66858`.
Authoritative contract: `docs/v2/planning/homestead-stone-v1-impl-spec.md` (already on base).

## Included — v1 product files

Runtime plugin (`src/SBPR.Niflheim.HomesteadStones/`):
- `SBPR.Niflheim.HomesteadStones.csproj` — net48 mod project; hard bundle+checksum packaging gate.
- `Plugin.cs` — BepInEx bootstrap; installs registrar + world-placement Harmony patches only.
- `Domain/HomesteadPlacement.cs` — engine-free deterministic selector + seating domain logic.
- `Features/HomesteadStone/HomesteadStoneData.cs` — reserved D3 zone-coord ZDO keys.
- `Features/HomesteadStone/HomesteadStoneRegistrar.cs` — additive prefab construction.
- `Features/HomesteadStone/HomesteadStoneVisualMotion.cs` — procedural hover/yaw contract.
- `Features/HomesteadStone/HomesteadStoneWorldPlacement.cs` — deterministic selected seating.
- `Assets/Editor/BuildNiflheimHomesteadStone.cs` — Unity Preview Lab bundle builder (not compiled).
- `Assets/Source/{PROVENANCE.md, guardian_basecolor.png, guardian_emission.png,
  guardian_stone_ivy_v9.blend, guardian_stone_ivy_v9.fbx, guardian_stone_ivy_v9.glb}` — LFS.
- `Assets/Bundles/sbpr_niflheim_homestead_stones.unity3d` (+ `.sha256`) — stable runtime bundle
  contract per v1 spec §1 (plain-committed, matching existing `assets/bundles/sbpr_tradertent.unity3d`).

Deterministic offline selector (`tools/niflheim-homestead-selector/`):
- `NiflheimHomesteadSelector.csproj`, `Program.cs` — net8 CLI that link-compiles the same
  `Domain/HomesteadPlacement.cs`; no engine deps, no evidence/harness deps.

Tests:
- `tests/HomesteadPlacementTests.cs` — production selector determinism / per-type target / 128 m
  exclusion / bounded-seat coverage.
- `tests/SBPR.Trailborne.Tests.csproj` — ONE added `<Compile Include>` for
  `Domain/HomesteadPlacement.cs`. The live-evidence `EvidenceReadiness.cs` include was deliberately
  NOT taken.

Repo:
- `.gitattributes` — Git LFS filter rules for the promoted `Assets/Source/` binaries.

## Excluded — unrelated / live-harness / S2

- Committed `feat/niflheim-homestead-stone` tip `aadb6ea` ("accept homestead progression S2 package")
  — S2 progression docs churn (`docs/v2/planning/homestead-stone-progression-*`, tobi doc deletions).
  Not v1 product; excluded entirely.
- `tools/niflheim-homestead-runner/**` — live screenshot/evidence harness (namespace
  `SBPR.Niflheim.HomesteadEvidence`): capture/contact-sheet/watchdog/prepare-run scripts,
  `EvidenceReadiness.cs`, `EvidenceRunnerPlugin.cs`, `inject-and-frame.cs`. Owned by the live
  evidence worker; excluded to avoid overlap.
- `tests/NiflheimEvidenceReadinessTests.cs`, `tests/NiflheimFrameQualityTests.cs` — depend on the
  excluded `HomesteadEvidence` harness; excluded.
- `Assets/Bundles/{Bundles, Bundles.manifest, sbpr_niflheim_homestead_stones.unity3d.manifest}` —
  Unity build-folder index cruft; not referenced by the csproj packaging gate; excluded.
- `bin/`, `obj/` build outputs — gitignored; excluded.

## Verification (all on candidate branch)

- Mod build: `dotnet build src/SBPR.Niflheim.HomesteadStones -c Release` → 0 warnings / 0 errors.
- Selector build: net8 → 0 / 0.
- Full test suite: `dotnet test tests/SBPR.Trailborne.Tests.csproj` → 576 passed / 0 failed.
- Regression: `dotnet build src/SBPR.Trailborne -c Release` → 0 / 0 (main product unaffected).
- Docs lint: `python3 scripts/docs-lint.py` → OK, 128 docs.
- LFS: `Assets/Source/` binaries stage as LFS pointers; bundle sha256 matches on-disk file.
- `git diff --cached --check` → clean.
- No S2 progression / claim-account / build-denial behavior in staged runtime (spec §6 cuts honored).

## Not done here (by design)

Live joined-client frame evidence (v1 spec §7 "Live joined-client gate") is the live evidence
worker's output. The final Homestead v1 landing card integrates THIS candidate with that accepted
live evidence. This branch is `review-required`; it must not merge standalone.
