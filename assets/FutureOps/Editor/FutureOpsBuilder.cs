// Isolated Blueprinter asset-library build. No game registration or donor mutation.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Blueprinter;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class FutureOpsBuilder
{
    static string PackName => Environment.GetEnvironmentVariable("FUTUREOPS_PACK_NAME") ?? "FutureOps";
    static string Root => "Assets/Blueprinter/Mods/"+PackName+"/";
    [Serializable] public class Catalog { public Entry[] assets; }
    [Serializable] public class Entry { public string key, model, rig_model; public Vector3 sizeUnity; public AnchorSpec[] anchors; }
    [Serializable] public class AnchorSpec { public string name; public Vector3 position; }
    static readonly Dictionary<string, Color> Palette = new Dictionary<string, Color> {
        {"Concrete", new Color(.34f,.36f,.33f)}, {"Olive", new Color(.19f,.235f,.14f)},
        {"Steel", new Color(.27f,.31f,.32f)}, {"Dark", new Color(.055f,.075f,.075f)},
        {"Canvas", new Color(.40f,.38f,.25f)}, {"Earth", new Color(.30f,.22f,.13f)},
        {"Safety", new Color(.78f,.55f,.12f)}, {"Glass", new Color(.055f,.16f,.20f)},
        {"Red", new Color(.40f,.13f,.085f)}, {"White", new Color(.68f,.70f,.63f)},
        {"MarineHull",new Color(.055f,.105f,.12f)}, {"MarineDeck",new Color(.105f,.15f,.145f)},
        {"MarineWhite",new Color(.63f,.66f,.63f)}, {"MarineSteel",new Color(.22f,.255f,.26f)},
        {"CargoRed",new Color(.38f,.10f,.065f)}, {"CargoBlue",new Color(.08f,.18f,.22f)},
        {"CargoOlive",new Color(.24f,.28f,.19f)}, {"CargoGrey",new Color(.49f,.52f,.49f)},
        {"Antifoul",new Color(.23f,.075f,.045f)}, {"MarineGlass",new Color(.035f,.09f,.105f)},
        {"MarineSafety",new Color(.70f,.34f,.065f)}, {"MarineBlack",new Color(.025f,.03f,.028f)},
        {"NavPort",new Color(.65f,.018f,.01f)}, {"NavStarboard",new Color(.015f,.5f,.06f)},
        {"SpaceGold",new Color(.48f,.29f,.065f)}, {"SolarBlue",new Color(.015f,.06f,.13f)},
        {"SolarDark",new Color(.012f,.025f,.055f)}, {"ThermalWhite",new Color(.63f,.67f,.65f)},
        {"Medical",new Color(.05f,.32f,.19f)}, {"Wreck",new Color(.09f,.11f,.10f)}
    };
    public static void Run()
    {
        string delivery = Environment.GetEnvironmentVariable("FUTUREOPS_OUT");
        try
        {
            Directory.CreateDirectory(Root + "Materials");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigurePipeline();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new Exception("URP/Lit missing");
            var materials = new Dictionary<string, Material>();
            foreach (var pair in Palette)
            {
                string materialPath = Root + "Materials/FutureOps_" + pair.Key + ".mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                bool create = !mat;
                if (create) mat = new Material(shader) { name = "FutureOps_" + pair.Key };
                mat.shader = shader;
                // Blender authored linear RGB; Unity material colours are stored in sRGB.
                mat.SetColor("_BaseColor", pair.Value.gamma);
                mat.SetFloat("_Metallic", pair.Key == "SpaceGold" ? .75f : pair.Key == "Steel" ? .45f : 0f);
                mat.SetFloat("_Smoothness", pair.Key == "Glass" ? .72f : .22f);
                var albedo=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CargoTextures/"+pair.Key+"_Albedo.png");
                if(albedo) {
                    mat.SetColor("_BaseColor",Color.white); mat.SetTexture("_BaseMap",albedo);
                    var normal=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/CargoTextures/"+(pair.Key.StartsWith("Cargo")?"Cargo":"MarinePaint")+"_Normal.png");
                    mat.SetTexture("_BumpMap",normal); mat.EnableKeyword("_NORMALMAP");
                }
                // Pack-local authored PBR surfaces; keep the existing marine skin separate.
                string surfaceRoot=Root+"Textures/"+pair.Key;
                var surfaceAlbedo=AssetDatabase.LoadAssetAtPath<Texture2D>(surfaceRoot+"_Albedo.png");
                if(surfaceAlbedo && AssetDatabase.LoadAssetAtPath<Texture2D>(surfaceRoot+"_Normal.png") && AssetDatabase.LoadAssetAtPath<Texture2D>(surfaceRoot+"_MetalGloss.png")) {
                    var normalImporter=(TextureImporter)AssetImporter.GetAtPath(surfaceRoot+"_Normal.png");
                    normalImporter.textureType=TextureImporterType.NormalMap; normalImporter.sRGBTexture=false;
                    normalImporter.wrapMode=TextureWrapMode.Repeat; normalImporter.SaveAndReimport();
                    var glossImporter=(TextureImporter)AssetImporter.GetAtPath(surfaceRoot+"_MetalGloss.png");
                    glossImporter.sRGBTexture=false; glossImporter.wrapMode=TextureWrapMode.Repeat; glossImporter.SaveAndReimport();
                    mat.SetColor("_BaseColor",Color.white); mat.SetTexture("_BaseMap",surfaceAlbedo);
                    mat.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(surfaceRoot+"_Normal.png"));
                    mat.SetFloat("_BumpScale",.45f); mat.EnableKeyword("_NORMALMAP");
                    mat.SetTexture("_MetallicGlossMap",AssetDatabase.LoadAssetAtPath<Texture2D>(surfaceRoot+"_MetalGloss.png"));
                    mat.SetFloat("_Smoothness",1); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                if (create) AssetDatabase.CreateAsset(mat, materialPath);
                else EditorUtility.SetDirty(mat);
                materials.Add(pair.Key, mat);
            }
            var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText(Root + "catalog.json"));
            var report = new List<string>();
            foreach (var entry in catalog.assets)
            {
                string path = "Assets/SourceModels/" + entry.model + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.globalScale = 1; importer.useFileUnits = false; importer.useFileScale = true;
                importer.bakeAxisConversion = true;
                importer.importNormals = ModelImporterNormals.Import;
                importer.importTangents = ModelImporterTangents.CalculateMikk;
                importer.importAnimation = false;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                importer.SaveAndReimport();
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var root = UnityEngine.Object.Instantiate(source); root.name = entry.key;
                root.transform.position = Vector3.zero;
                var lods = new List<LOD>();
                for (int level = 0; level < 3; level++)
                {
                    var child = root.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == "LOD" + level);
                    child.gameObject.SetActive(true);
                    var renderer = child.GetComponent<MeshRenderer>(); renderer.enabled = true;
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(m => {
                        string name = m.name.Replace(" (Instance)", "");
                        if (!materials.ContainsKey(name)) throw new Exception("Unknown material: " + name);
                        return materials[name];
                    }).ToArray();
                    lods.Add(new LOD(new[] {.3f,.12f,.015f}[level], new Renderer[] {renderer}));
                }
                var colliderSource = root.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == "COL_Static");
                colliderSource.gameObject.SetActive(true);
                UnityEngine.Object.DestroyImmediate(colliderSource.GetComponent<MeshRenderer>());
                var collider = colliderSource.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = colliderSource.sharedMesh; collider.convex = false;
                var mainRenderer = lods[0].renderers[0];
                if (entry.sizeUnity != Vector3.zero && (mainRenderer.bounds.size - entry.sizeUnity).magnitude > .05f)
                    throw new Exception("Import scale/axis mismatch " + entry.key + ": " + mainRenderer.bounds.size + " expected " + entry.sizeUnity);
                if (entry.model == "TrenchStraight" || entry.model == "TrenchCorner")
                {
                    Physics.SyncTransforms();
                    // Rays along each clear corridor must reach the join without hitting a wall.
                    RaycastHit hit;
                    if (Physics.Raycast(new Vector3(0,1,-3.8f),Vector3.forward,out hit,entry.model == "TrenchStraight" ? 7.6f : 3.8f))
                        throw new Exception("Trench passage obstructed " + entry.key + " by " + hit.collider.name);
                    if (entry.model == "TrenchCorner" && Physics.Raycast(new Vector3(0,1,0),Vector3.right,out hit,3.8f))
                        throw new Exception("Trench corner outlet obstructed");
                }
                var group = root.GetComponent<LODGroup>();
                if (!group) group = root.AddComponent<LODGroup>();
                group.SetLODs(lods.ToArray()); group.RecalculateBounds();
                AddAnchors(root, entry.model);
                if(entry.anchors!=null) foreach(var anchor in entry.anchors) {
                    var o=new GameObject(anchor.name); o.transform.SetParent(root.transform,false); o.transform.localPosition=anchor.position;
                }
                string prefabPath = Root + entry.key + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                if(!string.IsNullOrEmpty(entry.rig_model)) {
                    string rigPath="Assets/SourceModels/"+entry.rig_model+".fbx";
                    var rigImporter=(ModelImporter)AssetImporter.GetAtPath(rigPath);
                    rigImporter.globalScale=1; rigImporter.useFileUnits=false; rigImporter.useFileScale=true; rigImporter.bakeAxisConversion=true;
                    rigImporter.importNormals=ModelImporterNormals.Import; rigImporter.materialImportMode=ModelImporterMaterialImportMode.ImportStandard; rigImporter.importAnimation=false;
                    rigImporter.SaveAndReimport();
                    var rig=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(rigPath)); rig.name=entry.key+"_Rig";
                    foreach(var t in rig.GetComponentsInChildren<Transform>(true)) t.gameObject.SetActive(true);
                    foreach(var r in rig.GetComponentsInChildren<MeshRenderer>(true)) {r.enabled=true; r.sharedMaterials=r.sharedMaterials.Select(m=>materials[m.name]).ToArray();}
                    var rigRenderers=rig.GetComponentsInChildren<MeshRenderer>(true);
                    if(rigRenderers.Length>5) throw new Exception("Unmerged satellite rig renderers "+entry.key+": "+rigRenderers.Length);
                    report.Add(entry.key+"_Rig | moving mesh renderers "+rigRenderers.Length+" | material passes "+rigRenderers.Sum(r=>r.sharedMaterials.Length));
                    var rigBounds=rigRenderers[0].bounds; foreach(var r in rigRenderers) rigBounds.Encapsulate(r.bounds);
                    if((rigBounds.size-entry.sizeUnity).magnitude>.1f) throw new Exception("Rig placement differs from static model "+entry.key);
                    PrefabUtility.SaveAsPrefabAsset(rig,Root+rig.name+".prefab"); UnityEngine.Object.DestroyImmediate(rig);
                }
                report.Add(entry.key + " | LOD tris " + string.Join("/", lods.Select(l =>
                    l.renderers[0].GetComponent<MeshFilter>().sharedMesh.triangles.Length/3)) +
                    " | static collider tris " + collider.sharedMesh.triangles.Length/3);
                UnityEngine.Object.DestroyImmediate(root);
            }
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory(delivery);
            var started = DateTime.UtcNow;
            ModBuilder.Build(PackName, PackName, "0.1.0", delivery);
            string built = Path.Combine(delivery, PackName+"_0.1.0.nobp");
            if (!File.Exists(built) || File.GetLastWriteTimeUtc(built) < started.AddSeconds(-1))
                throw new Exception("Blueprinter did not produce a fresh bundle");
            File.Copy(built, Path.Combine(delivery, PackName+".nobp"), true);
            // Reopen the real shipped bundle and validate the serialized assets.
            var bundle = AssetBundle.LoadFromFile(Path.Combine(delivery, PackName+".nobp"));
            if (!bundle) throw new Exception("Cannot reopen exported bundle");
            foreach (var entry in catalog.assets)
            {
                var prefab = bundle.LoadAsset<GameObject>(Root + entry.key + ".prefab");
                if (!prefab || !prefab.GetComponent<LODGroup>() || !prefab.GetComponentInChildren<MeshCollider>())
                    throw new Exception("Incomplete bundled prefab " + entry.key);
                if (prefab.GetComponentsInChildren<Component>(true).Any(c => !c))
                    throw new Exception("Missing component in " + entry.key);
                if(entry.anchors!=null) foreach(var anchor in entry.anchors) if(!prefab.transform.Find(anchor.name)) throw new Exception("Missing anchor "+entry.key+"/"+anchor.name);
                if(!string.IsNullOrEmpty(entry.rig_model)) {
                    var rig=bundle.LoadAsset<GameObject>(Root+entry.key+"_Rig.prefab");
                    if(!rig || !rig.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="SolarPort") || !rig.GetComponentsInChildren<Transform>(true).Any(t=>t.name=="SolarStarboard")) throw new Exception("Satellite hinges missing "+entry.key);
                }
                foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.sharedMaterials.Any(m => !m || !m.shader || m.shader.name != "Universal Render Pipeline/Lit"))
                        throw new Exception("Invalid bundled material " + entry.key);
                RenderBundledPrefab(prefab, entry.model, delivery);
            }
            var manifest = bundle.LoadAsset<TextAsset>("Assets/Blueprinter/Generated/patch_manifest.json");
            if (!manifest || !manifest.text.Contains("\"schemaVersion\": 3")) throw new Exception("Blueprinter manifest missing");
            bundle.Unload(true);
            File.WriteAllLines(Path.Combine(delivery, "validation.txt"), new[] {
                "PASS: Blueprinter 0.1.0 bundle reopened and rendered; " + catalog.assets.Length + " main prefabs; verified metre scale and anchors; 3 LODs each; static colliders; URP materials; no missing scripts or magenta pixels. Articulated rigs checked when present.",
                "Asset library only: no BuildingDefinition, ship simulation, damage logic, network identity or registration operations.",
                "Offline evidence only; game loading and placement remain unverified."
            }.Concat(report));
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (!string.IsNullOrEmpty(delivery)) { Directory.CreateDirectory(delivery); File.WriteAllText(Path.Combine(delivery,"failure.txt"), e.ToString()); }
            EditorApplication.Exit(1);
        }
    }
    static void ConfigurePipeline()
    {
        PlayerSettings.colorSpace = ColorSpace.Linear;
        const string settings = "Assets/FutureOpsPreviewSettings/";
        Directory.CreateDirectory(settings); AssetDatabase.Refresh();
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(settings + "Renderer.asset");
        if (!renderer) { renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(renderer, settings + "Renderer.asset"); }
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(settings + "Pipeline.asset");
        if (!pipeline) { pipeline = UniversalRenderPipelineAsset.Create(renderer); AssetDatabase.CreateAsset(pipeline, settings + "Pipeline.asset"); }
        var serialized = new SerializedObject(pipeline);
        serialized.FindProperty("m_MainLightShadowsSupported").boolValue = true;
        serialized.FindProperty("m_SoftShadowsSupported").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        GraphicsSettings.renderPipelineAsset = pipeline; QualitySettings.renderPipeline = pipeline;
        var global = GraphicsSettings.GetSettingsForRenderPipeline<UniversalRenderPipeline>();
        if (!global) {
            var type = typeof(UniversalRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings", true);
            global = (RenderPipelineGlobalSettings)ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(global, settings + "Global.asset");
            GraphicsSettings.RegisterRenderPipelineSettings<UniversalRenderPipeline>(global);
        }
        // Retain the configured URP lighting/shadow variants without shipping every keyword combination.
        var globalSettings = new SerializedObject(global);
        globalSettings.FindProperty("m_StripUnusedVariants").boolValue = true;
        globalSettings.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
    }
    static void RenderBundledPrefab(GameObject prefab, string model, string delivery)
    {
        var instance = UnityEngine.Object.Instantiate(prefab);
        instance.GetComponent<LODGroup>().ForceLOD(0);
        var renderers = instance.GetComponentsInChildren<MeshRenderer>();
        var bounds = renderers.First().bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        var cameraObject = new GameObject("BundleCheckCamera"); var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true; camera.aspect = 4f/3f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.12f,.15f,.18f);
        camera.nearClipPlane = .01f; camera.farClipPlane = 1000;
        camera.transform.position = bounds.center + new Vector3(1.15f,1.15f,-1.5f).normalized * bounds.size.magnitude * 2;
        camera.transform.LookAt(bounds.center);
        float fit = 0;
        for (int x=-1;x<=1;x+=2) for (int y=-1;y<=1;y+=2) for (int z=-1;z<=1;z+=2) {
            var p = camera.transform.InverseTransformPoint(bounds.center + Vector3.Scale(bounds.extents,new Vector3(x,y,z)));
            fit = Mathf.Max(fit,Mathf.Abs(p.y),Mathf.Abs(p.x)/camera.aspect);
        }
        camera.orthographicSize = fit*1.1f;
        var sunObject = new GameObject("BundleCheckSun"); var sun = sunObject.AddComponent<Light>();
        sun.type = LightType.Directional; sun.intensity = 1.6f; sun.transform.rotation = Quaternion.Euler(45,-35,0);
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.4f,.44f,.48f);
        var target = new RenderTexture(800,600,24); target.Create(); camera.targetTexture = target;
        camera.Render();
        var old = RenderTexture.active; RenderTexture.active = target;
        var frame = new Texture2D(800,600,TextureFormat.RGB24,false);
        frame.ReadPixels(new Rect(0,0,800,600),0,0); frame.Apply();
        int magenta=0,content=0;
        var background = frame.GetPixel(0,0);
        foreach (var c in frame.GetPixels()) {
            if (c.r>.8f && c.b>.8f && c.g<.2f) magenta++;
            if (Mathf.Abs(c.r-background.r)+Mathf.Abs(c.g-background.g)+Mathf.Abs(c.b-background.b)>.08f) content++;
        }
        string previews = Path.Combine(delivery,"Previews","Unity"); Directory.CreateDirectory(previews);
        File.WriteAllBytes(Path.Combine(previews,model+".png"),frame.EncodeToPNG());
        RenderTexture.active = old; camera.targetTexture=null;
        target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(frame);
        UnityEngine.Object.DestroyImmediate(cameraObject); UnityEngine.Object.DestroyImmediate(sunObject); UnityEngine.Object.DestroyImmediate(instance);
        if (magenta>0 || content<1000) throw new Exception("Bundled render failed " + model + ": magenta="+magenta+", content="+content);
        Debug.Log("[FutureOps] BUNDLED_RENDER_OK " + model + " pixels=" + content);
    }
    static void AddAnchors(GameObject root, string model)
    {
        Action<string, Vector3> add = (name, pos) => {
            var anchor = new GameObject(name); anchor.transform.SetParent(root.transform, false); anchor.transform.localPosition = pos;
        };
        add("GroundOrigin", Vector3.zero);
        if (model == "LaunchSite") add("LaunchPad", new Vector3(0,1.1f,7));
        if (model == "CargoShip") { add("Waterline", Vector3.zero); add("CargoDeck", new Vector3(0,5.2f,0)); }
        if (model == "Helipad") add("LandingPoint", new Vector3(0,.43f,0));
        if (model == "TrenchStraight") { add("JoinStart", new Vector3(0,0,-4)); add("JoinEnd", new Vector3(0,0,4)); }
        if (model == "TrenchCorner") { add("JoinStart", new Vector3(0,0,-4)); add("JoinEnd", new Vector3(4,0,0)); }
        if (model == "RadarPost") add("SensorOrigin", new Vector3(0,8,3));
    }
}
