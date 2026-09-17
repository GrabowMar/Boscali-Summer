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
        public bool TryGetHoldStrength(int factionId, float x, float z, out float hold)
        {
            hold = BoundaryX - x;
            return true;
        }
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
            CheckWorksCatalog();
            CheckEarthworkMaterial();
            CheckLodFloatingOrigin();
            Render();
            File.WriteAllText("result.txt", "PASS: eight winding orientations; a sparse trace fitted to a smooth owned-side curve, terrain-refused runs split, a window fitted where its trace is (first and second window), contested band entrenched, stage growth through support/redoubt/saps, native-adapter defender budgets, damage suppression and no respawn after destruction; smooth terrain clipping a nest does not block it while solid obstacles still do; works deploy from the encyclopedia's instance lists with vehicle-scale pieces filtered; LOD is measured in local space so a chunk under a large floating origin stays visible at LOD0 with CameraStateManager present or absent. Native AI/networking require in-game acceptance. Stage renders saved.");
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

        // A field position must read from the air: a real parapet-and-spoil footprint, not a
        // garden ditch. One firing trench spreads across a dozen metres of earthwork.
        var section = TrenchMeshBuilder.BuildEdgeMesh(new[] { Vector3.zero, Vector3.forward * 40f },
            3.4f, 3.0f, 2.2f, Vector3.right);
        Check(section.bounds.size.x >= 12f,
            "One position's earthwork spans a real parapet, spoil apron and skirt footprint");
        Check(section.bounds.size.y >= 4f, "The parapet, spoil apron and skirt stand a real trench height");
        Object.DestroyImmediate(section);

        var wire = TrenchMeshBuilder.BuildWireBeltMesh(new[] { Vector3.zero, Vector3.forward * 60f }, 1.15f);
        Check(wire != null && wire.vertexCount >= 16 && wire.bounds.size.z > 50f,
            "The wire belt runs pickets and strands the whole length of the front");
        Object.DestroyImmediate(wire);

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

    /// <summary>
    /// The real control field is one signed value per kilometre cell, and a ragged front
    /// leaves salients whose own side still reads contested or slightly hostile. The cell
    /// holding the trace reads -0.05 (contested, hostile-leaning), the next cell in reads
    /// friendly: a position must still be dug there.
    /// </summary>
    private sealed class CellFront : ITerritoryIngress
    {
        public bool TryNearestEdge(int factionId, float playerX, float playerZ, out float x, out float z)
        { x = z = 0f; return false; }
        public bool OwnsPosition(int factionId, float x, float z) => Hold(x) >= 0f;
        public bool TryGetHoldStrength(int factionId, float x, float z, out float hold)
        { hold = Hold(x); return true; }
        public int CopyFrontlineTraces(int factionId, FrontlineTracePoint[] points, int[] lengths, float[] pressure)
            => 0;
        private static float Hold(float x)
            => x <= -1000f ? 1f : x <= 0f ? -0.05f : -1f;
    }

    private static void CheckCurveAndPlanner()
    {
        var front = new FlatFront();
        var trace = new FrontlineTracePoint[3];
        for (int i = 0; i < 3; i++) trace[i] = new FrontlineTracePoint(0f, -600f + i * 600f);

        var owner = new GameObject("HQ").AddComponent<FactionHQ>();
        Check(TrenchPlanner.TryPlanWindow(1, "Check_Front", owner, 0.8f, trace, 0, trace.Length, 0,
            front, out TrenchLine line, out int next, out _), "A front trace plans a position");
        Check(line.Curve.Length >= 100, "A 1.2km front yields a real curve");
        Check(next == 0, "One window spans the whole short trace");
        foreach (Vector3 station in line.Curve)
            Check(station.x <= -TrenchTraceMath.FireDepth + 1f,
                "Stations sit on the owned side of the trace");
        Check(line.Threat[0].x > 0.5f, "The parapet faces the enemy");
        Check(line.Anchors.Length >= 15 && line.Anchors.Length <= 64, "Anchors mark the ditch every sixty metres");

        // A real front is tens of kilometres long and its contour points arrive as cell-sized
        // strides. A window must still be a curve at the ditch's station spacing instead of the
        // handful of slabs the first planner produced from a trace-wide resample.
        var longTrace = new FrontlineTracePoint[60];
        for (int i = 0; i < longTrace.Length; i++)
            longTrace[i] = new FrontlineTracePoint(0f, -30000f + i * 1000f);
        Check(TrenchPlanner.TryPlanWindow(4, "Long_Front", owner, 0.8f, longTrace, 0, longTrace.Length, 0,
            front, out TrenchLine longLine, out int longNext, out _), "A long front plans a position");
        Check(longLine.Curve.Length >= 190,
            "A two-kilometre window of a long front keeps the ten-metre curve stations, not eight slabs");
        float worstStep = 0f;
        for (int i = 1; i < longLine.Base.Length; i++)
        {
            Vector3 delta = longLine.Base[i] - longLine.Base[i - 1];
            delta.y = 0f;
            worstStep = Mathf.Max(worstStep, delta.magnitude);
        }
        Check(worstStep <= TrenchTraceMath.CurveSpacing + 0.5f,
            "Trace stations stay at the curve spacing, not one per raw contour stride");
        Check(longLine.Anchors.Length >= 25, "A two-kilometre position carries an anchor every sixty metres");
        Check(longNext > 0, "The scan continues after the first window of a long trace");

        // The scan walks window by window: the second window must be fitted to its own
        // stretch of trace, never offset along it by the window start's raw station index
        // (the resampled buffer is window-local, so mixing the two shifts the position and
        // reads stale stations past the window's end).
        Check(TrenchPlanner.TryPlanWindow(5, "Second_Window", owner, 0.8f, longTrace, 0,
            longTrace.Length, longNext, front, out TrenchLine secondLine, out _, out TrenchRefusal secondRefusal),
            "The second window of a long front plans a position: " + secondRefusal);
        Check(Vector3.Distance(secondLine.Base[0],
                new Vector3(0f, 0f, -30000f + longNext * 1000f)) <= TrenchTraceMath.CurveSpacing * 2f,
            "A window is fitted where its trace actually is, not shifted by its start index");

        // The real control field is one signed value per kilometre cell, and a ragged front
        // leaves salients whose own side reads contested or slightly hostile. Ground on the
        // faction's own side still trenches; only deep enemy ground refuses.
        var cell = new CellFront();
        Check(TrenchPlanner.TryPlanWindow(6, "Cell_Front", owner, 0.5f, trace, 0, trace.Length, 0,
            cell, out TrenchLine cellLine, out _, out TrenchRefusal cellRefusal),
            "Contested ground on the faction's own side of the trace must entrench: " + cellRefusal);
        Check(cellLine.Curve.Length >= 100, "A contested-band position is still a real curve");

        // A trace too short to be a position is refused, and so is ground the owner lacks.
        var stub = new[] { new FrontlineTracePoint(0f, 0f), new FrontlineTracePoint(0f, 40f) };
        Check(!TrenchPlanner.TryPlanWindow(2, "Stub", owner, 0f, stub, 0, 2, 0, front, out _, out _, out _),
            "A stub trace refuses a position");
        front.BoundaryX = -10000f;
        Check(!TrenchPlanner.TryPlanWindow(3, "Enemy", owner, 0f, trace, 0, trace.Length, 0, front, out _, out _, out _),
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
            front, out TrenchLine line, out _, out _), "A front trace plans a position");

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

        // A blocked bay must be skipped, not roll the whole position back. The obstacle sits
        // on the bay the first MG team digs into.
        Vector3 blocked = line.Anchors[Mathf.RoundToInt(0.15f * (line.Anchors.Length - 1))];
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.transform.position = blocked + Vector3.up * 2f;
        obstacle.transform.localScale = new Vector3(10f, 4f, 10f);
        Physics.SyncTransforms();
        var displaced = new TrenchGarrison(line);
        int before = spawner.Spawned.Count;
        Check(displaced.Establish(), "One blocked bay must not cost the whole position");
        Check(spawner.Spawned.Count == before + 2, "The displaced teams still spawn native defenses");
        displaced.Remove();
        Object.DestroyImmediate(obstacle);

        // Terrain is the ground the nest stands on, not an obstacle: the same bay carrying
        // the game's terrain material must not displace or block the teams. The negative
        // case is the untextured obstacle above and the wall below.
        GameAssets.i = new GameAssets { terrainMaterial = new PhysicMaterial("SmoothTerrain") };
        var terrain = GameObject.CreatePrimitive(PrimitiveType.Cube);
        terrain.transform.position = blocked + Vector3.up * 2f;
        terrain.transform.localScale = new Vector3(10f, 4f, 10f);
        terrain.GetComponent<Collider>().sharedMaterial = GameAssets.i.terrainMaterial;
        Physics.SyncTransforms();
        var onTerrain = new TrenchGarrison(line);
        before = spawner.Spawned.Count;
        Check(onTerrain.Establish(), "Smooth terrain under the nest must not block it: " + onTerrain.LastFailure);
        Check(spawner.Spawned.Count == before + 2, "Both teams must establish through a terrain collider");
        onTerrain.Remove();
        Object.DestroyImmediate(terrain);

        // Ground that blocks every station still refuses the site rather than leaving a
        // cosmetic position with a leaked defender outside the manager's capacity.
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = line.Center + Vector3.up * 2f;
        wall.transform.localScale = new Vector3(60f, 6f, 4000f);
        Physics.SyncTransforms();
        var failed = new TrenchGarrison(line);
        before = spawner.Spawned.Count;
        Check(!failed.Establish(), "A fully blocked position must reject the site");
        Check(spawner.Spawned.Count == before, "A rejected position must not leak defenders");
        Object.DestroyImmediate(wall);
        Object.DestroyImmediate(owner.gameObject);
    }

    /// <summary>
    /// Works come from the encyclopedia's instance lists once it has loaded. The static
    /// Lookup dictionary is deliberately absent from the stubs: a catalog that reads it
    /// would not compile against this harness.
    /// </summary>
    private static void CheckWorksCatalog()
    {
        var front = new FlatFront();
        var trace = new FrontlineTracePoint[3];
        for (int i = 0; i < 3; i++) trace[i] = new FrontlineTracePoint(0f, -600f + i * 600f);
        var owner = new GameObject("HQ").AddComponent<FactionHQ>();
        Check(TrenchPlanner.TryPlanWindow(1, "Works_Front", owner, 0.8f, trace, 0, trace.Length, 0,
            front, out TrenchLine line, out _, out _), "A front trace plans a position for the works check");

        var encyclopedia = new Encyclopedia();
        Encyclopedia.i = encyclopedia;
        var smallPrefab = new GameObject("Hesco_Small");
        smallPrefab.AddComponent<Scenery>();
        encyclopedia.scenery.Add(new SceneryDefinition
        {
            jsonKey = "Hesco_Small", unitName = "HESCO Bastion", unitPrefab = smallPrefab,
            width = 2f, length = 2f, height = 1.2f
        });
        var hugePrefab = new GameObject("Hesco_Huge");
        hugePrefab.AddComponent<Scenery>();
        encyclopedia.scenery.Add(new SceneryDefinition
        {
            jsonKey = "Hesco_Huge", unitName = "HESCO Depot", unitPrefab = hugePrefab,
            width = 12f, length = 12f, height = 4f
        });

        var spawner = NetworkSceneSingleton<Spawner>.i = new Spawner();
        var works = new TrenchWorks(line);
        works.Deploy(TrenchStage.FireTrench);
        Check(works.Count == TrenchTraceMath.WorksBudget(TrenchStage.FireTrench),
            "A committed position must deploy works from the encyclopedia instance lists");
        Check(spawner.SceneryPrefabs.Count == works.Count && spawner.ScenerySpawned.Count == works.Count,
            "Every work must be one vanilla scenery spawn");
        foreach (GameObject prefab in spawner.SceneryPrefabs)
            Check(prefab == smallPrefab, "Vehicle-scale scenery must never be used as a work");
        works.Remove();

        Object.DestroyImmediate(smallPrefab);
        Object.DestroyImmediate(hugePrefab);
        Object.DestroyImmediate(owner.gameObject);
    }

    private static void CheckEarthworkMaterial()
    {
        var material = TrenchMaterialResolver.GetEarthBermMaterial();
        Check(material != null && material.mainTexture != null && material.mainTexture.width >= 64,
            "Earthwork material must carry the procedural cross-section palette texture");
        TrenchMaterialResolver.ResetForScene();
    }

    /// <summary>
    /// A live session showed every chunk at LOD3 with all roots inactive because the LOD
    /// comparison mixed global line centres with the local camera. The camera here stands on
    /// the earthwork while the floating origin is parked nearly ten kilometres away — the raw
    /// comparison would read the origin offset, not the chunk.
    /// </summary>
    private static void CheckLodFloatingOrigin()
    {
        var front = new FlatFront();
        var trace = new FrontlineTracePoint[3];
        for (int i = 0; i < 3; i++) trace[i] = new FrontlineTracePoint(0f, -600f + i * 600f);
        var owner = new GameObject("HQ").AddComponent<FactionHQ>();
        Check(TrenchPlanner.TryPlanWindow(1, "Lod_Front", owner, 0.8f, trace, 0, trace.Length, 0,
            front, out TrenchLine line, out _, out _), "A front trace plans a position for the LOD check");

        var cameraGo = new GameObject("LodCamera");
        cameraGo.tag = "MainCamera";
        var camera = cameraGo.AddComponent<Camera>();
        var viewGo = new GameObject("CameraView");
        var view = viewGo.AddComponent<CameraStateManager>();
        view.mainCamera = camera;
        SceneSingleton<CameraStateManager>.i = view;

        var chunk = new GameObject("LodChunk").AddComponent<TrenchVisualChunk>();
        chunk.Initialize(line);
        Datum.originPosition = new Vector3(-6400f, 0f, -7300f);
        camera.transform.position = new GlobalPosition(chunk.WorldCenter).ToLocalPosition();
        chunk.Rebuild();
        Check(chunk.CameraDistance < chunk.Lod0Distance,
            "LOD distance must be measured in one frame; a camera on the earthwork read " +
            chunk.CameraDistance + "m against Lod0Distance " + chunk.Lod0Distance);
        Check(chunk.ActiveLod == 0, "A camera on the earthwork must select LOD0, got " + chunk.ActiveLod);

        // A scene without a live CameraStateManager must still cull through Camera.main.
        SceneSingleton<CameraStateManager>.i = null;
        chunk.Rebuild();
        Check(chunk.CameraDistance < chunk.Lod0Distance && chunk.ActiveLod == 0,
            "LOD must fall back to Camera.main when CameraStateManager is absent");

        Object.DestroyImmediate(chunk.gameObject);
        Object.DestroyImmediate(viewGo);
        Object.DestroyImmediate(cameraGo);
        Object.DestroyImmediate(owner.gameObject);
        Datum.originPosition = Vector3.zero;
    }

    private static void Render()
    {
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.gameObject.tag = "MainCamera";
        camera.transform.position = new Vector3(60, 160, -300);
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 30000f;
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
            front, out TrenchLine line, out _, out _), "Render trace plans a position");
        var chunk = new GameObject("Chunk").AddComponent<TrenchVisualChunk>();
        chunk.Lod0Distance = 600f;
        chunk.Lod2Distance = 12000f;
        chunk.Lod1Distance = Mathf.Max(chunk.Lod0Distance * 2f, chunk.Lod2Distance * 0.22f);
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

        // The views that matter: a position has to read as an earthwork belt on a low pass
        // and from cruise altitude, instead of vanishing a few hundred metres out.
        FlightRender(camera, target, chunk, new Vector3(360, 460, -120), "flight-low.png");
        FlightRender(camera, target, chunk, new Vector3(1500, 2400, -700), "flight-cruise.png");

        Object.DestroyImmediate(chunk.gameObject);
        Object.DestroyImmediate(owner.gameObject);
    }

    private static void FlightRender(Camera camera, RenderTexture target, TrenchVisualChunk chunk,
        Vector3 position, string file)
    {
        camera.transform.position = position;
        camera.transform.LookAt(new Vector3(-60, 0, 0));
        chunk.Rebuild();
        camera.Render();
        RenderTexture.active = target;
        WriteRender(target, file);
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
