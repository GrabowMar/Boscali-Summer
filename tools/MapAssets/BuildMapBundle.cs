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
    private const long BundleLimit = 40L * 1024 * 1024;

    [Serializable] private class StripInfo { public float x, y, z, length; public int heading; }
    [Serializable] private class MapInfo
    {
        public string id, displayName; public float heightFloor, heightSpan, cell; public int res, tile, islandCount; public StripInfo[] strips;
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
            Material scenery = NewMaterial(shader, "MapScenery", .35f, 0f, .2f);
            Material paved = NewMaterial(shader, "MapPaved", 0f, 2f, .12f);

            var root = new GameObject("MapRoot");
            var terrain = Child(root, "Terrain");
            var props = Child(root, "Props");
            var airfields = Child(root, "Airfields");
            int trees = (int)(new FileInfo(Path.Combine(source, "trees.bin")).Length / 16);
            BuildPrototypes(props, scenery);
            BuildTerrain(terrain, info, ReadHeights(Path.Combine(source, "height.r16"), info), ReadColours(Path.Combine(source, "colour.rgb"), info.res), ground);
            for (int n = 0; n < info.strips.Length; n++) BuildStrip(airfields, n, info.strips[n], paved);

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
            Directory.CreateDirectory("BundleOutput");
            var build = new AssetBundleBuild
            {
                assetBundleName = info.id + ".bsmap",
                assetNames = new[] { prefabPath, Dir + "map.json", Dir + "trees.bytes", Dir + "ocean_depth.png", Dir + "ocean_color.png", Dir + "colormap.png", Dir + image },
            };
            if (BuildPipeline.BuildAssetBundles("BundleOutput", new[] { build },
                BuildAssetBundleOptions.StrictMode, BuildTarget.StandaloneWindows64) == null) // LZMA: a height field packs far smaller than with LZ4
                throw new Exception("Map bundle build returned no manifest.");
            long bytes = new FileInfo("BundleOutput/" + info.id + ".bsmap").Length;
            if (bytes > BundleLimit) throw new Exception("Map bundle exceeds 40 MiB: " + bytes + " bytes.");
            File.WriteAllText("result.txt", $"PASS: {info.displayName}: {info.islandCount} islands, {chunkCount} terrain tiles ({landTiles} with land), " +
                $"{triangleCount} triangles, {trees} scenery items, {info.strips.Length} strips; bundle {bytes} bytes.");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            File.WriteAllText("result.txt", "FAIL: " + error);
            EditorApplication.Exit(1);
        }
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
        int res = info.res, tile = info.tile;
        float half = res * info.cell * .5f;
        for (int z0 = 0; z0 < res; z0 += tile)
            for (int x0 = 0; x0 < res; x0 += tile)
            {
                int x1 = Mathf.Min(x0 + tile, res - 1), z1 = Mathf.Min(z0 + tile, res - 1);
                float top = float.MinValue;
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++) top = Mathf.Max(top, heights[z * res + x]);
                int step = top > 0f ? 1 : top > -40f ? 2 : top > -150f ? 8 : 32;
                var go = Child(parent, $"tile_{x0 / tile}_{z0 / tile}");
                // A tile's origin is its own south-west sample, in map coordinates: small numbers keep float precision.
                go.transform.localPosition = new Vector3(-half + (x0 + .5f) * info.cell, 0f, -half + (z0 + .5f) * info.cell);
                Mesh near = GridMesh(go.name, heights, colours, res, info.cell, x0, z0, x1, z1, step);
                go.AddComponent<MeshFilter>().sharedMesh = near;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                go.AddComponent<MeshCollider>().sharedMesh = near;
                chunkCount++;
                triangleCount += near.triangles.Length / 3;
                if (step != 1)
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    continue;
                }
                landTiles++;
                var low = Child(go, "lod1");
                low.AddComponent<MeshFilter>().sharedMesh = GridMesh(go.name + "_lod1", heights, colours, res, info.cell, x0, z0, x1, z1, 4);
                var lowRenderer = low.AddComponent<MeshRenderer>();
                lowRenderer.sharedMaterial = material;
                lowRenderer.shadowCastingMode = ShadowCastingMode.Off;
                go.AddComponent<LODGroup>().SetLODs(new[] { new LOD(.1f, new Renderer[] { renderer }), new LOD(.0005f, new Renderer[] { lowRenderer }) });
            }
    }

    private static Mesh GridMesh(string name, float[] heights, Color32[] colours, int res, float cell, int x0, int z0, int x1, int z1, int step)
    {
        var xs = Samples(x0, x1, step);
        var zs = Samples(z0, z1, step);
        var vertices = new Vector3[xs.Count * zs.Count];
        var normals = new Vector3[vertices.Length];
        var tint = new Color32[vertices.Length];
        for (int j = 0; j < zs.Count; j++)
            for (int i = 0; i < xs.Count; i++)
            {
                int x = xs[i], z = zs[j], at = j * xs.Count + i;
                vertices[at] = new Vector3((x - x0) * cell, heights[z * res + x], (z - z0) * cell);
                // Normals come from the whole island's heights, so neighbouring chunks agree along their shared edge.
                float dx = heights[z * res + Mathf.Min(x + 1, res - 1)] - heights[z * res + Mathf.Max(x - 1, 0)];
                float dz = heights[Mathf.Min(z + 1, res - 1) * res + x] - heights[Mathf.Max(z - 1, 0) * res + x];
                normals[at] = new Vector3(-dx, 2f * cell, -dz).normalized;
                tint[at] = colours[z * res + x];
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
        return Keep(new Mesh { name = name, vertices = vertices, normals = normals, colors32 = tint, triangles = triangles });
    }

    /// <summary>Every `step`-th sample from first to last, always ending on last so chunk edges meet.</summary>
    private static List<int> Samples(int first, int last, int step)
    {
        var list = new List<int>();
        for (int i = first; i < last; i += step) list.Add(i);
        list.Add(last);
        return list;
    }

    private static Mesh Keep(Mesh mesh)
    {
        mesh.RecalculateBounds();
        meshes.Add(mesh);
        return mesh;
    }

    // ---- scenery: palms, jungle trees, rocks, merged per island ---------------------------------------------------

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

    /// <summary>A few of each kind, hidden under Props. The mod places them from the tree list when it loads the map,
    /// which keeps thousands of trees out of the bundle. Names are kind + number: palm0.., tree0.., rock0..</summary>
    private static void BuildPrototypes(GameObject parent, Material material)
    {
        var random = new System.Random(4242);
        for (int kind = 0; kind < 3; kind++)
            for (int n = 0; n < 5; n++)
            {
                var builder = new Builder();
                if (kind == 0) Palm(builder, random, Vector3.zero);
                else if (kind == 1) JungleTree(builder, random, Vector3.zero);
                else Rock(builder, random, Vector3.zero);
                var go = Child(parent, new[] { "palm", "tree", "rock" }[kind] + n);
                var mesh = new Mesh { name = go.name };
                mesh.SetVertices(builder.Vertices);
                mesh.SetNormals(builder.Normals);
                mesh.SetColors(builder.Colours);
                mesh.SetTriangles(builder.Triangles, 0);
                go.AddComponent<MeshFilter>().sharedMesh = Keep(mesh);
                go.AddComponent<MeshRenderer>().sharedMaterial = material;
                go.SetActive(false);
            }
    }

    private static float Range(System.Random random, float low, float high) { return low + (float)random.NextDouble() * (high - low); }

    private static void Palm(Builder b, System.Random random, Vector3 at)
    {
        float height = Range(random, 7f, 12f), lean = Range(random, .6f, 2.4f), turn = Range(random, 0f, Mathf.PI * 2f);
        var leanTo = new Vector3(Mathf.Cos(turn), 0f, Mathf.Sin(turn));
        var bark = new Color(.47f, .39f, .28f) * Range(random, .85f, 1.1f);
        const int Sides = 5, Rings = 4;
        int first = b.Vertices.Count;
        for (int ring = 0; ring <= Rings; ring++)
        {
            float t = ring / (float)Rings, radius = Mathf.Lerp(.22f, .11f, t);
            Vector3 centre = at + Vector3.up * (height * t) + leanTo * (lean * t * t);
            for (int side = 0; side < Sides; side++)
            {
                float a = side * Mathf.PI * 2f / Sides;
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                b.Add(centre + outward * radius, outward, bark, t * t);
            }
        }
        for (int ring = 0; ring < Rings; ring++)
            for (int side = 0; side < Sides; side++)
            {
                int a = first + ring * Sides + side, c = first + ring * Sides + (side + 1) % Sides;
                b.Quad(a, a + Sides, c + Sides, c);
            }
        Vector3 crown = at + Vector3.up * height + leanTo * lean;
        int fronds = random.Next(7, 10);
        for (int f = 0; f < fronds; f++)
        {
            float a = turn + f * Mathf.PI * 2f / fronds + Range(random, -.25f, .25f), length = Range(random, 3.2f, 4.6f), droop = Range(random, 1.4f, 2.6f);
            var along = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var across = new Vector3(-along.z, 0f, along.x);
            var leaf = new Color(.30f, .52f, .20f) * Range(random, .8f, 1.2f);
            int previousLeft = -1, previousRight = -1;
            const int Segments = 4;
            for (int s = 0; s <= Segments; s++)
            {
                float t = s / (float)Segments, width = Mathf.Sin(Mathf.Lerp(.25f, 1f, t) * Mathf.PI) * .75f;
                Vector3 spine = crown + along * (length * t) + Vector3.up * (.9f * t - droop * t * t);
                var normal = (Vector3.up + along * (t - .3f)).normalized;
                int left = b.Add(spine - across * width, normal, leaf, 1f), right = b.Add(spine + across * width, normal, leaf, 1f);
                if (s > 0) b.Quad(previousLeft, left, right, previousRight);
                previousLeft = left; previousRight = right;
            }
        }
    }

    private static void JungleTree(Builder b, System.Random random, Vector3 at)
    {
        float height = Range(random, 7f, 13f), radius = Range(random, 3.2f, 5.5f);
        var canopy = new Color(.22f, .42f, .17f) * Range(random, .8f, 1.25f);
        Blob(b, random, at + Vector3.up * height, new Vector3(radius, radius * .62f, radius), canopy, .6f);
        var bark = new Color(.24f, .19f, .14f);
        int first = b.Vertices.Count;
        for (int ring = 0; ring < 2; ring++)
            for (int side = 0; side < 4; side++)
            {
                float a = side * Mathf.PI * .5f;
                var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                b.Add(at + Vector3.up * (ring * height) + outward * .3f, outward, bark, 0f);
            }
        for (int side = 0; side < 4; side++) b.Quad(first + side, first + 4 + side, first + 4 + (side + 1) % 4, first + (side + 1) % 4);
    }

    private static void Rock(Builder b, System.Random random, Vector3 at)
    {
        float size = Range(random, 1.2f, 4.5f);
        Blob(b, random, at + Vector3.up * (size * .3f), new Vector3(size, size * Range(random, .5f, .9f), size * Range(random, .7f, 1.3f)),
            new Color(.34f, .32f, .30f) * Range(random, .8f, 1.15f), 0f);
    }

    /// <summary>A lumpy ball: an octahedron split once, each corner pushed in or out a little.</summary>
    private static void Blob(Builder b, System.Random random, Vector3 centre, Vector3 radii, Color colour, float sway)
    {
        Vector3[] corners = { Vector3.up, Vector3.right, Vector3.forward, Vector3.left, Vector3.back, Vector3.down };
        int[] faces = { 0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 1, 4, 5, 1, 2, 5, 2, 3, 5, 3, 4, 5, 4, 1 };
        var bumps = new Dictionary<Vector3, float>();
        for (int f = 0; f < faces.Length; f += 3)
        {
            Vector3 p0 = corners[faces[f]], p1 = corners[faces[f + 1]], p2 = corners[faces[f + 2]];
            Vector3 m01 = (p0 + p1).normalized, m12 = (p1 + p2).normalized, m20 = (p2 + p0).normalized;
            foreach (Vector3[] triangle in new[] { new[] { p0, m01, m20 }, new[] { m01, p1, m12 }, new[] { m20, m12, p2 }, new[] { m01, m12, m20 } })
            {
                Vector3 flat = Vector3.zero;
                var points = new Vector3[3];
                for (int i = 0; i < 3; i++)
                {
                    if (!bumps.TryGetValue(triangle[i], out float bump)) bumps[triangle[i]] = bump = Range(random, .78f, 1.18f);
                    points[i] = Vector3.Scale(triangle[i] * bump, radii);
                }
                flat = Vector3.Cross(points[1] - points[0], points[2] - points[0]).normalized;
                float shade = .82f + .18f * Mathf.Max(0f, flat.y);
                int a = b.Add(centre + points[0], flat, colour * shade, sway), c = b.Add(centre + points[1], flat, colour * shade, sway),
                    d = b.Add(centre + points[2], flat, colour * shade, sway);
                b.Triangles.AddRange(new[] { a, c, d });
            }
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
        var asphalt = new Color(.13f, .13f, .14f);
        var paint = new Color(.82f, .82f, .78f);
        float half = strip.length * .5f;
        Slab(b, new Vector3(0f, .12f, 0f), 22f, half, asphalt);
        Slab(b, new Vector3(-72f, .12f, 0f), 50f, 110f, asphalt * 1.15f);          // apron
        Slab(b, new Vector3(-36f, .12f, 0f), 14f, 12f, asphalt * 1.15f);           // its link to the runway
        for (float z = -half + 90f; z < half - 90f; z += 60f) Slab(b, new Vector3(0f, .16f, z), .45f, 15f, paint);
        foreach (float end in new[] { -half + 14f, half - 14f })
            for (int bar = -4; bar <= 4; bar++)
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

    private static void ImportTexture(string path, bool srgb, bool readable, bool sprite)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new Exception(path + " was not imported as a texture.");
        importer.textureType = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
        if (sprite) { importer.spriteImportMode = SpriteImportMode.Single; importer.spritePixelsPerUnit = 100f; }
        importer.sRGBTexture = srgb;
        importer.isReadable = readable;
        importer.mipmapEnabled = !sprite;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.maxTextureSize = 2048;
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
