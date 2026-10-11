using BoscaliSummer.Modules.Weather.Configuration;
using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Weather.Runtime
{
    /// <summary>
    /// What the Met Office window needs from the manager and cannot read from the public surface: the live key, the settings, the theater span and the two console edits that
    /// are new (the TapeMoves switch and Pace). Everything else the window does goes through the existing console API of WeatherManager.cs.
    /// </summary>
    internal sealed partial class WeatherManager
    {
        internal WeatherKey LiveKey => fieldKey;
        internal WeatherSettings Settings => settings;
        internal float BaselineWind => baselineWindSpeed;

        /// <summary>Half of the theater, metres (x east, z north).</summary>
        internal Vector2 TheaterHalfSpan => fieldSpan * 0.5f;

        internal bool TryMissionSeconds(out float seconds) => TryMissionTime(out seconds);

        /// <summary>
        /// HOLD / RESUME: the same switch as SET SERVER > EFFECTS > CHANGING WEATHER (<c>TapeMoves</c>), written from the console. One key edit, a SMOOTH rebase
        /// (<see cref="TapeSwitch"/>): the tape position never jumps, a pinned sky keeps its pin.
        /// </summary>
        internal bool SetTapeMoves(bool moves)
        {
            if (!CanCommand || fieldKey == null || !TryMissionTime(out float now)) return false;
            TapeSwitchPlan plan = TapeSwitch.Plan(fieldKey, moves, now, TapePace.Speed(settings.Pace.Value, baselineWindSpeed), ShownClass());
            settings.TapeMoves.Value = plan.TapeMoves;
            consoleAuto = plan.ConsoleAuto;
            isManualOverride = plan.ManualOverride;
            explicitTargets = false;
            fieldKey = plan.Key;
            RekeyAndSync(now);
            NotifyPlayer(moves ? "TAPE RUNNING" : "TAPE HELD", forcedRainIntensity);
            return true;
        }

        /// <summary>
        /// PACE: writes the same <c>Pace</c> entry as the SET SERVER row and rebases the key to the speed it gives (spec 5.4). INTERIM(slice 4): the legacy 5-minute step ignores the speed,
        /// and a rebase restarts that step counter, so an unpinned sky keeps the class on screen as its opener (<see cref="TapeSwitch.Pace"/>).
        /// </summary>
        internal bool SetPace(int pace)
        {
            if (!CanCommand || fieldKey == null || !TryMissionTime(out float now)) return false;
            pace = TapeSwitch.Step(pace, 0);
            settings.Pace.Value = pace;
            fieldKey = TapeSwitch.Pace(fieldKey, now, TapePace.Speed(pace, baselineWindSpeed), ShownClass());
            RekeyAndSync(now);
            NotifyPlayer("PACE " + pace, forcedRainIntensity);
            return true;
        }

    }
}
