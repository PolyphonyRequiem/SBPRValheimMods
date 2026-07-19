---
status: current
---

# T026 Field Fletching I — machine manifest

Node: Archer / Field Fletching I (Character Effect, personal Offered, executable).
Acceptance: `AT-FIELD-FLETCHING`. Engine-free vertical + host runtime seam +
authoritative pure-client Personal Character-Effect delivery channel shipped green
and merged (PR #374 @ `33461d1`). Pure joined-client craft path verified at the
delivery + data layer; GUI-pixel last mile reasoned under the safety gate — see
[R2-joined-client-proof.md](R2-joined-client-proof.md). Logs-green is never
playability.

| id | claim | artifact |
|----|-------|----------|
| T1 | Active Field Fletching I (purchase record AND active relationship, via `DerivedActivationView`) exposes the unchanged vanilla Wood Arrow recipe through Bushcraft (station-free); inputs/yield/authority preserved | `tests/NiflheimFieldFletchingTests.cs` (ActiveEffect_ExposesUnchangedWoodArrowRecipeThroughBushcraft) |
| T2 | Purchased but no active relationship ⇒ dormant, exposes nothing | `tests/NiflheimFieldFletchingTests.cs` (PurchasedButNoRelationship_EffectDormant_ExposesNothing) |
| T3 | Active relationship but no purchase ⇒ not active, exposes nothing | `tests/NiflheimFieldFletchingTests.cs` (RelationshipButNoPurchase_ExposesNothing) |
| T4 | Undeveloped node ⇒ no derived row, exposes nothing even with purchase + relationship | `tests/NiflheimFieldFletchingTests.cs` (UndevelopedNode_EvenWithPurchaseAndRelationship_ExposesNothing) |
| T5 | Per-character effect: a sibling's reservation never activates the purchased caller | `tests/NiflheimFieldFletchingTests.cs` (SiblingCharacterActive_DoesNotLeakExposureToUnpurchasedCaller) |
| T6 | Relationship loss → restore flips exposure off/on with zero writes (no active-effects ledger) | `tests/NiflheimFieldFletchingTests.cs` (RelationshipLossThenRestore_FlipsExposureWithNoWrites) |
| T7 | Exposes ONLY the vanilla Wood Arrow (`ArrowWood`), never the Practice Range / Practice Arrow content (T025); content is station-free + unchanged | `tests/NiflheimFieldFletchingTests.cs` (ExposesOnlyWoodArrow…, ExposedRecipeContent_IsStationFreeAndUnchanged, NoneCapability_IsInert) |
| T8 | Full suite 1280/1280 (+9 this node); both net48 Release builds 0w/0e; docs-lint OK; `git diff --check` clean | build/test logs (this run) |
| T9 | Runtime seam: `Player.RequiredCraftingStation` postfix rescues the exact Wood Arrow recipe to station-free when the effect is active for the local occupant; host reads composed server stores, pure client reads the server-stamped `PersonalActivationSnapshot`, fails closed otherwise | `src/SBPR.Niflheim.HomesteadStones/Features/Archer/FieldFletchingRecipeGate.cs` |
| T10 | Engine-free provider: no UnityEngine/BepInEx/Valheim type in tested source; net8 link-compile = real execution | `src/SBPR.Niflheim.HomesteadStones/Adapters/Archer/BushcraftRecipeProvider.cs` |
| T11 | Authoritative pure-client delivery: server binds principal from ZRpc (never payload), derives per-(occupant,character) snapshot, client cache drops stale/reorder + fails closed on disconnect/denied/hostile; active/dormant/release verified over the exact server→wire→client path | `tests/NiflheimPersonalEffectDeliveryTests.cs`; `src/SBPR.Niflheim.HomesteadStones/Features/Progression/PersonalActivationDeliveryObserver.cs` |
| T12 | Merged @ `33461d1` (origin/main tip): both net48 Release 0w/0e, full suite 1355/1355, T026 subset 27/27, workbench 59/59, docs-lint OK. GUI last mile reasoned under safety gate | [R2-joined-client-proof.md](R2-joined-client-proof.md) |

- [README.md](README.md) — full node writeup
- [R2-joined-client-proof.md](R2-joined-client-proof.md) — merged-head pure-client delivery-layer proof
