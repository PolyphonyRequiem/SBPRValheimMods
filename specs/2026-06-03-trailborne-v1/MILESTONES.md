# Trailborne v0.1.0 — Milestone Map

**North star:** ship a playtestable Meadows-tier Explorer loop end-to-end on Niflheim.

A player can: build the bench → grind pigment → craft Painted Signs → walk out, mark things, walk back, the map remembers → place Cairns for shelter comfort + recovery.

Black Forest tier (Blue/Black pigment, Scrying Altar, embers, beacons, pocket portals, Seer's Stone) = **v0.2.0+**. Out of scope for v0.1.0.

---

## Status

| M  | Scope                                                              | Status   |
|----|--------------------------------------------------------------------|----------|
| M0 | Plugin loads + Table + Spade (Hoe-clone) + Path Lamp               | ✅ ship  |
| M1 | Pigments (Red+White) + Painted Signs (color + text + map pin)      | ⏳ active |
| M2 | Cairn Marker (consumable item) + Cairns (piece + SE_Rested patch)  | pending  |
| M3 | Trailblazer's Spade — real 3-radius path/replant/clear behavior    | pending  |
| M4 | Map fog default (mostly fogged) + Explorer's Bench rename          | pending  |
| -- | (v0.1.0 ship gate — tag, push, Thunderstore-ready packaging)       | pending  |

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
- "Trailblazer's Tools" (PLAYER_GUIDE) vs "Trailblazer's Spade" (recent locked rename). v0.1.0 ships as **Trailblazer's Spade**.
