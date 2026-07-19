using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Activation;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Warrior
{
    // T030 — Tracer 8 (Warrior), node 2 of 3. The Ready Hands effect-delivery provider (spec
    // §"Warrior": "Ready Hands shortens both queued equip and unequip durations for eligible melee
    // weapons only while active"; contracts.md §Warrior: "EquipDurationProvider: Ready Hands modifies
    // copied queued equip and unequip durations for authored eligible melee weapons only; no shared
    // prefab mutation"; data-model.md §"Warrior | 1 | Ready Hands | Character Effect | personal Offered").
    //
    // Architecture (plan.md A1; mirrors SavorTheHearthProvider): a provider is a DERIVED, read-only
    // translation of already-derived activation state into the exact vanilla-facing factor the engine
    // seam consumes. It is NOT a second ledger and it NEVER mutates an item, a stat, or a shared prefab.
    // The active/dormant decision for this personal Character Effect is owned entirely by the T004
    // DerivedActivationView (purchased AND caller holds an active relationship); this provider only READS
    // that node's Active bit and answers "at what duration does THIS queued equip/unequip action run?".
    //
    // WHY THE COPIED DURATION SEAM IS SAFE (decomp — vanilla is fair game, AGENTS.md / ADR-0001):
    //   Humanoid.QueueEquipAction / QueueUnequipAction COPY item.m_shared.m_equipDuration into a fresh
    //   MinorActionData.m_duration (decomp assembly_valheim :22252 equip, :22275 unequip). The action
    //   queue ticks that per-action COPY (UpdateActionQueue :22211-22212), never the shared field. So a
    //   provider that scales the per-action copy shortens the queued action WITHOUT touching the shared
    //   ItemData prefab: every other item that shares the prefab, and the same item after the effect
    //   ends, keep the unchanged vanilla duration (AT-READY-HANDS-EXCLUSIONS "shared-prefab mutation").
    //   Reload is a THIRD MinorActionData type built from GetWeaponLoadingTime() (:22292), never from
    //   m_equipDuration, so it is structurally outside this provider's surface.
    //
    // ELIGIBLE MELEE REGISTRY (data-defined, engine-free mirror of Skills.SkillType — decomp :23820):
    //   Ready Hands is eligible for MELEE WEAPON skill classes only: Swords, Knives, Clubs, Polearms,
    //   Spears, Axes. It EXCLUDES (spec + research.md R "armor/tool/bow/reload exclusions"):
    //     * Bows / Crossbows — ranged, and Crossbows additionally carry the reload action.
    //     * Blocking — shields are not weapons.
    //     * Pickaxes / WoodCutting — tools, not combat melee weapons.
    //     * ElementalMagic / BloodMagic — magic staves are not melee weapons.
    //     * Unarmed — no equippable weapon item exists to queue an equip action for.
    //     * Armor — armor is not a weapon skill at all; an armor equip carries a weapon-skill of None.
    //   The registry is keyed on the weapon's authored skill class (a SERVER/engine-observed fact of the
    //   item being equipped), never a client claim of eligibility — exactly like the sibling providers.
    //
    // net48 audit: value objects + double arithmetic only. No net5+ API, no UnityEngine / Valheim /
    // BepInEx reference, so this core link-compiles into the net8 test project exactly like
    // Adapters/Cooking/CookingProviders.cs while shipping under net48.

    /// <summary>The stable identity of the Ready Hands personal node (Warrior Tree, Level 1). Pinned here
    /// so the provider and its live engine seam agree on exactly which purchased node drives the duration
    /// factor, independent of display label.</summary>
    public static class WarriorNodes
    {
        public static readonly VersionedId ReadyHands = new VersionedId("ReadyHands", 1);
    }

    /// <summary>Engine-free mirror of Valheim's <c>Skills.SkillType</c> weapon-skill classes the provider
    /// reasons about (decomp assembly_valheim :23820). Only the classes Ready Hands must distinguish are
    /// enumerated; the live engine seam maps the equipped item's real <c>m_shared.m_skillType</c> onto
    /// these. The numeric values intentionally match the vanilla enum so the mapping is a straight cast.</summary>
    public enum WeaponSkillClass
    {
        /// <summary>No weapon skill — e.g. armor, or a non-weapon item. Never eligible.</summary>
        None = 0,
        Swords = 1,
        Knives = 2,
        Clubs = 3,
        Polearms = 4,
        Spears = 5,
        Blocking = 6,   // shields — excluded (not a weapon)
        Axes = 7,
        Bows = 8,       // ranged — excluded
        ElementalMagic = 9, // magic — excluded
        BloodMagic = 10,    // magic — excluded
        Unarmed = 11,   // fists — no equippable weapon item, excluded
        Pickaxes = 12,  // tool — excluded
        WoodCutting = 13, // tool — excluded
        Crossbows = 14  // ranged + reload — excluded
    }

    /// <summary>Which queued minor action's copied duration is being resolved. Ready Hands covers BOTH
    /// halves (equip AND unequip) identically; Reload is a distinct vanilla action built from the weapon
    /// loading time, never from <c>m_equipDuration</c>, so it is modelled here only to prove it is
    /// excluded.</summary>
    public enum QueuedEquipAction
    {
        /// <summary>A queued equip action (Humanoid.QueueEquipAction, decomp :22237).</summary>
        Equip = 0,

        /// <summary>A queued unequip action (Humanoid.QueueUnequipAction, decomp :22262).</summary>
        Unequip = 1,

        /// <summary>A queued reload action (Humanoid.QueueReloadAction, decomp :22282). NOT an equip
        /// duration — built from GetWeaponLoadingTime(); always excluded from Ready Hands.</summary>
        Reload = 2
    }

    /// <summary>The result of resolving one queued equip/unequip action's duration under Ready Hands. The
    /// <see cref="Duration"/> is the value the engine seam should assign to the per-action
    /// <c>MinorActionData.m_duration</c> COPY — never written back to the shared prefab.</summary>
    public readonly struct EquipDurationDecision
    {
        public EquipDurationDecision(double duration, bool shortened, WeaponSkillClass skillClass,
            QueuedEquipAction action)
        {
            Duration = duration;
            Shortened = shortened;
            SkillClass = skillClass;
            Action = action;
        }

        /// <summary>The resolved per-action duration (seconds). Equal to the base duration when the effect
        /// is not delivering; the shortened duration when Ready Hands is active for an eligible melee
        /// weapon on an equip/unequip action.</summary>
        public double Duration { get; }

        /// <summary>True iff Ready Hands actually shortened this action (active + eligible + equip/unequip).</summary>
        public bool Shortened { get; }

        public WeaponSkillClass SkillClass { get; }
        public QueuedEquipAction Action { get; }
    }

    /// <summary>Pure derived provider for Ready Hands (contracts.md §Warrior). Translates the T004
    /// <see cref="DerivedActivationView"/> active-state for the Ready Hands personal node into the
    /// shortened queued equip/unequip duration for eligible melee weapons. Stateless: every answer is a
    /// pure function of the supplied derived view + the equipped weapon's authored skill class + the
    /// queued action kind, so relationship loss / dormancy flips the factor with zero writes and no
    /// shared-prefab mutation.</summary>
    public sealed class EquipDurationProvider
    {
        /// <summary>The authored duration factor while the effect is active for an eligible melee weapon:
        /// queued equip/unequip actions run at half the copied vanilla duration. PROVISIONAL playtest
        /// value (mirrors the Savor precedent); final beneficiary/balance tuning is deferred (research.md).
        /// This is the ONLY tuning knob.</summary>
        public const double ActiveDurationFactor = 0.5;

        /// <summary>The vanilla baseline factor: with no active Ready Hands effect (or an ineligible item /
        /// non-equip action), the queued action keeps the full copied vanilla duration.</summary>
        public const double InactiveDurationFactor = 1.0;

        /// <summary>The exact authored registry of eligible MELEE WEAPON skill classes. Data-defined and
        /// closed: only these classes are shortened. Everything else (ranged, shields, tools, magic,
        /// unarmed, armor/None) keeps the full vanilla duration.</summary>
        private static readonly HashSet<WeaponSkillClass> EligibleMeleeSkills = new HashSet<WeaponSkillClass>
        {
            WeaponSkillClass.Swords,
            WeaponSkillClass.Knives,
            WeaponSkillClass.Clubs,
            WeaponSkillClass.Polearms,
            WeaponSkillClass.Spears,
            WeaponSkillClass.Axes,
        };

        private readonly VersionedId _node;

        public EquipDurationProvider() : this(WarriorNodes.ReadyHands) { }

        /// <summary>Test/extension seam: bind the provider to an explicit Ready Hands node identity.</summary>
        public EquipDurationProvider(VersionedId readyHandsNode)
        {
            if (readyHandsNode.IsNone)
                throw new ArgumentException("Ready Hands node id must not be None.", nameof(readyHandsNode));
            _node = readyHandsNode;
        }

        /// <summary>The authored Warrior Ready Hands node this provider governs.</summary>
        public VersionedId Node => _node;

        /// <summary>Whether the given weapon skill class is in the authored eligible melee registry. Pure
        /// content predicate — no engine dependency, no view. Exposed so tests and the runtime seam agree
        /// on the exact registry membership.</summary>
        public static bool IsEligibleMeleeSkill(WeaponSkillClass skillClass) =>
            EligibleMeleeSkills.Contains(skillClass);

        /// <summary>Whether Ready Hands is currently ACTIVE (delivering) for the caller in the supplied
        /// derived activation view: the Ready Hands node is purchased AND the caller holds an active
        /// relationship (the T004 DerivedActivationView Active bit). Pure: re-derive the view after any
        /// relationship/level change and call again — the answer flips with zero state carried here.</summary>
        public bool IsActive(DerivedActivationView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            foreach (var n in view.Nodes)
                if (string.Equals(n.Node.Key, _node.Key, StringComparison.Ordinal))
                    return n.Active;
            return false;
        }

        /// <summary>Whether this queued action's duration is a candidate for shortening at all: only the
        /// Equip and Unequip halves are (they copy m_equipDuration). Reload is built from the weapon
        /// loading time and is always excluded.</summary>
        public static bool IsEquipDurationAction(QueuedEquipAction action) =>
            action == QueuedEquipAction.Equip || action == QueuedEquipAction.Unequip;

        /// <summary>The duration factor to apply to a queued equip/unequip action for this caller RIGHT
        /// NOW. Returns 0.5 iff Ready Hands is active for the caller AND the queued action is an
        /// equip/unequip half AND the weapon is in the eligible melee registry; otherwise 1.0. Pure —
        /// flip any input and re-derive with zero writes.</summary>
        public double DurationFactor(DerivedActivationView view, WeaponSkillClass skillClass,
            QueuedEquipAction action)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            return DurationFactor(IsActive(view), skillClass, action);
        }

        /// <summary>Factor overload taking the already-resolved Ready Hands active bit. The runtime seam
        /// resolves the active bit authoritatively (host derivation, or the server-stamped personal client
        /// cache) and hands it in directly, so the exclusion + action grammar stays the single authority
        /// without re-deriving a view on the client. Pure.</summary>
        public double DurationFactor(bool readyHandsActive, WeaponSkillClass skillClass,
            QueuedEquipAction action)
        {
            bool shortens = readyHandsActive
                && IsEquipDurationAction(action)
                && IsEligibleMeleeSkill(skillClass);
            return shortens ? ActiveDurationFactor : InactiveDurationFactor;
        }

        /// <summary>Resolve the per-action duration the engine seam should assign to the queued action's
        /// COPY of the weapon's equip duration. The <paramref name="baseDurationSeconds"/> is the copied
        /// vanilla <c>item.m_shared.m_equipDuration</c>; this returns <c>base * DurationFactor</c>. It
        /// performs NO mutation and touches NO shared prefab — it only computes the value the caller
        /// assigns to the fresh per-action MinorActionData.m_duration. A non-positive base (an instant
        /// toggle, which vanilla never queues — decomp :22097) is returned unchanged.</summary>
        public EquipDurationDecision ResolveDuration(DerivedActivationView view, WeaponSkillClass skillClass,
            QueuedEquipAction action, double baseDurationSeconds)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (baseDurationSeconds <= 0.0 || double.IsNaN(baseDurationSeconds))
                return new EquipDurationDecision(baseDurationSeconds, false, skillClass, action);

            double factor = DurationFactor(view, skillClass, action);
            bool shortened = factor < InactiveDurationFactor;
            return new EquipDurationDecision(baseDurationSeconds * factor, shortened, skillClass, action);
        }

        /// <summary>Resolve the per-action duration from the already-resolved Ready Hands active bit. Same
        /// semantics as the view overload; used by the runtime seam which resolves activation
        /// authoritatively before reaching the pure grammar. Pure — no mutation, no shared prefab.</summary>
        public EquipDurationDecision ResolveDuration(bool readyHandsActive, WeaponSkillClass skillClass,
            QueuedEquipAction action, double baseDurationSeconds)
        {
            if (baseDurationSeconds <= 0.0 || double.IsNaN(baseDurationSeconds))
                return new EquipDurationDecision(baseDurationSeconds, false, skillClass, action);

            double factor = DurationFactor(readyHandsActive, skillClass, action);
            bool shortened = factor < InactiveDurationFactor;
            return new EquipDurationDecision(baseDurationSeconds * factor, shortened, skillClass, action);
        }
    }
}
