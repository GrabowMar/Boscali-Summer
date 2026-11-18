using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Maps.Domain;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Maps.Runtime
{
    /// <summary>
    /// A map's scenery from its <c>scenery</c> list and the shared kit. Kinds without collision (boulders, palms, floes
    /// ...) are merged into one mesh per 2.56 km square, draw range and material, and uploaded to the GPU without a CPU
    /// copy; solid kinds (stacks, bergs, huts, towers ...) are placed one object at a time with their colliders. A
    /// <see cref="MapSceneryCuller"/> on the scenery root switches the short and middle range off with distance.
    /// </summary>
    internal static class MapScenery
    {
        private const float Square = 2560f;

        private sealed class Part { public Mesh Mesh; public int SubMesh; public Matrix4x4 Pose; public Material Material; }
        private sealed class Shape { public GameObject Template; public List<Part> Parts = new List<Part>(); }
        private sealed class Batch { public Vector3 Origin; public Material Material; public List<CombineInstance> Pieces = new List<CombineInstance>(); }

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
                    shapes[record.kind << 8 | variant] = shape = Resolve(kind, variant);
                if (shape == null) continue;
                var position = new Vector3(record.x, record.y, record.z);
                Quaternion turn = Quaternion.Euler(0f, record.YawDegrees, 0f);
                Vector3 scale = Vector3.one * (record.scale > 0f ? record.scale : 1f);
                char draw = string.IsNullOrEmpty(kind.draw) ? 's' : kind.draw[0];
                if (kind.solid)
                {
                    GameObject solid = Object.Instantiate(shape.Template, scenery);
                    solid.name = draw + "_" + kind.name;
                    solid.transform.localPosition = position;
                    solid.transform.localRotation = turn;
                    solid.transform.localScale = scale;
                    foreach (Transform part in solid.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = PhysicsLayers.Statics;
                    solid.SetActive(true);
                }
                else
                {
                    int sx = Mathf.FloorToInt(position.x / Square), sz = Mathf.FloorToInt(position.z / Square);
                    var origin = new Vector3((sx + .5f) * Square, 0f, (sz + .5f) * Square);
                    Matrix4x4 pose = Matrix4x4.TRS(position - origin, turn, scale);
                    foreach (Part part in shape.Parts)
                    {
                        int material = materials.IndexOf(part.Material);
                        if (material < 0)
                        {
                            material = materials.Count;
                            materials.Add(part.Material);
                        }
                        string key = draw + "_" + sx + "_" + sz + "_" + material;
                        if (!batches.TryGetValue(key, out Batch batch)) batches[key] = batch = new Batch { Origin = origin, Material = part.Material };
                        batch.Pieces.Add(new CombineInstance { mesh = part.Mesh, subMeshIndex = part.SubMesh, transform = pose * part.Pose });
                    }
                }
                placed++;
            }
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

        /// <summary>The kit template of a kind's variant and, for merging, its meshes in the template's own space.</summary>
        private static Shape Resolve(KitManifest.Kind kind, int variant)
        {
            GameObject template = MapKitCatalog.Prototype(kind.name, variant);
            if (template == null)
            {
                Plugin.Logger?.LogWarning("[Maps] The scenery kit has no kit_" + kind.name + "_" + variant + "; those items are left out.");
                return null;
            }
            var shape = new Shape { Template = template };
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
            return shape.Parts.Count > 0 ? shape : null;
        }
    }

    /// <summary>
    /// Draw ranges of a map's scenery: one <see cref="CullingGroup"/> measures every short-range ("s_", to 3.5 km) and
    /// middle-range ("m_", to 12 km) renderer under this object against the active camera and switches it off beyond its
    /// range; long-range scenery is not listed and always draws. The camera is looked up again each second.
    /// </summary>
    internal sealed class MapSceneryCuller : MonoBehaviour
    {
        private const float Near = 3500f, Middle = 12000f;
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
