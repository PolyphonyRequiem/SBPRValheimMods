using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class BuildNiflheimHomesteadStone
{
    private const string SourceRoot = "Assets/SBPR/Niflheim/HomesteadStones/Source";
    private const string GeneratedRoot = "Assets/SBPR/Niflheim/HomesteadStones/Generated";
    private const string StablePrefabPath = "Assets/SBPR/Niflheim/HomesteadStones/MeadowsHomesteadingStone.prefab";
    private const string BundleName = "sbpr_niflheim_homestead_stones.unity3d";

    private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    private static string RepositoryRoot => Path.GetFullPath(Path.Combine(ProjectRoot, "..", "..", "repos", "SBPRValheimMods"));
    private static string RepoSourceRoot => Path.Combine(RepositoryRoot, "src", "SBPR.Niflheim.HomesteadStones", "Assets", "Source");
    private static string BundleOutputRoot => Path.Combine(RepositoryRoot, "src", "SBPR.Niflheim.HomesteadStones", "Assets", "Bundles");

    private static Material MakeMaterial(string name, Color color, float glossiness)
    {
        var material = new Material(Shader.Find("Standard"));
        material.name = name;
        material.color = color;
        material.SetColor("_Color", color);
        material.SetFloat("_Glossiness", glossiness);
        material.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(material, GeneratedRoot + "/" + name + ".mat");
        return material;
    }

    public static void Run()
    {
        Directory.CreateDirectory(SourceRoot);
        Directory.CreateDirectory(GeneratedRoot);
        Directory.CreateDirectory(BundleOutputRoot);

        CopyRequired("guardian_stone_ivy_v9.fbx", SourceRoot + "/guardian_stone.fbx");
        CopyRequired("guardian_basecolor.png", SourceRoot + "/guardian_basecolor.png");
        CopyRequired("guardian_emission.png", SourceRoot + "/guardian_emission.png");

        AssetDatabase.DeleteAsset(StablePrefabPath);
        foreach (var path in new[]
        {
            GeneratedRoot + "/StoneMat.mat",
            GeneratedRoot + "/IvyStemMat.mat",
            GeneratedRoot + "/IvyLeafDark.mat",
            GeneratedRoot + "/IvyLeafOlive.mat",
            GeneratedRoot + "/IvyLeafShadow.mat",
            GeneratedRoot + "/GuardianStoneIdle.anim",
            GeneratedRoot + "/GuardianStoneAnimator.controller",
        }) AssetDatabase.DeleteAsset(path);

        var fbxPath = SourceRoot + "/guardian_stone.fbx";
        var albedoPath = SourceRoot + "/guardian_basecolor.png";
        var emissionPath = SourceRoot + "/guardian_emission.png";
        AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(albedoPath, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(emissionPath, ImportAssetOptions.ForceSynchronousImport);

        ConfigureTexture(albedoPath, true);
        ConfigureTexture(emissionPath, false);
        var modelImporter = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
        modelImporter.animationType = ModelImporterAnimationType.None;
        modelImporter.importAnimation = false;
        modelImporter.materialImportMode = ModelImporterMaterialImportMode.None;
        modelImporter.SaveAndReimport();
        AssetDatabase.Refresh();

        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
        var emission = AssetDatabase.LoadAssetAtPath<Texture2D>(emissionPath);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (!albedo || !emission || !model) throw new InvalidOperationException("Promoted source import failed.");

        var stone = MakeMaterial("StoneMat", Color.white, 0.12f);
        stone.mainTexture = albedo;
        stone.SetTexture("_MainTex", albedo);
        stone.EnableKeyword("_EMISSION");
        stone.SetTexture("_EmissionMap", emission);
        stone.SetColor("_EmissionColor", new Color(0.02f, 1.35f, 2.4f, 1f));
        stone.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        var stem = MakeMaterial("IvyStemMat", new Color(0.055f, 0.075f, 0.032f, 1f), 0.05f);
        var dark = MakeMaterial("IvyLeafDark", new Color(0.24f, 0.36f, 0.10f, 1f), 0.04f);
        var olive = MakeMaterial("IvyLeafOlive", new Color(0.34f, 0.48f, 0.14f, 1f), 0.04f);
        var shadow = MakeMaterial("IvyLeafShadow", new Color(0.18f, 0.28f, 0.08f, 1f), 0.03f);
        foreach (var leafMaterial in new[] { dark, olive, shadow })
        {
            var color = leafMaterial.color;
            leafMaterial.shader = Shader.Find("Unlit/Color");
            leafMaterial.color = color;
            leafMaterial.SetColor("_Color", color);
        }

        var root = new GameObject("MeadowsHomesteadingStone");
        var visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);
        var stoneCount = 0;
        var stemCount = 0;
        var leafCount = 0;
        foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            var lowerName = renderer.gameObject.name.ToLowerInvariant();
            Material chosen;
            if (lowerName.Contains("guardianstone") || lowerName.Contains("geometry_0"))
            {
                chosen = stone;
                stoneCount++;
            }
            else if (lowerName.Contains("leaf"))
            {
                var hash = Math.Abs(renderer.gameObject.name.GetHashCode()) % 3;
                chosen = hash == 0 ? dark : hash == 1 ? olive : shadow;
                leafCount++;
            }
            else
            {
                chosen = stem;
                stemCount++;
            }
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++) materials[index] = chosen;
            renderer.sharedMaterials = materials;
        }

        // ── Visual LODGroup: one visual LOD (all renderers) + hard cull region ────────────
        // Soloredis/Daniel guidance (2026-07-16): assign the WHOLE visual parent so every
        // base/ivy/emission child renderer is one LOD group and culls together at 90–120 m.
        // There is no authored lower-poly mesh, so this is a SINGLE visual LOD (LOD0 = every
        // renderer) followed by Unity's implicit cull region — never a duplicated fake LOD and
        // never destructive geometry. Only renderers cull; the additive gameplay root
        // (ZNetView/identity/collider/placement/progression) is a separate object untouched here.
        var lodRenderers = visual.GetComponentsInChildren<Renderer>(true);
        var lodGroup = visual.GetComponent<LODGroup>();
        if (lodGroup == null) lodGroup = visual.AddComponent<LODGroup>();
        lodGroup.fadeMode = LODFadeMode.None;
        lodGroup.animateCrossFading = false;
        lodGroup.SetLODs(new[] { new LOD(CullTransitionHeight(lodGroup, lodRenderers), lodRenderers) });
        lodGroup.RecalculateBounds();
        var lodCount = lodRenderers.Length;

        var clip = new AnimationClip { name = "GuardianStone_Idle", frameRate = 24, wrapMode = WrapMode.Loop };
        var hover = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 0.045f), new Keyframe(2, 0), new Keyframe(3, -0.035f), new Keyframe(4, 0));
        var yaw = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 1.5f), new Keyframe(2, 0), new Keyframe(3, -1.4f), new Keyframe(4, 0));
        ApplyAutoTangents(hover);
        ApplyAutoTangents(yaw);
        clip.SetCurve("", typeof(Transform), "m_LocalPosition.y", hover);
        clip.SetCurve("", typeof(Transform), "localEulerAnglesRaw.y", yaw);
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        AssetDatabase.CreateAsset(clip, GeneratedRoot + "/GuardianStoneIdle.anim");
        var controller = AnimatorController.CreateAnimatorControllerAtPathWithClip(GeneratedRoot + "/GuardianStoneAnimator.controller", clip);
        var animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, StablePrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        if (!prefab) throw new InvalidOperationException("Prefab save failed.");
        if (stoneCount < 1 || stemCount < 20 || leafCount < 100 || !stone.mainTexture || !stone.GetTexture("_EmissionMap") || !prefab.GetComponent<Animator>())
            throw new InvalidOperationException($"Asset wiring assertion failed: stone={stoneCount} stems={stemCount} leaves={leafCount}.");

        // LODGroup structural assertion: exactly one visual LOD whose renderer set is byte-for-byte
        // the complete set of visual renderers (base + ivy + emission), so nothing renders outside
        // the cull group and no renderer is duplicated into a fake lower LOD.
        var savedVisual = prefab.transform.Find("Visual");
        if (savedVisual == null) throw new InvalidOperationException("Saved prefab is missing the 'Visual' presentation child.");
        var savedGroup = savedVisual.GetComponent<LODGroup>();
        if (savedGroup == null) throw new InvalidOperationException("Saved 'Visual' parent has no LODGroup.");
        var savedLods = savedGroup.GetLODs();
        if (savedLods.Length != 1)
            throw new InvalidOperationException($"Expected exactly one visual LOD (no authored lower mesh); found {savedLods.Length}.");
        var allVisualRenderers = new System.Collections.Generic.HashSet<Renderer>(savedVisual.GetComponentsInChildren<Renderer>(true));
        var lod0Renderers = new System.Collections.Generic.HashSet<Renderer>(savedLods[0].renderers);
        lod0Renderers.Remove(null);
        if (!allVisualRenderers.SetEquals(lod0Renderers))
            throw new InvalidOperationException(
                $"LOD0 renderer membership must exactly cover all {allVisualRenderers.Count} visual renderers; " +
                $"LOD0 has {lod0Renderers.Count}.");
        if (allVisualRenderers.Count != lodCount)
            throw new InvalidOperationException($"Renderer count drift: built {lodCount}, saved {allVisualRenderers.Count}.");
        Debug.Log(
            $"[Niflheim/HomesteadStones] LODGroup OK: 1 visual LOD covering {lod0Renderers.Count} renderers " +
            $"(cullHeight={savedLods[0].screenRelativeTransitionHeight:0.0000}); runtime cull target " +
            $"{TargetCullDistanceMeters:0} m at fov {LodFovVerticalDegrees:0}/lodBias {LodBiasReference:0}.");


        var build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = new[]
            {
                StablePrefabPath,
                GeneratedRoot + "/StoneMat.mat",
                GeneratedRoot + "/IvyStemMat.mat",
                GeneratedRoot + "/IvyLeafDark.mat",
                GeneratedRoot + "/IvyLeafOlive.mat",
                GeneratedRoot + "/IvyLeafShadow.mat",
                GeneratedRoot + "/GuardianStoneIdle.anim",
                GeneratedRoot + "/GuardianStoneAnimator.controller",
            },
            addressableNames = new[]
            {
                "assets/sbpr/niflheim/homesteadstones/meadowshomesteadingstone.prefab",
                "assets/sbpr/niflheim/homesteadstones/stonemat.mat",
                "assets/sbpr/niflheim/homesteadstones/ivystemmat.mat",
                "assets/sbpr/niflheim/homesteadstones/ivyleafdark.mat",
                "assets/sbpr/niflheim/homesteadstones/ivyleafolive.mat",
                "assets/sbpr/niflheim/homesteadstones/ivyleafshadow.mat",
                "assets/sbpr/niflheim/homesteadstones/guardianstoneidle.anim",
                "assets/sbpr/niflheim/homesteadstones/guardianstoneanimator.controller",
            },
        };
        var manifest = BuildPipeline.BuildAssetBundles(
            BundleOutputRoot,
            new[] { build },
            BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.StrictMode,
            BuildTarget.StandaloneLinux64);
        var output = Path.Combine(BundleOutputRoot, BundleName);
        if (manifest == null || !File.Exists(output) || new FileInfo(output).Length == 0)
            throw new InvalidOperationException("Stable bundle build failed.");
        Debug.Log($"[Niflheim/HomesteadStones] BUILT {output} ({new FileInfo(output).Length} bytes); stone={stoneCount} stems={stemCount} leaves={leafCount}");
    }

    // ── LOD constants + cull-height helper (mirror of Domain/HomesteadStonePresentation) ──
    // This editor script is never compiled into the runtime plugin (it lives under Assets/Editor
    // and is executed only in the Unity Preview Lab), so it cannot reference the runtime Domain
    // assembly. These four values MUST stay in lockstep with HomesteadStonePresentation:
    //   LodFovVerticalDegrees  ← LodCameraFovVerticalDegrees (vanilla GameCamera.m_fov = 65)
    //   LodBiasReference       ← LodBiasReference            (vanilla default lodBias = 2)
    //   TargetCullDistanceMeters ← TargetCullDistanceMeters  (90–120 m band midpoint)
    //   RuntimeVisualScale     ← VisualScale                 (registrar applies 2× at runtime)
    // A pinning test (HomesteadStonePresentationTests) guards the Domain side; the builder log
    // prints the resulting cull distance so a reviewer can cross-check against real-client frames.
    private const float LodFovVerticalDegrees = 65.0f;
    private const float LodBiasReference = 2.0f;
    private const float TargetCullDistanceMeters = 105.0f;
    private const float RuntimeVisualScale = 2.0f;

    /// <summary>
    /// The screen-relative transition height for the single visual LOD so the group culls at
    /// TargetCullDistanceMeters once the registrar scales the visual by RuntimeVisualScale.
    /// worldSize is the group's authored bounds size × the runtime scale (the vertical extent
    /// dominates for a tall stone). cullHeight = worldSize·lodBias / (2·dist·tan(fov/2)).
    /// </summary>
    private static float CullTransitionHeight(LODGroup group, Renderer[] renderers)
    {
        group.RecalculateBounds();
        // Measure the TRUE authored world size from renderer world-space AABBs. group.size is in
        // the group's LOCAL space and the guardian FBX bakes a large (~100×) import scale into the
        // child transforms, so group.size alone dramatically understates the metres-tall envelope.
        // Renderer.bounds are world-space and already include that import scale at authored (1×) size.
        var b = new Bounds(renderers[0].bounds.center, Vector3.zero);
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        var authoredWorldSize = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        // The registrar scales the presentation child by RuntimeVisualScale (2×) at runtime.
        var runtimeWorldSize = authoredWorldSize * RuntimeVisualScale;
        var halfFovTan = (float)System.Math.Tan(LodFovVerticalDegrees * 0.5f * System.Math.PI / 180.0);
        var height = runtimeWorldSize * LodBiasReference / (2.0f * TargetCullDistanceMeters * halfFovTan);
        return Mathf.Clamp01(height);
    }

    private static void CopyRequired(string filename, string unityPath)
    {
        var source = Path.Combine(RepoSourceRoot, filename);
        if (!File.Exists(source)) throw new FileNotFoundException("Promoted source file missing", source);
        var destination = Path.Combine(ProjectRoot, unityPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.Copy(source, destination, true);
    }

    private static void ConfigureTexture(string path, bool srgb)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        HomesteadTextureImportPolicy.Apply(importer, srgb);
        importer.SaveAndReimport();
        HomesteadTextureImportPolicy.AssertMatches(path, srgb);
    }

    private static void ApplyAutoTangents(AnimationCurve curve)
    {
        for (var index = 0; index < curve.keys.Length; index++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curve, index, AnimationUtility.TangentMode.Auto);
            AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.Auto);
        }
    }
}
