namespace BoscaliSummer.Runtime
{
    /// <summary>
    /// Stable labels for Boscali Summer's maximised-map MFD screens. Each value is both
    /// an <c>MFDScreen.shortName</c> and its shared <c>NOAvionics.BezelRegistry</c> claim key.
    /// </summary>
    internal static class MfdSlots
    {
        public const string Ops = "OPS";
        public const string Sqd = "SQD";
        public const string Str = "STR";
        public const string Rad = "RAD";
        public const string Set = "SET";
        public const string Events = "EVN";
        public const string Comms = "COM";
        public const string Weather = "ENV";
    }
}
