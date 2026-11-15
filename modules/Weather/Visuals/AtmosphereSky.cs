using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>Optional skybox look, using native time-of-day inputs and the existing cloud/sun/moon renderers.</summary>
    internal sealed class AtmosphereSky : IClientEffect
    {
        private static AssetBundle bundle;
        private static Shader shader;
        private static bool attempted;
        private Material material;
        private Shader original;
        private int originalQueue;
        private Material yieldedMaterial;

        public string EffectId => "atmosphere-sky";
        public void ReleaseFx() => Restore();
        public void DescribeFx(IDictionary<string, object> state) => state["fx.atmosphere-sky.active"] = material != null;

        internal void Tick(LevelInfo level, Camera camera, bool enabled, float haze, float nightLight)
        {
            if (!enabled || level == null || camera == null || Application.isBatchMode)
            { Restore(); return; }
            Material current = RenderSettings.skybox;
            if (material != null && material.shader != shader)
            {
                Material foreign = material;
                Restore();
                yieldedMaterial = foreign;
            }
            if (current == yieldedMaterial) return;
            if (material != null && current != material) Restore();
            if (material == null)
            {
                Shader skyShader = LoadShader();
                // Require the native sky's parameter contract; incompatible mission skies keep their renderer.
                if (skyShader == null || current == null || !current.HasProperty("_SunDirection") ||
                    !current.HasProperty("_ScatterIntensity") || !current.HasProperty("_AmbientIntensity")) return;
                material = current;
                original = current.shader;
                originalQueue = current.renderQueue;
                material.shader = skyShader;
                material.renderQueue = 1000;
            }
            Vector3 sun = level.sun != null ? -level.sun.transform.forward : Vector3.up;
            Light moon = level.moon;
            Vector3 moonDirection = moon != null ? -moon.transform.forward : Vector3.down;
            float moonlight = moon != null && moon.isActiveAndEnabled && moonDirection.y > 0f ? Mathf.Clamp01(moon.intensity * 2f) : 0f;
            material.SetVector("_SkyView", new Vector4(Mathf.Max(0f, camera.transform.position.GlobalY()), moonlight, nightLight, Mathf.Clamp01(haze)));
            material.SetVector("_SkySun", new Vector4(sun.x, sun.y, sun.z, 0f));
            material.SetVector("_SkyMoon", new Vector4(moonDirection.x, moonDirection.y, moonDirection.z, moonlight));
            Color fog = RenderSettings.fogColor.linear;
            material.SetVector("_SkyFog", new Vector4(fog.r, fog.g, fog.b, 0f));
            material.SetMatrix("_SkyStarRotation", level.starfield != null ? level.starfield.transform.worldToLocalMatrix : Matrix4x4.identity);
        }

        internal void Restore()
        {
            yieldedMaterial = null;
            if (material == null) return;
            if (material.shader == shader) { material.shader = original; material.renderQueue = originalQueue; }
            material = null;
            original = null;
        }

        private static Shader LoadShader()
        {
            if (attempted) return shader;
            attempted = true;
            try
            {
                byte[] bytes = EmbeddedResources.ReadAll(typeof(AtmosphereSky).Assembly, "BoscaliSummer.Weather.sky.bundle", 512 * 1024);
                if (bytes == null) return null;
                bundle = AssetBundle.LoadFromMemory(bytes);
                if (bundle != null)
                    foreach (Shader candidate in bundle.LoadAllAssets<Shader>())
                        if (candidate != null && candidate.name == "Boscali/AtmosphereSky" && candidate.isSupported)
                        { shader = candidate; return shader; }
                if (bundle != null) bundle.Unload(true);
                bundle = null;
            }
            catch (Exception error)
            {
                if (bundle != null) bundle.Unload(true);
                bundle = null;
                Debug.LogWarning("[BoscaliSummer] Atmospheric sky unavailable: " + error.Message);
            }
            return null;
        }
    }
}
