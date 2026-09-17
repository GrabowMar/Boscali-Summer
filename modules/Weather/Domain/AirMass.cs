using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// An air mass: the thing that actually decides what the sky does. A front is a boundary
    /// between two of these, so front behaviour falls out of the physics instead of being
    /// scripted per regime.
    /// </summary>
    internal enum AirMassKind
    {
        MaritimeTropical = 0,
        MaritimePolar = 1,
        ContinentalTropical = 2,
        ContinentalPolar = 3,
        ContinentalArctic = 4
    }

    /// <summary>
    /// The representative surface properties of one air mass. Values are typical rather than
    /// exact — this is a game sky, and what matters is that cold-and-dry behaves like
    /// cold-and-dry and warm-and-wet behaves like warm-and-wet.
    /// </summary>
    internal readonly struct AirMass
    {
        /// <summary>Surface temperature, degrees Celsius.</summary>
        public readonly float TemperatureC;

        /// <summary>Surface dewpoint, degrees Celsius. Never above <see cref="TemperatureC"/>.</summary>
        public readonly float DewpointC;

        /// <summary>Typical wind speed, metres per second.</summary>
        public readonly float WindSpeed;

        /// <summary>Direction the wind blows toward, degrees.</summary>
        public readonly float WindHeading;

        /// <summary>0 = strongly stable and capped, 1 = strongly unstable and free-convecting.</summary>
        public readonly float Stability;

        public AirMass(
            AirMassKind kind,
            float temperatureC,
            float dewpointC,
            float windSpeed,
            float windHeading,
            float stability,
            float moisture)
        {
            TemperatureC = temperatureC;
            DewpointC = Math.Min(dewpointC, temperatureC);
            WindSpeed = windSpeed;
            WindHeading = WeatherState.WrapHeading(windHeading);
            Stability = WeatherRegimes.Clamp01(stability);
        }
    }

    internal static class AirMasses
    {
        public const int Count = 5;

        private static readonly string[] Names =
        {
            "MARITIME TROPICAL", "MARITIME POLAR", "CONTINENTAL TROPICAL",
            "CONTINENTAL POLAR", "CONTINENTAL ARCTIC"
        };

        private static readonly AirMass[] Table =
        {
            //                kind                          T     Td    wind  hdg  stab  moist
            new AirMass(AirMassKind.MaritimeTropical,    28f,  23f,   6f,  210f, 0.95f, 0.95f),
            new AirMass(AirMassKind.MaritimePolar,       12f,   6f,   9f,  250f, 0.60f, 0.70f),
            new AirMass(AirMassKind.ContinentalTropical, 34f,   8f,   5f,  180f, 0.90f, 0.25f),
            new AirMass(AirMassKind.ContinentalPolar,     4f,  -5f,   7f,  320f, 0.45f, 0.45f),
            new AirMass(AirMassKind.ContinentalArctic,  -12f, -25f,  10f,   20f, 0.15f, 0.15f)
        };

        public static int Index(AirMassKind kind)
        {
            int index = (int)kind;
            return index < 0 ? 0 : index >= Count ? Count - 1 : index;
        }

        public static AirMass Get(AirMassKind kind) => Table[Index(kind)];

        public static string Name(AirMassKind kind) => Names[Index(kind)];

        /// <summary>Air that will convect on its own given a little surface heating.</summary>
        public static bool IsUnstable(AirMassKind kind) => Get(kind).Stability >= 0.5f;

        /// <summary>
        /// How much energy a boundary between these two masses carries: a cold mass pushing under
        /// a warm moist one is the violent case, and a warm mass riding over a cold one is the
        /// murky-but-quiet case. Normalised so a real frontal difference saturates the scale
        /// rather than scoring a third of it, which is what made every front feel the same.
        /// </summary>
        public static float Contrast(AirMassKind behind, AirMassKind ahead)
        {
            AirMass b = Get(behind);
            AirMass a = Get(ahead);
            float thermal = Math.Abs(b.TemperatureC - a.TemperatureC) / 22f;
            float moisture = Math.Abs(b.DewpointC - a.DewpointC) / 22f;
            return WeatherRegimes.Clamp01(thermal * 0.6f + moisture * 0.4f);
        }
    }
}
