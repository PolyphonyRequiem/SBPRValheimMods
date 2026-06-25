---
title: "Trailside Beautification — tiered area buffs from maintained trail furniture (living design)"
status: living
purpose: "Living design for the Trailside Beautification system: trail furniture (Painted Signs, Path Lamps, Traveller's Cache, terrain paint) aggregates into tiered area buffs across three categories (Convenience / Safety / Appeal) via a cascade quality-gate, over overlapping comfort-like coverage zones (~32 m radius). Captures Daniel's design across the 2026-06-24 #design thread. The three buffs are LOCKED + grounded: Convenience = stamina-consume reduction; Safety = 'Relaxed' (slowed food digestion); Appeal = 'Inspired' (accumulating skill-gain, also granted by non-cairn comfort). Cairn comfort/Rested is a SEPARATE shipped system. Net-new pillar — NOT in requirements.md v1. Iterate ON this doc, not in chat."
---

# Trailside Beautification

> **What this doc is.** The living design home for Daniel's beautification /
> "travelers welcome" system: how trail furniture aggregates into tiered area
> buffs over overlapping coverage zones. Structured DECIDED (Daniel's locked
> calls, quoted) / OPEN (the knobs) / GROUNDED (what shipped code + vanilla
> decomp actually do). Iterate here, gate the PR, then graduate to a
> version-scoped impl-spec once the tiers + remaining knobs lock.
>
> **There is no "waystation" entity.** An earlier draft reified one; Daniel
> corrected it (2026-06-24). The buff is a **field property of overlapping
> coverage zones**, exactly like vanilla comfort — wherever enough piece coverage
> overlaps, the area carries the buff. No new place-noun, no anchor object.
>
> **Lineage / why it's net-new.** This does **not** supersede anything — it is a
> new pillar that *consumes* pieces several existing slices already ship. It is a
> `docs/design/` doc (not a version planning dir) because **tier placement is an
> open knob** (§6-Q1); filing it under a version dir would presume the answer.
>
> **Cross-link — Trailside Camp.** The "tents" beautifier named in §2 (DECIDED #1)
> and §5.1 is the **Bear Hide Tent** designed in
> [`trailside-camp.md`](trailside-camp.md). Division of ownership: **this** doc
> owns the *aura/buff* layer (the tent as an Appeal/Safety cascade contributor);
> **that** doc owns the *sleep mechanic* (the tent + special bedroll + covered
> fire satisfying Valheim's vanilla sleep prerequisite). When the tent's tier
> locks there, mirror it into the §5.1 category table here.
>
> ⚙️ **Concurrent-editing note (2026-06-24).** This doc and `trailside-camp.md`
> were developed in two **parallel Starbright design sessions** touching the same
> `docs/design/` tree at the same time. A full-file overwrite once clobbered a
> cross-link (recovered). Convention going forward for either session: **prefer
> targeted `patch` edits over whole-file rewrites** on these two docs while both
> are live, and re-read immediately before writing. The two docs are coupled only
> through the shared tent piece (ownership split above) — no other shared state.

## 0. The reframe (read first — it changes the cost estimate)

Daniel's opening framing was "design a beautification buff system from scratch."
Grounding against the repo shows **most of the substrate already ships** — this is
a *layer that aggregates existing pieces*, not new pieces plus a new system:

| Piece (Daniel's word) | Repo reality | Status |
|---|---|---|
| (painted / road) Signs | `Features/Signs/` — Painted Signs (paint, retint, hover). *Also* `Features/MarkerSigns/` (map-pin signs). | 🟢 SHIPPED |
| Path Lamps / trail lights | `piece_sbpr_path_lamp` — Tier-1 = vanilla `piece_groundtorch_wood` scaled 3× vertical. Dataset notes a **"Path Lamp upgrade tier — TBD"** already. | 🟢 SHIPPED (1 tier) |
| Traveller's Cache ("chest") | `docs/design/travellers-cache.md` — public + per-player-private trailside chest. | 🟡 PROPOSED |
| Terrain paint (dirt/paved/grass) | Trailblazer's Spade paths + Replant Grass ship; paving via Spade. | 🟢 SHIPPED |
| Cairns | `Features/Cairns/` — full 5-tier comfort/Rested system. **A SEPARATE system, NOT this one** (§4). Open: does the cairn *piece* still drop a % into the Appeal cascade? (§6-Q8) | 🟢 SHIPPED (separate) |

**Consequence:** the genuinely new work is (a) the **aggregation/cascade engine**,
(b) the **three category buffs** (§3 — two are drop-in stat fields, one needs a
small patch), and (c) **upgrade tiers** for Signs/Lamps to match a 5-tier shape.
The pieces already exist; the engine and the buffs are the build.

## 1. The fantasy

A maintained trail *feels* maintained. Walk a route someone has lit with lamps,
signed at the forks, paved, and stocked with a cache, and the world should reward
you for being on a cared-for road — small buffs that say **"travelers welcome."**
The reward is strongest where the trail furniture is richest and best-built, and
it fades on a neglected or bare path. On-thesis with `trailborne-vision.md`: the
trail becomes *inhabited* infrastructure, not just navigation. It also closes a
build-motivation loop — the buff makes players *want* to build the thing the mod
is trying to get them to build.

## 2. 🟢 DECIDED (Daniel — quoted, 2026-06-24 #design thread)

Quoted verbatim; extensions flagged as such.

1. **Two families of beautifier.** *"beautifiers come in two major forms, terrain
   painting … And decorative/functional. Signs, fences, Cairns, light posts,
   tents, etc. anything that says travelers welcome."*
2. **Pieces feed tiered effects via partial contributions, gated as a cascade.**
   *"items contribute to tiers of some effects … a tier 2 at 30% a tier 3 at 40%
   and 3 tier 1's at 15% would be a tier 1 buff cuz 45% from t1 40% from tier 3
   and 30% at tier 2 is over 100% towards tier 1, but only 30+40 toward tier 2
   and 40 for tier 3."* → **the cascade quality-gate** (§3.1).
3. **Three categories, not one meter.** *"like convenience, safety, and decoration
   as different buffs at different tiers."* → three independent ladders.
   (Decoration renamed **Appeal** per Daniel, 2026-06-24.)
4. **Terrain paint is cascade-EXEMPT but ambient-contributing.** *"Roads and other
   paints would probably be tier system exempt for any buff given for standing on
   a given type of terrain or material, but having them present in range in any
   form might provide small bonuses towards other buff categories."*
5. **Starts in Meadows; tiers + pieces scatter upward.** *"this system starts in
   meadows and has a scattering of additional tiers and pieces introduced at
   higher levels."*
6. **Tier is progression-driven — partially.** *"Tier is progression driven to a
   degree yes."* (Matches Pillar 2: pigments are biome-tiered because ingredients
   are.)
7. **OUR pieces, not vanilla.** *"I meant our trailblazer painted signs, Cairns,
   and lamps, not the vanilla ones."*
8. **Aggregation is comfort-like radius, but wider — ~32 m.** *"the system is
   similar to the way that comfort is calculated, so radius, but more like 32
   meters rather than … 10[?]."* → 🟢 GROUNDED: vanilla comfort radius **is 10 m**
   (decomp `c_ComfortRadius = 10f`; cairn `CairnComfortRadius = 10f`). The ~32 m
   is a deliberate ~3× widening (§4.2).
9. **No "waystation" entity — overlapping coverage zones.** *"What 'waystation'
   are you talking about? We have overlapping coverage zones."* The buff is a
   field property of overlapping coverage, like comfort. No anchor object.
10. **Cairn is a different system.** *"Cairn is a different system entirely."* The
    cairn comfort/Rested mechanic is NOT part of beautification (§4). (Whether the
    cairn *piece* still contributes a % to the Appeal cascade is open — §6-Q8.)
11. **The three buffs (§3.2) — locked this thread.** Convenience = stamina-consume
    reduction (*"no to weight, no to speed, but yes to stamina consume reduction"*);
    Safety = *Relaxed*, slows digestion (*"it grants 'relaxed' which slows
    digestion"*); Appeal = *Inspired*, an accumulating timed buff *"that slowly
    accumulates over time on a trail … also innately … granted by non cairn
    comfort … so that resting in a normal base is likely to grant inspiration."*
12. **Safety pieces also grant base anti-spawn — secondary.** *"most safety things
    should grant player base anti spawn though but that's secondary"* — the
    vanilla workbench no-spawn bubble as a secondary property, not the primary
    tiered buff (§3.2).
13. **T1–T3 buff values approved.** *"these are good targets"* — the §3.3 magnitude
    table + caps are locked as starting targets (tune vs cascade thresholds in
    playtest; tier *count* past 3 still open, Q3).
14. **Traveller's Cache = Black Forest.** *"Cache is b territory"* — not a Meadows
    starter; the Meadows starter set is Painted Signs + Path Lamps + terrain paint
    (cairn piece pending Q8). Cache is the BF-tier Convenience anchor (§6-Q1).
15. **Convenience = run + jump stamina only, NOT attack.** *"sorry just run and
    jump stamina."* Keeps the trail buff out of the combat lane (§3.3).
16. **Per-piece authored % + max-contributors cap per contribution category.** *"I
    think more control per piece on percentage, and max contributors of a given
    category and a contribution category would be good. Category being a named set
    of any sort like fences or lights or whatever."* → rejects the one-constant
    formula; each piece authors its `contribution{}` table + declares a
    `contributionCategory` ("lights"/"signs"/…); each set has a max-N cap (§5.4).

## 3. The mechanic + the three buffs

### 3.1 The cascade quality-gate (core)

Each category has its own tier ladder. **Every piece drops its contribution % into
the meter of its own tier AND every tier below it.** You get the highest tier whose
meter reaches 100%. Worked from Daniel's example (T2@30, T3@40, 3×T1@15=45):

```
                          contributes to →   T1    T2    T3
  T3 piece  (40%)                             40    40    40
  T2 piece  (30%)                             30    30     ·
  T1 ×3     (45%)                             45     ·     ·
                                            ─────  ────  ────
                          meter total:       115    70    40
                          gate (≥100):        ✓✓     ✗     ✗
                          → buff awarded:   TIER 1
```

**Why this is the right shape (protect it):** the cascade is a *quality gate, not
a quantity sum.* A hundred Tier-1 stakes can never buy a Tier-3 buff, because
Tier-1 pieces don't touch the Tier-3 meter. High tiers *demand* high-tier
craftsmanship. This is the distinguishing idea vs a naive "add up beauty points."

### 3.2 🟢 The three category buffs (LOCKED — grounded vs decomp)

All three reward "a maintained trail is worth traveling," from three angles — food,
stamina, progression — none overlapping the cairn's comfort/rest lane.

| Category | Buff | Effect | Vanilla primitive | Build shape |
|---|---|---|---|---|
| **Convenience** | *(name TBD)* | reduced **run + jump** stamina consumption (NOT attack — Daniel, §3.3). **No carry weight, no move speed** (Daniel). | `m_runStaminaModifier` + `m_jumpStaminaModifier` — real `SE_Stats` fields, serialized at decomp `:15429`, applied via `SEMan.ModifyRunStaminaDrain` `:17632` (attack field left at 0) | 🟢 **drop-in stat field** |
| **Safety** | **Relaxed** | slows food **digestion** → your food stretches further, food buffs decay slower. Secondary: most safety pieces grant the vanilla base **anti-spawn** bubble (Daniel #12, secondary). | digestion tick is `food.m_time -= 1f`/s in `Player.UpdateFood` (`:17534`); buff magnitude scales off `m_time / m_foodBurnTime`. Anti-spawn = `EffectArea.Type.PlayerBase` (`:11705`/`:94501`). | 🟡 **needs a small Harmony patch** on `UpdateFood` — *there is NO food-burn modifier in `SE_Stats`/`SEMan`* (checked the whole field block). One-line decrement scale; well-bounded. |
| **Appeal** | **Inspired** | a **timed buff that slowly accumulates** while you're in Appeal coverage on a trail; you carry it after leaving. **Also innately granted by non-cairn comfort** — resting in a normal base grants Inspiration too. Effect: faster skill-gain. | `m_raiseSkill = All` + `m_raiseSkillModifier` (`ModifyRaiseSkill` `:17200`). Non-cairn-comfort hook: feed Inspired from vanilla Rested/comfort that is NOT cairn-sourced (the `CairnComfortStash` signal already distinguishes cairn comfort, so "comfort that isn't ours → Inspiration" is a clean detection). | 🟡 **accumulator + grant-hook** — not a static aura; the accumulate-over-time + carry-after-leave behavior is the novel bit |

**Why Relaxed is purely positive (not a nerf):** in Valheim your food *buff*
shrinks as the food clock runs down (`f = m_time/m_foodBurnTime`, `:17535`). Slower
digestion = the clock runs down slower = your HP/stamina/eitr bonus lasts longer.
"Relaxed slows digestion" reads as physiologically backwards (real relaxation aids
digestion) but is mechanically a clean win — pure flavor-text fix if it ever
itches: *"your body savors the meal."*

### 3.3 🟢 T1–T3 values + 🔒 caps (APPROVED — Daniel, 2026-06-24, *"these are good targets"*)

Starting magnitudes, grounded against vanilla anchors. **Approved as targets with
caps, NOT as final** — buff magnitude and the cascade-% thresholds (§5.x / Q5) must
be tuned *together* in playtest; a generous piece-% curve + these values could make
T3 trivial to reach.

| | **Convenience** (stamina-drain −%) | **Safety** — *Relaxed* (digestion −%) | **Appeal** — *Inspired* (skill-gain +%) |
|---|---|---|---|
| **T1** | −8% | −10% (~+2 m on a 20 m food) | +5% |
| **T2** | −15% | −20% (~+4–5 m) | +10% |
| **T3** | −25% | −33% (~+10 m on a 30 m food) | +15% |
| vanilla anchor | Eikthyr power ≈ −60%/5 s (far above) | none (net-new) | none (net-new) |

Grounding: stamina modifier is additive on drain (`drain += baseDrain * modifier`,
decomp `:25931`) so −0.25 = 25% cheaper; food durations top ~30 m (Serpent stew /
Lox pie, corpus) so the % reads in real minutes; **vanilla has NO status-effect
skill-XP bonus** (`Skills.RaiseSkill :24091` has no Rested check) — Inspired is
entirely net-new, hence deliberately conservative; persistence benchmark is
`SE_Rested` (`m_baseTTL = 300 s` + `60 s`/comfort, `:25341`).

🔒 **Caps / limits (load-bearing — the values are only safe WITH these):**

1. **One category → one buff, best-tier-only, NO stacking.** You hold exactly the
   highest tier you qualify for per category; never additive. Two overlapping T2
   zones ≠ T3. The cascade enforces this — stated so nobody "fixes" it into a sum.
2. **Convenience hard cap −25%.** Stamina-cost reduction compounds with
   food/armor/skill into "stamina stops mattering." −25% stays a *help*, not a
   trivializer — the cap to protect hardest.
3. **Relaxed cap −33% (never ≥ 50%).** Past ~half, food management — a core
   survival loop — stops mattering. −33% = "a good meal stretches noticeably," not
   "eat half as often."
4. **Inspired +15% ceiling AND a low persistence cap.** XP gain compounds over the
   *whole* playthrough → the sneaky-strong one. Carry-after-leaving buff caps at
   **~5 min** (under `SE_Rested`'s 300 s base), fills slowly (~full in 3–4 min of
   trail presence) → rewards *traveling a good trail*, not parking on one tile to
   farm a permanent XP aura. **Watch-this-one flag (Daniel acknowledged the
   targets; balance risk noted):** if playtest says T3 is invisible, prefer raising
   from a lower floor (+3/+6/+10%) over shipping high — easier to add than claw back.

⚠️ **✅ RESOLVED (Daniel, 2026-06-24, OOB): Convenience discounts RUN + JUMP stamina
only — NOT attack.** *"sorry just run and jump stamina."* So Convenience applies
`m_runStaminaModifier` + `m_jumpStaminaModifier`, leaves `m_attackStaminaModifier`
at 0. Beautification stays out of the combat-buff lane — the trail makes *travel*
cheaper, not *fighting* cheaper. (Was Q9; now locked. The §3.3 T-values apply to
run + jump drain.)

**Appeal is NOT comfort/rest** — that's the cairn's lane (§4). Appeal pays out in
*skill progression*, mechanically distinct from the cairn's health/stamina regen.

## 4. Cairn comfort is a SEPARATE system (do not entangle)

The cairn's shipped 5-tier comfort/Rested mechanic (`Features/Cairns/`,
`CairnComfortRadius = 10f`, comfort floor 3→7, open-air Rested grant) is **its own
system** and is untouched by beautification (Daniel #10). It is a *sit-here-and-
rest* effect on its own 10 m radius; beautification is a *travel-a-good-trail*
effect on ~32 m overlapping coverage. They coexist without interaction.

**The one open seam (§6-Q8):** does the cairn *piece* still drop a % into the
**Appeal** cascade as a decorative stone marker? Daniel's earlier msg listed cairns
as a contributor; "different system" scopes the *comfort buff* out, but may or may
not scope the *piece's Appeal contribution* out. Flagged, not assumed.

> **Note the Inspired↔comfort bridge is one-directional.** Beautification does not
> touch the cairn comfort system, but Inspired *reads* non-cairn comfort as one of
> its grant sources (§3.2). That's Appeal consuming a vanilla signal, not
> beautification modifying the cairn system — the wall holds.

## 5. The aggregate model

### 5.1 Category assignment (working table — confirm §6-Q5)

Net-new; no repo prior art. Working model: primary at full weight, optional
secondary at half (legible, but lets dual-purpose pieces exist):

| Piece | Primary category | Secondary (½) | Note |
|---|---|---|---|
| Path Lamp | **Safety** (lit = secure) | Appeal | needs upgrade tiers (§6-Q2) |
| Painted Sign | **Convenience** (wayfinding) | Appeal | which sign? §6-Q6 |
| Traveller's Cache | **Convenience** (resupply) | — | tier conflict w/ cache doc, §6-Q1 |
| Terrain paint | *(cascade-exempt)* | small ambient → Appeal/Convenience | §5.3 |
| Cairn (piece) | **Appeal?** | — | contribution open — §6-Q8 |
| (future: fences, tents, light posts) | TBD | TBD | the "scatter upward" set |

### 5.2 The ~32 m coverage and the corridor property

Vanilla comfort = 10 m; the ~32 m is ~3× wider on purpose: a piece's influence
reaches the *approach*, not just the spot. Emergent payoff worth tuning toward: if
trail markers sit ~32–64 m apart, **adjacent coverage zones nearly touch**, so a
well-furnished trail becomes a *continuous corridor* of buff rather than
disconnected bubbles. Spacing × radius is the lever for "how dense must a trail be
to stay buffed end-to-end." (Aggregation reuses the cairn's throttled-OverlapSphere
pattern — `CairnComfortStash`, one physics query per ~2 s off the hot path — so a
32 m multi-piece scan is cheap.)

### 5.3 Terrain paint — exempt but ambient (Daniel #4)

- **Stand-on buff:** painted material (dirt road / paved / cultivated / grass)
  gives a flat buff *while you stand on that material* — **not tiered, not
  aggregated, no cascade.** Material-keyed and binary.
- **Ambient contribution:** paint *present in range* adds a *small* % into the
  Appeal (and maybe Convenience) cascade meters — paving nudges an area's tier
  without ever being a cascade tier itself.

### 5.4 🟢 PROPOSED — the cascade-piece model (per-piece authored, capped per set)

> An earlier draft of this section proposed a one-constant formula (`BASE ×
> pieceTier`). **Daniel corrected it (2026-06-24): more control per piece on the
> percentage, plus a max-contributors cap per category, where "category" is a
> named set of any sort (fences / lights / …).** Taking it — and it matches the
> repo better: the shipped cairn floors are a hand-authored per-tier array
> (`ComfortFloorByTier = {0,3,4,5,6,7}`, `Cairns.cs:85`), not a formula. The
> authored model is consistent with how the existing system already works.

**🔴 Two meanings of "category" — pinned (the word was overloaded):**
- **Buff category** — Convenience / Safety / Appeal. *What the player gets.*
- **Contribution category** (Daniel's new term) — `"lights"` / `"signs"` /
  `"fences"` / `"caches"` / `"tents"`. *A named set of pieces.* The unit the
  **max-contributors cap** and the variety rule apply to. A piece declares which
  contribution category it belongs to.

**Each piece authors three things:**

| field | example | role |
|---|---|---|
| `contributionCategory` | `"lights"` | the named set — the cap + variety unit |
| `pieceTier` | 1–3 | which buff-tier meters it may feed (cascade §3.1 — unchanged) |
| `contribution` | `{Safety: 22, Appeal: 8}` | **hand-authored %** per buff category — the per-piece control |

So contribution is **per-piece data, not a formula** — a T2 lamp can be authored
Safety-22 while a T2 sign is Convenience-18; each piece tuned on its own merits.
(The cairn-array precedent: author a table, don't derive.)

**Max contributors per contribution category (the anti-spam, Daniel's call).** Each
contribution category has a cap N: only the **best N pieces** of that set count
toward the meters; the (N+1)th contributes nothing. Worked — caps `{lights: 2,
signs: 2}`, place 5× T1 lamp (Safety 12 each) + 1 sign:

```
  lights: 5 present, cap 2 → best 2 count → Safety 12 + 12 = 24   (3 lamps DEAD)
  signs:  1 present, cap 2 → counts        → Convenience <sign's value>
  → Safety meter = 24%, NOT 60%. The cap IS the anti-spam.
```

This **replaces** the earlier "≥2 distinct types to fill a tier" gate (Q4) with
something better: the cap makes spamming one set *useless* (not just
*insufficient*), and pushes variety because the way to raise a meter past the cap
is to add a *different* contribution category. (Whether we ALSO keep a distinct-set
floor is a small open knob — §6-Q4.)

**🔑 The cascade "scatter upward" free-win SURVIVES unchanged.** `pieceTier` still
gates which buff-tier meter a piece may feed (§3.1), so a Safety-T3 *buff* is still
physically unreachable without T3 *pieces* — and piece tier is biome-material-gated
(Q2, cairn model). Daniel's correction changed *how much each piece gives* and *how
many count* — NOT the tier-gating. So the buff progression still rides entirely on
the piece-upgrade progression; no separate biome-lock on buffs needed.

**The knobs this leaves (all per-piece / per-set data — playtest-tunable):**
- **`contribution{}` per piece** — the authored % table. Daniel's 15/30/45 example
  is a fine *starting* shape (low/mid/high-tier) but each piece is now free to
  diverge from it.
- **`N` (max contributors) per contribution category** — how many of a set count.
  Lean small (2–3) so variety matters fast; can differ per set (maybe lights cap 3,
  caches cap 1).
- **Open: a distinct-set FLOOR too?** (§6-Q4) — require ≥2 *different* contribution
  categories present at all, on top of the per-set caps? The caps already force
  variety to *scale* a meter; a floor would also block a single-set trail from
  earning *any* buff. Lean: not needed (the cap handles it), but flagging.

> This reshapes §6-Q4 and §6-Q5 (both now "PROPOSED — per-piece authored + capped").
> The remaining piece-side open is Q2 (piece-tier counts + material gates) and the
> §6-Q4 distinct-set-floor sub-knob.

## 6. 🟡 OPEN — knobs Daniel decides

**Q1 — ✅ RESOLVED (Daniel, 2026-06-24): Cache = Black Forest ("b territory").**
The Traveller's Cache is NOT a Meadows starter — it joins at Black Forest when the
tier opens, exactly per the cache-doc §9-Q1 lean and Daniel's "scatter upward"
rule. **The Meadows starter set is therefore Painted Signs + Path Lamps + terrain
paint** (+ cairn piece pending Q8). The cache becomes the *Convenience* anchor that
arrives with the BF tier. → `travellers-cache.md` §9-Q1 should be marked resolved
to BF and graduate to the `docs/v2/planning/` dir.

**Q2 — Path Lamp & Sign upgrade tiers.** Lamps ship 1 tier ("upgrade tier TBD"
already in the dataset), Signs ship untiered. For the cascade to award high
category tiers, they need a tier ladder. → Mirror the cairn's material-gated
upgrade model? How many tiers each?

**Q3 — ✅ MAGNITUDES APPROVED (Daniel, 2026-06-24) — tier COUNT still open.** The
per-tier buff *values* are locked at T1–T3 with caps (§3.3, "these are good
targets"). What remains: is **3 tiers** the ceiling per category, or do higher
biomes extend to T4/T5 (matching the cairn's 5-tier shape)? The §3.3 values define
T1–T3; a T4/T5 extension would need values + caps re-checked (esp. the Convenience
−25% and Inspired +15% hard caps, which should NOT simply keep climbing).

**Q4 — 🟢 PROPOSED (§5.4): max-contributors CAP per contribution category.** Daniel
(2026-06-24) replaced the "≥2 distinct types" gate with a per-set cap N: only the
best N pieces of a contribution category count; the rest are dead. Stronger
anti-spam (spamming one set is *useless*, not just insufficient). **Sub-knob still
open:** also keep a distinct-set *floor* (≥2 different categories to earn any buff)?
Lean no (the cap handles variety). → confirm N values + the floor knob.

**Q5 — 🟢 PROPOSED (§5.4): per-piece AUTHORED contribution %, not a formula.** Daniel
(2026-06-24) wanted per-piece control: each piece declares `contribution{BuffCat:%}`
hand-authored (cairn-array precedent, `Cairns.cs:85`), plus a `contributionCategory`
("lights"/"signs"/…). The 15/30/45 example is a starting *shape*, not a locked
formula. → confirm the authored-table model + seed the first piece values.

**Q6 — "Road signs": Painted Signs or Marker Signs?** Repo has both. Daniel said
"painted signs" — assume Painted Signs feed the cascade; confirm Marker Signs do/don't.

**Q7 — Legibility / HUD.** The cascade math is invisible. Without a HUD showing the
three category meters + current tier + contributors, players won't know why they're
stuck at T1. Stack has custom-HUD capability (Sunstone halo, compass). In scope now
or fast-follow?

**Q8 — Does the cairn PIECE contribute % to the Appeal cascade?** Cairn comfort is
out (§4). Is the cairn *as a decorative marker* still an Appeal contributor, or
fully out of beautification? (Daniel's earlier msg listed it; "different system"
may scope only the comfort buff.)

## 7. Numbered open-questions (fast triage)

**Resolved 2026-06-24 thread:** Q1 cache=BF ✅ · Q3 magnitudes approved (tier *count*
open) ✅ · Q9 Convenience = run+jump stamina, not attack ✅ · Q4+Q5 reshaped to the
§5.4 per-piece-authored + capped model (Daniel's correction) — now pending only
value-confirms, not design.

Still open (in pickup order):
1. ~~Cache tier~~ → **BF, locked** [Q1 ✅]
2. **Lamp/Sign upgrade tiers** — mirror cairn material-tiers? how many each? [Q2]
3. Tier *count* — 3 (T1–T3 values locked, §3.3) or extend to T4/T5? [Q3 — values ✅, count open]
4. **`N` (max contributors) per contribution category** + the distinct-set *floor* sub-knob (lean: no floor) [Q4 — model ✅, values open]
5. **Seed the per-piece `contribution{}` authored tables** + confirm "contribution category" naming [Q5 — model ✅, values open]
6. "Road signs" = Painted Signs (assumed) or Marker Signs too? [Q6]
7. HUD/meters surface — in scope now or fast-follow? [Q7]
8. Does the cairn PIECE contribute % to the Appeal cascade, or fully out? [Q8]
9. ~~Convenience attack-stamina?~~ → **run + jump only, locked** [Q9 ✅]

> **Until the remaining knobs (Q2, Q4–Q8 values + the Q3 tier-count) settle this
> stays `status: living` and does NOT graduate.** The buff design is locked (3
> categories, T1–T3 values + caps, run+jump scope) AND the cascade-piece *model* is
> locked (§5.4 per-piece authored + per-set cap); what's left is *values* (piece
> tiers, the authored % tables, the cap Ns) + HUD + two small routing calls
> (Q6/Q8). When the values land it graduates to a version-scoped impl-spec, with a
> SpecCheck manifest pass (new pieces/recipes) + a `PIECES_AND_CRAFTABLES.md`
> dataset row — code + spec + manifest move together (AGENTS.md).

## 8. 📌 Session resume point — picked up 2026-06-25

**Where we are:** the design is ~80% locked. Both the *buff* layer (what the player
gets) and the *cascade-piece model* (how pieces feed it) are decided; everything
remaining is **values + two routing calls + HUD scope**, not core design.

**Locked (don't re-litigate):** 3 buff categories (Convenience/Safety/Appeal) ·
their effects (run+jump stamina −%, Relaxed=slower digestion, Inspired=accumulating
skill-gain also fed by non-cairn comfort) · T1–T3 magnitudes + 4 caps (§3.3) ·
cascade quality-gate (§3.1) · ~32 m overlapping coverage (no "waystation") · cairn
comfort is a SEPARATE system · per-piece authored `contribution{}` + per-set
max-N cap (§5.4) · Cache=Black Forest, Meadows starter = Signs+Lamps+paint.

**Next session — pick up here (3 quick value-confirms close the design):**
1. **Q4/Q5 values** — confirm "contribution category" name (or pick: contribution
   set / piece family / furnishing class); set `N` per set (lean 2–3, can vary);
   confirm NO distinct-set floor; seed first `contribution{}` tables for Sign + Lamp.
2. **Q2** — piece-tier counts + material gates for Lamp & Sign (cairn model).
3. **Q6 + Q8** — Painted vs Marker signs; does the cairn *piece* feed Appeal.
4. **Q7** — decide HUD now or fast-follow (the legibility surface).

Then it graduates to an impl-spec (architect). **Heads-up for whoever picks up:** a
parallel Starbright session authored `trailside-camp.md` in the same tree tonight —
use targeted `patch` edits, not full rewrites, on both docs (see the concurrent-edit
note in the header). Neither doc is committed yet; both are lint-clean.
