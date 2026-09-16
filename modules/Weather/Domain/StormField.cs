using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// The storm population. Cells are born on a ring around the map centre at a bearing and
    /// radius drawn from the mission seed, then advect with the deterministic front wind. Every
    /// value here is a function of (seed, mission time, map size, front wind) alone, so a host
    /// and every client — including a late joiner — place the same storms in the same places
    /// forever, with no storm data on the wire.
    ///
    /// Slots are staggered by a third of the period, so one cell is always being born while the
    /// others mature or die: a stream of weather rather than a synchronised batch.
    /// </summary>
    internal static class StormField
    {
        public const int MaxCells = 3;

        /// <summary>How long one cell lives, and therefore how often its slot re-rolls.</summary>
        public const float Period = 720f;

        /// <summary>Birth radius as a fraction of the map size, so storms arrive from off-map.</summary>
        public const float SpawnRadiusMin = 0.26f;
        public const float SpawnRadiusMax = 0.52f;

        /// <summary>How far a cell travels over its life, as a fraction of its advection budget.</summary>
        public const float AdvectFactor = 0.55f;

        /// <summary>A cell below this intensity raises no warnings at all.</summary>
        public const float MinWarningIntensity = 0.18f;

        /// <summary>Radius of a supercell tower, in metres. Big enough to read from the cockpit.</summary>
        public const float SupercellRadiusMin = 3200f;
        public const float SupercellRadiusMax = 7600f;

        /// <summary>Tower top above the cloud base. This is what makes a cell feel enormous.</summary>
        public const float SupercellTopMin = 5200f;
        public const float SupercellTopMax = 9600f;

        public const float CellTopMin = 1800f;
        public const float CellTopMax = 4600f;

        /// <summary>Intensity ramps up over this fraction of the life and decays over the tail.</summary>
        public const float GrowFraction = 0.22f;
        public const float DecayFraction = 0.28f;

        public static float SlotStagger => Period / MaxCells;

        /// <summary>
        /// Fill <paramref name="destination"/> with the cells alive at <paramref name="missionTime"/>.
        /// Returns how many were written. Allocation-free; the caller owns the array.
        /// </summary>
        public static int Fill(
            StormCell[] destination,
            int seed,
            float missionTime,
            float mapSize,
            float frontConditions,
            float windHeading,
            float windSpeed)
        {
            if (destination == null) return 0;
            int capacity = destination.Length < MaxCells ? destination.Length : MaxCells;
            if (capacity <= 0) return 0;
            if (float.IsNaN(missionTime) || missionTime < 0f) missionTime = 0f;
            if (float.IsNaN(mapSize) || mapSize <= 1000f) return 0;
            if (float.IsNaN(frontConditions)) frontConditions = 0f;
            if (float.IsNaN(windHeading)) windHeading = 0f;
            if (float.IsNaN(windSpeed) || windSpeed < 0f) windSpeed = 0f;

            float stagger = SlotStagger;
            float windX = (float)Math.Sin(windHeading * (Math.PI / 180.0));
            float windZ = (float)Math.Cos(windHeading * (Math.PI / 180.0));

            int count = 0;
            for (int slot = 0; slot < capacity; slot++)
            {
                float phase = slot * stagger;
                float cycles = (float)Math.Floor((missionTime - phase) / Period);
                int cycle = (int)cycles;
                float birth = phase + cycles * Period;
                float age = missionTime - birth;
                if (age < 0f || age >= Period) continue;

                float intensity = IntensityAt(seed, slot, cycle, age, frontConditions);
                if (intensity <= 0f) continue;

                float spawnRoll = Unit(Mix3(seed, slot, cycle, 0x71u));
                float bearing = Unit(Mix3(seed, slot, cycle, 0x83u)) * 360f;
                float spawnRadius = mapSize * (SpawnRadiusMin + (SpawnRadiusMax - SpawnRadiusMin) * spawnRoll);

                float birthX = (float)Math.Sin(bearing * (Math.PI / 180.0)) * spawnRadius;
                float birthZ = (float)Math.Cos(bearing * (Math.PI / 180.0)) * spawnRadius;

                float travel = windSpeed * age * AdvectFactor;
                float x = birthX + windX * travel;
                float z = birthZ + windZ * travel;

                StormKind kind = KindAt(seed, slot, cycle, intensity, frontConditions);
                float span = Unit(Mix3(seed, slot, cycle, 0x97u));
                float radius = kind == StormKind.Supercell
                    ? WeatherState.Lerp(SupercellRadiusMin, SupercellRadiusMax, span)
                    : WeatherState.Lerp(SupercellRadiusMin * 0.28f, SupercellRadiusMin * 0.6f, span);

                // The base is absolute altitude and the tower is a height added to it, so a cell
                // can never top out below its own cloud base. Deriving them independently put a
                // weak cumulus' top under its base, which is not a cloud.
                float cloudBase = WeatherModel.ClampCloudBase(WeatherState.Lerp(2200f, 700f, intensity));
                float tower = kind == StormKind.Cumulus
                    ? WeatherState.Lerp(CellTopMin, CellTopMax * 0.6f, span)
                    : WeatherState.Lerp(SupercellTopMin, SupercellTopMax, span);

                destination[count++] = new StormCell(
                    slot,
                    kind,
                    x,
                    z,
                    radius,
                    intensity,
                    age,
                    Period,
                    cloudBase,
                    cloudBase + tower,
                    windX * windSpeed,
                    windZ * windSpeed);
            }

            return count;
        }

        /// <summary>
        /// The strongest local influence over the whole field, and which cell owns it. This is
        /// what rain, haze, the warning ring and the HUD all read.
        /// </summary>
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

        /// <summary>The highest warning ring any cell imposes on a point.</summary>
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

        /// <summary>
        /// A cell exists at all only when the front has enough energy for one: a clear front
        /// raises nothing, a storm front raises the biggest cells the slot can produce.
        /// </summary>
        private static float IntensityAt(int seed, int slot, int cycle, float age, float frontConditions)
        {
            float energy = WeatherRegimes.Clamp01((frontConditions - 0.10f) / 0.75f);
            if (energy <= 0f) return 0f;

            float peak = Unit(Mix3(seed, slot, cycle, 0x5Du));
            // The front sets the ceiling; the roll sets the cell inside it. A severe front
            // therefore always produces something worth flying around.
            float ceiling = WeatherState.Lerp(0.25f, 1f, energy);
            float target = WeatherState.Lerp(0.15f, ceiling, peak);

            float life = age / Period;
            float shape;
            if (life < GrowFraction) shape = life / GrowFraction;
            else if (life > 1f - DecayFraction) shape = (1f - life) / DecayFraction;
            else shape = 1f;
            shape = WeatherRegimes.Clamp01(shape);
            return target * (shape * shape * (3f - 2f * shape));
        }

        private static StormKind KindAt(int seed, int slot, int cycle, float intensity, float frontConditions)
        {
            if (frontConditions >= 0.80f && intensity >= 0.65f && Unit(Mix3(seed, slot, cycle, 0xB5u)) < 0.75f)
                return StormKind.Supercell;
            if (intensity >= 0.45f) return StormKind.ToweringCumulus;
            return StormKind.Cumulus;
        }

        private static uint Mix3(int seed, int a, int b, uint salt)
        {
            uint hash = (uint)seed ^ ((uint)a * 0x9E3779B9u) ^ ((uint)b * 0x85EBCA6Bu) ^ (salt * 0xC2B2AE35u);
            hash ^= hash >> 16;
            hash *= 0x7FEB352Du;
            hash ^= hash >> 15;
            hash *= 0x846CA68Bu;
            return hash ^ (hash >> 16);
        }

        private static float Unit(uint hash) => (hash >> 8) * (1f / 16777216f);
    }
}
