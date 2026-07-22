// SBPR.QaHarness.T022.Core — engine-free QA-harness contracts + arming gate (ADR-0009).
//
// This assembly references System.* ONLY (no UnityEngine / BepInEx / HarmonyLib /
// Valheim, and no src/SBPR.* product assembly). That is what lets it (a) compile
// into the net48 helper in CI, (b) link-compile into the net8 xUnit suite that runs
// headless with no Valheim SDK, and (c) stay on the QA side of the ADR-0009 §1/§7
// dependency firewall. M1 scope = contracts + capability-manifest parser + the
// fail-closed arming/admission DECISION only. No listener, socket, RPC, Harmony,
// Unity/game mutation, fixtures/actions, deployment, or live runtime lives here.
using System;

namespace SBPR.QaHarness.T022.Core
{
    /// <summary>
    /// The process role a harness instance runs as (ADR-0009 §2). Chosen at load
    /// from the runner's <b>explicit</b> signal — never inferred. An arming manifest
    /// pins exactly one role; a request whose role does not match the armed role is
    /// rejected (<see cref="RejectReason.RoleMismatch"/>).
    /// </summary>
    public enum HarnessRole
    {
        /// <summary>Headless dedicated server (isolated, disposable world).</summary>
        Server = 1,

        /// <summary>A GUI client (primary crafter or valbot counterparty).</summary>
        Client = 2,
    }

    /// <summary>Strict parser for <see cref="HarnessRole"/> — fail-closed on anything else.</summary>
    public static class HarnessRoleParser
    {
        /// <summary>
        /// Parse an exact role token. Case-sensitive and whitespace-intolerant on
        /// purpose: the role arrives from the runner's explicit config, not free text,
        /// so a sloppy value is a bug we must not silently coerce.
        /// </summary>
        public static bool TryParse(string? token, out HarnessRole role)
        {
            switch (token)
            {
                case "Server":
                    role = HarnessRole.Server;
                    return true;
                case "Client":
                    role = HarnessRole.Client;
                    return true;
                default:
                    role = default;
                    return false;
            }
        }
    }
}
