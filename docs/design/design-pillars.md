---
title: "Trailborne — design pillars"
status: living
purpose: The load-bearing design constraints — non-negotiable.
---

# Trailborne — design pillars

> Load-bearing patterns that justify the mechanics. When a future spec asks
> *why does this work this way?*, the answer should trace back to a pillar
> in this document. If a proposed change violates a pillar, the change needs
> to be reframed or the pillar needs to be explicitly retired (with a note).

---

## Pillar 1 — Trailblazer's Tools is a peer, not an extension

The vanilla Hammer raises walls. The vanilla Hoe levels foundations. The
**Trailblazer's Tools paves the world between settlements.**

This is the design pillar that justifies the Tools being its own top-level
tool — its own build menu, its own categories, its own pieces — rather than a
new tab on the Hoe or an upgrade path off the Hammer.

The Hoe and Hammer are the **settler's tools**: they shape the place you live.
The Trailblazer's Tools are the **wanderer's**: they shape the route between
places.

**Consequences:**

- Anything an Explorer places in the world goes through the Trailblazer's Tools
  build menu, not the Hammer's. Paths, signs, cairns, lamps, beacons (when they
  ship), pocket portals (when they ship) — all live on the Tools.
- The Tools graduates with the player as new pieces unlock per biome, but it is
  always *the same tool*, picked up fresh from each tier of crafting station.
- The Hoe and Hammer remain unmodified and fully usable. We don't replace them.
  We add a peer.

**Verification rule:** if a proposed new Trailborne piece feels like it should
live on the Hammer, that's a signal to re-examine whether the piece is actually
Trailborne-shaped, or whether it belongs in a different mod family.

---

## Pillar 2 — Color semantics are emergent

Pigments are biome-tiered (red unlocks at Meadows, blue at Black Forest, yellow
at Plains, etc.) because the **ingredients** are biome-tiered. The colors
themselves carry **no author-assigned meaning.**

Players decide what red means on their server. Players decide what blue means.
A group might use red for danger and blue for water; another group might use red
for "explored" and blue for "to-do"; a third might pick colors purely
aesthetically because they like how a blue sign looks against snow.

**The mod does not assert that red = danger or blue = water or any other
mapping.** PLAYER_GUIDE and tutorial content must avoid this framing.

**Consequences:**

- No tooltip text, UI hint, or guide passage that prescribes color meanings.
- Pigment names are color names (Red Pigment, White Pigment, etc.), not
  semantic names (Danger Pigment, Safety Pigment).
- If a player asks "what's blue for?", the honest answer is *whatever you and
  your group decide it's for.*
- Pin icons inherit the sign's color; the **player's chosen text on the sign**
  is the only assigned meaning a sign carries.

**Verification rule:** if a doc or spec says "color X is for purpose Y," that's
a doctrine violation. Fix it.

---

## Pillar 3 — *(reserved — for the next pillar Daniel identifies)*

We add pillars deliberately, not aspirationally. A pattern earns the pillar tag
when it has shaped two or more design decisions and is likely to shape more.
