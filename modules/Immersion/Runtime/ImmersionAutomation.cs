using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace BoscaliSummer.Features.Immersion.Runtime
{
    /// <summary>Hooks for unattended in-game checks through nomodkit's <c>nomod sim run</c>
    /// (<c>{"op": "call", "method": "BoscaliSummer.Features.Immersion.Runtime.ImmersionAutomation.Readout"}</c>).
    /// Each takes and returns a <c>Dictionary&lt;string, object&gt;</c>. Dev tooling: nothing here runs
    /// unless called, and every call is logged.</summary>
    public static class ImmersionAutomation
    {
        /// <summary>Head angles, measured force, shake counters and glare level.</summary>
        public static Dictionary<string, object> Readout(Dictionary<string, object> args)
        {
            ImmersionManager manager = ImmersionManager.Live;
            if (manager == null) return Fail("Readout", "immersion module not running");
            var state = new Dictionary<string, object> { { "ok", true } };
            manager.Describe(state);
            Debug.Log("[ImmersionAutomation] Readout: " + Describe(state));
            return state;
        }

        /// <summary>Pretend the pilot feels specific force (<c>x</c>, <c>y</c>, <c>z</c> in G; default
        /// 0, 6, 0) for <c>seconds</c> (default 5), so head movement can be captured without flying it.</summary>
        public static Dictionary<string, object> DebugForce(Dictionary<string, object> args)
        {
            ImmersionManager manager = ImmersionManager.Live;
            if (manager == null) return Fail("DebugForce", "immersion module not running");
            manager.Head.DebugForce(new Vector3(Number(args, "x", 0f), Number(args, "y", 6f), Number(args, "z", 0f)),
                Number(args, "seconds", 5f));
            return Readout(null);
        }

        /// <summary>Flip settings by name: <c>head</c>, <c>shake</c>, <c>glare</c> (booleans).</summary>
        public static Dictionary<string, object> Set(Dictionary<string, object> args)
        {
            ImmersionManager m = ImmersionManager.Live;
            if (m == null) return Fail("Set", "immersion module not running");
            if (Has(args, "head")) m.HeadMotionEnabled = Bool(args, "head");
            if (Has(args, "shake")) m.ExtraShakeEnabled = Bool(args, "shake");
            if (Has(args, "glare")) m.SunGlareEnabled = Bool(args, "glare");
            return Readout(null);
        }

        private static readonly List<Canvas> hiddenCanvases = new List<Canvas>();

        /// <summary><c>hide</c> true hides every screen-space overlay canvas (menus, HUD) so a capture
        /// shows only the world; false restores exactly those.</summary>
        public static Dictionary<string, object> HideUi(Dictionary<string, object> args)
        {
            bool hide = !Has(args, "hide") || Bool(args, "hide");
            if (hide)
            {
                foreach (Canvas canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
                {
                    if (!canvas.enabled || !canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                    canvas.enabled = false;
                    hiddenCanvases.Add(canvas);
                }
            }
            else
            {
                foreach (Canvas canvas in hiddenCanvases)
                    if (canvas != null) canvas.enabled = true;
                hiddenCanvases.Clear();
            }
            return new Dictionary<string, object> { { "ok", true }, { "hidden", hiddenCanvases.Count } };
        }

        /// <summary>Scale the sun flare by <c>gain</c> (default 1).</summary>
        public static Dictionary<string, object> GlareMode(Dictionary<string, object> args)
        {
            ImmersionManager m = ImmersionManager.Live;
            if (m == null) return Fail("GlareMode", "immersion module not running");
            if (Has(args, "gain")) m.Glare.Gain = Number(args, "gain", 1f);
            return Readout(null);
        }

        /// <summary>Host only: set the time of day to <c>hour</c> (0-24).</summary>
        public static Dictionary<string, object> SetTime(Dictionary<string, object> args)
        {
            if (!BoscaliSummer.Runtime.GameAccess.IsServer()) return Fail("SetTime", "host only");
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (level == null) return Fail("SetTime", "no level");
            level.SetTimeOfDay(Mathf.Repeat(Number(args, "hour", 12f), 24f));
            return Readout(null);
        }

        /// <summary>Free camera at the current camera position, looking <c>offset</c> degrees
        /// (default 12) to the side of the sun so the flare's ghosts are on screen.</summary>
        public static Dictionary<string, object> LookAtSun(Dictionary<string, object> args)
        {
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (cameras == null || level == null || level.sun == null) return Fail("LookAtSun", "no camera or sun");
            Vector3 toSun = -level.sun.transform.forward;
            Quaternion look = Quaternion.AngleAxis(Number(args, "offset", 12f), Vector3.up) * Quaternion.LookRotation(toSun);
            cameras.GetCameraPosition(out GlobalPosition position, out _);
            cameras.SetCameraPosition(position, look);
            Dictionary<string, object> state = Readout(null);
            state["sunElevation"] = Mathf.Asin(Mathf.Clamp(toSun.y, -1f, 1f)) * Mathf.Rad2Deg;
            return state;
        }

        private static bool Has(Dictionary<string, object> args, string key) => args != null && args.ContainsKey(key);

        private static bool Bool(Dictionary<string, object> args, string key)
        {
            try { return Convert.ToBoolean(args[key], CultureInfo.InvariantCulture); }
            catch (Exception) { return false; }
        }

        private static float Number(Dictionary<string, object> args, string key, float fallback)
        {
            if (args == null || !args.TryGetValue(key, out object value) || value == null) return fallback;
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
            Debug.LogWarning("[ImmersionAutomation] " + hook + ": " + error);
            return new Dictionary<string, object> { { "ok", false }, { "error", error } };
        }
    }
}
