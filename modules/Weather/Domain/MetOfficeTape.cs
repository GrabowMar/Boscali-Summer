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
    /// (AUTO is the only way to unpin).
    /// A held tape is a console hold (manual override), so fog, light and wind keep following the field exactly as the old state hold did.
    /// </summary>
    internal static class TapeSwitch
    {
        public static TapeSwitchPlan Plan(WeatherKey key, bool moves, float now, float speedMps)
        {
            WeatherKey next = key.Rebased(now, moves, speedMps);
            bool pinned = key.SkyPin < SkyLibrary.Count;
            return new TapeSwitchPlan(next, moves, moves, !moves || pinned);
        }

        /// <summary>A pace press: the same rebase at the new speed, never touching the hold state or the pin.</summary>
        public static WeatherKey Pace(WeatherKey key, float now, float speedMps) => key.Rebased(now, key.Dynamic, speedMps);

        /// <summary>Slice 4a holds D: the field is built with moving:false (D = Offset), but the capture key is Dynamic and <see cref="WeatherKey.Rebased"/> adds v * (now - Epoch)
        /// to a Dynamic key's offset, so the sky would jump on the first hold, resume, pace press, AUTO or clock reset. A manager re-key keeps the offset as it stood.
        /// Slice 5 moves the tape itself and deletes this.</summary>
        public static WeatherKey Held(WeatherKey before, WeatherKey after) => after.WithOffset(before.Offset);

        /// <summary>One step of the pace stepper, clamped to 1-6.</summary>
        public static int Step(int pace, int direction) =>
            pace + direction < TapePace.MinPace ? TapePace.MinPace : pace + direction > TapePace.MaxPace ? TapePace.MaxPace : pace + direction;
    }
}
