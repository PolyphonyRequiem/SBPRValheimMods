---
title: "SBPR Trailborne — Playtest Ledger"
status: living
purpose: >
  Durable, accumulating record of what needs in-game testing. Items land here as
  work merges (no reliance on memory). When a playtest is prepped, scripts/gen-playtest-guide.py
  rolls the PENDING section + git-derived code changes into a numbered Playtest #N testers guide,
  then the shipped items move to the ARCHIVE section under that playtest number.
playtest_counter: 2            # next playtest is #2 (human-facing series; DISTINCT from vX.Y.Z-playtest tags)
last_playtest_tag: v0.2.26-playtest
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

## PENDING — accrues for the next playtest (Playtest #2)

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

## ARCHIVE — shipped playtests

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
| 5 | **Swamp Sunstone Lens trinket** | t_2fd7bc7f | (tier) | Solar-charged monster-detection trinket: charges in daylight, reveals/indicates nearby monsters per spec. **NOTE:** the shipped detection render is a placeholder text HUD — the trophy-ring redesign (t_b8a19487) is carried forward to #2. |
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
