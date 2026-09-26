using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Intel.Domain
{
    /// <summary>
    /// Known hostile ground power around a point: ground vehicles and buildings in the
    /// faction's own list, statics always, mobile units only if seen within 600 s.
    /// </summary>
    internal static class AreaIntelReader
    {
        public const float MobileMemorySeconds = 600f;

        public static AreaIntel Read(KnownHostileTable table, ObservationGrid grid, float x, float z, float radius, float now)
        {
            grid.Read(x, z, radius, now, out bool scouted, out float age);
            float power = 0f, antiTank = 0f, airDefence = 0f, artillery = 0f;
            float limit = radius * radius;
            for (int i = 0; i < table.Count; i++)
            {
                ref KnownHostile entry = ref table[i];
                if (entry.Class != UnitClass.GroundVehicle && entry.Class != UnitClass.Building) continue;
                if (!entry.Static && now - entry.SpottedAt > MobileMemorySeconds) continue;
                float dx = entry.X - x, dz = entry.Z - z;
                if (dx * dx + dz * dz > limit) continue;
                float weight = ForceRoles.GroundWeight(entry.Role);
                power += weight;
                switch (entry.Role)
                {
                    case ForceRole.AntiTank: antiTank += weight; break;
                    case ForceRole.AirDefence: airDefence += weight; break;
                    case ForceRole.Artillery: artillery += weight; break;
                }
            }
            return new AreaIntel(scouted, age, power, antiTank, airDefence, artillery);
        }
    }
}
