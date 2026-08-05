---
status: current
---

# Patch-registration conformance — evidence (human orientation)

Human-readable orientation for the patch-registration conformance work collected under this
folder. Companion to the [machine index](index.md).

## What this is about

HarmonyX patches **only** the types explicitly passed to `harmony.PatchAll(typeof(X))`. There is no
assembly-wide auto-scan. So a `[HarmonyPatch]` class that nobody remembered to register compiles
cleanly, ships in the DLL, passes every unit test written against it, and does **nothing** in-world
— no build error, no boot warning, no runtime signal at all. The feature is simply inert, and the
usual evidence of health (green tests, green CI, green build) stays green the whole time.

That failure has now shipped **three times** in `SBPR.Niflheim.HomesteadStones`:

1. **IAP-015** — three operator classes shipped unregistered; the `sbpr_pilotop` console command was
   absent from `Terminal.commands`. Found by a human running a live smoke.
2. **T030 Ready Hands, first failure** — the patch was bound to `Humanoid`, which declares neither
   target method, so patch discovery resolved zero methods. Found by a human in-world.
3. **T030 Ready Hands, second failure (ADO #125)** — correct class, correct `Player` binding, simply
   missing from the registration list. Found by a static reachability trace, weeks after shipping.

Two guards already existed and neither could catch the general case: one asserts three *hardcoded*
operator roles wove, and the other proves the *target methods* still exist on `Player` while being
structurally blind to whether the patch class was ever registered. That second guard passed green
throughout the entire period Ready Hands was dead.

## What is here today

- [`ADO-126-patch-registration-conformance.md`](ADO-126-patch-registration-conformance.md) — the
  generalised fix (ADO #126). Delivers **both** halves of the net: `SBPR.Trailborne`'s boot-time
  `PatchCheck` ported into HomesteadStones (which never received it), plus a new **PR-time**
  source-conformance test that catches the defect before merge with no running game. Includes the
  red-first mutation proofs that reproduce occurrences 1 and 3 by name, and states plainly what
  remains unproven.

## The thing worth knowing if you only read one line

A runtime seam is not "landed" when it compiles. It is landed when it is **registered**. That rule
now lives in `AGENTS.md`, is enforced at PR time by
`tests/HomesteadPatchRegistrationConformanceTests.cs`, and screams at boot from
`src/SBPR.Niflheim.HomesteadStones/Features/Diagnostics/PatchCheck.cs`.

If you are intentionally shipping a patch class that must never be registered, say so out loud with
`[DeliberatelyUnregistered("reason")]`. Silence-by-omission is what caused all three bugs.
