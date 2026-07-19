using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression
{
    // T012 — Bond Power (BP) pure transitions (data-model.md §"Aggregate 3" BP; §"Credit and spend BP
    // on node development"; spec FR-010/FR-011). BP is ONE personal, Stone-wide balance per bonded
    // character per Stone: it carries NO Tree/source/target binding and is spendable across every
    // Committed Tree in that Governor's Responsibility Range. Different Governors NEVER share a balance
    // because BP lives on each character's own aggregate (never on the account, never on the Stone).
    //
    // These are the PURE domain transitions over CharacterProgressionAggregate: given the current
    // aggregate they validate the accepted invariants and PRODUCE the next authoritative state. They
    // never mutate in place, never journal, and never invent a refund/grant. The durable, receipt-backed
    // commit of the produced state lives in the application command layer (ActivityCommands.cs /
    // DevelopmentCommands.cs), mirroring Relationships.cs -> RelationshipCommands.cs.
    //
    // Load-bearing invariants encoded here (data-model.md CharacterProgression):
    //   * Personal BP is never negative. A debit larger than the current balance is rejected with ZERO
    //     mutation (InsufficientBp); a non-positive credit/debit amount is rejected (NonPositiveAmount).
    //   * BP is Stone-wide: it is keyed only by StoneId on the character aggregate, never by Tree. A
    //     credit/debit finds (or creates, for credit) the character's record at that Stone and adjusts
    //     ONLY its PersonalBp — Personal AP, Cumulative AP, Facet Credit, purchases, and relationships
    //     are preserved verbatim.
    //   * Every other Stone record on the aggregate is preserved verbatim (one Governor's Stone balance
    //     is independent of every other Stone AND of every other character).
    //
    // net48 audit: engine-free (System.Collections.Generic + value objects). No net5+ surface, no
    // UnityEngine/Valheim/BepInEx reference: link-compiles into the net8 test project.

    public enum BondPowerResult
    {
        Applied = 0,
        NonPositiveAmount = 1, // a credit/debit amount must be strictly positive
        InsufficientBp = 2,    // a debit would drive the Stone-wide balance negative
        StaleRevision = 3      // expected character revision did not match (optimistic concurrency)
    }

    /// <summary>Result of a pure BP transition. On rejection <see cref="NextCharacter"/> is the
    /// UNCHANGED input aggregate (a caller that commits it unconditionally still writes prior state),
    /// and <see cref="NewBalance"/> is the current (unchanged) Stone-wide balance.</summary>
    public readonly struct BondPowerTransition
    {
        private BondPowerTransition(BondPowerResult result, CharacterProgressionAggregate next, int newBalance)
        {
            Result = result;
            NextCharacter = next;
            NewBalance = newBalance;
        }

        public BondPowerResult Result { get; }
        public bool Accepted => Result == BondPowerResult.Applied;
        public CharacterProgressionAggregate NextCharacter { get; }

        /// <summary>The Stone-wide personal BP balance after the transition (unchanged on rejection).</summary>
        public int NewBalance { get; }

        public static BondPowerTransition Reject(BondPowerResult result,
            CharacterProgressionAggregate character, int currentBalance) =>
            new BondPowerTransition(result, character, currentBalance);

        public static BondPowerTransition Accept(CharacterProgressionAggregate next, int newBalance) =>
            new BondPowerTransition(BondPowerResult.Applied, next, newBalance);
    }

    /// <summary>Pure Bond Power transitions over the character aggregate. Credit adds to the one
    /// Stone-wide balance; debit removes from it under a strict non-negative invariant.</summary>
    public static class BondPower
    {
        /// <summary>The character's current Stone-wide personal BP balance at <paramref name="stoneId"/>
        /// (0 when the character has no record at that Stone yet).</summary>
        public static int BalanceAt(CharacterProgressionAggregate character, StoneId stoneId)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));
            foreach (var sr in character.StoneRecords)
                if (sr.StoneId.Equals(stoneId)) return sr.PersonalBp;
            return 0;
        }

        /// <summary>Credit <paramref name="amount"/> BP to the one Stone-wide personal balance at
        /// <paramref name="stoneId"/>. Creates the Stone record if absent (a first credit). Every other
        /// field/record is preserved verbatim; the aggregate revision advances once.</summary>
        public static BondPowerTransition Credit(
            CharacterProgressionAggregate character, StoneId stoneId, int amount,
            long? expectedCharacterRevision = null)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));

            int current = BalanceAt(character, stoneId);
            if (expectedCharacterRevision.HasValue && expectedCharacterRevision.Value != character.Revision)
                return BondPowerTransition.Reject(BondPowerResult.StaleRevision, character, current);
            if (amount <= 0)
                return BondPowerTransition.Reject(BondPowerResult.NonPositiveAmount, character, current);

            int newBalance = current + amount;
            var next = WithStoneBp(character, stoneId, newBalance);
            return BondPowerTransition.Accept(next, newBalance);
        }

        /// <summary>Debit <paramref name="amount"/> BP from the one Stone-wide personal balance at
        /// <paramref name="stoneId"/>. Rejects (no mutation) when the balance is insufficient — BP is
        /// never negative. Every other field/record is preserved verbatim; the revision advances once.</summary>
        public static BondPowerTransition Debit(
            CharacterProgressionAggregate character, StoneId stoneId, int amount,
            long? expectedCharacterRevision = null)
        {
            if (character == null) throw new ArgumentNullException(nameof(character));

            int current = BalanceAt(character, stoneId);
            if (expectedCharacterRevision.HasValue && expectedCharacterRevision.Value != character.Revision)
                return BondPowerTransition.Reject(BondPowerResult.StaleRevision, character, current);
            if (amount <= 0)
                return BondPowerTransition.Reject(BondPowerResult.NonPositiveAmount, character, current);
            if (amount > current)
                return BondPowerTransition.Reject(BondPowerResult.InsufficientBp, character, current);

            int newBalance = current - amount;
            var next = WithStoneBp(character, stoneId, newBalance);
            return BondPowerTransition.Accept(next, newBalance);
        }

        /// <summary>Produce the next aggregate with the Stone record at <paramref name="stoneId"/>
        /// carrying <paramref name="newBp"/> personal BP. All other records and every non-BP field of
        /// the target record are preserved verbatim; the aggregate revision advances once.</summary>
        private static CharacterProgressionAggregate WithStoneBp(
            CharacterProgressionAggregate character, StoneId stoneId, int newBp)
        {
            var records = new List<CharacterStoneRecord>(character.StoneRecords.Count + 1);
            bool found = false;
            foreach (var sr in character.StoneRecords)
            {
                if (sr.StoneId.Equals(stoneId))
                {
                    found = true;
                    records.Add(new CharacterStoneRecord(sr.StoneId, sr.PersonalAp, sr.CumulativeAp,
                        newBp, sr.FacetCredits, sr.Purchases, sr.Relationships));
                }
                else
                {
                    records.Add(sr);
                }
            }
            if (!found)
                records.Add(new CharacterStoneRecord(stoneId, 0, 0, newBp));

            return new CharacterProgressionAggregate(
                character.Account, character.Character, character.WorldProductScope,
                character.Revision + 1, character.BondSlots, character.AttunementSlots,
                character.LastAppliedReceiptId, records, character.SchemaVersion);
        }
    }
}
