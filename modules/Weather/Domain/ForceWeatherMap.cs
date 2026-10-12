using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// What the sims' <c>ForceWeather</c> call means on the tape (spec 10.4). The arguments stay the same; each now builds a held, pinned key:
    /// <c>regime</c> (a class name) pins the class's first library sky; <c>conditions</c> names the class when <c>regime</c> is absent; <c>cloudHeight</c> becomes
    /// <c>OpeningBase</c>; <c>seed</c> becomes <c>Seed</c>; <c>modelAge</c> becomes <c>Offset = v x modelAge</c> with <c>Dynamic</c> false. The natives follow the pinned sky by role,
    /// so there are no explicit native targets any more.
    /// </summary>
    internal static class ForceWeatherMap
    {
        public const float MaxAgeSeconds = 14400f;

        /// <summary>The first library sky (index 0-46) of a class: the sky a class name pins.</summary>
        public static byte FirstSky(WeatherRegimeType cls)
        {
            for (int i = 0; i < SkyLibrary.Count; i++)
                if (SkyLibrary.ClassOf(i) == cls) return (byte)i;
            return 0;
        }

        /// <summary>The class a forced call asks for: the named regime, else the class of <paramref name="conditions"/>, else the basis key's mission class, else Fair.</summary>
        public static WeatherRegimeType ClassFor(WeatherRegimeType? regime, float? conditions, WeatherKey basis)
        {
            if (regime.HasValue) return regime.Value;
            if (conditions.HasValue && Scalar.IsFinite(conditions.Value)) return RegimeSnapshot.FromConditions(Scalar.Clamp01(conditions.Value)).Type;
            return basis != null && basis.MissionClass < WeatherKey.ClassCount ? (WeatherRegimeType)basis.MissionClass : WeatherRegimeType.Fair;
        }

        /// <summary>
        /// The held key a forced call asks for, built from the live key so the frame, the layout salt, the set-pieces and the anchor survive.
        /// <paramref name="modelAgeSeconds"/> sets <c>Offset</c> to the system speed times the age; without it <c>Offset</c> is left to the opening placement.
        /// </summary>
        public static WeatherKey Key(WeatherKey basis, float now, WeatherRegimeType? regime, float? conditions, float? cloudHeight, uint? seed, float? modelAgeSeconds)
        {
            if (basis == null) throw new ArgumentNullException(nameof(basis));
            WeatherRegimeType cls = ClassFor(regime, conditions, basis);
            float opening = cloudHeight.HasValue ? WeatherKey.QuantiseOpeningBase(cloudHeight.Value) : basis.OpeningBase;
            float offset = modelAgeSeconds.HasValue ? basis.SystemSpeed * Scalar.Clamp(Scalar.IsFinite(modelAgeSeconds.Value) ? modelAgeSeconds.Value : 0f, 0f, MaxAgeSeconds) : basis.Offset;
            return new WeatherKey(seed ?? basis.Seed, now, false, (byte)cls, basis.Sets, basis.LayoutSalt, basis.HasAnchor, basis.AnchorX, basis.AnchorZ,
                WeatherKey.None, FirstSky(cls), opening, basis.HeadingDeg, offset, basis.SystemSpeed,
                basis.SetOriginSquall, basis.SetOriginSupercell, basis.SetOriginCell);
        }
    }
}
