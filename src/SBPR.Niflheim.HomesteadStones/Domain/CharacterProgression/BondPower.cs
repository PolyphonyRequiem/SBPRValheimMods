using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression
{
    // T012 — pure Bond Power (BP) transitions over the character aggregate (data-model.md §Aggregate 3
    // "BP: one personal BP balance per bonded Stone; no Tree/source/target binding"; contracts.md
    // RecordAlignedActivity "BP to a bonded character: N to that character's one Stone-wide personal BP
    // balance"; §"Credit and spend BP on node development").
    //
    // BP lives on the character aggregate's per-Stone record (CharacterStoneRecord.PersonalBp). There is
    // exactly ONE personal BP balance per (character, Stone): it is Stone-WIDE, with NO Tree/source/target
    // binding, so BP credited by a Cooking activity is spendable on a committed Crafting node at the same
    // Stone (AT-BP-STONE-WIDE). Because the balance is per (AccountId, CharacterId, StoneId), a different
    // Governor character — even a sibling on the same account — has a SEPARATE balance and cannot spend
    // this character's BP (AT-BP-NOT-SHARED).
    //
    // These are the PURE transitions: given the current character aggregate, they validate and PRODUCE
    // the next aggregate. They never mutate in place, never journal, and never touch AP/purchases/
    // relationships. The durable, receipt-backed commit lives in the application command layer
    // (ActivityCommands.cs for credit, DevelopmentCommands.cs for the spend half of node development).
    //
    // Invariant (data-model.md): "Personal BP is never negative and can be spent only by that bonded
    // character within their current Responsibility Range." Non-negativity is enforced here; the
    // Responsibility-Range gate is enforced by the command layer (which reads the live Bond authority).
    //
    // net48 audit: engine-free (System.Collections.Generic + value objects). Link-compiles into net8.

    /// <summary>Result of a pure BP transition. On rejection <see cref="Character"/> is the UNCHANGED
    /// original aggregate, so a caller that commits it unconditionally still writes the prior state.</summary>
    public readonly struct BondPowerTransition
    {
        private BondPowerTransition(bool accepted, string resultCode,
            CharacterProgressionAggregate character, int resultingBp)
        {
            Accepted = accepted;
            ResultCode = resultCode;
            Character = character;
            ResultingBp = resultingBp;
        }

        public bool Accepted { get; }
        public string ResultCode { get; }
        public CharacterProgressionAggregate Character { get; }

        /// <summary>The character's Stone-wide personal BP balance after the transition (or the current
        /// balance on rejection).</summary>
        public int ResultingBp { get; }

        public static BondPowerTransition Reject(string code, CharacterProgressionAggregate character, int currentBp) =>
            new BondPowerTransition(false, code, character, currentBp);

        public static BondPowerTransition Accept(CharacterProgressionAggregate character, int resultingBp) =>
            new BondPowerTransition(true, "Applied", character, resultingBp);
    }

    /// <summary>Pure credit/debit transitions for the one Stone-wide personal BP balance. Every method
    /// validates non-negativity and returns the next aggregate; none mutate their inputs.</summary>
    public static class BondPower
    {
        /// <summary>The character's current Stone-wide personal BP balance at <paramref name="stoneId"/>,
        /// or 0 when the character has no record for that Stone yet.</summary>
        public static int BalanceFor(CharacterProgressionAggregate character, StoneId stoneId)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            foreach (var sr in character.StoneRecords)
                if (sr.StoneId.Equals(stoneId)) return sr.PersonalBp;
            return 0;
        }

        /// <summary>Credit <paramref name="amount"/> BP to the character's Stone-wide balance
        /// (RecordAlignedActivity). Amount must be positive; the balance is monotonic on credit. Every
        /// other balance/purchase/relationship field is preserved verbatim.</summary>
        public static BondPowerTransition Credit(
            CharacterProgressionAggregate character, StoneId stoneId, int amount,
            long? expectedCharacterRevision = null)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            int current = BalanceFor(character, stoneId);

            if (expectedCharacterRevision.HasValue && expectedCharacterRevision.Value != character.Revision)
                return BondPowerTransition.Reject("StaleCharacterRevision", character, current);
            if (amount <= 0)
                return BondPowerTransition.Reject("EvidenceInvalid", character, current);

            int next = current + amount;
            var newCharacter = WithBp(character, stoneId, next, character.Revision + 1);
            return BondPowerTransition.Accept(newCharacter, next);
        }

        /// <summary>Debit <paramref name="amount"/> BP from the character's Stone-wide balance (the spend
        /// half of ApplyBPToNode). Amount must be positive and must not exceed the balance
        /// (InsufficientBP), preserving the never-negative invariant. Every other field is preserved.</summary>
        public static BondPowerTransition Debit(
            CharacterProgressionAggregate character, StoneId stoneId, int amount,
            long? expectedCharacterRevision = null)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            int current = BalanceFor(character, stoneId);

            if (expectedCharacterRevision.HasValue && expectedCharacterRevision.Value != character.Revision)
                return BondPowerTransition.Reject("StaleCharacterRevision", character, current);
            if (amount <= 0)
                return BondPowerTransition.Reject("EvidenceInvalid", character, current);
            if (amount > current)
                return BondPowerTransition.Reject("InsufficientBP", character, current);

            int next = current - amount;
            var newCharacter = WithBp(character, stoneId, next, character.Revision + 1);
            return BondPowerTransition.Accept(newCharacter, next);
        }

        /// <summary>Produce a new character aggregate whose Stone record for <paramref name="stoneId"/>
        /// carries <paramref name="newBp"/>. When the character has no record for the Stone yet, a clean
        /// zeroed record carrying only the BP is added. Every other Stone record and field is verbatim.</summary>
        private static CharacterProgressionAggregate WithBp(
            CharacterProgressionAggregate character, StoneId stoneId, int newBp, long newRevision)
        {
            var newStoneRecords = new List<CharacterStoneRecord>(character.StoneRecords.Count + 1);
            bool replaced = false;
            foreach (var sr in character.StoneRecords)
            {
                if (!sr.StoneId.Equals(stoneId))
                {
                    newStoneRecords.Add(sr);
                    continue;
                }
                replaced = true;
                newStoneRecords.Add(new CharacterStoneRecord(sr.StoneId, sr.PersonalAp, sr.CumulativeAp,
                    newBp, sr.FacetCredits, sr.Purchases, sr.Relationships));
            }
            if (!replaced)
                newStoneRecords.Add(new CharacterStoneRecord(stoneId, 0, 0, newBp,
                    facetCredits: null, purchases: null, relationships: null));

            return new CharacterProgressionAggregate(
                character.Account, character.Character, character.WorldProductScope, newRevision,
                character.BondSlots, character.AttunementSlots, character.LastAppliedReceiptId,
                newStoneRecords, character.SchemaVersion);
        }
    }
}
