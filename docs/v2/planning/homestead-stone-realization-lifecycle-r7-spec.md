---
title: "Homestead Stone realization lifecycle (R7) — provenance-in-ZDO, strict fail-closed durability, reproducible extraction"
status: current playtest implementation hypothesis (R7, t_8a46fb4f) — provisional until final pre-release playtest ratification
supersedes: R1–R6 realization approaches (R5 == commit 84ac516, PR #329 rejected; R6 == commit 1e4ba48, PR #332 rejected)
---

# Homestead Stone realization lifecycle (R7)

This spec is the same-PR buildable contract for how a Homestead Stone is *created, reused,
cleaned up, and deferred* on the authoritative server. R7 continues R6's static/manifest
authority split (§0–§8 below are the R6 contract, still in force) and adds the R7 corrections
in §9 that the R6 review (PR #332) required: provider/content provenance wired into ZDO truth,
strict fail-closed ledger durability, truthful/reproducible extraction, a strictly-enforced
manifest contract, and tests that execute the real production seams. It supersedes R1–R4 (live
host colliders / built Heightmap — proven impossible headless by spike `t_24a5d20d`), R5
(PR #329), and R6 (PR #332).

Companion runtime: `src/SBPR.Niflheim.HomesteadStones/`. Engine-free seams live under `Domain/` and
are unit-tested headless; the net48 Unity adapter (`Features/HomesteadStone/`) only reads
authoritative ZDO data + engine callsites and delegates every decision to the engine-free core.

## 0. Two authorities, by host class (load-bearing)

A headless dedicated server never instantiates host colliders or a built Heightmap without a joined
GPU client, so *any* server-side geometry query is doomed. R6 therefore derives seats from **authored
data**, never a live physics read, split by host class:

| Class | Hosts | Share | Seat authority (R6) |
|-------|-------|-------|---------------------|
| Ordinary | `WoodHouse1..13` | 104/114 (91.2%) | **Static geometry catalog** — a checked-in, offline-generated footprint catalog keyed by exact prefab + semantic content hash. |
| Generator | `WoodFarm1`, `WoodVillage1` | 10/114 (8.8%) | **Operational manifest** — a trusted, operator-supplied, versioned manifest (existing-world repair / generator layout). Never a runtime geometry guess; never a player-submittable row. |

`HomesteadHostClassifier` owns the split. The runtime supplies only the host's **realized
transform/rotation** (resolved from authoritative `LocationProxy` ZDO data — location hash, zone,
position, rotation — *not* nearest live proxy) and the terrain height. Geometry is authored data.

## 1. Static catalog is the production authority (Blocker 1)

- Live `LocationProxy` child-hierarchy discovery is removed from seat resolution. Ordinary-host
  footprints come from `HomesteadStaticGeometryCatalog`, loaded at startup from an embedded resource
  (`Assets/homestead-static-geometry.json`) that is **byte-identical** to the test fixture
  (`tests/Fixtures/homestead-static-geometry.json`; enforced by `HomesteadCatalogDriftGuardTests`).
- Host transform/rotation is resolved from authoritative location/proxy ZDO data (`ZDOVars.s_location`
  hash, `ZoneSystem.GetZone`, `ZDO.GetPosition/GetRotation`), not the nearest live proxy.
- **Missing identity / missing catalog entry is retryable/fail-closed (`CatalogUnavailable`), never a
  terminal `GeometryUnavailable`.** A host whose catalog row is temporarily unavailable is re-attempted.

## 2. Correct geometry extraction + one hash contract (Blocker 2)

- The offline extractor (`scripts/extract_homestead_geometry.py`, deps + repro command in its header,
  CI drift-checked via the byte-identical-catalog guard) composes **full 4×4 transform matrices** at
  every hierarchy level — parent rotation and non-uniform/negative scale honoured, not summed local
  positions.
- Per-collider shape math into host space, then a conservative axis-aligned XZ AABB (never
  `collider.bounds`): **Box** = transformed 8 corners; **Capsule** = direction/height/radius extent
  box, transformed; **Sphere** = center + scaled radius (`radius·max(|sx|,|sy|,|sz|)`); **Mesh** =
  conservative transformed mesh-bounds AABB or **fail closed** (never silently discarded).
- Active/enabled/trigger/layer, nearest `Piece` ancestry, and `RandomSpawn` branch semantics are
  preserved; inactive-branch colliders are **conservatively unioned** (we cannot know which branch the
  live world picked). `All_thirteen_ordinary_houses_resolve` pins that all 13 still resolve.
- **ONE canonical semantic hash schema** (`HomesteadGeometryHash`, computed over the stored
  kind-free footprint rows `cx,cz,halfX,halfZ`) is shared by extractor, fixture, runtime catalog, ZDO
  stamp, and tests. Production recomputes and **pins** each host's stored hash at startup; a mismatch
  throws inside catalog load (drift ⇒ regenerate catalog + roll selector version, never silent reseat).
  Tests validate the fixture's **stored** hash, not a differently-shaped recompute.

## 3. Terrain Y at host origin (Blocker 3)

Terrain Y = `WorldGenerator.instance.GetHeight(hostOriginX, hostOriginZ)` — the **host origin**, not
the seat XZ. SPIKE-2 proved the location's `flatten` TerrainModifier levels the ground to host-origin
Y within the level radius (≤ 6.0 m); every seat is clamped inside that radius (INV-1), so it sits on
the same flattened plane. Sampling the seat XZ would read pre-flatten procedural noise and float/sink
the Stone. Parity fixtures validate against the SPIKE-2 height port; no constant-height masking.

## 4. One full-ZDOID reconciler in production (Blocker 4)

The old `ReconcileExisting` is bypassed. Production uses `StoneReconciler` keyed on the full stable
`ZDOID(UserID, ID)` — never a truncated numeric ID — handling unkeyed, unselected, metadata-mismatch,
and duplicate Stones with a deterministic keep/remove (lowest `ZDOID` kept, so the SAME Stone survives
across restarts regardless of enumeration order). **Reconciliation runs before the event gate** so a
stale same-zone entry cannot suppress a needed creation. The adapter wiring is tested, not only the
pure model.

## 5. Ledger semantics & atomic storage (Blocker 5)

Persisted Stone ZDOs are **creation truth**; `HomesteadWorldLedger` records provenance/outcomes and
never overrides missing/mismatched ZDO reality. Keyed by world UID + selector version + host zone +
provider/content hash.

- `Created` is valid **only while a matching Stone exists**; a missing Stone permits recovery
  (re-creation), so a cleared/corrupt world is not silently stuck.
- Stamp/exception failures are recorded. No fake/phantom retries.
- **Atomic store** (`HomesteadLedgerAtomicIo`): write temp → flush/fsync → atomic same-filesystem
  rename/replace **without deleting the old file first**; a valid temp/backup is recovered after a
  crash. An I/O failure **fails closed** for realization (diagnostic) — never silently returns empty
  and retries. Path is rooted/validated per world. Crash-boundary and corruption tests cover this
  (`HomesteadLedgerAtomicIoTests`).

## 6. Operational manifest provider (Blocker 6)

`HomesteadOperationalManifest` + `HomesteadManifestStore` load/reload a configured manifest file and
validate it strictly: exact world UID + selector version (whole-document scope keys), provider version
+ document content digest, per-row host prefab + zone + finite coordinates within zone (±32 m) and
host bounds (≤ 96 m radius), unique `(prefab,zone)`. Malformed / non-finite / unbounded / duplicate
rows are rejected per-row; a document failing scope/provenance supplies **no** seats (forged/mismatched
manifests cannot seat a Stone).

The manifest carries a monotonic **generation**. A `ManifestRequired` outcome is recorded against the
generation current when decided; when a **new generation** with a valid matching row appears, the
resolver is allowed to retry (generation-scoped terminal, not permanent). The document digest +
provider version + generation are **stamped onto the Stone ZDO** and enforced on reuse/reconciliation.
Ordinary players cannot submit rows.

## 7. Drift gate & adapter tests (Blocker 7)

`HomesteadRuntimeDriftCheck.Verify()` runs once at load and its result is **load-bearing**:
`HomesteadStoneWorldPlacement.RealizationEnabled = Verify()`, so a false result **prevents the
realization patches/loop from running**. The check asserts the exact required Harmony targets /
engine callsites / fields (`ZoneSystem.Start/OnDestroy`, `ZNetScene.Awake`,
`WorldGenerator.GetHeight(float,float)`, `WorldGenerator.instance`, `ZoneSystem.LocationsGenerated`,
`ZoneSystem.m_locationInstances`, `ZoneSystem.GetZone`, `ZDO.GetPosition/GetRotation`,
`ZDOVars.s_location`, `ZDOMan.GetAllZDOsWithPrefabIterative`, `LocationProxy`) **and** exercises the
real production authority by loading the embedded catalog and asserting every semantic-hash pin holds
(`AssertCatalogPins` — anti-tautology). It reports rather than throws so a drifted game update degrades
to "no realization + a loud error" instead of a hard crash. Adapter/provider/store/manifest/drift
production files are compiled and tested (fresh location-ZDO transform, catalog load + hash pin,
ledger recovery, manifest reload, and actual reconciler wiring).

## 8. What remains post-merge (not in this card)

End-to-end fresh-world realization + realized-rotation recovery are verified by the scheduled
**T009L2** rerun on the spike's preserved disposable world fixture (world UID `-898655635`, seed
`kniTMtyDpB`) plus AP/retry/reconnect/restart — a joined-GPU-client path that cannot be exercised
headless. This PR delivers the engine-free authority (static catalog for houses, trusted manifest for
generators/existing-world repair) + adapter wiring; T009L2 is the in-engine acceptance gate.

## 9. R7 corrections (PR #332 review)

R7 keeps §0–§8 intact and closes the five gaps the R6 review rejected. Each is wired into the
**production** path and exercised by a test that runs that path, not only a pure model.

### 9.1 Provider/content provenance is wired into ZDO truth (Blocker 1)

A Stone's *creation authority* is now a durable, versioned fact on its ZDO, not something
re-guessed from bare zone existence:

- `HomesteadProvenanceCodec` (engine-free, `Domain/HomesteadProvenance.cs`) is the single source
  of truth for the provenance key names + schema version. `HomesteadStoneData` forwards to it, and
  `HomesteadProvenanceCodecTests` pins every key literal so the codec and the net48 stamp cannot
  drift apart.
- The full provenance = schema version + assignment (world UID, selector version, host prefab,
  zone) + **provider kind + provider version + content hash + manifest generation**. Ordinary hosts
  stamp the catalog digest (provider version) + the host's geometry semantic hash (content hash),
  generation 0; generator hosts stamp the manifest provider version + document digest + generation.
- `StampIdentity` persists **every** field and **read-back verifies** the whole fact through the
  same codec; a partial/torn write fails verification, the Stone is reaped, and **durable failure
  provenance is recorded before cleanup** (no phantom retry loop).
- The reconciler reads the full provenance back through the same codec and compares the **whole
  fact** — a selector/provider/content/generation upgrade is a mismatch that reaps the stale Stone.
  The event gate uses reconciled matching facts, never bare zone existence.
- A ledger `Created` outcome is **advisory only**. When the reconciler reaps the only Stone for a
  zone because its provenance was stale, it flags that zone for recovery and the production loop
  calls `HomesteadWorldLedger.ClearForRecovery`, so a sticky `Created` can never block an upgrade.
  Outcomes are keyed by world UID + selector version + host prefab/zone + provider/content hash +
  generation.

### 9.2 Strict, fail-closed ledger durability (Blocker 2)

- **World path resolution failure ⇒ fail-closed** (`LedgerIoException`), never a fabricated empty
  ledger — a fabricated clean history would phantom-retry every terminal zone.
- The serialized envelope carries the exact **world identity + record count + checksum**. On load,
  a world-identity mismatch, wrong line count (truncated valid-prefix or extra rows), a **duplicate
  zone row**, malformed fields, or a bad checksum invalidate the **whole candidate** (`IsWellFormed
  = false`), so recovery falls back to a valid temp/backup rather than adopting corruption.
- Recovery chooses only fully valid candidates and never prefers a torn temp over a valid primary.
  Writes are temp → flush/fsync → atomic `File.Replace` (keeping a `.bak`) **without deleting the
  old file first**; the first write uses an atomic rename. I/O/corruption **blocks realization with
  bounded diagnostics**. Covered by `HomesteadLedgerAtomicIoTests` + `HomesteadWorldLedgerTests`
  (world-mismatch / truncation / duplicate / garbage / crash-boundary).

### 9.3 Truthful, reproducible extraction (Blocker 3)

- Extraction semantics are unchanged from §2 (full transform matrices, conservative per-shape
  AABBs, RandomSpawn inactive-branch union, mesh fail-closed, one canonical hash).
- Extraction is now **self-contained from checkout**: pinned deps in
  `scripts/homestead-extraction-requirements.txt` and a documented, executable bootstrap in
  `docs/v2/planning/homestead-extraction-bootstrap.md`. The extractor resolves the offline
  `valheim_prefab` module from `VALHEIM_PREFAB_TOOLS` (no hard-coded machine path) and emits a clear
  bootstrap error when deps are missing.
- The drift gate no longer accepts a merely-nonempty catalog: `HomesteadCatalogDriftGuardTests` pins
  the **exact 13 ordinary host names + 2 generator hosts**, the schema string, and **every semantic
  hash**, and asserts the fixture and embedded catalog are byte-identical. CI (no game assets) guards
  the checked-in artifact; regeneration is a deliberate asset-bearing act.

### 9.4 Operational manifest contract (Blocker 4)

The §6 manifest validation stands; R7 stamps the manifest provider version + document digest +
generation onto the Stone ZDO as provenance (§9.1), so reuse/reconciliation enforce the manifest
identity a Stone was created under. A same/lower generation with changed content is rejected; a new
valid higher generation re-arms `ManifestRequired` and the resolver re-stamps the newer provenance.

### 9.5 Real production-seam tests (Blocker 5)

The headless suite exercises the actual production code paths, not only mocks:
`HomesteadProvenanceCodecTests` runs the production `Stamp`/`Read`/`ReadBackMatches` against an
in-memory ZDO surface (the exact seam `ZdoProvenanceAccessor` adapts); `StoneReconcilerTests` drives
the full-provenance reconciler including duplicate/unkeyed/mismatch/stale-provenance/stale-generation
paths; the ledger + manifest + drift-guard tests cover strict parsing, crash recovery, missing-Stone
recovery, selector/provider upgrade, and catalog parity. In-engine-only paths (fresh-world realized
rotation, joined-client craft/build) remain the post-merge **T009L2** gate (§8).
