using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// How the sky actually is, in physical terms, before anything is squeezed into the five
    /// channels vanilla can carry. <see cref="WeatherState"/> remains the output surface — the
    /// values that reach <c>LevelInfo</c> — and this is the weather behind them: what the
    /// panel, the radar, the clouds and the canopy rain all read.
    ///
    /// Pure, allocation-free and derived rather than simulated, so every peer arrives at the
    /// same atmosphere from the mission clock with nothing on the wire.
    /// </summary>
    internal readonly struct Atmosphere
    {
        public readonly bool Available;

        /// <summary>The air mass in control at the reader's position.</summary>
        public readonly AirMassKind AirMass;

        /// <summary>Nearest boundary's kind, or <see cref="FrontKind.None"/> in a uniform air mass.</summary>
        public readonly FrontKind Front;

        public readonly float TemperatureC;

        public readonly float DewpointC;

        /// <summary>Lifting condensation level: the cloud base, metres above the surface.</summary>
        public readonly float Lcl;

        /// <summary>Convective available potential energy, normalised 0..1.</summary>
        public readonly float Cape;

        /// <summary>Convective inhibition, normalised 0..1. High CIN means the energy is capped.</summary>
        public readonly float Cin;

        /// <summary>0..1 vertical wind shear. Sets whether storms organise into lines or stay cells.</summary>
        public readonly float Shear;

        /// <summary>Gust peak, metres per second. Rides inside the turbulence channel.</summary>
        public readonly float GustSpeed;

        /// <summary>0..1 precipitation rate at the reader.</summary>
        public readonly float RainRate;

        /// <summary>Slant visibility, metres.</summary>
        public readonly float Visibility;

        /// <summary>Depth of the mixed layer, metres. Deep means turbulence and good visibility.</summary>
        public readonly float BoundaryLayer;

        /// <summary>Synoptic pressure anomaly, 0 = deep low, 1 = strong high.</summary>
        public readonly float Pressure;

        /// <summary>0..1 diurnal surface heating.</summary>
        public readonly float Heating;

        public Atmosphere(
            bool available,
            AirMassKind airMass,
            FrontKind front,
            float temperatureC,
            float dewpointC,
            float lcl,
            float cape,
            float cin,
            float shear,
            float gustSpeed,
            float rainRate,
            float visibility,
            float boundaryLayer,
            float pressure,
            float heating)
        {
            Available = available;
            AirMass = airMass;
            Front = front;
            TemperatureC = temperatureC;
            DewpointC = Math.Min(dewpointC, temperatureC);
            Lcl = lcl;
            Cape = WeatherRegimes.Clamp01(cape);
            Cin = WeatherRegimes.Clamp01(cin);
            Shear = WeatherRegimes.Clamp01(shear);
            GustSpeed = gustSpeed < 0f ? 0f : gustSpeed;
            RainRate = WeatherRegimes.Clamp01(rainRate);
            Visibility = visibility < 0f ? 0f : visibility;
            BoundaryLayer = boundaryLayer < 0f ? 0f : boundaryLayer;
            Pressure = WeatherRegimes.Clamp01(pressure);
            Heating = WeatherRegimes.Clamp01(heating);
        }

        public static Atmosphere Unavailable => default;

        /// <summary>Relative humidity, 0..1, from the Magnus approximation.</summary>
        public float RelativeHumidity => Thermo.RelativeHumidity(TemperatureC, DewpointC);

        /// <summary>Fog and heavy rain are what actually stop a pilot, so visibility leads here.</summary>
        public FlightCategory Category => Atmospheres.Category(this);

        /// <summary>Convection is deep, free and sheared enough to organise.</summary>
        public bool IsSevereConvection => Cape >= 0.55f && Cin <= 0.45f;

        public PrecipitationKind Precipitation => Atmospheres.Precipitation(this);
    }

    /// <summary>
    /// The standard ceiling-and-visibility classification. Worth having because it is the one
    /// number that tells a pilot "can I fly this" without reading two others.
    /// </summary>
    internal enum FlightCategory
    {
        Vfr = 0,
        Mvfr = 1,
        Ifr = 2,
        Lifr = 3
    }

    /// <summary>What is actually falling. The canopy rain reads this; drizzle and hail look nothing alike.</summary>
    internal enum PrecipitationKind
    {
        None = 0,
        Drizzle = 1,
        Rain = 2,
        Showers = 3,
        Hail = 4
    }

    internal static class Atmospheres
    {
        private static readonly string[] PrecipitationLabels = { "DRY", "DRIZZLE", "RAIN", "SHOWERS", "HAIL" };

        public static string Label(PrecipitationKind kind)
        {
            int index = (int)kind;
            return PrecipitationLabels[index < 0 ? 0 : index >= PrecipitationLabels.Length ? 0 : index];
        }

        /// <summary>
        /// Derived rather than stored, so there is one source of truth for what is falling and no
        /// field can disagree with the rate. Layered warm-front rain, convective showers and hail
        /// are three different skies and want three different canopy effects.
        /// </summary>
        public static PrecipitationKind Precipitation(in Atmosphere atmosphere)
        {
            if (atmosphere.RainRate < 0.08f) return PrecipitationKind.None;
            if (atmosphere.RainRate < 0.25f && atmosphere.Cape < 0.30f) return PrecipitationKind.Drizzle;
            if (atmosphere.Cape >= 0.60f && atmosphere.TemperatureC <= 2f) return PrecipitationKind.Hail;
            if (atmosphere.Cape >= 0.50f) return PrecipitationKind.Showers;
            if (atmosphere.RainRate < 0.35f && atmosphere.Cape < 0.40f) return PrecipitationKind.Drizzle;
            return PrecipitationKind.Rain;
        }

        /// <summary>Ceiling, metres. Vanilla cloud base is the ceiling in every way that matters.</summary>
        public static FlightCategory Category(in Atmosphere atmosphere)
        {
            float ceiling = atmosphere.Lcl;
            float visibility = atmosphere.Visibility;

            if (ceiling < 150f || visibility < 1600f) return FlightCategory.Lifr;
            if (ceiling < 300f || visibility < 5000f) return FlightCategory.Ifr;
            if (ceiling < 900f || visibility < 8000f) return FlightCategory.Mvfr;
            return FlightCategory.Vfr;
        }

        public static string Label(FlightCategory category)
        {
            switch (category)
            {
                case FlightCategory.Lifr: return "LIFR";
                case FlightCategory.Ifr: return "IFR";
                case FlightCategory.Mvfr: return "MVFR";
                default: return "VFR";
            }
        }

        /// <summary>0 = best, 3 = worst. For colour ramps and severity comparisons.</summary>
        public static int Rank(FlightCategory category)
        {
            int rank = (int)category;
            return rank < 0 ? 0 : rank > 3 ? 3 : rank;
        }
    }
}
