using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.StoneProgression
{
    // T014 — the single Settlement-wide Local beneficiary policy (spec FR-016; contracts.md
    // §"SetSettlementLocalPolicy"; data-model.md §"Local state"). Stone-owned developed state that
    // governs which occupants benefit from ALL active Local Effects. There is exactly ONE policy per
    // Stone and NO per-effect override — every completed Local Node is governed by this same policy.
    //
    // Cardinal rules this file encodes (spec FR-015/FR-016/FR-019):
    //   * A Local node is Stone-owned developed state; the policy is never a personal purchase, never
    //     enters a personal Offered Set, and never contributes to Tier Access. It is authoritative
    //     Stone state carried on the Stone aggregate, and NOT a second mutable active-effects ledger:
    //     the ACTIVE/DORMANT projection of Local Effects is derived on demand in DerivedActivationView.
    //   * Everyone (default): every occupant inside the Stone Area benefits.
    //   * Attuned: the owner plus any occupant currently holding an active relationship to the Stone.
    //   * Private: the owner plus the explicit account allowlist (and no one else).
    //   * Beneficiary eligibility is only HALF of a Local placement capability — the other half is the
    //     caller's ordinary build Permission, evaluated independently (spec FR-016 final sentence; this
    //     type never grants build ACLs and is deliberately unaware of Permission).
    //
    // The policy carries its own revision so a SetSettlementLocalPolicy command can enforce optimistic
    // concurrency on the policy independently of (and in addition to) the Stone revision, and so a
    // stale-revision change rejects with zero mutation.
    //
    // net48 audit: only System / System.Collections.Generic + the engine-free snapshot codec. No net5+
    // surface, no UnityEngine/Valheim/BepInEx reference, so this link-compiles into the net8 tests.

    /// <summary>The single Settlement-wide beneficiary mode for all active Local Effects (spec FR-016).
    /// Authored policy state, never a per-effect flag.</summary>
    public enum LocalBeneficiaryMode
    {
        /// <summary>Default: everyone inside the Stone Area benefits.</summary>
        Everyone = 0,

        /// <summary>The owner plus any occupant currently holding an active relationship to the Stone.</summary>
        Attuned = 1,

        /// <summary>The owner plus the explicit Settlement account allowlist, and no one else.</summary>
        Private = 2
    }

    /// <summary>The single immutable Settlement-wide Local beneficiary policy for one Stone. Pure value
    /// object: every transition returns a NEW policy and never mutates the input (spec FR-019 — no
    /// mutable ledger). The allowlist is normalized (deduplicated, ordinal-sorted) so equal policies
    /// serialize identically regardless of insertion order.</summary>
    public sealed class SettlementLocalPolicy
    {
        private readonly List<string> _allowlist;

        public SettlementLocalPolicy(LocalBeneficiaryMode mode, long revision,
            IReadOnlyList<string>? allowlistAccounts = null)
        {
            Mode = mode;
            Revision = revision;
            _allowlist = Normalize(allowlistAccounts);
        }

        public LocalBeneficiaryMode Mode { get; }

        /// <summary>Monotonic policy revision, incremented on every accepted change. Used for optimistic
        /// concurrency on the policy itself independent of the Stone revision.</summary>
        public long Revision { get; }

        /// <summary>The explicit Private-mode account allowlist (normalized). Empty for Everyone/Attuned;
        /// for Private it is the exact set of non-owner accounts that additionally benefit.</summary>
        public IReadOnlyList<string> AllowlistAccounts => _allowlist;

        /// <summary>The default policy: Everyone at revision 0 with no allowlist. This is what a Stone
        /// carries before any SetSettlementLocalPolicy has been applied (spec FR-016: "Everyone
        /// (default)").</summary>
        public static SettlementLocalPolicy Default =>
            new SettlementLocalPolicy(LocalBeneficiaryMode.Everyone, 0, null);

        /// <summary>True when the given occupant account is a beneficiary of active Local Effects under
        /// THIS policy. Occupancy inside the Stone Area and dormancy are evaluated by the caller — this
        /// answers ONLY the policy-membership half. <paramref name="isOwner"/> is the server-validated
        /// Homestead-owner fact; <paramref name="hasActiveRelationship"/> is whether the occupant
        /// currently holds an active Bond/Attunement to the Stone.</summary>
        public bool IsBeneficiary(AccountId occupant, bool isOwner, bool hasActiveRelationship)
        {
            switch (Mode)
            {
                case LocalBeneficiaryMode.Everyone:
                    return true;
                case LocalBeneficiaryMode.Attuned:
                    return isOwner || hasActiveRelationship;
                case LocalBeneficiaryMode.Private:
                    return isOwner || _allowlist.Contains(occupant.Value);
                default:
                    return false;
            }
        }

        /// <summary>Produce the next policy with a new mode/allowlist and the revision incremented by one.
        /// Pure: the input is unchanged. The allowlist is ignored for non-Private modes (normalized to
        /// empty) so a mode change cannot smuggle a stale allowlist forward.</summary>
        public SettlementLocalPolicy With(LocalBeneficiaryMode mode, IReadOnlyList<string>? allowlistAccounts)
        {
            var list = mode == LocalBeneficiaryMode.Private ? allowlistAccounts : null;
            return new SettlementLocalPolicy(mode, Revision + 1, list);
        }

        private static List<string> Normalize(IReadOnlyList<string>? accounts)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            if (accounts != null)
                foreach (var a in accounts)
                    if (!string.IsNullOrEmpty(a)) set.Add(a);
            return new List<string>(set);
        }

        public string Serialize() => new SnapshotWriter()
            .PutInt("mode", (int)Mode)
            .PutLong("rev", Revision)
            .PutList("allow", _allowlist, x => x)
            .Build();

        public static SettlementLocalPolicy Deserialize(string s)
        {
            var r = new SnapshotReader(s);
            var allow = r.GetList("allow", x => x);
            return new SettlementLocalPolicy((LocalBeneficiaryMode)r.GetInt("mode"), r.GetLong("rev"), allow);
        }

        /// <summary>Structural equality over mode, revision, and the normalized allowlist. Used by the
        /// round-trip proof (AT-STATE-ROUNDTRIP) to assert the policy survives reload unchanged.</summary>
        public bool StructurallyEquals(SettlementLocalPolicy o)
        {
            if (o == null) return false;
            if (Mode != o.Mode || Revision != o.Revision || _allowlist.Count != o._allowlist.Count)
                return false;
            for (int i = 0; i < _allowlist.Count; i++)
                if (!string.Equals(_allowlist[i], o._allowlist[i], StringComparison.Ordinal))
                    return false;
            return true;
        }
    }
}
