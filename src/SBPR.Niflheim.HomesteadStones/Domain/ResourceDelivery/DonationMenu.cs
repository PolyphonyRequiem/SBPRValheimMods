using System;
using System.Collections.Generic;
using System.Globalization;

namespace SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery
{
    // RD-T008 (Tracer 3) — the authored donation candidate pool and the owner-role-selected /
    // default-materialized Donation Menu (spec RD-018, contracts §SelectDonationMenu, data-model
    // Aggregate 3 §"Donation menu"). Named acceptance: AT-RD-018.
    //
    // WHAT THIS FILE OWNS (all PURE — no I/O, no engine surface)
    //   * DonationOption — a versioned, authored, positive item-count vector. A donation option is
    //     NEVER a client-authored item id/quantity/name (data-model: "Arbitrary client-authored item
    //     IDs, quantities, or display names reject").
    //   * DonationCandidatePool — the versioned per-Stone-Level authored pool. The Level-2 Humble pool
    //     is exactly `20 Wood`, `20 Stone`, `10 Wood + 10 Stone`, with `20 Wood` + `20 Stone` the
    //     authored default pair (spec RD-018).
    //   * DonationMenuSelection — the stable pair of exactly two distinct current options plus the
    //     authority provenance (owner-role Bond selection, or the deterministic default operation).
    //   * The pure selection/default rules that gate WHICH two options become donatable.
    //
    // AUTHORITY MODEL (convention shared with the rest of this domain): the "active Bond carrying the
    // server-authored owner role" is resolved UPSTREAM (relationship/authority provider) and handed to
    // this rule as a resolved boolean. This file never reads a relationship graph; it only enforces
    // that a selection without owner-role authority is rejected.
    //
    // net48 audit: only System / System.Collections.Generic / value objects. Engine-free — no
    // UnityEngine / Valheim / BepInEx surface — so it link-compiles into the net8 test project.

    /// <summary>Why a Donation Menu selection could not be formed. <see cref="Accepted"/> is the only
    /// accepting outcome; every other value is a hard reject that mints no selection.</summary>
    public enum DonationSelectionResolution
    {
        Accepted = 0,
        OwnerRoleRequired = 1,     // caller is not an active Bond carrying the server-authored owner role
        OptionsNotDistinct = 2,    // the two chosen options are the same option id
        OptionNotInPool = 3,       // a chosen option id / version is not in the current candidate pool
        WrongLevel = 4,            // the pool's Stone Level does not match the requested level
        StalePoolVersion = 5       // the caller's candidate-pool version is not current
    }

    /// <summary>A single authored donation option: a stable, versioned, strictly-positive item vector.
    /// Two options are the same iff their <see cref="OptionId"/> matches (ordinal); the version pins the
    /// authored content so a display/content change does not silently rebind.</summary>
    public readonly struct DonationOption : IEquatable<DonationOption>
    {
        public DonationOption(string optionId, int version, ItemVector vector)
        {
            if (string.IsNullOrEmpty(optionId)) throw new ArgumentException("Donation option id must be non-empty.");
            if (vector.KindCount == 0) throw new ArgumentException("A donation option must contain at least one item.");
            OptionId = optionId;
            Version = version;
            Vector = vector;
        }

        public string OptionId { get; }
        public int Version { get; }
        public ItemVector Vector { get; }

        /// <summary>Stable identity key = option id + authored version. Used for menu membership and the
        /// donation replay binding so a version bump is a distinct authored option.</summary>
        public string CanonicalKey =>
            OptionId + "\u0001" + Version.ToString(CultureInfo.InvariantCulture);

        public bool Equals(DonationOption other) =>
            string.Equals(OptionId, other.OptionId, StringComparison.Ordinal) && Version == other.Version;
        public override bool Equals(object? obj) => obj is DonationOption other && Equals(other);
        public override int GetHashCode() =>
            unchecked(((OptionId == null ? 0 : StringComparer.Ordinal.GetHashCode(OptionId)) * 397) ^ Version);
        public override string ToString() => "DonationOption(" + CanonicalKey + ")";
    }

    /// <summary>How a Donation Menu selection came to exist — either an authenticated owner-role Bond
    /// chose the two options, or the deterministic authored default pair materialized because upkeep was
    /// needed before any valid selection existed (contracts §SelectDonationMenu).</summary>
    public enum DonationMenuProvenance
    {
        OwnerRoleSelection = 1,
        AuthoredDefault = 2
    }

    /// <summary>The versioned per-Stone-Level authored candidate pool (data-model Aggregate 3 §Donation
    /// menu). Contains at least two distinct authored options; exposes the authored default pair.</summary>
    public sealed class DonationCandidatePool
    {
        private readonly List<DonationOption> _options;
        private readonly Dictionary<string, DonationOption> _byKey;

        public DonationCandidatePool(int stoneLevel, int poolVersion, IReadOnlyList<DonationOption> options,
            DonationOption defaultA, DonationOption defaultB)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Count < 2) throw new ArgumentException("A candidate pool must contain at least two distinct options.");

            StoneLevel = stoneLevel;
            PoolVersion = poolVersion;
            _options = new List<DonationOption>(options);
            _byKey = new Dictionary<string, DonationOption>(StringComparer.Ordinal);
            foreach (var o in _options)
            {
                if (_byKey.ContainsKey(o.CanonicalKey))
                    throw new ArgumentException("Duplicate option in candidate pool: " + o.CanonicalKey);
                _byKey[o.CanonicalKey] = o;
            }

            if (!_byKey.ContainsKey(defaultA.CanonicalKey) || !_byKey.ContainsKey(defaultB.CanonicalKey))
                throw new ArgumentException("The default pair must be members of the candidate pool.");
            if (defaultA.Equals(defaultB))
                throw new ArgumentException("The default pair must be two distinct options.");

            DefaultA = defaultA;
            DefaultB = defaultB;
        }

        public int StoneLevel { get; }
        public int PoolVersion { get; }
        public IReadOnlyList<DonationOption> Options => _options;
        public DonationOption DefaultA { get; }
        public DonationOption DefaultB { get; }

        public bool Contains(string optionId, int version) =>
            _byKey.ContainsKey(optionId + "\u0001" + version.ToString(CultureInfo.InvariantCulture));

        public bool TryGet(string optionId, int version, out DonationOption option) =>
            _byKey.TryGetValue(optionId + "\u0001" + version.ToString(CultureInfo.InvariantCulture), out option);

        /// <summary>The exact authored Level-2 Humble pool (spec RD-018): `20 Wood`, `20 Stone`,
        /// `10 Wood + 10 Stone`, default pair `20 Wood` + `20 Stone`.</summary>
        public static DonationCandidatePool Level2Humble()
        {
            var wood20 = new DonationOption("humble-20wood", 1, Vec(("Wood", 20)));
            var stone20 = new DonationOption("humble-20stone", 1, Vec(("Stone", 20)));
            var mixed = new DonationOption("humble-10wood10stone", 1, Vec(("Wood", 10), ("Stone", 10)));
            return new DonationCandidatePool(2, 1, new[] { wood20, stone20, mixed }, wood20, stone20);
        }

        private static ItemVector Vec(params (string item, long qty)[] pairs)
        {
            var d = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var p in pairs) d[p.item] = p.qty;
            return new ItemVector(d);
        }
    }

    /// <summary>The stable Donation Menu = exactly two distinct current options plus authority
    /// provenance (data-model Aggregate 3). Immutable value; a replacement only via a later accepted
    /// level/menu transition. Either selected option, when completed, satisfies weekly upkeep.</summary>
    public readonly struct DonationMenuSelection : IEquatable<DonationMenuSelection>
    {
        private DonationMenuSelection(int stoneLevel, int poolVersion, DonationOption optionA,
            DonationOption optionB, DonationMenuProvenance provenance)
        {
            StoneLevel = stoneLevel;
            PoolVersion = poolVersion;
            OptionA = optionA;
            OptionB = optionB;
            Provenance = provenance;
        }

        public int StoneLevel { get; }
        public int PoolVersion { get; }
        public DonationOption OptionA { get; }
        public DonationOption OptionB { get; }
        public DonationMenuProvenance Provenance { get; }

        public bool IsSelected => !string.IsNullOrEmpty(OptionA.OptionId);

        /// <summary>True when <paramref name="optionId"/>/<paramref name="version"/> is one of the two
        /// selected options. Only a selected option may be donated (contracts §SubmitUpkeepDonation).</summary>
        public bool Includes(string optionId, int version)
        {
            return (string.Equals(OptionA.OptionId, optionId, StringComparison.Ordinal) && OptionA.Version == version)
                || (string.Equals(OptionB.OptionId, optionId, StringComparison.Ordinal) && OptionB.Version == version);
        }

        public bool TryResolve(string optionId, int version, out DonationOption option)
        {
            if (string.Equals(OptionA.OptionId, optionId, StringComparison.Ordinal) && OptionA.Version == version)
            {
                option = OptionA;
                return true;
            }
            if (string.Equals(OptionB.OptionId, optionId, StringComparison.Ordinal) && OptionB.Version == version)
            {
                option = OptionB;
                return true;
            }
            option = default;
            return false;
        }

        /// <summary>Attempt an owner-role selection of two distinct current options from the pool
        /// (contracts §SelectDonationMenu). Every rejection mints no selection and mutates nothing.</summary>
        public static DonationSelectionResolution TrySelect(
            DonationCandidatePool pool, int requestedLevel, int requestedPoolVersion,
            string optionAId, int optionAVersion, string optionBId, int optionBVersion,
            bool hasOwnerRoleBond, out DonationMenuSelection selection)
        {
            selection = default;
            if (pool == null) throw new ArgumentNullException(nameof(pool));

            // Owner-role authority is a HARD gate: only the active Bond carrying the server-authored
            // owner role may select (spec RD-018 / data-model Aggregate 4 invariant).
            if (!hasOwnerRoleBond) return DonationSelectionResolution.OwnerRoleRequired;
            if (requestedLevel != pool.StoneLevel) return DonationSelectionResolution.WrongLevel;
            if (requestedPoolVersion != pool.PoolVersion) return DonationSelectionResolution.StalePoolVersion;

            bool sameId = string.Equals(optionAId, optionBId, StringComparison.Ordinal) && optionAVersion == optionBVersion;
            if (sameId) return DonationSelectionResolution.OptionsNotDistinct;

            if (!pool.TryGet(optionAId, optionAVersion, out var a) ||
                !pool.TryGet(optionBId, optionBVersion, out var b))
                return DonationSelectionResolution.OptionNotInPool;

            selection = new DonationMenuSelection(pool.StoneLevel, pool.PoolVersion, a, b,
                DonationMenuProvenance.OwnerRoleSelection);
            return DonationSelectionResolution.Accepted;
        }

        /// <summary>The deterministic authored default pair (contracts §SelectDonationMenu: "If upkeep is
        /// requested before a valid selection, the server MUST materialize the versioned authored default
        /// pair"). This is authored content, not a random choice, so it never needs owner-role authority.</summary>
        public static DonationMenuSelection MaterializeDefault(DonationCandidatePool pool)
        {
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            return new DonationMenuSelection(pool.StoneLevel, pool.PoolVersion, pool.DefaultA, pool.DefaultB,
                DonationMenuProvenance.AuthoredDefault);
        }

        /// <summary>Reconstruct a selection from persisted authoritative facts during rehydration. The
        /// persisted options ARE the authority (the durable journal, not the current pool), so this
        /// bypasses pool validation — used only by the coordinator's replay path.</summary>
        public static DonationMenuSelection FromPersisted(int stoneLevel, int poolVersion,
            DonationOption optionA, DonationOption optionB, DonationMenuProvenance provenance) =>
            new DonationMenuSelection(stoneLevel, poolVersion, optionA, optionB, provenance);

        public static readonly DonationMenuSelection None = default;

        public bool Equals(DonationMenuSelection other) =>
            StoneLevel == other.StoneLevel && PoolVersion == other.PoolVersion &&
            OptionA.Equals(other.OptionA) && OptionB.Equals(other.OptionB) && Provenance == other.Provenance;
        public override bool Equals(object? obj) => obj is DonationMenuSelection other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + StoneLevel;
                h = h * 31 + PoolVersion;
                h = h * 31 + OptionA.GetHashCode();
                h = h * 31 + OptionB.GetHashCode();
                h = h * 31 + (int)Provenance;
                return h;
            }
        }
    }
}
