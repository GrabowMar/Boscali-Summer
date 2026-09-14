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
        public const int MaximumBulletins = 8;

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
                "Requests are handled at the source."
            }
        };

        private static readonly StationVoice Agrapol = new StationVoice
        {
            Shows = new[] { "Sunrise Drive", "Midday Rotation", "Evening Request Line", "Nightwatch Slow Set" },
            Bulletins = new[]
            {
                "Coastal weather holds clear through the night.",
                "Request lines open after the top of the hour.",
                "Two more nights of the Agrapol airshow. Tickets at the gate.",
                "Traffic on the ring road is moving again after the convoy.",
                "Tonight's slow set is dedicated to the night shift.",
                "The midnight countdown returns this weekend."
            }
        };

        private static readonly StationVoice Maris = new StationVoice
        {
            Shows = new[] { "Morning Brief", "World Service", "The Six O'Clock Report", "Overnight Wire" },
            Bulletins = new[]
            {
                "Shipping lanes report normal traffic through the strait.",
                "Currency markets closed steady after a quiet session.",
                "Rail service to the northern depots resumes on schedule.",
                "Forecast: scattered cloud, visibility good for flight ops.",
                "Next bulletin on the hour; world service continues.",
                "Local councils meet tomorrow over the harbor expansion.",
                "Rescue services stood down after the night's search."
            }
        };

        private static readonly StationVoice Base = new StationVoice
        {
            Shows = new[] { "Reveille and Duty Roster", "Wing Ops Bulletin", "Stand-down Report", "Night Watch" },
            Bulletins = new[]
            {
                "Range control: live fire to the east until 0400.",
                "Duty roster for the next watch is posted at the ready room.",
                "All flights check in on the tower channel before taxi.",
                "Weather advisory: crosswinds on the main runway.",
                "Commissary restock arrives with the morning transport.",
                "Reminder: blackout restrictions remain in effect.",
                "Ground crews report the flight line clear for the night."
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
