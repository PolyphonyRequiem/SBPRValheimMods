---
title: "ADR-0003: Explorer's Bench is its own CraftingStation, not the vanilla Workbench"
status: accepted
---

# ADR-0003: Explorer's Bench is its own CraftingStation, not the vanilla Workbench

- **Status:** accepted
- **Date:** 2026-06-03 (decision); reaffirmed 2026-06-04 after playtest
- **Deciders:** Daniel + Starbright

## Context

The Explorer's Bench is visually kitbashed from the vanilla Workbench. It is
tempting (and the v0.1.0 code initially did this) to keep the clone's Workbench
CraftingStation identity to avoid recipe collisions. The 2026-06-04 playtest
showed why that's wrong: the bench let players craft vanilla Workbench items
(club, torch, stone axe, hammer) and triggered the Hugin "you built a workbench"
tutorial — because it *was*, by identity, a workbench.

## Decision

The Explorer's Bench has its **own distinct CraftingStation identity** (unique
station name). Trailborne recipes register against that station; vanilla recipes
keyed to the Workbench do **not** attach to it. Requirements.md (~line 119)
already locks this: "its own CraftingStation, NOT the vanilla Workbench."

## Consequences

- Our recipes must be re-pointed at the new station name, and `SpecCheck` records
  that station for our items — keep them consistent.
- **The "vanilla craftables on the bench" leak is NOT a station-name collision.**
  The clone already carried a distinct station name; the leak came from a *separate*
  inherited flag, `CraftingStation.m_showBasicRecipies`. The vanilla Workbench is the
  ONLY station that ships this `true` — it's the flag that surfaces the stationless
  "basic" hand-craft recipes (Club, Torch, Stone Axe, Hammer, Hoe, rag armor, …). A
  raw clone inherits `true`, so those vanilla items appeared on the Explorer's Bench.
  The fix sets `m_showBasicRecipies = false` on the clone's CraftingStation (in
  `Trailhead.RegisterExplorersBenchPrefab`), matching every other vanilla station.
  Distinct station identity (`m_name`) + `m_showBasicRecipies = false` together give
  the bench a clean, Trailborne-only recipe list. (Card t_30f97042, playtest 2026-06-04.)
- Resolves the vanilla-craftables leak and the "two workbenches" confusion at the root.
- **An agent must not "simplify" the bench back to a raw Workbench clone** to dodge
  recipe wiring. That reintroduces all three bugs.

## Alternatives considered

- **Keep Workbench identity, hide vanilla recipes by other means:** fragile, fights
  vanilla, and still trips the tutorial. Rejected.
