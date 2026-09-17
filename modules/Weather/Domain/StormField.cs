using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// The storm population. Cells are born on a ring around the map centre at a bearing and
    /// radius drawn from the mission seed, then advect with the deterministic front wind. Every
    /// value here is a function of (seed, mission time, map size, front, atmosphere) alone, so a
    /// host and every client — including a late joiner — place the same storms in the same places
    /// forever, with no storm data on the wire.
    ///
    /// Slots are staggered by <see cref="SlotStagger"/>, so one cell is always being born while
    /// the others mature or die: a stream of weather rather than a synchronised batch.
    ///
    /// The atmosphere decides how the population is arranged (<see cref="StormMode"/>): a lone
    /// cell in marginal instability, scattered cells in ordinary convection, and — once CAPE and
    /// shear are both high — a cluster, or a line laid along a convective front. A stable air mass
    /// raises nothing at all.
    /// </summary>
    internal static class StormField
    {
        public const int MaxCells = 8;

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

        /// <summary>Below this CAPE the air is stable and raises no cells at all.</summary>
        public const float MinStormCape = 0.20f;

        /// <summary>CAPE and shear at or above these organise the population.</summary>
        public const float OrganisedCape = 0.55f;
        public const float OrganisedShear = 0.50f;

        /// <summary>Below this energy the population is a single cell; above it, scattered cells.</summary>
        public const float IsolatedEnergy = 0.35f;

        /// <summary>A cell far from any front keeps this fraction of its strength.</summary>
        public const float FrontFloor = 0.30f;

        /// <summary>Cluster cells sit within this fraction of their spawn radius of the centre.</summary>
        public const float ClusterSpreadFraction = 0.35f;

        /// <summary>Degrees per second the cluster centre drifts, so the group moves without popping.</summary>
        public const float ClusterDriftDegreesPerSecond = 0.02f;

        /// <summary>Squall-line spacing along the front, as a fraction of the map size.</summary>
        public const float SquallSpacingFraction = 0.06f;

        public static float SlotStagger => Period / MaxCells;

        /// <summary>
        /// Legacy overload: no front and no atmosphere. It stays while the integration pass still
        /// calls it, and retires with that pass — the context overload below is where the new
        /// population lives. Delegating with <see cref="WeatherFront.None"/> and
        /// <see cref="Atmosphere.Unavailable"/> keeps its cells on the birth ring as before.
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
            return Fill(
                destination, seed, missionTime, mapSize, frontConditions, windHeading, windSpeed,
                WeatherFront.None, Atmosphere.Unavailable, out _);
        }

        /// <summary>
        /// Fill <paramref name="destination"/> with the cells alive at <paramref name="missionTime"/>
        /// and report how the population is arranged in <paramref name="mode"/>. Returns how many
        /// were written. Allocation-free; the caller owns the array.
        /// </summary>
        public static int Fill(
            StormCell[] destination,
            int seed,
            float missionTime,
            float mapSize,
            float frontConditions,
            float windHeading,
            float windSpeed,
            in WeatherFront front,
            in Atmosphere atmosphere,
            out StormMode mode)
        {
            mode = StormMode.None;
            if (destination == null) return 0;
            int capacity = destination.Length < MaxCells ? destination.Length : MaxCells;
            if (capacity <= 0) return 0;
            if (float.IsNaN(missionTime) || missionTime < 0f) missionTime = 0f;
            if (float.IsNaN(mapSize) || float.IsInfinity(mapSize) || mapSize <= 1000f) return 0;
            if (float.IsNaN(frontConditions)) frontConditions = 0f;
            if (float.IsNaN(windHeading)) windHeading = 0f;
            if (float.IsNaN(windSpeed) || windSpeed < 0f) windSpeed = 0f;

            float energy = Energy(frontConditions, in atmosphere);
            if (energy <= 0f) return 0;
            if (atmosphere.Available &&
                (atmosphere.Cape < MinStormCape || !AirMasses.IsUnstable(atmosphere.AirMass))) return 0;

            mode = ModeOf(energy, in front, in atmosphere);

            float stagger = SlotStagger;
            float windX = (float)Math.Sin(windHeading * (Math.PI / 180.0));
            float windZ = (float)Math.Cos(windHeading * (Math.PI / 180.0));
            float spacing = mapSize * SquallSpacingFraction;

            int count = 0;
            for (int slot = 0; slot < capacity; slot++)
            {
                if (mode == StormMode.Isolated && slot != 0) continue;

                float phase = slot * stagger;
                float cycles = (float)Math.Floor((missionTime - phase) / Period);
                int cycle = (int)cycles;
                float birth = phase + cycles * Period;
                float age = missionTime - birth;
                if (age < 0f || age >= Period) continue;

                float travel = windSpeed * age * AdvectFactor;
                float velocityX = windX * windSpeed;
                float velocityZ = windZ * windSpeed;
                float x;
                float z;

                if (mode == StormMode.SquallLine)
                {
                    // Laid along the boundary, slightly on its leading side. The front's own
                    // position carries the line across the map; nothing else moves it.
                    float along = (slot - (capacity - 1) * 0.5f) * spacing;
                    along += (Unit(Mix3(seed, slot, cycle, 0x9Du)) - 0.5f) * spacing;
                    float lead = SquallLead(mapSize);
                    x = front.NormalX * front.Position - front.NormalZ * along + front.NormalX * lead;
                    z = front.NormalZ * front.Position + front.NormalX * along + front.NormalZ * lead;
                    velocityX = front.NormalX * front.Speed;
                    velocityZ = front.NormalZ * front.Speed;
                }
                else if (mode == StormMode.Cluster)
                {
                    // One centre for the whole population, drifting slowly so it moves without
                    // popping. Every cell sits within a fraction of the drawn spawn radius of it.
                    float centreRoll = Unit(Mix3(seed, 0, 0, 0xA3u));
                    float centreBearing = Unit(Mix3(seed, 0, 0, 0xB7u)) * 360f
                        + missionTime * ClusterDriftDegreesPerSecond;
                    float centreRadius =
                        mapSize * (SpawnRadiusMin + (SpawnRadiusMax - SpawnRadiusMin) * centreRoll);
                    float spread = Unit(Mix3(seed, slot, cycle, 0xC1u)) * ClusterSpreadFraction * centreRadius;
                    float spreadBearing = Unit(Mix3(seed, slot, cycle, 0xD3u)) * 360f;
                    x = (float)Math.Sin(centreBearing * (Math.PI / 180.0)) * centreRadius
                        + (float)Math.Sin(spreadBearing * (Math.PI / 180.0)) * spread
                        + windX * travel;
                    z = (float)Math.Cos(centreBearing * (Math.PI / 180.0)) * centreRadius
                        + (float)Math.Cos(spreadBearing * (Math.PI / 180.0)) * spread
                        + windZ * travel;
                }
                else
                {
                    float spawnRoll = Unit(Mix3(seed, slot, cycle, 0x71u));
                    float bearing = Unit(Mix3(seed, slot, cycle, 0x83u)) * 360f;
                    float spawnRadius = mapSize * (SpawnRadiusMin + (SpawnRadiusMax - SpawnRadiusMin) * spawnRoll);

                    x = (float)Math.Sin(bearing * (Math.PI / 180.0)) * spawnRadius + windX * travel;
                    z = (float)Math.Cos(bearing * (Math.PI / 180.0)) * spawnRadius + windZ * travel;
                }

                x = ClampAbs(x, mapSize);
                z = ClampAbs(z, mapSize);

                float intensity = IntensityAt(seed, slot, cycle, age, energy, in front, x, z);
                if (intensity <= 0f) continue;

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
                    velocityX,
                    velocityZ);
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
        /// Convective energy: the front's conditions, or the atmosphere's own instability when
        /// that is greater. The legacy path has no atmosphere, so this is exactly the old term.
        /// </summary>
        private static float Energy(float frontConditions, in Atmosphere atmosphere)
        {
            float energy = WeatherRegimes.Clamp01((frontConditions - 0.10f) / 0.75f);
            if (atmosphere.Available && atmosphere.Cape > energy) energy = atmosphere.Cape;
            return energy;
        }

        private static StormMode ModeOf(float energy, in WeatherFront front, in Atmosphere atmosphere)
        {
            bool organised = atmosphere.Available &&
                             atmosphere.Cape >= OrganisedCape &&
                             atmosphere.Shear >= OrganisedShear;
            if (organised && front.Present && FrontKinds.IsConvective(front.Kind)) return StormMode.SquallLine;
            if (organised) return StormMode.Cluster;
            if (atmosphere.Available && energy < IsolatedEnergy) return StormMode.Isolated;
            return StormMode.Scattered;
        }

        /// <summary>Leading-side offset of a squall line, never more than a fraction of the map.</summary>
        private static float SquallLead(float mapSize) => mapSize * 0.05f;

        /// <summary>
        /// A cell exists at all only when there is energy for one: a clear front raises nothing,
        /// a storm front raises the biggest cells the slot can produce. Once a front is present,
        /// its proximity is a multiplier — a cell grows as the boundary closes and peaks at it.
        /// </summary>
        private static float IntensityAt(
            int seed,
            int slot,
            int cycle,
            float age,
            float energy,
            in WeatherFront front,
            float x,
            float z)
        {
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
            float intensity = target * (shape * shape * (3f - 2f * shape));
            if (front.Present) intensity *= FrontFloor + (1f - FrontFloor) * front.InfluenceAt(x, z);
            return intensity;
        }

        private static StormKind KindAt(int seed, int slot, int cycle, float intensity, float frontConditions)
        {
            if (frontConditions >= 0.80f && intensity >= 0.65f && Unit(Mix3(seed, slot, cycle, 0xB5u)) < 0.75f)
                return StormKind.Supercell;
            if (intensity >= 0.45f) return StormKind.ToweringCumulus;
            return StormKind.Cumulus;
        }

        private static float ClampAbs(float value, float limit)
        {
            if (float.IsNaN(value)) return 0f;
            if (value < -limit) return -limit;
            return value > limit ? limit : value;
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
