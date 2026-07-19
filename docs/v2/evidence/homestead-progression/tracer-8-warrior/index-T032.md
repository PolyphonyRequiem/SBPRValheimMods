---
status: current
---

# Tracer 8 (Warrior) evidence — machine manifest — T032 (independent gate)

QA author: `qa-playtest` (**non-author** of T029–T031). Independent Tracer-8 gate
verdict for the **Warrior branch** — the three executable nodes reran plus BOTH
unavailable-node rejection paths. Verdict: **PASS**
([QA-joined-client-T032-PASS.md](QA-joined-client-T032-PASS.md)).

Acceptance closed: `AT-WARRIOR-UNAVAILABLE` (plus independent reruns of
`AT-TWIG-LOCAL`, `AT-READY-HANDS-BOTH-HALVES`, `AT-READY-HANDS-EXCLUSIONS`,
`AT-WEAPON-DISCIPLINE-CHOICE`, `AT-WEAPON-CAP-LIFECYCLE`).

Verified head: `5973616b4383ff266157e1725d5a777b77f9deb4` (exact `origin/main`).

| id | claim | artifact |
|----|-------|----------|
| V1 | Full suite **1413/1413**; both net48 Release builds **0w/0e**; docs-lint OK 210; `git diff --check` clean; SpecCheck recipe count unchanged — all rerun independently at the pinned head | build/test logs (this run) |
| V2 | Merged Warrior code is exactly on `origin/main` (`5973616`), working tree clean, no local delta | `git` ancestry (T029 #366→`a9d9990`, T030 #383/#384, T031 #386→`5973616`) |
| V3 | **T.W.I.G. Training** `AT-TWIG-LOCAL` reran green — placement + runtime-gate admit/refuse/undo matrix, **30/30** | `dotnet test --filter ~WarriorTwig` |
| V4 | **Ready Hands** `AT-READY-HANDS-BOTH-HALVES` / `AT-READY-HANDS-EXCLUSIONS` reran green — both-halves parity, exact 6-class melee registry, excluded classes + reload untouched, **10/10** | `dotnet test --filter ~ReadyHands` |
| V5 | **Weapon Discipline** `AT-WEAPON-DISCIPLINE-CHOICE` / `AT-WEAPON-CAP-LIFECYCLE` reran green — offered pick, **idempotent replay (one record)**, AlreadyChosen, ChoiceNotOffered (bad id + stale catalog version), highest-wins ≤100 clamp + target-skill **exclusion**, relationship-loss/death survival, save/restart rehydration, **18/18** | `dotnet test --filter ~WeaponDiscipline` |
| V6 | **AT-WARRIOR-UNAVAILABLE** — BOTH `ShrugItOffI@1` and `HeavyHands@1` are VISIBLE (authored, Unavailable status, no price, no gates), reject BP development (`NodeUnavailable`, balance untouched), reject AP purchase (`NodeNotOffered`, AP untouched, zero purchase records), and reject offering/activation (never enter the development ledger ⇒ never Offered/Purchased/Active — **NO FAKE EFFECT**). Throwaway probe **8/8**, node-by-name | `T032WarriorUnavailableProbeTests` (appendix below); real T012/T013 handlers + `DerivedActivationView` |
| V7 | Red-first: the probe's develop-rejection assertion was mutated to a bogus code, both nodes turned **RED** (`Actual "NodeUnavailable"`), then reverted **GREEN** — the probe genuinely bites | run log (this run) |
| V8 | Status-driven, not name-specific: development rejects on `!def.IsExecutable` (`TreeDevelopment.cs:189`); purchase rejects on `!def.IsExecutable \|\| Ownership != PersonalOffered` → `NodeNotOffered` (`NodePurchases.cs:150`); activation view iterates only `stone.NodeDevelopment` which an unavailable node cannot enter | source cross-reference |
| V9 | **Live server-authoritative boot** of the wired seam on the isolated `homestead-t009l-server`: `warriorTwigArmed=True`, drift check green, SpecCheck 31, **0 SBPR/Warrior Harmony failures** from the live-boot line onward (pre-boot `BadImageFormatException` is prior-process teardown; the `ShieldDomeImageEffect` NRE is vanilla `-nographics` graphics) | `QA-joined-client-T032-PASS.md` §4 |
| V10 | Safety honored: Daniel's live desktop client (`valheim.x86_64` PID 441239) never touched/deployed-to/launched-over; all work engine-free or on the disposable throwaway server; production servers untouched; throwaway probe removed after capture (suite back to 1413/1413) | `QA-joined-client-T032-PASS.md` header |
| V11 | **NO joined-client/playable claim.** The in-world last mile on a human client (place T.W.I.G., feel equip-speed, commit skill-cap, greyed-out unavailable nodes) remains explicitly unclaimed — gated on a free client per "logs-green ≠ playable" | `QA-joined-client-T032-PASS.md` §Remaining client-only risks |

- [QA-joined-client-T032-PASS.md](QA-joined-client-T032-PASS.md) — independent gate verdict (PASS)
- [index.md](index.md) — T029 node manifest
- [index-T030.md](index-T030.md) — T030 node manifest
- [index-T031.md](index-T031.md) — T031 node manifest

## Appendix — throwaway QA probe source (removed after capture)

For reproduction, drop this file at `tests/T032WarriorUnavailableProbeTests.cs`
(it link-compiles against the shipped engine-free slice, no production change), then
`dotnet test --filter "FullyQualifiedName~T032WarriorUnavailableProbe"` → **8/8**.
Delete it afterward to keep the suite at 1413/1413.

```csharp
// T032 INDEPENDENT VERIFICATION — AT-WARRIOR-UNAVAILABLE (throwaway QA probe).
// Non-author (qa-playtest) rerun. Drives BOTH authored-unavailable Warrior nodes
// — ShrugItOffI@1 and HeavyHands@1 — through the REAL shipped command handlers and
// the derived-activation view, proving each node is:
//   (1) VISIBLE with Unavailable status / no price / no gates,
//   (2) rejects BP development -> NodeUnavailable (balance untouched),
//   (3) rejects AP purchase    -> NodeNotOffered  (AP untouched, no record),
//   (4) never Offered/Purchased/Active in the derived activation view (NO FAKE EFFECT).
// Mirrors the T012/T013 fixtures (same handlers, same seeding).
//
// [Test class: T032WarriorUnavailableProbeTests — 4 [Theory] methods, each
//  [InlineData("ShrugItOffI")] + [InlineData("HeavyHands")] = 8 cases:
//    Unavailable_node_is_visible_with_no_price_and_no_gates
//    Unavailable_node_rejects_bp_development
//    Unavailable_node_rejects_ap_purchase
//    Unavailable_node_never_offered_purchased_or_active
//  Fixture: Stone-L2 Homestead, Warrior committed (Martial Facet); a bonded
//  Governor with BP=20/AP=20 and an attuned actor with AP=20 + purchase authority
//  (so rejections land on node STATUS, not empty wallet / missing relationship).
//  Assertions: def.Status==Unavailable, !IsExecutable, Ownership==NoneWhileUnavailable,
//  Pricing.{DevelopmentBpPrice,PurchaseApPrice}==null, Requirements gates false;
//  develop -> "NodeUnavailable" + BpOf unchanged; purchase -> "NodeNotOffered" +
//  ApOf unchanged + PurchaseCountOf==0; DerivedActivationView.Derive rows show the
//  node never Offered/Purchased/Active and no collateral activation anywhere.]
```

The complete compilable source (152 lines) is preserved in the QA run workspace at
`~/.hermes/kanban/workspaces/t_91763404/T032WarriorUnavailableProbeTests.cs.saved`.
It uses only public API of the engine-free slice: `ActivityCommandHandler`,
`DevelopmentCommandHandler`, `PurchaseCommandHandler`, `HomesteadProgressionCatalog`,
`DerivedActivationView.Derive`, and the in-memory stores — no decompiled IronGate
source, no other mod's code (clean-room intact).
