using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Configuration;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Presentation;
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

        /// <summary>
        /// Sims (WeatherAutomation.Console): open, close, press one control, read the Met Office or settle, without a mouse. Returns the window readout (the input state only, while no window
        /// exists yet), plus pressed (1 or 0) after a press and settled (1 or 0) after a settle. Settle puts the tape moving at the default pace through the same two edits as the buttons, so a
        /// run starts from a known state and a second run on the same private-install config does not start one pace higher (Pace and TapeMoves are config entries).
        /// </summary>
        internal Dictionary<string, object> ConsoleAct(string action, MetControl? control)
        {
            bool settled = action == "settle" && SetTapeMoves(true) && SetPace(TapePace.DefaultPace);
            if (action == "open" && (console == null || !console.IsOpen)) ToggleConsole();
            else if (action == "close" && console != null && console.IsOpen) console.Close();
            // Press first, then read: the commands apply at once, so the readout shows the state after the press.
            bool pressed = action == "press" && console != null && control.HasValue && console.IsOpen && console.Press(control.Value);
            Dictionary<string, object> readout;
            if (console == null) { readout = MetOfficeWindow.InputState(); readout["open"] = 0; }
            else readout = console.Readout();
            if (action == "press") readout["pressed"] = pressed ? 1 : 0;
            if (action == "settle") readout["settled"] = settled ? 1 : 0;
            return readout;
        }
    }
}
