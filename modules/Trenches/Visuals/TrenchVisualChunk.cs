using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Trenches.Runtime;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Visuals
{
    /// <summary>
    /// Renders a trench network in 3D world space using a 3-tier camera distance LOD.
    /// Handles spatial parenting under Datum.origin to maintain compatibility with
    /// Nuclear Option's floating origin shifts.
    /// </summary>
    internal sealed class TrenchVisualChunk : MonoBehaviour
    {
        private TrenchNetwork network;
        private GameObject lod0Root;
        private GameObject lod1Root;
        private GameObject lod2Root;

        private readonly List<Mesh> proceduralMeshes = new List<Mesh>();
        private readonly List<BoxCollider> colliders = new List<BoxCollider>();

        public float Lod0Distance = 250f;
        public float Lod1Distance = 1200f;
        public float Lod2Distance = 3500f;

        private int currentLod = -1;
        private float nextLodCheckTime;

        public void Initialize(TrenchNetwork net)
        {
            network = net;
            name = $"TrenchChunk_{net.Id}_{net.Name}";

            if (Datum.origin != null)
            {
                transform.SetParent(Datum.origin, true);
            }
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;

            Rebuild();
        }

        public void Rebuild()
        {
            ClearMeshes();

            if (network == null) return;

            Material earthMat = TrenchMaterialResolver.GetEarthBermMaterial();
            Material concreteMat = TrenchMaterialResolver.GetConcreteMaterial();

            // 1. Create LOD roots if needed
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

            // 2. Build Edge Meshes
            foreach (var edge in network.Edges)
            {
                if (edge.PathPoints == null || edge.PathPoints.Length < 2) continue;

                // Full 3D berm mesh for LOD0
                Mesh edgeMesh0 = TrenchMeshBuilder.BuildEdgeMesh(
                    edge.PathPoints,
                    edge.TrenchWidth,
                    edge.ParapetHeight,
                    edge.SkirtDepth,
                    network.ThreatDirection);

                if (edgeMesh0 != null)
                {
                    proceduralMeshes.Add(edgeMesh0);
                    var edgeObj = new GameObject($"Edge_{edge.Id}_LOD0");
                    edgeObj.transform.SetParent(lod0Root.transform, false);

                    var mf = edgeObj.AddComponent<MeshFilter>();
                    var mr = edgeObj.AddComponent<MeshRenderer>();
                    mf.sharedMesh = edgeMesh0;
                    mr.sharedMaterial = earthMat;

                    // Add simple BoxCollider along segments
                    AddEdgeColliders(edgeObj, edge.PathPoints, edge.TrenchWidth, edge.ParapetHeight);
                }

                // Simplified berm mesh for LOD1 (half resolution / lower parapet)
                Mesh edgeMesh1 = TrenchMeshBuilder.BuildEdgeMesh(
                    edge.PathPoints,
                    edge.TrenchWidth,
                    edge.ParapetHeight * 0.7f,
                    0.4f,
                    network.ThreatDirection);

                if (edgeMesh1 != null)
                {
                    proceduralMeshes.Add(edgeMesh1);
                    var edgeObj1 = new GameObject($"Edge_{edge.Id}_LOD1");
                    edgeObj1.transform.SetParent(lod1Root.transform, false);

                    var mf = edgeObj1.AddComponent<MeshFilter>();
                    var mr = edgeObj1.AddComponent<MeshRenderer>();
                    mf.sharedMesh = edgeMesh1;
                    mr.sharedMaterial = earthMat;
                }
            }

            // 3. Build Node Meshes (Bunkers, Weapon Pits, Foxholes)
            foreach (var node in network.Nodes)
            {
                Mesh nodeMesh = null;
                Material nodeMat = earthMat;

                switch (node.Type)
                {
                    case TrenchNodeType.BunkerBlindage:
                        nodeMesh = TrenchMeshBuilder.BuildBunkerMesh();
                        nodeMat = concreteMat;
                        break;
                    case TrenchNodeType.HeavyWeaponPit:
                        nodeMesh = TrenchMeshBuilder.BuildWeaponPitMesh();
                        break;
                    case TrenchNodeType.Foxhole:
                    case TrenchNodeType.RifleBay:
                        nodeMesh = TrenchMeshBuilder.BuildFoxholeMesh();
                        break;
                }

                if (nodeMesh != null)
                {
                    proceduralMeshes.Add(nodeMesh);
                    var nodeObj = new GameObject($"Node_{node.Id}_{node.Type}");
                    nodeObj.transform.SetParent(lod0Root.transform, false);
                    nodeObj.transform.position = node.Position;
                    nodeObj.transform.rotation = node.Rotation;

                    var mf = nodeObj.AddComponent<MeshFilter>();
                    var mr = nodeObj.AddComponent<MeshRenderer>();
                    mf.sharedMesh = nodeMesh;
                    mr.sharedMaterial = nodeMat;

                    // Simple box collider for bunker
                    if (node.Type == TrenchNodeType.BunkerBlindage)
                    {
                        var col = nodeObj.AddComponent<BoxCollider>();
                        col.size = new Vector3(5f, 2.5f, 6f);
                        col.center = new Vector3(0f, 1.2f, 0f);
                        colliders.Add(col);
                    }
                }
            }

            // Force initial LOD calculation
            currentLod = -1;
            UpdateLod(true);
        }

        private void AddEdgeColliders(GameObject parent, Vector3[] path, float width, float height)
        {
            for (int i = 0; i < path.Length - 1; i++)
            {
                Vector3 p0 = path[i];
                Vector3 p1 = path[i + 1];
                float len = Vector3.Distance(p0, p1);
                if (len < 1.0f) continue;

                Vector3 mid = (p0 + p1) * 0.5f;
                Vector3 fwd = (p1 - p0).normalized;

                var colObj = new GameObject($"Collider_{i}");
                colObj.transform.SetParent(parent.transform, false);
                colObj.transform.position = mid + Vector3.up * (height * 0.5f);
                colObj.transform.rotation = Quaternion.LookRotation(fwd, Vector3.up);

                var box = colObj.AddComponent<BoxCollider>();
                box.size = new Vector3(width + 2.0f, height + 0.4f, len);
                colliders.Add(box);
            }
        }

        private void Update()
        {
            float now = Time.time;
            if (now < nextLodCheckTime) return;
            nextLodCheckTime = now + 0.25f; // Evaluate LOD 4 times per second

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
            Vector3 center = (network != null) ? network.Center : transform.position;
            float dist = Vector3.Distance(camPos, center);

            int newLod;
            if (dist < Lod0Distance) newLod = 0;
            else if (dist < Lod1Distance) newLod = 1;
            else if (dist < Lod2Distance) newLod = 2;
            else newLod = 3; // Culled

            if (newLod == currentLod && !force) return;
            currentLod = newLod;

            if (lod0Root != null) lod0Root.SetActive(currentLod == 0);
            if (lod1Root != null) lod1Root.SetActive(currentLod == 1);
            if (lod2Root != null) lod2Root.SetActive(currentLod == 2);

            // Colliders only active at close distance (LOD0) to minimize PhysX overhead
            bool collidersActive = (currentLod == 0);
            for (int i = 0; i < colliders.Count; i++)
            {
                if (colliders[i] != null) colliders[i].enabled = collidersActive;
            }
        }

        private void ClearMeshes()
        {
            colliders.Clear();

            if (lod0Root != null)
            {
                Destroy(lod0Root);
                lod0Root = null;
            }
            if (lod1Root != null)
            {
                Destroy(lod1Root);
                lod1Root = null;
            }
            if (lod2Root != null)
            {
                Destroy(lod2Root);
                lod2Root = null;
            }

            for (int i = 0; i < proceduralMeshes.Count; i++)
            {
                if (proceduralMeshes[i] != null)
                {
                    Destroy(proceduralMeshes[i]);
                }
            }
            proceduralMeshes.Clear();
        }

        private void OnDestroy()
        {
            ClearMeshes();
        }
    }
}
