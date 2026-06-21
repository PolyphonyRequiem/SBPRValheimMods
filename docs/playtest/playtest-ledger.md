---
title: "SBPR Trailborne — Playtest Ledger"
status: living
purpose: >
  Durable, accumulating record of what needs in-game testing. Items land here as
  work merges (no reliance on memory). When a playtest is prepped, scripts/gen-playtest-guide.py
  rolls the PENDING section + git-derived code changes into a numbered Playtest #N testers guide,
  then the shipped items move to the ARCHIVE section under that playtest number.
playtest_counter: 5            # next playtest is #5 (human-facing series; DISTINCT from vX.Y.Z-playtest tags)
last_playtest_tag: v0.2.31-playtest
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

## PENDING — accrues for the next playtest (Playtest #5)

> Build target: `v0.2.31-playtest` (SBPR Trailborne 0.2.31) for items **1–2** (already shipped in the
> v0.2.31 tag); items **3–4** (#218/#220) are **merged to `main` and await the next build tag**
> (v0.2.32+, currently unreleased). Test **local solo** on a fresh client build unless an item says
> otherwise.
>
> _Why items 1–2 are hand-seeded: the Playtest #4 testers guide was generated on the **v0.2.30**
> build (`generated_from_tag: v0.2.30`, cut 00:32) and correctly describes the six v0.2.30 surfaces.
> But Playtest #4 actually **shipped under `v0.2.31`** (tagged 14:28), and **two `src/**/*.cs` PRs
> landed in the gap** — #216 (minimap-DISC margin) and #215 (modal chevron counter-rotate), both
> ~12:50 — **after the #4 guide was cut**, so the #4 guide describes neither. They get their first
> correct in-game checklist here (the same pattern by which the six v0.2.30 surfaces were hand-seeded
> into #4). Items 3–4 are the git cross-check candidates merged to `main` since v0.2.31 (the auto
> feeder) — see the cross-check section._

### 🆕 Shipped in v0.2.31 after the #4 guide was cut (test on v0.2.31 now)

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 1 | **Minimap DISC content-to-ring margin — closed (§2E.5.7)** | t_12e15162 (#216) | ✅ shipped #216 (`v0.2.31`); awaiting Daniel's GPU eyeball | nomap-ON, carry a named Local Map, open the **corner minimap disc** over a surveyed area. **AT-DISC-RING-1** — the cartography content edge **meets the bronze bezel ring with NO transparent annulus** (no live game world showing through a gap). This is the **second surface** of the content-to-ring family: #204 fixed the **MODAL** only; the **DISC** still gapped 5–32 px on most survey sizes (the `LayoutMapRect` integer-floored upscale made meshR < holeR; survey.Size=33 was a lucky clean case). The fix sizes the on-screen rect to the full **TargetPx** so meshR ≥ holeR for **every** survey size. **AT-DISC-RING-2** (regression #159) — cartography does **not** bleed past the ring under rotation. **AT-DISC-RING-3** — the disc's **125 m zoom/feel is unchanged** (the fog *texture* still upscales by the integer factor; only the on-screen rect size changed). **AT-DISC-RING-4** (THE LANDMINE) — **table pins + the in-disc player marker still land on the exact terrain cell** under rotation/zoom (every projection reads `edge` live from the rect; only `_mapRect.sizeDelta` changed, no second hard-coded edge literal — the #204 snapped-pin desync class). Headless `DiscRingGeometryTests` (meshR ≥ holeR ≤ ringOuterR swept across sizes 17..69 for disc TargetPx=200 + modal 900) + build 0/0 are green, but the parchment shader can't render headless — **AT-DISC-RING-1/2/4 are Daniel's GPU eyeball.** logs-green ≠ playable — closes t_12e15162. |
| 2 | **Modal / TableEdit in-disc chevron — counter-rotate to screen-up (§2H.2)** | t_423f5bd7 (#215) | ✅ shipped #215 (`v0.2.31`); awaiting Daniel's in-game look | Fixes Daniel's v0.2.30 report: *"the main map view has the player's chevron always facing north."* The held map already rotates-to-heading, but its in-disc chevron rode the rotating interior and pinned to **map-north** (the residual after §2H.1 rotation landed). **AT-MODAL-MARKER-1 (the fix)** — open the **main map (M / FieldReadOnly modal)**, turn the character **N→E→S→W**: the player chevron stays pointing **screen-up** (= your facing) while the map content rotates beneath it; it **no longer pins to map-north**. **AT-MODAL-MARKER-2 (regression — disc)** — the **corner minimap disc** chevron keeps its current correct screen-up behaviour (no double-rotation/flip). **AT-MODAL-MARKER-3 (regression — edge arrow)** — when the player is **outside** the surveyed disc on the modal, the orange **edge arrow** still points **outward toward the player's real bearing** under rotation (it is NOT counter-rotated — its own `angleDeg` composes with the container `+rotZ`). **AT-MODAL-MARKER-4 (TableEdit)** — open the **Surveyor's Table** (TableEdit) view and turn the character: the in-disc chevron is **also** screen-up while the table map rotates-to-heading (TableEdit was locked to rotate-to-heading on 2026-06-12 — same screen-up fix, not an exception). logs-green ≠ playable — Daniel's modal + table look closes t_423f5bd7. |

### 🆕 Merged to `main` since v0.2.31 — git cross-check candidates (need the next build tag)

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 3 | **Sunstone Lens → minimap detection handoff (any-minimap rule)** | t_91e86951 (#218) | ✅ merged to `main` (`f6456ed`); awaiting next build + Daniel | The full implementation of the Lens→minimap handoff (Daniel gated the 3 design knobs in #214, then directed the build). **When ANY minimap is present, the Lens' hostile detection moves ONTO it; the camera-relative trophy ring (Playtest #4 item 1) becomes the NO-minimap fallback only.** Defaults: `MinimapHandoffMode = DiscWhenBound`, `BlipStyle = Dots` (both live Config enums). Equip + solar-charge the Lens, approach hostiles (spawn Greydwarves / a Draugr Elite), and verify per the active minimap: **(a)** nomap-ON with the carry-disc bound → threat **blips ride the carry-disc** at each hostile's correct map position, counter-rotating + clearing with the disc (they ride `_pinObjects`); **(b)** nomap-OFF (vanilla corner minimap) → blips appear on the **vanilla corner minimap**, **north-up**, with the **aggro tint surviving** vanilla's per-frame `UpdatePins` (the overlay owns its `Image.color`); **(c) AT-LENS-DISC-NODRIFT** — tint/trophy/star derivation is single-sourced (`SunstoneProjection`), so a given hostile reads the **same** threat state on ring/disc/vanilla; **(d)** with **NO** minimap present, the camera-relative **trophy ring still renders** (the #4-item-1 behaviour, now the fallback — ring hides via `_content`, never the host, per the #209 dead-pump guard). Build 0/0, tests 186/186 (19 new truth-table cases), but render is GPU-only — Daniel's in-game look on each surface is the accept. logs-green ≠ playable — closes t_91e86951. _(Large render rework; if any surface behaves unexpectedly, verify against PR #218 / card t_91e86951 before filing a fix card.)_ |
| 4 | **Sunstone Lens NOT repairable at any station (`m_canBeReparied=false`)** | t_1afb94cd (#220) | ✅ merged to `main` (`ec057b1`); awaiting next build + Daniel | The Lens carries a durability/energy bar (`m_useDurability=true`) and crafts at the **Explorer's Bench**, so vanilla `InventoryGui.CanRepair` treated a partially-drained Lens as a valid **Repair** target there — a one-click free refill of the solar battery that bypassed the sunlight-only `CanRecharge` gate and defeated the sun-charge design. **AT-LENS-NOREPAIR** — with a **sun-depleted (partially-drained) Lens** in inventory: stand at the **Explorer's Bench** → the Lens does **NOT** appear as a repairable item (no hammer/repair affordance); confirm the same at a vanilla **Workbench** and **Forge** (non-repairable at **every** station, unconditionally — the flag short-circuits before any station-name match). The **charge meter + drain/recharge model are unchanged** (`m_useDurability` stays true); the **only** way to refill is **standing in sunlight**. logs-green ≠ playable — Daniel confirming no bench-repair + intact sun-charge closes t_1afb94cd. |
| 6 | **Map fullscreen (M) no longer steals the cursor (§2L.7-R)** | t_8b86adb3 (impl) / t_12acb9ce (spec #222) | ✅ merged to `main`; awaiting next build + Daniel | Fixes Daniel's 2026-06-20 report: *"both the localmap fullscreen and the painted sign 'steal' the mouse cursor."* This is the **map half (Surface A)** only — the Painted Sign (Surface B) is a separate open design decision still owed Daniel (accept / confine-to-rect / hide-until-move). `CursorPumpPatch` now keys on a narrower `CursorNeeded` (sign panels + the map viewer **only in TableEdit**) instead of `AnyOpen`, so the read-only M field map keeps mouse-look-freeze + world-input-block but no longer throws a free cursor over a surface with nothing to click. **AT-TABLE-FIELD-CURSOR** — equip a Local Map, press **M**: mouse-look stays frozen, WASD/world-input stays blocked, **NO free cursor appears**; close M → cursor re-locks + hides cleanly (no stuck-free cursor). **AT-TABLE-FIELD-CURSOR-REGRESSION** — open the **Surveyor's Table** (TableEdit): the free cursor **still** appears and pin-click-removal **still** works (the t_1f82da71 behaviour is preserved for the Table — only the M field view narrowed). **AT-SIGN-CURSOR-REGRESSION** — Painted Sign + Marker Sign panels still free the cursor; painting/text entry unaffected. **AT-CURSOR-PATCHCHECK** — `Runtime/PatchCheck` reports no UNREGISTERED PATCH CLASS at boot (the patch is unchanged; only the predicate it reads narrowed). Build 0/0, tests 186/186; cursor visibility is GPU-only so Daniel's eyeball on a client is the accept. logs-green ≠ playable — closes t_8b86adb3. |
| 7 | **Painted Sign board + border actually recolour (MaterialMan/MPB tint)** | t_f3310406 (impl) / t_24ad2570 (diagnosis) | ✅ merged to `main`; awaiting next build + Daniel | Fixes Daniel's 2026-06-20 report: *"I don't think the board is colored either, just the text."* The TMP letters recoloured but the **plank board** and the **two-tone border** never visibly changed. Root cause (decompiled `assembly_valheim.dll`, ADR-0001 base-game RE): every placed sign carries a `WearNTear`, and vanilla paints build-piece colour through a per-object `MaterialPropertyBlock` (MPB) managed by `MaterialMan` — an MPB **overrides** the material's own `_Color` at render time, so SBPR's old `sharedMaterials.SetColor("_Color")` write landed on a masked layer (the TMP text is a Canvas renderer outside `MaterialMan`, which is why only text worked). The fix tints board + border by writing `_Color` into each renderer's **own MPB** (`GetPropertyBlock`/`SetColor`/`SetPropertyBlock`) — per-renderer (NOT per-`GameObject` `MaterialMan.SetValue`, which would sweep the child-of-board border bars under one block and break two-tone). **AT-SIGN-BOARD-COLOR** — pick a Set Text Color swatch → the **plank board** visibly recolours to that tone (not just the letters). **AT-SIGN-BORDER-COLOR** — pick a Border Color swatch → the **border frame** visibly recolours, **independently** of the board (two-tone, §A2.6). **AT-SIGN-NONE** — `∅ None` on either slot reverts that element to plain wood (no stuck tint). **AT-SIGN-PERSIST** — both tints survive a relog / server restart (ZDO re-apply on spawn drives the MPB). **AT-SIGN-TEXT-REGRESSION** — the TMP text colour still works (the one already-working path is unregressed). **AT-SIGN-HIGHLIGHT-REASSERT** (architect-added) — hover a painted sign with the **Hammer** equipped: the red→green support-tint flashes, and after it clears (~0.2s) the **board + border paint returns** (not stuck on plain wood). The hammer overlay is the one thing that clobbers our `_Color` MPB; `SignMeshRetintPatch` (postfix on `WearNTear.Highlight`) debounces a one-shot re-assert ~0.3s after hover ends — the mesh-layer twin of `SignTextRetintPatch`. Build 0/0, SpecCheck +0 (no recipe/piece change). The `.diag-out/sbpr-sign-diag.sh` client kit can confirm the MPB write lands if needed, but the tint is GPU-only — **Daniel's eyeball on the next build is the accept.** logs-green ≠ playable — closes t_f3310406. |

### 🔁 Carried forward — not yet shipped / not yet verified

Did **not** ship a code change in any tag (blocked / verify-only), so it carries into #5 rather than
into #4's shipped archive.

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 5 | **Portal Seed crafting cost** | t_a6831e8e | `blocked` — verify local solo (NRE root-crash #154 shipped in #1) | At the Explorer's Bench, Portal Seed shows cost **AncientSeed ×1 + GreydwarfEye ×20 + SurtlingCore ×2**, and crafting **consumes** exactly that. Verify **local solo on current `main`** (the per-frame tooltip NRE that masked this, t_2dd7c705/#154, shipped in #1). If correct → close t_a6831e8e; if wrong → spawn a fix card from the observed failure mode (A no cost / B wrong cost / C not craftable / D shown-but-not-consumed). |

### 🧭 Ground-truth cross-check at roll time (git)

- **`src/**/*.cs` changes on `main` since `v0.2.31-playtest`: 4 commits** — #218 (`f6456ed`, card
  t_91e86951), #220 (`ec057b1`, card t_1afb94cd), the §2L.7-R cursor-narrow (card t_8b86adb3), and the
  Painted Sign MaterialMan/MPB tint fix (card t_f3310406) — **all four seeded above** as PENDING items
  3, 4, 6 & 7. The only non-gameplay post-tag change is the installer SHA pin **#219** (`a8561f9`) — a
  release chore, not a gameplay surface. So the auto cross-check is **clean (0 unledgered)**.
- Items **1–2** (#216/#215) shipped **inside** the `v0.2.31` tag **after** the Playtest #4 guide was
  cut on the `v0.2.30` build (00:32 < ~12:50 < tag 14:28), so they appear in **no** prior guide and
  are seeded by hand here for their first correct in-game checklist — the same pattern by which the
  six v0.2.30 surfaces were hand-seeded into #4.
- `scripts/gen-playtest-guide.py --ref main` confirms **2** code changes / **0** unledgered for this
  window (it diffs `v0.2.31..main`; both #218/#220 card ids are in the PENDING rows above).

### ⏳ In-flight (will join PENDING when merged)

- Nothing else `running` against `main` (#218/#220 are merged → PENDING items 3–4 above).
- **Open design PR #217** (`design/iron-compass-minimap-ring`, card t_85a46f42) — a **design-doc twin**
  of the Lens→minimap handoff: a compass-gated **north-ring on the minimap disc**. **Not built / not
  merged** → no test item until it ships.
- **Map fullscreen (M) cursor narrowing** (spec card t_12acb9ce / impl card t_8b86adb3) — Daniel's
  2026-06-20 playtest: the equipped Local Map full view (**M**) "steals the mouse cursor." Spec reverses
  cartography-impl-spec §2L.7 (→ §2L.7-R): the cursor pump narrows from `AnyOpen` to `CursorNeeded`
  (sign panels + Surveyor's-Table `TableEdit` only), so the read-only M map keeps mouse-look-freeze +
  input-block but no longer shows a free cursor; the Table keeps its pin-click cursor; the **Painted
  Sign** is a separate open design question (Surface B) NOT folded in. ✅ **Spec (#222) AND code both
  merged to `main`** → now a real PENDING test row above (item 6, AT-TABLE-FIELD-CURSOR +
  -REGRESSION). **Note for the #5 roll:** this does NOT touch archived Playtest #2 item 7 — that row is
  a true record of what shipped in v0.2.28; the M-map half of it was over-scoped and is being corrected
  forward, not rewritten in history. The **Painted Sign cursor** (Surface B) remains an open design
  decision owed Daniel — not yet a test item.

---

## ARCHIVE — shipped playtests

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
