namespace BoscaliSummer.Modules.Wing.Configuration
{
    // The wing's live settings, set by WingModule.Install from ModConfiguration.
    // Wing logic reads through here instead of threading a reference through every
    // call site; ModConfiguration remains the only place that binds the entries.
    internal static class WingSettings
    {
        internal static WingConfig Instance { get; set; }
    }
}
