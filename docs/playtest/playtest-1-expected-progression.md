---
title: "SBPR Trailborne — Playtest #1 Expected Progression (tester briefing)"
status: current
purpose: >
  Companion to playtest-1-testers-guide.md. The guide is the CHECKLIST (what to verify);
  this is the NARRATIVE (what the mod is, and what "working as designed" should feel like
  as you climb the tiers). Read this once before you start so the checklist items land in
  context. Grounded in PLAYER_GUIDE.md + docs/design/trailborne-vision.md + docs/design/nomap.md.
build: v0.2.26-playtest (current main)
---

# Trailborne — what you're testing and how it's meant to feel

**You're playtesting an Explorer-role overhaul of Valheim.** The one-sentence thesis:
vanilla hands you a satellite map after 20 minutes of walking; Trailborne takes that
away and makes the map something you *build*. This is **not a hard-mode mod** — same
world, same bosses, same loot — it's a *deeper* mod about learning to *see* the world
and mark it for your group.

> **The single most important thing to verify first:** press **M**. On a Trailborne
> world the map is **mostly fog** and walking around **does not** reveal terrain the way
> vanilla does. If walking reveals fog-of-war like normal vanilla, the NoMap premise has
> failed and nothing downstream makes sense — flag it immediately. (Server boot confirms
> `GlobalKeys.NoMap` is held, so this should be true on Niflheim.)

This build is **Meadows + Black Forest progression, plus the first Swamp tools**
(Sunstone Lens + Iron Compass). Mountains/Plains/Mistlands/Ashlands are scaffolded but
not built — don't expect explorer content past the Swamp yet.

---

## How to test (two modes)

You can verify almost everything **local solo** (your own client, single-player or
self-hosted) — that's the fastest loop and how most checklist items are written. A few
things are worth doing **on Niflheim** (the live dedicated server) specifically because
they're multiplayer/persistence behaviors: pin-sharing through a read table, signs other
players can see, and "does it survive a server reload." Join code is delivered separately
(it re-mints on every server restart, so it's never baked into these docs).

**Admin tools:** the installer's launcher adds `-console` (press **F5** for the dev
console). The modpack bundles **ServerDevcommands**, so once you're admin you have
`spawn`, `god`, `fly`, etc. — use them to skip the grind and jump straight to a tier you
want to stress (e.g. `spawn Draugr_Elite` to test Sunstone drops, `spawn SwampChest` to
test loot rates, give yourself iron to forge the Iron Compass). **Logs-green ≠ playable**
is the house rule: actually *do* the action in-game, don't trust a clean log line.

---

## The core loop (verify this works at all before anything else)

1. Kill deer → get a **Deer Trophy**.
2. At a vanilla **Workbench**, craft the **Explorer's Bench** (10 Wood + 4 Stone + 1
   Deer Trophy). This is the hub that gates the whole Explorer kit. The Deer Trophy's
   antlers should be visually incorporated into the bench art.
3. At the Explorer's Bench, craft **Red Pigment** (from raspberries) and the
   **Trailblazer's Spade** (the "explorer's hammer" — its own build menu/wheel).
4. Craft a **Painted Sign**, place it unpainted (plain wood), walk to a copper vein,
   and use the sign's panel to **color + label** it ("Copper here").
5. Press **M** → your sign shows up as a **colored pin** on the map.

**That loop — walk out, mark things, walk back, the map remembers — is the whole mod.**
If it completes end-to-end, the foundation is sound. Everything below is depth on top of it.

---

## Tier-by-tier — what "working as designed" looks like

### Meadows — establishing a vocabulary
- **Explorer's Bench** builds from the Deer Trophy recipe and is its **own** crafting
  station — it must **NOT** offer the vanilla hand-craft basics (Club, Torch, Stone Axe,
  Hammer, Hoe). If those show up in its tabs, that's the `m_showBasicRecipies` bug
  (regression of t_30f97042) — flag it.
- **Trailblazer's Spade** is a single tool with its own keybind + selection wheel. It
  places **Trail/Road/Highway paths at three widths** (1.5m / 3m / 5m) and **Replant
  Grass at three widths** that mirror the vanilla Cultivator's grass mode — grass-restore
  ONLY, **never** raise/level/cultivate terrain at any width. (If a width flattens or
  tills soil, that's the old "UBER replant" regression.)
  - **Grass and path must coexist on one tile** (last-applied-wins), like vanilla
    Cultivator-grass ↔ Hoe-path. Replant grass over a path and back; they should not
    fight each other.
- **Painted Signs**: build (2 Wood), place unpainted, open panel → pick a **text color**
  and optional **border color** from swatches (Red/Blue/Black/White), **Paint this and
  consume** (one pigment per color), then **Update Text** (free). Colors + text **persist
  across reload** and **other players see them**. Repaint should re-spend pigment and
  visibly recolor.
- **Path Lamps** (resin-fueled corewood torches) light the trails.
- Pigments: **Red** (raspberry) and **White** (bone fragment) at this tier; each craft
  yields 2.

### Black Forest — the magic begins
- Explorer's Bench upgradeable with a **Scrying Altar** (connected station). Unlocks
  **Black Pigment** (coal), **Blue Pigment** (blueberry), **Surtling Embers** (cores 1:5),
  **Ember Lamp** (eternal), **Surtling Torch** (eternal carry torch), **Beacons** (huge
  red corona, hilltop navigation), **Pocket Portals** (stackable one-shot teleport pairs).
- **Seer's Stone** (the signature item, worn accessory): walk the Black Forest and
  **wisps** drift into view above clusters (berries, mushrooms, crypt entrances, surtling
  spawners). Take it off → wisps vanish. Wisps are **personal** (other players don't see
  them). Pin-while-looking adds the cluster to your map. *(Note: parts of the Seer's Stone
  wisp pass may still be TBD in this build — verify what's wired, flag what isn't.)*
- **Cartography Table / Surveyor's Table**: each table is a **regional 1000m observation
  post**, not vanilla's global blob. Read a table → you get a fixed 1000m window centered
  on it. Walk 1500m away and that region fogs back over. **Zoom is capped**, map is a
  mosaic of regional windows. Maps become infrastructure you build across the world.

### Swamp — the first two Swamp tools (the new hotness this build)
- **Sunstone Lens** (trinket): a charm you **charge in the sun, spend in the gloom**.
  Charges only in **clear daylight, in the open, when dry** — never in rain/night/under
  roof/in the Swamp. Drains steadily while worn. Run it dry → goes quiet (doesn't break);
  re-charge in sun. **Sunstone is found, not crafted**: swamp chests (~1 in 7 ≈ 15%) and
  rarely Draugr Elite corpses (~5%). **Heads-up:** the lens *detection render* is being
  redesigned right now (the shipped version is a placeholder bottom-center text HUD that's
  easy to miss; the real design — a trophy ring around the player — is in flight, card
  t_b8a19487). For this playtest, just verify the charge/drain battery behavior and that
  Sunstone drops from the two loot sources; the detection *display* is known-placeholder.
- **Iron Compass** (trinket): the payoff for the no-north map. Forge at the Explorer's
  Bench (**4 iron + 2 ooze + 1 red pigment** — iron-gated, so it's a real Swamp earn). Wear
  it → a **HUD compass dial** appears at the top of the screen with a red-tipped needle
  that holds true north with a touch of lag (floaty, not snapping); tilts when you look
  up/down. It **never** puts a north arrow on the map — the map stays disorienting, the
  compass is a separate worn instrument. Take it off → needle's gone.
- **The Swamp trinket choice:** Sunstone Lens and Iron Compass **share the trinket slot**
  — you wear orientation **or** threat-sense, not both. Verify you can't equip both at once.

### Mountains / Plains / Mistlands / Ashlands — not in this build
Scaffolded in design, not implemented. No explorer content past the Swamp tools yet.
Don't treat their absence as a bug.

---

## Things that are deliberately different from vanilla (don't report these as bugs)
- **The map is mostly fog and walking doesn't reveal it.** Intended — that's the mod.
- **No north arrow / no compass on the map.** Intended; you earn the separate Iron Compass HUD.
- **Map zoom is capped and you can't free-scroll the whole world.** Intended — regional windows.
- **Vanilla portals still work and still block ore.** Pocket/Twisted Portals are additive, not replacements.
- **Vanilla cartography table behaves regionally now**, not as a global upload. Intended.

---

## When you find a failure
Note it inline on the **testers guide** checklist (check the box or mark the failure +
what you saw). Each failure becomes a kanban card assigned to the right specialist —
Starbright will file them. Be specific: "Sunstone Lens text HUD never appeared even worn +
charged + a Draugr 5m away" is actionable; "lens seems broken" is not.

— grounded in PLAYER_GUIDE.md, trailborne-vision.md, nomap.md (build v0.2.26-playtest)
