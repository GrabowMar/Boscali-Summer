using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
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
        public float BackgroundCover;
        public float ClusterCover;
        public float CellCover;
        public float CellShape;
        public float FrontCover;
        /// <summary>Local frontal shield bounds in metres; both zero without front cover.</summary>
        public float FrontBase;
        public float FrontTop;
        public float CloudBase;
        /// <summary>Ordinary deck/tower ceiling; excludes the independent frontal shield.</summary>
        public float LowTop;
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
    /// The weather at one instant: the timeline's state and a static cloud layout (up to two
    /// front bands, twelve storm-cell sites and eight cumulus groups), resolved once by
    /// <see cref="Build"/> and sampled anywhere by <see cref="Sample"/>. Nothing in the layout
    /// moves; a state change only grows or shrinks what is there. A field owns its buffers and is
    /// reused, so building one allocates nothing.
    ///
    /// <para>Everything is a pure function of (key, time, map extent, hour of day): the host,
    /// every client, the forecast and the tests all see the same sky.</para>
    /// </summary>
    internal sealed class WeatherField
    {
        public const float MaxRainRate = 150f;

        /// <summary>Scale of the static patch field that breaks up the sheet and area rain.</summary>
        public const float PatchScale = 14000f;

        /// <summary>Mean surface wind the state's wind factor scales, m/s. Display and local
        /// effects only; native wind keeps the mission's own speed.</summary>
        public const float BaseWindSpeed = 7f;

        private readonly StormCell[] cells = new StormCell[StormCells.MaxCells];
        private readonly FrontState[] fronts = new FrontState[WeatherFronts.MaxFronts];
        private readonly DryCloudCluster[] cloudClusters = new DryCloudCluster[DryCloudCluster.MaxCount];
        private readonly Superstructure[] superstructures = new Superstructure[Superstructures.MaxCount];

        public WeatherKey Key { get; private set; }
        public float Time { get; private set; }
        public float HalfX { get; private set; }
        public float HalfZ { get; private set; }
        public float HourOfDay { get; private set; }
        public TimelineState Timeline { get; private set; }
        /// <summary>The frontal boundary across the map in BROKEN and worse skies.</summary>
        public SkySplit Split { get; private set; }
        public StateParams Params { get; private set; }
        public int CellCount { get; private set; }
        public int FrontCount { get; private set; }
        public int CloudClusterCount { get; private set; }
        /// <summary>Scenery storms outside the theater; see <see cref="Superstructures"/>.</summary>
        public int SuperstructureCount { get; private set; }

        /// <summary>Multiplier on haze visibility (1 = clear air; smoke from fires lowers it).</summary>
        public float HazeScale { get; private set; } = 1f;

        /// <summary>Downwind heading of the layout's prevailing wind; fixed for the key.</summary>
        public float PrevailingHeading { get; private set; }
        public bool IsBuilt => Key != null;

        public StormCell Cell(int index) => cells[index];
        public FrontState Front(int index) => fronts[index];
        public DryCloudCluster CloudCluster(int index) => cloudClusters[index];
        public Superstructure SuperstructureAt(int index) => superstructures[index];

        public void Build(WeatherKey key, float time, float halfX, float halfZ, float hourOfDay = 12f, float hazeScale = 1f)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Time = time;
            HalfX = Math.Max(halfX, 1000f);
            HalfZ = Math.Max(halfZ, 1000f);
            HourOfDay = hourOfDay;
            HazeScale = Scalar.Clamp(hazeScale, 0.05f, 1f);

            TimelineState timeline = WeatherTimeline.Evaluate(key, time);
            Timeline = timeline;
            StateParams sky = StateTable.At(timeline.Level);
            Params = sky;
            PrevailingHeading = WeatherMath.Hash01(key.Seed, 41) * 360f;
            WeatherMath.HeadingToVector(PrevailingHeading, out float driftX, out float driftZ);

            Split = SkySplit.From(timeline.Layout, sky.Split, HalfX, HalfZ, PrevailingHeading, key.FrontTurn);
            FrontCount = WeatherFronts.Fill(fronts, timeline.Layout, sky, HalfX, HalfZ, PrevailingHeading, Split);
            StateParams convection = StateTable.At(timeline.GrowthLevel);
            CellCount = StormCells.Fill(cells, timeline.Layout, convection, HalfX, HalfZ, driftX, driftZ, Split);
            CloudClusterCount = 0;
            for (int i = 0; i < cloudClusters.Length; i++)
                if (DryCloudCluster.TryResolve(timeline.Layout, i, convection, HalfX, HalfZ, out DryCloudCluster cloud, driftX, driftZ))
                    cloudClusters[CloudClusterCount++] = cloud;
            SuperstructureCount = Superstructures.Fill(superstructures, timeline.Layout, convection,
                HalfX, HalfZ, PrevailingHeading, key.Sets, key.HasAnchor, key.AnchorX, key.AnchorZ);
        }

        public WeatherPoint Sample(float x, float z)
        {
            StateParams p = Params;
            var point = new WeatherPoint();

            // Area (stratiform) rain and the sheet, broken up by the static patch field so a
            // thinning deck opens in patches instead of fading uniformly.
            float patch = Patch(x, z);
            // Behind the frontal boundary the full deck; ahead of it the sky opens.
            float split = Split.Cover(x, z);
            float areaRain = p.AreaRain * 2.2f * WeatherMath.Smoothstep(0.45f, 0.8f, patch) * split;
            float sheet = p.Overcast * split;
            float coverBase = Scalar.Clamp01(sheet + (patch - 0.5f) * (0.10f + 0.55f * sheet));
            point.BackgroundCover = coverBase;

            float windSpeed = BaseWindSpeed * p.WindFactor;
            float turbulence = p.Turbulence;

            float frontRain = 0f;
            float clearFront = 1f;
            float veer = 0f;
            float frontWeight = 0f, frontBaseSum = 0f, frontTopSum = 0f;
            for (int i = 0; i < FrontCount; i++)
            {
                FrontState front = fronts[i];
                FrontEffect effect = WeatherFronts.Profile(front.Kind, front.SignedDistance(x, z));
                frontRain += effect.Rain * front.Strength;
                float cover = effect.Cover * front.Strength;
                clearFront *= 1f - cover;
                float underside = Scalar.Clamp(p.CloudBase + effect.BaseOffset, 250f, 8500f);
                frontWeight += cover;
                frontBaseSum += underside * cover;
                frontTopSum += (underside + effect.Depth) * cover;
                veer += effect.VeerDegrees * front.Strength;
                windSpeed += effect.WindBoost * front.Strength;
                turbulence += effect.Turbulence * front.Strength;
            }

            WeatherMath.HeadingToVector(PrevailingHeading + veer, out float windX, out float windZ);
            windX *= windSpeed;
            windZ *= windSpeed;

            float cellRain = 0f;
            float clearCells = 1f;
            float clearClusters = 1f;
            float outX = 0f, outZ = 0f, vertical = 0f;
            float lightning = 0f;
            float core = 0f;
            float top = 0f;
            bool hail = false;
            for (int i = 0; i < CellCount; i++)
            {
                StormCell cell = cells[i];
                float dx = x - cell.X, dz = z - cell.Z;
                float reach = cell.Radius * 8f;
                if (dx * dx + dz * dz > reach * reach) continue;

                cellRain += cell.RainAt(x, z);
                float cover = cell.CoverAt(x, z);
                clearCells *= 1f - cover;
                float shape = cell.ShapeAt(x, z);
                if (shape > point.CellShape) point.CellShape = shape;
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
                // The updraft is a dome. A real anvil holds that ceiling out to the flared
                // radius; otherwise the column top falls inside the stem and the flare in
                // CloudBodies never gets a column to fill.
                float column = (float)Math.Pow(Math.Max(shape, 0f), 0.65f);
                if (p.Anvil > 0.05f)
                {
                    float dist = cell.EllipticDistance(dx, dz);
                    float flare = CloudShape.Footprint(0.82f, p.Anvil);
                    float anvilReach = cell.Radius * 1.35f * flare;
                    float shelf = 1f - WeatherMath.Smoothstep(anvilReach, anvilReach + cell.Radius * 0.55f, dist);
                    shelf *= WeatherMath.Smoothstep(0.25f, 0.45f, p.Anvil);
                    // The spreading crown is lower away from the updraft. A literal flat
                    // shelf=1 across this radius made every ordinary storm a giant saucer.
                    shelf *= 0.82f + 0.18f * Scalar.Clamp01(shape);
                    if (shelf > column) column = shelf;
                }
                float cellCrown = cell.Base + (cell.Top - cell.Base) * column;
                if (cellCrown > top) top = cellCrown;
            }

            for (int i = 0; i < CloudClusterCount; i++)
            {
                DryCloudCluster cloud = cloudClusters[i];
                float cover = cloud.CoverAt(x, z);
                clearClusters *= 1f - cover;
                if (cover > point.CellShape) point.CellShape = cover;
                float clusterCrown = cloud.Base + (cloud.Top - cloud.Base) * (float)Math.Pow(cover, 0.65f);
                if (clusterCrown > top) top = clusterCrown;
            }

            float rain = Math.Min(areaRain + frontRain + cellRain, MaxRainRate);
            point.RainRate = rain;
            point.ConvectiveShare = rain > 0.01f ? cellRain / (areaRain + frontRain + cellRain) : 0f;
            point.Cover = Scalar.Clamp01(1f - (1f - coverBase) * clearFront * clearCells * clearClusters);
            point.FrontCover = Scalar.Clamp01(1f - clearFront);
            point.ClusterCover = Scalar.Clamp01(1f - clearClusters);
            point.CellCover = Scalar.Clamp01(1f - clearCells);

            if (frontWeight > 0f)
            {
                point.FrontBase = frontBaseSum / frontWeight;
                point.FrontTop = frontTopSum / frontWeight;
            }

            float cloudBase = p.CloudBase - 300f * WeatherMath.Smoothstep(0f, 15f, rain);
            // A front may lower the rain-bearing ceiling, but a high leading shield must not
            // lift low cloud; the shield renders through FrontBase/FrontTop.
            cloudBase = WeatherMath.Lerp(cloudBase, Math.Min(cloudBase, point.FrontBase), point.FrontCover);
            point.CloudBase = Scalar.Clamp(cloudBase, 250f, 3600f);
            float stratiformCover = Scalar.Clamp01(1f - (1f - coverBase) * clearClusters);
            CloudGenus genus = CloudShape.Resolve(p);
            float deck = WeatherMath.Lerp(genus.PuffDepth, Math.Max(genus.PuffDepth, p.LayerDepth), genus.SheetBlend);
            deck = Math.Max(280f, deck);
            float crown = top;
            // Fair and stratocumulus stay at the genus depth. A cumulonimbus keeps the cell's full column.
            crown = WeatherMath.Lerp(Math.Min(crown, CloudShape.TowerCap(point.CloudBase, genus, p.LayerDepth)), crown, genus.TowerBlend);
            point.LowTop = Math.Max(point.CloudBase + deck * (0.5f + 0.5f * stratiformCover), crown);
            point.CloudTop = Math.Max(point.LowTop,
                WeatherMath.Lerp(point.CloudBase, point.FrontTop, point.FrontCover));

            float haze = Math.Max(p.HazeKm * HazeScale, 0.5f);
            float extinction = 3.912f / haze + 0.25f * (float)Math.Pow(rain, 0.66);
            point.VisibilityKm = Scalar.Clamp(3.912f / extinction, 0.3f, 50f);

            point.WindX = windX + outX;
            point.WindZ = windZ + outZ;
            point.WindUp = vertical;
            float outflow = (float)Math.Sqrt(outX * outX + outZ * outZ);
            point.Turbulence = Scalar.Clamp01(turbulence);
            point.Gust = 0.35f * windSpeed * point.Turbulence + outflow;
            point.LightningRate = lightning;
            point.CoreDepth = Scalar.Clamp01(core);
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
            float humid = Scalar.Clamp01(point.Cover * 0.6f + WeatherMath.Smoothstep(0f, 5f, rain) * 0.6f);
            point.Dewpoint = point.Temperature - WeatherMath.Lerp(9f, 0.5f, humid);
            return point;
        }

        /// <summary>The regional sky at the map centre.</summary>
        public WeatherPoint Regional() => Sample(0f, 0f);

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

        /// <summary>The static patch field in [0, 1] that breaks up area rain and cover.</summary>
        public float Patch(float x, float z)
            => WeatherMath.Fbm(Key.Seed ^ 0x5eedu, x / PatchScale, z / PatchScale);

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
    }

    /// <summary>A rain-free cumulus group at a fixed site of the layout. The state's cumulus
    /// level decides how many groups are out (by rank) and how strongly each one builds.</summary>
    internal struct DryCloudCluster
    {
        public const int MaxCount = 8;
        public int Slot;
        public float X, Z, Radius, Base, Top, Strength, AxisX, AxisZ, Aspect;

        public float CoverAt(float x, float z)
        {
            float dx = x - X, dz = z - Z;
            if (dx * dx + dz * dz > Radius * Radius * 4f) return 0f;
            float along = (dx * AxisX + dz * AxisZ) / Math.Max(1f, Aspect);
            float across = (-dx * AxisZ + dz * AxisX) / 0.72f;
            float r = Radius;
            float shape = Math.Max(Blob(along, across, r * 0.50f),
                Math.Max(Blob(along - r * 0.48f, across - r * 0.18f, r * 0.38f),
                Math.Max(Blob(along + r * 0.49f, across + r * 0.08f, r * 0.38f),
                Math.Max(Blob(along - r * 0.10f, across - r * 0.52f, r * 0.35f),
                         Blob(along + r * 0.16f, across + r * 0.51f, r * 0.35f)))));
            return shape * Math.Min(1f, Strength * 2.5f);
        }

        private static float Blob(float x, float z, float radius)
        {
            float q = 1f - (x * x + z * z) / (radius * radius);
            return q > 0f ? q * q : 0f;
        }

        public static bool TryResolve(uint layout, int slot, StateParams sky, float halfX, float halfZ,
            out DryCloudCluster cloud, float windX = 0f, float windZ = 0f)
        {
            cloud = default;
            uint seed = unchecked(layout ^ (uint)(slot * 73856093) ^ 0x7c15u);
            float rank = (slot + WeatherMath.Hash01(seed, 70)) / MaxCount;
            float strength = WeatherMath.Smoothstep(rank * 0.7f, rank * 0.7f + 0.35f, sky.Cumulus) *
                Scalar.Clamp01(0.4f + 0.6f * sky.Cumulus);
            // Keep the tail until zero: CoverAt magnifies strength.
            if (strength <= 0f) return false;
            cloud.Slot = slot;
            cloud.X = WeatherMath.HashRange(seed, 73, 0, 0, -halfX * 0.85f, halfX * 0.85f);
            cloud.Z = WeatherMath.HashRange(seed, 74, 0, 0, -halfZ * 0.85f, halfZ * 0.85f);
            cloud.Radius = WeatherMath.HashRange(seed, 75, 0, 0, 5000f, 9500f);
            cloud.Base = sky.CloudBase;
            float puff = sky.PuffDepth >= 200f ? sky.PuffDepth : 900f;
            cloud.Top = cloud.Base + WeatherMath.HashRange(seed, 76, 0, 0, 0.65f, 1.1f) * puff *
                (0.75f + 0.25f * sky.Cumulus);
            cloud.Strength = strength;
            float angle = windX * windX + windZ * windZ > 0.001f
                ? (float)Math.Atan2(windZ, windX) + WeatherMath.HashRange(seed, 77, 0, 0, -0.28f, 0.28f)
                : WeatherMath.Hash01(seed, 77) * 2f * (float)Math.PI;
            cloud.AxisX = (float)Math.Cos(angle);
            cloud.AxisZ = (float)Math.Sin(angle);
            cloud.Aspect = WeatherMath.HashRange(seed, 78, 0, 0, 1.05f, 1.45f);
            return true;
        }
    }
}
