using System;
using System.IO;
using SBPR.Niflheim.HomesteadStones.Domain;

namespace SBPR.Niflheim.HomesteadStones.Features.HomesteadStone
{
    /// <summary>
    /// net48 durable store for the per-world <see cref="HomesteadWorldLedger"/>. Persists the ledger as a
    /// crash-safe text sidecar under the world save directory, keyed by world identity.
    ///
    /// R6 (Blocker 5): this adapter only resolves the world-scoped paths and maps engine-free IO failures to
    /// the fail-closed <see cref="LedgerIoException"/> the placement loop understands. The atomic write +
    /// crash/corruption recovery contract lives in the engine-free <see cref="HomesteadLedgerAtomicIo"/> so it
    /// is fully unit-tested headless. The ledger records provenance, never creation truth (persisted Stone
    /// ZDOs are the sole source of "a Stone exists").
    /// </summary>
    internal static class HomesteadLedgerStore
    {
        private const string SubDirectory = "sbpr-niflheim-homestead";
        private const string LiveSuffix = ".ledger.txt";
        private const string TempSuffix = ".ledger.txt.tmp";
        private const string BackupSuffix = ".ledger.txt.bak";

        /// <summary>Fail-closed signal: the ledger could not be made durable, or a present ledger is corrupt
        /// with no valid temp/backup. The realization loop halts this tick rather than proceeding on a lost or
        /// fabricated history.</summary>
        internal sealed class LedgerIoException : Exception
        {
            internal LedgerIoException(string message, Exception inner) : base(message, inner) { }
        }

        internal static HomesteadWorldLedger Load(string worldIdentity)
        {
            var live = PathFor(worldIdentity, LiveSuffix);
            if (live == null)
            {
                // R7 (Blocker 2): a world save path that cannot be resolved is NOT an empty ledger — returning
                // empty here would fabricate a clean history and phantom-retry every terminal zone. Fail closed
                // so the realization loop halts for this world until the path resolves.
                Plugin.Log.LogError(
                    $"[Niflheim/HomesteadStones] Ledger path unresolved for world '{worldIdentity}'; failing closed "
                    + "(refusing to fabricate empty history).");
                throw new LedgerIoException(
                    $"Cannot resolve a durable ledger path for world '{worldIdentity}'.",
                    new IOException("no world save path"));
            }
            var temp = PathFor(worldIdentity, TempSuffix)!;
            var backup = PathFor(worldIdentity, BackupSuffix)!;
            try
            {
                return HomesteadLedgerAtomicIo.LoadWithRecovery(worldIdentity, live, temp, backup);
            }
            catch (HomesteadLedgerIoException exception)
            {
                Plugin.Log.LogError(
                    $"[Niflheim/HomesteadStones] Ledger load fail-closed for world '{worldIdentity}': {exception.Message}");
                throw new LedgerIoException(exception.Message, exception);
            }
        }

        internal static void Save(HomesteadWorldLedger ledger)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            var live = PathFor(ledger.WorldIdentity, LiveSuffix);
            if (live == null)
                throw new LedgerIoException(
                    $"Cannot resolve a durable ledger path for world '{ledger.WorldIdentity}'.",
                    new IOException("no world save path"));
            var temp = PathFor(ledger.WorldIdentity, TempSuffix)!;
            var backup = PathFor(ledger.WorldIdentity, BackupSuffix)!;
            try
            {
                HomesteadLedgerAtomicIo.WriteAtomic(live, temp, backup, ledger.Serialize());
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError(
                    $"[Niflheim/HomesteadStones] Ledger save FAILED for world '{ledger.WorldIdentity}' "
                    + $"({exception.Message}); realization aborts this tick (fail-closed).");
                throw new LedgerIoException(
                    $"Ledger save failed for world '{ledger.WorldIdentity}'.", exception);
            }
        }

        private static string? PathFor(string worldIdentity, string suffix)
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
            return Path.Combine(root, SubDirectory, safe + suffix);
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
