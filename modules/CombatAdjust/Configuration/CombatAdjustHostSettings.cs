using System;
using System.Globalization;
using BoscaliSummer.Core.Ui;

namespace BoscaliSummer.Modules.CombatAdjust.Configuration
{
    /// <summary>The one COMBAT table on SET SERVER. Rows an external combat overhaul owns read as unavailable while it is loaded.</summary>
    internal static class CombatAdjustHostSettings
    {
        internal const string ExternalOwns = "ANOTHER MOD OWNS THIS";

        internal static HostSettingsTable Build(CombatAdjustSettings s, Func<bool> external)
        {
            Func<string> owned = () => external() ? ExternalOwns : null;
            return new HostSettingsTable("COMBAT")
                .Toggle(1, s.LinkDiscipline, "LINK DISCIPLINE",
                    "Radars and aircraft share what they see at once; ground and ship lookouts report distant contacts only now and then.", owned)
                .Toggle(2, s.TrackFog, "TRACK FOG",
                    "Contacts nobody is reporting drift, further the longer they go unseen. Contacts being reported stay exact.", owned)
                .Toggle(3, s.ArmourSmoke, "ARMOUR SMOKE",
                    "Armoured vehicles pop smoke at laser, TV and IR guided weapons; the smoke breaks those locks.")
                .Toggle(4, s.ShipDecoys, "SHIP DECOYS",
                    "Ships fire chaff and decoy salvos at guided missiles coming for them.")
                .Toggle(5, s.SamEmcon, "SAM EMCON",
                    "Ground radars switch off when an anti-radiation missile comes for them.")
                .Number(10, s.LinkChance, "LINK CHANCE", "Chance a lookout's distant sighting reaches the picture on each scan.", 0.05f, Percent, owned)
                .Number(11, s.LinkCloseRangeKm, "CLOSE RANGE", "Lookouts always report contacts closer than this.", 0.5f,
                    v => v.ToString("0.0", CultureInfo.InvariantCulture) + " KM", owned)
                .Number(12, s.FogScale, "FOG SCALE", "How far unreported contacts drift.", 0.25f, Times, owned)
                .Number(13, s.TrackExpirySeconds, "TRACK MEMORY", "Unseen ground and sea contacts leave the picture after this; aircraft after a third.", 30f,
                    v => v <= 0f ? "OFF" : v.ToString("0", CultureInfo.InvariantCulture) + " S", owned)
                .Number(14, s.MissileFailChance, "MISSILE FAILURES", "Share of guided missiles that fail on their own.", 0.01f, Percent, owned)
                .Number(15, s.CountermeasureScale, "COUNTERMEASURES", "How much stronger flares, chaff and jamming are.", 0.25f, Times, owned);
        }

        private static string Percent(float v) => (v * 100f).ToString("0", CultureInfo.InvariantCulture) + " %";
        private static string Times(float v) => "X" + v.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
