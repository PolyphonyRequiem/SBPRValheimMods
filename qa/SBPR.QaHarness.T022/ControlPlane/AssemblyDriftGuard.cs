// Assembly / MVID drift guard (ADR-0009 §5.1, §8; PR #408 VANILLA-BINDINGS.md §1) — M2R.
//
// The arming gate already refuses on a drifted immutable hash manifest (ArmingGate + the
// runner-supplied product/helper/game/BepInEx/Harmony/scenario hashes). This guard is the
// complementary BINDING-MAP pin: it checks the ACTUAL loaded assembly_valheim module
// identity (MVID / version constants) against the exact values PR #408 pinned for the two
// authorized 0.221.12 builds. If the helper is loaded against a Valheim build whose seams
// may have moved, the guard fails CLOSED so a stale helper can never drive a moved seam.
//
// Engine-free (System.* only): the caller (the engine-bound helper) reads the live module's
// MVID + version constants via reflection and passes them here as plain values, so the pin
// table and its comparison logic link-compile into the headless xUnit suite and are tested
// without a game.
using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core.ControlPlane
{
    /// <summary>The observed identity of the live assembly_valheim module, supplied by the engine-bound caller.</summary>
    public sealed class ObservedGameAssembly
    {
        /// <summary>The module MVID (Guid) of the loaded assembly_valheim.dll.</summary>
        public Guid Mvid { get; }
        /// <summary>The game version string (e.g. "0.221.12").</summary>
        public string GameVersion { get; }
        /// <summary>The network protocol version constant (Version.m_networkVersion).</summary>
        public uint NetworkVersion { get; }

        public ObservedGameAssembly(Guid mvid, string gameVersion, uint networkVersion)
        {
            Mvid = mvid;
            GameVersion = gameVersion ?? string.Empty;
            NetworkVersion = networkVersion;
        }
    }

    /// <summary>An authorized build pin (one row of PR #408 §1).</summary>
    public sealed class GameAssemblyPin
    {
        public string Label { get; }
        public Guid Mvid { get; }
        public string GameVersion { get; }
        public uint NetworkVersion { get; }

        public GameAssemblyPin(string label, string mvid, string gameVersion, uint networkVersion)
        {
            Label = label;
            Mvid = Guid.Parse(mvid);
            GameVersion = gameVersion;
            NetworkVersion = networkVersion;
        }
    }

    /// <summary>The outcome of the drift check.</summary>
    public sealed class DriftCheck
    {
        public bool Ok { get; }
        public string Reason { get; }
        public string? MatchedLabel { get; }

        private DriftCheck(bool ok, string reason, string? label)
        {
            Ok = ok; Reason = reason; MatchedLabel = label;
        }

        public static DriftCheck Pass(string label) => new(true, "None", label);
        public static DriftCheck Fail(string reason) => new(false, reason, null);
    }

    /// <summary>
    /// Fail-closed guard: the observed assembly_valheim identity must EXACTLY match one of the
    /// PR #408-pinned authorized builds (MVID + version + network version all agree). Anything
    /// else is drift → refuse to arm.
    /// </summary>
    public static class AssemblyDriftGuard
    {
        // The two authorized 0.221.12 builds pinned in PR #408 VANILLA-BINDINGS.md §1.
        // MVID is the byte-identity axis; game/network version guard against a same-MVID
        // reissue. SHA-256 is recorded in the doc for provenance but the runtime cannot
        // cheaply hash a loaded module, so the runtime pin is MVID + version constants.
        //
        // Vanilla `Version.GetVersionString()` prepends a PLATFORM prefix from
        // `Version.GetPlatformPrefix()` — `"l"` on SteamLinux, `"dl"`/`"dw"`/`"ms"` elsewhere —
        // so the same MVID reports `l-0.221.12` at runtime on the Linux dedicated build and the
        // Linux GUI client. We do NOT strip the prefix inside a fail-closed gate (that would
        // silently widen acceptance to e.g. `l-0.999.0`); instead each observed platform-flavored
        // string gets its own explicit, ordinal-exact pin under the same authorized MVID.
        public static readonly IReadOnlyList<GameAssemblyPin> AuthorizedPins = new[]
        {
            new GameAssemblyPin(
                "client-trailborne-modded-gui",
                "23db560f-3f87-4454-8fe1-c434da4f936a", "0.221.12", 36u),
            new GameAssemblyPin(
                "server-dedicated-niflheim-dl",
                "62393fbd-383b-447c-9ae7-7ae16afa654f", "0.221.12", 36u),
            // Linux-flavored runtime version strings (GetVersionString() → "l-" prefix on SteamLinux).
            new GameAssemblyPin(
                "server-dedicated-niflheim-dl-linux",
                "62393fbd-383b-447c-9ae7-7ae16afa654f", "l-0.221.12", 36u),
        };

        /// <summary>Check an observed game assembly against the authorized pins. Fail-closed on any mismatch.</summary>
        public static DriftCheck Check(ObservedGameAssembly? observed)
        {
            if (observed == null) return DriftCheck.Fail("ObservedAssemblyNull");
            bool mvidMatched = false;
            string failReason = "MvidNotAuthorized";
            foreach (var pin in AuthorizedPins)
            {
                if (pin.Mvid != observed.Mvid) continue;
                // MVID matched — every other axis must agree or it is a tampered/reissued build.
                // A single MVID may carry several authorized rows (one per platform-flavored
                // version string), so keep scanning after a version/net mismatch instead of
                // failing on the first row; only fail once all pins are exhausted.
                mvidMatched = true;
                if (!string.Equals(pin.GameVersion, observed.GameVersion, StringComparison.Ordinal))
                {
                    // Version mismatch is the weakest same-MVID failure: only record it if no
                    // stronger reason (a version-matched net drift) has been seen on a sibling row.
                    if (failReason == "MvidNotAuthorized") failReason = "GameVersionDrift";
                    continue;
                }
                if (pin.NetworkVersion != observed.NetworkVersion)
                {
                    // Version matched but net drifted — the most specific same-MVID failure.
                    // It outranks any GameVersionDrift recorded from a differently-flavored row.
                    failReason = "NetworkVersionDrift";
                    continue;
                }
                return DriftCheck.Pass(pin.Label);
            }
            return DriftCheck.Fail(mvidMatched ? failReason : "MvidNotAuthorized");
        }
    }
}
