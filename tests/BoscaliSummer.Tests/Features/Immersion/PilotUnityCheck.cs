#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BoscaliSummer.Modules.Immersion.Visuals;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Modules.Weather.Visuals;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// Unmodified Unity-only production helpers in a real URP player. This synthetic
// sixteen-bone pilot does not establish native aircraft fit or live acceptance.
public sealed class PilotUnityCheck : MonoBehaviour
{
    private readonly StringBuilder log = new StringBuilder();
    private int assertions;
    private CockpitPilotRig rig;
    private GameObject sourceRoot;
    private SkinnedMeshRenderer source;
    private Animator animator;
    private Mesh borrowedMesh;
    private Texture2D borrowedTexture;
    private Material borrowedMaterial;
    private Camera eye, foreign;
    private RenderTexture eyeTarget, foreignTarget, captureTarget;
    private Mesh reflectionPane;
    private Material reflectionMaterial;
    private bool drawReflection;
    private PilotReflection reflection;
    private GameObject canopyRoot;
    private bool tickReflection;
    private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    private static readonly string[] Names = { "pelvis", "chest", "neck", "head", "upperarm_L", "forearm_L", "hand_L", "upperarm_R", "forearm_R", "hand_R", "thigh_R", "shin_R", "foot_R", "thigh_L", "shin_L", "foot_L" };

#if UNITY_EDITOR
    public static void Build()
    {
        try
        {
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, "Assets/Resources/PilotFixtureRenderer.asset");
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.msaaSampleCount = 1;
            pipeline.renderScale = 1;
            pipeline.supportsCameraDepthTexture = true;
            PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.graphicsJobs = false;
            AssetDatabase.CreateAsset(pipeline, "Assets/Resources/PilotFixturePipeline.asset");
            GraphicsSettings.renderPipelineAsset = pipeline;
            QualitySettings.renderPipeline = pipeline;
            // Keep the native material's real property/keyword contract in the player.
            AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/Lit")), "Assets/Resources/PilotNativeMaterial.mat");
            foreach (string name in new[] { "PilotBody", "PilotCanopyReflection" })
            {
                Shader shader = Resources.Load<Shader>(name);
                if (shader == null || ShaderUtil.ShaderHasError(shader))
                {
                    string detail = "";
                    if (shader != null) foreach (var message in ShaderUtil.GetShaderMessages(shader))
                        detail += "\n" + message.severity + " " + message.file + ":" + message.line + " " + message.message;
                    throw new Exception("Shader invalid: " + name + detail);
                }
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Pilot URP check").AddComponent<PilotUnityCheck>();
            EditorSceneManager.SaveScene(scene, "Assets/check.unity");
            AssetDatabase.SaveAssets();
            var result = BuildPipeline.BuildPlayer(new[] { "Assets/check.unity" }, "Player/PilotCheck.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Player build failed: " + result.summary.result);
            File.WriteAllText("build-result.txt", "PASS real URP 14.0.12 pilot player");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { File.WriteAllText("build-result.txt", "FAIL " + error); Debug.LogException(error); EditorApplication.Exit(1); }
    }
#endif

    private IEnumerator Start()
    {
        IEnumerator checks = Run();
        bool failed = false;
        while (true)
        {
            bool next;
            try { next = checks.MoveNext(); }
            catch (Exception error) { log.AppendLine("FAIL " + error); Debug.LogException(error); failed = true; break; }
            if (!next) break;
            yield return checks.Current;
        }
        rig?.Release();
        reflection?.Release();
        for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Destroy(owned[i]);
        log.AppendLine(failed ? "FAILED" : "PASS: " + assertions + " pilot URP assertions");
        File.WriteAllText("result.txt", log.ToString());
        Application.Quit(failed ? 1 : 0);
    }

    private IEnumerator Run()
    {
        log.AppendLine("GPU " + SystemInfo.graphicsDeviceName + " | " + SystemInfo.graphicsDeviceType + " | 1920x1080");
        Check(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset, "URP pipeline active");
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.4f, .4f, .4f);
        var keyLight = new GameObject("Pilot fixture main light"); owned.Add(keyLight);
        var light = keyLight.AddComponent<Light>(); light.type = LightType.Directional;
        light.intensity = 1.4f; light.transform.rotation = Quaternion.Euler(35, 145, 0);
        QualitySettings.SetQualityLevel(Math.Max(0, QualitySettings.names.Length - 1), false);
        FxBus.SetAdaptiveCap(null);
        Shader shader = Resources.Load<Shader>("PilotBody");
        Check(shader != null && shader.isSupported, "Body shader supported");
        Check(Resources.Load<Shader>("PilotCanopyReflection").isSupported, "Glass shader supported");
        eyeTarget = Target("Cockpit frame", 1920, 1080);
        foreignTarget = Target("Foreign frame", 1920, 1080);
        captureTarget = Target("Pilot capture", 256, 256);
        eye = CameraAt("Cockpit camera", eyeTarget, 0);
        foreign = CameraAt("Foreign camera", foreignTarget, 1);
        foreign.cullingMask = -1;
        CreateSource();
        rig = new CockpitPilotRig();
        AnimatorCullingMode borrowedCulling = animator.cullingMode;
        Check(rig.Bind(source, animator, shader, eye), "Native-shaped sixteen-bone rig binds");
        rig.SetLight(1);
        Check(rig.Renderer != null && rig.Renderer != source && rig.Renderer.sharedMesh == borrowedMesh, "One owned renderer shares unreadable mesh");
        Check(!borrowedMesh.isReadable, "CPU-inaccessible source mesh exercised");
        Check(rig.Renderer.bones.Length == 16, "Sixteen linked bones retained");
        Check(rig.BodyRenderer == rig.Renderer, "Unrecognised synthetic geometry uses its own mesh rather than a native replacement");
        Check(rig.Head == rig.Bone("head") && rig.Chest == rig.Bone("chest"), "Head/chest named access resolves");
        Check(rig.Bone("thigh_L").parent == rig.Bone("pelvis").parent, "Sibling thigh hierarchy preserved");
        Check(Vector3.Distance(rig.Head.position, source.bones[3].position) < .00001f, "Armature scale100 preserved in world position");
        Check(rig.Renderer.GetComponentsInChildren<Collider>(true).Length == 0 && rig.Renderer.GetComponentsInChildren<Rigidbody>(true).Length == 0,
            "Visual copy contains no physics components");
        Transform[] nativeBones = source.bones;
        Vector3[] nativePositions = new Vector3[16];
        Quaternion[] nativeRotations = new Quaternion[16];
        for (int i = 0; i < 16; i++) { nativePositions[i] = nativeBones[i].localPosition; nativeRotations[i] = nativeBones[i].localRotation; }
        rig.CopySeatedPose();
        rig.SetBodyVisible(true);
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        Save(eyeTarget, "body-front.png");
        Check(BrightPixels(eyeTarget, .20f, .65f) > 10000, "URP direct body produces visible torso pixels");
        Check(BrightPixels(foreignTarget, 0, 1) < 50, "Foreign camera excludes pilot despite matching culling mask");
        DrawCapture(eye);
        Save(captureTarget, "pilot-capture-full.png");
        int completeHead = NonzeroAlpha(captureTarget, .78f, 1f);
        int directHead = BrightPixels(eyeTarget, .78f, 1f);
        Check(completeHead > 250, "Capture pass includes complete helmet/head");
        Check(directHead < 100, "Direct pass masks head volume");

        var stick = new GameObject("Native control grip fixture"); owned.Add(stick);
        var throttle = new GameObject("Native throttle grip fixture"); owned.Add(throttle);
        stick.transform.position = rig.Bone("hand_R").position;
        throttle.transform.position = rig.Bone("hand_L").position;
        rig.SetControls(stick.transform, throttle.transform);
        Quaternion handNeutral = rig.Bone("hand_R").rotation;
        Quaternion footNeutral = rig.Bone("foot_R").rotation;
        for (int frame = 0; frame < 45; frame++)
        {
            stick.transform.position = source.bones[9].position + new Vector3(.02f, .015f, -.03f);
            stick.transform.rotation = Quaternion.Euler(12, -9, -8);
            throttle.transform.position = source.bones[6].position + new Vector3(0, .01f, .03f);
            throttle.transform.rotation = Quaternion.Euler(-11, 0, 0);
            rig.CopySeatedPose();
            rig.Pose(.8f, -.6f, .7f, .9f, new Vector3(.8f, 2.5f, 0), Quaternion.Euler(-30, 65, 0), 1f / 60f, true, false);
            yield return null;
        }
        Check(Quaternion.Angle(handNeutral, rig.Bone("hand_R").rotation) > .5f, "Control inputs move owned hand pose");
        Check(Vector3.Distance(rig.Bone("hand_R").position, stick.transform.position) < .005f, "Reachable stick grip stays in hand contact");
        Check(Vector3.Distance(rig.Bone("hand_L").position, throttle.transform.position) < .005f, "Reachable throttle grip stays in hand contact");
        Check(Quaternion.Angle(footNeutral, rig.Bone("foot_R").rotation) > .5f, "Rudder moves owned foot pose");
        for (int i = 0; i < 16; i++) Check(nativeBones[i].localPosition == nativePositions[i] && nativeBones[i].localRotation == nativeRotations[i], "Native bone remains unchanged: " + Names[i]);
        yield return new WaitForEndOfFrame();
        Save(eyeTarget, "body-controls.png");
        DrawCapture(eye);
        Save(captureTarget, "pilot-capture-controls.png");
        rig.SetBodyVisible(false);
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        Check(BrightPixels(eyeTarget, 0, 1) < 50, "Body visibility can be disabled independently");
        Check(rig.Renderer.enabled, "Fallback full native renderer remains available for explicit reflection capture");
        DrawCapture(eye);
        Check(NonzeroAlpha(captureTarget, 0, 1) > 3000, "Reflection-only capture keeps complete skinned body");
        Save(captureTarget, "pilot-capture-body-off.png");
        Quaternion capturedLook = rig.Head.rotation;
        for (int frame = 0; frame < 30; frame++)
        {
            rig.CopySeatedPose();
            rig.Pose(-.7f, .4f, -.7f, .15f, Vector3.up, Quaternion.Euler(20, -55, 0), 1f / 60f, true, false);
            yield return null;
        }
        Check(Quaternion.Angle(capturedLook, rig.Head.rotation) > 5, "Head continues moving while direct body is off");
        DrawCapture(eye);
        Save(captureTarget, "pilot-capture-body-off-moved.png");
        CheckGripWeld(stick.transform, throttle.transform);
        CheckPoseTransitions(stick.transform, throttle.transform);
        rig.SetBodyVisible(true);
        eye.transform.position = new Vector3(.65f, 1.05f, 1.7f);
        eye.transform.LookAt(new Vector3(0, .8f, .15f));
        yield return new WaitForEndOfFrame();
        Save(eyeTarget, "body-side.png");
        eye.transform.position = new Vector3(0, 1.55f, .78f);
        eye.transform.LookAt(new Vector3(0, .7f, .25f));
        eye.orthographicSize = .75f;
        yield return new WaitForEndOfFrame();
        Save(eyeTarget, "body-down.png");
        IEnumerator glassChecks = CheckGlassShader();
        while (glassChecks.MoveNext()) yield return glassChecks.Current;
        IEnumerator reflectionChecks = CheckReflectionLifecycle();
        while (reflectionChecks.MoveNext()) yield return reflectionChecks.Current;
        IEnumerator performance = CheckPairedPerformance();
        while (performance.MoveNext()) yield return performance.Current;
        rig.Release(); rig.Release();
        yield return null;
        Check(rig.Renderer == null, "Repeated release clears renderer ownership");
        Check(borrowedMesh != null && borrowedTexture != null && borrowedMaterial != null, "Borrowed mesh texture material survive release");
        Check(animator.cullingMode == borrowedCulling, "Borrowed Animator culling restored");
        Check(!rig.Bind(null, animator, shader, eye), "Missing source fails closed");
        Transform missing = source.bones[3];
        Transform[] broken = (Transform[])source.bones.Clone();
        broken[3] = null; source.bones = broken;
        Check(!rig.Bind(source, animator, shader, eye), "Missing head bone fails closed");
        broken[3] = missing; source.bones = broken;
        Check(rig.Bind(source, animator, shader, eye), "Valid source rebinds after release");
        rig.CopySeatedPose();
        rig.Pose(0,0,0,0,Vector3.up,Quaternion.identity,1f/60,false,true);
        Check(Vector3.Distance(rig.Bone("hand_R").position, source.bones[9].position) < .00001f,
            "Respawn clears the previous pilot's control target");
        stick.transform.SetPositionAndRotation(source.bones[9].position, Quaternion.identity);
        rig.SetControls(stick.transform, null);
        stick.transform.position += new Vector3(.015f,.01f,-.03f);
        rig.CopySeatedPose(); rig.Pose(0,0,0,0,Vector3.up,Quaternion.identity,1f/60,false,true);
        Check(Vector3.Distance(rig.Bone("hand_R").position, stick.transform.position) < .0005f,
            "Respawned pilot attaches to its newly bound stick with motion disabled");
        Destroy(source); yield return null;
        Check(!rig.Valid, "Loss of borrowed renderer alone invalidates live rig");
        rig.Release();
        CreateSource();
        Check(rig.Bind(source, animator, shader, eye), "Fresh native source binds after renderer loss");
        Destroy(source.bones[3].gameObject); yield return null;
        Check(!rig.Valid, "Loss of a single borrowed bone invalidates live rig");
        rig.Release();
        Destroy(sourceRoot); yield return null;
        Check(!rig.Bind(source, animator, shader, eye), "Destroyed source fails closed");
        IEnumerator improvedChecks = CheckNativeHeadAndMaterial(shader);
        while (improvedChecks.MoveNext()) yield return improvedChecks.Current;
        CheckAttackHeloGrip(shader);
        log.AppendLine("Boundary: synthetic URP source closure; game camera stack, native fit, rain ordering and live performance remain unverified.");
    }

    private void Update()
    {
        if (tickReflection) reflection.Tick(rig, canopyRoot.transform, eye, eye, 1, true);
        if (drawReflection && reflectionPane != null && reflectionMaterial != null)
            Graphics.DrawMesh(reflectionPane, Matrix4x4.identity, reflectionMaterial, 3, eye, 0, null, ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
    }

    private void LateUpdate()
    {
        // Batch players skip the display render loop. Submit supported URP requests so
        // these are real pipeline renders including its normal camera callbacks.
        if (eye != null && eye.enabled) RenderPipeline.SubmitRenderRequest(eye,
            new UniversalRenderPipeline.SingleCameraRequest { destination = eyeTarget });
        if (foreign != null && foreign.enabled) RenderPipeline.SubmitRenderRequest(foreign,
            new UniversalRenderPipeline.SingleCameraRequest { destination = foreignTarget });
    }

    private IEnumerator CheckReflectionLifecycle()
    {
        log.AppendLine("Bundle resources: " + string.Join(",", typeof(PilotShaderBundle).Assembly.GetManifestResourceNames()));
        using (var sha = System.Security.Cryptography.SHA256.Create())
        using (Stream embedded = typeof(PilotShaderBundle).Assembly.GetManifestResourceStream("BoscaliSummer.Immersion.pilot.bundle"))
        using (Stream approved = File.OpenRead("pilot.bundle"))
        {
            string embeddedHash = BitConverter.ToString(sha.ComputeHash(embedded)).Replace("-", "");
            string approvedHash = BitConverter.ToString(sha.ComputeHash(approved)).Replace("-", "");
            log.AppendLine("Embedded production bundle SHA256 " + embeddedHash);
            Check(embeddedHash == approvedHash, "Player embedded bundle bytes match the approved production snapshot");
        }
        Shader bundledBody = PilotShaderBundle.GetBody(), bundledGlass = PilotShaderBundle.GetReflection();
        log.AppendLine("Bundled shaders: body=" + (bundledBody != null ? bundledBody.name : "null") + " glass=" + (bundledGlass != null ? bundledGlass.name : "null"));
        AssetBundle loadedBundle = (AssetBundle)typeof(PilotShaderBundle).GetField("bundle", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        if (loadedBundle != null) foreach (Shader bundled in loadedBundle.LoadAllAssets<Shader>())
            log.AppendLine("Bundle shader " + bundled.name + " supported=" + bundled.isSupported);
        Check(PilotShaderBundle.GetBody() != null && PilotShaderBundle.GetReflection() != null, "Unchanged bounded loader resolves embedded production bundle shaders");
        Mesh firstPersonMesh = PilotShaderBundle.GetFirstPersonMesh();
        Check(firstPersonMesh != null && firstPersonMesh.vertexCount > 0 && firstPersonMesh.bindposes.Length == 16 && !firstPersonMesh.isReadable,
            "Embedded first-person mesh retains sixteen bind poses without CPU-readable vertices");
        rig.SetLight(1); rig.SetBodyVisible(false);
        canopyRoot = new GameObject("Fixture cockpit"); owned.Add(canopyRoot);
        var pane = new GameObject("Canopy glass"); pane.layer = 3; pane.transform.SetParent(canopyRoot.transform, false);
        pane.AddComponent<MeshFilter>().sharedMesh = reflectionPane;
        var nativePane = pane.AddComponent<MeshRenderer>();
        var nativeGlass = new Material(Resources.Load<Shader>("PilotBody")) { name = "Native cockpit glass", renderQueue = 3000 }; owned.Add(nativeGlass);
        nativePane.sharedMaterial = nativeGlass;
        CanopyGlassResolver.ResetForScene();
        reflection = new PilotReflection(); reflection.SetGlassView(new CanopyGlassView()); tickReflection = true;
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        Check(reflection.Target != null && reflection.Target.width == 256 && reflection.Target.height == 256,
            "Production reflection owns one256x256 target");
        Check(reflection.Target.depth == 16 && reflection.Target.antiAliasing == 1 && !reflection.Target.useMipMap,
            "Capture target depth16 noMSAA noMips");
        Check(reflection.GlassDraws == 1 && reflection.CaptureCount > 0, "Verified canopy submits bounded production reflection");
        Check(FxRtPool.UsedBytes == 256L * 256 * 6, "Production target accounted by FX ledger");
        Check(nativePane.sharedMaterial == nativeGlass, "Native glass material preserved");
        Check(NonzeroAlpha(reflection.Target, 0, 1) > 3000, "Production capture draws current complete rig with body off");
        Save(reflection.Target, "production-pilot-capture.png");
        Save(eyeTarget, "production-reflection-body-off.png");
        Check(BrightPixels(foreignTarget, 0, 1) < 50, "Production reflection absent from foreign camera");
        int initialCaptures = reflection.CaptureCount;
        double started = Time.unscaledTimeAsDouble;
        while (Time.unscaledTimeAsDouble - started < 1.1) yield return null;
        double elapsed = Time.unscaledTimeAsDouble - started;
        Check(reflection.CaptureCount - initialCaptures <= Math.Floor(elapsed * 10) + 1, "Production cadence remains at most ten captures per second");
        Texture2D beforeImage = Read(reflection.Target); Color32[] before = beforeImage.GetPixels32(); Destroy(beforeImage);
        Save(reflection.Target, "production-capture-before-motion.png");
        int motionCaptures = reflection.CaptureCount;
        double motionAt = Time.unscaledTimeAsDouble;
        Quaternion motionHead = rig.Head.rotation;
        for (int frame = 0; frame < 45; frame++)
        {
            rig.CopySeatedPose(); rig.Pose(.7f, .5f, .7f, .8f, Vector3.up, Quaternion.Euler(-20, 60, 0), 1f / 60, true, false);
            yield return null;
        }
        // Batch mode may execute45 frames in25ms. Wait for the real10Hz clock so
        // the pixel comparison actually includes a post-motion production capture.
        yield return new WaitForSecondsRealtime(.15f);
        Texture2D afterImage = Read(reflection.Target); Color32[] after = afterImage.GetPixels32(); Destroy(afterImage);
        Save(reflection.Target, "production-capture-after-motion.png");
        int changed = 0;
        for (int i = 0; i < before.Length; i++) if (before[i].r != after[i].r || before[i].g != after[i].g || before[i].b != after[i].b || before[i].a != after[i].a) changed++;
        log.AppendLine("Motion capture diagnostic: before=" + motionCaptures + " after=" + reflection.CaptureCount + " elapsed=" + (Time.unscaledTimeAsDouble-motionAt) +
            " headAngle=" + Quaternion.Angle(motionHead, rig.Head.rotation) + " changedPixels=" + changed + " glassDraws=" + reflection.GlassDraws +
            " captureAt=" + typeof(PilotReflection).GetField("capturedAt", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(reflection) +
            " captureRect=" + typeof(PilotReflection).GetField("captureRect", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(reflection));
        Check(changed > 100, "Production capture updates moving skinning while body remains off");
        nativePane.enabled = false;
        yield return null;
        int suspended = reflection.CaptureCount;
        for (int frame = 0; frame < 10; frame++) yield return null;
        Check(reflection.CaptureCount == suspended && reflection.GlassDraws == 0, "No visible pane suspends all reflection work");
        nativePane.enabled = true;
        double resumeDeadline = Time.unscaledTimeAsDouble + .3;
        bool staleHidden = true;
        while (reflection.CaptureCount == suspended && Time.unscaledTimeAsDouble < resumeDeadline)
        {
            staleHidden &= reflection.GlassDraws == 0;
            yield return null;
        }
        Check(staleHidden, "Stale resume content stays hidden until next due capture");
        Check(reflection.CaptureCount == suspended + 1 && reflection.GlassDraws == 1, "Resume refreshes before glass becomes visible");
        int alternatingCaptures = reflection.CaptureCount;
        double alternatingStart = Time.unscaledTimeAsDouble;
        int alternatingFrame = 0;
        while (Time.unscaledTimeAsDouble - alternatingStart < 1.1)
        {
            nativePane.enabled = (alternatingFrame++ & 1) == 0;
            yield return null;
        }
        Check(reflection.CaptureCount - alternatingCaptures <= Math.Floor((Time.unscaledTimeAsDouble - alternatingStart) * 10) + 1,
            "Rapid pane visibility alternation cannot bypass capture cadence");
        nativePane.enabled = true;
        tickReflection = false;
        reflection.Release(); reflection.Release();
        Check(reflection.Target == null && reflection.CaptureCount == 0 && reflection.GlassDraws == 0 && FxRtPool.UsedBytes == 0,
            "Repeated reflection release clears target commandbuffer and FX ownership");
        var reservation = new RenderTexture(4096, 1536, 0, RenderTextureFormat.ARGB32); owned.Add(reservation);
        Check(FxRtPool.Own(reservation), "Reserve entire ledger without allocating GPU image");
        reflection.Tick(rig, canopyRoot.transform, eye, eye, 1, true);
        Check(reflection.Target == null && reflection.GlassDraws == 0, "FX target refusal fails closed");
        FxRtPool.Disown(reservation); reflection.Release();
        FxBus.SetAdaptiveCap(FxQuality.Low);
        reflection.Tick(rig, canopyRoot.transform, eye, eye, 1, true);
        Check(reflection.Target != null && reflection.Target.width == 128, "Low quality uses128x128 pilot capture");
        reflection.Release(); FxBus.SetAdaptiveCap(null);
        Check(nativePane.sharedMaterial == nativeGlass, "Reset preserves native glass material");
        canopyRoot.SetActive(false);
    }

    private IEnumerator CheckPairedPerformance()
    {
        eye.transform.position = new Vector3(0, 1.4f, .1f);
        eye.transform.LookAt(new Vector3(0, .8f, .8f)); eye.fieldOfView = 80;
        foreign.enabled = false;
        canopyRoot.SetActive(true);
        tickReflection = false;
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120;
        var timing = new FrameTiming[1];
        var csv = new StringBuilder("mode,cpu_submit_mean_ms,cpu_submit_max_ms,capture_submit_mean_ms,capture_submit_max_ms,gpu_frame_mean_ms,gpu_samples,owned_loop_alloc_bytes,captures\n");
        foreach (string mode in new[] { "off", "body", "reflection128", "reflection256" })
        {
            bool body = mode != "off", reflected = mode.StartsWith("reflection", StringComparison.Ordinal);
            FxBus.SetAdaptiveCap(mode == "reflection128" ? FxQuality.Low : FxQuality.High);
            reflection.Release(); rig.SetBodyVisible(body);
            for (int frame = 0; frame < 20; frame++)
            {
                if (body) { rig.CopySeatedPose(); rig.Pose(.3f,.2f,.2f,.5f,Vector3.up,Quaternion.identity,1f/120,true,false); }
                if (reflected) reflection.Tick(rig, canopyRoot.transform, eye, eye, 1, true);
                yield return null;
            }
            double total = 0, maximum = 0, captureTotal = 0, captureMaximum = 0, gpuTotal = 0;
            int captureFrames = 0, gpuSamples = 0, capturesBefore = reflection.CaptureCount;
            long allocations = 0;
            for (int frame = 0; frame < 120; frame++)
            {
                int beforeCapture = reflection.CaptureCount;
                long beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                if (body) { rig.CopySeatedPose(); rig.Pose(.3f,.2f,.2f,.5f,Vector3.up,Quaternion.identity,1f/120,true,false); }
                if (reflected) reflection.Tick(rig, canopyRoot.transform, eye, eye, 1, true);
                double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                allocations += GC.GetAllocatedBytesForCurrentThread() - beforeAlloc;
                total += ms; maximum = Math.Max(maximum, ms);
                if (reflection.CaptureCount != beforeCapture) { captureFrames++; captureTotal += ms; captureMaximum = Math.Max(captureMaximum, ms); }
                FrameTimingManager.CaptureFrameTimings();
                yield return new WaitForEndOfFrame();
                uint available = FrameTimingManager.GetLatestTimings(1, timing);
                if (available > 0 && timing[0].gpuFrameTime > 0) { gpuSamples++; gpuTotal += timing[0].gpuFrameTime; }
            }
            csv.Append(mode).Append(',').Append((total / 120).ToString("F5", System.Globalization.CultureInfo.InvariantCulture))
                .Append(',').Append(maximum.ToString("F5", System.Globalization.CultureInfo.InvariantCulture))
                .Append(',').Append((captureFrames == 0 ? 0 : captureTotal / captureFrames).ToString("F5", System.Globalization.CultureInfo.InvariantCulture))
                .Append(',').Append(captureMaximum.ToString("F5", System.Globalization.CultureInfo.InvariantCulture))
                .Append(',').Append(gpuSamples == 0 ? "unavailable" : (gpuTotal / gpuSamples).ToString("F5", System.Globalization.CultureInfo.InvariantCulture))
                .Append(',').Append(gpuSamples).Append(',').Append(allocations).Append(',').Append(reflection.CaptureCount - capturesBefore).AppendLine();
            Check(allocations == 0, "Zero managed allocations in warmed production loop: " + mode);
            Save(eyeTarget, "performance-" + mode + ".png");
        }
        File.WriteAllText("paired-performance.csv", csv.ToString());
        log.AppendLine("Timing: CPU submission measured independently; FrameTimingManager GPU frame times when available; no GPU attribution inferred from CPU timings.");
        reflection.Release(); FxBus.SetAdaptiveCap(null);
        canopyRoot.SetActive(false);
    }

    private IEnumerator CheckGlassShader()
    {
        eye.transform.position = new Vector3(0, .85f, 2.7f);
        eye.transform.LookAt(new Vector3(0, .85f, 0)); eye.orthographicSize = .9f;
        Matrix4x4 frontVP = GL.GetGPUProjectionMatrix(eye.projectionMatrix, true) * eye.worldToCameraMatrix;
        DrawCapture(eye);
        rig.SetBodyVisible(false);
        eye.transform.position = new Vector3(0, .85f, .55f);
        eye.transform.LookAt(new Vector3(0, .85f, 1.4f));
        eye.orthographic = false; eye.fieldOfView = 70;
        reflectionPane = new Mesh { name = "Curved canopy fixture" }; owned.Add(reflectionPane);
        var vertices = new Vector3[10]; var normals = new Vector3[10]; var triangles = new int[24];
        for (int column = 0; column < 5; column++)
        {
            float x = (column - 2) * .35f;
            float z = 1.2f - .12f * x * x;
            vertices[column * 2] = new Vector3(x, .05f, z);
            vertices[column * 2 + 1] = new Vector3(x, 1.65f, z);
            normals[column * 2] = normals[column * 2 + 1] = new Vector3(-.24f * x, 0, -1).normalized;
            if (column == 4) continue;
            int t = column * 6, v = column * 2;
            triangles[t] = v; triangles[t+1] = v+1; triangles[t+2] = v+3;
            triangles[t+3] = v; triangles[t+4] = v+3; triangles[t+5] = v+2;
        }
        reflectionPane.vertices = vertices; reflectionPane.normals = normals; reflectionPane.triangles = triangles;
        reflectionPane.RecalculateBounds();
        reflectionMaterial = new Material(Resources.Load<Shader>("PilotCanopyReflection")); owned.Add(reflectionMaterial);
        reflectionMaterial.SetTexture("_PilotTex", captureTarget);
        reflectionMaterial.SetMatrix("_WorldToPilot", Matrix4x4.identity);
        reflectionMaterial.SetVector("_CaptureRect", new Vector4(-1.6f, -.05f, 3.2f, 1.8f));
        reflectionMaterial.SetVector("_EyeWorld", eye.transform.position);
        reflectionMaterial.SetFloat("_ReflectionStrength", .12f);
        reflectionMaterial.SetFloat("_LightLevel", 1);
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        Texture2D baseline = Read(eyeTarget); Color32[] without = baseline.GetPixels32(); Destroy(baseline);
        drawReflection = true;
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        Save(eyeTarget, "reflection-curved-day.png");
        Texture2D dayImage = Read(eyeTarget); Color32[] day = dayImage.GetPixels32(); Destroy(dayImage);
        int changed = 0, peak = 0, totalDay = 0;
        for (int pixel = 0; pixel < day.Length; pixel++)
        {
            int delta = Math.Abs(day[pixel].r - without[pixel].r) + Math.Abs(day[pixel].g - without[pixel].g) + Math.Abs(day[pixel].b - without[pixel].b);
            if (delta > 3) changed++;
            peak = Math.Max(peak, delta); totalDay += delta;
        }
        Check(changed > 200, "Curved canopy reflects sampled full pilot in URP");
        Check(HelmetAboveBody(day, eyeTarget.width), "Captured helmet remains above torso in canopy reflection");
        Check(peak < 125, "Glass blend stays restrained within opacity cap");
        Check(BrightPixels(foreignTarget, 0, 1) < 50, "Reflection draw filtered to cockpit camera");
        rig.SetLight(.12f); DrawCapture(frontVP);
        yield return new WaitForEndOfFrame();
        Save(eyeTarget, "reflection-curved-night.png");
        Texture2D nightImage = Read(eyeTarget); Color32[] night = nightImage.GetPixels32(); Destroy(nightImage);
        int totalNight = 0;
        for (int pixel = 0; pixel < night.Length; pixel++)
            totalNight += Math.Abs(night[pixel].r - without[pixel].r) + Math.Abs(night[pixel].g - without[pixel].g) + Math.Abs(night[pixel].b - without[pixel].b);
        Check(totalNight < totalDay, "Night reflection brightness decreases");
        var halfAlpha = new Texture2D(1,1,TextureFormat.RGBA32,false,true) { name = "Premultiplied half-alpha pixel", filterMode = FilterMode.Point }; owned.Add(halfAlpha);
        halfAlpha.SetPixel(0,0,new Color(.5f,.5f,.5f,.5f)); halfAlpha.Apply();
        reflectionMaterial.SetTexture("_PilotTex",halfAlpha);
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        Texture2D halfFrame = Read(eyeTarget);
        int centreIndex = eyeTarget.height/2*eyeTarget.width + eyeTarget.width/2;
        float premultiplied = 128f/255f, opacity = .12f*.35f;
        float expected = premultiplied*opacity + without[centreIndex].r/255f*(1-premultiplied*opacity);
        float observed = halfFrame.GetPixel(eyeTarget.width/2,eyeTarget.height/2).r;
        Destroy(halfFrame);
        log.AppendLine("Premultiplied-alpha GPU diagnostic: observed=" + observed + " expected=" + expected);
        Check(Mathf.Abs(observed-expected) < .0041f, "Fractional capture alpha preserves premultiplied edge colour without squaring alpha");
        reflectionMaterial.SetTexture("_PilotTex",captureTarget);
        reflectionMaterial.SetVector("_EyeWorld", new Vector3(0, .85f, 3));
        yield return new WaitForEndOfFrame();
        Check(BrightPixels(eyeTarget, 0, 1) < 50, "Reflected rays pointing away from pilot fail closed");
        drawReflection = false;
    }

    private void CheckGripWeld(Transform stick, Transform throttle)
    {
        // A lever rotates around its base, so its cached contact point must have a
        // nonzero local offset. Both the native source and the owned visual copy
        // move with an aircraft frame rather than remaining on world axes.
        var cockpit = new GameObject("Moving cockpit grip fixture"); owned.Add(cockpit);
        Transform copiedRoot = rig.Renderer.transform.root, originalFrame = rig.Frame;
        sourceRoot.transform.SetParent(cockpit.transform, false);
        copiedRoot.SetParent(cockpit.transform, false);
        stick.SetParent(cockpit.transform, false); throttle.SetParent(cockpit.transform, false);
        rig.SetFrame(cockpit.transform); rig.CopySeatedPose();
        CheckDelayedGripBinding(cockpit.transform);
        rig.CopySeatedPose();
        Vector3 rightRest = cockpit.transform.InverseTransformPoint(source.bones[9].position);
        var nativePositions = new Vector3[source.bones.Length];
        var nativeRotations = new Quaternion[source.bones.Length];
        for (int bone = 0; bone < source.bones.Length; bone++)
        { nativePositions[bone] = source.bones[bone].localPosition; nativeRotations[bone] = source.bones[bone].localRotation; }
        Vector3 contact = new Vector3(.015f,.04f,-.012f);
        stick.localRotation = Quaternion.identity;
        stick.localPosition = rightRest - contact;
        rig.SetControls(null, null);
        rig.AttachControls(stick, contact, Quaternion.identity, null, Vector3.zero, Quaternion.identity);
        Quaternion bindRotation = rig.Bone("hand_R").rotation;
        stick.localRotation = Quaternion.Euler(12,0,0);
        stick.localPosition += new Vector3(.01f,.01f,-.025f);
        rig.CopySeatedPose(); rig.Pose(0,0,0,0,Vector3.up,Quaternion.identity,1f/60,false,true);
        Quaternion attachedRotation = rig.Bone("hand_R").rotation;
        rig.CopySeatedPose();
        throttle.SetPositionAndRotation(source.bones[6].position, Quaternion.identity);
        rig.AttachControls(stick, Vector3.one, Quaternion.Euler(0,80,0), throttle, Vector3.zero, Quaternion.identity);
        rig.Pose(0,0,0,0,Vector3.up,Quaternion.identity,1f/60,false,true);
        Check(rig.StickBound && rig.ThrottleBound && Vector3.Distance(rig.Bone("hand_R").position, stick.TransformPoint(contact)) < .0005f &&
            Quaternion.Angle(rig.Bone("hand_R").rotation, attachedRotation) < .05f,
            "Delayed throttle attachment preserves an already tilted stick hand's anchor and orientation");
        rig.SetControls(null, null); rig.CopySeatedPose();
        stick.localRotation = Quaternion.identity;
        stick.localPosition = rightRest - contact;
        rig.AttachControls(stick, contact, Quaternion.identity, null, Vector3.zero, Quaternion.identity);
        float maximumError = 0;
        for (int sample = 0; sample < 36; sample++)
        {
            float angle = sample * Mathf.PI / 18f;
            cockpit.transform.SetPositionAndRotation(new Vector3(17 + sample * .03f, 4, -9), Quaternion.Euler(12, 71, -18));
            stick.localPosition = rightRest - contact + new Vector3(Mathf.Sin(angle) * .025f, .01f, -.025f);
            stick.localRotation = Quaternion.Euler(Mathf.Cos(angle) * 18, Mathf.Sin(angle) * 24, -8);
            rig.CopySeatedPose();
            rig.Pose(.8f,-.6f,.5f,.9f,new Vector3(.8f,2.5f,0),Quaternion.Euler(-15,40,0),
                sample % 3 == 0 ? 0 : 1f/60, sample % 2 == 0, sample % 4 == 0);
            float error = Vector3.Distance(rig.Bone("hand_R").position, stick.TransformPoint(contact));
            maximumError = Mathf.Max(maximumError, error);
            Check(error < .0005f, "Moving cached stick contact remains welded, sample " + sample);
            Check(Quaternion.Angle(rig.Bone("hand_R").rotation, stick.rotation * bindRotation) < .05f,
                "Hand orientation follows moving stick, sample " + sample);
            Check(Quaternion.Angle(rig.Bone("forearm_L").localRotation, source.bones[5].localRotation) < .001f,
                "Missing throttle leaves opposite hand seated, sample " + sample);
        }
        log.AppendLine("Moving stick maximum wrist-contact error: " + (maximumError * 1000).ToString("F4") + " mm");
        rig.AttachControls(null, Vector3.zero, Quaternion.identity, throttle, Vector3.zero, Quaternion.identity);
        rig.CopySeatedPose(); rig.Pose(0,0,0,0,Vector3.up,Quaternion.identity,1f/60,false,true);
        long beforeWeldAllocations = GC.GetAllocatedBytesForCurrentThread();
        for (int sample = 0; sample < 120; sample++)
        {
            rig.CopySeatedPose();
            rig.Pose(.8f,-.6f,.5f,.9f,new Vector3(.8f,2.5f,0),Quaternion.Euler(-15,40,0),1f/60,true,false);
        }
        long weldAllocations = GC.GetAllocatedBytesForCurrentThread() - beforeWeldAllocations;
        Check(rig.StickBound && rig.ThrottleBound && weldAllocations == 0,
            "Both cached hand welds produce zero managed allocations across 120 warmed updates");
        Vector3 shoulder = source.bones[7].position;
        float reach = Vector3.Distance(shoulder, source.bones[8].position) + Vector3.Distance(source.bones[8].position, source.bones[9].position);
        Vector3 fullThrow = shoulder + (source.bones[9].position - shoulder).normalized * (reach + .02f);
        stick.position = fullThrow - stick.TransformVector(contact);
        rig.CopySeatedPose(); rig.Pose(0,0,0,0,Vector3.up,Quaternion.identity,1f/60,false,true);
        Check(Vector3.Distance(rig.Bone("hand_R").position, fullThrow) < .0005f,
            "Small full-throw reach extension keeps the wrist welded");
        for (int bone = 0; bone < source.bones.Length; bone++)
            Check(rig.Bone(Names[bone]) != source.bones[bone] && source.bones[bone].localPosition == nativePositions[bone] &&
                source.bones[bone].localRotation == nativeRotations[bone], "Weld leaves native bone untouched: " + Names[bone]);
        sourceRoot.transform.SetParent(null, false); copiedRoot.SetParent(null, false);
        stick.SetParent(null, false); throttle.SetParent(null, false);
        rig.SetFrame(originalFrame); rig.CopySeatedPose();
        stick.SetPositionAndRotation(rig.Bone("hand_R").position, Quaternion.identity);
        throttle.SetPositionAndRotation(rig.Bone("hand_L").position, Quaternion.identity);
        rig.SetControls(stick, throttle);
        rig.Pose(0,0,0,0,Vector3.up,Quaternion.Euler(-15,40,0),0,true,false);
    }

    private void CheckAttackHeloGrip(Shader shader)
    {
        // Exported AttackHelo1 coordinates after its native seated clip finishes.
        // Keep its actual torso pivot so bracing exercises real shoulder motion.
        CreateSource();
        source.bones[1].SetPositionAndRotation(new Vector3(0,.178146639f,2.406972298f),
            new Quaternion(-.12432188f,0,0,.99224194f));
        source.bones[7].SetPositionAndRotation(new Vector3(.155818590f,.481096734f,2.329844844f),
            Quaternion.LookRotation(new Vector3(-.147736096f,-.240566954f,.959323183f), new Vector3(.572078769f,-.812021336f,-.115528198f)));
        source.bones[8].SetPositionAndRotation(new Vector3(.296197424f,.281839919f,2.301496079f),
            Quaternion.LookRotation(new Vector3(-.563795248f,.177385072f,.806640105f), new Vector3(-.283033700f,-.959020731f,.013070158f)));
        source.bones[9].SetPositionAndRotation(new Vector3(.236869944f,.080816999f,2.304235754f),
            new Quaternion(-.307685f,-.044829f,.856020f,.412978f));
        source.bones[4].SetPositionAndRotation(new Vector3(-.155813934f,.481096721f,2.329844793f),
            Quaternion.LookRotation(new Vector3(-.002665683f,-.314692175f,.949190020f), new Vector3(-.455142525f,-.844796722f,-.281360134f)));
        source.bones[5].SetPositionAndRotation(new Vector3(-.267498570f,.273797310f,2.260803569f),
            Quaternion.LookRotation(new Vector3(.142536188f,.145008497f,.979109638f), new Vector3(.238632494f,-.965064787f,.108189023f)));
        source.bones[6].SetPositionAndRotation(new Vector3(-.217478158f,.071507572f,2.283481373f),
            Quaternion.LookRotation(new Vector3(.142536118f,.145008328f,.979109612f), new Vector3(.749968775f,-.661378483f,-.011226713f)));
        Check(rig.Bind(source, animator, shader, eye), "Held native AttackHelo1 arm and torso coordinates bind to an owned rig");
        var stick = new GameObject("Measured AttackHelo1 stick"); owned.Add(stick);
        stick.transform.position = new Vector3(.38f,.053254f,2.64958f);
        var handle = new Mesh { name = "Unreadable AttackHelo1 native stick bounds" }; owned.Add(handle);
        Bounds bounds = new Bounds(new Vector3(0,.135634854f,.021722855f), new Vector3(.096594691f,.344315141f,.140039757f));
        handle.vertices = new[] { bounds.min, bounds.max }; handle.bounds = bounds; handle.UploadMeshData(true);
        stick.AddComponent<MeshFilter>().sharedMesh = handle;
        Vector3 grip; Quaternion gripRotation; float distance;
        Check(CockpitPilotRig.TryFindGrip(stick.transform, rig.Bone("hand_R"), rig.Bone("upperarm_R"), true, out grip, out gripRotation, out distance),
            "Measured AttackHelo1 native stick produces a reachable grip");
        Check(Vector3.Distance(stick.transform.TransformPoint(grip), new Vector3(.400000027f,.326614912f,2.586302857f)) < .0005f &&
            Quaternion.Angle(gripRotation, new Quaternion(.5f,-.5f,-.5f,.5f)) < .05f,
            "AttackHelo1 thumb-up grip agrees with the independently exported neutral fit");
        rig.AttachControls(stick.transform, grip, gripRotation, null, Vector3.zero, Quaternion.identity);
        float maximumError = 0, maximumExtension = 0;
        float upperLength = Vector3.Distance(source.bones[7].position, source.bones[8].position);
        float lowerLength = Vector3.Distance(source.bones[8].position, source.bones[9].position);
        float naturalReach = upperLength + lowerLength;
        Vector3 nativeWrist = source.bones[9].localPosition;
        Quaternion nativeRotation = source.bones[9].localRotation;
        Vector3 wristAxis = nativeWrist.normalized;
        float maximumTwist = 0;
        for (int pitch = -12; pitch <= 12; pitch += 12)
            for (int roll = -12; roll <= 12; roll += 12)
            {
                stick.transform.rotation = Quaternion.Euler(pitch,0,-roll);
                rig.CopySeatedPose(); rig.Pose(0,0,0,0,Vector3.up,Quaternion.identity,1f/60,false,true);
                Vector3 target = stick.transform.TransformPoint(grip);
                float error = Vector3.Distance(rig.Bone("hand_R").position, target);
                maximumError = Mathf.Max(maximumError, error);
                maximumExtension = Mathf.Max(maximumExtension, Vector3.Distance(source.bones[7].position, target) - naturalReach);
                Vector3 upperHandle = stick.transform.TransformPoint(new Vector3(0,.273360911f,.021722855f));
                Transform handBone = rig.Bone("hand_R");
                maximumTwist = Mathf.Max(maximumTwist, WristTwist(handBone.localRotation, nativeRotation, wristAxis));
                Check(error < .0005f && Vector3.Distance(handBone.position + handBone.rotation * new Vector3(0,.085f,.02f), upperHandle) < .0005f,
                    "AttackHelo1 full-throw palm remains welded: pitch " + pitch + " roll " + roll);
            }
        Check(maximumExtension > .005f && maximumExtension < .0075f,
            "AttackHelo1 thumb-up full throw needs only the measured 6 mm visual reach extension");
        Check(maximumTwist < 2f, "Forearm pronation keeps right wrist axial twist below two degrees across all nine throws");
        var collective = new GameObject("Measured AttackHelo1 collective"); owned.Add(collective);
        collective.transform.position = new Vector3(-.907626987f,-.968311012f,2.539900064f);
        var collectiveMesh = new Mesh { name = "Unreadable AttackHelo1 collective bounds" }; owned.Add(collectiveMesh);
        Bounds collectiveBounds = new Bounds(new Vector3(.516232967f,1.040686846f,.145437866f),
            new Vector3(.146345675f,.086973250f,.143924057f));
        collectiveMesh.vertices = new[] { collectiveBounds.min, collectiveBounds.max };
        collectiveMesh.bounds = collectiveBounds; collectiveMesh.UploadMeshData(true);
        collective.AddComponent<MeshFilter>().sharedMesh = collectiveMesh;
        Vector3 leftGrip = Vector3.zero, nativeLeftWrist = source.bones[6].localPosition;
        Quaternion leftGripRotation = Quaternion.identity, nativeLeftRotation = source.bones[6].localRotation;
        float collectiveError = 0, collectiveTwist = 0;
        for (int binding = 0; binding < 3; binding++)
        {
            collective.transform.rotation = Quaternion.Euler(-6f * binding,0,0);
            rig.SetControls(null, null); rig.CopySeatedPose();
            Check(CockpitPilotRig.TryFindGrip(collective.transform, rig.Bone("hand_L"), rig.Bone("upperarm_L"), false,
                out leftGrip, out leftGripRotation, out distance), "Native collective binds at throttle " + (binding * .5f));
            if (binding == 0)
                Check(Vector3.Distance(collective.transform.TransformPoint(leftGrip), new Vector3(-.372760842f,.163203669f,2.621014628f)) < .0005f &&
                    Quaternion.Angle(leftGripRotation, new Quaternion(.430897768f,.195261045f,.802474646f,.363640867f)) < .05f,
                    "Left collective grip agrees with the independently exported reach-aligned neutral fit");
            rig.AttachControls(stick.transform, grip, gripRotation, collective.transform, leftGrip, leftGripRotation);
            for (int operation = 0; operation < 3; operation++)
            {
                collective.transform.rotation = Quaternion.Euler(-6f * operation,0,0);
                rig.CopySeatedPose(); rig.Pose(0,0,0,0,Vector3.up,Quaternion.identity,1f/60,false,true);
                Transform leftHand = rig.Bone("hand_L");
                Vector3 palm = collective.transform.TransformPoint(new Vector3(.516232967f,1.075476146f,.145437866f));
                collectiveError = Mathf.Max(collectiveError, Vector3.Distance(leftHand.position, collective.transform.TransformPoint(leftGrip)),
                    Vector3.Distance(leftHand.position + leftHand.rotation * new Vector3(0,.085f,.02f), palm));
                collectiveTwist = Mathf.Max(collectiveTwist, WristTwist(leftHand.localRotation, nativeLeftRotation, nativeLeftWrist.normalized));
            }
        }
        Check(collectiveError < .0005f && collectiveTwist < 2f,
            "Collective palm stays welded with under two degrees wrist twist across all binding and operating levels");
        collective.transform.rotation = Quaternion.identity;
        rig.SetControls(null, null); rig.CopySeatedPose();
        Check(CockpitPilotRig.TryFindGrip(collective.transform, rig.Bone("hand_L"), rig.Bone("upperarm_L"), false,
            out leftGrip, out leftGripRotation, out distance), "Collective neutral rebind prepares full-force contact test");
        rig.AttachControls(stick.transform, grip, gripRotation, collective.transform, leftGrip, leftGripRotation);
        float bracedError = 0, segmentError = 0, bracedTwist = 0, bracedLeftError = 0, bracedLeftTwist = 0;
        Vector3[] forces = { new Vector3(100,100,100), new Vector3(-100,100,-100),
            new Vector3(100,-100,-100), new Vector3(-100,-100,100) };
        for (int pitch = -12; pitch <= 12; pitch += 12)
            for (int roll = -12; roll <= 12; roll += 12)
            {
                stick.transform.rotation = Quaternion.Euler(pitch,0,-roll);
                for (int force = 0; force < forces.Length; force++)
                    for (int frame = 0; frame < 90; frame++)
                    {
                        collective.transform.rotation = Quaternion.Euler(-12f * (frame % 30) / 29f,0,0);
                        rig.CopySeatedPose(); rig.Pose(1,1,1,1,forces[force],Quaternion.identity,1f/60,true,false);
                        Transform upper = rig.Bone("upperarm_R"), lower = rig.Bone("forearm_R"), wrist = rig.Bone("hand_R");
                        Vector3 target = stick.transform.TransformPoint(grip);
                        bracedError = Mathf.Max(bracedError, Vector3.Distance(wrist.position, target));
                        float scale = Mathf.Max(1, Vector3.Distance(upper.position, target) / naturalReach);
                        segmentError = Mathf.Max(segmentError, Mathf.Abs(Vector3.Distance(upper.position, lower.position) - upperLength * scale),
                            Mathf.Abs(Vector3.Distance(lower.position, wrist.position) - lowerLength * scale));
                        bracedTwist = Mathf.Max(bracedTwist, WristTwist(wrist.localRotation, nativeRotation, wristAxis));
                        Transform leftHand = rig.Bone("hand_L");
                        bracedLeftError = Mathf.Max(bracedLeftError, Vector3.Distance(leftHand.position, collective.transform.TransformPoint(leftGrip)));
                        bracedLeftTwist = Mathf.Max(bracedLeftTwist, WristTwist(leftHand.localRotation, nativeLeftRotation, nativeLeftWrist.normalized));
                    }
            }
        Check(bracedError < .0005f, "AttackHelo1 all nine stick throws stay welded through full torso bracing and breathing");
        Check(segmentError < .0011f, "Bounded visual reach extension is shared proportionally by upper arm and forearm");
        Check(bracedTwist < 2f && bracedLeftTwist < 2f && bracedLeftError < .0005f,
            "Full-force stick and moving collective keep both wrist twists below two degrees and maintain hand contact");
        Check(source.bones[9].localPosition == nativeWrist && source.bones[9].localRotation == nativeRotation &&
            source.bones[6].localPosition == nativeLeftWrist && source.bones[6].localRotation == nativeLeftRotation,
            "Measured airframe weld leaves both native pilot wrists untouched");
        log.AppendLine("AttackHelo1 maximum full-throw wrist-contact error: " + (maximumError * 1000).ToString("F4") + " mm");
        log.AppendLine("AttackHelo1 braced wrist-contact error: " + (bracedError * 1000).ToString("F4") + " mm; proportional segment error: " +
            (segmentError * 1000).ToString("F4") + " mm");
        log.AppendLine("AttackHelo1 right/left wrist twist: " + bracedTwist.ToString("F4") + "/" + bracedLeftTwist.ToString("F4") +
            " degrees; braced collective contact error: " + (bracedLeftError * 1000).ToString("F4") + " mm");
        rig.Release();
    }

    private static float WristTwist(Quaternion actual, Quaternion native, Vector3 forearmAxis)
    {
        Quaternion relative = actual * Quaternion.Inverse(native);
        float axial = Vector3.Dot(new Vector3(relative.x, relative.y, relative.z), forearmAxis);
        return Mathf.Abs(Mathf.DeltaAngle(0, 2f * Mathf.Atan2(axial, relative.w) * Mathf.Rad2Deg));
    }

    private void CheckDelayedGripBinding(Transform cockpit)
    {
        var lever = new GameObject("Late native stick"); owned.Add(lever);
        lever.transform.SetParent(cockpit, false);
        lever.transform.SetPositionAndRotation(source.bones[9].position, Quaternion.Euler(7,-11,3));
        Transform hand = rig.Bone("hand_R"), shoulder = rig.Bone("upperarm_R");
        rig.SetControls(null, null);
        Vector3 grip; Quaternion gripRotation; float distance;
        Check(!CockpitPilotRig.TryFindGrip(lever.transform, hand, shoulder, true, out grip, out gripRotation, out distance),
            "Native stick without a ready mesh remains unbound");
        var handle = new GameObject("Native stick handle mesh"); owned.Add(handle);
        handle.transform.SetParent(lever.transform, false);
        handle.transform.localRotation = Quaternion.Euler(9,-13,4);
        var mesh = new Mesh { name = "Unreadable delayed native stick bounds" }; owned.Add(mesh);
        mesh.vertices = new[] { new Vector3(-.02f,-.08f,-.02f), new Vector3(.02f,.08f,.02f) };
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(.04f,.16f,.04f));
        mesh.UploadMeshData(true);
        handle.AddComponent<MeshFilter>().sharedMesh = mesh;
        float reach = Vector3.Distance(shoulder.position, hand.parent.position) + Vector3.Distance(hand.parent.position, hand.position);
        Vector3 expectedWrist = shoulder.position + (hand.position - shoulder.position).normalized * (reach + .025f);
        Quaternion expectedRotation = Quaternion.LookRotation(-handle.transform.right, handle.transform.forward);
        Vector3 palmOffset = expectedRotation * new Vector3(0,.085f,.02f);
        Vector3 upperHandle = expectedWrist + palmOffset;
        handle.transform.position = upperHandle - handle.transform.TransformVector(new Vector3(0,.064f,0));
        Check(CockpitPilotRig.TryFindGrip(lever.transform, hand, shoulder, true, out grip, out gripRotation, out distance),
            "A delayed native handle binds within bounded full-throw reach");
        Check(!mesh.isReadable && Vector3.Distance(lever.transform.TransformPoint(grip), expectedWrist) < .0005f,
            "Nested rotated unreadable handle bounds produce the measured palm-fit wrist target");
        Vector3 nativeLeverPosition = lever.transform.position;
        Quaternion nativeLeverRotation = lever.transform.rotation;
        Vector3 nativeWristPosition = source.bones[9].localPosition;
        Quaternion nativeWristRotation = source.bones[9].localRotation;
        rig.AttachControls(lever.transform, grip, gripRotation, null, Vector3.zero, Quaternion.identity);
        rig.CopySeatedPose(); rig.Pose(0,0,0,0,Vector3.up,Quaternion.identity,1f/60,false,true);
        Check(Vector3.Distance(hand.position + hand.up * .085f + hand.forward * .02f, upperHandle) < .0005f,
            "Bound hand palm touches the upper handle with body motion disabled");
        Check(lever.transform.position == nativeLeverPosition && lever.transform.rotation == nativeLeverRotation &&
            source.bones[9].localPosition == nativeWristPosition && source.bones[9].localRotation == nativeWristRotation,
            "Hand weld never writes the native stick transform or wrist bone");
        lever.transform.position += Vector3.one * 100;
        rig.CopySeatedPose();
        Check(!CockpitPilotRig.TryFindGrip(lever.transform, hand, shoulder, true, out grip, out gripRotation, out distance),
            "Far native control geometry fails binding safely");
        rig.SetControls(null, null);
    }

    private void CheckPoseTransitions(Transform stick, Transform throttle)
    {
        Quaternion head = rig.Head.rotation, chest = rig.Chest.rotation, leftFoot = rig.Bone("foot_L").rotation, rightFoot = rig.Bone("foot_R").rotation;
        Vector3 chestPosition = rig.Chest.position;
        rig.CopySeatedPose();
        rig.Pose(-1,1,-1,0,new Vector3(100,100,100),Quaternion.Euler(90,180,0),0,true,false);
        Check(Quaternion.Angle(head, rig.Head.rotation) < .001f && Quaternion.Angle(chest, rig.Chest.rotation) < .001f &&
            Quaternion.Angle(leftFoot, rig.Bone("foot_L").rotation) < .001f && Quaternion.Angle(rightFoot, rig.Bone("foot_R").rotation) < .001f &&
            Vector3.Distance(chestPosition, rig.Chest.position) < .00001f, "Zero dt reapplies previous pose after native pose copy");
        rig.CopySeatedPose(); rig.Pose(1,1,1,1,new Vector3(100,100,100),Quaternion.identity,1f/60,false,false);
        Check(Quaternion.Angle(rig.Chest.rotation, source.bones[1].rotation) < .001f &&
            Quaternion.Angle(rig.Bone("foot_L").rotation, source.bones[15].rotation) < .001f &&
            Quaternion.Angle(rig.Bone("foot_R").rotation, source.bones[12].rotation) < .001f,
            "Motion disabled restores neutral chest and feet despite extreme controls");
        for (int frame = 0; frame < 90; frame++)
        {
            rig.CopySeatedPose(); rig.Pose(1,1,1,1,new Vector3(100,100,100),Quaternion.Euler(70,-120,0),1f/60,true,true);
        }
        Check(Vector3.Distance(rig.Chest.position, source.bones[1].position) < .00001f, "Comfort removes breathing translation");
        Vector3 lean = (Quaternion.Inverse(source.bones[1].rotation) * rig.Chest.rotation).eulerAngles;
        Check(Mathf.Abs(Mathf.DeltaAngle(0,lean.x)) <= .51f && Mathf.Abs(Mathf.DeltaAngle(0,lean.z)) <= .51f,
            "Comfort torso pitch and roll stay within half-degree limits");
        Check(Quaternion.Angle(source.bones[1].rotation, rig.Chest.rotation) <= .501f,
            "Combined comfort torso rotation stays within half degree");
        float headPitch = (float)typeof(CockpitPilotRig).GetField("headPitch", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rig);
        float headYaw = (float)typeof(CockpitPilotRig).GetField("headYaw", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rig);
        Check(headPitch <= 35.001f && headPitch >= -45.001f && headYaw <= 75.001f && headYaw >= -75.001f && headPitch > 34 && headYaw < -74,
            "Head clamps preserve signed35down45up75yaw limits");
        for (int frame = 0; frame < 90; frame++)
        {
            rig.CopySeatedPose(); rig.Pose(0,0,0,0,Vector3.up,Quaternion.Euler(-80,120,0),1f/60,true,true);
        }
        headPitch = (float)typeof(CockpitPilotRig).GetField("headPitch", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rig);
        headYaw = (float)typeof(CockpitPilotRig).GetField("headYaw", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(rig);
        Check(headPitch >= -45.001f && headPitch < -44 && headYaw <= 75.001f && headYaw > 74, "Opposite head limits retain45up and positive75yaw");
        for (int frame = 0; frame < 90; frame++)
        {
            rig.CopySeatedPose(); rig.Pose(1,1,1,1,new Vector3(100,100,100),Quaternion.identity,1f/60,true,false);
        }
        Check(Quaternion.Angle(source.bones[1].rotation, rig.Chest.rotation) <= 2.001f,
            "Combined full-motion torso rotation stays within two degrees");
        Check(Vector3.Distance(source.bones[1].position, rig.Chest.position) <= .00301f,
            "Full-motion breathing translation stays within three millimetres");
        rig.SetControls(null,null); rig.CopySeatedPose();
        rig.Pose(1,1,1,1,Vector3.up,Quaternion.identity,1f/60,true,true);
        Check(Quaternion.Angle(rig.Bone("upperarm_R").localRotation, source.bones[7].localRotation) < .001f &&
            Quaternion.Angle(rig.Bone("forearm_L").localRotation, source.bones[5].localRotation) < .001f,
            "Missing native grips preserve conservative seated arm pose");
        rig.SetControls(stick,throttle);
        stick.position = new Vector3(100,100,100); throttle.position = new Vector3(-100,100,100);
        rig.CopySeatedPose(); rig.Pose(1,1,1,1,Vector3.up,Quaternion.identity,1f/60,true,true);
        Check(Quaternion.Angle(rig.Bone("forearm_R").localRotation, source.bones[8].localRotation) < .001f &&
            Quaternion.Angle(rig.Bone("forearm_L").localRotation, source.bones[5].localRotation) < .001f,
            "Unreachable native grips preserve seated arms");
        rig.SetControls(null,null);
    }

    private static bool HelmetAboveBody(Color32[] pixels, int width)
    {
        long helmetY = 0, bodyY = 0; int helmetCount = 0, bodyCount = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 colour = pixels[i];
            if (colour.r >= colour.g + 2 && colour.r >= colour.b + 2) { helmetY += i / width; helmetCount++; }
            else if (colour.g >= colour.r + 1 && colour.g >= colour.b + 2) { bodyY += i / width; bodyCount++; }
        }
        return helmetCount > 50 && bodyCount > 50 && (double)helmetY / helmetCount > (double)bodyY / bodyCount;
    }

    private void DrawCapture(Camera projection)
    {
        DrawCapture(GL.GetGPUProjectionMatrix(projection.projectionMatrix, true) * projection.worldToCameraMatrix);
    }

    private void DrawCapture(Matrix4x4 viewProjection)
    {
        var material = rig.Renderer.sharedMaterial;
        material.SetMatrix("_CaptureVP", viewProjection);
        var command = new CommandBuffer { name = "Fixture pilot-only capture" };
        command.SetRenderTarget(captureTarget);
        command.ClearRenderTarget(true, true, Color.clear);
        command.DrawRenderer(rig.Renderer, material, 0, 1);
        Graphics.ExecuteCommandBuffer(command);
        command.Release();
    }

    private Camera CameraAt(string name, RenderTexture target, float depth)
    {
        var node = new GameObject(name); owned.Add(node);
        var camera = node.AddComponent<Camera>();
        camera.transform.position = new Vector3(0, .85f, 2.7f);
        camera.transform.LookAt(new Vector3(0, .85f, 0));
        camera.orthographic = true; camera.orthographicSize = .9f;
        camera.nearClipPlane = .01f; camera.farClipPlane = 10;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.025f, .025f, .025f, 1);
        camera.cullingMask = 1 << 3; camera.depth = depth; camera.targetTexture = target;
        var data = camera.GetUniversalAdditionalCameraData();
        data.requiresDepthTexture = true; data.requiresColorTexture = false;
        return camera;
    }

    private RenderTexture Target(string name, int width, int height)
    {
        var target = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32) { name = name, antiAliasing = 1, useMipMap = false };
        target.Create(); owned.Add(target); return target;
    }

    private IEnumerator CheckNativeHeadAndMaterial(Shader shader)
    {
        CreateSource(true);
        Check(Vector3.Distance(source.bones[3].position, sourceRoot.transform.Find("Armature/pelvis/chest/neck/head/head_end").position) > .2069f,
            "Skull-base fixture retains native-shaped 207 mm head-end span");
        Check(rig.Bind(source, animator, shader, eye), "Skull-base head/end/eye hierarchy binds");
        Check(rig.BodyRenderer == rig.Renderer, "Head/end/eye names alone never substitute unrelated native geometry");
        rig.SetLight(1); rig.SetBodyVisible(true);
        eye.transform.position = new Vector3(0, .85f, 2.7f); eye.transform.LookAt(new Vector3(0, .85f, 0));
        eye.orthographic = true; eye.orthographicSize = .9f;
        yield return new WaitForEndOfFrame(); yield return new WaitForEndOfFrame();
        Save(eyeTarget, "native-shaped-head-mask-neutral.png");
        Check(ColourPixels(eyeTarget, false) > 1500, "Skull-base mask preserves the chest-weighted collar below the helmet");
        Check(ColourPixels(eyeTarget, true) < 25, "Skull-base mask hides the complete helmet");
        foreach (Vector2 look in new[] { new Vector2(35, 75), new Vector2(-45, -75), new Vector2(35, -75), new Vector2(-45, 75) })
        {
            for (int frame = 0; frame < 90; frame++)
            { rig.CopySeatedPose(); rig.Pose(0, 0, 0, 0, Vector3.up, Quaternion.Euler(look.x, look.y, 0), 1f / 60f, true, true); }
            yield return new WaitForEndOfFrame(); yield return new WaitForEndOfFrame();
            Check(ColourPixels(eyeTarget, true) < 25, "Helmet coverage remains complete at look limit " + look);
            Check(ColourPixels(eyeTarget, false) > 800, "Collar remains visible at look limit " + look);
        }
        Save(eyeTarget, "native-shaped-head-mask-look-limit.png");
        rig.SetBodyVisible(false);
        yield return new WaitForEndOfFrame(); yield return new WaitForEndOfFrame();
        Check(BrightPixels(eyeTarget, 0, 1) < 50, "Improved direct body can be hidden independently");
        DrawCapture(eye);
        Check(ColourPixels(captureTarget, true) > 30 && ColourPixels(captureTarget, false) > 30,
            "Full-head capture preserves helmet and collar while direct body is hidden");
        Save(captureTarget, "native-shaped-full-head-body-off.png");
        rig.Release(); yield return null;

        // A real URP Lit source has maps and shader keywords the body material must
        // retain, without changing the shared native material or importing its shader.
        borrowedMaterial = new Material(Resources.Load<Material>("PilotNativeMaterial")) { name = "Native URP Lit contract" };
        owned.Add(borrowedMaterial); source.sharedMaterial = borrowedMaterial;
        string[] maps = { "_BaseMap", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap" };
        for (int i = 0; i < maps.Length; i++)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, i != 0) { name = "Native map " + maps[i] }; owned.Add(texture);
            texture.SetPixel(0, 0, i == 1 ? new Color(.5f, .5f, 1, .5f) : new Color(.7f, .6f, .5f, .8f)); texture.Apply();
            borrowedMaterial.SetTexture(maps[i], texture);
            borrowedMaterial.SetTextureScale(maps[i], new Vector2(1.1f + i * .2f, .8f + i * .1f));
            borrowedMaterial.SetTextureOffset(maps[i], new Vector2(.02f + i * .03f, -.04f - i * .02f));
        }
        string[] keywords = { "_NORMALMAP", "_METALLICSPECGLOSSMAP", "_OCCLUSIONMAP" };
        foreach (string keyword in keywords) borrowedMaterial.EnableKeyword(keyword);
        string[] scalars = { "_BumpScale", "_Metallic", "_Smoothness", "_OcclusionStrength", "_SmoothnessTextureChannel", "_SpecularHighlights" };
        float[] values = { .73f, .37f, .68f, .82f, 0f, 1f };
        for (int i = 0; i < scalars.Length; i++) borrowedMaterial.SetFloat(scalars[i], values[i]);
        Color tint = new Color(.82f, .91f, .77f, 1); borrowedMaterial.SetColor("_BaseColor", tint);
        Check(rig.Bind(source, animator, shader, eye), "Mapped URP Lit native material binds");
        Material copied = rig.Material;
        Check(copied != borrowedMaterial && copied.shader == shader && source.sharedMaterial == borrowedMaterial,
            "Owned shading material preserves source shared-material ownership");
        for (int i = 0; i < maps.Length; i++)
        {
            Check(copied.GetTexture(maps[i]) == borrowedMaterial.GetTexture(maps[i]), "Native map borrowed by identity: " + maps[i]);
            Check(copied.GetTextureScale(maps[i]) == borrowedMaterial.GetTextureScale(maps[i]) && copied.GetTextureOffset(maps[i]) == borrowedMaterial.GetTextureOffset(maps[i]),
                "Native map tiling and offset retained: " + maps[i]);
        }
        for (int i = 0; i < scalars.Length; i++)
            Check(Mathf.Abs(copied.GetFloat(scalars[i]) - values[i]) < .00001f && Mathf.Abs(borrowedMaterial.GetFloat(scalars[i]) - values[i]) < .00001f,
                "Native scalar copied without source mutation: " + scalars[i]);
        Check(copied.GetColor("_BaseColor") == tint && borrowedMaterial.GetColor("_BaseColor") == tint, "Native colour tint retained without source mutation");
        Check(copied.GetFloat("_HasNormalMap") == 1 && copied.GetFloat("_HasMetallicMap") == 1 && copied.GetFloat("_HasOcclusionMap") == 1,
            "Native map keywords enable the owned detail samples");
        foreach (string keyword in keywords) Check(borrowedMaterial.IsKeywordEnabled(keyword), "Native shader keyword retained: " + keyword);
        DrawCapture(eye);
        float occludedCapture = CaptureLuminance(captureTarget);
        copied.SetFloat("_HasOcclusionMap", 0); DrawCapture(eye);
        Check(CaptureLuminance(captureTarget) > occludedCapture * 1.10f,
            "Native occlusion map changes the actual rendered capture pixels");
        copied.SetFloat("_HasOcclusionMap", 1);
        rig.Release(); yield return null;
        foreach (string map in maps) borrowedMaterial.SetTexture(map, null);
        Check(rig.Bind(source, animator, shader, eye), "Missing native maps use the inexpensive fallback");
        Check(rig.Material.GetFloat("_HasNormalMap") == 0 && rig.Material.GetFloat("_HasMetallicMap") == 0 && rig.Material.GetFloat("_HasOcclusionMap") == 0,
            "Enabled source keywords alone never trigger absent texture samples");
        rig.Release(); yield return null;
    }

    private static int ColourPixels(RenderTexture target, bool helmet)
    {
        Texture2D image = Read(target); Color32[] pixels = image.GetPixels32(); int count = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 pixel = pixels[i];
            if (helmet ? pixel.r > pixel.g * 1.25f && pixel.r > pixel.b * 1.4f && pixel.r > 30
                       : pixel.b > pixel.r * 1.5f && pixel.b > pixel.g * 1.5f && pixel.b > 30) count++;
        }
        Destroy(image); return count;
    }

    private static float CaptureLuminance(RenderTexture target)
    {
        Texture2D image = Read(target); Color32[] pixels = image.GetPixels32(); long total = 0; int count = 0;
        for (int i = 0; i < pixels.Length; i++) if (pixels[i].a > 100)
        { total += pixels[i].r + pixels[i].g + pixels[i].b; count++; }
        Destroy(image); return count == 0 ? 0 : total / (float)count;
    }

    private void CreateSource(bool skullBaseHead = false)
    {
        sourceRoot = new GameObject("Borrowed native pilot"); owned.Add(sourceRoot);
        animator = sourceRoot.AddComponent<Animator>(); animator.cullingMode = AnimatorCullingMode.CullCompletely;
        var armature = new GameObject("Armature").transform;
        armature.SetParent(sourceRoot.transform, false); armature.localScale = Vector3.one * 100;
        Transform[] bones = new Transform[16];
        int[] parents = { -1, 0, 1, 2, 1, 4, 5, 1, 7, 8, -1, 10, 11, -1, 13, 14 };
        Vector3[] centres = {
            new Vector3(0,.66f,0), new Vector3(0,.99f,0), new Vector3(0,1.24f,0), new Vector3(0,1.4f,0),
            new Vector3(-.23f,1.08f,0), new Vector3(-.32f,.87f,.19f), new Vector3(-.24f,.86f,.44f),
            new Vector3(.23f,1.08f,0), new Vector3(.32f,.87f,.19f), new Vector3(.24f,.86f,.44f),
            new Vector3(.14f,.66f,.05f), new Vector3(.14f,.38f,.34f), new Vector3(.14f,.15f,.46f),
            new Vector3(-.14f,.66f,.05f), new Vector3(-.14f,.38f,.34f), new Vector3(-.14f,.15f,.46f)
        };
        Vector3[] meshCentres = (Vector3[])centres.Clone();
        if (skullBaseHead) { centres[2].y = 1.16f; centres[3].y = 1.2965f; }
        for (int i = 0; i < 16; i++)
        {
            bones[i] = new GameObject(Names[i]).transform;
            bones[i].SetParent(parents[i] < 0 ? armature : bones[parents[i]], false);
            bones[i].position = centres[i];
        }
        if (skullBaseHead)
        {
            var headEnd = new GameObject("head_end").transform;
            headEnd.SetParent(bones[3], false); headEnd.position = centres[3] + Vector3.up * .207f;
            var helmetEye = new GameObject("helmetCamPoint").transform;
            helmetEye.SetParent(bones[3], false); helmetEye.position = centres[3] + new Vector3(0, .08f, .08f);
        }
        source = sourceRoot.AddComponent<SkinnedMeshRenderer>(); source.bones = bones; source.rootBone = armature;
        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>();
        var triangles = new List<int>(); var weights = new List<BoneWeight>();
        Vector3[] sizes = {
            new Vector3(.4f,.2f,.26f), new Vector3(.44f,.42f,.25f), new Vector3(.13f,.1f,.13f), new Vector3(.25f,.29f,.25f),
            new Vector3(.12f,.21f,.12f), new Vector3(.1f,.1f,.23f), new Vector3(.12f,.07f,.12f),
            new Vector3(.12f,.21f,.12f), new Vector3(.1f,.1f,.23f), new Vector3(.12f,.07f,.12f),
            new Vector3(.16f,.20f,.26f), new Vector3(.13f,.27f,.14f), new Vector3(.15f,.08f,.23f),
            new Vector3(.16f,.20f,.26f), new Vector3(.13f,.27f,.14f), new Vector3(.15f,.08f,.23f)
        };
        var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Mesh cube = primitive.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] cubeVertices = cube.vertices; Vector3[] cubeNormals = cube.normals; int[] cubeTriangles = cube.triangles;
        for (int bone = 0; bone < 16; bone++)
        {
            int offset = vertices.Count;
            for (int v = 0; v < cubeVertices.Length; v++)
            {
                vertices.Add(meshCentres[bone] + Vector3.Scale(cubeVertices[v], sizes[bone])); normals.Add(cubeNormals[v]);
                uv.Add(new Vector2(bone == 3 ? .375f : .125f, .5f)); weights.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1 });
            }
            for (int t = 0; t < cubeTriangles.Length; t++) triangles.Add(offset + cubeTriangles[t]);
        }
        if (skullBaseHead)
        {
            // Blue collar is chest-weighted and near the skull-base bone, matching the
            // morphology missed by the old helmet-centred synthetic fixture.
            int offset = vertices.Count;
            for (int v = 0; v < cubeVertices.Length; v++)
            {
                vertices.Add(new Vector3(0, 1.20f, .145f) + Vector3.Scale(cubeVertices[v], new Vector3(.32f, .035f, .03f)));
                normals.Add(cubeNormals[v]); uv.Add(new Vector2(.625f, .5f));
                weights.Add(new BoneWeight { boneIndex0 = 1, weight0 = 1 });
            }
            for (int t = 0; t < cubeTriangles.Length; t++) triangles.Add(offset + cubeTriangles[t]);
        }
        Destroy(primitive);
        borrowedMesh = new Mesh { name = "Unreadable native-shaped mesh" }; owned.Add(borrowedMesh);
        borrowedMesh.SetVertices(vertices); borrowedMesh.SetNormals(normals); borrowedMesh.SetUVs(0, uv); borrowedMesh.SetTriangles(triangles, 0);
        borrowedMesh.boneWeights = weights.ToArray();
        Matrix4x4[] bindposes = new Matrix4x4[16];
        for (int i = 0; i < 16; i++) bindposes[i] = bones[i].worldToLocalMatrix * source.transform.localToWorldMatrix;
        borrowedMesh.bindposes = bindposes; borrowedMesh.RecalculateBounds(); source.sharedMesh = borrowedMesh; source.localBounds = borrowedMesh.bounds;
        borrowedMesh.UploadMeshData(true);
        borrowedTexture = new Texture2D(4, 1, TextureFormat.RGBA32, false) { name = "Borrowed pilot albedo", filterMode = FilterMode.Point }; owned.Add(borrowedTexture);
        borrowedTexture.SetPixels(new[] { new Color(.56f,.61f,.3f,1), new Color(.9f,.46f,.12f,1), new Color(.12f,.3f,.95f,1), Color.white }); borrowedTexture.Apply();
        borrowedMaterial = new Material(Resources.Load<Shader>("PilotBody")) { name = "Borrowed native material" }; borrowedMaterial.SetTexture("_BaseMap", borrowedTexture); owned.Add(borrowedMaterial);
        source.sharedMaterial = borrowedMaterial; source.enabled = false;
    }

    private static Texture2D Read(RenderTexture target)
    {
        RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply(); RenderTexture.active = previous; return image;
    }
    private static int BrightPixels(RenderTexture target, float low, float high)
    {
        Texture2D image = Read(target); Color32[] pixels = image.GetPixels32(); int count = 0;
        int first = (int)(target.height * low) * target.width, last = (int)(target.height * high) * target.width;
        for (int i = first; i < last; i++) if (pixels[i].r + pixels[i].g + pixels[i].b > 150) count++;
        Destroy(image); return count;
    }
    private static int NonzeroAlpha(RenderTexture target, float low, float high)
    {
        Texture2D image = Read(target); Color32[] pixels = image.GetPixels32(); int count = 0;
        int first = (int)(target.height * low) * target.width, last = (int)(target.height * high) * target.width;
        for (int i = first; i < last; i++) if (pixels[i].a > 100) count++;
        Destroy(image); return count;
    }
    private static void Save(RenderTexture target, string name)
    {
        Texture2D image = Read(target); File.WriteAllBytes(name, image.EncodeToPNG()); Destroy(image);
    }
    private void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        assertions++; log.AppendLine("PASS " + message);
    }
}
#endif
