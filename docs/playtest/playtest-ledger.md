---
title: "SBPR Trailborne — Playtest Ledger"
status: living
purpose: >
  Durable, accumulating record of what needs in-game testing. Items land here as
  work merges (no reliance on memory). When a playtest is prepped, scripts/gen-playtest-guide.py
  rolls the PENDING section + git-derived code changes into a numbered Playtest #N testers guide,
  then the shipped items move to the ARCHIVE section under that playtest number.
playtest_counter: 4            # next playtest is #4 (human-facing series; DISTINCT from vX.Y.Z-playtest tags)
last_playtest_tag: v0.2.30-playtest
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

## PENDING — accrues for the next playtest (Playtest #4)

> Build target: `v0.2.30-playtest` (SBPR Trailborne 0.2.30). Test **local solo** on a
> fresh client build unless an item says otherwise.
>
> _Why this PENDING is large — the #3 guide is stale. The Playtest #3 testers guide was
> generated on the **v0.2.29** build (`generated_from_tag: v0.2.29`, cut 18:28 as part of
> the #2→#3 roll), but Playtest #3 actually **shipped under `v0.2.30`** (tagged 22:37). In
> the gap, **five `src/**/*.cs` PRs landed** (#204/#205/#207/#208/#209) plus the equipable-icon
> PR #201 — none of which the #3 guide describes. Worse: two of them (#208 compass, #209 lens)
> are **"rendered NOTHING — dead Update pump"** fixes, so the #3 guide's trophy-ring headline
> was **dead-on-arrival on the v0.2.29 build it targeted** — `v0.2.30` is the **first build the
> Sunstone trophy ring and the Iron Compass needle actually draw**. So **#4 is the first guide
> that correctly describes the whole v0.2.30 surface set.** All six surfaces are seeded by hand
> below (same pattern as the trophy ring was hand-seeded into #3) because the roll-time git
> window `v0.2.30..main` is clean — see the cross-check section._

### 🆕 First correct guide for the v0.2.30 surface set (test now)

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 1 | **Sunstone Lens — trophy-RING detection render (NOW LIVE)** | t_b8a19487 (render #199) + t_d5949685 (dead-pump fix #209) | ✅ render shipped #199 (`v0.2.29`); **dead Update pump fixed #209 (`v0.2.30`)** — **first build it actually draws**; both cards `blocked` awaiting Daniel's in-game accept | 🔴 **This is the first build the ring renders at all** — #199's render was frozen inactive by a self-deactivating-host Update pump (`SetVisible(false)` killed the GameObject its own `Update` lived on), fixed in #209 by moving visibility to a `_content` child. So a totally-absent ring on v0.2.29 was the *pump bug*, not the equip-gate. Now: equip the Sunstone Lens, let it **solar-charge in daylight**, then approach hostiles (solo: spawn Greydwarves / a Draugr Elite). Verify: **(a)** a **trophy ring** appears — each detected hostile shows its **creature trophy** at that enemy's **screen bearing, camera-relative** (turning the camera sweeps the ring; **NOT** north-locked — preserves the Compass's north payoff); **(b) size ∝ proximity** (ring radius fixed ~180 px); **(c) vanilla star pips** above starred enemies (real nameplate art, not ★ glyphs); **(d) aggro-colour tint** 🟡 idle · 🟠 aggroed on another player · 🔴 aggroed on **YOU**; **(e)** trophy-less hostile → generic threat glyph (never invisible); **(f)** worn+charged, nothing near → faint **solar ring** (`ShowEmptyRing` default ON); depleted / not worn → ring **OFF**. `SunstoneLens.DebugMount` logs mount + visibility transitions (default ON this cut) — a fresh `LogOutput.log` now splits mount/pump-fail from on-screen-but-empty. `[SunstoneLens]` config (`RingRadiusPx`/`RingIcon{Min,Max}Px`/`RingMaxIcons`/…) is live-tunable. AT-LENS-RING-1..5 / AGGRO / CAMREL; logs-green ≠ playable — Daniel's in-game look closes t_b8a19487 + t_d5949685. |
| 2 | **Iron Compass — HUD needle render (NOW LIVE)** | t_61aff612 (dead-pump fix #208) + t_ee61472f (orig #171) | ✅ dead Update pump + `Knob.psd` sprite fixed #208 (`v0.2.30`) — **first build the dial/needle actually draw**; t_61aff612 `blocked` awaiting Daniel | 🔴 **Same dead-pump bug as the lens, fixed first here.** The compass HUD showed **nothing** when worn (Daniel, v0.2.28): `SBPR_CompassHud`'s `_root` *was* its own host GameObject, so `SetVisible(false)` deactivated the object its `Update` pump lives on — froze it permanently. Fix: visibility toggles a dedicated `_content` child; host stays active. Secondary fix: the dial sprite (`Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd")`) **fails to load on Valheim's 0.221.x Unity** — replaced with a procedural disc (`DiscSprite()`). Verify: equip the **Iron Compass in the Trinket slot** → a HUD compass overlay (dial + cardinal N/E/S/W + needle) appears top-centre. **AT-COMPASS-HEADING** — needle points to **true world north**: face N → needle up; turn to face E → needle reads east (camera-relative wrap, smooth). **AT-COMPASS-LAG** — needle lags slightly behind a fast spin and settles (not an instant snap). **AT-COMPASS-TILT** — looking up/down tilts the dial ≤~45° then clamps. **AT-COMPASS-EQUIP-GATE** — overlay shows **only while equipped** in the Trinket slot; carried-unequipped → no overlay; unequip → hides immediately. **AT-COMPASS-NOMAP-SAFE** — renders correctly under the SB server's default NoMap (no minimap). `IronCompass.DebugMount` logs (default ON this cut) split mount/pump-fail from off-screen; `SBPR_CompassNeedleLag` is live-tunable. Bake `DefaultDebugMount`→false once Daniel confirms the dial. logs-green ≠ playable — Daniel's worn-compass look closes t_61aff612. |
| 3 | **[M] open-hint + map NAME UNDER the minimap disc** | t_26bba85b (#205) | ✅ shipped #205 (`v0.2.30`); card `done`, awaiting Daniel's in-game accept | nomap-ON, carry a named, imprinted Local Map. Verify: **(a) AT-MAPNAME-UNDER-DISC** — the map's name ("Local map for <Table>") renders **directly under the minimap disc** (top-right), legible at the ~200 px disc, **NOT** a floating bottom-centre element; **(b) AT-MKEY-HINT-COLOCATED** — the `[M]` open-hint sits **with the name** as one unit, rebind-correct (rebind Map → key updates; no hardcoded "M"); **(c) AT-HINT-VISIBILITY** — the caption is visible **whenever the disc is** (provider bound + nomap-ON), **including bound-but-unequipped** (the old bottom hint only showed while equipped — this widening is the recommended Q2 model; tell Daniel if he'd rather it be equipped-only, a one-line flip); **(d) AT-HINT-NO-BOTTOM** — there is **no** `[M] Read map` element at screen bottom-centre anymore; **(e) AT-CAPTION-NO-ROTATE** — the caption is **screen-stable**, it does NOT spin when the disc rotates to heading; **(f) AT-MAPNAME-BLANK** — a pre-naming map shows the hint line **only**, never "Local map for " with an empty tail; **(g)** disc render + on-face chevron + the modal's BARE title cartouche are **unchanged**. **Build-calibration knobs** (`CaptionNameFontPx 18` / `CaptionHintFontPx 16` / `CaptionGapPx 10` in `MapSurface.cs`) — tell Daniel if the placement/legibility under the disc feels off. logs-green ≠ playable — Daniel's GPU eyeball on the top-right placement closes t_26bba85b. |
| 4 | **Biome NAME readout on BOTH cartography surfaces (Path A)** | t_304076fa (#207) | ✅ shipped #207 (`v0.2.30`); card `done`, awaiting Daniel — **sequence with item 3** | nomap-ON, carry a named, imprinted Local Map; walk across biome borders (e.g. Meadows → Black Forest → Swamp). Verify: **(a) AT-BIOME-MINIMAP** — the disc caption now shows the **current-biome NAME as the MIDDLE line** (stack reads **name / biome / `[M]` hint**), and it **updates on biome change** as you cross a border; **(b) AT-BIOME-MODAL** — open the modal (M): a **fixed current-biome readout** sits **under the title cartouche** ("Local map for X" / **biome**) and tracks the player live while open (NOT a cursor-hover readout — that's a deferred follow-up); **(c) AT-BIOME-SHARED** — both surfaces show the **same** biome name (one shared code path); **(d) AT-BIOME-CLEAN** — the biome name is **locale-correct** (switch language → it follows the vanilla `$biome_*` tokens), and a raw `$biome_*` literal **never** appears; **(e) AT-BIOME-NONE-OMIT** — in a `Biome.None` edge state (pre-spawn / between zones) the disc **omits** the biome line (falls back to name / hint) and the modal **hides** its biome label — never an empty row or `$biome_none`; **(f) regressions** — the PR #205 name + `[M]` hint lines are **unchanged**, the caption is still **screen-stable** (does NOT spin), and the modal's BARE title cartouche is unchanged (biome sits **below** it, no overlap). **Build-calibration knobs** (`CaptionBiomeFontPx 16` disc / `ModalBiomeFontPx 22` modal, plus the modal label's `anchoredPosition -84` in `MapSurface.cs`) — tell Daniel if the biome line's size/placement/order feels off (the name/biome/hint order + sizes are his eyeball's to tune). logs-green ≠ playable — Daniel's GPU eyeball on **both** surfaces (placement, legibility, update-on-border-crossing) closes t_304076fa. |
| 5 | **Modal map content-to-ring margin — zeroed (frame surveyed disc)** | t_89d30da3 (#204) | ✅ shipped #204 (`v0.2.30`); card `done`, awaiting Daniel's GPU eyeball | Open the **full circular modal map (M)** over a **fully-surveyed** area. **AT-RING-1** — the cartography disc edge **meets the bronze bezel ring with NO visible shroud/fog band** (Daniel's v0.2.27 ask: "no margin at all"; the old ~22 px band is gone — modal now frames 2×survey-radius ≈ 2000 m instead of the over-provisioned 2112 m window). **AT-RING-2** (regression #159) — cartography does **not** bleed past the ring / outside the disc under rotation. **AT-RING-3** (regression) — the **corner minimap disc (125 m)** keeps its current framing (untouched by this fix). **AT-RING-4** (THE LANDMINE) — **table pins + the in-disc player marker still land on the exact terrain cell they annotate** under rotation/zoom (the snapped-pin projection was re-derived through the shared span; headless arithmetic shows +0 px drift vs. the +23.6 px it would drift if only the modal span were changed). Headless verified build 0/0 + BoundedMapMath 27/27, but the parchment shader can't render headless — **AT-RING-1/2/4 are Daniel's GPU-client eyeball.** logs-green ≠ playable. |
| 6 | **Equipable icons — transparent backgrounds (blue equipped-indicator shows)** | t_b9a111ca (#201) | ✅ shipped #201 (`v0.2.30`); card `done`, awaiting Daniel's in-game equip check | **AT-EQUIP-IND-1** — equip each of **Cartographer's Kit / Sunstone Lens / Iron Compass / Trailblazer's Spade / Local Map** → the vanilla **blue "equipped" highlight is visible** behind/around the icon in the inventory slot (it draws behind the icon Image; the old opaque icon backgrounds occluded it). **AT-EQUIP-IND-2** — each item silhouette still reads clearly in the slot: **no haloing, no eaten edges, no leftover background fringe**. **AT-EQUIP-IND-3 (Local Map)** — the Local Map now ships a **real transparent-bg icon** (it previously had **no icon at all** → magenta fallback); confirm a parchment-map icon **and** the equipped indicator. **AT-EQUIP-IND-REGRESSION** — the marker build-piece icons (`marker_*`) are unchanged (already transparent). A CI-gating `EquipableIconTransparencyTests.cs` (42/42) now red-fails if any equipable icon ships opaque again. logs-green ≠ playable — Daniel equipping each item is the accept. |

### 🔁 Carried forward — not yet shipped / not yet verified

Did **not** ship a code change in `v0.2.30-playtest` (blocked / verify-only), so it rolls
into #4 rather than #3's shipped archive.

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 7 | **Portal Seed crafting cost** | t_a6831e8e | `blocked` — verify local solo (NRE root-crash #154 shipped in #1) | At the Explorer's Bench, Portal Seed shows cost **AncientSeed ×1 + GreydwarfEye ×20 + SurtlingCore ×2**, and crafting **consumes** exactly that. Verify **local solo on current `main`** (the per-frame tooltip NRE that masked this, t_2dd7c705/#154, shipped in #1). If correct → close t_a6831e8e; if wrong → spawn a fix card from the observed failure mode (A no cost / B wrong cost / C not craftable / D shown-but-not-consumed). |

### 🧭 Ground-truth cross-check at roll time (git)

- **`src/**/*.cs` changes on `main` since `v0.2.30-playtest`: 0 commits.** The only
  post-tag change is the installer SHA pin **#210** (`e0975b9`) — a release chore, not a
  gameplay surface. So the auto cross-check window is clean.
- The six headline items above (#204/#205/#207/#208/#209 + icon #201) all shipped *inside*
  the `v0.2.30` tag, **after** the Playtest #3 guide was cut on the `v0.2.29` build (18:28),
  so they appear in **no** prior guide and are seeded by hand here for their first correct
  in-game checklist — the same pattern by which the trophy ring was hand-seeded into #3.
- `scripts/gen-playtest-guide.py --ref main` confirms 0 code changes / 0 unledgered for
  this window (it diffs `v0.2.30..main`).

### ⏳ In-flight (will join PENDING when merged)

- Nothing currently `running` against `main`. (All of #204/#205/#207/#208/#209/#201 merged
  into the `v0.2.30` tag and are now PENDING items 1–6 above.)
- Design/blocked cards not yet built (no test item until they ship): biome indicators were
  Path A and shipped (#207); no other built-but-unmerged cartography/HUD surface is open.

---

## ARCHIVE — shipped playtests

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
