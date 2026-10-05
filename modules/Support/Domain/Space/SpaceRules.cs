namespace BoscaliSummer.Modules.Support.Domain.Space
{
    internal enum BirdKind : byte { Optical, Radar, Kinetic }
    internal enum BirdTask : byte { Scan, Mti, Camera, Rod }
    internal enum SpaceFamilyState : byte { Normal, Degraded, Dark }
    internal enum SpaceBirdRequirement : byte { None, Optical, Radar, Kinetic, OpticalOrRadar }

    internal static class SpaceRules
    {
        public const int BirdCount = 3, TaskCount = 4;
        public const float DamagedHealth = 0.5f, DamagedCooldownFactor = 1.25f, DarkGraceSeconds = 120f;
        /// <summary>Space spec 5.2: a killed bird returns through a fund bar of 400 CR (HQ FUND pays it) and then a 6 minute build.</summary>
        public const float BirdRebuildGoal = 400f, BirdBuildSeconds = 360f;

        public static float Cooldown(BirdTask task) => task switch
        {
            BirdTask.Scan => 90f,
            BirdTask.Mti => 150f,
            BirdTask.Camera => 120f,
            BirdTask.Rod => 600f,
            _ => 0f
        };

        public static bool TryBird(BirdTask task, out BirdKind bird)
        {
            switch (task)
            {
                case BirdTask.Scan:
                case BirdTask.Mti: bird = BirdKind.Radar; return true;
                case BirdTask.Camera: bird = BirdKind.Optical; return true;
                case BirdTask.Rod: bird = BirdKind.Kinetic; return true;
                default: bird = default; return false;
            }
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool MissionTime(float value) => Finite(value) && value >= 0f;
    }
}
