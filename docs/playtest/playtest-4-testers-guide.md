---
title: "SBPR Trailborne — Playtest #4 Testers Guide"
status: current
purpose: "Playtest #4 — generated from playtest-ledger.md + git ground truth. Do not hand-edit; regenerate."
generated_from_tag: v0.2.30-playtest
diff_ref: main
---

# SBPR Trailborne — Playtest #4 Testers Guide

**Build:** SBPR Trailborne 0.2.30 (current `main`, ahead of `v0.2.30-playtest`)
**Test mode:** Local solo on a fresh client build (unless an item says otherwise).
**Generated:** 2026-06-20 00:28 PDT

> This guide is produced by `scripts/gen-playtest-guide.py` from the living
> **playtest ledger** and the actual code changes since `v0.2.30-playtest`. The
> **Playtest #4** number is the human-facing testing series — distinct from the
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
`Loading [SBPR Trailborne 0.2.30]` and `Harmony patches applied.`

## 2. Acceptance checklist

Check each item in-game. **Logs-green ≠ playable** — actually do the action.

### Test items (from the ledger)

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


## 3. Ground-truth cross-check (auto)

Code commits touching `src/**/*.cs` since **v0.2.30-playtest**: **0**


✅ Every merged code change maps to a ledger item. No silent-untested changes.


## 4. After the playtest


- Record results inline (check the boxes, note failures).
- File a kanban card per failure (assign the right specialist; the planner cron can seed these).
- When the next `-playtest` tag ships, `sbpr-playtest-planner` archives this list under
  Playtest #4 in the ledger, bumps the counter, and opens the Playtest #5 planning card.
