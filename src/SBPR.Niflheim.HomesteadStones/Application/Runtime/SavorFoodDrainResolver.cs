using System;
using SBPR.Niflheim.HomesteadStones.Adapters.Cooking;
using SBPR.Niflheim.HomesteadStones.Application.Activation;

namespace SBPR.Niflheim.HomesteadStones.Application.Runtime
{
    // T016 remediation (rebased onto the merged shared Local Effect runtime, PR #368) — the engine-free,
    // TESTABLE translator the net48 Player.UpdateFood prefix delegates its ONE gameplay decision to:
    // "given the authoritative per-occupant Local Effect read model the shared substrate already derived,
    // at what factor does an active food timer consume this elapsed slice right now?"
    //
    // This file used to carry a PARALLEL family-local activation ledger (a SavorLocalContextIndex + a
    // SavorContextFactory that fabricated a developed-Savor Stone). That provisional state is DELETED:
    // the reviewed shared substrate (Application/Activation/LocalActivationService + LocalProgressionServer
    // + LocalActivationClientCache) is now the single authority that DERIVES whether Savor is active for an
    // occupant from the real Stone aggregate + committed relationship/governance + Settlement policy +
    // server-observed occupancy. The food seam consumes that authoritative read model — it never derives,
    // stores, or fabricates activation itself.
    //
    // What remains here is a pure, stateless projection: read the already-derived Savor active-state off a
    // LocalActivationSnapshot and translate it to the vanilla food-timer drain factor via the shipped
    // SavorTheHearthProvider (0.5 active / 1.0 otherwise). No second ledger, no state: a null/denied
    // snapshot (Area exit, no authority, policy loss, governance dormancy → all reflected in the snapshot
    // the substrate hands us) yields factor 1, and only the elapsed slice handed in is scaled (no
    // retroactive m_time rewrite) — the AT-SAVOR-AREA-EXIT / AT-NO-ACTIVE-LEDGER contract, now anchored to
    // the authoritative substrate instead of a family-local copy.
    //
    // net48 audit: value objects + the engine-free provider/snapshot only. No UnityEngine/Valheim/BepInEx,
    // so it link-compiles into the net8 test project and every branch is unit-tested without a live server.
    public sealed class SavorFoodDrainResolver
    {
        private readonly SavorTheHearthProvider _provider;

        public SavorFoodDrainResolver() : this(new SavorTheHearthProvider()) { }

        public SavorFoodDrainResolver(SavorTheHearthProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>The vanilla food-timer drain factor for the occupant RIGHT NOW, read from the
        /// authoritative per-occupant <paramref name="snapshot"/> the shared substrate derived: 0.5 iff the
        /// snapshot has the Savor Local Effect active (developed + governance + inside Area + policy-eligible,
        /// all decided by the substrate), otherwise 1.0. A null or authority-absent snapshot is full factor —
        /// fail closed. Pure: re-fetch the snapshot after any change and call again; the factor flips with
        /// zero writes here.</summary>
        public double DrainFactor(LocalActivationSnapshot? snapshot)
        {
            if (snapshot == null || !snapshot.AuthorityPresent)
                return SavorTheHearthProvider.InactiveDrainFactor;
            return snapshot.IsActive(CookingNodes.SavorTheHearth)
                ? SavorTheHearthProvider.ActiveDrainFactor
                : SavorTheHearthProvider.InactiveDrainFactor;
        }

        /// <summary>Scale one elapsed real-time slice by the current derived factor — the exact quantity the
        /// food-timer seam treats as elapsed for the occupant's active food timers. Non-positive elapsed
        /// scales to nothing. No mutation, no retroactive duration: only the slice handed in is scaled.</summary>
        public double ConsumeElapsed(LocalActivationSnapshot? snapshot, double elapsedSeconds)
        {
            if (elapsedSeconds <= 0.0 || double.IsNaN(elapsedSeconds)) return 0.0;
            return elapsedSeconds * DrainFactor(snapshot);
        }
    }
}
