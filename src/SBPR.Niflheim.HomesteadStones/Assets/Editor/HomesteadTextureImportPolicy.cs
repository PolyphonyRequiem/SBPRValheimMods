using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Single source of truth for the Homestead Stone "Valheim pixel look" texture
/// import policy (Daniel, 2026-07-15: "Use these settings, 256-512, point no
/// filter for the Valheim pixel look.").
///
/// The importer settings are pinned here as named data so the whole policy —
/// including an A/B of the max-size cap — can be tuned in ONE place without
/// hunting through builder code. Both the repository builder and the mirrored
/// preview-lab builder call <see cref="Apply"/>, and both call
/// <see cref="AssertMatches"/> so the checked-in bundle can never silently drift
/// from this policy.
///
/// To A/B a 256 cap against the current 512 cap, change <see cref="MaxTextureSize"/>
/// only. Everything else stays put.
/// </summary>
public static class HomesteadTextureImportPolicy
{
    // --- Named policy data (the ONLY knobs; A/B the cap here) --------------
    public const TextureImporterType TextureType = TextureImporterType.Default;
    public const TextureImporterShape TextureShape = TextureImporterShape.Texture2D;
    public const FilterMode Filter = FilterMode.Point;      // "Point (no filter)"
    public const int MaxTextureSize = 512;                  // authored inputs are 512; A/B 256 here
    public const TextureWrapMode Wrap = TextureWrapMode.Repeat;
    public const int AnisoLevel = 1;
    public const bool MipmapEnabled = true;
    public const TextureImporterAlphaSource AlphaSource = TextureImporterAlphaSource.None;
    public const TextureImporterCompression Compression = TextureImporterCompression.CompressedLQ; // low-quality
    public const bool CrunchedCompression = true;
    public const int CompressionQuality = 100;              // crunch quality

    /// <summary>Apply the full policy to an importer. Caller must SaveAndReimport.</summary>
    public static void Apply(TextureImporter importer, bool srgb)
    {
        if (importer == null) throw new ArgumentNullException(nameof(importer));

        importer.textureType = TextureType;
        importer.textureShape = TextureShape;
        importer.sRGBTexture = srgb;
        importer.filterMode = Filter;
        importer.wrapMode = Wrap;
        importer.anisoLevel = AnisoLevel;
        importer.mipmapEnabled = MipmapEnabled;
        importer.alphaSource = AlphaSource;
        importer.maxTextureSize = MaxTextureSize;

        // Automatic platform format, low-quality compression, crunch @ 100.
        var defaults = importer.GetDefaultPlatformTextureSettings();
        defaults.maxTextureSize = MaxTextureSize;
        defaults.format = TextureImporterFormat.Automatic;
        defaults.textureCompression = Compression;
        defaults.crunchedCompression = CrunchedCompression;
        defaults.compressionQuality = CompressionQuality;
        importer.SetPlatformTextureSettings(defaults);

        // Also mirror onto the top-level importer fields so the Inspector's
        // "Default" tab reflects the same values (Unity keeps both in sync on
        // reimport, but set explicitly for determinism).
        importer.textureCompression = Compression;
        importer.crunchedCompression = CrunchedCompression;
        importer.compressionQuality = CompressionQuality;
    }

    /// <summary>
    /// Throws if the imported asset at <paramref name="path"/> does not match this
    /// policy. Fails the build reproducibly on any importer drift.
    /// </summary>
    public static void AssertMatches(string path, bool srgb)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null)
            throw new InvalidOperationException($"No TextureImporter at '{path}'.");

        void Check(string field, object expected, object actual)
        {
            if (!Equals(expected, actual))
                throw new InvalidOperationException(
                    $"Homestead texture import drift at '{path}': {field} expected '{expected}' but was '{actual}'.");
        }

        Check("textureType", TextureType, importer.textureType);
        Check("textureShape", TextureShape, importer.textureShape);
        Check("sRGBTexture", srgb, importer.sRGBTexture);
        Check("filterMode", Filter, importer.filterMode);
        Check("wrapMode", Wrap, importer.wrapMode);
        Check("anisoLevel", AnisoLevel, importer.anisoLevel);
        Check("mipmapEnabled", MipmapEnabled, importer.mipmapEnabled);
        Check("alphaSource", AlphaSource, importer.alphaSource);
        Check("maxTextureSize", MaxTextureSize, importer.maxTextureSize);

        var defaults = importer.GetDefaultPlatformTextureSettings();
        Check("platform.maxTextureSize", MaxTextureSize, defaults.maxTextureSize);
        Check("platform.textureCompression", Compression, defaults.textureCompression);
        Check("platform.crunchedCompression", CrunchedCompression, defaults.crunchedCompression);
        Check("platform.compressionQuality", CompressionQuality, defaults.compressionQuality);
    }
}
