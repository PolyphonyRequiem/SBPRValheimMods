---
title: "Sunstone Lens → persistent corona aura (lens-live cue on the minimap path) — impl-spec (buildable; engineer-ui)"
status: proposed
purpose: "Buildable spec graduating the SURVIVING half of Daniel's 2026-06-21 /bug aura idea (docs/design/sunstone-lens-aura.md, card t_acaa0190): a faint golden 'lens is live' aura that pulses while the Sunstone Lens is worn+charged. The flat-ring half of that idea died when the world-space sun-corona (t_2d500d45 / PR #254) REPLACED the screen-space _emptyRing it was going to animate. The corona-impl-spec (sunstone-lens-corona-impl-spec.md §6) PARKED the remaining 'aura on a minimap surface' half conditional on Daniel still wanting it; on 2026-06-22 (Discord ticket-diegetic-halo-render) Daniel confirmed: 'implement 241, apply it to the new design if there's value.' This doc is the buildable HOW for that confirmation — re-homed onto the NEW design. INSTEAD of a separate gold annulus on the vanilla-minimap rim / carry-disc bezel (the parked plan, which collided with the Iron Compass bezel tint, corona-spec §2), we DECOUPLE the existing world-space corona from the threat trophies: when a minimap takes over the threat feed (the shipped Sunstone→minimap handoff hides the ring's trophies), the corona KEEPS breathing on the ground around the player instead of going dark with them. The corona becomes the universal 'the lens is watching' cue on every surface; the trophies/blips remain the threat display. One new live config knob (CoronaPersistsOnMinimap, default ON), gated in the engine-free LensHandoffDecision truth table (CI-fenced). Render-only: SpecCheck +0, no new prefab (ADR-0006 N/A — reuses the shipped SunstoneCoronaDisc + SunstoneWorldRing root), clean-side (ADR-0001 — SBPR-owned types only). Daniel gates the impl-spec at doc review AND the final in-game AT on a GPU client (headless cannot render Valheim shaders)."
owner: Daniel (design + merge authority); Starbright (architect + impl — in-session); engineer-ui (impl lane)
design_source: "docs/design/sunstone-lens-aura.md (card t_acaa0190, the SURVIVING minimap-surface half) + the corona that superseded its flat-ring half (docs/v3/planning/sunstone-lens-corona-impl-spec.md, card t_2d500d45 / PR #254)"
supersedes_partial:
  - "docs/design/sunstone-lens-aura.md (t_acaa0190) — the 'pulsing aura on a minimap SURFACE' half (the part the corona-spec §6 PARKED) is now BUILT, but RE-HOMED: not a separate gold annulus on the minimap rim / carry-disc bezel, but the world-space corona itself kept alive on the minimap path. The doc's flat-ring no-minimap half was already superseded by the corona (t_2d500d45). With both halves now resolved, the aura design doc graduates from PROPOSED to fully realized — this is the last card it owed."
---

# Sunstone Lens → persistent corona aura — impl-spec (buildable)

Daniel's original idea ([`../../design/sunstone-lens-aura.md`](../../design/sunstone-lens-aura.md),
card `t_acaa0190`, 2026-06-21 `/bug`), verbatim:

> "when the lens is equipped, a faint golden aura should be present pulsing around
> the outer rim of the minimap (or as the art for the no-minimap ring)."

That idea had two halves. The **no-minimap-ring** half is already shipped — but not
as a flat pulse: Daniel's later `/bug` (`t_2d500d45`, ticket `ticket-diegetic-halo-render`)
redirected it to a **world-space 3D pulsing sun-corona disc** (PR #254), which *replaced*
the flat `_emptyRing` the original aura idea was going to animate. This doc builds the
**other** half — "a pulsing aura on the minimap" — re-homed onto that new design, per
Daniel's 2026-06-22 confirmation in-thread ("implement 241, apply it to the new design
if there's value").

> **Clean-side (ADR-0001):** SBPR-owned types only (`LensHandoffDecision`,
> `SunstoneLensHudOverlay`, `SunstoneCoronaDisc`, `SunstoneWorldRing`). No vanilla
> prefab read as a mutable base, no third-party mod code.
>
> **No ADR-0006 concern:** this card adds **no new GameObject prefab and no new
> surface**. It reuses the *already-shipped* world-space corona (`SunstoneCoronaDisc`)
> and the shared world-content root (`SunstoneWorldRing.EnsureContentRoot`). The only
> new artifact is a boolean output on a pure decision struct + a config knob.
>
> **SpecCheck/manifest impact: NONE.** Render-only; no recipe/piece/station/item change.

## 0. The insight — why this beats the parked "minimap-rim annulus" plan

The corona-impl-spec (`sunstone-lens-corona-impl-spec.md` §6) parked the surviving half
as *"re-home the aura onto the minimap rim / carry-disc bezel when a minimap owns
detection."* That plan had a real cost it flagged itself: a gold annulus on the
**carry-disc bezel** collides with the Iron Compass iron-tint that already writes
`_bezel.color` per-frame (corona-spec §2 / `MapSurface.cs:1244`), and on the **vanilla
corner map** it's a third net-new element fighting for rim space with the off-edge
threat-blip clamp (`t_aab051ae`).

The cheaper, more coherent realization: **the corona is already the "lens is live" cue,
and it already lives in world space where there's no bezel to collide with.** The only
reason it goes dark on the minimap path is an *implementation coupling* — the corona
shares `SunstoneWorldRing`'s scene root, and when a minimap owns detection the overlay
calls `_worldRing.Hide()`, which darkens the whole root (trophies AND corona) together.
**Decouple them:** keep the corona breathing on the minimap path; only the threat
*trophies* hand off to the minimap. The lens then reads "live" on every surface, the cue
is the same diegetic sun-corona everywhere (not a different annulus per surface), and
there is no bezel-colour collision because nothing new is drawn on the minimap at all.

This is the SBPR "one renderer model, consistent everywhere" instinct (the same one
behind `ThreatBlip` → ring/disc/vanilla): the corona is the lens-live cue, full stop.

## 1. The decision change (engine-free, CI-fenced) — `LensHandoffDecision`

`LensRenderPlan` gains a fourth output, `CoronaContentVisible`, INDEPENDENT of
`RingContentVisible`:

| surface \\ state | `RingContentVisible` (threats) | `CoronaContentVisible` (lens-live cue) |
|---|---|---|
| Ring (no minimap) | true (every mode) | **true** (rides the ring's `RenderWorldHalo` path) |
| Minimap, `RingOnly` | true | true |
| Minimap, `Both` | true | true |
| Minimap, `DiscWhenBound` (default) | **false** (threats → minimap) | **true if `CoronaPersistsOnMinimap` (default ON)**, else false |

The rule is one line: `coronaContentVisible = ringContentVisible || coronaPersistsOnMinimap`.
The corona shows whenever the ring's trophies show (it rides the same world-halo path),
OR — when a minimap has taken the threat feed and the trophies are hidden — whenever the
persist knob is on. Knob OFF restores the exact pre-card behaviour (corona dark on the
minimap path), so the change is fully reversible live (the banner-windsock pattern).

This is a pure boolean addition to the **already-CI-gated** `LensHandoffDecision`
truth table — `tests/LensHandoffDecisionTests.cs` pins it with 8 new assertions
(persist-on/off × both minimap surfaces, ring-fallback always-on, the default-overload
default). A future edit that re-couples the corona to the trophies, or that leaves it
dark by default, fails CI.

## 2. The render change — `SunstoneLensHudOverlay.Update` else-branch

When `!plan.RingContentVisible` (a minimap owns detection), the overlay used to do one
coherent hide: `_worldRing.Hide()` (root off → trophies + corona dark) + `_content` off.
Now:

```
_content?.gameObject.SetActive(false);          // legacy screen ring / debug text stays hidden
if (plan.CoronaContentVisible)
{
    _worldRing.ShowRootWithoutTrophies();        // root active, every trophy slot parked
    RenderCorona(player, depletedDim: false);    // FULL live breathing envelope
}
else
{
    _worldRing.Hide();                            // knob OFF → pre-card behaviour (both dark)
}
```

`ShowRootWithoutTrophies()` + `RenderCorona()` is the **exact same mechanism the depleted
hint already uses** (`SunstoneLensHudOverlay.cs:272-273`) — proven, not net-new — except
here the corona breathes at its full live envelope, not the dimmed steady (`hz=0`,
half-alpha) depleted glow. The #209 invariant holds: only world-content children toggle;
the host `_root` carrying the Update pump is never deactivated, so the detection sweep +
the minimap feed keep running underneath.

## 3. Config — one new live knob

`SunstoneLens.CoronaPersistsOnMinimap` (bool, default **ON**), bound in `Plugin.cs`
alongside the other `Corona*` knobs; default const `DefaultCoronaPersistsOnMinimap` on
`SunstoneLensHudOverlay` (single source of truth, mirrored by the bind). Live-flippable
on a joined client, no rebuild. Off = corona dark whenever a minimap owns detection.

## 4. Acceptance tests

- **AT-AURA-PERSIST-MATH** (headless-CI, `tests/LensHandoffDecisionTests.cs`): the truth
  table above — corona persists on the `DiscWhenBound` minimap handoff with the knob ON,
  goes dark with it OFF, always rides the ring fallback, and the convenience overload
  defaults to ON. **Confirmable here; gated in CI.**
- **AT-AURA-PERSIST-BUILD** (headless-CI): clean compile, 0 warnings (TWAE on).
  **Confirmable here.**
- **AT-AURA-LIVE-NOMAP-ON** (Daniel, GPU client): nomap-ON, lens worn+charged, carry-disc
  bound, **zero** hostiles → the sun-corona keeps breathing on the ground around the
  player while the carry-disc is up. Walk near a hostile → its blip appears on the disc;
  the corona keeps breathing (it's the substrate, not the threat). **Daniel's eye.**
- **AT-AURA-LIVE-NOMAP-OFF** (Daniel, GPU client): nomap-OFF, vanilla corner map up, lens
  worn+charged → the corona breathes in the world; threats draw on the corner map.
  **Daniel's eye.**
- **AT-AURA-KNOB-OFF** (Daniel, GPU client): set `CoronaPersistsOnMinimap = false` on a
  joined client → the corona goes dark the moment a minimap owns detection (pre-card
  behaviour), live, no rebuild. **Daniel's eye.**

> **Headless-render caveat (honesty rule):** this box cannot render the result — Valheim
> shaders collapse on the headless server. The MATH + BUILD ATs are CI-gated here; the
> visual LIVE/KNOB ATs are Daniel's in-game confirms on a GPU client. logs-green ≠ playable.

## 5. Clean/dirty routing + ADR notes

**Clean/dirty: CLEAN-SIDE.** All SBPR-authored (`Features/Sunstone/`). **Route
`architect` (this doc) → `engineer-ui` (impl).** **SpecCheck +0** (render-only).

**ADR-0006 (additive):** no new GameObject prefab, no new surface — reuses the shipped
`SunstoneCoronaDisc` + `SunstoneWorldRing` root. The only new artifacts are a struct
field + a config bool. Nothing instantiated, nothing cloned.

## 6. Sibling docs that move in this PR

- `docs/design/sunstone-lens-aura.md` — supersession banner: the surviving
  minimap-surface half is now BUILT (re-homed onto corona persistence, not a rim
  annulus); the doc graduates from PROPOSED to fully realized.
- `docs/v3/planning/index.md` — manifest row for this spec (two-file rule).

## Links

- **Design idea:** [`../../design/sunstone-lens-aura.md`](../../design/sunstone-lens-aura.md) (card `t_acaa0190`).
- **The corona that superseded the flat-ring half:** [`sunstone-lens-corona-impl-spec.md`](sunstone-lens-corona-impl-spec.md) (card `t_2d500d45`, PR #254).
- **The handoff this rides:** [`sunstone-minimap-handoff-impl-spec.md`](sunstone-minimap-handoff-impl-spec.md) (card `t_91e86951`).
- **Implementing card:** `t_7416e5b9` (engineer-ui).
