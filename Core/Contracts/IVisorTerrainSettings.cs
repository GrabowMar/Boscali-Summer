namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// The visor's terrain picture (Command owns it), for a settings surface that must not import Command: the ground ghost over the cockpit view. Client-local; setters persist to config and apply at once.
    /// </summary>
    internal interface IVisorTerrainSettings
    {
        /// <summary>Strength of the ground ghost, 0 (off) to 1.</summary>
        float TerrainGhost { get; set; }
    }
}
