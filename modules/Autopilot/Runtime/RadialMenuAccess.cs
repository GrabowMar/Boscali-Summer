using BoscaliSummer.Core.Game;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>The native radial wheel the autopilot entry drives, resolved once in
    /// Infrastructure GameAccess. A missing member disables the radial entry, never
    /// the module.</summary>
    internal static class RadialMenuAccess
    {
        public static bool Available => GameAccess.RadialAvailable;

        public static void Initialise()
        {
            if (!Available)
                Plugin.Logger?.LogWarning("Autopilot: native radial wheel left alone (" +
                    GameAccess.RadialUnavailableReason + ")");
        }

        public static RadialMenuAction[] GetActions(RadialMenuMain menu) => GameAccess.GetRadialActions(menu);

        public static void SetActions(RadialMenuMain menu, RadialMenuAction[] actions) => GameAccess.SetRadialActions(menu, actions);

        public static Aircraft GetAircraft(RadialMenuMain menu) => GameAccess.GetRadialAircraft(menu);

        public static void SetupMain(RadialMenuMain menu) => GameAccess.InvokeRadialSetupMain(menu);
    }
}
