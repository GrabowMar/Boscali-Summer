namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>The seven skies the schedule moves between, mildest first.</summary>
    internal enum WeatherRegime : byte
    {
        Clear = 0,
        Fair = 1,
        Showers = 2,
        Overcast = 3,
        Frontal = 4,
        Storms = 5,
        Severe = 6,
    }

    /// <summary>
    /// Everything a regime says about the sky, as plain numbers so two regimes can be blended
    /// field by field. Units: metres, m/s, mm/h, km, hPa; the rest are 0..1 weights.
    /// </summary>
    internal struct RegimeParams
    {
        public float Overcast;
        public float CloudBase;
        public float WindSpeed;
        public float Turbulence;
        public float AreaRain;
        public float Convective;
        public float HazeKm;
        public float Qnh;
        public float Temperature;

        /// <summary>How much of this sky is SEVERE; decides cell strength and hail.</summary>
        public float Severity;

        /// <summary>How strongly a front belongs to this sky: 1 for FRONTAL and SEVERE.</summary>
        public float Frontal;

        public static RegimeParams Blend(RegimeParams a, RegimeParams b, float t)
        {
            return new RegimeParams
            {
                Overcast = WeatherMath.Lerp(a.Overcast, b.Overcast, t),
                CloudBase = WeatherMath.Lerp(a.CloudBase, b.CloudBase, t),
                WindSpeed = WeatherMath.Lerp(a.WindSpeed, b.WindSpeed, t),
                Turbulence = WeatherMath.Lerp(a.Turbulence, b.Turbulence, t),
                AreaRain = WeatherMath.Lerp(a.AreaRain, b.AreaRain, t),
                Convective = WeatherMath.Lerp(a.Convective, b.Convective, t),
                HazeKm = WeatherMath.Lerp(a.HazeKm, b.HazeKm, t),
                Qnh = WeatherMath.Lerp(a.Qnh, b.Qnh, t),
                Temperature = WeatherMath.Lerp(a.Temperature, b.Temperature, t),
                Severity = WeatherMath.Lerp(a.Severity, b.Severity, t),
                Frontal = WeatherMath.Lerp(a.Frontal, b.Frontal, t),
            };
        }
    }

    /// <summary>
    /// The regime table and the chain's transition weights. Both are uncompiled configuration:
    /// every peer derives the same sky from them, so they are constants, never settings.
    /// </summary>
    internal static class RegimeTable
    {
        public const int Count = 7;

        private static readonly RegimeParams[] Table =
        {
            //          overcast base  wind turb  rain  conv  haze  qnh    temp  sev frontal
            Make(0.05f, 2400f, 3f, 0.05f, 0f, 0.00f, 45f, 1022f, 24f, 0f, 0f),
            Make(0.30f, 1500f, 5f, 0.10f, 0f, 0.15f, 30f, 1017f, 23f, 0f, 0f),
            Make(0.50f, 1100f, 7f, 0.20f, 0f, 0.50f, 20f, 1010f, 21f, 0f, 0f),
            Make(0.88f, 650f, 9f, 0.15f, 3f, 0.05f, 8f, 1006f, 16f, 0f, 0f),
            Make(0.75f, 800f, 12f, 0.30f, 1.5f, 0.35f, 12f, 999f, 17f, 0f, 1f),
            Make(0.70f, 1000f, 8f, 0.25f, 0f, 0.85f, 15f, 1004f, 22f, 0f, 0f),
            Make(0.85f, 900f, 14f, 0.40f, 2f, 1.00f, 10f, 995f, 19f, 1f, 1f),
        };

        private static readonly string[] Names =
        {
            "CLEAR", "FAIR", "SHOWERS", "OVERCAST", "FRONTAL", "STORMS", "SEVERE",
        };

        /// <summary>
        /// Row = current regime, column = next. Neighbours dominate so the sky builds and clears
        /// the way a real one does: CLEAR → FAIR → SHOWERS → STORMS, FAIR → FRONTAL → OVERCAST.
        /// A zero forbids a jump (CLEAR never goes straight to SEVERE).
        /// </summary>
        private static readonly float[,] Transitions =
        {
            //  CLR   FAIR  SHWR  OVC   FRNT  STRM  SEV
            { 0.00f, 0.60f, 0.20f, 0.05f, 0.15f, 0.00f, 0.00f }, // CLEAR
            { 0.25f, 0.00f, 0.35f, 0.10f, 0.20f, 0.10f, 0.00f }, // FAIR
            { 0.05f, 0.25f, 0.00f, 0.10f, 0.15f, 0.35f, 0.10f }, // SHOWERS
            { 0.10f, 0.25f, 0.30f, 0.00f, 0.25f, 0.10f, 0.00f }, // OVERCAST
            { 0.10f, 0.15f, 0.20f, 0.30f, 0.00f, 0.15f, 0.10f }, // FRONTAL
            { 0.05f, 0.15f, 0.30f, 0.15f, 0.10f, 0.00f, 0.25f }, // STORMS
            { 0.00f, 0.10f, 0.30f, 0.20f, 0.20f, 0.20f, 0.00f }, // SEVERE
        };

        public static RegimeParams Get(WeatherRegime regime) => Table[Index(regime)];

        public static string Name(WeatherRegime regime) => Names[Index(regime)];

        public static WeatherRegime Clamp(int value)
            => (WeatherRegime)(value < 0 ? 0 : value >= Count ? Count - 1 : value);

        /// <summary>Picks the next regime from a uniform roll in [0, 1).</summary>
        public static WeatherRegime Next(WeatherRegime current, float roll)
        {
            int row = Index(current);
            float total = 0f;
            for (int i = 0; i < Count; i++) total += Transitions[row, i];
            float target = roll * total;
            float sum = 0f;
            for (int i = 0; i < Count; i++)
            {
                sum += Transitions[row, i];
                if (Transitions[row, i] > 0f && target < sum) return (WeatherRegime)i;
            }
            for (int i = Count - 1; i >= 0; i--)
            {
                if (Transitions[row, i] > 0f) return (WeatherRegime)i;
            }
            return current;
        }

        /// <summary>The weight of moving from one regime to another; zero means never.</summary>
        public static float TransitionWeight(WeatherRegime from, WeatherRegime to) => Transitions[Index(from), Index(to)];

        /// <summary>
        /// Reads a vanilla <c>conditions</c> value back into a regime, using vanilla's own bands
        /// (its five weather sets are <c>floor(conditions × 5)</c>: clear, scattered, moderate,
        /// overcast, thunderstorm). This is how an authored mission beat enters the schedule.
        /// </summary>
        public static WeatherRegime FromConditions(float conditions)
        {
            if (conditions < 0.2f) return WeatherRegime.Clear;
            if (conditions < 0.4f) return WeatherRegime.Fair;
            if (conditions < 0.6f) return WeatherRegime.Showers;
            if (conditions < 0.8f) return WeatherRegime.Overcast;
            if (conditions < 0.93f) return WeatherRegime.Storms;
            return WeatherRegime.Severe;
        }

        private static int Index(WeatherRegime regime)
        {
            int i = (int)regime;
            return i < 0 ? 0 : i >= Count ? Count - 1 : i;
        }

        private static RegimeParams Make(
            float overcast, float cloudBase, float wind, float turbulence, float rain, float convective,
            float haze, float qnh, float temperature, float severity, float frontal)
        {
            return new RegimeParams
            {
                Overcast = overcast,
                CloudBase = cloudBase,
                WindSpeed = wind,
                Turbulence = turbulence,
                AreaRain = rain,
                Convective = convective,
                HazeKm = haze,
                Qnh = qnh,
                Temperature = temperature,
                Severity = severity,
                Frontal = frontal,
            };
        }
    }
}
