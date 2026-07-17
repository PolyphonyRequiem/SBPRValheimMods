using System;
using SBPR.Niflheim.HomesteadStones.Domain.Identity;

namespace SBPR.Niflheim.HomesteadStones.Domain.ResourceDelivery
{
    // RD-T002 (Gate A) — canonical world/product-scoped Connection identity and exact maturity
    // arithmetic. Named acceptance: AT-RD-001 (canonical account-pair identity) and AT-RD-003
    // (the six approved maturity bands), per docs/v2/planning/homestead-resource-delivery-
    // {spec,data-model,contracts}.md (merged PR #327).
    //
    // WHAT THIS FILE OWNS
    //   * ProductScope — the stable authored product/runtime discriminator (data-model §Stable
    //     identities) that keeps another product sharing AccountIds out of this Connection graph.
    //   * ConnectionId — the canonical unordered (WorldId, ProductScope, lower AccountId, higher
    //     AccountId) identity (RD-001). Either input order yields the same identity; a self-pair
    //     is rejected; an unauthenticated (empty) subject is rejected.
    //   * ConnectionMaturity — the exact age→multiplier band table (RD-003). The multiplier is a
    //     rational numerator/denominator pair (e.g. 1.1× = 11/10), never a float, because
    //     data-model modeling rule 6 forbids authoritative floating-point accumulation.
    //
    // net48 audit: only System / System.String / value objects from Domain.Identity. Engine-free —
    // no UnityEngine / Valheim / BepInEx surface — so it link-compiles into the net8 test project
    // exactly like ProgressionIdentity.cs while shipping under net48.

    /// <summary>Stable authored product/runtime discriminator (data-model §Stable identities,
    /// <c>ProductScope</c>). Prevents another product that happens to share Account IDs from
    /// joining this Connection graph. Compared ordinally; never a display name.</summary>
    public readonly struct ProductScope : IEquatable<ProductScope>
    {
        public ProductScope(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));
        public string Value { get; }
        public bool Equals(ProductScope other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is ProductScope other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
    }

    /// <summary>Why a canonical Connection identity could not be formed. <see cref="Valid"/> is the
    /// only accepting outcome; every other value is a hard reject with NO identity produced.</summary>
    public enum ConnectionIdentityResolution
    {
        Valid = 0,
        SelfPair = 1,            // both accounts resolve to the same subject (RD-001 / ConnectionSelfPair)
        UnauthenticatedSubject = 2, // an empty/absent account subject (RD-001: "reject unauthenticated subjects")
        MissingScope = 3         // empty world or product scope — the graph would not be world/product-scoped
    }

    /// <summary>Canonical unordered account-pair loyalty identity =
    /// <c>(WorldId, ProductScope, lower AccountId, higher AccountId)</c> (RD-001, data-model
    /// Aggregate 1). Construction is via <see cref="TryCreate"/> only, so a self-pair or an
    /// unauthenticated subject can never mint an identity. Ordinal string comparison fixes the
    /// canonical low/high order, so <c>(A,B)</c> and <c>(B,A)</c> are the same Connection.</summary>
    public readonly struct ConnectionId : IEquatable<ConnectionId>
    {
        private ConnectionId(WorldId world, ProductScope product, AccountId lower, AccountId higher)
        {
            World = world;
            Product = product;
            AccountLow = lower;
            AccountHigh = higher;
        }

        public WorldId World { get; }
        public ProductScope Product { get; }

        /// <summary>The canonically-lower AccountId (ordinal). Never the caller's argument order.</summary>
        public AccountId AccountLow { get; }

        /// <summary>The canonically-higher AccountId (ordinal).</summary>
        public AccountId AccountHigh { get; }

        /// <summary>Resolve a canonical Connection identity from two authenticated accounts in ANY
        /// order. Returns <see cref="ConnectionIdentityResolution.Valid"/> and sets
        /// <paramref name="id"/> only for a distinct, authenticated, world/product-scoped pair;
        /// every rejection leaves <paramref name="id"/> at its default and mutates nothing.</summary>
        public static ConnectionIdentityResolution TryCreate(
            WorldId world, ProductScope product, AccountId a, AccountId b, out ConnectionId id)
        {
            id = default;

            if (string.IsNullOrEmpty(world.Value) || string.IsNullOrEmpty(product.Value))
                return ConnectionIdentityResolution.MissingScope;

            // An empty subject never came from an authenticated principal (RD-001).
            if (string.IsNullOrEmpty(a.Value) || string.IsNullOrEmpty(b.Value))
                return ConnectionIdentityResolution.UnauthenticatedSubject;

            int cmp = string.CompareOrdinal(a.Value, b.Value);
            if (cmp == 0)
                return ConnectionIdentityResolution.SelfPair;

            var (lower, higher) = cmp < 0 ? (a, b) : (b, a);
            id = new ConnectionId(world, product, lower, higher);
            return ConnectionIdentityResolution.Valid;
        }

        /// <summary>True when this identity binds <paramref name="account"/> as one of its two members.</summary>
        public bool Involves(AccountId account) =>
            AccountLow.Equals(account) || AccountHigh.Equals(account);

        /// <summary>The canonical string key. Stable across process/replay because it is derived only
        /// from the four ordinally-ordered identity components; used as a dictionary/journal key.</summary>
        public string CanonicalKey =>
            World.Value + "\u0001" + Product.Value + "\u0001" + AccountLow.Value + "\u0001" + AccountHigh.Value;

        public bool Equals(ConnectionId other) =>
            World.Equals(other.World) && Product.Equals(other.Product) &&
            AccountLow.Equals(other.AccountLow) && AccountHigh.Equals(other.AccountHigh);
        public override bool Equals(object? obj) => obj is ConnectionId other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + World.GetHashCode();
                h = h * 31 + Product.GetHashCode();
                h = h * 31 + AccountLow.GetHashCode();
                h = h * 31 + AccountHigh.GetHashCode();
                return h;
            }
        }
        public override string ToString() => "Connection(" + CanonicalKey + ")";
    }

    /// <summary>An exact rational maturity multiplier (numerator/denominator), e.g. 1.1× = 11/10.
    /// Kept rational so contribution/AP math floors once at the end without floating-point drift
    /// (data-model modeling rule 6, spec RD-009). Immutable value.</summary>
    public readonly struct MaturityMultiplier : IEquatable<MaturityMultiplier>
    {
        public MaturityMultiplier(int numerator, int denominator)
        {
            if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
            if (numerator < 0) throw new ArgumentOutOfRangeException(nameof(numerator));
            Numerator = numerator;
            Denominator = denominator;
        }

        public int Numerator { get; }
        public int Denominator { get; }

        /// <summary>Multiply <paramref name="value"/> by this rational exactly, as a numerator over the
        /// denominator. The caller floors once at the end of the full multiplication chain.</summary>
        public long ApplyNumerator(long value) => value * Numerator;

        public bool Equals(MaturityMultiplier other) =>
            Numerator == other.Numerator && Denominator == other.Denominator;
        public override bool Equals(object? obj) => obj is MaturityMultiplier other && Equals(other);
        public override int GetHashCode() => unchecked(Numerator * 397 ^ Denominator);
        public override string ToString()
        {
            // Human-readable decimal for logs/diagnostics only; never used for authoritative math.
            return (Numerator / (double)Denominator).ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture) + "x";
        }
    }

    /// <summary>Exact Connection-maturity band table (RD-003 / data-model §Derived maturity). The
    /// bands are half-open on the left boundary: an age of exactly N days enters the band whose lower
    /// bound is N. All arithmetic is in whole seconds against the durable accumulated age; there is no
    /// floating-point accumulation.</summary>
    public static class ConnectionMaturity
    {
        public const long SecondsPerDay = 86400L;

        // Band lower bounds in days (inclusive): <1, 1, 7, 30, 60, 90.
        public static readonly MaturityMultiplier Band0 = new MaturityMultiplier(10, 10); // <1d  = 1.0×
        public static readonly MaturityMultiplier Band1 = new MaturityMultiplier(11, 10); // 1–<7d = 1.1×
        public static readonly MaturityMultiplier Band2 = new MaturityMultiplier(12, 10); // 7–<30 = 1.2×
        public static readonly MaturityMultiplier Band3 = new MaturityMultiplier(13, 10); // 30–<60 = 1.3×
        public static readonly MaturityMultiplier Band4 = new MaturityMultiplier(14, 10); // 60–<90 = 1.4×
        public static readonly MaturityMultiplier Band5 = new MaturityMultiplier(15, 10); // ≥90d  = 1.5×

        /// <summary>Select the exact maturity multiplier for an accumulated connected age in seconds.
        /// Negative age is treated as zero (a clock anomaly never advances maturity; data-model
        /// Aggregate 1 invariant). The boundary is inclusive on the lower side.</summary>
        public static MaturityMultiplier ForAccumulatedSeconds(long accumulatedSeconds)
        {
            if (accumulatedSeconds < 0) accumulatedSeconds = 0;
            long days = accumulatedSeconds / SecondsPerDay; // whole elapsed days

            if (days < 1) return Band0;
            if (days < 7) return Band1;
            if (days < 30) return Band2;
            if (days < 60) return Band3;
            if (days < 90) return Band4;
            return Band5;
        }
    }
}
