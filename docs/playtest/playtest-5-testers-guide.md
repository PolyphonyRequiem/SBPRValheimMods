---
title: "SBPR Trailborne — Playtest #5 Testers Guide"
status: current
purpose: "Playtest #5 — generated from playtest-ledger.md + git ground truth. Do not hand-edit; regenerate."
generated_from_tag: v0.2.31-playtest
diff_ref: main
---

# SBPR Trailborne — Playtest #5 Testers Guide

**Build:** SBPR Trailborne 0.2.33 (current `main`, ahead of `v0.2.31-playtest`)
**Test mode:** Local solo on a fresh client build (unless an item says otherwise).
**Generated:** 2026-06-21 16:40 PDT

> This guide is produced by `scripts/gen-playtest-guide.py` from the living
> **playtest ledger** and the actual code changes since `v0.2.31-playtest`. The
> **Playtest #5** number is the human-facing testing series — distinct from the
> `vX.Y.Z-playtest` build tags.

---

## 1. Install on your client (one-time per build)

**Easiest — the one-line installer** (copies Valheim to a separate modded folder;
your vanilla install is never touched; bundles BepInEx + Trailborne +
ServerDevcommands and prints the live join code):

- **Windows (PowerShell):**
  ```powershell
  iwr https://raw.githubusercontent.com/PolyphonyRequiem/SBPRValheimMods/main/installer.ps1 -UseBasicParsing | iex
  ```
- **Linux / macOS (bash):**
  ```bash
  curl -fsSL https://raw.githubusercontent.com/PolyphonyRequiem/SBPRValheimMods/main/installer.sh | bash
  ```

Both verify the modpack SHA256 before installing and write a launcher
(`Play Trailborne` shortcut / `run-trailborne.sh`). Pass `--no-console` (bash) /
`-NoConsole` (PS1) to omit the F5 dev console.

**Manual alternative:** install BepInExPack_Valheim (r2modman or manual), then copy
this build's `BepInEx/plugins/SBPR.Trailborne/` from the release zip into your install.

Either way, launch Valheim and confirm the BepInEx console logs
`Loading [SBPR Trailborne 0.2.33]` and `Harmony patches applied.`

## 2. Acceptance checklist

Check each item in-game. **Logs-green ≠ playable** — actually do the action.

### Test items (from the ledger)

> Build target: **`v0.2.33-playtest`** (SBPR Trailborne 0.2.33) — the build that carries Playtest #5.
> Items **1–2** already shipped in the v0.2.31 tag; items **3–4 (#218/#220), 6 (#224), 7 (#230) and
> 8 (#228)** are all **merged to `main` and ship in `v0.2.33-playtest`** (cut 2026-06-21). The interim
> `v0.2.32` tag (cut earlier the same day) carried 3–4/6/7 but **not** the signs three-slot #228, which
> merged after it — so v0.2.33 is the build to test, and v0.2.32 is superseded for Playtest #5. Test
> **local solo** on a fresh client build unless an item says otherwise.
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

### 🆕 Merged to `main` since v0.2.31 — ship in `v0.2.33-playtest` (Playtest #5 build)

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 3 | **Sunstone Lens → minimap detection handoff (any-minimap rule)** | t_91e86951 (#218) | ✅ merged to `main` (`f6456ed`); awaiting next build + Daniel | The full implementation of the Lens→minimap handoff (Daniel gated the 3 design knobs in #214, then directed the build). **When ANY minimap is present, the Lens' hostile detection moves ONTO it; the camera-relative trophy ring (Playtest #4 item 1) becomes the NO-minimap fallback only.** Defaults: `MinimapHandoffMode = DiscWhenBound`, `BlipStyle = Dots` (both live Config enums). Equip + solar-charge the Lens, approach hostiles (spawn Greydwarves / a Draugr Elite), and verify per the active minimap: **(a)** nomap-ON with the carry-disc bound → threat **blips ride the carry-disc** at each hostile's correct map position, counter-rotating + clearing with the disc (they ride `_pinObjects`); **(b)** nomap-OFF (vanilla corner minimap) → blips appear on the **vanilla corner minimap**, **north-up**, with the **aggro tint surviving** vanilla's per-frame `UpdatePins` (the overlay owns its `Image.color`); **(c) AT-LENS-DISC-NODRIFT** — tint/trophy/star derivation is single-sourced (`SunstoneProjection`), so a given hostile reads the **same** threat state on ring/disc/vanilla; **(d)** with **NO** minimap present, the camera-relative **trophy ring still renders** (the #4-item-1 behaviour, now the fallback — ring hides via `_content`, never the host, per the #209 dead-pump guard). Build 0/0, tests 186/186 (19 new truth-table cases), but render is GPU-only — Daniel's in-game look on each surface is the accept. logs-green ≠ playable — closes t_91e86951. _(Large render rework; if any surface behaves unexpectedly, verify against PR #218 / card t_91e86951 before filing a fix card.)_ |
| 4 | **Sunstone Lens NOT repairable at any station (`m_canBeReparied=false`)** | t_1afb94cd (#220) | ✅ merged to `main` (`ec057b1`); awaiting next build + Daniel | The Lens carries a durability/energy bar (`m_useDurability=true`) and crafts at the **Explorer's Bench**, so vanilla `InventoryGui.CanRepair` treated a partially-drained Lens as a valid **Repair** target there — a one-click free refill of the solar battery that bypassed the sunlight-only `CanRecharge` gate and defeated the sun-charge design. **AT-LENS-NOREPAIR** — with a **sun-depleted (partially-drained) Lens** in inventory: stand at the **Explorer's Bench** → the Lens does **NOT** appear as a repairable item (no hammer/repair affordance); confirm the same at a vanilla **Workbench** and **Forge** (non-repairable at **every** station, unconditionally — the flag short-circuits before any station-name match). The **charge meter + drain/recharge model are unchanged** (`m_useDurability` stays true); the **only** way to refill is **standing in sunlight**. logs-green ≠ playable — Daniel confirming no bench-repair + intact sun-charge closes t_1afb94cd. |
| 6 | **Painted Sign board + border actually recolour (MaterialMan/MPB tint)** | t_f3310406 (impl) / t_24ad2570 (diagnosis) | ✅ merged to `main`; awaiting next build + Daniel | Fixes Daniel's 2026-06-20 report: *"I don't think the board is colored either, just the text."* The TMP letters recoloured but the **plank board** and the **two-tone border** never visibly changed. Root cause (decompiled `assembly_valheim.dll`, ADR-0001 base-game RE): every placed sign carries a `WearNTear`, and vanilla paints build-piece colour through a per-object `MaterialPropertyBlock` (MPB) managed by `MaterialMan` — an MPB **overrides** the material's own `_Color` at render time, so SBPR's old `sharedMaterials.SetColor("_Color")` write landed on a masked layer (the TMP text is a Canvas renderer outside `MaterialMan`, which is why only text worked). The fix tints board + border by writing `_Color` into each renderer's **own MPB** (`GetPropertyBlock`/`SetColor`/`SetPropertyBlock`) — per-renderer (NOT per-`GameObject` `MaterialMan.SetValue`, which would sweep the child-of-board border bars under one block and break two-tone). **AT-SIGN-BOARD-COLOR** — pick a Set Text Color swatch → the **plank board** visibly recolours to that tone (not just the letters). **AT-SIGN-BORDER-COLOR** — pick a Border Color swatch → the **border frame** visibly recolours, **independently** of the board (two-tone, §A2.6). **AT-SIGN-NONE** — `∅ None` on either slot reverts that element to plain wood (no stuck tint). **AT-SIGN-PERSIST** — both tints survive a relog / server restart (ZDO re-apply on spawn drives the MPB). **AT-SIGN-TEXT-REGRESSION** — the TMP text colour still works (the one already-working path is unregressed). **AT-SIGN-HIGHLIGHT-REASSERT** (architect-added) — hover a painted sign with the **Hammer** equipped: the red→green support-tint flashes, and after it clears (~0.2s) the **board + border paint returns** (not stuck on plain wood). The hammer overlay is the one thing that clobbers our `_Color` MPB; `SignMeshRetintPatch` (postfix on `WearNTear.Highlight`) debounces a one-shot re-assert ~0.3s after hover ends — the mesh-layer twin of `SignTextRetintPatch`. Build 0/0, SpecCheck +0 (no recipe/piece change). The `.diag-out/sbpr-sign-diag.sh` client kit can confirm the MPB write lands if needed, but the tint is GPU-only — **Daniel's eyeball on the next build is the accept.** logs-green ≠ playable — closes t_f3310406. |

| 7 | **Iron Compass → minimap north-ring (M1): compass-gated iron bezel + N-glyph (disc + modal)** | t_fb53c9e4 (#230) | ✅ merged to `main` (`3337bbe`); ships in `v0.2.33`; awaiting Daniel | M1 of the iron-compass-minimap-ring impl-spec (design t_85a46f42/#226 → spec t_ed803a83/#229 → code t_fb53c9e4/#230). When the **Iron Compass is worn AND an SBPR map surface is showing**, draw a compass-gated **north ring** on that surface — an **iron-bezel recolor** + an **N-glyph + cardinal ticks** — and **hide** the TopCenter HUD needle while the surface ring is up (`CompassDiscMode=DiscWhenBound`, the default; also `HudOnly`/`Both`). **M1 does NOT change the rotation math** — `CompassAutoNorthUp` is bound but **inert** (that's M2, a later card). **AT-COMPASS-DISC-RING** — worn compass + carry-disc minimap: the disc bezel recolors **iron** (cool grey, IronTint `(0.66,0.68,0.72)` ≈ RGB 168/173/184 — a **first-guess value to tune by eye against a real iron item**) and an **N-glyph + ticks** appear. **AT-COMPASS-MODAL-RING** — same on the full-map **(M) FieldReadOnly modal**. **AT-COMPASS-DISC-ROTATE** — turn N→E→S→W: the **N-glyph orbits to stay at true map-north** (it rides the rotating `_mapContainer`, counter-rotated), while the **bezel recolor is rotation-invariant** (non-rotating `_frame`). **AT-COMPASS-BEZEL-GATED** — **unequip** the compass → bezel reverts to **bronze**, N-glyph + ticks vanish, HUD needle returns; **re-equip** → ring returns. **AT-DISC-NORTH-GATED** — with NO compass worn the surface shows **no** north ring (HUD-needle-only path). **AT-COMPASS-DISC-PUMP** (regression #208/#209) — the needle-hide toggles `_content`, never the host pump, so the yaw needle **never freezes** after toggling. **Flagged deviation to eyeball:** the N-glyph is gated on the bezel being a visible ring, so it shows on disc + FieldReadOnly modal but **stays off the square TableEdit** pin-editing view (no bezel there) — say if you want it on TableEdit too. Build 0/0, tests 226/226 (40 new `CompassNorthGate` AT-COMPASS-GATE cases); render is GPU-only — Daniel's eyeball + IronTint/N-glyph tuning is the accept. logs-green ≠ playable — closes t_fb53c9e4. |
| 8 | **Painted Sign — three independent paint slots (letters / board / frame) + stained-wood basis** | t_6cc9f652 (#228) | ✅ merged to `main` (`130663e`); ships in `v0.2.33`; awaiting Daniel | **Supersedes item 6** for the next build — extends the #224 MPB tint into **three independent color slots**, each tinting exactly one surface: **Text Color** → letters, **Board Color** → the plank mesh *(NEW slot)*, **Border Color** → the frame bars. Fixes Daniel's 2026-06-20 follow-up that the border "didn't look colored": the prior interim wiring tinted board+frame the **same** tone (no edge contrast — invisible as a frame, doubly so red-on-red for a colorblind eye). Now a sign can read e.g. **white board / red frame / blue letters**. Also folds in the **stained-wood albedo fix** (t_6cc9f652): the per-renderer MPB `_Color` used to multiply over the brown wood albedo (white washed out, red→maroon); a neutralized grain copy is now pushed through the same MPB as `_MainTex` so color reads **true** with grain still showing. **AT-SIGN-3SLOT-INDEPENDENT** — paint three *different* colors and confirm three surfaces land independently (e.g. white board, **distinctly-lighter-or-darker** red frame that reads as an edge, blue letters — differentiate by **lightness/value**, not hue alone). **AT-SIGN-BOARD-COLOR** / **AT-SIGN-BORDER-COLOR** / **AT-SIGN-TEXT-COLOR** — each slot recolors only its own surface. **AT-SIGN-NONE** — `∅ None` clears each slot independently back to plain stained wood (no stuck tint, no muddy multiply). **AT-SIGN-PERSIST** — all three survive a relog / server restart (per-slot ZDO; legacy `SBPR_SignColor` migrates to the **board** slot). **AT-SIGN-HIGHLIGHT-REASSERT** — hammer-hover re-asserts paint after the support-tint flash clears. **Cost model:** 1 pigment per *filled* slot (same color in N slots = N pigments). GPU-verified on Prime (MPB readback: board white `(0.95,0.94,0.88)`, frame red `(0.85,0.18,0.18)` = RGB 217/46/46, independent); build 0/0, 186/186 tests — but the on-screen tint is Daniel's eyeball. logs-green ≠ playable — closes t_6cc9f652. |

### 🔁 Carried forward — not yet shipped / not yet verified

Did **not** ship a code change in any tag (blocked / verify-only), so it carries into #5 rather than
into #4's shipped archive.

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 5 | **Portal Seed crafting cost** | t_a6831e8e | `blocked` — verify local solo (NRE root-crash #154 shipped in #1) | At the Explorer's Bench, Portal Seed shows cost **AncientSeed ×1 + GreydwarfEye ×20 + SurtlingCore ×2**, and crafting **consumes** exactly that. Verify **local solo on current `main`** (the per-frame tooltip NRE that masked this, t_2dd7c705/#154, shipped in #1). If correct → close t_a6831e8e; if wrong → spawn a fix card from the observed failure mode (A no cost / B wrong cost / C not craftable / D shown-but-not-consumed). |

### 🧭 Ground-truth cross-check at roll time (git)

- **`src/**/*.cs` changes on `main` since `v0.2.31-playtest`: 7 commits**, 5 of them net-new gameplay
  surfaces seeded above — #218 (`f6456ed`, card t_91e86951, item 3), #220 (`ec057b1`, card t_1afb94cd,
  item 4), the Painted Sign MPB tint fix (`600781f`, card t_f3310406, #224, item 6), the Iron-Compass
  M1 north-ring (`3337bbe`, card t_fb53c9e4, #230, **item 7**), and the Painted Sign three-slot +
  stained-wood basis (`130663e`, card t_6cc9f652, #228, **item 8**). The remaining 2 are the **§2L.7-R
  cursor narrowing** (`2b0387e`, card t_8b86adb3, #223 — and its spec #222, card t_12acb9ce) which was
  **reverted on `main` by #225** (`b618aa8`) as a wrong-direction fix — **net-zero diff, no test item**.
  The only non-gameplay post-tag changes are the installer SHA pins **#219** (`a8561f9`, v0.2.31) and
  **#231** (`ae4cb2a`, v0.2.32) — release chores, not gameplay surfaces. So the auto cross-check is
  **clean (0 unledgered)**: every shipped gameplay change maps to a PENDING item; the reverted cursor
  pair (cards t_8b86adb3 / t_12acb9ce) is named here so the generator's card-id cross-check stays clean.
- Items **1–2** (#216/#215) shipped **inside** the `v0.2.31` tag **after** the Playtest #4 guide was
  cut on the `v0.2.30` build (00:32 < ~12:50 < tag 14:28), so they appear in **no** prior guide and
  are seeded by hand here for their first correct in-game checklist — the same pattern by which the
  six v0.2.30 surfaces were hand-seeded into #4.
- **Build note:** the interim `v0.2.32-playtest` tag (cut 2026-06-21 15:32) carried items 3–4/6/7 but
  merged **before** the signs three-slot #228 (item 8, merged 16:11). Playtest #5 therefore ships under
  **`v0.2.33-playtest`** — the first build with all of 1–8. Counter held at #5 (Daniel hasn't played it
  yet — re-cut on a new build tag, not a roll to #6); `last_playtest_tag` advanced to `v0.2.33-playtest`
  so the planner cron stays silent.
- `scripts/gen-playtest-guide.py --tag v0.2.31-playtest --ref main` confirms **7** code changes /
  **0** unledgered for this window (it diffs `v0.2.31..main`; every card id above is in the PENDING rows
  or named in this cross-check).

### ⏳ In-flight (will join PENDING when merged)

- Nothing `running` against `main` for Playtest #5 — items 3–4/6/7/8 are all merged and ship in
  `v0.2.33-playtest`.
- The **iron-compass-minimap-ring** (design t_85a46f42/#226 → impl-spec t_ed803a83/#229) **graduated
  to buildable and shipped its M1** as item 7 above (code card t_fb53c9e4/#230). **M2** — the opt-in
  `CompassAutoNorthUp` north-up lock (default OFF, bound-but-inert in M1) — is a **separate later
  card**, not yet built → no test item until it ships.

---


## 3. Ground-truth cross-check (auto)

Code commits touching `src/**/*.cs` since **v0.2.31-playtest**: **7**


✅ Every merged code change maps to a ledger item. No silent-untested changes.


## 4. After the playtest


- Record results inline (check the boxes, note failures).
- File a kanban card per failure (assign the right specialist; the planner cron can seed these).
- When the next `-playtest` tag ships, `sbpr-playtest-planner` archives this list under
  Playtest #5 in the ledger, bumps the counter, and opens the Playtest #6 planning card.
