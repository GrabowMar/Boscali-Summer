#if UNITY_2022_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using BoscaliSummer.Garrisons;
using BoscaliSummer.Core.Fx;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

public static class HaloFlareAssetMeasure
{
    public const string ModelRoot = "Assets/HaloModels/";
    public static Vector3 Left, Right;

    public static void Run()
    {
        try { Measure(); EditorApplication.Exit(0); }
        catch (Exception e) { File.WriteAllText("halo-measure.txt", "FAIL: " + e); Debug.LogException(e); EditorApplication.Exit(1); }
    }

    public static void Measure()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelRoot + "Models/AirborneJumper.fbx");
        importer.isReadable = true; importer.SaveAndReimport();
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelRoot + "Models/AirborneJumper.fbx");
        var filter = asset.GetComponentInChildren<MeshFilter>(true);
        var skinned = asset.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Mesh mesh = skinned ? skinned.sharedMesh : filter.sharedMesh;
        Transform renderer = skinned ? skinned.transform : filter.transform;
        Material[] materials = skinned ? skinned.sharedMaterials : filter.GetComponent<Renderer>().sharedMaterials;
        Vector3[] vertices = mesh.vertices, delta = new Vector3[mesh.vertexCount];
        mesh.GetBlendShapeFrameVertices(mesh.GetBlendShapeIndex("Track"), 0, delta, null, null);
        var text = new StringBuilder();
        text.AppendLine("Model " + asset.name + " | root scale " + asset.transform.localScale + " | renderer " + renderer.localToWorldMatrix + " | bounds " + mesh.bounds);
        var keys = new Dictionary<Vector3, int>(); var parent = new Dictionary<int, int>();
        Func<int, int> find = null;
        find = i => parent[i] == i ? i : parent[i] = find(parent[i]);
        Func<int, int> welded = i =>
        {
            Vector3 v = vertices[i];
            var key = new Vector3(Mathf.Round(v.x * 100000), Mathf.Round(v.y * 100000), Mathf.Round(v.z * 100000));
            int id; if (!keys.TryGetValue(key, out id)) { id = i; keys[key] = id; parent[id] = id; }
            return id;
        };
        var members = new Dictionary<int, List<int>>();
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            text.AppendLine("Material " + s + " " + materials[s].name);
            if (!materials[s].name.Contains("AB_Gear")) continue;
            int[] triangles = mesh.GetTriangles(s);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = welded(triangles[i]), b = welded(triangles[i + 1]), c = welded(triangles[i + 2]);
                parent[find(b)] = find(a); parent[find(c)] = find(a);
            }
            foreach (int i in triangles.Distinct())
            {
                int key = find(welded(i)); List<int> list;
                if (!members.TryGetValue(key, out list)) members[key] = list = new List<int>();
                list.Add(i);
            }
        }
        foreach (List<int> list in members.Values)
        {
            Bounds bounds = new Bounds(renderer.TransformPoint(vertices[list[0]]), Vector3.zero);
            foreach (int i in list) bounds.Encapsulate(renderer.TransformPoint(vertices[i]));
            text.AppendLine("Gear component " + list.Count + " rest centre " + bounds.center.ToString("F6") + " size " + bounds.size.ToString("F6"));
            if (bounds.size.magnitude > .09f || bounds.center.y > -.4f) continue;
            // The source's terminal 6-sided gear cylinder is 20 mm long. Its
            // lower end ring is the cap facing out of the ankle canister.
            int[] cap = list.Where(i => renderer.TransformPoint(vertices[i]).y < bounds.min.y + .0001f).Distinct().ToArray();
            Vector3 sum = Vector3.zero; int n = 0;
            var unique = new HashSet<Vector3>();
            foreach (int i in cap) if (unique.Add(vertices[i])) { sum += vertices[i] + delta[i]; n++; }
            Vector3 tip = sum / Math.Max(1, n);
            if (tip.x < 0) Left = tip; else Right = tip;
            text.AppendLine("TRACK TIP ring=" + n + " mesh-local=" + tip.ToString("F9") + " wrapper-local=" + renderer.TransformPoint(tip).ToString("F9"));
        }
        File.WriteAllText("halo-measure.txt", text.ToString());
        Debug.Log(text.ToString());
    }
}
#endif

// Native FBX geometry and textures, production HaloGlidePlan/HaloFlareVisuals,
// and the unchanged GlowFx.Glow material/texture recipe in a real URP player.
public sealed class HaloFlareUnityCheck : MonoBehaviour
{
    [Serializable] private sealed class Tips { public Vector3 left, right; }
    private readonly StringBuilder log = new StringBuilder();
    private int assertions;
    private Camera cameraEye;
    private Light sun;
    private RenderTexture target;
    private RenderTexture displayReadback;
    private Transform operation;
    private HaloFlareVisuals effect;
    private readonly List<Transform> bodies = new List<Transform>();
    private readonly List<Transform> canopies = new List<Transform>();
    private Tips tips;
    private HaloGlidePlan plan;
    private bool show, formation, burning = true, deployed;
    private string output;
    private Color32[] baseline;
    private Color32[] lastPixels;
    private Vector3 motion, viewFrom, viewAt;

#if UNITY_EDITOR
    public static void Build()
    {
        try
        {
            HaloFlareAssetMeasure.Measure();
            File.WriteAllText("Assets/Resources/HaloTips.json", JsonUtility.ToJson(new Tips { left = HaloFlareAssetMeasure.Left, right = HaloFlareAssetMeasure.Right }));
            AssetDatabase.ImportAsset("Assets/Resources/HaloTips.json");
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>("Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            if (renderer.postProcessData == null) throw new Exception("Installed URP post-process shader resources unavailable");
            AssetDatabase.CreateAsset(renderer, "Assets/Resources/HaloRenderer.asset");
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            pipeline.msaaSampleCount = 4; pipeline.renderScale = 1; pipeline.supportsHDR = true;
            pipeline.colorGradingMode = ColorGradingMode.HighDynamicRange;
            AssetDatabase.CreateAsset(pipeline, "Assets/Resources/HaloPipeline.asset");
            GraphicsSettings.renderPipelineAsset = pipeline; QualitySettings.renderPipeline = pipeline;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, "Assets/Resources/HaloDisplay.asset");
            var tonemap = profile.Add<Tonemapping>(true); tonemap.mode.Override(TonemappingMode.ACES);
            AssetDatabase.AddObjectToAsset(tonemap, profile);
            EditorUtility.SetDirty(profile); EditorUtility.SetDirty(tonemap);
            PlayerSettings.colorSpace = ColorSpace.Linear; PlayerSettings.graphicsJobs = false;
            foreach (string name in new[] { "AirborneJumper", "AirborneCanopy" })
            {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(HaloFlareAssetMeasure.ModelRoot + "Models/" + name + ".fbx");
                var wrapper = new GameObject(name);
                GameObject imported = UnityEngine.Object.Instantiate(asset, wrapper.transform, false);
                foreach (var filter in imported.GetComponentsInChildren<MeshFilter>(true))
                {
                    var plain = filter.GetComponent<MeshRenderer>(); Mesh mesh = filter.sharedMesh; Material[] materials = plain.sharedMaterials;
                    GameObject node = filter.gameObject;
                    UnityEngine.Object.DestroyImmediate(plain); UnityEngine.Object.DestroyImmediate(filter);
                    var skin = node.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh; skin.sharedMaterials = materials;
                    skin.localBounds = mesh.bounds; skin.updateWhenOffscreen = true;
                }
                PrefabUtility.SaveAsPrefabAsset(wrapper, "Assets/Resources/" + name + ".prefab");
                UnityEngine.Object.DestroyImmediate(wrapper);
            }
            // Preserve the exact additive production shader variant in the player.
            var glow = new Material(GlowFx.Glow);
            AssetDatabase.CreateAsset(UnityEngine.Object.Instantiate(glow.GetTexture("_BaseMap")), "Assets/Resources/HaloGlowTexture.asset");
            glow.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture>("Assets/Resources/HaloGlowTexture.asset"));
            AssetDatabase.CreateAsset(glow, "Assets/Resources/HaloGlow.mat");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("HALO flare check").AddComponent<HaloFlareUnityCheck>();
            EditorSceneManager.SaveScene(scene, "Assets/halo-check.unity"); AssetDatabase.SaveAssets();
            var result = BuildPipeline.BuildPlayer(new[] { "Assets/halo-check.unity" }, "HaloPlayer/HaloCheck.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Player build failed: " + result.summary.result);
            File.WriteAllText("halo-build-result.txt", "PASS: real URP player built from unchanged production helper, glow recipe, glide plan and exact FBX assets.");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { File.WriteAllText("halo-build-result.txt", "FAIL: " + e); Debug.LogException(e); EditorApplication.Exit(1); }
    }
#endif

    private IEnumerator Start()
    {
        output = Arg("-haloOutput") ?? Directory.GetCurrentDirectory(); Directory.CreateDirectory(output);
        bool failed = false; IEnumerator run = Run();
        while (true)
        {
            bool next;
            try { next = run.MoveNext(); }
            catch (Exception e) { log.AppendLine("FAIL: " + e); Debug.LogException(e); failed = true; break; }
            if (!next) break;
            yield return run.Current;
        }
        show = false; effect?.Clear();
        File.WriteAllText(Path.Combine(output, "halo-result.txt"), (failed ? "FAIL: " : "PASS: " + assertions + " assertions. ") +
            "Offline real URP with production flare helper, GlowFx.Glow, HaloGlidePlan, exact airborne FBX and native soldier textures. " +
            "No native aircraft/mount, game phase adapter, multiplayer, gameplay flare or live acceptance executed.\n" + log);
        Application.Quit(failed ? 1 : 0);
    }

    private IEnumerator Run()
    {
        log.AppendLine("GPU " + SystemInfo.graphicsDeviceName + " | " + SystemInfo.graphicsDeviceType);
        log.AppendLine("Substitute display: HDR ARGBHalf target, standard URP ACES tonemapping, no bloom; native game scene/camera/postprocess are not reproduced.");
        Check(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset, "Real URP pipeline active");
        operation = new GameObject("Datum-parented operation substitute").transform;
        Material source = GlowFx.Glow; Color sourceTint = source.GetColor("_BaseColor");
        effect = new HaloFlareVisuals(operation, source);
        ParticleSystem[] systems = operation.GetComponentsInChildren<ParticleSystem>();
        Check(systems.Length == 2, "One operation uses only two aggregate particle systems");
        ParticleSystem heads = systems.First(s => s.name.Contains("Attached")), sparks = systems.First(s => s.name.Contains("Trailing"));
        Material owned = heads.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        Check(heads.main.maxParticles == 64 && sparks.main.maxParticles == 512, "Sixteen-jumper budgets are 64 heads and 512 sparks");
        foreach (var ps in systems)
        {
            Check(ps.main.simulationSpace == ParticleSystemSimulationSpace.Local && !ps.emission.enabled, "Manual local-space emission follows operation origin");
            Check(!ps.collision.enabled && !ps.lights.enabled && !ps.trails.enabled, "No particle collision, lights or trails");
        }
        Check(operation.GetComponentsInChildren<Light>().Length == 0 && operation.GetComponentsInChildren<Rigidbody>().Length == 0, "Visual helper adds no light or rigidbody components");
        var particles = new ParticleSystem.Particle[512];
        Vector3 left = new Vector3(-.4f, 1, 2), right = new Vector3(.4f, 1, 2);
        effect.BeginFrame(0); effect.Draw(left, right, Vector3.forward * 45, 0, true); effect.EndFrame();
        Check(heads.GetParticles(particles) == 4 && sparks.particleCount == 2, "One jumper emits two attached core/halo pairs and one spark pair");
        var streams = new List<ParticleSystemVertexStream>();
        heads.GetComponent<ParticleSystemRenderer>().GetActiveVertexStreams(streams);
        Check(streams.Contains(ParticleSystemVertexStream.Color), "Production particle renderer passes emitted colour to the shader");
        Color32[] current = Enumerable.Range(0, 4).Select(i => particles[i].GetCurrentColor(heads)).ToArray();
        Check(current.Any(c => c.r > 200 && c.g > 40 && c.g < 180 && c.b < 64 && c.a >= 25 && c.a < 180), "Evaluated halo particle colour remains translucent warm amber");
        Check(current.Any(c => c.r > 230 && c.g > 200 && c.b > 140 && c.a > 230), "Evaluated core particle colour remains near white");
        log.AppendLine("Material " + owned.shader.name + " | base " + owned.GetColor("_BaseColor") + " | blend " + owned.GetFloat("_SrcBlend") + "/" + owned.GetFloat("_DstBlend") + " | streams " + string.Join(",", streams));
        log.AppendLine("Actual head RGBA " + string.Join(" / ", current.Select(c => c.ToString())));
        Check(AtAnchors(heads, particles, left, right), "Attached particle centres coincide with both requested world anchors");
        left += Vector3.forward * 20; right += Vector3.forward * 20;
        effect.BeginFrame(.001f); effect.Draw(left, right, Vector3.forward * 45, .001f, true); effect.EndFrame();
        Check(heads.GetParticles(particles) == 4 && AtAnchors(heads, particles, left, right), "Attached heads follow high-speed repositioning without old head particles");
        Check(sparks.particleCount == 2, "Spark rate limiter skips a frame inside its 40 ms interval");
        Vector3 sim = particles[0].position, world = heads.transform.TransformPoint(sim), shift = new Vector3(1200, -300, 900);
        operation.position += shift; heads.GetParticles(particles);
        Check((particles[0].position - sim).sqrMagnitude < .000001f && (heads.transform.TransformPoint(sim) - world - shift).sqrMagnitude < .0001f,
            "Origin shift carries heads without changing their local simulation positions");
        operation.position = Vector3.zero;
        effect.BeginFrame(.04f); effect.Draw(left, right, Vector3.forward * 45, .04f, false); effect.EndFrame();
        Check(heads.particleCount == 0 && sparks.particleCount == 2, "Burn-off removes attached heads immediately and lets old sparks drain");
        sparks.Simulate(1, false, false, false);
        Check(sparks.particleCount == 0, "Trailing sparks expire after burn-off");
        int peak = 0, headPeak = 0; bool budgetHeld = true;
        for (int frame = 0; frame < 120; frame++)
        {
            float time = 2 + frame * .04f; sparks.Simulate(.04f, false, false, false); effect.BeginFrame(time);
            for (int j = 0; j < 16; j++) effect.Draw(left + Vector3.right * j * 3, right + Vector3.right * j * 3, Vector3.forward * 45, time, true);
            effect.EndFrame();
            peak = Mathf.Max(peak, sparks.particleCount);
            headPeak = Mathf.Max(headPeak, heads.particleCount);
            budgetHeld &= heads.particleCount == 64 && sparks.particleCount <= 512;
        }
        Check(budgetHeld && headPeak == 64 && peak > 100, "Repeated full-stick release stays bounded at 64 heads and 512 sparks");
        log.AppendLine("Full-stick spark peak " + peak);
        effect.Clear(); yield return null;
        Check(owned == null && operation.GetComponentsInChildren<ParticleSystem>().Length == 0, "Clear releases owned material and particle root");
        Check(source != null && source.GetColor("_BaseColor") == sourceTint, "Shared production GlowFx.Glow remains unchanged");

        tips = JsonUtility.FromJson<Tips>(Resources.Load<TextAsset>("HaloTips").text);
        Check((tips.left - HaloFlareVisuals.TrackLeftNozzle).sqrMagnitude < .000000000000001f &&
            (tips.right - HaloFlareVisuals.TrackRightNozzle).sqrMagnitude < .000000000000001f,
            "Production Track anchors match measured six-vertex cap centres of exact imported FBX");
        for (int i = 0; i < 16; i++)
        {
            var body = Instantiate(Resources.Load<GameObject>("AirborneJumper"), operation).transform; bodies.Add(body);
            var canopy = Instantiate(Resources.Load<GameObject>("AirborneCanopy"), body).transform; canopies.Add(canopy); canopy.gameObject.SetActive(false);
        }
        plan = HaloGlidePlan.Build(new Vector3(0, 3000, 0), new Vector3(0, 0, 100), new Vector3(1500, 0, 3000), p => 0, 16);
        effect = new HaloFlareVisuals(operation, source);
        sun = new GameObject("Daylight").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.4f; sun.transform.rotation = Quaternion.Euler(40, 150, 0);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.25f, .28f, .32f);
        cameraEye = new GameObject("HALO preview camera").AddComponent<Camera>(); cameraEye.clearFlags = CameraClearFlags.SolidColor;
        cameraEye.backgroundColor = new Color(.15f, .27f, .4f); cameraEye.fieldOfView = 42; cameraEye.nearClipPlane = .05f; cameraEye.farClipPlane = 2000;
        target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGBHalf) { antiAliasing = 4 }; target.Create(); cameraEye.targetTexture = target;
        displayReadback = new RenderTexture(1600, 900, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); displayReadback.Create();
        var cameraData = cameraEye.GetUniversalAdditionalCameraData(); cameraData.requiresColorTexture = false; cameraData.renderPostProcessing = true;
        var volume = new GameObject("Substitute ACES display").AddComponent<Volume>(); volume.isGlobal = true; volume.sharedProfile = Resources.Load<VolumeProfile>("HaloDisplay");
        Tonemapping configuredTone;
        Check(volume.sharedProfile != null && volume.sharedProfile.TryGet(out configuredTone) && configuredTone.mode.value == TonemappingMode.ACES,
            "Substitute display profile contains enabled ACES tonemapping");
        Check(Resources.Load<UniversalRendererData>("HaloRenderer").postProcessData != null, "URP renderer includes the actual post-process shader resources");
        View(new Vector3(3, 1.5f, -6), Vector3.zero);
        show = true; burning = false; yield return new WaitForEndOfFrame(); ReadBaseline();
        burning = true; yield return new WaitForSeconds(.35f); yield return new WaitForEndOfFrame();
        Check(Save("halo-track-close-day.png") > 50, "Actual Track jumper renders luminous flare pixels above burn-off baseline in daylight");
        Check(VolumeManager.instance.stack.GetComponent<Tonemapping>().mode.value == TonemappingMode.ACES, "Actual preview camera uses the ACES volume stack");
        var sparkRenderer = operation.GetComponentsInChildren<ParticleSystem>().First(p => p.name.Contains("Trailing")).GetComponent<ParticleSystemRenderer>();
        sparkRenderer.enabled = false;
        View(new Vector3(2.4f, .15f, -.9f), new Vector3(.2f, -.12f, -.6f));
        burning = false; yield return new WaitForEndOfFrame(); ReadBaseline();
        burning = true; yield return new WaitForEndOfFrame();
        Check(Save("halo-ankle-heads-only-oblique-day.png") > 50, "Heads-only oblique view renders attached burn above its burn-off baseline");
        var bodySkin = bodies[0].GetComponentInChildren<SkinnedMeshRenderer>();
        Check(GainNear(bodySkin.transform.TransformPoint(tips.left)) > 20 && GainNear(bodySkin.transform.TransformPoint(tips.right)) > 20,
            "Both projected ankle anchors contain luminous attached-source pixels with sparks hidden");
        Check(lastPixels.Count(c => c.r > 110 && c.r > c.g * 1.35f && c.r > c.b * 2) > 50, "Attached-source render contains visibly orange halo pixels");
        sparkRenderer.enabled = true;
        View(new Vector3(3, 1.5f, -6), Vector3.zero);
        sun.enabled = false; RenderSettings.ambientLight = Color.black; RenderSettings.ambientProbe = new SphericalHarmonicsL2(); RenderSettings.reflectionIntensity = 0;
        cameraEye.backgroundColor = new Color(.006f, .01f, .018f);
        burning = false; yield return new WaitForSeconds(.75f); yield return new WaitForEndOfFrame(); ReadBaseline();
        burning = true; yield return new WaitForSeconds(.35f); yield return new WaitForEndOfFrame();
        Check(Save("halo-track-close-night.png") > 50, "Actual Track jumper retains luminous attached cores at night");
        formation = true;
        Vector3 centre = Vector3.zero; for (int i = 0; i < 16; i++) centre += plan.Sample(i, 20).Position; centre /= 16;
        Vector3 first = plan.Sample(0, 20).Position;
        Bounds formationBounds = new Bounds(centre - first, Vector3.zero); for (int i = 0; i < 16; i++) formationBounds.Encapsulate(plan.Sample(i, 20).Position - first);
        View(formationBounds.center + new Vector3(.25f, .24f, -1).normalized * Mathf.Max(28, formationBounds.size.magnitude * 1.15f), formationBounds.center);
        burning = false; yield return new WaitForSeconds(.75f); yield return new WaitForEndOfFrame(); ReadBaseline();
        burning = true; yield return new WaitForSeconds(.35f); yield return new WaitForEndOfFrame();
        Check(Save("halo-formation-night.png") > 20, "Production sixteen-jumper glide formation renders visible burn points");
        sun.enabled = true; RenderSettings.ambientLight = new Color(.25f, .28f, .32f); RenderSettings.reflectionIntensity = 1;
        cameraEye.backgroundColor = new Color(.15f, .27f, .4f);
        burning = false; yield return new WaitForSeconds(.75f); yield return new WaitForEndOfFrame(); ReadBaseline();
        burning = true; yield return new WaitForSeconds(.35f); yield return new WaitForEndOfFrame(); Save("halo-formation-day.png");
        formation = false; burning = false; deployed = true;
        View(new Vector3(7, 3, -14), new Vector3(0, 2, 0));
        yield return new WaitForSeconds(.75f); yield return new WaitForEndOfFrame();
        baseline = null;
        Save("halo-canopy-burn-off.png");
        Check(operation.GetComponentsInChildren<ParticleSystem>().All(p => p.particleCount == 0), "Canopy deployment preview has no attached burn or lingering sparks");
    }

    private void LateUpdate()
    {
        if (show)
        {
            Vector3 leadVelocity = formation ? (plan.Sample(0, 20.05f).Position - plan.Sample(0, 20).Position) / .05f : new Vector3(0, -18, 45);
            if (!deployed) motion += leadVelocity * Time.deltaTime;
            effect.BeginFrame(Time.time);
            Vector3 first = plan.Sample(0, 20).Position;
            for (int i = 0; i < bodies.Count; i++)
            {
                Transform body = bodies[i]; body.gameObject.SetActive(formation || i == 0); if (!body.gameObject.activeSelf) continue;
                HaloSample sample = plan.Sample(i, 20);
                body.position = (formation ? sample.Position - first : Vector3.zero) + motion;
                body.rotation = formation ? Quaternion.LookRotation(sample.Heading, Vector3.up) * Quaternion.Euler(sample.Pitch, 0, 0) : Quaternion.Euler(deployed ? 0 : 80, 0, 0);
                var skin = body.GetComponentInChildren<SkinnedMeshRenderer>();
                for (int s = 0; s < skin.sharedMesh.blendShapeCount; s++) skin.SetBlendShapeWeight(s, skin.sharedMesh.GetBlendShapeName(s) == (deployed ? "Hang" : "Track") ? 100 : 0);
                canopies[i].gameObject.SetActive(deployed);
                Vector3 velocity = formation ? (plan.Sample(i, 20.05f).Position - sample.Position) / .05f : leadVelocity;
                effect.Draw(skin.transform.TransformPoint(tips.left), skin.transform.TransformPoint(tips.right), velocity, Time.time, burning && (!formation || sample.Smoke));
            }
            effect.EndFrame();
            cameraEye.transform.position = viewFrom + motion; cameraEye.transform.LookAt(viewAt + motion);
        }
    }

    private bool AtAnchors(ParticleSystem ps, ParticleSystem.Particle[] values, Vector3 left, Vector3 right)
    {
        for (int i = 0; i < ps.particleCount; i++)
        {
            Vector3 world = ps.transform.TransformPoint(values[i].position);
            if (Mathf.Min((world - left).sqrMagnitude, (world - right).sqrMagnitude) > .000001f) return false;
        }
        return true;
    }

    private int Save(string name)
    {
        Texture2D image = Read(); Color32[] pixels = image.GetPixels32(); lastPixels = pixels; int glow = 0;
        var head = operation.GetComponentsInChildren<ParticleSystem>().First(p => p.name.Contains("Attached"));
        var renderer = head.GetComponent<ParticleSystemRenderer>(); var sample = new ParticleSystem.Particle[64]; int count = head.GetParticles(sample);
        log.AppendLine("Head diagnostic count=" + count + " mode=" + renderer.renderMode + " enabled=" + renderer.enabled + " bounds=" + renderer.bounds +
            " screenMax=" + renderer.maxParticleSize + " particleSize=" + (count > 0 ? sample[0].GetCurrentSize(head).ToString("F3") : "none") +
            " firstWorld=" + (count > 0 ? head.transform.TransformPoint(sample[0].position).ToString("F3") : "none") + " camera=" + cameraEye.transform.position.ToString("F3"));
        var baked = new Mesh(); renderer.BakeMesh(baked, cameraEye, true);
        log.AppendLine("Baked head vertex colours " + string.Join(" / ", baked.colors32.Distinct().Select(c => c.ToString()).Take(8)) + " | keywords " + string.Join(",", renderer.sharedMaterial.shaderKeywords));
        Destroy(baked);
        if (baseline != null)
            for (int i = 0; i < pixels.Length; i++) if (pixels[i].r > baseline[i].r + 20 && pixels[i].g > baseline[i].g + 10) glow++;
        File.WriteAllBytes(Path.Combine(output, name), image.EncodeToPNG()); Destroy(image);
        log.AppendLine(name + " | luminous pixels above identical burn-off baseline " + glow); return glow;
    }

    private Texture2D Read()
    {
        // Every caller has yielded WaitForEndOfFrame. The actual production
        // EndFrame prepares heads; there is no fixture-only particle flush.
        // StandardRequest updates the volume stack and applies the configured
        // display transform; SingleCameraRequest skips that framework update.
        RenderPipeline.SubmitRenderRequest(cameraEye, new RenderPipeline.StandardRequest { destination = target });
        // HDR blending and tonemapping happen in linear light. Encode the final
        // display copy to sRGB before PNG readback; direct half-float readback
        // stores linear values in an ordinary PNG and makes the preview too dark.
        bool priorSrgb = GL.sRGBWrite; GL.sRGBWrite = true; Graphics.Blit(target, displayReadback); GL.sRGBWrite = priorSrgb;
        RenderTexture prior = RenderTexture.active; RenderTexture.active = displayReadback;
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
        RenderTexture.active = prior; return image;
    }
    private void ReadBaseline() { Texture2D image = Read(); baseline = image.GetPixels32(); Destroy(image); }
    private void View(Vector3 from, Vector3 at) { viewFrom = from; viewAt = at; cameraEye.transform.position = from + motion; cameraEye.transform.LookAt(at + motion); }
    private int GainNear(Vector3 world)
    {
        Vector3 centre = cameraEye.WorldToScreenPoint(world);
        float radius = Vector3.Distance(centre, cameraEye.WorldToScreenPoint(world + cameraEye.transform.up * .7f));
        int count = 0;
        for (int y = Math.Max(0, (int)(centre.y - radius)); y < Math.Min(target.height, centre.y + radius); y++)
            for (int x = Math.Max(0, (int)(centre.x - radius)); x < Math.Min(target.width, centre.x + radius); x++)
            {
                int i = y * target.width + x;
                if (lastPixels[i].r > baseline[i].r + 20 && lastPixels[i].g > baseline[i].g + 10) count++;
            }
        return count;
    }

    private void Check(bool condition, string detail) { if (!condition) throw new Exception(detail); assertions++; log.AppendLine("OK " + detail); }
    private static string Arg(string name) { string[] args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
}

// Compile only the actual production glow recipe; unused world-helper calls are
// bounded stubs so no game assembly or static game bootstrap enters this player.
internal static class Datum { public static Transform origin; }
internal static class NetworkSceneSingleton<T> { public static T i; }
internal static class SceneSingleton<T> { public static T i; }
internal sealed class LevelInfo { public Vector3 GetWind() => Vector3.zero; public float GetAmbientLight() => 1; }
internal sealed class CameraStateManager { public Camera mainCamera; public void ShakeCamera(float a, float b) { } }
#endif
