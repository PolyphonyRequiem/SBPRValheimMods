using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // IAP-007W — the engine-free LIVE session-admission orchestrator.
    //
    // This is the seam the net48 transport layer calls to turn one authenticated peer connection into a
    // BOUND INTERNAL gameplay principal in BoundSessionPrincipalIndex (and to tear it down on
    // disconnect). It composes the shipped Tracer-1/2 admission cores — PilotAccountService (resolve/mint
    // the internal AccountId), PilotCharacterAdmissionService (reserve the sole session lease, resolve/mint
    // the internal CharacterId, activate), and the BoundSessionAdmission coupler (publish/remove the live
    // principal) — into ONE ordered, fail-closed call.
    //
    // Everything the orchestrator consumes is a SERVER-OBSERVED fact the net48 layer read off the
    // transport-authenticated peer (its socket host id → provider subject; its character ZDO's s_playerID
    // → profile subject; the durable player:<s_playerID> character subject → peer key). NOTHING here comes
    // from a client payload. The net48 hook is deliberately thin: observe those facts, call Admit; on
    // disconnect call Close(transportHandle).
    //
    // Fail-closed ordering (task requirement, contracts data-model "Begin account admission"):
    //   1. ResolveOrCreateAccount            — an un-allowlisted / disabled subject rejects here; no bind.
    //   2. BeginAdmission (reserve the lease) — a second concurrent session rejects AccountAlreadyConnected.
    //   3. ResolveOrCreateCharacter          — mint/resolve the internal character under the held lease.
    //   4. ActivateAndBind                    — promote the lease AND publish the bound internal principal.
    // Any earlier rejection short-circuits and publishes nothing, so an incompletely-admitted peer never
    // becomes a resolvable gameplay principal (the observer/ingress then credit nothing).
    //
    // One-session and stale-disconnect semantics are inherited from the admission lease + the
    // session-qualified BoundSessionPrincipalIndex.TryUnbind: a late Close for a superseded session is a
    // no-op and cannot tear down a newer reconnect that already rebound the same peer key.
    //
    // net48 audit: System.* + engine-free admission/identity cores only. No UnityEngine/Valheim/BepInEx.
    public sealed class LiveSessionAdmission
    {
        private readonly PilotAccountService _accounts;
        private readonly PilotCharacterAdmissionService _characters;
        private readonly BoundSessionAdmission _binder;
        // IAP-012 fix-forward (t_f6c8c748): the privacy fail-closed admission gate. A closed pilot or an
        // uncataloged/expired/purged world fixture rejects admission BEFORE any account resolution or bind,
        // so a live principal can never be published into a closed/uncataloged pilot. Optional so the
        // pre-privacy composition path (and Tracer-1/2 tests) continue to work unchanged.
        private readonly IPrivacyAdmissionGate? _privacyGate;

        // Per-transport live session ledger so a disconnect (which carries only the transport handle) can
        // deterministically close the exact (peerKey, account, session) it opened. Serialized so a
        // reconnect racing a stale disconnect resolves cleanly.
        private readonly object _gate = new object();
        private readonly Dictionary<long, LiveSession> _byTransport = new Dictionary<long, LiveSession>();

        public LiveSessionAdmission(
            PilotAccountService accounts,
            PilotCharacterAdmissionService characters,
            BoundSessionPrincipalIndex boundSessions)
            : this(accounts, characters, boundSessions, null)
        {
        }

        /// <summary>Compose with a privacy fail-closed admission gate (t_f6c8c748). The gate is evaluated
        /// FIRST on every admission; a closed pilot or uncataloged/expired world fixture rejects before any
        /// account resolution so nothing binds.</summary>
        public LiveSessionAdmission(
            PilotAccountService accounts,
            PilotCharacterAdmissionService characters,
            BoundSessionPrincipalIndex boundSessions,
            IPrivacyAdmissionGate? privacyGate)
        {
            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _characters = characters ?? throw new ArgumentNullException(nameof(characters));
            if (boundSessions == null) throw new ArgumentNullException(nameof(boundSessions));
            _binder = new BoundSessionAdmission(characters, boundSessions);
            _privacyGate = privacyGate;
        }

        private readonly struct LiveSession
        {
            public LiveSession(string peerKey, PilotAccountId account, SessionId session, long transportHandle)
            {
                PeerKey = peerKey; Account = account; Session = session; TransportHandle = transportHandle;
            }
            public string PeerKey { get; }
            public PilotAccountId Account { get; }
            public SessionId Session { get; }
            public long TransportHandle { get; }
        }

        /// <summary>Admit one authenticated peer end-to-end and publish its bound internal principal. All
        /// arguments are server-observed facts (never a client payload): <paramref name="provider"/> is the
        /// verified transport/provider principal, <paramref name="profile"/> is the server-observed profile
        /// s_playerID fact, and <paramref name="peerKey"/> is the durable player:&lt;s_playerID&gt;
        /// character subject the gameplay path keys the bound-session index by. <paramref name="opSeed"/>
        /// disambiguates this admission's operation ids (the net48 layer passes a per-connection value).
        /// Fail-closed: any stage rejection publishes nothing.</summary>
        public LiveAdmissionResult Admit(
            string peerKey,
            VerifiedProviderPrincipal provider,
            VerifiedProfileSubject profile,
            long transportHandle,
            long occurredAt,
            string opSeed)
        {
            if (string.IsNullOrEmpty(peerKey))
                return LiveAdmissionResult.Fail(LiveAdmissionStage.PeerKey, "MissingPeerKey");
            if (!provider.IsResolved)
                return LiveAdmissionResult.Fail(LiveAdmissionStage.Account, "ProviderSubjectInvalid");
            if (!profile.IsResolved)
                return LiveAdmissionResult.Fail(LiveAdmissionStage.Character, "ProfileSubjectInvalid");

            // 0) Privacy fail-closed gate FIRST (t_f6c8c748): a closed pilot or an uncataloged/expired/
            //    purged world fixture rejects here, before any account resolution or bind, so nothing binds.
            if (_privacyGate != null)
            {
                var privacyReject = _privacyGate.EvaluateAdmission(occurredAt);
                if (privacyReject != PrivacyRejectionCode.None)
                    return LiveAdmissionResult.Fail(LiveAdmissionStage.Privacy, privacyReject.ToString());
            }

            opSeed ??= peerKey;

            // 1) Resolve or mint the internal account. Un-allowlisted / disabled / deleted subjects reject.
            var acct = _accounts.ResolveOrCreateAccount("acct-" + opSeed, provider, occurredAt);
            if (!acct.Accepted)
                return LiveAdmissionResult.Fail(LiveAdmissionStage.Account, acct.ResultCode);

            // 2) Reserve the sole admission lease BEFORE any character mint (one-session fence).
            var begin = _characters.BeginAdmission(acct.AccountId, transportHandle, occurredAt);
            if (!begin.Admitted)
                return LiveAdmissionResult.Fail(LiveAdmissionStage.Admission, begin.RejectionCode.ToString());

            // 3) Resolve or mint the internal character under the held lease.
            var chr = _characters.ResolveOrCreateCharacter("char-" + opSeed, acct.AccountId, begin.SessionId, profile, occurredAt);
            if (!chr.Accepted)
            {
                // Release the lease we just reserved so a rejected character mint does not strand the account.
                _characters.CloseSession(acct.AccountId, begin.SessionId, transportHandle);
                return LiveAdmissionResult.Fail(LiveAdmissionStage.Character, chr.RejectionCode.ToString());
            }

            // 4) Activate the session AND publish the bound internal principal (fail-closed on activation).
            var code = _binder.ActivateAndBind(peerKey, acct.AccountId, begin.SessionId, transportHandle, chr.CharacterId);
            if (code != CharacterRejectionCode.None)
            {
                _characters.CloseSession(acct.AccountId, begin.SessionId, transportHandle);
                return LiveAdmissionResult.Fail(LiveAdmissionStage.Activation, code.ToString());
            }

            lock (_gate)
                _byTransport[transportHandle] = new LiveSession(peerKey, acct.AccountId, begin.SessionId, transportHandle);

            return LiveAdmissionResult.Ok(acct.AccountId, chr.CharacterId, begin.SessionId, peerKey);
        }

        /// <summary>Close one peer's session on disconnect, identified by its transport handle. Releases the
        /// admission lease and removes the live bound principal (session-qualified, so a stale disconnect
        /// for a superseded session is a no-op). Returns true iff a live bound principal was removed.</summary>
        public bool Close(long transportHandle)
        {
            LiveSession session;
            lock (_gate)
            {
                if (!_byTransport.TryGetValue(transportHandle, out session)) return false;
                _byTransport.Remove(transportHandle);
            }
            return _binder.CloseAndUnbind(session.PeerKey, session.Account, session.Session, transportHandle);
        }

        /// <summary>Live admitted-session count (test/operator visibility). Cleared on restart.</summary>
        public int LiveSessionCount { get { lock (_gate) return _byTransport.Count; } }
    }

    /// <summary>Which stage of live admission a peer reached before rejection (fail-closed diagnostics).</summary>
    public enum LiveAdmissionStage { None, PeerKey, Privacy, Account, Admission, Character, Activation }

    /// <summary>The outcome of one end-to-end live admission. On success it carries the bound internal
    /// principal; on failure the stage + a stable, subject-free result code.</summary>
    public readonly struct LiveAdmissionResult
    {
        private LiveAdmissionResult(bool admitted, LiveAdmissionStage failedStage, string resultCode,
            PilotAccountId account, PilotCharacterId character, SessionId session, string peerKey)
        {
            Admitted = admitted;
            FailedStage = failedStage;
            ResultCode = resultCode ?? string.Empty;
            Account = account;
            Character = character;
            Session = session;
            PeerKey = peerKey ?? string.Empty;
        }

        public bool Admitted { get; }
        public LiveAdmissionStage FailedStage { get; }
        public string ResultCode { get; }
        public PilotAccountId Account { get; }
        public PilotCharacterId Character { get; }
        public SessionId Session { get; }
        public string PeerKey { get; }

        internal static LiveAdmissionResult Ok(PilotAccountId account, PilotCharacterId character, SessionId session, string peerKey) =>
            new LiveAdmissionResult(true, LiveAdmissionStage.None, "Admitted", account, character, session, peerKey);

        internal static LiveAdmissionResult Fail(LiveAdmissionStage stage, string code) =>
            new LiveAdmissionResult(false, stage, code, default, default, default, string.Empty);

        /// <summary>One-line, PII-free operator rendering (never a raw subject).</summary>
        public string ToOperatorLine() =>
            Admitted
                ? "[session-admission] admitted peerBound stage=None result=Admitted"
                : $"[session-admission] rejected stage={FailedStage} result={ResultCode}";
    }
}
