// Engine-free QA arm-bootstrap parser (ADR-0009 §2, §5.1) — M2R runtime wiring.
//
// The helper is DEFAULT-DISABLED. It only ever attempts to arm when the runner has placed
// an explicit local bootstrap document (a small JSON file whose path the runner passes via
// an env var — never inferred). This class parses that document into the pieces the arming
// gate + control channel need: the ArmManifest (fed to ArmingGate against OBSERVED world
// facts) and the per-session operator token (fed to the loopback bind policy). It performs
// NO arming decision itself — it only turns text into typed inputs; ArmingGate stays the
// single fail-closed authority.
//
// Engine-free (System.* only, reuses MiniJson): link-compiles into the xUnit suite so the
// bootstrap shape is tested headlessly, and into the net48 helper which supplies the file
// text + observed world/assembly facts.
using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core.ControlPlane
{
    /// <summary>The parsed bootstrap: an arm manifest + operator token, or a parse failure reason.</summary>
    public sealed class ArmBootstrap
    {
        public bool Ok { get; }
        public string Reason { get; }
        public ArmManifest? Manifest { get; }
        public string OperatorToken { get; }
        public int LoopbackPort { get; }

        /// <summary>The raw pinned component hashes (product/helper/game/bepinex/harmony/scenario) as parsed.</summary>
        public IReadOnlyDictionary<string, string> Hashes { get; }

        private ArmBootstrap(bool ok, string reason, ArmManifest? manifest, string token, int port,
            IReadOnlyDictionary<string, string>? hashes)
        {
            Ok = ok; Reason = reason; Manifest = manifest; OperatorToken = token ?? string.Empty; LoopbackPort = port;
            Hashes = hashes ?? new Dictionary<string, string>();
        }

        public static ArmBootstrap Fail(string reason) => new(false, reason, null, string.Empty, 0, null);
        public static ArmBootstrap Success(ArmManifest m, string token, int port, IReadOnlyDictionary<string, string> hashes)
            => new(true, "None", m, token, port, hashes);
    }

    /// <summary>Parses the runner's local bootstrap JSON into typed arm inputs. Never throws.</summary>
    public static class ArmBootstrapParser
    {
        /// <summary>
        /// Parse the bootstrap document. Expected flat shape:
        /// {enabled, role, actor, worldUid, worldName, nonce, expiry, hmacSecret, operatorToken,
        ///  loopbackPort, verbs:"A,B,C", hashes:{product,helper,game,bepinex,harmony,scenario}}.
        /// A missing/mis-typed required field fails closed (the gate then never arms).
        /// </summary>
        public static ArmBootstrap Parse(string? text)
        {
            if (!MiniJson.TryParse(text, out var o)) return ArmBootstrap.Fail("MalformedBootstrap");

            bool enabled = o.TryGetLong("enabled", out var en) ? en != 0 : GetBool(o, "enabled");
            o.TryGetString("role", out var role);
            o.TryGetString("actor", out var actor);
            o.TryGetString("worldName", out var worldName);
            o.TryGetString("nonce", out var nonce);
            o.TryGetString("hmacSecret", out var secret);
            o.TryGetString("operatorToken", out var token);
            o.TryGetString("verbs", out var verbsCsv);
            if (!o.TryGetLong("worldUid", out var worldUid)) return ArmBootstrap.Fail("MissingWorldUid");
            if (!o.TryGetLong("expiry", out var expiry)) return ArmBootstrap.Fail("MissingExpiry");
            int port = o.TryGetLong("loopbackPort", out var lp) ? (int)lp : 0;

            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            if (o.TryGetObject("hashes", out var h))
            {
                foreach (var kv in h.Scalars)
                    if (kv.Value.ScalarKind == JsonScalar.Kind.String && kv.Value.Str != null)
                        hashes[kv.Key] = kv.Value.Str;
            }

            var verbs = new List<string>();
            if (!string.IsNullOrEmpty(verbsCsv))
            {
                foreach (var v in verbsCsv.Split(','))
                {
                    string t = v.Trim();
                    if (t.Length > 0) verbs.Add(t);
                }
            }

            var manifest = new ArmManifest(
                enabled: enabled,
                roleToken: role,
                actor: actor,
                world: new WorldIdentity(worldUid, worldName),
                nonce: nonce,
                expiryUnixMs: expiry,
                hashes: new HashManifest(hashes),
                permittedVerbs: verbs,
                hmacSecret: secret);

            return ArmBootstrap.Success(manifest, token, port, hashes);
        }

        private static bool GetBool(MiniJsonObject o, string key)
        {
            // MiniJson stores a JSON bool as a Bool scalar; TryGetLong won't see it, so read raw.
            if (o.Scalars.TryGetValue(key, out var s) && s.ScalarKind == JsonScalar.Kind.Bool)
                return s.Boolean;
            return false;
        }
    }
}
