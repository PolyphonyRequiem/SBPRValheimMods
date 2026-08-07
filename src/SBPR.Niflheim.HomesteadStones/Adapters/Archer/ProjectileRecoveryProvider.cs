using System;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Archer
{
    // T027 (Tracer 7, Archer node 3 of 3) — the trusted, engine-free Fletcher's Habit projectile-recovery
    // provider (spec §"Archer" line 161 "Fletcher's Habit permanently gives one configurable, authoritative
    // terminal-impact recovery chance for one exact eligible arrow instance"; contracts.md §Archer
    // "ProjectileRecoveryProvider: Fletcher's Habit makes one authoritative terminal-impact decision for one
    // exact consumed eligible arrow; deterministic Practice Range return suppresses this roll"; data-model.md
    // §"Archer | 1 | Fletcher's Habit | Permanent Effect | personal Offered"; research.md line 139 "Projectile
    // can retain exact consumed ItemData and spawn it at terminal impact ... one-result guarantee, water/
    // shield/miss/TTL/multishot cases, target-return exclusion").
    //
    // Architecture decision A1 (plan.md): a derived provider translates persisted character/Stone state +
    // server-observed terminal-impact facts into a typed decision. It writes no ledger; it is a PURE
    // projection layered on the shipped T004 DerivedActivationView.
    //
    // WHAT MAKES THIS NODE DIFFERENT — the FIRST Permanent Effect implemented (cf. the sibling T026 Field
    // Fletching I Character Effect, and T025 Practice Range Local Effect):
    //
    //   * Fletcher's Habit is a personal PERMANENT Effect (NodeOutcomeType.PermanentEffect, personal
    //     Offered). Its ownership is DURABLE: once purchased it REMAINS owned through relationship loss and
    //     revocation (spec line 130 "Permanent Effects remain active"; spec line 260 "A released character
    //     retains Permanent Effects and Progression Keys"; US4 sc6 "Permanent Effects remain active"). This
    //     is the one behavioural divergence from a Character Effect: a Character Effect dormants when its
    //     supplying relationship goes inactive, a Permanent Effect does NOT. Ownership therefore derives from
    //     the PURCHASE record (persisted provenance), never from the caller's currently-active relationship —
    //     the developed node + a purchase record is the whole ownership truth. There is still NO second
    //     mutable active-effects ledger: OwnsFletchersHabit re-derives ownership from persisted state each
    //     call (AT-NO-ACTIVE-LEDGER, carried by T004).
    //   * The effect makes ONE AUTHORITATIVE terminal-impact decision for one exact consumed eligible arrow
    //     instance. "Authoritative" and "one result" are the load-bearing guarantees (research.md line 139):
    //     the server makes exactly one decision per fired instance; the decision is a pure function of
    //     (owned, arrow eligibility, terminal surface, target-return exclusion, one roll). Non-recoverable
    //     surfaces (water, miss/TTL) are definitively lost — the roll does not run. Recoverable surfaces
    //     (solid structure, ground, creature, shield-blocked at-rest) roll the one configured chance; on a
    //     pass the EXACT consumed ItemData provenance is respawned (no substitution, exact instance), on a
    //     fail nothing is recovered.
    //   * Deterministic Practice Range target return SUPPRESSES the roll entirely (spec Edge case "target
    //     return wins its deterministic path and the permanent recovery roll does not run"). The caller
    //     passes the T025 TargetReturnDecision.TargetReturnWon flag; when true the outcome is
    //     SuppressedByTargetReturn with nothing recovered here (the deterministic path already returned it).
    //
    // The one-result-per-instance and multishot no-duplication guarantees are enforced by
    // ProjectileRecoverySession, which keys resolution by the fired instance id so a re-entrant resolution of
    // the same arrow cannot mint a second recovered instance, and a volley resolves each instance
    // independently.
    //
    // Client claims are NEVER the source of truth: the caller supplies authenticated aggregates + the
    // SERVER-OBSERVED terminal surface / consumed provenance / owner facts; nothing here trusts a payload.
    //
    // net48 audit: only System + the engine-free Domain value objects / catalog / activation view. No net5+
    // surface, no UnityEngine/Valheim/BepInEx reference, so this link-compiles into the net8 test project
    // exactly like the sibling adapters.

    /// <summary>The stable surface a fired arrow terminally impacted, as attributed by trusted server code.
    /// Distinct from the T025 <see cref="TerminalImpactSurface"/> (Practice Range's deterministic-return
    /// classification); this is the recovery-relevant classification Fletcher's Habit consumes. Recoverable
    /// surfaces let the arrow come to rest where it can be picked up; non-recoverable surfaces destroy it.</summary>
    public enum RecoverySurface
    {
        /// <summary>The arrow struck a solid built structure and came to rest — recoverable.</summary>
        SolidStructure = 0,

        /// <summary>The arrow struck terrain/ground and came to rest — recoverable.</summary>
        Ground = 1,

        /// <summary>The arrow struck a creature/character and came to rest — recoverable.</summary>
        Creature = 2,

        /// <summary>The arrow was blocked by a shield and came to rest at a solid surface — recoverable.</summary>
        ShieldBlocked = 3,

        /// <summary>The arrow struck water — NON-recoverable (sinks/lost). The roll does not run.</summary>
        Water = 4,

        /// <summary>The arrow missed, was destroyed, or expired (TTL) with no recoverable surface —
        /// NON-recoverable. The roll does not run.</summary>
        LostOrExpired = 5,

        /// <summary>The arrow struck the vanilla Archery Target — the Practice Range deterministic return
        /// path owns this surface; Fletcher's Habit yields to it (see <c>targetReturnWon</c>).</summary>
        ArcheryTarget = 6
    }

    /// <summary>The authoritative outcome of one terminal-impact decision. Exactly one applies per fired
    /// instance — the "one authoritative result" guarantee (research.md line 139).</summary>
    public enum RecoveryOutcome
    {
        /// <summary>The caller does not own Fletcher's Habit — vanilla behaviour, nothing recovered.</summary>
        NotOwned = 0,

        /// <summary>The impacted arrow is not the configured eligible arrow — vanilla behaviour.</summary>
        IneligibleArrow = 1,

        /// <summary>Deterministic Practice Range target return won; the recovery roll did not run.</summary>
        SuppressedByTargetReturn = 2,

        /// <summary>The surface is non-recoverable (water / miss / TTL); the arrow is lost, no roll.</summary>
        NonRecoverableSurface = 3,

        /// <summary>Owned + eligible + recoverable surface, but the one configured roll failed.</summary>
        RollFailed = 4,

        /// <summary>Owned + eligible + recoverable surface + roll passed: the EXACT consumed instance is
        /// recovered (respawned), exactly once.</summary>
        Recovered = 5,

        /// <summary>This exact fired instance was already resolved once — a re-entrant resolution returns
        /// this and recovers nothing (the no-duplication guarantee, enforced by the session).</summary>
        AlreadyResolved = 6
    }

    /// <summary>The exact consumed arrow ItemData provenance a fired eligible arrow carries, so a recovered
    /// arrow can be proven to be the EXACT consumed one — no substitution, no duplication (research.md line
    /// 139 "retain exact consumed ItemData"). Pure engine-free value object; the net48 seam maps a real
    /// <c>ItemDrop.ItemData</c> into and out of this. Value equality on every field.</summary>
    public readonly struct ConsumedArrowProvenance : IEquatable<ConsumedArrowProvenance>
    {
        public ConsumedArrowProvenance(string itemId, int quality, int variant, double durability,
            long crafterId, string crafterName, string customData)
        {
            ItemId = itemId ?? string.Empty;
            Quality = quality;
            Variant = variant;
            Durability = durability;
            CrafterId = crafterId;
            CrafterName = crafterName ?? string.Empty;
            CustomData = customData ?? string.Empty;
        }

        /// <summary>The vanilla item id (prefab name, clone-suffix stripped) of the consumed arrow.</summary>
        public string ItemId { get; }

        /// <summary>The consumed instance quality level.</summary>
        public int Quality { get; }

        /// <summary>The consumed instance visual variant.</summary>
        public int Variant { get; }

        /// <summary>The consumed instance durability.</summary>
        public double Durability { get; }

        /// <summary>The consumed instance crafter player id (0 when unset).</summary>
        public long CrafterId { get; }

        /// <summary>The consumed instance crafter name.</summary>
        public string CrafterName { get; }

        /// <summary>Opaque serialized custom item data preserved verbatim.</summary>
        public string CustomData { get; }

        /// <summary>An empty provenance (no consumed instance captured).</summary>
        public static readonly ConsumedArrowProvenance None =
            new ConsumedArrowProvenance(string.Empty, 0, 0, 0.0, 0, string.Empty, string.Empty);

        /// <summary>True when no meaningful consumed instance was captured (empty item id).</summary>
        public bool IsNone => string.IsNullOrEmpty(ItemId);

        public bool Equals(ConsumedArrowProvenance other) =>
            string.Equals(ItemId, other.ItemId, StringComparison.Ordinal) &&
            Quality == other.Quality &&
            Variant == other.Variant &&
            Durability.Equals(other.Durability) &&
            CrafterId == other.CrafterId &&
            string.Equals(CrafterName, other.CrafterName, StringComparison.Ordinal) &&
            string.Equals(CustomData, other.CustomData, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is ConsumedArrowProvenance other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ItemId.GetHashCode();
                hash = (hash * 397) ^ Quality;
                hash = (hash * 397) ^ Variant;
                hash = (hash * 397) ^ Durability.GetHashCode();
                hash = (hash * 397) ^ CrafterId.GetHashCode();
                hash = (hash * 397) ^ CrafterName.GetHashCode();
                return (hash * 397) ^ CustomData.GetHashCode();
            }
        }

        public static bool operator ==(ConsumedArrowProvenance a, ConsumedArrowProvenance b) => a.Equals(b);
        public static bool operator !=(ConsumedArrowProvenance a, ConsumedArrowProvenance b) => !a.Equals(b);
    }

    /// <summary>The authored Fletcher's Habit content: which arrow is eligible and the configurable recovery
    /// chance. The eligible arrow is the exact vanilla Wood Arrow (the earliest, always-present arrow, same
    /// blueprint Field Fletching I exposes). The chance is a provisional proof value (spec "one configurable
    /// ... recovery chance"); balance is not locked here.</summary>
    public static class FletchersHabitContent
    {
        /// <summary>The exact eligible arrow item id (vanilla Wood Arrow). Fletcher's Habit affects only this
        /// one arrow instance type; any other arrow yields vanilla behaviour.</summary>
        public const string EligibleArrowItem = "ArrowWood";

        /// <summary>The configurable recovery chance in [0,1] (provisional proof value). A fired eligible
        /// arrow on a recoverable surface recovers when the one roll is strictly below this chance. Authored
        /// at 0.5 for the proof; the "configurable" contract is honoured by making this the single authored
        /// constant a future config surface overrides.</summary>
        public const double DefaultRecoveryChance = 0.5;
    }

    /// <summary>The single authoritative terminal-impact decision for one fired eligible arrow instance.
    /// Exactly one <see cref="RecoveryOutcome"/> applies; at most one exact instance is recovered.</summary>
    public readonly struct RecoveryDecision
    {
        public RecoveryDecision(RecoveryOutcome outcome, int recoveredCount, ConsumedArrowProvenance recoveredArrow)
        {
            Outcome = outcome;
            RecoveredCount = recoveredCount;
            RecoveredArrow = recoveredArrow;
        }

        /// <summary>The one authoritative outcome for this fired instance.</summary>
        public RecoveryOutcome Outcome { get; }

        /// <summary>How many exact instances this decision recovers (0 or 1).</summary>
        public int RecoveredCount { get; }

        /// <summary>The exact consumed provenance recovered (respawned) when <see cref="Recovered"/>; else
        /// <see cref="ConsumedArrowProvenance.None"/>. Proves the recovered arrow is the EXACT consumed one.</summary>
        public ConsumedArrowProvenance RecoveredArrow { get; }

        /// <summary>At least one exact instance was recovered.</summary>
        public bool Recovered => RecoveredCount > 0;

        /// <summary>This is a single, definitive server decision (always true — every branch is one result).</summary>
        public bool Authoritative => true;

        /// <summary>An inert decision: not owned, nothing recovered.</summary>
        public static readonly RecoveryDecision None =
            new RecoveryDecision(RecoveryOutcome.NotOwned, 0, ConsumedArrowProvenance.None);
    }

    public sealed class ProjectileRecoveryProvider
    {
        /// <summary>The stable Fletcher's Habit personal Permanent-Effect node identity in the current build
        /// (HomesteadProgressionCatalog: Archer / FletchersHabit v1).</summary>
        public static readonly VersionedId FletchersHabitNode = new VersionedId("FletchersHabit", 1);

        // Held for parity with sibling providers and future catalog-driven validation; the ownership
        // derivation itself needs only the character + authority + Stone development aggregates.
        private readonly HomesteadProgressionCatalog _catalog;
        private readonly double _recoveryChance;

        public ProjectileRecoveryProvider(HomesteadProgressionCatalog catalog)
            : this(catalog, FletchersHabitContent.DefaultRecoveryChance)
        {
        }

        public ProjectileRecoveryProvider(HomesteadProgressionCatalog catalog, double recoveryChance)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (recoveryChance < 0.0 || recoveryChance > 1.0)
                throw new ArgumentOutOfRangeException(nameof(recoveryChance), "Recovery chance must be in [0,1].");
            _recoveryChance = recoveryChance;
        }

        /// <summary>The configured recovery chance in [0,1] this provider rolls against.</summary>
        public double RecoveryChance => _recoveryChance;

        /// <summary>Whether the caller OWNS Fletcher's Habit — the durable Permanent-Effect ownership truth:
        /// the node is developed on this Stone AND the caller holds a purchase record for it. Unlike a
        /// Character Effect, this is NOT gated by the caller's currently-active relationship — a Permanent
        /// Effect persists through relationship loss / revocation (spec line 130 / line 260). Re-derived from
        /// persisted state each call; no second ledger.</summary>
        public bool OwnsFletchersHabit(
            StoneProgressionAggregate stone,
            CharacterProgressionAggregate character,
            AccountStoneAuthorityIndex authority)
        {
            if (stone == null) throw new ArgumentNullException(nameof(stone));
            if (character == null) throw new ArgumentNullException(nameof(character));
            if (authority == null) throw new ArgumentNullException(nameof(authority));

            // Reuse the shipped T004 view for its consistent purchase/development derivation, then read the
            // Permanent-Effect ownership from PURCHASED (not Active): the purchase persists regardless of the
            // relationship, so a Permanent Effect stays owned when a Character Effect would dormant.
            var view = DerivedActivationView.Derive(stone, character, authority);
            foreach (var row in view.Nodes)
            {
                if (row.Node.Key == FletchersHabitNode.Key)
                    return row.Developed && row.Purchased;
            }
            return false;
        }

        /// <summary>Make the ONE authoritative terminal-impact decision for a single fired arrow instance.
        /// Pure function of the inputs — the caller supplies server-observed facts:
        ///   * <paramref name="owned"/>: whether the shooter owns Fletcher's Habit (from
        ///     <see cref="OwnsFletchersHabit"/>).
        ///   * <paramref name="provenance"/>: the exact consumed arrow ItemData captured at fire time.
        ///   * <paramref name="surface"/>: the server-classified terminal surface.
        ///   * <paramref name="targetReturnWon"/>: the T025 Practice Range deterministic-return flag — when
        ///     true, the recovery roll is SUPPRESSED (spec Edge case).
        ///   * <paramref name="roll"/>: one roll in [0,1) supplied by trusted RNG. Recovery on
        ///     <c>roll &lt; RecoveryChance</c> (half-open), so chance 0 never recovers and chance 1 always does.
        /// Precedence is fixed and total: not-owned → ineligible → target-return suppression →
        /// non-recoverable surface → roll. Exactly one outcome; at most one exact instance recovered.</summary>
        public RecoveryDecision Resolve(
            bool owned,
            ConsumedArrowProvenance provenance,
            RecoverySurface surface,
            bool targetReturnWon,
            double roll)
        {
            // 1. Ownership: no Fletcher's Habit ⇒ vanilla behaviour.
            if (!owned)
                return new RecoveryDecision(RecoveryOutcome.NotOwned, 0, ConsumedArrowProvenance.None);

            // 2. Eligibility: only the exact configured arrow is affected.
            if (!string.Equals(provenance.ItemId, FletchersHabitContent.EligibleArrowItem, StringComparison.Ordinal))
                return new RecoveryDecision(RecoveryOutcome.IneligibleArrow, 0, ConsumedArrowProvenance.None);

            // 3. Target-return exclusion: the deterministic Practice Range return already handled this arrow;
            //    the permanent recovery roll does NOT run (spec Edge case). Checked before the surface roll so
            //    an Archery Target hit under Practice Range never double-returns.
            if (targetReturnWon)
                return new RecoveryDecision(RecoveryOutcome.SuppressedByTargetReturn, 0, ConsumedArrowProvenance.None);

            // 4. Non-recoverable surfaces: water and miss/TTL destroy the arrow; the Archery Target surface
            //    without a target-return win still isn't a Fletcher's Habit recovery surface. Definitive, no roll.
            if (!IsRecoverable(surface))
                return new RecoveryDecision(RecoveryOutcome.NonRecoverableSurface, 0, ConsumedArrowProvenance.None);

            // 5. The one configured roll. Half-open [0, chance): chance 0 ⇒ never, chance 1 ⇒ always.
            if (roll < _recoveryChance)
                return new RecoveryDecision(RecoveryOutcome.Recovered, 1, provenance);

            return new RecoveryDecision(RecoveryOutcome.RollFailed, 0, ConsumedArrowProvenance.None);
        }

        /// <summary>Whether a terminal surface can yield a recoverable arrow. Solid structure, ground,
        /// creature, and shield-blocked all leave the arrow at rest; water and miss/TTL lose it; the Archery
        /// Target belongs to the Practice Range deterministic path, not the Fletcher's Habit roll.</summary>
        private static bool IsRecoverable(RecoverySurface surface)
        {
            switch (surface)
            {
                case RecoverySurface.SolidStructure:
                case RecoverySurface.Ground:
                case RecoverySurface.Creature:
                case RecoverySurface.ShieldBlocked:
                    return true;
                default:
                    return false;
            }
        }
    }
}
