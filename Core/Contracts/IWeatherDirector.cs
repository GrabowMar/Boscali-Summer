namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The host's hand on the weather, for a mission script that wants a sky for a scene. The weather a host sets travels to every player the way the weather
    /// console's does. Published by Weather; without it a script falls back to the game's own weather value.
    /// </summary>
    internal interface IWeatherDirector
    {
        /// <summary>
        /// Holds a set-piece sky: "storm" (a squall line with thunderstorm cells), "hurricane", "front", "fog" or "wave". False when the name is unknown, this
        /// peer is not the host, or no mission is running.
        /// </summary>
        bool Hold(string setPiece);

        /// <summary>Back to the changing weather, starting from the sky on screen.</summary>
        void Resume();

        /// <summary>Opens the weather console (the Ctrl+O window); read-only on a client.</summary>
        void OpenConsole();
    }
}
