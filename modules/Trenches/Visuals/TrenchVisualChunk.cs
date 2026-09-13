using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Trenches.Runtime;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Visuals
{
    /// <summary>
    /// Renders a trench network in 3D world space using a 3-tier camera distance LOD.
    /// LOD0 is a continuous earthwork extrusion per edge; an artist AssetBundle, when
    /// present, replaces it with modular prefabs. Handles spatial parenting under
    /// Datum.origin to maintain compatibility with Nuclear Option's floating origin shifts.
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

            bool modular = TrenchPrefabResolver.HasExternalBundle;
            foreach (var edge in network.Edges)
            {
                if (edge.PathPoints == null || edge.PathPoints.Length < 2) continue;

                var edgeObj0 = new GameObject($"Edge_{edge.Id}_LOD0");
                edgeObj0.transform.SetParent(lod0Root.transform, false);
                if (modular) BuildModularEdge(edge, edgeObj0, earthMat);
                else BuildContinuousEdge(edge, edgeObj0, earthMat);
                AddEdgeColliders(edgeObj0, edge.PathPoints, edge.TrenchWidth, edge.ParapetHeight);

                // Simplified berm mesh for LOD1 (lower parapet, no detail)
                Mesh edgeMesh1 = TrenchMeshBuilder.BuildEdgeMesh(
                    edge.PathPoints,
                    edge.TrenchWidth,
                    edge.ParapetHeight * 0.7f,
                    0.5f,
                    network.ThreatDirection);
                if (edgeMesh1 != null)
                {
                    proceduralMeshes.Add(edgeMesh1);
                    var edgeObj1 = new GameObject($"Edge_{edge.Id}_LOD1");
                    edgeObj1.transform.SetParent(lod1Root.transform, false);
                    edgeObj1.AddComponent<MeshFilter>().sharedMesh = edgeMesh1;
                    edgeObj1.AddComponent<MeshRenderer>().sharedMaterial = earthMat;
                }

                // LOD2 ground scar ribbon for high-altitude viewing
                Mesh ribbon = TrenchMeshBuilder.BuildEdgeMesh(edge.PathPoints,
                    edge.TrenchWidth + 1.6f, 0.10f, 0.05f, network.ThreatDirection);
                if (ribbon != null)
                {
                    proceduralMeshes.Add(ribbon);
                    var scar = new GameObject($"Edge_{edge.Id}_LOD2");
                    scar.transform.SetParent(lod2Root.transform, false);
                    scar.AddComponent<MeshFilter>().sharedMesh = ribbon;
                    scar.AddComponent<MeshRenderer>().sharedMaterial = earthMat;
                }
            }

            foreach (var node in network.Nodes)
            {
                GameObject nodeObj0 = null;
                switch (node.Type)
                {
                    case TrenchNodeType.BunkerBlindage:
                        nodeObj0 = TrenchPrefabResolver.InstantiateBunker(node.Position, node.Rotation, lod0Root.transform);
                        if (nodeObj0 != null)
                        {
                            var col = nodeObj0.AddComponent<BoxCollider>();
                            col.size = new Vector3(7.5f, 3.2f, 8.5f);
                            col.center = new Vector3(0f, 0.7f, 0f);
                            colliders.Add(col);
                        }
                        break;

                    case TrenchNodeType.HeavyWeaponPit:
                        nodeObj0 = TrenchPrefabResolver.InstantiateWeaponPit(node.Position, node.Rotation, lod0Root.transform);
                        break;

                    case TrenchNodeType.TrenchJunction:
                        nodeObj0 = TrenchPrefabResolver.InstantiateJunction(node.Position, node.Rotation, lod0Root.transform);
                        break;

                    case TrenchNodeType.Foxhole:
                    case TrenchNodeType.RifleBay:
                    default:
                        nodeObj0 = TrenchPrefabResolver.InstantiateRifleBay(node.Position, node.Rotation, lod0Root.transform);
                        break;
                }

                TrenchPrefabResolver.InstantiateLOD1Node(node.Position, node.Rotation, lod1Root.transform);
                TrenchPrefabResolver.InstantiateLOD1Node(node.Position, node.Rotation, lod2Root.transform);
            }

            currentLod = -1;
            UpdateLod(true);
        }

        private void BuildContinuousEdge(TrenchEdge edge, GameObject edgeObj, Material earthMat)
        {
            Mesh mesh = TrenchMeshBuilder.BuildEdgeMesh(edge.PathPoints, edge.TrenchWidth,
                edge.ParapetHeight, edge.SkirtDepth, network.ThreatDirection);
            if (mesh == null) return;
            proceduralMeshes.Add(mesh);
            edgeObj.AddComponent<MeshFilter>().sharedMesh = mesh;
            edgeObj.AddComponent<MeshRenderer>().sharedMaterial = earthMat;
        }

        private void BuildModularEdge(TrenchEdge edge, GameObject edgeObj, Material earthMat)
        {
            const float moduleLength = 8.0f;
            for (int i = 0; i < edge.PathPoints.Length - 1; i++)
            {
                Vector3 p0 = edge.PathPoints[i];
                Vector3 p1 = edge.PathPoints[i + 1];
                float dist = Vector3.Distance(p0, p1);
                if (dist < 0.2f) continue;

                Vector3 fwd = (p1 - p0) / dist;
                Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
                Vector3 right = rot * Vector3.right;
                if (Vector3.Dot(right, network.ThreatDirection) < 0f)
                    rot = Quaternion.LookRotation(-fwd, Vector3.up);

                int count = Mathf.Max(1, Mathf.CeilToInt(dist / moduleLength));
                float step = dist / count;
                for (int p = 0; p < count; p++)
                {
                    Vector3 pos = Vector3.Lerp(p0, p1, (p + 0.5f) / count);
                    GameObject straight = TrenchPrefabResolver.InstantiateStraight(pos, rot, edgeObj.transform);
                    if (straight != null)
                        straight.transform.localScale = new Vector3(1f, 1f, (step + 0.3f) / moduleLength);
                }

                if (i < edge.PathPoints.Length - 2)
                {
                    Vector3 nextFwd = (edge.PathPoints[i + 2] - p1).normalized;
                    if (Vector3.Angle(fwd, nextFwd) > 12f)
                    {
                        Vector3 bisector = (fwd + nextFwd).normalized;
                        if (bisector.sqrMagnitude > 0.001f)
                            TrenchPrefabResolver.InstantiateCorner(p1, Quaternion.LookRotation(bisector, Vector3.up), edgeObj.transform);
                    }
                }
            }
        }

        private void AddEdgeColliders(GameObject parent, Vector3[] path, float width, float height)
        {
            float halfW = width * 0.5f;
            // One elongated box spans two zigzag segments; a sector belt is a vehicle
            // obstacle, not a precision mesh, and this halves PhysX object count.
            for (int i = 0; i < path.Length - 1; i += 2)
            {
                Vector3 p0 = path[i];
                Vector3 p1 = path[Math.Min(i + 2, path.Length - 1)];
                float len = Vector3.Distance(p0, p1);
                if (len < 1.0f) continue;

                Vector3 mid = (p0 + p1) * 0.5f;
                Vector3 fwd = (p1 - p0).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized;
                for (int wall = -1; wall <= 1; wall += 2)
                {
                    var colObj = new GameObject($"BermCollider_{i}_{wall}");
                    colObj.transform.SetParent(parent.transform, false);
                    colObj.transform.localPosition = mid + side * (wall * (halfW + 0.95f)) + Vector3.up * (height * 0.5f);
                    colObj.transform.localRotation = Quaternion.LookRotation(fwd, Vector3.up);
                    var box = colObj.AddComponent<BoxCollider>();
                    box.size = new Vector3(1.8f, height + 0.4f, len);
                    colliders.Add(box);
                }
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
