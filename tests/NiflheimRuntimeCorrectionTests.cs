// ============================================================================
//  Homestead progression — T009R3 runtime-correction tests.
// ----------------------------------------------------------------------------
//  Blocker 2 — authenticated creator-identity reconciliation:
//    The placed piece's ZDO records s_creator = the placing character's
//    s_playerID (Player.SetCreator(GetPlayerID())). The authenticated sender
//    must be resolved to the SAME server-owned s_playerID, NOT the platform id
//    (peer.m_characterID.UserID) the T009R2 path used. AuthenticatedSenderBinder
//    is the engine-free reconciliation; these tests pin match / mismatch / empty
//    / reconnect and prove the bound principal matches a real placed creator via
//    the shipped DedicatedPlacementIngress.
//
//  Blocker 3 — live relationship establishment:
//    RelationshipProvisioningIngress seeds an absent character aggregate and
//    drives the SHIPPED RelationshipCommandHandler with a server-derived subject,
//    so a placement can then be credited. Tests prove it establishes an
//    Attunement, is idempotent, rejects unsupported commands / empty identity,
//    and that a subsequently-placed piece by that subject earns a receipt.
// ============================================================================

using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Activities;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimRuntimeCorrectionTests : System.IDisposable
    {
        private readonly string _durableDir;
        private readonly WorldId _world = new WorldId("uid:t009r3");
        private readonly StoneId _stone;
        private const double StoneX = 100.0;
        private const double StoneZ = 100.0;

        // s_playerID of the placing character (what vanilla stamps as the piece creator). The CHARACTER
        // subject is derived from this durable id; the ACCOUNT is the distinct authenticated platform
        // subject. Creator binding is creator == CHARACTER subject (T009R4, Blockers 2/3).
        private const long PlayerId = 424242L;
        private readonly AccountId _account = new AccountId("acct:steam-abc");   // authenticated platform account subject
        private readonly CharacterId _character = new CharacterId("player:424242"); // stable s_playerID character subject

        public NiflheimRuntimeCorrectionTests()
        {
            _durableDir = Path.Combine(Path.GetTempPath(), "niflheim-t009r3-" + System.Guid.NewGuid().ToString("N"));
            _stone = StoneId.FromHostZone(_world, 7, 3);
        }

        public void Dispose()
        {
            if (Directory.Exists(_durableDir)) Directory.Delete(_durableDir, recursive: true);
        }

        // ── Blocker 2: AuthenticatedSenderBinder reconciliation (corrected split) ────

        [Fact]
        public void Binder_CharacterWithPlayerIdAndPlatform_BindsAccountAndCharacter()
        {
            var character = new AuthenticatedSenderCharacter(PlayerId, "acct:steam-abc");
            bool ok = AuthenticatedSenderBinder.TryBind(character, out string account, out string charSubject);

            Assert.True(ok);
            // Account = the authenticated platform subject; character = the stable player:<s_playerID>
            // subject a placed ZDO's s_creator renders to.
            Assert.Equal("acct:steam-abc", account);
            Assert.Equal(ServerCreatorIdentity.CharacterSubject(PlayerId), charSubject);
            Assert.Equal("player:424242", charSubject);
        }

        [Fact]
        public void Binder_ZeroPlayerId_IsUnbindable()
        {
            var character = new AuthenticatedSenderCharacter(0L, "acct:steam-abc");
            bool ok = AuthenticatedSenderBinder.TryBind(character, out _, out string charSubject);

            Assert.False(ok);                          // empty s_playerID → cannot bind a character
            Assert.Equal(string.Empty, charSubject);   // never an empty subject that could "match" an empty creator
        }

        [Fact]
        public void Binder_MissingPlatformAccount_IsUnbindable()
        {
            var character = new AuthenticatedSenderCharacter(PlayerId, "");
            bool ok = AuthenticatedSenderBinder.TryBind(character, out string account, out _);

            Assert.False(ok);
            Assert.Equal(string.Empty, account);
        }

        [Fact]
        public void Binder_Reconnect_SamePlayerIdSameAccount_YieldsSameCharacterSubject()
        {
            // Session 1 and session 2 (reconnect): the live character ZDOID changes, but s_playerID and
            // the platform account are durable, so BOTH bound subjects are reconnect-stable (Blocker 3).
            AuthenticatedSenderBinder.TryBind(new AuthenticatedSenderCharacter(PlayerId, "acct:steam-abc"),
                out string account1, out string char1);
            AuthenticatedSenderBinder.TryBind(new AuthenticatedSenderCharacter(PlayerId, "acct:steam-abc"),
                out string account2, out string char2);

            Assert.Equal(account1, account2);   // account subject reconnect-stable
            Assert.Equal(char1, char2);         // character subject reconnect-stable (durable s_playerID)
        }

        [Fact]
        public void Binder_BoundCharacter_MatchesRealPlacedCreator_EarnsReceipt()
        {
            // End-to-end: a ZDO created by s_playerID=PlayerId, and an authenticated sender resolved to the
            // SAME s_playerID, reconcile through the shipped ingress and earn a receipt. Creator binds to
            // the CHARACTER subject, not the account.
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);
            SeedAndAttune(server);

            // The placed ZDO's recorded creator, rendered from s_playerID (as ZdoServerPlacedInstanceSource does).
            string zdoCreatorPrincipal = ServerCreatorIdentity.CreatorPrincipal(PlayerId);
            var source = new FakeInstanceSource();
            source.Put("777:9", "wood_floor", zdoCreatorPrincipal, StoneX, StoneZ);

            // The authenticated sender, bound from its character facts (s_playerID + platform account).
            AuthenticatedSenderBinder.TryBind(new AuthenticatedSenderCharacter(PlayerId, _account.Value),
                out string senderAccount, out string senderCharacter);

            var outcome = server.CreateDedicatedIngress(source).Ingest(senderAccount, senderCharacter, "777:9");

            Assert.True(outcome.Routed);
            Assert.Equal(RuntimePlacementDisposition.Earned, outcome.Runtime.Disposition);
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(_stone));
        }

        // ── Blocker 3: RelationshipProvisioningIngress ──────────────────────────────

        [Fact]
        public void Provision_SeedsAbsentCharacter_EstablishesAttunement()
        {
            var server = NewServer();
            var ingress = server.CreateRelationshipProvisioningIngress();

            // No character seeded beforehand — the ingress must seed it, then establish the Attunement.
            Assert.Null(server.Characters.GetCharacter(_account, _character));

            var result = ingress.Provision(
                new AuthoritativeSubject(_account, _character), _stone,
                RelationshipCommandType.CreateAttunement, "op-prov-1", "rel-prov-1", "t009r3/trailborne");

            Assert.True(result.Established);
            Assert.Equal(RelationshipCommandOutcome.Applied, result.Outcome);
            Assert.NotNull(server.Characters.GetCharacter(_account, _character));
        }

        [Fact]
        public void Provision_Idempotent_SameOperationReplays()
        {
            var server = NewServer();
            var ingress = server.CreateRelationshipProvisioningIngress();
            var subject = new AuthoritativeSubject(_account, _character);

            var first = ingress.Provision(subject, _stone, RelationshipCommandType.CreateAttunement,
                "op-prov-2", "rel-prov-2", "t009r3/trailborne");
            var replay = ingress.Provision(subject, _stone, RelationshipCommandType.CreateAttunement,
                "op-prov-2", "rel-prov-2", "t009r3/trailborne");

            Assert.Equal(RelationshipCommandOutcome.Applied, first.Outcome);
            Assert.Equal(RelationshipCommandOutcome.Replayed, replay.Outcome);
            Assert.True(replay.Established);
        }

        [Fact]
        public void Provision_EmptyIdentity_Rejected_NoHandlerCall()
        {
            var server = NewServer();
            var ingress = server.CreateRelationshipProvisioningIngress();

            var result = ingress.Provision(
                new AuthoritativeSubject(new AccountId(""), new CharacterId("")), _stone,
                RelationshipCommandType.CreateAttunement, "op-prov-3", "rel-prov-3", "t009r3/trailborne");

            Assert.False(result.Routed);
            Assert.Equal("Unauthenticated", result.ResultCode);
        }

        [Fact]
        public void Provision_UnsupportedCommand_Rejected()
        {
            var server = NewServer();
            var ingress = server.CreateRelationshipProvisioningIngress();

            var result = ingress.Provision(
                new AuthoritativeSubject(_account, _character), _stone,
                RelationshipCommandType.ReleaseRelationship, "op-prov-4", "rel-prov-4", "t009r3/trailborne");

            Assert.False(result.Routed);
            Assert.Equal("UnsupportedProvisioningCommand", result.ResultCode);
        }

        [Fact]
        public void Provision_DoesNotOverwriteExistingProgression()
        {
            var server = NewServer();
            // Pre-seed a character carrying real progression (non-zero personal AP).
            ((InMemoryCharacterAggregateStore)server.Characters).PutCharacter(
                new CharacterProgressionAggregate(_account, _character,
                    worldProductScope: "t009r3/trailborne", revision: 3,
                    bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "prior",
                    stoneRecords: new[] { new CharacterStoneRecord(_stone, 42, 42, 0, null, null) }));

            var ingress = server.CreateRelationshipProvisioningIngress();
            ingress.Provision(new AuthoritativeSubject(_account, _character), _stone,
                RelationshipCommandType.CreateAttunement, "op-prov-5", "rel-prov-5", "t009r3/trailborne");

            var after = server.Characters.GetCharacter(_account, _character);
            Assert.NotNull(after);
            // The pre-existing personal AP survived — the ingress seeded nothing over it.
            Assert.Equal(42, after!.StoneRecords[0].PersonalAp);
        }

        [Fact]
        public void Provision_ThenPlace_EarnsReceipt_FullLiveEstablishmentPath()
        {
            // Blocker 3's reason to exist: with no prior relationship, a placement cannot credit. After
            // provisioning establishes the Attunement, the SAME shared placement runtime credits it.
            var stoneStore = new InMemoryMirroredStoneApStore();
            var server = NewServer(stoneStore);

            // Before provisioning: a placement observation is unauthorized (no relationship).
            var pre = server.Runtime.Observe(new FoundationalPlacementObservation(
                _stone, _account.Value, _character.Value, "foundation_wood_floor", "777:20",
                insideStoneArea: true, placementSucceeded: true, foundationalCatalogVersion: "v1"));
            Assert.Equal(RuntimePlacementDisposition.PipelineRejected, pre.Disposition);
            Assert.Equal("RelationshipRequired", pre.ResultCode);

            // Provision the Attunement through the live seam.
            server.CreateRelationshipProvisioningIngress().Provision(
                new AuthoritativeSubject(_account, _character), _stone,
                RelationshipCommandType.CreateAttunement, "op-prov-6", "rel-prov-6", "t009r3/trailborne");

            // Now the same placement path credits.
            var post = server.Runtime.Observe(new FoundationalPlacementObservation(
                _stone, _account.Value, _character.Value, "foundation_wood_floor", "777:21",
                insideStoneArea: true, placementSucceeded: true, foundationalCatalogVersion: "v1"));
            Assert.Equal(RuntimePlacementDisposition.Earned, post.Disposition);
            Assert.Equal(1, stoneStore.GetMirroredStoneAp(_stone));
        }

        // ── fixtures ────────────────────────────────────────────────────────────

        private sealed class FixedFamilyResolver : IStoneFamilyResolver
        {
            private readonly string _key;
            public FixedFamilyResolver(StoneId stone) { _key = stone.Value; }
            public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
            {
                if (string.Equals(stoneId.Value, _key, System.StringComparison.Ordinal))
                { family = "Settlement"; variant = "Homestead"; return true; }
                family = variant = string.Empty; return false;
            }
        }

        private sealed class HomesteadBondPolicy : IBondAuthorityPolicy
        {
            public bool TryAuthorizeBond(StoneId stoneId, string requestedResponsibilityRange,
                out string grantedRange, out string grantedRole)
            {
                grantedRange = requestedResponsibilityRange ?? string.Empty;
                grantedRole = "Governor";
                return string.Equals(requestedResponsibilityRange, "Homestead:All", System.StringComparison.Ordinal);
            }
        }

        private sealed class FakeInstanceSource : IServerPlacedInstanceSource
        {
            private readonly System.Collections.Generic.Dictionary<string, ServerPlacedInstanceFacts> _byKey =
                new System.Collections.Generic.Dictionary<string, ServerPlacedInstanceFacts>(System.StringComparer.Ordinal);

            public void Put(string key, string prefabName, string creatorPrincipal, double x, double z) =>
                _byKey[key] = new ServerPlacedInstanceFacts(key, prefabName, creatorPrincipal, x, z, exists: true);

            public bool TryResolve(string instanceKey, out ServerPlacedInstanceFacts facts)
            {
                if (instanceKey != null && _byKey.TryGetValue(instanceKey, out facts)) return true;
                facts = ServerPlacedInstanceFacts.Absent(instanceKey ?? string.Empty);
                return false;
            }
        }

        private FoundationalProgressionServer NewServer(IMirroredStoneApStore? stoneStore = null)
        {
            var server = FoundationalProgressionServer.Create(
                _durableDir,
                accountIdForPlatform: null,
                familyResolver: new FixedFamilyResolver(_stone),
                bondAuthority: new HomesteadBondPolicy(),
                stoneApStore: stoneStore ?? new InMemoryMirroredStoneApStore());
            server.StoneAreas.Register(_stone, StoneX, StoneZ, radius: 20.0);
            return server;
        }

        private void SeedAndAttune(FoundationalProgressionServer server)
        {
            ((InMemoryCharacterAggregateStore)server.Characters).PutCharacter(
                new CharacterProgressionAggregate(_account, _character,
                    worldProductScope: "t009r3/trailborne", revision: 0,
                    bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                    stoneRecords: new[] { new CharacterStoneRecord(_stone, 0, 0, 0, null, null) }));
            var res = server.Relationships.Handle(new RelationshipCommand(
                new OperationId("op-att-r3"), RelationshipCommandType.CreateAttunement, _stone,
                new AuthenticatedConnection(_account.Value, _character.Value), default, "rel-att-r3"));
            Assert.Equal(RelationshipCommandOutcome.Applied, res.Outcome);
        }
    }
}
