---
status: current
---

# T023 — Built to Last as durable future-output provenance

Acceptance: `AT-BUILT-TO-LAST`.

Built to Last is the Crafting branch's personal **Permanent Effect**: once a
character has bought it, every *future* eligible item they craft is issued a
maximum-durability improvement, bound to that exact instance behind a
server-keyed integrity token. Being a Permanent Effect, it keeps issuing after
relationship release, Tree revocation, and a server restart — and, critically,
it never reaches back and rewrites an item that already exists in the world.

## What is proven here, and what is not

**Proven (engine-free, headless):** the issuance decision, the stamp/read/
validate codec, the effective-maximum-durability derivation, idempotency, the
tamper/degrade matrix, and every durability and no-retroactive-mutation claim
below — all as real executed tests over the exact production code.

**NOT proven:** anything in-world. No Valheim client was available during this
work, so no joined-client claim is made. "Logs green ≠ playable" applies in
full. The in-world artifact is produced separately and is the real bar; the
independent Tracer-6 gate is T024.

## Engine-free CLEAN-side proof (landed)

Two pure files, composed over the already-accepted shared grammar:

- `Domain/CharacterProgression/DurabilityProvenance.cs` — the durability
  provenance codec. Sibling of the accepted T022 `WorkmanshipCodec`, over the
  same abstract `IItemMetadataWriter` / `IItemMetadataReader` surface, so the
  exact production stamp/read/validate runs headless against an in-memory map.
  Two deliberate differences:
  - **Domain separation.** It signs the canonical domain `builttolast-v1` and
    writes the disjoint `niflheim.durability.*` key namespace. Both provenances
    are HMAC-SHA-256'd under the *same* server-owned `WorkmanshipIntegrityKey`
    (one key file, one rotation surface), so the domain label is what makes a
    Workmanship token unusable as a durability token, and vice versa.
  - **The stamp carries a value, not just a seal.** Masterwork stamps one named
    seal; Built to Last freezes the *configured maximum-durability factor* that
    was in force at issuance into the signed fact.
- `Adapters/Crafting/DurabilityIssuanceProvider.cs` — the pure decision plus the
  read side. Entitlement is the character's **durable purchase record alone**
  (outcome class `PermanentEffect`, exact `BuiltToLast@1`): the provider takes no
  Stone aggregate and no authority index, so there is structurally no
  relationship / policy / permission / development conjunct that could be lost.

### Why "no retroactive mutation" is structural, not a promise

An item's effective maximum durability is derived from the vanilla maximum and
**only the signed stamp that exact instance carries**
(`DurabilityIssuanceProvider.ResolveMaxDurability`). The crafter's current
relationship, current purchases, and the *currently configured* factor are
deliberately not inputs. Three consequences, each a test:

1. An item crafted before acquisition carries no stamp → reads `Absent` → keeps
   the vanilla maximum forever. Acquiring the effect writes nothing to it.
2. Retuning the configured factor changes only what *future* issuances freeze.
   An already-stamped item's token is signed over the old factor, so the new
   value is simply not part of that instance's fact.
3. Losing the effect after issuance does not strip an already-issued item — the
   improvement is the instance's own durable fact.

And the write path can only touch the item vanilla just produced: the issuance
seam is a single `InventoryGui.DoCrafting` postfix that never enumerates or
rewrites existing inventory.

## Machine manifest

Source: `tests/NiflheimBuiltToLastTests.cs` (35 tests, all executed green).

| id | claim | artifact |
|----|-------|----------|
| BTL1 | Acquired Built to Last issues the configured maximum-durability property on an eligible non-stackable durable output | `AcquiredBuiltToLast_IssuesConfiguredMaxDurabilityProperty_OnEligibleDurableOutput` |
| BTL2 | Issuance is deterministic (no RNG) | `Issuance_IsDeterministic_SameInputsProduceTheSameStamp` |
| BTL3 | Without the purchase, nothing is issued | `WithoutBuiltToLast_IssuesNothing` |
| BTL4 | Only a `PermanentEffect`-class purchase counts; a same-keyed Character-Effect purchase does not | `OnlyPermanentEffectPurchaseCounts_NotACharacterEffectOfTheSameNode` |
| BTL5 | A sibling Masterwork purchase never grants Built to Last | `SiblingMasterworkPurchase_DoesNotGrantBuiltToLast` |
| BTL6 | Eligibility = non-stackable AND durable (full matrix) | `EligibilityMatrix_OnlyNonStackableDurableOutputsReceiveTheProperty` |
| BTL7 | An already-stamped instance is a no-op (idempotent) | `AlreadyStampedInstance_IsANoOp_NeverReIssuesOrOverwrites` |
| BTL8 | A replayed production event against one item stamps once; bytes unchanged | `RepeatedIssuanceAgainstOneItem_StampsOnce_AndTheStampIsUnchanged` |
| BTL9 | Issuance survives relationship loss (no relationship input exists) | `IssuanceSurvivesRelationshipLoss_FutureOutputsStillReceiveTheProperty` |
| BTL10 | Issuance survives Tree revocation (no development input exists) | `IssuanceSurvivesTreeRevocation` |
| BTL11 | Issuance survives restart via the serialized character aggregate | `IssuanceSurvivesRestart_RoundTripsThroughSerializedCharacter` |
| BTL12 | Repeated resolution mutates no state (no second ledger) | `ResolvingRepeatedly_MutatesNoState` |
| BTL13 | A stamped item resolves to the improved maximum durability | `StampedItem_ResolvesToTheImprovedMaximumDurability` |
| BTL14 | **An item crafted before acquisition is never retroactively improved** | `ItemCraftedBeforeAcquisition_IsNeverRetroactivelyImproved` |
| BTL15 | **Retuning the configured factor does not alter already-crafted items** | `RetuningTheConfiguredFactor_DoesNotAlterAlreadyCraftedItems` |
| BTL16 | **Losing the effect does not strip an already-issued item** | `LosingTheEffectAfterIssuance_DoesNotStripAnAlreadyIssuedItem` |
| BTL17 | A configured factor below vanilla neutral is rejected (never a nerf) | `ConfiguredFactorBelowVanillaNeutral_IsRejectedAtConstruction` |
| BTL18 | The stamp round-trips exactly through the codec | `Stamp_RoundTripsExactly_ThroughTheCodec` |
| BTL19 | The stamp survives transfer (exact custom-data map re-validates) | `StampSurvivesTransfer_TheExactCustomDataMapRevalidatesIdentically` |
| BTL20 | Upgrade carry-forward restores the exact signed bytes (preserve, not reissue) | `StampSurvivesUpgrade_CaptureRestoreCarriesTheExactSignedBytes` |
| BTL21 | An empty capture clears residue — a fresh replacement stays vanilla | `RestoringAnEmptyCapture_ClearsAnyDurabilityKeys_FreshReplacementStaysVanilla` |
| BTL22 | A hand-edited factor reads Tampered and degrades to vanilla | `HandEditedFactor_ReadsTampered_AndDegradesToVanilla` |
| BTL23 | A forged or missing token degrades to vanilla | `ForgedOrMissingToken_ReadsTampered_AndDegradesToVanilla` |
| BTL24 | An unknown schema degrades to vanilla | `UnknownSchema_ReadsTampered_AndDegradesToVanilla` |
| BTL25 | A foreign server key never validates here | `ForeignServerKey_NeverValidatesHere_DegradesToVanilla` |
| BTL26 | A stamp lifted onto a different item type fails validation | `StampLiftedOntoADifferentItemType_FailsValidation` |
| BTL27 | A torn/partial write reads Malformed and degrades | `PartialWrite_ReadsMalformed_AndDegradesToVanilla` |
| BTL28 | An unstamped item reads Absent, not Tampered | `UnstampedItem_ReadsAbsent_NotTampered` |
| BTL29 | Workmanship and durability stamps coexist on one item without interference | `WorkmanshipAndDurabilityStamps_CoexistOnOneItem_WithoutInterference` |
| BTL30 | A durability token cannot be replayed as a Workmanship token | `ADurabilityTokenIsNotAWorkmanshipToken_CrossDomainReplayFails` |
| BTL31 | The fingerprint changes whenever any signed field changes | `Fingerprint_ChangesWheneverAnySignedFieldChanges` |
| BTL32 | The pre-derived overload is the same policy as the aggregate overload | `BooleanOverload_AgreesWithTheAggregateOverload` |

### Red-first probes (confirmed, then reverted)

- Neutralizing the idempotency guard (`AlreadyHasValidDurabilityStamp` made
  unreachable) → **2 failures** (BTL7, BTL8), then reverted green.
- Neutralizing the degrade path (returning the stamp factor regardless of read
  state) → **4 failures** across the tamper/absent tests, then reverted green.

## Runtime seam (net48) — three registered patch classes

A patch class is not landed until its `PatchAll` line exists (AGENTS.md; this
family has shipped inert three times). All three are registered in
`Plugin.Awake()` and armed/disarmed with the durable key in
`FoundationalRuntimeBootstrap`.

| class | vanilla binding | role |
|---|---|---|
| `Features/Crafting/BuiltToLastIssuanceObserver` | `InventoryGui.DoCrafting` postfix (decomp :42523) | Stamps the frozen factor onto the freshly produced eligible item on the authoritative host and explicitly dirties persistence via `Inventory.Changed()` (:57540). Entitlement resolves the **bound internal principal** through `BoundSessions` — the same identity space the purchase committed under (the T022 R6 sender-binding lesson). |
| `Features/Crafting/BuiltToLastMaxDurabilityPatch` | `ItemDrop.ItemData.GetMaxDurability(int)` postfix (decomp :58135) | Scales vanilla's answer by that instance's frozen factor. This overload is the single vanilla authority — the parameterless `GetMaxDurability()` (:58130) delegates to it and `GetDurabilityPercentage()` (:58120) divides by it — so one patch covers the durability bar, the wear maths, and every consumer with no second copy of the policy. `m_shared` is never written (no shared-prefab mutation, the T030 discipline). Hot-path guards: one dictionary `ContainsKey` rejects an unstamped item before any crypto, and a stamped item's verdict is memoized against the complete signed-stamp fingerprint, so a mutated signed byte misses the memo and is re-validated. |
| `Features/Crafting/BuiltToLastUpgradePreservationObserver` | `InventoryGui.DoCrafting` prefix+postfix, `Priority.First` | Vanilla's upgrade branch destroys the source instance and creates a fresh replacement with an **empty** custom-data map, so the signed stamp is captured and restored byte-for-byte. No re-mint, no re-sign — an upgrade *preserves* rather than reissues, and a retuned current factor cannot leak in through one. |

All three fail closed with no armed server key: a pure remote client issues
nothing and applies nothing. As with the accepted T021/T022 host-first cuts, the
proven issuance topology is a listen-host / dedicated-host crafter;
authoritative server→client durability replication for a pure remote crafter is
the documented follow-up.

## Gate results actually run

- Full suite: **1743/1743** passing.
- `SBPR.Niflheim.HomesteadStones` Release (net48): **0 warnings / 0 errors**.
- `SBPR.Trailborne` Release (net48): **0 warnings / 0 errors**.
- `python3 scripts/docs-lint.py`: OK, 242 docs.
- `git diff --check`: clean.
- `src/SBPR.Trailborne/Runtime/SpecCheck.cs` recipe manifest: **checked and
  correctly unchanged** — Built to Last registers no SBPR recipe, piece, or
  station, so the tally is unaffected.

## Tuning surface

The factor is **1.25** (a 25% higher maximum durability on an issued instance).
It is the single knob and is provisional, consistent with research.md's
"most effect factors" configurable row and the Savor-50% / Iron-Stomach-75%
precedents. It is frozen per issued item, so retuning it is safe by
construction: it changes future issuances only.
