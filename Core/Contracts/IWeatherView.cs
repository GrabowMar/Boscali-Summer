namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// What a sky-dependent sensor needs from the weather at one point: total cloud cover (0 clear, 1 overcast),
    /// the local deck bounds in metres, rain intensity 0..1 and whether it is night. A value, not a handle.
    /// </summary>
    internal readonly struct WeatherViewSample
    {
        public readonly float Cover, CloudBase, CloudTop, Rain01;
        public readonly bool Night;

        public WeatherViewSample(float cover, float cloudBase, float cloudTop, float rain01, bool night)
        {
            Cover = cover; CloudBase = cloudBase; CloudTop = cloudTop; Rain01 = rain01; Night = night;
        }

        /// <summary>
        /// The game's own daylight factor (<c>LevelInfo.GetDaylightFactor</c>) falls to zero at 18.5 h and rises from 5.5 h:
        /// outside them it is night. One rule for the weather module and for Support's native fallback.
        /// </summary>
        public static bool IsNightHour(float timeOfDayHours) => timeOfDayHours >= 18.5f || timeOfDayHours <= 5.5f;
    }

    /// <summary>
    /// Read-only weather at a global point, published by the Weather module through <c>ModuleContext.AddService</c> and
    /// read late through <c>ModuleServices</c>. False means the field is not ready or the module is off: a caller must
    /// never invent a clear sky from it. Coordinates are global metres.
    /// </summary>
    internal interface IWeatherView
    {
        bool TrySample(float globalX, float globalZ, out WeatherViewSample sample);
    }
}
