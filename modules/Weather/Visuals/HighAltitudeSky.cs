using BoscaliSummer.Core.Util;
using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>
    /// The globals the cloud shaders read for the view from very high up (2026-10-28): the composite paints the curved Earth and its limb in sky
    /// pixels, and the horizon deck and thin layers stop at the planet's horizon. All zero below 12 km, where nothing changes. Call after
    /// <c>RainAtmosphere</c> has written the fog: the backdrop's column haze is the scene's fog density times the height, so the two meet at the far plane.
    /// </summary>
    internal static class HighAltitudeSky
    {
        private static readonly int ViewId = Shader.PropertyToID("_EarthView");
        private static readonly int HazeId = Shader.PropertyToID("_EarthHaze");
        private static readonly int SunId = Shader.PropertyToID("_EarthSun");
        private static readonly int SeaId = Shader.PropertyToID("_EarthSea");

        private static readonly int OrbitCloudId = Shader.PropertyToID("_OrbitCloudTex");
        private const string OrbitCloudResource = "BoscaliSummer.Weather.orbit-clouds.png";
        private static Texture2D orbitClouds;

        internal static float Fade { get; private set; }

        /// <param name="height">The camera's height above the sea (m).</param>
        internal static void Publish(float height, Light sun)
        {
            float fade = HighAir.EarthFade(height);
            if (fade <= 0f) { Clear(); return; }
            if (orbitClouds == null) LoadOrbitClouds();
            Fade = fade;
            Shader.SetGlobalVector(ViewId, new Vector4(height, fade, RenderSettings.fogDensity * height, HighAir.EarthRadius));
            Color haze = RenderSettings.fogColor.linear;
            Shader.SetGlobalVector(HazeId, new Vector4(haze.r, haze.g, haze.b, 0f));
            Vector3 toSun = sun != null ? -sun.transform.forward : Vector3.up;
            Shader.SetGlobalVector(SunId, new Vector4(toSun.x, toSun.y, toSun.z, 0f));
            // The far sea: the ocean's own deep colour, dimmed with the haze's brightness (0.45 is a clear day's), so it darkens at dusk and night.
            Color deep = OceanDressing.CalmDeep * Mathf.Clamp01(haze.grayscale / 0.45f);
            Shader.SetGlobalVector(SeaId, new Vector4(deep.r, deep.g, deep.b, 0f));
        }

        /// <summary>The orbit deck's baked tile (tools/Bake-OrbitClouds.py): linear data, mipmapped, repeating. Loaded once; a failure leaves
        /// a black 1 x 1, which draws no orbit cloud, so the loader never runs again.</summary>
        private static void LoadOrbitClouds()
        {
            orbitClouds = new Texture2D(2, 2, TextureFormat.RGB24, true, true) { name = "BoscaliOrbitClouds", hideFlags = HideFlags.DontSave };
            try
            {
                byte[] bytes = EmbeddedResources.ReadAll(typeof(HighAltitudeSky).Assembly, OrbitCloudResource, 8 * 1024 * 1024);
                if (bytes == null || !ImageConversion.LoadImage(orbitClouds, bytes, true))
                {
                    Debug.LogWarning("[BoscaliSummer] Orbit cloud texture missing; no clouds from orbit.");
                    orbitClouds.Reinitialize(1, 1);
                    orbitClouds.SetPixel(0, 0, Color.black);
                    orbitClouds.Apply(false, true);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[BoscaliSummer] Orbit cloud texture load failed: " + e.Message);
            }
            orbitClouds.wrapMode = TextureWrapMode.Repeat;
            orbitClouds.filterMode = FilterMode.Trilinear;
            orbitClouds.anisoLevel = 4;
            Shader.SetGlobalTexture(OrbitCloudId, orbitClouds);
        }

        internal static void Clear()
        {
            if (Fade <= 0f) return;
            Fade = 0f;
            Shader.SetGlobalVector(ViewId, Vector4.zero);
        }
    }
}
