using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Adapters.Warrior
{
    // T031 — Tracer 8 (Warrior), node 3 of 3. The Weapon Discipline skill-cap provider (spec §"Warrior":
    // "Weapon Discipline grants one permanent, idempotent choice among at least two authored melee
    // skill-cap tiers"; contracts.md §Warrior: "SkillCapProvider: Weapon Discipline supplies the one
    // selected authored cap tier, highest-wins"; data-model.md Warrior L1 "Weapon Discipline | Permanent
    // Effect | personal Offered").
    //
    // Architecture (plan.md A1; mirrors the sibling providers): a provider is a DERIVED, read-only
    // translation of already-persisted state into the exact vanilla-facing value the engine seam
    // consumes. It is NOT a second ledger and it NEVER mutates a skill, a stat, or a shared prefab. Where
    // Ready Hands / Savor are relationship-gated Character/Local effects, Weapon Discipline is a PERMANENT
    // Effect: its selected cap tier is read straight from the durable SkillCapChoiceRecord provenance and
    // is NOT suppressed by relationship loss / death / Tree revocation (data-model.md invariant "Permanent
    // Effects and Progression Keys survive relationship loss and Tree revocation").
    //
    // Two responsibilities, both pure:
    //   1. The AUTHORED CHOICE CATALOG — the fixed roster of at least two melee skill-cap tiers the player
    //      picks ONE of. Each entry names ONE target melee skill and ONE authored cap value (≤100). The
    //      choice raises ONLY that skill's cap, so the node "cannot raise every melee cap" (contracts.md).
    //   2. The EFFECTIVE-CAP COMPOSITION — given the durable choices a character committed at a Stone,
    //      resolve the cap that applies to a given melee skill. Composition is HIGHEST-WINS (contracts.md
    //      "highest-wins"): the vanilla baseline cap is 100 for every skill, and a Weapon Discipline choice
    //      NEVER lowers a cap — the effective cap is max(baseline, any authored choice for that skill),
    //      always clamped to the hard cap of 100. So for the shipped baseline-100 game the visible effect
    //      is that a lower authored tier can never reduce a skill below its vanilla ceiling; the seam and
    //      composition are authored so a future sub-100 baseline (a harder-mode content build) would see
    //      the selected tier raise exactly the chosen skill and nothing else.
    //
    // net48 audit: value objects + System.Collections.Generic only. No net5+ API, no UnityEngine /
    // Valheim / BepInEx reference, so this core link-compiles into the net8 test project exactly like the
    // sibling providers while shipping under net48.

    /// <summary>The stable identity of the Weapon Discipline personal node (Warrior Tree, Level 1). Pinned
    /// here so the provider, the choice command, and the durable choice record agree on exactly which
    /// grant a choice belongs to, independent of display label.</summary>
    public static class WeaponDisciplineNode
    {
        public static readonly VersionedId WeaponDiscipline = new VersionedId("WeaponDiscipline", 1);
    }

    /// <summary>One authored Weapon Discipline melee skill-cap tier the player may pick. A choice raises
    /// ONE target melee skill to <see cref="CapValue"/> (never every cap). Stable <see cref="ChoiceId"/>
    /// is identity; the display label is separate presentation.</summary>
    public readonly struct WeaponDisciplineChoice
    {
        public WeaponDisciplineChoice(string choiceId, WeaponSkillClass targetSkill, int capValue,
            string displayLabel)
        {
            ChoiceId = choiceId ?? string.Empty;
            TargetSkill = targetSkill;
            CapValue = capValue;
            DisplayLabel = displayLabel ?? string.Empty;
        }

        /// <summary>Stable authored id (never a display label).</summary>
        public string ChoiceId { get; }

        /// <summary>The single melee skill class this choice raises the cap of.</summary>
        public WeaponSkillClass TargetSkill { get; }

        /// <summary>The authored cap tier value for <see cref="TargetSkill"/>. Authored ≤100.</summary>
        public int CapValue { get; }

        public string DisplayLabel { get; }
    }

    /// <summary>The Weapon Discipline skill-cap provider (contracts.md §Warrior). Owns the authored choice
    /// catalog and composes the effective per-skill cap from a character's durable choices, highest-wins.
    /// Stateless: every answer is a pure function of the supplied persisted choice records + the authored
    /// catalog, so death / relationship loss / revocation do not change the committed cap (it is a
    /// Permanent Effect) and there is no second ledger.</summary>
    public sealed class SkillCapProvider
    {
        /// <summary>The vanilla baseline hard skill cap for every skill (Skills.cs m_skillCeiling = 100,
        /// decomp assembly_valheim — vanilla is fair game per AGENTS.md/ADR-0001). A Weapon Discipline
        /// choice composes highest-wins against this baseline and is clamped to it.</summary>
        public const int VanillaBaselineCap = SkillCapLimits.HardSkillCap; // 100

        /// <summary>Current authored Weapon Discipline choice-catalog version. Bumped only when the roster
        /// changes; stamped on every durable choice so a later catalog revision never silently rebinds a
        /// committed selection.</summary>
        public const int CurrentCatalogVersion = 1;

        // The authored roster: at least two melee skill-cap tiers (contracts.md "at least two authored
        // choices"). PROVISIONAL proof-only values (mirrors the sibling providers' provisional tuning);
        // final skill-cap ladder is explicitly deferred (spec §Non-goals "final skill-cap ladder"). Each
        // entry raises exactly ONE melee skill from the eligible Ready-Hands registry, so no single choice
        // raises every melee cap. Two distinct target skills prove the "one selected tier" semantics.
        private static readonly IReadOnlyList<WeaponDisciplineChoice> Catalog = new[]
        {
            new WeaponDisciplineChoice("swordmastery", WeaponSkillClass.Swords, 100, "Sword Mastery"),
            new WeaponDisciplineChoice("axemastery", WeaponSkillClass.Axes, 100, "Axe Mastery"),
        };

        private readonly VersionedId _grant;

        public SkillCapProvider() : this(WeaponDisciplineNode.WeaponDiscipline) { }

        /// <summary>Test/extension seam: bind the provider to an explicit Weapon Discipline grant identity.</summary>
        public SkillCapProvider(VersionedId grantNode)
        {
            if (grantNode.IsNone)
                throw new ArgumentException("Weapon Discipline grant node id must not be None.", nameof(grantNode));
            _grant = grantNode;
        }

        /// <summary>The authored Weapon Discipline grant node this provider governs.</summary>
        public VersionedId Grant => _grant;

        /// <summary>The authored choice catalog (at least two tiers). Read-only; the command layer resolves
        /// a caller selection against this and the durable record persists the resolved value.</summary>
        public IReadOnlyList<WeaponDisciplineChoice> Choices => Catalog;

        /// <summary>Number of authored choices in the current catalog (≥2). Handed to the pure choice
        /// transition so the "at least two authored choices" gate is evaluated against the real roster.</summary>
        public int ChoiceCount => Catalog.Count;

        /// <summary>Resolve a caller-selected stable choice id against the authored catalog, producing the
        /// value the pure domain transition commits. Returns <see cref="ResolvedSkillCapChoice.IsNone"/>
        /// (a None resolution) when the id is not offered or the catalog version is stale — the command
        /// layer maps that to ChoiceNotOffered. The resolved cap is clamped to the hard cap defensively;
        /// the transition additionally rejects an authored value that exceeds it.</summary>
        public ResolvedSkillCapChoice Resolve(string choiceId, int catalogVersion)
        {
            if (string.IsNullOrEmpty(choiceId)) return default;
            if (catalogVersion != CurrentCatalogVersion) return default;
            foreach (var c in Catalog)
            {
                if (!string.Equals(c.ChoiceId, choiceId, StringComparison.Ordinal)) continue;
                return new ResolvedSkillCapChoice(c.ChoiceId, CurrentCatalogVersion,
                    c.TargetSkill.ToString(), c.CapValue);
            }
            return default;
        }

        /// <summary>The effective skill cap for one melee skill, composed HIGHEST-WINS (contracts.md) from
        /// the character's durable Weapon Discipline choices at this Stone against the vanilla baseline of
        /// 100. Convenience overload for the shipped game; see the baseline overload for a build whose
        /// baseline differs.</summary>
        public int EffectiveCap(CharacterProgressionAggregate character, StoneId stoneId,
            WeaponSkillClass skill)
            => EffectiveCap(character, stoneId, skill, VanillaBaselineCap);

        /// <summary>The effective skill cap for one melee skill, composed HIGHEST-WINS (contracts.md) from
        /// the character's durable Weapon Discipline choices at this Stone against <paramref
        /// name="baselineCap"/>. The result is never below the baseline (a choice cannot lower a cap) and
        /// never above the hard cap of 100 (values ≤100). ONLY choices whose target skill matches
        /// <paramref name="skill"/> contribute, so the selection raises exactly the chosen skill and no
        /// other (the node "cannot raise every melee cap"). Pure: it reads only the persisted choice
        /// provenance, so the same character composes the same cap after death / relationship loss /
        /// revocation (a Permanent Effect).</summary>
        public int EffectiveCap(CharacterProgressionAggregate character, StoneId stoneId,
            WeaponSkillClass skill, int baselineCap)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            int cap = baselineCap;
            string skillName = skill.ToString();
            foreach (var choice in SkillCapChoices.ChoicesAt(character, stoneId))
            {
                if (!choice.GrantNode.Equals(_grant)) continue;
                if (!string.Equals(choice.TargetSkill, skillName, StringComparison.Ordinal)) continue;
                if (choice.CapValue > cap) cap = choice.CapValue; // highest-wins
            }
            return cap > SkillCapLimits.HardSkillCap ? SkillCapLimits.HardSkillCap : cap;
        }

        /// <summary>The effective cap composed highest-wins from an explicit set of authored cap values for
        /// ONE skill (e.g. multiple providers/choices contributing to the same skill). Never below the
        /// baseline, never above the hard cap. Exposed so the composition rule is unit-testable
        /// independent of the aggregate, and reusable if a future build has more than one cap contributor.</summary>
        public static int ComposeHighestWins(int baselineCap, IEnumerable<int> contributedCaps)
        {
            int cap = baselineCap;
            if (contributedCaps != null)
                foreach (var v in contributedCaps)
                    if (v > cap) cap = v;
            if (cap < 0) cap = 0;
            return cap > SkillCapLimits.HardSkillCap ? SkillCapLimits.HardSkillCap : cap;
        }

        /// <summary>Whether the character has committed a Weapon Discipline choice for this grant at the
        /// Stone (the permanent selection exists). Pure read of durable provenance.</summary>
        public bool HasChosen(CharacterProgressionAggregate character, StoneId stoneId)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            foreach (var c in SkillCapChoices.ChoicesAt(character, stoneId))
                if (c.GrantNode.Equals(_grant)) return true;
            return false;
        }
    }
}
