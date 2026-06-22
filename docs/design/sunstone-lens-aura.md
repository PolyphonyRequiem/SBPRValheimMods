---
title: "Sunstone Lens → pulsing solar aura — the empty-ring affordance, animated and re-homed onto the minimap rim (design decision, awaiting Daniel's gate)"
status: proposed
purpose: "Architect design decision for Daniel's 2026-06-21 /bug idea: while the Sunstone Lens is worn, a faint golden aura pulses 'around the outer rim of the minimap (or as the art for the no-minimap ring).' KEY REFRAME — the gold ring is NOT net-new: SBPR already draws a faint warm-amber solar ring as the lens' empty-state affordance (SunstoneLensHudOverlay._emptyRing :164, colour CSolarRing :101 = RGBA 0.98/0.78/0.36/0.18, sprite RingSprite() :579). This card is EXTEND + ANIMATE, not build-from-scratch: (1) make the existing ring PULSE, and (2) re-home that same pulsing aura onto the minimap rim WHEN a minimap owns detection. CENTRAL INSIGHT — Daniel's '(or as the art for the no-minimap ring)' maps exactly onto the ALREADY-SHIPPED Sunstone→minimap handoff (t_91e86951): the handoff hides _content (and _emptyRing with it, :315) when a minimap owns detection, so the aura is simply the empty-state ring re-homed onto whichever surface currently owns detection — ONE concept, following the existing LensRenderPlan. STRUCTURAL SIBLING of sunstone-lens-minimap-handoff.md (PR #214) and iron-compass-minimap-ring.md (PR #226): a lens-gated cosmetic that lives wherever detection lives. Corrects 3 grounding errors in the bug card (MapSurface.cs is Features/Cartography/ not Features/Sunstone/; the carry-disc bezel setter routes through MapViewer.SetCompassNorth which fans out to BOTH _disc AND _modal — two surfaces, not one; the vanilla-minimap mount is north-up under m_pinRootSmall, no counter-rotate) and surfaces 1 architecture finding the card hand-waved (on the carry-disc the bezel is ALREADY a per-frame colour consumer for the Iron Compass iron/bronze tint — a lens aura on the SAME _bezel.color channel COLLIDES; they do not trivially coexist). Every code line cited against main @ aa03e21. UNSPECCED visual knob → Daniel look-locks the pulse by eye (he's colourblind; gold named by RGB, not hue). Card t_acaa0190; Daniel gates the decision AND the merge."
owner: Daniel (design authority); Starbright (architect — capture + grounding)
supersedes_partial:
  - "docs/design/sunstone-lens-trophy-ring.md §1.6 (:237-247) + §5.3 (:436) — the 'faint solar ring … ~0.18 alpha … ShowEmptyRing default ON' empty-state affordance gains an OPTIONAL pulse (the static ring becomes a breathing one when LensAuraPulse is on); the ring's DESCRIPTION as static is the only thing superseded, the affordance and default-ON stay."
  - "docs/design/sunstone-lens-minimap-handoff.md — the handoff currently hides the whole ring _content (incl. _emptyRing) when a minimap owns detection; this card RE-HOMES the empty-state aura onto the owning surface's rim instead of letting it vanish, so 'lens is live' stays legible on the minimap path. No blip/projection change — purely an added rim element on the surface the handoff already targets."
graduated_to: "docs/v3/planning/sunstone-lens-aura-impl-spec.md (the buildable spec; cut on Daniel's gate)"
---

# Sunstone Lens → pulsing solar aura — the empty-ring affordance, animated and re-homed

> **STATUS: PROPOSED — Daniel gates this before it becomes an impl-spec.** This is
> an UNSPECCED new visual knob, so the spec-first repo rule (AGENTS.md) holds the
> impl until Daniel look-locks the pulse by eye. 🟢 DECIDED = Daniel's locked call
> or forced-by-grounding architecture; 🟡 OPEN = a knob Daniel is still exploring;
> 🔵 GROUNDED = verified against `main` @ `aa03e21`.
>
> **Sibling cards** — same shape, a lens/compass-gated cosmetic drawn on whichever
> map surface is live: [`sunstone-lens-minimap-handoff.md`](sunstone-lens-minimap-handoff.md)
> (PR #214, impl `t_91e86951`) and [`iron-compass-minimap-ring.md`](iron-compass-minimap-ring.md)
> (PR #226). Read §3 first if you read only one section — it's the insight that
> turns five "open" knobs into two grounded + three eyeball calls.

> 🐛 **PARTIAL SUPERSESSION (card `t_2d500d45`, Daniel `/bug`, 2026-06-22).** Daniel
> reported the no-minimap "ring" as *"just a screen space circle, not a 3d slowly
> pulsing 'sun corona' disc like we discussed."* That **redirects this card's
> flat-pulse half**: the no-minimap pulsing art is now a **world-space 3D sun-corona
> disc**, NOT an animated flat `_emptyRing`. The buildable spec is
> [`../v3/planning/sunstone-lens-corona-impl-spec.md`](../v3/planning/sunstone-lens-corona-impl-spec.md).
>
> - **§0–§5 below (pulse the FLAT `_emptyRing` for the no-minimap home) → SUPERSEDED**
>   by the 3D corona. The screen-space annulus is removed, not animated.
> - **The MINIMAP-RIM RE-HOMING half (§3 / §7 AT-AURA-MINIMAP-RIM / AT-AURA-FOLLOWS-
>   DETECTION) → PARKED, still a live proposal.** Drawing the lens aura on a *minimap
>   surface* (vanilla corner-map rim / carry-disc bezel) when a minimap owns detection
>   is a **different surface and a different concern** (the §2 disc-bezel colour
>   collision) — it is **NOT built by `t_2d500d45`** and was **not blocked on it**.
>   Per Daniel it is **conditional on him still wanting the aura on a minimap surface**;
>   if he confirms, cut a fresh `engineer-ui` card then. This doc stays `proposed` for
>   that parked half.

Daniel's idea, verbatim (2026-06-21, via `/bug`, ticket `ticket-sunstone-lens-aura`):

> "when the lens is equipped, a faint golden aura should be present pulsing around
> the outer rim of the minimap (or as the art for the no-minimap ring)"

---

## 0. The reframe — the gold ring already exists; this is extend + animate 🔵 GROUNDED

SBPR **already** draws a faint warm-amber solar ring as the Sunstone Lens'
empty-state affordance. Daniel's "the art for the no-minimap ring" **is this
existing element** — he named it, he didn't ask for a new one:

| What | Where (`main` @ `aa03e21`) |
|---|---|
| The ring `Image` (`_emptyRing`) | `SunstoneLensHudOverlay.cs:164`, parented under `_content` at `:159` |
| Its colour `CSolarRing` | `:101` = `new Color(0.98f, 0.78f, 0.36f, 0.18f)` — **warm amber/gold, already faint (α 0.18)** |
| Its sprite | procedural annulus `RingSprite()` `:579` (256 px, 3 px-thick white ring, tinted by the `Image.color`) |
| Shown whenever worn + charged | `:418-422` (`_emptyRing.gameObject.SetActive(showEmpty)`, colour re-stamped each frame) |
| Half-alpha "depleted hint" variant | `:255-258` (`CSolarRing.a * 0.5f`) |

So the build is two small deltas, **not** a from-scratch aura:

1. **Animate it** — write a time-driven alpha into the ring's colour each frame
   instead of a constant α 0.18. There is **no pulse/breathing animation anywhere
   in the codebase today** (🔵 grepped `Mathf.Sin(Time` / `PingPong` / breathing-α
   → zero hits on a UI alpha; the only `Mathf.Sin` calls are geometric ring-position
   math, and the only "pulse" strings are the Signs crafting-cost affordance). It's
   net-new but trivial: a 2-line inline `α = base * (k + (1-k)·½(1+sin(t·ω)))`.
2. **Re-home it onto the minimap rim** when a minimap owns detection (§3).

**No new art assets.** Reuse `CSolarRing` (`:101`) + `RingSprite()` (`:579`). Reading
those is free; the annulus already renders at any size we anchor it to.

---

## 1. Grounding — the per-frame driver + the three surfaces 🔵 GROUNDED

**The per-frame home is already lens-gated.** `SunstoneLensHudOverlay.Update()`
(`:222`) runs every frame the HUD lives. It already early-returns when the lens
isn't worn (`GetEquippedLens(player) == null`, `:233-239`) and when the lens is
depleted (`lens.m_durability < SunstoneLens.MinChargeToDetect`, `:243`). So
**lens-equipped + charged state is known here per-frame** — the pulse drives from
the exact spot that already decides whether the ring shows at all. No new gate.

The aura can appear on up to three surfaces; each is already a known seam:

1. **Screen-space ring (the no-minimap home).** `_emptyRing` itself (`:164`). When
   no minimap owns detection, the camera-relative ring is the surface and
   `_emptyRing` is its empty art. Pulse = animate the `:422` colour write. Zero new
   attach.

2. **Vanilla corner minimap rim.** `SunstoneMinimapThreatLayer` (`:57`) already
   mounts a custom layer under `Minimap.instance.m_pinRootSmall` via `EnsureLayer()`
   (`:68-85`), invoked only when the lens owns the vanilla minimap
   (`SunstoneLensHudOverlay.cs:324`) → inherently lens-gated. **There is no SBPR rim
   `Image` on the vanilla map today** — the layer holds blips, not a rim. A rim aura
   is a single centred gold annulus sized to the small-map diameter, mounted the same
   way (a sibling of the blip layer, or a second child of the same root).
   - 🔴 **CORRECTION to the bug card:** the card says "mirror `EnsureLayer`" without
     noting orientation. The threat-layer's own comment (`:24-35`) establishes the
     vanilla corner map is **north-up** and SBPR never rotates `m_smallRoot` /
     `m_pinRootSmall` (only the player chevron rotates). A **rim** aura is
     radially symmetric, so orientation is moot — but **do NOT counter-rotate it**
     (that's the carry-disc's behaviour, wrong here). Mount centred, north-up, done.
     `m_smallRoot` (`Minimap.cs:132`) is the show/hide gate; `m_pinRootSmall`
     (`:140`) is the centred mount frame. Both public (clean-side, ADR-0001).

3. **SBPR carry-disc bezel (nomap-ON home).** The carry-disc has a real custom rim
   `Image` — `MapSurface._bezel` (RawImage, `:157`), a procedural bronze annulus
   (`EnsureBezelTexture()` `:1399`).
   - 🔴 **CORRECTION to the bug card (path):** `MapSurface.cs` lives at
     **`Features/Cartography/MapSurface.cs`**, not `Features/Sunstone/` as the card
     states. (All cited line numbers — `:157 / :235 / :1163 / :1399` — are correct;
     only the directory is wrong.)
   - 🔴 **CORRECTION to the bug card (routing):** the card says "add a `SetLensAura`
     setter beside `MapSurface.SetCompassNorth()` `:235`." That setter is **not
     called directly** — the Iron Compass pushes state through
     `MapViewer.SetCompassNorth(bool)` (`MapViewer.cs:131`), which **fans out to BOTH
     `_disc` AND `_modal`** (`:133-134`). There are **two** `MapSurface` instances
     (carry-disc + full-map modal), not one. A lens-aura setter must thread through
     `MapViewer` the same way, and the modal is in scope by symmetry (the compass
     ring already treats it so — `iron-compass-minimap-ring.md` Daniel ④).

---

## 2. 🔴 The one architecture finding the bug card hand-waves — the bezel colour collision

The card calls the carry-disc home "coexists fine, two rim consumers." **They do
not trivially coexist.** The disc bezel is **already a per-frame colour consumer**:

```
MapSurface.ApplyFieldOrientation (:1163):  _bezel.color = _compassWorn ? CIronTint : Color.white;
```

`_bezel.color` is **one channel, written every frame** off the compass's
`_compassWorn` flag (set via `SetCompassNorth`, `:235`). If a lens aura also wants
to write `_bezel.color` (gold), and the player wears **both** the Iron Compass and
the Sunstone Lens, the two features fight for the same pixel each frame — last
writer wins, and the order is incidental. This is a genuine design conflict, not a
co-existence.

Two clean resolutions (this is the disc-specific sub-knob in §5, Knob 1c):

- **(a) Separate element.** The lens aura is its **own** concentric gold `Image`
  just inside/outside the bezel (its own colour channel), never touching
  `_bezel.color`. Costs one more annulus; zero collision. **Architect lean.**
- **(b) Priority on the shared channel.** Define who owns `_bezel.color` when both
  are worn (e.g. iron-compass-wins, lens-aura-only-when-no-compass). Cheaper, but
  bakes a feature-coupling into `MapSurface` that the §3 model otherwise avoids.

On the **screen-space ring** and the **vanilla minimap rim** there is **no such
collision** — the aggro-state tint (`trophy-ring.md §1.8`, yellow→orange→red) tints
the trophy **slots** + star pips (`Image.color` per slot), **not** `_emptyRing`
(🔵 verified — `_emptyRing.color` is only ever `CSolarRing`-derived, `:258 / :422`).
So a gold pulse on the ring and aggro-tinted trophies are independent elements that
already coexist. The collision is **disc-bezel-only**.

---

## 3. 🟢 The central insight — the aura follows detection, via the SHIPPED handoff

Daniel's phrasing carries the architecture: *"around the rim of the minimap **(or
as the art for the no-minimap ring)**."* That "or" is **one concept with two
homes**, and the homes are **already arbitrated by shipped code**.

The Sunstone→minimap handoff (`sunstone-lens-minimap-handoff.md`, PR #214, impl
`t_91e86951`) already decides, every frame, **which surface owns detection**:

```
SunstoneLensHudOverlay.Update (:279):  LensRenderPlan plan = LensHandoffDecision.Resolve(sbprDiscBound, vanillaMinimapShowing, mode);
                              (:305):  if (plan.RingContentVisible) { _content.SetActive(true); RenderRing(...); }
                              (:310-316): else { HideAllSlots(); _content.SetActive(false); }   // ← _emptyRing hidden too (it's under _content, :159)
                              (:321-324): feedDisc / feedVanilla = plan.FeedMinimap && plan.MinimapTarget == LensSurface.{SbprDisc|VanillaMinimap}
```

`LensSurface` + `MinimapHandoffMode` enums live at `LensHandoffDecision.cs:76 / :46`.
**Exactly one surface owns detection per tick** (`:321-325`, mutually exclusive).

So today, **when a minimap owns detection, `_content` is hidden and the empty-state
ring vanishes** (`:315`). The player loses the "lens is live" cue precisely when the
detection moves to the minimap. Daniel's idea **fixes that hole**: re-home the
empty-state aura onto whichever surface `plan` already points at.

> **🟢 The model (forced by Daniel's "or" + the shipped handoff):** the pulsing
> solar aura is the **empty-state ring re-homed onto whichever surface currently
> owns detection.** No minimap → pulse `_emptyRing` (screen-space). Vanilla minimap
> owns it → pulse a rim annulus on the corner map. Carry-disc owns it → a gold
> element on the disc (§2 sub-knob). **ONE concept, driven by the existing
> `LensRenderPlan`** — the aura reads `plan.MinimapTarget` / `plan.RingContentVisible`
> the same way the blips already do. This resolves Knob 1 (surfaces) and Knob 5
> (unify vs split) by **grounding, not guessing**: it's unified, and the surface
> set is whatever the handoff already targets.

This is why the card is **CLEAN and small**: no new surface-arbitration logic, no
new gate, no new art. It rides a decision the codebase already makes 60×/second.

---

## 4. The pulse itself — net-new, but a two-line inline 🔵 GROUNDED (absence)

No easing/breathing helper exists (§0). The pulse is an inline alpha written into
whichever aura `Image.color` is live this frame:

```csharp
// base = the locked baseline alpha (Knob 4); depth k = how far it swings (Knob 3);
// omega = 2*PI*Hz (Knob 3 rate). One shared Time.time phase so every surface that
// could show the aura breathes in lockstep (AT-AURA-NODRIFT).
float s     = 0.5f * (1f + Mathf.Sin(Time.time * omega));   // 0..1
float alpha = baseA * (floor + (1f - floor) * s);           // floor = min-fraction at trough
img.color   = new Color(CSolarRing.r, CSolarRing.g, CSolarRing.b, alpha);
```

Everything in that expression except `CSolarRing` is a **locked knob** (§5). The
look-lock mock (§6) exists to turn `omega`, `floor`, `baseA` into eyeball-chosen
constants before the impl card is cut.

---

## 5. 🟡 The knobs — the GATE (Daniel locks these; do NOT guess)

Two are resolved by §3's grounding; three are genuine eyeball calls. Architect
leans are stated but **Daniel decides**.

| # | Knob | Architect lean (grounded) | Status |
|---|------|---------------------------|--------|
| 1 | **Surfaces** — which homes? | **Resolved by §3:** the aura follows detection. 1a screen-space ring ✅, 1b vanilla minimap rim ✅, **1c carry-disc** = the only real sub-decision (it carries the §2 bezel collision). | 🟢 1a/1b forced; 🟡 1c open |
| 1c | **Carry-disc treatment** | **(a) separate concentric gold `Image`** (no `_bezel.color` collision with the compass). | 🟡 open (a vs b, §2) |
| 2 | **Pulse style** | **steady ambient "breathing"** while worn+charged — it's an at-a-glance "lens is live" cue, not an alert. (Reactive-on-proximity is a different, busier feel; defer unless Daniel wants it.) | 🟡 open |
| 3 | **Rate + depth** | start ~**0.25 Hz** (one breath / 4 s, calm) swinging **α 0.10 ↔ 0.25** around the 0.18 baseline. Converge by eye. | 🟡 open (mock) |
| 4 | **Intensity vs the existing 0.18** | keep the screen-space ring's **0.18 baseline**; the minimap rim **may read brighter** (it competes with map texture, not empty screen). Should the existing empty-ring *also* pulse, or only the minimap rim? Architect lean: **both pulse** (one concept). | 🟡 open |
| 5 | **Relationship to `_emptyRing`** | **Resolved by §3: unify.** One pulsing-gold-aura concept, re-homed by the handoff — not two distinct elements. | 🟢 forced |

➡ **Recommended gate-break (the card's own steer, endorsed):** Starbright renders
the pulsing aura as a short clip at 2–3 rates × 2–3 depths; Daniel locks the look by
eye. He validates visuals by eye and is **colourblind — gold is named by RGBA
(`0.98/0.78/0.36`), never by hue.** A static-image or HTML mock with live Hz/α-depth
sliders is enough to lock Knobs 2–4. §6 is that mock.

---

## 6. The look-lock artifact (how Daniel locks Knobs 2–4 by eye)

Because the gold is named by RGB not hue (colourblind) and the only open knobs are
**rate, depth, baseline**, the lockable artifact is a tiny interactive HTML page:

- A dark canvas with the `RingSprite()`-equivalent gold annulus (`rgba(250,199,92,α)`)
  at the screen-space size **and** a smaller copy on a faux minimap-corner rim.
- Live sliders: **breaths/sec (Hz)**, **trough α**, **peak α**.
- A "freeze + read values" button that prints the exact `omega / floor / baseA`
  constants to paste into the impl-spec.

Daniel drags until it feels right, reads off three numbers, and those become the
locked constants. No GPU client needed for the look-lock (the headless box can't
render Valheim shaders — §8 — but it *can* serve an HTML page Daniel opens on his
GPU client). The in-game AT (§7) is the final confirm, not the lock.

---

## 7. Proposed acceptance tests (the impl-spec will formalize)

- **AT-AURA-RING-PULSE** — lens worn + charged, no minimap → the screen-space
  `_emptyRing` breathes per the locked Hz/depth (visible α swing), not a static ring.
- **AT-AURA-MINIMAP-RIM** — vanilla corner map showing + lens active → a gold
  annulus pulses on the **rim**, north-up, no counter-rotation, sized to the
  small-map diameter.
- **AT-AURA-FOLLOWS-DETECTION** (🟢 the §3 invariant) — the aura re-homes with the
  handoff: ring when no minimap; minimap rim when a minimap owns detection;
  **exactly one home at a time** (rides `plan.MinimapTarget` / `RingContentVisible`).
- **AT-AURA-GATED** — aura shows **iff** lens worn AND `m_durability ≥
  MinChargeToDetect`. Unworn → none (`:236`). Depleted → none/half per the existing
  depleted-hint default (`:246-258`). No aura on a dedicated server (no `Minimap`).
- **AT-AURA-NODRIFT** — one shared `Time.time` phase: whichever surface shows the
  aura, the breath is in lockstep; switching homes mid-breath does not jump phase.
- **AT-AURA-DISC-BEZEL** (🟡 conditional on Knob 1c) — carry-disc home: the lens
  aura (separate gold `Image`, lean (a)) coexists with the Iron Compass iron/bronze
  `_bezel.color` tint **without** either feature stomping the other's channel
  (the §2 collision is resolved, not merely asserted).
- **AT-AURA-CLEAN** (ADR-0001) — no third-party mod code; hooks are base-game
  primitives (`Minimap` public fields) + SBPR-owned types (`SunstoneLensHudOverlay`,
  `MapSurface`, `MapViewer`). Reuses `CSolarRing` + `RingSprite()`; **no new art**.
- **AT-AURA-BUILD** — `dotnet build -c Release` → **0 errors / 0 warnings**
  (`TreatWarningsAsErrors` is on); no regression to the compass north-ring
  (`MapSurface._bezel` iron tint) or the threat-layer blips.

---

## 8. Clean/dirty routing + spec impact

- **CLEAN-SIDE.** Self-contained cosmetic, gated on lens-equipped, riding the
  already-shipped handoff. Reads base-game `Minimap` public fields + SBPR-owned
  Cartography/Sunstone types. No other-mod code read or needed. **SpecCheck +0** —
  render-only, no recipe/piece/station/mechanic change (the `SpecCheck.cs` manifest
  does not move; per AGENTS.md this is a render-only doc-and-code pair).
- **Headless-render caveat (honesty rule).** This box **cannot** render the result
  — Valheim shaders collapse on the headless server. The look-lock is an HTML mock
  Daniel opens on a GPU client (§6); the final AT-AURA-* in-game confirms are
  **Daniel's on a GPU client**, not claimable from here.

**Spec docs that move on the gate (spec-and-code together, AGENTS.md):**

| Doc | Change |
|---|---|
| `docs/design/sunstone-lens-trophy-ring.md` | §1.6 (`:237-247`) + §5.3 (`:436`): the "faint solar ring … ~0.18 alpha … ShowEmptyRing ON" affordance gains the **optional pulse** (static → breathing under `LensAuraPulse`); affordance + default-ON unchanged. |
| `docs/design/sunstone-lens-minimap-handoff.md` | note the aura **re-homes** onto the owning surface's rim instead of vanishing when `_content` hides (`:315`) — added rim element, **no** blip/projection change. |
| **NEW** `docs/v3/planning/sunstone-lens-aura-impl-spec.md` | the buildable spec: the inline pulse (§4) with Daniel's locked `omega/floor/baseA`; the vanilla-minimap rim annulus under `m_pinRootSmall` (north-up, §1.2); the carry-disc treatment per Knob 1c (§2); the `MapViewer`-routed lens-aura setter (two surfaces, §1.3); config knobs (`LensAuraPulse` default ON, rate/depth/baseline); AT-AURA-* (§7). |

➡ On Daniel's look-lock (Knobs 1c, 2, 3, 4), this graduates to the impl-spec above
and an impl card is cut for **engineer-ui**. It waits on nothing — the handoff it
rides is already shipped (PR #214).

---

## Links

- **Reframe target (the ring that already exists):**
  `Features/Sunstone/SunstoneLensHudOverlay.cs` — `_emptyRing` `:164`, `CSolarRing`
  `:101`, `RingSprite()` `:579`, the per-frame driver `Update()` `:222`.
- **Surfaces:** `Features/Sunstone/SunstoneMinimapThreatLayer.cs` (vanilla corner
  map, `EnsureLayer` `:68`); `Features/Cartography/MapSurface.cs` (carry-disc +
  modal bezel, `_bezel` `:157`, `SetCompassNorth` `:235`, tint `:1163`) +
  `MapViewer.cs` (`SetCompassNorth` fan-out `:131-134`).
- **The handoff it rides (NOT modified, only read):**
  [`sunstone-lens-minimap-handoff.md`](sunstone-lens-minimap-handoff.md) (PR #214,
  impl `t_91e86951`); `Features/Sunstone/LensHandoffDecision.cs` (`LensRenderPlan`,
  `LensSurface` `:76`, `MinimapHandoffMode` `:46`).
- **Sibling cosmetic (north, not detection):**
  [`iron-compass-minimap-ring.md`](iron-compass-minimap-ring.md) (PR #226) — same
  "draw a gated cue on the live map surface" shape; **shares the `_bezel.color`
  channel on the carry-disc** (the §2 collision is with this feature).
- **Empty-ring spec being extended:**
  [`sunstone-lens-trophy-ring.md`](sunstone-lens-trophy-ring.md) §1.6 / §5.3.
- Reported via `/bug` thread `ticket-sunstone-lens-aura` (Discord
  `1518410793777893466`). Card `t_acaa0190`. Grounded @ `aa03e21`.
