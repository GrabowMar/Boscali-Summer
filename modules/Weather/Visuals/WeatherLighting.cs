using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Visuals
{
    internal readonly struct WeatherLight
    {
        internal readonly Vector3 Direction;
        internal readonly Color Colour;
        internal readonly bool IsMoon;
        internal WeatherLight(Vector3 direction, Color colour, bool moon)
        { Direction = direction; Colour = colour; IsMoon = moon; }
    }

    /// <summary>Native celestial lighting, with cloud illumination independent of ground occlusion.</summary>
    internal static class WeatherLighting
    {
        internal static WeatherLight Resolve(LevelInfo level)
        {
            return Resolve(level, Source(level));
        }
        internal static WeatherLight ResolveCloud(LevelInfo level)
        {
            // LevelInfo disables the native sun object when the observer is under an
            // opaque deck. Its time-of-day colour remains valid for the cloud tops.
            // Read that light without enabling it or changing native ground lighting.
            Light sun = level != null ? level.sun : null;
            bool day = sun != null && sun.intensity > 0f && sun.color.maxColorComponent > 0f &&
                -sun.transform.forward.y > 0f;
            return Resolve(level, day ? sun : Source(level));
        }
        private static WeatherLight Resolve(LevelInfo level, Light source)
        {
            if (source == null) return new WeatherLight(Vector3.up, Color.black, false);
            Color colour = source.color * source.intensity;
            float peak = Mathf.Max(colour.r, Mathf.Max(colour.g, colour.b));
            if (peak > 2f) colour *= 2f / peak;
            return new WeatherLight(-source.transform.forward, colour, source == level.moon);
        }
        internal static Light Source(LevelInfo level) =>
            Visible(level != null ? level.sun : null) ? level.sun :
            Visible(level != null ? level.moon : null) ? level.moon : null;
        private static bool Visible(Light light) => light != null && light.isActiveAndEnabled && light.intensity > 0f;
    }
}
