---
status: current
---

# T016 Savor the Hearth — live food-timer seam wired (remediation t_803e92f6)

Remediation for the FAIL recorded in
[`joined-client-FAIL-t_0fb85725.md`](joined-client-FAIL-t_0fb85725.md): the
shipped `SavorTheHearthProvider` had zero production callers and no Harmony patch
on any food-timer seam, so a joined client could never observe factor 0.5. This
change wires the delivery seam. It is **code + tests + docs landed under review**;
the joined-client in-world 0.5/1.0 artifact is QA's to capture (qa-playtest,
T020/T016 gate) using the operator steps below. **Logs-green ≠ playable.**

## What was wired

Delivery seam (the exact missing hook the FAIL named):

- **`Player.UpdateFood(float dt, bool forceUpdate)` prefix** —
  `src/SBPR.Niflheim.HomesteadStones/Features/Cooking/SavorFoodTimerObserver.cs`
  (net48-only, not link-compiled). Vanilla accumulates `dt` into the private
  `m_foodUpdateTimer`; each 1s crossing subtracts a fixed `1f` from every active
  `Player.Food.m_time` (decomp `assembly_valheim` :17526). The prefix pre-adjusts
  `m_foodUpdateTimer` by `-dt*(1-factor)` for the **local player only**, so
  vanilla's own `m_foodUpdateTimer += dt` nets to `+dt*factor` for the food-drain
  slice. The separate `m_foodRegenTimer += dt` (healing) keeps the full `dt`, so
  **only food drain is slowed** — no stat/regen side effect. Stored `m_time` is
  never rewritten (no retroactive duration); when the factor returns to 1 the
  adjustment is 0, so exit/dormancy restores normal drain on the very next tick
  with zero carried state. `forceUpdate` ticks (dt=0) are never scaled.

- **`SavorFoodDrainResolver`** (engine-free, link-compiled + unit-tested) —
  `src/SBPR.Niflheim.HomesteadStones/Application/Runtime/SavorFoodDrainResolver.cs`.
  Owns every gameplay decision the prefix delegates: resolve the occupant's Stone
  Area from server-owned position, look up the established active Savor context,
  derive the T014 `LocalEffectActivationView` for the occupant at that Stone, and
  return the `SavorTheHearthProvider` factor (0.5 active / 1.0 otherwise). It
  reintroduces no state and no second ledger — each answer is a pure function of
  the current context + occupant facts.

Establishment seam (why the context exists at runtime):

- The live server composes only the Foundational AP slice, not the full
  Stone-progression command runtime, so nothing in a live session yet *develops*
  a Savor Local node. Rather than redesign that substrate for one node (out of
  scope), a bounded, **playtest-gated, admin-only** seam establishes the
  developed-Savor Stone context at the sender's current Stone Area — mirroring the
  T009R3/R4 `RelationshipProvisioningAdmin` pattern (config flag + Valheim-admin,
  transport-bound `ZRpc`, server-derived Stone identity, never client-authored).
  `src/SBPR.Niflheim.HomesteadStones/Features/Cooking/SavorProvisioningAdmin.cs`
  + config flag `[Cooking] EnableSavorPlaytestSeam` (default **false**). The
  established context is the exact developed-Savor shape the T014/T016 unit tests
  derive, so the live in-world factor matches the automated proof.
- The `SavorLocalContextIndex` is composed onto `FoundationalProgressionServer`
  (process-local, non-durable, republished by the seam on restart). It holds only
  the developed Stone context + governance fact; the ACTIVE/DORMANT status is
  still derived per food tick — never a stored active flag.

## Scope honestly stated

- **Listen host / singleplayer host**: the local player's food simulation runs
  where the authoritative server is composed, so the factor is derived and applied
  in-world. This is the path the joined-client proof exercises.
- **Pure dedicated CLIENT**: `FoundationalPlacementObserver.Server` is not composed
  on the client, so the observer no-ops and the factor is 1. Pushing the
  server-derived factor down to a dedicated client is deferred future work,
  exactly like the T009R2 dedicated-ingress split. Not claimed here.

## Automated proof (real code execution)

`tests/NiflheimSavorLiveSeamTests.cs` — 11 new tests, link-compiling the shipped
`SavorFoodDrainResolver` over the shipped `StoneAreaMembership`, the T014
`LocalEffectActivationView` derivation, and `SavorTheHearthProvider` (no mocks):

| claim | test |
|---|---|
| inside Area + active context → 0.5; slice scaled | `Inside_area_with_active_context_drains_at_half` |
| **AT-SAVOR-AREA-EXIT**: Area exit → 1.0 immediately, slices independent | `Stepping_outside_area_restores_full_factor_immediately` |
| no established context → 1.0 even inside | `No_established_context_is_full_factor_even_inside` |
| clearing context → 1.0 immediately | `Clearing_context_restores_full_factor_immediately` |
| context at a different Stone does not slow here | `Context_established_at_a_different_stone_does_not_slow_here` |
| Attuned policy, unrelated occupant → 1.0 | `Attuned_policy_unrelated_occupant_is_full_factor_inside` |
| Attuned policy, related occupant → 0.5 | `Attuned_policy_related_occupant_gains_slow_inside` |
| governance dormancy (no Governor) → 1.0 | `Governance_dormancy_restores_full_factor` |
| non-positive elapsed consumes nothing | `Non_positive_elapsed_consumes_nothing` |
| resolver stateless across interleaved evaluations | `Resolver_is_stateless_across_interleaved_evaluations` |
| empty membership → always 1.0 | `Empty_membership_is_always_full_factor` |

Build / suite (verified this run):

- `dotnet build src/SBPR.Niflheim.HomesteadStones -c Release`: **0 warnings, 0 errors**.
- `dotnet build src/SBPR.Trailborne -c Release`: **0 warnings, 0 errors**.
- Full suite: **1216 / 1216** (Savor engine-free subset 10/10 + live-seam subset 11/11).
- `python3 scripts/docs-lint.py`: **OK**.

## Joined-client operator steps (for QA — the outstanding in-world artifact)

1. Deploy the built `SBPR.Niflheim.HomesteadStones.dll` to a QA listen-host.
2. Set `[Cooking] EnableSavorPlaytestSeam = true` in
   `BepInEx/config/net.danielgreen.sbpr.niflheim.homesteadstones.cfg`; restart.
   Boot log prints the seam is available.
3. Join as a server admin, eat a food, stand inside a Homestead Stone Area, run
   the client console command `sbpr_savor on`. Observe the food status timer drain
   at ~half rate (screenshot/log with timestamps).
4. Run `sbpr_savor off` (or step outside the Area): drain returns to normal
   immediately, with the already-elapsed portion neither refunded nor clawed back.
5. Confirm no item swap, no stat write, no food-entry mutation across the
   transition. Teardown: set the flag back to false and restart; verify the
   console command is inert.

Until (1)–(5) are captured this remediation is code+tests+docs landed under
review, NOT gate sign-off. Independent Tracer-5 acceptance remains T020.
