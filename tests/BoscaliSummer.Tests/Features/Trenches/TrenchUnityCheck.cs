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
        public bool TryGetHoldStrength(int factionId, float x, float z, out float hold)
        {
            hold = BoundaryX - x;
            return true;
        }
        public int CopyFrontlineTraces(int factionId, FrontlineTracePoint[] points, int[] lengths, float[] pressure)
            => 0;
    }

    // Measured numbers from the checks below, quoted in the PASS summary and the log so a
    // regression can be read off result.txt without re-running the harness.
    private static float PlainHalfWidth, PlainCrestHeight, PlainFloorWidth, BayHalfWidth;
    private static int RedoubtNests, SapsNests;
    private static float MaxNestNodeOffset;

    public static void Run()
    {
        try
        {
            CheckMeshWindingAndConformance();
            CheckCurveAndPlanner();
            CheckBaySchedule();
            CheckGrowthAndCombat();
            CheckBayPlacement();
            CheckFallbackNest();
            CheckNestStrip();
            CheckWorksCatalog();
            CheckEarthworkMaterial();
            CheckLodFloatingOrigin();
            Render();
            File.WriteAllText("result.txt",
                "PASS: eight winding orientations; a man-scale earthwork footprint beside the " +
                "vanilla emplacement models that stand in the bays; a sparse trace fitted to a " +
                "smooth owned-side curve, " +
                "terrain-refused runs split, a window fitted where its trace is (first and second " +
                "window), contested band entrenched, stage growth through support/redoubt/saps, " +
                "native-adapter defender budgets, damage suppression and no respawn after " +
                "destruction; a blocked bay walks to a neighbouring node before the slot gives up; " +
                "the fire ditch flares around its bay nodes and the " +
                "garrison stands in those bays; smooth terrain clipping a nest does not block it " +
                "while solid obstacles still do; works deploy from the encyclopedia's instance " +
                "lists with vehicle-scale pieces filtered, a village on the line is fortified " +
                "while a foreign building is left alone, and the saps stage adds its two " +
                "listening posts; two wire belts dress the approach; LOD is measured in local " +
                "space so a chunk under a large floating origin stays visible at LOD0 with " +
                "CameraStateManager present or absent; a vanilla or foreign building is never " +
                "stripped while a Boscali nest has its sandbag ring hidden and its unit part " +
                "kept alive. Native AI/networking require in-game acceptance.\n" +
                "Measured: plain ditch half=" + PlainHalfWidth.ToString("0.###") + "m crest=" +
                PlainCrestHeight.ToString("0.###") + "m floor=" + PlainFloorWidth.ToString("0.###") +
                "m; flared bay half=" + BayHalfWidth.ToString("0.###") + "m (gain " +
                (BayHalfWidth - PlainHalfWidth).ToString("0.###") + "m); " +
                "redoubt " + RedoubtNests + " nests, saps " + SapsNests + " nests; " +
                "worst nest-to-node " + MaxNestNodeOffset.ToString("0.###") + "m.\n" +
                "Renders: stage-0..4.png, bay-closeup.png, closeup-final.png, flight-low.png, " +
                "flight-cruise.png.");
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

        // A fire trench is an earthwork, but a man-scale one: it stands beside the vanilla
        // emplacement models that occupy its bays, so its parapet, berm and
        // skirt must not spread into the dozen-metre field work the flight silhouette
        // started as. The kit numbers are the LOD0 fire-trench call's; the bands below pin
        // the measured profile (half 2.41m, crest 1.24m, floor 0.88m) against accidental drift.
        var sectionPath = new[] { Vector3.zero, Vector3.forward * 40f };
        var section = TrenchMeshBuilder.BuildEdgeMesh(sectionPath, 1.6f, 1.2f, 1.1f, Vector3.right);
        var sv = section.vertices;
        float halfWidth = Mathf.Max(-section.bounds.min.x, section.bounds.max.x);
        Check(halfWidth >= 2f, "A fire trench still spreads a real parapet, berm and skirt per side");
        Check(halfWidth >= 2.3f && halfWidth <= 2.5f,
            "The plain ditch keeps its measured half footprint near 2.41m, got " + halfWidth + "m");
        Check(halfWidth <= 2.6f,
            "The earthwork stays man-scale beside the vanilla emplacement models: half footprint " +
            halfWidth + "m");
        float crestHeight = 0f;
        float floorWidth = 0f;
        for (int r = 0; r + TrenchMeshBuilder.ProfilePointCount <= sv.Length - 2; r += TrenchMeshBuilder.ProfilePointCount)
        {
            crestHeight = Mathf.Max(crestHeight, sv[r + 6].y);
            floorWidth = Mathf.Max(floorWidth, Mathf.Abs(sv[r + 4].x - sv[r + 3].x));
        }
        Check(crestHeight >= 1.15f && crestHeight <= 1.35f,
            "The plain parapet crest stays near its measured 1.24m, got " + crestHeight + "m");
        Check(crestHeight <= 1.4f, "A man-scale parapet crest stays under 1.4m, got " + crestHeight + "m");
        Check(floorWidth >= 0.83f && floorWidth <= 0.93f,
            "The fire-step floor keeps its measured 0.88m width, got " + floorWidth + "m");
        Check(floorWidth <= 1.2f, "A man-scale ditch floor is walkable under 1.2m, got " + floorWidth + "m");
        PlainHalfWidth = halfWidth;
        PlainCrestHeight = crestHeight;
        PlainFloorWidth = floorWidth;
        Debug.Log($"[TrenchUnityCheck] fire section: half={halfWidth:0.###}m crest={crestHeight:0.###}m floor={floorWidth:0.###}m");
        Object.DestroyImmediate(section);

        // A bay flares the whole cross-section instead of widening one strip of wall, and the
        // flare must still be man-scale: around 3.1m half width against the plain 2.4m.
        var bay = TrenchMeshBuilder.BuildEdgeMesh(sectionPath, 1.6f, 1.2f, 1.1f, Vector3.right, null,
            new[] { TrenchTraceMath.BayExtra(0f), TrenchTraceMath.BayExtra(0f) });
        BayHalfWidth = Mathf.Max(-bay.bounds.min.x, bay.bounds.max.x);
        Check(BayHalfWidth >= 2.95f && BayHalfWidth <= 3.3f,
            "A bay half-width sits around 3.1m, got " + BayHalfWidth + "m");
        Check(BayHalfWidth >= halfWidth + 0.5f,
            "A bay is meaningfully wider than the plain ditch: " + BayHalfWidth + "m vs " + halfWidth + "m");
        Check(BayHalfWidth <= halfWidth * 1.4f,
            "A flared bay is still man-scale, got " + BayHalfWidth + "m against a " + halfWidth + "m ditch");
        Object.DestroyImmediate(bay);

        // null and all-zero ring extra must be the plain profile to the float: this is what
        // keeps mid/far LODs and every non-fire ditch exactly as they were.
        var zeroExtra = TrenchMeshBuilder.BuildEdgeMesh(sectionPath, 1.6f, 1.2f, 1.1f, Vector3.right, null,
            new float[] { 0f, 0f });
        Check(zeroExtra.vertices.Length == sv.Length,
            "A zero ringExtra keeps the plain ring count");
        for (int i = 0; i < sv.Length; i++)
            Check(sv[i].x == zeroExtra.vertices[i].x && sv[i].y == zeroExtra.vertices[i].y &&
                sv[i].z == zeroExtra.vertices[i].z,
                "A null or all-zero ringExtra must reproduce the plain profile exactly at vertex " + i);
        Object.DestroyImmediate(zeroExtra);

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

    /// <summary>
    /// The bay schedule <see cref="TrenchLine.Validate"/> samples from the fire curve: a
    /// 400m straight curve gives a bay about every twenty metres, a 2400m one widens its
    /// pitch to stay inside <see cref="TrenchTraceMath.MaximumNodes"/> bays, and an empty
    /// curve has no bays at all (the null contract the garrison and mesh builder rely on).
    /// </summary>
    private static void CheckBaySchedule()
    {
        var owner = new GameObject("BayOwner").AddComponent<FactionHQ>();

        Vector3[] Straight(float length)
        {
            int count = Mathf.RoundToInt(length / TrenchTraceMath.CurveSpacing) + 1;
            var curve = new Vector3[count];
            for (int i = 0; i < count; i++)
                curve[i] = new Vector3(0f, 0f, i * TrenchTraceMath.CurveSpacing);
            return curve;
        }

        TrenchLine Synthetic(Vector3[] curve) => new TrenchLine(90, "Bay_Math", owner, 0f,
            TrenchTraceMath.CurveSpacing, Array.Empty<Vector3>(), Array.Empty<Vector3>(),
            new float[curve.Length], curve, Array.Empty<Vector3>());

        TrenchLine shortLine = Synthetic(Straight(400f));
        Check(shortLine.Nodes != null, "A 400m fire curve samples bay nodes");
        Check(shortLine.Nodes.Length == 21,
            "A 400m fire curve yields 21 bays at the 20m pitch, got " + (shortLine.Nodes?.Length ?? 0));
        Check(shortLine.Nodes[0].z == 0f && shortLine.Nodes[shortLine.Nodes.Length - 1].z == 400f,
            "The bay schedule spans the fire curve end to end");

        TrenchLine longLine = Synthetic(Straight(2400f));
        Check(longLine.Nodes != null && longLine.Nodes.Length > 20 &&
            longLine.Nodes.Length <= TrenchTraceMath.MaximumNodes,
            "A 2400m fire curve widens its pitch instead of packing past the node budget, got " +
            (longLine.Nodes?.Length ?? 0));

        TrenchLine emptyLine = Synthetic(Array.Empty<Vector3>());
        Check(emptyLine.Nodes == null, "An empty fire curve has no bays");

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
        foreach (string key in new[] { "Emplacement1_MG", "Emplacement1_ATGM", "Emplacement1_MANPADS", "Emplacement1_23mm" })
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
        Check(garrison.Alive == 5, "Wrong native defender count after the support line");
        Check(TrenchPlanner.TryGrowBelt(line, front) && line.Stage == TrenchStage.Redoubt &&
            line.Redoubt != null && line.RedoubtAnchors.Length > 0,
            "The redoubt stage must lay the rear trace");
        garrison.Reinforce(); garrison.Poll(2f);
        Check(garrison.Alive == 7, "Wrong native defender count after the redoubt");
        Check(TrenchPlanner.TryGrowBelt(line, front) && line.Stage == TrenchStage.Saps &&
            line.Spurs != null && line.Spurs.Length == 2,
            "The final stage must push two saps ending in forward listening posts");
        Check(!TrenchPlanner.TryGrowBelt(line, front), "A finished position must stop growing");
        // The saps stage is the eighth nest: commit it before the casualty test so a later
        // reinforce cannot add a fresh slot and disguise a respawn.
        garrison.Reinforce(); garrison.Poll(3f);
        Check(garrison.Alive == 8, "A saps position reinforces its full chain of eight nests, got " +
            garrison.Alive);

        spawner.Spawned[0].GetComponent<UnitPart>().hitPoints = 70;
        Check(garrison.Poll(10f) && garrison.SuppressedUntil == 70f, "A hit must stop construction for sixty seconds");
        spawner.Spawned[0].disabled = true;
        garrison.Poll(11f); garrison.Reinforce();
        Check(spawner.Spawned.Count == 8 && garrison.Alive == 7, "A destroyed slot must never respawn");
        foreach (var unit in spawner.Spawned) unit.disabled = true;
        garrison.Poll(12f); garrison.Reinforce();
        Check(garrison.Overrun && garrison.Alive == 0 && spawner.Spawned.Count == 8, "Wiped positions stop reinforcing");
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
        Check(failed.LastFailure == "footprint blocked",
            "A blocked position names the refusal: " + failed.LastFailure);
        Check(spawner.Spawned.Count == before, "A rejected position must not leak defenders");
        Object.DestroyImmediate(wall);
        Object.DestroyImmediate(owner.gameObject);
    }

    /// <summary>
    /// A nest whose primary bay is blocked falls back to a neighbouring node: one bad bay
    /// must not cost a whole position.
    /// </summary>
    private static void CheckFallbackNest()
    {
        var front = new FlatFront();
        var trace = new FrontlineTracePoint[3];
        for (int i = 0; i < 3; i++) trace[i] = new FrontlineTracePoint(0f, -600f + i * 600f);
        var owner = new GameObject("HQ").AddComponent<FactionHQ>();
        Check(TrenchPlanner.TryPlanWindow(1, "Fallback_Front", owner, 0.8f, trace, 0, trace.Length, 0,
            front, out TrenchLine line, out _, out _), "A front trace plans a position for the fallback check");

        var encyclopedia = new Encyclopedia();
        Encyclopedia.i = encyclopedia;
        encyclopedia.AddDefensePrefab("Emplacement1_MG", 2.4f);
        encyclopedia.AddDefensePrefab("Emplacement1_ATGM", 2.4f);
        encyclopedia.AddDefensePrefab("Emplacement1_MANPADS", 3.4f);
        encyclopedia.AddDefensePrefab("Emplacement1_23mm", 2.4f);
        var spawner = NetworkSceneSingleton<Spawner>.i = new Spawner();

        // Block the opening slot's primary bay; its neighbouring node must stay free.
        Check(line.Nodes != null && line.Nodes.Length > 2, "The fallback position carries bay nodes");
        int budget = TrenchTraceMath.DefenderBudget(line.Stage);
        int primary = Mathf.Clamp(Mathf.RoundToInt(TrenchTraceMath.NodeFraction(0, budget) *
            (line.Nodes.Length - 1)), 0, line.Nodes.Length - 1);
        Vector3 blockedNode = line.Nodes[primary];
        Vector3 fallbackNode = line.Nodes[primary + 1];
        var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obstacle.transform.position = blockedNode + Vector3.up * 2f;
        obstacle.transform.localScale = new Vector3(8f, 4f, 8f);
        Physics.SyncTransforms();

        var garrison = new TrenchGarrison(line);
        Check(garrison.Establish(), "A blocked opening bay still establishes: " + garrison.LastFailure);
        garrison.Reinforce();
        garrison.Poll(0f);
        Check(spawner.Spawned.Count >= 1, "The fallback nest still spawns its weapon");
        Vector3 nest = spawner.Spawned[0].transform.position;
        Check(new Vector2(nest.x - blockedNode.x, nest.z - blockedNode.z).magnitude > 5f,
            "The blocked primary bay pushed the nest off it");
        Check(new Vector2(nest.x - fallbackNode.x, nest.z - fallbackNode.z).magnitude <= 1f,
            "The nest landed on the neighbouring fallback bay");
        int nestNode = NearestNodeIndex(line.Nodes, nest);
        Check(nestNode == primary + 1, "The fallback nest's node is the neighbouring bay, got index " +
            nestNode + " against primary " + primary);

        garrison.Remove();
        Object.DestroyImmediate(obstacle);
        Object.DestroyImmediate(owner.gameObject);
    }

    /// <summary>
    /// The fire ditch is a chain of bays and the nests stand in them: at Redoubt and Saps the
    /// nest count is exactly the stage budget and every nest sits on a bay node facing the
    /// threat. A line with no bays refuses the site by name instead of leaking a defender.
    /// </summary>
    private static void CheckBayPlacement()
    {
        var front = new FlatFront();
        var trace = new FrontlineTracePoint[3];
        for (int i = 0; i < 3; i++) trace[i] = new FrontlineTracePoint(0f, -600f + i * 600f);
        var owner = new GameObject("HQ").AddComponent<FactionHQ>();
        Check(TrenchPlanner.TryPlanWindow(1, "Bay_Front", owner, 0.8f, trace, 0, trace.Length, 0,
            front, out TrenchLine line, out _, out _), "A front trace plans a position for the bay placement check");

        var encyclopedia = new Encyclopedia();
        Encyclopedia.i = encyclopedia;
        encyclopedia.AddDefensePrefab("Emplacement1_MG", 2.4f);
        encyclopedia.AddDefensePrefab("Emplacement1_ATGM", 2.4f);
        encyclopedia.AddDefensePrefab("Emplacement1_MANPADS", 3.4f);
        encyclopedia.AddDefensePrefab("Emplacement1_23mm", 2.4f);
        var spawner = NetworkSceneSingleton<Spawner>.i = new Spawner();

        Check(TrenchPlanner.TryGrowBelt(line, front) && TrenchPlanner.TryGrowBelt(line, front) &&
            TrenchPlanner.TryGrowBelt(line, front) && line.Stage == TrenchStage.Redoubt,
            "The bay placement check grows a position to the redoubt");
        var garrison = new TrenchGarrison(line);
        Check(garrison.Establish(), "A redoubt establishes its defenders: " + garrison.LastFailure);
        garrison.Reinforce();
        garrison.Poll(0f);
        RedoubtNests = spawner.Spawned.Count;
        Check(RedoubtNests == TrenchTraceMath.DefenderBudget(TrenchStage.Redoubt),
            "A redoubt fields exactly its seven bay nests, got " + RedoubtNests);
        Check(line.Nodes != null && line.Nodes.Length > 0,
            "A planned position carries bay nodes for its nests");

        MaxNestNodeOffset = 0f;
        for (int i = 0; i < spawner.Spawned.Count; i++)
        {
            Vector3 nest = spawner.Spawned[i].transform.position;
            float nodeDistance = NearestNodeDistance(line.Nodes, nest);
            MaxNestNodeOffset = Mathf.Max(MaxNestNodeOffset, nodeDistance);
            Check(nodeDistance <= 1f, "A nest stands on a bay node, " + nodeDistance + "m away");
            Check(Vector3.Dot(spawner.Spawned[i].transform.forward, line.ThreatAt(nest)) > 0.99f,
                "A nest faces the threat");
        }

        // The chain grows to Saps: the eighth bay appears.
        Check(TrenchPlanner.TryGrowBelt(line, front) && line.Stage == TrenchStage.Saps,
            "The bay placement check grows a position to the saps");
        garrison.Reinforce();
        garrison.Poll(0f);
        SapsNests = spawner.Spawned.Count;
        Check(SapsNests == TrenchTraceMath.DefenderBudget(TrenchStage.Saps),
            "A saps position fields exactly its eight bay nests, got " + SapsNests);
        garrison.Remove();

        // No bays and no anchors at all: the position must refuse by name, never spawn a
        // stray defender outside the manager's capacity.
        var bayless = new TrenchLine(98, "Bayless", owner, 0f, TrenchTraceMath.CurveSpacing,
            Array.Empty<Vector3>(), Array.Empty<Vector3>(), Array.Empty<float>(),
            Array.Empty<Vector3>(), Array.Empty<Vector3>()) { Anchors = Array.Empty<Vector3>() };
        int before = spawner.Spawned.Count;
        var noBays = new TrenchGarrison(bayless);
        Check(!noBays.Establish(), "A bayless line refuses the site");
        Check(noBays.LastFailure == "no anchor", "A bayless line names the refusal: " + noBays.LastFailure);
        Check(spawner.Spawned.Count == before, "A refused bayless line leaks no defender");

        Object.DestroyImmediate(owner.gameObject);
    }

    /// <summary>
    /// The sandbag ring under a Boscali nest is hidden on every peer, identified by the name
    /// the garrison gives its own buildings only: a vanilla or foreign building is never
    /// touched, and the dugout's UnitPart stays enabled so damage RPC indices never shift.
    /// </summary>
    private static void CheckNestStrip()
    {
        Check(TrenchNestVisual.MarksOwnPosition("BoscaliSummer:Trench:5:1"),
            "A Boscali nest name marks the position");
        Check(!TrenchNestVisual.MarksOwnPosition("Emplacement1_MG") &&
            !TrenchNestVisual.MarksOwnPosition("OtherMod:Trench:5:1") &&
            !TrenchNestVisual.MarksOwnPosition("BoscaliSummer:Trench") &&
            !TrenchNestVisual.MarksOwnPosition("") &&
            !TrenchNestVisual.MarksOwnPosition(null),
            "A vanilla or foreign building name is never marked as Boscali's own");

        var nest = new GameObject("BoscaliNest");
        var unit = nest.AddComponent<Unit>();
        unit.UniqueName = "BoscaliSummer:Trench:9:2";
        var dugout = new GameObject("dugout");
        dugout.transform.SetParent(nest.transform, false);
        var ringRenderer = dugout.AddComponent<MeshRenderer>();
        var ringCollider = dugout.AddComponent<BoxCollider>();
        var ringPart = dugout.AddComponent<UnitPart>();
        var ringBehaviour = dugout.AddComponent<Scenery>();
        TrenchNestVisual.Strip(unit);
        Check(!ringRenderer.enabled && !ringCollider.enabled && !ringBehaviour.enabled,
            "The sandbag ring's renderers, colliders and cosmetic behaviours are disabled");
        Check(ringPart.enabled, "The dugout's UnitPart stays enabled so damage RPC indices never shift");
        Object.DestroyImmediate(nest);

        var vanilla = new GameObject("VanillaNest");
        var vanillaUnit = vanilla.AddComponent<Unit>();
        vanillaUnit.UniqueName = "Emplacement1_MG";
        var vanillaDugout = new GameObject("dugout");
        vanillaDugout.transform.SetParent(vanilla.transform, false);
        var vanillaRenderer = vanillaDugout.AddComponent<MeshRenderer>();
        TrenchNestVisual.Strip(vanillaUnit);
        Check(vanillaRenderer.enabled, "A vanilla emplacement's sandbag ring is never stripped");
        Object.DestroyImmediate(vanilla);
    }

    private static float NearestNodeDistance(Vector3[] nodes, Vector3 point)
    {
        float best = float.MaxValue;
        for (int i = 0; nodes != null && i < nodes.Length; i++)
        {
            float dx = nodes[i].x - point.x, dz = nodes[i].z - point.z;
            best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
        }
        return best;
    }

    private static int NearestNodeIndex(Vector3[] nodes, Vector3 point)
    {
        int best = -1;
        float bestSq = float.MaxValue;
        for (int i = 0; nodes != null && i < nodes.Length; i++)
        {
            float dx = nodes[i].x - point.x, dz = nodes[i].z - point.z;
            float sq = dx * dx + dz * dz;
            if (sq < bestSq) { bestSq = sq; best = i; }
        }
        return best;
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

        // A village on the line: the building itself stays vanilla but gets one sandbag piece
        // against its front, on top of the ditch works. A building owned by the other side is
        // left alone.
        var villageGo = new GameObject("Village");
        var village = villageGo.AddComponent<Building>();
        village.NetworkHQ = owner;
        villageGo.transform.position = line.Curve[line.Curve.Length / 2];
        UnitRegistry.allUnits.Add(village);
        var foreignGo = new GameObject("ForeignDepot");
        var foreignOwner = new GameObject("OtherHQ").AddComponent<FactionHQ>();
        var foreign = foreignGo.AddComponent<Building>();
        foreign.NetworkHQ = foreignOwner;
        foreignGo.transform.position = line.Curve[line.Curve.Length / 2] + Vector3.forward * 30f;
        UnitRegistry.allUnits.Add(foreign);

        var structural = new TrenchWorks(line);
        structural.Deploy(TrenchStage.FireTrench);
        Check(structural.Count == TrenchTraceMath.WorksBudget(TrenchStage.FireTrench) &&
            spawner.ScenerySpawned.Count == works.Count + structural.Count + 1,
            "A village inside the corridor gets one fortifying piece and the ditch works still deploy");
        structural.Remove();
        UnitRegistry.allUnits.Clear();

        // The saps stage unlocks the two listening posts at the sap heads: the works budget
        // reaches ten and only the sap positions change.
        for (int i = 0; i < 4; i++)
            Check(TrenchPlanner.TryGrowBelt(line, front), "The works line must grow to the saps stage");
        works.Deploy(TrenchStage.Saps);
        Check(works.Count == TrenchTraceMath.WorksBudget(TrenchStage.Saps),
            "The saps stage must add two listening posts at the sap heads, got " + works.Count);
        works.Remove();

        Object.DestroyImmediate(villageGo);
        Object.DestroyImmediate(foreignGo);
        Object.DestroyImmediate(foreignOwner.gameObject);
        Object.DestroyImmediate(smallPrefab);
        Object.DestroyImmediate(hugePrefab);
        Object.DestroyImmediate(owner.gameObject);
    }

    private static void CheckEarthworkMaterial()
    {
        var material = TrenchMaterialResolver.GetEarthBermMaterial();
        Check(material != null && material.mainTexture != null && material.mainTexture.width >= 64,
            "Earthwork material must carry the procedural cross-section palette texture");
        var wireMaterial = TrenchMaterialResolver.GetWireMaterial();
        Check(wireMaterial != null && wireMaterial != material &&
            (wireMaterial.HasProperty("_BaseColor")
                ? wireMaterial.GetColor("_BaseColor").r < 0.3f : wireMaterial.color.r < 0.3f),
            "The wire belt needs a distinct dark steel material");
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
        Check(chunk.transform.Find("LOD0_FullDetail/WireBelt") != null &&
            chunk.transform.Find("LOD0_FullDetail/WireBelt_Outer") != null,
            "The wired approach must read as two belts in front of the parapet");
        Datum.originPosition = new Vector3(-6400f, 0f, -7300f);
        camera.transform.position = new GlobalPosition(chunk.WorldCenter).ToLocalPosition();
        chunk.Rebuild();
        Check(chunk.CameraDistance < chunk.Lod0Distance,
            "LOD distance must be measured in one frame; a camera on the earthwork read " +
            chunk.CameraDistance + "m against Lod0Distance " + chunk.Lod0Distance);
        Check(chunk.ActiveLod == 0, "A camera on the earthwork must select LOD0, got " + chunk.ActiveLod);

        NetworkSceneSingleton<Spawner>.i = new Spawner { IsServer = true };
        camera.transform.position += Vector3.up * 13000f;
        chunk.Rebuild();
        var barriers = chunk.GetComponentsInChildren<BoxCollider>(true);
        Check(chunk.ActiveLod == 3 && barriers.Length > 0 &&
            Array.Exists(barriers, barrier => barrier.enabled),
            "Host parapet collision must persist when the camera culls the visual trench");
        NetworkSceneSingleton<Spawner>.i.IsServer = false;
        chunk.Rebuild();
        barriers = chunk.GetComponentsInChildren<BoxCollider>(true);
        Check(barriers.Length > 0 && Array.TrueForAll(barriers, barrier => !barrier.enabled),
            "A client-side visual chunk must not own parapet collision");
        NetworkSceneSingleton<Spawner>.i.IsServer = true;
        camera.transform.position = new GlobalPosition(chunk.WorldCenter).ToLocalPosition();

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
        NetworkSceneSingleton<Spawner>.i = null;
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
        // Who holds the ditch: one nest proxy per bay node, so the close-up shows the
        // emplacements standing in the flared bays.
        var renderEncyclopedia = new Encyclopedia();
        Encyclopedia.i = renderEncyclopedia;
        AddNestProxy(renderEncyclopedia, "Emplacement1_MG", 2.4f, new Color(0.62f, 0.49f, 0.29f));
        AddNestProxy(renderEncyclopedia, "Emplacement1_ATGM", 2.4f, new Color(0.55f, 0.44f, 0.27f));
        AddNestProxy(renderEncyclopedia, "Emplacement1_MANPADS", 3.4f, new Color(0.50f, 0.47f, 0.30f));
        AddNestProxy(renderEncyclopedia, "Emplacement1_23mm", 2.4f, new Color(0.45f, 0.43f, 0.28f));
        var renderSpawner = NetworkSceneSingleton<Spawner>.i = new Spawner();
        var renderGarrison = new TrenchGarrison(line);
        Check(renderGarrison.Establish(), "The render garrison establishes: " + renderGarrison.LastFailure);
        renderGarrison.Reinforce();
        renderGarrison.Poll(0f);
        Check(renderSpawner.Spawned.Count == TrenchTraceMath.DefenderBudget(line.Stage),
            "The render garrison fields the full bay chain: " + renderSpawner.Spawned.Count + " nests");

        // Frame a bay that actually holds a nest, not a bare mid-line node.
        Vector3 bay = renderSpawner.Spawned[renderSpawner.Spawned.Count / 2].transform.position;
        Debug.Log("[TrenchUnityCheck] render bay " + bay + " nest0 " +
            renderSpawner.Spawned[0].transform.position);
        camera.transform.position = bay + new Vector3(-4.5f, 2.3f, -9f);
        camera.transform.LookAt(bay + new Vector3(0f, 0.6f, 0f));
        camera.Render();
        RenderTexture.active = target;
        WriteRender(target, "bay-closeup.png");

        camera.transform.position = new Vector3(10, 9, 34);
        camera.transform.LookAt(new Vector3(-58, 0, 0));
        camera.Render();
        RenderTexture.active = target;
        WriteRender(target, "closeup-final.png");

        // The views that matter: a position has to read as an earthwork belt on a low pass
        // and from cruise altitude, instead of vanishing a few hundred metres out.
        FlightRender(camera, target, chunk, new Vector3(360, 460, -120), "flight-low.png");
        FlightRender(camera, target, chunk, new Vector3(1500, 2400, -700), "flight-cruise.png");

        renderGarrison.Remove();
        Object.DestroyImmediate(chunk.gameObject);
        Object.DestroyImmediate(owner.gameObject);
    }

    /// <summary>A visible, man-scale stand-in for a game model the harness does not load.</summary>
    private static void AddProxy(GameObject prefab, PrimitiveType shape, Color color, Vector3 scale,
        Vector3 localCenter)
    {
        var body = GameObject.CreatePrimitive(shape);
        body.name = "proxy";
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.transform.SetParent(prefab.transform, false);
        body.transform.localScale = scale;
        body.transform.localPosition = localCenter;
        body.GetComponent<MeshRenderer>().sharedMaterial =
            new Material(Shader.Find("Standard")) { color = color };
    }

    private static void AddNestProxy(Encyclopedia encyclopedia, string key, float footprint, Color color)
    {
        GameObject prefab = encyclopedia.AddDefensePrefab(key, footprint);
        AddProxy(prefab, PrimitiveType.Cube, color,
            new Vector3(footprint * 0.8f, 0.9f, footprint * 0.8f), new Vector3(0f, 0.45f, 0f));
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
