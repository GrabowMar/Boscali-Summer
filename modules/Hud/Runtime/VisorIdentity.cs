using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Hud.Patches;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>Who sits in the seat, in the words the visor etches and the boot's closing line uses: the pilot's callsign and the airframe's type, capitals, short. Read when the aircraft changes, not per frame.</summary>
    internal static class VisorIdentity
    {
        public const int CallsignMax = 14, AirframeMax = 18;

        /// <summary>The SQD profile's callsign, else the name the game's chat writes for the local player; "PILOT" when neither is known.</summary>
        public static string Callsign()
        {
            string name = null;
            if (ModuleServices.TryGet(out ISquadView squad) && !(squad is Object gone && gone == null)) name = squad.Pilot.Callsign;
            if (string.IsNullOrWhiteSpace(name)) name = EventFeed.OwnPlayer();
            return Clean(name, CallsignMax, "PILOT");
        }

        /// <summary>The airframe's type as the game names it ("FS-20 VORTEX"); the unit's own name when the definition has none; "" when unknown.</summary>
        public static string Airframe(Aircraft aircraft)
        {
            if (aircraft == null) return "";
            string name = aircraft.definition != null ? aircraft.definition.unitName : null;
            if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrEmpty(aircraft.unitName))
            {
                name = aircraft.unitName;
                int open = name.LastIndexOf(" [", System.StringComparison.Ordinal);
                if (open >= 0 && name.EndsWith("]", System.StringComparison.Ordinal)) name = name.Substring(open + 2, name.Length - open - 3);
            }
            return Clean(name, AirframeMax, "");
        }

        private static string Clean(string s, int max, string fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            s = s.Trim().ToUpperInvariant();
            return s.Length > max ? s.Substring(0, max) : s;
        }
    }
}
