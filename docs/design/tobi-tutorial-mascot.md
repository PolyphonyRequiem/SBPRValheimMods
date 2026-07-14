---
title: "Tobi the Tutorial 'Tiel — the SBPR tutorial mascot (a third guide-bird)"
status: living
purpose: "Living design for Tobi, an SBPR tutorial mascot: a distinct third guide-bird (a whiteface cockatiel) that delivers SBPR's OWN onboarding hints, additive to — never replacing — vanilla's Hugin/Munin tutorial system. GROUNDED hard against the decompiled Tutorial/Raven/Player code (line-cited) so the integration is mechanism-true, not vibes: the trigger chokepoint, the save-persisted seen-set, the STATIC single-instance-per-bird constraint, and the binary m_isMunin identity flag are all read from assembly_valheim, not memory. Captures Daniel's 2026-07-07 Matrix #design thread. Tobi memorializes Luna's departed cockatiel — the tribute that lands is one built well and integrated right, so this doc keeps the engineering lane. Net-new Trailborne onboarding feature — NOT in requirements.md v1, NOT yet slotted to a version. Iterate ON this doc, not in chat."
---

# Tobi the Tutorial 'Tiel — the SBPR tutorial mascot

> **What this doc is.** The living design home for **Tobi**, an SBPR tutorial
> mascot: a distinct third guide-bird — a whiteface cockatiel — who delivers
> SBPR's *own* onboarding hints. Tobi is **additive**: vanilla's Hugin/Munin
> tutorial birds are untouched; Tobi arrives only for SBPR's special hints, and
> his different look *is the signal* — "pay attention, I'm not your regular
> tutorial." Structured DECIDED (Daniel's 2026-07-07 thread) / OPEN (the
> sub-forks still needing a call) / GROUNDED (decomp-line-cited mechanism).
>
> 🕊️ **The heart of it.** Tobi memorializes Luna's recently departed cockatiel.
> The way this doc honors that is by getting the engineering *right* — a mascot
> built well and integrated cleanly is the fitting tribute. The relational
> weight lives with Daniel; this doc stays in the engineering lane by design.

> **Status is `living`, not `accepted`.** The core architecture is DECIDED
> (§1); two sub-forks remain OPEN (§5). This is a design capture to iterate on,
> **not** a build-ready impl-spec. When Tobi is slotted to a version and the §5
> forks close, this graduates to a `docs/<semver>/planning/tobi-*-impl-spec.md`
> and an impl card is cut. Do not build from this doc alone.

> **Clean-side note (ADR-0001).** Every decomp line cited is base game
> (`assembly_valheim`), which the repo AGENTS.md and ADR-0001 explicitly permit
> reading *and adapting* — reading the game we mod is not a clean-room
> violation. Line numbers are from
> `~/valheim/worldgen-spike/decomp/assembly_valheim.decompiled.cs`, grepped live
> this pass. Vanilla asset facts (Hugin/Munin mesh, materials, textures) are
> from `vprefab inspect` over the dedicated-server payload, run this pass.

---

## 0. Provenance & scope guard

- **Design source:** Daniel's 2026-07-07 Matrix engineering thread ("Tobi the
  Tutorial 'Tiel — SBPR mascot").
- **Reference material (private, NOT a repo asset):** Daniel holds photos of
  Tobi and a **commissioned painting** of him (signed, by a personal
  acquaintance). The painting is **private reference only** — it informed Tobi's
  *register* (painterly, Valheim-congruent) and *personality* (low, curious,
  leaning-in, mischievous), but it is **not** derivable source for a shipped
  asset and is **not** committed to this repo. Shipped Tobi is rebuilt from the
  photographic truth of the bird + Valheim's own art pipeline. This keeps the
  MIT repo clean of third-party-authored art and avoids any rights entanglement.
- **Not in scope of v1.** Tobi is net-new onboarding, absent from
  `requirements.md`. It is not slotted to any current version roadmap; version
  placement is an OPEN call (§5).

---

## 1. DECIDED — the locked architecture (Daniel, 2026-07-07)

1. **Tobi is a distinct THIRD guide-bird, not a reskin of the vanilla birds.**
   Vanilla Hugin/Munin stay exactly as they are. Tobi appears *only* for SBPR's
   own hints. His distinct look is the functional signal ("not your regular
   tutorial").
2. **Additive scope — SBPR hints only.** Tobi speaks SBPR's onboarding hints.
   He does **not** re-voice the ~20 vanilla tutorials (§2). No vanilla hint
   changes bird.
3. **Zero vanilla-method patching for triggers (library goal).** The Tobi
   library registers data + presents a bird; it patches **no** vanilla gameplay
   method to fire hints. The three trigger kinds (§3) are all additive or
   SBPR-owned. (A consumer who *wants* to piggyback a vanilla moment writes
   their *own* Harmony patch and calls `Tobi.Show()` — the library never does.)
4. **Two-operation API.** `Register(...)` once at init; `Show("key")` at the
   moment. Library owns the noun (visual, fly/perch behavior, save-backed
   show-once, nameplate, exclamation, registration); consumer owns the verb
   (*when* to fire). See §4.
5. **Own mesh, on the vanilla skeleton.** Tobi ships SBPR's **own** cockatiel
   mesh (not extracted vanilla geometry), skinned to a **copy of Hugin's
   skeleton** so every vanilla fly/perch/idle animation retargets for free (§6).
6. **Articulated crest is REQUIRED, not cosmetic.** A cockatiel's crest is its
   primary expressive organ; crest-up *is* the "pay attention" signal — the same
   gesture as the mascot's whole job. Hugin's skeleton has no crest bones, so
   Tobi's rig **adds** a small crest bone chain (§6). A static crest is
   explicitly rejected.
7. **Color truth — whiteface palette. NO yellow, NO orange, NO red. Anywhere.**
   Tobi is a *whiteface* cockatiel: the mutation lacks the lipochrome pigment
   that gives normal cockatiels a yellow face and orange cheek-discs. Palette:
   **white / cream / tan / fawn / soft grey-brown**, pink feet, dark eye, pale
   **horn** (desaturated, not saturated-yellow) beak. The crest is **white/cream
   to the tip** — this is the single most likely spot for a generic-cockatiel
   prior to wrongly reintroduce yellow; it must not. (Daniel corrected exactly
   this error during design — this is a hard constraint, not a preference.)
8. **Match Valheim's fidelity tier.** Same poly-count class, same rendering
   style, same skin resolution as the vanilla guide-bird, so Tobi reads as part
   of the game, not an import (§6, measured target).

---

## 2. GROUNDED — how vanilla's tutorial system works today

The system Tobi extends is **two classes + one save-persisted state set**.

### 2.1 The state layer — `Player.m_shownTutorials`
- A `HashSet<string>` of tutorial keys already shown (`:15576`), **serialized
  into the character save** (written `:19646-19647`, read `:19775`) — so "seen"
  persists per-character, across sessions.
- API (`Player`, `:20541-20581`): `ShowTutorial(name, force=false)` (entry
  point — shows only if `!HaveSeenTutorial`), `HaveSeenTutorial`,
  `SetSeenTutorial`, `ResetSeenTutorials` / `IsSeenTutorialsCleared`.

### 2.2 The trigger chokepoint — everything funnels here
```
<trigger> → Player.ShowTutorial("key")
          → if !HaveSeenTutorial("key")          [dedupe gate, save-backed :20551]
            → Tutorial.instance.ShowText("key")
              → m_texts.Find(name=="key")         [MUST be registered or it no-ops :55415]
              → SpawnRaven(...)                    [bird flies in :55428]
```
> **Load-bearing:** if a key isn't registered in `Tutorial.m_texts`, `ShowText`
> logs *"Missing tutorial text"* and does nothing (`:55420-55425`). A bird can
> only speak a **registered** key. This is Tobi's one hard prerequisite (§3.4).

### 2.3 The trigger brain — `class Tutorial : MonoBehaviour` (`:55345`)
- Singleton (`Tutorial.instance`) holding `List<TutorialText> m_texts`. Each
  `TutorialText` (`:55348`) carries `m_name` (key), `m_topic`, `m_label`,
  `m_text` (`[TextArea]`), `m_isMunin` (which bird), and two auto-trigger fields:
  `m_globalKeyTrigger` (fire if a world global-key is set) and `m_tutorialTrigger`
  (fire if another named tutorial has been seen — "chain multiple birds").
- `Update()` (`:55394`) polls every `m_GlobalKeyCheckRateSec` (**default 10s**),
  walking `m_texts` and firing any satisfied global-key or chain trigger.

### 2.4 The two vanilla trigger CLASSES
- **Class A — hardcoded C# call sites (~18).** Direct `ShowTutorial("…")` calls
  baked into vanilla methods: item pickups routed through `Player.OnInventoryChanged`
  (`:20475` — `hammer`, `hoe`, `pickaxe`, `ore`, `food`, `shield`, `wishbone`,
  `bellfragment`, `boss_trophy`, `trinket`), status/area events (`encumbered`
  `:17278`, `cold` `:17389`, `eitr` `:17553`, `death` `:18436`, `blackforest`
  `:20069`, `haldor` `:20075`, `ashlands` `:20079`), `inventory` (forced,
  `:41462`), `randomevent` (`:90999`). These need vanilla's own code to fire —
  **not extensible without patching a vanilla method.**
- **Class B — data-driven auto-triggers.** `Tutorial.Update()` fires purely from
  the two fields a registered text carries (global-key / chain). **No call site
  needed** — SBPR never touches a vanilla method.

### 2.5 The delivery bird — `class Raven` (`:118212`)
- **This is Hugin/Munin.** Flies to the player, perches, shows a dialog bubble,
  is hoverable/interactable. Tunables `:118239-118271` (spawn 15m / despawn 20m,
  auto-talk 3m, dialog 10s, etc.).
- **Public delivery seam:** `Raven.AddTempText(key, topic, text, label, munin)`
  (`static`, `:118754`) — dedupes by key, builds a `RavenText`, queues it.

---

## 3. The Tobi integration surface (trigger axis)

Tobi's triggers fall into three buckets, cleanest first. **None require the
library to patch a vanilla method** (DECIDED §1.3):

- **3.1 SBPR-owned moments → `Manual`.** Equip Trailblazer's Tools, place the
  Explorer's Bench, build a first cairn — these are *SBPR's own code*, which
  simply calls `Tobi.Show("tobi_bench")`. We own the call site; nothing to patch.
  This is the bulk of Tobi and the easy path.
- **3.2 World-progression moments → `GlobalKey`.** Anything mapping to a world
  global key registers a global-key trigger and rides the 10s poll (Class B).
  No call site, no patch.
- **3.3 Chain-off-another-hint → `AfterSeen`.** Fire once another (SBPR or
  vanilla) tutorial key has been seen (Class B chain). No patch.
- **3.4 The one shared prerequisite: registration.** Tobi's texts must be
  injected so the chokepoint's `Find` resolves. For vanilla keys that's
  `Tutorial.m_texts`; for the third-bird routing see §7. This is a startup hook
  (a Harmony postfix on `Tutorial.Awake` or equivalent), **not** a gameplay-method
  patch — it adds data, it doesn't alter vanilla behavior.
- **3.5 Piggybacking a vanilla moment (consumer-owned, out of the library).** If
  a consumer wants "when you pick up X, Tobi adds an SBPR note," they write their
  *own* Harmony patch on the vanilla method and call `Tobi.Show()` from it. The
  library stays patch-free forever; the verb (including the patch) is the
  consumer's. (This dissolves the earlier either/or: nobody pays for
  vanilla-patching unless they opt in.)

---

## 4. The API (directional — not a locked signature)

Two operations are the whole consumer surface:

```csharp
// 1) Declare once, at mod init:
Tobi.Register(new TobiTutorial {
    Key   = "trailborne.bench",
    Topic = "The Explorer's Bench",
    Text  = "…",
    Trigger = TobiTrigger.Manual,           // or .GlobalKey("defeated_x"), or .AfterSeen("otherkey")
});

// 2) Fire at the SBPR-owned moment:
Tobi.Show("trailborne.bench");              // → dedupe gate → registered lookup → Tobi flies in
```

- **Library owns the noun (policy + shipped defaults):** the cockatiel visual,
  fly/perch behavior (inherited from the vanilla Raven path), save-backed
  show-once dedupe (inherited from `m_shownTutorials`), the `[!]` exclamation,
  dialog styling + "Tobi" nameplate, and the one-time registration hook.
- **Consumer owns the verb:** *when* to fire (`Show`), and — if they want a
  vanilla-moment piggyback — their own patch (§3.5).
- **`TobiTrigger`** maps onto the grounded trigger kinds: `Manual` (§3.1),
  `GlobalKey` (§3.2 / Class B), `AfterSeen` (§3.3 / Class B chain).

> Signatures are illustrative. The exact API is fixed in the impl-spec once §5
> and §7 close.

---

## 5. OPEN — sub-forks needing a call before impl-spec

**Q1 — Version placement.** Tobi is net-new and unslotted. Which version does it
ride (a v-tier release, or its own onboarding milestone)? This decides where the
impl-spec lives (`docs/<semver>/planning/`) and when it's built.
→ *No lean — Daniel's roadmap call.*

**Q2 — Crest drive.** How is the (required, §1.6) crest animated?
- 🔵 **(a) Procedural, state-driven (lean):** a script lerps crest angle toward a
  target set by Tobi's state — snaps up on arrival/speak, eases to relaxed when
  idle. Cheaper (no authored clips), and ties the crest to *delivery moments*
  (the responsiveness we want).
- 🟠 **(b) Authored crest clips:** hand-animated crest beats layered over the
  vanilla body clips — frame-level artist control, more animation-pipeline work.
→ *Lean: (a) procedural — the crest should respond, not loop.*

*(These two are the only design-level forks left. Everything else in §1 is
DECIDED; §6/§7 are impl-shape the impl-spec finalizes.)*

---

## 6. Asset target (measured) & build approach

### 6.1 Fidelity target — measured off vanilla this pass (`vprefab inspect`)
| Spec | Vanilla Hugin/Munin (measured) | Tobi target |
|---|---|---|
| Skin texture | **256 × 256** | 256 × 256 |
| Map set | 4-map PBR: `_d` diffuse, `_n` normal, `_m` metallic, `_e` emissive | same 4 maps |
| Material | one material, one skinned mesh per bird (`TheHugin`, `Munin_mat`) | one `Tobi` material |
| Rig | full skeleton: hip, 2 legs × 3-jointed toes + back talon, spine, neck, jaw, 3-segment wings w/ feather bones, tail | **copy of this skeleton + a crest chain** |
| Mesh | single low-poly skinned body (vertex-compressed in bundle; low hundreds of tris) | **own** cockatiel mesh, same tri class |
| Bundle | `c4210710` (shared w/ RavenThrone) | SBPR AssetBundle (the Bear Hide Tent bundle is the in-repo precedent) |

> **Key finding:** Munin is already a **recolor of Hugin** — identical mesh dims
> `(1.65, 7.51, 1.57)`, identical 4-map structure, different `_d`. Iron Gate
> built the second bird the way we build the third. Tobi is Munin's sibling in
> pipeline terms, differing in that Tobi carries an **own mesh + crest** rather
> than only a recolor.

### 6.2 The mesh — own geometry on the vanilla skeleton (DECIDED §1.5)
- Author SBPR's **own** low-poly cockatiel body at the same tri class, **skinned
  to a copy of Hugin's skeleton** (same bone names) so all vanilla fly/perch/idle
  clips retarget for free. We author the *body*; we inherit the *motion*.
- **Provenance:** own geometry, **not** extracted-and-shipped Hugin verts.
  Skeleton *reuse* is clean (it's the animation contract); shipping our own mesh
  is cleaner than shipping extracted vanilla geometry. No vanilla mesh in the
  shipped artifact.

### 6.3 The crest — the one genuinely new animated surface (REQUIRED §1.6)
- Hugin's head chain is only Neck → Jaw → Jaw1 (grounded via `vprefab inspect`);
  **no crest bones exist.** Tobi's rig **adds** a small crest bone chain (~base →
  mid → tip, hinged at the head) as new bones — additive, same doctrine as the
  rest of SBPR. Body motion inherited; crest is authored/driven (Q2).

### 6.4 The skin — the one new-art step
1. Export Hugin's UV layout + existing `_d` as the template.
2. Paint the whiteface Tobi diffuse onto our own mesh's UVs — the §1.7 palette,
   painterly, matching Valheim's coarse hand-painted texel density. **No yellow /
   orange / red; crest white-to-tip.**
3. Derive `_n`/`_m`/`_e` for our mesh (feather normals authored to match).
4. Pack into a BepInEx-loaded AssetBundle, bind to the Tobi material.
> Image tools may *concept* the skin look; the shipped file is a hand-corrected,
> UV-correct 256² texture, not a generated asset dropped in raw.

### 6.5 The retexture SPIKE (de-risk the plumbing — throwaway, NOT the ship path)
Before the mesh work, a cheap spike proves the seam: ship a temporary Tobi *skin*
on the **raw vanilla Hugin mesh** to verify (a) the SBPR AssetBundle loads and
binds, and (b) the third-bird presentation seam (§7) fires in-game. Value:
proves the plumbing cheaply **and** yields a real in-game frame showing exactly
how the raven silhouette fails as Tobi — telling the modeler what the mesh must
fix. The spike **cannot** show the crest (raw Hugin has none); the crest lives
entirely in the own-mesh phase. Prove the seam cheap, then invest in the mesh.

---

## 7. GROUNDED — the third-bird mechanism (corrects an earlier imprecision)

> This section records a mechanism fact that **narrows how §1.1 is implemented**.
> It does not change any DECIDED item — it replaces a hand-waved "reskin the
> flag" framing with the grounded truth.

**Vanilla's bird-identity axis is BINARY and STATIC — there is no "third slot"
to hand Tobi.** Read from the decomp this pass:
- `Raven.m_isMunin` is a **`bool`** (`:118245`). Identity is Hugin(false) /
  Munin(true) — two values, no third.
- `Raven.m_tempTexts`, `m_staticTexts`, and `m_instance` are all **`static`**
  (`:118301-118305`). There is **one shared text queue** and **one instance
  slot** the `Raven` class arbitrates.
- Each bird self-selects its texts by the flag: `GetTempText()` returns a queued
  text only `if (tempText.m_munin == m_isMunin)` (`:118537`); the static-text
  path mirrors it (`:118557`).
- Vanilla runs **two** `Raven` objects (both children of the `Ravens` prefab,
  each with `m_isMunin` fixed), delivering from that one shared queue.

**Consequence:** a genuinely distinct third bird cannot be expressed by "add a
third enum value" — the field is a bool, and the queue/instance are shared
statics. So the third-bird presentation needs **its own routing seam.** Two
viable shapes — this is the §7 impl fork the impl-spec closes (NOT a §5
design-level fork; both satisfy every DECIDED item):

- **7.a — Parallel SBPR bird class + own queue (lean).** SBPR ships its own
  lightweight guide-bird driver (its own instance + text queue + `Show` path),
  reusing the vanilla Raven **prefab machinery as a blueprint** (mesh-swap to the
  Tobi asset, same fly/perch behavior) but **not** contending for vanilla's
  `Raven.m_instance` static. Tobi and Hugin/Munin are independent; no risk of
  Tobi suppressing a vanilla hint or vice-versa. Costs more up-front (an own
  driver) but is the clean, decoupled path and honors ADR-0006 (additive; read
  vanilla as blueprint, don't clone-and-contend).
- **7.b — Harmony-extend the vanilla Raven to carry a third identity.** Patch the
  flag-based selection (`GetTempText`/visual pick) to understand a third
  identity token and mount the Tobi visual. Less new code, but it entangles Tobi
  with vanilla's single-instance static (the exact fragility that makes 7.a
  attractive) — a Tobi hint and a vanilla hint would fight over one bird
  instance/queue.
→ *Lean: 7.a — an own, decoupled guide-bird driver. It keeps Tobi additive at
the mechanism level (no contention with vanilla's static instance), matching the
DECIDED "additive, vanilla-untouched" spirit. The impl-spec finalizes this.*

> **Honesty note:** the earlier framing "Tobi is a third value on the same axis
> vanilla already uses" was imprecise — the axis is a bool, not an enum. The
> *decision* (distinct, additive, own-mesh third bird) is unchanged; the
> *mechanism* is an own routing seam (7.a lean), grounded here so the impl-spec
> doesn't inherit the wrong picture.

---

## 8. SBPR doctrine this must honor (from the repo)

- **Spec-first (ADR-0002).** This is the design doc; the impl-spec + code + any
  `SpecCheck` rows move together in the build PR. This doc alone is not buildable.
- **Additive construction, NO runtime prefab cloning (ADR-0006).** Build Tobi's
  GameObject from `new GameObject()` + `AddComponent`; read the vanilla Raven
  prefab only as a *blueprint* (`vprefab inspect`, `ZNetScene.GetPrefab`). Do not
  `Instantiate` the vanilla Raven and strip it. (§7.a is chosen partly because it
  keeps this clean.)
- **Clean-room (ADR-0001).** Vanilla Raven/Tutorial/Player source is fair to read
  and adapt (base game). Do not copy other mods' mascot code. Do not commit the
  private reference painting or any game binary/decompiled source.
- **net48 / BepInEx / HarmonyX, 0-warning build.** Daniel gates every merge.
- **Home module.** Tobi most naturally lives in `SBPR.Trailborne` (onboarding for
  Trailborne features) or a shared onboarding module — decide at impl-spec time.
  SBPR touches no tutorial/mascot code today (greenfield — grep confirms zero
  existing `tobi`/`cockatiel`/tutorial-mascot references in `src/`).

---

## 9. Observable acceptance tests (named — for the eventual impl-spec)

> "Logs green ≠ playable" — these are in-game checks the impl-spec will own.

- **AT-TOBI-DISTINCT:** an SBPR hint is delivered by a **visibly distinct** bird
  (the whiteface cockatiel), while vanilla hints still come from Hugin/Munin
  unchanged.
- **AT-TOBI-ADDITIVE:** triggering the full vanilla tutorial set shows **no**
  Tobi and **no** altered vanilla bird — Tobi appears only for SBPR keys.
- **AT-TOBI-ONCE:** a Tobi hint shows once per character and does not repeat
  across sessions (rides `m_shownTutorials` persistence).
- **AT-TOBI-COLOR:** Tobi shows **no yellow, orange, or red** on any surface —
  crest included (white-to-tip). *(Daniel is red/green colorblind; palette is
  named by value, and this AT is by-eye against the whiteface reference.)*
- **AT-TOBI-CREST:** the crest **articulates** — reads "up/alert" on arrival/speak
  and eases to relaxed when idle (per the Q2 drive). A static crest fails this AT.
- **AT-TOBI-FIDELITY:** Tobi reads as in-game art (256² skin, same render style,
  same poly class) — not an out-of-place import next to the vanilla bird.
- **AT-TOBI-NOPATCH:** the Tobi library patches **no vanilla gameplay method** to
  fire hints (registration hook + presentation only). *(Consumer piggybacks, §3.5,
  are separate and out of the library.)*
- **AT-TOBI-SPIKE (spike-phase only, throwaway):** a temporary skin on the raw
  vanilla Hugin mesh loads from the SBPR bundle and the third-bird seam fires
  in-game — proves plumbing before the mesh exists. (Not a ship AT.)

---

## 10. Cross-links

- **Vanilla mechanism source:** `assembly_valheim` — `Tutorial`@55345,
  `Raven`@118212, `Raven.AddTempText`@118754, `Player.ShowTutorial`@20541,
  `Player.m_shownTutorials`@15576 (save @19646/@19775).
- **AssetBundle precedent (in-repo):** the Bear Hide Tent — SBPR's first custom
  AssetBundle (`PARKED-2026-06-25.md`) — is the loading pattern Tobi's skin/mesh
  bundle follows.
- **Additive/clean-room doctrine:** `docs/decisions/0001-clean-room-no-jotunn.md`,
  `docs/decisions/0006-additive-prefab-construction.md`.
- **Prefab inspection tool:** `vprefab inspect Ravens|Hugin|Munin` (the measured
  §6.1 target came from this).
