namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>What the manager must apply after a HOLD / RESUME press: the rebased key and the three flags that follow it.</summary>
    internal readonly struct TapeSwitchPlan
    {
        public readonly WeatherKey Key;
        public readonly bool TapeMoves, ConsoleAuto, ManualOverride;

        public TapeSwitchPlan(WeatherKey key, bool tapeMoves, bool consoleAuto, bool manualOverride)
        { Key = key; TapeMoves = tapeMoves; ConsoleAuto = consoleAuto; ManualOverride = manualOverride; }
    }

    /// <summary>
    /// HOLD / RESUME as one pure decision (spec 9.3: a SMOOTH rebase). The key is rebased so the tape position stays continuous; a pinned sky keeps its pin
    /// (AUTO is the only way to unpin); an unpinned sky keeps the class on screen as the legacy timeline's opener (INTERIM, slice 4 deletes it).
    /// A held tape is a console hold (manual override), so fog, light and wind keep following the field exactly as the old state hold did.
    /// </summary>
    internal static class TapeSwitch
    {
        public static TapeSwitchPlan Plan(WeatherKey key, bool moves, float now, float speedMps, byte shownClass)
        {
            WeatherKey next = key.Rebased(now, moves, speedMps);
            bool pinned = key.SkyPin < SkyLibrary.Count;
            if (!pinned) next = next.WithMissionClass(shownClass);
            return new TapeSwitchPlan(next, moves, moves, !moves || pinned);
        }

        /// <summary>A pace press: the same rebase at the new speed, never touching the hold state or the pin; an unpinned sky keeps the class on screen as its legacy opener.</summary>
        public static WeatherKey Pace(WeatherKey key, float now, float speedMps, byte shownClass)
        {
            WeatherKey next = key.Rebased(now, key.Dynamic, speedMps);
            return key.SkyPin < SkyLibrary.Count ? next : next.WithMissionClass(shownClass);
        }

        /// <summary>One step of the pace stepper, clamped to 1-6.</summary>
        public static int Step(int pace, int direction) =>
            pace + direction < TapePace.MinPace ? TapePace.MinPace : pace + direction > TapePace.MaxPace ? TapePace.MaxPace : pace + direction;
    }
}
