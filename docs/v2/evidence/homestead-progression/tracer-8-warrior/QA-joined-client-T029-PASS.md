---
status: current
---

# T029 T.W.I.G. Training — joined-client QA verdict: **PASS** (runtime seam wired + verified)

QA author: `qa-playtest` (independent, non-author). Task `t_a811a842`
(continuation of the FAIL card `t_92e47866`). PR #366.

- Branch: `feat/hs-t029-twig-training`
- Reconciled head (this run): `84c51ad` — the pinned implementation head
  `78bb66e` (PR #366) MECHANICALLY merged twice with fresh `origin/main`
  (`d5a6792`) during the run; both merges were additive `<Compile>` / constructor
  seam unions (T016 Savor + T021/T025/crafting includes vs the T029 include; the
  T029 `warriorTwigPendingDeadline` constructor arg preserved). No accepted
  gameplay redesigned.
- Isolated QA box: throwaway server `homestead-t009l-server` (disposable world
  `homesteadt009l`, non-public). Production `niflheim-server` / `heistan-server`
  UNTOUCHED. No user desktop Steam client (`valheim.x86_64`, AppId 892970) was
  present at any point — verified before and after the boot.
- Deployed net48 DLL md5 at boot: `edaeed6747…` (byte-identical T029 content to
  the final reconciled head; the post-T016-reconcile rebuild md5 `5680999549…`
  differs only by unrelated Savor merge code, not the Warrior seam).

## Verdict: PASS

The blocking FAIL (`QA-joined-client-T029.md`) was: **the pure
`LocalPlacementProvider` had ZERO runtime callers**, so a joined client's T.W.I.G.
(`TrainingDummy`) placement ran through vanilla `Player.PlacePiece` with no SBPR
gating and the FR-016 effect-active / Settlement-policy / build-Permission AND
never fired in-world. **That seam is now wired and verified live.** The prefab
identity (`TrainingDummy`) was already CONFIRMED correct in the FAIL doc and is
unchanged.

## What is now VERIFIED

### 1. The net48 runtime seam exists and binds to real vanilla (closes the FAIL)
- `Plugin.cs` now `PatchAll`s both Warrior observers:
  `WarriorTwigPlacementObserver` (listen-host, `Player.PlacePiece` **postfix** →
  gate → **undo on refusal** via `ZNetView.Destroy`) and
  `WarriorTwigDedicatedIngressObserver` (dedicated: `ZNet.OnNewConnection` notice
  + `ZDOMan.Update`-cadence server-side ZDO revalidation + undo).
- `FoundationalRuntimeBootstrap` calls `server.ArmWarriorTwig(stoneAggregates,
  ownerPresence)` **immediately after** composing the shared Local Effect runtime
  (`LocalProgressionServer`, PR #368) — so the gate reads the SAME authoritative
  Stone aggregate + governance projection a Local Effect snapshot reads. There is
  **no provisional/parallel second progression truth or activation ledger**.
- **LIVE BOOT PROOF** on the isolated server-authoritative headless server:
  ```
  [Niflheim.HomesteadStones] Harmony patches installed.
  [Niflheim/HomesteadStones] Runtime drift check: all required targets/callsites present.
  [Niflheim/HomesteadStones] Local progression runtime composed (server-authoritative).
      … warriorTwigArmed=True.
  ```
  `warriorTwigArmed=True` is the decisive line: the gate + pending queue composed
  against the authoritative runtime at boot. Zero `Failed to patch`, zero SBPR/
  Warrior exceptions **from the live-boot line onward** (the pre-boot
  `CultureNotFoundException: β` / `BadImageFormatException: zero rva` /
  `ZNetScene::Awake Failed to patch` entries are the PREVIOUS process's BepInEx
  `UnityPatches` shutdown-teardown noise, timestamped 23:59:16, strictly BEFORE
  the live SBPR `Awake` at 23:59:21 — the documented headless artifact, not this
  DLL). SpecCheck green (31 recipes).

### 2. The authoritative gate decision matrix (the accepted joined-client proof)
`NiflheimWarriorTwigRuntimeGateTests` (net8 link-compile = REAL execution of the
engine-free projection the net48 observers drive) composes the actual
`FoundationalProgressionServer.Create` runtime, the accepted relationship/policy
handlers, and the armed gate, and proves every named branch:

- **AT-TWIG-LOCAL (admit)** — provisioned owner, inside Stone Area, effect active
  AND ordinary build Permission → **Admitted** (`WarriorPlacementGateDisposition.Admitted`).
- **Refusal — no Permission** — effect active but `hasOrdinaryBuildPermission:false`
  → **Denied / MissingBuildPermission / RequiresUndo** (the load-bearing AND).
- **Refusal — outside policy** — owner sets Private policy, empty allowlist; a
  bound guest outside the policy → **Denied / EffectNotActive / RequiresUndo**.
- **Refusal — governance dormancy** — Governor Bond released via the accepted
  handler → every Local Effect dormant (US5 sc2) → even the former owner is
  **Denied** (proves NO relationship-only provisional activation).
- **Refusal — undeveloped node**, **unbound peer (fail closed)**, **placement
  outside every Stone Area** → all **Denied / RequiresUndo**.
- **Non-T.W.I.G. prefab** → **NotTwig / declined, NOT gated, no undo** (the node
  never widens into a general build gate).
- **Dedicated ingress** — admit a provisioned creator-bound T.W.I.G.; refuse+undo
  without Permission / outside policy; decline a non-T.W.I.G. without undo; reject
  a creator mismatch without touching the piece; await ZDO replication for an
  unresolved ZDO; pending queue converges duplicates and drops entries whose ZDO
  never replicates by deadline (the replication-race path, in-memory, restart-safe).

30/30 Warrior tests pass; full suite 1328/1328.

### 3. Build + gate hygiene (this run, at reconciled head `84c51ad`)
- net48 `SBPR.Trailborne` Release: **0 warnings / 0 errors**.
- net48 `SBPR.Niflheim.HomesteadStones` Release: **0 warnings / 0 errors**.
- Full suite: **1328 / 1328**. T029 subset (`~WarriorTwig`): **30 / 30**.
- Workbench tests: **59 / 59**. CLI `validate`: no diagnostics. Deterministic
  double-generation: byte-identical. Drift `check`: clean (asset matches
  generated). `docs-lint`: OK (194 docs). `docs-freshness`: advisory pass
  (pre-existing sunstone `last_reviewed` gaps, not T029).

## Verified (data/runtime layer) vs reasoned (client last mile)

- **VERIFIED**: the seam is wired to real vanilla methods (compiles against
  `valheim-managed`; patches install; `warriorTwigArmed=True` on a live
  server-authoritative boot; drift watchdog green). The full admit/refuse/undo
  decision matrix executes against the real authoritative runtime in tests.
- **REASONED (not observed)**: an actual human client placing the exact T.W.I.G.
  in-world and watching the piece stand (admit) or be destroyed (refuse). The
  headless `-nographics` server has NO `Player`, so it cannot itself run a
  `PlacePiece` — this is the SAME structural last-mile limit accepted for every
  prior homestead tracer (T009L, T025R). The net48 observer that performs the
  undo is thin, vanilla-typed, and mirrors the already-merged, joined-client-
  proven `FoundationalPlacementObserver` pattern one-for-one.

## Disposition
PASS for the T029 definition-of-done: the previously-missing runtime seam is
present, binds live, and enforces the FR-016 AND with the full refusal matrix
against one authoritative progression truth. Merge remains gated on independent
adversarial review (fresh reviewer card, no parent) per protocol. QA altered no
production server and no client binary; the isolated box carries the fresh T029
DLL (prior DLL backed up `*.bak-pre-qa-t029-*`).
