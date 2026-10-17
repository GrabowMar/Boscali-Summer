#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections;
using System.IO;
using System.Text;
using BoscaliSummer.Core.Fx;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// The unmodified production helper in a real URP player. No native RadarChaff,
// game host, radar decisions, multiplayer or game bootstrap is executed here.
public sealed class ChaffVisualUnityCheck : MonoBehaviour
{
    private readonly StringBuilder log = new StringBuilder();
    private readonly ChaffVisuals visuals = new ChaffVisuals();
    private int checks;
    private int brighterPixels;
    private Camera eye;
    private Light sun;
    private RenderTexture target;
    private readonly ParticleSystem.Particle[] particles = new ParticleSystem.Particle[1024];

#if UNITY_EDITOR
    public static void Build()
    {
        try
        {
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, "Assets/Resources/ChaffRenderer.asset");
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.msaaSampleCount = 4;
            pipeline.renderScale = 1;
            pipeline.supportsHDR = true;
            AssetDatabase.CreateAsset(pipeline, "Assets/Resources/ChaffPipeline.asset");
            GraphicsSettings.renderPipelineAsset = pipeline;
            QualitySettings.renderPipeline = pipeline;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.graphicsJobs = false;
            // Preserve the actual production transparent material's shader variants.
            var source = new GameObject("Production material");
            var sourceParticles = source.AddComponent<ParticleSystem>();
            var owner = new ChaffVisuals();
            if (!owner.Configure(sourceParticles, source.transform, 5)) throw new Exception("Production foil shader unavailable");
            Material material = sourceParticles.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            if (ShaderUtil.ShaderHasError(material.shader)) throw new Exception("Production foil shader invalid");
            AssetDatabase.CreateAsset(new Material(material), "Assets/Resources/ProductionChaff.mat");
            UnityEngine.Object.DestroyImmediate(source);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Offline production chaff check").AddComponent<ChaffVisualUnityCheck>();
            EditorSceneManager.SaveScene(scene, "Assets/check.unity");
            AssetDatabase.SaveAssets();
            var built = BuildPipeline.BuildPlayer(new[] { "Assets/check.unity" }, "Player/ChaffCheck.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (built.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Player build failed: " + built.summary.result);
            File.WriteAllText("build-result.txt", "PASS: real URP 14.0.12 chaff player built from production source.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { File.WriteAllText("build-result.txt", "FAIL: " + error); Debug.LogException(error); EditorApplication.Exit(1); }
    }
#endif

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
        visuals.Clear();
        string result = (failed ? "FAIL: " : "PASS: " + checks + " assertions. ") +
            "Offline production ChaffVisuals; real URP, deterministic particles, native scale, 20 m and 75 m cameras plus one 5 m detail. " +
            "Game integration, floating-origin callbacks, multiplayer and live visual acceptance remain untested.\n" + log;
        File.WriteAllText("result.txt", result);
        Application.Quit(failed ? 1 : 0);
    }

    private IEnumerator Run()
    {
        log.AppendLine("GPU " + SystemInfo.graphicsDeviceName + " | " + SystemInfo.graphicsDeviceType);
        Check(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset, "Real URP pipeline active");
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.25f, .28f, .32f);
        sun = new GameObject("Daylight").AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.5f;
        sun.transform.rotation = Quaternion.Euler(35, 145, 0);
        eye = new GameObject("20 m camera").AddComponent<Camera>();
        eye.transform.position = new Vector3(0, 1, -20);
        eye.transform.LookAt(Vector3.zero);
        eye.clearFlags = CameraClearFlags.SolidColor;
        eye.backgroundColor = new Color(.21f, .34f, .48f);
        eye.fieldOfView = 42;
        eye.nearClipPlane = .1f;
        eye.farClipPlane = 150;
        eye.cullingMask = 1 << 30;
        target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        target.Create();
        eye.targetTexture = target;
        eye.GetUniversalAdditionalCameraData().requiresColorTexture = false;

        Transform origin = new GameObject("Datum origin substitute").transform;
        ParticleSystem pop = Make("Player pop", origin, false);
        Check(pop.main.maxParticles > 0 && pop.main.maxParticles <= 128, "Player particle budget <= 128");
        Check(pop.main.simulationSpace == ParticleSystemSimulationSpace.Custom && pop.main.customSimulationSpace == origin, "Particles use shared custom simulation origin");
        Check(pop.main.loop && pop.main.duration >= 10, "Native 5 s lifecycle stays playing without an early loop");
        Check(pop.emission.rateOverTime.constantMax == 0 && pop.emission.rateOverDistance.constantMax == 0, "Player dispenser has no continuous emission");
        Check(pop.emission.burstCount > 0, "Player dispenser uses an initial release burst");
        for (int i = 0; i < pop.emission.burstCount; i++)
            Check(pop.emission.GetBurst(i).time <= .1f && pop.emission.GetBurst(i).cycleCount == 1, "Every release burst finishes within 0.1 s without repeating");
        CheckSafe(pop);

        pop.Simulate(.12f, false, true, false);
        int early = pop.GetParticles(particles);
        float earlySpread = Spread(early);
        float earlyAlpha = MaxAlpha(pop, early);
        Check(early > 0 && early <= 128, "Player pop emits a bounded live cloud");
        yield return new WaitForEndOfFrame();
        Save("player-early-day.png");
        eye.transform.position = new Vector3(0, .25f, -5);
        eye.transform.LookAt(Vector3.zero);
        yield return new WaitForEndOfFrame();
        Save("player-early-detail-5m.png");
        eye.transform.position = new Vector3(0, 1, -20);
        eye.transform.LookAt(Vector3.zero);
        pop.Simulate(1.5f, false, true, false);
        int bloom = pop.GetParticles(particles);
        Check(bloom <= early && bloom > 0, "No fresh player particles appear after the pop");
        Check(Spread(bloom) > earlySpread * 1.25f, "Particle positions form a spreading cloud");
        Vector3 local = particles[0].position;
        Vector3 world = origin.TransformPoint(local);
        Vector3 shift = new Vector3(2500, 300, -1700);
        origin.position += shift;
        pop.GetParticles(particles);
        Check((particles[0].position - local).sqrMagnitude < .000001f && (origin.TransformPoint(particles[0].position) - world - shift).sqrMagnitude < .0001f,
            "Origin translation moves the cloud coherently without changing simulation coordinates");
        origin.position = Vector3.zero;
        yield return new WaitForEndOfFrame();
        int dayPixels = Save("player-cloud-day.png");
        Check(dayPixels > 0, "Production foil shader produces visible pixels at 20 m");
        int dayGlints = brighterPixels;
        eye.transform.position = new Vector3(0, 3.75f, -75);
        eye.transform.LookAt(Vector3.zero);
        yield return new WaitForEndOfFrame();
        Check(Save("player-cloud-day-75m.png") > 0, "Production foil shader produces visible pixels at 75 m");
        eye.transform.position = new Vector3(0, 1, -20);
        eye.transform.LookAt(Vector3.zero);
        SphericalHarmonicsL2 daylightProbe = RenderSettings.ambientProbe;
        float daylightReflection = RenderSettings.reflectionIntensity;
        sun.enabled = false;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.ambientProbe = new SphericalHarmonicsL2();
        RenderSettings.reflectionIntensity = 0;
        yield return new WaitForEndOfFrame();
        Save("player-cloud-unlit-night.png");
        Check(dayGlints > 0 && brighterPixels == 0, "Daylight produces bright glints; no-light foil produces no bright pixels");
        sun.enabled = true;
        RenderSettings.ambientLight = new Color(.25f, .28f, .32f);
        RenderSettings.ambientProbe = daylightProbe;
        RenderSettings.reflectionIntensity = daylightReflection;
        pop.Simulate(3.8f, false, true, false);
        int late = pop.GetParticles(particles);
        Check(late <= early && late > 0, "Late cloud stays below the original burst budget");
        Check(MaxAlpha(pop, late) < earlyAlpha * .75f, "Real particle alpha fades as the cloud ages");
        var gradient = pop.colorOverLifetime.color.gradient;
        Check(pop.colorOverLifetime.enabled && gradient != null && gradient.Evaluate(.99f).a < gradient.Evaluate(.35f).a,
            "Foil fades before its visual lifetime expires");
        yield return new WaitForEndOfFrame();
        Save("player-late-day.png");
        pop.Simulate(5.1f, false, true, false);
        Check(pop.particleCount == 0, "Player visual cloud ends within the native decoy lifetime");

        // Native RadarChaff polls isPlaying at five seconds. Exercise real engine
        // frame updates: Simulate() deliberately pauses and cannot establish this.
        ParticleSystem running = Make("Real clock native lifecycle", origin, false);
        running.gameObject.layer = 29;
        running.Play(false);
        yield return new WaitForSeconds(5.25f);
        Check(running.isPlaying && running.time >= 5, "Real-time particle system still playing after native 5 s gate");
        running.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem ribbon = Make("Ribbon pod", origin, true);
        Check(ribbon.main.maxParticles <= 1000 && ribbon.emission.rateOverDistance.constantMax > 0, "Ribbon pod has bounded distance emission");
        Check(ribbon.main.startLifetime.constantMax <= 16 && ribbon.main.startLifetime.constantMin >= 10, "Ribbon visual lifetime is independent of its 60 s gameplay lifetime");
        CheckSafe(ribbon);
        ribbon.Simulate(.01f, false, true, false);
        for (int i = 0; i < 35; i++) { ribbon.transform.position = new Vector3(-5 + i * .3f, 1, i * .15f); ribbon.Simulate(.04f, false, false, false); }
        Check(ribbon.particleCount > 0 && ribbon.particleCount <= 1000, "Moving ribbon pod emits bounded foil particles");
        yield return new WaitForEndOfFrame();
        Save("ribbon-pod-day.png");
        int beforeStop = ribbon.particleCount;
        ribbon.transform.SetParent(null, true);
        ribbon.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        ribbon.Simulate(.1f, false, false, false);
        Check(ribbon.transform.parent == null && ribbon.particleCount <= beforeStop && ribbon.particleCount > 0, "Detached ribbon stops emission while existing foil remains");
        ribbon.Simulate(17, false, false, false);
        Check(ribbon.particleCount == 0, "Detached ribbon particles eventually expire");
        ribbon.transform.position = Vector3.zero;
        ribbon.Simulate(.01f, false, true, false);
        int ribbonPeak = 0;
        for (int i = 0; i < 400; i++)
        {
            ribbon.transform.position = Vector3.forward * (i * 11.5f);
            ribbon.Simulate(.05f, false, false, false);
            ribbonPeak = Mathf.Max(ribbonPeak, ribbon.particleCount);
        }
        Check(ribbonPeak > 200 && ribbonPeak <= 1000, "Sustained 230 m/s ribbon release remains within its real particle budget");
        Check(pop.GetComponent<ParticleSystemRenderer>().sharedMaterial == ribbon.GetComponent<ParticleSystemRenderer>().sharedMaterial &&
            pop.GetComponent<ParticleSystemRenderer>().mesh == ribbon.GetComponent<ParticleSystemRenderer>().mesh,
            "Dispenser and ribbon share one owned material and foil mesh");
        Material ownedMaterial = ribbon.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        Mesh ownedMesh = ribbon.GetComponent<ParticleSystemRenderer>().mesh;
        visuals.Clear();
        yield return null;
        Check(ownedMaterial == null && ownedMesh == null, "Clear releases the owned material and mesh");
    }

    private ParticleSystem Make(string name, Transform origin, bool ribbon)
    {
        var go = new GameObject(name); go.layer = 30;
        go.transform.SetParent(origin, false);
        var system = go.AddComponent<ParticleSystem>();
        system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        Check(visuals.Configure(system, origin, ribbon ? 60 : 5, ribbon), "Production helper configured " + name);
        system.useAutoRandomSeed = false; system.randomSeed = 913;
        return system;
    }

    private void LateUpdate()
    {
        // Batch players skip the display render loop. Use the supported URP
        // request as the existing pilot and weather fixtures do.
        if (eye != null && target != null) RenderPipeline.SubmitRenderRequest(eye,
            new UniversalRenderPipeline.SingleCameraRequest { destination = target });
    }

    private void CheckSafe(ParticleSystem system)
    {
        var renderer = system.GetComponent<ParticleSystemRenderer>();
        Check(renderer.renderMode == ParticleSystemRenderMode.Mesh && renderer.mesh != null, "Foil uses real mesh geometry");
        Check(renderer.sharedMaterial != null && renderer.sharedMaterial.shader.isSupported && renderer.sharedMaterial.shader.name.Contains("Universal"), "Supported native URP shader is used");
        Check(renderer.shadowCastingMode == ShadowCastingMode.Off && !renderer.receiveShadows, "Foil neither casts nor receives shadows");
        Check(!system.collision.enabled && !system.trails.enabled && !system.lights.enabled, "No particle collision, trails or lights");
        Check(!renderer.sharedMaterial.IsKeywordEnabled("_EMISSION"), "Foil has no self illumination");
    }

    private float Spread(int count)
    {
        if (count == 0) return 0;
        var bounds = new Bounds(particles[0].position, Vector3.zero);
        for (int i = 1; i < count; i++) bounds.Encapsulate(particles[i].position);
        return bounds.size.magnitude;
    }

    private float MaxAlpha(ParticleSystem system, int count)
    {
        float alpha = 0;
        for (int i = 0; i < count; i++) alpha = Mathf.Max(alpha, particles[i].GetCurrentColor(system).a / 255f);
        return alpha;
    }

    private int Save(string path)
    {
        RenderTexture previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
        Color32[] pixels = image.GetPixels32(); Color32 background = pixels[0]; int visible = 0; brighterPixels = 0;
        float backgroundLuma = background.r * .2126f + background.g * .7152f + background.b * .0722f;
        foreach (Color32 p in pixels)
        {
            if (Math.Abs(p.r - background.r) + Math.Abs(p.g - background.g) + Math.Abs(p.b - background.b) > 9) visible++;
            if (p.r * .2126f + p.g * .7152f + p.b * .0722f > backgroundLuma + 5) brighterPixels++;
        }
        File.WriteAllBytes(path, image.EncodeToPNG());
        log.AppendLine(path + " | altered pixels " + visible + " | bright glints " + brighterPixels);
        Destroy(image); RenderTexture.active = previous;
        return visible;
    }

    private void Check(bool ok, string detail)
    {
        if (!ok) throw new Exception(detail);
        checks++; log.AppendLine("OK " + detail);
    }
}
#endif
