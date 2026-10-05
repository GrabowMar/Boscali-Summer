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
        TestBudget();
        TestCloudSilhouette();
        IEnumerator motionChecks = TestMotionOcclusion();
        while (motionChecks.MoveNext()) yield return motionChecks.Current;
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
            (camera, view, projection) => { callbacks++; uniforms.ApplyView(camera, camera.transform.position, view, projection); });
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
            (camera, view, projection) => { callbacks++; uniforms.ApplyView(camera, camera.transform.position, view, projection); });
        for (int i = 0; i < 8; i++) { RenderPair(); yield return null; }
        var halfReference = Capture(mainTarget, "urp-half-reference.png");
        float meanDifference = MeanDelta(withCloud, halfReference);
        Check(meanDifference < .15f, "Settled temporal clouds stay close to full half-resolution reference");
        log.AppendLine("Mean settled temporal/reference RGB difference " + meanDifference.ToString("F4"));
        pass.Bind(main, volume, march, composite, true, true,
            (camera, view, projection) => { callbacks++; uniforms.ApplyView(camera, camera.transform.position, view, projection); });
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
            (camera, view, projection) => { callbacks++; uniforms.ApplyView(camera, camera.transform.position, view, projection); });
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
