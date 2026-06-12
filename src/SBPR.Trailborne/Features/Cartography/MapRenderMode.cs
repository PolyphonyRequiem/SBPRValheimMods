namespace SBPR.Trailborne.Features.Cartography
{
    /// <summary>
    /// Which cartography render path the bounded Local Map viewer uses (§2E.3 two-mode toggle).
    ///
    /// <para><b>Why two modes.</b> Daniel's requirement is the *vanilla parchment look*
    /// (paper texture + cloud/haze + fog feathering), which lives in vanilla's GPU map
    /// DISPLAY SHADER on <c>Minimap.m_mapImageLarge.material</c> — NOT in the four data
    /// textures it composites. A CPU re-color (the <see cref="MapRenderMode.Cpu"/> path,
    /// shipped in §2E.1) reproduces only <c>_MainTex</c> (flat biome fills) and therefore
    /// can never show the parchment styling. The <see cref="MapRenderMode.Shader"/> path
    /// reuses a COPY of vanilla's styled material, framed to our 1000&#160;m window.</para>
    ///
    /// <para>The shader render CANNOT be verified on this project's headless build box
    /// (no Valheim client, no map shader present), so rather than ship one blind, BOTH
    /// paths are built and Daniel picks the one that looks right on his GPU via the
    /// <c>sbpr_mapmode</c> console command (<see cref="MapModeCommand"/>). Default is
    /// <see cref="MapRenderMode.Shader"/>: the first thing Daniel sees is the parchment
    /// attempt, and if the styled material isn't available it silently falls back to
    /// <see cref="MapRenderMode.Cpu"/>, then to the 2-color <c>PaintFog</c> last resort.</para>
    /// </summary>
    public enum MapRenderMode
    {
        /// <summary>Reuse a COPY of vanilla's styled large-map material (the parchment look),
        /// framed to the bound 1000&#160;m window. Falls back to <see cref="Cpu"/> if the
        /// material/textures aren't generated (e.g. headless, or pre-join).</summary>
        Shader,

        /// <summary>CPU composite from public <c>WorldGenerator</c> data (biome color + water +
        /// hillshade). Always renderable (no GPU shader dependency), but no parchment styling.</summary>
        Cpu,
    }

    /// <summary>
    /// Process-wide selected <see cref="MapRenderMode"/>. Seeded from the BepInEx config at
    /// <c>Plugin.Awake</c> (<c>SBPR_CartographyRenderMode</c>) and overridable live, without a
    /// relog, by the <c>sbpr_mapmode</c> console command. Read by
    /// <c>MapViewer.Render</c> each draw.
    /// </summary>
    public static class MapRenderModeState
    {
        /// <summary>The active render mode. Defaults to <see cref="MapRenderMode.Shader"/> so the
        /// player's first look is the parchment attempt (auto-fallback if unavailable).</summary>
        public static MapRenderMode Current { get; set; } = MapRenderMode.Shader;
    }
}
