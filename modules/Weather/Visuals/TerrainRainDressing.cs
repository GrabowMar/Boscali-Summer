using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Features.Weather.Visuals
{
    // Map-owned discovery is incremental and finite. Native materials and meshes stay untouched.
    internal sealed class TerrainRainDressing
    {
        private struct Surface { public MeshRenderer Renderer; public Mesh Mesh; public int Slot; public uint Indices; }
        private readonly List<Transform> pending = new List<Transform>(1024);
        private readonly List<Surface> surfaces = new List<Surface>(256);
        private readonly List<Material> slots = new List<Material>(16);
        private readonly int[] nearest = new int[8];
        private readonly float[] distances = new float[8];
        private readonly Plane[] planes = new Plane[6];
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private Transform map;
        private Material material;
        private int inspected;
        private float wetness;
        private Vector3 sunDirection = Vector3.up;
        private Color sunColor = Color.black;
        private Color skyColor = Color.black;
        private float fogDensity;
        private float rippleTime;
        private static readonly int WetnessId = Shader.PropertyToID("_Wetness");
        private static readonly int RainId = Shader.PropertyToID("_Rain");
        private static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int SkyColorId = Shader.PropertyToID("_SkyColor");
        private static readonly int FogDensityId = Shader.PropertyToID("_FogDensity");
        private static readonly int RippleTimeId = Shader.PropertyToID("_RippleTime");
        internal int SurfaceCount => surfaces.Count;
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

        internal void Update(Transform mapRoot, Camera camera, float rain, float deltaTime)
        {
            if (map != mapRoot)
            {
                Reset();
                map = mapRoot;
                if (map != null) pending.Add(map);
            }
            if (map == null || camera == null) return;
            // Ground follows mission precipitation, not whether the aircraft is sheltered/above clouds.
            wetness = Mathf.MoveTowards(wetness, Mathf.Clamp01(rain),
                Mathf.Max(0f, deltaTime) * (rain > wetness ? 1f / 35f : 1f / 180f));
            Discover();
            if (wetness < 0.001f || surfaces.Count == 0) return;
            if (material == null)
            {
                Shader shader = CanopyShaderBundle.GetTerrainShader();
                if (shader == null) return;
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            GeometryUtility.CalculateFrustumPlanes(camera, planes);
            for (int i = 0; i < 8; i++) { nearest[i] = -1; distances[i] = float.MaxValue; }
            for (int i = 0; i < surfaces.Count; i++)
            {
                var s = surfaces[i];
                if (s.Renderer == null || s.Mesh == null || !s.Renderer.enabled ||
                    !s.Renderer.gameObject.activeInHierarchy || !s.Renderer.isVisible ||
                    (camera.cullingMask & (1 << s.Renderer.gameObject.layer)) == 0) continue;
                Bounds bounds = s.Renderer.bounds;
                float distance = bounds.SqrDistance(camera.transform.position);
                if (distance >= 1200f * 1200f || !GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                for (int j = 0; j < 8; j++)
                {
                    if (distance >= distances[j]) continue;
                    for (int k = 7; k > j; k--) { distances[k] = distances[k-1]; nearest[k] = nearest[k-1]; }
                    distances[j] = distance; nearest[j] = i; break;
                }
            }
            if (nearest[0] < 0) return;
            properties.SetFloat(WetnessId, wetness);
            properties.SetFloat(RainId, Mathf.Clamp01(rain));
            properties.SetVector(SunDirId, new Vector4(sunDirection.x, sunDirection.y, sunDirection.z, 0f));
            properties.SetColor(SunColorId, sunColor);
            properties.SetColor(SkyColorId, skyColor);
            properties.SetFloat(FogDensityId, fogDensity);
            properties.SetFloat(RippleTimeId, rippleTime);
            uint indexBudget = 300000; // At most 100,000 extra triangles on dense maps.
            for (int i = 0; i < 8 && nearest[i] >= 0; i++)
            {
                var s = surfaces[nearest[i]];
                if (s.Indices > indexBudget) continue;
                indexBudget -= s.Indices;
                Graphics.DrawMesh(s.Mesh, s.Renderer.localToWorldMatrix, material,
                    s.Renderer.gameObject.layer, camera, s.Slot, properties,
                    ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
            }
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
            material = null; map = null; wetness = 0f; inspected = 0;
            pending.Clear(); surfaces.Clear(); slots.Clear();
        }
    }
}
