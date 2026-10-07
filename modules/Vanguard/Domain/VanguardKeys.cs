namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal enum VanguardRole { None, Decoy, Jammer, Drone, Glider, Interceptor }

    /// <summary>Unit jsonKeys baked into Vanguard.nobp by VanguardBuilder. Renaming one is a save/MP break.</summary>
    internal static class VanguardKeys
    {
        public const string MaldX = "VG_MaldX";
        public const string MaldJ = "VG_MaldJ";
        public const string Remora = "VG_Remora";
        public const string HawcX = "VG_HawcX";
        public const string AegisDart = "VG_AegisDart";
        public const string AegisInfo = "WI_VG_Aegis"; // WeaponInfo asset name of the AEGIS pod

        public static VanguardRole RoleOf(string jsonKey)
        {
            switch (jsonKey)
            {
                case MaldX: return VanguardRole.Decoy;
                case MaldJ: return VanguardRole.Jammer;
                case Remora: return VanguardRole.Drone;
                case HawcX: return VanguardRole.Glider;
                case AegisDart: return VanguardRole.Interceptor;
                default: return VanguardRole.None;
            }
        }
    }
}
