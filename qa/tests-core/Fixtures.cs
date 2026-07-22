// Shared test fixtures for the ADR-0009 M1 engine-free contract suite. Builds a
// canonical VALID armed run + a valid signed request, so each test mutates exactly
// one axis to prove the specific gate that fires.
using System.Collections.Generic;
using SBPR.QaHarness.T022.Core;

namespace SBPR.QaHarness.T022.Core.Tests
{
    internal static class Fixtures
    {
        public const long DisposableUid = 9001;
        public const string DisposableName = "sbpr-qa-disposable-t022";
        public const string Nonce = "run-nonce-abc123";
        public const string Secret = "run-hmac-secret-xyz";
        public const long Now = 1_000_000_000_000;
        public const long ArmExpiry = Now + 3_600_000; // +1h

        public static WorldIdentity DisposableWorld() => new(DisposableUid, DisposableName);

        public static WorldPolicy Policy() => new(new[] { DisposableWorld() });

        public static Dictionary<string, string> ValidHashes() => new()
        {
            ["product"] = "p1", ["helper"] = "h1", ["game"] = "g1",
            ["bepinex"] = "b1", ["harmony"] = "y1", ["scenario"] = "s1",
        };

        public static HashManifest HashManifest() => new(ValidHashes());

        /// <summary>A fully valid Server-role arm manifest permitting the fixture verbs.</summary>
        public static ArmManifest ValidServerManifest(
            IReadOnlyList<string>? verbs = null)
            => new(
                enabled: true,
                roleToken: "Server",
                actor: "primary",
                world: DisposableWorld(),
                nonce: Nonce,
                expiryUnixMs: ArmExpiry,
                hashes: HashManifest(),
                permittedVerbs: verbs ?? new[] { "SpawnStation", "GrantVanillaMaterials", "Ping" },
                hmacSecret: Secret);

        /// <summary>Arm the canonical valid Server run; asserts success.</summary>
        public static ArmedState ArmValidServer(IReadOnlyList<string>? verbs = null)
        {
            var decision = ArmingGate.Evaluate(
                ValidServerManifest(verbs), DisposableWorld(), ValidHashes(), Policy(), Now);
            return decision.State!;
        }

        /// <summary>Build a correctly-signed request envelope for the armed run.</summary>
        public static RequestEnvelope SignedRequest(
            ArmedState armed,
            string verb,
            long seq,
            string requestId,
            IReadOnlyDictionary<string, object?> args,
            long? expiry = null,
            string? nonceOverride = null,
            string? roleOverride = null,
            long? worldUidOverride = null)
        {
            string nonce = nonceOverride ?? armed.Nonce;
            string role = roleOverride ?? RoleToken(armed.Role);
            long worldUid = worldUidOverride ?? armed.World.WorldUid;
            long exp = expiry ?? (Now + 60_000);
            string canonical = RequestHmac.CanonicalString(nonce, seq, exp, role, worldUid, verb, requestId);
            string hmac = RequestHmac.Compute(armed.HmacSecret, canonical);
            return new RequestEnvelope(nonce, seq, exp, hmac, role, worldUid, verb, requestId, args);
        }

        public static string RoleToken(HarnessRole role) => role == HarnessRole.Server ? "Server" : "Client";

        public static Dictionary<string, object?> SpawnArgs()
            => new() { ["prefab"] = "piece_workbench", ["posRadius"] = 2.0 };

        public static Dictionary<string, object?> GrantArgs(long qty = 5)
            => new() { ["itemId"] = "Wood", ["qty"] = qty };
    }
}
