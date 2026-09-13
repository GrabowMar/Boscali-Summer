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
                    1.4f, 1.6f, 1f, Vector3.Cross(Vector3.up, heading) * side);
                var v = mesh.vertices;
                var t = mesh.triangles;
                // The central floor is the third cross-section strip (two triangles per strip).
                int offset = 2 * 6;
                Vector3 normal = Vector3.Cross(v[t[offset + 1]] - v[t[offset]], v[t[offset + 2]] - v[t[offset]]);
                if (normal.y <= 0f) throw new Exception($"Trench floor faces underground: heading={heading}, threat side={side}");
                Object.DestroyImmediate(mesh);
            }
            CheckGrowthAndCombat();
            var curve = TrenchEdge.GeneratePathPoints(Vector3.zero, Vector3.back * 35f, Vector3.forward,
                TrenchEdgeType.CommunicationTrench);
            var curvedMesh = TrenchMeshBuilder.BuildEdgeMesh(curve, 1.8f, 1.1f, 1f, Vector3.forward);
            var curvedVertices = curvedMesh.vertices;
            for (int ring = 1; ring < curve.Length; ring++)
                Check(Vector3.Dot(curvedVertices[ring * 7 + 6] - curvedVertices[ring * 7],
                    curvedVertices[(ring - 1) * 7 + 6] - curvedVertices[(ring - 1) * 7]) > 0,
                    "Communication trench walls must not twist across an S-curve");
            Render();
            File.WriteAllText("result.txt", "PASS: eight winding orientations; connected seed, bounded growth, invalid-ground rejection, native-adapter defender budgets, damage suppression and no respawn after destruction. Native AI/networking require in-game acceptance. Stage renders saved.");
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

    private static TrenchNetwork Network()
    {
        var net = new TrenchNetwork(1, "Check", new GameObject("HQ").AddComponent<FactionHQ>(), Vector3.zero, Vector3.forward);
        net.PlacementValidator = p => Mathf.Abs(p.x) <= 60 && Mathf.Abs(p.z) <= 60;
        Check(TrenchGrowthSimulator.Seed(net, p => p), "Connected seed failed");
        return net;
    }

    private static void CheckGrowthAndCombat()
    {
        var net = Network();
        Check(net.NodeCount == 5 && net.EdgeCount == 4, "Seed must already have five connected bays");
        var blocked = Network();
        Check(TrenchGrowthSimulator.AdvanceSimulation(blocked, p => p), "Deepening failed");
        blocked.PlacementValidator = p => p.z > -10;
        Check(!TrenchGrowthSimulator.AdvanceSimulation(blocked, p => p) && blocked.NodeCount == 5,
            "Invalid rear terrain must not create disconnected nodes");
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
        for (int stage = 2; stage <= 4; stage++)
        {
            Check(TrenchGrowthSimulator.AdvanceSimulation(net, p => p), "Growth failed at stage " + stage);
            garrison.Reinforce(); garrison.Poll(stage);
            Check(garrison.Alive == (stage == 2 ? 4 : 6), "Wrong native defender count");
        }
        Check(net.NodeCount == 9 && net.EdgeCount == 10, "Mature network must have a connected rear line and hub");
        spawner.Spawned[0].GetComponent<UnitPart>().hitPoints = 70;
        Check(garrison.Poll(10) && garrison.SuppressedUntil == 70, "A hit must stop construction for sixty seconds");
        spawner.Spawned[0].disabled = true;
        garrison.Poll(11); garrison.Reinforce();
        Check(spawner.Spawned.Count == 6 && garrison.Alive == 5, "A destroyed slot must never respawn");
        foreach (var unit in spawner.Spawned) unit.disabled = true;
        garrison.Poll(12); garrison.Reinforce();
        Check(garrison.Overrun && garrison.Alive == 0 && spawner.Spawned.Count == 6, "Wiped positions stop reinforcing");
        net.Overrun = true;
        Check(!TrenchGrowthSimulator.AdvanceSimulation(net, p => p), "Overrun position must stop growing");
        garrison.Remove();
        // A blocked second slot must roll the first spawn back, not leave a cosmetic
        // site with a leaked defender outside the manager's capacity accounting.
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.transform.position = new Vector3(36, 2, 12);
        obstacle.transform.localScale = new Vector3(5, 4, 5);
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
        camera.transform.position = new Vector3(75, 72, 66);
        camera.transform.LookAt(new Vector3(0, 0, -15));
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.12f, 0.18f, 0.23f);
        var light = new GameObject("Sun").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(45, -30, 0);
        var material = new Material(Shader.Find("Standard")) { color = new Color(0.48f, 0.34f, 0.19f) };
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.transform.position = new Vector3(0, -0.55f, -20);
        ground.transform.localScale = new Vector3(110, 1, 100);
        ground.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.21f, 0.3f, 0.15f) };
        var target = new RenderTexture(960, 600, 24);
        camera.targetTexture = target;
        var net = Network();
        for (int stage = 1; stage <= 4; stage++)
        {
            var root = new GameObject("Stage" + stage);
            foreach (var edge in net.Edges)
                DrawMesh(root.transform, TrenchMeshBuilder.BuildEdgeMesh(edge.PathPoints, edge.TrenchWidth,
                    edge.ParapetHeight, edge.SkirtDepth, net.ThreatDirection), Vector3.zero, material);
            foreach (var node in net.Nodes)
                DrawMesh(root.transform, TrenchMeshBuilder.BuildFightingBayMesh(), node.Position, material);
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(960, 600, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 960, 600), 0, 0);
            image.Apply();
            File.WriteAllBytes("stage-" + stage + ".png", image.EncodeToPNG());
            Object.DestroyImmediate(root);
            TrenchGrowthSimulator.AdvanceSimulation(net, p => p);
        }
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
