// ============================================================================
//  RD-T004 fix-forward — relationship-to-Connection integration regressions.
// ----------------------------------------------------------------------------
//  Independent review found the RD-T004 Tracer-1 coordinator standalone and
//  incorrect at lifecycle edges. These regressions lock the four required fixes
//  (spec RD-002 / RD-004, contracts §"Relationship-to-Connection integration"):
//
//    1. COMMAND INTEGRATION — a real CreateBond/CreateAttunement/Release through the
//       shipped RelationshipCommandHandler drives the matching account-pair Connection
//       source transition in the SAME logical transaction (acknowledgement only after
//       the source transition is durable, because ApplyProjections runs after the
//       relationship terminal is fsync'd).
//    2. EXPIRED-GRACE RECONNECT — reconnecting a pair AFTER its 72h grace has expired
//       resets the accumulated age to zero (a fresh Active segment), never restoring
//       frozen maturity.
//    3. DURABLE RESTART — a restarted process reconstructs the exact Connection source
//       state from the committed relationship journal alone (the source coordinator is
//       a recoverable projection of it), and a journaled grace-expiry reset survives.
//    4. EXACT REPLAY RESULT — replaying a committed lifecycle op returns the ORIGINAL
//       exact affected-set, never a recomputation against the current roster and never
//       an empty release set.
//
//  Engine-free: exercises only link-compiled ../src types + the in-memory stores.
// ============================================================================

using System.Collections.Generic;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.ResourceDelivery;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class ResourceDeliveryRelationshipIntegrationTests : System.IDisposable
    {
        private static readonly WorldId World = new WorldId("uid:rd-t004-int");
        private static readonly ProductScope Product = new ProductScope(FoundationalConnectionProduct);
        private const string FoundationalConnectionProduct = "SBPR.Trailborne";
        private const long Day = ConnectionMaturity.SecondsPerDay;

        private readonly string _relJournal;
        private readonly string _srcJournal;
        private readonly StoneId _stone;
        private readonly AccountId _accountA = new AccountId("acct-A");
        private readonly CharacterId _charA = new CharacterId("char-A");
        private readonly AccountId _accountB = new AccountId("acct-B");
        private readonly CharacterId _charB = new CharacterId("char-B");

        private readonly InMemoryCharacterAggregateStore _characters = new InMemoryCharacterAggregateStore();
        private readonly InMemoryAccountStoneAuthorityStore _authority = new InMemoryAccountStoneAuthorityStore();
        private readonly StubFamilyResolver _families = new StubFamilyResolver();

        public ResourceDeliveryRelationshipIntegrationTests()
        {
            string g = System.Guid.NewGuid().ToString("N");
            _relJournal = Path.Combine(Path.GetTempPath(), "rd-t004-int-rel-" + g + ".journal");
            _srcJournal = Path.Combine(Path.GetTempPath(), "rd-t004-int-src-" + g + ".journal");
            _stone = StoneId.FromHostZone(World, 3, 3);
            _families.Set(_stone, "Community", "Community"); // Community allows independent per-account bonds
            _characters.PutCharacter(BuildCharacter(_accountA, _charA));
            _characters.PutCharacter(BuildCharacter(_accountB, _charB));
        }

        public void Dispose()
        {
            if (File.Exists(_relJournal)) File.Delete(_relJournal);
            if (File.Exists(_srcJournal)) File.Delete(_srcJournal);
        }

        private static CharacterProgressionAggregate BuildCharacter(AccountId account, CharacterId character) =>
            new CharacterProgressionAggregate(account, character,
                worldProductScope: "rd-t004-int/trailborne", revision: 0,
                bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[] { new CharacterStoneRecord(_StoneFor(), 0, 0, 0, null, null) });

        // Character stone record must carry the same StoneId; helper keeps ctor concise.
        private static StoneId _StoneFor() => StoneId.FromHostZone(World, 3, 3);

        private StoneConnectionSourceRegistry NewSources() => new StoneConnectionSourceRegistry(_srcJournal);

        private RelationshipCommandHandler NewHandler(StoneConnectionSourceRegistry sources) =>
            new RelationshipCommandHandler(_relJournal, new PrincipalResolver(), _characters, _authority,
                _families, new StubBondAuthorityPolicy(), sources, World, Product);

        private RelationshipCommand Bond(AccountId account, CharacterId who, string relId, long time) =>
            new RelationshipCommand(new OperationId("op-bond-" + relId), RelationshipCommandType.CreateBond, _stone,
                new AuthenticatedConnection(account.Value, who.Value), default, relId,
                responsibilityRange: "Community:All", ownerGovernorRole: "Owner", serverTimeSeconds: time);

        private RelationshipCommand Release(AccountId account, CharacterId who, string relId, string op, long time) =>
            new RelationshipCommand(new OperationId(op), RelationshipCommandType.ReleaseRelationship, _stone,
                new AuthenticatedConnection(account.Value, who.Value), default, relId, serverTimeSeconds: time);

        private static ConnectionId Conn(string a, string b)
        {
            ConnectionId.TryCreate(World, Product, new AccountId(a), new AccountId(b), out var id);
            return id;
        }

        // ── 1. Command-level integration ────────────────────────────────────────

        [Fact]
        public void CommandIntegration_TwoBonds_ActivateConnection_InSameLogicalTransaction()
        {
            var sources = NewSources();
            var handler = NewHandler(sources);
            long t0 = 1000;

            Assert.Equal(RelationshipCommandOutcome.Applied, handler.Handle(Bond(_accountA, _charA, "rel-A", t0)).Outcome);
            Assert.Equal(RelationshipCommandOutcome.Applied, handler.Handle(Bond(_accountB, _charB, "rel-B", t0)).Outcome);

            // The account-pair Connection is Active and contribution-qualifying purely as a projection of
            // the two committed relationship commands — no standalone registry call was needed.
            var conn = sources.GetConnection(Conn("acct-A", "acct-B"));
            Assert.Equal(ConnectionLifecycle.Active, conn.Lifecycle);
            Assert.True(conn.IsContributionQualifying);
            Assert.Equal(ConnectionMaturity.Band2, conn.MaturityAt(t0 + 10 * Day)); // age-only, 7–<30d band
        }

        [Fact]
        public void CommandIntegration_Release_DropsConnectionIntoFrozenGrace()
        {
            var sources = NewSources();
            var handler = NewHandler(sources);
            long t0 = 1000;
            handler.Handle(Bond(_accountA, _charA, "rel-A", t0));
            handler.Handle(Bond(_accountB, _charB, "rel-B", t0));

            long releaseAt = t0 + 10 * Day;
            Assert.Equal(RelationshipCommandOutcome.Applied,
                handler.Handle(Release(_accountA, _charA, "rel-A", "op-rel-A", releaseAt)).Outcome);

            var conn = sources.GetConnection(Conn("acct-A", "acct-B"));
            Assert.Equal(ConnectionLifecycle.Grace, conn.Lifecycle);
            Assert.False(conn.IsContributionQualifying);
            Assert.Equal(10 * Day, conn.AccumulatedSeconds);
            Assert.Equal(releaseAt + ConnectionAggregate.GraceSeconds, conn.GraceExpiresAtSeconds);
        }

        // ── 2. Expired-grace reconnect resets age ────────────────────────────────

        [Fact]
        public void ExpiredGraceReconnect_ResetsAge_InsteadOfRestoringFrozenMaturity()
        {
            var sources = NewSources();
            long t0 = 1000;
            // Bond, mature 10 days, release into grace, let the 72h grace fully expire, then reconnect.
            sources.ActivateRelationship("op-a", World, Product, _stone, _accountA, "rel-A", RelationshipKind.Bond, t0);
            sources.ActivateRelationship("op-b", World, Product, _stone, _accountB, "rel-B", RelationshipKind.Bond, t0);
            long releaseAt = t0 + 10 * Day;
            sources.ReleaseRelationship("op-rel-a", World, Product, _stone, _accountA, "rel-A", releaseAt);

            long reconnectAt = releaseAt + ConnectionAggregate.GraceSeconds + Day; // AFTER expiry
            sources.ActivateRelationship("op-a2", World, Product, _stone, _accountA, "rel-A", RelationshipKind.Bond, reconnectAt);

            var conn = sources.GetConnection(Conn("acct-A", "acct-B"));
            Assert.Equal(ConnectionLifecycle.Active, conn.Lifecycle);
            // The frozen 10-day age is DISCARDED: the reconnect after expiry starts a fresh segment.
            Assert.Equal(0, conn.AccumulatedSeconds);
            Assert.Equal(Day, conn.LiveAgeSeconds(reconnectAt + Day));
        }

        // ── 3. Durable restart ──────────────────────────────────────────────────

        [Fact]
        public void DurableRestart_ReconstructsConnectionFromRelationshipJournalAlone()
        {
            long t0 = 1000;
            {
                var sources = NewSources();
                var handler = NewHandler(sources);
                handler.Handle(Bond(_accountA, _charA, "rel-A", t0));
                handler.Handle(Bond(_accountB, _charB, "rel-B", t0));
            }

            // Fresh source registry + fresh handler over the SAME journals == restarted process.
            var sources2 = new StoneConnectionSourceRegistry(_srcJournal);
            var characters2 = new InMemoryCharacterAggregateStore();
            var authority2 = new InMemoryAccountStoneAuthorityStore();
            characters2.PutCharacter(BuildCharacter(_accountA, _charA));
            characters2.PutCharacter(BuildCharacter(_accountB, _charB));
            _ = new RelationshipCommandHandler(_relJournal, new PrincipalResolver(), characters2, authority2,
                _families, new StubBondAuthorityPolicy(), sources2, World, Product);

            var conn = sources2.GetConnection(Conn("acct-A", "acct-B"));
            Assert.Equal(ConnectionLifecycle.Active, conn.Lifecycle);
            Assert.True(conn.IsContributionQualifying);
            Assert.Equal(10 * Day, conn.LiveAgeSeconds(t0 + 10 * Day));
        }

        [Fact]
        public void DurableRestart_JournaledGraceExpiryReset_SurvivesRestart()
        {
            long t0 = 1000;
            long releaseAt = t0 + 10 * Day;
            {
                var sources = NewSources();
                sources.ActivateRelationship("op-a", World, Product, _stone, _accountA, "rel-A", RelationshipKind.Bond, t0);
                sources.ActivateRelationship("op-b", World, Product, _stone, _accountB, "rel-B", RelationshipKind.Bond, t0);
                sources.ReleaseRelationship("op-rel-a", World, Product, _stone, _accountA, "rel-A", releaseAt);
                // Reconcile the grace expiry -> Reset. This MUST be journaled.
                var reset = sources.ReconcileGraceExpiry(Conn("acct-A", "acct-B"), releaseAt + ConnectionAggregate.GraceSeconds);
                Assert.Equal(ConnectionLifecycle.Reset, reset.Lifecycle);
            }

            // Restart: a fresh registry replays the journal. Without the journaled reset it would restore
            // the frozen grace maturity; with it, the Connection is Reset (age zero).
            var sources2 = new StoneConnectionSourceRegistry(_srcJournal);
            var conn = sources2.GetConnection(Conn("acct-A", "acct-B"));
            Assert.Equal(ConnectionLifecycle.Reset, conn.Lifecycle);
            Assert.Equal(0, conn.AccumulatedSeconds);
        }

        // ── 4. Exact replay result ──────────────────────────────────────────────

        [Fact]
        public void ExactReplay_Release_ReturnsOriginalAffectedSet_NotEmpty()
        {
            var sources = NewSources();
            long t0 = 1000;
            sources.ActivateRelationship("op-a", World, Product, _stone, _accountA, "rel-A", RelationshipKind.Bond, t0);
            sources.ActivateRelationship("op-b", World, Product, _stone, _accountB, "rel-B", RelationshipKind.Bond, t0);

            var original = sources.ReleaseRelationship("op-rel-a", World, Product, _stone, _accountA, "rel-A", t0 + Day);
            Assert.Equal(ConnectionSourceOutcome.Applied, original.Outcome);
            Assert.Contains(Conn("acct-A", "acct-B").CanonicalKey, original.AffectedConnectionKeys);

            // Replaying the SAME release op returns the ORIGINAL affected set, not the empty set a
            // post-removal recomputation would produce (the participant is already gone).
            var replay = sources.ReleaseRelationship("op-rel-a", World, Product, _stone, _accountA, "rel-A", t0 + Day);
            Assert.Equal(ConnectionSourceOutcome.Replayed, replay.Outcome);
            Assert.Equal(original.AffectedConnectionKeys, replay.AffectedConnectionKeys);
        }

        [Fact]
        public void ExactReplay_AfterRestart_ReturnsPersistedAffectedSet()
        {
            long t0 = 1000;
            IReadOnlyList<string> original;
            {
                var sources = NewSources();
                sources.ActivateRelationship("op-a", World, Product, _stone, _accountA, "rel-A", RelationshipKind.Bond, t0);
                var res = sources.ActivateRelationship("op-b", World, Product, _stone, _accountB, "rel-B", RelationshipKind.Bond, t0);
                original = res.AffectedConnectionKeys;
                Assert.Contains(Conn("acct-A", "acct-B").CanonicalKey, original);
            }

            // Restart, then replay op-b: the persisted affected set is returned verbatim.
            var sources2 = new StoneConnectionSourceRegistry(_srcJournal);
            var replay = sources2.ActivateRelationship("op-b", World, Product, _stone, _accountB, "rel-B", RelationshipKind.Bond, t0);
            Assert.Equal(ConnectionSourceOutcome.Replayed, replay.Outcome);
            Assert.Equal(original, replay.AffectedConnectionKeys);
        }

        // ── Reused test stubs (mirror NiflheimRelationshipLifecycleTests) ────────

        private sealed class StubFamilyResolver : IStoneFamilyResolver
        {
            private readonly Dictionary<string, (string family, string variant)> _map =
                new Dictionary<string, (string, string)>(System.StringComparer.Ordinal);
            public void Set(StoneId stone, string family, string variant) => _map[stone.Value] = (family, variant);
            public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
            {
                if (_map.TryGetValue(stoneId.Value, out var v)) { family = v.family; variant = v.variant; return true; }
                family = variant = string.Empty; return false;
            }
        }

        private sealed class StubBondAuthorityPolicy : IBondAuthorityPolicy
        {
            public bool TryAuthorizeBond(StoneId stoneId, string requestedResponsibilityRange,
                out string grantedRange, out string grantedRole)
            {
                grantedRange = string.Empty;
                grantedRole = string.Empty;
                bool authored =
                    string.Equals(requestedResponsibilityRange, "Homestead:All", System.StringComparison.Ordinal) ||
                    string.Equals(requestedResponsibilityRange, "Community:All", System.StringComparison.Ordinal);
                if (!authored) return false;
                grantedRange = requestedResponsibilityRange;
                grantedRole = "Governor";
                return true;
            }
        }
    }
}
