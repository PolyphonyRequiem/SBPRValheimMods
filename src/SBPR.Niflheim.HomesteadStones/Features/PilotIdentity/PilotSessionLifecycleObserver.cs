using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Application.Accounts;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Features.Progression;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;

namespace SBPR.Niflheim.HomesteadStones.Features.PilotIdentity
{
    /// <summary>
    /// IAP-007W — the net48-ONLY LIVE session-admission seam. THIS is what closes the PR #338 pre-merge
    /// gap: on a real authoritative server it composes the shipped account+character admission stack
    /// (Tracer 1/2) and drives it from server-observed peer facts so that the acting peer's BOUND INTERNAL
    /// principal is actually PUBLISHED into <see cref="FoundationalProgressionServer.BoundSessions"/> (the
    /// index the placement observer/ingress already resolve against and previously always found empty).
    ///
    /// Composition (server-only, lazy from the same ZNet as the Foundational runtime): a durable
    /// PilotAccountStore + persisted LookupKeyRing under a server-owned key directory, the Gate-0 provider
    /// gate configured for the one Steamworks pilot backend, and a <see cref="LiveSessionAdmission"/> over
    /// the SERVER's own BoundSessionPrincipalIndex — so the principal it publishes is exactly what the
    /// gameplay path resolves.
    ///
    /// Lifecycle without a brittle single disconnect hook: this reconciles against the authoritative
    /// connected-peer set on the ZDOMan.Update cadence (the same pump the placement queue uses). Each tick:
    ///   * ADMIT — for every connected peer whose server-owned character ZDO now exposes a nonzero
    ///     s_playerID (profile chosen) and an authenticated socket host, and which is not yet admitted,
    ///     run the full fail-closed admission (account resolve → lease → character → activate+bind). An
    ///     un-allowlisted / disabled subject rejects and NOTHING binds (correct fail-closed). Identity is
    ///     100% server-observed off the transport-authenticated peer — never a client payload.
    ///   * CLOSE — for every previously-admitted transport handle no longer in the connected set, close the
    ///     session (release the lease + session-qualified unbind of the live principal).
    ///
    /// One-session and stale-disconnect semantics are the admission lease's + the session-qualified index
    /// unbind's; a reconnect re-binds under the same durable peer key and a late close cannot tear it down.
    ///
    /// References Valheim (ZNet, ZNetPeer, ZDOMan) → net48-only, not link-compiled. Every decision it makes
    /// lives in the engine-free, unit-tested LiveSessionAdmission / BoundSessionAdmission cores.
    /// </summary>
    [HarmonyPatch]
    internal static class PilotSessionLifecycleObserver
    {
        // The one Gate-0-proven Steamworks pilot backend/issuer. Matches ZdoPilotProviderSubjectSource /
        // PilotProviderGate docs and the dedicated server proven by PR #317.
        internal const string PilotBackendIssuer = "niflheim-pilot-app-896660";

        private static ZNet? composedFor;
        private static LiveSessionAdmission? live;
        private static PilotProviderGate? providerGate;

        /// <summary>IAP-015 — the SHARED live operator service bundle composed at ZNet.Awake over the SAME
        /// durable store + session registry + bound-session index this observer drives admission through. The
        /// operator command ingress (OperatorCommandIngressObserver) reads it so operator commands act on the
        /// exact live universe admission mutates. Null on a client / before composition.</summary>
        internal static LiveOperatorServices? OperatorServices { get; private set; }

        // transportHandle -> true for peers this observer has admitted, so it admits once and closes on
        // disconnect. Purely process-local (mirrors the admission index): cleared on restart.
        private static readonly HashSet<long> admittedTransports = new HashSet<long>();

        // Per-connection monotonic seed so each admission's operation ids are distinct across reconnects.
        private static long opSeedCounter;

        [HarmonyPatch(typeof(ZNet), "Awake")]
        [HarmonyPostfix]
        private static void OnZNetAwake(ZNet __instance)
        {
            try
            {
                if (__instance == null || !__instance.IsServer()) return;
                if (ReferenceEquals(composedFor, __instance) && live != null) return;

                var server = FoundationalPlacementObserver.Server;
                if (server == null) return;   // the Foundational runtime composes first (same ZNet.Awake)

                string durableDir = server.DurableDirectory;
                var accountStore = new PilotAccountStore(
                    System.IO.Path.Combine(durableDir, "pilot-account.journal"));
                var keyRing = PilotKeyRingFile.LoadOrCreate(System.IO.Path.Combine(durableDir, "keys"));

                var accounts = new PilotAccountService(
                    accountStore, keyRing, PilotDisclosureVersions.NoticeVersion, PilotDisclosureVersions.RetentionVersion);
                var characters = new PilotCharacterAdmissionService(accountStore, keyRing, new AccountAdmissionIndex());

                // IAP-015 fix: compose the operator DECISION cores + the live admission over ONE shared store,
                // session registry, mutation fence, and bound-session index — the exact gap EXECUTE run 1426
                // exposed. Previously this observer built its own inline PilotAccountStore + PilotPrivacyService
                // with NO operator ingress; now the operator command surface (OperatorCommandIngressObserver)
                // and this admission path provably act on the SAME universe.
                //
                // The admin gate reads the LIVE server-owned adminlist.txt on EVERY authorization (via a
                // provider closure over ZNet.GetAdminList) — never a boot snapshot — so an admin removed
                // mid-run is rejected on the next command (fail closed on removal). Payload identity is never
                // authority (AIP-FR-019).
                var adminGate = new OperatorAdminGate(() =>
                {
                    var z = ZNet.instance;
                    var list = z != null ? z.GetAdminList() : null;
                    return list != null ? new List<string>(list) : (IReadOnlyCollection<string>)Array.Empty<string>();
                });

                var retentionPolicy = PilotRetentionPolicy.ShippedDefault(PilotDisclosureVersions.RetentionVersion);

                // The active world-save fixture locator: the server's own world name+uid, server-derived
                // (never a client claim). open-pilot catalogs THIS fixture and binds the admission gate to it.
                string worldFixture = SafeWorldFixtureLocator(__instance);

                var boundSessions = server.BoundSessions;

                // Rehydrate the privacy admission gate from the durable journal exactly as before: if an Active
                // pilot + cataloged WorldSave already exist, ENFORCE the fail-closed gate; else leave it open
                // (server not bricked mid-migration) — logged prominently. The privacy SERVICE is composed by
                // LiveOperatorServices.Compose over the same store, so we resolve the gate from that service.
                var services = LiveOperatorServices.Compose(
                    accountStore, accounts, characters, boundSessions, adminGate,
                    retentionPolicy, worldFixture, privacyGate: null);

                var activePilot = accountStore.Pilots.FirstOrDefault(p => p.Status == PilotLifecycleStatus.Active);
                var worldFixtureArtifact = accountStore.Artifacts.FirstOrDefault(a =>
                    a.ArtifactType == PilotArtifactType.WorldSave && a.Status == ArtifactStatus.Active);
                IPrivacyAdmissionGate? privacyGate = null;
                if (activePilot != null && worldFixtureArtifact != null)
                {
                    services.Privacy.ConfigureAdmission(activePilot.PilotId, worldFixtureArtifact.StorageLocator);
                    privacyGate = services.Privacy;
                    Plugin.Log.LogInfo("[Niflheim/HomesteadStones] Privacy admission gate ENFORCED for pilot='"
                        + activePilot.PilotId.Value + "' worldFixture='" + worldFixtureArtifact.StorageLocator + "'.");
                }
                else
                {
                    Plugin.Log.LogWarning("[Niflheim/HomesteadStones] No open pilot + cataloged world fixture in the "
                        + "durable journal; privacy admission gate NOT enforced (open a pilot via the operator "
                        + "open-pilot command to fail closed on closure/uncataloged fixtures).");
                }

                // Recompose the live admission WITH the resolved privacy gate but over the SAME shared store,
                // session registry, and bound-session index (so operator + admission stay one universe).
                live = new LiveSessionAdmission(accounts, characters, boundSessions, privacyGate, services.Sessions);
                OperatorServices = services;
                providerGate = new PilotProviderGate(PilotProviderKey.Steamworks(PilotBackendIssuer));
                admittedTransports.Clear();
                composedFor = __instance;

                Plugin.Log.LogInfo(
                    "[Niflheim/HomesteadStones] Live session admission + operator command surface composed " +
                    "(server-authoritative, shared store). durable='" + durableDir + "' provider=" +
                    providerGate.DescribeProviderClass() + ".");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Live session admission composition failed: " + ex);
            }
        }

        [HarmonyPatch(typeof(ZNet), "OnDestroy")]
        [HarmonyPostfix]
        private static void OnZNetDestroyed(ZNet __instance)
        {
            if (ReferenceEquals(composedFor, __instance))
            {
                composedFor = null;
                live = null;
                providerGate = null;
                OperatorServices = null;
                admittedTransports.Clear();
            }
        }

        /// <summary>The server-owned world-save fixture locator, derived from the server's own world name +
        /// durable world UID (never a client claim). Used as the WorldSave artifact locator that open-pilot
        /// catalogs and the fail-closed admission gate binds to. Falls back to a stable marker if the world
        /// is not loaded yet.</summary>
        private static string SafeWorldFixtureLocator(ZNet znet)
        {
            try
            {
                string name = znet.GetWorldName() ?? "world";
                long uid = znet.GetWorldUID();
                return "world-save:" + name + "/" + uid.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return "world-save:unknown";
            }
        }

        /// <summary>Reconcile admitted sessions against the authoritative connected-peer set once per
        /// ZDOMan.Update tick: admit newly-resolvable peers, close peers that disconnected.</summary>
        [HarmonyPatch(typeof(ZDOMan), "Update")]
        [HarmonyPostfix]
        private static void OnZdoManUpdate()
        {
            try
            {
                var znet = ZNet.instance;
                var admission = live;
                var gate = providerGate;
                if (znet == null || !znet.IsServer() || admission == null || gate == null) return;

                var connected = znet.GetConnectedPeers();
                if (connected == null) return;

                var seen = new HashSet<long>();
                foreach (var peer in connected)
                {
                    if (peer == null) continue;
                    long transport = peer.m_uid;
                    seen.Add(transport);
                    if (admittedTransports.Contains(transport)) continue;   // already admitted this session

                    if (!ZdoAuthenticatedSenderSource.Instance.TryResolveFromPeer(peer, out var facts))
                        continue;   // no character ZDO / no s_playerID yet — try again next tick

                    // Provider principal (account subject) from the authenticated socket host id.
                    var observed = new ServerObservedTransportSubject(facts.PlatformSubject, transport);
                    if (gate.TryResolve(observed, out var provider) != PilotProviderRejection.None)
                        continue;   // unauthenticated / unsupported provider — never admit

                    var profile = new VerifiedProfileSubject(facts.PlayerId, transport);
                    string peerKey = ServerCreatorIdentity.CharacterSubject(facts.PlayerId);
                    string opSeed = transport.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + "-" + (++opSeedCounter).ToString(System.Globalization.CultureInfo.InvariantCulture);

                    var result = admission.Admit(peerKey, provider, profile, transport, DateTime.UtcNow.Ticks, opSeed);
                    if (result.Admitted)
                    {
                        admittedTransports.Add(transport);
                        Plugin.Log.LogInfo("[Niflheim/HomesteadStones] " + result.ToOperatorLine());
                    }
                    else
                    {
                        // Fail closed on a genuine rejection (wound-down account barrier, unsupported
                        // provider, disabled/deleted/quarantined owner). Logging once per resolvable-but-
                        // rejected peer would spam, so mark it and don't retry every tick. Normal first
                        // authenticated joins no longer reject here — they auto-create an opaque account.
                        admittedTransports.Add(transport);
                        Plugin.Log.LogInfo("[Niflheim/HomesteadStones] " + result.ToOperatorLine());
                    }
                }

                // Close sessions whose transport handle is no longer connected (disconnect).
                if (admittedTransports.Count > 0)
                {
                    List<long>? gone = null;
                    foreach (var transport in admittedTransports)
                        if (!seen.Contains(transport)) (gone ??= new List<long>()).Add(transport);
                    if (gone != null)
                        foreach (var transport in gone)
                        {
                            admittedTransports.Remove(transport);
                            admission.Close(transport);
                        }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[Niflheim/HomesteadStones] Live session reconcile threw: " + ex);
            }
        }
    }

    /// <summary>The pilot's configured disclosure/retention notice versions for the live account service.
    /// These match the versions an operator provisions allowlist entries under (the local bootstrap CLI);
    /// a first-bind only proceeds when the matched allowlist entry acknowledges this exact notice version
    /// (PilotAccountService.DisclosureIncomplete otherwise), so both sides MUST agree.</summary>
    internal static class PilotDisclosureVersions
    {
        internal const string NoticeVersion = "notice-v1";
        internal const string RetentionVersion = "retention-v1";
    }
}
