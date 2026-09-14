#if UNITY_EDITOR
using System;
using System.IO;
using BoscaliSummer.Features.Trenches.Visuals;
using BoscaliSummer.Features.Trenches.Runtime;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class TrenchUnityCheck
{
    public static void Run()
    {
        try
        {
            foreach (Vector3 heading in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            foreach (float side in new[] { -1f, 1f })
            {
                var mesh = TrenchMeshBuilder.BuildEdgeMesh(new[] { Vector3.zero, heading * 30f },
                    2.6f, 2.4f, 1.4f, Vector3.Cross(Vector3.up, heading) * side);
                var v = mesh.vertices;
                var t = mesh.triangles;
                // The dry floor is the cross-section strip between profile points 3 and 4.
                int offset = TrenchMeshBuilder.FloorStrip * 6;
                Vector3 normal = Vector3.Cross(v[t[offset + 1]] - v[t[offset]], v[t[offset + 2]] - v[t[offset]]);
                if (normal.y <= 0f) throw new Exception($"Trench floor faces underground: heading={heading}, threat side={side}");
                Object.DestroyImmediate(mesh);
            }
            // Cross-slope conformance: with a sampled slope, each outer skirt tip must sit
            // one skirt depth below the ground it sampled on its own side of the profile.
            var conform = TrenchMeshBuilder.BuildEdgeMesh(new[] { Vector3.zero, Vector3.forward * 30f },
                2.6f, 2.4f, 1.4f, Vector3.right, p => new Vector3(p.x, p.x * 0.25f, p.z));
            var cv = conform.vertices;
            Check(Mathf.Abs(cv[0].y - (cv[0].x * 0.25f - 1.4f)) < 0.01f &&
                Mathf.Abs(cv[9].y - (cv[9].x * 0.25f - 1.4f)) < 0.01f,
                "Ditch skirt tips must sit below the terrain they sample on each side");
            Object.DestroyImmediate(conform);
            CheckGrowthAndCombat();
            var curve = TrenchEdge.GeneratePathPoints(Vector3.zero, Vector3.back * 35f, Vector3.forward,
                TrenchEdgeType.CommunicationTrench);
            var curvedMesh = TrenchMeshBuilder.BuildEdgeMesh(curve, 2.2f, 1.7f, 1.4f, Vector3.forward);
            var curvedVertices = curvedMesh.vertices;
            int last = TrenchMeshBuilder.ProfilePointCount - 1;
            for (int ring = 1; ring < curve.Length; ring++)
                Check(Vector3.Dot(curvedVertices[ring * TrenchMeshBuilder.ProfilePointCount + last] -
                        curvedVertices[ring * TrenchMeshBuilder.ProfilePointCount],
                    curvedVertices[(ring - 1) * TrenchMeshBuilder.ProfilePointCount + last] -
                        curvedVertices[(ring - 1) * TrenchMeshBuilder.ProfilePointCount]) > 0,
                    "Communication trench walls must not twist across a traversed path");
            CheckEarthworkMaterial();
            Render();
            File.WriteAllText("result.txt", "PASS: eight winding orientations; connected seed, atomic growth through the full belt with forward saps, invalid-ground rejection, native-adapter defender budgets, damage suppression and no respawn after destruction. Native AI/networking require in-game acceptance. Stage renders saved.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText("result.txt", "FAIL: " + ex);
            Debug.LogException(ex);
            EditorApplication.Exit(1);
        }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    private static void CheckEarthworkMaterial()
    {
        var material = TrenchMaterialResolver.GetEarthBermMaterial();
        Check(material != null && material.mainTexture != null && material.mainTexture.width >= 64,
            "Earthwork material must carry the procedural cross-section palette texture");
        TrenchMaterialResolver.ResetForScene();
    }

    private static TrenchNetwork Network()
    {
        var net = new TrenchNetwork(1, "Check", new GameObject("HQ").AddComponent<FactionHQ>(), Vector3.zero, Vector3.forward);
        net.PlacementValidator = p => Mathf.Abs(p.x) <= 180f && p.z <= 60f && p.z >= -260f;
        Check(TrenchGrowthSimulator.Seed(net, p => p), "Connected seed failed");
        return net;
    }

    private static void CheckGrowthAndCombat()
    {
        var net = Network();
        Check(net.NodeCount == 7 && net.EdgeCount == 6, "Seed must be a seven-bay connected line");
        Check(net.FrontHalfSpan == 66f, "Seed half-span must be 66m");

        var blocked = Network();
        Check(TrenchGrowthSimulator.AdvanceSimulation(blocked, p => p), "Deepening failed");
        blocked.PlacementValidator = p => Mathf.Abs(p.x) < 60f;
        Check(!TrenchGrowthSimulator.AdvanceSimulation(blocked, p => p) && blocked.NodeCount == 7,
            "Invalid flank terrain must not extend the line");

        Encyclopedia.i = new Encyclopedia();
        foreach (string key in new[] { "Emplacement1_MG", "Emplacement1_ATGM", "Emplacement1_MANPADS" })
        {
            var prefab = new GameObject(key);
            prefab.AddComponent<Building>(); prefab.AddComponent<UnitPart>();
            prefab.SetActive(false);
            Encyclopedia.i.buildings.Add(new BuildingDefinition { jsonKey = key, unitPrefab = prefab });
        }
        var spawner = NetworkSceneSingleton<Spawner>.i = new Spawner();
        var garrison = new TrenchGarrison(net);
        Check(garrison.Establish() && spawner.Spawned.Count == 2, "Must start with two actual defense spawns");
        for (int stage = 2; stage <= 5; stage++)
        {
            Check(TrenchGrowthSimulator.AdvanceSimulation(net, p => p),
                "Growth failed at stage " + stage + ": " + TrenchGrowthSimulator.LastFailure);
            garrison.Reinforce(); garrison.Poll(stage);
            Check(garrison.Alive == (stage == 2 ? 3 : 4), "Wrong native defender count");
        }
        Check(net.NodeCount == 17 && net.EdgeCount == 18, "Mature network must fill the sector belt");
        Check(net.BunkerCount == 2, "Mature belt must include support and rear dugouts");
        Check(net.FrontHalfSpan == 72f, "Network half-span must match its capped flank limit");
        Check(TrenchGrowthSimulator.AdvanceSimulation(net, p => p), "Forward saps failed: " + TrenchGrowthSimulator.LastFailure);
        Check(net.Stage == TrenchStage.Stage6_Saps && net.NodeCount == 19 && net.EdgeCount == 20,
            "The final stage must push two saps ending in forward listening posts");
        spawner.Spawned[0].GetComponent<UnitPart>().hitPoints = 70;
        Check(garrison.Poll(10) && garrison.SuppressedUntil == 70, "A hit must stop construction for sixty seconds");
        spawner.Spawned[0].disabled = true;
        garrison.Poll(11); garrison.Reinforce();
        Check(spawner.Spawned.Count == 4 && garrison.Alive == 3, "A destroyed slot must never respawn");
        foreach (var unit in spawner.Spawned) unit.disabled = true;
        garrison.Poll(12); garrison.Reinforce();
        Check(garrison.Overrun && garrison.Alive == 0 && spawner.Spawned.Count == 4, "Wiped positions stop reinforcing");
        net.Overrun = true;
        Check(!TrenchGrowthSimulator.AdvanceSimulation(net, p => p), "Overrun position must stop growing");
        garrison.Remove();
        // A blocked slot must roll the spawn back, not leave a cosmetic position with a
        // leaked defender outside the manager's capacity accounting.
        float span = Mathf.Max(60f, net.FrontHalfSpan);
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.transform.position = new Vector3(0.62f * span, 2, -16);
        obstacle.transform.localScale = new Vector3(6, 4, 6);
        Physics.SyncTransforms();
        var failed = new TrenchGarrison(Network());
        int before = spawner.Spawned.Count;
        Check(!failed.Establish(), "Blocked native footprint must reject the site");
        Check(spawner.Spawned.Count == before + 1 && spawner.Spawned[before] == null,
            "Partial establishment must roll back its successful native spawn");
        Object.DestroyImmediate(obstacle);
    }

    private static void Render()
    {
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.transform.position = new Vector3(60, 160, 150);
        camera.transform.LookAt(new Vector3(0, 0, -40));
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.12f, 0.18f, 0.23f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.40f, 0.43f, 0.47f);
        var light = new GameObject("Sun").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(45, -30, 0);
        Material earth = TrenchMaterialResolver.GetEarthBermMaterial();
        if (earth == null) earth = new Material(Shader.Find("Standard")) { color = new Color(0.48f, 0.34f, 0.19f) };
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.transform.position = new Vector3(0, -0.6f, -50);
        ground.transform.localScale = new Vector3(460, 1, 320);
        ground.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.21f, 0.3f, 0.15f) };
        var target = new RenderTexture(1280, 720, 24);
        camera.targetTexture = target;
        var net = Network();
        for (int stage = 1; stage <= 5; stage++)
        {
            var root = new GameObject("Stage" + stage);
            foreach (var edge in net.Edges)
                DrawMesh(root.transform, TrenchMeshBuilder.BuildEdgeMesh(edge.PathPoints, edge.TrenchWidth,
                    edge.ParapetHeight, edge.SkirtDepth, net.ThreatDirection), Vector3.zero, earth);
            camera.Render();
            RenderTexture.active = target;
            WriteRender(target, "stage-" + stage + ".png");
            if (stage == 5)
            {
                camera.transform.position = new Vector3(34, 9, 34);
                camera.transform.LookAt(new Vector3(30, 0, -4));
                camera.Render();
                RenderTexture.active = target;
                WriteRender(target, "closeup-stage-5.png");
            }
            Object.DestroyImmediate(root);
            TrenchGrowthSimulator.AdvanceSimulation(net, p => p);
        }
    }

    private static void WriteRender(RenderTexture target, string file)
    {
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        image.Apply();
        File.WriteAllBytes(file, image.EncodeToPNG());
        Object.DestroyImmediate(image);
    }

    private static void DrawMesh(Transform root, Mesh mesh, Vector3 position, Material material)
    {
        var go = new GameObject(mesh.name);
        go.transform.SetParent(root, false); go.transform.localPosition = position;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
    }
}
#endif
