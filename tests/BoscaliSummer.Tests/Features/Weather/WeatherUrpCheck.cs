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
            foreach (string name in new[] { "FlightCloud", "FlightCloudComposite", "CloudEdgeFixture" })
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
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-shape-only") >= 0)
        { TestCloudShapeRegistration(); yield break; }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-motion-backbuffer") >= 0)
        {
            Application.runInBackground = true;
            IEnumerator backbufferChecks = TestMotionOcclusion(true);
            while (backbufferChecks.MoveNext()) yield return backbufferChecks.Current;
            yield break;
        }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-motion-rt") >= 0)
        {
            IEnumerator motionOnly = TestMotionOcclusion();
            while (motionOnly.MoveNext()) yield return motionOnly.Current;
            yield break;
        }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-matrix-only") >= 0)
        { TestRenderedView(); yield break; }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-precull-only") >= 0)
        { IEnumerator preCullOnly = TestPreCullVolumeFollow(); while (preCullOnly.MoveNext()) yield return preCullOnly.Current; yield break; }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-fast-only") >= 0)
        { IEnumerator fastOnly = TestFastRealClouds(); while (fastOnly.MoveNext()) yield return fastOnly.Current; yield break; }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-loopinterp-only") >= 0)
        { IEnumerator loopOnly = TestLoopInterpolationFollow(true); while (loopOnly.MoveNext()) yield return loopOnly.Current; yield break; }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-loopinterp-nofollow") >= 0)
        { IEnumerator loopControl = TestLoopInterpolationFollow(false); while (loopControl.MoveNext()) yield return loopControl.Current; yield break; }
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-edge-only") >= 0)
        { TestBudget(); TestCloudSilhouette(); yield break; }
        TestBudget();
        TestCloudSilhouette();
        TestCloudShapeRegistration();
        IEnumerator motionChecks = TestMotionOcclusion();
        while (motionChecks.MoveNext()) yield return motionChecks.Current;
        IEnumerator preCullChecks = TestPreCullVolumeFollow();
        while (preCullChecks.MoveNext()) yield return preCullChecks.Current;
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
            (camera, view, projection) => { callbacks++; uniforms.ApplyView(camera, camera.transform.position, view, projection, march); });
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-real-native-only") >= 0)
        {
            pass.Dispose();
            IEnumerator nativeChecks = TestMovingRealClouds(march, composite, frame, bytes);
            while (nativeChecks.MoveNext()) yield return nativeChecks.Current;
            yield break;
        }
        for (int i = 0; i < 64; i++) { RenderPair(); yield return null; }
        Check(pass.ExecutedFrame == Time.frameCount - 1, "Pass executes in completed rendered frame");
        Check(callbacks >= 62 && callbacks <= 64, "Only bound camera invokes preparation callback across 64 settling frames");
        Check(pass.Width > 0 && pass.Width <= 960 && pass.Height <= 540, "Half-resolution targets capped at 1080p");
        Check(FxRtPool.UsedBytes == pass.TargetBytes && pass.TargetBytes <= 16L * 1024 * 1024,
            "All cloud targets registered with exact bounded bytes");
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
            (camera, view, projection) => { callbacks++; uniforms.ApplyView(camera, camera.transform.position, view, projection, march); });
        for (int i = 0; i < 8; i++) { RenderPair(); yield return null; }
        var halfReference = Capture(mainTarget, "urp-half-reference.png");
        float meanDifference = MeanDelta(withCloud, halfReference);
        Check(meanDifference < .15f, "Settled temporal clouds stay close to full half-resolution reference");
        log.AppendLine("Mean settled temporal/reference RGB difference " + meanDifference.ToString("F4"));
        pass.Bind(main, volume, march, composite, true, true,
            (camera, view, projection) => { callbacks++; uniforms.ApplyView(camera, camera.transform.position, view, projection, march); });
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
            (camera, view, projection) => { callbacks++; uniforms.ApplyView(camera, camera.transform.position, view, projection, march); });
        for (int i = 0; i < 5; i++) { RenderPair(); yield return null; }
        Check(pass.ExecutedFrame == Time.frameCount - 1 && callbacks == before + 5,
            "Rebinding after teardown recovers only on new camera");
        Capture(otherTarget, "urp-rebound-cloud.png");
        pass.Dispose(); volume.enabled = false;
        Check(FxRtPool.UsedBytes == 0, "Rebind teardown leaves no cloud target ledger entries");
        IEnumerator movingRealChecks = TestMovingRealClouds(march, composite, frame, bytes);
        while (movingRealChecks.MoveNext()) yield return movingRealChecks.Current;
        IEnumerator fastRealChecks = TestFastRealClouds();
        while (fastRealChecks.MoveNext()) yield return fastRealChecks.Current;
        IEnumerator loopInterpChecks = TestLoopInterpolationFollow(true);
        while (loopInterpChecks.MoveNext()) yield return loopInterpChecks.Current;
    }

    private IEnumerator TestMovingRealClouds(Material march, Material composite, CloudFrame frame, byte[] noiseBytes)
    {
        mainTarget.Release(); otherTarget.Release(); Destroy(mainTarget); Destroy(otherTarget);
        mainTarget = new RenderTexture(1280, 720, 24) { name = "Temporal real cloud view" };
        otherTarget = new RenderTexture(1280, 720, 24) { name = "Fresh real cloud view" };
        mainTarget.Create(); otherTarget.Create();
        main.targetTexture = mainTarget; main.enabled = true;
        other.CopyFrom(main); other.targetTexture = otherTarget; other.enabled = true;
        volume.enabled = true;
        var referenceVolume = Instantiate(volume.gameObject).GetComponent<Renderer>();
        var referenceComposite = new Material(composite); referenceVolume.sharedMaterial = referenceComposite;
        var actualView = new CloudVolumeUniforms(); var referenceView = new CloudVolumeUniforms();
        var freshPass = new WeatherCloudPass();
        var mipNoise = new Texture3D(64, 64, 64, TextureFormat.RGBA32, true)
        { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat };
        mipNoise.SetPixelData(noiseBytes, 0); mipNoise.Apply(true, true); march.SetTexture("_CloudNoiseTex", mipNoise);
        march.SetFloat("_CloudPixelAngle", 2f * Mathf.Tan(frame.FieldOfView * .5f * Mathf.Deg2Rad) / 360f);
        pass.Bind(main, volume, march, composite, true, true,
            (camera, view, projection) => actualView.ApplyView(camera, camera.transform.position, view, projection, march));
        freshPass.Bind(other, referenceVolume, march, referenceComposite, true, false,
            (camera, view, projection) => referenceView.ApplyView(camera, camera.transform.position, view, projection, march));
        var actual = new Texture2D(640, 360, TextureFormat.RGBAFloat, false, true);
        var fresh = new Texture2D(640, 360, TextureFormat.RGBAFloat, false, true);
        Vector3 initialPosition = frame.CameraPosition; Quaternion initialRotation = Quaternion.LookRotation(frame.CameraForward);
        float worstRgb = 0, worstAlpha = 0, worstCentroid = 0, worstStoppedVariation = 0, worstFreshStoppedVariation = 0;
        Color[] previousActual = null, previousFresh = null;
        for (int step = -32; step < 80; step++)
        {
            float phase = step < 0 ? 0 : step < 24 ? step : step < 48 ? 48 - step : 0;
            main.transform.SetPositionAndRotation(initialPosition + new Vector3(phase * 10f, Mathf.Sin(phase * .08f) * 2f, phase * 3f),
                Quaternion.Euler(Mathf.Sin(phase * .09f) * 1.2f, phase * .3f, 0) * initialRotation);
            other.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            volume.transform.position = main.transform.position; referenceVolume.transform.position = main.transform.position;
            RenderPair();
            Check(pass.Width == 640 && pass.Height == 360 && freshPass.Width == pass.Width && freshPass.Height == pass.Height,
                "Paired real cloud views use equal descriptors within the shared budget " + step);
            var actualTarget = (RenderTexture)composite.GetTexture("_CloudLowResColour");
            var freshTarget = (RenderTexture)referenceComposite.GetTexture("_CloudLowResColour");
            Check(actualTarget != null && freshTarget != null && actualTarget.width == freshTarget.width && actualTarget.height == freshTarget.height,
                "Each URP pass owns a matching real cloud target " + step);
            ReadTarget(actualTarget, actual); ReadTarget(freshTarget, fresh);
            if (step >= 0)
            {
                Color[] a = actual.GetPixels(), b = fresh.GetPixels();
                float aa = 0, ba = 0, rgb = 0, alpha = 0; Vector2 ac = Vector2.zero, bc = Vector2.zero;
                for (int p = 0; p < a.Length; p++)
                {
                    Vector2 pixel = new Vector2(p % actual.width, p / actual.width);
                    aa += a[p].a; ba += b[p].a; ac += pixel * a[p].a; bc += pixel * b[p].a;
                    rgb += Delta(a[p], b[p]); alpha += Mathf.Abs(a[p].a - b[p].a);
                }
                Check(aa > 100 && ba > 100, "Both paired URP views contain real cloud volume " + step);
                rgb /= a.Length; alpha /= a.Length; float centroid = (ac / aa - bc / ba).magnitude;
                worstRgb = Mathf.Max(worstRgb, rgb); worstAlpha = Mathf.Max(worstAlpha, alpha); worstCentroid = Mathf.Max(worstCentroid, centroid);
                if (step >= 60 && previousActual != null)
                {
                    float actualVariation = 0, freshVariation = 0;
                    for (int p = 0; p < a.Length; p++)
                    { actualVariation += Delta(a[p], previousActual[p]); freshVariation += Delta(b[p], previousFresh[p]); }
                    worstStoppedVariation = Mathf.Max(worstStoppedVariation, actualVariation / a.Length);
                    worstFreshStoppedVariation = Mathf.Max(worstFreshStoppedVariation, freshVariation / a.Length);
                }
                previousActual = a; previousFresh = b;
                log.AppendLine("Real cloud motion " + step + " rgb=" + rgb.ToString("F5") + " alpha=" + alpha.ToString("F5") +
                    " centroid=" + centroid.ToString("F3") + " mass=" + aa.ToString("F1") + "/" + ba.ToString("F1"));
                if (step == 0 || step == 23 || step == 25 || step == 47 || step == 49 || step == 79)
                {
                    File.WriteAllBytes("real-cloud-temporal-" + step + ".png", actual.EncodeToPNG());
                    File.WriteAllBytes("real-cloud-fresh-" + step + ".png", fresh.EncodeToPNG());
                }
            }
            yield return null;
        }
        log.AppendLine("Real cloud motion worst rgb=" + worstRgb.ToString("F5") + " alpha=" + worstAlpha.ToString("F5") +
            " centroid=" + worstCentroid.ToString("F3") + " stoppedVariation=" + worstStoppedVariation.ToString("F5") +
            " freshStoppedVariation=" + worstFreshStoppedVariation.ToString("F5"));
        pass.Dispose(); freshPass.Dispose(); volume.enabled = false; referenceVolume.enabled = false;
        Destroy(referenceVolume.gameObject); Destroy(referenceComposite); Destroy(mipNoise); Destroy(actual); Destroy(fresh);
        Check(FxRtPool.UsedBytes == 0, "Paired real cloud passes release their target budget");
        Check(worstRgb < .15f, "Moving real cloud volume remains close to a fresh half-resolution URP reference");
    }

    private IEnumerator TestFastRealClouds()
    {
        // The in-game trail regime the slow paired test never exercises: inside the deck,
        // translating at flight speed while looking around fast. Near volume has huge
        // per-frame parallax that single-depth reprojection cannot capture; the resolve
        // must refresh toward the fresh march instead of trailing it.
        var field = new WeatherField();
        var key = new WeatherKey(90210u, 0f, false, (byte)WeatherRegimeType.Broken, 5f, 60f);
        field.Build(key, 900f, 60000f, 60000f, 13f);
        CloudMaps maps = CloudMaps.Build(key, 900f, 60000f, 60000f, 13f, 105000f, 315000f);
        var uniforms = new CloudVolumeUniforms(); uniforms.Settle(field);
        Vector3 deckPosition = new Vector3(0f, maps.Bottom + 150f, 0f);
        Quaternion deckRotation = Quaternion.Euler(-2f, 30f, 0f);
        var frame = new CloudFrame
        {
            CameraPosition = deckPosition, CameraForward = deckRotation * Vector3.forward,
            FieldOfView = 60f, PixelHeight = 360, Bottom = maps.Bottom, Top = maps.Top,
            HorizonCover = maps.HorizonCover, SunDirection = new Vector3(.35f, .55f, -.65f),
            SunColor = new Color(1.9f, 1.8f, 1.6f), Ambient = new Color(.5f, .58f, .7f),
            Ground = new Color(.12f, .15f, .13f), Fog = new Color(.72f, .8f, .9f), Extinction = .00004f
        };
        byte[] bytes = CloudNoise3D.Generate(64, 47);
        var noise = new Texture3D(64, 64, 64, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat };
        noise.SetPixelData(bytes, 0); noise.Apply(false, true);
        var march = new Material(Resources.Load<Shader>("FlightCloud"));
        var composite = new Material(Resources.Load<Shader>("FlightCloudComposite"));
        composite.renderQueue = 2997;
        uniforms.Apply(march, field, frame, noise);
        CloudVolumeUniforms.ApplySpans(march, 105000f, 315000f);
        march.SetTexture("_WeatherMapTex", Map(maps.Near, CloudMaps.NearSize));
        march.SetTexture("_WeatherProfileTex", Map(maps.NearProfiles, CloudMaps.NearSize));
        march.SetTexture("_WeatherFarMapTex", Map(maps.Far, CloudMaps.FarSize));
        march.SetTexture("_WeatherFarProfileTex", Map(maps.FarProfiles, CloudMaps.FarSize));
        march.SetTexture("_WeatherEnvelopeTex", Map(maps.Envelope, CloudMaps.EnvelopeSize));
        march.SetFloat("_WeatherEnvelopeOn", 1f);
        var fastTarget = new RenderTexture(1280, 720, 24) { name = "Fast temporal cloud view" };
        var freshTarget = new RenderTexture(1280, 720, 24) { name = "Fast fresh cloud view" };
        fastTarget.Create(); freshTarget.Create();
        var fastMain = CameraAt("Fast cloud camera", fastTarget, 0);
        var fastOther = CameraAt("Fast clear camera", freshTarget, 1);
        fastMain.transform.SetPositionAndRotation(deckPosition, deckRotation);
        fastOther.CopyFrom(fastMain); fastOther.targetTexture = freshTarget;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Fast cloud volume";
        cube.transform.position = deckPosition;
        cube.transform.localScale = Vector3.one * 1000f;
        var fastVolume = cube.GetComponent<Renderer>();
        fastVolume.sharedMaterial = composite;
        fastVolume.shadowCastingMode = ShadowCastingMode.Off;
        fastVolume.receiveShadows = false;
        var fastReferenceVolume = Instantiate(fastVolume.gameObject).GetComponent<Renderer>();
        var referenceComposite = new Material(composite); fastReferenceVolume.sharedMaterial = referenceComposite;
        var actualView = new CloudVolumeUniforms(); var referenceView = new CloudVolumeUniforms();
        var temporalPass = new WeatherCloudPass();
        var freshPass = new WeatherCloudPass();
        var mipNoise = new Texture3D(64, 64, 64, TextureFormat.RGBA32, true)
        { filterMode = FilterMode.Trilinear, wrapMode = TextureWrapMode.Repeat };
        mipNoise.SetPixelData(bytes, 0); mipNoise.Apply(true, true); march.SetTexture("_CloudNoiseTex", mipNoise);
        march.SetFloat("_CloudPixelAngle", 2f * Mathf.Tan(frame.FieldOfView * .5f * Mathf.Deg2Rad) / 360f);
        temporalPass.Bind(fastMain, fastVolume, march, composite, true, true,
            (camera, view, projection) => actualView.ApplyView(camera, camera.transform.position, view, projection, march));
        freshPass.Bind(fastOther, fastReferenceVolume, march, referenceComposite, true, false,
            (camera, view, projection) => referenceView.ApplyView(camera, camera.transform.position, view, projection, march));
        var actual = new Texture2D(640, 360, TextureFormat.RGBAFloat, false, true);
        var fresh = new Texture2D(640, 360, TextureFormat.RGBAFloat, false, true);
        float worstRgb = 0, worstAlpha = 0, worstCentroid = 0, worstStoppedVariation = 0;
        Color[] previousActual = null;
        for (int step = -12; step < 36; step++)
        {
            float phase = step < 0 ? 0 : step < 12 ? step : step < 24 ? 24 - step : 0;
            fastMain.transform.SetPositionAndRotation(deckPosition + new Vector3(phase * 2f, Mathf.Sin(phase * .3f) * 1f, phase * .6f),
                Quaternion.Euler(Mathf.Sin(phase * .25f) * 4f, phase * 2f, 0) * deckRotation);
            fastOther.transform.SetPositionAndRotation(fastMain.transform.position, fastMain.transform.rotation);
            fastVolume.transform.position = fastMain.transform.position; fastReferenceVolume.transform.position = fastMain.transform.position;
            if (fastMain.enabled) RenderPipeline.SubmitRenderRequest(fastMain,
                new UniversalRenderPipeline.SingleCameraRequest { destination = fastTarget });
            if (fastOther.enabled) RenderPipeline.SubmitRenderRequest(fastOther,
                new UniversalRenderPipeline.SingleCameraRequest { destination = freshTarget });
            yield return null;
            Check(temporalPass.Width == 640 && temporalPass.Height == 360 && freshPass.Width == temporalPass.Width && freshPass.Height == temporalPass.Height,
                "Paired fast cloud views use equal descriptors within the shared budget " + step);
            var actualTarget = (RenderTexture)composite.GetTexture("_CloudLowResColour");
            var freshTargetTex = (RenderTexture)referenceComposite.GetTexture("_CloudLowResColour");
            Check(actualTarget != null && freshTargetTex != null && actualTarget.width == freshTargetTex.width && actualTarget.height == freshTargetTex.height,
                "Each fast URP pass owns a matching real cloud target " + step);
            ReadTarget(actualTarget, actual); ReadTarget(freshTargetTex, fresh);
            if (step >= 0)
            {
                Color[] a = actual.GetPixels(), b = fresh.GetPixels();
                float aa = 0, ba = 0, rgb = 0, alpha = 0; Vector2 ac = Vector2.zero, bc = Vector2.zero;
                for (int p = 0; p < a.Length; p++)
                {
                    Vector2 pixel = new Vector2(p % actual.width, p / actual.width);
                    aa += a[p].a; ba += b[p].a; ac += pixel * a[p].a; bc += pixel * b[p].a;
                    rgb += Delta(a[p], b[p]); alpha += Mathf.Abs(a[p].a - b[p].a);
                }
                Check(aa > 100 && ba > 100, "Both paired fast URP views contain real cloud volume " + step);
                rgb /= a.Length; alpha /= a.Length; float centroid = (ac / aa - bc / ba).magnitude;
                worstRgb = Mathf.Max(worstRgb, rgb); worstAlpha = Mathf.Max(worstAlpha, alpha); worstCentroid = Mathf.Max(worstCentroid, centroid);
                if (step >= 30 && previousActual != null)
                {
                    float actualVariation = 0;
                    for (int p = 0; p < a.Length; p++) actualVariation += Delta(a[p], previousActual[p]);
                    worstStoppedVariation = Mathf.Max(worstStoppedVariation, actualVariation / a.Length);
                }
                previousActual = a;
                log.AppendLine("Fast cloud motion " + step + " rgb=" + rgb.ToString("F5") + " alpha=" + alpha.ToString("F5") +
                    " centroid=" + centroid.ToString("F3") + " mass=" + aa.ToString("F1") + "/" + ba.ToString("F1"));
                if (step == 0 || step == 11 || step == 13 || step == 23 || step == 25 || step == 35)
                {
                    File.WriteAllBytes("fast-cloud-temporal-" + step + ".png", actual.EncodeToPNG());
                    File.WriteAllBytes("fast-cloud-fresh-" + step + ".png", fresh.EncodeToPNG());
                }
            }
        }
        log.AppendLine("Fast cloud motion worst rgb=" + worstRgb.ToString("F5") + " alpha=" + worstAlpha.ToString("F5") +
            " centroid=" + worstCentroid.ToString("F3") + " stoppedVariation=" + worstStoppedVariation.ToString("F5"));
        temporalPass.Dispose(); freshPass.Dispose(); fastVolume.enabled = false; fastReferenceVolume.enabled = false;
        Destroy(fastReferenceVolume.gameObject); Destroy(referenceComposite); Destroy(mipNoise); Destroy(actual); Destroy(fresh);
        Destroy(fastMain.gameObject); Destroy(fastOther.gameObject); Destroy(cube);
        Destroy(march); Destroy(composite); Destroy(noise);
        fastTarget.Release(); freshTarget.Release(); Destroy(fastTarget); Destroy(freshTarget);
        Check(FxRtPool.UsedBytes == 0, "Paired fast cloud passes release their target budget");
        Check(worstRgb < .5f, "Fast in-deck temporal clouds stay within a loose probe bound of fresh");
    }

    private static float SamplePattern(Texture2D marched, Matrix4x4 liveView, Matrix4x4 projection, Vector3 world)
    {
        Vector4 clip = projection * liveView * new Vector4(world.x, world.y, world.z, 1f);
        int tx = Mathf.Clamp(Mathf.RoundToInt((clip.x / clip.w * .5f + .5f) * marched.width), 0, marched.width - 1);
        int ty = Mathf.Clamp(Mathf.RoundToInt((clip.y / clip.w * .5f + .5f) * marched.height), 0, marched.height - 1);
        return marched.GetPixel(tx, ty).a;
    }

    private IEnumerator TestLoopInterpolationFollow(bool parented)
    {
        // Real-loop counterpart to the manual-submit pre-cull test: the enabled camera
        // renders through the loop itself, and Application.onBeforeRender moves it after
        // our follow write, modelling the interpolated aircraft hierarchy. The assert
        // reads the marched target (not the composite: occluder centers are
        // depth-protected there) and projects the occluder with live transform matrices,
        // because the camera matrix properties only refresh at the transform sync.
        // parented=false (-loopinterp-nofollow) reads identically: the march is a
        // fullscreen blit, so this test guards march-pose tracking, not cube parenting.
        if (Application.isBatchMode)
        {
            log.AppendLine("Loop follow skipped in batchmode (needs display-loop rendering).");
            yield break;
        }
        var loopTarget = Target("Loop follow cloud view");
        var moving = CameraAt("Loop follow cloud camera", loopTarget, 0);
        Vector3 basePose = Vector3.zero;
        moving.transform.SetPositionAndRotation(basePose, Quaternion.identity);
        moving.nearClipPlane = 1; moving.farClipPlane = 20000;
        moving.enabled = true;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.localScale = Vector3.one * 1000;
        cube.transform.position = basePose;
        if (parented)
        {
            cube.transform.SetParent(moving.transform, false);
            cube.transform.localPosition = Vector3.zero;
            cube.transform.localRotation = Quaternion.identity;
        }
        var cloudRenderer = cube.GetComponent<Renderer>();
        var march = new Material(Resources.Load<Shader>("CloudEdgeFixture"));
        march.SetFloat("_CloudShapePattern", 1);
        var composite = new Material(Resources.Load<Shader>("FlightCloudComposite"));
        composite.renderQueue = 2997; cloudRenderer.sharedMaterial = composite;
        cloudRenderer.shadowCastingMode = ShadowCastingMode.Off;
        // Shape pattern needs no occluders (analytic, depth-independent).
        var view = new CloudVolumeUniforms();
        var clouds = new WeatherCloudPass();
        clouds.Bind(moving, cloudRenderer, march, composite, true, false,
            (camera, renderedView, projection) => view.ApplyView(camera, camera.transform.position, renderedView, projection));
        Vector3 jump = new Vector3(20f, 0f, 0f);
        int beforeRenderCount = 0;
        UnityEngine.Events.UnityAction jumpHandler = () =>
        {
            beforeRenderCount++;
            moving.transform.position = basePose + jump;
        };
        Application.onBeforeRender += jumpHandler;
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        log.AppendLine("Loop follow warmup onBeforeRender count " + beforeRenderCount +
            " marched=" + (composite.GetTexture("_CloudLowResColour") != null));
        var marched = new Texture2D(960, 540, TextureFormat.RGBA32, false);
        for (int frame = 0; frame < 12; frame++)
        {
            moving.transform.SetPositionAndRotation(basePose, Quaternion.identity);
            if (!parented) cube.transform.position = basePose;
            yield return new WaitForEndOfFrame();
            log.AppendLine("Loop follow frame " + frame + " onBeforeRender count " + beforeRenderCount);
            var lowres = (RenderTexture)composite.GetTexture("_CloudLowResColour");
            Check(lowres != null, "Loop cloud pass marched frame " + frame);
            ReadTarget(lowres, marched);
            // Live view: the camera is unparented, so its TRS composes the render pose
            // directly, unlike the sync-cached matrix properties. Cameras render down
            // -Z (view space), so the rigid inverse needs the Z flip a TRS lacks.
            Matrix4x4 liveView = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) *
                Matrix4x4.TRS(moving.transform.position, moving.transform.rotation, Vector3.one).inverse;
            float center = SamplePattern(marched, liveView, moving.projectionMatrix, new Vector3(-85f, 35f, 800f));
            float edge = SamplePattern(marched, liveView, moving.projectionMatrix, new Vector3(-21.76f, 35f, 800f));
            log.AppendLine("Loop follow frame " + frame + " pattern center " + center.ToString("F5") +
                " edge " + edge.ToString("F5"));
            Vector3 diagAnchor = new Vector3(-85f, 35f, 800f);
            Vector4 diagClip = moving.projectionMatrix * liveView * new Vector4(diagAnchor.x, diagAnchor.y, diagAnchor.z, 1f);
            int diagTx = Mathf.Clamp(Mathf.RoundToInt((diagClip.x / diagClip.w * .5f + .5f) * marched.width), 0, marched.width - 1);
            int diagTy = Mathf.Clamp(Mathf.RoundToInt((diagClip.y / diagClip.w * .5f + .5f) * marched.height), 0, marched.height - 1);
            Vector3 diagPos = Shader.GetGlobalVector("_CloudCameraPos");
            Matrix4x4 diagFrustum = Shader.GetGlobalMatrix("_CloudFrustum");
            Vector4 diagSize = Shader.GetGlobalVector("_CloudLowResSize");
            Vector4 diagChecker = Shader.GetGlobalVector("_CloudChecker");
            log.AppendLine("Loop diag frame " + frame + " size " + diagSize.ToString("F5") +
                " checker " + diagChecker.ToString("F3") +
                " checkerOn " + Shader.GetGlobalFloat("_CloudCheckerOn").ToString("F2") +
                " lowres " + lowres.width + "x" + lowres.height);
            log.AppendLine("Loop diag frame " + frame + " texel " + diagTx + "," + diagTy +
                " clip " + diagClip.x.ToString("F2") + "," + diagClip.y.ToString("F2") + "," + diagClip.w.ToString("F2") +
                " cpos " + diagPos.ToString("F2") +
                " tpos " + moving.transform.position.ToString("F2") +
                " c2w " + moving.cameraToWorldMatrix.GetColumn(3).ToString("F2"));
            log.AppendLine("Loop diag frame " + frame + " frustum " +
                diagFrustum.GetRow(0).ToString("F3") + " / " + diagFrustum.GetRow(1).ToString("F3") + " / " +
                diagFrustum.GetRow(2).ToString("F3") + " / " + diagFrustum.GetRow(3).ToString("F3"));
            string grid = "";
            for (int gy = -1; gy <= 1; gy++)
                for (int gx = -1; gx <= 1; gx++)
                    grid += marched.GetPixel(Mathf.Clamp(diagTx + gx * 8, 0, marched.width - 1),
                        Mathf.Clamp(diagTy + gy * 8, 0, marched.height - 1)).a.ToString("F2") + " ";
            log.AppendLine("Loop diag frame " + frame + " grid " + grid);
            if (frame == 0) File.WriteAllBytes("loop-marched-0.png", marched.EncodeToPNG());
            Check(Mathf.Abs(center - .9f) < .15f, "Loop pattern sampling is sane frame " + frame);
            Check(edge < .5f, "Loop march tracks the render pose frame " + frame);
        }
        log.AppendLine("Loop follow onBeforeRender count " + beforeRenderCount);
        Check(beforeRenderCount >= 12, "Loop camera moved after the follow write every frame");
        Application.onBeforeRender -= jumpHandler;
        moving.enabled = false;
        clouds.Dispose();
        Destroy(moving.gameObject); Destroy(cube);
        Destroy(march); Destroy(composite); Destroy(marched);
        loopTarget.Release(); Destroy(loopTarget);
        Check(FxRtPool.UsedBytes == 0, "Loop follow fixture releases targets");
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

    private void TestCloudSilhouette()
    {
        var targets = new CloudLowRes();
        var camera = new GameObject("Silhouette reprojection").AddComponent<Camera>(); camera.enabled = false;
        camera.nearClipPlane = 1f; camera.farClipPlane = 20000f; camera.aspect = 2f;
        var uniforms = new CloudVolumeUniforms();
        var march = new Material(Resources.Load<Shader>("CloudEdgeFixture"));
        var composite = new Material(Resources.Load<Shader>("FlightCloudComposite"));
        var depth = new Texture2D(128, 64, TextureFormat.RFloat, false, true)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var output = new RenderTexture(128, 64, 0, RenderTextureFormat.ARGBHalf); output.Create();
        var quad = new Mesh
        {
            vertices = new[] { new Vector3(-1, -1, .5f), new Vector3(1, -1, .5f),
                new Vector3(1, 1, .5f), new Vector3(-1, 1, .5f) },
            triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 }
        };
        Check(targets.Ensure(128, 64), "Silhouette fixture targets allocated");
        void RenderEdge(int edge, bool reset, bool slope = false, int phase = 0)
        {
            var pixels = new float[128 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 128; x++)
                pixels[y * 128 + x] = slope ? 1f / (1500f + ((x + phase) % 8) * 800f) :
                    x < edge ? 1f / 40f : 1f / 10000f;
            depth.SetPixelData(pixels, 0); depth.Apply();
            uniforms.ApplyView(camera, Vector3.zero);
            // Deliberately make one checker sample lie on the opposite side of the edge.
            Shader.SetGlobalVector("_CloudChecker", new Vector4(0, 0, 0, 1));
            Shader.SetGlobalVector("_ZBufferParams", new Vector4(0, 0, 1, 0));
            Shader.SetGlobalTexture("_CameraDepthTexture", depth);
            if (reset) targets.InvalidateHistory();
            var cmd = new CommandBuffer(); targets.Record(cmd, march, composite, true);
            cmd.SetRenderTarget(output); cmd.ClearRenderTarget(false, true, Color.clear);
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cmd.SetGlobalVector("_ProjectionParams", new Vector4(1, 1, 20000, 1f / 20000));
            cmd.DrawMesh(quad, Matrix4x4.identity, composite, 0, 0);
            Graphics.ExecuteCommandBuffer(cmd); cmd.Release();
        }
        RenderEdge(63, true);
        var firstComposite = Capture(output, "cloud-edge-composite-first.png");
        Check(firstComposite.GetPixel(63, 32).a > .85f && firstComposite.GetPixel(62, 32).a < .01f,
            "Full-resolution composite rejects empty foreground samples at the sky silhouette");
        RenderEdge(80, true); RenderEdge(63, false);
        var movedComposite = Capture(output, "cloud-edge-composite-disocclusion.png");
        Check(movedComposite.GetPixel(67, 32).a > .85f && movedComposite.GetPixel(62, 32).a < .01f,
            "Moving foreground leaves neither an empty cloud outline nor cloud bleed over the aircraft");
        march.SetFloat("_CloudEdgeGap", 1f);
        RenderEdge(80, true); RenderEdge(63, false);
        var mixedHistory = Capture(output, "cloud-edge-mixed-history.png");
        RenderEdge(63, true);
        var mixedFresh = Capture(output, "cloud-edge-mixed-fresh.png");
        Check(mixedFresh.GetPixel(67, 32).a > .2f &&
            Math.Abs(mixedHistory.GetPixel(67, 32).a - mixedFresh.GetPixel(67, 32).a) < .02f,
            "Depth rejection survives a real clear-cloud neighbour that cannot clamp away foreground history");
        march.SetFloat("_CloudEdgeGap", 0f); march.SetFloat("_CloudEdgeDistance", 1000f);
        RenderEdge(0, true, true);
        var slope = Capture(output, "cloud-depth-slope.png");
        float minimum = 1f;
        for (int x = 8; x < 120; x++) minimum = Mathf.Min(minimum, slope.GetPixel(x, 32).a);
        log.AppendLine("Minimum opacity of cloud in front of steep terrain " + minimum.ToString("F4"));
        Check(minimum > .85f, "A cloud in front of steep terrain retains coverage across scene-depth slopes");
        for (int frame = 1; frame <= 8; frame++)
        {
            camera.transform.rotation = Quaternion.Euler(frame * .4f, frame * .5f, frame * .2f);
            RenderEdge(0, false, true, frame);
            var movingSlope = Capture(output, "cloud-depth-slope-moving-" + frame + ".png");
            minimum = 1f;
            for (int x = 8; x < 120; x++) minimum = Mathf.Min(minimum, movingSlope.GetPixel(x, 32).a);
            Check(minimum > .85f, "Moving view retains clouds in front of changing terrain depth " + frame);
        }
        uniforms.Reset();
        Check(!uniforms.ApplyView(camera, Vector3.zero) && uniforms.ApplyView(camera, Vector3.zero),
            "Temporal view history arms only after its first render");
        camera.aspect = 1.5f;
        Check(!uniforms.ApplyView(camera, Vector3.zero), "Aspect changes invalidate cloud reprojection");
        camera.projectionMatrix = Matrix4x4.Perspective(55f, 1.5f, 1f, 20000f);
        Check(!uniforms.ApplyView(camera, Vector3.zero), "Projection changes invalidate cloud reprojection");
        camera.targetTexture = output;
        Check(!uniforms.ApplyView(camera, Vector3.zero), "Render-target changes invalidate cloud reprojection");
        var replacement = new GameObject("Replacement view").AddComponent<Camera>(); replacement.enabled = false;
        replacement.CopyFrom(camera);
        Check(!uniforms.ApplyView(replacement, Vector3.zero), "Camera replacement cannot reuse another view's history");
        targets.Dispose(); output.Release();
        Destroy(camera.gameObject); Destroy(replacement.gameObject); Destroy(march); Destroy(composite);
        Destroy(depth); Destroy(output); Destroy(quad);
        Check(FxRtPool.UsedBytes == 0, "Silhouette fixture releases its targets");
        TestRenderedView();
    }

    private void TestRenderedView()
    {
        var parent = new GameObject("Scaled camera hierarchy");
        parent.transform.localScale = new Vector3(2, 3, 4);
        parent.transform.rotation = Quaternion.Euler(12, 34, 8);
        var camera = new GameObject("Rendered matrix view").AddComponent<Camera>(); camera.enabled = false;
        camera.transform.SetParent(parent.transform, false);
        camera.nearClipPlane = 1f; camera.farClipPlane = 20000f; camera.aspect = 2f;
        var view = new CloudVolumeUniforms();
        for (int frame = 0; frame < 12; frame++)
        {
            parent.transform.rotation = Quaternion.Euler(frame, 34 + frame * 2, 8 + frame * .5f);
            view.ApplyView(camera, camera.transform.position);
            Vector3 v = camera.projectionMatrix.inverse.MultiplyPoint(new Vector3(-1, -1, -1));
            Vector3 expected = camera.worldToCameraMatrix.inverse.MultiplyVector(v / -v.z);
            Vector3 actual = Shader.GetGlobalMatrix("_CloudFrustum").GetRow(0);
            float error = (actual - expected).magnitude;
            log.AppendLine("Scaled-view corner error " + frame + " " + error.ToString("F4"));
            Check(error < .001f, "Moving scaled camera uses its rendered view matrix " + frame);
        }
        camera.worldToCameraMatrix = Matrix4x4.Scale(new Vector3(1, 1, -1)) *
            Matrix4x4.TRS(new Vector3(10, 20, 30), Quaternion.Euler(15, 40, 5), Vector3.one).inverse;
        view.ApplyView(camera, camera.transform.position);
        Vector3 renderedOrigin = camera.worldToCameraMatrix.inverse.GetColumn(3);
        Check(((Vector3)Shader.GetGlobalVector("_CloudCameraPos") - renderedOrigin).magnitude < .001f,
            "Overridden rendered view owns cloud origin instead of the camera transform");
        var material = new Material(Resources.Load<Shader>("FlightCloud"));
        Vector3 offset = new Vector3(10000, 0, 20000);
        view.ApplyView(camera, camera.transform.position + offset, material);
        Check(((Vector3)material.GetVector("_CloudWorldOffset") - offset).magnitude < .001f,
            "Rendered view override preserves the floating-origin world offset");
        Matrix4x4 renderedView = camera.worldToCameraMatrix;
        renderedView = Matrix4x4.Scale(new Vector3(1, 1, -1)) *
            Matrix4x4.TRS(renderedOrigin + Vector3.right, Quaternion.Euler(15, 40, 5), Vector3.one).inverse;
        view.ApplyView(camera, camera.transform.position + offset, renderedView, camera.projectionMatrix, material);
        Check(((Vector3)Shader.GetGlobalVector("_CloudCamDelta") - Vector3.right).magnitude < .001f,
            "History translation follows the rendered origin when the camera transform stays still");
        Destroy(material);
        Destroy(camera.gameObject); Destroy(parent);
    }

    private void TestCloudShapeRegistration()
    {
        const int width = 384, height = 216;
        var temporal = new CloudLowRes(); var reference = new CloudLowRes(); var checkerReference = new CloudLowRes();
        Check(temporal.Ensure(width, height) && reference.Ensure(width, height) && checkerReference.Ensure(width, height),
            "Cloud-shape targets allocated");
        var camera = new GameObject("World-anchored cloud pattern").AddComponent<Camera>(); camera.enabled = false;
        camera.nearClipPlane = 1; camera.farClipPlane = 400000; camera.aspect = (float)width / height;
        var uniforms = new CloudVolumeUniforms();
        var temporalMarch = new Material(Resources.Load<Shader>("CloudEdgeFixture"));
        var referenceMarch = new Material(Resources.Load<Shader>("CloudEdgeFixture"));
        var checkerMarch = new Material(Resources.Load<Shader>("CloudEdgeFixture"));
        temporalMarch.SetFloat("_CloudShapePattern", 1); referenceMarch.SetFloat("_CloudShapePattern", 1); checkerMarch.SetFloat("_CloudShapePattern", 1);
        var temporalComposite = new Material(Resources.Load<Shader>("FlightCloudComposite"));
        var referenceComposite = new Material(Resources.Load<Shader>("FlightCloudComposite"));
        var checkerComposite = new Material(Resources.Load<Shader>("FlightCloudComposite"));
        var depth = new Texture2D(1, 1, TextureFormat.RFloat, false, true);
        depth.SetPixelData(new[] { 1f / 400000f }, 0); depth.Apply();
        var actual = new Texture2D(temporal.Width, temporal.Height, TextureFormat.RGBAFloat, false, true);
        var fresh = new Texture2D(reference.Width, reference.Height, TextureFormat.RGBAFloat, false, true);
        var checker = new Texture2D(reference.Width, reference.Height, TextureFormat.RGBAFloat, false, true);
        float worstCentroid = 0, worstAlpha = 0, worstEdge = 0;
        float worstCheckerCentroid = 0, worstCheckerEdge = 0, worstLag = 0, worstCheckerLag = 0;
        Vector2 previousCentre = Vector2.zero, stopMin = new Vector2(float.MaxValue, float.MaxValue), stopMax = new Vector2(float.MinValue, float.MinValue);
        Vector2 checkerStopMin = stopMin, checkerStopMax = stopMax;
        for (int frame = -24; frame < 80; frame++)
        {
            float phase = frame < 0 ? 0 : frame < 24 ? frame : frame < 48 ? 48 - frame : 0;
            camera.transform.SetPositionAndRotation(new Vector3(phase * 3, Mathf.Sin(phase * .08f) * .5f, phase * .9f),
                Quaternion.Euler(Mathf.Sin(phase * .09f) * 1.2f, phase * .12f, 0));
            uniforms.ApplyView(camera, camera.transform.position);
            Shader.SetGlobalVector("_ZBufferParams", new Vector4(0, 0, 1, 0));
            Shader.SetGlobalTexture("_CameraDepthTexture", depth);
            var cmd = new CommandBuffer(); temporal.Record(cmd, temporalMarch, temporalComposite, true);
            Graphics.ExecuteCommandBuffer(cmd); cmd.Release();
            cmd = new CommandBuffer(); reference.Record(cmd, referenceMarch, referenceComposite, false);
            Graphics.ExecuteCommandBuffer(cmd); cmd.Release();
            checkerReference.InvalidateHistory();
            cmd = new CommandBuffer(); checkerReference.Record(cmd, checkerMarch, checkerComposite, true);
            Graphics.ExecuteCommandBuffer(cmd); cmd.Release();
            ReadTarget((RenderTexture)temporalComposite.GetTexture("_CloudLowResColour"), actual);
            ReadTarget((RenderTexture)referenceComposite.GetTexture("_CloudLowResColour"), fresh);
            ReadTarget((RenderTexture)checkerComposite.GetTexture("_CloudLowResColour"), checker);
            if (frame < 0) continue;
            Color[] a = actual.GetPixels(), b = fresh.GetPixels(), q = checker.GetPixels();
            Vector2 ac = Vector2.zero, bc = Vector2.zero, qc = Vector2.zero; float aa = 0, ba = 0, qa = 0, difference = 0;
            float edgeDifference = 0, checkerEdgeDifference = 0; int edgeCount = 0;
            for (int y = 0; y < actual.height; y++) for (int x = 0; x < actual.width; x++)
            {
                int p = y * actual.width + x; Vector2 pixel = new Vector2(x, y);
                aa += a[p].a; ba += b[p].a; ac += pixel * a[p].a; bc += pixel * b[p].a;
                qa += q[p].a; qc += pixel * q[p].a;
                difference += Mathf.Abs(a[p].a - b[p].a);
                if (x <= 0 || y <= 0 || x >= actual.width - 1 || y >= actual.height - 1) continue;
                if (b[p].a < .3f || b[p].a >= .6f) continue;
                float gradient = new Vector2(b[p + 1].a - b[p - 1].a, b[p + actual.width].a - b[p - actual.width].a).magnitude * .5f;
                if (gradient < .05f) continue;
                edgeDifference += Mathf.Abs(a[p].a - b[p].a) / gradient;
                checkerEdgeDifference += Mathf.Abs(q[p].a - b[p].a) / gradient; edgeCount++;
            }
            Check(aa > 10 && ba > 10 && qa > 10 && edgeCount > 4, "Finite world-anchored pattern is visible " + frame);
            ac /= aa; bc /= ba; qc /= qa;
            float centroid = (ac - bc).magnitude, checkerCentroid = (qc - bc).magnitude;
            Vector2 movement = bc - previousCentre;
            float lag = frame > 0 && movement.sqrMagnitude > .0001f ? Vector2.Dot(bc - ac, movement.normalized) : 0;
            float checkerLag = frame > 0 && movement.sqrMagnitude > .0001f ? Vector2.Dot(bc - qc, movement.normalized) : 0;
            previousCentre = bc;
            if (frame >= 60)
            {
                stopMin = Vector2.Min(stopMin, ac); stopMax = Vector2.Max(stopMax, ac);
                checkerStopMin = Vector2.Min(checkerStopMin, qc); checkerStopMax = Vector2.Max(checkerStopMax, qc);
            }
            difference /= a.Length;
            float edge = edgeDifference / Math.Max(1, edgeCount), checkerEdge = checkerEdgeDifference / Math.Max(1, edgeCount);
            log.AppendLine("Cloud shape frame " + frame + " centroid=" + centroid.ToString("F4") +
                " alpha=" + difference.ToString("F5") + " edge=" + edge.ToString("F4") + " checkerCentroid=" + checkerCentroid.ToString("F4") +
                " lag=" + lag.ToString("F4") + " checkerLag=" + checkerLag.ToString("F4") + " checkerEdge=" + checkerEdge.ToString("F4"));
            worstCentroid = Mathf.Max(worstCentroid, centroid); worstAlpha = Mathf.Max(worstAlpha, difference);
            worstEdge = Mathf.Max(worstEdge, edge);
            worstCheckerCentroid = Mathf.Max(worstCheckerCentroid, checkerCentroid); worstLag = Mathf.Max(worstLag, lag);
            worstCheckerEdge = Mathf.Max(worstCheckerEdge, checkerEdge); worstCheckerLag = Mathf.Max(worstCheckerLag, checkerLag);
            if (frame == 0 || frame == 23 || frame == 25 || frame == 47 || frame == 49 || frame == 79)
            {
                File.WriteAllBytes("cloud-shape-temporal-" + frame + ".png", actual.EncodeToPNG());
                File.WriteAllBytes("cloud-shape-fresh-" + frame + ".png", fresh.EncodeToPNG());
            }
        }
        log.AppendLine("Cloud shape worst centroid=" + worstCentroid.ToString("F4") + " alpha=" + worstAlpha.ToString("F5") +
            " edge=" + worstEdge.ToString("F4") + " checkerCentroid=" + worstCheckerCentroid.ToString("F4") + " lag=" + worstLag.ToString("F4") +
            " checkerLag=" + worstCheckerLag.ToString("F4") + " checkerEdge=" + worstCheckerEdge.ToString("F4") +
            " stopJitter=" + (stopMax - stopMin).magnitude.ToString("F4") + " checkerStopJitter=" + (checkerStopMax - checkerStopMin).magnitude.ToString("F4"));
        temporal.Dispose(); reference.Dispose(); checkerReference.Dispose(); Destroy(camera.gameObject); Destroy(depth); Destroy(actual); Destroy(fresh); Destroy(checker);
        Destroy(temporalMarch); Destroy(referenceMarch); Destroy(temporalComposite); Destroy(referenceComposite);
        Destroy(checkerMarch); Destroy(checkerComposite);
        Check(FxRtPool.UsedBytes == 0, "Cloud-shape fixture releases targets");
        float stopJitter = (stopMax - stopMin).magnitude, checkerStopJitter = (checkerStopMax - checkerStopMin).magnitude;
        // The independently rendered fresh checker measures quarter-input quantization.
        // Require bounded subpixel tracking and no added bias beyond that input floor,
        // rather than demanding half-resolution detail the quarter march never sampled.
        Check(worstCentroid < .5f && worstEdge < 1f && worstLag < .25f && stopJitter < .2f,
            "Temporal cloud shape bounds displacement, directional lag and stopped jitter");
        Check(worstCentroid <= worstCheckerCentroid + .05f && worstLag <= worstCheckerLag + .02f &&
            worstEdge <= worstCheckerEdge + .05f && stopJitter <= checkerStopJitter * .5f,
            "Temporal registration stays within fresh checker uncertainty and reduces stopped jitter");
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

    private IEnumerator TestMotionOcclusion(bool backbuffer = false)
    {
        var cloudyTarget = Target("Moving cloud view");
        var clearTarget = Target("Moving native reference");
        var moving = CameraAt("Moving asymmetric cloud camera", cloudyTarget, 0);
        var reference = CameraAt("Moving clear camera", clearTarget, 1);
        reference.enabled = false;
        var overlays = new List<Camera>();
        if (backbuffer)
        {
            moving.targetTexture = null;
            for (int i = 0; i < 2; i++)
            {
                var overlay = CameraAt("Empty native overlay " + i, null, i + 2);
                overlay.cullingMask = 0;
                overlay.GetUniversalAdditionalCameraData().renderType = CameraRenderType.Overlay;
                moving.GetUniversalAdditionalCameraData().cameraStack.Add(overlay);
                overlays.Add(overlay);
            }
            Check(Screen.width == 1920 && Screen.height == 1080, "Backbuffer fixture uses requested display dimensions");
        }
        moving.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        moving.nearClipPlane = 1; moving.farClipPlane = 20000;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.localScale = Vector3.one * 1000;
        var cloudRenderer = cube.GetComponent<Renderer>();
        var march = new Material(Resources.Load<Shader>("CloudEdgeFixture"));
        var composite = new Material(Resources.Load<Shader>("FlightCloudComposite"));
        composite.renderQueue = 2997; cloudRenderer.sharedMaterial = composite;
        cloudRenderer.shadowCastingMode = ShadowCastingMode.Off;
        var foreground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        foreground.transform.position = new Vector3(7, 5, 40);
        foreground.transform.rotation = Quaternion.Euler(10, 25, 17);
        foreground.transform.localScale = new Vector3(9, 7, 3);
        foreground.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("Occluder");
        var ridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ridge.transform.position = new Vector3(-8, -5, 60);
        ridge.transform.rotation = Quaternion.Euler(8, 17, -11);
        ridge.transform.localScale = new Vector3(22, 5, 6);
        ridge.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("Occluder");
        var view = new CloudVolumeUniforms();
        var clouds = new WeatherCloudPass();
        clouds.Bind(moving, cloudRenderer, march, composite, true, true,
            (camera, renderedView, projection) => view.ApplyView(camera, camera.transform.position, renderedView, projection));
        if (backbuffer)
        {
            // Start's first frame can precede registration in the display render loop.
            yield return null;
            yield return new WaitForEndOfFrame();
            Check(clouds.ExecutedFrame >= 0, "Backbuffer display loop executes the production cloud pass");
        }
        var cloudy = new Texture2D(1920, 1080, TextureFormat.RGBA32, false);
        var clear = new Texture2D(1920, 1080, TextureFormat.RGBA32, false);
        float worst = 0;
        for (int frame = 0; frame < 24; frame++)
        {
            moving.transform.SetPositionAndRotation(new Vector3(frame * .04f, 0, -frame * .02f),
                Quaternion.Euler(Mathf.Sin(frame * .12f) * 9, Mathf.Sin(frame * .08f) * 5,
                    Mathf.Sin(frame * .15f) * 14));
            cube.transform.position = moving.transform.position;
            reference.CopyFrom(moving); reference.targetTexture = clearTarget; reference.enabled = false;
            RenderPipeline.SubmitRenderRequest(reference,
                new UniversalRenderPipeline.SingleCameraRequest { destination = clearTarget });
            if (backbuffer)
            {
                foreach (Camera overlay in overlays) overlay.transform.SetPositionAndRotation(moving.transform.position, moving.transform.rotation);
                yield return new WaitForEndOfFrame();
                var previous = RenderTexture.active; RenderTexture.active = null;
                cloudy.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); cloudy.Apply();
                RenderTexture.active = previous;
            }
            else
            {
                RenderPipeline.SubmitRenderRequest(moving,
                    new UniversalRenderPipeline.SingleCameraRequest { destination = cloudyTarget });
                ReadTarget(cloudyTarget, cloudy);
            }
            ReadTarget(clearTarget, clear);
            foreach (Vector3 point in new[] { foreground.transform.position, ridge.transform.position })
            {
                Vector3 pixel = moving.WorldToScreenPoint(point);
                int x = Mathf.RoundToInt(pixel.x), y = Mathf.RoundToInt(pixel.y);
                Color native = clear.GetPixel(x, y), actual = cloudy.GetPixel(x, y);
                Check(native.g > .6f && native.r < .3f, "Moving asymmetric native depth reference " + frame);
                float error = Delta(native, actual); worst = Mathf.Max(worst, error);
                if (error >= .035f)
                {
                    log.AppendLine("Cloud registration error " + frame + " pixel " + x + "," + y +
                        " native=" + native + " cloud=" + actual + " view=" +
                        Shader.GetGlobalMatrix("_CloudFrustum").GetRow(0) + " z=" + Shader.GetGlobalVector("_ZBufferParams"));
                    File.WriteAllBytes("cloud-motion-failed-" + frame + ".png", cloudy.EncodeToPNG());
                    Capture(clearTarget, "cloud-motion-reference-" + frame + ".png");
                }
                Check(error < .035f, "Moving asymmetric foreground stays registered with cloud depth " + frame);
            }
            yield return null;
        }
        log.AppendLine((backbuffer ? "Backbuffer stack" : "Render target") + " worst moving foreground cloud RGB error " + worst.ToString("F4"));
        clouds.Dispose();
        foreach (Camera overlay in overlays) Destroy(overlay.gameObject);
        Destroy(moving.gameObject); Destroy(reference.gameObject); Destroy(cube); Destroy(foreground); Destroy(ridge);
        Destroy(march); Destroy(composite); Destroy(cloudy); Destroy(clear);
        cloudyTarget.Release(); clearTarget.Release(); Destroy(cloudyTarget); Destroy(clearTarget);
        Check(FxRtPool.UsedBytes == 0, "Moving cloud fixture releases targets");
    }

    private IEnumerator TestPreCullVolumeFollow()
    {
        // In-game the camera rides the interpolated aircraft hierarchy, so it moves after
        // our LateUpdate follow: at cull time the composite cube trails the render pose.
        // The pass must re-follow pre-cull, or the marched sky composites through a stale
        // screen mapping and clouds trail the camera. A trailing cube must render exactly
        // like a fresh one (non-temporal march, so the pair is deterministic).
        var staleTarget = Target("Trailing cloud view");
        var freshTarget = Target("Fresh cloud view");
        var moving = CameraAt("Trailing cloud camera", staleTarget, 0);
        moving.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        moving.nearClipPlane = 1; moving.farClipPlane = 20000;
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.localScale = Vector3.one * 1000;
        var cloudRenderer = cube.GetComponent<Renderer>();
        var march = new Material(Resources.Load<Shader>("CloudEdgeFixture"));
        var composite = new Material(Resources.Load<Shader>("FlightCloudComposite"));
        composite.renderQueue = 2997; cloudRenderer.sharedMaterial = composite;
        cloudRenderer.shadowCastingMode = ShadowCastingMode.Off;
        var foreground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        foreground.transform.position = new Vector3(7, 5, 40);
        foreground.transform.rotation = Quaternion.Euler(10, 25, 17);
        foreground.transform.localScale = new Vector3(9, 7, 3);
        foreground.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("Occluder");
        var ridge = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ridge.transform.position = new Vector3(-8, -5, 60);
        ridge.transform.rotation = Quaternion.Euler(8, 17, -11);
        ridge.transform.localScale = new Vector3(22, 5, 6);
        ridge.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("Occluder");
        var view = new CloudVolumeUniforms();
        var clouds = new WeatherCloudPass();
        clouds.Bind(moving, cloudRenderer, march, composite, true, false,
            (camera, renderedView, projection) => view.ApplyView(camera, camera.transform.position, renderedView, projection));
        var stale = new Texture2D(1920, 1080, TextureFormat.RGBA32, false);
        var fresh = new Texture2D(1920, 1080, TextureFormat.RGBA32, false);
        var trail = new Vector3(20f, 0f, 0f);
        float worst = 0;
        // The first manual render never enqueues (URP assigns the camera renderer later
        // in that same render), so warm up once before comparing.
        RenderPipeline.SubmitRenderRequest(moving,
            new UniversalRenderPipeline.SingleCameraRequest { destination = freshTarget });
        log.AppendLine("Pre-cull follow warmup executed=" + clouds.ExecutedFrame);
        for (int frame = 0; frame < 12; frame++)
        {
            moving.transform.SetPositionAndRotation(new Vector3(frame * .04f, 0, -frame * .02f),
                Quaternion.Euler(Mathf.Sin(frame * .12f) * 9, Mathf.Sin(frame * .08f) * 5,
                    Mathf.Sin(frame * .15f) * 14));
            cube.transform.position = moving.transform.position + trail;
            RenderPipeline.SubmitRenderRequest(moving,
                new UniversalRenderPipeline.SingleCameraRequest { destination = staleTarget });
            int staleExecuted = clouds.ExecutedFrame;
            ReadTarget(staleTarget, stale);
            log.AppendLine("Pre-cull follow frame " + frame + " cube=" + cube.transform.position +
                " camera=" + moving.transform.position);
            cube.transform.position = moving.transform.position;
            RenderPipeline.SubmitRenderRequest(moving,
                new UniversalRenderPipeline.SingleCameraRequest { destination = freshTarget });
            ReadTarget(freshTarget, fresh);
            log.AppendLine("Pre-cull follow frame " + frame + " executed=" + staleExecuted + "/" + clouds.ExecutedFrame +
                " time=" + Time.frameCount);
            if (frame == 0)
            {
                Capture(staleTarget, "precull-stale-0.png");
                Capture(freshTarget, "precull-fresh-0.png");
            }
            float difference = MeanDelta(stale, fresh);
            worst = Mathf.Max(worst, difference);
            log.AppendLine("Pre-cull follow frame " + frame + " stale/fresh RGB difference " + difference.ToString("F5"));
            // Back-to-back submits carry ~0.004 of cross-submit noise; an un-erased
            // 20 m trail differs by ~0.07.
            Check(difference < .01f, "Trailing composite cube renders like a fresh one " + frame);
            yield return null;
        }
        log.AppendLine("Pre-cull follow worst stale/fresh RGB difference " + worst.ToString("F5"));
        clouds.Dispose();
        Destroy(moving.gameObject); Destroy(cube); Destroy(foreground); Destroy(ridge);
        Destroy(march); Destroy(composite); Destroy(stale); Destroy(fresh);
        staleTarget.Release(); freshTarget.Release(); Destroy(staleTarget); Destroy(freshTarget);
        Check(FxRtPool.UsedBytes == 0, "Pre-cull follow fixture releases targets");
    }

    private static void ReadTarget(RenderTexture target, Texture2D image)
    {
        RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
        RenderTexture.active = previous;
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
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
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
