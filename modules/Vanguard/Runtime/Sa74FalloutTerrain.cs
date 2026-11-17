using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Effects;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Bounded height patches plus a conforming, persistent glass/scorch field. No map assets are saved.</summary>
    internal sealed class Sa74FalloutTerrain
    {
        private sealed class Patch
        {
            internal TerrainData Data;
            internal int X, Z;
            internal float[,] Before, Written;
        }
        private readonly List<Patch> patches = new List<Patch>();
        private readonly List<GameObject> scars = new List<GameObject>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private sealed class MeshPatch
        {
            internal MeshFilter Filter;
            internal MeshCollider Collider;
            internal Mesh Before, Written;
        }
        private readonly List<MeshPatch> meshPatches = new List<MeshPatch>();
        private Material glass;
        private int meshChangedCells;
        internal int Scars => scars.Count;
        internal int ChangedCells { get; private set; }
        internal int RestoredCells { get; private set; }

        internal void Scar(Vector3 center, float radius)
        {
            if (scars.Count >= Sa74Fallout.MaxSites) return;
            // A true ground burst excavates a bowl and raised ejecta rim on native Unity Terrains.
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                TerrainData data = terrain.terrainData;
                if (data == null || data.heightmapResolution < 3 || data.size.y <= 0f) continue;
                Vector3 local = center - terrain.transform.position;
                if (local.x + radius < 0f || local.z + radius < 0f || local.x - radius > data.size.x || local.z - radius > data.size.z) continue;
                // Airbursts high above the ground still scorch, but do not excavate terrain kilometres below them.
                if (Mathf.Abs(terrain.SampleHeight(center) + terrain.transform.position.y - center.y) > 500f) continue;
                int resolution = data.heightmapResolution;
                int x = Mathf.Clamp(Mathf.FloorToInt((local.x - radius) / data.size.x * (resolution - 1)), 0, resolution - 1);
                int z = Mathf.Clamp(Mathf.FloorToInt((local.z - radius) / data.size.z * (resolution - 1)), 0, resolution - 1);
                int maxX = Mathf.Clamp(Mathf.CeilToInt((local.x + radius) / data.size.x * (resolution - 1)), 0, resolution - 1);
                int maxZ = Mathf.Clamp(Mathf.CeilToInt((local.z + radius) / data.size.z * (resolution - 1)), 0, resolution - 1);
                int width = maxX - x + 1, height = maxZ - z + 1;
                if (width <= 1 || height <= 1) continue;
                // ponytail: at most 1024² cells per terrain/strike; no full-heightmap copy. Current game terrains are coarser.
                if (width > 1024 || height > 1024) continue;
                float[,] before = data.GetHeights(x, z, width, height);
                var after = (float[,])before.Clone();
                for (int row = 0; row < height; row++)
                    for (int col = 0; col < width; col++)
                    {
                        float dx = (x + col) * data.size.x / (resolution - 1) - local.x;
                        float dz = (z + row) * data.size.z / (resolution - 1) - local.z;
                        float distance = Mathf.Sqrt(dx * dx + dz * dz) / radius;
                        if (distance >= 1f) continue;
                        float bowl = -170f * Mathf.Pow(Mathf.Clamp01(1f - distance / .82f), 1.6f);
                        float rim = 32f * Mathf.Exp(-Mathf.Pow((distance - .80f) / .105f, 2f)) * Mathf.SmoothStep(0f, 1f, (1f - distance) * 8f);
                        after[row, col] = Mathf.Clamp01(before[row, col] + (bowl + rim) / data.size.y);
                        if (Mathf.Abs(after[row, col] - before[row, col]) > .000001f) ChangedCells++;
                    }
                patches.Add(new Patch { Data = data, X = x, Z = z, Before = before, Written = after });
                data.SetHeightsDelayLOD(x, z, after);
                data.SyncHeightmap();
            }
            ExcavateMeshes(center, radius);
            MakeScorch(center, radius * 2.5f);
        }

        private void ExcavateMeshes(Vector3 center, float radius)
        {
            TerrainHeightMap heightMap = Object.FindObjectOfType<TerrainHeightMap>();
            var classify = AccessTools.Method(typeof(TerrainHeightMap), "IsTerrain");
            var physicMaterials = new HashSet<PhysicMaterial>();
            if (GameAssets.i != null && GameAssets.i.terrainMaterial != null) physicMaterials.Add(GameAssets.i.terrainMaterial);
            if (heightMap != null && AccessTools.Field(typeof(TerrainHeightMap), "terrainPhysicMaterials")?.GetValue(heightMap) is PhysicMaterial[] native)
                foreach (PhysicMaterial material in native) if (material != null) physicMaterials.Add(material);
            var originals = new Dictionary<(Mesh, Matrix4x4), Mesh>();
            var terrainObjects = new HashSet<GameObject>();
            int vertexBudget = 1000000;
            foreach (MeshRenderer renderer in Object.FindObjectsOfType<MeshRenderer>())
            {
                if (renderer == null || renderer.bounds.SqrDistance(center) > radius * radius) continue;
                MeshCollider collider = renderer.GetComponent<MeshCollider>();
                bool ground = collider != null && physicMaterials.Contains(collider.sharedMaterial);
                if (!ground && heightMap != null && classify != null) ground = (bool)classify.Invoke(heightMap, new object[] { renderer });
                if (!ground) continue;
                terrainObjects.Add(renderer.gameObject);
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Mesh before = filter.sharedMesh;
                Mesh written = Deform(before, renderer.transform);
                if (written == null) continue;
                filter.sharedMesh = written;
                meshPatches.Add(new MeshPatch { Filter = filter, Before = before, Written = written });
            }
            foreach (MeshCollider collider in Object.FindObjectsOfType<MeshCollider>())
            {
                if (collider.sharedMesh == null || collider.bounds.SqrDistance(center) > radius * radius ||
                    (!physicMaterials.Contains(collider.sharedMaterial) && !terrainObjects.Contains(collider.gameObject))) continue;
                Mesh before = collider.sharedMesh;
                Mesh written = Deform(before, collider.transform);
                if (written == null) continue;
                collider.sharedMesh = written;
                meshPatches.Add(new MeshPatch { Collider = collider, Before = before, Written = written });
            }
            if (heightMap != null && originals.Count != 0) AccessTools.Method(typeof(TerrainHeightMap), "SetDirty")?.Invoke(heightMap, null);

            Mesh Deform(Mesh source, Transform transform)
            {
                // LODs and physics using the same terrain mesh receive the very same replacement.
                var identity = (source, transform.localToWorldMatrix);
                if (originals.TryGetValue(identity, out Mesh existing)) return existing;
                if (source.vertexCount > vertexBudget || source.vertexCount > 500000) return null;
                Mesh copy = ReadableCopy(source);
                if (copy == null) return null;
                Vector3[] points = copy.vertices;
                int changed = 0;
                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 world = transform.TransformPoint(points[i]);
                    float dx = world.x - center.x, dz = world.z - center.z;
                    float distance = Mathf.Sqrt(dx * dx + dz * dz) / radius;
                    if (distance >= 1f || Mathf.Abs(world.y - center.y) > 600f) continue;
                    float bowl = -170f * Mathf.Pow(Mathf.Clamp01(1f - distance / .82f), 1.6f);
                    float rim = 32f * Mathf.Exp(-Mathf.Pow((distance - .80f) / .105f, 2f)) * Mathf.SmoothStep(0f, 1f, (1f - distance) * 8f);
                    world.y += bowl + rim;
                    points[i] = transform.InverseTransformPoint(world);
                    changed++;
                }
                if (changed == 0) { Object.Destroy(copy); return null; }
                copy.vertices = points;
                copy.RecalculateNormals(); copy.RecalculateBounds();
                originals.Add(identity, copy);
                ChangedCells += changed;
                meshChangedCells += changed;
                vertexBudget -= source.vertexCount;
                return copy;
            }
        }

        /// <summary>Game map meshes discard their CPU data. Read only the touched tile's existing GPU buffers once.</summary>
        private static Mesh ReadableCopy(Mesh source)
        {
            if (source.isReadable) return Object.Instantiate(source);
            if (!SystemInfo.supportsAsyncGPUReadback || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            Mesh copy = null;
            try
            {
                copy = new Mesh { name = source.name + " / SA-74N crater" };
                copy.SetVertexBufferParams(source.vertexCount, source.GetVertexAttributes());
                for (int stream = 0; stream < source.vertexBufferCount; stream++)
                {
                    using (GraphicsBuffer buffer = source.GetVertexBuffer(stream))
                    {
                        if ((long)buffer.count * buffer.stride > 64000000) { Object.Destroy(copy); return null; }
                        var request = AsyncGPUReadback.Request(buffer);
                        request.WaitForCompletion();
                        if (request.hasError) { Object.Destroy(copy); return null; }
                        byte[] bytes = request.GetData<byte>().ToArray();
                        copy.SetVertexBufferData(bytes, 0, 0, bytes.Length, stream, MeshUpdateFlags.DontRecalculateBounds);
                    }
                }
                using (GraphicsBuffer buffer = source.GetIndexBuffer())
                {
                    if ((long)buffer.count * buffer.stride > 24000000) { Object.Destroy(copy); return null; }
                    var request = AsyncGPUReadback.Request(buffer);
                    request.WaitForCompletion();
                    if (request.hasError) { Object.Destroy(copy); return null; }
                    byte[] bytes = request.GetData<byte>().ToArray();
                    copy.SetIndexBufferParams(buffer.count, source.indexFormat);
                    copy.SetIndexBufferData(bytes, 0, 0, bytes.Length, MeshUpdateFlags.DontRecalculateBounds);
                }
                copy.subMeshCount = source.subMeshCount;
                for (int i = 0; i < source.subMeshCount; i++) copy.SetSubMesh(i, source.GetSubMesh(i), MeshUpdateFlags.DontRecalculateBounds);
                copy.bounds = source.bounds;
                return copy;
            }
            catch (System.Exception e)
            {
                if (copy != null) Object.Destroy(copy);
                Plugin.Logger?.LogWarning("[Vanguard] SA-74N terrain tile readback: " + e.Message);
                return null;
            }
        }

        private void MakeScorch(Vector3 center, float radius)
        {
            const int sectors = 64, rings = 12;
            var vertices = new Vector3[(sectors + 1) * (rings + 1)];
            var colors = new Color[vertices.Length];
            var triangles = new int[sectors * rings * 6];
            for (int ring = 0; ring <= rings; ring++)
                for (int sector = 0; sector <= sectors; sector++)
                {
                    float t = ring / (float)rings, angle = sector / (float)sectors * Mathf.PI * 2f;
                    float scallop = 1f + .025f * Mathf.Sin(angle * 13f) + .018f * Mathf.Cos(angle * 7f);
                    Vector3 point = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (t * radius * scallop);
                    float ground = Datum.LocalSeaY - 2f;
                    if (Physics.Raycast(point + Vector3.up * 3000f, Vector3.down, out RaycastHit hit, 7000f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                        ground = hit.point.y;
                    vertices[ring * (sectors + 1) + sector] = new Vector3(point.x - center.x, ground + 1.5f - center.y, point.z - center.z);
                    float hot = Mathf.Clamp01(1f - t * 3f);
                    Color color = Color.Lerp(new Color(.023f, .025f, .022f), new Color(.095f, .052f, .025f), hot);
                    color.a = Mathf.SmoothStep(.88f, 0f, Mathf.InverseLerp(.65f, 1f, t));
                    colors[ring * (sectors + 1) + sector] = color;
                }
            int index = 0;
            for (int ring = 0; ring < rings; ring++)
                for (int sector = 0; sector < sectors; sector++)
                {
                    int a = ring * (sectors + 1) + sector, b = a + sectors + 1;
                    triangles[index++] = a; triangles[index++] = b; triangles[index++] = a + 1;
                    triangles[index++] = a + 1; triangles[index++] = b; triangles[index++] = b + 1;
                }
            var mesh = new Mesh { name = "SA-74N fused ground", vertices = vertices, colors = colors, triangles = triangles };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); meshes.Add(mesh);
            if (glass == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
                if (shader != null)
                {
                    glass = new Material(shader) { name = "SA-74N scorched glass", renderQueue = 2990 };
                    glass.SetColor("_BaseColor", Color.white); glass.SetColor("_Color", Color.white);
                    glass.SetFloat("_Surface", 1f); glass.SetInt("_ZWrite", 0);
                    glass.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); glass.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    glass.SetInt("_Cull", (int)CullMode.Off); glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                }
            }
            var root = new GameObject("SA-74N permanent glass/scorch field");
            root.transform.SetParent(Datum.origin, false); root.transform.position = center;
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>(); renderer.sharedMaterial = glass;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            scars.Add(root);
        }

        internal void Clear()
        {
            RestoredCells = meshChangedCells;
            meshChangedCells = 0;
            for (int i = meshPatches.Count - 1; i >= 0; i--)
            {
                MeshPatch patch = meshPatches[i];
                if (patch.Filter != null && patch.Filter.sharedMesh == patch.Written) patch.Filter.sharedMesh = patch.Before;
                if (patch.Collider != null && patch.Collider.sharedMesh == patch.Written) patch.Collider.sharedMesh = patch.Before;
            }
            var destroyed = new HashSet<Mesh>();
            foreach (MeshPatch patch in meshPatches)
                if (patch.Written != null && destroyed.Add(patch.Written)) Object.Destroy(patch.Written);
            meshPatches.Clear();
            for (int i = patches.Count - 1; i >= 0; i--)
            {
                Patch patch = patches[i];
                if (patch.Data == null) continue;
                try
                {
                    int height = patch.Before.GetLength(0), width = patch.Before.GetLength(1);
                    float[,] current = patch.Data.GetHeights(patch.X, patch.Z, width, height);
                    for (int row = 0; row < height; row++)
                        for (int col = 0; col < width; col++)
                        {
                            float external = current[row, col] - patch.Written[row, col];
                            if (Mathf.Abs(patch.Before[row, col] - patch.Written[row, col]) > .000001f) RestoredCells++;
                            current[row, col] = Mathf.Clamp01(patch.Before[row, col] + (Mathf.Abs(external) > .0001f ? external : 0f));
                        }
                    patch.Data.SetHeightsDelayLOD(patch.X, patch.Z, current);
                    patch.Data.SyncHeightmap();
                }
                catch (System.Exception e) { Plugin.Logger?.LogWarning("[Vanguard] SA-74N terrain restore: " + e.Message); }
            }
            patches.Clear(); ChangedCells = 0;
            foreach (GameObject scar in scars) if (scar != null) Object.Destroy(scar);
            foreach (Mesh mesh in meshes) if (mesh != null) Object.Destroy(mesh);
            scars.Clear(); meshes.Clear();
            if (glass != null) Object.Destroy(glass);
            glass = null;
        }
    }
}
