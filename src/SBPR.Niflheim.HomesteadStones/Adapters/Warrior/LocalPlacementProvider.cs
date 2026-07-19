using System;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.Content;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Warrior
{
    // T029 — Tracer 8 (Warrior), node 1 of 3. The T.W.I.G. Training Local placement capability
    // (spec §"Warrior": "T.W.I.G. Training locally unlocks the unchanged vanilla T.W.I.G."; contracts.md
    // §Warrior: "LocalPlacementProvider: T.W.I.G. Training grants exact T.W.I.G. placement inside the
    // Homestead and remains Permission-gated").
    //
    // T.W.I.G. Training is a Stone-cultivated Local Node (data-model.md Warrior L1, Local Effect). Its
    // ONLY effect is to expose the EXACT UNCHANGED vanilla T.W.I.G. build piece (internal id
    // "TrainingDummy") as placeable — no altered recipe, durability, resistances, or behaviour. The
    // capability is governed by the shared T014/T015 grammar and adds NOTHING to it:
    //
    //   * WHETHER the effect is active for the occupant is decided entirely by the pure
    //     LocalEffectActivationView (developed + authorized Governor + Active Stone Level + committed
    //     Tree + inside Stone Area + the single Settlement Local policy). This provider never re-derives
    //     or second-guesses that projection, and it holds NO active-effects ledger of its own
    //     (AT-NO-ACTIVE-LEDGER): flip any relationship/policy/governance input, re-derive the view, and
    //     the same authored T.W.I.G. capability flips available<->unavailable with zero writes.
    //   * The load-bearing AND (spec FR-016 final sentence): the effect being active is only HALF a
    //     placement capability. The occupant must independently pass ORDINARY build Permission. Neither
    //     the relationship nor the Local policy silently grants a build ACL, so a policy-eligible
    //     occupant without build Permission cannot place, and a build-permitted occupant outside the
    //     policy cannot place. This provider evaluates both conjuncts separately only to report a precise
    //     reason; the admit decision is exactly LocalEffectActivationView.CanExercisePlacement.
    //   * EXACT piece only. The provider authorizes a placement of the ONE authored T.W.I.G. prefab and
    //     nothing else. An unknown/renamed prefab, or any other build piece, is rejected — the T.W.I.G.
    //     Training node never widens to a general build grant, and it does not overlap another Tree's
    //     Local node (Savor/Refined/Practice Range are governed by their own catalog rows).
    //
    // The prefab name a placement carries is a SERVER-OBSERVED fact (the ZDO prefab of the piece the
    // player is placing), never a client claim of eligibility — exactly like FoundationalPlacementAdapter.
    //
    // net48 audit: System + the engine-free activation view / content catalog / snapshot value objects
    // only. No net5+ API, no UnityEngine/Valheim/BepInEx reference, so this link-compiles into the net8
    // test project alongside the rest of the Homestead progression slice.

    /// <summary>Stable outcome of one T.W.I.G. placement authorization. Machine codes are contract; any
    /// localized text is presentation.</summary>
    public enum WarriorPlacementAdmission
    {
        /// <summary>The exact authored T.W.I.G. piece may be placed: the Local Effect is active for this
        /// occupant AND the occupant holds ordinary build Permission.</summary>
        Admitted,

        /// <summary>The requested prefab is not the exact authored T.W.I.G. build piece. The T.W.I.G.
        /// Training node authorizes ONLY the one vanilla training dummy; it never grants any other piece.</summary>
        NotTwigPiece,

        /// <summary>The T.W.I.G. Training Local Effect is not currently active for this occupant — it is
        /// dormant or the occupant is outside the Settlement policy / Stone Area / governance. Retained
        /// developed state is never deleted; the capability simply is not exercisable right now.</summary>
        EffectNotActive,

        /// <summary>The effect is active for the occupant but they lack ORDINARY build Permission. Neither
        /// relationship nor Local policy grants a build ACL (spec FR-016 final sentence).</summary>
        MissingBuildPermission
    }

    /// <summary>The result of authorizing a T.W.I.G. placement. When <see cref="IsAdmitted"/>, the
    /// authorized prefab is the exact unchanged vanilla T.W.I.G. build piece the server should allow.</summary>
    public readonly struct WarriorPlacementDecision
    {
        public WarriorPlacementDecision(WarriorPlacementAdmission admission, string authorizedPrefabName)
        {
            Admission = admission;
            AuthorizedPrefabName = authorizedPrefabName;
        }

        public WarriorPlacementAdmission Admission { get; }
        public bool IsAdmitted => Admission == WarriorPlacementAdmission.Admitted;

        /// <summary>Only meaningful when <see cref="IsAdmitted"/>: the exact authored T.W.I.G. prefab the
        /// server permits. Empty on any rejection.</summary>
        public string AuthorizedPrefabName { get; }
    }

    /// <summary>The T.W.I.G. Training Local placement capability provider. Pure/engine-free: it composes
    /// the shared Local Effect projection with the exact authored T.W.I.G. piece identity and answers a
    /// single question — "may THIS occupant place THIS prefab as the T.W.I.G. Training effect right now?"
    /// It stores no per-occupant activation state and mutates nothing.</summary>
    public sealed class LocalPlacementProvider
    {
        /// <summary>The exact unchanged vanilla T.W.I.G. build-piece internal id (Valheim "TrainingDummy",
        /// wiki: T.W.I.G.). The node exposes THIS prefab and no other. Stored as the one authored binding
        /// so a rename can never silently authorize a different piece.</summary>
        public const string TwigPrefabName = "TrainingDummy";

        private readonly VersionedId _node;
        private readonly string _prefabName;

        /// <summary>Current-build provider: the authored Warrior T.W.I.G. Training node bound to the exact
        /// vanilla T.W.I.G. prefab.</summary>
        public LocalPlacementProvider()
            : this(new VersionedId("TwigTraining", 1), TwigPrefabName) { }

        /// <summary>Explicit binding (node id + exact prefab). Used by conformance/tests to pin the exact
        /// authored identity; production wiring uses the parameterless current-build binding.</summary>
        public LocalPlacementProvider(VersionedId node, string prefabName)
        {
            if (node.IsNone) throw new ArgumentException("T.W.I.G. Training node id must be set.", nameof(node));
            if (string.IsNullOrEmpty(prefabName))
                throw new ArgumentException("T.W.I.G. prefab name must be set.", nameof(prefabName));
            _node = node;
            _prefabName = prefabName;
        }

        /// <summary>The authored Warrior T.W.I.G. Training node this provider governs.</summary>
        public VersionedId Node => _node;

        /// <summary>The exact authored T.W.I.G. prefab this node exposes.</summary>
        public string PrefabName => _prefabName;

        /// <summary>Authorize a server-observed T.W.I.G. placement attempt for one occupant. The
        /// <paramref name="activation"/> view is the pure per-occupant Local Effect projection (already
        /// carrying the Settlement policy, governance, occupancy, dormancy state); <paramref
        /// name="requestedPrefabName"/> is the SERVER-OBSERVED prefab the occupant is placing; <paramref
        /// name="hasOrdinaryBuildPermission"/> is the independent ordinary build ACL result.
        ///
        /// The piece gate is checked first for precise diagnosis, then the effect-active and build
        /// Permission conjuncts. The admit decision for the exact piece is identical to
        /// <see cref="LocalEffectActivationView.CanExercisePlacement"/>; the separate reason codes only
        /// tell a rejected caller which half failed.</summary>
        public WarriorPlacementDecision Admit(
            LocalEffectActivationView activation,
            string requestedPrefabName,
            bool hasOrdinaryBuildPermission)
        {
            if (activation == null) throw new ArgumentNullException(nameof(activation));

            // Exact piece only — the T.W.I.G. Training node never authorizes any other prefab.
            if (!string.Equals(requestedPrefabName, _prefabName, StringComparison.Ordinal))
                return Rejected(WarriorPlacementAdmission.NotTwigPiece);

            // The effect must currently be active for this occupant (policy + governance + occupancy +
            // Stone Level + committed Tree). Dormancy retains development but suppresses the capability.
            if (!activation.StatusFor(_node).Active)
                return Rejected(WarriorPlacementAdmission.EffectNotActive);

            // The load-bearing AND: the occupant must independently hold ordinary build Permission.
            if (!hasOrdinaryBuildPermission)
                return Rejected(WarriorPlacementAdmission.MissingBuildPermission);

            return new WarriorPlacementDecision(WarriorPlacementAdmission.Admitted, _prefabName);
        }

        /// <summary>Convenience boolean mirroring <see cref="LocalEffectActivationView.CanExercisePlacement"/>
        /// for the exact authored T.W.I.G. piece: active for the occupant AND ordinary build Permission AND
        /// the requested prefab is exactly the authored T.W.I.G.</summary>
        public bool CanPlace(LocalEffectActivationView activation, string requestedPrefabName,
            bool hasOrdinaryBuildPermission)
            => Admit(activation, requestedPrefabName, hasOrdinaryBuildPermission).IsAdmitted;

        private WarriorPlacementDecision Rejected(WarriorPlacementAdmission admission) =>
            new WarriorPlacementDecision(admission, string.Empty);
    }
}
