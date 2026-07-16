using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using SBPR.Niflheim.HomesteadStones.Domain;
using UnityEngine;

namespace SBPR.Niflheim.HomesteadStones.Features.HomesteadStone
{
    /// <summary>
    /// Constructs the network/gameplay root additively and attaches the accepted V12
    /// presentation prefab as a cosmetic child. The AssetBundle stays resident for the
    /// lifetime of the process because live renderers reference its assets.
    /// </summary>
    [HarmonyPatch]
    internal static class HomesteadStoneRegistrar
    {
        internal const string PrefabName = "piece_niflheim_homestead_stone";
        internal const string BundleFile = "sbpr_niflheim_homestead_stones.unity3d";
        internal const string VisualAssetPath = "assets/sbpr/niflheim/homesteadstones/meadowshomesteadingstone.prefab";
        internal const float VisualLocalY = HomesteadStonePresentation.VisualLocalY;

        private static readonly GameObject Holder = CreateHolder();
        private static AssetBundle? bundle;
        private static GameObject? visualPrefab;

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void OnZNetSceneAwake(ZNetScene __instance)
        {
            try
            {
                Register(__instance);
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError($"[Niflheim/HomesteadStones] Prefab registration failed: {exception}");
            }
        }

        internal static void Register(ZNetScene scene)
        {
            if (scene.GetPrefab(PrefabName) != null) return;
            if (!TryLoadVisual(out var visual)) return;

            var root = new GameObject(PrefabName);
            root.transform.SetParent(Holder.transform, false);
            root.layer = LayerMask.NameToLayer("Default");

            var networkView = root.AddComponent<ZNetView>();
            networkView.m_persistent = true;
            networkView.m_type = ZDO.ObjectType.Solid;
            networkView.m_distant = false;

            // This marker owns Homestead identity/state semantics without silently opting into
            // vanilla build removal, support, damage, or destruction policy.
            root.AddComponent<HomesteadStoneIdentity>();

            // Explicit gameplay collision belongs to the additive root, never the decorative bundle.
            // Refit to the enlarged (2×) and raised (+1 m) visual envelope so targeting is neither
            // undersized nor ghostly against the ~3.6 m stone floating at +2.0 m.
            var collider = root.AddComponent<CapsuleCollider>();
            collider.radius = HomesteadStonePresentation.ColliderRadius;
            collider.height = HomesteadStonePresentation.ColliderHeight;
            collider.center = new Vector3(0f, HomesteadStonePresentation.ColliderCenterY, 0f);

            var presentation = UnityEngine.Object.Instantiate(visual, root.transform, false);
            presentation.name = "MeadowsHomesteadingStone visual";
            presentation.transform.localPosition = new Vector3(0f, VisualLocalY, 0f);
            presentation.transform.localRotation = Quaternion.identity;
            presentation.transform.localScale = Vector3.one * HomesteadStonePresentation.VisualScale;
            RemoveGameplayComponents(presentation);
            if (presentation.GetComponentInChildren<Animator>(true) == null)
            {
                presentation.AddComponent<HomesteadStoneVisualMotion>();
                Plugin.Log.LogWarning(
                    "[Niflheim/HomesteadStones] Stable bundle contains no Animator; installed the equivalent " +
                    "four-second procedural hover/yaw motion on the visual child.");
            }

            RegisterPrefab(scene, root);
            Plugin.Log.LogInfo(
                $"[Niflheim/HomesteadStones] Registered {PrefabName} additively with V12 visual " +
                $"'{VisualAssetPath}' at {HomesteadStonePresentation.VisualScale:0.0}× scale, local Y +{VisualLocalY:0.0} m, " +
                $"refit root collider (r={HomesteadStonePresentation.ColliderRadius:0.00}, h={HomesteadStonePresentation.ColliderHeight:0.0}), and no Piece/WearNTear policy.");
        }

        private static bool TryLoadVisual(out GameObject visual)
        {
            if (visualPrefab != null)
            {
                visual = visualPrefab;
                return true;
            }
            var path = Path.Combine(Plugin.PluginFolder, BundleFile);
            if (!File.Exists(path))
            {
                Plugin.Log.LogError($"[Niflheim/HomesteadStones] Required AssetBundle missing: {path}");
                visual = null!;
                return false;
            }
            bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                Plugin.Log.LogError($"[Niflheim/HomesteadStones] AssetBundle.LoadFromFile returned null: {path}");
                visual = null!;
                return false;
            }
            visualPrefab = bundle.LoadAsset<GameObject>(VisualAssetPath);
            if (visualPrefab == null)
            {
                Plugin.Log.LogError(
                    $"[Niflheim/HomesteadStones] Required visual asset '{VisualAssetPath}' was absent from {path}. " +
                    $"Available assets: {string.Join(", ", bundle.GetAllAssetNames())}");
                visual = null!;
                return false;
            }
            visual = visualPrefab;
            return true;
        }

        private static void RemoveGameplayComponents(GameObject presentation)
        {
            foreach (var networkView in presentation.GetComponentsInChildren<ZNetView>(true))
                UnityEngine.Object.DestroyImmediate(networkView);
            foreach (var piece in presentation.GetComponentsInChildren<Piece>(true))
                UnityEngine.Object.DestroyImmediate(piece);
            foreach (var wear in presentation.GetComponentsInChildren<WearNTear>(true))
                UnityEngine.Object.DestroyImmediate(wear);
            foreach (var collider in presentation.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            foreach (var body in presentation.GetComponentsInChildren<Rigidbody>(true))
                UnityEngine.Object.DestroyImmediate(body);
        }

        private static void RegisterPrefab(ZNetScene scene, GameObject prefab)
        {
            var field = typeof(ZNetScene).GetField("m_namedPrefabs", BindingFlags.Instance | BindingFlags.NonPublic);
            var named = field?.GetValue(scene) as Dictionary<int, GameObject>;
            if (named == null) throw new MissingFieldException(typeof(ZNetScene).FullName, "m_namedPrefabs");
            var hash = prefab.name.GetStableHashCode();
            if (!named.ContainsKey(hash))
            {
                scene.m_prefabs.Add(prefab);
                named.Add(hash, prefab);
            }
        }

        private static GameObject CreateHolder()
        {
            var holder = new GameObject("SBPR.Niflheim.HomesteadStones.PrefabHolder");
            holder.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(holder);
            return holder;
        }
    }

    internal sealed class HomesteadStoneIdentity : MonoBehaviour
    {
    }
}
