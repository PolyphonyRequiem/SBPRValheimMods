---
title: "CLEANUP 3/3 — Null-as-value audit (design discipline, not compiler discipline)"
status: proposed
date: 2026-06-17
owner: Daniel (gates merges); engineer-systems (authored)
card: t_4364809c
purpose: "Audit the ~1100 raw `null` occurrences in src and triage them into avoidable (design smell — cut these) vs unavoidable (engine/vanilla boundary — document & keep). Drives the standing null-avoidance rule as DESIGN discipline. Routes the avoidable bulk to the structural fix (the ZdoComponent/TryGet seam) rather than an 18-file point-edit sweep, per the card's 'don't chase the number to zero dogmatically.'"
---

# Null-as-value audit

> **STATUS: PROPOSED.** This is an audit + remediation routing, not a merged sweep.
> The card's standing rule (Daniel's): drive **design** discipline, not just compiler
> discipline. The compiler half is already won — `<Nullable>enable</Nullable>` +
> `<TreatWarningsAsErrors>` means no *unannounced* null can ship. This audit is about
> the **design** half: where `null` is used *as a value* (an optional return, a sentinel,
> a deferred-init hole) versus where it's an unavoidable fact of the engine boundary.

## 1. The measured shape (whole `src`, `main`, 2026-06-17)

The headline "~1079 nulls" is real but **mostly defensive guards, not null-as-value**.
Counting method matters: there are **1274 raw occurrences** of the word `null` across
**1106 lines** (some lines hold several). The per-*line* breakdown below is what tells
you where the *design* nulls are (counts are reproducible via `grep -rE` on `src/**/*.cs`):

| Pattern | Lines | Reading |
|---|---|---|
| `== null` / `!= null` guard | **805** | Defensive null-CHECKS, overwhelmingly at the Unity/Valheim boundary (`GetComponent`, `GetZDO()`, `ZNetScene.GetPrefab`, `Player.m_localPlayer`). These are **correct** — they're how you survive ghosts/headless. Not the problem. |
| `?.` null-conditional | 92 | Same family: safe-navigation across engine refs that genuinely can be null. Mostly correct. |
| `= null;` field/local init | 67 | Mixed. Some are nullable-by-design (`string? lastAppliedText = null` — a real "not yet" state). Some are deferred-init holes better expressed another way. |
| `?? ` coalesce | 58 | Mostly fine — supplying a non-null default IS the null-avoidance pattern. |
| `return null;` | 60 | **The avoidable core.** Optional-returns and not-found sentinels — the prime target. |
| `null!` null-forgiving | 34 | Deferred-init declarations (see §3). Almost all are the **unavoidable** Unity-lifecycle category, already correctly annotated. |
| `return default` | **0** | None. (The arch review's worry about `return default` sentinels in the tags is already not present in this form.) |
| `out … = null` | 3 | Minor. |

**The key finding:** 805 of the 1106 null-bearing lines (~73%) are `== null` guards —
*correct defensive code at an engine boundary you don't control*. Chasing that number
down would make the code **worse** (you'd strip the ghost-guards that prevent the
v0.2.7-class ZDO crashes). The card says this outright: *"Don't chase the number to zero
dogmatically."* The real work is the **~60 `return null` + a slice of the 67 `= null`
inits + the sentinel-shaped subset of the guards** — call it ~120–150 genuinely-design
nulls.

## 2. Triage: avoidable (cut) vs unavoidable (document & keep)

### 2A. AVOIDABLE — design smells worth fixing

| Class | Where it concentrates | The fix (the card's named patterns) |
|---|---|---|
| **Optional-return `return null`** | `Runtime/Assets.cs` (~25 of the 60 — additive-construction helpers that return `null` on "couldn't resolve the blueprint/tool/prefab"), scattered `Find*`/`Resolve*` helpers. | `TryGet`-style `bool + out` for the not-found case, OR a null-object/empty default where the caller always proceeds. Highest-value where the caller immediately `if (x == null) return;` — that's a `Try` in disguise. |
| **ZDO sentinel reads** | The `*Tag` family (`SignTag`, `MarkerSignTag`, `SurveyorTableTag`, `CairnTag`, `AncientPortalTag`) — `ReadX()` returns `null`/`""`/`ZDOID.None` on the ghost case. | **Structural — routed to the ZdoComponent/TryGet seam (arch review P1/§3.1).** A `TryRead(key, out value)` returning `bool` replaces the sentinel. This is the single biggest avoidable-null reduction and it lands *for free* with P1, not as a point-edit. **Do not hand-sweep these — they're removed by the base-class extraction.** |
| **Deferred-init holes that aren't lifecycle** | A subset of the 67 `= null;` fields that are set later in the same class by a non-Unity path. | Constructor-init / readonly where the value is known at construction; `required` or an init method that returns the built object non-null. |

### 2B. UNAVOIDABLE — document & keep (genuinely engine-bound)

| Class | Count (approx) | Why it's unavoidable | Disposition |
|---|---|---|---|
| **Unity-injected fields** (`null!`) | ~20 of 34 | `private ZNetView nview = null!; // Unity-injected in Awake via GetComponent`. Unity constructs `MonoBehaviour`s and populates fields *after* the ctor, in `Awake`. The compiler cannot see this; `null!` is the **correct, idiomatic** annotation. | **Keep.** Already documented inline. This is the right pattern, not a smell. |
| **Static-in-Awake config/log** (`null!`) | ~14 of 34 | `internal static ManualLogSource Log = null!; // set in Awake (BepInEx guarantees Awake before any patch fires)`. BepInEx lifecycle guarantee. | **Keep.** Already documented. |
| **Vanilla API that returns null** | bulk of the 805 guards | `GetComponent<T>()`, `ZDO.GetString()`, `Player.m_localPlayer`, `ZNetScene.GetPrefab` *do* return null by contract. Guarding them is mandatory. | **Keep the guards.** This is survival code. |
| **Real "absence" state** (`string? x = null`) | a few of the 67 | e.g. `SignTag.lastAppliedText = null` genuinely means "no text applied yet" — null IS the correct domain value (nullable reference type, properly annotated `?`). | **Keep.** Correctly modeled as `T?`. |

## 3. Why the `null!` set is mostly fine (don't "fix" it)

34 `null!` looks alarming but it's the **most-correct** part of the null story. Sampled:

```
Plugin.cs:26     internal static ManualLogSource Log = null!;   // set in Awake (BepInEx guarantees Awake before any patch fires)
CairnTag.cs:40   private ZNetView nview = null!;                // Unity-injected in Awake via GetComponent
SurveyorTableTag.cs:72  private ZNetView nview = null!;         // Unity-injected in Awake via GetComponent
```

These are the canonical Unity/BepInEx deferred-init pattern. The alternative
(`ZNetView? nview` everywhere + a `!`/guard at every use) would add ~hundreds of
null-checks to satisfy the compiler for a field the lifecycle guarantees is set —
strictly worse. **Leave them.** The one nuance: `null!` *suppresses* the compiler, so if
a field is ever read *before* `Awake` it NREs at runtime. That risk is real but it's a
lifecycle-ordering bug, not a null-as-value design issue, and it's out of scope here.

The **one** `null!` worth a second look is `SpecCheck.cs:60`
(`public Req[] Resources = null!; // always set by each manifest initializer`) — that's a
data-shape that the arch review's Core `RecipeDef` model (P2) replaces with a
properly-constructed non-null array. Routed to P2, not a point-fix.

## 4. Remediation routing (the honest plan)

The card asks to *"kill the avoidable nulls and document the unavoidable ones,"* and
warns against a dogmatic zero-chase. So the plan **routes by mechanism**, not by a
find-replace count:

1. **The ZDO sentinel bulk → the ZdoComponent/TryGet seam (arch review P1/P3).**
   *Blocked on the Daniel-gated Core.* Do not hand-sweep; it's removed structurally.
   This is the largest avoidable slice and the cleanest fix. **No action on this card
   beyond naming it** (the testing work this card ships builds the seam's test home).
2. **`Assets.cs` optional-returns → `TryGet` pass (standalone, unblocked).** ~25
   `return null` in additive-construction helpers can become `bool TryX(out …)` *today*,
   independent of the Core. This is a real, mergeable null-reduction PR — but it touches
   the 22-change top-churn file, so it wants its own focused review (a separate card,
   assignee `engineer-systems`), not a smuggle-in here.

   > **✅ EXECUTED (card t_0234cc42).** Done as its own focused PR. Of the 26 `return null`
   > sites in `Assets.cs`, **six methods were converted to the `bool TryX(out …)` contract**
   > because every caller immediately branches on the result (the "`if (x == null) return;`
   > is a `Try` in disguise" case):
   > `ClonePrefab → TryClonePrefab`, `ConstructPieceShell → TryConstructPieceShell`,
   > `ConstructItemShell → TryConstructItemShell`, `GetHammerPieceTable → TryGetHammerPieceTable`,
   > `GraftVisualSubtree → TryGraftVisualSubtree`, `GraftEffectSubtree → TryGraftEffectSubtree`.
   > The `out` parameters carry `[NotNullWhen(true)]` (via a one-file net48 polyfill,
   > `Runtime/NullableAttributes.cs` — the attribute isn't in net48's BCL) so the compiler
   > flow-narrows the handle to non-null on the `true` branch under
   > `<Nullable>enable</Nullable>` + `<TreatWarningsAsErrors>`.
   >
   > **Four methods were deliberately KEPT as `T?` optional-returns**, because their callers
   > *compose* the value rather than branch-and-bail — converting would just relocate the
   > sentinel one frame up:
   > `LoadPngAsSprite` (a missing icon is a legitimate absence; every caller falls back to a
   > donor icon / `FallbackIcon` and proceeds), `GraftMeshFromBlueprint` (two callers `return`
   > it up the stack; the optional is the genuine contract), `FindOpInToolPieceTable` (the
   > blueprint is optional end-to-end — `ConstructTerrainOpPiece` takes a `GameObject?` and
   > degrades cosmetics), and `GraftTorchFire` (stored as a nullable `fireRoot` field that is
   > null-guarded at every use; null = "no flame this build" on a headless server). These match
   > §2B / §3's "real absence state, correctly modeled as `T?`" — not null-as-value smells.
   >
   > Pure refactor, no behavior change (the not-found code paths are byte-for-byte the same
   > logging + early-out); build 0 warnings / 0 errors, `dotnet test` 27/27 green. The
   > `TryX` naming also seeds the reviewable convention §5 calls for, ahead of the
   > ZdoComponent/TryGet seam.
3. **Keep + document the unavoidable set.** This audit *is* that documentation. The
   `null!` lifecycle fields and the vanilla-boundary guards are inventoried in §2B as
   intentional. No code change.
4. **`SpecCheck.cs:60` data-shape null → P2 `RecipeDef`.** Routed to the recipe-model
   card.

**Net:** the avoidable nulls are real but ~80% of them are dissolved by *structural*
work that's gated on the Core (P1/P2/P3), and the remaining unblocked slice (`Assets.cs`
TryGet) is its own churn-sensitive PR. A blind sweep on this card would (a) touch
high-churn files the architecture is about to refactor anyway and (b) risk stripping
defensive guards. The disciplined move is to **document the triage, route the fixes to
their structural homes, and not thrash the churn files pre-refactor.**

## 5. What would make this enforceable (future)

To make null-avoidance a *durable* design rule rather than a periodic audit:

- A small Roslyn analyzer or an `.editorconfig` rule flagging `return null` in
  non-`?`-annotated methods would catch new optional-return sentinels at compile time —
  the same "fail-loud at the earliest layer" move the arch review applies to recipes.
  Worth its own tooling card; not built here.
- The `TryGet` convention, once established by the ZdoComponent seam, becomes the
  reviewable standard ("new ZDO/lookup accessors return `bool + out`, not a sentinel").

## 6. Scope honesty

This card (3/3) **does not execute the null sweep.** It (a) audits and triages the
nulls, (b) documents the unavoidable set as intentional, and (c) routes the avoidable
bulk to its structural home (the gated ZdoComponent seam) + names the one unblocked
follow-up (`Assets.cs` TryGet) as a future card. That matches the card's own sequencing:
*"the testing work depends on card 2/3's engine-free core extraction — coordinate; do
the org/null/dead-code passes first where they don't need the refactor."* The null
*analysis* needs no refactor (done here); the null *fix* mostly does (routed, not forced).
