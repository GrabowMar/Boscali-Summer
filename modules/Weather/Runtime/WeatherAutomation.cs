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

        /// <summary>Temporary A/B toggle for dark nights (<c>enabled</c>, or null to follow the setting); does not write configuration.</summary>
        public static Dictionary<string, object> SetDarkNights(Dictionary<string, object> args)
        {
            WeatherManager manager = Find();
            if (manager == null) return Fail("SetDarkNights", "no weather manager in this scene");
            manager.SetCaptureDarkNights(Arg(args, "enabled") as bool?);
            manager.LogAutomation("SetDarkNights: " + (Arg(args, "enabled") ?? "setting"));
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
            if (!manager.SetManualOverride(conditions, cloud, rain, snapImmediate: true))
                return Fail("ForceWeather", "no valid mission clock yet; the weather has not started");
            if (args != null && (args.ContainsKey("seed") || args.ContainsKey("modelAge") || fixtureRegime.HasValue))
                manager.SetFixtureField((uint)Mathf.Clamp(Number(args, "seed", 1337f), 1f, 16000000f),
                    Mathf.Clamp(Number(args, "modelAge", 0f), 0f, 14400f), fixtureRegime);
            string followed = Follow(args);
            manager.LogAutomation($"ForceWeather: conditions {conditions:F2}, cloud {cloud:F0} m, rain {(rain.HasValue ? rain.Value.ToString("F2") : "AUTO")}, follow {followed ?? "-"}");
            return Readout(manager);
        }

        /// <summary>Sims: a held tape at a pinned place. Args: seed (1), story (255), pin (255), offsetKm (0), dynamic (false).</summary>
        public static Dictionary<string, object> SetFixtureTape(Dictionary<string, object> args)
        {
            WeatherManager manager = Find();
            if (manager == null) return Fail("SetFixtureTape", "no weather manager in this scene");
            if (!BoscaliSummer.Core.Game.GameAccess.IsServer()) return Fail("SetFixtureTape", "fixture tapes are host-only");
            bool dynamic = args != null && args.ContainsKey("dynamic") && Arg(args, "dynamic") is bool d && d;
            if (!manager.SetFixtureTape((uint)Mathf.Clamp(Number(args, "seed", 1f), 1f, 16000000f), (byte)Mathf.Clamp(Number(args, "story", 255f), 0f, 255f),
                    (byte)Mathf.Clamp(Number(args, "pin", 255f), 0f, 255f), Number(args, "offsetKm", 0f), dynamic))
                return Fail("SetFixtureTape", "needs a manual override (ForceWeather first), a captured mission and a valid clock");
            manager.LogAutomation("SetFixtureTape");
            return Readout(manager);
        }

        /// <summary>Pin the whole sky to one of the 47 library skies: <c>sky</c> is 1-47, or 0 to unpin and resume the changing
        /// weather. Host only; it goes through the same key path as the console, so clients receive the pin.</summary>
        public static Dictionary<string, object> PinSky(Dictionary<string, object> args)
        {
            WeatherManager manager = Find();
            if (manager == null) return Fail("PinSky", "no weather manager in this scene");
            if (!BoscaliSummer.Core.Game.GameAccess.IsServer()) return Fail("PinSky", "pinning a sky is host-only");
            float number = Number(args, "sky", 0f);
            if (!(number >= 0f && number <= SkyLibrary.Count)) return Fail("PinSky", "sky must be 0 (unpin) or 1.." + SkyLibrary.Count);
            int sky = Mathf.RoundToInt(number);
            if (!manager.PinSky(sky == 0 ? WeatherKey.None : (byte)(sky - 1)))
                return Fail("PinSky", "the weather has not started or has no valid mission clock yet");
            manager.LogAutomation("PinSky: " + sky);
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
            if (!manager.ApplyScenario(scenario))
                return Fail("ApplyScenario", "the weather has not started or has no valid mission clock yet");
            manager.LogAutomation("ApplyScenario: " + scenario);
            return Readout(manager);
        }

        /// <summary>Drive the Met Office (the weather console) without a mouse: <c>action</c> is <c>open</c>, <c>close</c>, <c>press</c> (with <c>control</c>, a MetControl name such as Hold, PaceUp, PinSelected, SetSupercell, AnchorAhead, RainLight, ScenReset), <c>read</c> (the input state too, with no window yet) or <c>settle</c> (tape running at the default pace, so a run starts from a known state).
        /// Host only. A press goes through the same rules and the same one-request path as a click or a key, so a disabled control is refused and reads <c>pressed</c> 0.</summary>
        public static Dictionary<string, object> Console(Dictionary<string, object> args)
        {
            WeatherManager manager = Find();
            if (manager == null) return Fail("Console", "no weather manager in this scene");
            if (!BoscaliSummer.Core.Game.GameAccess.IsServer()) return Fail("Console", "the weather console is steered by the host");
            string action = (Text(args, "action") ?? "read").ToLowerInvariant();
            MetControl? control = null;
            if (action == "press")
            {
                if (!Enum.TryParse(Text(args, "control"), true, out MetControl parsed) || !Enum.IsDefined(typeof(MetControl), parsed))
                    return Fail("Console", "press needs control: a MetControl name (Hold, PaceUp, PinSelected, SetSupercell, ...)");
                control = parsed;
            }
            else if (action != "open" && action != "close" && action != "read" && action != "settle") return Fail("Console", "action is open, close, press, read or settle");
            Dictionary<string, object> result = manager.ConsoleAct(action, control);
            result["ok"] = true;
            manager.LogAutomation("Console: " + action + (control.HasValue ? " " + control.Value : "") + " -> " + Describe(result));
            return result;
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
