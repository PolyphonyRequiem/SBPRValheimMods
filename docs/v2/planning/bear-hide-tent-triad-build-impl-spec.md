---
title: "Bear Hide Tent triad — special bedroll + covered camp fire (finish the sleep-anywhere camp)"
status: proposed
purpose: "Buildable implementation spec for the two unbuilt Trailside Camp pieces (card t_439f2351, defects 2 + 3): the special bedroll (piece_sbpr_bedroll, gated Bed.CheckExposure relax + skip-night-no-spawn) and the covered camp fire (piece_sbpr_camp_fire, small Fireplace providing the Heat gate). Together with the (separately-fixed) tent collider, these complete the triad so an Explorer can sleep out on the trail. Architect spec-pass from trailside-camp.md, with the open knobs Q3-Q7 closed (Q3/Q5/Q6 resolved by grounded lean; Q4/Q7 proposed, Daniel-gated). Implementer = engineer-systems (the Bed patch is the spine)."
---

# Bear Hide Tent triad — special bedroll + covered camp fire

**Status:** PROPOSED (Daniel gates the two flagged knobs Q4/Q7 + doc-review; then →
current → implement). Architect-authored 2026-06-26 from card `t_439f2351`, graduating the
buildable half of `docs/design/trailside-camp.md` (which stays `status: living` for the art
+ time-skip seeds).
**Assignee for impl:** `engineer-systems` (owns the gated `Bed.CheckExposure` Harmony patch
+ the skip-night-no-spawn path — the spine of this feature; the camp-fire is a small
additive `Fireplace` in the same hand).
**Clean/dirty:** Clean-side (ADR-0001). Reading/adapting vanilla `Bed` / `Cover` /
`Fireplace` / `EffectArea` / `Player.SetSleeping` is base-game internals. No third-party
mod code. No RE handoff.
**Manifest/SpecCheck impact:** **+2 build pieces** — `piece_sbpr_bedroll` +
`piece_sbpr_camp_fire` added to `Runtime/SpecCheck.cs` + dataset rows in
`docs/datasets/PIECES_AND_CRAFTABLES.md`. Per AGENTS.md, code + spec + manifest move
together.
**Base branch:** `main` (where the tent + SpecCheck rows ship). Stacks logically AFTER the
collider-fit spec (`bear-hide-tent-collider-fit-impl-spec.md`) but is **independent code**
(different files) — the two impl cards can run in parallel.
**Depends on (design):** `trailside-camp.md` §3 (the grounded 5-gate sleep chain), §4
(architecture sketch), §3.1 (skip-night mechanism + the no-spawn divergence).

---

## 0. What's missing and why the camp doesn't work (verified from source)

Daniel, 2026-06-26: *"I don't see the bed."* Verified: `Features/Camp/` contains a single
file, `BearHideTent.cs`. Grep for `sbpr_bedroll` / `sbpr_camp_fire` / `AddComponent<Bed>` /
`Fireplace` across `src/SBPR.Trailborne/Features/Camp/` returns nothing. **Only 1 of the 3
triad pieces was ever built.** There is no bed to see and no fire to satisfy the vanilla
sleep gate — so even with the tent collider fixed (separate spec), there is no path to a
sleepable camp.

The design (`trailside-camp.md §3`) grounds the full vanilla sleep prerequisite —
`Bed.Interact` runs a 5-gate chain, **all must pass**:

| Gate | Vanilla requirement | Under the fixed tent + this spec |
|---|---|---|
| 1. Time (`CanSleep`) | afternoon/night + cooldown | vanilla, unchanged |
| 2. No enemies (`CheckEnemies`) | `!IsSensed()` | vanilla, unchanged |
| 3. Not exposed (`CheckExposure`) | `underRoof` **AND** `cover ≥ 0.8` | canopy gives `underRoof` ✓; cover ≈ 0.47 **FAILS** → **the bedroll's gated relax drops ONLY the 0.8 clause** |
| 4. Near fire (`CheckFire`) | bed inside a **burning** Heat area | **the covered camp fire** provides it (stays lit under canopy in rain) |
| 5. Not wet (`CheckWet`) | no Wet status | canopy keeps player dry ✓ |

So the build is exactly two pieces: the **bedroll** (relax gate 3's cover clause, drive the
skip without setting spawn) and the **camp fire** (satisfy gate 4). Everything else the
camp already satisfies natively.

---

## 1. Open knobs — RESOLVED / PROPOSED (architect closing §5 of the design)

> The design doc blocked graduation on Q3–Q7. Closing them here. Three resolve by grounded
> lean + repo consistency (architect's call); two stay flagged for Daniel because they are
> gameplay-feel / cross-doc-sequencing calls, not buildability calls.

- **Q3 — Placement tool = SPADE. 🟢 RESOLVED (architect).** The tent already ships
  Spade-placed (`Trailblazing.DoObjectDBWiring`, `m_craftingStation = null`, Pillar 1 —
  "every Spade-placed SBPR piece"). For the triad to be one coherent trail set, the bedroll
  + fire match. Splitting the camp across Spade+Hammer would be incoherent. Both new pieces:
  `m_craftingStation = null`, added to the Spade `PieceTable` "Trail" tab,
  `PieceCategory.Misc`.

- **Q5 — THREE separate pieces. 🟢 RESOLVED (architect).** Confirms the design lean. Each
  piece is independently placeable (flexible camp arrangement), independently reusable, and
  independently a future beautifier contributor. A single combo placement has **no repo
  precedent** (atomic multi-piece placement) and would be a much larger, riskier build for
  no player benefit. Three pieces, player arranges them under the tent.

- **Q6 — Bedroll KEEPS `underRoof` required. 🟢 RESOLVED (architect).** Confirms the design
  lean. The gated relax drops **only** the `cover ≥ 0.8` clause (gate 3b) — it keeps the
  `underRoof` clause (gate 3a). This is what makes the **tent load-bearing** instead of
  decorative: you cannot sleep under open sky, you must be under the canopy. It is also the
  minimal honest patch (§2 proves cover-0.8 is the *only* clause the camp can't satisfy
  natively).

- **Q4 — Camp-fire storm-proofing = ACCEPT RAIN-ONLY (placeholder). 🟡 PROPOSED, Daniel
  gates.** Under the canopy the fire survives **rain** (`underRoof` beats
  `Fireplace.CheckWet`'s rain clause) but a **high-wind storm** still douses it (canopy's
  0.47 < the 0.7 wind-cover threshold, design §3). Architect lean: accept it — it's honest
  ("a storm is a real reason you can't always camp"), free, and matches the placeholder
  ethos. **Flagged because it's a gameplay-feel call:** if Daniel wants storm-proof camping,
  the fire gets its own tight mini-roof (≥0.7 cover) — a small art/collider add, deferred to
  the real-art pass. Build the placeholder as rain-only; revisit if Daniel rejects.

- **Q7 — Inspired grant = DEFERRED, bedroll ships Inspired-READY but not wired. 🟡 PROPOSED,
  Daniel gates + 🔴 SEQUENCING CORRECTION.** The design §5-Q7 says "confirm the bedroll
  grants Inspired via the existing non-cairn-comfort hook." **That hook does NOT exist
  yet:** grep for `Inspired` / `m_raiseSkillModifier` / `Appeal` across `src/` returns
  **zero** — the `Inspired` buff lives only in `trailside-beautification.md` (`status:
  living`, ungraduated). The bedroll **cannot** wire into an unbuilt hook. So:
  - **This cut:** the bedroll grants vanilla **`SE_Rested`** for free (it rides the
    skip-wake `Player.SetSleeping(false):21464`, design §3.1.1 — "comfort is the wake
    event"). That is the "plus comfort" Daniel asked for, already satisfied.
  - **Inspired is deferred** to when `trailside-beautification.md` graduates and actually
    builds the `Inspired` buff + its "non-cairn comfort grants Inspired" hook. At that point
    the bedroll's wake is a natural grant source — a one-line addition behind the existing
    `BedrollTag`. Spec it as a **forward-reference**, not a dependency: the bedroll does not
    block on beautification, and beautification's Inspired hook will find the bedroll waiting.
  - **Flagged for Daniel:** confirm "Rested-now, Inspired-when-beautification-ships" is the
    right sequencing (vs. holding the bedroll until Inspired exists — not recommended; that
    couples a shippable camp to an unstarted system).

---

## 2. The special bedroll (`piece_sbpr_bedroll`) — engineer-systems

**Lands in:** `Features/Camp/Bedroll.cs` (+ `Features/Camp/BedrollTag.cs` MonoBehaviour for
the prefab-gate identity + the no-spawn sleep path) + one Harmony patch class
`Features/Camp/BedrollCheckExposurePatch.cs`.

### 2.1 Construction (ADR-0006 additive)
- `Assets.TryConstructPieceShell("piece_sbpr_bedroll", donor)` for the ZNetView + Piece +
  WearNTear + collider shell. Effect donor: a cloth/wood piece (e.g. the vanilla `bed` read
  as blueprint for its place/hit effects) — **do NOT `Instantiate` the vanilla `bed`** (it
  carries a `Bed` + ZNetView we'd have to strip; build additively).
- Add a **vanilla `Bed`** component (`go.AddComponent<Bed>()`) so the piece is a real bed
  (drives `AttachStart`, is recognized by the all-asleep vote). Set `m_name` to the bedroll.
- Graft a visual: placeholder = a vanilla bedroll-ish mesh read as blueprint
  (`TryGraftVisualSubtree`) — pick a low bedroll/fur donor; real art is a later swap. Seat
  it flush (foot at y=0) using the **measured** `Assets.MeasureLocalFootY` pattern (Signs/
  MarkerSigns), NOT a hand-guessed Y — same discipline the collider-fit spec applies to the
  tent (don't repeat the un-measured-seat defect).
- `m_category = Misc`, `m_craftingStation = null`, Spade-placed (Q3). Recipe: Black-Forest
  band (Bear hide + leather/wood) — PROVISIONAL, mirror the tent until Daniel locks costs.

### 2.2 The gated `Bed.CheckExposure` relax (the ONE behavioral patch)
The minimal patch surface (design §4 "Patch honesty"): a Harmony patch on
`Bed.CheckExposure` that, **gated to our prefab only**, relaxes the `cover ≥ 0.8` clause to
`underRoof`-only.

- **Gate by prefab identity, NOT a blanket patch.** The patch must no-op on every vanilla
  bed. Gate on the `BedrollTag` presence (`__instance.GetComponent<BedrollTag>() != null`)
  or the prefab name — same prefab-gate discipline as every other SBPR patch
  (`CompassHudBootstrapPatch`, the cairn patches key off `CairnTag`).
- **Relax only the 0.8 cover clause.** Keep `underRoof` required (Q6). Concretely: the
  vanilla `CheckExposure` fails if `!underRoof` ($msg_bedneedroof) OR `cover < 0.8`
  ($msg_bedtooexposed). For our bedroll: keep the `!underRoof` refusal; **skip** the
  `cover < 0.8` refusal. Gates 1/2/4/5 stay fully vanilla.
- **Patch shape:** a `Prefix` that, when our tag is present AND `underRoof` is true, forces
  the result to "not exposed" (return false / set the `out` so the vanilla method passes
  exposure) while leaving the `!underRoof` path to refuse normally. The engineer picks
  Prefix-with-skip vs Postfix-fixup based on the exact decompiled signature
  (`assembly_valheim.decompiled.cs:99677`) — flag which, and assert it wove (PatchCheck, the
  unregistered-patch lesson).

> 🔴 **Regression guard (AT-BEDROLL-VANILLA):** a vanilla bed under any 0.47-cover lean-to
> must STILL refuse to sleep ($msg_bedtooexposed). The patch is prefab-gated; vanilla beds
> are untouched. This is non-negotiable — an ungated relax is a balance change, not a camp.

### 2.3 Skip-night WITHOUT setting spawn (the design §1.4 / §3.1 divergence)
Daniel: *"Skip night only plus comfort and inspiration"* — the bedroll skips the night but
does **NOT** become your respawn point.
- Vanilla `Bed.Interact` calls `SetCustomSpawnPoint` on the claim/sleep paths
  (`:99613/:99651`). The night-skip itself (`AttachStart(isBed:true)` → `s_inBed` →
  `Game.EverybodyIsTryingToSleep` → `EnvMan.SkipToMorning`) does **not** depend on the bed
  being your spawn — it only needs the in-bed flag.
- **Therefore:** the bedroll drives `AttachStart(..., isBed:true, "attach_bed")` directly
  (via `BedrollTag`'s own interact path) and **omits** `SetCustomSpawnPoint`. Your home
  spawn is never overwritten by a trail nap.
- 🔴 **VERIFY at impl (design §3.1 flagged):** confirm the bedroll's `Bed` can drive
  `AttachStart`/`s_inBed` without auto-claiming spawn — this likely means `BedrollTag`
  calls `AttachStart` itself rather than routing through the full vanilla
  `Bed.Interact` spawn-set branch. The engineer confirms the exact wiring against the
  decomp; flag if the vanilla `Bed.Interact` can't be cleanly bypassed (fallback: let it set
  spawn and accept that divergence — but that contradicts Daniel's lock, so block for
  guidance before shipping a spawn-setting bedroll).
- **Comfort:** free — `SE_Rested` rides the skip-wake (`Player.SetSleeping(false):21464`).
  No extra work. (Inspired deferred per Q7.)

---

## 3. The covered camp fire (`piece_sbpr_camp_fire`) — engineer-systems

**Lands in:** `Features/Camp/CampFire.cs`.

### 3.1 Construction (ADR-0006 additive)
- `Assets.TryConstructPieceShell("piece_sbpr_camp_fire", donor)` shell, override
  `WearNTear.MaterialType = Wood`.
- Add a **vanilla `Fireplace`** + the **Heat `EffectArea`** that gate 4 (`CheckFire` →
  `EffectArea.IsPointInsideArea(bed.pos, EffectArea.Type.Heat)`) requires. **Strong repo
  precedent:** the Cairns feature already works with vanilla `Fireplace` / `EffectArea` /
  `FireWarmth` (`CairnTag` muzzles/keeps a Fireplace; `Assets.cs:1070-1136` copies/handles
  the `FireWarmth`/`EffectArea` subtrees). The camp fire is the **opposite** of the cairn —
  the cairn *strips* the Heat EffectArea, the camp fire *keeps* it. Reuse that machinery
  inverted: graft a small fire (e.g. read `fire_pit` as blueprint) and **retain** its
  Fireplace + Heat EffectArea + flame VFX.
- **Small** footprint (it's a camp fire, not a hearth). Tune the Heat radius so a bedroll
  placed under the same canopy sits inside it.
- `m_category = Misc`, `m_craftingStation = null`, Spade-placed (Q3). Recipe: Black-Forest
  band (wood + stone/coal) — PROVISIONAL.

### 3.2 "Covered" = under the canopy (Q4: rain-only this cut)
- The fire needs `underRoof` to survive rain (`Fireplace.CheckWet` rain clause). It gets
  that **from standing under the tent** — no per-fire roof this cut (Q4 lean (a)).
- A high-wind storm still douses it (canopy 0.47 < 0.7) — **accepted** as the placeholder
  behavior (Q4). If Daniel rejects, add the fire's own ≥0.7 mini-roof in the art pass.
- 🔴 **Fuel/lit-state:** confirm the camp fire takes fuel + lights like a vanilla fireplace
  (so "remove the fire / let it go out → `$msg_bednofire`" works for the AT below). Reuse
  vanilla `Fireplace` fuel mechanics; do not reinvent.

---

## 4. SpecCheck + dataset (the manifest move — same PR as code)

The engineer's impl PR adds, in the same commit:
- **`Runtime/SpecCheck.cs`** — two new `RecipeSpec` Piece rows after the Bear Hide Tent row
  (`:103-106`): `piece_sbpr_bedroll` and `piece_sbpr_camp_fire`, `Station = null`,
  Black-Forest resources matching each piece's `BuildResources()` (PROVISIONAL costs flagged
  the same way the tent's are).
- **`docs/datasets/PIECES_AND_CRAFTABLES.md`** — two new piece rows under the existing
  "Trailborne — Trailside Camp (Black Forest)" section (`:428`), and flip the section's
  "**Only the Bear Hide Tent ships so far**" note to "all three triad pieces ship."
- This spec PR (docs-only) updates the planning docs (this file + `index.md`/`README.md`)
  and graduates the buildable scope out of `trailside-camp.md` (which keeps the art +
  time-skip seeds as `living`).

---

## 5. Acceptance criteria (named, observable)

Logs-green ≠ playable. The sleep ATs close **only** on Daniel sleeping in the camp in-game;
AT-*-VANILLA / AT-*-BUILD are mechanical.

- **AT-BEDROLL-PLACE** — the bedroll places via the Spade menu ("Trail" tab), seats flush,
  and is visible under the tent. (Fixes "I don't see the bed.")
- **AT-CAMPFIRE-PLACE** — the camp fire places via the Spade menu, lights, and produces a
  Heat area; under the canopy it **stays lit in rain**.
- **AT-BEDROLL-SLEEP** — with the bedroll + a lit camp fire **under the fixed tent** at
  night, pressing Use on the bedroll → **sleep SUCCEEDS** (skip to morning), and your
  **respawn point is NOT changed** (design §1.4 — verify by checking your spawn is still
  home after the nap).
- **AT-BEDROLL-NOFIRE** — remove / extinguish the fire, retry → refused with
  **`$msg_bednofire`** (gate 4 intact).
- **AT-BEDROLL-WET** — step the bedroll off the canopy into rain, retry → refused with
  **`$msg_bedwet`** / **`$msg_bedtooexposed`** (gates 3a/5 intact: no sleeping under open
  sky — Q6 holds).
- **AT-BEDROLL-COMFORT** — waking from the skip grants vanilla **`SE_Rested`** (the "plus
  comfort"). (Inspired deferred per Q7 — not tested this cut.)
- **AT-BEDROLL-VANILLA (regression guard)** — a **vanilla bed** under a 0.47-cover open
  lean-to STILL refuses to sleep (`$msg_bedtooexposed`). The `CheckExposure` relax is
  prefab-gated to the SBPR bedroll only — vanilla beds untouched. 🔴 Non-negotiable.
- **AT-TRIAD-BUILD** — `dotnet build src/SBPR.Trailborne/SBPR.Trailborne.csproj -c Release`
  → **0 errors, 0 warnings**; `SpecCheck` passes with the +2 piece rows.

---

## 6. Scope

- **In:** the special bedroll (`piece_sbpr_bedroll`: vanilla `Bed` + prefab-gated
  `Bed.CheckExposure` cover-clause relax + skip-night-no-spawn path); the covered camp fire
  (`piece_sbpr_camp_fire`: small `Fireplace` + Heat `EffectArea`); both Spade-placed (Q3);
  the SpecCheck +2 rows + dataset rows; the §1 knob closures.
- **Out:** the tent **collider fix** (separate spec `bear-hide-tent-collider-fit-impl-spec.md`,
  card `t_439f2351` defect 1); **real art** (placeholder donors stay); the **Inspired** grant
  (deferred to `trailside-beautification.md` graduation, Q7); the **partial-sleep
  time-acceleration** system (design §8, a separate seed); any change to vanilla beds.
