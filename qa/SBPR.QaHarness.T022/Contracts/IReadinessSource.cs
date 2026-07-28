// Arm-time readiness seams (ADR-0009, spec-role-split-arm-gate.md §3/§4) — role-split arm gate.
//
// These are ENGINE-FREE, single-member interfaces (System.* only) that name the ONE question a
// readiness source answers at arm time. They exist so the type of a source names its question and
// a client source can never be mistaken for a server source (spec AC4). The engine-bound impls live
// in Runtime/ (they touch ZNet/Player/Harmony); these declarations carry no game reference so the
// net8 tests-core suite and the net48 helper compile the SAME source — one definition, no fork.
//
// NOTE: this is the ARM-TIME readiness signal (when may TryArm run), distinct from the
// EXECUTION-TIME IServerAuthoritySource (per-op authority) — see spec §1. Naming is deliberately
// "...ReadinessSource" to keep it apart from "...AuthoritySource".
namespace SBPR.QaHarness.T022.Core
{
    /// <summary>
    /// Shared arm-time readiness contract: <see cref="Ready"/> is true once this source's ONE
    /// readiness condition holds. Fail-closed: any doubt yields false. The two role-named
    /// sub-interfaces below MUST both exist so a source's TYPE names the question it answers.
    /// </summary>
    public interface IReadinessSource
    {
        /// <summary>True once this source's readiness condition holds; fail-closed (false on any doubt).</summary>
        bool Ready { get; }
    }

    /// <summary>
    /// Server-role arm-time readiness: <see cref="IReadinessSource.Ready"/> == "the authoritative
    /// world is loaded" (ZNet.World != null). Bound by the existing world-identity adapter; its
    /// predicate is unchanged by the role split (spec AC1).
    /// </summary>
    public interface IServerReadinessSource : IReadinessSource
    {
    }

    /// <summary>
    /// Client-role arm-time readiness: <see cref="IReadinessSource.Ready"/> == "this is a client
    /// instance joined to a remote server AND a local player has spawned in-world" (spec AC2).
    /// Ready only when the role predicate (!IsServer) AND an event-driven spawned-player flag AND
    /// a live re-read of the local player all hold. SP/host return IsServer==true and are therefore
    /// never ready via this source (spec AC3).
    /// </summary>
    public interface IClientReadinessSource : IReadinessSource
    {
    }

    /// <summary>
    /// Engine-free client-readiness decision (spec AC2/AC5, §6): ready IFF the client role predicate
    /// holds AND a local player has spawned AND that local player is still live. Factored out of the
    /// engine-bound <c>ZNetClientReadinessSource</c> so the AND logic is unit-testable headlessly
    /// against three injected booleans (no ZNet/Player statics), mirroring how the fixture authority
    /// is faked. Fail-closed is the caller's job: any exception reading the three inputs must be
    /// turned into <c>false</c> before calling here (or simply pass false).
    /// </summary>
    public static class ClientReadinessDecision
    {
        /// <summary>
        /// True only when ALL three hold: <paramref name="clientRole"/> (this is a client instance
        /// joined to a remote server, i.e. !IsServer), <paramref name="spawnedFlag"/> (the
        /// Player.OnSpawned postfix has fired), and <paramref name="livePlayer"/> (a live re-read of
        /// Player.m_localPlayer != null, guarding a spawned-then-destroyed player).
        /// </summary>
        public static bool Ready(bool clientRole, bool spawnedFlag, bool livePlayer)
            => clientRole && spawnedFlag && livePlayer;
    }
}
