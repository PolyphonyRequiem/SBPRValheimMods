// The immutable capability-manifest parser (ADR-0009 §3.1, §5.1). Takes the raw
// permitted-verb list from an ArmManifest and resolves it into a validated, immutable
// capability set: every entry must be a KNOWN catalog verb, appropriate to the armed
// role, with no duplicates and at least one verb. A superset of the catalog is
// impossible; an unknown or role-inappropriate verb fails the whole parse (fail-closed).
using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core
{
    /// <summary>Result of parsing a capability manifest — either an immutable set or a reject reason.</summary>
    public sealed class CapabilityManifest
    {
        private readonly HashSet<string> _permitted;

        private CapabilityManifest(HashSet<string> permitted)
        {
            _permitted = permitted;
        }

        /// <summary>The permitted verb names (ordinal).</summary>
        public IReadOnlyCollection<string> PermittedVerbs => _permitted;

        /// <summary>True when <paramref name="verb"/> is permitted this run.</summary>
        public bool Permits(string? verb) => verb != null && _permitted.Contains(verb);

        /// <summary>
        /// Parse + validate the permitted-verb list against the static catalog for the
        /// given armed role. Fail-closed:
        ///   • empty list          => EmptyCapability
        ///   • any unknown verb    => UnknownVerb
        ///   • any duplicate       => MalformedManifest
        ///   • verb wrong for role => RoleMismatch
        /// On success returns None and sets <paramref name="manifest"/>.
        /// </summary>
        public static RejectReason TryParse(
            IReadOnlyList<string>? permittedVerbs,
            HarnessRole role,
            out CapabilityManifest? manifest)
        {
            manifest = null;
            if (permittedVerbs == null || permittedVerbs.Count == 0)
                return RejectReason.EmptyCapability;

            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in permittedVerbs)
            {
                if (string.IsNullOrEmpty(name))
                    return RejectReason.MalformedManifest;
                var verb = VerbCatalog.Get(name);
                if (verb == null)
                    return RejectReason.UnknownVerb;
                if (!verb.AllowsRole(role))
                    return RejectReason.RoleMismatch;
                if (!set.Add(name))
                    return RejectReason.MalformedManifest; // duplicate
            }

            manifest = new CapabilityManifest(set);
            return RejectReason.None;
        }
    }
}
