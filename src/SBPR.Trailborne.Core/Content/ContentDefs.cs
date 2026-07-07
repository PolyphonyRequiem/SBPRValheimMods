using System.Collections.Generic;

namespace SBPR.Trailborne.Core.Content
{
    /// <summary>
    /// One resource requirement in a recipe/piece cost: a resource prefab name and an amount.
    /// The <see cref="Resource"/> string is a wire-contract identifier (GetStableHashCode on the
    /// prefab name — R3); this type CARRIES it, it does not MINT it. The shell populates these
    /// from the existing feature name constants (e.g. <c>Pigments.PigmentRedName</c>), so no new
    /// string is introduced by the registry.
    /// </summary>
    public sealed class Req
    {
        public string Resource { get; }
        public int Amount { get; }

        public Req(string resource, int amount)
        {
            Resource = resource;
            Amount = amount;
        }
    }

    /// <summary>
    /// The engine-free definition of one crafted ITEM recipe: which item is output, how many, at
    /// which crafting station, from which resources. This is the single data shape the review's
    /// Model B unifies — the same tuple the live <c>DoObjectDBWiring</c> builds against ObjectDB
    /// AND the <c>SpecCheck</c> boot guard re-declared as a manifest row. Holding it once, in the
    /// engine-free Core, lets a test assert its invariants (station resolvable, costs = locked
    /// values, no duplicate outputs) left of boot — and lets SpecCheck read the SAME list it used
    /// to duplicate, so the two can no longer drift.
    ///
    /// <para>Pure data — no ObjectDB, no Recipe, no engine type. The shell adapts a
    /// <see cref="RecipeDef"/> into a live <c>Recipe</c> at registration; the Core only describes
    /// the intended shape. <see cref="Station"/> null = no bench requirement (craftable anywhere).</para>
    /// </summary>
    public sealed class RecipeDef
    {
        /// <summary>Output item prefab name (e.g. "SBPR_SunstoneLens"). Wire-contract (R3).</summary>
        public string Item { get; }

        /// <summary>Output amount per craft.</summary>
        public int Amount { get; }

        /// <summary>Crafting-station piece prefab name, or null for "no bench requirement".</summary>
        public string? Station { get; }

        /// <summary>The resource cost. Order is not significant (SpecCheck compares as a set).</summary>
        public IReadOnlyList<Req> Resources { get; }

        public RecipeDef(string item, int amount, string? station, IReadOnlyList<Req> resources)
        {
            Item = item;
            Amount = amount;
            Station = station;
            Resources = resources;
        }
    }

    /// <summary>
    /// The engine-free definition of one buildable PIECE cost: which piece prefab, from which
    /// resources, at which station (null = Hammer-placed, no bench). The piece analogue of
    /// <see cref="RecipeDef"/> — the same drift the review names (live <c>Piece.m_resources</c>
    /// vs. the SpecCheck manifest row) unified onto one description.
    /// </summary>
    public sealed class PieceDef
    {
        /// <summary>Piece prefab name (e.g. "piece_sbpr_surveyors_table"). Wire-contract (R3).</summary>
        public string Piece { get; }

        /// <summary>Crafting-station piece prefab name, or null (Hammer-placed, no bench).</summary>
        public string? Station { get; }

        /// <summary>The build cost. Order is not significant.</summary>
        public IReadOnlyList<Req> Resources { get; }

        public PieceDef(string piece, string? station, IReadOnlyList<Req> resources)
        {
            Piece = piece;
            Station = station;
            Resources = resources;
        }
    }
}
