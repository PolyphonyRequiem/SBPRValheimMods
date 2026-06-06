using System;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.Cairns
{
    /// <summary>
    /// Interactable surface for cairns:
    ///   • E (no alt)          → repair+upgrade combo, gated on HP &lt; 75%
    ///   • Shift+E (alt=true)  → debug-damage to 70% HP, gated on Plugin.DebugCairnDamage
    ///
    /// Hover text reports tier, HP%, and next-action affordance.
    /// </summary>
    public class CairnInteractable : MonoBehaviour, Hoverable, Interactable
    {
        private const float UseDistance = 4.0f;
        private CairnTag cairnTag = null!;   // Unity-injected in Awake via GetComponent
        private WearNTear wnt = null!;        // Unity-injected in Awake via GetComponent
        private Piece piece = null!;          // Unity-injected in Awake via GetComponent

        private void Awake()
        {
            cairnTag = GetComponent<CairnTag>();
            wnt = GetComponent<WearNTear>();
            piece = GetComponent<Piece>();
        }

        public string GetHoverName()
        {
            return piece != null ? piece.m_name : "Cairn";
        }

        public string GetHoverText()
        {
            if (cairnTag == null) return GetHoverName();
            int tier = cairnTag.ReadTier();
            float hp = wnt != null ? wnt.GetHealthPercentage() : 1f;
            int hpPct = Mathf.RoundToInt(hp * 100f);
            int comfortFloor = Cairns.ComfortFloorForTier(tier);

            string line2;
            if (hp < Cairns.PristineHpFraction)
            {
                if (tier < Cairns.MaxTier)
                    line2 = $"[<color=yellow><b>$KEY_Use</b></color>] Repair + Upgrade → T{tier + 1} ({Cairns.UpgradeStoneCost} Stone + {Cairns.UpgradeResinCost} Resin)";
                else
                    line2 = $"[<color=yellow><b>$KEY_Use</b></color>] Repair ({Cairns.UpgradeStoneCost} Stone + {Cairns.UpgradeResinCost} Resin)";
            }
            else
            {
                line2 = "Pristine — no maintenance needed.";
            }

            string line3 = "";
            if (Plugin.DebugCairnDamage != null && Plugin.DebugCairnDamage.Value)
                line3 = "\n[<color=#ff8b3d><b>Shift+$KEY_Use</b></color>] (debug) damage to 70%";

            return $"{piece.m_name}\nTier {tier} / {Cairns.MaxTier} — comfort floor {comfortFloor} — HP {hpPct}%\n{line2}{line3}";
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (user != Player.m_localPlayer) return false;
            if (Vector3.Distance(user.transform.position, transform.position) > UseDistance + 1.0f) return false;
            if (cairnTag == null || wnt == null) return false;

            if (alt)
            {
                // Shift+E debug damage
                if (Plugin.DebugCairnDamage == null || !Plugin.DebugCairnDamage.Value)
                    return false;
                return DoDebugDamage();
            }

            return DoRepairAndUpgrade(user);
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        private bool DoRepairAndUpgrade(Humanoid user)
        {
            float hp = wnt.GetHealthPercentage();
            if (hp >= Cairns.PristineHpFraction)
            {
                MessageHud.instance?.ShowMessage(
                    MessageHud.MessageType.Center,
                    "Cairn is pristine — no maintenance needed.");
                return true;
            }

            var inv = user.GetInventory();
            if (inv == null) return false;
            int needStone = Cairns.UpgradeStoneCost;
            int needResin = Cairns.UpgradeResinCost;
            if (inv.CountItems("$item_stone") < needStone || inv.CountItems("$item_resin") < needResin)
            {
                MessageHud.instance?.ShowMessage(
                    MessageHud.MessageType.Center,
                    $"Need {needStone} Stone + {needResin} Resin.");
                return true;
            }

            // Pay
            inv.RemoveItem("$item_stone", needStone);
            inv.RemoveItem("$item_resin", needResin);

            // Repair to max via vanilla path — handles ZDO + RPC fanout cleanly.
            try { wnt.Repair(); }
            catch (Exception e) { Plugin.Log.LogWarning($"[Trailborne/M2] Repair() threw: {e.Message}"); }

            int tier = cairnTag.ReadTier();
            string action = "Repaired";
            if (tier < Cairns.MaxTier)
            {
                // WriteTier rebuilds the pile at the new stone count AND re-evaluates
                // the ember bracket, so the relight is handled there on an upgrade.
                cairnTag.WriteTier(tier + 1);
                action = $"Repaired + upgraded to T{tier + 1}";
            }
            else
            {
                // Max tier: no rebuild, so nudge the ember to reconcile to the now-
                // pristine HP bracket immediately instead of waiting for the 1 s poll.
                cairnTag.RefreshEmber();
            }

            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.Center,
                $"{action} ({needStone}S + {needResin}R)");

            // Refresh comfort if the player is nearby (cheap, the SE_Rested patch
            // re-runs OverlapSphere on each comfort calculation tick anyway).
            return true;
        }

        private bool DoDebugDamage()
        {
            if (wnt == null) return false;
            float curPct = wnt.GetHealthPercentage();
            if (curPct < Cairns.PristineHpFraction)
            {
                MessageHud.instance?.ShowMessage(
                    MessageHud.MessageType.Center,
                    $"(debug) Cairn already fizzled — HP {Mathf.RoundToInt(curPct * 100f)}%");
                return true;
            }
            // ApplyDamage bypasses our Damage(HitData) immunity prefix on purpose —
            // this is the deliberate debug back-door for the v0.1.0 playtest.
            float damageAmount = wnt.m_health * (1f - Cairns.DebugDamageTargetFraction);
            try { wnt.ApplyDamage(damageAmount); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Trailborne/M2] Debug ApplyDamage threw: {e.Message}");
                return false;
            }
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.Center,
                $"(debug) Cairn damaged to ~{Mathf.RoundToInt(Cairns.DebugDamageTargetFraction * 100f)}% HP");
            return true;
        }
    }
}
