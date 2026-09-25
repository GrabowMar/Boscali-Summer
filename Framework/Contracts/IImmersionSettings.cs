namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Presentation-level seam for reading and flipping the cockpit-feel options from a settings
    /// console (the MFD SET page) without importing the Immersion implementation. Setters persist to config.
    /// </summary>
    internal interface IImmersionSettings
    {
        bool IsEnabled { get; }
        bool HeadMotionEnabled { get; set; }
        float HeadMotionStrength { get; set; }
        bool ExtraShakeEnabled { get; set; }
        float ShakeStrength { get; set; }
        bool SunGlareEnabled { get; set; }
    }
}
