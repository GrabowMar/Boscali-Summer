using System.Collections.Generic;
using BoscaliSummer.Features.Weather.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Features.Weather.Visuals
{
    // Additional glass-only draws preserve all native materials, frames and animations.
    internal sealed class CanopyShaderDressing
    {
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int FlowPhaseId = Shader.PropertyToID("_FlowPhase");
        private static readonly int FlowId = Shader.PropertyToID("_Flow");
        private static readonly int LightId = Shader.PropertyToID("_LightLevel");
        private static readonly int SpeedId = Shader.PropertyToID("_Speed");
        private static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        private static readonly int SunColorId = Shader.PropertyToID("_SunColor");
        private static readonly int FogColorId = Shader.PropertyToID("_FogColor");
        private static readonly int RefractId = Shader.PropertyToID("_Refract");
        private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
        private Material material;
        private float phase;
        private Vector3 sunDirection = Vector3.zero;
        private Color sunColor = Color.black;
        private Color fogColor = new Color(0.55f, 0.6f, 0.68f, 1f);
        private bool refract;

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

        internal bool Draw(IReadOnlyList<CanopySurface> surfaces, Camera camera, float wetness,
            Vector3 airflow, float speedNorm, float lightLevel)
        {
            if (camera == null || wetness <= 0.001f || surfaces == null || surfaces.Count == 0) return false;
            if (material == null)
            {
                Shader shader = CanopyShaderBundle.GetShader();
                if (shader == null || !shader.isSupported) return false;
                material = new Material(shader) { name = "BoscaliCanopyRain", hideFlags = HideFlags.HideAndDontSave };
            }
            phase = (phase + Time.deltaTime * CanopyShaderParams.FlowPhaseRate(wetness, speedNorm)) % 1024f;
            properties.SetFloat(IntensityId, wetness);
            properties.SetFloat(FlowPhaseId, phase);
            properties.SetFloat(LightId, Mathf.Clamp(lightLevel, 0.04f, 1f));
            properties.SetFloat(SpeedId, Mathf.Clamp01(speedNorm));
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
                Transform glass = renderer.transform;
                Vector3 flow = glass.InverseTransformDirection(airflow);
                Vector3 sun = glass.InverseTransformDirection(sunDirection);
                properties.SetVector(FlowId, new Vector4(flow.x, flow.y, flow.z, 0f));
                properties.SetVector(SunDirId, new Vector4(sun.x, sun.y, sun.z, 0f));
                Graphics.DrawMesh(surface.Mesh, renderer.localToWorldMatrix, material,
                    renderer.gameObject.layer, camera, surface.Submesh, properties,
                    ShadowCastingMode.Off, false, null, LightProbeUsage.Off);
                drawn = true;
            }
            return drawn;
        }

        internal void Detach()
        {
            if (material != null) Object.Destroy(material);
            material = null;
            phase = 0f;
        }
    }
}
