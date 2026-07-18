using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;

namespace SBPR.Niflheim.HomesteadStones.Application.Accounts
{
    // IAP-005 Tracer 2 — the ephemeral, process-local admission index (engine-free CLEAN-side core).
    //
    // This owns the "at most one pending admission OR active session per account" rule (spec closed-pilot
    // decision #5, AIP-FR-013; data-model.md "Ephemeral index — AccountAdmissionIndex"). It is
    // DELIBERATELY NON-DURABLE: no journal record, no receipt, no revision. Its guarantees are purely
    // process-local:
    //
    //   * atomic reservation      — a PendingAdmission lease is reserved atomically immediately after
    //                               account resolution and BEFORE any profile lookup or character mint;
    //   * one-session exclusion   — a second session (even a sibling profile of the same account) that
    //                               tries to reserve while a pending/active lease exists rejects as
    //                               AccountAlreadyConnected without touching durable state;
    //   * idempotent same-session — the exact holder retrying its own reservation gets the same lease;
    //   * matching-session release— disconnect/close removes ONLY a lease whose (AccountId, SessionId,
    //                               transportHandle) all match, so a STALE disconnect cannot close a
    //                               NEWER admission/session (AT-AIP-STALE-DISCONNECT);
    //   * restart clears it       — it lives in memory only; a server restart drops every lease while the
    //                               durable account/character journal survives.
    //
    // net48 audit: only System.* + generics. No UnityEngine/Valheim/BepInEx — link-compiles under net8
    // and ships under net48 exactly like the rest of the pilot account layer.

    /// <summary>Whether a reserved lease has been promoted to an active gameplay session yet.</summary>
    public enum AdmissionPhase { PendingAdmission, Active }

    /// <summary>One ephemeral admission lease. Carries no durable identity beyond the internal ids it
    /// references; it is never journaled and is dropped on restart.</summary>
    public sealed class AdmissionLease
    {
        public PilotAccountId AccountId { get; }
        public SessionId SessionId { get; }
        public long TransportHandle { get; }
        public AdmissionPhase Phase { get; internal set; }
        public PilotCharacterId CharacterId { get; internal set; }
        public long AdmittedAt { get; }

        internal AdmissionLease(PilotAccountId accountId, SessionId sessionId, long transportHandle, long admittedAt)
        {
            AccountId = accountId;
            SessionId = sessionId;
            TransportHandle = transportHandle;
            Phase = AdmissionPhase.PendingAdmission;
            CharacterId = default;
            AdmittedAt = admittedAt;
        }
    }

    /// <summary>Outcome of a reservation attempt. <c>Reserved</c> and <c>AlreadyHeldBySameSession</c> both
    /// carry the winning lease; <c>AlreadyConnected</c> means another session holds this account.</summary>
    public enum AdmissionReservationOutcome { Reserved, AlreadyHeldBySameSession, AlreadyConnected }

    public readonly struct AdmissionReservation
    {
        public AdmissionReservationOutcome Outcome { get; }
        public AdmissionLease? Lease { get; }

        public AdmissionReservation(AdmissionReservationOutcome outcome, AdmissionLease? lease)
        {
            Outcome = outcome;
            Lease = lease;
        }

        public bool Granted => Outcome != AdmissionReservationOutcome.AlreadyConnected;
    }

    /// <summary>The process-local admission index. Every mutation is serialized under one lock so a race
    /// between two sibling-profile connections resolves to exactly one winner (AT-AIP-ADMISSION-LEASE-RACE,
    /// AT-AIP-ONE-SESSION). It is intentionally the only authority for concurrent-session exclusion; the
    /// durable journal never records a lease.</summary>
    public sealed class AccountAdmissionIndex
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, AdmissionLease> _leases = new Dictionary<string, AdmissionLease>(StringComparer.Ordinal);

        /// <summary>Atomically reserve a PendingAdmission lease for the account, or reject because another
        /// session already holds a pending/active lease. The exact same (session, transport) retrying its
        /// own reservation is idempotent and returns the existing lease.</summary>
        public AdmissionReservation TryReserve(PilotAccountId accountId, SessionId sessionId, long transportHandle, long admittedAt)
        {
            lock (_gate)
            {
                if (_leases.TryGetValue(accountId.Value, out var existing))
                {
                    if (existing.SessionId.Equals(sessionId) && existing.TransportHandle == transportHandle)
                        return new AdmissionReservation(AdmissionReservationOutcome.AlreadyHeldBySameSession, existing);
                    return new AdmissionReservation(AdmissionReservationOutcome.AlreadyConnected, null);
                }
                var lease = new AdmissionLease(accountId, sessionId, transportHandle, admittedAt);
                _leases[accountId.Value] = lease;
                return new AdmissionReservation(AdmissionReservationOutcome.Reserved, lease);
            }
        }

        /// <summary>Return the account's current lease iff it is held by exactly this session/transport.
        /// Used by character mint/activation to prove the caller holds the account's matching pending
        /// lease (contracts.md "caller holds the account's matching pending lease").</summary>
        public bool TryGetHeldLease(PilotAccountId accountId, SessionId sessionId, long transportHandle, out AdmissionLease lease)
        {
            lock (_gate)
            {
                if (_leases.TryGetValue(accountId.Value, out var existing) &&
                    existing.SessionId.Equals(sessionId) && existing.TransportHandle == transportHandle)
                {
                    lease = existing;
                    return true;
                }
                lease = null!;
                return false;
            }
        }

        /// <summary>Promote the matching pending lease to Active, stamping the resolved CharacterId.
        /// Rejects (returns false) if no matching pending lease exists.</summary>
        public bool TryActivate(PilotAccountId accountId, SessionId sessionId, long transportHandle, PilotCharacterId characterId)
        {
            lock (_gate)
            {
                if (_leases.TryGetValue(accountId.Value, out var existing) &&
                    existing.SessionId.Equals(sessionId) && existing.TransportHandle == transportHandle)
                {
                    existing.Phase = AdmissionPhase.Active;
                    existing.CharacterId = characterId;
                    return true;
                }
                return false;
            }
        }

        /// <summary>Release ONLY a lease whose account, session, and transport all match. A stale
        /// disconnect carrying an older SessionId/handle finds no match against a newer lease and closes
        /// nothing (AT-AIP-STALE-DISCONNECT). Returns true only when a lease was actually removed.</summary>
        public bool TryRelease(PilotAccountId accountId, SessionId sessionId, long transportHandle)
        {
            lock (_gate)
            {
                if (_leases.TryGetValue(accountId.Value, out var existing) &&
                    existing.SessionId.Equals(sessionId) && existing.TransportHandle == transportHandle)
                {
                    _leases.Remove(accountId.Value);
                    return true;
                }
                return false;
            }
        }

        /// <summary>True when the account currently has any pending/active lease.</summary>
        public bool HasLease(PilotAccountId accountId)
        {
            lock (_gate) { return _leases.ContainsKey(accountId.Value); }
        }

        /// <summary>Live lease count (test/operator visibility). Cleared to zero on restart by construction
        /// (a fresh index).</summary>
        public int ActiveLeaseCount
        {
            get { lock (_gate) { return _leases.Count; } }
        }
    }
}
