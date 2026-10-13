namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Internal flight, combat, and economy tuning with units and constraints. Player preferences
    /// belong in WingConfig; mode-dependent gating belongs in WingFidelity.</summary>
    internal static class WingTuning
    {

        /// <summary>Slot distance in metres defining the capture transition.</summary>
        public const float CaptureDistance = 300f;

        /// <summary>Slot-reach standing weapons range in metres. The aircraft does not manoeuvre to engage.</summary>
        public const float ReachSlotMetres = 6000f;

        /// <summary>Long-reach standing weapons range, and the cap for explicit Attack and Splash, in metres.</summary>
        public const float ReachLongMetres = 12000f;

        // Pilot progression uses a shared triangular rank curve.

        /// <summary>XP for credited target destruction.</summary>
        public const int XpPerKill = 25;

        /// <summary>XP for recovered sorties or completed cargo deliveries.</summary>
        public const int XpPerSortie = 40;

        /// <summary>Triangular rank step: Wingman 1, Veteran 3, Ace 6, Legend 10 steps.</summary>
        public const int XpPerRank = 120;
    }
}
