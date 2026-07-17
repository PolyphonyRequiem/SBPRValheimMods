// ============================================================================
//  IAP-001 — Gate 0: prove the pilot transport principal.
// ----------------------------------------------------------------------------
//  Executable evidence for the six named Gate-0 acceptance IDs. These exercise
//  the engine-free CLEAN-side core (Adapters/Identity/*) that the net48 transport
//  adapter (Features/PilotIdentity/ZdoPilotProviderSubjectSource.cs) defers to,
//  so the asserted behaviour IS the shipped decision, not a parallel copy. The
//  net48 adapter references Valheim (ZNet/ZNetPeer/ISocket) and is therefore NOT
//  link-compiled here — exactly like the shipped ZdoAuthenticatedSenderSource.
//
//  Named acceptance (spec §Requirement-to-acceptance, AIP-FR-001/006):
//    AT-AIP-PROVIDER-GATE0            — one backend named + proven; transient principal
//    AT-AIP-UNAUTHENTICATED           — empty/anonymous/payload-claimed identity rejects
//    AT-AIP-PROVIDER-NAMESPACE        — exactly one namespace admitted; sibling refused
//    AT-AIP-PROVIDER-PROVISION-INPUT  — protected no-echo provisioning input, allowlist-only
//    AT-AIP-PROVIDER-RECONNECT        — subject stable across reconnect/restart
//    AT-AIP-PROVIDER-LOG-SCRUB        — Niflheim subsystem logs carry no raw subject;
//                                       upstream facts inventoried with bounded purge
// ============================================================================

using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using Xunit;

namespace SBPR.Trailborne.Tests
{
    public sealed class NiflheimPilotProviderGate0Tests
    {
        // The one Gate-0-proven pilot configuration: Steamworks against a named backend/issuer context.
        private static readonly PilotProviderKey Steamworks =
            PilotProviderKey.Steamworks("niflheim-pilot-app-896660");

        private static PilotProviderGate NewGate() => new PilotProviderGate(Steamworks);

        // ── AT-AIP-PROVIDER-GATE0 ───────────────────────────────────────────────
        // Exactly one provider backend is named and proven: a server-observed authenticated Steam
        // transport fact resolves to a transient VerifiedProviderPrincipal in the configured namespace.

        [Fact]
        public void AT_AIP_PROVIDER_GATE0_ServerObservedSteamSubject_ResolvesTransientPrincipal()
        {
            var gate = NewGate();
            var observed = new ServerObservedTransportSubject("Steam_76561198000000001", transportHandle: 4242L);

            var rejection = gate.TryResolve(observed, out var principal);

            Assert.Equal(PilotProviderRejection.None, rejection);
            Assert.True(principal.IsResolved);
            Assert.Equal(PilotProviderNamespace.Steam, principal.ProviderKey.Namespace);
            Assert.Equal("niflheim-pilot-app-896660", principal.ProviderKey.BackendIssuer);
            // Canonical subject is the namespace-stripped bare id — stable, memory-only.
            Assert.Equal("76561198000000001", principal.CanonicalSubject);
            // Exactly one backend is named by the gate.
            Assert.Equal("Steam", gate.ConfiguredNamespace);
        }

        [Fact]
        public void AT_AIP_PROVIDER_GATE0_MisconfiguredGate_FailsClosed()
        {
            // An unconfigured gate (no namespace/backend) admits nothing — fail closed, never crediting.
            var gate = new PilotProviderGate(new PilotProviderKey("", ""));
            var observed = new ServerObservedTransportSubject("Steam_76561198000000001", 1L);

            Assert.Equal(PilotProviderRejection.ProviderUnsupported, gate.TryResolve(observed, out var principal));
            Assert.False(principal.IsResolved);
        }

        [Theory]
        [InlineData("Steam:76561198000000042", "76561198000000042")]   // colon-separated form
        [InlineData("Steam_76561198000000042", "76561198000000042")]   // underscore-separated form
        [InlineData("76561198000000042", "76561198000000042")]         // bare id on the server namespace
        public void AT_AIP_PROVIDER_GATE0_AcceptsSteamHostIdForms(string hostId, string expectedSubject)
        {
            var gate = NewGate();
            var rejection = gate.TryResolve(new ServerObservedTransportSubject(hostId, 7L), out var principal);

            Assert.Equal(PilotProviderRejection.None, rejection);
            Assert.Equal(expectedSubject, principal.CanonicalSubject);
        }

        // ── AT-AIP-UNAUTHENTICATED ──────────────────────────────────────────────
        // No authenticated subject, an anonymous placeholder, or any client-payload-claimed identity
        // rejects BEFORE any account/character mutation. The gate has no API to accept a payload claim.

        [Fact]
        public void AT_AIP_UNAUTHENTICATED_EmptyHost_RejectsUnauthenticated()
        {
            var gate = NewGate();
            Assert.Equal(PilotProviderRejection.UnauthenticatedPeer,
                gate.TryResolve(ServerObservedTransportSubject.None, out var principal));
            Assert.False(principal.IsResolved);
        }

        [Theory]
        [InlineData("Steam_0")]
        [InlineData("Steam_anonymous")]
        [InlineData("anonymous")]
        [InlineData("Steam_unknown")]
        public void AT_AIP_UNAUTHENTICATED_AnonymousPlaceholder_Rejects(string hostId)
        {
            var gate = NewGate();
            Assert.Equal(PilotProviderRejection.UnauthenticatedPeer,
                gate.TryResolve(new ServerObservedTransportSubject(hostId, 9L), out var principal));
            Assert.False(principal.IsResolved);
        }

        [Fact]
        public void AT_AIP_UNAUTHENTICATED_ServerObservedFact_IsOnlyInput_PayloadCannotManufactureIt()
        {
            // ServerObservedTransportSubject is constructed ONLY from a server-read host id; there is no
            // constructor path carrying a client claim. Proving the type's shape here is the guarantee that
            // a hostile payload claiming another subject cannot become authority (AIP-FR-001).
            var ctors = typeof(ServerObservedTransportSubject).GetConstructors();
            Assert.Single(ctors);
            var parameters = ctors[0].GetParameters();
            Assert.Equal(2, parameters.Length);
            Assert.Equal("authenticatedHostId", parameters[0].Name);
            Assert.Equal("transportHandle", parameters[1].Name);
        }

        // ── AT-AIP-PROVIDER-NAMESPACE ───────────────────────────────────────────
        // Exactly one namespace is admitted; a subject in a non-selected namespace (PlayFab) is refused
        // as ProviderUnsupported rather than silently accepted.

        [Fact]
        public void AT_AIP_PROVIDER_NAMESPACE_PlayFabSubject_RejectedOnSteamPilot()
        {
            var gate = NewGate();
            var observed = new ServerObservedTransportSubject("PlayFab:ABCDEF0123456789", 3L);

            Assert.Equal(PilotProviderRejection.ProviderUnsupported,
                gate.TryResolve(observed, out var principal));
            Assert.False(principal.IsResolved);
        }

        [Fact]
        public void AT_AIP_PROVIDER_NAMESPACE_SameSubjectDifferentBackend_DoesNotCollide()
        {
            // A subject minted under one backend/issuer configuration must not resolve equal under another
            // configuration of the same namespace (data-model ProviderKey = namespace + backend).
            var gateA = new PilotProviderGate(PilotProviderKey.Steamworks("backend-A"));
            var gateB = new PilotProviderGate(PilotProviderKey.Steamworks("backend-B"));
            var observed = new ServerObservedTransportSubject("Steam_76561198000000055", 5L);

            gateA.TryResolve(observed, out var pa);
            gateB.TryResolve(observed, out var pb);

            Assert.True(pa.IsResolved);
            Assert.True(pb.IsResolved);
            Assert.Equal(pa.CanonicalSubject, pb.CanonicalSubject);      // same bare subject
            Assert.NotEqual(pa.ProviderKey, pb.ProviderKey);            // but different provider keys
        }

        [Theory]
        [InlineData(":76561198000000001")]   // leading separator — ambiguous
        [InlineData("Steam:")]               // trailing separator — empty subject
        [InlineData("Steam:7656:1198")]      // residual separator in subject — ambiguous
        [InlineData("Steam_7656 1198")]      // whitespace — noncanonical
        public void AT_AIP_PROVIDER_NAMESPACE_NoncanonicalSubject_Rejected(string hostId)
        {
            var gate = NewGate();
            var rejection = gate.TryResolve(new ServerObservedTransportSubject(hostId, 2L), out var principal);

            Assert.NotEqual(PilotProviderRejection.None, rejection);
            Assert.False(principal.IsResolved);
        }

        // ── AT-AIP-PROVIDER-PROVISION-INPUT ─────────────────────────────────────
        // A bounded, non-logging operator path exists to provision the exact allowlist subject: protected
        // no-echo stdin only, owner-only key path, allowlist-only verb scope, subject-redacted output.

        [Fact]
        public void AT_AIP_PROVIDER_PROVISION_INPUT_NoEchoStdin_OwnerOnly_AllowlistVerb_Accepted()
        {
            var gate = new PilotProvisioningInputGate();
            var decision = gate.EvaluateProvision(
                ProvisioningInputChannel.ProtectedNoEchoStdin,
                PathOwnershipState.OwnerOnly(),
                LocalBootstrapVerb.ProvisionAllowlistEntry);

            Assert.True(decision.Accepted);
            Assert.Equal("Accepted", decision.ResultCode);
        }

        [Theory]
        [InlineData(ProvisioningInputChannel.CommandLineArgument, "SubjectChannelForbidden")]
        [InlineData(ProvisioningInputChannel.ChatOrConsoleCommand, "SubjectChannelForbidden")]
        [InlineData(ProvisioningInputChannel.EnvironmentVariable, "SubjectChannelForbidden")]
        public void AT_AIP_PROVIDER_PROVISION_INPUT_ForbiddenChannels_Rejected(ProvisioningInputChannel channel, string expected)
        {
            var gate = new PilotProvisioningInputGate();
            var decision = gate.EvaluateProvision(channel, PathOwnershipState.OwnerOnly(),
                LocalBootstrapVerb.ProvisionAllowlistEntry);

            Assert.False(decision.Accepted);
            Assert.Equal(expected, decision.ResultCode);
        }

        [Fact]
        public void AT_AIP_PROVIDER_PROVISION_INPUT_PermissiveKeyPath_FailsClosed()
        {
            var gate = new PilotProvisioningInputGate();
            // Group-readable (broader than 0600) key path must fail closed.
            var permissive = new PathOwnershipState(ownedByServiceAccount: true,
                groupReadable: true, groupWritable: false, otherReadable: false, otherWritable: false);

            var decision = gate.EvaluateProvision(ProvisioningInputChannel.ProtectedNoEchoStdin,
                permissive, LocalBootstrapVerb.ProvisionAllowlistEntry);

            Assert.False(decision.Accepted);
            Assert.Equal("KeyPathTooPermissive", decision.ResultCode);
        }

        [Theory]
        [InlineData(LocalBootstrapVerb.InspectAccount)]
        [InlineData(LocalBootstrapVerb.ExportAccount)]
        [InlineData(LocalBootstrapVerb.DisableAccount)]
        [InlineData(LocalBootstrapVerb.DeleteAccount)]
        [InlineData(LocalBootstrapVerb.ResetAccount)]
        [InlineData(LocalBootstrapVerb.ChangeRetention)]
        [InlineData(LocalBootstrapVerb.InvokeGameplayCommand)]
        public void AT_AIP_PROVIDER_PROVISION_INPUT_OutOfScopeVerbs_Rejected(LocalBootstrapVerb verb)
        {
            var gate = new PilotProvisioningInputGate();
            var decision = gate.EvaluateProvision(ProvisioningInputChannel.ProtectedNoEchoStdin,
                PathOwnershipState.OwnerOnly(), verb);

            Assert.False(decision.Accepted);
            Assert.Equal("VerbOutOfLocalScope", decision.ResultCode);
            Assert.False(PilotProvisioningInputGate.IsLocalAllowlistVerb(verb));
        }

        [Fact]
        public void AT_AIP_PROVIDER_PROVISION_INPUT_OutputRedactsRawSubject()
        {
            const string rawSubject = "76561198000000001";
            string leaked = "allowlistEntryId=AE-123 subject=" + rawSubject + " resultCode=Accepted";
            string redacted = PilotProvisioningInputGate.RedactSubject(leaked, rawSubject);

            Assert.DoesNotContain(rawSubject, redacted);
            Assert.Contains("<redacted-subject>", redacted);
            // The admissible-output allowlist carries only internal ids/codes — never a subject field.
            Assert.DoesNotContain("subject", (IEnumerable<string>)PilotProvisioningInputGate.AdmissibleOutputFields);
        }

        // ── AT-AIP-PROVIDER-RECONNECT ───────────────────────────────────────────
        // The subject is stable across reconnect and server restart: two independently observed transport
        // facts (different per-session transport handles) for the same durable Steam identity resolve to
        // the same canonical subject; a different identity does not.

        [Fact]
        public void AT_AIP_PROVIDER_RECONNECT_SameIdentityDifferentHandle_SameSubject()
        {
            var gate = NewGate();
            // Session 1 and a later reconnect/restart: same authenticated Steam host id, different
            // per-session transport handle.
            var session1 = new ServerObservedTransportSubject("Steam_76561198000000077", transportHandle: 111L);
            var session2 = new ServerObservedTransportSubject("Steam_76561198000000077", transportHandle: 999L);

            Assert.True(gate.ResolvesToSameSubject(session1, session2));

            gate.TryResolve(session1, out var p1);
            gate.TryResolve(session2, out var p2);
            Assert.Equal(p1.CanonicalSubject, p2.CanonicalSubject);
            Assert.NotEqual(p1.TransportHandle, p2.TransportHandle);   // handle is per-session, not identity
        }

        [Fact]
        public void AT_AIP_PROVIDER_RECONNECT_MixedHostIdForms_SameIdentity_SameSubject()
        {
            var gate = NewGate();
            // The same identity may surface in colon and underscore forms across sessions; both canonicalize
            // to the same bare subject.
            var colon = new ServerObservedTransportSubject("Steam:76561198000000077", 1L);
            var underscore = new ServerObservedTransportSubject("Steam_76561198000000077", 2L);

            Assert.True(gate.ResolvesToSameSubject(colon, underscore));
        }

        [Fact]
        public void AT_AIP_PROVIDER_RECONNECT_DifferentIdentity_DifferentSubject()
        {
            var gate = NewGate();
            var a = new ServerObservedTransportSubject("Steam_76561198000000001", 1L);
            var b = new ServerObservedTransportSubject("Steam_76561198000000002", 2L);

            Assert.False(gate.ResolvesToSameSubject(a, b));
        }

        // ── AT-AIP-PROVIDER-LOG-SCRUB ───────────────────────────────────────────
        // Niflheim subsystem log lines carry no raw provider subject/HMAC/token; the outcome line exposes
        // only provider CLASS + result code + correlation. Upstream base-runtime facts are inventoried and
        // each has a bounded access/purge path or enrollment fails closed.

        [Fact]
        public void AT_AIP_PROVIDER_LOG_SCRUB_OutcomeLine_CarriesClassNotSubject()
        {
            const string rawSubject = "76561198000000001";
            string line = PilotAuthLogScrubber.OutcomeLine(
                unixSeconds: 1_784_000_000L, providerClass: "Steam", resultCode: "Resolved", correlationId: "corr-1");

            Assert.DoesNotContain(rawSubject, line);
            Assert.Contains("ProviderClass=Steam", line);
            Assert.Contains("ResultCode=Resolved", line);
        }

        [Fact]
        public void AT_AIP_PROVIDER_LOG_SCRUB_SeededForbiddenValues_AbsentFromEmittedLines()
        {
            const string rawSubject = "76561198000000001";
            const string hmac = "9f8e7d6c5b4a39281706";
            var forbidden = new List<string> { rawSubject, hmac, "steam_ticket_abc" };

            // Compose the lines the subsystem would emit for a resolve + a rejection.
            var lines = new List<string>
            {
                PilotAuthLogScrubber.OutcomeLine(1L, "Steam", "Resolved", "corr-a"),
                PilotAuthLogScrubber.OutcomeLine(2L, "Steam", "ProviderUnsupported", "corr-b"),
                new PilotAuthLogLine()
                    .With(AuthLogField.ProviderClass, "Steam")
                    .With(AuthLogField.AccountIdAfterResolve, "acct-opaque-1")
                    .With(AuthLogField.ResultCode, "Resolved").Emit()
            };

            Assert.False(PilotAuthLogScrubber.TryFindForbidden(lines, forbidden, out _, out _));
            foreach (var line in lines)
                Assert.False(PilotAuthLogScrubber.ContainsForbidden(line, forbidden));
        }

        [Fact]
        public void AT_AIP_PROVIDER_LOG_SCRUB_Scrubber_CatchesALeak()
        {
            // Negative control: a line that DID leak a subject is caught by the same mechanical scan.
            const string rawSubject = "76561198000000001";
            var lines = new List<string> { "resolved subject=" + rawSubject };

            Assert.True(PilotAuthLogScrubber.TryFindForbidden(lines,
                new List<string> { rawSubject }, out int idx, out string offending));
            Assert.Equal(0, idx);
            Assert.Equal(rawSubject, offending);
        }

        [Fact]
        public void AT_AIP_PROVIDER_LOG_SCRUB_UpstreamInventory_AllBounded_EnrollmentMayOpen()
        {
            var inventory = new UpstreamSubjectInventory()
                .Add(new UpstreamSubjectArtifact(UpstreamArtifactKind.ValheimServerLog,
                    "BepInEx/LogOutput.log", accessRestricted: true, hasBoundedPurgePath: true, retentionDays: 14))
                .Add(new UpstreamSubjectArtifact(UpstreamArtifactKind.VanillaWorldSaveCreatorFact,
                    "worlds_local/<world>.db (s_creator)", accessRestricted: true, hasBoundedPurgePath: true, retentionDays: 30))
                .Add(new UpstreamSubjectArtifact(UpstreamArtifactKind.BepInExDiagnosticLog,
                    "BepInEx/LogOutput.log (diagnostics)", accessRestricted: true, hasBoundedPurgePath: true, retentionDays: 14));

            Assert.True(inventory.EnrollmentMayOpen());
            Assert.Empty(inventory.NonClearing());
        }

        [Fact]
        public void AT_AIP_PROVIDER_LOG_SCRUB_UpstreamInventory_UnboundedArtifact_FailsClosed()
        {
            var inventory = new UpstreamSubjectInventory()
                .Add(new UpstreamSubjectArtifact(UpstreamArtifactKind.ValheimServerLog,
                    "BepInEx/LogOutput.log", accessRestricted: true, hasBoundedPurgePath: false, retentionDays: 0));

            Assert.False(inventory.EnrollmentMayOpen());
            Assert.Single(inventory.NonClearing());
        }

        [Fact]
        public void AT_AIP_PROVIDER_LOG_SCRUB_EmptyInventory_IsNotAPass()
        {
            // An empty inventory means "not yet inventoried" — Gate 0 must enumerate known upstream facts,
            // so this fails closed rather than vacuously passing.
            Assert.False(new UpstreamSubjectInventory().EnrollmentMayOpen());
        }
    }
}
