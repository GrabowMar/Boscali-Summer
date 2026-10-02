namespace BoscaliSummer.Core.Contracts
{
    /// <summary>
    /// Presentation-level seam for reading and flipping the cockpit-feel options from a settings
    /// console (the MFD SET page) without importing the Immersion implementation. Setters persist to config.
    /// </summary>
    internal interface IImmersionSettings
    {
        bool IsEnabled { get; set; }
        bool HeadMotionEnabled { get; set; }
        float HeadMotionStrength { get; set; }
        bool ExtraShakeEnabled { get; set; }
        float ShakeStrength { get; set; }
        bool SunGlareEnabled { get; set; }
        bool MfdGlowEnabled { get; set; }
        bool AirframeAudioEnabled { get; set; }
        bool SurfaceImmersionEnabled { get; set; }
        bool GForceAudioEnabled { get; set; }
        bool WindAudioEnabled { get; set; }
        bool GVignetteEnabled { get; set; }
        bool MachBuffetEnabled { get; set; }
        bool PilotStrainAudioEnabled { get; set; }
    }
}
