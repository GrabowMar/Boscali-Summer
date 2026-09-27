using System.Collections.Generic;
using UnityEngine;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Fx;
using UnityEngine.Rendering;

namespace BoscaliSummer.Features.Weather.Visuals
{
    // Additional glass-only draws preserve all native materials, frames and animations.
    // Owns the droplet sim tick plus the heightfield render: UpdateSim steps every live
    // pane with its projected flow (called in any view so rain stays live), Draw shows
    // the state through the cockpit camera. Dry glass releases the sim: fresh rain
    // starts from a clean sheet instead of resurrecting stale puddles.
    internal sealed class CanopyShaderDressing : IClientEffect
    {
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int DropTexId = Shader.PropertyToID("_DropTex");
        private static readonly int SaltId = Shader.PropertyToID("_Salt");
        private static readonly int MapAxisId = Shader.PropertyToID("_MapAxis");
        private static readonly int LightId = Shader.PropertyToID("_LightLevel");
        private static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int FogColorId = Shader.PropertyToID("_FogColor");
        private static readonly int RefractId = Shader.PropertyToID("_Refract");

        private const float PatternDensity = 1.5f; // tiles/m, mirrors the shader
        private const float GravityTiles = 0.08f; // parked beads creep; airflow drives fast runoff

        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private readonly CanopyDropletSim sim = new CanopyDropletSim();
        private readonly Vector2[] flowsTiles = new Vector2[CanopyDropletSim.MaxPanes];
        private Material material;
        private Material updateMaterial;
        private Vector3 sunDirection = Vector3.zero;
        private Color sunColor = Color.black;
        private Color fogColor = new Color(0.55f, 0.6f, 0.68f, 1f);
        private bool refract;

        public string EffectId => "canopy";

        public FxBudget Budget => new FxBudget(4 * 1024 * 1024, CanopyDropletSim.MaxPanes * 2, 0, true);

        public void ReleaseFx()
        {
            Detach();
            sim.Release();
        }

        public void DescribeFx(IDictionary<string, object> state)
        {
            state["fx.canopy.panes"] = sim.PaneCount;
            state["fx.canopy.materials"] = (material != null ? 1 : 0) + (updateMaterial != null ? 1 : 0);
        }

        /// <summary>
        /// World-space lighting for the glass: sun direction (toward the sun, zero when it
        /// is off), its colour, the fog colour used as the drop body, and whether the
        /// pipeline provides an opaque texture to refract. Defaults keep the fixture look.
        /// </summary>
        internal void SetLighting(Vector3 sunDirWorld, Color sun, Color fog, bool sceneRefraction)
        {
            sunDirection = sunDirWorld;
            sunColor = sun;
            fogColor = fog;
            refract = sceneRefraction;
        }

        internal void UpdateSim(IReadOnlyList<CanopySurface> surfaces, float rain,
            float speedNorm, Vector3 slipWorld, float dt)
        {
            if (surfaces == null || surfaces.Count == 0 || (rain <= 0.001f && sim.PaneCount == 0)) return;
            if (updateMaterial == null)
            {
                Shader updateShader = CanopyShaderBundle.GetUpdateShader();
                if (updateShader == null || !updateShader.isSupported) return;
                updateMaterial = new Material(updateShader) { name = "BoscaliCanopyDroplets", hideFlags = HideFlags.HideAndDontSave };
            }
            int count = Mathf.Min(surfaces.Count, flowsTiles.Length);
            for (int i = 0; i < count; i++)
            {
                CanopySurface surface = surfaces[i];
                if (surface.Renderer == null || surface.Mesh == null) { flowsTiles[i] = Vector2.zero; continue; }
                Transform glass = surface.Renderer.transform;
                Vector3 gravityObj = glass.InverseTransformDirection(Vector3.down) * GravityTiles;
                Vector3 slipObj = glass.InverseTransformDirection(slipWorld) * PatternDensity;
                Vector3 flowObj = gravityObj + slipObj;
                flowsTiles[i] = ProjectFlow(flowObj, MapAxis(surface.Mesh));
            }
            using (FxBus.Time("canopy"))
            {
                sim.Update(updateMaterial, surfaces, flowsTiles,
                rain, speedNorm, dt);
            }
        }

        internal bool Draw(IReadOnlyList<CanopySurface> surfaces, Camera camera, float wetness,
            float lightLevel)
        {
            if (camera == null || surfaces == null || surfaces.Count == 0) return false;
            if (wetness <= 0.001f) { sim.Release(); return false; }
            if (material == null)
            {
                Shader shader = CanopyShaderBundle.GetShader();
                if (shader == null || !shader.isSupported) return false;
                material = new Material(shader) { name = "BoscaliCanopyRain", hideFlags = HideFlags.HideAndDontSave };
            }
            properties.SetFloat(IntensityId, wetness);
            properties.SetFloat(LightId, Mathf.Clamp(lightLevel, 0.04f, 1f));
            properties.SetVector(SunDirId, new Vector4(sunDirection.x, sunDirection.y, sunDirection.z, 0f));
            properties.SetColor(SunColorId, sunColor);
            properties.SetColor(FogColorId, fogColor);
            properties.SetFloat(RefractId, refract ? 1f : 0f);
            bool drawn = false;
            for (int i = 0; i < surfaces.Count; i++)
            {
                CanopySurface surface = surfaces[i];
                MeshRenderer renderer = surface.Renderer;
                if (renderer == null || surface.Mesh == null || !renderer.enabled ||
                    !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff ||
                    (surface.Lod != null && !renderer.isVisible) ||
                    (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
                int key = CanopyDropletSim.KeyFor(renderer.GetInstanceID(), surface.Submesh);
                RenderTexture state = sim.StateFor(key);
                if (state == null) continue;
                Vector2 salt = sim.SaltFor(key);
                properties.SetTexture(DropTexId, state);
                properties.SetVector(SaltId, new Vector4(salt.x, salt.y, 0f, 0f));
                properties.SetFloat(MapAxisId, MapAxis(surface.Mesh));
                Graphics.DrawMesh(surface.Mesh, renderer.localToWorldMatrix, material,
                    renderer.gameObject.layer, camera, surface.Submesh, properties,
                    ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
                drawn = true;
            }
            return drawn;
        }

        // Planar frame = the mesh's thin axis (CPU-side bounds, no vertex read).
        // Must mirror the shader's _MapAxis mapping exactly.
        private static int MapAxis(Mesh mesh)
        {
            Vector3 size = mesh.bounds.size;
            if (size.z <= size.x && size.z <= size.y) return 0;
            return size.x <= size.y ? 1 : 2;
        }

        private static Vector2 ProjectFlow(Vector3 flowObj, int axis)
        {
            if (axis == 1) return new Vector2(flowObj.z, flowObj.y);
            if (axis == 2) return new Vector2(flowObj.x, flowObj.z);
            return new Vector2(flowObj.x, flowObj.y);
        }

        internal void Detach()
        {
            sim.Release();
            if (material != null) Object.Destroy(material);
            material = null;
            if (updateMaterial != null) Object.Destroy(updateMaterial);
            updateMaterial = null;
        }

        internal void ClearWater() => sim.Release();
    }
}
