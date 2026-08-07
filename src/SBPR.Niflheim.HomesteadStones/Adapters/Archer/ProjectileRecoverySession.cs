using System;
using System.Collections.Generic;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Archer
{
    // T027 (Tracer 7, Archer node 3 of 3) — the one-result-per-instance / no-duplication guard for
    // Fletcher's Habit projectile recovery (research.md line 139 "one-result guarantee ... multishot cases";
    // tasks.md T027 acceptance AT-FLETCHER-NO-DUP).
    //
    // The pure ProjectileRecoveryProvider makes ONE authoritative decision from the inputs, but a fired
    // arrow's terminal impact can be observed re-entrantly by the runtime (e.g. an OnHit that fires, an RPC
    // echo, a physics re-evaluation) and a multishot volley fires several instances at once. This session
    // keys resolution by the fired INSTANCE id so:
    //   * the SAME instance resolves at most once — a second resolution returns AlreadyResolved and mints
    //     nothing (the arrow was already recovered-or-not exactly once); and
    //   * a MULTISHOT volley resolves each distinct instance independently, so N arrows can recover up to N
    //     exact instances with zero cross-instance duplication.
    //
    // It holds only a set of already-resolved instance ids and a recovered tally — disposable per fire
    // context, NOT a persisted authority. The authoritative recovery decision itself is still the pure
    // provider's; this session only enforces the once-per-instance boundary around it.
    //
    // net48 audit: engine-free (System collections only). Link-compiles into the net8 test project.

    /// <summary>Enforces the once-per-fired-instance boundary around
    /// <see cref="ProjectileRecoveryProvider.Resolve"/>. Not thread-safe by itself — the runtime seam calls
    /// it on the single owner thread that observes terminal impacts.</summary>
    public sealed class ProjectileRecoverySession
    {
        private readonly HashSet<long> _resolved = new HashSet<long>();
        private int _totalRecovered;

        /// <summary>How many exact instances this session has recovered across all resolved instances.</summary>
        public int TotalRecovered => _totalRecovered;

        /// <summary>How many distinct fired instances this session has resolved.</summary>
        public int ResolvedCount => _resolved.Count;

        /// <summary>Whether the given fired instance has already been resolved by this session.</summary>
        public bool HasResolved(long instanceId) => _resolved.Contains(instanceId);

        /// <summary>Resolve one fired instance exactly once. On the first observation of
        /// <paramref name="instanceId"/> the pure provider makes the authoritative decision and the recovered
        /// tally advances; every subsequent observation of the same instance returns
        /// <see cref="RecoveryOutcome.AlreadyResolved"/> and recovers nothing (the no-duplication guarantee).
        /// Distinct instance ids (a multishot volley) each resolve independently.</summary>
        public RecoveryDecision ResolveOnce(
            ProjectileRecoveryProvider provider,
            long instanceId,
            bool owned,
            ConsumedArrowProvenance provenance,
            RecoverySurface surface,
            bool targetReturnWon,
            double roll)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));

            if (!_resolved.Add(instanceId))
                return new RecoveryDecision(RecoveryOutcome.AlreadyResolved, 0, ConsumedArrowProvenance.None);

            var decision = provider.Resolve(owned, provenance, surface, targetReturnWon, roll);
            _totalRecovered += decision.RecoveredCount;
            return decision;
        }
    }
}
