---
title: "SBPR Trailborne — Playtest #5 Testers Guide"
status: current
purpose: "Playtest #5 — generated from playtest-ledger.md + git ground truth. Do not hand-edit; regenerate."
generated_from_tag: v0.2.31-playtest
diff_ref: main
---

# SBPR Trailborne — Playtest #5 Testers Guide

**Build:** SBPR Trailborne 0.2.31 (current `main`, ahead of `v0.2.31-playtest`)
**Test mode:** Local solo on a fresh client build (unless an item says otherwise).
**Generated:** 2026-06-20 18:21 PDT

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
`Loading [SBPR Trailborne 0.2.31]` and `Harmony patches applied.`

## 2. Acceptance checklist

Check each item in-game. **Logs-green ≠ playable** — actually do the action.

### Test items (from the ledger)

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

### 🔁 Carried forward — not yet shipped / not yet verified

Did **not** ship a code change in any tag (blocked / verify-only), so it carries into #5 rather than
into #4's shipped archive.

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 5 | **Portal Seed crafting cost** | t_a6831e8e | `blocked` — verify local solo (NRE root-crash #154 shipped in #1) | At the Explorer's Bench, Portal Seed shows cost **AncientSeed ×1 + GreydwarfEye ×20 + SurtlingCore ×2**, and crafting **consumes** exactly that. Verify **local solo on current `main`** (the per-frame tooltip NRE that masked this, t_2dd7c705/#154, shipped in #1). If correct → close t_a6831e8e; if wrong → spawn a fix card from the observed failure mode (A no cost / B wrong cost / C not craftable / D shown-but-not-consumed). |

### 🧭 Ground-truth cross-check at roll time (git)

- **`src/**/*.cs` changes on `main` since `v0.2.31-playtest`: 2 commits** — #218 (`f6456ed`, card
  t_91e86951) and #220 (`ec057b1`, card t_1afb94cd) — **both seeded above** as PENDING items 3 & 4.
  The only other post-tag change is the installer SHA pin **#219** (`a8561f9`) — a release chore, not
  a gameplay surface. So the auto cross-check is **clean (0 unledgered)**.
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

---


## 3. Ground-truth cross-check (auto)

Code commits touching `src/**/*.cs` since **v0.2.31-playtest**: **2**


✅ Every merged code change maps to a ledger item. No silent-untested changes.


## 4. After the playtest


- Record results inline (check the boxes, note failures).
- File a kanban card per failure (assign the right specialist; the planner cron can seed these).
- When the next `-playtest` tag ships, `sbpr-playtest-planner` archives this list under
  Playtest #5 in the ledger, bumps the counter, and opens the Playtest #6 planning card.
