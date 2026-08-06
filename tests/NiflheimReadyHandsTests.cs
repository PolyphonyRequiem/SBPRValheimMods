// ============================================================================
//  Homestead progression — WARRIOR: Ready Hands provider tests (T030, US4).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free T030 Warrior effect-delivery provider
//  (link-compiled from ../src):
//    * EquipDurationProvider — the derived queued equip/unequip duration
//      provider that reads the T004 DerivedActivationView Active bit for the
//      Ready Hands personal node and shortens the COPIED per-action duration for
//      eligible melee weapons only, with NO shared-prefab mutation.
//
//  Named acceptance closed here (tasks.md T030 / plan.md Tracer 8):
//    AT-READY-HANDS-BOTH-HALVES  while Ready Hands is active for the caller, BOTH
//                                the queued equip AND unequip durations of an
//                                eligible melee weapon are shortened identically;
//                                relationship loss / dormancy restores the full
//                                vanilla duration immediately with zero writes.
//    AT-READY-HANDS-EXCLUSIONS   the shortening applies ONLY to the authored
//                                eligible melee registry (Swords/Knives/Clubs/
//                                Polearms/Spears/Axes) on equip/unequip actions;
//                                armor(None)/tools/bows/crossbows/shields/magic
//                                and the Reload action are excluded, and the
//                                shared prefab duration is never mutated.
//
//  Honesty: these are REAL executions of the shipped provider + the shipped T004
//  DerivedActivationView derivation (both engine-free, link-compiled into the
//  net8 host). They prove the pure delivery grammar; they do NOT prove a joined
//  Valheim client sees the shortened equip timer in-world — that is the node's
//  joined-client artifact (docs/v2/evidence/homestead-progression/tracer-8-warrior/).
// ============================================================================

using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Warrior;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimReadyHandsTests
    {
        private static readonly WorldId World = new WorldId("uid:ready-hands-030");
        private static readonly StoneId Stone = StoneId.FromHostZone(World, 7, -3);
        private static readonly AccountId OwnerAccount = new AccountId("acct-owner");
        private static readonly CharacterId OwnerChar = new CharacterId("char-owner");

        private static readonly VersionedId ReadyHands = new VersionedId("ReadyHands", 1);
        private static readonly VersionedId WarriorTree = new VersionedId("Warrior", 2);
        private static readonly VersionedId WarriorL1 = new VersionedId("Warrior-L1", 1);

        private readonly EquipDurationProvider _provider = new EquipDurationProvider();

        // A Stone with the Ready Hands personal node developed + Offered. Personal Character Effects are
        // purchased (not Stone-cultivated), so the Stone side only carries development/offered state.
        private static StoneProgressionAggregate BuildStone(bool readyHandsOffered = true)
        {
            var nodes = new List<NodeDevelopmentRecord>
            {
                new NodeDevelopmentRecord(ReadyHands, 1, 1, developed: true, offered: readyHandsOffered, "op-dev-rh"),
            };
            return new StoneProgressionAggregate(
                Stone, revision: 5,
                historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "created", updatedProvenance: "seed",
                mirroredStoneAp: 0, lastAppliedReceiptId: "r-seed",
                committedTrees: null, nodeDevelopment: nodes);
        }

        // A character who HAS purchased Ready Hands at this Stone (provenance only; no stored "active").
        private static CharacterProgressionAggregate BuildBuyer()
        {
            var purchases = new[]
            {
                new NodePurchaseRecord(WarriorTree, ReadyHands, "PersonalAP", "CharacterEffect", WarriorL1, "op-buy-rh"),
            };
            var record = new CharacterStoneRecord(Stone, personalAp: 3, cumulativeAp: 3, personalBp: 5,
                purchases: purchases);
            return new CharacterProgressionAggregate(OwnerAccount, OwnerChar,
                worldProductScope: "world/trailborne", revision: 3,
                bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "receipt:char",
                stoneRecords: new[] { record });
        }

        // A character who has NOT purchased Ready Hands.
        private static CharacterProgressionAggregate BuildNonBuyer()
        {
            var record = new CharacterStoneRecord(Stone, personalAp: 3, cumulativeAp: 3, personalBp: 5);
            return new CharacterProgressionAggregate(OwnerAccount, OwnerChar,
                worldProductScope: "world/trailborne", revision: 3,
                bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "receipt:char",
                stoneRecords: new[] { record });
        }

        private static AccountStoneAuthorityIndex Authority(RelationshipKind kind)
        {
            var reservations = kind == RelationshipKind.None
                ? null
                : new[] { new AuthorityReservation(OwnerChar, kind, "rel-1", "receipt:activate-1") };
            return new AccountStoneAuthorityIndex(OwnerAccount, Stone, revision: 2, reservations, lastReleaseReceiptId: "");
        }

        private static DerivedActivationView DeriveActive() =>
            DerivedActivationView.Derive(BuildStone(), BuildBuyer(), Authority(RelationshipKind.Attunement));

        private static DerivedActivationView DeriveDormant() =>
            DerivedActivationView.Derive(BuildStone(), BuildBuyer(), Authority(RelationshipKind.None));

        // The full authored eligible melee registry, and the excluded classes.
        public static readonly WeaponSkillClass[] EligibleMelee =
        {
            WeaponSkillClass.Swords, WeaponSkillClass.Knives, WeaponSkillClass.Clubs,
            WeaponSkillClass.Polearms, WeaponSkillClass.Spears, WeaponSkillClass.Axes,
        };

        public static readonly WeaponSkillClass[] ExcludedClasses =
        {
            WeaponSkillClass.None,          // armor / non-weapon
            WeaponSkillClass.Blocking,      // shields
            WeaponSkillClass.Bows,          // ranged
            WeaponSkillClass.Crossbows,     // ranged + reload
            WeaponSkillClass.ElementalMagic,
            WeaponSkillClass.BloodMagic,
            WeaponSkillClass.Unarmed,
            WeaponSkillClass.Pickaxes,      // tool
            WeaponSkillClass.WoodCutting,   // tool
        };

        // ============================================================================
        //  AT-READY-HANDS-BOTH-HALVES
        // ============================================================================

        [Fact]
        public void Active_effect_is_derived_from_purchase_plus_relationship()
        {
            Assert.True(_provider.IsActive(DeriveActive()));
            Assert.False(_provider.IsActive(DeriveDormant()));
        }

        [Fact]
        public void Both_equip_and_unequip_are_shortened_identically_for_eligible_melee()
        {
            var active = DeriveActive();
            foreach (var skill in EligibleMelee)
            {
                var equip = _provider.ResolveDuration(active, skill, QueuedEquipAction.Equip, 2.0);
                var unequip = _provider.ResolveDuration(active, skill, QueuedEquipAction.Unequip, 2.0);

                Assert.True(equip.Shortened);
                Assert.True(unequip.Shortened);
                // BOTH halves scale identically by the authored factor.
                Assert.Equal(1.0, equip.Duration);
                Assert.Equal(1.0, unequip.Duration);
                Assert.Equal(0.5, _provider.DurationFactor(active, skill, QueuedEquipAction.Equip));
                Assert.Equal(0.5, _provider.DurationFactor(active, skill, QueuedEquipAction.Unequip));
            }
        }

        [Fact]
        public void Relationship_loss_restores_full_duration_immediately_both_halves()
        {
            var dormant = DeriveDormant();
            foreach (var skill in EligibleMelee)
            {
                var equip = _provider.ResolveDuration(dormant, skill, QueuedEquipAction.Equip, 2.0);
                var unequip = _provider.ResolveDuration(dormant, skill, QueuedEquipAction.Unequip, 2.0);

                Assert.False(equip.Shortened);
                Assert.False(unequip.Shortened);
                Assert.Equal(2.0, equip.Duration);   // full vanilla duration retained
                Assert.Equal(2.0, unequip.Duration);
                Assert.Equal(1.0, _provider.DurationFactor(dormant, skill, QueuedEquipAction.Equip));
            }
        }

        [Fact]
        public void Non_buyer_never_shortens_even_with_active_relationship()
        {
            var view = DerivedActivationView.Derive(BuildStone(), BuildNonBuyer(), Authority(RelationshipKind.Attunement));
            Assert.False(_provider.IsActive(view));
            var equip = _provider.ResolveDuration(view, WeaponSkillClass.Swords, QueuedEquipAction.Equip, 2.0);
            Assert.False(equip.Shortened);
            Assert.Equal(2.0, equip.Duration);
        }

        [Fact]
        public void Provider_is_stateless_across_interleaved_evaluations()
        {
            var active = DeriveActive();
            var dormant = DeriveDormant();
            // Each answer depends ONLY on the view handed in — no hysteresis / carried state.
            Assert.Equal(0.5, _provider.DurationFactor(active, WeaponSkillClass.Axes, QueuedEquipAction.Equip));
            Assert.Equal(1.0, _provider.DurationFactor(dormant, WeaponSkillClass.Axes, QueuedEquipAction.Equip));
            Assert.Equal(0.5, _provider.DurationFactor(active, WeaponSkillClass.Axes, QueuedEquipAction.Unequip));
            Assert.Equal(1.0, _provider.DurationFactor(dormant, WeaponSkillClass.Axes, QueuedEquipAction.Unequip));
        }

        // ============================================================================
        //  AT-READY-HANDS-EXCLUSIONS
        // ============================================================================

        [Fact]
        public void Registry_membership_is_exactly_the_six_melee_weapon_skills()
        {
            foreach (var skill in EligibleMelee)
                Assert.True(EquipDurationProvider.IsEligibleMeleeSkill(skill));
            foreach (var skill in ExcludedClasses)
                Assert.False(EquipDurationProvider.IsEligibleMeleeSkill(skill));
        }

        [Fact]
        public void Excluded_classes_are_never_shortened_even_when_active()
        {
            var active = DeriveActive();
            foreach (var skill in ExcludedClasses)
            {
                var equip = _provider.ResolveDuration(active, skill, QueuedEquipAction.Equip, 2.0);
                var unequip = _provider.ResolveDuration(active, skill, QueuedEquipAction.Unequip, 2.0);
                Assert.False(equip.Shortened);
                Assert.False(unequip.Shortened);
                Assert.Equal(2.0, equip.Duration);
                Assert.Equal(2.0, unequip.Duration);
            }
        }

        [Fact]
        public void Reload_action_is_never_shortened_even_for_a_crossbow_or_melee()
        {
            var active = DeriveActive();
            // Crossbow reload (the real reload case) and a spurious melee "reload" both excluded:
            // reload duration is built from the weapon loading time, never m_equipDuration.
            var crossbowReload = _provider.ResolveDuration(active, WeaponSkillClass.Crossbows, QueuedEquipAction.Reload, 3.0);
            var meleeReload = _provider.ResolveDuration(active, WeaponSkillClass.Swords, QueuedEquipAction.Reload, 3.0);
            Assert.False(crossbowReload.Shortened);
            Assert.False(meleeReload.Shortened);
            Assert.Equal(3.0, crossbowReload.Duration);
            Assert.Equal(3.0, meleeReload.Duration);
            Assert.False(EquipDurationProvider.IsEquipDurationAction(QueuedEquipAction.Reload));
        }

        [Fact]
        public void Instant_toggle_zero_base_duration_is_returned_unchanged()
        {
            // Vanilla never queues a minor action when m_equipDuration <= 0 (decomp :22097); guard it.
            var active = DeriveActive();
            var d = _provider.ResolveDuration(active, WeaponSkillClass.Swords, QueuedEquipAction.Equip, 0.0);
            Assert.False(d.Shortened);
            Assert.Equal(0.0, d.Duration);
        }

        [Fact]
        public void Deriving_and_resolving_mutates_no_persisted_state()
        {
            // The provider only scales the per-action COPY it is handed — it holds no ItemData, no shared
            // prefab, and cannot write one. Re-deriving from the same aggregates yields the same answer and
            // the character aggregate is byte-identical (the T004 view carries no ledger to poke).
            var stone = BuildStone();
            var buyer = BuildBuyer();
            var before = buyer.Serialize();
            var view = DerivedActivationView.Derive(stone, buyer, Authority(RelationshipKind.Attunement));
            _provider.ResolveDuration(view, WeaponSkillClass.Clubs, QueuedEquipAction.Equip, 2.0);
            _provider.ResolveDuration(view, WeaponSkillClass.Clubs, QueuedEquipAction.Unequip, 2.0);
            Assert.Equal(before, buyer.Serialize());
        }
    }
}
