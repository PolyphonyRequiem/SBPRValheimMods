using System;
using System.Globalization;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Identity
{
    // IAP-001 Gate 0 — prove the pilot transport principal (engine-free CLEAN-side core).
    //
    // This file is the executable server-side peer-auth tracer the Gate-0 acceptance tests exercise.
    // It reproduces, engine-free, the exact decision the net48 transport adapter
    // (Features/PilotIdentity/ZdoPilotProviderSubjectSource.cs) makes on a real dedicated server:
    // turn ONE server-observed authenticated transport fact into a transient VerifiedProviderPrincipal
    // in the ONE configured provider namespace, or a stable rejection. It NEVER trusts a client payload
    // claim, NEVER logs/serializes the raw subject, and NEVER creates an account (Gate 0 is proof-only).
    //
    // PROVIDER DECISION (spec Closed-pilot decision #3, AIP-FR-001; contracts "Provider adapter port"):
    //   Exactly one backend is named for the pilot: STEAMWORKS. The shipped Homestead transport seam
    //   (Features/Progression/ZdoAuthenticatedSenderSource.cs) already reads the authenticated peer's
    //   Steam socket host id off m_socket.GetHostName(), and VanillaAdminIdentity.DefaultPlatform is
    //   "Steam" — the dedicated server proven by PR #317 is a Steam backend. PlayFab is NOT admitted by
    //   this pilot configuration; a PlayFab-namespace subject is rejected as ProviderUnsupported.
    //
    // net48 audit: only System.String / System.Globalization here. No UnityEngine / Valheim / BepInEx,
    // so this link-compiles into the net8 test project exactly like the shipped identity seam and ships
    // under net48 in the mod.

    /// <summary>The one provider namespace this pilot admits, plus the rejected sibling namespace kept
    /// only so Gate 0 can prove it is refused. A subject is never globally meaningful without its
    /// backend namespace (research.md "Stable external identity"; OIDC (issuer,subject) discipline).</summary>
    public static class PilotProviderNamespace
    {
        /// <summary>The single admitted pilot backend namespace (Steamworks dedicated server).</summary>
        public const string Steam = "Steam";

        /// <summary>A provider namespace explicitly NOT selected for this pilot configuration. Present so
        /// AT-AIP-PROVIDER-NAMESPACE can prove a non-Steam subject is refused rather than silently
        /// accepted. Supporting it is deferred (spec decision #3).</summary>
        public const string PlayFab = "PlayFab";
    }

    /// <summary>The configured provider namespace + backend/issuer identity that distinguishes subjects
    /// from different providers/configurations (data-model.md `ProviderKey`). A subject only resolves
    /// under the exact configured key; a subject observed under a different namespace or backend does
    /// not collide with it.</summary>
    public readonly struct PilotProviderKey : IEquatable<PilotProviderKey>
    {
        public PilotProviderKey(string providerNamespace, string backendIssuer)
        {
            Namespace = providerNamespace ?? string.Empty;
            BackendIssuer = backendIssuer ?? string.Empty;
        }

        /// <summary>Provider namespace, e.g. "Steam". Never empty for a valid configured key.</summary>
        public string Namespace { get; }

        /// <summary>Backend/issuer identity within the namespace (e.g. the Steam app/universe context the
        /// dedicated server authenticates against). Distinguishes two configurations of the same
        /// namespace so a subject minted under one backend cannot be replayed under another.</summary>
        public string BackendIssuer { get; }

        /// <summary>The one Gate-0-proven Steamworks pilot configuration.</summary>
        public static PilotProviderKey Steamworks(string backendIssuer) =>
            new PilotProviderKey(PilotProviderNamespace.Steam, backendIssuer);

        public bool IsConfigured => !string.IsNullOrEmpty(Namespace) && !string.IsNullOrEmpty(BackendIssuer);

        public bool Equals(PilotProviderKey other) =>
            string.Equals(Namespace, other.Namespace, StringComparison.Ordinal) &&
            string.Equals(BackendIssuer, other.BackendIssuer, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is PilotProviderKey other && Equals(other);
        public override int GetHashCode() =>
            (StringComparer.Ordinal.GetHashCode(Namespace ?? string.Empty) * 397) ^
            StringComparer.Ordinal.GetHashCode(BackendIssuer ?? string.Empty);
        public override string ToString() => Namespace + ":" + BackendIssuer;
    }

    /// <summary>ONE server-observed authenticated transport fact — the raw host id read off the peer's
    /// own authenticated socket (Steam: m_socket.GetHostName()), never a client payload. The gate turns
    /// this, and only this, into a VerifiedProviderPrincipal. There is deliberately NO constructor path
    /// that accepts a client-supplied claim: a payload cannot manufacture this fact (AIP-FR-001,
    /// contracts "Payload identity is compared or ignored, never authority").</summary>
    public readonly struct ServerObservedTransportSubject
    {
        public ServerObservedTransportSubject(string authenticatedHostId, long transportHandle)
        {
            AuthenticatedHostId = authenticatedHostId ?? string.Empty;
            TransportHandle = transportHandle;
        }

        /// <summary>The authenticated socket host id (e.g. "Steam_7656119..." / "Steam:7656119..." /
        /// bare "7656119..."). Read off the server's own ZNetPeer socket. Transient; never persisted.</summary>
        public string AuthenticatedHostId { get; }

        /// <summary>The opaque per-peer transport handle (the ZRpc-bound peer), so reconnect stability is
        /// asserted against the durable subject, not this per-session handle.</summary>
        public long TransportHandle { get; }

        public static ServerObservedTransportSubject None => new ServerObservedTransportSubject(string.Empty, 0L);
    }

    /// <summary>Transient verified principal (contracts "VerifiedProviderPrincipal"; data-model.md
    /// "Transient verified principal"). Exists only during admission; the raw subject is memory-only and
    /// is discarded after it is converted to a lookup HMAC (Tracer 1 owns that conversion, not Gate 0).</summary>
    public readonly struct VerifiedProviderPrincipal
    {
        public VerifiedProviderPrincipal(PilotProviderKey providerKey, string canonicalSubject, long transportHandle)
        {
            ProviderKey = providerKey;
            CanonicalSubject = canonicalSubject ?? string.Empty;
            TransportHandle = transportHandle;
        }

        public PilotProviderKey ProviderKey { get; }

        /// <summary>The canonical, namespace-stripped subject (bare user id within the configured
        /// backend). Stable across reconnect/restart. Memory-only; never logged/serialized.</summary>
        public string CanonicalSubject { get; }

        public long TransportHandle { get; }

        public bool IsResolved => ProviderKey.IsConfigured && !string.IsNullOrEmpty(CanonicalSubject);
    }

    /// <summary>Stable rejection vocabulary for Gate 0 (a strict subset of contracts "Stable rejection
    /// vocabulary"). Every non-resolution is one of these; no rejection ever falls back to a raw
    /// identifier or a payload claim.</summary>
    public enum PilotProviderRejection
    {
        None = 0,
        /// <summary>No server-authenticated transport subject (empty/anonymous host id).</summary>
        UnauthenticatedPeer,
        /// <summary>Subject belongs to a provider namespace not selected by Gate 0/config.</summary>
        ProviderUnsupported,
        /// <summary>Empty/ambiguous/noncanonical provider subject within the configured namespace.</summary>
        ProviderSubjectInvalid
    }

    /// <summary>The engine-free Gate-0 provider gate. Given the ONE configured provider key and ONE
    /// server-observed transport fact, it resolves a transient VerifiedProviderPrincipal or returns a
    /// stable rejection. It is the single decision the net48 adapter defers to, so the acceptance tests
    /// prove the real shipped behavior, not a parallel copy.</summary>
    public sealed class PilotProviderGate
    {
        private readonly PilotProviderKey _configured;

        /// <param name="configuredProvider">The one Gate-0-proven pilot provider key. Must be configured
        /// (non-empty namespace + backend); an unconfigured gate rejects everything as unsupported so a
        /// misconfigured server fails closed rather than admitting an unqualified subject.</param>
        public PilotProviderGate(PilotProviderKey configuredProvider)
        {
            _configured = configuredProvider;
        }

        /// <summary>The single admitted namespace this gate was configured with (for disclosure/logging
        /// as a provider CLASS, never a subject).</summary>
        public string ConfiguredNamespace => _configured.Namespace;

        /// <summary>Resolve one server-observed transport fact into a transient verified principal, or a
        /// stable rejection. Never consults a client payload, never logs the raw subject.</summary>
        public PilotProviderRejection TryResolve(ServerObservedTransportSubject observed, out VerifiedProviderPrincipal principal)
        {
            principal = default;

            if (!_configured.IsConfigured)
                return PilotProviderRejection.ProviderUnsupported;   // fail closed on misconfiguration

            string host = observed.AuthenticatedHostId ?? string.Empty;
            if (host.Length == 0)
                return PilotProviderRejection.UnauthenticatedPeer;   // no authenticated subject at all

            if (!TrySplitNamespace(host, _configured.Namespace, out string ns, out string subject))
                return PilotProviderRejection.ProviderSubjectInvalid;

            // An "anonymous"/placeholder subject is not a real authenticated identity.
            if (IsAnonymousSubject(subject))
                return PilotProviderRejection.UnauthenticatedPeer;

            if (!string.Equals(ns, _configured.Namespace, StringComparison.Ordinal))
                return PilotProviderRejection.ProviderUnsupported;   // e.g. a PlayFab subject on a Steam pilot

            if (!IsCanonicalSubject(subject))
                return PilotProviderRejection.ProviderSubjectInvalid;

            principal = new VerifiedProviderPrincipal(_configured, subject, observed.TransportHandle);
            return PilotProviderRejection.None;
        }

        /// <summary>True when a server-observed fact resolves under the configured provider.</summary>
        public bool TryResolve(ServerObservedTransportSubject observed, out VerifiedProviderPrincipal principal, out PilotProviderRejection rejection)
        {
            rejection = TryResolve(observed, out principal);
            return rejection == PilotProviderRejection.None;
        }

        /// <summary>Split a host id into (namespace, bare subject). Mirrors the vanilla PlatformUserID
        /// shape reproduced by VanillaAdminIdentity: an id qualified with a provider separator is
        /// "&lt;namespace&gt;&lt;sep&gt;&lt;subject&gt;"; a bare id is a subject on the SERVER's configured
        /// namespace. Both ':' and '_' are accepted separators (Steam host ids appear in both forms).
        /// Returns false when the shape is unusable (trailing/leading separator, empty subject).</summary>
        private static bool TrySplitNamespace(string host, string serverNamespace, out string ns, out string subject)
        {
            ns = string.Empty;
            subject = string.Empty;

            int sep = IndexOfSeparator(host);
            if (sep < 0)
            {
                // Bare id → subject on the server's own configured namespace.
                ns = serverNamespace;
                subject = host;
                return subject.Length > 0;
            }

            if (sep == 0 || sep >= host.Length - 1)
                return false;   // ":x" or "x:" — ambiguous/noncanonical

            ns = host.Substring(0, sep);
            subject = host.Substring(sep + 1);
            // A subject that still contains a separator is ambiguous.
            return ns.Length > 0 && subject.Length > 0 && IndexOfSeparator(subject) < 0;
        }

        private static int IndexOfSeparator(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == ':' || s[i] == '_') return i;
            }
            return -1;
        }

        /// <summary>A subject that is a known anonymous/placeholder marker rather than a real
        /// authenticated identity. Steam surfaces such peers before authentication completes; they must
        /// fail closed, never mint identity.</summary>
        private static bool IsAnonymousSubject(string subject)
        {
            if (subject.Length == 0) return true;
            if (string.Equals(subject, "0", StringComparison.Ordinal)) return true;
            if (string.Equals(subject, "anonymous", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(subject, "unknown", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>A canonical subject is a nonempty run of unambiguous identifier characters
        /// (alphanumeric) — no whitespace, no residual separators. Steam user ids are decimal; we accept
        /// the broader alphanumeric set so a future backend id within the Steam namespace stays valid,
        /// while still rejecting whitespace/control/delimiter noise.</summary>
        private static bool IsCanonicalSubject(string subject)
        {
            if (subject.Length == 0) return false;
            foreach (char c in subject)
            {
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>Prove subject stability across reconnect/restart: two independently observed transport
        /// facts (different per-session transport handles) that resolve under the configured provider
        /// yield the SAME canonical subject iff their durable host ids denote the same identity. Returns
        /// false if either does not resolve. Used by AT-AIP-PROVIDER-RECONNECT.</summary>
        public bool ResolvesToSameSubject(ServerObservedTransportSubject a, ServerObservedTransportSubject b)
        {
            if (TryResolve(a, out var pa) != PilotProviderRejection.None) return false;
            if (TryResolve(b, out var pb) != PilotProviderRejection.None) return false;
            return pa.ProviderKey.Equals(pb.ProviderKey) &&
                   string.Equals(pa.CanonicalSubject, pb.CanonicalSubject, StringComparison.Ordinal);
        }

        /// <summary>Format the configured provider CLASS (namespace only) for disclosure/logging. NEVER
        /// includes a subject. Distinct from any resolved principal.</summary>
        public string DescribeProviderClass() =>
            _configured.IsConfigured
                ? string.Format(CultureInfo.InvariantCulture, "provider={0}", _configured.Namespace)
                : "provider=<unconfigured>";
    }
}
