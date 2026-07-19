using System;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Cooking
{
    // T018 (Tracer 5, Cooking node 3 of 4) — the trusted, engine-free Iron Stomach food
    // refresh-threshold provider (spec §US4 sc1 "Iron Stomach permanently permits food
    // refresh/replacement at 75% remaining"; contracts.md §Cooking "FoodRefreshThresholdProvider:
    // Iron Stomach supplies threshold 0.75, highest applicable provider wins; three slots and normal
    // food debit remain"; data-model.md §"Cooking | 1 | Iron Stomach | Permanent Effect | personal
    // Offered"; research.md "Iron Stomach changes only the configured threshold and preserves three
    // slots, debit, stats, and duration").
    //
    // Architecture decision A1 (plan.md): a derived provider translates persisted character/Stone
    // state into a typed capability. It writes no ledger; it is a PURE projection.
    //
    // The load-bearing distinction from the T017 Field Prep Character Effect: Iron Stomach is a
    // PERMANENT Effect. Per data-model.md §CharacterProgression, "Permanent Effects and Progression
    // Keys survive relationship loss and Tree revocation." So — unlike Field Prep, whose exposure goes
    // dormant the instant the caller's relationship drops (it routes through DerivedActivationView's
    // relationship-gated Active bit) — the Iron Stomach threshold is keyed on the character's DURABLE
    // purchase record (outcome class PermanentEffect) ALONE:
    //   * No active-relationship conjunct: releasing the relationship never removes the purchase, so
    //     the raised threshold persists.
    //   * No Settlement Local policy / build Permission conjunct: those gate LOCAL effects (Savor
    //     drain / Practice Range placement), not a personal Permanent Effect.
    //   * No Stone node-development conjunct: Tree revocation removes development yet the Permanent
    //     Effect survives — so the provider takes no Stone aggregate and reads only the character.
    //   * Restart-durable: the purchase round-trips through the serialized character aggregate, so the
    //     provider re-derives the identical threshold with zero writes.
    //
    // What Iron Stomach changes and what it PRESERVES: it changes ONLY the configured food
    // refresh/replacement threshold — the remaining-fraction cutoff below which vanilla lets you eat a
    // food again — raising it from the vanilla baseline (0.5) to 0.75 (refresh permitted at 75%
    // remaining). It preserves the three food slots and the normal food debit, stats, and duration; it
    // authors and mutates none of them. It is a THRESHOLD provider only.
    //
    // "Highest applicable provider wins" (contracts.md): the resolved threshold is the MAXIMUM of all
    // applicable candidate thresholds (the vanilla baseline plus the Iron Stomach 0.75 candidate when
    // acquired), never a sum and never a value that lowers a stronger baseline.
    //
    // net48 audit: only System + the engine-free Domain value objects. No net5+ surface, no
    // UnityEngine/Valheim/BepInEx reference, so this link-compiles into the net8 test project exactly
    // like the sibling adapters (CookingProviders, CookingCraftPolicy, PracticeRangeProvider,
    // EffectiveStationLevelProvider).

    /// <summary>The resolved Iron Stomach food-refresh capability for one caller. Pure projection:
    /// whether the durable Permanent Effect is acquired, the resulting refresh/replacement threshold,
    /// and the preserved-invariant markers (three slots, normal debit/stats/duration). Carries no
    /// mutable state — flip the acquisition and re-derive with zero writes.</summary>
    public readonly struct FoodRefreshCapability
    {
        public FoodRefreshCapability(bool acquired, double threshold)
        {
            Acquired = acquired;
            Threshold = threshold;
        }

        /// <summary>The Iron Stomach Permanent Effect is durably acquired by this character (a purchase
        /// record for the Iron Stomach node at some Stone). Being a Permanent Effect, this survives
        /// relationship loss, Tree revocation, and restart — there is no relationship conjunct.</summary>
        public bool Acquired { get; }

        /// <summary>The resolved food refresh/replacement threshold: the remaining-duration fraction at
        /// or below which a food may be eaten again. 0.75 when Iron Stomach is acquired (refresh at 75%
        /// remaining), otherwise the vanilla baseline 0.5.</summary>
        public double Threshold { get; }

        /// <summary>The number of food slots — always three. Iron Stomach is a threshold provider only;
        /// it never reduces the slots (contracts.md "three slots ... remain").</summary>
        public int FoodSlots => FoodRefreshThresholdProvider.PreservedFoodSlots;

        /// <summary>The normal food debit, stats, and duration are preserved untouched — Iron Stomach
        /// changes ONLY the refresh threshold (research.md "preserves ... debit, stats, and
        /// duration"). Always true.</summary>
        public bool PreservesNormalDebitStatsDuration => true;

        /// <summary>Whether a food at the given remaining-duration fraction may be refreshed/replaced
        /// under the resolved threshold. Inclusive at the boundary ("refresh at 75% remaining"): a food
        /// AT exactly the threshold is refreshable.</summary>
        public bool CanRefresh(double remainingFraction) => remainingFraction <= Threshold;

        /// <summary>An inert capability: Iron Stomach not acquired, vanilla baseline threshold, slots
        /// and debit/stats/duration preserved.</summary>
        public static readonly FoodRefreshCapability None =
            new FoodRefreshCapability(false, FoodRefreshThresholdProvider.VanillaBaselineThreshold);
    }

    /// <summary>The pure Iron Stomach food refresh-threshold provider (contracts.md §Cooking
    /// "FoodRefreshThresholdProvider"). Derives the durable Permanent-Effect acquisition from the
    /// character's purchase records and supplies the raised refresh/replacement threshold (0.75),
    /// composing with other candidate thresholds by taking the highest applicable one. Keys ONLY on the
    /// durable purchase — no relationship, policy, permission, or Stone-development conjunct — so the
    /// threshold survives relationship loss, Tree revocation, and restart.</summary>
    public sealed class FoodRefreshThresholdProvider
    {
        /// <summary>The stable Iron Stomach personal Permanent-Effect node identity in the current build
        /// (HomesteadProgressionCatalog: Cooking / IronStomach v1).</summary>
        public static readonly VersionedId IronStomachNode = new VersionedId("IronStomach", 1);

        /// <summary>The outcome class stamped on a Permanent-Effect purchase (NodePurchases.OutcomeClassOf
        /// → NodeOutcomeType.PermanentEffect). Iron Stomach is durable ONLY as a Permanent Effect; a
        /// Character-Effect purchase of a same-keyed node would not be a durable Iron Stomach grant.</summary>
        public const string PermanentEffectOutcomeClass = "PermanentEffect";

        /// <summary>The vanilla baseline food refresh/replacement threshold: a food may be eaten again at
        /// or below 50% remaining. This is the safe floor when no stronger provider applies.</summary>
        public const double VanillaBaselineThreshold = 0.5;

        /// <summary>The Iron Stomach candidate threshold: while acquired, food refresh/replacement is
        /// permitted at 75% remaining (spec §US4 sc1). This is the ONLY tuning knob; final balance is
        /// deferred (research.md — the mechanic remains replaceable before compatibility freeze).</summary>
        public const double IronStomachThreshold = 0.75;

        /// <summary>The preserved number of food slots — always three (contracts.md "three slots ...
        /// remain"). Iron Stomach never reduces them.</summary>
        public const int PreservedFoodSlots = 3;

        /// <summary>Compose candidate thresholds by taking the HIGHEST applicable one (contracts.md
        /// "highest applicable provider wins"). With no candidates the safe vanilla baseline is returned
        /// — never a fabricated grant.</summary>
        public static double Compose(params double[] candidates)
        {
            double resolved = VanillaBaselineThreshold;
            if (candidates != null)
            {
                foreach (var c in candidates)
                    if (c > resolved) resolved = c;
            }
            return resolved;
        }

        /// <summary>Resolve the Iron Stomach capability for one caller from their durable character
        /// aggregate ALONE. A Permanent Effect keys on the durable purchase record (outcome class
        /// PermanentEffect) for the Iron Stomach node at any Stone — no relationship, policy, permission,
        /// or Stone-development conjunct, so the threshold survives relationship loss / Tree revocation /
        /// restart.</summary>
        public FoodRefreshCapability Resolve(CharacterProgressionAggregate character)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));

            bool acquired = HasDurableIronStomach(character);
            return acquired
                ? new FoodRefreshCapability(true, IronStomachThreshold)
                : FoodRefreshCapability.None;
        }

        /// <summary>Resolve the effective food refresh/replacement threshold for this caller against a
        /// supplied baseline candidate (typically <see cref="VanillaBaselineThreshold"/>, or a stronger
        /// baseline from another applicable provider). "Highest applicable provider wins": the result is
        /// the maximum of the baseline and the Iron Stomach candidate (when acquired).</summary>
        public double ResolveThreshold(CharacterProgressionAggregate character, double baselineThreshold)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));

            return HasDurableIronStomach(character)
                ? Compose(baselineThreshold, IronStomachThreshold)
                : Compose(baselineThreshold);
        }

        /// <summary>Whether a food at the given remaining-duration fraction may be refreshed/replaced for
        /// this caller under the resolved threshold. Inclusive at the boundary.</summary>
        public bool CanRefresh(CharacterProgressionAggregate character, double remainingFraction) =>
            Resolve(character).CanRefresh(remainingFraction);

        /// <summary>Whether the character durably holds Iron Stomach: a purchase record for the exact
        /// Iron Stomach node identity whose outcome class is the durable Permanent Effect class. Matches
        /// across every Stone record — the Permanent Effect is not scoped to a single Stone's current
        /// state.</summary>
        private static bool HasDurableIronStomach(CharacterProgressionAggregate character)
        {
            foreach (var sr in character.StoneRecords)
            {
                foreach (var p in sr.Purchases)
                {
                    if (p.Node.Key == IronStomachNode.Key &&
                        p.Node.Version == IronStomachNode.Version &&
                        string.Equals(p.OutcomeClass, PermanentEffectOutcomeClass, StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }
    }
}
