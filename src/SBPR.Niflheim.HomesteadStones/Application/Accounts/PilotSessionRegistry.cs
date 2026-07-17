using System;
using System.Collections.Generic;

namespace SBPR.Niflheim.HomesteadStones.Application.Accounts
{
    // IAP-009 Operator foundation — the process-local session registry (engine-free CLEAN core).
    //
    // Tracks at most one pending-or-active session per AccountId (AIP-FR-013) and provides the
    // DETERMINISTIC session close/kick the operator disable/delete path relies on (contracts
    // ClosePilotSession, DisablePilotAccount: "server-close the active session"). Two properties matter:
    //
    //  * A close/kick targeting an account removes ONLY the currently-registered session for that account
    //    and returns the transport handle to close, so the net48 host closes exactly one deterministic
    //    connection — never a guess.
    //  * A stale disconnect cannot close a NEWER admission/session: CloseMatching only removes an entry
    //    whose (accountId, sessionId, transportHandle) all match. A late close for a prior session whose
    //    sessionId/handle no longer match is a no-op, so a reconnect that already replaced the session is
    //    never torn down by the old connection's delayed close (spec edge "stale disconnect").
    //
    // The registry is intentionally NON-durable (AIP-FR-016: admission leases are process-local, race-safe,
    // cleared on restart). It carries no journal revision and is rebuilt empty on boot.
    //
    // net48 audit: System.Collections.Generic only. No UnityEngine / Valheim / BepInEx.

    public enum PilotSessionState { Pending, Active }

    /// <summary>One process-local session lease for an account. Opaque transport handle is what the net48
    /// host closes; sessionId is the server-minted admission identity.</summary>
    public readonly struct PilotSession
    {
        public PilotSession(string accountId, string sessionId, long transportHandle, PilotSessionState state)
        {
            AccountId = accountId ?? string.Empty;
            SessionId = sessionId ?? string.Empty;
            TransportHandle = transportHandle;
            State = state;
        }

        public string AccountId { get; }
        public string SessionId { get; }
        public long TransportHandle { get; }
        public PilotSessionState State { get; }

        public bool IsEmpty => string.IsNullOrEmpty(AccountId) && string.IsNullOrEmpty(SessionId);
        public static PilotSession None => default;
    }

    /// <summary>The outcome of a deterministic close: whether a session was present and, if so, the exact
    /// transport handle the host must close. A close of an account with no session is a clean no-op.</summary>
    public readonly struct SessionCloseResult
    {
        public SessionCloseResult(bool closed, long transportHandle, string sessionId)
        {
            Closed = closed;
            TransportHandle = transportHandle;
            SessionId = sessionId ?? string.Empty;
        }

        public bool Closed { get; }
        public long TransportHandle { get; }
        public string SessionId { get; }

        public static SessionCloseResult NoSession => new SessionCloseResult(false, 0L, string.Empty);
    }

    /// <summary>Process-local, one-session-per-account registry. Thread-safe under a single lock so a
    /// reserve, promote, and operator close cannot interleave into a torn state.</summary>
    public sealed class PilotSessionRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, PilotSession> _byAccount = new Dictionary<string, PilotSession>(StringComparer.Ordinal);

        /// <summary>Reserve the single pending admission for an account. Returns false (no change) if the
        /// account already has a pending/active session held by a DIFFERENT session id
        /// (AccountAlreadyConnected). A same-session retry returns the existing lease.</summary>
        public bool TryReservePending(string accountId, string sessionId, long transportHandle)
        {
            lock (_sync)
            {
                if (_byAccount.TryGetValue(accountId, out var existing))
                    return string.Equals(existing.SessionId, sessionId, StringComparison.Ordinal);
                _byAccount[accountId] = new PilotSession(accountId, sessionId, transportHandle, PilotSessionState.Pending);
                return true;
            }
        }

        /// <summary>Promote a matching pending lease to active. Only the exact (account, session, handle)
        /// pending lease promotes; a mismatch is a no-op returning false.</summary>
        public bool TryActivate(string accountId, string sessionId, long transportHandle)
        {
            lock (_sync)
            {
                if (_byAccount.TryGetValue(accountId, out var existing) &&
                    existing.State == PilotSessionState.Pending &&
                    string.Equals(existing.SessionId, sessionId, StringComparison.Ordinal) &&
                    existing.TransportHandle == transportHandle)
                {
                    _byAccount[accountId] = new PilotSession(accountId, sessionId, transportHandle, PilotSessionState.Active);
                    return true;
                }
                return false;
            }
        }

        public bool TryGet(string accountId, out PilotSession session)
        {
            lock (_sync) { return _byAccount.TryGetValue(accountId, out session); }
        }

        public bool HasSession(string accountId)
        {
            lock (_sync) { return _byAccount.ContainsKey(accountId); }
        }

        /// <summary>Deterministically close whatever session an account currently holds (operator disable/
        /// delete path). Returns the exact transport handle to close, or NoSession if the account is idle.
        /// This is the "server-close the active session" step — it does not care about the closing
        /// caller's own handle because the OPERATOR, not the peer, is ending the session.</summary>
        public SessionCloseResult CloseForAccount(string accountId)
        {
            lock (_sync)
            {
                if (_byAccount.TryGetValue(accountId, out var s))
                {
                    _byAccount.Remove(accountId);
                    return new SessionCloseResult(true, s.TransportHandle, s.SessionId);
                }
                return SessionCloseResult.NoSession;
            }
        }

        /// <summary>Close a session ONLY when (account, session, handle) all match — the stale-disconnect
        /// guard. A late close for a superseded session whose id/handle no longer match is a no-op, so a
        /// newer admission is never torn down by an old connection's delayed close (spec edge case).</summary>
        public SessionCloseResult CloseMatching(string accountId, string sessionId, long transportHandle)
        {
            lock (_sync)
            {
                if (_byAccount.TryGetValue(accountId, out var s) &&
                    string.Equals(s.SessionId, sessionId, StringComparison.Ordinal) &&
                    s.TransportHandle == transportHandle)
                {
                    _byAccount.Remove(accountId);
                    return new SessionCloseResult(true, s.TransportHandle, s.SessionId);
                }
                return SessionCloseResult.NoSession;
            }
        }

        public int ActiveSessionCount { get { lock (_sync) { return _byAccount.Count; } } }
    }
}
