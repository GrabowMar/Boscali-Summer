using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Effects;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Bounded native terrain/physics edits and native textured soot. No map assets are saved.</summary>
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
        private sealed class MeshPatch
        {
            internal MeshFilter Filter;
            internal MeshCollider Collider;
            internal Mesh Before, Written;
        }
        private readonly List<MeshPatch> meshPatches = new List<MeshPatch>();
        private static readonly FieldInfo HeightEntries = AccessTools.Field(typeof(TerrainHeightMap), "allEntries");
        private static readonly MethodInfo UnregisterHeight = AccessTools.Method(typeof(TerrainHeightMap), "UnregisterInternal");
        private static readonly MethodInfo RegisterHeight = AccessTools.Method(typeof(TerrainHeightMap), "RegisterInternal", new[] { typeof(TerrainHeightMap.RenderFilter) });
        private int meshChangedCells;
        private int strikes;
        internal int Scars => strikes;
        internal int ChangedCells { get; private set; }
        internal int RestoredCells { get; private set; }
        internal float CenterBefore { get; private set; }
        internal float CenterAfter { get; private set; }
        internal string LastSurface { get; private set; } = "none";
        internal int NativeSootMarks => scars.Count;
        internal float CollisionDrop => CenterBefore - CenterAfter;

        internal static bool TryGroundBurst(Vector3 center, out Vector3 ground)
        {
            ground = center;
            if (center.y <= Datum.LocalSeaY + 1f) return false;
            TerrainHeightMap heightMap = Object.FindObjectOfType<TerrainHeightMap>();
            RaycastHit[] hits = Physics.RaycastAll(center + Vector3.up * 150f, Vector3.down, 1000f,
                PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            foreach (RaycastHit hit in hits)
            {
                if (hit.point.y <= Datum.LocalSeaY + .2f || Mathf.Abs(center.y - hit.point.y) > 120f) continue;
                if (!IsGround(hit.collider, heightMap) || hit.distance >= nearest) continue;
                nearest = hit.distance; ground = hit.point;
            }
            return nearest != float.MaxValue;
        }

        internal static float GroundClearance(Vector3 center)
        {
            float surface = Datum.LocalSeaY;
            TerrainHeightMap heightMap = Object.FindObjectOfType<TerrainHeightMap>();
            foreach (RaycastHit hit in Physics.RaycastAll(center + Vector3.up * 2f, Vector3.down, 20000f,
                PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                if (hit.point.y <= center.y + 1f && hit.point.y > surface && IsGround(hit.collider, heightMap)) surface = hit.point.y;
            return Mathf.Max(0f, center.y - surface);
        }

        private static bool IsGround(Collider collider, TerrainHeightMap heightMap)
        {
            if (collider is TerrainCollider || (GameAssets.i != null && GameAssets.i.terrainMaterial != null &&
                collider.sharedMaterial == GameAssets.i.terrainMaterial)) return true;
            if (heightMap?.terrainPhysicMaterials != null)
                foreach (PhysicMaterial material in heightMap.terrainPhysicMaterials)
                    if (material != null && material == collider.sharedMaterial) return true;
            return false;
        }

        internal void Scar(Vector3 center, float radius)
        {
            if (strikes >= Sa74Fallout.MaxSites) return;
            if (!TryGroundBurst(center, out Vector3 ground))
            {
                LastSurface = center.y <= Datum.LocalSeaY + 1f ? "water" : "air";
                return;
            }
            LastSurface = "ground";
            strikes++;
            center = ground;
            CenterBefore = ground.y;
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
                        float original = terrain.transform.position.y + before[row, col] * data.size.y;
                        after[row, col] = Mathf.Clamp01((CraterSurface(original, center.y, distance) - terrain.transform.position.y) / data.size.y);
                        if (Mathf.Abs(after[row, col] - before[row, col]) > .000001f) ChangedCells++;
                    }
                patches.Add(new Patch { Data = data, X = x, Z = z, Before = before, Written = after });
                data.SetHeightsDelayLOD(x, z, after);
                data.SyncHeightmap();
            }
            ExcavateMeshes(center, radius);
            Physics.SyncTransforms();
            CenterAfter = center.y;
            if (Physics.Raycast(center + Vector3.up * 20f, Vector3.down, out RaycastHit floor, 1000f,
                PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore)) CenterAfter = floor.point.y;
            MakeScorch(center, radius * 2.5f);
        }

        private static float CraterSurface(float original, float ground, float distance)
        {
            float bowl = -260f * Mathf.Pow(Mathf.Clamp01(1f - distance / .82f), 1.35f);
            float rim = 55f * Mathf.Exp(-Mathf.Pow((distance - .80f) / .10f, 2f));
            float blend = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.86f, 1f, distance));
            return Mathf.Lerp(original, ground + bowl + rim, blend);
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
                RefreshHeightMap(renderer);
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
                    world.y = CraterSurface(world.y, center.y, distance);
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

        private static void RefreshHeightMap(MeshRenderer renderer)
        {
            TerrainHeightMap heightMap = Object.FindObjectOfType<TerrainHeightMap>();
            if (heightMap == null || renderer == null || !(HeightEntries.GetValue(heightMap) is List<TerrainHeightMap.Entry> entries)) return;
            TerrainHeightMap.Entry found = null;
            foreach (TerrainHeightMap.Entry entry in entries)
                if (entry.renderer == renderer) { found = entry; break; }
            if (found == null) return;
            // Entry.mesh is cached, not read from MeshFilter during a bake. Refresh it without adding an external registration.
            UnregisterHeight.Invoke(heightMap, new object[] { renderer });
            RegisterHeight.Invoke(heightMap, new object[] { new TerrainHeightMap.RenderFilter(renderer, found.submeshFilter, found.type) });
            heightMap.SetDirty();
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
            GameObject prefab = GameAssets.i != null ? GameAssets.i.scorchMarkDecal : null;
            if (prefab == null || Sa74NativeGround.SootMaterial == null) return;
            GameObject root = Object.Instantiate(prefab, Datum.origin, false);
            root.name = "SA-74N persistent native soot";
            root.transform.SetPositionAndRotation(center, Quaternion.LookRotation(Vector3.up));
            DecalProjector projector = root.GetComponent<DecalProjector>();
            if (projector == null) { Object.Destroy(root); return; }
            projector.material = Sa74NativeGround.SootMaterial;
            projector.size = new Vector3(radius * 2f, radius * 2f, 1600f);
            projector.drawDistance = 45000f;
            projector.fadeScale = .25f;
            projector.fadeFactor = 1f;
            scars.Add(root);
        }

        internal void Clear()
        {
            RestoredCells = meshChangedCells;
            meshChangedCells = 0;
            for (int i = meshPatches.Count - 1; i >= 0; i--)
            {
                MeshPatch patch = meshPatches[i];
                if (patch.Filter != null && patch.Filter.sharedMesh == patch.Written)
                {
                    patch.Filter.sharedMesh = patch.Before;
                    RefreshHeightMap(patch.Filter.GetComponent<MeshRenderer>());
                }
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
            scars.Clear();
            strikes = 0;
            CenterBefore = CenterAfter = 0f; LastSurface = "none";
        }
    }
}
