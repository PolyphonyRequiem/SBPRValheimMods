using System;
using HarmonyLib;
using UnityEngine;

namespace SBPR.Trailborne.Features.Cairns
{
    /// <summary>
    /// Interactable surface for cairns:
    ///   • E (no alt)          → repair+upgrade combo, gated on HP &lt; 75%
    ///   • Shift+E (alt=true)  → debug-damage to 70% HP, gated on TrailbornePlugin.DebugCairnDamage
    ///
    /// Hover text reports tier, HP%, and next-action affordance.
    /// </summary>
    public class TrailborneCairnInteractable : MonoBehaviour, Hoverable, Interactable
    {
        private const float UseDistance = 4.0f;
        private TrailborneCairnTag _tag;
        private WearNTear _wnt;
        private Piece _piece;

        private void Awake()
        {
            _tag = GetComponent<TrailborneCairnTag>();
            _wnt = GetComponent<WearNTear>();
            _piece = GetComponent<Piece>();
        }

        public string GetHoverName()
        {
            return _piece != null ? _piece.m_name : "Cairn";
        }

        public string GetHoverText()
        {
            if (_tag == null) return GetHoverName();
            int tier = _tag.ReadTier();
            float hp = _wnt != null ? _wnt.GetHealthPercentage() : 1f;
            int hpPct = Mathf.RoundToInt(hp * 100f);
            int comfortFloor = TrailborneM2.ComfortFloorForTier(tier);

            string line2;
            if (hp < TrailborneM2.PristineHpFraction)
            {
                if (tier < TrailborneM2.MaxTier)
                    line2 = $"[<color=yellow><b>$KEY_Use</b></color>] Repair + Upgrade → T{tier + 1} ({TrailborneM2.UpgradeStoneCost} Stone + {TrailborneM2.UpgradeResinCost} Resin)";
                else
                    line2 = $"[<color=yellow><b>$KEY_Use</b></color>] Repair ({TrailborneM2.UpgradeStoneCost} Stone + {TrailborneM2.UpgradeResinCost} Resin)";
            }
            else
            {
                line2 = "Pristine — no maintenance needed.";
            }

            string line3 = "";
            if (TrailbornePlugin.DebugCairnDamage != null && TrailbornePlugin.DebugCairnDamage.Value)
                line3 = "\n[<color=#ff8b3d><b>Shift+$KEY_Use</b></color>] (debug) damage to 70%";

            return $"{_piece.m_name}\nTier {tier} / {TrailborneM2.MaxTier} — comfort floor {comfortFloor} — HP {hpPct}%\n{line2}{line3}";
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            if (user != Player.m_localPlayer) return false;
            if (Vector3.Distance(user.transform.position, transform.position) > UseDistance + 1.0f) return false;
            if (_tag == null || _wnt == null) return false;

            if (alt)
            {
                // Shift+E debug damage
                if (TrailbornePlugin.DebugCairnDamage == null || !TrailbornePlugin.DebugCairnDamage.Value)
                    return false;
                return DoDebugDamage();
            }

            return DoRepairAndUpgrade(user);
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        private bool DoRepairAndUpgrade(Humanoid user)
        {
            float hp = _wnt.GetHealthPercentage();
            if (hp >= TrailborneM2.PristineHpFraction)
            {
                MessageHud.instance?.ShowMessage(
                    MessageHud.MessageType.Center,
                    "Cairn is pristine — no maintenance needed.");
                return true;
            }

            var inv = user.GetInventory();
            if (inv == null) return false;
            int needStone = TrailborneM2.UpgradeStoneCost;
            int needResin = TrailborneM2.UpgradeResinCost;
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
            try { _wnt.Repair(); }
            catch (Exception e) { TrailbornePlugin.Log.LogWarning($"[Trailborne/M2] Repair() threw: {e.Message}"); }

            int tier = _tag.ReadTier();
            string action = "Repaired";
            if (tier < TrailborneM2.MaxTier)
            {
                _tag.WriteTier(tier + 1);
                action = $"Repaired + upgraded to T{tier + 1}";
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
            if (_wnt == null) return false;
            float curPct = _wnt.GetHealthPercentage();
            if (curPct < TrailborneM2.PristineHpFraction)
            {
                MessageHud.instance?.ShowMessage(
                    MessageHud.MessageType.Center,
                    $"(debug) Cairn already fizzled — HP {Mathf.RoundToInt(curPct * 100f)}%");
                return true;
            }
            // ApplyDamage bypasses our Damage(HitData) immunity prefix on purpose —
            // this is the deliberate debug back-door for the v0.1.0 playtest.
            float damageAmount = _wnt.m_health * (1f - TrailborneM2.DebugDamageTargetFraction);
            try { _wnt.ApplyDamage(damageAmount); }
            catch (Exception e)
            {
                TrailbornePlugin.Log.LogWarning($"[Trailborne/M2] Debug ApplyDamage threw: {e.Message}");
                return false;
            }
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.Center,
                $"(debug) Cairn damaged to ~{Mathf.RoundToInt(TrailborneM2.DebugDamageTargetFraction * 100f)}% HP");
            return true;
        }
    }
}
