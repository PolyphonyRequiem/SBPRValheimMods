---
status: current
---

# Tracer 5 — Cooking branch evidence (T016: Savor the Hearth)

Node: **T016 [US4]** — Savor the Hearth, first Cooking vertical slice (node 1 of 4).
Acceptance target: `AT-SAVOR-AREA-EXIT`.

This folder collects the Cooking-branch evidence. T016 is the first node; T017–T019
add their own artifacts here, and T020 records the independent Tracer-5 verdict.

## What T016 landed (engine-free CLEAN slice)

`Adapters/Cooking/CookingProviders.cs` — `SavorTheHearthProvider`, the pure,
stateless derived provider that translates the T014
`LocalEffectActivationView.StatusFor(Savor).Active` decision into the vanilla
food-timer drain factor:

- **factor 0.5** while the Savor Local Effect is active for the occupant (developed
  + authorized Governor present + inside the Stone Area + Settlement-policy
  eligible — the AND already owned by `LocalEffectActivationView`);
- **factor 1.0** otherwise.

It is **not** a second ledger: it stores nothing, mutates no item / stat / food
entry, and carries **no retroactive duration**. `ConsumeElapsed(view, elapsed)`
scales only the slice it is handed (`elapsed * factor`), so stepping outside the
Area (or losing policy eligibility / governance) restores factor 1 on the very next
derivation with zero writes — and time already consumed at a different factor is
never refunded or clawed back.

## Automated proof (real code execution)

`tests/NiflheimSavorTheHearthTests.cs` — **10 tests**, red-first-then-green,
link-compiling the SHIPPED provider + the SHIPPED `LocalEffectActivationView`
derivation into the net8 test host (no mocks):

| # | claim | test |
|---|---|---|
| 1 | eligible occupant inside the Area drains at 0.5 | `Eligible_occupant_inside_area_drains_at_half_factor` |
| 2 | **AT-SAVOR-AREA-EXIT**: exit restores 1.0 immediately (stateless re-derive) | `Stepping_outside_area_restores_full_factor_immediately` |
| 3 | policy loss restores 1.0 even inside the Area | `Policy_loss_restores_full_factor_even_inside_area` |
| 4 | governance dormancy (no authorized Governor) restores 1.0 | `Governance_dormancy_restores_full_factor` |
| 5 | undeveloped Savor never slows | `Undeveloped_savor_never_slows` |
| 6 | `ConsumeElapsed` scales only the current slice | `Consume_elapsed_scales_only_the_current_slice` |
| 7 | exit does not retroactively refund previously-slowed time; aggregate untouched | `Exit_does_not_retroactively_refund_previously_slowed_time` |
| 8 | non-positive elapsed consumes nothing | `Non_positive_elapsed_consumes_nothing` |
| 9 | provider is stateless across interleaved evaluations | `Provider_is_stateless_across_repeated_evaluations` |
| 10 | attuned guest gains the slow when relationship + policy admit | `Guest_gains_slow_when_relationship_and_policy_admit` |

### Build / suite / docs (verified this node)

- `dotnet build src/SBPR.Niflheim.HomesteadStones -c Release`: **0 warnings, 0 errors**.
- `dotnet build src/SBPR.Trailborne -c Release`: **0 warnings, 0 errors**.
- Full test suite: **1205 / 1205** (Savor subset: 10 / 10).
- `python3 scripts/docs-lint.py`: **OK**.
- `git diff --check`: clean.

## Joined-client in-area/exit artifact — live seam WIRED (remediation t_803e92f6)

The T016 engine-free slice above shipped the pure provider only; the net48
food-timer seam was absent, so the first joined-client proof returned **FAIL
(live path absent)** — see
[`joined-client-FAIL-t_0fb85725.md`](joined-client-FAIL-t_0fb85725.md).

Remediation `t_803e92f6` wires the exact missing hook — a `Player.UpdateFood`
prefix that scales ONLY the elapsed food-drain slice by the shipped provider
factor, plus a playtest-gated admin seam that establishes the active Savor
context. Full description, the 11 new engine-free live-seam tests, the
listen-host vs. dedicated-client scope, and the QA operator steps for the
in-world 0.5/1.0 artifact are in
[`live-seam-wired-t_803e92f6.md`](live-seam-wired-t_803e92f6.md).

**Logs-green is STILL NEVER playable.** The in-world artifact (a joined client's
food timer visibly draining at 0.5 inside the Area, snapping to 1.0 on exit,
without item/stat mutation) is captured by QA using the operator steps in that
doc; it is **not claimed** by this remediation. What the in-world artifact must
still capture, once QA runs it:

1. The net48 Harmony seam that scales an active food timer by the provider factor
   (the engine adapter over `SavorTheHearthProvider`), deployed to a QA client.
2. A joined client standing inside the Stone Area with a cultivated Savor node:
   food status timer drains at ~half rate; screenshot/log with timestamps.
3. The same client stepping OUTSIDE the Area: drain rate returns to normal
   immediately, with the already-elapsed portion neither refunded nor clawed back.
4. No item swap, no stat write, no food-entry mutation across the transition.
5. QA teardown restores client binaries byte-for-byte; the AFK/live client is
   stopped after evidence and its process absence verified.

Until (1)–(5) are captured, T016 is **code + tests + docs landed under review**,
NOT gate sign-off. Independent Tracer-5 acceptance is T020.
