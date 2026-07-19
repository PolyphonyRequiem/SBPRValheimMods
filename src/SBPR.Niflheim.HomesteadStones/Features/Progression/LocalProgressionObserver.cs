using SBPR.Niflheim.HomesteadStones.Application.Activation;

namespace SBPR.Niflheim.HomesteadStones.Features.Progression
{
    /// <summary>
    /// T016 shared runtime substrate — the process-local holder for the LIVE Local progression runtime.
    /// The T021 investigation found the activation substrate had ZERO live constructions; this holder is
    /// the seam the engine-bound bootstrap publishes the composed <see cref="LocalProgressionServer"/> into
    /// and the RPC delivery glue + the gameplay-family consumers (Refined/Savor/Practice/T.W.I.G.) read.
    ///
    /// Server-authoritative: only the authoritative host composes and publishes a server here. A pure
    /// client leaves <see cref="Server"/> null and instead consumes the replicated read model through
    /// <see cref="ClientCache"/>. Both are cleared on ZNet teardown.
    ///
    /// References only engine-free application types → this file itself is engine-free, but it is placed in
    /// Features/Progression alongside the engine-bound bootstrap that owns its lifecycle. It is intentionally
    /// NOT link-compiled into the net8 tests (the substrate it holds is tested directly).
    /// </summary>
    internal static class LocalProgressionObserver
    {
        /// <summary>The composed server-side runtime (authoritative host only). Null on a pure client and
        /// before composition / after teardown.</summary>
        internal static LocalProgressionServer? Server;

        /// <summary>The activation service of the composed server, or null. Convenience accessor for the
        /// RPC delivery glue.</summary>
        internal static LocalActivationService? Activation => Server?.Activation;

        /// <summary>The personal Character-Effect activation service of the composed server, or null.
        /// Convenience accessor for the personal-effect RPC delivery glue (T026 remediation).</summary>
        internal static PersonalActivationService? PersonalActivation => Server?.PersonalActivation;

        /// <summary>The client-side bounded read-model cache. Every joined client (including a listen-host
        /// acting as its own client) holds one; the RPC receive handler applies snapshots into it and the
        /// gameplay-family consumers read it to decide whether an effect is active for the local player.</summary>
        internal static readonly LocalActivationClientCache ClientCache = new LocalActivationClientCache();

        /// <summary>The client-side bounded PERSONAL Character-Effect read-model cache (T026 remediation).
        /// Every joined client (including a listen-host acting as its own client) holds one; the personal RPC
        /// receive handler applies snapshots into it and the Field Fletching recipe gate reads it to decide
        /// whether the personal effect is active for the local player.</summary>
        internal static readonly PersonalActivationClientCache PersonalClientCache =
            new PersonalActivationClientCache();

        internal static void Clear()
        {
            Server = null;
            PersonalClientCache.Clear();
        }
    }
}
