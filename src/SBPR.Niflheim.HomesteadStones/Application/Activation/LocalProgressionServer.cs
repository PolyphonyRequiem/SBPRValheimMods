using System;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Application.Commands;
using SBPR.Niflheim.HomesteadStones.Application.Runtime;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;
using SBPR.Niflheim.HomesteadStones.Persistence.Characters;
using SBPR.Niflheim.HomesteadStones.Persistence.Stone;

namespace SBPR.Niflheim.HomesteadStones.Application.Activation
{
    // T016 shared runtime substrate — the engine-free COMPOSITION ROOT that wires the already-accepted
    // Stone progression aggregate store + the T012-T014 Facet / Development / LocalPolicy command handlers
    // (over the SAME character/authority stores as the Foundational runtime) + the LocalActivationService
    // into ONE live server-side surface. This is what the T021 investigation found missing: there was NO
    // production composition of these handlers, so a Local node could never reach Developed/committed at
    // runtime and no per-occupant activation could be derived or delivered.
    //
    // It deliberately does NOT invent a parallel provisional node-development or policy ledger: every
    // mutation goes through the shipped, receipt-backed handlers onto their durable journals, and every
    // activation read is a fresh derivation via LocalActivationService. The stores are the SAME shipped
    // engine-free projection sinks; the durable directory hosts the four progression journals alongside
    // the Foundational runtime's journals.
    //
    // Authority seams (IGovernorAuthorityPolicy / IGovernorDevelopmentAuthority / IHomesteadOwnerAuthority)
    // are INJECTED: production supplies server-owned content-backed policies; the net8 tests supply
    // deterministic stubs. There is no permissive fallback — a null seam throws at construction.
    //
    // net48 audit: System.IO (Directory/Path) + shipped engine-free types only. No UnityEngine/Valheim/
    // BepInEx, so this whole root — construction, rehydration, and the activation seam — link-compiles into
    // the net8 test project and is fully unit-tested without a live server.
    public sealed class LocalProgressionServer
    {
        public const string FacetJournalFile = "facet-commit.journal";
        public const string DevelopmentJournalFile = "node-development.journal";
        public const string ActivityJournalFile = "aligned-activity.journal";
        public const string LocalPolicyJournalFile = "local-policy.journal";
        public const string PurchaseJournalFile = "node-purchase.journal";
        public const string RevocationJournalFile = "tree-revocation.journal";

        private LocalProgressionServer(
            IStoneAggregateStore stones,
            ICharacterAggregateStore characters,
            IAccountStoneAuthorityStore authority,
            ICharacterApStore? characterApStore,
            RelationshipCommandHandler relationships,
            FacetCommandHandler facets,
            ActivityCommandHandler activities,
            DevelopmentCommandHandler development,
            LocalPolicyCommandHandler localPolicy,
            LocalActivationService activation,
            PersonalActivationService personalActivation,
            GovernorPresenceResolver governorPresence,
            HomesteadProgressionCatalog catalog,
            IGovernorAuthorityPolicy governorAuthority,
            string durableDirectory)
        {
            Stones = stones;
            Characters = characters;
            Authority = authority;
            CharacterApStore = characterApStore;
            Relationships = relationships;
            Facets = facets;
            Activities = activities;
            Development = development;
            LocalPolicy = localPolicy;
            Activation = activation;
            PersonalActivation = personalActivation;
            GovernorPresence = governorPresence;
            Catalog = catalog;
            GovernorAuthority = governorAuthority;
            DurableDirectory = durableDirectory;
        }

        /// <summary>The authoritative Stone progression aggregate store (Aggregate 1). The SAME instance is
        /// read by the Facet/Development/LocalPolicy handlers and by the activation service — there is one
        /// authority, never a shadow copy.</summary>
        public IStoneAggregateStore Stones { get; }

        public ICharacterAggregateStore Characters { get; }
        public IAccountStoneAuthorityStore Authority { get; }

        /// <summary>T022 split-ledger fix — the AUTHORITATIVE Personal-AP earn ledger (the same
        /// receipt-derived ICharacterApStore the Foundational runtime credits on every valid placement).
        /// The purchase handler composed by <see cref="CreateLocalProvisioningIngress"/> reads spendable
        /// Personal AP from here (earned − spent) instead of the character aggregate's stored PersonalAp,
        /// so earned placement AP is authoritatively visible to Masterwork purchase. Shared by reference,
        /// never copied — one earn authority. Null only on the legacy composition path where a caller
        /// pre-seeds PersonalAp directly on the character aggregate (purchase then reads that verbatim).</summary>
        public ICharacterApStore? CharacterApStore { get; }

        public RelationshipCommandHandler Relationships { get; }
        public FacetCommandHandler Facets { get; }
        public ActivityCommandHandler Activities { get; }
        public DevelopmentCommandHandler Development { get; }
        public LocalPolicyCommandHandler LocalPolicy { get; }

        /// <summary>The bounded server→client Local Effect delivery authority. Derives per-occupant read
        /// models from <see cref="Stones"/> + server-observed presence and emits bounded notifications.</summary>
        public LocalActivationService Activation { get; }

        /// <summary>T026 remediation — the bounded server→client PERSONAL Character-Effect delivery authority.
        /// Derives per-(occupant, character) read models from <see cref="Stones"/>/<see cref="Characters"/>/
        /// <see cref="Authority"/> (purchase record AND active relationship, via the shipped
        /// DerivedActivationView) and emits bounded notifications. This is the channel Field Fletching I needs
        /// so a pure joined client can craft; the Local channel above carries Stone-owned Local nodes only.</summary>
        public PersonalActivationService PersonalActivation { get; }

        /// <summary>T016 fix-forward — derives the two cross-account governance facts (Stone-wide
        /// authorized-Governor presence and this-account ownership) from COMMITTED relationship/authority
        /// state. The delivery channel consumes it to compose <see cref="Application.Activation.OccupantPresence"/>
        /// so owner/governor-presence are real derived facts, never a never-written flag.</summary>
        public GovernorPresenceResolver GovernorPresence { get; }

        /// <summary>Compose the authoritative per-occupant presence for the delivery channel from
        /// SERVER-OWNED facts: the caller supplies only the transport-authenticated occupant identity, the
        /// server-observed relationship activity, and the server-resolved in-Area occupancy — every one a
        /// server truth. The owner + Stone-wide authorized-Governor-presence facts are DERIVED here from
        /// committed state via <see cref="GovernorPresence"/>, so they can never be forged and are never a
        /// dead flag. This is the single seam the net48 RPC handler and the tests both drive, so the suite
        /// cannot pass while the real channel is inert.</summary>
        public OccupantPresence ComposePresence(
            StoneId stone,
            Domain.Identity.AccountId occupant,
            Domain.Identity.CharacterId character,
            bool hasActiveRelationship,
            bool insideStoneArea)
        {
            bool isOwner = GovernorPresence.IsOwner(occupant, stone);
            bool authorizedGovernorPresent = GovernorPresence.AuthorizedGovernorPresent(stone);
            return new OccupantPresence(occupant, character, isOwner, hasActiveRelationship,
                insideStoneArea, authorizedGovernorPresent);
        }

        public HomesteadProgressionCatalog Catalog { get; }

        /// <summary>The server-owned Governor authority + Responsibility Range policy this root was
        /// composed with. Retained (not just passed to the Facet handler) because Tree REVOCATION gates
        /// on exactly the same authority as Tree commitment — revoking a commitment is at least as
        /// authoritative an act as making one, so it must not be composed against a second, laxer
        /// policy.</summary>
        public IGovernorAuthorityPolicy GovernorAuthority { get; }

        public string DurableDirectory { get; }

        /// <summary>T021 remediation 2 — build the isolated-QA Local-node development / personal-node
        /// purchase ingress over this server's accepted handlers + shared stores. The PurchaseCommandHandler
        /// is composed here over the SAME character/authority/Stone stores and a durable node-purchase
        /// journal alongside the four progression journals, so a purchase crosses the real receipt-backed
        /// path and rehydrates on restart. The ingress never writes node/purchase state directly — it only
        /// routes server-derived subjects through the shipped handlers. Constructed and driven ONLY by the
        /// net48 admin/isolated-QA seam (config-flag + Valheim-admin gated); production fails closed.</summary>
        public LocalProvisioningIngress CreateLocalProvisioningIngress()
        {
            var purchases = new PurchaseCommandHandler(
                Path.Combine(DurableDirectory, PurchaseJournalFile), new PrincipalResolver(),
                Stones, Characters, Authority, Catalog, CharacterApStore);
            return new LocalProvisioningIngress(this, purchases);
        }

        /// <summary>ADO #137 — compose the authority-gated Tree revocation handler over this server's
        /// shared stores, the SAME server-owned Governor authority policy the Facet handler uses, and a
        /// purchase handler over the SAME durable node-purchase journal.
        ///
        /// <para>The purchase handler is passed in rather than reconstructed independently because it is
        /// the single writer of that journal AND the owner of the (now <c>internal</c>) cancellation
        /// primitive: revocation issues refunds only THROUGH it, never by opening the journal itself.
        /// This is the wrapping the #132 review demanded — the unguarded primitive now has exactly one
        /// production caller, and that caller authenticates, authorizes, and journals first.</para></summary>
        public RevocationCommandHandler CreateRevocationCommandHandler(PurchaseCommandHandler purchases)
        {
            if (purchases == null) throw new ArgumentNullException(nameof(purchases));
            return new RevocationCommandHandler(
                Path.Combine(DurableDirectory, RevocationJournalFile), new PrincipalResolver(),
                Stones, Characters, Authority, GovernorAuthority, purchases, palette: null, catalog: Catalog);
        }

        /// <summary>Convenience overload composing the purchase handler over this server's own durable
        /// node-purchase journal and shared stores — the same composition
        /// <see cref="CreateLocalProvisioningIngress"/> uses, so both surfaces read and write one journal.</summary>
        public RevocationCommandHandler CreateRevocationCommandHandler()
        {
            return CreateRevocationCommandHandler(new PurchaseCommandHandler(
                Path.Combine(DurableDirectory, PurchaseJournalFile), new PrincipalResolver(),
                Stones, Characters, Authority, Catalog, CharacterApStore));
        }

        /// <summary>Compose the live Local progression runtime over a stable server-owned durable directory
        /// and the shared character/authority stores the Foundational runtime already rehydrated. The four
        /// progression journals live in <paramref name="durableDirectory"/> and are rehydrated at
        /// construction (each handler replays its journal onto the shared projections). Production supplies
        /// the ZDO-shared stores + server-owned authority policies; tests supply in-memory stores + stubs.</summary>
        public static LocalProgressionServer Create(
            string durableDirectory,
            IStoneAggregateStore stones,
            ICharacterAggregateStore characters,
            IAccountStoneAuthorityStore authority,
            RelationshipCommandHandler relationships,
            IStoneFamilyResolver familyResolver,
            IGovernorAuthorityPolicy governorAuthority,
            IGovernorDevelopmentAuthority developmentAuthority,
            IHomesteadOwnerAuthority ownerAuthority,
            HomesteadProgressionCatalog? catalog = null,
            TreeDevelopmentConfig? developmentConfig = null,
            ICharacterApStore? characterApStore = null)
        {
            if (string.IsNullOrEmpty(durableDirectory)) throw new ArgumentNullException(nameof(durableDirectory));
            if (stones == null) throw new ArgumentNullException(nameof(stones));
            if (characters == null) throw new ArgumentNullException(nameof(characters));
            if (authority == null) throw new ArgumentNullException(nameof(authority));
            if (relationships == null) throw new ArgumentNullException(nameof(relationships));
            if (familyResolver == null) throw new ArgumentNullException(nameof(familyResolver));
            if (governorAuthority == null) throw new ArgumentNullException(nameof(governorAuthority));
            if (developmentAuthority == null) throw new ArgumentNullException(nameof(developmentAuthority));
            if (ownerAuthority == null) throw new ArgumentNullException(nameof(ownerAuthority));

            // T022 split-ledger fix: the Personal-AP earn ledger. Production passes the Foundational
            // runtime's shared ICharacterApStore (server.CharacterApStore) so purchase reads the same
            // authoritative earned balance placement credits (earned − spent). When null (legacy tests
            // that pre-seed PersonalAp directly on the character aggregate and never exercise the earn
            // path), the purchase handler falls back to the aggregate's stored PersonalAp verbatim, so
            // those callers keep their prior behavior. There is never a fabricated second earn ledger.
            var apStore = characterApStore;

            Directory.CreateDirectory(durableDirectory);
            var effectiveCatalog = catalog ?? new HomesteadProgressionCatalog();
            var resolver = new PrincipalResolver();

            // Each handler rehydrates its own durable journal onto the SHARED stores at construction.
            var facets = new FacetCommandHandler(
                Path.Combine(durableDirectory, FacetJournalFile), resolver, stones, characters, authority,
                governorAuthority, palette: null, catalog: effectiveCatalog);

            var activities = new ActivityCommandHandler(
                Path.Combine(durableDirectory, ActivityJournalFile), resolver, stones, characters, authority,
                developmentAuthority);

            var development = new DevelopmentCommandHandler(
                Path.Combine(durableDirectory, DevelopmentJournalFile), resolver, stones, characters, authority,
                developmentAuthority, effectiveCatalog, developmentConfig);

            var localPolicy = new LocalPolicyCommandHandler(
                Path.Combine(durableDirectory, LocalPolicyJournalFile), resolver, stones, ownerAuthority);

            var activation = new LocalActivationService(stones, effectiveCatalog);
            var personalActivation = new PersonalActivationService(stones, characters, authority);
            var governorPresence = new GovernorPresenceResolver(characters, authority);

            return new LocalProgressionServer(
                stones, characters, authority, apStore, relationships, facets, activities, development, localPolicy,
                activation, personalActivation, governorPresence, effectiveCatalog, governorAuthority, durableDirectory);
        }
    }
}
