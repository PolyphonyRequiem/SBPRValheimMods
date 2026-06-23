---
title: "SBPR Trailborne — Playtest #6 Testers Guide"
status: current
purpose: "Playtest #6 — generated from playtest-ledger.md + git ground truth. Do not hand-edit; regenerate."
generated_from_tag: v0.2.34-playtest
diff_ref: main
---

# SBPR Trailborne — Playtest #6 Testers Guide

**Build:** SBPR Trailborne 0.2.34 (current `main`, ahead of `v0.2.34-playtest`)
**Test mode:** Local solo on a fresh client build (unless an item says otherwise).
**Generated:** 2026-06-22 12:18 PDT

> This guide is produced by `scripts/gen-playtest-guide.py` from the living
> **playtest ledger** and the actual code changes since `v0.2.34-playtest`. The
> **Playtest #6** number is the human-facing testing series — distinct from the
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
`Loading [SBPR Trailborne 0.2.34]` and `Harmony patches applied.`

## 2. Acceptance checklist

Check each item in-game. **Logs-green ≠ playable** — actually do the action.

### Test items (from the ledger)

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


## 3. Ground-truth cross-check (auto)

Code commits touching `src/**/*.cs` since **v0.2.34-playtest**: **0**


✅ Every merged code change maps to a ledger item. No silent-untested changes.


## 4. After the playtest


- Record results inline (check the boxes, note failures).
- File a kanban card per failure (assign the right specialist; the planner cron can seed these).
- When the next `-playtest` tag ships, `sbpr-playtest-planner` archives this list under
  Playtest #6 in the ledger, bumps the counter, and opens the Playtest #7 planning card.
