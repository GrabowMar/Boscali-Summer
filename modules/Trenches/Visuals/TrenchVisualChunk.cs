using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Trenches.Domain;
using BoscaliSummer.Modules.Trenches.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Trenches.Visuals
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
        private const float TraceStep = TrenchTraceMath.MeshSpacing;
        private const float CoarseStep = 9f;
        private const float RidgeStep = 18f;
        private const float WireForward = 7f;
        private const float WireHeight = 0.75f;
        private const float OuterWireForward = 24f;
        private const float OuterWireHeight = 0.6f;

        private TrenchLine line;
        private GameObject lod0Root;
        private GameObject lod1Root;
        private GameObject lod2Root;
        private GameObject colliderRoot;

        private readonly List<Mesh> proceduralMeshes = new List<Mesh>();
        private readonly List<BoxCollider> colliders = new List<BoxCollider>(MaximumColliders);
        private readonly float[] pathX = new float[MaximumRings];
        private readonly float[] pathZ = new float[MaximumRings];
        private readonly float[] bayExtra = new float[MaximumRings];
        private readonly float[] curveX = new float[TrenchLine.MaximumCurvePoints];
        private readonly float[] curveZ = new float[TrenchLine.MaximumCurvePoints];
        private readonly Queue<TrenchBuildStep> pendingBuild = new Queue<TrenchBuildStep>();
        private readonly TrenchBuildStep[] buildPlan = new TrenchBuildStep[TrenchTraceMath.MaximumBuildSteps];
        private Vector3[] stagedFirePath;
        // A path step (ring snapping, then mesh rings) and the collider step run over several frames and resume from these.
        private readonly Queue<PathJob> pathJobs = new Queue<PathJob>();
        private int colliderAt = -1;   // >= 0 while the collider step has boxes left to place
        private bool building;
        // Was 3 with a whole step as the unit (a long fire ditch is hundreds of terrain rays: 6 ms in one call);
        // now the budget alone bounds a frame, to this plus the dearest single piece (a mesh finish, a handful of boxes).
        // 1 ms shared by every chunk in a frame (measured: a mesh finish is 1.5 ms by itself, so 2 ms per chunk reached 4 ms).
        private const int RebuildMillisecondsPerFrame = 1;
        private static int budgetFrame = -1;
        private static double budgetSpentMs;

        /// <summary>One ditch mesh in flight: the ring-by-ring ground snap of a path, then the mesh rings.</summary>
        private sealed class PathJob
        {
            public Transform Parent;
            public string Name;
            public Vector3[] Curve;
            public float Step, Width, Parapet, Skirt;
            public Material Material;
            public bool Conform, Fire;
            public Vector3[] Path;
            public int Count, Snapped;
            public TrenchMeshBuilder.EdgeJob Mesh;
        }

        public float Lod0Distance = 600f;
        public float Lod1Distance = 2640f;
        public float Lod2Distance = 12000f;

        /// <summary>
        /// Multiplies the shared earthwork palette on this position's renderers (a property block, so the
        /// material stays shared): the manager sets it from the map's own terrain colour, so turned earth
        /// on red desert reads red and on grey steppe grey instead of one brown everywhere.
        /// </summary>
        public Color EarthTint = Color.white;
        private static MaterialPropertyBlock tintBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

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
            if (colliderRoot == null)
            {
                colliderRoot = new GameObject("ParapetColliders");
                colliderRoot.transform.SetParent(transform, false);
            }

            int planned = TrenchTraceMath.PlanBuildSteps(line.Support != null, line.Redoubt != null,
                line.Links != null ? line.Links.Length : 0, line.Spurs != null ? line.Spurs.Length : 0,
                buildPlan);
            for (int i = 0; i < planned; i++) pendingBuild.Enqueue(buildPlan[i]);
            building = true;
            PumpBuildQueue();
        }

        /// <summary>Works through the staged build until the frame's rebuild budget is spent; what is left resumes next frame.</summary>
        internal void PumpBuildQueue(int budgetMilliseconds = RebuildMillisecondsPerFrame)
        {
            if (!building || line == null) return;
            if (budgetMilliseconds == int.MaxValue) { PumpBuildQueueCore(budgetMilliseconds); return; }
            if (budgetFrame != Time.frameCount) { budgetFrame = Time.frameCount; budgetSpentMs = 0.0; }
            if (budgetSpentMs >= budgetMilliseconds) return;   // other chunks spent this frame's share; this one resumes next frame
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            PumpBuildQueueCore(budgetMilliseconds);
            budgetSpentMs += (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        private void PumpBuildQueueCore(int budgetMilliseconds)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                if (pathJobs.Count > 0)
                {
                    if (!StepPathJob(pathJobs.Peek(), watch, budgetMilliseconds)) return;
                    pathJobs.Dequeue();
                }
                else if (colliderAt >= 0)
                {
                    if (!StepColliders(watch, budgetMilliseconds)) return;
                }
                else if (pendingBuild.Count > 0)
                {
                    RunBuildStep(pendingBuild.Dequeue());
                }
                else
                {
                    building = false;
                    stagedFirePath = null;
                    ApplyTint();
                    currentLod = -1;
                    UpdateLod(true);
                    return;
                }
                if (watch.ElapsedMilliseconds >= budgetMilliseconds) return;
            }
        }

        /// <summary>Finishes a staged rebuild at once: tests and scene teardown, never the frame loop.</summary>
        internal void CompleteBuild()
        {
            PumpBuildQueue(int.MaxValue);
            stagedFirePath = null;
            currentLod = -1;
            UpdateLod(true);
        }

        private void RunBuildStep(TrenchBuildStep step)
        {
            if (line == null || line.Curve == null || line.Curve.Length < 2) return;
            Material earthMat = TrenchMaterialResolver.GetEarthBermMaterial();
            if (earthMat == null) return;
            float width = WidthFor(line.Stage);
            float parapet = ParapetFor(line.Stage);
            switch (step)
            {
                case TrenchBuildStep.Fire:
                    // Bays flare the LOD0 fire ditch only: a 1.4m widening is sub-pixel at the mid
                    // and far LOD ranges, and the constant silhouettes already read from there.
                    // The job sets stagedFirePath once its path is snapped; the colliders and the belts come after it.
                    pathJobs.Enqueue(new PathJob
                    {
                        Parent = lod0Root.transform, Name = "Fire_LOD0", Curve = line.Curve, Step = TraceStep,
                        Width = width, Parapet = parapet, Skirt = 1.1f, Material = earthMat, Conform = true, Fire = true
                    });
                    break;
                case TrenchBuildStep.Colliders:
                    if (stagedFirePath != null) colliderAt = 0;
                    break;
                case TrenchBuildStep.Wire:
                case TrenchBuildStep.WireOuter:
                    // The wired approach rides the fire ditch: no ditch, no belts. The second,
                    // lower belt further out is the double-apron read of a real wired approach.
                    if (stagedFirePath == null) break;
                    Material wireMat = TrenchMaterialResolver.GetWireMaterial();
                    if (step == TrenchBuildStep.Wire)
                        AddWireBelt(lod0Root.transform, wireMat ?? earthMat, WireForward, WireHeight, "WireBelt");
                    else
                        AddWireBelt(lod0Root.transform, wireMat ?? earthMat, OuterWireForward, OuterWireHeight, "WireBelt_Outer");
                    break;
                case TrenchBuildStep.Cover:
                    // Roofed lengths between the open firing bays, once the position matures
                    // past its open stage. Rides the fire ditch like the wire; a stage gate,
                    // not a rebuild, decides it, so every peer agrees from replicated state.
                    if (stagedFirePath == null || !TrenchTraceMath.HasOverheadCover(line.Stage)) break;
                    Mesh cover = TrenchMeshBuilder.BuildOverheadCoverMesh(stagedFirePath, width, line.Nodes);
                    if (cover == null) break;
                    proceduralMeshes.Add(cover);
                    var coverGo = new GameObject("OverheadCover");
                    coverGo.transform.SetParent(lod0Root.transform, false);
                    coverGo.AddComponent<MeshFilter>().sharedMesh = cover;
                    coverGo.AddComponent<MeshRenderer>().sharedMaterial = earthMat;
                    break;
                case TrenchBuildStep.Support:
                    AddPath(lod0Root.transform, "Support", line.Support, TraceStep, 2.2f, 1.2f, 1.0f, earthMat, true);
                    break;
                case TrenchBuildStep.Redoubt:
                    AddPath(lod0Root.transform, "Redoubt", line.Redoubt, TraceStep, 2.2f, 1.15f, 1.0f, earthMat, true);
                    break;
                case TrenchBuildStep.Links:
                    AddTraces(lod0Root.transform, line.Links, "Link", 1.8f, 1.1f, earthMat);
                    break;
                case TrenchBuildStep.Spurs:
                    AddTraces(lod0Root.transform, line.Spurs, "Sap", 1.3f, 0.8f, earthMat);
                    break;
                // Mid and far LODs stay real earthworks at a coarser ring pitch: the far ridge
                // is the silhouette (berms, no skirts) rather than a flat scar.
                case TrenchBuildStep.FireLod1:
                    AddPath(lod1Root.transform, "Fire_LOD1", line.Curve, CoarseStep, width, parapet * 0.85f, 0.8f, earthMat, true);
                    break;
                case TrenchBuildStep.FireLod2:
                    AddPath(lod2Root.transform, "Fire_LOD2", line.Curve, RidgeStep, width + 3f, 1.8f, 0.5f, earthMat, true);
                    break;
                case TrenchBuildStep.SupportLod1:
                    AddPath(lod1Root.transform, "Support_LOD1", line.Support, CoarseStep, 2.2f, 1.05f, 0.8f, earthMat, true);
                    break;
                case TrenchBuildStep.SupportLod2:
                    AddPath(lod2Root.transform, "Support_LOD2", line.Support, RidgeStep, 3.4f, 1.5f, 0.5f, earthMat, true);
                    break;
                case TrenchBuildStep.RedoubtLod1:
                    AddPath(lod1Root.transform, "Redoubt_LOD1", line.Redoubt, CoarseStep, 2.2f, 1.0f, 0.8f, earthMat, true);
                    break;
                case TrenchBuildStep.RedoubtLod2:
                    AddPath(lod2Root.transform, "Redoubt_LOD2", line.Redoubt, RidgeStep, 3.4f, 1.5f, 0.5f, earthMat, true);
                    break;
            }
        }

        private void AddTraces(Transform parent, Vector3[][] traces, string name, float width, float parapet,
            Material material)
        {
            if (traces == null || parent == null) return;
            for (int i = 0; i < traces.Length; i++)
                AddPath(parent, $"{name}{i}", traces[i], TraceStep, width, parapet, 1.4f, material, true);
        }

        /// <summary>
        /// A wire belt in front of the fire trench: pickets and strands offset onto the enemy
        /// side of the parapet and draped over the ground, so the approach reads as wired no
        /// man's land instead of bare field.
        /// </summary>
        private void AddWireBelt(Transform parent, Material material, float forward, float height, string name)
        {
            if (parent == null || material == null || line == null) return;
            Vector3[] wireCurve = OffsetCurve(line.Curve, line.Threat, forward);
            if (wireCurve == null) return;
            Vector3[] path = BuildDitchPath(wireCurve, CoarseStep);
            if (path == null) return;

            Mesh mesh = TrenchMeshBuilder.BuildWireBeltMesh(path, height, line.Spurs);
            if (mesh == null) return;
            proceduralMeshes.Add(mesh);
            var go = new GameObject(name);
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
            int count = DensifyDitch(curve, step);
            if (count < 2) return null;
            var path = new Vector3[count];
            for (int i = 0; i < count; i++) path[i] = SnapRing(curve, i, count);
            return path;
        }

        /// <summary>The curve's traversed ditch rings in pathX/pathZ (no terrain rays yet); the ring count, 0 for no curve.</summary>
        private int DensifyDitch(Vector3[] curve, float step)
        {
            if (curve == null || curve.Length < 2) return 0;
            return TrenchTraceMath.DensifyTraversed(
                ToX(curve), ToZ(curve), curve.Length, step,
                TrenchTraceMath.TraverseSpacing, TrenchTraceMath.TraverseAmplitude,
                curve[0].x * 0.5f + curve[0].z * 0.5f, pathX, pathZ);
        }

        /// <summary>Ring <paramref name="i"/> of <paramref name="count"/> laid on the ground: one terrain ray.</summary>
        private Vector3 SnapRing(Vector3[] curve, int i, int count)
        {
            // A missed probe takes the height of the nearest curve station. Not the station at the
            // ring's share of the ring count: rings are not spread evenly over the stations, so that
            // read a height from far along the line and stood the ring metres in the air.
            if (TrenchTerrain.TryTerrain(pathX[i], pathZ[i], out Vector3 ground)) return ground;
            int nearest = 0;
            float bestSq = float.MaxValue;
            for (int c = 0; c < curve.Length; c++)
            {
                float dx = curve[c].x - pathX[i], dz = curve[c].z - pathZ[i], sq = dx * dx + dz * dz;
                if (sq < bestSq) { bestSq = sq; nearest = c; }
            }
            return new Vector3(pathX[i], curve[nearest].y, pathZ[i]);
        }

        private void AddPath(Transform parent, string name, Vector3[] curve, float step, float width,
            float parapet, float skirt, Material material, bool conform)
        {
            if (parent == null || curve == null || curve.Length < 2) return;
            pathJobs.Enqueue(new PathJob
            {
                Parent = parent, Name = name, Curve = curve, Step = step,
                Width = width, Parapet = parapet, Skirt = skirt, Material = material, Conform = conform
            });
        }

        /// <summary>
        /// Advances one path job until the frame's budget is spent; true when its mesh is on the scene. Its rings are
        /// snapped to the ground, then the mesh rings are laid, one terrain ray or two per ring, so a long ditch spreads
        /// over frames instead of being one call. The scratch rings (pathX/pathZ) belong to this job until it finishes:
        /// the pump starts no other step meanwhile.
        /// </summary>
        private bool StepPathJob(PathJob job, System.Diagnostics.Stopwatch watch, int budgetMilliseconds)
        {
            if (job.Parent == null || job.Material == null || line == null) return true;
            if (job.Path == null)
            {
                int count = DensifyDitch(job.Curve, job.Step);
                if (count < 2) return true;
                job.Count = count;
                job.Path = new Vector3[count];
            }
            while (job.Snapped < job.Count)
            {
                job.Path[job.Snapped] = SnapRing(job.Curve, job.Snapped, job.Count);
                job.Snapped++;
                if (watch.ElapsedMilliseconds >= budgetMilliseconds) return false;
            }
            if (job.Mesh == null)
            {
                float[] bays = null;
                if (job.Fire)
                {
                    stagedFirePath = job.Path;
                    bays = BuildBaySchedule(job.Path);
                }
                job.Mesh = new TrenchMeshBuilder.EdgeJob(job.Path, job.Width, job.Parapet, job.Skirt, line.Threat[0],
                    job.Conform ? TrenchTerrain.SnapToGround : (Func<Vector3, Vector3>)null, bays);
            }
            while (!job.Mesh.Done)
            {
                job.Mesh.LayRings(1);
                if (!job.Mesh.Done && watch.ElapsedMilliseconds >= budgetMilliseconds) return false;
            }
            // The finish (mesh upload) is one 1.5 ms piece: start it only on a nearly fresh budget, else next frame.
            if (budgetMilliseconds != int.MaxValue && watch.Elapsed.TotalMilliseconds > 0.4) return false;
            Mesh mesh = job.Mesh.Finish();
            if (mesh == null) return true;
            proceduralMeshes.Add(mesh);
            var go = new GameObject(job.Name);
            go.transform.SetParent(job.Parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = job.Material;
            return true;
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

        /// <summary>
        /// Low obstacle boxes strung along the whole fire trench (never only its first
        /// stretch): the front line stops vehicles without paying for a collider per segment.
        /// A few boxes per call (each is a GameObject and a physics body); false while some are left.
        /// </summary>
        private bool StepColliders(System.Diagnostics.Stopwatch watch, int budgetMilliseconds)
        {
            Vector3[] path = stagedFirePath;
            if (path == null || colliderRoot == null) { colliderAt = -1; return true; }
            float width = WidthFor(line.Stage);
            string tag = "Fire";
            // The band is the forward parapet wall, not the whole cut: the interior stays
            // clear so the ditch floor reads as a walkable trench, and the berm wall in front
            // of it is what stops ground units crossing the line.
            float wallHeight = ParapetFor(line.Stage) + 0.6f;
            int stride = Mathf.Max(3, path.Length / MaximumColliders);
            for (; colliderAt < path.Length - 1 && colliders.Count < MaximumColliders; colliderAt += stride)
            {
                if (watch.ElapsedMilliseconds >= budgetMilliseconds) return false;   // resumes at this box
                int i = colliderAt;
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
                // Physics belongs to the host's line, not the viewing camera's mesh LOD.
                colObj.transform.SetParent(colliderRoot.transform, false);
                colObj.transform.localPosition = mid + side * (width * 0.5f + 0.55f) +
                    Vector3.up * (wallHeight * 0.5f);
                colObj.transform.localRotation = Quaternion.LookRotation(fwd, Vector3.up);
                var box = colObj.AddComponent<BoxCollider>();
                box.size = new Vector3(0.9f, wallHeight, len + 0.5f);
                colliders.Add(box);
            }
            colliderAt = -1;
            return true;
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

        // No per-instance Update: one static driver ticks every live chunk (a native-to-managed
        // call per chunk per frame bought nothing between the 0.25 s LOD checks).
        private static readonly List<TrenchVisualChunk> live = new List<TrenchVisualChunk>();
        private static TrenchChunkDriver driver;
        private static int cameraFrame = -1;
        private static Camera frameCamera;

        private void OnEnable()
        {
            if (!live.Contains(this)) live.Add(this);
            if (driver == null)
            {
                var go = new GameObject("TrenchChunkDriver") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                driver = go.AddComponent<TrenchChunkDriver>();
            }
        }

        private void OnDisable() => live.Remove(this);

        private void Tick(float now)
        {
            if (building) PumpBuildQueue();
            if (now < nextLodCheckTime) return;
            nextLodCheckTime = now + 0.25f;

            UpdateLod(false);
        }

        /// <summary>The view camera, resolved at most once per frame however many chunks ask.</summary>
        private static Camera ViewCamera()
        {
            int frame = Time.frameCount;
            if (frame != cameraFrame || frameCamera == null)
            {
                cameraFrame = frame;
                CameraStateManager view = SceneSingleton<CameraStateManager>.i;
                frameCamera = view != null && view.mainCamera != null ? view.mainCamera : Camera.main;
            }
            return frameCamera;
        }

        private sealed class TrenchChunkDriver : MonoBehaviour
        {
            private void Update()
            {
                float now = Time.time;
                // Index loop with a live bound: a tick may destroy or rebuild a chunk.
                for (int i = 0; i < live.Count; i++)
                {
                    TrenchVisualChunk chunk = live[i];
                    if (chunk == null) { live.RemoveAt(i--); continue; }
                    int before = live.Count;
                    chunk.Tick(now);
                    if (live.Count < before) i -= before - live.Count;
                }
            }
        }

        private void UpdateLod(bool force)
        {
            bool collidersActive = NetworkSceneSingleton<Spawner>.i?.IsServer == true &&
                line != null && !line.Overrun;
            for (int i = 0; i < colliders.Count; i++)
                if (colliders[i] != null && colliders[i].enabled != collidersActive)
                    colliders[i].enabled = collidersActive;

            // The distance test must run in one coordinate frame. A line's centre is global
            // (terrain probes return GlobalPosition) while the camera is local, so comparing
            // them raw measures the floating origin, not the chunk: every built earthwork
            // reads as tens of kilometres away and sits at LOD3 — fully culled — forever.
            Camera cam = ViewCamera();
            if (cam == null) return;

            // Measured to the nearest station, not the centre: a position runs up to 2.4km, so a
            // camera standing on one end sat 1.2km from the centre and saw the mid LOD (no bays,
            // wire, cover or saps) at its feet. The global-to-local shift is one translation.
            Vector3 center = WorldCenter;
            Vector3 camGlobal = cam.transform.position + center -
                new GlobalPosition(center.x, center.y, center.z).ToLocalPosition();
            float nearestSq = NearestSq(line?.Curve, camGlobal, float.MaxValue);
            nearestSq = NearestSq(line?.Support, camGlobal, nearestSq);
            nearestSq = NearestSq(line?.Redoubt, camGlobal, nearestSq);
            if (nearestSq == float.MaxValue) nearestSq = (camGlobal - center).sqrMagnitude;
            float dist = Mathf.Sqrt(nearestSq);
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

        }

        private void ApplyTint()
        {
            if (EarthTint == Color.white) return;
            if (tintBlock == null) tintBlock = new MaterialPropertyBlock();
            Material earth = TrenchMaterialResolver.GetEarthBermMaterial();
            tintBlock.SetColor(BaseColorId, EarthTint);
            tintBlock.SetColor(ColorId, EarthTint);
            foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.sharedMaterial == earth) renderer.SetPropertyBlock(tintBlock);
        }

        private static float NearestSq(Vector3[] points, Vector3 from, float best)
        {
            if (points == null) return best;
            for (int i = 0; i < points.Length; i++)
            {
                float sq = (points[i] - from).sqrMagnitude;
                if (sq < best) best = sq;
            }
            return best;
        }

        private void ClearMeshes()
        {
            for (int i = 0; i < colliders.Count; i++)
                if (colliders[i] != null) colliders[i].enabled = false;
            colliders.Clear();

            if (lod0Root != null) { Destroy(lod0Root); lod0Root = null; }
            if (lod1Root != null) { Destroy(lod1Root); lod1Root = null; }
            if (lod2Root != null) { Destroy(lod2Root); lod2Root = null; }
            if (colliderRoot != null) { Destroy(colliderRoot); colliderRoot = null; }

            for (int i = 0; i < proceduralMeshes.Count; i++)
            {
                if (proceduralMeshes[i] != null) Destroy(proceduralMeshes[i]);
            }
            proceduralMeshes.Clear();
            pendingBuild.Clear();
            pathJobs.Clear();
            colliderAt = -1;
            building = false;
            stagedFirePath = null;
        }

        private void OnDestroy()
        {
            ClearMeshes();
        }
    }
}
