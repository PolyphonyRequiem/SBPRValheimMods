using System.Collections.Generic;
using HarmonyLib;
using SBPR.Trailborne.Runtime;

namespace SBPR.Trailborne.Features.Trailblazing
{
    /// <summary>
    /// ONE-TIME, SERVER-ONLY migration sweep (card t_6fc9b3fa, AT-OP-3): destroy any
    /// legacy persistent terrain-op ZDOs that earlier (pre-0.2.17) builds left in the
    /// world, before they can spam "ZDO … not used when creating" warnings and hang a
    /// joining client (the v0.2.14 failure).
    ///
    /// ── Why these orphans exist ──
    /// Through v0.2.15 the spade cloned the LEGACY `path`/`replant` donors, which are
    /// `TerrainModifier` networked pieces WITH a ZNetView — so every spade op the player
    /// placed wrote a PERSISTENT `piece_sbpr_path_*` / `piece_sbpr_replant_*` ZDO into the
    /// world. v0.2.17 rebuilds those ops as modern `TerrainOp` pieces with NO ZNetView, not
    /// registered in ZNetScene (mirroring vanilla `path_v2`/`replant_v2`). The old prefab
    /// names therefore no longer resolve to a registered prefab.
    ///
    /// ── Why the structural fix already handles them, and why we ALSO sweep ──
    /// Because the new ops are NOT registered in ZNetScene, `ZNetScene.CreateObject`
    /// (assembly_valheim L69166) calls `GetPrefab(hash)`, gets null, and returns BEFORE it
    /// instantiates anything or emits the "not used when creating" warning (that warning
    /// only fires AFTER an Instantiate leaves the init-ZDO unconsumed — L69182-69186). The
    /// server's own `CreateObjectsSorted` (L69305-69310) then SetOwner+DestroyZDO's the
    /// invalid ZDO. So vanilla auto-cleans these on the first server load REGARDLESS of this
    /// patch — that is the real structural guarantee (verified against the live niflheim
    /// world: 18 legacy op ZDOs across path+replant).
    ///
    /// This sweep is belt-and-braces on top of that: it runs the SAME vanilla operation
    /// (claim ownership of the orphan, then `ZDOMan.DestroyZDO`, which broadcasts the
    /// destroy RPC to clients) EXPLICITLY and EAGERLY at world-load — before any zone even
    /// streams in — and logs an exact count. That gives a deterministic, verifiable
    /// migration step (AT-OP-3 asks for "ZERO 'not used when creating' warnings on a
    /// CLIENT") instead of relying on the lazy per-zone path firing before a client looks at
    /// that zone. Idempotent by nature: once destroyed the ZDOs are gone, so a second boot
    /// sweeps zero.
    ///
    /// ── Hook ──
    /// Postfix on `ZNet.LoadWorld` — which is called ONLY from `ServerLoadWorld`, itself
    /// gated by `if (m_isServer)` (assembly_valheim L66811-66823). So this is inherently
    /// server-only and fires exactly once per boot, AFTER `m_zdoMan.Load` has populated
    /// every ZDO (L68188). No client ever runs it. Clean-room: every surface
    /// (`ZDOMan.GetAllZDOsWithPrefabIterative`, `ZDO.SetOwner`, `ZDOMan.GetSessionID`,
    /// `ZDOMan.DestroyZDO`) is a public method on Valheim's own assembly, used exactly as
    /// vanilla's own invalid-ZDO cleanup uses them.
    /// </summary>
    [HarmonyPatch(typeof(ZNet), "LoadWorld")]
    public static class LegacyTerrainOpZdoCleanup
    {
        // The exact prefab names earlier builds wrote as persistent networked modifiers.
        // (Sourced from the same constants the ops use — kept as a literal list so the
        // sweep targets the LEGACY footprint explicitly even if the op naming ever changes.)
        private static readonly string[] LegacyOpPrefabNames =
        {
            Trailblazing.PathNarrowName,
            Trailblazing.PathStandardName,
            Trailblazing.PathWideName,
            Trailblazing.ReplantNarrowName,
            Trailblazing.ReplantStandardName,
            Trailblazing.ReplantWideName,
        };

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix()
        {
            try
            {
                var zdoMan = ZDOMan.instance;
                if (zdoMan == null)
                {
                    Plugin.Log.LogWarning(
                        "[Trailborne/Migrate] LoadWorld postfix: ZDOMan.instance is null; " +
                        "legacy terrain-op ZDO sweep skipped this boot (vanilla will still auto-clean lazily).");
                    return;
                }

                long sessionId = ZDOMan.GetSessionID();
                int totalDestroyed = 0;
                var perName = new List<string>();

                foreach (var name in LegacyOpPrefabNames)
                {
                    var found = new List<ZDO>();
                    // Iterate the sector arrays in bounded chunks until the enumerator says
                    // it's done (returns true). Same idiom vanilla uses for prefab queries
                    // (assembly_valheim L37511-37514).
                    int index = 0;
                    while (!zdoMan.GetAllZDOsWithPrefabIterative(name, found, ref index))
                    {
                    }

                    int destroyed = 0;
                    foreach (var zdo in found)
                    {
                        if (zdo == null) continue;
                        // Claim ownership before destroying — ZDOMan.DestroyZDO only queues a
                        // destroy for ZDOs WE own (assembly_valheim L65001-65007). This is
                        // exactly what vanilla's invalid-ZDO cleanup does (L69305-69310).
                        zdo.SetOwner(sessionId);
                        zdoMan.DestroyZDO(zdo);
                        destroyed++;
                    }

                    if (destroyed > 0)
                    {
                        perName.Add($"{name}={destroyed}");
                        totalDestroyed += destroyed;
                    }
                }

                if (totalDestroyed > 0)
                {
                    Plugin.Log.LogWarning(
                        $"[Trailborne/Migrate] Cleaned {totalDestroyed} legacy persistent terrain-op ZDO(s) " +
                        $"from this world ({string.Join(", ", perName)}). These were written by the pre-0.2.17 " +
                        "TerrainModifier donors; the new additive TerrainOp ops write no persistent ZDO. " +
                        "Destroy RPCs were broadcast so clients drop them too — no orphan, no client hang.");
                }
                else
                {
                    Plugin.Log.LogInfo(
                        "[Trailborne/Migrate] No legacy persistent terrain-op ZDOs found in this world " +
                        "(already clean, or this world never held the old donors). Nothing to migrate.");
                }
            }
            catch (System.Exception e)
            {
                // A failure here must NOT take down world load. Worst case we fall back to
                // vanilla's own lazy per-zone invalid-ZDO cleanup, which still removes them.
                Plugin.Log.LogError($"[Trailborne/Migrate] Legacy terrain-op ZDO sweep threw (non-fatal): {e}");
            }
        }
    }
}
