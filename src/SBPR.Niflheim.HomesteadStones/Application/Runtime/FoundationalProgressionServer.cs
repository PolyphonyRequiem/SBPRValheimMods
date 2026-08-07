using System;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Activities;
using SBPR.Niflheim.HomesteadStones.Application.Activation;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Application.ResourceDelivery;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // T009 — the engine-free composition root for the live Foundational AP runtime. It assembles the
    // shipped, durable slice — the AP operation-receipt journal, the relationship lifecycle journal,
    // the authority/character/Stone/character-AP projection sinks, the hardened placement adapter, and
    // the relationship-backed pipeline — into a single FoundationalPlacementRuntime, wired to a stable
    // server-owned durable directory.
    //
    // Startup rehydration is structural: constructing OperationReceiptStore replays the AP journal onto
    // its Stone/character sinks, and constructing RelationshipCommandHandler replays the relationship
    // journal onto the shared authority/character stores. Because the SAME authority store instance is
    // handed to both the relationship handler (which rehydrates it) and the pipeline's
    // RelationshipPlacementAuthorizer (which reads it), a restarted server authorizes and credits
    // exactly the state the durable journals hold — no relationship or receipt is lost or duplicated.
    //
    // The Stone AP sink is injectable: production hands the ZDO-backed ZdoStoneProgressionStore (which
    // ALSO re-stamps the world Stone ZDO during rehydration); the net8 tests hand the engine-free
    // InMemoryMirroredStoneApStore. Everything else is engine-free, so this whole root — including
    // rehydration and the live Observe path — is exercised by the net8 test project. Production merely
    // supplies the ZDO sink and the real per-Stone family classification.
    //
    // net48 audit: System.IO (Directory/Path) + shipped engine-free types only. No net5+ surface, no
    // UnityEngine/Valheim/BepInEx reference, so it link-compiles into the net8 test project.
    public sealed class FoundationalProgressionServer
    {
        public const string ApJournalFile = "foundational-ap.journal";
        public const string RelationshipJournalFile = "relationships.journal";
        public const string ConnectionSourceJournalFile = "connection-sources.journal";

        /// <summary>Stable product discriminator for this mod's Connection graph (data-model §Stable
        /// identities). Keeps another product that happens to share Account IDs out of this graph.</summary>
        public const string ConnectionProduct = "SBPR.Trailborne";

        private FoundationalProgressionServer(
            FoundationalPlacementRuntime runtime,
            RelationshipCommandHandler relationships,
            IAccountStoneAuthorityStore authority,
            ICharacterAggregateStore characters,
            IMirroredStoneApStore stoneApStore,
            ICharacterApStore characterApStore,
            OperationReceiptStore receipts,
            StoneAreaMembership stoneAreas,
            ServerObservedCharacterPositions characterPositions,
            PendingRevalidationQueue pendingPlacements,
            BoundSessionPrincipalIndex boundSessions,
            StoneConnectionSourceRegistry connectionSources,
            TimeSpan warriorTwigPendingDeadline,
            string durableDirectory)
        {
            Runtime = runtime;
            Relationships = relationships;
            Authority = authority;
            Characters = characters;
            StoneApStore = stoneApStore;
            CharacterApStore = characterApStore;
            Receipts = receipts;
            StoneAreas = stoneAreas;
            CharacterPositions = characterPositions;
            PendingPlacements = pendingPlacements;
            BoundSessions = boundSessions;
            ConnectionSources = connectionSources;
            _warriorTwigPendingDeadline = warriorTwigPendingDeadline;
            DurableDirectory = durableDirectory;
        }

        private readonly TimeSpan _warriorTwigPendingDeadline;

        public FoundationalPlacementRuntime Runtime { get; }
        public RelationshipCommandHandler Relationships { get; }
        public IAccountStoneAuthorityStore Authority { get; }
        public ICharacterAggregateStore Characters { get; }
        public IMirroredStoneApStore StoneApStore { get; }
        public ICharacterApStore CharacterApStore { get; }
        public OperationReceiptStore Receipts { get; }

        /// <summary>Server-owned Stone Area membership the observer registers resident Stones into and
        /// queries per placement. Populated by the engine-bound layer from world Stone facts; empty
        /// until a Stone is registered (an empty membership resolves every position to OutsideStoneArea).</summary>
        public StoneAreaMembership StoneAreas { get; }

        /// <summary>ADO #138 — server-observed world positions of acting characters, published by the
        /// engine-bound layer from each peer's own character ZDO. This is the position half of the
        /// server-checked proximity gate on Bond/Attunement formation; <see cref="StoneAreas"/> is the
        /// Area half. Non-durable by design: a restart clears it and the next server-side observation
        /// republishes, and an unknown position fails closed (the relationship command rejects
        /// <c>NotAtStone</c>).</summary>
        public ServerObservedCharacterPositions CharacterPositions { get; }

        /// <summary>T009R4 (Blocker 5) — the bounded pending-revalidation queue that absorbs the ZDO
        /// replication race. A dedicated-client placement notice captures the transport-authenticated
        /// sender identity here and defers the credit-bearing ingest until the physical ZDO replicates to
        /// the server (or a short deadline expires, writing no credit). The net48 layer pumps it on the
        /// ZDOMan replication cadence. Purely in-memory: a restart starts empty and never re-scans.</summary>
        public PendingRevalidationQueue PendingPlacements { get; }

        /// <summary>IAP-007 Tracer 3 — the process-local bound-session principal index. Admission
        /// (Tracer 1/2) publishes each connected peer's minted internal (AccountId, CharacterId,
        /// SessionId) here; the live placement observer resolves the acting peer's BOUND INTERNAL
        /// principal from it instead of deriving one from a raw provider subject. Non-durable:
        /// cleared on restart, republished by admission on reconnect.</summary>
        public BoundSessionPrincipalIndex BoundSessions { get; }

        /// <summary>RD-T004 — the durable Connection source coordinator. Every committed
        /// Bond/Attunement/Release through <see cref="Relationships"/> drives the matching account-pair
        /// Connection source transition in the SAME logical transaction, and this coordinator's projections
        /// are reconstructed from the same relationship journal on restart.</summary>
        public StoneConnectionSourceRegistry ConnectionSources { get; }

        /// <summary>T029 — the Warrior T.W.I.G. Training Local placement gate. Composes the shipped pure
        /// LocalPlacementProvider with the AUTHORITATIVE Stone aggregate + governance projection from the
        /// merged shared Local Effect runtime (t_02c13405 / PR #368), the composed relationship authority,
        /// the bound-session index, and the Stone Area membership, so a joined client's exact T.W.I.G.
        /// (TrainingDummy) placement is admitted/refused by the FR-016 effect-active / Settlement-policy /
        /// build-Permission AND. Null until <see cref="ArmWarriorTwig"/> composes it against the live
        /// LocalProgressionServer (the engine-bound bootstrap arms it right after composing that runtime).
        /// The engine-bound observer/ingress route a server-observed placement through this and undo it on
        /// refusal.</summary>
        public WarriorLocalPlacementGate? WarriorTwigGate { get; private set; }

        /// <summary>T029 — the bounded pending queue that absorbs the ZDO replication race for a joined
        /// DEDICATED-server client's T.W.I.G. placement. The dedicated notice captures the
        /// transport-authenticated sender + candidate ZDOID here; the net48 layer pumps it on the
        /// ZDOMan.Update cadence, gating (and undoing on refusal) once the ZDO replicates. In-memory: a
        /// restart starts empty and never re-acts on old resident pieces. Null until
        /// <see cref="ArmWarriorTwig"/>.</summary>
        public WarriorTwigPendingUndoQueue? WarriorTwigPending { get; private set; }

        /// <summary>T029 — compose the Warrior T.W.I.G. gate against the AUTHORITATIVE Stone aggregate store
        /// and governance resolver from the merged shared Local Effect runtime, and arm the pending queue.
        /// Idempotent-ish: re-arming replaces the gate/queue. Called by the engine-bound bootstrap right
        /// after it composes the LocalProgressionServer (so the gate reads the same authoritative
        /// projection), and by the tests after they compose that runtime. Everything else the gate needs
        /// (bound sessions, authority, Stone areas) is this server's already-composed state.</summary>
        public void ArmWarriorTwig(IStoneAggregateStore stones, GovernorPresenceResolver governorPresence)
        {
            if (stones == null) throw new ArgumentNullException(nameof(stones));
            if (governorPresence == null) throw new ArgumentNullException(nameof(governorPresence));
            WarriorTwigGate = new WarriorLocalPlacementGate(
                stones, governorPresence, Authority, BoundSessions, StoneAreas);
            WarriorTwigPending = new WarriorTwigPendingUndoQueue(_warriorTwigPendingDeadline);
        }

        public string DurableDirectory { get; }

        /// <summary>Build the DEDICATED-server placement ingress over this server's shared validation
        /// core. Both host shapes converge here: the listen-host observer calls <c>Runtime.Observe</c>
        /// directly (its PlacePiece runs on the server), while a joined dedicated-server client's build
        /// arrives as a notice this ingress revalidates against the caller-supplied server-owned ZDO
        /// source, then routes through the SAME <see cref="Runtime"/> (adapter → pipeline → receipt) and
        /// the SAME <see cref="StoneAreas"/>. Production supplies a ZDOMan-backed instance source; tests
        /// supply an in-memory one. There is deliberately no client-authoritative fallback: every
        /// credit-bearing fact is re-derived from <paramref name="instances"/>, never the notice.</summary>
        public DedicatedPlacementIngress CreateDedicatedIngress(IServerPlacedInstanceSource instances)
        {
            if (instances == null) throw new ArgumentNullException(nameof(instances));
            return new DedicatedPlacementIngress(Runtime, instances, StoneAreas, BoundSessions, FoundationalPrefabMap.CurrentBuild);
        }

        /// <summary>T029 — build the DEDICATED-server Warrior T.W.I.G. ingress over this server's shared
        /// <see cref="WarriorTwigGate"/>. A joined dedicated-server client's T.W.I.G. build arrives as a
        /// notice this ingress revalidates against the caller-supplied server-owned ZDO source, then routes
        /// through the SAME gate (and undoes the placement on refusal). Production supplies a ZDOMan-backed
        /// instance source; tests supply an in-memory one. No client-authoritative fact is trusted.</summary>
        public WarriorTwigDedicatedIngress CreateWarriorTwigDedicatedIngress(IServerPlacedInstanceSource instances)
        {
            if (instances == null) throw new ArgumentNullException(nameof(instances));
            if (WarriorTwigGate == null)
                throw new InvalidOperationException(
                    "Warrior T.W.I.G. gate is not armed. Call ArmWarriorTwig(...) after composing the LocalProgressionServer.");
            return new WarriorTwigDedicatedIngress(WarriorTwigGate, instances);
        }

        /// <summary>T009R3 (Blocker 3) — build the bounded relationship provisioning ingress over this
        /// server's shipped <see cref="Relationships"/> handler and <see cref="Characters"/> store. It is
        /// the smallest seam that lets a real session ESTABLISH the Bond/Attunement
        /// RecordFoundationalPlacement requires: it seeds an absent character aggregate and drives the
        /// SAME command handler that boot-rehydrates the relationship journal, with a server-derived
        /// principal. There is deliberately no permissive authorizer or client identity here — the net48
        /// admin seam (config-flag + Valheim-admin gated) supplies the server-derived subject.</summary>
        public RelationshipProvisioningIngress CreateRelationshipProvisioningIngress()
        {
            return new RelationshipProvisioningIngress(Relationships, Characters);
        }

        /// <summary>Compose the live runtime over a stable server-owned durable directory. The
        /// directory is created if absent; the two journals live inside it and are rehydrated at
        /// construction. IAP-007 Tracer 3: the gameplay principal is the BOUND INTERNAL session
        /// (server-minted AccountId/CharacterId) — there is no provider platform→account map and no
        /// provider lookup on the gameplay path (AIP-FR-014/018). Caller supplies the per-Stone family
        /// classification, the server-owned Bond authority policy, and the Stone AP sink (ZDO-backed in
        /// production, in-memory in tests).</summary>
        public static FoundationalProgressionServer Create(
            string durableDirectory,
            IStoneFamilyResolver familyResolver,
            IBondAuthorityPolicy bondAuthority,
            IMirroredStoneApStore stoneApStore,
            FoundationalPieceCatalog? catalog = null,
            RuntimePlacementLog? log = null,
            TimeSpan? pendingRevalidationDeadline = null,
            int pendingRevalidationCapacity = PendingRevalidationQueue.DefaultCapacity,
            WorldId world = default)
        {
            if (string.IsNullOrEmpty(durableDirectory)) throw new ArgumentNullException(nameof(durableDirectory));
            if (familyResolver == null) throw new ArgumentNullException(nameof(familyResolver));
            if (bondAuthority == null) throw new ArgumentNullException(nameof(bondAuthority));
            if (stoneApStore == null) throw new ArgumentNullException(nameof(stoneApStore));

            Directory.CreateDirectory(durableDirectory);
            string apJournal = Path.Combine(durableDirectory, ApJournalFile);
            string relJournal = Path.Combine(durableDirectory, RelationshipJournalFile);
            string sourceJournal = Path.Combine(durableDirectory, ConnectionSourceJournalFile);

            var resolver = new PrincipalResolver();
            var authority = new InMemoryAccountStoneAuthorityStore();
            var characters = new InMemoryCharacterAggregateStore();
            var characterApStore = new InMemoryCharacterApStore();

            // Rehydrate the relationship authority/character projections from the durable relationship
            // journal (server boot). The SAME authority store is read by the placement authorizer below.
            // RD-T004: the Connection source coordinator is constructed FIRST (rehydrating its own
            // journal), then handed to the relationship handler so every committed Bond/Attunement/Release
            // — including the ones replayed during this boot rehydration — drives the matching account-pair
            // Connection source transition in the same logical transaction.
            var connectionSources = new StoneConnectionSourceRegistry(sourceJournal);

            // ADO #138 — the server-owned proximity facts must exist BEFORE the relationship handler,
            // because the handler now REQUIRES the authority composed over them (Bond/Attunement
            // formation is server-checked-at-the-Stone). The Area membership is the same instance the
            // placement pipeline reads; the positions index is published by the engine-bound layer from
            // each peer's own character ZDO. Both start empty, so before any Stone is registered / any
            // position observed the gate fails closed — the same posture placement already has.
            var stoneAreas = new StoneAreaMembership();
            var characterPositions = new ServerObservedCharacterPositions();
            var proximity = new StoneAreaProximityAuthority(stoneAreas, characterPositions);

            var relationships = new RelationshipCommandHandler(
                relJournal, resolver, characters, authority, familyResolver, bondAuthority,
                proximity, connectionSources, world, new ProductScope(ConnectionProduct));

            // Rehydrate the AP receipt projections from the durable AP journal (server boot). The Stone
            // AP sink is injected so production re-stamps the world Stone ZDO during this replay.
            var receipts = new OperationReceiptStore(apJournal, stoneApStore, characterApStore);

            var pipeline = new ProgressionCommandPipeline(
                resolver, receipts, new RelationshipPlacementAuthorizer(authority));

            // Production/ongoing adapter: explicit current-build catalog + stateful anti-repetition so
            // the same physical piece instance is credited at most once within this process lifetime.
            var adapter = new FoundationalPlacementAdapter(
                catalog ?? FoundationalPieceCatalog.CurrentBuild, new InMemoryPlacementRepetitionPolicy());

            var runtime = new FoundationalPlacementRuntime(adapter, pipeline, log);

            var pending = new PendingRevalidationQueue(
                pendingRevalidationDeadline ?? TimeSpan.FromSeconds(30), pendingRevalidationCapacity);

            var boundSessions = new BoundSessionPrincipalIndex();

            // T029 — the Warrior T.W.I.G. gate and its pending queue are NOT composed here: the gate reads
            // the AUTHORITATIVE Stone aggregate + governance projection owned by the shared Local Effect
            // runtime (LocalProgressionServer, t_02c13405 / PR #368), which is composed AFTER this server.
            // The engine-bound bootstrap (and the tests) call ArmWarriorTwig(stones, governorPresence) right
            // after composing that runtime, so there is exactly ONE progression truth — no provisional
            // second Stone state.
            var warriorTwigPendingDeadline = pendingRevalidationDeadline ?? TimeSpan.FromSeconds(30);

            return new FoundationalProgressionServer(
                runtime, relationships, authority, characters, stoneApStore, characterApStore,
                receipts, stoneAreas, characterPositions, pending, boundSessions, connectionSources,
                warriorTwigPendingDeadline, durableDirectory);
        }
    }
}
