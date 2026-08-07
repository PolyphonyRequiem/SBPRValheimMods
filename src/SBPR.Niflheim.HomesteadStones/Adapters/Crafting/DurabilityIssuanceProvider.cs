using System;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Crafting
{
    // T023 (Tracer 6, Crafting node 3 of 3) — the trusted, engine-free Built to Last maximum-durability
    // issuance provider (spec §Acceptance scenario 2 "Crafting": "Built to Last permanently improves maximum
    // durability on future eligible outputs with exact-item provenance"; contracts.md §Crafting:
    // "DurabilityIssuanceProvider: acquired Built to Last supplies the configured maximum-durability property
    // on future eligible outputs after relationship loss as well"; data-model.md fixed roster: "Crafting | 1 |
    // Built to Last | Permanent Effect | personal Offered | executable"). Acceptance: AT-BUILT-TO-LAST.
    //
    // Architecture decision A1 (plan.md): a derived provider translates persisted character state into a typed
    // capability. It writes no ledger; it is a PURE projection (AT-NO-ACTIVE-LEDGER).
    //
    // THE LOAD-BEARING DISTINCTION FROM THE SIBLING T022 MASTERWORK PROVIDER. Masterwork is a personal
    // CHARACTER Effect: its issuance routes through the relationship-gated T004 DerivedActivationView, so it
    // goes dormant the instant the crafter's relationship drops. Built to Last is a personal PERMANENT Effect,
    // and per data-model.md §CharacterProgression "Permanent Effects and Progression Keys survive relationship
    // loss and Tree revocation." So — exactly like the accepted T018 Iron Stomach provider — this provider keys
    // on the character's DURABLE purchase record (outcome class PermanentEffect, exact BuiltToLast node
    // identity) ALONE:
    //   * no active-relationship conjunct — releasing the relationship never removes the purchase, so FUTURE
    //     outputs keep receiving the property (contracts.md: "on future eligible outputs after relationship
    //     loss as well");
    //   * no Settlement Local policy / build Permission conjunct — those gate Local placement effects;
    //   * no Stone node-development conjunct — Tree revocation removes development yet the Permanent Effect
    //     survives, so the provider takes no Stone aggregate or authority index at all;
    //   * restart-durable — the purchase round-trips through the serialized character aggregate, so the
    //     provider re-derives the identical decision with zero writes.
    //
    // FUTURE OUTPUTS ONLY — THE NO-RETROACTIVE-MUTATION INVARIANT (the trap this card exists to avoid). The
    // provider decides issuance for ONE item at its moment of production, and the effective maximum durability
    // of any item is derived ONLY from the signed stamp that instance actually carries (ResolveMaxDurability
    // below). Consequences, all of which the tests pin:
    //   * an item crafted BEFORE the effect was acquired carries no stamp, so it reads Absent and keeps the
    //     vanilla maximum forever — acquiring Built to Last never reaches back and rewrites it;
    //   * the configured factor is FROZEN into the signed stamp at issuance, so a later retune of the
    //     configured factor cannot change an already-crafted item either (its token is signed over the old
    //     factor; the new factor is simply not part of that instance's fact);
    //   * nothing in this provider mutates any existing item — it returns a decision, never a write.
    //
    // IDEMPOTENCY. Issuance is one-per-instance: an item that already carries a VALID durability stamp gets
    // AlreadyStamped and the seam performs no write, so a replayed/duplicated production event cannot
    // double-issue or re-mint a second provenance identity onto the same instance.
    //
    // Client claims are NEVER the source of truth: the caller supplies the authenticated character aggregate
    // composed by trusted server code, and the produced item's eligibility facts are server-observed.
    //
    // net48 audit: only System + engine-free Domain value objects / codec. No net5+ surface, no
    // UnityEngine/Valheim/BepInEx, so this link-compiles into the net8 test project exactly like the sibling
    // adapters (WorkmanshipIssuanceProvider, EffectiveStationLevelProvider, FoodRefreshThresholdProvider).

    /// <summary>The reason a Built to Last issuance decision resolved the way it did — a stable machine outcome
    /// so the net48 seam and tests can diagnose precisely which gate decided, never a bare bool.</summary>
    public enum DurabilityIssuanceOutcome
    {
        /// <summary>Issue the returned stamp: Built to Last acquired, eligible output, not already stamped.</summary>
        Issue = 0,
        /// <summary>The crafter does not durably hold Built to Last — no issuance.</summary>
        EffectNotAcquired,
        /// <summary>The produced item is not an eligible non-stackable durable output — no issuance.</summary>
        IneligibleItem,
        /// <summary>The item already carries a valid durability stamp — issuance is a no-op (idempotent).</summary>
        AlreadyStamped
    }

    /// <summary>The pure decision for one produced item: whether to issue, why, and — when issuing — the exact
    /// deterministic stamp to write. Carries no mutable authority.</summary>
    public readonly struct DurabilityIssuanceDecision
    {
        private DurabilityIssuanceDecision(DurabilityIssuanceOutcome outcome, bool shouldIssue, DurabilityStamp stamp)
        {
            Outcome = outcome;
            ShouldIssue = shouldIssue;
            Stamp = stamp;
        }

        public DurabilityIssuanceOutcome Outcome { get; }

        /// <summary>True only when <see cref="Outcome"/> is <see cref="DurabilityIssuanceOutcome.Issue"/> — the
        /// net48 seam should write <see cref="Stamp"/> and dirty persistence.</summary>
        public bool ShouldIssue { get; }

        /// <summary>The exact stamp to persist when <see cref="ShouldIssue"/> is true; default otherwise.</summary>
        public DurabilityStamp Stamp { get; }

        internal static DurabilityIssuanceDecision Issue(DurabilityStamp stamp) =>
            new DurabilityIssuanceDecision(DurabilityIssuanceOutcome.Issue, true, stamp);
        internal static DurabilityIssuanceDecision Refused(DurabilityIssuanceOutcome outcome) =>
            new DurabilityIssuanceDecision(outcome, false, default);
    }

    /// <summary>The facts about ONE produced item the provider needs to decide durability issuance. All
    /// server-observed at production time: the item's exact type (prefab id), whether it is non-stackable and
    /// durable (eligibility), whether it already carries a valid durability stamp, and a server-minted
    /// exact-instance provenance id to bind. Never a client eligibility claim.</summary>
    public readonly struct DurableItemFacts
    {
        public DurableItemFacts(string itemType, bool nonStackable, bool durable,
            bool alreadyHasValidDurabilityStamp, ItemProvenanceId provenanceId)
        {
            ItemType = itemType ?? string.Empty;
            NonStackable = nonStackable;
            Durable = durable;
            AlreadyHasValidDurabilityStamp = alreadyHasValidDurabilityStamp;
            ProvenanceId = provenanceId;
        }

        public string ItemType { get; }
        public bool NonStackable { get; }
        public bool Durable { get; }
        public bool AlreadyHasValidDurabilityStamp { get; }
        public ItemProvenanceId ProvenanceId { get; }
    }

    public sealed class DurabilityIssuanceProvider
    {
        /// <summary>The stable Built to Last personal Permanent-Effect node identity in the current build
        /// (HomesteadProgressionCatalog: Crafting / BuiltToLast v1).</summary>
        public static readonly VersionedId BuiltToLastNode = new VersionedId("BuiltToLast", 1);

        /// <summary>The outcome class stamped on a Permanent-Effect purchase (NodePurchases.OutcomeClassOf →
        /// NodeOutcomeType.PermanentEffect). Built to Last is durable ONLY as a Permanent Effect; a
        /// Character-Effect purchase of a same-keyed node would not be a durable Built to Last grant.</summary>
        public const string PermanentEffectOutcomeClass = "PermanentEffect";

        /// <summary>The configured maximum-durability factor an acquired Built to Last issues onto an eligible
        /// future output: 1.25 (a 25% higher maximum durability than vanilla for that exact instance). This is
        /// the ONE tuning knob and it is PROVISIONAL — research.md §"Fixed proof values versus tuning surfaces"
        /// puts "most effect factors" on the configurable side, exactly like the Savor 50% and Iron Stomach 75%
        /// precedents. It is frozen into the signed stamp at issuance so retuning it can never alter an
        /// already-crafted item.</summary>
        public const double ConfiguredMaxDurabilityFactor = 1.25;

        private readonly double _factor;

        public DurabilityIssuanceProvider() : this(ConfiguredMaxDurabilityFactor) { }

        /// <summary>Construct with an explicit configured factor (the tuning seam). The factor must improve
        /// durability — a factor below the vanilla neutral 1.0 would REDUCE maximum durability, which Built to
        /// Last must never do, so it is rejected at construction rather than silently clamped.</summary>
        public DurabilityIssuanceProvider(double configuredFactor)
        {
            if (!(configuredFactor >= DurabilityProperty.NeutralFactor))
                throw new ArgumentOutOfRangeException(nameof(configuredFactor),
                    "Built to Last must not reduce maximum durability; the configured factor must be >= 1.0.");
            _factor = configuredFactor;
        }

        /// <summary>The configured maximum-durability factor this provider issues.</summary>
        public double Factor => _factor;

        /// <summary>Whether this character DURABLY holds Built to Last: a purchase record for the exact
        /// BuiltToLast node identity whose outcome class is the durable Permanent Effect class, at ANY Stone.
        /// No relationship / policy / permission / Stone-development conjunct — a Permanent Effect survives
        /// relationship loss, Tree revocation, and restart.</summary>
        public bool IsBuiltToLastAcquired(CharacterProgressionAggregate character)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));

            foreach (var sr in character.StoneRecords)
            {
                foreach (var p in sr.Purchases)
                {
                    if (p.Node.Key == BuiltToLastNode.Key &&
                        p.Node.Version == BuiltToLastNode.Version &&
                        string.Equals(p.OutcomeClass, PermanentEffectOutcomeClass, StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }

        /// <summary>Decide issuance for one produced item from the caller's durable character aggregate. This is
        /// the single authority the net48 host seam calls.</summary>
        public DurabilityIssuanceDecision Decide(CharacterProgressionAggregate character, in DurableItemFacts item)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            return Decide(IsBuiltToLastAcquired(character), character.Account.Value, item);
        }

        /// <summary>Decide issuance from the ALREADY-DERIVED acquisition bit plus the crafter account and the
        /// server-observed produced-item facts. This is the single policy both entry points reach, so the
        /// decision can never diverge between the server aggregate path and a pre-derived (e.g. replicated
        /// read-model) path. The stamp content is deterministic: the configured factor bound to the
        /// server-minted provenance id, the crafter account, and the exact item type.</summary>
        public DurabilityIssuanceDecision Decide(bool builtToLastAcquired, string crafterAccount, in DurableItemFacts item)
        {
            if (!builtToLastAcquired)
                return DurabilityIssuanceDecision.Refused(DurabilityIssuanceOutcome.EffectNotAcquired);

            if (!DurabilityCodec.IsEligible(item.NonStackable, item.Durable))
                return DurabilityIssuanceDecision.Refused(DurabilityIssuanceOutcome.IneligibleItem);

            // One provenance per exact instance: never overwrite an already-valid durability stamp.
            if (item.AlreadyHasValidDurabilityStamp)
                return DurabilityIssuanceDecision.Refused(DurabilityIssuanceOutcome.AlreadyStamped);

            var stamp = new DurabilityStamp(
                DurabilityCodec.SchemaVersion,
                BuiltToLastNode,
                item.ProvenanceId,
                crafterAccount ?? string.Empty,
                item.ItemType,
                new DurabilityProperty(_factor));
            return DurabilityIssuanceDecision.Issue(stamp);
        }

        /// <summary>The READ side, and the whole of the no-retroactive-mutation invariant: resolve an item's
        /// effective maximum durability from the VANILLA maximum plus ONLY the signed stamp that exact instance
        /// carries. An unstamped item (Absent) and a tampered/unknown stamp (Tampered) both return the vanilla
        /// maximum unchanged — degrade to vanilla, never a trusted forgery. The crafter's current relationship
        /// state, current purchases, and the currently-configured factor are deliberately NOT inputs: once
        /// issued, the instance's improvement is its own durable fact.</summary>
        public static double ResolveMaxDurability(
            double vanillaMaxDurability, IItemMetadataReader item, WorkmanshipIntegrityKey integrity)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (integrity == null) throw new ArgumentNullException(nameof(integrity));

            var read = DurabilityCodec.Read(item, integrity);
            return read.IsValid
                ? ApplyFactor(vanillaMaxDurability, read.Stamp.Property.Factor)
                : vanillaMaxDurability;
        }

        /// <summary>Apply a frozen factor to a vanilla maximum. Never reduces: a factor at or below the neutral
        /// 1.0 (only reachable from a hand-authored stamp that still validated, i.e. never in production)
        /// leaves the vanilla maximum untouched.</summary>
        internal static double ApplyFactor(double vanillaMaxDurability, double factor) =>
            factor > DurabilityProperty.NeutralFactor ? vanillaMaxDurability * factor : vanillaMaxDurability;
    }
}
