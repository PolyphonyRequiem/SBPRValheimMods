// ============================================================================
//  QA-M3 fixture authority recheck (canonical, t_4db82cc0) — engine-free.
// ----------------------------------------------------------------------------
//  FixtureAuthority + FixtureProvisioner — the execution-time gate ADR-0009 §5.1
//  ("delivering-peer binding + admin recheck AT EXECUTION, not just at arm") for
//  the owned-resource lifecycle. Arming (M1) proves the run MAY provision fixtures;
//  this gate re-proves, at the instant Ensure/Cleanup runs, that the caller is
//  STILL the authoritative server, STILL admin/owner, and the request STILL comes
//  from the bound delivering peer on the current connection generation.
//
//  Why re-check at execution: a peer can lose admin, disconnect+reconnect (a new
//  connection generation), or be substituted between arm and execution. Fail-closed
//  at the moment of the world side effect is the only safe point — a stale
//  authority snapshot from arm time is not enough (T9/T12).
//
//  This is pure decision + orchestration logic. It drives the engine-free
//  OwnedResourceLedger; the real IServerAuthoritySource / IVanillaFixtureSeam
//  adapters are supplied by the net48 helper. No product identity/AP/ownership/
//  signature/verdict — this only decides WHO may run the vanilla lifecycle.
//
//  Engine-free: System.* only.
// ============================================================================

using System;
using SBPR.QaHarness.T022.Core.ControlPlane;

namespace SBPR.QaHarness.T022.Core.Fixtures
{
    /// <summary>Why a fixture lifecycle operation was refused at execution time. None is the only accept.</summary>
    public enum FixtureAuthorityReason
    {
        None = 0,
        NotServerRole,        // caller is not the authoritative server (fixtures are Server-role only)
        WorldNotLoaded,       // ZNet has no world yet — fixtures only run post world-load
        NotAdmin,             // the delivering peer is not admin/owner at execution time
        PeerRejected,         // delivering-peer binding/generation check failed (substitution or stale)
    }

    /// <summary>
    /// The execution-time authority facts the server re-reads for a fixture op. Observed-only —
    /// the helper's net48 adapter fills these from ZNet.IsServer()/world-load/adminlist and the
    /// bound peer; the engine-free gate only DECIDES on them.
    /// </summary>
    public interface IServerAuthoritySource
    {
        /// <summary>True iff this process is the authoritative server right now (ZNet.IsServer()).</summary>
        bool IsServer { get; }

        /// <summary>True iff a world is loaded (fixtures NRE before world load — §3.1/§3.5).</summary>
        bool WorldLoaded { get; }

        /// <summary>True iff the given delivering peer id is admin/owner right now (re-read, not cached).</summary>
        bool IsAdmin(string deliveringPeerId);
    }

    /// <summary>The outcome of a fixture authority recheck.</summary>
    public sealed class FixtureAuthorityDecision
    {
        public FixtureAuthorityReason Reason { get; }
        public bool Ok => Reason == FixtureAuthorityReason.None;

        private FixtureAuthorityDecision(FixtureAuthorityReason reason) => Reason = reason;

        public static readonly FixtureAuthorityDecision Accept = new(FixtureAuthorityReason.None);
        public static FixtureAuthorityDecision Reject(FixtureAuthorityReason reason) => new(reason);
    }

    /// <summary>
    /// Re-checks, at execution time, that a fixture lifecycle op may run: authoritative server,
    /// world loaded, delivering peer bound+current+admin. Fail-closed and fixed-order.
    /// </summary>
    public static class FixtureAuthority
    {
        /// <summary>
        /// Decide whether a fixture lifecycle op (Ensure/Cleanup) may execute now. <paramref name="peerState"/>
        /// is the current delivering-peer/generation state; <paramref name="deliveringPeerId"/> is the ACTUAL
        /// peer the transport observed; <paramref name="claimedGeneration"/> is the request's asserted generation.
        /// </summary>
        public static FixtureAuthorityDecision Recheck(
            IServerAuthoritySource authority,
            DeliveringPeerState peerState,
            string? deliveringPeerId,
            long claimedGeneration)
        {
            if (authority == null) throw new ArgumentNullException(nameof(authority));
            if (peerState == null) throw new ArgumentNullException(nameof(peerState));

            // 1. Server role — a fixture is never a client-side op.
            if (!authority.IsServer)
                return FixtureAuthorityDecision.Reject(FixtureAuthorityReason.NotServerRole);

            // 2. World loaded — creating/destroying before world load is undefined (NRE).
            if (!authority.WorldLoaded)
                return FixtureAuthorityDecision.Reject(FixtureAuthorityReason.WorldNotLoaded);

            // 3. Delivering-peer binding + connection generation (substitution / stale-replay).
            var admit = peerState.Validate(deliveringPeerId, claimedGeneration);
            if (!admit.Ok)
                return FixtureAuthorityDecision.Reject(FixtureAuthorityReason.PeerRejected);

            // 4. Admin/owner RE-READ at execution (not cached from arm time).
            if (!authority.IsAdmin(deliveringPeerId!))
                return FixtureAuthorityDecision.Reject(FixtureAuthorityReason.NotAdmin);

            return FixtureAuthorityDecision.Accept;
        }
    }

    /// <summary>
    /// Orchestrates a gated fixture lifecycle: recheck authority, THEN drive the ledger's
    /// Ensure/Cleanup. A refused recheck performs NO world side effect (the ledger is not touched),
    /// so an unauthorized caller can neither create nor delete anything.
    /// </summary>
    public sealed class FixtureProvisioner
    {
        private readonly IServerAuthoritySource _authority;
        private readonly DeliveringPeerState _peerState;
        private readonly IFixtureWorld _world;

        public FixtureProvisioner(IServerAuthoritySource authority, DeliveringPeerState peerState, IFixtureWorld world)
        {
            _authority = authority ?? throw new ArgumentNullException(nameof(authority));
            _peerState = peerState ?? throw new ArgumentNullException(nameof(peerState));
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        /// <summary>The last authority decision (for receipt/telemetry). None until a call is made.</summary>
        public FixtureAuthorityDecision LastDecision { get; private set; } = FixtureAuthorityDecision.Accept;

        /// <summary>Gated idempotent ensure. Returns null (and sets LastDecision) when the recheck refuses.</summary>
        public EnsureResult? Ensure(OwnedResourceLedger ledger, string? deliveringPeerId, long claimedGeneration)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            var decision = FixtureAuthority.Recheck(_authority, _peerState, deliveringPeerId, claimedGeneration);
            LastDecision = decision;
            if (!decision.Ok) return null;   // fail closed: no world side effect
            return ledger.Ensure(_world);
        }

        /// <summary>Gated cleanup. Returns null (and sets LastDecision) when the recheck refuses.</summary>
        public CleanupResult? Cleanup(OwnedResourceLedger ledger, string? deliveringPeerId, long claimedGeneration)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            var decision = FixtureAuthority.Recheck(_authority, _peerState, deliveringPeerId, claimedGeneration);
            LastDecision = decision;
            if (!decision.Ok) return null;   // fail closed: no world side effect
            return ledger.Cleanup(_world);
        }
    }
}
