---
title: "CLEANUP 3/3 — Dead-code & low-value-code kill-list (Daniel-gated)"
status: proposed
date: 2026-06-17
owner: Daniel (gates every deletion); engineer-systems (authored)
card: t_4364809c
purpose: "A REVIEW ARTIFACT, not a merged deletion. Per AGENTS.md + the card, Daniel gates every deletion — so this is a kill-list with per-item evidence and rationale, batched by confidence. Nothing here is deleted until Daniel says cut. Each greenlit cut becomes a small, separately-reviewable PR."
---

# Dead-code & low-value-code kill-list

> **STATUS: PROPOSED — for Daniel's gate.** Nothing in this list has been deleted.
> The CLEANUP-3/3 card explicitly says *"Daniel gates deletions — produce a kill-list
> with rationale, don't silently delete."* This is that list. Each item carries the
> grep evidence behind it so the call is auditable, and a confidence tier. On a
> greenlight, each tier (or item) ships as its own small Daniel-gated PR with a
> `dotnet build -c Release` 0/0 + `dotnet test` green gate.

## Method (how "dead" was decided — and its limits)

Candidates were found by `search_files`/`grep` caller-tracing on the `main` tree
(63 .cs): a member whose only occurrences are its own declaration + doc `<see cref>`
xrefs, with **zero call sites**, is a dead candidate. This is a **static, conservative**
pass:

- It does **not** prove reachability through reflection or Harmony. Valheim mods patch
  by reflection/attribute, so a type with no C# caller may still be live (e.g. a
  `[HarmonyPatch]` class registered via `PatchAll(typeof(...))`). Every patch-class
  candidate below was cross-checked against `Plugin.cs`'s `PatchAll` list before listing.
- It does **not** include the big structural removals (the `SpecCheck` recipe-shape
  manifest, etc.) — those are **gated on the engine-free Core landing** (arch review
  P2/P6) and are explicitly *that* card's scope, not a raw delete. They are noted in
  §4 as "blocked, do not cut yet."
- **Confidence tiers:** T1 = zero callers + self-contained, safe mechanical cut.
  T2 = dead/stale but needs a judgement call (behaviour or diagnostic intent).
  T3 = stale comment / doc-rot, not code removal (cheap correctness fixes).

---

## ⚠️ Correction first: an arch-review claim that is STALE (do not act on it)

The architecture review (`docs/design/architecture-review.md` §0, §4, §9 Q4) states
`SignInteractPatch` "shipped **dead** (never registered, no compile error)" and uses it
as the motivating example for the §4 explicit-patch-registration seam.

**That is no longer true on `main`.** `Plugin.cs:391` registers it:

```
src/.../Plugin.cs:391:  harmony.PatchAll(typeof(SBPR.Trailborne.Features.Signs.SignInteractPatch));
```

`SignInteractPatch` is the **live primary entrypoint** for both Painted Signs (opens
`SignPaintPanel` on E) and Marker Signs (opens `MarkerSignPanel` on E; toggles the
`SBPR_Pinned` ZDO + projects/removes the WorldPin on Shift+E). It is load-bearing, not
dead. It was wired up at some point after the review's example was written.

**Action:** do NOT "wire it up" (arch review §9 Q4 is moot — it's already wired). The §4
explicit-`ApplyPatches` seam is still a good idea on its own merits, but its headline
example is stale and should be re-grounded on a *currently*-dead patch (there are none
right now — every `[HarmonyPatch]` class resolves to a `PatchAll` line in `Plugin.cs`)
or reframed as "prevent the NEXT dead patch" rather than "fix the existing one." Two
stale in-code comments propagate the same wrong claim and are listed as T3 below.

This is exactly the AGENTS.md RULE-5 trap (confident-wrong from memory). Flagging it so
the kill-list itself doesn't inherit the error.

---

## T1 — Safe mechanical cuts (zero callers, self-contained)

| # | Symbol | File | Evidence | Rationale |
|---|---|---|---|---|
| T1-1 | `Signs.PinTypeForColor(string)` | `Features/Signs/Signs.cs:202` | `grep PinTypeForColor` → **only the declaration**, 0 call sites. | The per-color minimap-pin mapping was the old Painted-Sign pin path; pinning is now owned by Marker Signs (`MarkerSignTag`/`WorldPins`). Superseded. |
| T1-2 | `Signs.PinTypes` dict | `Features/Signs/Signs.cs:154` | Only consumer is `PinTypeForColor` (line 204), itself T1-1. | Dies with T1-1; nothing else reads it. |
| T1-3 | `Signs.ColorForPigment(string)` | `Features/Signs/Signs.cs:168` | `grep ColorForPigment` → declaration + one `<see cref>` doc xref, **0 call sites**. (Its inverse `PigmentForColor` is live — 4 callers in `SignPaintBackend` — keep that one.) | Unused inverse helper. Note: removing it means updating the `PigmentForColor` doc-comment's `<see cref="ColorForPigment"/>` (compile-safe but tidy). |

T1 total: ~3 symbols, est. ~40–60 LOC. All in `Signs.cs` (an 18-change-churn file —
trimming dead members here directly reduces its surface). **Wire-contract safe:** none
of these are ZDO keys, prefab names, or config keys (R3) — they're pure in-memory maps.

> Verify on cut: `dotnet build -c Release` 0/0 (TreatWarningsAsErrors would catch an
> orphaned reference) + `dotnet test` green + `git grep` confirms no remaining
> reference. One PR for all of T1.

---

## T2 — Judgement-call removals (dead/stale, but Daniel's call on intent)

| # | Symbol | File | LOC | The call |
|---|---|---|---|---|
| T2-1 | `BannerDiagnostic` runtime probe | `Features/Cairns/BannerDiagnostic.cs` | 376 | Self-described **temporary diagnostic**: *"card t_7de074f3 — ATTEMPT #6 … Default ON for this diagnostic build; turn OFF (or strip the component) once the attempt-#6 rebuild lands and the failure mode is known."* It attaches to every cairn banner and logs per-frame probes. The cairn-banner work has since shipped multiple fixes (issue 10, force-vs-constraint, 2026-06-17). **Question for Daniel:** is the banner failure mode now understood enough to strip this, or is it still load-bearing for an open banner investigation? |
| T2-2 | `BannerDiagCommand` console command | `Features/Cairns/BannerDiagCommand.cs` | 55 | The `Terminal.ConsoleCommand` companion to T2-1 (a dev-only `sbpr_bannerdiag` console probe). Same lifecycle question — strip with T2-1 or keep as a live diagnostic. |
| T2-3 | `CairnTag.DefaultBannerDiagnostic = true` + `Plugin.BannerDiagnostic` config bind + the `PatchAll(typeof(BannerDiagCommand))` line | `CairnTag.cs:438`, `Plugin.cs:71/274`, `Plugin.cs:445` | ~8 | The config plumbing that defaults the diagnostic **ON in release**. Even if T2-1/T2-2 stay, **defaulting a per-frame diagnostic probe ON in a shipped build is a low-value default** — at minimum flip `DefaultBannerDiagnostic` to `false`. That alone is a safe, behaviour-preserving-for-players cut of overhead. |

T2 total (if all greenlit): ~430 LOC + the config plumbing — the single biggest
dead-weight block in the tree, and it's all in the `Cairns` slice (the 24-change
top-churn area). **Honest caveat:** T2-1/T2-2 are *diagnostic* code; if Daniel has an
open banner-cloth investigation they're cheap insurance. The unambiguous win is T2-3's
`Default… = false` regardless. **Recommend:** flip the default now (T2-3), strip the
probe (T2-1/T2-2) once Daniel confirms the banner is settled.

---

## T3 — Stale comments / doc-rot (correctness fixes, not deletions)

These aren't dead *code* — they're **wrong comments** that will mislead the next worker
(and already misled the arch review). Cheap to fix; high value because they're
load-bearing lies in a spec-first repo.

| # | Location | The rot | Fix |
|---|---|---|---|
| T3-1 | `Features/Signs/Signs.cs:153` | `"// Consumed by the (still-unregistered) SignInteractPatch pin path."` — `SignInteractPatch` **is** registered (Plugin.cs:391). And the dict it annotates (`PinTypes`) is itself dead (T1-2). | If T1-2 is cut, the comment goes with it. If not, fix "still-unregistered" → the patch is registered; this dict is the dead remnant. |
| T3-2 | `docs/design/architecture-review.md` §0/§4/§9 Q4 | The "`SignInteractPatch` shipped dead / never registered" claim (see the Correction section above). | Re-ground the §4 example or reframe it as preventing future dead patches; remove the "wire it up" framing from §9 Q4. (Doc edit — coordinate with whoever owns the living arch doc; it's a *proposed* doc so editing it is in-bounds.) |
| T3-3 | `Plugin.cs:460` | `"// Shift+E pin gesture itself rides the already-registered SignInteractPatch"` — this one is **correct** (good cross-check that 391 is the live registration). No action; listed only to show the two comments disagree with each other, which is the tell. | None — confirms T3-1 is the wrong one. |

---

## 4. Explicitly OUT of scope for a raw delete (blocked on the Core)

Do **not** delete these as part of a dead-code pass — they are scheduled structural
removals gated on the engine-free Core extraction, which is **Daniel-gated and not yet
ratified** (arch review §9 open questions):

- **`SpecCheck.cs` recipe-shape manifest** (~lines 63–200, ~140 LOC). The arch review's
  P2/P6 retire this *by replacing it with a Core registry* — it is a refactor with a
  safety-net demotion, not a delete. Cutting it now removes the only drift guard.
- **Any `*Tag` ZDO accessor boilerplate.** The duplication is real (arch review §3.1)
  but it's removed by *extracting a base class* (P1/P3), not by deleting code. A raw
  delete would orphan ZDO wire contracts (R3).

These belong to the architecture-execution cards (P1/P2/P3/P6), sequenced after Daniel
ratifies the Core packaging (§9). Listed here only so they're not double-counted as
"dead code" — they're *un-extracted* code, which is a different fix.

---

## 5. What this pass deliberately did NOT do

- **No exhaustive 63-file member-by-member sweep.** A full unused-member audit wants a
  Roslyn analyzer (IDE0051/CS0169) run, not hand-grep — that's a tooling task worth its
  own card. This list is the **high-confidence, hand-verified** subset, which is what a
  Daniel-gated kill-list should be (every item defensible, no false positives shipped).
- **No deletions.** Per the card + AGENTS.md. Greenlight any tier/item and it becomes a
  small PR.

## 6. Recommended order if greenlit

1. **T3** (comment/doc fixes) — zero risk, removes the misinformation that's already
   cost a wrong arch-review claim.
2. **T2-3** (`DefaultBannerDiagnostic = false`) — one-line, kills per-frame release
   overhead, fully reversible.
3. **T1** (dead Signs helpers) — one small PR, build+test gated.
4. **T2-1/T2-2** (strip the diagnostic probe) — once Daniel confirms the banner work is
   settled.
