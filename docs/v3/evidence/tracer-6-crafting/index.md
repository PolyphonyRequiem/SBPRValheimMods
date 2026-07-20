---
status: current
---

# Tracer 6 v3 evidence — machine manifest

Joined-client / live-artifact proofs for the Crafting branch that extend the v2
host-first evidence.

## T022 — Masterwork joined-client Workmanship issuance + delivery (PR #388 @ 8ccf6d3)

| id | claim | artifact |
|----|-------|----------|
| MW-ISSUE | Active-Masterwork joined crafter's item carries the deterministic signed Workmanship stamp; byte-identical to a host stamp | `tests/NiflheimMasterworkClientDeliveryTests.cs::ActiveMasterwork_ServerMintsAndSigns_JoinedClientWritesAndItReValidates`, `::ClientWrittenSignedStamp_IsByteIdenticalToAHostStampedOne`, `::IssuanceRequest_And_Grant_RoundTripThroughTheWire` |
| MW-ISSUE-FAILCLOSED | Dormant / ineligible / already-stamped → no issuance | `::InactiveMasterwork_ServerRefuses_ClientLeavesItemVanilla`, `::IneligibleOutput_ServerRefuses_EvenWhenActive`, `::AlreadyStampedInstance_ServerRefuses_Idempotent` |
| MW-UPGRADE | Upgrade preserving custom data keeps a valid stamp | `::ClientWrittenStamp_KeepsValidating_AfterUpgradeThatPreservesCustomData` |
| MW-TRANSFER | Receiving joined client validates a transferred stamp via the server (keyless read → verdict) | `::TransferredStamp_IsValidatedByReceivingClientViaServer_KeylessReadThenVerdict`, `::ValidationVerdict_RoundTripsThroughTheWire` |
| MW-TAMPER | Hand-edited / foreign-key / unconfirmed stamp degrades to vanilla (no tooltip line) | `::HandEditedStamp_GetsTamperedVerdict_CacheFailsClosed`, `::ForeignServerKeyStamp_GetsTamperedVerdictHere`, `::UnconfirmedProvenance_FailsClosed_InTheVerdictCache` |
| MW-KEY | Raw integrity key never appears on any serialized wire message | `::RawIntegrityKey_NeverAppearsOnAnySerializedWireMessage` |
| MW-BIND | All three runtime seams resolve on the live game assembly + install on a live dedicated-server boot with zero Harmony failures | `capture/t022-masterwork-nodeown-live-20260719-105702.log` (drift check clean, `Harmony patches installed`, MetadataLoadContext probe: InventoryGui.DoCrafting / ZNet.OnNewConnection / ItemDrop.ItemData.GetTooltip / ZRpc all RESOLVE) |

Full suite: **1447 / 1447**. Both net48 Release builds **0w / 0e**. Server DLL sha256
`3cd86e94c0a09d61e4843710fefd2408cd8c2e16470cae139025bac5816ee3b8`.

GUI-pixel last mile (human crafting/upgrading/transferring on a rendering client) is
REASONED, matching the merged sibling nodes T026 / T025 / T030.
