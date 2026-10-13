namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The two terrain pictures the visor can draw (Command owns them), for a settings surface that must not import Command: the holo map the map key
    /// stops at, and the ground ghost over the cockpit view. Client-local; setters persist to config and apply at once.
    /// </summary>
    internal interface IVisorTerrainSettings
    {
        bool HoloMap { get; set; }
        /// <summary>Strength of the ground ghost, 0 (off) to 1.</summary>
        float TerrainGhost { get; set; }
    }
}
