---
title: "Sunstone Lens → pulsing solar aura — impl-spec (buildable; engineer-ui)"
status: proposed
purpose: "Buildable spec graduating the LOOK-LOCKED design doc docs/design/sunstone-lens-aura.md (card t_acaa0190, PR #235 @ main e4c2100). Daniel look-locked the visual (steady ambient breathing, ~0.25 Hz, alpha ~0.10↔0.30 around the 0.18 baseline, faint floor preserved) and the residual architecture knob 1c (carry-disc bezel collision → resolution (a): a SEPARATE concentric gold Image on its own channel, never _bezel.color). This doc is the buildable HOW: animate the existing empty-state solar ring (_emptyRing/CSolarRing) with an inline Time.time-phased alpha (one shared phase, AT-AURA-NODRIFT), and re-home that same pulsing aura onto whichever surface the SHIPPED LensRenderPlan handoff points at — screen-space ring (no minimap), vanilla corner-map rim (nomap-OFF), SBPR carry-disc (nomap-ON). Every code line re-grounded against main @ e4c2100 (NOT the design doc's stale aa03e21 — the iron-compass-minimap-ring M1 has since SHIPPED, PR #230/#233/#236, making the bezel collision live verified code at MapSurface.cs:1244 and handing us a complete BuildNorthLayer/ApplyCompassNorth/SetCompassNorth template to mirror). Render-only: SpecCheck +0, no new prefab (ADR-0006 N/A), clean-side (ADR-0001 — base-game Minimap public fields + SBPR-owned types, reuses CSolarRing + RingSprite, no new art). This PR also carries the two sibling-doc edits PR #235 deferred (trophy-ring §1.6 optional-pulse note + minimap-handoff re-home note). Daniel gates the impl-spec at doc review AND the final in-game AT on a GPU client (headless cannot render Valheim shaders)."
owner: Daniel (design + merge authority); Starbright (architect — spec); engineer-ui (impl)
design_source: "docs/design/sunstone-lens-aura.md (LOOK-LOCKED, PR #235 @ main e4c2100)"
supersedes_partial:
  - "docs/design/sunstone-lens-trophy-ring.md §1.6 (:237-251) — the 'faint solar ring … ~0.18 alpha … ShowEmptyRing default ON' empty-state affordance gains an OPTIONAL pulse (static → breathing under LensAuraPulse, default ON); the affordance + default-ON are unchanged (this PR, §7)"
  - "docs/design/sunstone-lens-minimap-handoff.md §0/§4 — the handoff hides _content (incl. _emptyRing) when a minimap owns detection; this card RE-HOMES the empty-state aura onto the owning surface's rim instead of letting it vanish (this PR, §7)"
---

# Sunstone Lens → pulsing solar aura — impl-spec (buildable)

The design note ([`../../design/sunstone-lens-aura.md`](../../design/sunstone-lens-aura.md),
**LOOK-LOCKED**, PR #235) is the locked *what*: while the Sunstone Lens is worn and
charged, a faint **golden aura breathes** — the existing empty-state solar ring
(`_emptyRing`), animated, and **re-homed** onto whichever surface the already-shipped
`LensRenderPlan` handoff currently points at. This doc is the buildable *how*. It is the
structural **sibling** of the Sunstone→minimap handoff
([`sunstone-minimap-handoff-impl-spec.md`](sunstone-minimap-handoff-impl-spec.md), card
`t_91e86951`) and the Iron Compass north-ring
([`iron-compass-minimap-ring-impl-spec.md`](iron-compass-minimap-ring-impl-spec.md), card
`t_ed803a83`) — all three render a lens/compass-gated cue onto whichever map surface is
live. This one **rides** the handoff the first already shipped; it adds **no** new
surface-arbitration logic, **no** new gate, **no** new art.

> **Clean-side (ADR-0001):** every vanilla fact cited is base-game (`Minimap` public
> fields, `Time`, `Mathf`, `Player`) — fair to read and adapt. No third-party mod code is
> read or copied. SBPR lines are `main` @ `e4c2100` (every cited line re-grounded against
> current main, NOT the design doc's stale `aa03e21`).
>
> **No ADR-0006 concern:** this card adds **no item prefab**. The aura reuses the existing
> `_emptyRing` `Image` (screen-space), mounts one additive gold annulus under the vanilla
> `m_pinRootSmall` (minimap rim), and one additive concentric gold `Image` on the carry-disc
> (per Knob 1c) — all `new GameObject()` + `Image`, reading no vanilla prefab as a mutable base.
>
> **SpecCheck/manifest impact: NONE.** Render-only; no recipe/piece/station/item change. The
> `SpecCheck.cs` manifest does not move (AGENTS.md render-only doc-and-code pair).

## 0. The look-locked decisions (build constraints)

| Knob | 🟢 Locked value (Daniel) | Build consequence |
|---|---|---|
| Concept | **ONE pulsing gold aura** = the empty-state ring (`_emptyRing`/`CSolarRing`) animated + re-homed via the shipped `LensRenderPlan` | No new art; reuse `CSolarRing` (`:101`) + `RingSprite()` (`:579`). The aura follows detection (§3). |
| Pulse style | **Steady ambient "breathing"** while worn+charged (NOT reactive-on-proximity) | A `Time.time`-phased sine on the aura's alpha, every frame the aura is live (§4). |
| Rate / depth / baseline | **~0.25 Hz** (≈2.6–4 s/breath, calm); alpha breathes **~0.10 ↔ 0.30** around the **0.18** baseline; faint floor preserved; minimap-rim peak may read brighter | The locked `omega`/`floor`/`baseA` constants (§4); engineer fine-tunes **within this envelope**, Daniel's eye confirms final value at the in-game AT. |
| 1c — carry-disc bezel collision | **Resolution (a): a SEPARATE concentric gold `Image` on its own channel.** Do NOT write `_bezel.color` | The Iron Compass already owns `_bezel.color` per-frame (`MapSurface.cs:1244`, shipped). The aura mounts its own annulus — zero channel contention (§5). |

**Architect-proposed defaults (reversible config — Daniel may adjust; NOT his locks):**

| Knob | Architect default | Where |
|---|---|---|
| `LensAuraPulse` (master on/off) | **ON** | §6 config |
| Pulse envelope as live Config (`LensAuraHz`, `LensAuraAlphaFloor`, `LensAuraAlphaPeak`) | **bound live** so Daniel converges the feel on a joined client (the `LensMinimapHandoffMode` precedent) | §6 |
| Depleted-state aura | **off** (matches the existing depleted-hint default OFF; a breathing aura on a dead lens is a false "live" cue) | §2 / §4 |

## 1. File map — what this card builds

```
src/SBPR.Trailborne/Features/Sunstone/
  SunstoneLensHudOverlay.cs   ← EDIT: (a) a small inline pulse helper AuraAlpha(baseA)
                                  computing the Time.time-phased alpha (§4); (b) replace the
                                  3 static CSolarRing writes that drive the SCREEN-SPACE ring
                                  (:166 build-time, :258 depleted-hint, :422 RenderRing) with the
                                  pulsed alpha when LensAuraPulse is on; (c) when a minimap owns
                                  detection (plan.RingContentVisible == false → the :315 hide), drive
                                  the re-homed rim aura on the OWNING surface this tick (vanilla rim
                                  via _vanillaLayer; disc via the MapViewer push). One shared phase.
  SunstoneMinimapThreatLayer.cs ← EDIT: add a single centred gold rim annulus (one pooled Image,
                                  north-up, NO counter-rotate) under the SAME m_pinRootSmall mount the
                                  blips use (EnsureLayer :68); show/pulse it when the vanilla minimap is
                                  the aura's home this tick; hide it in Clear(). Reuses RingSprite()-style
                                  annulus art tinted by Image.color.

src/SBPR.Trailborne/Features/Cartography/
  MapSurface.cs               ← EDIT: (a) a SEPARATE concentric gold aura Image (own channel, NEVER
                                  _bezel.color — Knob 1c). Built additively in BuildNorthLayer's
                                  sibling spot (a new BuildLensAuraRing, mirrors the bezel build at
                                  :1557-1559); child of the NON-rotating _frame (radially symmetric, no
                                  orbit). (b) a SetLensAura(bool worn, float alpha) setter (mirrors
                                  SetCompassNorth :244) that toggles + tints the aura ring; applied
                                  per-frame in ApplyFieldOrientation (:1194) alongside ApplyCompassNorth.
  MapViewer.cs                ← EDIT: a SetLensAura(bool, float) push that forwards to the carry-disc
                                  ONLY (NOT the modal — the modal is not a detection surface, §2).
                                  Mirrors SetCompassNorth :131-135 but single-target.

src/SBPR.Trailborne/
  Plugin.cs                   ← EDIT: bind LensAuraPulse (bool, default ON) + the 3 live envelope
                                  entries (LensAuraHz, LensAuraAlphaFloor, LensAuraAlphaPeak) in the
                                  "SunstoneLens" Config section (mirrors LensRingShowEmpty :394-395).
```

No `SpecCheck.cs` change (render-only; §8). **Build target: 0 warnings / 0 errors**
(`<TreatWarningsAsErrors>` ON). **No new CI unit is strictly required** (the pulse is a pure
inline alpha with no branching policy worth a truth table — unlike `LensHandoffDecision` /
`CompassNorthGate`); if the engineer extracts an `AuraAlpha(t, hz, floor, peakBaseline)` pure
function, a tiny phase/clamp test is welcome but optional. Everything visual is an in-game AT
(logs-green ≠ playable).

## 2. The gate + the grounding — re-grounded against main @ e4c2100

**The per-frame home is already lens-gated.** `SunstoneLensHudOverlay.Update()` (`:222`) runs
every frame the HUD lives. It already early-returns when the lens isn't worn
(`GetEquippedLens(player) == null`, `:234-239`) and when depleted
(`lens.m_durability < SunstoneLens.MinChargeToDetect`, `:243`). So **worn + charged state is
known here per-frame** — the pulse drives from the exact spot that already decides whether the
ring shows at all. **No new gate.**

```
worn?      SunstoneLens.GetEquippedLens(player) != null     (:233-239, early-return if null)
charged?   lens.m_durability >= SunstoneLens.MinChargeToDetect (:243, early-return-ish if below)
```

🔵 **The aura shows IFF worn AND `m_durability ≥ MinChargeToDetect`** — exactly the active-state
gate the ring content already rides. Depleted → no aura (the architect-default; the depleted-hint
path at `:255-258` stays as-is, NOT pulsed — a breathing aura on an inert lens is a false cue).
**No aura on a dedicated server** (`Minimap.instance` is null; the HUD overlay is client-only by
construction — `SunstoneMinimapThreatLayer` header `:37`).

**The three homes — all already arbitrated by the shipped handoff.** `Update()` resolves the
plan at `:279`:

```csharp
LensRenderPlan plan = LensHandoffDecision.Resolve(sbprDiscBound, vanillaMinimapShowing, mode);  // :279
```

`LensSurface` (`LensHandoffDecision.cs:76`) has exactly three members — `Ring`, `SbprDisc`,
`VanillaMinimap`. The aura's home each tick is read off the SAME `plan`:

| Plan state (mode-resolved) | Detection home | Aura home (this card) |
|---|---|---|
| `RingContentVisible == true`, no minimap (`surface == Ring`) | screen-space ring | pulse `_emptyRing` (screen-space) |
| `FeedMinimap`, `MinimapTarget == VanillaMinimap` (nomap-OFF) | vanilla corner map | pulse a rim annulus under `m_pinRootSmall` |
| `FeedMinimap`, `MinimapTarget == SbprDisc` (nomap-ON) | SBPR carry-disc | pulse the separate gold disc annulus (Knob 1c) |

🔴 **CORRECTION to the design doc — the carry-disc setter is single-target, NOT a two-surface
fan-out.** The design doc (§1.3) said route the lens-aura setter through the
`MapViewer.SetCompassNorth`-style fan-out which "hits BOTH `_disc` AND `_modal`." That is correct
for **north** (the compass ring shows on disc AND the full-map modal — Daniel ④) but **wrong for
the aura**: the aura follows **detection**, and the full-map modal is **not a detection surface**.
`LensSurface` has no `Modal` member; the SBPR-side detection home is only ever the carry-disc
(`SbprDisc`). So `MapViewer.SetLensAura` forwards to `_disc` **only** — north and detection are
different invariants on the same surface class. (This is the same "north ≠ detection" split the
iron-compass impl-spec §5 already draws.)

## 3. The re-home model — the aura follows detection, via the SHIPPED handoff

The aura is the empty-state ring **re-homed onto whichever surface `plan` already points at**.
Today, when a minimap owns detection, `_content` is hidden and the empty ring vanishes with it
(`SunstoneLensHudOverlay.cs:315`, `_content?.gameObject.SetActive(false)` — `_emptyRing` is a
child of `_content` at `:159`). The player loses the "lens is live" cue precisely when detection
moves to the minimap. This card fixes that hole: re-home the breathing aura onto the owning
surface's rim instead of letting it vanish.

The driver is the SAME mutually-exclusive feed split the blips already ride (`:321-325`):

```csharp
bool feedDisc    = plan.FeedMinimap && plan.MinimapTarget == LensSurface.SbprDisc;     // :321
bool feedVanilla = plan.FeedMinimap && plan.MinimapTarget == LensSurface.VanillaMinimap; // :322
_feedDiscNow = feedDisc;                                                                // :323
if (feedVanilla) _vanillaLayer.Render(_blips, _blipStyleNow);                           // :324
else _vanillaLayer.Clear();                                                             // :325
```

🟢 **The aura home each tick (the engineer wires this off the existing locals):**

- **`plan.RingContentVisible == true` AND no minimap** (`surface == Ring`) → pulse the
  **screen-space `_emptyRing`** (it's already shown via the `:419-422` `showEmpty` path). This is
  the only home today; the pulse just animates its alpha.
- **`feedVanilla`** → the vanilla corner map owns detection; drive the **rim annulus** on
  `SunstoneMinimapThreatLayer` (north-up, §4.2). The screen ring is hidden (`_content` off, `:315`),
  so the aura lives on the rim.
- **`feedDisc`** → the SBPR carry-disc owns detection; push the aura alpha to the disc via
  `MapViewer.SetLensAura(true, alpha)` (the separate gold annulus, Knob 1c, §5).

🔴 **CORRECTION to the design doc — "exactly one home at a time" is FALSE under `Both` mode.**
The design doc's AT-AURA-FOLLOWS-DETECTION asserts the aura has *exactly one* home per tick. That
holds for `DiscWhenBound` (the default) and `RingOnly`, but **not** for `MinimapHandoffMode.Both`:
`LensHandoffDecision.Resolve` returns `RingContentVisible: true, feedMinimap: true` simultaneously
under `Both` (`LensHandoffDecision.cs:160-162`). In that mode the **screen ring AND the minimap
surface both show threats**, so the aura correctly shows in **both** places (the screen `_emptyRing`
*and* the rim). This is not a bug — it mirrors the blips, which also double-render under `Both`.

> **🟢 The refined invariant (replaces the design doc's over-strong claim):** the aura renders on
> **exactly the surface set the handoff feeds this tick** — one home under `DiscWhenBound`/`RingOnly`,
> two under `Both` — read off `plan.RingContentVisible` (screen ring) ∪ `plan.MinimapTarget` (the
> fed minimap). The aura is wherever detection is, no more and no less. **One shared `Time.time`
> phase** drives every live surface, so under `Both` the screen ring and the rim breathe in lockstep
> (AT-AURA-NODRIFT).

This is why the card is **CLEAN and small**: it rides a feed decision the codebase already makes
60×/second. No new arbitration, no new gate, no new art.

## 4. The pulse itself — a two-line inline alpha (net-new; grounded absence)

🔵 **No easing/breathing helper exists anywhere** (grep-confirmed against `main @ e4c2100`:
`Mathf.Sin(Time` / `PingPong` / breathing-α → zero hits on a UI alpha; the only `Mathf.Sin` calls
are geometric ring-position math at `SunstoneLensHudOverlay.cs:399-401`, and the only "pulse"
strings are the Signs crafting-cost affordance). The pulse is net-new but trivial — an inline
alpha written into whichever aura `Image.color` is live this frame:

```csharp
// One private helper on SunstoneLensHudOverlay, read by every aura surface this tick so they
// share ONE phase (AT-AURA-NODRIFT). baseA = the 0.18 CSolarRing baseline (Knob: AlphaPeak maps
// the crest; AlphaFloor maps the trough). omega = 2*PI*Hz.
private static float AuraAlpha(float baseA)
{
    if (!(Plugin.LensAuraPulse?.Value ?? true)) return baseA;          // master off → static ring
    float hz    = Plugin.LensAuraHz?.Value         ?? DefaultAuraHz;        // ~0.25 Hz
    float floor = Plugin.LensAuraAlphaFloor?.Value ?? DefaultAuraAlphaFloor; // ~0.10
    float peak  = Plugin.LensAuraAlphaPeak?.Value  ?? DefaultAuraAlphaPeak;  // ~0.30
    float s     = 0.5f * (1f + Mathf.Sin(Time.time * (2f * Mathf.PI * hz))); // 0..1, the breath
    return Mathf.Lerp(floor, peak, s);                                       // breathe floor↔peak
}
```

🟢 **Locked envelope (Daniel's mock-approval):** `Hz ≈ 0.25` (≈2.6–4 s/breath, calm),
`AlphaFloor ≈ 0.10`, `AlphaPeak ≈ 0.30`, breathing around the existing **0.18** baseline with the
**faint floor preserved**. The engineer fine-tunes **within this envelope**; Daniel's eye at the
in-game AT sets the final value. The three are **live Config** (§6) so he converges on a joined
client without a rebuild.

🔵 **Where it's written (screen-space ring — the three existing `CSolarRing` writes):**

| Site (`main @ e4c2100`) | Today | This card |
|---|---|---|
| `:166` build-time stamp | `_emptyRing.color = CSolarRing;` | leave as init; the per-frame paths below own the live alpha |
| `:422` `RenderRing` (worn+charged, ring is home) | `_emptyRing.color = CSolarRing;` | `_emptyRing.color = new Color(CSolarRing.r, CSolarRing.g, CSolarRing.b, AuraAlpha(CSolarRing.a));` |
| `:258` depleted-hint (architect default: NOT pulsed) | `… CSolarRing.a * 0.5f` | **unchanged** — depleted aura stays static-dim (a breathing dead lens is a false cue) |

🟡 **Minimap-rim brightness:** the design doc notes the rim peak **may read brighter** than the
screen ring (it competes with map texture, not empty screen). The engineer may scale the rim's
`AlphaPeak` up (e.g. a `LensAuraRimAlphaScale` ×1.3–1.6) — flagged as an in-game tuning call for
Daniel, NOT a separate locked knob. Keep the SAME phase; only the amplitude differs.

## 5. The three surface mounts — build detail

### 5.1 Screen-space ring (no-minimap home) — zero new attach 🔵

The screen ring **is** `_emptyRing` (`:164`), already built, already shown whenever worn+charged
via the `showEmpty` path (`RenderRing`, `:419-422`). The only delta is the alpha source: swap the
static `CSolarRing` write at `:422` for `AuraAlpha(CSolarRing.a)` (§4 table). **No new GameObject,
no new mount.** This is the cheapest home and the one Daniel will see first at the AT.

### 5.2 Vanilla corner-map rim (nomap-OFF home) — one additive annulus 🔵

`SunstoneMinimapThreatLayer` already mounts a custom layer under `Minimap.instance.m_pinRootSmall`
via `EnsureLayer()` (`:68-85`) — the blip pool lives there. Add **one** centred gold rim annulus as
a sibling of the blip pool (same `_layer` parent, or a dedicated child of `m_pinRootSmall`):

```csharp
// In SunstoneMinimapThreatLayer — a single pooled rim Image, built lazily like the blip pool.
// Mounted under the SAME m_pinRootSmall frame the blips use; the annulus is the RingSprite()-style
// procedural ring (a thin gold band). NORTH-UP, NO counter-rotate (the layer header :24-35 lock:
// the vanilla corner map is north-up; SBPR never rotates m_smallRoot / m_pinRootSmall).
private Image? _rim;
internal void RenderRim(float alpha)         // called from the overlay when the vanilla map is the aura home
{
    if (!EnsureLayer() || _layer == null) return;
    if (_rim == null) { /* new GameObject + Image, RingSprite() annulus, raycastTarget=false,
                           sized to the small-map diameter, centred at (0,0) */ }
    _rim.gameObject.SetActive(true);
    _rim.color = new Color(CSolarRing.r, CSolarRing.g, CSolarRing.b, alpha);  // shared phase, §4
}
```

🔴 **Orientation lock (design §1.2 + the layer's own header `:24-35`):** mount the rim **centred,
north-up, NO counter-rotation.** A rim is radially symmetric so orientation is visually moot — but
do NOT add the carry-disc's counter-rotate idiom here (wrong surface). Sized to the small-map
diameter (read off `mm.m_pinRootSmall` / the layer's existing geometry, NOT a literal). `Clear()`
(`:115-119`) must also hide `_rim` so an unequipped/depleted lens leaves no stale rim. **Client-only
by construction** (`Minimap.instance` null on a dedicated server — header `:37`).

### 5.3 SBPR carry-disc (nomap-ON home) — the SEPARATE gold annulus (Knob 1c) 🔴🟢

🔴 **The collision is now LIVE, verified code — not a prediction.** Since the design doc was
grounded (`aa03e21`), the iron-compass-minimap-ring M1 **shipped** (PR #230, tuned by #233/#236).
`MapSurface.ApplyCompassNorth` writes `_bezel.color` **every frame** off the compass gate:

```csharp
// MapSurface.cs:1244 (inside ApplyCompassNorth, called from ApplyFieldOrientation :1217) — SHIPPED.
_bezel.color = _compassWorn ? CIronTint : Color.white;   // the Iron Compass OWNS this channel per-frame
```

If a lens aura also wrote `_bezel.color` (gold) and the player wears **both** the Iron Compass and
the Sunstone Lens, the two features fight for the same pixel each frame — last writer wins, order
incidental. 🟢 **Locked resolution (a): a SEPARATE concentric gold `Image` on its own channel.**
The aura never touches `_bezel.color`; it's its own annulus, just inside/outside the bezel band.

**Build it additively, mirroring the shipped `BuildNorthLayer` (`:1727`) — but on the NON-rotating
`_frame`, not the rotating `_mapContainer`:**

```csharp
// MapSurface — a new BuildLensAuraRing(), called from EnsureBuilt beside BuildNorthLayer() (:1569).
// Unlike the N-glyph (which orbits on _mapContainer), the aura ring is RADIALLY SYMMETRIC, so it
// parents to the FIXED _frame (like the bezel itself, :1557-1559) — no orbit, no counter-rotate.
private Image? _lensAura;
private void BuildLensAuraRing()
{
    if (_frame == null) return;
    var go = new GameObject("lensAura");
    go.transform.SetParent(_frame.transform, false);              // FIXED frame — radial, no spin
    _lensAura = go.AddComponent<Image>();
    _lensAura.sprite = /* a RingSprite()-style gold annulus, sized at/just inside the bezel band */;
    _lensAura.raycastTarget = false;
    _lensAura.color = CSolarRing;                                 // gold; alpha driven per-frame
    _lensAura.gameObject.SetActive(false);                        // gated on by SetLensAura
}

// The setter the Cartography push calls — mirrors SetCompassNorth (:244) but tints the OWN channel.
public void SetLensAura(bool auraOn, float alpha)
{
    if (_lensAura == null) return;
    if (_lensAura.gameObject.activeSelf != auraOn) _lensAura.gameObject.SetActive(auraOn);
    if (auraOn) _lensAura.color = new Color(CSolarRing.r, CSolarRing.g, CSolarRing.b, alpha);
}
```

🔴 **Radius — bind to the helper, never a literal.** Size the annulus off
`DiscRingGeometry.HoleRadius(_cfg.TargetPx)` / `RingOuterRadius` (the same shared source the bezel +
N-glyph use, `BuildNorthLayer:1731`) so it tracks any future margin tune and works at BOTH the disc
(~200 px) and — should detection ever reach it — any other surface scale. **Do NOT hard-code a pixel
radius.** The disc and the full-map modal are the same `MapSurface` class; the aura only mounts the
disc (detection home), but reading the helper keeps it scale-correct regardless.

🟢 **The push path (Cartography-owned, single-target):**

```csharp
// MapViewer.cs — mirrors SetCompassNorth (:131-135) but forwards to the carry-disc ONLY (§2:
// the modal is not a detection surface). Called each frame from SunstoneLensHudOverlay.Update
// when plan feeds the SBPR disc; passed (false, 0) when it doesn't (revert/hide).
public void SetLensAura(bool auraOn, float alpha) => _disc?.SetLensAura(auraOn, alpha);
```

The overlay calls `MapViewer.Instance?.SetLensAura(feedDisc, feedDisc ? AuraAlpha(CSolarRing.a) : 0f)`
each tick (single source of the shared phase). When the disc isn't the home — or the lens is
unworn/depleted — it passes `(false, 0f)` so the disc aura hides (the `StandDownMinimaps` `:335-340`
inert path is the natural call site for the off-state, alongside the existing `_vanillaLayer.Clear()`).

🔵 **No collision anywhere else.** On the screen ring and the vanilla rim there is no shared channel:
the aggro-state tint (`trophy-ring §1.8`) tints the trophy **slots** + star pips, never `_emptyRing`
(verified — `_emptyRing.color` is only ever `CSolarRing`-derived, `:166/:258/:422`). The collision is
**disc-bezel-only**, and resolution (a) sidesteps it entirely.

## 5b. The dead-pump invariant — the aura must NOT reintroduce a host-deactivation path 🔴

The Sunstone overlay already shipped the #209 fix: "ring hides" = toggle the `_content` **child**,
never the host `_root` (which carries the `Update` pump). The aura work touches `_emptyRing.color`,
the `_vanillaLayer`, and a `MapViewer` push — **none** of which deactivate `_root` or any host. Keep
it that way: the pulse is a per-frame **color/alpha** write + an additive child toggle, never a host
`SetActive(false)`. The aura rides the existing pump; it must never gate it.

## 6. Config — the live entries (`SunstoneLens` section)

The Sunstone overlay binds its knobs in `Plugin.cs` under `"SunstoneLens"` (e.g.
`LensRingShowEmpty` at `:394-395`, the live `LensMinimapHandoffMode` enum at `:416-417`). The aura
mirrors that pattern — a master toggle + three live envelope floats so Daniel converges the feel on
a joined client without a rebuild:

```csharp
// Master on/off — when false the aura is the STATIC empty ring (today's behaviour exactly).
LensAuraPulse = Config.Bind("SunstoneLens", "AuraPulse", true,
    "Pulse the faint solar aura while the lens is worn+charged (steady ambient breathing). " +
    "false: the empty-state ring stays static (pre-aura behaviour).");

// The breathing envelope — live so Daniel locks the feel by eye on a joined client.
LensAuraHz = Config.Bind("SunstoneLens", "AuraHz", 0.25f,
    new ConfigDescription("Breaths per second (~0.25 = one calm breath / 4 s).",
        new AcceptableValueRange<float>(0.05f, 1.0f)));
LensAuraAlphaFloor = Config.Bind("SunstoneLens", "AuraAlphaFloor", 0.10f,
    new ConfigDescription("Aura alpha at the trough (the faint floor — keep it low, never 0).",
        new AcceptableValueRange<float>(0.0f, 0.5f)));
LensAuraAlphaPeak = Config.Bind("SunstoneLens", "AuraAlphaPeak", 0.30f,
    new ConfigDescription("Aura alpha at the crest (breathes up to here from the floor).",
        new AcceptableValueRange<float>(0.05f, 0.6f)));
```

🟢 **Defaults are Daniel's look-locked envelope** (`Hz 0.25`, floor `0.10`, peak `0.30`, around the
existing `0.18` baseline). The `Default*` consts live next to the existing `SunstoneLensHudOverlay`
defaults (single source of truth; the §4 helper reads the Config with a const fallback so a
no-Plugin unit context still works). **No new section, no new prefab** — four entries in the
existing `SunstoneLens` config block.

## 7. Sibling docs that move in THIS PR (spec-and-code together — AGENTS.md)

🔴 **CORRECTION to the card's framing:** the card says the trophy-ring + minimap-handoff edits were
"already touched by #235 — verify." They were **not**. PR #235 (`62467db`) touched only
`docs/design/sunstone-lens-aura.md` + `docs/design/README.md` + `docs/design/index.md` (verified via
`git show 62467db --stat`). The two sibling-doc edits are **pending** and ship **here** with the
impl-spec (the same standalone-design-then-graduate path the iron-compass twin followed in #226→impl).

### 7.1 `docs/design/sunstone-lens-trophy-ring.md` §1.6 (`:237-251`)

The empty-state affordance ("faint solar ring … ~0.18 alpha … `ShowEmptyRing` default ON") gains an
**optional pulse**. APPEND a note under §1.6 — the affordance + default-ON are unchanged; the static
ring becomes a breathing one when `LensAuraPulse` is on:

> *"**Pulse (LensAuraPulse, default ON — `sunstone-lens-aura-impl-spec.md`):** the faint solar ring
> breathes (steady ambient alpha pulse, ~0.25 Hz, ~0.10↔0.30 around this 0.18 baseline) while
> worn+charged, and re-homes onto the owning minimap surface's rim when a minimap owns detection
> instead of vanishing with `_content`. The affordance and its default-ON are unchanged; only the
> static→breathing description is superseded. Depleted state stays static-dim (not pulsed)."*

> 🔴 Note the design doc's `supersedes_partial` cites trophy-ring "§5.3 (:436)" — **there is no §5.3**
> (the doc's last section is §5 "Open questions," ending at `:447`; verified via header grep). The
> real target is **§1.6 only**. This impl-spec's frontmatter corrects that reference.

### 7.2 `docs/design/sunstone-lens-minimap-handoff.md`

APPEND a cross-ref note (the handoff hides `_content` incl. `_emptyRing` when a minimap owns
detection, `:315`): the aura **re-homes** onto the owning surface's rim instead of vanishing.

> *"**Aura re-home (`sunstone-lens-aura-impl-spec.md`):** when a minimap owns detection and the ring
> `_content` hides, the lens' empty-state solar aura does NOT vanish — it re-homes as a pulsing rim
> annulus on the owning surface (vanilla corner-map rim, or the SBPR carry-disc's separate gold
> `Image`). Added rim element only; **no** blip/projection change, and the `Both`-mode double-render
> (ring AND minimap) carries the aura to both, matching the blips."*

🔴 **Mechanically (per `sbpr-docs-conventions`): these are APPEND edits, not rewrites** — the
shipped behavioural descriptions stay; the aura is named as the additive extension. The
`SunstoneLensHudOverlay` / `MapSurface` / `MapViewer` code edits and these doc edits land in the SAME
PR (the engineer's impl PR carries both — this impl-spec instructs it).

## 8. Acceptance tests (formalize the design doc §7 AT-AURA-*)

All visual ATs are **Daniel's eye on a GPU client** (logs-green ≠ playable; the headless box cannot
render Valheim shaders — §9). The engineer self-verifies build 0/0 + no regression; Daniel gates the
look.

- **AT-AURA-RING-PULSE** — lens worn + charged, no minimap → the screen-space `_emptyRing` **breathes**
  per the locked envelope (visible α swing ~0.10↔0.30 at ~0.25 Hz), not a static ring. With
  `LensAuraPulse = false` it reverts to the static 0.18 ring (pre-aura behaviour).
- **AT-AURA-MINIMAP-RIM** — vanilla corner map showing (nomap-OFF) + lens active → a gold annulus
  **pulses on the rim**, north-up, **no counter-rotation**, sized to the small-map diameter. Hides
  cleanly when the lens is doffed/depleted (`Clear()` hides `_rim`).
- **AT-AURA-FOLLOWS-DETECTION** (the §3 invariant, **refined**) — the aura renders on **exactly the
  surface set the handoff feeds this tick**: ring when no minimap; the fed minimap's rim when a
  minimap owns detection; **both** under `MinimapHandoffMode.Both` (mirrors the blips). Rides
  `plan.RingContentVisible` ∪ `plan.MinimapTarget` — never a surface the plan didn't feed.
- **AT-AURA-GATED** — aura shows **iff** lens worn AND `m_durability ≥ MinChargeToDetect`. Unworn →
  none (`:236`). Depleted → static-dim hint per the existing default (NOT pulsed). **No aura on a
  dedicated server** (no `Minimap`).
- **AT-AURA-NODRIFT** — one shared `Time.time` phase: every surface showing the aura breathes in
  lockstep (under `Both`, the screen ring and the rim are in phase); switching homes mid-breath does
  not jump phase (the alpha is a pure function of `Time.time`, not a per-surface accumulator).
- **AT-AURA-DISC-BEZEL** (Knob 1c — the live collision) — carry-disc home with **both** the Iron
  Compass and the Sunstone Lens worn: the lens aura (separate gold `Image`) and the compass's iron
  `_bezel.color` tint (`MapSurface.cs:1244`) **coexist without either stomping the other's channel**.
  Doffing the lens hides the aura; the iron bezel persists. Doffing the compass reverts bronze; the
  aura persists. (This is the §5.3 resolution proven, not asserted.)
- **AT-AURA-CLEAN** (ADR-0001) — no third-party mod code; hooks are base-game primitives (`Minimap`
  public fields, `Time`, `Mathf`) + SBPR-owned types (`SunstoneLensHudOverlay`,
  `SunstoneMinimapThreatLayer`, `MapSurface`, `MapViewer`). Reuses `CSolarRing` + `RingSprite()`;
  **no new art.**
- **AT-AURA-BUILD** — `dotnet build -c Release` → **0 errors / 0 warnings** (`TreatWarningsAsErrors`
  on); **no regression** to the compass north-ring (`_bezel` iron tint + N-glyph) or the Sunstone
  threat blips (disc + vanilla rim). The existing `LensHandoffDecision` / `CompassNorthGate` unit
  tests stay green (the aura adds no new branch to either).

## 9. Clean/dirty routing + SpecCheck impact

**Clean/dirty: CLEAN-SIDE.** 🟢 Self-contained cosmetic, gated on lens-equipped, riding the
already-shipped handoff. Reads base-game `Minimap` public fields + `Time`/`Mathf` + SBPR-owned
Sunstone/Cartography types. No other-mod code read or needed. **Route `architect` (this doc) →
`engineer-ui` (impl).**

**SpecCheck/manifest: NONE.** 🔵 Render-only; no recipe/piece/station/item change. The `SpecCheck.cs`
manifest does not move. **Patches: NONE new** — the pulse + re-home live inside the existing
`SunstoneLensHudOverlay.Update` / `SunstoneMinimapThreatLayer` / `MapSurface.ApplyFieldOrientation`
render paths; no vanilla method is patched.

**ADR-0006 (additive):** the rim annulus + the disc aura `Image` are built additively (`new
GameObject()` + `Image`), reading no vanilla prefab as a mutable base. The screen-space home reuses
the existing `_emptyRing`.

**Headless-render caveat (honesty rule).** This box **cannot** render the result — Valheim shaders
collapse under `-nographics`. The look-lock was an HTML mock Daniel approved (design §6); the final
AT-AURA-* in-game confirms are **Daniel's on a GPU client**, the only gate expected past merge.

## 10. Routing / delivery

architect writes this impl-spec → decompose to an `engineer-ui` impl card (multi-file HUD touch +
net-new pulse, NOT a self-implement one-liner) → engineer implements in an isolated worktree, builds
0/0, carries the §7 sibling-doc edits in the same PR → self-blocks `review-required` (no merge creds)
→ merge-tail watcher merges under delegated authority → batch into the next playtest build. **Final
gate = Daniel's in-game AT on a GPU client.**

## Links

- **Design (LOOK-LOCKED):** [`../../design/sunstone-lens-aura.md`](../../design/sunstone-lens-aura.md)
  (card `t_acaa0190`, PR #235 @ main `e4c2100`).
- **The handoff it rides (NOT modified, only read):**
  [`sunstone-minimap-handoff-impl-spec.md`](sunstone-minimap-handoff-impl-spec.md) (card
  `t_91e86951`); `Features/Sunstone/LensHandoffDecision.cs` (`LensRenderPlan`, `LensSurface` `:76`,
  `MinimapHandoffMode` `:46`).
- **Sibling cosmetic (north, not detection) — SHIPPED, source of the live `_bezel.color` collision:**
  [`iron-compass-minimap-ring-impl-spec.md`](iron-compass-minimap-ring-impl-spec.md) (card
  `t_ed803a83`, M1 shipped PR #230/#233/#236); `MapSurface.ApplyCompassNorth` `:1237`,
  `_bezel.color` `:1244`, `BuildNorthLayer` `:1727`, `SetCompassNorth` `:244`, `MapViewer` fan-out
  `:131-135`.
- **Reframe target (the ring that already exists):** `Features/Sunstone/SunstoneLensHudOverlay.cs` —
  `_emptyRing` `:164`, `CSolarRing` `:101`, `RingSprite()` `:579`, `Update()` `:222`, feed-split
  `:321-325`.
- **Surfaces:** `Features/Sunstone/SunstoneMinimapThreatLayer.cs` (vanilla rim, `EnsureLayer` `:68`);
  `Features/Cartography/MapSurface.cs` (carry-disc, `_frame` `:161`, `_bezel` `:166`, `DiscRingGeometry`
  helper) + `MapViewer.cs` (`SetCompassNorth` `:131`).
- **Sibling docs moved in this PR (§7):** [`../../design/sunstone-lens-trophy-ring.md`](../../design/sunstone-lens-trophy-ring.md)
  §1.6, [`../../design/sunstone-lens-minimap-handoff.md`](../../design/sunstone-lens-minimap-handoff.md).
- Card `t_e4a6f559` (graduates `t_acaa0190`). Grounded against `main` @ `e4c2100` (every cited line
  read directly).
