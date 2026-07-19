---
status: current
verdict: FAIL
---

# T030 Ready Hands — joined-client in-world equip/unequip timing QA (DoD item 9)

Author: `qa-playtest` (non-implementer). This is the decision-grade joined-client /
in-world timing artifact T030 requires and which the implementer deferred to T032
(index-T030.md R11 — ruled an unacceptable waiver by review card t_4990c465).

**VERDICT: FAIL — Ready Hands does not shorten any queued equip/unequip in-world.**
The runtime seam is bound to a method that does not exist on its declared type, so the
Harmony postfix attaches to **zero methods** and the shortening can never fire — despite
the full engine-free suite passing 1365/1365. This is the exact "logs green ≠ playable"
gap the per-node in-world DoD item exists to catch.

## Environment (real, in-world)

- Build under test: PR #376 `feat/hs-t030-ready-hands-r2` head
  `83d558fbc586ac165fb52d0069fc79e52c76aca1`.
- Deployed plugin DLL md5 `64ef911b40ec1bb1736f9ff2fa56f42f`
  (`SBPR.Niflheim.HomesteadStones.dll`), staged to BOTH config and data plugin dirs of
  the **isolated** dedicated test server `HomesteadT009L` (world `homesteadt009l`,
  private, its own container `homestead-t009l-server`). Production Niflheim/Heistan
  worlds were **not touched**.
- Real GPU modded client joined that server in-world:
  player `kniTMtyDpB_QA`, pos `(195.31, 46.90, 124.17)`. Live-game C# was executed
  in-process via the ValBridgeServer `run_script` seam (measures the actual running
  client, not an offline restatement).
- Confirmed the loaded assembly IS the PR #376 build:
  `ReadyHandsEquipDurationPatch` type is present, and its two postfix methods carry
  `[HarmonyPatch(declaringType=Humanoid, methodName=QueueEquipAction/QueueUnequipAction)]`.

## Root cause (decision-grade)

The patch declares its targets as `typeof(Humanoid).QueueEquipAction` /
`QueueUnequipAction`. In the shipped Valheim build these two methods are **private
members of `Player`, not `Humanoid`.** Live reflection on the running client:

```
Humanoid.GetMethod("QueueEquipAction")           = NULL
Player.GetMethod("QueueEquipAction")             = Player   (declaring type)
HarmonyLib.AccessTools.Method(Humanoid, "QueueEquipAction") = NULL  (target unresolved)
Harmony.GetPatchInfo(Player.QueueEquipAction)    = NULL (unpatched)
Harmony.GetPatchInfo(Player.QueueUnequipAction)  = NULL (unpatched)
```

Because `AccessTools.Method(typeof(Humanoid), "QueueEquipAction")` resolves to `null`,
Harmony has no method to attach to and the postfix is a no-op. Enumerating every
SBPR-owned patched method on the live client (38 total) confirms **neither**
`QueueEquipAction` nor `QueueUnequipAction` is among them — the Ready Hands postfixes
are simply absent from the patch set.

### Why the decomp citation misled the implementer

The patch cites decomp `assembly_valheim :22237` (equip) / `:22262` (unequip) as
"`Humanoid.QueueEquipAction`". Those line numbers fall **inside `class Player`**, which
spans decomp lines 15312–22409; `class Humanoid` is 12798–15312. The methods are
`private void` members of `Player`. The line reference was right; the declaring type
attributed to it was wrong. `Player : Humanoid`, so calling them "on the Humanoid" is
a natural but load-bearing slip — HarmonyPatch resolves the target against the exact
type token you hand it, and `Humanoid` does not declare (nor inherit a public/visible)
`QueueEquipAction`.

## Live timing capture

Method: give the joined client an eligible melee weapon and an excluded item, invoke
the real private `Player.QueueEquipAction`/`QueueUnequipAction` via reflection, then read
the `m_duration` of the `MinorActionData` the vanilla queue just appended to
`Player.m_actionQueue` — the exact per-action copy the patch claims to scale. Vanilla
`m_equipDuration` for all four test items is `0.2s`; an ACTIVE Ready Hands (factor 0.5)
must produce `0.1s` on eligible melee.

| item | skill | class | queued m_duration | vanilla m_equipDuration | ratio | expected if active |
|------|-------|-------|-------------------|-------------------------|-------|--------------------|
| SwordBronze | Swords | eligible melee | 0.2 (equip) / 0.2 (unequip) | 0.2 | 1.00 | 0.5 |
| AxeBronze | Axes | eligible melee | 0.2 (equip) | 0.2 | 1.00 | 0.5 |
| Bow | Bows | excluded | 0.2 (equip) | 0.2 | 1.00 | 1.00 |
| ShieldWood | Blocking | excluded | 0.2 (equip) | 0.2 | 1.00 | 1.00 |

Every queued action — including the two **eligible** melee weapons — runs at the full
vanilla duration (ratio 1.00). The excluded items also run at 1.00, which is correct,
but is not evidence of the exclusion logic working: nothing in this patch executes at
all, so eligible and excluded are indistinguishable in-world.

## Verified vs. reasoned

- **Verified (in-world, on the live joined client):** the postfix attaches to zero
  methods; `AccessTools.Method(typeof(Humanoid),"QueueEquipAction")` is null; eligible
  melee equip AND unequip run at full vanilla `m_duration` (ratio 1.00); no SBPR patch
  exists on `Player.QueueEquipAction`/`QueueUnequipAction`.
- **Not separately exercised (moot given the root cause):** the ACTIVE-vs-DORMANT
  duration contrast and the excluded-item non-shortening cannot be meaningfully
  contrasted in-world because the seam never runs. The personal-activation client cache
  held one snapshot with zero active rows during capture; even a fully ACTIVE Ready
  Hands cannot shorten anything while the postfix is detached. The dormant baseline
  (full vanilla duration) is therefore trivially satisfied by the bug, not by correct
  dormant handling.

## Required fix (for the implementer — not done here; QA does not edit gameplay)

Bind the patch to the real declaring type. `[HarmonyPatch(typeof(Player), "QueueEquipAction")]`
and `[HarmonyPatch(typeof(Player), "QueueUnequipAction")]` (both are `private` on
`Player`; Harmony patches privates fine). After rebinding, re-run this in-world capture
and confirm: eligible melee equip AND unequip drop to ratio 0.5 while ACTIVE; return to
1.00 while DORMANT; excluded items (bow/crossbow/shield/tool/armor) stay 1.00 while
ACTIVE; reload timing (built from `GetWeaponLoadingTime()`, a separate action type)
untouched. Add a load-time guard/assert that fails loudly if the target method does not
resolve, so a future engine rename can't silently re-open this "logs green ≠ playable"
hole.

## Handoff

PR #376 must **not** merge on this evidence. T030 is a functional FAIL in-world. The
engine-free gates (both net48 Release builds 0w/0e, suite 1365/1365, docs-lint,
diff-check, clean-room) remain green and are not in dispute — they simply do not
exercise the Harmony target-resolution seam, which is where the defect lives.
