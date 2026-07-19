---
status: current
---

# T017 Field Prep — joined-client / live-seam proof (Cooking node 2 of 4)

- Task: `t_4554b814` — T017 [US4] Implement Field Prep through the shared
  Cooking-aware Bushcraft policy. Acceptance: `AT-FIELD-PREP-COOKING-POLICY`.
- Branch: `feat/hs-t017-field-prep` (off `origin/main@a9d9990`, which already
  contains the merged T016 Cooking adapter surface `d5a6792`).
- QA profile: isolated throwaway server `homestead-t009l` (container
  `homestead-t009l-server`, NON-public, disposable world `homesteadt009l`) — the
  same isolated box T016/T025R used. Production Niflheim/Heistan untouched.
- Safety: pre-deploy check for a user-owned graphical `valheim.x86_64` found
  NONE (only the persistent dedicated `valheim_server.x86_64` infra + a Steam
  desktop with no game running). No user session altered.
- Fresh net48 DLL md5 (this run, HomesteadStones): `d5c3ee4e19b42fd8ac2dc1bf61edb23f`.
- Throwaway server restored byte-for-byte after capture: both the container
  runtime plugin dir and the `/config` mount put back to the pre-QA
  HomesteadStones dll md5 `edaeed67478dc65f2ba1848d30903cad`; the QADiag.T017
  instrument removed from both; final clean boot loads **2 plugins** and shows
  **0 QADiag-T017 lines**.

## Verdict: PASS (data + delivery-seam layer verified) — in-world station-free craft last mile REASONED, not observed

Field Prep is a personal Character Effect that, while active, EXPOSES the
unchanged vanilla Boar Jerky (`BoarJerky`) and Queen's Jam (`QueensJam`) recipes
through Bushcraft — i.e. makes those existing vanilla recipes craftable WITHOUT
their ordinary Cooking station — while preserving the recipes' inputs, yield,
authority, and the normal Cooking XP/craft-speed/bonus-output mechanics. This
run verifies the two layers a headless box can decisively prove, and states
honestly which last mile is client-only.

## What was VERIFIED

### Build + suite (this run)
- net48 `SBPR.Niflheim.HomesteadStones` Release: **0 warnings / 0 errors**.
- net48 `SBPR.Trailborne` Release: **0 warnings / 0 errors**.
- Full test suite: **1338 / 1338 passed** (baseline 1328 + 10 new Field Prep
  adapter tests).
- Field Prep subset (`tests/NiflheimFieldPrepTests.cs`): **10 / 10**, authored
  red-first (verified by removing `CookingCraftPolicy.cs` and observing the
  CS2001 / type-missing compile failure, then restoring to green).
- `python3 scripts/docs-lint.py`: **OK**. `git diff --check`: clean.

### Live delivery-seam presence — VERIFIED at runtime on the booted server
A throwaway read-only BepInEx probe (`SBPR.QADiag.T017`, own GUID
`net.danielgreen.sbpr.qadiag.t017`, clean-side: public API + Harmony reflection
only) inspected `Harmony.GetPatchInfo(Player.RequiredCraftingStation)` on the
live boot POST-SBPR-patch, at `ZNetScene.Awake`. Captured from
`docker logs homestead-t009l-server` (full excerpt in
`capture/t017-boot-capture.log`):

```
[QADiag-T017] --- Player.RequiredCraftingStation patch info ---
[QADiag-T017]   all owners=[net.danielgreen.sbpr.niflheim.homesteadstones]
[QADiag-T017]   SBPR postfixes=[RefinedWorkshopStationLevelPatch.RequiredCraftingStation_Postfix,FieldPrepRecipeGate.RequiredCraftingStation_Postfix]
[QADiag-T017]   EXPECT SBPR plugin owns a patch on RequiredCraftingStation -> PASS
[QADiag-T017]   EXPECT SBPR owner is a POSTFIX (station-gate rescuer) -> PASS
[QADiag-T017]   EXPECT the postfix is FieldPrepRecipeGate -> PASS
```

This is decisive: the station-gate seam Field Prep depends on is an **installed
Harmony postfix** on `Player.RequiredCraftingStation`, owned by the SBPR
HomesteadStones plugin, coexisting cleanly with the T021 Refined Workshop postfix
on the same method (both are additive station-gate rescuers; neither turns a
vanilla PASS into a fail). The seam is wired, not an orphan provider.

- Zero Harmony patch failures and zero SBPR/HomesteadStones exceptions on the
  live boot; `[Niflheim.HomesteadStones] Harmony patches installed` logged, world
  `homesteadt009l` loaded, `Game server connected`.

### Authoritative policy logic — VERIFIED by code + link-compiled tests
`tests/NiflheimFieldPrepTests.cs` drives the shipped engine-free
`CookingCraftPolicy` over authoritative aggregates (Stone development + character
purchase + (account, Stone) authority index) through the shipped T004
`DerivedActivationView`. Proven claims:
- active Field Prep (purchase record AND active relationship) exposes EXACTLY the
  two unchanged recipes Boar Jerky + Queen's Jam, each station-free with
  `PreservesVanillaInputsYieldAuthority` and `PreservesNormalCookingXpSpeedBonus`
  true (`ActiveEffect_ExposesUnchangedBoarJerkyAndQueensJamThroughBushcraft`);
- purchased-but-no-relationship → dormant, exposes nothing
  (`PurchasedButNoRelationship_EffectDormant_ExposesNothing`);
- relationship-but-no-purchase → nothing
  (`RelationshipButNoPurchase_ExposesNothing`);
- undeveloped node even with purchase + relationship → nothing
  (`UndevelopedNode_EvenWithPurchaseAndRelationship_ExposesNothing`);
- a sibling character's active reservation never leaks exposure to the purchased
  caller (`SiblingCharacterActive_DoesNotLeakExposureToUnpurchasedCaller`);
- relationship loss→restore flips exposure with zero writes (pure re-derivation)
  (`RelationshipLossThenRestore_FlipsExposureWithNoWrites`);
- exposes ONLY the two Field Prep recipes, never Savor / Wood Arrow / arbitrary
  items (`ExposesOnlyFieldPrepRecipes_NotSavorOrOtherItems`);
- static content + dormant per-item + inert None coverage.

## What is REASONED, not observed (the honest last mile)

`logs-green ≠ playable`. The dedicated server runs `-nographics -batchmode` with
NO local `Player`, so `Player.RequiredCraftingStation` never executes in-world
here — the actual Boar Jerky / Queen's Jam entries appearing in a joined client's
crafting list WITHOUT a Cooking station while Field Prep is active (and
disappearing on relationship loss), and the ordinary Cooking XP/speed/bonus
running unchanged on that craft, are a **client-only last mile** that cannot be
captured on a headless box. It is REASONED from: (a) the installed postfix on the
correct vanilla method, verified live above; (b) the engine-free policy's
exposure decisions, verified by the link-compiled tests above; (c) the identical,
already-accepted structural frame proven for the sibling personal-recipe seams
(Field Fletching / Refined Workshop).

Scope honestly restated (unchanged from the impl doc): the exposure verdict is
resolved on the authoritative HOST path (listen-server / singleplayer host) where
the composed server stores exist; `FieldPrepRecipeGate` fails closed on a pure
dedicated client (no personal-effect snapshot channel yet), off-host, outside
every Stone Area, or without an active purchase. A personal Character-Effect
client delivery channel is deferred follow-up, exactly as the sibling seams
documented. Independent Tracer-5 acceptance is T020.
