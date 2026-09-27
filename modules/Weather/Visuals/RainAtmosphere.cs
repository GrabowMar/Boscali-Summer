using System.Collections.Generic;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Fx;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Visuals
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

        private bool applied;
        private Slot fog, ambient;
        private ColorSlot fogColor, skyColor, equatorColor, groundColor;

        public string EffectId => "rain-haze";

        public FxBudget Budget => new FxBudget(0, 0, 0, false);

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

        internal void Apply(float rain, bool underwater)
        {
            rain = Mathf.Clamp01(rain);
            if (underwater || rain <= 0.001f) { Restore(); return; }

            fog.Track(RenderSettings.fogDensity, applied);
            ambient.Track(RenderSettings.ambientIntensity, applied);
            fogColor.Track(RenderSettings.fogColor, applied);
            skyColor.Track(RenderSettings.ambientSkyColor, applied);
            equatorColor.Track(RenderSettings.ambientEquatorColor, applied);
            groundColor.Track(RenderSettings.ambientGroundColor, applied);

            // Vanilla storm fog peaks near 0.0014; the underwater preset is 0.05. Never amplify the latter.
            if (fog.Base > 0.02f) { Restore(); return; }

            LastFogMultiplier = RainSkyMath.FogMultiplier(rain);
            float dim = RainSkyMath.AmbientMultiplier(rain);
            // Vanilla's near-opaque storm fog erases the cloud bodies and terrain a pilot
            // must still be able to navigate by. Heavy rain keeps several km of contrast.
            fog.Written = Mathf.Min(fog.Base * LastFogMultiplier, 0.00055f);
            ambient.Written = ambient.Base * dim;
            fogColor.Written = Tint(fogColor.Base, rain);
            skyColor.Written = Dim(skyColor.Base, dim);
            equatorColor.Written = Dim(equatorColor.Base, dim);
            groundColor.Written = Dim(groundColor.Base, dim);

            RenderSettings.fogDensity = fog.Written;
            RenderSettings.ambientIntensity = ambient.Written;
            RenderSettings.fogColor = fogColor.Written;
            RenderSettings.ambientSkyColor = skyColor.Written;
            RenderSettings.ambientEquatorColor = equatorColor.Written;
            RenderSettings.ambientGroundColor = groundColor.Written;
            applied = true;
        }

        /// <summary>Undo only values that are still ours; vanilla rewrites within a second anyway.</summary>
        internal void Restore()
        {
            if (!applied) return;
            applied = false;
            LastFogMultiplier = 1f;
            if (RainSkyMath.IsOwnWrite(fog.Written, RenderSettings.fogDensity)) RenderSettings.fogDensity = fog.Base;
            if (RainSkyMath.IsOwnWrite(ambient.Written, RenderSettings.ambientIntensity)) RenderSettings.ambientIntensity = ambient.Base;
            if (SameColor(fogColor.Written, RenderSettings.fogColor)) RenderSettings.fogColor = fogColor.Base;
            if (SameColor(skyColor.Written, RenderSettings.ambientSkyColor)) RenderSettings.ambientSkyColor = skyColor.Base;
            if (SameColor(equatorColor.Written, RenderSettings.ambientEquatorColor)) RenderSettings.ambientEquatorColor = equatorColor.Base;
            if (SameColor(groundColor.Written, RenderSettings.ambientGroundColor)) RenderSettings.ambientGroundColor = groundColor.Base;
        }

        private static Color Tint(Color c, float rain)
        {
            float r = c.r, g = c.g, b = c.b;
            RainSkyMath.FogTint(rain, ref r, ref g, ref b);
            return new Color(r, g, b, c.a);
        }

        private static Color Dim(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        internal static bool SameColor(Color a, Color b) =>
            RainSkyMath.IsOwnWrite(a.r, b.r) && RainSkyMath.IsOwnWrite(a.g, b.g) &&
            RainSkyMath.IsOwnWrite(a.b, b.b) && RainSkyMath.IsOwnWrite(a.a, b.a);
    }
}
