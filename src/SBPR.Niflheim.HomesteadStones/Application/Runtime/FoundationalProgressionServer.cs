using System;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Adapters.Activities;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Receipts;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
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

        private FoundationalProgressionServer(
            FoundationalPlacementRuntime runtime,
            RelationshipCommandHandler relationships,
            IAccountStoneAuthorityStore authority,
            ICharacterAggregateStore characters,
            IMirroredStoneApStore stoneApStore,
            ICharacterApStore characterApStore,
            OperationReceiptStore receipts,
            StoneAreaMembership stoneAreas,
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

        public string DurableDirectory { get; }

        /// <summary>Compose the live runtime over a stable server-owned durable directory. The
        /// directory is created if absent; the two journals live inside it and are rehydrated at
        /// construction. Caller supplies the platform→account map (candidate E; null falls back to
        /// candidate A — platform id as account), the per-Stone family classification, the server-owned
        /// Bond authority policy, and the Stone AP sink (ZDO-backed in production, in-memory in tests).</summary>
        public static FoundationalProgressionServer Create(
            string durableDirectory,
            Func<string, string?>? accountIdForPlatform,
            IStoneFamilyResolver familyResolver,
            IBondAuthorityPolicy bondAuthority,
            IMirroredStoneApStore stoneApStore,
            FoundationalPieceCatalog? catalog = null,
            RuntimePlacementLog? log = null)
        {
            if (string.IsNullOrEmpty(durableDirectory)) throw new ArgumentNullException(nameof(durableDirectory));
            if (familyResolver == null) throw new ArgumentNullException(nameof(familyResolver));
            if (bondAuthority == null) throw new ArgumentNullException(nameof(bondAuthority));
            if (stoneApStore == null) throw new ArgumentNullException(nameof(stoneApStore));

            Directory.CreateDirectory(durableDirectory);
            string apJournal = Path.Combine(durableDirectory, ApJournalFile);
            string relJournal = Path.Combine(durableDirectory, RelationshipJournalFile);

            var resolver = new PrincipalResolver(accountIdForPlatform);
            var authority = new InMemoryAccountStoneAuthorityStore();
            var characters = new InMemoryCharacterAggregateStore();
            var characterApStore = new InMemoryCharacterApStore();

            // Rehydrate the relationship authority/character projections from the durable relationship
            // journal (server boot). The SAME authority store is read by the placement authorizer below.
            var relationships = new RelationshipCommandHandler(
                relJournal, resolver, characters, authority, familyResolver, bondAuthority);

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

            return new FoundationalProgressionServer(
                runtime, relationships, authority, characters, stoneApStore, characterApStore,
                receipts, stoneAreas, durableDirectory);
        }
    }
}
