using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BoscaliSummer.Core.Lifecycle;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Custom structural damage on the game's own building meshes. Hits carve noisy holes
    /// (worker thread) and drop the removed surface as thick tumbling chunks; death leaves a
    /// jagged stub, drops Voronoi sections into a rising rubble mound and hides the native
    /// wreck renderers. Visual only: native HP, colliders and obstacles are never touched.
    /// </summary>
    internal sealed class BuildingCarver : MonoBehaviour, ISceneService
    {
        private const int MaxOwners = 12, MaxRuins = 6, MaxMoving = 64, MaxLobes = 48, GroundGrid = 6;

        private sealed class Part
        {
            internal NativeSurface Surface;
            internal GameObject Root;
            internal Mesh Mesh;
            internal Material[] Materials;
            internal int Version, Applied;
        }

        private sealed class Owner
        {
            internal int Id;
            internal readonly List<Part> Parts = new List<Part>(2);
            internal readonly List<Renderer> Hidden = new List<Renderer>();
            internal readonly List<bool> HiddenState = new List<bool>();
            internal readonly List<Lobe> Lobes = new List<Lobe>(MaxLobes);
            internal readonly List<Slab> Slabs = new List<Slab>();
            internal int WreckRenderers, WreckColliders;
            internal Transform Building, Wreck;
            internal MapBuilding Support;
            internal readonly List<Renderer> WreckList = new List<Renderer>();
            internal Bounds Bounds; // Datum-local
            internal float GroundX0, GroundX1, GroundZ0, GroundZ1, Ground;
            internal readonly float[] Lattice = new float[GroundGrid * GroundGrid];
            internal readonly float[] Raw = new float[GroundGrid * GroundGrid];
            internal readonly List<Vector3> Lip = new List<Vector3>(); // Mound-local outer vertices that rest on real ground.
            internal bool Dead;
            internal float At, BreakBase, BreakAmp, MoundBorn = -1f;
            internal GameObject Mound;
            internal Mesh MoundMesh;
            internal int Seed;
        }

        private sealed class Moving
        {
            internal GameObject Root;
            internal Mesh Mesh;
            internal Owner Owner;
            internal Vector3 Start, Velocity, Axis, Outward;
            internal Quaternion Rotation;
            internal Vector3[] Hull;
            internal float Born, Spin, Radius, ExtentY, Delay, MaxTilt;
            internal bool Section, Landed, Dusted, Released, Shared;
            internal Vector3 Last;
        }

        private sealed class Job
        {
            internal Task<SurfaceCutter> Task;
            internal Action<SurfaceCutter> Apply;
            internal double Ms;
        }

        private sealed class Batch { internal int Remaining; internal readonly List<Action> Ready = new List<Action>(); }

        public static BuildingCarver Instance { get; private set; }
        private readonly List<Owner> owners = new List<Owner>(MaxOwners);
        private readonly List<Moving> moving = new List<Moving>(MaxMoving);
        private readonly List<Job> jobs = new List<Job>(16);
        private readonly RaycastHit[] probe = new RaycastHit[8];
        private Transform lastHit;
        private readonly Dictionary<MapBuilding, int> supports = new Dictionary<MapBuilding, int>();
        private Material interior, edge, rubble;
        private Texture2D interiorTexture, concreteTexture;
        private readonly BuildingFires fires = new BuildingFires();
        private readonly List<(Owner Owner, Vector3 At, Vector3 Direction, float Size)> sprays = new List<(Owner, Vector3, Vector3, float)>(8);
        private Mesh[] chunkMeshes;
        private Vector3[] chunkHull;
        private Light[] flashes;
        private readonly float[] flashPeak = new float[3], flashAt = new float[3];
        private int flashHead;

        internal double MaxCarveMs { get; private set; }
        internal double MaxApplyMs { get; private set; }
        internal double LastCarveMs { get; private set; }
        internal int CarveCount { get; private set; }
        internal int PendingJobs => jobs.Count;

        private void Awake() => Instance = this;
        private void Start()
        {
            if (GameManager.IsHeadless || !Plugin.Settings.FireAndDestruction.ImpactScorchEnabled.Value) return;
            NativeSurfaceLibrary.BeginLoad();
            EnsureMaterials();
            StartCoroutine(WarmUp());
        }
        private void OnDestroy() { Clear(); if (Instance == this) Instance = null; }
        public void ResetForScene() { Clear(); if (isActiveAndEnabled) Start(); }

        // ---------- public seams ----------

        internal double LastHitMs { get; private set; }

        internal bool Hit(MapBuilding building, Vector3 point, Vector3 normal, float diameter)
        {
            if (diameter < 1f) return false; // Cannon chips stay in the particle pool.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Owner owner = Get(building);
            if (owner == null) return false;
            Carve(owner, point, normal, diameter * 0.5f);
            LastHitMs = clock.Elapsed.TotalMilliseconds;
            return true;
        }

        /// <summary>
        /// Scene-load warm-up: run the whole hit path once on a real building and undo it in the
        /// same frame, so JIT and first-use costs land in the loading screen, not the first strike.
        /// </summary>
        private System.Collections.IEnumerator WarmUp()
        {
            while (NativeSurfaceLibrary.Count == 0) yield return null;
            yield return null;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            foreach (MapBuilding building in FindObjectsOfType<MapBuilding>())
            {
                Owner owner = Get(building);
                if (owner == null) continue;
                Carve(owner, owner.Parts[0].Root.GetComponent<Renderer>().bounds.center, Vector3.forward, 3f);
                Release(owner);
                break;
            }
            WarmUpMs = clock.Elapsed.TotalMilliseconds;
        }
        internal double WarmUpMs { get; private set; }

        /// <summary>Called before the native object dies so the full surface survives the frame.</summary>
        internal bool PrepareCollapse(MapBuilding building) => Get(building) != null;

        /// <summary>Probed street level (world Y) of a carved building; its pivot and bounds can sit far underground.</summary>
        internal bool TryStreetLevel(MapBuilding building, out float worldY)
        {
            worldY = 0f;
            int id = building != null ? building.GetInstanceID() : 0;
            Owner owner = owners.Find(o => o.Id == id);
            if (owner == null) return false;
            worldY = Datum.origin.TransformPoint(new Vector3(0f, owner.Ground, 0f)).y;
            return true;
        }

        internal bool Collapse(int id, GameObject nativeWreck, bool animate)
        {
            Owner owner = owners.Find(o => o.Id == id);
            if (owner == null || owner.Dead) return false;
            owner.Dead = true; owner.At = Time.timeSinceLevelLoad;
            float top = owner.Bounds.max.y, height = Mathf.Max(3f, top - owner.Ground);
            // Low base, wide swing: some bays drop to the ground, some walls stand a third of the height.
            owner.BreakBase = owner.Ground + Mathf.Clamp(height * 0.13f, 2f, 8f);
            owner.BreakAmp = Mathf.Clamp(height * 0.22f, 2f, 12f);
            if (nativeWreck != null)
            {
                // Hide only the wreck's meshes; its colliders and obstacle stay native.
                // Held back while the sections fall, then the vanilla skeleton stands in the rubble.
                foreach (MeshRenderer renderer in nativeWreck.GetComponentsInChildren<MeshRenderer>(true))
                { Hide(owner, renderer); owner.WreckList.Add(renderer); owner.WreckRenderers++; }
                owner.Wreck = nativeWreck.transform;
                owner.WreckColliders = nativeWreck.GetComponentsInChildren<Collider>(true).Length;
            }
            owner.Slabs.RemoveAll(s => s.C.y > owner.BreakBase - owner.BreakAmp);

            var random = new System.Random(owner.Seed);
            Vector3[] sites = null;
            if (animate)
            {
                sites = new Vector3[height > 28f ? 6 : 4];
                Bounds b = owner.Bounds;
                for (int i = 0; i < sites.Length; i++)
                    sites[i] = new Vector3(
                        Mathf.Lerp(b.min.x, b.max.x, 0.2f + 0.6f * (float)random.NextDouble()),
                        Mathf.Lerp(owner.BreakBase, top, 0.25f + 0.55f * (float)random.NextDouble()),
                        Mathf.Lerp(b.min.z, b.max.z, 0.2f + 0.6f * (float)random.NextDouble()));
            }
            var batch = new Batch { Remaining = owner.Parts.Count * (1 + (sites?.Length ?? 0)) };
            foreach (Part part in owner.Parts)
            {
                Part p = part;
                int version = ++p.Version;
                Launch(p.Surface, Field(owner, p, CarveField.Kind.Keep, int.MaxValue, true, null, 0), true,
                    cut => Join(batch, () => { if (version >= p.Applied) { p.Applied = version; cut.Apply(p.Mesh, Vector3.zero); } }, owner));
                if (sites == null) continue;
                for (int i = 0; i < sites.Length; i++)
                {
                    int cell = i, seed = random.Next();
                    Launch(p.Surface, Field(owner, p, CarveField.Kind.Section, int.MaxValue, true, sites, cell), true,
                        cut => Join(batch, () => SpawnSection(owner, p, cut, cell, seed), owner));
                }
            }
            BuildMound(owner);
            if (!animate) { owner.MoundBorn = -100f; owner.Mound.transform.localScale = Vector3.one; }
            // Pockets of fire smoulder in the heap; late-join replays stay cold.
            if (animate)
                for (int i = 0, n = height > 28f ? 3 : 2; i < n; i++)
                {
                    Vector3 at = owner.Bounds.center + new Vector3((i - 0.5f * (n - 1)) * owner.Bounds.extents.x * 0.6f, 0f, (i % 2 == 0 ? -0.3f : 0.3f) * owner.Bounds.extents.z);
                    at.y = GroundAt(owner, at) + 1.5f;
                    fires.Ignite(Datum.origin.TransformPoint(at).ToGlobalPosition(), 0.6f + 0.15f * i);
                }
            // The building it stood on (a podium) is crushed when the sections hit it. Native damage on
            // the host only, so the kill replicates and runs this same collapse for the support.
            if (animate && owner.Support != null && BoscaliSummer.Core.Game.GameAccess.IsServer())
                StartCoroutine(Crush(owner, owner.Support));
            while (CountRuins() > MaxRuins)
            {
                Owner oldest = null;
                foreach (Owner o in owners) if (o.Dead && o != owner && (oldest == null || o.At < oldest.At)) oldest = o;
                if (oldest == null) break;
                Release(oldest);
            }
            return true;
        }

        private System.Collections.IEnumerator Crush(Owner upper, MapBuilding support)
        {
            yield return new WaitForSeconds(1.1f);
            if (support == null) yield break;
            support.TakeDamage(0f, 0f, 1f, 0f, 100000f, default);
            // Chunks resting on that roof fall again and sweep down to whatever is below now.
            float now = Time.timeSinceLevelLoad;
            foreach (Moving m in moving)
                if (!m.Section && m.Landed && m.Root != null)
                {
                    m.Landed = false; m.Born = now; m.Velocity = Vector3.zero; m.Spin = 40f;
                    m.Start = m.Last = m.Root.transform.localPosition; m.Rotation = m.Root.transform.localRotation;
                }
            // The upper stub and heap stood on that roof; they go down with it, under the dust.
            if (!owners.Contains(upper)) yield break;
            foreach (Part part in upper.Parts) { Destroy(part.Root); Destroy(part.Mesh); part.Applied = int.MaxValue; }
            upper.Parts.Clear();
            if (upper.Mound != null) Destroy(upper.Mound);
            if (upper.MoundMesh != null) Destroy(upper.MoundMesh);
            upper.Mound = null; upper.MoundMesh = null;
        }

        /// <summary>A blast near a custom ruin bites the stub again. True when it landed on one.</summary>
        internal bool Poke(Vector3 point, float power)
        {
            Vector3 local = Datum.origin.InverseTransformPoint(point);
            foreach (Owner owner in owners)
            {
                if (!owner.Dead) continue;
                Bounds b = owner.Bounds; b.Expand(4f);
                if (!b.Contains(local) || local.y > owner.BreakBase + owner.BreakAmp + 3f) continue;
                Vector3 centre = b.center; centre.y = local.y;
                Carve(owner, point, (local - centre).normalized, Mathf.Clamp(2.5f + power * 0.8f, 2.5f, 6f));
                return true;
            }
            return false;
        }

        // ---------- carving ----------

        private void Carve(Owner owner, Vector3 worldPoint, Vector3 normal, float radius)
        {
            owner.At = Time.timeSinceLevelLoad;
            if (owner.Lobes.Count + 3 > MaxLobes) return; // Saturated: dust and decals only.
            if (normal.sqrMagnitude < 0.01f) normal = Vector3.up;
            normal.Normalize();
            Vector3 c = Datum.origin.InverseTransformPoint(worldPoint) - normal * radius * 0.3f;
            Vector3 t1 = Vector3.Cross(normal, Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
            Vector3 t2 = Vector3.Cross(normal, t1);
            var random = new System.Random(owner.Seed ^ owner.Lobes.Count * 7919 ^ Mathf.RoundToInt(c.x * 13f + c.z * 7f));
            float R(float lo, float hi) => lo + (hi - lo) * (float)random.NextDouble();
            int fresh = owner.Lobes.Count;
            owner.Lobes.Add(new Lobe { C = c, R = radius * R(0.75f, 0.9f) });
            owner.Lobes.Add(new Lobe { C = c + t1 * radius * R(-0.7f, 0.7f) + t2 * radius * R(0.1f, 0.5f), R = radius * R(0.4f, 0.6f) });
            owner.Lobes.Add(new Lobe { C = c + t1 * radius * R(-0.6f, 0.6f) - t2 * radius * R(0.3f, 0.7f), R = radius * R(0.35f, 0.55f) });
            // Blast power (breach radius 1.5..7 m) drives everything: heavy warheads tear ragged
            // satellite holes, throw more and faster pieces and flash brighter.
            float power = Mathf.Clamp01((radius * 2f - 3f) / 11f);
            int satellites = power > 0.55f ? 2 : power > 0.3f ? 1 : 0;
            for (int s = 0; s < satellites && owner.Lobes.Count + 1 <= MaxLobes; s++)
            {
                float angle = R(0f, Mathf.PI * 2f);
                owner.Lobes.Add(new Lobe { C = c + (t1 * Mathf.Cos(angle) + t2 * Mathf.Sin(angle)) * radius * R(0.8f, 1.1f), R = radius * R(0.3f, 0.45f) });
            }
            var sites = new Vector3[2 + Mathf.RoundToInt(power * 3f)];
            for (int s = 0; s < sites.Length; s++)
            {
                float angle = s * Mathf.PI * 2f / sites.Length + R(-0.4f, 0.4f);
                sites[s] = c + (t1 * Mathf.Cos(angle) + t2 * Mathf.Sin(angle)) * radius * R(0.35f, 0.6f);
            }
            Flash(worldPoint + normal * 1.5f, power);
            // Medium and heavy warheads leave the storey burning just behind the breach.
            if (power > 0.2f) fires.Ignite((worldPoint - normal * 1.2f).ToGlobalPosition(), power);
            Spray(owner, Datum.origin.InverseTransformPoint(worldPoint) + normal * 0.5f, normal, 3 + Mathf.RoundToInt(power * 6f), 4f + power * 9f, 0.25f + power * 0.35f);
            Vector3 inward = -normal; inward.y = 0f;
            if (inward.sqrMagnitude > 0.25f && !owner.Dead)
            {
                inward.Normalize();
                // Storey slabs cross the breach, measured up from the probed street level.
                for (float y = owner.Ground + 0.05f; y < owner.Bounds.max.y - 2f; y += 3.3f)
                    if (Mathf.Abs(y - c.y) < radius * 0.95f)
                        owner.Slabs.Add(new Slab { C = new Vector3(c.x, y + owner.Slabs.Count * 0.004f, c.z), Inward = inward, R = radius });
            }
            foreach (Part part in owner.Parts)
            {
                Part p = part;
                int version = ++p.Version;
                Launch(p.Surface, Field(owner, p, CarveField.Kind.Keep, int.MaxValue, owner.Dead, null, 0), true, cut =>
                {
                    if (version < p.Applied) return;
                    p.Applied = version; cut.Apply(p.Mesh, Vector3.zero);
                });
                for (int i = 0; i < sites.Length; i++)
                {
                    int seed = random.Next();
                    Launch(p.Surface, Field(owner, p, CarveField.Kind.Fresh, fresh, owner.Dead, sites, i), true,
                        cut => SpawnFragment(owner, p, cut, normal, seed, power));
                }
            }
        }

        private static CarveField Field(Owner owner, Part part, CarveField.Kind mode, int freshFrom, bool broken, Vector3[] sites, int cell)
        {
            Transform t = part.Root.transform;
            Matrix4x4 toLocal = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale).inverse;
            float scale = 1f / Mathf.Max(0.01f, t.localScale.x);
            var field = new CarveField { Mode = mode, FreshFrom = freshFrom, Break = broken, Cell = cell, Seed = (owner.Seed & 1023) * 0.37f };
            field.Lobes = new Lobe[owner.Lobes.Count];
            for (int i = 0; i < field.Lobes.Length; i++)
                field.Lobes[i] = new Lobe { C = toLocal.MultiplyPoint3x4(owner.Lobes[i].C), R = owner.Lobes[i].R * scale };
            if (broken)
            {
                field.BreakBase = toLocal.MultiplyPoint3x4(new Vector3(owner.Bounds.center.x, owner.BreakBase, owner.Bounds.center.z)).y;
                field.BreakAmp = owner.BreakAmp * scale;
            }
            if (sites != null)
            {
                field.Sites = new Vector3[sites.Length];
                for (int i = 0; i < sites.Length; i++) field.Sites[i] = toLocal.MultiplyPoint3x4(sites[i]);
            }
            // Falling sections are dust-covered and brief; the stub's break line needs less detail than a breach.
            field.Leaf = mode == CarveField.Kind.Section ? 1.6f : broken ? 1.0f : 0.75f;
            if (mode == CarveField.Kind.Keep && owner.Slabs.Count > 0 && part == owner.Parts[0])
            {
                field.Slabs = new Slab[owner.Slabs.Count];
                for (int i = 0; i < field.Slabs.Length; i++)
                {
                    Slab s = owner.Slabs[i];
                    field.Slabs[i] = new Slab { C = toLocal.MultiplyPoint3x4(s.C), Inward = toLocal.MultiplyVector(s.Inward).normalized, R = s.R * scale };
                }
            }
            return field;
        }

        private void Launch(NativeSurface surface, CarveField field, bool withInterior, Action<SurfaceCutter> apply)
        {
            var job = new Job { Apply = apply };
            job.Task = Task.Run(() =>
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var cutter = new SurfaceCutter();
                cutter.Run(surface, field, withInterior);
                job.Ms = clock.Elapsed.TotalMilliseconds;
                return cutter;
            });
            jobs.Add(job);
        }

        private void Join(Batch batch, Action ready, Owner owner)
        {
            batch.Ready.Add(ready);
            if (--batch.Remaining > 0 || !owners.Contains(owner)) return;
            foreach (Action action in batch.Ready) action();
            if (owner.MoundBorn > -50f) owner.MoundBorn = Time.timeSinceLevelLoad;
        }

        // ---------- owners ----------

        private Owner Get(MapBuilding building)
        {
            if (building == null || GameManager.IsHeadless || !Plugin.Settings.FireAndDestruction.ImpactScorchEnabled.Value || !EnsureMaterials()) return null;
            int id = building.GetInstanceID();
            Owner owner = owners.Find(o => o.Id == id);
            if (owner != null) return owner.Dead ? null : owner;
            MeshFilter[] filters = building.GetComponentsInChildren<MeshFilter>(false);
            foreach (MeshFilter filter in filters)
            {
                Mesh shared = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (shared == null || renderer == null || !renderer.enabled || !NativeSurfaceLibrary.TryGet(shared.name, out NativeSurface surface)) continue;
                if (owner == null)
                {
                    if (owners.Count >= MaxOwners) Evict();
                    owner = new Owner { Id = id, At = Time.timeSinceLevelLoad, Seed = id * 486187739 };
                }
                var part = new Part { Surface = surface, Root = new GameObject("BoscaliSummer.CarvedBuilding") { layer = filter.gameObject.layer } };
                Transform t = part.Root.transform;
                t.SetParent(Datum.origin, false);
                t.SetPositionAndRotation(filter.transform.position, filter.transform.rotation);
                t.localScale = filter.transform.lossyScale;
                part.Mesh = new Mesh { name = "Carved " + shared.name };
                Material[] native = renderer.sharedMaterials;
                part.Materials = new Material[surface.Sub.Length + 2];
                for (int i = 0; i < surface.Sub.Length; i++) part.Materials[i] = i < native.Length ? native[i] : edge;
                part.Materials[surface.Sub.Length] = interior;
                part.Materials[surface.Sub.Length + 1] = edge;
                part.Root.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                var drawn = part.Root.AddComponent<MeshRenderer>();
                drawn.sharedMaterials = part.Materials;
                drawn.shadowCastingMode = renderer.shadowCastingMode;
                drawn.receiveShadows = renderer.receiveShadows;
                var intact = new SurfaceCutter();
                intact.Run(surface, null, false); // No carving yet: native triangles, synchronous.
                intact.Apply(part.Mesh, Vector3.zero);
                owner.Parts.Add(part);
                // LOD siblings share the base name; hide them with LOD0 so no intact copy shows through.
                foreach (MeshFilter sibling in filters)
                    if (sibling.sharedMesh != null && sibling.sharedMesh.name.StartsWith(shared.name, StringComparison.Ordinal))
                        Hide(owner, sibling.GetComponent<Renderer>());
            }
            if (owner == null) return null;
            Bounds bounds = default; bool any = false;
            foreach (Part part in owner.Parts)
            {
                Bounds b = part.Root.GetComponent<Renderer>().bounds;
                b.center = Datum.origin.InverseTransformPoint(b.center);
                if (!any) { bounds = b; any = true; } else bounds.Encapsulate(b);
            }
            owner.Bounds = bounds;
            ProbeGround(owner, building);
            owners.Add(owner);
            return owner;
        }

        /// <summary>
        /// Street level from rays just outside the footprint: native meshes extend below their
        /// pivot and some sit on podiums, so neither the pivot nor bounds.min is the ground.
        /// </summary>
        private void ProbeGround(Owner owner, MapBuilding building)
        {
            owner.Building = building.transform;
            Bounds b = owner.Bounds;
            float spillX = b.extents.x * 0.3f + 6f, spillZ = b.extents.z * 0.3f + 6f;
            owner.GroundX0 = b.min.x - spillX; owner.GroundX1 = b.max.x + spillX;
            owner.GroundZ0 = b.min.z - spillZ; owner.GroundZ1 = b.max.z + spillZ;
            var under = new List<float>(GroundGrid * GroundGrid);
            var street = new List<float>(GroundGrid * GroundGrid);
            for (int j = 0; j < GroundGrid; j++)
                for (int i = 0; i < GroundGrid; i++)
                {
                    var local = new Vector3(Mathf.Lerp(owner.GroundX0, owner.GroundX1, i / (GroundGrid - 1f)), 0f,
                        Mathf.Lerp(owner.GroundZ0, owner.GroundZ1, j / (GroundGrid - 1f)));
                    float y = Surface(owner, local, b.max.y + 5f, b.size.y + 400f);
                    owner.Lattice[j * GroundGrid + i] = y;
                    if (float.IsNaN(y)) continue;
                    bool outside = local.x < b.min.x || local.x > b.max.x || local.z < b.min.z || local.z > b.max.z;
                    (outside ? street : under).Add(y);
                    MapBuilding below = outside || lastHit == null ? null : lastHit.GetComponentInParent<MapBuilding>();
                    if (below != null && below != building) { supports.TryGetValue(below, out int n); supports[below] = n + 1; }
                }
            // The base is what the building actually rests on (terrain, plaza or a podium roof);
            // the street beside it only stands in when nothing was found underneath.
            List<float> basis = under.Count > 0 ? under : street;
            basis.Sort();
            owner.Ground = basis.Count == 0 ? Datum.origin.InverseTransformPoint(building.transform.position).y : basis[basis.Count / 2];
            // A podium holds it up only if it carries most of the footprint and lifts it clear of the street.
            street.Sort();
            float streetLevel = street.Count > 0 ? street[street.Count / 2] : owner.Ground;
            owner.Support = null;
            foreach (var entry in supports)
                if (entry.Value * 2 >= under.Count && owner.Ground > streetLevel + 3f) owner.Support = entry.Key;
            supports.Clear();
            // Raw keeps real drops (podium edges) so the mound can stop at them; Lattice is the
            // smoothed resting surface that never climbs onto a neighbouring roof.
            for (int k = 0; k < owner.Lattice.Length; k++)
            {
                owner.Raw[k] = float.IsNaN(owner.Lattice[k]) ? owner.Ground : owner.Lattice[k];
                owner.Lattice[k] = Mathf.Clamp(owner.Raw[k], owner.Ground - 2f, owner.Ground + 1f);
            }
        }

        /// <summary>Highest static surface under a Datum-local point, ignoring the building and its wreck. NaN when nothing.</summary>
        private float Surface(Owner owner, Vector3 local, float fromY, float length)
        {
            local.y = fromY;
            int count = Physics.RaycastNonAlloc(Datum.origin.TransformPoint(local), Vector3.down, probe, length,
                PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            lastHit = null;
            for (int h = 0; h < count; h++)
            {
                Transform hit = probe[h].collider.transform;
                if ((owner.Building != null && hit.IsChildOf(owner.Building)) || (owner.Wreck != null && hit.IsChildOf(owner.Wreck))) continue;
                if (probe[h].point.y > best) { best = probe[h].point.y; lastHit = hit; }
            }
            return float.IsNegativeInfinity(best) ? float.NaN : Datum.origin.InverseTransformPoint(new Vector3(0f, best, 0f)).y;
        }

        /// <summary>First static surface on a Datum-local segment, ignoring the building and its wreck.</summary>
        private bool Sweep(Owner owner, Vector3 from, Vector3 to, out float hitY)
        {
            hitY = 0f;
            Vector3 a = Datum.origin.TransformPoint(from), delta = Datum.origin.TransformPoint(to) - a;
            float length = delta.magnitude;
            if (length < 1e-4f) return false;
            int count = Physics.RaycastNonAlloc(a, delta / length, probe, length, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            for (int h = 0; h < count; h++)
            {
                Transform hit = probe[h].collider.transform;
                if ((owner.Building != null && hit.IsChildOf(owner.Building)) || (owner.Wreck != null && hit.IsChildOf(owner.Wreck))) continue;
                if (probe[h].distance < nearest) { nearest = probe[h].distance; hitY = Datum.origin.InverseTransformPoint(probe[h].point).y; }
            }
            return nearest < float.MaxValue;
        }

        private static float GroundAt(Owner o, Vector3 local) => Sample(o, o.Lattice, local);

        /// <summary>True where the real surface lies level with the base, so rubble can rest there.</summary>
        private static bool Supported(Owner o, Vector3 local)
        {
            // Lowest raw corner of the lattice cell: a drop anywhere in the cell unsupports it.
            float u = Mathf.Clamp01(Mathf.InverseLerp(o.GroundX0, o.GroundX1, local.x)) * (GroundGrid - 1);
            float v = Mathf.Clamp01(Mathf.InverseLerp(o.GroundZ0, o.GroundZ1, local.z)) * (GroundGrid - 1);
            int i = Mathf.Min((int)u, GroundGrid - 2), j = Mathf.Min((int)v, GroundGrid - 2);
            float low = Mathf.Min(Mathf.Min(o.Raw[j * GroundGrid + i], o.Raw[j * GroundGrid + i + 1]),
                Mathf.Min(o.Raw[(j + 1) * GroundGrid + i], o.Raw[(j + 1) * GroundGrid + i + 1]));
            return low > o.Ground - 2f;
        }

        private static float Sample(Owner o, float[] grid, Vector3 local)
        {
            float u = Mathf.Clamp01(Mathf.InverseLerp(o.GroundX0, o.GroundX1, local.x)) * (GroundGrid - 1);
            float v = Mathf.Clamp01(Mathf.InverseLerp(o.GroundZ0, o.GroundZ1, local.z)) * (GroundGrid - 1);
            int i = Mathf.Min((int)u, GroundGrid - 2), j = Mathf.Min((int)v, GroundGrid - 2);
            float fu = u - i, fv = v - j;
            float a = Mathf.Lerp(grid[j * GroundGrid + i], grid[j * GroundGrid + i + 1], fu);
            float b = Mathf.Lerp(grid[(j + 1) * GroundGrid + i], grid[(j + 1) * GroundGrid + i + 1], fu);
            return Mathf.Lerp(a, b, fv);
        }

        private static void Hide(Owner owner, Renderer renderer)
        {
            if (renderer == null || owner.Hidden.Contains(renderer)) return;
            owner.Hidden.Add(renderer); owner.HiddenState.Add(renderer.enabled); renderer.enabled = false;
        }

        private static void RevealWreck(Owner owner)
        {
            for (int i = 0; i < owner.Hidden.Count; i++)
                if (owner.Hidden[i] != null && owner.WreckList.Contains(owner.Hidden[i])) owner.Hidden[i].enabled = owner.HiddenState[i];
        }

        private int CountRuins() { int n = 0; foreach (Owner o in owners) if (o.Dead) n++; return n; }

        private void Evict()
        {
            Owner oldest = null;
            foreach (Owner o in owners) if (!o.Dead && (oldest == null || o.At < oldest.At)) oldest = o;
            if (oldest == null) foreach (Owner o in owners) if (oldest == null || o.At < oldest.At) oldest = o;
            if (oldest != null) Release(oldest);
        }

        private void Release(Owner owner)
        {
            for (int i = 0; i < owner.Hidden.Count; i++) if (owner.Hidden[i] != null) owner.Hidden[i].enabled = owner.HiddenState[i];
            foreach (Part part in owner.Parts) { Destroy(part.Root); Destroy(part.Mesh); part.Applied = int.MaxValue; }
            if (owner.Mound != null) Destroy(owner.Mound);
            if (owner.MoundMesh != null) Destroy(owner.MoundMesh);
            owners.Remove(owner);
        }

        // ---------- moving pieces ----------

        private Moving Acquire()
        {
            if (moving.Count >= MaxMoving)
            {
                Moving oldest = null;
                foreach (Moving m in moving) if (oldest == null || m.Born < oldest.Born) oldest = m;
                Retire(oldest);
            }
            var piece = new Moving { Root = new GameObject("BoscaliSummer.StructuralFragment"), Born = Time.timeSinceLevelLoad };
            piece.Root.transform.SetParent(Datum.origin, false);
            piece.Root.AddComponent<MeshFilter>();
            piece.Root.AddComponent<MeshRenderer>();
            moving.Add(piece);
            return piece;
        }

        private void Retire(Moving piece)
        {
            moving.Remove(piece);
            if (piece.Root != null) Destroy(piece.Root);
            if (piece.Mesh != null && !piece.Shared) Destroy(piece.Mesh);
        }

        /// <summary>
        /// Ballistic concrete chunks on a shared mesh: no carving, no allocation per chunk mesh. They
        /// sweep-land on real surfaces, so heavy hits and collapses scatter debris across the street.
        /// </summary>
        private void Spray(Owner owner, Vector3 local, Vector3 direction, int count, float speed, float size)
        {
            if (chunkMeshes == null) return;
            var random = new System.Random(owner.Seed ^ Mathf.RoundToInt(local.x * 31f + local.y * 17f + local.z));
            float R(float lo, float hi) => lo + (hi - lo) * (float)random.NextDouble();
            for (int i = 0; i < count; i++)
            {
                Moving piece = Acquire();
                piece.Owner = owner; piece.Shared = true;
                piece.Mesh = chunkMeshes[i % chunkMeshes.Length];
                piece.Root.GetComponent<MeshFilter>().sharedMesh = piece.Mesh;
                var renderer = piece.Root.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = i % 3 == 0 && owner.Parts.Count > 0 ? owner.Parts[0].Materials[0] : edge;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                float scale = size * R(0.5f, 1.6f);
                piece.Root.transform.localScale = Vector3.one * scale;
                piece.Start = local + new Vector3(R(-1f, 1f), R(-0.5f, 1f), R(-1f, 1f));
                piece.Root.transform.localPosition = piece.Start;
                piece.Rotation = Quaternion.Euler(R(0f, 360f), R(0f, 360f), R(0f, 360f));
                Vector3 spread = Quaternion.Euler(R(-35f, 35f), R(-50f, 50f), 0f) * direction;
                piece.Velocity = spread * speed * R(0.5f, 1.2f) + Vector3.up * speed * R(0.15f, 0.5f);
                piece.Axis = new Vector3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f)).normalized;
                piece.Spin = R(200f, 700f);
                piece.Radius = scale * 0.5f;
                piece.Hull = new Vector3[chunkHull.Length];
                for (int h = 0; h < chunkHull.Length; h++) piece.Hull[h] = chunkHull[h] * scale;
            }
        }

        /// <summary>Three pooled point lights: a 0.3 s orange blast flash, brighter for heavier warheads.</summary>
        private void Flash(Vector3 world, float power)
        {
            if (flashes == null) return;
            int i = flashHead; flashHead = (flashHead + 1) % flashes.Length;
            flashes[i].transform.position = world;
            flashes[i].range = 18f + power * 40f;
            flashPeak[i] = 3f + power * 9f;
            flashAt[i] = Time.timeSinceLevelLoad;
            flashes[i].enabled = true;
        }

        private Moving Place(Owner owner, Part part, SurfaceCutter cut)
        {
            if (cut.TriangleCount == 0 || part.Root == null || !owners.Contains(owner)) return null;
            Moving piece = Acquire();
            piece.Owner = owner;
            Vector3 centre = cut.Bounds.center;
            piece.Hull = new Vector3[Mathf.Min(48, cut.Positions.Count)];
            int stride = Mathf.Max(1, cut.Positions.Count / piece.Hull.Length);
            Transform t = piece.Root.transform, source = part.Root.transform;
            for (int i = 0; i < piece.Hull.Length; i++) piece.Hull[i] = Vector3.Scale(cut.Positions[i * stride] - centre, source.localScale);
            piece.Mesh = new Mesh { name = "Structural fragment" };
            cut.Apply(piece.Mesh, centre);
            piece.Root.GetComponent<MeshFilter>().sharedMesh = piece.Mesh;
            var renderer = piece.Root.GetComponent<MeshRenderer>();
            // A broken-off piece shows raw concrete behind its facade, not the dark gutted interior.
            var materials = (Material[])part.Materials.Clone();
            materials[part.Surface.Sub.Length] = edge;
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            piece.Root.layer = part.Root.layer;
            t.localRotation = source.localRotation;
            t.localScale = source.localScale;
            piece.Start = source.localPosition + source.localRotation * Vector3.Scale(centre, source.localScale);
            t.localPosition = piece.Start;
            piece.Rotation = t.localRotation;
            Vector3 extents = Vector3.Scale(cut.Bounds.extents, source.localScale);
            piece.Radius = extents.magnitude;
            piece.ExtentY = Mathf.Abs((source.localRotation * extents).y);
            return piece;
        }

        private void SpawnFragment(Owner owner, Part part, SurfaceCutter cut, Vector3 normal, int seed, float power)
        {
            Moving piece = Place(owner, part, cut);
            if (piece == null) return;
            var random = new System.Random(seed);
            float R(float lo, float hi) => lo + (hi - lo) * (float)random.NextDouble();
            Vector3 side = Vector3.Cross(normal, Vector3.up);
            float throw_ = 1f + power * 1.6f;
            piece.Velocity = normal * R(2.5f, 6f) * throw_ + Vector3.up * R(0.5f, 2.5f) * throw_ + side * R(-2.5f, 2.5f) * throw_;
            piece.Axis = new Vector3(R(-1f, 1f), R(-1f, 1f), R(-1f, 1f)).normalized;
            piece.Spin = R(90f, 300f);
        }

        private void SpawnSection(Owner owner, Part part, SurfaceCutter cut, int cell, int seed)
        {
            Moving piece = Place(owner, part, cut);
            if (piece == null) return;
            var random = new System.Random(seed);
            piece.Section = true;
            Vector3 outward = piece.Start - owner.Bounds.center; outward.y = 0f;
            piece.Outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.right;
            piece.Axis = Vector3.Cross(Vector3.up, piece.Outward);
            piece.Delay = (float)random.NextDouble() * 0.45f + cell * 0.05f;
            piece.MaxTilt = 6f + (float)random.NextDouble() * 26f;
        }

        private void Update()
        {
            float now = Time.timeSinceLevelLoad;
            for (int i = jobs.Count - 1; i >= 0; i--)
            {
                Job job = jobs[i];
                if (!job.Task.IsCompleted) continue;
                jobs.RemoveAt(i);
                if (job.Task.IsFaulted) { Plugin.Logger.LogError("Building carve failed: " + job.Task.Exception?.GetBaseException()); continue; }
                var clock = System.Diagnostics.Stopwatch.StartNew();
                job.Apply(job.Task.Result);
                MaxApplyMs = Math.Max(MaxApplyMs, clock.Elapsed.TotalMilliseconds);
                LastCarveMs = job.Ms; MaxCarveMs = Math.Max(MaxCarveMs, job.Ms); CarveCount++;
            }
            for (int i = moving.Count - 1; i >= 0; i--) if (i < moving.Count) Animate(moving[i], now);
            fires.Update(now);
            foreach (var spray in sprays)
                if (owners.Contains(spray.Owner)) Spray(spray.Owner, spray.At, spray.Direction, 4, 9f, spray.Size);
            sprays.Clear();
            foreach (Owner owner in owners)
                if (owner.Mound != null && owner.MoundBorn > -50f)
                {
                    float k = owner.MoundBorn < 0f ? 0.04f : 1f - Mathf.Pow(1f - Mathf.Clamp01((now - owner.MoundBorn) / 3.4f), 2f);
                    owner.Mound.transform.localScale = new Vector3(1f, Mathf.Max(0.04f, k), 1f);
                    if (k >= 1f) { owner.MoundBorn = -100f; RevealWreck(owner); }
                }
            if (flashes != null)
                for (int i = 0; i < flashes.Length; i++)
                {
                    if (!flashes[i].enabled) continue;
                    float age = (now - flashAt[i]) / 0.3f;
                    flashes[i].intensity = flashPeak[i] * (1f - age) * (1f - age);
                    if (age >= 1f) flashes[i].enabled = false;
                }
        }

        private void Animate(Moving piece, float now)
        {
            if (piece.Root == null) { moving.Remove(piece); return; }
            if (piece.Landed) return;
            Transform t = piece.Root.transform;
            float time = now - piece.Born;
            if (piece.Section)
            {
                time -= piece.Delay;
                if (time < 0f) return;
                if (!piece.Released)
                {
                    // Upper floors let go in a burst of pulverised concrete at their own height.
                    piece.Released = true;
                    RuinAftermathManager.Instance?.SectionDust(t.position.ToGlobalPosition(), Mathf.Clamp(piece.Radius * 0.35f, 3f, 9f));
                }
                float drop = 3.4f * time * time;
                float ground = GroundAt(piece.Owner, piece.Start);
                t.localPosition = piece.Start + piece.Outward * (0.9f * time * time) - Vector3.up * drop;
                t.localRotation = Quaternion.AngleAxis(Mathf.Min(piece.MaxTilt, 34f * time * time), piece.Axis) * piece.Rotation;
                if (!piece.Dusted && piece.Start.y - piece.ExtentY - drop <= ground + 0.5f)
                {
                    piece.Dusted = true;
                    Vector3 at = t.localPosition; at.y = ground;
                    RuinAftermathManager.Instance?.LandingDust(Datum.origin.TransformPoint(at).ToGlobalPosition());
                    // The section shatters on impact and throws rubble across the street (after this loop).
                    sprays.Add((piece.Owner, at + Vector3.up * 1.5f, (piece.Outward + Vector3.up * 0.2f).normalized, Mathf.Clamp(piece.Radius * 0.6f, 0.5f, 1.4f)));
                }
                if (piece.Start.y + piece.ExtentY - drop < ground - 2f || time > 9f) Retire(piece);
                return;
            }
            Vector3 position = piece.Start + piece.Velocity * time + 0.5f * Physics.gravity * time * time;
            Quaternion rotation = Quaternion.AngleAxis(piece.Spin * time, piece.Axis) * piece.Rotation;
            // Sweep the fall since last frame: a chunk lands on the first roof, kerb or street it meets.
            // The fallback floor follows real drops (off a podium edge), never the smoothed rubble base.
            float floor = Mathf.Min(GroundAt(piece.Owner, position), Sample(piece.Owner, piece.Owner.Raw, position));
            Vector3 belly = Vector3.up * (piece.Radius * 0.3f);
            if (piece.Last == Vector3.zero) piece.Last = piece.Start;
            if (Sweep(piece.Owner, piece.Last - belly, position - belly, out float hitY)) { floor = hitY; position.y = Mathf.Min(position.y, hitY + piece.Radius * 0.3f); }
            piece.Last = position;
            if (position.y - piece.Radius * 0.3f <= floor || time > 8f)
            {
                // Heavy throws can leave the sampled ground grid: one exact ray settles where it really is.
                float exact = Surface(piece.Owner, position, position.y + 2f, 400f);
                if (!float.IsNaN(exact) && exact < position.y + 1f) floor = exact;
                else if (!float.IsNaN(exact)) floor = Mathf.Max(floor, exact);
                if (position.y - piece.Radius * 0.3f > floor && time <= 8f) { t.localPosition = position; t.localRotation = rotation; return; }
                float lowest = 0f;
                foreach (Vector3 v in piece.Hull) lowest = Mathf.Min(lowest, (rotation * v).y);
                position.y = floor - lowest - 0.05f;
                piece.Landed = true;
                BuildingHitLedger.Instance?.EmitDebrisDust(Datum.origin.TransformPoint(position).ToGlobalPosition());
            }
            t.localPosition = position; t.localRotation = rotation;
        }

        // ---------- rubble mound ----------

        private void BuildMound(Owner owner)
        {
            Part part = owner.Parts[0];
            Transform source = part.Root.transform;
            Bounds footprint = part.Surface.Bounds;
            Vector3 scale = source.localScale;
            float hx = Mathf.Max(3f, footprint.extents.x * scale.x), hz = Mathf.Max(3f, footprint.extents.z * scale.z);
            float height = Mathf.Clamp((owner.Bounds.max.y - owner.Ground) * 0.28f, 2.5f, 10f);
            float bulk = Mathf.Clamp(Mathf.Sqrt(hx * hz) / 12f, 0.7f, 2.2f); // Debris scales with the building.
            Vector3 centre = source.localPosition + source.localRotation * Vector3.Scale(footprint.center, scale);
            centre.y = owner.Ground;
            var root = new GameObject("BoscaliSummer.RubbleMound") { layer = part.Root.layer };
            root.transform.SetParent(Datum.origin, false);
            root.transform.localPosition = centre;
            root.transform.localRotation = Quaternion.Euler(0f, source.localRotation.eulerAngles.y, 0f);
            Quaternion yaw = root.transform.localRotation;
            float seed = (owner.Seed & 255) * 0.73f;
            float Surface(float x, float z)
            {
                float s = Mathf.Pow(Mathf.Pow(Mathf.Abs(x) / hx, 4f) + Mathf.Pow(Mathf.Abs(z) / hz, 4f), 0.25f);
                float n = CarveField.Noise(new Vector3(x * 0.12f, seed, z * 0.12f)), m = CarveField.Noise(new Vector3(x * 0.35f, z * 0.35f, seed));
                float fine = CarveField.Noise(new Vector3(x * 1.1f, seed * 1.7f, z * 1.1f));
                // Several heaps, not a plateau: low-frequency peaks, broken surface, ragged spill edge.
                float h = height * Smooth(1.35f, 0.45f, s + 0.22f * n + 0.08f * m) * Mathf.Max(0.15f, 0.6f + 0.45f * n + 0.25f * m) + 0.18f * fine;
                float ground = GroundAt(owner, centre + yaw * new Vector3(x, 0f, z)) - centre.y;
                return ground + h - 0.35f;
            }
            // Rubble rests inside the walls and on level ground around them; it stops at a drop
            // (a podium edge) instead of hanging over it as a skirt.
            bool Rests(float x, float z) =>
                (Mathf.Abs(x) <= hx && Mathf.Abs(z) <= hz) || Supported(owner, centre + yaw * new Vector3(x, 0f, z));
            const int grid = 20;
            var vertices = new List<Vector3>(grid * grid + 700);
            var uv = new List<Vector2>(vertices.Capacity);
            var heap = new List<int>(grid * grid * 6);
            var chunks = new List<int>(2600);
            var slabs = new List<int>(2600);
            var rests = new bool[grid * grid];
            owner.Lip.Clear();
            for (int j = 0; j < grid; j++)
                for (int i = 0; i < grid; i++)
                {
                    float x = (i / (grid - 1f) * 2f - 1f) * hx * 1.5f, z = (j / (grid - 1f) * 2f - 1f) * hz * 1.5f;
                    var vertex = new Vector3(x, Surface(x, z), z);
                    vertices.Add(vertex); uv.Add(new Vector2(x, z) * 0.12f);
                    rests[j * grid + i] = Rests(x, z);
                    bool rim = i == 0 || j == 0 || i == grid - 1 || j == grid - 1;
                    if (rim && rests[j * grid + i] && (i + j) % 3 == 0) owner.Lip.Add(vertex);
                    if (i > 0 && j > 0)
                    {
                        int a = j * grid + i, b = a - 1, c = a - grid, d = c - 1;
                        if (!rests[a] || !rests[b] || !rests[c] || !rests[d]) continue;
                        heap.Add(d); heap.Add(b); heap.Add(a); heap.Add(d); heap.Add(a); heap.Add(c);
                    }
                }
            var random = new System.Random(owner.Seed);
            float R(float lo, float hi) => lo + (hi - lo) * (float)random.NextDouble();
            // Broken facade blocks wear the building's own trim texture; pancaked floor slabs lie
            // tilted and half-buried in bare concrete. Denser and bigger toward the heap core.
            int count = Mathf.Clamp(Mathf.RoundToInt(hx * hz * 0.07f), 70, 180);
            for (int k = 0; k < count; k++)
            {
                // Most debris piles inside the walls; a third is flung into a ragged outer field.
                bool far = k % 10 < 3;
                float angle = R(0f, Mathf.PI * 2f), reach = far ? R(1.15f, 1.7f) : R(0f, 1.1f);
                float x = Mathf.Cos(angle) * reach * hx, z = Mathf.Sin(angle) * reach * hz;
                if (!far) { x = R(-1.1f, 1.1f) * hx; z = R(-1.1f, 1.1f) * hz; }
                if (!Rests(x, z)) continue;
                bool slab = k % 5 < 2;
                float size = (slab ? R(2.5f, 6f) : R(0.8f, 3.2f)) * bulk * (far ? 0.55f : 1f);
                var extents = slab ? new Vector3(size, R(0.3f, 0.45f), size * R(0.5f, 0.9f))
                    : new Vector3(size, size * R(0.35f, 0.9f), size * R(0.5f, 1.1f));
                float tilt = slab ? 50f : 35f;
                Quaternion spin = Quaternion.Euler(R(-tilt, tilt), R(0f, 360f), R(-tilt, tilt));
                var at = new Vector3(x, Surface(x, z) + extents.y * R(-0.1f, 0.3f), z);
                Block(vertices, uv, slab ? slabs : chunks, at, spin, extents, new Vector2(R(0f, 1f), R(0f, 1f)), random);
            }
            var mesh = new Mesh { name = "Rubble mound", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv);
            mesh.subMeshCount = 3; mesh.SetTriangles(heap, 0); mesh.SetTriangles(chunks, 1); mesh.SetTriangles(slabs, 2);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { rubble, part.Materials[0], edge };
            owner.Mound = root; owner.MoundMesh = mesh;
            root.transform.localScale = new Vector3(1f, 0.04f, 1f);
        }

        private static float Smooth(float edge0, float edge1, float x)
        { float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0)); return t * t * (3f - 2f * t); }

        private static void Block(List<Vector3> vertices, List<Vector2> uv, List<int> triangles, Vector3 at, Quaternion spin, Vector3 extents, Vector2 offset, System.Random random)
        {
            var corners = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                c = Vector3.Scale(c * (0.7f + 0.3f * (float)random.NextDouble()), extents) * 0.5f;
                corners[i] = at + spin * c;
            }
            int[] faces = { 0, 2, 3, 1, 4, 5, 7, 6, 0, 1, 5, 4, 2, 6, 7, 3, 0, 4, 6, 2, 1, 3, 7, 5 };
            for (int f = 0; f < 24; f += 4)
            {
                int start = vertices.Count;
                for (int k = 0; k < 4; k++)
                {
                    vertices.Add(corners[faces[f + k]]);
                    uv.Add(offset + new Vector2(k == 1 || k == 2 ? 0.08f : 0f, k >= 2 ? 0.08f : 0f));
                }
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                triangles.Add(start); triangles.Add(start + 3); triangles.Add(start + 2);
            }
        }

        // ---------- materials ----------

        private bool EnsureMaterials()
        {
            if (interior != null) return true;
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return false;
            const int w = 32, h = 128;
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = y / (float)h, grain = CarveField.Noise(new Vector3(x * 0.4f, y * 0.4f, 0f)) * 0.03f;
                    // One storey: slab edge at the bottom, a soot shadow under the next slab.
                    float shade = v < 0.09f ? 0.36f : v > 0.9f ? 0.05f : 0.1f + 0.05f * Mathf.Sin(v * 9f);
                    shade = Mathf.Clamp01(shade + grain);
                    pixels[y * w + x] = (Color32)new Color(shade, shade * 0.97f, shade * 0.93f, 1f);
                }
            interiorTexture = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Gutted interior", wrapMode = TextureWrapMode.Repeat };
            interiorTexture.SetPixels32(pixels); interiorTexture.Apply(true, true);
            interior = new Material(lit) { name = "Gutted interior" };
            interior.SetTexture("_BaseMap", interiorTexture); interior.SetFloat("_Smoothness", 0f);
            concreteTexture = DestructionAssets.ConcreteTexture();
            edge = new Material(lit) { name = "Broken concrete edge" };
            edge.SetTexture("_BaseMap", concreteTexture); edge.SetColor("_BaseColor", new Color(1.55f, 1.52f, 1.45f)); edge.SetFloat("_Smoothness", 0f);
            rubble = new Material(lit) { name = "Concrete rubble" };
            rubble.SetTexture("_BaseMap", concreteTexture); rubble.SetColor("_BaseColor", new Color(0.95f, 0.9f, 0.84f)); rubble.SetFloat("_Smoothness", 0f);
            chunkMeshes = new Mesh[4];
            for (int i = 0; i < chunkMeshes.Length; i++) chunkMeshes[i] = FractureGeometry.Section(911u + (uint)i * 7919u);
            chunkHull = chunkMeshes[0].vertices;
            flashes = new Light[3];
            for (int i = 0; i < flashes.Length; i++)
            {
                var go = new GameObject("BoscaliSummer.BlastFlash");
                go.transform.SetParent(Datum.origin, false);
                flashes[i] = go.AddComponent<Light>();
                flashes[i].type = LightType.Point;
                flashes[i].color = new Color(1f, 0.62f, 0.3f);
                flashes[i].shadows = LightShadows.None;
                flashes[i].enabled = false;
            }
            return true;
        }

        private void Clear()
        {
            StopAllCoroutines();
            fires.Clear();
            jobs.Clear(); // Running tasks finish harmlessly; their results are dropped.
            while (owners.Count > 0) Release(owners[owners.Count - 1]);
            while (moving.Count > 0) Retire(moving[moving.Count - 1]);
            if (interior != null) Destroy(interior);
            if (edge != null) Destroy(edge);
            if (rubble != null) Destroy(rubble);
            if (interiorTexture != null) Destroy(interiorTexture);
            if (concreteTexture != null) Destroy(concreteTexture);
            interior = edge = rubble = null; interiorTexture = concreteTexture = null;
            if (chunkMeshes != null) foreach (Mesh mesh in chunkMeshes) Destroy(mesh);
            if (flashes != null) foreach (Light light in flashes) if (light != null) Destroy(light.gameObject);
            chunkMeshes = null; flashes = null;
            MaxCarveMs = MaxApplyMs = LastCarveMs = 0; CarveCount = 0;
        }

        // ---------- evidence ----------

        internal void ResetMetrics() { MaxCarveMs = MaxApplyMs = 0; }

        private int StubTriangles()
        {
            int n = 0;
            foreach (Owner o in owners) if (o.Dead) foreach (Part p in o.Parts) n += p.Mesh.triangles.Length / 3;
            return n;
        }

        /// <summary>Largest gap between the mound's outer lip and the physical ground under it (floating rubble).</summary>
        private float MoundBaseError()
        {
            float worst = 0f;
            foreach (Owner o in owners)
            {
                if (o.Mound == null || o.MoundBorn > -50f) continue; // Only judge a fully risen mound.
                foreach (Vector3 lip in o.Lip)
                {
                    Vector3 world = o.Mound.transform.TransformPoint(lip);
                    if (Physics.Raycast(world + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 60f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                        worst = Mathf.Max(worst, world.y - hit.point.y);
                }
            }
            return worst;
        }

        internal Dictionary<string, object> Audit()
        {
            int ruins = 0, lobes = 0, triangles = 0, hidden = 0, sections = 0, fragments = 0, landed = 0, checkedGround = 0, wreckRenderers = 0, wreckColliders = 0;
            bool collisionFree = true;
            float groundError = 0f;
            var roots = new List<Transform>();
            foreach (Owner o in owners)
            {
                if (o.Dead) ruins++;
                lobes += o.Lobes.Count;
                foreach (Renderer r in o.WreckList) if (r != null && r.enabled) wreckRenderers++;
                // Colliders counted at collapse versus now: the visual swap must never touch them.
                if (o.Wreck != null && o.Wreck.GetComponentsInChildren<Collider>(true).Length != o.WreckColliders) wreckColliders = -1;
                foreach (Renderer r in o.Hidden) if (r != null && !r.enabled) hidden++;
                foreach (Part p in o.Parts) { roots.Add(p.Root.transform); triangles += p.Mesh.triangles.Length / 3; }
                if (o.Mound != null) roots.Add(o.Mound.transform);
            }
            foreach (Moving m in moving)
            {
                if (m.Root == null) continue;
                roots.Add(m.Root.transform);
                if (m.Section) { sections++; continue; }
                fragments++;
                if (!m.Landed) continue;
                landed++;
                // Real contact check: lowest hull point against a physics ray at the same spot.
                Transform t = m.Root.transform;
                float lowest = float.MaxValue;
                foreach (Vector3 v in m.Hull) lowest = Mathf.Min(lowest, (t.rotation * v + t.position).y);
                if (Physics.Raycast(new Vector3(t.position.x, lowest + 3f, t.position.z), Vector3.down, out RaycastHit hit, 40f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                { groundError = Mathf.Max(groundError, Mathf.Abs(lowest - hit.point.y)); checkedGround++; }
                else groundError = 99f; // No surface under a landed fragment is itself a failure.
            }
            foreach (Transform root in roots)
                collisionFree &= root.GetComponentsInChildren<Collider>(true).Length == 0 && root.GetComponentsInChildren<Rigidbody>(true).Length == 0;
            Vector3 before = Datum.origin.position, shift = new Vector3(125f, 0f, -250f);
            var positions = new List<Vector3>();
            foreach (Transform root in roots) positions.Add(root.position);
            bool follows = true;
            try
            {
                Datum.origin.position = before + shift;
                for (int i = 0; i < roots.Count; i++) follows &= (roots[i].position - positions[i] - shift).sqrMagnitude < 0.001f;
            }
            finally { Datum.origin.position = before; }
            bool budgets = owners.Count <= MaxOwners && ruins <= MaxRuins && moving.Count <= MaxMoving;
            return new Dictionary<string, object>
            {
                { "ok", budgets && collisionFree && follows }, { "owners", owners.Count }, { "ruins", ruins },
                { "lobes", lobes }, { "triangles", triangles }, { "hiddenNative", hidden },
                { "fragments", fragments }, { "landed", landed }, { "sections", sections },
                { "groundError", groundError }, { "collisionFree", collisionFree }, { "parentShift", follows },
                { "pendingJobs", jobs.Count }, { "carves", CarveCount }, { "maxCarveMs", MaxCarveMs },
                { "lastCarveMs", LastCarveMs }, { "maxApplyMs", MaxApplyMs },
                { "libraryMs", NativeSurfaceLibrary.LoadMs }, { "libraryMeshes", NativeSurfaceLibrary.Count },
                { "mound", owners.Exists(o => o.Mound != null) ? 1 : 0 }, { "groundChecked", checkedGround },
                { "wreckShown", wreckRenderers }, { "moving", moving.Count },
                { "fires", fires.Count }, { "firesDrawn", fires.Drawn }, { "wreckCollidersIntact", wreckColliders == 0 ? 1 : 0 },
                { "warmUpMs", WarmUpMs }, { "lastHitMs", LastHitMs },
                { "stubTriangles", StubTriangles() }, { "moundBaseError", MoundBaseError() }
            };
        }
    }
}
