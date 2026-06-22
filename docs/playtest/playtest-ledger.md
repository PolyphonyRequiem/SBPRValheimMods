---
title: "SBPR Trailborne — Playtest Ledger"
status: living
purpose: >
  Durable, accumulating record of what needs in-game testing. Items land here as
  work merges (no reliance on memory). When a playtest is prepped, scripts/gen-playtest-guide.py
  rolls the PENDING section + git-derived code changes into a numbered Playtest #N testers guide,
  then the shipped items move to the ARCHIVE section under that playtest number.
playtest_counter: 6            # next playtest is #6 (human-facing series; DISTINCT from vX.Y.Z-playtest tags)
last_playtest_tag: v0.2.34-playtest
---

# SBPR Trailborne — Playtest Ledger

This is the single source of truth for **what needs to be tested next**. It is
deliberately decoupled from the `vX.Y.Z-playtest` git tags: those are *build*
markers; the **Playtest #N** counter here is the *human-facing* testing series.

## How this stays reliable (read before editing)

1. **Items are added as work merges, not from memory.** Two feeders:
   - **Auto (ground truth):** `scripts/gen-playtest-guide.py` derives candidate items
     from `git log <last_playtest_tag>..main -- 'src/**/*.cs'` — every code change since
     the last playtest is a candidate test item, even if nobody added it by hand.
   - **Manual (judgment):** specific test instructions a commit message can't capture
     (exact steps, what "correct" looks like, edge cases) go in **PENDING** below.
     Kanban workers append here on a review-required handoff; the orchestrator appends
     on merge.
2. **The cron `sbpr-playtest-planner` fires when a new `-playtest` tag lands** → it
   archives PENDING under the shipped Playtest #N, bumps `playtest_counter`, updates
   `last_playtest_tag`, and creates the next-playtest **planning card** on the board.
   Items that did **not** actually ship in the tag (still `todo`/`blocked`/unmerged) are
   **not** archived as shipped — they carry forward into the next PENDING.
3. **Nothing is "tested" until Daniel says so in-game** (logs-green ≠ playable). The
   guide produces a checklist; Daniel's run fills it in.

---

## PENDING — accrues for the next playtest (Playtest #6)

> Build target: **`v0.2.34-playtest`** (SBPR Trailborne 0.2.34) — the build that carries Playtest #6.
> Items **1–6** are the **Daniel-feedback fixes** that merged to `main` and shipped in `v0.2.34-playtest`
> (cut 2026-06-22 08:38 PDT) **after Daniel's Playtest #5 run on the `v0.2.33` client**. Test **local solo**
> on a fresh client build unless an item says otherwise.
>
> _Provenance: every item below traces to a Daniel report from the Playtest #5 run — the compass rim read the
> wrong colour (#236), the N-glyph hid behind the bezel (#233), the Lens minimap wanted the richer HUD read
> (#238), the detection radius wanted 50 m (#234), the modal cursor still snapped (#237), and the standalone
> Lens ring was re-locked to a world-space head-halo (#242). The seventh row (Portal Seed) is carried forward
> unverified — it shipped no code change, so it is **not** archived as a #5 surface._

### 🆕 Daniel-feedback fixes merged since `v0.2.33` — ship in `v0.2.34-playtest` (Playtest #6 build)

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 1 | **Iron Compass equipped-rim → neutral medium grey `#6B6B6B`** | t_540ace8c (#236) | ✅ merged to `main` (`e4c2100`); ships in `v0.2.34`; awaiting Daniel | Worn Iron Compass + an SBPR map surface showing → the equipped bezel **rim now reads as a neutral medium grey ≈ `#6B6B6B`** (RGB 107/107/107), **not** the prior muddy dark brown-grey (`#685F4D`). Root cause: `CIronTint` **multiplies** the warm bronze-baked bezel, so the old `(0.66,0.68,0.72)` constant landed dark; retuned to `(0.677,0.764,1.0)` so tint × base = neutral grey. **AT-COMPASS-RIM-COLOR** — Daniel's GPU eyeball is the accept (an explicit *tune-by-eye against a real iron item* tunable; capped at RGB ≤ 107 by the base's blue channel — a lighter neutral needs the shared base lifted). Unworn bezel (`Color.white`) unchanged. logs-green ≠ playable — closes t_540ace8c. |
| 2 | **Map-surface N-glyph + cardinal ticks lifted in front of the iron bezel** | t_3f7f3a0f (#233) | ✅ merged to `main` (`a6d9527`); ships in `v0.2.34`; awaiting Daniel | Worn compass + map surface → the orbiting **N-glyph + E/S/W cardinal ticks now render in front of the iron bezel band** (on the rim), no longer occluded behind it. Fix gave `_northLayer` its own nested Canvas (`overrideSorting`, `sortingOrder = SortingOrder+1`) so it lifts above the bezel while still riding `+rotZ` for the orbit (surface-relative +1, so disc N=3001 stays below the modal surface=5000). **AT-COMPASS-N-ZORDER** — confirm N + ticks sit **on** the bezel on both the carry-disc **and** the (M) modal; turn N→E→S→W and the N still orbits to true map-north with ticks following. Daniel's eye is the accept. logs-green ≠ playable — closes t_3f7f3a0f. |
| 3 | **Sunstone Lens minimap representation richened — trophies + tint + stars + off-edge rim** | t_aab051ae (#238) | ✅ merged to `main` (`70ab72b`); ships in `v0.2.34`; awaiting Daniel | Equip + solar-charge the Lens, approach hostiles **with a minimap present** (carry-disc or vanilla corner). The minimap detection overlay now matches the HUD ring's richness: **(a)** `MinimapBlipStyle` default flips **Dots→Trophy** (aggro-tinted trophy art; Dots still selectable in Config); **(b)** **star pips** appear above each blip (level-1 hostiles show none), aggro-tinted; **(c)** **off-window threats are clamped to the bezel rim** and drawn smaller instead of dropped (new `BoundedMapMath.ClampToRimPx`); **(d)** the aggro tint rides the trophy. **AT-LENS-MINIMAP-RICH** on the disc **and** the vanilla corner. Supersedes the #5 handoff's *dots / no stars / no rim* representation (spec §5 knob-2 re-locked 2026-06-21). Build 0/0, tests 231/231; render is GPU-only — Daniel's look is the accept. logs-green ≠ playable — closes t_aab051ae. |
| 4 | **Sunstone Lens detection radius 30 m → 50 m** | t_4b9f8889 (#234) | ✅ merged to `main` (`dd680eb`); ships in `v0.2.34`; awaiting Daniel | Equip the Lens; confirm hostile **detection now reaches 50 m** (was 30 m) on **all three** surfaces (HUD head-halo, carry-disc, vanilla-minimap handoff) — single knob `DefaultDetectRadius` 30→50, one sweep feeds all. **AT-LENS-RADIUS-50** — spawn a hostile ~40–45 m out and confirm it's detected (silent at 30 m). The disc inner geometry widened (~48%→~80% of the disc); the iron-compass N (~94 px) vs Sunstone blip zone (~80 px) margin narrows to ~14 px — **flagged: verify the two are still disjoint** (no overlap) by eye. logs-green ≠ playable — closes t_4b9f8889. |
| 5 | **SBPR modal cursor-capture — the real fix (`IsMouseActive` postfix) + inventory-open suppress** | t_f7a5ad53 / t_a1cf35b0 (#237) | ✅ merged to `main` (`142b740`); ships in `v0.2.34`; awaiting Daniel | **The real cursor-capture fix** (supersedes the reverted §2L.7-R). Open each SBPR modal — **Local Map full view (M)**, **Surveyor's Table**, **sign panels**: the cursor is **free to move and click** (pins, swatches) and does **not** snap to screen-centre — **even with a Steam-Input virtual gamepad / drifting stick connected** (root cause: the Input System flipped the active source to Gamepad and re-locked every frame; the fix postfixes `ZInput.IsMouseActive`→true while a modal is open so vanilla's own `UpdateCursor` computes `lockState=None`). On **close**, the cursor re-locks exactly once (no stuck-free cursor). **AT-CURSOR-NOSNAP-ALL-MODALS** + **AT-CURSOR-RELOCK**. Sibling (t_a1cf35b0): the **Inventory hotkey cannot open over an SBPR modal** — **AT-INV-SUPPRESS**. Build 0/0, 226/226. logs-green ≠ playable — closes t_f7a5ad53 + t_a1cf35b0. |
| 6 | **Sunstone Lens standalone ring → world-space eidetic head-halo render** | t_d17d9b58 (#242) | ✅ merged to `main` (`05c53cb`); ships in `v0.2.34`; awaiting Daniel | Major rework — the standalone (**no-minimap**) Sunstone ring is now a **diegetic world-space head-halo of billboarded creature trophies** floating around the player's eye-point, replacing the screen-space camera-relative trophy ring. With **no** minimap present, equip + charge the Lens and approach hostiles: **(a)** trophies float in a tight halo around your head (`Character.GetEyePoint`), rarely occluded by terrain (honest depth, no through-wall material); **(b)** each trophy's radius + scale vary with distance (closer = nearer + bigger); **(c)** trophy-less creatures fall back via a variant→sibling remap (Greyling→Greydwarf …) then a generic threat glyph — a startup `DumpUnmappedCreatures` scan logs any unmapped; **(d)** trophies are **flat billboarded** `m_icons[0]` sprites (vanilla `Billboard`, `m_vertical`), not 3D meshes; **(e)** the faint solar empty-state ring stays screen-space. **AT-LENS-HALO-1..5**. **Supersedes Playtest #5 item 3's camera-relative ring fallback.** Host stays active (#209 invariant; only `_worldContent` toggles). Render is GPU-only — Daniel's in-world look is the accept; large rework, verify per PR #242 / t_d17d9b58 before filing fixes. logs-green ≠ playable — closes t_d17d9b58. |

### 🔁 Carried forward — not yet shipped / not yet verified

Shipped **no** code change in any tag (blocked / verify-only), so it carries into #6 rather than being archived as a #5 surface.

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 7 | **Portal Seed crafting cost** | t_a6831e8e | `blocked` — verify local solo (NRE root-crash #154 shipped in #1) | At the Explorer's Bench, Portal Seed shows cost **AncientSeed ×1 + GreydwarfEye ×20 + SurtlingCore ×2**, and crafting **consumes** exactly that. Verify **local solo on current `main`** (the per-frame tooltip NRE that masked this, t_2dd7c705/#154, shipped in #1). If correct → close t_a6831e8e; if wrong → spawn a fix card from the observed failure mode (A no cost / B wrong cost / C not craftable / D shown-but-not-consumed). |

### 🧭 Ground-truth cross-check at roll time (git)

- **`src/**/*.cs` changes on `main` since `v0.2.34-playtest`: 0.** Only the installer SHA pin **#244** (`9abe250`) and the
  BepInEx typeloader-cache clear **#245** (`7769812`) landed after the tag — both release/installer chores, **not** gameplay
  surfaces. So the auto cross-check for the **next** window is **clean (0 unledgered)**:
  `python3 scripts/gen-playtest-guide.py --check` (diffs `v0.2.34..main`) is green.
- The **six** Playtest #6 surfaces above are the `src/**/*.cs` changes in the **`v0.2.33..v0.2.34`** window (the build Daniel
  tested → the build carrying his fixes): **#236** (`e4c2100`, t_540ace8c, item 1), **#233** (`a6d9527`, t_3f7f3a0f, item 2),
  **#238** (`70ab72b`, t_aab051ae, item 3), **#234** (`dd680eb`, t_4b9f8889, item 4), **#237** (`142b740`, t_f7a5ad53 +
  t_a1cf35b0, item 5), **#242** (`05c53cb`, t_d17d9b58, item 6). Every one maps to a PENDING row → **0 unledgered** for this
  window too.
- **Supersession notes:** item 6 (world-space head-halo, #242) **supersedes** Playtest #5 item 3's camera-relative trophy-ring
  fallback; item 3 (richen minimap, #238) **supersedes** the #5 handoff's "dots / no stars / no rim" minimap representation
  (spec §5 knob-2 re-locked 2026-06-21); items 1–2 (#236 / #233) are **follow-ups** to #5 item 7 (the iron-compass M1
  north-ring); item 5 (#237) is the **real** cursor fix that **supersedes** the reverted §2L.7-R pair (t_8b86adb3 / t_12acb9ce).

### ⏳ In-flight (will join PENDING when merged)

- **Open PRs** not yet on `main` (become Playtest #7 candidates when merged): **#246** config bake-down classification
  (t_f87361cf), **#243** Painted Sign consume-cost per-CHANGED-slot (t_6df12ca8), **#241** Sunstone Lens pulsing solar aura
  impl-spec (t_e4a6f559), **#227** gen-playtest revert-net tooling (t_0fc06f42).
- **Reported-but-unmerged** Playtest #5 bugs (blocked cards, **no** shipped code yet → not test items until they merge):
  Local Map held mesh renders as a Hoe (t_2fb48391), Eikthyr boss pin = yellow square + raw `$enemy_eikthyr` label
  (t_5c3944cd).

---

## ARCHIVE — shipped playtests

### Playtest #5 — shipped v0.2.33-playtest (2026-06-21)

Build tag: `v0.2.33-playtest` (SBPR Trailborne 0.2.33) — the **first build carrying all of items 1–8** (the interim
`v0.2.32` tag lacked the Painted Sign three-slot #228, which merged after it). Guide:
[`playtest-5-testers-guide.md`](playtest-5-testers-guide.md). **Daniel ran #5 on the `v0.2.33` client** (2026-06-21 →
2026-06-22); his feedback generated the **Playtest #6 fix set** (6 PRs, shipped in `v0.2.34-playtest`) — see the current
PENDING. logs-green ≠ playable; the in-game run was the accept.

| # | Feature | Card | PR | What to verify in-game |
|---|---------|------|-----|------------------------|
| 1 | **Minimap DISC content-to-ring margin — closed (§2E.5.7)** | t_12e15162 | #216 (`v0.2.31`) | DISC cartography edge meets the bronze bezel with no transparent annulus; table pins + in-disc marker stay on the exact terrain cell under rotation/zoom; 125 m disc feel unchanged. Full AT-DISC-RING-1..4 → #5 guide item 1. |
| 2 | **Modal / TableEdit in-disc chevron — counter-rotate to screen-up (§2H.2)** | t_423f5bd7 | #215 (`v0.2.31`) | Player chevron stays screen-up (= facing) while the map rotates beneath it; the (M) modal no longer pins the chevron to map-north; edge-arrow + TableEdit regressions hold. Full AT-MODAL-MARKER-1..4 → #5 guide item 2. |
| 3 | **Sunstone Lens → minimap detection handoff (any-minimap rule)** | t_91e86951 | #218 (`v0.2.33`) | With any minimap present the Lens' detection moves onto it (disc blips ride `_pinObjects`; vanilla corner north-up with surviving aggro tint); camera-relative ring is the no-minimap fallback; single-sourced threat state. Full AT-LENS-DISC-* → #5 guide item 3. _(Standalone ring re-locked to a world-space head-halo in #6 item 6; minimap representation richened in #6 item 3.)_ |
| 4 | **Sunstone Lens NOT repairable at any station (`m_canBeReparied=false`)** | t_1afb94cd | #220 (`v0.2.33`) | A sun-depleted Lens shows **no** Repair affordance at the Explorer's Bench / Workbench / Forge; sunlight is the only refill; charge meter + drain model unchanged. AT-LENS-NOREPAIR → #5 guide item 4. |
| 6 | **Painted Sign board + border recolour (MaterialMan/MPB tint)** | t_f3310406 / t_24ad2570 | #224 (`v0.2.33`) | Board + two-tone border visibly recolour via each renderer's own MPB (not just the TMP text); `∅ None` reverts; tints persist a relog; hammer-hover re-asserts. **Superseded by item 8** (three independent slots). Full AT-SIGN-* → #5 guide item 6. |
| 7 | **Iron Compass → minimap north-ring M1 (compass-gated iron bezel + N-glyph)** | t_fb53c9e4 | #230 (`v0.2.33`) | Worn compass + SBPR map surface draws an iron-bezel recolor + N-glyph + cardinal ticks and hides the HUD needle; gated on equip; N orbits to true map-north. Full AT-COMPASS-* → #5 guide item 7. _(Rim colour retuned in #6 item 1; N z-order fixed in #6 item 2.)_ |
| 8 | **Painted Sign — three independent paint slots (letters / board / frame) + stained-wood basis** | t_6cc9f652 | #228 (`v0.2.33`) | Text / Board / Frame each tint exactly one surface independently (e.g. white board / red frame / blue letters); `∅ None` clears each slot; all three persist a relog; legacy `SBPR_SignColor` migrates to the board slot; stained-wood albedo reads true. Full AT-SIGN-3SLOT-* → #5 guide item 8. |

#### 🧭 Ground-truth cross-check (git) — what shipped under Playtest #5

- Items **1–2** (#216 / #215) shipped **inside** `v0.2.31` **after** the #4 guide was cut on the v0.2.30 build, so they appear
  in no prior guide and were hand-seeded into #5.
- Items **3–4 / 6 / 7 / 8** are the **7** `src/**/*.cs` changes in `v0.2.31..v0.2.33` (the auto feeder), **0 unledgered**. The
  reverted §2L.7-R cursor pair (t_8b86adb3 / t_12acb9ce) netted to zero (no test item). Installer SHA pins **#219 / #231 / #232**
  are release chores, not surfaces.
- **Build:** `v0.2.33` was the first build with all of 1–8 (interim `v0.2.32` lacked the signs three-slot #228). **Daniel played
  #5 on the v0.2.33 client** (2026-06-21 → -22); his feedback drove the **6-PR Playtest #6 fix set** that shipped under
  `v0.2.34-playtest` (see the current PENDING). logs-green ≠ playable.


### Playtest #4 — shipped v0.2.31-playtest (2026-06-20)

Build tag: `v0.2.31-playtest` (SBPR Trailborne 0.2.31). Guide:
[`playtest-4-testers-guide.md`](playtest-4-testers-guide.md) — the **first guide that correctly
describes the whole v0.2.30 surface set** (cut on the v0.2.30 build at 00:32, after the #3 guide had
been cut on v0.2.29). Playtest #4 actually **shipped under `v0.2.31`** (tagged 14:28): **2** further
`src/**/*.cs` PRs landed in the gap — **#215** (modal chevron counter-rotate, `af4202f`) and **#216**
(minimap-DISC margin, `8601647`), both ~12:50 — **after the #4 guide was cut**, so the #4 guide
describes neither. They carry into **Playtest #5** (hand-seeded, the same pattern by which the six
v0.2.30 surfaces were seeded into #4). Daniel's local-solo run on the v0.2.31 client is the accept —
logs-green ≠ playable.

| # | Feature | Card | PR | What to verify in-game |
|---|---------|------|-----|------------------------|
| 1 | **Sunstone Lens trophy-RING — first build it draws** | t_b8a19487 + t_d5949685 | #199/#209 (`v0.2.29`/`v0.2.30`) | The #199 render was frozen by a self-deactivating-host Update pump; #209 moved visibility to a `_content` child so `v0.2.30` is the first build the ring draws. Full AT-LENS-RING-1..5/AGGRO/CAMREL checklist → #4 guide item 1. |
| 2 | **Iron Compass HUD needle — first build it draws** | t_61aff612 + t_ee61472f | #208 (`v0.2.30`) | Same dead-pump bug as the lens + an unloadable `Knob.psd` sprite (replaced with a procedural disc); `v0.2.30` is the first build the dial/needle render. Full AT-COMPASS-HEADING/LAG/TILT/EQUIP-GATE/NOMAP-SAFE → #4 guide item 2. |
| 3 | **[M] open-hint + map NAME under the minimap disc** | t_26bba85b | #205 (`v0.2.30`) | Relocated the bottom-centre `[M] Read map` hint under the carry disc, co-located with the map name as one screen-stable unit. Full AT-MAPNAME-UNDER-DISC/MKEY-HINT-COLOCATED/HINT-VISIBILITY/HINT-NO-BOTTOM/CAPTION-NO-ROTATE/MAPNAME-BLANK → #4 guide item 3. |
| 4 | **Biome NAME on both cartography surfaces (Path A)** | t_304076fa | #207 (`v0.2.30`) | Current-biome NAME on the disc caption middle line + a fixed modal readout, one shared `$biome_*`-localized path. Full AT-BIOME-MINIMAP/MODAL/SHARED/CLEAN/NONE-OMIT → #4 guide item 4. |
| 5 | **Modal map content-to-ring margin — zeroed** | t_89d30da3 | #204 (`v0.2.30`) | The §2E.5.6 two-knob reframe (2×survey-radius + re-derived snapped pins) closed the ~22 px MODAL shroud band. Full AT-RING-1..4 → #4 guide item 5. **(NB: the corner DISC margin was a separate surface — fixed later in #216, now Playtest #5 item 1.)** |
| 6 | **Equipable icons — transparent backgrounds** | t_b9a111ca | #201 (`v0.2.30`) | Transparent RGBA canvas so vanilla's blue equipped indicator shows through; Local Map gets a real icon; a CI-gating test guards regressions. Full AT-EQUIP-IND-1/2/3/REGRESSION → #4 guide item 6. |

#### 🧭 Ground-truth cross-check (git) — what shipped under Playtest #4

- Items 1–6 are the v0.2.30 surface set the **#4 guide** describes (carried from #3 because the #3
  guide was cut on the v0.2.29 build). All await Daniel's v0.2.31-client run — logs-green ≠ playable.
- **Two PRs landed in the `v0.2.31` tag AFTER the #4 guide was cut (00:32 → tag 14:28):**
  - **PR #216** (squash `8601647`, card t_12e15162) — close the minimap **DISC** content-to-ring
    margin (§2E.5.7; size the cartography rect to the full TargetPx so meshR ≥ holeR on every survey
    size; extracted a `DiscRingGeometry` helper, CI-gated via `DiscRingGeometryTests`). The **second**
    surface of the content-to-ring family (#204 fixed the MODAL only). **Tested under Playtest #5**
    (PENDING item 1). (`v0.2.31`)
  - **PR #215** (squash `af4202f`, card t_423f5bd7) — modal/TableEdit **in-disc chevron counter-rotate
    to screen-up** (§2H.2; generalise the counter-rotation gate from `PlayerCentred` to
    `!_markerOffDisc`). Fixes Daniel's v0.2.30 "main map chevron always faces north." **Tested under
    Playtest #5** (PENDING item 2). (`v0.2.31`)

### Playtest #3 — shipped v0.2.30-playtest (2026-06-19)

Build tag: `v0.2.30-playtest` (SBPR Trailborne 0.2.30). Guide:
[`playtest-3-testers-guide.md`](playtest-3-testers-guide.md) — **note:** that guide was
generated on the **v0.2.29** build (frontmatter `generated_from_tag: v0.2.29`, cut 18:28 as
part of the #2→#3 roll), so it describes only the trophy-ring + Portal-Seed items. Playtest #3
actually **shipped under `v0.2.30`** (tagged 22:37): **5** code commits touching `src/**/*.cs`
landed in the gap (#204/#205/#207/#208/#209) plus the equipable-icon asset PR #201 — none of
which the #3 guide describes. Critically, **#208 (compass) and #209 (lens) are "rendered
NOTHING — dead Update pump" fixes**, so the #3 guide's trophy-ring headline was **dead on the
v0.2.29 build it targeted**; `v0.2.30` is the first build the ring/needle actually draw. The
full, correct checklist for all six v0.2.30 surfaces lives in **Playtest #4** (the first guide
cut on the v0.2.30 build). Daniel's local-solo run on the v0.2.30 client is the accept —
logs-green ≠ playable.

| # | Feature | Card | PR | What to verify in-game |
|---|---------|------|-----|------------------------|
| 1 | **Sunstone Lens trophy-ring — dead Update pump fixed** | t_d5949685 | #209 (`v0.2.30`) | The #199 trophy-ring render (shipped in v0.2.29) was frozen inactive by a self-deactivating-host `Update` pump — total, deterministic absence whether or not the lens was worn. #209 moves visibility to a `_content` child so the host's `Update` keeps pumping. **`v0.2.30` is the first build the ring renders.** Full checklist (AT-LENS-RING-1..5/AGGRO/CAMREL) → **Playtest #4** PENDING item 1. |
| 2 | **Iron Compass HUD needle — dead Update pump + Knob.psd fixed** | t_61aff612 | #208 (`v0.2.30`) | The compass HUD showed nothing when worn — same self-deactivating-host pump bug as the lens (found + fixed here first). #208 moves visibility to a `_content` child (host stays active) AND replaces the `UI/Skin/Knob.psd` builtin sprite (fails to load on Valheim 0.221.x Unity) with a procedural disc. **`v0.2.30` is the first build the dial/needle render.** Full checklist (AT-COMPASS-HEADING/LAG/TILT/EQUIP-GATE/NOMAP-SAFE) → **Playtest #4** PENDING item 2. |
| 3 | **[M] open-hint + map NAME under the minimap disc** | t_26bba85b | #205 (`v0.2.30`) | Relocated the floating bottom-centre `[M] Read map` hint to sit **under the carry minimap disc**, co-located with the map's name as one screen-stable unit (Daniel v0.2.27: the bottom hint "doesn't really work for me"). Full checklist (AT-MAPNAME-UNDER-DISC/MKEY-HINT-COLOCATED/HINT-VISIBILITY/HINT-NO-BOTTOM/CAPTION-NO-ROTATE/MAPNAME-BLANK) → **Playtest #4** PENDING item 3. |
| 4 | **Biome NAME readout on both cartography surfaces (Path A)** | t_304076fa | #207 (`v0.2.30`) | Current-biome NAME added to both surfaces: disc caption middle line (name / biome / `[M]` hint) + a fixed modal readout under the title cartouche, one shared `CurrentBiomeNameOrNull()` path, `$biome_*`-localized with a leak guard. Full checklist (AT-BIOME-MINIMAP/MODAL/SHARED/CLEAN/NONE-OMIT) → **Playtest #4** PENDING item 4. |
| 5 | **Modal map content-to-ring margin zeroed** | t_89d30da3 | #204 (`v0.2.30`) | The full circular modal map had a ~22 px shroud band between the cartography disc and the bezel ring (Daniel v0.2.27: "no margin at all"). The §2E.5.6 two-knob fix reframes the modal to 2×survey-radius (~2000 m) AND re-derives the snapped table-pin projection through that same span (the landmine — prevents +23.6 px pin drift). Full checklist (AT-RING-1..4) → **Playtest #4** PENDING item 5. |
| 6 | **Equipable icons — transparent backgrounds** | t_b9a111ca | #201 (`v0.2.30`) | Every equipable icon (Kit/Lens/Compass/Spade/Local Map) shipped an opaque background that occluded vanilla's blue "equipped" highlight (it draws behind the icon Image). Generators now build on a transparent RGBA canvas; a knockout script handles the FLUX composites; Local Map gets a real icon (it had none → magenta fallback); a CI-gating test guards regressions. Full checklist (AT-EQUIP-IND-1/2/3/REGRESSION) → **Playtest #4** PENDING item 6. |

#### 🧭 Ground-truth cross-check (git) — what shipped under Playtest #3

- **PR #209** (squash `1f3ef3b`, card t_d5949685) — Sunstone Lens detection-ring overlay
  **rendered NOTHING** — dead Update pump fix in `SunstoneLensHudOverlay.cs` (+ `Plugin.cs`):
  visibility moved to a `_content` child so the host's `Update` keeps pumping. First build the
  #199 trophy ring actually draws. **Tested under Playtest #4** (PENDING item 1). (`v0.2.30`)
- **PR #208** (squash `6676080`, card t_61aff612) — Iron Compass HUD overlay **rendered nothing
  when worn** — same dead-pump bug fix in `SBPR_CompassHud.cs` (+ `Plugin.cs`), plus a procedural
  dial sprite replacing the unloadable `UI/Skin/Knob.psd` builtin. First build the dial/needle
  render. **Tested under Playtest #4** (PENDING item 2). (`v0.2.30`)
- **PR #207** (squash `8b2e328`, card t_304076fa) — biome NAME readout on both cartography
  surfaces (`MapCaptionText.cs` + `MapSurface.cs`): disc caption middle line + modal fixed label,
  one shared biome path. **Tested under Playtest #4** (PENDING item 4). (`v0.2.30`)
- **PR #205** (squash `dcd2181`, card t_26bba85b) — relocate `[M]` open-hint + map NAME under the
  minimap disc (`LocalMapController.cs`/`MapSurface.cs`/`MapViewer.cs`/`CartographyViewer.cs`),
  deleting the bottom-centre prompt. **Tested under Playtest #4** (PENDING item 3). (`v0.2.30`)
- **PR #204** (squash `333d1df`, card t_89d30da3) — zero the modal map content-to-ring margin
  (`MapSurface.cs`): §2E.5.6 two-knob reframe (DisplayedSpanMeters 2×radius + WorldToSurfacePxSnapped
  re-derive). **Tested under Playtest #4** (PENDING item 5). (`v0.2.30`)
- **PR #201** (squash `b19f670`, card t_b9a111ca) — equipable icons get transparent backgrounds
  (asset generators + knockout script + new Local Map icon + CI-gating
  `EquipableIconTransparencyTests.cs`) so vanilla's blue equipped indicator shows through. Asset/test
  change, no `src/**/*.cs` gameplay surface. **Tested under Playtest #4** (PENDING item 6). (`v0.2.30`)

### Playtest #2 — shipped v0.2.29-playtest (2026-06-19)

Build tag: `v0.2.29-playtest` (SBPR Trailborne 0.2.29). Guide:
[`playtest-2-testers-guide.md`](playtest-2-testers-guide.md) — **note:** that guide was
generated at the v0.2.28 build (frontmatter `generated_from_tag: v0.2.27`), *before* the
trophy-ring landed, so its Sunstone item is stale (see the ⤳ row). **4** code commits
touching `src/**/*.cs` shipped across the #2 cycle (`v0.2.26`→`v0.2.29`: #192/#196/#197/#199).
Daniel's local-solo run on the v0.2.29 client is the accept — logs-green ≠ playable.

| # | Feature | Card | PR | What to verify in-game |
|---|---------|------|-----|------------------------|
| 1 | **Disc player-marker chevron (A′)** | t_efe8b32b | #192 (`v0.2.27`) | Open the local-map **disc**; the player marker is the **vanilla default-map chevron** (not a blue quad), dead-centre, pointing **up = your facing** (NOT north — the disc rotates to heading). Confirm it reads as "you, forward = up" with no north-locked arrow. Logs note `using VANILLA art` vs `chevronFallback` — either is acceptable visually, but flag if it's the procedural fallback. |
| 1a | **Disc render-correctness — real fog cloud + circular clip** | t_ba31ad30 | #192 (`v0.2.27`) + shroud fix #197 (`v0.2.28`) | Open the disc: (a) **no opaque black square** behind it — outside the circle is transparent, the world shows through; (b) the unexplored area inside renders as **vanilla's real fog-of-war cloud** (matching the normal map's unexplored look), NOT a flat dark fill **and NOT the faded "shared-via-table" look** (the #197 G-channel fix — see item 6); (c) the interior is a **continuous disc** — no rotated-square/diamond, no ocean bleeding in at the corners; (d) the bronze bezel ring is visible. GPU-verified on Prime; Daniel's eye on the live client is the final accept. |
| 1b | **Minimap disc fixed tight zoom (125 m)** | t_ba31ad30 | #192 (`v0.2.27`) | The corner disc frames a **tight ~125 m local window** around you (a small portion of the surveyed area), NOT the whole survey — you should read immediate surroundings, like a vanilla minimap. There is **no zoom input** (`,`/`.`/scroll do nothing on the disc by design). To see the whole local map, open the **full map (M)**, which stays at its full-survey scale. Verify: terrain, pins, and the chevron all sit at the same scale (a pin on a landmark lands ON that landmark at the tight zoom). 125 m is a starting value — tell Daniel if it feels too tight/loose. |
| 6 | **Unexplored = full shroud, not the shared-table look** | t_48c23824 | #197 (`v0.2.28`) | Open the local-map disc **and** the full map (M) / Surveyor's Table with a partially-explored survey. The **unexplored** area must render as Valheim's **solid fog-of-war shroud** — the same dark cloud you see on a fresh regular map for terrain nobody has visited — **NOT** the lighter, faded "someone shared this map with you via the cartography table" look it showed in v0.2.27. Explored/surveyed area still shows full bounded cartography (biome/water/relief). Root cause was a one-channel encoding bug: the reveal tagged unexplored as `_FogTex` G=0 (= shared-by-others) instead of G=255 (= nobody-explored). Daniel's eye on the GPU client is the accept (AT-FOG-VANILLA). |
| 7 | **Map-table / full-map cursor is free to click** | t_1f82da71 | #196 (`v0.2.28`) | At the **Surveyor's Table** map and the equipped **Local Map full view (M)**, the mouse cursor is **free to move and click** (e.g. to click pins for removal) — NOT locked to screen-centre. Mouse-look stays frozen while the modal is open (you don't pan the world). On **close**, the cursor re-locks and the game resumes normal mouse-look exactly once (no stuck-free cursor after exit). Fixes Daniel's 06-19 "my mouse is not free to move and click on pins" report; root cause was the cursor-release hooked an empty/inlined vanilla method — re-seated onto `GameCamera.LateUpdate`. |
| ⤳ | **Sunstone Lens trophy-ring** | t_b8a19487 | #199 (`v0.2.29`) | **Render SHIPPED in v0.2.29, ~6 min AFTER the #2 guide was generated at v0.2.28** — so the #2 guide's Sunstone line still describes the retired **placeholder text HUD** (now OFF by default, behind `DebugTextReadout`). The full, correct trophy-ring checklist lives in **Playtest #3** (the first guide cut on the v0.2.29 build). Recorded here so the #2 build's git cross-check stays complete; **tested under #3**. |

#### 🧭 Ground-truth cross-check (git) — what shipped under Playtest #2

- **PR #199** (squash `2ed397f`, card t_b8a19487) — Sunstone Lens **trophy-RING** detection
  render in `SunstoneLensHudOverlay.cs` (+ `Plugin.cs`), replacing the bottom-centre text
  placeholder: trophy-per-hostile ring, proximity-scaled, vanilla star pips, aggro-colour
  tint, empty solar ring, threat-glyph fallback. Shipped in the `v0.2.29` tag. **Tested under
  Playtest #3** (the ⤳ row above + #3 PENDING item 1).
- **PR #197** (squash `bf562ec`, card t_48c23824) — unexplored-area full-shroud `_FogTex`
  **G-channel** fix in `MapSurface.BindBoundedReveal` (fogged texels now R=255 **and** G=255
  = nobody-explored → solid shroud, instead of R=255/G=0 = shared-by-others; plus a
  `_SharedFade=0` pin on the cloned material). This is **item 6** above. (`v0.2.28`)
- **PR #196** (squash `f3fc663`, card t_1f82da71) — modal cursor-release re-seated onto the
  live `GameCamera.LateUpdate` seam (the old hook targeted an emptied/inlined vanilla
  method), freeing the cursor at the Surveyor's Table + Local Map full view. This is
  **item 7** above. (`v0.2.28`)
- **PR #192** (commit `678f9aa`, cards t_ba31ad30 + t_efe8b32b) — the MapSurface
  render-correctness fix (real `_FogTex` cloud, geometry circular clip, transparent-outside
  bezel), the disc player-marker chevron, and the 125 m fixed-zoom decouple
  (`MapViewer`/`MapSurface`, new `CircularRawImage.cs`). These are items 1 / 1a / 1b above.
  (`v0.2.27`)

### Playtest #1 — shipped v0.2.26-playtest (2026-06-18)

Build tag: `v0.2.26-playtest` (SBPR Trailborne 0.2.26).
Guide: [`playtest-1-testers-guide.md`](playtest-1-testers-guide.md) · progression
briefing: [`playtest-1-expected-progression.md`](playtest-1-expected-progression.md).
**17** code commits touching `src/**/*.cs` shipped since `v0.2.25-playtest`. Daniel's
local-solo run is the accept — logs-green ≠ playable.

| # | Feature | Card | PR | What to verify in-game |
|---|---------|------|-----|------------------------|
| 1 | **Sunstone dual-source loot** | t_0445f590 | #183 | Sunstone drops from swamp **surface** chests (~15%) and **Draugr Elite** (~5%). Loot a spread of **freshly-discovered** swamp chests (already-populated chests keep old contents — vanilla populate-once behavior) + kill Draugr Elites; confirm it appears at roughly those rates and is **pickable** (lands in inventory with its icon). **QA data-layer PASS** (t_0aef1243, `docs/v3/research/QA-sunstone-loot-economy.md`): swamp table carries Sunstone w=0.584 → empirically 15.01%/chest over 200k draws of the vanilla sampler; crypt table clean; elite drop 5% flat. Daniel's run closes the observed last mile (logs-green ≠ playable). |
| 2 | **Local Map opens on M, not E** | t_f9a04fda | #181 | Press **M** with the Surveyor's Table map equipped/in-range → local map opens. Pressing **E** (Use) does NOT open it. SBPR owns the M edge. |
| 3 | **Ancient Portal proximity FX aligned** | t_06b7b13c | #180 | Approach an Ancient Portal → the proximity/target-found effect renders **flat, aligned to the ring** (not vertical/offset). |
| 4 | **Iron Compass v3 — Trinket + HUD needle** | t_ee61472f | #171 | Equip Iron Compass in the Trinket slot → a HUD compass needle overlay appears and tracks heading (the no-map orientation payoff). |
| 5 | **Swamp Sunstone Lens trinket** | t_2fd7bc7f | (tier) | Solar-charged monster-detection trinket: charges in daylight, reveals/indicates nearby monsters per spec. **NOTE:** the shipped detection render is a placeholder text HUD — the trophy-ring redesign (t_b8a19487) shipped later in `v0.2.29` #199, tested under Playtest #3. |
| 6 | **Per-pin icon tint + label color** | t_3d7aaa90 | #168 | Place marker pins → per-pin icon tint and label text color apply and persist. |
| 7 | **Ancient Portal walk-up access** | t_ea0072ba | (#162/#169) | Walk up to / through an Ancient Portal cleanly — per-leg colliders, centre drop slab; no pathing block, no getting stuck. |
| 8 | **Local Map provider binding + carry-state disc** | t_7dd54899 | #162 | Carry the map item → minimap renders as the carry-state disc; provider binding correct. |
| 9 | **Map bezel circular clip** | t_d44572f2 | #159 | Local map parchment does NOT bleed past the disc edge — hard circular bezel clip. |
| 10 | **Local Map title format** | t_783672ac | #158 | Map title renders without a race glitch, formatted `Local map for <name>`. |
| 11 | **Re-name a named Surveyor's Table** | (#157) | #157 | `[Use]+Alt` on an already-named Surveyor's Table re-opens the name prompt (§1.6.5). |
| 12 | **Ancient Portal proximity effect wired** | t_e58283d7 | #156 | Vanilla proximity/target-found effect fires on Ancient Portal approach (issue 1). |
| 13 | **Item tooltip NRE fix** | t_2dd7c705 | #154 | Hover SBPR items with custom attacks → no per-frame tooltip NullReferenceException in the log. |

**Also shipped (graduated from in-flight in #1):**

- **Sunstone recipe removal** — t_c27f985e (#186, commit `c7463ac`). The provisional
  Iron×1+Crystal×2 Explorer's Bench craft is **gone**; exploration drops are the sole
  acquisition path. Verify Sunstone is **NOT** craftable and still obtainable via drops.

**Shipped refactor (no dedicated in-game item — behavior-preserving):**

- **CLEANUP 3/3 — null-remediation** — t_0234cc42 (#187, commit `1c5da09`):
  null-as-value → `TryX(out)` across the 6 branch-on-result helpers (Assets.cs + 13
  feature files), build 0/0. No behavior change, so no gameplay test item — covered by
  the structural xUnit suite (#182), not in-game testing. Recorded here so the
  generator's "every code change maps to a ledger item" cross-check stays honest.
