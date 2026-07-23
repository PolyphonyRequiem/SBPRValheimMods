---
title: "ADR-0009: QA-only T022 fixture/action harness — a separate, fail-closed BepInEx helper + external runner"
status: accepted
---

# ADR-0009: QA-only T022 fixture/action harness — a separate, fail-closed BepInEx helper + external runner

- **Status:** accepted
- **Date:** 2026-07-22
- **Deciders:** Daniel (gate) + architect (this spec)
- **Card:** t_57cb1d88 (DESIGN REPAIR / CONVERGENCE ONLY). No implementation, no
  game launch, no deploy, no production touch, no implementation cards on this
  card. Supersedes the DESIGN authorization of t_5a294bfe.

> **This ADR is the single authority.** It converges two prior divergent
> proposals into one hash-pinned design. Where it conflicts with **architect
> comment 1982** (t_5a294bfe) or the **t_18470c4d** `_kanban-artifacts` proposal,
> **this ADR wins and those are superseded** (see §Superseded inputs). It is long
> because it is the *authorizing* record for a whole new testing subsystem — the
> load-bearing decision (§Decision) is short; the rest is the buildable design
> (identifiers, command schemas, packaging/CI contract, the folded threat model
> and acceptance matrix, milestone decomposition). Implementation cards are cut
> only after Daniel opens design review; **M6 live qualification is a separate
> operator authorization, never opened by this ADR.**

---

## Superseded inputs (explicit)

This ADR resolves prior divergence. The following are **historical context only**
and are superseded where they conflict with anything below:

- **architect comment 1982** (t_5a294bfe) — proposed a reusable `SBPR.QaHarness`
  under `qa/live/` (later `tools-qa/`) with an in-game **console** control surface.
  **Superseded:** the console surface is rejected (§5.2, re-entry risk); the
  reusable-everywhere scope is narrowed to a T022 slice (§Decision); the path is
  fixed to `qa/` (§1).
- **t_18470c4d** `_kanban-artifacts/t022-test-helper-design/*` — proposed
  `qa/SBPR.QaHarness.T022/` with a loopback-TCP channel and a **client-only** helper
  (no server-side component), gated on world **name** only. **Superseded where it
  conflicts:** this ADR keeps the loopback-TCP channel and the T022 scope from that
  proposal, but **adds a server-side helper half** (server-only fixtures via
  authenticated per-peer ZRpc, §2/§3.2) and **strengthens the gate to exact world
  UID *and* name** (§5.1). The three external files are reduced to
  `SUPERSEDED BY ADR-0009` pointer records (§Appendix C).
- **`QaT022Driver` / `tools-qa/driver/QaT022Driver.cs`** and any ad hoc
  `run_script` console probes — **untracked, never committed**, and cited here only
  as **historical/unverifiable observations**. No claim in this ADR rests on that
  scratch code. If any observation from it is ever to become a design input, it
  must arrive with an **exact blob sha256**; unpinned scratch code is **not** repo
  authority. Notably, the old driver's `CmdOffer`/`CmdBuy` discriminator was
  observed as `0/1` against the product's real `1/2`
  (`MasterworkOwnershipProvisioningAdmin.cs:68-73,108`) — a **false-sent risk** that
  is one more reason the driver is **retired** (§9), not generalized.

---

## Context

### What forced this

The T022 Masterwork joined-client QA node (historical research
`docs/v3/research/QA-T022-masterwork-joined-client.md`, carried in the PR #388
lineage — a background reference, **not** an ADR-0009 or QA-M0 dependency)
exposed a durable gap, not a one-off. Live QA can safely launch a headless
dedicated server + two genuinely-joined GABS clients, but it **cannot
reproducibly establish ordinary vanilla fixtures** (a crafting station, the raw
materials, a placed stone) **or drive bounded gameplay actions** (craft, upgrade,
drop→pickup transfer, a controlled tamper). Consequences already paid:

- T022 **ISSUE** passed, but the **UPGRADE** node burned ~180 agent turns manually
  hunting chests/materials, **wedged ValBridge** with an item-loot loop, and never
  reached the transfer/tamper legs.
- The stopgap `QaT022Driver.cs` was **untracked, ephemeral, scenario-specific**,
  carried a discriminator mismatch (above), and is not accepted architecture. Ad
  hoc `run_script` console probes are equally unrepeatable and share the exact lock
  that wedged the run.

Daniel explicitly stopped the T022 live run and directed a **durable solution: a
distinct QA-only BepInEx helper mod**, kept separate from SBPR product libraries
but shipped alongside test artifacts/profiles. This ADR chooses the **smallest safe
T022 slice now, generalizable only after one qualified cycle.**

### Repo facts this design is grounded on (verified, not assumed)

- **Product assemblies:** `src/SBPR.Trailborne` (`net48`,
  `<TreatWarningsAsErrors>true`, BepInPlugin GUID `net.danielgreen.sbpr.trailborne`
  family) → depends on `src/SBPR.Trailborne.Core`; and
  `src/SBPR.Niflheim.HomesteadStones` (BepInPlugin
  `net.danielgreen.sbpr.niflheim.homesteadstones`). All reference the Valheim
  managed assemblies via the repo-root `Directory.Build.props` SDK gate.
- **The SDK-gate escape hatch already exists and is proven.** `tests/` and
  `qa-operator-harness/` each ship a **local `Directory.Build.props` that
  deliberately does NOT import the repo root** — MSBuild stops at the first
  `Directory.Build.props` walking up, so those subtrees are shielded from
  `SbprValidateSdkPaths` and reference no game/BepInEx assemblies. The **external
  runner is engine-free by another route entirely** (Python, §1) and needs no SDK
  shield; the **helper plugin** necessarily references the game.
- **Packaging is a single source of truth.** `scripts/pack-modpack.sh` assembles
  the modpack by **explicit allow-listed overlay** — it copies only
  `SBPR.Trailborne.dll` + `SBPR.Trailborne.Core.dll` + icons/textures/bundles/
  configs + the one bundled `ServerDevcommands`. There is **no wildcard glob of
  `src/**` or `BepInEx/plugins/**`**, so a new project is excluded *by default*;
  exclusion is the current behavior, and we make it **enforced** (§7).
- **CI** (`.github/workflows/ci.yml`) builds product + tests + workbench and packs
  the modpack as an artifact; **docs** (`docs.yml`) runs `scripts/docs-lint.py`
  (two-file rule, `status:` frontmatter, no broken relative links). Release
  (`release.yml`) is tag-driven, deterministic, publish-then-PR (ADR-0004).
- **Product integrity is server-only by construction** (the reason the T022 last
  mile is a code gap, not a tooling gap): the Workmanship HMAC key
  (`WorkmanshipIntegrityKey`, "the raw key lives only server-side",
  `ItemProvenance.cs:205-224`) is armed **only** inside
  `FoundationalRuntimeBootstrap.OnZNetAwake` when `ZNet.IsServer()`
  (`FoundationalRuntimeBootstrap.cs:37,47`), and issuance additionally requires
  `player == Player.m_localPlayer`. The provisioning admin path uses
  `CmdOffer=1`/`CmdBuy=2` (`MasterworkOwnershipProvisioningAdmin.cs:68-73,108`).
  **This is the trust boundary the harness must never cross** (§4).

### Constraints inherited

- **ADR-0001 clean-room:** vanilla is fair game to read/adapt; other mods only via
  the Chinese-wall RE process; never *commit* copyrighted binaries.
- **ADR-0006 additive construction:** no runtime prefab cloning; read vanilla
  prefabs as blueprints via `ZNetScene.GetPrefab`. The harness's fixture
  primitives obey this — they use vanilla *spawn* seams, not subtractive clones.
- **spec⇄code⇄manifest triangle** (CONTRIBUTING): impl cards ship spec+code+manifest
  together; this ADR is the spec anchor.
- **"Logs green ≠ playable."** The harness helps *produce* joined-client evidence;
  it must **never itself declare a product acceptance-test PASS**.

---

## Decision

**Build a QA-only T022 subsystem in two separated halves, both outside the product
assemblies, both excluded from the shipped modpack, the plugin half fail-closed by
default. Choose the smallest safe T022 slice now; generalize to a reusable harness
only after one qualified T022 cycle.**

1. **`SBPR.QaHarness.T022`** — a distinct **fail-closed BepInEx helper**
   (`net48`, `AssemblyName=SBPR.QaHarness.T022`, output `SBPR.QaHarness.T022.dll`,
   BepInPlugin GUID **`net.danielgreen.sbpr.qa.harness.t022`**) living at
   **`qa/SBPR.QaHarness.T022/`**. The **same role-gated assembly** loads on the
   **primary GUI client**, the **valbot GUI client**, and the **isolated dedicated
   server**. It exposes a **narrow, typed catalog of bounded vanilla fixture /
   action / observation primitives**. It references the Valheim/BepInEx SDK (it
   must, to touch the game) but **must not `ProjectReference` or otherwise link any
   `src/SBPR.*` product assembly**, and product code never references it. It is
   **default-disabled**, refuses to arm outside a disposable-world allowlist, and
   emits **structured JSON receipts for primitives only** — never a verdict.

2. **External runner** — `qa/runner/sbpr-qa-t022.py`, an **engine-free Python
   program**. It is the **sole scenario state machine and the sole PASS/FAIL
   composer**, correlating server + both clients into final evidence. The helper is
   dumb primitives; the runner is the brain. It **cannot emit PASS without all four
   named T022 ATs asserted and cleanup confirmed.**

**Two fixed control surfaces, nothing else:**

- **GUI clients expose a dedicated owner-local loopback TCP/JSON request channel**
  (`127.0.0.1` bind), **completely independent of ValBridge / Terminal /
  ScriptTools locks** — its own single-slot main-thread dispatcher. This is the
  only way the runner talks to a client helper.
- **The dedicated server exposes NO host listener.** Server-only world fixtures are
  **requested by an authenticated GUI helper over direct per-peer ZRpc**. The
  server binds the **actual delivering peer**, validates
  **capability/HMAC/role/admin/sequence**, performs **bounded vanilla fixture
  operations**, and returns **primitive receipts**. There is no loopback socket, no
  console relay, and no scenario RPC on the server.

**Nothing beyond those two fixed surfaces exists:** no arbitrary eval, no broad
reflection, no scene-wide scans, no console relay, no method/prefab/path/shell/
network surface. The command catalog is finite and manifest-compiled.

Product code remains the **system under test**: every product state transition
(entitlement, identity, ownership, AP/BP, relationship, Workmanship signature,
snapshot, journal, cache, verdict) must flow through the **normal authenticated
product seams** on the authoritative server. The harness may synthesize only
**ordinary allowlisted vanilla prerequisites** (station, materials, position) in a
disposable world — nothing product-authored.

This separation is load-bearing: **you must not undo it without a new ADR.**
Folding QA primitives into a product assembly, letting the helper ship in the
modpack, adding a server host listener, putting the command surface on the game
console, or letting the helper emit a product AT verdict each re-opens exactly the
failure modes (untracked scenario drift, entitlement forgery, ValBridge deadlock,
"logs-green≠playable" self-certification) this ADR exists to close.

---

## Component & trust-boundary diagram

```
  DISPOSABLE-WORLD QA TOPOLOGY (never production)
  ┌─────────────────────────────────────────────────────────────────────┐
  │  EXTERNAL RUNNER  (engine-free Python, owns state machine + PASS/FAIL)│
  │  qa/runner/sbpr-qa-t022.py                                            │
  │    • scenario state machine (T022 = issue→upgrade→transfer→tamper)   │
  │    • mints run nonce + capability manifest + per-request HMAC        │
  │    • correlates receipts from server + client A + client B          │
  │    • emits final evidence JSON  ── the ONLY thing that says PASS/FAIL│
  └───────▲────────────────────────▲────────────────────────────────────┘
          │ loopback TCP/JSON        │ loopback TCP/JSON
          │ (owner-local, 127.0.0.1) │ (owner-local, 127.0.0.1)
   ╔══════╪════════════╗      ╔══════╪════════════╗
   ║  CLIENT A (GUI)   ║      ║  CLIENT B (GUI)   ║
   ║  SBPR.QaHarness   ║      ║  SBPR.QaHarness   ║
   ║  .T022 role=Client║      ║  .T022 role=Client║
   ║  + PRODUCT mods   ║      ║  + PRODUCT mods   ║
   ╚═══════╤═══════════╝      ╚═══════╤═══════════╝
           │ actions via vanilla input seams (craft/upgrade/drop/pickup)
           │
           │  authenticated per-peer ZRpc  (NO host listener on server)
           │  envelope: {nonce, seq, expiry, HMAC, role, worldUID, capability}
           ▼
   ╔═══════════════════════════════════════════════════════════╗
   ║ HEADLESS DEDICATED SERVER  role=Server                    ║
   ║ SBPR.QaHarness.T022  + PRODUCT mods                        ║
   ║  • binds the actual DELIVERING peer (not the claimed one)  ║
   ║  • validates capability / HMAC / role / admin / sequence  ║
   ║  • bounded vanilla fixture ops (station/mats/position)     ║
   ║  • returns primitive receipts                             ║
   ╚═══════════════════════════╤═══════════════════════════════╝
                               ▼
   ┌───────────── TRUST BOUNDARY (never crossed) ──────────────────────┐
   │  Product integrity seams — server-only, authenticated:            │
   │   WorkmanshipIntegrityKey (raw key server-only, ItemProvenance    │
   │     .cs:205-224), armed only when ZNet.IsServer()                 │
   │     (FoundationalRuntimeBootstrap.cs:37,47)                       │
   │   entitlement / identity / ownership / AP/BP / relationship /     │
   │     signature / snapshot / journal / cache / verdict              │
   │  HARNESS MAY: trigger craft at a real station with real mats,     │
   │               spawn allowlisted vanilla mats/stations/position.   │
   │  HARNESS MAY NOT: mint a key, forge/copy a signature, grant       │
   │               AP/BP/ownership/entitlement/relationship, set       │
   │               identity, write a snapshot/journal/cache, or assert │
   │               a product verdict.                                  │
   └───────────────────────────────────────────────────────────────────┘
```

The harness sits **outside** the product boundary and pokes the game the way a
player would (spawn a station, place mats, press craft); product state changes
happen **because the product mod reacted**, exactly as in a real session. That is
what makes the resulting evidence genuine rather than staged.

---

## Design decisions (one resolved value per axis)

### 1. Location, identifiers, references, dependency boundary

Every identifier is fixed to exactly one value:

| Axis | Value |
|---|---|
| Helper path | `qa/SBPR.QaHarness.T022/` |
| Helper assembly / namespace | `SBPR.QaHarness.T022` → `SBPR.QaHarness.T022.dll` |
| BepInPlugin GUID | `net.danielgreen.sbpr.qa.harness.t022` |
| External runner | `qa/runner/sbpr-qa-t022.py` (Python, engine-free) |
| Wire schemas | `qa/contracts/` (request/receipt/envelope JSON schema) |
| Scenario definition | `qa/scenarios/t022.json` |
| Client control channel | owner-local loopback TCP/JSON, `127.0.0.1` |
| Server control channel | authenticated direct per-peer ZRpc (no host listener) |
| Role model | one assembly, role ∈ {`Server`, `Client`} chosen at load from explicit config/env the runner sets |

- **Helper build:** `net48`; own `Directory.Build.props` that **imports the
  repo-root props** (it needs `$(ValheimManaged)`/`$(BepInExCore)`) but sets its own
  `AssemblyName`, GUID, `<TreatWarningsAsErrors>true` (match product discipline).
  **No `ProjectReference` to any `src/SBPR.*`.** A CI guard asserts this (§7).
- **Runner:** plain Python; owns the scenario JSON, mints the run nonce +
  capability manifest, signs each request, correlates receipts. No game
  dependency, no SDK gate, no product import.
- **Contracts:** the JSON schemas under `qa/contracts/` are the shared wire truth;
  the helper validates every inbound request against them and rejects anything
  off-schema. No product types cross the wire.

### 2. Client/server role model

The **same assembly** loads in three processes; role is chosen at load from an
**explicit config/env signal the runner sets**, never inferred:

| Process | Role | Responsibilities |
|---|---|---|
| Headless dedicated server (isolated, disposable world) | `Server` | fixture setup (spawn allowlisted station/mats/position via server-authoritative vanilla seams), cleanup, server-side observation receipts — **only** over authenticated per-peer ZRpc; **no listener socket** |
| Primary GUI client | `Client` | drive local-player actions (craft/upgrade/drop), inventory/tooltip observation as the joined crafter; expose loopback channel to the runner |
| valbot GUI client | `Client` | receive transferred item, observe post-transfer validation; the transfer counterparty; expose its own loopback channel |

The **runner** decides which primitive runs on which role and refuses a
role-inappropriate request. A client cannot run a server-only fixture primitive;
the `Server` helper only acts on a fixture request delivered by an **authenticated
GUI helper peer** whose **actual delivering peer** it binds (not a claimed
identity), rejecting with `role_mismatch` / `peer_unbound` otherwise.

### 3. Typed command / API surface

Every command is a **named, bounded verb with a typed schema** — **no arbitrary C#
eval, no broad reflection, no scene-wide scans, no console relay, no sleeps, no
monolithic "do-scenario" verbs, no prefab/type/method/file/network/shell surface.**

#### 3.1 Verb families

- **Fixture (Server role, delivered via authenticated per-peer ZRpc only):**
  `SpawnStation{prefab, pos}`, `GrantVanillaMaterials{itemId, qty}` (ordinary
  allowlisted vanilla items only), `PlaceVanillaPiece{prefab, pos}`,
  `SetWorldTime{phase}`. All bounded by **exact IDs / counts / radius** and tracked
  in an **owned-resource ledger**.
- **Action (Client role, delivered via loopback TCP/JSON only):**
  `Craft{recipeName, station}`, `UpgradeItem{itemSlot, targetQuality}`,
  `DropItem{itemSlot}`, `PickUpNearest{itemName, radius<=Rmax}`,
  `TamperField{itemSlot, field}` — a *controlled* edit that may **replace or remove
  an existing allowlisted field only on an exact tracked throwaway item**; it may
  **never add or copy a valid signature field**.
- **Observation (either role):** `ReadInventory{}`, `ReadItem{itemSlot}`,
  `ReadTooltip{itemSlot}` (surfaces the in-world text a human would see — this is
  what proves `Workmanship=Masterwork` is *visible*), `ReadWorldName{}`,
  `ReadWorldUid{}`.
- **Lifecycle:** `Arm{nonce, manifest, expiry}`, `Ping{}`, `Cleanup{scope}`,
  `Disarm{}`.

#### 3.2 Envelope, concurrency, and finite-state semantics

- **Envelope (both channels):** every request carries `{nonce, seq, expiry, HMAC,
  role, worldUid, capabilityVerb, requestId}`. The server additionally binds the
  **actual delivering peer**. Unknown verb, out-of-manifest verb, out-of-bounds
  argument, expired, bad HMAC, replayed sequence, or wrong role → **fail-closed
  reject receipt**.
- **One primitive in flight per process.** Each helper owns its **own main-thread
  queue / coroutine budget** with explicit **poll / cancel / deadline**; a second
  concurrent request returns `BUSY`; an over-deadline request returns `TIMEOUT` and
  frees the slot; `cancel` returns `CANCELLED`. **No loops, no sleeps, no monolithic
  commands.** This is what structurally avoids the ValBridge/ScriptTools deadlock
  class (§5.2).
- **Idempotency:** `requestId` + `seq` dedup on replay — a repeat returns the
  cached receipt, never a re-execution.

### 4. Fixture policy (the firewall)

Fixtures may synthesize **ordinary allowlisted vanilla prerequisites only** — a
workbench, a forge, wood/leather/iron, a placed vanilla piece, a position — via the
**same server-authoritative spawn seams the game uses** (additive per ADR-0006;
read vanilla prefabs as blueprints, never clone-and-strip), **bounded by exact
IDs/counts/radius and recorded in an owned-resource ledger.**

Fixtures **MUST NOT** grant or fabricate any **product** state. The helper cannot
mint, sign, or grant: **product identity, entitlement, AP/BP, relationship,
ownership, signatures, snapshots, journals, caches, or verdicts.** To get a
Masterwork stamp on an item, the harness must **craft it at a real station on the
authoritative server through the product's own issuance path** — if the product
code can't deliver that to a joined client, the harness surfaces that truthfully
(it does not paper over the gap).

**Tamper** is strictly bounded: it may **replace or remove an existing allowlisted
field only on an exact tracked throwaway item**, to prove degrade. It may **never
add or copy a valid signature** onto any item. The allowlist of spawnable vanilla
items/prefabs and tamperable fields is an **explicit static list** in the helper,
reviewed like any code.

### 5. Security / fail-closed gates

#### 5.1 Arming gate (AND-composed, fail-closed)

The helper registers **no mutating command surface and no state-mutating Harmony
hooks** until **every** condition holds:

- **Default disabled.** Absent an explicit arm signal + valid nonce, nothing arms.
- **Exact world UID *and* exact world name.** World **name alone is insufficient**;
  the helper requires both the exact disposable-world UID and its exact name to
  match the run manifest.
- **Hard production deny list.** Known production worlds/servers (Niflheim `2456`,
  Heistan `2466`) are **rejected even if the allowlist is misconfigured** —
  production rejection is a hard gate, not a warning.
- **Explicit process role / actor alias.** Role and actor come from the runner's
  explicit signal, never inference.
- **Immutable hash manifest.** The run pins an immutable manifest of
  **product / helper / game / BepInEx / Harmony / scenario** hashes; drift on any
  of them refuses to arm, so a stale helper can't silently drive a moved seam.
- **Run nonce + expiry + capability manifest.** Per-run nonce, a hard expiry, and a
  manifest enumerating exactly which verbs are permitted this run; everything else
  fail-closes.
- **HMAC + sequence/idempotency.** Every request is HMAC-signed and
  sequence/idempotency-checked.
- **Delivering-peer binding + connection generation + admin recheck at execution.**
  Server fixture verbs bind the **actual delivering peer** (resolved from the transport,
  never a claimed identity) and re-check admin/owner authority at the **moment of
  execution**, not just at arm. Every request carries a required, strictly-positive
  **`connectionGeneration`** in its authenticated envelope (part of the HMAC input); the
  server bumps the generation on every peer (re)bind and **rejects any request whose
  claimed generation is not the current bound one (StaleGeneration)** — so a pre-reconnect
  envelope is refused before any fixture/action mutation even if its HMAC verifies. The
  claimed generation is **decoded from the signed envelope, never injected by the transport
  bridge**, so the stale-generation defense is reachable on the real RPC path. The server's
  current generation is echoed on every receipt (`connectionGeneration`) so the authorized
  runner can form its next request; a reconnect advances it.

#### 5.2 Control-channel non-reentry (the deadlock proof obligation)

The cancelled run wedged because probes rode the in-game console and shared the
`ScriptTools` / `Terminal` main-thread lock. This design **removes that surface
entirely**:

- Client control is a **dedicated loopback TCP/JSON channel** with the helper's
  **own single-slot, timeout-bounded main-thread dispatcher** — it **never shares
  the ValBridge / Terminal / ScriptTools lock**.
- Server control is **per-peer ZRpc**, not a console verb and not a loopback socket.
- There is **no console relay** on either side.

An implementation AT (`AT-QA-NO-SCRIPTTOOLS-LOCK`, §10) must **prove non-reentry**:
the dispatcher acquires no lock that the game console/`ScriptTools`/ValBridge path
also acquires. Absent that proof, the channel does not ship.

#### 5.3 Owner-local / admin boundary

The loopback channel binds **`127.0.0.1` only** and requires the runner's
per-session operator token; a request claiming a different account/peer is checked
against the local session identity and rejected on mismatch. No remote client can
drive fixtures; server fixtures additionally require the admin/owner control plane
and the execution-time admin recheck (§5.1).

#### 5.4 Cleanup / no leakage

`Arm` carries a TTL; on TTL, `Disarm`, or process teardown the helper runs
`Cleanup` (removing spawned fixtures tracked by the run-scoped **owned-resource
ledger**) and disarms. **No persistence leakage:** the helper writes nothing to the
world save that survives disarm, and never to a product durable path (store,
journal, cache, adminlist). A crash finalizer flushes the receipt and runs
`cleanup.reset`.

### 6. Evidence protocol

- The **helper emits primitive facts only**: `{requestId, verb, role, worldUid,
  nonce, seq, ts, outcome, observed:{...}}`. Receipts are **descriptive**
  ("tooltip text = …", "item field present = true", "quality = 3") — they **never
  contain a PASS/FAIL for a product AT** and never a product verdict.
- The **runner** collects receipts from server + both clients, runs the scenario
  state machine (`qa/scenarios/t022.json`), and **emits the single final evidence
  JSON** mapping observed primitives → AT verdicts. **Only the runner declares
  PASS/FAIL, and it cannot PASS without all four named T022 ATs asserted and
  cleanup confirmed.**
- Final evidence lands as a normal QA evidence doc (e.g. under `docs/v3/evidence/…`)
  authored by the human/architect from the runner output — consistent with the
  existing evidence-doc convention. **Receipt hash chains and connection-generation
  hardening are deferred to a later milestone (§10) but are required before M6.**

### 7. Packaging / CI (production inclusion structurally impossible)

- **Never in the product modpack.** `scripts/pack-modpack.sh` overlays an explicit
  allowlist; `qa/**` is excluded by default. We **harden this into an assertion**:
  after staging, the pack script greps the staged tree and **fails if any
  `SBPR.QaHarness*` assembly is present**.
- **Case/rename/path-traversal-resistant CI negative tests.** A CI negative test
  builds the modpack artifact and asserts no QA assembly is present, and does so
  **resistant to case-folding, renaming, and path-traversal evasion** (normalized
  path + content signature, not a single literal filename match).
- **No product dependency in either direction.** A CI guard asserts the helper
  `.csproj` has **zero** `ProjectReference`/`Reference` to `src/SBPR.*`, **and** that
  no product project references `qa/**`.
- **Separate deterministic QA overlay.** `qa/` is its own deterministic bundle
  (helper DLL + Python runner + a disposable-world BepInEx profile) with an
  **immutable manifest + sha256 pin**, shipped alongside testing — never referenced
  by the installer or the product release. The helper gets its **own CI job**
  (build the plugin against the SDK; run the Python runner's unit/contract tests)
  so a harness change is gated independently and never sits on the product build's
  critical path.

### 8. Compatibility / versioning / drift

The immutable hash manifest (§5.1) pins product/helper/game/BepInEx/Harmony/scenario
byte state; on arm the helper verifies loaded versions and refuses on mismatch
(drift rejection). Deployed DLL sha256 is reviewed + pinned; the launch controller
fail-closes on a mismatched deploy (already-proven mechanism).

### 9. Migration / retirement of ephemera

- `QaT022Driver.cs` and ad hoc `run_script` probes are **retired** once the harness
  lands the equivalent primitives. Their observations are historical/unverifiable
  (no committed blob hash); the discriminator mismatch (`0/1` vs product `1/2`) is a
  **false-sent risk** that argues for retirement, not generalization. The T022
  scenario is re-expressed as `qa/scenarios/t022.json` driven by the runner. The
  untracked driver is deleted (never committed); the retirement is noted in the T022
  evidence doc so the trail is explicit.

### 10. Phased milestones & named acceptance tests

**P0/P1 are kept minimal (card requirement):** path/dependency/production-exclusion
gates + exact-world fail-closed arming come first; the safe loopback + peer-RPC
channel comes second. **Receipt hash chains and connection-generation hardening are
deferred to M4 but are required before M6.**

- **M0 — isolation + gate (P0).** Path isolation; no-product-reference guard;
  production-exclusion guard; exact-world fail-closed arming.
  - `AT-QA-NO-PRODUCT-REF` — build guard: helper links no `src/SBPR.*`; no product
    project references `qa/**`.
  - `AT-QA-MODPACK-EXCLUDES-HARNESS` — CI negative test (case/rename/path-traversal
    resistant): staged modpack + zip contain no QA assembly.
  - `AT-QA-DISABLED-BY-DEFAULT` — unarmed helper exposes no mutating verb.
  - `AT-QA-PRODWORLD-REJECT` — arm refused on any non-allowlisted world and hard-
    refused on production worlds/servers (`2456`/`2466`) even with a misconfigured
    allowlist.
  - `AT-QA-EXACT-WORLD-UID` — arm refused when world name matches but UID does not.
- **M1 — safe channels (P1).** Loopback TCP/JSON client channel + authenticated
  per-peer ZRpc server channel; `Arm`/`Disarm`/`Ping`; single-slot dispatcher.
  - `AT-QA-LOOPBACK-ONLY` — client channel binds `127.0.0.1`, rejects non-loopback
    and bad token.
  - `AT-QA-NO-SCRIPTTOOLS-LOCK` — proof of non-reentry: dispatcher shares no lock
    with console/`ScriptTools`/ValBridge.
  - `AT-QA-SERVER-NO-LISTENER` — server exposes no host listener; fixture verbs only
    via authenticated per-peer ZRpc, delivering-peer bound.
  - `AT-QA-BUSY-TIMEOUT-CANCEL` — one primitive in flight; `BUSY`/`TIMEOUT`/
    `CANCELLED` semantics; channel stays responsive.
- **M2R — real fail-closed runtime control plane (corrective implementation).** A fresh
  audit found merged M1/M2/M3 were engine-free CORES with `Plugin.cs` still inert; this
  slice wires the *actual* net48 runtime that ADR §2/§3.2/§5.1–§5.3 promised, on current
  main after merged M3. Delivered:
  - `Plugin.cs` stays default-disabled: it arms ONLY when the runner places an explicit
    local bootstrap doc (path via `SBPR_QA_T022_BOOTSTRAP` env — never inferred) AND the
    world has loaded AND the assembly/MVID drift guard (PR #408 §1 pins) passes AND the
    exact M1 arming AND-gate accepts. Otherwise it performs zero I/O and zero mutation.
  - Client role: a REAL `TcpListener` bound to `IPAddress.Loopback` only, owner-local
    operator token, bounded 4-byte-prefixed framing, one connection/request slot, read
    deadlines; parsed requests flow through M1 `RequestAdmission` and the M2
    `ControlDispatcher` before any receipt.
  - The helper's OWN `MonoBehaviour.Update` pump drains the loopback queue on the main
    thread; the socket accept loop only enqueues. It shares no Terminal/ScriptTools/
    ValBridge lock.
  - Server role: NO host listener. A Harmony postfix on the pinned private
    `ZNet.OnNewConnection(ZNetPeer)` registers exactly one fixed, namespaced helper ZRpc
    (`SBPRQA.Control`) on that peer's `m_rpc`; the handler binds the ACTUAL delivering
    peer via `ZNet.GetPeer(rpc)`, validates connection generation + execution-time admin
    recheck, and supports status/ping/reject-only in M2R (no fixtures/actions).
  - Conspicuous ARMED/DISARMED audit banners; safe unbind on disable/destroy; no secret
    logging; no product references.
  - Engine-free brain (`MiniJson`/`EnvelopeCodec`/`ControlPlaneRuntime`/
    `LoopbackControlServer`/`ServerRpcResponder`/`AssemblyDriftGuard`/
    `ArmBootstrapParser`) is link-compiled into the headless xUnit suite and proven by
    real-loopback component tests (remote bind refused, malformed/partial/oversized
    frames, wrong token), peer-substitution / stale-generation / admin-recheck rejects,
    server-no-listener assertion, drift fail-closed, and full-gate arm-only. M2R executes
    no fixtures/actions and mutates no game state.
- **M2 — fixtures + cleanup.** Server-role `SpawnStation`/`GrantVanillaMaterials`/
  `PlaceVanillaPiece` + owned-resource ledger + `Cleanup`.
  - `AT-QA-FIXTURE-VANILLA-ONLY` — attempt to grant any product-authored state is
    rejected.
  - `AT-QA-CLEANUP-NO-LEAK` — post-disarm world save carries no harness spawn; no
    product durable write.
- **M3 — actions + observation + transfer + tamper + full T022 cycle.**
  `Craft`/`UpgradeItem`/`Drop`/`PickUp`/`Read*`; two-client transfer; `TamperField`.
  - `AT-QA-CRAFT-THROUGH-PRODUCT-SEAM` — a Masterwork stamp appears **only** because
    product issuance ran server-side (not fabricated by the harness).
  - `AT-QA-TOOLTIP-OBSERVE` — `ReadTooltip` surfaces the in-world Workmanship text.
  - `AT-QA-TRANSFER-PRESERVES` — receiving client observes the preserved stamp.
  - `AT-QA-TAMPER-DEGRADES` — tamper replaces/removes an allowlisted field on a
    throwaway item only; product renders no line; **no signature added/copied**.
  - `AT-QA-T022-COLD-30MIN` — one **cold** end-to-end T022 cycle
    (issue→upgrade→transfer→tamper) completes in **≤30 minutes**, no manual hunting.

  > **Implementation note (2026-07-22, card t_4db82cc0 + t_1572d041).** M3 landed in two
  > slices. First (t_4db82cc0) shipped the ENGINE-FREE fixture core: the owned-resource
  > ledger + deterministic plan validation + `VanillaFixtureManifest` (real vanilla
  > allowlist + bounds + product-id guard) + the `SeamFixtureWorld` bridge to the additive
  > `IVanillaFixtureSeam` + the execution-time `FixtureAuthority` recheck — but the seam was
  > FAKE-ONLY (`FakeVanillaFixtureSeam`); nothing touched the game and the ledger had no
  > durable persistence. The corrective slice **M3R (t_1572d041)** wires the REAL net48
  > server adapter behind the M1/M2R gates and closes the fake-only gap:
  >   • `ZNetVanillaFixtureSeam` (Runtime, engine-bound) — the additive vanilla implementation
  >     of `IVanillaFixtureSeam`: materials via `ItemDrop.DropItem` of the item's own drop
  >     prefab (the game's own additive grant seam); stations/anchors via TRUE ADDITIVE
  >     CONSTRUCTION per ADR-0006 — an INACTIVE `new GameObject` with only the INTENDED
  >     components AddComponent'd (`ZNetView` with m_persistent/m_type/m_distant set ourselves,
  >     a root `BoxCollider`, and, for a station request, a required `CraftingStation` whose `m_name` and
  >     `m_useDistance` are value-copied off the vanilla prefab read as a read-only blueprint via
  >     `ZNetScene.GetPrefab` — no mesh/renderer or other component is read or attached). A station
  >     blueprint missing `CraftingStation`, a non-empty `m_name`, or a finite positive `m_useDistance`
  >     is treated as drift and creation fails closed rather than degrading to a bare anchor. The shell is
  >     registered in `ZNetScene` by name and then instantiated — there is NO `Instantiate` of a
  >     vanilla ZNetView-bearing prefab and no clone-and-strip anywhere (mirrors the product's
  >     `Assets.TryConstructPieceShell`). Cleanup via the network-aware `ZNetView.Destroy` /
  >     `ZNetScene.Destroy` / `ZDOMan.DestroyZDO` path so no ZDO survives (AT-QA-CLEANUP-NO-LEAK).
  >     The handle the ledger stores is the object's full stable `ZDOID` ("UserID:ID"), so only
  >     the EXACT owned instance is ever despawned.
  >   • **Durable exact ownership markers (crash-safe ownership, ADR-0009 §5.4).** Every spawned
  >     fixture object is stamped, as PART of creation, with a QA ownership marker on its ZDO under
  >     the single namespaced key `SBPRQA_FixtureOwner`, encoding (world uid, run nonce, fixture id,
  >     owned-resource id). Because the marker lives on the game-persisted ZDO, a crash at ANY point
  >     after spawn — including BEFORE the snapshot is written — leaves a self-describing survivor.
  >     On the next run, `OwnedResourceLedger.RecoverFromMarkers` scopes discovery to EXACTLY (this
  >     world uid, this run nonce, this fixture id) and adopts each matching survivor into the Created
  >     state; it FAILS CLOSED (no adoption, no world side effect) on any malformed / duplicate /
  >     foreign-world / foreign-run / unexpected-resource marker. If the marker cannot be durably
  >     written+read-back at create time, the half-built object is destroyed and the create is
  >     reported as a failure — never a silently untracked leak. Unmarked/unrelated world objects are
  >     never discovered, so they are structurally un-adoptable and un-deletable (preserved).
  >   • `ZNetServerAuthoritySource` (Runtime, engine-bound) — the real `IServerAuthoritySource`:
  >     live `ZNet.IsServer()` / world-load / admin re-read (same admin surface as the M2R
  >     control-plane recheck), fail-closed on any drift.
  >   • `LedgerSnapshotStore` (engine-free) — CRASH-SAFE durable FAST-PATH cache: atomic
  >     temp+fsync+`File.Replace` write; fail-closed read (missing = Absent, unreadable = IoError,
  >     undecodable = Corrupt — NEVER treated as an empty ledger; a Corrupt/IoError/Absent load
  >     falls back to the durable ON-OBJECT markers as the authority on what survived). `Delete`
  >     returns an observable success/failure so a snapshot that cannot be removed after full cleanup
  >     is surfaced/retryable, not swallowed over a durable leak.
  >   • `FixtureRequestMapper` + `ServerFixtureExecutor` + `FixtureVerbExecutorBridge`
  >     (engine-free) — map an admitted fixture verb+args to a bounded vanilla-only plan (product
  >     ids refused pre-allowlist), then run durable-marker recovery (fail-closed) + snapshot load +
  >     reconcile → execution-time authority gate → ensure/cleanup → atomic snapshot, adopting/
  >     reconciling ONLY the exact owned ids across a restart. The `ServerRpcResponder` runs this
  >     ONLY after a fixture verb has passed delivering-peer + generation binding, execution-time
  >     admin recheck, and the shared M1 admission + single-slot dispatch — so a fixture never
  >     mutates the world without every prior gate.
  > M3R adds NO product state, NO craft/upgrade/transfer/tamper, and NO verdict — those remain
  > later work. Proven headless in `tests-core` (crash-safe store round-trip/corrupt/atomic;
  > durable-marker crash recovery: crash-before-snapshot adoption, corrupt-snapshot-never-empty,
  > duplicate/foreign-world/foreign-run/unexpected/malformed marker fail-closed refusal, unmarked
  > same-prefab preservation, marker-write-failure as a create failure, observable snapshot-delete
  > failure; request→plan mapping incl. exact allowlist/bounds/radius + product-id refusal; gated
  > executor with peer-substitution / stale-generation / non-admin / non-server / world-not-loaded
  > rejects all zero-side-effect; partial spawn; restart reconcile; owned-only cleanup; unrelated
  > preservation) + the net48 helper Release build (0w/0e) compiling against the exact pinned
  > vanilla members. **No live game launch / deploy / in-game qualification is part of this card**
  > — in-game execution is NOT verified here (that is M6, a separate operator authorization).
  >
  > **Repair note (2026-07-22, card t_0e3a88bd on PR #414).** Owner review of the first M3R push
  > found two load-bearing defects that this repair corrects: (1) the seam directly `Instantiate`d
  > a vanilla ZNetView-bearing station prefab while calling it additive — an ADR-0006 violation,
  > now replaced with the true `new GameObject` + intended-`AddComponent` shell above; and (2)
  > "crash-safe ownership" was false because world creation preceded the durable snapshot and a
  > corrupt/absent snapshot was treated as empty — now closed by the durable ON-OBJECT ownership
  > markers + bounded fail-closed recovery above, with observable snapshot-delete failure.
  >
  > **Second-review repair (2026-07-22, same card).** A follow-up owner review found the durable-
  > marker recovery's world scan was still a WHOLE-WORLD walk of `ZDOMan.m_objectsByID` mislabelled
  > "bounded", and that on a read/enumeration fault it returned the partial list found so far, which
  > the ledger treated as complete (an unenumerated survivor could be duplicated). Both are now
  > corrected: discovery is a typed **complete/refused** result over a **bounded spatial query** —
  > the engine-free recovery hands the seam a `FixtureWorldScope` (the plan's allowlisted prefabs,
  > max radius, and a hard candidate cap), and the engine-bound seam answers it with a pinned
  > `ZoneSystem.GetZone` + `ZDOMan.FindSectorObjects` sector query around the deterministic fixture
  > origin, filtered to the allowlisted prefab hashes, then to the exact maximum radius and the QA
  > marker key, with a hard sector-ring and allowlisted-candidate cap. Any binding failure,
  > enumeration exception, per-candidate position/marker/handle error,
  > or cap overflow yields a **refusal with ZERO candidates and ZERO world mutation**; the ledger
  > fails closed (`FixtureRecoveryStatus.DiscoveryRefused`) and never adopts a partial list. There is
  > no parameterless (full-world) discovery path on either the engine-free port or the seam. New
  > headless tests: discovery-fault refuses and creates nothing, cap overflow refuses, an
  > out-of-region (non-allowlisted-prefab) marked object is neither adopted nor destroyed, a valid
  > survivor still adopts exactly once, and the contract exposes only the scoped overload.
- **M4 — adversarial + evidence hardening (required before M6).**
  - `AT-QA-BAD-NONCE-REJECT`, `AT-QA-OUT-OF-MANIFEST-REJECT`,
    `AT-QA-REMOTE-FIXTURE-REJECT`, `AT-QA-OUT-OF-BOUNDS-ARG-REJECT`,
    `AT-QA-REPLAY-REJECT`, `AT-QA-PEER-SUBSTITUTION-REJECT`.
  - `AT-QA-RECEIPT-HASH-CHAIN` — deferred receipt hash chain + connection-generation
    binding land here; required before any live qualification.
  - `AT-QA-CLEANROOM` — `reviewer-cleanroom` sign-off: genuine vanilla public API +
    product public seams only; no decompiled/other-mod source.
- **M5 — packaging + drift + deploy pinning.** QA bundle manifest + sha256 pin;
  drift rejection; deployed-DLL hash review + launch-controller fail-closed.
- **M6 — live qualification (SEPARATE operator authorization, NOT this ADR).**
  On a disposable lane with two genuine licensed clients and entitlement seeded via
  the authorized admin path, the four ATs are observed in-world via helper receipts;
  adminlist byte-restored; no production touched. **Hard gate: M0–M5 all green +
  `reviewer-cleanroom` + Daniel approve before M6 is ever authorized.**

---

## Consequences

- **Easier:** reproducible fixtures + bounded actions make the T022-class
  joined-client nodes achievable in minutes instead of 180-turn manual hunts.
- **Constrained (deliberately):** the harness can *never* fabricate product state or
  self-certify a PASS — a green harness run means exactly what the product code
  makes true, preserving "logs-green≠playable" honesty.
- **Load-bearing — do not undo without a new ADR:** (a) harness stays out of
  `src/`; (b) harness never ships in the product modpack; (c) helper is fail-closed
  / disposable-world-gated on exact UID+name; (d) server has no host listener;
  (e) no command surface on the game console; (f) only the external runner emits a
  verdict.
- **Cost:** a new subsystem, a new CI job, and drift-pinning against product head.
  Accepted: the alternative is repeatedly rebuilding throwaway scenario drivers and
  risking an entitlement-forging or false-sent test shortcut.

## Alternatives considered

1. **Keep per-scenario ephemeral drivers (`QaT022Driver` pattern).** Rejected:
   exactly what burned 180 turns and wedged ValBridge; untracked, unrepeatable, no
   safety envelope, and carried a real discriminator mismatch (§Superseded inputs).
2. **Add QA hooks inside the product mod behind a debug flag.** Rejected: violates
   the trust boundary — a product-resident test seam that can synthesize
   entitlement/ownership is indistinguishable from a cheat/exploit surface and would
   ship in the DLL. Product must stay the pure system under test.
3. **In-game console verbs as the control surface (architect comment 1982).**
   Rejected: reintroduces proximity to the `ScriptTools`/`Terminal` main-thread lock
   that wedged the cancelled run. The loopback + per-peer-ZRpc surfaces remove that
   class entirely (§5.2).
4. **Client-only helper, no server component (t_18470c4d).** Rejected: server-only
   world fixtures (station placement, server-authoritative spawns) need an
   authoritative-server actor. This ADR adds the server half over authenticated
   per-peer ZRpc with **no host listener**, rather than forcing fixtures through a
   client.
5. **World-name-only allowlist (t_18470c4d).** Rejected: name is spoofable/reusable;
   the gate requires exact world **UID and name** plus a hard production deny list.
6. **ServerDevcommands / console cheats as the fixture layer.** Rejected as the
   *primitive* layer: unbounded, string-parsed, cheat-flagged, can grant
   product-forbidden state, no typed receipt or nonce gating. (May remain a *manual
   operator convenience*, not the reproducible bounded API this design requires.)

---

## Appendix A — Threat model (folded from THREAT-MODEL.md)

Scope: `SBPR.QaHarness.T022`. Enumerates what could go wrong and the design control
that prevents it.

**Assets:** A1 production servers/worlds (Niflheim `2456`, Heistan `2466`);
A2 product entitlement integrity (Masterwork purchase, Stone relationship,
Workmanship signing key); A3 verdict authenticity (no fabricated PASS); A4 product
data stores/journals/ownership/caches; A5 QA client liveness (the ValBridge deadlock
lesson); A6 release/modpack purity.

| ID | Threat | Impact | Control |
|----|--------|--------|---------|
| T1 | Helper armed on a production server/world | A1 | AND-composed gate (§5.1) with hard deny list for `niflheim`/`heistan` and `:2456`/`:2466`; exact UID+name; refuses even if allowlist misconfigured |
| T2 | Helper packaged into a product modpack/release | A6 | `qa/` path isolation; pack-modpack staged-tree assertion; case/rename/path-traversal-resistant CI negative test; no-product-dependency guard both directions (§7) |
| T3 | Helper mints entitlement / signs Workmanship to force ISSUE | A2, A3 | Authority model (§4): no key, no mint/sign/grant API; entitlement seeding is the operator's authorized admin OFFER→BUY path, out of band |
| T4 | Helper forces internal verdict state via reflection to fabricate PASS | A3 | Observation reads only product-rendered tooltip + raw field keys; no reflection into verdict caches; clean-room review (§10 M4) enforces |
| T5 | Helper writes forged stamp onto a legitimate item / store | A4 | Tamper replaces/removes an allowlisted field only on an exact tracked throwaway item; never adds/copies a signature; zero product store/journal/cache writes; item destroyed at cleanup |
| T6 | Observation call deadlocks the client (the cancelled-run failure) | A5 | Loopback channel with helper's own single-slot, timeout-bounded main-thread dispatcher; never shares ValBridge/ScriptTools lock; `AT-QA-NO-SCRIPTTOOLS-LOCK` proof |
| T7 | Open-ended evaluator/discovery loop exhausts budget or hangs | A5 | Every action is a bounded FSM with per-action deadline; one slot; `BUSY`/`TIMEOUT`/`CANCELLED`; no polling loops or sleeps |
| T8 | Replay of a captured control request re-runs a destructive action | A3, A4 | Idempotent nonce + sequence (repeat → cached receipt, no re-exec); per-session operator token; expiry; loopback-only / per-peer ZRpc bind |
| T9 | Unauthorized local process drives the control channel | A1–A4 | `127.0.0.1`-only bind + operator token; server verbs require authenticated delivering peer + admin recheck |
| T10 | Identity/peer substitution (request claims a different account/peer) | A3 | Client: claimed identity checked against local session; Server: **actual delivering peer** bound, claimed identity ignored |
| T11 | Silent no-op mistaken for a pass | A3 | Loud arm/disarm banners; every refusal emits a `DISARMED(reason)` receipt; absence of banner = not armed; runner cannot PASS without all four ATs + cleanup |
| T12 | Crash leaves world/adminlist dirty | A1, A4 | Helper writes no product durable state; finalizer flushes receipt + runs `cleanup.reset` from the owned-resource ledger; adminlist untouched (operator owns admin path with verified backup) |
| T13 | Helper references decompiled IronGate/other-mod source (clean-room breach) | legal | CLEAN side only; genuine vanilla public API + product public seams; `reviewer-cleanroom` audit gate before merge |
| T14 | Unreviewed/altered DLL deployed | A3 | Exact sha256 review + launch-controller manifest fail-closed on mismatch; immutable hash manifest at arm (§5.1, §8) |

**Residual risk:** the GUI-pixel last mile remains human-observed at M6 (a licensed
player sees the tooltip); the helper makes this deterministic and receipted, but the
final visual confirmation stays an operator smoke — consistent with the accepted
T025/T026/T030 precedent, disclosed not hidden. Entitlement seeding correctness
depends on the operator's authorized admin path; if wrong, ISSUE genuinely
fail-closes (correct), it does not falsely pass.

## Appendix B — Acceptance matrix (folded from ACCEPTANCE-MATRIX.md)

Milestone→acceptance→evidence. "Green" = automated where possible; **M6 live
qualification is a separate operator-run card**, not part of this design. AT ids
are the §10 names.

| Milestone | Acceptance (representative) | Evidence |
|---|---|---|
| M0 isolation+gate | product build unaffected by `qa/` (0w/0e); `AT-QA-NO-PRODUCT-REF`; `AT-QA-MODPACK-EXCLUDES-HARNESS` red/green; `AT-QA-DISABLED-BY-DEFAULT`; `AT-QA-PRODWORLD-REJECT`; `AT-QA-EXACT-WORLD-UID`; helper builds 0w/0e net48 Release | build logs, CI red/green, disarm receipts |
| M1 safe channels | `AT-QA-LOOPBACK-ONLY`; `AT-QA-NO-SCRIPTTOOLS-LOCK`; `AT-QA-SERVER-NO-LISTENER`; `AT-QA-BUSY-TIMEOUT-CANCEL`; schema-valid bounded receipts | contract tests, code review, schema tests |
| M2 fixtures+cleanup | `fixture.*` via genuine vanilla seams; owned-resource ledger tracks every spawn; `AT-QA-FIXTURE-VANILLA-ONLY`; `AT-QA-CLEANUP-NO-LEAK`; no product store/journal write | fake-game tests, code review |
| M3 behavior+observation | `AT-QA-CRAFT-THROUGH-PRODUCT-SEAM` (ISSUE); upgrade preserves (UPGRADE); `AT-QA-TRANSFER-PRESERVES` (TRANSFER); `AT-QA-TAMPER-DEGRADES` (TAMPER); `AT-QA-TOOLTIP-OBSERVE`; `AT-QA-T022-COLD-30MIN`; observation reads only tooltip+field keys | FSM tests, clean-room audit |
| M4 adversarial+evidence | reused nonce/forged token/replay/peer-substitution/out-of-manifest/out-of-bounds all rejected; `AT-QA-RECEIPT-HASH-CHAIN` (deferred hardening, required before M6); `AT-QA-CLEANROOM` sign-off | attack tests, reviewer-cleanroom |
| M5 packaging+drift | separate deterministic QA bundle + sha256 pin; drift rejection; deployed-DLL hash review + launch-controller fail-closed | manifest, hash review |
| M6 live qual (SEPARATE operator card) | two genuine licensed clients on disposable lane; entitlement seeded via authorized admin path; four ATs observed in-world; adminlist byte-restored; no production touched | operator receipt bundle |

**Hard merge/release gate:** no helper artifact merges or releases until M0–M5 all
green AND `reviewer-cleanroom` + Daniel approve. M6 live qualification is a distinct
operator-run card and is never auto-run.

## Appendix C — Superseded artifact records

The three `_kanban-artifacts/t022-test-helper-design/` files are reduced to
`SUPERSEDED BY ADR-0009` pointer records (this ADR path + sha256, recorded in the
completion handoff). They retain no competing architecture; all threat-model and
acceptance content is folded into Appendices A and B above.

## Implementation task decomposition (cards to cut ONLY after design review opens)

- **QA-M0** engineer: scaffold `qa/SBPR.QaHarness.T022/` (own props, no product ref)
  + `qa/contracts/` schemas + `qa/runner/sbpr-qa-t022.py` skeleton; path/dependency/
  production-exclusion guards; exact-world fail-closed arming
  (`AT-QA-NO-PRODUCT-REF`, `AT-QA-MODPACK-EXCLUDES-HARNESS`, `AT-QA-DISABLED-BY-DEFAULT`,
  `AT-QA-PRODWORLD-REJECT`, `AT-QA-EXACT-WORLD-UID`).
- **QA-M1** engineer: loopback TCP/JSON client channel + authenticated per-peer ZRpc
  server channel (no listener) + single-slot dispatcher + `Arm/Disarm/Ping`
  (`AT-QA-LOOPBACK-ONLY`, `AT-QA-NO-SCRIPTTOOLS-LOCK`, `AT-QA-SERVER-NO-LISTENER`,
  `AT-QA-BUSY-TIMEOUT-CANCEL`).

  > **Implementation note (2026-07-22, card t_1be7f7d2).** QA-M1 was split: the
  > **engine-free contract layer landed first** — the typed request/receipt/envelope
  > contracts (`qa/contracts/*.json` + `qa/SBPR.QaHarness.T022/Contracts/*.cs`), the
  > immutable **capability-manifest parser** (`CapabilityManifest`/`VerbCatalog`), and
  > the **fail-closed arming + request-admission decision** (`ArmingGate`/
  > `RequestAdmission`), proven headless by `qa/tests-core/` under the card's named ATs
  > (AT-QA-DISABLED-BY-DEFAULT, PROD-WORLD-REJECT, EXACT-WORLD-UID, BAD-NONCE-REJECT,
  > OUT-OF-MANIFEST-REJECT, OUT-OF-BOUNDS-ARG-REJECT, REPLAY-REJECT). No listener,
  > socket, RPC, Harmony, Unity/game mutation, fixture, deployment, or live runtime is
  > in that slice — the helper compiles the contracts but does not invoke them (nothing
  > can arm without a channel). The **channels + single-slot dispatcher** above
  > (`AT-QA-LOOPBACK-ONLY`, `AT-QA-NO-SCRIPTTOOLS-LOCK`, `AT-QA-SERVER-NO-LISTENER`,
  > `AT-QA-BUSY-TIMEOUT-CANCEL`) remain the next slice and drive the arming gate that
  > this card delivered.

  > **Implementation note (2026-07-22, card t_e596652b).** The channels + dispatcher
  > slice landed its **engine-free control-plane core** first (`qa/SBPR.QaHarness.T022/
  > ControlPlane/*.cs`): the owner-local loopback **frame parser + `127.0.0.1`/operator-
  > token bind policy** (`LoopbackFrameParser`), the **single-slot, deadline-bounded,
  > cancellable dispatcher** with a bounded FIFO backlog (`ControlDispatcher`), the
  > **delivering-peer / connection-generation** state model (`DeliveringPeerState`), and
  > **engine-free game-binding seam interfaces + inert fakes** (`GameBindingAdapters`,
  > written CLEAN-side from the PR #408 behavioral map, no decompiled source). All six
  > of this slice's named ATs are proven headless by `qa/tests-core/` —
  > `AT-QA-LOOPBACK-ONLY` (loopback-only + bad-token), `AT-QA-SERVER-NO-LISTENER` /
  > `AT-QA-PEER-SUBSTITUTION-REJECT` (delivering-peer bound, substitution + stale-
  > generation refused), `AT-QA-BUSY-TIMEOUT-CANCEL` (one primitive in flight;
  > BUSY/TIMEOUT/CANCELLED), `AT-QA-REMOTE-FIXTURE-REJECT` (fixture verbs are ServerRpc-
  > only and refuse a remote/unbound delivering peer), and `AT-QA-NO-SCRIPTTOOLS-LOCK`
  > (the core owns no synchronization primitive and re-enters from its own scheduler
  > continuation without deadlock — the structural non-reentry anchor). **Still no
  > listener, socket, live ZRpc, Harmony hook, Unity/game mutation, fixture execution,
  > deployment, or runtime** — the live TCP/ZRpc pump binding these seams to the game is
  > the next slice; the Plugin remains inert/disarmed. No fixture/action mutations yet
  > (§10 QA-M2 fixtures follow).
- **QA-M2** engineer: fixture verbs + owned-resource ledger + `Cleanup`
  (`AT-QA-FIXTURE-VANILLA-ONLY`, `AT-QA-CLEANUP-NO-LEAK`).
- **QA-M3** engineer + qa: action + observation verbs + transfer + tamper + T022
  runner state machine (`qa/scenarios/t022.json`); retire `QaT022Driver` + ad hoc
  probes (§9) (`AT-QA-CRAFT-THROUGH-PRODUCT-SEAM`, `AT-QA-TOOLTIP-OBSERVE`,
  `AT-QA-TRANSFER-PRESERVES`, `AT-QA-TAMPER-DEGRADES`, `AT-QA-T022-COLD-30MIN`).
- **QA-M4** engineer + reviewer: adversarial suite + deferred receipt hash chain /
  connection-generation hardening + clean-room sign-off.
- **QA-M5** engineer: QA bundle manifest + sha256 pin + drift rejection + deploy
  hash pinning.

Each impl card ships spec+code together (CONTRIBUTING triangle); this ADR is the
spec anchor those cards reference. **No implementation, and no M6 live
qualification, is authorized by this ADR.**
