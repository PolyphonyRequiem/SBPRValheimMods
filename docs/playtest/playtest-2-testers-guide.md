---
title: "SBPR Trailborne — Playtest #2 Testers Guide"
status: current
purpose: "Playtest #2 — generated from playtest-ledger.md + git ground truth. Do not hand-edit; regenerate."
generated_from_tag: v0.2.26-playtest
diff_ref: main
---

# SBPR Trailborne — Playtest #2 Testers Guide

**Build:** SBPR Trailborne 0.2.26 (current `main`, ahead of `v0.2.26-playtest`)
**Test mode:** Local solo on a fresh client build (unless an item says otherwise).
**Generated:** 2026-06-18 18:13 PDT

> This guide is produced by `scripts/gen-playtest-guide.py` from the living
> **playtest ledger** and the actual code changes since `v0.2.26-playtest`. The
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
`Loading [SBPR Trailborne 0.2.26]` and `Harmony patches applied.`

## 2. Acceptance checklist

Check each item in-game. **Logs-green ≠ playable** — actually do the action.

### Test items (from the ledger)

> Build target: current `main` (at/ahead of `v0.2.26-playtest`). Test **local solo**
> on a fresh client build unless an item says otherwise.

### 🔁 Carried forward from Playtest #1 — not yet shipped / not yet verified

These were in #1's PENDING but did **not** ship in `v0.2.26-playtest` (or shipped but
remain unverified), so they roll into #2 rather than the #1 archive.

| # | Feature | Card | Status | What to verify in-game |
|---|---------|------|--------|------------------------|
| 1 | **Disc player-marker chevron (A′)** | t_efe8b32b | `todo` — impl not merged (not in any branch) | When it lands: open the local-map **disc**; the player marker is the **vanilla default-map chevron** (not a blue quad), dead-centre, pointing **up = your facing** (NOT north — the disc rotates to heading). Confirm it reads as "you, forward = up" with no north-locked arrow. |
| 2 | **Sunstone Lens — trophy-ring detection (redesign)** | t_b8a19487 | `blocked` — REDESIGN in flight | Target design: a **trophy ring** around the player — each hostile's trophy billboarded at its bearing, size ∝ closeness, ★/★★ pips for star levels. Until the redesign lands, verify whether the **currently-shipped v3 lens** (placeholder text HUD, shipped in #1 as item 5) shows **anything** when worn+charged near a hostile — helps confirm the wiring vs. a dead HUD. |
| 3 | **Portal Seed crafting cost** | t_a6831e8e | `blocked` — verify local solo (NRE root-crash #154 shipped in #1) | At the Explorer's Bench, Portal Seed shows cost **AncientSeed ×1 + GreydwarfEye ×20 + SurtlingCore ×2**, and crafting **consumes** exactly that. Verify **local solo on current `main`** (the per-frame tooltip NRE that masked this, t_2dd7c705/#154, shipped in #1). If correct → close t_a6831e8e; if wrong → spawn a fix card from the observed failure mode (A no cost / B wrong cost / C not craftable / D shown-but-not-consumed). |

### 🧭 Ground-truth cross-check at roll time (git)

- **No `src/**/*.cs` changes on `main` since `v0.2.26-playtest`.** The only post-tag
  commits are docs + the new Linux/macOS bash installer (`installer.sh`) and the
  installer SHA pin — no gameplay code. So Playtest #2 opens with **no new
  auto-derived code items**; it carries the three judgment items above until new work
  merges. `scripts/gen-playtest-guide.py --ref main` will accrue auto items as code lands.

### ⏳ In-flight (will join PENDING when merged)

- _(none beyond the carried-forward redesign t_b8a19487 above)_

---


## 3. Ground-truth cross-check (auto)

Code commits touching `src/**/*.cs` since **v0.2.26-playtest**: **0**


✅ Every merged code change maps to a ledger item. No silent-untested changes.


## 4. After the playtest


- Record results inline (check the boxes, note failures).
- File a kanban card per failure (assign the right specialist; the planner cron can seed these).
- When the next `-playtest` tag ships, `sbpr-playtest-planner` archives this list under
  Playtest #2 in the ledger, bumps the counter, and opens the Playtest #3 planning card.
