---
status: current
---

# T025R Practice Range — joined-client / data-layer proof (PR #370 @ 22600ff)

- PR: https://github.com/PolyphonyRequiem/SBPRValheimMods/pull/370
- Branch: `fix/hs-t025-practice-runtime-r2`
- Exact head: `22600ffe46bffc35350bd42211a556f1817de6f7`
- Base: `origin/main@fbea39c`
- QA profile: qa-playtest · isolated throwaway server `homestead-t009l`
  (ports 2476-2478, non-public, disposable world `homesteadt009l`) — production
  Niflheim/Heistan untouched.
- Fresh net48 DLL md5 `6890a68bf3fbd345dc45b6f66a1aba29`.

## Verdict: PASS (data layer verified) — one client-only last mile REASONED, not observed

The prior T025 gate (`t_ea8270e0`) FAILed for two concrete reasons, both fixed at
this head and re-verified here:

1. **"No net48 Practice Range runtime seam."** FIXED. `Plugin.Awake` now
   `PatchAll`s both `ArcherContentRegistrar` (ZNetScene/ObjectDB wiring) and
   `ArcheryTargetPlacementGate` (a live `Player.PlacePiece` **prefix**). The gate
   is wired, not an orphan provider.
2. **"Wrong Archery Target prefab id."** FIXED. Content const is now
   `piece_ArcheryTarget` (capital A/T), confirmed a real vanilla prefab (present
   in the game's `manifest_extended` SoftRef) and resolved live from ZNetScene at
   boot (QADiag observed it with an attached `ArcheryTarget` component + a 21-entry
   vanilla `m_returnAmmo`).

## What was VERIFIED

### Build + suite (this run, at 22600ff)
- net48 `SBPR.Niflheim.HomesteadStones` Release: **0 warnings / 0 errors**.
- Full test suite: **1242 / 1242 passed** (net8 link-compile = real execution of
  the engine-free projection the gate consumes).
- Practice Range subset (`FullyQualifiedName~PracticeRange`): **29 / 29** —
  16 `NiflheimPracticeRangeTests` + 8 `NiflheimPracticeRangeGateRuntimeTests` +
  siblings.

### Authoritative gate path (task item 4) — VERIFIED by code + runtime tests
`NiflheimPracticeRangeGateRuntimeTests` drives the exact projection the net48 gate
consumes, through the real shared activation runtime (PR #368):
- **HOST path** — `LocalActivationService.Fetch(stone, presence)` then the FR-016
  AND `snapshot.CanExercisePlacement(PracticeRangeNode, hasBuildPermission)`.
  Proven: active effect + build permission → grant; build permission alone (effect
  active but `hasOrdinaryBuildPermission:false`) → refuse; outside Stone Area →
  dormant → refuse; missing authorized Governor → dormant → refuse.
- **PURE CLIENT path** — `LocalActivationClientCache.CanExercisePlacementForNode`
  reads ONLY the server-delivered snapshot ANDed with build permission. Proven:
  delivered active snapshot + permission → grant; no snapshot (never delivered /
  relog / area exit) → fail closed; denied snapshot → fail closed; delivered
  **dormant** snapshot → refuse even with permission; another occupant's hostile
  snapshot never serves. This is exactly the "remote client driven by the
  server-delivered snapshot" requirement — the gate does NOT re-derive activation
  and holds no provisional relationship-only ledger.

The gate reads `LocalProgressionObserver.Server` (host Fetch) vs
`.ClientCache` (pure client snapshot) — `ArcheryTargetPlacementGate.cs:86-93`.

### Registration data layer — OBSERVED on the isolated headless server
Read-only throwaway BepInEx probe `SBPR.QADiag.T025R` (own GUID, removed after
capture; final boot confirmed **0** QADiag lines). Dumped POST-SBPR-wiring:

```
[QADiag] --- ArrowPractice item ---
  present=YES name='Practice Arrow' itemType=Ammo ammoType='$ammo_arrows' maxStack=100
  m_damages totalSum=0  nonZero=[]
  EXPECT itemType=Ammo -> PASS
  EXPECT 0 ammo damage (damage sum==0) -> PASS
[QADiag] --- ArrowPractice recipe ---
  amount=100 station=<null/hand> minStationLvl=1 resources=[Woodx8]
  EXPECT amount=100 -> PASS
  EXPECT 8 Wood only -> PASS
  EXPECT hand-craftable (no station) -> PASS
  recipe count for ArrowPractice: 1 -> EXPECT exactly 1 -> PASS
[QADiag] --- Hammer build table ---
  piece_ArcheryTarget occurrences=1 (of 313 pieces)
  EXPECT present exactly once -> PASS
[QADiag] --- piece_ArcheryTarget ArcheryTarget.m_returnAmmo ---
  entries=22 containsArrowPractice=True occurrences=1
  EXPECT ArrowPractice present exactly once -> PASS
```

Maps to the named acceptance:
- **AT-PRACTICE-ARROW-DAMAGE** — `ArrowPractice` is a real Ammo item nockable on
  vanilla bows (`$ammo_arrows`), `m_damages` sums to **0** → the fired shot carries
  the bow's own draw damage with 0 ammo contribution (data-driven, no patch).
  Recipe is **exactly 100 for 8 Wood, hand-craftable** (no station). VERIFIED.
- **AT-TARGET-RETURN** — `ArrowPractice` appended to the vanilla
  `piece_ArcheryTarget` `ArcheryTarget.m_returnAmmo` **exactly once** (22 = 21
  vanilla + 1). Vanilla `ArcheryTarget.DropArrows()` returns entries in that list
  with no roll → deterministic single return on the target; every other terminal
  surface returns nothing (yields to the later Fletcher's Habit roll, T027).
  VERIFIED at the data layer.
- **AT-PRACTICE-RANGE (buildable)** — `piece_ArcheryTarget` present exactly once
  in the Hammer PieceTable → the piece is buildable at all; the per-attempt
  capability AND is enforced by the wired gate prefix. VERIFIED.

Boot cleanliness: zero SBPR/Archer exceptions on the live boot. (The
`ShieldDomeImageEffect.Awake` `ArgumentNullException: shader` and the
SeersStone/Sunstone/tent icon+graft warnings are pre-existing vanilla `-nographics`
headless noise and stale-Trailborne-icon deploy artifacts on the throwaway box —
NOT T025R and NOT this DLL's code.) SpecCheck green (31 recipes).

## What is REASONED, not observed (honest last mile — "logs-green ≠ playable")

The dedicated server runs `-nographics` with **no local `Player`**, so the actual
`Player.PlacePiece` build attempt — and therefore the live gate prefix firing and
its `$msg_invalidplacement` refusal UX — cannot be observed headless. What is
proven: (a) the gate is installed on `Player.PlacePiece`; (b) the exact
authoritative projection it calls (host Fetch + FR-016 AND / client snapshot
consumer) returns the correct allow/deny across all activation/permission
combinations via the runtime tests; (c) the piece, item, recipe, and return wiring
the gate depends on are all live and correct in the server-authoritative data
layer. The remaining client-only last mile — a human on a joined GUI client seeing
the target place inside an active Homestead, get refused with `$msg_invalidplacement`
outside a ward, and get refused when the Local Effect is dormant — is REASONED from
(a)+(b)+(c) and left for a GUI-client smoke by the owner (safety gate forbids
launching a user-owned client here).

## Spec/docs concordance
Recipe (100/8 Wood), 0 ammo damage, deterministic single target return, and the
`piece_ArcheryTarget` id all match `PracticeRangeProvider.cs` constants and the
contracts update in this same commit. No spec drift introduced by this QA.
