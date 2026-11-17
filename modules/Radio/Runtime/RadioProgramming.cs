using System;

namespace BoscaliSummer.Modules.Radio.Runtime
{
    internal enum RadioDaypart { Morning, Day, Evening, Night }

    /// <summary>Local display copy only. Actual station items come from broadcast.tsv.</summary>
    internal static class RadioProgramming
    {
        private static readonly string[] Republic = { "Republic Dawn", "Open Assembly", "Harbor Voices", "The Night Commons" };
        private static readonly string[] State = { "First Order", "The State Hour", "Evening Directive", "Night Vigil" };
        private static readonly string[] Mercenary = { "First Contract", "Open Frequencies", "Shift Change", "The Long Watch" };
        private static readonly string[] Local = { "Morning Rotation", "Day Rotation", "Evening Rotation", "Night Rotation" };

        public static RadioDaypart DaypartAt(DateTime when)
        {
            int hour = when.Hour;
            if (hour >= 5 && hour < 11) return RadioDaypart.Morning;
            if (hour >= 11 && hour < 17) return RadioDaypart.Day;
            if (hour >= 17 && hour < 22) return RadioDaypart.Evening;
            return RadioDaypart.Night;
        }
        public static string ProgramName(string stationId, DateTime when)
        {
            string[] shows = stationId == BuiltInStationRules.AgrapolId ? Republic
                : stationId == BuiltInStationRules.MarisId ? State
                : stationId == BuiltInStationRules.BaseId ? Mercenary : Local;
            return shows[(int)DaypartAt(when)];
        }
    }
}
