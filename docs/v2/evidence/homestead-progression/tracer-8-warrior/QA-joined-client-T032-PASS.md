---
status: current
---

# T032 Warrior branch — independent Tracer-8 gate verdict: **PASS**

QA author: `qa-playtest` (independent, **non-author of T029–T031**). Kanban task
`t_bf662ceb`. Standing evidence-merge authority (authorized 2026-07-18).

- Verified head: **`5973616b4383ff266157e1725d5a777b77f9deb4`** — exact `origin/main`
  tip carrying all three merged Warrior nodes (T029 T.W.I.G. via #366→`a9d9990`,
  T030 Ready Hands via #383/#384, T031 Weapon Discipline via #386→`5973616`).
- Verified in a detached worktree pinned to that exact SHA; `git diff --check`
  clean, working tree clean (no local delta to the merged Warrior code).
- Fresh net48 Release `SBPR.Niflheim.HomesteadStones.dll` md5 at the isolated-server
  boot: **`d86bc118f2c6d958063537b893035678`** (byte content of the pinned head).
- **Safety honored.** A live user desktop client (`valheim.x86_64`, PID 441239)
  belonging to Daniel was running under the interactive Plasma session throughout
  this run. It was **never touched, deployed to, or launched over.** All QA work ran
  engine-free (test suite) or on the isolated headless throwaway server
  `homestead-t009l-server` (disposable world `homesteadt009l`, non-public).
  Production `niflheim-server` / `heistan-server` were UNTOUCHED.

## Verdict: **PASS** — Warrior branch closed for the T033 fan-in

All three executable Warrior nodes' own proofs reran green, the choice
idempotency/exclusion cases hold, and BOTH unavailable-node rejection paths
(Shrug It Off I and Heavy Hands) are verified visible-but-inert with no fake
effect. **`AT-WARRIOR-UNAVAILABLE` PASSES.**

> **Honesty frame (repo AGENTS.md): "logs-green ≠ playable."** What is VERIFIED
> below is the authoritative data/decision layer a joined client reads from plus a
> live server-authoritative boot of the wired seam. What remains explicitly
> UNCLAIMED is the in-world last mile on a human's client (a player physically
> placing the T.W.I.G. piece / feeling the equip-speed change / seeing the two
> unavailable nodes greyed-out in the Stone UI). That last mile is gated on a
> free client and is not asserted here.

## 1. Independent full-gate rerun (engine-free)

Reran from the pinned worktree with the repo build refs:

| gate | result |
|------|--------|
| Full test suite (`dotnet test -c Release`) | **1413 / 1413 passed**, 0 failed, 0 skipped |
| net48 Release build — `SBPR.Niflheim.HomesteadStones` | **0 warnings / 0 errors** |
| net48 Release build — `SBPR.Trailborne` | **0 warnings / 0 errors** |
| `python3 scripts/docs-lint.py` | **OK — 210 docs checked** |
| `git diff --check` | **clean** |
| `SpecCheck` recipe manifest | unchanged (Warrior nodes register no SBPR recipe) |

## 2. Each executable Warrior node's own proof — independently rerun

| node | acceptance | rerun result |
|------|-----------|--------------|
| **T.W.I.G. Training** (`TwigTraining@1`, Local) | `AT-TWIG-LOCAL` | `~WarriorTwig` filter: **30 / 30 passed** — placement/runtime-gate admit + every refuse/undo branch against the composed `FoundationalProgressionServer` |
| **Ready Hands** (`ReadyHands@1`, Character Effect) | `AT-READY-HANDS-BOTH-HALVES`, `AT-READY-HANDS-EXCLUSIONS` | `~ReadyHands` filter: **10 / 10 passed** — both-halves parity across every eligible melee skill, exact 6-class registry, every excluded class + reload untouched |
| **Weapon Discipline** (`WeaponDiscipline@1`, Permanent Effect) | `AT-WEAPON-DISCIPLINE-CHOICE`, `AT-WEAPON-CAP-LIFECYCLE` | `~WeaponDiscipline` filter: **18 / 18 passed** — offered pick, idempotent replay (one record), AlreadyChosen, highest-wins ≤100 clamp, relationship-loss/death survival, save/restart rehydration |

The **exact choice idempotency / exclusion cases** required by the card are inside
the Weapon Discipline set: idempotent replay yields exactly one record;
`AlreadyChosen` blocks a second spend; `ChoiceNotOffered` (bad id + stale catalog
version) and target-skill-filter exclusion (highest-wins contributes ONLY from
matching target skills, red-first-confirmed by the authors) all pass.

## 3. `AT-WARRIOR-UNAVAILABLE` — BOTH unavailable nodes, independently proven

Both **Shrug It Off I** (`ShrugItOffI@1`) and **Heavy Hands** (`HeavyHands@1`) are
authored Warrior L1 Character-Effect nodes with `Status = Unavailable`,
`Ownership = NoneWhileUnavailable`, no BP/AP price, and no authored gates.

A **throwaway, read-only QA probe** (`T032WarriorUnavailableProbeTests`, mirroring
the shipped T012/T013 fixtures — same real `ActivityCommandHandler`,
`DevelopmentCommandHandler`, `PurchaseCommandHandler`, and `DerivedActivationView`)
drove BOTH nodes end-to-end. The probe authors no production change and was removed
after capture (suite returns to 1413/1413). Its full source is transcribed in the
machine manifest's appendix ([index-T032.md](index-T032.md)) for reproduction.

Probe result — **8 / 8 passed** (both nodes × 4 properties):

| property | Shrug It Off I | Heavy Hands |
|----------|---------------|-------------|
| **Visible** in the authored catalog, `Unavailable` status, no price, no gates | ✅ | ✅ |
| Rejects **BP development** → `NodeUnavailable`, BP balance untouched (no partial spend) | ✅ | ✅ |
| Rejects **AP purchase** (by an attuned actor holding AP + authority) → `NodeNotOffered`, AP untouched, zero purchase records | ✅ | ✅ |
| Rejects **Offering / activation — NO FAKE EFFECT**: never enters the development ledger, so never surfaces Offered / Purchased / Active in the derived activation view; no collateral activation of any node | ✅ | ✅ |

**Red-first discipline.** The probe's develop-rejection assertion was mutated to
expect a bogus result code; both nodes turned **RED** (`Expected "SHOULD_NOT_MATCH",
Actual "NodeUnavailable"`), then the assertion was reverted **GREEN**. The probe
genuinely bites — it is not a vacuous pass.

**Why this is decisive, not merely reasoned.** The rejection gates are status-driven,
not node-name-specific: development rejects on `!def.IsExecutable`
(`TreeDevelopment.cs`), purchase rejects on `!def.IsExecutable || Ownership !=
PersonalOffered` reported as `NodeNotOffered` (`NodePurchases.cs`), and the derived
activation view iterates only `stone.NodeDevelopment` — which an unavailable node can
never enter because both its develop and purchase paths reject. A "fake effect" is
therefore structurally impossible, and the probe exercises that on the two real
Warrior nodes by name.

## 4. Live server-authoritative boot of the wired seam

Deployed the pinned-head `HomesteadStones.dll` to the isolated
`homestead-t009l-server` (config + data staged; process restarted via
`supervisorctl restart valheim-server`). Boot capture (`docker logs`):

```
[Niflheim/HomesteadStones] Runtime drift check: all required targets/callsites present.
[Niflheim.HomesteadStones] Harmony patches installed.
[Niflheim/HomesteadStones] Local progression runtime composed (server-authoritative). … warriorTwigArmed=True.
[Trailborne/SpecCheck] ✓ All 31 recipes match the v0.1.0 spec manifest; …
[Niflheim/HomesteadStones] [stone-areas] registered=7 …
```

- **`warriorTwigArmed=True`** — the T.W.I.G. placement gate + pending-undo queue
  composed against the authoritative Local Effect runtime at boot.
- **Zero SBPR / Warrior Harmony failures** from the live-boot line (Harmony installed
  08:47:25) onward. The `BadImageFormatException: Method has zero rva` entries are
  timestamped 08:47:21 — the PREVIOUS process's BepInEx teardown noise, strictly
  BEFORE the live boot (the documented headless artifact).
- The one `ArgumentNullException` at boot is vanilla `ShieldDomeImageEffect.Awake`
  (a null shader on a `-nographics` server) — a base-game graphics component, NOT
  SBPR/Warrior code. A `SeersStone` icon-missing error is an unrelated Trailborne
  deploy artifact (Trailborne DLL was not redeployed; irrelevant to the Warrior gate).

## Remaining client-only risks (explicitly unclaimed)

- A joined human client physically placing the exact vanilla T.W.I.G. piece under
  the FR-016 effect-active AND build-Permission gate and seeing refused placements
  undone in-world.
- A player feeling the Ready Hands equip/unequip speed-up on an eligible melee
  weapon and NO change on an excluded class/reload.
- A player committing a Weapon Discipline choice and observing the raised skill cap
  survive death/relog in the live `Skills` UI.
- Both unavailable nodes rendering visible-but-non-interactable in the joined Stone
  progression UI.

These are the standard "logs-green ≠ playable" last-mile items, gated on a free
client (a live user client was present throughout this run).

## Reproduction

```
git worktree add --detach <wt> 5973616b4383ff266157e1725d5a777b77f9deb4
cd <wt>; set -a; source .env; set +a
dotnet build src/SBPR.Niflheim.HomesteadStones/SBPR.Niflheim.HomesteadStones.csproj -c Release   # 0w/0e
dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release                                # 0w/0e
dotnet test tests/SBPR.Trailborne.Tests.csproj -c Release                                         # 1413/1413
dotnet test … --filter "FullyQualifiedName~WarriorTwig"        # 30/30
dotnet test … --filter "FullyQualifiedName~ReadyHands"         # 10/10
dotnet test … --filter "FullyQualifiedName~WeaponDiscipline"   # 18/18
# drop tests/T032WarriorUnavailableProbeTests.cs (appendix of index-T032.md), then:
dotnet test … --filter "FullyQualifiedName~T032WarriorUnavailableProbe"   # 8/8
python3 scripts/docs-lint.py    # OK
```
