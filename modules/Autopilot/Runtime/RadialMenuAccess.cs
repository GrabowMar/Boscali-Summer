using System;
using HarmonyLib;

namespace BoscaliSummer.Features.Autopilot.Runtime
{
    /// <summary>Cached reflection into the native radial wheel: the action array the wheel
    /// rebuilds from, the cached aircraft passed to AllowedOnAircraft, and the private
    /// SetupMain rebuild. A missing member disables the radial entry, never the module.</summary>
    internal static class RadialMenuAccess
    {
        private static AccessTools.FieldRef<RadialMenuMain, RadialMenuAction[]> actionsMain;
        private static AccessTools.FieldRef<RadialMenuMain, Aircraft> menuAircraft;
        private static Action<RadialMenuMain> setupMain;

        public static bool Available { get; private set; }

        public static void Initialise()
        {
            try
            {
                actionsMain = AccessTools.FieldRefAccess<RadialMenuMain, RadialMenuAction[]>(
                    AccessTools.Field(typeof(RadialMenuMain), "actionsMain")
                    ?? throw new MissingFieldException(typeof(RadialMenuMain).FullName, "actionsMain"));
                menuAircraft = AccessTools.FieldRefAccess<RadialMenuMain, Aircraft>(
                    AccessTools.Field(typeof(RadialMenuMain), "aircraft")
                    ?? throw new MissingFieldException(typeof(RadialMenuMain).FullName, "aircraft"));
                setupMain = AccessTools.MethodDelegate<Action<RadialMenuMain>>(
                    AccessTools.Method(typeof(RadialMenuMain), "SetupMain")
                    ?? throw new MissingMethodException(typeof(RadialMenuMain).FullName, "SetupMain"));
                Available = true;
            }
            catch (Exception e)
            {
                Available = false;
                Plugin.Logger?.LogWarning("Autopilot: native radial wheel left alone (" + e.Message + ")");
            }
        }

        public static RadialMenuAction[] GetActions(RadialMenuMain menu) => actionsMain(menu);

        public static void SetActions(RadialMenuMain menu, RadialMenuAction[] actions) => actionsMain(menu) = actions;

        public static Aircraft GetAircraft(RadialMenuMain menu) => menuAircraft(menu);

        public static void SetupMain(RadialMenuMain menu) => setupMain(menu);
    }
}
