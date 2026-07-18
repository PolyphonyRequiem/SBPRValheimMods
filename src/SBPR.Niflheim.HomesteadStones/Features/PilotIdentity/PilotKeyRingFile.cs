using System;
using System.IO;
using System.Security.Cryptography;
using SBPR.Niflheim.HomesteadStones.Adapters.Identity;
using SBPR.Niflheim.HomesteadStones.Domain.Accounts;

namespace SBPR.Niflheim.HomesteadStones.Features.PilotIdentity
{
    // IAP-007W — the net48 persisted-keyring loader for the LIVE account-admission composition.
    //
    // The pilot's only durable lookup key is a full-length HMAC over a >=256-bit secret held OUTSIDE the
    // account journal (LookupKeyRing / AIP-FR-004/005). The engine-free ring takes raw key bytes; this
    // net48 helper materializes those bytes from a server-owned key file next to (but distinct from) the
    // account journal, minting a fresh 256-bit active key on first boot and reusing it thereafter so a
    // restart resolves the same HMACs. A missing/short/corrupt file fails closed by minting a fresh key —
    // never by falling back to a weak or absent key (a fresh key simply means no prior binding resolves,
    // which is the safe direction: admission re-mints; it never admits under a degraded key).
    //
    // File format (v1): a single line "v1|<active-version>|<hex-32-bytes>". Deliberately minimal; the
    // pilot has one active key and no rotation UI (rotation is an operator concern deferred past the
    // pilot). The raw key bytes are generated here with a CSPRNG and are never logged.
    //
    // References only System.IO + System.Security.Cryptography + the engine-free identity cores, but it
    // lives under Features/ (net48) because it performs host filesystem I/O with server-owned side
    // effects, exactly like the other net48 composition seams. Not link-compiled into the net8 tests.
    internal static class PilotKeyRingFile
    {
        private const string FileName = "lookup-key.v1";
        private const string ActiveVersion = "k1";
        private const int KeyBytes = 32; // 256 bits

        /// <summary>Load the persisted active lookup key from <paramref name="durableDirectory"/>, minting
        /// and persisting a fresh 256-bit key on first boot (or when the existing file is unreadable). The
        /// returned ring has exactly one active key and no previous key — pilot rotation is deferred.</summary>
        internal static LookupKeyRing LoadOrCreate(string durableDirectory)
        {
            if (string.IsNullOrEmpty(durableDirectory)) throw new ArgumentNullException(nameof(durableDirectory));
            Directory.CreateDirectory(durableDirectory);
            string path = Path.Combine(durableDirectory, FileName);

            if (TryRead(path, out var version, out var bytes))
                return new LookupKeyRing(new LookupHmacKey(new LookupKeyVersion(version), bytes));

            byte[] fresh = new byte[KeyBytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(fresh);
            Persist(path, ActiveVersion, fresh);
            return new LookupKeyRing(new LookupHmacKey(new LookupKeyVersion(ActiveVersion), fresh));
        }

        private static bool TryRead(string path, out string version, out byte[] bytes)
        {
            version = string.Empty;
            bytes = Array.Empty<byte>();
            try
            {
                if (!File.Exists(path)) return false;
                var parts = File.ReadAllText(path).Trim().Split('|');
                if (parts.Length != 3 || !string.Equals(parts[0], "v1", StringComparison.Ordinal)) return false;
                byte[] b = FromHex(parts[2]);
                if (b.Length * 8 < LookupHmacKey.MinKeyBits) return false;
                version = parts[1];
                bytes = b;
                return !string.IsNullOrEmpty(version);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[Niflheim/HomesteadStones] Lookup key file unreadable; minting fresh (ignored): " + ex.Message);
                return false;
            }
        }

        private static void Persist(string path, string version, byte[] bytes)
        {
            File.WriteAllText(path, "v1|" + version + "|" + ToHex(bytes));
            try { TryHardenPermissions(path); } catch { /* best effort */ }
        }

        private static void TryHardenPermissions(string path)
        {
            // Best-effort owner-only permission on POSIX hosts (the dedicated server runtime). No-op where
            // chmod is unavailable; the file already lives under the server-owned config root.
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
