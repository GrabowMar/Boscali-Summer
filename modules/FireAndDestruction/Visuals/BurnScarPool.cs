using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Util;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Pooled vanilla soot decals and Voronoi forest footprints. Client-local
    /// cosmetic: no colliders, no networking, and the oldest mark is recycled at the ceiling
    /// so a long campaign cannot grow the projector count without bound.
    /// </summary>
    internal sealed class BurnScarPool
    {
        private const int MaximumScars = 64;
        private const int SeedSalt = 0x4b1d5c07;

        private readonly List<GameObject> marks = new List<GameObject>(MaximumScars);
        private int ringHead;
        private bool warnedUnavailable;
        private readonly Dictionary<long, GameObject> forestMarks = new Dictionary<long, GameObject>(MaximumScars);
        private readonly Dictionary<GameObject, Mesh> forestMeshes = new Dictionary<GameObject, Mesh>(MaximumScars);
        private Material forestMaterial;
        private AssetBundle forestBundle;
        private bool forestShaderSearched;

        public void UpdateCulling(Camera camera)
        {
            if (camera == null) return;
            Vector3 position = camera.transform.position;
            // Forest meshes use the same 4 km range as the pooled native soot projectors.
            foreach (var mark in forestMarks.Values)
            {
                if (mark == null) continue;
                var renderer = mark.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.enabled = (mark.transform.position - position).sqrMagnitude < 4000f * 4000f;
            }
        }

        public void StampForest(GlobalPosition position, FireFrontCell cell)
        {
            if (GameManager.IsHeadless || cell == null || forestMarks.ContainsKey(cell.Key)) return;
            if (forestMaterial == null && !forestShaderSearched)
            {
                forestShaderSearched = true;
                Shader shader = Shader.Find("Boscali/ForestBurnSurface");
                if (shader == null)
                {
                    byte[] bytes = EmbeddedResources.ReadAll(typeof(BurnScarPool).Assembly,
                        "BoscaliSummer.Fire.forestfire.bundle", 1024 * 1024);
                    if (bytes != null) forestBundle = AssetBundle.LoadFromMemory(bytes);
                    if (forestBundle != null) shader = forestBundle.LoadAsset<Shader>("Assets/ForestBurnSurface.shader");
                }
                if (shader != null) forestMaterial = new Material(shader) { name = "Forest ash surface" };
            }
            if (forestMaterial == null) return;
            GameObject mark = Acquire();
            if (mark == null) return;
            var projector = mark.GetComponent<DecalProjector>();
            if (projector != null) projector.enabled = false;
            var filter = mark.GetComponent<MeshFilter>();
            if (filter == null) filter = mark.AddComponent<MeshFilter>();
            var renderer = mark.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = mark.AddComponent<MeshRenderer>();
            // Never rebuild a shared native mesh if the prefab happens to contain one.
            if (!forestMeshes.TryGetValue(mark, out Mesh mesh))
            {
                mesh = new Mesh { name = "Voronoi burn footprint" };
                forestMeshes[mark] = mesh;
            }
            filter.sharedMesh = mesh;
            var vertices = new Vector3[cell.Count + 1];
            var uv = new Vector2[cell.Count + 1];
            var indices = new int[cell.Count * 3];
            Vector3 anchor = position.ToLocalPosition();
            TerrainProbeCache.TryProbe(position, out GlobalPosition centerGround, out _);
            vertices[0] = Vector3.up * (centerGround.ToLocalPosition().y - anchor.y + 0.04f);
            uv[0] = new Vector2(cell.Center.X, cell.Center.Z);
            for (int i = 0; i < cell.Count; i++)
            {
                FireFrontCell.Point p = cell.Vertices[i];
                GlobalPosition query = new GlobalPosition(cell.Center.X + p.X, position.y, cell.Center.Z + p.Z);
                float height = TerrainProbeCache.TryProbe(query, out GlobalPosition ground, out _) ? ground.ToLocalPosition().y - anchor.y : 0f;
                vertices[i + 1] = new Vector3(p.X, height + 0.04f, p.Z);
                uv[i + 1] = new Vector2(query.x, query.z);
                indices[i * 3] = 0; indices[i * 3 + 1] = (i + 1) % cell.Count + 1; indices[i * 3 + 2] = i + 1;
            }
            mesh.Clear(); mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = indices; mesh.RecalculateBounds();
            renderer.sharedMaterial = forestMaterial; renderer.enabled = true;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            mark.transform.SetPositionAndRotation(anchor, Quaternion.identity); mark.transform.localScale = Vector3.one;
            forestMarks[cell.Key] = mark;
        }

        public void Stamp(GlobalPosition position, float diameter)
        {
            if (GameManager.IsHeadless || diameter <= 0f) return;
            // The same cached ground probe the fire front uses: one terrain cast per 24 m
            // cell instead of one per decal lobe.
            if (!TerrainProbeCache.TryProbe(position, out GlobalPosition ground, out Vector3 normal)) return;

            GameObject mark = Acquire();
            if (mark == null) return;
            DecalProjector projector = mark.GetComponent<DecalProjector>();
            if (projector == null) return;
            projector.enabled = true;
            MeshRenderer meshRenderer = mark.GetComponent<MeshRenderer>();
            if (meshRenderer != null) meshRenderer.enabled = false;

            // Ground projection: the box looks along the surface normal, rolled around it so
            // neighbouring scars never read as identical stamps.
            Vector3 forward = -normal;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, forward);
            if (up.sqrMagnitude < 0.01f) up = Vector3.ProjectOnPlane(Vector3.forward, forward);
            uint seed = Deterministic.Hash(
                Mathf.RoundToInt(ground.x), Mathf.RoundToInt(ground.y),
                Mathf.RoundToInt(ground.z), SeedSalt);
            float roll = Deterministic.UnitFloat(seed) * 360f;
            mark.transform.rotation =
                Quaternion.AngleAxis(roll, forward) * Quaternion.LookRotation(forward, up);
            mark.transform.position = ground.ToLocalPosition();

            float depth = Mathf.Clamp(diameter * 0.18f, 1.6f, 6f);
            projector.size = new Vector3(diameter, diameter, depth);
            projector.renderingLayerMask = ~0u;
            projector.startAngleFade = 45f;
            projector.endAngleFade = 70f;
            projector.fadeFactor = 1f;
            projector.drawDistance = 4000f;

            Material scorch = ScorchDecalMaterialResolver.Resolve();
            if (scorch != null) projector.material = scorch;
        }

        public void Clear()
        {
            for (int i = 0; i < marks.Count; i++)
                if (marks[i] != null)
                {
                    Object.Destroy(marks[i]);
                }
            marks.Clear();
            forestMarks.Clear();
            foreach (Mesh mesh in forestMeshes.Values) if (mesh != null) Object.Destroy(mesh);
            forestMeshes.Clear();
            if (forestMaterial != null) Object.Destroy(forestMaterial);
            forestMaterial = null;
            if (forestBundle != null) forestBundle.Unload(false);
            forestBundle = null; forestShaderSearched = false;
            ringHead = 0;
            warnedUnavailable = false;
            ScorchDecalMaterialResolver.ResetForScene();
        }

        private GameObject Acquire()
        {
            if (marks.Count < FxBudget.ScaleCount(MaximumScars, FxBus.Scales.RenderTargets))
            {
                GameObject prefab = GameAssets.i != null ? GameAssets.i.scorchMarkDecal : null;
                if (prefab == null)
                {
                    if (!warnedUnavailable)
                    {
                        warnedUnavailable = true;
                        Plugin.Logger.LogWarning(
                            "Ground burn scars unavailable: GameAssets.scorchMarkDecal is not loaded.");
                    }
                    return null;
                }
                GameObject created = Object.Instantiate(prefab, Datum.origin, false);
                created.name = "BoscaliSummer.BurnScar";
                created.SetActive(true);
                marks.Add(created);
                return created;
            }
            GameObject oldest = marks[ringHead];
            ringHead = (ringHead + 1) % marks.Count;
            if (oldest != null) oldest.SetActive(true);
            long removeKey = 0; bool found = false;
            foreach (var entry in forestMarks)
                if (entry.Value == oldest) { removeKey = entry.Key; found = true; break; }
            if (found) forestMarks.Remove(removeKey);
            return oldest;
        }
    }
}
