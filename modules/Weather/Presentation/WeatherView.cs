using System;
using BoscaliSummer.Core;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Features.Weather.Runtime;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Presentation
{
    // ----------------------------------------------------------------------------------------
    // The WEA page, the weather line and the debug overlay are ported from the previous
    // weather module; this file is the read model that feeds them from the new field. Every
    // number below is derived, read-only and per-capture: nothing here is simulation state,
    // settings or wire data.
    //
    // Names deliberately match the presentation vocabulary the ported files already speak
    // ("WeatherState", "StormCell", "WeatherFront"). Where the new domain has a type of the
    // same name it is aliased to DomainXxx, and the view type wins inside this namespace.
    // ----------------------------------------------------------------------------------------

    /// <summary>One complete sky a display can read, in the units the page prints.</summary>
    internal readonly struct WeatherState
    {
        public readonly WeatherRegime Regime;
        public readonly float Conditions;
        public readonly float CloudBase;
        public readonly float WindSpeed;
        public readonly float WindHeading;
        public readonly float Turbulence;

        public WeatherState(WeatherRegime regime, float conditions, float cloudBase,
            float windSpeed, float windHeading, float turbulence)
        {
            Regime = regime;
            Conditions = conditions;
            CloudBase = cloudBase;
            WindSpeed = windSpeed;
            WindHeading = WrapHeading(windHeading);
            Turbulence = turbulence;
        }

        public bool IsSevere => WeatherRegimes.IsSevere(Regime);

        public static float WrapHeading(float heading)
        {
            if (float.IsNaN(heading) || float.IsInfinity(heading)) return 0f;
            float wrapped = heading % 360f;
            return wrapped < 0f ? wrapped + 360f : wrapped;
        }
    }

    /// <summary>The seven skies and their names, indexed for the panel's severity ladder.</summary>
    internal static class WeatherRegimes
    {
        public const int Count = RegimeTable.Count;

        public static int Index(WeatherRegime regime)
        {
            int index = (int)regime;
            return index < 0 ? 0 : index >= Count ? Count - 1 : index;
        }

        public static WeatherRegime FromIndex(int index) => RegimeTable.Clamp(index);

        public static string Label(WeatherRegime regime) => RegimeTable.Name(regime);

        /// <summary>Frontal and up is what the panel paints as a hazard.</summary>
        public static bool IsSevere(WeatherRegime regime) => Index(regime) >= (int)WeatherRegime.Frontal;

        public static WeatherRegime FromConditions(float conditions) => RegimeTable.FromConditions(conditions);

        public static float Clamp01(float value) =>
            float.IsNaN(value) ? 0f : Math.Max(0f, Math.Min(1f, value));
    }

    /// <summary>What is falling, in the words the observation cell prints.</summary>
    internal enum PrecipitationKind
    {
        None = 0,
        Drizzle = 1,
        Rain = 2,
        Showers = 3,
        Hail = 4,
    }

    /// <summary>The standard ceiling-and-visibility classification.</summary>
    internal enum FlightCategory
    {
        Vfr = 0,
        Mvfr = 1,
        Ifr = 2,
        Lifr = 3,
    }

    internal static class Atmospheres
    {
        private static readonly string[] PrecipitationLabels = { "DRY", "DRIZZLE", "RAIN", "SHOWERS", "HAIL" };

        public static string Label(PrecipitationKind kind)
        {
            int index = (int)kind;
            return PrecipitationLabels[index < 0 ? 0 : index >= PrecipitationLabels.Length ? 0 : index];
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

        /// <summary>0 = best, 3 = worst, for the ramp.</summary>
        public static int Rank(FlightCategory category)
        {
            int rank = (int)category;
            return rank < 0 ? 0 : rank > 3 ? 3 : rank;
        }
    }

    /// <summary>
    /// The physics behind the schedule's own sky: temperature, dewpoint, ceiling, visibility,
    /// gust and the convective energy, straight from the one point sample. The presentation
    /// marks these <c>· SCHED</c> while an override owns the world, exactly as before.
    /// </summary>
    internal readonly struct Atmosphere
    {
        public readonly bool Available;
        public readonly float TemperatureC;
        public readonly float DewpointC;
        public readonly float Lcl;
        public readonly float Cape;
        public readonly float Cin;
        public readonly float Shear;
        public readonly float GustSpeed;

        /// <summary>0..1 rain intensity at the reader, the panel's own fraction.</summary>
        public readonly float RainRate;

        /// <summary>Slant visibility, metres.</summary>
        public readonly float Visibility;

        public readonly PrecipitationKind Precipitation;
        public readonly FlightCategory Category;

        public Atmosphere(bool available, float temperatureC, float dewpointC, float lcl, float cape,
            float cin, float shear, float gustSpeed, float rainRate, float visibility,
            PrecipitationKind precipitation, FlightCategory category)
        {
            Available = available;
            TemperatureC = temperatureC;
            DewpointC = Mathf.Min(dewpointC, temperatureC);
            Lcl = lcl;
            Cape = WeatherRegimes.Clamp01(cape);
            Cin = WeatherRegimes.Clamp01(cin);
            Shear = WeatherRegimes.Clamp01(shear);
            GustSpeed = gustSpeed < 0f ? 0f : gustSpeed;
            RainRate = WeatherRegimes.Clamp01(rainRate);
            Visibility = visibility < 0f ? 0f : visibility;
            Precipitation = precipitation;
            Category = category;
        }

        public static Atmosphere Unavailable => default;

        public bool IsSevereConvection => Cape >= 0.55f && Cin <= 0.45f;
    }

    internal enum FrontKind
    {
        None = 0,
        Cold = 1,
        Warm = 2,
        Occluded = 3,
        DryLine = 4,
    }

    internal static class FrontKinds
    {
        public const int Count = 5;

        private static readonly string[] Labels = { "NONE", "COLD", "WARM", "OCCLUDED", "DRY LINE" };
        private static readonly float[] Widths = { 0f, 14000f, 62000f, 40000f, 9000f };

        public static int Index(FrontKind kind)
        {
            int index = (int)kind;
            return index < 0 ? 0 : index >= Count ? Count - 1 : index;
        }

        public static string Label(FrontKind kind) => Labels[Index(kind)];

        public static float Width(FrontKind kind) => Widths[Index(kind)];

        public static bool IsConvective(FrontKind kind) =>
            kind == FrontKind.Cold || kind == FrontKind.Occluded || kind == FrontKind.DryLine;
    }

    /// <summary>
    /// One boundary as the page reads it: a moving line with a band width and an activity.
    /// <see cref="BehindHeading"/> is the wind the reader ends up in once it has passed, which
    /// is what the rose's veer needle points at.
    /// </summary>
    internal readonly struct WeatherFront
    {
        public readonly bool Present;
        public readonly FrontKind Kind;
        public readonly float NormalX;
        public readonly float NormalZ;
        public readonly float Position;
        public readonly float Speed;
        public readonly float Width;
        public readonly float Activity;
        public readonly byte Behind;
        public readonly byte Ahead;
        public readonly float BehindHeading;

        public WeatherFront(bool present, FrontKind kind, float normalX, float normalZ, float position,
            float speed, float width, float activity, byte behind, byte ahead)
            : this(present, kind, normalX, normalZ, position, speed, width, activity, behind, ahead, 0f)
        {
        }

        public WeatherFront(bool present, FrontKind kind, float normalX, float normalZ, float position,
            float speed, float width, float activity, byte behind, byte ahead, float behindHeading)
        {
            float length = (float)Math.Sqrt(normalX * normalX + normalZ * normalZ);
            if (length < 1e-5f)
            {
                normalX = 0f;
                normalZ = 1f;
            }
            else
            {
                normalX /= length;
                normalZ /= length;
            }

            Present = present;
            Kind = kind;
            NormalX = normalX;
            NormalZ = normalZ;
            Position = position;
            Speed = speed < 0f ? 0f : speed;
            Width = width < 1f ? 1f : width;
            Activity = WeatherRegimes.Clamp01(activity);
            Behind = behind;
            Ahead = ahead;
            BehindHeading = behindHeading;
        }

        public static WeatherFront None => default;

        public float SignedDistanceTo(float x, float z) => Position - (x * NormalX + z * NormalZ);

        public float DistanceTo(float x, float z) => Math.Abs(SignedDistanceTo(x, z));

        public float InfluenceAt(float x, float z)
        {
            if (!Present || Activity <= 0f) return 0f;
            float t = WeatherRegimes.Clamp01(1f - DistanceTo(x, z) / Width);
            return t * t * (3f - 2f * t) * Activity;
        }

        public float SecondsUntil(float x, float z)
        {
            if (!Present || Speed <= 0f) return float.MaxValue;
            float distance = -SignedDistanceTo(x, z);
            if (distance <= 0f) return 0f;
            return distance / Speed;
        }
    }

    internal enum StormKind
    {
        Cumulus = 0,
        ToweringCumulus = 1,
        Supercell = 2,
    }

    internal enum StormWarning
    {
        None = 0,
        Advisory = 1,
        Watch = 2,
        Warning = 3,
    }

    /// <summary>One cell as the radar, the warning rings and the table read it.</summary>
    internal readonly struct StormCell
    {
        public readonly int Slot;
        public readonly StormKind Kind;
        public readonly float X;
        public readonly float Z;
        public readonly float Radius;
        public readonly float Intensity;
        public readonly float Age;
        public readonly float Lifetime;
        public readonly float CloudBase;
        public readonly float TopHeight;
        public readonly float VelocityX;
        public readonly float VelocityZ;

        public StormCell(int slot, StormKind kind, float x, float z, float radius, float intensity,
            float age, float lifetime, float cloudBase, float topHeight, float velocityX, float velocityZ)
        {
            Slot = slot;
            Kind = kind;
            X = x;
            Z = z;
            Radius = radius;
            Intensity = intensity;
            Age = age;
            Lifetime = lifetime;
            CloudBase = cloudBase;
            TopHeight = topHeight;
            VelocityX = velocityX;
            VelocityZ = velocityZ;
        }

        public bool IsSupercell => Kind == StormKind.Supercell;

        public float DistanceTo(float x, float z)
        {
            float dx = X - x;
            float dz = Z - z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public float InfluenceAt(float x, float z)
        {
            if (Radius <= 0f || Intensity <= 0f) return 0f;
            float falloff = WeatherRegimes.Clamp01(1f - DistanceTo(x, z) / Radius);
            return falloff * falloff * (3f - 2f * falloff) * Intensity;
        }

        public StormWarning WarningAt(float x, float z)
        {
            if (Radius <= 0f || Intensity < StormField.MinWarningIntensity) return StormWarning.None;
            float distance = DistanceTo(x, z);
            if (distance <= Radius * 0.6f) return StormWarning.Warning;
            if (distance <= Radius * 1.6f) return StormWarning.Watch;
            if (distance <= Radius * 4f) return StormWarning.Advisory;
            return StormWarning.None;
        }
    }

    internal enum StormMode
    {
        None = 0,
        Isolated = 1,
        Scattered = 2,
        Cluster = 3,
        SquallLine = 4,
    }

    internal static class StormModes
    {
        private static readonly string[] Labels = { "NONE", "ISOLATED", "SCATTERED", "CLUSTER", "SQUALL LINE" };

        public static string Label(StormMode mode)
        {
            int index = (int)mode;
            return Labels[index < 0 ? 0 : index >= Labels.Length ? 0 : index];
        }

        public static bool IsOrganised(StormMode mode) =>
            mode == StormMode.Cluster || mode == StormMode.SquallLine;
    }

    internal readonly struct WeatherForecastEntry
    {
        public readonly float AtSeconds;
        public readonly WeatherState State;
        public readonly float ConditionsDelta;
        public readonly float WindDelta;

        public WeatherForecastEntry(float atSeconds, WeatherState state, float conditionsDelta, float windDelta)
        {
            AtSeconds = atSeconds;
            State = state;
            ConditionsDelta = Clamp(conditionsDelta, -1f, 1f);
            WindDelta = Clamp(windDelta, -WindDeltaLimit, WindDeltaLimit);
        }

        public int Severity => WeatherRegimes.Index(State.Regime);

        public const float WindDeltaLimit = 40f;

        private static float Clamp(float value, float min, float max)
        {
            if (float.IsNaN(value)) return 0f;
            if (value < min) return min;
            return value > max ? max : value;
        }
    }

    internal sealed class WeatherForecast
    {
        public const int MaxEntries = 12;
        public const float MinStepSeconds = 30f;
        public const float DefaultStepSeconds = 180f;
        public const int DefaultSteps = 8;

        private readonly WeatherForecastEntry[] entries = new WeatherForecastEntry[MaxEntries];

        public int Count { get; private set; }
        public float NextChangeSeconds { get; private set; }
        public WeatherRegime NextRegime { get; private set; }

        public WeatherForecastEntry this[int index] => entries[index < 0 ? 0 : index >= Count ? Count - 1 : index];

        internal void Set(int index, WeatherForecastEntry entry) => entries[index] = entry;

        internal void SetCount(int count) => Count = count < 0 ? 0 : count > MaxEntries ? MaxEntries : count;

        internal void SetNext(float seconds, WeatherRegime regime)
        {
            NextChangeSeconds = seconds;
            NextRegime = regime;
        }
    }

    /// <summary>The forecast's model seam: the same chain the field runs, sampled ahead.</summary>
    internal static class WeatherModel
    {
        public const float MinCloudBase = 250f;
        public const float MaxCloudBase = 3600f;

        private static readonly WeatherField Scratch = new WeatherField();

        public static int Seed(string identity) => unchecked((int)Deterministic.HashString(identity));

        public static float ClampCloudBase(float metres) => Mathf.Clamp(metres, MinCloudBase, MaxCloudBase);

        /// <summary>The schedule's sky at a mission instant, for the radar's scrub view.</summary>
        public static WeatherState Sample(int seed, float at)
        {
            var key = new WeatherKey(unchecked((uint)seed), 0f, true, WeatherKey.AutoRegime, WeatherFlags.None);
            Scratch.Build(key, at, 40000f, 40000f);
            RegimeState regime = Scratch.Regime;
            return new WeatherState(
                regime.Dominant,
                regime.Params.Overcast,
                regime.Params.CloudBase,
                regime.Params.WindSpeed,
                WeatherField.PrevailingHeading(key.Seed, at),
                regime.Params.Turbulence);
        }
    }

    /// <summary>The deterministic cell population, mapped into the read model.</summary>
    internal static class StormField
    {
        public const int MaxCells = StormCells.MaxCells;
        public const float MinWarningIntensity = 0.18f;
        public const float SupercellTopMax = 9600f;

        private static readonly WeatherField Scratch = new WeatherField();

        /// <summary>
        /// The cells alive at <paramref name="missionTime"/>, and how the population reads.
        /// The front and atmosphere terms are already folded into the field, so they are taken
        /// for the read-model shape only.
        /// </summary>
        public static int Fill(StormCell[] destination, int seed, float missionTime, float mapSize,
            float frontConditions, float windHeading, float windSpeed,
            in WeatherFront front, in Atmosphere atmosphere, out StormMode mode)
        {
            mode = StormMode.None;
            if (destination == null) return 0;

            var key = new WeatherKey(unchecked((uint)seed), 0f, true, WeatherKey.AutoRegime, WeatherFlags.None);
            Scratch.Build(key, missionTime, mapSize, mapSize);
            mode = ModeOf(Scratch);

            int count = 0;
            for (int i = 0; i < Scratch.CellCount && count < destination.Length && count < MaxCells; i++)
            {
                destination[count++] = Map(Scratch.Cell(i));
            }
            return count;
        }

        public static StormCell StrongestAt(StormCell[] cells, int count, float x, float z, out float influence)
        {
            influence = 0f;
            StormCell best = default;
            for (int i = 0; i < count; i++)
            {
                float value = cells[i].InfluenceAt(x, z);
                if (value <= influence) continue;
                influence = value;
                best = cells[i];
            }
            return best;
        }

        public static StormWarning WarningAt(StormCell[] cells, int count, float x, float z, out StormCell source)
        {
            StormWarning worst = StormWarning.None;
            source = default;
            for (int i = 0; i < count; i++)
            {
                StormWarning warning = cells[i].WarningAt(x, z);
                if (warning <= worst) continue;
                worst = warning;
                source = cells[i];
            }
            return worst;
        }

        internal static StormCell Map(in Domain.StormCell cell)
        {
            StormKind kind = cell.Stage == StormStage.Mature
                ? cell.Severe ? StormKind.Supercell : StormKind.ToweringCumulus
                : StormKind.Cumulus;
            return new StormCell(
                cell.Slot, kind, cell.X, cell.Z, cell.Radius, cell.RainLevel, cell.Age, cell.Life,
                cell.Base, cell.Top, cell.VelocityX, cell.VelocityZ);
        }

        internal static StormMode ModeOf(WeatherField field)
        {
            int count = field.CellCount;
            if (count <= 0) return StormMode.None;

            int onFront = 0;
            for (int i = 0; i < count; i++)
            {
                if (field.Cell(i).OnFront) onFront++;
            }
            if (onFront >= 3) return StormMode.SquallLine;
            if (field.Regime.Params.Convective >= 0.7f && count >= 4) return StormMode.Cluster;
            if (count == 1) return StormMode.Isolated;
            return StormMode.Scattered;
        }
    }

    /// <summary>
    /// One read of the new field in the shape the page speaks. Pure per call: it copies cells
    /// into the caller's slot buffer and derives everything else, never mutating the field.
    /// </summary>
    internal static class WeatherView
    {
        private const int Slots = 3;

        private static readonly StormCell[][] Buffers = CreateBuffers();

        private static StormCell[][] CreateBuffers()
        {
            var buffers = new StormCell[Slots][];
            for (int i = 0; i < Slots; i++) buffers[i] = new StormCell[StormField.MaxCells];
            return buffers;
        }

        public static bool OverrideActive(SynopticWeather manager) =>
            manager != null && manager.Key != null && manager.Key.OverrideCount > 0;

        public static WeatherSnapshot Capture(SynopticWeather manager, int slot)
        {
            if (manager == null || !manager.Ready) return WeatherSnapshot.Unavailable;

            StormCell[] cells = Buffers[slot < 0 ? 0 : slot >= Slots ? Slots - 1 : slot];
            WeatherField field = manager.Field;
            WeatherPoint point = manager.Local;

            int count = 0;
            for (int i = 0; i < field.CellCount && count < cells.Length; i++)
            {
                cells[count++] = StormField.Map(field.Cell(i));
            }

            float readerX = manager.CameraGlobal.x;
            float readerZ = manager.CameraGlobal.z;

            WeatherState model = Model(field);
            WeatherState live = new WeatherState(field.Regime.Dominant, point.Cover, point.CloudBase,
                point.WindSpeed, point.WindHeading, point.Turbulence);

            Atmosphere atmosphere = AtmosphereOf(field, point);
            WeatherFront front = FrontOf(field, readerX, readerZ, point.WindHeading);
            StormWarning warning = StormField.WarningAt(cells, count, readerX, readerZ, out StormCell source);

            return new WeatherSnapshot(
                true,
                manager.MissionTime,
                model,
                live,
                point.WindX,
                point.WindUp,
                point.WindZ,
                point.Cover,
                Daylight(manager.HourOfDay),
                OverrideActive(manager),
                manager.HostAuthority,
                cells,
                count,
                point.CoreDepth,
                warning,
                source,
                atmosphere,
                front,
                StormField.ModeOf(field));
        }

        /// <summary>The schedule's forward sample, built from the live key so it cannot lie.</summary>
        public static WeatherForecast Forecast(SynopticWeather manager)
        {
            if (manager == null || manager.Key == null || !manager.Ready) return null;

            WeatherField field = manager.Field;
            WeatherKey key = manager.Key;
            float now = manager.MissionTime;
            float step = manager.Settings != null
                ? Mathf.Max(WeatherForecast.MinStepSeconds, manager.Settings.ForecastStepMinutes.Value * 60f)
                : WeatherForecast.DefaultStepSeconds;
            int steps = manager.Settings != null
                ? Mathf.Clamp(manager.Settings.ForecastSteps.Value, 0, WeatherForecast.MaxEntries)
                : WeatherForecast.DefaultSteps;

            var forecast = new WeatherForecast();
            var scratch = new WeatherField();
            var entries = new ForecastEntry[WeatherForecast.MaxEntries];
            int count = WeatherWords.Forecast(scratch, key, now, step, 0f, 0f,
                field.HalfX, field.HalfZ, manager.HourOfDay, entries);

            WeatherState previous = Sample(key, field, now);
            for (int i = 0; i < count; i++)
            {
                ForecastEntry entry = entries[i];
                WeatherState state = new WeatherState(
                    entry.Regime,
                    entry.Cover,
                    RegimeTable.Get(entry.Regime).CloudBase,
                    entry.WindSpeed,
                    WeatherState.WrapHeading(entry.WindFrom + 180f),
                    RegimeTable.Get(entry.Regime).Turbulence);
                forecast.Set(i, new WeatherForecastEntry(
                    now + entry.OffsetSeconds,
                    state,
                    state.Conditions - previous.Conditions,
                    state.WindSpeed - previous.WindSpeed));
                previous = state;
            }
            forecast.SetCount(count);

            RegimeState current = RegimeSchedule.Evaluate(key, now);
            float until = current.NextChangeAt - now;
            forecast.SetNext(
                float.IsNaN(until) || float.IsInfinity(until) || until < 0f ? 0f : until,
                current.NextRegime);
            return forecast;
        }

        private static WeatherState Model(WeatherField field)
        {
            RegimeState regime = field.Regime;
            return new WeatherState(
                regime.Dominant,
                regime.Params.Overcast,
                regime.Params.CloudBase,
                regime.Params.WindSpeed,
                field.PrevailingHeadingNow,
                regime.Params.Turbulence);
        }

        private static WeatherState Sample(WeatherKey key, WeatherField field, float at)
        {
            if (Mathf.Abs(at - field.Time) < 0.01f) return Model(field);
            RegimeState regime = RegimeSchedule.Evaluate(key, at);
            return new WeatherState(
                regime.Dominant,
                regime.Params.Overcast,
                regime.Params.CloudBase,
                regime.Params.WindSpeed,
                WeatherField.PrevailingHeading(key.Seed, at),
                regime.Params.Turbulence);
        }

        private static Atmosphere AtmosphereOf(WeatherField field, WeatherPoint point)
        {
            RegimeParams regime = field.Regime.Params;
            PrecipitationKind kind = PrecipitationOf(point);
            FlightCategory category = CategoryOf(point);
            return new Atmosphere(
                true,
                point.Temperature,
                point.Dewpoint,
                point.CloudBase,
                regime.Convective,
                WeatherRegimes.Clamp01(1f - regime.Convective),
                ShearOf(field, point, regime),
                point.Gust,
                point.RainRate / 50f,
                point.VisibilityKm * 1000f,
                kind,
                category);
        }

        private static PrecipitationKind PrecipitationOf(WeatherPoint point)
        {
            switch (point.Precipitation)
            {
                case Domain.PrecipitationKind.Drizzle:
                    return PrecipitationKind.Drizzle;
                case Domain.PrecipitationKind.Light:
                    return point.Hail ? PrecipitationKind.Hail : PrecipitationKind.Rain;
                case Domain.PrecipitationKind.Moderate:
                    return point.Hail ? PrecipitationKind.Hail
                        : point.ConvectiveShare > 0.5f ? PrecipitationKind.Showers : PrecipitationKind.Rain;
                case Domain.PrecipitationKind.Heavy:
                case Domain.PrecipitationKind.Violent:
                    return point.Hail ? PrecipitationKind.Hail : PrecipitationKind.Showers;
                default:
                    return PrecipitationKind.None;
            }
        }

        private static FlightCategory CategoryOf(WeatherPoint point)
        {
            switch (WeatherWords.Category(point.VisibilityKm, point.CloudBase))
            {
                case Domain.FlightCategory.Lifr: return FlightCategory.Lifr;
                case Domain.FlightCategory.Ifr: return FlightCategory.Ifr;
                case Domain.FlightCategory.Mvfr: return FlightCategory.Mvfr;
                default: return FlightCategory.Vfr;
            }
        }

        private static float ShearOf(WeatherField field, WeatherPoint point, RegimeParams regime)
        {
            float shear = regime.Severity * 0.45f;
            for (int i = 0; i < field.FrontCount; i++)
            {
                FrontState front = field.Front(i);
                FrontEffect effect = WeatherFronts.Profile(front.Kind, front.SignedDistance(0f, 0f));
                shear = Mathf.Max(shear, Mathf.Abs(effect.VeerDegrees) / 60f * front.Strength);
            }
            return WeatherRegimes.Clamp01(shear);
        }

        private static WeatherFront FrontOf(WeatherField field, float readerX, float readerZ, float windHeading)
        {
            int best = -1;
            float strongest = 0.05f;
            for (int i = 0; i < field.FrontCount; i++)
            {
                if (field.Front(i).Strength <= strongest) continue;
                strongest = field.Front(i).Strength;
                best = i;
            }
            if (best < 0) return WeatherFront.None;

            FrontState source = field.Front(best);
            FrontKind kind = source.Kind == Domain.FrontKind.Warm ? FrontKind.Warm : FrontKind.Cold;
            FrontEffect effect = WeatherFronts.Profile(source.Kind, source.SignedDistance(readerX, readerZ));
            float behind = WeatherState.WrapHeading(windHeading + effect.VeerDegrees * source.Strength);
            return new WeatherFront(
                true,
                kind,
                source.NormalX,
                source.NormalZ,
                source.Offset,
                source.Speed,
                FrontKinds.Width(kind),
                source.Strength,
                0,
                0,
                behind);
        }

        private static float Daylight(float hourOfDay) =>
            WeatherRegimes.Clamp01((float)Math.Sin((hourOfDay - 6f) / 12f * Math.PI));
    }

    /// <summary>One read of the sky, for the panel and the debug overlay.</summary>
    internal readonly struct WeatherSnapshot
    {
        public readonly bool Available;
        public readonly float MissionTime;
        public readonly WeatherState Model;
        public readonly WeatherState Live;
        public readonly float LocalWindX;
        public readonly float LocalWindY;
        public readonly float LocalWindZ;
        public readonly float CloudOcclusion;
        public readonly float DaylightFactor;
        public readonly bool Overridden;
        public readonly bool HostAuthority;
        public readonly bool Severe;
        public readonly StormCell[] Cells;
        public readonly int CellCount;
        public readonly float CellInfluence;
        public readonly StormWarning Warning;
        public readonly StormCell WarningSource;
        public readonly Atmosphere Atmosphere;
        public readonly WeatherFront Front;
        public readonly StormMode StormMode;

        public WeatherSnapshot(
            bool available,
            float missionTime,
            WeatherState model,
            WeatherState live,
            float localWindX,
            float localWindY,
            float localWindZ,
            float cloudOcclusion,
            float daylightFactor,
            bool overridden,
            bool hostAuthority,
            StormCell[] cells,
            int cellCount,
            float cellInfluence,
            StormWarning warning,
            StormCell warningSource,
            Atmosphere atmosphere,
            WeatherFront front,
            StormMode stormMode)
        {
            Available = available;
            MissionTime = missionTime;
            Model = model;
            Live = live;
            LocalWindX = localWindX;
            LocalWindY = localWindY;
            LocalWindZ = localWindZ;
            CloudOcclusion = cloudOcclusion;
            DaylightFactor = daylightFactor;
            Overridden = overridden;
            HostAuthority = hostAuthority;
            Severe = live.IsSevere;
            Cells = cells;
            CellCount = cellCount;
            CellInfluence = cellInfluence;
            Warning = warning;
            WarningSource = warningSource;
            Atmosphere = atmosphere;
            Front = front;
            StormMode = stormMode;
        }

        public float RainIntensity
        {
            get
            {
                float cell = WeatherRegimes.Clamp01(CellInfluence);
                float frontal = Atmosphere.Available ? Atmosphere.RainRate : 0f;
                return WeatherRegimes.Clamp01(cell > frontal ? cell : frontal);
            }
        }

        public PrecipitationKind Precipitation
        {
            get
            {
                if (Atmosphere.Available) return Atmosphere.Precipitation;
                return RainIntensity >= 0.25f ? PrecipitationKind.Showers : PrecipitationKind.Drizzle;
            }
        }

        public float LocalWindSpeed
        {
            get
            {
                float speed = (float)Math.Sqrt(LocalWindX * LocalWindX + LocalWindY * LocalWindY + LocalWindZ * LocalWindZ);
                return float.IsNaN(speed) ? 0f : speed;
            }
        }

        public float LocalWindHeading
        {
            get
            {
                if (float.IsNaN(LocalWindX) || float.IsNaN(LocalWindZ)) return 0f;
                if (Math.Abs(LocalWindX) < 1e-4f && Math.Abs(LocalWindZ) < 1e-4f) return Live.WindHeading;
                float degrees = (float)(Math.Atan2(LocalWindX, LocalWindZ) * 180.0 / Math.PI);
                return WeatherState.WrapHeading(degrees);
            }
        }

        public static WeatherSnapshot Unavailable => default;
    }
}
