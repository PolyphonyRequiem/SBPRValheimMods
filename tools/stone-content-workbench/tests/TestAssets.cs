using System;
using System.IO;

namespace StoneContent.Workbench.Tests
{
    // Locates the canonical asset + schema. The .csproj copies both into Assets/ beside the test
    // assembly, so tests read them by a stable relative path with no repo-layout coupling.
    internal static class TestAssets
    {
        public static string AssetPath =>
            Path.Combine(AppContext.BaseDirectory, "Assets", "homestead-stone.content.json");

        public static string SchemaPath =>
            Path.Combine(AppContext.BaseDirectory, "Assets", "homestead-stone.content.schema.json");

        public static string AssetJson => File.ReadAllText(AssetPath);
    }
}
