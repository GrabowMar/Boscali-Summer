#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BoscaliSummer.Modules.Weather.Audio;
using BoscaliSummer.Modules.Weather.Visuals;
using BoscaliSummer.Core.Fx;
using UnityEngine;
using UnityEngine.Audio;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// Standalone native-mesh and shader fixture; not a Nuclear Option camera/multiplayer test.
public sealed class WeatherUnityCheck : MonoBehaviour
{
    private static int assertions;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        assertions++;
    }

#if UNITY_EDITOR
    public static void Build()
    {
        try
        {
            Shader shader = Resources.Load<Shader>("CanopyRain");
            Check(shader != null && !ShaderUtil.ShaderHasError(shader), "Shader compile failed");
            Check(!ShaderUtil.ShaderHasError(Resources.Load<Shader>("TerrainRain")), "Terrain shader compile failed");
            Check(!ShaderUtil.ShaderHasError(Resources.Load<Shader>("CanopyDroplets")), "Droplet update shader compile failed");
            AssetDatabase.CreateAsset(RainStreakMaterial.CreateMaterial(null), "Assets/Resources/RainFallback.mat");
            AssetDatabase.CreateAsset(RainStreakMaterial.CreateMaterial(null, cameraFade: false), "Assets/Resources/CanopyFallback.mat");
            Type mixerType = typeof(Editor).Assembly.GetType("UnityEditor.Audio.AudioMixerController") ??
                Type.GetType("UnityEditor.Audio.AudioMixerController, UnityEditor", true);
            mixerType.GetMethod("CreateMixerControllerAtPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { "Assets/Resources/WeatherFixtureMixer.mixer" });
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("WeatherCheck").AddComponent<WeatherUnityCheck>();
            EditorSceneManager.SaveScene(scene, "Assets/check.unity");
            var report = BuildPipeline.BuildPlayer(new[] { "Assets/check.unity" }, "Player/WeatherCheck.exe",
                BuildTarget.StandaloneWindows64, BuildOptions.Development);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new Exception("Player build failed: " + report.summary.result);
            File.WriteAllText("build-result.txt", "PASS");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { File.WriteAllText("build-result.txt", e.ToString()); EditorApplication.Exit(1); }
    }
#endif

    private IEnumerator Start()
    {
        yield return null;
        try
        {
            Run();
            File.WriteAllText("result.txt", "PASS: " + assertions + " standalone weather assertions");
            Application.Quit(0);
        }
        catch (Exception e) { File.WriteAllText("result.txt", e.ToString()); Debug.LogException(e); Application.Quit(1); }
    }

    private static void Run()
    {
        CheckCloudDressing();
        CheckRainSoundscape();
        CheckRainAudioOwnership();
        CheckThunder();
        var atmosphere = new RainAtmosphere();
        float originalFog = RenderSettings.fogDensity;
        RenderSettings.fogDensity = 0f;
        atmosphere.Apply(1f, false);
        Check(RenderSettings.fogDensity > 0f, "Local rain adds haze to a fog-free mission");
        atmosphere.Apply(0f, false);
        Check(RenderSettings.fogDensity == 0f, "Dry weather restores the fog-free baseline");
        RenderSettings.fogDensity = originalFog;
        Shader shader = Resources.Load<Shader>("CanopyRain");
        Check(shader != null && shader.isSupported, "Rain shader unsupported in player");
        var opaque = new Material(Resources.Load<Shader>("Fixture")) { name = "Paint", renderQueue = 2000 };
        var glass = new Material(opaque) { name = "WindowGlass", renderQueue = 3000 };
        var hud = new Material(glass) { name = "HUD_Glass" };
        var root = new GameObject("Aircraft");
        var node = new GameObject("Canopy");
        node.transform.SetParent(root.transform, false);
        Mesh mesh = Quad();
        mesh.subMeshCount = 3;
        for (int i = 0; i < 3; i++) mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, i);
        mesh.UploadMeshData(true);
        node.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = node.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { opaque, hud, glass };
        Check(!mesh.isReadable, "Fixture must exercise non-readable native mesh behavior");
        CanopyGlassResolver.ResetForScene();
        var surfaces = CanopyGlassResolver.Resolve(root.transform, new Vector3(0,0,-1));
        Check(surfaces.Count == 1 && surfaces[0].Submesh == 2, "Select glass slot beyond first two; reject frame/HUD");
        Check(surfaces[0].Mesh == mesh, "Reuse native mesh without cloning");

        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(0,0,-3);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.3f,0.3f,0.3f);
        camera.orthographic = true;
        camera.orthographicSize = 0.65f;
        var dressing = new CanopyShaderDressing();
        typeof(CanopyShaderDressing).GetField("material", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(dressing, new Material(shader));
        Shader updateShader = Resources.Load<Shader>("CanopyDroplets");
        Check(updateShader != null, "Droplet update shader loads");
        PrimeSim(dressing, updateShader, surfaces, 30, 1f, 0f, Vector3.down);
        Check(dressing.Draw(surfaces, camera, 1f, 1f), "Draw non-readable glass");
        Check(renderer.sharedMaterials[0] == opaque && renderer.sharedMaterials[2] == glass,
            "Overlay must never replace original materials");
        renderer.enabled = false;
        Check(!dressing.Draw(surfaces, camera, 1f, 1f), "Hidden canopy must not draw");
        renderer.enabled = true;
        dressing.Detach(); dressing.Detach();
        Check(renderer.sharedMaterials[2] == glass, "Idempotent teardown preserves glass");

        var dropsObject = new GameObject("Fallback");
        var drops = dropsObject.AddComponent<CanopyDropletEmitter>();
        drops.Initialize();
        Check(!drops.GetComponent<ParticleSystemRenderer>().sharedMaterial.IsKeywordEnabled("_FADING_ON"),
            "Glass fallback droplets remain visible near the camera");
        drops.UpdateDrops(surfaces[0], 1f, 0f);
        Check(!drops.IsEmitting, "Unreadable mesh fallback must fail closed");

        for (int i = 0; i < 16; i++)
        {
            var pane = new GameObject("Windshield" + i);
            pane.transform.SetParent(root.transform, false);
            pane.AddComponent<MeshFilter>().sharedMesh = mesh;
            pane.AddComponent<MeshRenderer>().sharedMaterial = glass;
        }
        CanopyGlassResolver.ResetForScene();
        Check(CanopyGlassResolver.Resolve(root.transform, Vector3.zero).Count == CanopyGlassResolver.MaxSurfaces,
            "Multi-pane discovery must obey hard cap");
        CanopyGlassResolver.ResetForScene();
        Check(CanopyGlassResolver.Resolve(root.transform, Vector3.one * 100f).Count == 0,
            "Remote geometry must not count as cockpit glass");

        // Native cockpit parts are detached roots, and their interior glass is layer 3.
        var body = new GameObject("DetachedAircraftBody");
        var cockpit = new GameObject("DetachedCockpit");
        var interior = new GameObject("canopy_int");
        interior.transform.SetParent(cockpit.transform, false);
        interior.layer = 3;
        interior.AddComponent<MeshFilter>().sharedMesh = mesh;
        var interiorRenderer = interior.AddComponent<MeshRenderer>();
        interiorRenderer.sharedMaterials = new[] { opaque, hud, glass };
        Check(CanopyGlassResolver.Resolve(body.transform, Vector3.zero, 1 << 3).Count == 0,
            "Detached cockpit cannot be discovered from aircraft body");
        var interiorSurfaces = CanopyGlassResolver.Resolve(cockpit.transform, Vector3.zero, 1 << 3);
        Check(interiorSurfaces.Count == 1, "Discover detached interior glass through cockpit camera mask");
        typeof(CanopyShaderDressing).GetField("material", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(dressing, new Material(shader));
        camera.cullingMask = 1;
        Check(!dressing.Draw(interiorSurfaces, camera, 1f, 1f), "World camera cannot draw interior glass");
        camera.cullingMask = 1 << 3;
        PrimeSim(dressing, updateShader, interiorSurfaces, 30, 1f, 0f, Vector3.down);
        Check(dressing.Draw(interiorSurfaces, camera, 1f, 1f), "Cockpit camera draws GPU-only interior glass");
        Check(CanopyGlassResolver.Resolve(cockpit.transform, Vector3.zero, 1).Count == 0,
            "Camera mask changes invalidate cached discovery");
        dressing.Detach(); cockpit.SetActive(false); camera.cullingMask = -1;

        // Droplet sim: step real frames through the dressing, then render. Drops must
        // accumulate with rain, run with the slipstream, agree across split panes,
        // refract a scene gradient, and vanish fully when dry.
        root.SetActive(false);
        camera.enabled = false;
        camera = UnityEngine.Object.Instantiate(camera);
        camera.enabled = true;
        camera.orthographicSize = 1.05f;
        var texture = new RenderTexture(256, 256, 24);
        camera.targetTexture = texture;

        var rigRoot = new GameObject("SimRig");
        string[] paneNames = { "WindshieldC", "WindshieldL", "WindshieldR" };
        Vector3[] paneAt = { new Vector3(0f, 0f, 0f), new Vector3(-0.77f, 0f, -0.12f), new Vector3(0.77f, 0f, -0.12f) };
        float[] paneYaw = { 0f, 35f, -35f };
        float[] paneWide = { 1.0f, 0.6f, 0.6f };
        for (int p = 0; p < 3; p++)
        {
            var pane = new GameObject(paneNames[p]);
            pane.transform.SetParent(rigRoot.transform, false);
            pane.transform.position = paneAt[p];
            pane.transform.rotation = Quaternion.Euler(0f, paneYaw[p], 0f);
            var paneMesh = new Mesh();
            float x = paneWide[p] / 2f;
            paneMesh.vertices = new[] { new Vector3(-x, -0.5f, 0f), new Vector3(x, -0.5f, 0f),
                new Vector3(x, 0.5f, 0f), new Vector3(-x, 0.5f, 0f) };
            paneMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            paneMesh.RecalculateNormals();
            paneMesh.RecalculateBounds();
            pane.AddComponent<MeshFilter>().sharedMesh = paneMesh;
            pane.AddComponent<MeshRenderer>().sharedMaterial = glass;
        }
        CanopyGlassResolver.ResetForScene();
        var simSurfaces = CanopyGlassResolver.Resolve(rigRoot.transform, camera.transform.position, -1);
        Check(simSurfaces.Count == 3, "All three split panes resolve as glass: " + simSurfaces.Count);
        typeof(CanopyShaderDressing).GetField("material", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(dressing, new Material(shader));
        dressing.SetLighting(Vector3.up, Color.black, new Color(0.55f, 0.6f, 0.68f, 1f), false);

        Color32[] simBg = Capture(camera, texture, "sim-bg.png");
        PrimeSim(dressing, updateShader, simSurfaces, 5, 1f, 0f, Vector3.down);
        Check(dressing.Draw(simSurfaces, camera, 1f, 1f), "Fresh sim draws");
        Color32[] simFresh = Capture(camera, texture, "sim-fresh.png");
        PrimeSim(dressing, updateShader, simSurfaces, 85, 1f, 0f, Vector3.down);
        Check(FxRtPool.UsedBytes > 0 && FxRtPool.UsedBytes <= 4L * 1024 * 1024,
            "Canopy state stays within its 4 MiB render-target budget: " + FxRtPool.UsedBytes);
        Check(dressing.Draw(simSurfaces, camera, 1f, 1f), "Developed sim draws");
        Color32[] simWet = Capture(camera, texture, "sim-wet.png");
        int freshCount = DiffCount(simBg, simFresh, 6);
        double freshMass = DiffMass(simBg, simFresh);
        int wetCount = DiffCount(simBg, simWet, 6);
        double wetMass = DiffMass(simBg, simWet);
        Check(freshCount > 10, "Drops spawn within frames: " + freshCount);
        Check(wetMass > freshMass, "Rain accumulates water mass over time: " + freshMass + " -> " + wetMass);
        Check(wetCount < simBg.Length / 2, "Developed rain must not veil the pane: " + wetCount);
        Check(ThirdSpread(simWet, texture.width) < 5.0, "Split panes agree when parked");

        PrimeSim(dressing, updateShader, simSurfaces, 60, 1f, 1f, new Vector3(2f, 0.5f, 0f));
        Check(dressing.Draw(simSurfaces, camera, 1f, 1f), "Slipstream sim draws");
        Color32[] simSlip = Capture(camera, texture, "sim-slip.png");
        Check(DiffCount(simWet, simSlip, 8) > 100, "Slipstream advects the pattern");
        Check(ThirdSpread(simSlip, texture.width) < 5.0, "Split panes agree at speed");

        var gradient = new Texture2D(64, 64, TextureFormat.RGB24, false);
        for (int y = 0; y < 64; y++)
            for (int gx = 0; gx < 64; gx++)
                gradient.SetPixel(gx, y, new Color(y / 63f, y / 63f, y / 63f));
        gradient.Apply();
        Shader.SetGlobalTexture("_CameraOpaqueTexture", gradient);
        dressing.SetLighting(Vector3.up, Color.black, new Color(0.55f, 0.6f, 0.68f, 1f), true);
        Check(dressing.Draw(simSurfaces, camera, 1f, 1f), "Refracting sim draws");
        Color32[] simRefract = Capture(camera, texture, "sim-refract.png");
        Check(DiffCount(simBg, simRefract, 6) < simBg.Length / 2, "Refraction must not veil the pane");
        Shader.SetGlobalTexture("_CameraOpaqueTexture", Texture2D.blackTexture);
        UnityEngine.Object.Destroy(gradient);

        texture.Release();
        texture = new RenderTexture(1024, 1024, 24);
        camera.targetTexture = texture;
        camera.transform.position = new Vector3(-0.50f, 0f, -3f);
        camera.orthographicSize = 0.16f;
        dressing.SetLighting(Vector3.up, Color.black, new Color(0.55f, 0.6f, 0.68f, 1f), false);
        Check(dressing.Draw(simSurfaces, camera, 1f, 1f), "Close-up sim draws");
        Color32[] simClose = Capture(camera, texture, "sim-close.png");
        int closeCount = 0;
        for (int i = 0; i < simClose.Length; i++)
            if (Math.Abs(simClose[i].r - simBg[0].r) + Math.Abs(simClose[i].g - simBg[0].g) + Math.Abs(simClose[i].b - simBg[0].b) > 6) closeCount++;
        Check(closeCount > 5000, "Drops stay visible at close-up scale: " + closeCount);
        camera.transform.position = new Vector3(0f, 0f, -3f);
        camera.orthographicSize = 0.65f;
        texture.Release();
        texture = new RenderTexture(256, 256, 24);
        camera.targetTexture = texture;

        Check(!dressing.Draw(simSurfaces, camera, 0f, 1f), "Dry glass draws nothing and frees the sim");
        Check(FxRtPool.UsedBytes == 0, "Dry canopy releases every render target");
        Color32[] simDried = Capture(camera, texture, "sim-dried.png");
        Check(Array.TrueForAll(simDried, pixel => pixel.Equals(simDried[0])), "Dry glass must leave no residual filter");
        for (int frame = 0; frame < 450; frame++) dressing.SetColdMoisture(-6f, 0f, .1f);
        Check(!dressing.Draw(simSurfaces, camera, 0f, 1f), "Cold dry glass cannot create frost");
        for (int frame = 0; frame < 450; frame++) dressing.SetColdMoisture(-6f, 1f, .1f);
        Check(dressing.Draw(simSurfaces, camera, 0f, 1f), "Cold wet glass retains a thermal pane overlay");
        Color32[] frost = Capture(camera, texture, "canopy-cold-moisture.png");
        Check(DiffCount(simDried, frost, 2) > 50, "Wet cold pane produces visible restrained frost");
        int paneCenter = 128 * 256 + 128;
        Check(Math.Abs(frost[paneCenter].r - simDried[paneCenter].r) +
            Math.Abs(frost[paneCenter].g - simDried[paneCenter].g) +
            Math.Abs(frost[paneCenter].b - simDried[paneCenter].b) < 30,
            "Thermal pane center retains view readability");
        for (int frame = 0; frame < 1000; frame++) dressing.SetColdMoisture(15f, 0f, .1f);
        Check(!dressing.Draw(simSurfaces, camera, 0f, 1f), "Warm dry glass recovers fully from frost");
        dressing.Detach();
        Check(FxRtPool.UsedBytes == 0, "Thermal pane cleanup releases owned render targets");
        UnityEngine.Object.Destroy(rigRoot);
        var terrainMap = new GameObject("Map");
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.SetParent(terrainMap.transform, false);
        var terrainMaterial = new Material(Resources.Load<Shader>("TerrainFixture"));
        ground.GetComponent<MeshRenderer>().sharedMaterial = terrainMaterial;
        ground.GetComponent<MeshFilter>().sharedMesh.UploadMeshData(true);
        camera.transform.position = new Vector3(0,3,0);
        camera.transform.rotation = Quaternion.Euler(90,0,0);
        var terrain = new TerrainRainDressing();
        terrain.Update(terrainMap.transform,camera,0f,0f);
        Check(terrain.SurfaceCount == 1, "Native terrain shader discovery");
        var terrainOverlay = new Material(Resources.Load<Shader>("TerrainRain"));
        typeof(TerrainRainDressing).GetField("material",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(terrain,terrainOverlay);
        Color32[] dryGround = Capture(camera,texture,"terrain-dry.png");
        terrain.Update(terrainMap.transform,camera,1f,35f);
        Check(terrain.Wetness == 1f, "Ground becomes wet gradually");
        Color32[] wetGround = Capture(camera,texture,"terrain-wet.png");
        int center = 128 * 256 + 128;
        Check(wetGround[center].r < dryGround[center].r - 3 && wetGround[center].r > dryGround[center].r * 0.75f,
            "Terrain shader adds restrained darkening: " + dryGround[center].r + " -> " + wetGround[center].r);
        terrain.Update(terrainMap.transform,camera,0f,90f);
        Check(Math.Abs(terrain.Wetness - 0.5f) < 0.001f, "Ground retains water after rain stops");
        var terrainProperties = (MaterialPropertyBlock)typeof(TerrainRainDressing)
            .GetField("properties", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(terrain);
        Check(terrainProperties.GetFloat("_Rain") == 0f && terrainProperties.GetFloat("_Wetness") > 0f,
            "Impact rings stop when rain stops, while ground remains damp");
        ground.transform.localScale = new Vector3(4000f, 1f, 4000f);
        camera.transform.position = new Vector3(0f, 18000f, 0f);
        camera.orthographic = false;
        camera.fieldOfView = 70f;
        camera.farClipPlane = 40000f;
        camera.Render(); // Update native visibility after the fixture's changed pose.
        Color32[] highDryGround = Capture(camera, texture, "terrain-high-dry.png");
        terrain.Update(terrainMap.transform, camera, 0f, 0f);
        Check(terrainProperties.GetVector("_WetFade").y == 26000f,
            "High view uses the bounded distant damp-tone range");
        Color32[] highWetGround = Capture(camera, texture, "terrain-high-wet.png");
        Check(highWetGround[center].r < highDryGround[center].r - 3 &&
            highWetGround[center].r > highDryGround[center].r * .85f,
            "Broad damp tone survives the height-limit view without a dark mirror: " +
            highDryGround[center].r + " -> " + highWetGround[center].r);
        ground.transform.localScale = Vector3.one;
        camera.transform.position = new Vector3(0f, 3f, 0f);
        camera.orthographic = true;
        camera.Render();
        terrain.Reset(); terrain.Reset();
        Check(terrain.SurfaceCount == 0 && terrain.Wetness == 0f && ground.GetComponent<MeshRenderer>().sharedMaterial == terrainMaterial,
            "Terrain reset preserves original material and releases cached surfaces");
        var history = new BoscaliSummer.Modules.Weather.Domain.WeatherField();
        terrain.Update(terrainMap.transform, camera, 0f, 0f);
        typeof(TerrainRainDressing).GetField("material", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(terrain, new Material(Resources.Load<Shader>("TerrainRain")));
        var historyKey = new BoscaliSummer.Modules.Weather.Domain.WeatherKey(90210u, 0f, false,
            (byte)BoscaliSummer.Modules.Weather.Domain.WeatherRegimeType.Clear, 5f, 60f);
        history.Build(historyKey, 60f, 60000f, 60000f, 13f);
        terrain.Update(terrainMap.transform, camera, 0f, 0f, history, 60f, 1f);
        Check(terrain.Wetness > .99f, "New ground surface replays recent mission precipitation");
        history.Build(historyKey, 0f, 60000f, 60000f, 13f);
        terrain.Update(terrainMap.transform, camera, 0f, 0f, history, 0f, 0f);
        Check(terrain.Wetness == 0f && ground.GetComponent<MeshRenderer>().sharedMaterial == terrainMaterial,
            "Mission-clock rollback discards future wetness and preserves native ground");
        terrain.Reset();
        terrainMap.SetActive(false);

        var streaks = new GameObject("Streaks").AddComponent<ProceduralRainEmitter>();
        streaks.Initialize(camera);
        streaks.UpdateRain(Vector3.forward * 250f, Vector3.zero, 1f, camera);
        var ps = streaks.GetComponent<ParticleSystem>();
        Check(ps.main.simulationSpace == ParticleSystemSimulationSpace.Custom && ps.main.maxParticles == 2500,
            "Relative rain needs translating simulation frame and fixed budget");
        Material streakMaterial = ps.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        Check(streakMaterial.GetInt("_DstBlend") == (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha,
            "Rain retains contrast against bright sky instead of additive washout");
        Check(streakMaterial.GetVector("_CameraFadeParams").y > 0f && streakMaterial.IsKeywordEnabled("_FADING_ON"),
            "Runtime rain material uploads the camera fade coefficients");
        Check(ps.velocityOverLifetime.enabled && ps.velocityOverLifetime.space == ParticleSystemSimulationSpace.World &&
            Math.Abs(ps.velocityOverLifetime.z.constant + 250f) < .001f,
            "Existing particles receive the current common camera-relative velocity");
        float rainAspect = camera.aspect;
        camera.aspect = 16f / 9f;
        camera.fieldOfView = 100f;
        streaks.UpdateRain(Vector3.zero, Vector3.zero, 1f, camera);
        Check(ps.shape.scale.x > 20f && ps.shape.scale.y > 18f && ps.main.maxParticles == 2500,
            "Wide ground/exterior cameras spread the bounded rain stream across their view");
        Check(Math.Abs(ps.velocityOverLifetime.z.constant) < .001f &&
            Math.Abs(ps.velocityOverLifetime.y.constant + 9f) < .001f,
            "Orbit/hover rain removes obsolete fast-flight velocity from all live drops");
        camera.aspect = rainAspect;
        camera.fieldOfView = 70f;
        Color dayStreak = ps.main.startColor.color;
        streaks.UpdateRain(Vector3.zero, Vector3.zero, 1f, camera, lightLevel: 0.04f);
        Color nightStreak = ps.main.startColor.color;
        Check(Math.Abs(nightStreak.r - dayStreak.r * 0.22f) < 0.001f &&
            Math.Abs(nightStreak.g - dayStreak.g * 0.22f) < 0.001f &&
            Math.Abs(nightStreak.b - dayStreak.b * 0.22f) < 0.001f && nightStreak.a == dayStreak.a,
            "Night rain retains the authored 22 percent visibility floor without changing particle coverage");
        streaks.UpdateRain(Vector3.zero, Vector3.zero, 0f, camera);
        Check(!ps.isEmitting, "Dry weather stops rain emission");
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var fixedVelocity = ps.velocityOverLifetime;
        fixedVelocity.x = -20f; fixedVelocity.y = -9f; fixedVelocity.z = 0f;
        ps.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, velocity = Vector3.zero,
            startLifetime = 10f, startSize = .03f, startColor = Color.white }, 1);
        ps.Simulate(.1f, false, false, false);
        var probeDrops = new ParticleSystem.Particle[4];
        Check(ps.GetParticles(probeDrops) == 1, "Single live native drop for exterior-turn regression");
        Vector3 beforeTurn = probeDrops[0].position;
        fixedVelocity.x = 0f; fixedVelocity.z = -20f;
        ps.Simulate(.1f, false, false, false);
        Check(ps.GetParticles(probeDrops) == 1 && Math.Abs(probeDrops[0].position.x - beforeTurn.x) < .01f &&
            probeDrops[0].position.z < beforeTurn.z - 1.9f,
            "A surviving native rain drop follows changed relative motion without a per-particle CPU update");
        // Move the real camera and let production UpdateRain translate its custom frame.
        // With no horizontal wind, the same drop must stay at the same world X/Z. This
        // fails if camera travel is omitted, subtracted twice, or retained after a turn.
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var probeEmission = ps.emission;
        probeEmission.enabled = false;
        ps.Play();
        ps.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, velocity = Vector3.zero,
            startLifetime = 10f, startSize = .03f, startColor = Color.white }, 1);
        Check(ps.GetParticles(probeDrops) == 1, "Moving-camera probe starts with one surviving drop");
        Vector3 originalWorldDrop = ps.main.customSimulationSpace.TransformPoint(probeDrops[0].position);
        float motionStep = Time.deltaTime;
        Check(motionStep > 0f && motionStep < .5f, "Moving-camera probe uses the actual finite frame duration");
        camera.transform.position += Vector3.right * (20f * motionStep);
        streaks.UpdateRain(Vector3.zero, Vector3.zero, 1f, camera);
        ps.Simulate(motionStep, false, false, false);
        Check(ps.GetParticles(probeDrops) == 1, "Camera translation preserves the original drop");
        Vector3 translatedWorldDrop = ps.main.customSimulationSpace.TransformPoint(probeDrops[0].position);
        Check(Math.Abs(translatedWorldDrop.x - originalWorldDrop.x) < .005f &&
            Math.Abs(translatedWorldDrop.z - originalWorldDrop.z) < .005f,
            "Native camera translation is compensated exactly once by the custom rain frame");
        camera.transform.position += Vector3.forward * (20f * motionStep);
        streaks.UpdateRain(Vector3.zero, Vector3.zero, 1f, camera);
        ps.Simulate(motionStep, false, false, false);
        Check(ps.GetParticles(probeDrops) == 1, "Camera turn preserves the original drop");
        Vector3 turnedWorldDrop = ps.main.customSimulationSpace.TransformPoint(probeDrops[0].position);
        Check(Math.Abs(turnedWorldDrop.x - originalWorldDrop.x) < .005f &&
            Math.Abs(turnedWorldDrop.z - originalWorldDrop.z) < .005f,
            "A moving native camera turn does not double-count travel or retain the old relative velocity");

        // Read the actual GPU sprite: production discards its CPU copy after upload.
        var sprite = RainStreakMaterial.CreateTexture();
        var spriteTarget = RenderTexture.GetTemporary(sprite.width, sprite.height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(sprite, spriteTarget);
        RenderTexture previousTarget = RenderTexture.active;
        RenderTexture.active = spriteTarget;
        var spriteReadback = new Texture2D(sprite.width, sprite.height, TextureFormat.RGBA32, false);
        spriteReadback.ReadPixels(new Rect(0, 0, sprite.width, sprite.height), 0, 0);
        spriteReadback.Apply();
        for (int y = 0; y < sprite.height; y++)
            Check(spriteReadback.GetPixel(0, y).a < 0.01f && spriteReadback.GetPixel(sprite.width - 1, y).a < 0.01f,
                "Streak sides fade to transparent");
        Check(spriteReadback.GetPixel(sprite.width / 2, sprite.height / 2).a > 0.9f, "Soft streak retains a visible core");
        File.WriteAllBytes("rain-streak.png", spriteReadback.EncodeToPNG());
        RenderTexture.active = previousTarget;
        RenderTexture.ReleaseTemporary(spriteTarget);
        UnityEngine.Object.Destroy(spriteReadback);
        UnityEngine.Object.Destroy(sprite);
    }

    private static void CheckRainSoundscape()
    {
        float[] rush = RainSoundscape.BakeRush(2, out int rushFrames);
        float[] patter = RainSoundscape.BakePatter(2, out int patterFrames);
        Check(rushFrames > 40000 && patterFrames > 40000,
            "Rain clips have a bounded seamless-loop length");
        float rushPeak = 0f, patterPeak = 0f;
        for (int i = 0; i < rushFrames * 2; i++) rushPeak = Mathf.Max(rushPeak, Mathf.Abs(rush[i]));
        for (int i = 0; i < patterFrames * 2; i++) patterPeak = Mathf.Max(patterPeak, Mathf.Abs(patter[i]));
        Check(rushPeak > 0.02f && rushPeak < 0.8f && patterPeak > 0.01f && patterPeak < 0.8f,
            "Rain rush and canopy taps are audible without clipping");
        double rushEnergy = 0, patterEnergy = 0;
        for (int i = 0; i < rushFrames * 2; i++) rushEnergy += rush[i] * rush[i];
        for (int i = 0; i < patterFrames * 2; i++) patterEnergy += patter[i] * patter[i];
        Check(Math.Sqrt(rushEnergy / (rushFrames * 2)) > 0.10 &&
            Math.Sqrt(patterEnergy / (patterFrames * 2)) > 0.06,
            "Baked loops retain useful average level, not merely isolated peaks");
        // Reviewable heavy-rain cockpit mix before the game's effects mixer.
        using (var writer = new BinaryWriter(File.Create("cockpit-rain.wav")))
        {
            int frames = Math.Min(rushFrames, patterFrames);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + frames * 4);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
            writer.Write((short)1); writer.Write((short)2); writer.Write(22050); writer.Write(88200);
            writer.Write((short)4); writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(frames * 4);
            for (int i = 0; i < frames * 2; i++)
                writer.Write((short)(Mathf.Clamp(patter[i] * .35f, -1f, 1f) * 32767f));
        }
    }

    private static void CheckThunder()
    {
        float[] a = ThunderSoundscape.Bake(451), b = ThunderSoundscape.Bake(783);
        Check(a.Length == 22050 * 6 && b.Length == a.Length, "Thunder bank has bounded six-second clips");
        float peak = 0f; double difference = 0;
        for (int i = 0; i < a.Length; i++)
        {
            Check(!float.IsNaN(a[i]) && !float.IsInfinity(a[i]), "Thunder samples remain finite");
            peak = Math.Max(peak, Math.Abs(a[i])); difference += Math.Abs(a[i] - b[i]);
        }
        Check(peak <= .721f && peak > .7f && difference > 100f, "Thunder voices differ and preserve peak headroom");
        var thunder = new ThunderSoundscape();
        for (int i = 0; i < 20; i++) thunder.Enqueue(100f, 2000f, (uint)i, 0f);
        Check(thunder.Queued == 8, "Thunder arrival queue has a hard cap");
        thunder.Tick(101f, false, false);
        Check(thunder.Queued == 0 && thunder.Playing == 0, "Audio-off immediately clears delayed thunder");
        thunder.Enqueue(100f, 10001f, 9, 0f);
        Check(thunder.Queued == 0, "Inaudible distant thunder is culled");
        thunder.Dispose(); thunder.Dispose();
    }

    private static void CheckRainAudioOwnership()
    {
        var mixer = Resources.Load<AudioMixer>("WeatherFixtureMixer");
        Check(mixer != null && mixer.FindMatchingGroups("Master").Length > 0, "Rain fixture has a native effects mixer");
        SoundManager.i = new SoundManager { EffectsMixer = mixer.FindMatchingGroups("Master")[0] };
        var rain = new GameObject("Rain sound ownership").AddComponent<RainSoundscape>(); rain.Initialize();
        // Install deterministic already-baked PCM so this component check does not race its worker.
        var rush = AudioClip.Create("Fixture rush", 22050, 2, 22050, false);
        var patter = AudioClip.Create("Fixture patter", 22050, 2, 22050, false);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(RainSoundscape).GetField("rushClip", fields).SetValue(rain, rush);
        typeof(RainSoundscape).GetField("patterClip", fields).SetValue(rain, patter);
        var rushSource = (AudioSource)typeof(RainSoundscape).GetField("rush", fields).GetValue(rain);
        var patterSource = (AudioSource)typeof(RainSoundscape).GetField("patter", fields).GetValue(rain);
        rushSource.clip = rush; patterSource.clip = patter;
        rain.UpdateAudio(1f, 0f, 1f, true, true);
        Check(patterSource.isPlaying && !rushSource.isPlaying && LoopCount() == 1,
            "Cockpit rain owns exactly one audible patter loop");
        rain.UpdateAudio(1f, 0f, 1f, false, true);
        Check(rushSource.isPlaying && !patterSource.isPlaying && LoopCount() == 1,
            "Exterior view replaces patter with exactly one rush loop");
        rain.UpdateAudio(1f, 0f, 1f, true, false);
        Check(!rain.IsPlaying && !rushSource.isPlaying && !patterSource.isPlaying &&
            rain.RushVolume == 0f && rain.PatterVolume == 0f && LoopCount() == 0,
            "Audio-off stops rain immediately and frees its voice");
        rain.UpdateAudio(0f, 1f, 1f, true, true, 200f);
        Check(patterSource.isPlaying && !rushSource.isPlaying && LoopCount() == 1,
            "Cockpit cloud at speed owns exactly one audible patter loop");
        rain.Release(); rain.Release(); SoundManager.i = null;
        Check(LoopCount() == 0, "Repeated rain release leaves no voice reservation");
    }
    private static int LoopCount()
    {
        var state = new System.Collections.Generic.Dictionary<string, object>();
        FxVoiceBus.Describe(state); return (int)state["fxLoops"];
    }

    private static void CheckCloudDressing()
    {
        var weather = new GameObject("WeatherFixture").AddComponent<LevelInfo>();
        var layer = new GameObject("NativeClouds").AddComponent<CloudLayer>();
        var particles = layer.gameObject.AddComponent<ParticleSystem>();
        var flyThrough = new GameObject("FlyThrough").AddComponent<ParticleSystem>();
        flyThrough.transform.SetParent(layer.transform, false);
        var distant = new GameObject("DistantClouds").AddComponent<ParticleSystem>();
        distant.transform.SetParent(layer.transform, false);
        var sheet = new GameObject("CloudSheet").AddComponent<MeshRenderer>();
        sheet.transform.SetParent(layer.transform, false);
        var lightning = new GameObject("Lightning").AddComponent<Lightning>();
        lightning.transform.SetParent(layer.transform, false);
        var strikes = lightning.gameObject.AddComponent<ParticleSystem>();
        var flash = new GameObject("LightningFlash").AddComponent<Light>();
        flash.transform.SetParent(layer.transform, false);
        lightning.SetEffects(strikes, flash);
        layer.SetCloudSystem(particles);
        layer.SetDistantSystem(distant);
        layer.SetFlyThroughSystem(flyThrough);
        layer.SetCloudRenderer(sheet);
        layer.SetLightning(lightning);
        weather.SetCloudLayer(layer);
        var dressing = new CloudDressing();
        flyThrough.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        Check(!dressing.InCloud(weather), "Dry native cloud fly-through is ignored");
        flyThrough.Play();
        Check(dressing.InCloud(weather), "Native fly-through starts canopy cloud moisture");
        flyThrough.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        dressing.Apply(weather);
        Check(dressing.Applied && Mathf.Approximately(layer.SizeMin, 540f) &&
            Mathf.Approximately(layer.SizeMax, 900f),
            "Cloud particles become larger");
        Check(Mathf.Approximately(layer.MapScale, 412.5f) &&
            Mathf.Approximately(layer.Thickness, 202.5f), "Cloud pattern and deck grow");
        Check(layer.ParticleLimit == 60 && particles.main.maxParticles == 60,
            "Larger clouds use fewer particles");
        dressing.Apply(weather);
        Check(Mathf.Approximately(layer.SizeMin, 540f), "Repeated apply does not compound size");
        dressing.Apply(weather, 1f, 1f);
        Check(layer.SizeMin > 540f && layer.SizeMax > 900f && layer.Thickness > 202.5f,
            "Fronts and cell cores deepen native cloud shapes");
        dressing.Restore();
        Check(!dressing.Applied && layer.SizeMin == 300f && layer.SizeMax == 500f &&
            layer.MapScale == 250f && layer.Thickness == 150f &&
            layer.ParticleLimit == 100 && particles.main.maxParticles == 100,
            "Cloud settings restore on disable");
        dressing.Apply(weather);
        var foreignLimit = particles.main;
        foreignLimit.maxParticles = 7;
        dressing.Restore();
        Check(layer.ParticleLimit == 100 && particles.main.maxParticles == 7,
            "Restore leaves a later particle-system override alone");
        var renderer = particles.GetComponent<ParticleSystemRenderer>();
        var distantRenderer = distant.GetComponent<ParticleSystemRenderer>();
        var flyThroughRenderer = flyThrough.GetComponent<ParticleSystemRenderer>();
        var strikeRenderer = strikes.GetComponent<ParticleSystemRenderer>();
        distantRenderer.enabled = false;
        Datum.LocalSeaY = -600f;
        weather.cloudHeight = 2400f;
        dressing.SetNativeHidden(weather, true);
        Check(dressing.NativeHidden && !renderer.enabled && !distantRenderer.enabled &&
            !flyThroughRenderer.enabled && !sheet.enabled && !layer.enabled,
            "Volume clouds suppress every native cloud surface and cookie updater");
        Check(!lightning.enabled && !strikeRenderer.enabled && !flash.enabled,
            "Native lightning cannot flash or emit during volume ownership");
        Check(Mathf.Approximately(layer.transform.position.y, 1800f),
            "Suppressed native layer retains the correct floating-origin altitude");
        distant.gameObject.SetActive(false);
        distant.gameObject.SetActive(true);
        distantRenderer.enabled = true;
        lightning.enabled = true;
        flash.enabled = true;
        weather.cloudHeight = 2600f;
        dressing.SetNativeHidden(weather, true);
        Check(!distantRenderer.enabled && !lightning.enabled && !flash.enabled &&
            Mathf.Approximately(layer.transform.position.y, 2000f),
            "Repeated suppression survives native asynchronous object reactivation");
        dressing.SetNativeHidden(null, false);
        Check(!dressing.NativeHidden && renderer.enabled && !distantRenderer.enabled &&
            flyThroughRenderer.enabled && sheet.enabled && layer.enabled &&
            lightning.enabled && strikeRenderer.enabled && !flash.enabled,
            "Native cloud and lightning component states restore without replaying an old flash");
        layer.enabled = false;
        lightning.enabled = false;
        dressing.SetNativeHidden(weather, true);
        dressing.Restore();
        dressing.Restore();
        Check(!layer.enabled && !lightning.enabled && renderer.enabled,
            "Idempotent restore preserves components disabled before takeover");
        Datum.LocalSeaY = 0f;
        UnityEngine.Object.Destroy(weather.gameObject);
        UnityEngine.Object.Destroy(layer.gameObject);
    }

    // Steps the dressing sim with an injected update material (the fixture player
    // has no embedded bundle to load it from).
    private static void PrimeSim(CanopyShaderDressing dressing, Shader updateShader,
        System.Collections.Generic.IReadOnlyList<CanopySurface> surfaces,
        int frames, float rain, float speed, Vector3 slip)
    {
        var updateField = typeof(CanopyShaderDressing).GetField("updateMaterial", BindingFlags.Instance | BindingFlags.NonPublic);

        if (updateField.GetValue(dressing) as Material == null)
            updateField.SetValue(dressing, new Material(updateShader));
        for (int f = 0; f < frames; f++)
            dressing.UpdateSim(surfaces, rain, speed, slip, 1f / 30f);
    }

    private static double DiffMass(Color32[] a, Color32[] b)
    {
        double mass = 0;
        for (int i = 0; i < a.Length; i++)
            mass += Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b);
        return mass;
    }

    private static int DiffCount(Color32[] a, Color32[] b, int threshold)
    {
        int count = 0;
        for (int i = 0; i < a.Length; i++)
            if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > threshold) count++;
        return count;
    }

    private static double ThirdSpread(Color32[] wet, int width)
    {
        double[] third = new double[3];
        int[] count = new int[3];
        for (int i = 0; i < wet.Length; i++)
        {
            int slot = (i % width) * 3 / width;
            third[slot] += wet[i].r + wet[i].g + wet[i].b;
            count[slot]++;
        }
        return Math.Max(Math.Abs(third[0] / count[0] - third[1] / count[1]),
            Math.Abs(third[2] / count[2] - third[1] / count[1]));
    }
    private static Mesh Quad()
    {
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-0.5f,-0.5f,0), new Vector3(0.5f,-0.5f,0),
            new Vector3(0.5f,0.5f,0), new Vector3(-0.5f,0.5f,0) };
        mesh.triangles = new[] { 0,1,2,0,2,3 };
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    private static Color32[] Capture(Camera camera, RenderTexture target, string path)
    {
        camera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        var image = new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
        image.ReadPixels(new Rect(0,0,target.width,target.height),0,0); image.Apply();
        File.WriteAllBytes(path,image.EncodeToPNG());
        Color32[] pixels = image.GetPixels32();
        UnityEngine.Object.Destroy(image); RenderTexture.active = previous;
        return pixels;
    }
}
#endif
