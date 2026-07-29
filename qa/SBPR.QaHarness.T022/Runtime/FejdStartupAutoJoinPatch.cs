using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using SBPR.QaHarness.T022.Core.ControlPlane;

namespace SBPR.QaHarness.T022.Runtime
{
    /// <summary>
    /// QA-only, default-disabled headless auto-join hook (M6-JOIN3).
    ///
    /// <para>
    /// <b>Problem.</b> Vanilla <c>+connect host:port</c> does NOT auto-join. On boot
    /// <c>FejdStartup.HandleStartupJoin</c> only QUEUES the target
    /// (<c>ZSteamMatchmaking.QueueServerJoin</c> → <c>m_joinData</c>). Each frame
    /// <c>FejdStartup.CheckPendingJoinRequest</c> drains that into the private
    /// <c>m_queuedJoinServer</c> and — because the server-list panel is not open —
    /// calls <c>ShowCharacterSelection()</c> and PARKS there. The queued join is only
    /// consumed when the human presses the character-select "Start" button, which drives
    /// <c>FejdStartup.OnCharacterStart()</c>: that method selects the current profile
    /// (<c>Game.SetProfile</c>), and, seeing <c>m_queuedJoinServer.IsValid</c>, promotes
    /// it to <c>m_joinServer</c> and calls <c>JoinServer()</c> → <c>TransitionToMainScene</c>,
    /// which is what actually opens the ZNet socket to the lane. Headless there is no click,
    /// so the client never emits the join (proven in kanban t_51bd009d: zero socket to the
    /// lane, lane Connections 0).
    /// </para>
    ///
    /// <para>
    /// <b>Fix.</b> When (and ONLY when) the QA join env var <c>SBPR_QA_CONNECT</c> is present,
    /// a postfix on <c>FejdStartup.ShowCharacterSelection</c> checks whether a valid queued
    /// join target exists (<c>m_queuedJoinServer.IsValid</c>) and, if so, invokes the SAME
    /// vanilla method the Start button drives — <c>OnCharacterStart()</c>. No new join logic
    /// is written here: the connect handoff is a 100% vanilla code path. The hook simply
    /// supplies the missing headless "Start" click. It fires at most once and re-checks the
    /// queued target each call, so a non-QA launch (no env var) or a plain character-select
    /// with no queued join is completely unaffected.
    /// </para>
    ///
    /// <para>
    /// <b>B1 — the QA character must be an allowlist of ONE.</b> Vanilla
    /// <c>OnCharacterStart</c> selects <c>m_profiles[m_profileIndex]</c>, which defaults to the
    /// FIRST profile on disk. On this host that is Daniel's real character, so the first cut
    /// loaded and spawned <c>pololol.fch</c> in-world (lane logged <c>Got character ZDOID from
    /// Pololol</c>). Before driving <c>OnCharacterStart</c> this hook now pins the vanilla
    /// selection to a single QA-owned profile named by <c>SBPR_QA_PROFILE</c>: it selects that
    /// profile BY NAME (via vanilla <c>SetSelectedProfile</c>), CREATES it (vanilla
    /// <c>PlayerProfile</c> + <c>Save</c>) if absent, and REFUSES the join (fail closed, no
    /// <c>OnCharacterStart</c>) if no QA profile is configured or the resolved selection is not
    /// exactly that name. There is NO fallback to any existing profile, and the shape is an
    /// allowlist of one — not a denylist of human names, which would rot the moment Daniel
    /// makes a new character. Every human profile is therefore structurally unreachable, and a
    /// final belt-and-braces assertion re-verifies the resolved filename equals the configured
    /// QA name immediately before the Start click. See <see cref="QaJoinProfilePolicy"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Clean-room.</b> AGENTS.md / ADR-0001: the clean-room firewall is around OTHER mods,
    /// NOT the base game. Reading and adapting vanilla Valheim decomp to hook the base game is
    /// explicitly allowed. This patch drives only public vanilla members
    /// (<c>ShowCharacterSelection</c>, <c>OnCharacterStart</c>) and reads one private field
    /// (<c>m_queuedJoinServer</c>) via Harmony reflection; it copies no IronGate source.
    /// </para>
    ///
    /// <para>
    /// <b>Default-disabled discipline (ADR-0009).</b> Like the rest of this QA helper the hook
    /// is inert unless the runner set <c>SBPR_QA_CONNECT</c> for THIS launch. Absent the env
    /// var it no-ops on every call — no join, no mutation.
    /// </para>
    /// </summary>
    internal static class FejdStartupAutoJoin
    {
        /// <summary>The join-target env var the QA launch-env sidecar carries (mirrors the
        /// wrapper's <c>SBPR_QA_CONNECT</c> → <c>+connect host:port</c> plumbing). Absent =>
        /// this hook stays disarmed.</summary>
        internal const string ConnectEnvVar = "SBPR_QA_CONNECT";

        /// <summary>
        /// Env var naming the absolute path of a per-run credential file whose sole line is the lane's
        /// join password. When present, the hook reads that file and sets the vanilla
        /// <c>FejdStartup.ServerPassword</c> so the vanilla handshake auto-supplies it
        /// (<c>ZNet.RPC_ClientHandshake</c> → <c>OnPasswordEntered(FejdStartup.ServerPassword)</c>)
        /// instead of parking on the password dialog. Absent (or an open/no-password lane) =>
        /// no password is set and vanilla's <c>needPassword=false</c> branch joins directly.
        ///
        /// <para>Only the PATH rides the 0644 launch-env sidecar (non-secret, exactly like the
        /// bootstrap-doc path). The password itself lives ONLY in the credential file, never in
        /// the sidecar and never logged — consistent with ADR-0009's secret-carrier discipline.</para>
        /// </summary>
        internal const string ServerPasswordFileEnvVar = "SBPR_QA_SERVER_PASSWORD_FILE";

        private static bool _installed;
        private static bool _joinDriven;
        private static ManualLogSource? _log;

        /// <summary>
        /// Arm the auto-join hook iff <c>SBPR_QA_CONNECT</c> is present in this process's
        /// environment. Safe to call more than once (idempotent). Called from
        /// <see cref="Plugin.Awake"/> BEFORE the world-load arm deferrer, because this hook
        /// must fire at the pre-join character-select screen — long before ZNet.World exists.
        /// </summary>
        internal static void TryInstall(Harmony harmony, ManualLogSource log)
        {
            if (_installed) return;

            string? connect = Environment.GetEnvironmentVariable(ConnectEnvVar);
            if (string.IsNullOrEmpty(connect))
            {
                log.LogInfo($"SBPRQA: no {ConnectEnvVar}; headless auto-join stays DISARMED.");
                return;
            }

            _log = log;
            _installed = true;

            // Supply the lane join password (if the runner provided its 0600 file) BEFORE the
            // join is driven, so vanilla's RPC_ClientHandshake finds FejdStartup.ServerPassword
            // set and auto-submits it rather than parking on the password dialog. Never logs the
            // secret — only whether a password was applied.
            TrySetServerPassword(log);

            harmony.PatchAll(typeof(FejdStartupAutoJoin));
            log.LogWarning(
                $"SBPRQA: headless auto-join ARMED ({ConnectEnvVar}={connect}). On character-select " +
                "with a queued +connect target the vanilla Start path (OnCharacterStart) will be driven once.");
        }

        /// <summary>
        /// Read the lane password from the per-run file named by
        /// <see cref="ServerPasswordFileEnvVar"/> (if set) and assign it to the vanilla
        /// <c>FejdStartup.ServerPassword</c> static property (private setter, reached via
        /// Harmony reflection). No-op if the env var is unset, the file is unreadable, or the
        /// value is empty — those all fall through to vanilla's no-password join path. The
        /// password value is never written to the log.
        /// </summary>
        private static void TrySetServerPassword(ManualLogSource log)
        {
            string? pwFile = Environment.GetEnvironmentVariable(ServerPasswordFileEnvVar);
            if (string.IsNullOrEmpty(pwFile))
            {
                log.LogInfo($"SBPRQA: no {ServerPasswordFileEnvVar}; joining with no password (open/no-password lane).");
                return;
            }

            string password;
            try { password = System.IO.File.ReadAllText(pwFile).Trim('\r', '\n', ' ', '\t'); }
            catch (Exception)
            {
                log.LogWarning($"SBPRQA: {ServerPasswordFileEnvVar} set but file unreadable; no password applied.");
                return;
            }

            if (password.Length == 0)
            {
                log.LogWarning("SBPRQA: server-password file was empty; no password applied.");
                return;
            }

            try
            {
                // FejdStartup.ServerPassword has a private setter; set it via reflection. It is a
                // static auto-property, so the backing field/setter lives on the type itself.
                Traverse.Create(typeof(FejdStartup)).Property("ServerPassword").SetValue(password);
                log.LogWarning(
                    "SBPRQA: lane join password applied to FejdStartup.ServerPassword " +
                    "(value read from the 0600 file; not logged). Handshake will auto-submit it.");
            }
            catch (Exception ex)
            {
                // Log only the exception TYPE, never `ex` in full: a reflective setter failure
                // could theoretically echo the value it was handed, and the password must never
                // reach the log. The type name is enough to diagnose a wiring failure.
                log.LogError("SBPRQA: failed to set FejdStartup.ServerPassword (" + ex.GetType().Name + ").");
            }
        }

        [HarmonyPatch(typeof(FejdStartup), "ShowCharacterSelection")]
        [HarmonyPostfix]
        private static void AfterShowCharacterSelection(FejdStartup __instance)
        {
            if (_joinDriven) return;

            try
            {
                // Read the private queued-join target vanilla stashed in CheckPendingJoinRequest.
                // Only a resolved `+connect host:port` (dedicated) leaves it .IsValid here.
                var queued = Traverse.Create(__instance).Field("m_queuedJoinServer").GetValue<ServerJoinData>();
                if (!queued.IsValid)
                {
                    // A plain character-select (e.g. OnStartGame with no queued join) — leave it be.
                    return;
                }

                // B1: pin the vanilla profile selection to the single QA-owned profile named by
                // SBPR_QA_PROFILE BEFORE driving the Start click. Refuse (fail closed) rather than
                // let vanilla load m_profiles[m_profileIndex] — which is the first (human) profile.
                if (!TryPinQaProfile(__instance))
                {
                    // Refused: a refused join is a correct outcome; loading a human's character is not.
                    // Mark driven so we do not retry and accidentally load a human profile on a later call.
                    _joinDriven = true;
                    return;
                }

                _joinDriven = true;
                _log?.LogWarning(
                    "SBPRQA: queued +connect join detected on character-select; QA profile pinned; " +
                    "driving vanilla OnCharacterStart() to begin the join (headless Start).");

                // Drive the EXACT vanilla path the Start button drives. OnCharacterStart selects
                // m_profiles[m_profileIndex] — which we have just pinned to the QA profile — calls
                // Game.SetProfile, and (because m_queuedJoinServer.IsValid) promotes it to
                // m_joinServer and calls JoinServer(). No re-implementation of the join.
                __instance.OnCharacterStart();
            }
            catch (Exception ex)
            {
                _log?.LogError("SBPRQA: headless auto-join failed to drive OnCharacterStart: " + ex);
            }
        }

        /// <summary>
        /// Resolve the vanilla profile selection to the single QA-owned profile named by
        /// <c>SBPR_QA_PROFILE</c>, returning true only when the currently-selected profile IS
        /// that QA profile (belt-and-braces final assertion). Fail closed (return false) if:
        /// no QA profile is configured, the QA profile is absent and cannot be created, or the
        /// resolved selection is not exactly the configured name. NEVER falls back to any
        /// existing profile — the correct outcome of a missing QA profile is a refused join.
        /// </summary>
        private static bool TryPinQaProfile(FejdStartup fejd)
        {
            string? qaProfile = Environment.GetEnvironmentVariable(QaJoinProfilePolicy.ProfileEnvVar);

            // Enumerate the vanilla profile list (populated by ShowCharacterSelection).
            var existing = ReadProfileFilenames(fejd);

            var decision = QaJoinProfilePolicy.Resolve(qaProfile, existing);
            switch (decision)
            {
                case QaJoinProfilePolicy.Decision.RefuseNoQaProfileConfigured:
                    _log?.LogWarning(
                        $"SBPRQA: refusing headless join — no {QaJoinProfilePolicy.ProfileEnvVar} configured. " +
                        "A QA run must name its own profile; it will NEVER load an existing (human) character. " +
                        "Refused join is a correct outcome.");
                    return false;

                case QaJoinProfilePolicy.Decision.CreateThenSelect:
                    if (!TryCreateQaProfile(qaProfile!))
                    {
                        _log?.LogWarning(
                            $"SBPRQA: refusing headless join — QA profile '{qaProfile}' is absent and could not be " +
                            "created; not falling back to any existing profile (fail closed).");
                        return false;
                    }
                    // Force the vanilla list to re-load from disk so the freshly-created profile appears.
                    Traverse.Create(fejd).Field("m_profiles").SetValue(null);
                    break;

                case QaJoinProfilePolicy.Decision.SelectExisting:
                    break;
            }

            // Select the QA profile BY NAME through the vanilla selector (sets m_profileIndex to
            // the matching profile; loads the list if null). Never by index.
            Traverse.Create(fejd).Method("SetSelectedProfile", new[] { typeof(string) }).GetValue(qaProfile);

            // FINAL GUARD (belt-and-braces): re-read what vanilla actually selected and assert it
            // is exactly the configured QA name. If SetSelectedProfile fell back to index 0 (no
            // match) this catches it and refuses — a human profile can never match the QA name.
            string? resolved = ReadSelectedProfileFilename(fejd);
            if (!QaJoinProfilePolicy.ResolvedNameIsQaProfile(qaProfile, resolved))
            {
                _log?.LogWarning(
                    $"SBPRQA: refusing headless join — resolved profile '{resolved}' is NOT the configured QA " +
                    $"profile '{qaProfile}'. Refusing rather than load a non-QA (possibly human) character.");
                return false;
            }

            _log?.LogWarning($"SBPRQA: QA profile '{resolved}' selected by name for headless join (allowlist of one).");
            return true;
        }

        /// <summary>Read the filenames of the vanilla <c>m_profiles</c> list (empty if null).</summary>
        private static List<string> ReadProfileFilenames(FejdStartup fejd)
        {
            var result = new List<string>();
            var profiles = Traverse.Create(fejd).Field("m_profiles").GetValue<List<PlayerProfile>>();
            if (profiles != null)
            {
                foreach (var p in profiles)
                {
                    if (p != null) result.Add(p.GetFilename());
                }
            }
            return result;
        }

        /// <summary>Read the filename of the currently-selected vanilla profile
        /// (<c>m_profiles[m_profileIndex]</c>), or null if the index is out of range.</summary>
        private static string? ReadSelectedProfileFilename(FejdStartup fejd)
        {
            var profiles = Traverse.Create(fejd).Field("m_profiles").GetValue<List<PlayerProfile>>();
            int index = Traverse.Create(fejd).Field("m_profileIndex").GetValue<int>();
            if (profiles == null || index < 0 || index >= profiles.Count) return null;
            return profiles[index]?.GetFilename();
        }

        /// <summary>
        /// Create the named QA profile through the vanilla creation path (<c>new PlayerProfile</c>
        /// + <c>Save</c>) so a fresh QA host with no QA character can still join without ever
        /// touching a human profile. Returns true only if the save succeeded. This is the ONLY
        /// profile this hook ever writes, and it writes exactly the configured QA name.
        /// </summary>
        private static bool TryCreateQaProfile(string qaProfile)
        {
            try
            {
                // Defence in depth: never create over an existing (possibly human) profile of the
                // same filename — if vanilla already has it, treat as present.
                if (PlayerProfile.HaveProfile(qaProfile))
                {
                    return true;
                }
                var profile = new PlayerProfile(qaProfile, FileHelpers.FileSource.Local);
                profile.SetName(qaProfile);
                bool saved = profile.Save();
                if (saved)
                {
                    _log?.LogWarning($"SBPRQA: created QA-owned profile '{qaProfile}' (vanilla PlayerProfile.Save).");
                }
                return saved;
            }
            catch (Exception ex)
            {
                _log?.LogError("SBPRQA: failed to create QA profile '" + qaProfile + "': " + ex);
                return false;
            }
        }

        /// <summary>Test/lifecycle reset hook (kept internal; mirrors the plugin's OnDestroy tidy-up).</summary>
        internal static void Reset()
        {
            _installed = false;
            _joinDriven = false;
            _log = null;
        }
    }
}
