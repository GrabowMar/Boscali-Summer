using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    internal enum PrecipitationKind : byte
    {
        None = 0,
        Drizzle = 1,
        Light = 2,
        Moderate = 3,
        Heavy = 4,
        Violent = 5,
    }

    /// <summary>The weather at one point and instant. Units: mm/h, metres, km, m/s, hPa, °C.</summary>
    internal struct WeatherPoint
    {
        public float RainRate;
        public float Cover;
        public float CloudBase;
        public float CloudTop;
        public float VisibilityKm;

        /// <summary>Horizontal wind, downwind components (x east, z north).</summary>
        public float WindX;
        public float WindZ;

        /// <summary>Vertical air motion: positive updraft, negative downdraft.</summary>
        public float WindUp;
        public float Gust;
        public float Turbulence;

        /// <summary>Strikes per minute from cells within a few radii.</summary>
        public float LightningRate;

        /// <summary>0..1 how deep the point is in the strongest cell core.</summary>
        public float CoreDepth;

        /// <summary>Share of the rain that is convective (cells), 0..1.</summary>
        public float ConvectiveShare;

        public bool Hail;
        public float Qnh;
        public float Temperature;
        public float Dewpoint;

        public PrecipitationKind Precipitation => WeatherField.Classify(RainRate);

        public float WindSpeed => (float)Math.Sqrt(WindX * WindX + WindZ * WindZ);

        /// <summary>Downwind heading in degrees. Displays use <see cref="WeatherWords.WindFrom"/>.</summary>
        public float WindHeading => WeatherMath.VectorToHeading(WindX, WindZ);
    }

    /// <summary>
    /// The weather field at one instant: the regime, up to three fronts and up to twelve cells,
    /// resolved once by <see cref="Build"/> and then sampled anywhere by <see cref="Sample"/>.
    /// A frame owns its buffers and is reused, so building one allocates nothing.
    ///
    /// <para>Everything is a pure function of (key, time, map extent, hour of day): the host,
    /// every client, the radar, the forecast and the tests all see the same sky.</para>
    /// </summary>
    internal sealed class WeatherField
    {
        public const float MaxRainRate = 150f;

        /// <summary>Rain patches drift at a fixed velocity so they never jump when the wind changes.</summary>
        public const float PatchScale = 14000f;
        public const float PatchDrift = 6f;

        private readonly StormCell[] cells = new StormCell[StormCells.MaxCells];
        private readonly FrontState[] fronts = new FrontState[RegimeState.MaxFronts];

        public WeatherKey Key { get; private set; }
        public float Time { get; private set; }
        public float HalfX { get; private set; }
        public float HalfZ { get; private set; }
        public float HourOfDay { get; private set; }
        public RegimeState Regime { get; private set; }
        public int CellCount { get; private set; }
        public int FrontCount { get; private set; }

        /// <summary>Multiplier on haze visibility (1 = clear air; smoke from fires lowers it).</summary>
        public float HazeScale { get; private set; } = 1f;

        public float PrevailingHeadingNow { get; private set; }
        public bool IsBuilt => Key != null;

        public StormCell Cell(int index) => cells[index];
        public FrontState Front(int index) => fronts[index];

        public void Build(WeatherKey key, float time, float halfX, float halfZ, float hourOfDay = 12f, float hazeScale = 1f)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Time = time;
            HalfX = Math.Max(halfX, 1000f);
            HalfZ = Math.Max(halfZ, 1000f);
            HourOfDay = hourOfDay;
            HazeScale = WeatherMath.Clamp(hazeScale, 0.05f, 1f);

            RegimeState regime = RegimeSchedule.Evaluate(key, time);
            Regime = regime;
            PrevailingHeadingNow = PrevailingHeading(key.Seed, time);

            FrontCount = 0;
            for (int i = 0; i < regime.FrontCount; i++)
            {
                fronts[FrontCount++] = WeatherFronts.Resolve(regime.GetFront(i), key.Seed, time);
            }

            CellCount = StormCells.Fill(cells, key, time, HalfX, HalfZ);
        }

        public WeatherPoint Sample(float x, float z)
        {
            RegimeParams p = Regime.Params;
            var point = new WeatherPoint();

            // Area (stratiform) rain and base cover, broken up by a drifting patch field.
            float patch = Patch(x, z);
            float areaRain = p.AreaRain * 2.2f * WeatherMath.Smoothstep(0.45f, 0.8f, patch);
            float coverBase = WeatherMath.Clamp01(p.Overcast + (patch - 0.5f) * 1.1f);

            // Mean wind.
            PrevailingWind(Key.Seed, p.WindSpeed, Time, out float windX, out float windZ);
            float windSpeed = p.WindSpeed;
            float turbulence = p.Turbulence;

            float frontRain = 0f;
            float clearFront = 1f;
            float veer = 0f;
            for (int i = 0; i < FrontCount; i++)
            {
                FrontState front = fronts[i];
                FrontEffect effect = WeatherFronts.Profile(front.Kind, front.SignedDistance(x, z));
                frontRain += effect.Rain * front.Strength;
                clearFront *= 1f - effect.Cover * front.Strength;
                veer += effect.VeerDegrees * front.Strength;
                windSpeed += effect.WindBoost * front.Strength;
                turbulence += effect.Turbulence * front.Strength;
            }

            if (veer != 0f || windSpeed != p.WindSpeed)
            {
                float heading = WeatherMath.VectorToHeading(windX, windZ) + veer;
                WeatherMath.HeadingToVector(heading, out float hx, out float hz);
                windX = hx * windSpeed;
                windZ = hz * windSpeed;
            }

            float cellRain = 0f;
            float clearCells = 1f;
            float outX = 0f, outZ = 0f, vertical = 0f;
            float lightning = 0f;
            float core = 0f;
            float top = 0f;
            bool hail = false;
            for (int i = 0; i < CellCount; i++)
            {
                StormCell cell = cells[i];
                float dx = x - cell.X, dz = z - cell.Z;
                // Every term is negligible by 8 radii; a shorter cut would drop the trailing
                // halo abruptly and make rain pop at the cut.
                float reach = cell.Radius * 8f;
                if (dx * dx + dz * dz > reach * reach) continue;

                cellRain += cell.RainAt(x, z);
                float cover = cell.CoverAt(x, z);
                clearCells *= 1f - cover;
                cell.WindAt(x, z, out float ox, out float oz, out float w, out float t);
                outX += ox;
                outZ += oz;
                vertical += w;
                turbulence += t;

                float d2 = (dx * dx + dz * dz) / (9f * cell.Radius * cell.Radius);
                lightning += cell.LightningRate * (float)Math.Exp(-d2);

                float depth = cell.CoreAt(x, z);
                if (depth > core) core = depth;
                if (cell.Hail && depth > 0.5f) hail = true;
                if (cover > 0.3f && cell.Top > top) top = cell.Top;
            }

            float rain = Math.Min(areaRain + frontRain + cellRain, MaxRainRate);
            point.RainRate = rain;
            point.ConvectiveShare = rain > 0.01f ? cellRain / (areaRain + frontRain + cellRain) : 0f;
            point.Cover = WeatherMath.Clamp01(1f - (1f - coverBase) * clearFront * clearCells);

            float cloudBase = p.CloudBase - 300f * WeatherMath.Smoothstep(0f, 15f, rain);
            point.CloudBase = WeatherMath.Clamp(cloudBase, 250f, 3600f);
            point.CloudTop = Math.Max(point.CloudBase + 900f + 1500f * point.Cover, top);

            float haze = Math.Max(p.HazeKm * HazeScale, 0.5f);
            float extinction = 3.912f / haze + 0.25f * (float)Math.Pow(rain, 0.66);
            point.VisibilityKm = WeatherMath.Clamp(3.912f / extinction, 0.3f, 50f);

            point.WindX = windX + outX;
            point.WindZ = windZ + outZ;
            point.WindUp = vertical;
            float outflow = (float)Math.Sqrt(outX * outX + outZ * outZ);
            point.Turbulence = WeatherMath.Clamp01(turbulence);
            point.Gust = 0.35f * windSpeed * point.Turbulence + outflow;
            point.LightningRate = lightning;
            point.CoreDepth = WeatherMath.Clamp01(core);
            point.Hail = hail;

            float frontDip = 0f;
            for (int i = 0; i < FrontCount; i++)
            {
                float d = fronts[i].SignedDistance(x, z);
                frontDip += 3f * fronts[i].Strength * (float)Math.Exp(-(d / 15000f) * (d / 15000f));
            }
            point.Qnh = p.Qnh - frontDip + (patch - 0.5f) * 2f;

            float diurnal = 5f * (float)Math.Sin((HourOfDay - 9f) / 24f * 2f * Math.PI);
            point.Temperature = p.Temperature + diurnal - 4f * WeatherMath.Smoothstep(0f, 15f, rain);
            float humid = WeatherMath.Clamp01(point.Cover * 0.6f + WeatherMath.Smoothstep(0f, 5f, rain) * 0.6f);
            point.Dewpoint = point.Temperature - WeatherMath.Lerp(9f, 0.5f, humid);
            return point;
        }

        /// <summary>The regional sky at the map centre.</summary>
        public WeatherPoint Regional() => Sample(0f, 0f);

        /// <summary>
        /// The map's mean wind: prevailing wind veered and strengthened by the fronts at the map
        /// centre, without any cell's outflow. This is what vanilla's single global wind should
        /// say; cells act locally through the owner of each aircraft instead.
        /// </summary>
        public void MeanWind(out float speed, out float heading, out float turbulence)
        {
            RegimeParams p = Regime.Params;
            speed = p.WindSpeed;
            heading = PrevailingHeadingNow;
            turbulence = p.Turbulence;
            for (int i = 0; i < FrontCount; i++)
            {
                FrontEffect effect = WeatherFronts.Profile(fronts[i].Kind, fronts[i].SignedDistance(0f, 0f));
                heading += effect.VeerDegrees * fronts[i].Strength;
                speed += effect.WindBoost * fronts[i].Strength;
                turbulence += effect.Turbulence * fronts[i].Strength;
            }
            heading = WeatherMath.WrapHeading(heading);
            turbulence = WeatherMath.Clamp01(turbulence);
        }

        /// <summary>Mean of cover over a coarse grid; what vanilla's global <c>conditions</c> should say.</summary>
        public float MeanCover(int steps = 5)
        {
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < steps; i++)
            {
                for (int j = 0; j < steps; j++)
                {
                    float x = ((i + 0.5f) / steps * 2f - 1f) * HalfX;
                    float z = ((j + 0.5f) / steps * 2f - 1f) * HalfZ;
                    sum += Sample(x, z).Cover;
                    n++;
                }
            }
            return n > 0 ? sum / n : 0f;
        }

        /// <summary>The drifting patch field in [0, 1] that breaks up area rain and cover.</summary>
        public float Patch(float x, float z)
        {
            WeatherMath.HeadingToVector(PatchHeading(Key.Seed), out float dx, out float dz);
            float px = (x - dx * PatchDrift * Time) / PatchScale;
            float pz = (z - dz * PatchDrift * Time) / PatchScale;
            return WeatherMath.Fbm(Key.Seed ^ 0x5eedu, px, pz);
        }

        public static PrecipitationKind Classify(float rain)
        {
            if (rain < 0.1f) return PrecipitationKind.None;
            if (rain < 0.5f) return PrecipitationKind.Drizzle;
            if (rain < 2.5f) return PrecipitationKind.Light;
            if (rain < 10f) return PrecipitationKind.Moderate;
            if (rain < 50f) return PrecipitationKind.Heavy;
            return PrecipitationKind.Violent;
        }

        /// <summary>Radar reflectivity (dBZ) for a rain rate, Marshall–Palmer Z = 200 R^1.6.</summary>
        public static float Reflectivity(float rain)
        {
            if (rain < 0.05f) return 0f;
            return 10f * (float)Math.Log10(200.0 * Math.Pow(rain, 1.6));
        }

        /// <summary>
        /// Downwind heading of the prevailing wind: a seeded direction swinging ±35° over two
        /// hours. Continuous in time; fronts and cells read it at their own birth instants.
        /// </summary>
        public static float PrevailingHeading(uint seed, float time)
        {
            float baseHeading = WeatherMath.Hash01(seed, 41) * 360f;
            float phase = WeatherMath.Hash01(seed, 42) * 6.2831853f;
            return WeatherMath.WrapHeading(baseHeading + 35f * (float)Math.Sin(time / 7200f * 6.2831853f + phase));
        }

        public static void PrevailingWind(uint seed, float speed, float time, out float x, out float z)
        {
            WeatherMath.HeadingToVector(PrevailingHeading(seed, time), out x, out z);
            x *= speed;
            z *= speed;
        }

        private static float PatchHeading(uint seed) => WeatherMath.Hash01(seed, 41) * 360f;
    }
}
