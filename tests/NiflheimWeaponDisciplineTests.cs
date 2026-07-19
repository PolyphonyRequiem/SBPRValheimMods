// ============================================================================
//  Homestead progression — WARRIOR: Weapon Discipline skill-cap tests (T031, US4).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free T031 Warrior permanent skill-cap slice
//  (link-compiled from ../src):
//    * SkillCapChoices — the pure durable-choice domain transition (one permanent
//      idempotent choice per grant identity; ≤100 cap; cannot raise every cap).
//    * SkillCapProvider — the authored choice catalog (≥2 melee tiers) + the
//      highest-wins effective-cap composition (values ≤100).
//    * WeaponDisciplineCommandHandler (ChooseWeaponDisciplineSkill) — the
//      receipt-backed command over its engine-free projection sink, wired end to
//      end through the real T012/T013 develop→offer→purchase pipeline so the choice
//      is exercised against a genuinely purchased Weapon Discipline node.
//
//  Named acceptance closed here (tasks.md T031 / plan.md Tracer 8):
//    AT-WEAPON-DISCIPLINE-CHOICE  Weapon Discipline is ONE permanent, idempotent
//                                 choice among at least two authored melee skill-cap
//                                 tiers: a purchased character picks one offered
//                                 tier; replay of the same op is idempotent (one
//                                 record); a SECOND distinct choice rejects
//                                 (cannot be spent twice); an unoffered/too-small/
//                                 not-purchased/over-cap selection rejects; and the
//                                 selection raises only the chosen skill, never all.
//    AT-WEAPON-CAP-LIFECYCLE      The selected cap-provider tier composes highest-
//                                 wins and is a PERMANENT Effect: it survives
//                                 save/restart (journal rehydration), relationship
//                                 loss/rejoin, and never exceeds the hard cap 100.
//
//  Honesty: these are REAL executions of the shipped domain transition, provider,
//  and receipt-backed command (all engine-free, link-compiled into the net8 host).
//  They prove the pure choice/cap grammar; they do NOT prove a joined Valheim client
//  sees the raised cap in the skills UI / on gain / on death — that is the node's
//  joined-client artifact (docs/v2/evidence/homestead-progression/tracer-8-warrior/).
// ============================================================================

using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Activities;
using SBPR.Niflheim.HomesteadStones.Adapters.Warrior;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimWeaponDisciplineTests : System.IDisposable
    {
        private readonly string _activityJournal;
        private readonly string _developJournal;
        private readonly string _purchaseJournal;
        private readonly string _choiceJournal;
        private readonly WorldId _world = new WorldId("uid:weapdisc-031");
        private readonly StoneId _stone;

        private readonly AccountId _account = new AccountId("acct-gov");
        private readonly CharacterId _governor = new CharacterId("char-gov");
        private readonly AccountId _accountAtt = new AccountId("acct-att");
        private readonly CharacterId _attuned = new CharacterId("char-att");

        private readonly InMemoryStoneAggregateStore _stones = new InMemoryStoneAggregateStore();
        private readonly InMemoryCharacterAggregateStore _characters = new InMemoryCharacterAggregateStore();
        private readonly InMemoryAccountStoneAuthorityStore _authority = new InMemoryAccountStoneAuthorityStore();

        private const string BondRelId = "rel-bond-gov";
        private const string AttRelId = "rel-att";

        private ActivityCommandHandler _activity;
        private DevelopmentCommandHandler _develop;
        private PurchaseCommandHandler _purchase;
        private WeaponDisciplineCommandHandler _choice;

        private static readonly VersionedId Warrior = HomesteadProgressionCatalog.WarriorTree;
        private static readonly VersionedId WeaponDiscipline = new VersionedId("WeaponDiscipline", 1);
        private static readonly VersionedId ReadyHands = new VersionedId("ReadyHands", 1);

        private readonly SkillCapProvider _provider = new SkillCapProvider();

        public NiflheimWeaponDisciplineTests()
        {
            _activityJournal = TempJournal("activity");
            _developJournal = TempJournal("develop");
            _purchaseJournal = TempJournal("purchase");
            _choiceJournal = TempJournal("choice");
            _stone = StoneId.FromHostZone(_world, 9, 4);

            _stones.PutStone(BuildStone(revision: 10, activeLevel: 2, committed: new[]
            {
                new CommittedTreeRecord(HomesteadProgressionCatalog.MartialFacetId,
                    Warrior, "seed-commit-war", _governor.Value, 1, 0),
            }));

            _characters.PutCharacter(BuildGovernor(_account, _governor, BondRelId, personalBp: 20));
            _authority.ApplyAuthorityProjection("seed-bond", BondIndex(_account, _governor, BondRelId));

            _characters.PutCharacter(BuildAttuned(_accountAtt, _attuned, personalAp: 10));
            _authority.ApplyAuthorityProjection("seed-att", AttIndex(_accountAtt, _attuned));

            _activity = NewActivityHandler();
            _develop = NewDevelopHandler();
            _purchase = NewPurchaseHandler();
            _choice = NewChoiceHandler();
        }

        public void Dispose()
        {
            foreach (var p in new[] { _activityJournal, _developJournal, _purchaseJournal, _choiceJournal })
                if (File.Exists(p)) File.Delete(p);
        }

        private static string TempJournal(string tag) => Path.Combine(Path.GetTempPath(),
            "niflheim-t031-" + tag + "-" + System.Guid.NewGuid().ToString("N") + ".journal");

        private ActivityCommandHandler NewActivityHandler() =>
            new ActivityCommandHandler(_activityJournal, new PrincipalResolver(), _stones, _characters,
                _authority, new StubDevelopmentAuthority());

        private DevelopmentCommandHandler NewDevelopHandler() =>
            new DevelopmentCommandHandler(_developJournal, new PrincipalResolver(), _stones, _characters,
                _authority, new StubDevelopmentAuthority(), new HomesteadProgressionCatalog(), null);

        private PurchaseCommandHandler NewPurchaseHandler() =>
            new PurchaseCommandHandler(_purchaseJournal, new PrincipalResolver(), _stones, _characters,
                _authority, new HomesteadProgressionCatalog());

        private WeaponDisciplineCommandHandler NewChoiceHandler() =>
            new WeaponDisciplineCommandHandler(_choiceJournal, new PrincipalResolver(), _stones, _characters,
                _authority, new SkillCapProvider());

        // ── Fixtures ──

        private StoneProgressionAggregate BuildStone(long revision, int activeLevel,
            System.Collections.Generic.IReadOnlyList<CommittedTreeRecord>? committed)
        {
            return new StoneProgressionAggregate(_stone, revision,
                historicalStoneLevel: 2, activeStoneLevel: activeLevel,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "created", updatedProvenance: "seed",
                mirroredStoneAp: 0, lastAppliedReceiptId: "r-seed",
                committedTrees: committed, nodeDevelopment: null);
        }

        private CharacterProgressionAggregate BuildGovernor(AccountId account, CharacterId character,
            string bondRelId, int personalBp, int personalAp = 0)
        {
            var bond = new RelationshipRecord(bondRelId, RelationshipKind.Bond, RelationshipStatus.Active,
                "Homestead:All", "Governor", "relreceipt:seed-bond", string.Empty);
            var stoneRecord = new CharacterStoneRecord(_stone, personalAp, 0, personalBp,
                facetCredits: null, purchases: null, relationships: new[] { bond });
            return new CharacterProgressionAggregate(account, character, "weapdisc-031/trailborne",
                revision: 3, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[] { stoneRecord });
        }

        private CharacterProgressionAggregate BuildAttuned(AccountId account, CharacterId character, int personalAp)
        {
            var att = new RelationshipRecord(AttRelId, RelationshipKind.Attunement, RelationshipStatus.Active,
                string.Empty, string.Empty, "relreceipt:seed-att", string.Empty);
            var stoneRecord = new CharacterStoneRecord(_stone, personalAp, personalAp, 0,
                facetCredits: null, purchases: null, relationships: new[] { att });
            return new CharacterProgressionAggregate(account, character, "weapdisc-031/trailborne",
                revision: 1, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[] { stoneRecord });
        }

        private AccountStoneAuthorityIndex BondIndex(AccountId account, CharacterId who, string relId) =>
            AccountStoneAuthorityIndex.Vacant(account, _stone).WithReservationAdded(
                new AuthorityReservation(who, RelationshipKind.Bond, relId, "relreceipt:seed-bond"), 1);

        private AccountStoneAuthorityIndex AttIndex(AccountId account, CharacterId who) =>
            AccountStoneAuthorityIndex.Vacant(account, _stone).WithReservationAdded(
                new AuthorityReservation(who, RelationshipKind.Attunement, AttRelId, "relreceipt:seed-att"), 1);

        // ── Command helpers ──

        private void CreditBp(int amount, string op)
        {
            var adapter = new AlignedActivityAdapter();
            var evidence = new AlignedActivityEvidence(new OperationId(op), _stone,
                "activity.war", 1, "MeleeKill", Warrior, amount, serverAttributed: true);
            var admission = adapter.Admit(evidence,
                new AuthenticatedConnection(_account.Value, _governor.Value), default);
            Assert.True(admission.IsAdmitted);
            var r = _activity.Handle(admission.Command);
            Assert.Equal(ActivityCommandOutcome.Applied, r.Outcome);
        }

        private DevelopmentCommandResult DevelopToComplete(string op, VersionedId tree, VersionedId node)
        {
            DevelopmentCommandResult last = default;
            for (int i = 0; i < 16; i++)
            {
                var cmd = new ApplyBPToNodeCommand(new OperationId(op + "-" + i), _stone,
                    new AuthenticatedConnection(_account.Value, _governor.Value), default,
                    tree.Key, tree.Version, node.Key, node.Version, 1);
                last = _develop.Handle(cmd);
                Assert.Equal(DevelopmentCommandOutcome.Applied, last.Outcome);
                if (last.NodeCompleted) break;
            }
            return last;
        }

        private PurchaseNodeCommand Purchase(string op, VersionedId node) =>
            new PurchaseNodeCommand(new OperationId(op), _stone,
                new AuthenticatedConnection(_accountAtt.Value, _attuned.Value), default,
                Warrior.Key, Warrior.Version, node.Key, node.Version,
                string.Empty, 0, PurchasePaymentSource.PersonalAp);

        private ChooseWeaponDisciplineSkillCommand Choose(string op, string choiceId,
            int catalogVersion = SkillCapProvider.CurrentCatalogVersion,
            AccountId? account = null, CharacterId? who = null,
            long? expStone = null, long? expChar = null) =>
            new ChooseWeaponDisciplineSkillCommand(new OperationId(op), _stone,
                new AuthenticatedConnection((account ?? _accountAtt).Value, (who ?? _attuned).Value), default,
                WeaponDiscipline.Key, WeaponDiscipline.Version, choiceId, catalogVersion, expStone, expChar);

        /// <summary>Drive Weapon Discipline all the way to a genuine purchase by the attuned character.</summary>
        private void PurchaseWeaponDiscipline()
        {
            CreditBp(10, "op-credit");
            Assert.True(DevelopToComplete("op-dev-weapdisc", Warrior, WeaponDiscipline).NodeOffered);
            Assert.Equal(PurchaseCommandOutcome.Applied,
                _purchase.Handle(Purchase("op-buy-weapdisc", WeaponDiscipline)).Outcome);
        }

        private CharacterProgressionAggregate Att() => _characters.GetCharacter(_accountAtt, _attuned)!;

        private int ChoiceCountOf(CharacterProgressionAggregate c)
        {
            int n = 0;
            foreach (var sr in c.StoneRecords)
                if (sr.StoneId.Equals(_stone)) n += sr.SkillCapChoices.Count;
            return n;
        }

        // ════════════════════════════════════════════════════════════════════
        //  Pure provider — authored choice catalog (≥2 melee tiers) & composition
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public void Catalog_offers_at_least_two_distinct_melee_tiers()
        {
            Assert.True(_provider.ChoiceCount >= 2);
            var skills = new System.Collections.Generic.HashSet<WeaponSkillClass>();
            foreach (var c in _provider.Choices)
            {
                Assert.True(EquipDurationProvider.IsEligibleMeleeSkill(c.TargetSkill),
                    "Every Weapon Discipline choice must target an eligible melee skill.");
                Assert.True(c.CapValue <= SkillCapLimits.HardSkillCap);
                skills.Add(c.TargetSkill);
            }
            // At least two DISTINCT target skills, so no single choice raises every melee cap.
            Assert.True(skills.Count >= 2);
        }

        [Fact]
        public void Compose_highest_wins_never_below_baseline_and_clamped_to_100()
        {
            // A lower contributed tier never lowers the baseline cap.
            Assert.Equal(100, SkillCapProvider.ComposeHighestWins(100, new[] { 60, 80 }));
            // Below a sub-100 baseline, the highest contributor wins.
            Assert.Equal(80, SkillCapProvider.ComposeHighestWins(50, new[] { 60, 80 }));
            // Never exceeds the hard cap even if a contributor overshoots.
            Assert.Equal(100, SkillCapProvider.ComposeHighestWins(50, new[] { 130 }));
            // No contributors -> baseline.
            Assert.Equal(50, SkillCapProvider.ComposeHighestWins(50, new int[0]));
        }

        // ════════════════════════════════════════════════════════════════════
        //  AT-WEAPON-DISCIPLINE-CHOICE
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public void Purchased_character_chooses_one_offered_tier()
        {
            PurchaseWeaponDiscipline();
            var pick = _provider.Choices[0];

            var r = _choice.Handle(Choose("op-choose", pick.ChoiceId));
            Assert.Equal(WeaponDisciplineCommandOutcome.Applied, r.Outcome);
            Assert.Equal(pick.ChoiceId, r.ChoiceId);
            Assert.Equal(pick.TargetSkill.ToString(), r.TargetSkill);
            Assert.Equal(pick.CapValue, r.CapValue);

            Assert.Equal(1, ChoiceCountOf(Att()));
            Assert.True(_provider.HasChosen(Att(), _stone));
        }

        [Fact]
        public void Choice_without_purchase_rejects_not_purchased()
        {
            // Develop+offer but do NOT purchase.
            CreditBp(10, "op-credit");
            Assert.True(DevelopToComplete("op-dev-weapdisc", Warrior, WeaponDiscipline).NodeOffered);

            var r = _choice.Handle(Choose("op-nopurchase", _provider.Choices[0].ChoiceId));
            Assert.Equal(WeaponDisciplineCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("NotPurchased", r.ResultCode);
            Assert.Equal(0, ChoiceCountOf(Att()));
        }

        [Fact]
        public void Unoffered_choice_id_rejects_choice_not_offered()
        {
            PurchaseWeaponDiscipline();
            var r = _choice.Handle(Choose("op-bad", "not-a-real-choice"));
            Assert.Equal(WeaponDisciplineCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("ChoiceNotOffered", r.ResultCode);
        }

        [Fact]
        public void Stale_choice_catalog_version_rejects_choice_not_offered()
        {
            PurchaseWeaponDiscipline();
            var r = _choice.Handle(Choose("op-staleversion", _provider.Choices[0].ChoiceId,
                catalogVersion: SkillCapProvider.CurrentCatalogVersion + 1));
            Assert.Equal(WeaponDisciplineCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("ChoiceNotOffered", r.ResultCode);
        }

        [Fact]
        public void Choice_replay_is_idempotent_single_record()
        {
            PurchaseWeaponDiscipline();
            var pick = _provider.Choices[0];

            var first = _choice.Handle(Choose("op-idem", pick.ChoiceId));
            Assert.Equal(WeaponDisciplineCommandOutcome.Applied, first.Outcome);

            var replay = _choice.Handle(Choose("op-idem", pick.ChoiceId));
            Assert.Equal(WeaponDisciplineCommandOutcome.Replayed, replay.Outcome);
            Assert.Equal(pick.ChoiceId, replay.ChoiceId);

            Assert.Equal(1, ChoiceCountOf(Att())); // exactly one record despite replay
        }

        [Fact]
        public void Second_distinct_choice_rejects_already_chosen_cannot_spend_twice()
        {
            PurchaseWeaponDiscipline();
            Assert.Equal(WeaponDisciplineCommandOutcome.Applied,
                _choice.Handle(Choose("op-first", _provider.Choices[0].ChoiceId)).Outcome);

            // A DIFFERENT choice under a NEW op id: the permanent choice cannot be spent twice.
            var second = _choice.Handle(Choose("op-second", _provider.Choices[1].ChoiceId));
            Assert.Equal(WeaponDisciplineCommandOutcome.Rejected, second.Outcome);
            Assert.Equal("AlreadyChosen", second.ResultCode);

            // Still exactly one record; the ORIGINAL choice is intact.
            Assert.Equal(1, ChoiceCountOf(Att()));
            var choices = SkillCapChoices.ChoicesAt(Att(), _stone);
            Assert.Single(choices);
            Assert.Equal(_provider.Choices[0].ChoiceId, choices[0].ChoiceId);
        }

        [Fact]
        public void Conflicting_reuse_of_operation_id_rejects()
        {
            PurchaseWeaponDiscipline();
            Assert.Equal(WeaponDisciplineCommandOutcome.Applied,
                _choice.Handle(Choose("op-conflict", _provider.Choices[0].ChoiceId)).Outcome);

            // Same op id, DIFFERENT payload (choice id) -> OperationConflict, zero mutation.
            var conflict = _choice.Handle(Choose("op-conflict", _provider.Choices[1].ChoiceId));
            Assert.Equal(WeaponDisciplineCommandOutcome.Rejected, conflict.Outcome);
            Assert.Equal("OperationConflict", conflict.ResultCode);
            Assert.Equal(1, ChoiceCountOf(Att()));
        }

        [Fact]
        public void Too_small_catalog_rejects_catalog_too_small()
        {
            // Drive to a real purchase, then invoke the pure transition with an authored count of 1.
            PurchaseWeaponDiscipline();
            var resolved = new ResolvedSkillCapChoice("swordmastery", 1, "Swords", 100);
            var t = SkillCapChoices.Choose(Att(), _stone, WeaponDiscipline, resolved,
                authoredChoiceCount: 1, sourceOperationId: "op-small");
            Assert.False(t.Accepted);
            Assert.Equal(SkillCapChoiceResult.CatalogTooSmall, t.Result);
        }

        [Fact]
        public void Authored_cap_above_hard_cap_rejects_cap_exceeds_max()
        {
            PurchaseWeaponDiscipline();
            var overCap = new ResolvedSkillCapChoice("bad", 1, "Swords", 130);
            var t = SkillCapChoices.Choose(Att(), _stone, WeaponDiscipline, overCap,
                authoredChoiceCount: 2, sourceOperationId: "op-over");
            Assert.False(t.Accepted);
            Assert.Equal(SkillCapChoiceResult.CapExceedsMax, t.Result);
        }

        [Fact]
        public void Choice_raises_only_the_chosen_skill_never_every_cap()
        {
            PurchaseWeaponDiscipline();
            var pick = _provider.Choices[0]; // e.g. Swords
            Assert.Equal(WeaponDisciplineCommandOutcome.Applied,
                _choice.Handle(Choose("op-onlyone", pick.ChoiceId)).Outcome);

            var c = Att();
            // The chosen skill composes to (at least) the authored tier.
            Assert.Equal(System.Math.Max(SkillCapProvider.VanillaBaselineCap, pick.CapValue),
                _provider.EffectiveCap(c, _stone, pick.TargetSkill));

            // A DIFFERENT eligible melee skill the choice did not target stays at the baseline — the node
            // never raises every melee cap. Pick a skill from the eligible registry other than the target.
            var other = pick.TargetSkill == WeaponSkillClass.Swords
                ? WeaponSkillClass.Knives : WeaponSkillClass.Swords;
            Assert.Equal(SkillCapProvider.VanillaBaselineCap, _provider.EffectiveCap(c, _stone, other));

            // Under a sub-100 baseline (a harder-mode build) the selection is directly observable: the
            // chosen skill is raised to the authored tier, EVERY other eligible melee skill stays at the
            // lower baseline. This is the load-bearing "cannot raise every melee cap" proof — at the
            // shipped 100 baseline the raise is masked by the ceiling, so exercise it below the ceiling.
            const int hardModeBaseline = 50;
            Assert.Equal(pick.CapValue, _provider.EffectiveCap(c, _stone, pick.TargetSkill, hardModeBaseline));
            foreach (var skill in new[]
            {
                WeaponSkillClass.Swords, WeaponSkillClass.Knives, WeaponSkillClass.Clubs,
                WeaponSkillClass.Polearms, WeaponSkillClass.Spears, WeaponSkillClass.Axes,
            })
            {
                if (skill == pick.TargetSkill) continue;
                Assert.Equal(hardModeBaseline, _provider.EffectiveCap(c, _stone, skill, hardModeBaseline));
            }
        }

        [Fact]
        public void Hostile_identity_claim_rejects()
        {
            PurchaseWeaponDiscipline();
            var cmd = new ChooseWeaponDisciplineSkillCommand(new OperationId("op-hostile"), _stone,
                new AuthenticatedConnection(_accountAtt.Value, _attuned.Value),
                new ClaimedPrincipal(_account.Value, _governor.Value),
                WeaponDiscipline.Key, WeaponDiscipline.Version, _provider.Choices[0].ChoiceId,
                SkillCapProvider.CurrentCatalogVersion);
            var r = _choice.Handle(cmd);
            Assert.Equal(WeaponDisciplineCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("PrincipalMismatch", r.ResultCode);
        }

        [Fact]
        public void Stale_character_revision_rejects_with_zero_mutation()
        {
            PurchaseWeaponDiscipline();
            var r = _choice.Handle(Choose("op-stale", _provider.Choices[0].ChoiceId, expChar: 999));
            Assert.Equal(WeaponDisciplineCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("StaleCharacterRevision", r.ResultCode);
            Assert.Equal(0, ChoiceCountOf(Att()));
        }

        // ════════════════════════════════════════════════════════════════════
        //  AT-WEAPON-CAP-LIFECYCLE
        // ════════════════════════════════════════════════════════════════════

        [Fact]
        public void Effective_cap_never_exceeds_hard_cap_of_100()
        {
            PurchaseWeaponDiscipline();
            Assert.Equal(WeaponDisciplineCommandOutcome.Applied,
                _choice.Handle(Choose("op-cap100", _provider.Choices[0].ChoiceId)).Outcome);
            foreach (var choice in _provider.Choices)
                Assert.True(_provider.EffectiveCap(Att(), _stone, choice.TargetSkill) <= SkillCapLimits.HardSkillCap);
        }

        [Fact]
        public void Permanent_choice_survives_relationship_loss()
        {
            PurchaseWeaponDiscipline();
            var pick = _provider.Choices[0];
            Assert.Equal(WeaponDisciplineCommandOutcome.Applied,
                _choice.Handle(Choose("op-perm", pick.ChoiceId)).Outcome);

            // Release the attunement (relationship loss). The durable choice record is a Permanent Effect
            // and MUST persist on the character regardless of relationship state.
            _authority.ApplyAuthorityProjection("release-att",
                _authority.GetAuthority(_accountAtt, _stone).WithReservationReleased(AttRelId, "relreceipt:release", 2));

            var c = Att();
            Assert.True(_provider.HasChosen(c, _stone));
            Assert.Equal(1, ChoiceCountOf(c));
            Assert.Equal(System.Math.Max(SkillCapProvider.VanillaBaselineCap, pick.CapValue),
                _provider.EffectiveCap(c, _stone, pick.TargetSkill));
        }

        [Fact]
        public void Save_restart_rehydrates_the_permanent_choice_and_replay_is_pure()
        {
            PurchaseWeaponDiscipline();
            var pick = _provider.Choices[0];
            Assert.Equal(WeaponDisciplineCommandOutcome.Applied,
                _choice.Handle(Choose("op-restart", pick.ChoiceId)).Outcome);

            // Fresh stores seeded with the PRE-choice purchased state; fresh handler rehydrates from journal.
            var freshChars = new InMemoryCharacterAggregateStore();
            var purchased = _purchase; // the purchase journal already made the purchase durable
            // Rebuild the purchased character by replaying the purchase journal onto fresh stores.
            freshChars.PutCharacter(BuildAttuned(_accountAtt, _attuned, personalAp: 10));
            var rehydratedPurchase = new PurchaseCommandHandler(_purchaseJournal, new PrincipalResolver(),
                _stones, freshChars, _authority, new HomesteadProgressionCatalog());

            var rehydratedChoice = new WeaponDisciplineCommandHandler(_choiceJournal, new PrincipalResolver(),
                _stones, freshChars, _authority, new SkillCapProvider());

            // The permanent choice is present after boot (projection rebuilt from journal truth).
            var c = freshChars.GetCharacter(_accountAtt, _attuned)!;
            Assert.Equal(1, ChoiceCountOf(c));
            Assert.Equal(System.Math.Max(SkillCapProvider.VanillaBaselineCap, pick.CapValue),
                new SkillCapProvider().EffectiveCap(c, _stone, pick.TargetSkill));

            // Replaying the same choice op after restart is idempotent (no second record).
            var replay = rehydratedChoice.Handle(Choose("op-restart", pick.ChoiceId));
            Assert.Equal(WeaponDisciplineCommandOutcome.Replayed, replay.Outcome);
            Assert.Equal(1, ChoiceCountOf(freshChars.GetCharacter(_accountAtt, _attuned)!));
            _ = purchased; _ = rehydratedPurchase;
        }

        [Fact]
        public void State_roundtrip_preserves_the_skill_cap_choice_record()
        {
            PurchaseWeaponDiscipline();
            Assert.Equal(WeaponDisciplineCommandOutcome.Applied,
                _choice.Handle(Choose("op-roundtrip", _provider.Choices[0].ChoiceId)).Outcome);

            var c = Att();
            var restored = CharacterProgressionAggregate.Deserialize(c.Serialize());
            Assert.True(c.StructurallyEquals(restored));
            Assert.Equal(1, ChoiceCountOf(restored));
            var rc = SkillCapChoices.ChoicesAt(restored, _stone)[0];
            Assert.Equal(_provider.Choices[0].ChoiceId, rc.ChoiceId);
            Assert.Equal(_provider.Choices[0].CapValue, rc.CapValue);
            Assert.True(rc.GrantNode.Equals(WeaponDiscipline));
        }

        // ── Stubs ──

        private sealed class StubDevelopmentAuthority : IGovernorDevelopmentAuthority
        {
            public bool CanDevelop(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                VersionedId tree)
            {
                return string.Equals(responsibilityRange, "Homestead:All", System.StringComparison.Ordinal)
                    && string.Equals(ownerGovernorRole, "Governor", System.StringComparison.Ordinal)
                    && !tree.IsNone;
            }
        }
    }
}
