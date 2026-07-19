---
status: current
---

# T026 — Field Fletching I: Bushcraft Wood Arrow recipe exposure evidence

Node: Archer / Field Fletching I (Character Effect, personal Offered, executable).
Acceptance: `AT-FIELD-FLETCHING`.

## What this node is (and is NOT)

Field Fletching I is a **personal Character Effect** — the SECOND Archer node and
the FIRST personal Character Effect in the codebase to reach runtime. It is a
different node *shape* from its sibling T025 Practice Range:

| | T025 Practice Range | T026 Field Fletching I |
|---|---|---|
| Outcome type | Local Effect (Stone-cultivated) | **Character Effect (personal Offered)** |
| Activation gate | Settlement Local policy AND ordinary build Permission | **purchase record AND active relationship** |
| Activation view | `LocalEffectActivationView` | **`DerivedActivationView`** |
| Content | ships NEW `ArrowPractice` item + 100-for-8 recipe | **authors NO new content** |
| Effect | unlocks Archery Target placement + Practice Arrow recipe | **exposes the UNCHANGED vanilla Wood Arrow recipe station-free (Bushcraft)** |

The whole node is an **exposure gate**: while Field Fletching I is active for the
caller, the existing vanilla `ArrowWood` recipe becomes craftable **without its
ordinary crafting station** (Bushcraft), and reverts to requiring the station when
the effect goes dormant. The provider authors and mutates **nothing** about the
recipe's inputs, yield, or authority (spec line 160 "exposes unchanged Wood Arrows
through Bushcraft while active"; contracts.md §Archer; research.md "Field Fletching
I = unchanged Wood Arrow recipe" — wider ammunition-registry / input changes are
explicitly deferred to later Field Fletching levels).

## Engine-free proof (shipped, green)

Provider: `src/SBPR.Niflheim.HomesteadStones/Adapters/Archer/BushcraftRecipeProvider.cs`.
Tests: `tests/NiflheimFieldFletchingTests.cs` (9 facts, all passing under the net8
link-compiled suite; full suite **1280/1280**).

### AT-FIELD-FLETCHING — active personal effect ⇒ unchanged Wood Arrow exposed through Bushcraft

- Active/dormant is re-derived through the shipped T004 `DerivedActivationView`:
  the effect is Active only when the caller holds a **purchase record** for
  `FieldFletchingI@1` at this Stone **AND** an **active relationship** to this
  Stone. No second "active effects" ledger — flip the relationship and re-derive
  with zero writes (AT-NO-ACTIVE-LEDGER).
- While active, `BushcraftRecipeCapability.WoodArrowRecipeExposed == true` and the
  exposed recipe is the **exact** vanilla `ArrowWood`, `StationFree == true`,
  `PreservesVanillaInputsYieldAuthority == true` (exposure only — no rewrite).
- Proven suppression paths (each ⇒ nothing exposed):
  - purchased but **no active relationship** → dormant;
  - **active relationship but no purchase** → not active;
  - node **not developed** on the Stone → no derived row;
  - a **sibling** character holds the reservation but the purchased caller does not
    → per-character effect does not leak;
  - relationship loss → restore flips exposure off/on with zero writes.
- Exposure targets **only** the Wood Arrow — never the Practice Range / Practice
  Arrow content (a separate Local node, T025).

## Runtime seam (net48, host-authoritative)

- `src/SBPR.Niflheim.HomesteadStones/Features/Archer/FieldFletchingRecipeGate.cs`
  — a postfix on `Player.RequiredCraftingStation` (decomp :17790, the station gate
  `Player.GetAvailableRecipes` :20443 and `Player.HaveRequirements` consult). When
  vanilla refused a recipe purely on its station requirement, the recipe is the
  exact vanilla Wood Arrow (`ArrowWood`, matched by output-ItemDrop prefab name,
  never a display string), and Field Fletching I is active for the local occupant,
  the postfix rescues the result to TRUE — station-free Bushcraft. Vanilla PASS is
  never flipped to fail; no other recipe is touched.
- **Single authority:** the exposure verdict routes through the shipped, unit-tested
  `BushcraftRecipeProvider.Resolve(stone, character, authority)`. The gate
  re-derives nothing and holds no parallel ledger.
- Registered in `Plugin.Awake` alongside the T025-RT Archer patches.

### Activation source — fail closed, honest transport scope

Field Fletching I is the **first personal Character Effect at runtime**, and the
bounded server→client delivery transport that Practice Range / Refined Workshop use
carries **Local-effect** snapshots only — there is **no personal Character-Effect
replication channel yet**. So the gate reads the authoritative projection where it
exists in-process:

- **Authoritative HOST** (listen-server / singleplayer host): the composed
  `LocalProgressionObserver.Server` holds the character / authority / Stone stores.
  The gate resolves the acting occupant's bound internal principal + Stone Area
  membership from `FoundationalPlacementObserver.Server` (server-owned facts, never
  a client claim), pulls the three aggregates, and asks
  `BushcraftRecipeProvider.Resolve(...).WoodArrowRecipeExposed`.
- **Pure remote CLIENT**: the server runtime is null and there is no personal-effect
  snapshot to consume, so the gate **fails closed** — Wood Arrow keeps its vanilla
  station requirement — rather than inventing an unauthenticated grant.

The proven Bushcraft topology for T026 is therefore the **host occupant**. A
personal-effect **client** delivery channel is a scoped follow-up (mirroring the way
the sibling Refined Workshop patch documented its listen-host self-delivery gap and
deferred it). This is stated plainly here because **logs-green is never playability**.

## Joined-client / in-world artifact — status

**PENDING.** No GUI `valheim.x86_64` client was running at implementation time
(only headless dedicated servers), and per the task safety gate no QA client is
launched while the desktop could be owner-occupied. The in-world craft capture (an
active-Field-Fletching-I host occupant crafting Wood Arrows with no crafting station
in range, and the recipe reverting to station-required when the effect is dormant)
is the one remaining item and is captured in a follow-up run once a client is
available. The engine-free vertical + host runtime seam above are shipped and green;
the client-delivery channel gap is documented, not hidden.
