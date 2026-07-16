---
title: "Server-side characters, account management, and Discord linking — design discussion"
status: idea
purpose: >
  Chart the design space for authoritative server-side characters, account
  management, and optional Discord linking on Niflheim, BEFORE any aggregate,
  OAuth flow, or bot automation is committed. Grounds every load-bearing claim
  against the accepted identity substrate (data-model.md, research.md, PR #317
  transport-bound identity). No implementation, no OAuth setup, no bot role
  automation, no migration, no spec lock. Produces the glossary, 2-3 architecture
  shapes, lifecycle tables, a migration/recovery model, a minimum vertical slice,
  and Daniel decision clusters presented one at a time.
author: architect
card: t_67ba7117
grounds_against:
  - homestead-stone-progression-data-model.md
  - homestead-stone-progression-research.md
  - "PR #317 (025b24c) transport-bound identity"
---

# Server-side characters, account management, and Discord linking — design discussion

**Daniel's framing (from the card / Discord seed):** a dedicated design discussion
for **server-side characters, account management, and Discord linking** — three
layers kept explicit, no premature collapse into one identity blob.

**Reading rule for this doc.** Every load-bearing claim is either cited to the
substrate (`data-model.md` / `research.md` field or rule, or PR #317's shipped
identity code) or marked **[OPEN → Daniel]** / **[OPEN → RE]**. Where I
recommend, the recommendation is labelled **[ARCHITECT LEAN]** and is *not* a
lock. Nothing here mutates a spec, stands up an OAuth app, or provisions a bot.

---

## 0. What the substrate already fixes (the security floor)

Before drawing any account or Discord model, these are the identity facts that are
already **accepted and shipped**. Any architecture below must compose from them or
consciously, explicitly extend them — never reintroduce something they rule out.

| Fact | What it means | Cite |
|---|---|---|
| `AccountId` = authenticated provider subject | Authority / grouping / audit only. **Never** gameplay progression ownership; **never** accepted from an unauthenticated payload | data-model §Stable identity (`AccountId` row); research L84-87 |
| `CharacterId` = server-bound subject *within* an account | Owns all gameplay progression (AP/BP, purchases, durable outcomes, relationships). Never accepted from an unauthenticated payload | data-model §Stable identity (`CharacterId` row), §Aggregate 3 inv. "Gameplay progression belongs to the character, not the account" |
| Identity is **transport-bound**, not payload-claimed | The forgeable routed `m_senderPeerID` is rejected; identity is resolved via direct per-peer ZRpc → real `ZNetPeer` → server-owned facts. A forged peer/admin id cannot redirect authority | PR #317 Blocker 2 (`AuthenticatedSenderIdentity.cs`) |
| Account subject = platform/socket host | `PlatformSubject` = the authenticated socket identity; it feeds `AccountId` via a **server-owned platform→account resolver**, never a client string | `AuthenticatedSenderIdentity.cs`; research L160-167 (candidate A shipped, candidate E standing) |
| Character subject = durable `player:<s_playerID>` | `s_playerID` is stable across session, rename, restart, reconnect. The live character ZDOID is reconnect-unstable and is deliberately **not** a durable subject | PR #317 Blocker 3; `ServerCreatorIdentity.CharacterSubject` |
| A Valheim PlayerID / portable save is **insufficient authentication** | The exact production account provider is still an **unresolved P0 choice**; must be proven before any production mutation endpoint | research L89-91 |
| Account–Stone exclusivity is an **index**, policy-driven | `(AccountId, StoneId) → active character(s)`; Homestead = at most one sibling active; Community Attunement permits siblings, Community Bond stays account-exclusive | data-model §Aggregate 2 |
| Every mutation is receipted, revision-checked, idempotent, recoverable | `OperationReceiptStore` rehydrates from journal at boot; the journal is the single authority | data-model §Aggregate 5; Gate-A evidence |

**Critical starting fact:** there is **no account-management aggregate, no login
ceremony, and no Discord model today.** What exists is a *transport-derived*
`(account, character)` pair proven correct against forgery and reconnect (PR #317),
plus a **standing but unbuilt** choice to put a server-owned `platform→AccountId`
resolver in front of it (research candidate E). So the honest first questions are:
"**does the transport-derived pair need to become a persisted account record, and
if so what does that record own?**" and "**where — if anywhere — does Discord
attach without becoming an authentication root?**"

---

## Ubiquitous-language glossary

The vocabulary this whole discussion (and the eventual spec) must use consistently.
New terms are flagged **[NEW]**; the rest are pulled from the accepted substrate.

| Term | Definition | Source |
|---|---|---|
| **Platform subject** | The authenticated socket/platform identity of a connection (e.g. the Steam/host identity the transport proves). Raw input to the account resolver | `AuthenticatedSenderIdentity.cs` |
| **AccountId** | Server-owned authority/grouping/audit subject. Resolved *from* a platform subject; never the platform string itself once a resolver exists | data-model; research |
| **Account resolver** **[NEW]** | The server-owned function `platform subject → AccountId`. Today implicit (identity == platform subject); candidate E makes it an explicit, persisted map | research candidate E |
| **CharacterId** | Server-bound gameplay-progression subject within one account; durable `player:<s_playerID>` | data-model; PR #317 |
| **Account record** **[NEW]** | The (proposed) persisted aggregate keyed by `AccountId`: its bound platform credentials, its characters, moderation state, and any linked community identities | this doc |
| **Character record** | The `CharacterProgressionAggregate` (already modeled) plus its selection/ownership/lifecycle facts | data-model §Aggregate 3 |
| **Credential** **[NEW]** | A proof-of-identity a connection presents (a platform subject; later possibly a second-factor or a recovery secret). Distinct from the account it authenticates *to* | this doc |
| **Community identity** **[NEW]** | An *associated*, non-authoritative external identity (Discord user). Attached to an account; never a gameplay-authentication root | card constraint |
| **Link** **[NEW]** | A durable, opt-in, revocable association `AccountId ↔ community identity`, with provenance | this doc |
| **Link ceremony** **[NEW]** | The bounded, replay-resistant handshake that proves *both* sides consented to a Link (e.g. in-game code entered in Discord) | this doc |
| **Server mode** **[NEW]** | A server-config axis deciding whether authoritative server characters are mandatory, optional, or off (vanilla-only) | design question |
| **Portability scope** **[NEW]** | The classification of each piece of state as world-scoped, server-product-scoped, or portable across worlds/servers | data-model §modeling rule 2; §Aggregate 3 envelope |

---

## Layer 1 — Server-side characters

### Decision cluster 1A — Mandatory server characters, or a server mode?

**The axis.** A Homestead-progression server *requires* a server-owned character
(that's where AP/BP/relationships live — data-model §Aggregate 3). But Niflheim may
also want to run in configurations where progression is off. So: is the authoritative
server character **mandatory**, or a **configurable server mode**?

- **Mandatory:** every join binds/creates a server character; simpler invariant, one
  code path, no "which mode am I in" branching in the authority layer.
- **Configurable (`server mode`):** `progression = full | off`. `off` = vanilla
  characters, no server-owned aggregate, Homestead pieces inert or absent.

**[ARCHITECT LEAN]** Make it a **server-mode config with `full` as the shipped
default for Niflheim**, but keep the *authority layer* mono-modal: when progression
is on, the server character is mandatory and non-optional; the only thing the mode
switches is whether the progression subsystem is mounted at all. This avoids a
half-authenticated middle state (the thing PR #317 spent five blockers killing)
while still letting a plain-Valheim server exist. **Never** ship a mode where a
server character is "optional but present" — that is exactly the client-claimed-vs-
server-owned ambiguity the substrate forbids.

**[OPEN → Daniel]** Do you want a genuine `progression = off` vanilla mode to exist
at all, or is Niflheim always a progression server (making this a non-question and
saving a config axis + its test matrix)?

### Decision cluster 1B — Character creation, selection, and ownership

Today the character *subject* is derived (`player:<s_playerID>`), but there is no
**creation ceremony**, **multi-character selection**, or **ownership record** — the
character simply *is* whatever `s_playerID` the connecting profile carries.

| Concern | Substrate today | Question this raises |
|---|---|---|
| Creation | Implicit: first placement/relationship under an `s_playerID` mints its aggregate | Should creation be an explicit, receipted "create character" op with a chosen display name, or stay implicit-on-first-use? |
| Selection | One `s_playerID` per connected Valheim profile; no server-side "pick which of my characters" | Multiple characters per account (data-model §Aggregate 2 sibling rules) implies a **selection** step. Where does it live — client profile choice, or a server character-select screen? |
| Ownership | `CharacterId` sits *within* `AccountId`; the account is the owner | Is ownership strictly `character ∈ account`, or can a character ever be reassigned between accounts (recovery/dispute — see 2C)? |

**[ARCHITECT LEAN]** Keep creation **implicit-on-first-use but receipted**: the first
authenticated operation under a new `(AccountId, s_playerID)` pair emits a
`CharacterCreated` receipt (name, account, provenance) so creation is auditable and
recoverable, without a separate blocking UI. Selection rides Valheim's existing
profile picker (each Valheim character profile *is* a candidate `s_playerID`); the
server binds whichever authenticated profile connects, and the account's sibling
rules (§Aggregate 2) govern what that character may *do*, not whether it may exist.
Ownership stays strict: `character ∈ account`, reassignment only via the explicit
recovery path (2C), never silently.

**[OPEN → Daniel]** Is "your Valheim profile picker *is* your character selector"
acceptable, or do you want a **server-side character-select** (which would let one
Valheim profile map to several server characters, decoupling server characters from
local save files entirely)? This is the biggest 1B fork: it decides whether server
characters are 1:1 with local saves or fully server-owned slots.

### Decision cluster 1C — Persistence, backup, rollback, and conflict recovery

The receipt journal already gives us the hard part: **the journal is the single
authority; projections rebuild from it at boot** (data-model §Aggregate 5 boot
rehydration). That means "server character persistence" is *already* journal-backed
for progression state.

| Capability | Substrate today | Gap |
|---|---|---|
| Durable progression | Journal-backed, rehydrated at boot | none for progression facts |
| Backup | Implicit in whatever stores the journal | No stated backup *cadence/retention* policy |
| Rollback | `Validation and recovery`: replay a committed receipt, rebuild a projection, quarantine invalid state; a repair tool **may not invent** a purchase/balance/relationship | Rollback is *per-receipt / per-projection*, not "restore character to yesterday" — no snapshot/time-travel primitive |
| Conflict recovery | CAS revisions + quarantine of interrupted mutations | none for progression; **none defined for account/link records** (they don't exist yet) |

**[ARCHITECT LEAN]** Do **not** invent a character-snapshot/time-travel system.
The journal-as-authority model already gives principled recovery: to "roll back" you
quarantine bad receipts and re-project, you never hand-edit a balance. Backup =
back up the journal (+ any account/link store) as one consistent unit; retention is
an ops policy, not a new aggregate. **[OPEN → Daniel]** confirm you're happy with
"recovery = journal replay + quarantine" as the *only* rollback story, i.e. we
explicitly **do not** promise "restore my character to how it was last Tuesday."

### Decision cluster 1D — Death, retirement, deletion, restoration

| Event | Meaning | Substrate hook | Question |
|---|---|---|---|
| Death | Vanilla character death | Not progression-terminal; relationships/AP/BP/durable outcomes persist (nothing in §Aggregate 3 ties them to being alive) | Confirm death is **cosmetic to progression** (my lean) — you don't lose your Homestead standing by dying |
| Retirement | Voluntary "set this character aside" | none | Is retirement just dormancy (data-model §Dormancy) at the character scope, or a distinct state? |
| Deletion | Destroy a server character | none; **dangerous** — deletes progression + may free sibling slots | Must be an explicit, receipted, account-authorized op with a grace window; never a side-effect |
| Restoration | Undo a deletion | Journal still holds the receipts | Restore = re-project from journal if within retention; hard-delete only after retention expiry |

**[ARCHITECT LEAN]** Model deletion as **soft-delete + retention window**: a
receipted `CharacterRetired`/`CharacterDeleted` op flips the character to a
tombstoned state (frees its sibling reservations per §Aggregate 2 release rules,
preserves the journal), and restoration is a re-project within the retention window.
Hard purge only after retention. Death stays cosmetic to progression. This reuses
the release/dormancy machinery instead of inventing lifecycle state.

---

## Layer 2 — Account management

### Decision cluster 2A — Is the platform ID the account root, or one credential on a server-local account?

**This is the pivotal decision of the whole effort.** Research already frames it:
candidate A (identity == server-derived platform id, **shipped**) vs candidate E (a
server-owned `platform-id → AccountId` map, **standing choice** because R-003 needs
an exclusivity index anyway).

| Model | `AccountId` is… | Pros | Cons |
|---|---|---|---|
| **Platform-rooted** (candidate A as-is) | *the* platform subject | Zero new state; already shipped; nothing to spoof beyond the transport (already hardened) | Account == one platform forever. Steam change / platform migration = new account = lost progression. No path for "same person, new platform" |
| **Server-local account, platform as a credential** (candidate E) | a stable server-minted id; platform subjects are *credentials linked to it* | Survives platform change; supports multiple credentials (future second platform, recovery secret); the natural home for moderation/ban state and Discord links | New persisted aggregate + resolver; a linking/recovery ceremony is now required; more surface to secure |

**[ARCHITECT LEAN — the load-bearing decision]:** adopt **candidate E: a
server-local account is the root; the platform subject is the first credential bound
to it.** Reasoning: (1) research already names E the standing choice because the
exclusivity index needs a stable account key regardless; (2) *every* hard question
in this card — platform change, account recovery, ownership dispute, Discord linking,
moderation/ban durability — has a clean home on a server-local account and **no**
clean home on a raw platform id; (3) it changes nothing about the security floor —
the platform subject is still transport-proven and still the only thing that
*authenticates*; E only adds an indirection `platform → AccountId` that the server
owns. The character subject (`player:<s_playerID>`) is untouched.

The one honest cost: E requires a **first-bind ceremony** (the first time a platform
subject connects, mint an account and bind the credential) and a **recovery
ceremony** (2C). Both are server-owned and receipted; neither accepts a client claim.

**[OPEN → Daniel]** This is the first decision cluster to lock. **Confirm the account
root = a server-local account with the platform subject as its first credential
(candidate E), NOT the raw platform id (candidate A).** Everything downstream —
migration, recovery, Discord, moderation durability — assumes E. If you want to stay
on A for now, say so and I'll re-scope layers 2-3 to the "account == platform"
constraint (and flag what becomes impossible).

### Decision cluster 2B — Multiple characters, siblings, and simultaneous-presence policy

Mostly **already decided** in the substrate; surfaced here so the account layer
doesn't contradict it.

| Rule | Substrate | Account-layer implication |
|---|---|---|
| Multiple characters per account | `CharacterId` ∈ `AccountId`; §Aggregate 2 governs siblings | The account record owns the *set* of its characters (the selection list) |
| Homestead sibling exclusivity | ≤1 sibling actively holds a relationship to one Homestead Stone | The account record is where the exclusivity index is keyed |
| Community Attunement | siblings **may** be simultaneously active | Policy-driven; account layer must not hard-code "one character per account" |
| Community Bond | still account-exclusive | ditto |
| Simultaneous *connections* | undefined by substrate | **[OPEN → Daniel]** may two of my characters be *logged in at once* (two clients, same account)? Distinct from "active at one Stone." |

**[ARCHITECT LEAN]** The account record owns the character set + the exclusivity
index; all "may siblings coexist" questions stay **policy-driven** (never hard-coded),
exactly as §Aggregate 2 already insists. Simultaneous connections is a genuinely open
question the substrate doesn't answer — my lean is to **allow at most one active
connection per account by default** (config-overridable), because concurrent siblings
of the *same account* is a rich exploit surface for the exclusivity index and buys
little.

### Decision cluster 2C — Stable IDs, migration, recovery, and ownership disputes

Only reachable under candidate E (2A); under A these are mostly impossible.

| Need | Mechanism (under E) | Threat it must resist |
|---|---|---|
| Stable account id | Server-minted, opaque, never a platform string; survives credential changes | — |
| Platform change ("new Steam") | Bind the new platform subject as an *additional credential* to the existing `AccountId` via a recovery ceremony | Attacker binds *their* platform to *your* account (account theft) — ceremony must prove control of an existing credential or an out-of-band recovery secret |
| Account recovery (lost platform) | Recovery secret / linked community identity used as a *recovery factor* (not an auth root) | Discord takeover → account takeover (see Layer 3 threat model) |
| Ownership dispute | Admin-mediated, receipted reassignment of a character between accounts | Silent reassignment; admin impersonation (use the PR #317 admin gate: `VanillaAdminIdentity.ListContainsId`) |

**[ARCHITECT LEAN]** Migration/recovery is a **credential-rebind ceremony on a
stable server-minted `AccountId`**, always receipted, always requiring proof of an
*existing* factor (old credential, or a pre-registered recovery secret). Ownership
disputes resolve only through the **admin gate that already ships** (server-admin
only, normalized identity match — PR #317 Blocker 4), never through a client claim.
**[OPEN → Daniel]** What recovery factors do you want to support at v1 — just
"prove control of the old platform," or also a pre-registered recovery secret and/or
a linked Discord as a *recovery hint* (with the takeover risk that implies)?

### Decision cluster 2D — Moderation, bans, admin state

| State | Where it belongs (under E) | Why |
|---|---|---|
| Ban | on the **account record** (+ optionally per-character) | Account-scoped so a banned player can't dodge by switching characters; the account is the durable authority subject (data-model: `AccountId` = "authority/grouping/audit") |
| Admin | the shipped `sbpr_provision` admin gate + `VanillaAdminIdentity` | Already normalized, server-admin-only, spoof-tested (PR #317) |
| Audit | receipts already carry `AccountId`/`CharacterId`/`StoneId` on every durable record | data-model §Aggregate 5 |

**[ARCHITECT LEAN]** Bans and moderation flags live on the **account** (with an
optional per-character mute/restrict), reusing the shipped admin gate for authority
and the receipt store for audit. No new admin identity path — the substrate already
hardened one.

---

## Layer 3 — Discord linking

**Hard constraint (card):** Discord is an **optional associated community identity**
unless Daniel explicitly chooses otherwise. **Never** silently make Discord the sole
gameplay-authentication root. This section is written to honor that as an invariant,
not a preference.

### Decision cluster 3A — What Discord linking is *for* (use cases before mechanism)

| Use case | Needs a link? | Authorization impact |
|---|---|---|
| Support ("who is this player" for a mod) | yes (read-only association) | none — pure lookup |
| Notifications (server events → DM/channel) | yes | none — outbound only |
| Community roles (Discord role ↔ "verified player") | yes | **one-way only** — see 3D |
| Account **recovery hint** | yes (as a *factor*, not a root) | see 2C threat model |
| Gameplay authorization ("Discord role grants in-game power") | — | **[DEFAULT: NO]** — forbidden unless Daniel explicitly opts in |

**[ARCHITECT LEAN]** Scope Discord at v1 to **support + notifications + a verified-
role flag that flows Discord→game only as a cosmetic/community marker, never as game
authority.** Recovery-hint is a *maybe* (2C). Gameplay authorization stays off.

### Decision cluster 3B — The link ceremony (which handshake?)

| Option | How | Operational dependency | Verdict |
|---|---|---|---|
| **In-game code → Discord** | Game shows a short-lived code; player types it to the bot in Discord; bot confirms to server | A bot + a server↔bot channel; no OAuth app | **[ARCHITECT LEAN]** simplest, no web infra, code is server-minted + short-TTL + single-use |
| **Discord code → in-game** | Reverse: bot mints code, player enters it in-game | bot + server↔bot channel | Symmetric; fine alternative |
| **Discord OAuth (web portal)** | Standard OAuth2 authorize → callback | a hosted web endpoint + OAuth app + secret management | Heavier; real infra to run and secure |
| **OAuth device-code** | Device-code grant, no web callback | OAuth app; still needs bot to be useful | Middle weight |
| **Bot DM only** | Player DMs bot a game-issued code | bot | Same as in-game→Discord, DM transport |

**[ARCHITECT LEAN]** Ship the **in-game-code → Discord-bot** ceremony: the server
mints a short-TTL, single-use code bound to `(AccountId, nonce)`; the player proves
Discord control by presenting it to the bot; the bot relays `(code, discordUserId)`
to the server over a trusted server↔bot channel; the server binds the Link and
receipts it. No OAuth app, no hosted web portal, no callback URL to secure. The code
does the consent-proof on both sides. **[OPEN → Daniel]** OK to defer OAuth/web-
portal entirely and ship the in-game-code + bot ceremony, or do you specifically want
OAuth (e.g. for a future web dashboard)?

### Decision cluster 3C — Cardinality, unlink, relink, no-Discord users

| Question | **[ARCHITECT LEAN]** | Rationale |
|---|---|---|
| One-to-one or many? | **One account ↔ at most one Discord identity** at v1; one Discord ↔ at most one account | Keeps recovery/roles unambiguous; multi-link is a later relaxation, not a v1 need |
| Unlink | Always allowed by the account owner; receipted; immediately revokes role sync + recovery-hint | Opt-in must be opt-out-able (privacy) |
| Relink | Unlink then link again via the same ceremony; new provenance record | No special path needed |
| No-Discord users | **First-class** — everything gameplay works with zero Discord | Constraint: Discord is never required for auth or play |

### Decision cluster 3D — The boundary between Discord roles and game authorization

**The one rule that keeps this safe:** role information flows **one way, and only as
data, never as authority.**

- **Discord → game:** a linked+role-holding player may get a **cosmetic/community
  marker** ("verified", a title). It **never** grants a gameplay capability,
  permission, AP/BP, or Stone authority. Game authorization is *always* resolved
  from the account/character substrate, per operation (mirrors the Homestead
  "permission never composes by union / resolve per-operation" rule).
- **Game → Discord:** the server *may* tell the bot "assign role X to Discord user Y
  because their linked account is verified/subscribed." This is the **only**
  direction that touches roles, and it's an outbound notification, not an inbound
  grant.

**[ARCHITECT LEAN]** Lock this as an invariant for the eventual spec: **Discord role
state is never an input to any in-game authorization decision.** If Daniel later
wants "Discord subscribers get an in-game perk," that perk is granted on the
*account* (server-authoritative, receipted) and Discord is merely the *signal* that
triggered an admin/automated account grant — the authority still lives on the account.

**[OPEN → Daniel]** Confirm the hard boundary: Discord roles are **read/notify only**,
never game authority. (Strong lean; this is the card's central safety constraint.)

---

## The architecture shapes (pick the trust-boundary posture)

Three coherent whole-system shapes, honestly compared. The *decision* is Daniel's.

### Shape 1 — Thin: platform-rooted, no Discord authority, no account aggregate

Stay on candidate A (`AccountId == platform subject`). Discord is a **pure side-car**:
a bot maps `discordUserId → platform subject` in its *own* store, used only for
support lookups and notifications. No server-local account record, no recovery, no
migration.

- **State added:** essentially none server-side; the bot owns a lookup table.
- **Trust boundary:** the game never trusts Discord; the account is the platform.
- **Pros:** minimal; ships fastest; nothing new to secure in the game.
- **Cons:** no platform-change survival, no recovery, no ownership dispute path, no
  ban-durability across characters beyond the platform. Discord link is fragile
  (keyed on a platform id that can vanish).

### Shape 2 — Server-local account, Discord as linked community identity (**[ARCHITECT LEAN]**)

Candidate E. A server-minted `AccountId` owns credentials, characters, moderation
state, and an optional one-to-one Discord Link established by the in-game-code
ceremony. Discord is read/notify only (3D). Recovery via credential-rebind (2C).

- **State added:** an **AccountRecord aggregate** (credentials, character set,
  moderation, link) + the account resolver + a Link record with provenance. All
  journal-backed and receipted, reusing the existing store.
- **Trust boundary:** platform subject *authenticates*; account *authorizes*; Discord
  *associates* — three clean layers, no crossing.
- **Pros:** every hard question (platform change, recovery, dispute, ban durability,
  Discord) has a clean home; changes nothing about the shipped security floor; matches
  the substrate's "many small aggregates, sharp ownership" style.
- **Cons:** real new surface (the account aggregate + two ceremonies: first-bind and
  link); must keep Discord strictly non-authoritative by discipline (3D invariant).

### Shape 3 — Federated identity (Discord/OAuth as a first-class credential)

Candidate E **plus** Discord (via OAuth) as a *bindable credential*, not just an
association — so "log in with Discord" could authenticate a session.

- **State added:** Shape 2 + OAuth app + web/device-code flow + Discord-as-credential
  in the resolver.
- **Trust boundary:** **weaker** — Discord compromise now reaches authentication, not
  just association. Directly in tension with the card's "never make Discord the sole
  auth root."
- **Pros:** frictionless web login; strongest "one identity everywhere" UX.
- **Cons:** violates the card's default constraint unless Daniel explicitly opts in;
  largest attack surface; Discord outage/ban becomes a *login* outage. **Not
  recommended for v1.**

**[ARCHITECT LEAN] Recommendation:** **Shape 2.** It's the smallest architecture that
answers *all* the card's required questions, keeps the shipped security properties
exactly, and treats Discord as the card demands (associated, optional, non-authoritative).
Shape 1 is a valid "not yet" if Daniel wants to defer accounts entirely; Shape 3 is
explicitly out unless Daniel opts into Discord-as-auth with eyes open.

---

## Lifecycle tables

### Account lifecycle (Shape 2)

| Event | Trigger | Server action | Receipt |
|---|---|---|---|
| First bind | New platform subject connects | Mint `AccountId`; bind platform subject as credential #1 | `AccountCreated` + `CredentialBound` |
| Add credential | Recovery/migration ceremony (proof of existing factor) | Bind new platform subject to existing `AccountId` | `CredentialBound` |
| Ban | Admin op (shipped admin gate) | Flag account (+ optional per-character); reject future auth/ops | `AccountBanned` |
| Recover | Lost-platform ceremony (recovery factor) | Rebind credential to existing account | `AccountRecovered` |
| Dispute resolve | Admin-mediated | Reassign character between accounts | `CharacterReassigned` |

### Character lifecycle

| Event | Trigger | Server action | Receipt |
|---|---|---|---|
| Create | First op under new `(AccountId, s_playerID)` | Mint `CharacterProgressionAggregate` | `CharacterCreated` |
| Select | Authenticated profile connects | Bind active character; enforce §Aggregate 2 policy | (no mutation; index touch receipted on relationship acts) |
| Death | Vanilla death | none to progression | — (cosmetic) |
| Retire/Dormant | Voluntary | Dormancy state (data-model §Dormancy); free sibling reservations | `CharacterRetired` |
| Delete | Explicit account-authorized op | Soft-delete + tombstone; retention window | `CharacterDeleted` |
| Restore | Within retention | Re-project from journal | `CharacterRestored` |
| Purge | After retention | Hard-remove | `CharacterPurged` |

### Link lifecycle (Discord, Shape 2)

| Event | Trigger | Server action | Receipt |
|---|---|---|---|
| Link | In-game-code ceremony completes | Bind `AccountId ↔ discordUserId`, one-to-one; store provenance | `DiscordLinked` |
| Role sync (out) | Account state change (verified/sub) | Notify bot to set/clear Discord role | `DiscordRoleSyncRequested` |
| Unlink | Owner request | Revoke Link; clear role sync + recovery-hint | `DiscordUnlinked` |
| Compromise | Reported stolen Discord | Admin/owner force-unlink; Link cannot re-auth anything | `DiscordUnlinked` (reason=compromise) |
| Relink | New ceremony | Fresh Link + provenance | `DiscordLinked` |

---

## Migration and recovery model

- **No production content migration is promised** — consistent with data-model
  rules 6 & §Aggregate 4 ("production migration deferred; incompatible unreleased
  test data may be reset"). Account/character *records* are new, so there is no
  legacy account data to migrate at v1; the migration story is **forward-only**.
- **Recovery = journal replay + quarantine** (data-model §Validation and recovery).
  There is no snapshot/time-travel. "Restore my character" means re-project from the
  journal within retention, never hand-edit a balance.
- **Account recovery = credential rebind** on a stable server-minted `AccountId`,
  proving an existing factor; never a client claim; always receipted.
- **Backup unit = the journal + account/link store as one consistent set**; retention
  is an ops policy, not an aggregate.

---

## Threat / failure sweep

| # | Threat | Vector | Mitigation (grounded) |
|---|---|---|---|
| T1 | Client-claimed identity | payload asserts an account/character | Rejected at the floor: identity is transport-bound; payload is never authority (PR #317; research L164-166) |
| T2 | Forged sender / admin | spoof `m_senderPeerID` / admin id | Direct per-peer ZRpc + normalized admin match; spoof-tested (PR #317 Blockers 2,4) |
| T3 | Stolen Steam/platform | attacker connects as your platform subject | Platform compromise = session compromise (unavoidable at that layer); **account** ban/recovery limits blast radius under Shape 2; under Shape 1 there is no recourse |
| T4 | Stolen Discord | attacker controls your Discord | Discord is never auth (3D invariant) → **no gameplay access**. Only risk is recovery-hint abuse → gate recovery on a *second* factor, not Discord alone (2C) |
| T5 | Replayed link code | reuse/guess the ceremony code | Server-minted, single-use, short-TTL, bound to `(AccountId, nonce)`; consumed on first use (mirrors `OperationId` idempotency, §Aggregate 5) |
| T6 | Link hijacking | bind attacker's Discord to your account (or vice-versa) | Ceremony proves control of *both* sides (in-game code proves account; presenting it in Discord proves Discord); one-to-one cardinality (3C) |
| T7 | Bot outage | bot down during link/role sync | Gameplay + auth unaffected (Discord non-authoritative); links/role-syncs queue or fail closed; **no** gameplay dependency on bot uptime |
| T8 | Provider loss (Steam/Discord shutdown) | credential provider disappears | Under Shape 2 the account survives on its other credentials/recovery factor; under Shape 1 the account is lost with the platform |
| T9 | Duplicate accounts | one person, many accounts (ban evasion) | Not fully preventable; ban on account + optional platform-subject dedupe heuristics; **do not** over-engineer — flag for ops, don't build biometric-grade dedupe |
| T10 | Rollback abuse | replay to duplicate progression | Journal idempotency + CAS revisions reject conflicting replays (Gate-A proven) |
| T11 | Silent Discord→authority leak | a role quietly gates a game action | Forbidden by the 3D invariant; enforce in review — Discord role is never an authorization input |

None of these need machinery beyond the substrate's shipped per-operation
revalidation + receipt/quarantine, **provided** two rules hold: (a) the account root
is a server-minted subject (Shape 2), and (b) Discord role state is never an
authorization input (3D).

---

## Minimum vertical slice (when Daniel advances)

The smallest end-to-end thing that proves the architecture without over-building:

1. **AccountRecord aggregate + account resolver** (Shape 2): mint-on-first-bind,
   platform subject as credential #1, receipted. Reuses the existing store.
2. **Character create/select** stays implicit-on-first-use + receipted (1B lean); no
   new UI.
3. **One Discord Link ceremony** (in-game-code → bot, 3B lean) producing a `DiscordLinked`
   receipt, plus **one** outbound role sync — read/notify only (3D), no inbound authority.
4. **Ban on account** using the shipped admin gate.

This slice exercises: transport-bound auth → server-local account → character binding
→ optional Discord association → moderation, with every mutation receipted. It proves
the trust boundaries end-to-end.

### Explicit NOT-yet scope

- No OAuth app / web portal / device-code flow (defer; in-game-code ceremony suffices).
- No Discord-as-credential / "log in with Discord" (Shape 3 — out unless Daniel opts in).
- No multi-Discord or multi-account-per-Discord (one-to-one at v1).
- No snapshot/time-travel rollback (journal replay only).
- No automated bot role *hierarchy* management beyond a single verified/role flag.
- No production data migration (forward-only).
- No cross-server character portability beyond what §Aggregate 3's world/product
  scoping already classifies — **[OPEN → Daniel/RE]** portability scope per field is
  its own decision (see below).

---

## Questions reserved for Daniel (consolidated, one cluster at a time)

Presented in the sections above; collected here for the reply. Ordered by how much
each unblocks.

- **2A — the pivotal one.** Account root = **server-local account with the platform
  subject as its first credential (candidate E / Shape 2)**, or stay platform-rooted
  (candidate A / Shape 1)? Everything downstream assumes E. *Lock this first.*
- **3D — the central safety constraint.** Confirm Discord roles are **read/notify
  only, never an input to in-game authorization.** (Strong lean.)
- **1A.** Does a `progression = off` vanilla server mode need to exist, or is Niflheim
  always a progression server?
- **1B.** "Valheim profile picker *is* your character selector" (server characters 1:1
  with local saves), or a **server-side character-select** (server characters fully
  decoupled from local saves)?
- **2B.** May two characters on the **same account** be *logged in at once* (distinct
  from "active at one Stone")? Lean: at most one active connection per account.
- **2C.** Which recovery factors at v1 — "prove control of old platform" only, or also
  a pre-registered recovery secret and/or Discord-as-recovery-hint (with T4 risk)?
- **3B.** Ship the **in-game-code + bot** link ceremony and defer OAuth/web-portal
  entirely? (Lean: yes.)
- **1C.** Accept "recovery = journal replay + quarantine" as the *only* rollback story
  (no "restore to last Tuesday" promise)?

## Items routed to RE / other subsystems

- **[OPEN → RE]** **Portability scope per field** — exactly which `CharacterProgressionAggregate`
  fields are world-scoped vs server-product-scoped vs portable (data-model §Aggregate 3
  envelope says "world/product scope" but the per-field classification isn't enumerated).
  This needs an RE pass against the shipped aggregate before any cross-server portability
  is designed.
- **[OPEN → RE]** The **exact production account provider** remains the unresolved P0
  choice research L89-91 already flags; Shape 2 makes the *account* server-local, but
  the *credential* provider (Steam platform proof vs another) still needs the P0 spike
  before a production mutation endpoint ships.
