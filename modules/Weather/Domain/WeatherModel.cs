using System;
using BoscaliSummer.Core;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// The deterministic weather schedule. Every peer derives the same sky from the mission
    /// identity and the mission clock alone, so the forecast needs no wire format of its own:
    /// the vanilla <c>LevelInfo</c> sync vars carry the authoritative values and this model
    /// supplies the shape around them.
    ///
    /// The model is a composition, not a table. A synoptic pressure cycle decides which two air
    /// masses are in contact; the contrast between them decides the front, its kind, its speed
    /// and how hard it lifts; the diurnal cycle decides how much of that energy is released; and
    /// the thermodynamics turn all of it into the five channels vanilla can carry.
    ///
    /// The global scalars depend only on (seed, mission time, haze, daylight) — never on the map
    /// size. Map size only places the boundary spatially, and the front's position and width
    /// both scale with it so the atmosphere cannot differ between two peers on different maps.
    /// </summary>
    internal static class WeatherModel
    {
        /// <summary>
        /// One weather front. Five minutes: long enough to plan a sortie inside one sky, short
        /// enough that a player who flies for half an hour crosses several and sees the weather
        /// change. The first draft of this used twenty-five minutes and nothing visible happened
        /// inside a session — do not lengthen it again without that in mind.
        /// </summary>
        public const float FrontSeconds = 300f;

        /// <summary>How much of the tail of a front is spent cross-fading into the next one.</summary>
        public const float FrontBlendSeconds = 90f;

        /// <summary>Fronts that share one day character. Three fronts is fifteen minutes.</summary>
        public const int FrontsPerPhase = 3;

        /// <summary>Lowest cloud base the model will ask vanilla for.</summary>
        public const float MinCloudBase = 450f;

        public const float MaxCloudBase = 3400f;

        public const int MaxFrontIndex = 1_000_000;

        /// <summary>
        /// The map size the front's physical width is quoted at. Both the boundary's position
        /// and its width scale with the real map size, so the ratio — and with it the atmosphere
        /// the reader derives — is the same on every map.
        /// </summary>
        private const float ReferenceMapSize = 20000f;

        /// <summary>Map size used when a caller has no level to read one from.</summary>
        private const float NominalMapSize = ReferenceMapSize;

        /// <summary>Fraction of the synoptic cycle the boundary takes to cross, by kind.</summary>
        private const float ColdCrossFraction = 0.45f;

        private const float WarmCrossFraction = 0.75f;

        private const float OtherCrossFraction = 0.60f;

        /// <summary>Below this air-mass contrast there is no boundary worth drawing.</summary>
        private const float MinContrast = 0.06f;

        /// <summary>
        /// Convective inhibition at which convection is fully suppressed. Above this the lid is
        /// shut and no amount of CAPE reaches the sky.
        /// </summary>
        private const float CinLid = 0.35f;

        public static int Seed(string missionIdentity)
        {
            if (string.IsNullOrEmpty(missionIdentity)) return 0x5EED;
            uint hash = 2166136261u;
            for (int i = 0; i < missionIdentity.Length; i++)
                hash = (hash ^ missionIdentity[i]) * 16777619u;
            return (int)hash;
        }

        public static int FrontIndex(float missionTime)
        {
            if (float.IsNaN(missionTime) || missionTime <= 0f) return 0;
            int index = (int)(missionTime / FrontSeconds);
            return index > MaxFrontIndex ? MaxFrontIndex : index;
        }

        public static float FrontStart(int front) => front * FrontSeconds;

        public static float PhaseIndex(int front) => front / FrontsPerPhase;

        /// <summary>How far into the cross-fade from <paramref name="front"/> to the next front we are.</summary>
        public static float BlendWeight(float missionTime)
        {
            float local = missionTime - FrontIndex(missionTime) * FrontSeconds;
            float t = WeatherRegimes.Clamp01((local - (FrontSeconds - FrontBlendSeconds)) / FrontBlendSeconds);
            return t * t * (3f - 2f * t);
        }

        /// <summary>The sky the model wants at a mission time, before any haze term.</summary>
        public static WeatherState Sample(int seed, float missionTime) =>
            Sample(seed, missionTime, Diurnal.UnknownDaylight);

        /// <summary>
        /// The sky the model wants at a mission time, given the caller's daylight. The live
        /// manager uses this so the panel's forecast and the driven sky are the same function of
        /// the same inputs; a pure caller passes <see cref="Diurnal.UnknownDaylight"/>.
        /// </summary>
        public static WeatherState Sample(int seed, float missionTime, float daylight)
        {
            int front = FrontIndex(missionTime);
            WeatherState current = Settled(seed, FrontStart(front), daylight);
            float weight = BlendWeight(missionTime);
            if (weight <= 0f) return current;
            return WeatherState.Blend(current, Settled(seed, FrontStart(front + 1), daylight), weight);
        }

        /// <summary>The settled sky of one front, ignoring the cross-fade into its successor.</summary>
        public static WeatherState Front(int seed, int front) =>
            Settled(seed, FrontStart(front) + FrontSeconds * 0.5f, Diurnal.UnknownDaylight);

        /// <summary>
        /// The full atmosphere at a mission time: what the panel, the clouds, the radar and the
        /// canopy rain all read. Depends on the mission clock, never on the map.
        /// </summary>
        public static Atmosphere SampleAtmosphere(int seed, float missionTime, float mapSize, float haze, float daylight)
        {
            WeatherFront front = SampleFront(seed, missionTime, mapSize);
            AirMassKind dominant = SynopticCycle.Sample(seed, missionTime, out AirMassKind behind, out AirMassKind ahead);
            if (behind == ahead) dominant = behind;

            AirMass mass = AirMasses.Get(dominant);
            float frontal = front.Present ? front.InfluenceAt(0f, 0f) : 0f;
            float forcing = front.Present
                ? frontal * (FrontKinds.IsConvective(front.Kind) ? 1f : 0.55f)
                : 0f;

            float heating = Diurnal.Heating(missionTime, daylight);
            float smoke = WeatherRegimes.Clamp01(haze);

            // Heating lifts the surface; a cold passage drops it hard and takes the moisture with it.
            float temperature = mass.TemperatureC + 9f * (heating - 0.35f) -
                                (front.Present && front.Kind == FrontKind.Cold ? 5f * frontal : 0f);
            float dewpoint = mass.DewpointC;

            // Mesoscale variability: a real sky is not a smooth curve, and without this a single
            // front inside a three-hour pressure cycle barely moves. Each five-minute front gets
            // its own bounded character, seeded from the front index so every peer — and the
            // panel's forecast — sees exactly the same wobble.
            int frontIndex = FrontIndex(missionTime);
            float thermalWobble = Deterministic.UnitFloat(Deterministic.Hash(seed, frontIndex, 0xC3)) * 2f - 1f;
            float moistureWobble = Deterministic.UnitFloat(Deterministic.Hash(seed, frontIndex, 0xD7)) * 2f - 1f;
            temperature += thermalWobble * 1.5f;
            dewpoint += moistureWobble * 1.2f;

            float cape = Thermo.Cape(temperature, dewpoint, mass.Stability, heating);
            float cin = Thermo.Cin(mass.Stability, heating, forcing);
            float shear = Thermo.Shear(mass.WindSpeed, frontal, cape);
            float gust = Thermo.GustSpeed(mass.WindSpeed, cape);
            float lcl = Thermo.Lcl(temperature, dewpoint);
            float rain = Thermo.RainRate(cape, cin, frontal, 0f);
            float visibility = Thermo.Visibility(rain, temperature - dewpoint, smoke);
            float pressure = SynopticCycle.Pressure(seed, missionTime);
            float boundaryLayer = Diurnal.BoundaryLayer(heating, mass.Stability);

            return new Atmosphere(
                true,
                dominant,
                front.Present ? front.Kind : FrontKind.None,
                temperature,
                dewpoint,
                lcl,
                cape,
                cin,
                shear,
                gust,
                rain,
                visibility,
                boundaryLayer,
                pressure,
                heating);
        }

        /// <summary>
        /// The frontal boundary crossing the map, or <see cref="WeatherFront.None"/> when there
        /// is no boundary in play or it has already left. Both its position and its width scale
        /// with the map so the reader's experience of the passage does not depend on map size.
        /// </summary>
        public static WeatherFront SampleFront(int seed, float missionTime, float mapSize)
        {
            if (float.IsNaN(mapSize) || mapSize <= 1000f) mapSize = NominalMapSize;

            SynopticCycle.Sample(seed, missionTime, out AirMassKind behind, out AirMassKind ahead);
            if (behind == ahead) return WeatherFront.None;

            float contrast = AirMasses.Contrast(behind, ahead);
            if (contrast <= MinContrast) return WeatherFront.None;

            int cycle = SynopticCycle.CycleIndex(missionTime);
            float pressure = SynopticCycle.Pressure(seed, missionTime);
            FrontKind kind = KindOf(seed, cycle, behind, ahead, contrast);

            float cross = kind == FrontKind.Cold ? ColdCrossFraction
                : kind == FrontKind.Warm ? WarmCrossFraction
                : OtherCrossFraction;
            float half = cross * 0.5f;
            float phase = SynopticCycle.Phase01(missionTime);
            float u = (phase - (0.5f - half)) / cross;
            if (u < 0f || u > 1f) return WeatherFront.None;

            float scale = mapSize / ReferenceMapSize;
            float span = mapSize * 1.5f;
            float position = -span + u * 2f * span;
            float speed = 2f * span / (cross * SynopticCycle.PeriodSeconds);

            float bearing = Deterministic.UnitFloat(Deterministic.Hash(seed, cycle, 0x91)) * 360f;
            float radians = bearing * (float)(Math.PI / 180.0);
            float normalX = (float)Math.Sin(radians);
            float normalZ = (float)Math.Cos(radians);

            float activity = WeatherRegimes.Clamp01(contrast * (0.65f + 0.55f * (1f - pressure)));

            return new WeatherFront(
                true,
                kind,
                normalX,
                normalZ,
                position,
                speed,
                FrontKinds.Width(kind) * scale,
                activity,
                behind,
                ahead);
        }

        /// <summary>
        /// Squeeze an atmosphere onto the five channels vanilla can actually carry. Gusts ride
        /// in turbulence because there is no gust channel, and the heading veers across the
        /// front's band so a passage is felt as a wind shift and not just a number change.
        /// </summary>
        public static WeatherState ToState(in Atmosphere atmosphere, in WeatherFront front, float x, float z)
        {
            if (!atmosphere.Available) return default;

            AirMass mass = AirMasses.Get(atmosphere.AirMass);
            float frontal = front.Present ? front.InfluenceAt(x, z) : 0f;

            // Cloud needs moisture AND lift, and the two come from different places: a boundary
            // lifting air, and a low subsiding less. They combine rather than competing, because
            // a front inside a deep low is the worst of both. Without a lift term the sky can
            // never clear, because no air mass here is dry enough on its own.
            float subsidence = WeatherRegimes.Clamp01((0.62f - atmosphere.Pressure) / 0.62f);
            float lift = 1f - (1f - frontal) * (1f - subsidence);
            float moistureFactor = 0.35f + 0.65f * atmosphere.RelativeHumidity;
            float humidCover = lift * moistureFactor;

            // Deep convection covers the sky with its own anvil whether or not the surface air
            // is near saturation, which is why an afternoon storm can build from a clear morning.
            // CIN is a lid rather than a damper: above it convection simply does not start, and
            // treating it as a multiplier let a capped ridge still grow cloud.
            float lid = WeatherRegimes.Clamp01((CinLid - atmosphere.Cin) / CinLid);
            float convectiveCover = atmosphere.Cape * lid;
            float cover = humidCover > convectiveCover ? humidCover : convectiveCover;

            float conditions = WeatherRegimes.Clamp01(
                0.02f +
                0.95f * cover +
                0.70f * atmosphere.RainRate);

            // Pressure gradient is what drives wind: a deep low is a windy sky.
            float gradient = 0.75f + 0.85f * (1f - atmosphere.Pressure);
            float windSpeed = mass.WindSpeed * gradient + 6f * frontal;
            if (float.IsNaN(windSpeed) || windSpeed < 0f) windSpeed = 0f;

            float turbulence = WeatherRegimes.Clamp01(
                atmosphere.Shear * 0.55f + atmosphere.Cape * 0.30f + frontal * 0.25f);

            float heading = mass.WindHeading;
            if (front.Present)
            {
                float across = WeatherRegimes.Clamp01(0.5f + front.SignedDistanceTo(x, z) / (2f * front.Width));
                heading += VeerDegrees(front.Kind) * (across - 0.5f) * 2f;
            }

            return new WeatherState(
                WeatherRegimes.FromConditions(conditions),
                conditions,
                ClampCloudBase(atmosphere.Lcl),
                windSpeed,
                WeatherState.WrapHeading(heading),
                turbulence);
        }

        /// <summary>Mission time at which the next front's cross-fade begins.</summary>
        public static float NextChangeAt(float missionTime)
        {
            int front = FrontIndex(missionTime);
            float changeAt = FrontStart(front + 1) - FrontBlendSeconds;
            return changeAt > missionTime ? changeAt : FrontStart(front + 2) - FrontBlendSeconds;
        }

        public static WeatherRegime NextRegime(int seed, float missionTime)
        {
            int front = FrontIndex(missionTime);
            if (FrontStart(front + 1) - FrontBlendSeconds <= missionTime) front += 1;
            return Front(seed, front + 1).Regime;
        }

        public static float ClampCloudBase(float value)
        {
            if (float.IsNaN(value)) return 1800f;
            if (value < MinCloudBase) return MinCloudBase;
            return value > MaxCloudBase ? MaxCloudBase : value;
        }

        /// <summary>How far the heading swings across a passage, signed by kind.</summary>
        private static float VeerDegrees(FrontKind kind)
        {
            switch (kind)
            {
                case FrontKind.Cold: return 50f;
                case FrontKind.Warm: return -35f;
                case FrontKind.Occluded: return 60f;
                case FrontKind.DryLine: return 30f;
                default: return 0f;
            }
        }

        /// <summary>
        /// Which kind of boundary the contrast produces. Drawn once per cycle rather than
        /// derived, because "a cold front overtook a warm one" is history this model does not
        /// keep — but the choice is seeded so every peer draws the same one.
        /// </summary>
        private static FrontKind KindOf(int seed, int cycle, AirMassKind behind, AirMassKind ahead, float contrast)
        {
            AirMass trailing = AirMasses.Get(behind);
            AirMass leading = AirMasses.Get(ahead);
            float thermal = trailing.TemperatureC - leading.TemperatureC;
            float moisture = trailing.DewpointC - leading.DewpointC;

            if (Math.Abs(thermal) < 3f && Math.Abs(moisture) >= 6f) return FrontKind.DryLine;
            if (contrast >= 0.62f && Deterministic.UnitFloat(Deterministic.Hash(seed, cycle, 0xA7)) < 0.28f)
                return FrontKind.Occluded;
            return thermal < 0f ? FrontKind.Cold : FrontKind.Warm;
        }

        /// <summary>
        /// The map-independent global sky at an instant. The nominal map size only places the
        /// boundary; it cannot change the scalars, which is what keeps the forecast the panel
        /// draws identical to the sky the host drives.
        ///
        /// <para>Daylight is left to the mission clock here. The live manager passes the level's
        /// synced time of day instead, because a pure function has no level to read; both are
        /// position-independent, which is the property that matters.</para>
        /// </summary>
        private static WeatherState Settled(int seed, float missionTime, float daylight)
        {
            Atmosphere atmosphere = SampleAtmosphere(seed, missionTime, NominalMapSize, 0f, daylight);
            return ToState(atmosphere, SampleFront(seed, missionTime, NominalMapSize), 0f, 0f);
        }
    }
}
