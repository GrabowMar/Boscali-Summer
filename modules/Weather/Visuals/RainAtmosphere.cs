using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Core.Contracts;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    /// <summary>
    /// Rain haze layered on vanilla's lighting without a patch. LevelInfo rewrites fog and
    /// ambient once a second in Update; this runs in LateUpdate, treats any value it did not
    /// write itself as the new vanilla base, and re-applies the local rain modifiers on top.
    /// Underwater and foreign dense fog are left alone. Restore puts the base back.
    /// </summary>
    internal sealed class RainAtmosphere : IClientEffect
    {
        private struct Slot
        {
            public float Base, Written;
            public void Track(float now, bool applied)
            {
                if (!applied || !RainSkyMath.IsOwnWrite(Written, now)) Base = now;
            }
        }

        private struct ColorSlot
        {
            public Color Base, Written;
            public void Track(Color now, bool applied)
            {
                if (!applied || !RainAtmosphere.SameColor(Written, now)) Base = now;
            }
        }

        // The skybox's own horizon colour: vanilla writes it beside RenderSettings.fogColor (LevelInfo.UpdateTimeOfDayLighting), so the overcast grey
        // never reached the horizon and a blue band showed between the hazed sea and the cloud deck (2026-10-18).
        private static readonly int SkyFogId = Shader.PropertyToID("_FogColor");
        // The sky dome's brightness, written by vanilla beside it (ambient intensity x air density); dark nights scale it so the stars stand out.
        private static readonly int SkyAmbientId = Shader.PropertyToID("_AmbientIntensity");

        private bool applied;
        private Slot fog, ambient, skyAmbient;
        private ColorSlot fogColor, skyColor, equatorColor, groundColor, skyFog;
        private Material skyMaterial;
        private Slot sunSlot;
        private Light sunOwner;

        public string EffectId => "rain-haze";

        public void ReleaseFx()
        {
            Restore();
        }

        public void DescribeFx(IDictionary<string, object> state)
        {
            state["fx.rain-haze.applied"] = applied;
            state["fx.rain-haze.fog"] = LastFogMultiplier;
        }

        internal bool Applied => applied;
        internal float LastFogMultiplier { get; private set; } = 1f;
        /// <summary>The dark-nights factor of the last apply (1 by day, when off or restored).</summary>
        internal float LastNightLight { get; private set; } = 1f;
        /// <summary>The factor this effect currently puts on <c>sun.intensity</c> (1 when it writes nothing): the cloud lighting divides it out, so the cloud tops keep the sun's own colour.</summary>
        internal static float CurrentSunScale { get; private set; } = 1f;

        /// <param name="shade">0..1 how much cloud deck hangs overhead; dims and greys the air
        /// under overcast even when it is dry.</param>
        /// <param name="sun">The game's sun; its elevation also turns the night fog grey (<see cref="OvercastLook.NightHaze"/>), dry or not.</param>
        /// <param name="moon">The game's moon <c>Light.intensity</c> (0 to 0.5), for <paramref name="darkNight"/>.</param>
        /// <param name="darkNight">Darken the night and turn its ambient moonlit blue-grey (<see cref="OvercastLook.NightLight"/>).</param>
        /// <param name="height">The camera's height above the sea (m): above 12 km the fog thins to a column of air (<see cref="HighAir.FogScale"/>).</param>
        internal void Apply(float rain, bool underwater, float shade = 0f, Light sun = null, float moon = 0f, bool darkNight = false, float height = 0f)
        {
            rain = Mathf.Clamp01(rain);
            shade = Mathf.Clamp01(shade);
            float sunHeight = sun != null ? -sun.transform.forward.y : 1f;
            float thin = HighAir.FogScale(height);
            if (underwater || (rain <= 0.001f && shade <= 0.001f && sunHeight >= 0f && thin >= 1f)) { Restore(); return; }

            fog.Track(RenderSettings.fogDensity, applied);
            ambient.Track(RenderSettings.ambientIntensity, applied);
            fogColor.Track(RenderSettings.fogColor, applied);
            skyColor.Track(RenderSettings.ambientSkyColor, applied);
            equatorColor.Track(RenderSettings.ambientEquatorColor, applied);
            groundColor.Track(RenderSettings.ambientGroundColor, applied);
            Material sky = RenderSettings.skybox;
            if (sky != skyMaterial) skyMaterial = null;
            if (sky != null && sky.HasProperty(SkyFogId))
            {
                skyFog.Track(sky.GetColor(SkyFogId), applied && skyMaterial != null);
                if (sky.HasProperty(SkyAmbientId)) skyAmbient.Track(sky.GetFloat(SkyAmbientId), applied && skyMaterial != null);
                skyMaterial = sky;
            }
            if (sun != null) { sunOwner = sun; sunSlot.Track(sun.intensity, applied); }

            // Vanilla storm fog peaks near 0.0014; the underwater preset is 0.05. Never amplify the latter.
            if (fog.Base > 0.02f) { Restore(); return; }

            LastFogMultiplier = RainSkyMath.FogMultiplier(rain) * RainSkyMath.ShadeFog(shade);
            float dim = RainSkyMath.AmbientMultiplier(rain) * RainSkyMath.ShadeAmbient(shade);
            // Vanilla's near-opaque storm fog erases the cloud bodies and terrain a pilot
            // must still be able to navigate by. Heavy rain keeps several km of contrast.
            // Forced local showers also need haze when the mission starts with zero fog.
            fog.Written = Mathf.Min(Mathf.Max(fog.Base * LastFogMultiplier, rain * 0.00012f), 0.00055f) * thin;
            // Cloud extinction belongs to the ray-marched volume. Applying it to global
            // fog as well erased gaps and local geometry as soon as the camera crossed an edge.
            // Dark nights: ambient, fog and sky dome take one factor, so near terrain, far haze and the sky stay in step at the horizon.
            float night = darkNight ? OvercastLook.NightLight(sunHeight, moon) : 1f;
            LastNightLight = night;
            ambient.Written = ambient.Base * dim * night;
            // At night the fog colours turn grey too: vanilla's night fog is a saturated teal far brighter than its night sky, and the far
            // sea (Boscali/Ocean fogs to it) and the distant deck seen from above glowed cyan along the horizon (2026-10-19).
            fogColor.Written = Dim(NightGrey(ShadeTint(Tint(fogColor.Base, rain), shade), sunHeight, shade), night);
            skyColor.Written = Moonlit(Dim(skyColor.Base, dim), sunHeight, moon, darkNight);
            equatorColor.Written = Moonlit(Dim(equatorColor.Base, dim), sunHeight, moon, darkNight);
            groundColor.Written = Moonlit(Dim(groundColor.Base, dim), sunHeight, moon, darkNight);

            RenderSettings.fogDensity = fog.Written;
            RenderSettings.ambientIntensity = ambient.Written;
            RenderSettings.fogColor = fogColor.Written;
            RenderSettings.ambientSkyColor = skyColor.Written;
            RenderSettings.ambientEquatorColor = equatorColor.Written;
            RenderSettings.ambientGroundColor = groundColor.Written;
            if (skyMaterial != null)
            {
                skyFog.Written = Dim(NightGrey(ShadeTint(Tint(skyFog.Base, rain), shade), sunHeight, shade), night);
                skyMaterial.SetColor(SkyFogId, skyFog.Written);
                if (skyMaterial.HasProperty(SkyAmbientId))
                {
                    skyAmbient.Written = skyAmbient.Base * night;
                    skyMaterial.SetFloat(SkyAmbientId, skyAmbient.Written);
                }
            }
            CurrentSunScale = OvercastLook.SunScale(shade);
            if (sunOwner != null) { sunSlot.Written = sunSlot.Base * CurrentSunScale; sunOwner.intensity = sunSlot.Written; }
            applied = true;
        }

        /// <summary>Undo only values that are still ours; vanilla rewrites within a second anyway.</summary>
        internal void Restore()
        {
            if (!applied) return;
            applied = false;
            LastFogMultiplier = 1f;
            LastNightLight = 1f;
            CurrentSunScale = 1f;
            if (sunOwner != null && RainSkyMath.IsOwnWrite(sunSlot.Written, sunOwner.intensity)) sunOwner.intensity = sunSlot.Base;
            sunOwner = null;
            if (RainSkyMath.IsOwnWrite(fog.Written, RenderSettings.fogDensity)) RenderSettings.fogDensity = fog.Base;
            if (RainSkyMath.IsOwnWrite(ambient.Written, RenderSettings.ambientIntensity)) RenderSettings.ambientIntensity = ambient.Base;
            if (SameColor(fogColor.Written, RenderSettings.fogColor)) RenderSettings.fogColor = fogColor.Base;
            if (SameColor(skyColor.Written, RenderSettings.ambientSkyColor)) RenderSettings.ambientSkyColor = skyColor.Base;
            if (SameColor(equatorColor.Written, RenderSettings.ambientEquatorColor)) RenderSettings.ambientEquatorColor = equatorColor.Base;
            if (SameColor(groundColor.Written, RenderSettings.ambientGroundColor)) RenderSettings.ambientGroundColor = groundColor.Base;
            if (skyMaterial != null && SameColor(skyFog.Written, skyMaterial.GetColor(SkyFogId))) skyMaterial.SetColor(SkyFogId, skyFog.Base);
            if (skyMaterial != null && skyMaterial.HasProperty(SkyAmbientId) && RainSkyMath.IsOwnWrite(skyAmbient.Written, skyMaterial.GetFloat(SkyAmbientId)))
                skyMaterial.SetFloat(SkyAmbientId, skyAmbient.Base);
            skyMaterial = null;
        }

        private static Color Tint(Color c, float rain)
        {
            float r = c.r, g = c.g, b = c.b;
            RainSkyMath.FogTint(rain, ref r, ref g, ref b);
            return new Color(r, g, b, c.a);
        }

        private static Color ShadeTint(Color c, float shade)
        {
            float r = c.r, g = c.g, b = c.b;
            OvercastLook.Tint(shade, ref r, ref g, ref b);
            return new Color(r, g, b, c.a);
        }

        private static Color NightGrey(Color c, float sunHeight, float shade)
        {
            OvercastLook.NightHaze(sunHeight, shade, ref c.r, ref c.g, ref c.b);
            return c;
        }

        private static Color Dim(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        private static Color Moonlit(Color c, float sunHeight, float moon, bool darkNight)
        {
            if (darkNight) OvercastLook.NightAmbient(sunHeight, moon, ref c.r, ref c.g, ref c.b);
            return c;
        }

        internal static bool SameColor(Color a, Color b) =>
            RainSkyMath.IsOwnWrite(a.r, b.r) && RainSkyMath.IsOwnWrite(a.g, b.g) &&
            RainSkyMath.IsOwnWrite(a.b, b.b) && RainSkyMath.IsOwnWrite(a.a, b.a);
    }
}
