using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using NuclearOption.Effects;
using UnityEngine;

namespace BoscaliSummer.Features.Visuals.Runtime
{
    /// <summary>Hooks for unattended in-game checks through nomodkit's <c>nomod sim run</c>
    /// (<c>{"op": "call", "method": "BoscaliSummer.Features.Visuals.Runtime.VisualsAutomation.Readout"}</c>).
    /// Each takes and returns a <c>Dictionary&lt;string, object&gt;</c>. Client-side only; nothing here
    /// runs unless called, and every call is logged.</summary>
    public static class VisualsAutomation
    {
        /// <summary>Volume, pipeline and foliage state.</summary>
        public static Dictionary<string, object> Readout(Dictionary<string, object> args)
        {
            var state = new Dictionary<string, object> { { "ok", true }, { "fps", 1f / Mathf.Max(0.0001f, Time.smoothDeltaTime) } };
            VisualsManager.Live?.Describe(state);
            FoliageWindService.Live?.Describe(state);
            Debug.Log("[VisualsAutomation] Readout: " + Describe(state));
            return state;
        }

        /// <summary>Flip settings by name: any of <c>grade</c>, <c>sharpen</c>, <c>g</c>,
        /// <c>foliage</c> as booleans, and <c>sway</c>, <c>sharpenStrength</c>,
        /// <c>bloom</c> as numbers.</summary>
        public static Dictionary<string, object> Set(Dictionary<string, object> args)
        {
            VisualsManager v = VisualsManager.Live;
            if (v == null) return Fail("Set", "visuals module not running");
            if (Arg(args, "grade") != null) v.CinematicPostFxEnabled = Bool(args, "grade");
            if (Arg(args, "sharpen") != null) v.SharpenEnabled = Bool(args, "sharpen");
            if (Arg(args, "g") != null) v.GForceEffectsEnabled = Bool(args, "g");
            if (Arg(args, "foliage") != null) v.FoliageDynamicsEnabled = Bool(args, "foliage");
            if (Arg(args, "sway") != null) v.FoliageSwayStrength = Number(args, "sway", 1f);
            if (Arg(args, "sharpenStrength") != null) v.SharpenStrength = Number(args, "sharpenStrength", 0.5f);
            if (Arg(args, "bloom") != null) v.BloomBoost = Number(args, "bloom", 1.35f);
            return Readout(null);
        }

        /// <summary>Pretend a G load (<c>g</c>, default -3.5) for <c>seconds</c> (default 6), so red-out
        /// and fringing can be captured without flying the manoeuvre. They still need the cockpit view.</summary>
        public static Dictionary<string, object> DebugFlight(Dictionary<string, object> args)
        {
            VisualsManager v = VisualsManager.Live;
            if (v == null) return Fail("DebugFlight", "visuals module not running");
            v.DebugG(Number(args, "g", -3.5f), Number(args, "seconds", 6f));
            return Readout(null);
        }

        /// <summary>Free camera parked near the nearest tree clump to the current camera, looking
        /// across it from <c>distance</c> metres (default 60) at <c>height</c> metres (default 12).</summary>
        public static Dictionary<string, object> LookAtTrees(Dictionary<string, object> args)
        {
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            DetailRenderer detail = SceneSingleton<DetailRenderer>.i;
            if (cameras == null || detail == null) return Fail("LookAtTrees", "no camera or detail renderer");

            TreeRenderer[] renderers = HarmonyLib.AccessTools.FieldRefAccess<DetailRenderer, TreeRenderer[]>("treeRenderers")(detail);
            if (renderers == null || renderers.Length == 0 || renderers[0].PositionData == null)
                return Fail("LookAtTrees", "no tree positions");

            byte[] bytes = renderers[0].PositionData.bytes;
            int count = bytes.Length / 12;
            GlobalPosition from = cameras.transform.position.ToGlobalPosition();
            float best = float.MaxValue;
            Vector3 pick = Vector3.zero;
            for (int i = 0; i < count; i += Math.Max(1, count / 20000))
            {
                var p = new Vector3(BitConverter.ToSingle(bytes, i * 12), BitConverter.ToSingle(bytes, i * 12 + 4),
                    BitConverter.ToSingle(bytes, i * 12 + 8));
                float d = (p.x - from.x) * (p.x - from.x) + (p.z - from.z) * (p.z - from.z);
                if (d < best) { best = d; pick = p; }
            }

            float distance = Number(args, "distance", 60f);
            float height = Number(args, "height", 12f);
            var target = new GlobalPosition(pick.x, pick.y + 8f, pick.z);
            var eye = new GlobalPosition(pick.x - distance, pick.y + height, pick.z - distance * 0.3f);
            Quaternion look = Quaternion.LookRotation(new Vector3(target.x - eye.x, target.y - eye.y, target.z - eye.z));
            cameras.SetCameraPosition(eye, look);
            Dictionary<string, object> state = Readout(null);
            state["treeX"] = pick.x;
            state["treeY"] = pick.y;
            state["treeZ"] = pick.z;
            state["treeCount"] = count;
            return state;
        }

        /// <summary>Screenshot to <c>BepInEx/visuals-shots/&lt;name&gt;.png</c>.</summary>
        public static Dictionary<string, object> Capture(Dictionary<string, object> args)
        {
            string name = (Arg(args, "name") as string) ?? "shot";
            string dir = Path.Combine(Paths.BepInExRootPath, "visuals-shots");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name + ".png");
            // ScreenCapture lives in a module the mod does not compile against; this is dev tooling.
            Type capture = Type.GetType("UnityEngine.ScreenCapture, UnityEngine.ScreenCaptureModule");
            var method = capture?.GetMethod("CaptureScreenshot", new[] { typeof(string) });
            if (method == null) return Fail("Capture", "ScreenCapture module not loaded");
            method.Invoke(null, new object[] { path });
            Debug.Log("[VisualsAutomation] Capture: " + path);
            return new Dictionary<string, object> { { "ok", true }, { "path", path } };
        }

        private static object Arg(Dictionary<string, object> args, string key) =>
            args != null && args.TryGetValue(key, out object value) ? value : null;

        private static bool Bool(Dictionary<string, object> args, string key)
        {
            object value = Arg(args, key);
            try { return Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
            catch (Exception) { return false; }
        }

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
            Debug.LogWarning("[VisualsAutomation] " + hook + ": " + error);
            return new Dictionary<string, object> { { "ok", false }, { "error", error } };
        }
    }
}
