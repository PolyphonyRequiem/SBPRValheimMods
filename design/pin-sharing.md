# Pin-sharing investigation

> _Pre-design investigation for the Niflheim Modpack — how vanilla Valheim handles
> player-to-player pin sharing, what BetterCartographyTable did about it, and what
> SBPR should do for our Painted Signs + Seer's Amulet systems._

## TL;DR

- **Vanilla DOES share pins across players, but only via cartography tables, and the model is "all-or-nothing per write."** Every saveable pin on your map is broadcast every time you write to a table, and every pin you've previously read from any table can get *deleted* if its owner later removes it from theirs.
- **The vanilla model is exactly the "cluttered, frustrating, all-or-nothing" experience BetterCartographyTable's author calls out**, and it's broadly hated in the multiplayer community.
- **For Niflheim, my recommendation is a "Per-pin explicit sharing" model** — the same architectural insight BetterCartographyTable uses, but reimplemented clean-room under SBPR.* with our pigment-color semantics layered on top.
- **No voting, no merging, no CRDTs.** Voting is a real distributed-systems rabbit hole; the practical answer is "pins have owners, sharing is opt-in per-pin, conflicts are rare in practice because of color/position naming."

---

## How vanilla pin-sharing actually works

### Storage shape

`Minimap.m_pins : List<PinData>` is a flat client-local list. There is **no RPC traffic for pins** — pin add/remove is a pure client operation. Pins are persisted in two places:

1. **Per-player profile** — `PlayerProfile.GetMapData()` / `SetMapData()` writes the player's entire map state (fog + pins) to disk. Survives logout.
2. **Per cartography table ZDO** — when you Use a table with the Write switch, a compressed snapshot of (fog + pins) is stored in the table's `ZDOVars.s_data`. This is the only mechanism by which one player's pins reach another player.

### What `PinData` actually contains

From the decomp (line 46513-46566):

```csharp
public class PinData {
    public string m_name;                  // user-typed label or auto-generated
    public PinType m_type;                  // enum: Icon0..4, Death, Bed, Shout, Boss, Player, ...
    public Sprite m_icon;                   // visual
    public Vector3 m_pos;                   // world coords
    public bool m_save;                     // persisted? (false for ephemeral pins like Player/Shout)
    public long m_ownerID;                  // PlayerID of pin creator (0 = "mine")
    public PlatformUserID m_author;         // Steam/Xbox identity for UGC filtering
    public bool m_shouldDelete;             // tombstone flag during cross-player sync
    public bool m_checked;                  // crossed-off
    public bool m_doubleSize;
    public bool m_animate;
    public float m_worldSize;
    // ... UI bits (m_uiElement etc) are render-only
}
```

The key fields for sharing are `m_save`, `m_ownerID`, `m_author`, and `m_checked`.

### The actual sharing wire format

`Minimap.GetSharedMapData(byte[] oldMapData)` (line 48754) is what `MapTable.OnWrite` calls. The packet shape, version 3:

```
int   version                  // = 3
int   explored_array_length    // typically 2048 * 2048
bool[] fog_of_war              // 4MB raw — every map tile
int   pin_count                // number of saveable, non-Death pins on the writer's map
for each pin {
    long          owner_id     // 0 if owner==writer, else writer's playerID
    string        name
    Vector3       pos
    int           pin_type
    bool          is_checked
    string        author_platform_id   // Steam ID etc
}
```

**Every saveable pin** on the writer's map is included in every write. There is no "this pin is private," no "share only these pins," no "share only my own pins." If you've collected 200 berry pins and 14 mushroom-cluster pins and 3 dungeon pins, the table gets all 217.

### The read side and the destructive sync

`Minimap.AddSharedMapData(byte[] dataArray)` (line 48823) is what `MapTable.OnRead` calls. The behavior is genuinely strange:

```csharp
// Step 1: tombstone all my "not-mine" pins
for each pin in m_pins where m_ownerID != 0 && m_ownerID != playerID:
    pin.m_shouldDelete = true

// Step 2: read the table's pin list
for each incoming pin:
    if I have a pin within 1m of this position:
        // it's mine OR I already had it — unmark for delete, do nothing else
        closest.m_shouldDelete = false
    elif owner != my playerID:
        // new pin, owned by someone else — add it
        AddPin(pos, type, name, save=true, isChecked, owner, author)

// Step 3: purge tombstoned pins
for each pin where m_shouldDelete == true:
    RemovePin(pin)
```

**Consequences of this design:**

1. **You lose pins you previously got from other tables.** If Alice writes table T1 (containing pins P1, P2). Bob reads T1, gets P1+P2. Bob then reads table T2 which doesn't have P1+P2 — and Bob *loses* P1+P2. This is the "pins disappear / reappear constantly" complaint.

2. **You cannot un-share a pin you already wrote.** Once you write a table that contains pin X, every other player who reads that table gets X. If you later remove X from your own map and write the table again, the table is updated — but readers who already pulled X *don't know to remove it*.

3. **You cannot privately keep pins separate from sharing.** Every saveable pin you have is broadcast every write. No checkbox, no toggle, no folder.

4. **"Checked" status doesn't sync.** If Alice crosses off shared pin P1 on her map, then writes, the table records `checked=true`. Bob reads — his copy of P1 is now checked. But if Bob then crosses off P1 himself and writes back, the table marks it checked — but Alice's existing copy doesn't update unless she re-reads, and *even if she does*, the 1m-proximity match in Step 2 leaves her own pin's checked state unchanged.

5. **Position is the conflict-resolution key.** Two pins within 1m of each other are considered "the same pin." There is no merging of name, type, or color — first one wins, second one is silently dropped.

This is the multiplayer pin experience Valheim ships with today. It's bad, and BetterCartographyTable was made specifically to fix it.

---

## What BetterCartographyTable did about it

Their architectural moves (cribbed for understanding, not for code):

1. **Subclass `PinData` → `SharablePinData`** with a `SharingMode` enum (`Private | Public | Guild`). Default = `Private`.

2. **Harmony-patch `Minimap.AddPin`** to *always* return a `SharablePinData` instead of a plain `PinData`, so every pin in `m_pins` is sharable. This is the cleverest move — it means downstream code that iterates `m_pins` doesn't need to know about the new type; it just works through `PinData` polymorphism.

3. **Per-cartography-table mode** — each table is either "Public" or "Guild" (if Smoothbrain/Guilds is installed). The table's mode controls *which pins are eligible to be uploaded to it*.

4. **Real-time sync** when a table is open by multiple players — pins update live, not just on Use.

5. **Pin write filtering** — only `IsShared && matching SharingMode` pins go into the table's packet.

6. **Pin read merging** — when reading a table, incoming pins are integrated as `Public` or `Guild` SharablePinData (not transmuted into your own pins). They show up with a visual treatment that distinguishes "this is mine" vs "this came from a table."

7. **Pin visibility toggle** in the map UI — show/hide shared pins independently of your own.

This is essentially **what vanilla should have shipped.** It's the right shape. The pattern is well-trodden, well-validated, and well-loved by the multiplayer community.

---

## SBPR's specific needs

We have two new pin-creation paths that don't exist in vanilla:

1. **Painted Sign pins** (player walks up to a sign, presses pin button, gets a pin matching the sign's pigment color). The pin should be tagged with the *sign's identity* (its ZDOID + author), not just the player's.

2. **Seer's Amulet pins** (player wears the amulet, sees a wisp, presses pin button, gets a pin for that pickable cluster or location). Personal-only by nature — the amulet is a personal lens.

Plus our **fixed-shroud cartography table** rebalance (Q1) — tables now have a *defined 1000m visible window*, which means a table's stored data is semantically a *regional snapshot* tied to where the table was built.

These three changes shift the design space:

- **Painted Signs are inherently a public artifact.** Anyone can see the sign in the world. So pin-sharing from a sign isn't an inherent privacy decision — *the sign is already public information by being placed*. The only question is whether the *pin* travels via the table's regional snapshot.

- **Seer's Amulet pins should default to private.** The whole flavor is "I have the lens, I can see this thing, my map records it." Sharing is a separate action.

- **Tables now have *position-defined regional identity*.** A table at coordinates X only stores pins+fog within its 1000m window. This means we can ask the structurally cleaner question: "which table holds this pin?" rather than "is this pin shared globally?"

---

## Four model options

### Option A — Pure-vanilla behavior

Don't touch pin-sharing at all. Painted Sign pins and Seer's Amulet pins are just regular pins; they ride the vanilla all-or-nothing sharing through map tables.

**Pros:** Zero new code. Behavior matches what vanilla players expect.

**Cons:** Inherits every vanilla complaint (clutter, disappearance, no opt-out). Worse: our Painted Sign system encourages players to pin *every sign they pass*, which would absolutely flood the all-or-nothing share.

**Verdict:** No. This would make our Painted Sign feature *worse* than not having it.

### Option B — Per-pin sharing toggle (BetterCartographyTable model, reimplemented)

Same architectural shape as BCT: pins are private by default, players individually opt-in to share specific pins to a table. Cartography tables now have a "shared inbox" that's separate from the writer's full pin list.

**Pros:** Clean, proven, matches multiplayer expectations. Lets players curate what they share. Makes Painted Signs work properly (you pin a sign for yourself; you optionally publish it to a table when you visit one).

**Cons:** Requires us to subclass `PinData` and patch `Minimap.AddPin`. Real engineering effort — call it 200-400 LOC including the UI for marking pins as shared.

**Verdict:** Strong contender. Doctrine-clean (we reimplement, not copy). Solves the problem properly.

### Option C — Sign-driven implicit sharing

Painted Sign pins are *intrinsically shared via their sign's ZDO*. The sign itself is the pin's "home"; any player who sees the sign and pins it gets a pin that *points back to the sign's ZDO*. When a player reads a cartography table that covers the sign's region, they get the sign's pin automatically — but it's keyed to the sign, not added to their personal map list independently.

Seer's Amulet pins remain personal (Option A behavior).

**Pros:** No pin subclassing needed. The sign IS the shared artifact; pinning it is just "I want this sign to show on my map." Conflict resolution is automatic — there's exactly one canonical position/name/color per sign, because there's exactly one sign. Editing the sign edits the pin for everyone who has pinned it.

**Cons:** "Sign-keyed pin" requires our own pin lifecycle (does the pin live in `m_pins`? in a separate list? what happens when the sign is destroyed?). Roughly the same engineering effort as Option B, but in a different shape. Also doesn't fix anything about *non-sign* pins (regular vanilla map pins still suffer the vanilla problems).

**Verdict:** Elegant for signs specifically. Less general than B.

### Option D — Hybrid: Option B for the architecture, Option C for the sign UX

Use the per-pin sharing model (B) as the foundation — every pin is sharable, default private, explicit opt-in to share. Then layer the sign-driven UX on top: when a player creates a pin from a Painted Sign, it's pre-marked with `SharingMode = Public` AND tagged with the sign's ZDOID for identity. Later sign edits propagate via the same regional-table mechanism.

For Seer's Amulet pins — default `Private`, player can opt-in to share like any other pin.

**Pros:** Best of both. Solves vanilla pin-clutter problems generally (Option B benefit). Makes sign-pinning *frictionless and obviously shared* (Option C benefit). Maintains our doctrine that everything is opt-in / explicit.

**Cons:** Most engineering work. Probably 500-700 LOC total. Some of it is UI (per-pin sharing-mode picker) which requires asset bundle UI prefabs.

**Verdict:** This is the right answer for what we're building.

---

## Recommendation: Option D, scoped to Painted Signs first

Build it in two passes, both as separate PRs:

### Pass 1 (with the first Painted-Signs PR)
- Implement `SBPR.Pact.SharablePinData : PinData` (or our own field-equivalent — TBD: subclass vs composition, depending on what plays nicest with Harmony)
- Harmony-patch `Minimap.AddPin` to return our type
- Patch `MapTable.OnWrite`/`OnRead` to filter on `SharingMode` and integrate incoming pins as separate-but-visible
- Painted Sign pins are auto-tagged `Public` and carry the sign's ZDOID as a new field `m_sourceSignZDOID`
- **No UI for the per-pin toggle yet.** All sign-pins are public-by-creation; all other pins default to vanilla-like (`Public` if they were already in `m_pins` at install time, so nothing breaks for existing players).

### Pass 2 (with the Seer's Amulet PR)
- Add the per-pin sharing-mode UI (a small popover when you right-click a pin on the map)
- Seer's Amulet pins default to `Private`
- Player can promote any pin to `Public` via the popover

### What we explicitly are NOT building

- **No voting.** Voting requires durable per-pin per-player state, conflict-resolution windows, and quorum rules. None of these are needed for our actual use case — players don't typically duel-pin the same coordinate.
- **No CRDT merging of pin attributes.** Pins have a clear owner (the player who placed it OR the sign that defines it). Owner edits propagate; non-owner edits don't.
- **No "guild" mode** (BCT-style). We don't have a guild system on Niflheim. If we ever add one, we extend the SharingMode enum then; the architecture supports it cleanly.
- **No automatic merging of nearby pins.** Vanilla's 1m-proximity merge is destructive; ours doesn't need it because explicit owner identity makes "is this the same pin?" answerable.

---

## Open questions still on the table after this investigation

1. **`SharablePinData` as subclass vs composition?** Subclass is cleaner (BCT-proven); composition (a parallel dictionary `pin -> mode`) is safer if other mods touch the pin list. Decision deferred until implementation start. **Lean: subclass**, matches BCT and Harmony plays well with it.

2. **What happens to a sign-pin when the sign is destroyed?** Three options:
   - (a) Pin stays, becomes orphan (gets a "[Removed]" tag on the name)
   - (b) Pin auto-deletes for everyone who had it
   - (c) Pin stays for owner (the player who pinned it), deletes for everyone else
   **Lean: (c)** — your personal record persists, but the public sharing stops because the source artifact is gone.

3. **Real-time sync via table — yes or no?** BCT does it (when two players have the same table open, edits propagate live). Cool but adds RPC complexity and edge cases. **Lean: NO for v1**, defer to a later pass. Vanilla-style "sync on Use" is enough.

4. **Pin visibility filter UI?** A toggle to hide all-shared, all-private, or all-from-a-specific-table. **Lean: defer to v2** — vanilla already has the icon-type filter strip; piggyback on that if we need more.

5. **Server-side validation of incoming sign-pin ZDOIDs?** If a malicious client spams "I pinned sign Z" with a forged ZDOID, do we let it pollute other players' maps via the table? **Lean: server-gate the validation** — when a sign-pin is integrated, verify the ZDOID exists and is actually a Painted Sign in the table's region. Cheap check, no UX cost. This is the "real anti-cheat = server-side sanity gates" doctrine in action.

---

## Implementation effort estimate

| Pass | Scope | LOC est | Risk |
|---|---|---|---|
| Pass 1 | SharablePinData + AddPin patch + table filter/integrate + sign-pin auto-tag + server validation | ~400 | Medium (needs careful PinData lifecycle handling, but precedent exists) |
| Pass 2 | Per-pin mode UI + Amulet integration | ~200 | Low (UI only, mode field already exists from Pass 1) |
| **Total** | | ~600 | Medium overall |

---

## Decision needed from Daniel

Before any code is written:

- **Does Option D land for you?** Or is there an angle I haven't considered?
- **Are the three "lean" answers in Open Questions above acceptable as defaults**, or do you want to weigh in?
- **Pass 1 is part of the Painted Signs PR — does that scope feel right?** The alternative is to ship Painted Signs with vanilla pin behavior first (Option A), then retrofit Option D in a later PR. That's faster initially but creates a behavior change between v0.1 and v0.2 that existing players would notice.

Once you sign off, this design lives. Tech spec will reference back to this doc rather than re-litigating the model.
