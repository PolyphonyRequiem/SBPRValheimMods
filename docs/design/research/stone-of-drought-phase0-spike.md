---
title: "Stone of Drought — Phase-0 water-repel spike (findings)"
status: historical
last_updated: 2026-06-16
card: t_5baa81c9
spike_branch: spike/water-repel-drought-t_5baa81c9
spike_commit: a460919
source_of_truth: docs/design/stone-of-drought-feasibility.md
verdict: "OBSERVATION-PENDING — instrument built + logical path validated headlessly; the
  visible-divot verdict (annoying vs showstopper) requires one in-engine look on a graphical
  client, which the build box cannot run. Decision matrix below makes the verdict mechanical
  once the observation lands."
---

# Stone of Drought — Phase-0 water-repel spike (findings)

> 🌱 **THROWAWAY PHASE-0 SPIKE — not a build order, nothing shipped.** This records what a
> disposable Harmony spike proved about the "Stone of Drought" water-repel divot, and hands off
> the one question no headless box can answer to a human with a graphical client. The spike code
> lives on branch `spike/water-repel-drought-t_5baa81c9` (commit `a460919`) and is wired into
> `Plugin.Awake` **only on that branch**. It must never merge to `v1`.

## TL;DR

- **The logical-repel hook is real and tractable.** A single Harmony postfix on vanilla
  `WaterVolume.GetWaterSurface` carves a curved bowl in the *logical* water surface. It **builds
  0 warnings / 0 errors** against the shipped `assembly_valheim.dll`, with the **seabed-floor
  clamp present from the first commit** (the doc's depth limit). This half of the feature is
  confirmed cheap, exactly as `stone-of-drought-feasibility.md` predicted.
- **The visible-divot question is genuinely unanswerable headless** and is the whole reason this
  spike exists. The water you *see* is a separate GPU-driven mesh; the C# postfix cannot move it.
  Whether the untouched visual water over a carved bowl reads as "annoying" or "showstopper" is an
  **eyeball call that needs a joined client.** This box has no Valheim *client* (no
  `valheim.x86_64`, no Steam — only the headless dedicated server), so I built the instrument and
  am handing off the look. **No observation has been fabricated.**
- **One grounding correction to the design doc:** the seabed clamp must use
  `Heightmap.GetHeight` (true world terrain height), **not** `WaterVolume.Depth()` as the doc
  suggested — `Depth()` returns a *normalized [0,1]* value, not a world height. Fixed in the spike;
  the design doc should be patched (see "Correction" below).

---

## What this spike actually is

A disposable instrument, not a feature. One file
(`src/SBPR.Trailborne/Spike/WaterRepelSpike.cs`) containing:

1. **The hook (AC1).** `[HarmonyPatch(typeof(WaterVolume), nameof(WaterVolume.GetWaterSurface))]`
   postfix. For any point within `Radius` of an active test stone it subtracts a **radial cosine
   well** from vanilla's returned logical surface height, deepest at the centre, fading at the rim.
2. **The seabed-floor clamp (AC2), on from commit 1.** After carving, the result is clamped to
   `Heightmap.GetHeight(point) + ε` so the bowl never digs below the actual seabed. This is the
   depth limit expressed as a floor: in shallow water the bowl reaches the seabed (you stand on dry
   ground); in deep water the same subtraction is clamped (only a shallow surface dimple — the
   stone "runs out of power" and can't drain the ocean).
3. **An in-engine observer API (enables AC3).** A `drought` dev console command (registered via a
   `Terminal.InitTerminal` postfix, mirroring the repo's `BannerDiagCommand`) lets a human plant a
   test stone at their feet and live-tune the divot while standing in the water looking at it:

   ```
   drought plant        drop a stone of drought at your feet
   drought off / on     A/B the divot in place without moving (the key comparison)
   drought clamp 0|1    toggle the seabed clamp (watch it try, and fail, to drain deep water)
   drought soft 0|1     swap falloff shape: Hann bell (soft, seamless) vs hold-then-drop (legible wall)
   drought radius <m>   bowl radius (default 8 m)
   drought well <m>     max carve depth at centre (default 6 m)
   drought eps <m>      seabed clearance (default 0.15 m)
   drought status       print current knobs
   ```

   Live-tunable so the observation in step (b) below can sweep shallow→deep and soft→hard in one
   session without rebuilds.

### What I verified headlessly (the honest ledger)

| Claim | How verified | Result |
|---|---|---|
| Hook compiles against the real game | `dotnet build -c Release` vs shipped `assembly_valheim.dll` | ✅ **0 W / 0 E** |
| `GetWaterSurface` is the right method + single overload | decomp index `by_type/ZNet::WaterVolume.txt` line 34 (`:127768`); only one overload → Harmony can't mis-resolve | ✅ grounded |
| Postfix signature matches (instance method) | `(WaterVolume __instance, Vector3 point, ref float __result)`; compile links it | ✅ grounded |
| Seabed source returns *world* height, not normalized | read `Heightmap.GetHeight` (`:109529`) and `WaterVolume.Depth` (`:127831`) bodies | ✅ corrected (see below) |
| Both patch classes registered (no dead patch) | added both `PatchAll` lines before `PatchCheck.Run()` | ✅ wired |
| Visual "Water" prefab really is two systems | `vprefab inspect Water` | ✅ **confirmed** (below) |
| **Does lowering logical surface let you stand on the seabed? (AC3-a)** | needs joined client | ⏳ **OBSERVATION-PENDING** |
| **What does untouched visual water do over the bowl? (AC3-b)** | needs joined client | ⏳ **OBSERVATION-PENDING** |
| **How does the depth-limited bowl read shallow vs deep? (AC3-c)** | needs joined client | ⏳ **OBSERVATION-PENDING** |

> Per the repo's honesty rule ("logs green ≠ playable"): everything above the dashed line is
> **built + compiles**. Nothing is **tested in-game.** The three ⏳ rows are the spike's entire
> reason to exist and are Daniel's to fill.

---

## Grounding: the vanilla water *is* two systems (confirmed, not assumed)

`vprefab inspect Water` on the shipped asset payload confirms the design doc's load-bearing claim
concretely. The vanilla `Water` prefab is:

```
● Water
  ● WaterVolume          script: WaterVolume   collider: Box trigger 64×60×64   ← LOGICAL (our hook)
  ● WaterSurface         mesh: 1025 verts / 1922 tris (subdivided 2×2 grid)     ← VISUAL (GPU shader)
  ● sub_water_opak_thingyu  mesh: Quad   material: water_bottomplane            ← opaque bottom plane
```

The logical volume (which `GetWaterSurface` belongs to) and the visual surface mesh are **sibling
nodes**. The 1025-vertex subdivided grid on `WaterSurface` is exactly what a GPU vertex shader
displaces for waves. Our C# postfix runs on the `WaterVolume` side; nothing we do there moves the
sibling `WaterSurface` MeshRenderer. **That is the invisible-divot problem, proven structurally
before anyone joins a client.** It is why a logical-only carve cannot, on its own, "look" repelled.

---

## Correction to the design doc (clean-side, grounded)

`stone-of-drought-feasibility.md` (lines 78–85) proposes the seabed clamp read the seabed height
from `WaterVolume.Depth(point)` via `m_heightmap`. **That is wrong and would silently mis-clamp:**

- `WaterVolume.Depth(point)` (`:127831`) returns a **normalized [0,1]** value — a bilerp of the
  volume's four `m_normalizedDepth` corners — *not* a world-space seabed height. Clamping a world
  Y-coordinate against a 0–1 number would be nonsense (the clamp would almost always bind at ~0–1 m
  world height, i.e. near sea level, regardless of actual terrain).
- The correct source is **`Heightmap.GetHeight(Vector3 worldPos, out float height)`** (static,
  `:109529`), which returns the **true world terrain height** at that XZ (terrain only — no placed
  objects, no raycast, so the stone can't clamp against itself). The spike uses this.
- `ZoneSystem.GetSolidHeight` (`:98145`) was the other candidate but is a **physics raycast** that
  includes placed objects (including the stone) and is heavier — wrong for "where is the seabed."

**Action:** patch the design doc's "Seabed floor clamp" bullet to cite `Heightmap.GetHeight`
instead of `Depth()` when this idea is greenlit. (Doc-only fix; deferred so this spike ships
nothing per AC6.)

---

## ⏳ The observation Daniel must run (AC3) — exact protocol

~15 minutes on a graphical client. Produces the verdict.

**Setup**
1. Build the spike DLL (already built locally at
   `src/SBPR.Trailborne/bin/Release/SBPR.Trailborne.dll` on the spike branch) into your client's
   `BepInEx/plugins/` — or point r2modman/the dev loader at the spike branch build.
2. Join any world with a shoreline. A console-enabled client (`-console`) is enough; the `drought`
   command is `isCheat:false`, so it works **without** `devcommands`.

**Observe — the three AC3 questions, in order**

- **(a) Stand-on-seabed?** Wade into shallow water at a beach. `drought plant`. Walk into the
  bowl. *Does the player physically drop to / stand on the exposed seabed* (swim state ends, feet
  on ground)? Toggle `drought off` / `drought on` standing in the same spot to feel the
  difference. → answers AC3-a.
- **(b) What does the UNTOUCHED visual water do?** While standing in the carved bowl, *look at the
  water*. Does the visual surface: **fill** the bowl (you're standing in a dry pocket the water
  still visually covers)? **z-fight** at the rim? **clip** through the terrain? Take a screenshot.
  → answers AC3-b, and **this single look decides annoying vs showstopper** (matrix below).
- **(c) Shallow vs deep read.** Walk the planted stone's effect from beach → drop-off (or
  `drought plant` once in waist-deep water, once over a deep channel). Confirm: shallow → dry
  bowl to the seabed; deep → only a shallow surface dimple that *can't* reach the bottom (the
  clamp doing its job). Optionally `drought clamp 0` over deep water to watch it *try* to drain
  the ocean (demonstrates why the clamp is load-bearing). → answers AC3-c.

**Record** the three answers (a/b/c + the screenshot) in this doc's "Observation log" section
below, then read off the verdict from the matrix.

---

## Decision matrix — makes the verdict mechanical once (b) is known

The verdict hinges almost entirely on observation **(b)**. Pre-committed so the call isn't
re-litigated after the look:

| Observation (b): untouched visual water over the bowl… | Verdict on the visible-divot problem | Phase 1 (logical repel + cosmetic stand-in) | Phase 2 (real visual carve) |
|---|---|---|---|
| **Fills the bowl opaquely** — you stand in a dry pocket but it *looks* like solid water above/around you, reads as "underwater room" | **SHOWSTOPPER** for "repels water" framing | Phase 1 **cannot** ship as "repel" — needs a cosmetic stand-in that *hides* local water (scale/disable the local `WaterSurface` tile, or a dry-bowl decal) to read at all | **Mandatory** before ship-quality; the expensive GPU-bound mesh/shader carve. Scope it as its own milestone with an in-engine loop. |
| **Z-fights / shimmers at the rim** but the bowl interior reads mostly dry | **ANNOYING**, not fatal | Phase 1 ships as a playable curiosity; a rim-mask or slightly larger cosmetic dry-bowl hides the seam | Still wanted for polish, but **not** blocking a Phase-1 playtest release |
| **Clips cleanly** — visual water mostly respects the terrain bowl, only minor artifacts | **MINOR** | Phase 1 is close to shippable with a light cosmetic touch-up | **Optional / deferrable** — diminishing returns |

Phase sizing also keys on observation **(a)** and **(c)**:

- If **(a) stand-on-seabed = NO** (logical carve doesn't actually expose walkable ground), the
  whole gameplay premise is **INVALIDATED** and Phase 1/2 are moot — the feature would be cosmetic
  only, which contradicts the locked "GAMEPLAY" answer (doc Q1). *This is the cheapest kill and the
  first thing to check.*
- If **(c) shallow-vs-deep** reads well (clamp convincingly limits depth), the doc's central
  scope-control lever holds and Phase 2 is bounded to the *shallow* visual case (tractable). If the
  clamp reads badly (hard ugly seam at the depth limit), add a soft-falloff-at-depth knob before
  Phase 2 — `drought soft 1` already lets Daniel feel that trade in-engine.

---

## Observation log (Daniel fills this in)

> _Pending the in-engine look. Fill the three answers, drop the screenshot path, then set the
> front-matter `verdict:` to the matrix row that matches._

- **(a) Stand on seabed in the bowl?** _[ YES / NO + note ]_
- **(b) Untouched visual water behavior:** _[ fills / z-fights / clips + screenshot ]_
- **(c) Shallow vs deep read:** _[ note ]_
- **Verdict (from matrix):** _[ SHOWSTOPPER / ANNOYING / MINOR / INVALIDATED ]_
- **Phase 1 / Phase 2 sizing implied:** _[ … ]_

---

## Clean-room note (ADR-0001)

The spike patches base-game `WaterVolume.GetWaterSurface` only and reproduces the divot from
vanilla water internals. It reads **no** third-party mod code; the Rune Magic mod is walled off per
the design doc. All vanilla surfaces (`GetWaterSurface`, `Heightmap.GetHeight`, `Player.m_localPlayer`,
`Terminal.ConsoleCommand`) are grounded against the decomp and link cleanly at compile time.

## Scope discipline (AC6 — nothing shipped)

No recipe, no item, no piece, no `requirements.md` / SpecCheck / manifest / dataset change. The
spike is a throwaway branch + this research note. The only doc-fix this surfaced (the `Depth()` →
`Heightmap.GetHeight` correction) is deliberately **deferred** to a future greenlit build so this
spike ships nothing.
