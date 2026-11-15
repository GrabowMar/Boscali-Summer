using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Standalone editor entry point used by Build-SurfaceAssets.ps1; no mod/test DLL needed.
public static class BuildSurfaceAssets
{
    private const string Root = "Assets/Surfaces/";
    private static readonly string[] Assets = { Root + "Ocean.shader", Root + "TerrainDetail.shader", Root + "terrain-detail.png" };
    private const int BundleLimit = 2 * 1024 * 1024;

    public static void Run()
    {
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var detail = ImportDetail();
            var ocean = CheckShader(Assets[0], "Boscali/Ocean");
            var terrain = CheckShader(Assets[1], "Boscali/TerrainDetail");
            bool skip = Array.IndexOf(Environment.GetCommandLineArgs(), "-skipSurfaceRenderCheck") >= 0;
            string checks = skip ? "Surface render checks SKIPPED (-SkipRenderCheck)." :
                CheckTerrain(terrain, detail) + "\n" + OceanReflectionCheck.Run(ocean);
            Directory.CreateDirectory("BundleOutput");
            var build = new AssetBundleBuild { assetBundleName = "surfaces.bundle", assetNames = Assets };
            if (BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64) == null) throw new Exception("Surface bundle build returned no manifest.");
            CheckShader(Assets[0], ocean.name);
            CheckShader(Assets[1], terrain.name);
            long bytes = new FileInfo("BundleOutput/surfaces.bundle").Length;
            if (bytes > BundleLimit) throw new Exception("Surface bundle exceeds 2 MiB: " + bytes + " bytes.");
            File.WriteAllText("result.txt", "PASS: two surface shaders compiled; packed detail 256x256 linear DXT5, 9 mips, Repeat/Trilinear/aniso 1; bundle " + bytes + " bytes.\n" + checks);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("result.txt", "FAIL: " + error);
            EditorApplication.Exit(1);
        }
    }

    private static Texture2D ImportDetail()
    {
        var importer = AssetImporter.GetAtPath(Assets[2]) as TextureImporter;
        if (importer == null) throw new Exception("Packed terrain detail PNG was not imported.");
        importer.textureType = TextureImporterType.Default; // GB are packed raw slopes, not an RGB normal map.
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = false;
        importer.filterMode = FilterMode.Trilinear;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.anisoLevel = 1;
        importer.maxTextureSize = 256;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.isReadable = false;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.crunchedCompression = false;
        importer.compressionQuality = 100;
        var platform = importer.GetPlatformTextureSettings("Standalone");
        platform.overridden = true;
        platform.maxTextureSize = 256;
        platform.format = TextureImporterFormat.DXT5;
        platform.textureCompression = TextureImporterCompression.CompressedHQ;
        platform.crunchedCompression = false;
        platform.compressionQuality = 100;
        importer.SetPlatformTextureSettings(platform);
        importer.SaveAndReimport();
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Assets[2]);
        if (texture == null || texture.width != 256 || texture.height != 256 || texture.mipmapCount != 9 || texture.isDataSRGB ||
            texture.format != TextureFormat.DXT5 || texture.filterMode != FilterMode.Trilinear || texture.wrapMode != TextureWrapMode.Repeat || texture.anisoLevel != 1)
            throw new Exception("Packed terrain detail import does not match its linear 256x256 DXT5/mip/filter contract.");
        return texture;
    }

    private static Shader CheckShader(string path, string expectedName)
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        if (shader == null || shader.name != expectedName) throw new Exception("Surface shader missing or renamed: " + path);
        if (ShaderUtil.ShaderHasError(shader))
        {
            foreach (var message in ShaderUtil.GetShaderMessages(shader)) Debug.LogError(path + ": " + message.message);
            throw new Exception("Surface shader compilation failed: " + path);
        }
        var material = new Material(shader);
        try
        {
            if (material.passCount != 1) throw new Exception("Surface shader must stay within one render pass: " + path);
        }
        finally { Object.DestroyImmediate(material); }
        return shader;
    }

    private static string CheckTerrain(Shader shader, Texture2D detail)
    {
        if (!shader.isSupported || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBFloat))
            throw new Exception("Terrain fixture requires a supported shader and ARGBFloat render target; use -SkipRenderCheck only for compile-only builds.");
        const int size = 128;
        var cameraObject = new GameObject("SurfaceCheck.Camera");
        var planeObject = new GameObject("SurfaceCheck.Ground");
        var mesh = new Mesh { name = "SurfaceCheck.Plane" };
        var material = new Material(shader);
        var target = new RenderTexture(size, size, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var read = new Texture2D(size, size, TextureFormat.RGBAFloat, false, true);
        RenderTexture saved = RenderTexture.active;
        try
        {
            mesh.vertices = new[] { new Vector3(-4, 0, -4), new Vector3(-4, 0, 4), new Vector3(4, 0, 4), new Vector3(4, 0, -4) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.RecalculateBounds();
            planeObject.layer = 30;
            planeObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = planeObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.3f, 0.45f, 0.25f, 1);
            camera.cullingMask = 1 << planeObject.layer;
            camera.orthographic = true;
            camera.orthographicSize = 5;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 200;
            camera.allowHDR = true;
            camera.allowMSAA = false;
            camera.transform.position = new Vector3(0, 6, -6);
            camera.transform.LookAt(Vector3.zero);
            camera.targetTexture = target;
            material.SetTexture("_TerrainDetail", detail);
            material.SetVector("_DetailFade", new Vector4(50, 100, 0.1f, 1.5f));
            material.SetVector("_SunDir", new Vector4(0.2f, 1, 0.3f, 0));
            material.SetColor("_SunColor", Color.white);
            renderer.enabled = false;
            Color[] baseline = Render(camera, target, read);
            renderer.enabled = true;
            material.SetFloat("_DetailStrength", 0);
            RequireIdentity(baseline, Render(camera, target, read), "zero strength");
            material.SetFloat("_DetailStrength", 1);
            Color[] active = Render(camera, target, read);
            double change = MeanDifference(baseline, active);
            if (change < 0.00001) throw new Exception("Terrain detail did not affect the fixture; mean RGB change " + change);
            SavePreview("terrain-detail-native.png", baseline, size);
            SavePreview("terrain-detail-active.png", active, size);
            material.SetFloat("_FogDensity", 10);
            RequireIdentity(baseline, Render(camera, target, read), "dense fog");
            material.SetFloat("_FogDensity", 0);
            material.SetVector("_DetailFade", new Vector4(0.1f, 0.2f, 0.1f, 1.5f));
            RequireIdentity(baseline, Render(camera, target, read), "past distance fade");
            material.SetVector("_DetailFade", new Vector4(50, 100, 0.00001f, 0.00002f));
            RequireIdentity(baseline, Render(camera, target, read), "subpixel footprint fade");
            return "Terrain fixture PASS: finite output; active detail changes pixels (mean RGB " + change.ToString("G6") + "); zero strength, dense fog, distance and footprint fades preserve the native colour. Fixture only; no live performance claim.";
        }
        finally
        {
            RenderTexture.active = saved;
            Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(planeObject);
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(read);
            target.Release();
            Object.DestroyImmediate(target);
        }
    }

    private static Color[] Render(Camera camera, RenderTexture target, Texture2D read)
    {
        camera.Render();
        RenderTexture.active = target;
        read.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        read.Apply(false, false);
        Color[] pixels = read.GetPixels();
        foreach (Color value in pixels)
            if (!Finite(value.r) || !Finite(value.g) || !Finite(value.b) || !Finite(value.a)) throw new Exception("Non-finite terrain fixture pixel.");
        return pixels;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static double MeanDifference(Color[] a, Color[] b)
    {
        double change = 0;
        for (int i = 0; i < a.Length; i++) change += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
        return change / (a.Length * 3);
    }

    private static void RequireIdentity(Color[] baseline, Color[] actual, string reason)
    {
        for (int i = 0; i < baseline.Length; i++)
            if (Math.Abs(baseline[i].r - actual[i].r) > 0.0002f || Math.Abs(baseline[i].g - actual[i].g) > 0.0002f ||
                Math.Abs(baseline[i].b - actual[i].b) > 0.0002f || Math.Abs(baseline[i].a - actual[i].a) > 0.0002f)
                throw new Exception("Terrain detail did not preserve colour at " + reason + ", pixel " + i);
    }

    private static void SavePreview(string path, Color[] pixels, int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        try { texture.SetPixels(pixels); texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG()); }
        finally { Object.DestroyImmediate(texture); }
    }
}
