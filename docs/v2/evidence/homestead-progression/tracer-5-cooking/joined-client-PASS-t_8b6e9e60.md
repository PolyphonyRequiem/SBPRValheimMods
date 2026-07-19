---
status: current
---

# T016 Savor the Hearth — joined-client / data-layer proof (PR #364, merge head)

- PR: https://github.com/PolyphonyRequiem/SBPRValheimMods/pull/364
- Branch: `feat/hs-t016-savor-hearth`
- Pinned implementation head verified: `69797e22710f95f0425143fcca96a78c721ccb98`
- Final head after mechanical merge of fresh `origin/main`: `1019c925e071f274934b454f934022907fff080c`
- Base merged: `origin/main@0a28b4833a48c50a3fc0a80db870b9b5cb817dcc`
  (includes accepted T025R PR #370 and T021 PR #369)
- QA profile: qa-playtest · isolated throwaway server `homestead-t009l`
  (container `homestead-t009l-server`, NON-public, disposable world
  `homesteadt009l`) — production Niflheim/Heistan untouched.
- Safety: pre-deploy check for user-owned Steam AppId 892970 / `valheim.x86_64`
  found NONE (only dedicated `valheim_server.x86_64` processes, none stopped or
  touched). No user session altered.
- Fresh net48 DLL md5 (merge head `1019c92`): `82b271e142955480bf803608723a9a2b`.
- Client files restored byte-for-byte after capture: DLL back to pre-QA md5
  `6890a68bf3fbd345dc45b6f66a1aba29`; QADiag instrument removed from both config
  and data plugin dirs; final clean boot has **0 QADiag lines**.

## Verdict: PASS (data + delivery-seam layer verified) — in-world 0.5x food-bar last mile REASONED, not observed

The prior T016 gate (`t_0fb85725`, FAIL) failed for ONE concrete reason: the
shipped `SavorTheHearthProvider` had **zero production callers and no Harmony
patch on any food-timer seam**, so a joined client could never observe factor
0.5. That exact condition is now remediated and verified on the live booted
server process at this head.

## What was VERIFIED

### Build + suite (this run, at merge head `1019c92`)
- net48 `SBPR.Niflheim.HomesteadStones` Release: **0 warnings / 0 errors**.
- net48 `SBPR.Trailborne` Release: **0 warnings / 0 errors**.
- Full test suite: **1288 / 1288 passed** (post-merge; picks up the merged
  T021/T025 tests alongside the T016 live-seam suite).
- Savor subset (`FullyQualifiedName~Savor`): **17 / 17**.
- `python3 scripts/docs-lint.py`: **OK** (192 docs). No merge conflict markers
  remain anywhere in the tree.

### Mechanical rebase/merge onto fresh main
PR #364 conflicted against current main in exactly two files, both mechanical
registration-list / test-include collisions resolved by KEEPING BOTH sides:
- `src/SBPR.Niflheim.HomesteadStones/Plugin.cs` — the `harmony.PatchAll(...)`
  block: main's substrate/T021/T025 registrations + our T016 Savor
  (`SavorFoodTimerObserver`, `SavorProvisioningAdmin`, `SavorProvisioningConsole`)
  registrations now coexist.
- `tests/SBPR.Trailborne.Tests.csproj` — the `<Compile Include>` list: main's
  PracticeRange/EffectiveStationLevel providers + our `SavorFoodDrainResolver`
  now coexist.
Merge commit `1019c92`. Both builds + full suite re-run green post-merge.

### Live delivery-seam presence (the exact prior-FAIL condition) — VERIFIED at runtime
A throwaway read-only BepInEx probe (`SBPR.QADiag.T016`, own GUID
`net.danielgreen.sbpr.qadiag.t016`, clean-side: public API + reflection only)
inspected `Harmony.GetPatchInfo(Player.UpdateFood(float,bool))` on the live boot
POST-SBPR-patch. Captured from `docker logs homestead-t009l-server`:

```
[QADiag] --- Player.UpdateFood(float,bool) patch info ---
[QADiag]   all owners=[net.danielgreen.sbpr.niflheim.homesteadstones]
[QADiag]   prefixes=[net.danielgreen.sbpr.niflheim.homesteadstones::SavorFoodTimerObserver.OnUpdateFood]
[QADiag]   EXPECT SBPR plugin owns a patch on UpdateFood -> PASS
[QADiag]   EXPECT SBPR owner is a PREFIX (drain-slice scaler) -> PASS
[QADiag] --- Terminal console commands ---
[QADiag]   Terminal.commands count=143 'sbpr_savor' present=True
[QADiag]   sbpr_savor registered on this process -> PASS (present)
```

This is decisive: the food-timer seam the FAIL named as absent is now an
**installed Harmony prefix** on `Player.UpdateFood`, owned by the SBPR
HomesteadStones plugin, and the playtest establishment console command
`sbpr_savor` is registered. The seam is wired, not an orphan provider.

- Zero Harmony patch failures and zero SBPR/HomesteadStones exceptions on the
  live boot (Awake at 23:33:15 onward). The `BadImageFormatException` /
  `CultureNotFoundException` lines at 23:33:11 are the PREVIOUS process's
  shutdown-teardown noise (BepInEx `UnityPatches`), pre-dating this boot. The
  `ShieldDomeImageEffect.Awake` shader NRE and the Trailborne `seers_stone`
  icon warning are known vanilla `-nographics` / Trailborne deploy artifacts,
  unrelated to Savor.

### Authoritative resolver logic — VERIFIED by code + link-compiled tests
`tests/NiflheimSavorLiveSeamTests.cs` drives the shipped engine-free
`SavorFoodDrainResolver` over authoritative `LocalActivationSnapshot`s produced
by the merged shared `LocalActivationService`, over a Stone whose Savor node was
developed through the shared `LocalNodeProvisioningDriver`'s accepted commands
(no family-local ledger, no fabricated activation). Proven claims:
- inside Area + active derived effect → factor 0.5; only the elapsed slice scaled
  (`Inside_area_with_active_effect_drains_at_half`).
- **AT-SAVOR-AREA-EXIT**: Area exit → factor 1.0 immediately; slices independent
  (`Stepping_outside_area_restores_full_factor_immediately`).
- policy loss (Attuned, unrelated occupant) → 1.0
  (`Attuned_policy_unrelated_occupant_is_full_factor_inside`).
- governance dormancy (no Governor) → 1.0
  (`Governance_dormancy_restores_full_factor`).
- denied/null snapshot → 1.0 fail-closed (`Denied_or_null_snapshot_is_full_factor`).
- non-positive elapsed consumes nothing; resolver stateless across interleaved
  evaluations.

The prefix (`SavorFoodTimerObserver.OnUpdateFood`) pre-adjusts the private
`m_foodUpdateTimer` by `-dt*(1-factor)` for the LOCAL player only, so vanilla's
own `m_foodUpdateTimer += dt` nets to `+dt*factor` on the food-drain slice; the
separate `m_foodRegenTimer += dt` keeps full dt (healing untouched), stored
`m_time` is never rewritten (no retroactive duration / authored-duration change),
and `forceUpdate`/`dt<=0` ticks are never scaled. Food item, stats, authored
duration, and healing timer therefore remain unchanged by construction.

## What is REASONED, not observed (the honest last mile)

`logs-green ≠ playable`. The dedicated server runs `-nographics -batchmode` with
NO local `Player`, so `Player.UpdateFood` never executes in-world here — the
actual food-status-bar draining at ~half rate inside a Stone Area, and returning
to full rate on Area exit / policy switch / governance dormancy, is a
**client-only last mile** that cannot be captured on a headless box. It is
REASONED from: (a) the installed prefix on the correct vanilla method, verified
live above; (b) the engine-free resolver's factor decisions, verified by the
link-compiled tests above; (c) the merged shared substrate's own suite proving
the authoritative snapshot path. This is the same accepted data-layer frame as
the T025R and T009L proofs.

Remaining client-only risk: a joined listen-host must actually exercise the
`sbpr_savor` establishment seam and eat a food inside the Area to visually
confirm the ~half drain and the clean exit restore. Operator steps are in
`live-seam-wired-t_803e92f6.md` §"Joined-client operator steps".

Scope honestly restated (unchanged from the impl doc): the factor is derived and
applied on the listen-host / singleplayer-host path where both the Foundational
and Local runtimes are composed locally; pushing the server-derived factor down
to a pure dedicated client is deferred (mirrors the T009R2 ingress split), not
claimed here.
