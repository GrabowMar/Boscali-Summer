using System.Collections.Generic;
using UnityEngine;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Fx;
using UnityEngine.Rendering;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    // Map-owned discovery is incremental and finite. Native materials and meshes stay untouched.
    internal sealed class TerrainRainDressing : IClientEffect
    {
        private struct Surface
        {
            public MeshRenderer Renderer; public Mesh Mesh; public int Slot; public uint Indices;
            public float Wet, LastRain, LastTime, NextSample; public bool Initialized;
        }
        private readonly WeatherField historyField = new WeatherField();
        private readonly List<Transform> pending = new List<Transform>(1024);
        private readonly List<Surface> surfaces = new List<Surface>(256);
        private readonly List<Material> slots = new List<Material>(16);
        private readonly int[] nearest = new int[8];
        private readonly float[] distances = new float[8];
        private readonly Plane[] planes = new Plane[6];
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private Transform map;
        private Material material;
        private Material detailMaterial;
        private Texture detailBoundTexture;
        private bool wetActive;
        private int inspected;
        private float wetness;
        private Vector3 sunDirection = Vector3.up;
        private Color sunColor = Color.black;
        private Color skyColor = Color.black;
        private float fogDensity;
        private float rippleTime;
        private float nextSelect;
        private static readonly int WetnessId = Shader.PropertyToID("_Wetness");
        private static readonly int RainId = Shader.PropertyToID("_Rain");
        private static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int SkyColorId = Shader.PropertyToID("_SkyColor");
        private static readonly int FogDensityId = Shader.PropertyToID("_FogDensity");
        private static readonly int RippleTimeId = Shader.PropertyToID("_RippleTime");
        private static readonly int WetFadeId = Shader.PropertyToID("_WetFade");
        private static readonly int DetailTextureId = Shader.PropertyToID("_TerrainDetail");
        private static readonly int DetailFadeId = Shader.PropertyToID("_DetailFade");
        private static readonly int DetailOriginId = Shader.PropertyToID("_DetailOrigin");
        private static readonly int DetailStrengthId = Shader.PropertyToID("_DetailStrength");
        internal int SurfaceCount => surfaces.Count;
        internal int DetailDrawCount { get; private set; }
        internal uint DetailTriangleCount { get; private set; }

        public string EffectId => "terrain-rain";

        public void ReleaseFx()
        {
            Reset();
        }

        public void DescribeFx(IDictionary<string, object> state)
        {
            state["fx.terrain-rain.surfaces"] = surfaces.Count;
            state["fx.terrain-rain.wetness"] = wetness;
            state["fx.terrain-detail.draws"] = DetailDrawCount;
            state["fx.terrain-detail.triangles"] = DetailTriangleCount;
        }
        internal float Wetness => wetness;

        /// <summary>
        /// World-space lighting for glints and sheen. Black colours (the default) leave the
        /// pass as pure damp darkening, which is what the standalone fixture measures.
        /// </summary>
        internal void SetLighting(Vector3 sunDirWorld, Color sun, Color sky, float fog, float time)
        {
            sunDirection = sunDirWorld.sqrMagnitude > 0.0001f ? sunDirWorld.normalized : Vector3.up;
            sunColor = sun;
            skyColor = sky;
            fogDensity = Mathf.Max(0f, fog);
            rippleTime = time;
        }

        internal void Update(Transform mapRoot, Camera camera, float rain, float deltaTime,
            WeatherField field = null, float missionTime = 0f, float? forcedRain = null,
            bool wetEnabled = true, bool detailEnabled = false, Shader detailShader = null, Texture detailTexture = null)
        {
            DetailDrawCount = 0;
            DetailTriangleCount = 0;
            if (Application.isBatchMode && !Application.isEditor) { Reset(); return; }
            if (map != mapRoot)
            {
                Reset();
                map = mapRoot;
                if (map != null) pending.Add(map);
            }
            if (map == null || camera == null) return;
            if (!wetEnabled && wetActive)
            {
                // Turning wet ground off must not leave stale moisture when it is enabled again.
                for (int i = 0; i < surfaces.Count; i++)
                {
                    var s = surfaces[i]; s.Initialized = false; s.Wet = 0f; surfaces[i] = s;
                }
                if (material != null) Object.Destroy(material);
                material = null;
            }
            wetActive = wetEnabled;
            // Ground follows mission precipitation, not whether the aircraft is sheltered/above clouds.
            wetness = wetEnabled ? Mathf.MoveTowards(wetness, Mathf.Clamp01(rain),
                Mathf.Max(0f, deltaTime) * (rain > wetness ? 1f / 35f : 1f / 180f)) : 0f;
            detailEnabled &= detailShader != null && detailShader.isSupported && detailTexture != null;
            if (!detailEnabled) ReleaseDetail();
            float detailQuality = detailEnabled ? FxBus.Scales.RenderTargets : 0f;
            float detailStrength = detailEnabled ? 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(700f, 1200f, camera.transform.position.y - Datum.LocalSeaY)) : 0f;
            float detailRange = detailEnabled && detailStrength > 0.001f ? 1000f * detailQuality : 0f;
            if (!wetEnabled && detailRange <= 0f) return;
            Discover();
            if (surfaces.Count == 0) return;
            if (wetEnabled && material == null)
            {
                Shader shader = CanopyShaderBundle.GetTerrainShader();
                if (shader != null) material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            float drawRange = wetEnabled && material != null
                ? AtmosphericSurfaceMath.GroundDrawRange(camera.transform.position.y - Datum.LocalSeaY) : 0f;
            float selectRange = Mathf.Max(drawRange, detailRange);
            if (selectRange <= 0f) return;
            // Which eight chunks are nearest and in view is a walk over every surface (bounds, visibility, frustum): redo it 30 times a second, draw them every frame.
            bool select = Time.unscaledTime >= nextSelect;
            if (select)
            {
                nextSelect = Time.unscaledTime + 1f / 30f;
                GeometryUtility.CalculateFrustumPlanes(camera, planes);
                for (int i = 0; i < 8; i++) { nearest[i] = -1; distances[i] = float.MaxValue; }
            }
            for (int i = 0; select && i < surfaces.Count; i++)
            {
                var s = surfaces[i];
                if (s.Renderer == null || s.Mesh == null || !s.Renderer.enabled ||
                    !s.Renderer.gameObject.activeInHierarchy || !s.Renderer.isVisible ||
                    (camera.cullingMask & (1 << s.Renderer.gameObject.layer)) == 0) continue;
                Bounds bounds = s.Renderer.bounds;
                float distance = bounds.SqrDistance(camera.transform.position);
                if (distance >= selectRange * selectRange || !GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                for (int j = 0; j < 8; j++)
                {
                    if (distance >= distances[j]) continue;
                    for (int k = 7; k > j; k--) { distances[k] = distances[k-1]; nearest[k] = nearest[k-1]; }
                    distances[j] = distance; nearest[j] = i; break;
                }
            }
            if (nearest[0] < 0) return;
            properties.SetVector(SunDirId, new Vector4(sunDirection.x, sunDirection.y, sunDirection.z, 0f));
            properties.SetColor(SunColorId, sunColor);
            properties.SetColor(SkyColorId, skyColor);
            properties.SetFloat(FogDensityId, fogDensity);
            properties.SetFloat(RippleTimeId, rippleTime);
            properties.SetVector(WetFadeId, new Vector4(drawRange * 0.65f, drawRange, 0f, 0f));
            if (detailRange > 0f) DrawDetail(camera, detailShader, detailTexture, detailRange, detailStrength, detailQuality);
            if (!wetEnabled || material == null) return;
            uint indexBudget = 300000; // At most 100,000 extra triangles on dense maps.
            int sampleBudget = 2;
            if (field != null) wetness = 0f;
            for (int i = 0; i < 8 && nearest[i] >= 0; i++)
            {
                var s = surfaces[nearest[i]];
                if (s.Renderer == null || s.Mesh == null || distances[i] >= drawRange * drawRange) continue;
                if (field != null && field.IsBuilt)
                {
                    if ((!s.Initialized || missionTime >= s.NextSample || missionTime < s.LastTime) && sampleBudget-- > 0)
                    {
                        GlobalPosition origin = s.Renderer.transform.GlobalPosition();
                        Vector3 offset = s.Renderer.bounds.center - s.Renderer.transform.position;
                        float gx = origin.x + offset.x, gz = origin.z + offset.z;
                        if (!s.Initialized || missionTime < s.LastTime)
                        {
                            s.Wet = 0f;
                            float start = Mathf.Max(field.Key.Epoch, missionTime - 180f);
                            // The history field is built non-moving, so its rain does not depend on the step time: build and sample once.
                            float oldRain = 0f;
                            for (float time = start; time < missionTime; time += 15f)
                            {
                                if (time == start)
                                {
                                    historyField.Build(field.Key, time, field.HalfX, field.HalfZ, field.HourOfDay);
                                    oldRain = forcedRain ?? Mathf.Clamp01(historyField.Sample(gx, gz).RainRate / 20f);
                                }
                                s.Wet = AtmosphericSurfaceMath.AdvanceWetness(s.Wet, oldRain, Mathf.Min(15f, missionTime - time));
                            }
                            s.Initialized = true;
                        }
                        else s.Wet = AtmosphericSurfaceMath.AdvanceWetness(s.Wet, s.LastRain, Mathf.Max(0f, missionTime - s.LastTime));
                        s.LastRain = forcedRain ?? Mathf.Clamp01(field.Sample(gx, gz).RainRate / 20f);
                        s.LastTime = missionTime;
                        s.NextSample = missionTime + 1f + (nearest[i] % 4) * 0.25f;
                        surfaces[nearest[i]] = s;
                    }
                    wetness = Mathf.Max(wetness, s.Wet);
                }
                float surfaceWet = field != null ? s.Wet : wetness;
                if (surfaceWet <= 0.001f) continue;
                if (s.Indices > indexBudget) continue;
                indexBudget -= s.Indices;
                properties.SetFloat(WetnessId, surfaceWet);
                properties.SetFloat(RainId, field != null ? s.LastRain : Mathf.Clamp01(rain));
                Graphics.DrawMesh(s.Mesh, s.Renderer.localToWorldMatrix, material,
                    s.Renderer.gameObject.layer, camera, s.Slot, properties,
                    ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
            }
        }

        private void DrawDetail(Camera camera, Shader shader, Texture texture, float range, float strength, float quality)
        {
            if (detailMaterial == null || detailMaterial.shader != shader || detailBoundTexture != texture)
            {
                ReleaseDetail();
                detailMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                detailMaterial.SetTexture(DetailTextureId, texture);
                detailBoundTexture = texture;
            }
            properties.SetFloat(DetailStrengthId, strength);
            properties.SetVector(DetailFadeId, new Vector4(range * 0.55f, range, 0.25f, 1.5f));
            // At full quality at most four existing submeshes / 30,000 triangles receive detail.
            // Lower/adaptive tiers reduce both distance and geometry; no native object is changed.
            uint budget = (uint)(90000f * quality);
            int limit = Mathf.Max(1, Mathf.FloorToInt(4f * quality));
            for (int i = 0; i < 8 && nearest[i] >= 0 && DetailDrawCount < limit; i++)
            {
                var s = surfaces[nearest[i]];
                if (s.Renderer == null || s.Mesh == null || !s.Renderer.enabled || !s.Renderer.gameObject.activeInHierarchy ||
                    (camera.cullingMask & (1 << s.Renderer.gameObject.layer)) == 0 ||
                    s.Indices > budget || distances[i] >= range * range) continue;
                Transform transform = s.Renderer.transform;
                GlobalPosition origin = transform.GlobalPosition();
                Vector3 position = transform.position;
                Matrix4x4 matrix = s.Renderer.localToWorldMatrix;
                // Renderer matrices can differ from Transform matrices under static batching.
                float x = origin.x + matrix.m03 - position.x, z = origin.z + matrix.m23 - position.z;
                properties.SetVector(DetailOriginId, new Vector4(
                    Mathf.Repeat(x, 64f) / 64f, Mathf.Repeat(z, 64f) / 64f, 0f, 0f));
                Graphics.DrawMesh(s.Mesh, matrix, detailMaterial,
                    s.Renderer.gameObject.layer, camera, s.Slot, properties,
                    ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
                budget -= s.Indices;
                DetailDrawCount++;
                DetailTriangleCount += s.Indices / 3;
            }
        }

        private void ReleaseDetail()
        {
            if (detailMaterial != null) Object.Destroy(detailMaterial);
            detailMaterial = null;
            detailBoundTexture = null;
        }

        private void Discover()
        {
            for (int budget = 0; budget < 128 && pending.Count > 0 && inspected < 8192; budget++)
            {
                int last = pending.Count - 1;
                Transform node = pending[last]; pending.RemoveAt(last); inspected++;
                if (node == null) continue;
                // Prioritize native terrain branches over large building trees; shader still decides eligibility.
                int children = Mathf.Min(node.childCount, 1024 - pending.Count);
                for (int pass = 0; pass < 2; pass++)
                    for (int i = children - 1; i >= 0; i--)
                    {
                        Transform child = node.GetChild(i);
                        bool terrainBranch = child.name.StartsWith("terrain", System.StringComparison.OrdinalIgnoreCase);
                        if (terrainBranch == (pass == 1)) pending.Add(child);
                    }
                var renderer = node.GetComponent<MeshRenderer>();
                var filter = node.GetComponent<MeshFilter>();
                if (renderer == null || filter == null || filter.sharedMesh == null) continue;
                renderer.GetSharedMaterials(slots);
                for (int i = 0; i < slots.Count && i < 16 && i < filter.sharedMesh.subMeshCount; i++)
                {
                    Material source = slots[i];
                    // Exact installed-game shader contract; never infer terrain from an object name.
                    if (source == null || source.shader == null || source.shader.name != "Shader Graphs/TerrainShader") continue;
                    surfaces.Add(new Surface { Renderer = renderer, Mesh = filter.sharedMesh, Slot = i,
                        Indices = filter.sharedMesh.GetIndexCount(i) });
                    if (surfaces.Count == 256) { pending.Clear(); return; }
                }
                slots.Clear();
            }
            if (inspected >= 8192) pending.Clear();
        }

        internal void Reset()
        {
            if (material != null) Object.Destroy(material);
            ReleaseDetail();
            material = null; map = null; wetness = 0f; inspected = 0;
            wetActive = false;
            DetailDrawCount = 0;
            DetailTriangleCount = 0;
            pending.Clear(); surfaces.Clear(); slots.Clear();
            nextSelect = 0f;
        }
    }
}
