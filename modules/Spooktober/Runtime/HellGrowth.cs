using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using RoadPathfinding;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>Reversible basalt growth, built once and raised in groups. Native terrain edits share cell ownership.</summary>
    internal static class HellGrowth
    {
        private const int GroupCount = 6, FormsPerZone = 70, MaxForms = 1200, MaxHeightCells = 65536;
        private const float GrowSeconds = 30f;
        private static readonly Dictionary<TerrainData, Dictionary<int, HeightCell>> TerrainCells = new Dictionary<TerrainData, Dictionary<int, HeightCell>>();
        private static int heightCells;
        private static Gradient ash;
        internal static int Count { get; private set; }
        internal static int Forms { get; private set; }

        internal static Handle Create(Transform parent, int seed, Vector3 centerLocal, float radius, float strength, float sizeScale = 1f)
        {
            if (parent == null || !float.IsFinite(radius) || radius < 40f) return null;
            var handle = new Handle();
            try { handle.Build(parent, seed, centerLocal, radius, strength, sizeScale); return handle; }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning("[Spooktober] basalt growth: " + e.Message);
                handle.Dispose();
                return null;
            }
        }

        internal sealed class Handle : IDisposable
        {
            private GameObject root;
            private readonly List<Transform> groups = new List<Transform>();
            private readonly List<Mesh> meshes = new List<Mesh>();
            private readonly List<ProtectedStrip> clear = new List<ProtectedStrip>();
            private readonly List<TerrainPatch> patches = new List<TerrainPatch>();
            private readonly List<ParticleSystem> vents = new List<ParticleSystem>();
            private readonly float[] baseHeights = new float[GroupCount + 1];
            private float born, strength, radius, sizeScale, nextHaze, lastAge = -1f, lastStrength = -1f;
            private int seed, forms, terrainStage = -1;
            private bool counted, disposed;
            private Vector3 firstForm;
            private bool haveForm;
            internal int TerrainRevision { get; private set; }

            internal void Build(Transform parent, int randomSeed, Vector3 centerLocal, float zoneRadius, float targetStrength, float scale)
            {
                root = new GameObject("Hell grown basalt");
                root.transform.SetParent(parent, false);
                root.transform.localPosition = centerLocal;
                born = Time.time;
                seed = randomSeed;
                radius = zoneRadius;
                sizeScale = Mathf.Clamp(scale, .25f, 6f);
                strength = Mathf.Clamp01(targetStrength);
                counted = true;
                Count++;
                ProtectAccess();
                BuildForms();
                BuildStratum();
                PrepareTerrain();
                var runner = root.AddComponent<GrowthRunner>();
                runner.Owner = this;
                Advance(born);
            }

            internal void SetStrength(float value)
            {
                if (disposed || !float.IsFinite(value)) return;
                value = Mathf.Clamp01(value);
                if (Mathf.Abs(value - strength) < .0001f) return;
                strength = value;
                terrainStage = -1;
                Advance(Time.time);
            }

            internal void Advance(float now)
            {
                if (disposed || root == null) return;
                float age = Mathf.Clamp01((now - born) / GrowSeconds);
                RefreshHaze(now, age);
                if (age == lastAge && strength == lastStrength && terrainStage >= 0) return;
                lastAge = age;
                lastStrength = strength;
                for (int i = 0; i < groups.Count; i++)
                {
                    float delay = i == GroupCount ? 0f : i * .1f;
                    float grown = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((age - delay) / (1f - delay))) * strength;
                    Transform group = groups[i];
                    if (group.gameObject.activeSelf != grown > .001f) group.gameObject.SetActive(grown > .001f);
                    group.localScale = new Vector3(1f, Mathf.Max(.001f, grown), 1f);
                    // Scale about the lowest planted point, so growth on a slope emerges from below instead of floating.
                    group.localPosition = Vector3.up * baseHeights[i] * (1f - grown);
                }
                int stage = Mathf.FloorToInt(age * GroupCount);
                if (stage == terrainStage) return;
                terrainStage = stage;
                float amount = Mathf.SmoothStep(0f, 1f, stage / (float)GroupCount) * strength;
                foreach (TerrainPatch patch in patches) patch.Apply(this, amount, false);
                if (patches.Count > 0) TerrainRevision++;
            }

            private void BuildForms()
            {
                var random = new System.Random(seed);
                var models = new[] { Model.Read("hell_spire_a"), Model.Read("hell_spire_b"), Model.Read("hell_arch"), Model.Read("hell_vent") };
                var builders = new Builder[GroupCount];
                for (int i = 0; i < builders.Length; i++) builders[i] = new Builder();
                int wanted = Math.Min(FormsPerZone, MaxForms - Forms);
                for (int attempt = 0; forms < wanted && attempt < wanted * 20; attempt++)
                {
                    int kind = forms % 13 == 0 ? 2 : forms % 7 == 0 ? 3 : forms % 2;
                    Model model = models[kind];
                    if (model == null) continue;
                    float angle = Next(random) * Mathf.PI * 2f;
                    float distance = Mathf.Lerp(.34f, .9f, Mathf.Sqrt(Next(random))) * radius;
                    Vector3 probe = new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                    if (!Sample(probe, out Vector3 local)) continue;
                    float targetHeight = (kind == 2 ? Mathf.Lerp(28f, 65f, Next(random)) : kind == 3 ? Mathf.Lerp(8f, 18f, Next(random)) :
                        Mathf.Lerp(sizeScale > 1.1f ? 22f : 10f, 100f, Mathf.Pow(Next(random), .85f))) * sizeScale;
                    float unit = targetHeight / Mathf.Max(.1f, model.Bounds.size.y);
                    Vector3 scale = new Vector3(unit * Mathf.Lerp(.7f, 1.15f, Next(random)), unit, unit * Mathf.Lerp(.7f, 1.15f, Next(random)));
                    float footprint = Mathf.Max(model.Bounds.extents.x * scale.x, model.Bounds.extents.z * scale.z);
                    if (Protected(local, footprint + 10f)) continue;
                    Quaternion turn = Quaternion.Euler(0f, Next(random) * 360f, 0f);
                    Matrix4x4 matrix = Matrix4x4.TRS(local, turn, scale) * Matrix4x4.Translate(new Vector3(-model.Bounds.center.x, -model.Bounds.min.y, -model.Bounds.center.z));
                    int group = forms % GroupCount;
                    builders[group].Add(model, matrix);
                    baseHeights[group] = Mathf.Min(baseHeights[group], local.y);
                    if (!haveForm) { haveForm = true; firstForm = local; }
                    if (kind == 3 && vents.Count < 2) Vent(local);
                    forms++;
                    Forms++;
                }
                for (int i = 0; i < builders.Length; i++) AddGroup(builders[i].Finish("Basalt needles " + i), i);
            }

            private void Vent(Vector3 local)
            {
                ParticleSystem ps = RadGhostLook.NewEmbers("Hell vent ash", root.transform, 3f * sizeScale, 120, Ash);
                ps.gameObject.SetActive(false);
                ps.transform.localPosition = local + Vector3.up * (2f * sizeScale);
                var main = ps.main;
                main.startLifetime = new ParticleSystem.MinMaxCurve(9f, 15f);
                main.startSize = new ParticleSystem.MinMaxCurve(.4f * sizeScale, 1.6f * sizeScale);
                main.maxParticles = 120;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 4f * sizeScale;
                shape.rotation = new Vector3(90f, 0f, 0f);
                var rise = ps.velocityOverLifetime;
                rise.x = new ParticleSystem.MinMaxCurve(-.5f * sizeScale, .8f * sizeScale);
                rise.y = new ParticleSystem.MinMaxCurve(1.2f * sizeScale, 2.8f * sizeScale);
                rise.z = new ParticleSystem.MinMaxCurve(-.3f * sizeScale, .6f * sizeScale);
                var emission = ps.emission;
                emission.rateOverTime = 0f;
                ps.GetComponent<ParticleSystemRenderer>().sharedMaterial = SmokeFx.Puff;
                ps.gameObject.SetActive(true);
                ps.Play();
                vents.Add(ps);
            }

            private void RefreshHaze(float now, float age)
            {
                if (now < nextHaze) return;
                nextHaze = now + .5f;
                Camera camera = SceneSingleton<CameraStateManager>.i?.mainCamera;
                if (camera == null) camera = Camera.main;
                float range = sizeScale > 1.1f ? 6000f : 3000f;
                float rate = 10f * FxBus.Scales.Particles * strength * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.2f, .9f, age));
                foreach (ParticleSystem ps in vents)
                {
                    if (ps == null) continue;
                    bool near = camera != null && (camera.transform.position - ps.transform.position).sqrMagnitude < range * range;
                    var emission = ps.emission;
                    emission.enabled = near && rate > .01f;
                    emission.rateOverTime = near ? rate : 0f;
                }
            }

            private void BuildStratum()
            {
                const int side = 49;
                var vertices = new Vector3[side * side];
                var uv = new Vector2[vertices.Length];
                var valid = new bool[vertices.Length];
                var body = new List<int>();
                var ribs = new List<int>();
                for (int z = 0; z < side; z++)
                    for (int x = 0; x < side; x++)
                    {
                        int index = z * side + x;
                        Vector3 probe = new Vector3((x / (float)(side - 1) * 2f - 1f) * radius, 0f,
                            (z / (float)(side - 1) * 2f - 1f) * radius);
                        if (new Vector2(probe.x, probe.z).sqrMagnitude > radius * radius || !Sample(probe, out Vector3 at) || Protected(at, 22f)) continue;
                        float rise = Ridge(at) * 22f * sizeScale;
                        vertices[index] = at + Vector3.up * (1.6f + rise);
                        uv[index] = HellGroundSurface.Uv(root.transform.TransformPoint(at).ToGlobalPosition());
                        baseHeights[GroupCount] = Mathf.Min(baseHeights[GroupCount], at.y);
                        valid[index] = true;
                    }
                for (int z = 0; z < side - 1; z++)
                    for (int x = 0; x < side - 1; x++)
                    {
                        int a = z * side + x, b = a + side;
                        Add(a, b, a + 1); Add(a + 1, b, b + 1);
                    }
                if (body.Count + ribs.Count == 0) { AddGroup(null, GroupCount); return; }
                var mesh = new Mesh { name = "Grown basalt ridges", subMeshCount = 3 };
                mesh.vertices = vertices;
                mesh.uv = uv;
                mesh.SetTriangles(body, 0, false);
                mesh.SetTriangles(ribs, 1, false);
                mesh.SetTriangles(Array.Empty<int>(), 2, false);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                AddGroup(mesh, GroupCount);

                void Add(int a, int b, int c)
                {
                    if (!valid[a] || !valid[b] || !valid[c]) return;
                    float low = Mathf.Min(vertices[a].y, Mathf.Min(vertices[b].y, vertices[c].y));
                    float high = Mathf.Max(vertices[a].y, Mathf.Max(vertices[b].y, vertices[c].y));
                    List<int> target = high - low > 10f * sizeScale ? ribs : body;
                    target.Add(a); target.Add(b); target.Add(c);
                }
            }

            private void AddGroup(Mesh mesh, int index)
            {
                var go = new GameObject(index == GroupCount ? "Lava ridges" : "Growing needle forest " + index);
                go.transform.SetParent(root.transform, false);
                go.SetActive(false);
                groups.Add(go.transform);
                if (mesh == null) return;
                meshes.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                Material soil = index == GroupCount ? HellGroundSurface.Material() : null;
                renderer.sharedMaterials = soil != null ? new[] { soil, soil, soil } : HellMaterials.Shared;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = soil != null;
                // No colliders: the forest changes the silhouette while roads and native vehicle movement remain usable.
            }

            private bool Sample(Vector3 local, out Vector3 surface)
            {
                surface = default;
                Vector3 world = root.transform.TransformPoint(local);
                float above = sizeScale > 1f ? 2200f : 800f;
                Vector3 from = new Vector3(world.x, root.transform.position.y + above, world.z);
                if (!Physics.Raycast(from, Vector3.down, out RaycastHit hit, above + 700f, PhysicsLayers.StaticsMask,
                        QueryTriggerInteraction.Ignore) || hit.point.y < root.transform.position.y - 600f || hit.normal.y < .42f) return false;
                if (hit.collider == null || GameAssets.i != null && hit.collider.sharedMaterial != GameAssets.i.terrainMaterial) return false;
                surface = root.transform.InverseTransformPoint(hit.point);
                return true;
            }

            private float Ridge(Vector3 local)
            {
                float fraction = new Vector2(local.x, local.z).magnitude / radius;
                float feather = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.28f, .48f, fraction)) *
                    Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, .82f, fraction));
                float noise = Mathf.PerlinNoise(local.x * .007f / sizeScale + (seed & 1023) * .1f,
                    local.z * .007f / sizeScale + ((seed >> 10) & 1023) * .1f);
                return Mathf.Pow(1f - Mathf.Abs(noise * 2f - 1f), 7f) * feather;
            }

            private void ProtectAccess()
            {
                clear.Add(new ProtectedStrip { Radius = Mathf.Clamp(radius * .28f, 150f, 400f) });
                Vector3 worldCenter = root.transform.position;
                foreach (HellPortal portal in HellPortal.All)
                    if (portal.Alive && Mathf.Abs(portal.Root.transform.position.y - worldCenter.y) < 400f)
                    {
                        Vector3 at = root.transform.InverseTransformPoint(portal.Root.transform.position);
                        clear.Add(new ProtectedStrip { A = at, B = at, Radius = portal.Radius * portal.Scale + 120f });
                    }
                foreach (Unit unit in UnitRegistry.allUnits)
                {
                    if (unit == null || unit.disabled || !(unit is GroundVehicle || unit is Aircraft || unit is Building)) continue;
                    Vector3 at = root.transform.InverseTransformPoint(unit.transform.position);
                    if (Mathf.Abs(at.y) > 300f || new Vector2(at.x, at.z).magnitude > radius + 200f) continue;
                    clear.Add(new ProtectedStrip { A = at, B = at, Radius = unit is Building ? 65f : 120f });
                }
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                    if (airbase != null && airbase.runways != null)
                        foreach (Airbase.Runway runway in airbase.runways)
                            if (runway?.Start != null && runway.End != null)
                                Protect(runway.Start.position, runway.End.position, runway.GetWidth() * .5f + 80f);
                RoadNetwork network = NetworkSceneSingleton<LevelInfo>.i?.roadNetwork;
                if (network?.roads == null) return;
                foreach (Road road in network.roads)
                {
                    if (road?.points == null) continue;
                    for (int i = 1; i < road.points.Count; i++) Protect(road.points[i - 1].ToLocalPosition(), road.points[i].ToLocalPosition(), 32f);
                }
            }

            private void Protect(Vector3 from, Vector3 to, float width)
            {
                Vector3 a = root.transform.InverseTransformPoint(from), b = root.transform.InverseTransformPoint(to);
                if (Mathf.Min(Mathf.Abs(a.y), Mathf.Abs(b.y)) > 400f || DistanceSquared(Vector3.zero, a, b) > (radius + width) * (radius + width)) return;
                clear.Add(new ProtectedStrip { A = a, B = b, Radius = width });
            }

            private bool Protected(Vector3 local, float extra)
            {
                foreach (ProtectedStrip strip in clear)
                    if (DistanceSquared(local, strip.A, strip.B) < (strip.Radius + extra) * (strip.Radius + extra)) return true;
                return false;
            }

            private void PrepareTerrain()
            {
                if (!haveForm || sizeScale > 1.1f) return;
                Vector3 hero = root.transform.TransformPoint(firstForm);
                foreach (Terrain terrain in Terrain.activeTerrains)
                {
                    TerrainData data = terrain.terrainData;
                    if (data == null || data.heightmapResolution < 3 || Mathf.Abs(terrain.SampleHeight(hero) + terrain.transform.position.y - hero.y) > 30f) continue;
                    Vector3 local = hero - terrain.transform.position;
                    if (local.x < 0f || local.z < 0f || local.x > data.size.x || local.z > data.size.z) continue;
                    int resolution = data.heightmapResolution;
                    int cx = Mathf.RoundToInt(local.x / data.size.x * (resolution - 1)), cz = Mathf.RoundToInt(local.z / data.size.z * (resolution - 1));
                    int half = Mathf.Min(32, Mathf.CeilToInt(Mathf.Min(220f, radius * .3f) / Mathf.Max(data.size.x, data.size.z) * (resolution - 1)));
                    int x = Mathf.Clamp(cx - half, 0, resolution - 1), z = Mathf.Clamp(cz - half, 0, resolution - 1);
                    int width = Mathf.Min(half * 2 + 1, resolution - x), height = Mathf.Min(half * 2 + 1, resolution - z);
                    float[,] original = data.GetHeights(x, z, width, height);
                    var patch = new TerrainPatch { Data = data, X = x, Z = z, Width = width, Height = height };
                    if (!TerrainCells.TryGetValue(data, out Dictionary<int, HeightCell> cells)) TerrainCells.Add(data, cells = new Dictionary<int, HeightCell>());
                    for (int row = 0; row < height; row++)
                        for (int col = 0; col < width; col++)
                        {
                            Vector3 world = terrain.transform.position + new Vector3((x + col) / (float)(resolution - 1) * data.size.x,
                                original[row, col] * data.size.y, (z + row) / (float)(resolution - 1) * data.size.z);
                            Vector3 at = root.transform.InverseTransformPoint(world);
                            if (Protected(at, 35f) || new Vector2(at.x, at.z).magnitude > radius * .94f) continue;
                            float border = Mathf.SmoothStep(0f, 1f, Mathf.Min(Mathf.Min(col, width - 1 - col), Mathf.Min(row, height - 1 - row)) / 6f);
                            float delta = Ridge(at) * 18f * border / data.size.y;
                            if (delta < .00001f) continue;
                            int key = (z + row) * resolution + x + col;
                            if (!cells.TryGetValue(key, out HeightCell cell))
                            {
                                if (heightCells >= MaxHeightCells) continue;
                                cells.Add(key, cell = new HeightCell { Original = original[row, col], Written = original[row, col] });
                                heightCells++;
                            }
                            cell.Owners[this] = 0f;
                            patch.Points.Add(new HeightPoint { Row = row, Col = col, Key = key, Delta = delta });
                        }
                    if (patch.Points.Count > 0) patches.Add(patch);
                    else if (cells.Count == 0) TerrainCells.Remove(data);
                }
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                foreach (TerrainPatch patch in patches) patch.Apply(this, 0f, true);
                patches.Clear();
                foreach (Mesh mesh in meshes) if (mesh != null) Object.Destroy(mesh);
                meshes.Clear();
                vents.Clear();
                groups.Clear();
                if (root != null) Object.Destroy(root);
                root = null;
                Forms -= forms;
                forms = 0;
                if (counted) { Count--; counted = false; }
            }
        }

        private sealed class GrowthRunner : MonoBehaviour
        {
            internal Handle Owner;
            private void Update() => Owner?.Advance(Time.time);
            private void OnDestroy() => Owner?.Dispose();
        }

        private struct ProtectedStrip { internal Vector3 A, B; internal float Radius; }
        private struct HeightPoint { internal int Row, Col, Key; internal float Delta; }
        private sealed class HeightCell
        {
            internal float Original, Written;
            internal readonly Dictionary<Handle, float> Owners = new Dictionary<Handle, float>();
        }

        private sealed class TerrainPatch
        {
            internal TerrainData Data;
            internal int X, Z, Width, Height;
            internal readonly List<HeightPoint> Points = new List<HeightPoint>();
            internal void Apply(Handle owner, float amount, bool remove)
            {
                if (ReferenceEquals(Data, null) || !TerrainCells.TryGetValue(Data, out Dictionary<int, HeightCell> cells)) return;
                if (Data == null) { ReleaseOwnership(); return; }
                try
                {
                    float[,] current = Data.GetHeights(X, Z, Width, Height);
                    foreach (HeightPoint point in Points)
                    {
                        if (!cells.TryGetValue(point.Key, out HeightCell cell)) continue;
                        float external = current[point.Row, point.Col] - cell.Written;
                        if (Mathf.Abs(external) > .0001f) cell.Original += external; // ignore heightmap quantization; preserve external edits
                        if (remove) cell.Owners.Remove(owner);
                        else cell.Owners[owner] = point.Delta * amount;
                        float displacement = 0f;
                        foreach (float value in cell.Owners.Values) displacement = Mathf.Max(displacement, value);
                        cell.Written = Mathf.Clamp01(cell.Original + displacement);
                        current[point.Row, point.Col] = cell.Written;
                        if (cell.Owners.Count == 0) { cells.Remove(point.Key); heightCells--; }
                    }
                    Data.SetHeightsDelayLOD(X, Z, current);
                    Data.SyncHeightmap();
                }
                catch (Exception e) { Plugin.Logger.LogWarning("[Spooktober] basalt terrain restore/grow: " + e.Message); }
                if (remove) ReleaseOwnership();
                if (cells.Count == 0) TerrainCells.Remove(Data);

                void ReleaseOwnership()
                {
                    foreach (HeightPoint point in Points)
                        if (cells.TryGetValue(point.Key, out HeightCell cell))
                        {
                            cell.Owners.Remove(owner);
                            if (cell.Owners.Count == 0) { cells.Remove(point.Key); heightCells--; }
                        }
                    if (cells.Count == 0) TerrainCells.Remove(Data);
                }
            }
        }

        private sealed class Model
        {
            internal Vector3[] Vertices, Normals;
            internal int[][] Indices;
            internal Bounds Bounds;
            internal static Model Read(string key)
            {
                Mesh mesh = HellMeshes.Mesh(key);
                if (mesh == null) return null;
                return new Model { Vertices = mesh.vertices, Normals = mesh.normals, Bounds = mesh.bounds,
                    Indices = new[] { mesh.GetTriangles(0), mesh.GetTriangles(1), mesh.GetTriangles(2) } };
            }
        }

        private sealed class Builder
        {
            private readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
            private readonly List<int>[] indices = { new List<int>(), new List<int>(), new List<int>() };
            internal void Add(Model model, Matrix4x4 transform)
            {
                int start = vertices.Count;
                Matrix4x4 normal = transform.inverse.transpose;
                for (int i = 0; i < model.Vertices.Length; i++)
                {
                    vertices.Add(transform.MultiplyPoint3x4(model.Vertices[i]));
                    normals.Add(normal.MultiplyVector(model.Normals[i]).normalized);
                }
                for (int sub = 0; sub < 3; sub++) foreach (int index in model.Indices[sub]) indices[sub].Add(start + index);
            }
            internal Mesh Finish(string name)
            {
                if (vertices.Count == 0) return null;
                var mesh = new Mesh { name = name, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16, subMeshCount = 3 };
                mesh.SetVertices(vertices); mesh.SetNormals(normals);
                for (int sub = 0; sub < 3; sub++) mesh.SetTriangles(indices[sub], sub, false);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        private static float Next(System.Random random) => (float)random.NextDouble();
        private static Gradient Ash
        {
            get
            {
                if (ash != null) return ash;
                ash = new Gradient();
                ash.SetKeys(new[] { new GradientColorKey(new Color(.75f, .3f, .12f), 0f),
                    new GradientColorKey(new Color(.27f, .23f, .2f), .2f), new GradientColorKey(new Color(.11f, .105f, .105f), 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.65f, .06f), new GradientAlphaKey(.35f, .5f), new GradientAlphaKey(0f, 1f) });
                return ash;
            }
        }
        private static float DistanceSquared(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 along = b - a; along.y = 0f;
            Vector3 from = p - a; from.y = 0f;
            float t = along.sqrMagnitude > .001f ? Mathf.Clamp01(Vector3.Dot(from, along) / along.sqrMagnitude) : 0f;
            return (from - along * t).sqrMagnitude;
        }
    }
}
