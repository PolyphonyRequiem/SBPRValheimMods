using System;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Domain;

namespace SBPR.Niflheim.HomesteadStones.Features.HomesteadStone
{
    /// <summary>
    /// net48 durable store for the per-world <see cref="HomesteadWorldLedger"/>. Persists the ledger as a
    /// small text sidecar under the world save directory, keyed by world identity, so a fresh-world
    /// realization FAILURE (no valid seat / manifest required / exception) survives a server restart as a
    /// terminal fact — not a session-only dictionary that a restart silently clears into a phantom retry.
    ///
    /// A sidecar file (not a global key) is deliberate: global keys are RPC-broadcast to every client on
    /// every change and are the wrong channel for a per-tick operator ledger; the world save directory is
    /// server-local, durable across restarts, and travels with the world. Failures to read/write are logged
    /// and treated as an empty ledger rather than crashing realization.
    /// </summary>
    internal static class HomesteadLedgerStore
    {
        private const string SubDirectory = "sbpr-niflheim-homestead";

        internal static HomesteadWorldLedger Load(string worldIdentity)
        {
            try
            {
                var path = PathFor(worldIdentity);
                if (path == null || !File.Exists(path))
                {
                    var empty = new HomesteadWorldLedger();
                    empty.SetWorldIdentity(worldIdentity);
                    return empty;
                }
                var serialized = File.ReadAllText(path);
                var ledger = HomesteadWorldLedger.Deserialize(worldIdentity, serialized);
                ledger.SetWorldIdentity(worldIdentity);
                return ledger;
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning(
                    $"[Niflheim/HomesteadStones] Ledger load failed for world '{worldIdentity}' ({exception.Message}); starting empty.");
                var empty = new HomesteadWorldLedger();
                empty.SetWorldIdentity(worldIdentity);
                return empty;
            }
        }

        internal static void Save(HomesteadWorldLedger ledger)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            try
            {
                var path = PathFor(ledger.WorldIdentity);
                if (path == null) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temp = path + ".tmp";
                File.WriteAllText(temp, ledger.Serialize());
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);   // atomic-ish replace so a crash mid-write cannot corrupt the ledger
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning(
                    $"[Niflheim/HomesteadStones] Ledger save failed for world '{ledger.WorldIdentity}': {exception.Message}");
            }
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
            var safe = Sanitize(worldIdentity);
            return Path.Combine(root, SubDirectory, safe + ".ledger.txt");
        }

        private static string Sanitize(string worldIdentity)
        {
            var chars = worldIdentity.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                var c = chars[i];
                if (!char.IsLetterOrDigit(c) && c != '-' && c != '_') chars[i] = '_';
            }
            return new string(chars);
        }
    }
}
