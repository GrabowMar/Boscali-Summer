namespace BoscaliSummer.Features.Intel.Domain
{
    /// <summary>
    /// Pre-war intel (switch Intel.PreWarIntel, default on): 30 s after the mission starts,
    /// each faction learns the OTHER factions' fixed, mission-placed air-defence installations
    /// — static SAMs, AD emplacements and the radar carriers of their sites. It is intelligence
    /// on fixed sites, not tracking: mobile units, factory output and garrisons are never
    /// pre-war.
    /// </summary>
    internal static class PreWarRules
    {
        public const float SeedDelaySeconds = 30f;

        public static bool Qualifies(bool missionPlaced, bool isStatic, UnitClass unitClass, bool airDefence) =>
            missionPlaced && isStatic && airDefence &&
            (unitClass == UnitClass.GroundVehicle || unitClass == UnitClass.Building);

        /// <summary>
        /// An unconfirmed pre-war site that has died leaves the picture only once the observer
        /// has looked at its cell after the death. Until then it stays known: conservative,
        /// never a leak. (A site that died while tracked is removed by vanilla's onForgetUnit.)
        /// </summary>
        public static bool ShouldDrop(float deadSince, float lastStamp) =>
            !float.IsNaN(deadSince) && !float.IsNaN(lastStamp) && lastStamp > deadSince;
    }
}
