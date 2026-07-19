---
status: current
---

# T029 T.W.I.G. Training — joined-client QA verdict: **FAIL (blocking, unwired seam)**

QA author: `qa-playtest` (independent, non-author). Task `t_92e47866`, PR #366,
reviewed engine-free head `be3ce33`.

## Verdict

**FAIL.** The T.W.I.G. Training Local placement capability
(`LocalPlacementProvider`) is a correct, well-tested **pure value object that is
not connected to any net48 runtime placement surface**. On a real joined client,
placing a T.W.I.G. (`TrainingDummy`) inside a Homestead executes entirely through
vanilla `Player.PlacePiece` with **zero** SBPR gating: the effect-active /
Settlement-policy / build-Permission AND that the provider computes is never
invoked in-world. There is therefore no observable joined-client behavior to
verify — neither the admit path nor the refusal paths exist at runtime.

Per the card's explicit instruction ("If the provider is not wired into the net48
placement/runtime surface or prefab binding is wrong, FAIL with exact seam ... no
simulation substitution"), no QA client was launched to "prove" behavior that the
code cannot produce. The absence is proven by static wiring analysis of the
shipped source, which is dispositive.

## Prefab identity — CONFIRMED CORRECT

The one thing the card flagged as unverified (`TrainingDummy`) is correct:

- Vanilla decompiled `assembly_valheim.decompiled.cs` uses `TrainingDummy` as the
  `Character.Faction` for the piece (line 5083, 6832).
- Fandom wiki `T.W.I.G.` entry, "Internal ID": `TrainingDummy`
  (`sbpr-corpus/wiki/fandom/T.W.I.G..md:27`).
- `LocalPlacementProvider.TwigPrefabName = "TrainingDummy"` matches.

The binding is not the defect. The wiring is.

## The exact seam (blocking)

`LocalPlacementProvider.Admit(...)` is never reached at runtime. Evidence:

1. **No source references the provider.** `grep -rln "LocalPlacementProvider|WarriorPlacement"
   src --include=*.cs` returns only the provider's own file. The one other mention
   of the node (`HomesteadProgressionCatalog.cs:308`) is the catalog `NodeDefinition`
   for `TwigTraining@1` — it names the node, it does not wire the provider to a
   placement observer.

2. **No placement observer routes `TrainingDummy`.** The only in-world placement
   patches in the plugin are `FoundationalPlacementObserver` (listen-host,
   `[HarmonyPatch(typeof(Player), Player.PlacePiece)]`) and
   `DedicatedPlacementIngressObserver` → `DedicatedPlacementIngress`. Both resolve
   the placed prefab through `FoundationalPrefabMap` and route to
   `FoundationalPlacementRuntime.Observe`. `FoundationalPrefabMap` contains **no**
   `TrainingDummy` mapping (`grep TrainingDummy FoundationalPrefabMap.cs` → NONE),
   so a T.W.I.G. placement resolves to empty and is simply ignored by the
   Foundational path — it is never handed to the Warrior provider.

3. **`Plugin.cs` arms no Warrior/Local placement path.** The boot `PatchAll` set
   (lines 48–92) installs the Foundational runtime bootstrap + observer, the
   dedicated ingress observer, pilot/session lifecycle, and relationship
   provisioning. There is no Warrior placement observer, no
   `LocalEffectActivationView` construction from live world state, and no
   engine-bound adapter that feeds a real placement + occupant into
   `LocalPlacementProvider.Admit`.

Net effect: the AND the provider computes has no runtime caller. A client placing
the exact T.W.I.G. is gated only by vanilla build rules; the Settlement-policy /
effect-active / SBPR-Permission refusal the spec (FR-016) requires does not fire
in-game, and the "refusal outside policy / without Permission" cases cannot be
demonstrated because no SBPR code runs on that placement.

## What IS verified (engine-free, holds)

- Provider shape, exact-piece grammar, the load-bearing Permission AND, no-second-
  ledger dormancy re-derivation: all covered by `NiflheimWarriorTwigPlacementTests`
  (11 tests) and the shared `NiflheimLocalPolicyDormancyTests`. Reviewer
  `t_2b31bc9b` is engine-free PASS. That verdict stands and is not disturbed.
- This QA does not contradict the engine-free reviewer; it reports the missing
  layer the reviewer explicitly deferred ("joined-client artifact").

## Comparison to the wired precedent (Foundational, T009)

The Foundational placement capability is the pattern to match. It has:
`Application/Runtime/FoundationalPlacementRuntime` (adapter → pipeline → receipt),
`FoundationalPrefabMap` (prefab→stable-id), a listen-host
`FoundationalPlacementObserver` and a `DedicatedPlacementIngress(Observer)`, all
armed in `Plugin.cs`, and a joined-client evidence trail (T009L2/L3, merged main).
The Warrior Local slice has **none of these** — only the pure provider.

## Remediation

Filed engineer-systems remediation to wire the Warrior Local placement provider
into the net48 runtime placement surface (see child card). Until that lands and a
joined client can be shown (a) placing the exact T.W.I.G. under active policy +
Permission and (b) being refused outside policy / without Permission, T029's
definition-of-done item 9 (joined-client artifact) is **not met** and PR #366 must
remain merge-blocked on this artifact.

No branch commit of a PASS artifact is made (there is nothing to pass). QA
launched no client and altered no client binaries; nothing to tear down.
