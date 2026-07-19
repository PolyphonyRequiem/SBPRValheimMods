using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Snapshots;

namespace SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression
{
    // T031 — Tracer 8 (Warrior), node 3 of 3. Weapon Discipline durable skill-cap CHOICE state and the
    // pure choice transition (data-model.md §CharacterProgression "Skill-cap choices | stable choice
    // records, including Weapon Discipline and cap-provider provenance"; contracts.md
    // §ChooseWeaponDisciplineSkill; spec §"Warrior": "Weapon Discipline grants one permanent, idempotent
    // choice among at least two authored melee skill-cap tiers").
    //
    // Weapon Discipline is a PERMANENT Effect (data-model.md Warrior L1 "Permanent Effect"). Its outcome
    // is ONE selected authored melee skill-cap tier that, once committed, is PERMANENT: it survives
    // relationship loss, death, and Tree revocation (data-model.md invariant "Permanent Effects and
    // Progression Keys survive relationship loss and Tree revocation"). Unlike a Character Effect, the
    // cap it supplies is NOT gated on an active relationship — it is a durable choice/provenance record,
    // read directly by the SkillCapProvider.
    //
    // This file owns the CHARACTER-side authoritative state + pure transition ONLY:
    //   * SkillCapChoiceRecord — the durable {grant node identity/version, choice-catalog version,
    //     selected stable choice id, target melee skill, cap tier value (≤100), source op} provenance
    //     record persisted on the CharacterStoneRecord (the "Skill-cap choices" field group).
    //   * ResolvedSkillCapChoice — the value the application layer resolves from the authored choice
    //     catalog (Adapters/Warrior/SkillCapProvider.WeaponDisciplineChoiceCatalog) and hands to the
    //     transition, so the DOMAIN never depends on the adapter-layer catalog (clean layering).
    //   * SkillCapChoices.Choose — the pure transition that validates the accepted contract and produces
    //     the next character with exactly ONE appended choice record. It never mutates its input, never
    //     journals, and never raises every melee cap (the choice names ONE target skill).
    //
    // Accepted contract encoded here (contracts.md §ChooseWeaponDisciplineSkill "Validates"):
    //   * Weapon Discipline purchased/eligible for this caller at this Stone (NotPurchased);
    //   * at least two authored choices in the current catalog (CatalogTooSmall);
    //   * the selected skill is offered by the catalog (ChoiceNotOffered — resolved==null);
    //   * the authored cap tier does not exceed the hard skill cap of 100 (CapExceedsMax — an authoring
    //     guard so a bad catalog can never grant a >100 cap; spec/research "values ≤100");
    //   * no prior committed choice for this grant identity (AlreadyChosen — "cannot be spent twice").
    //
    // The operation-replay (same op returns the recorded terminal) and revision/CAS concerns live in the
    // application command handler (WeaponDisciplineCommandHandler), mirroring how PurchaseCommands gates
    // authority/concurrency around the pure NodePurchases transition.
    //
    // net48 audit: engine-free (System.Collections.Generic + value objects + snapshot codec). No net5+
    // surface, no UnityEngine/Valheim/BepInEx reference: link-compiles into the net8 test project.

    /// <summary>The hard vanilla skill cap. Every authored Weapon Discipline cap tier and every composed
    /// cap-provider result is clamped to this ceiling (spec/research "values ≤100"). A tier authored above
    /// it is a content error the transition rejects rather than silently honouring.</summary>
    public static class SkillCapLimits
    {
        public const int HardSkillCap = 100;
    }

    /// <summary>The result of resolving a caller-selected choice against the authored Weapon Discipline
    /// choice catalog (done by the application layer from the adapter-owned catalog). A null resolution at
    /// the command layer maps to <see cref="SkillCapChoiceResult.ChoiceNotOffered"/>. Carries only value
    /// data so the pure domain transition never depends on the adapter-layer catalog type.</summary>
    public readonly struct ResolvedSkillCapChoice
    {
        public ResolvedSkillCapChoice(string choiceId, int catalogVersion, string targetSkill, int capValue)
        {
            ChoiceId = choiceId ?? string.Empty;
            CatalogVersion = catalogVersion;
            TargetSkill = targetSkill ?? string.Empty;
            CapValue = capValue;
        }

        /// <summary>Stable authored id of the selected choice (never a display label).</summary>
        public string ChoiceId { get; }

        /// <summary>Version of the authored choice catalog the selection was made against.</summary>
        public int CatalogVersion { get; }

        /// <summary>The one target melee skill class name this choice raises (engine-free string mirror of
        /// the adapter's WeaponSkillClass). The choice raises ONLY this skill's cap, never every cap.</summary>
        public string TargetSkill { get; }

        /// <summary>The authored cap tier value this choice grants for <see cref="TargetSkill"/>.</summary>
        public int CapValue { get; }

        public bool IsNone => string.IsNullOrEmpty(ChoiceId);
    }

    /// <summary>One durable Weapon Discipline skill-cap choice + cap-provider provenance record. Keyed by
    /// the grant node identity/version (the "grant identity"); one permanent record per grant that cannot
    /// be spent twice. Persisted on the owning CharacterStoneRecord (data-model.md "Skill-cap choices").</summary>
    public sealed class SkillCapChoiceRecord
    {
        public SkillCapChoiceRecord(VersionedId grantNode, int catalogVersion, string choiceId,
            string targetSkill, int capValue, string sourceOperationId)
        {
            GrantNode = grantNode;
            CatalogVersion = catalogVersion;
            ChoiceId = choiceId ?? string.Empty;
            TargetSkill = targetSkill ?? string.Empty;
            CapValue = capValue;
            SourceOperationId = sourceOperationId ?? string.Empty;
        }

        /// <summary>The Weapon Discipline grant node identity/version this choice belongs to (the grant
        /// identity). Idempotency is keyed on this: one choice per grant, permanently.</summary>
        public VersionedId GrantNode { get; }

        public int CatalogVersion { get; }
        public string ChoiceId { get; }
        public string TargetSkill { get; }
        public int CapValue { get; }
        public string SourceOperationId { get; }

        public string Serialize() => new SnapshotWriter()
            .Put("grant", GrantNode.Serialize())
            .PutInt("catVer", CatalogVersion)
            .Put("choice", ChoiceId)
            .Put("skill", TargetSkill)
            .PutInt("cap", CapValue)
            .Put("srcOp", SourceOperationId)
            .Build();

        public static SkillCapChoiceRecord Deserialize(string s)
        {
            var r = new SnapshotReader(s);
            return new SkillCapChoiceRecord(
                VersionedId.Deserialize(r.GetString("grant")),
                r.GetInt("catVer"),
                r.GetString("choice"),
                r.GetString("skill"),
                r.GetInt("cap"),
                r.GetString("srcOp"));
        }
    }

    public enum SkillCapChoiceResult
    {
        Applied = 0,
        NotPurchased = 1,      // caller holds no Weapon Discipline purchase record at this Stone
        CatalogTooSmall = 2,   // fewer than two authored choices in the current catalog
        ChoiceNotOffered = 3,  // selected choice does not resolve in the current catalog
        CapExceedsMax = 4,     // authored cap tier exceeds the hard skill cap of 100 (authoring guard)
        AlreadyChosen = 5      // a permanent choice for this grant identity already exists (idempotent)
    }

    /// <summary>Result of a pure ChooseWeaponDisciplineSkill transition. On rejection <see
    /// cref="NextCharacter"/> is the UNCHANGED input aggregate. On acceptance it carries the next
    /// character with exactly one appended <see cref="SkillCapChoiceRecord"/> and the committed record.</summary>
    public readonly struct SkillCapChoiceTransition
    {
        private SkillCapChoiceTransition(SkillCapChoiceResult result, CharacterProgressionAggregate next,
            SkillCapChoiceRecord? committed)
        {
            Result = result;
            NextCharacter = next;
            Committed = committed;
        }

        public SkillCapChoiceResult Result { get; }
        public bool Accepted => Result == SkillCapChoiceResult.Applied;
        public CharacterProgressionAggregate NextCharacter { get; }

        /// <summary>The one committed choice record (only meaningful when <see cref="Accepted"/>).</summary>
        public SkillCapChoiceRecord? Committed { get; }

        public static SkillCapChoiceTransition Reject(SkillCapChoiceResult result,
            CharacterProgressionAggregate character) =>
            new SkillCapChoiceTransition(result, character, null);

        public static SkillCapChoiceTransition Accept(CharacterProgressionAggregate next,
            SkillCapChoiceRecord committed) =>
            new SkillCapChoiceTransition(SkillCapChoiceResult.Applied, next, committed);
    }

    /// <summary>Pure Weapon Discipline choice transition + durable-choice reads over the character
    /// aggregate. Choose commits ONE permanent choice; the read helpers expose the persisted choices so
    /// the SkillCapProvider composes the effective cap without a second ledger.</summary>
    public static class SkillCapChoices
    {
        /// <summary>ChooseWeaponDisciplineSkill (contracts.md). Validates the accepted contract against the
        /// current character snapshot + the caller-resolved authored choice, then produces the next
        /// character with exactly ONE appended permanent choice record on the owning Stone record. Never
        /// mutates its inputs, never journals.</summary>
        /// <param name="character">Current authoritative character aggregate.</param>
        /// <param name="stoneId">Stone the Weapon Discipline grant/choice belongs to.</param>
        /// <param name="grantNode">The Weapon Discipline grant node identity/version (grant identity).</param>
        /// <param name="resolved">The choice resolved from the authored catalog, or None when the selected
        /// choice did not resolve (ChoiceNotOffered).</param>
        /// <param name="authoredChoiceCount">Count of authored choices in the current catalog (≥2 required).</param>
        /// <param name="sourceOperationId">Provenance op id stamped on the committed choice record.</param>
        public static SkillCapChoiceTransition Choose(
            CharacterProgressionAggregate character,
            StoneId stoneId,
            VersionedId grantNode,
            ResolvedSkillCapChoice resolved,
            int authoredChoiceCount,
            string sourceOperationId)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));

            // At least two authored choices must exist (contracts.md: "at least two authored choices in
            // the current catalog"). A degenerate one-choice catalog is not a real choice.
            if (authoredChoiceCount < 2)
                return SkillCapChoiceTransition.Reject(SkillCapChoiceResult.CatalogTooSmall, character);

            // The selected skill must be offered by the catalog (resolved is None when it did not resolve).
            if (resolved.IsNone)
                return SkillCapChoiceTransition.Reject(SkillCapChoiceResult.ChoiceNotOffered, character);

            // The authored cap tier can never exceed the hard skill cap of 100 (spec/research "values
            // ≤100"). This is an authoring guard: a misconfigured catalog cannot grant a >100 cap.
            if (resolved.CapValue > SkillCapLimits.HardSkillCap)
                return SkillCapChoiceTransition.Reject(SkillCapChoiceResult.CapExceedsMax, character);

            var sr = FindStoneRecord(character, stoneId);

            // Weapon Discipline must be purchased/eligible for this caller at this Stone. The choice is
            // downstream of the permanent-node purchase (data-model.md Warrior L1 Permanent Effect,
            // personal Offered). No purchase -> nothing to choose against.
            if (!HasPurchase(sr, grantNode))
                return SkillCapChoiceTransition.Reject(SkillCapChoiceResult.NotPurchased, character);

            // No prior committed choice for this grant identity: the permanent choice cannot be spent
            // twice (contracts.md "It cannot be spent twice"). Idempotent replay of the SAME operation is
            // handled by the command layer; a distinct SECOND choice attempt rejects here.
            if (HasChoiceFor(sr, grantNode))
                return SkillCapChoiceTransition.Reject(SkillCapChoiceResult.AlreadyChosen, character);

            var record = new SkillCapChoiceRecord(grantNode, resolved.CatalogVersion, resolved.ChoiceId,
                resolved.TargetSkill, resolved.CapValue, sourceOperationId ?? string.Empty);

            var next = WithChoice(character, stoneId, sr, record);
            return SkillCapChoiceTransition.Accept(next, record);
        }

        /// <summary>Every durable Weapon Discipline choice record this character holds at the given Stone.
        /// Pure read of persisted provenance — the SkillCapProvider composes the effective cap from these
        /// (no second active-effect ledger).</summary>
        public static IReadOnlyList<SkillCapChoiceRecord> ChoicesAt(
            CharacterProgressionAggregate character, StoneId stoneId)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            var result = new List<SkillCapChoiceRecord>();
            foreach (var sr in character.StoneRecords)
            {
                if (!sr.StoneId.Equals(stoneId)) continue;
                foreach (var c in sr.SkillCapChoices) result.Add(c);
            }
            return result;
        }

        private static CharacterStoneRecord? FindStoneRecord(CharacterProgressionAggregate character, StoneId stoneId)
        {
            foreach (var sr in character.StoneRecords)
                if (sr.StoneId.Equals(stoneId)) return sr;
            return null;
        }

        private static bool HasPurchase(CharacterStoneRecord? sr, VersionedId grantNode)
        {
            if (sr == null) return false;
            foreach (var p in sr.Purchases)
                if (p.Node.Equals(grantNode)) return true;
            return false;
        }

        private static bool HasChoiceFor(CharacterStoneRecord? sr, VersionedId grantNode)
        {
            if (sr == null) return false;
            foreach (var c in sr.SkillCapChoices)
                if (c.GrantNode.Equals(grantNode)) return true;
            return false;
        }

        private static CharacterProgressionAggregate WithChoice(
            CharacterProgressionAggregate character, StoneId stoneId, CharacterStoneRecord? existing,
            SkillCapChoiceRecord record)
        {
            var newRecords = new List<CharacterStoneRecord>(character.StoneRecords.Count + 1);
            bool found = false;
            foreach (var sr in character.StoneRecords)
            {
                if (!sr.StoneId.Equals(stoneId))
                {
                    newRecords.Add(sr);
                    continue;
                }
                found = true;
                newRecords.Add(AppendChoice(sr, record));
            }
            if (!found)
            {
                // Defensive fresh-record path: a choice can only reach here when the caller already holds a
                // purchase (so a record exists), but keep parity with WithPurchase's seed path.
                var seed = new CharacterStoneRecord(stoneId, 0, 0, 0);
                newRecords.Add(AppendChoice(seed, record));
            }

            return new CharacterProgressionAggregate(
                character.Account, character.Character, character.WorldProductScope,
                character.Revision + 1, character.BondSlots, character.AttunementSlots,
                character.LastAppliedReceiptId, newRecords, character.SchemaVersion);
        }

        private static CharacterStoneRecord AppendChoice(CharacterStoneRecord sr, SkillCapChoiceRecord record)
        {
            var choices = new List<SkillCapChoiceRecord>(sr.SkillCapChoices.Count + 1);
            foreach (var c in sr.SkillCapChoices) choices.Add(c);
            choices.Add(record);
            return new CharacterStoneRecord(sr.StoneId, sr.PersonalAp, sr.CumulativeAp, sr.PersonalBp,
                sr.FacetCredits, sr.Purchases, sr.Relationships, choices);
        }
    }
}
