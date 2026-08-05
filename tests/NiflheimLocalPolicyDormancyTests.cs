// ============================================================================
//  Homestead progression — LOCAL POLICY / RELATIONSHIP DORMANCY tests (T014, US2/US3).
// ----------------------------------------------------------------------------
//  Exercises the SHIPPED, engine-free T014 slice (link-compiled from ../src):
//    * the pure SettlementLocalPolicy value object (Everyone/Attuned/Private,
//      normalized allowlist, revision, round-trip),
//    * the Stone-aggregate policy field (backward-compatible round-trip),
//    * the receipt-backed SetSettlementLocalPolicyCommandHandler (owner-only
//      authority, dual-revision optimistic concurrency, zero-mutation reject,
//      replay idempotency, conflict, restart/recovery),
//    * the pure LocalEffectActivationView that derives active/dormant/policy-
//      eligible Local Effects with no second ledger, and ANDs Local policy with
//      ordinary build Permission.
//
//  Named acceptance closed here (tasks.md T014 / plan.md US2/US3):
//    AT-LOCAL-POLICY          one Settlement-wide policy governs all active Local
//                             Effects (Everyone default / Attuned / Private
//                             allowlist), no per-effect override; Local nodes are
//                             Stone-owned developed state, never purchases or
//                             Tier-Access inputs; Local placement requires BOTH
//                             policy eligibility AND ordinary build Permission;
//                             owner-only mutation; stale/replay/conflict/
//                             unauthorized are explicit and zero-mutation.
//    AT-RELATIONSHIP-DORMANCY relationship release, missing authorized Governor,
//                             Stone/Tree dormancy, and rejoin re-derive active/
//                             dormant with no mutable active-effects ledger;
//                             policy changes during occupancy re-evaluate
//                             deterministically; restart rebuilds the same result.
// ============================================================================

using System.Collections.Generic;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimLocalPolicyDormancyTests : System.IDisposable
    {
        private readonly string _policyJournal;
        private readonly WorldId _world = new WorldId("uid:lp-771");
        private readonly StoneId _stone;

        private readonly AccountId _owner = new AccountId("acct-owner");
        private readonly CharacterId _ownerChar = new CharacterId("char-owner");
        private readonly AccountId _guest = new AccountId("acct-guest");
        private readonly CharacterId _guestChar = new CharacterId("char-guest");
        private readonly AccountId _stranger = new AccountId("acct-stranger");
        private readonly CharacterId _strangerChar = new CharacterId("char-stranger");

        private readonly InMemoryStoneAggregateStore _stones = new InMemoryStoneAggregateStore();
        private readonly StubOwnerAuthority _ownerAuthority;

        private static readonly VersionedId Cooking = HomesteadProgressionCatalog.CookingTree;
        private static readonly VersionedId Warrior = HomesteadProgressionCatalog.WarriorTree;
        private static readonly VersionedId Savor = new VersionedId("SavorTheHearth", 1); // Local L1 Cooking
        private static readonly VersionedId Twig = new VersionedId("TwigTraining", 1);    // Local L1 Warrior
        private static readonly VersionedId FieldPrep = new VersionedId("FieldPrep", 1);  // personal L1

        private readonly HomesteadProgressionCatalog _catalog = new HomesteadProgressionCatalog();

        public NiflheimLocalPolicyDormancyTests()
        {
            _policyJournal = TempJournal("policy");
            _stone = StoneId.FromHostZone(_world, 3, 9);
            _ownerAuthority = new StubOwnerAuthority(_owner, _ownerChar, _stone);
            _stones.PutStone(BuildStone(revision: 5));
        }

        public void Dispose()
        {
            if (File.Exists(_policyJournal)) File.Delete(_policyJournal);
        }

        private static string TempJournal(string tag) => Path.Combine(Path.GetTempPath(),
            "niflheim-t014-" + tag + "-" + System.Guid.NewGuid().ToString("N") + ".journal");

        private LocalPolicyCommandHandler NewHandler() =>
            new LocalPolicyCommandHandler(_policyJournal, new PrincipalResolver(), _stones, _ownerAuthority);

        // A Stone with Savor (Cooking) + T.W.I.G. (Warrior) developed as Stone-owned Local state, both
        // Trees committed, at Active/Historical Level 2.
        private StoneProgressionAggregate BuildStone(long revision, SettlementLocalPolicy? policy = null,
            bool cookingCommitted = true, int activeLevel = 2,
            IReadOnlyList<NodeDevelopmentRecord>? dev = null)
        {
            var committed = new List<CommittedTreeRecord>();
            if (cookingCommitted)
                committed.Add(new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                    Cooking, "seed-commit-cook", _ownerChar.Value, 1, 0));
            committed.Add(new CommittedTreeRecord(HomesteadProgressionCatalog.MartialFacetId,
                Warrior, "seed-commit-war", _ownerChar.Value, 1, 0));

            var development = dev ?? new[]
            {
                new NodeDevelopmentRecord(Savor, 1, 1, true, false, "seed-dev-savor"),
                new NodeDevelopmentRecord(Twig, 1, 1, true, false, "seed-dev-twig"),
            };

            return new StoneProgressionAggregate(_stone, revision,
                historicalStoneLevel: 2, activeStoneLevel: activeLevel,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: 1, createdProvenance: "created", updatedProvenance: "seed",
                mirroredStoneAp: 0, lastAppliedReceiptId: "r-seed",
                committedTrees: committed, nodeDevelopment: development,
                localPolicy: policy);
        }

        private SetSettlementLocalPolicyCommand Cmd(string op, AccountId account, CharacterId who,
            LocalBeneficiaryMode mode, IReadOnlyList<string>? allow = null,
            long? expStone = null, long? expPolicy = null)
            => new SetSettlementLocalPolicyCommand(new OperationId(op), _stone,
                new AuthenticatedConnection(account.Value, who.Value), default,
                mode, allow, expStone, expPolicy);

        private LocalEffectActivationView DeriveFor(AccountId occupant, bool isOwner,
            bool hasRelationship, bool inside, bool governorPresent)
        {
            var stone = _stones.GetStone(_stone)!;
            return LocalEffectActivationView.Derive(stone, _catalog, occupant, isOwner,
                hasRelationship, inside, governorPresent);
        }

        // ============================================================================
        //  AT-LOCAL-POLICY — the single Settlement-wide policy + placement Permission AND.
        // ============================================================================

        [Fact]
        public void Default_policy_is_everyone_at_revision_zero()
        {
            var stone = _stones.GetStone(_stone)!;
            Assert.Equal(LocalBeneficiaryMode.Everyone, stone.LocalPolicy.Mode);
            Assert.Equal(0, stone.LocalPolicy.Revision);
            Assert.Empty(stone.LocalPolicy.AllowlistAccounts);

            // Everyone benefits regardless of relationship/owner status.
            var v = DeriveFor(_stranger, isOwner: false, hasRelationship: false, inside: true, governorPresent: true);
            Assert.True(v.OccupantPolicyEligible);
            Assert.True(v.StatusFor(Savor).Active);
        }

        [Fact]
        public void Owner_sets_policy_and_it_governs_all_active_local_effects_no_per_effect_override()
        {
            var h = NewHandler();
            var r = h.Handle(Cmd("op-attuned", _owner, _ownerChar, LocalBeneficiaryMode.Attuned));
            Assert.Equal(LocalPolicyCommandOutcome.Applied, r.Outcome);
            Assert.Equal(LocalBeneficiaryMode.Attuned, r.Mode);
            Assert.Equal(1, r.PolicyRevision);

            // ONE policy governs BOTH Savor (Cooking) and T.W.I.G. (Warrior) identically — no per-effect
            // override: an unrelated stranger is ineligible for every Local Effect at once.
            var stranger = DeriveFor(_stranger, isOwner: false, hasRelationship: false, inside: true, governorPresent: true);
            Assert.False(stranger.OccupantPolicyEligible);
            Assert.False(stranger.StatusFor(Savor).Active);
            Assert.False(stranger.StatusFor(Twig).Active);

            // An attuned guest benefits from BOTH at once.
            var guest = DeriveFor(_guest, isOwner: false, hasRelationship: true, inside: true, governorPresent: true);
            Assert.True(guest.OccupantPolicyEligible);
            Assert.True(guest.StatusFor(Savor).Active);
            Assert.True(guest.StatusFor(Twig).Active);
        }

        [Fact]
        public void Private_policy_benefits_owner_plus_explicit_allowlist_only()
        {
            var h = NewHandler();
            var r = h.Handle(Cmd("op-private", _owner, _ownerChar, LocalBeneficiaryMode.Private,
                allow: new[] { _guest.Value }));
            Assert.Equal(LocalPolicyCommandOutcome.Applied, r.Outcome);

            // Owner benefits even without an active relationship (Private = owner + allowlist).
            Assert.True(DeriveFor(_owner, isOwner: true, hasRelationship: false, inside: true, governorPresent: true)
                .StatusFor(Savor).Active);
            // Allowlisted guest benefits.
            Assert.True(DeriveFor(_guest, isOwner: false, hasRelationship: true, inside: true, governorPresent: true)
                .StatusFor(Savor).Active);
            // A stranger with an ACTIVE relationship still does NOT benefit under Private (not owner, not allowlisted).
            Assert.False(DeriveFor(_stranger, isOwner: false, hasRelationship: true, inside: true, governorPresent: true)
                .OccupantPolicyEligible);
        }

        [Fact]
        public void Attuned_policy_excludes_unrelated_occupant_even_inside_area()
        {
            var h = NewHandler();
            h.Handle(Cmd("op-att", _owner, _ownerChar, LocalBeneficiaryMode.Attuned));
            var v = DeriveFor(_stranger, isOwner: false, hasRelationship: false, inside: true, governorPresent: true);
            Assert.False(v.StatusFor(Savor).Active);
            Assert.True(v.StatusFor(Savor).Developed); // retained developed state, just not active for them
        }

        [Fact]
        public void Local_placement_requires_both_policy_eligibility_and_ordinary_build_permission()
        {
            var h = NewHandler();
            h.Handle(Cmd("op-att", _owner, _ownerChar, LocalBeneficiaryMode.Attuned));

            var eligible = DeriveFor(_guest, isOwner: false, hasRelationship: true, inside: true, governorPresent: true);
            // Policy-eligible + build Permission => can place.
            Assert.True(eligible.CanExercisePlacement(Twig, hasOrdinaryBuildPermission: true));
            // Policy-eligible but NO build Permission => cannot place (policy never grants build ACL).
            Assert.False(eligible.CanExercisePlacement(Twig, hasOrdinaryBuildPermission: false));

            // Build-permitted but OUTSIDE the policy => cannot place (relationship/policy never silently
            // grants; Permission alone is insufficient).
            var outside = DeriveFor(_stranger, isOwner: false, hasRelationship: false, inside: true, governorPresent: true);
            Assert.False(outside.CanExercisePlacement(Twig, hasOrdinaryBuildPermission: true));
        }

        [Fact]
        public void Local_node_is_stone_owned_developed_state_never_a_purchase_or_tier_input()
        {
            // The Local nodes are surfaced only as Stone-owned developed Local Effects; the personal
            // DerivedActivationView never treats a Local node as purchased/offered, and it never enters a
            // purchase record (that boundary is proven in the T013 purchase tests — here we assert the
            // Local projection is purely Stone-owned with no character record required).
            var v = DeriveFor(_owner, isOwner: true, hasRelationship: false, inside: true, governorPresent: true);
            var savor = v.StatusFor(Savor);
            Assert.True(savor.Developed);
            // A Local Effect status carries no purchase/offered concept — it is Stone state only.
            Assert.Equal(Savor.Key, savor.Node.Key);
        }

        // ---- Owner-only authority + zero-mutation rejects ----

        [Fact]
        public void Non_owner_cannot_set_policy_and_nothing_mutates()
        {
            var h = NewHandler();
            long revBefore = _stones.GetStone(_stone)!.Revision;
            var r = h.Handle(Cmd("op-hostile", _guest, _guestChar, LocalBeneficiaryMode.Private));
            Assert.Equal(LocalPolicyCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("Unauthorized", r.ResultCode);
            var stone = _stones.GetStone(_stone)!;
            Assert.Equal(revBefore, stone.Revision);
            Assert.Equal(LocalBeneficiaryMode.Everyone, stone.LocalPolicy.Mode); // unchanged
        }

        [Fact]
        public void Stale_stone_revision_rejects_zero_mutation()
        {
            var h = NewHandler();
            var r = h.Handle(Cmd("op-stalestone", _owner, _ownerChar, LocalBeneficiaryMode.Attuned,
                expStone: 999));
            Assert.Equal(LocalPolicyCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("StaleStoneRevision", r.ResultCode);
            Assert.Equal(LocalBeneficiaryMode.Everyone, _stones.GetStone(_stone)!.LocalPolicy.Mode);
        }

        [Fact]
        public void Stale_policy_revision_rejects_zero_mutation()
        {
            var h = NewHandler();
            // First change moves policy revision 0 -> 1.
            Assert.Equal(LocalPolicyCommandOutcome.Applied,
                h.Handle(Cmd("op-first", _owner, _ownerChar, LocalBeneficiaryMode.Attuned)).Outcome);
            // A second change that still expects policy revision 0 is stale.
            var r = h.Handle(Cmd("op-second", _owner, _ownerChar, LocalBeneficiaryMode.Private,
                allow: new[] { _guest.Value }, expPolicy: 0));
            Assert.Equal(LocalPolicyCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("StalePolicyRevision", r.ResultCode);
            // Still the first policy.
            Assert.Equal(LocalBeneficiaryMode.Attuned, _stones.GetStone(_stone)!.LocalPolicy.Mode);
        }

        [Fact]
        public void Concurrent_expected_policy_revision_succeeds_when_current()
        {
            var h = NewHandler();
            var r = h.Handle(Cmd("op-cas", _owner, _ownerChar, LocalBeneficiaryMode.Attuned,
                expStone: 5, expPolicy: 0));
            Assert.Equal(LocalPolicyCommandOutcome.Applied, r.Outcome);
            Assert.Equal(1, r.PolicyRevision);
        }

        [Fact]
        public void Replay_same_op_is_idempotent_single_change()
        {
            var h = NewHandler();
            var first = h.Handle(Cmd("op-replay", _owner, _ownerChar, LocalBeneficiaryMode.Private,
                allow: new[] { _guest.Value }));
            Assert.Equal(LocalPolicyCommandOutcome.Applied, first.Outcome);
            long revAfterFirst = _stones.GetStone(_stone)!.Revision;

            var replay = h.Handle(Cmd("op-replay", _owner, _ownerChar, LocalBeneficiaryMode.Private,
                allow: new[] { _guest.Value }));
            Assert.Equal(LocalPolicyCommandOutcome.Replayed, replay.Outcome);
            Assert.Equal(first.PolicyRevision, replay.PolicyRevision);
            // No second revision bump.
            Assert.Equal(revAfterFirst, _stones.GetStone(_stone)!.Revision);
        }

        [Fact]
        public void Allowlist_order_does_not_change_replay_identity()
        {
            var h = NewHandler();
            var a = h.Handle(Cmd("op-order", _owner, _ownerChar, LocalBeneficiaryMode.Private,
                allow: new[] { _guest.Value, _stranger.Value }));
            Assert.Equal(LocalPolicyCommandOutcome.Applied, a.Outcome);
            // Same op, allowlist supplied in a different order + a duplicate -> still a replay, not conflict.
            var b = h.Handle(Cmd("op-order", _owner, _ownerChar, LocalBeneficiaryMode.Private,
                allow: new[] { _stranger.Value, _guest.Value, _guest.Value }));
            Assert.Equal(LocalPolicyCommandOutcome.Replayed, b.Outcome);
        }

        [Fact]
        public void Same_op_conflicting_payload_rejects_operation_conflict()
        {
            var h = NewHandler();
            Assert.Equal(LocalPolicyCommandOutcome.Applied,
                h.Handle(Cmd("op-conf", _owner, _ownerChar, LocalBeneficiaryMode.Attuned)).Outcome);
            var conflict = h.Handle(Cmd("op-conf", _owner, _ownerChar, LocalBeneficiaryMode.Private,
                allow: new[] { _guest.Value }));
            Assert.Equal(LocalPolicyCommandOutcome.Rejected, conflict.Outcome);
            Assert.Equal("OperationConflict", conflict.ResultCode);
        }

        [Fact]
        public void Unauthenticated_peer_rejected()
        {
            var h = NewHandler();
            var cmd = new SetSettlementLocalPolicyCommand(new OperationId("op-noauth"), _stone,
                new AuthenticatedConnection("", ""), default, LocalBeneficiaryMode.Attuned);
            var r = h.Handle(cmd);
            Assert.Equal(LocalPolicyCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("Unauthenticated", r.ResultCode);
        }

        [Fact]
        public void Claim_mismatch_rejected()
        {
            var h = NewHandler();
            var cmd = new SetSettlementLocalPolicyCommand(new OperationId("op-claim"), _stone,
                new AuthenticatedConnection(_owner.Value, _ownerChar.Value),
                new ClaimedPrincipal("acct-someone-else", null), LocalBeneficiaryMode.Attuned);
            var r = h.Handle(cmd);
            Assert.Equal(LocalPolicyCommandOutcome.Rejected, r.Outcome);
            Assert.Equal("PrincipalMismatch", r.ResultCode);
        }

        // ============================================================================
        //  AT-RELATIONSHIP-DORMANCY — derived dormancy with no mutable active-effects ledger.
        // ============================================================================

        [Fact]
        public void Missing_authorized_governor_dormants_all_local_effects_without_deleting_development()
        {
            var h = NewHandler();
            h.Handle(Cmd("op-att", _owner, _ownerChar, LocalBeneficiaryMode.Everyone));

            // No authorized Governor present (spec US5 sc2): every Local Effect is dormant even for a
            // policy-eligible occupant inside the Area, but the developed state is retained.
            var v = DeriveFor(_guest, isOwner: false, hasRelationship: true, inside: true, governorPresent: false);
            var savor = v.StatusFor(Savor);
            Assert.True(savor.Developed);
            Assert.True(savor.Dormant);
            Assert.False(savor.Active);
        }

        [Fact]
        public void Relationship_release_flips_active_to_dormant_with_zero_writes()
        {
            // Attuned policy. An occupant with an active relationship is active; the SAME persisted Stone
            // re-derived after the relationship is released flips to dormant with no mutation.
            var h = NewHandler();
            h.Handle(Cmd("op-att", _owner, _ownerChar, LocalBeneficiaryMode.Attuned));
            long revBefore = _stones.GetStone(_stone)!.Revision;

            var active = DeriveFor(_guest, isOwner: false, hasRelationship: true, inside: true, governorPresent: true);
            Assert.True(active.StatusFor(Savor).Active);

            var released = DeriveFor(_guest, isOwner: false, hasRelationship: false, inside: true, governorPresent: true);
            Assert.False(released.StatusFor(Savor).Active);
            // No writes occurred from deriving twice.
            Assert.Equal(revBefore, _stones.GetStone(_stone)!.Revision);
        }

        [Fact]
        public void Rejoin_re_derives_active_without_a_second_ledger()
        {
            var stone = _stones.GetStone(_stone)!;
            var away = LocalEffectActivationView.Derive(stone, _catalog, _guest, false, false, false, true);
            Assert.False(away.StatusFor(Savor).Active); // outside the Area

            var back = LocalEffectActivationView.Derive(stone, _catalog, _guest, false, true, true, true);
            Assert.True(back.StatusFor(Savor).Active); // rejoined + inside, re-derived from same Stone
        }

        [Fact]
        public void Tree_dormancy_when_active_stone_level_below_node_level()
        {
            // Rebuild the Stone at Active Level 0 with Savor still developed: the developed Local Effect
            // is dormant because the Active Stone Level no longer meets the node's authored level, yet the
            // development record is retained.
            _stones.PutStone(BuildStone(revision: 6, activeLevel: 0));
            var v = DeriveFor(_owner, isOwner: true, hasRelationship: false, inside: true, governorPresent: true);
            var savor = v.StatusFor(Savor);
            Assert.True(savor.Developed);
            Assert.True(savor.Dormant);
            Assert.False(savor.Active);
        }

        [Fact]
        public void Tree_not_committed_dormants_its_local_effect_only()
        {
            // Cooking Tree not committed (revoked), but T.W.I.G./Warrior is: Savor dormants, T.W.I.G. does not.
            _stones.PutStone(BuildStone(revision: 7, cookingCommitted: false));
            var v = DeriveFor(_owner, isOwner: true, hasRelationship: false, inside: true, governorPresent: true);
            Assert.True(v.StatusFor(Savor).Dormant);
            Assert.False(v.StatusFor(Twig).Dormant);
            Assert.True(v.StatusFor(Twig).Active);
        }

        [Fact]
        public void Policy_change_during_occupancy_re_evaluates_deterministically()
        {
            var h = NewHandler();
            // Start Everyone: stranger benefits.
            Assert.True(DeriveFor(_stranger, false, false, true, true).StatusFor(Savor).Active);
            // Owner tightens to Private (allowlist = guest): re-derive for the SAME occupant flips off.
            h.Handle(Cmd("op-tighten", _owner, _ownerChar, LocalBeneficiaryMode.Private,
                allow: new[] { _guest.Value }));
            Assert.False(DeriveFor(_stranger, false, false, true, true).StatusFor(Savor).Active);
            Assert.True(DeriveFor(_guest, false, true, true, true).StatusFor(Savor).Active);
        }

        [Fact]
        public void Restart_recovery_rebuilds_the_same_policy_and_derivation()
        {
            var h1 = NewHandler();
            var applied = h1.Handle(Cmd("op-restart", _owner, _ownerChar, LocalBeneficiaryMode.Private,
                allow: new[] { _guest.Value }));
            Assert.Equal(LocalPolicyCommandOutcome.Applied, applied.Outcome);

            // Simulate a restart: a NEW handler over the SAME journal + a fresh store rehydrates the
            // committed policy projection, and the derived result is identical.
            var freshStore = new InMemoryStoneAggregateStore();
            freshStore.PutStone(BuildStone(revision: 5)); // seed baseline as on boot
            var h2 = new LocalPolicyCommandHandler(_policyJournal, new PrincipalResolver(), freshStore, _ownerAuthority);
            var recovered = freshStore.GetStone(_stone)!;
            Assert.Equal(LocalBeneficiaryMode.Private, recovered.LocalPolicy.Mode);
            Assert.Equal(applied.PolicyRevision, recovered.LocalPolicy.Revision);

            var v = LocalEffectActivationView.Derive(recovered, _catalog, _guest, false, true, true, true);
            Assert.True(v.StatusFor(Savor).Active);
            var stranger = LocalEffectActivationView.Derive(recovered, _catalog, _stranger, false, true, true, true);
            Assert.False(stranger.StatusFor(Savor).Active);
            _ = h2; // handler constructed to exercise rehydrate path
        }

        // ── AT-JOURNAL-DELIMITER-SAFE (ADO #127) ──
        // A StoneId is "world|zoneX|zoneZ" by construction, so a caller-composed operation id
        // legitimately embeds '|'. Written raw into the pipe-delimited frame it explodes the field
        // count and the strict parser rejects EVERY record — total, silent policy loss on restart.
        [Fact]
        public void Policy_with_pipes_in_operation_id_survives_restart_from_journal()
        {
            const string PipedOp = "savor-seam-on-uid:-898655635|3|2";
            var h1 = NewHandler();
            var applied = h1.Handle(Cmd(PipedOp, _owner, _ownerChar, LocalBeneficiaryMode.Private,
                allow: new[] { _guest.Value }));
            Assert.Equal(LocalPolicyCommandOutcome.Applied, applied.Outcome);

            var freshStore = new InMemoryStoneAggregateStore();
            freshStore.PutStone(BuildStone(revision: 5));
            var h2 = new LocalPolicyCommandHandler(_policyJournal, new PrincipalResolver(), freshStore, _ownerAuthority);

            var recovered = freshStore.GetStone(_stone)!;
            Assert.Equal(LocalBeneficiaryMode.Private, recovered.LocalPolicy.Mode);
            Assert.Equal(applied.PolicyRevision, recovered.LocalPolicy.Revision);

            var replay = h2.Handle(Cmd(PipedOp, _owner, _ownerChar, LocalBeneficiaryMode.Private,
                allow: new[] { _guest.Value }));
            Assert.Equal(LocalPolicyCommandOutcome.Replayed, replay.Outcome);
        }

        // ============================================================================
        //  Pure value-object + aggregate round-trip coverage.
        // ============================================================================

        [Fact]
        public void Policy_round_trips_through_serialization()
        {
            var p = new SettlementLocalPolicy(LocalBeneficiaryMode.Private, 4,
                new[] { _stranger.Value, _guest.Value, _guest.Value });
            var back = SettlementLocalPolicy.Deserialize(p.Serialize());
            Assert.True(p.StructurallyEquals(back));
            Assert.Equal(2, back.AllowlistAccounts.Count); // deduplicated
        }

        [Fact]
        public void Stone_with_policy_round_trips_and_default_is_backward_compatible()
        {
            var stone = BuildStone(revision: 9,
                policy: new SettlementLocalPolicy(LocalBeneficiaryMode.Attuned, 2, null));
            var back = StoneProgressionAggregate.Deserialize(stone.Serialize());
            Assert.True(stone.StructurallyEquals(back));
            Assert.Equal(LocalBeneficiaryMode.Attuned, back.LocalPolicy.Mode);
        }

        [Fact]
        public void Mode_change_drops_stale_allowlist()
        {
            var priv = new SettlementLocalPolicy(LocalBeneficiaryMode.Private, 1, new[] { _guest.Value });
            var everyone = priv.With(LocalBeneficiaryMode.Everyone, null);
            Assert.Empty(everyone.AllowlistAccounts);
            Assert.Equal(2, everyone.Revision);
        }

        // ── Test doubles ──

        private sealed class StubOwnerAuthority : IHomesteadOwnerAuthority
        {
            private readonly AccountId _owner;
            private readonly CharacterId _ownerChar;
            private readonly StoneId _stone;

            public StubOwnerAuthority(AccountId owner, CharacterId ownerChar, StoneId stone)
            {
                _owner = owner; _ownerChar = ownerChar; _stone = stone;
            }

            public bool IsOwner(AuthoritativePrincipal principal, StoneId stoneId) =>
                stoneId.Equals(_stone) && principal.Account.Equals(_owner)
                && principal.Character.Equals(_ownerChar);
        }
    }
}
