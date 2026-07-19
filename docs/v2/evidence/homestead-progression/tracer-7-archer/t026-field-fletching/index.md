---
status: current
---

# T026 Field Fletching I — machine manifest

Node: Archer / Field Fletching I (Character Effect, personal Offered, executable).
Acceptance: `AT-FIELD-FLETCHING`. Engine-free vertical + host runtime seam shipped
green; joined-client / in-world craft artifact PENDING (no GUI client available —
see [README.md](README.md)). Logs-green is never playability.

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
| T9 | Runtime seam: `Player.RequiredCraftingStation` postfix rescues the exact Wood Arrow recipe to station-free when the provider reports it exposed for the local occupant; host-authoritative, fails closed on pure client (no personal-effect delivery channel yet — follow-up) | `src/SBPR.Niflheim.HomesteadStones/Features/Archer/FieldFletchingRecipeGate.cs` |
| T10 | Engine-free provider: no UnityEngine/BepInEx/Valheim type in tested source; net8 link-compile = real execution | `src/SBPR.Niflheim.HomesteadStones/Adapters/Archer/BushcraftRecipeProvider.cs` |

- [README.md](README.md) — full node writeup
