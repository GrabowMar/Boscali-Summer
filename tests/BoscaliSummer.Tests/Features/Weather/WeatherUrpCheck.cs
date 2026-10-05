#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Modules.Immersion.Audio;
using BoscaliSummer.Modules.Immersion.Visuals;
using BoscaliSummer.Modules.Immersion.Runtime;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// Real URP frames exercise the production pass and unchanged pipeline-tagged shaders.
// This isolated scene does not substitute for Nuclear Option's complete camera stack.
public sealed class WeatherUrpCheck : MonoBehaviour
{
    private readonly StringBuilder log = new StringBuilder();
    private WeatherCloudPass pass;
    private Renderer volume;
    private Camera main, other;
    private RenderTexture mainTarget, otherTarget;
    private int callbacks;

#if UNITY_EDITOR
    public static void Build()
    {
        try
        {
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, "Assets/Resources/FixtureRenderer.asset");
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.supportsCameraDepthTexture = true;
            pipeline.supportsCameraOpaqueTexture = true;
            pipeline.msaaSampleCount = 1;
            pipeline.renderScale = 1f;
            AssetDatabase.CreateAsset(pipeline, "Assets/Resources/FixturePipeline.asset");
            GraphicsSettings.renderPipelineAsset = pipeline;
            QualitySettings.renderPipeline = pipeline;
            var occluder = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            occluder.SetColor("_BaseColor", new Color(.12f, .8f, .2f));
            AssetDatabase.CreateAsset(occluder, "Assets/Resources/Occluder.mat");
            var native = new Material(Shader.Find("Unlit/Color"));
            AssetDatabase.CreateAsset(native, "Assets/Resources/NativeColor.mat");
            foreach (string name in new[] { "FlightCloud", "FlightCloudComposite" })
            {
                Shader shader = Resources.Load<Shader>(name);
                if (shader == null || ShaderUtil.ShaderHasError(shader))
                    throw new Exception("Missing or invalid shader " + name);
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("URP validation").AddComponent<WeatherUrpCheck>();
            EditorSceneManager.SaveScene(scene, "Assets/check.unity");
            AssetDatabase.SaveAssets();
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/check.unity" }, "Player/WeatherUrpCheck.exe",
                BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception("Player build failed: " + report.summary.result);
            File.WriteAllText("build-result.txt", "PASS URP 14.0.12");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { File.WriteAllText("build-result.txt", e.ToString()); EditorApplication.Exit(1); }
    }
#endif

    private IEnumerator Start()
    {
        IEnumerator checks = Run();
        int exitCode = 0;
        while (true)
        {
            bool more;
            try { more = checks.MoveNext(); }
            catch (Exception e) { log.AppendLine("FAIL " + e); exitCode = 1; break; }
            if (!more) break;
            yield return checks.Current;
        }
        pass?.Dispose();
        File.WriteAllText("result.txt", log.ToString());
        Application.Quit(exitCode);
    }

    private IEnumerator Run()
    {
        log.AppendLine("GPU " + SystemInfo.graphicsDeviceName + " | " + SystemInfo.graphicsDeviceType);
        Check(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset, "URP pipeline active");
        QualitySettings.SetQualityLevel(Math.Max(0, QualitySettings.names.Length - 1), false);
        FxBus.SetAdaptiveCap(null);
        TestBudget();
        IEnumerator materialChecks = TestMaterialOwnership();
        while (materialChecks.MoveNext()) yield return materialChecks.Current;
        TestCameraOwnership();
        IEnumerator filterChecks = TestFilterOwnership();
        while (filterChecks.MoveNext()) yield return filterChecks.Current;

        var march = new Material(Resources.Load<Shader>("FlightCloud"));
        var composite = new Material(Resources.Load<Shader>("FlightCloudComposite"));
        composite.renderQueue = 2997;
        Check(march.shader.isSupported && composite.shader.isSupported, "Production cloud shaders supported");
        mainTarget = Target("Main frame"); otherTarget = Target("Secondary frame");
        main = CameraAt("Main camera", mainTarget, 0);
        other = CameraAt("Secondary camera", otherTarget, 1);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Production cloud volume";
        cube.transform.position = main.transform.position;
        cube.transform.localScale = Vector3.one * 1000f;
        volume = cube.GetComponent<Renderer>();
        volume.sharedMaterial = composite;
        volume.shadowCastingMode = ShadowCastingMode.Off;
        volume.receiveShadows = false;
        var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.name = "Opaque depth occluder";
        blocker.transform.position = main.transform.position + main.transform.forward * 40f;
        blocker.transform.rotation = main.transform.rotation;
        blocker.transform.localScale = new Vector3(16, 16, 2);
        blocker.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("Occluder");

        var field = new WeatherField();
        var key = new WeatherKey(90210u, 0f, false, (byte)WeatherRegimeType.Broken, 5f, 60f);
        field.Build(key, 900f, 60000f, 60000f, 13f);
        CloudMaps maps = CloudMaps.Build(key, 900f, 60000f, 60000f, 13f, 105000f, 315000f);
        var uniforms = new CloudVolumeUniforms(); uniforms.Settle(field);
        var frame = new CloudFrame
        {
            CameraPosition = main.transform.position, CameraForward = main.transform.forward,
            FieldOfView = main.fieldOfView, PixelHeight = 540, Bottom = maps.Bottom, Top = maps.Top,
            HorizonCover = maps.HorizonCover, SunDirection = new Vector3(.35f, .55f, -.65f),
            SunColor = new Color(1.9f, 1.8f, 1.6f), Ambient = new Color(.5f, .58f, .7f),
            Ground = new Color(.12f, .15f, .13f), Fog = new Color(.72f, .8f, .9f), Extinction = .00004f
        };
        byte[] bytes = CloudNoise3D.Generate(64, 47);
        var noise = new Texture3D(64, 64, 64, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat };
        noise.SetPixelData(bytes, 0); noise.Apply(false, true);
        uniforms.Apply(march, field, frame, noise);
        CloudVolumeUniforms.ApplySpans(march, 105000f, 315000f);
        march.SetTexture("_WeatherMapTex", Map(maps.Near, CloudMaps.NearSize));
        march.SetTexture("_WeatherProfileTex", Map(maps.NearProfiles, CloudMaps.NearSize));
        march.SetTexture("_WeatherFarMapTex", Map(maps.Far, CloudMaps.FarSize));
        march.SetTexture("_WeatherFarProfileTex", Map(maps.FarProfiles, CloudMaps.FarSize));
        march.SetTexture("_WeatherEnvelopeTex", Map(maps.Envelope, CloudMaps.EnvelopeSize));
        march.SetFloat("_WeatherEnvelopeOn", 1f);
        pass = new WeatherCloudPass();
        pass.Bind(main, volume, march, composite, true, true,
            camera => { callbacks++; uniforms.ApplyView(camera, camera.transform.position); });
        for (int i = 0; i < 64; i++) { RenderPair(); yield return null; }
        Check(pass.ExecutedFrame == Time.frameCount - 1, "Pass executes in completed rendered frame");
        Check(callbacks >= 62 && callbacks <= 64, "Only bound camera invokes preparation callback across 64 settling frames");
        Check(pass.Width > 0 && pass.Width <= 960 && pass.Height <= 540, "Half-resolution targets capped at 1080p");
        Check(FxRtPool.UsedBytes == pass.TargetBytes && pass.TargetBytes <= 16L * 1024 * 1024,
            "All five cloud targets registered with exact bounded bytes");
        var withCloud = Capture(mainTarget, "urp-main-cloud.png");
        var withoutCloud = Capture(otherTarget, "urp-secondary-clear.png");
        Color centerA = withCloud.GetPixel(960, 540), centerB = withoutCloud.GetPixel(960, 540);
        Check(Delta(centerA, centerB) < .035f && centerB.g > centerB.r * 2f,
            "Opaque scene depth occludes the cloud composite");
        float skyDifference = 0f;
        for (int y = 700; y < 1040; y += 40)
            for (int x = 100; x < 1800; x += 100)
                skyDifference += Delta(withCloud.GetPixel(x, y), withoutCloud.GetPixel(x, y));
        Check(skyDifference > 1f, "Bound camera has visible clouds; secondary camera stays clear");
        log.AppendLine("Sky sample difference " + skyDifference.ToString("F3"));

        pass.Bind(main, volume, march, composite, true, false,
            camera => { callbacks++; uniforms.ApplyView(camera, camera.transform.position); });
        for (int i = 0; i < 8; i++) { RenderPair(); yield return null; }
        var halfReference = Capture(mainTarget, "urp-half-reference.png");
        float meanDifference = MeanDelta(withCloud, halfReference);
        Check(meanDifference < .15f, "Settled temporal clouds stay close to full half-resolution reference");
        log.AppendLine("Mean settled temporal/reference RGB difference " + meanDifference.ToString("F4"));
        pass.Bind(main, volume, march, composite, true, true,
            camera => { callbacks++; uniforms.ApplyView(camera, camera.transform.position); });
        Quaternion initial = main.transform.rotation;
        for (int i = 0; i < 48; i++)
        {
            main.transform.rotation = Quaternion.AngleAxis(i * .5f, Vector3.up) * initial;
            RenderPair(); yield return null;
        }
        Capture(mainTarget, "urp-pan-temporal.png");
        for (int i = 0; i < 64; i++) { RenderPair(); yield return null; }
        Capture(mainTarget, "urp-pan-settled.png");
        Check(pass.ExecutedFrame == Time.frameCount - 1, "Temporal pass survives panning and settles after motion");

        int executed = pass.ExecutedFrame, before = callbacks;
        main.enabled = false;
        RenderPair(); yield return null; RenderPair(); yield return null;
        Check(pass.ExecutedFrame == executed && callbacks == before, "Disabled bound camera cannot execute pass");
        Check(volume.forceRenderingOff, "Other camera suppresses camera-following volume");
        pass.Dispose(); pass.Dispose();
        volume.enabled = false;
        Check(!volume.forceRenderingOff && pass.ExecutedFrame == -1 && FxRtPool.UsedBytes == 0,
            "Repeated teardown releases targets and restores renderer force flag");
        before = callbacks;
        main.enabled = true;
        RenderPair(); yield return null; RenderPair(); yield return null;
        Check(callbacks == before, "Disposed pass removes rendering callback");
        volume.enabled = true;
        pass.Bind(other, volume, march, composite, true, true,
            camera => { callbacks++; uniforms.ApplyView(camera, camera.transform.position); });
        for (int i = 0; i < 5; i++) { RenderPair(); yield return null; }
        Check(pass.ExecutedFrame == Time.frameCount - 1 && callbacks == before + 5,
            "Rebinding after teardown recovers only on new camera");
        Capture(otherTarget, "urp-rebound-cloud.png");
        pass.Dispose(); volume.enabled = false;
        Check(FxRtPool.UsedBytes == 0, "Rebind teardown leaves no cloud target ledger entries");
    }

    private void TestBudget()
    {
        var targets = new CloudLowRes();
        Check(targets.Ensure(4096, 2160) && targets.Width <= 960 && targets.Height <= 540,
            "4K request respects 1080p-equivalent cloud cap");
        Check(FxRtPool.UsedBytes == targets.TargetBytes, "Target-byte formula equals ledger accounting");
        targets.Dispose(); targets.Dispose();
        Check(FxRtPool.UsedBytes == 0, "Repeated direct-target disposal clears ledger");
        var blockers = new List<RenderTexture>();
        for (int i = 0; i < 3; i++)
        {
            var rt = new RenderTexture(2048, 1024, 0, RenderTextureFormat.ARGB32);
            Check(rt.Create() && FxRtPool.Own(rt), "Reserve shared FX budget " + i);
            blockers.Add(rt);
        }
        Check(FxRtPool.UsedBytes == FxRtPool.CapBytes && !targets.Ensure(1920, 1080),
            "Exhausted shared FX budget refuses cloud allocation");
        Check(FxRtPool.UsedBytes == FxRtPool.CapBytes && targets.Width == 0 && targets.Height == 0,
            "Refused cloud allocation releases partial attempts atomically");
        int refusalObjects = Resources.FindObjectsOfTypeAll<RenderTexture>().Length;
        for (int i = 0; i < 5; i++)
            Check(!targets.Ensure(1920, 1080), "Repeated refused cloud descriptor remains disabled " + i);
        Check(Resources.FindObjectsOfTypeAll<RenderTexture>().Length == refusalObjects,
            "Exhausted-pool retry cooldown creates no repeated render targets");
        foreach (var rt in blockers) { FxRtPool.Disown(rt); rt.Release(); Destroy(rt); }
        Check(targets.Ensure(1920, 1080), "Cloud allocation recovers after shared budget released");
        targets.Dispose();
        Check(FxRtPool.UsedBytes == 0, "Budget fixture leaves ledger empty");
    }

    private void RenderPair()
    {
        // Batch players do not enter their regular display render loop. A supported URP render
        // request executes the normal pipeline, camera callbacks, depth and our production pass.
        if (main.enabled) RenderPipeline.SubmitRenderRequest(main,
            new UniversalRenderPipeline.SingleCameraRequest { destination = mainTarget });
        if (other.enabled) RenderPipeline.SubmitRenderRequest(other,
            new UniversalRenderPipeline.SingleCameraRequest { destination = otherTarget });
    }

    private IEnumerator TestMaterialOwnership()
    {
        int color = Shader.PropertyToID("_BaseColor"), foreign = Shader.PropertyToID("_ForeignFixture");
        var renderer = GameObject.CreatePrimitive(PrimitiveType.Cube).GetComponent<Renderer>();
        renderer.enabled = false;
        var a = new Material(Resources.Load<Material>("Occluder"));
        var b = new Material(Resources.Load<Material>("Occluder"));
        var screen = new RenderTexture(8, 8, 0);
        a.SetTexture("_BaseMap", screen);
        var original = new Color(.2f, .3f, .4f, .7f); a.SetColor(color, original);
        renderer.sharedMaterials = new[] { a, b };
        var owner = MaterialSlotClone.TryBindDisplay(renderer, a, 0);
        Check(owner != null, "Verified display binds with an empty native MPB");
        Material clone = renderer.sharedMaterials[0]; owner.Apply(1000f);
        Check(clone != a && Delta(clone.GetColor(color), original * 1.15f) < .001f && clone.GetColor(color).a == original.a,
            "Only display material is cloned with bounded brightness and preserved alpha");
        var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block, 0);
        Check(block.isEmpty && Delta(a.GetColor(color), original) < .001f, "Clone glow leaves native material and empty MPB intact");
        var updated = new Color(.4f, .5f, .6f, .8f); a.SetColor(color, updated); owner.Apply(1.15f);
        Check(Delta(clone.GetColor(color), updated * 1.15f) < .001f && clone.GetColor(color).a == updated.a,
            "Current native display brightness is copied before each gain");
        block.SetFloat(foreign, 9f); block.SetColor(color, original); renderer.SetPropertyBlock(block, 0);
        var wide = new MaterialPropertyBlock(); wide.SetFloat(foreign, 12f); renderer.SetPropertyBlock(wide);
        owner.Apply(1.15f); owner.Restore(); owner.Restore(); renderer.GetPropertyBlock(block, 0);
        Check(Delta(block.GetColor(color), original) < .001f && block.GetFloat(foreign) == 9f,
            "Display release preserves latest foreign slot MPB");
        renderer.GetPropertyBlock(wide); Check(wide.GetFloat(foreign) == 12f, "Display release preserves renderer-wide MPB");
        Check(renderer.sharedMaterials[0] == a && renderer.sharedMaterials[1] == b,
            "Native material identity and unrelated slot are preserved");
        renderer.GetPropertyBlock(block, 1); Check(block.isEmpty, "Unrelated material slot stays unmodified");
        owner = MaterialSlotClone.TryBindDisplay(renderer, a, 0); Material replacedClone = renderer.sharedMaterials[0];
        renderer.sharedMaterials = new[] { b, b }; owner.Apply(1.15f); owner.Restore();
        Check(renderer.sharedMaterials[0] == b && !owner.Active, "Foreign display slot replacement remains authoritative");
        yield return null;
        Check(clone == null && replacedClone == null, "Released display clones are destroyed by Unity");
        Destroy(renderer.gameObject); Destroy(a); Destroy(b); Destroy(screen);
    }

    private void TestCameraOwnership()
    {
        var pivot = new GameObject("Native pivot ownership").transform;
        var look = new GameObject("Native free look ownership").transform; look.SetParent(pivot, false);
        Quaternion baseline = Quaternion.Euler(3f, 6f, 2f), free = Quaternion.Euler(20f, 25f, 1f);
        Quaternion offset = Quaternion.Euler(1.5f, -.5f, .5f);
        Vector3 position = new Vector3(3f, 4f, 5f); pivot.localPosition = position;
        pivot.localRotation = baseline; look.localRotation = free;
        var owner = new CockpitCameraOffset();
        for (int i = 0; i < 1000; i++)
        {
            owner.Apply(pivot, offset); owner.Apply(pivot, offset);
            Check(Same(pivot.localRotation, baseline * offset), "Camera offset composes exactly once " + i);
            owner.Remove();
        }
        Check(Same(pivot.localRotation, baseline) && pivot.localPosition == position && Same(look.localRotation, free),
            "Repeated camera ownership preserves native inertia position and free look");
        owner.Apply(pivot, offset); Quaternion foreign = Quaternion.Euler(-7f, 12f, 9f); pivot.localRotation = foreign;
        owner.Remove();
        Check(Same(pivot.localRotation, foreign), "Foreign absolute pivot rotation survives removal");
        pivot.localRotation = baseline; owner.Apply(pivot, offset); owner.Remove(); owner.Remove();
        Check(!owner.IsApplied && Same(pivot.localRotation, baseline), "Master/view/camera exit removes its own offset idempotently");
        Destroy(pivot.gameObject);
    }

    private static bool Same(Quaternion a, Quaternion b) =>
        Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y) + Math.Abs(a.z - b.z) + Math.Abs(a.w - b.w) < .0001f ||
        Math.Abs(a.x + b.x) + Math.Abs(a.y + b.y) + Math.Abs(a.z + b.z) + Math.Abs(a.w + b.w) < .0001f;
    private static float MeanDelta(Texture2D a, Texture2D b)
    {
        float sum = 0; int count = 0;
        for (int y = 16; y < a.height; y += 32)
            for (int x = 16; x < a.width; x += 32) { sum += Delta(a.GetPixel(x, y), b.GetPixel(x, y)); count++; }
        return sum / count;
    }

    private IEnumerator TestFilterOwnership()
    {
        var camera = new GameObject("Native filtered cockpit").AddComponent<Camera>(); camera.enabled = false;
        camera.gameObject.AddComponent<AudioListener>();
        var native = camera.gameObject.AddComponent<AudioLowPassFilter>();
        native.enabled = true; native.cutoffFrequency = 7777f; native.lowpassResonanceQ = 1.5f;
        var owner = new CockpitAudioFilter(); owner.Tick(camera, 1f, 0f, true, true, 1f);
        owner.Tick(camera, 1f, 0f, true, true, 1f);
        Check(native.cutoffFrequency < 7777f && owner.IsFilteringActive, "Pilot exposure owns a bounded native filter cutoff");
        native.lowpassResonanceQ = 2.5f;
        owner.Tick(camera, 0f, 0f, true, true, .02f);
        Check(native.enabled && native.cutoffFrequency == 7777f && native.lowpassResonanceQ == 2.5f,
            "Normal G restores cutoff and retains native resonance updates");
        owner.Tick(camera, 1f, 0f, true, true, 1f); native.cutoffFrequency = 5555f;
        owner.Release();
        Check(native != null && native.enabled && native.cutoffFrequency == 5555f && native.lowpassResonanceQ == 2.5f,
            "Foreign native filter replacement survives release");
        var replacement = new GameObject("Replacement cockpit").AddComponent<Camera>(); replacement.enabled = false;
        replacement.gameObject.AddComponent<AudioListener>();
        owner.Tick(camera, 1f, 0f, true, true, 1f);
        owner.Tick(replacement, 1f, 0f, true, true, 1f);
        Check(native.cutoffFrequency == 5555f, "Camera replacement restores the previous camera filter");
        var created = replacement.GetComponent<AudioLowPassFilter>();
        Check(created != null, "Missing cockpit filter is created for exposure");
        owner.Release(); owner.Release(); yield return null;
        Check(created == null && replacement.GetComponent<AudioLowPassFilter>() == null,
            "Created cockpit filter is destroyed after repeated release");
        Destroy(camera.gameObject); Destroy(replacement.gameObject);
    }

    private static Camera CameraAt(string name, RenderTexture target, float depth)
    {
        var camera = new GameObject(name).AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 400, 0);
        camera.transform.rotation = Quaternion.Euler(-12, 30, 0);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.42f, .6f, .85f);
        camera.nearClipPlane = 1f; camera.farClipPlane = 400000f; camera.fieldOfView = 60f;
        camera.targetTexture = target; camera.depth = depth;
        var data = camera.GetUniversalAdditionalCameraData();
        data.requiresDepthTexture = true; data.requiresColorTexture = true;
        return camera;
    }
    private static RenderTexture Target(string name)
    {
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { name = name };
        rt.Create(); return rt;
    }
    private static Texture2D Map(byte[] data, int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
        { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        texture.LoadRawTextureData(data); texture.Apply(false, true); return texture;
    }
    private static Texture2D Capture(RenderTexture target, string path)
    {
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(path, image.EncodeToPNG()); return image;
    }
    private static float Delta(Color a, Color b) => Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b);
    private void Check(bool ok, string description)
    {
        if (!ok) throw new InvalidOperationException(description);
        log.AppendLine("PASS " + description);
    }
}
#endif
