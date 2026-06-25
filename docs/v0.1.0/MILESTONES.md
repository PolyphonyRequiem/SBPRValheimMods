---
title: "Trailborne v0.1.0 — Milestone Map"
status: current
purpose: v0.1.0 milestone map (M0–M4).
---

# Trailborne v0.1.0 — Milestone Map

**North star:** ship a playtestable Meadows-tier Explorer loop end-to-end on Niflheim.

A player can: build the bench → grind pigment → craft Painted Signs → walk out, mark things, walk back, the map remembers → place Cairns for shelter comfort + recovery.

Black Forest tier (Blue/Black pigment, Scrying Altar, embers, beacons, pocket portals) = **v0.2.0+**. Out of scope for v0.1.0.

> **Tier correction (2026-06-25):** the **Seer's Stone** is **v4 / Mountains / crystal-gated**, NOT Black Forest — it was previously listed on the Black Forest line above. The locked spec (`requirements.md`, `PARKED-2026-06-03.md`) places it at Mountains as the sole v4 headline; see `docs/design/seers-stone.md`.

---

## Status

| M  | Scope                                                              | Status   |
|----|--------------------------------------------------------------------|----------|
| M0 | Plugin loads + Bench + Spade (Hoe-clone) + Path Lamp               | ✅ ship  |
| M1 | Pigments (R/W/B/K inks) + Painted Signs (color + Shift+E map pin)  | ✅ ship  |
| M2 | Cairn Marker + Cairn piece + SE_Rested comfort floor (tier-1)      | ✅ ship  |
| M3 | Spade real ops — 3 path radii + vanilla-radius grass replant      | ✅ ship  |
| M4 | Explorer's Bench rename, English display names (locale-less)       | ✅ ship  |
| -- | **v0.1.0 ships ✅** — tag, push when greenlit                       | ✅ ship  |
| M5 | Real client/server handshake (replace OnSBServer stub)             | v0.2.0   |
| M6 | Minimap fog patch (nomap default for non-hardcore SBPR servers)    | v0.2.0   |
| M7 | Cairn tier 2-5 + repair + downgrade@25% + collapse@0%              | v0.2.0   |
| M8 | ClearVegetation spade op + spade-only PieceTable                   | v0.2.0   |
| M9 | Localization JSON support                                          | v0.2.0   |


## Out of scope for v0.1.0 (explicitly deferred)

- Blue / Black / Yellow pigments
- Scrying Altar + connected-station upgrade pattern
- Surtling Embers + Ember Lamps + Beacons + Pocket Portals
- Seer's Stone + wisp system + per-player pin model
- Cartography Table regional-window rebalance
- Twisted Portal, Traveler's Tent, Traveler's Storage
- Real client/server handshake (`SBPRContext.OnSBServer` stays stubbed `=> true` for v0.1.0; M5/v0.2.0 wires the real handshake)
- Bespoke 3D models (placeholder doctrine; vanilla mesh tints + runtime PNG icons)
- Thunderstore publish (manifest.json + README staged in v0.1.0, but actual upload is human-gated)

## Per-milestone autonomy contract

- Each milestone goes through a subagent checkpoint (or me directly if narrow).
- Server-gate every registration behind `SBPRContext.OnSBServer`.
- Placeholder doctrine: vanilla mesh + material tint for in-world pieces; runtime PNG icons.
- Stop-and-ping rule: architectural only. Feel decisions get best-guess + TWEAK ME in playtest doc.
- After each milestone: deploy to Niflheim, grep logs to prove load, write `M[n]-PLAYTEST.md` checklist, commit on `spec/2026-06-03-trailborne-v1`.

## Renames + cleanup carried through v0.1.0

- "Orienteering Table" (M0 codename) → **Explorer's Bench** (canonical per PLAYER_GUIDE). Either patch in-place or alias — TBD in M4 cleanup.
- "Trailblazer's Tools" vs "Trailblazer's Spade" — RESOLVED (Daniel, 2026-06-05): there is no tool *family*, only the single item. The umbrella "Trailblazer's Tools" name is retired; everything is **Trailblazer's Spade** (`SBPR_TrailblazersSpade`).
