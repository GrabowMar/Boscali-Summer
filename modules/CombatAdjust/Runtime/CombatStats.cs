namespace BoscaliSummer.Modules.CombatAdjust.Runtime
{
    /// <summary>What the rules did this mission, for the dev sim hook. Plain counters, reset with the scene.</summary>
    internal static class CombatStats
    {
        public static int MissilesSeen, LinkShared, LinkDropped, Duds, Wanders, SmokePops, SmokeBlinds, DecoySalvos, Seduced, RadarsDark, Expired;

#if PUBLIC_RELEASE
        public const bool ForceReactions = false;
#else
        /// <summary>Sim only: every smoke, decoy and EMCON roll succeeds, so a targeted scenario is deterministic.</summary>
        public static bool ForceReactions;
#endif

        public static void Reset() =>
            MissilesSeen = LinkShared = LinkDropped = Duds = Wanders = SmokePops = SmokeBlinds = DecoySalvos = Seduced = RadarsDark = Expired = 0;
    }
}
