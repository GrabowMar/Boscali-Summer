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

        internal static float Fade { get; private set; }

        /// <param name="height">The camera's height above the sea (m).</param>
        internal static void Publish(float height, Light sun)
        {
            float fade = HighAir.EarthFade(height);
            if (fade <= 0f) { Clear(); return; }
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

        internal static void Clear()
        {
            if (Fade <= 0f) return;
            Fade = 0f;
            Shader.SetGlobalVector(ViewId, Vector4.zero);
        }
    }
}
