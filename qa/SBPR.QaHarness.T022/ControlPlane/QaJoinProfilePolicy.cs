using System;
using System.Collections.Generic;

namespace SBPR.QaHarness.T022.Core.ControlPlane
{
    /// <summary>
    /// Engine-free profile-selection policy for the headless QA auto-join (M6-JOIN3 / B1).
    ///
    /// <para>
    /// <b>The defect this closes.</b> The first cut of the auto-join hook drove vanilla
    /// <c>FejdStartup.OnCharacterStart()</c>, which selects <c>m_profiles[m_profileIndex]</c>
    /// — the FIRST profile on disk with no QA scoping. On this host that first profile is
    /// Daniel's real character, so a QA run loaded and spawned <c>pololol.fch</c> in-world
    /// (the lane logged <c>Got character ZDOID from Pololol</c>). A human's character being
    /// reachable by a QA run must be IMPOSSIBLE, not merely unlikely.
    /// </para>
    ///
    /// <para>
    /// <b>The fix is an allowlist of ONE.</b> The runner names exactly one QA-owned profile
    /// via <c>SBPR_QA_PROFILE</c>. The hook selects that profile BY NAME (never by index,
    /// never "first available"); if it does not exist it is CREATED; if it cannot be created
    /// the join is REFUSED (fail closed). A denylist of known human names is explicitly the
    /// WRONG shape — it would rot the moment Daniel makes a new character. This policy is a
    /// pure allowlist of the single configured name, so every profile that is not that exact
    /// name — including every present and future human profile — is structurally unreachable.
    /// </para>
    ///
    /// <para>
    /// This type is engine-free (no Unity/Valheim/BepInEx types) so the property can be unit
    /// tested in the SDK-free tests-core suite: given a configured QA name and the set of
    /// existing profile filenames, it decides SELECT / CREATE / REFUSE with no game present.
    /// The C# hook applies the decision against the real vanilla profile list, then re-asserts
    /// the resolved name equals the configured name before <c>OnCharacterStart</c> fires.
    /// </para>
    /// </summary>
    public static class QaJoinProfilePolicy
    {
        /// <summary>The env var naming the single QA-owned profile the headless join may select.
        /// Absent/empty => the join is refused (there is no allowlisted QA character to load).</summary>
        public const string ProfileEnvVar = "SBPR_QA_PROFILE";

        public enum Decision
        {
            /// <summary>No QA profile was configured — refuse to join (fail closed). Never fall
            /// back to any existing (possibly human) profile.</summary>
            RefuseNoQaProfileConfigured,

            /// <summary>The configured QA profile exists — select it by name.</summary>
            SelectExisting,

            /// <summary>The configured QA profile does not exist — create it, then select it.
            /// Creation must produce exactly the configured name or the join is refused.</summary>
            CreateThenSelect,
        }

        /// <summary>
        /// Decide how to resolve the QA join profile from the configured name and the set of
        /// existing profile filenames. Pure: no game state, no side effects.
        /// </summary>
        public static Decision Resolve(string? configuredQaProfile, IEnumerable<string>? existingFilenames)
        {
            if (string.IsNullOrWhiteSpace(configuredQaProfile))
            {
                return Decision.RefuseNoQaProfileConfigured;
            }

            if (existingFilenames != null)
            {
                foreach (var name in existingFilenames)
                {
                    if (NamesMatch(name, configuredQaProfile))
                    {
                        return Decision.SelectExisting;
                    }
                }
            }

            return Decision.CreateThenSelect;
        }

        /// <summary>
        /// The FINAL guard (belt-and-braces): the profile actually resolved for the join must
        /// be exactly the configured QA name. Returns true only when they match. A human
        /// profile can never match the single configured QA name, so a false here is the
        /// signal to REFUSE the join rather than load whatever vanilla selected.
        /// </summary>
        public static bool ResolvedNameIsQaProfile(string? configuredQaProfile, string? resolvedFilename)
        {
            if (string.IsNullOrWhiteSpace(configuredQaProfile) || string.IsNullOrWhiteSpace(resolvedFilename))
            {
                return false;
            }
            return NamesMatch(resolvedFilename, configuredQaProfile);
        }

        // Vanilla profile filenames are compared case-sensitively as stored; we normalize only
        // surrounding whitespace so a sidecar-delivered value with a stray trailing newline
        // still matches. We do NOT lowercase: two distinct stored filenames differing only in
        // case are different files, and collapsing them could mask a human profile.
        private static bool NamesMatch(string? a, string? b)
        {
            return string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.Ordinal);
        }
    }
}
