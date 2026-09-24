using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// A single entry in the weather forecast timeline.
    /// </summary>
    internal readonly struct ForecastStep
    {
        public int OffsetMinutes { get; }
        public float Conditions { get; }
        public float CloudDeckMetres { get; }
        public float RainProbability { get; }
        public WeatherRegime Regime { get; }

        public ForecastStep(int offsetMinutes, float conditions, float cloudDeckMetres, float rainProbability)
        {
            OffsetMinutes = offsetMinutes;
            Conditions = Math.Max(0f, Math.Min(1f, conditions));
            CloudDeckMetres = cloudDeckMetres;
            RainProbability = Math.Max(0f, Math.Min(1f, rainProbability));
            Regime = WeatherRegime.FromConditions(Conditions);
        }
    }

    /// <summary>
    /// Solar ephemeris calculation results.
    /// </summary>
    internal readonly struct SolarData
    {
        public float ElevationDegrees { get; }
        public float AzimuthDegrees { get; }
        public float SunriseHour { get; }
        public float SunsetHour { get; }
        public bool PolarDay { get; }
        public bool PolarNight { get; }
        public float TimeToNextEventMinutes { get; }
        public string NextEventName { get; }

        public SolarData(
            float elevation,
            float azimuth,
            float sunriseHour,
            float sunsetHour,
            bool polarDay,
            bool polarNight,
            float timeToNextEvent,
            string nextEventName)
        {
            ElevationDegrees = elevation;
            AzimuthDegrees = azimuth;
            SunriseHour = sunriseHour;
            SunsetHour = sunsetHour;
            PolarDay = polarDay;
            PolarNight = polarNight;
            TimeToNextEventMinutes = timeToNextEvent;
            NextEventName = nextEventName;
        }
    }

    /// <summary>
    /// Lunar ephemeris calculation results.
    /// </summary>
    internal readonly struct LunarData
    {
        public string PhaseName { get; }
        public float IlluminationFraction { get; }
        public float MoonlightIntensity { get; }
        public bool IsMoonless { get; }

        public LunarData(string phaseName, float illumination, float moonlightIntensity, bool isMoonless)
        {
            PhaseName = phaseName;
            IlluminationFraction = Math.Max(0f, Math.Min(1f, illumination));
            MoonlightIntensity = moonlightIntensity;
            IsMoonless = isMoonless;
        }
    }

    /// <summary>
    /// Pure, deterministic ephemeris and forecast calculations.
    /// Engine-agnostic (pure .NET), runnable in any runtime or test suite.
    /// </summary>
    internal static class WeatherForecast
    {
        public const float Rad2Deg = (float)(180.0 / Math.PI);
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public static readonly int[] DefaultOffsetsMinutes = { 0, 5, 10, 15, 30, 60 };

        /// <summary>Conditions index where precipitation begins (heavy overcast).</summary>
        public const float RainThreshold = 0.60f;

        /// <summary>Transition length the base modulation rates are tuned for.</summary>
        public const float ReferenceTransitionSeconds = 120f;

        /// <summary>
        /// Effective rain intensity: an explicit forced value wins, otherwise conditions
        /// ramp from dry at <see cref="RainThreshold"/> to full storm at 0.95.
        /// </summary>
        public static float ResolveRainIntensity(float conditions, float? forcedRain)
        {
            if (forcedRain.HasValue) return Math.Max(0f, Math.Min(1f, forcedRain.Value));
            return RainIntensityFromConditions(conditions);
        }

        public static float RainIntensityFromConditions(float conditions)
        {
            if (conditions < RainThreshold) return 0f;
            return (float)Math.Max(0.0, Math.Min(1.0, (conditions - RainThreshold) / 0.35));
        }

        /// <summary>
        /// Scales the base modulation rates so shorter/longer transitions actually blend
        /// faster/slower. 1.0 at the reference length; identical behaviour to the legacy
        /// fixed rates at the default 2-minute transition.
        /// </summary>
        public static float TransitionRateMultiplier(float transitionDurationSeconds)
        {
            if (transitionDurationSeconds <= 1f) return 1f;
            return ReferenceTransitionSeconds / transitionDurationSeconds;
        }

        /// <summary>
        /// Predict future weather conditions given current condition, mission seed,
        /// and elapsed mission time.
        /// </summary>
        public static ForecastStep SampleForecast(
            float currentConditions,
            float baseCloudHeight,
            int seed,
            float elapsedSeconds,
            int offsetMinutes)
        {
            float futureElapsed = elapsedSeconds + (offsetMinutes * 60f);

            // Deterministic harmonic weather oscillation based on seed:
            // 20-minute, 45-minute, and 90-minute harmonic wave combinations
            double wave1 = Math.Sin((futureElapsed / 1200.0) + (seed * 0.17));
            double wave2 = Math.Cos((futureElapsed / 2700.0) + (seed * 0.31));
            double wave3 = Math.Sin((futureElapsed / 5400.0) + (seed * 0.73));

            double delta = (wave1 * 0.15) + (wave2 * 0.10) + (wave3 * 0.05);
            float futureConditions = (float)Math.Max(0.0, Math.Min(1.0, currentConditions + delta));

            // Cloud deck dips as conditions become stormy
            double deckRatio = 1.0 - Math.Pow(futureConditions, 1.5) * 0.65;
            float futureDeck = (float)Math.Max(800.0, baseCloudHeight * deckRatio);

            // Rain probability ramps sharply above 0.60 conditions
            float rainProb = RainIntensityFromConditions(futureConditions);

            return new ForecastStep(offsetMinutes, futureConditions, futureDeck, rainProb);
        }

        /// <summary>
        /// Compute solar elevation, azimuth, and sunrise/sunset times from sun direction
        /// and celestial rotation axis.
        /// </summary>
        public static SolarData ComputeSolarData(
            float sunDirX, float sunDirY, float sunDirZ,
            float axisX, float axisY, float axisZ,
            float timeOfDayHours)
        {
            // Normalize sun direction (pointing up towards the sun)
            float mag = (float)Math.Sqrt(sunDirX * sunDirX + sunDirY * sunDirY + sunDirZ * sunDirZ);
            if (mag < 0.001f)
            {
                sunDirX = 0f; sunDirY = 1f; sunDirZ = 0f; mag = 1f;
            }
            float sx = sunDirX / mag;
            float sy = sunDirY / mag;
            float sz = sunDirZ / mag;

            // Elevation angle above horizontal horizon (-90 to +90)
            float elevation = (float)Math.Asin(Math.Max(-1.0, Math.Min(1.0, sy))) * Rad2Deg;

            // Azimuth angle clockwise from North (0 to 360)
            float azimuth = (float)Math.Atan2(sx, sz) * Rad2Deg;
            if (azimuth < 0f) azimuth += 360f;

            // Normalize rotation axis
            float aMag = (float)Math.Sqrt(axisX * axisX + axisY * axisY + axisZ * axisZ);
            if (aMag < 0.001f)
            {
                axisX = 0f; axisY = 0f; axisZ = 1f; aMag = 1f;
            }
            float ax = axisX / aMag;
            float ay = axisY / aMag;
            float az = axisZ / aMag;

            float sunrise = -1f;
            float sunset = -1f;
            bool alwaysAbove = true;
            bool alwaysBelow = true;

            // Sample every 10 minutes over a 24-hour day using Rodrigues rotation formula
            const int sampleCount = 144;
            float prevElev = 0f;
            bool hasPrev = false;

            for (int i = 0; i <= sampleCount; i++)
            {
                float tHours = (i * 24f) / sampleCount;
                float hourDelta = tHours - timeOfDayHours;
                float angleRad = hourDelta * 15f * Deg2Rad; // 15 degrees per hour

                float cosA = (float)Math.Cos(angleRad);
                float sinA = (float)Math.Sin(angleRad);

                // Rodrigues rotation: v*cosA + (k x v)*sinA + k*(k . v)*(1 - cosA)
                float dot = ax * sx + ay * sy + az * sz;
                float crossX = ay * sz - az * sy;
                float crossY = az * sx - ax * sz;
                float crossZ = ax * sy - ay * sx;

                float rotY = sy * cosA + crossY * sinA + ay * dot * (1f - cosA);
                float elev = (float)Math.Asin(Math.Max(-1.0, Math.Min(1.0, rotY))) * Rad2Deg;

                if (elev > 0f) alwaysBelow = false;
                if (elev < 0f) alwaysAbove = false;

                if (hasPrev)
                {
                    // Sunrise: crossing from negative to positive
                    if (prevElev <= 0f && elev > 0f && sunrise < 0f)
                    {
                        float frac = -prevElev / (elev - prevElev);
                        sunrise = ((i - 1 + frac) * 24f) / sampleCount;
                    }
                    // Sunset: crossing from positive to negative
                    else if (prevElev >= 0f && elev < 0f && sunset < 0f)
                    {
                        float frac = prevElev / (prevElev - elev);
                        sunset = ((i - 1 + frac) * 24f) / sampleCount;
                    }
                }

                prevElev = elev;
                hasPrev = true;
            }

            bool polarDay = alwaysAbove;
            bool polarNight = alwaysBelow;

            float nextEventMinutes = -1f;
            string nextEvent = "---";

            if (!polarDay && !polarNight && sunrise >= 0f && sunset >= 0f)
            {
                float hoursToSunrise = (sunrise - timeOfDayHours + 24f) % 24f;
                float hoursToSunset = (sunset - timeOfDayHours + 24f) % 24f;

                if (hoursToSunrise < hoursToSunset)
                {
                    nextEvent = "SUNRISE";
                    nextEventMinutes = hoursToSunrise * 60f;
                }
                else
                {
                    nextEvent = "SUNSET";
                    nextEventMinutes = hoursToSunset * 60f;
                }
            }

            return new SolarData(
                elevation,
                azimuth,
                sunrise,
                sunset,
                polarDay,
                polarNight,
                nextEventMinutes,
                nextEvent);
        }

        /// <summary>
        /// Compute moon phase, illumination fraction, and moonlight intensity.
        /// </summary>
        public static LunarData ComputeLunarData(float moonPhase, float baseMoonlight = 0.05f)
        {
            float phase = moonPhase % 1f;
            if (phase < 0f) phase += 1f;

            // Illumination curve: 0 at phase 0 (New), 1.0 at phase 0.5 (Full)
            float illumination = (float)(0.5 * (1.0 - Math.Cos(phase * Math.PI * 2.0)));
            float moonlight = baseMoonlight * illumination;

            string name;
            if (phase < 0.03f || phase > 0.97f) name = "New Moon";
            else if (phase < 0.22f) name = "Waxing Crescent";
            else if (phase < 0.28f) name = "First Quarter";
            else if (phase < 0.47f) name = "Waxing Gibbous";
            else if (phase < 0.53f) name = "Full Moon";
            else if (phase < 0.72f) name = "Waning Gibbous";
            else if (phase < 0.78f) name = "Last Quarter";
            else name = "Waning Crescent";

            bool moonless = illumination < 0.02f;
            return new LunarData(name, illumination, moonlight, moonless);
        }

        /// <summary>
        /// Format wind bearing: returns heading the wind is blowing towards and coming from.
        /// </summary>
        public static void FormatWind(float vx, float vz, out float speedKts, out int towardsDeg, out int fromDeg)
        {
            float speedMs = (float)Math.Sqrt(vx * vx + vz * vz);
            speedKts = speedMs * 1.943844f;

            if (speedMs < 0.1f)
            {
                towardsDeg = 0;
                fromDeg = 0;
                return;
            }

            towardsDeg = (int)Math.Round(Math.Atan2(vx, vz) * Rad2Deg);
            if (towardsDeg < 0) towardsDeg += 360;
            towardsDeg %= 360;

            fromDeg = (towardsDeg + 180) % 360;
        }

        public static string FormatHourTime(float hours)
        {
            if (hours < 0f || hours > 24f) return "--:--";
            int h = (int)Math.Floor(hours) % 24;
            int m = (int)Math.Floor((hours - Math.Floor(hours)) * 60.0) % 60;
            return $"{h:D2}:{m:D2}";
        }

        public static string FormatMinutesDuration(float minutes)
        {
            if (minutes < 0f) return "--";
            int totalM = (int)Math.Round(minutes);
            int h = totalM / 60;
            int m = totalM % 60;
            return h > 0 ? $"{h}H {m:D2}M" : $"{m}M";
        }
    }
}
