using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // ============================================================================
    //  T022 — QA-ONLY EPHEMERAL ACCOUNT BYPASS for the isolated HomesteadT009L fixture.
    // ----------------------------------------------------------------------------
    //  *** THIS IS TEST INFRASTRUCTURE, NEVER PRODUCTION ARCHITECTURE. ***
    //
    //  Daniel's explicit scope decision (supersedes live-store provisioning gate
    //  t_63e803b9): rather than provisioning the real valbot subject into the pilot
    //  account store, admit configured server-observed Steam peers into Homestead
    //  gameplay under EPHEMERAL, opaque, process-local QA account/character identities
    //  so canonical T022 can run live on the isolated T009L fixture — WITHOUT a
    //  PilotAllowlistEntry and WITHOUT writing any account / disclosure / credential /
    //  character record to the durable journal.
    //
    //  This engine-free core owns the whole decision. The net48 seam
    //  (Features/PilotIdentity/PilotSessionLifecycleObserver.cs) only feeds it
    //  server-observed facts and, on a positive admit, publishes the ephemeral bound
    //  internal principal into the SAME BoundSessionPrincipalIndex the gameplay path
    //  resolves against — so a live QA session can issue T022 commands.
    //
    //  SAFETY BOUNDARY (task requirement — a CONJUNCTION of explicit gates, not one bool):
    //    1. Default OFF. Nothing here runs unless every gate below passes; existing
    //       behavior and `NotAllowlisted` stay bit-for-bit unchanged everywhere else.
    //    2. Conjunction: QA-bypass enabled  AND  exact environment tag
    //       "homestead-t009l"  AND  exact server/world/data-root confinement to the
    //       isolated T009L fixture (observed == configured expected, and NEITHER is a
    //       production marker)  AND  a non-empty, canonical, wildcard-free configured
    //       server-observed SteamID allowlist. Any production root/name/tag hard-refuses.
    //    3. Authority is ONLY the authenticated server-observed transport principal
    //       (VerifiedProviderPrincipal off the Gate-0 provider gate). Client payload
    //       identity is never trusted.
    //    4. Ephemeral opaque AccountId + CharacterId, process-local, ≥128-bit CSPRNG
    //       (OpaqueIdMint). Distinct Steam peers → distinct accounts; distinct profiles
    //       → distinct characters. Stable only for the live QA session; cleared on restart.
    //    5. Never calls PilotAccountService first-bind, never fabricates a disclosure
    //       acknowledgement, never appends an account-journal record, and never emits a
    //       raw Steam subject / HMAC. The only marker is a subject-free
    //       "[qa-account-bypass] admitted" line carrying opaque IDs + result code.
    //    6. Session-fence semantics preserved: one active/pending session per ephemeral
    //       account (AccountAdmissionIndex), cleanup on disconnect, and a stale disconnect
    //       cannot close a newer session (session-qualified unbind).
    //    7. Grants the Homestead gameplay principal ONLY. It does NOT grant Valheim admin;
    //       the t009l adminlist remains a separate exact-ID operator step.
    //    8. Rollback: disabling the QA gates restores normal `NotAllowlisted` behavior with
    //       NO durable state to clean (every structure here is in-memory and dropped on
    //       restart).
    //
    //  net48 audit: System.* + generics + the engine-free identity/admission value objects
    //  only. No UnityEngine / Valheim / BepInEx — link-compiles under net8 and ships net48.
    // ============================================================================

    /// <summary>The exact, validated QA-bypass configuration. Constructed only from server-owned
    /// (never client-settable) values. Validation is the gate's job (<see cref="QaAccountBypassGate"/>);
    /// this is a plain carrier of the raw configured strings + the allowlist snapshot.</summary>
    public sealed class QaAccountBypassConfig
    {
        public QaAccountBypassConfig(
            bool enabled,
            string environmentTag,
            string expectedWorldName,
            string expectedDataRoot,
            IEnumerable<string> allowlistedSteamSubjects)
        {
            Enabled = enabled;
            EnvironmentTag = environmentTag ?? string.Empty;
            ExpectedWorldName = expectedWorldName ?? string.Empty;
            ExpectedDataRoot = expectedDataRoot ?? string.Empty;

            var set = new HashSet<string>(StringComparer.Ordinal);
            if (allowlistedSteamSubjects != null)
                foreach (var s in allowlistedSteamSubjects)
                    if (!string.IsNullOrEmpty(s)) set.Add(s.Trim());
            AllowlistedSteamSubjects = set;
        }

        /// <summary>Master flag. Default MUST be false; the bypass is off unless an operator explicitly
        /// enables it on the isolated fixture.</summary>
        public bool Enabled { get; }

        /// <summary>Operator-declared environment tag. Must equal exactly "homestead-t009l".</summary>
        public string EnvironmentTag { get; }

        /// <summary>The exact isolated-fixture world name the observed world must equal.</summary>
        public string ExpectedWorldName { get; }

        /// <summary>The exact isolated-fixture data root the observed durable root must equal.</summary>
        public string ExpectedDataRoot { get; }

        /// <summary>The configured server-observed SteamID allowlist snapshot (ordinal set).</summary>
        public HashSet<string> AllowlistedSteamSubjects { get; }
    }

    /// <summary>The two server-observed runtime facts the gate confines against: the world the server
    /// actually loaded and the durable data root it actually writes under. Read off the authoritative
    /// server (ZNet.GetWorldName + the composed durable directory) — never a client claim.</summary>
    public readonly struct QaIsolationFacts
    {
        public QaIsolationFacts(string observedWorldName, string observedDataRoot)
        {
            ObservedWorldName = observedWorldName ?? string.Empty;
            ObservedDataRoot = observedDataRoot ?? string.Empty;
        }

        public string ObservedWorldName { get; }
        public string ObservedDataRoot { get; }
    }

    /// <summary>Stable, subject-free reason a QA-bypass gate evaluation refused. Every non-None value
    /// means the bypass stays OFF and normal admission (including `NotAllowlisted`) runs unchanged.</summary>
    public enum QaBypassGateRejection
    {
        None = 0,
        Disabled,                 // master flag off (the default)
        EnvironmentTagMismatch,   // tag != "homestead-t009l"
        WorldNameMismatch,        // observed world != configured expected (or expected empty)
        DataRootMismatch,         // observed data root != configured expected (or expected empty)
        ProductionMarker,         // a production name/root/tag appeared anywhere — hard refuse
        EmptyAllowlist,           // no configured SteamID
        WildcardAllowlist,        // a wildcard / non-canonical id was configured — refuse the whole set
    }

    /// <summary>The engine-free conjunction gate. Given the validated config + the observed isolation
    /// facts it returns None only when EVERY gate passes; otherwise a stable rejection. Fail-closed by
    /// construction: a null/empty/misconfigured input rejects rather than admitting.</summary>
    public static class QaAccountBypassGate
    {
        /// <summary>The one admitted environment tag. Exact match required (case-sensitive).</summary>
        public const string RequiredEnvironmentTag = "homestead-t009l";

        /// <summary>Substrings that mark a PRODUCTION deployment. If any appears in the tag, the
        /// configured/observed world name, or the data root, the gate hard-refuses so a misconfiguration
        /// pointed at production can never open the bypass. Lowercase; matched case-insensitively.</summary>
        private static readonly string[] ProductionMarkers = { "niflheim", "heistan" };

        public static QaBypassGateRejection Evaluate(QaAccountBypassConfig config, QaIsolationFacts facts)
        {
            if (config == null || !config.Enabled)
                return QaBypassGateRejection.Disabled;

            // Exact environment tag.
            if (!string.Equals(config.EnvironmentTag, RequiredEnvironmentTag, StringComparison.Ordinal))
                return QaBypassGateRejection.EnvironmentTagMismatch;

            // Hard production refusal across the environment tag + configured/observed WORLD NAMES. (The
            // data root is deliberately NOT marker-scanned: the mod's own durable directory always contains
            // the literal "sbpr-niflheim-homestead" path component, so a substring scan there would false-
            // positive on every deployment including t009l. The data root is instead confined by the EXACT
            // ExpectedDataRoot == ObservedDataRoot match below.)
            if (ContainsProductionMarker(config.EnvironmentTag) ||
                ContainsProductionMarker(config.ExpectedWorldName) ||
                ContainsProductionMarker(facts.ObservedWorldName))
                return QaBypassGateRejection.ProductionMarker;

            // Exact world confinement: a non-empty configured expected world MUST equal the observed world.
            if (string.IsNullOrEmpty(config.ExpectedWorldName) ||
                !string.Equals(config.ExpectedWorldName, facts.ObservedWorldName, StringComparison.Ordinal))
                return QaBypassGateRejection.WorldNameMismatch;

            // Exact data-root confinement: a non-empty configured expected root MUST equal the observed root.
            if (string.IsNullOrEmpty(config.ExpectedDataRoot) ||
                !string.Equals(config.ExpectedDataRoot, facts.ObservedDataRoot, StringComparison.Ordinal))
                return QaBypassGateRejection.DataRootMismatch;

            // Non-empty, canonical, wildcard-free allowlist.
            if (config.AllowlistedSteamSubjects.Count == 0)
                return QaBypassGateRejection.EmptyAllowlist;
            foreach (var id in config.AllowlistedSteamSubjects)
                if (!IsCanonicalSteamSubject(id))
                    return QaBypassGateRejection.WildcardAllowlist;

            return QaBypassGateRejection.None;
        }

        private static bool ContainsProductionMarker(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            string lower = s.ToLowerInvariant();
            foreach (var marker in ProductionMarkers)
                if (lower.IndexOf(marker, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        /// <summary>A canonical configured SteamID: a nonempty run of decimal digits. Rejects a wildcard
        /// ("*"), an empty/whitespace id, an anonymous placeholder ("0"), and anything non-numeric — the
        /// same fail-closed discipline the Gate-0 provider gate applies to an observed subject.</summary>
        public static bool IsCanonicalSteamSubject(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (string.Equals(id, "0", StringComparison.Ordinal)) return false;
            foreach (char c in id)
                if (c < '0' || c > '9') return false;
            return true;
        }
    }

    /// <summary>Process-local, restart-cleared mint of EPHEMERAL opaque QA identities. A distinct Steam
    /// subject always maps to the SAME opaque account for the life of the process (so a reconnect resolves
    /// the same account), and a distinct (subject, profile) pair to the same opaque character. Every id is
    /// a fresh ≥128-bit CSPRNG value from <see cref="OpaqueIdMint"/> — never derived from the Steam subject
    /// or the s_playerID, so a minted id reveals nothing about the credential that owns it. Nothing here is
    /// journaled; a restart drops the whole map (task safety bullet 4/8).</summary>
    public sealed class QaEphemeralIdentityMint
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, PilotAccountId> _accountBySubject = new Dictionary<string, PilotAccountId>(StringComparer.Ordinal);
        private readonly Dictionary<string, PilotCharacterId> _characterByKey = new Dictionary<string, PilotCharacterId>(StringComparer.Ordinal);

        /// <summary>The stable-for-this-process opaque account for a Steam subject.</summary>
        public PilotAccountId AccountFor(string steamSubject)
        {
            string key = steamSubject ?? string.Empty;
            lock (_gate)
            {
                if (!_accountBySubject.TryGetValue(key, out var acct))
                {
                    acct = OpaqueIdMint.NewAccountId();
                    _accountBySubject[key] = acct;
                }
                return acct;
            }
        }

        /// <summary>The stable-for-this-process opaque character for a (Steam subject, profile) pair.</summary>
        public PilotCharacterId CharacterFor(string steamSubject, string canonicalPlayerId)
        {
            string key = (steamSubject ?? string.Empty) + "|" + (canonicalPlayerId ?? string.Empty);
            lock (_gate)
            {
                if (!_characterByKey.TryGetValue(key, out var chr))
                {
                    chr = OpaqueIdMint.NewCharacterId();
                    _characterByKey[key] = chr;
                }
                return chr;
            }
        }

        public int AccountCount { get { lock (_gate) return _accountBySubject.Count; } }
        public int CharacterCount { get { lock (_gate) return _characterByKey.Count; } }
    }

    /// <summary>Which stage a QA-bypass admission reached before rejection (fail-closed diagnostics).</summary>
    public enum QaBypassStage { None, PeerKey, Provider, Profile, NotAllowlisted, Admission, Activation }

    /// <summary>The outcome of one QA-bypass admission attempt. On success it carries the EPHEMERAL bound
    /// internal principal; on failure a stage + a stable, subject-free code. <see cref="ToOperatorLine"/>
    /// is the ONLY rendering the observer logs — it never contains a raw Steam subject or HMAC.</summary>
    public readonly struct QaBypassAdmissionResult
    {
        private QaBypassAdmissionResult(bool admitted, QaBypassStage failedStage, string resultCode,
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
        public QaBypassStage FailedStage { get; }
        public string ResultCode { get; }
        public PilotAccountId Account { get; }
        public PilotCharacterId Character { get; }
        public SessionId Session { get; }
        public string PeerKey { get; }

        internal static QaBypassAdmissionResult Ok(PilotAccountId account, PilotCharacterId character, SessionId session, string peerKey) =>
            new QaBypassAdmissionResult(true, QaBypassStage.None, "Admitted", account, character, session, peerKey);

        internal static QaBypassAdmissionResult Fail(QaBypassStage stage, string code) =>
            new QaBypassAdmissionResult(false, stage, code, default, default, default, string.Empty);

        /// <summary>The loud, PII-free marker (opaque IDs + result code only; never a raw subject).</summary>
        public string ToOperatorLine() =>
            Admitted
                ? "[qa-account-bypass] admitted account=" + Account.Value + " character=" + Character.Value
                  + " session=" + Session.Value + " result=Admitted"
                : "[qa-account-bypass] rejected stage=" + FailedStage + " result=" + ResultCode;
    }

    /// <summary>The engine-free QA-bypass admission service. It mirrors the shipped
    /// <see cref="LiveSessionAdmission"/> fence/close semantics — one active/pending session per ephemeral
    /// account via <see cref="AccountAdmissionIndex"/>, session-qualified unbind via
    /// <see cref="BoundSessionPrincipalIndex"/> — but issues EPHEMERAL opaque identities from
    /// <see cref="QaEphemeralIdentityMint"/> instead of resolving/minting durable pilot accounts. It NEVER
    /// touches the account journal, disclosure, or credential store. Compose it ONLY when
    /// <see cref="QaAccountBypassGate"/> returns None.</summary>
    public sealed class QaAccountBypassAdmission
    {
        private readonly QaEphemeralIdentityMint _mint;
        private readonly AccountAdmissionIndex _admissionIndex;
        private readonly BoundSessionPrincipalIndex _boundSessions;
        private readonly HashSet<string> _allowlist;

        private readonly object _gate = new object();
        private readonly Dictionary<long, QaLiveSession> _byTransport = new Dictionary<long, QaLiveSession>();

        public QaAccountBypassAdmission(
            QaEphemeralIdentityMint mint,
            AccountAdmissionIndex admissionIndex,
            BoundSessionPrincipalIndex boundSessions,
            IEnumerable<string> allowlistedSteamSubjects)
        {
            _mint = mint ?? throw new ArgumentNullException(nameof(mint));
            _admissionIndex = admissionIndex ?? throw new ArgumentNullException(nameof(admissionIndex));
            _boundSessions = boundSessions ?? throw new ArgumentNullException(nameof(boundSessions));
            _allowlist = new HashSet<string>(StringComparer.Ordinal);
            if (allowlistedSteamSubjects != null)
                foreach (var s in allowlistedSteamSubjects)
                    if (!string.IsNullOrEmpty(s)) _allowlist.Add(s);
        }

        private readonly struct QaLiveSession
        {
            public QaLiveSession(string peerKey, PilotAccountId account, SessionId session, long transportHandle)
            {
                PeerKey = peerKey; Account = account; Session = session; TransportHandle = transportHandle;
            }
            public string PeerKey { get; }
            public PilotAccountId Account { get; }
            public SessionId Session { get; }
            public long TransportHandle { get; }
        }

        /// <summary>True iff this authenticated Steam subject is on the configured server-observed
        /// allowlist. Never consults a client payload — the caller passes the subject resolved off the
        /// authenticated transport principal.</summary>
        public bool IsAllowlisted(string steamSubject) =>
            !string.IsNullOrEmpty(steamSubject) && _allowlist.Contains(steamSubject);

        /// <summary>Admit one allowlisted authenticated peer under an EPHEMERAL opaque QA principal and
        /// publish it into the bound-session index. All arguments are server-observed facts (never a client
        /// payload): <paramref name="provider"/> is the Gate-0 verified transport principal (its canonical
        /// subject is the allowlist key), <paramref name="profile"/> the server-observed s_playerID fact,
        /// and <paramref name="peerKey"/> the durable player:&lt;s_playerID&gt; character subject the
        /// gameplay path keys by. Fail-closed: any stage rejection publishes nothing.</summary>
        public QaBypassAdmissionResult Admit(
            string peerKey,
            VerifiedProviderPrincipal provider,
            VerifiedProfileSubject profile,
            long transportHandle,
            long occurredAt)
        {
            if (string.IsNullOrEmpty(peerKey))
                return QaBypassAdmissionResult.Fail(QaBypassStage.PeerKey, "MissingPeerKey");
            if (!provider.IsResolved)
                return QaBypassAdmissionResult.Fail(QaBypassStage.Provider, "ProviderSubjectInvalid");
            if (!profile.IsResolved)
                return QaBypassAdmissionResult.Fail(QaBypassStage.Profile, "ProfileSubjectInvalid");

            string subject = provider.CanonicalSubject;
            if (!IsAllowlisted(subject))
                return QaBypassAdmissionResult.Fail(QaBypassStage.NotAllowlisted, "NotAllowlisted");

            // Ephemeral opaque account for this Steam subject (distinct subjects → distinct accounts).
            var account = _mint.AccountFor(subject);

            // Reserve the sole admission lease BEFORE binding (one-session fence, mirrors BeginAdmission).
            var session = OpaqueIdMint.NewSessionId();
            var reservation = _admissionIndex.TryReserve(account, session, transportHandle, occurredAt);
            if (reservation.Outcome == AdmissionReservationOutcome.AlreadyConnected)
                return QaBypassAdmissionResult.Fail(QaBypassStage.Admission, "AccountAlreadyConnected");

            // Ephemeral opaque character for this (subject, profile) pair (distinct profiles → distinct chars).
            var character = _mint.CharacterFor(subject, profile.CanonicalPlayerId);

            // Promote the lease to Active, then publish the ephemeral bound internal principal.
            if (!_admissionIndex.TryActivate(account, session, transportHandle, character))
            {
                _admissionIndex.TryRelease(account, session, transportHandle);
                return QaBypassAdmissionResult.Fail(QaBypassStage.Activation, "ActivationFailed");
            }

            var principal = new PilotSessionPrincipal(
                new AccountId(account.Value), new CharacterId(character.Value), session.Value);
            _boundSessions.Bind(peerKey, principal);

            lock (_gate)
                _byTransport[transportHandle] = new QaLiveSession(peerKey, account, session, transportHandle);

            return QaBypassAdmissionResult.Ok(account, character, session, peerKey);
        }

        /// <summary>Close one peer's QA session on disconnect, identified by its transport handle. Releases
        /// the admission lease and removes the ephemeral bound principal (session-qualified, so a stale
        /// disconnect for a superseded session is a no-op). Returns true iff a live principal was removed.</summary>
        public bool Close(long transportHandle)
        {
            QaLiveSession session;
            lock (_gate)
            {
                if (!_byTransport.TryGetValue(transportHandle, out session)) return false;
                _byTransport.Remove(transportHandle);
            }
            _admissionIndex.TryRelease(session.Account, session.Session, transportHandle);
            return _boundSessions.TryUnbind(session.PeerKey, session.Session.Value);
        }

        /// <summary>Live admitted-session count (test/operator visibility). Cleared on restart.</summary>
        public int LiveSessionCount { get { lock (_gate) return _byTransport.Count; } }
    }
}
