---
title: "SBPR Trailborne — Playtest #3 Testers Guide"
status: current
purpose: "Playtest #3 — generated from playtest-ledger.md + git ground truth. Do not hand-edit; regenerate."
generated_from_tag: v0.2.29-playtest
diff_ref: main
---

# SBPR Trailborne — Playtest #3 Testers Guide

**Build:** SBPR Trailborne 0.2.29 (current `main`, ahead of `v0.2.29-playtest`)
**Test mode:** Local solo on a fresh client build (unless an item says otherwise).
**Generated:** 2026-06-19 18:25 PDT

> This guide is produced by `scripts/gen-playtest-guide.py` from the living
> **playtest ledger** and the actual code changes since `v0.2.29-playtest`. The
> **Playtest #3** number is the human-facing testing series — distinct from the
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
`Loading [SBPR Trailborne 0.2.29]` and `Harmony patches applied.`

## 2. Acceptance checklist

Check each item in-game. **Logs-green ≠ playable** — actually do the action.

### Test items (from the ledger)

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
  on the disc/modal (t_caf0f1cf), [M] name-hint under the disc (t_338f723b / t_26bba85b),
  equipable-icon transparent-bg (t_b9a111ca, PR #201 open).

---


## 3. Ground-truth cross-check (auto)

Code commits touching `src/**/*.cs` since **v0.2.29-playtest**: **0**


✅ Every merged code change maps to a ledger item. No silent-untested changes.


## 4. After the playtest


- Record results inline (check the boxes, note failures).
- File a kanban card per failure (assign the right specialist; the planner cron can seed these).
- When the next `-playtest` tag ships, `sbpr-playtest-planner` archives this list under
  Playtest #3 in the ledger, bumps the counter, and opens the Playtest #4 planning card.
