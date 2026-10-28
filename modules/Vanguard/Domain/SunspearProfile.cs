namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>
    /// HEL-60 SUNSPEAR numbers. The vanilla 120 kW laser (laser_EW1) is the base; the pod trades its 15 km reach for a wider turret
    /// cone and more power, and leans to point defence. Vanilla's Laser deals fireDamage x curve x powerRatio per second.
    /// </summary>
    internal static class SunspearProfile
    {
        public const string Info = "WI_VG_Sunspear";
        public const string Name = "HEL-60 SUNSPEAR", ShortName = "SUNSPEAR";
        public const float Power = 160f;          // vanilla 120
        public const float MaxAngleDeg = 60f;     // turret cone around the pod's nose, vanilla 30
        public const float FireDamage = 45f;      // vanilla 40
        public const float RangeM = 12000f;       // damage reaches 0 here, vanilla 15 km
        public const float MassKg = 420f;
        public const float Cost = 1.5f;           // millions, the mount's emptyCost
        public const float AntiMissile = 0.45f;   // vanilla 0.2: the AI picks it for inbound missiles
        public const float AntiAir = 0.08f;       // vanilla 0.01: modest, it is not a fighter killer
        public const float MinBodyM = 2f;         // a template body shorter than this is nose hardware, not a pod

        /// <summary>Whether the cloned prefab needs a pod shell copied in (its own renderers are missing or only a nose).</summary>
        public static bool NeedsShell(bool hasBody, float bodyLengthM) => !hasBody || bodyLengthM < MinBodyM;
    }
}
