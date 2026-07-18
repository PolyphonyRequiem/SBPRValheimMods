using System;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // T009R3 (Blocker 3) — the smallest bounded, server-authoritative relationship provisioning ingress.
    //
    // Why this exists: FoundationalProgressionServer.Create composes the shipped RelationshipCommandHandler
    // over EMPTY character/authority projections and only rehydrates the relationship journal. Nothing in
    // the live server can ESTABLISH the Bond/Attunement that RecordFoundationalPlacement requires, so
    // T009L cannot reach a single credited placement in a real session. This ingress adds the one missing
    // seam: a server-derived principal drives the SHIPPED RelationshipCommandHandler to seed a character
    // aggregate (if absent) and create an Attunement/Bond — no permissive authorizer, no client-supplied
    // identity, no fabricated projection mutation. It is a thin front-end over the existing command path;
    // every invariant (slot capacity, sibling exclusivity, revisions, recovery) is still the handler's.
    //
    // Identity: the caller (the net48 admin/test seam) resolves the target from server-owned facts (the
    // authenticated character's s_playerID + stable character ZDOID via AuthenticatedSenderBinder), then
    // passes them here as the AuthoritativeSubject. This ingress NEVER trusts a client-supplied account or
    // character; it binds the command to the server-derived AuthenticatedConnection and leaves the claim
    // empty (nothing to compare), exactly like the placement runtime.
    //
    // Restriction: this ingress is only ever constructed and driven by the net48
    // RelationshipProvisioningAdmin seam, which is gated behind an explicit server-owned config flag AND
    // Valheim admin authority (see Features/Progression/RelationshipProvisioningAdmin.cs). Disabled by
    // default; it is a playtest-provisioning path, not a shipping gameplay command.
    //
    // net48 audit: value objects + the shipped engine-free command handler/stores only. No net5+ surface,
    // no UnityEngine/Valheim, so it link-compiles into the net8 test project and every branch is tested.
    public sealed class RelationshipProvisioningIngress
    {
        private readonly RelationshipCommandHandler _relationships;
        private readonly ICharacterAggregateStore _characters;

        // Provisional playtest defaults: the shipped Homestead proof grants 1 Bond + 2 Attunement slots
        // (matching the T009R2 test fixtures and the Settlement/Homestead policy). A seeded character is
        // an empty aggregate at revision 0 with one zeroed record for the target Stone.
        private const int DefaultBondSlots = 1;
        private const int DefaultAttunementSlots = 2;

        public RelationshipProvisioningIngress(
            RelationshipCommandHandler relationships,
            ICharacterAggregateStore characters)
        {
            _relationships = relationships ?? throw new ArgumentNullException(nameof(relationships));
            _characters = characters ?? throw new ArgumentNullException(nameof(characters));
        }

        /// <summary>Provision (seed-if-absent + create) a relationship for a server-derived subject.
        /// <paramref name="subject"/> carries the server-owned account/character identity the caller
        /// derived from the authenticated peer (never a client claim). <paramref name="operationId"/> is
        /// caller-supplied and idempotent: re-running the same provisioning converges (Replayed), and a
        /// conflicting reuse rejects — the shipped handler owns that. Returns the handler's result.</summary>
        public RelationshipProvisioningResult Provision(
            AuthoritativeSubject subject,
            StoneId stoneId,
            RelationshipCommandType commandType,
            string operationId,
            string relationshipId,
            string worldProductScope,
            string requestedResponsibilityRange = "")
        {
            if (string.IsNullOrEmpty(operationId))
                return RelationshipProvisioningResult.Rejected("MissingOperationId");
            if (string.IsNullOrEmpty(subject.Account.Value) || string.IsNullOrEmpty(subject.Character.Value))
                return RelationshipProvisioningResult.Rejected("Unauthenticated");
            if (commandType != RelationshipCommandType.CreateBond &&
                commandType != RelationshipCommandType.CreateAttunement)
                return RelationshipProvisioningResult.Rejected("UnsupportedProvisioningCommand");

            // Seed the character aggregate ONLY when absent — never overwrite existing progression. This
            // is not a projection mutation of relationship/AP state; it is the empty owner row the handler
            // needs to exist before it can transition (the handler rejects CharacterNotFound otherwise).
            if (_characters.GetCharacter(subject.Account, subject.Character) == null)
            {
                _characters.PutCharacter(new CharacterProgressionAggregate(
                    subject.Account, subject.Character,
                    worldProductScope: worldProductScope ?? string.Empty, revision: 0,
                    bondSlots: DefaultBondSlots, attunementSlots: DefaultAttunementSlots,
                    lastAppliedReceiptId: "provisioned",
                    stoneRecords: new[] { new CharacterStoneRecord(stoneId, 0, 0, 0, null, null) }));
            }

            // Drive the SHIPPED handler with the server-derived connection identity. The claim is empty:
            // there is no untrusted client payload to compare, so PrincipalResolver binds from the
            // connection alone (identical to the placement runtime's authority model).
            var connection = new AuthenticatedConnection(subject.Account.Value, subject.Character.Value);
            var command = new RelationshipCommand(
                new OperationId(operationId), commandType, stoneId,
                connection, default, relationshipId,
                responsibilityRange: requestedResponsibilityRange ?? string.Empty);

            var result = _relationships.Handle(command);
            return RelationshipProvisioningResult.FromCommand(result);
        }
    }

    /// <summary>A server-derived provisioning subject: the authoritative account + stable character id the
    /// net48 admin seam resolved from the authenticated peer's server-owned character facts. There is no
    /// client-supplied identity here — the caller must have derived both from server state.</summary>
    public readonly struct AuthoritativeSubject
    {
        public AuthoritativeSubject(AccountId account, CharacterId character)
        {
            Account = account;
            Character = character;
        }

        public AccountId Account { get; }
        public CharacterId Character { get; }
    }

    /// <summary>T009R4 (Blocker 3) — engine-free, deterministic provisioning operation-id / relationship-id
    /// derivation that binds ALL material fields (account, stable character, Stone, command, requested
    /// range, world scope). Exact retries produce the SAME op id → the shipped handler replays; any changed
    /// binding (different Stone, command, range, or world) produces a DISTINCT op id that conflicts
    /// intentionally at the receipt layer rather than silently double-applying. Isolated here so the
    /// binding property is unit-tested independent of the net48 admin seam that calls it.</summary>
    public static class ProvisioningOperationBinding
    {
        public static string OperationId(
            string account, string character, StoneId stoneId, RelationshipCommandType commandType,
            string requestedRange, string worldScope) =>
            "op-provision-" + string.Join("|", new[]
            {
                ((int)commandType).ToString(System.Globalization.CultureInfo.InvariantCulture),
                account ?? string.Empty,
                character ?? string.Empty,
                stoneId.Value ?? string.Empty,
                requestedRange ?? string.Empty,
                worldScope ?? string.Empty
            });

        /// <summary>Relationship id bound to the command + stable character (durable across reconnect, so a
        /// reconnected character resolves to the same relationship rather than orphaning it).</summary>
        public static string RelationshipId(string character, RelationshipCommandType commandType) =>
            "rel-provision-" + ((int)commandType).ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "-" + (character ?? string.Empty);

        /// <summary>A PII-free, stable pseudonymous correlation tag for operator logs, derived from the
        /// BOUND INTERNAL (account, character) — which are already server-minted opaque ids, never a
        /// provider/socket subject or a raw s_playerID. We still emit only a short SHA-256 digest so no
        /// gameplay subject value is echoed verbatim into the operator log or any export path (T009L2
        /// Blocker 1: no raw provider/profile subject in the provisioning log). The same input always yields
        /// the same tag, so an operator can correlate repeated provisioning of one character.</summary>
        public static string CorrelationTag(string account, string character)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] h = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(
                    (account ?? string.Empty) + "|" + (character ?? string.Empty)));
                var sb = new System.Text.StringBuilder(12);
                for (int i = 0; i < 6; i++) sb.Append(h[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return "corr-" + sb.ToString();
            }
        }
    }

    /// <summary>Outcome of a provisioning attempt: either a pre-command rejection (no handler call), or the
    /// shipped handler's terminal result.</summary>
    public readonly struct RelationshipProvisioningResult
    {
        private RelationshipProvisioningResult(bool routed, RelationshipCommandOutcome outcome,
            string resultCode, string relationshipId)
        {
            Routed = routed;
            Outcome = outcome;
            ResultCode = resultCode ?? string.Empty;
            RelationshipId = relationshipId ?? string.Empty;
        }

        /// <summary>True when the ingress reached the shipped command handler.</summary>
        public bool Routed { get; }
        public RelationshipCommandOutcome Outcome { get; }
        public string ResultCode { get; }
        public string RelationshipId { get; }

        public bool Established => Routed &&
            (Outcome == RelationshipCommandOutcome.Applied || Outcome == RelationshipCommandOutcome.Replayed);

        internal static RelationshipProvisioningResult Rejected(string code) =>
            new RelationshipProvisioningResult(false, RelationshipCommandOutcome.Rejected, code, string.Empty);

        internal static RelationshipProvisioningResult FromCommand(RelationshipCommandResult r) =>
            new RelationshipProvisioningResult(true, r.Outcome, r.ResultCode, r.RelationshipId);

        /// <summary>One-line, PII-free operator rendering.</summary>
        public string ToOperatorLine() =>
            Routed
                ? $"[relationship-provisioning] outcome={Outcome} result={ResultCode} rel={RelationshipId}"
                : $"[relationship-provisioning] rejected={ResultCode}";
    }
}
