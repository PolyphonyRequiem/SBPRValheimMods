---
title: "Trailside Camp — Bear Hide Tent + special bedroll + covered camp fire (the sleep-anywhere triad)"
status: living
purpose: "Living design for the Trailside Camp: a portable three-piece camp (Bear Hide Tent + a special bedroll + a small covered camp fire) that together satisfy Valheim's vanilla SLEEP prerequisite out on the trail. GROUNDED hard against the decompiled Bed/Cover/Fireplace/Player code (line-cited) so the special-bed + covered-fire requirement is mechanism-true, not vibes. Captures Daniel's 2026-06-24 #design thread (tent placeholder = TraderTent; 'special form of bed + covered small fire pit for the sleep prereq'). Net-new Trailborne furniture — NOT in requirements.md v1. Iterate ON this doc, not in chat."
---

# Trailside Camp — the sleep-anywhere triad

> **What this doc is.** The living design home for Daniel's portable trail camp:
> a **Bear Hide Tent**, a **special bedroll**, and a **covered camp fire** that
> together let an Explorer sleep out on the trail (skip the night / set a
> waypoint spawn) without hauling a roofed base. Structured DECIDED (Daniel's
> locked calls, quoted) / OPEN (the knobs) / GROUNDED (what the decompiled game
> actually requires). Iterate here, gate the PR, then graduate to a
> version-scoped impl-spec once the tier + knobs lock.
>
> **Lineage / cross-links.** The tent is also the "tents … travelers welcome"
> beautifier promised in `trailside-beautification.md` §2 (DECIDED #1) / §5.1 — that doc owns
> the *aura/buff* layer; **this** doc owns the *sleep mechanic*. The Cover/Resting
> machinery here is the same vanilla surface decoded for the cairn open-air
> comfort fix (`docs/investigations/2026-06-13-cairn-no-open-air-comfort-near-fire-gate.md`);
> read that for the `SE_Rested`/`m_nearFireTimer` side. Net-new pillar — does not
> supersede anything.

## 0. The fantasy

The Explorer is the party member who sleeps rough. Far from base, at dusk, they
plant a **Bear Hide Tent**, unroll a **bedroll** under it, light a **small camp
fire** in the doorway — and that little camp is enough to pass the night safely
and (optionally) set a waypoint they can respawn to. It is the survival-camp
fantasy Valheim gestures at but never quite gives you: vanilla makes you build a
*roofed structure* (walls + roof to 80% cover) before a bed will let you sleep,
which is a base, not a camp. Trailside Camp makes the *camp itself* legal to
sleep in — by being a deliberately-shaped exception to one specific vanilla gate.

## 1. 🟢 DECIDED (Daniel — 2026-06-24 #design thread)

1. **The triad is the unit.** *"we need the special form of bed and a covered
   small fire pit nearby for the sleep prereq."* Sleep requires **all three**:
   tent (cover), bedroll (the sleep surface), covered fire (heat). This doc's job
   is to make those three, together, satisfy §3's vanilla gate chain.
2. **Tent placeholder = `TraderTent` (Haldor's), legs kept, native size,
   VISUAL-ONLY.** *"Make it trader tent keep the legs, and keep the size. It's
   fine."* + *"make it so … It's placeholder art anyhow."* Adopt the vanilla
   `TraderTent` mesh/material as the Bear Hide Tent's stand-in art. Native dims
   **8.0 W × 4.9 H × 6.9 D m** (`vprefab inspect TraderTent`). No shelter-collider
   work on the placeholder — see §2 for why that's the *correct* call, not a
   shortcut.
3. **It's "Bear Hide" — and that resolves the tier (Black Forest).** 🟢 GROUNDED:
   the **Bear** (internal `Bjorn`, hide = `BjornHide`) is a **Black Forest** creature
   (wiki `Bear.md`/`Bear_hide.md`), NOT Mountains (an earlier from-memory claim — bears
   are BF). So a *bear-hide* tent is a **Black Forest-tier** piece with no name/biome
   conflict, and the Meadows-vs-Mountains tension flagged last turn dissolves. Recipe
   leans Bear hide + wood/leather; `TraderTent`'s diffuse (stitched brown hide panels,
   verified from the extracted `TraderTent_d`) matches.
4. **Sleep = SKIP-NIGHT-ONLY, no spawn-point set.** Daniel: *"Skip night only plus
   comfort and inspiration."* The bedroll lets you sleep through the night but does **NOT**
   become your respawn point (resolves §5-Q2). 🟢 GROUNDED divergence (§3.1): vanilla
   `Bed.Interact` *always* `SetCustomSpawnPoint`s when you claim/sleep — "skip-night-only"
   means our bedroll path runs the sleep (`AttachStart isBed:true` → the `s_inBed` →
   `SkipToMorning` chain) **without** the `SetCustomSpawnPoint` call. Your home spawn is
   never overwritten by a trail nap.
5. **Comfort is FREE from the skip; "Inspiration" is the beautification *Inspired* buff.**
   🟢 GROUNDED: waking from a skip already grants vanilla **`SE_Rested`**
   (`Player.SetSleeping(false):21464`) — the comfort/Rested buff is the *same event* as
   the night-skip, not a bolt-on. So "plus comfort" = rides vanilla Rested. **"Inspiration"
   is NOT net-new to this doc** — `trailside-beautification.md` already LOCKED **Inspired**
   (its Appeal buff): a timed, accumulating **skill-gain** buff (`m_raiseSkill = All` +
   `m_raiseSkillModifier`) that is *"also innately granted by non-cairn comfort — resting in
   a normal base grants Inspiration too."* **The camp bedroll is exactly such a non-cairn
   comfort source**, so it grants Inspired through that doc's already-grounded hook — the
   camp *consumes* the buff, it does not define a parallel one. (§5-Q7 is now just "confirm
   the bedroll counts as a non-cairn comfort grant," not "design Inspiration.")

## 2. Why the tent is visual-only — and still does real mechanical work (GROUNDED)

The placeholder ships with **no added cover collider**, and that is deliberate.
Shelter/cover in Valheim is **not a flag** — it is an emergent raycast (`Cover`,
`assembly_utils`), and the tent's *existing* collider already passes the part
that matters for a camp. Two distinct results from one mesh:

- **`underRoof` = TRUE.** `Cover.IsUnderRoof(p)` is an upward spherecast that
  succeeds if it hits any non-`"leaky"` collider on the cover mask
  (`{Default, static_solid, Default_small, piece, terrain, vehicle}`). The
  `TraderTent` collider sits on **`static_solid`** (verified layer 15 = static_solid
  against the game TagManager) and isn't leaky → **the canopy passes `underRoof`.**
- **`coverPercentage` ≈ 0.47, NOT ≥ 0.80.** The 17-ray cover check (1 up, 8
  diagonal-up, **8 horizontal**) tops out ~0.47 standing under the open canopy —
  simulated against the real collider mesh, **0/8 horizontal rays blocked** every
  position, because it's open-sided. It never reaches the 0.80 `InShelter`/
  `CheckExposure` bar.

**That split is the whole design.** The canopy's `underRoof=true` already buys
three things the camp needs *for free*, while its sub-0.8 cover is exactly the one
gate the **special bedroll** exists to relax:

| Vanilla gate (player/fire under the open tent) | Canopy gives `underRoof`? | Result |
|---|---|---|
| Player **Wet** status (`Player.UpdateEnvStatusEffects:17348` — Wet only if `flag5 && !m_underRoof`) | yes | **stays dry** ✓ |
| Fire **doused by rain** (`Fireplace.CheckWet:106505` — `flag2 && !underRoof` ⇒ wet) | yes | **stays lit in rain** ✓ |
| Bed `CheckExposure` **`!underRoof`** half (`Bed.CheckExposure:99680`) | yes | passes the roof half ✓ |
| Bed `CheckExposure` **`coverPercentage < 0.8`** half (`:99685`) | n/a (0.47) | **FAILS** ✗ ← bedroll's job |

So a *vanilla* bed under the Bear Hide Tent fails to allow sleep on **one** clause
only: the 0.8 cover threshold. Everything else the camp satisfies naturally.

## 3. GROUNDED — the full vanilla sleep prerequisite (decomp, line-cited)

`Bed.Interact(human, repeat, alt)` (`assembly_valheim.decompiled.cs:99592`), when
the bed is your current spawn and you press Use, runs this gate chain — **every
one must pass** or sleep is refused with the noted center-message:

1. **Time** — `EnvMan.CanSleep()` (`:99622` → `CalculateCanSleep:81205`): must be
   **afternoon or night** AND `now > wakeupTime + m_sleepCooldownSeconds`. Else
   `$msg_cantsleep`.
2. **No enemies** — `CheckEnemies` (`:99667`): `!human.IsSensed()` — nothing is
   currently aware of you. Else `$msg_bedenemiesnearby`.
3. **Not exposed** — `CheckExposure` (`:99677`) on the **bed's spawn point**:
   `Cover.GetCoverForPoint(GetSpawnPoint(), out cover, out underRoof)`; requires
   `underRoof` (else `$msg_bedneedroof`) **AND** `cover >= 0.8` (else
   `$msg_bedtooexposed`). **← the open canopy fails the 0.8 half (§2).**
4. **Near fire** — `CheckFire` (`:99694`): `EffectArea.IsPointInsideArea(bed.pos,
   EffectArea.Type.Heat)` — the bed must sit inside a **burning** fire's Heat area.
   Else `$msg_bednofire`. **← why the fire must stay LIT, hence COVERED (§2).**
5. **Not wet** — `CheckWet` (`:99657`): player must not have the **Wet** status.
   Else `$msg_bedwet`. **← the canopy keeps the player dry (§2).**

(Passing all five ⇒ `human.AttachStart(..., isBed:true, "attach_bed")` — the sleep
animation/skip. Setting a *spawn point* on a non-current bed only needs gate 3,
`CheckExposure`, per `:99647`.)

### The fire-pit "covered" requirement, grounded
`Fireplace.CheckWet` (`:106493`): a fire goes `m_wet` (stops burning → no Heat
area → kills gate 4) when **either** high wind (`windIntensity >= 0.8`) **and**
`cover < 0.7`, **or** rain (`EnvMan.IsWet()`) **and** `!underRoof`. Under the Bear
Hide Tent canopy: rain is handled (canopy = `underRoof`), but a **high-wind storm
still douses it** (canopy's 0.47 < 0.7). So "covered by the tent" keeps the camp
fire lit through **rain**; a true storm is an open knob (§5-Q4) — accept it, or
give the camp fire its own tight roof/0.7 cover.

### 3.1 The night-skip mechanism, and how "skip-only, no spawn" diverges (GROUNDED)
Passing the 5 gates calls `human.AttachStart(..., isBed:true, "attach_bed")`, which
sets the character ZDO flag `s_inBed`. The **`Game`** loop then coordinates the skip
(`:84708`): when `!IsTimeSkipping() && (IsAfternoon() || IsNight()) &&
EverybodyIsTryingToSleep()` (every character ZDO has `s_inBed`, `:84716`) → it calls
`EnvMan.SkipToMorning()` + RPCs `SleepStart`/`SleepStop` to everyone. On wake,
`Player.SetSleeping(false)` (`:21456`) fires `$msg_goodmorning`, grants
**`SE_Rested`** (`:21464`), and stamps `m_wakeupTime`. **Three consequences for this design:**

1. **"Comfort" is not a separate feature — it IS the wake event.** `SE_Rested` is
   granted by the same `SetSleeping(false)`. The camp gets Rested for free; "plus
   comfort" just means optionally raising its *level* (longer TTL) the way the cairn
   already does (`SE_Rested.CalculateComfortLevel` floor-clamp).
2. **"Skip-only, no spawn point" is a real divergence from vanilla `Bed`.** Vanilla
   `Bed.Interact` calls `SetCustomSpawnPoint` on the claim/sleep paths (`:99613/:99651`).
   The skip itself (the `s_inBed` → `SkipToMorning` chain) does **not** depend on the bed
   being your spawn — it only needs the in-bed flag. So the bedroll achieves skip-night
   *without* spawn-setting by running `AttachStart(isBed:true)` and **omitting**
   `SetCustomSpawnPoint`. (VERIFY at impl: confirm the bedroll's `Bed` can drive
   `AttachStart`/`s_inBed` without auto-claiming spawn — may need the Tag to call
   `AttachStart` directly rather than routing through the full vanilla `Bed.Interact`
   spawn-set branch.)
3. **Multiplayer gate is server-wide (`EverybodyIsTryingToSleep`).** Same as vanilla:
   ALL players must be in a bed for the skip to fire. A lone Explorer can sleep solo; in a
   group, the camp bedroll behaves exactly like any vanilla bed for the all-asleep vote.

## 4. Architecture sketch (ADDITIVE, ADR-0006 — no runtime clones)

All three pieces are built from scratch per ADR-0006 (read vanilla prefabs as
*blueprints* via `vprefab`/`ZNetScene.GetPrefab`; never `Instantiate`-then-strip):

- **Bear Hide Tent** (`piece_sbpr_bearhide_tent`) — additive piece: graft the
  `TraderTent` mesh+material as a ZNetView-free cosmetic child onto a
  `ZNetView + Piece + WearNTear + collider` shell (the Surveyor's Table /
  Traveller's Cache `ConstructPieceShell` + `GraftVisualSubtree` pattern). The
  grafted collider must land on an **in-cover-mask, non-`leaky` layer** (e.g.
  `static_solid` like the donor) so it keeps `underRoof` (§2). Placeholder art =
  donor mesh; real bear-hide art is a later swap behind the same prefab name.
- **Special bedroll** (`piece_sbpr_bedroll`) — additive piece carrying a vanilla
  **`Bed`** component (so it sets spawn + drives `AttachStart`) **plus a Harmony
  patch on `Bed.CheckExposure` gated to our prefab** that relaxes the gate to
  `underRoof`-only (drop the `cover >= 0.8` clause for our bedroll; keep
  `underRoof` so you still can't sleep under open sky). This is the **minimal**
  patch surface — §2 shows it's the *only* vanilla clause the camp can't satisfy
  natively. Gates 1/2/4/5 stay vanilla (you still need night, safety, a lit fire,
  and to be dry).
- **Covered camp fire** (`piece_sbpr_camp_fire`) — additive **small** `Fireplace`
  piece providing the Heat `EffectArea` gate 4 needs. "Covered" is satisfied by
  standing it under the tent canopy (`underRoof`); §5-Q4 decides whether it also
  carries its own mini-roof for storm-proofing.

> **Patch honesty:** the only behavioral patch is the gated `Bed.CheckExposure`
> relax. It MUST be prefab-gated (check the bed's prefab name / a `BedrollTag`) so
> vanilla beds are untouched — otherwise we'd let players sleep in any 0.47-cover
> lean-to, which is a balance change, not a camp. Reading/adapting vanilla `Bed`/
> `Cover`/`Fireplace` is ADR-0001-clean (base game, not another mod).

## 5. 🟡 OPEN — knobs Daniel decides (blocks lock)

> **🟢 RESOLVED 2026-06-24 (Daniel):**
> - **Q1 Tier = Black Forest** (was open). Bear (`Bjorn`) is a Black Forest creature,
>   so bear hide is a BF material and the tent is **Black Forest-tier**. The earlier
>   Meadows-vs-Mountains worry was a from-memory error (bears aren't Mountains). Recipe
>   = Bear hide + wood/leather, BF band. (§1.3)
> - **Q2 Sleep = skip-night-only, NO spawn set** (was open). Bedroll skips the night +
>   grants comfort, but does not overwrite your respawn point. Mechanism + impl-VERIFY
>   in §3.1. (§1.4)

**Q3 — Placement tool: Spade or Hammer?** Same Pillar-1 tension as the Traveller's
Cache. The camp is *trailside* furniture (Spade-shaped), but it's also a
"deployable convenience you plant" (the Ancient Portal's Hammer exception). Lean
**Spade** (groups with the trail set). Could flip if Daniel reads it as a campsite
convenience.

**Q4 — Storm-proofing the camp fire.** Under the canopy the fire survives **rain**
but not a **high-wind storm** (§3, 0.47 < 0.7). Options: (a) accept it — storms
are a real reason you *can't* always camp; (b) give the camp fire its own tight
mini-roof so it clears 0.7; (c) make the *tent* the storm-proofer later when real
art adds side coverage. Lean (a) for the placeholder (it's honest and free).

**Q5 — One piece or three?** Is "the camp" three separately-placed pieces
(tent + bedroll + fire, player arranges them) or a single combo placement that
drops all three? Lean **three separate** (flexible, reuses each piece elsewhere,
and each is independently a beautifier contributor for `trailside-beautification`).

**Q6 — Does the bedroll relax `underRoof` too, or keep it?** The §4 lean keeps
`underRoof` required (no sleeping under open sky — you must be under *the tent*).
Confirm that's the intent vs. a fully-exposed bedroll that needs only the fire.
Lean: **keep `underRoof`** — it's what makes the *tent* load-bearing instead of
decorative.

**Q7 — Confirm the bedroll grants *Inspired* via the non-cairn-comfort hook.**
"Inspiration" is NOT a new buff to design here — `trailside-beautification.md` already
LOCKED **Inspired** (Appeal buff: timed accumulating skill-gain, `m_raiseSkill = All` +
`m_raiseSkillModifier`), explicitly *"also innately granted by non-cairn comfort."* The
camp bedroll's wake grants vanilla Rested (non-cairn comfort), so it should feed Inspired
through that existing hook. **Confirm:** (a) the bedroll counts as a "non-cairn comfort"
grant source for Inspired (lean yes — it's literally resting, not at a cairn); (b) whether
a *one-shot* skip-wake should grant a flat Inspired chunk vs. the beautification doc's
*accumulate-while-in-coverage* model (the camp is a moment, not a zone — may want a fixed
on-wake grant rather than an accumulator). This is a small reconciliation with the
beautification Inspired spec, **not** a from-scratch effect design.

## 6. Numbered open-questions (fast triage)
*(Q1 tier=Black Forest and Q2 sleep=skip-only-no-spawn RESOLVED 2026-06-24 — see §5 banner.)*
1. Placement tool — Spade (lean) or Hammer? [§5-Q3]
2. Camp-fire storm-proofing — accept rain-only (lean), own roof, or defer to art? [§5-Q4]
3. Three separate pieces (lean) or one combo placement? [§5-Q5]
4. Bedroll keeps `underRoof` required (lean) or fully exposed? [§5-Q6]
5. **Bedroll grants *Inspired* — confirm it's the beautification buff via the non-cairn-comfort hook** (not a new effect) [§5-Q7]

> **Until the open knobs settle this stays `status: living` and does NOT graduate.** When
> they lock it becomes a version-scoped impl-spec under `docs/v2/planning/` (Black Forest
> tier, Q1 resolved), adds SpecCheck manifest rows (3 pieces) + `PIECES_AND_CRAFTABLES.md`
> dataset rows, and code + spec + manifest move together (AGENTS.md). The implementing card
> goes to **engineer-systems** (the gated `Bed.CheckExposure` relax + skip-no-spawn path +
> wiring the bedroll wake into the beautification *Inspired* grant hook + 3 additive
> pieces); the real bear-hide art is a separate art swap behind the placeholder prefab names.


## 7. Scope boundaries
- **In:** the 3 additive pieces, the gated `Bed.CheckExposure` relax, the
  placeholder `TraderTent` art graft, the SpecCheck + dataset rows, and a
  ≥1-real-client in-game sleep verification ("logs-green ≠ playable": place camp at
  dusk, light fire, sleep succeeds; remove fire → `$msg_bednofire`; step into rain
  off-canopy → `$msg_bedwet`/`$msg_bedtooexposed`).
- **Out (separate cards/docs):** real bear-hide tent art; the beautification
  *aura* contribution of the tent (owned by `trailside-beautification.md`); any
  comfort/Rested interaction beyond the vanilla gates (owned by the cairn comfort
  investigation).

## 8. Related future system — partial-sleep TIME ACCELERATION (seed, NOT scoped)

> Daniel, 2026-06-24: *"explore some sort of synchronized 'accelerate the passage
> of time once some % of players are sleeping' system. Might have some challenges in
> synchronization."* Captured here as the multiplayer **generalization** of this
> doc's night-skip (§3.1); it is NOT part of the camp build and graduates to its own
> `status: idea` doc if pursued. Tracked-as-related per Daniel.

🟢 **GROUNDED feasibility (decomp) — sync is the EASY part, not the hard part.** The
worry inverts: time is already a single **server-authoritative** value and vanilla
already runs *accelerated synchronized* time, so the machinery exists.

- **Time is one server-owned scalar.** `ZNet.m_netTime` (double) is incremented
  server-side only (`ZNet.UpdateNetTime:67534`, `if (IsServer() && GetNrOfPlayers()>0)
  m_netTime += dt`) and pushed to clients via the `NetTime` RPC (`SendNetTime:67815` →
  `RPC_NetTime:67826` sets `m_netTime = time`). Clients are pure followers — **there is
  no client-side time simulation to desync.** Accelerate server-side → all clients
  follow automatically.
- **Vanilla ALREADY accelerates, it doesn't teleport.** `EnvMan.SkipToMorning:81111`
  sets `m_skipTime=true` + `m_timeSkipSpeed=(morning−now)/12.0`, then
  `UpdateTimeSkip:81091` ramps `m_netTime += dt*m_timeSkipSpeed` (server-only) until
  morning. So "variable-rate time acceleration" = one existing scalar (`m_timeSkipSpeed`).
  Vanilla's 12-second night-skip is the existence proof.

**So the build is two small server-side patches:** (1) replace the all-or-nothing
`Game.EverybodyIsTryingToSleep:84716` (counts `s_inBed` across all character ZDOs) with
a **fractional** trigger (`sleeping/total ≥ X%`); (2) hold `m_timeSkipSpeed` as a live
function of the sleeping fraction instead of the fixed 12s ramp, reverting to 1× when it
drops.

🔴 **The REAL challenge is design, not sync: time is GLOBAL, there is no local time
bubble.** Accelerating `m_netTime` fast-forwards the world for the *awake* players too
(mid-fight, sailing, smelting) against their will — torches gutter, food digests, raids
re-time. Vanilla's all-or-nothing sidesteps this (if everyone's in bed, nobody's
actively experiencing the speed-up). Going partial imposes one shared timeline on
non-consenting players, and that's engine-deep (no per-player clock). Cascades into two
more design problems: **the denominator** (who counts — dead/loading/AFK/2km-away? vanilla
never had to answer because it required *all*) and **rate jitter** (multiplier tracking a
live fraction strobes the sky as people climb in/out of bed → needs server-side
hysteresis + a floor/ceiling).

**Two framing calls before this graduates (Daniel):** (a) **acceleration cap** — gentle
(~3–5×, time *flows* faster) vs aggressive (near-instant like vanilla's 12s); (b)
**non-sleeper handling** — accelerate regardless, or gate it to when awake players are
also safe/idle. Escape hatches if pursued: higher threshold (60–70%, small dissenting
minority by construction), low multiplier cap (nudge not blast), or safe/idle gating.

