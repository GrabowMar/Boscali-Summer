namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Presentation-level seam for reading and flipping the visual layer from a settings console
    /// (the MFD SET page) without importing the Visuals implementation. Setters persist to config.
    /// </summary>
    internal interface IVisualEnhancements
    {
        bool IsEnabled { get; }
        bool CinematicPostFxEnabled { get; set; }
        float BloomBoost { get; set; }
        bool SharpenEnabled { get; set; }
        float SharpenStrength { get; set; }
        bool GForceEffectsEnabled { get; set; }
        bool FoliageDynamicsEnabled { get; set; }
        float FoliageSwayStrength { get; set; }
    }
}
