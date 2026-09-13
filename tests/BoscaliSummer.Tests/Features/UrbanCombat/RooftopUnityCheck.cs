#if UNITY_5_3_OR_NEWER
// Run with Run-RooftopUnityCheck.ps1. Uses real Unity physics and the production
// placement/mesh code; game API stubs deliberately do not simulate networking or AI.
using System;
using System.Collections.Generic;
using System.IO;
using BoscaliSummer.Garrisons;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class Building : MonoBehaviour
{
    public bool disabled;
    public string NetworkUniqueName;
    public FactionHQ NetworkHQ;
    public BuildingDefinition definition;
}
public sealed class FactionHQ { public Faction faction = new Faction(); }
public sealed class Faction { public Color color = Color.blue; }
public sealed class BuildingDefinition
{
    public float width = 4f, length = 4f;
    public BuildingType buildingType;
    public GameObject unitPrefab;
    public string jsonKey;
}
public enum BuildingType { DEF }
public static class PhysicsLayers { public static int StaticsMask = ~0; }
public sealed class Encyclopedia
{
    public static Encyclopedia i;
    public List<BuildingDefinition> buildings;
}
namespace BoscaliSummer.Garrisons
{
    internal static class ZoneGarrisonManager { internal const string NamePrefix = "BoscaliSummer:Garrison:"; }
}

public static class RooftopUnityCheck
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
    private static GameObject Roof(Vector3 size, Vector3 angles)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.localScale = size;
        go.transform.rotation = Quaternion.Euler(angles);
        Physics.SyncTransforms();
        return go;
    }
    private static bool Fits(GameObject go, BuildingDefinition definition, out Vector3 position) =>
        RooftopPlacement.TryPlace(go, go.GetComponent<Renderer>().bounds, definition, out position, out _);

#if UNITY_EDITOR
    public static void BuildPlayer()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
        // Retain the shader used by the game; an empty test scene otherwise strips it.
        var shaderReference = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shaderReference.transform.position = Vector3.one * 100000f;
        var material = new Material(Shader.Find("Standard"));
        AssetDatabase.CreateAsset(material, "Assets/CheckMaterial.mat");
        shaderReference.GetComponent<Renderer>().sharedMaterial = material;
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, "Assets/Check.unity");
        var result = BuildPipeline.BuildPlayer(new[] { "Assets/Check.unity" }, "Player/RooftopCheck.exe",
            BuildTarget.StandaloneWindows64, BuildOptions.None);
        EditorApplication.Exit(result.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    public static void Run()
    {
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) RunChecks();
        };
        EditorApplication.EnterPlaymode();
    }
#else
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Run() => RunChecks();
#endif

    private static void RunChecks()
    {
        try
        {
            var definition = new BuildingDefinition();
            GameObject roof = Roof(new Vector3(30, 10, 30), new Vector3(0, 37, 0));
            Check(Fits(roof, definition, out Vector3 p) && Mathf.Abs(p.y - 5.03f) < 0.01f, "Rotated flat roof must fit at roof height");
            Object.DestroyImmediate(roof);
            roof = Roof(new Vector3(3, 10, 3), Vector3.zero);
            Check(!Fits(roof, definition, out _), "Small roof must reject an overhanging footprint");
            var neighbor = Roof(new Vector3(30, 8, 30), Vector3.zero);
            Check(!Fits(roof, definition, out _), "Neighbour roof must not satisfy support probes");
            Object.DestroyImmediate(neighbor); Object.DestroyImmediate(roof);
            roof = Roof(new Vector3(30, 2, 30), new Vector3(20, 0, 0));
            Check(!Fits(roof, definition, out _), "Sloped roof must fail");
            Object.DestroyImmediate(roof);
            roof = Roof(new Vector3(30, 10, 30), Vector3.zero);
            var obstacle = Roof(new Vector3(32, 2, 32), Vector3.zero);
            obstacle.transform.position = Vector3.up * 6f; Physics.SyncTransforms();
            Check(!Fits(roof, definition, out _), "Covered roof must reject unrelated obstruction");
            Object.DestroyImmediate(obstacle);

            Object.DestroyImmediate(roof);
            roof = Roof(new Vector3(30, 10, 30), Vector3.zero);
            roof.GetComponent<BoxCollider>().size = new Vector3(1f, 0.1f, 1f);
            Check(Fits(roof, definition, out p) && Mathf.Abs(p.y - 5.03f) < .01f,
                "Short collision proxy must not bury the nest below the visible mesh roof");
            roof.GetComponent<Renderer>().enabled = false;
            Check(Fits(roof, definition, out p), "Culled renderer must retain geometric roof support");
            roof.GetComponent<Renderer>().enabled = true;
            Mesh unreadable = Object.Instantiate(roof.GetComponent<MeshFilter>().sharedMesh);
            unreadable.UploadMeshData(true);
            roof.GetComponent<MeshFilter>().sharedMesh = unreadable;
            Check(Fits(roof, definition, out p) && Mathf.Abs(p.y - 5.03f) < .01f,
                "Non-readable meshes must use box support lifted to visible roof height");
            Check(roof.GetComponent<BoxCollider>().enabled && roof.GetComponent<BoxCollider>().size.y == .1f,
                "Placement must preserve the original collider");
            foreach (var probe in roof.GetComponentsInChildren<MeshCollider>(true))
                Check(probe.sharedMesh != unreadable, "Never cook a non-readable mesh, even in Editor");
            Object.DestroyImmediate(roof.GetComponent<BoxCollider>());
            Check(!Fits(roof, definition, out _), "Unreadable geometry without native support must fail safely");
            Object.DestroyImmediate(roof); Object.DestroyImmediate(unreadable);
            roof = Roof(new Vector3(40, 10, 40), Vector3.zero);
            var tower = Roof(new Vector3(12, 20, 12), Vector3.zero);
            tower.transform.position = new Vector3(5, 15, 5);
            tower.transform.SetParent(roof.transform, true);
            Bounds tiers = roof.GetComponent<Renderer>().bounds;
            tiers.Encapsulate(tower.GetComponent<Renderer>().bounds);
            Check(RooftopPlacement.TryPlace(roof, tiers, definition, out p, out _) && Mathf.Abs(p.y - 25.03f) < .01f,
                "Highest supported tower roof must win over lower podium candidates");
            foreach (var collider in roof.GetComponentsInChildren<MeshCollider>(true))
                Check(!collider.gameObject.activeSelf, "Temporary mesh probes must be inactive immediately");
            Object.DestroyImmediate(roof);
            roof = Roof(new Vector3(30, 10, 30), Vector3.zero);

            Object.DestroyImmediate(roof);
#if UNITY_EDITOR
            foreach (string nativeName in new[] { "commercial_2a", "commercial_2c", "commercial_3b", "midrise_1d" })
            {
                var nativeAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/" + nativeName + ".obj");
                if (nativeAsset == null) continue;
                var native = Object.Instantiate(nativeAsset);
                // OBJ imports contain no colliders. Match the game's box-backed props
                // instead of relying on Editor-only cooking of unreadable imports.
                foreach (var filter in native.GetComponentsInChildren<MeshFilter>())
                {
                    var support = filter.gameObject.AddComponent<BoxCollider>();
                    support.center = filter.sharedMesh.bounds.center;
                    support.size = filter.sharedMesh.bounds.size;
                }
                Renderer[] surfaces = native.GetComponentsInChildren<Renderer>();
                Bounds nativeBounds = surfaces[0].bounds;
                foreach (Renderer surface in surfaces) nativeBounds.Encapsulate(surface.bounds);
                Check(RooftopPlacement.TryPlace(native, nativeBounds, definition, out p, out _) && p.y >= nativeBounds.max.y - 5f,
                    "Native " + nativeName + " must have a supported position on its upper roof");
                Debug.Log("Native roof fixture passed: " + nativeName + " at " + p);
                Object.DestroyImmediate(native);
            }
#endif
            roof = Roof(new Vector3(30, 10, 30), Vector3.zero);

            var unit = new GameObject("NativeEmplacementFixture");
            unit.transform.position = Vector3.up * 5.03f;
            var building = unit.AddComponent<Building>();
            building.definition = definition;
            building.NetworkHQ = new FactionHQ();
            building.NetworkHQ.faction.color = new Color(0.16f, 0.40f, 0.75f);
            building.NetworkUniqueName = RooftopPlacement.NamePrefix + "Fixture";
            var dugout = new GameObject("dugout"); dugout.transform.SetParent(unit.transform, false);
            var grass = new GameObject("GrassBlocker_Proxy Height (2)"); grass.transform.SetParent(unit.transform, false);
            GarrisonVisual.Apply(building);
            Check(!dugout.activeSelf && !grass.activeSelf, "Terrain-only pieces must be inactive on roofs");
            var marking = unit.GetComponent<OccupiedBuildingMarking>();
            Check(marking != null, "Rooftop network prefix must create decoration");
            GarrisonVisual.Apply(building);
            Check(unit.GetComponentsInChildren<MeshRenderer>().Length == 4, "Repeated client/server setup must not duplicate decoration");
            Check(unit.GetComponentsInChildren<Collider>().Length == 0, "Cosmetics must not add combat colliders");
            int vertices = 0;
            foreach (var mf in unit.GetComponentsInChildren<MeshFilter>())
            {
                vertices += mf.sharedMesh.vertexCount;
                foreach (var normal in mf.sharedMesh.normals)
                    Check(!float.IsNaN(normal.x), "Mesh normals must be finite");
            }
            Check(vertices < 3000, "Decoration vertex budget");

            // Optional locally extracted native meshes improve the illustration, but
            // no game assets are checked in or required to run the physics assertions.
#if UNITY_EDITOR
            foreach (string name in new[] { "emplacement1_MG_base", "emplacement1_MG_traverse", "emplacement1_MG_barrel", "emplacement1_MG_crew1" })
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/" + name + ".obj");
                if (asset == null) continue;
                var model = Object.Instantiate(asset, unit.transform);
                model.transform.localPosition = name.EndsWith("barrel") ? new Vector3(0, .8428f, .0007f) :
                    name.EndsWith("crew1") ? new Vector3(0, 0, .2327f) : Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;
                foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                    renderer.sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(.25f, .29f, .24f) };
            }
#endif
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.55f, .58f, .65f);
            var light = new GameObject("Sun").AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.15f;
            light.transform.rotation = Quaternion.Euler(45, -30, 0);
            var camera = new GameObject("PreviewCamera").AddComponent<Camera>();
            camera.backgroundColor = new Color(.12f, .16f, .21f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.transform.position = new Vector3(12, 14, 15);
            camera.transform.LookAt(new Vector3(0, 6.5f, 0));
            camera.fieldOfView = 42;
            Render(camera, "roof-front.png");
            camera.transform.position = new Vector3(-12, 13, -14);
            camera.transform.LookAt(new Vector3(0, 6.5f, 0));
            Render(camera, "roof-back.png");
            marking.CleanUp();
            Check(!unit.transform.Find("BoscaliSummer.OccupiedRoof").gameObject.activeSelf, "Cleanup hides decoration immediately");
            File.WriteAllText(Path.Combine(Application.dataPath, "../results.txt"), "PASS: " + checks + " assertions; actual Unity physics, production placement and decoration meshes. Networking, weapon AI and in-game lighting not exercised.");
            Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); Exit(1); }
    }
    private static void Exit(int code)
    {
#if UNITY_EDITOR
        EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
    private static void Render(Camera camera, string name)
    {
        var rt = new RenderTexture(1200, 900, 24);
        camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
        var texture = new Texture2D(1200, 900, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 1200, 900), 0, 0); texture.Apply();
        File.WriteAllBytes(Path.Combine(Application.dataPath, "../" + name), texture.EncodeToPNG());
        RenderTexture.active = null; camera.targetTexture = null;
        Object.DestroyImmediate(texture); Object.DestroyImmediate(rt);
    }
}
#endif
