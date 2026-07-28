using System;
using BepInEx.Logging;
using HarmonyLib;

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
    /// is written here: profile selection and the connect handoff are 100% vanilla code paths.
    /// The hook simply supplies the missing headless "Start" click. It fires at most once and
    /// re-checks the queued target each call, so a non-QA launch (no env var) or a plain
    /// character-select with no queued join is completely unaffected.
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
        /// Env var naming the absolute path of a mode-0600 file whose sole line is the lane's
        /// join password. When present, the hook reads that file and sets the vanilla
        /// <c>FejdStartup.ServerPassword</c> so the vanilla handshake auto-supplies it
        /// (<c>ZNet.RPC_ClientHandshake</c> → <c>OnPasswordEntered(FejdStartup.ServerPassword)</c>)
        /// instead of parking on the password dialog. Absent (or an open/no-password lane) =>
        /// no password is set and vanilla's <c>needPassword=false</c> branch joins directly.
        ///
        /// <para>Only the PATH rides the 0644 launch-env sidecar (non-secret, exactly like the
        /// bootstrap-doc path). The password itself lives ONLY in the mode-0600 file, never in
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
        /// Read the lane password from the mode-0600 file named by
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
                log.LogError("SBPRQA: failed to set FejdStartup.ServerPassword: " + ex);
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

                _joinDriven = true;
                _log?.LogWarning(
                    "SBPRQA: queued +connect join detected on character-select; driving vanilla " +
                    "OnCharacterStart() to select the current profile and begin the join (headless Start).");

                // Drive the EXACT vanilla path the Start button drives. OnCharacterStart selects
                // m_profiles[m_profileIndex] (populated by ShowCharacterSelection), calls
                // Game.SetProfile, and — because m_queuedJoinServer.IsValid — promotes it to
                // m_joinServer and calls JoinServer(). No re-implementation of the join.
                __instance.OnCharacterStart();
            }
            catch (Exception ex)
            {
                _log?.LogError("SBPRQA: headless auto-join failed to drive OnCharacterStart: " + ex);
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
