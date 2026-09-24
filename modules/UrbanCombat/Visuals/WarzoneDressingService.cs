using System;
using System.Collections.Generic;
using BoscaliSummer.Core;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Client-local street dressing around garrisoned zones: sandbag arcs, concrete
    /// chicanes and burnt-car husks derived deterministically from replicated defense
    /// names, so every peer builds the same warzone with no network traffic. One mesh
    /// per material for the whole scene, rebuilt at most every 30 s when membership
    /// changes. No colliders, no lights.
    /// </summary>
    internal sealed class WarzoneDressingService : MonoBehaviour, ISceneService
    {
        private const float RebuildInterval = 30f;
        private const float SnapHeight = 50f;
        private const float SnapDistance = 100f;
        private const int ScanCap = 96;

        private float nextRebuild;
        private int membershipHash;
        private GameObject root;
        private readonly List<Mesh> meshes = new List<Mesh>(4);
        private readonly List<Building> scratch = new List<Building>(ScanCap);
        private static Material charMaterial;

        public void ResetForScene()
        {
            Clear();
            nextRebuild = 0f;
            membershipHash = 0;
        }

        private void OnDestroy() => Clear();

        private void Update()
        {
            if (Application.isBatchMode) return;
            if (Time.unscaledTime < nextRebuild) return;
            nextRebuild = Time.unscaledTime + RebuildInterval;
            Rebuild();
        }

        private void Rebuild()
        {
            scratch.Clear();
            bool enabled = Plugin.Settings != null && Plugin.Settings.UrbanCombat.GarrisonsEnabled.Value;
            if (enabled)
            {
                NestRegistry.Prune();
                NestRegistry.CopyLiveNests(scratch, ScanCap);
                scratch.Sort((a, b) => string.CompareOrdinal(a.NetworkUniqueName, b.NetworkUniqueName));
            }
            int hash = 17;
            int clusters = Math.Min(scratch.Count, WarzoneDressingMath.MaxClusters);
            for (int i = 0; i < clusters; i++)
                hash = hash * 31 + (int)Deterministic.HashString(scratch[i].NetworkUniqueName);
            if (hash == membershipHash) return;
            membershipHash = hash;
            Clear();
            if (!enabled || clusters == 0) return;
            Build(clusters);
        }

        private void Build(int clusters)
        {
            var sand = new MeshSoup();
            var concrete = new MeshSoup();
            var charred = new MeshSoup();
            for (int i = 0; i < clusters; i++)
            {
                Building defense = scratch[i];
                if (defense == null) continue;
                WarzoneDressingMath.ClusterLayout(defense.NetworkUniqueName, out float angle, out float radius, out int variant);
                Vector3 anchor = defense.transform.position + new Vector3(
                    (float)Math.Cos(angle) * radius, 0f, (float)Math.Sin(angle) * radius);
                if (!Physics.Raycast(anchor + Vector3.up * SnapHeight, Vector3.down,
                    out RaycastHit hit, SnapDistance, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                    continue;
                Vector3 ground = hit.point;
                float yaw = -angle;
                if (variant == WarzoneDressingMath.VariantBurntCar)
                    EmitBurntCar(ground, yaw, charred, sand);
                else if (variant == WarzoneDressingMath.VariantChicane)
                    EmitChicane(ground, yaw, concrete, sand);
                else
                    EmitBarricade(ground, yaw, sand, concrete);
            }
            // Vertices are baked in world space: parent under Datum.origin keeping that world
            // position so floating-origin shifts carry the dressing with the garrisons.
            root = new GameObject("BoscaliSummer.WarzoneDressing");
            root.transform.SetParent(Datum.origin, true);
            EmitMesh(sand, MaterialProvider.GetSandbagMaterial(), "Sand", root.transform);
            EmitMesh(concrete, MaterialProvider.GetConcreteMaterial(), "Concrete", root.transform);
            EmitMesh(charred, CharMaterial(), "Char", root.transform);
        }

        private void EmitMesh(MeshSoup soup, Material material, string name, Transform parent)
        {
            if (soup.Count == 0 || material == null) return;
            Mesh mesh = soup.ToMesh();
            mesh.name = "BoscaliSummer.WarzoneDressing." + name;
            meshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static void EmitBarricade(Vector3 ground, float yaw, MeshSoup sand, MeshSoup concrete)
        {
            for (int k = 0; k < 5; k++)
            {
                float a = yaw + (k - 2) * 0.35f;
                Vector3 pos = ground + new Vector3((float)Math.Cos(a) * 2.2f, 0.425f, (float)Math.Sin(a) * 2.2f);
                sand.AddBox(pos, new Vector3(1.5f, 0.85f, 0.55f), a + (float)Math.PI / 2f);
            }
            Vector3 back = ground - new Vector3((float)Math.Cos(yaw) * 1.5f, 0f, (float)Math.Sin(yaw) * 1.5f);
            concrete.AddBox(back + Vector3.up * 0.5f, new Vector3(2f, 1f, 0.5f), yaw);
        }

        private static void EmitChicane(Vector3 ground, float yaw, MeshSoup concrete, MeshSoup sand)
        {
            Vector3 dir = new Vector3((float)Math.Cos(yaw), 0f, (float)Math.Sin(yaw));
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
            concrete.AddBox(ground + side * 1.9f + Vector3.up * 0.525f, new Vector3(2.4f, 1.05f, 0.55f), yaw + 0.35f);
            concrete.AddBox(ground - side * 1.9f + Vector3.up * 0.525f, new Vector3(2.4f, 1.05f, 0.55f), yaw - 0.35f);
            sand.AddBox(ground + side * 3.4f + Vector3.up * 0.4f, new Vector3(1.4f, 0.8f, 0.5f), yaw);
            sand.AddBox(ground - side * 3.4f + Vector3.up * 0.4f, new Vector3(1.4f, 0.8f, 0.5f), yaw);
        }

        private static void EmitBurntCar(Vector3 ground, float yaw, MeshSoup charred, MeshSoup sand)
        {
            Vector3 dir = new Vector3((float)Math.Cos(yaw), 0f, (float)Math.Sin(yaw));
            Vector3 side = new Vector3(-dir.z, 0f, dir.x);
            charred.AddBox(ground + Vector3.up * 0.62f, new Vector3(4.3f, 0.85f, 1.8f), yaw);
            charred.AddBox(ground - dir * 0.35f + Vector3.up * 1.3f, new Vector3(2f, 0.6f, 1.6f), yaw);
            sand.AddBox(ground + side * 3f + Vector3.up * 0.4f, new Vector3(1.4f, 0.8f, 0.5f), yaw + 0.2f);
            sand.AddBox(ground - side * 3f + Vector3.up * 0.4f, new Vector3(1.4f, 0.8f, 0.5f), yaw - 0.2f);
        }

        private static Material CharMaterial()
        {
            if (charMaterial != null) return charMaterial;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            charMaterial = new Material(shader)
            {
                color = new Color(0.16f, 0.14f, 0.13f, 1f)
            };
            charMaterial.SetFloat("_Metallic", 0f);
            charMaterial.SetFloat("_Smoothness", 0f);
            return charMaterial;
        }

        private void Clear()
        {
            for (int i = 0; i < meshes.Count; i++)
                if (meshes[i] != null) Destroy(meshes[i]);
            meshes.Clear();
            if (root != null) Destroy(root);
            root = null;
        }

        private sealed class MeshSoup
        {
            private readonly List<Vector3> vertices = new List<Vector3>(1024);
            private readonly List<Vector3> normals = new List<Vector3>(1024);
            private readonly List<Vector2> uvs = new List<Vector2>(1024);
            private readonly List<int> triangles = new List<int>(1536);

            public int Count => triangles.Count;

            public Mesh ToMesh()
            {
                var mesh = new Mesh();
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }

            public void AddBox(Vector3 center, Vector3 size, float yaw)
            {
                float hx = size.x / 2f;
                float hy = size.y / 2f;
                float hz = size.z / 2f;
                float cos = (float)Math.Cos(yaw);
                float sin = (float)Math.Sin(yaw);
                AddFace(center, hx, hy, hz, cos, sin, -1f, 1f, -1f, 1f, 1f, -1f, 1f, 1f, 1f, -1f, 1f, 1f, 0f, 1f, 0f);
                AddFace(center, hx, hy, hz, cos, sin, -1f, -1f, -1f, -1f, -1f, 1f, 1f, -1f, 1f, 1f, -1f, -1f, 0f, -1f, 0f);
                AddFace(center, hx, hy, hz, cos, sin, 1f, -1f, -1f, 1f, -1f, 1f, 1f, 1f, 1f, 1f, 1f, -1f, 1f, 0f, 0f);
                AddFace(center, hx, hy, hz, cos, sin, -1f, -1f, -1f, -1f, 1f, -1f, -1f, 1f, 1f, -1f, -1f, 1f, -1f, 0f, 0f);
                AddFace(center, hx, hy, hz, cos, sin, 1f, -1f, 1f, -1f, -1f, 1f, -1f, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 1f);
                AddFace(center, hx, hy, hz, cos, sin, -1f, -1f, -1f, 1f, -1f, -1f, 1f, 1f, -1f, -1f, 1f, -1f, 0f, 0f, -1f);
            }

            private void AddFace(Vector3 center, float hx, float hy, float hz, float cos, float sin,
                float ax, float ay, float az, float bx, float by, float bz,
                float cx, float cy, float cz, float dx, float dy, float dz,
                float nx, float ny, float nz)
            {
                int baseIndex = vertices.Count;
                vertices.Add(Corner(center, hx, hy, hz, cos, sin, ax, ay, az));
                vertices.Add(Corner(center, hx, hy, hz, cos, sin, bx, by, bz));
                vertices.Add(Corner(center, hx, hy, hz, cos, sin, cx, cy, cz));
                vertices.Add(Corner(center, hx, hy, hz, cos, sin, dx, dy, dz));
                Vector3 normal = new Vector3(nx * cos + nz * sin, ny, -nx * sin + nz * cos);
                normals.Add(normal);
                normals.Add(normal);
                normals.Add(normal);
                normals.Add(normal);
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(0f, 1f));
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 3);
                triangles.Add(baseIndex + 2);
            }

            private static Vector3 Corner(Vector3 center, float hx, float hy, float hz, float cos, float sin,
                float sx, float sy, float sz)
            {
                float lx = sx * hx;
                float lz = sz * hz;
                return center + new Vector3(lx * cos + lz * sin, sy * hy, -lx * sin + lz * cos);
            }
        }
    }
}
