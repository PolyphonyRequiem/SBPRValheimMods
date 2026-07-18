using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // IAP-007 Tracer 3 — the process-local bound-session principal index (engine-free CLEAN core).
    //
    // This is the seam that removes provider/profile identity from the LIVE gameplay hot path
    // (AIP-FR-014/018, AT-AIP-NO-PROVIDER-HOTPATH). Admission (Tracer 1/2) mints and binds the
    // internal AccountId/CharacterId for a connected peer; it publishes that binding here keyed by a
    // server-owned, durable peer key (the character's s_playerID rendered stable). The live placement
    // observer then resolves the acting peer's BOUND INTERNAL principal from this index — it never
    // derives a gameplay principal from a raw provider subject, performs no provider lookup, and makes
    // no network call.
    //
    // It is DELIBERATELY NON-DURABLE (mirrors AccountAdmissionIndex): no journal record, no receipt,
    // no revision. A restart clears it; admission republishes on reconnect. A peer with NO bound
    // internal session resolves to nothing, and the gameplay path FAILS CLOSED (credits nothing)
    // rather than falling back to a provider/platform identity — there is no candidate-A fallback any
    // more.
    //
    // net48 audit: only System.* + generics + the engine-free identity value objects. No
    // UnityEngine/Valheim/BepInEx — link-compiles under net8 and ships under net48.

    /// <summary>Server-owned read port resolving an authenticated peer key to its bound internal
    /// gameplay session principal. The net48 layer keys it by the durable s_playerID; tests key it
    /// with any stable string. Returns false when the peer has no admitted, bound internal
    /// session.</summary>
    public interface IBoundSessionPrincipalSource
    {
        bool TryResolve(string peerKey, out PilotSessionPrincipal principal);
    }

    /// <summary>The process-local bound-session principal index. Admission publishes the minted
    /// internal (AccountId, CharacterId, SessionId) for a peer key; the gameplay observer reads it.
    /// Every access is serialized so a reconnect republish never races a concurrent read. Cleared on
    /// restart by construction (a fresh index).</summary>
    public sealed class BoundSessionPrincipalIndex : IBoundSessionPrincipalSource
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, PilotSessionPrincipal> _bound =
            new Dictionary<string, PilotSessionPrincipal>(StringComparer.Ordinal);

        /// <summary>Publish (or refresh on reconnect) the bound internal session principal for a
        /// server-owned peer key. A null/empty peer key or an unbound (empty-account) principal is
        /// ignored — the index never holds a provider-shaped or empty identity.</summary>
        public void Bind(string peerKey, PilotSessionPrincipal principal)
        {
            if (string.IsNullOrEmpty(peerKey) || string.IsNullOrEmpty(principal.Account.Value)) return;
            lock (_gate) { _bound[peerKey] = principal; }
        }

        /// <summary>Operator/hard close: remove a peer's binding unconditionally (idempotent). Used by the
        /// deterministic operator disable/delete path where the OPERATOR, not the peer, ends the session —
        /// it does not care which session id currently occupies the key. For an ordinary peer disconnect
        /// use <see cref="TryUnbind(string,string)"/> so a stale disconnect cannot clobber a newer bind.</summary>
        public void Unbind(string peerKey)
        {
            if (string.IsNullOrEmpty(peerKey)) return;
            lock (_gate) { _bound.Remove(peerKey); }
        }

        /// <summary>Stale-safe session close: remove the peer's binding ONLY when the currently-bound
        /// principal's <see cref="PilotSessionPrincipal.SessionId"/> matches <paramref name="sessionId"/>.
        /// A late disconnect for a superseded session whose id no longer matches the live bind is a no-op,
        /// so a reconnect that already republished a NEWER session under the same server-owned peer key
        /// (durable s_playerID) is never torn down by the old connection's delayed close (AIP-FR-013 /
        /// spec edge "stale disconnect"). Returns true iff a binding was actually removed.</summary>
        public bool TryUnbind(string peerKey, string sessionId)
        {
            if (string.IsNullOrEmpty(peerKey)) return false;
            lock (_gate)
            {
                if (_bound.TryGetValue(peerKey, out var current) &&
                    string.Equals(current.SessionId, sessionId ?? string.Empty, StringComparison.Ordinal))
                {
                    _bound.Remove(peerKey);
                    return true;
                }
                return false;
            }
        }

        public bool TryResolve(string peerKey, out PilotSessionPrincipal principal)
        {
            principal = default;
            if (string.IsNullOrEmpty(peerKey)) return false;
            lock (_gate) { return _bound.TryGetValue(peerKey, out principal); }
        }

        /// <summary>Live binding count (test/operator visibility). Zero on restart by construction.</summary>
        public int BoundCount { get { lock (_gate) { return _bound.Count; } } }
    }
}
