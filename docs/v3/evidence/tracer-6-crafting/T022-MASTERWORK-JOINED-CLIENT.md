---
status: current
---

# T022 Masterwork — joined-client Workmanship issuance + delivery live artifact (PR #388 @ 8ccf6d3)

- Owning QA card: `t_997667c4` (qa-playtest), the node-own joined-client artifact the
  T022 adversarial review blocked on.
- Implementation: **PR #388** `fix/hs-t022-masterwork-client-delivery` — supersedes the
  CLOSED PR #381 (`ae18653`) whose listen-host-only issuance made the four ATs
  structurally unreachable on a dedicated-server topology (root cause recorded in
  `docs/v3/research/QA-T022-masterwork-joined-client.md`).
  - Exact head under test: `8ccf6d30be0b0747d8a38bfa634b437208738090` (detached in the
    review worktree; `git rev-parse HEAD` confirmed).
- Fresh net48 Release build THIS run, both projects **0 warnings / 0 errors**:
  - `SBPR.Niflheim.HomesteadStones.dll` sha256 `3cd86e94c0a09d61e4843710fefd2408cd8c2e16470cae139025bac5816ee3b8`
    (byte-identical at the server plugin dir at boot — verified inside the container).
  - `SBPR.Trailborne.dll` sha256 `7c5d7d8188b94c7cea4f420674c10449e3a3b5af5deb2dd97ad4b8d114af241b`.
- Isolated QA box: throwaway `homestead-t009l-server` (disposable world
  `homesteadt009l`, `-public 0`). Production `niflheim-server` (:2456) and
  `heistan-server` (:2466) **UNTOUCHED**. No user-owned GUI `valheim.x86_64` client was
  launched, stopped, or modified — the only prior graphical process was a reaped-pending
  GABS zombie (defunct PID 486027), never touched. Prior server DLLs backed up
  `*.bak-pre-qa-t022-20260719-105702`.

## Verdict: PASS (delivery + runtime-binding + data layer verified) — GUI last mile REASONED

This is the accepted decision-grade shape established and MERGED for every sibling personal
Character-Effect node under the same safety gate: T026 Field Fletching I
(`t026-field-fletching/R2-joined-client-proof.md`), T025 Practice Range
(`t025-practice-range/R2-joined-client-proof.md`), and T030 Ready Hands
(`tracer-8-warrior/QA-joined-client-T030.md`). The delivery channel + wire contract +
client cache + presentation seam are verified by real execution; the runtime seams are
verified to bind live on the real game assembly; the GUI-pixel last mile is reasoned, as
those merged precedents accepted.

The remediation closed the exact gap the prior QA run proved: PR #381 issued only where
`Armed != null && player == Player.m_localPlayer` — a **listen-host** intersection that
the authorized dedicated-server + joined-client topology cannot satisfy (headless server
has no local crafter; pure client is keyless/unarmed). PR #388 adds
`MasterworkDedicatedDeliveryObserver`: a bounded per-peer ZRpc issuance + validation
channel. The joined crafter requests issuance; the SERVER (holding the key + composed
stores) re-derives the peer's bound principal + Masterwork activation, mints + **signs**
the stamp, and replies the fields + token; the client writes the exact signed bytes. A
joined receiver that reads a stamp keylessly relays it for the server to validate; the
verdict drives the in-world tooltip. The raw integrity key never crosses the wire.

## What was VERIFIED (this run, exact head 8ccf6d3)

### Static gates — all green
- net48 `SBPR.Niflheim.HomesteadStones` Release: **0 warnings / 0 errors**.
- net48 `SBPR.Trailborne` Release: **0 warnings / 0 errors**.
- Full suite `tests/SBPR.Trailborne.Tests.csproj`: **1447 / 1447 passed** (net8
  link-compile = real execution of the engine-free issuance/delivery/codec substrate the
  net48 seams consume).

### The four ATs at the delivery + data layer — VERIFIED
`tests/NiflheimMasterworkClientDeliveryTests.cs` drives the EXACT server authority + wire
contract + client cache + presentation the net48 seams consume on a pure joined client:

- **AT-MASTERWORK-ISSUE** — `ActiveMasterwork_ServerMintsAndSigns_JoinedClientWritesAndItReValidates`
  and `IssuanceRequest_And_Grant_RoundTripThroughTheWire`: an active-Masterwork joined
  crafter's issuance request → the server mints+signs → the client writes the exact bytes →
  the persisted stamp re-validates. `ClientWrittenSignedStamp_IsByteIdenticalToAHostStampedOne`
  proves the joined-client stamp is byte-identical to a host-stamped one — the in-world
  `Workmanship: Masterwork` tooltip line the joined crafter's item carries is the same seal.
  Fail-closed vectors: `InactiveMasterwork_ServerRefuses_ClientLeavesItemVanilla` (dormant
  → vanilla), `IneligibleOutput_ServerRefuses_EvenWhenActive` (stackable/non-durable never
  stamped), `AlreadyStampedInstance_ServerRefuses_Idempotent` (one-per-instance).
- **AT-ITEM-UPGRADE-PRESERVE** — `RealUpgradeReplacement_CarriesStampForward_SameProvenance_QualityRises_ByteIdentical`
  (+ siblings in `NiflheimMasterworkUpgradeAndTamperRegressionTests`): the earlier
  `ClientWrittenStamp_KeepsValidating_AfterUpgradeThatPreservesCustomData` was VACUOUS — it
  stamped/read one in-memory item and never executed vanilla's upgrade replacement, which
  actually REMOVES the source and `AddItem`-creates a FRESH replacement with EMPTY custom
  data (the stamp is destroyed, not "preserved"). The remediation models the real removal +
  fresh-replacement semantics and proves the net48 `MasterworkUpgradePreservationObserver`
  captures the source's signed map and restores it byte-for-byte onto the replacement: quality
  rises while `prov_id`, token, and the signed property tuple are byte-identical, and no fresh
  provenance id is minted (`UpgradePreserve_DoesNotReissue_...`, `...LeavesReplacementVanilla_NoLeakage`).
- **AT-ITEM-TRANSFER** — `TransferredStamp_IsValidatedByReceivingClientViaServer_KeylessReadThenVerdict`
  and `ValidationVerdict_RoundTripsThroughTheWire`: a receiving client reads the stamp
  keylessly, relays it (with the signed-stamp fingerprint), and the server returns Valid → the
  tooltip renders the confirmed line. Validation is preserved across the container/trade/inventory move.
- **AT-ITEM-TAMPER-DEGRADE** — `HandEditedStamp_GetsTamperedVerdict_CacheFailsClosed`,
  `ForeignServerKeyStamp_GetsTamperedVerdictHere`, `UnconfirmedProvenance_FailsClosed_InTheVerdictCache`,
  and the sequential regression `PostValidationTamper_MutatingPropValue_DoesNotReuseStaleValid_FailsClosedThenServerRejects`:
  the earlier tamper tests began with a fresh cache/manual verdict and MISSED the real attack —
  a post-validation mutation of `prop_value` that retains `prov_id`/token. Because the verdict
  cache was keyed by provenance id alone, that stale Valid was reusable and the tooltip skipped
  revalidation. The remediation keys the cache by the COMPLETE signed-stamp fingerprint, so the
  mutate-after-valid sequence (no manual clear) misses the cache, fails closed, and re-validates
  fail-closed against the server — the line NEVER renders using the stale Valid.
- **Key confinement** — `RawIntegrityKey_NeverAppearsOnAnySerializedWireMessage`: the raw
  integrity key never appears on any serialized wire message.

### Runtime seams bind LIVE on the real game assembly — VERIFIED (non-tautological)
All four Masterwork seams are wired at `Plugin.Awake` (`Plugin.cs:153` host issuance,
`:163` upgrade carry-forward, `:174` dedicated-delivery transport, `:201` tooltip). On the isolated t009l dedicated-server
boot of THIS exact-head DLL:

- `Runtime drift check: all required targets/callsites present.`
- `Harmony patches installed.` — printed AFTER every `PatchAll` (Plugin.cs:216). HarmonyX
  `PatchAll` **throws** on any unresolvable `[HarmonyPatch(typeof(...))]` target, so this
  clean line with **ZERO** SBPR `Failed to patch` entries in the live window (10:52:41→)
  proves all three seams attached. The single `Failed to patch void ZNetScene::Awake()`
  `CultureNotFoundException` at 10:52:37 is the documented pre-boot BepInEx `UnityPatches`
  teardown noise (predates the SBPR boot line), structurally unrelated.
- Runtime `composed (server-authoritative)` ×3, `SpecCheck ✓ 31 recipes`, `Load world`,
  `Game server connected`, `[stone-areas] registered=7`.

A `MetadataLoadContext` probe over the deployed `assembly_valheim.dll` (the actual game the
server runs) asserts every vanilla target/field the three seams touch resolves:
`InventoryGui.DoCrafting`, `ZNet.OnNewConnection(ZNetPeer)`, `ZNet.GetServerRPC`,
`ZNet.IsServer`, `ItemDrop.ItemData.GetTooltip(ItemData,int,bool,float,int)`,
`ItemDrop.ItemData.m_customData`, `ZRpc.Register`, `ZRpc.Invoke`, `ZNetPeer.m_rpc` — **all
RESOLVE**. Raw capture: `capture/t022-masterwork-nodeown-live-20260719-105702.log`.

## What is REASONED, not observed (honest last mile — "logs-green ≠ playable")

Two live-`Player` surfaces a headless `-nographics` server cannot supply:
- Issuance send is a postfix on the crafting client's `InventoryGui.DoCrafting`, gated to
  `player == Player.m_localPlayer` (`MasterworkDedicatedDeliveryObserver.cs:269-276`); the
  tooltip is a postfix on `ItemDrop.ItemData.GetTooltip`. A dedicated server has no local
  `Player`, so the live firing of the request send and the rendered tooltip line cannot be
  observed headless — identical to the accepted T026/T025/T030 last-mile limit.

Proven vs reasoned:
- **Proven:** (a) the server→wire→client delivery channel the review found missing is
  shipped and wired; (b) the full server-authority + signed-issuance + keyless-validation +
  verdict-cache + tooltip path returns the correct issue/preserve/transfer/tamper verdict
  across every activation and hostile vector via link-compiled real execution; (c) all three
  seams bind to real vanilla methods that resolve on the live game assembly, and install on
  a live server-authoritative boot with zero Harmony failures.
- **Reasoned (GUI last mile):** a human on a joined GUI client, Masterwork-active, crafting
  an eligible non-stackable durable item and seeing `Workmanship: Masterwork`, upgrading it
  and keeping the line, handing it to a second joined client that also sees it, and a
  hand-forged stamp showing no line — is reasoned from (a)+(b)+(c) and left for an owner GUI
  smoke, exactly as the merged sibling nodes established. The full GPU 4-AT run additionally
  requires progression-seeding a licensed client's bound principal (Masterwork purchase +
  active Stone relationship), which is owner-gated setup beyond this QA slot.

## Spec/docs concordance
No spec drift. Masterwork issues one deterministic `Workmanship: Masterwork` property
(requirements.md §Acceptance "Crafting" line 155; contracts.md §Crafting). SpecCheck recipe
count unchanged (31) — the node adds no recipe row. This evidence adds only documentation.

## Disposition
PASS for the T022 node-own joined-client definition-of-done at the delivery + runtime-binding
+ data layer: the listen-host-only gap the prior QA run proved is closed by the shipped
server→client delivery channel, all four ATs are exercised against real execution, and the
three runtime seams bind live on the real game assembly with zero failures. Merge remains
gated on independent adversarial review of exact head `8ccf6d3` + owner approval; PR #388
must also take a fresh current-`origin/main` exact-head check before merge (main has
advanced). QA altered no production server and no client binary.
