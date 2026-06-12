---
title: "Pocket Portal (v2 IDEA)"
status: idea
purpose: "Capture Daniel's 'v2 includes the pocket portal' note. IDEA-stage placeholder: Daniel named it as in-scope for v2 but gave no mechanics yet. Records the open question ('do you need anything to add that?') and the obvious design hooks so it can be fleshed out when he is ready."
---

# Pocket Portal (v2 IDEA)

> 🌱 **IDEA — named, not specced.** Daniel flagged the pocket portal as part of the v2 scope
> and asked what's needed to add it. This note exists so the feature isn't lost (it was the one
> playtest-batch item with zero board presence). It is intentionally thin — Daniel hasn't given
> mechanics yet. No impl card, no SpecCheck row until it's fleshed out and ratified.

## What Daniel said (verbatim + attributed)
> "Oh and v2 includes the pocket portal! Do you need anything to add that?"
> — Daniel, 2026-06-10, Discord /queue

## What we know
- **Scope:** it's a **v2** feature (Black Forest cartography tier era), alongside Local Maps,
  the Cartographer's Kit, marker signs, and the Surveyor's Table.
- **The name implies the concept:** a **portable / personal portal** — a player-carried or
  cheaply-placed teleport rather than the vanilla two-stone-portal-per-tag pair. The exact
  affordance (a deployable item? a single anchored "home" you recall to? a paired pocket
  device?) is **undecided** — Daniel has not specified mechanics.

## 🔴 OPEN — the answer to "do you need anything to add that?"
What I need from Daniel to turn this into a real design doc:
1. **The core mechanic.** Pick the shape:
   - (a) a **carried item** you deploy as a temporary portal endpoint, or
   - (b) a **personal recall** — one bound "home," recall-to-it on use (one-way), or
   - (c) a **paired pocket device** — two linked items, teleport between them, or
   - (d) something else entirely.
2. **Tier / cost / gating.** Is it Black Forest? What recipe/mats? Cooldown or charges? Does it
   respect vanilla's "no teleport while carrying ore/metal" rule, or deliberately break it?
3. **Relationship to vanilla portals.** Does it replace the stone portal, supplement it, or sit
   in a different niche (emergency escape vs. logistics network)?
4. **Multiplayer / persistence.** Is the endpoint per-player or world-shared? ZDO-persisted
   (like the Surveyor's Table survey + WorldPins) so it survives relog/restart?

## Likely design hooks (🟡 PROPOSED — for when it's fleshed out)
- Vanilla `Teleport` / `TeleportWorld` (the portal component) is the obvious blueprint to read
  (clean-side, ADR-0001 — reading/adapting vanilla is fair game). Build additively per ADR-0006
  (no cloning a ZNetView-bearing portal prefab and stripping it).
- If it's a placed endpoint, it's ZDO-anchored like the Surveyor's Table — reuse the
  derive-by-scan/owner-write ZDO patterns the WorldPin substrate already established.
- The "ore/metal teleport ban" is a single vanilla check; whether to honor or bypass it is a
  deliberate balance call, not an accident — decide it explicitly.

## Status / next step
IDEA only — blocked on Daniel's core-mechanic decision (the OPEN list above). When he answers,
promote to a full design doc (cartography-v2.md shape), then architect-spec, then impl card.
The honest answer to "do you need anything to add that?" is: **yes — pick the mechanic (1) and
the tier/cost (2); everything else follows from those.**
