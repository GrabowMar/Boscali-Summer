namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal enum VanguardRole { None, Decoy, Jammer, Drone, Glider, Interceptor, Carrier, Torpedo, Towed }

    /// <summary>Unit jsonKeys baked into Vanguard.nobp by VanguardBuilder. Renaming one is a save/MP break.</summary>
    internal static class VanguardKeys
    {
        public const string MaldX = "VG_MaldX";
        public const string MaldJ = "VG_MaldJ";
        public const string Remora = "VG_Remora";
        public const string HawcX = "VG_HawcX";
        public const string AegisDart = "VG_AegisDart";
        public const string Glaive2A = "VG_Glaive2A";
        public const string Glaive2S = "VG_Glaive2S";
        public const string Orca = "VG_Orca";
        public const string AleX = "VG_AleX";
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
                case Glaive2A:
                case Glaive2S: return VanguardRole.Carrier;
                case Orca: return VanguardRole.Torpedo;
                case AleX: return VanguardRole.Towed;
                default: return VanguardRole.None;
            }
        }

        /// <summary>Vanilla vehicle jsonKey a GLAIVE carrier deploys (two per carrier), or null.</summary>
        public static string PayloadOf(string jsonKey)
        {
            switch (jsonKey)
            {
                case Glaive2A: return "UGV1_grenade";  // Hexhound GMG
                case Glaive2S: return "UGV1_SAMx1";    // Hexhound SAM
                default: return null;
            }
        }
    }
}
