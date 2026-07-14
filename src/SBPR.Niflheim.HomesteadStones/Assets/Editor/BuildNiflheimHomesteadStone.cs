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
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = srgb;
        importer.mipmapEnabled = true;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.SaveAndReimport();
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
