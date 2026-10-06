namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Internal flight, combat, and economy tuning with units and constraints. Player preferences
    /// belong in WingConfig; mode-dependent gating belongs in WingFidelity.</summary>
    internal static class WingTuning
    {
        public const float FormationInitialBraking = 2f;

        /// <summary>Maximum station-keeping heading correction, in degrees. Large corrections clamp
        /// well below this; the ceiling only matters while recovering a blown slot.</summary>
        public const float CommandAngle = 50f;

        // Leader prediction combines immediate lever intent with measured acceleration, which also
        // captures dives and turns.

        /// <summary>Seconds of acceleration prediction, balancing thrust-response lag against amplified
        /// throttle jitter. Selected from fixed-wing closed-loop simulation sweeps.</summary>
        public const float SpeedLeadSeconds = 0.75f;

        /// <summary>Credible acceleration bound in m/s², rejecting derivative spikes from respawns,
        /// collisions, and missed samples.</summary>
        public const float MaxCredibleAccel = 25f;

        /// <summary>Slot distance in metres defining the capture transition.</summary>
        public const float CaptureDistance = 300f;

        /// <summary>Slot-reach standing weapons range in metres. The aircraft does not manoeuvre to engage.</summary>
        public const float ReachSlotMetres = 6000f;

        /// <summary>Long-reach standing weapons range, and the cap for explicit Attack and Splash, in metres.</summary>
        public const float ReachLongMetres = 12000f;
        public const float CollisionHorizon = 6f;


        // Economy tuning.

        /// <summary>One-time command-right fee for active mission aircraft, as a fraction of list
        /// price.</summary>
        public const float RecruitmentCostRate = 0.25f;

        // Pilot progression uses a shared triangular rank curve.

        /// <summary>XP for credited target destruction.</summary>
        public const int XpPerKill = 25;

        /// <summary>XP for recovered sorties or completed cargo deliveries.</summary>
        public const int XpPerSortie = 40;

        /// <summary>Triangular rank step: Wingman 1, Veteran 3, Ace 6, Legend 10 steps.</summary>
        public const int XpPerRank = 120;

        /// <summary>Rank-effect scale; at full effect, Legend gains roughly 12% envelope and shot-cycle
        /// improvement over Rookie.</summary>
        public const float RankEffect = 1f;
    }
}
