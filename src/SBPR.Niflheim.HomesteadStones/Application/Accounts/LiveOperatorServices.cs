using System;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;
using SBPR.Niflheim.HomesteadStones.Persistence.Accounts;

namespace SBPR.Niflheim.HomesteadStones.Application.Accounts
{
    // IAP-015 — the SINGLE shared live-operator service bundle (engine-free CLEAN core).
    //
    // The IAP-015 gap this closes: the operator DECISION cores (OperatorAccountService, PilotPrivacyService,
    // PilotDestructionService, OperatorAdminGate) shipped with NO net48 ingress, and the live admission
    // observer composed its OWN account store + privacy service inline. That meant a live operator command,
    // had one existed, could have inspected a DIFFERENT in-memory universe than the one admission mutates.
    //
    // This bundle is the ONE composition root: it builds every account/lifecycle/privacy/destruction service
    // over exactly ONE PilotAccountStore, ONE PilotSessionRegistry, ONE AccountMutationFence, and ONE
    // BoundSessionPrincipalIndex, plus the LiveSessionAdmission used by the live path — so the operator
    // command ingress and the session-admission observer provably act on the SAME durable store and the SAME
    // process-local session/binding state. A join-created account is immediately inspectable through the
    // operator ingress; an operator disable actually drops the live session admission published; a restart
    // rehydrates the same durable store.
    //
    // net48 audit: System.* + engine-free account/identity cores only. No UnityEngine/Valheim/BepInEx, so
    // the whole composition (including the router it feeds) is exercised under net8.
    public sealed class LiveOperatorServices
    {
        public LiveOperatorServices(
            PilotAccountStore store,
            PilotSessionRegistry sessions,
            AccountMutationFence fence,
            OperatorAdminGate adminGate,
            OperatorAccountService accounts,
            PilotPrivacyService privacy,
            PilotDestructionService destruction,
            LiveSessionAdmission liveAdmission,
            PilotRetentionPolicy retentionPolicy,
            string worldFixtureLocator)
        {
            Store = store ?? throw new ArgumentNullException(nameof(store));
            Sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            Fence = fence ?? throw new ArgumentNullException(nameof(fence));
            AdminGate = adminGate ?? throw new ArgumentNullException(nameof(adminGate));
            Accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            Privacy = privacy ?? throw new ArgumentNullException(nameof(privacy));
            Destruction = destruction ?? throw new ArgumentNullException(nameof(destruction));
            LiveAdmission = liveAdmission ?? throw new ArgumentNullException(nameof(liveAdmission));
            RetentionPolicy = retentionPolicy ?? throw new ArgumentNullException(nameof(retentionPolicy));
            WorldFixtureLocator = worldFixtureLocator ?? string.Empty;
        }

        /// <summary>The one durable account store both admission and the operator ingress read/mutate.</summary>
        public PilotAccountStore Store { get; }

        /// <summary>The one process-local session registry admission publishes into and the operator disable/
        /// delete path deterministically closes against.</summary>
        public PilotSessionRegistry Sessions { get; }

        public AccountMutationFence Fence { get; }
        public OperatorAdminGate AdminGate { get; }
        public OperatorAccountService Accounts { get; }
        public PilotPrivacyService Privacy { get; }
        public PilotDestructionService Destruction { get; }

        /// <summary>The live session-admission orchestrator the net48 observer drives — composed over the SAME
        /// store, so a join-created account is visible to the operator ingress immediately.</summary>
        public LiveSessionAdmission LiveAdmission { get; }

        public PilotRetentionPolicy RetentionPolicy { get; }

        /// <summary>The server-owned world-save fixture locator the operator <c>open-pilot</c> catalogs and
        /// binds the fail-closed admission gate to. Empty until the composition supplies one.</summary>
        public string WorldFixtureLocator { get; }

        /// <summary>Standard drain timeout for the operator/privacy mutation fence (shared with the QA host).</summary>
        public static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(5);

        /// <summary>Compose the full shared operator+admission service bundle over one store + collaborators.
        /// The net48 observer calls this once at ZNet.Awake; the tests call it with in-memory collaborators.
        /// The admin gate is supplied by the caller (live adminlist provider in production, a fixed/mutable
        /// list in tests) so the same composition proves both dynamic-adminlist and fixed-list behavior.</summary>
        public static LiveOperatorServices Compose(
            PilotAccountStore store,
            PilotAccountService accountService,
            PilotCharacterAdmissionService characterService,
            BoundSessionPrincipalIndex boundSessions,
            OperatorAdminGate adminGate,
            PilotRetentionPolicy retentionPolicy,
            string worldFixtureLocator,
            IPrivacyAdmissionGate? privacyGate = null,
            PilotSessionRegistry? sessions = null,
            AccountMutationFence? fence = null,
            TimeSpan? drainTimeout = null)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (accountService == null) throw new ArgumentNullException(nameof(accountService));
            if (characterService == null) throw new ArgumentNullException(nameof(characterService));
            if (boundSessions == null) throw new ArgumentNullException(nameof(boundSessions));
            if (adminGate == null) throw new ArgumentNullException(nameof(adminGate));
            if (retentionPolicy == null) throw new ArgumentNullException(nameof(retentionPolicy));

            var reg = sessions ?? new PilotSessionRegistry();
            var mutationFence = fence ?? new AccountMutationFence();
            var timeout = drainTimeout ?? DefaultDrainTimeout;

            var operatorAccounts = new OperatorAccountService(store, adminGate, mutationFence, reg, timeout);
            var privacy = new PilotPrivacyService(store, adminGate, mutationFence, timeout);
            var destruction = new PilotDestructionService(store, adminGate, mutationFence, privacy, timeout);

            // The live admission path publishes into the SAME store + bound-session index + session registry.
            var liveAdmission = new LiveSessionAdmission(accountService, characterService, boundSessions, privacyGate, reg);

            return new LiveOperatorServices(store, reg, mutationFence, adminGate, operatorAccounts,
                privacy, destruction, liveAdmission, retentionPolicy, worldFixtureLocator);
        }
    }
}
