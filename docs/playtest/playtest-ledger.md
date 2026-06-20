---
title: "SBPR Trailborne — Playtest Ledger"
status: living
purpose: >
  Durable, accumulating record of what needs in-game testing. Items land here as
  work merges (no reliance on memory). When a playtest is prepped, scripts/gen-playtest-guide.py
  rolls the PENDING section + git-derived code changes into a numbered Playtest #N testers guide,
  then the shipped items move to the ARCHIVE section under that playtest number.
playtest_counter: 3            # next playtest is #3 (human-facing series; DISTINCT from vX.Y.Z-playtest tags)
last_playtest_tag: v0.2.29-playtest
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

## PENDING — accrues for the next playtest (Playtest #3)

> Build target: `v0.2.29-playtest` (SBPR Trailborne 0.2.29 — the v0.2.28 cartography
> build **plus** the Sunstone Lens **trophy-ring** detection render #199). Test
> **local solo** on a fresh client build unless an item says otherwise.
>
> _Why the trophy-ring is here and not in #2's archive-as-tested: #199 merged ~6 min
> AFTER the Playtest #2 testers guide was generated (guide cut at the v0.2.28 build
> 17:28; #199 merged 17:32; `v0.2.29-playtest` tagged 17:34). So the #2 guide's Sunstone
> line still describes the retired placeholder text HUD — **this** is the first guide to
> describe the shipped trophy ring. Seeded by hand because the generator's card-id
> cross-check matched t_b8a19487 in #2's stale PENDING and never saw the description drift._

### 🆕 Shipped in v0.2.29 — first guide to describe it (test now)

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 1 | **Sunstone Lens — trophy-RING detection render** | t_b8a19487 | ✅ render shipped #199 (`v0.2.29`); card still `blocked` (awaiting Daniel's in-game accept) | Equip the Sunstone Lens and let it **solar-charge in daylight**, then approach hostiles (solo: spawn a few Greydwarves / a Draugr Elite). Verify: **(a)** a **trophy ring** appears around you — each detected hostile shows its **creature trophy** at that enemy's **screen bearing**, **camera-relative** (turning the camera sweeps the ring; it is **NOT** north-locked — that preserves the Iron Compass's north payoff); **(b) size ∝ proximity** — nearer = bigger, ring radius fixed (~180 px default); **(c) vanilla star pips** above starred enemies (★ 1-star, ★★ 2-star — real Valheim nameplate art, not glyphs); **(d) aggro-colour tint** 🟡 idle · 🟠 aggroed on another player · 🔴 aggroed on **YOU**; **(e)** a **trophy-less** hostile shows a generic threat glyph / danger-triangle (never invisible); **(f)** worn+charged with **nothing near** → a faint **solar ring** outline (`ShowEmptyRing` default ON); **depleted / not worn** → ring **OFF**. The detection **mechanic** (`GatherHostiles`, solar battery, equip-gate) is **UNCHANGED** — this PR is the **render** half only, so an empty ring near a real hostile is a charge/equip-gate bug, not a render bug (**cross-ref t_7fc750ea**, the Iron Compass "does nothing" report — possible shared root, both are HUD overlays). Config lives under `[SunstoneLens]` (`RingRadiusPx`/`RingIcon{Min,Max}Px`/`RingMaxIcons`/…) and is **live-tunable** — tell Daniel if radius/sizes feel off. AT-LENS-RING-1..5 / AGGRO / CAMREL; logs-green ≠ playable — Daniel's in-game look closes t_b8a19487. |

### 🔁 Carried forward — not yet shipped / not yet verified

These were in #2's PENDING but did **not** ship a code change in `v0.2.29-playtest`
(blocked / verify-only), so they roll into #3 rather than #2's shipped archive.

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 2 | **Portal Seed crafting cost** | t_a6831e8e | `blocked` — verify local solo (NRE root-crash #154 shipped in #1) | At the Explorer's Bench, Portal Seed shows cost **AncientSeed ×1 + GreydwarfEye ×20 + SurtlingCore ×2**, and crafting **consumes** exactly that. Verify **local solo on current `main`** (the per-frame tooltip NRE that masked this, t_2dd7c705/#154, shipped in #1). If correct → close t_a6831e8e; if wrong → spawn a fix card from the observed failure mode (A no cost / B wrong cost / C not craftable / D shown-but-not-consumed). |
| 3 | **[M] open-hint + map NAME UNDER the minimap disc** | t_26bba85b | review-required (PR open; code built 0/0) — **verify after merge** | nomap-ON, carry a named, imprinted Local Map. Verify: **(a) AT-MAPNAME-UNDER-DISC** — the map's name ("Local map for <Table>") renders **directly under the minimap disc** (top-right), legible at the ~200 px disc, **NOT** a floating bottom-centre element; **(b) AT-MKEY-HINT-COLOCATED** — the `[M]` open-hint sits **with the name** as one unit, rebind-correct (rebind Map → key updates; no hardcoded "M"); **(c) AT-HINT-VISIBILITY** — the caption is visible **whenever the disc is** (provider bound + nomap-ON), **including bound-but-unequipped** (the old bottom hint only showed while equipped — this widening is the recommended Q2 model; tell Daniel if he'd rather it be equipped-only, a one-line flip); **(d) AT-HINT-NO-BOTTOM** — there is **no** `[M] Read map` element at screen bottom-centre anymore; **(e) AT-CAPTION-NO-ROTATE** — the caption is **screen-stable**, it does NOT spin when the disc rotates to heading; **(f) AT-MAPNAME-BLANK** — a pre-naming map shows the hint line **only**, never "Local map for " with an empty tail; **(g)** disc render + on-face chevron + the modal's BARE title cartouche are **unchanged**. **Build-calibration knobs** (`CaptionNameFontPx 18` / `CaptionHintFontPx 16` / `CaptionGapPx 10` in `MapSurface.cs`) — tell Daniel if the placement/legibility under the disc feels off. logs-green ≠ playable — Daniel's GPU eyeball on the top-right placement closes t_26bba85b. |

### 🧭 Ground-truth cross-check at roll time (git)

- **`src/**/*.cs` changes on `main` since `v0.2.29-playtest`: 0 commits.** The only
  post-tag change is the installer SHA pin **#200** (`83a44df`) — a release chore, not a
  gameplay surface. So the auto cross-check window is clean; the headline item above
  (**trophy-ring #199**, `2ed397f`, card t_b8a19487) shipped *inside* the v0.2.29 tag and
  is seeded by hand here for its first correct in-game checklist (see the PENDING note).
- `scripts/gen-playtest-guide.py --ref main` confirms 0 code changes / 0 unledgered for
  this window.

### ⏳ In-flight (will join PENDING when merged)

- **t_89d30da3** — modal map content-to-ring margin (reframe modal to surveyed-disc
  diameter, §2E.5.6, AT-RING-1..4) is `running`; will accrue here when it merges.
- Design/blocked cards not yet built (no test item until they ship): biome indicators
  on the disc/modal (t_caf0f1cf), equipable-icon transparent-bg (t_b9a111ca, PR #201 open).
  ([M] name-hint under the disc, t_26bba85b, now ships its code in this PR → moved to
  PENDING item 3 above; verify after merge.)

---

## ARCHIVE — shipped playtests

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
