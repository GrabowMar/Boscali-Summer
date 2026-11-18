namespace BoscaliSummer.Core.Contracts
{
    /// <summary>The climate of one of the mod's maps, read from its manifest (json keys = field names).</summary>
    internal sealed class ClimateProfile
    {
        public string profile = "";           // "tropical" | "alpine" | "arctic"
        public byte id;                        // 1 tropical, 2 alpine, 3 arctic
        public float seaLevelTempC = 21f, diurnalC = 5f, hazeScale = 1f;
    }

    internal static class MapClimate
    {
        /// <summary>The profile of the map that is loaded now; null on a vanilla map.</summary>
        internal static System.Func<ClimateProfile> Current = () => null;
    }
}
