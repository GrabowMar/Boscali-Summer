using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Standalone Unity 2022.3 / URP 14 editor entry point; never imports the mod DLL.
public static class BuildWorldDetailAssets
{
    private const string Root = "Assets/WorldDetail/", Source = Root + "WorldDetail.shader";
    private const string VariantsPath = Root + "WorldDetail.shadervariants";
    private const string OpaquePath = Root + "Opaque.mat", LeafPath = Root + "Leaf.mat";
    private const string BundlePath = "BundleOutput/world-detail.bundle";
    private const int BundleLimit = 512 * 1024;

    public static void Run()
    {
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureScratchPipeline();
            string meshChecks = BoscaliSummer.Modules.Immersion.Visuals.WorldDetailMeshCheck.Run();
            Shader shader = CheckShader(AssetDatabase.LoadAssetAtPath<Shader>(Source));
            KeepMaterial(shader, OpaquePath, false);
            KeepMaterial(shader, LeafPath, true);
            int combinations = KeepVariants(shader);
            AssetDatabase.SaveAssets();

            Directory.CreateDirectory("BundleOutput");
            var build = new AssetBundleBuild
            {
                assetBundleName = "world-detail.bundle",
                assetNames = new[] { Source, VariantsPath, OpaquePath, LeafPath }
            };
            if (BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build },
                    BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                    BuildTarget.StandaloneWindows64) == null) throw new Exception("World detail bundle build returned no manifest.");
            CheckShader(shader); // Build-time variant compilation must not hide errors.
            long bytes = new FileInfo(BundlePath).Length;
            if (bytes <= 0 || bytes > BundleLimit) throw new Exception("World detail bundle exceeds its 512 KiB cap: " + bytes);
            CheckBundle(combinations);
            File.WriteAllText("result.txt", "PASS: Boscali/WorldDetail compiled; one forward pass, no shadow/depth-normal pass; " +
                combinations + " requested instancing/leaf/main-light-shadow/fog combinations retained in its variant collection; bundle " +
                bytes + " bytes. Compile/bundle checks only; no rendered, live performance or multiplayer acceptance.\n" + meshChecks);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("result.txt", "FAIL: " + error);
            EditorApplication.Exit(1);
        }
    }

    private static void ConfigureScratchPipeline()
    {
        const string rendererPath = Root + "BuildRenderer.asset", pipelinePath = Root + "BuildPipeline.asset";
        UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, rendererPath);
        }
        UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, pipelinePath);
        }
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;

        // These settings apply only to this disposable authoring project. Runtime-built
        // materials cannot keep shader_feature/instancing variants through scene analysis.
        var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        SerializedProperty instancing = graphics.FindProperty("m_InstancingStripping");
        if (instancing == null) throw new Exception("Unity's instancing stripping setting was not found.");
        instancing.intValue = 2; // Unity 2022.3: StripUnused = 0, StripAll = 1, KeepAll = 2.
        SerializedProperty fog = graphics.FindProperty("m_FogStripping");
        if (fog == null) throw new Exception("Unity's fog stripping setting was not found.");
        fog.intValue = 1; // Custom: asset bundles must also support the game's runtime fog modes.
        foreach (string name in new[] { "m_FogKeepLinear", "m_FogKeepExp", "m_FogKeepExp2" })
        {
            SerializedProperty keep = graphics.FindProperty(name);
            if (keep == null) throw new Exception("Unity's fog mode setting was not found: " + name);
            keep.boolValue = true;
        }
        graphics.ApplyModifiedPropertiesWithoutUndo();

        // URP 14's GlobalSettings type is internal. Pin its serialized settings rather
        // than changing the game's pipeline or relying on a player-side included shader.
        const string globalPath = Root + "BuildGlobalSettings.asset";
        RenderPipelineGlobalSettings global = AssetDatabase.LoadAssetAtPath<RenderPipelineGlobalSettings>(globalPath);
        if (global == null)
        {
            Type type = typeof(UniversalRenderPipelineAsset).Assembly.GetType(
                "UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings", true);
            global = (RenderPipelineGlobalSettings)ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(global, globalPath);
        }
        var settings = new SerializedObject(global);
        SerializedProperty stripVariants = settings.FindProperty("m_StripUnusedVariants");
        SerializedProperty stripPost = settings.FindProperty("m_StripUnusedPostProcessingVariants");
        if (stripVariants == null || stripPost == null) throw new Exception("URP 14 shader stripping settings were not found.");
        stripVariants.boolValue = false;
        stripPost.boolValue = false;
        settings.ApplyModifiedPropertiesWithoutUndo();
        GraphicsSettings.RegisterRenderPipelineSettings<UniversalRenderPipeline>(global);
    }

    private static void KeepMaterial(Shader shader, string path, bool leaf)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.shaderKeywords = Array.Empty<string>();
        material.enableInstancing = true;
        if (leaf) material.EnableKeyword("_LEAF_CUTOUT");
        EditorUtility.SetDirty(material);
    }

    private static int KeepVariants(Shader shader)
    {
        if (!shader.keywordSpace.FindKeyword("_LEAF_CUTOUT").isValid ||
            !shader.keywordSpace.FindKeyword("_MAIN_LIGHT_SHADOWS").isValid)
            throw new Exception("World detail leaf/main-light keywords were not declared.");
        var shadows = new List<string> { "" };
        foreach (string keyword in new[] { "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE", "_MAIN_LIGHT_SHADOWS_SCREEN" })
            if (shader.keywordSpace.FindKeyword(keyword).isValid) shadows.Add(keyword);
        ShaderVariantCollection variants = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(VariantsPath);
        if (variants == null)
        {
            variants = new ShaderVariantCollection();
            AssetDatabase.CreateAsset(variants, VariantsPath);
        }
        variants.Clear();
        for (int instanced = 0; instanced < 2; instanced++)
        for (int leaf = 0; leaf < 2; leaf++)
        foreach (string shadow in shadows)
        foreach (string fog in new[] { "", "FOG_LINEAR", "FOG_EXP", "FOG_EXP2" })
        {
            if (fog.Length != 0 && !shader.keywordSpace.FindKeyword(fog).isValid)
                throw new Exception("World detail fog keyword was not declared: " + fog);
            var keywords = new List<string>(4);
            if (instanced != 0) keywords.Add("INSTANCING_ON");
            if (leaf != 0) keywords.Add("_LEAF_CUTOUT");
            if (shadow.Length != 0) keywords.Add(shadow);
            if (fog.Length != 0) keywords.Add(fog);
            var variant = new ShaderVariantCollection.ShaderVariant(shader, PassType.ScriptableRenderPipeline, keywords.ToArray());
            if (!variants.Add(variant)) throw new Exception("World detail variant was not retained: " + string.Join(" ", keywords));
        }
        EditorUtility.SetDirty(variants);
        return variants.variantCount;
    }

    private static Shader CheckShader(Shader shader)
    {
        if (shader == null || shader.name != "Boscali/WorldDetail") throw new Exception("World detail shader is missing or renamed.");
        if (ShaderUtil.ShaderHasError(shader))
        {
            foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader)) Debug.LogError(message.message);
            throw new Exception("World detail shader compilation failed.");
        }
        var material = new Material(shader);
        try
        {
            if (material.passCount != 1) throw new Exception("World detail must have exactly one shader pass.");
            string lightMode = shader.FindPassTagValue(0, new ShaderTagId("LightMode")).name;
            if (lightMode != "UniversalForward" && lightMode != "UniversalForwardOnly")
                throw new Exception("World detail's sole pass must be a URP forward pass: " + lightMode);
        }
        finally { Object.DestroyImmediate(material); }
        return shader;
    }

    private static void CheckBundle(int combinations)
    {
        AssetBundle bundle = AssetBundle.LoadFromFile(Path.GetFullPath(BundlePath));
        if (bundle == null) throw new Exception("World detail bundle could not be reloaded.");
        try
        {
            Shader[] shaders = bundle.LoadAllAssets<Shader>();
            if (shaders.Length != 1) throw new Exception("World detail bundle must contain exactly one shader.");
            CheckShader(shaders[0]);
            ShaderVariantCollection variants = bundle.LoadAsset<ShaderVariantCollection>(VariantsPath);
            if (variants == null || variants.shaderCount != 1 || variants.variantCount != combinations)
                throw new Exception("World detail bundle's variant collection changed during packaging.");
            if (bundle.LoadAllAssets<Texture>().Length != 0) throw new Exception("World detail textures must stay outside this shader bundle.");
        }
        finally { bundle.Unload(true); }
    }
}
