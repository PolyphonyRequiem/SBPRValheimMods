---
title: "QA-M4 — Action & evidence implementation contract (ADR-0009 §4/§6/§10)"
status: accepted
card: t_32eb1bd8
supersedes: none
depends_on:
  - docs/decisions/0009-qa-harness-separate-fail-closed-mod.md   # ADR-0009 (spec anchor)
  - qa/decomp-map/VANILLA-BINDINGS.md                            # PR #408 accepted binding map (M2–M4)
  - qa/tests-core/EvidenceM4Tests.cs                             # the executable acceptance suite
---

# QA-M4 — Action & evidence implementation contract

**Scope.** This is the buildable implementation spec for ADR-0009 **M4**
(adversarial + evidence hardening) against the *existing* repository. It converts
the accepted vanilla binding map (`qa/decomp-map/VANILLA-BINDINGS.md`, "PR408"
below) and the QA-M4 requirements (ADR-0009 §4, §6, §10) into bounded interfaces,
inputs, outputs, failure behavior, and limits, plus exact acceptance criteria.

**Firewall preamble (load-bearing, both directions).** Every helper primitive
defined here emits **primitive facts only**. No M4 primitive, adapter, or
receipt may ever emit an acceptance-test **PASS** or a runner **verdict** — only
the external Python runner (`qa/runner/`) composes PASS/FAIL, and only after it
correlates receipts and confirms cleanup (ADR-0009 §4, §6). This is enforced
structurally, not by convention: see §5 (product firewall) and the
`ReceiptOutcome`/`FactSource` shapes.

**Placement (single PR).** The M4 evidence core is engine-free and lands under
`qa/SBPR.QaHarness.T022/Evidence/` (System.* only), link-compiled by the net8
xUnit suite `qa/tests-core/` and consumed under net48 by the helper — the same
build-on-reviewed-head pattern M1/M2/M3 used. Files:

| Path | Role |
|------|------|
| `qa/SBPR.QaHarness.T022/Evidence/EvidenceReason.cs` | Additive M4 reject taxonomy (leaves M1/M2/M3 files byte-identical). |
| `qa/SBPR.QaHarness.T022/Evidence/ItemFingerprint.cs` | `ItemFingerprint` + `ItemContinuity` (transfer/upgrade continuity). |
| `qa/SBPR.QaHarness.T022/Evidence/TamperPolicy.cs` | Bounded replace/remove tamper firewall. |
| `qa/SBPR.QaHarness.T022/Evidence/RedactedReceipt.cs` | `RedactedReceipt` + `ReceiptFirewall` + `ReceiptOutcome`. |
| `qa/SBPR.QaHarness.T022/Evidence/ReceiptHashChain.cs` | Hash-chained receipts + connection-generation `ReceiptCache`. |
| `qa/SBPR.QaHarness.T022/Evidence/ActionObservationAdapters.cs` | `IActionAdapter`/`IObservationAdapter`/`IPeerBindingAdapter` seams, `FactSource`, `ProductFirewall`. |
| `qa/contracts/receipt.schema.json` | Wire truth: mechanical evidence-outcome shape + `connectionGeneration`. |
| `qa/tests-core/EvidenceM4Tests.cs` | The named + adversarial acceptance suite (headless, no SDK). |
| `.github/workflows/ci.yml`, `qa/README.md`, ADR-0009 §10 impl note | Spec/CI move with code (CONTRIBUTING triangle). |

**M4 non-scope (hard).** No fabricated product state; no reflection into product
verdict caches; no AppDomain scanning; no ScriptTools/Terminal binding or locking;
no live channel/socket/ZRpc/Harmony hook; no craft/tamper *execution*; no
deployment/runtime; no runner verdict; no M5 packaging or M6 live qualification.
The M4 core is a set of **pure decision functions + inert seam contracts**; the
game-touching adapters are the net48 slice and are out of this milestone's proof.

---

## 1. Genuine craft/upgrade invocation through the product seam

**Interface.** `IActionAdapter.Craft(string recipeName, string station)` and
`IActionAdapter.UpgradeItem(int itemSlot, int targetQuality)`
(`Evidence/ActionObservationAdapters.cs`). **Contract-only** in M4 — the net48
helper implements the body behind the single-slot dispatcher; the M4 core proves
the *evidence mapping* the receipt must carry, not the invocation.

- **Binding (PR408 §3.6, client-only-live).** `InventoryGui.DoCrafting(Player)`
  (private @1500) is **the** issuance seam the product's Workmanship hook rides.
  There is **no public "craft this" API**. The adapter selects the recipe via
  `SetRecipe(index)` (found from `m_availableRecipes`), then drives the private
  `OnCraftPressed()` via Harmony/`AccessTools`, letting
  `UpdateRecipe→DoCrafting` run naturally on subsequent frames. Upgrade == the
  same path with a non-null `m_craftUpgradeItem`; `DoCrafting` computes
  `targetQuality = m_craftUpgradeItem.m_quality + 1`.
- **Inputs.** `recipeName` (must resolve in `m_availableRecipes`), `station`
  (must match `Player.GetCurrentCraftingStation()`); upgrade takes an `itemSlot`
  (resolves via `Inventory.GetItem(index)`) and an expected `targetQuality`.
- **Output.** A `RedactedReceipt` whose `Observed` records the **result** —
  post-action item `prefab`, `quality`, present custom-data KEY names, and (for
  the visible stamp) tooltip text via §4. The receipt records *that the helper
  drove the seam and observed a result*; it must never claim the helper minted
  the stamp (§5, `ProductFirewall`).
- **Failure behavior.** `DoCrafting` **silently no-ops** on unmet requirements /
  no open station (guards @1502/@1515) → the adapter MUST observe the *result*
  (item present + quality) rather than assume success; `m_craftTimer` must be
  honored or the craft effect double-fires. Client-role only —
  `InventoryGui.instance`/`Player.m_localPlayer` are null on the dedicated
  server. Reflection into game privates via Harmony/`AccessTools` is
  clean-room-permitted (ADR-0001 wall is around *other mods*, not the game we
  mod); **no publicized game DLL** may enter the build (PR408 §1).
- **Limits.** One primitive in flight (single-slot dispatcher, PR408 §3.2); main
  thread only.

**Evidence mapping (the M4-proven part).**
`ItemContinuity.CheckUpgrade(source, replacement, targetQuality)`
(`ItemFingerprint.cs`) returns `EvidenceReason.None` iff:

1. same continuity key `TrackId:Prefab` (identity preserved),
2. `replacement.Quality == targetQuality == source.Quality + 1`
   (else `InvalidUpgradeMapping`),
3. every source custom-data key survives (else `ContinuityBroken`),
4. **no new signature-prefixed key** appeared on the replacement (else
   `TamperWouldAddSignature`) — the **no-second-issuance** guard.

---

## 2. Exact tracked-item drop & pickup across distinct clients

**Interface.** `IActionAdapter.DropItem(int itemSlot)` (giver, role Client A) and
`IActionAdapter.PickUpNearest(string itemName, double radius)` (receiver, role
Client B).

- **Binding (PR408 §3.7).** `Humanoid.DropItem` (@767) → `ItemDrop.DropItem`
  (@1646) which **`Clone()`s** the `ItemData` (deep-copying `m_customData` @412)
  onto the real world drop; the receiver resolves the world `ItemDrop` by
  proximity and calls `Humanoid.Pickup(go)` (@588). The stamp carried in
  `m_customData` survives the round-trip through the ZDO save/load (PR408 §3.9).
  The transfer is a **genuine world-item hop**, not a synthetic copy.
- **Inputs.** Giver slot index; receiver `itemName` + `radius` (≤ `Rmax`, a
  bounded manifest constant — see OPEN-1).
- **Output.** Two receipts (drop, pickup), each with the item fingerprint the
  runner correlates.
- **Failure behavior.** `ItemDrop.CanPickup` is false during the auto-pickup
  delay → the receiver polls on the dispatcher within a bounded FSM (**no
  sleeps**). Dropping an equipped item requires `UnequipItem` first. Amount >
  stack silently clamps.
- **Limits.** `radius ≤ Rmax`; distinct giver/receiver aliases required (§ below).

**Evidence mapping.**
`ItemContinuity.CheckTransfer(giverAlias, receiverAlias, dropped, pickedUp)`:

- distinct aliases required — an identical giver/receiver returns
  `EvidenceReason.SelfTransfer` (a self-transfer is not a transfer),
- then full `CheckContinuity`: same `ContinuityKey` **and** every custom-data key
  present before the hop is still present after (a dropped stamp key ⇒
  `ContinuityBroken`).

---

## 3. Allowlisted replace/remove tampering on an exact throwaway item

**Interface.** `IActionAdapter.TamperField(int itemSlot, string fieldName,
TamperOperation operation)`, gated by `TamperPolicy.Validate(...)`
(`TamperPolicy.cs`).

- **Binding (PR408 §3.9, ADR-0009 §4, threat T5).** Operates on
  `ItemDrop.ItemData.m_customData` (@392) **in-memory** on the throwaway item;
  persistence rides the item's normal `SaveToZDO`/`LoadFromZDO` path. **Never**
  edit a product-store copy.
- **Inputs / limits (fixed-order, fail-closed).**
  1. item MUST be a tracked run-scoped **throwaway** (`isThrowawayItem`) — else
     `TamperItemNotThrowaway`;
  2. operation MUST be `Replace` or `Remove` — `TamperOperation` has **no `add`
     member** (structural, threat T5) — a defensive undefined-value check returns
     `TamperWouldAddSignature`;
  3. non-empty `fieldName`, and it MUST NOT be a signature-shaped key
     (`ItemContinuity.LooksLikeSignature`, prefixes `sbpr_sig`, `sbpr_hmac`,
     `sbpr_provenance`) — else `TamperWouldAddSignature`;
  4. `fieldName` MUST be in the static allowlist
     (`DefaultFieldAllowlist = {sbpr_workmanship_display,
     sbpr_workmanship_grade_label}`) — else `TamperFieldNotAllowlisted`;
  5. `fieldName` MUST already be **present** — replace/remove of an EXISTING key
     only — else `TamperFieldNotPresent` (an absent key would be an add).
- **Output.** A `RedactedReceipt`; on refusal, `Outcome = Rejected` with the
  exact `RejectReason`.
- **Failure behavior.** Editing `m_customData` without re-saving leaves the ZDO
  copy stale (tamper appears not to stick). The allowlist and signature prefixes
  are **data reviewed like code**.

There is **no code path that adds a key**, so a signature can never be
minted/copied by this primitive.

---

## 4. Safe tooltip / item observations

**Interface.** `IObservationAdapter.ReadInventory/ReadItem/ReadTooltip/
ReadWorldName/ReadWorldUid` (`ActionObservationAdapters.cs`) — either-role, raw
facts only.

- **Binding.** `ReadTooltip` → `ItemDrop.ItemData.GetTooltip(stackOverride=-1)`
  (PR408 §3.8 @622), a **pure string builder** over a **static** `m_stringBuilder`
  — **main thread only** (dispatcher tick) or it corrupts an in-flight UI
  tooltip. This is deliberately the observation seam because it does **not** touch
  the `Terminal`/`ScriptTools` lock (PR408 §3.3). `UITooltip` lives in
  `assembly_guiutils`, not `assembly_valheim` — the harness reads the **text**,
  not the component. `ReadItem` emits prefab, quality, and present custom-data
  **KEY names only** (values redacted, §6). `ReadWorldUid` binds
  `ZNet.GetWorldUID()` (NOT `GetUID()`, which is the session id) and MUST
  null-check `ZNet.instance`/`ZNet.World`.
- **Output.** A `RedactedReceipt` built via
  `ReceiptFirewall.ExtractObservedFacts(prefab, quality, customData, tooltipText)`
  — emits `prefab`, `quality`, sorted `custom_key_names`, per-key
  `custom_value_digests` (bounded, non-reversible), and verbatim `tooltip_text`.
- **Labels.** A visible-Workmanship fact is `FactSource.Direct` **only** when read
  off the item's own tooltip; anything the runner derives by correlating receipts
  is `FactSource.Inferred` (`LabeledFact.Direct/Inferred`). Evidence honesty
  (§6, T11).
- **Limits.** No reflection into verdict caches (threat T4); reads only what a
  player would see or raw field *keys*.

---

## 5. Product firewall (helper never claims it produced product state)

**Interface.** `ProductFirewall.AssertNoProductStateClaim(RedactedReceipt)`
(`ActionObservationAdapters.cs`), the emission complement to
`ReceiptFirewall.AssertNoProductVerdict`.

- Raises `HelperVerdictException` if any observed key (case-insensitive) is in
  `ForbiddenClaimKeys = {minted, signed, granted, issued_by_harness,
  stamp_written}`. The harness may **observe** a stamp (via tooltip/field keys);
  it may never claim it **wrote** one (ADR-0009 §4, threat T11).

**Verdict firewall (ADR-0009 §6).** `ReceiptFirewall.AssertNoProductVerdict`
rejects any observed key in `ForbiddenObservedKeys = {pass, fail, verdict,
at_result, accepted}`. And structurally: `ReceiptOutcome` is a **mechanical enum
with no PASS/FAIL member** — `{Ok, Rejected, Busy, Timeout, Cancelled}` — so a
receipt cannot even represent a verdict. **Only the external runner composes
PASS/FAIL.**

---

## 6. Hash-chained receipts + connection-generation binding

**Interface.** `ReceiptHashChain` + `ReceiptCache` + `ConnectionId`
(`ReceiptHashChain.cs`); `RedactedReceipt` + `ReceiptFirewall`
(`RedactedReceipt.cs`).

- **Receipt shape.** `RedactedReceipt` is an immutable value object:
  `{RequestId, Verb, Role, WorldUid, Nonce, Seq, ConnectionGeneration, TsUnixMs,
  Outcome, Observed, RejectReason}`. `Observed` is an ordinal `string→object?`
  map of **descriptive facts only** — never a verdict-shaped key, never a raw
  custom-data value.
- **Redaction / limits.** `ReceiptFirewall.Redact(receipt, byteBudget=4096)`
  asserts no verdict, strips any raw values map (`custom_values`/`custom_data`)
  that leaked in, and collapses an oversized `tooltip_text` to a length marker
  when the approximate serialized size exceeds the byte budget — a hostile giant
  tooltip cannot blow the receipt channel. `BoundedDigest(value, cap=12)` yields
  a short `len=N;edge='h'..'t'` descriptor (NOT the raw value and NOT a full
  crypto hash, which could itself be a copyable token) — enough to prove
  presence/absence/change without surfacing the value (threat T5).
- **Hash chain.** `ReceiptHashChain.Append` redacts, then commits
  `LinkHash = SHA256(prevHash + "\n" + CanonicalReceiptString(receipt))`;
  `CanonicalReceiptString` is an order-stable newline-joined string over the
  authenticated fields (RequestId…RejectReason). `FindFirstBreak` / `Verify`
  detect any **insert / drop / reorder / edit** by recomputing every link and
  checking `Index` and `PrevHash` continuity.
- **Connection-generation binding.** `ReceiptCache` dedups on `(RequestId, Seq)`.
  `IsStaleGeneration(current, receiptGeneration)` is true when
  `receiptGeneration < current.Generation`; `Get` refuses to resurrect a cached
  receipt minted on an **older** generation (post-reconnect replay) →
  `StaleConnectionGeneration`. `ConnectionGeneration` is part of the canonical
  HMAC input (`qa/contracts/envelope.schema.json`, §3.2/§5.1) and appears in the
  receipt schema.
- **Threading.** `ReceiptCache` is not thread-safe by itself; the single-slot
  dispatcher serializes it.

---

## 7. Connection-generation delivering-peer binding

**Interface.** `IPeerBindingAdapter.BindDeliveringPeer(RequestEnvelope)`.

- **Binding (PR408 §3.4, ADR-0009 §5.1).** Map the inbound `ZRpc rpc` → peer via
  `ZNet.GetPeer(rpc)` (@729/@820); read `ZRpc.GetSocket()` for socket identity;
  wait for `ZNetPeer.IsReady()` (`m_uid != 0`). The server binds the **actual
  delivering peer** and **ignores** any identity claimed in the envelope — a
  mismatch is a peer substitution the caller rejects. Returns the peer uid, or
  `null` to reject.

---

## 8. Direct-vs-inferred fact labels

Covered in §4/§5: `FactSource.{Direct, Inferred}` + `LabeledFact`. A Masterwork
stamp is `Direct` **only** when read off the item's own tooltip; a "transfer
preserved" conclusion spanning two clients' receipts is `Inferred`. The label is
carried so the runner knows the strength of each fact; the label is a *fact about
the fact*, never a verdict.

---

## 9. Exact acceptance criteria

All proven headless in `qa/tests-core/EvidenceM4Tests.cs` (net8 xUnit, no Valheim
SDK). Named ATs (ADR-0009 §10 M4):

| AT | Criterion | Proof |
|----|-----------|-------|
| **CRAFT-THROUGH-PRODUCT-SEAM** | A Masterwork stamp appears **only** because product issuance ran; the harness records driving the seam + observing a result, never minting. Upgrade mapping accepts iff same identity + quality+1 + keys preserved + no new signature key. | `Upgrade_ValidMapping_Accepted`, `Upgrade_RejectsWrongQualityBump`, `Upgrade_RejectsDroppedStampKey`, `ProductFirewall_RejectsHarnessStateClaim` |
| **TOOLTIP-OBSERVE** | `ReadTooltip` surfaces verbatim in-world Workmanship text as a `Direct` fact; observation emits raw facts only (prefab/quality/key-names/tooltip), never a value or verdict. | `Observe_EmitsRawFactsOnly`, `LabeledFact_DistinguishesDirectFromInferred` |
| **TRANSFER-PRESERVES** | Receiving client observes the preserved stamp across **distinct** aliases; self-transfer and any dropped stamp key or changed identity are rejected. | `Transfer_PreservesTrackedItem_AcrossDistinctAliases`, `Transfer_RejectsSelfTransfer`, `Transfer_RejectsDroppedStampKey_ContinuityBroken`, `Transfer_RejectsDifferentIdentity`, `Continuity_AllowsQualityDifference` |
| **TAMPER-DEGRADES** | Replace/remove an existing allowlisted key on a throwaway item only; product renders no line; **no signature added/copied**. Non-throwaway, signature key, non-allowlisted field, absent field, empty field all fail-closed. | `Tamper_AllowsReplaceOrRemove_OnThrowawayAllowlistedPresent`, `Tamper_RejectsNonThrowawayItem`, `Tamper_RejectsSignatureKey`, `Tamper_RejectsNonAllowlistedField`, `Tamper_RejectsAbsentField_WouldBeAdd`, `Tamper_RejectsEmptyField`, `TamperOperation_HasNoAddMember` |
| **RECEIPT-HASH-CHAIN** | Append-only tamper-evident chain detects insert/drop/reorder/edit; stale (pre-reconnect) generations are rejected on replay. | `Chain_AppendsAndVerifies`, `Chain_DetectsEditedReceipt`, `Chain_DetectsReorder` (+ generation tests) |
| **CLEANROOM** | `reviewer-cleanroom` sign-off: every adapter method pins a PR #408 binding point (`TODO(PR408 §x.y)`), **no decompiled body**; genuine vanilla API + product public seams only; no other-mod source; no committed decomp. | Source review of `ActionObservationAdapters.cs` + `VANILLA-BINDINGS.md` |

### Adversarial cases (all fail-closed, zero side effect)

| Adversarial axis | Criterion |
|------------------|-----------|
| **No second issuance on upgrade** | `CheckUpgrade` rejects any new signature-prefixed key on the replacement (`TamperWouldAddSignature`). `Upgrade_RejectsNewSignatureKey_NoSecondIssuance`. |
| **Fingerprint continuity** | `ItemFingerprint` normalizes keys + equality; continuity is on `TrackId:Prefab`, quality may differ. `Fingerprint_NormalizesKeys_AndEquality`. |
| **Hostile stale-cache ordering** | `ReceiptCache` dedups on `(RequestId,Seq)` and refuses older-generation hits regardless of arrival order. |
| **Token / signature redaction** | Raw custom-data values never surface — only bounded digests; a values map is stripped in `Redact`. `Redact_StripsRawValueMaps`. |
| **Replay / stale generations** | `IsStaleGeneration` refuses `receiptGeneration < current.Generation` (`StaleConnectionGeneration`). |
| **Large-inventory / frame budget** | Oversized tooltip collapses to a length marker under the 4096-byte budget. `Redact_BoundsOversizedTooltip`. |
| **Verdict smuggling** | `ReceiptOutcome` has no PASS/FAIL member; forbidden observed keys rejected. `ReceiptOutcome_HasNoPassOrFailMember`, `Firewall_RejectsVerdictKey`. |
| **Product-state claim** | `ProductFirewall` rejects `minted/signed/granted/issued_by_harness/stamp_written`. `ProductFirewall_RejectsHarnessStateClaim`. |

---

## 10. Evidence citations & OPEN items

Every binding claim above cites the accepted map by section (PR408 §3.x) with the
exact vanilla member + decompile line, or the shipped Evidence source. No claim
is asserted without a citation.

**OPEN items (not guessed, routed rather than invented):**

- **OPEN-1 — `Rmax` transfer radius constant.** §2 references a bounded
  `PickUpNearest` radius `Rmax`; the concrete manifest value is owned by the
  fixture/manifest bounds (ADR-0009 §3.1), not fixed in the M4 evidence core.
  The M4 contract only requires `radius ≤ Rmax`; the literal is set where the
  manifest bounds live and is enforced at admission, not here.
- **OPEN-2 — production world `m_uid` literals.** Inherited from PR408 §5: the
  arming gate's production deny is fully covered by port (`2456`/`2466`) +
  disposable-world UID+name allowlist; the literal production `World.m_uid`
  integers are **not** pinned (reading a production save is off-limits and
  unnecessary). Not an M4 blocker — flagged only if a future gate card insists on
  UID-value deny.

Neither OPEN item blocks M4: both are outside the evidence/action layer this card
specifies, and the M4 core fails closed without them.

---

## 11. Single-PR expectation

Spec (this file + ADR-0009 §10 impl note), code (`Evidence/*.cs`), schema
(`receipt.schema.json`), and CI (`ci.yml`, `qa/README.md`) move **together** in
one PR (CONTRIBUTING triangle). Acceptance = the net8 xUnit suite green
(`dotnet test qa/tests-core/... -c Release`) **and** the net48 helper Release
build clean (0w/0e, `<TreatWarningsAsErrors>`) **and** `reviewer-cleanroom`
sign-off on `AT-QA-CLEANROOM`. **No live game launch, deploy, runtime, or runner
verdict is part of this milestone** — live qualification is the separate
operator-authorized M6 card.
