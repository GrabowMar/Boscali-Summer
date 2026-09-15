#if UNITY_EDITOR
using System;
using System.IO;
using BoscaliSummer.Features.Trenches.Domain;
using BoscaliSummer.Features.Trenches.Runtime;
using BoscaliSummer.Features.Trenches.Visuals;
using BoscaliSummer.Framework.Contracts;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class TrenchUnityCheck
{
    private sealed class FlatFront : ITerritoryIngress
    {
        public float BoundaryX;
        public bool TryNearestEdge(int factionId, float playerX, float playerZ, out float x, out float z)
        { x = z = 0f; return false; }
        public bool OwnsPosition(int factionId, float x, float z) => x < BoundaryX;
        public int CopyFrontlineTraces(int factionId, FrontlineTracePoint[] points, int[] lengths, float[] pressure)
            => 0;
    }

    public static void Run()
    {
        try
        {
            CheckMeshWindingAndConformance();
            CheckCurveAndPlanner();
            CheckGrowthAndCombat();
            CheckEarthworkMaterial();
            Render();
            File.WriteAllText("result.txt", "PASS: eight winding orientations; a sparse trace fitted to a smooth owned-side curve, terrain-refused runs split, stage growth through support/redoubt/saps, native-adapter defender budgets, damage suppression and no respawn after destruction. Native AI/networking require in-game acceptance. Stage renders saved.");
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

    private static void CheckMeshWindingAndConformance()
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
        var capped = TrenchMeshBuilder.BuildEdgeMesh(new[] { Vector3.zero, Vector3.forward * 9f },
            2.6f, 2.4f, 1.4f, Vector3.right);
        Check(capped.vertices.Length == 2 * TrenchMeshBuilder.ProfilePointCount + 2,
            "Both ditch ends must be closed with a head-cover cap");
        Object.DestroyImmediate(capped);

        var conform = TrenchMeshBuilder.BuildEdgeMesh(new[] { Vector3.zero, Vector3.forward * 30f },
            2.6f, 2.4f, 1.4f, Vector3.right, p => new Vector3(p.x, p.x * 0.25f, p.z));
        var cv = conform.vertices;
        Check(Mathf.Abs(cv[0].y - (cv[0].x * 0.25f - 1.4f)) < 0.01f &&
            Mathf.Abs(cv[9].y - (cv[9].x * 0.25f - 1.4f)) < 0.01f,
            "Ditch skirt tips must sit below the terrain they sample on each side");
        Object.DestroyImmediate(conform);

        // A traversed path must not twist its walls ring to ring.
        var traceX = new[] { 0f, 22f };
        var traceZ = new[] { 0f, 0f };
        var pathX = new float[64];
        var pathZ = new float[64];
        int rings = TrenchTraceMath.DensifyTraversed(traceX, traceZ, 2, TrenchTraceMath.MeshSpacing,
            TrenchTraceMath.TraverseSpacing, TrenchTraceMath.TraverseAmplitude, 0f, pathX, pathZ);
        var curve = new Vector3[rings];
        for (int i = 0; i < rings; i++) curve[i] = new Vector3(pathX[i], 0f, pathZ[i]);
        var curvedMesh = TrenchMeshBuilder.BuildEdgeMesh(curve, 2.2f, 1.7f, 1.4f, Vector3.forward);
        var curvedVertices = curvedMesh.vertices;
        int last = TrenchMeshBuilder.ProfilePointCount - 1;
        for (int ring = 1; ring < curve.Length; ring++)
            Check(Vector3.Dot(curvedVertices[ring * TrenchMeshBuilder.ProfilePointCount + last] -
                    curvedVertices[ring * TrenchMeshBuilder.ProfilePointCount],
                curvedVertices[(ring - 1) * TrenchMeshBuilder.ProfilePointCount + last] -
                    curvedVertices[(ring - 1) * TrenchMeshBuilder.ProfilePointCount]) > 0,
                "Trench walls must not twist across a traversed path");
        Object.DestroyImmediate(curvedMesh);
    }

    private static void CheckCurveAndPlanner()
    {
        var front = new FlatFront();
        var trace = new FrontlineTracePoint[3];
        for (int i = 0; i < 3; i++) trace[i] = new FrontlineTracePoint(0f, -600f + i * 600f);

        var owner = new GameObject("HQ").AddComponent<FactionHQ>();
        Check(TrenchPlanner.TryPlanWindow(1, "Check_Front", owner, 0.8f, trace, 0, trace.Length, 0,
            front, out TrenchLine line, out int next), "A front trace plans a position");
        Check(line.Curve.Length >= 100, "A 1.2km front yields a real curve");
        Check(next == 0, "One window spans the whole short trace");
        foreach (Vector3 station in line.Curve)
            Check(station.x <= -TrenchTraceMath.FireDepth + 1f,
                "Stations sit on the owned side of the trace");
        Check(line.Threat[0].x > 0.5f, "The parapet faces the enemy");
        Check(line.Anchors.Length >= 15 && line.Anchors.Length <= 64, "Anchors mark the ditch every sixty metres");

        // A trace too short to be a position is refused, and so is ground the owner lacks.
        var stub = new[] { new FrontlineTracePoint(0f, 0f), new FrontlineTracePoint(0f, 40f) };
        Check(!TrenchPlanner.TryPlanWindow(2, "Stub", owner, 0f, stub, 0, 2, 0, front, out _, out _),
            "A stub trace refuses a position");
        front.BoundaryX = -10000f;
        Check(!TrenchPlanner.TryPlanWindow(3, "Enemy", owner, 0f, trace, 0, trace.Length, 0, front, out _, out _),
            "Ground the faction does not hold is never entrenched");
        front.BoundaryX = 0f;

        Object.DestroyImmediate(owner.gameObject);
    }

    private static void CheckGrowthAndCombat()
    {
        var front = new FlatFront();
        var trace = new FrontlineTracePoint[3];
        for (int i = 0; i < 3; i++) trace[i] = new FrontlineTracePoint(0f, -600f + i * 600f);
        var owner = new GameObject("HQ").AddComponent<FactionHQ>();
        Check(TrenchPlanner.TryPlanWindow(1, "Check_Front", owner, 0.8f, trace, 0, trace.Length, 0,
            front, out TrenchLine line, out _), "A front trace plans a position");

        Encyclopedia.i = new Encyclopedia();
        foreach (string key in new[] { "Emplacement1_MG", "Emplacement1_ATGM", "Emplacement1_MANPADS" })
        {
            var prefab = new GameObject(key);
            prefab.AddComponent<Building>(); prefab.AddComponent<UnitPart>();
            prefab.SetActive(false);
            Encyclopedia.i.buildings.Add(new BuildingDefinition { jsonKey = key, unitPrefab = prefab });
        }
        var spawner = NetworkSceneSingleton<Spawner>.i = new Spawner();
        var garrison = new TrenchGarrison(line);
        Check(garrison.Establish() && spawner.Spawned.Count == 2, "Must start with two actual defense spawns");

        Check(TrenchPlanner.TryGrowBelt(line, front) && line.Stage == TrenchStage.FireTrench,
            "The fire trench stage must deepen the position");
        Check(TrenchPlanner.TryGrowBelt(line, front) && line.Stage == TrenchStage.Support &&
            line.Support != null && line.Support.Length >= 8 && line.SupportAnchors.Length > 0,
            "The support stage must lay a parallel trace behind the fire line");
        garrison.Reinforce(); garrison.Poll(1f);
        Check(garrison.Alive == 3, "Wrong native defender count after the support line");
        Check(TrenchPlanner.TryGrowBelt(line, front) && line.Stage == TrenchStage.Redoubt &&
            line.Redoubt != null && line.RedoubtAnchors.Length > 0,
            "The redoubt stage must lay the rear trace");
        garrison.Reinforce(); garrison.Poll(2f);
        Check(garrison.Alive == 4, "Wrong native defender count after the redoubt");
        Check(TrenchPlanner.TryGrowBelt(line, front) && line.Stage == TrenchStage.Saps &&
            line.Spurs != null && line.Spurs.Length == 2,
            "The final stage must push two saps ending in forward listening posts");
        Check(!TrenchPlanner.TryGrowBelt(line, front), "A finished position must stop growing");

        spawner.Spawned[0].GetComponent<UnitPart>().hitPoints = 70;
        Check(garrison.Poll(10f) && garrison.SuppressedUntil == 70f, "A hit must stop construction for sixty seconds");
        spawner.Spawned[0].disabled = true;
        garrison.Poll(11f); garrison.Reinforce();
        Check(spawner.Spawned.Count == 4 && garrison.Alive == 3, "A destroyed slot must never respawn");
        foreach (var unit in spawner.Spawned) unit.disabled = true;
        garrison.Poll(12f); garrison.Reinforce();
        Check(garrison.Overrun && garrison.Alive == 0 && spawner.Spawned.Count == 4, "Wiped positions stop reinforcing");
        garrison.Remove();

        // A blocked slot must roll the spawn back, not leave a cosmetic position with a
        // leaked defender outside the manager's capacity accounting. The obstacle sits on
        // the bay the first MG team digs into.
        Vector3 blocked = line.Anchors[Mathf.RoundToInt(0.15f * (line.Anchors.Length - 1))];
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.transform.position = blocked + Vector3.up * 2f;
        obstacle.transform.localScale = new Vector3(10f, 4f, 10f);
        Physics.SyncTransforms();
        var failed = new TrenchGarrison(line);
        int before = spawner.Spawned.Count;
        Check(!failed.Establish(), "Blocked native footprint must reject the site");
        Check(spawner.Spawned.Count == before, "A rejected position must not leak defenders");
        Object.DestroyImmediate(obstacle);
        Object.DestroyImmediate(owner.gameObject);
    }

    private static void CheckEarthworkMaterial()
    {
        var material = TrenchMaterialResolver.GetEarthBermMaterial();
        Check(material != null && material.mainTexture != null && material.mainTexture.width >= 64,
            "Earthwork material must carry the procedural cross-section palette texture");
        TrenchMaterialResolver.ResetForScene();
    }

    private static void Render()
    {
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.gameObject.tag = "MainCamera";
        camera.transform.position = new Vector3(60, 160, -300);
        camera.transform.LookAt(new Vector3(-60, 0, 0));
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
        ground.transform.position = new Vector3(-60, -0.6f, 0);
        ground.transform.localScale = new Vector3(460, 1, 1400);
        ground.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.21f, 0.3f, 0.15f) };
        var target = new RenderTexture(1280, 720, 24);
        camera.targetTexture = target;

        var front = new FlatFront();
        var trace = new FrontlineTracePoint[3];
        for (int i = 0; i < 3; i++) trace[i] = new FrontlineTracePoint(0f, -600f + i * 600f);
        var owner = new GameObject("HQ").AddComponent<FactionHQ>();
        Check(TrenchPlanner.TryPlanWindow(1, "Render_Front", owner, 0.8f, trace, 0, trace.Length, 0,
            front, out TrenchLine line, out _), "Render trace plans a position");
        var chunk = new GameObject("Chunk").AddComponent<TrenchVisualChunk>();
        chunk.Initialize(line);
        for (int stage = 0; stage <= 4; stage++)
        {
            if (stage > 0 && !TrenchPlanner.TryGrowBelt(line, front)) break;
            chunk.Rebuild();
            camera.Render();
            RenderTexture.active = target;
            WriteRender(target, "stage-" + stage + ".png");
        }
        camera.transform.position = new Vector3(10, 9, 34);
        camera.transform.LookAt(new Vector3(-58, 0, 0));
        camera.Render();
        RenderTexture.active = target;
        WriteRender(target, "closeup-final.png");
        Object.DestroyImmediate(chunk.gameObject);
        Object.DestroyImmediate(owner.gameObject);
    }

    private static void WriteRender(RenderTexture target, string file)
    {
        var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        image.Apply();
        File.WriteAllBytes(file, image.EncodeToPNG());
        Object.DestroyImmediate(image);
    }
}
#endif
