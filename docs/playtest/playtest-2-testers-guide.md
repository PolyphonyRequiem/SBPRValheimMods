---
title: "SBPR Trailborne — Playtest #2 Testers Guide"
status: historical
purpose: "Playtest #2 — generated from playtest-ledger.md + git ground truth. Do not hand-edit; regenerate."
generated_from_tag: v0.2.27-playtest
diff_ref: main
---

# SBPR Trailborne — Playtest #2 Testers Guide

**Build:** SBPR Trailborne 0.2.28 (current `main`, ahead of `v0.2.27-playtest`)
**Test mode:** Local solo on a fresh client build (unless an item says otherwise).
**Generated:** 2026-06-19 17:28 PDT

> This guide is produced by `scripts/gen-playtest-guide.py` from the living
> **playtest ledger** and the actual code changes since `v0.2.27-playtest`. The
> **Playtest #2** number is the human-facing testing series — distinct from the
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
`Loading [SBPR Trailborne 0.2.28]` and `Harmony patches applied.`

## 2. Acceptance checklist

Check each item in-game. **Logs-green ≠ playable** — actually do the action.

### Test items (from the ledger)

> Build target: `v0.2.28-playtest` (SBPR Trailborne 0.2.28 — the v0.2.27 disc render
> build **plus** the unexplored-area full-shroud `_FogTex` G-channel fix #197). Test
> **local solo** on a fresh client build unless an item says otherwise.
>
> _Rebuild of Playtest #2 (counter held at 2): v0.2.27 shipped the disc render path but
> unexplored terrain rendered as the faded "shared-via-table" look; v0.2.28 corrects it to
> the full fog-of-war shroud. Re-cut on a new build tag, same playtest cycle._

### 🔁 Carried forward from Playtest #1 — not yet shipped / not yet verified

These were in #1's PENDING but did **not** ship in `v0.2.26-playtest` (or shipped but
remain unverified), so they roll into #2 rather than the #1 archive.

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 1 | **Disc player-marker chevron (A′)** | t_efe8b32b | ✅ merged #192 (`v0.2.27`) | Open the local-map **disc**; the player marker is the **vanilla default-map chevron** (not a blue quad), dead-centre, pointing **up = your facing** (NOT north — the disc rotates to heading). Confirm it reads as "you, forward = up" with no north-locked arrow. Logs note `using VANILLA art` vs `chevronFallback` — either is acceptable visually, but flag if it's the procedural fallback. |
| 1a | **Disc render-correctness — real fog cloud + circular clip** | t_ba31ad30 | ✅ merged #192 (`v0.2.27`) + **shroud fix #197 (`v0.2.28`)** | Open the disc: (a) **no opaque black square** behind it — outside the circle is transparent, the world shows through; (b) the unexplored area inside renders as **vanilla's real fog-of-war cloud** (matching the normal map's unexplored look), NOT a flat dark fill **and NOT the faded "shared-via-table" look** (the #197 G-channel fix — see item 6); (c) the interior is a **continuous disc** — no rotated-square/diamond, no ocean bleeding in at the corners; (d) the bronze bezel ring is visible. GPU-verified on Prime; Daniel's eye on the live client is the final accept. |
| 1b | **Minimap disc fixed tight zoom (125 m)** | t_ba31ad30 | ✅ merged #192 (`v0.2.27`) | The corner disc now frames a **tight ~125 m local window** around you (a small portion of the surveyed area), NOT the whole survey — you should read immediate surroundings, like a vanilla minimap. There is **no zoom input** (`,`/`.`/scroll do nothing on the disc by design). To see the whole local map, open the **full map (M)**, which stays at its full-survey scale. Verify: terrain, pins, and the chevron all sit at the same scale (a pin on a landmark lands ON that landmark at the tight zoom). 125 m is a starting value — tell Daniel if it feels too tight/loose. |
| 2 | **Sunstone Lens — trophy-ring detection (redesign)** | t_b8a19487 | `blocked` — REDESIGN in flight | Target design: a **trophy ring** around the player — each hostile's trophy billboarded at its bearing, size ∝ closeness, ★/★★ pips for star levels. Until the redesign lands, verify whether the **currently-shipped v3 lens** (placeholder text HUD, shipped in #1 as item 5) shows **anything** when worn+charged near a hostile — helps confirm the wiring vs. a dead HUD. |
| 3 | **Portal Seed crafting cost** | t_a6831e8e | `blocked` — verify local solo (NRE root-crash #154 shipped in #1) | At the Explorer's Bench, Portal Seed shows cost **AncientSeed ×1 + GreydwarfEye ×20 + SurtlingCore ×2**, and crafting **consumes** exactly that. Verify **local solo on current `main`** (the per-frame tooltip NRE that masked this, t_2dd7c705/#154, shipped in #1). If correct → close t_a6831e8e; if wrong → spawn a fix card from the observed failure mode (A no cost / B wrong cost / C not craftable / D shown-but-not-consumed). |
| 6 | **Unexplored = full shroud, not the shared-table look** | t_48c23824 | ✅ merged #197 (`v0.2.28`) | **THE fix this rebuild ships.** Open the local-map disc **and** the full map (M) / Surveyor's Table with a partially-explored survey. The **unexplored** area must render as Valheim's **solid fog-of-war shroud** — the same dark cloud you see on a fresh regular map for terrain nobody has visited — **NOT** the lighter, faded "someone shared this map with you via the cartography table" look it showed in v0.2.27. Explored/surveyed area still shows full bounded cartography (biome/water/relief). Root cause was a one-channel encoding bug: the reveal tagged unexplored as `_FogTex` G=0 (= shared-by-others) instead of G=255 (= nobody-explored). Daniel's eye on the GPU client is the accept (AT-FOG-VANILLA). |
| 7 | **Map-table / full-map cursor is free to click** | t_1f82da71 | ✅ merged #196 (`v0.2.28`) | At the **Surveyor's Table** map and the equipped **Local Map full view (M)**, the mouse cursor is **free to move and click** (e.g. to click pins for removal) — NOT locked to screen-centre. Mouse-look stays frozen while the modal is open (you don't pan the world). On **close**, the cursor re-locks and the game resumes normal mouse-look exactly once (no stuck-free cursor after exit). Fixes Daniel's 06-19 "my mouse is not free to move and click on pins" report; root cause was the cursor-release hooked an empty/inlined vanilla method — re-seated onto `GameCamera.LateUpdate`. |

### 🧭 Ground-truth cross-check at roll time (git)

- **`src/**/*.cs` changes on `main` since `v0.2.27-playtest`: 2 commits.**
  - **PR #197** (squash `bf562ec`, card t_48c23824) — the unexplored-area full-shroud
    `_FogTex` **G-channel** fix in `MapSurface.BindBoundedReveal` (fogged texels now R=255
    **and** G=255 = nobody-explored → solid shroud, instead of R=255/G=0 = shared-by-others;
    plus a `_SharedFade=0` pin on the cloned material). This is **item 6** above.
  - **PR #196** (squash `f3fc663`, card t_1f82da71) — modal cursor-release re-seated onto
    the live `GameCamera.LateUpdate` seam (the old hook targeted an emptied/inlined vanilla
    method), freeing the cursor at the Surveyor's Table + Local Map full view. This is
    **item 7** above.
  - `scripts/gen-playtest-guide.py --tag v0.2.27-playtest --ref main` accrues both auto items.
- **Prior cycle (`v0.2.26-playtest`..`v0.2.27-playtest`): PR #192** (commit `678f9aa`,
  cards t_ba31ad30 + t_efe8b32b) — the MapSurface render-correctness fix (real `_FogTex`
  cloud, geometry circular clip, transparent-outside bezel), the disc player-marker chevron,
  and the 125 m fixed-zoom decouple (`MapViewer`/`MapSurface`, new `CircularRawImage.cs`).
  These are items 1 / 1a / 1b above.

### ⏳ In-flight (will join PENDING when merged)

- _(none beyond the carried-forward redesign t_b8a19487 above)_

---


## 3. Ground-truth cross-check (auto)

Code commits touching `src/**/*.cs` since **v0.2.27-playtest**: **2**


✅ Every merged code change maps to a ledger item. No silent-untested changes.


## 4. After the playtest


- Record results inline (check the boxes, note failures).
- File a kanban card per failure (assign the right specialist; the planner cron can seed these).
- When the next `-playtest` tag ships, `sbpr-playtest-planner` archives this list under
  Playtest #2 in the ledger, bumps the counter, and opens the Playtest #3 planning card.
