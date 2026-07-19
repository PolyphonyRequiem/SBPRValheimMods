---
status: current
---

# T025 — Practice Range: joined-client placement/recipe/impact evidence

Node: Archer / Practice Range (Local Effect, Stone-cultivated, executable).
Acceptance: `AT-PRACTICE-RANGE`, `AT-PRACTICE-ARROW-DAMAGE`, `AT-TARGET-RETURN`.

## Status: JOINED-CLIENT PROOF PENDING OWNER CLEARANCE

At implementation time a live `valheim.x86_64` client (Steam AppId 892970) was
running under the owner's desktop session. Per the task's joined-client safety
gate, no QA client was launched: an occupied client must be preserved and the
in-world proof deferred until the owner grants clearance. Server/engine-free work
(implementation, tests, both net48 Release builds, docs) proceeded and is
recorded below; the in-world artifact is the one remaining item and is captured
in a follow-up run once the desktop is free.

The engine-free vertical (below) is fully green and shipped; what remains is
exercising it on a joined client. **Logs-green is never playability** — this
node is not "done" until the in-world capture lands.

## Engine-free proof (shipped, green)

Provider: `src/SBPR.Niflheim.HomesteadStones/Adapters/Archer/PracticeRangeProvider.cs`.
Tests: `tests/NiflheimPracticeRangeTests.cs` (21 facts, all passing under the
net8 link-compiled suite; full suite 1216/1216).

### AT-PRACTICE-RANGE — capability = active Local Effect AND ordinary build Permission

- The Practice Range Local Effect's active/dormant status is re-derived through the
  shipped T014 `LocalEffectActivationView` (single Settlement Local policy +
  relationship/governance/level dormancy, no second ledger).
- The placement (exact vanilla Archery Target) and recipe (Practice Arrow)
  capabilities are the load-bearing AND of that active status and the occupant's
  ordinary build Permission (spec FR-016 final sentence). Proven: policy-eligible
  without build Permission → no capability; build-permitted but outside the policy
  → no capability; outside the Stone Area / missing authorized Governor / Archer
  Tree uncommitted / Active Stone Level below node level / undeveloped node → all
  dormant.

### AT-PRACTICE-ARROW-DAMAGE — 100 for 8 Wood, 0 ammo damage, bow damage retained

- Authored Practice Arrow recipe is exactly 100 arrows for 8 Wood
  (`PracticeRangeContent.PracticeArrowRecipe`).
- Practice Arrow ammo damage is `0`; the fired shot's effective damage equals the
  bow's own draw damage across sampled draw values (0, 22, 47.5) — the arrow adds
  and removes nothing from the weapon's output.

### AT-TARGET-RETURN — deterministic vanilla return, no roll

- A practice arrow terminally impacting the `ArcheryTarget` surface is returned
  exactly once, `TargetReturnWon = true`, `Deterministic = true`, stable across
  repeated evaluations (no RNG).
- Every other terminal surface (Ground/Water/Creature/LostOrExpired) returns
  nothing and does not win — yielding to the ordinary path and to the later
  Fletcher's Habit recovery roll (T027).

## Build / suite

- `dotnet test tests/SBPR.Trailborne.Tests.csproj -c Release` → 1216/1216 passed
  (+21 for this node).
- `dotnet build src/SBPR.Niflheim.HomesteadStones/...csproj -c Release` → 0w/0e.
- `dotnet build src/SBPR.Trailborne/...csproj -c Release` → 0w/0e.
- `python3 scripts/docs-lint.py` → OK. `git diff --check` → clean.

## Runtime seam (T025-RT — net48 engine binding, shipped)

The engine-free projection above is now backed by a real net48 runtime seam in
`src/SBPR.Niflheim.HomesteadStones/Features/Archer/`:

- `ArcherContent` / `ArcherContentAssets` — additively construct the Practice
  Arrow item (`ArrowPractice`, ADR-0006: `new GameObject` + AddComponent, never a
  vanilla-arrow clone), register it into ZNetScene + ObjectDB, add the recipe
  (exactly **100 for 8 Wood**, hand-craftable), append it to the vanilla
  `piece_ArcheryTarget`'s `ArcheryTarget.m_returnAmmo` list (deterministic single
  return — no roll), and add `piece_ArcheryTarget` to the Hammer build table.
- `ArcheryTargetPlacementGate` — a `Player.PlacePiece` prefix that refuses an
  Archery Target placement unless the Practice Range capability holds (active Local
  Effect AND ordinary build Permission, spec FR-016); resolves the server-observed
  facts (Stone Area membership, bound principal, active relationship) and **fails
  closed** where live state is not yet composed.
- `ArcherContentRegistrar` — ZNetScene.Awake / ObjectDB.Awake+CopyOtherDB hooks
  (Priority.Last, idempotent), wired into `Plugin.Awake`.

**0 ammo damage is data-driven, not a patch:** the Practice Arrow is an Ammo item
with a fresh all-zero `HitData.DamageTypes`, so vanilla `Attack.FireProjectileBurst`
(`hitData.m_damage.Add(m_weapon.GetDamage())` then `.Add(ammoItem.GetDamage())`)
adds nothing from the arrow while retaining the bow's own draw damage — exactly
`PracticeRangeProvider.ResolvePracticeArrowDamage`.

## Vanilla-binding note (CORRECTED)

The exact Archery Target build-piece prefab is **`piece_ArcheryTarget`** (capital A,
capital T) — verified against the running build's
`StreamingAssets/SoftRef/manifest_extended` and the decompiled `ArcheryTarget`
component (localization tokens `$piece_archerytarget_*`). The earlier const
`piece_archery_target` was WRONG and has been fixed in `PracticeRangeContent`. The
Practice Arrow item (`ArrowPractice`) is **new SBPR content** (vanilla arrows are
`ArrowWood`/`ArrowBronze`/… only), authored additively — not bound to a vanilla
prefab. Both are the single authored binding points (cf. `FoundationalPrefabMap`);
the joined-client capture confirms them against the running build in-world.
