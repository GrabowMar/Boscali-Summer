namespace BoscaliSummer.Features.Command.Domain
{
    /// <summary>
    /// Pure theater-AI bias. Boscali Command uses this so doctrine never retasks a
    /// wingman and never steers enemy analyzers. Multipliers are capped on purpose:
    /// the old 2.2–4.5× range made HQ doctrine louder than a standing order.
    ///
    /// <para>Was <c>NOAvionics.TheaterScoring</c> in the shared avionics folder. It is
    /// plain math with no cross-mod role, so it now lives here.</para>
    /// </summary>
    internal static class TheaterBias
    {
        public const float AirSuperiorityAir = 1.45f;
        public const float AirSuperiorityOther = 0.75f;
        public const float StrikeBuilding = 1.55f;
        public const float StrikeSurface = 1.25f;
        public const float SeadAntiAir = 1.60f;
        public const float CasGround = 1.40f;
        public const float PriorityTarget = 1.50f;

        public const float Minimum = 0.70f;
        public const float Maximum = 1.60f;

        public const int DoctrineBalanced = 0;
        public const int DoctrineAirSuperiority = 1;
        public const int DoctrineStrikeFocus = 2;
        public const int DoctrineSead = 3;
        public const int DoctrineCloseAirSupport = 4;

        public static float Bias(
            bool analyzerIsFriendly, bool targetIsWingman,
            int doctrine, bool priority,
            bool targetIsAircraft, bool targetIsBuilding, bool targetIsAntiAir)
        {
            if (!analyzerIsFriendly) return 1f;
            if (targetIsWingman) return 1f;

            float multiplier = 1f;
            switch (doctrine)
            {
                case DoctrineAirSuperiority:
                    multiplier *= targetIsAircraft ? AirSuperiorityAir : AirSuperiorityOther;
                    break;
                case DoctrineStrikeFocus:
                    if (targetIsBuilding) multiplier *= StrikeBuilding;
                    else if (!targetIsAircraft) multiplier *= StrikeSurface;
                    break;
                case DoctrineSead:
                    if (targetIsAntiAir) multiplier *= SeadAntiAir;
                    break;
                case DoctrineCloseAirSupport:
                    if (!targetIsAircraft && !targetIsBuilding) multiplier *= CasGround;
                    break;
            }

            if (priority) multiplier *= PriorityTarget;
            if (multiplier < Minimum) return Minimum;
            if (multiplier > Maximum) return Maximum;
            return multiplier;
        }
    }
}
