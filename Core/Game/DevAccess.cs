namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// Whether the player accepted the cheating warning on the MISC screen this session. ARES and DEBUG stay locked until they do.
    /// Session only: never saved, so every game launch asks again.
    /// </summary>
    internal static class DevAccess
    {
        /// <summary>True from ACCEPT on the MISC screen's DEV ACCESS page until the game closes.</summary>
        internal static bool Granted { get; private set; }

        internal static void Accept() => Granted = true;

        internal static void Lock() => Granted = false;
    }
}
