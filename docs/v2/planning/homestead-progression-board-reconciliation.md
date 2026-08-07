---
title: "Homestead progression — board vs. code reconciliation (2026-08-07)"
status: current
purpose: Establish, from code and evidence rather than from checkboxes, the true state of every open ADO epic-85 card and every open task box in the S2 progression decomposition.
---

# Homestead progression — board reconciliation

**Reconciled at:** `origin/main` @ `de77f31` (2026-08-07).
**Method:** for every open card under ADO epic 85 and every open task box in
[`homestead-stone-progression-tasks.md`](homestead-stone-progression-tasks.md), the named
production file was probed on `origin/main`, its tests located and run, and
`docs/v2/evidence/` searched for a joined-client artifact. No classification below rests on a
checkbox, a card state, or a prior report.

**Gates re-run this pass (real numbers):** `SBPR.Trailborne` net48 Release 0 warnings / 0 errors;
`SBPR.Niflheim.HomesteadStones` net48 Release 0 warnings / 0 errors; full suite
**1708 / 1708 passed**; `git diff --check` clean; `SpecCheck.cs` manifest checked and correctly
unchanged (this pass changes no recipe, piece, station, item, or mechanic).

**No in-world claim is available in this pass.** There was no game client. Every "verified"
below means *file exists on main, tests exist and pass, and an evidence artifact is on disk* —
never that a human saw it work.

## Classification key

| Class | Meaning | Board action |
|---|---|---|
| SHIPPED-AND-VERIFIED | Code on main, tests green, AND a non-author in-world artifact | close |
| SHIPPED-BUT-UNVERIFIED | Code on main and tests green, but no in-world proof | **stays open** |
| GENUINELY-UNBUILT | Named production file absent from main | leave open |
| VERIFICATION-ONLY | Nothing to build; needs a non-author in-world pass | flag for Daniel |

## Task boxes

| Box | Named production file | On main? | Tests | Evidence artifact | Class |
|---|---|---|---|---|---|
| T006 verify Tracer 1 | — (verifier) | n/a | n/a | **no `tracer-1/` folder** | VERIFICATION-ONLY |
| T007 relationships | `Domain/CharacterProgression/Relationships.cs`, `Application/Commands/RelationshipCommands.cs`, `Persistence/Characters/AccountStoneAuthorityStore.cs` | **yes, all three** | `NiflheimRelationshipLifecycleTests.cs` (640 lines), green | **no `tracer-2/` folder** | SHIPPED-BUT-UNVERIFIED |
| T009 verify Tracer 2 | — (verifier) | n/a | n/a | **no `tracer-2/` folder** | VERIFICATION-ONLY |
| T016 Savor the Hearth | `Adapters/Cooking/CookingProviders.cs` + net48 `Features/Cooking/SavorFoodTimerObserver.cs` (registered in `Plugin.Awake`) | **yes** | `NiflheimSavorTheHearthTests.cs`, `NiflheimSavorLiveSeamTests.cs`, green | `joined-client-PASS-t_8b6e9e60.md` — PASS at data+delivery layer, **"in-world 0.5x food-bar last mile REASONED, not observed"** | SHIPPED-BUT-UNVERIFIED |
| T020 verify Cooking branch | — (verifier) | n/a | n/a | no branch-level verdict; `AT-WATCHFUL-UNAVAILABLE` unproven | VERIFICATION-ONLY |
| T023 Built to Last | `Adapters/Crafting/DurabilityIssuanceProvider.cs` | **no** | — | — | GENUINELY-UNBUILT (PR #497 open, unmerged) |
| T024 verify Crafting branch | — (verifier) | n/a | n/a | node artifacts only; no branch verdict | VERIFICATION-ONLY |
| T026 Field Fletching I | `Adapters/Archer/BushcraftRecipeProvider.cs` + net48 `Features/Archer/FieldFletchingRecipeGate.cs` (registered) | **yes** | `NiflheimFieldFletchingTests.cs`, green | `t026-field-fletching/R2-joined-client-proof.md` — PASS at delivery+data layer, **"GUI last mile REASONED"** | SHIPPED-BUT-UNVERIFIED |
| T027 Fletcher's Habit | `Adapters/Archer/ProjectileRecoveryProvider.cs` | **no** | — | — | GENUINELY-UNBUILT |
| T028 verify Archer branch | — (verifier) | n/a | n/a | no branch verdict; T027 not built | VERIFICATION-ONLY |
| T034 recovery + operator surface | `Features/Progression/ProgressionDiagnostics.cs` | **no** | — | — | GENUINELY-UNBUILT (PR #500 open, unmerged) |
| T035 remote-shaped command | `Features/Progression/ProgressionCommandEndpoint.cs`, `Application/Queries/GetRelationshipPortfolio.cs`, `Application/Queries/ProgressionNotifications.cs` | **no, none** | — | — | GENUINELY-UNBUILT (PR #499 open, unmerged) |
| T036 runtime conformance | `Features/Progression/ProgressionConformance.cs` | **no** | — | — | GENUINELY-UNBUILT (PR #498 open, unmerged) |
| T037 final verification | — (verifier) | n/a | n/a | no `tracer-9/` folder | VERIFICATION-ONLY |

### Boxes already ticked — spot-checked, all correctly ticked

T025 (`PracticeRangeProvider.cs` present + gate registered + R2 PASS artifact), T030
(`EquipDurationProvider.cs` + registered `ReadyHandsEquipDurationPatch`, non-author PASS),
T032 (non-author branch PASS), T021/T022 (providers present, PASS artifacts), T017–T019
(`CookingCraftPolicy.cs`, `FoodRefreshThresholdProvider.cs`, `MenuCraftDurationProvider.cs` all
present with gates registered), T033 (`TreeRevocation.cs`, `RevocationCommands.cs` present).
No ticked box was found to be hollow.

### Correction to the 2026-08-06 probe

That probe reported production files on main for **T007, T016, T026 and T034**. Three of the
four hold. **T034 does not:** its named file `Features/Progression/ProgressionDiagnostics.cs` is
absent from main. What exists is `Persistence/Recovery/ProgressionStateRepair.cs`, which is
**T005's** artifact, not T034's — T034 only *extends* it. A path-fragment match on
`ProgressionStateRepair` is the likely source of the false positive. T034 is unbuilt on main.

## ADO epic 85 — open cards

Every open card under epic 85 was read. **No card is closed by this pass**, and the reason is
uniform enough to state once rather than per row.

| Card | Subject | Truth from code/evidence | Class |
|---|---|---|---|
| #113 | points credited exactly once through crash/retry | Gate-A slice on main; non-author PASS recorded at `gate-a/README.md` covering hostile principal, replay, real SIGKILL at each write boundary, two-client race | SHIPPED-BUT-UNVERIFIED in-world (offline proof is complete and independent) |
| #114 | saved state reloads, refuses nonsense | `ProgressionStateRepair.cs` on main, recovery tests green; no `tracer-1/` verdict | VERIFICATION-ONLY |
| #115 | Bond / Attune / release, one character per account | code on main (T007), tests green; no `tracer-2/` verdict | SHIPPED-BUT-UNVERIFIED |
| #116 | food drains slower inside the Homestead | code + registered seam on main; artifact PASS at delivery layer, in-world reasoned | SHIPPED-BUT-UNVERIFIED |
| #117 | Cooking branch as a whole | 4 of 4 nodes on main; no branch verdict, `AT-WATCHFUL-UNAVAILABLE` unproven | VERIFICATION-ONLY |
| #118 | arrows in the field without a workbench | code + registered gate on main, PASS artifact — **the card says "Unbuilt", which is wrong** | SHIPPED-BUT-UNVERIFIED |
| #119 | recover the exact arrow, once | provider absent from main | GENUINELY-UNBUILT |
| #120 | Archer branch as a whole | blocked by #119 | VERIFICATION-ONLY |
| #121 | weapon swaps are faster | code + registered patch on main; non-author PASS (T030) proving the live binding resolves on the real game assembly; the visual stopwatch is explicitly unclaimed | SHIPPED-BUT-UNVERIFIED |
| #122 | crash/restart/rejoin loses nothing | T034 absent from main (PR #500 unmerged) | GENUINELY-UNBUILT |
| #95 | craft at a bench beyond its level | T021 on main, joined-client rerun PASS at durable layer | SHIPPED-BUT-UNVERIFIED |
| #96 | crafted item comes out marked | T022 on main; the mark has never been observed applied in a live craft | SHIPPED-BUT-UNVERIFIED |
| #109 | upgrade at a bench the effect makes eligible | path never reached live; fixture shape resolved 2026-08-04, not executed | VERIFICATION-ONLY |
| #110 | marked item survives upgrade and trade | codec on main, proven offline; live leg never executed | SHIPPED-BUT-UNVERIFIED |
| #111 | forged mark ignored | proven offline against every named forgery class; card body already records **"CLOSED — offline sufficient per Daniel 2026-08-04"** | **closable, pending Daniel** (see below) |
| #112 | whole-proof gate | fan-in; inputs open | VERIFICATION-ONLY |

### Why zero cards were closed

The brief's rule is that a code-exists probe never closes a card. Applying it strictly: of the
sixteen open cards, none has an artifact in which a non-author observed the behaviour on a
rendering client. Every PASS on disk is honestly self-labelled — *"data layer verified, last
mile REASONED, not observed"* — and that is exactly the claim that must not be upgraded.

**#111 is the one genuine candidate and it is deliberately left to Daniel.** Its card body
already carries `CLOSED - offline sufficient per Daniel 2026-08-04`, and its subject (a forged
save-file mark is ignored) is a class of proof where offline *is* the right instrument — there
is no in-world observation that would add information. But the record of that decision is a
line in a description field, not a state transition, and closing a card on my reading of
someone else's note is precisely the overstatement this pass exists to remove. One word from
Daniel closes it.

## What this pass verified vs. did not

- **Verified:** file presence on `origin/main` for every named production artifact; test
  presence and a green 1708/1708 suite; both net48 Release builds at 0/0; evidence-folder
  contents and the literal verdict line of every artifact; the `SpecCheck` manifest is
  unchanged and correct; Harmony registration in `Plugin.Awake()` for every seam claimed
  landed.
- **Not verified:** any in-world behaviour whatsoever. No client was run. Every
  SHIPPED-BUT-UNVERIFIED row stays open for that reason and no other.

## Open question for Daniel

Nine cards sit in SHIPPED-BUT-UNVERIFIED with the same shape: the code is on main, the offline
proof is thorough and independent, and the only missing step is a human on a rendering client.
That is one QA campaign, not nine — but it needs two live clients and your co-design of the
per-client step plan before anything runs. Say the word and it becomes a single scheduled pass
rather than nine cards waiting separately.
