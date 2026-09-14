using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Trenches.Runtime;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Visuals
{
    /// <summary>
    /// Renders a trench network's carved ditches with a 3-tier camera distance LOD.
    /// LOD0 is the full earthwork extrusion plus the front-line vehicle obstacle;
    /// LOD1 is a simplified berm and LOD2 a ground scar for high-altitude viewing.
    /// Strongpoints are real game scenery spawned by TrenchWorks, not built here.
    /// All geometry is parented under Datum.origin for floating-origin compatibility.
    /// </summary>
    internal sealed class TrenchVisualChunk : MonoBehaviour
    {
        private const int MaximumColliders = 48;
        private const float FrontBand = -30f;

        private TrenchNetwork network;
        private GameObject lod0Root;
        private GameObject lod1Root;
        private GameObject lod2Root;

        private readonly List<Mesh> proceduralMeshes = new List<Mesh>();
        private readonly List<BoxCollider> colliders = new List<BoxCollider>(MaximumColliders);

        public float Lod0Distance = 250f;
        public float Lod1Distance = 1200f;
        public float Lod2Distance = 3500f;

        private int currentLod = -1;
        private float nextLodCheckTime;
        public float CameraDistance { get; private set; }
        public Vector3 WorldCenter => network != null ? transform.TransformPoint(network.Center) : transform.position;
        public string EarthMaterial { get; private set; }
        public int MeshCount => proceduralMeshes.Count;

        public void Initialize(TrenchNetwork net)
        {
            network = net;
            name = $"TrenchChunk_{net.Id}_{net.Name}";

            if (Datum.origin != null)
            {
                transform.SetParent(Datum.origin, false);
            }
            transform.localPosition = Vector3.zero;
            transform.rotation = Quaternion.identity;

            Rebuild();
        }

        public void Rebuild()
        {
            ClearMeshes();

            if (network == null) return;

            Material earthMat = TrenchMaterialResolver.GetEarthBermMaterial();
            EarthMaterial = earthMat != null ? earthMat.name + " / " + earthMat.shader?.name : "MISSING";
            if (earthMat == null) return;

            if (lod0Root == null)
            {
                lod0Root = new GameObject("LOD0_FullDetail");
                lod0Root.transform.SetParent(transform, false);
            }
            if (lod1Root == null)
            {
                lod1Root = new GameObject("LOD1_Berms");
                lod1Root.transform.SetParent(transform, false);
            }
            if (lod2Root == null)
            {
                lod2Root = new GameObject("LOD2_GroundRibbon");
                lod2Root.transform.SetParent(transform, false);
            }

            foreach (var edge in network.Edges)
            {
                if (edge.PathPoints == null || edge.PathPoints.Length < 2) continue;
                bool frontLine = Vector3.Dot(edge.PathPoints[0] - network.SeedCenter, network.ThreatDirection) >= FrontBand;

                AddEdgeMesh(lod0Root.transform, $"Edge_{edge.Id}_LOD0", edge,
                    edge.TrenchWidth, edge.ParapetHeight, edge.SkirtDepth, earthMat, true);
                AddEdgeMesh(lod1Root.transform, $"Edge_{edge.Id}_LOD1", edge,
                    edge.TrenchWidth, edge.ParapetHeight * 0.7f, 0.5f, earthMat, true);
                AddEdgeMesh(lod2Root.transform, $"Edge_{edge.Id}_LOD2", edge,
                    edge.TrenchWidth + 1.0f, 0.10f, 0.05f, earthMat, false);

                if (frontLine) AddFrontColliders(edge);
            }

            currentLod = -1;
            UpdateLod(true);
        }

        private void AddEdgeMesh(Transform parent, string name, TrenchEdge edge, float width, float height, float skirt,
            Material material, bool conformToGround)
        {
            Mesh mesh = TrenchMeshBuilder.BuildEdgeMesh(edge.PathPoints, width, height, skirt, network.ThreatDirection,
                conformToGround ? TrenchManager.SnapToGround : (Func<Vector3, Vector3>)null);
            if (mesh == null) return;
            proceduralMeshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>
        /// One low obstacle box every three path points along the fire trench: the front
        /// line stops vehicles without paying for a collider per ditch segment.
        /// </summary>
        private void AddFrontColliders(TrenchEdge edge)
        {
            Vector3[] path = edge.PathPoints;
            for (int i = 0; i < path.Length - 1 && colliders.Count < MaximumColliders; i += 3)
            {
                Vector3 p0 = path[i];
                Vector3 p1 = path[Math.Min(i + 3, path.Length - 1)];
                float len = Vector3.Distance(p0, p1);
                if (len < 1.5f) continue;

                Vector3 mid = (p0 + p1) * 0.5f;
                Vector3 fwd = (p1 - p0).normalized;
                var colObj = new GameObject($"DitchCollider_{edge.Id}_{i}");
                colObj.transform.SetParent(lod0Root.transform, false);
                colObj.transform.localPosition = mid + Vector3.up * (edge.ParapetHeight * 0.5f);
                colObj.transform.localRotation = Quaternion.LookRotation(fwd, Vector3.up);
                var box = colObj.AddComponent<BoxCollider>();
                box.size = new Vector3(edge.TrenchWidth + 1.8f, Mathf.Max(1.2f, edge.ParapetHeight * 0.9f), len + 0.5f);
                colliders.Add(box);
            }
        }

        private void Update()
        {
            float now = Time.time;
            if (now < nextLodCheckTime) return;
            nextLodCheckTime = now + 0.25f;

            UpdateLod(false);
        }

        private void UpdateLod(bool force)
        {
            Camera cam = Camera.main;
            if (cam == null && Camera.allCamerasCount > 0)
            {
                cam = Camera.allCameras[0];
            }
            if (cam == null) return;

            Vector3 camPos = cam.transform.position;
            Vector3 center = WorldCenter;
            float dist = Vector3.Distance(camPos, center);
            CameraDistance = dist;

            int newLod;
            if (dist < Lod0Distance) newLod = 0;
            else if (dist < Lod1Distance) newLod = 1;
            else if (dist < Lod2Distance) newLod = 2;
            else newLod = 3;

            if (newLod == currentLod && !force) return;
            currentLod = newLod;

            if (lod0Root != null) lod0Root.SetActive(currentLod == 0);
            if (lod1Root != null) lod1Root.SetActive(currentLod == 1);
            if (lod2Root != null) lod2Root.SetActive(currentLod == 2);

            // Colliders only active at close distance (LOD0) to minimize PhysX overhead
            bool collidersActive = (currentLod == 0 && network != null && !network.Overrun);
            for (int i = 0; i < colliders.Count; i++)
            {
                if (colliders[i] != null) colliders[i].enabled = collidersActive;
            }
        }

        private void ClearMeshes()
        {
            colliders.Clear();

            if (lod0Root != null) { Destroy(lod0Root); lod0Root = null; }
            if (lod1Root != null) { Destroy(lod1Root); lod1Root = null; }
            if (lod2Root != null) { Destroy(lod2Root); lod2Root = null; }

            for (int i = 0; i < proceduralMeshes.Count; i++)
            {
                if (proceduralMeshes[i] != null) Destroy(proceduralMeshes[i]);
            }
            proceduralMeshes.Clear();
        }

        private void OnDestroy()
        {
            ClearMeshes();
        }
    }
}
