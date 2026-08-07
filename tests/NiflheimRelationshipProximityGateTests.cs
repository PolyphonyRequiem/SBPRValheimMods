// ============================================================================
//  ADO #138 — SERVER-CHECKED PROXIMITY on the proximate relationship acts.
// ----------------------------------------------------------------------------
//  Forming a Bond or requesting an Attunement requires the acting character to
//  actually be standing at the target Stone, and the SERVER decides that from its
//  own position + Stone Area facts. Before this card RelationshipCommands.cs had
//  NO position check at all: the server took the client's word for it, exactly the
//  unguarded-authority shape ADO #137 closed for the refund primitive.
//
//  What is proven here, against the REAL RelationshipCommandHandler over a real
//  durable journal:
//    * CreateBond and CreateAttunement from OUTSIDE the Stone Area reject NotAtStone
//      and write NOTHING durable (no journal record, no projection).
//    * The same commands from INSIDE the Area apply.
//    * Standing inside a DIFFERENT Stone's Area does not authorize the target Stone.
//    * An UNKNOWN position fails closed, and an EMPTY Area membership denies
//      everything (the fail-closed posture placement already has).
//    * ReleaseRelationship is NOT gated — releasing is not the proximate act, and
//      gating it would strand a character who released away from the Stone.
//    * A committed operation still REPLAYS after the actor walks away: the gate sits
//      after the idempotency lookup so the "timeout after commit, before ack" edge
//      cannot turn into a false failure.
//    * A null proximity authority throws at construction (no permissive fallback).
//
//  Engine-free: no live server, no client. Logs-green != playable — nothing here is
//  in-world evidence.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    /// <summary>Shared test double for the suites whose subject is NOT the ADO #138 gate: they compose the
    /// relationship handler to exercise some other invariant, so they state "the actor is at the Stone" as
    /// an explicit, named premise rather than silently depending on a permissive default. There is no
    /// permissive default in production — the handler requires an authority.</summary>
    internal sealed class AlwaysAtStoneProximity : IStoneProximityAuthority
    {
        internal static readonly AlwaysAtStoneProximity Instance = new AlwaysAtStoneProximity();
        public bool IsAtStone(AuthoritativePrincipal principal, StoneId stoneId) => true;
    }

    public sealed class NiflheimRelationshipProximityGateTests : IDisposable
    {
        private readonly string _journalPath;
        private readonly WorldId _world = new WorldId("uid:prox-138");
        private readonly StoneId _stone;
        private readonly StoneId _otherStone;
        private readonly AccountId _account = new AccountId("acct-A");
        private readonly CharacterId _character = new CharacterId("char-A1");

        private const double StoneX = 1000.0, StoneZ = 2000.0;
        private const double OtherStoneX = 4000.0, OtherStoneZ = 5000.0;

        private readonly InMemoryCharacterAggregateStore _characters = new InMemoryCharacterAggregateStore();
        private readonly InMemoryAccountStoneAuthorityStore _authority = new InMemoryAccountStoneAuthorityStore();
        private readonly ProxFamilyResolver _families = new ProxFamilyResolver();
        private readonly StoneAreaMembership _areas = new StoneAreaMembership();
        private readonly ServerObservedCharacterPositions _positions = new ServerObservedCharacterPositions();

        public NiflheimRelationshipProximityGateTests()
        {
            _journalPath = Path.Combine(Path.GetTempPath(),
                "niflheim-ado138-prox-" + Guid.NewGuid().ToString("N") + ".journal");
            _stone = StoneId.FromHostZone(_world, 3, 7);
            _otherStone = StoneId.FromHostZone(_world, 11, 11);
            _families.Set(_stone, "Settlement", "Homestead");
            _families.Set(_otherStone, "Settlement", "Homestead");

            _characters.PutCharacter(new CharacterProgressionAggregate(
                _account, _character, worldProductScope: "SBPR.Trailborne", revision: 0,
                bondSlots: 1, attunementSlots: 2, lastAppliedReceiptId: "seed",
                stoneRecords: new[]
                {
                    new CharacterStoneRecord(_stone, 0, 0, 0, null, null),
                    new CharacterStoneRecord(_otherStone, 0, 0, 0, null, null)
                }));

            _areas.Register(_stone, StoneX, StoneZ);
            _areas.Register(_otherStone, OtherStoneX, OtherStoneZ);
        }

        public void Dispose()
        {
            if (File.Exists(_journalPath)) File.Delete(_journalPath);
        }

        private RelationshipCommandHandler NewHandler() =>
            new RelationshipCommandHandler(_journalPath, new PrincipalResolver(), _characters, _authority,
                _families, new ProxBondPolicy(),
                new StoneAreaProximityAuthority(_areas, _positions));

        private RelationshipCommand Cmd(string opId, RelationshipCommandType type, string relId,
            StoneId? stoneOverride = null) =>
            new RelationshipCommand(new OperationId(opId), type, stoneOverride ?? _stone,
                new AuthenticatedConnection(_account.Value, _character.Value), default, relId,
                responsibilityRange: type == RelationshipCommandType.CreateBond ? "Homestead:All" : string.Empty);

        // ── Denials ────────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void CreateBond_OutsideStoneArea_RejectsNotAtStone_AndWritesNothing()
        {
            // Standing well outside the Area radius, but claiming the Stone.
            _positions.Publish(_character, StoneX + 500.0, StoneZ);

            var result = NewHandler().Handle(Cmd("op-bond-out", RelationshipCommandType.CreateBond, "rel-1"));

            Assert.Equal(RelationshipCommandOutcome.Rejected, result.Outcome);
            Assert.Equal("NotAtStone", result.ResultCode);
            // A rejection is not a receipt-bearing mutation: nothing durable, nothing projected.
            Assert.False(File.Exists(_journalPath) && new FileInfo(_journalPath).Length > 0);
            Assert.True(_authority.GetAuthority(_account, _stone).IsVacant);
        }

        [Fact]
        public void CreateAttunement_OutsideStoneArea_RejectsNotAtStone()
        {
            _positions.Publish(_character, StoneX, StoneZ + 500.0);

            var result = NewHandler().Handle(Cmd("op-att-out", RelationshipCommandType.CreateAttunement, "rel-2"));

            Assert.Equal(RelationshipCommandOutcome.Rejected, result.Outcome);
            Assert.Equal("NotAtStone", result.ResultCode);
            Assert.True(_authority.GetAuthority(_account, _stone).IsVacant);
        }

        [Fact]
        public void UnknownPosition_FailsClosed()
        {
            // No observation was ever published for this character.
            var result = NewHandler().Handle(Cmd("op-bond-unknown", RelationshipCommandType.CreateBond, "rel-3"));

            Assert.Equal("NotAtStone", result.ResultCode);
        }

        [Fact]
        public void EmptyAreaMembership_DeniesEverything()
        {
            var empty = new StoneAreaMembership();
            _positions.Publish(_character, StoneX, StoneZ);

            var handler = new RelationshipCommandHandler(_journalPath, new PrincipalResolver(), _characters,
                _authority, _families, new ProxBondPolicy(),
                new StoneAreaProximityAuthority(empty, _positions));

            Assert.Equal("NotAtStone",
                handler.Handle(Cmd("op-bond-noarea", RelationshipCommandType.CreateBond, "rel-4")).ResultCode);
        }

        [Fact]
        public void StandingAtADifferentStone_DoesNotAuthorizeTheTargetStone()
        {
            // Genuinely inside SOME Area — the other Stone's — and targeting this one.
            _positions.Publish(_character, OtherStoneX, OtherStoneZ);

            var result = NewHandler().Handle(Cmd("op-bond-wrongstone", RelationshipCommandType.CreateBond, "rel-5"));

            Assert.Equal("NotAtStone", result.ResultCode);
        }

        [Fact]
        public void JustOutsideTheRadius_Denies_AndJustInside_Applies()
        {
            double r = StoneAreaMembership.DefaultAreaRadius;

            _positions.Publish(_character, StoneX + r + 0.5, StoneZ);
            Assert.Equal("NotAtStone",
                NewHandler().Handle(Cmd("op-edge-out", RelationshipCommandType.CreateBond, "rel-6")).ResultCode);

            _positions.Publish(_character, StoneX + r - 0.5, StoneZ);
            Assert.Equal(RelationshipCommandOutcome.Applied,
                NewHandler().Handle(Cmd("op-edge-in", RelationshipCommandType.CreateBond, "rel-6")).Outcome);
        }

        // ── Admissions ─────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void CreateBond_InsideStoneArea_Applies()
        {
            _positions.Publish(_character, StoneX + 1.0, StoneZ - 1.0);

            var result = NewHandler().Handle(Cmd("op-bond-in", RelationshipCommandType.CreateBond, "rel-7"));

            Assert.Equal(RelationshipCommandOutcome.Applied, result.Outcome);
            Assert.Equal("Applied", result.ResultCode);
            Assert.False(_authority.GetAuthority(_account, _stone).IsVacant);
        }

        [Fact]
        public void CreateAttunement_InsideStoneArea_Applies()
        {
            _positions.Publish(_character, StoneX, StoneZ);

            var result = NewHandler().Handle(Cmd("op-att-in", RelationshipCommandType.CreateAttunement, "rel-8"));

            Assert.Equal(RelationshipCommandOutcome.Applied, result.Outcome);
        }

        // ── Scope: what the gate deliberately does NOT cover ───────────────────────────────────────

        [Fact]
        public void ReleaseRelationship_IsNotProximityGated()
        {
            _positions.Publish(_character, StoneX, StoneZ);
            var handler = NewHandler();
            Assert.Equal(RelationshipCommandOutcome.Applied,
                handler.Handle(Cmd("op-bond-rel", RelationshipCommandType.CreateBond, "rel-9")).Outcome);

            // Walk far away, then release. Releasing is not the proximate act: gating it would strand a
            // character who left the Stone.
            _positions.Publish(_character, StoneX + 9000.0, StoneZ + 9000.0);

            var release = handler.Handle(Cmd("op-release", RelationshipCommandType.ReleaseRelationship, "rel-9"));

            Assert.NotEqual("NotAtStone", release.ResultCode);
            Assert.Equal(RelationshipCommandOutcome.Applied, release.Outcome);
        }

        [Fact]
        public void CommittedOperation_StillReplays_AfterTheActorWalksAway()
        {
            _positions.Publish(_character, StoneX, StoneZ);
            var handler = NewHandler();
            var applied = handler.Handle(Cmd("op-replay", RelationshipCommandType.CreateBond, "rel-10"));
            Assert.Equal(RelationshipCommandOutcome.Applied, applied.Outcome);

            // The acknowledgement was lost and the client retried the SAME operation — from elsewhere.
            _positions.Publish(_character, StoneX + 9000.0, StoneZ);

            var replay = handler.Handle(Cmd("op-replay", RelationshipCommandType.CreateBond, "rel-10"));

            Assert.Equal(RelationshipCommandOutcome.Replayed, replay.Outcome);
            Assert.Equal("Applied", replay.ResultCode);
        }

        // ── Composition ────────────────────────────────────────────────────────────────────────────

        [Fact]
        public void NullProximityAuthority_ThrowsAtConstruction()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new RelationshipCommandHandler(_journalPath, new PrincipalResolver(), _characters, _authority,
                    _families, new ProxBondPolicy(), null!));
        }

        [Fact]
        public void DenyAllAuthority_IsAVisibleClosedDoor()
        {
            _positions.Publish(_character, StoneX, StoneZ);
            var handler = new RelationshipCommandHandler(_journalPath, new PrincipalResolver(), _characters,
                _authority, _families, new ProxBondPolicy(), DenyAllStoneProximityAuthority.Instance);

            Assert.Equal("NotAtStone",
                handler.Handle(Cmd("op-denyall", RelationshipCommandType.CreateBond, "rel-11")).ResultCode);
        }

        // ── The position index itself ──────────────────────────────────────────────────────────────

        [Fact]
        public void PositionIndex_PublishRefreshesClearRemovesAndEmptyIdIsIgnored()
        {
            var index = new ServerObservedCharacterPositions();
            Assert.False(index.TryGetPosition(_character, out _, out _));

            index.Publish(_character, 1.0, 2.0);
            Assert.True(index.TryGetPosition(_character, out double x, out double z));
            Assert.Equal(1.0, x);
            Assert.Equal(2.0, z);

            index.Publish(_character, 3.0, 4.0);
            Assert.True(index.TryGetPosition(_character, out x, out z));
            Assert.Equal(3.0, x);
            Assert.Equal(4.0, z);
            Assert.Equal(1, index.ObservedCount);

            index.Publish(new CharacterId(string.Empty), 9.0, 9.0);
            Assert.Equal(1, index.ObservedCount);

            index.Clear(_character);
            Assert.False(index.TryGetPosition(_character, out _, out _));
            index.Clear(_character);   // idempotent
            Assert.Equal(0, index.ObservedCount);
        }

        [Fact]
        public void ProximityAuthority_RejectsAnEmptyStoneId()
        {
            _positions.Publish(_character, StoneX, StoneZ);
            var authority = new StoneAreaProximityAuthority(_areas, _positions);

            Assert.False(authority.IsAtStone(
                new AuthoritativePrincipal(_account, _character), default));
        }

        // ── Doubles ────────────────────────────────────────────────────────────────────────────────

        private sealed class ProxFamilyResolver : IStoneFamilyResolver
        {
            private readonly Dictionary<string, KeyValuePair<string, string>> _map =
                new Dictionary<string, KeyValuePair<string, string>>(StringComparer.Ordinal);

            public void Set(StoneId stone, string family, string variant) =>
                _map[stone.Value] = new KeyValuePair<string, string>(family, variant);

            public bool TryGetClassification(StoneId stoneId, out string family, out string variant)
            {
                if (_map.TryGetValue(stoneId.Value ?? string.Empty, out var v))
                {
                    family = v.Key; variant = v.Value; return true;
                }
                family = variant = string.Empty;
                return false;
            }
        }

        private sealed class ProxBondPolicy : IBondAuthorityPolicy
        {
            public bool TryAuthorizeBond(StoneId stoneId, string requestedResponsibilityRange,
                out string grantedRange, out string grantedRole)
            {
                grantedRange = string.Empty;
                grantedRole = string.Empty;
                if (!string.Equals(requestedResponsibilityRange, "Homestead:All", StringComparison.Ordinal))
                    return false;
                grantedRange = requestedResponsibilityRange;
                grantedRole = "Governor";
                return true;
            }
        }
    }
}
