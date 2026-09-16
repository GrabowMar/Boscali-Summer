using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Trenches.Domain;
using BoscaliSummer.Features.Trenches.Runtime;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Visuals
{
    /// <summary>
    /// Renders one position's carved ditches with a 3-tier camera distance LOD. The fire
    /// line is one continuous curve; its traversed ditch is cut from the same Bezier the
    /// planner fitted to the front trace, so the earthwork bends with the geography instead
    /// of stepping through nodes. Strongpoints are real game scenery spawned by
    /// <see cref="TrenchWorks"/>, not built here. All geometry is parented under
    /// Datum.origin for floating-origin compatibility.
    /// </summary>
    internal sealed class TrenchVisualChunk : MonoBehaviour
    {
        private const int MaximumColliders = 48;
        private const int MaximumRings = 1200;
        private const float TraceStep = 3.5f;
        private const float CoarseStep = 9f;
        private const float RidgeStep = 18f;
        private const float WireForward = 16f;
        private const float WireHeight = 1.15f;

        private TrenchLine line;
        private GameObject lod0Root;
        private GameObject lod1Root;
        private GameObject lod2Root;

        private readonly List<Mesh> proceduralMeshes = new List<Mesh>();
        private readonly List<BoxCollider> colliders = new List<BoxCollider>(MaximumColliders);
        private readonly float[] pathX = new float[MaximumRings];
        private readonly float[] pathZ = new float[MaximumRings];
        private readonly float[] curveX = new float[TrenchLine.MaximumCurvePoints];
        private readonly float[] curveZ = new float[TrenchLine.MaximumCurvePoints];

        public float Lod0Distance = 250f;
        public float Lod1Distance = 1200f;
        public float Lod2Distance = 3500f;

        private int currentLod = -1;
        private float nextLodCheckTime;
        public float CameraDistance { get; private set; }
        public Vector3 WorldCenter => line != null ? line.Center : transform.position;
        public string EarthMaterial { get; private set; }
        public int MeshCount => proceduralMeshes.Count;

        public void Initialize(TrenchLine position)
        {
            line = position;
            name = $"TrenchChunk_{position.Id}_{position.Name}";

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

            if (line == null || line.Curve == null || line.Curve.Length < 2) return;

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

            float width = WidthFor(line.Stage);
            float parapet = ParapetFor(line.Stage);
            Vector3[] fire = BuildDitchPath(line.Curve, TraceStep);
            if (fire != null)
            {
                AddMesh(lod0Root.transform, "Fire_LOD0", fire, width, parapet, 2.2f, earthMat, true);
                AddFrontColliders(fire, width, parapet, "Fire");
                AddWireBelt(lod0Root.transform, earthMat);
            }
            // Mid and far LODs stay real earthworks at a coarser ring pitch: a wing flying
            // over the front must still read the parapet line and the belt behind it. The
            // far ridge is the silhouette — berms, no skirts — rather than a flat scar.
            AddPath(lod1Root.transform, "Fire_LOD1", line.Curve, CoarseStep,
                width, parapet * 0.85f, 1.6f, earthMat, true);
            AddPath(lod2Root.transform, "Fire_LOD2", line.Curve, RidgeStep,
                width + 4.5f, 1.6f, 0.8f, earthMat, true);
            if (line.Support != null)
            {
                AddPath(lod0Root.transform, "Support", line.Support, TraceStep, 4.2f, 3.0f, 2.0f, earthMat, true);
                AddPath(lod1Root.transform, "Support_LOD1", line.Support, CoarseStep, 4.2f, 2.4f, 1.6f, earthMat, true);
                AddPath(lod2Root.transform, "Support_LOD2", line.Support, RidgeStep, 7.5f, 1.5f, 0.8f, earthMat, true);
            }
            if (line.Redoubt != null)
            {
                AddPath(lod0Root.transform, "Redoubt", line.Redoubt, TraceStep, 4.2f, 2.6f, 2.0f, earthMat, true);
                AddPath(lod1Root.transform, "Redoubt_LOD1", line.Redoubt, CoarseStep, 4.2f, 2.2f, 1.6f, earthMat, true);
                AddPath(lod2Root.transform, "Redoubt_LOD2", line.Redoubt, RidgeStep, 7.5f, 1.5f, 0.8f, earthMat, true);
            }
            AddTraces(lod0Root.transform, line.Links, "Link", 3.2f, 2.2f, earthMat);
            AddTraces(lod0Root.transform, line.Spurs, "Sap", 2.0f, 1.4f, earthMat);

            currentLod = -1;
            UpdateLod(true);
        }

        private void AddTraces(Transform parent, Vector3[][] traces, string name, float width, float parapet,
            Material material)
        {
            if (traces == null || parent == null) return;
            for (int i = 0; i < traces.Length; i++)
                AddPath(parent, $"{name}{i}", traces[i], TraceStep, width, parapet, 1.4f, material, true);
        }

        /// <summary>
        /// The wire belt in front of the fire trench: pickets and strands offset onto the
        /// enemy side of the parapet and draped over the ground, so the approach reads as
        /// wired no man's land instead of bare field.
        /// </summary>
        private void AddWireBelt(Transform parent, Material material)
        {
            if (parent == null || material == null || line == null) return;
            Vector3[] wireCurve = OffsetCurve(line.Curve, line.Threat, WireForward);
            if (wireCurve == null) return;
            Vector3[] path = BuildDitchPath(wireCurve, CoarseStep);
            if (path == null) return;

            Mesh mesh = TrenchMeshBuilder.BuildWireBeltMesh(path, WireHeight);
            if (mesh == null) return;
            proceduralMeshes.Add(mesh);
            var go = new GameObject("WireBelt");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static Vector3[] OffsetCurve(Vector3[] curve, Vector3[] threat, float forward)
        {
            if (curve == null || threat == null || curve.Length == 0) return null;
            var offset = new Vector3[curve.Length];
            for (int i = 0; i < curve.Length; i++)
            {
                Vector3 direction = threat.Length > 0 ? threat[Mathf.Min(i, threat.Length - 1)] : Vector3.forward;
                offset[i] = curve[i] + new Vector3(direction.x, 0f, direction.z).normalized * forward;
            }
            return offset;
        }

        /// <summary>
        /// Densifies the planner's curve into ditch rings and lays the traverse wave over
        /// it. The phase comes from the line's world position, so neighbouring positions
        /// continue one pattern instead of restarting it.
        /// </summary>
        private Vector3[] BuildDitchPath(Vector3[] curve, float step)
        {
            if (curve == null || curve.Length < 2) return null;
            int count = TrenchTraceMath.DensifyTraversed(
                ToX(curve), ToZ(curve), curve.Length, step,
                TrenchTraceMath.TraverseSpacing, TrenchTraceMath.TraverseAmplitude,
                curve[0].x * 0.5f + curve[0].z * 0.5f, pathX, pathZ);
            if (count < 2) return null;
            var path = new Vector3[count];
            for (int i = 0; i < count; i++)
                path[i] = TrenchTerrain.SnapToGround(new Vector3(pathX[i], 0f, pathZ[i]));
            return path;
        }

        private void AddPath(Transform parent, string name, Vector3[] curve, float step, float width,
            float parapet, float skirt, Material material, bool conform)
        {
            if (parent == null || curve == null || curve.Length < 2) return;
            Vector3[] path = BuildDitchPath(curve, step);
            if (path == null) return;
            AddMesh(parent, name, path, width, parapet, skirt, material, conform);
        }

        private float[] ToX(Vector3[] curve)
        {
            int length = Mathf.Min(curve.Length, curveX.Length);
            for (int i = 0; i < length; i++) curveX[i] = curve[i].x;
            return curveX;
        }

        private float[] ToZ(Vector3[] curve)
        {
            int length = Mathf.Min(curve.Length, curveZ.Length);
            for (int i = 0; i < length; i++) curveZ[i] = curve[i].z;
            return curveZ;
        }

        private void AddMesh(Transform parent, string name, Vector3[] path, float width, float height, float skirt,
            Material material, bool conformToGround)
        {
            if (parent == null || material == null) return;
            Mesh mesh = TrenchMeshBuilder.BuildEdgeMesh(path, width, height, skirt, line.Threat[0],
                conformToGround ? TrenchTerrain.SnapToGround : (Func<Vector3, Vector3>)null);
            if (mesh == null) return;
            proceduralMeshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>
        /// Low obstacle boxes strung along the whole fire trench (never only its first
        /// stretch): the front line stops vehicles without paying for a collider per segment.
        /// </summary>
        private void AddFrontColliders(Vector3[] path, float width, float parapetHeight, string tag)
        {
            int stride = Mathf.Max(3, path.Length / MaximumColliders);
            for (int i = 0; i < path.Length - 1 && colliders.Count < MaximumColliders; i += stride)
            {
                Vector3 p0 = path[i];
                Vector3 p1 = path[Mathf.Min(i + stride, path.Length - 1)];
                float len = Vector3.Distance(p0, p1);
                if (len < 1.5f) continue;

                Vector3 mid = (p0 + p1) * 0.5f;
                Vector3 fwd = (p1 - p0).normalized;
                var colObj = new GameObject($"DitchCollider_{tag}_{i}");
                // Ignore Raycast layer: the earthwork still blocks vehicles, but placement
                // queries never mistake the mod's own ditch for an obstacle to spawn into.
                colObj.layer = PhysicsLayers.IgnoreRaycast;
                colObj.transform.SetParent(lod0Root.transform, false);
                colObj.transform.localPosition = mid + Vector3.up * (parapetHeight * 0.5f);
                colObj.transform.localRotation = Quaternion.LookRotation(fwd, Vector3.up);
                var box = colObj.AddComponent<BoxCollider>();
                box.size = new Vector3(width + 4f, Mathf.Max(1.2f, parapetHeight * 0.9f), len + 0.5f);
                colliders.Add(box);
            }
        }

        private static float WidthFor(TrenchStage stage)
            => stage >= TrenchStage.Support ? 3.6f : stage >= TrenchStage.FireTrench ? 3.0f : 2.0f;

        private static float ParapetFor(TrenchStage stage)
        {
            switch (stage)
            {
                case TrenchStage.Scrape: return 0.8f;
                case TrenchStage.FireTrench: return 2.0f;
                case TrenchStage.Support: return 2.6f;
                default: return 3.4f;
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
            bool collidersActive = (currentLod == 0 && line != null && !line.Overrun);
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
