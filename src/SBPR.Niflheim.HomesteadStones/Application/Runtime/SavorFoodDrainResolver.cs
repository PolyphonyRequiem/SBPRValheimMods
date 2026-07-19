using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Adapters.Cooking;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;
using SBPR.Niflheim.HomesteadStones.Domain.StoneProgression;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // T016 remediation — the engine-free, TESTABLE live delivery seam for Savor the Hearth. The net48
    // Player.UpdateFood prefix (Features/Cooking/SavorFoodTimerObserver.cs) owns ONLY the engine I/O it
    // cannot avoid — reading the local player's position, its bound-session AccountId, and calling this
    // resolver. Every gameplay DECISION lives here so it is unit-tested exactly like the pure provider:
    //
    //   * which Stone Area (if any) the occupant currently stands inside (server-owned membership);
    //   * whether an ACTIVE Savor Local context has been established at that Stone (the playtest seam);
    //   * the derived LocalEffectActivationView for the occupant AT that Stone (T014 derivation over the
    //     established Stone aggregate + server-observed occupancy/governance facts);
    //   * the vanilla food-timer drain factor for the current elapsed slice, via the already-shipped
    //     SavorTheHearthProvider.ConsumeElapsed (0.5 active / 1.0 otherwise).
    //
    // It reintroduces NO state and NO second ledger: every answer is a pure function of the current
    // established context + the current occupant facts. Stepping outside the Area (TryResolve fails) or
    // clearing the context flips the factor to 1 on the very next tick with zero writes, and only the
    // elapsed slice it is handed is scaled (no retroactive m_time rewrite). This is the same "derive on
    // demand, never persist an active-effects ledger" contract the provider and DerivedActivationView
    // already guarantee (AT-NO-ACTIVE-LEDGER / AT-SAVOR-AREA-EXIT).
    //
    // Establishment note (honest scope): the live server does NOT yet compose the full Stone-progression
    // command stack (Facet commit / BP development / policy) end-to-end — only the Foundational AP slice
    // is composed. Rather than redesign that whole substrate for one Cooking node, the ACTIVE Savor
    // context is established through a bounded, playtest-gated admin seam (Features/Cooking/
    // SavorProvisioningAdmin.cs), exactly mirroring the T009R3/R4 RelationshipProvisioningAdmin pattern.
    // Wiring the context out of a real BP-development runtime is the same "later tracer composes the full
    // command runtime" deferral the tasks doc already carries for T012+; this seam proves the DELIVERY
    // path (factor 0.5 in-world, 1.0 on exit) that T016's joined-client acceptance requires.
    //
    // net48 audit: value objects + the engine-free provider/derivation/membership types only. No net5+
    // surface, no UnityEngine/Valheim/BepInEx reference, so it link-compiles into the net8 test project.

    /// <summary>One established ACTIVE Savor Local context for a single Stone: the Stone aggregate that
    /// carries the developed Savor Local node (committed Cooking Tree, developed Savor, and the current
    /// Settlement Local policy), plus the server-observed governance/owner facts the T014 derivation
    /// needs. Pure data — carries no mutable authority; clearing it flips the factor to 1.</summary>
    public sealed class SavorLocalContext
    {
        public SavorLocalContext(StoneProgressionAggregate stone, bool authorizedGovernorPresent)
        {
            Stone = stone ?? throw new ArgumentNullException(nameof(stone));
            AuthorizedGovernorPresent = authorizedGovernorPresent;
        }

        /// <summary>The Stone aggregate carrying the developed Savor Local node + the Settlement Local
        /// policy that governs beneficiary eligibility.</summary>
        public StoneProgressionAggregate Stone { get; }

        /// <summary>Whether an authorized Governor currently holds governance of this Stone. When false
        /// every Local Effect is dormant regardless of policy (spec US5 sc2), so the factor is 1.</summary>
        public bool AuthorizedGovernorPresent { get; }
    }

    /// <summary>Server-owned, process-local index of established ACTIVE Savor contexts keyed by StoneId.
    /// The playtest establishment seam sets/clears an entry; the food-timer resolver reads it. Non-durable
    /// by construction (mirrors the bound-session index): a restart starts empty and the seam republishes.
    /// It is NOT a second active-effects ledger — it holds only the developed Stone context + governance
    /// fact from which the ACTIVE/DORMANT status is DERIVED per tick, never a stored "active" flag.</summary>
    public sealed class SavorLocalContextIndex
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, SavorLocalContext> _byStone =
            new Dictionary<string, SavorLocalContext>(StringComparer.Ordinal);

        /// <summary>Establish (or refresh) the active Savor context for a Stone. A null context or an
        /// unkeyed Stone is ignored.</summary>
        public void Set(StoneId stoneId, SavorLocalContext context)
        {
            if (string.IsNullOrEmpty(stoneId.Value) || context == null) return;
            lock (_gate) { _byStone[stoneId.Value] = context; }
        }

        /// <summary>Clear a Stone's active Savor context (idempotent). After this the resolver returns the
        /// full factor for that Stone on the next tick.</summary>
        public void Clear(StoneId stoneId)
        {
            if (string.IsNullOrEmpty(stoneId.Value)) return;
            lock (_gate) { _byStone.Remove(stoneId.Value); }
        }

        public bool TryGet(StoneId stoneId, out SavorLocalContext context)
        {
            context = null!;
            if (string.IsNullOrEmpty(stoneId.Value)) return false;
            lock (_gate) { return _byStone.TryGetValue(stoneId.Value, out context!); }
        }

        /// <summary>Live established-context count (test/operator visibility). Zero on restart.</summary>
        public int Count { get { lock (_gate) { return _byStone.Count; } } }
    }

    /// <summary>The occupant facts the resolver needs, all server-observed off the local player — never a
    /// client claim. The AccountId is the occupant's bound-session gameplay account; the (x,z) is the
    /// server-owned world position used to resolve Area membership.</summary>
    public readonly struct SavorOccupant
    {
        public SavorOccupant(AccountId account, bool isOwner, bool hasActiveRelationship, double x, double z)
        {
            Account = account;
            IsOwner = isOwner;
            HasActiveRelationship = hasActiveRelationship;
            X = x;
            Z = z;
        }

        public AccountId Account { get; }
        public bool IsOwner { get; }
        public bool HasActiveRelationship { get; }
        public double X { get; }
        public double Z { get; }
    }

    /// <summary>Pure resolver: given the current Stone Area membership, the established Savor contexts, and
    /// one occupant's server-observed facts, answers the vanilla food-timer drain factor and scales an
    /// elapsed slice. Stateless — every answer is a pure function of the inputs handed in.</summary>
    public sealed class SavorFoodDrainResolver
    {
        private readonly SavorTheHearthProvider _provider;
        private readonly HomesteadProgressionCatalog _catalog;

        public SavorFoodDrainResolver()
            : this(new SavorTheHearthProvider(), new HomesteadProgressionCatalog()) { }

        public SavorFoodDrainResolver(SavorTheHearthProvider provider, HomesteadProgressionCatalog catalog)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>The drain factor for the occupant RIGHT NOW: 0.5 iff the occupant stands inside a Stone
        /// Area that has an established active Savor context AND the T014 derivation makes Savor active for
        /// them (developed + governance + inside + policy-eligible); otherwise 1.0. Pure: re-call after any
        /// change (Area exit, context clear, policy loss) and the factor flips with zero writes.</summary>
        public double DrainFactor(StoneAreaMembership membership, SavorLocalContextIndex contexts, SavorOccupant occupant)
        {
            if (membership == null) throw new ArgumentNullException(nameof(membership));
            if (contexts == null) throw new ArgumentNullException(nameof(contexts));

            // Outside every Area -> full factor (occupancy is a hard conjunct of the active status).
            if (!membership.TryResolve(occupant.X, occupant.Z, out var stoneId))
                return SavorTheHearthProvider.InactiveDrainFactor;

            // No established active Savor context at this Stone -> full factor.
            if (!contexts.TryGet(stoneId, out var context))
                return SavorTheHearthProvider.InactiveDrainFactor;

            // Derive the T014 Local Effect view for THIS occupant at THIS Stone. Inside-area is true (we
            // resolved into it); the remaining occupancy/governance/owner facts are server-observed.
            var view = LocalEffectActivationView.Derive(
                context.Stone,
                _catalog,
                occupant.Account,
                occupant.IsOwner,
                occupant.HasActiveRelationship,
                insideStoneArea: true,
                authorizedGovernorPresent: context.AuthorizedGovernorPresent);

            return _provider.DrainFactor(view);
        }

        /// <summary>Scale one elapsed real-time slice by the current derived factor. This is the exact
        /// quantity the food-timer seam should treat as elapsed for the occupant's active food timers:
        /// <c>elapsedSeconds * DrainFactor(...)</c>. Non-positive elapsed scales to nothing. No mutation,
        /// no retroactive duration — only the slice handed in is scaled.</summary>
        public double ConsumeElapsed(StoneAreaMembership membership, SavorLocalContextIndex contexts,
            SavorOccupant occupant, double elapsedSeconds)
        {
            if (elapsedSeconds <= 0.0 || double.IsNaN(elapsedSeconds)) return 0.0;
            return elapsedSeconds * DrainFactor(membership, contexts, occupant);
        }
    }

    /// <summary>Builds the developed-Savor Stone aggregate the playtest establishment seam publishes as the
    /// active Savor context. Engine-free and deterministic so both the seam and the tests construct the
    /// exact same shape: Cooking committed, Savor developed, at Active Stone Level 2, under the given (or
    /// default Everyone) Settlement Local policy.</summary>
    public static class SavorContextFactory
    {
        /// <summary>Compose a Stone aggregate that has the Savor Local node developed under a committed
        /// Cooking Tree at Active Stone Level 2. Mirrors the T014/T016 test fixture so the live path and
        /// the unit tests derive identical activation.</summary>
        public static StoneProgressionAggregate DevelopedSavorStone(StoneId stoneId, SettlementLocalPolicy? policy = null)
        {
            var savor = new VersionedId("SavorTheHearth", 1);
            var committed = new List<CommittedTreeRecord>
            {
                new CommittedTreeRecord(HomesteadProgressionCatalog.ProfessionFacetId,
                    HomesteadProgressionCatalog.CookingTree, "savor-live-commit", "server", 1, 0),
            };
            var development = new List<NodeDevelopmentRecord>
            {
                new NodeDevelopmentRecord(savor, 1, 1, developed: true, offered: false, "savor-live-dev"),
            };
            return new StoneProgressionAggregate(
                stoneId, revision: 1,
                historicalStoneLevel: 2, activeStoneLevel: 2,
                foundationalTree: new VersionedId("FoundationalTree", 1),
                foundationalCatalog: new VersionedId("FoundationalCatalog", 1),
                contentRegistryVersion: HomesteadProgressionCatalog.CurrentContentRegistryVersion,
                createdProvenance: "savor-live", updatedProvenance: "savor-live",
                mirroredStoneAp: 0, lastAppliedReceiptId: "savor-live",
                committedTrees: committed, nodeDevelopment: development,
                localPolicy: policy);
        }
    }
}
