using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;
using UnityEngine;
using static BoscaliSummer.Core.Diagnostics.AutomationArgs;

namespace BoscaliSummer.Modules.Weather.Runtime
{
    /// <summary>Hooks for unattended in-game weather checks: nomodkit's <c>nomod sim run</c> calls them by name
    /// (<c>{"op": "call", "method": "BoscaliSummer.Modules.Weather.Runtime.WeatherAutomation.ForceWeather", "args": {...}}</c>).
    /// <list type="bullet">
    /// <item>Each takes and returns a <c>Dictionary&lt;string, object&gt;</c>. Numbers may arrive as doubles; a string
    /// arg naming a scenario unit also arrives as the unit itself under <c>&lt;key&gt;Unit</c>.</item>
    /// <item>A failure returns <c>{"ok": false, "error": ...}</c> and changes nothing. Overrides are host-only and
    /// go through the same manual-override path as the debug hotkeys, so clients receive them too.</item>
    /// <item>Dev tooling: nothing here runs unless called, and every call is logged.</item>
    /// </list></summary>
    public static class WeatherAutomation
    {
        private static bool captureProfile;
        private static int originalWidth, originalHeight;
        private static FullScreenMode originalScreenMode;

        /// <summary>Bounded visual-test profile. RestoreCapture returns the previous display mode.</summary>
        public static Dictionary<string, object> PrepareCapture(Dictionary<string, object> args)
        {
            if (Application.isBatchMode) return Fail("PrepareCapture", "requires a rendered game window");
            if (!captureProfile)
            {
                originalWidth = Screen.width;
                originalHeight = Screen.height;
                originalScreenMode = Screen.fullScreenMode;
                captureProfile = true;
            }
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            return new Dictionary<string, object> { { "ok", true }, { "requestedWidth", 1920 },
                { "requestedHeight", 1080 }, { "restoreWidth", originalWidth }, { "restoreHeight", originalHeight } };
        }

        public static Dictionary<string, object> RestoreCapture(Dictionary<string, object> args)
        {
            RestoreCaptureProfile();
            return new Dictionary<string, object> { { "ok", true } };
        }

        internal static void RestoreCaptureProfile()
        {
            Find()?.SetCaptureRainVisuals(null);
            if (captureProfile)
                Screen.SetResolution(originalWidth, originalHeight, originalScreenMode);
            captureProfile = false;
        }

        /// <summary>Temporary visual-test toggle; does not write configuration and clears with the capture/scene.</summary>
        public static Dictionary<string, object> SetRainVisuals(Dictionary<string, object> args)
        {
            WeatherManager manager = Find();
            if (manager == null) return Fail("SetRainVisuals", "no weather manager in this scene");
            if (!(Arg(args, "enabled") is bool enabled)) return Fail("SetRainVisuals", "enabled must be a boolean");
            manager.SetCaptureRainVisuals(enabled);
            manager.LogAutomation("SetRainVisuals: " + enabled);
            return Readout(manager);
        }

        /// <summary>Snap the weather to <c>conditions</c> (default 0.92), <c>cloudHeight</c> metres (default 4500)
        /// and optional forced <c>rain</c>. Without rain, the model controls precipitation.
        /// Optionally follows the scenario unit named by <c>follow</c>.</summary>
        public static Dictionary<string, object> ForceWeather(Dictionary<string, object> args)
        {
            WeatherManager manager = Find();
            if (manager == null) return Fail("ForceWeather", "no weather manager in this scene");
            if (!BoscaliSummer.Core.Game.GameAccess.IsServer()) return Fail("ForceWeather", "weather overrides are host-only");
            WeatherRegimeType? fixtureRegime = null;
            if (Has(args, "regime"))
            {
                if (!Enum.TryParse(Text(args, "regime"), true, out WeatherRegimeType parsed) || !Enum.IsDefined(typeof(WeatherRegimeType), parsed))
                    return Fail("ForceWeather", "unknown weather state (Clear, Fair, Scattered, Broken, Overcast, RainSquall, Storm)");
                fixtureRegime = parsed;
            }
            float conditions = Mathf.Clamp01(Number(args, "conditions", 0.92f));
            float cloud = Mathf.Clamp(Number(args, "cloudHeight", 4500f), 500f, 12000f);
            float? rain = args != null && args.ContainsKey("rain")
                ? Mathf.Clamp01(Number(args, "rain", 1f)) : (float?)null;
            manager.SetManualOverride(conditions, cloud, null, rain, snapImmediate: true);
            if (args != null && (args.ContainsKey("seed") || args.ContainsKey("modelAge") || fixtureRegime.HasValue))
                manager.SetFixtureField((uint)Mathf.Clamp(Number(args, "seed", 1337f), 1f, 16000000f),
                    Mathf.Clamp(Number(args, "modelAge", 0f), 0f, 14400f), fixtureRegime);
            string followed = Follow(args);
            manager.LogAutomation($"ForceWeather: conditions {conditions:F2}, cloud {cloud:F0} m, rain {(rain.HasValue ? rain.Value.ToString("F2") : "AUTO")}, follow {followed ?? "-"}");
            return Readout(manager);
        }

        /// <summary>Apply an existing console preset through its native host-authoritative path.</summary>
        public static Dictionary<string, object> ApplyScenario(Dictionary<string, object> args)
        {
            WeatherManager manager = Find();
            if (manager == null) return Fail("ApplyScenario", "no weather manager in this scene");
            if (!BoscaliSummer.Core.Game.GameAccess.IsServer()) return Fail("ApplyScenario", "weather presets are host-only");
            if (!Enum.TryParse(Text(args, "scenario"), true, out WeatherScenario scenario) ||
                !Enum.IsDefined(typeof(WeatherScenario), scenario))
                return Fail("ApplyScenario", "unknown console weather preset");
            manager.ApplyScenario(scenario);
            manager.LogAutomation("ApplyScenario: " + scenario);
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
            if (Bool(args, "requireClouds") &&
                (Convert.ToInt32(state["flightClouds"]) != 1 || Convert.ToInt32(state["nativeCloudsHidden"]) != 1))
                throw new InvalidOperationException("Weather capture requested before cloud renderer/native takeover was ready.");
            manager.LogAutomation("Readout: " + Describe(state));
            return state;
        }

        /// <summary>Read-only bounded search for an interior in the currently displayed cloud maps.</summary>
        public static Dictionary<string, object> CloudProbe(Dictionary<string, object> args)
        {
            WeatherManager manager = Find();
            if (manager == null) return Fail("CloudProbe", "no weather manager in this scene");
            float x = Number(args, "x", 0f), z = Number(args, "z", 0f), radius = Number(args, "radius", 8000f);
            float minRain = Number(args, "minRain", 0f);
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z) ||
                float.IsNaN(radius) || float.IsInfinity(radius) || float.IsNaN(minRain) || float.IsInfinity(minRain))
                return Fail("CloudProbe", "coordinates and rain threshold must be finite");
            Dictionary<string, object> result = manager.ProbeCloud(x, z, radius, Mathf.Clamp(minRain, 0f, 100f));
            manager.LogAutomation("CloudProbe: " + Describe(result));
            return result;
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

        private static Dictionary<string, object> Fail(string hook, string error) => Failure("WeatherAutomation", hook, error);
    }
}
