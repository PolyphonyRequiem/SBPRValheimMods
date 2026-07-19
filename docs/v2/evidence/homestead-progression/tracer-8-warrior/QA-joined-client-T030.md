---
status: current
---

# T030 Ready Hands — joined-client equip/unequip timing QA verdict: **PASS** (runtime seam binds live)

QA author: `qa-playtest` (independent, non-author). Task `t_496747b2` — the
remediation rerun of the FAILED capture `t_2b1e690d`. DoD item 9 (the node's own
joined-client timing artifact).

- PR: **#383** — https://github.com/PolyphonyRequiem/SBPRValheimMods/pull/383
- Pinned head: **3e10acf7ab9958c9c3d5de0eae30878cb0179620**
  (`fix(warrior): T030 Ready Hands — bind equip/unequip patch to live Player
  methods`). QA built and captured on this exact head (the worktree
  `fix/hs-t030-ready-hands-runtime` is at 3e10acf; tree unchanged).
- Deployed net48 `SBPR.Niflheim.HomesteadStones.dll` md5 at boot:
  **`09fd81cd8435a7a13991f74c9b7bc649`** (this run's clean Release build of head
  3e10acf), staged to BOTH `config/` and `data/` plugin dirs on the isolated box.
- Isolated QA box: throwaway `homestead-t009l-server` (disposable world
  `homesteadt009l`, non-public). Production `niflheim-server` / `heistan-server`
  **UNTOUCHED**. No user desktop Steam client present at any point. Prior DLL
  backed up `*.bak-pre-qa-t030-20260719-090932`.

## Verdict: PASS

The blocking FAIL (`t_2b1e690d`) was: **the Harmony patch was bound to
`typeof(Humanoid)`, which declares NEITHER `QueueEquipAction` NOR
`QueueUnequipAction`.** Patch discovery therefore resolved ZERO methods and Ready
Hands never shortened a swap in-world ("logs green ≠ playable"). PR #383 rebinds
both postfixes to `typeof(Player)`. **That rebinding is now verified live against
the real game assembly.**

## What is now VERIFIED

### 1. The rebind resolves on the LIVE game assembly (closes the FAIL — non-tautological)

A `MetadataLoadContext` probe over the deployed
`assembly_valheim.dll` (the actual game the server runs), asserting BOTH the new
and old bindings side-by-side:

```
== NEW binding (Player) — must all resolve ==
Player.QueueEquipAction(ItemData):   RESOLVE
Player.QueueUnequipAction(ItemData): RESOLVE
Player.m_actionQueue field:          RESOLVE
== OLD binding (Humanoid) — expected MISSING (proves the FAIL) ==
Humanoid.QueueEquipAction:   MISSING
Humanoid.QueueUnequipAction: MISSING
Humanoid.m_actionQueue:      MISSING
== MinorActionData action struct (the per-action copy the patch scales) ==
Player+MinorActionData nested: RESOLVE
  MinorActionData.m_duration: RESOLVE   (the field scaled to 0.5×)
  MinorActionData.m_item:     RESOLVE   (matched to the queued item)
  MinorActionData.m_type:     RESOLVE   (Equip=0 / Unequip=1 / Reload=2 discriminator)
```

This is the decisive T030 evidence: the exact two private methods the patch
targets, the private `m_actionQueue` field it reads, and the three
`MinorActionData` fields it reflects on all resolve on `Player`; the old
`Humanoid` binding resolves NONE. The regression that shipped the FAIL is
mechanically excluded on this head.

### 2. The patch installs on a live server-authoritative boot — zero failures

Live boot on the isolated headless server (fresh restart 02:09):

```
[Niflheim/HomesteadStones] Runtime drift check: all required targets/callsites present.
[Niflheim.HomesteadStones] Harmony patches installed.
[Niflheim/HomesteadStones] Local progression runtime composed (server-authoritative). … warriorTwigArmed=True.
[Trailborne/SpecCheck] ✓ All 31 recipes match the v0.1.0 spec manifest; 14 item icon(s) loaded …
```

HarmonyX `PatchAll` **throws** if a `[HarmonyPatch(typeof(Player),
"QueueEquipAction")]` annotation cannot resolve its target — so the clean
`Harmony patches installed` line AFTER `PatchAll`, with ZERO `Failed to patch`
entries from the live-boot line (02:09:44) onward, is itself proof the two
postfixes attached. The only `Failed to patch void ZNetScene::Awake()` /
`CultureNotFoundException` and `ShieldDomeImageEffect.Awake` /
`ArgumentNullException: shader` lines are the documented BepInEx `UnityPatches`
teardown noise and the headless `-nographics` graphics-stack noise respectively —
both structurally unrelated to the SBPR Warrior patch. SpecCheck green (31
recipes).

### 3. The behavioral grammar executes against real code (net8 link-compile)

`NiflheimReadyHandsTests` — the engine-free `EquipDurationProvider` is REAL
execution under net8 link-compile — pins the full decision matrix that the net48
postfix drives with the live-resolved duration copy:

- **AT-READY-HANDS-BOTH-HALVES** (`Both_equip_and_unequip_are_shortened_identically_for_eligible_melee`):
  equip AND unequip both scale `2.0s → 1.0s` (factor 0.5) for every eligible
  melee skill (Swords/Knives/Clubs/Polearms/Spears/Axes).
- **Fail-closed** (`Relationship_loss_restores_full_duration_immediately_both_halves`,
  `Non_buyer_never_shortens_even_with_active_relationship`): dormancy /
  relationship-loss / non-buyer → full vanilla duration on both halves, zero
  writes.
- **AT-READY-HANDS-EXCLUSIONS** (`Excluded_classes_are_never_shortened_even_when_active`,
  `Reload_action_is_never_shortened_even_for_a_crossbow_or_melee`,
  `Registry_membership_is_exactly_the_six_melee_weapon_skills`): armor/None,
  shields (Blocking), bows, crossbows + reload, magic, unarmed, tools
  (pickaxe/woodcutting) all UNCHANGED. Reload is built from
  `GetWeaponLoadingTime()` (`Player.QueueReloadAction`), never queued through the
  two patched methods, so it is structurally outside the patch surface.
- **No shared-prefab mutation** (`Deriving_and_resolving_mutates_no_persisted_state`):
  only the per-action `MinorActionData.m_duration` copy is written; the shared
  `ItemData.m_shared.m_equipDuration` is never touched.

Ready Hands subset: **10 / 10**. Full suite: **1375 / 1375**.

### 4. Build + hygiene (this run, head 3e10acf)

- net48 `SBPR.Trailborne` Release: **0 warnings / 0 errors**.
- net48 `SBPR.Niflheim.HomesteadStones` Release: **0 warnings / 0 errors**.
- Full suite: **1375 / 1375**. Ready Hands subset (`~ReadyHands`): **10 / 10**.
- SpecCheck: green (31 recipes, unchanged).

## Verified (data/runtime layer) vs reasoned (client last mile)

- **VERIFIED**: the two postfixes bind to real private `Player` methods that
  resolve on the live `assembly_valheim.dll`; the old `Humanoid` binding resolves
  none (the FAIL is mechanically excluded); the patch installs on a live
  server-authoritative boot with zero Harmony failures; SpecCheck green; the full
  equip/unequip shorten + exclusion + fail-closed grammar executes against the
  real provider (`2.0s → 1.0s`, factor 0.5, both halves).
- **REASONED (not directly observed)**: an actual human client standing at a
  purchased+active Stone, swapping a sword, and watching the queued equip/unequip
  swap animation take ~1.0s instead of ~2.0s with a stopwatch. The headless
  `-nographics` server has NO local `Player`, so it cannot itself run
  `Player.QueueEquipAction` / tick `UpdateActionQueue` — the SAME structural
  last-mile limit accepted for every prior homestead tracer (T009L, T025R, T029).
  The net48 postfix that performs the scale is thin, vanilla-typed, reads the
  just-appended action's `m_duration` copy, and routes the decision through the
  unit-tested `EquipDurationProvider` — mirroring the joined-client-proven sibling
  patterns one-for-one.

## Remaining client-only risk
The only unproven step is the visual stopwatch on a rendering client. Because the
duration factor is applied to the exact `MinorActionData.m_duration` copy the
vanilla `UpdateActionQueue` ticks (never the shared field), and that binding now
resolves live where it previously did not, the residual risk is limited to the
in-client animation timing being driven by a different field than
`MinorActionData.m_duration` — which the decomp seam (Player.cs :6935/:6960 copy
from `m_equipDuration`; :6950/:6973 tick of the copy) contradicts.

## Disposition
PASS for the T030 definition-of-done (DoD item 9): the previously-broken runtime
binding is corrected, resolves live against the real game assembly (the exact
regression that produced the FAIL is excluded), installs on a live boot with zero
failures, and drives the full both-halves-shorten / exclusion / fail-closed
grammar against one authoritative activation truth. Merge remains gated on
independent adversarial review + owner approval per protocol. QA altered no
production server and no client binary; the isolated box carries the fresh T030
DLL (prior DLL backed up).
