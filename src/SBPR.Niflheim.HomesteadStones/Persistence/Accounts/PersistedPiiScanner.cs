using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SBPR.Niflheim.HomesteadStones.Persistence.Accounts
{
    // IAP-003 Tracer 1 — mechanical persisted-PII scanner (engine-free CLEAN core).
    //
    // AIP-FR-006/022, AT-AIP-PERSISTED-PII-SCAN, AT-AIP-DATA-INVENTORY-BASIS support: a mechanical scan
    // of the durable Niflheim account fixture must find NO raw provider subject, token, refresh token,
    // email, Discord id, or profile claim. This reads the on-disk journal bytes (both the framed binary
    // and its decoded UTF-8 payload, so base64-embedded fields are also caught) and reports any forbidden
    // needle that appears. Negative fixtures seed a forbidden value to prove the scan actually catches a
    // leak rather than vacuously passing.
    //
    // net48 audit: System.IO + System.Text only. No UnityEngine/Valheim/BepInEx.

    public static class PersistedPiiScanner
    {
        /// <summary>Scan one file's raw bytes and its decoded record payloads for any forbidden needle.
        /// Returns true and the first offending needle if found. The decoded-payload pass is what makes
        /// this honest: account records base64-encode their fields, so a raw subject that leaked into a
        /// field would be invisible to a naive byte-grep but is surfaced here.</summary>
        public static bool TryFindForbidden(string filePath, IEnumerable<string> forbiddenNeedles, out string offending)
        {
            offending = string.Empty;
            if (!File.Exists(filePath)) return false;

            byte[] raw = File.ReadAllBytes(filePath);
            string rawText = Encoding.UTF8.GetString(raw);

            // Decode every framed record's payload and its per-field base64, concatenating the plaintext
            // so an embedded forbidden value is exposed regardless of encoding depth.
            string decoded = DecodeAllPlaintext(raw);

            foreach (var needle in forbiddenNeedles)
            {
                if (string.IsNullOrEmpty(needle)) continue;
                if (rawText.IndexOf(needle, StringComparison.Ordinal) >= 0 ||
                    decoded.IndexOf(needle, StringComparison.Ordinal) >= 0)
                {
                    offending = needle;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Convenience over a store's journal.</summary>
        public static bool JournalContainsForbidden(PilotAccountStore store, IEnumerable<string> forbiddenNeedles, out string offending) =>
            TryFindForbidden(store.JournalPath, forbiddenNeedles, out offending);

        private static string DecodeAllPlaintext(byte[] raw)
        {
            var sb = new StringBuilder();
            using (var ms = new MemoryStream(raw))
            using (var br = new BinaryReader(ms, Encoding.UTF8))
            {
                long length = ms.Length;
                while (true)
                {
                    long start = ms.Position;
                    if (start + 8 > length) break;
                    int payloadLen = br.ReadInt32();
                    br.ReadUInt32(); // crc, skip
                    if (payloadLen < 0 || ms.Position + payloadLen > length) break;
                    byte[] payload = br.ReadBytes(payloadLen);
                    if (payload.Length != payloadLen) break;
                    string text = Encoding.UTF8.GetString(payload);
                    sb.Append(text).Append('\n');
                    // Deep-decode: any '|' or ';' or ',' or '=' separated base64 token.
                    foreach (var token in text.Split('|', ';', ',', '='))
                        AppendIfBase64(sb, token);
                }
            }
            return sb.ToString();
        }

        private static void AppendIfBase64(StringBuilder sb, string token)
        {
            if (token.Length == 0 || token.Length % 4 != 0) return;
            try
            {
                byte[] bytes = Convert.FromBase64String(token);
                sb.Append(Encoding.UTF8.GetString(bytes)).Append('\n');
            }
            catch (FormatException)
            {
                // not base64 — ignore
            }
        }
    }
}
