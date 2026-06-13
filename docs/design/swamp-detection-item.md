---
title: "Swamp-tier solar-charged monster-detection accessory (IDEA)"
status: idea
purpose: "Capture Daniel's swamp-tier item idea — an equippable that charges durability in sunlight and reveals nearby hostiles while worn, draining at a fixed rate. THEME + MATERIAL DECIDED 2026-06-13: built around Sunstone (net-new Iceland-spar material), dual-sourced from swamp surface chests (primary) + rare Draugr Elite drop (secondary). Mechanics still PROPOSED; promote to a cartography-v2-style design doc when Daniel picks it up."
---

# Swamp-tier solar-charged monster-detection accessory (IDEA)

> 🌱 **IDEA — not specced, not scheduled.** Daniel asked to "just make a note somewhere."
> This is that note: enough to resume the idea cold without re-deriving intent. Theme is
> explicitly undecided. Mechanics below are 🟡PROPOSED starting points, not locked.
> No `requirements.md` section, no impl card, no SpecCheck row until Daniel promotes it.

## What Daniel asked for (verbatim + attributed)
> "I want to add an item to the swamp tier. Not sure on theme yet, but essentially I want an
> item that charges (durability) in the sunlight (clear weather) and while equipped shows
> monsters nearby in basically the same manner as the rune magic mod's rune of detection.
> Just make a note somewhere. Equipping the item uses energy (durability) at a fixed rate."
> — Daniel, 2026-06-10, Discord /queue

## The core loop (the load-bearing idea)
A **charge/drain accessory**, not a consumable:
- **Charges** its "energy" (modeled as durability) when exposed to **sunlight / clear weather**.
- **Drains** that energy at a **fixed rate while equipped**.
- **While equipped + charged**, it **reveals nearby hostiles** — same player-facing effect as
  the Rune Magic mod's "rune of detection" (monsters shown on the minimap / via an overlay).
- Net feel: a Swamp-tier survival tool you must "sun-charge" in the open, then spend that
  charge to see threats in the dark/fog — a risk/reward rhythm that fits the Swamp's danger.

## 🟡 PROPOSED mechanics (starting points — Daniel/architect refine)
- **Slot:** likely the **Utility slot** (same family as the Cartographer's Kit / Megingjord),
  so it coexists with weapon + shield and reads as "equipped gear," not a hand item. Confirm.
- **Charge source — sunlight / clear weather:** vanilla exposes weather + time-of-day and a
  "is the sky clear + is it day + am I outdoors" notion (the same inputs Rested/sunlight
  mechanics read). Charge ticks up only when that's true; pauses indoors / at night / in rain.
  GROUND the exact vanilla members against the decomp before building (don't guess the API).
- **Energy = durability:** reuse the item durability bar as the energy meter — sunlight
  *repairs* it, equip *drains* it at a fixed rate. When empty, detection switches off (item
  stays equipped, just inert until re-charged). This avoids a bespoke meter UI.
- **Detection effect:** reproduce the *behavior* of rune-of-detection (reveal nearby hostiles)
  — but per ADR-0001, do NOT copy the Rune Magic mod's code. Either build the reveal from
  vanilla (query nearby `Character`/`BaseAI` hostiles, surface them on the minimap/an overlay)
  or, if we ever want parity with that mod, go through the clean-room RE wall. Clean-side:
  reading vanilla's character/minimap surface is fair game.
- **Tier:** Swamp. Recipe undecided (Swamp-tier mats — but now anchored on **Sunstone**, the
  new material decided 2026-06-13; see "Theme + material + sourcing" below).

## 🟢 Theme + material + sourcing — DECIDED (Daniel, 2026-06-13)

The theme is locked: the item is built around **Sunstone**, a NEW craftable material modeled
after **Iceland spar** (Silfurberg) — the real birefringent calcite crystal tied to the
legendary Viking *sólarsteinn* ("sunstone") used to find the sun through overcast. This gives
the solar-charge fiction a concrete, lore-true anchor: the item literally holds a shard of
sun-finding crystal.

- 🔴 **Sunstone is NET-NEW mod fiction — there is NO "Sunstone" in vanilla Valheim** (verified
  vs the full wiki corpus, 0 hits). We are AUTHORING this material. It is not a reskin of an
  existing item. (Closest vanilla cousin in spirit is Crystal — a Mountain material — but
  Sunstone is its own Swamp-tier thing, NOT Crystal.)
- **Lane note:** the *sólarsteinn* legend is historically a NAVIGATION aid (find the sun →
  orient), which overlaps thematically with the **Iron Compass** (also v3 Swamps, the no-map
  orientation payoff — `nomap.md` §8). We are deliberately routing Sunstone to the
  *threat-detection* item, not the compass. The fiction that bridges it: the stone stores
  daylight and, while charged, lets the bearer *sense what moves in the dark*. If Daniel later
  decides Sunstone fits the compass better, that's a swap to revisit — flagged, not foreclosed.

### Sourcing — DUAL-SOURCE (decided 2026-06-13), grounded vs wiki loot/drop tables

Sunstone is found by **exploring the Swamp surface** (primary) with a **rare combat drop**
(secondary) — deliberately NOT locked behind the Sunken Crypts, to pull players into the open,
dangerous overworld rather than the safe gated-dungeon loop.

1. **PRIMARY — surface loot chests (exploration path).** Add Sunstone as a low-weight entry to
   the **Swamp surface** chest tables — explicitly NOT the Sunken Crypt table. Grounded targets
   (verified `~/valheim/sbpr-corpus/wiki/fandom/Swamp_chest.md` + `Swamp.md`):
   - The **Swamp Chest** table (spawns in the **Swamp Runestone Tower**, a surface structure) —
     the canonical "swamp surface" chest. Its existing entries sit at ~10.5%/slot, 2–3 slots,
     with **Withered bone at 5.3% as the rare tail** — Sunstone slots in at that rare-tail
     weight as precedent.
   - Other swamp **surface** POIs that use loot chests: **Draugr Village**, **Ruined Tower**,
     **Abandoned House / Abandoned Village**, **Viking Graveyard**, **Shipwreck**.
   - 🔴 EXCLUDE the **Sunken Crypts** chest table (the gated-dungeon loop we're steering away from).
2. **SECONDARY — rare drop from Draugr Elite (combat path).** Draugr Elite are already **rare**
   (night-spawn/despawn-at-dawn, or one-time near Inverted Towers) and carry a **tiny** vanilla
   drop table (Draugr Elite trophy 10% + Entrails), so a low-% Sunstone roll fits cleanly with
   no economy collision. Verified `~/valheim/sbpr-corpus/wiki/fandom/Draugr_elite.md`.
   - ⚠️ **Caveat (why it's SECONDARY, not the only source):** the canonical Draugr-Elite farm is
     Body Piles *inside* Sunken Crypts — so an elite-ONLY drop would quietly route players back
     into the crypts we're avoiding. Keeping chests primary preserves the surface-exploration
     intent; the elite drop is a combat-path bonus, not the bottleneck.
- **Drop rarity — OPEN knob for Daniel:** how rare overall? Two anchors to pick between:
  *looser* (~5–8% surface / ~5% elite → a Sunstone after a couple hours of swamp exploration)
  vs *tighter* (~2–3% → a genuine treasure, a few sessions in). Lean to be set by Daniel.
- **Recipe:** the detector item's recipe is then Sunstone ×N + other Swamp mats (iron? guck?) —
  N and the supporting mats TBD with the rarity knob (rarer Sunstone → fewer per craft).

## 🔴 OPEN questions for Daniel (when he picks this up)
- ~~**Theme / fiction:**~~ ✅ DECIDED 2026-06-13 — **Sunstone** (Iceland-spar / *sólarsteinn*),
  a net-new material, dual-sourced from swamp surface chests + rare Draugr Elite drop. See the
  "Theme + material + sourcing" section above. Remaining sub-knob: overall drop **rarity** (the
  looser-vs-tighter lean).
- **Detection scope:** all hostiles, or a subset? Radius? Does it show them on the minimap
  (which the cartography tier may have *disabled* by default — see `nomap.md` / the NoMap
  enforcer) or via a separate on-screen overlay? **Interaction with NoMap is a real design
  coupling — if the minimap is off by default, the reveal needs its own surface.**
- **Charge economy:** how fast does it charge vs. drain? Is a full charge minutes or a play
  session of detection? This is the whole balance of the item.
- **Does detection cost charge only, or also stamina/eitr?** (Daniel said durability-drain;
  confirm that's the only cost.)

## Dependencies / couplings noted
- **NoMap (card t_8c9abf6f / `nomap.md`):** if the cartography tier disables the minimap by
  default, a minimap-based reveal won't have a surface — design the reveal independently of the
  minimap, or gate it to "works even under NoMap."
- **Utility slot:** shares the slot family with the Cartographer's Kit — confirm they coexist
  or are mutually exclusive (you can only wear one Utility item at a time in vanilla).

## Status / next step
IDEA only. When Daniel wants it, promote to a full design doc (cartography-v2.md shape:
GROUNDED/PROPOSED/OPEN sections, vanilla decomp anchors), THEN architect-spec, THEN impl card.
Do not build from this note alone.
