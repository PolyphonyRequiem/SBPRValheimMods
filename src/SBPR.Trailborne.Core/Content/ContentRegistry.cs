using System.Collections.Generic;

namespace SBPR.Trailborne.Core.Content
{
    /// <summary>
    /// The single source of truth for SBPR's declarative recipe/piece manifest (arch review
    /// Model B). A shell builder populates it once from the existing feature name+cost constants
    /// (no new strings — R3), and BOTH consumers read the same list:
    ///
    /// <list type="bullet">
    ///   <item><b>SpecCheck</b> (shell boot guard) walks the live ObjectDB and compares each row
    ///     against THIS registry instead of a hand-copied manifest array — so the boot check and
    ///     the intended shape can no longer drift (they're the same list).</item>
    ///   <item><b>Tests</b> (engine-free, left of boot) assert the registry's own invariants:
    ///     no duplicate outputs, every recipe names a resource, costs equal their locked values.</item>
    /// </list>
    ///
    /// <para>Pure data + lookups — no engine. Immutable once built (the lists are copied in), so a
    /// consumer can't mutate the shared source. This is deliberately ONLY the ~14 declarative rows
    /// the review identified as duplicated between the live wiring and SpecCheck's Manifest; the
    /// generated loops (cairn colours, marker signs) and the asset-renderability checks stay
    /// shell-side (they need the live engine or are procedural, not declarative rows).</para>
    /// </summary>
    public sealed class ContentRegistry
    {
        private readonly List<RecipeDef> recipes;
        private readonly List<PieceDef> pieces;

        public ContentRegistry(IEnumerable<RecipeDef> recipes, IEnumerable<PieceDef> pieces)
        {
            this.recipes = new List<RecipeDef>(recipes);
            this.pieces = new List<PieceDef>(pieces);
        }

        /// <summary>All item recipes, in declaration order.</summary>
        public IReadOnlyList<RecipeDef> Recipes => this.recipes;

        /// <summary>All build pieces, in declaration order.</summary>
        public IReadOnlyList<PieceDef> Pieces => this.pieces;

        /// <summary>Find the recipe whose output item matches <paramref name="itemName"/>, or null.</summary>
        public RecipeDef? RecipeForItem(string itemName)
        {
            foreach (var r in this.recipes)
                if (r.Item == itemName) return r;
            return null;
        }

        /// <summary>Find the piece def for <paramref name="pieceName"/>, or null.</summary>
        public PieceDef? PieceForName(string pieceName)
        {
            foreach (var p in this.pieces)
                if (p.Piece == pieceName) return p;
            return null;
        }

        /// <summary>The distinct set of crafting-station names referenced by any recipe/piece
        /// (excluding null = "no bench"). A test asserts each of these is a real SBPR station.</summary>
        public IReadOnlyCollection<string> ReferencedStations()
        {
            var set = new HashSet<string>();
            foreach (var r in this.recipes) if (r.Station != null) set.Add(r.Station);
            foreach (var p in this.pieces) if (p.Station != null) set.Add(p.Station);
            return set;
        }

        /// <summary>Output names (recipe items + pieces) that appear more than once — the registry's
        /// own duplicate-output invariant. Empty = healthy. A dup means two defs claim the same
        /// prefab, which would double-register at boot.</summary>
        public IReadOnlyList<string> DuplicateOutputs()
        {
            var seen = new HashSet<string>();
            var dups = new List<string>();
            void Check(string name)
            {
                if (!seen.Add(name) && !dups.Contains(name)) dups.Add(name);
            }
            foreach (var r in this.recipes) Check(r.Item);
            foreach (var p in this.pieces) Check(p.Piece);
            return dups;
        }
    }
}
