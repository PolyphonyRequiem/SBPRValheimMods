---
title: "Trailborne config bake-down + diagnostic-flag cleanup — classification spec-pass (card t_f87361cf)"
status: proposed
purpose: "Architect spec-pass that classifies every live BepInEx Config.Bind knob in SBPR.Trailborne into three buckets — (A) developer diagnostics to remove/default-OFF, (B) tune-then-bake provisionals to freeze into code constants once Daniel LOCKS each value, (C) legit player/server config to keep live — and proposes the root-cause fix for the stale-default-drift UX wart (a bumped code default silently masked by an old .cfg value). Grounded against the COMPLETE 50-knob inventory on main @ 7769812 (NOT the card's ~54 estimate, which predates the #239 Ring→Halo swap). Corrects three load-bearing premises in the card: (1) SpecCheck.cs has NO config manifest to sync — it is a recipe/piece/icon/attack drift watchdog only; (2) SBPR_BannerDiagnostic is listed in Bucket A but sits inside the hard-excluded [CairnBanner] block and is the live instrument of windsock attempt #6 — it must stay live, not be removed; (3) Bucket B is nearly EMPTY right now because almost every provisional is still being actively tuned (Halo* from #239 just shipped; the compass feel's in-game accept is still pending) — you cannot bake a value that isn't locked. This doc PROPOSES the classification and the bind-removal strategy; Daniel gates which knobs lock + the locked figures + the kept-live migration decision before any value is frozen. Decomposes into an engineer-ui/engineer impl card. Authored by the architect spec-pass; Daniel gates the merge. DEFERRED-card unblocked post-Playtest-#5."
---

# Trailborne config bake-down — classification spec-pass

This is the **architect spec-pass** the deferred card `t_f87361cf` calls for: it
classifies every live config knob, proposes the root-cause fix for the stale-default
drift, and surfaces the decisions **Daniel must gate before any value is frozen**. It
is **not** a buildable HOW yet — the buildable bind-removal + spec-sync lands in the
engineer impl card cut from this one, *after* Daniel locks the classification and the
Bucket-B figures.

Grounded against **`main` @ `7769812`** (`origin/main` HEAD at authoring; the card was
written against a working tree that predates the #239 Ring→Halo swap, hence its
"~54 knobs" estimate — the **verified live count is 50**, all in `src/SBPR.Trailborne/Plugin.cs`).

Relates to `architecture-review.md` Model D (trinket/charged-accessory) — the Sunstone
Lens + Iron Compass charge/feel knobs are that model's config surface.

---

## §0 — The verified inventory (50 knobs, 1 file)

Every `Config.Bind` lives in `Plugin.Awake` (`Plugin.cs`); **zero binds elsewhere**
(verified). Sectioned exactly as they bind:

| # | Section | Key | Default (source) |
|---|---------|-----|------------------|
| 1 | `Debug` | `SBPR_DebugCairnDamage` | `true` (literal) |
| 2 | `Cairns` | `SBPR_CairnDecayHpPerDay` | `Cairns.DefaultDecayHpPerDay` = 10 |
| 3 | `CairnBanner` | `SBPR_BannerTailLength` | `DefaultBannerDropY` = 1.15 |
| 4 | `CairnBanner` | `SBPR_BannerWidth` | `DefaultBannerWidthZ` = 0.18 |
| 5 | `CairnBanner` | `SBPR_BannerMountHeight` | `DefaultBannerMountHeight` = 0.20 |
| 6 | `CairnBanner` | `SBPR_BannerOffsetXZ` | `DefaultBannerOffsetXZ` = 0.30 |
| 7 | `CairnBanner` | `SBPR_BannerWindMult` | `DefaultBannerWindMult` = 1.0 |
| 8 | `CairnBanner` | `SBPR_BannerWindRandomFactor` | `DefaultBannerWindRandomFactor` = 0.25 |
| 9 | `CairnBanner` | `SBPR_BannerClothDamping` | `DefaultBannerClothDamping` = 0.10 |
| 10 | `CairnBanner` | `SBPR_BannerStretchStiffness` | `DefaultBannerStretchStiffness` = 0.5 |
| 11 | `CairnBanner` | `SBPR_BannerBendStiffness` | `DefaultBannerBendStiffness` = 0.5 |
| 12 | `CairnBanner` | `SBPR_BannerTailFreedom` | `DefaultBannerClothFreeDistance` = 3.0 |
| 13 | `CairnBanner` | `SBPR_BannerFreedomRampExp` | `DefaultBannerFreeRampExp` = 1.0 |
| 14 | `CairnBanner` | `SBPR_BannerMountPinBandFrac` | `DefaultBannerPinBandFrac` = 0.04 |
| 15 | `CairnBanner` | `SBPR_BannerUseGravity` | `DefaultBannerUseGravity` = true |
| 16 | `CairnBanner` | `SBPR_BannerSubdivisions` | `DefaultBannerSubdivisions` = 1 |
| 17 | `CairnBanner` | `SBPR_BannerRockDrape` | `DefaultBannerRockDrape` = true |
| 18 | `CairnBanner` | `SBPR_BannerTiltDegrees` | `DefaultBannerTiltDegrees` = 0 |
| 19 | `CairnBanner` | `SBPR_BannerAlignToWind` | `DefaultBannerAlignToWind` = true |
| 20 | `CairnBanner` | `SBPR_BannerAlignMode` | `DefaultBannerAlignMode` = 0 |
| 21 | `CairnBanner` | `SBPR_BannerDiagnostic` | `DefaultBannerDiagnostic` = **true** |
| 22 | `Cartography` | `SBPR_EnforceNoMap` | `true` (literal) |
| 23 | `SunstoneLens` | `MaxCharge` | `DefaultMaxCharge` = 100 |
| 24 | `SunstoneLens` | `DrainPerSecond` | `DefaultDrainPerSec` = 0.33 |
| 25 | `SunstoneLens` | `ChargePerSecond` | `DefaultChargePerSec` = 1.0 |
| 26 | `SunstoneLens` | `DetectRadius` | `DefaultDetectRadius` = **50** (#234, was 30) |
| 27 | `SunstoneLens` | `DetectIntervalSeconds` | `DefaultDetectInterval` = 0.5 |
| 28 | `SunstoneLens` | `ClearWeatherNames` | `""` (literal) |
| 29 | `SunstoneLens` | `HaloRadiusMin` | `DefaultHaloRadiusMin` = 1.2 |
| 30 | `SunstoneLens` | `HaloRadiusMax` | `DefaultHaloRadiusMax` = 3.0 |
| 31 | `SunstoneLens` | `HaloScaleMax` | `DefaultHaloScaleMax` = 0.6 |
| 32 | `SunstoneLens` | `HaloScaleMin` | `DefaultHaloScaleMin` = 0.12 |
| 33 | `SunstoneLens` | `HaloEyeOffsetY` | `DefaultHaloEyeOffsetY` = 0 |
| 34 | `SunstoneLens` | `RingMaxIcons` | `DefaultRingMaxIcons` = 12 |
| 35 | `SunstoneLens` | `ShowEmptyRing` | `DefaultShowEmptyRing` = true |
| 36 | `SunstoneLens` | `ShowDepletedHint` | `DefaultShowDepletedHint` = false |
| 37 | `SunstoneLens` | `DebugTextReadout` | `DefaultDebugTextReadout` = false |
| 38 | `SunstoneLens` | `DebugMount` | `DefaultDebugMount` = **true** |
| 39 | `SunstoneLens` | `DumpUnmappedCreatures` | `DefaultDumpUnmappedCreatures` = **true** |
| 40 | `SunstoneLens` | `MinimapHandoffMode` | `MinimapHandoffMode.DiscWhenBound` |
| 41 | `SunstoneLens` | `MinimapBlipStyle` | `BlipStyle.Trophy` (#238, was Dots) |
| 42 | `IronCompass` | `NeedleLag` | `DefaultNeedleLag` = 8 |
| 43 | `IronCompass` | `MaxTiltDegrees` | `DefaultMaxTilt` = 45 |
| 44 | `IronCompass` | `Anchor` | `CompassAnchor.TopCenter` |
| 45 | `IronCompass` | `SizePx` | `DefaultSize` = 140 |
| 46 | `IronCompass` | `OffsetXPx` | `DefaultOffsetX` = 0 |
| 47 | `IronCompass` | `OffsetYPx` | `DefaultOffsetY` = −94 |
| 48 | `IronCompass` | `DebugMount` | `DefaultDebugMount` = **true** |
| 49 | `IronCompass` | `DiscMode` | `CompassDiscModeEnum.DiscWhenBound` |
| 50 | `IronCompass` | `AutoNorthUp` | `false` (literal) |

The `[CairnBanner]` block is **19 knobs** (#3–#21), not 18 — the card's "18" counts
the tuning knobs and parks `SBPR_BannerDiagnostic` separately in Bucket A. See §5.2 for
why that split is wrong.

---

## §1 — The two harms (verified)

**Harm #1 — stale-default drift silently suppressed shipped changes.** BepInEx
`Config.Bind(section, key, default, …)` uses the *saved* `.cfg` value whenever the key
already exists in the file; the `default` argument is written **only when the key is
absent**. So when a code PR bumps a default, every existing player's `.cfg` keeps the
old value and the new default never reaches them. Verified instances:

- **`DetectRadius`** — `DefaultDetectRadius` is `50f` on main (#234, `t_4b9f8889`,
  merged 2026-06-22), but Daniel's `.cfg` pinned `30`, hiding the buff.
- **`MinimapBlipStyle`** — default is `BlipStyle.Trophy` on main (#238, `t_aab051ae`,
  merged 2026-06-22), but Daniel's `.cfg` pinned `Dots`, hiding the richer blip.

Net effect: a correct `v0.2.34` build "felt like an older build." This is the
root-cause UX wart, not a symptom.

**Harm #2 — diagnostic spam.** `SunstoneLens/DebugMount = true` emitted 7,745
`LensHud: content → VISIBLE` lines in one session. **Sharp nuance (verified at
`SunstoneLensHudOverlay.cs:218`):** that line is guarded by an
`activeSelf == on` early-return, so it logs **only on a visibility transition**, not
per-frame. 7,745 transition logs ⇒ the overlay visibility is **thrashing on/off every
frame** — a latent render-stability signal, not mere verbosity. Removing the knob
silences the spam **and** removes the only in-log evidence of that thrash. Flag for the
engineer: when this diagnostic is removed, confirm the thrash itself is understood or
file a separate bug (`AT-CFG-A4`). Same shared-pump bug family as `IronCompass/DebugMount`.

---

## §2 — Root-cause analysis of harm #1

The card frames a per-knob sub-decision: for **baked** knobs, remove the `Config.Bind`
so no stale `.cfg` can mask the constant; for **kept-live** knobs whose default
changed, decide whether a migration/reset note is warranted. The architectural truth is
sharper than that framing:

> **The two knobs that actually caused harm #1 — `DetectRadius` and
> `MinimapBlipStyle` — are the two whose "keep live" (Bucket C) classification is most
> questionable.** They drifted *because Daniel changes their default via a code PR and
> expects the code default to win*. That is the behavior of a **baked constant**, not a
> live config knob. A knob is only legitimately live if a **player or server operator**
> is meant to override it; if the only actor who ever changes it is Daniel-via-PR, the
> live binding is pure liability — it adds a `.cfg` surface whose sole effect is to let
> a stale value mask the very change he shipped.

So harm #1 is not only a migration problem; it is partly a **mis-classification**
problem. The durable fix splits the kept-live population by *who is the intended tuner*:

- **Daniel-via-PR knobs** (default changes ship as code; no player is meant to pin them)
  → **bake them** (Bucket B). Removing the bind removes the drift surface entirely.
  Airtight. This is the recommended home for `DetectRadius`, `MinimapBlipStyle`, and
  `MinimapHandoffMode` **if** Daniel confirms they are not server-operator knobs.
- **Genuine player/server knobs** (HUD placement, charge economy, server toggles) →
  keep live, and accept that an explicit player choice persists (that is *correct*). For
  these the drift risk is low because their **defaults rarely change**. If Daniel still
  wants code-default-wins for untouched keys here, that requires a `ConfigVersion`
  migration (§4.2) — a net-new mechanism, scoped as a follow-up, **not** folded into
  this cleanup.

---

## §3 — The three-bucket classification (proposal)

### Bucket A — diagnostics to remove or default-OFF

| Knob | Default | Recommendation | Rationale |
|------|---------|----------------|-----------|
| `SunstoneLens/DebugMount` (#38) | true | **REMOVE bind**, hard-code `DefaultDebugMount=false` const | Pure dev instrumentation; per-transition spam (§1). A stale `=true` must not resurrect it. Keep the const (false) so the gated log lines stay compilable but inert. |
| `IronCompass/DebugMount` (#48) | true | **REMOVE bind**, const→false | Same pump-diagnostic family; render bug fixed (#208/#209). |
| `SunstoneLens/DumpUnmappedCreatures` (#39) | true | **Flip default false** (keep bind) — *not* remove | One-shot startup dump (NOT per-frame), and it has ongoing utility: grow the variant→sibling remap table over time. Daniel still wants to invoke it occasionally → keep it as an opt-in, default OFF. |
| `SunstoneLens/DebugTextReadout` (#37) | false | **No change** (already OFF) | Card notes this; leave as a debug opt-in. |
| `Debug/SBPR_DebugCairnDamage` (#1) | true | **Daniel-gated** — flip false OR remove | ⚠️ **NOT pure logging** — Shift+E drops a pristine cairn to 70% HP (`CairnInteractable.cs:55`). Removing it deletes a *playtest gesture*, not a log line. Its own comment says "flip false (or remove) once decay tuning lands" — decay *has* landed (`SBPR_CairnDecayHpPerDay` live). Recommend **flip default false** (keep the gesture for opt-in testers) rather than remove. |

**`SBPR_BannerDiagnostic` (#21) is deliberately NOT in Bucket A** — see §5.2. It stays
live with the rest of the windsock block.

### Bucket B — tune-then-bake provisionals (bake ONLY on Daniel's lock)

> **This bucket is nearly empty right now, by design.** You cannot bake a value that
> isn't locked, and almost every provisional is *still being actively converged*. The
> bake-down the card anticipates is real for the diagnostics (A) and the drift fix (the
> §2 re-classification), but the literal "freeze the tuning knobs" set is small and
> **contingent on Daniel confirming each feel is locked.**

| Knob | Default | Lockable now? | Note |
|------|---------|---------------|------|
| `IronCompass/NeedleLag` (#42) | 8 | ⚠️ only if compass feel locked | Compass HUD render fix's *in-game accept is still pending Daniel pulling a fresh cut* (PIECES_AND_CRAFTABLES §Iron Compass Status). If the feel isn't accepted, the lag isn't locked. |
| `IronCompass/MaxTiltDegrees` (#43) | 45 | ⚠️ same gate as NeedleLag | Design §8 "~45°". Bake with NeedleLag or not at all. |
| `SunstoneLens/Halo*` (#29–#33) | see §0 | ❌ **NO** | World-space halo is the **newest** surface (#239, `t_68672b6b`, design just merged; trophy-ring impl-spec is still `proposed`). Feel is unconverged. **Leave live.** |
| `SunstoneLens` legacy ring (`RingMaxIcons`/`ShowEmptyRing`/`ShowDepletedHint`, #34–#36) | see §0 | low value | Carry-overs; baking buys little. Defer. |
| Drift victims `DetectRadius`/`MinimapBlipStyle`/`MinimapHandoffMode` (#26/#41/#40) | see §0 | ✅ **recommended** (see §2) | If Daniel confirms these are Daniel-via-PR knobs (not server-operator knobs), **bake them** — this is the airtight half of the harm-#1 fix. |

For every knob Daniel **does** lock: the bake is (a) set the `Default*` const to the
locked figure, (b) **remove the `Config.Bind`**, (c) the existing read-sites already
fall back to the const via `Plugin.X?.Value ?? DefaultX` — once the bind is gone the
`?.Value` is always null and the const wins (no read-site edit needed; verified the
pattern at `SBPR_CompassHud.cs:326/332`, `SunstoneLens.cs:461/462`, `Registrar.cs:102`).

### Bucket C — keep live (legit player/server config)

| Knob(s) | Why live |
|---------|----------|
| `SBPR_CairnDecayHpPerDay` (#2) | Server-tunable decay economy. |
| `SunstoneLens` charge economy: `MaxCharge`/`DrainPerSecond`/`ChargePerSecond`/`DetectIntervalSeconds` (#23–#25, #27) | Server-operator-tunable "top up in the open, spend in the dark" rhythm. |
| `SunstoneLens/ClearWeatherNames` (#28) | Server allowlist escape hatch. |
| `IronCompass` HUD placement: `Anchor`/`SizePx`/`OffsetXPx`/`OffsetYPx` (#44–#47) | Player places their own HUD widget. |
| `IronCompass/DiscMode`, `AutoNorthUp` (#49–#50) | Live feel enums (the banner-windsock pattern); `AutoNorthUp` is also the M2 hook. |
| `Cartography/SBPR_EnforceNoMap` (#22) | Server toggle (the escape hatch). |
| **Entire `[CairnBanner]` block (#3–#21)** | 🔴 **HARD EXCLUSION** — windsock attempt #6 in progress. See §5.2. |
| `DetectRadius`/`MinimapBlipStyle`/`MinimapHandoffMode` (#26/#41/#40) | **Only if** Daniel says server operators tune these. Otherwise → Bucket B (§2). This is the load-bearing gate. |

---

## §4 — Root-cause fix for harm #1

### §4.1 Baked knobs (airtight)
Removing the `Config.Bind` deletes the `.cfg` key, so there is no stale value to mask
the constant. This is the **complete** fix for every knob that moves to Bucket B. No
migration needed — the drift surface ceases to exist.

### §4.2 Kept-live knobs whose default changed (the honest fork)
For Bucket C knobs you cannot remove the bind (a player/server must override them), so
"code default always wins" and "player can override" are in **direct tension** for the
same key. Options, with trade-offs:

- **(a) Accept it (do nothing).** A player's `.cfg` value persists. *Correct* when the
  player deliberately set it; *wrong* when BepInEx auto-wrote an old default the player
  never chose. Zero code. Recommended for knobs whose defaults essentially never change.
- **(b) `ConfigVersion` migration.** A `[Meta] ConfigVersion` stamp; on a version bump,
  reset the managed keys to code defaults. 🔴 **This clobbers genuine player
  customizations** unless it tracks per-key "was this changed from the prior default"
  — bookkeeping that rots. A net-new feature, **separate card**, not this cleanup.
- **(c) One-time reset note.** Tell players to delete the `.cfg` after a major default
  change. Heavy-handed; loses real customizations. Reject.

**Recommendation:** the durable fix for the *observed* harm is §2 — move the drift
victims (`DetectRadius`, `MinimapBlipStyle`) to Bucket B and bake them, which makes the
drift impossible by construction. The remaining Bucket C knobs take option **(a)**;
only escalate to a `ConfigVersion` migration (b) if Daniel later finds a genuine
player-facing knob whose default he needs to push across a version boundary.

---

## §5 — Corrections to the card's premises (load-bearing)

### §5.1 SpecCheck.cs has NO config manifest to sync
The card's Acceptance says "update the relevant `docs/**` config sections **+
`SpecCheck.cs` manifest** in the SAME PR." **Verified false premise:** `SpecCheck.cs`
is a **recipe / build-piece / icon / attack** drift watchdog. It contains **zero**
config-knob assertions (grep for `config|knob|DebugMount|Banner` in it returns nothing
relevant). There is nothing config-related in `SpecCheck.cs` to sync. The "spec + code
move together" rule still applies — but the spec surface for config knobs is the
**embedded config rows in the feature docs** (§5.4), not `SpecCheck`.

> **Optional follow-up (Daniel's call, out of scope here):** if you want a *config*
> drift watchdog analogous to the recipe one — e.g. a boot-time assert that the live
> `Config.Bind` set matches a manifest, so a future un-baked provisional or
> resurrected diagnostic screams — that is a *net-new* `ConfigCheck.cs` sibling, a
> separate card. It is the natural home for "SpecCheck for config" the card seems to
> imagine, but it does not exist today and should not be silently invented inside a
> cleanup PR.

### §5.2 `SBPR_BannerDiagnostic` — the card contradicts itself
The card lists `SBPR_BannerDiagnostic` in **Bucket A (remove/default-OFF)** but also
declares the **entire `[CairnBanner]` block a HARD EXCLUSION** and cites
`SBPR_BannerDiagnostic = true, attempt #6` as the *evidence* the block is live.
`SBPR_BannerDiagnostic` (#21) binds inside `[CairnBanner]` and is the **live instrument
of the ongoing windsock investigation**. Removing it mid-investigation is
self-defeating. **Resolution: it stays live with the rest of the block** — it is NOT a
Bucket A item until Daniel locks the windsock feel and the whole block bakes together.

### §5.3 The count is 50, not ~54
Verified 50 live binds on main @ `7769812`. The card's "~54" predates #239, which
**replaced** the four screen-space ring knobs (`RingRadiusPx`/`RingCenterOffsetY`/
`RingIconMinPx`/`RingIconMaxPx`) with the five `Halo*` knobs (#29–#33). No knob is
missing from this pass; the delta is the Ring→Halo swap plus estimate slack.

### §5.4 The config-doc surface (what the impl card actually syncs)
There is **no central config reference doc**. Knob rows are embedded in feature docs;
the impl card must keep these in lockstep with the code change:
- `docs/datasets/PIECES_AND_CRAFTABLES.md` — `| Config |` rows for the Lens (`:402`) and
  Compass (`:421`), and the Lens render row (`:399`).
- `docs/v3/planning/iron-compass-impl-spec.md` — NeedleLag/MaxTilt/DebugMount bake notes
  (`:105`, `:322–325`).
- `docs/design/sunstone-lens-trophy-ring.md` — DebugMount/DumpUnmapped/DebugTextReadout
  rows (`:600–602`, `:407`, `:412`).
- `docs/v3/planning/sunstone-minimap-handoff-impl-spec.md` — the `MinimapBlipStyle` bind
  snippet (`:264`).
Any knob that moves bucket (removed, default-flipped, or baked) updates its row in the
doc(s) above in the **same PR** (AGENTS.md spec+code rule).

---

## §6 — Named acceptance criteria (for the impl card)

- **AT-CFG-A1** — `SunstoneLens/DebugMount` + `IronCompass/DebugMount` binds removed;
  their `DefaultDebugMount` consts are `false`; the gated log lines compile and stay
  inert. A fresh `.cfg` has no `DebugMount` key in either section.
- **AT-CFG-A2** — `DumpUnmappedCreatures` default is `false`, bind retained (opt-in).
- **AT-CFG-A3** — `SBPR_DebugCairnDamage` resolved per Daniel's §7-Q4 gate (flip-false
  or remove); if removed, the Shift+E 70%-HP gesture is gone and its docs note that.
- **AT-CFG-A4** — the `LensHud` visibility-thrash that produced 7,745 transition logs is
  either explained in the PR or filed as a separate render bug (the diagnostic removal
  must not bury an unexplained thrash).
- **AT-CFG-B1** — for every knob Daniel LOCKS (§7-Q1/Q2): the `Default*` const equals
  the locked figure exactly, the `Config.Bind` is removed, and the read-site fallback
  (`?.Value ?? Default*`) resolves to the const. Zero behavior change vs the locked value.
- **AT-CFG-C1** — every Bucket C knob is byte-for-byte unchanged (bind, default,
  range-clamp, description). Zero behavior change.
- **AT-CFG-BANNER-EXCL** — the entire `[CairnBanner]` block (#3–#21, including
  `SBPR_BannerDiagnostic`) is untouched.
- **AT-CFG-DOCSYNC** — every knob that changed bucket has its embedded config-doc row
  (§5.4) updated in the same PR; `python3 scripts/docs-lint.py` is green.
- **AT-CFG-BUILD** — `dotnet build … -c Release` is **0 errors / 0 warnings** (TWAE on);
  unit tests green.

---

## §7 — Gates for Daniel (decide before any impl)

1. **Q1 — Which drift victims bake?** Are `DetectRadius`, `MinimapBlipStyle`,
   `MinimapHandoffMode` **server-operator** knobs (keep live, Bucket C) or
   **Daniel-via-PR** knobs (bake, Bucket B — the airtight harm-#1 fix, §2)?
   *Architect lean: bake all three* unless a server use-case exists.
2. **Q2 — Compass feel locked?** Is the Iron Compass in-game feel **accepted**? If yes,
   confirm the locked `NeedleLag` (=8?) and `MaxTiltDegrees` (=45?) to bake. If not,
   they stay live and Bucket B is *empty this pass*.
3. **Q3 — `SBPR_DebugCairnDamage`:** flip default `false` (keep the opt-in Shift+E
   gesture) or remove entirely? *Architect lean: flip false.*
4. **Q4 — Kept-live migration:** accept option §4.2(a) (player `.cfg` persists) for the
   remaining Bucket C knobs, or do you want a `ConfigVersion` migration scoped as a
   **separate** follow-up card? *Architect lean: (a) now; migration only if a real
   player-facing default later needs to cross a version boundary.*
5. **Q5 — Optional `ConfigCheck.cs`:** do you want a config-drift watchdog sibling to
   `SpecCheck` (a separate card), now that we've established `SpecCheck` has no config
   manifest? *Architect lean: defer; nice-to-have, not this cleanup.*
6. **Q6 — Windsock confirm:** confirm `[CairnBanner]` (incl. `SBPR_BannerDiagnostic`)
   stays fully live until the windsock locks (per the card's own hard exclusion). This
   overrides the card's stray listing of `SBPR_BannerDiagnostic` in Bucket A.

---

## §8 — Decomposition

After Daniel gates §7, cut **one** impl card (assignee `engineer-ui` — the touched
read-sites are all HUD/render features; `engineer` if Daniel prefers):

> **"REFACTOR(impl): config bake-down — remove Bucket-A diagnostics, bake Daniel-locked
> Bucket-B knobs, sync config-doc rows"** — execute the locked classification: remove
> the two `DebugMount` binds (const→false), flip `DumpUnmappedCreatures` default false,
> resolve `SBPR_DebugCairnDamage` per Q3, bake the Q1/Q2-locked knobs (const = locked
> figure + remove bind), leave all of Bucket C and the entire `[CairnBanner]` block
> untouched, update the §5.4 embedded config-doc rows in the same PR. Acceptance = §6
> AT-CFG-* all green; build 0/0; docs-lint green. Daniel gates the PR merge.

No `SpecCheck.cs` change (§5.1). No new prefab (ADR-0006 N/A). Clean-side (ADR-0001).
