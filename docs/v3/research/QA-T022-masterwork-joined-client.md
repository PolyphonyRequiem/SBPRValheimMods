---
status: current
card: t_997667c4
pr: 381
head: ae18653
verdict: BLOCKED-BY-DESIGN (joined-client node DoD unmet by the code, not by tooling)
---

# QA T022 Masterwork — joined-client / in-world Workmanship artifact

## What this card asked for

A decision-grade **live joined-client** artifact on a real `host + genuinely joined
remote client` topology, proving four ATs *in-world* (not just host logs):

1. `AT-MASTERWORK-ISSUE` — an active-Masterwork crafter crafts an eligible
   non-stackable durable item and the produced instance **visibly carries**
   `Workmanship=Masterwork` in-world.
2. `AT-ITEM-UPGRADE-PRESERVE` — upgrading preserves a still-valid stamp.
3. `AT-ITEM-TRANSFER` — transferring the item to **another client** preserves
   validation.
4. `AT-ITEM-TAMPER-DEGRADE` — a forged/hand-edited stamp degrades to vanilla on
   the joined client.

Authorized topology (coordinator, comment 2026-07-19 07:38): isolated **headless
dedicated server as authoritative host + one genuine GABS-licensed joined client**
as remote crafter/receiver.

## Verdict: the required joined-client artifact is IMPOSSIBLE under PR #381 — this is a design gap, not a tooling limit

I did **not** launch a GPU client. The determination below is a *code* fact,
established by reading the shipped runtime seam; a live session cannot change it,
and launching the gated GPU client would only illustrate a conclusion the source
already forces. Per AGENTS.md "logs green ≠ playable" — here the sharper finding is
**"even a live client can't make the joined-client path playable, because the
integrity key never leaves the server."**

### The two load-bearing gates (both in `MasterworkIssuanceObserver.DoCrafting_Postfix`)

`src/SBPR.Niflheim.HomesteadStones/Features/Crafting/MasterworkIssuanceObserver.cs:77-81`

```csharp
var key = Armed;                       // null on a pure client (never armed)
var server = LocalProgressionObserver.Server;
if (key == null || server == null) return;   // pure client / not composed — fail closed.
if (__instance == null || player == null) return;
if (player != Player.m_localPlayer) return;  // only the LOCAL crafter is ever stamped
```

- `Armed` is set **only** in `FoundationalRuntimeBootstrap` (line 98), and that
  bootstrap runs **only** when `ZNet.IsServer()` is true
  (`FoundationalRuntimeBootstrap.cs:37`). So `Armed != null && Server != null`
  holds **only inside the authoritative-server process**.
- The postfix additionally requires `player == Player.m_localPlayer` — i.e. the
  crafter must be the **local player of that same server process**.

The intersection of "is the authoritative server" AND "is the local crafting
player" is satisfied in exactly **one** topology: a **listen-host** (the process
that is the server is also the human crafter). It is satisfied by **neither** actor
in the authorized topology:

| Actor | `IsServer()` → Armed/Server | `player==m_localPlayer` on it | Issues a stamp? |
|-------|------------------------------|-------------------------------|-----------------|
| Headless dedicated server | ✅ armed & composed | ❌ has **no** local Player (never runs `DoCrafting`) | **No** |
| Pure joined client (crafter) | ❌ `Armed==null`, fails closed at line 79 | ✅ | **No** |

**⇒ The authorized topology (headless dedicated + joined-client crafter) issues
ZERO Workmanship stamps. `AT-MASTERWORK-ISSUE` cannot be produced on it at all.**

### The key never reaches a client — so a joined client also cannot VALIDATE a transferred stamp

Validation is a keyed HMAC-SHA-256 read (`WorkmanshipCodec.Read` →
`WorkmanshipIntegrityKey`). The raw key is server-only by construction
(`ItemProvenance.cs:205-224`, comment: "The raw key lives only server-side") and
**there is no RPC / ZRoutedRpc / replication path** that ships it or a validated
result to clients (grep of the whole mod: zero `ZRoutedRpc|RegisterRPC` for
workmanship/masterwork; the only client-side crafting patch is the T021
`RefinedWorkshopStationLevelPatch`, unrelated to issuance).

Consequently, on a pure joined client `Armed == null` → the codec cannot verify the
token → an issued item's stamp **presents as vanilla** (the PR's own evidence doc
lines 41-46 and 96-108 admit exactly this). So even the *cross-client transfer*
last mile (`AT-ITEM-TRANSFER` "preserves validation" **on the receiving client**)
is unobservable: the stamp survives as opaque `m_customData` bytes but is never
surfaced/validated client-side.

### Net: every AT that needs the JOINED CLIENT to observe or validate is unreachable

| AT | Needs joined client to… | Reachable under PR #381? | Why |
|----|--------------------------|--------------------------|-----|
| AT-MASTERWORK-ISSUE (joined crafter) | be issued a stamp in-world | ❌ | joined client fails closed (`Armed==null`); only a listen-host local crafter is stamped |
| AT-ITEM-UPGRADE-PRESERVE (joined) | upgrade + re-validate | ❌ | no key on client → cannot validate |
| AT-ITEM-TRANSFER (→ joined receiver) | validate received stamp | ❌ | no key on client → "presents as vanilla" |
| AT-ITEM-TAMPER-DEGRADE (joined) | show forged→vanilla | ⚠️ vacuous | client shows *every* stamp as vanilla (keyless), so degrade is indistinguishable from the legitimate case |

The single topology where all four are real and validated is the **listen-host's
own inventory** — which is precisely the "host-first / host-side" proof the card
explicitly rejects as **not a waiver** for the node's own DoD.

## What IS already proven (and is genuinely solid)

- Engine-free CLEAN-side: `tests/NiflheimMasterworkTests.cs` 21/21, full suite
  1386/1386 (adversarial reviewer confirmed; not re-run here — not the gap).
- Host-authoritative data-layer issuance + tamper/transfer *semantics* headless.
- Both net48 Release builds 0w/0e.

None of that closes the **joined-client in-world** gap this card exists for.

## Recommended resolution (needs a human/architecture decision)

The gap is structural, so it can't be QA'd away. One of:

1. **Accept the scope split explicitly** — amend T022's per-node DoD so the
   joined-client issuance/validation last mile is formally owned by T024 (the code
   + evidence doc already assume this), and merge PR #381 as the host-authoritative
   slice. This is a spec/DoD change, per AGENTS.md it rides in the same PR.
2. **Implement client delivery now** — add server→client replication of the
   validated Workmanship (or a client-verifiable signature) so a joined client can
   surface `Workmanship=Masterwork`, then re-run this live QA. This is real feature
   work (mirrors the T021/T026 "host-first-then-pure-client-delivery" remediations),
   not a QA task.

Until one of those lands, **PR #381 must not merge against a node-own joined-client
DoD** — the required live artifact is unachievable with the current code.

## Safety / side-effects this run

None. No GPU client launched, no server started/stopped, no deploy, no world
touched. Production Niflheim (2456) and Heistan (2466) left untouched; the T030
worker's client not touched (none running). The stale GABS `valheim: running`
status is orphaned (no `valheim.x86_64` process present) — left as-is.
