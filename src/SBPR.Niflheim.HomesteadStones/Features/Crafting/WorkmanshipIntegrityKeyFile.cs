using System;
using System.IO;
using System.Security.Cryptography;
using SBPR.Niflheim.HomesteadStones.Domain.CharacterProgression;

namespace SBPR.Niflheim.HomesteadStones.Features.Crafting
{
    // T022 — the net48 persisted server integrity-key loader for the LIVE Workmanship issuance path.
    //
    // A Masterwork Workmanship stamp is protected by a server-held HMAC-SHA-256 token
    // (WorkmanshipIntegrityKey): a client can copy the visible property strings but cannot forge the token
    // without this secret, so a hand-edited / foreign / partial stamp fails validation and DEGRADES TO
    // VANILLA (AT-ITEM-TAMPER-DEGRADE). This helper materializes the >=256-bit secret from a server-owned
    // key file next to the progression journals, minting a fresh key on first boot and reusing it thereafter
    // so a restart validates the same already-issued stamps.
    //
    // Fail-closed direction: a missing/short/corrupt file mints a FRESH key rather than an absent/weak one.
    // A fresh key means previously-issued stamps no longer validate here (they degrade to plain items) —
    // the SAFE direction, never a trusted forgery. Mirrors PilotKeyRingFile exactly.
    //
    // File format (v1): a single line "v1|<hex-32-bytes>". Deliberately minimal; there is one key and no
    // rotation UI (an operator concern deferred past the pilot). Raw bytes are CSPRNG-generated, never logged.
    //
    // References only System.IO + System.Security.Cryptography + the engine-free codec, but lives under
    // Features/ (net48) because it performs host filesystem I/O with server-owned side effects, exactly like
    // the sibling PilotKeyRingFile. Not link-compiled into the net8 tests.
    internal static class WorkmanshipIntegrityKeyFile
    {
        private const string FileName = "workmanship-key.v1";
        private const int KeyBytes = 32; // 256 bits

        /// <summary>Load the persisted Workmanship integrity key from <paramref name="durableDirectory"/>,
        /// minting and persisting a fresh 256-bit key on first boot (or when the existing file is
        /// unreadable). Owner-only permissions are applied best-effort on POSIX hosts.</summary>
        internal static WorkmanshipIntegrityKey LoadOrCreate(string durableDirectory)
        {
            if (string.IsNullOrEmpty(durableDirectory)) throw new ArgumentNullException(nameof(durableDirectory));
            Directory.CreateDirectory(durableDirectory);
            string path = Path.Combine(durableDirectory, FileName);

            if (TryRead(path, out var bytes))
                return new WorkmanshipIntegrityKey(bytes);

            byte[] fresh = new byte[KeyBytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(fresh);
            Persist(path, fresh);
            return new WorkmanshipIntegrityKey(fresh);
        }

        private static bool TryRead(string path, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            try
            {
                if (!File.Exists(path)) return false;
                var parts = File.ReadAllText(path).Trim().Split('|');
                if (parts.Length != 2 || !string.Equals(parts[0], "v1", StringComparison.Ordinal)) return false;
                byte[] b = FromHex(parts[1]);
                if (b.Length * 8 < WorkmanshipIntegrityKey.MinKeyBits) return false;
                bytes = b;
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/Crafting] Workmanship key file unreadable; minting fresh (ignored): " + ex.Message);
                return false;
            }
        }

        private static void Persist(string path, byte[] bytes)
        {
            File.WriteAllText(path, "v1|" + ToHex(bytes));
            try { TryHardenPermissions(path); } catch { /* best effort */ }
        }

        private static void TryHardenPermissions(string path)
        {
            if (Environment.OSVersion.Platform == PlatformID.Unix ||
                Environment.OSVersion.Platform == PlatformID.MacOSX)
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("chmod", "600 \"" + path + "\"")
                    { UseShellExecute = false, CreateNoWindow = true };
                    System.Diagnostics.Process.Start(psi)?.WaitForExit(2000);
                }
                catch { /* best effort */ }
            }
        }

        private static string ToHex(byte[] bytes)
        {
            const string hex = "0123456789abcdef";
            char[] chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = hex[bytes[i] >> 4];
                chars[i * 2 + 1] = hex[bytes[i] & 0xF];
            }
            return new string(chars);
        }

        private static byte[] FromHex(string hex)
        {
            if (string.IsNullOrEmpty(hex) || (hex.Length % 2) != 0) return Array.Empty<byte>();
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }
    }
}
