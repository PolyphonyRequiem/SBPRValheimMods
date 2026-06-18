---
title: "SBPR Trailborne — Playtest Ledger"
status: living
purpose: >
  Durable, accumulating record of what needs in-game testing. Items land here as
  work merges (no reliance on memory). When a playtest is prepped, scripts/gen-playtest-guide.py
  rolls the PENDING section + git-derived code changes into a numbered Playtest #N testers guide,
  then the shipped items move to the ARCHIVE section under that playtest number.
playtest_counter: 1            # next playtest is #1 (human-facing series; DISTINCT from vX.Y.Z-playtest tags)
last_playtest_tag: v0.2.25-playtest
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
3. **Nothing is "tested" until Daniel says so in-game** (logs-green ≠ playable). The
   guide produces a checklist; Daniel's run fills it in.

---

## PENDING — accrues for the next playtest (Playtest #1)

> Build target: current `main` (ahead of `v0.2.25-playtest`). Test **local solo**
> on a fresh client build unless an item says otherwise.

### 🆕 Merged since v0.2.25-playtest — needs first in-game verify

| # | Feature | Card | PR | What to verify in-game |
|---|---------|------|-----|------------------------|
| 1 | **Sunstone dual-source loot** | t_0445f590 | #183 | Sunstone drops from swamp **surface** chests (~15%) and **Draugr Elite** (~5%). Loot a spread of **freshly-discovered** swamp chests (already-populated chests keep old contents — vanilla populate-once behavior) + kill Draugr Elites; confirm it appears at roughly those rates and is **pickable** (lands in inventory with its icon). **QA data-layer PASS** (t_0aef1243, `docs/v3/research/QA-sunstone-loot-economy.md`): swamp table carries Sunstone w=0.584 → empirically 15.01%/chest over 200k draws of the vanilla sampler; crypt table clean; elite drop 5% flat. Daniel's run closes the observed last mile (logs-green ≠ playable). |
| 2 | **Local Map opens on M, not E** | t_f9a04fda | #181 | Press **M** with the Surveyor's Table map equipped/in-range → local map opens. Pressing **E** (Use) does NOT open it. SBPR owns the M edge. |
| 3 | **Ancient Portal proximity FX aligned** | t_06b7b13c | #180 | Approach an Ancient Portal → the proximity/target-found effect renders **flat, aligned to the ring** (not vertical/offset). |
| 4 | **Iron Compass v3 — Trinket + HUD needle** | t_ee61472f | #171 | Equip Iron Compass in the Trinket slot → a HUD compass needle overlay appears and tracks heading (the no-map orientation payoff). |
| 5 | **Swamp Sunstone Lens trinket** | t_2fd7bc7f | (tier) | Solar-charged monster-detection trinket: charges in daylight, reveals/indicates nearby monsters per spec. |
| 6 | **Per-pin icon tint + label color** | t_3d7aaa90 | #168 | Place marker pins → per-pin icon tint and label text color apply and persist. |
| 7 | **Ancient Portal walk-up access** | t_ea0072ba | (#162/#169) | Walk up to / through an Ancient Portal cleanly — per-leg colliders, centre drop slab; no pathing block, no getting stuck. |
| 8 | **Local Map provider binding + carry-state disc** | t_7dd54899 | #162 | Carry the map item → minimap renders as the carry-state disc; provider binding correct. |
| 9 | **Map bezel circular clip** | t_d44572f2 | #159 | Local map parchment does NOT bleed past the disc edge — hard circular bezel clip. |
| 10 | **Local Map title format** | t_783672ac | #158 | Map title renders without a race glitch, formatted `Local map for <name>`. |
| 11 | **Re-name a named Surveyor's Table** | (#157) | #157 | `[Use]+Alt` on an already-named Surveyor's Table re-opens the name prompt (§1.6.5). |
| 12 | **Ancient Portal proximity effect wired** | t_e58283d7 | #156 | Vanilla proximity/target-found effect fires on Ancient Portal approach (issue 1). |
| 13 | **Item tooltip NRE fix** | t_2dd7c705 | #154 | Hover SBPR items with custom attacks → no per-frame tooltip NullReferenceException in the log. |

### 🎯 Specific verifies called out by Daniel
- [ ] **Sunstone Lens detection actually shows something** — card **t_b8a19487** (REDESIGN in flight).
      Current shipped impl is a placeholder text HUD (easy to miss / possibly not wiring up). The
      REAL design: a trophy ring around the player — each hostile's trophy billboarded at its bearing,
      size ∝ closeness, ★/★★ pips for star levels. Until the redesign lands, verify whether the
      *current* lens shows ANYTHING when worn+charged near a hostile (helps confirm the wiring bug).


- [ ] **Disc player-marker chevron (A′)** — card **t_efe8b32b** (impl, engineer-ui; not yet merged).
      When it lands: open the local-map **disc**; the player marker is the **vanilla default-map chevron**
      (not a blue quad), dead-centre, pointing **up = your facing** (NOT north — the disc rotates to heading).
      Confirm it reads as "you, forward = up" and there's no north-locked arrow.
- [ ] **Portal Seed crafting cost** — card **t_a6831e8e** (verify local solo on current `main`).
      At the Explorer's Bench, Portal Seed shows cost **AncientSeed ×1 + GreydwarfEye ×20 + SurtlingCore ×2**,
      and crafting **consumes** exactly that. (Was blocked on a stale niflheim server; Daniel runs local solo,
      so it reflects current main. If correct → close t_a6831e8e; if wrong → spawn a fix card from the failure mode.)

### ⏳ In-flight (will join PENDING when merged)

- Sunstone **recipe removal** — card t_c27f985e (ready). When merged: the provisional Iron×1+Crystal×2
  Explorer's Bench craft is **gone**; exploration drops are the sole acquisition path. Verify Sunstone is
  NOT craftable and still obtainable via drops.

---

## ARCHIVE — shipped playtests

_(none yet — first roll happens when the next `-playtest` tag lands)_
