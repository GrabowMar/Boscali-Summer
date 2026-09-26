#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BoscaliSummer.Features.Weather.Visuals;
using UnityEngine;
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
            AssetDatabase.CreateAsset(new Material(Shader.Find("Particles/Standard Unlit")), "Assets/Resources/RainFallback.mat");
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
        var clip = (AudioClip)typeof(BoscaliSummer.Features.Weather.Audio.ProceduralRainAudio)
            .GetMethod("SynthesizeCanopyPatterClip", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { "RainPreview", 24f, 44100 });
        var samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);
        float peak = 0f;
        foreach (float sample in samples)
        {
            if (float.IsNaN(sample) || float.IsInfinity(sample)) throw new Exception("Invalid rain audio sample");
            peak = Mathf.Max(peak, Mathf.Abs(sample));
        }
        Check(peak > 0.01f && peak < 0.8f, "Rain taps must have headroom without clipping");
        double energy = 0, sharpness = 0, stereo = 0;
        for (int i = 2; i < samples.Length; i += 2)
        {
            energy += samples[i] * samples[i];
            sharpness += Math.Pow(samples[i] - samples[i - 2], 2);
            stereo += Math.Pow(samples[i] - samples[i + 1], 2);
        }
        Check(energy > 0.001 && sharpness / energy < 0.15, "Patter must remain softly filtered, not sharp clicks");
        Check(stereo / energy > 0.02 && stereo / energy < 1, "Patter needs gentle stereo separation");
        Check(Math.Abs(samples[0] - samples[samples.Length - 2]) < 0.01f &&
            Math.Abs(samples[1] - samples[samples.Length - 1]) < 0.01f, "Stereo loop seam must not pop");
        using (var wav = new BinaryWriter(File.Create("rain-patter-preview.wav")))
        {
            wav.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); wav.Write(36 + samples.Length * 2);
            wav.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); wav.Write(16);
            wav.Write((short)1); wav.Write((short)2); wav.Write(44100); wav.Write(44100*4);
            wav.Write((short)4); wav.Write((short)16);
            wav.Write(System.Text.Encoding.ASCII.GetBytes("data")); wav.Write(samples.Length * 2);
            foreach (float sample in samples) wav.Write((short)(sample * 32767));
        }
        UnityEngine.Object.Destroy(clip);
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
        Color32[] simDried = Capture(camera, texture, "sim-dried.png");
        Check(Array.TrueForAll(simDried, pixel => pixel.Equals(simDried[0])), "Dry glass must leave no residual filter");
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
        terrain.Reset(); terrain.Reset();
        Check(terrain.SurfaceCount == 0 && terrain.Wetness == 0f && ground.GetComponent<MeshRenderer>().sharedMaterial == terrainMaterial,
            "Terrain reset preserves original material and releases cached surfaces");
        terrainMap.SetActive(false);

        var streaks = new GameObject("Streaks").AddComponent<ProceduralRainEmitter>();
        streaks.Initialize(camera);
        streaks.UpdateRain(Vector3.forward * 250f, Vector3.zero, 1f, camera);
        var ps = streaks.GetComponent<ParticleSystem>();
        Check(ps.main.simulationSpace == ParticleSystemSimulationSpace.Custom && ps.main.maxParticles == 1000,
            "Relative rain needs translating simulation frame and fixed budget");
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
