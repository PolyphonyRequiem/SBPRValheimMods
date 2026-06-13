// ============================================================================
//  Trailborne v2 cartography — CPU map compositor (§2E.1, issue 10)
// ----------------------------------------------------------------------------
//  Builds OUR own windowed RGBA32 cartography texture by replicating vanilla's
//  per-pixel map logic on the CPU, sampling only PUBLIC, DETERMINISTIC base-game
//  data (WorldGenerator.GetBiome / GetBiomeHeight + the Minimap biome→Color table).
//  This REPLACES the §2E GPU-material-copy path (TryRenderVanillaCartography),
//  whose dependency on vanilla's custom GPU map shader could not be verified on a
//  headless worker and shipped blind → the v0.2.22 "flat land + shroud" failure.
//
//  The compositor is a PURE FUNCTION of (biome/height sampler, window geometry,
//  palette): it has NO Unity scene/Minimap-lifecycle dependency beyond the data it
//  is handed. That is the §2E.2 design point — the SAME Compose() runs in-game
//  (fed WorldGenerator.instance + Minimap.instance colors) AND in the headless
//  preview harness (fed the real WorldGenerator inside a batch/server host), so
//  "preview == ship" by construction. The harness lives in tools/, not in the
//  shipped plugin; this file is the shared core both call.
//
//  Clean-side (ADR-0001): every pixel rule below is adapted from the base game we
//  mod (vanilla Minimap.GetPixelColor :1754-1769, GetMaskColor :1719-1752,
//  GenerateWorldMap :1639-1682, WorldGenerator.GetBiome/GetBiomeHeight — all
//  PUBLIC) — reading + adapting vanilla is explicitly fair game. No decompiled
//  IronGate source is copied; no third-party mod code is touched. Verified against
//  ~/valheim/sbpr-corpus/subsystems/Minimap.cs + decomp-index WorldGenerator.
// ============================================================================

using System;
using UnityEngine;

namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// The biome/height data source the compositor samples. In-game this wraps
    /// <c>WorldGenerator.instance</c>; the headless preview harness implements the
    /// same interface against a WorldGenerator it initialized from a seed. One
    /// interface → identical pixels in both paths (§2E.2 "preview == ship").
    /// </summary>
    public interface IBiomeSampler
    {
        /// <summary>Vanilla <c>WorldGenerator.GetBiome(wx, wy)</c> (public, deterministic from seed).</summary>
        Heightmap.Biome GetBiome(float wx, float wy);

        /// <summary>
        /// Vanilla <c>WorldGenerator.GetBiomeHeight(biome, wx, wy, out _)</c> — the world
        /// height in metres at (wx, wy). Water level is <c>height &lt; 30f</c>
        /// (ZoneSystem.c_WaterLevel = 30f; the test vanilla GetMaskColor uses at :1722).
        /// </summary>
        float GetBiomeHeight(Heightmap.Biome biome, float wx, float wy);
    }

    /// <summary>
    /// The biome→Color palette. Mirrors vanilla <c>Minimap</c>'s public color fields
    /// (<c>m_meadowsColor</c> etc., Minimap.cs:237-251). In-game we copy them off
    /// <c>Minimap.instance</c> so a future game-patch recolor is inherited; the
    /// harness uses the same literals captured from the decomp.
    /// </summary>
    public struct CartographyPalette
    {
        public Color Meadows;
        public Color AshLands;
        public Color BlackForest;
        public Color DeepNorth;
        public Color Plains;     // vanilla m_heathColor
        public Color Swamp;
        public Color Mountain;
        public Color Mistlands;
        public Color Ocean;      // vanilla: Color.white (unknown/ocean)

        /// <summary>
        /// Vanilla default palette (Minimap.cs:237-251, decomp-verified). Used by the
        /// headless harness and as the in-game fallback if Minimap.instance is null.
        /// </summary>
        public static CartographyPalette Vanilla => new CartographyPalette
        {
            Meadows     = new Color(0.45f, 1f, 0.43f),
            AshLands    = new Color(1f, 0.2f, 0.2f),
            BlackForest = new Color(0f, 0.7f, 0f),
            DeepNorth   = new Color(1f, 1f, 1f),
            Plains      = new Color(1f, 1f, 0.2f),
            Swamp       = new Color(0.6f, 0.5f, 0.5f),
            Mountain    = new Color(1f, 1f, 1f),
            Mistlands   = new Color(0.2f, 0.2f, 0.2f),
            Ocean       = Color.white,
        };

        /// <summary>
        /// Build the palette from the live <c>Minimap.instance</c> so a future game-patch
        /// recolor is inherited. The biome color fields are PUBLIC (Minimap.cs:237-249)
        /// except <c>m_mistlandsColor</c> (private, :251) — that one is read via reflection
        /// with the vanilla literal as fallback. Returns <see cref="Vanilla"/> wholesale if
        /// Minimap.instance is null (pre-Minimap-Start). Clean-side: a field read, never a
        /// SetMapMode / root toggle (AT-RENDER-NOMAP-INTACT).
        /// </summary>
        public static CartographyPalette FromMinimap()
        {
            var mm = Minimap.instance;
            if (mm == null) return Vanilla;

            var p = new CartographyPalette
            {
                Meadows     = mm.m_meadowsColor,
                AshLands    = mm.m_ashlandsColor,
                BlackForest = mm.m_blackforestColor,
                DeepNorth   = mm.m_deepnorthColor,
                Plains      = mm.m_heathColor,
                Swamp       = mm.m_swampColor,
                Mountain    = mm.m_mountainColor,
                Mistlands   = Vanilla.Mistlands, // private field; reflection-read below
                Ocean       = Color.white,
            };

            // m_mistlandsColor is private — read it reflectively so a recolor is still
            // inherited; the literal already loaded above is the graceful fallback.
            try
            {
                var f = typeof(Minimap).GetField("m_mistlandsColor",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (f != null && f.GetValue(mm) is Color c) p.Mistlands = c;
            }
            catch { /* keep the literal fallback */ }

            return p;
        }

        /// <summary>Biome → base color, replicating vanilla <c>Minimap.GetPixelColor</c> (:1754-1769).</summary>
        public Color ForBiome(Heightmap.Biome biome)
        {
            switch (biome)
            {
                case Heightmap.Biome.Meadows:     return Meadows;
                case Heightmap.Biome.AshLands:    return AshLands;
                case Heightmap.Biome.BlackForest: return BlackForest;
                case Heightmap.Biome.DeepNorth:   return DeepNorth;
                case Heightmap.Biome.Plains:      return Plains;
                case Heightmap.Biome.Swamp:       return Swamp;
                case Heightmap.Biome.Mountain:    return Mountain;
                case Heightmap.Biome.Mistlands:   return Mistlands;
                case Heightmap.Biome.Ocean:       return Ocean;
                default:                          return Ocean; // vanilla _ => Color.white
            }
        }
    }

    /// <summary>
    /// The CPU map compositor (§2E.1). Pure: given a biome/height sampler, the bound
    /// window geometry, and a palette, it produces the Size×Size RGBA32 cartography
    /// for the disc — biome color + water tone + hillshade relief. No GPU, no
    /// Minimap-lifecycle dependency. Shared by the in-game render and the §2E.2
    /// preview harness so they are byte-identical.
    /// </summary>
    public static class CartographyComposer
    {
        // Water level (metres). Vanilla GetMaskColor tests `height < 30f` (:1722);
        // ZoneSystem.c_WaterLevel = 30f (decomp members.tsv:11958). Cells below this
        // render as the water tone — the headline missing feature (AT-RENDER-WATER).
        public const float WaterLevel = 30f;

        // Deep/shallow water tones. Vanilla composites water via the GPU shader's
        // ocean handling (not a flat fill); we approximate the map's blue water with a
        // depth ramp so shoreline vs deep ocean read distinctly (AT-RENDER-WATER).
        private static readonly Color WaterDeep    = new Color(0.10f, 0.22f, 0.40f);
        private static readonly Color WaterShallow = new Color(0.21f, 0.41f, 0.58f);

        // Relief/hillshade tuning (AT-RENDER-RELIEF). We shade land cells by the height
        // delta to the north-east neighbor (a cheap directional hillshade), brightening
        // slopes that face the light and darkening those that fall away. Kept subtle so
        // biome color stays dominant (matches the vanilla map's gentle relief).
        private const float ReliefStrength   = 0.55f;  // max ± luminance swing
        private const float ReliefSlopeScale = 0.06f;  // metres of delta → full swing (per ~pixelSize step)

        /// <summary>
        /// Compose the cartography for a bounded window. The window is the SAME
        /// <see cref="BoundedMapMath.WindowSpec"/> the fog + pins use, so the output
        /// aligns cell-for-cell with the shroud mask and pin overlay by construction.
        /// Rows are bottom-up (wy=0 = southmost), matching <c>PaintFog</c> so north = up
        /// on screen with no flip.
        /// </summary>
        /// <param name="sampler">Public WorldGenerator biome/height source.</param>
        /// <param name="palette">Biome→color table (vanilla Minimap colors in-game).</param>
        /// <param name="window">The disc's over-provisioned cell window.</param>
        /// <param name="pixelSize">World metres per source cell (vanilla m_pixelSize = 64).</param>
        /// <param name="textureSize">Full-world grid edge (vanilla m_textureSize = 256).</param>
        /// <returns>Size×Size RGBA32 pixels, row-major, index = wy*Size + wx.</returns>
        public static Color32[] Compose(IBiomeSampler sampler, CartographyPalette palette,
                                        BoundedMapMath.WindowSpec window,
                                        float pixelSize, int textureSize)
        {
            int size = window.Size;
            var px = new Color32[size * size];
            if (sampler == null) return px;

            // First pass: sample biome + height per cell (height is reused for relief).
            var biomes  = new Heightmap.Biome[size * size];
            var heights = new float[size * size];
            for (int wy = 0; wy < size; wy++)
            {
                for (int wx = 0; wx < size; wx++)
                {
                    int srcX = window.OriginCellX + wx;
                    int srcY = window.OriginCellY + wy;
                    int oi = wy * size + wx;

                    float cwx = BoundedMapMath.CellCenterWorldX(srcX, pixelSize, textureSize);
                    float cwz = BoundedMapMath.CellCenterWorldZ(srcY, pixelSize, textureSize);

                    Heightmap.Biome b = sampler.GetBiome(cwx, cwz);
                    biomes[oi]  = b;
                    heights[oi] = sampler.GetBiomeHeight(b, cwx, cwz);
                }
            }

            // Second pass: colorize (biome/water) + apply hillshade from the height field.
            for (int wy = 0; wy < size; wy++)
            {
                for (int wx = 0; wx < size; wx++)
                {
                    int oi = wy * size + wx;
                    float h = heights[oi];
                    Color c;

                    if (h < WaterLevel)
                    {
                        // Depth ramp: shoreline (just below 30 m) → shallow, abyssal → deep.
                        float depth = Mathf.Clamp01((WaterLevel - h) / 60f);
                        c = Color.Lerp(WaterShallow, WaterDeep, depth);
                    }
                    else
                    {
                        c = palette.ForBiome(biomes[oi]);
                        c = ApplyRelief(c, heights, wx, wy, size);
                    }

                    px[oi] = (Color32)c;
                }
            }

            return px;
        }

        /// <summary>
        /// Directional hillshade (AT-RENDER-RELIEF): brighten a land cell when it rises
        /// toward the north-east light and darken it when it falls away. Uses the height
        /// delta to the (wx+1, wy+1) neighbor; clamps to the window edge so border cells
        /// don't read off-array. Water cells are shaded flat (handled before this call).
        /// </summary>
        private static Color ApplyRelief(Color baseColor, float[] heights, int wx, int wy, int size)
        {
            int nx = Mathf.Min(wx + 1, size - 1);
            int ny = Mathf.Min(wy + 1, size - 1);
            float here = heights[wy * size + wx];
            float neigh = heights[ny * size + nx];
            // Positive delta = neighbor higher = this cell faces away from the NE light → darker.
            float delta = neigh - here;
            float shade = Mathf.Clamp(-delta * ReliefSlopeScale, -1f, 1f) * ReliefStrength;

            float mul = 1f + shade;
            return new Color(Mathf.Clamp01(baseColor.r * mul),
                             Mathf.Clamp01(baseColor.g * mul),
                             Mathf.Clamp01(baseColor.b * mul),
                             1f);
        }
    }

    /// <summary>
    /// In-game <see cref="IBiomeSampler"/> over the live <c>WorldGenerator.instance</c>.
    /// WorldGenerator is initialized on the joining client from the server's seed
    /// (deterministic), so this is valid on a dedicated-nomap client. Clean-side: only
    /// PUBLIC WorldGenerator methods, never Minimap.SetMapMode / roots / Game.m_noMap
    /// (AT-RENDER-NOMAP-INTACT).
    /// </summary>
    public sealed class WorldGeneratorSampler : IBiomeSampler
    {
        private readonly WorldGenerator _wg;

        private WorldGeneratorSampler(WorldGenerator wg) { _wg = wg; }

        /// <summary>The live sampler, or null if worldgen isn't initialized yet
        /// (pre-join — caller falls back to PaintFog per §2E.1 graceful degradation).</summary>
        public static WorldGeneratorSampler? Live
            => WorldGenerator.instance != null ? new WorldGeneratorSampler(WorldGenerator.instance) : null;

        public Heightmap.Biome GetBiome(float wx, float wy) => _wg.GetBiome(wx, wy);

        public float GetBiomeHeight(Heightmap.Biome biome, float wx, float wy)
            => _wg.GetBiomeHeight(biome, wx, wy, out _);
    }
}
