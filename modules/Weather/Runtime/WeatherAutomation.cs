using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace BoscaliSummer.Features.Weather.Runtime
{
    /// <summary>Hooks for unattended in-game weather checks: nomodkit's <c>nomod sim run</c> calls them by name
    /// (<c>{"op": "call", "method": "BoscaliSummer.Features.Weather.Runtime.WeatherAutomation.ForceWeather", "args": {...}}</c>).
    /// <list type="bullet">
    /// <item>Each takes and returns a <c>Dictionary&lt;string, object&gt;</c>. Numbers may arrive as doubles; a string
    /// arg naming a scenario unit also arrives as the unit itself under <c>&lt;key&gt;Unit</c>.</item>
    /// <item>A failure returns <c>{"ok": false, "error": ...}</c> and changes nothing. Overrides are host-only and
    /// go through the same manual-override path as the debug hotkeys, so clients receive them too.</item>
    /// <item>Dev tooling: nothing here runs unless called, and every call is logged.</item>
    /// </list></summary>
    public static class WeatherAutomation
    {
        /// <summary>Snap the weather to <c>conditions</c> (default 0.92), <c>cloudHeight</c> metres (default 4500)
        /// and forced <c>rain</c> (default 1). Optionally follows the scenario unit named by <c>follow</c>.</summary>
        public static Dictionary<string, object> ForceWeather(Dictionary<string, object> args)
        {
            WeatherManager manager = Find();
            if (manager == null) return Fail("ForceWeather", "no weather manager in this scene");
            if (!BoscaliSummer.Runtime.GameAccess.IsServer()) return Fail("ForceWeather", "weather overrides are host-only");
            float conditions = Mathf.Clamp01(Number(args, "conditions", 0.92f));
            float cloud = Mathf.Clamp(Number(args, "cloudHeight", 4500f), 500f, 12000f);
            float rain = Mathf.Clamp01(Number(args, "rain", 1f));
            manager.SetManualOverride(conditions, cloud, null, rain, snapImmediate: true);
            string followed = Follow(args);
            manager.LogAutomation($"ForceWeather: conditions {conditions:F2}, cloud {cloud:F0} m, rain {rain:F2}, follow {followed ?? "-"}");
            return Readout(manager);
        }

        /// <summary>Switch the camera: <c>view</c> is <c>orbit</c> (default), <c>cockpit</c> or <c>free</c>;
        /// <c>follow</c> optionally names the scenario unit to follow first.</summary>
        public static Dictionary<string, object> View(Dictionary<string, object> args)
        {
            WeatherManager manager = Find();
            if (manager == null) return Fail("View", "no weather manager in this scene");
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null) return Fail("View", "no camera manager");
            string followed = Follow(args);
            string view = Text(args, "view") ?? "orbit";
            try
            {
                switch (view.ToLowerInvariant())
                {
                    case "cockpit": cameras.SwitchState(cameras.cockpitState); break;
                    case "free": cameras.SwitchState(cameras.freeState); break;
                    case "orbit":
                        if (cameras.followingUnit == null) return Fail("View", "orbit needs a followed unit");
                        cameras.SwitchState(cameras.orbitState);
                        break;
                    default: return Fail("View", $"no view '{view}' (orbit, cockpit, free)");
                }
            }
            catch (Exception e)
            {
                return Fail("View", $"{view}: {e.Message}");
            }
            manager.LogAutomation($"View: {view}, follow {followed ?? "-"}");
            return Readout(manager);
        }

        /// <summary>Live rain state: audio routing, canopy, atmosphere and ground wetness.</summary>
        public static Dictionary<string, object> Readout(Dictionary<string, object> args)
        {
            WeatherManager manager = Find();
            if (manager == null) return Fail("Readout", "no weather manager in this scene");
            Dictionary<string, object> state = Readout(manager);
            manager.LogAutomation("Readout: " + Describe(state));
            return state;
        }

        private static Dictionary<string, object> Readout(WeatherManager manager)
        {
            Dictionary<string, object> state = manager.DebugReadout();
            state["ok"] = true;
            return state;
        }

        private static WeatherManager Find() => WeatherManager.Live;

        private static string Follow(Dictionary<string, object> args)
        {
            if (!(Arg(args, "followUnit") is Unit unit) || unit.disabled) return null;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (cameras == null) return null;
            cameras.SetFollowingUnit(unit);
            return unit.unitName;
        }

        private static object Arg(Dictionary<string, object> args, string key) =>
            args != null && args.TryGetValue(key, out object value) ? value : null;

        private static string Text(Dictionary<string, object> args, string key) => Arg(args, key) as string;

        private static float Number(Dictionary<string, object> args, string key, float fallback)
        {
            object value = Arg(args, key);
            if (value == null) return fallback;
            try { return Convert.ToSingle(value, CultureInfo.InvariantCulture); }
            catch (Exception) { return fallback; }
        }

        private static string Describe(Dictionary<string, object> state)
        {
            var parts = new List<string>(state.Count);
            foreach (KeyValuePair<string, object> pair in state)
                parts.Add(pair.Key + "=" + Convert.ToString(pair.Value, CultureInfo.InvariantCulture));
            return string.Join(", ", parts);
        }

        private static Dictionary<string, object> Fail(string hook, string error)
        {
            Debug.LogWarning("[WeatherAutomation] " + hook + ": " + error);
            return new Dictionary<string, object> { { "ok", false }, { "error", error } };
        }
    }
}
