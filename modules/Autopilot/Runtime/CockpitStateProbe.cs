using System;
using HarmonyLib;

namespace BoscaliSummer.Features.Autopilot.Runtime
{
    /// <summary>
    /// Cached read-only reflection for the few vanilla toggles that keep their state private
    /// (nav lights, night vision, linked guns), so the interaction menu can print ON / OFF. A
    /// missing member only blanks that state line; the toggle itself still works.
    /// </summary>
    internal static class CockpitStateProbe
    {
        private static AccessTools.FieldRef<NavLights, bool> navLightsOn;
        private static AccessTools.FieldRef<NightVision, bool> nightVisionSelected;
        private static AccessTools.FieldRef<WeaponManager, bool> gunsLinked;
        private static bool initialised;

        private static Aircraft navAircraft;
        private static NavLights navLights;

        public static void Initialise()
        {
            if (initialised) return;
            initialised = true;
            navLightsOn = Ref<NavLights>("isOn");
            nightVisionSelected = Ref<NightVision>("nightVisSelected");
            gunsLinked = Ref<WeaponManager>("gunsLinked");
        }

        public static void Reset()
        {
            navAircraft = null;
            navLights = null;
        }

        public static bool? NavLightsOn(Aircraft aircraft)
        {
            if (navLightsOn == null || aircraft == null) return null;
            if (navAircraft != aircraft)
            {
                navAircraft = aircraft;
                navLights = aircraft.GetComponentInChildren<NavLights>();
            }
            return navLights != null ? navLightsOn(navLights) : (bool?)null;
        }

        public static bool HasNavLights(Aircraft aircraft)
        {
            NavLightsOn(aircraft);
            return navAircraft == aircraft && navLights != null;
        }

        public static bool? NightVisionOn() =>
            nightVisionSelected != null && NightVision.i != null ? nightVisionSelected(NightVision.i) : (bool?)null;

        public static bool? GunsLinked(WeaponManager manager) =>
            gunsLinked != null && manager != null ? gunsLinked(manager) : (bool?)null;

        private static AccessTools.FieldRef<T, bool> Ref<T>(string field)
        {
            try
            {
                return AccessTools.Field(typeof(T), field) == null ? null : AccessTools.FieldRefAccess<T, bool>(field);
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogWarning("Autopilot: interaction menu state " + typeof(T).Name + "." + field + " unavailable (" + e.Message + ")");
                return null;
            }
        }
    }
}
