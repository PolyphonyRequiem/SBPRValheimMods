// ============================================================================
//  T035 (Tracer 9) — HOSTILE remote contract tests for the transport-neutral
//  progression command/read seam.
// ----------------------------------------------------------------------------
//  Two named acceptance criteria are proven here, against the SHIPPED,
//  receipt-backed handlers composed exactly as a live server composes them:
//
//   AT-REMOTE-SHAPED — one authorized progression SELECTION is submitted and
//     committed through ProgressionCommandEndpoint with NOTHING in its input
//     naming a position, a Stone Area, a nearby panel, or a transport. The
//     compact GetRelationshipPortfolio read is the non-proximate first hop that
//     lets a caller with no Stone in front of it find one. FR-025 revalidation
//     (authority, relationship, balances, requirements, content version,
//     revisions, replay) still fires on every submission — the handlers do it.
//
//   AT-LOCAL-EVIDENCE-NOT-REMOTE — a client message can NEVER reach the
//     server-observed evidence adapters. Placement / presence / cooking /
//     crafting / combat evidence is rejected at the routing wall BEFORE any
//     handler is consulted, so no receipt, no journal entry, and no balance
//     movement is possible from a remote submission.
//
//  Written adversarially: a client that lies about its position (it cannot —
//  there is no position field), about its principal (claim substitution), about
//  its evidence (evidence-command submission, direct-handler-shape submission),
//  about revisions (stale CAS), and about operation identity (replay + op-id
//  collision with a different payload). Every attempt must return the prior
//  recorded result or reject with zero gameplay mutation.
//
//  The joined-client half of T035 (an actual second client issuing this away
//  from the Stone) is IN-WORLD work and is NOT proven here.
// ============================================================================

using System.IO;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Queries;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Features.Progression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimRemoteShapedCommandTests : System.IDisposable
    {
        private readonly string _dir;
        private readonly WorldId _world = new WorldId("uid:t035-remote");
        private readonly StoneId _stone;
        private readonly StoneId _otherStone;

        private readonly AccountId _gov = new AccountId("acct-gov");
        private readonly CharacterId _govChar = new CharacterId("char-gov");
        private readonly AccountId _hostile = new AccountId("acct-hostile");
        private readonly CharacterId _hostileChar = new CharacterId("char-hostile");

        private static readonly VersionedId RefinedWorkshop = new VersionedId("RefinedWorkshop", 1);
        private static readonly VersionedId Masterwork = new VersionedId("Masterwork", 1);
        private static readonly VersionedId Crafting = HomesteadProgressionCatalog.CraftingTree;

        private InMemoryStoneAggregateStore _stones = null!;
        private InMemoryCharacterAggregateStore _characters = null!;
        private InMemoryAccountStoneAuthorityStore _authority = null!;

        public NiflheimRemoteShapedCommandTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "niflheim-t035-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _stone = StoneId.FromHostZone(_world, 3, 4);
            _otherStone = StoneId.FromHostZone(_world, 9, 9);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        }

        // ── Composition: the SAME live composition root a server uses ────────────────────────────────

        private LocalProgressionServer NewServer()
        {
            _stones = new InMemoryStoneAggregateStore();
            _characters = new InMemoryCharacterAggregateStore();
            _authority = new InMemoryAccountStoneAuthorityStore();
            _characters.PutCharacter(Governor());
            _authority.ApplyAuthorityProjection("seed-bond", BondIndex());

            var relationships = new RelationshipCommandHandler(
                Path.Combine(_dir, "relationships.journal"), new PrincipalResolver(), _characters, _authority,
                new FixedFamilyResolver(), new AllowHomesteadBondPolicy(), AlwaysAtStoneProximity.Instance,
                null, _world,
                new ProductScope("SBPR.Trailborne"));

            return LocalProgressionServer.Create(
                _dir, _stones, _characters, _authority, relationships,
                new FixedFamilyResolver(), new AllowGovernorAuthority(), new AllowDevelopmentAuthority(),
                new CommittedGovernorOwnerAuthority(new GovernorPresenceResolver(_characters, _authority)));
        }

        private CharacterProgressionAggregate Governor() =>
            new CharacterProgressionAggregate(_gov, _govChar, "t035/trailborne",
                revision: 3, bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[]
                {
                    new CharacterStoneRecord(_stone, 0, 0, 0, purchases: null,
                        relationships: new[]
                        {
                            new RelationshipRecord("rel-bond-gov", RelationshipKind.Bond,
                                RelationshipStatus.Active, "Homestead:All", "Governor",
                                "relreceipt:seed-bond", string.Empty)
                        }),
                    // A SECOND Stone, with a RELEASED attunement — proves the portfolio reports both
                    // Stones and honest released status, which is what a remote UI needs.
                    new CharacterStoneRecord(_otherStone, 0, 0, 0, purchases: null,
                        relationships: new[]
                        {
                            new RelationshipRecord("rel-att-old", RelationshipKind.Attunement,
                                RelationshipStatus.Released, string.Empty, string.Empty,
                                "relreceipt:seed-att", "relreceipt:released")
                        })
                });

        private AccountStoneAuthorityIndex BondIndex() =>
            AccountStoneAuthorityIndex.Vacant(_gov, _stone).WithReservationAdded(
                new AuthorityReservation(_govChar, RelationshipKind.Bond, "rel-bond-gov",
                    "relreceipt:seed-bond"), 1);

        private void SeedBareStone(StoneId stoneId)
        {
            var catalog = new HomesteadProgressionCatalog();
            _stones.PutStone(new StoneProgressionAggregate(
                stoneId, revision: 1, historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: catalog.FoundationalTree, foundationalCatalog: catalog.FoundationalCatalog,
                contentRegistryVersion: catalog.ContentRegistryVersion,
                createdProvenance: "t035-seed", updatedProvenance: "t035-seed",
                mirroredStoneAp: 0, lastAppliedReceiptId: "t035-seed",
                committedTrees: null, nodeDevelopment: null));
        }

        private static AuthenticatedConnection Conn(AccountId account, CharacterId character) =>
            new AuthenticatedConnection(account.Value, character.Value);

        private ProgressionCommandEnvelope Commit(string opId, AccountId a, CharacterId c,
            ClaimedPrincipal claim = default, long? expectedStoneRevision = null) =>
            new ProgressionCommandEnvelope(
                ProgressionCommandType.CommitTreeToFacet, new OperationId(opId), _stone,
                Conn(a, c), claim, HomesteadProgressionCatalog.CurrentContentRegistryVersion,
                new ProgressionCommandPayload(
                    facetId: HomesteadProgressionCatalog.ProfessionFacetId, tree: Crafting,
                    paletteVersion: 1),
                expectedStoneRevision);

        /// <summary>Credit BP the ONLY way it can be credited — through the server-observed aligned-activity
        /// adapter/handler. This is deliberately NOT routed through the endpoint: it is evidence.</summary>
        private void CreditBpServerSide(LocalProgressionServer server, int amount, string opId)
        {
            var result = server.Activities.Handle(new RecordAlignedActivityCommand(
                new OperationId(opId), _stone, Conn(_gov, _govChar), default,
                Crafting, amount, evidenceDigest: opId + "-ev"));
            Assert.NotEqual(ActivityCommandOutcome.Rejected, result.Outcome);
        }

        private static bool NodeDeveloped(StoneProgressionAggregate? stone, VersionedId node)
        {
            if (stone == null) return false;
            foreach (var d in stone.NodeDevelopment)
                if (d.Node.Key == node.Key && d.Developed) return true;
            return false;
        }

        private static bool TreeCommitted(StoneProgressionAggregate? stone, VersionedId tree)
        {
            if (stone == null) return false;
            foreach (var c in stone.CommittedTrees)
                if (c.Tree.Key == tree.Key) return true;
            return false;
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        //  AT-REMOTE-SHAPED — an authorized selection commits through the shared seam, non-proximately
        // ════════════════════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void Remote_selection_commits_through_the_shared_seam_without_any_proximity_input()
        {
            var server = NewServer();
            SeedBareStone(_stone);
            var endpoint = ProgressionCommandEndpoint.ForServer(server);

            // Step 1 — the NON-PROXIMATE first hop: the caller has no Stone in front of it and asks which
            // Stones it is related to. The portfolio hands back the link key + the revisions to submit
            // against. Nothing about position is asked or answered.
            var portfolio = endpoint.Portfolio(Conn(_gov, _govChar), default);
            Assert.True(portfolio.Bound);
            Assert.Equal("Applied", portfolio.ResultCode);
            RelationshipPortfolioEntry? bonded = null;
            foreach (var e in portfolio.Entries)
                if (e.StoneId.Equals(_stone) && e.Kind == RelationshipKind.Bond) bonded = e;
            Assert.NotNull(bonded);
            Assert.Equal(RelationshipStatus.Active, bonded!.Status);
            Assert.True(bonded.AuthorityIndexActive);
            Assert.True(bonded.StoneResolved);

            // Step 2 — the SELECTION itself, submitted with the revisions the portfolio reported.
            var commit = endpoint.Submit(Commit("t035-commit", _gov, _govChar,
                expectedStoneRevision: bonded.StoneRevision));
            Assert.Equal(CommandOutcome.Applied, commit.Outcome);
            Assert.True(TreeCommitted(_stones.GetStone(_stone), Crafting));

            // Step 3 — spend server-credited BP on a node through the same seam. The BP itself came from
            // server-observed activity (credited outside the endpoint); the SELECTION is remote.
            CreditBpServerSide(server, 200, "t035-bp");
            var def = server.Catalog.TryResolveNode(RefinedWorkshop)!;
            int cost = def.Pricing.DevelopmentBpPrice ?? 0;
            Assert.True(cost > 0);

            var develop = endpoint.Submit(new ProgressionCommandEnvelope(
                ProgressionCommandType.ApplyBPToNode, new OperationId("t035-dev"), _stone,
                Conn(_gov, _govChar), default, HomesteadProgressionCatalog.CurrentContentRegistryVersion,
                new ProgressionCommandPayload(tree: Crafting, node: RefinedWorkshop, bpAmount: cost)));

            Assert.Equal(CommandOutcome.Applied, develop.Outcome);
            Assert.True(NodeDeveloped(_stones.GetStone(_stone), RefinedWorkshop));

            // A committed operation publishes the BOUNDED invalidation event (ids + revisions + code only).
            Assert.NotNull(develop.Notification);
            Assert.Equal(_stone.Value, develop.Notification!.Value.StoneId.Value);
            Assert.Equal(ProgressionCommandType.ApplyBPToNode, develop.Notification.Value.CommandType);
        }

        [Fact]
        public void Envelope_carries_no_position_area_or_panel_field()
        {
            // Structural proof that the seam is transport-neutral: a client CANNOT lie about where it is
            // standing because there is nowhere on the envelope or payload to say it. Reflection over the
            // public surface is the assertion — a future field named for proximity fails this test.
            foreach (var t in new[] { typeof(ProgressionCommandEnvelope), typeof(ProgressionCommandPayload) })
            {
                foreach (var p in t.GetProperties())
                {
                    string n = p.Name.ToLowerInvariant();
                    Assert.DoesNotContain("position", n);
                    Assert.DoesNotContain("inside", n);
                    Assert.DoesNotContain("area", n);
                    Assert.DoesNotContain("proximity", n);
                    Assert.DoesNotContain("panel", n);
                    Assert.DoesNotContain("distance", n);
                }
            }
        }

        [Fact]
        public void Portfolio_is_compact_and_never_carries_balances_or_ledgers()
        {
            // contracts.md: "Do not broadcast entire character ledgers." A portfolio ROW is a link, not a
            // read model — no AP/BP, no purchases, no node rows, no policy allowlist.
            foreach (var p in typeof(RelationshipPortfolioEntry).GetProperties())
            {
                string n = p.Name.ToLowerInvariant();
                Assert.DoesNotContain("balance", n);
                Assert.DoesNotContain("purchase", n);
                Assert.DoesNotContain("node", n);
                Assert.DoesNotContain("allowlist", n);
                Assert.NotEqual("personalap", n);
                Assert.NotEqual("cumulativeap", n);
                Assert.NotEqual("personalbp", n);
            }
        }

        [Fact]
        public void Portfolio_reports_every_related_stone_including_released_relationships()
        {
            var server = NewServer();
            SeedBareStone(_stone);
            var endpoint = ProgressionCommandEndpoint.ForServer(server);

            var portfolio = endpoint.Portfolio(Conn(_gov, _govChar), default);
            Assert.Equal(2, portfolio.Entries.Count);

            RelationshipPortfolioEntry? released = null;
            foreach (var e in portfolio.Entries)
                if (e.StoneId.Equals(_otherStone)) released = e;
            Assert.NotNull(released);
            Assert.Equal(RelationshipStatus.Released, released!.Status);
            Assert.False(released.AuthorityIndexActive);
            // The other Stone has no aggregate on this server — the link is honestly unresolvable rather
            // than a fabricated zeroed Stone.
            Assert.False(released.StoneResolved);
            Assert.Equal(0, released.StoneRevision);
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        //  AT-LOCAL-EVIDENCE-NOT-REMOTE — evidence adapters are unreachable from a client message
        // ════════════════════════════════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData(ProgressionCommandType.RecordFoundationalPlacement)]
        [InlineData(ProgressionCommandType.RecordAlignedActivity)]
        public void Evidence_commands_are_rejected_at_the_routing_wall_with_zero_mutation(
            ProgressionCommandType evidenceCommand)
        {
            var server = NewServer();
            SeedBareStone(_stone);
            var endpoint = ProgressionCommandEndpoint.ForServer(server);

            long revBefore = _stones.GetStone(_stone)!.Revision;
            long charRevBefore = _characters.GetCharacter(_gov, _govChar)!.Revision;

            // A fully-authenticated, fully-authorized Governor submitting evidence is STILL refused: the
            // wall is the command's CLASSIFICATION, not the caller's authority. Placement, presence,
            // cooking, crafting, and combat evidence are all attributed by trusted server code only.
            var attempt = endpoint.Submit(new ProgressionCommandEnvelope(
                evidenceCommand, new OperationId("t035-forge"), _stone,
                Conn(_gov, _govChar), default, HomesteadProgressionCatalog.CurrentContentRegistryVersion,
                new ProgressionCommandPayload(tree: Crafting, node: RefinedWorkshop, bpAmount: 9999)));

            Assert.True(attempt.Rejected);
            Assert.Equal(ProgressionCommandEndpoint.EvidenceNotRemotelyInvocable, attempt.ResultCode);
            Assert.Equal(string.Empty, attempt.ReceiptId);      // a rejection is not a receipt-bearing mutation
            Assert.Null(attempt.Notification);                   // and never invalidates a client's read model

            // Nothing moved: no Stone revision, no character revision, no BP, no development.
            Assert.Equal(revBefore, _stones.GetStone(_stone)!.Revision);
            Assert.Equal(charRevBefore, _characters.GetCharacter(_gov, _govChar)!.Revision);
            Assert.False(NodeDeveloped(_stones.GetStone(_stone), RefinedWorkshop));
        }

        [Fact]
        public void Evidence_rejection_happens_even_with_no_handlers_composed_at_all()
        {
            // The wall is not a side effect of a missing handler: an endpoint with EVERY handler composed
            // still refuses, and an endpoint with NONE composed refuses with the SAME evidence code rather
            // than HandlerUnavailable. Reachability is checked first, always.
            var bare = new ProgressionCommandEndpoint();
            var attempt = bare.Submit(new ProgressionCommandEnvelope(
                ProgressionCommandType.RecordFoundationalPlacement, new OperationId("op"), _stone,
                Conn(_gov, _govChar), default, 1, new ProgressionCommandPayload()));
            Assert.Equal(ProgressionCommandEndpoint.EvidenceNotRemotelyInvocable, attempt.ResultCode);
        }

        [Fact]
        public void Every_command_type_is_explicitly_classified_and_unknown_fails_closed()
        {
            // Every KNOWN type carries a real classification — only Unknown may be Rejected.
            foreach (var type in ProgressionCommandRouting.AllCommandTypes)
            {
                var reach = ProgressionCommandRouting.Reachability(type);
                if (type == ProgressionCommandType.Unknown)
                    Assert.Equal(ProgressionCommandReachability.Rejected, reach);
                else
                    Assert.NotEqual(ProgressionCommandReachability.Rejected, reach);
            }

            // A new command type added without classification must NOT become remotely invocable by
            // default. The routing table's default arm is Rejected.
            Assert.Equal(ProgressionCommandReachability.Rejected,
                ProgressionCommandRouting.Reachability(ProgressionCommandType.Unknown));
            Assert.Equal(ProgressionCommandReachability.Rejected,
                ProgressionCommandRouting.Reachability((ProgressionCommandType)987654));

            var endpoint = new ProgressionCommandEndpoint();
            var attempt = endpoint.Submit(new ProgressionCommandEnvelope(
                (ProgressionCommandType)987654, new OperationId("op"), _stone,
                Conn(_gov, _govChar), default, 1, new ProgressionCommandPayload()));
            Assert.Equal(ProgressionCommandEndpoint.UnknownCommand, attempt.ResultCode);
        }

        [Theory]
        [InlineData(ProgressionCommandType.CreateBond)]
        [InlineData(ProgressionCommandType.CreateAttunement)]
        [InlineData(ProgressionCommandType.ReleaseRelationship)]
        public void Relationship_formation_is_not_remotely_reachable_here(ProgressionCommandType type)
        {
            // Bond/Attunement FORMATION is proximate and belongs to card #138. This card does not widen
            // the remote surface to cover it.
            var server = NewServer();
            var endpoint = ProgressionCommandEndpoint.ForServer(server);
            var attempt = endpoint.Submit(new ProgressionCommandEnvelope(
                type, new OperationId("op"), _stone, Conn(_gov, _govChar), default, 1,
                new ProgressionCommandPayload()));
            Assert.True(attempt.Rejected);
            Assert.Equal(ProgressionCommandEndpoint.NotRemotelyInvocable, attempt.ResultCode);
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        //  Hostile client: identity, revision, replay, authority
        // ════════════════════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void Claimed_principal_substitution_is_rejected_with_no_mutation()
        {
            var server = NewServer();
            SeedBareStone(_stone);
            var endpoint = ProgressionCommandEndpoint.ForServer(server);
            long revBefore = _stones.GetStone(_stone)!.Revision;

            // The hostile client is authenticated as ITSELF but claims the Governor in the payload.
            var attempt = endpoint.Submit(Commit("t035-sub", _hostile, _hostileChar,
                new ClaimedPrincipal(_gov.Value, _govChar.Value)));

            Assert.True(attempt.Rejected);
            Assert.Equal("PrincipalMismatch", attempt.ResultCode);
            Assert.Equal(revBefore, _stones.GetStone(_stone)!.Revision);
            Assert.False(TreeCommitted(_stones.GetStone(_stone), Crafting));
        }

        [Fact]
        public void Unauthenticated_submission_is_rejected_with_no_mutation()
        {
            var server = NewServer();
            SeedBareStone(_stone);
            var endpoint = ProgressionCommandEndpoint.ForServer(server);
            long revBefore = _stones.GetStone(_stone)!.Revision;

            var attempt = endpoint.Submit(Commit("t035-anon", new AccountId(string.Empty), new CharacterId(string.Empty)));
            Assert.True(attempt.Rejected);
            Assert.Equal("Unauthenticated", attempt.ResultCode);
            Assert.Equal(revBefore, _stones.GetStone(_stone)!.Revision);

            // The read is fail-closed too: an unauthenticated caller sees an EMPTY portfolio, never a list.
            var portfolio = endpoint.Portfolio(
                new AuthenticatedConnection(string.Empty, string.Empty), default);
            Assert.False(portfolio.Bound);
            Assert.Equal("Unauthenticated", portfolio.ResultCode);
            Assert.Empty(portfolio.Entries);
        }

        [Fact]
        public void Portfolio_never_leaks_another_principals_stones()
        {
            var server = NewServer();
            SeedBareStone(_stone);
            var endpoint = ProgressionCommandEndpoint.ForServer(server);

            // A hostile account with no character aggregate sees nothing — never the Governor's rows.
            var hostile = endpoint.Portfolio(Conn(_hostile, _hostileChar), default);
            Assert.True(hostile.Bound);
            Assert.Equal("CharacterNotFound", hostile.ResultCode);
            Assert.Empty(hostile.Entries);

            // And a claim substitution on the READ is rejected exactly like on a command.
            var forged = endpoint.Portfolio(Conn(_hostile, _hostileChar),
                new ClaimedPrincipal(_gov.Value, _govChar.Value));
            Assert.False(forged.Bound);
            Assert.Equal("PrincipalMismatch", forged.ResultCode);
            Assert.Empty(forged.Entries);
        }

        [Fact]
        public void Unauthorized_remote_selection_is_rejected_by_the_real_authority_gate()
        {
            var server = NewServer();
            SeedBareStone(_stone);
            var endpoint = ProgressionCommandEndpoint.ForServer(server);

            // The hostile principal is authenticated and consistent — it simply has no Bond here. The
            // SHIPPED handler's authority gate fires; the endpoint contributed no check of its own.
            var attempt = endpoint.Submit(Commit("t035-unauth", _hostile, _hostileChar));
            Assert.True(attempt.Rejected);
            Assert.NotEqual("Applied", attempt.ResultCode);
            Assert.False(TreeCommitted(_stones.GetStone(_stone), Crafting));
        }

        [Fact]
        public void Stale_expected_revision_is_rejected_with_current_revision_and_no_mutation()
        {
            var server = NewServer();
            SeedBareStone(_stone);
            var endpoint = ProgressionCommandEndpoint.ForServer(server);

            long current = _stones.GetStone(_stone)!.Revision;
            var attempt = endpoint.Submit(Commit("t035-stale", _gov, _govChar,
                expectedStoneRevision: current + 500));

            Assert.True(attempt.Rejected);
            Assert.Equal("StaleStoneRevision", attempt.ResultCode);
            Assert.Equal(current, _stones.GetStone(_stone)!.Revision);
            Assert.False(TreeCommitted(_stones.GetStone(_stone), Crafting));
        }

        [Fact]
        public void Replay_of_the_same_operation_returns_the_recorded_result_without_a_second_mutation()
        {
            var server = NewServer();
            SeedBareStone(_stone);
            var endpoint = ProgressionCommandEndpoint.ForServer(server);

            var first = endpoint.Submit(Commit("t035-replay", _gov, _govChar));
            Assert.Equal(CommandOutcome.Applied, first.Outcome);
            long rev = _stones.GetStone(_stone)!.Revision;

            var second = endpoint.Submit(Commit("t035-replay", _gov, _govChar));
            Assert.Equal(CommandOutcome.Replayed, second.Outcome);
            Assert.Equal(first.ReceiptId, second.ReceiptId);
            Assert.Equal(rev, _stones.GetStone(_stone)!.Revision);   // no second revision bump
        }

        [Fact]
        public void Operation_id_reuse_with_a_different_binding_conflicts_without_mutation()
        {
            var server = NewServer();
            SeedBareStone(_stone);
            var endpoint = ProgressionCommandEndpoint.ForServer(server);

            var first = endpoint.Submit(Commit("t035-collide", _gov, _govChar));
            Assert.Equal(CommandOutcome.Applied, first.Outcome);
            long rev = _stones.GetStone(_stone)!.Revision;

            // Same operation id, DIFFERENT principal — an idempotency conflict, not a replay.
            var collision = endpoint.Submit(Commit("t035-collide", _hostile, _hostileChar));
            Assert.True(collision.Rejected);
            Assert.Equal(rev, _stones.GetStone(_stone)!.Revision);
        }

        [Fact]
        public void Remote_purchase_without_attunement_is_rejected_by_the_real_gate()
        {
            var server = NewServer();
            SeedBareStone(_stone);
            var endpoint = ProgressionCommandEndpoint.ForServer(server);

            // Routable — but the shipped PurchaseCommandHandler still demands an active Attunement, and the
            // Governor holds only a Bond. Remote reach never widens authority.
            var attempt = endpoint.Submit(new ProgressionCommandEnvelope(
                ProgressionCommandType.PurchaseNode, new OperationId("t035-buy"), _stone,
                Conn(_gov, _govChar), default, HomesteadProgressionCatalog.CurrentContentRegistryVersion,
                new ProgressionCommandPayload(tree: Crafting, node: Masterwork)));

            Assert.True(attempt.Rejected);
            Assert.Equal("RelationshipRequired", attempt.ResultCode);
            Assert.Null(attempt.Notification);
        }

        [Fact]
        public void Negative_bp_amount_is_rejected_and_never_credits()
        {
            var server = NewServer();
            SeedBareStone(_stone);
            var endpoint = ProgressionCommandEndpoint.ForServer(server);
            endpoint.Submit(Commit("t035-neg-commit", _gov, _govChar));

            var attempt = endpoint.Submit(new ProgressionCommandEnvelope(
                ProgressionCommandType.ApplyBPToNode, new OperationId("t035-neg"), _stone,
                Conn(_gov, _govChar), default, HomesteadProgressionCatalog.CurrentContentRegistryVersion,
                new ProgressionCommandPayload(tree: Crafting, node: RefinedWorkshop, bpAmount: -1000)));

            Assert.True(attempt.Rejected);
            Assert.False(NodeDeveloped(_stones.GetStone(_stone), RefinedWorkshop));
        }

        [Fact]
        public void Preview_revocation_through_the_seam_mutates_nothing()
        {
            var server = NewServer();
            SeedBareStone(_stone);
            var endpoint = ProgressionCommandEndpoint.ForServer(server);
            endpoint.Submit(Commit("t035-prev-commit", _gov, _govChar));

            long rev = _stones.GetStone(_stone)!.Revision;
            var preview = endpoint.Submit(new ProgressionCommandEnvelope(
                ProgressionCommandType.PreviewRevocation, new OperationId("t035-preview"), _stone,
                Conn(_gov, _govChar), default, HomesteadProgressionCatalog.CurrentContentRegistryVersion,
                new ProgressionCommandPayload(
                    facetId: HomesteadProgressionCatalog.ProfessionFacetId, tree: Crafting)));

            // Step one of the two-step act: it may be accepted or refused, but it NEVER mutates and never
            // publishes an invalidation.
            Assert.Null(preview.Notification);
            Assert.Equal(rev, _stones.GetStone(_stone)!.Revision);
            Assert.True(TreeCommitted(_stones.GetStone(_stone), Crafting));
        }

        [Fact]
        public void Uncomposed_handler_fails_closed_rather_than_permissively()
        {
            var bare = new ProgressionCommandEndpoint();
            var attempt = bare.Submit(Commit("t035-nohandler", _gov, _govChar));
            Assert.True(attempt.Rejected);
            Assert.Equal(ProgressionCommandEndpoint.HandlerUnavailable, attempt.ResultCode);

            var portfolio = bare.Portfolio(Conn(_gov, _govChar), default);
            Assert.False(portfolio.Bound);
            Assert.Equal(ProgressionCommandEndpoint.HandlerUnavailable, portfolio.ResultCode);
            Assert.Empty(portfolio.Entries);
        }

        // ════════════════════════════════════════════════════════════════════════════════════════════
        //  Bounded revision / invalidation notifications
        // ════════════════════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void Notifications_are_per_subscriber_monotonic_and_never_broadcast()
        {
            var hub = new ProgressionNotificationHub();
            var seed = new ProgressionRevisionNotification(_stone, _gov.Value,
                ProgressionCommandType.ApplyBPToNode, 7, 3, 0, "Applied");

            // No subscribers ⇒ nothing is delivered. There is no broadcast path.
            Assert.Empty(hub.Publish(seed));

            hub.Subscribe(_stone, _gov);
            hub.Subscribe(_stone, _hostile);

            var first = hub.Publish(seed);
            Assert.Equal(2, first.Count);
            foreach (var n in first) Assert.Equal(1, n.Sequence);

            var second = hub.Publish(seed);
            foreach (var n in second) Assert.Equal(2, n.Sequence);

            // Unsubscribing stops delivery for that account only.
            hub.Unsubscribe(_stone, _hostile);
            var third = hub.Publish(seed);
            Assert.Single(third);
            Assert.Equal(_gov.Value, third[0].SubscriberAccountId);
        }

        [Fact]
        public void Client_refetches_on_moved_revisions_and_drops_stale_reordered_events()
        {
            var cache = new ProgressionRevisionCache();

            // Nothing held ⇒ always refetch (fail toward fetching).
            Assert.True(cache.ShouldRefetch(new ProgressionRevisionNotification(
                _stone, _gov.Value, ProgressionCommandType.ApplyBPToNode, 5, 2, 0, "Applied", 1)));

            cache.RecordFetched(_stone, stoneRevision: 5, characterRevision: 2, policyRevision: 0, sequence: 3);

            // A duplicate/reordered event at or behind the held sequence with identical revisions is dropped.
            Assert.False(cache.ShouldRefetch(new ProgressionRevisionNotification(
                _stone, _gov.Value, ProgressionCommandType.ApplyBPToNode, 5, 2, 0, "Applied", 2)));

            // Any moved revision forces a refetch even when the sequence is behind — a DROPPED notification
            // can never strand a client on stale data.
            Assert.True(cache.ShouldRefetch(new ProgressionRevisionNotification(
                _stone, _gov.Value, ProgressionCommandType.ApplyBPToNode, 6, 2, 0, "Applied", 1)));
            Assert.True(cache.ShouldRefetch(new ProgressionRevisionNotification(
                _stone, _gov.Value, ProgressionCommandType.SetSettlementLocalPolicy, 5, 2, 9, "Applied", 1)));

            // A sequence ahead forces a refetch too.
            Assert.True(cache.ShouldRefetch(new ProgressionRevisionNotification(
                _stone, _gov.Value, ProgressionCommandType.ApplyBPToNode, 5, 2, 0, "Applied", 4)));

            cache.Invalidate(_stone);
            Assert.True(cache.ShouldRefetch(new ProgressionRevisionNotification(
                _stone, _gov.Value, ProgressionCommandType.ApplyBPToNode, 5, 2, 0, "Applied", 1)));
        }

        [Fact]
        public void Notification_round_trips_over_the_wire_codec()
        {
            var n = new ProgressionRevisionNotification(_stone, _gov.Value,
                ProgressionCommandType.SetSettlementLocalPolicy, 11, 4, 6, "Applied", 9);
            var back = ProgressionRevisionNotification.Deserialize(n.Serialize());

            Assert.Equal(n.StoneId.Value, back.StoneId.Value);
            Assert.Equal(n.SubscriberAccountId, back.SubscriberAccountId);
            Assert.Equal(n.CommandType, back.CommandType);
            Assert.Equal(n.StoneRevision, back.StoneRevision);
            Assert.Equal(n.CharacterRevision, back.CharacterRevision);
            Assert.Equal(n.PolicyRevision, back.PolicyRevision);
            Assert.Equal(n.ResultCode, back.ResultCode);
            Assert.Equal(n.Sequence, back.Sequence);
        }

        [Fact]
        public void Notification_carries_no_ledger_fields()
        {
            // Bounded by construction: stable ids + revisions + result code + sequence. Nothing else.
            foreach (var p in typeof(ProgressionRevisionNotification).GetProperties())
            {
                string n = p.Name.ToLowerInvariant();
                Assert.DoesNotContain("balance", n);
                Assert.DoesNotContain("purchase", n);
                Assert.DoesNotContain("ap", n.Replace("commandtype", string.Empty));
                Assert.DoesNotContain("relationship", n);
            }
        }

        // ── Stubs (server-owned authority policies; mirror the shared-suite fixtures) ────────────────

        private sealed class FixedFamilyResolver : IStoneFamilyResolver
        {
            public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
            {
                family = "Settlement"; variant = "Homestead"; return true;
            }
        }

        private sealed class AllowHomesteadBondPolicy : IBondAuthorityPolicy
        {
            public bool TryAuthorizeBond(StoneId stoneId, string requestedResponsibilityRange,
                out string grantedRange, out string grantedRole)
            {
                grantedRange = requestedResponsibilityRange ?? string.Empty;
                grantedRole = "Governor";
                return string.Equals(requestedResponsibilityRange, "Homestead:All",
                    System.StringComparison.Ordinal);
            }
        }

        private sealed class AllowGovernorAuthority : IGovernorAuthorityPolicy
        {
            public bool CanCommit(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                string facetId, FacetCategory category) =>
                string.Equals(responsibilityRange, "Homestead:All", System.StringComparison.Ordinal)
                && string.Equals(ownerGovernorRole, "Governor", System.StringComparison.Ordinal)
                && category != FacetCategory.None;
        }

        private sealed class AllowDevelopmentAuthority : IGovernorDevelopmentAuthority
        {
            public bool CanDevelop(StoneId stoneId, string responsibilityRange, string ownerGovernorRole,
                VersionedId tree) =>
                string.Equals(responsibilityRange, "Homestead:All", System.StringComparison.Ordinal)
                && string.Equals(ownerGovernorRole, "Governor", System.StringComparison.Ordinal)
                && !tree.IsNone;
        }
    }
}
