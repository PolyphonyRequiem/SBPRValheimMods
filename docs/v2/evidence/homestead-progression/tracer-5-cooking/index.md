---
status: current
---

# Tracer 5 (Cooking) evidence — machine manifest (T016: Savor the Hearth)

Node: **T016 [US4]** — Savor the Hearth, first Cooking vertical slice (node 1 of 4).
Acceptance target: `AT-SAVOR-AREA-EXIT`. Status: **code + tests + docs landed under
review**; joined-client in-world artifact **BLOCKED** pending QA clearance (owner was
mid-session on a live Valheim client). Independent Tracer-5 verdict is T020.

| id | claim | artifact |
|----|-------|----------|
| S1 | Eligible occupant inside the Stone Area drains active food timers at factor 0.5 | `tests/NiflheimSavorTheHearthTests.cs` — `Eligible_occupant_inside_area_drains_at_half_factor` |
| S2 | AT-SAVOR-AREA-EXIT: Area exit restores factor 1.0 immediately (stateless re-derive, zero writes) | `tests/NiflheimSavorTheHearthTests.cs` — `Stepping_outside_area_restores_full_factor_immediately` |
| S3 | Settlement-policy loss restores factor 1.0 even inside the Area | `tests/NiflheimSavorTheHearthTests.cs` — `Policy_loss_restores_full_factor_even_inside_area` |
| S4 | Governance dormancy (no authorized Governor) restores factor 1.0 | `tests/NiflheimSavorTheHearthTests.cs` — `Governance_dormancy_restores_full_factor` |
| S5 | Undeveloped Savor never slows | `tests/NiflheimSavorTheHearthTests.cs` — `Undeveloped_savor_never_slows` |
| S6 | No retroactive duration: `ConsumeElapsed` scales only the current slice; exit never refunds/claws back; aggregate untouched | `tests/NiflheimSavorTheHearthTests.cs` — `Consume_elapsed_scales_only_the_current_slice`, `Exit_does_not_retroactively_refund_previously_slowed_time`, `Non_positive_elapsed_consumes_nothing` |
| S7 | Provider is stateless across interleaved evaluations; attuned guest admitted when relationship + policy allow | `tests/NiflheimSavorTheHearthTests.cs` — `Provider_is_stateless_across_repeated_evaluations`, `Guest_gains_slow_when_relationship_and_policy_admit` |
| S8 | Full suite 1205/1205 (Savor subset 10/10); both net48 Release builds 0w/0e (HomesteadStones + Trailborne); docs-lint OK; `git diff --check` clean | build/test logs (this run) |
| S9 | Engine-free CLEAN slice: no UnityEngine/BepInEx/ZNetView/Harmony/Valheim type in `Adapters/Cooking/CookingProviders.cs`; net8 link-compile = real execution. NO playable/live-client claim | `src/SBPR.Niflheim.HomesteadStones/Adapters/Cooking/CookingProviders.cs`; README §"Joined-client in-area/exit artifact — BLOCKED" |
| S10 | Joined-client in-area/exit in-world proof (net48 Harmony food-timer seam on a QA client) — NOT produced; deferred under the safety gate | README §"Joined-client in-area/exit artifact — BLOCKED (pending QA clearance)" |

- [README.md](README.md) — full analysis, provider description, and the blocked joined-client leg
