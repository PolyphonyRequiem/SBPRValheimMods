---
spec_name: trailborne-v1
shaped_at: 2026-06-03
shaper: spec-shaper (Starbright, in-session with Daniel)
status: current
progress: IN PROGRESS — Round 3 closed, Round 4 (decomp/wiki scan) next
correction_notes: |
  - Initial Round 2 questions posed mechanics as undefined; they weren't.
    design/PARKED-2026-06-03.md locked most v1 design on 2026-06-02.
  - Round 2 corrections committed in c73ba19.
  - Round 3 (this revision): Starbright failed AGAIN to read the
    existing PLAYER_GUIDE.md (20KB locked design) and design/nomap.md
    (20KB patch-surface cross-ref) before posing Round 3 questions.
    Re-baselined against ALL repo docs now. Most "Round 3 answers"
    were partially or fully already in PLAYER_GUIDE.md. Where Daniel's
    today-answers SUPERSEDE PLAYER_GUIDE.md text, the PLAYER_GUIDE
    needs a follow-up doc-PR (tracked at bottom of this file).
  - Process lesson: read EVERY *.md in repo before EVERY shaper round,
    not just stage 1. Skill patched (commit pending).
---

# Requirements: SBPR Trailborne v1

> **Working document.** Each shaper round is appended here as it happens.
> When shaper completes, this file is promoted to "final" state and handed
> to spec-writer. Until then, the latest round may be partial.

## Source idea

See `planning/initialization.md` for the verbatim raw idea + carried doctrine + concept-seed inventory.

**Critical primary-source reference:** `design/PARKED-2026-06-03.md` in this repo
already contains substantial v1 design lock-in from 2026-06-02 evening session.
This requirements.md MUST stay consistent with that document. Where this
document diverges from the parked doc, the parked doc wins unless explicitly
overridden in a numbered round below.

---

## Q&A round-by-round

### Round 1 — Scope & Purpose ✅ COMPLETE

**Q1.1 — v1 piece roster:** Propose v1 ships exactly: Explorer's Bench, Cairns, Pigments, Painted Signs, Trailblazer's Spade (single tool item, hoe/hammer tier-equivalent), **Path Lamps**.

**A1.1:** ✅ Yes — INCLUDING Path Lamps. They're a philosophy-completing piece (night-time trail illumination), not scope creep. Without them, the trail-discipline loop is complete-by-day, broken-by-night, and players will misuse vanilla torches to fill the gap. Path Lamps belong in v1.

**Explicitly OUT of Trailborne entirely (different mod, different family):**
- **Guardian Stones (active OR inert)** — server worldbuilding artifact, separate mod (`SBPR.Wardens` or similar), gated on `valheim-regions` macro-boundary work. Stripped from Trailborne scope entirely.

---

**Q1.2 — Map-nerf scope for v1:** *CORRECTED from initial pass.* v1 DOES nerf the Cartography Table — existing in-world Cartography Tables lose functionality; new ones cannot be built.

**Map situation for v1 (locked):**
- `nomap=ON` (server setting) → **no map at all** (vanilla nomap mode)
- `nomap=OFF` (default) → **minimap ONLY**, freely rotating, **no north indicator**, **no M-key map**
- Cartography Table: disabled functionality if pre-existing; cannot be built

**A1.2:** ✅ Locked per parked doc.

---

**Q1.3 — Server-gated vs always-on:** Remove `SBPRContext.OnSBServer` gate for Trailborne. Always-on, configurable via BepInEx config.

**A1.3:** ✅ Agreed for now.

---

### 🆕 DOCTRINE REFINEMENT from Round 1 (added by Daniel, captured for spec-writer):

**"Leverage Unity indirectly, not directly."** This refines fact #112. What we CAN do at runtime:
- Compose vanilla Unity prefabs via Harmony + reflection
- Instantiate vanilla `ParticleSystem` instances for visual effects
- Reflect vanilla materials onto vanilla meshes with runtime tinting
- Reuse vanilla sprites for menu icons where available
- Load PNG icons via `File.ReadAllBytes` → `Texture2D.LoadImage` → `Sprite.Create`

What we will NOT do (in v1):
- Open the Unity Editor
- Bake `.unity3d` assetbundles
- Author custom meshes, materials, ParticleSystems, or animations in Unity

**v1 visual approach: kitbash prototype assets where possible for playtesting.** Composite existing vanilla prefabs/materials/particles into Trailborne pieces. Visual polish (custom materials, custom icons that aren't kitbashed) is a v1.1+ concern. Goal is *playtest-quality mechanics* in v1, not *ship-quality art*.

**Reserved exception for v∞:** when **Locations** become a thing, Daniel reserves the right to revisit this doctrine — Locations need baked scene hierarchies that can't be assembled at runtime. NOT a v1 problem.

---

### Round 2 — Mechanics ✅ COMPLETE (corrected against design/PARKED-2026-06-03.md)

#### A2.1 — Cairn mechanics (LOCKED)

| Aspect | Locked value |
|---|---|
| **Activation** | Always-on once built (no fuel) |
| **Comfort tier ladder** | 5-tier: stones can stack to give comfort floors of **3 / 4 / 5 / 6 / 7** |
| **Comfort interaction with vanilla** | `max()` clamp — cairn never *reduces* effective comfort, only raises floor |
| **Implementation surface** | Patch `SE_Rested.CalculateComfortLevel` directly (cairn is NOT in vanilla `ComfortGroup` enum so it bypasses vanilla's same-group dedup) |
| **Decay** | ⚠️ **MANDATORY decay** — cairns ARE destructible. Downgrade @25% HP, collapse @0%. Cairns are *evidence of a trail still being walked* — abandonment = collapse. Re-correction: I (Starbright) proposed indestructible in error this morning; Daniel snapped me back. Decay is the design's *core thesis-in-a-piece*. |
| **Repair** | Flat **3 stone + 1 resin** regardless of damage level |
| **Pigment / banner persistence** | Persist across rebuilds — applied colors survive damage + repair cycles |
| **Downgrade re-ignite of resin** | OPEN — Daniel: "lean deliberate-only" (i.e. requires explicit player action, not auto-re-ignite on repair) |
| **Visual (LOCKED 2026-06-05, Daniel)** | Rich procedural stone pile — see **§A2.1b — Cairn visual** below. Per-tier haphazard stack of vertically-squashed / horizontally-flattened stones (count = the stone ladder), **deliberately constructed from the bare `Pickable_Stone` mesh+material — NOT runtime prefab clones (REVISED 2026-06-07)**, on a fire-neutralized `bonfire` structural base, with an HP-gated **wear-state ember** at the top that fizzles out below ~75% HP. |
| **Build cost** | Per-tier stone ladder (cumulative): T1=9 / T2=12 / T3=15 / T4=18 / T5=21 Stone, +1 Resin, +1 Cairn Marker at T1 (see §A3.1). |
| **Placement elevation gate (LOCKED 2026-06-08, Daniel)** | A cairn **cannot be placed below 2 m above sea level**, measured at the piece's placement origin (ground-contact point). Sea level is Valheim's global ocean plane read live from `ZoneSystem.instance.m_waterLevel` (default **30**; compile-time anchor `ZoneSystem.c_WaterLevel = 30f`) — NOT hardcoded. Valid iff `placementPoint.y ≥ seaLevel + 2` (≥ y32 at default sea level); below is rejected. Keeps cairns out of the waterline / shallows — they are trail markers, not buoys. **Cairn-only** — signs / Path Lamps / Spade path ops are unaffected (widening to other trail pieces is an OPEN question for Daniel, not assumed). Implemented as a Harmony **postfix on `Player.UpdatePlacementGhost`**: for a cairn ghost vanilla rated `Valid` but sitting too low, force `m_placementStatus = Invalid` (blocks the place + shows `$msg_invalidplacement`) and redden the preview via the public `Piece.SetInvalidPlacementHeightlight(true)`. Daniel, 2026-06-08 (v0.2.9 playtest): "cairns should not be able to be placed at elevations under 2 m from sea level." |

#### A2.1b — Cairn visual (LOCKED 2026-06-05, Daniel)

> Supersedes the earlier "procedural `rock_low` stack capped with a rune-glow particle" sketch AND extends the 2026-06-05 bonfire-neutralization fix (PR #23 / card t_9f8341c9). PR #23 stripped ALL fire off the cairn (correct — a cairn is not a campfire); this spec keeps that neutralized base and adds back a **small, deliberate, HP-gated** ember as a *wear indicator*, not a light source. The two do not conflict: neutralization runs first and unconditionally; the ember is a separate opt-in element layered on top only at high HP.

The cairn should read as a **real, hand-piled trail cairn** — not a tidy cone. Three parts:

| Aspect | Locked value |
|---|---|
| **Structural base** | Clone of vanilla `bonfire` for its `WearNTear` / `Piece` / `ZNetView` only; the donor fire is fully neutralized on the client (Fireplace destroyed; Light/LightFlicker/LightLod, ParticleSystem/SmokeSpawner, EffectArea, AudioSource/ZSFX, donor mesh Renderers all disabled — the PR #23 path, kept as-is). |
| **Stone pile** | A **haphazard** pile of stones assembled at runtime by **deliberate construction (REVISED 2026-06-07, Daniel — was runtime prefab-cloning)**. Each stone is a **hand-built GameObject carrying ONLY `Transform` + `MeshFilter` + `MeshRenderer`** — the bare stone **mesh + material are read off the vanilla `Pickable_Stone` donor (mesh `Box02`, ~0.32×0.19×0.65 m) without ever instantiating that networked prefab** (so no `ZNetView`, no `Pickable`, no `StaticPhysics`, no ZDO, no collider rides along). **Why the pivot:** the old path `Instantiate`d `Pickable_Stone` (a ZNetView-bearing prefab) onto the active cairn inside vanilla's init-ZDO window and then `DestroyImmediate`'d the ZNetView, orphaning null-ZDO entries in `ZNetScene.m_instances` that vanilla `RemoveObjects` dereferenced every frame → client soft-lock (×21 "Double ZNetView" → repeating NRE). Constructing from bare mesh removes the crash mechanism by construction. **Count scales with tier and equals the stone ladder: T1=9, T2=12, T3=15, T4=18, T5=21 stones.** Each stone renders at **NATIVE mesh proportions — NO per-stone scaling or squash (REVISED 2026-06-08, Daniel — was vertically-squashed/horizontally-stretched)**. The squashed-disc look read as flat coins in-game; native `Box02` rocks read as real piled stones. Placement is **deterministically randomized** (seed = ZDO id, so it survives reload + is identical on every client): jittered position, random yaw, slight random tilt — a believable irregular pile, wider at the base, tapering up, built from POSITION + ROTATION variation alone. |
| **Color identity — BANNER (REVISED 2026-06-08, Daniel — replaces stone pigment tint)** | The cairn's bound color is carried by a **wind-responsive banner**, NOT by tinting the stones (stones stay natural grey). One **cloth element** is read off a vanilla banner donor (cloth mesh + `Banner_Border_*_mat` material ONLY — never the `woodbeam` pole) and hand-built as a bare `Transform`+`MeshFilter`+`MeshRenderer` GameObject (additive, ADR-0006 — no `ZNetView`/`Piece`/`Cloth` component). The cloth's **wave is shader-driven** (the banner material's vertex shader reads the global wind vector), so waving is free with no physics. Color→donor (LOCKED 2026-06-08): **black→`piece_banner01`, blue→`piece_banner02`, red→`piece_banner04`, white→`piece_banner11`**. *Eyeball note:* the donor secondary tones (02 = blue/yellow, 11 = black/white-inverted) are carried as-is pending in-game review. Banner persists across rebuilds (re-applied from ZDO color on every `BuildKitbashArt`). |
| **Wear-state ember (NEW)** | A **small** flame/ember at the **top** of the pile, present **only while HP ≥ ~75%** (pristine). It **fizzles out below ~75%** and stays out until repaired back to pristine. This is the ONLY fire on the cairn — small and decorative, NOT the donor bonfire's blaze, NOT a light/heat source (no `EffectArea`, no comfort contribution — comfort is the `SE_Rested` patch). Implement as a **small dedicated particle/light element toggled by HP**, layered on the neutralized base — do NOT re-enable the donor `Fireplace`. |
| **Wear states (visual ladder)** | ≥75% HP = pristine (ember lit). <75% = fizzled (ember out; per §A2.1 this is also the repair-eligible threshold). <25% = downgrade one tier (rebuild the pile at the lower stone count). 0% = collapse (piece destroyed, leaves a small rubble remnant). The ember tracks the pristine/fizzled line; the stone count tracks the tier. |
| **Determinism** | Pile layout + ember presence must be a pure function of (ZDO id, tier, current HP bracket) so all clients and post-reload spawns agree. No per-frame RNG. |

⚠️ **Open technical questions for the engineer (investigate, don't guess):**
1. Best vanilla source for the **small ember** — a stripped-down particle from `fire_pit`/`bonfire` re-parented to the pile top, vs. a minimal custom `ParticleSystem`. Whichever, it must be cheap and not re-introduce heat/light/SFX.
2. The HP→visual hook — postfix `WearNTear.OnDamage` / a health-bracket check on the `CairnTag` to toggle the ember and rebuild the pile on tier change. Confirm the bracket fires on repair *up* as well as damage *down*.
3. Flatten ratios + jitter ranges that look "haphazard but stable" — tune against a joined client; bake the chosen constants into the spec on sign-off.

**Engineering resolutions (card t_f3761d28, build-verified 2026-06-05 — pending in-game sign-off):**
1. **Ember source → minimal custom `ParticleSystem`.** A hand-built GameObject carrying ONLY `Transform` + `ParticleSystem` + `ParticleSystemRenderer` (so by construction no `Light`/`EffectArea`/`AudioSource`/`SmokeSpawner` and it never touches the donor `Fireplace`). It borrows only the *material reference* off the vanilla `fire_pit` particle renderer so it renders without shader-guessing — the donor prefab is never instantiated, so no heat/light/SFX rides along. Re-parenting a real donor sub-particle was rejected: it risks dragging back the exact components PR #23 neutralized. Tiny budget (≤14 particles, rate 10/s). Parented under the kitbash root so the neutralization sweep (which excludes the kitbash) never strips it and a tier rebuild recycles it.
2. **HP→visual hook → 1 Hz health-bracket poll on `CairnTag` (`InvokeRepeating`), NOT a `WearNTear.OnDamage` postfix.** The poll is *path-independent*: it reconciles the ember on repair-UP, debug-damage-DOWN, out-of-zone backfill, and natural weather decay alike — sidestepping the open risk that a damage-only vanilla hook would miss the repair-up relight. The repair path additionally calls `RefreshEmber()` for an instant (no up-to-1 s wait) relight on the player's own action. The same poll performs the §A3.5 auto-downgrade (HP <25% & tier >1 → drop one tier + `Repair()` to 100% of the new tier; tier 1 falls through to 0% collapse). Owner-gated.
3. **Flatten/jitter constants (proposed — eyeball on a joined client, then bake here):** stones squashed to vertical/horizontal ratio **0.16–0.30**; overall size **0.55–0.95**; base disk radius **0.42 m (T1) + 0.06 m/tier**, pile height **0.34 m (T1) + 0.12 m/tier**; height exponent **1.6** (base-weighted → wider base, tapering up); top taper **0.78**; ±**12°** tilt; ±**0.06 m** lateral / ±**0.04 m** vertical jitter. Constants live in `CairnTag` as named `private const`s (single source of truth) until sign-off.

#### A2.2 — Trailblazer's Spade (LOCKED — single item, NOT options)

**Single tool item.** Hoe/hammer tier-equivalent. Its own slot in the player's inventory, its own keybind, its own selection wheel.

| Capability | Detail |
|---|---|
| **Path widths** | **1.5m / 3m / 5m** — three selectable widths (analogous to hoe's flatten radii) |
| **Path stamina** | **Flat 2 stamina per path/replant op, INDEPENDENT of width** (1.5m / 3m / 5m all cost 2). See A3.9. |
| **Replant Grass** | **Three grass-restore widths (1.5m / 3m / 5m)** that mirror the vanilla **Cultivator's replant ("Grass") mode** — restore/seed grass over the selected footprint, with **NO terrain raise/level/cultivate at ANY width**. The three widths mirror the path widths for a consistent three-width UX (Daniel, playtest 2026-06-05). Each width scales **only the grass/paint footprint** (`m_paintRadius`); `m_levelRadius`/`m_smoothRadius` are left at the vanilla op's stock values so no width can flatten or smooth terrain. ⚠️ **Correction (Daniel, playtest 2026-06-04) — still in force:** this is NOT the `cultivate` (soil-tiller) op. An earlier build cloned `cultivate` at a forced 5m radius — an "UBER level" that flattened/cultivated a huge area. PR #16 fixed it to clone `replant`; this slice extends that fix to three grass-restore widths WITHOUT reintroducing terrain modification (the level/smooth radii are never written). |
| **ClearVegetation** | Removes existing vegetation along the laid path (small brush, grass, mushrooms — NOT trees) so the path is *visually a path*, not a stripe through bushes. **Deferred to v0.2.0** (see playtest limitations). |
| **Implementation surface** | Likely a new item class analogous to `Hoe`, with custom `m_operations` array entries for the three widths + cultivate-replant + clear-vegetation. May patch `Hoe` directly OR introduce a new `TrailblazerTool` MonoBehaviour. Decision for spec-writer. |

#### A2.3 — Explorer's Bench (LOCKED — kitbash for playtest)

**v1 approach:** kitbash the vanilla Workbench. Tier 1 reuse — vanilla Workbench mesh + Trailborne material tint + visual props (half-rolled hide-map + bone-needle-in-stone-disk per `design/nomap.md` §1 + antlers from Deer Trophy visually integrated into the bench mesh). Trailborne recipes register as new tabs on the Explorer's Bench (its own CraftingStation, NOT the vanilla Workbench). **Its CraftingStation must set `m_showBasicRecipies = false`** — the vanilla Workbench is the only station that ships this `true`, and it's what surfaces the stationless "basic" hand-craft recipes (Club, Torch, Stone Axe, Hammer, Hoe, …); a raw clone inherits `true` and wrongly offers all of them (bugfix 2026-06-04, card t_30f97042).

**v1.1+ path:** graduate to a visually-distinct mesh once mechanics validate. Retains thematic anchor (own recipe, own discovery moment).

**Recipe (LOCKED, Daniel 2026-06-03):** 10 Wood + 4 Stone + 1 Deer Trophy. No raspberries, no resin. See the dedicated Explorer's Bench section below for full detail.

#### A2.4 — Path Lamps (LOCKED, added in this round per Daniel)

**Recipe:** Wood + Resin (per parked doc; exact quantities TBD).

| Mechanic | Detail |
|---|---|
| **Light source** | Passive — like vanilla torch but slightly **dimmer** (trail-illumination, not base-illumination) |
| **Fuel duration** | **Longer** than vanilla torch (so a string of them doesn't become a refuel chore — *evidence-of-trail* shape rather than *maintenance burden*) |
| **Chain ignition** | Walking close to a lit Path Lamp with an unlit Path Lamp in proximity should light the unlit one (gives the satisfying "lighting the path home" moment without manual torch-by-torch interaction) — OPEN: Daniel to confirm |
| **Implementation surface** | Likely Tier 1 reuse — vanilla `Torch` prefab + custom light intensity + custom fuel rate + custom recipe. Chain-ignition would require a small `MonoBehaviour` on the lamp that polls for nearby lit lamps. |
| **Visual (playtest)** | Kitbash: vanilla torch model + dimmer light intensity reflection + (optional) pigment-tinted flame via runtime ParticleSystem property edit |

#### A2.5 — Pigments (LOCKED per parked doc)

| Aspect | Locked value |
|---|---|
| **Colors** | Red, White, Black, Blue (4 basic pigments) |
| **Display names** | `Red Pigment` / `White Pigment` / `Black Pigment` / `Blue Pigment` (canonical "Pigment" naming — unified 2026-06-07) |
| **Prefab names** | `SBPR_InkRed` / `SBPR_InkWhite` / `SBPR_InkBlue` / `SBPR_InkBlack` — **unchanged save/wire contract** (placed signs/cairns store these); only display + code identifiers say "Pigment" |
| **Output per craft** | 2 pigments per craft |
| **Stack size** | 20 |
| **Weight** | 0.1 |
| **Recipe inputs** | Red ← 1 Raspberry, White ← 1 BoneFragments, Blue ← 1 Blueberries, Black ← 1 Coal |
| **Craft station** | Explorer's Bench (v1 = vanilla Workbench kitbash) |

#### A2.6 — Painted Signs (LOCKED — single combined Paint+Text panel, two-tone, Daniel 2026-06-05)

> **SUPERSEDES the 2026-06-04 "single-color, apply-ink-item, no UI" lock**, which
> itself superseded the older "E=text color / Shift+E=accent / two-tone pin" model.
> Daniel re-locked this on 2026-06-05 from a UI mockup. Two deliberate reversals of
> the 6/04 lock: **(1) a real UI panel returns** (replaces apply-ink-item), and
> **(2) two-tone returns** — a sign now carries a **text color AND a border color**.
> Still ONE buildable sign piece (the four-variant sprawl stays dropped).

A single panel handles **both** painting and text, opened by interacting with a
placed sign (replaces the vanilla text dialog). Layout (from Daniel's mockup):

```
--- PAINTING ---
 Set Text Color:  [∅ None][Red][Blue][Black][White]   (only DISCOVERED pigments render)
 Border Color:    [∅ None][Red][Blue][Black][White]   (∅ None = explicit clear)
 Cost:            <icon> Red Pigment    1/1
                  <icon> White Pigment  0/1   (red while short)
 { Paint this and consume }
--- TEXT ---
 [ text field ]   (enabled only once a paint color is chosen)
 { Update Text }   { Close }
```

| Aspect | Locked value |
|---|---|
| **Base** | ONE buildable piece (`piece_sbpr_sign`), variant of the vanilla wood sign. Placed **UNPAINTED** (plain wood). Build cost **2 Wood** (pigment is NOT a build ingredient) |
| **Panel** | Interacting with a placed sign opens the **combined Painted Sign panel** (custom uGUI built on Unity **layout groups**), NOT the vanilla text dialog. Two sections: PAINTING + TEXT. Rebuilt each open so swatch rows reflect current discovery |
| **Set Text Color** | Swatch row — an explicit **`∅ None`** tile (clears the slot) followed by one swatch per **DISCOVERED** pigment (discovery = *ever-discovered material OR known recipe OR owned*; primary signal `IsKnownMaterial`, persistent so swatches don't flicker on last-unit spend). Undiscovered pigments are **NOT rendered** (no dead/unclickable reserved boxes). Sets the **board/text** tint — which colours BOTH the board mesh AND the written letters (`Sign.m_textWidget`) |
| **Border Color** | Second swatch row, same `∅ None` + discovered-only swatches. Sets a **separate border** tint (two-tone). `None` removes the border color |
| **Cost** | **Crafting-style requirement rows** (replicates `InventoryGui`'s recipe-requirement idiom): per pigment an **icon + pigment name + `have/need` count**, the count flashing **red while short**. One pigment per filled color slot (text Red + border White → 1 Red + 1 White Pigment). Same color in both slots = **2 of that pigment**. Border is **optional** (text-only paint = 1 pigment); **at least one** color required |
| **`{ Paint this and consume }`** | Commits painting: removes exactly the displayed pigments from inventory, tints board=text color + sign letters=text color + border element=border color, writes both to ZDO. **Disabled** unless the player holds the required pigments. **Re-painting later re-consumes** |
| **`{ Update Text }`** | Commits the text. **Free** (no pigment cost — Cost applies to PAINTING only). Text field is **locked until ≥1 paint color is chosen** |
| **Camera** | While the panel is open, **mouse-look is frozen** and the cursor is released, matching every vanilla full-screen GUI. Achieved by routing through vanilla's own suppression gate (`PlayerController.TakeInput` → false while open — the same gate the vanilla sign dialog used, which our panel bypasses by replacing that dialog), NOT by overriding `GameCamera.UpdateCamera` |
| **Color persistence** | Per-instance ZDO: `SBPR_SignTextColor`, `SBPR_SignBorderColor` (`""` = unset) + vanilla text. Persists across reloads, syncs to clients, both tints (board + text widget + border) re-applied on spawn (mirrors `CairnTag`) |
| **Naming** | The items are **Pigments** — display names `Red/White/Blue/Black Pigment`, code identifiers `Pigments.Pigment*Name` / `Signs.PigmentForColor`. The prefab-name VALUES stay `SBPR_Ink*` (save/wire contract — renaming would orphan placed signs/cairns); only player- and code-facing strings say "Pigment" |
| **Pin (deferred)** | Minimap pin path stays **unregistered** for v0.1.0 (follow-up). If later registered, the pin reflects the **text** color when `nomap=OFF`; no-op if `nomap=ON` |
| **Implementation surface** | Custom uGUI panel (`SignPaintPanel`) replacing the vanilla `Sign` text dialog (`SignInteractPatch` intercepts `Sign.Interact`). Backend `SignPaintBackend` drives economy + commit; `SignTag` owns ZDO + re-tint on spawn; `Signs.TintBoard`/`TintText`/`TintBorder` (+ `RestoreBoard`/`RestoreBorder` for the None affordances) do the visuals; `SignTintBackup` snapshots original materials so None reverts live. Two-tone border = kitbashed `SBPR_SignBorder` element (separate material). Owner-write via `ZNetView` (mirrors `CairnTag`). **CLIENT-SIDE surface — cannot be proven headless.** |

> **Rebuild note (2026-06-07, Daniel playtest with screenshots):** the original
> hand-built panel (raw `UnityEngine.UI` primitives + hand-computed `y -=` offsets)
> shipped with defects — 6 dead reserved swatches that couldn't be clicked, an
> invisible "remove border" affordance, no "remove text color", text color that
> never reached the letters (`TintBoard` only tinted the plank, never
> `Sign.m_textWidget`), a custom "icon ×N" cost row instead of the crafting idiom,
> the camera not locking while the panel was open, and inconsistent alignment. This
> revision rebuilds the panel on Unity **layout groups**, renders **only discovered
> pigments** plus an **explicit `None`** on both rows, drives the **TMP text widget**
> from the text color, replicates the **crafting-UI cost rows**, and **locks the
> camera** through vanilla's own input gate. "Pigment" naming is unified across the
> UI and code. ONE buildable sign piece, two-tone, unchanged.
>
> **RESOLVED (Daniel 2026-06-07) — "discovered" definition.** A pigment swatch renders
> only when the pigment is discovered, defined as: **ever-discovered material OR known
> recipe OR currently owned** — `Player.IsKnownMaterial(name) || Player.IsRecipeKnown(name)
> || CountPigment > 0`. The PRIMARY signal is `IsKnownMaterial` (vanilla's persistent
> material-discovery set, populated by `AddKnownItem` on first pickup and never cleared),
> so a swatch does NOT flicker away when the player spends their last unit. Recipe-known
> and owned are belt-and-braces fallbacks. Note SBPR pigments set
> `m_shared.m_name = displayName`, so the display name is the correct key for both
> vanilla knowledge sets. Code: `SignPaintBackend.IsPigmentDiscovered`.

---

### Round 3 — Open mechanical questions ✅ CLOSED (Daniel's answers + repo + log re-check)

**Re-baseline note (corrected 2nd pass):** Two parallel sources of truth need cross-checking, NOT just the repo:
1. **Committed repo docs** (`PLAYER_GUIDE.md`, `design/*.md`, `README.md`) — but young, may lag behind chat decisions
2. **Recent chat decisions** (this Discord conversation, especially the prior session that established Trailborne naming, Explorer's Bench rename, and other refinements) — authoritative when they supersede repo docs, but only durable if captured to disk

The crafting station **was renamed from "Orienteering Table" → "Explorer's Bench"** in last night's Discord conversation (confirmed via session DB at id 37430 vicinity). The rename never propagated into PLAYER_GUIDE.md or design/nomap.md. When Starbright re-read those files this morning, she reverted to the older repo name. **Explorer's Bench is correct.** PLAYER_GUIDE.md + design/nomap.md need a doc-PR for the rename.

Skill lesson (already patched): cross-check repo docs AND recent chat decisions; capture chat-decisions to disk same-day or they rot.

#### A3.1 — Cairn build cost ✅ LOCKED (Daniel today)

Daniel: "the build cost for a cairn is 3 stone, 1 resin and one pre-made cairn marker. upgrade cost is always 3s + 1r"

**Cairn recipe (v1, locked — 2026-06-04 ladder update):**
- Initial build (Tier 1): **9 Stone + 1 Resin + 1 Cairn Marker (pre-crafted item)**
- Per-tier stone build cost (cumulative ladder, flat +3 per tier): **T1=9 / T2=12 / T3=15 / T4=18 / T5=21**
- Comfort floor per tier: **T1=3 / T2=4 / T3=5 / T4=6 / T5=7**
- Upgrade / repair gesture (combo E-press): **3 Stone + 1 Resin** flat per use, gated on HP <75%. Always repairs to max; if tier<5, simultaneously upgrades to tier+1. One-press, outcome state-dependent.
- **Damage immunity (LOCKED):** cairns are immune to player + monster damage (Harmony-prefix on `WearNTear.Damage`). Only weather/time decay ticks affect HP. Combat cannot grief cairns; only abandonment can.
- **Out-of-zone decay (LOCKED):** ZDO-persisted `SBPR_LastWearTick` (in-game day-time) + Harmony-postfix on `WearNTear.Awake` backfills missed wear ticks at vanilla rate when a chunk reloads after being unloaded. Tested rate v0.1.0: ~10 HP/day. Tunable v0.2.0.
- **Shift+E debug-damage (v0.1.0 only):** `SBPR_DebugCairnDamage` BepInEx config (default `true`). With a pristine cairn (≥75% HP), Shift+E drops it to ~70% so the combo gesture is exercisable without waiting on weather. Flip false or remove in v0.2.0 once natural decay is tuned.
- Repair: **3 Stone + 1 Resin** (flat, matches upgrade — confirmed from PARKED doc)
- **Visual identity (REVISED 2026-06-07 — the prior "non-burning" lock was wrong, Daniel):** a Cairn shows a **cosmetic fire** at pristine — **flame VFX + fire SFX + a small Light (intensity/range clearly BELOW a vanilla torch)** — but the fire grants **NO heat and consumes NO fuel**. It is cloned from the vanilla `bonfire` prefab; on the client the donor fire is CONFIGURED into a cosmetic fire by component type: `Fireplace` is KEPT but forced `m_infiniteFuel=true` with fuel knobs zeroed (eternal, fuel-less, no 'add fuel' hover); the flame `ParticleSystem`(s) + fire `AudioSource`/`ZSFX` are KEPT (the flame + crackle); ONE `Light` is kept and dimmed below a torch; `EffectArea` is DISABLED (no heat); `SmokeSpawner` is DISABLED. The donor mesh logs are hidden and a runtime stack of `Pickable_Stone` clones (T1=9 → T5=21 stones) shows instead. The cosmetic fire is HP-gated: **lit at ≥75% HP (pristine), OUT below** — so "fizzled" reads as the fire going out, and repair-to-pristine relights it. Comfort comes from the `SE_Rested` patch, NOT from fire. See `Features/Cairns/CairnTag.cs` (`ConfigureCosmeticFire` / `ReconcileFire`).

**New item introduced: Cairn Marker.** This is a pre-crafted consumable item (not a piece) used as the build ingredient for the base cairn. Recipe TBD — needs a Round 3.5 question. Likely crafted at Explorer's Bench. Thematic: the "marker" is what you carry out to plant a new cairn somewhere, after which you stack stones around it on-site (the cairn is built around a planted marker, not from raw stones alone).

#### A3.2 — Blue pigment Meadows-availability ✅ LOCKED

Daniel: "no, blueberries it is. V1"

**Pigment recipes (v1, locked):**
- Red: 1 raspberry → 2 red pigment
- White: 1 bone fragment → 2 white pigment
- Black: 1 coal → 2 black pigment
- Blue: 1 blueberry → 2 blue pigment

v1 effectively spans Meadows through early Black Forest for pigment ladder. Yellow (cloudberry, Plains) is v5+, not v1.

#### A3.3 — Path Lamp chain-ignition ✅ DROPPED

Daniel: "this isn't really a thing we discussed"

Starbright-hallucinated mechanic, removed. v1 Path Lamps: manual ignition, no chain effect.

#### A3.4 — Trailblazer's Spade recipe ✅ LOCKED

Daniel: "Leather Hides not scraps. Flint, not stone. So 5w/2f/2h"

**Trailblazer's Spade recipe (v1, locked):** 5 Wood + 2 Flint + 2 Leather Hides
**Crafted at:** Explorer's Bench

#### A3.5 — Cairn resin re-ignite on repair ✅ LOCKED

Daniel: "it reignites if the cairn is in the 'pristine' piece state rather than the lower tiers of wear and tear. 75% threshold as discussed to 'fizzle out'"

**Cairn resin glow mechanic (v1, locked):**
- **≥75% HP** = pristine, resin glows (visual)
- **<75% HP** = fizzled, no glow (visual maintenance signal)
- **<25% HP** = downgrade tier (per PARKED-2026-06-03.md)
- **0% HP** = collapse (per PARKED-2026-06-03.md)
- Re-ignite: AUTOMATIC when HP returns to ≥75% via repair. No player action required.
- Implementation: postfix `WearNTear.OnDamage`/`OnRepair` to toggle `ParticleSystem.emission.enabled` based on HP threshold.

#### A3.7 — Path Lamps wood material ✅ LOCKED

Daniel: "I think corewood still tracks"

**Path Lamps recipe (v1, locked — 2026-06-04 update):** **3 Wood + 2 Resin** (downshifted from Corewood to plain Wood per Daniel's morning playtest pass — Meadows-tier accessibility wins over the Black Forest gate; visual remains a slim post topped with a resin-fueled flame).
- Path Lamps are now squarely Meadows-tier — no Black Forest material gate. Pure trail discipline, available the moment a player has a workbench.
- Consistent with PLAYER_GUIDE.md line 110: "3m corewood torches, resin-fueled, long burn"

#### A3.8 — Ember Lamps in v1 ✅ DROPPED FROM v1

Daniel: "No"

**Decision:** Ember Lamps are NOT in v1. They move to v1.1 (or a later release). Keeps v1 scope tight on the Path Lamps tier; Ember Lamps + Beacons come together later.

#### A3.9 — Spade path stamina is flat 2, radius-independent ✅ LOCKED

Daniel (2026-06-04 playtest): "Pathing is supposed to only be 2 stamina with the spade regardless of size."

**Spade path/replant stamina (v1, locked):** **Flat 2 stamina per op for ALL widths** — 1.5m, 3m, and 5m each drain exactly 2. Stamina does NOT scale with radius.

- **Why the tool, not the op piece:** terrain-op / build stamina is driven by the *wielding tool*, not the placed op. `Player.GetBuildStamina()` returns the right-hand `ItemDrop`'s `m_shared.m_attack.m_attackStamina`. `Piece` / `TerrainModifier` carry no stamina field, so per-variant pinning is impossible; the only correct layer is the spade item itself. (Cross-ref `design/nomap.md` §2, which already recommended setting `m_attackStamina` on the tool.)
- **Implementation:** `Trailblazing.RegisterSpadeItemPrefab` sets `m_shared.m_attack.m_attackStamina = 2f` (and `m_secondaryAttack` to match) on the cloned spade. Because the spade's `SharedData`/`Attack` are `[Serializable]` and deep-copied by `Instantiate`, this does not mutate the vanilla Hoe.
- Radius-independence is structural: a single scalar on the tool cannot vary by op width.

---

### EXPLORER'S BENCH (LOCKED)

| Aspect | Value |
|---|---|
| Name | **Explorer's Bench** |
| Function | Crafting hub for all Trailborne pieces + Trailborne items (Trailblazer's Spade, Cairn Markers, Pigments, Painted Signs, Path Lamps) |
| Piece category | `PieceCategory.Crafting` |
| v1 implementation | Kitbash vanilla Workbench. Tier 1 reuse — vanilla Workbench mesh + Trailborne material tint + **antlers from the Deer Trophy visually integrated into the bench art itself** (NOT mounted on top as a trophy decoration — the antler shapes are part of the bench's structure: carved cups, leg supports, pen-holders, etc.; final composition deferred to visual-design stage) + half-rolled hide-map and bone needle stuck in a stone disk (per `design/nomap.md` §1 prop hint) |
| v1 recipe (LOCKED, Daniel 2026-06-03) | **10 Wood + 4 Stone + 1 Deer Trophy.** No raspberries. No resin. No bone fragments. No greydwarf eyes. No deer hide. (Earlier brainstorms in `design/nomap.md` §1 and prose in `PLAYER_GUIDE.md` lines 58-60 implied other ingredients; this recipe supersedes them and both docs have been updated to match.) |
| Patch surface | Pure prefab work. Clone `piece_workbench` → name `SBPR_ExplorersBench`. Add `CraftingStation` component with `m_name = "$sbpr_piece_explorers_bench"` and **`m_showBasicRecipies = false`** (the Workbench is the only vanilla station that ships this `true`; it's what surfaces the stationless basic hand-craft recipes — Club, Torch, Stone Axe, Hammer, Hoe — so a raw clone wrongly offers them; bugfix t_30f97042). Visual integration of antler shapes into the bench mesh is a kitbash / material composition task — NOT attaching the vanilla `TrophyDeer` prefab as a child. The antlers should *be part of the bench*, not sit *on* the bench. **After cloning, also strip the inherited `GuidePoint` component** — the vanilla Workbench prefab carries one (the proximity hook that makes Hugin pop the "you built a workbench" tutorial); the clone inherits it and Hugin wrongly greets the Explorer's Bench as a Workbench. The bench is its own station, so it must carry no Workbench tutorial hook (bugfix 2026-06-04, card t_53ab3232). |
| v1.1+ path | Graduate to visually-distinct mesh once mechanics validate. |

---

### CAIRN MARKER (LOCKED — pre-crafted item, gates Cairn construction)

| Aspect | Value |
|---|---|
| Name | Cairn Marker |
| Type | `ItemDrop` (consumable item, used as build-ingredient for Cairn pieces) |
| Recipe (Daniel today) | **2 Leather Scraps + 1 Finewood + 1 Pigment (player's color choice)** |
| Crafted at | Explorer's Bench |
| Function | Required ingredient for Cairn initial-build (1 Cairn Marker + 3 Stone + 1 Resin → Tier 1 Cairn). Consumed on placement. |
| Color-binding | The Pigment color used to craft the Marker IS the color the placed Cairn takes. The marker is what carries the cairn's color/banner identity from craft-time to plant-time. *Pigment+banner persist across rebuilds* (per PARKED-2026-06-03.md) implies the Cairn ZDO remembers its initial-marker color even after collapse/rebuild. |
| Thematic | The "marker" is the trail-claiming artifact you carry out into the wilderness. Stones-around-a-planted-marker is the cairn assembly mental model — you don't build a cairn from raw stones alone, you build it *around something you brought*. |
| Stack size | TBD — likely 10 (matches similar consumables like Surtling Core / Greydwarf Eye stacking shape) |
| Weight | TBD — likely 0.5 |
| Patch surface | None — pure ObjectDB registration. Recipe registers via standard `Recipe` ScriptableObject pattern. Cairn `Piece.m_resources` declares 1 `ItemDrop.ItemData` of type `Item_CairnMarker` as a required ingredient. |

---

### Round 4 — Reusability scan against decomp + wiki (NEXT)

Leveraging `design/nomap.md` line-references (Minimap, Hammer/Hoe, Sign, Fireplace, TeleportWorld, ZoneSystem, ObjectDB already mapped). Additional scans needed:
- `WearNTear` (cairn resin glow + decay)
- `SE_Rested.CalculateComfortLevel` (cairn comfort patch)
- `MapTable` (v1 disable mechanism)
- Wiki: Raspberries, Bone fragments, Coal, Resin, Blueberries (pigment input biome confirmation)
- Wiki: Banner (cairn comfort comparison)
- Wiki: Cartography Table (disable surface)
- Wiki: Torch (Path Lamp Tier 1 reuse pattern + fuel mechanics)

---

### Round 5 — Visual assets *(NOT YET ASKED)*

### Round 6 — Scope boundaries / out-of-scope *(NOT YET ASKED)*

---

### Round 3.5 — Single remaining open question

**Q3.9 — Cairn Marker recipe:** Daniel introduced "Cairn Marker" as a pre-crafted item required to build a cairn (3 stone + 1 resin + 1 cairn marker). What goes into a Cairn Marker? My instinct: thematic ingredients that make it feel like a "trail-claiming artifact" — maybe 1 Stone + 1 Resin + 1 Pigment (your-color choice), so the cairn's color is established at marker-craft time and the planted-marker is what carries the color into the cairn. But: this is your call, not a Starbright guess. What's the recipe?

---

### Round 4 — Reusability scan against decomp + wiki
*(NOT YET PERFORMED — will execute after Round 3 answers + with the grep-wiki-first discipline)*

Planned scans:
- `Hoe` class in decomp → for Trailblazer's Spade implementation pattern
- `Sign` class in decomp → for Painted Signs interaction extension
- `SE_Rested.CalculateComfortLevel` in decomp → for cairn comfort patch surface
- `Minimap.AddPin` + pin data structures in decomp → for two-tone pins
- `Torch` class in decomp → for Path Lamp Tier 1 reuse + chain-ignition surface
- `Piece.m_resources` shape in decomp → for recipe registration
- Wiki: `Cartography_Table.md` → for disable-mechanism surface
- Wiki: `Raspberries.md`, `Blueberries.md`, `Coal.md`, `Bone_fragments.md`, `Resin.md` → for pigment recipe-input availability per biome
- Wiki: `Banner.md` → for the +1 comfort radius/contribution Cairns are modeled after

---

### Round 5 — Visual assets
*(NOT YET ASKED — will ask in next round)*

---

### Round 6 — Scope boundaries / out-of-scope
*(NOT YET ASKED — will ask after Round 4)*

---

## Explicit features requested (running list)

1. **Explorer's Bench** (Meadows, v1 = kitbash vanilla Workbench with antlers from Deer Trophy integrated into bench art, recipe = **10 Wood + 4 Stone + 1 Deer Trophy**)
2. **Cairns** — 5-tier comfort floor 3/4/5/6/7, build cost **3 Stone + 1 Resin + 1 Cairn Marker**, upgrade cost flat **3 Stone + 1 Resin** per tier, repair cost flat **3 Stone + 1 Resin**, mandatory decay, ≥75% pristine (resin glows) / <75% fizzled / <25% downgrade / 0% collapse, pigment+banner persist, auto-re-ignite glow on repair-to-pristine
3. **Cairn Marker** (pre-crafted consumable, recipe = **2 Leather Scraps + 1 Finewood + 1 Pigment** of player's color, crafted at Explorer's Bench, pigment color binds cairn color at craft-time)
4. **Pigments** — R/W/B/Blue, 2/craft, stack 20, weight 0.1, recipes: R=raspberry, W=bone fragment, B=coal, Blue=blueberry (1:2 each)
5. **Painted Signs** — ONE buildable sign (`piece_sbpr_sign`, 2 Wood), placed via the **Trailblazer's Spade build menu** ('Trail' tab, NOT the Hammer; no station-proximity to place), UNPAINTED. Interacting with a placed sign opens a **custom combined Paint+Text uGUI panel** (replaces the vanilla text dialog): set a **text/board color** AND an optional **border color** (two-tone), pay one pigment per filled slot via `{Paint this and consume}` (border optional; same color in both slots = 2 of that pigment; ≥1 required; re-paint re-consumes), edit the label via `{Update Text}` (free, locked until a color is chosen). Both tones + text persist via ZDO (`SBPR_SignTextColor` + `SBPR_SignBorderColor`). **Free-standing on a kitbashed 2m wood pole** (`wood_pole2`), board at readable height (Daniel 2026-06-05); two-tone via a kitbashed `SBPR_SignBorder` element. Pin path (Shift+E) deferred/unregistered (combined Paint+Text panel, two-tone, Daniel 2026-06-05; supersedes the 6/04 apply-ink model)
6. **Trailblazer's Spade** — single tool item, hoe/hammer-tier, 1.5/3/5m path widths, **Replant Grass in 3 widths (1.5/3/5m)** mirroring the path widths (each restores grass over the stated footprint, still mirrors the Cultivator's "Grass" mode — NOT cultivate, NO terrain raise/level at any width; 3 widths per Daniel's 2026-06-05 playtest, scaling only the grass/paint radius), Clear Vegetation wide-radius (deferred to v0.2.0), recipe **5 Wood + 2 Flint + 2 Leather Hides**, crafted at Explorer's Bench
7. **Path Lamps** — **3 Wood + 2 Resin** (Meadows-tier, Daniel 2026-06-04), placed via the **Trailblazer's Spade build menu** ('Trail' tab, NOT the Hammer; no station-proximity to place), dimmer than torch, longer fuel, manual ignition (no chain ignition). **Scaled 3× vertically** (foot-anchored — base on the ground, flame at the new top; Daniel 2026-06-05)
8. **Map disable in v1** — Cartography Table disabled (no build, no functionality on existing); nomap=ON → no map; nomap=OFF → minimap only (no M-key, no north indicator)

**NOT in v1:** Ember Lamps, Beacons, Seer's Stone, Map Station, Pocket Portal, Twisted Portal, Iron Compass, Inert Guardian Stones, Yellow pigment (cloudberry).

## Constraints stated

- Standalone-by-default; solo install must be complete good experience
- "Leverage Unity indirectly" — runtime composition of vanilla prefabs/materials/ParticleSystems OK; Unity Editor + assetbundles NOT in v1
- v1 visual approach is "kitbash for playtest" — playtest-quality mechanics ≠ ship-quality art
- No server-gating for Trailborne (philosophy mod, not house-rules mod)
- All v1 pieces are Meadows tier (no biome-progression gate in v1)
- v1 DOES nerf vanilla map (Cartography Table disabled, no M-key map)
- v1 doctrine: corpus-first — grep `~/valheim/sbpr-corpus/wiki/fandom/` BEFORE claiming any vanilla-content fact

## Out of scope (user-confirmed)

- **Guardian Stones (active OR inert)** — entirely separate mod family, NOT Trailborne
- Local Maps + Map Stations — v2 (Black Forest tier)
- Real Tents (Bear hide) — v2 (Black Forest tier)
- Cartographer's Kit (gated on 4 pigment discovery) — v2
- Iron Compass — v3 (Swamps tier, iron is Swamps metal)
- Twisted Portal, Beacons, Ember magic, Scrying Altar, Smokeless Cookfire — v3
- Seer's Stone (crystal-gated, Stone Golem drop) — v4 (Mountains tier, sole headline)
- Plains sailing tier, lighthouse-promote, Star Glass — v5
- Portable map magic — v6 (Mistlands)
- Custom Unity-authored assets — deferred to Locations work (v∞)
- Ship-quality custom art for v1 pieces — v1.1+ polish

## Reusability notes

*Round 4 — decomp + wiki + reference-mod scan, derived from `design/PARKED-2026-06-03.md` as source of truth (not re-imagined). Source-of-truth lines/files captured so the implementer doesn't re-derive.*

### Explorer's Bench (custom `CraftingStation`)

- **Vanilla anchor:** `piece_workbench` prefab. Reuse pattern proven by 3+ reference mods (`RandyKnapp/AdvancedPortals` sets `CraftingStation = "piece_workbench"`; `rolopogo/CraftyCarts` does `Prefab.Cache.GetPrefab<GameObject>("piece_workbench").GetComponent<CraftingStation>()`).
- **Class:** `CraftingStation` (`assembly_valheim.decompiled.cs` lines 56034–56416). Key fields: `m_name`, `m_icon`, `m_discoverRange`, `m_rangeBuild = 10f`, `m_craftRequireRoof = true`, `m_craftRequireFire = false` (vanilla Workbench doesn't need fire), `m_useDistance = 2f`, `m_craftingSkill = Skills.SkillType.Crafting`, `m_areaMarker`, `m_inUseObject`, `m_haveFireObject`.
- **Registration:** clone `piece_workbench` GameObject in `ZNetScene.Awake` postfix, swap `Piece.m_name = "$sbpr_piece_explorers_bench"`, set the SBPR recipe via `Piece.m_resources`, append the clone to the `_HammerPieceTable.m_pieces` list. **Pitfall:** `PieceTable.m_availablePieces` (`assembly_valheim.decompiled.cs` 59893–60202) caches lazily; register pieces in `ZNetScene.Awake` postfix BEFORE the first `Player.Awake` fires.
- **Art swap (Tier 1 — vanilla mesh + custom material):** runtime material swap on the workbench mesh. Antler integration deferred to visual round.

### Trailblazer's Spade (Hoe-class ground-modification tool — NOT a Hammer-class piece spawner)

- **Per PARKED v1 scope:** "1.5/3/5m paths, replant, ClearVegetation". This is a **Hoe variant**, not a build-piece spawner. Three terrain-paint modes (path widths) plus replant and clear actions.
- **🔴 NOT in scope:** **No Cultivate ability.** Cultivate is the vanilla Cultivator's job (turning ground into cultivated soil for crops). The Trailblazer's Spade stays in its own lane — exploration/trail-discipline, not farming. Earlier draft said "Cultivate replant"; that was a misread of PARKED's shorthand. Removed.
- **Vanilla anchors:** `TerrainComp` (`assembly_valheim.decompiled.cs` line 123154) owns ground modifications; `Heightmap` exposes `IsCleared` and the paint-mask enum (`PaintType.Path`, `PaintType.Reset` — line 123801 / 109565). The vanilla `Hoe` prefab + tool item is the closest peer (path-paint surface).
- **Class:** clone the `Hoe` item prefab, replace its `m_buildPieces` PieceTable with a Trailborne-specific table that exposes three "path" entries (1.5m / 3m / 5m widths) plus **three replant entries** at the same 1.5m / 3m / 5m widths. The paint operations bottom out at `TerrainModifier.PaintType.Path` / `Reset` — vanilla already supports the brush widths via radius parameters; we wrap them at our widths. The replant entries clone the vanilla `replant` op and scale **only its grass/paint radius** (leaving level/smooth radii at vanilla stock — see replant mechanic below).
- **Replant mechanic (3 widths — Daniel playtest 2026-06-05; supersedes the single-op form from 2026-06-04):** clone the vanilla **`replant` prefab** (the Cultivator's "Grass" mode — `Assets/GameElements/Pieces/replant.prefab`, display token `piece_replant` → "Grass") **three times** and register `piece_sbpr_replant_narrow/standard/wide` at **1.5m / 3m / 5m**, mirroring the three path widths. Each clone scales **ONLY `m_paintRadius`** (the grass/vegetation footprint) and leaves `m_levelRadius`/`m_smoothRadius` at the vanilla op's stock values, so every width behaves like the Cultivator's "Grass" mode — a grass-restore brush that regrows grass on dirt with **no terrain raise/level/cultivate at any width**. ⚠️ The original M3 build wrongly cloned the **`cultivate`** prefab (the soil-tiller that turns ground into farmland) at a forced **5m** radius, producing a wide terrain-modifying "UBER level" op (the PR #16 bug). The fix was to clone `replant` instead; this slice extends it to three grass-restore widths while **preserving that fix by construction** — because the level/smooth radii are never written, no replant width can reintroduce terrain modification. `cultivate` is the Cultivator's farming job and stays out of the spade's lane.
- **ClearVegetation (deferred to v0.2.0):** would use a clear/`Reset` paint pass on a radius to remove bushes/grass/small rocks. NOT shipped in v0.1.0 — the spade ships only Path (×3) + Replant.

### Painted Signs (single buildable + combined Paint+Text panel, two-tone)

- **Model (LOCKED 2026-06-04, Daniel):** ONE buildable sign, placed UNPAINTED, painted afterward by applying a pigment/ink item. This SUPERSEDES the earlier "subclass `Sign` + custom multi-field edit dialog + E text-color / Shift+E accent-color / two-tone pin" design. No custom edit dialog, no accent color, no two-tone pin for v0.1.0.
  - **Build:** `piece_sbpr_sign` ("Painted Sign"), **Trailblazer's Spade build menu** ('Trail' tab — NOT the Hammer; design pillar: Explorer-placed pieces live on the Tools), **2 Wood**, **no station-proximity required to place** (`Piece.m_craftingStation` cleared). Clone of the vanilla wood `sign` prefab **kitbashed onto a vanilla 2m wood pole (`wood_pole2`)** so it stands free on the ground like a trail signpost (Daniel 2026-06-05), board raised so its TOP sits just under the measured pole crown (board centre ~1.65m, post foot flush at ground; board-top anchored to the measured crown at register time — pivot-robust, no magic height). The pole is a decorative child stripped of ZNetView/Piece/WearNTear/Collider (no own ZDO, not separately destructible, never intercepts the E raycast — board stays the sole interact/paint target). Ships in plain wood (unpainted); ink is NOT a build ingredient.
    - **Board lateral standoff (v0.2.9):** the board mounts against the post's SIDE face — offset laterally along the board's facing normal by (½ post thickness + ½ board thickness, both measured from transformed bounds) so the board's back face just kisses the post side (no interpenetration, no gap); it is NOT embedded in the post centerline. The lateral axis and sign are derived from the donor board's facing normal at runtime, never hardcoded. Exact kiss tolerance is visual-polish (v0.2+).
  - **Paint:** with an ink in hand, apply it to the placed sign → the sign takes that color. Apply a different ink → repaint. One ink consumed per paint. An already-applied color is a no-op (no ink consumed).
  - **Text:** vanilla `E` text dialog, unchanged. Default label "Painted Sign".
- **Color state:** stored per-instance on the sign's ZDO as a string field `SBPR_SignColor` (one of `red`/`white`/`blue`/`black`, or `""` = unpainted). Owner-write via `ZNetView.ClaimOwnership()` + `ZDO.Set(string,string)` (mirrors the `CairnTag` tier pattern). Persists across reloads + syncs to clients; re-applied to the mesh on spawn via a `SignTag` component (`Renderer.sharedMaterials` `_Color` tint).
- **Paint seam (clean-room):** Harmony prefix on `Sign.UseItem(Humanoid, ItemDrop.ItemData)` — the public `Interactable.UseItem` contract for "apply a held item to this placed object," the same surface `ItemStand` uses. When the used item is one of our four inks AND the target carries a `SignTag`, we paint + consume one ink + return `true` (skip vanilla). Any other item / non-SBPR sign falls through to vanilla. Method/field signatures (`Sign : Hoverable, Interactable, TextReceiver`; `bool UseItem(Humanoid, ItemDrop.ItemData)`; `ItemData.m_dropPrefab`; `Inventory.RemoveItem(ItemData,int)`; `ZNetView.IsOwner/ClaimOwnership/GetZDO`; `ZDO.GetString/Set`) were confirmed against `assembly_valheim.dll` **public metadata** — not decompiled IronGate source.
- **Pin behavior (deferred, currently UNREGISTERED):** `SignInteractPatch` (Shift+E → `Minimap.AddPin` reflecting the sign's single color, no-op if `nomap=ON`) exists but is not `PatchAll`-registered, so the pin gesture is presently dead code. Wiring it is a separate follow-up. (Single-color pin now; the dropped accent means a single pin color, not two-tone.)
- **UGC gate (decision LOCKED 2026-06-03):** v1 Painted Signs inherit vanilla `Sign`'s UGC gate as-is. Defer the bypass conversation to v2.

### Cairns (custom piece + comfort-level state machine via `SE_Rested.CalculateComfortLevel` patch)

- **Per PARKED v1 scope:** "3/4/5/6/7 comfort floor, max() clamp, patch `SE_Rested.CalculateComfortLevel` directly (not in vanilla `ComfortGroup` enum), repair flat 3 stone + 1 resin, pigment+banner persist, downgrade@25%, collapse@0%."
- **Vanilla anchors:** `SE_Rested` class at `assembly_valheim.decompiled.cs` line 25338. `SE_Rested.CalculateComfortLevel(Player)` at line 25397, overload `CalculateComfortLevel(bool inShelter, Vector3 position)` at line 25402. Vanilla `ComfortGroup` enum at line 116068; `Piece.m_comfortGroup` at line 116123 — confirmed that adding to the enum requires touching the assembly; the PARKED decision to patch `CalculateComfortLevel` directly bypasses this.
- **Why the `CalculateComfortLevel` patch (PARKED rationale):** vanilla comfort is computed by iterating nearby pieces grouped by `ComfortGroup` and picking the highest-comfort piece in each group. Cairns aren't in any vanilla group. Adding a new enum value would require IL-modifying the enum (fragile across game updates). Instead, postfix `SE_Rested.CalculateComfortLevel(bool, Vector3)`: scan nearby SBPR cairns within vanilla's comfort search radius, find the highest-tier (3-7), `result = Mathf.Max(vanillaResult, cairnTier)`. Clean and update-tolerant.
- **Lifecycle state machine:** Cairn has 5 tiers, comfort floor 3/4/5/6/7. Health tracked via vanilla `WearNTear.m_health` (line 128064) and `m_onDamaged` delegate (line 128029). Postfix `WearNTear.OnDamage` to check our thresholds: at `m_healthPercentage < 75%` lose pristine (visual indicator), at `< 25%` downgrade by one tier (reduces comfort floor by 1, resets health to 100% of new tier), at `0%` collapse (destroy piece, leave a pile-of-rocks remnant).
- **Repair:** flat 3 Stone + 1 Resin per tier-upgrade or per pristine-restore. Painted color + banner attachment persist through downgrade/upgrade — stored on cairn's ZDO, re-applied on tier swap.
- **Initial build:** 3 Stone + 1 Resin + 1 Cairn Marker. Marker carries the pigment color choice into the cairn ZDO at place-time.
- **Open thread (PARKED):** "Cairn downgrade re-ignite resin? lean: deliberate-only." Stays open.

### Pigments (custom `ItemDrop`, consumable craft input)

- **Per PARKED v1 scope:** "R/W/B/Blue, 2/craft, stack 20, weight 0.1". Four pigments in v1: red, white, black, blue. Each recipe yields 2 pigments per craft, max stack 20, item weight 0.1.
- **Vanilla anchor:** no vanilla pigment/dye/ink/paint item exists (full wiki grep returns only `Trinkets.md`). Pigments are novel; naming space is clean.
- **Pattern:** clone any simple consumable prefab (e.g. `Raspberry`) as a sprite-only stand-in, swap `m_shared.m_name`, `m_shared.m_icons`, `m_shared.m_maxStackSize = 20`, `m_shared.m_weight = 0.1f`. One `Recipe` per color, crafted at Explorer's Bench, yields 2 per craft.
- **v1 ingredient inputs:** red ← raspberries; blue ← blueberries; white ← (TBD — bone fragment? mushroom?); black ← (TBD — coal? greydwarf eye?). These are reasonable instincts only — needs Daniel confirmation in Round 5 alongside icons.

### Cairn Marker (custom `ItemDrop`, single-use consumable)

- **Recipe (LOCKED):** 2 Leather Scraps + 1 Finewood + 1 Pigment. Pigment color selected at craft-time → cairn color at build-time.
- **Pattern:** simple `ItemDrop` clone (any small consumable), recipe at Explorer's Bench. Consumed by the Cairn piece's `Piece.m_resources` requirement on initial build only (not on tier upgrades).

### Path Lamps (kitbash vanilla `piece_groundtorch_wood`)

- **Recipe (LOCKED, Daniel 2026-06-04 — see §A3.7):** **3 Wood + 2 Resin** (downshifted from the earlier 3 Corewood; Meadows-tier accessibility).
- **Build menu (LOCKED, Daniel 2026-06-05):** placed via the **Trailblazer's Spade build menu** ('Trail' tab — NOT the Hammer; design pillar: Explorer-placed pieces live on the Tools), **no station-proximity required to place** (`Piece.m_craftingStation` cleared).
- **Vanilla anchor:** `piece_groundtorch_wood` (Fireplace + Piece combo). Tune `Fireplace.m_fuelItem = Resin`, extend `m_secPerFuel` for "long burn" (vanilla torch ~600s/resin; ours ~1800s/resin so a 2-resin lamp = ~1hr burn), reduce child `Light.intensity` ~30% for "dimmer trail glow."
- **Visual (kitbash, Daniel 2026-06-05):** **scaled 3× vertically** so it reads as a tall standing path lamp. Foot-anchored: the base stays flush with the ground and the flame/light rides up to the new top (geometry children scale on Y; the flame/Light children keep their size and only translate up — not a bonfire-on-a-stick). Root collider intentionally NOT rescaled (flag QA if the collision box should match the taller visual).

### v1 Cartography Table (DISABLED)

- **Approach:** prefix `MapTable.OnRead` and `MapTable.OnWrite` (`assembly_valheim.decompiled.cs` 114014–114141) to return false. Show MessageHud text: "$sbpr_cartography_disabled_v1 — coming in v2."
- **Reference:** `shudnal/NomapPrinter` already patches the exact same `MapTable.OnRead`/`OnWrite` surface (`HarmonyPatch(typeof(MapTable), nameof(MapTable.OnRead))`). Clean precedent — different intent, same surface.

### Map situation (PARKED-locked, NOT a piece — global behavior)

- **Per PARKED:** "nomap ON = no map at all. nomap OFF = minimap ONLY, freely rotating, NO north indicator. No M-key map."
- **When nomap mode is OFF:** patch `Minimap.SetMapMode` to suppress `MapMode.Large` (full M-key map blocked); patch the minimap rotation/north-arrow logic in `Minimap.Awake`/`Minimap.UpdateMap` to disable the compass needle.
- **When nomap mode is ON:** patch `Minimap.IsOpen` to return false (compose with NomapPrinter's existing patch on this method — read NomapPrinter for the precise pattern, do not copy).
- **Pin behavior coupling:** when nomap is ON, `Minimap.AddPin` calls from Painted Signs early-return (PARKED: "no-op if nomap ON"). When nomap is OFF, pins land on the minimap as normal.

### Vanilla content corpus-verified this round

- ✅ `piece_workbench` exists; clone pattern proven by 3+ reference mods.
- ✅ `Sign` class signature current as of Bog Witch/Ashlands decomp; UGC gate at `Sign.Interact` confirmed.
- ✅ `WearNTear.m_health` / `m_onDamaged` / `OnDamage` / `Repair` all current.
- ✅ `SE_Rested.CalculateComfortLevel` exists at lines 25397/25402; both overloads present. `ComfortGroup` enum at 116068. PARKED rationale for patching `CalculateComfortLevel` directly (rather than extending the enum) is sound — confirmed cairns aren't in the vanilla enum.
- ✅ `TerrainComp` + `Heightmap.IsCultivated`/`IsCleared` exist (lines 123154, 109613). `PaintType.Cultivate`, `Path`, `Reset` enum values present (line 123801). Trailblazer's Spade wraps these.
- ✅ `Pickable.m_picked` + `m_itemPrefab` + `m_amount` + `m_respawnTimeMinutes` exist — usable for replant and v4 Seer's Stone area-pop.
- ✅ `Resin` is real, drops from all tree types + Greylings.
- ✅ Vanilla `Cairns` (`Waymarker01`/`Waymarker02`) are inert Mountain-biome POI, NOT buildable. Our buildable + tiered + comfort-emitting Cairn is original.
- ✅ `TextInput.RequestText` is the vanilla single-line input dialog (line 27163 — used by rename). For our multi-field sign edit dialog, we'll clone `InventoryGui` panel templates at runtime (no vanilla multi-field text-input dialog exists).
- ❌ No vanilla pigment/dye/ink/paint item. Pigments are fully novel.
- ❌ No vanilla "tiered building piece with comfort progression" — Cairn lifecycle is original work.

### What this round did NOT cover (deferred to later rounds or already locked elsewhere)

- **Pact** — out of scope for this mod entirely. Trailborne v1 ships standalone with no shared-library dependency.
- **Shared-infrastructure code organization** — also out of scope; each mod stands alone for now.
- **LOC estimates** — withdrawn. Not useful before tasks decomposition; was anchored to imaginary shared infrastructure.
- **Inert Guardian Stones** — PARKED as stretch goal contingent on `valheim-regions` macro boundaries finalizing first. NOT a v1 blocker.

## Visual assets

**v0.1 decision (LOCKED 2026-06-03):** ship with **placeholder art**. Focus on getting gameplay elements working first; visual polish iterates from a working playtest, not before. Asset doctrine (fact #112 — zero Unity assetbundles, Tier 0/1/2 reuse only) still applies — placeholders are runtime-loaded PNGs and vanilla material swaps, not custom Unity geometry.

### Placeholder art lanes for v0.1

- **Item icons** (Trailblazer's Spade, Cairn Marker, 4 Pigments, Path Lamp): runtime-loaded PNGs via `File.ReadAllBytes` → `Texture2D.LoadImage` → `Sprite.Create`. Generated as needed via FLUX local lane; quick + good-enough is the bar. Iterate freely in v0.1 → v0.2.
- **Build icons** (Explorer's Bench, Painted Sign, Cairn × 5 tiers, Path Lamp build form): same runtime-PNG approach.
- **In-world meshes:**
  - Explorer's Bench → vanilla Workbench mesh + color-tinted material (so it visually reads as "not-quite-Workbench" in the world; the dialog hover-name + icon disambiguate it).
  - Cairn (5 tiers) → procedurally-stacked vanilla Stone prefabs at runtime, scaled per tier. Pigment overlay = material tint on the stack. Banner attachment = vanilla Banner prefab parented to the cairn root at place-time.
  - Painted Sign → vanilla Sign mesh; built unpainted (plain wood material), runtime per-instance tint once painted (reads `SBPR_SignTextColor` + `SBPR_SignBorderColor` from ZDO, applies the two tones — board + border — at spawn). ⚠️ Needs a separable border renderer/material on the mesh (open technical question).
  - Path Lamp → vanilla `piece_groundtorch_wood` mesh, no swap. Lower light intensity is the visual differentiation.
  - Trailblazer's Spade → vanilla Hoe item mesh; icon does the work of disambiguation in inventory.
- **Custom UI panel** — **REQUIRED for v0.1.0** (re-locked Daniel 2026-06-05). The Painted Sign uses a **custom combined Paint+Text uGUI panel** (text color + border color swatches, crafting-style pigment cost, `{Paint this and consume}` + `{Update Text}` buttons) that replaces the vanilla single-line text dialog. This **reverses** the 6/04 "no UI, apply-ink" note. Panel is built clean-room (no copied vanilla UI prefab); no new *mesh* prefabs required, but the sign mesh needs a separable border renderer for the two-tone tint.

### Asset generation as needed

Starbright generates placeholders on demand during implementation. FLUX local lane is the default (fast, free, run from RequiemSoul → Prime-W). Style target for placeholders: legibly Valheim-shaped, low-fi-okay, recognizable silhouette. Polish quality is explicitly NOT the bar — "you can tell what it is" is the bar.

### Deferred to v0.2+ (after gameplay works)

- Iconography polish pass (consistent line weight, palette, silhouette discipline across the set)
- Antler integration for Explorer's Bench mesh (per Q3.10 — deer trophy antlers visually integrated into the bench, not mounted on top)
- Custom mesh authoring (if/when v2 brings Map Station, Iron Compass, Tents — those have genuine geometry needs)
- Visual differentiation of the 5 Cairn tiers beyond "scale + color" (e.g. moss progression, lichen, accumulated character)
- Pigment vegetation-stain visual on Cairns (color seeps slightly into the rock surface vs flat tint)

### Open for placeholder generation when implementation gets there

- 4 Pigment ingredient confirms: red ← raspberries ✓, blue ← blueberries ✓, white ← bone fragment OR mushroom (TBD), black ← coal OR greydwarf eye (TBD). Pick at icon-time alongside the visual.
- Painted Sign color palette: Round-1 captured "color is emergent player decision" (pillar 2). v0.1 ships with a fixed palette of 4 colors derived from the 4 pigments; the player chooses a sign's **text color and border color** (two-tone) via the combined Paint+Text panel after placement (two-tone re-locked 2026-06-05, reversing the 6/04 single-color drop). No per-color sign icons needed (one buildable, vanilla wood icon); the swatches live in the panel.

## Open questions / TBD

- **Q3.6: Cairn per-tier build cost** ✅ LOCKED — 3 Stone + 1 Resin + 1 Cairn Marker (initial); 3 Stone + 1 Resin (per upgrade)
- **Q3.7: Path Lamp wood material** ✅ LOCKED — corewood
- **Q3.8: Ember Lamps in v1** ✅ DROPPED FROM v1
- **Q3.9: Cairn Marker recipe** ✅ LOCKED — 2 Leather Scraps + 1 Finewood + 1 Pigment (player color choice)
- **Q3.10: Explorer's Bench exact quantities** ✅ LOCKED — 10 Wood + 4 Stone + 1 Deer Trophy. No raspberries. No resin. (Earlier I had inferred raspberries+resin from PLAYER_GUIDE.md narrative — Daniel corrected: the narrative's mention of those ingredients was describing what the bench is USED FOR, not what it's MADE OF.)
- **Q3.11: Path Lamp exact quantities** ✅ LOCKED — 3 Corewood + 2 Resin (3m light pole)
- Round 4 decomp/wiki scans pending (will leverage `design/nomap.md`'s existing line-references first)
- Round 5 visual assets pending
- Round 6 out-of-scope confirmation pending

## PLAYER_GUIDE.md / design/*.md doc-PR follow-up tracker

After spec finalization, the following doc updates are needed to keep repo consistent with this requirements.md (the authoritative v1 spec):

### ✅ Done this session
- **Rename Orienteering Table → Explorer's Bench** — propagated to `README.md` (module list line 28), `PLAYER_GUIDE.md` (lines 56-62, 87, 121, 229-230), and `design/nomap.md` (§1 heading, prefab name `SBPR_ExplorersBench`, localization key `$sbpr_piece_explorers_bench`, plus references in open-questions §2 and §5 and risk-ranking §5).
- **design/nomap.md §1 recipe** — corrected to `10 Wood + 4 Stone + 1 Deer Trophy` (was `20W + 4Stone + 4Bone fragment + 2Greydwarf eye + 2Deer hide`). Explanatory note added inline so future readers see why the change was made.
- **PLAYER_GUIDE.md bench-recipe prose** — line 58-62 rewritten. Now explicitly states `10 Wood + 4 Stone + 1 Deer Trophy` and clarifies that antlers are part of the bench art (not mounted-on-top). The misread-inducing phrase "raspberries (for red pigment), and resin (for ink fixative and lamp oil)" has been removed from the recipe paragraph (raspberries/resin are still mentioned later in §Meadows as pigment inputs, which is correct — they're what the bench is *used to process*, not ingredients in the bench itself).

### ⏳ Remaining doc-PR work
1. **Trailblazer's Spade recipe** — `PLAYER_GUIDE.md` line 67 says "wood, tin, flint". Today-locked: 5 Wood + 2 Flint + 2 Leather Hides. No tin.
2. **v1 Cartography Table behavior** — `PLAYER_GUIDE.md` §"Cartography Table (vanilla) — but rebalanced" describes the v2 Map Station shape. v1 is DISABLED, not "rebalanced." Move that section to a future-v2 doc or annotate inline.
3. **Painted Sign interaction model** — line 253 says "default keybind _TBD_" for the pin trigger. **Re-locked 2026-06-05 (Daniel, from UI mockup):** ONE buildable sign, placed UNPAINTED (2 Wood). Interacting with a placed sign opens a **custom combined Paint+Text panel** — set a **text color AND a border color** (two-tone), pay one pigment per filled slot via `{Paint this and consume}` (border optional, re-paint re-consumes), and edit the label via `{Update Text}` (free, locked until a color is chosen). This **supersedes** the 6/04 apply-ink/single-color/no-UI lock. Pin trigger (text color, no-op if nomap=ON) deferred + currently unregistered. PLAYER_GUIDE needs the build-unpainted-then-open-panel loop surfaced (and the "color baked at craft time" line corrected).
4. **Cairn lifecycle prose** — PLAYER_GUIDE references "the way Cairns are maintained" in Guardian Stones forward-pointer (lines 351-353). Cairn lifecycle now fully specified (3 Stone + 1 Resin + 1 Cairn Marker initial, flat 3+1 upgrade/repair, 5-tier comfort floor, 75% pristine threshold, 25% downgrade, 0% collapse). PLAYER_GUIDE should get a brief Cairn lifecycle section in §Meadows.
5. **Cairn Marker (new item)** — not yet in PLAYER_GUIDE. Add to crafted-at-Explorer's-Bench item list with recipe: 2 Leather Scraps + 1 Finewood + 1 Pigment.
6. **Remove Ember Lamps / Beacons from v1 scope language** — PLAYER_GUIDE includes them in the Black Forest section. They're not in v1. Either move them to a "Roadmap" section or clearly label them v1.1+.

## Vision context

Aligned with holographic facts:
- `#111` Trailborne naming lock
- `#112` Trailborne asset doctrine (Round 1 refined as "leverage Unity indirectly")
- `#93` Niflheim parked design
- `#94` Corpus-first rule (must grep wiki before claiming vanilla-content facts)
- `#110` Kanban Swarm execution handoff after spec lock

And primary-source design lock: `design/PARKED-2026-06-03.md` in this repo.

Bigger picture: Trailborne v1 is SBPR's first public Thunderstore release.
Its reception sets the brand for everything downstream (Guardian Stones as
separate mod family, Niflheim modpack, the eventual `niflheim.wiki`).
Standalone-install experience is non-negotiably good.
