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

## Runtime seam (net48, host + pure-client authoritative)

- `src/SBPR.Niflheim.HomesteadStones/Features/Archer/FieldFletchingRecipeGate.cs`
  — a postfix on `Player.RequiredCraftingStation` (decomp :17790, the station gate
  `Player.GetAvailableRecipes` :20443 and `Player.HaveRequirements` consult). When
  vanilla refused a recipe purely on its station requirement, the recipe is the
  exact vanilla Wood Arrow (`ArrowWood`, matched by output-ItemDrop prefab name,
  never a display string), and Field Fletching I is active for the local occupant,
  the postfix rescues the result to TRUE — station-free Bushcraft. Vanilla PASS is
  never flipped to fail; no other recipe is touched.
- **Single authority:** the exposure verdict routes through the shipped, unit-tested
  `BushcraftRecipeProvider.Resolve(stone, character, authority)` (host) or the
  server-derived `PersonalActivationSnapshot` (client). The gate re-derives nothing
  and holds no parallel ledger.
- Registered in `Plugin.Awake` alongside the T025-RT Archer patches; the personal
  delivery transport is registered right after the Local delivery transport.

### Activation source — fail closed, two authoritative paths (T026 remediation `t_3a899381`)

The T026 review of PR #373 (adversarial card `t_49e78f41`) verified the slice was
engineering-clean but **correctly refused merge**: Field Fletching I was
host-occupant-only because no authoritative Personal Character-Effect server→client
delivery channel existed, so a pure joined client always failed closed and the
required node-owned craft artifact was PENDING. This remediation replaced the
host-only lookup with a **bounded authoritative Personal Character-Effect delivery /
read-model channel**, mirroring the accepted Local Effect snapshot/delivery
architecture while preserving Personal ownership semantics.

- **Authoritative HOST** (listen-server / singleplayer host): the composed
  `LocalProgressionObserver.Server` holds the character / authority / Stone stores.
  The gate resolves the acting occupant's bound internal principal + Stone Area
  membership from `FoundationalPlacementObserver.Server` (server-owned facts, never
  a client claim), pulls the three aggregates, and asks
  `BushcraftRecipeProvider.Resolve(...).WoodArrowRecipeExposed`.
- **Pure remote CLIENT**: the gate reads **only** the server-stamped
  `PersonalActivationSnapshot` the server pushed into
  `LocalProgressionObserver.PersonalClientCache` over the
  `PersonalActivationDeliveryObserver` transport, and opportunistically requests a
  fresh snapshot for the Stone the local player stands in on a bounded (2s) interval.
  No held snapshot, a denied snapshot, standing outside every Stone Area, or an
  inactive row ⇒ the recipe keeps its vanilla station requirement. The client
  authors no entitlement.

The delivery substrate is engine-free and unit-tested (link-compiled net8 + shipped
net48):

- `Application/Activation/PersonalActivationDelivery.cs` — the bounded wire contract
  (`PersonalActivationSnapshot` read model + `PersonalActivationNotification`
  invalidation event, each carrying stable IDs, the Stone/character/authority
  revisions, and a monotonic per-`(occupant, character)` delivery sequence).
  `Denied(...)` is the fail-closed empty, all-inactive snapshot.
- `Application/Activation/PersonalActivationService.cs` — the SERVER authority. Every
  snapshot is a fresh derivation of the shipped `DerivedActivationView` (purchase
  record AND active relationship, per character) from the authoritative
  Stone/character/authority stores. **No second active-effects ledger**; the only
  state is a per-caller monotonic delivery sequence (delivery metadata, never
  gameplay authority). Composed into `LocalProgressionServer.PersonalActivation`.
- `Application/Activation/PersonalActivationClientCache.cs` — the bounded client
  consumer. Applies a snapshot only when its sequence ≥ the last applied one
  (stale/reordered dropped), decides refetch from a notification whose sequence or
  revisions moved ahead, and fails closed on an unknown caller / denied snapshot.
  Invalidate + Clear drop held snapshots on relationship loss / disconnect / teardown.

**Ownership semantics preserved.** Unlike the Local channel, the personal effect is
NOT gated by occupancy, the Settlement Local policy, or governor presence — active ==
(purchase AND active relationship), per character. The server resolves the requesting
peer's BOUND INTERNAL principal from the delivering ZRpc, never the payload, so a
hostile client cannot forge whose effect it asks for or author an active row.
Relationship loss / disconnect / dormancy flip Active to false with zero writes.

### Red-first delivery tests

`tests/NiflheimPersonalEffectDeliveryTests.cs` (link-compiled net8 suite): authenticated
server snapshot, bound principal (no cross-account leak), monotonic revision/replay
(Publish bumps sequence, Fetch does not), stale/out-of-order rejection, disconnect /
cache invalidation, hostile payload/identity, dormant / released fail-closed,
listen-host and pure-client consumers, wire round-trip, and NO second active-effects
ledger (relationship loss↔restore is pure re-derivation). Full suite **1308/1308**.

## Joined-client / in-world artifact — status: DELIVERED (delivery layer) at merged head

The authoritative pure-client delivery channel is shipped and **merged** (PR #374 @
`33461d1`), so a real joined (non-host) client resolves Field Fletching I exposure
from a server-stamped snapshot rather than failing closed. The QA card
(`t_e9fffb41`) verified the pure joined-client craft path at the delivery + data
layer against the exact merged implementation — see
[R2-joined-client-proof.md](R2-joined-client-proof.md): active exposure of the
unchanged `ArrowWood` recipe station-free, and removal on release / dormancy /
disconnect / cache-clear, with stale/out-of-order snapshots unable to reactivate and
ineligible recipes untouched — all proven over the real server→wire→client cache
path. The remaining GUI-pixel last mile (a human on a joined GUI client seeing Wood
Arrows craftable away from a station while Attuned, and losing it on release) is
**reasoned** from the verified layer under the task safety gate — no user-owned
`valheim.x86_64` GUI client is launched, and production Niflheim/Heistan are never
touched. **Logs-green is never playability**, so that human-pixel smoke is left for
the owner; everything server-authoritative and client-consumable is verified.
