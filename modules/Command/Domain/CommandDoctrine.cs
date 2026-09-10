namespace BoscaliSummer.Features.Command.Domain
{
    internal enum CommandDoctrine : byte
    {
        Balanced = 0,
        AirSuperiority = 1,
        StrikeFocus = 2,
        SEAD = 3,
        CloseAirSupport = 4
    }

    internal static class CommandDoctrineHelper
    {
        public const int RankObserver = 0;
        public const int RankPriorityTarget = 1;
        public const int RankDoctrine = 2;
        public const int RankReinforcement = 3;
        public const int RankSectorStrike = 4;
        public const int RankTheaterScramble = 5;

        public static string GetName(CommandDoctrine doctrine)
        {
            switch (doctrine)
            {
                case CommandDoctrine.AirSuperiority: return "AIR SUPERIORITY";
                case CommandDoctrine.StrikeFocus: return "STRATEGIC STRIKE";
                case CommandDoctrine.SEAD: return "SEAD / AIR DEFENSE";
                case CommandDoctrine.CloseAirSupport: return "CAS / GROUND BLITZ";
                default: return "BALANCED DOCTRINE";
            }
        }

        public static string GetDescription(CommandDoctrine doctrine)
        {
            switch (doctrine)
            {
                case CommandDoctrine.AirSuperiority:
                    return "Prioritizes enemy fighters and air threats (+200% air priority, defensive combat air patrols).";
                case CommandDoctrine.StrikeFocus:
                    return "Directs strike flights against enemy airbases, factories, depots, and power infrastructure.";
                case CommandDoctrine.SEAD:
                    return "Prioritizes suppression and destruction of enemy SAM sites, search radars, and AAA.";
                case CommandDoctrine.CloseAirSupport:
                    return "Focuses air assets on attacking enemy ground convoys, armor columns, and artillery.";
                default:
                    return "Standard tactical doctrine: autonomous balance across air and surface objectives.";
            }
        }

        public static int MaxPriorityTargets(int rank) => 3;

        public static bool CanSetDoctrine(int rank) => true;
        public static bool CanOrderSectorStrike(int rank) => false;
        public static bool CanOrderScramble(int rank) => false;
    }
}
