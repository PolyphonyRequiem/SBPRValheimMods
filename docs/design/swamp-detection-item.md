---
title: "Swamp-tier solar-charged monster-detection accessory (IDEA)"
status: idea
purpose: "Capture Daniel's swamp-tier item idea — an equippable that charges durability in sunlight and reveals nearby hostiles while worn, draining at a fixed rate. IDEA-stage only: theme undecided, mechanics proposed, nothing locked. Promote to a cartography-v2-style design doc when Daniel picks it up."
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
- **Tier:** Swamp. Recipe undecided (Swamp-tier mats — e.g. some combination of the Swamp's
  signature drops; Daniel to theme it).

## 🔴 OPEN questions for Daniel (when he picks this up)
- **Theme / fiction:** what IS the item? (A sunstone amulet? A charged lantern? A bug-in-amber
  charm?) The theme drives the art, the name, and the recipe mats.
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
