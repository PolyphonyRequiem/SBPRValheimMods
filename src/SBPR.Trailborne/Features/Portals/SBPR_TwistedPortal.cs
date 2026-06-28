using System.Collections.Generic;
using UnityEngine;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Portals
{
    /// <summary>
    /// The Twisted Portal's per-instance brain (spec §4). A DISTINCT class that does NOT inherit
    /// vanilla <see cref="TeleportWorld"/> (card AC#1 — tag-collision avoidance). One MonoBehaviour
    /// is the teleporter AND the rune-name ZDO owner (the spec §3 fold-in: the Twisted Portal is
    /// already a per-instance MonoBehaviour, so a separate <c>…Tag</c> would be redundant).
    ///
    /// Implements the three vanilla interaction interfaces directly (verified shapes):
    ///   • <see cref="Hoverable"/>   (GetHoverText / GetHoverName, decomp :111336)
    ///   • <see cref="Interactable"/> (Interact / UseItem, decomp :111594)
    ///   • <see cref="TextReceiver"/> (GetText / SetText, decomp :54810) — the rune rename dialog
    ///
    /// ════════════════════════════════════════════════════════════════════════════════════════
    /// 🟥 LOOK-TO-AIM (card t_f4d0d5e1 / L1, spec supersession 2026-06-27). Travel is no longer a
    /// jump-through-the-ring trigger resolving the nearest SAME-RUNE portal (Model A, RETIRED).
    /// Instead the player stands on a Twisted Portal, AIMS the crosshair at any destination Twisted
    /// Portal in the world (angular pick, <see cref="AimPickMath"/>), and taps [Use]/E to commit.
    /// The destination is the AIMED portal — any Twisted Portal, not a name match (AT-AIM-SELECT).
    /// Rune names survive as human-readable AIM LABELS (spec §6), NOT the pairing key.
    ///
    /// E-INPUT IS OWNED BY <see cref="TwistedPortalCommitInput"/> (a Player.Update postfix, the
    /// SeersStone PinByLookInput precedent): tap-E commits travel to the aimed destination; hold-E
    /// opens the rune rename (Daniel's locked E-key fork — tap=commit, rename=hold). This class's
    /// <see cref="Interact"/> is therefore a NO-OP for E (it would otherwise double-fire with the
    /// Update path); the input host calls <see cref="CommitTravel"/> + <see cref="RequestRenameDialog"/>.
    /// ════════════════════════════════════════════════════════════════════════════════════════
    ///
    /// THE TELEPORT SEAM (spec §4.4 — the contract with cost-model card C2, t_6e992a30):
    ///   <see cref="CommitTravel"/> takes the AIMED destination, computes the jump distance D, hands
    ///   (player, D) to <see cref="TwistedPortalEnergy.TrySpendForJump"/>, and acts on the returned
    ///   <see cref="TwistedPortalEnergy.JumpResult"/>. The food-as-fuel math is UNCHANGED (C2 owns it);
    ///   L1 only changed how the destination is chosen and how the method is invoked.
    ///
    /// Owner-write ZDO discipline (the SurveyorTableTag / AncientPortalTag precedent): every write
    /// guards on a live ZDO (ghost = no-op), claims ownership first, then Sets. Reads are free.
    /// </summary>
    public class SBPR_TwistedPortal : MonoBehaviour, Hoverable, Interactable, TextReceiver
    {
        // 🔒 LOCKED ZDO key (save/wire contract — NEVER rename; a rename orphans every named
        // portal's rune in a live world, the SBPR_PortalPlantTime / SBPR_Ink* lock lesson).
        // DELIBERATELY SEPARATE from vanilla s_tag (spec §6 / §4.3): keeping the rune off s_tag is
        // what stops vanilla's tag machinery from ever connecting a Twisted Portal by tag collision.
        public const string ZdoRuneName = "sbpr_rune_name";

        // Rune-name char cap (spec §6 — vanilla portal tags are 10; a rune can be a touch longer).
        private const int RuneNameCharLimit = 15;

        // Exit offset for the arrival point (mirrors vanilla TeleportWorld.m_exitDistance = 1,
        // decomp :123030): step the player one metre out the front of the destination ring + up.
        private const float ExitDistance = 1f;

        // NoBossPortals: KEEP the boss-portal gate by default (spec §4.4 — a no-restriction portal
        // mid-boss-fight is likely NOT intended; flagged for Daniel, default KEEP). Flip to false to
        // let Twisted bypass the boss gate too. Engineer-resolvable; default conservative.
        private const bool RespectNoBossPortals = true;

        // Ore-ban: KEEP by default (spec §4.4 — the card lifts NoPortals, not the ore restriction;
        // the ore ban is a separate axis). Flip to false to let the endgame portal pass ore.
        // Flagged for Daniel; default conservative (keep the ban).
        private const bool RespectOreBan = true;

        private ZNetView? nview;

        // ════════════════════════════════════════════════════════════════════════════════════
        // ACTIVE-PORTAL REGISTRY (look-to-aim, L1). The input host (TwistedPortalCommitInput) and
        // the L3 overlay highlight need to resolve a candidate's ZDOID back to its live component
        // (to open the rename dialog on the ORIGIN portal the player stands on). Maintained on
        // enable/disable so a destroyed/ghost portal never lingers. Client-only in practice (the
        // input host is a local-player concern) but harmless on the server (it just isn't queried).
        // ════════════════════════════════════════════════════════════════════════════════════
        private static readonly List<SBPR_TwistedPortal> _active = new List<SBPR_TwistedPortal>();
        public static IReadOnlyList<SBPR_TwistedPortal> Active => _active;

        private void Awake()
        {
            nview = GetComponent<ZNetView>();
            // No grow lifecycle (spec §4.6): the portal is live on placement. Nothing to schedule.
            // Travel is the aim+tap-E commit owned by TwistedPortalCommitInput; the only travel gate
            // is the food-as-fuel check at commit time.
        }

        private void OnEnable()
        {
            if (!_active.Contains(this)) _active.Add(this);
        }

        private void OnDisable()
        {
            _active.Remove(this);
        }

        /// <summary>This portal's ZDO id (the stable identity the candidate set carries), or a
        /// default <see cref="ZDOID"/> on the ghost / no live ZDO.</summary>
        public ZDOID GetZdoId()
        {
            if (nview == null || !nview.IsValid()) return default;
            var zdo = nview.GetZDO();
            return zdo != null ? zdo.m_uid : default;
        }

        /// <summary>Resolve a live <see cref="SBPR_TwistedPortal"/> component by its ZDO id (the
        /// candidate-set identity). Returns null when no active portal currently matches (e.g. the
        /// origin scrolled out of the held set). Linear over the active set — tiny (you stand on one
        /// portal at a time; the active list is the handful of loaded portals).</summary>
        public static SBPR_TwistedPortal? FindByZdoId(ZDOID id)
        {
            if (id == default) return null;
            for (int i = 0; i < _active.Count; i++)
            {
                SBPR_TwistedPortal p = _active[i];
                if (p != null && p.GetZdoId() == id) return p;
            }
            return null;
        }

        // ════════════════════════════════════════════════════════════════════════════════════
        // COMMIT TRAVEL (spec §4.4) — our reimplementation of the small slice of TeleportWorld.Teleport
        // we need (decomp :123002), deliberately OMITTING the NoPortals check (:123008) — the whole
        // point (card AC#2) — and debiting belly Portal Energy (food-as-fuel) instead of any key.
        // ════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Commit travel to the AIMED destination <paramref name="selected"/> (look-to-aim, spec §4.4).
        /// Called by <see cref="TwistedPortalCommitInput"/> on tap-[Use]/E while the player stands on
        /// this portal and aims at a destination.
        ///
        /// Order of operations mirrors vanilla TeleportWorld.Teleport EXCEPT:
        ///   • NO <c>GlobalKeys.NoPortals</c> check (decomp :123008) — bypassed by design (AC#2).
        ///   • Destination is the AIMED portal (passed in), not vanilla's 1:1 ConnectionType.Portal
        ///     channel and not a same-rune match (Model A retired).
        ///   • The travel GATE + cost is FOOD-AS-FUEL via the C2 seam, not a key charge.
        ///
        /// 🔴 §5.9 COOLDOWN-REFUND GUARD: <see cref="Character.TeleportTo"/> (:20771) can return false
        /// WITHOUT moving the player (in flight, or <c>m_teleportCooldown &lt; 2f</c>, :20778-:20785).
        /// To avoid the "food/berries spent but no jump" report we PRE-CHECK that the player can teleport
        /// NOW (<see cref="CanTeleportNow"/>) BEFORE the debit — so a no-op second attempt inside the 2 s
        /// cooldown spends nothing (AT-COOLDOWN-REFUND).
        /// </summary>
        public void CommitTravel(Player player, in TwistedDestination selected)
        {
            if (player == null) return;
            if (nview == null || !nview.IsValid()) return;

            Vector3 destPos = selected.Position;
            Quaternion destRot = selected.Rotation;

            // 1) NoBossPortals — KEEP by default (spec §4.4). This is the boss gate, NOT the
            //    NoPortals gate we bypass. Mirrors vanilla :123013.
            if (RespectNoBossPortals
                && ZoneSystem.instance != null
                && RandEventSystem.instance != null
                && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBossPortals)
                && (RandEventSystem.instance.GetBossEvent() != null
                    || (ZoneSystem.instance.GetGlobalKey(GlobalKeys.activeBosses, out float activeBosses) && activeBosses > 0f)))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_blockedbyboss");
                return;
            }

            // 2) Ore-ban — KEEP by default (spec §4.4). Mirrors vanilla :123018 (the m_allowAllItems
            //    == false branch). The card lifts NoPortals, not the ore restriction.
            if (RespectOreBan && !player.IsTeleportable())
            {
                player.Message(MessageHud.MessageType.Center, "$msg_noteleport");
                return;
            }

            // 3) 🔴 §5.9 COOLDOWN PRE-CHECK — BEFORE the debit. TeleportTo would return false without
            //    moving if the player is mid-teleport or the 2 s cooldown hasn't elapsed. We must not
            //    spend food/berries on a jump that can't happen, so check FIRST and bail spending
            //    nothing (AT-COOLDOWN-REFUND). Pre-check, not refund: the cleaner of the spec's two
            //    options — no partial-state un-debit to get wrong.
            if (!CanTeleportNow(player))
            {
                // Quiet, non-tutorializing: the portal is just settling from a recent jump.
                player.Message(MessageHud.MessageType.Center, "The portal is still settling");
                return;
            }

            // 4) The jump distance — the seam input for the food-as-fuel cost model (spec §4.4).
            float distance = Vector3.Distance(player.transform.position, destPos);

            // 5) FOOD-AS-FUEL GATE + DEBIT (spec §5, card C2). C1 must NOT invent PE math — we hand
            //    the distance to the cost engine and act on its verdict. A failed gate blocks the
            //    jump with the engine's reason and spends nothing. (The food PREVIEW already showed
            //    this number pre-commit — L3 / Beat 3 — via the non-mutating PreviewJump sibling.)
            TwistedPortalEnergy.JumpResult result = TwistedPortalEnergy.TrySpendForJump(player, distance);
            if (!result.Ok)
            {
                string reason = string.IsNullOrEmpty(result.Reason) ? "Not enough fuel to travel" : result.Reason;
                player.Message(MessageHud.MessageType.Center, reason);
                return;
            }

            // 6) The actual move — the clean public primitive (decomp :20771, owner-RPC-safe:
            //    handles the 2 s fade + area-ready wait + floor-find). Same call vanilla
            //    TeleportWorld.Teleport ends in (:123031). We do NOT hand-poke transforms.
            //    The cooldown pre-check above means this should now return true; if a race still
            //    makes it false (another teleport landed in the same frame), nothing was spent
            //    beyond the debit we just applied — the §5.9 refund note's residual edge, far rarer
            //    than the back-to-back-tap case the pre-check covers.
            Vector3 exitPos = destPos + (destRot * Vector3.forward) * ExitDistance + Vector3.up;
            player.TeleportTo(exitPos, destRot, distantTeleport: true);

            // 7) Arrival side-effect owned by the cost model: a berry-burning jump arrives Feeling
            //    Sick (vanilla SE_Puke), already APPLIED inside TrySpendForJump (it has the SEMan
            //    surface). C1 only logs that berries were burned.
            string destLabel = selected.HasRune ? $"\"{selected.Rune}\"" : "(unnamed)";
            if (result.BurnedBerries > 0)
                Plugin.Log.LogInfo(
                    $"[Trailborne/TwistedPortal] Aim-commit jump to {destLabel}: {result.BurnedBerries} Bukeberries burned " +
                    $"to cover a {distance:F0} m shortfall (arrival Feeling Sick applied by C2, spec §5.5).");
            else
                Plugin.Log.LogInfo(
                    $"[Trailborne/TwistedPortal] Aim-commit jump to {destLabel}: {distance:F0} m, belly-covered.");
        }

        // ── §5.9 cooldown pre-check ──────────────────────────────────────────────────────────

        // Cached reflection handle for the private Character.m_teleportCooldown (decomp :15760). The
        // repo's AccessTools/GetField idiom (Assets.cs / SurveyorTableTag.cs). Read-only: we never
        // write it — we only check it so the food debit never fires on a jump TeleportTo would no-op.
        private static System.Reflection.FieldInfo? _teleportCooldownField;
        private static bool _teleportCooldownResolved;

        /// <summary>
        /// True when <paramref name="player"/> can actually teleport right now — i.e. vanilla
        /// <see cref="Character.TeleportTo"/> would NOT immediately return false (decomp :20778-:20785).
        /// Two gates: not already teleporting (<see cref="Character.IsTeleporting"/>, public) and the
        /// 2 s teleport cooldown has elapsed (the private <c>m_teleportCooldown</c>, read via cached
        /// reflection). Fail-OPEN on a reflection miss: if we can't read the cooldown we allow the
        /// attempt (TeleportTo's own guard still protects correctness; only the food-debit ordering
        /// edge is at stake, and a one-off mis-debit is preferable to bricking all travel on a decomp
        /// drift). The IsTeleporting gate alone already covers the in-flight case.
        /// </summary>
        private static bool CanTeleportNow(Player player)
        {
            if (player.IsTeleporting()) return false;

            if (!_teleportCooldownResolved)
            {
                _teleportCooldownResolved = true;
                _teleportCooldownField = typeof(Character).GetField(
                    "m_teleportCooldown",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (_teleportCooldownField == null)
                    Plugin.Log.LogWarning(
                        "[Trailborne/TwistedPortal] Could not resolve Character.m_teleportCooldown — the §5.9 " +
                        "cooldown pre-check will fall back to IsTeleporting() only (a back-to-back commit inside the " +
                        "2 s cooldown could mis-debit once). (Decomp drift? re-check :15760.)");
            }

            if (_teleportCooldownField == null) return true; // fail-open (IsTeleporting already passed)

            try
            {
                object? boxed = _teleportCooldownField.GetValue(player);
                if (boxed is float cooldown) return cooldown >= 2f; // vanilla's own gate (:20782)
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/TwistedPortal] CanTeleportNow cooldown read failed (fail-open): {e.Message}");
            }
            return true;
        }

        // ════════════════════════════════════════════════════════════════════════════════════
        // RUNE-NAME STORAGE (spec §6) — the sbpr_rune_name ZDO slot, owner-write discipline.
        // Under look-to-aim the rune is a human-readable AIM LABEL, not the pairing key.
        // ════════════════════════════════════════════════════════════════════════════════════

        /// <summary>This portal's rune name from the ZDO (censored on read, the SurveyorTableTag
        /// precedent), or "" when unnamed / on the ghost.</summary>
        public string ReadRuneName()
        {
            if (nview == null || !nview.IsValid()) return string.Empty;
            var zdo = nview.GetZDO();
            if (zdo == null) return string.Empty;
            string stored = zdo.GetString(ZdoRuneName, string.Empty);
            if (string.IsNullOrEmpty(stored)) return string.Empty;
            return CensorShittyWords.FilterUGC(stored, UGCType.Text, 0L);
        }

        /// <summary>Owner-write the rune name (spec §6). Claims ownership first — the exact shape
        /// SurveyorTableTag.WriteTableName / MarkerSignTag use — never a raw m_nview poke. Censors
        /// before persisting so the stored bytes are already clean. No-op on the ghost / no ZDO.
        /// Empty input clears the name (back to unnamed).</summary>
        private void WriteRuneName(string name)
        {
            if (nview == null || !nview.IsValid()) return;
            var zdo = nview.GetZDO();
            if (zdo == null) return;
            string clean = string.IsNullOrEmpty(name)
                ? string.Empty
                : CensorShittyWords.FilterUGC(name.Trim(), UGCType.Text, 0L);
            if (!nview.IsOwner()) nview.ClaimOwnership();
            zdo.Set(ZdoRuneName, clean);
        }

        /// <summary>Launch the vanilla rename dialog for this portal (spec §6) — the same TextInput
        /// path vanilla portals/signs/Tameable use (decomp TextInput.RequestText :54895). This
        /// component is the TextReceiver: GetText() pre-fills the field with the current rune (so a
        /// re-name edits rather than retypes), SetText() owner-writes the typed rune. Client act only
        /// (TextInput.instance is null headless). Public: the input host calls this on the ORIGIN
        /// portal on hold-E (the demoted rename gesture, spec §6 / AT-RENAME-DEMOTE).</summary>
        public void RequestRenameDialog()
        {
            if (TextInput.instance == null) return; // headless / no UI — nothing to show
            // Don't re-open if the dialog is already up (hold-E fires across frames).
            if (TextInput.instance.m_panel != null && TextInput.instance.m_panel.activeSelf) return;
            // Topic is plain English (repo convention — no $sbpr_* localization layer exists, so a
            // custom token leaks as "[sbpr_...]"; Localization.Localize passes plain strings through
            // unchanged, decomp :54904). "rune" semantics has no vanilla token, so plain English.
            TextInput.instance.RequestText(this, "Set portal rune", RuneNameCharLimit);
        }

        // ── Hoverable (decomp :111336) ──────────────────────────────────────────────────────

        public string GetHoverName() => "Twisted Portal";

        public string GetHoverText()
        {
            // Plain English + vanilla $KEY_Use token (the CairnInteractable / SurveyorTableTag
            // convention — a CUSTOM $piece_* token would leak as a literal, the 2026-06-05 sign bug).
            //
            // ⚠ PERF: GetHoverText is re-read EVERY FRAME by the look-at poll. We deliberately do NOT
            // walk the portal ZDO set here (that's the input host's throttled job). The hover just
            // states the rune (the aim label) + the look-to-aim gesture hints.
            string rune = ReadRuneName();
            string header = string.IsNullOrEmpty(rune)
                ? "Twisted Portal (unnamed)"
                : $"Twisted Portal — rune \"{rune}\"";
            // Look-to-aim affordances (spec §6 / Beat 4): tap-[Use] aims+commits travel; hold-[Use]
            // renames (the demoted gesture). The actual aim+commit is owned by TwistedPortalCommitInput.
            string body =
                "\n[<color=yellow><b>$KEY_Use</b></color>] Aim at a portal + tap to travel" +
                "\n[<color=yellow><b>Hold $KEY_Use</b></color>] Set rune";
            return Localize(header + body);
        }

        // ── Interactable (decomp :111594) ───────────────────────────────────────────────────

        /// <summary>
        /// 🟥 NO-OP under look-to-aim (L1). E-input (tap-E commit / hold-E rename) is owned ENTIRELY
        /// by <see cref="TwistedPortalCommitInput"/> (the Player.Update postfix) so the two paths can
        /// never double-fire (a vanilla <c>Interact(hold:true)</c> fires from frame 2 of any press,
        /// which can't cleanly separate tap from hold — the input host's press-duration timer can).
        /// We return false so vanilla treats the E press as unconsumed here and the host handles it.
        /// </summary>
        public bool Interact(Humanoid user, bool hold, bool alt) => false;

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        // ── TextReceiver (the vanilla rename dialog contract; spec §6) ───────────────────────

        /// <summary>TextReceiver: the current rune shown in the rename field (censored ZDO value,
        /// or "" when unnamed). Mirrors Tameable.GetText / SurveyorTableTag.GetText.</summary>
        public string GetText() => ReadRuneName();

        /// <summary>TextReceiver: persist the typed rune owner-side. Called by TextInput on confirm.
        /// The hover refreshes on its own — Hoverable.GetHoverName/GetHoverText are re-read every
        /// frame by the look-at poll, so the renamed portal reads correctly immediately.</summary>
        public void SetText(string text) => WriteRuneName(text);

        // ── helpers ───────────────────────────────────────────────────────────────────────────

        private static string Localize(string raw)
            => Localization.instance != null ? Localization.instance.Localize(raw) : raw;
    }
}
