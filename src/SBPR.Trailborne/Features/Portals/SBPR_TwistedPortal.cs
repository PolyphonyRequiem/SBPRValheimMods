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
    /// THE TELEPORT SEAM (spec §4.4 — the contract with cost-model card C2, t_6e992a30):
    ///   This class (C1) owns <see cref="Teleport"/>, resolves the destination, and computes the
    ///   jump distance D. It hands (player, D) to <see cref="TwistedPortalEnergy.TrySpendForJump"/>
    ///   and acts on the returned <see cref="TwistedPortalEnergy.JumpResult"/>. C1 does NOT
    ///   implement the food-as-fuel Portal Energy math; C2 fills TrySpendForJump's body. The seam
    ///   is exactly: D (meters) in → { Ok, Reason, BurnedBerries } out. See TwistedPortalEnergy.cs.
    /// ════════════════════════════════════════════════════════════════════════════════════════
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

        // Use-range guard — a portal is a small piece; match the Interact reach the cartography
        // table uses (generous; the actual gate is the look-at hover, this is the anti-reach guard).
        private const float UseDistance = 5f;

        // NoBossPortals: KEEP the boss-portal gate by default (spec §4.4 — a no-restriction portal
        // mid-boss-fight is likely NOT intended; flagged for Daniel, default KEEP). Flip to false to
        // let Twisted bypass the boss gate too. Engineer-resolvable; default conservative.
        private const bool RespectNoBossPortals = true;

        // Ore-ban: KEEP by default (spec §4.4 — the card lifts NoPortals, not the ore restriction;
        // the ore ban is a separate axis). Flip to false to let the endgame portal pass ore.
        // Flagged for Daniel; default conservative (keep the ban).
        private const bool RespectOreBan = true;

        private ZNetView? nview;

        private void Awake()
        {
            nview = GetComponent<ZNetView>();
            // No grow lifecycle (spec §4.6): the portal is live on placement. Nothing to schedule.
            // The trigger (SBPR_TwistedPortalTrigger) drives Teleport; the only gate is food-as-fuel.
        }

        // ════════════════════════════════════════════════════════════════════════════════════
        // TELEPORT (spec §4.4) — our reimplementation of the small slice of TeleportWorld.Teleport
        // we need (decomp :123002), deliberately OMITTING the NoPortals check (:123008) — the whole
        // point (card AC#2) — and debiting belly Portal Energy (food-as-fuel) instead of any key.
        // ════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Teleport <paramref name="player"/> to the nearest other Twisted Portal sharing this
        /// portal's rune name. Called by <see cref="SBPR_TwistedPortalTrigger"/> on jump-through.
        ///
        /// Order of operations mirrors vanilla TeleportWorld.Teleport EXCEPT:
        ///   • NO <c>GlobalKeys.NoPortals</c> check (decomp :123008) — bypassed by design (AC#2).
        ///   • Destination is a RUNE-NAME MATCH resolved server-side (Model A), not vanilla's 1:1
        ///     ConnectionType.Portal channel.
        ///   • The travel GATE + cost is FOOD-AS-FUEL via the C2 seam, not a key charge.
        /// </summary>
        public void Teleport(Player player)
        {
            if (player == null) return;
            if (nview == null || !nview.IsValid()) return;

            // 1) Resolve the paired destination by rune name (server-correct walk, Model A).
            if (!ResolveDestination(out Vector3 destPos, out Quaternion destRot))
            {
                // Plain English (repo convention — there is no $sbpr_* localization registration
                // layer in this repo, so a custom token would leak as a literal "[sbpr_...]"; the
                // SurveyorTableTag center-message precedent uses plain English for the same reason).
                player.Message(MessageHud.MessageType.Center, "No twisted portal shares this rune");
                return;
            }

            // 2) NoBossPortals — KEEP by default (spec §4.4). This is the boss gate, NOT the
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

            // 3) Ore-ban — KEEP by default (spec §4.4). Mirrors vanilla :123018 (the m_allowAllItems
            //    == false branch). The card lifts NoPortals, not the ore restriction.
            if (RespectOreBan && !player.IsTeleportable())
            {
                player.Message(MessageHud.MessageType.Center, "$msg_noteleport");
                return;
            }

            // 4) The jump distance — the seam input for the food-as-fuel cost model (spec §4.4).
            float distance = Vector3.Distance(player.transform.position, destPos);

            // 5) FOOD-AS-FUEL GATE + DEBIT (spec §5, card C2). C1 must NOT invent PE math — we hand
            //    the distance to the cost engine and act on its verdict. A failed gate blocks the
            //    jump with the engine's reason and spends nothing.
            TwistedPortalEnergy.JumpResult result = TwistedPortalEnergy.TrySpendForJump(player, distance);
            if (!result.Ok)
            {
                // Reason is a message string chosen by C2. Plain English (repo convention — no
                // $sbpr_* localization layer exists, so a custom token would leak as a literal);
                // fall back to a generic line if C2 left it empty so the player is never left guessing.
                string reason = string.IsNullOrEmpty(result.Reason) ? "Not enough fuel to travel" : result.Reason;
                player.Message(MessageHud.MessageType.Center, reason);
                return;
            }

            // 6) The actual move — the clean public primitive (decomp :20771, owner-RPC-safe:
            //    handles the 2 s fade + area-ready wait + floor-find). Same call vanilla
            //    TeleportWorld.Teleport ends in (:123031). We do NOT hand-poke transforms.
            Vector3 exitPos = destPos + (destRot * Vector3.forward) * ExitDistance + Vector3.up;
            player.TeleportTo(exitPos, destRot, distantTeleport: true);

            // 7) Arrival side-effect owned by the cost model: a berry-burning jump arrives Feeling
            //    Sick (vanilla SE_Puke). C1 only relays the verdict; C2 owns the effect application
            //    inside TrySpendForJump (it has the SEMan surface). If C2 chooses to apply it here
            //    instead, BurnedBerries lets C1 do so — but the seam contract is C2 applies it; C1
            //    just records that berries were burned for the log. (Spec §5.5.)
            if (result.BurnedBerries > 0)
                Plugin.Log.LogInfo(
                    $"[Trailborne/TwistedPortal] Berry-assisted jump: {result.BurnedBerries} Bukeberries burned " +
                    $"to cover a {distance:F0} m shortfall (arrival Feeling Sick is C2's to apply, spec §5.5).");
        }

        // ════════════════════════════════════════════════════════════════════════════════════
        // DESTINATION RESOLUTION (spec §4.4a) — Model A name-pairing, server-correct.
        // ════════════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Resolve the paired destination: the NEAREST OTHER Twisted Portal whose <c>sbpr_rune_name</c>
        /// equals this portal's (Model A, spec §4.4a). Walks the Twisted-Portal ZDO set via
        /// <c>ZDOMan.GetAllZDOsWithPrefabIterative</c> (decomp :65497) — our own walk, NOT vanilla's
        /// m_portalObjects (the §4.3 option-(b) decoupling).
        ///
        /// 🔴 Multiplayer correctness (spec §2): this walk sees only the ZDOs the CURRENT peer holds.
        /// On a dedicated server the HOST holds the full world ZDO set, so when the host owns the
        /// world (the common case) resolution is complete. A client that owns its local portal but
        /// not the distant destination would see a short list — for v3.0 with Model A the common
        /// host-owns-world case resolves directly; a routed-RPC owner fallback is the robustness path
        /// the spec flags as out-of-v3.0-scope (§4.4a) and is left for a later milestone if needed.
        ///
        /// Returns false (no destination) when the portal is unnamed, or no other portal shares the
        /// rune. An unnamed portal never pairs (you must name two portals the same rune to link them).
        /// </summary>
        private bool ResolveDestination(out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;

            if (nview == null || !nview.IsValid()) return false;
            var selfZdo = nview.GetZDO();
            if (selfZdo == null) return false;

            string myRune = ReadRuneName();
            if (string.IsNullOrEmpty(myRune)) return false;   // unnamed → never pairs

            var zdoMan = ZDOMan.instance;
            if (zdoMan == null) return false;

            // Accumulate every Twisted-Portal ZDO this peer holds. GetAllZDOsWithPrefabIterative is
            // a paged walk (≤400 sectors/call) that returns false until exhausted, then true on the
            // final outside-sector sweep — drain it fully (the decomp's own usage idiom, :37512).
            var all = new List<ZDO>();
            int index = 0;
            while (!zdoMan.GetAllZDOsWithPrefabIterative(TwistedPortal.PortalPieceName, all, ref index)) { }

            Vector3 here = transform.position;
            ZDOID selfId = selfZdo.m_uid;
            ZDO? best = null;
            float bestSqr = float.MaxValue;

            foreach (var z in all)
            {
                if (z == null) continue;
                if (z.m_uid == selfId) continue;                  // exclude self
                // Censor BOTH sides identically before comparing (myRune is already censored via
                // ReadRuneName; censor theirs too). Both were censored at write time so this is a
                // no-op for normal runes, but it keeps the match symmetric and bulletproof against
                // any legacy-uncensored bytes / non-idempotent filter edge case.
                string theirRaw = z.GetString(ZdoRuneName, string.Empty);
                if (string.IsNullOrEmpty(theirRaw)) continue;      // unnamed peer can't be a destination
                string theirRune = CensorShittyWords.FilterUGC(theirRaw, UGCType.Text, 0L);
                if (theirRune != myRune) continue;                 // Model A: exact rune match

                float d = (z.GetPosition() - here).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = z; }
            }

            if (best == null)
            {
                // 🔴 §2 DIAGNOSTIC — distinguish "genuinely no partner" from "partner windowed-out
                // on a dedicated client." This walk sees only the ZDOs THIS peer holds. On a
                // dedicated server a client holds ~64–128 m of ZDOs (§2), so a 300 m-distant partner
                // is invisible here even though it exists server-side. We can't teleport to a ZDO we
                // can't see, so we fail closed — but we log WHICH case it is so playtest can tell a
                // real missing-pair from the known dedicated-server limitation (the §4.4a robustness
                // path / routed-RPC resolution is explicitly out of v3.0 scope; flagged for review).
                bool maybeWindowed = !nview.IsOwner() && ZNet.instance != null && !ZNet.instance.IsServer();
                Plugin.Log.LogInfo(
                    $"[Trailborne/TwistedPortal] No destination for rune \"{myRune}\" among {all.Count} " +
                    $"Twisted Portal ZDO(s) held by this peer." +
                    (maybeWindowed
                        ? " NOTE: this peer is a dedicated-server CLIENT and does not own this portal — a "
                          + "partner beyond the ~64–128 m ZDO window (§2) would not appear here. If the pair "
                          + "exists but is far, this is the known v3.0 dedicated-server limitation (routed-RPC "
                          + "resolution is out of scope), NOT a missing pair."
                        : " (This peer holds the full/local ZDO set, so the pair genuinely does not exist.)"));
                return false;
            }

            pos = best.GetPosition();
            rot = best.GetRotation();
            return true;
        }

        // ════════════════════════════════════════════════════════════════════════════════════
        // RUNE-NAME STORAGE (spec §6) — the sbpr_rune_name ZDO slot, owner-write discipline.
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

        /// <summary>Launch the vanilla rename dialog for this portal (spec §1 Q3a / §6) — the same
        /// TextInput path vanilla portals/signs/Tameable use (decomp TextInput.RequestText :54895).
        /// This component is the TextReceiver: GetText() pre-fills the field with the current rune
        /// (so a re-name edits rather than retypes), SetText() owner-writes the typed rune. Client
        /// act only (TextInput.instance is null headless).</summary>
        private void RequestRename()
        {
            if (TextInput.instance == null) return; // headless / no UI — nothing to show
            // Topic is plain English (repo convention — no $sbpr_* localization layer exists, so a
            // custom token leaks as "[sbpr_...]"; Localization.Localize passes plain strings through
            // unchanged, decomp :54904). SurveyorTableTag uses the VANILLA $hud_rename token; we want
            // "rune" semantics, which has no vanilla token, so plain English is the no-leak choice.
            TextInput.instance.RequestText(this, "Set portal rune", RuneNameCharLimit);
        }

        // ── Hoverable (decomp :111336) ──────────────────────────────────────────────────────

        public string GetHoverName() => "Twisted Portal";

        public string GetHoverText()
        {
            // Plain English + vanilla $KEY_Use token (the CairnInteractable / SurveyorTableTag
            // convention — a CUSTOM $piece_* token would leak as a literal, the 2026-06-05 sign bug).
            //
            // ⚠ PERF: GetHoverText is re-read EVERY FRAME by the look-at poll while a player aims at
            // the portal. We deliberately do NOT resolve the destination here — ResolveDestination
            // walks the whole Twisted-Portal ZDO set (GetAllZDOsWithPrefabIterative), which is far too
            // expensive to run per-frame. The "what am I paired with" read-out belongs to the on-step
            // proximity overlay (card C3, t_e732bd8b), which throttles its own refresh. The hover just
            // states the rune + the naming affordance.
            string rune = ReadRuneName();
            string header = string.IsNullOrEmpty(rune)
                ? "Twisted Portal (unnamed — name it to pair)"
                : $"Twisted Portal — rune \"{rune}\"";
            string body = "\n[<color=yellow><b>$KEY_Use</b></color>] Set rune";
            return Localize(header + body);
        }

        // ── Interactable (decomp :111594) ───────────────────────────────────────────────────

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (user == null || user != Player.m_localPlayer) return false;
            if (nview == null || !nview.IsValid()) return false;
            if (Vector3.Distance(user.transform.position, transform.position) > UseDistance + 1f) return false;

            // [Use] (and Alt+Use) both open the rune rename dialog — naming IS the only interaction
            // (Model A: you pick a destination by naming two portals the same rune, spec §1 Q3a).
            // GetText() pre-fills the current rune so an already-named portal edits rather than retypes.
            RequestRename();
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        // ── TextReceiver (the vanilla rename dialog contract; spec §6 / Q3a) ──────────────────

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
