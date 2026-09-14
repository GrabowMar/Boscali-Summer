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
                    return "Mission-AI scoring only: +45% vs aircraft, −25% vs other. No CAP spawn. Wingmen excluded.";
                case CommandDoctrine.StrikeFocus:
                    return "Mission-AI scoring only: +55% vs buildings, +25% vs other surface. Wingmen excluded.";
                case CommandDoctrine.SEAD:
                    return "Mission-AI scoring only: +60% vs anti-air. Wingmen excluded.";
                case CommandDoctrine.CloseAirSupport:
                    return "Mission-AI scoring only: +40% vs ground units. Wingmen excluded.";
                default:
                    return "Mission-AI scoring only: no extra bias. Wingmen excluded.";
            }
        }
    }
}
