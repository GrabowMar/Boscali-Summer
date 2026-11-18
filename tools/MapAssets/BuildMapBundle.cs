using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Standalone editor entry point used by Build-MapBundle.ps1: turns the folder `nomod map gen` wrote into one bundle.
// The bundle holds only engine types (meshes, colliders, LOD groups, textures, a json TextAsset); the mod adds the
// game's own components when it loads the map, so a game update cannot break the baked data.
public static class BuildMapBundle
{
    private const string Dir = "Assets/Map/";
    private const long BundleLimit = 80L * 1024 * 1024; // embedded in the mod DLL: past this, ship the bundle as a loose file

    [Serializable] private class StripInfo { public float x, y, z, length, width = 40f, padLength = 1500f, padWidth = 300f; public float heading; public string surface = "asphalt"; }
    [Serializable] private class ForestInfo { public string name; }
    [Serializable] private class BridgeInfo { public float x, z, heading, length, width; }
    [Serializable] private class RoadInfo { public string kind; public float width; public float[] points, widths; }
    [Serializable] private class MapInfo
    {
        public string id, displayName; public float heightFloor, heightSpan, cell; public int res, tile, vertexBudget; public StripInfo[] strips; public ForestInfo[] forests; public RoadInfo[] roads, rivers; public BridgeInfo[] bridges;
    }


    private static readonly List<Mesh> meshes = new List<Mesh>();
    private static int chunkCount, landTiles, triangleCount;

    public static void Run()
    {
        try
        {
            string source = Argument("-mapSource");
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureScratchPipeline();
            var info = JsonUtility.FromJson<MapInfo>(File.ReadAllText(Path.Combine(source, "map.json")));
            if (info == null || string.IsNullOrEmpty(info.id) || info.res <= 0 || info.tile <= 0 || info.res % info.tile != 0 || info.cell <= 0f)
                throw new Exception("map.json has no id, or no usable res / tile / cell.");

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Dir + "MapSurface.shader");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("MapSurface shader did not compile.");
            Material ground = NewMaterial(shader, "MapGround", 0f, 2f, .35f);
            Material paved = NewMaterial(shader, "MapPaved", 0f, 2f, .12f);

            var root = new GameObject("MapRoot");
            var terrain = Child(root, "Terrain");
            var airfields = Child(root, "Airfields");
            BuildTerrain(terrain, info, ReadHeights(Path.Combine(source, "height.r16"), info), ReadColours(Path.Combine(source, "colour.rgb"), info.res), ground);
            for (int n = 0; n < info.strips.Length; n++) BuildStrip(airfields, n, info.strips[n], paved);
            BuildRoads(Child(root, "Water"), info, info.rivers, paved);
            var roadsRoot = Child(root, "Roads");
            BuildRoads(roadsRoot, info, info.roads, paved);
            BuildBridges(roadsRoot, info, paved);

            string meshPath = Dir + "MapMeshes.asset";
            AssetDatabase.CreateAsset(meshes[0], meshPath);
            for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], meshPath);
            AssetDatabase.SaveAssets();
            string prefabPath = Dir + "MapRoot.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            Validate(prefabPath);

            string image = $"bs_{info.id}_map.png";
            ImportTexture(Dir + "ocean_depth.png", false, false, false);
            ImportTexture(Dir + "ocean_color.png", true, false, false);
            ImportTexture(Dir + "colormap.png", true, true, false);
            ImportTexture(Dir + image, true, false, true);
            // For the game's own terrain shader, grass and trees: whole-map colour, ground-cover weights, grass mask.
            ImportTexture(Dir + "macro.png", true, false, false, 4096);
            foreach (string cover in new[] { "splat_grass.png", "splat_rock.png", "splat_lush.png", "grass_mask.png" }) ImportTexture(Dir + cover, false, false, false);
            var extras = new List<string>();
            foreach (string extra in new[] { "splat_extra.png", "macro_ao.png", "macro_normal.png" })
                if (File.Exists(Dir + extra))
                {
                    ImportTexture(Dir + extra, false, false, false, extra == "macro_normal.png" ? 4096 : 2048, extra == "macro_normal.png");
                    extras.Add(Dir + extra);
                }
            Directory.CreateDirectory("BundleOutput");
            var names = new List<string> { prefabPath, Dir + "map.json", Dir + "ocean_depth.png", Dir + "ocean_color.png", Dir + "colormap.png", Dir + image,
                Dir + "macro.png", Dir + "splat_grass.png", Dir + "splat_rock.png", Dir + "splat_lush.png", Dir + "grass_mask.png" };
            for (int n = 0; n < (info.forests ?? new ForestInfo[0]).Length; n++) names.Add(Dir + "forest_" + n + ".bytes");
            if (File.Exists(Dir + "scenery.bytes")) names.Add(Dir + "scenery.bytes");
            names.AddRange(extras);
            var build = new AssetBundleBuild { assetBundleName = info.id + ".bsmap", assetNames = names.ToArray() };
            if (BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build },
                BuildAssetBundleOptions.StrictMode, BuildTarget.StandaloneWindows64) == null) // LZMA: a height field packs far smaller than with LZ4
                throw new Exception("Map bundle build returned no manifest.");
            long bytes = new FileInfo("BundleOutput/" + info.id + ".bsmap").Length;
            if (bytes > BundleLimit) throw new Exception("Map bundle exceeds 80 MiB: " + bytes + " bytes.");
            File.WriteAllText("result.txt", $"PASS: {info.displayName}: {chunkCount} terrain tiles ({landTiles} with land, a vertex every {landStride * info.cell:0} m), " +
                $"{triangleCount} triangles, {info.strips.Length} strips; bundle {bytes} bytes.");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("result.txt", "FAIL: " + error);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>mapkit.bskit: every shape of MapKitShapes as an inactive child kit_&lt;kind&gt;_&lt;variant&gt; of the prefab MapKit,
    /// and the text asset kit that lists them. Maps place them from their scenery list; other modules may borrow them.</summary>
    public static void RunKit()
    {
        try
        {
            PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureScratchPipeline();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Dir + "MapSurface.shader");
            if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new Exception("MapSurface shader did not compile.");
            Material still = NewMaterial(shader, "KitStill", 0f, 0f, .2f), swaying = NewMaterial(shader, "KitSway", .35f, 0f, .2f);
            var root = new GameObject("MapKit");
            var lines = new List<string>();
            int shapes = 0, kindIndex = 0;
            foreach (MapKitShapes.Kind kind in MapKitShapes.Kinds)
            {
                var random = new System.Random(4242 + kindIndex++);
                float radius = 0f;
                int most = 0;
                for (int v = 0; v < kind.Variants; v++)
                {
                    MapKitShapes.Shape shape = MapKitShapes.Build(kind.Name, v, random);
                    var go = Child(root, "kit_" + kind.Name + "_" + v);
                    Mesh mesh = KitMesh(go.name, shape.Mesh, true);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = kind.Sway ? swaying : still;
                    if (kind.Solid)
                    {
                        var collider = go.AddComponent<MeshCollider>();
                        collider.sharedMesh = shape.Collision != null ? KitMesh(go.name + "_solid", shape.Collision, false) : mesh;
                        collider.convex = shape.Collision == null || shape.CollisionConvex;
                    }
                    go.SetActive(false);
                    radius = Mathf.Max(radius, shape.Mesh.Radius());
                    most = Mathf.Max(most, shape.Mesh.TriangleCount);
                    shapes++;
                }
                lines.Add("{\"name\":\"" + kind.Name + "\",\"variants\":" + kind.Variants + ",\"triangles\":" + most + ",\"solid\":" + (kind.Solid ? "true" : "false") +
                    ",\"draw\":\"" + kind.Draw + "\",\"radius\":" + radius.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "}");
            }
            File.WriteAllText(Dir + "kit.json", "{\"version\":1,\"kinds\":[" + string.Join(",", lines) + "]}");
            AssetDatabase.CreateAsset(meshes[0], Dir + "KitMeshes.asset");
            for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], Dir + "KitMeshes.asset");
            AssetDatabase.SaveAssets();
            PrefabUtility.SaveAsPrefabAsset(root, Dir + "MapKit.prefab");
            Object.DestroyImmediate(root);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Directory.CreateDirectory("BundleOutput");
            var build = new AssetBundleBuild { assetBundleName = "mapkit.bskit", assetNames = new[] { Dir + "MapKit.prefab", Dir + "kit.json" } };
            if (BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build }, BuildAssetBundleOptions.StrictMode, BuildTarget.StandaloneWindows64) == null)
                throw new Exception("Kit bundle build returned no manifest.");
            File.WriteAllText("result.txt", $"PASS: scenery kit: {MapKitShapes.Kinds.Length} kinds, {shapes} shapes; bundle {new FileInfo("BundleOutput/mapkit.bskit").Length} bytes.");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("result.txt", "FAIL: " + error);
            EditorApplication.Exit(1);
        }
    }

    private static Mesh KitMesh(string name, MapMeshBuilder b, bool drawn)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(b.Vertices);
        mesh.SetTriangles(b.Triangles, 0);
        if (drawn)
        {
            mesh.SetNormals(b.Normals);
            mesh.SetColors(b.LinearColours());
            mesh.SetUVs(0, b.Uvs);
        }
        return Keep(mesh);
    }

    // Same setup as tools/WorldDetailAssets: without a pipeline asset URP strips every variant of a bundle's shader
    // ("After scriptable stripping: 0") and the game draws the map magenta.
    private static void ConfigureScratchPipeline()
    {
        const string rendererPath = Dir + "BuildRenderer.asset", pipelinePath = Dir + "BuildPipeline.asset";
        UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, rendererPath);
        }
        UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, pipelinePath);
        }
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;

        // These settings apply only to this disposable authoring project. Runtime-built
        // materials cannot keep shader_feature/instancing variants through scene analysis.
        var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
        SerializedProperty instancing = graphics.FindProperty("m_InstancingStripping");
        if (instancing == null) throw new Exception("Unity's instancing stripping setting was not found.");
        instancing.intValue = 2; // Unity 2022.3: StripUnused = 0, StripAll = 1, KeepAll = 2.
        SerializedProperty fog = graphics.FindProperty("m_FogStripping");
        if (fog == null) throw new Exception("Unity's fog stripping setting was not found.");
        fog.intValue = 1; // Custom: asset bundles must also support the game's runtime fog modes.
        foreach (string name in new[] { "m_FogKeepLinear", "m_FogKeepExp", "m_FogKeepExp2" })
        {
            SerializedProperty keep = graphics.FindProperty(name);
            if (keep == null) throw new Exception("Unity's fog mode setting was not found: " + name);
            keep.boolValue = true;
        }
        graphics.ApplyModifiedPropertiesWithoutUndo();

        // URP 14's GlobalSettings type is internal. Pin its serialized settings rather
        // than changing the game's pipeline or relying on a player-side included shader.
        const string globalPath = Dir + "BuildGlobalSettings.asset";
        RenderPipelineGlobalSettings global = AssetDatabase.LoadAssetAtPath<RenderPipelineGlobalSettings>(globalPath);
        if (global == null)
        {
            Type type = typeof(UniversalRenderPipelineAsset).Assembly.GetType(
                "UnityEngine.Rendering.Universal.UniversalRenderPipelineGlobalSettings", true);
            global = (RenderPipelineGlobalSettings)ScriptableObject.CreateInstance(type);
            AssetDatabase.CreateAsset(global, globalPath);
        }
        var settings = new SerializedObject(global);
        SerializedProperty stripVariants = settings.FindProperty("m_StripUnusedVariants");
        SerializedProperty stripPost = settings.FindProperty("m_StripUnusedPostProcessingVariants");
        if (stripVariants == null || stripPost == null) throw new Exception("URP 14 shader stripping settings were not found.");
        stripVariants.boolValue = false;
        stripPost.boolValue = false;
        settings.ApplyModifiedPropertiesWithoutUndo();
        GraphicsSettings.RegisterRenderPipelineSettings<UniversalRenderPipeline>(global);
    }

    private static string Argument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, name);
        if (at < 0 || at + 1 >= args.Length || !Directory.Exists(args[at + 1])) throw new Exception(name + " <folder> is required.");
        return args[at + 1];
    }

    private static GameObject Child(GameObject parent, string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        return child;
    }

    private static Material NewMaterial(Shader shader, string name, float sway, float cull, float detail)
    {
        var material = new Material(shader) { name = name };
        material.SetFloat("_Sway", sway);
        material.SetFloat("_Cull", cull);
        material.SetFloat("_Detail", detail);
        AssetDatabase.CreateAsset(material, Dir + name + ".mat");
        return material;
    }

    // ---- source files ----------------------------------------------------------------------------------------------

    /// <summary>The whole map's heights in metres, row 0 south: 16-bit samples over heightFloor .. heightFloor + heightSpan.</summary>
    private static float[] ReadHeights(string path, MapInfo info)
    {
        byte[] raw = File.ReadAllBytes(path);
        if (raw.Length != info.res * info.res * 2) throw new Exception(Path.GetFileName(path) + " is not " + info.res + " x " + info.res + " 16-bit samples.");
        var heights = new float[info.res * info.res];
        for (int i = 0; i < heights.Length; i++)
            heights[i] = (raw[i * 2] | (raw[i * 2 + 1] << 8)) / 65535f * info.heightSpan + info.heightFloor;
        return heights;
    }

    /// <summary>Vertex colours from the generator's raw RGB picture of the ground and seabed. It is authored as it should
    /// look on screen (sRGB); the game shades in linear space, so each channel is converted here.</summary>
    private static Color32[] ReadColours(string path, int res)
    {
        byte[] raw = File.ReadAllBytes(path);
        if (raw.Length != res * res * 3) throw new Exception(Path.GetFileName(path) + " is not " + res + " x " + res + " RGB bytes.");
        var linear = new byte[256];
        for (int i = 0; i < 256; i++) linear[i] = (byte)Mathf.RoundToInt(Mathf.GammaToLinearSpace(i / 255f) * 255f);
        var colours = new Color32[res * res];
        for (int i = 0; i < colours.Length; i++) colours[i] = new Color32(linear[raw[i * 3]], linear[raw[i * 3 + 1]], linear[raw[i * 3 + 2]], 0);
        return colours;
    }

    // ---- terrain ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// One continuous ground for the whole map, seabed included, cut into square tiles. A tile keeps every sample where
    /// there is land, every second one over shallows, and far fewer over deep water, where nobody sees or touches it.
    /// Every tile has a collider, so nothing that sinks falls through the world.
    /// </summary>
    private static void BuildTerrain(GameObject parent, MapInfo info, float[] heights, Color32[] colours, Material material)
    {
        int res = info.res, tile = info.tile, block = tile * 8;
        groundHeights = heights; groundColours = colours; groundRes = res; groundCell = info.cell; groundPads = info.strips ?? new StripInfo[0];
        // ponytail: one land stride for the whole map, the finest that fits the vertex budget; per-tile strides if mountains need more
        long land = 0;
        for (int z0 = 0; z0 < res; z0 += tile)
            for (int x0 = 0; x0 < res; x0 += tile)
                if (Top(heights, res, x0, z0, Mathf.Min(x0 + tile, res - 1), Mathf.Min(z0 + tile, res - 1)) > 0f) land++;
        landStride = .5f;
        while (landStride < 4f && land * (tile / landStride + 1) * (tile / landStride + 1) > (info.vertexBudget > 0 ? info.vertexBudget : 3300000)) landStride *= 2f;   // near-LOD land vertices in the whole map
        // Deep water is drawn and collided in blocks of 8 x 8 tiles: thousands of separate deep tiles cost the game
        // frame time for ground nobody sees. Only blocks that hold shallows or land are cut into tiles.
        for (int bz = 0; bz < res; bz += block)
            for (int bx = 0; bx < res; bx += block)
            {
                if (Top(heights, res, bx, bz, Mathf.Min(bx + block, res - 1), Mathf.Min(bz + block, res - 1)) <= -150f)
                {
                    Piece(parent, info, heights, colours, material, $"deep_{bx / block}_{bz / block}", bx, bz, block, 32);
                    continue;
                }
                for (int z0 = bz; z0 < Mathf.Min(bz + block, res); z0 += tile)
                    for (int x0 = bx; x0 < Mathf.Min(bx + block, res); x0 += tile)
                    {
                        float top = Top(heights, res, x0, z0, Mathf.Min(x0 + tile, res - 1), Mathf.Min(z0 + tile, res - 1));
                        Piece(parent, info, heights, colours, material, $"tile_{x0 / tile}_{z0 / tile}", x0, z0, tile, top > 0f ? 1 : top > -40f ? 2 : top > -150f ? 8 : 32);
                    }
            }
    }

    private static float Top(float[] heights, int res, int x0, int z0, int x1, int z1)
    {
        float top = float.MinValue;
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++) top = Mathf.Max(top, heights[z * res + x]);
        return top;
    }

    private static void Piece(GameObject parent, MapInfo info, float[] heights, Color32[] colours, Material material, string name, int x0, int z0, int size, int step)
    {
        int res = info.res, x1 = Mathf.Min(x0 + size, res - 1), z1 = Mathf.Min(z0 + size, res - 1);
        float half = res * info.cell * .5f;
        var go = Child(parent, name);
        // A piece's origin is its own south-west sample, in map coordinates: small numbers keep float precision.
        go.transform.localPosition = new Vector3(-half + (x0 + .5f) * info.cell, 0f, -half + (z0 + .5f) * info.cell);
        Mesh near = GridMesh(name, x0, z0, x1, z1, step == 1 ? landStride : step);
        go.AddComponent<MeshFilter>().sharedMesh = near;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        go.AddComponent<MeshCollider>().sharedMesh = near;
        chunkCount++;
        triangleCount += near.triangles.Length / 3;
        if (step != 1)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return;
        }
        landTiles++;
        var low = Child(go, "lod1");
        low.AddComponent<MeshFilter>().sharedMesh = GridMesh(name + "_lod1", x0, z0, x1, z1, Mathf.Max(4f, landStride * 4f));
        var lowRenderer = low.AddComponent<MeshRenderer>();
        lowRenderer.sharedMaterial = material;
        lowRenderer.shadowCastingMode = ShadowCastingMode.Off;
        go.AddComponent<LODGroup>().SetLODs(new[] { new LOD(.1f, new Renderer[] { renderer }), new LOD(.0005f, new Renderer[] { lowRenderer }) });
    }

    // The ground being cut into pieces: set once by BuildTerrain, read by every mesh.
    private static float[] groundHeights;
    private static Color32[] groundColours;
    private static int groundRes;
    private static float groundCell;
    private static StripInfo[] groundPads;
    private static float landStride = .5f;

    /// <summary>The generator's height between its samples.</summary>
    private static float Base(float fx, float fz)
    {
        fx = Mathf.Clamp(fx, 0f, groundRes - 1); fz = Mathf.Clamp(fz, 0f, groundRes - 1);
        int x = Mathf.Min((int)fx, groundRes - 2), z = Mathf.Min((int)fz, groundRes - 2);
        float tx = fx - x, tz = fz - z;
        float south = Mathf.Lerp(groundHeights[z * groundRes + x], groundHeights[z * groundRes + x + 1], tx);
        float north = Mathf.Lerp(groundHeights[(z + 1) * groundRes + x], groundHeights[(z + 1) * groundRes + x + 1], tx);
        return Mathf.Lerp(south, north, tz);
    }

    /// <summary>
    /// The height the mesh uses. On dry land it adds three scales of small relief (40 m, 17 m and 7 m across) that the
    /// generator's 20 m samples cannot hold: little on flats, more where the ground is steep, none on a strip's pad.
    /// It is a function of map position only, so neighbouring pieces agree along their shared edge.
    /// </summary>
    private static float Height(float fx, float fz, bool detail)
    {
        float h = Base(fx, fz);
        if (!detail || h < 1.5f) return h;
        float sx = Base(fx + 1f, fz) - Base(fx - 1f, fz), sz = Base(fx, fz + 1f) - Base(fx, fz - 1f);
        float slope = Mathf.Sqrt(sx * sx + sz * sz) / (2f * groundCell);
        float wx = fx * groundCell, wz = fz * groundCell;
        float relief = (Mathf.PerlinNoise(wx / 41f, wz / 41f) - .5f) + .5f * (Mathf.PerlinNoise(wx / 17f + 31f, wz / 17f + 7f) - .5f) +
            .25f * (Mathf.PerlinNoise(wx / 7f + 3f, wz / 7f + 59f) - .5f);
        return h + relief * Mathf.Clamp01((h - 1.5f) / 3f) * (.5f + 5f * Mathf.Clamp01(slope / .7f)) * OffPads(wx, wz);
    }

    /// <summary>0 on a strip's pad and 60 m around it, rising to 1 by 160 m out.</summary>
    private static float OffPads(float wx, float wz)
    {
        float half = groundRes * groundCell * .5f, least = 1f;
        foreach (StripInfo pad in groundPads)
        {
            float a = pad.heading * Mathf.Deg2Rad, rx = wx - half - pad.x, rz = wz - half - pad.z;
            float dx = Mathf.Max(0f, Mathf.Abs(rx * Mathf.Sin(a) + rz * Mathf.Cos(a)) - pad.padLength * .5f);
            float dz = Mathf.Max(0f, Mathf.Abs(rx * Mathf.Cos(a) - rz * Mathf.Sin(a)) - pad.padWidth * .5f);
            least = Mathf.Min(least, Mathf.Clamp01((Mathf.Sqrt(dx * dx + dz * dz) - 60f) / 100f));
        }
        return least;
    }

    /// <summary>
    /// One piece of ground from sample (x0, z0) to (x1, z1), a vertex every `stride` samples (0.5 on land, which is
    /// where the extra relief goes). It carries what the game's terrain shader may read: normals, tangents and a UV that
    /// is the vertex's place on the whole map, 0..1; and a vertex colour for the mod's own fallback shader.
    /// </summary>
    private static Mesh GridMesh(string name, int x0, int z0, int x1, int z1, float stride)
    {
        List<float> xs = Samples(x0, x1, stride), zs = Samples(z0, z1, stride);
        bool detail = stride <= landStride;
        var vertices = new Vector3[xs.Count * zs.Count];
        var normals = new Vector3[vertices.Length];
        var tangents = new Vector4[vertices.Length];
        var uvs = new Vector2[vertices.Length];
        var tint = new Color32[vertices.Length];
        float reach = Mathf.Max(.5f, stride * .5f);
        for (int j = 0; j < zs.Count; j++)
            for (int i = 0; i < xs.Count; i++)
            {
                float x = xs[i], z = zs[j];
                int at = j * xs.Count + i;
                vertices[at] = new Vector3((x - x0) * groundCell, Height(x, z, detail), (z - z0) * groundCell);
                float dx = Height(x + reach, z, detail) - Height(x - reach, z, detail), dz = Height(x, z + reach, detail) - Height(x, z - reach, detail);
                float run = 2f * reach * groundCell;
                normals[at] = new Vector3(-dx, run, -dz).normalized;
                Vector3 east = new Vector3(run, dx, 0f).normalized;
                tangents[at] = new Vector4(east.x, east.y, east.z, -1f);
                uvs[at] = new Vector2((x + .5f) / groundRes, (z + .5f) / groundRes);
                tint[at] = groundColours[Mathf.Min(groundRes - 1, Mathf.RoundToInt(z)) * groundRes + Mathf.Min(groundRes - 1, Mathf.RoundToInt(x))];
            }
        var triangles = new int[(xs.Count - 1) * (zs.Count - 1) * 6];
        int t = 0;
        for (int j = 0; j < zs.Count - 1; j++)
            for (int i = 0; i < xs.Count - 1; i++)
            {
                int a = j * xs.Count + i, b = a + 1, c = a + xs.Count, d = c + 1;
                triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
            }
        return Keep(new Mesh { name = name, vertices = vertices, normals = normals, tangents = tangents, uv = uvs, colors32 = tint, triangles = triangles });
    }

    /// <summary>Every `stride` from first to last, always ending on last so neighbouring pieces meet.</summary>
    private static List<float> Samples(int first, int last, float stride)
    {
        var list = new List<float>();
        for (float at = first; at < last - .001f; at += stride) list.Add(at);
        list.Add(last);
        return list;
    }

    private static Mesh Keep(Mesh mesh)
    {
        mesh.RecalculateBounds();
        meshes.Add(mesh);
        return mesh;
    }

    private sealed class Builder
    {
        public readonly List<Vector3> Vertices = new List<Vector3>(), Normals = new List<Vector3>();
        public readonly List<Color32> Colours = new List<Color32>();
        public readonly List<int> Triangles = new List<int>();

        public int Add(Vector3 p, Vector3 normal, Color colour, float sway)
        {
            Color lit = new Color(colour.r, colour.g, colour.b).linear;
            Vertices.Add(p); Normals.Add(normal); Colours.Add(new Color(lit.r, lit.g, lit.b, sway));
            return Vertices.Count - 1;
        }

        public void Quad(int a, int b, int c, int d) { Triangles.AddRange(new[] { a, b, c, a, c, d }); }
    }

    // ---- roads --------------------------------------------------------------------------------------------------------

    /// <summary>The height of the drawn land mesh itself (its triangles, not the smooth field), so a road lies on what is drawn.</summary>
    private static float Surface(float fx, float fz)
    {
        float s = landStride, gx = Mathf.Floor(fx / s) * s, gz = Mathf.Floor(fz / s) * s, u = (fx - gx) / s, v = (fz - gz) / s;
        float a = Height(gx, gz, true), b = Height(gx + s, gz, true), c = Height(gx, gz + s, true), d = Height(gx + s, gz + s, true);
        return u + v <= 1f ? a + u * (b - a) + v * (c - a) : d + (1f - u) * (c - d) + (1f - v) * (b - d);
    }

    private static Vector2 Curve(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        return .5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * (t * t) + (3f * p1 - p0 - 3f * p2 + p3) * (t * t * t));
    }

    /// <summary>One ribbon per road, a cross-section every 10 m along a smooth curve through its points: asphalt with
    /// gravel shoulders, or a dirt track with two ruts. Looks only; wheels run on the ground under it.</summary>
    private static void BuildRoads(GameObject parent, MapInfo info, RoadInfo[] roads, Material material)
    {
        float half = info.res * info.cell * .5f;
        float[] across = { -1f, -.8f, 0f, .8f, 1f };
        int n = 0;
        foreach (RoadInfo road in roads ?? new RoadInfo[0])
        {
            int count = road.points != null ? road.points.Length / 3 : 0;
            if (count < 2) continue;
            bool hard = road.kind == "paved", water = road.kind == "river";
            float width = road.width > 0f ? road.width : hard ? 4.5f : 2.6f;   // half the road's width
            Color edge = hard ? new Color(.34f, .33f, .30f) : new Color(.42f, .36f, .26f), lane = hard ? new Color(.20f, .20f, .21f) : new Color(.30f, .24f, .17f),
                mid = hard ? new Color(.27f, .27f, .27f) : new Color(.46f, .39f, .28f);
            if (water) { edge = new Color(.34f, .40f, .36f); lane = new Color(.10f, .24f, .30f); mid = new Color(.07f, .19f, .27f); }
            Func<int, Vector2> point = i => { i = Mathf.Clamp(i, 0, count - 1); return new Vector2(road.points[i * 3], road.points[i * 3 + 2]); };
            Vector2 origin = point(0);
            var b = new Builder();
            int previous = -1;
            for (int i = 0; i < count - 1; i++)
            {
                Vector2 p0 = point(i - 1), p1 = point(i), p2 = point(i + 1), p3 = point(i + 2);
                int steps = Mathf.Max(1, Mathf.CeilToInt((p2 - p1).magnitude / 10f));
                for (int k = i == 0 ? 0 : 1; k <= steps; k++)
                {
                    float t = k / (float)steps;
                    Vector2 at = Curve(p0, p1, p2, p3, t), ahead = Curve(p0, p1, p2, p3, Mathf.Min(1f, t + .05f)) - Curve(p0, p1, p2, p3, Mathf.Max(0f, t - .05f));
                    Vector2 side = new Vector2(ahead.y, -ahead.x).normalized;
                    int first = b.Vertices.Count;
                    if (road.widths != null && road.widths.Length == count) width = Mathf.Lerp(road.widths[i], road.widths[i + 1], t);
                    // A river lies level from bank to bank at its middle's height, a little under a road that crosses it.
                    float level = Surface((at.x + half) / info.cell - .5f, (at.y + half) / info.cell - .5f) + .18f;
                    for (int c = 0; c < 5; c++)
                    {
                        Vector2 q = at + side * (across[c] * width);
                        float y = water ? level : Surface((q.x + half) / info.cell - .5f, (q.y + half) / info.cell - .5f) + .3f;
                        b.Add(new Vector3(q.x - origin.x, y, q.y - origin.y), Vector3.up, c == 0 || c == 4 ? edge : c == 2 ? mid : lane, 0f);
                    }
                    if (previous >= 0)
                        for (int c = 0; c < 4; c++) b.Quad(previous + c, first + c, first + c + 1, previous + c + 1);
                    previous = first;
                }
            }
            var go = Child(parent, (water ? "river_" : "road_") + n++);
            go.transform.localPosition = new Vector3(origin.x, 0f, origin.y);
            var mesh = new Mesh { name = go.name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(b.Vertices);
            mesh.SetNormals(b.Normals);
            mesh.SetColors(b.Colours);
            mesh.SetTriangles(b.Triangles, 0);
            go.AddComponent<MeshFilter>().sharedMesh = Keep(mesh);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    /// <summary>A bridge where a road crosses a river: a concrete deck from bank to bank just over the road, with
    /// parapets and an abutment at each end. Looks only: the river bed is not carved here, so wheels cross on the ground.</summary>
    private static void BuildBridges(GameObject parent, MapInfo info, Material material)
    {
        float half = info.res * info.cell * .5f;
        int n = 0;
        foreach (BridgeInfo bridge in info.bridges ?? new BridgeInfo[0])
        {
            float reach = bridge.length * .5f, side = bridge.width * .5f, a = bridge.heading * Mathf.Deg2Rad;
            Func<float, float> ground = along => Surface((bridge.x + Mathf.Sin(a) * along + half) / info.cell - .5f, (bridge.z + Mathf.Cos(a) * along + half) / info.cell - .5f);
            float back = ground(-reach), front = ground(reach), middle = (back + front) * .5f + .45f;
            var go = Child(parent, "bridge_" + n++);
            go.transform.localPosition = new Vector3(bridge.x, middle, bridge.z);
            go.transform.localRotation = Quaternion.Euler(Mathf.Atan2(back - front, bridge.length) * Mathf.Rad2Deg, bridge.heading, 0f); // local +z runs along the road
            var b = new Builder();
            Color deck = new Color(.44f, .44f, .42f), wall = new Color(.56f, .55f, .52f), pier = new Color(.36f, .36f, .35f);
            Block(b, new Vector3(0f, -.2f, 0f), new Vector3(side, .25f, reach), deck);
            foreach (float x in new[] { -side + .15f, side - .15f }) Block(b, new Vector3(x, .5f, 0f), new Vector3(.15f, .45f, reach), wall);
            foreach (float z in new[] { -reach + 1f, reach - 1f }) Block(b, new Vector3(0f, -1.9f, z), new Vector3(side + .4f, 1.5f, 1f), pier);
            if (bridge.length > 44f) Block(b, new Vector3(0f, -1.9f, 0f), new Vector3(side * .7f, 1.5f, .8f), pier);
            var mesh = new Mesh { name = go.name };
            mesh.SetVertices(b.Vertices);
            mesh.SetNormals(b.Normals);
            mesh.SetColors(b.Colours);
            mesh.SetTriangles(b.Triangles, 0);
            go.AddComponent<MeshFilter>().sharedMesh = Keep(mesh);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
    }

    /// <summary>A box from its centre and half-extents; the faces are flat shaded and seen from outside.</summary>
    private static void Block(Builder b, Vector3 c, Vector3 h, Color colour)
    {
        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
        for (int axis = 0; axis < 3; axis++)
            foreach (float sign in new[] { -1f, 1f })
            {
                Vector3 normal = axes[axis] * sign, u = axes[(axis + 1) % 3], v = axes[(axis + 2) % 3];
                Vector3 centre = c + Vector3.Scale(normal, h);
                Vector3 du = u * Vector3.Dot(u, h), dv = v * Vector3.Dot(v, h);
                Color lit = colour * (axis == 1 ? 1f : .86f);
                int p0 = b.Add(centre - du - dv, normal, lit, 0f), p1 = b.Add(centre + du - dv, normal, lit, 0f),
                    p2 = b.Add(centre + du + dv, normal, lit, 0f), p3 = b.Add(centre - du + dv, normal, lit, 0f);
                if (sign > 0f) b.Quad(p0, p1, p2, p3); else b.Quad(p0, p3, p2, p1);
            }
    }

    // ---- airstrips ---------------------------------------------------------------------------------------------------

    /// <summary>A runway with centre-line and threshold marks and an apron beside it, lying just above the flattened pad.</summary>
    private static void BuildStrip(GameObject parent, int n, StripInfo strip, Material material)
    {
        var go = Child(parent, "strip_" + n);
        go.transform.localPosition = new Vector3(strip.x, strip.y, strip.z);
        go.transform.localRotation = Quaternion.Euler(0f, strip.heading, 0f); // local +z runs along the runway
        var b = new Builder();
        bool marked = strip.surface == "asphalt";
        var asphalt = marked ? new Color(.13f, .13f, .14f) : strip.surface == "ice" ? new Color(.62f, .74f, .82f) : new Color(.36f, .33f, .29f);
        var paint = new Color(.82f, .82f, .78f);
        float half = strip.length * .5f;
        Slab(b, new Vector3(0f, .12f, 0f), 22f, half, asphalt);
        Slab(b, new Vector3(-72f, .12f, 0f), 50f, 110f, asphalt * 1.15f);          // apron
        Slab(b, new Vector3(-36f, .12f, 0f), 14f, 12f, asphalt * 1.15f);           // its link to the runway
        for (float z = -half + 90f; marked && z < half - 90f; z += 60f) Slab(b, new Vector3(0f, .16f, z), .45f, 15f, paint);
        foreach (float end in new[] { -half + 14f, half - 14f })
            for (int bar = -4; marked && bar <= 4; bar++)
                if (bar != 0) Slab(b, new Vector3(bar * 4.2f, .16f, end), .9f, 10f, paint);
        var mesh = new Mesh { name = go.name };
        mesh.SetVertices(b.Vertices);
        mesh.SetNormals(b.Normals);
        mesh.SetColors(b.Colours);
        mesh.SetTriangles(b.Triangles, 0);
        go.AddComponent<MeshFilter>().sharedMesh = Keep(mesh);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        // Wheels roll on one flat slab, not on the paint.
        var surface = Child(go, "surface");
        var flat = new Builder();
        Slab(flat, new Vector3(0f, .12f, 0f), 22f, half, asphalt);
        Slab(flat, new Vector3(-61f, .12f, 0f), 61f, 110f, asphalt);
        var collision = new Mesh { name = go.name + "_surface" };
        collision.SetVertices(flat.Vertices);
        collision.SetTriangles(flat.Triangles, 0);
        surface.AddComponent<MeshCollider>().sharedMesh = Keep(collision);
    }

    private static void Slab(Builder b, Vector3 centre, float halfWidth, float halfLength, Color colour)
    {
        int a = b.Add(centre + new Vector3(-halfWidth, 0f, -halfLength), Vector3.up, colour, 0f);
        int c = b.Add(centre + new Vector3(-halfWidth, 0f, halfLength), Vector3.up, colour, 0f);
        int d = b.Add(centre + new Vector3(halfWidth, 0f, halfLength), Vector3.up, colour, 0f);
        int e = b.Add(centre + new Vector3(halfWidth, 0f, -halfLength), Vector3.up, colour, 0f);
        b.Quad(a, c, d, e);
    }

    // ---- import settings and the final check -----------------------------------------------------------------------

    private static void ImportTexture(string path, bool srgb, bool readable, bool sprite, int maxSize = 2048, bool normal = false)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new Exception(path + " was not imported as a texture.");
        importer.textureType = sprite ? TextureImporterType.Sprite : normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (sprite) { importer.spriteImportMode = SpriteImportMode.Single; importer.spritePixelsPerUnit = 100f; }
        importer.sRGBTexture = srgb;
        importer.isReadable = readable;
        importer.mipmapEnabled = !sprite;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = maxSize;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = readable ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    private static void Validate(string prefabPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) throw new Exception("MapRoot prefab was not saved.");
        if (AssetDatabase.LoadAssetAtPath<TextAsset>(Dir + "map.json") == null) throw new Exception("map.json is not a TextAsset.");
        Transform terrain = prefab.transform.Find("Terrain");
        if (terrain == null || terrain.childCount == 0) throw new Exception("The map has no terrain chunks.");
        foreach (Transform chunk in terrain)
        {
            var filter = chunk.GetComponent<MeshFilter>();
            var collider = chunk.GetComponent<MeshCollider>();
            if (filter == null || filter.sharedMesh == null || chunk.GetComponent<MeshRenderer>() == null || collider == null || collider.sharedMesh == null)
                throw new Exception("Terrain chunk " + chunk.name + " lacks a mesh, renderer or collider on the same object.");
        }
        foreach (Component component in prefab.GetComponentsInChildren<Component>(true))
            if (component == null || !component.GetType().Namespace.StartsWith("UnityEngine", StringComparison.Ordinal))
                throw new Exception("The prefab holds a non-engine component: " + (component == null ? "missing script" : component.GetType().FullName));
    }
}
