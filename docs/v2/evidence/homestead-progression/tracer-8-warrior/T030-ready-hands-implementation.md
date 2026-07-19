---
status: current
---

# T030 — Warrior Ready Hands implementation evidence

Author: `engineer-systems` (implementer). This is the node's own implementation
evidence (DoD items 1–8, 10 pre-merge). The independent joined-client in-world
timing capture (DoD item 9) and the Tracer-8 gate verdict are QA / T032
(non-author) and remain gated on client availability.

Acceptance owned here: `AT-READY-HANDS-BOTH-HALVES`, `AT-READY-HANDS-EXCLUSIONS`.

## What Ready Hands is (spec-grounded)

Ready Hands is the Warrior Tree, Level-1 **personal Character Effect**
(`ReadyHands@1`; data-model.md roster). Per spec §"Warrior" it "shortens both
queued equip and unequip durations for eligible melee weapons only while active",
and per contracts.md §Warrior the `EquipDurationProvider` "modifies copied queued
equip and unequip durations for authored eligible melee weapons only; no shared
prefab mutation". It registers **no** SBPR recipe or buildable, so the
`SpecCheck.cs` recipe manifest is unchanged.

## The vanilla seam (decomp — vanilla is fair game, AGENTS.md / ADR-0001)

Weapon switching is a queued minor action. `Player.ToggleEquipped`
(`Player.cs` decomp :6785 — a `protected override` of the `Humanoid` virtual)
routes an equipable with a positive `m_equipDuration` to the PRIVATE
`Player.QueueEquipAction` (:6935) or `Player.QueueUnequipAction`
(:6960). Each builds a fresh `MinorActionData` whose `m_duration` is **COPIED**
off `item.m_shared.m_equipDuration` (:6950 equip, :6973 unequip) and appended
to the private `Player.m_actionQueue`. `Player.UpdateActionQueue` ticks that
per-action copy and, on completion, performs the real `EquipItem`/`UnequipItem`.
Reload is a **third** `MinorActionData` type built from
`GetWeaponLoadingTime()` (`Player.QueueReloadAction` :6980), never from `m_equipDuration`.

> **T030 remediation note.** These queue methods and `m_actionQueue` are declared
> on **`Player`**, not on the `Humanoid` base. The accepted PR #376 bound the
> Harmony patch to `typeof(Humanoid)`, so patch discovery attached to **zero**
> methods and Ready Hands never shortened a swap on a joined client (reproduced by
> QA `t_2b1e690d`). The binding is now `typeof(Player)`, verified against
> `assembly_valheim.dll` metadata, and a CI reflection guard fails the build if
> either method (or `m_actionQueue`) stops resolving on `Player`.

Because the queued `m_duration` is a throwaway per-action copy, scaling it
shortens the action **without ever writing the shared prefab** — the exact
"no shared-prefab mutation" the card and contract demand.

## Eligible melee registry (data-defined)

Mirrors `Skills.SkillType` (decomp :23820). Eligible = the six melee **weapon**
skill classes: `Swords`, `Knives`, `Clubs`, `Polearms`, `Spears`, `Axes`.
Excluded: `Bows`/`Crossbows` (ranged; Crossbows also carry reload), `Blocking`
(shields), `Pickaxes`/`WoodCutting` (tools), `ElementalMagic`/`BloodMagic`
(staves), `Unarmed` (no equippable weapon item), and armor (weapon-skill `None`).
An unknown/future `SkillType` maps to `None` (never eligible) — the correct
fail-safe.

## What landed

- `src/SBPR.Niflheim.HomesteadStones/Adapters/Warrior/EquipDurationProvider.cs`
  — the pure, engine-free provider. Reads the shipped T004
  `DerivedActivationView` Active bit for `ReadyHands@1` (purchased AND active
  relationship; no second ledger — `AT-NO-ACTIVE-LEDGER`) and resolves the queued
  action's duration: `base × 0.5` when active + eligible-melee + equip/unequip,
  else the full `base`. The `0.5` factor is a provisional playtest value (mirrors
  the Savor precedent) and the sole tuning knob; final balance is deferred.
- `src/SBPR.Niflheim.HomesteadStones/Features/Warrior/ReadyHandsEquipDurationPatch.cs`
  — the net48 runtime seam. Postfixes the private `Player.QueueEquipAction` /
  `Player.QueueUnequipAction`, maps the equipped item's `m_shared.m_skillType` to the
  engine-free `WeaponSkillClass`, resolves the Ready Hands active bit
  authoritatively (fail-closed), and scales ONLY the just-appended
  `MinorActionData.m_duration` copy. Local player only; skips instant toggles
  (`m_equipDuration <= 0`) and any re-queued/cancelled action (vanilla removes it,
  leaving nothing to scale).

## Activation source (fail closed — mirrors FieldFletchingRecipeGate)

- **HOST:** derive the acting occupant's purchase + active relationship straight
  from the composed `LocalProgressionServer` stores (Stone + character +
  authority), through the shipped `DerivedActivationView`.
- **PURE CLIENT:** read ONLY the server-stamped personal read model in
  `LocalProgressionObserver.PersonalClientCache` (delivered over the T026
  `PersonalActivationDeliveryObserver` transport), refetched on a bounded interval
  for the Stone the local player stands in. No active snapshot ⇒ full vanilla
  duration. The client never authors entitlement.

## Tests (red-first, then green)

`tests/NiflheimReadyHandsTests.cs` — 10 named facts across both acceptance ids:

- **AT-READY-HANDS-BOTH-HALVES:** active bit derived from purchase+relationship;
  both equip AND unequip shortened identically (`2.0s → 1.0s`) for every eligible
  melee skill; relationship loss restores the full `2.0s` immediately on both
  halves; a non-buyer never shortens even with an active relationship; stateless
  across interleaved active/dormant evaluations.
- **AT-READY-HANDS-EXCLUSIONS:** registry membership is exactly the six melee
  weapon skills; every excluded class (armor/None, shields, bows, crossbows,
  magic, unarmed, tools) is untouched even when active; the Reload action is never
  shortened (crossbow or melee); a zero-base instant toggle is returned unchanged;
  resolving mutates no persisted state (character aggregate byte-identical — no
  ledger to poke).

## Gate results (this remediation, pre-merge)

- Full suite: **1375/1375** passing.
- net48 Release builds: **0 warnings / 0 errors** (HomesteadStones and Trailborne).
- `python3 scripts/docs-lint.py`: **OK (207 docs)**.
- `git diff --check`: clean.
- `SpecCheck.cs` recipe manifest: **unchanged** (Ready Hands registers no recipe).
- CI reflection guard (`.github/workflows/ci.yml`) now asserts
  `Player.QueueEquipAction(ItemDrop.ItemData)`, `Player.QueueUnequipAction(ItemDrop.ItemData)`,
  and `Player.m_actionQueue` resolve on the live `assembly_valheim.dll`. Verified
  locally the corrected `Player` binding resolves all three while the old
  `Humanoid` binding resolves zero — the regression that shipped in PR #376.

## Honesty — logs-green ≠ playable

The above proves the pure delivery grammar and that the runtime seam compiles and
targets the correct copied-duration hook. It does **not** prove a joined Valheim
client observes the shortened equip/unequip timer in-world. That is DoD item 9 —
the independent joined-client timing capture, produced by qa-playtest and recorded
alongside this file, and gated on client availability. Tracer-8 sign-off is T032
(non-author).
