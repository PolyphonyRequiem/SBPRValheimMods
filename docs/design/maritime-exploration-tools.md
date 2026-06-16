---
title: "Maritime Exploration Tools (v5 / Plains) — Lighthouses, Fog Buoys & the wider sea-navigation set"
status: idea
purpose: "BRAINSTORM/seed-idea doc for the v5 Plains maritime exploration-tools tier. Captures Daniel's two seed ideas (lighthouses, fog buoys) and the wider sea-navigation tool set as prompts, grounded against the locked roadmap. Nothing here is locked or specced — items graduate to their own design docs / cards as Daniel rates them. Open questions are LEFT open by design."
---

# Maritime Exploration Tools (v5 / Plains) — Lighthouses, Fog Buoys & the wider sea-navigation set

> 🧠 **BRAINSTORM — not a spec, not a lock.** This brick captures the *maritime
> exploration-tools* idea space for the v5 Plains sailing tier so individual tools
> can later be promoted to their own design docs and cards. Two of Daniel's seed
> ideas (Lighthouses, Fog Buoys) get a fuller treatment; the rest of the set is
> enumerated as **prompts only** — captured, not decided. Roadmap grounding is
> cited inline. Where a mechanism rests on a vanilla assumption I have NOT
> decomp-verified, it is marked **🔴 UNVERIFIED — ground before asserting**. Every
> open fork is collected in §6 and left open for Daniel; I lock nothing unilaterally.

## 0. Where this sits (roadmap grounding)

- **Tier: v5 (Plains) — the sailing tier.** Grounded:
  - `design/PARKED-2026-06-03.md:40` (§v5): *"Sailing tier. Beacons promote to
    lighthouses. Star Glass. Ship infrastructure."*
  - `docs/v0.1.0/planning/requirements.md:569`: *"Plains sailing tier,
    lighthouse-promote, Star Glass — v5"*.
  - Both name the same three v5 maritime anchors: **lighthouse-promotion**,
    **Star Glass**, and **ship/sailing infrastructure**. This doc treats that triad
    as the seed of the broader "maritime exploration tools" set.
- **This is the *last-but-one* roadmap tier.** The locked progression is Meadows
  (v1) → Black Forest (v2) → Swamps (v3) → Mountains (v4) → **Plains (v5)** →
  Mistlands (v6, "portable map magic"). So maritime tools land *after* the whole
  land-based exploration kit (signs, cairns, lamps, beacons, cartography, compass,
  Seer's Stone) already exists. The sea is the **last frontier** the Explorer
  learns to read — design accordingly: these are capstone tools, not starter ones.
- **Nothing in `src/` exists for this tier.** Greenfield. No maritime code, no
  Plains-tier pieces. This doc is pure design-space capture.
- **Why brainstorm now, build later:** Daniel seeded lighthouses + fog buoys ahead
  of the tier so the ideas are captured while fresh. The value of this doc is to
  hold them (plus the wider set) in a grounded, promotable shape — NOT to start
  v5 work. v3 (beacons) and v2 (cartography) are the active tiers.

## 1. The framing every maritime tool must honor — no map, no GPS

The load-bearing pillar (`design/trailborne-vision.md`,
`design/design-pillars.md`) is that **Trailborne took away Valheim's free
satellite map** and gives back *earned, player-built, diegetic* navigation. That
pillar does not relax at sea — if anything the open ocean is where disorientation
bites hardest (no landmarks, fog, night, look-alike coastlines).

**The maritime framing, stated explicitly:**

- **Sea aids are player-built infrastructure, never a returned map.** A lighthouse,
  a buoy, a star-sighting tool — each makes the *world itself* more legible (a
  light you can steer toward, a marker that pings, a star you can read). None of
  them hands back the M-key map or a GPS dot. The Explorer who lights the coast is
  doing for the sea what the beacon-builder did for the hills.
- **Disorientation is the design substrate, not a bug to fix.** Getting lost at sea
  is *supposed* to be a real risk. Maritime tools reduce it the way real-world
  seamarks do — by adding fixed, trustworthy references you sailed out and placed —
  not by removing the possibility of being lost.
- **Pillar 1 (the Spade is a peer):** sea-marking infrastructure that the Explorer
  *places in the world* (lighthouses, buoys, route markers) should route through
  the **Trailblazer's Spade** build menu like every other trail-marking piece —
  unless a tool is a *carried instrument* (Star Glass, a sextant), in which case
  it's an item, not a placed piece. Flag the Spade-vs-item call per tool (§6).
- **Pillar 2 (color is emergent):** if any maritime tool carries color (buoy paint,
  lamp tint), the mod assigns **no** meaning — no "red buoy = danger." Players
  decide. Same rule as signs and pins.

**Verification rule for this whole set:** if a proposed maritime tool's payoff is
"and now you can see the map / your position on a map," it has violated the pillar
and must be reframed (or explicitly retired with a note). The payoff must always be
*a thing in the world you can see/steer-by*, not *a UI that tells you where you are*.

## 2. Seed idea #1 — **Lighthouses** (the v5 promotion of the v3 Beacon)

### 2.1 What it is
The roadmap is explicit: *"Beacons promote to lighthouses"* (PARKED:40). The
lighthouse is the **Plains-tier graduation of the v3 Beacon** — the same "place a
light on high ground that sailors steer by" fantasy, scaled up for the open-water
tier: taller, brighter, longer-throw, built to be seen from a ship far offshore at
night or in weather. Where a hilltop beacon marks a *trail*, a lighthouse marks a
*coast* — a harbor mouth, a headland, a dangerous shoal — for navigation from the
sea.

### 2.2 The v3 Beacon substrate this builds on (dependency: card t_117bc232)
🔗 **Hard forward-dependency.** The lighthouse is a *promotion of* the Beacon, so it
inherits whatever the Beacon's design locks. The Beacon family is being specced
**right now** on sibling card **t_117bc232** ("Spec beacon / eternal-fire /
Surtling-ember trail-light family (v3)"). The lighthouse MUST consume that spec's
decisions rather than re-derive them. Specifically inherited / still-open upstream:

- **Beacon = `Fireplace` subclass** (grounded: `design/nomap.md:78`). A lighthouse
  is plausibly the same `Fireplace` substrate with a bigger light rig and a taller
  mesh — but confirm against whatever t_117bc232 lands (it may extract a shared
  eternal-fire helper the lighthouse should reuse).
- **🔴 Fuel model is UNRESOLVED upstream and the lighthouse inherits it.** There is a
  live tension in the existing docs that t_117bc232 is chartered to resolve:
  - `design/nomap.md:78-83` specs the Beacon as **resin-fueled** (`m_secPerFuel =
    14400`, `m_maxFuel = 4`, `m_fuelItem = resin`; point light intensity 1.5 /
    range 12 / no shadows / no smoke).
  - `PLAYER_GUIDE.md:142-143` describes Beacons as **eternal** ("1 ember + 1 ruby +
    2 corewood … eternal beacons with a huge red corona visible at great distance").
  - These disagree on fueled-vs-eternal. **Do not pick for the lighthouse here** —
    the lighthouse's fuel model follows whatever t_117bc232 locks for the Beacon
    line (and its own v5-tier may legitimately diverge: see Q-MAR-2).

### 2.3 Open design questions (LEFT OPEN — Daniel's calls)
- **Range / throw.** A lighthouse should out-reach a beacon (it's a sea mark, not a
  trail mark). What's the intended visible distance, and how is "visible from a ship
  far offshore" achieved — raw point-light range, an unlit-distance corona/billboard
  sprite that reads past the light's actual falloff, or a height advantage? (Vanilla
  point lights fall off fast; a *huge* literal range is expensive to render. The
  "corona visible at great distance" language in PLAYER_GUIDE hints at a
  billboard/halo trick rather than a literally enormous light — **🔴 UNVERIFIED**,
  ground the cheap-long-range-glow approach before asserting it.)
- **Fuel vs eternal at v5.** Even if v3 beacons are fueled, a Plains-tier lighthouse
  might be the *eternal* graduation (you've earned a maintenance-free coast light).
  Or it stays fueled to keep the "tend your infrastructure" loop. Daniel's call;
  gated on t_117bc232's beacon decision (Q-MAR-2).
- **Fog / distance rendering.** Should a lighthouse cut *through* fog/weather
  specifically (its whole point is being seen in bad conditions), and if so, how —
  a light that ignores fog density, a through-weather halo, or just brute brightness?
  This is the same "show through fog" problem the fog buoy has (§3); they may share a
  rendering approach. **🔴 UNVERIFIED** — Valheim's fog/weather render interaction
  with lights needs decomp grounding before a mechanism is named.
- **ZDO-anchored shared visibility.** A lighthouse is shared infrastructure — every
  player on the server should see the same light at the same world spot, persisting
  across restarts. As a `Fireplace`/`ZNetView` piece this falls out of the vanilla
  ZDO substrate for free (same as the beacon), but confirm the light/corona state
  (lit, fuel level if fueled) round-trips through the ZDO for all clients, not just
  the owner. Owner-authoritative writes only (the standing doctrine).
- **Placement gating.** Beacons go on hilltops; lighthouses go on *coasts*. Is there
  a placement rule (near water / on a headland / elevation band), à la the Cairn
  below-2m-above-sea-level gate (`fix/cairn-placement-elevation-gate-t_aceacef6`)?
  Or is it free-place and players self-police? (Q-MAR-6.)
- **Recipe / tier mats.** Plains-tier materials (the sailing tier — likely Black
  Metal, Fine Wood, Tar, maybe Flametal-adjacent for the lamp). Eyeball only; needs
  a lock once the piece is real (Q-MAR-5).

## 3. Seed idea #2 — **Fog Buoys** (water-anchored floating markers)

### 3.1 What it is
A **water-anchored floating marker** the Explorer drops at sea to mark a spot —
a safe channel, a fishing ground, a shoal to avoid, a rendezvous, the way back to a
harbor mouth lost in weather. Unlike a lighthouse (a fixed coast structure on land),
a buoy *floats on the water itself*, out in the open where there's nothing to build
on. The seed concept bundles several payoffs (any subset could ship):

- **Shows through fog** — its whole reason to exist is being visible when you can't
  see anything else. A buoy you can't see in fog is useless.
- **Pings the minimap** — a presence on the rotating minimap circle so you can steer
  toward it even before it's in visual range (note: under the no-map regime the
  minimap circle is the only map surface that exists — see §3.3).
- **Light and/or sound** — a lamp on top for night visibility, and/or a bell/horn
  audio cue that grows with proximity ("you can hear the buoy before you see it").
- **Server-persistent** — placed once, it stays for everyone across restarts, like
  all shared infrastructure.

### 3.2 🔴 The load-bearing UNVERIFIED assumption — floating on water
**The entire "floats on the water" premise rests on a vanilla mechanism I have NOT
grounded, and the task explicitly flags this.** Do **not** assert any of the
following until decomp-verified:

- That a `Floating` component exists, what it's called, and what it does. Valheim
  *does* have floating objects (you can see logs, the raft/karve/longship, dropped
  items bob on the surface), so a buoyancy mechanism certainly exists — but its
  exact type name, fields, and whether it's reusable on an arbitrary placed
  `Piece`/`ZNetView` is **🔴 UNVERIFIED**. Candidate leads to investigate (NOT
  claims): a `Floating`/`WaterVolume` interaction, the component on `Fish`/dropped
  `ItemDrop` that makes them surface, or the ship hull's buoyancy. Ground against
  `assembly_valheim` decomp + `vprefab inspect` on a known floater before any
  buoy mechanism is specced.
- That a placed, ward-style, ZDO-anchored piece can *stay put* while floating — i.e.
  buoyancy (vertical bob to the surface) without drift (horizontal station-keeping
  at its dropped world coord). A real buoy is anchored; a drifting marker is worse
  than useless. Whether vanilla buoyancy holds horizontal position or needs us to
  pin X/Z while only letting Y track the wave surface is **🔴 UNVERIFIED**.
- That building it additively (ADR-0006 — `new GameObject()` + only the components
  we intend, no cloning a floating donor and stripping it) is feasible *with* the
  buoyancy component. If the buoyancy behavior is deeply entangled with a
  ZNetView-bearing donor, the additive-construction doctrine may make this harder
  than a land piece — flag for the engineering spike.

### 3.3 Open design questions (LEFT OPEN — Daniel's calls)
- **"Show through fog" — same problem as the lighthouse.** How does a buoy stay
  visible in fog/weather/at distance? Through-fog shader on the light/marker, a
  minimap ping that's fog-independent, an audio cue (sound carries through fog where
  light doesn't — arguably the *most* on-theme answer), or all three? Shared with the
  lighthouse's fog-render question (§2.3); they likely want one grounded approach.
- **Minimap ping under the no-map regime.** "Pings the minimap" needs care: under
  Trailborne, when `nomap=ON` the minimap is OFF entirely, and even `nomap=OFF` is
  minimap-circle-only (`requirements.md:55-57`). A `Minimap.AddPin` ping renders
  *nothing* when the map is disabled — exactly the constraint the v3 Sunstone Lens
  hit (`docs/v3/planning/sunstone-lens-impl-spec.md:234-238`, the
  `NoMapEnforcer`-sets-`GlobalKeys.NoMap`-by-default reality). So a buoy's "ping"
  can't rely on the vanilla map pin in the no-map case. **Design the buoy's
  at-distance cue independently of the minimap** (world-space marker / light / sound),
  and treat any actual minimap ping as a *bonus that only works when a map surface
  exists* — never the primary mechanism. **🔴** Ground the pin-renders-nothing-under-
  nomap behavior the same way the Sunstone spec did before relying on any ping.
- **Pillar-fit: is a "minimap ping" even allowed?** A ping that shows your position
  relative to the buoy edges toward the GPS the pillar forbids. The honest, on-pillar
  version is "the buoy is a *thing in the world* you see/hear and steer toward,"
  not "a dot on a map that tells you where you are." Lean hard on world-space cues;
  treat map pings as suspect. (Q-MAR-3 — a real pillar call.)
- **Light vs sound vs both.** Night light is obvious; a bell/horn that scales with
  proximity is more distinctive and more fog-appropriate (sound ignores fog). Pick
  per Daniel's taste; sound is the more novel, more diegetic option.
- **Placement & retrieval.** Dropped from a ship? From shore into adjacent water?
  Can it be picked back up (a redeployable tool) or is it permanent-once-placed
  (commit to your sea marks)? Does it need open water / minimum depth to place?
- **Recipe / tier mats.** Plains-tier; should *feel* nautical (Tar for waterproofing,
  Fine Wood, Black Metal fittings, maybe a chain/anchor evocation). Eyeball only.
- **Color (Pillar 2).** If the buoy carries paint/light color, the mod assigns no
  meaning — no "red = hazard." Players decide, same as signs/pins.

## 4. The wider maritime tool set (PROMPTS ONLY — capture, don't decide)

These are the rest of the maritime idea space, listed as **brainstorm prompts** so
they're captured for later promotion. **None is designed here** — each is a one-liner
+ the open question it raises + any pillar/grounding flag. Daniel rates which are
worth their own doc; the unrated ones just sit here as a backlog.

- **Star Glass (roadmap-named, PARKED:40 / requirements.md:569).** The third named v5
  anchor alongside lighthouse-promote and ship infrastructure — so it's *already on
  the roadmap*, not a fresh idea. Its concept is **unspecified** in the docs (the name
  is all we have). Prompt: what IS the Star Glass? Plausible readings, all open: a
  *carried instrument* (telescope/spyglass — zoom in on a distant lighthouse/coast),
  a *star-navigation* aid (read heading from the night sky — the "Valheim has always
  rewarded paying attention to the sky" thread from the vision doc), or a *fog-piercing
  lens* (see through weather). It reads most naturally as a **sky/star navigation tool**
  given the name. Likely an **item, not a placed piece** (it's a glass you look
  through) → lives on the item path, not the Spade. 🔴 Genuinely undefined — needs its
  own brainstorm before any mechanism. (Q-MAR-4.)
- **Sea-route markers.** A line of placed markers (buoys or floating stakes) that
  define a *navigable channel* — "follow the marks through the shoals." Open: is this
  just repeated fog buoys, or a distinct paired/linked piece that draws a route? Pillar
  check: a *visible chain of marks in the world* is on-pillar; a drawn route line on a
  map is not.
- **Depth / shoal warnings.** A tool or buoy that warns of shallow water / rocks
  before your ship grounds. Open: passive (a buoy you place on a known shoal) or active
  (a carried sounding tool that reads depth where you are)? 🔴 Needs grounding on how
  vanilla represents water depth / the seabed (`WaterVolume`, heightmap-vs-sea-level)
  before any "reads depth" claim.
- **Sextant / star-navigation instrument.** A carried instrument that gives *heading*
  (not position) from sun/star sightings — restoring orientation without restoring the
  map. Strongly on-pillar (heading ≠ GPS). Possible overlap with Star Glass (may be the
  same tool) and with the v3 **Iron Compass** (`nomap.md:8`, the no-map orientation
  payoff) and the *sólarsteinn* navigation legend noted for Sunstone
  (`design/swamp-detection-item.md:69-72`). Open: how does maritime heading differ from
  the land compass enough to justify a separate tool?
- **Tide / current reading.** Read prevailing current/wind to plan a crossing. Open: 🔴
  does Valheim even *have* tides/currents as a readable system, or only wind? Ground
  before assuming — wind exists (sails use it); tides/currents likely don't, which may
  kill this one or reframe it as "wind-reading."
- **Harbor markers.** A distinct "this is home port / safe landing" mark, larger or
  coded differently from a generic buoy — the thing you steer *home* to. Open: distinct
  piece, or just a lighthouse/buoy a player designates as home? Probably folds into
  lighthouse + buoy rather than being its own piece.
- **Message-in-a-bottle async comms.** A floating container that carries a written
  message (and maybe items) to another player who finds it — async, drifting,
  serendipitous. Open: does it actually *drift* (hard — networked moving floater) or
  sit where dropped? Leans more "social toy" than "navigation tool"; flag as
  scope-adjacent / maybe-not-Trailborne. Reuses the Sign text substrate
  (`ZDOVars.s_text`) for the message. 🔴 Drifting-floater networking is the same
  unverified buoyancy/station-keeping problem as the buoy, made harder by wanting drift.

**Note on overlaps:** Star Glass / sextant / star-navigation may collapse into **one**
sky-reading instrument; depth-warning / sea-route-markers / harbor-markers may collapse
into the **buoy** family with variants. Don't pre-collapse them here — but flag for
Daniel that the set probably reduces to ~3 real pieces (lighthouse, buoy-family,
star-instrument) plus the named-but-undefined Star Glass, rather than ten distinct
builds (Q-MAR-1).

## 5. Dependencies & grounding ledger

**Forward / cross-feature dependencies:**
- 🔗 **Lighthouse → v3 Beacon (card t_117bc232).** The lighthouse is the *promotion of*
  the Beacon; it inherits the Beacon's substrate (`Fireplace` subclass, `nomap.md:78`)
  and especially its **fuel model**, which t_117bc232 is actively resolving (the
  fueled-vs-eternal tension between `nomap.md:78-83` and `PLAYER_GUIDE.md:142`). Do not
  build or fully spec the lighthouse until that beacon decision lands. (t_117bc232's own
  body explicitly defers "Lighthouse promotion (v5 maritime tier)" to a separate card —
  this is that card's design seed.)
- **Fog buoy / route markers / message-bottle → vanilla buoyancy.** All floating pieces
  share the **🔴 UNVERIFIED `Floating`/buoyancy** assumption (§3.2). One engineering
  spike to ground "can an additive ZDO piece float and hold station" unblocks the whole
  floating-marker family.
- **Buoy "ping" / any map-surface payoff → the no-map regime.** `NoMapEnforcer` sets
  `GlobalKeys.NoMap` server-side by default (`docs/v3/planning/sunstone-lens-impl-spec.md`
  :234-238); map pins render nothing under it. Any maritime tool that wanted to "show on
  the map" must instead use world-space cues — same lesson the Sunstone Lens learned.
- **Star Glass / sextant → v3 Iron Compass + Sunstone *sólarsteinn*.** Overlapping
  orientation-aid lineage (`nomap.md:8`, `swamp-detection-item.md:69-72`); design the
  maritime sky-instrument aware of what the land compass already does so it's additive.

**Grounding ledger (what's cited vs what's open):**
| Claim | Status |
|---|---|
| Maritime tools = v5 Plains tier | ✅ GROUNDED — PARKED:40, requirements.md:569 |
| "Beacons promote to lighthouses" | ✅ GROUNDED — PARKED:40 |
| Star Glass is a named v5 anchor | ✅ GROUNDED (name only) — PARKED:40, requirements.md:569 |
| Beacon = `Fireplace` subclass | ✅ GROUNDED — nomap.md:78 |
| Beacon fuel model (fueled vs eternal) | 🔴 OPEN upstream — t_117bc232 resolving |
| No-map regime kills map pins | ✅ GROUNDED — sunstone-lens-impl-spec.md:234-238 |
| Fog-buoy `Floating`/buoyancy mechanism | 🔴 UNVERIFIED — ground vs decomp before asserting |
| Lighthouse long-range "corona" render trick | 🔴 UNVERIFIED — ground before asserting |
| Through-fog light rendering | 🔴 UNVERIFIED — ground before asserting |
| Vanilla water depth / tides / currents readability | 🔴 UNVERIFIED — ground before asserting |
| Star Glass concept/mechanism | 🔴 OPEN — undefined in docs, needs its own brainstorm |

## 6. 🔴 OPEN QUESTIONS — Daniel's calls (none locked here)

Collected so a future promotion step can work them. **All deliberately left open** —
this is a brainstorm; I lock nothing unilaterally.

1. **Q-MAR-1 — How big is the set, really?** Does the maritime tier ship ~3 real
   pieces (lighthouse, buoy-family, star-instrument) + Star Glass, or do you want more
   of §4 as distinct builds? Which §4 prompts are worth their own doc, and which get
   cut or folded?
2. **Q-MAR-2 — Lighthouse fuel model.** Fueled (tend it) or eternal (earned, v5
   maintenance-free)? Gated on t_117bc232's beacon decision — does the lighthouse
   match the beacon or diverge at v5?
3. **Q-MAR-3 — Does a buoy "ping the minimap" at all?** Is a map ping on-pillar, or do
   we commit fully to world-space cues (light/sound/visible marker) and treat map pings
   as forbidden GPS? (My lean: world-space only; pings are suspect. Your call.)
4. **Q-MAR-4 — What IS the Star Glass?** Spyglass (zoom), star-navigation (heading from
   the sky), or fog-piercing lens? It's roadmap-named but undefined — needs its own
   seed brainstorm before any mechanism.
5. **Q-MAR-5 — Recipes / tier mats** for lighthouse + buoy (+ whatever else promotes).
   All §2.3/§3.3 material guesses are eyeball-only and need a lock when pieces are real.
6. **Q-MAR-6 — Placement gating.** Do lighthouses need a coast/elevation placement rule
   (cf. the Cairn sea-level gate)? Do buoys need open-water / minimum-depth placement?
7. **Q-MAR-7 — Spade vs item, per tool.** Placed infrastructure (lighthouse, buoy,
   route markers) → Spade build menu (Pillar 1). Carried instruments (Star Glass,
   sextant) → item path. Confirm the split per tool; flag any that feel ambiguous.
8. **Q-MAR-8 — Fog/weather rendering approach (shared).** Lighthouse and buoy share a
   "be visible through fog/at distance" problem. Do you want one grounded
   through-fog/long-range approach (shader? billboard halo? audio?) reused across both,
   or per-tool treatments? (Needs the 🔴 fog-render decomp grounding first.)
9. **Q-MAR-9 — Sequencing vs the rest of v5.** Maritime tools sit in the same tier as
   "ship infrastructure" (PARKED:40). Do the navigation aids come first (mark the sea
   you already sail), or after ship pieces (build the ships, then the marks)? Affects
   how these decompose into cards when v5 starts.

## 7. Status & next step

- **This doc is `status: idea`** — captured seeds, nothing specced, nothing locked.
- **Next step is Daniel's rating**, not implementation. When Daniel rates a seed worth
  pursuing, it graduates to its own design doc (lighthouse, buoy, or Star Glass each
  likely warrant one), then — once design questions close — to a buildable spec and a
  card, the same path cartography-v2.md walked.
- **Do not build any of this yet.** v5 is two tiers out (v2/v3 are active). The
  lighthouse in particular is blocked on the v3 Beacon spec (t_117bc232) landing first.
- **Before anything here is asserted as fact**, the 🔴 UNVERIFIED rows in §5 must be
  decomp-grounded (buoyancy, fog rendering, water-depth, long-range light). This doc
  flags them precisely so a future worker grounds them instead of inheriting a guess.
