using System;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Domain;

namespace SBPR.Niflheim.HomesteadStones.Features.HomesteadStone
{
    /// <summary>
    /// net48 adapter: loads and RELOADS the operational manifest (R6 Blocker 6) from a configured file under
    /// the world save directory. The parse/validate logic is engine-free (<see cref="HomesteadOperationalManifest"/>);
    /// this adapter only supplies the raw text and detects when the file changed so the placement loop can
    /// pick up a new generation without a restart.
    ///
    /// The file is operator/admin-supplied and lives server-side in the world save directory — it is NOT a
    /// channel an ordinary player can write to (no RPC, no client path). A missing file is a genuine empty
    /// manifest (generator hosts stay ManifestRequired until an operator supplies one); an unreadable file is
    /// logged and treated as empty for THIS load (the placement loop simply keeps ManifestRequired, which is
    /// retryable, rather than failing the whole realization loop for a purely-generator concern).
    /// </summary>
    internal static class HomesteadManifestStore
    {
        private const string SubDirectory = "sbpr-niflheim-homestead";
        private const string FileName = "generator-manifest.txt";

        private static string? lastPath;
        private static long lastWriteTicks = -1;
        private static long lastLength = -1;
        private static HomesteadOperationalManifest cached = HomesteadOperationalManifest.Empty;

        /// <summary>Load (or reload if the file changed since last call) the operational manifest for a world.
        /// Returns the cached instance when the file is unchanged so the common per-tick call is cheap.</summary>
        internal static HomesteadOperationalManifest LoadOrReload(string worldIdentity, string selectorVersion)
        {
            var path = PathFor(worldIdentity);
            if (path == null) return HomesteadOperationalManifest.Empty;

            try
            {
                if (!File.Exists(path))
                {
                    if (!cached.IsEmpty || lastPath != path)
                    {
                        cached = HomesteadOperationalManifest.Empty;
                        lastPath = path;
                        lastWriteTicks = -1;
                        lastLength = -1;
                    }
                    return cached;
                }

                var info = new FileInfo(path);
                var writeTicks = info.LastWriteTimeUtc.Ticks;
                var length = info.Length;
                if (path == lastPath && writeTicks == lastWriteTicks && length == lastLength)
                    return cached;   // unchanged since last load — reuse cache

                var text = File.ReadAllText(path);
                var parsed = HomesteadOperationalManifest.Parse(text, worldIdentity, selectorVersion);
                cached = parsed;
                lastPath = path;
                lastWriteTicks = writeTicks;
                lastLength = length;

                if (parsed.IsEmpty)
                    Plugin.Log.LogWarning(
                        $"[Niflheim/HomesteadStones] Operational manifest at '{path}' present but yields NO valid rows "
                        + "(scope/provenance mismatch or all rows rejected); generator hosts remain ManifestRequired.");
                else
                    Plugin.Log.LogInfo(
                        $"[Niflheim/HomesteadStones] Operational manifest loaded: generation={parsed.Generation} "
                        + $"provider='{parsed.ProviderVersion}' rows={parsed.Count} rejected={parsed.RejectedRows.Count} "
                        + $"digest={parsed.DocumentDigest}.");
                return cached;
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning(
                    $"[Niflheim/HomesteadStones] Operational manifest load failed for world '{worldIdentity}' "
                    + $"({exception.Message}); treating as empty (generator hosts stay retryable).");
                cached = HomesteadOperationalManifest.Empty;
                lastPath = path;
                lastWriteTicks = -1;
                lastLength = -1;
                return cached;
            }
        }

        /// <summary>Reset cached state — used by tests and on world unload so a new world reparses cleanly.</summary>
        internal static void Reset()
        {
            lastPath = null;
            lastWriteTicks = -1;
            lastLength = -1;
            cached = HomesteadOperationalManifest.Empty;
        }

        private static string? PathFor(string worldIdentity)
        {
            if (string.IsNullOrEmpty(worldIdentity)) return null;
            string root;
            try
            {
                root = World.GetWorldSavePath();
            }
            catch
            {
                return null;
            }
            if (string.IsNullOrEmpty(root)) return null;
            return Path.Combine(root, SubDirectory, FileName);
        }
    }
}
