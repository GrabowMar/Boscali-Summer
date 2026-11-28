using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Modules.Maps.Domain;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Maps.Runtime
{
    /// <summary>
    /// Places the shared kit's scenery. Repeated decoration uses the game's GPU foliage culler and shared meshes.
    /// Other decoration uses coarse combined meshes. Solid props retain their prefabs, LOD groups and colliders.
    /// </summary>
    internal static class MapScenery
    {
        private const float Square = 2560f;
        private const int MinimumInstanceBatch = 32;

        private sealed class Part { public Mesh Mesh; public int SubMesh; public Matrix4x4 Pose; public Material Material; }
        private sealed class Shape
        {
            public GameObject Template;
            public readonly List<Part> Parts = new List<Part>();
            public KitManifest.Kind Kind;
            public List<SceneryRecord> Instances;
        }
        private sealed class Batch { public Vector3 Origin; public Material Material; public List<CombineInstance> Pieces = new List<CombineInstance>(); }
        internal sealed class PlacementBatch
        {
            public Mesh Mesh;
            public Material Material, Fallback;
            public Vector3[] Positions;
            public Vector4[] Poses;
            public float Radius, Distance;
            public Bounds Bounds;
        }

        private static ManualLogSource logger;
        internal static void Configure(ManualLogSource log) => logger = log;

        /// <summary>Places every record; returns how many were placed.</summary>
        public static int Place(Transform root, MapEntry entry)
        {
            TextAsset list = entry.Bundle.LoadAsset<TextAsset>("scenery");
            byte[] bytes = list != null ? list.bytes : null;
            int count = SceneryRecord.Count(bytes);
            if (count == 0) return 0;
            KitManifest kit = MapKitCatalog.Manifest;
            MapKitCatalog.Dress();
            string[] names = entry.Manifest.kinds ?? new string[0];
            var kinds = new KitManifest.Kind[names.Length];
            var unknown = new List<string>();
            for (int k = 0; k < names.Length; k++)
                if (MapTowns.Handles(names[k])) kinds[k] = null;
                else if ((kinds[k] = kit.Find(names[k])) == null || kinds[k].variants <= 0)
                {
                    kinds[k] = null;
                    unknown.Add(names[k]);
                }
            if (unknown.Count > 0)
                Plugin.Logger?.LogWarning("[Maps] " + entry.Manifest.displayName + ": the scenery kit has no " + string.Join(", ", unknown) + "; those items are left out.");

            var scenery = new GameObject("Scenery").transform;
            scenery.SetParent(root, false);
            var shapes = new Dictionary<int, Shape>();
            var batches = new Dictionary<string, Batch>();
            var materials = new List<Material>();
            bool headless = GameManager.IsHeadless;
            Shader instanceShader = MapSceneryInstances.IsSupported ? MapKitCatalog.InstanceShader : null;
            int placed = 0;
            for (int i = 0; i < count; i++)
            {
                SceneryRecord record = SceneryRecord.Read(bytes, i);
                if (record.kind < names.Length && MapTowns.Place(names[record.kind], new Vector3(record.x, record.y, record.z), record.YawDegrees, i))
                {
                    placed++;
                    continue;
                }
                KitManifest.Kind kind = record.kind < kinds.Length ? kinds[record.kind] : null;
                if (kind == null) continue;
                int variant = record.variant % kind.variants;
                if (!shapes.TryGetValue(record.kind << 8 | variant, out Shape shape))
                    shapes[record.kind << 8 | variant] = shape = Resolve(kind, variant, instanceShader != null);
                if (shape == null) continue;
                char draw = Range(kind);
                if (kind.solid)
                {
                    GameObject solid = Object.Instantiate(shape.Template, scenery);
                    solid.name = draw + "_" + kind.name;
                    solid.transform.localPosition = Position(record);
                    solid.transform.localRotation = Quaternion.Euler(0f, record.YawDegrees, 0f);
                    solid.transform.localScale = Vector3.one * Scale(record);
                    foreach (Transform part in solid.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = PhysicsLayers.Statics;
                    solid.SetActive(true);
                }
                else if (!headless)
                {
                    if (shape.Instances != null) shape.Instances.Add(record);
                    else AddCombined(shape, record, batches, materials);
                }
                placed++;
            }
            PrepareInstances(entry, shapes.Values, instanceShader, batches, materials);
            foreach (KeyValuePair<string, Batch> batch in batches)
            {
                var go = new GameObject(batch.Key);
                go.transform.SetParent(scenery, false);
                go.transform.localPosition = batch.Value.Origin;
                var mesh = new Mesh { name = batch.Key, indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontUnloadUnusedAsset };
                mesh.CombineMeshes(batch.Value.Pieces.ToArray(), true, true);
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true); // GPU only: scenery is never read back, and a whole map of it is millions of vertices
                entry.Owned.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = batch.Value.Material;
            }
            scenery.gameObject.AddComponent<MapSceneryCuller>();
            return placed;
        }

        private static void AddCombined(Shape shape, SceneryRecord record, Dictionary<string, Batch> batches, List<Material> materials)
        {
            Vector3 position = Position(record);
            int x = Mathf.FloorToInt(position.x / Square), z = Mathf.FloorToInt(position.z / Square);
            var origin = new Vector3((x + .5f) * Square, 0f, (z + .5f) * Square);
            Matrix4x4 pose = Matrix4x4.TRS(position - origin, Quaternion.Euler(0f, record.YawDegrees, 0f), Vector3.one * Scale(record));
            foreach (Part part in shape.Parts)
            {
                int material = materials.IndexOf(part.Material);
                if (material < 0)
                {
                    material = materials.Count;
                    materials.Add(part.Material);
                }
                string key = Range(shape.Kind) + "_" + x + "_" + z + "_" + material;
                if (!batches.TryGetValue(key, out Batch batch)) batches[key] = batch = new Batch { Origin = origin, Material = part.Material };
                batch.Pieces.Add(new CombineInstance { mesh = part.Mesh, subMeshIndex = part.SubMesh, transform = pose * part.Pose });
            }
        }

        internal static void AttachInstances(MapEntry entry, Transform map)
        {
            if (entry.SceneryInstances.Count == 0 || GameManager.IsHeadless) return;
            Transform scenery = map.Find("Scenery");
            if (scenery == null || scenery.GetComponent<MapSceneryInstances>() != null) return;
            // Instantiate does not preserve this plugin component's placement data in a game-prefab clone.
            // Attach to the live map and share the arrays owned by the current map build.
            var renderer = scenery.gameObject.AddComponent<MapSceneryInstances>();
            foreach (PlacementBatch batch in entry.SceneryInstances)
                renderer.Configure(batch.Mesh, batch.Material, batch.Positions, batch.Poses, batch.Radius, batch.Distance, batch.Bounds, batch.Fallback);
        }

        private static void PrepareInstances(MapEntry entry, IEnumerable<Shape> shapes, Shader shader,
            Dictionary<string, Batch> combined, List<Material> combinedMaterials)
        {
            var instanceMaterials = new Dictionary<(Material source, Texture2D foliage), Material>();
            int placements = 0;
            foreach (Shape shape in shapes)
            {
                if (shape == null || shape.Instances == null) continue;
                int count = shape.Instances.Count;
                if (count == 0) continue;
                if (count < MinimumInstanceBatch && !IsFoliage(shape.Kind.name))
                {
                    foreach (SceneryRecord record in shape.Instances) AddCombined(shape, record, combined, combinedMaterials);
                    continue;
                }

                Part part = shape.Parts[0];
                Mesh mesh = part.Mesh;
                Texture2D foliage = MapKitCatalog.FoliageTexture(shape.Kind.name, mesh);
                if (part.Pose != Matrix4x4.identity)
                {
                    mesh = new Mesh { name = part.Mesh.name + " scenery", indexFormat = part.Mesh.indexFormat };
                    mesh.CombineMeshes(new[] { new CombineInstance { mesh = part.Mesh, subMeshIndex = 0, transform = part.Pose } });
                    entry.Owned.Add(mesh);
                }
                var materialKey = (part.Material, foliage);
                if (!instanceMaterials.TryGetValue(materialKey, out Material material))
                {
                    material = new Material(shader) { name = part.Material.name + " Instanced", enableInstancing = true };
                    material.CopyPropertiesFromMaterial(part.Material);
                    material.enableInstancing = true;
                    material.SetFloat("_SceneryIce", part.Material.shader.name == "Boscali/MapKitIce" ? 1f : 0f);
                    if (foliage != null)
                    {
                        material.SetFloat("_FoliageCutout", 1f);
                        material.SetTexture("_FoliageMap", foliage);
                        Texture2D surface = MapKitCatalog.NativeTreeSurface;
                        if (surface != null)
                        {
                            material.SetFloat("_NativeTreeSurface", 1f);
                            material.SetTexture("_TreeSurface", surface);
                            logger?.LogInfo($"[Maps] {shape.Kind.name} uses the game's {surface.name} tree atlas.");
                        }
                    }
                    instanceMaterials.Add(materialKey, material);
                    entry.Owned.Add(material);
                }
                entry.SceneryInstances.Add(BuildBatch(shape, mesh, material, part.Material));
                placements += count;
            }
            if (placements > 0)
                logger?.LogInfo($"[Maps] {entry.Manifest.displayName}: {placements} scenery placements use native GPU culling in {entry.SceneryInstances.Count} indirect batches.");
        }

        private static PlacementBatch BuildBatch(Shape shape, Mesh mesh, Material material, Material fallback)
        {
            int count = shape.Instances.Count;
            var positions = new Vector3[count];
            var poses = new Vector4[count];
            float maxScale = 0f;
            var bounds = new Bounds(Position(shape.Instances[0]), Vector3.zero);
            for (int i = 0; i < count; i++)
            {
                SceneryRecord record = shape.Instances[i];
                Vector3 position = Position(record);
                float scale = Scale(record), yaw = record.YawDegrees * Mathf.Deg2Rad;
                positions[i] = position;
                poses[i] = new Vector4(scale, Mathf.Cos(yaw), Mathf.Sin(yaw), 0f);
                maxScale = Mathf.Max(maxScale, scale);
                bounds.Encapsulate(position);
            }
            float sway = material.HasProperty("_Sway") ? material.GetFloat("_Sway") : 0f;
            float radius = (mesh.bounds.center.magnitude + mesh.bounds.extents.magnitude) * maxScale + 1.42f * Mathf.Abs(sway);
            bounds.Expand(2f * radius);
            float distance = Range(shape.Kind) == 's' ? MapSceneryCuller.Near : Range(shape.Kind) == 'm' ? MapSceneryCuller.Middle : 200000f;
            return new PlacementBatch
            {
                Mesh = mesh, Material = material, Fallback = fallback, Positions = positions, Poses = poses,
                Radius = radius, Distance = distance, Bounds = bounds,
            };
        }

        private static bool IsFoliage(string kind) => kind == "palm" || kind == "acacia" || kind == "bush" || kind.StartsWith("conifer", StringComparison.Ordinal);
        private static char Range(KitManifest.Kind kind) => string.IsNullOrEmpty(kind.draw) ? 's' : kind.draw[0];
        private static float Scale(SceneryRecord record) => record.scale > 0f ? record.scale : 1f;
        private static Vector3 Position(SceneryRecord record) => new Vector3(record.x, record.y, record.z);

        /// <summary>The kit template of a kind's variant and, for merging, its meshes in the template's own space.</summary>
        private static Shape Resolve(KitManifest.Kind kind, int variant, bool instancing)
        {
            GameObject template = MapKitCatalog.Prototype(kind.name, variant);
            if (template == null)
            {
                Plugin.Logger?.LogWarning("[Maps] The scenery kit has no kit_" + kind.name + "_" + variant + "; those items are left out.");
                return null;
            }
            var shape = new Shape { Template = template, Kind = kind };
            if (kind.solid) return shape;
            Matrix4x4 toTemplate = template.transform.worldToLocalMatrix;
            bool levels = template.GetComponent<LODGroup>() != null; // a modelled shape: merged scenery draws its middle level
            foreach (MeshFilter filter in template.GetComponentsInChildren<MeshFilter>(true))
            {
                if (levels && filter.name != "lod1") continue;
                Mesh mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                Material[] materials = renderer != null ? renderer.sharedMaterials : null;
                if (mesh == null || materials == null || materials.Length == 0) continue;
                if (!mesh.isReadable)
                {
                    Plugin.Logger?.LogWarning("[Maps] kit_" + kind.name + "_" + variant + " has a mesh that cannot be read (bake it with Read/Write on); those items are left out.");
                    return null;
                }
                Matrix4x4 pose = toTemplate * filter.transform.localToWorldMatrix;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Material material = materials[Mathf.Min(sub, materials.Length - 1)];
                    if (material != null) shape.Parts.Add(new Part { Mesh = mesh, SubMesh = sub, Pose = pose, Material = material });
                }
            }
            if (shape.Parts.Count == 0) return null;
            if (instancing && shape.Parts.Count == 1)
            {
                Part part = shape.Parts[0];
                Shader shader = part.Material.shader;
                if (part.Mesh.subMeshCount == 1 && shader != null && (shader.name == "Boscali/MapSurface" || shader.name == "Boscali/MapKitIce"))
                    shape.Instances = new List<SceneryRecord>();
            }
            return shape;
        }
    }

    /// <summary>
    /// Draw ranges of a map's scenery: one <see cref="CullingGroup"/> measures every short-range ("s_", to 3.5 km) and
    /// middle-range ("m_", to 12 km) renderer under this object against the active camera and switches it off beyond its
    /// range; long-range scenery is not listed and always draws. The camera is looked up again each second.
    /// </summary>
    internal sealed class MapSceneryCuller : MonoBehaviour
    {
        internal const float Near = 3500f, Middle = 12000f;
        private static readonly float[] Bands = { Near, Middle };

        private CullingGroup group;
        private Renderer[] renderers;
        private int[] lastBand;       // the last distance band a renderer still draws in: 0 for "s_", 1 for "m_"
        private Vector3[] centres;    // each renderer's bounds centre in its own space
        private BoundingSphere[] spheres;
        private Camera target;
        private Vector3 placedAt;
        private float nextLook;

        private void OnEnable()
        {
            if (GameManager.IsHeadless) return;
            var found = new List<Renderer>();
            var bands = new List<int>();
            foreach (Transform child in transform)
            {
                string label = child.name;
                if (label.Length < 2 || label[1] != '_' || (label[0] != 's' && label[0] != 'm')) continue;
                foreach (Renderer renderer in child.GetComponentsInChildren<Renderer>(true))
                {
                    found.Add(renderer);
                    bands.Add(label[0] == 's' ? 0 : 1);
                }
            }
            if (found.Count == 0) return;
            renderers = found.ToArray();
            lastBand = bands.ToArray();
            centres = new Vector3[renderers.Length];
            spheres = new BoundingSphere[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Bounds bounds = renderers[i].localBounds;
                Vector3 size = renderers[i].transform.lossyScale;
                centres[i] = bounds.center;
                spheres[i].radius = bounds.extents.magnitude * Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            }
            PlaceSpheres();
            group = new CullingGroup();
            group.SetBoundingSpheres(spheres);
            group.SetBoundingSphereCount(spheres.Length);
            group.SetBoundingDistances(Bands);
            group.onStateChanged = Changed;
            target = null;
            nextLook = 0f;
        }

        private void OnDisable()
        {
            if (group != null) group.Dispose();
            group = null;
        }

        private void Changed(CullingGroupEvent change)
        {
            Renderer renderer = renderers[change.index];
            if (renderer != null) renderer.enabled = change.currentDistance <= lastBand[change.index];
        }

        private void LateUpdate()
        {
            if (group == null) return;
            if (transform.position != placedAt) PlaceSpheres(); // the floating origin moved the map
            if (Time.unscaledTime < nextLook) return;
            nextLook = Time.unscaledTime + 1f;
            Camera camera = SceneSingleton<CameraStateManager>.i != null ? SceneSingleton<CameraStateManager>.i.mainCamera : null;
            if (camera == null) camera = Camera.main;
            if (camera == null) return;
            if (camera != target)
            {
                target = camera;
                group.targetCamera = camera;
                group.SetDistanceReferencePoint(camera.transform);
            }
            // The group reports only changes; this sweep also settles the first second and anything a change missed.
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = group.GetDistance(i) <= lastBand[i];
        }

        private void PlaceSpheres()
        {
            placedAt = transform.position;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) spheres[i].position = renderers[i].transform.TransformPoint(centres[i]);
        }
    }
}
