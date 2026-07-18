using System;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Activities;
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
            PendingRevalidationQueue pendingPlacements,
            BoundSessionPrincipalIndex boundSessions,
            StoneConnectionSourceRegistry connectionSources,
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
            PendingPlacements = pendingPlacements;
            BoundSessions = boundSessions;
            ConnectionSources = connectionSources;
            DurableDirectory = durableDirectory;
        }

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
            var relationships = new RelationshipCommandHandler(
                relJournal, resolver, characters, authority, familyResolver, bondAuthority,
                connectionSources, world, new ProductScope(ConnectionProduct));

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

            var stoneAreas = new StoneAreaMembership();

            var pending = new PendingRevalidationQueue(
                pendingRevalidationDeadline ?? TimeSpan.FromSeconds(30), pendingRevalidationCapacity);

            var boundSessions = new BoundSessionPrincipalIndex();

            return new FoundationalProgressionServer(
                runtime, relationships, authority, characters, stoneApStore, characterApStore,
                receipts, stoneAreas, pending, boundSessions, connectionSources, durableDirectory);
        }
    }
}
