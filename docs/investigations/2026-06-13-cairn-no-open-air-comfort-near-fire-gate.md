---
title: "Cairns grant no comfort in the open — the blocker is vanilla's NEAR-FIRE gate on the Resting grant, not our comfort-level patch"
status: current
last_updated: 2026-06-13
investigator: architect, clean-side — read Valheim's OWN decompiled assembly (permitted, ADR-0001)
spec_anchor: "docs/v0.1.0/planning/requirements.md §A2.1 (cairn comfort) + §A2.1b + the :388 cosmetic-fire row"
decomp_source: "Valheim full decomp (Bog Witch era) — verified locally /tmp/valheim_full.cs + /tmp/Player.decomp.cs"
trigger_card: t_1cdea346
implements_via: "engineer-systems implementation card (spec + code land in ONE PR per AGENTS.md)"
---

# Cairns: why they grant no comfort out in the open

## Trigger (Daniel, 2026-06-13 in-game playtest, verbatim)
> "I noticed our cairns still don't grant comfort out in the open. They should grant comfort
> regardless of shelter status when sitting near them like a camp fire does."

## TL;DR — the root cause (grounded in the decomp, NOT a guess)

There are **two independent vanilla gates** between "a comfort piece is nearby" and "you get the
**Rested** buff." Our cairn patch satisfies the first and never touches the second:

1. **Comfort LEVEL gate** — `SE_Rested.CalculateComfortLevel(bool inShelter, Vector3 pos)`. Only
   tallies nearby pieces `if (inShelter)`; in the open it returns a flat `1`. **Our patch already
   defeats this** (`CairnPatches.SE_Rested_CalculateComfortLevel_Postfix` clamps the result UP to the
   cairn floor *unconditionally*, ignoring `inShelter`). So the comfort *level* is correctly raised
   even in the open. **This is not the bug.**
2. **Near-FIRE gate** — `Player.UpdateEnvStatusEffects`. The `Resting` status (which is what actually
   produces/maintains the `Rested` buff) is only granted when a long AND-chain is true, and it ends in
   `&& flag`, where `flag = m_nearFireTimer < 0.25f`. **No fire ⇒ no Resting ⇒ no Rested, regardless of
   how high the comfort level is.** A cairn emits no heat (by design — it is not a campfire), so out in
   the open `flag` stays false and the buff is never granted. **THIS is the bug.**

A campfire works in the open because it emits a **Heat `EffectArea`** that calls `Player.OnNearFire()`
every tick, resetting `m_nearFireTimer` → `flag` true. A cairn is deliberately heat-free, so it never
resets the timer.

## The evidence (ground-truthed against the decomp)

### 1. Comfort LEVEL is shelter-gated — `SE_Rested.CalculateComfortLevel` (`valheim_full.cs:25397`)
```csharp
public static int CalculateComfortLevel(bool inShelter, Vector3 position)
{
    int num = 1;
    if (inShelter)                                   // ← open air skips the whole block
    {
        num++;
        List<Piece> nearbyComfortPieces = GetNearbyComfortPieces(position);  // GetAllComfortPiecesInRadius(pos, 10)
        nearbyComfortPieces.Sort(PieceComfortSort);
        // …dedup by ComfortGroup / m_name, sum piece.GetComfort()…
    }
    return num;                                       // open air → ALWAYS 1
}
```
- Called every 2 s from `Player.UpdateBaseValue` (`Player.decomp.cs:1823`): `m_comfortLevel =
  SE_Rested.CalculateComfortLevel(this)`, where the 1-arg overload (`:25392`) forwards
  `(player.InShelter(), player.transform.position)`.
- **Our cairn patch** (`Features/Cairns/CairnPatches.cs:111-124`) postfixes the 2-arg overload and clamps
  `__result` UP to `Cairns.GetCairnComfortBonus(position)` **unconditionally**. Correct, and **not** where
  the open-air failure lives. `GetComfortLevel()` returns the raised value in the open (≥3 for a T1 cairn).

### 2. The REAL blocker — the Resting grant requires NEAR FIRE — `Player.UpdateEnvStatusEffects` (`Player.decomp.cs:2023`)
```csharp
m_nearFireTimer += dt;
bool flag  = m_nearFireTimer < 0.25f;                 // "near a fire within the last 0.25 s"
bool flag2 = m_seman.HaveStatusEffect(Burning);
bool flag3 = InShelter();
bool flag4 = EnvMan.IsFreezing();
bool num   = EnvMan.IsCold();
bool flag6 = IsSensed();
bool flag7 = m_seman.HaveStatusEffect(Wet);
bool flag8 = IsSitting();
bool flag9 = EffectArea.IsPointInsideArea(pos, EffectArea.Type.WarmCozyArea, 1f);
bool flag11 = flag4 && !flag && !flag3;                                          // → Freezing
bool flag12 = (num && !flag) || (flag4 && flag && !flag3) || (flag4 && !flag && flag3);  // → Cold
// …
if (flag) m_seman.AddStatusEffect(CampFire); else m_seman.RemoveStatusEffect(CampFire);  // :2058

bool flag13 = !flag6 && (flag8 || flag3) && !flag12 && !flag11 && (!flag7 || flag9) && !flag2 && flag;  // :2066
if (flag13) m_seman.AddStatusEffect(Resting);          // :2069  (no resetTime → preserves SE_Cozy timer)
else        m_seman.RemoveStatusEffect(Resting);       // :2073  ← runs EVERY tick when not near fire

m_safeInHome = flag13 && flag3 && (float)GetBaseValue() >= 1f;                    // :2075
// …flag11→Freezing / flag12→Cold applied AFTER the Resting decision (:2076-2094)
```
- **`flag` (near-fire) is mandatory for `flag13`.** Cairn → no heat → `flag` false in the open →
  `flag13` false → `RemoveStatusEffect(Resting)` **every FixedUpdate**.
- `flag` is **SHARED**: it also drives `flag11` (Freezing), `flag12` (Cold), and the `CampFire` status.
  *Anything that makes `flag` true gives the cairn campfire-grade cold/freeze protection* — the exact
  heat leak the spec forbids (§A2.1b / :388, the PR #23 invariant).

### 3. `flag` is reset ONLY by a Heat `EffectArea` — `Player.OnNearFire` (`Player.decomp.cs:4068`) + `EffectArea` (`valheim_full.cs:105193`)
```csharp
public override void OnNearFire(Vector3 point) { m_nearFireTimer = 0f; }   // the ONLY writer of the timer to 0
// EffectArea: m_isHeatType = m_type.HasFlag(Type.Heat);  → a Heat area calls item.OnNearFire(...) each tick
```
Confirms: the vanilla way to get open-air Rested is a **Heat EffectArea**, which is precisely what the
cairn does not (and must not) have.

### 4. Resting → Rested bridge — `SE_Cozy` (asset-named "Resting", `valheim_full.cs:24808`)
```csharp
public override void Setup(Character c){ base.Setup(c); c.Message(Center, "$se_resting_start"); }  // :24832
public override void UpdateStatusEffect(float dt){
    base.UpdateStatusEffect(dt);
    if (m_time > m_delay /*=10s*/) m_character.GetSEMan().AddStatusEffect(m_statusEffectHash, resetTime:true); // "Rested"
}
```
- "Resting" must be **continuously maintained for `m_delay` = 10 s** before it grants the `Rested` buff.
  A fresh `AddStatusEffect("Resting")` after a removal re-runs `Setup` → `m_time = 0` and re-prints the
  center message. **This is the thrash hazard below.**

### 5. Rested TTL scales with comfort level — `SE_Rested.UpdateTTL` (`valheim_full.cs:25365`)
```csharp
m_ttl = m_baseTTL /*300*/ + (GetComfortLevel() - 1) * m_TTLPerComfortLevel /*60*/;
```
- Our comfort-level postfix already raises `GetComfortLevel()` in the open, so **once Rested is granted,
  its duration is automatically tier-correct** (T1 floor 3 → 300+2·60 = 420 s, … T5 floor 7 → 660 s).
  The comfort LEVEL decides *how long* Rested lasts; the near-fire gate decides *whether you get it at all*.

### 6. Nothing in vanilla removes the Rested BUFF (grep, this decomp)
- `RemoveStatusEffect(...Rested...)`: **zero hits.** The `Rested` buff is only ever *added* (sleep `:21458`;
  via the Resting/SE_Cozy bridge). It is never force-removed — it simply counts down its TTL. **This is the
  property the chosen fix rides.**

## The SBPR collision (why this is a DESIGN decision, not a clean drift fix)

`requirements.md:388` + §A2.1b LOCK the cairn as **no heat / no `EffectArea`** ("comfort comes from the
`SE_Rested` patch, NOT from fire"), reaffirming the v0.2.7 bonfire-leak fix (PR #23, t_9f8341c9) and the
ADR-0006 additive-construction pivot (the cairn is built from scratch — it never had a `Fireplace`/`EffectArea`
to strip). "Make the cairn grant open-air comfort like a campfire" collides with that lock, because the
*vanilla* mechanism for open-air Rested is exactly the Heat `EffectArea` we refuse to add. **The spec must
move with the fix** (AGENTS.md spec+code rule). Resolution: keep "no heat," and satisfy the *Resting/Rested*
requirement by a heat-free route. Mechanism below.

> ⚠️ Note for the record: the trigger card's root-cause writeup said the cairn is "fire-NEUTRALIZED" via
> `CairnTag.ConfigureCosmeticFire`/`ReconcileFire` (clone-then-strip). That describes the **pre-v0.2.8**
> path. The current code (ADR-0006) builds the cairn **additively from scratch** and grafts a small
> cosmetic flame with `BuildCosmeticFire`/`ReconcileFire` — there is **no donor `EffectArea` to disable**.
> The *net* fact is identical (no Heat area → no `OnNearFire` → no open-air Rested), but the mechanism note
> in the spec (:116, :388) is stale and is corrected in the spec-edit handoff.

---

## Mechanism decision (architect — LOCKED: refined "drive Rested directly")

**Chosen: a Harmony POSTFIX on `Player.UpdateEnvStatusEffects` that directly maintains the `Rested` buff
when a cairn is in range and the player otherwise qualifies — reading vanilla's OWN just-computed exclusion
statuses instead of re-deriving the flag algebra, and NEVER touching `m_nearFireTimer` (so zero heat).**

Predicate (all queryable via public API / post-state, no duplicated boolean algebra):
```
cairnInRange                     = Cairns.GetCairnComfortBonus(pos) > 0     // throttled — see perf note
&& !player.IsSensed()                                                       // flag6
&& (player.IsSitting() || player.InShelter())                              // flag8 || flag3
&& !seman.HaveStatusEffect(Cold) && !seman.HaveStatusEffect(Freezing)      // !flag12  (vanilla set these THIS tick)
&& !seman.HaveStatusEffect(Burning)                                        // !flag2
&& (!seman.HaveStatusEffect(Wet) || EffectArea.IsPointInsideArea(pos, WarmCozyArea, 1f))  // !flag7 || flag9
⇒ seman.AddStatusEffect("Rested", resetTime: true);   // refresh; TTL = 300 + (GetComfortLevel()-1)*60
```
When the predicate is false we do **nothing** (no removal) — Rested then counts down its TTL naturally,
matching how vanilla lets Rested linger after you leave a campfire.

### Why this shape (the three non-obvious reasons)

1. **It rides a buff vanilla never removes (`Rested`), so there is no thrash.** The naive "postfix that
   re-adds *Resting* after vanilla removed it" is **fatally broken**: vanilla calls `RemoveStatusEffect(Resting)`
   every tick in the open, our re-add re-runs `SE_Cozy.Setup` → `m_time = 0` and re-prints `$se_resting_start`
   every tick → the 10 s delay never elapses → Rested is *never* granted, plus a center-message spam storm.
   Targeting `Rested` (never vanilla-removed) sidesteps the fight entirely and lets us **throttle freely**.
2. **It reads vanilla's post-state for the exclusions instead of re-deriving them.** By the time the postfix
   runs, vanilla has already applied Cold/Freezing/Burning/Wet for THIS tick using the *real* (no-fire) flags.
   Reading those statuses is faithful **by construction** and drift-resistant — if IronGate changes the cold
   model, we inherit it for free. (This is the refinement over the card's Option 3, which proposed
   re-implementing `flag7/flag11/flag12` — brittle.) **AT-CAIRN-NO-STORM-REST holds automatically:** in
   Mountains/storm the player IS Cold/Freezing/Wet → predicate false → no Rested.
3. **It never touches `m_nearFireTimer`, so it leaks no heat.** `flag11`/`flag12`/`CampFire` are computed by
   vanilla with the unmodified `flag` (false) → the cairn still gives no freeze-thaw, no warmth vs cold, no
   `CampFire` status. **AT-CAIRN-NOT-A-FIRE holds by construction.**

### Options REJECTED (with grounded reasons — investigate-don't-pre-lock honored)

- **Option 2 — heat-less "comfort" `EffectArea` calling `OnNearFire`.** `OnNearFire` sets
  `m_nearFireTimer = 0` → `flag` true → but `flag` is shared: it suppresses `flag11`/`flag12` (gives
  campfire-grade cold/freeze protection) and adds the `CampFire` status. **Re-leaks heat by construction —
  violates AT-CAIRN-NOT-A-FIRE and the PR #23 invariant.** Rejected.
- **Option 1 as a TRANSPILER** (rewrite `flag13`'s trailing `&& flag` → `&& (flag || cairnProxy)`).
  Conceptually the "purest" fix (rides 100% of vanilla's Resting machinery: 10 s ramp, message, TTL,
  exclusions). Rejected because: (a) **no transpiler precedent in this repo** — it is prefix/postfix +
  additive by doctrine ("simple over clever"); introducing IL rewriting is an unbudgeted maintenance
  burden; (b) **fragile** — `flag13` short-circuits into a branch chain on a 50 Hz method; isolating the
  `flag` load that feeds `flag13` from the ones feeding `flag11`/`flag12`/`CampFire` is version-brittle;
  (c) **it would also flip `m_safeInHome`** (`:2075` uses `flag13` directly) → under a dry roof + cairn with
  no fire, the spot becomes "safe in home" (logout/no-spawn semantics) — an unintended side effect that
  direct-Rested avoids.
- **Option 1 as a plain POSTFIX re-adding `Resting`.** The thrash in reason (1) above. **Fatal.** Rejected.

### Deviation flagged for Daniel (cheap to revert)
Direct-Rested grants the buff **as soon as the player qualifies**, skipping vanilla's **10 s SE_Cozy ramp**
(a campfire makes you dwell ~10 s before Rested appears). This is a minor, arguably-nicer UX (sit → rested)
and the stated intent ("grant comfort … like a camp fire") is about *open-air comfort*, not the ramp. If
Daniel wants strict campfire parity, add a lightweight 10 s dwell timer on `CairnTag`/the patch before the
first grant — isolated, low-risk. **Defaulting to immediate grant; flagging the ramp as a Daniel call.**

### Interactions verified (no regressions)
- **Near shelter + real fire (today's working path):** vanilla still grants Rested via Resting/SE_Cozy; our
  postfix idempotently *refreshes* the same `Rested` buff (`resetTime:true`). One buff, max-clamped comfort
  LEVEL → **no double-count. AT-CAIRN-COMFORT-STACK holds.**
- **Under a roof + cairn, no fire:** today gives no Rested; with the fix the cairn (as the comfort source)
  grants it — a sensible superset of "comfort regardless of shelter status," consistent with the design.
- **`m_safeInHome` / base value:** untouched (we never set `flag13`). A cairn does not make an open or roofed
  spot "safe in home." Correct.

## Perf note (load-bearing — `UpdateEnvStatusEffects` runs at ~50 Hz)
`UpdateEnvStatusEffects` is called every `FixedUpdate` for the **local owner player only**
(`Player.decomp.cs:727` gates `IsOwner()` + `m_localPlayer == this`). Do **NOT** run a fresh
`Physics.OverlapSphere` (the `cairnInRange` check) at 50 Hz. Throttle the cairn query to **≥ 1 s** (Rested TTL
is 300 s+, so even 2–5 s is imperceptible) and cache the bool; the cheap post-state status reads can run each
invocation. Equivalent acceptable alternative: stash `cairnInRange` from the existing 2 s
`CalculateComfortLevel` postfix path and read the stash here (zero new physics queries). **Engineer picks the
container + throttle; recommend a per-player throttle keyed on the local player.**

## Acceptance tests (named — refined from the card; "logs-green ≠ playable", real bar is Daniel in-game)
- **AT-CAIRN-OPEN-COMFORT** — Sit/stand within `CairnComfortRadius` (10 m) of a cairn **in the open** (no
  shelter, no fire, fair weather) ⇒ the **Rested** buff appears, at the tier-scaled duration (T1 floor 3 →
  420 s … T5 floor 7 → 660 s). Parity with a campfire granting Rested in the same open spot (modulo the ramp
  deviation above).
- **AT-CAIRN-NOT-A-FIRE (regression)** — The cairn provides **NO heat**: does not thaw Freezing, does not
  count as warmth vs Cold/Mountains, adds no `CampFire` status, and re-introduces no `EffectArea`/Heat. Stand
  at a cairn in a cold/Mountain biome with no other heat ⇒ still Cold/Freezing (and therefore no Rested).
- **AT-CAIRN-NO-STORM-REST (regression)** — No Rested while the player is **Wet (uncovered) / Cold / Freezing
  / Burning**. Verify in rain with no cover and in a freezing biome near a cairn ⇒ no Rested. (Holds because
  the predicate reads vanilla's own just-set exclusion statuses.)
- **AT-CAIRN-COMFORT-STACK (regression)** — Near shelter, the existing comfort-floor behavior is unchanged:
  one `Rested` buff, comfort LEVEL max-clamped to the cairn floor, no `ComfortGroup` double-count, TTL scales
  with the floor.
- **AT-CAIRN-SAFEHOME-UNCHANGED (new regression)** — A cairn does **not** flip "safe in home": an open or
  roofed-but-fireless spot near a cairn does not gain `m_safeInHome` semantics (no-spawn / safe-logout).
- **AT-CAIRN-NO-MESSAGE-SPAM (new regression)** — Sustained presence near a cairn produces **no** repeating
  `$se_resting_start` / status-flicker center-message spam (guards against the rejected thrash design).
- **Spec moved with code** — `requirements.md` §A2.1 + §A2.1b/:388 record that the cairn grants open-air
  Rested-comfort **without heat**, reconciling the "no `EffectArea` / not a heat source" lock with the new
  behavior, and correct the stale "fire-neutralized clone" mechanism note to the ADR-0006 additive reality.

## Scope / routing
- **In:** make cairns grant their comfort floor (the Rested buff) out in the open without becoming a heat
  source; update the spec to match. **Out:** comfort *values* (floors 3/4/5/6/7 unchanged), the cosmetic
  flame visual, any other piece's comfort, the 50 Hz hot-path budget.
- **Clean-side → architect (design, here) → engineer-systems (impl).** All vanilla internals only
  (`Player.UpdateEnvStatusEffects`, `SE_Rested`, `SE_Cozy`, `EffectArea`) — fair to read/adapt per ADR-0001.
  No third-party mod code; no RE-firewall card.
