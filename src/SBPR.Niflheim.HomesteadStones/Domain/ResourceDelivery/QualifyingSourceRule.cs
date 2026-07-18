using System;
using System.Collections.Generic;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery
{
    // RD-T004 (Tracer 1) — the PURE qualifying-loyalty-source rule (spec RD-002 / data-model
    // Aggregate 1 §Invariants "Every active source resolves to one current Bonded↔Attuned or
    // Bonded↔Bonded pair at one Stone" and "Attuned↔Attuned and all social/indirect edges are
    // invalid sources"). Named acceptance: AT-RD-002.
    //
    // WHAT THIS FILE OWNS
    //   The single authority that decides whether TWO accounts sharing a Stone form a qualifying
    //   Connection SOURCE, and derives the exact canonical ConnectionSource for that pair. It answers
    //   only, given the CURRENT authoritative relationship roles at one Stone:
    //     * do these two accounts' Stone roles form a qualifying loyalty pair? and if so,
    //     * what is the exact (ConnectionId, ConnectionSource) that pairing produces?
    //
    // THE RULE (spec RD-002, verbatim intent)
    //   A qualifying source exists between two DISTINCT accounts A and B at Stone S iff BOTH hold an
    //   ACTIVE Stone relationship (Bond or Attunement) to S and AT LEAST ONE of them is a Bond:
    //       Bonded ↔ Bonded    -> qualifies
    //       Bonded ↔ Attuned   -> qualifies
    //       Attuned ↔ Attuned  -> DOES NOT qualify
    //   Nothing else is a source. There is NO social graph: friendship, party, guild, Discord,
    //   proximity, co-presence, suggestion, and transitive (A–B–C ⇒ A–C) edges are simply never
    //   expressible here — the ONLY inputs this rule accepts are two accounts' authoritative
    //   per-Stone RELATIONSHIP roles, so an indirect edge has no way to mint a source. A self-pair
    //   (same account) never qualifies.
    //
    // This is PURE derivation over already-resolved relationship roles. It reads no Unity state, does
    // no persistence, and takes no lifecycle/grace action (that is the coordinator's job). It only
    // classifies and derives.
    //
    // net48 audit: System.Collections.Generic + value objects. Engine-free; link-compiles into net8 tests.

    /// <summary>An account's authoritative loyalty role at ONE Stone, derived from its active
    /// relationship record. Only these role classes can participate in a Connection source; a social,
    /// party, or proximity relation has no role here and therefore cannot form a source.</summary>
    public enum StoneRelationshipRole
    {
        /// <summary>No active Bond or Attunement to the Stone — never a source participant.</summary>
        None = 0,
        /// <summary>Active Attunement (no cultivation authority). Qualifies only when paired with a Bond.</summary>
        Attuned = 1,
        /// <summary>Active Bond. Qualifies when paired with either a Bond or an Attunement.</summary>
        Bonded = 2
    }

    /// <summary>One account's active relationship participation at a Stone — the ONLY qualifying input.
    /// It carries the authenticated AccountId, the exact RelationshipId that activated it, and the
    /// derived role. A participant is created only from a real, active Bond/Attunement record.</summary>
    public readonly struct StoneParticipant
    {
        public StoneParticipant(AccountId account, string relationshipId, StoneRelationshipRole role)
        {
            Account = account;
            RelationshipId = relationshipId ?? string.Empty;
            Role = role;
        }

        public AccountId Account { get; }
        public string RelationshipId { get; }
        public StoneRelationshipRole Role { get; }

        /// <summary>Map a relationship kind to the loyalty role. A released/none relationship maps to
        /// <see cref="StoneRelationshipRole.None"/> and can never be an active participant.</summary>
        public static StoneRelationshipRole RoleOf(RelationshipKind kind) =>
            kind == RelationshipKind.Bond ? StoneRelationshipRole.Bonded
            : kind == RelationshipKind.Attunement ? StoneRelationshipRole.Attuned
            : StoneRelationshipRole.None;
    }

    /// <summary>One derived qualifying source: the canonical Connection identity and the exact
    /// ConnectionSource record that a qualifying account-pair produces at a Stone.</summary>
    public readonly struct DerivedQualifyingSource
    {
        public DerivedQualifyingSource(ConnectionId connectionId, ConnectionSource source)
        {
            ConnectionId = connectionId;
            Source = source;
        }

        public ConnectionId ConnectionId { get; }
        public ConnectionSource Source { get; }
    }

    public static class QualifyingSourceRule
    {
        /// <summary>The stable source version derived pairings carry. Reconnecting the same relationship
        /// pair re-derives the SAME source id, so a within-grace reconnect resumes the frozen age rather
        /// than minting a distinct source (data-model Aggregate 1 "adding a valid source during Grace
        /// clears grace and resumes"). Provenance history lives in receipts, not the version.</summary>
        public const int DerivedSourceVersion = 1;

        /// <summary>True iff two Stone roles form a qualifying loyalty pair: both active and at least one
        /// Bond (spec RD-002). Attuned↔Attuned and any None participant do NOT qualify. This function is
        /// symmetric.</summary>
        public static bool RolesQualify(StoneRelationshipRole a, StoneRelationshipRole b)
        {
            if (a == StoneRelationshipRole.None || b == StoneRelationshipRole.None) return false;
            // At least one side must be a Bond. Bonded↔Bonded and Bonded↔Attuned qualify;
            // Attuned↔Attuned does not.
            return a == StoneRelationshipRole.Bonded || b == StoneRelationshipRole.Bonded;
        }

        /// <summary>Derive the exact ConnectionSource for a qualifying pair of participants at a Stone,
        /// or null when the pair does not qualify (same account, a None participant, or Attuned↔Attuned).
        /// The ConnectionId is canonical and unordered, so argument order never changes the result.</summary>
        public static DerivedQualifyingSource? DeriveSource(
            WorldId world, ProductScope product, StoneId stoneId,
            StoneParticipant a, StoneParticipant b, string activationProvenance)
        {
            if (!RolesQualify(a.Role, b.Role)) return null;

            // Distinct, authenticated account pair only. A self-pair or an unauthenticated subject
            // yields no identity (RD-001), so it can never mint a source.
            var resolution = ConnectionId.TryCreate(world, product, a.Account, b.Account, out var connectionId);
            if (resolution != ConnectionIdentityResolution.Valid) return null;

            var source = new ConnectionSource(stoneId, a.RelationshipId, b.RelationshipId,
                DerivedSourceVersion, activationProvenance ?? string.Empty);
            return new DerivedQualifyingSource(connectionId, source);
        }

        /// <summary>Derive every qualifying source across a Stone's full active participant roster:
        /// every unordered distinct-account pair whose roles qualify. Attuned↔Attuned, same-account, and
        /// None pairs are omitted. Deterministic in participant order for replay stability.</summary>
        public static IReadOnlyList<DerivedQualifyingSource> DeriveStoneSources(
            WorldId world, ProductScope product, StoneId stoneId,
            IReadOnlyList<StoneParticipant> participants, string activationProvenance)
        {
            var results = new List<DerivedQualifyingSource>();
            if (participants == null) return results;
            for (int i = 0; i < participants.Count; i++)
            {
                for (int j = i + 1; j < participants.Count; j++)
                {
                    var derived = DeriveSource(world, product, stoneId,
                        participants[i], participants[j], activationProvenance);
                    if (derived.HasValue) results.Add(derived.Value);
                }
            }
            return results;
        }
    }
}
