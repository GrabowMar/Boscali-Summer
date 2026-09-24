using System;

namespace BoscaliSummer.Features.Radio.Runtime
{
    internal enum RadioDaypart
    {
        Morning,
        Day,
        Evening,
        Night
    }

    /// <summary>
    /// The fiction layer: what each station says it is, what it claims to be airing, and a
    /// bounded rotation of wire copy for the status strip. All text is original flavour; no
    /// stored or transmitted music metadata passes through here.
    /// </summary>
    internal static class RadioProgramming
    {
        public const int MaximumBulletins = 12;

        private sealed class StationVoice
        {
            public string[] Shows;
            public string[] Bulletins;
        }

        private static readonly StationVoice Generic = new StationVoice
        {
            Shows = new[] { "Morning Signal", "Day Rotation", "Evening Session", "Night Carrier" },
            Bulletins = new[]
            {
                "Broadcasting from your local archive.",
                "You are listening to a local transmission.",
                "Signal strength nominal. Enjoy the rotation.",
                "This frequency is operated by the station owner.",
                "Requests are handled at the source.",
                "Thanks for tuning in. Stay on this frequency for the next set.",
                "Programming continues after this short break in transmission."
            }
        };

        private static readonly StationVoice Agrapol = new StationVoice
        {
            Shows = new[] { "Republic Dawn", "Open Assembly", "Harbor Voices", "The Night Commons" },
            Bulletins = new[]
            {
                "Boscali Republic Radio. The coast belongs to everyone who calls it home.",
                "The assembly meets at noon; the public gallery remains open.",
                "Harbor crews have cleared the relief convoy for departure.",
                "Volunteers are collecting blankets at the western ferry hall.",
                "Civilian traffic may use the south bridge after the morning inspection.",
                "This next song goes to the mechanics keeping the lights on.",
                "The election office reminds residents to check their registration.",
                "Keep the emergency lane clear for ambulances and fire crews.",
                "From the hills to the harbor, this is your republic on the air."
            }
        };

        private static readonly StationVoice Maris = new StationVoice
        {
            Shows = new[] { "First Order", "The State Hour", "Evening Directive", "Night Vigil" },
            Bulletins = new[]
            {
                "PALA State Radio. Order is the duty of every citizen.",
                "Travel permits are required at all eastern checkpoints.",
                "The Directorate confirms the curfew remains in effect.",
                "Factory shifts will report one hour before dawn.",
                "Unauthorized transmissions should be reported to the district office.",
                "The military council thanks the workers of the northern rail line.",
                "Civil defense drills begin at the second siren.",
                "Do not approach restricted airfields without written authorization.",
                "The state endures through discipline and vigilance."
            }
        };

        private static readonly StationVoice Base = new StationVoice
        {
            Shows = new[] { "First Watch", "Flight Line", "Shift Change", "Night Operations" },
            Bulletins = new[]
            {
                "Base Broadcast. Flight line checks are due before first launch.",
                "Tower, confirm the runway is clear for recovery traffic.",
                "Fuel teams report to the hardstands after the next landing.",
                "Maintain radio discipline on the operational channels.",
                "The ready room has posted the revised duty roster.",
                "Ground control reports a vehicle crossing on taxiway two.",
                "Medics and rescue crews remain on immediate standby.",
                "Night crews, inspect the approach lights before handover.",
                "All personnel, acknowledge the shelter drill when directed."
            }
        };

        public static RadioDaypart DaypartAt(DateTime when)
        {
            int hour = when.Hour;
            if (hour >= 5 && hour < 11) return RadioDaypart.Morning;
            if (hour >= 11 && hour < 17) return RadioDaypart.Day;
            if (hour >= 17 && hour < 22) return RadioDaypart.Evening;
            return RadioDaypart.Night;
        }

        public static string ProgramName(string stationId, DateTime when) =>
            Voice(stationId).Shows[(int)DaypartAt(when)];

        public static int BulletinCount(string stationId) =>
            Math.Min(MaximumBulletins, Voice(stationId).Bulletins.Length);

        public static string Bulletin(string stationId, int index)
        {
            string[] lines = Voice(stationId).Bulletins;
            if (lines.Length == 0) return string.Empty;
            int wrapped = ((index % lines.Length) + lines.Length) % lines.Length;
            return lines[wrapped];
        }

        private static StationVoice Voice(string stationId)
        {
            switch (stationId)
            {
                case BuiltInStationRules.AgrapolId: return Agrapol;
                case BuiltInStationRules.MarisId: return Maris;
                case BuiltInStationRules.BaseId: return Base;
                default: return Generic;
            }
        }
    }
}
