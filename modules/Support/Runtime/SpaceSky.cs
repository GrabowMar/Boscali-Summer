using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Support's one boundary to the sky. The Weather module publishes <see cref="IWeatherView"/>; Support imports no Weather
    /// implementation. Where that view is absent or not ready (module off, field unbuilt), the game's own mission baseline on
    /// <c>LevelInfo</c> stands in: that is the sky the world actually shows, not an invented clear one. With neither, there is no
    /// sample and the optical bird refuses with words while RADAR stays usable.
    /// </summary>
    internal static class SpaceSky
    {
        private const float NativeDeckDepth = 600f;

        /// <summary>Global metres. False only when no honest sky state exists.</summary>
        public static bool TrySample(GlobalPosition point, out WeatherViewSample sample)
        {
            if (ModuleServices.TryGet(out IWeatherView view) && !(view is Object unity && unity == null) &&
                view.TrySample(point.x, point.z, out sample) && Valid(sample)) return true;
            return TryNative(out sample);
        }

        /// <summary>Native mission weather: LevelInfo conditions (0..1) are the cover and cloudHeight the deck base; no rain channel.</summary>
        public static bool TryNative(out WeatherViewSample sample)
        {
            sample = default;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (level == null || float.IsNaN(level.conditions) || float.IsNaN(level.cloudHeight) || float.IsNaN(level.timeOfDay)) return false;
            sample = new WeatherViewSample(Mathf.Clamp01(level.conditions), level.cloudHeight, level.cloudHeight + NativeDeckDepth, 0f,
                WeatherViewSample.IsNightHour(level.timeOfDay));
            return true;
        }

        private static bool Valid(in WeatherViewSample s) =>
            !float.IsNaN(s.Cover) && !float.IsNaN(s.Rain01) && !float.IsInfinity(s.Cover) && !float.IsInfinity(s.Rain01);
    }
}
