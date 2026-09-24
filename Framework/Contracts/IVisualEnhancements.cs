namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Presentation-level seam for querying and adjusting visual enhancements
    /// from settings consoles (e.g. MFD SET panel) without importing Visuals implementation.
    /// </summary>
    internal interface IVisualEnhancements
    {
        bool IsEnabled { get; }
        bool CinematicPostFxEnabled { get; set; }
        bool GForceEffectsEnabled { get; set; }
        bool MotionBlurEnabled { get; set; }
        bool FoliageDynamicsEnabled { get; set; }
        float BloomIntensity { get; set; }
        float FoliageSwayStrength { get; set; }
    }
}
