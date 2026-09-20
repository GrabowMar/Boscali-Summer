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
        private const float WireForward = 7f;
        private const float WireHeight = 0.75f;

        private TrenchLine line;
        private GameObject lod0Root;
        private GameObject lod1Root;
        private GameObject lod2Root;

        private readonly List<Mesh> proceduralMeshes = new List<Mesh>();
        private readonly List<BoxCollider> colliders = new List<BoxCollider>(MaximumColliders);
        private readonly float[] pathX = new float[MaximumRings];
        private readonly float[] pathZ = new float[MaximumRings];
        private readonly float[] bayExtra = new float[MaximumRings];
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
        public int ActiveLod => currentLod;

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
                // Bays flare the LOD0 fire ditch only: a 1.4m widening is sub-pixel at the mid
                // and far LOD ranges, and the constant silhouettes already read from there.
                AddMesh(lod0Root.transform, "Fire_LOD0", fire, width, parapet, 1.1f, earthMat, true,
                    BuildBaySchedule(fire));
                AddFrontColliders(fire, width, parapet, "Fire");
                AddWireBelt(lod0Root.transform, earthMat);
            }
            // Mid and far LODs stay real earthworks at a coarser ring pitch: a wing flying
            // over the front must still read the parapet line and the belt behind it. The
            // far ridge is the silhouette — berms, no skirts — rather than a flat scar.
            AddPath(lod1Root.transform, "Fire_LOD1", line.Curve, CoarseStep,
                width, parapet * 0.85f, 0.8f, earthMat, true);
            AddPath(lod2Root.transform, "Fire_LOD2", line.Curve, RidgeStep,
                width + 3f, 1.8f, 0.5f, earthMat, true);
            if (line.Support != null)
            {
                AddPath(lod0Root.transform, "Support", line.Support, TraceStep, 2.2f, 1.2f, 1.0f, earthMat, true);
                AddPath(lod1Root.transform, "Support_LOD1", line.Support, CoarseStep, 2.2f, 1.05f, 0.8f, earthMat, true);
                AddPath(lod2Root.transform, "Support_LOD2", line.Support, RidgeStep, 3.4f, 1.5f, 0.5f, earthMat, true);
            }
            if (line.Redoubt != null)
            {
                AddPath(lod0Root.transform, "Redoubt", line.Redoubt, TraceStep, 2.2f, 1.15f, 1.0f, earthMat, true);
                AddPath(lod1Root.transform, "Redoubt_LOD1", line.Redoubt, CoarseStep, 2.2f, 1.0f, 0.8f, earthMat, true);
                AddPath(lod2Root.transform, "Redoubt_LOD2", line.Redoubt, RidgeStep, 3.4f, 1.5f, 0.5f, earthMat, true);
            }
            AddTraces(lod0Root.transform, line.Links, "Link", 1.8f, 1.1f, earthMat);
            AddTraces(lod0Root.transform, line.Spurs, "Sap", 1.3f, 0.8f, earthMat);

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
            {
                // A missed or refused ground probe keeps the curve's own height (planned from
                // successful probes): falling back to y=0 buries the ring under the terrain
                // and spikes the mesh wherever the ray hits water, a tree or a building first.
                int curveIndex = Mathf.RoundToInt((float)i / (count - 1) * (curve.Length - 1));
                path[i] = TrenchTerrain.SnapToGround(new Vector3(pathX[i], curve[curveIndex].y, pathZ[i]));
            }
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

        /// <summary>
        /// Per-ring widening for the fire ditch: the extra half-width at each ring from the
        /// nearest bay node, full through a bay and easing back into the plain ditch between.
        /// Returns the cached buffer (no per-rebuild allocation); a line without nodes yet
        /// returns null and digs the plain ditch.
        /// </summary>
        private float[] BuildBaySchedule(Vector3[] path)
        {
            Vector3[] nodes = line.Nodes;
            if (nodes == null || nodes.Length == 0 || path == null) return null;

            int count = Mathf.Min(path.Length, bayExtra.Length);
            for (int i = 0; i < count; i++)
            {
                float nearestSq = float.MaxValue;
                for (int n = 0; n < nodes.Length; n++)
                {
                    float dx = path[i].x - nodes[n].x;
                    float dz = path[i].z - nodes[n].z;
                    float sq = dx * dx + dz * dz;
                    if (sq < nearestSq) nearestSq = sq;
                }
                bayExtra[i] = TrenchTraceMath.BayExtra(Mathf.Sqrt(nearestSq));
            }
            return bayExtra;
        }

        private void AddMesh(Transform parent, string name, Vector3[] path, float width, float height, float skirt,
            Material material, bool conformToGround, float[] ringExtra = null)
        {
            if (parent == null || material == null) return;
            Mesh mesh = TrenchMeshBuilder.BuildEdgeMesh(path, width, height, skirt, line.Threat[0],
                conformToGround ? TrenchTerrain.SnapToGround : (Func<Vector3, Vector3>)null, ringExtra);
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
            // The band is the forward parapet wall, not the whole cut: the interior stays
            // clear so the crew stands on the ditch floor, and the berm wall in front of
            // them is what stops ground units crossing the line.
            float wallHeight = parapetHeight + 0.6f;
            int stride = Mathf.Max(3, path.Length / MaximumColliders);
            for (int i = 0; i < path.Length - 1 && colliders.Count < MaximumColliders; i += stride)
            {
                Vector3 p0 = path[i];
                Vector3 p1 = path[Mathf.Min(i + stride, path.Length - 1)];
                float len = Vector3.Distance(p0, p1);
                if (len < 1.5f) continue;

                Vector3 mid = (p0 + p1) * 0.5f;
                Vector3 fwd = (p1 - p0).normalized;
                Vector3 threat = line.ThreatAt(mid);
                Vector3 side = new Vector3(threat.x, 0f, threat.z);
                if (side.sqrMagnitude < 0.0001f) side = Vector3.Cross(Vector3.up, fwd);
                else side.Normalize();

                var colObj = new GameObject($"DitchCollider_{tag}_{i}");
                // Ignore Raycast layer: the obstacle boxes block vehicles only, and placement
                // queries never mistake the mod's own ditch for an obstacle to spawn into.
                colObj.layer = PhysicsLayers.IgnoreRaycast;
                colObj.transform.SetParent(lod0Root.transform, false);
                colObj.transform.localPosition = mid + side * (width * 0.5f + 0.55f) +
                    Vector3.up * (wallHeight * 0.5f);
                colObj.transform.localRotation = Quaternion.LookRotation(fwd, Vector3.up);
                var box = colObj.AddComponent<BoxCollider>();
                box.size = new Vector3(width + 1.2f, wallHeight, len + 0.5f);
                colliders.Add(box);
            }
        }

        private static float WidthFor(TrenchStage stage)
            => stage >= TrenchStage.Support ? 1.7f : stage >= TrenchStage.FireTrench ? 1.6f : 1.4f;

        private static float ParapetFor(TrenchStage stage)
        {
            switch (stage)
            {
                case TrenchStage.Scrape: return 0.5f;
                case TrenchStage.FireTrench: return 1.2f;
                case TrenchStage.Support: return 1.3f;
                default: return 1.45f;
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
            // The distance test must run in one coordinate frame. A line's centre is global
            // (terrain probes return GlobalPosition) while the camera is local, so comparing
            // them raw measures the floating origin, not the chunk: every built earthwork
            // reads as tens of kilometres away and sits at LOD3 — fully culled — forever.
            CameraStateManager view = SceneSingleton<CameraStateManager>.i;
            Camera cam = view != null && view.mainCamera != null ? view.mainCamera : Camera.main;
            if (cam == null) return;

            Vector3 center = WorldCenter;
            Vector3 centerLocal = new GlobalPosition(center.x, center.y, center.z).ToLocalPosition();
            float dist = Vector3.Distance(cam.transform.position, centerLocal);
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
