using System;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Crafting
{
    // T022 (Tracer 6, Crafting node 2 of 3) — the trusted, engine-free Masterwork Workmanship-issuance
    // provider (spec §Acceptance scenario 2 "Crafting" line 155: "Masterwork issues one deterministic
    // visible validated Workmanship Property on an eligible non-stackable durable item while active";
    // contracts.md §Crafting "WorkmanshipIssuanceProvider: active Masterwork may issue one deterministic
    // property on an eligible exact non-stackable durable output"; data-model.md "Crafting | 1 | Masterwork
    // | Character Effect | personal Offered").
    //
    // Architecture decision A1 (plan.md): a derived provider translates persisted character/Stone state
    // into a typed capability. It writes no ledger; it is a PURE projection layered on the shipped T004
    // DerivedActivationView. Masterwork is a PERSONAL Character Effect (NodeOutcomeType.CharacterEffect,
    // personal Offered), the SAME node shape as the sibling T026 Field Fletching I — so its active/dormant
    // status derives identically: the caller must hold a PURCHASE record for the node at this Stone AND an
    // ACTIVE relationship to this Stone (no Settlement Local policy / build Permission conjunct — those gate
    // Local placement effects, not a personal crafting effect). Change the relationship and re-derive: the
    // same persisted purchase flips active<->dormant with zero writes (AT-NO-ACTIVE-LEDGER, from T004).
    //
    // The provider decides ONLY whether, and with WHAT stamp, an issuance may happen for one produced item:
    //
    //   * Masterwork must be currently ACTIVE for the caller (purchase + active relationship). Dormant/
    //     unpurchased => no issuance.
    //   * The produced item must be an ELIGIBLE non-stackable durable output (WorkmanshipCodec.IsEligible):
    //     a stackable or non-durable output (arrows, food, materials) never receives a Workmanship stamp.
    //   * The item must NOT ALREADY carry a valid Workmanship stamp — issuance is one-per-instance and does
    //     not overwrite existing provenance (idempotent for an already-stamped output; a re-issue attempt on
    //     a valid stamp is a no-op decision).
    //
    // When all hold, the provider returns the exact WorkmanshipStamp to write (deterministic: one named
    // property, no RNG, bound to the server-minted provenance id + crafter + item type). The net48 seam
    // performs the actual custom-data write + integrity token via WorkmanshipCodec.Stamp and then EXPLICITLY
    // dirties persistence; this pure provider authors the decision and the stamp content, nothing else.
    //
    // Client claims are NEVER the source of truth: the caller supplies authenticated aggregates (character
    // purchases + the (account, Stone) authority index) composed by trusted server code; nothing here trusts
    // a payload, and the produced item's eligibility facts are server-observed, never a client eligibility
    // claim.
    //
    // net48 audit: only System + engine-free Domain value objects / catalog / activation view / codec. No
    // net5+ surface, no UnityEngine/Valheim/BepInEx reference, so this link-compiles into the net8 test
    // project exactly like the sibling adapters.

    /// <summary>The reason an issuance decision resolved the way it did — a stable machine outcome so the
    /// net48 seam and tests can diagnose precisely which gate decided, never a bare bool.</summary>
    public enum WorkmanshipIssuanceOutcome
    {
        /// <summary>Issue the returned stamp: Masterwork active, eligible output, not already stamped.</summary>
        Issue = 0,
        /// <summary>Masterwork is not active for this caller (dormant/unpurchased) — no issuance.</summary>
        EffectNotActive,
        /// <summary>The produced item is not an eligible non-stackable durable output — no issuance.</summary>
        IneligibleItem,
        /// <summary>The item already carries a valid Workmanship stamp — issuance is a no-op (idempotent).</summary>
        AlreadyStamped
    }

    /// <summary>The pure decision for one produced item: whether to issue, why, and — when issuing — the
    /// exact deterministic stamp to write. Carries no mutable authority.</summary>
    public readonly struct WorkmanshipIssuanceDecision
    {
        private WorkmanshipIssuanceDecision(WorkmanshipIssuanceOutcome outcome, bool shouldIssue, WorkmanshipStamp stamp)
        {
            Outcome = outcome;
            ShouldIssue = shouldIssue;
            Stamp = stamp;
        }

        public WorkmanshipIssuanceOutcome Outcome { get; }

        /// <summary>True only when <see cref="Outcome"/> is <see cref="WorkmanshipIssuanceOutcome.Issue"/> —
        /// the net48 seam should write <see cref="Stamp"/> and dirty persistence.</summary>
        public bool ShouldIssue { get; }

        /// <summary>The exact stamp to persist when <see cref="ShouldIssue"/> is true; default otherwise.</summary>
        public WorkmanshipStamp Stamp { get; }

        internal static WorkmanshipIssuanceDecision Issue(WorkmanshipStamp stamp) =>
            new WorkmanshipIssuanceDecision(WorkmanshipIssuanceOutcome.Issue, true, stamp);
        internal static WorkmanshipIssuanceDecision Refused(WorkmanshipIssuanceOutcome outcome) =>
            new WorkmanshipIssuanceDecision(outcome, false, default);
    }

    /// <summary>The facts about ONE produced item the provider needs to decide issuance. All server-observed
    /// at production time: the item's exact type (prefab id), whether it is non-stackable and durable
    /// (eligibility), whether it already carries a valid Workmanship stamp, and a server-minted exact-instance
    /// provenance id to bind. Never a client eligibility claim.</summary>
    public readonly struct ProducedItemFacts
    {
        public ProducedItemFacts(string itemType, bool nonStackable, bool durable,
            bool alreadyHasValidWorkmanship, ItemProvenanceId provenanceId)
        {
            ItemType = itemType ?? string.Empty;
            NonStackable = nonStackable;
            Durable = durable;
            AlreadyHasValidWorkmanship = alreadyHasValidWorkmanship;
            ProvenanceId = provenanceId;
        }

        public string ItemType { get; }
        public bool NonStackable { get; }
        public bool Durable { get; }
        public bool AlreadyHasValidWorkmanship { get; }
        public ItemProvenanceId ProvenanceId { get; }
    }

    public sealed class WorkmanshipIssuanceProvider
    {
        /// <summary>The stable Masterwork personal Character-Effect node identity in the current build
        /// (HomesteadProgressionCatalog: Crafting / Masterwork v1).</summary>
        public static readonly VersionedId MasterworkNode = new VersionedId("Masterwork", 1);

        /// <summary>The single deterministic Workmanship property an active Masterwork issues. One named
        /// seal — no RNG, no tier catalog (the spec defers a "final Workmanship catalog" to future work).
        /// Fixed here so the same issuance always produces the same visible property (deterministic).</summary>
        public static readonly WorkmanshipProperty MasterworkProperty =
            new WorkmanshipProperty("Workmanship", "Masterwork");

        private readonly HomesteadProgressionCatalog _catalog;

        public WorkmanshipIssuanceProvider(HomesteadProgressionCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>Resolve whether Masterwork is currently active for one caller from current Stone state +
        /// the caller's character aggregate + the (account, Stone) authority index. Reuses the shipped T004
        /// DerivedActivationView so active/dormant is derived identically to every other personal Character
        /// Effect (purchase record AND active relationship, no second ledger).</summary>
        public bool IsMasterworkActive(
            StoneProgressionAggregate stone,
            CharacterProgressionAggregate character,
            AccountStoneAuthorityIndex authority)
        {
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (authority == null) throw new ArgumentNullException(nameof(authority));

            var view = DerivedActivationView.Derive(stone, character, authority);
            foreach (var row in view.Nodes)
                if (row.Node.Key == MasterworkNode.Key)
                    return row.Active;
            return false;
        }

        /// <summary>Decide issuance for one produced item using the composed aggregates. This is the single
        /// authority the net48 host seam calls; it resolves the caller's Masterwork activation and then
        /// applies the item-eligibility/idempotency policy through the boolean overload below.</summary>
        public WorkmanshipIssuanceDecision Decide(
            StoneProgressionAggregate stone,
            CharacterProgressionAggregate character,
            AccountStoneAuthorityIndex authority,
            in ProducedItemFacts item)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            bool active = IsMasterworkActive(stone, character, authority);
            return Decide(active, character.Account.Value, item);
        }

        /// <summary>Decide issuance from the ALREADY-DERIVED Masterwork activation bit plus the crafter
        /// account and the server-observed produced-item facts. This is the single policy both entry points
        /// reach so the decision can never diverge between the server-view path and any pre-derived path.
        /// The +stamp content is deterministic: one named property bound to the server-minted provenance id,
        /// the crafter account, and the exact item type.</summary>
        public WorkmanshipIssuanceDecision Decide(bool masterworkActive, string crafterAccount, in ProducedItemFacts item)
        {
            if (!masterworkActive)
                return WorkmanshipIssuanceDecision.Refused(WorkmanshipIssuanceOutcome.EffectNotActive);

            if (!WorkmanshipCodec.IsEligible(item.NonStackable, item.Durable))
                return WorkmanshipIssuanceDecision.Refused(WorkmanshipIssuanceOutcome.IneligibleItem);

            // One provenance per exact instance: never overwrite an already-valid Workmanship stamp.
            if (item.AlreadyHasValidWorkmanship)
                return WorkmanshipIssuanceDecision.Refused(WorkmanshipIssuanceOutcome.AlreadyStamped);

            var stamp = new WorkmanshipStamp(
                WorkmanshipCodec.SchemaVersion,
                MasterworkNode,
                item.ProvenanceId,
                crafterAccount ?? string.Empty,
                item.ItemType,
                MasterworkProperty);
            return WorkmanshipIssuanceDecision.Issue(stamp);
        }
    }
}
