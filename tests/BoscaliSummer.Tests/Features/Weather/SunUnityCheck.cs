#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections;
using System.IO;
using System.Text;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.Immersion.Visuals;
using BoscaliSummer.Modules.Weather.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// Isolated production-source URP check. It proves fixture behavior, not the game's full camera stack or live performance.
public sealed class SunUnityCheck : MonoBehaviour
{
    private readonly StringBuilder log = new StringBuilder();
    private readonly SunDisc disc = new SunDisc();
    private readonly SunGlare glare = new SunGlare();
    private readonly SunShaftsPass shafts = new SunShaftsPass();
    private readonly RenderPipeline.StandardRequest frameRequest = new RenderPipeline.StandardRequest();
    private readonly RenderPipeline.StandardRequest auxiliaryRequest = new RenderPipeline.StandardRequest();
    private new Camera camera;
    private Camera unrelated;
    private LevelInfo level;
    private RenderTexture target;
    private Texture2D cloudCover;
    private Material nativeDiscMaterial;
    private float transmission = 1;
    private bool show = true;
    private int assertions;
    private int worldFrames, lateTicks;

#if UNITY_EDITOR
    public static void Build()
    {
        try
        {
            foreach (string name in new[] { "SunRenderer.asset", "SunPipeline.asset", "SunDisc.mat", "SunOccluder.mat", "SunPost.asset" })
                AssetDatabase.DeleteAsset("Assets/Resources/" + name);
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            if (renderer.postProcessData == null || renderer.postProcessData.shaders.bloomPS == null)
                throw new Exception("URP post-processing resources missing");
            AssetDatabase.CreateAsset(renderer, "Assets/Resources/SunRenderer.asset");
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.supportsCameraDepthTexture = true;
            pipeline.supportsCameraOpaqueTexture = true;
            pipeline.supportsHDR = true;
            pipeline.msaaSampleCount = 1;
            AssetDatabase.CreateAsset(pipeline, "Assets/Resources/SunPipeline.asset");
            GraphicsSettings.renderPipelineAsset = pipeline;
            QualitySettings.renderPipeline = pipeline;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.graphicsJobs = false;
            var sunMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            sunMaterial.EnableKeyword("_EMISSION");
            sunMaterial.SetColor("_BaseColor", Color.black);
            sunMaterial.SetColor("_EmissionColor", Color.white * 1000);
            AssetDatabase.CreateAsset(sunMaterial, "Assets/Resources/SunDisc.mat");
            var occluderMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            occluderMaterial.SetColor("_BaseColor", new Color(.028f, .034f, .04f));
            AssetDatabase.CreateAsset(occluderMaterial, "Assets/Resources/SunOccluder.mat");
            var post = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(post, "Assets/Resources/SunPost.asset");
            Bloom bloom = post.Add<Bloom>();
            bloom.intensity.Override(.5f); bloom.threshold.Override(1.3f); bloom.scatter.Override(.55f);
            Tonemapping tone = post.Add<Tonemapping>(); tone.mode.Override(TonemappingMode.ACES);
            AssetDatabase.AddObjectToAsset(bloom, post);
            AssetDatabase.AddObjectToAsset(tone, post);
            post.components.Clear(); post.components.Add(bloom); post.components.Add(tone);
            EditorUtility.SetDirty(post);
            foreach (string name in new[] { "SunShafts", "SunDisc" })
            {
                Shader shader = Resources.Load<Shader>(name);
                if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("Production shader missing or invalid: " + name);
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Offline sun check").AddComponent<SunUnityCheck>();
            EditorSceneManager.SaveScene(scene, "Assets/check.unity");
            AssetDatabase.SaveAssets();
            if (!AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Resources/SunPost.asset").TryGet<Bloom>(out var persistedBloom) || persistedBloom.intensity.value != .5f)
                throw new Exception("Bloom profile failed to persist");
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/check.unity" }, "Player/SunCheck.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Player build failed: " + report.summary.result);
            File.WriteAllText("build-result.txt", "PASS: production sun presenters and shader built in isolated URP 14.0.12 player.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { File.WriteAllText("build-result.txt", "FAIL: " + error); Debug.LogException(error); EditorApplication.Exit(1); }
    }
#endif

    private void LateUpdate()
    {
        if (level == null || camera == null) return;
        lateTicks++;
        disc.Tick(level, camera, transmission, 1f / 60);
        glare.Tick(level, camera, show, 1f / 60, transmission);
        shafts.Tick(level, camera, show, transmission);
        frameRequest.destination = target;
        RenderPipeline.SubmitRenderRequest(camera, frameRequest);
        if (unrelated != null)
        {
            auxiliaryRequest.destination = unrelated.targetTexture;
            RenderPipeline.SubmitRenderRequest(unrelated, auxiliaryRequest);
        }
    }

    private IEnumerator Start()
    {
        bool failed = false;
        IEnumerator run = Run();
        while (true)
        {
            bool next;
            try { next = run.MoveNext(); }
            catch (Exception error) { log.AppendLine("FAIL: " + error); Debug.LogException(error); failed = true; break; }
            if (!next) break;
            yield return run.Current;
        }
        shafts.Dispose(); glare.Release(); SunGlare.ReleaseData(); disc.Release();
        RenderPipelineManager.beginCameraRendering -= BindCloudForCamera;
        string result = (failed ? "FAIL: " : "PASS: " + assertions + " assertions. ") +
            "Exact production SunShafts shader + SunShaftsPass, SunGlare, SunDisc in real offline URP. " +
            "Native LevelInfo/physics masks/global origin/bundle lookup substituted; no game boot, deployment, or performance claim.\n" + log;
        File.WriteAllText("result.txt", result);
        Application.Quit(failed ? 1 : 0);
    }

    private IEnumerator Run()
    {
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        log.AppendLine("GPU " + SystemInfo.graphicsDeviceName + " | " + SystemInfo.graphicsDeviceType);
        Check(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset, "Real URP active");
        ShaderTests();
        target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGBHalf) { name = "SunCheckFrame", antiAliasing = 1 };
        target.Create();
        camera = new GameObject("Flight Camera").AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.position = new Vector3(0, 800, 0);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.16f, .31f, .5f);
        camera.nearClipPlane = .1f; camera.farClipPlane = 50000;
        camera.fieldOfView = 60; camera.allowHDR = true; camera.targetTexture = target;
        var data = camera.GetUniversalAdditionalCameraData();
        data.requiresDepthTexture = true; data.renderPostProcessing = true;
        var post = new GameObject("Native-like bloom fixture").AddComponent<Volume>();
        post.isGlobal = true; post.profile = Resources.Load<VolumeProfile>("SunPost");
        Check(post.profile.TryGet<Bloom>(out var bloom) && bloom.intensity.value == .5f, "Saved native-like bloom settings are active");
        level = new GameObject("Native LevelInfo substitute").AddComponent<LevelInfo>();
        level.sun = new GameObject("Directional Sun").AddComponent<Light>();
        level.sun.type = LightType.Directional; level.sun.intensity = 1;
        level.sunObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        level.sunObject.name = "Native-size 0.53 degree sun disc";
        Destroy(level.sunObject.GetComponent<Collider>());
        nativeDiscMaterial = new Material(Resources.Load<Material>("SunDisc"));
        level.sunObject.GetComponent<MeshRenderer>().sharedMaterial = nativeDiscMaterial;
        level.sunObject.transform.localScale = Vector3.one * (2 * 40000 * Mathf.Tan(SunDiscMath.AngularRadiusDeg * Mathf.Deg2Rad));
        cloudCover = Cloud(0);
        Shader.SetGlobalTexture(CloudLowRes.CoverId, cloudCover);
        RenderPipelineManager.beginCameraRendering += BindCloudForCamera;
        SetSun(45, .69f, .64f);
        yield return Settle(45);
        Save("startup-diagnostic.png");
        Vector4 diagnosticClip = camera.projectionMatrix * camera.worldToCameraMatrix * new Vector4(-level.sun.transform.forward.x, -level.sun.transform.forward.y, -level.sun.transform.forward.z, 0);
        log.AppendLine("Startup frame=" + Time.frameCount + " shafts=" + shafts.ExecutedFrame + " world renders=" + worldFrames + " late ticks=" + lateTicks + " sun clip=" + diagnosticClip + " viewport=" + camera.WorldToViewportPoint(level.sunObject.transform.position));
        Check(shafts.ExecutedFrame >= Time.frameCount - 1, "Production shafts pass draws on flight camera");
        Check(glare.Intensity > .05f, "Production glare draws after warmup");
        Check(level.sunObject.GetComponent<MeshRenderer>().sharedMaterial != nativeDiscMaterial &&
            level.sunObject.GetComponent<MeshRenderer>().sharedMaterial.shader.name == "Boscali/SunDisc", "Disc owns an isolated unlit material on the native mesh");
        Save("clear-midday.png");
        Color[] still = Pixels(target);
        yield return Settle(32);
        Color[] later = Pixels(target);
        float stillDelta = MaxDelta(still, later);
        log.AppendLine("Still frame max linear RGB delta " + stillDelta);
        Check(stillDelta < .0002f, "Still midday frames are identical within half-float precision; no time-driven blinking");
        glare.Gain = 0;
        yield return Settle(3);
        Color[] withoutGlare = Pixels(target);
        glare.Gain = 1;
        yield return Settle(3);
        Color[] withGlare = Pixels(target);
        float glareDelta = MaxDelta(withoutGlare, withGlare);
        log.AppendLine("Actual URP glare Gain0-to-1 max linear RGB delta " + glareDelta);
        Check(glareDelta > .001f, "Production lens flare makes a measurable pixel contribution through the real URP post chain");
        bloom.intensity.value = 0;
        yield return Settle(3);
        Color[] withoutBloom = Pixels(target);
        bloom.intensity.value = .5f;
        yield return Settle(3);
        Color[] withBloom = Pixels(target);
        float bloomDelta = MaxDelta(withoutBloom, withBloom);
        log.AppendLine("Actual URP bloom0-to-.5 max linear RGB delta " + bloomDelta);
        Check(bloomDelta > .005f, "Native-like bloom makes a measurable pixel contribution around the HDR sun");
        later = withBloom;
        nativeDiscMaterial.SetColor("_EmissionColor", Color.white * 1000000);
        yield return Settle(3);
        Check(MaxDelta(later, Pixels(target)) < .0002f, "Native cached emission rewrite cannot change the rendered sun");
        float noonBlue = disc.Shown.b / Mathf.Max(.0001f, disc.Shown.r);
        SetSun(2, .69f, .58f);
        camera.backgroundColor = new Color(.13f, .15f, .19f);
        yield return Settle(35);
        Save("warm-low-sun.png");
        Check(disc.Shown.b / Mathf.Max(.0001f, disc.Shown.r) < noonBlue * .5f, "Low sun reddens through the production air model");

        SetSun(35, .69f, .64f);
        camera.backgroundColor = new Color(.16f, .31f, .5f);
        SetCloud(.5f); transmission = .75f;
        yield return Settle(45); Save("cloud-edge.png");
        transmission = 0; SetCloud(1);
        yield return Settle(45); Save("full-cloud.png");
        Check(glare.Intensity < .0001f, "Full cloud extinguishes glare while GPU alpha extinguishes the disc");
        transmission = 1; SetCloud(0);
        yield return Settle(45);
        var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.name = "Fixture ridge depth occluder"; blocker.layer = 29;
        blocker.transform.position = camera.transform.position - level.sun.transform.forward * 80;
        blocker.transform.localScale = Vector3.one * 16;
        blocker.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("SunOccluder");
        yield return new WaitForSeconds(.16f);
        yield return Settle(25); Save("depth-occluder.png");
        Check(glare.Intensity < .0001f, "Statics terrain raycast extinguishes production glare");
        Destroy(blocker); yield return Settle(35);
        int previousDraw = shafts.ExecutedFrame;
        SetSun(35, 1.6f, .64f);
        yield return Settle(35); Save("offscreen-out.png");
        Check(shafts.ExecutedFrame <= previousDraw + 1, "Far offscreen sun stops shaft draws");
        SetSun(35, .97f, .64f);
        yield return Settle(35); Save("offscreen-edge.png");
        Check(shafts.ExecutedFrame >= Time.frameCount - 1, "Shafts resume at viewport edge");
        SetSun(35, .69f, .64f);
        yield return Settle(35); Save("offscreen-return.png");

        data.renderPostProcessing = false;
        for (int index = 0; index < 2; index++)
        {
            var overlay = new GameObject(index == 0 ? "cockpitRenderer" : "postProcessingRenderer").AddComponent<Camera>();
            overlay.transform.SetParent(camera.transform, false);
            overlay.nearClipPlane = index == 0 ? .05f : 4;
            overlay.farClipPlane = index == 0 ? 1000 : 10;
            overlay.fieldOfView = camera.fieldOfView;
            overlay.cullingMask = 1 << 30;
            var overlayData = overlay.GetUniversalAdditionalCameraData();
            overlayData.renderType = CameraRenderType.Overlay;
            overlayData.renderPostProcessing = index == 1;
            data.cameraStack.Add(overlay);
        }
        yield return Settle(35); Save("camera-stack.png");
        Color[] stackStill = Pixels(target);
        yield return Settle(16);
        Check(MaxDelta(stackStill, Pixels(target)) < .0002f, "World plus two parented overlay cameras keep a still sun stable");

        // Render an unrelated base camera between valid frames: global flare state must not blink the flight view.
        unrelated = new GameObject("Unrelated map camera").AddComponent<Camera>();
        unrelated.enabled = false;
        unrelated.depth = camera.depth + 5; unrelated.cullingMask = 0;
        unrelated.targetTexture = new RenderTexture(32, 32, 16); unrelated.targetTexture.Create();
        Color[] beforeOther = Pixels(target);
        yield return Settle(8);
        Check(MaxDelta(beforeOther, Pixels(target)) < .0002f, "Unrelated camera renders do not blink the flight camera sun");
        var activeFlare = FindObjectOfType<LensFlareComponentSRP>();
        Check(activeFlare != null && activeFlare.enabled, "Flare stays registered while auxiliary cameras suppress intensity");
        show = false;
        disc.Release();
        Check(level.sunObject.GetComponent<MeshRenderer>().sharedMaterial == nativeDiscMaterial, "Release restores the original native sun material");
    }

    private void BindCloudForCamera(ScriptableRenderContext context, Camera rendering)
    {
        if (rendering == camera) worldFrames++;
        Shader.SetGlobalFloat("_BoscaliCloudCoverFrame", rendering == camera ? 1 : 0);
    }

    private void ShaderTests()
    {
        var material = new Material(Resources.Load<Shader>("SunShafts"));
        Check(material.passCount == 3, "Production shader has mask, blur and additive composite passes");
        var mask = new RenderTexture(256, 144, 0, RenderTextureFormat.ARGBFloat); mask.Create();
        var blurred = new RenderTexture(256, 144, 0, RenderTextureFormat.ARGBFloat); blurred.Create();
        var clear = Cloud(0); var opaque = Cloud(1);
        var depth = new Texture2D(2, 2, TextureFormat.RFloat, false, true);
        // UnityCG's Linear01Depth uses these globals; a raw far depth is 0 on reversed-Z hardware.
        float rawFar = SystemInfo.usesReversedZBuffer ? 0 : 1;
        depth.SetPixels(new[] { new Color(rawFar, 0, 0), new Color(rawFar, 0, 0), new Color(rawFar, 0, 0), new Color(rawFar, 0, 0) }); depth.Apply();
        Shader.SetGlobalVector("_ZBufferParams", SystemInfo.usesReversedZBuffer ? new Vector4(499999, 1, .999998f, .000002f) : new Vector4(-499999, 500000, -.999998f, 1));
        Shader.SetGlobalTexture("_CameraDepthTexture", depth);
        Shader.SetGlobalVector("_ShaftSun", new Vector4(.6f, .6f, 16f / 9, .6f));
        Shader.SetGlobalVector("_ShaftParams", new Vector4(1, 1, .9f, 1));
        Shader.SetGlobalColor("_ShaftTint", Color.white);
        Shader.SetGlobalFloat("_BoscaliCloudCoverFrame", 1);
        Shader.SetGlobalTexture(CloudLowRes.CoverId, clear);
        Graphics.Blit(Texture2D.blackTexture, mask, material, 0);
        float clearLight = Mean(Pixels(mask));
        Check(clearLight > .001f, "Zero cloud alpha permits shaft source");
        Shader.SetGlobalTexture(CloudLowRes.CoverId, opaque);
        Graphics.Blit(Texture2D.blackTexture, mask, material, 0);
        float blockedLight = Mean(Pixels(mask));
        Check(blockedLight < .00001f, "One cloud alpha blocks shaft source");
        Shader.SetGlobalFloat("_BoscaliCloudCoverFrame", 0);
        Graphics.Blit(Texture2D.blackTexture, mask, material, 0);
        Check(Mean(Pixels(mask)) >= clearLight * .99f, "Stale cloud texture is ignored when this camera has no current cover");
        Shader.SetGlobalFloat("_BoscaliCloudCoverFrame", 1);
        float rawNear = SystemInfo.usesReversedZBuffer ? 1 : 0;
        depth.SetPixels(new[] { new Color(rawNear, 0, 0), new Color(rawNear, 0, 0), new Color(rawNear, 0, 0), new Color(rawNear, 0, 0) }); depth.Apply();
        Shader.SetGlobalTexture(CloudLowRes.CoverId, clear);
        Graphics.Blit(Texture2D.blackTexture, mask, material, 0);
        Check(Mean(Pixels(mask)) < .00001f, "Foreground depth blocks shaft source");
        Shader.SetGlobalTexture(CloudLowRes.CoverId, clear);
        Shader.SetGlobalVector("_ShaftSun", new Vector4(2, .5f, 16f / 9, .6f));
        Shader.SetGlobalVector("_ShaftParams", new Vector4(1, 1, 1, 1));
        Graphics.Blit(Texture2D.whiteTexture, blurred, material, 1);
        Color[] blur = Pixels(blurred);
        // Rays toward x=2 spend at least half their taps outside the viewport. Those taps must be black, never clamp to white.
        Check(Mean(blur) < .7f, "Radial blur rejects offscreen taps instead of stretching border texels");
        for (int i = 0; i < blur.Length; i++)
            if (float.IsNaN(blur[i].r) || float.IsInfinity(blur[i].r)) throw new Exception("Nonfinite radial blur output");
        Check(true, "Radial blur output stays finite");
        var sunMaterial = new Material(Resources.Load<Shader>("SunDisc"));
        sunMaterial.SetColor("_EmissionColor", Color.white);
        sunMaterial.SetFloat("_SunCloudMask", 1);
        sunMaterial.SetFloat("_SunFallbackTransmission", 1);
        var cloudEdge = Cloud(.5f);
        Shader.SetGlobalTexture(CloudLowRes.CoverId, cloudEdge);
        Shader.SetGlobalFloat("_BoscaliCloudCoverFrame", 1);
        Clear(mask, Color.clear);
        Graphics.Blit(Texture2D.blackTexture, mask, sunMaterial, 0);
        Color[] discPixels = Pixels(mask);
        float left = discPixels[72 * 256 + 64].r, right = discPixels[72 * 256 + 230].r;
        log.AppendLine("Disc per-pixel cloud extinction, left=" + left + " right=" + right);
        Check(left > .9f && right < .00001f, "Production sun shader preserves the clear limb and extinguishes the cloudy limb independently");
        var edgeImage = Read(mask, TextureFormat.RGBA32);
        File.WriteAllBytes("shader-disc-cloud-edge.png", edgeImage.EncodeToPNG()); Destroy(edgeImage);
        Shader.SetGlobalTexture(CloudLowRes.CoverId, opaque);
        Clear(mask, Color.clear);
        Graphics.Blit(Texture2D.blackTexture, mask, sunMaterial, 0);
        Check(Mean(Pixels(mask)) < .00001f, "Production sun shader fully extinguishes behind opaque cloud");
        Shader.SetGlobalFloat("_BoscaliCloudCoverFrame", 0);
        Clear(mask, Color.clear);
        Graphics.Blit(Texture2D.blackTexture, mask, sunMaterial, 0);
        Check(Mean(Pixels(mask)) > .99f, "Production sun shader ignores stale cloud cover for auxiliary cameras");
        sunMaterial.SetFloat("_SunFallbackTransmission", 0);
        Clear(mask, Color.clear);
        Graphics.Blit(Texture2D.blackTexture, mask, sunMaterial, 0);
        Check(Mean(Pixels(mask)) < .00001f, "Production sun shader uses fallback extinction without current GPU cover");
        Color sky = new Color(.1f, .2f, .3f, .4f);
        Clear(mask, sky);
        Graphics.Blit(Texture2D.blackTexture, mask, sunMaterial, 0);
        Color unchanged = Pixels(mask)[72 * 256 + 128];
        Check(Mathf.Abs(unchanged.r - sky.r) < .00001f && Mathf.Abs(unchanged.g - sky.g) < .00001f &&
            Mathf.Abs(unchanged.b - sky.b) < .00001f && Mathf.Abs(unchanged.a - sky.a) < .00001f,
            "Zero sun radiance preserves existing sky RGB and alpha");
        RenderTexture.active = null;
        Destroy(sunMaterial); Destroy(cloudEdge);
        Destroy(material); Destroy(mask); Destroy(blurred); Destroy(clear); Destroy(opaque); Destroy(depth);
        Shader.SetGlobalTexture("_CameraDepthTexture", null);
        Shader.SetGlobalTexture(CloudLowRes.CoverId, null);
        Shader.SetGlobalFloat("_BoscaliCloudCoverFrame", 0);
    }

    private void SetSun(float elevation, float u, float v)
    {
        Vector3 toSun = new Vector3(0, Mathf.Sin(elevation * Mathf.Deg2Rad), Mathf.Cos(elevation * Mathf.Deg2Rad));
        level.sun.transform.rotation = Quaternion.LookRotation(-toSun, Vector3.up);
        camera.transform.rotation = Quaternion.LookRotation(toSun, Vector3.up);
        float yaw = Mathf.Atan((u - .5f) * 2 * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad) * target.width / target.height) * Mathf.Rad2Deg;
        float pitch = Mathf.Atan((v - .5f) * 2 * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad)) * Mathf.Rad2Deg;
        camera.transform.rotation *= Quaternion.Euler(pitch, -yaw, 0);
        level.sunObject.transform.position = camera.transform.position + toSun * 40000;
    }
    private void SetCloud(float alpha)
    {
        Destroy(cloudCover); cloudCover = Cloud(alpha); Shader.SetGlobalTexture(CloudLowRes.CoverId, cloudCover);
    }
    private static Texture2D Cloud(float alpha)
    {
        var texture = new Texture2D(256, 144, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[256 * 144];
        for (int y = 0; y < 144; y++) for (int x = 0; x < 256; x++)
        {
            float cover = alpha == .5f ? Mathf.SmoothStep(0, 1, (x / 256f - .56f + .035f * Mathf.Sin(y * .18f)) * 12) : alpha;
            pixels[y * 256 + x] = new Color(.3f, .32f, .35f, cover);
        }
        texture.SetPixels(pixels); texture.Apply(); return texture;
    }
    private static IEnumerator Settle(int frames)
    {
        for (int i = 0; i < frames; i++) yield return new WaitForEndOfFrame();
    }
    private void Save(string name)
    {
        var image = Read(target, TextureFormat.RGBAFloat);
        Color[] pixels = image.GetPixels();
        // The HDR render target stores linear values; PNG viewers expect display-encoded RGB.
        if (!target.sRGB)
        {
            for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i].gamma;
        }
        var output = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        output.SetPixels(pixels); output.Apply();
        File.WriteAllBytes(name, output.EncodeToPNG()); Destroy(output); Destroy(image);
    }
    private static Color[] Pixels(RenderTexture source)
    {
        var image = Read(source, TextureFormat.RGBAFloat);
        Color[] pixels = image.GetPixels(); Destroy(image); return pixels;
    }
    private static Texture2D Read(RenderTexture source, TextureFormat format)
    {
        var image = new Texture2D(source.width, source.height, format, false, true);
        var previous = RenderTexture.active; RenderTexture.active = source;
        image.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0); image.Apply();
        RenderTexture.active = previous; return image;
    }
    private static void Clear(RenderTexture target, Color color)
    {
        var previous = RenderTexture.active; RenderTexture.active = target;
        GL.Clear(false, true, color); RenderTexture.active = previous;
    }
    private static float Mean(Color[] pixels)
    {
        float sum = 0; for (int i = 0; i < pixels.Length; i++) sum += (pixels[i].r + pixels[i].g + pixels[i].b) / 3;
        return sum / pixels.Length;
    }
    private static float MaxDelta(Color[] a, Color[] b)
    {
        float maximum = 0;
        for (int i = 0; i < a.Length; i++) maximum = Mathf.Max(maximum, Mathf.Max(Mathf.Abs(a[i].r - b[i].r), Mathf.Max(Mathf.Abs(a[i].g - b[i].g), Mathf.Abs(a[i].b - b[i].b))));
        return maximum;
    }
    private void Check(bool ok, string description)
    {
        if (!ok) throw new InvalidOperationException(description);
        assertions++; log.AppendLine("PASS " + description);
    }
}
#endif
