// Shared helpers for the ADR-0009 M2R runtime-wiring tests: build a signed control-envelope
// JSON payload matching EnvelopeCodec's shape, reusing the M1 Fixtures armed run + HMAC so a
// payload the runtime accepts is signed exactly as the real runner would sign it.
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SBPR.QaHarness.T022.Core;

namespace SBPR.QaHarness.T022.Core.Tests
{
    internal static class WireFixtures
    {
        /// <summary>Build a correctly-signed JSON control payload for the armed run.</summary>
        public static string SignedPayload(
            ArmedState armed, string verb, long seq, string requestId,
            IReadOnlyDictionary<string, object?>? args = null,
            long? expiry = null, string? nonceOverride = null,
            string? roleOverride = null, long? worldUidOverride = null,
            string? hmacOverride = null, long connectionGeneration = 1,
            long? signedGenerationOverride = null, bool omitGeneration = false)
        {
            string nonce = nonceOverride ?? armed.Nonce;
            string role = roleOverride ?? (armed.Role == HarnessRole.Server ? "Server" : "Client");
            long worldUid = worldUidOverride ?? armed.World.WorldUid;
            long exp = expiry ?? (Fixtures.Now + 60_000);
            // The generation the HMAC is computed over may differ from the one placed on the wire
            // (signedGenerationOverride) so a test can prove a tampered/unsigned generation rejects.
            long signedGen = signedGenerationOverride ?? connectionGeneration;
            string canonical = RequestHmac.CanonicalString(nonce, seq, exp, role, worldUid, verb, requestId, signedGen);
            string hmac = hmacOverride ?? RequestHmac.Compute(armed.HmacSecret, canonical);

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"nonce\":\"").Append(nonce).Append("\",");
            sb.Append("\"seq\":").Append(seq.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"expiry\":").Append(exp.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"hmac\":\"").Append(hmac).Append("\",");
            sb.Append("\"role\":\"").Append(role).Append("\",");
            sb.Append("\"worldUid\":").Append(worldUid.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"verb\":\"").Append(verb).Append("\",");
            sb.Append("\"requestId\":\"").Append(requestId).Append("\"");
            if (!omitGeneration)
            {
                sb.Append(",\"connectionGeneration\":")
                  .Append(connectionGeneration.ToString(CultureInfo.InvariantCulture));
            }
            if (args != null && args.Count > 0)
            {
                sb.Append(",\"args\":{");
                bool first = true;
                foreach (var kv in args)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"').Append(kv.Key).Append("\":");
                    switch (kv.Value)
                    {
                        case string s: sb.Append('"').Append(s).Append('"'); break;
                        case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                        case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                        case double d: sb.Append(d.ToString(CultureInfo.InvariantCulture)); break;
                        case bool b: sb.Append(b ? "true" : "false"); break;
                        default: sb.Append("null"); break;
                    }
                }
                sb.Append('}');
            }
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>A valid Client-role armed run permitting Ping (for loopback/runtime tests).</summary>
        public static ArmedState ArmValidClient(IReadOnlyList<string>? verbs = null)
        {
            var manifest = new ArmManifest(
                enabled: true, roleToken: "Client", actor: "primary",
                world: Fixtures.DisposableWorld(), nonce: Fixtures.Nonce,
                expiryUnixMs: Fixtures.ArmExpiry, hashes: Fixtures.HashManifest(),
                permittedVerbs: verbs ?? new[] { "Ping", "ReadWorldName", "Disarm" },
                hmacSecret: Fixtures.Secret);
            var decision = ArmingGate.Evaluate(
                manifest, Fixtures.DisposableWorld(), Fixtures.ValidHashes(), Fixtures.Policy(), Fixtures.Now);
            return decision.State!;
        }
    }
}
