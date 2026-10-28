namespace BoscaliSummer.Modules.Vanguard.Domain
{
    internal enum VanguardRole { None, Decoy, Jammer, Drone, Glider, Interceptor, Torpedo, Towed, Sentinel, Gunship, Scout, Lander }

    /// <summary>Unit jsonKeys baked into the mod's Blueprinter bundle by VanguardBuilder. Renaming one is a save/MP break.</summary>
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
        public const string Kestrel = "VG_Kestrel";
        public const string KestrelMount = "VG_Kestrel_Sled"; // KS-01 launch sled (hold mount)
        public const string Hamilcar = "VG_Hamilcar";
        public const string AegisInfo = "WI_VG_Aegis"; // WeaponInfo asset name of the AEGIS pod
        public const string LanceInfo = "WI_VG_Lance";   // RG-12 LANCE railgun pod WeaponInfo
        public const string SkywellInfo = "WI_VG_Skywell";   // SKYWELL kit WeaponInfo (cargo MountedMissile)
        public const string SkywellMount = "VG_Skywell_Kit";

        // Runtime stores (Runtime/RuntimeStores): cloned from vanilla or bundle mounts at load, no bundle content.
        public const string TorchMount = "VG_Torch";           // SRB-2 TORCH strap-on boost rockets
        public const string SunspearMount = "VG_Sunspear";     // HEL-60 SUNSPEAR laser pod
        public const string StormcellMount = "VG_Stormcell";   // PCU-9 STORMCELL capacitor pod
        public const string WatchtowerMount = "VG_Watchtower"; // ASQ-X WATCHTOWER recon pod (IRST, ESM, MAWS, relay)
        public const string Manta = "VG_Manta";                // XQ-90 MANTA heavy escort drone (runtime missile clone of REMORA)
        public const string MantaMount = "VG_Manta_Wing";      // MANTA on its wing station; it blocks the neighbouring station

        /// <summary>GLAIVE family (gun drones carried as missiles).</summary>
        public static bool IsGunDrone(VanguardRole role) => role == VanguardRole.Sentinel || role == VanguardRole.Gunship;

        /// <summary>Hand-steerable from the CMenu (STEER DRONES BY HAND). AEGIS darts, the ORCA torpedo and the ALE-X kite are never hand-flown.</summary>
        public static bool IsSteerable(VanguardRole role) =>
            role == VanguardRole.Decoy || role == VanguardRole.Jammer || role == VanguardRole.Drone || role == VanguardRole.Glider ||
            role == VanguardRole.Sentinel || role == VanguardRole.Gunship || role == VanguardRole.Scout || role == VanguardRole.Lander;

        public static VanguardRole RoleOf(string jsonKey)
        {
            switch (jsonKey)
            {
                case MaldX: return VanguardRole.Decoy;
                case MaldJ: return VanguardRole.Jammer;
                case Remora:
                case Manta: return VanguardRole.Drone;
                case HawcX: return VanguardRole.Glider;
                case AegisDart: return VanguardRole.Interceptor;
                case Glaive2A: return VanguardRole.Sentinel;
                case Glaive2S: return VanguardRole.Gunship;
                case Orca: return VanguardRole.Torpedo;
                case AleX: return VanguardRole.Towed;
                case Kestrel: return VanguardRole.Scout;
                case Hamilcar: return VanguardRole.Lander;
                default: return VanguardRole.None;
            }
        }

    }
}
