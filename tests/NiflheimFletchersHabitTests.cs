// ============================================================================
//  Homestead progression — ARCHER / FLETCHER'S HABIT tests (T027, US4).
// ----------------------------------------------------------------------------
//  Exercises the engine-free T027 Archer node-3 vertical slice (link-compiled
//  from ../src): the pure ProjectileRecoveryProvider + exact consumed-arrow
//  provenance that make ONE authoritative terminal-impact recovery decision for
//  a single fired eligible arrow instance.
//
//  Fletcher's Habit is a PERMANENT Effect (data-model.md §"Archer | 1 | Fletcher's
//  Habit | Permanent Effect | personal Offered"; spec line 161 "Fletcher's Habit
//  permanently gives one configurable, authoritative terminal-impact recovery
//  chance for one exact eligible arrow instance"). Unlike a Character Effect it is
//  NOT relationship-dormant: once purchased it REMAINS owned through relationship
//  loss / revocation (spec line 130 "Permanent Effects remain active"; line 260 "A
//  released character retains Permanent Effects"). Ownership therefore derives from
//  the PURCHASE record (persisted), not from the caller's currently-active
//  relationship — the single behavioural difference from the sibling Field
//  Fletching I Character Effect (T026).
//
//  Named acceptance closed here (tasks.md T027):
//    AT-FLETCHER-HIT-LIFECYCLE  a fired eligible arrow that terminally impacts a
//                               recoverable surface produces ONE authoritative
//                               recovery result (roll against the configured
//                               chance); water / shield / miss / TTL each resolve
//                               to exactly one deterministic result; a non-owner or
//                               ineligible arrow yields vanilla behaviour.
//    AT-FLETCHER-NO-DUP         a single arrow instance resolves at most ONCE and
//                               recovers at most ONE exact consumed instance; a
//                               multishot volley resolves each instance
//                               independently with no cross-instance duplication;
//                               deterministic Practice Range target return SUPPRESSES
//                               the roll entirely (no recovered arrow, spec Edge
//                               case "target return wins ... the permanent recovery
//                               roll does not run").
// ============================================================================

using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Archer;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimFletchersHabitTests
    {
        private readonly WorldId _world = new WorldId("uid:fh-027");
        private readonly StoneId _stone;

        private readonly AccountId _account = new AccountId("acct-fletcher");
        private readonly CharacterId _character = new CharacterId("char-fletcher");
        private readonly CharacterId _sibling = new CharacterId("char-sibling");

        private static readonly VersionedId Archer = HomesteadProgressionCatalog.ArcherTree;
        private static readonly VersionedId FletchersHabit = new VersionedId("FletchersHabit", 1);

        private readonly HomesteadProgressionCatalog _catalog = new HomesteadProgressionCatalog();
        private readonly ProjectileRecoveryProvider _provider;

        // A canonical exact consumed Wood Arrow instance (the eligible arrow), with full item provenance
        // so a recovered arrow can be proven to be the EXACT consumed one (no substitution, no dup).
        private static readonly ConsumedArrowProvenance WoodArrow =
            new ConsumedArrowProvenance(
                itemId: FletchersHabitContent.EligibleArrowItem,
                quality: 1, variant: 0, durability: 100.0,
                crafterId: 42, crafterName: "Fletcher", customData: "seed:abc");

        public NiflheimFletchersHabitTests()
        {
            _stone = StoneId.FromHostZone(_world, 9, 4);
            _provider = new ProjectileRecoveryProvider(_catalog);
        }

        private StoneProgressionAggregate BuildStone(bool developed = true, bool offered = true)
        {
            var committed = new List<CommittedTreeRecord>
            {
                new CommittedTreeRecord(HomesteadProgressionCatalog.MartialFacetId,
                    Archer, "seed-commit-archer", _character.Value, 1, 0)
            };
            var development = new List<NodeDevelopmentRecord>();
            if (developed)
                development.Add(new NodeDevelopmentRecord(FletchersHabit, 1, 1, true, offered, "seed-dev-fh"));

            return new StoneProgressionAggregate(_stone, revision: 5, historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "c", updatedProvenance: "u",
                mirroredStoneAp: 3, lastAppliedReceiptId: "r",
                committedTrees: committed, nodeDevelopment: development);
        }

        private CharacterProgressionAggregate BuildCharacter(CharacterId character, bool withPurchase)
        {
            NodePurchaseRecord[]? purchases = withPurchase
                ? new[]
                {
                    new NodePurchaseRecord(Archer, FletchersHabit, "ap:personal",
                        "PermanentEffect", VersionedId.None, "op-buy-fh")
                }
                : null;

            var stoneRecord = new CharacterStoneRecord(_stone, 3, 3, 1, null, purchases, null);
            return new CharacterProgressionAggregate(_account, character,
                "world-scope", 1, 2, 2, "receipt", new[] { stoneRecord });
        }

        private AccountStoneAuthorityIndex BuildAuthority(CharacterId? activeCharacter)
        {
            var idx = AccountStoneAuthorityIndex.Vacant(_account, _stone);
            if (activeCharacter.HasValue)
                idx = idx.WithReservationAdded(
                    new AuthorityReservation(activeCharacter.Value, RelationshipKind.Bond,
                        "rel-fh", "relreceipt:seed"), 1);
            return idx;
        }

        // Ownership: the Permanent Effect is owned when the caller holds the purchase record — regardless
        // of whether the supplying relationship is currently active.
        private bool Owns(bool withPurchase, CharacterId? activeCharacter, bool developed = true)
        {
            var stone = BuildStone(developed);
            var character = BuildCharacter(_character, withPurchase);
            var authority = BuildAuthority(activeCharacter);
            return _provider.OwnsFletchersHabit(stone, character, authority);
        }

        // ── AT-FLETCHER-HIT-LIFECYCLE — ownership derivation (Permanent semantics) ──

        [Fact]
        public void PurchasedWithActiveRelationship_IsOwned()
        {
            Assert.True(Owns(withPurchase: true, activeCharacter: _character));
        }

        [Fact]
        public void PurchasedButRelationshipLost_StillOwned_PermanentEffectPersists()
        {
            // The load-bearing difference from a Character Effect: relationship loss does NOT revoke a
            // Permanent Effect (spec line 130 / line 260). The purchase alone is the ownership truth.
            Assert.True(Owns(withPurchase: true, activeCharacter: null));
        }

        [Fact]
        public void SiblingHoldsRelationship_PurchasedCallerStillOwned()
        {
            // Another character's reservation is irrelevant to a persisted personal Permanent purchase.
            Assert.True(Owns(withPurchase: true, activeCharacter: _sibling));
        }

        [Fact]
        public void NotPurchased_IsNotOwned()
        {
            Assert.False(Owns(withPurchase: false, activeCharacter: _character));
        }

        [Fact]
        public void UndevelopedNode_EvenPurchased_IsNotOwned()
        {
            Assert.False(Owns(withPurchase: true, activeCharacter: _character, developed: false));
        }

        // ── AT-FLETCHER-HIT-LIFECYCLE — one authoritative result per surface ──

        [Fact]
        public void OwnedEligibleArrow_HitRecoverableSurface_LowRoll_Recovers_ExactInstance()
        {
            // roll below the configured chance ⇒ Recovered, exactly one instance, exact provenance.
            var decision = _provider.Resolve(
                owned: true, provenance: WoodArrow, surface: RecoverySurface.SolidStructure,
                targetReturnWon: false, roll: 0.0);

            Assert.Equal(RecoveryOutcome.Recovered, decision.Outcome);
            Assert.Equal(1, decision.RecoveredCount);
            Assert.True(decision.Recovered);
            Assert.Equal(WoodArrow, decision.RecoveredArrow);
            Assert.True(decision.Authoritative);
        }

        [Fact]
        public void OwnedEligibleArrow_HitRecoverableSurface_HighRoll_DoesNotRecover()
        {
            var decision = _provider.Resolve(
                owned: true, provenance: WoodArrow, surface: RecoverySurface.Creature,
                targetReturnWon: false, roll: 1.0);

            Assert.Equal(RecoveryOutcome.RollFailed, decision.Outcome);
            Assert.Equal(0, decision.RecoveredCount);
            Assert.False(decision.Recovered);
        }

        [Theory]
        [InlineData(RecoverySurface.Water)]
        [InlineData(RecoverySurface.LostOrExpired)]
        public void OwnedEligibleArrow_NonRecoverableSurface_NeverRecovers_NoRollEvenAtZero(RecoverySurface surface)
        {
            // Water and miss/TTL are authoritatively non-recoverable: a definitive single result even
            // with the most favourable roll. The arrow is gone; the roll does not run.
            var decision = _provider.Resolve(
                owned: true, provenance: WoodArrow, surface: surface,
                targetReturnWon: false, roll: 0.0);

            Assert.Equal(RecoveryOutcome.NonRecoverableSurface, decision.Outcome);
            Assert.Equal(0, decision.RecoveredCount);
            Assert.False(decision.Recovered);
            Assert.True(decision.Authoritative);
        }

        [Fact]
        public void OwnedEligibleArrow_ShieldBlocked_IsRecoverable()
        {
            // A shield-blocked arrow comes to rest at a solid surface — recoverable (rolls).
            var decision = _provider.Resolve(
                owned: true, provenance: WoodArrow, surface: RecoverySurface.ShieldBlocked,
                targetReturnWon: false, roll: 0.0);

            Assert.Equal(RecoveryOutcome.Recovered, decision.Outcome);
            Assert.Equal(1, decision.RecoveredCount);
        }

        [Fact]
        public void NotOwned_EligibleArrow_YieldsVanillaBehaviour_NoRecovery()
        {
            var decision = _provider.Resolve(
                owned: false, provenance: WoodArrow, surface: RecoverySurface.SolidStructure,
                targetReturnWon: false, roll: 0.0);

            Assert.Equal(RecoveryOutcome.NotOwned, decision.Outcome);
            Assert.Equal(0, decision.RecoveredCount);
            Assert.False(decision.Recovered);
        }

        [Fact]
        public void Owned_IneligibleArrow_YieldsVanillaBehaviour_NoRecovery()
        {
            // A different arrow (not the configured eligible one) is not affected by Fletcher's Habit.
            var ironArrow = new ConsumedArrowProvenance("ArrowIron", 1, 0, 100.0, 42, "Fletcher", "seed:xyz");
            var decision = _provider.Resolve(
                owned: true, provenance: ironArrow, surface: RecoverySurface.SolidStructure,
                targetReturnWon: false, roll: 0.0);

            Assert.Equal(RecoveryOutcome.IneligibleArrow, decision.Outcome);
            Assert.Equal(0, decision.RecoveredCount);
        }

        // ── AT-FLETCHER-NO-DUP — target-return exclusion ──

        [Fact]
        public void TargetReturnWon_SuppressesTheRoll_NoRecoveredArrow()
        {
            // spec Edge case: Practice Range deterministic target return wins; the permanent recovery
            // roll does NOT run — even with the most favourable roll and full ownership.
            var decision = _provider.Resolve(
                owned: true, provenance: WoodArrow, surface: RecoverySurface.ArcheryTarget,
                targetReturnWon: true, roll: 0.0);

            Assert.Equal(RecoveryOutcome.SuppressedByTargetReturn, decision.Outcome);
            Assert.Equal(0, decision.RecoveredCount);
            Assert.False(decision.Recovered);
            Assert.True(decision.Authoritative);
        }

        [Fact]
        public void TargetReturnDecision_FromPracticeRange_IntegratesAsSuppression()
        {
            // The Practice Range T025 deterministic decision is the exact input Fletcher's Habit yields to.
            var pr = PracticeRangeProvider.ResolveTargetReturn(TerminalImpactSurface.ArcheryTarget);
            Assert.True(pr.TargetReturnWon);

            var decision = _provider.Resolve(
                owned: true, provenance: WoodArrow, surface: RecoverySurface.ArcheryTarget,
                targetReturnWon: pr.TargetReturnWon, roll: 0.0);
            Assert.Equal(RecoveryOutcome.SuppressedByTargetReturn, decision.Outcome);
        }

        // ── AT-FLETCHER-NO-DUP — one-result-per-instance / multishot ──

        [Fact]
        public void SameArrowInstance_ResolvedTwice_RecoversAtMostOnce()
        {
            var session = new ProjectileRecoverySession();
            var d1 = session.ResolveOnce(_provider, instanceId: 7001,
                owned: true, provenance: WoodArrow, surface: RecoverySurface.SolidStructure,
                targetReturnWon: false, roll: 0.0);
            Assert.Equal(RecoveryOutcome.Recovered, d1.Outcome);
            Assert.Equal(1, d1.RecoveredCount);

            // Re-entrant resolution of the SAME instance must not mint a second arrow (no-dup guarantee).
            var d2 = session.ResolveOnce(_provider, instanceId: 7001,
                owned: true, provenance: WoodArrow, surface: RecoverySurface.SolidStructure,
                targetReturnWon: false, roll: 0.0);
            Assert.Equal(RecoveryOutcome.AlreadyResolved, d2.Outcome);
            Assert.Equal(0, d2.RecoveredCount);

            Assert.Equal(1, session.TotalRecovered);
        }

        [Fact]
        public void MultishotVolley_ResolvesEachInstanceIndependently_NoCrossInstanceDup()
        {
            // A three-arrow volley: two land on recoverable surfaces (roll passes), one lands in water.
            var session = new ProjectileRecoverySession();

            var a = session.ResolveOnce(_provider, 8001, true, WoodArrow, RecoverySurface.SolidStructure, false, 0.0);
            var b = session.ResolveOnce(_provider, 8002, true, WoodArrow, RecoverySurface.Creature, false, 0.0);
            var c = session.ResolveOnce(_provider, 8003, true, WoodArrow, RecoverySurface.Water, false, 0.0);

            Assert.Equal(RecoveryOutcome.Recovered, a.Outcome);
            Assert.Equal(RecoveryOutcome.Recovered, b.Outcome);
            Assert.Equal(RecoveryOutcome.NonRecoverableSurface, c.Outcome);

            // Exactly two exact instances recovered across the whole volley; each keyed to its own arrow.
            Assert.Equal(2, session.TotalRecovered);
        }

        [Fact]
        public void ConfiguredChance_IsBoundaryInclusiveAtRollBelowChance_ExclusiveAtOrAbove()
        {
            double chance = FletchersHabitContent.DefaultRecoveryChance;
            // roll strictly below chance recovers; roll == chance does NOT (half-open [0,chance)).
            var below = _provider.Resolve(true, WoodArrow, RecoverySurface.SolidStructure, false, chance - 1e-9);
            var at = _provider.Resolve(true, WoodArrow, RecoverySurface.SolidStructure, false, chance);
            Assert.Equal(RecoveryOutcome.Recovered, below.Outcome);
            Assert.Equal(RecoveryOutcome.RollFailed, at.Outcome);
        }

        [Fact]
        public void RecoveredArrow_PreservesExactProvenance_NotASubstitute()
        {
            var decision = _provider.Resolve(true, WoodArrow, RecoverySurface.SolidStructure, false, 0.0);
            var recovered = decision.RecoveredArrow;
            Assert.Equal(WoodArrow.ItemId, recovered.ItemId);
            Assert.Equal(WoodArrow.Quality, recovered.Quality);
            Assert.Equal(WoodArrow.Variant, recovered.Variant);
            Assert.Equal(WoodArrow.Durability, recovered.Durability);
            Assert.Equal(WoodArrow.CrafterId, recovered.CrafterId);
            Assert.Equal(WoodArrow.CrafterName, recovered.CrafterName);
            Assert.Equal(WoodArrow.CustomData, recovered.CustomData);
            Assert.Equal(WoodArrow, recovered);
        }

        [Fact]
        public void NoneDecision_IsInert()
        {
            var none = RecoveryDecision.None;
            Assert.Equal(RecoveryOutcome.NotOwned, none.Outcome);
            Assert.Equal(0, none.RecoveredCount);
            Assert.False(none.Recovered);
        }

        // ── AT-FLETCHER-HIT-LIFECYCLE — durable ownership on the delivery wire (Permanent semantics) ──
        //
        // The pure OwnsFletchersHabit derives ownership host-side; a PURE CLIENT reads durable ownership from
        // the server-stamped PersonalActivationSnapshot. These prove the wire carries the Permanent-Effect
        // distinction: OWNED depends on developed+purchased and NOT on the (possibly-lost) relationship, so a
        // relationship-dormant snapshot (Active=false) still reports IsOwned=true.

        private static SBPR.Niflheim.HomesteadStones.Application.Activation.PersonalActivationSnapshot
            SnapshotWith(StoneId stone, AccountId occ, CharacterId chr, bool developed, bool purchased, bool active)
        {
            var row = new SBPR.Niflheim.HomesteadStones.Application.Activation.PersonalActivationRow(
                FletchersHabit, developed, offered: true, purchased: purchased, active: active);
            return new SBPR.Niflheim.HomesteadStones.Application.Activation.PersonalActivationSnapshot(
                stone, occ, chr, sequence: 1, stoneRevision: 1, characterRevision: 1, authorityRevision: 1,
                authorityPresent: true, new[] { row });
        }

        [Fact]
        public void Snapshot_PurchasedButRelationshipInactive_IsOwned_ButNotActive()
        {
            // Permanent Effect: relationship-dormant (Active=false) yet still OWNED (developed+purchased).
            var snap = SnapshotWith(_stone, _account, _character, developed: true, purchased: true, active: false);
            Assert.True(snap.IsOwned(FletchersHabit));
            Assert.False(snap.IsActive(FletchersHabit));
        }

        [Fact]
        public void Snapshot_NotPurchased_IsNotOwned()
        {
            var snap = SnapshotWith(_stone, _account, _character, developed: true, purchased: false, active: false);
            Assert.False(snap.IsOwned(FletchersHabit));
        }

        [Fact]
        public void Snapshot_Undeveloped_IsNotOwned_EvenIfPurchasedBitSet()
        {
            var snap = SnapshotWith(_stone, _account, _character, developed: false, purchased: true, active: false);
            Assert.False(snap.IsOwned(FletchersHabit));
        }

        [Fact]
        public void DeniedSnapshot_OwnsNothing_FailClosed()
        {
            var denied = SBPR.Niflheim.HomesteadStones.Application.Activation.PersonalActivationSnapshot
                .Denied(_stone, _account, _character, sequence: 1);
            Assert.False(denied.IsOwned(FletchersHabit));
        }

        [Fact]
        public void ClientCache_IsOwnedForStone_ReadsDurableOwnership_RelationshipIndependent()
        {
            var cache = new SBPR.Niflheim.HomesteadStones.Application.Activation.PersonalActivationClientCache();
            // Apply a relationship-dormant but purchased snapshot — the client still OWNS the Permanent Effect.
            cache.Apply(SnapshotWith(_stone, _account, _character, developed: true, purchased: true, active: false));
            Assert.True(cache.IsOwnedForStone(_stone, FletchersHabit));
            // But it is NOT active (relationship lost) — the two queries are distinct.
            Assert.False(cache.IsActiveForStone(_stone, FletchersHabit));
        }

        [Fact]
        public void ClientCache_IsOwnedForStone_FailsClosed_WhenNoSnapshotHeld()
        {
            var cache = new SBPR.Niflheim.HomesteadStones.Application.Activation.PersonalActivationClientCache();
            Assert.False(cache.IsOwnedForStone(_stone, FletchersHabit));
        }
    }
}
