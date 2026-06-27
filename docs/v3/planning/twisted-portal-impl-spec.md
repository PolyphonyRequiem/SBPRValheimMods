---
title: "Twisted Portal — long-range named portal network, food-as-fuel cost model (v3 impl spec)"
status: proposed
purpose: "Build-ready architect spec for the v3 Swamp-tier Twisted Portal: a distinct portal class that teleports even where vanilla portals are blocked (NoPortals), travel range gated by FOOD-AS-FUEL — NO key trinket. DESTINATION model SUPERSEDED 2026-06-27 (card t_3d908685): the prior Model A (same-rune-name pairing) is RETIRED in favor of a LOOK-TO-AIM destination picker (option A, gaze-selected) — stand on a portal, aim the crosshair at a destination portal in the world, a visual indicator + a food-impact PREVIEW show the target, press [Use]/E to commit. Daniel reported the look-to-aim interaction as completely absent in playtest (blocker) and confirmed it IS option A (the selectable picker) with gaze selection, not name-matching. Rune names survive as human-readable aim labels (default), not the pairing key. Server-authoritative long-range resolution is now REQUIRED (a picker must reach destinations beyond the client's ~64-128 m ZDO window — the SurveyorTableTag owner-routed-RPC precedent), and the false 'runs on the OWNER' comment at TwistedPortal.cs:54-58 is swept. A read-only PreviewJump food-impact readout is added (the non-mutating PortalEnergyMath.SolveJump seam already ships), overturning the prior 'no charge readout' line. The overlay flips from informational to an interactive selection surface. UNCHANGED and verified firsthand against origin/main: the shipped food-as-fuel cost engine (PortalEnergyMath/TwistedPortalEnergy), the additive swamp-tint construction, the sbpr_rune_name ZDO slot + owner-write discipline, the NoPortals bypass, and Player.TeleportTo as the move primitive — only the addressing/selection/commit surface moved. This card converts the prior design-revisit-blocked card into a SPEC-PASS that rewrites Q3 + the destination/commit/overlay sections in one PR and decomposes into impl children. The earlier reconciliation context (food-as-fuel, PR #270/#271; the SBPR_TwistedKey economy removed; Q1=coexist) is carried forward intact. Daniel gates the merge (docs-only PR) and the single E-key design-lock line."
supersedes_partial:
  - "This spec's own former §5 (the SBPR_TwistedKey food-charged Trinket durability-battery economy) is REPLACED by food-as-fuel per docs/design/twisted-portal-food-charge.md. No SBPR_TwistedKey item, no EatFood/RemoveOneFood charge postfixes, no DrainEquipedItemDurability prefix. The portal class, rune-name storage, NoPortals bypass, and food-as-fuel cost engine are carried forward unchanged."
  - "The former Q3 DESTINATION model (Model A: a Twisted Portal teleports to the nearest other Twisted Portal that shares its rune name) is SUPERSEDED 2026-06-27 (card t_3d908685) by a LOOK-TO-AIM destination picker (option A, gaze-selected): the player aims the crosshair at a destination portal and presses [Use]/E to commit, with a food-impact preview. Rune names are demoted to human-readable aim labels (default) and are no longer the pairing key. The informational-overlay lock (§7) and the jump-through-ring commit verb (§4.5) are superseded accordingly; server-authoritative long-range resolution becomes REQUIRED (§2). The NAMING UX (TextInput via TextReceiver, §6) and the rune-name ZDO storage slot are carried forward — only their ROLE changes (label, not key) and the E-key that opens rename moves to a secondary gesture."
---

# Twisted Portal — long-range named portal network, food-as-fuel cost model

> 🟥🟥 **SUPERSESSION 2026-06-27 (card t_3d908685): the DESTINATION model is now LOOK-TO-AIM, not Model A name-pairing.**
> Daniel playtested on Niflheim and reported the travel interaction he expects is **completely absent** (severity:
> blocker): *"the model we discussed is 'while standing on the foundation of a twisted portal, aim the crosshair close
> to the target twisted portal in the world, a visual indicator shows the target clearly, as well as the impact to
> food. Press E to commit.' None of that is happening. When standing on a portal, other names are appearing but that's
> it."* He corrected this card's framing explicitly — *"read again and you would know it was A"* — confirming the
> look-to-aim model **IS option A, the selectable destination picker**, with selection by **gaze** (aim the crosshair)
> rather than a list click. This is a **design decision by the authority, not an open question**: name-matching as the
> *destination rule* is retired. What this changes, in this PR:
> - **§1 Q3** — destination model flips from **Model A (same-rune pairing)** → **look-to-aim destination picker (option
>   A, gaze-selected)**. Rune names survive as **human-readable aim labels** (default), not as the pairing key.
> - **§2** — server-authoritative long-range resolution moves from *optional / out-of-v3.0-scope* → **REQUIRED on the
>   redesign** (a picker must surface destinations beyond the client's ~64–128 m ZDO window). The false "runs on the
>   OWNER" comment at `TwistedPortal.cs:54-58` is swept by the impl (grounded below).
> - **§4.4 / §4.5** — the commit verb flips from **jump-through-the-ring** → **aim + press [Use]/E to commit**;
>   `ResolveDestination` changes from *nearest-same-rune* → *the aimed/selected destination*; the overhead jump-through
>   trigger is removed/repurposed.
> - **§5 / §6** — a read-only **`PreviewJump`** food-impact readout is added (the non-mutating `SolveJump` seam already
>   exists), which **overturns §6's "No charge readout"** line (the model makes cost a pre-commit preview).
> - **§7** — the overlay flips from **informational** → **interactive selection surface** (the labels are the aim
>   targets; a selected/aimed-highlight state is added). This overturns §7's "informational, not a picker" lock.
> - **UNCHANGED** (verified firsthand against `origin/main` this pass): the food-as-fuel cost engine (§5 math:
>   `PortalEnergyMath`/`TwistedPortalEnergy`, shipped), the additive construction + swamp-tint kitbash (§4.1–§4.3), the
>   `sbpr_rune_name` ZDO slot + owner-write discipline (§6 storage), the `NoPortals` bypass, and `Player.TeleportTo` as
>   the move primitive. Only the *addressing / selection / commit* surface moved.
>
> **The one genuine fork carried to Daniel (a single design-lock line, NOT a menu — §6):** today [Use]/E opens the rune
> rename; under look-to-aim **E must commit travel**. Architect default for his nod: **keep rune names as aim labels;
> tap-E commits travel to the aimed destination; renaming moves to hold-E / the hover menu.** (If he'd rather drop
> naming entirely, E frees up — but the labels are the aim targets, so read-only labels is the safer default.)
>
> The rest of this document is the prior **food-as-fuel reconciliation (2026-06-24)**, carried forward intact where the
> banner above does not override it. Where the two conflict, **this banner and the sections it names win.**

---

The design is locked across two docs: the *what* of the portal mechanism
([`nomap.md` §7 "Twisted Portal (THE BIG ONE)"](../../design/nomap.md)), and the *what* of the
travel-cost model ([`twisted-portal-food-charge.md`](../../design/twisted-portal-food-charge.md),
**accepted** — Daniel locked food-as-fuel + the Bukeperry reserve 2026-06-24, PR #270 + #271).
The kanban card **t_f9cab392** is the original acceptance shape; this doc was **reconciled to
food-as-fuel** under card **t_c15411b2**. This is the buildable *how*: the multi-prefab
architecture, the vanilla hooks re-verified against the decomp, the named-directory teleport model
that replaces vanilla's 1:1 tag pairing, the **food-as-fuel Portal Energy engine** (no key), named
acceptance tests, the `Features/Portals/` placement, and the SpecCheck manifest impact.

> 🔄 **RECONCILED 2026-06-24 (card t_c15411b2): the cost model is now FOOD-AS-FUEL, not a key.**
> The earlier draft of this spec described an `SBPR_TwistedKey` Trinket whose durability was a
> charge meter (eat food → charge; teleport → spend; Pukeberries → dump-charge). Daniel locked a
> different model: **there is no key.** Teleport range is gated by the food in your belly (**Portal
> Energy** `PE = remaining-food-minutes × a stat-derived tier`), a jump spends PE as food-time
> scaled by distance, and **Bukeperries** are a burnable emergency reserve for the shortfall. §5
> below is the rewritten cost model; the authority for every number is the design doc. The portal
> mechanism (§3–§4, §6), the overlay (§7), and the multiplayer finding (§2) are **unchanged** by
> this — only the cost economy moved.

> **Clean-side note (ADR-0001 — `0001-clean-room-no-jotunn.md`):** every decomp line cited here is
> the base game (`assembly_valheim`), which is **fair game to read and adapt** (repo AGENTS.md + the
> 2026-06-09 clarification). Line numbers are from
> `~/valheim/worldgen-spike/decomp/assembly_valheim.decompiled.cs` (this box) and were **re-grepped
> live during the food-as-fuel reconciliation pass (2026-06-24)** — re-confirm against the build
> assembly if the decomp drifts. `PortalIndicator` / Rune-Magic mods are **reference-only**: their
> *behaviour* (through-terrain portal labels; rune-of-detection) is reproduced from vanilla
> primitives only — no third-party code is read or copied. The named-directory + the food-as-fuel
> Portal Energy mechanics are net-new SBPR fiction.

> **ADR-0006 (load-bearing):** every prefab here is built **additively** (`new GameObject()` +
> `AddComponent`), reading vanilla prefabs only as blueprints via `ZNetScene.GetPrefab` /
> `vprefab inspect`. We do **NOT** `Instantiate` `portal_wood` (it drags `ZNetView` + a
> `PlayerBase` EffectArea + `GuidePoint` + `portal_destruction` — the cairn-soft-lock /
> Explorer's-Bench-GuidePoint bug class). The portal piece reuses the Ancient Portal's proven
> additive shell + grafted-ring kitbash (`docs/v2/planning/ancient-portal-impl-spec.md`).

> 🟢🟢 **STATUS after reconciliation: the cost economy is DECIDED (food-as-fuel, design-locked).**
> Q1 (coexist vs replace) is LOCKED = **coexist**. Q2 (charge economy) is RESOLVED = **food-as-fuel** (merged design
> doc). **Q3 (destination UX) is SUPERSEDED 2026-06-27 (card t_3d908685): the destination model is now the LOOK-TO-AIM
> picker (option A, gaze-selected), NOT Model A name-pairing** — see the supersession banner above and the rewritten
> §1 Q3 / §2 / §4.4–§4.5 / §6 / §7. The old "🔴 BLOCKED ON DANIEL: three design decisions" gate stays retired (Q2 is
> resolved by the merged design doc; Q1 is locked-coexist). The remaining secondary flags (NoBossPortals keep/lift,
> ore-ban keep/lift) are engineer-resolvable in-game and noted per-section, not gates. The **one** new design-lock line
> this pass carries to Daniel is the **E-key collision** (rename vs travel-commit, §6) — a single default for his nod,
> not a menu. If an engineer finds a hard reason to reopen Q1 mid-build, they BLOCK with the specific finding rather
> than guessing (§1).

---

## 0. SpecCheck manifest impact (read first — it moves with the code)

`Runtime/SpecCheck.cs` holds the recipe drift manifest. Under food-as-fuel this feature adds
**+1 entry** (down from the former +3 — **the `SBPR_TwistedKey` item row is REMOVED**, and the
Sunstone row was always pre-existing, not re-added):

| # | Manifest entry | Kind | Resources | Station |
|---|---|---|---|---|
| 1 | `piece_sbpr_twisted_portal` | build piece | **see Q1 / §4.7 — recipe shape depends on coexist-vs-replace** (proposed, Q1 = coexist: `FineWood ×20, GreydwarfEye ×10, SurtlingCore ×4, SBPR_Sunstone ×1`) | (Hammer-placed; `m_craftingStation = null`) |
| — | ~~`SBPR_TwistedKey`~~ | ~~item recipe~~ | **REMOVED — no key trinket under food-as-fuel.** The whole row, its icon-ship requirement, and its `Item`-shape gotcha are gone. | — |
| — | `SBPR_Sunstone` | *(EXISTING — shared v3 material, already a SpecCheck row from the Sunstone Lens; this feature CONSUMES it via the portal recipe, does not re-add it)* | — | — |

> 🔄 **Reconciliation note:** the former manifest listed **+3** rows (a `SBPR_TwistedKey` item row,
> the portal piece row, and a Sunstone caveat). Killing the key drops the item row entirely, so the
> net is **+1 new row** (the portal piece). The Bukeperry reserve adds **no recipe** — Pukeberries
> are a vanilla loot item the feature *reads*, not a craftable; it never touches SpecCheck.

**Resource prefab-name caveats (must match vanilla internal IDs / SBPR consts, or SpecCheck flags a
NULL `m_resItem`) — verified this pass against the wiki corpus `Internal ID` field
(`~/valheim/sbpr-corpus/wiki/fandom/`):**
- `SBPR_Sunstone` = the shared v3 material const `SunstoneLens.SunstoneName` — referenced via the
  const, **never a literal**. The Twisted Portal network is Sunstone's second consumer (after the
  Lens) — the "more than one use" design intent realized. Spec-wires Twisted to depend on the
  Sunstone feature.
- GreydwarfEye = vanilla **`GreydwarfEye`** — already `MarkerSigns.EyeResource`; reference that
  const (the Ancient Portal precedent, `Portals.cs:71`), don't hardcode.
- FineWood / SurtlingCore = vanilla **`FineWood`** / **`SurtlingCore`** (portal-family mats).
- *(`Iron` and `Bloodbag` are dropped from the manifest — they were the proposed KEY recipe
  resources; with no key there is no key recipe. If Daniel later wants the portal piece itself
  Iron-gated to hold true Swamp tier, that's a one-line recipe edit + this row, flagged not built.)*

**The SpecCheck shape (gotcha — same as the Ancient Portal §0):** row 1
(`piece_sbpr_twisted_portal`) is `Piece` only (no `Item`, no `Station`). A `RecipeSpec` with both
`Item` and `Piece` null, or both set, is silently skipped — match the shape exactly. The portal
**piece** uses `m_icon` for the Hammer menu (absent = no thumbnail, non-fatal). **There is no longer
an additively-constructed `Item` shell** (the former key), so the `SpecCheck.CheckIcon` (C1)
boot-ERROR-on-missing-PNG concern that applied to the key **no longer applies** to this feature.

The card that touches `SpecCheck.cs` cites **this doc** alongside the existing sources. Code +
spec + SpecCheck row move in the **same PR** (spec-first rule).

---

## 1. Design questions — Q2 RESOLVED, Q1 LOCKED, Q3 SUPERSEDED (look-to-aim picker)

The card body named three open questions. Q1/Q2 are settled; Q3's *destination* answer was superseded 2026-06-27:

### Q1 — Coexist with, or replace, vanilla/Ancient portals once unlocked? → **LOCKED: COEXIST**
*(card OpenQ 1; also `nomap.md` §7 open #3)*

**Locked to COEXIST (additive, disable nothing)** — the architect's standing recommendation, and
the answer the child portal-core card (t_2b388cd5) is written against. Twisted is a separate prefab
on the Hammer menu; building it does not touch vanilla `portal_wood` or
`piece_sbpr_ancient_portal`. Lowest-risk, most-reversible; keeps the Ancient Portal's "convenience
portal that keeps the ore ban" identity intact (the two are deliberately different tools —
`PIECES_AND_CRAFTABLES.md:442`); matches the Trailborne philosophy of *adding* options, not removing
vanilla ones. **What it changes in the build:** §4.7 is empty (zero gating patches). If an engineer
discovers a hard reason REPLACE is required, they **BLOCK with the specific finding** rather than
silently building a `PieceTable`-filter patch + global-key unlock (a meaningfully larger, more
invasive build that touches vanilla content the rest of Trailborne leaves alone).

### Q2 — Charge economy → **RESOLVED: FOOD-AS-FUEL (no key)**
*(card OpenQ 2 — superseded by the merged design doc)*

🟢 **RESOLVED by [`twisted-portal-food-charge.md`](../../design/twisted-portal-food-charge.md)
(accepted, PR #270 + #271).** The "key durability battery" framing of this question is **void** —
there is no key. The cost model is **food-as-fuel Portal Energy** (§5): each active belly food slot
contributes `PE = remaining_minutes × tier`, a teleport spends PE as food-time scaled by distance,
and Bukeperries are an emergency reserve for the shortfall. The *numbers* (the `/30` tier divisor,
the `[1,5]` clamp, the `28 m` feast cap, `30 m`/berry, the meters-per-PE conversion) are explicit
**tuning knobs** the design doc marks live (playtest may retune them; the architecture is fixed) —
exposed as BepInEx config per the Sunstone Lens precedent. **What it changes in the build:** the
entire §5 (now the PE engine, not a key), §8 (the patch surface **collapses** — food-as-fuel needs
**zero Harmony patches**, where the key needed three), and the SpecCheck manifest (§0, the key row
removed). This is the substance of the t_c15411b2 reconciliation.

### Q3 — Destination UX → **SUPERSEDED 2026-06-27: LOOK-TO-AIM PICKER (option A, gaze-selected)**
*(card OpenQ 3; `nomap.md` §7 — "on-step shows visible portal names"; supersession card t_3d908685)*

> 🟥 **The former answer (Model A: "a Twisted Portal teleports to the nearest other Twisted Portal that shares its
> rune name") is RETIRED as the DESTINATION rule.** Daniel never intended a matching-name requirement, reported the
> look-to-aim interaction as **absent** in playtest (blocker), and confirmed the model **IS option A — the selectable
> destination picker** — with selection by **gaze** (aim the crosshair at the destination) rather than a list click.
> The prior `ResolveDestination` (`SBPR_TwistedPortal.cs:174-243`, verified this pass) does an exact same-rune match
> (`:212 if (theirRune != myRune) continue;`) and an unnamed portal never pairs (`:184`) — that is the gate Daniel is
> rejecting. This is the design authority's call, not an open question.

**The model, verbatim (Daniel, 2026-06-27):** *"while standing on the foundation of a twisted portal, aim the crosshair
close to the target twisted portal in the world, a visual indicator shows the target clearly, as well as the impact to
food. Press E to commit."*

Decomposed into **four beats**, each grounded against shipped code (verified firsthand against `origin/main` this pass)
so the impl child can pick it up cold:

- **Beat 1 — "stand on the portal" = the proximity-active state. ALREADY EXISTS.** The overlay's `nearAPortal` gate
  (`TwistedPortalOverlay.cs`) + `TwistedPortalOverlayModel.DefaultProximityRange = 3f` (`:72`) is exactly "you're on a
  portal." Reuse directly — the overlay-visible state IS the selection-active state.
- **Beat 2 — "aim the crosshair at a destination" = the NOVEL selection mechanism (the heart of this redesign).** Two
  grounded options; the architect default is **(ii)**:
  - *(i) Vanilla hover-raycast* (`Player.FindHoverObject` → `m_hovering`). Clean and standard, BUT it needs a
    line-of-sight collider — you can only select a destination portal you can physically see and that's in collider
    range. A portal behind a hill is unselectable, which **defeats "look at a destination"** on the very terrain the
    through-terrain labels were built for.
  - *(ii) Angular pick among the overlay's candidate set* — nearest portal to the **crosshair direction** among the
    nearest-N the overlay already computes (`TwistedPortalOverlayModel.OverlayCandidate{Distance,HasRune}` `:41`,
    `SelectNearest` `:119`). You aim at the **through-terrain label**, not the portal's collider, so a destination
    behind a hill is selectable. **This is the architect-locked default**; flag only if grounding overturns it. The
    input plumbing has an in-repo precedent: `SeersStoneFieldHost.cs:127-141` does a `GameCamera.instance.transform`
    position+forward read (camera-forward ray) gated on a keypress with Console/Chat-focus guards — mirror that camera
    read for the **angular** pick (compare each candidate's world-direction-from-camera against `camT.forward`), rather
    than inventing input plumbing.
  - 🔴 **This flips the overlay from INFORMATIONAL to INTERACTIVE** (the labels become the selection surface) — a §7
    rewrite, and it **couples to the label-render work** (t_f739451f / its impl child): the labels you aim at must be
    legible and stable, which the constant-on-screen-size fix already delivered look-to-aim-forward.
- **Beat 3 — "visual indicator shows the target clearly" + "the impact to food" = the SELECTED-highlight + PREVIEW.**
  - The *selected/aimed* label gets a distinct highlight state (color/material). The overlay already left per-slot
    label color/material reachable for exactly this (`TwistedPortalOverlayModel.DefaultLabelScale`/material seam, §7.4
    forward-compat note) — no re-architecture needed.
  - The *food-impact preview* is the **cheap beat**: the cost math is already pure and non-mutating.
    `PortalEnergyMath.SolveJump(slots, distanceMeters, knobs)` (`PortalEnergyMath.cs:262`) returns a read-only
    `JumpSolution` (`:128`) carrying `BellyRangeMeters`, `ShortfallMeters`, `BerriesNeeded`, `MinutesRemovedPerSlot` —
    and its own doc-comment states it **"does NOT decide success."** A preview snapshots the belly (the same
    `GetFoods()` → `PeSlot[]` walk `TwistedPortalEnergy.TrySpendForJump` does at `:107-124`) and calls `SolveJump`,
    then renders the result and **STOPS before the debit** (`ApplyFoodTimeDrain`/`RemoveBukeberries`/`ApplyFeelingSick`,
    `:146-153`). Requires a new **read-only `PreviewJump(player, D) → JumpSolution`** sibling of `TrySpendForJump`; the
    math already exists, so this is an I/O wrapper, not new cost logic. **This overturns §6's "No charge readout"** —
    the model makes cost a pre-commit preview (see §6).
- **Beat 4 — "press E to commit" = the COMMIT verb, which collides with rename and replaces the jump-through trigger.**
  - 🔴 **E currently opens the rune-rename dialog.** `SBPR_TwistedPortal.Interact` (`:317-329`) → `RequestRename()` only.
    Under look-to-aim, **tap-E must commit travel** to the aimed destination. This is the one genuine design fork for
    Daniel (§6); architect default: tap-E commits travel, rename moves to hold-E / the hover menu, rune names stay as
    read-only aim labels.
  - 🔴 **The real current commit verb is JUMP-THROUGH-THE-RING, not E.** Travel fires from
    `SBPR_TwistedPortalTrigger.OnTriggerEnter` (`TwistedPortal.cs:411-418`) when the player jumps into the overhead
    ring built by `BuildOverheadTrigger` (`:333`). Under E-commit, the overhead jump-through trigger is **removed or
    repurposed** in the same change (impl default; flag to Daniel only if he wants to keep *both* travel verbs). See
    §4.5.

**Rune names under the new model.** Naming itself is carried forward unchanged (the vanilla `TextInput` rename dialog
via `TextReceiver`, §6) — but its ROLE changes: rune names are now **human-readable aim labels** (so you know what
you're aiming at), **not** the pairing key. Default: keep them. (If Daniel later wants to drop naming entirely, E frees
up for commit with no collision — flagged in §6, not pre-decided.)

> 🟢 **Architect's bottom line (this pass):** the shippable v3.0 is **Q1 = coexist, Q2 = food-as-fuel (design-locked),
> Q3 = look-to-aim destination picker (gaze-selected, option A)** with: selection = **angular-pick among the
> through-terrain candidate set** (default ii), a **selected-highlight + read-only food preview** (the `SolveJump` seam
> already ships), **tap-E commits / rename demoted** (the one Daniel fork), and the overhead jump-through trigger
> retired. Server-authoritative long-range resolution is **REQUIRED** (§2) — a picker must reach destinations past the
> client's ZDO window, which is the work the old spec scoped out *under name-pairing* and is now back in scope because
> the picker (A) was chosen.

---

## 2. 🔴 The multiplayer 300m-proximity fork — now a REQUIRED build axis (look-to-aim promotes it)

> 🟥 **SUPERSEDED FRAMING (2026-06-27, card t_3d908685):** the prior note said "Model A (locked Q3) is what defuses
> it" and the custom-RPC directory was "out of v3.0 scope." **Both are overtaken.** Under the look-to-aim picker
> (option A, chosen by Daniel), the player must be able to **select and travel to a destination beyond the client's
> ~64–128 m ZDO window** — so server-authoritative resolution is **REQUIRED, not optional**. The finding below is
> preserved (citations re-verified this pass) because it is exactly the constraint the redesign must satisfy; only its
> *disposition* changed (defused-by-name-pairing → must-be-built).
>
> 🔴 **Code landmine to sweep in the same impl change — a comment that lies.** `TwistedPortal.cs:54-58` asserts the
> destination walk *"runs on the OWNER … NOT subject to the client's ~64–128 m sector window."* **This is false**
> (verified firsthand this pass): there is **no RPC anywhere in the path** —
> `SBPR_TwistedPortalTrigger.OnTriggerEnter` (`:411-418`) → `SBPR_TwistedPortal.Teleport` (`:84`) →
> `ResolveDestination` (`:174`) all run **synchronously on the local client**, and `ResolveDestination`'s own
> doc-comment (`:164-169`) admits the opposite (*"this walk sees only the ZDOs the CURRENT peer holds … a client …
> would see a short list"*). Anyone reading `:54-58` today is told the feature is multiplayer-correct when it is not.
> The look-to-aim impl that adds real server-authoritative resolution must **delete/correct `:54-58`** in the same
> change so the comment stops lying.

The design doc (`nomap.md` §7) says: *"query `ZDOMan` for all ZDOs of the SBPR prefab hash within
radius (cheap — there's already `ZDOMan.GetAllZDOsWithPrefab` available)."* **That works in
singleplayer and BREAKS on a dedicated server**, and the design doc missed it — exactly as the
Ancient Portal spec caught the `PortalPrefabHash` pairing gotcha that *its* design doc missed.

**Why it breaks (re-verified against the decomp this pass):**
- A client does **not** hold every ZDO in the world. The server sends a client only the ZDOs within
  its **active sector window**: `ZDOMan.CreateSyncList` (`:65244`) → `FindSectorObjects(zone,
  m_activeArea, m_activeDistantArea, …)` (`:65212`, called at `:65252`). The window is purely
  sector/distance-based.
- `ZoneSystem.m_zoneSize = 64f` (`:96263`), `m_activeArea = 1` (`:96258`), `m_activeDistantArea = 1`
  (`:96260`). That is a **±1 active zone (~128 m of full ZDOs) + ±1 distant ring**, and the distant
  ring only carries `m_distant`-flagged ZDOs and is only appended when the near sync list has room
  (`if (toSync.Count < 10)`, `:65261`). A build piece is **not** `m_distant` by default.
- Net: a multiplayer client reliably holds Twisted Portal ZDOs only within **~one to two zones
  (~64–128 m)** of itself — **never a guaranteed 300 m**. So `ZDOMan.GetAllZDOsWithPrefabIterative`
  (`:65497`, the real API — note it is the *iterative* variant; the design doc's
  `GetAllZDOsWithPrefab(int)` name does not exist as written) returns only the *locally-held* subset
  on a client. On a dedicated server the result is silently short.
- **Vanilla portals dodge this entirely:** they never do a client-side range query. Pairing is
  resolved **server-side** in `Game.ConnectPortals` (`:84570`) over the `m_portalObjects` list
  (`:64445`) the server maintains for every prefab whose hash is in `Game.instance.PortalPrefabHash`
  (the add path is `:64704`/`:64706`). Registering our hash there (§4.3) populates the **server's**
  list, not the client's view.

**The resolution options, RE-RANKED for look-to-aim (architect):**

1. 🟥 **Model A (name-tag pairing) — RETIRED as the destination rule (Q3 superseded).** It *would* have routed travel
   server-side over the `m_portalObjects` list, but Daniel rejected same-rune-name matching as the addressing model.
   Kept here only as the record of why server-side resolution is correct: the server holds every Twisted ZDO, so a
   server-side walk is the multiplayer-correct substrate. The look-to-aim picker must inherit that property by a
   different route (option 2), since selection is now by gaze, not by shared name.

2. ✅ **Server-authoritative resolution for the picker — REQUIRED (in v3.0 scope under look-to-aim).** The candidate
   set the player aims at, and the destination they commit to, must include portals **beyond the client's held ZDO
   window**. The repo has the exact precedent: `Cartography/SurveyorTableTag.cs` registers an owner-side persistence
   RPC (`nview.Register<ZPackage>(RpcSurveyData, RPC_SurveyData)` `:86`) and routes a blob owner→client via
   `nview.InvokeRPC(...)` (`:603`), mirroring vanilla `MapTable`'s `MapData` RPC. A Twisted "rune registry" applies the
   same shape: the server/owner maintains the authoritative directory (name → position) over the `m_portalObjects`
   list filtered to our hash (or our own `GetAllZDOsWithPrefabIterative` walk where the full set lives) and RPC-pushes
   the within-range slice (the candidate set) to the stepping client; commit routes the chosen destination's resolution
   through the owner the same way. **This is the work the old spec scoped OUT under name-pairing and is now back IN
   scope because the picker (option A) was chosen.** It is the bulk of the redesign's net-new build — the impl child
   should scope it explicitly and may stage it (local-window candidate set first for the in-game look/feel accept, then
   the RPC directory for true long range) but must not ship a client-window-limited picker as the final state.

3. ❌ **Force `m_distant = true` on the portal ZNetView.** Still rejected (unchanged): the distant ring is gated behind
   `toSync.Count < 10` (`:65261`), is only ±1 distant zone, carries no 300 m guarantee, and marking a teleport-bearing
   build piece `m_distant` has unknown interactions with the portal-hash sync path. Not a real fix; noted so a future
   worker doesn't rediscover it as a "clever" shortcut.

> 🟢 **Architect resolution (this pass):** the look-to-aim picker **requires** server-authoritative candidate-set +
> destination resolution (option 2, the `SurveyorTableTag` owner-routed-RPC precedent). A pure client-side
> `GetAllZDOsWithPrefabIterative` walk is acceptable as a **staging step** for the in-game look/feel accept, but the
> shipped state must reach destinations past the client's ~64–128 m window. The same impl change deletes the false
> `TwistedPortal.cs:54-58` "runs on the OWNER" comment (it describes server-side resolution that doesn't currently
> exist).

---

## 3. Architecture — two prefabs + one tag (the key prefab is GONE)

`Features/Portals/` already exists (the Ancient Portal). Twisted lands in the **same feature
folder**. Under food-as-fuel the build surface **drops the `SBPR_TwistedKey` ItemDrop entirely**:

| Prefab / type | Kind | Role |
|---|---|---|
| `piece_sbpr_twisted_portal` | build `Piece` (Hammer) | The portal. A **distinct class** `SBPR_TwistedPortal : MonoBehaviour, Hoverable, Interactable, TextReceiver` — does NOT inherit `TeleportWorld` (tag-collision avoidance, card AC#1). Carries our own teleport + rune-name + **the PE-debit call** (§4.4 / §5). |
| ~~`SBPR_TwistedKey`~~ | ~~`ItemDrop` (Trinket)~~ | **REMOVED.** Food-as-fuel has no key. Travel is gated by belly Portal Energy, read on demand at teleport time — there is no item to construct, equip, charge, or drain. |
| `SBPR_TwistedPortalTag` | `MonoBehaviour` | Per-instance ZDO discipline (rune-name slot, owner-write, ghost guard) — the `AncientPortalTag` / `CairnTag` precedent. (May be folded INTO `SBPR_TwistedPortal` since that's already a per-instance MonoBehaviour — see §4.1.) |
| `SBPR_Sunstone` | *(shared v3 material)* | Consumed by the portal recipe; already shipped by the Sunstone Lens. |

**Files (mirrors the Ancient Portal layout in `Features/Portals/`):**
- `Features/Portals/TwistedPortal.cs` — `RegisterPrefabs` + `DoObjectDBWiring` for the portal piece
  + the `SBPR_TwistedPortal` class (the distinct teleporter; owns the teleport entry point C2 hooks).
- `Features/Portals/TwistedPortalEnergy.cs` — **the food-as-fuel cost engine (NEW, replaces
  `TwistedKey.cs`).** A mostly **engine-free** PE calculator: `tier(food)`, `PE(player)`, the
  distance→food-time debit, the Bukeperry-shortfall solve, and the `SE_Puke` arrival application.
  Reads `Player.GetFoods()` on demand; **no Harmony patches** (see §5, §8). The pure-math core
  (tier curve, PE sum, berry-shortfall) lands as a link-compiled engine-free truth table (the
  `SunstoneHaloGeometry.cs` / `CompassNorthGate.cs` precedent) so it's CI-gated (AT-PE-MATH).
- `Features/Portals/TwistedPortalOverlay.cs` — the on-step proximity overlay (§7), client-only.
- Wire `TwistedPortal` into `Runtime/Registrar.cs` (after `Portals` / `SunstoneLens`, since Twisted
  consumes Sunstone and reuses Portal helpers). **No patch registration is needed for the cost
  model** — the only thing `Plugin.Awake` must register is whatever the overlay/proximity needs, if
  anything (the portal/teleport/trigger/ZDO/PE machinery is **patch-free by construction**).

**The key architectural distinction from the Ancient Portal (card AC#1):** the Ancient Portal *adds
a real vanilla `TeleportWorld`* (inheriting tag-pairing + ore-ban + the NoPortals check). Twisted
**cannot** — `TeleportWorld.Teleport` hard-enforces `GlobalKeys.NoPortals` (`:123008`), the exact
thing Twisted must bypass. So Twisted **reimplements the small slice of teleport it needs** in
`SBPR_TwistedPortal`, omitting the NoPortals gate, and **debits belly Portal Energy** instead of key
charge. It still registers its prefab hash in `Game.PortalPrefabHash` (§4.3) so the server tracks it
for name-pairing — but the teleport *method* is ours, not vanilla's.

---

## 4. The Twisted Portal piece (`piece_sbpr_twisted_portal`)

> 🔄 The portal **mechanism** in this section is unaffected by the cost-model change — it is carried
> forward intact. The ONE change is §4.4: the teleport now **debits Portal Energy** (food-as-fuel,
> §5) where the old draft "spent key charge."

### 4.1 Construction (ADR-0006 additive — reuse the Ancient Portal shell)
- Build with **`Assets.TryConstructPieceShell("piece_sbpr_twisted_portal", donor, out var go)`** (the
  Ancient Portal path, `Portals.cs:215` — note the current helper is the `TryX(out)` form after the
  null-remediation refactor, t_0234cc42 / PR #187). Use a wood/organic effect donor (`portal_wood`
  read as a blueprint) so hit/destroy/place sounds read as a portal.
- **Visual kitbash** — reuse the Ancient Portal's grafted ring/legs/roots envelope
  (`Portals.BuildLegs` `:245` / `Portals.BuildRoots` `:248` + the grafted `small_portal` ring) but
  **re-tinted / re-themed** to read as "twisted/swamp" rather than "ancient/forest" — a darker,
  sicklier emission. Exact retint is a flagged art-pass detail (AT-GEOMETRY); v3.0 ships the Ancient
  envelope with a swamp tint so it's visually distinguishable at a glance. All grafts are
  mesh-reference, ZNetView-free (ADR-0006).
- **The per-instance class IS the portal's brain.** Unlike the Ancient Portal (which bolts a real
  `TeleportWorld` + a separate `AncientPortalTag`), Twisted's `SBPR_TwistedPortal` MonoBehaviour *is*
  both the teleporter and the ZDO-discipline owner. It implements `Hoverable` (`GetHoverText`
  /`GetHoverName`, decomp interface `:111336`), `Interactable` (`Interact`/`UseItem`, `:111594`), and
  `TextReceiver` (`GetText`/`SetText`, `:54810`). One MonoBehaviour, no `TeleportWorld`.

### 4.2 Fragility / placement (reuse the Ancient Portal's LOCKED choices)
- `WearNTear.MaterialType.Wood`, `m_health` per the Ancient Portal's 300 default unless Daniel
  re-leans (flag — Twisted is endgame, arguably tankier; default to **300** to match, note as a
  tunable). `m_noRoofWear = true` (no rain decay — the Ancient Portal precedent).
- **Hammer-placed, no station** (`m_category = Misc`, `m_craftingStation = null`,
  `Assets.AddOrReplacePieceByName` onto `Assets.GetHammerPieceTable()`) — identical to the Ancient
  Portal and consistent with the design-pillars Hammer-exception already carved for portals
  (`design-pillars.md`).
- **Solid-earth only** (`m_groundOnly = true`, vanilla field `:116132`) — same as the Ancient Portal.
- Build cost: recipe row 1 (§0), Q1 = coexist shape. Rebuilt authoritatively in `DoObjectDBWiring`
  after the materials resolve (the Ancient Portal ordering).

### 4.3 Prefab-hash registration / the s_tag-collision question — RESOLVED by look-to-aim (option b, de-facto)

> 🟥 **Look-to-aim note (2026-06-27).** Under the picker, travel resolution does **not** depend on vanilla's
> `m_portalObjects`/`ConnectPortals` pairing at all — it uses **our own server-authoritative directory** (L2, the
> `SurveyorTableTag` RPC). The shipped code already chose **option (b)** below (its own
> `GetAllZDOsWithPrefabIterative` walk, never writing `s_tag`, verified `SBPR_TwistedPortal.cs:194/:209`), so the
> AT-NO-VANILLA-PAIR `s_tag`-collision concern is **moot by construction** — we never participate in vanilla pairing.
> The subsection is kept for the record; the only live action is "never write `s_tag`," which the shipped code honors.
> Whether to register our hash in `PortalPrefabHash` at all is now a don't-care for travel (it doesn't drive
> resolution); leave it as shipped unless L2 finds a reason to change it.

Register `"piece_sbpr_twisted_portal".GetStableHashCode()` into `Game.instance.PortalPrefabHash`
**exactly as the Ancient Portal does** (`Portals.EnsurePortalHashRegistered`, called at
`Portals.cs:155` — idempotent, null-Game-guarded, re-asserted in `DoObjectDBWiring`). This makes
`ZDOMan` track our portal ZDOs in `m_portalObjects` (add path `:64704`/`:64706`). *(Historically this was framed as
"the server-side substrate for name-pairing"; under look-to-aim it is not on the travel path — see the note above.)*

> 🔴 **But do NOT let vanilla `Game.ConnectPortals` auto-connect our portals on `s_tag`.** Vanilla
> `ConnectPortals` (`:84570`) pairs `m_portalObjects` entries by their `s_tag` ZDO string. Our
> portals must pair on `sbpr_rune_name`, NOT `s_tag` — and our portals must never form a vanilla
> `ConnectionType.Portal` ZDO connection (that's vanilla's 1:1 channel; ours is a name-directory).
> **Mitigation:** never write `s_tag` on a Twisted Portal ZDO. **This is a real interaction to
> verify in-game (AT-NO-VANILLA-PAIR):** confirm two unnamed Twisted Portals do not get
> auto-connected by vanilla's `ConnectPortals` into a spurious `ConnectionType.Portal` link. If they
> do (empty-tag collision), the fix is either (a) seed a unique per-portal sentinel into `s_tag` so
> vanilla never pairs them, or (b) keep our hash OUT of `PortalPrefabHash` and resolve name-pairing
> entirely in our own code over `ZDOMan.GetAllZDOsWithPrefabIterative` (`:65497`) server-side.
> **Architect lean: option (b) is cleaner** — it fully decouples us from vanilla's portal-pairing
> machinery and avoids any `s_tag` games, at the cost of doing our own server-side directory walk
> instead of reusing the `m_portalObjects` list. The engineer picks (a) vs (b) after the in-game
> AT-NO-VANILLA-PAIR check; this is the #1 build-time decision inside the portal-core card.

### 4.4 Teleport — our reimplementation, NoPortals omitted, PE debited (card AC#2 + food-as-fuel)

> 🟥 **SUPERSEDED activation surface (2026-06-27, look-to-aim).** The prior draft activated teleport via the **overhead
> jump-through trigger** and resolved the destination by **nearest-same-rune**. Under the look-to-aim picker: the
> activation surface is **aim + press [Use]/E to commit** (the trigger is removed, §4.5), and the destination is **the
> aimed/selected portal** (§4.4a), not a name match. The teleport *core* below — NoPortals omitted, ore/boss gates,
> the food-as-fuel debit, `Player.TeleportTo` as the move — is **carried forward unchanged**; only how `target` is
> chosen and how the method is *invoked* change.

The teleport core reproduces the minimal slice of `TeleportWorld.Teleport` (`:123002`) we want, deliberately
**omitting the NoPortals check** (`:123008`), and **debiting belly Portal Energy** (§5) instead of any key charge.
Under look-to-aim it is invoked from the **commit keypress** (tap-E while aiming), receiving the already-selected
destination rather than resolving nearest-same-rune:

```
SBPR_TwistedPortal.CommitTravel(Player player, TwistedDestination selected):  # called on tap-E while aiming
   if player == null or selected == null: return
   target = selected                                   # §4.4a — the AIMED destination, not a name match
   D = Vector3.Distance(player.pos, target.pos)        # the jump distance (same as before)
   # NO GlobalKeys.NoPortals check — this is the whole point (AC#2)
   # NoBossPortals: KEEP the boss check (:123013) — flagged for Daniel, default KEEP
   # Ore-ban: KEEP — player.IsTeleportable() (:57606); block "$msg_noteleport" (flag, default KEEP)
   #
   # FOOD-AS-FUEL GATE + DEBIT (§5) — unchanged. (The food PREVIEW already showed this number pre-commit, Beat 3.)
   result = TwistedPortalEnergy.TrySpendForJump(player, D)   # drains belly PE, then berries
   if not result.ok: message result.reason; return           # e.g. "not enough fuel even with berries"
   pos = target.position + (target.rotation * Vector3.forward) * exitDistance + Vector3.up
   player.TeleportTo(pos, target.rotation, distantTeleport: true)      # :20771 — the clean primitive
   if result.burnedBerries > 0:
       player.GetSEMan().AddStatusEffect(SE_Puke.NameHash(), resetTime:true)  # §5.4 — arrive Feeling Sick
```

- **`Player.TeleportTo(pos, rot, distantTeleport)`** (`:20771`) is the clean public primitive —
  owner-RPC-safe, handles the 2 s fade + area-ready wait + floor-find. We do NOT hand-poke
  transforms. Same call vanilla `TeleportWorld.Teleport` ends in (`:123031`).
- **The PE debit + berry-shortfall + Feeling Sick is the cost model (§5).** The shipped seam is `D` (distance, meters)
  in, `{ok, reason, burnedBerries}` out — `TwistedPortalEnergy.TrySpendForJump` (already built). The look-to-aim impl
  must NOT re-derive PE math; it adds a **read-only preview sibling** `PreviewJump(player, D) → JumpSolution` (Beat 3,
  §5) that calls the same non-mutating `PortalEnergyMath.SolveJump` and stops before the debit, for the pre-commit
  food-impact readout. The debit path itself is unchanged.
- **Ore-ban (card "otherwise behaves"): KEEP by default** — call `player.IsTeleportable()`
  (`:57606`) and block with `$msg_noteleport` if carrying ore, exactly like vanilla (`:123018`).
  The card lifts the *NoPortals* restriction, not the *ore* restriction. **Flag for Daniel:** does
  the endgame portal also let ore through? Default **NO** (keep the ban); it's a one-line flip.

#### 4.4a `ResolveDestination()` → **`ResolveAimedDestination()`** — the gaze pick (look-to-aim)

> 🟥 **SUPERSEDED (2026-06-27).** The old `ResolveDestination` returned the **nearest other portal sharing this rune**
> (Model A). Under look-to-aim it returns **the portal the player is aiming at**. Same return shape (position+rotation);
> different selection rule.

- **Selection = angular pick among the candidate set (Beat 2, default ii).** While the player stands on a portal (Beat
  1 proximity-active), each refresh: build the candidate set (the portals the overlay already computes,
  `TwistedPortalOverlayModel.OverlayCandidate` `:41` / `SelectNearest` `:119`, extended to the server-authoritative set
  per §2), read the camera forward (`GameCamera.instance.transform.forward`, the `SeersStoneFieldHost.cs:135` idiom),
  and pick the candidate whose **world-direction-from-the-player** is closest in angle to the crosshair (within an
  aim-cone tolerance, a live BepInEx knob). That candidate is the *aimed destination*; its label gets the
  selected-highlight (Beat 3). Returns its position+rotation on commit.
- **Why angular, not raycast:** aiming at the **through-terrain label** (not the collider) is what lets you select a
  destination behind a hill — the whole point of "look at a destination" on Niflheim terrain. Vanilla hover-raycast
  (option i) is rejected for the destination pick because a line-of-sight collider requirement defeats that. (Hover/use
  reach for the *current* portal's own interactions still uses the normal Interact gate.)
- **Server-authoritative reach (§2, REQUIRED):** the candidate set + the committed destination must include portals
  beyond the client's ~64–128 m ZDO window. The shipped client-side `GetAllZDOsWithPrefabIterative` walk
  (`SBPR_TwistedPortal.cs:194`) is acceptable as a **staging step** for the in-game aim/feel accept, but the final
  state routes through the owner via the `SurveyorTableTag` `Register<ZPackage>`/`InvokeRPC` precedent (§2 option 2).
  The false "runs on the OWNER" comment at `TwistedPortal.cs:54-58` is corrected in this change (§2).

### 4.5 Commit input — **aim + tap-[Use]/E** (the overhead jump-through trigger is RETIRED)

> 🟥 **SUPERSEDED (2026-06-27).** The prior draft committed travel via the **overhead jump-through trigger**
> (`BuildOverheadTrigger` `Portals.cs`→`TwistedPortal.cs:333`; `SBPR_TwistedPortalTrigger.OnTriggerEnter` `:411-418`
> calls `Teleport`). Under look-to-aim, **commit is a keypress** — while aiming at a destination (Beat 2), **tap [Use]
> /E** to call `CommitTravel(player, aimedDestination)` (§4.4). The overhead jump-through trigger is **removed or
> repurposed** in the same change (impl default; flag to Daniel only if he wants to keep *both* travel verbs — jump AND
> aim+E).

- **Input plumbing precedent:** `SeersStoneFieldHost.cs:101-141` is the in-repo pattern — a per-frame check gated on a
  keypress (`Input.GetKeyDown(KeyCode.E)`), guarding against Console/Chat focus (`:117`), reading the camera transform
  for the aim, doing the pick, and acting. Mirror it: gate on the proximity-active state (Beat 1), aim-pick (Beat 2),
  and on tap-E commit. **E-key collision with rename is the one Daniel fork (§6):** default tap-E = commit travel,
  rename → hold-E / hover menu.
- **The C1 cooldown-refund edge (carried forward, still applies):** `Character.TeleportTo` (`:20771`) can return
  `false` without moving (`:20778-:20785`) if `m_teleportCooldown < 2f` or a teleport is in flight — in which case the
  food/berries were spent but no jump happened (§5.9 ⚠). Under E-commit the debit still precedes the move, so the same
  refund-on-false / cooldown-pre-check guard is required at the commit site.
- **No grow timer (unchanged, §4.6):** the portal is live on placement, so commit is available as soon as you stand on
  a placed portal — there is no warm-up gate (only the food-as-fuel check at commit time).

### 4.6 No grow timer (a deliberate difference from the Ancient Portal)
The Ancient Portal's 15 s grow is its planted-seed fantasy. Twisted is *built*, not *grown* — active
on placement. So `SBPR_TwistedPortal` skips the grow lifecycle; the trigger is live once placed
(gated only by the PE check at teleport time, §5). (If Daniel wants a "warm-up" flourish, it's an
easy add later — flag, don't pre-build.)

### 4.7 Vanilla-portal gating (ONLY if Q1 = REPLACE — under the locked Q1 = coexist, this is EMPTY)
Under the **locked Q1 = coexist**, this section is empty — Twisted disables nothing. If Daniel ever
reverses to REPLACE, this is where a `PieceTable`-filter patch hides the vanilla `portal_wood` (and
possibly Ancient Portal) recipe behind a Twisted unlock global-key, plus an AT. Specced as a stub so
the decision has a home; not built under the locked answer.

---

## 5. The cost model — FOOD-AS-FUEL Portal Energy (replaces the TwistedKey entirely)

> 🔄 **This entire section was rewritten in the t_c15411b2 reconciliation.** The former §5 specced
> an `SBPR_TwistedKey` Trinket with durability-as-charge, two `EatFood`/`RemoveOneFood` charge
> postfixes, a `DrainEquipedItemDurability` prefix, and stack-split logic. **All of that is deleted.**
> The authority for everything below is
> [`twisted-portal-food-charge.md`](../../design/twisted-portal-food-charge.md) (accepted). The
> cost-model card is **C2 (t_6e992a30)**.

**The whole model in one line:** how far you can teleport is set by the food in your belly. There is
**no key, no item, no equip slot.** A jump spends food-time scaled by distance, so a long haul both
*costs* provisioning and *lands you depleted*. Bukeberries are an emergency reserve for the overflow.

> 🟥 **Look-to-aim addition (2026-06-27): a read-only `PreviewJump` for the pre-commit food readout (Beat 3).** The
> shipped engine `TwistedPortalEnergy.TrySpendForJump` (`:99`) is compute-AND-debit in one call (snapshot belly →
> `SolveJump` → drain `m_time` + burn berries + apply `SE_Puke`). Daniel's model needs the player to **see the food
> impact BEFORE committing**, so the impl adds a **non-mutating sibling** `PreviewJump(player, D) → JumpSolution`: it
> reuses the exact belly snapshot (`GetFoods()` → `PeSlot[]`, `:107-124`) and the **already-non-mutating**
> `PortalEnergyMath.SolveJump` (`PortalEnergyMath.cs:262`, whose doc-comment states it "does NOT decide success"), then
> returns the `JumpSolution` (`BellyRangeMeters`, `ShortfallMeters`, `BerriesNeeded`, per-slot drain) and **stops before
> any debit**. No new cost math — the seam already exists; this is the read-only I/O wrapper. The overlay (§7) renders
> the result on/under the aimed label. This **overturns §6's "No charge readout"** (the cost is now a pre-commit
> preview). The whisper/no-numbers intent (§5.7) constrains the *block* message, not this *aimed preview* — the preview
> is the deliberate, designed readout Daniel asked for.

### 5.1 The decomp surface — Portal Energy is a pure on-demand READ (no patches, no accumulation)

This is the load-bearing reconciliation finding the engineer must internalize: **food-as-fuel needs
zero Harmony patches.** The old key model patched `EatFood`/`RemoveOneFood` because it had to
*accumulate* charge into an item as food was eaten. Food-as-fuel accumulates nothing — it **reads the
live belly state at teleport time** and mutates it inline. The surfaces (re-verified this pass):

- **`Player.GetFoods()`** (`:17598`) — public; returns the live `List<Player.Food>` (`m_foods`,
  `:15604`; max **3 slots**, enforced at `:17423`/`:17498`). This is the PE read surface. No patch
  needed — it's a public getter on the local player.
- **`Player.Food`** (the slot struct, `:15321`) carries everything PE needs per slot:
  - `m_item` (`:15325`) — the `ItemDrop.ItemData`; its `m_shared.{m_food, m_foodStamina, m_foodEitr}`
    (`:57786`–`:57790`) are the food's **base** stat budget → the **tier** input.
  - `m_time` (`:15327`) — **remaining seconds** on this slot, counted down each second in `UpdateFood`
    (`:17534`, `food.m_time -= 1f`). `remaining_minutes = m_time / 60` → the **PE minutes** input.
  - `m_item.m_shared.m_foodBurnTime` (`:57792`) — the slot's max seconds (the full clock).
- 🔴 **Tier reads the BASE budget, not the decayed live stat.** `UpdateFood` decays the live
  `food.m_health/m_stamina/m_eitr` along a `Mathf.Pow(f, 0.3f)` curve as the slot empties
  (`:17535`–`:17539`). **Do NOT** feed those decayed values into `tier()` — that would double-count
  decay (PE already scales by `m_time`). `tier()` must read the **constant** base budget off
  `m_item.m_shared.m_food + m_foodStamina + m_foodEitr`. The remaining-time factor (`m_time`) is the
  *only* thing that should shrink as a slot depletes. This is the single most important correctness
  note in the cost model — flag it loud for C2.

So `TwistedPortalEnergy` is a plain helper that, on demand, walks `player.GetFoods()`, computes
`PE = Σ (slot.m_time/60 × tier(slot.m_item))`, and (on a jump) shortens `slot.m_time` to spend it.
**No `[HarmonyPatch]` anywhere in the cost model.**

### 5.2 Tier — derived from total stats (the design doc's formula, verbatim)

```
total_stats(food) = m_item.m_shared.m_food + m_foodStamina + m_foodEitr     # base budget
tier(food)        = round( clamp(total_stats / 30, 1.0, 5.0) × 2 ) / 2       # snapped to 0.5 rungs
```

- `/30` = ~30 stat-points per whole tier (slope); `clamp(…,1,5)` = floor/ceiling; `round(×2)/2` snaps
  to **0.5 rungs** so foods fall into legible travel classes. Range `[1.0, 5.0]`.
- **Stat-fallback IS the primary rule.** Every vanilla food's tier is computed; every
  modded/unaccounted food gets the identical treatment for **zero hand-authoring**. An **optional
  explicit override registry** (`foodID → tier`) may later pin mis-priced foods, but is not required
  for the system to function — `TwistedPortalEnergy` ships with the formula only. (Design doc §2; the
  full 60-food PE table is §3 there — do not duplicate it into code, derive it.)
- Eitr counts at full weight (mage foods rank highest for travel) — a locked **tuning knob**, not an
  architecture choice (design doc §6.1).

### 5.3 Portal Energy + the distance debit

```
PE(player) = Σ over active slots:  (slot.m_time / 60) × tier(slot.m_item)        # PE "minutes×tier"
belly_range = PE(player) × METERS_PER_PE                                         # PE → meters
```

A jump of distance `D`:
1. Drain belly PE → covers `belly_range` meters. The drain is applied as **food-time removed from
   slots** (shorten each `slot.m_time` proportionally to the PE spent, weighted by tier so the
   "minutes×tier" accounting stays exact). A `UpdateFood(0f, forceUpdate:true)` (`:17526`) style
   refresh — or letting the next tick recompute — re-derives Max HP/Stamina/Eitr from the shortened
   slots, so the player visibly weakens.
2. `D ≤ belly_range` → done. Arrive depleted (the coupling). **ZERO berries spent.**
3. `D > belly_range` → belly empties; the shortfall is paid by Bukeberries (§5.4).

> 🔴 **`METERS_PER_PE` is a tuning knob the design doc leaves to playtest.** The doc fixes the
> *architecture* (distance drains food-time; long jump → arrive depleted) and the *berry* conversion
> (30 m/berry), but does **not** pin the belly-PE→meters constant. **C2 must NOT invent it from
> nowhere** — derive a defensible baseline from the locked anchors (the 300 m portal ceiling, the
> PE-150 best personal food, the "a full belly is a meaningful but not unlimited range" intent) and
> **expose it as a BepInEx config knob**, OR, if no defensible baseline falls out of the design doc's
> numbers, **BLOCK for Daniel** rather than guess. This is the one genuinely under-specified constant
> in the cost model; the card body for C2 says so explicitly.
>
> ✅ **RESOLVED in C2 (card t_6e992a30): `METERS_PER_PE = 1.0`, exposed as the live config knob
> `TwistedPortal/MetersPerPortalEnergy` (range 0.1–10).** This is a *derived*, not invented, baseline —
> it is the value that makes the locked anchors line up most legibly:
> - **1 PE = 1 m** means a Bukeberry's locked **30 m** reserve is worth exactly **30 PE** of belly, so
>   belly fuel and berry fuel are denominated in the same unit — the "berries extend reach" framing
>   (§5.4) reads as one continuous fuel scale, not two arbitrary ones.
> - The **300 m ceiling** then equals **300 PE**. A *strong* full belly (three premium personal slots,
>   PE ≈ 105–150 each → ~315–450 PE) earns one or two near-ceiling jumps before leaning on berries,
>   while a *modest* belly (PE ~30–120) hits the reserve sooner — exactly the "meaningful but not
>   unlimited range" intent, with provisioning quality (not just quantity) setting reach.
> - It is round and legible, so Daniel can retune in playtest against an obvious mental model
>   (raise → food stretches farther per minute; lower → food costs more per metre). The architecture
>   (distance drains food-time; long jump → depleted) is unchanged by any retune.
>   No BLOCK was needed — a defensible baseline fell straight out of the locked numbers.

### 5.4 Bukeberries — the emergency reserve (vanilla `Pukeberries`)

> **Naming (corpus-verified):** the vanilla item is **`Pukeberries`** (internal id `Pukeberries`,
> `~/valheim/sbpr-corpus/wiki/fandom/Pukeberries.md`; "Bukeperries" is the wiki's spoonerism display
> name). Use the internal id `Pukeberries` in any reference. Drops from Greydwarf shaman (Black
> Forest) + Fuling shaman (Plains), stack 50 @ 0.1 weight.

Bukeberries are a **burnable bonus PE source, spent ONLY for the shortfall** when belly PE can't
cover the jump (design doc §5):

```
shortfall = D − belly_range                          # only reached when D > belly_range
berries_needed = ceil(shortfall / BUKE_METERS_PER_BERRY)     # 30 m/berry baseline
if inventory Pukeberries ≥ berries_needed:
    remove berries_needed Pukeberries; jump succeeds; arrive food-empty AND Feeling Sick
else:
    block the jump ("not enough fuel"); spend nothing
```

- **30 m per berry → 10 berries = 300 m.** Not arbitrary: 300 m is the Twisted Portal's own
  pairing/visible range (`nomap.md` §7). A from-empty, maximum-range jump costs **exactly 10
  berries**, and you can never need more than 10 on one jump (you can't jump farther than 300 m).
- **Berries extend reach, they do not refill you.** They fire only *after* the belly is empty, so the
  "long jump = arrive depleted" coupling is preserved by construction.
- **Decomp:** consume berries with `Inventory.RemoveItem("Pukeberries", n)` (`:56922`) /
  `CountItems("Pukeberries")` (`:56985`) on the player inventory. **No patch** — we read/mutate the
  inventory directly in our teleport code.

### 5.5 The thematic cost — arrive *Feeling Sick* (`SE_Puke`), applied directly (no patch)

When a jump burns Bukeberries, the player **arrives food-empty AND afflicted with vanilla *Feeling
Sick*** — the same `SE_Puke` the berry already carries on consumption (15 s of −100 % health regen,
−100 % stamina regen, −50 % move speed, 1 dmg / 2 s). Pushed past your provisions → land **wrecked**.

- **Decomp:** apply it in our teleport code with `player.GetSEMan().AddStatusEffect(SE_Puke.NameHash(),
  resetTime: true)` (the `SEMan.AddStatusEffect` overloads are `:24339` / `:24381`; `GetSEMan()` is
  `:10644`). `SE_Puke` is the class at `:25312`; while active it calls `Player.RemoveOneFood()`
  (`:17452`) every `m_removeInterval = 1f` second (`:25315`, driver at `:25331`) — i.e. the effect
  itself keeps the belly empty for its duration, reinforcing "arrive depleted." We do **not** patch
  `RemoveOneFood` (the old key model did, to charge the key faster — that hook is **deleted**); we
  just apply the standard effect and let vanilla run it.
- **Design tension, accepted (design doc §5.1):** the debuff lands exactly when a player is most
  likely to berry-jump (a panicked "get home NOW" escape) — arriving with −50 % move / no regen is
  genuinely punishing there. That is the **intended** weight: berry travel is a desperation lever,
  not a convenience one. (The forgiving alternative — food-empty, no debuff — was considered and
  rejected; it made berries too clean a substitute for cooking.)
- **Tuning knob:** the arrival debuff can be shortened/scaled vs the full 15 s vanilla `SE_Puke` if
  playtest finds it too lethal on escape jumps (design doc §6.6) — expose as config; baseline = full
  vanilla `SE_Puke`.

### 5.6 Feasts — separate normalized RANGE clock (slightly under personal meals)

Every vanilla feast is **50 m duration, flat** (encodes *convenience*, not earned power). Feeding raw
50 m into `minutes × tier` would hand feasts a fuel tank 67 % over the 30 m personal ceiling and
flatten progression. **Fix (design doc §4): two clocks that diverge only for feasts:**
- **Buff clock: 50 m, untouched.** Feasts stay excellent combat/sustain food.
- **Range clock: normalized to `FEAST_RANGE_CAP` (~28 m baseline)** — the value fed into the PE
  formula for a feast slot, regardless of its real `m_time`. Feast *range* then progresses through
  the stat-derived tier ladder, not the flat duration. The depletion-coupling (§5.3) still applies.
- **Result:** the best feast (Ashlands gourmet bowl, PE 140) stays **under** the best personal food
  (Marinated greens, PE 150) — the travel-optimal pick is always a personal meal.

> **Feast discriminator (grounded this pass):** vanilla feasts share the **`Feast*` prefab-name
> family** — `vprefab list` shows `FeastMeadows, FeastBlackforest, FeastSwamps, FeastMountains,
> FeastPlains, FeastMistlands, FeastAshlands, FeastOceans` (the 8 biome feasts). C2 detects a feast
> slot by matching `food.m_item.m_dropPrefab.name` against this `Feast`-prefixed set (or the
> equivalent shared-name check) and substitutes `FEAST_RANGE_CAP` for `m_time/60` in the PE term for
> that slot. `FEAST_RANGE_CAP` is a config knob (baseline 28 m; design doc §6.2).

### 5.7 Lore breadcrumb — the ONLY advertisement (localization, not a mechanic)

The Bukeberry-reserve feature is a **whispered feature**: no tutorial, no tooltip math, no UI
readout. The *only* in-game signpost is a **lore breadcrumb in item description text** — the Twisted
Portal (and/or the Pukeberry) hover/description hints that *Greydwarves are known to use these
berries in their own portal-magicks, though nobody really understands how it works.* It is a
**localization-string edit** (`$sbpr_*` keys), kept evocative and **non-explicit** (no numbers, no
"10 berries = 300 m"). Players discover the mechanic by experiment and rumor — that's what makes it
feel arcane (design doc §5.2). This is flavor text owned by C2, not a mechanic.

### 5.8 Open tuning knobs (architecture locked, numbers live) — all BepInEx config

Per the design doc §6, these are dials for playtest, **not** architecture reopenings — expose each as
BepInEx config (the Sunstone Lens precedent): `METERS_PER_PE` (§5.3, the one under-specified
constant), `BUKE_METERS_PER_BERRY` (30), `FEAST_RANGE_CAP` (28), the `/30` tier divisor + `[1,5]`
clamp, the eitr weighting, the `SE_Puke` arrival-debuff scale, and shaman drop rate (vanilla). Encode
the locked baselines as a **boot-time runtime assertion** (the SpecCheck/watchdog pattern) so a
silently-drifted config default screams on boot (AT-PE-MATH covers the pure-math core).

### 5.9 C2 build notes (card t_6e992a30 — what shipped, and the grounding corrections)

> ✅ **BUILT 2026-06-25.** The cost model is implemented as the §3 split: the pure math is the
> engine-free `Features/Portals/PortalEnergyMath.cs` (CI-gated by `tests/PortalEnergyMathTests.cs`,
> AT-PE-MATH — 58 cases derived from the design-doc §3/§5 worked numbers, link-compiled into the net8
> test project the same way `SunstoneHaloGeometry`/`CompassNorthGate` are), and the engine I/O is
> `Features/Portals/TwistedPortalEnergy.cs` (the C1 seam body, now the real debit). Six live
> `TwistedPortal/*` BepInEx knobs (`MetersPerPortalEnergy`, `FeastRangeCapMinutes`,
> `BukeberryMetersPerBerry`, `TierDivisor`, `TierClampLo`, `TierClampHi`), resolved via
> `TwistedPortalEnergy.ResolveKnobs()` with `?.Value ?? PortalEnergyMath.Default*` (the no-Plugin unit
> fallback). A `SpecCheck.CheckPortalEnergyManifest` boot assertion diffs the locked §6 baselines
> against the `Default*` consts AND the two derived anchors (300 m ÷ 30 m/berry == 10; tier(143)==5.0,
> tier(27)==1.0). Build `-c Release` → **0/0**. **Patch-free** (PE read on demand from `GetFoods()`;
> no Harmony patch — §8 holds). The lore breadcrumb (§5.7) ships in the portal `m_description`.

**Two grounding corrections vs the seam's decomp hints (re-verified against `assembly_valheim` this
pass — the hints were close but imprecise; the build follows the decomp, not the hint):**

1. **Bukeberry count/remove matches on the PREFAB name, not the vanilla string overloads.** §5.4 cites
   `Inventory.RemoveItem("Pukeberries", n)` / `CountItems("Pukeberries")`. Those overloads
   (`Inventory.CountItems(string)` :56985, `RemoveItem(string,int,…)` :56938) match on
   **`m_shared.m_name`** — the *localization token* `$item_pukeberries`, **not** the prefab name
   `Pukeberries` — so passing the prefab name silently matches nothing. C2 therefore enumerates
   `Inventory.GetAllItems()` (:57227) and matches each item's **`m_dropPrefab.name`** (stripped of any
   `(Clone)`), then removes via the by-reference `RemoveItem(ItemData,int)` (:56922). This is the same
   `m_dropPrefab.name` discipline the equipped-accessory detection uses elsewhere in the repo.
2. **The lore breadcrumb is plain English, NOT a `$sbpr_*` token.** §5.7 (and the design doc §5.2)
   describe the hint as a `$sbpr_*` localization-string edit. **This repo has no localization-
   registration layer** (no `Localization.AddJson`/`AddWord` anywhere — confirmed; the C1
   `SBPR_TwistedPortal` / `SurveyorTableTag` center messages all use plain English for exactly this
   reason), so a `$sbpr_*` token would render on-screen as a literal `[sbpr_...]`. The breadcrumb is
   written as evocative, non-explicit plain-English description text instead — faithful to the design
   intent (a whispered, no-numbers hint), just via the only string surface the repo actually localizes
   cleanly. *(If a `$sbpr_*` JSON layer is ever added, the hint can migrate to a token then — flagged,
   not built.)*

> ⚠️ **One C1-seam ordering edge case observed (flagged for review — it lives at C1's call site, not in
> C2's math):** `SBPR_TwistedPortal.Teleport` (C1) calls `TwistedPortalEnergy.TrySpendForJump` (which
> **debits**) *before* `player.TeleportTo` (:20771). `Character.TeleportTo` can return `false` without
> moving the player if `m_teleportCooldown < 2f` or a teleport is already in flight (:20778–:20785) —
> in which case the food/berries were spent but no jump happened. In normal play the cooldown is
> satisfied (you're not teleporting twice in 2 s), so this is an edge case, not a hot path; but the
> clean fix is for C1 to check `TeleportTo`'s return and refund on `false`, OR gate the spend on a
> cooldown pre-check. **Left for C1 to resolve** (C2 must not re-derive the teleport per the seam
> contract); noted here so it isn't rediscovered as a "berries vanished but I didn't jump" report.

---

## 6. Rune-name storage (`sbpr_rune_name` ZDO slot — carried forward; ROLE changes under look-to-aim)

> 🟥 **Look-to-aim role change (2026-06-27).** The ZDO slot + owner-write discipline below are **carried forward
> unchanged** (the storage mechanism is correct and shipped). What changes: rune names are now **human-readable aim
> labels** (so you know what destination you're aiming at, §7), **not** the pairing key — and the **E-key that opens
> rename is demoted** (E now commits travel, §4.5). Two bullets below (Naming UX, Hover text) are updated for this; the
> rest is intact.

- **A dedicated ZDO string slot, SEPARATE from `s_tag`** — `m_zdo.Set("sbpr_rune_name", name)` /
  `GetString("sbpr_rune_name")`. The card names this slot explicitly. Keeping it off `s_tag` is what
  prevents vanilla's tag machinery (and vanilla portals) from ever connecting a Twisted Portal by tag
  collision (§4.3). **LOCK the key string `sbpr_rune_name`** — a rename orphans every named portal's
  ZDO (the `SBPR_PortalPlantTime` lock lesson).
- **Owner-write discipline** (the `CairnTag`/`AncientPortalTag` precedent): writes guard on a live
  ZDO (ghost = no-op), claim ownership before writing (`if (!nview.IsOwner()) nview.ClaimOwnership()`),
  then `Set`. Reads are free. The `SBPR_TwistedPortal` class owns this — fold the ZDO discipline in
  rather than a separate `…Tag` unless the class grows unwieldy (§3 noted both options).
- **Naming UX** = the vanilla `TextInput` dialog (carried forward), but the **opening gesture is demoted off tap-E**
  under look-to-aim (tap-E now commits travel, §4.5). Default: **hold-E** (or a hover-menu entry) opens the rename box;
  `Interact(human, hold, alt)` routes the hold/alt branch to `TextInput.instance.RequestText(this, "Set portal rune",
  15)` and `TextReceiver.SetText` writes the slot. 15-char cap (flag the exact cap, default 15). 🔴 **This is the one
  Daniel design-lock fork** (E-key sharing): tap-E = commit travel, hold-E = rename is the architect default for his
  nod; if he'd rather drop naming entirely, tap-E is free and rename disappears (but read-only labels are the safer
  default since the labels are the aim targets). Today `SBPR_TwistedPortal.Interact` (`:317-329`) maps **all** [Use]/E
  to `RequestRename` — that mapping is what changes.
- **Hover text** shows the rune name (the aim label) + the naming affordance: `GetHoverText()` returns the localized
  rune name + the rename key hint (now "[Hold E] Set rune" per the demote) + (optionally) "[E] Travel" when aiming.
  `GetHoverName()` returns "Twisted Portal". 🟥 **The old "No charge readout" note is OVERTURNED (2026-06-27):** under
  look-to-aim the **food-impact preview IS shown** — but on the *aimed destination label* (§7 / Beat 3), driven by the
  read-only `PreviewJump` (§5), **not** in this per-frame hover (`GetHoverText` is re-read every frame and must stay
  cheap — `:301-306` deliberately avoids the ZDO walk there). The preview surface is the overlay, not the hover.
- **CensorShittyWords / UGC filter:** vanilla portal tags run through `CensorShittyWords.FilterUGC`
  (`:123045`). Mirror it for rune names so a server's profanity policy applies (the Surveyor's Table
  naming did this) — flag as a nicety, not a blocker.

---

## 7. The on-step overlay — now the INTERACTIVE selection surface (was informational)

> 🟥 **SUPERSEDED (2026-06-27, look-to-aim).** The prior draft locked the overlay as **informational, not a destination
> picker** (under Model A). Daniel's look-to-aim model makes the overlay's labels the **selection surface** — you aim
> the crosshair at a through-terrain label to pick a destination, the aimed label highlights, and the food-impact
> preview renders on/under it; tap-E commits (§4.5). The through-terrain world-space render (Route B) is **carried
> forward and is now load-bearing** (the labels you aim at must render through hills). What's added: an
> aimed/selected-highlight state, the food preview readout, and the server-authoritative candidate set (§2). The
> render-defect fixes (§7.4) already shipped look-to-aim-forward.

### 7.1 What it shows
When the local player stands on (or near) a Twisted Portal, an overlay shows nearby Twisted Portals
by rune name, **rendered through terrain** so labels are visible behind hills. Under look-to-aim the
labels are also the **aim targets**: the one the crosshair is pointing at (angular pick, §4.4a) gets a
**selected-highlight**, and the **food-impact preview** (`PreviewJump`, §5 / Beat 3) renders on or under
that highlighted label. The other labels stay in their normal (unselected) state.

- **Route B — world-space through-terrain labels (the v3.0 deliverable, C3 — BUILT).** A world-space
  `Canvas` per nearby portal with a `UI.Text` rune label (+ optional distance) rendered with a
  **ZTest Always** material so it shows *through* terrain (the `nomap.md` §7 "ZTest-Always shader,
  prefab-only" note). The most bespoke UI in the mod — which is why C3 is **intentionally LAST** in
  the build chain and is a **visual eyeball accept** that's Daniel's (build-green is the floor, "does
  it read right through terrain" is the in-game gate). Reference `BugattiBoys/PortalIndicator` for the
  *pattern* only (clean-room: read for API shape, copy zero gameplay code — ADR-0001).
  **Shipped files:** `Features/Portals/TwistedPortalOverlay.cs` (the render — a `Hud.Awake`-postfix
  pump mounted under `Hud.m_rootObject` that toggles a WORLD-SPACE billboarded label field, the
  `SunstoneLensHudOverlay`/`SunstoneWorldRing` #209 pump-stays-alive pattern; the ZTest-Always
  material is a non-destructive clone of the default UI material with `unity_GUIZTestMode = Always`)
  + `Features/Portals/TwistedPortalOverlayModel.cs` (the engine-free nearest-N / radius / unnamed-skip
  / distance-format policy, CI-gated in `tests/TwistedPortalOverlayModelTests.cs` — the
  `SunstoneHaloGeometry` link-compile precedent). Through-terrain is a **live BepInEx toggle**
  (`TwistedPortalOverlay.ThroughTerrain`) so Daniel A/B's see-through-hills vs honest-depth in-world.
- **Route A — HUD overlay (the fallback, NOT the commissioned route).** A list under
  `Hud.instance.m_rootObject` showing rune name + distance + bearing — NoMap-safe, client-only, the
  Sunstone Lens precedent. Documented here as the lower-risk alternative **if** the through-terrain
  labels prove too costly in playtest, but C3's scope is Route B. If the engineer-ui worker finds
  Route B unviable, they fall back to Route A and flag it — they do not silently swap.

### 7.2 Data source + the 300m multiplayer caveat (from §2)
- The overlay queries nearby Twisted Portals via `ZDOMan.GetAllZDOsWithPrefabIterative` (`:65497`) for
  our prefab hash, filtered to within `OverlayRadius` (300 m) of the player, reading each one's
  `sbpr_rune_name`.
- 🔴 **Per §2, on a dedicated server the client only holds portal ZDOs within ~64–128 m** — so a client-side walk
  shows (and lets you aim at) only the portals the client currently has. 🟥 **Under look-to-aim this is NO LONGER a
  cosmetic shortfall** — the overlay is the *selection surface*, so a range-limited candidate set means you literally
  cannot aim at / travel to a far destination. That is exactly why **server-authoritative candidate-set resolution is
  REQUIRED (§2 option 2)**, not optional: the candidate set the player aims at must include portals past the client
  window (the `SurveyorTableTag` owner-routed-RPC precedent). A client-side walk is acceptable only as a **staging
  step** for the in-game aim/feel accept; the shipped state must reach the full set. **AT-OVERLAY tests the local case;
  AT-PICK-LONGRANGE (§10) tests the required long-range case.**

### 7.3 Trigger
A proximity check (player within ~3 m of a Twisted Portal, the `Player.GetClosestPlayer` /
`m_activationRange` pattern vanilla portals use — `m_activationRange = 5f` `:122904`, used at
`:122980`) toggles the overlay visible, refreshed on a ~0.5 s throttle from the overlay's `Update`
(costs nothing on the server, which has no Hud — the Sunstone Lens cadence).

### 7.4 Label render-defect fixes + the constant-on-screen-size decision (t_f739451f → architect-locked)

Daniel's 2026-06-26 Niflheim playtest reported the Route B labels were mirrored, fuzzy, and
shrank with distance (unreadable). Architect decision: **Route B is KEPT** (Route A — the
screen-space HUD list — loses the through-terrain headline and discards shipped code; the
defects are all fixable in place). The three fixes:

- **Un-mirror:** the per-slot `Billboard` gets `m_invert = true` (it was unset → vanilla default
  `false`). Vanilla `Billboard.LateUpdate` with `m_invert` reflects the camera-facing target to
  *behind* the label so the canvas +Z points away from the camera → uGUI Text reads
  left-to-right instead of back-to-front. Composes with `m_vertical = true` (reflect runs first,
  upright-yaw preserved); ZTest-Always through-terrain is unaffected (it's on the material).
- **De-fuzz:** the glyph atlas is **supersampled** — `FontPx`, `ReferencePx`, and the canvas px
  are raised by the same factor so the on-screen WORLD size is invariant but the raster is
  rendered at N× density then minified (crisp). The black Outline is kept (dark-swamp contrast)
  and scaled proportionally. The exact factor is a GPU-client eyeball converge (banner-windsock).
- **Constant on-screen size:** labels are **distance-compensated** to hold ~constant angular
  (pixel) size across the overlay range, clamped near/far — they no longer shrink with raw
  perspective. This is the same move as the Sunstone trophy halo (`SunstoneHaloGeometry.ScaleAt`):
  the SCALE carries the range behaviour, factored into the engine-free CI-gated
  `TwistedPortalLabelScale.cs` (`ScaleMul`, link-compiled + unit-pinned — AT-LABEL-SCALE-MATH).
  Two modes ship behind a live enum (`ConstantOnScreen` default; `KneeFloor` — the Sunstone
  knee+floor shape with a high readable floor — selectable, reversible). All scale params are
  live BepInEx config for in-game convergence.

**Now the build target (look-to-aim, 2026-06-27 — supersedes the prior "forward-compat, NOT built here" note):** the
constant-on-screen-size labels ARE the destination-selection surface. The render-defect fixes above already shipped
look-to-aim-forward (constant on-screen size is exactly right for an aim target; per-slot color/material was left
reachable for the selected/aimed-highlight state). The look-to-aim impl adds: the **selected/aimed-highlight** state on
the per-slot label material (the reachable seam), the **food-impact preview** rendered on/under the aimed label
(`PreviewJump`, §5), and consumption of the **server-authoritative candidate set** (§2). The prior lock — "this card
builds NO picker; a picker is a Model B scope expansion, BLOCK + flag" — is **RETIRED**: Daniel chose the picker
(option A, gaze-selected), so building the selection surface is now in scope, not a violation.

---

## 8. Registration + wiring order (Registrar, PatchCheck, server-gating) — the patch surface COLLAPSES

> 🟥 **Look-to-aim build-surface additions (2026-06-27) on top of the shipped wiring below.** The food-as-fuel cost
> model stays **patch-free**; the look-to-aim redesign adds, in `Features/Portals/`:
> - **Commit-input poll + aim pick** — a per-frame client-only check (gate on Beat-1 proximity-active, read camera
>   forward, angular-pick the candidate, on tap-E commit) following the `SeersStoneFieldHost` precedent
>   (`:101-141`, a `Player.Update` postfix — one additive Harmony postfix, NOT a game-state patch) OR an `Update` on
>   the existing overlay pump (no new patch). Engineer picks the mount; both are precedented.
> - **`PreviewJump(player, D) → JumpSolution`** — the read-only sibling of `TrySpendForJump` in `TwistedPortalEnergy`
>   (§5). No patch; reuses the shipped non-mutating `SolveJump`.
> - **`CommitTravel(player, selected)`** in `SBPR_TwistedPortal` (§4.4) — replaces the jump-through trigger's
>   `Teleport` call. The overhead trigger (`BuildOverheadTrigger`/`SBPR_TwistedPortalTrigger`) is **removed/repurposed**.
> - **Server-authoritative resolution** — the `SurveyorTableTag` owner-routed-RPC (`Register<ZPackage>`/`InvokeRPC`)
>   for the candidate set + commit (§2). This is the one net-new networking surface; it is NOT a Harmony patch (it's a
>   `ZNetView` RPC registration, the cartography precedent).
> - **Overlay selection surface** — the aimed-highlight state + preview render on the per-slot label material (§7).
> - **Sweep the false `TwistedPortal.cs:54-58` comment** in the same change (§2).
>
> Net: the cost model remains patch-free; the look-to-aim surface adds at most one additive input postfix (or reuses
> the overlay `Update`) + one RPC registration — no game-state Harmony patches.

> 🔄 **The biggest structural win of food-as-fuel: the cost model is patch-free.** The old key model
> needed **three Harmony patches** (`Player.EatFood` postfix, `Player.RemoveOneFood` postfix, the
> `DrainEquipedItemDurability` prefix cloned from the Sunstone Lens). Food-as-fuel needs **zero** —
> PE is read on demand from `GetFoods()` and the debit/berry/`SE_Puke` all run inline in our own
> teleport method (§5.1). The feature is now **entirely component-wiring + one piece registration**,
> with no game-method patches at all.

New files in the existing `Features/Portals/` slice:
- **`TwistedPortal.RegisterPrefabs(zns)`** — build + register the portal piece (additive shell +
  grafted kitbash + `SBPR_TwistedPortal` MonoBehaviour); register our prefab hash into
  `Game.PortalPrefabHash` per §4.3 (idempotent, the Ancient Portal helper).
- **`TwistedPortal.DoObjectDBWiring(zns)`** — add the portal piece recipe (Q1 = coexist shape, §0),
  rebuild the portal piece cost, add the portal to the Hammer PieceTable (`AddOrReplacePieceByName`).
  **No item to register** (the key is gone).
- **`TwistedPortalEnergy`** (the §5 cost engine) — a plain helper class, **no registration, no
  patches**. Its pure-math core (tier, PE sum, berry-shortfall) is link-compiled into the test
  project as an engine-free truth table (the `SunstoneHaloGeometry`/`CompassNorthGate` precedent) so
  CI gates the math (AT-PE-MATH). The runtime call site is `SBPR_TwistedPortal.Teleport` (§4.4).
- **`TwistedPortalOverlay`** (§7) — client-only overlay. Its ONE Harmony patch is the **`Hud.Awake`
  postfix** (`TwistedPortalOverlay.HudBootstrap`) that mounts the overlay pump under
  `Hud.m_rootObject` — the Sunstone Lens / Iron Compass HUD-bootstrap doctrine. Past the mount it is
  patch-free (reads ZDOs + draws world-space Canvas labels). Registered in `Plugin.Awake` and
  PatchCheck-asserted (see below).
- **Wire into `Runtime/Registrar.cs`** — add the `RegisterPrefabs` call **after `Portals` and
  `SunstoneLens`** (Twisted reuses Portal helpers and consumes Sunstone), and `DoObjectDBWiring`
  **after `Trailhead`** (Explorer's Bench must exist if any recipe needs a station — though the
  portal piece is Hammer-placed/no-station) — mirror the Ancient Portal ordering.
- **`SpecCheck.cs`** — add the **one** new row (§0, the portal piece); extend the `LOCKED SOURCE`
  comment to cite this doc. (Sunstone's row already exists from the Lens — do not duplicate. **Do not
  add a key row** — there is no key.)
- **`Plugin.Awake` patch registration: ONE patch for this feature (C3's overlay mount).** The
  portal/teleport/trigger/ZDO/PE/berry/`SE_Puke` machinery (C1+C2) is **patch-free by construction**
  — the cost model contributes no patches (contrast the old draft, which had to register three). C3's
  overlay adds exactly one: the **`Hud.Awake` postfix** (`TwistedPortalOverlay.HudBootstrap`) that
  mounts the overlay pump under `Hud.m_rootObject` — registered via
  `harmony.PatchAll(typeof(TwistedPortalOverlay.HudBootstrap))` and PatchCheck-asserted (or it ships
  dead, the t_564f695a lesson). This is the same `Hud.Awake`-mount idiom the Sunstone Lens HUD + Iron
  Compass overlays use; it never fires on the dedicated server (no Hud).
- **Server-gating:** every registration is gated `if (!ServerContext.OnSBServer) return;` via the
  Registrar fan-out (SBPR doctrine). The overlay is client-relevant; the registration is server+client.

---

## 9. Decomposition — the look-to-aim impl cards (created by THIS spec pass; gate on the merge)

> 🟥 **SUPERSEDED (2026-06-27).** The prior §9 listed the already-BUILT Model A cards (C1 t_2b388cd5 portal core, C2
> t_6e992a30 food-as-fuel cost, C3 t_e732bd8b informational overlay). Those shipped the **base portal + the cost
> engine + the through-terrain labels** — all carried forward and reused. They did **NOT** build the look-to-aim
> destination/commit/preview surface (verified: aim-to-commit exists nowhere in code). This spec pass (card
> t_3d908685) creates the **look-to-aim impl children** below, parented on this card so they auto-promote from
> `todo`→`ready` when this spec PR merges (the same gating pattern the prior cards used). This architect card does
> **NOT** implement — it specs + decomposes.

| # | Card (real id) | Assignee | Depends on | Scope | Primary ATs |
|---|---|---|---|---|---|
| L1 | **t_f4d0d5e1** — aim-pick destination + tap-E commit; retire the jump-through trigger | `engineer-systems` | this card (t_3d908685) | §1 Q3, §4.4, §4.4a, §4.5, §6. The angular aim-pick (default ii, the `SeersStoneFieldHost` camera-read precedent), `CommitTravel(player, selected)` reusing the shipped teleport core + food-as-fuel debit, the overhead trigger removal, the E-key demote (rename→hold-E). Structures the candidate-set seam for L2. | AT-AIM-SELECT, AT-AIM-THROUGHTERRAIN, AT-COMMIT-E, AT-NO-JUMPTRIGGER, AT-RENAME-DEMOTE, AT-COOLDOWN-REFUND |
| L2 | **t_ccb454f8** — server-authoritative long-range candidate set + commit (RPC); sweep the lying comment | `engineer-systems` | **L1** (the aim-pick seam) | §2 (now REQUIRED). The owner-routed `Register<ZPackage>`/`InvokeRPC` directory (the `SurveyorTableTag` precedent), swapping the client-side staging walk for the RPC candidate set so travel reaches destinations past the ~64–128 m client window; deletes the false `TwistedPortal.cs:54-58` comment. | AT-PICK-LONGRANGE, AT-COMMENT-SWEPT |
| L3 | **t_d9ea1b2c** — overlay selection-highlight + food-impact preview render | `engineer-ui` | **L1** (the aim-pick + PreviewJump seam) | §7 (now interactive) + §5 (PreviewJump). The aimed/selected-highlight on the per-slot label material (the reachable seam), the read-only food-impact preview on the aimed label (overturns §6 "no charge readout"), consuming L2's candidate set. | AT-AIM-HIGHLIGHT, AT-FOOD-PREVIEW, AT-OVERLAY-THROUGHTERRAIN |

- **Build order:** L1 → (L2, L3 in parallel, both depend on L1). The board edges encode `parents`. Each is its own
  Daniel-gated PR (incremental delivery doctrine); each ends in `kanban_block(review-required)` with a PR URL, not
  `kanban_complete`.
- **The shipped Model A cards (C1/C2/C3) are NOT re-run.** Their output (portal class, cost engine, through-terrain
  labels) is the foundation the look-to-aim cards build ON. The one piece of shipped Model A behavior that is *removed*
  is the nearest-same-rune `ResolveDestination` + the jump-through trigger (L1 replaces them).
- **This (architect) card does NOT implement.** Its deliverable is the rewritten spec + the doc manifest updates + the
  L1–L3 fan-out; on merge, the board promotes L1, and L2/L3 follow as L1 completes.

---

## 10. Named acceptance tests (the single source of truth for "done")

Observable criteria. **logs-green ≠ playable** — every AT closes only on Daniel placing/using one
in-game on a joined client (repo honesty rule). The engineer reports per-AT status in each PR handoff;
the build PRs do NOT self-close these.

> 🔄 **The six key-charge ATs are DROPPED** (AT-KEY-CRAFT, AT-KEY-CHARGE-FOOD, AT-KEY-PUKE,
> AT-KEY-SPLIT, AT-KEY-SPEND, AT-KEY-ZERO-INERT) — there is no key. They are replaced by the
> food-as-fuel ATs below.

> 🟥 **Look-to-aim AT changes (2026-06-27, card t_3d908685):** **AT-NAME-PAIR is RETIRED** (same-rune pairing is no
> longer the destination rule) and **AT-JUMP-ACTIVATE is RETIRED** (the jump-through trigger is removed — E commits).
> They are replaced by the look-to-aim ATs in the new block below. The food-as-fuel ATs (AT-PE-*, AT-BUKE-*, etc.) and
> the portal/placement ATs (AT-PORTAL-PLACE, AT-NOPORTALS-BYPASS, AT-RUNE-NAME, AT-NO-VANILLA-PAIR, AT-NO-KEY) are
> carried forward unchanged.

**Look-to-aim destination/commit/preview (L1/L2/L3 — the new headline ATs):**
- **AT-AIM-SELECT** (L1, the headline) — standing on a Twisted Portal, **aiming the crosshair at another Twisted
  Portal** selects it as the destination (its label highlights); sweeping the crosshair to a different label moves the
  selection. No same-rune requirement — any Twisted Portal is selectable by aim.
- **AT-AIM-THROUGHTERRAIN** (L1) — a destination portal **behind a hill** (no line-of-sight collider) is still
  selectable by aiming at its through-terrain label (proves the angular pick, not a collider raycast).
- **AT-COMMIT-E** (L1, card AC) — **tap [Use]/E while aiming** at a selected destination teleports there (with
  `NoPortals` set). The food-as-fuel debit fires exactly as before (AT-PE-DEBIT still holds).
- **AT-NO-JUMPTRIGGER** (L1) — jumping into the overhead ring **no longer teleports** (the trigger is removed); travel
  is aim+E only. (Unless Daniel opted to keep both verbs — then this AT is "jump still works too," flagged.)
- **AT-RENAME-DEMOTE** (L1, the E-key fork) — **tap-E commits travel; rename opens on hold-E / the hover menu** (per
  Daniel's locked choice). The rune name still persists across relog (AT-RUNE-NAME holds) — only the gesture moved.
- **AT-COOLDOWN-REFUND** (L1) — committing twice inside the 2 s teleport cooldown does **not** silently eat food/berries
  on the no-op second attempt (refund-on-`false` / cooldown pre-check).
- **AT-PICK-LONGRANGE** (L2, 🔴 the dedicated-server proof) — on the **Niflheim dedicated server**, standing on portal
  A you can aim at and travel to portal B that is **beyond one client's ZDO window** (>128 m, different sector). Proves
  the server-authoritative candidate set + commit (not a client-window-limited walk).
- **AT-COMMENT-SWEPT** (L2) — the `TwistedPortal.cs:54-58` comment matches reality (no longer claims owner-side
  resolution that doesn't exist). *(Closeable at code review, not in-game.)*
- **AT-AIM-HIGHLIGHT** (L3, visual) — the aimed label visibly highlights and tracks as the crosshair sweeps across
  candidate labels; through terrain. Daniel's eyeball accept.
- **AT-FOOD-PREVIEW** (L3, card AC — "the impact to food") — the food impact (belly range vs distance, berries needed)
  shows on the aimed label **before** commit; the actual post-commit drain matches the preview. The preview spends
  nothing (non-mutating `PreviewJump`).

**Portal mechanism (carried forward — the base portal shipped):**
- **AT-PORTAL-PLACE** — `piece_sbpr_twisted_portal` is placeable with the **Hammer**, **no station in
  range**, solid-earth only (rejected on structures), costs the recipe-1 materials. Visually distinct
  from the Ancient Portal (swamp tint).
- **AT-NOPORTALS-BYPASS** (🔴 the headline, card AC#2) — with the `NoPortals` global key SET, a Twisted
  Portal **still teleports**. A vanilla portal next to it does NOT (proves we bypass the gate that
  still binds vanilla).
- **AT-RUNE-NAME** (card AC) — `[Use]` opens the rename dialog; the typed rune name persists across
  relog (stored in `sbpr_rune_name`, NOT `s_tag`). Hover shows the rune name.
- **AT-NAME-PAIR** — 🟥 **RETIRED (2026-06-27).** Same-rune pairing is no longer the destination rule (look-to-aim
  picker supersedes it). Replaced by AT-AIM-SELECT / AT-COMMIT-E above. *(Kept as a tombstone so the supersession is
  legible.)*
- **AT-NO-VANILLA-PAIR** (🔴 §4.3 risk) — two UNNAMED Twisted Portals are NOT auto-connected by vanilla
  `ConnectPortals` into a spurious `ConnectionType.Portal` link; a Twisted Portal never pairs with a
  vanilla `portal_wood`. Verified in-game (the `s_tag`-empty / hash-registration interaction).
- **AT-JUMP-ACTIVATE** — 🟥 **RETIRED (2026-06-27).** The overhead jump-through trigger is removed; travel commits on
  aim+E (AT-COMMIT-E / AT-NO-JUMPTRIGGER). *(Tombstone.)*
- **AT-NO-KEY** (🔄 NEW, the reconciliation proof) — **no trinket item exists.** There is no
  `SBPR_TwistedKey` in the ObjectDB, no craftable key recipe, no Trinket to equip; SpecCheck has **no
  key row**. Travel is gated purely by belly food. (The negative AT that proves the supersession
  shipped.)

**Cost model — food-as-fuel (C2):**
- **AT-PE-DEBIT** (🔄 NEW, card AC) — a jump **drains belly food-time proportional to distance**: after
  a teleport, the player's active food slots show **less remaining time** than before, scaled by the
  jump distance; Max HP/Stamina/Eitr visibly drop. A longer jump drains more.
- **AT-ARRIVE-DEPLETED** (🔄 NEW, card AC) — a **long** jump lands the player **food-low** (slots near
  empty, buffs bottoming out); a short hop barely dents the belly. The distance↔depletion coupling is
  observable, not bolted on.
- **AT-BUKE-RESERVE** (🔄 NEW, card AC) — Bukeberries (`Pukeberries`) are burned **only on shortfall**:
  a jump the belly can cover spends **zero** berries; a jump past belly range burns
  `ceil(shortfall/30m)` berries; **10 berries = a from-empty 300 m max jump.** With no berries and an
  empty belly, the jump is blocked (not a crash).
- **AT-BUKE-SICK** (🔄 NEW, card AC) — a **berry-burning** jump applies the vanilla **Feeling Sick**
  (`SE_Puke`) effect on arrival (−50 % move / no regen) AND lands the player food-empty. A
  belly-covered jump (no berries) does NOT apply it.
- **AT-FEAST-CLOCK** (🔄 NEW) — a **feast** (a `Feast*` food) contributes travel range on the
  normalized ~28 m `FEAST_RANGE_CAP` clock, NOT its real 50 m buff timer; its 50 m buff duration is
  unchanged. The best feast out-ranges *no* personal meal (always slightly under).
- **AT-PE-MATH** (🔄 NEW, CI-gated) — the engine-free PE core (tier = `round(clamp(stats/30,1,5)×2)/2`,
  `PE = Σ minutes×tier`, berry-shortfall = `ceil((D−belly_range)/30)`) matches the design doc's
  worked numbers in a link-compiled unit table (the `SunstoneHaloGeometry` precedent). The one AT that
  closes headless.

**Overlay + cross-cutting:**
- **AT-OVERLAY** (carried) — standing on a Twisted Portal shows the nearby Twisted Portal rune names (the aim labels).
  🟥 **Look-to-aim note:** the overlay is now the **interactive selection surface** (not informational) — see
  AT-AIM-SELECT / AT-AIM-HIGHLIGHT. On a dedicated server the candidate set must reach past the client window
  (AT-PICK-LONGRANGE / §2 — server-authoritative, no longer an accepted shortfall).
- **AT-OVERLAY-THROUGHTERRAIN** (the visual gate) — the rune labels render **through terrain**
  (ZTest Always), readable behind hills. Daniel's in-game eyeball accept.
- **AT-NOMAP-SAFE** — the overlay renders with the SB server's default NoMap (no minimap) — it does not
  depend on the map being on (the Sunstone Lens / Iron Compass HUD doctrine).
- **AT-VANILLA-ONLY** (card AC) — no third-party portal-mod code read or copied; all hooks are
  base-game primitives (the look-to-aim picker + food-as-fuel mechanics are net-new SBPR fiction).

---

## 11. Cross-doc updates (spec-first — move in the SAME PR as this spec) + decision log

### Cross-doc updates this spec PR carries (the docs half of spec-first)
- **`docs/v3/planning/index.md`** + **`docs/v3/planning/README.md`** — **rewrite the Twisted Portal rows** to flip the
  destination model from "Model A (same-rune name-pairing) + informational overlay" to the **look-to-aim destination
  picker (option A, gaze-selected): aim the crosshair at a destination portal, see a food-impact preview, press [Use]/E
  to commit**; note server-authoritative long-range resolution is now REQUIRED and the overlay is interactive. (The
  food-as-fuel cost model is unchanged.) *(Done in this PR.)*
- **`docs/datasets/PIECES_AND_CRAFTABLES.md:471`** — **update the Twisted Portal line:** the addressing/travel model is
  now look-to-aim (aim + E to commit, food preview), not same-rune pairing; the prefab + food-as-fuel cost are
  unchanged. *(Done in this PR.)*
- **`docs/design/nomap.md` §7** — **NO edit in this PR.** The design doc's "on-step shows visible portal names" still
  reads true (the labels exist; they're now also the aim targets). The look-to-aim *interaction* is an impl-spec-level
  detail; if Daniel wants the design doc to carry the look-to-aim narrative, that's a separate small design-doc pass —
  flagged, not bundled. *(No-op this PR.)*

> **Folder-existence note (resolved):** the old draft flagged that `docs/v3/planning/` "does not yet
> exist on the v1 integration branch." That is **stale** — the folder exists on `main` (this spec, the
> Sunstone Lens spec, the Iron Compass spec, etc. all live there). The scaffolding concern is closed;
> this PR is cut from `main`.

### Decision log (reconciled state — what's locked vs. what an engineer may still resolve in-game)

**Locked by the design authority (Daniel, merged PRs #270/#271 — not reopenable without him):**
- **Cost model = food-as-fuel.** No key trinket. `PE = remaining_minutes × tier`, distance debits
  food-time, Bukeberries are the shortfall reserve, berry-jumps arrive `SE_Puke`. (Resolves the old
  Q2.) Numbers are tuning knobs; the architecture is fixed.

**Locked by the architect this pass (grounded in precedent + the locked design):**
- **Q1 = COEXIST** — vanilla/Ancient portals stay craftable; §4.7 empty (the child portal card builds
  this).
- **Q3 = LOOK-TO-AIM destination picker (option A, gaze-selected)** — 🟥 supersedes the prior "Model A name-pairing."
  Stand on a portal, aim the crosshair at a destination portal (angular pick among the through-terrain candidate set,
  default ii), see a food-impact preview on the aimed label, **press [Use]/E to commit**. Rune names are demoted to
  human-readable **aim labels** (not the pairing key). Server-authoritative long-range resolution is **REQUIRED** (§2,
  the `SurveyorTableTag` RPC precedent). The overlay is **interactive** (the selection surface), not informational. The
  overhead jump-through trigger is **retired**.
- Distinct class `SBPR_TwistedPortal`, NOT inheriting `TeleportWorld` (card AC#1; tag-collision).
- Reimplement teleport via `Player.TeleportTo` (`:20771`), omitting the `NoPortals` check (`:123008`,
  card AC#2); debit belly PE (§5) at the commit seam. **Carried forward unchanged.**
- Additive construction reusing the Ancient Portal shell + kitbash (ADR-0006). **Shipped.**
- Hammer-placed, no station, solid-earth-only (the Ancient Portal's locked placement choices). **Shipped.**
- Rune name in the dedicated `sbpr_rune_name` ZDO slot, off `s_tag` (card AC); naming via the vanilla
  `TextInput` dialog through `TextReceiver`. **Shipped; the opening gesture moves to hold-E (see the Daniel fork).**
- **The cost model is patch-free** — PE read on demand from `Player.GetFoods()` (`:17598`); no
  `EatFood`/`RemoveOneFood`/`DrainEquipedItemDurability` patches (those were the deleted key model). **Shipped.**

**The ONE open Daniel design-lock (a single line for his nod, not a menu — §6):**
- **E-key sharing.** Today [Use]/E opens rename; under look-to-aim E must commit travel. Architect default: **tap-E =
  commit travel; rename → hold-E / hover menu; rune names kept as read-only aim labels.** Alternative (his call): drop
  naming entirely, freeing E with no collision. Default = keep-as-labels. The impl children build the default unless
  Daniel changes it at merge.

**Engineer-resolvable IN-GAME (noted per-section; not gates, not Daniel decisions):**
- Aim-cone tolerance for the angular pick (§4.4a) — a live BepInEx knob; tune on a joined client.
- `METERS_PER_PE` baseline (§5.3) and the other cost knobs — config, baselines from the design doc §6. **Shipped.**
- NoBossPortals keep/lift (§4.4, default KEEP); ore-ban keep/lift (§4.4, default KEEP).
- Selection mechanism (§4.4a): angular-pick (default ii) vs hover-raycast (i) — default ii; flag only if grounding
  overturns it in-game.
- Server-auth staging (§2): client-side candidate set is OK as a staging step for the aim/feel accept, but the shipped
  state must reach the full set (L2).

**No remaining 🔴 BLOCK on this card.** This is a docs-only spec PR; on Daniel's merge it promotes the look-to-aim impl
children L1 (t_f4d0d5e1) → L2 (t_ccb454f8) + L3 (t_d9ea1b2c). **No impl code is written by this card.** The single
E-key default above rides into the impl unless Daniel overrides it at merge.
