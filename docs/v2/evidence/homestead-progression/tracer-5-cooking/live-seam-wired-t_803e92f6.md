---
status: current
---

# T016 Savor the Hearth — live food-timer seam wired (remediation t_803e92f6)

Remediation for the FAIL recorded in
[`joined-client-FAIL-t_0fb85725.md`](joined-client-FAIL-t_0fb85725.md): the
shipped `SavorTheHearthProvider` had zero production callers and no Harmony patch
on any food-timer seam, so a joined client could never observe factor 0.5. This
change wires the delivery seam **on top of the merged shared Local Effect runtime
substrate** (PR #368, `LocalActivationService` / `LocalProgressionServer` /
`LocalNodeProvisioningDriver` / `LocalActivationClientCache`). It is **code +
tests + docs landed under review**; the joined-client in-world 0.5/1.0 artifact
is QA's to capture (qa-playtest, T020/T016 gate) using the operator steps below.
**Logs-green ≠ playable.**

## Design after the rebase — consume the authoritative substrate, no parallel state

The earlier remediation head carried a **family-local activation ledger**
(`SavorLocalContextIndex` + a `SavorContextFactory` that fabricated a
developed-Savor Stone) plus a `SavorContexts` field on
`FoundationalProgressionServer`. That provisional state is **deleted**. The
reviewed shared substrate is now the single authority that *derives* whether Savor
is active for an occupant from the real Stone aggregate + committed
relationship/governance + Settlement policy + server-observed occupancy. The food
seam **consumes** that authoritative read model; it never derives, stores, or
fabricates activation itself.

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
- The prefix resolves the local player's **bound internal principal** (the same
  identity space placement authorizes under), its server-owned world position →
  Stone Area occupancy, and its committed relationship activity, then calls the
  composed `LocalActivationService.Fetch(...)` (a read — it never bumps the
  delivery sequence) to obtain the authoritative per-occupant
  `LocalActivationSnapshot`.

- **`SavorFoodDrainResolver`** (engine-free, link-compiled + unit-tested) —
  `src/SBPR.Niflheim.HomesteadStones/Application/Runtime/SavorFoodDrainResolver.cs`.
  Reduced to a pure **projection**: read the already-derived Savor active-state
  off the `LocalActivationSnapshot` and translate it to the vanilla food-timer
  drain factor via `SavorTheHearthProvider` (0.5 active / 1.0 otherwise). A null
  or authority-absent snapshot → factor 1 (fail closed). No state, no second
  ledger; only the elapsed slice handed in is scaled.

Establishment seam (why a developed Savor node exists at runtime, for QA):

- The live server composes the Foundational AP slice **and** (via PR #368) the
  Local progression runtime, but a live session still needs a *developed* Savor
  node to observe. Rather than fabricate one, a bounded, **playtest-gated,
  admin-only** seam drives the shared **`LocalNodeProvisioningDriver`** through
  the *accepted, receipt-backed commands* (commit Cooking Tree → credit BP →
  develop Savor) so the node reaches Developed as real Stone-owned state — mirroring
  the T009R3/R4 `RelationshipProvisioningAdmin` pattern (config flag +
  Valheim-admin, transport-bound `ZRpc`, server-derived Stone identity, never
  client-authored).
  `src/SBPR.Niflheim.HomesteadStones/Features/Cooking/SavorProvisioningAdmin.cs`
  + config flag `[Cooking] EnableSavorPlaytestSeam` (default **false**). The
  acting Governor's Bond must already exist (establish it first with the shipped
  `sbpr_provision bond` seam). `sbpr_savor on` develops the node and sets the
  Everyone policy; `sbpr_savor off` switches the policy to Attuned via the same
  accepted owner-only handler (an in-place exit proof for an unrelated occupant).
  The ACTIVE/DORMANT status is still derived per food tick — never a stored flag.

## Scope honestly stated

- **Listen host / singleplayer host**: the local player's food simulation runs
  where both the Foundational and Local progression runtimes are composed, so the
  factor is derived from authoritative state and applied in-world. This is the
  path the joined-client proof exercises.
- **Pure dedicated CLIENT**: the server runtimes are not composed on the client,
  so the seam reads no authoritative snapshot and the factor is 1. Pushing the
  server-derived factor down to a dedicated client (via the shared server→client
  delivery channel + the client cache) is deferred future work, exactly like the
  T009R2 dedicated-ingress split. Not claimed here.

## Automated proof (real code execution)

`tests/NiflheimSavorLiveSeamTests.cs` — link-compiles the shipped
`SavorFoodDrainResolver` over **authoritative `LocalActivationSnapshot`s produced
by the shared `LocalActivationService`**, over a Stone whose Savor node was
developed through the shared `LocalNodeProvisioningDriver`'s accepted commands (no
family-local ledger, no fabricated activation):

| claim | test |
|---|---|
| inside Area + active derived effect → 0.5; slice scaled | `Inside_area_with_active_effect_drains_at_half` |
| **AT-SAVOR-AREA-EXIT**: Area exit → 1.0 immediately, slices independent | `Stepping_outside_area_restores_full_factor_immediately` |
| denied/null snapshot → 1.0 (fail closed) | `Denied_or_null_snapshot_is_full_factor` |
| Attuned policy, unrelated occupant → 1.0 | `Attuned_policy_unrelated_occupant_is_full_factor_inside` |
| governance dormancy (no Governor) → 1.0 | `Governance_dormancy_restores_full_factor` |
| non-positive elapsed consumes nothing | `Non_positive_elapsed_consumes_nothing` |
| resolver stateless across interleaved evaluations | `Resolver_is_stateless_across_interleaved_evaluations` |

The shared substrate's own suite (`NiflheimSharedLocalEffectRuntimeTests.cs`,
merged in PR #368) proves the accepted-command provisioning path, area
entry/exit, dormancy, and stale/reordered-notification refetch.

Build / suite (verified this run):

- `dotnet build src/SBPR.Niflheim.HomesteadStones -c Release`: **0 warnings, 0 errors**.
- `dotnet build src/SBPR.Trailborne -c Release`: **0 warnings, 0 errors**.
- Full suite: **1230 / 1230**.
- `python3 scripts/docs-lint.py`: **OK**.

## Joined-client operator steps (for QA — the outstanding in-world artifact)

1. Deploy the built `SBPR.Niflheim.HomesteadStones.dll` to a QA listen-host.
2. In `BepInEx/config/net.danielgreen.sbpr.niflheim.homesteadstones.cfg` set
   `[Cooking] EnableSavorPlaytestSeam = true` **and**
   `[Progression] EnableAdminRelationshipProvisioning = true`; restart.
3. Join as a server admin, stand inside a Homestead Stone Area, run
   `sbpr_provision bond` (establishes the Governor bond), then `sbpr_savor on`
   (develops Savor + Everyone policy through accepted commands).
4. Eat a food and observe the food status timer drain at ~half rate inside the
   Area (screenshot/log with timestamps).
5. Step outside the Area (or run `sbpr_savor off` to switch the policy to Attuned):
   drain returns to normal immediately, with the already-elapsed portion neither
   refunded nor clawed back.
6. Confirm no item swap, no stat write, no food-entry mutation across the
   transition. Teardown: set the flags back to false and restart; verify the
   console command is inert.

Until (1)–(6) are captured this remediation is code+tests+docs landed under
review, NOT gate sign-off. Independent Tracer-5 acceptance remains T020.
